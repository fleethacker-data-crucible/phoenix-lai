// Batched kernels: process a whole token sequence per dispatch (EmbeddingGemma on the GPU; a base for batched
// prefill). Activations are row-major [token][feature] f32. C# 5 only.
using System;

namespace Phoenix
{
    public static class KernelsBatch
    {
        public const uint MmRows = 64, MmTokens = 8, MmK = 128;   // matmul tile: 64 weight rows x 8 tokens, K step 128

        // Y[t][r] = sum_j W[r][j] X[t][j], W as fp16 pairs (cols even, multiple of MmK). Grid (ceil(rows/64), ceil(n/8)).
        // bindings: 0 W u32 [rows * cols/2], 1 X f32 [n * cols], 2 Y f32 [n * rows]. push: rows, cols, n
        public static uint[] MatMulF16()
        {
            KB k = new KB(MmRows);
            KB.Buf w = k.Buffer(0, k.U32, true), x = k.Buffer(1, k.F32, true), y = k.Buffer(2, k.F32, false);
            k.Push(3);
            KB.Shared xs = k.SharedArray(k.F32, MmTokens * MmK);
            k.Begin();
            uint k0 = k.Local(k.U32), i = k.Local(k.U32), kk = k.Local(k.U32);
            uint[] acc = new uint[MmTokens];
            for (int t = 0; t < MmTokens; t++) acc[t] = k.Local(k.F32);
            uint rows = k.PushU(0), cols = k.PushU(1), n = k.PushU(2);
            uint lid = k.LocalId();
            uint r = k.IAdd(k.IMul(k.GroupX(), k.C(MmRows)), lid);
            uint tok0 = k.IMul(k.GroupY(), k.C(MmTokens));
            uint rowOk = k.ULt(r, rows);
            uint rSafe = k.Select(k.U32, rowOk, r, k.C(0));
            uint wRow = k.IMul(rSafe, k.Shr(cols, k.C(1)));
            for (int t = 0; t < MmTokens; t++) k.Set(acc[t], k.CF(0));
            k.For(k0, k.C(0), cols, k.C(MmK), delegate
            {
                uint kb = k.Get(k0, k.U32);
                k.For(i, lid, k.C(MmTokens * MmK), k.C(MmRows), delegate
                {
                    uint idx = k.Get(i, k.U32);
                    uint t = k.UDiv(idx, k.C(MmK)), c = k.UMod(idx, k.C(MmK));
                    uint tok = k.IAdd(tok0, t);
                    uint ok = k.ULt(tok, n);
                    uint src = k.IAdd(k.IMul(k.Select(k.U32, ok, tok, k.C(0)), cols), k.IAdd(kb, c));
                    k.St(xs, idx, k.Select(k.F32, ok, k.Ld(x, src), k.CF(0)));
                });
                k.Barrier();
                k.For(kk, k.C(0), k.C(MmK), k.C(2), delegate
                {
                    uint c = k.Get(kk, k.U32);
                    uint ww = k.Ld(w, k.IAdd(wRow, k.Shr(k.IAdd(kb, c), k.C(1))));
                    uint w0 = k.HalfOf(ww, false), w1 = k.HalfOf(ww, true);
                    for (int t = 0; t < MmTokens; t++)
                    {
                        uint xo = k.IAdd(k.C((uint)(t * MmK)), c);
                        k.Set(acc[t], k.FAdd(k.Get(acc[t], k.F32), k.FAdd(k.FMul(w0, k.Ld(xs, xo)), k.FMul(w1, k.Ld(xs, k.IAdd(xo, k.C(1)))))));
                    }
                });
                k.Barrier();
            });
            k.If(rowOk, delegate
            {
                for (int t = 0; t < MmTokens; t++)
                {
                    uint tok = k.IAdd(tok0, k.C((uint)t));
                    uint tt = (uint)t;
                    k.If(k.ULt(tok, n), delegate { k.St(y, k.IAdd(k.IMul(tok, rows), r), k.Get(acc[tt], k.F32)); });
                }
            });
            return k.Finish();
        }

        // Per-row RMSNorm with a weight. mode 0: y = norm(x) * w.  mode 1: x = (x + norm(y) * w) * s (residual add + scale).
        // One workgroup per row (token). bindings: 0 x, 1 y, 2 w. push: d, eps(bits), mode, s(bits)
        public static uint[] RowNorm()
        {
            KB k = new KB(Kernels.NormWg);
            KB.Buf x = k.Buffer(0, k.F32, false), y = k.Buffer(1, k.F32, false), w = k.Buffer(2, k.F32, true);
            k.Push(4);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.NormWg);
            k.Begin();
            uint i = k.Local(k.U32), acc = k.Local(k.F32);
            uint d = k.PushU(0), eps = k.PushF(1), mode = k.PushU(2), s = k.PushF(3);
            uint lid = k.LocalId(), off = k.IMul(k.GroupX(), d);
            uint addMode = k.IEq(mode, k.C(1));
            k.Set(acc, k.CF(0));
            k.For(i, lid, d, k.C(Kernels.NormWg), delegate
            {
                uint idx = k.IAdd(off, k.Get(i, k.U32));
                uint v = k.Select(k.F32, addMode, k.Ld(y, idx), k.Ld(x, idx));
                k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(v, v)));
            });
            k.St(sh, lid, k.Get(acc, k.F32));
            k.Reduce(sh, lid, Kernels.NormWg, k.FAdd);
            uint scale = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(k.Ld(sh, k.C(0)), k.ToF(d)), eps)));
            k.For(i, lid, d, k.C(Kernels.NormWg), delegate
            {
                uint j = k.Get(i, k.U32), idx = k.IAdd(off, j);
                k.If(addMode, delegate { k.St(x, idx, k.FMul(k.FAdd(k.Ld(x, idx), k.FMul(k.FMul(k.Ld(y, idx), scale), k.Ld(w, j))), s)); });
                k.If(k.S.Value(Op.LogicalNot, k.Bool, addMode), delegate { k.St(y, idx, k.FMul(k.FMul(k.Ld(x, idx), scale), k.Ld(w, j))); });
            });
            return k.Finish();
        }

        // NeoX RoPE for every token: x [n][nHeads][headDim], cs [n][nRot] (cos, sin per pair).
        // bindings: 0 x, 1 cs. push: nHeads, headDim, halfRot, n
        public static uint[] RopeNeoxRows()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, false), cs = k.Buffer(1, k.F32, true);
            k.Push(4);
            k.Begin();
            uint p = k.GlobalId();
            uint nHeads = k.PushU(0), headDim = k.PushU(1), half = k.PushU(2), n = k.PushU(3);
            uint perTok = k.IMul(nHeads, half);
            k.If(k.ULt(p, k.IMul(perTok, n)), delegate
            {
                uint t = k.UDiv(p, perTok), q = k.UMod(p, perTok);
                uint h = k.UDiv(q, half), i = k.UMod(q, half);
                uint o = k.IAdd(k.IMul(k.IAdd(k.IMul(t, nHeads), h), headDim), i), o2 = k.IAdd(o, half);
                uint cso = k.IAdd(k.IMul(t, k.IMul(half, k.C(2))), k.IMul(i, k.C(2)));
                uint a = k.Ld(x, o), b = k.Ld(x, o2), c = k.Ld(cs, cso), s = k.Ld(cs, k.IAdd(cso, k.C(1)));
                k.St(x, o, k.FSub(k.FMul(a, c), k.FMul(b, s)));
                k.St(x, o2, k.FAdd(k.FMul(a, s), k.FMul(b, c)));
            });
            return k.Finish();
        }

        public const uint MaxSeq = 1024;   // longest sequence the bidirectional attention kernel takes (shared scores)

        // Bidirectional attention, scale 1: query token t, head h attends keys [lo, hi] (symmetric window, or all).
        // q [n][nHead][hd], k/v [n][nKv][hd], out [n][nHead][hd]. Grid (n, nHead).
        // bindings: 0 q, 1 k, 2 v, 3 out. push: n, hd, nHead, nKv, halfWindow (0 = all)
        public static uint[] AttentionBi()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf q = k.Buffer(0, k.F32, true), kb = k.Buffer(1, k.F32, true), vb = k.Buffer(2, k.F32, true), ob = k.Buffer(3, k.F32, false);
            k.Push(5);
            KB.Shared sc = k.SharedArray(k.F32, MaxSeq), red = k.SharedArray(k.F32, Kernels.Wg);
            k.Begin();
            uint j = k.Local(k.U32), d = k.Local(k.U32), acc = k.Local(k.F32), mx = k.Local(k.F32), sum = k.Local(k.F32);
            uint n = k.PushU(0), hd = k.PushU(1), nHead = k.PushU(2), nKv = k.PushU(3), halfW = k.PushU(4);
            uint t = k.GroupX(), h = k.GroupY(), lid = k.LocalId();
            uint group = k.UDiv(nHead, nKv), kvh = k.UDiv(h, group);
            uint all = k.IEq(halfW, k.C(0));
            uint lo = k.Select(k.U32, k.S.Value(Op.LogicalOr, k.Bool, all, k.ULt(t, halfW)), k.C(0), k.S.Value(Op.ISub, k.U32, t, halfW));
            uint hiEx = k.Select(k.U32, all, n, k.S.Glsl(k.U32, Spv.GlslUMin, n, k.IAdd(k.IAdd(t, halfW), k.C(1))));   // exclusive end
            uint qo = k.IMul(k.IAdd(k.IMul(t, nHead), h), hd);
            // scores
            k.Set(mx, k.CF(float.NegativeInfinity));
            k.For(j, k.IAdd(lo, lid), hiEx, k.C(Kernels.Wg), delegate
            {
                uint key = k.Get(j, k.U32);
                uint ko = k.IMul(k.IAdd(k.IMul(key, nKv), kvh), hd);
                k.Set(acc, k.CF(0));
                k.For(d, k.C(0), hd, k.C(1), delegate
                {
                    uint dd = k.Get(d, k.U32);
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(k.Ld(q, k.IAdd(qo, dd)), k.Ld(kb, k.IAdd(ko, dd)))));
                });
                k.St(sc, k.S.Value(Op.ISub, k.U32, key, lo), k.Get(acc, k.F32));
                k.Set(mx, k.FMax(k.Get(mx, k.F32), k.Get(acc, k.F32)));
            });
            k.St(red, lid, k.Get(mx, k.F32));
            k.Reduce(red, lid, Kernels.Wg, k.FMax);
            uint m = k.Ld(red, k.C(0));
            k.Barrier();
            k.Set(sum, k.CF(0));
            k.For(j, k.IAdd(lo, lid), hiEx, k.C(Kernels.Wg), delegate
            {
                uint si = k.S.Value(Op.ISub, k.U32, k.Get(j, k.U32), lo);
                uint e = k.Exp(k.FSub(k.Ld(sc, si), m));
                k.St(sc, si, e);
                k.Set(sum, k.FAdd(k.Get(sum, k.F32), e));
            });
            k.St(red, lid, k.Get(sum, k.F32));
            k.Reduce(red, lid, Kernels.Wg, k.FAdd);
            uint inv = k.FDiv(k.CF(1f), k.Ld(red, k.C(0)));
            k.Barrier();
            // out[d] = sum_j p_j v[j][d]
            k.For(d, lid, hd, k.C(Kernels.Wg), delegate
            {
                uint dd = k.Get(d, k.U32);
                k.Set(acc, k.CF(0));
                k.For(j, lo, hiEx, k.C(1), delegate
                {
                    uint key = k.Get(j, k.U32);
                    uint p = k.FMul(k.Ld(sc, k.S.Value(Op.ISub, k.U32, key, lo)), inv);
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(p, k.Ld(vb, k.IAdd(k.IMul(k.IAdd(k.IMul(key, nKv), kvh), hd), dd)))));
                });
                k.St(ob, k.IAdd(qo, dd), k.Get(acc, k.F32));
            });
            return k.Finish();
        }

        // g[t][i] = gelu(g[t][i]) * u[t * uStride + uOff + i] (FFN gate·up: uStride = width, uOff = 0; per-layer input:
        // u = the PLE rows, uStride = layers * width, uOff = layer * width). bindings: 0 g, 1 u. push: width, n, uStride, uOff
        public static uint[] GeluMulRows()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf g = k.Buffer(0, k.F32, false), u = k.Buffer(1, k.F32, true);
            k.Push(4);
            k.Begin();
            uint p = k.GlobalId();
            uint width = k.PushU(0), n = k.PushU(1), uStride = k.PushU(2), uOff = k.PushU(3);
            k.If(k.ULt(p, k.IMul(width, n)), delegate
            {
                uint t = k.UDiv(p, width), i = k.UMod(p, width);
                uint ui = k.IAdd(k.IAdd(k.IMul(t, uStride), uOff), i);
                k.St(g, p, k.FMul(KernelsQuant.Gelu(k, k.Ld(g, p)), k.Ld(u, ui)));
            });
            return k.Finish();
        }

        // x[i] *= s (bindings: 0 x. push: count, s(bits)) — PLE input scaling.
        public static uint[] Scale()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, false);
            k.Push(2);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate { k.St(x, i, k.FMul(k.Ld(x, i), k.PushF(1))); });
            return k.Finish();
        }

        // Copies x into y (bindings: 0 x, 1 y. push: count) — V = K for layers without a V weight.
        public static uint[] Copy()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, true), y = k.Buffer(1, k.F32, false);
            k.Push(1);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate { k.St(y, i, k.Ld(x, i)); });
            return k.Finish();
        }
    }
}
