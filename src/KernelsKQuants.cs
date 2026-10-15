// GPU kernels for k-quant weights (Q4_K, Q5_K, Q6_K) with Q8_K activations. C# 5 only.
// Integer math mirrors CpuRef.DotK; only the float accumulation order differs.
using System;

namespace Phoenix
{
    public static class KernelsK
    {
        public const int Q6_KPaddedBytes = 212; // block_q6_K (210 bytes) padded to whole words for the GPU

        // Words per block as stored on the GPU.
        public static uint BlockWords(GgmlType t)
        {
            switch (t)
            {
                case GgmlType.Q4_K: return 36;
                case GgmlType.Q5_K: return 44;
                case GgmlType.Q6_K: return 53;
            }
            throw new NotSupportedException(GgmlTypes.Name(t));
        }

        // Q8_K activations (CpuRef.QuantizeQ8_K). One thread per 256-element block.
        // bindings: 0 x f32, 1 kq u32 (64 words per block), 2 kd f32. push: nBlocks
        public static uint[] QuantizeQ8K()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, true), kq = k.Buffer(1, k.U32, false), kd = k.Buffer(2, k.F32, false);
            k.Push(1);
            k.Begin();
            uint j = k.Local(k.U32), amax = k.Local(k.F32), max = k.Local(k.F32), word = k.Local(k.U32), w = k.Local(k.U32);
            uint b = k.GlobalId();
            k.If(k.ULt(b, k.PushU(0)), delegate
            {
                uint o = k.IMul(b, k.C(256));
                k.Set(amax, k.CF(0)); k.Set(max, k.CF(0));
                k.For(j, k.C(0), k.C(256), k.C(1), delegate
                {
                    uint v = k.Ld(x, k.IAdd(o, k.Get(j, k.U32)));
                    uint gt = k.S.Value(Op.FOrdGreaterThan, k.Bool, k.Abs(v), k.Get(amax, k.F32));
                    k.Set(amax, k.Select(k.F32, gt, k.Abs(v), k.Get(amax, k.F32)));
                    k.Set(max, k.Select(k.F32, gt, v, k.Get(max, k.F32)));
                });
                uint nonzero = k.FNe(k.Get(amax, k.F32), k.CF(0));
                uint iscale = k.Select(k.F32, nonzero, k.FDiv(k.CF(-127f), k.Get(max, k.F32)), k.CF(0));
                k.For(w, k.C(0), k.C(64), k.C(1), delegate
                {
                    k.Set(word, k.C(0));
                    uint baseIdx = k.IAdd(o, k.IMul(k.Get(w, k.U32), k.C(4)));
                    for (int kk = 0; kk < 4; kk++)
                    {
                        uint v = k.FToS(k.RoundEven(k.FMul(iscale, k.Ld(x, k.IAdd(baseIdx, k.C((uint)kk))))));
                        uint q = k.And(k.AsU(k.SMin(v, k.CI(127))), k.C(0xFF));
                        k.Set(word, k.Or(k.Get(word, k.U32), kk == 0 ? q : k.Shl(q, k.C((uint)(8 * kk)))));
                    }
                    k.St(kq, k.IAdd(k.IMul(b, k.C(64)), k.Get(w, k.U32)), k.Get(word, k.U32));
                });
                k.St(kd, b, k.Select(k.F32, nonzero, k.FDiv(k.CF(1f), iscale), k.CF(0)));
            });
            return k.Finish();
        }

        // y[r] = dot(W[r], x) for k-quant W. One workgroup per row; each thread takes (block, 32-element group) items.
        // bindings: 0 w u32 (blocks), 1 kq u32, 2 kd f32, 3 y f32. push: rows, nb (blocks per row), groupsX
        public static uint[] MatVec(GgmlType type)
        {
            uint bw = BlockWords(type);
            KB k = new KB(Kernels.Wg);
            KB.Buf wb = k.Buffer(0, k.U32, true), kq = k.Buffer(1, k.U32, true), kd = k.Buffer(2, k.F32, true), y = k.Buffer(3, k.F32, false);
            k.Push(3);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.Wg);
            k.Begin();
            uint itv = k.Local(k.U32), sum = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), gx = k.PushU(2);
            uint r = k.IAdd(k.GroupX(), k.IMul(k.GroupY(), gx));
            uint lid = k.LocalId();
            k.Set(sum, k.CF(0));
            k.If(k.ULt(r, rows), delegate
            {
                k.For(itv, lid, k.IMul(nb, k.C(8)), k.C(Kernels.Wg), delegate
                {
                    uint item = k.Get(itv, k.U32);
                    uint b = k.Shr(item, k.C(3)), g = k.And(item, k.C(7));
                    uint wBase = k.IMul(k.IAdd(k.IMul(r, nb), b), k.C(bw));  // first word of the block
                    uint xBase = k.IAdd(k.IMul(b, k.C(64)), k.IMul(g, k.C(8))); // first kq word of the 32 activations
                    uint yd = k.Ld(kd, b);
                    uint contrib = type == GgmlType.Q6_K ? Q6Group(k, wb, kq, wBase, xBase, g, yd) : Q45Group(k, type, wb, kq, wBase, xBase, g, yd);
                    k.Set(sum, k.FAdd(k.Get(sum, k.F32), contrib));
                });
            });
            k.St(sh, lid, k.Get(sum, k.F32));
            k.Reduce(sh, lid, Kernels.Wg, k.FAdd);
            k.If(k.ULt(r, rows), delegate
            {
                k.If(k.IEq(lid, k.C(0)), delegate { k.St(y, r, k.Ld(sh, k.C(0))); });
            });
            return k.Finish();
        }

        // Q4_K / Q5_K: sub-block g (32 elements) of the block. Returns d*yd*sc*isum - dmin*yd*m*qsum.
        static uint Q45Group(KB k, GgmlType type, KB.Buf wb, KB.Buf kq, uint wBase, uint xBase, uint g, uint yd)
        {
            uint dw = k.Ld(wb, wBase);
            uint d = k.HalfOf(dw, false), dmin = k.HalfOf(dw, true);

            // get_scale_min_k4(g, scales) with scales at byte 4 of the block.
            uint sBase = k.IMul(wBase, k.C(4));
            uint scBase = k.IAdd(sBase, k.C(4));
            uint isLow = k.ULt(g, k.C(4));
            uint gm4 = k.Select(k.U32, isLow, g, k.S.Value(Op.ISub, k.U32, g, k.C(4)));
            uint bG = k.ByteAt(wb, k.IAdd(scBase, g));
            uint bG4 = k.ByteAt(wb, k.IAdd(scBase, k.IAdd(g, k.C(4))));
            uint bGm4 = k.ByteAt(wb, k.IAdd(scBase, gm4));
            uint scLo = k.And(bG, k.C(63)), mLo = k.And(bG4, k.C(63));
            uint scHi = k.Or(k.And(bG4, k.C(0xF)), k.Shl(k.Shr(bGm4, k.C(6)), k.C(4)));
            uint mHi = k.Or(k.Shr(bG4, k.C(4)), k.Shl(k.Shr(bG, k.C(6)), k.C(4)));
            uint sc = k.Select(k.U32, isLow, scLo, scHi), m = k.Select(k.U32, isLow, mLo, mHi);

            // Quants: chunk j = g/2 of 32 bytes; low nibble for even g, high for odd.
            uint j = k.Shr(g, k.C(1));
            uint nibShift = k.IMul(k.And(g, k.C(1)), k.C(4));
            uint qsWord0 = type == GgmlType.Q4_K ? 4u : 12u;
            uint qsBase = k.IAdd(wBase, k.IAdd(k.C(qsWord0), k.IMul(j, k.C(8))));
            uint isum = k.CI(0), qsum = k.CI(0);
            for (uint w = 0; w < 8; w++)
            {
                uint qw = k.Ld(wb, k.IAdd(qsBase, k.C(w)));
                uint xw = k.Ld(kq, k.IAdd(xBase, k.C(w)));
                uint hw = type == GgmlType.Q5_K ? k.Ld(wb, k.IAdd(wBase, k.C(4 + w))) : 0; // qh bytes l = 4w..4w+3
                for (int kk = 0; kk < 4; kk++)
                {
                    uint q = k.And(k.Shr(qw, k.IAdd(k.C((uint)(8 * kk)), nibShift)), k.C(0xF));
                    if (type == GgmlType.Q5_K)
                    {
                        uint hb = k.And(k.Shr(hw, k.IAdd(k.C((uint)(8 * kk)), g)), k.C(1));
                        q = k.IAdd(q, k.IMul(hb, k.C(16)));
                    }
                    uint xv = k.SByte(xw, kk);
                    isum = k.IAddS(isum, k.IMulS(k.AsI(q), xv));
                    qsum = k.IAddS(qsum, xv);
                }
            }
            uint dd = k.FMul(d, yd), dm = k.FMul(dmin, yd);
            uint a = k.FMul(dd, k.SToF(k.IMulS(k.AsI(sc), isum)));
            uint b2 = k.FMul(dm, k.SToF(k.IMulS(k.AsI(m), qsum)));
            return k.FSub(a, b2);
        }

        // Q6_K (padded to 53 words): 32-element group g = 4n + part. Returns d*yd*(sc0*isum0 + sc1*isum1).
        static uint Q6Group(KB k, KB.Buf wb, KB.Buf kq, uint wBase, uint xBase, uint g, uint yd)
        {
            uint d = k.HalfOf(k.Ld(wb, k.IAdd(wBase, k.C(52))), false);
            uint n = k.Shr(g, k.C(2)), part = k.And(g, k.C(3));
            uint qlBase = k.IAdd(wBase, k.IAdd(k.IMul(n, k.C(16)), k.IMul(k.And(part, k.C(1)), k.C(8))));
            uint nibShift = k.IMul(k.Shr(part, k.C(1)), k.C(4));
            uint qhBase = k.IAdd(wBase, k.IAdd(k.C(32), k.IMul(n, k.C(8))));
            uint qhShift = k.IMul(part, k.C(2));

            // Scales (int8) for elements 128n + 32part + [0,16) and [16,32): indices 8n + 2part (+1), at byte 192.
            uint scIdx = k.IAdd(k.IMul(n, k.C(8)), k.IMul(part, k.C(2)));
            uint scByte = k.IAdd(k.IMul(wBase, k.C(4)), k.IAdd(k.C(192), scIdx));
            uint sc0 = Sext8(k, k.ByteAt(wb, scByte)), sc1 = Sext8(k, k.ByteAt(wb, k.IAdd(scByte, k.C(1))));

            uint isum0 = k.CI(0), isum1 = k.CI(0);
            for (uint w = 0; w < 8; w++)
            {
                uint qlw = k.Ld(wb, k.IAdd(qlBase, k.C(w)));
                uint qhw = k.Ld(wb, k.IAdd(qhBase, k.C(w)));
                uint xw = k.Ld(kq, k.IAdd(xBase, k.C(w)));
                for (int kk = 0; kk < 4; kk++)
                {
                    uint lo = k.And(k.Shr(qlw, k.IAdd(k.C((uint)(8 * kk)), nibShift)), k.C(0xF));
                    uint hi = k.And(k.Shr(qhw, k.IAdd(k.C((uint)(8 * kk)), qhShift)), k.C(3));
                    uint q = k.ISubS(k.AsI(k.Or(lo, k.Shl(hi, k.C(4)))), k.CI(32));
                    uint p = k.IMulS(q, k.SByte(xw, kk));
                    if (w < 4) isum0 = k.IAddS(isum0, p); else isum1 = k.IAddS(isum1, p);
                }
            }
            uint isum = k.IAddS(k.IMulS(sc0, isum0), k.IMulS(sc1, isum1));
            return k.FMul(k.FMul(d, yd), k.SToF(isum));
        }

        static uint Sext8(KB k, uint b)
        {
            return k.S.Value(Op.ShiftRightArithmetic, k.I32, k.AsI(k.Shl(b, k.C(24))), k.C(24));
        }
    }
}
