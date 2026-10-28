// Gemma 4 26B A4B mixture-of-experts kernels (decode, one token). C# 5 only.
// Math follows build_moe_ffn in llama.cpp (MIT) for gemma4: router logits = W_gate_inp (rmsnorm(x) / sqrt(n) * s),
// softmax, top-k, weights renormalised over the chosen k, expert out = down(gelu(gate) * up) * down_scale[e] * w.
// The routing stays on the GPU (ids and weights in a small buffer), so the decode pass is one reusable command buffer.
//   RouterLogits  one workgroup per expert: logits[e]
//   RouterSelect  one workgroup: top-k ids and weights -> route[0..k) ids, route[k..2k) weights (f32 bits)
//   GateUp        Q4_0 matvec over the k chosen experts' fused gate|up rows -> g[e*half + r], u[e*half + r]
//   Down          Q4_0 matvec over the k experts' down rows, weighted sum of the k outputs in one pass -> y[r]
//   Norm2         out = rmsnorm(a) * wa + rmsnorm(b) * wb (the shared-MLP and expert branches)
namespace Phoenix
{
    public static class KernelsMoe
    {
        // bindings: 0 x f32 [T][n], 1 s f32 (gate_inp.scale), 2 W f32 [nExp][n], 3 logits f32 [T][nExp]. push: n, eps, 1/sqrt(n), nExp
        // grid (nExp, T)
        public static uint[] RouterLogits(bool subgroups)
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, true), s = k.Buffer(1, k.F32, true), w = k.Buffer(2, k.F32, true), lg = k.Buffer(3, k.F32, false);
            k.Push(4);
            KB.Shared sh1 = k.SharedArray(k.F32, Kernels.Wg), sh2 = k.SharedArray(k.F32, Kernels.Wg);
            k.Begin();
            uint i = k.Local(k.U32), ss = k.Local(k.F32), dd = k.Local(k.F32);
            uint n = k.PushU(0), eps = k.PushF(1), invSqrtN = k.PushF(2), nExp = k.PushU(3);
            uint e = k.GroupX(), t = k.GroupY(), lid = k.LocalId();
            uint xOff = k.IMul(t, n);
            uint rowBase = k.IMul(e, n);
            k.Set(ss, k.CF(0)); k.Set(dd, k.CF(0));
            k.For(i, lid, n, k.C(Kernels.Wg), delegate
            {
                uint ii = k.Get(i, k.U32);
                uint xi = k.Ld(x, k.IAdd(xOff, ii));
                k.Set(ss, k.FAdd(k.Get(ss, k.F32), k.FMul(xi, xi)));
                k.Set(dd, k.FAdd(k.Get(dd, k.F32), k.FMul(k.FMul(xi, k.Ld(s, ii)), k.Ld(w, k.IAdd(rowBase, ii)))));
            });
            uint tss = k.SumAll(k.Get(ss, k.F32), sh1, lid, Kernels.Wg, subgroups);
            uint tdd = k.SumAll(k.Get(dd, k.F32), sh2, lid, Kernels.Wg, subgroups);
            uint rinv = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(tss, k.ToF(n)), eps)));
            k.If(k.IEq(lid, k.C(0)), delegate { k.St(lg, k.IAdd(k.IMul(t, nExp), e), k.FMul(k.FMul(tdd, rinv), invSqrtN)); });
            return k.Finish();
        }

        public const uint MaxExperts = 256, MaxUsed = 16;

        // bindings: 0 logits f32 [T][nExp], 1 dscale f32 (ffn_down_exps.scale, per expert), 2 route u32 [T][2 nUsed].
        // push: nExp, nUsed. grid (T). Per token: route[0..k) expert ids (best first), route[k..2k) weights.
        // Selection by rank: every expert counts the experts that beat it (higher logit, or equal with a lower index)
        // in parallel; ranks below k are the top k in order, the same choice as a sequential argmax (no serial scan).
        // Weights: exp(l - max) over the chosen experts only, divided by their sum. That is softmax over all experts
        // followed by renormalisation (llama.cpp), without the full-softmax sum's rounding. The down scale is folded in.
        public static uint[] RouterSelect()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf lg = k.Buffer(0, k.F32, true), ds = k.Buffer(1, k.F32, true), route = k.Buffer(2, k.U32, false);
            k.Push(2);
            KB.Shared sl = k.SharedArray(k.F32, MaxExperts), sv = k.SharedArray(k.F32, MaxUsed), si = k.SharedArray(k.U32, MaxUsed);
            k.Begin();
            uint i = k.Local(k.U32), j = k.Local(k.U32), rank = k.Local(k.U32), sum = k.Local(k.F32);
            uint nExp = k.PushU(0), nUsed = k.PushU(1);
            uint t = k.GroupX(), lid = k.LocalId();
            uint lOff = k.IMul(t, nExp), rOff = k.IMul(t, k.IMul(nUsed, k.C(2)));
            k.For(i, lid, nExp, k.C(Kernels.Wg), delegate { uint ii = k.Get(i, k.U32); k.St(sl, ii, k.Ld(lg, k.IAdd(lOff, ii))); });
            k.Barrier();
            k.For(i, lid, nExp, k.C(Kernels.Wg), delegate
            {
                uint e = k.Get(i, k.U32);
                uint v = k.Ld(sl, e);
                k.Set(rank, k.C(0));
                k.For(j, k.C(0), nExp, k.C(1), delegate
                {
                    uint o = k.Get(j, k.U32), w = k.Ld(sl, o);
                    uint beats = k.S.Value(Op.LogicalOr, k.Bool, k.S.Value(Op.FOrdGreaterThan, k.Bool, w, v),
                        k.S.Value(Op.LogicalAnd, k.Bool, k.S.Value(Op.FOrdEqual, k.Bool, w, v), k.ULt(o, e)));
                    k.Set(rank, k.IAdd(k.Get(rank, k.U32), k.Select(k.U32, beats, k.C(1), k.C(0))));
                });
                uint rk = k.Get(rank, k.U32);
                k.If(k.ULt(rk, nUsed), delegate { k.St(sv, rk, v); k.St(si, rk, e); });
            });
            k.Barrier();
            k.If(k.IEq(lid, k.C(0)), delegate
            {
                uint m = k.Ld(sv, k.C(0));
                k.Set(sum, k.CF(0));
                k.For(j, k.C(0), nUsed, k.C(1), delegate
                {
                    uint jj = k.Get(j, k.U32);
                    uint p = k.Exp(k.FSub(k.Ld(sv, jj), m));
                    k.St(sv, jj, p);
                    k.Set(sum, k.FAdd(k.Get(sum, k.F32), p));
                });
                uint inv = k.FDiv(k.CF(1f), k.Get(sum, k.F32));
                k.For(j, k.C(0), nUsed, k.C(1), delegate
                {
                    uint jj = k.Get(j, k.U32);
                    uint id = k.Ld(si, jj);
                    k.St(route, k.IAdd(rOff, jj), id);
                    k.St(route, k.IAdd(rOff, k.IAdd(nUsed, jj)), k.S.Value(Op.Bitcast, k.U32, k.FMul(k.FMul(k.Ld(sv, jj), inv), k.Ld(ds, id))));
                });
            });
            return k.Finish();
        }

        // One Q4_0 block dot (weights blk, activations block xb): isum * (d_w * d_x).
        static uint BlockDot(KB k, KB.Buf wq, KB.Buf wd, KB.Buf xq, KB.Buf xd, uint blk, uint xb, bool dot)
        {
            uint wBase = k.IMul(blk, k.C(4)), xBase = k.IMul(xb, k.C(8));
            uint isum = k.CI(0);
            for (uint w = 0; w < 4; w++)
            {
                uint ww = k.Ld(wq, k.IAdd(wBase, k.C(w)));
                uint lo = k.And(ww, k.C(0x0F0F0F0F)), hi = k.And(k.Shr(ww, k.C(4)), k.C(0x0F0F0F0F));
                uint xlo = k.Ld(xq, k.IAdd(xBase, k.C(w))), xhi = k.Ld(xq, k.IAdd(xBase, k.C(w + 4)));
                if (dot)
                {
                    uint slo = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(lo, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                    uint shi = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(hi, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                    isum = k.IAddS(isum, k.IAddS(k.Dot4(slo, xlo, true), k.Dot4(shi, xhi, true)));
                }
                else
                {
                    for (int kk = 0; kk < 4; kk++)
                    {
                        uint ql = k.ISubS(k.AsI(k.And(k.Shr(lo, k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                        uint qh = k.ISubS(k.AsI(k.And(k.Shr(hi, k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                        isum = k.IAddS(isum, k.IAddS(k.IMulS(ql, k.SByte(xlo, kk)), k.IMulS(qh, k.SByte(xhi, kk))));
                    }
                }
            }
            return k.FMul(k.SToF(isum), k.FMul(KernelsAttention.HalfAt(k, wd, blk), k.Ld(xd, xb)));
        }

        // bindings: 0 wq, 1 wd, 2 xq, 3 xd, 4 g f32, 5 u f32, 6 route u32. push: rows (2*half), nb, stride, half
        // grid: (rows, nUsed). Row r of expert slot e reads stored row route[e] * rows + r.
        public static uint[] GateUp(bool dot, bool subgroups)
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.U32, true), xq = k.Buffer(2, k.U32, true),
                xd = k.Buffer(3, k.F32, true), g = k.Buffer(4, k.F32, false), u = k.Buffer(5, k.F32, false), route = k.Buffer(6, k.U32, true);
            k.Push(4);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.Wg);
            k.Begin();
            uint bv = k.Local(k.U32), sum = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), stride = k.PushU(2), half = k.PushU(3);
            uint r = k.GroupX(), e = k.GroupY(), lid = k.LocalId();
            uint rowBase = k.IMul(k.IAdd(k.IMul(k.Ld(route, e), rows), r), stride);
            k.Set(sum, k.CF(0));
            k.For(bv, lid, nb, k.C(Kernels.Wg), delegate
            {
                uint b = k.Get(bv, k.U32);
                k.Set(sum, k.FAdd(k.Get(sum, k.F32), BlockDot(k, wq, wd, xq, xd, k.IAdd(rowBase, b), b, dot)));
            });
            uint total = k.SumAll(k.Get(sum, k.F32), sh, lid, Kernels.Wg, subgroups);
            k.If(k.IEq(lid, k.C(0)), delegate
            {
                uint isGate = k.ULt(r, half);
                k.If(isGate, delegate { k.St(g, k.IAdd(k.IMul(e, half), r), total); });
                k.If(k.S.Value(Op.LogicalNot, k.Bool, isGate), delegate { k.St(u, k.IAdd(k.IMul(e, half), k.S.Value(Op.ISub, k.U32, r, half)), total); });
            });
            return k.Finish();
        }

        // bindings: 0 wq, 1 wd, 2 xq, 3 xd (nUsed * nb blocks, slot-major), 4 y f32, 5 route u32. push: rows, nb, stride, nUsed
        // y[r] = sum_e w[e] * dot(down[route[e]] row r, act slot e).
        public static uint[] Down(bool dot, bool subgroups)
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.U32, true), xq = k.Buffer(2, k.U32, true),
                xd = k.Buffer(3, k.F32, true), y = k.Buffer(4, k.F32, false), route = k.Buffer(5, k.U32, true);
            k.Push(4);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.Wg);
            k.Begin();
            uint jv = k.Local(k.U32), sum = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), stride = k.PushU(2), nUsed = k.PushU(3);
            uint r = k.GroupX(), lid = k.LocalId();
            k.Set(sum, k.CF(0));
            k.For(jv, lid, k.IMul(nUsed, nb), k.C(Kernels.Wg), delegate
            {
                uint j = k.Get(jv, k.U32);
                uint e = k.UDiv(j, nb), b = k.UMod(j, nb);
                uint blk = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Ld(route, e), rows), r), stride), b);
                uint we = k.S.Value(Op.Bitcast, k.F32, k.Ld(route, k.IAdd(nUsed, e)));
                k.Set(sum, k.FAdd(k.Get(sum, k.F32), k.FMul(we, BlockDot(k, wq, wd, xq, xd, blk, j, dot))));
            });
            uint total = k.SumAll(k.Get(sum, k.F32), sh, lid, Kernels.Wg, subgroups);
            k.If(k.IEq(lid, k.C(0)), delegate { k.St(y, r, total); });
            return k.Finish();
        }

        // bindings: 0 a, 1 b, 2 wa, 3 wb, 4 out (rows of n). push: n, eps. grid (rows)
        public static uint[] Norm2()
        {
            KB k = new KB(Kernels.NormWg);
            KB.Buf a = k.Buffer(0, k.F32, true), b = k.Buffer(1, k.F32, true), wa = k.Buffer(2, k.F32, true), wb = k.Buffer(3, k.F32, true), o = k.Buffer(4, k.F32, false);
            k.Push(2);
            KB.Shared sh1 = k.SharedArray(k.F32, Kernels.NormWg), sh2 = k.SharedArray(k.F32, Kernels.NormWg);
            k.Begin();
            uint i = k.Local(k.U32), sa = k.Local(k.F32), sb = k.Local(k.F32);
            uint n = k.PushU(0), eps = k.PushF(1);
            uint lid = k.LocalId(), off = k.IMul(k.GroupX(), n);
            k.Set(sa, k.CF(0)); k.Set(sb, k.CF(0));
            k.For(i, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint ii = k.IAdd(off, k.Get(i, k.U32));
                uint va = k.Ld(a, ii), vb = k.Ld(b, ii);
                k.Set(sa, k.FAdd(k.Get(sa, k.F32), k.FMul(va, va)));
                k.Set(sb, k.FAdd(k.Get(sb, k.F32), k.FMul(vb, vb)));
            });
            k.St(sh1, lid, k.Get(sa, k.F32));
            k.Reduce(sh1, lid, Kernels.NormWg, k.FAdd);
            k.St(sh2, lid, k.Get(sb, k.F32));
            k.Reduce(sh2, lid, Kernels.NormWg, k.FAdd);
            uint ra = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(k.Ld(sh1, k.C(0)), k.ToF(n)), eps)));
            uint rb = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(k.Ld(sh2, k.C(0)), k.ToF(n)), eps)));
            k.For(i, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint c = k.Get(i, k.U32), ii = k.IAdd(off, c);
                k.St(o, ii, k.FAdd(k.FMul(k.FMul(k.Ld(a, ii), ra), k.Ld(wa, c)), k.FMul(k.FMul(k.Ld(b, ii), rb), k.Ld(wb, c))));
            });
            return k.Finish();
        }

        // ---------------- batched prefill (T tokens) ----------------

        public const uint RtTok = 16;   // tokens per router tile

        // Router logits of T rows, RtTok tokens per workgroup, one thread per expert (nExp threads): the weights are read
        // once per tile instead of once per token. W is transposed ([n][nExp], coalesced across the experts).
        // bindings: 0 x f32 [T][n], 1 s f32, 2 Wt f32 [n][nExp], 3 logits f32 [T][nExp]. push: n, eps, 1/sqrt(n), T
        public static uint[] RouterLogitsRows(uint nExp)
        {
            KB k = new KB(nExp);
            KB.Buf x = k.Buffer(0, k.F32, true), s = k.Buffer(1, k.F32, true), w = k.Buffer(2, k.F32, true), lg = k.Buffer(3, k.F32, false);
            k.Push(4);
            KB.Shared xs = k.SharedArray(k.F32, RtTok * 64), rinv = k.SharedArray(k.F32, RtTok), red = k.SharedArray(k.F32, nExp);
            k.Begin();
            uint i = k.Local(k.U32), kc = k.Local(k.U32), ss = k.Local(k.F32);
            uint[] acc = new uint[RtTok];
            for (int t = 0; t < RtTok; t++) acc[t] = k.Local(k.F32);
            uint n = k.PushU(0), eps = k.PushF(1), invSqrtN = k.PushF(2), T = k.PushU(3);
            uint e = k.LocalId(), tok0 = k.IMul(k.GroupX(), k.C(RtTok));
            for (uint t = 0; t < RtTok; t++)
            {
                uint tk = k.IAdd(tok0, k.C(t));
                uint row = k.IMul(k.S.Glsl(k.U32, Spv.GlslUMin, tk, k.S.Value(Op.ISub, k.U32, T, k.C(1))), n);
                k.Set(ss, k.CF(0));
                k.For(i, e, n, k.C(nExp), delegate { uint v = k.Ld(x, k.IAdd(row, k.Get(i, k.U32))); k.Set(ss, k.FAdd(k.Get(ss, k.F32), k.FMul(v, v))); });
                k.St(red, e, k.Get(ss, k.F32));
                k.Reduce(red, e, nExp, k.FAdd);
                uint tt = t;
                k.If(k.IEq(e, k.C(0)), delegate { k.St(rinv, k.C(tt), k.FMul(k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(k.Ld(red, k.C(0)), k.ToF(n)), eps))), invSqrtN)); });
                k.Barrier();
                k.Set(acc[t], k.CF(0));
            }
            k.For(kc, k.C(0), n, k.C(64), delegate
            {
                uint c0 = k.Get(kc, k.U32);
                for (uint q = 0; q < RtTok * 64 / nExp; q++)
                {
                    uint idx = k.IAdd(k.C(q * nExp), e);
                    uint t = k.Shr(idx, k.C(6)), j = k.And(idx, k.C(63));
                    uint tk = k.S.Glsl(k.U32, Spv.GlslUMin, k.IAdd(tok0, t), k.S.Value(Op.ISub, k.U32, T, k.C(1)));
                    uint col = k.IAdd(c0, j);
                    k.St(xs, idx, k.FMul(k.Ld(x, k.IAdd(k.IMul(tk, n), col)), k.Ld(s, col)));
                }
                k.Barrier();
                for (uint j = 0; j < 64; j++)
                {
                    uint wv = k.Ld(w, k.IAdd(k.IMul(k.IAdd(c0, k.C(j)), k.C(nExp)), e));
                    for (uint t = 0; t < RtTok; t++) k.Set(acc[t], k.FAdd(k.Get(acc[t], k.F32), k.FMul(k.Ld(xs, k.C(t * 64 + j)), wv)));
                }
                k.Barrier();
            });
            for (uint t = 0; t < RtTok; t++)
            {
                uint tk = k.IAdd(tok0, k.C(t));
                uint tt = t;
                k.If(k.ULt(tk, T), delegate { k.St(lg, k.IAdd(k.IMul(tk, k.C(nExp)), e), k.FMul(k.Get(acc[tt], k.F32), k.Ld(rinv, k.C(tt)))); });
            }
            return k.Finish();
        }
        // Assignment a = t * nUsed + j: token t's j-th expert. Experts gather their assignments (Bucket), so each expert's
        // weights are read once per chunk of tokens (GroupedMm) instead of once per token.

        // bindings: 0 route u32 [T][2 nUsed], 1 list u32 [nExp][T], 2 cnt u32 [nExp]. push: T, nUsed. grid (nExp)
        // list[e][..cnt[e]) = the assignments routed to expert e, in token order.
        public static uint[] Bucket()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf route = k.Buffer(0, k.U32, true), list = k.Buffer(1, k.U32, false), cnt = k.Buffer(2, k.U32, false);
            k.Push(2);
            KB.Shared counts = k.SharedArray(k.U32, Kernels.Wg);
            k.Begin();
            uint t = k.Local(k.U32), j = k.Local(k.U32), c = k.Local(k.U32), p = k.Local(k.U32);
            uint T = k.PushU(0), nUsed = k.PushU(1);
            uint e = k.GroupX(), lid = k.LocalId();
            uint per = k.UDiv(k.IAdd(T, k.C(Kernels.Wg - 1)), k.C(Kernels.Wg));
            uint t0 = k.S.Glsl(k.U32, Spv.GlslUMin, k.IMul(lid, per), T), t1 = k.S.Glsl(k.U32, Spv.GlslUMin, k.IAdd(t0, per), T);
            uint r2 = k.IMul(nUsed, k.C(2));
            k.Set(c, k.C(0));
            k.For(t, t0, t1, k.C(1), delegate
            {
                uint tt = k.Get(t, k.U32);
                k.For(j, k.C(0), nUsed, k.C(1), delegate
                {
                    uint hit = k.IEq(k.Ld(route, k.IAdd(k.IMul(tt, r2), k.Get(j, k.U32))), e);
                    k.Set(c, k.IAdd(k.Get(c, k.U32), k.Select(k.U32, hit, k.C(1), k.C(0))));
                });
            });
            k.St(counts, lid, k.Get(c, k.U32));
            k.Barrier();
            // exclusive prefix over the threads' counts (64 values), by every thread for its own offset
            k.Set(p, k.C(0));
            k.For(j, k.C(0), lid, k.C(1), delegate { k.Set(p, k.IAdd(k.Get(p, k.U32), k.Ld(counts, k.Get(j, k.U32)))); });
            uint baseL = k.IMul(e, T);
            k.For(t, t0, t1, k.C(1), delegate
            {
                uint tt = k.Get(t, k.U32);
                k.For(j, k.C(0), nUsed, k.C(1), delegate
                {
                    uint jj = k.Get(j, k.U32);
                    k.If(k.IEq(k.Ld(route, k.IAdd(k.IMul(tt, r2), jj)), e), delegate
                    {
                        k.St(list, k.IAdd(baseL, k.Get(p, k.U32)), k.IAdd(k.IMul(tt, nUsed), jj));
                        k.Set(p, k.IAdd(k.Get(p, k.U32), k.C(1)));
                    });
                });
            });
            k.If(k.IEq(lid, k.C(Kernels.Wg - 1)), delegate { k.St(cnt, e, k.Get(p, k.U32)); });
            return k.Finish();
        }

        public const uint GmTok = 16, GmKb = 8;   // tokens per tile, blocks per shared-memory step

        // Y = W_e X for the assignments of expert e (KernelsPrefill.MatMulQ4Q8 with a gather): Q4_0 weights of expert e
        // (stored rows e * rows ..), Q8_0 activation row a / actDiv for assignment a. Output row a: y[a * rows + r], or
        // with half > 0 the fused gate|up split: r < half -> y[a * half + r], else y2[a * half + r - half].
        // bindings: 0 wq, 1 wd, 2 xq, 3 xd, 4 y, 5 y2, 6 list, 7 cnt. push: rows, nb, stride, listStride, actDiv, half
        // grid (ceil(rows / (64 rpt)), ceil(maxTokens / 16), nExp); tiles past an expert's count do nothing.
        public static uint[] GroupedMm(bool dot, uint rpt)
        {
            const uint Th = 64;
            KB k = new KB(Th);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.U32, true), xq = k.Buffer(2, k.U32, true), xd = k.Buffer(3, k.F32, true),
                y = k.Buffer(4, k.F32, false), y2 = k.Buffer(5, k.F32, false), list = k.Buffer(6, k.U32, true), cnt = k.Buffer(7, k.U32, true);
            k.Push(6);
            KB.Shared xs = k.SharedArray(k.U32, GmTok * GmKb * 8), xds = k.SharedArray(k.F32, GmTok * GmKb);
            KB.Shared arow = k.SharedArray(k.U32, GmTok), orow = k.SharedArray(k.U32, GmTok);
            k.Begin();
            uint b0v = k.Local(k.U32), i = k.Local(k.U32), bbv = k.Local(k.U32);
            uint[,] acc = new uint[rpt, GmTok];
            for (int a = 0; a < rpt; a++) for (int t = 0; t < GmTok; t++) acc[a, t] = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), stride = k.PushU(2), ls = k.PushU(3), actDiv = k.PushU(4), half = k.PushU(5);
            uint lid = k.LocalId(), e = k.GroupZ();
            uint T = k.Ld(cnt, e);
            uint tok0 = k.IMul(k.GroupY(), k.C(GmTok));
            k.If(k.ULt(tok0, T), delegate
            {
                k.If(k.ULt(lid, k.C(GmTok)), delegate
                {
                    uint tk = k.IAdd(tok0, lid);
                    uint ok = k.ULt(tk, T);
                    uint a = k.Select(k.U32, ok, k.Ld(list, k.IAdd(k.IMul(e, ls), k.Select(k.U32, ok, tk, k.C(0)))), k.C(0));
                    k.St(arow, lid, k.UDiv(a, actDiv));
                    k.St(orow, lid, a);
                });
                k.Barrier();
                uint rBase = k.IAdd(k.IMul(k.GroupX(), k.C(Th * rpt)), lid);
                uint[] rowOk = new uint[rpt], rowBase = new uint[rpt], rr = new uint[rpt];
                for (uint a = 0; a < rpt; a++)
                {
                    rr[a] = k.IAdd(rBase, k.C(a * Th));
                    rowOk[a] = k.ULt(rr[a], rows);
                    rowBase[a] = k.IMul(k.IAdd(k.IMul(e, rows), k.Select(k.U32, rowOk[a], rr[a], k.C(0))), stride);
                }
                for (int a = 0; a < rpt; a++) for (int t = 0; t < GmTok; t++) k.Set(acc[a, t], k.CF(0));
                k.For(b0v, k.C(0), nb, k.C(GmKb), delegate
                {
                    uint b0 = k.Get(b0v, k.U32);
                    k.For(i, lid, k.C(GmTok * GmKb * 8), k.C(Th), delegate
                    {
                        uint idx = k.Get(i, k.U32);
                        uint tt = k.UDiv(idx, k.C(GmKb * 8)), rem = k.UMod(idx, k.C(GmKb * 8));
                        uint blk = k.IAdd(b0, k.UDiv(rem, k.C(8))), tk = k.IAdd(tok0, tt);
                        uint ok = k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(tk, T), k.ULt(blk, nb));
                        uint src = k.IAdd(k.IMul(k.IAdd(k.IMul(k.Ld(arow, tt), nb), k.Select(k.U32, ok, blk, k.C(0))), k.C(8)), k.UMod(rem, k.C(8)));
                        k.St(xs, idx, k.Select(k.U32, ok, k.Ld(xq, src), k.C(0)));
                    });
                    k.For(i, lid, k.C(GmTok * GmKb), k.C(Th), delegate
                    {
                        uint idx = k.Get(i, k.U32);
                        uint tt = k.UDiv(idx, k.C(GmKb)), blk = k.IAdd(b0, k.UMod(idx, k.C(GmKb))), tk = k.IAdd(tok0, tt);
                        uint ok = k.S.Value(Op.LogicalAnd, k.Bool, k.ULt(tk, T), k.ULt(blk, nb));
                        uint src = k.IAdd(k.IMul(k.Ld(arow, tt), nb), k.Select(k.U32, ok, blk, k.C(0)));
                        k.St(xds, idx, k.Select(k.F32, ok, k.Ld(xd, src), k.CF(0)));
                    });
                    k.Barrier();
                    uint kbEnd = k.S.Glsl(k.U32, Spv.GlslUMin, k.C(GmKb), k.S.Value(Op.ISub, k.U32, nb, b0));
                    k.For(bbv, k.C(0), kbEnd, k.C(1), delegate
                    {
                        uint bb = k.Get(bbv, k.U32);
                        uint[] dw = new uint[rpt];
                        uint[,] slo = new uint[rpt, 4], shi = new uint[rpt, 4];
                        for (uint a = 0; a < rpt; a++)
                        {
                            uint blk = k.IAdd(rowBase[a], k.IAdd(b0, bb));
                            dw[a] = KernelsAttention.HalfAt(k, wd, blk);
                            for (uint w = 0; w < 4; w++)
                            {
                                uint ww = k.Ld(wq, k.IAdd(k.IMul(blk, k.C(4)), k.C(w)));
                                uint lo = k.And(ww, k.C(0x0F0F0F0F)), hi = k.And(k.Shr(ww, k.C(4)), k.C(0x0F0F0F0F));
                                if (dot)
                                {
                                    slo[a, w] = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(lo, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                                    shi[a, w] = k.S.Value(Op.BitwiseXor, k.U32, k.S.Value(Op.ISub, k.U32, k.Or(hi, k.C(0x80808080)), k.C(0x08080808)), k.C(0x80808080));
                                }
                                else { slo[a, w] = lo; shi[a, w] = hi; }
                            }
                        }
                        for (int t = 0; t < GmTok; t++)
                        {
                            uint xb = k.IAdd(k.C((uint)(t * GmKb * 8)), k.IMul(bb, k.C(8)));
                            uint[] xw = new uint[8];
                            for (uint w = 0; w < 8; w++) xw[w] = k.Ld(xs, k.IAdd(xb, k.C(w)));
                            uint dx = k.Ld(xds, k.IAdd(k.C((uint)(t * GmKb)), bb));
                            for (uint a = 0; a < rpt; a++)
                            {
                                uint isum = k.CI(0);
                                for (uint w = 0; w < 4; w++)
                                {
                                    if (dot) isum = k.Dot4Acc(shi[a, w], xw[w + 4], k.Dot4Acc(slo[a, w], xw[w], isum));
                                    else
                                        for (int kk = 0; kk < 4; kk++)
                                        {
                                            uint ql = k.ISubS(k.AsI(k.And(k.Shr(slo[a, w], k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                                            uint qh = k.ISubS(k.AsI(k.And(k.Shr(shi[a, w], k.C((uint)(8 * kk))), k.C(0xFF))), k.CI(8));
                                            isum = k.IAddS(isum, k.IAddS(k.IMulS(ql, k.SByte(xw[w], kk)), k.IMulS(qh, k.SByte(xw[w + 4], kk))));
                                        }
                                }
                                k.Set(acc[a, t], k.FAdd(k.Get(acc[a, t], k.F32), k.FMul(k.SToF(isum), k.FMul(dw[a], dx))));
                            }
                        }
                    });
                    k.Barrier();
                });
                for (uint a = 0; a < rpt; a++)
                {
                    uint aa = a;
                    k.If(rowOk[a], delegate
                    {
                        for (int t = 0; t < GmTok; t++)
                        {
                            uint tk = k.IAdd(tok0, k.C((uint)t));
                            int tt = t;
                            k.If(k.ULt(tk, T), delegate
                            {
                                uint o = k.Ld(orow, k.C((uint)tt)), r = rr[aa], v = k.Get(acc[aa, tt], k.F32);
                                uint split = k.S.Value(Op.INotEqual, k.Bool, half, k.C(0));
                                k.If(k.S.Value(Op.LogicalNot, k.Bool, split), delegate { k.St(y, k.IAdd(k.IMul(o, rows), r), v); });
                                k.If(split, delegate
                                {
                                    uint isGate = k.ULt(r, half);
                                    k.If(isGate, delegate { k.St(y, k.IAdd(k.IMul(o, half), r), v); });
                                    k.If(k.S.Value(Op.LogicalNot, k.Bool, isGate), delegate { k.St(y2, k.IAdd(k.IMul(o, half), k.S.Value(Op.ISub, k.U32, r, half)), v); });
                                });
                            });
                        }
                    });
                }
            });
            return k.Finish();
        }

        // bindings: 0 yd f32 [T * nUsed][d], 1 route u32, 2 out f32 [T][d]. push: d, nUsed. grid (ceil(d / 64), T)
        // out[t][r] = sum_j w[t][j] * yd[t * nUsed + j][r] (weights already hold the down scale).
        public static uint[] Combine()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf yd = k.Buffer(0, k.F32, true), route = k.Buffer(1, k.U32, true), o = k.Buffer(2, k.F32, false);
            k.Push(2);
            k.Begin();
            uint j = k.Local(k.U32), s = k.Local(k.F32);
            uint d = k.PushU(0), nUsed = k.PushU(1);
            uint r = k.IAdd(k.IMul(k.GroupX(), k.C(Kernels.Wg)), k.LocalId()), t = k.GroupY();
            k.If(k.ULt(r, d), delegate
            {
                k.Set(s, k.CF(0));
                uint rOff = k.IMul(t, k.IMul(nUsed, k.C(2)));
                k.For(j, k.C(0), nUsed, k.C(1), delegate
                {
                    uint jj = k.Get(j, k.U32);
                    uint w = k.S.Value(Op.Bitcast, k.F32, k.Ld(route, k.IAdd(rOff, k.IAdd(nUsed, jj))));
                    uint a = k.IAdd(k.IMul(t, nUsed), jj);
                    k.Set(s, k.FAdd(k.Get(s, k.F32), k.FMul(w, k.Ld(yd, k.IAdd(k.IMul(a, d), r)))));
                });
                k.St(o, k.IAdd(k.IMul(t, d), r), k.Get(s, k.F32));
            });
            return k.Finish();
        }
    }
}
