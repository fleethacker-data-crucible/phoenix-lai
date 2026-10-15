// GPU kernels for Q3_K, IQ2_XXS, IQ2_S and IQ3_S weights with Q8_K activations. C# 5 only.
// Blocks are repacked for the GPU as (payload words, f32 scale): the fp16 d is moved out of the block so
// the remaining bytes start word-aligned. Integer math mirrors CpuRefIQuants / CpuRefKQuants.
using System;

namespace Phoenix
{
    public static unsafe class KernelsIQ
    {
        // Offsets (in u32 words) inside the shared lookup-table buffer.
        public const uint TabKsigns = 0;          // 128 entries, one byte value per word
        public const uint TabIq2xxs = 128;        // 256 x u64 = 512 words
        public const uint TabIq2s = 128 + 512;    // 1024 x u64 = 2048 words
        public const uint TabIq3s = 128 + 512 + 2048; // 512 x u32
        public const uint TabWords = 128 + 512 + 2048 + 512;

        public static bool Handles(GgmlType t)
        {
            return t == GgmlType.Q3_K || t == GgmlType.IQ2_XXS || t == GgmlType.IQ2_S || t == GgmlType.IQ3_S;
        }

        // Payload bytes per block on the GPU (block minus its fp16 d), always a multiple of 4.
        public static int PayloadBytes(GgmlType t)
        {
            switch (t)
            {
                case GgmlType.Q3_K: return 108;
                case GgmlType.IQ2_XXS: return 64;
                case GgmlType.IQ2_S: return 80;
                case GgmlType.IQ3_S: return 108;
            }
            throw new NotSupportedException(GgmlTypes.Name(t));
        }

        // Offset of the fp16 d inside the original block, and of the payload.
        public static int DOffset(GgmlType t) { return t == GgmlType.Q3_K ? 108 : 0; }
        public static int PayloadOffset(GgmlType t) { return t == GgmlType.Q3_K ? 0 : 2; }

        public static uint[] Tables()
        {
            uint[] w = new uint[TabWords];
            for (int i = 0; i < 128; i++) w[TabKsigns + i] = IqTables.KsignsIq2xs[i];
            for (int i = 0; i < 256; i++) { w[TabIq2xxs + 2 * i] = (uint)IqTables.Iq2xxsGrid[i]; w[TabIq2xxs + 2 * i + 1] = (uint)(IqTables.Iq2xxsGrid[i] >> 32); }
            for (int i = 0; i < 1024; i++) { w[TabIq2s + 2 * i] = (uint)IqTables.Iq2sGrid[i]; w[TabIq2s + 2 * i + 1] = (uint)(IqTables.Iq2sGrid[i] >> 32); }
            for (int i = 0; i < 512; i++) w[TabIq3s + i] = IqTables.Iq3sGrid[i];
            return w;
        }

        // y[r] = dot(W[r], x). One workgroup per row; threads take 32-element groups.
        // bindings: 0 w u32 (payloads), 1 wd f32 (per block), 2 kq u32, 3 kd f32, 4 y f32, 5 tables u32. push: rows, nb, groupsX
        public static uint[] MatVec(GgmlType type)
        {
            uint pw = (uint)PayloadBytes(type) / 4;
            KB k = new KB(Kernels.Wg);
            KB.Buf wb = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.F32, true), kq = k.Buffer(2, k.U32, true),
                kd = k.Buffer(3, k.F32, true), y = k.Buffer(4, k.F32, false), tab = k.Buffer(5, k.U32, true);
            k.Push(4);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.Wg);
            k.Begin();
            uint itv = k.Local(k.U32), sum = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), gx = k.PushU(2), addTo = k.PushU(3);
            uint r = k.IAdd(k.GroupX(), k.IMul(k.GroupY(), gx));
            uint lid = k.LocalId();
            k.Set(sum, k.CF(0));
            k.If(k.ULt(r, rows), delegate
            {
                k.For(itv, lid, k.IMul(nb, k.C(8)), k.C(Kernels.Wg), delegate
                {
                    uint item = k.Get(itv, k.U32);
                    uint b = k.Shr(item, k.C(3)), g = k.And(item, k.C(7));
                    uint blk = k.IAdd(k.IMul(r, nb), b);
                    uint wBase = k.IMul(blk, k.C(pw));                          // first payload word of the block
                    uint xBase = k.IAdd(k.IMul(b, k.C(64)), k.IMul(g, k.C(8))); // first kq word of the 32 activations
                    uint dyd = k.FMul(k.Ld(wd, blk), k.Ld(kd, b));
                    uint contrib;
                    switch (type)
                    {
                        case GgmlType.Q3_K: contrib = Q3K(k, wb, kq, wBase, xBase, g, dyd); break;
                        case GgmlType.IQ2_XXS: contrib = IQ2XXS(k, wb, kq, tab, wBase, xBase, g, dyd); break;
                        case GgmlType.IQ2_S: contrib = IQ2S(k, wb, kq, tab, wBase, xBase, g, dyd); break;
                        default: contrib = IQ3S(k, wb, kq, tab, wBase, xBase, g, dyd); break;
                    }
                    k.Set(sum, k.FAdd(k.Get(sum, k.F32), contrib));
                });
            });
            k.St(sh, lid, k.Get(sum, k.F32));
            k.Reduce(sh, lid, Kernels.Wg, k.FAdd);
            k.If(k.ULt(r, rows), delegate
            {
                k.If(k.IEq(lid, k.C(0)), delegate { k.StoreRow(y, r, k.Ld(sh, k.C(0)), addTo); });
            });
            return k.Finish();
        }

        static uint ByteOf(KB k, KB.Buf b, uint wBase, uint byteOffset)
        {
            return k.ByteAt(b, k.IAdd(k.IMul(wBase, k.C(4)), byteOffset));
        }

        // Sign bit j of a byte -> +1 / -1 as int.
        static uint SignOf(KB k, uint signsByte, int j)
        {
            uint bit = k.And(k.Shr(signsByte, k.C((uint)j)), k.C(1));
            return k.ISubS(k.CI(1), k.AsI(k.IMul(bit, k.C(2))));
        }

        // Q3_K group g: elements 32g..32g+31. Payload: hmask[32] at 0, qs[64] at 32, scales[12] at 96.
        static uint Q3K(KB k, KB.Buf wb, KB.Buf kq, uint wBase, uint xBase, uint g, uint dyd)
        {
            uint j128 = k.Shr(g, k.C(2));
            uint shift = k.IMul(k.And(g, k.C(3)), k.C(2));
            uint qsBase = k.IAdd(wBase, k.IAdd(k.C(8), k.IMul(j128, k.C(8))));
            uint isum0 = k.CI(0), isum1 = k.CI(0);
            for (uint w = 0; w < 8; w++)
            {
                uint qw = k.Ld(wb, k.IAdd(qsBase, k.C(w)));
                uint hw = k.Ld(wb, k.IAdd(wBase, k.C(w)));        // hmask bytes l = 4w..4w+3
                uint xw = k.Ld(kq, k.IAdd(xBase, k.C(w)));
                for (int kk = 0; kk < 4; kk++)
                {
                    uint q = k.And(k.Shr(qw, k.IAdd(k.C((uint)(8 * kk)), shift)), k.C(3));
                    uint hbit = k.And(k.Shr(hw, k.IAdd(k.C((uint)(8 * kk)), g)), k.C(1));
                    uint v = k.ISubS(k.AsI(q), k.AsI(k.IMul(k.S.Value(Op.BitwiseXor, k.U32, hbit, k.C(1)), k.C(4))));
                    uint p = k.IMulS(v, k.SByte(xw, kk));
                    if (w < 4) isum0 = k.IAddS(isum0, p); else isum1 = k.IAddS(isum1, p);
                }
            }
            uint sc0 = ScaleQ3K(k, wb, wBase, k.IMul(g, k.C(2)));
            uint sc1 = ScaleQ3K(k, wb, wBase, k.IAdd(k.IMul(g, k.C(2)), k.C(1)));
            uint isum = k.IAddS(k.IMulS(sc0, isum0), k.IMulS(sc1, isum1));
            return k.FMul(dyd, k.SToF(isum));
        }

        // CpuRef.ScaleQ3K(j) - 32, as int.
        static uint ScaleQ3K(KB k, KB.Buf wb, uint wBase, uint j)
        {
            uint lowHalf = k.ULt(j, k.C(8));
            uint idx = k.Select(k.U32, lowHalf, j, k.S.Value(Op.ISub, k.U32, j, k.C(8)));
            uint nshift = k.Select(k.U32, lowHalf, k.C(0), k.C(4));
            uint low = k.And(k.Shr(ByteOf(k, wb, wBase, k.IAdd(k.C(96), idx)), nshift), k.C(0xF));
            uint hb = ByteOf(k, wb, wBase, k.IAdd(k.C(104), k.And(j, k.C(3))));
            uint high = k.And(k.Shr(hb, k.IMul(k.Shr(j, k.C(2)), k.C(2))), k.C(3));
            return k.ISubS(k.AsI(k.Or(low, k.Shl(high, k.C(4)))), k.CI(32));
        }

        // IQ2_XXS group g: payload words 2g (grid indices) and 2g+1 (signs + scale).
        static uint IQ2XXS(KB k, KB.Buf wb, KB.Buf kq, KB.Buf tab, uint wBase, uint xBase, uint g, uint dyd)
        {
            uint a0 = k.Ld(wb, k.IAdd(wBase, k.IMul(g, k.C(2))));
            uint a1 = k.Ld(wb, k.IAdd(wBase, k.IAdd(k.IMul(g, k.C(2)), k.C(1))));
            uint ls = k.IAdd(k.IMul(k.Shr(a1, k.C(28)), k.C(2)), k.C(1));
            uint sumi = k.CI(0);
            for (int l = 0; l < 4; l++)
            {
                uint gi = k.And(k.Shr(a0, k.C((uint)(8 * l))), k.C(0xFF));
                uint gBase = k.IAdd(k.C(TabIq2xxs), k.IMul(gi, k.C(2)));
                uint gLo = k.Ld(tab, gBase), gHi = k.Ld(tab, k.IAdd(gBase, k.C(1)));
                uint signs = k.Ld(tab, k.IAdd(k.C(TabKsigns), k.And(k.Shr(a1, k.C((uint)(7 * l))), k.C(127))));
                sumi = k.IAddS(sumi, Grid8(k, kq, xBase, (uint)(2 * l), gLo, gHi, signs));
            }
            return k.FMul(k.FMul(dyd, k.SToF(k.IMulS(sumi, k.AsI(ls)))), k.CF(0.125f));
        }

        // IQ2_S group g. Payload: qs[32] at 0, signs[32] at 32, qh[8] at 64, scales[8] at 72.
        static uint IQ2S(KB k, KB.Buf wb, KB.Buf kq, KB.Buf tab, uint wBase, uint xBase, uint g, uint dyd)
        {
            uint qh = ByteOf(k, wb, wBase, k.IAdd(k.C(64), g));
            uint sc = ByteOf(k, wb, wBase, k.IAdd(k.C(72), g));
            uint ls1 = k.IAdd(k.IMul(k.And(sc, k.C(0xF)), k.C(2)), k.C(1));
            uint ls2 = k.IAdd(k.IMul(k.Shr(sc, k.C(4)), k.C(2)), k.C(1));
            uint qsWord = k.Ld(wb, k.IAdd(wBase, g));                 // qs[4g .. 4g+3]
            uint sgWord = k.Ld(wb, k.IAdd(wBase, k.IAdd(k.C(8), g))); // signs[4g .. 4g+3]
            uint sum1 = k.CI(0), sum2 = k.CI(0);
            for (int l = 0; l < 4; l++)
            {
                uint qsB = k.And(k.Shr(qsWord, k.C((uint)(8 * l))), k.C(0xFF));
                uint hiBits = k.And(k.Shl(qh, k.C((uint)(8 - 2 * l))), k.C(0x300));
                uint gBase = k.IAdd(k.C(TabIq2s), k.IMul(k.Or(qsB, hiBits), k.C(2)));
                uint gLo = k.Ld(tab, gBase), gHi = k.Ld(tab, k.IAdd(gBase, k.C(1)));
                uint signs = k.And(k.Shr(sgWord, k.C((uint)(8 * l))), k.C(0xFF));
                uint s = Grid8(k, kq, xBase, (uint)(2 * l), gLo, gHi, signs);
                if (l < 2) sum1 = k.IAddS(sum1, s); else sum2 = k.IAddS(sum2, s);
            }
            uint isum = k.IAddS(k.IMulS(k.AsI(ls1), sum1), k.IMulS(k.AsI(ls2), sum2));
            return k.FMul(k.FMul(dyd, k.SToF(isum)), k.CF(0.125f));
        }

        // IQ3_S group g. Payload: qs[64] at 0, qh[8] at 64, signs[32] at 72, scales[4] at 104.
        static uint IQ3S(KB k, KB.Buf wb, KB.Buf kq, KB.Buf tab, uint wBase, uint xBase, uint g, uint dyd)
        {
            uint h = ByteOf(k, wb, wBase, k.IAdd(k.C(64), g));
            uint scB = ByteOf(k, wb, wBase, k.IAdd(k.C(104), k.Shr(g, k.C(1))));
            uint nib = k.And(k.Shr(scB, k.IMul(k.And(g, k.C(1)), k.C(4))), k.C(0xF));
            uint ls = k.IAdd(k.IMul(nib, k.C(2)), k.C(1));
            uint sgWord = k.Ld(wb, k.IAdd(wBase, k.IAdd(k.C(18), g))); // signs[4g .. 4g+3] at byte 72 + 4g
            uint sumi = k.CI(0);
            for (int l = 0; l < 4; l++)
            {
                uint qsW = k.Ld(wb, k.IAdd(wBase, k.IAdd(k.IMul(g, k.C(2)), k.C((uint)(l / 2))))); // qs[8g + 2l], [8g + 2l + 1]
                uint q1 = k.And(k.Shr(qsW, k.C((uint)(16 * (l % 2)))), k.C(0xFF));
                uint q2 = k.And(k.Shr(qsW, k.C((uint)(16 * (l % 2) + 8))), k.C(0xFF));
                uint i1 = k.Or(q1, k.And(k.Shl(h, k.C((uint)(8 - 2 * l))), k.C(256)));
                uint i2 = k.Or(q2, k.And(k.Shl(h, k.C((uint)(7 - 2 * l))), k.C(256)));
                uint g1 = k.Ld(tab, k.IAdd(k.C(TabIq3s), i1)), g2 = k.Ld(tab, k.IAdd(k.C(TabIq3s), i2));
                uint signs = k.And(k.Shr(sgWord, k.C((uint)(8 * l))), k.C(0xFF));
                sumi = k.IAddS(sumi, Grid8(k, kq, xBase, (uint)(2 * l), g1, g2, signs));
            }
            return k.FMul(dyd, k.SToF(k.IMulS(sumi, k.AsI(ls))));
        }

        // Sum over 8 activations (kq words xBase+xw, xBase+xw+1) of grid byte j * x_j * sign_j; grid bytes 0..3 in lo, 4..7 in hi.
        static uint Grid8(KB k, KB.Buf kq, uint xBase, uint xw, uint lo, uint hi, uint signs)
        {
            uint x0 = k.Ld(kq, k.IAdd(xBase, k.C(xw))), x1 = k.Ld(kq, k.IAdd(xBase, k.C(xw + 1)));
            uint s = k.CI(0);
            for (int j = 0; j < 8; j++)
            {
                uint word = j < 4 ? lo : hi, xword = j < 4 ? x0 : x1;
                uint gv = k.AsI(k.And(k.Shr(word, k.C((uint)(8 * (j & 3)))), k.C(0xFF)));
                uint prod = k.IMulS(k.IMulS(gv, k.SByte(xword, j & 3)), SignOf(k, signs, j));
                s = k.IAddS(s, prod);
            }
            return s;
        }
    }
}
