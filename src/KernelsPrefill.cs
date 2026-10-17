// Batched prompt processing ("prefill"): a chunk of T prompt tokens goes through each layer at once instead of one
// token per pass. Activations are row-major [token][feature]. C# 5 only.
//   MatMulQ4Q8Tile  Q4_0 weights x Q8_0-quantized activations (the decode path's numerics): 64 rows x 24 tokens per
//                   tile, 16-byte shared loads, fused integer dot-accumulate (~7.4 TMAC/s on an RX 6700 XT); MatMulQ4Q8 is
//                   the general fallback for rows whose block count is not a multiple of 8
//   KvStoreRows  writes T positions into the packed-fp16 ring caches
//   Attention    causal attention for T query tokens (sliding window or full), streaming softmax over key chunks
//   CopyRange    copies a range between buffers (the last token's hidden state into the decode buffer)
using System;

namespace Phoenix
{
    public static class KernelsPrefill
    {
        public const uint MmThreads = 64, MmTok = 16, MmKb = 8;   // 64 threads, 16 tokens, 8 blocks (256 values) per step

        // Rows covered by one workgroup (rpt rows per thread).
        public static uint MmRows(uint rpt) { return MmThreads * rpt; }

        // Y[t][r] = sum_b dW[r,b] dX[t,b] sum_j (q_rbj - 8) x_tbj — like KernelsGemma.MatVecQ4_0 per token.
        // Weights: nibble words (4 per block) + fp16 scale pairs; activations: Q8_0 words (8 per block) + f32 scales,
        // row-major per token, staged in shared memory 16 tokens x 8 blocks at a time. Each thread computes rpt rows
        // (lid, lid + 64, ...) for the 16 tokens, so every activation word loaded from shared memory feeds rpt dot
        // products. Grid (ceil(rows / (64 rpt)), ceil(T / 16)).
        // bindings: 0 wq u32, 1 wd u32, 2 xq u32 [T * nb * 8], 3 xd f32 [T * nb], 4 y f32 [T * rows].
        // push: rows, nb, T, stride (blocks per stored weight row)
        public static uint[] MatMulQ4Q8(bool dot) { return MatMulQ4Q8(dot, 4); }

        public static uint[] MatMulQ4Q8(bool dot, uint rpt)
        {
            KB k = new KB(MmThreads);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.U32, true), xq = k.Buffer(2, k.U32, true),
                xd = k.Buffer(3, k.F32, true), y = k.Buffer(4, k.F32, false);
            k.Push(4);
            KB.Shared xs = k.SharedArray(k.U32, MmTok * MmKb * 8), xds = k.SharedArray(k.F32, MmTok * MmKb);
            k.Begin();
            uint b0v = k.Local(k.U32), i = k.Local(k.U32), bbv = k.Local(k.U32);
            uint[,] acc = new uint[rpt, MmTok];
            for (int a = 0; a < rpt; a++) for (int t = 0; t < MmTok; t++) acc[a, t] = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), T = k.PushU(2), stride = k.PushU(3);
            uint lid = k.LocalId();
            uint rBase = k.IAdd(k.IMul(k.GroupX(), k.C(MmThreads * rpt)), lid);
            uint tok0 = k.IMul(k.GroupY(), k.C(MmTok));
            uint[] rowOk = new uint[rpt], rowBase = new uint[rpt], rr = new uint[rpt];
            for (uint a = 0; a < rpt; a++)
            {
                rr[a] = k.IAdd(rBase, k.C(a * MmThreads));
                rowOk[a] = k.ULt(rr[a], rows);
                rowBase[a] = k.IMul(k.Select(k.U32, rowOk[a], rr[a], k.C(0)), stride);   // out-of-range rows read row 0, never stored
            }
            for (int a = 0; a < rpt; a++) for (int t = 0; t < MmTok; t++) k.Set(acc[a, t], k.CF(0));
            k.For(b0v, k.C(0), nb, k.C(MmKb), delegate
            {
                uint b0 = k.Get(b0v, k.U32);
                k.For(i, lid, k.C(MmTok * MmKb * 8), k.C(MmThreads), delegate
                {
                    uint idx = k.Get(i, k.U32);
                    uint tt = k.UDiv(idx, k.C(MmKb * 8)), rem = k.UMod(idx, k.C(MmKb * 8));
                    uint blk = k.IAdd(b0, k.UDiv(rem, k.C(8))), tok = k.IAdd(tok0, tt);
                    uint ok = k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(tok, T), k.ULt(blk, nb));
                    uint src = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, tok, k.C(0)), nb), k.Select(k.U32, ok, blk, k.C(0))), k.C(8)), k.UMod(rem, k.C(8)));
                    k.St(xs, idx, k.Select(k.U32, ok, k.Ld(xq, src), k.C(0)));
                });
                k.For(i, lid, k.C(MmTok * MmKb), k.C(MmThreads), delegate
                {
                    uint idx = k.Get(i, k.U32);
                    uint tt = k.UDiv(idx, k.C(MmKb)), blk = k.IAdd(b0, k.UMod(idx, k.C(MmKb))), tok = k.IAdd(tok0, tt);
                    uint ok = k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(tok, T), k.ULt(blk, nb));
                    uint src = k.IAdd(k.IMul(k.Select(k.U32, ok, tok, k.C(0)), nb), k.Select(k.U32, ok, blk, k.C(0)));
                    k.St(xds, idx, k.Select(k.F32, ok, k.Ld(xd, src), k.CF(0)));
                });
                k.Barrier();
                uint kbEnd = k.S.Glsl(k.U32, Spv.GlslUMin, k.C(MmKb), k.S.Value(Op.ISub, k.U32, nb, b0));
                k.For(bbv, k.C(0), kbEnd, k.C(1), delegate
                {
                    uint bb = k.Get(bbv, k.U32);
                    // this block's weights for each of the thread's rows, as signed bytes
                    uint[] dw = new uint[rpt];
                    uint[,] slo = new uint[rpt, 4], shi = new uint[rpt, 4];
                    for (uint a = 0; a < rpt; a++)
                    {
                        uint blk = k.IAdd(rowBase[a], k.IAdd(b0, bb));
                        dw[a] = KernelsAttention.HalfAt(k, wd, blk);
                        for (uint w = 0; w < 4; w++)
                        {
                            uint ww = k.Ld(wq, k.IAdd(k.IMul(blk, k.C(4)), k.C(w)));
                            uint lo = k.And(ww, k.C(0x0F0F0F0F)), hi = k.And(k.Shr(ww, k.C(4)), k.C(0x0F0F0F0F));
                            if (dot)
                            {
                                slo[a, w] = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(lo, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                                shi[a, w] = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(hi, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                            }
                            else { slo[a, w] = lo; shi[a, w] = hi; }
                        }
                    }
                    for (int t = 0; t < MmTok; t++)
                    {
                        uint xb = k.IAdd(k.C((uint)(t * MmKb * 8)), k.IMul(bb, k.C(8)));
                        uint[] xw = new uint[8];
                        for (uint w = 0; w < 8; w++) xw[w] = k.Ld(xs, k.IAdd(xb, k.C(w)));
                        uint dx = k.Ld(xds, k.IAdd(k.C((uint)(t * MmKb)), bb));
                        for (uint a = 0; a < rpt; a++)
                        {
                            uint isum = k.CI(0);
                            for (uint w = 0; w < 4; w++)
                            {
                                if (dot) isum = k.Dot4Acc(shi[a, w], xw[w + 4], k.Dot4Acc(slo[a, w], xw[w], isum));
                                else
                                    for (int kk = 0; kk < 4; kk++)
                                    {
                                        uint ql = k.ISubS(k.AsI(k.And(k.Shr(slo[a, w], k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                                        uint qh = k.ISubS(k.AsI(k.And(k.Shr(shi[a, w], k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                                        isum = k.IAddS(isum, k.IAddS(k.IMulS(ql, k.SByte(xw[w], kk)), k.IMulS(qh, k.SByte(xw[w + 4], kk))));
                                    }
                            }
                            k.Set(acc[a, t], k.FAdd(k.Get(acc[a, t], k.F32), k.FMul(k.SToF(isum), k.FMul(dw[a], dx))));
                        }
                    }
                });
                k.Barrier();
            });
            for (uint a = 0; a < rpt; a++)
            {
                uint aa = a;
                k.If(rowOk[a], delegate
                {
                    for (int t = 0; t < MmTok; t++)
                    {
                        uint tok = k.IAdd(tok0, k.C((uint)t));
                        int tt = t;
                        k.If(k.ULt(tok, T), delegate { k.St(y, k.IAdd(k.IMul(tok, rows), rr[aa]), k.Get(acc[aa, tt], k.F32)); });
                    }
                });
            }
            return k.Finish();
        }

        // Fast path for rows whose block count is a multiple of 8 (all Gemma 4 matrices): the 8-block step is unrolled at
        // build time; each thread keeps rpt rows (lid, lid + 64, ...) x tok tokens of accumulators, so every activation
        // word read from shared memory feeds rpt dot products; integer dot products accumulate in one instruction
        // (OpSDotAccSat). Same math as MatMulQ4Q8. Grid (ceil(rows / (64 rpt)), ceil(T / tok)). Bindings/push as MatMulQ4Q8.

        public static uint[] MatMulQ4Q8Tile(bool dot, uint rpt, uint tok) { return MatMulQ4Q8Tile(dot, rpt, tok, false); }
        // (push: rows, nb, T, stride — weight rows may be padded so they do not all start on the same memory channel)

        // vec4: the activation tile is declared as uvec4 in shared memory (stored and read 16 bytes at a time).
        public static uint[] MatMulQ4Q8Tile(bool dot, uint rpt, uint tok, bool vec4)
        {
            KB k = new KB(MmThreads);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.U32, true), xq = k.Buffer(2, k.U32, true),
                xd = k.Buffer(3, k.F32, true), y = k.Buffer(4, k.F32, false);
            k.Push(4);
            uint v4 = k.S.TypeVector(k.U32, 4);
            KB.Shared xs = vec4 ? null : k.SharedArray(k.U32, tok * MmKb * 8), xds = k.SharedArray(k.F32, tok * MmKb);
            KB.Shared xs4 = vec4 ? k.SharedArray(v4, tok * MmKb * 2) : null;
            k.Begin();
            uint b0v = k.Local(k.U32), i = k.Local(k.U32);
            uint[,] acc = new uint[rpt, tok];
            for (int a = 0; a < rpt; a++) for (int t = 0; t < tok; t++) acc[a, t] = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), T = k.PushU(2), stride = k.PushU(3);   // stride: blocks per stored weight row (>= nb)
            uint lid = k.LocalId();
            uint rFirst = k.IAdd(k.IMul(k.GroupX(), k.C(MmThreads * rpt)), lid);
            uint tok0 = k.IMul(k.GroupY(), k.C(tok));
            uint[] r = new uint[rpt], rowOk = new uint[rpt], rowBase = new uint[rpt];
            for (uint a = 0; a < rpt; a++)
            {
                r[a] = k.IAdd(rFirst, k.C(a * MmThreads));
                rowOk[a] = k.ULt(r[a], rows);
                rowBase[a] = k.IMul(k.Select(k.U32, rowOk[a], r[a], k.C(0)), stride);   // out-of-range rows read row 0, never stored
            }
            for (int a = 0; a < rpt; a++) for (int t = 0; t < tok; t++) k.Set(acc[a, t], k.CF(0));
            k.For(b0v, k.C(0), nb, k.C(MmKb), delegate
            {
                uint b0 = k.Get(b0v, k.U32);
                if (xs4 == null)
                {
                    k.For(i, lid, k.C(tok * MmKb * 8), k.C(MmThreads), delegate
                    {
                        uint idx = k.Get(i, k.U32);
                        uint tt = k.Shr(idx, k.C(6)), rem = k.And(idx, k.C(63));   // MmKb * 8 = 64 words per token
                        uint tk = k.IAdd(tok0, tt);
                        uint ok = k.ULt(tk, T);
                        uint src = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, tk, k.C(0)), nb), b0), k.C(8)), rem);
                        k.St(xs, idx, k.Select(k.U32, ok, k.Ld(xq, src), k.C(0)));
                    });
                }
                else
                {
                    // 16 uvec4 per token, built from four word loads each
                    k.For(i, lid, k.C(tok * MmKb * 2), k.C(MmThreads), delegate
                    {
                        uint idx = k.Get(i, k.U32);
                        uint tt = k.Shr(idx, k.C(4)), rem = k.And(idx, k.C(15));
                        uint tk = k.IAdd(tok0, tt);
                        uint ok = k.ULt(tk, T);
                        uint src = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, tk, k.C(0)), nb), b0), k.C(8)), k.IMul(rem, k.C(4)));
                        uint[] c = new uint[4];
                        for (uint e = 0; e < 4; e++) c[e] = k.Select(k.U32, ok, k.Ld(xq, k.IAdd(src, k.C(e))), k.C(0));
                        k.St(xs4, idx, k.S.Value(Op.CompositeConstruct, v4, c[0], c[1], c[2], c[3]));
                    });
                }
                k.For(i, lid, k.C(tok * MmKb), k.C(MmThreads), delegate
                {
                    uint idx = k.Get(i, k.U32);
                    uint tt = k.Shr(idx, k.C(3)), tk = k.IAdd(tok0, tt);
                    uint ok = k.ULt(tk, T);
                    uint src = k.IAdd(k.IMul(k.Select(k.U32, ok, tk, k.C(0)), nb), k.IAdd(b0, k.And(idx, k.C(7))));
                    k.St(xds, idx, k.Select(k.F32, ok, k.Ld(xd, src), k.CF(0)));
                });
                k.Barrier();
                for (uint bb = 0; bb < MmKb; bb++)
                {
                    uint[] dw = new uint[rpt];
                    uint[,] slo = new uint[rpt, 4], shi = new uint[rpt, 4];
                    for (uint a = 0; a < rpt; a++)
                    {
                        uint blk = k.IAdd(rowBase[a], k.IAdd(b0, k.C(bb)));
                        dw[a] = KernelsAttention.HalfAt(k, wd, blk);
                        for (uint w = 0; w < 4; w++)
                        {
                            uint ww = k.Ld(wq, k.IAdd(k.IMul(blk, k.C(4)), k.C(w)));
                            uint lo = k.And(ww, k.C(0x0F0F0F0F)), hi = k.And(k.Shr(ww, k.C(4)), k.C(0x0F0F0F0F));
                            if (dot)
                            {
                                slo[a, w] = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(lo, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                                shi[a, w] = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(hi, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                            }
                            else { slo[a, w] = lo; shi[a, w] = hi; }
                        }
                    }
                    for (uint t = 0; t < tok; t++)
                    {
                        uint xb = t * MmKb * 8 + bb * 8;
                        uint[] xw = new uint[8];
                        if (xs4 != null)
                        {
                            // two 16-byte shared loads instead of eight 4-byte ones
                            uint lo4 = k.Ld(xs4, k.C(xb / 4)), hi4 = k.Ld(xs4, k.C(xb / 4 + 1));
                            for (uint w = 0; w < 4; w++) { xw[w] = k.Comp(lo4, w); xw[w + 4] = k.Comp(hi4, w); }
                        }
                        else for (uint w = 0; w < 8; w++) xw[w] = k.Ld(xs, k.C(xb + w));
                        uint dx = k.Ld(xds, k.C(t * MmKb + bb));
                        for (uint a = 0; a < rpt; a++)
                        {
                            uint isum = k.CI(0);
                            for (uint w = 0; w < 4; w++)
                            {
                                if (dot) isum = k.Dot4Acc(shi[a, w], xw[w + 4], k.Dot4Acc(slo[a, w], xw[w], isum));
                                else
                                    for (int kk = 0; kk < 4; kk++)
                                    {
                                        uint ql = k.ISubS(k.AsI(k.And(k.Shr(slo[a, w], k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                                        uint qh = k.ISubS(k.AsI(k.And(k.Shr(shi[a, w], k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                                        isum = k.IAddS(isum, k.IAddS(k.IMulS(ql, k.SByte(xw[w], kk)), k.IMulS(qh, k.SByte(xw[w + 4], kk))));
                                    }
                            }
                            k.Set(acc[a, t], k.FAdd(k.Get(acc[a, t], k.F32), k.FMul(k.SToF(isum), k.FMul(dw[a], dx))));
                        }
                    }
                }
                k.Barrier();
            });
            for (uint a = 0; a < rpt; a++)
            {
                uint aa = a;
                k.If(rowOk[a], delegate
                {
                    for (uint t = 0; t < tok; t++)
                    {
                        uint tk = k.IAdd(tok0, k.C(t));
                        uint tt = t;
                        k.If(k.ULt(tk, T), delegate { k.St(y, k.IAdd(k.IMul(tk, rows), r[aa]), k.Get(acc[aa, tt], k.F32)); });
                    }
                });
            }
            return k.Finish();
        }

        // KV cache writes for T positions p0 .. p0+T-1 (packed fp16, ring slots), one thread per (token, element pair).
        // Two neighbouring slots share a word of the transposed K cache, so the call is made twice, parity 0 then 1
        // (with a barrier between): each pass writes only tokens whose slot has that parity, and no two of those share
        // a word. bindings: 0 k [T * kvDim], 1 v, 2 kc u32, 3 vc u32. push: kvDim, ring, p0, T, parity
        public static uint[] KvStoreRows()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf kk = k.Buffer(0, k.F32, true), v = k.Buffer(1, k.F32, true), kc = k.Buffer(2, k.U32, false), vc = k.Buffer(3, k.U32, false);
            k.Push(5);
            k.Begin();
            uint g = k.GlobalId();
            uint kvDim = k.PushU(0), ring = k.PushU(1), p0 = k.PushU(2), T = k.PushU(3), parity = k.PushU(4);
            uint pairs = k.Shr(kvDim, k.C(1));
            uint t0 = k.UDiv(g, pairs);
            uint slot0 = k.UMod(k.IAdd(p0, t0), ring);
            k.If(k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(g, k.IMul(pairs, T)), k.IEq(k.And(slot0, k.C(1)), parity)), delegate
            {
                uint t = t0, i = k.IMul(k.UMod(g, pairs), k.C(2));
                uint slot = slot0;
                uint src = k.IAdd(k.IMul(t, kvDim), i);
                uint hiHalf = k.IEq(k.And(slot, k.C(1)), k.C(1));
                for (uint e = 0; e < 2; e++)
                {
                    uint idx = k.IAdd(k.IMul(k.IAdd(i, k.C(e)), ring), slot);
                    uint wi = k.Shr(idx, k.C(1));
                    uint val = k.F16(k.Ld(kk, k.IAdd(src, k.C(e))));
                    uint old = k.Ld(kc, wi);
                    uint v2 = k.S.TypeVector(k.F32, 2);
                    uint asLo = k.Or(k.And(old, k.C(0xFFFF0000)), k.And(k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, val, k.CF(0))), k.C(0xFFFF)));
                    uint asHi = k.Or(k.And(old, k.C(0xFFFF)), k.And(k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, k.CF(0), val)), k.C(0xFFFF0000)));
                    k.St(kc, wi, k.Select(k.U32, hiHalf, asHi, asLo));
                }
                uint vw = k.Shr(k.IAdd(k.IMul(slot, kvDim), i), k.C(1));
                uint v2b = k.S.TypeVector(k.F32, 2);
                k.St(vc, vw, k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2b, k.F16(k.Ld(v, src)), k.F16(k.Ld(v, k.IAdd(src, k.C(1)))))));
            });
            return k.Finish();
        }

        public const uint AttWg = 256;   // threads per (query, head); each owns head dimensions lid and lid + 256

        // Causal attention for T query tokens at positions p0 + t: query t, head h attends keys [lo, p] with
        // lo = max(0, p - window + 1) (window 0 = all). Keys/values from the ring caches (this chunk is already stored).
        // Streaming softmax over 256-key chunks, so any context length works. Scores use f16(q) like the decode path.
        // Grid (T, nHead). bindings: 0 q [T][nHead][hd], 1 kc, 2 vc, 3 out [T][nHead][hd].
        // push: hd, kvDim, ring, nHead, nKv, p0, window, scale(bits), b0, b1 (bidirectional image span; 0, 0 = causal)
        public static uint[] Attention() { return Attention(KvType.F16, KvType.F16); }

        // kvType: the K cache format; vType: the V cache format (KernelsKv).
        public static uint[] Attention(KvType kvType, KvType vType)
        {
            KB k = new KB(AttWg);
            KB.Buf q = k.Buffer(0, k.F32, true), kc = k.Buffer(1, k.U32, true), vc = k.Buffer(2, k.U32, true), ob = k.Buffer(3, k.F32, false);
            k.Push(10);
            KB.Shared qs = k.SharedArray(k.F32, 512), sc = k.SharedArray(k.F32, AttWg), red = k.SharedArray(k.F32, AttWg);
            k.Begin();
            uint c0v = k.Local(k.U32), d = k.Local(k.U32), j = k.Local(k.U32), acc = k.Local(k.F32);
            uint m = k.Local(k.F32), ssum = k.Local(k.F32), o0 = k.Local(k.F32), o1 = k.Local(k.F32);
            uint hd = k.PushU(0), kvDim = k.PushU(1), ring = k.PushU(2), nHead = k.PushU(3), nKv = k.PushU(4);
            uint p0 = k.PushU(5), window = k.PushU(6), scale = k.PushF(7);
            uint t = k.GroupX(), h = k.GroupY(), lid = k.LocalId();
            uint p = k.IAdd(p0, t);
            uint kvh = k.UDiv(h, k.UDiv(nHead, nKv));
            uint lo = k.Select(k.U32, k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.INotEqual, k.Bool, window, k.C(0)), k.S.Value(Op.UGreaterThanEqual, k.Bool, p, window)),
                               k.IAdd(k.S.Value(Op.ISub, k.U32, p, window), k.C(1)), k.C(0));
            uint qo = k.IMul(k.IAdd(k.IMul(t, nHead), h), hd);
            k.For(d, lid, hd, k.C(AttWg), delegate { k.St(qs, k.Get(d, k.U32), k.F16(k.Ld(q, k.IAdd(qo, k.Get(d, k.U32))))); });
            k.Barrier();
            uint kRow0 = k.IMul(kvh, hd);
            uint d1 = k.IAdd(lid, k.C(AttWg));
            uint has1 = k.ULt(d1, hd);
            k.Set(m, k.CF(float.NegativeInfinity)); k.Set(ssum, k.CF(0)); k.Set(o0, k.CF(0)); k.Set(o1, k.CF(0));
            // [b0, b1): an image whose tokens see each other both ways (b1 = 0: none): keys run to the image end
            uint b0 = k.PushU(8), b1 = k.PushU(9);
            uint inImg = k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.UGreaterThanEqual, k.Bool, p, b0), k.ULt(p, b1));
            uint pEnd = k.S.Glsl(k.U32, Spv.GlslUMax, k.IAdd(p, k.C(1)), k.Select(k.U32, inImg, b1, k.C(0)));
            k.For(c0v, lo, pEnd, k.C(AttWg), delegate
            {
                uint c0 = k.Get(c0v, k.U32);
                uint key = k.IAdd(c0, lid);
                uint valid = k.ULt(key, pEnd);
                uint slot = k.UMod(k.Select(k.U32, valid, key, c0), ring);
                k.Set(acc, k.CF(0));
                k.For(d, k.C(0), hd, k.C(1), delegate
                {
                    uint dd = k.Get(d, k.U32);
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(k.Ld(qs, dd), KernelsKv.K(k, kvType, kc, k.IAdd(kRow0, dd), slot, ring, kvDim))));
                });
                uint s = k.Select(k.F32, valid, k.FMul(k.Get(acc, k.F32), scale), k.CF(float.NegativeInfinity));
                k.St(red, lid, s);
                k.Reduce(red, lid, AttWg, k.FMax);
                uint mNew = k.FMax(k.Get(m, k.F32), k.Ld(red, k.C(0)));
                k.Barrier();
                uint e = k.Select(k.F32, valid, k.Exp(k.FSub(s, mNew)), k.CF(0));
                k.St(sc, lid, e);
                k.St(red, lid, e);
                k.Reduce(red, lid, AttWg, k.FAdd);
                uint corr = k.Exp(k.FSub(k.Get(m, k.F32), mNew));
                k.Set(ssum, k.FAdd(k.FMul(k.Get(ssum, k.F32), corr), k.Ld(red, k.C(0))));
                k.Set(o0, k.FMul(k.Get(o0, k.F32), corr));
                k.Set(o1, k.FMul(k.Get(o1, k.F32), corr));
                uint cnt = k.S.Glsl(k.U32, Spv.GlslUMin, k.C(AttWg), k.S.Value(Op.ISub, k.U32, pEnd, c0));
                k.For(j, k.C(0), cnt, k.C(1), delegate
                {
                    uint jj = k.Get(j, k.U32);
                    uint pj = k.Ld(sc, jj);
                    uint vSlot = k.UMod(k.IAdd(c0, jj), ring);
                    k.Set(o0, k.FAdd(k.Get(o0, k.F32), k.FMul(pj, KernelsKv.V(k, vType, vc, vSlot, k.IAdd(kRow0, k.Select(k.U32, k.ULt(lid, hd), lid, k.C(0))), ring, kvDim))));
                    k.Set(o1, k.FAdd(k.Get(o1, k.F32), k.FMul(pj, KernelsKv.V(k, vType, vc, vSlot, k.IAdd(kRow0, k.Select(k.U32, has1, d1, k.C(0))), ring, kvDim))));
                });
                k.Set(m, mNew);
                k.Barrier();
            });
            uint inv = k.FDiv(k.CF(1f), k.Get(ssum, k.F32));
            k.If(k.ULt(lid, hd), delegate { k.St(ob, k.IAdd(qo, lid), k.FMul(k.Get(o0, k.F32), inv)); });
            k.If(has1, delegate { k.St(ob, k.IAdd(qo, d1), k.FMul(k.Get(o1, k.F32), inv)); });
            return k.Finish();
        }

        // dst[dstOff + i] = src[srcOff + i]. bindings: 0 src, 1 dst. push: count, srcOff, dstOff
        public static uint[] CopyRange()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf src = k.Buffer(0, k.F32, true), dst = k.Buffer(1, k.F32, false);
            k.Push(3);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate { k.St(dst, k.IAdd(k.PushU(2), i), k.Ld(src, k.IAdd(k.PushU(1), i))); });
            return k.Finish();
        }
    }
}
