// Instant Recall kernels: copy a range of positions out of an fp16 KV ring cache (Gather) and back into a cache at
// another position (Scatter), re-rotating K by the position difference. RoPE is a rotation by pos * theta per
// dimension pair, so a key computed at position s becomes the key at position p by one more rotation by (p - s).
// C# 5 only. Packed fp16 cache layout as KernelsAttention (K transposed: i * ring + slot; V: slot * kvDim + i).
//
// Snapshot layout (u32 words, fp16 pairs), m positions padded to mPad (even):
//   K: [kvDim][mPad] halves at i * mPad + t         (words 0 .. kvDim * mPad / 2)
//   V: [m][kvDim] halves at t * kvDim + i, after K
using System;

namespace Phoenix
{
    public static class KernelsRecall
    {
        // Gather positions src0 .. src0+m-1. bindings: 0 kc, 1 vc, 2 snap (out). push: kvDim, ring, src0, m, mPad
        public static uint[] Gather()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf kc = k.Buffer(0, k.U32, true), vc = k.Buffer(1, k.U32, true), snap = k.Buffer(2, k.U32, false);
            k.Push(5);
            k.Begin();
            uint g = k.GlobalId();
            uint kvDim = k.PushU(0), ring = k.PushU(1), src0 = k.PushU(2), m = k.PushU(3), mPad = k.PushU(4);
            uint halfPad = k.Shr(mPad, k.C(1));
            uint kWords = k.IMul(kvDim, halfPad), vWords = k.Shr(k.IMul(m, kvDim), k.C(1));
            uint v2 = k.S.TypeVector(k.F32, 2);
            k.If(k.ULt(g, kWords), delegate
            {
                uint i = k.UDiv(g, halfPad), t0 = k.IMul(k.UMod(g, halfPad), k.C(2)), t1 = k.IAdd(t0, k.C(1));
                uint a = KernelsAttention.HalfAt(k, kc, k.IAdd(k.IMul(i, ring), k.UMod(k.IAdd(src0, t0), ring)));
                uint b = k.Select(k.F32, k.ULt(t1, m), KernelsAttention.HalfAt(k, kc, k.IAdd(k.IMul(i, ring), k.UMod(k.IAdd(src0, t1), ring))), k.CF(0));
                k.St(snap, g, k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, a, b)));
            });
            k.If(k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.UGreaterThanEqual, k.Bool, g, kWords), k.ULt(g, k.IAdd(kWords, vWords))), delegate
            {
                uint w = k.S.Value(Op.ISub, k.U32, g, kWords);          // word of [t][kvDim]
                uint pairs = k.Shr(kvDim, k.C(1));
                uint t = k.UDiv(w, pairs), ip = k.UMod(w, pairs);
                uint slot = k.UMod(k.IAdd(src0, t), ring);
                k.St(snap, g, k.Ld(vc, k.IAdd(k.Shr(k.IMul(slot, kvDim), k.C(1)), ip)));
            });
            return k.Finish();
        }

        // Scatter a snapshot to positions dst0 .. dst0+m-1, rotating K by the table cs (cos, sin per dimension pair for
        // the position difference; NeoX pairing (j, j + half) within each head, dims >= 2*half unrotated). Called twice,
        // parity 0 then 1: each pass writes only positions whose slot has that parity (K slots share fp16 words); V and
        // the unrotated dims are written in pass 0 / both passes as needed.
        // bindings: 0 snap, 1 cs, 2 kc, 3 vc. push: kvDim, ring, dst0, m, mPad, headDim, half, parity
        public static uint[] Scatter()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf snap = k.Buffer(0, k.U32, true), cs = k.Buffer(1, k.F32, true), kc = k.Buffer(2, k.U32, false), vc = k.Buffer(3, k.U32, false);
            k.Push(8);
            k.Begin();
            uint g = k.GlobalId();
            uint kvDim = k.PushU(0), ring = k.PushU(1), dst0 = k.PushU(2), m = k.PushU(3), mPad = k.PushU(4);
            uint headDim = k.PushU(5), half = k.PushU(6), parity = k.PushU(7);
            uint nKv = k.UDiv(kvDim, headDim);
            // K: one thread per (kv head, dims pair j, position t): pairs j < half rotate, the rest (if any) copy
            uint perHead = k.Shr(headDim, k.C(1));                    // threads per head per position: headDim / 2
            uint kThreads = k.IMul(k.IMul(nKv, perHead), m);
            k.If(k.ULt(g, kThreads), delegate
            {
                uint t = k.UDiv(g, k.IMul(nKv, perHead)), r = k.UMod(g, k.IMul(nKv, perHead));
                uint h = k.UDiv(r, perHead), j = k.UMod(r, perHead);
                uint slot = k.UMod(k.IAdd(dst0, t), ring);
                k.If(k.IEq(k.And(slot, k.C(1)), parity), delegate
                {
                    uint rot = k.ULt(j, half);
                    // rotated pair: dims j and j + half; unrotated: dims 2*half + 2*(j-half) and +1
                    uint dA = k.Select(k.U32, rot, j, k.IAdd(k.IMul(half, k.C(2)), k.IMul(k.S.Value(Op.ISub, k.U32, j, half), k.C(2))));
                    uint dB = k.Select(k.U32, rot, k.IAdd(j, half), k.IAdd(dA, k.C(1)));
                    uint iA = k.IAdd(k.IMul(h, headDim), dA), iB = k.IAdd(k.IMul(h, headDim), dB);
                    uint a = KernelsAttention.HalfAt(k, snap, k.IAdd(k.IMul(iA, mPad), t));
                    uint b = KernelsAttention.HalfAt(k, snap, k.IAdd(k.IMul(iB, mPad), t));
                    uint jc = k.Select(k.U32, rot, j, k.C(0));
                    uint c = k.Select(k.F32, rot, k.Ld(cs, k.IMul(jc, k.C(2))), k.CF(1f));
                    uint s = k.Select(k.F32, rot, k.Ld(cs, k.IAdd(k.IMul(jc, k.C(2)), k.C(1))), k.CF(0f));
                    uint a2 = k.F16(k.FSub(k.FMul(a, c), k.FMul(b, s)));
                    uint b2 = k.F16(k.FAdd(k.FMul(a, s), k.FMul(b, c)));
                    SetHalf(k, kc, k.IAdd(k.IMul(iA, ring), slot), a2);
                    SetHalf(k, kc, k.IAdd(k.IMul(iB, ring), slot), b2);
                });
            });
            // V: whole words, pass 0 only
            uint vWords = k.Shr(k.IMul(m, kvDim), k.C(1));
            k.If(k.S.Value(Op.LogicalAnd, k.Bool, k.IEq(parity, k.C(0)),
                 k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.UGreaterThanEqual, k.Bool, g, kThreads), k.ULt(g, k.IAdd(kThreads, vWords)))), delegate
            {
                uint w = k.S.Value(Op.ISub, k.U32, g, kThreads);
                uint pairs = k.Shr(kvDim, k.C(1));
                uint t = k.UDiv(w, pairs), ip = k.UMod(w, pairs);
                uint slot = k.UMod(k.IAdd(dst0, t), ring);
                uint kWords = k.IMul(kvDim, k.Shr(mPad, k.C(1)));
                k.St(vc, k.IAdd(k.Shr(k.IMul(slot, kvDim), k.C(1)), ip), k.Ld(snap, k.IAdd(kWords, w)));
            });
            return k.Finish();
        }

        static void SetHalf(KB k, KB.Buf b, uint hi, uint value)
        {
            uint wi = k.Shr(hi, k.C(1)), old = k.Ld(b, wi);
            uint h = k.And(k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, k.S.TypeVector(k.F32, 2), value, k.CF(0))), k.C(0xFFFF));
            uint odd = k.IEq(k.And(hi, k.C(1)), k.C(1));
            k.St(b, wi, k.Select(k.U32, odd, k.Or(k.And(old, k.C(0xFFFF)), k.Shl(h, k.C(16))), k.Or(k.And(old, k.C(0xFFFF0000)), h)));
        }
    }
}
