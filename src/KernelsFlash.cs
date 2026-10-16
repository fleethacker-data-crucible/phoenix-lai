// Tiled causal attention for batched prefill ("flash" style). C# 5 only.
// KernelsPrefill.Attention has one workgroup per (query, head) and every query re-reads all of its keys and values from
// VRAM: at long context it was 63% of Gemma's prompt reading. Here one workgroup takes TQ = 16 consecutive queries of
// one head; each block of KC = 16 keys and their values is loaded once into shared memory (fp16) and used by all 16
// queries, so K/V traffic drops ~16x. Streaming softmax per query row, the same math as KernelsPrefill.Attention
// (scores from f16(q), f32 accumulation); only the summation order differs.
// Grid (ceil(T / 16), nHead). bindings: 0 q [T][nHead][hd], 1 kc, 2 vc, 3 out. push: hd, kvDim, ring, nHead, nKv, p0,
// window, scale(bits), T.
using System;

namespace Phoenix
{
    public static class KernelsFlash
    {
        // Tile: tq queries x kc keys, tq * kc threads. 16 x 16 normally; 8 x 8 for 512-wide heads, so the tile fits the
        // 32 KB of shared memory some drivers allow (RX 6700 XT).
        public static uint SharedBytes(uint hdMax, uint tq, uint kc) { return (tq * hdMax / 2 + 2 * kc * hdMax / 2) * 4 + (tq * kc + 4 * tq) * 4; }

        public static void Tile(uint hdMax, uint maxShared, out uint tq, out uint kc)
        {
            tq = 16; kc = 16;
            if (maxShared != 0 && SharedBytes(hdMax, 16, 16) > maxShared) { tq = 8; kc = 8; }
        }

        public static uint[] Attention(KvType kt, KvType vt, uint hdMax, uint TQ, uint KC)
        {
            uint Wg = TQ * KC;
            uint dpt = hdMax / KC;                 // output dims per thread: KC threads per query row
            KB k = new KB(Wg);
            KB.Buf q = k.Buffer(0, k.F32, true), kc = k.Buffer(1, k.U32, true), vc = k.Buffer(2, k.U32, true), ob = k.Buffer(3, k.F32, false);
            k.Push(9);
            KB.Shared qs = k.SharedArray(k.U32, TQ * hdMax / 2);      // f16(q) pairs [qi][d]
            KB.Shared ks = k.SharedArray(k.U32, KC * hdMax / 2);      // K block, fp16 pairs [ki][d]
            KB.Shared vs = k.SharedArray(k.U32, KC * hdMax / 2);      // V block, fp16 pairs [ki][d]
            KB.Shared sc = k.SharedArray(k.F32, TQ * KC);             // scores, then probabilities [qi][ki]
            KB.Shared rm = k.SharedArray(k.F32, TQ), rl = k.SharedArray(k.F32, TQ), rc = k.SharedArray(k.F32, TQ);   // row max, sum, correction
            k.Begin();
            uint c0v = k.Local(k.U32), iv = k.Local(k.U32), dv = k.Local(k.U32), acc = k.Local(k.F32), mx = k.Local(k.F32), sm = k.Local(k.F32);
            uint[] o = new uint[dpt];
            for (int e = 0; e < dpt; e++) o[e] = k.Local(k.F32);
            uint hd = k.PushU(0), kvDim = k.PushU(1), ring = k.PushU(2), nHead = k.PushU(3), nKv = k.PushU(4);
            uint p0 = k.PushU(5), window = k.PushU(6), scale = k.PushF(7), T = k.PushU(8);
            uint tile = k.GroupX(), h = k.GroupY(), lid = k.LocalId();
            uint t0 = k.IMul(tile, k.C(TQ));
            uint kvh = k.UDiv(h, k.UDiv(nHead, nKv));
            uint kRow0 = k.IMul(kvh, hd);
            uint v2 = k.S.TypeVector(k.F32, 2);
            uint hasWin = k.S.Value(Op.INotEqual, k.Bool, window, k.C(0));
            // last query of the tile (clamped to T)
            uint tLast = k.S.Value(Op.ISub, k.U32, k.S.Glsl(k.U32, Spv.GlslUMin, k.IAdd(t0, k.C(TQ)), T), k.C(1));
            uint pFirst = k.IAdd(p0, t0), pLast = k.IAdd(p0, tLast);
            uint loTile = k.Select(k.U32, k.S.Value(Op.LogicalAnd, k.Bool, hasWin, k.S.Value(Op.UGreaterThanEqual, k.Bool, pFirst, window)),
                                   k.IAdd(k.S.Value(Op.ISub, k.U32, pFirst, window), k.C(1)), k.C(0));
            uint endTile = k.IAdd(pLast, k.C(1));

            // queries -> shared (f16-rounded, as the decode path)
            uint half = k.Shr(hd, k.C(1));
            k.For(iv, lid, k.IMul(k.C(TQ), half), k.C(Wg), delegate
            {
                uint w = k.Get(iv, k.U32), qi = k.UDiv(w, half), dp = k.IMul(k.UMod(w, half), k.C(2));
                uint t = k.IAdd(t0, qi);
                uint ok = k.ULt(t, T);
                uint base0 = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, t, k.C(0)), nHead), h), hd), dp);
                uint a = k.Select(k.F32, ok, k.F16(k.Ld(q, base0)), k.CF(0)), b = k.Select(k.F32, ok, k.F16(k.Ld(q, k.IAdd(base0, k.C(1)))), k.CF(0));
                k.St(qs, k.IAdd(k.IMul(qi, k.C(hdMax / 2)), k.Shr(dp, k.C(1))), k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, a, b)));
            });
            k.If(k.ULt(lid, k.C(TQ)), delegate { k.St(rm, lid, k.CF(float.NegativeInfinity)); k.St(rl, lid, k.CF(0)); });
            for (int e = 0; e < dpt; e++) k.Set(o[e], k.CF(0));
            k.Barrier();

            uint myQ = k.UDiv(lid, k.C(KC)), myK = k.UMod(lid, k.C(KC));      // score thread: query row, key in block
            uint myT = k.IAdd(t0, myQ), myP = k.IAdd(p0, myT);
            uint myLo = k.Select(k.U32, k.S.Value(Op.LogicalAnd, k.Bool, hasWin, k.S.Value(Op.UGreaterThanEqual, k.Bool, myP, window)),
                                 k.IAdd(k.S.Value(Op.ISub, k.U32, myP, window), k.C(1)), k.C(0));
            uint myValid = k.ULt(myT, T);
            k.For(c0v, loTile, endTile, k.C(KC), delegate
            {
                uint c0 = k.Get(c0v, k.U32);
                // K and V block -> shared (pairs of dims; keys past the tile range are zero)
                k.For(iv, lid, k.IMul(k.C(KC), half), k.C(Wg), delegate
                {
                    uint w = k.Get(iv, k.U32), ki = k.UDiv(w, half), dp = k.IMul(k.UMod(w, half), k.C(2));
                    uint key = k.IAdd(c0, ki);
                    uint ok = k.ULt(key, endTile);
                    uint slot = k.UMod(k.Select(k.U32, ok, key, c0), ring);
                    uint i0 = k.IAdd(kRow0, dp), i1 = k.IAdd(i0, k.C(1));
                    uint ka = k.Select(k.F32, ok, KernelsKv.K(k, kt, kc, i0, slot, ring, kvDim), k.CF(0));
                    uint kb = k.Select(k.F32, ok, KernelsKv.K(k, kt, kc, i1, slot, ring, kvDim), k.CF(0));
                    uint va = k.Select(k.F32, ok, KernelsKv.V(k, vt, vc, slot, i0, ring, kvDim), k.CF(0));
                    uint vb = k.Select(k.F32, ok, KernelsKv.V(k, vt, vc, slot, i1, ring, kvDim), k.CF(0));
                    uint at = k.IAdd(k.IMul(ki, k.C(hdMax / 2)), k.Shr(dp, k.C(1)));
                    k.St(ks, at, k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, ka, kb)));
                    k.St(vs, at, k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, va, vb)));
                });
                k.Barrier();
                // scores: one per thread (query myQ, key myK)
                uint key2 = k.IAdd(c0, myK);
                k.Set(acc, k.CF(0));
                k.For(dv, k.C(0), half, k.C(1), delegate
                {
                    uint d2 = k.Get(dv, k.U32);
                    uint qw = k.Ld(qs, k.IAdd(k.IMul(myQ, k.C(hdMax / 2)), d2)), kw = k.Ld(ks, k.IAdd(k.IMul(myK, k.C(hdMax / 2)), d2));
                    uint qv = k.S.Glsl(v2, Spv.GlslUnpackHalf2x16, qw), kv2 = k.S.Glsl(v2, Spv.GlslUnpackHalf2x16, kw);
                    uint dot = k.FAdd(k.FMul(k.S.Value(Op.CompositeExtract, k.F32, qv, 0u), k.S.Value(Op.CompositeExtract, k.F32, kv2, 0u)),
                                      k.FMul(k.S.Value(Op.CompositeExtract, k.F32, qv, 1u), k.S.Value(Op.CompositeExtract, k.F32, kv2, 1u)));
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), dot));
                });
                uint inRange = k.S.Value(Op.LogicalAnd, k.Bool, myValid,
                    k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.UGreaterThanEqual, k.Bool, key2, myLo), k.S.Value(Op.ULessThanEqual, k.Bool, key2, myP)));
                k.St(sc, lid, k.Select(k.F32, inRange, k.FMul(k.Get(acc, k.F32), scale), k.CF(float.NegativeInfinity)));
                k.Barrier();
                // row update: new max, probabilities, sum, correction for the running output
                k.If(k.ULt(lid, k.C(TQ)), delegate
                {
                    uint rb = k.IMul(lid, k.C(KC));
                    uint mOld = k.Ld(rm, lid);
                    k.Set(mx, mOld);
                    for (uint j = 0; j < KC; j++) k.Set(mx, k.FMax(k.Get(mx, k.F32), k.Ld(sc, k.IAdd(rb, k.C(j)))));
                    uint mNew = k.Get(mx, k.F32);
                    uint finite = k.S.Value(Op.FOrdGreaterThan, k.Bool, mNew, k.CF(float.NegativeInfinity));
                    k.Set(sm, k.CF(0));
                    for (uint j = 0; j < KC; j++)
                    {
                        uint s = k.Ld(sc, k.IAdd(rb, k.C(j)));
                        uint p = k.Select(k.F32, k.S.Value(Op.LogicalAnd, k.Bool, finite, k.S.Value(Op.FOrdGreaterThan, k.Bool, s, k.CF(float.NegativeInfinity))),
                                          k.Exp(k.FSub(s, mNew)), k.CF(0));
                        k.St(sc, k.IAdd(rb, k.C(j)), p);
                        k.Set(sm, k.FAdd(k.Get(sm, k.F32), p));
                    }
                    uint corr = k.Select(k.F32, k.S.Value(Op.FOrdGreaterThan, k.Bool, mOld, k.CF(float.NegativeInfinity)), k.Exp(k.FSub(mOld, mNew)), k.CF(0));
                    k.St(rc, lid, k.Select(k.F32, finite, corr, k.CF(1)));
                    k.St(rl, lid, k.FAdd(k.FMul(k.Ld(rl, lid), k.Select(k.F32, finite, corr, k.CF(1))), k.Get(sm, k.F32)));
                    k.St(rm, lid, k.Select(k.F32, finite, mNew, mOld));
                });
                k.Barrier();
                // output: 16 threads per query row, dpt dims each (dims sub, sub + 16, ...)
                uint oq = k.UDiv(lid, k.C(KC)), sub = k.UMod(lid, k.C(KC));
                uint corrQ = k.Ld(rc, oq);
                for (uint e = 0; e < dpt; e++)
                {
                    uint d = k.IAdd(sub, k.C(e * KC));
                    uint ee = e;
                    k.If(k.ULt(d, hd), delegate
                    {
                        k.Set(acc, k.FMul(k.Get(o[ee], k.F32), corrQ));
                        for (uint j = 0; j < KC; j++)
                        {
                            uint vw = k.Ld(vs, k.IAdd(k.C(j * (hdMax / 2)), k.Shr(d, k.C(1))));
                            uint vv = k.S.Glsl(v2, Spv.GlslUnpackHalf2x16, vw);
                            uint val = k.Select(k.F32, k.IEq(k.And(d, k.C(1)), k.C(0)), k.S.Value(Op.CompositeExtract, k.F32, vv, 0u), k.S.Value(Op.CompositeExtract, k.F32, vv, 1u));
                            k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(k.Ld(sc, k.IAdd(k.IMul(oq, k.C(KC)), k.C(j))), val)));
                        }
                        k.Set(o[ee], k.Get(acc, k.F32));
                    });
                }
                k.Barrier();
            });
            // write out
            uint wq = k.UDiv(lid, k.C(KC)), wsub = k.UMod(lid, k.C(KC));
            uint wt = k.IAdd(t0, wq);
            k.If(k.ULt(wt, T), delegate
            {
                uint inv = k.FDiv(k.CF(1f), k.Ld(rl, wq));
                uint ob0 = k.IMul(k.IAdd(k.IMul(wt, nHead), h), hd);
                for (uint e = 0; e < dpt; e++)
                {
                    uint d = k.IAdd(wsub, k.C(e * KC));
                    uint ee = e;
                    k.If(k.ULt(d, hd), delegate { k.St(ob, k.IAdd(ob0, d), k.FMul(k.Get(o[ee], k.F32), inv)); });
                }
            });
            return k.Finish();
        }
    }
}
