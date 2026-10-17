// Decode attention as wide kernels: scores, softmax, chunked weighted V, chunk reduction. Same math and fp16
// rounding points as Kernels.Attention: s = f16(q)·k * scale; p = f16(exp(s - max) / sum); out = sum_t p_t v_t.
// The GQA group size is compiled in. Per-token values come from a small params buffer (not push constants) so a
// recorded command buffer can be replayed for every token:
//   params[0] = pos, params[1] = n (keys attended), params[2] = number of key chunks, params[3] = attention temperature (f32 bits),
//   params[4] = first key position (0, or the start of a sliding window: keys first .. first+n-1).
// C# 5 only.
using System;

namespace Phoenix
{
    public static class KernelsAttention
    {
        public const uint MaxGroup = 8;
        public const uint MaxHeadDim = 256;
        public const uint Chunk = 32;            // keys per weighted-V chunk
        public const uint OutWg = 128;           // weighted-V threads per workgroup (one per head dimension)
        public const int ParamWords = 5;

        // RoPE with the host cos/sin table; Q (useTemp = 1) is then scaled by the attention temperature.
        // bindings: 0 x, 1 cs, 2 params. push: nHeads, headDim, halfRot, useTemp
        public static uint[] Rope()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, false), cs = k.Buffer(1, k.F32, true), prm = k.Buffer(2, k.U32, true);
            k.Push(4);
            k.Begin();
            uint p = k.GlobalId();
            uint nHeads = k.PushU(0), headDim = k.PushU(1), half = k.PushU(2), useTemp = k.PushU(3);
            uint post = k.Select(k.F32, k.IEq(useTemp, k.C(1)), k.S.Value(Op.Bitcast, k.F32, k.Ld(prm, k.C(3))), k.CF(1f));
            k.If(k.ULt(p, k.IMul(nHeads, half)), delegate
            {
                uint h = k.UDiv(p, half), i = k.UMod(p, half);
                uint o = k.IAdd(k.IMul(h, headDim), k.IMul(i, k.C(2)));
                uint a = k.Ld(x, o), b = k.Ld(x, k.IAdd(o, k.C(1)));
                uint c = k.Ld(cs, k.IMul(i, k.C(2))), s = k.Ld(cs, k.IAdd(k.IMul(i, k.C(2)), k.C(1)));
                k.St(x, o, k.FMul(k.FSub(k.FMul(a, c), k.FMul(b, s)), post));
                k.St(x, k.IAdd(o, k.C(1)), k.FMul(k.FAdd(k.FMul(a, s), k.FMul(b, c)), post));
            });
            return k.Finish();
        }

        // The KV cache holds fp16 values packed two per u32 word (they are fp16-rounded anyway, so this halves the memory
        // with identical results). A cache has `ring` slots (even); position p lives in slot p % ring, so sliding-window
        // layers keep only the last `ring` positions while full-attention layers use ring = context length.
        //   K transposed: element (i, slot) at half index i * ring + slot (i = kvh * headDim + d)
        //   V:            element (slot, i) at half index slot * kvDim + i

        static uint Pack2(KB k, uint lo, uint hi)
        {
            return k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, k.S.TypeVector(k.F32, 2), lo, hi));
        }

        // fp16 at half-index idx of a packed buffer, as f32.
        public static uint HalfAt(KB k, KB.Buf b, uint idx)
        {
            uint w = k.Ld(b, k.Shr(idx, k.C(1)));
            return k.Select(k.F32, k.IEq(k.And(idx, k.C(1)), k.C(0)), k.HalfOf(w, false), k.HalfOf(w, true));
        }

        // KV cache write for the current position. One thread per pair of elements (i, i+1).
        // bindings: 0 k, 1 v, 2 kc (u32), 3 vc (u32), 4 params. push: kvDim, ring
        public static uint[] KvStore()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf kk = k.Buffer(0, k.F32, true), v = k.Buffer(1, k.F32, true), kc = k.Buffer(2, k.U32, false), vc = k.Buffer(3, k.U32, false);
            KB.Buf prm = k.Buffer(4, k.U32, true);
            k.Push(2);
            k.Begin();
            uint pi = k.GlobalId();
            uint kvDim = k.PushU(0), ring = k.PushU(1), pos = k.Ld(prm, k.C(0));
            k.If(k.ULt(pi, k.Shr(kvDim, k.C(1))), delegate
            {
                uint i = k.IMul(pi, k.C(2));
                uint slot = k.UMod(pos, ring);
                uint hiHalf = k.IEq(k.And(slot, k.C(1)), k.C(1));
                // K: elements i and i+1 are in different words (rows of the transposed cache); replace one half of each.
                for (uint e = 0; e < 2; e++)
                {
                    uint idx = k.IAdd(k.IMul(k.IAdd(i, k.C(e)), ring), slot);
                    uint wi = k.Shr(idx, k.C(1));
                    uint val = k.F16(k.Ld(kk, k.IAdd(i, k.C(e))));
                    uint old = k.Ld(kc, wi);
                    uint asLo = k.Or(k.And(old, k.C(0xFFFF0000)), k.And(Pack2(k, val, k.CF(0)), k.C(0xFFFF)));
                    uint asHi = k.Or(k.And(old, k.C(0xFFFF)), k.And(Pack2(k, k.CF(0), val), k.C(0xFFFF0000)));
                    k.St(kc, wi, k.Select(k.U32, hiHalf, asHi, asLo));
                }
                // V: elements i, i+1 share one word.
                uint vw = k.Shr(k.IAdd(k.IMul(slot, kvDim), i), k.C(1));
                k.St(vc, vw, Pack2(k, k.F16(k.Ld(v, i)), k.F16(k.Ld(v, k.IAdd(i, k.C(1))))));
            });
            return k.Finish();
        }

        public const uint KeysPerGroup = 16;   // scores: keys per workgroup
        public const uint Lanes = 4;           // scores: threads cooperating on one key's dot product

        // Scores: 16 keys x 4 lanes per 64-thread workgroup; each lane takes every 4th dimension of the transposed K cache,
        // the 4 partial dot products are added in a fixed order. Scores every query head of the group.
        // Grid: (ceil(n / 16), nHeadKv). bindings: 0 q, 1 kc (packed fp16), 2 scores, 3 params.
        // push: headDim, kvDim, maxCtx (score row stride), scale(bits), ring
        public static uint[] Scores(uint group) { return Scores(group, MaxHeadDim, KvType.F16); }
        public static uint[] Scores(uint group, uint maxHeadDim) { return Scores(group, maxHeadDim, KvType.F16); }

        // Shared memory needed by Scores (bytes): the group's queries as packed fp16 pairs + the partial sums.
        public static uint ScoresShared(uint group, uint maxHeadDim) { return (group * maxHeadDim / 2 + group * KeysPerGroup * Lanes) * 4; }

        // kv: cache format (KernelsKv).
        public static uint[] Scores(uint group, uint maxHeadDim, KvType kvType)
        {
            KB k = new KB(KeysPerGroup * Lanes);
            KB.Buf q = k.Buffer(0, k.F32, true), kc = k.Buffer(1, k.U32, true), sc = k.Buffer(2, k.F32, false), prm = k.Buffer(3, k.U32, true);
            k.Push(5);
            KB.Shared qs = k.SharedArray(k.U32, group * maxHeadDim / 2);   // f16(q) packed two per word (exact: q is f16-rounded)
            KB.Shared part = k.SharedArray(k.F32, group * KeysPerGroup * Lanes);
            k.Begin();
            uint i = k.Local(k.U32), d = k.Local(k.U32);
            uint[] acc = new uint[group];
            for (int a = 0; a < group; a++) acc[a] = k.Local(k.F32);
            uint headDim = k.PushU(0), kvDim = k.PushU(1), maxCtx = k.PushU(2), scale = k.PushF(3), ring = k.PushU(4);
            uint n = k.Ld(prm, k.C(1)), first = k.Ld(prm, k.C(4));
            uint lid = k.LocalId(), kvh = k.GroupY();
            uint keyInGroup = k.Shr(lid, k.C(2)), lane = k.And(lid, k.C(Lanes - 1));
            uint t = k.IAdd(k.IMul(k.GroupX(), k.C(KeysPerGroup)), keyInGroup);
            uint qLen = k.IMul(k.C(group), headDim);
            uint qBase = k.IMul(k.IMul(kvh, k.C(group)), headDim);

            uint v2 = k.S.TypeVector(k.F32, 2);
            k.For(i, lid, k.Shr(qLen, k.C(1)), k.C(KeysPerGroup * Lanes), delegate
            {
                uint ii = k.Get(i, k.U32);
                uint a0 = k.Ld(q, k.IAdd(qBase, k.IMul(ii, k.C(2)))), a1 = k.Ld(q, k.IAdd(qBase, k.IAdd(k.IMul(ii, k.C(2)), k.C(1))));
                k.St(qs, ii, k.S.Glsl(k.U32, Spv.GlslPackHalf2x16, k.S.Value(Op.CompositeConstruct, v2, k.F16(a0), k.F16(a1))));
            });
            k.Barrier();

            for (int a = 0; a < group; a++) k.Set(acc[a], k.CF(0));
            uint tSafe = k.Select(k.U32, k.ULt(t, n), t, k.C(0));
            uint slot = k.UMod(k.IAdd(first, tSafe), ring);
            uint kRow = k.IMul(kvh, headDim);
            k.For(d, lane, headDim, k.C(Lanes), delegate
            {
                uint dd = k.Get(d, k.U32);
                uint kv = KernelsKv.K(k, kvType, kc, k.IAdd(kRow, dd), slot, ring, kvDim);
                for (int a = 0; a < group; a++)
                {
                    uint qi = k.IAdd(k.IMul(k.C((uint)a), headDim), dd);
                    uint qw = k.Ld(qs, k.Shr(qi, k.C(1)));
                    uint qv = k.Select(k.F32, k.IEq(k.And(qi, k.C(1)), k.C(0)), k.HalfOf(qw, false), k.HalfOf(qw, true));
                    k.Set(acc[a], k.FAdd(k.Get(acc[a], k.F32), k.FMul(qv, kv)));
                }
            });
            for (int a = 0; a < group; a++) k.St(part, k.IAdd(k.IMul(k.C((uint)a), k.C(KeysPerGroup * Lanes)), lid), k.Get(acc[a], k.F32));
            k.Barrier();
            k.If(k.S.Value(Op.LogicalAnd, k.Bool, k.IEq(lane, k.C(0)), k.ULt(t, n)), delegate
            {
                for (int a = 0; a < group; a++)
                {
                    uint pb = k.IAdd(k.IMul(k.C((uint)a), k.C(KeysPerGroup * Lanes)), lid);
                    uint sum = k.Ld(part, pb);
                    for (uint l = 1; l < Lanes; l++) sum = k.FAdd(sum, k.Ld(part, k.IAdd(pb, k.C(l))));
                    uint h = k.IAdd(k.IMul(kvh, k.C(group)), k.C((uint)a));
                    k.St(sc, k.IAdd(k.IMul(h, maxCtx), t), k.FMul(sum, scale));
                }
            });
            return k.Finish();
        }

        // Softmax over n scores per head, then p = f16(e / sum). Grid: (nHead). bindings: 0 scores, 1 params. push: maxCtx
        public static uint[] Softmax()
        {
            KB k = new KB(Kernels.NormWg);
            KB.Buf sc = k.Buffer(0, k.F32, false), prm = k.Buffer(1, k.U32, true);
            k.Push(1);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.NormWg);
            k.Begin();
            uint j = k.Local(k.U32), mx = k.Local(k.F32), sum = k.Local(k.F32);
            uint maxCtx = k.PushU(0), n = k.Ld(prm, k.C(1));
            uint lid = k.LocalId(), sBase = k.IMul(k.GroupX(), maxCtx);

            k.Set(mx, k.CF(float.NegativeInfinity));
            k.For(j, lid, n, k.C(Kernels.NormWg), delegate { k.Set(mx, k.FMax(k.Get(mx, k.F32), k.Ld(sc, k.IAdd(sBase, k.Get(j, k.U32))))); });
            k.St(sh, lid, k.Get(mx, k.F32));
            k.Reduce(sh, lid, Kernels.NormWg, k.FMax);
            uint m = k.Ld(sh, k.C(0));
            k.Barrier();

            k.Set(sum, k.CF(0));
            k.For(j, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint idx = k.IAdd(sBase, k.Get(j, k.U32));
                uint e = k.Exp(k.FSub(k.Ld(sc, idx), m));
                k.St(sc, idx, e);
                k.Set(sum, k.FAdd(k.Get(sum, k.F32), e));
            });
            k.St(sh, lid, k.Get(sum, k.F32));
            k.Reduce(sh, lid, Kernels.NormWg, k.FAdd);
            uint inv = k.FDiv(k.CF(1f), k.Ld(sh, k.C(0)));
            k.For(j, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint idx = k.IAdd(sBase, k.Get(j, k.U32));
                k.St(sc, idx, k.F16(k.FMul(k.Ld(sc, idx), inv)));
            });
            return k.Finish();
        }

        // Partial weighted V over one 32-key chunk. Grid: (nChunks, nHeadKv).
        // bindings: 0 p, 1 vc (packed fp16), 2 partial, 3 params. push: headDim, kvDim, maxCtx, nHead, ring
        public static uint[] OutputPartial(uint group) { return OutputPartial(group, KvType.F16); }

        public static uint[] OutputPartial(uint group, KvType kvType)
        {
            KB k = new KB(OutWg);
            KB.Buf p = k.Buffer(0, k.F32, true), vc = k.Buffer(1, k.U32, true), part = k.Buffer(2, k.F32, false), prm = k.Buffer(3, k.U32, true);
            k.Push(5);
            k.Begin();
            uint j = k.Local(k.U32), dv = k.Local(k.U32);
            uint[] acc = new uint[group];
            for (int a = 0; a < group; a++) acc[a] = k.Local(k.F32);
            uint headDim = k.PushU(0), kvDim = k.PushU(1), maxCtx = k.PushU(2), nHead = k.PushU(3), ring = k.PushU(4);
            uint n = k.Ld(prm, k.C(1)), first = k.Ld(prm, k.C(4));
            uint c = k.GroupX(), kvh = k.GroupY(), lid = k.LocalId();
            uint t0 = k.IMul(c, k.C(Chunk));
            uint t1 = k.Select(k.U32, k.ULt(k.IAdd(t0, k.C(Chunk)), n), k.IAdd(t0, k.C(Chunk)), n);
            uint h0 = k.IMul(kvh, k.C(group));
            k.For(dv, lid, headDim, k.C(OutWg), delegate
            {
                uint d = k.Get(dv, k.U32);
                uint vOff = k.IAdd(k.IMul(kvh, headDim), d);
                for (int a = 0; a < group; a++) k.Set(acc[a], k.CF(0));
                k.For(j, t0, t1, k.C(1), delegate
                {
                    uint t = k.Get(j, k.U32);
                    uint v = KernelsKv.V(k, kvType, vc, k.UMod(k.IAdd(first, t), ring), vOff, ring, kvDim);
                    for (int a = 0; a < group; a++)
                    {
                        uint pv = k.Ld(p, k.IAdd(k.IMul(k.IAdd(h0, k.C((uint)a)), maxCtx), t));
                        k.Set(acc[a], k.FAdd(k.Get(acc[a], k.F32), k.FMul(pv, v)));
                    }
                });
                for (int a = 0; a < group; a++)
                {
                    uint h = k.IAdd(h0, k.C((uint)a));
                    k.St(part, k.IAdd(k.IMul(k.IAdd(k.IMul(c, nHead), h), headDim), d), k.Get(acc[a], k.F32));
                }
            });
            return k.Finish();
        }

        // out[i] = sum over chunks in order. One thread per (h, d). bindings: 0 partial, 1 out, 2 params. push: nHead*headDim
        public static uint[] OutputReduce()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf part = k.Buffer(0, k.F32, true), outB = k.Buffer(1, k.F32, false), prm = k.Buffer(2, k.U32, true);
            k.Push(1);
            k.Begin();
            uint c = k.Local(k.U32), acc = k.Local(k.F32);
            uint total = k.PushU(0), nChunks = k.Ld(prm, k.C(2));
            uint i = k.GlobalId();
            k.If(k.ULt(i, total), delegate
            {
                k.Set(acc, k.CF(0));
                k.For(c, k.C(0), nChunks, k.C(1), delegate
                {
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.Ld(part, k.IAdd(k.IMul(k.Get(c, k.U32), total), i))));
                });
                k.St(outB, i, k.Get(acc, k.F32));
            });
            return k.Finish();
        }

    }
}
