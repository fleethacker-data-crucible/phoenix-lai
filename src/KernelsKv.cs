// Quantized KV cache: 8-bit (Q8) or 4-bit (Q4) values with one fp16 scale per 32 values of a head vector, so long
// contexts take half / a quarter of the fp16 cache's memory. C# 5 only.
//
// Layout (same element order as the fp16 cache in KernelsAttention, `ring` slots, ring a multiple of 8):
//   K transposed: value (i, slot) at element index i * ring + slot (i = kvh * headDim + d)
//   V:            value (slot, i) at element index slot * kvDim + i
//   elements are signed bytes (Q8, 4 per u32 word) or signed nibbles (Q4, 8 per word, stored as q + 8)
//   after the data, fp16 scales (2 per word) at half index scaleBase + ...:
//     K: (i / 32) * ring + slot        V: slot * (kvDim / 32) + i / 32
//   scaleBase = 2 * data words = ring * kvDim / 2 (Q8) or ring * kvDim / 4 (Q4)
// A value is q * scale; the scale is amax / 127 (Q8) or amax / 7 (Q4) of the f16-rounded block, itself fp16-rounded.
// Writers own whole words: one thread per (token, 64 values = two blocks) — a V block pair fills whole words and one
// scale word; K values of different slots share words, so the prefill store runs in passes by slot % (4 or 8).
using System;

namespace Phoenix
{
    public enum KvType { F16 = 0, Q8 = 1, Q4 = 2 }

    public static class KernelsKv
    {
        public static string Name(KvType t) { return t == KvType.Q8 ? "q8" : t == KvType.Q4 ? "q4" : "f16"; }

        // Bytes of one cache (K or V) with `ring` slots of kvDim values.
        public static ulong CacheBytes(KvType t, int ring, int kvDim)
        {
            ulong n = (ulong)ring * (ulong)kvDim;
            if (t == KvType.F16) return n * 2;
            ulong data = t == KvType.Q8 ? n : n / 2;
            return data + n / 32 * 2;
        }

        // Bytes per cached token (K and V together) for kvDim values per layer.
        public static double BytesPerValue(KvType t) { return t == KvType.F16 ? 2.0 : t == KvType.Q8 ? 1.0 + 2.0 / 32 : 0.5 + 2.0 / 32; }

        // Prefill store passes (tokens whose slot % passes == pass).
        public static uint Passes(KvType t) { return t == KvType.Q8 ? 4u : t == KvType.Q4 ? 8u : 2u; }

        static uint ScaleBase(KB k, KvType t, uint ring, uint kvDim)
        {
            uint n = k.IMul(ring, kvDim);
            return t == KvType.Q8 ? k.Shr(n, k.C(1)) : k.Shr(n, k.C(2));
        }

        // Quantized element e (element index) of a cache, times its scale (half index si), as f32.
        static uint Elem(KB k, KvType t, KB.Buf b, uint e, uint si)
        {
            uint q;
            if (t == KvType.Q8)
            {
                uint w = k.Ld(b, k.Shr(e, k.C(2)));
                uint sh = k.IMul(k.And(e, k.C(3)), k.C(8));
                q = k.S.Value(Op.ShiftRightArithmetic, k.I32, k.AsI(k.Shl(w, k.S.Value(Op.ISub, k.U32, k.C(24), sh))), k.C(24));
            }
            else
            {
                uint w = k.Ld(b, k.Shr(e, k.C(3)));
                uint nib = k.And(k.Shr(w, k.IMul(k.And(e, k.C(7)), k.C(4))), k.C(15));
                q = k.ISubS(k.AsI(nib), k.CI(8));
            }
            return k.FMul(k.SToF(q), KernelsAttention.HalfAt(k, b, si));
        }

        // K value (i, slot) as f32 for any cache type.
        public static uint K(KB k, KvType t, KB.Buf kc, uint i, uint slot, uint ring, uint kvDim)
        {
            uint e = k.IAdd(k.IMul(i, ring), slot);
            if (t == KvType.F16) return KernelsAttention.HalfAt(k, kc, e);
            uint si = k.IAdd(ScaleBase(k, t, ring, kvDim), k.IAdd(k.IMul(k.Shr(i, k.C(5)), ring), slot));
            return Elem(k, t, kc, e, si);
        }

        // V value (slot, i) as f32 for any cache type.
        public static uint V(KB k, KvType t, KB.Buf vc, uint slot, uint i, uint ring, uint kvDim)
        {
            uint e = k.IAdd(k.IMul(slot, kvDim), i);
            if (t == KvType.F16) return KernelsAttention.HalfAt(k, vc, e);
            uint si = k.IAdd(ScaleBase(k, t, ring, kvDim), k.IAdd(k.IMul(slot, k.Shr(kvDim, k.C(5))), k.Shr(i, k.C(5))));
            return Elem(k, t, vc, e, si);
        }


        // fp16-rounded scale for a block whose largest |value| is amax, and its inverse (0 for an all-zero block).
        static void Scale(KB k, KvType t, uint amax, out uint d, out uint inv)
        {
            d = k.F16(k.FMul(amax, k.CF(t == KvType.Q8 ? 1f / 127f : 1f / 7f)));
            inv = k.Select(k.F32, k.S.Value(Op.FOrdGreaterThan, k.Bool, d, k.CF(0)), k.FDiv(k.CF(1f), d), k.CF(0));
        }

        static uint Quant(KB k, KvType t, uint x, uint inv)
        {
            float lim = t == KvType.Q8 ? 127f : 7f;
            uint r = k.RoundEven(k.FMul(x, inv));
            r = k.S.Glsl(k.F32, Spv.GlslFMin, k.FMax(r, k.CF(-lim)), k.CF(lim));
            return k.FToS(r);
        }

        // Writes the 64 values src[0..63] (K and V of one token, values i0 .. i0+63) into slot `slot`; K and V may use
        // different formats (kt, vt; F16 = the plain packed fp16 cache).
        static void StoreBlocks(KB k, KvType kt, KvType vt, KB.Buf kk, KB.Buf v, uint src, KB.Buf kc, KB.Buf vc, uint i0, uint slot, uint ring, uint kvDim)
        {
            uint[] vScale = new uint[2];
            for (uint blk = 0; blk < 2; blk++)
            {
                uint b0 = k.IAdd(i0, k.C(blk * 32));
                uint[] kx = new uint[32], vx = new uint[32];
                uint kmax = k.CF(0), vmax = k.CF(0);
                for (uint e = 0; e < 32; e++)
                {
                    kx[e] = k.F16(k.Ld(kk, k.IAdd(src, k.C(blk * 32 + e))));
                    vx[e] = k.F16(k.Ld(v, k.IAdd(src, k.C(blk * 32 + e))));
                    kmax = k.FMax(kmax, k.Abs(kx[e]));
                    vmax = k.FMax(vmax, k.Abs(vx[e]));
                }
                // K: one element per row of the transposed cache (read-modify-write of its word)
                if (kt == KvType.F16)
                    for (uint e = 0; e < 32; e++) k.StHalf(kc, k.IAdd(k.IMul(k.IAdd(b0, k.C(e)), ring), slot), kx[e]);
                else
                {
                    uint kd, kinv;
                    Scale(k, kt, kmax, out kd, out kinv);
                    for (uint e = 0; e < 32; e++)
                    {
                        uint idx = k.IAdd(k.IMul(k.IAdd(b0, k.C(e)), ring), slot);
                        uint q = Quant(k, kt, kx[e], kinv);
                        if (kt == KvType.Q8)
                        {
                            uint wi = k.Shr(idx, k.C(2)), sh = k.IMul(k.And(idx, k.C(3)), k.C(8));
                            uint mask = k.Shl(k.C(0xFF), sh);
                            k.St(kc, wi, k.Or(k.And(k.Ld(kc, wi), k.S.Value(Op.Not, k.U32, mask)), k.Shl(k.And(k.AsU(q), k.C(0xFF)), sh)));
                        }
                        else
                        {
                            uint wi = k.Shr(idx, k.C(3)), sh = k.IMul(k.And(idx, k.C(7)), k.C(4));
                            uint mask = k.Shl(k.C(0xF), sh);
                            uint nib = k.And(k.AsU(k.IAddS(q, k.CI(8))), k.C(0xF));
                            k.St(kc, wi, k.Or(k.And(k.Ld(kc, wi), k.S.Value(Op.Not, k.U32, mask)), k.Shl(nib, sh)));
                        }
                    }
                    k.StHalf(kc, k.IAdd(ScaleBase(k, kt, ring, kvDim), k.IAdd(k.IMul(k.Shr(b0, k.C(5)), ring), slot)), kd);
                }
                // V: the block is contiguous: 16 (fp16), 8 (Q8) or 4 (Q4) whole words
                uint vBase = k.IAdd(k.IMul(slot, kvDim), b0);   // element index, a multiple of 32
                if (vt == KvType.F16)
                {
                    uint v2 = k.S.TypeVector(k.F32, 2);
                    for (uint w = 0; w < 16; w++)
                        k.St(vc, k.IAdd(k.Shr(vBase, k.C(1)), k.C(w)), k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, vx[2 * w], vx[2 * w + 1])));
                    continue;
                }
                uint vd, vinv;
                Scale(k, vt, vmax, out vd, out vinv);
                vScale[blk] = vd;
                uint per = vt == KvType.Q8 ? 4u : 8u;
                for (uint w = 0; w < 32 / per; w++)
                {
                    uint word = k.C(0);
                    for (uint j = 0; j < per; j++)
                    {
                        uint q = Quant(k, vt, vx[w * per + j], vinv);
                        uint bits = vt == KvType.Q8 ? k.And(k.AsU(q), k.C(0xFF)) : k.And(k.AsU(k.IAddS(q, k.CI(8))), k.C(0xF));
                        word = k.Or(word, k.Shl(bits, k.C(j * (32 / per))));
                    }
                    k.St(vc, k.IAdd(k.Shr(vBase, k.C(vt == KvType.Q8 ? 2u : 3u)), k.C(w)), word);
                }
            }
            if (vt == KvType.F16) return;
            // both V scales of the pair share one word (block index i0/32 is even)
            uint vs = k.IAdd(ScaleBase(k, vt, ring, kvDim), k.IAdd(k.IMul(slot, k.Shr(kvDim, k.C(5))), k.Shr(i0, k.C(5))));
            k.St(vc, k.Shr(vs, k.C(1)), k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, k.S.TypeVector(k.F32, 2), vScale[0], vScale[1])));
        }

        // Decode store of the current position (params[0]): one thread per 64 values.
        // bindings: 0 k, 1 v, 2 kc, 3 vc, 4 params. push: kvDim, ring  (as KernelsAttention.KvStore)
        public static uint[] Store(KvType t) { return Store(t, t); }

        public static uint[] Store(KvType kt, KvType vt)
        {
            if (kt == KvType.F16 && vt == KvType.F16) return KernelsAttention.KvStore();
            KB k = new KB(Kernels.Wg);
            KB.Buf kk = k.Buffer(0, k.F32, true), v = k.Buffer(1, k.F32, true), kc = k.Buffer(2, k.U32, false), vc = k.Buffer(3, k.U32, false);
            KB.Buf prm = k.Buffer(4, k.U32, true);
            k.Push(2);
            k.Begin();
            uint g = k.GlobalId();
            uint kvDim = k.PushU(0), ring = k.PushU(1), pos = k.Ld(prm, k.C(0));
            k.If(k.ULt(g, k.Shr(kvDim, k.C(6))), delegate
            {
                uint i0 = k.Shl(g, k.C(6));
                StoreBlocks(k, kt, vt, kk, v, i0, kc, vc, i0, k.UMod(pos, ring), ring, kvDim);
            });
            return k.Finish();
        }

        // Prefill store of T positions p0..: one thread per (token, 64 values); called Passes(t) times with pass 0..,
        // each writing the tokens whose slot % passes == pass (barrier between calls).
        // bindings: 0 k [T * kvDim], 1 v, 2 kc, 3 vc. push: kvDim, ring, p0, T, pass  (as KernelsPrefill.KvStoreRows)
        public static uint[] StoreRows(KvType t) { return StoreRows(t, t); }

        public static uint[] StoreRows(KvType kt, KvType vt)
        {
            if (kt == KvType.F16 && vt == KvType.F16) return KernelsPrefill.KvStoreRows();
            uint passes = Passes(kt);   // K shares words across slots; V never does
            KB k = new KB(Kernels.Wg);
            KB.Buf kk = k.Buffer(0, k.F32, true), v = k.Buffer(1, k.F32, true), kc = k.Buffer(2, k.U32, false), vc = k.Buffer(3, k.U32, false);
            k.Push(5);
            k.Begin();
            uint g = k.GlobalId();
            uint kvDim = k.PushU(0), ring = k.PushU(1), p0 = k.PushU(2), T = k.PushU(3), pass = k.PushU(4);
            uint per = k.Shr(kvDim, k.C(6));
            uint tok = k.UDiv(g, per);
            uint slot = k.UMod(k.IAdd(p0, tok), ring);
            k.If(k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(g, k.IMul(per, T)), k.IEq(k.UMod(slot, k.C(passes)), pass)), delegate
            {
                uint i0 = k.Shl(k.UMod(g, per), k.C(6));
                StoreBlocks(k, kt, vt, kk, v, k.IAdd(k.IMul(tok, kvDim), i0), kc, vc, i0, slot, ring, kvDim);
            });
            return k.Finish();
        }
    }
}
