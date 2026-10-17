// Batched kernels: process a whole token sequence per dispatch (EmbeddingGemma on the GPU; a base for batched
// prefill). Activations are row-major [token][feature] f32. C# 5 only.
using System;

namespace Phoenix
{
    public static class KernelsBatch
    {
        public const uint MmRows = 64, MmTokens = 64, MmK = 32;   // matmul tile: 64 weight rows x 64 tokens, K step 32
        const uint MmWg = 256, MmPad = 65;                           // 16 x 16 threads, 4 x 4 outputs each; padded shared rows

        // Y[t][r] = sum_j W[r][j] X[t][j], W as fp16 pairs (cols a multiple of MmK). Grid (ceil(rows/64), ceil(n/64)).
        // Both tiles are loaded coalesced into shared memory (transposed, [k][row] / [k][token]); each thread keeps a
        // 4 x 4 register block (rows tx + 16i, tokens ty + 16j). bindings: 0 W u32 [rows * cols/2], 1 X f32 [n * cols],
        // 2 Y f32 [n * rows]. push: rows, cols, n
        public static uint[] MatMulF16()
        {
            KB k = new KB(MmWg);
            KB.Buf w = k.Buffer(0, k.U32, true), x = k.Buffer(1, k.F32, true), y = k.Buffer(2, k.F32, false);
            k.Push(3);
            KB.Shared ws = k.SharedArray(k.F32, MmK * MmPad), xs = k.SharedArray(k.F32, MmK * MmPad);
            k.Begin();
            uint k0 = k.Local(k.U32), kk = k.Local(k.U32);
            uint[] acc = new uint[16];
            for (int e = 0; e < 16; e++) acc[e] = k.Local(k.F32);
            uint rows = k.PushU(0), cols = k.PushU(1), n = k.PushU(2);
            uint lid = k.LocalId(), tx = k.And(lid, k.C(15)), ty = k.Shr(lid, k.C(4));
            uint r0 = k.IMul(k.GroupX(), k.C(MmRows)), t0 = k.IMul(k.GroupY(), k.C(MmTokens));
            uint halfCols = k.Shr(cols, k.C(1));
            for (int e = 0; e < 16; e++) k.Set(acc[e], k.CF(0));
            k.For(k0, k.C(0), cols, k.C(MmK), delegate
            {
                uint kb = k.Get(k0, k.U32);
                for (uint e = 0; e < MmRows * MmK / 2 / MmWg; e++)   // W: 64 rows x 16 words
                {
                    uint idx = k.IAdd(lid, k.C(e * MmWg));
                    uint row = k.Shr(idx, k.C(4)), kp = k.And(idx, k.C(15));
                    uint gr = k.IAdd(r0, row), ok = k.ULt(gr, rows);
                    uint word = k.Select(k.U32, ok, k.Ld(w, k.IAdd(k.IMul(k.Select(k.U32, ok, gr, k.C(0)), halfCols), k.IAdd(k.Shr(kb, k.C(1)), kp))), k.C(0));
                    uint at = k.IAdd(k.IMul(kp, k.C(2 * MmPad)), row);
                    k.St(ws, at, k.HalfOf(word, false));
                    k.St(ws, k.IAdd(at, k.C(MmPad)), k.HalfOf(word, true));
                }
                for (uint e = 0; e < MmTokens * MmK / MmWg; e++)     // X: 64 tokens x 32 values
                {
                    uint idx = k.IAdd(lid, k.C(e * MmWg));
                    uint tok = k.Shr(idx, k.C(5)), c = k.And(idx, k.C(31));
                    uint gt = k.IAdd(t0, tok), ok = k.ULt(gt, n);
                    uint v = k.Select(k.F32, ok, k.Ld(x, k.IAdd(k.IMul(k.Select(k.U32, ok, gt, k.C(0)), cols), k.IAdd(kb, c))), k.CF(0));
                    k.St(xs, k.IAdd(k.IMul(c, k.C(MmPad)), tok), v);
                }
                k.Barrier();
                k.For(kk, k.C(0), k.C(MmK), k.C(1), delegate
                {
                    uint o = k.IMul(k.Get(kk, k.U32), k.C(MmPad));
                    uint[] a = new uint[4], b = new uint[4];
                    for (uint i = 0; i < 4; i++) a[i] = k.Ld(ws, k.IAdd(o, k.IAdd(tx, k.C(16 * i))));
                    for (uint j = 0; j < 4; j++) b[j] = k.Ld(xs, k.IAdd(o, k.IAdd(ty, k.C(16 * j))));
                    for (int i = 0; i < 4; i++)
                        for (int j = 0; j < 4; j++)
                            k.Set(acc[i * 4 + j], k.FAdd(k.Get(acc[i * 4 + j], k.F32), k.FMul(a[i], b[j])));
                });
                k.Barrier();
            });
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    uint r = k.IAdd(r0, k.IAdd(tx, k.C((uint)(16 * i)))), tok = k.IAdd(t0, k.IAdd(ty, k.C((uint)(16 * j))));
                    uint e = acc[i * 4 + j];
                    k.If(k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(r, rows), k.ULt(tok, n)), delegate { k.St(y, k.IAdd(k.IMul(tok, rows), r), k.Get(e, k.F32)); });
                }
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

        public const uint MaxSeq = 1024;   // longest sequence the embedder takes
        public static uint AttnSharedBytes(uint hdMax, uint tq, uint kc) { return (tq * hdMax / 2 + 2 * kc * hdMax / 2) * 4 + (tq * kc + 3 * tq) * 4; }

        // Attention tile: 16 queries x 16 keys (256 threads), or 8 x 8 when that does not fit shared memory (512-wide heads).
        public static void AttnTile(uint hdMax, uint maxShared, out uint tq, out uint kc)
        {
            tq = 16; kc = 16;
            if (maxShared != 0 && AttnSharedBytes(hdMax, 16, 16) > maxShared) { tq = 8; kc = 8; }
        }

        // Bidirectional attention, scale 1: query t, head h attends keys [t - halfWindow, t + halfWindow] (or all).
        // Flash style: one workgroup takes TQ consecutive queries of one head; each block of KC keys and values goes
        // through shared memory once (fp16 pairs, q rounded to fp16 too) for all TQ queries, with a streaming softmax
        // per query row. hdMax: multiple of 2 * KC. q [n][nHead][hd], k/v [n][nKv][hd], out [n][nHead][hd].
        // Grid (ceil(n/TQ), nHead). bindings: 0 q, 1 k, 2 v, 3 out. push: n, hd, nHead, nKv, halfWindow (0 = all)
        public static uint[] AttentionBi(uint hdMax, uint TQ, uint KC)
        {
            uint Wg = TQ * KC;
            uint dpt = hdMax / KC, hw = hdMax / 2;
            KB k = new KB(Wg);
            KB.Buf q = k.Buffer(0, k.F32, true), kb = k.Buffer(1, k.F32, true), vb = k.Buffer(2, k.F32, true), ob = k.Buffer(3, k.F32, false);
            k.Push(5);
            KB.Shared qs = k.SharedArray(k.U32, TQ * hw), ks = k.SharedArray(k.U32, KC * hw), vs = k.SharedArray(k.U32, KC * hw);
            KB.Shared sc = k.SharedArray(k.F32, TQ * KC);
            KB.Shared rm = k.SharedArray(k.F32, TQ), rl = k.SharedArray(k.F32, TQ), rc = k.SharedArray(k.F32, TQ);
            k.Begin();
            uint c0v = k.Local(k.U32), iv = k.Local(k.U32), dv = k.Local(k.U32), acc = k.Local(k.F32), mx = k.Local(k.F32), sm = k.Local(k.F32);
            uint[] o = new uint[dpt];
            for (int e = 0; e < dpt; e++) o[e] = k.Local(k.F32);
            uint n = k.PushU(0), hd = k.PushU(1), nHead = k.PushU(2), nKv = k.PushU(3), halfW = k.PushU(4);
            uint t0 = k.IMul(k.GroupX(), k.C(TQ)), h = k.GroupY(), lid = k.LocalId();
            uint kvh = k.UDiv(h, k.UDiv(nHead, nKv));
            uint v2 = k.S.TypeVector(k.F32, 2);
            uint all = k.IEq(halfW, k.C(0));
            uint tLast = k.S.Value(Op.ISub, k.U32, k.S.Glsl(k.U32, Spv.GlslUMin, k.IAdd(t0, k.C(TQ)), n), k.C(1));
            uint loTile = k.Select(k.U32, k.S.Value(Op.LogicalOr, k.Bool, all, k.ULt(t0, halfW)), k.C(0), k.S.Value(Op.ISub, k.U32, t0, halfW));
            uint endTile = k.Select(k.U32, all, n, k.S.Glsl(k.U32, Spv.GlslUMin, n, k.IAdd(k.IAdd(tLast, halfW), k.C(1))));
            uint half = k.Shr(hd, k.C(1));

            // queries -> shared (fp16 pairs)
            k.For(iv, lid, k.IMul(k.C(TQ), half), k.C(Wg), delegate
            {
                uint w = k.Get(iv, k.U32), qi = k.UDiv(w, half), dp = k.UMod(w, half);
                uint t = k.IAdd(t0, qi), ok = k.ULt(t, n);
                uint i0 = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, t, k.C(0)), nHead), h), hd), k.IMul(dp, k.C(2)));
                uint a = k.Select(k.F32, ok, k.Ld(q, i0), k.CF(0)), b = k.Select(k.F32, ok, k.Ld(q, k.IAdd(i0, k.C(1))), k.CF(0));
                k.St(qs, k.IAdd(k.IMul(qi, k.C(hw)), dp), k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, a, b)));
            });
            k.If(k.ULt(lid, k.C(TQ)), delegate { k.St(rm, lid, k.CF(float.NegativeInfinity)); k.St(rl, lid, k.CF(0)); });
            for (int e = 0; e < dpt; e++) k.Set(o[e], k.CF(0));
            k.Barrier();

            uint myQ = k.UDiv(lid, k.C(KC)), myK = k.UMod(lid, k.C(KC));
            uint myT = k.IAdd(t0, myQ);
            uint myValid = k.ULt(myT, n);
            k.For(c0v, loTile, endTile, k.C(KC), delegate
            {
                uint c0 = k.Get(c0v, k.U32);
                k.For(iv, lid, k.IMul(k.C(KC), half), k.C(Wg), delegate
                {
                    uint w = k.Get(iv, k.U32), ki = k.UDiv(w, half), dp = k.UMod(w, half);
                    uint key = k.IAdd(c0, ki), ok = k.ULt(key, endTile);
                    uint i0 = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, key, k.C(0)), nKv), kvh), hd), k.IMul(dp, k.C(2))), i1 = k.IAdd(i0, k.C(1));
                    uint ka = k.Select(k.F32, ok, k.Ld(kb, i0), k.CF(0)), kb2 = k.Select(k.F32, ok, k.Ld(kb, i1), k.CF(0));
                    uint va = k.Select(k.F32, ok, k.Ld(vb, i0), k.CF(0)), vb2 = k.Select(k.F32, ok, k.Ld(vb, i1), k.CF(0));
                    uint at = k.IAdd(k.IMul(ki, k.C(hw)), dp);
                    k.St(ks, at, k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, ka, kb2)));
                    k.St(vs, at, k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, va, vb2)));
                });
                k.Barrier();
                // score of (query myQ, key myK)
                uint key2 = k.IAdd(c0, myK);
                k.Set(acc, k.CF(0));
                k.For(dv, k.C(0), half, k.C(1), delegate
                {
                    uint d2 = k.Get(dv, k.U32);
                    uint qv = k.S.Glsl(v2, Spv.GlslUnpackHalf2x16, k.Ld(qs, k.IAdd(k.IMul(myQ, k.C(hw)), d2)));
                    uint kv = k.S.Glsl(v2, Spv.GlslUnpackHalf2x16, k.Ld(ks, k.IAdd(k.IMul(myK, k.C(hw)), d2)));
                    uint dot = k.FAdd(k.FMul(k.S.Value(Op.CompositeExtract, k.F32, qv, 0u), k.S.Value(Op.CompositeExtract, k.F32, kv, 0u)),
                                      k.FMul(k.S.Value(Op.CompositeExtract, k.F32, qv, 1u), k.S.Value(Op.CompositeExtract, k.F32, kv, 1u)));
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), dot));
                });
                // |key - t| <= halfW (or all), key in range
                uint diff = k.Select(k.U32, k.ULt(key2, myT), k.S.Value(Op.ISub, k.U32, myT, key2), k.S.Value(Op.ISub, k.U32, key2, myT));
                uint inWin = k.S.Value(Op.LogicalOr, k.Bool, all, k.S.Value(Op.ULessThanEqual, k.Bool, diff, halfW));
                uint inRange = k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.LogicalAnd, k.Bool, myValid, k.ULt(key2, endTile)), inWin);
                k.St(sc, lid, k.Select(k.F32, inRange, k.Get(acc, k.F32), k.CF(float.NegativeInfinity)));
                k.Barrier();
                // per row: new max, probabilities, sum, correction for the running output
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
                    uint c = k.Select(k.F32, finite, corr, k.CF(1));
                    k.St(rc, lid, c);
                    k.St(rl, lid, k.FAdd(k.FMul(k.Ld(rl, lid), c), k.Get(sm, k.F32)));
                    k.St(rm, lid, k.Select(k.F32, finite, mNew, mOld));
                });
                k.Barrier();
                // output: KC threads per query row, dims sub, sub + KC, ...
                uint oq = k.UDiv(lid, k.C(KC)), sub = k.UMod(lid, k.C(KC));
                uint corrQ = k.Ld(rc, oq);
                uint[] pj = new uint[KC];
                for (uint j = 0; j < KC; j++) pj[j] = k.Ld(sc, k.IAdd(k.IMul(oq, k.C(KC)), k.C(j)));
                for (uint e = 0; e < dpt; e++)
                {
                    uint d = k.IAdd(sub, k.C(e * KC));
                    uint ee = e;
                    k.If(k.ULt(d, hd), delegate
                    {
                        k.Set(acc, k.FMul(k.Get(o[ee], k.F32), corrQ));
                        uint hi = k.IEq(k.And(d, k.C(1)), k.C(1)), dw = k.Shr(d, k.C(1));
                        for (uint j = 0; j < KC; j++)
                        {
                            uint vv = k.S.Glsl(v2, Spv.GlslUnpackHalf2x16, k.Ld(vs, k.IAdd(k.C(j * hw), dw)));
                            uint val = k.Select(k.F32, hi, k.S.Value(Op.CompositeExtract, k.F32, vv, 1u), k.S.Value(Op.CompositeExtract, k.F32, vv, 0u));
                            k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(pj[j], val)));
                        }
                        k.Set(o[ee], k.Get(acc, k.F32));
                    });
                }
                k.Barrier();
            });
            uint wq = k.UDiv(lid, k.C(KC)), wsub = k.UMod(lid, k.C(KC));
            uint wt = k.IAdd(t0, wq);
            k.If(k.ULt(wt, n), delegate
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
