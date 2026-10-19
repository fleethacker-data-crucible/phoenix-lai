// Register-blocked causal attention for batched prefill (fp16 KV cache). C# 5 only.
// KernelsFlash shares a K/V block between 16 queries of one head, but every score and output element costs two shared
// loads, a quarter of the threads do the softmax while the rest wait, and 512-wide heads do not fit its tile (Gemma's
// global layers fell back to the per-query kernel: 43% of a 9k-token prompt). Here a workgroup takes R rows that read
// the same KV head: (query position, head) pairs, so a grouped-query model shares each K/V block between its heads too
// (Gemma 4 global layers: 16 heads on one KV head). Each thread keeps a 4 x 2 (or 2 x 4) block of scores and a 4 rows
// x 16 dims block of the output in registers; head dims stream through shared memory in slices of 16.
// Same math as KernelsFlash (scores from f16(q) . f16(k) in f32, streaming softmax, f32 output); only summation order
// differs. Grid (ceil(T / (R / G)), nKv) with G = nHead / nKv, which must divide R.
// bindings: 0 q [T][nHead][hd], 1 kc, 2 vc, 3 out. push: hd, kvDim, ring, nHead, nKv, p0, window, scale(bits), T.
using System;

namespace Phoenix
{
    public static class KernelsFlash2
    {
        const uint Wg = 256, DS = 16, Pad = DS + 1;

        // Rows per workgroup and keys per block for a head size (R * hd = 16384 output floats, R * KC = 2048 scores:
        // 64 and 8 per thread). 256: 64 x 32, 512: 32 x 64.
        public static void Tile(uint hdMax, out uint rows, out uint keys)
        {
            rows = 16384 / hdMax; keys = 2048 / rows;
        }

        public static bool Fits(uint hdMax) { return hdMax == 256 || hdMax == 512; }

        // Rows of one workgroup that share a KV head: G heads per KV head must divide R.
        public static bool Fits(uint hdMax, int nHead, int nKv)
        {
            uint R, KC;
            if (!Fits(hdMax) || nKv <= 0 || nHead % nKv != 0) return false;
            Tile(hdMax, out R, out KC);
            return R % (uint)(nHead / nKv) == 0;
        }

        public static uint[] Attention(uint hdMax)
        {
            uint R, KC;
            Tile(hdMax, out R, out KC);
            uint RI = R / 16, KJ = KC / 16;          // score block per thread: rows ty + 16 i, keys tx + 16 j
            uint TPR = Wg / R, KPT = KC / TPR;       // softmax: threads per row, keys per thread
            uint DG = hdMax / 16, RG = R / 4;        // output block: rows rg + RG i (i < 4), dims dg + DG e (e < 16)
            uint VC = 4096 / hdMax;                  // V keys per shared chunk (4096 f32)
            uint SP = KC + 1;                        // score row stride (padded)
            KB k = new KB(Wg);
            KB.Buf q = k.Buffer(0, k.F32, true), kc = k.Buffer(1, k.U32, true), vc = k.Buffer(2, k.U32, true), ob = k.Buffer(3, k.F32, false);
            k.Push(9);
            KB.Shared buf = k.SharedArray(k.F32, 4096);            // Q slice [R][Pad] + K slice [KC][Pad], or a V chunk [VC][hd]
            KB.Shared sc = k.SharedArray(k.F32, R * SP);           // scores, then probabilities
            KB.Shared pm = k.SharedArray(k.F32, Wg), ps = k.SharedArray(k.F32, Wg);
            KB.Shared rm = k.SharedArray(k.F32, R), rl = k.SharedArray(k.F32, R), rc = k.SharedArray(k.F32, R);
            k.Begin();
            uint c0v = k.Local(k.U32), s0v = k.Local(k.U32), v0v = k.Local(k.U32), tmp = k.Local(k.F32);
            uint[] s = new uint[RI * KJ], o = new uint[4 * 16];
            for (int i = 0; i < s.Length; i++) s[i] = k.Local(k.F32);
            for (int i = 0; i < o.Length; i++) o[i] = k.Local(k.F32);
            uint hd = k.PushU(0), kvDim = k.PushU(1), ring = k.PushU(2), nHead = k.PushU(3), nKv = k.PushU(4);
            uint p0 = k.PushU(5), window = k.PushU(6), scale = k.PushF(7), T = k.PushU(8);
            uint tile = k.GroupX(), kvh = k.GroupY(), lid = k.LocalId();
            uint G = k.UDiv(nHead, nKv), P = k.UDiv(k.C(R), G);
            uint t0 = k.IMul(tile, P), kRow0 = k.IMul(kvh, hd);
            uint hasWin = k.S.Value(Op.INotEqual, k.Bool, window, k.C(0));
            uint ninf = k.CF(float.NegativeInfinity);

            Func<uint, uint> lowKey = delegate(uint p)
            {
                return k.Select(k.U32, k.S.Value(Op.LogicalAnd, k.Bool, hasWin, k.S.Value(Op.UGreaterThanEqual, k.Bool, p, window)),
                                k.IAdd(k.S.Value(Op.ISub, k.U32, p, window), k.C(1)), k.C(0));
            };
            uint tLast = k.S.Value(Op.ISub, k.U32, k.S.Glsl(k.U32, Spv.GlslUMin, k.IAdd(t0, P), T), k.C(1));
            uint endKey = k.IAdd(k.IAdd(p0, tLast), k.C(1));
            uint firstBlock = k.IMul(k.UDiv(lowKey(k.IAdd(p0, t0)), k.C(KC)), k.C(KC));

            k.If(k.ULt(lid, k.C(R)), delegate { k.St(rm, lid, ninf); k.St(rl, lid, k.CF(0)); });
            for (int i = 0; i < o.Length; i++) k.Set(o[i], k.CF(0));

            uint tx = k.UMod(lid, k.C(16)), ty = k.UDiv(lid, k.C(16));
            // row -> query index t, head h, position, valid
            Func<uint, uint> rowT = delegate(uint r) { return k.IAdd(t0, k.UDiv(r, G)); };
            Func<uint, uint> rowH = delegate(uint r) { return k.IAdd(k.IMul(kvh, G), k.UMod(r, G)); };

            k.For(c0v, firstBlock, endKey, k.C(KC), delegate
            {
                uint c0 = k.Get(c0v, k.U32);
                for (int i = 0; i < s.Length; i++) k.Set(s[i], k.CF(0));
                k.For(s0v, k.C(0), hd, k.C(DS), delegate
                {
                    uint s0 = k.Get(s0v, k.U32);
                    k.Barrier();
                    for (uint u = 0; u < R * DS / Wg; u++)
                    {
                        uint w = k.IAdd(lid, k.C(u * Wg)), r = k.UDiv(w, k.C(DS)), dd = k.UMod(w, k.C(DS));
                        uint t = rowT(r), ok = k.ULt(t, T);
                        uint qi = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Select(k.U32, ok, t, k.C(0)), nHead), rowH(r)), hd), k.IAdd(s0, dd));
                        k.St(buf, k.IAdd(k.IMul(r, k.C(Pad)), dd), k.Select(k.F32, ok, k.F16(k.Ld(q, qi)), k.CF(0)));
                    }
                    for (uint u = 0; u < KC * DS / Wg; u++)
                    {
                        uint w = k.IAdd(lid, k.C(u * Wg)), key = k.UMod(w, k.C(KC)), dd = k.UDiv(w, k.C(KC));
                        uint slot = k.UMod(k.IAdd(c0, key), ring);
                        uint kv = KernelsKv.K(k, KvType.F16, kc, k.IAdd(kRow0, k.IAdd(s0, dd)), slot, ring, kvDim);
                        k.St(buf, k.IAdd(k.C(R * Pad), k.IAdd(k.IMul(key, k.C(Pad)), dd)), kv);
                    }
                    k.Barrier();
                    for (uint dd = 0; dd < DS; dd++)
                    {
                        uint[] qv = new uint[RI], kv = new uint[KJ];
                        for (uint i = 0; i < RI; i++) qv[i] = k.Ld(buf, k.IAdd(k.IMul(k.IAdd(ty, k.C(16 * i)), k.C(Pad)), k.C(dd)));
                        for (uint j = 0; j < KJ; j++) kv[j] = k.Ld(buf, k.IAdd(k.IMul(k.IAdd(tx, k.C(16 * j)), k.C(Pad)), k.C(R * Pad + dd)));
                        for (uint i = 0; i < RI; i++)
                            for (uint j = 0; j < KJ; j++)
                                k.Set(s[i * KJ + j], k.FAdd(k.Get(s[i * KJ + j], k.F32), k.FMul(qv[i], kv[j])));
                    }
                });
                // masked, scaled scores -> shared
                for (uint i = 0; i < RI; i++)
                {
                    uint r = k.IAdd(ty, k.C(16 * i)), t = rowT(r), p = k.IAdd(p0, t);
                    uint lo = lowKey(p);
                    for (uint j = 0; j < KJ; j++)
                    {
                        uint kk = k.IAdd(tx, k.C(16 * j)), key = k.IAdd(c0, kk);
                        uint ok = k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(t, T),
                            k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.UGreaterThanEqual, k.Bool, key, lo), k.S.Value(Op.ULessThanEqual, k.Bool, key, p)));
                        k.St(sc, k.IAdd(k.IMul(r, k.C(SP)), kk), k.Select(k.F32, ok, k.FMul(k.Get(s[i * KJ + j], k.F32), scale), ninf));
                    }
                }
                k.Barrier();
                // softmax update: TPR threads per row, keys part + TPR e
                uint row = k.UDiv(lid, k.C(TPR)), part = k.UMod(lid, k.C(TPR)), rb = k.IMul(row, k.C(SP));
                k.Set(tmp, ninf);
                for (uint e = 0; e < KPT; e++) k.Set(tmp, k.FMax(k.Get(tmp, k.F32), k.Ld(sc, k.IAdd(rb, k.IAdd(part, k.C(TPR * e))))));
                k.St(pm, lid, k.Get(tmp, k.F32));
                k.Barrier();
                uint mOld = k.Ld(rm, row);
                k.Set(tmp, mOld);
                for (uint e = 0; e < TPR; e++) k.Set(tmp, k.FMax(k.Get(tmp, k.F32), k.Ld(pm, k.IAdd(k.IMul(row, k.C(TPR)), k.C(e)))));
                uint mNew = k.Get(tmp, k.F32);
                uint finite = k.S.Value(Op.FOrdGreaterThan, k.Bool, mNew, ninf);
                k.Set(tmp, k.CF(0));
                for (uint e = 0; e < KPT; e++)
                {
                    uint at = k.IAdd(rb, k.IAdd(part, k.C(TPR * e)));
                    uint sv = k.Ld(sc, at);
                    uint pv = k.Select(k.F32, k.S.Value(Op.LogicalAnd, k.Bool, finite, k.S.Value(Op.FOrdGreaterThan, k.Bool, sv, ninf)),
                                       k.Exp(k.FSub(sv, mNew)), k.CF(0));
                    k.St(sc, at, pv);
                    k.Set(tmp, k.FAdd(k.Get(tmp, k.F32), pv));
                }
                k.St(ps, lid, k.Get(tmp, k.F32));
                k.Barrier();
                k.If(k.IEq(part, k.C(0)), delegate
                {
                    uint sum = k.CF(0);
                    for (uint e = 0; e < TPR; e++) sum = k.FAdd(sum, k.Ld(ps, k.IAdd(lid, k.C(e))));
                    uint corr = k.Select(k.F32, k.S.Value(Op.LogicalAnd, k.Bool, finite, k.S.Value(Op.FOrdGreaterThan, k.Bool, mOld, ninf)),
                                         k.Exp(k.FSub(mOld, mNew)), k.Select(k.F32, finite, k.CF(0), k.CF(1)));
                    k.St(rc, row, corr);
                    k.St(rl, row, k.FAdd(k.FMul(k.Ld(rl, row), corr), sum));
                    k.St(rm, row, k.Select(k.F32, finite, mNew, mOld));
                });
                k.Barrier();
                // output: o[r][d] = o * corr + sum_j p[r][j] v[j][d]
                uint rg = k.UDiv(lid, k.C(DG)), dg = k.UMod(lid, k.C(DG));
                for (uint i = 0; i < 4; i++)
                {
                    uint corr = k.Ld(rc, k.IAdd(rg, k.C(RG * i)));
                    for (uint e = 0; e < 16; e++) k.Set(o[i * 16 + e], k.FMul(k.Get(o[i * 16 + e], k.F32), corr));
                }
                k.For(v0v, k.C(0), k.C(KC), k.C(VC), delegate
                {
                    uint v0 = k.Get(v0v, k.U32);
                    k.Barrier();
                    for (uint u = 0; u < VC * hdMax / 2 / Wg; u++)
                    {
                        uint w = k.IAdd(lid, k.C(u * Wg)), kj = k.UDiv(w, k.C(hdMax / 2)), dp = k.UMod(w, k.C(hdMax / 2));
                        uint key = k.IAdd(c0, k.IAdd(v0, kj));
                        uint ok = k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(key, endKey), k.ULt(k.IMul(dp, k.C(2)), hd));
                        uint slot = k.UMod(k.Select(k.U32, ok, key, c0), ring);
                        uint word = k.Ld(vc, k.Shr(k.IAdd(k.IMul(slot, kvDim), k.IAdd(kRow0, k.IMul(dp, k.C(2)))), k.C(1)));
                        uint at = k.IAdd(k.IMul(kj, k.C(hdMax)), k.IMul(dp, k.C(2)));
                        k.St(buf, at, k.Select(k.F32, ok, k.HalfOf(word, false), k.CF(0)));
                        k.St(buf, k.IAdd(at, k.C(1)), k.Select(k.F32, ok, k.HalfOf(word, true), k.CF(0)));
                    }
                    k.Barrier();
                    for (uint jj = 0; jj < VC; jj++)
                    {
                        uint[] pv = new uint[4], vv = new uint[16];
                        for (uint i = 0; i < 4; i++) pv[i] = k.Ld(sc, k.IAdd(k.IMul(k.IAdd(rg, k.C(RG * i)), k.C(SP)), k.IAdd(v0, k.C(jj))));
                        for (uint e = 0; e < 16; e++) vv[e] = k.Ld(buf, k.IAdd(dg, k.C(jj * hdMax + DG * e)));
                        for (uint i = 0; i < 4; i++)
                            for (uint e = 0; e < 16; e++)
                                k.Set(o[i * 16 + e], k.FAdd(k.Get(o[i * 16 + e], k.F32), k.FMul(pv[i], vv[e])));
                    }
                });
            });
            // write out
            k.Barrier();
            uint rg2 = k.UDiv(lid, k.C(DG)), dg2 = k.UMod(lid, k.C(DG));
            for (uint i = 0; i < 4; i++)
            {
                uint r = k.IAdd(rg2, k.C(RG * i)), t = rowT(r);
                uint ii = i;
                k.If(k.ULt(t, T), delegate
                {
                    uint inv = k.FDiv(k.CF(1f), k.Ld(rl, r));
                    uint ob0 = k.IMul(k.IAdd(k.IMul(t, nHead), rowH(r)), hd);
                    for (uint e = 0; e < 16; e++)
                    {
                        uint d = k.IAdd(dg2, k.C(DG * e));
                        uint ee = e;
                        k.If(k.ULt(d, hd), delegate { k.St(ob, k.IAdd(ob0, d), k.FMul(k.Get(o[ii * 16 + ee], k.F32), inv)); });
                    }
                });
            }
            return k.Finish();
        }
    }
}
