// Batched prompt processing for Llama-family models with k-quant weights. C# 5 only.
//   MatMulK   Q4_K / Q6_K weights x Q8_K-quantized activations for T tokens: the integer math of KernelsK.MatVec, with
//             each weight group decoded once and reused for `tok` tokens staged in shared memory
//   RopeRows  normal (adjacent-pair) RoPE for every token, as Kernels.Rope / KernelsAttention.Rope
// Needs integer dot products (SPV_KHR_integer_dot_product); without them the model prefills token by token.
using System;

namespace Phoenix
{
    public static class KernelsPrefillK
    {
        public const uint Threads = 64;

        // Y[t][r] (+)= W[r] . X[t]. One thread per weight row, `tok` tokens per workgroup; per 256-element block the
        // tokens' Q8_K words (64 each) and scales go to shared memory, then each of the 8 groups of 32 weights is decoded
        // once and dotted with every token. Grid (ceil(rows / 64), ceil(T / tok)).
        // bindings: 0 w u32 (blocks, Q6_K padded to 53 words), 1 kq u32 [T][nb * 64], 2 kd f32 [T][nb], 3 y f32 [T][rows].
        // push: rows, nb, T, addTo
        public static uint[] MatMulK(GgmlType type, uint tok)
        {
            if (type != GgmlType.Q4_K && type != GgmlType.Q6_K) throw new NotSupportedException(GgmlTypes.Name(type));
            uint bw = KernelsK.BlockWords(type);
            KB k = new KB(Threads);
            KB.Buf wb = k.Buffer(0, k.U32, true), kq = k.Buffer(1, k.U32, true), kd = k.Buffer(2, k.F32, true), y = k.Buffer(3, k.F32, false);
            k.Push(4);
            KB.Shared xs = k.SharedArray(k.U32, tok * 64), xds = k.SharedArray(k.F32, tok);
            k.Begin();
            uint bv = k.Local(k.U32), i = k.Local(k.U32);
            uint[] acc = new uint[tok];
            for (int t = 0; t < tok; t++) acc[t] = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), T = k.PushU(2), addTo = k.PushU(3);
            uint lid = k.LocalId();
            uint r = k.IAdd(k.IMul(k.GroupX(), k.C(Threads)), lid);
            uint rowOk = k.ULt(r, rows);
            uint rowBase = k.IMul(k.Select(k.U32, rowOk, r, k.C(0)), nb);   // out-of-range rows read row 0, never stored
            uint tok0 = k.IMul(k.GroupY(), k.C(tok));
            for (int t = 0; t < tok; t++) k.Set(acc[t], k.CF(0));
            k.For(bv, k.C(0), nb, k.C(1), delegate
            {
                uint b = k.Get(bv, k.U32);
                k.For(i, lid, k.C(tok * 64), k.C(Threads), delegate
                {
                    uint idx = k.Get(i, k.U32);
                    uint tk = k.IAdd(tok0, k.Shr(idx, k.C(6)));
                    uint ok = k.ULt(tk, T);
                    uint src = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, tk, k.C(0)), nb), b), k.C(64)), k.And(idx, k.C(63)));
                    k.St(xs, idx, k.Select(k.U32, ok, k.Ld(kq, src), k.C(0)));
                });
                k.If(k.ULt(lid, k.C(tok)), delegate
                {
                    uint tk = k.IAdd(tok0, lid);
                    uint ok = k.ULt(tk, T);
                    k.St(xds, lid, k.Select(k.F32, ok, k.Ld(kd, k.IAdd(k.IMul(k.Select(k.U32, ok, tk, k.C(0)), nb), b)), k.CF(0)));
                });
                k.Barrier();
                uint wBase = k.IMul(k.IAdd(rowBase, b), k.C(bw));
                for (uint g = 0; g < 8; g++)
                {
                    if (type == GgmlType.Q4_K) Q4KGroup(k, wb, wBase, g, xs, xds, acc, tok);
                    else Q6KGroup(k, wb, wBase, g, xs, xds, acc, tok);
                }
                k.Barrier();
            });
            k.If(rowOk, delegate
            {
                for (uint t = 0; t < tok; t++)
                {
                    uint tk = k.IAdd(tok0, k.C(t));
                    uint tt = t;
                    k.If(k.ULt(tk, T), delegate
                    {
                        uint idx = k.IAdd(k.IMul(tk, rows), r);
                        uint v = k.Get(acc[tt], k.F32);
                        k.St(y, idx, k.Select(k.F32, k.IEq(addTo, k.C(1)), k.FAdd(k.Ld(y, idx), v), v));
                    });
                }
            });
            return k.Finish();
        }

        // Q4_K group g: d*yd*sc*isum - dmin*yd*m*qsum (as KernelsK.Q45Group) for every staged token.
        static void Q4KGroup(KB k, KB.Buf wb, uint wBase, uint g, KB.Shared xs, KB.Shared xds, uint[] acc, uint tok)
        {
            uint dw = k.Ld(wb, wBase);
            uint d = k.HalfOf(dw, false), dmin = k.HalfOf(dw, true);
            uint scBase = k.IAdd(k.IMul(wBase, k.C(4)), k.C(4));
            uint sc, m;
            if (g < 4)
            {
                sc = k.And(k.ByteAt(wb, k.IAdd(scBase, k.C(g))), k.C(63));
                m = k.And(k.ByteAt(wb, k.IAdd(scBase, k.C(g + 4))), k.C(63));
            }
            else
            {
                uint bG = k.ByteAt(wb, k.IAdd(scBase, k.C(g))), bG4 = k.ByteAt(wb, k.IAdd(scBase, k.C(g + 4))), bGm4 = k.ByteAt(wb, k.IAdd(scBase, k.C(g - 4)));
                sc = k.Or(k.And(bG4, k.C(0xF)), k.Shl(k.Shr(bGm4, k.C(6)), k.C(4)));
                m = k.Or(k.Shr(bG4, k.C(4)), k.Shl(k.Shr(bG, k.C(6)), k.C(4)));
            }
            uint scI = k.AsI(sc), mI = k.AsI(m);
            uint qsBase = k.IAdd(wBase, k.C(4 + (g >> 1) * 8));
            uint[] packed = new uint[8];
            for (uint w = 0; w < 8; w++) packed[w] = k.And(k.Shr(k.Ld(wb, k.IAdd(qsBase, k.C(w))), k.C((g & 1) * 4)), k.C(0x0F0F0F0F));
            for (uint t = 0; t < tok; t++)
            {
                uint isum = k.CI(0), qsum = k.CI(0);
                for (uint w = 0; w < 8; w++)
                {
                    uint xw = k.Ld(xs, k.C(t * 64 + g * 8 + w));
                    isum = k.Dot4Acc(packed[w], xw, isum);
                    qsum = k.Dot4Acc(xw, k.C(0x01010101), qsum);
                }
                uint yd = k.Ld(xds, k.C(t));
                // Same products as KernelsK: (d*yd) * (sc*isum) - (dmin*yd) * (m*qsum).
                uint a = k.FMul(k.FMul(d, yd), k.SToF(k.IMulS(scI, isum)));
                uint b2 = k.FMul(k.FMul(dmin, yd), k.SToF(k.IMulS(mI, qsum)));
                k.Set(acc[t], k.FAdd(k.Get(acc[t], k.F32), k.FSub(a, b2)));
            }
        }

        // Q6_K group g = 4n + part: d*yd*(sc0*isum0 + sc1*isum1) (as KernelsK.Q6Group) for every staged token.
        static void Q6KGroup(KB k, KB.Buf wb, uint wBase, uint g, KB.Shared xs, KB.Shared xds, uint[] acc, uint tok)
        {
            uint n = g >> 2, part = g & 3;
            uint d = k.HalfOf(k.Ld(wb, k.IAdd(wBase, k.C(52))), false);
            uint qlBase = k.IAdd(wBase, k.C(n * 16 + (part & 1) * 8));
            uint qhBase = k.IAdd(wBase, k.C(32 + n * 8));
            uint scByte = k.IAdd(k.IMul(wBase, k.C(4)), k.C(192 + n * 8 + part * 2));
            uint sc0 = Sext8(k, k.ByteAt(wb, scByte)), sc1 = Sext8(k, k.ByteAt(wb, k.IAdd(scByte, k.C(1))));
            uint[] packed = new uint[8];
            for (uint w = 0; w < 8; w++)
            {
                uint lo4 = k.And(k.Shr(k.Ld(wb, k.IAdd(qlBase, k.C(w))), k.C((part >> 1) * 4)), k.C(0x0F0F0F0F));
                uint hi4 = k.Shl(k.And(k.Shr(k.Ld(wb, k.IAdd(qhBase, k.C(w))), k.C(part * 2)), k.C(0x03030303)), k.C(4));
                packed[w] = k.Or(lo4, hi4);   // q + 32, 0..63
            }
            for (uint t = 0; t < tok; t++)
            {
                uint i0 = k.CI(0), i1 = k.CI(0), s0 = k.CI(0), s1 = k.CI(0);
                for (uint w = 0; w < 8; w++)
                {
                    uint xw = k.Ld(xs, k.C(t * 64 + g * 8 + w));
                    if (w < 4) { i0 = k.Dot4Acc(packed[w], xw, i0); s0 = k.Dot4Acc(xw, k.C(0x01010101), s0); }
                    else { i1 = k.Dot4Acc(packed[w], xw, i1); s1 = k.Dot4Acc(xw, k.C(0x01010101), s1); }
                }
                i0 = k.ISubS(i0, k.IMulS(s0, k.CI(32)));
                i1 = k.ISubS(i1, k.IMulS(s1, k.CI(32)));
                uint isum = k.IAddS(k.IMulS(sc0, i0), k.IMulS(sc1, i1));
                uint yd = k.Ld(xds, k.C(t));
                k.Set(acc[t], k.FAdd(k.Get(acc[t], k.F32), k.FMul(k.FMul(d, yd), k.SToF(isum))));
            }
        }

        static uint Sext8(KB k, uint b)
        {
            return k.S.Value(Op.ShiftRightArithmetic, k.I32, k.AsI(k.Shl(b, k.C(24))), k.C(24));
        }

        // Normal RoPE (pairs 2i, 2i+1) for every token. x [T][nHeads][headDim], cs [T][nRot] (cos, sin per pair).
        // bindings: 0 x, 1 cs. push: nHeads, headDim, half, T
        public static uint[] RopeRows()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, false), cs = k.Buffer(1, k.F32, true);
            k.Push(4);
            k.Begin();
            uint p = k.GlobalId();
            uint nHeads = k.PushU(0), headDim = k.PushU(1), half = k.PushU(2), T = k.PushU(3);
            uint perTok = k.IMul(nHeads, half);
            k.If(k.ULt(p, k.IMul(perTok, T)), delegate
            {
                uint t = k.UDiv(p, perTok), rem = k.UMod(p, perTok);
                uint h = k.UDiv(rem, half), i = k.UMod(rem, half);
                uint o = k.IAdd(k.IMul(k.IAdd(k.IMul(t, nHeads), h), headDim), k.IMul(i, k.C(2)));
                uint co = k.IAdd(k.IMul(t, k.IMul(half, k.C(2))), k.IMul(i, k.C(2)));
                uint a = k.Ld(x, o), b = k.Ld(x, k.IAdd(o, k.C(1)));
                uint c = k.Ld(cs, co), s = k.Ld(cs, k.IAdd(co, k.C(1)));
                k.St(x, o, k.FSub(k.FMul(a, c), k.FMul(b, s)));
                k.St(x, k.IAdd(o, k.C(1)), k.FAdd(k.FMul(a, s), k.FMul(b, c)));
            });
            return k.Finish();
        }
    }
}
