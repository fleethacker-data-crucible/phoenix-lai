// Kernels added for Gemma 4: NeoX RoPE, per-head RMSNorm, norm + residual add + scale, Q4_0 matvec. C# 5 only.
// Each mirrors the matching GemmaCpu / CpuRef step.
using System;

namespace Phoenix
{
    public static class KernelsGemma
    {
        // NeoX RoPE (CpuRef.RopeNeox): rotates (x[i], x[i + halfRot]) of every head with the host cos/sin table.
        // bindings: 0 x, 1 cs. push: nHeads, headDim, halfRot
        public static uint[] RopeNeox()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, false), cs = k.Buffer(1, k.F32, true);
            k.Push(3);
            k.Begin();
            uint p = k.GlobalId();
            uint nHeads = k.PushU(0), headDim = k.PushU(1), half = k.PushU(2);
            k.If(k.ULt(p, k.IMul(nHeads, half)), delegate
            {
                uint h = k.UDiv(p, half), i = k.UMod(p, half);
                uint o = k.IAdd(k.IMul(h, headDim), i), o2 = k.IAdd(o, half);
                uint a = k.Ld(x, o), b = k.Ld(x, o2);
                uint c = k.Ld(cs, k.IMul(i, k.C(2))), s = k.Ld(cs, k.IAdd(k.IMul(i, k.C(2)), k.C(1)));
                k.St(x, o, k.FSub(k.FMul(a, c), k.FMul(b, s)));
                k.St(x, o2, k.FAdd(k.FMul(a, s), k.FMul(b, c)));
            });
            return k.Finish();
        }

        // Per-head RMSNorm: y[h*hd + d] = x[h*hd + d] / rms(head h) * (w[d] or 1). One workgroup per head.
        // x and y may be the same buffer. bindings: 0 x, 1 w, 2 y. push: headDim, eps(bits), useWeight
        public static uint[] HeadNorm()
        {
            KB k = new KB(Kernels.NormWg);
            KB.Buf x = k.Buffer(0, k.F32, true), w = k.Buffer(1, k.F32, true), y = k.Buffer(2, k.F32, false);
            k.Push(3);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.NormWg);
            k.Begin();
            uint i = k.Local(k.U32), acc = k.Local(k.F32);
            uint hd = k.PushU(0), eps = k.PushF(1), useW = k.PushU(2);
            uint lid = k.LocalId(), off = k.IMul(k.GroupX(), hd);
            k.Set(acc, k.CF(0));
            k.For(i, lid, hd, k.C(Kernels.NormWg), delegate
            {
                uint v = k.Ld(x, k.IAdd(off, k.Get(i, k.U32)));
                k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(v, v)));
            });
            k.St(sh, lid, k.Get(acc, k.F32));
            k.Reduce(sh, lid, Kernels.NormWg, k.FAdd);
            uint scale = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(k.Ld(sh, k.C(0)), k.ToF(hd)), eps)));
            k.For(i, lid, hd, k.C(Kernels.NormWg), delegate
            {
                uint d = k.Get(i, k.U32);
                uint wv = k.Select(k.F32, k.IEq(useW, k.C(1)), k.Ld(w, d), k.CF(1f));
                k.St(y, k.IAdd(off, d), k.FMul(k.FMul(k.Ld(x, k.IAdd(off, d)), scale), wv));
            });
            return k.Finish();
        }

        // y[i] = min(max(x[i], lo), hi) (Gemma 4 vision "clippable linear" input / output clamps; x and y may be the same).
        // bindings: 0 x, 1 y. push: n, lo(bits), hi(bits)
        public static uint[] Clamp()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, true), y = k.Buffer(1, k.F32, false);
            k.Push(3);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate { k.St(y, i, k.S.Glsl(k.F32, Spv.GlslFMin, k.FMax(k.Ld(x, i), k.PushF(1)), k.PushF(2))); });
            return k.Finish();
        }

        // g[i] = gelu_quick(g[i]) * u[i] = g * sigmoid(1.702 g) * u (llama.cpp's vision activation when the mmproj does not say
        // "use_gelu"; only for comparing against llama.cpp). bindings: 0 g, 1 u. push: n
        public static uint[] GeluQuickMul()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf g = k.Buffer(0, k.F32, false), u = k.Buffer(1, k.F32, true);
            k.Push(1);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate
            {
                uint x = k.Ld(g, i);
                uint sg = k.FDiv(k.CF(1f), k.FAdd(k.CF(1f), k.Exp(k.FMul(x, k.CF(-1.702f)))));
                k.St(g, i, k.FMul(k.FMul(x, sg), k.Ld(u, i)));
            });
            return k.Finish();
        }

        // E2B/E4B per-layer inputs (GemmaCpu.TokenPerLayerInputs) for T tokens; one workgroup per (token t, layer l),
        // group id t * nl + l: out[l][t][i] = (rmsnorm(proj[t][l*pl + i] * s) * w[i] + tok[t][l*pl + i]) * c.
        // tok rows come pre-scaled by sqrt(pl). bindings: 0 proj, 1 tok, 2 w, 3 out. push: pl, nl, T, eps, s, c (bits)
        public static uint[] PleInputs()
        {
            KB k = new KB(Kernels.NormWg);
            KB.Buf proj = k.Buffer(0, k.F32, true), tok = k.Buffer(1, k.F32, true), w = k.Buffer(2, k.F32, true), outB = k.Buffer(3, k.F32, false);
            k.Push(6);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.NormWg);
            k.Begin();
            uint i = k.Local(k.U32), acc = k.Local(k.F32);
            uint pl = k.PushU(0), nl = k.PushU(1), T = k.PushU(2), eps = k.PushF(3), s = k.PushF(4), c = k.PushF(5);
            uint lid = k.LocalId(), gid = k.GroupX();
            uint t = k.UDiv(gid, nl), l = k.UMod(gid, nl);
            uint inOff = k.IMul(gid, pl);                            // [t][l*pl] == (t*nl + l) * pl
            uint outOff = k.IMul(k.IAdd(k.IMul(l, T), t), pl);       // [l][t]
            k.Set(acc, k.CF(0));
            k.For(i, lid, pl, k.C(Kernels.NormWg), delegate
            {
                uint v = k.FMul(k.Ld(proj, k.IAdd(inOff, k.Get(i, k.U32))), s);
                k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(v, v)));
            });
            k.St(sh, lid, k.Get(acc, k.F32));
            k.Reduce(sh, lid, Kernels.NormWg, k.FAdd);
            uint scale = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(k.Ld(sh, k.C(0)), k.ToF(pl)), eps)));
            k.For(i, lid, pl, k.C(Kernels.NormWg), delegate
            {
                uint j = k.Get(i, k.U32), at = k.IAdd(inOff, j);
                uint nv = k.FMul(k.FMul(k.FMul(k.Ld(proj, at), s), scale), k.Ld(w, j));
                k.St(outB, k.IAdd(outOff, j), k.FMul(k.FAdd(nv, k.Ld(tok, at)), c));
            });
            return k.Finish();
        }

        // x = (x + rmsnorm(y) * w) * s  — Gemma's post-attention / post-FFN norm with the residual add and the layer
        // output scale (s = 1 except after the FFN). Single workgroup. bindings: 0 x, 1 y, 2 w. push: n, eps(bits), s(bits)
        public static uint[] NormAdd()
        {
            KB k = new KB(Kernels.NormWg);
            KB.Buf x = k.Buffer(0, k.F32, false), y = k.Buffer(1, k.F32, true), w = k.Buffer(2, k.F32, true);
            k.Push(3);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.NormWg);
            k.Begin();
            uint i = k.Local(k.U32), acc = k.Local(k.F32);
            uint n = k.PushU(0), eps = k.PushF(1), sc = k.PushF(2);
            uint lid = k.LocalId();
            k.Set(acc, k.CF(0));
            k.For(i, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint v = k.Ld(y, k.Get(i, k.U32));
                k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(v, v)));
            });
            k.St(sh, lid, k.Get(acc, k.F32));
            k.Reduce(sh, lid, Kernels.NormWg, k.FAdd);
            uint scale = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(k.Ld(sh, k.C(0)), k.ToF(n)), eps)));
            k.For(i, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint d = k.Get(i, k.U32);
                uint v = k.FAdd(k.Ld(x, d), k.FMul(k.FMul(k.Ld(y, d), scale), k.Ld(w, d)));
                k.St(x, d, k.Select(k.F32, k.FNe(sc, k.CF(1f)), k.FMul(v, sc), v));
            });
            return k.Finish();
        }

        // NormAdd followed by the next RMSNorm + Q8_0 quantization, in one workgroup:
        //   x = (x + rmsnorm(y) * w) * s;  then quantize rmsnorm(x) * w2 into (xq, xd) like KernelsQuant.Q80(RmsNorm).
        // Saves a dispatch and a full re-read of x per norm. bindings: 0 x, 1 y, 2 w, 3 w2, 4 xq u32, 5 xd f32.
        // push: n, eps(bits), s(bits)
        public static uint[] NormAddQuant() { return NormAddQuant(false); }

        public static uint[] NormAddQuant(bool subgroups)
        {
            KB k = new KB(Kernels.NormWg);
            KB.Buf x = k.Buffer(0, k.F32, false), y = k.Buffer(1, k.F32, true), w = k.Buffer(2, k.F32, true), w2 = k.Buffer(3, k.F32, true);
            KB.Buf xq = k.Buffer(4, k.U32, false), xd = k.Buffer(5, k.F32, false);
            k.Push(3);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.NormWg);
            k.Begin();
            uint i = k.Local(k.U32), acc = k.Local(k.F32), b = k.Local(k.U32), j = k.Local(k.U32), amax = k.Local(k.F32);
            uint n = k.PushU(0), eps = k.PushF(1), sc = k.PushF(2);
            uint lid = k.LocalId();
            // 1. rms of y
            k.Set(acc, k.CF(0));
            k.For(i, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint v = k.Ld(y, k.Get(i, k.U32));
                k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(v, v)));
            });
            uint sum1 = k.SumAll(k.Get(acc, k.F32), sh, lid, Kernels.NormWg, subgroups);
            uint scale1 = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(sum1, k.ToF(n)), eps)));
            k.Barrier();
            // 2. residual update, accumulating the new x's squares
            k.Set(acc, k.CF(0));
            k.For(i, lid, n, k.C(Kernels.NormWg), delegate
            {
                uint d = k.Get(i, k.U32);
                uint v = k.FAdd(k.Ld(x, d), k.FMul(k.FMul(k.Ld(y, d), scale1), k.Ld(w, d)));
                v = k.Select(k.F32, k.FNe(sc, k.CF(1f)), k.FMul(v, sc), v);
                k.St(x, d, v);
                k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(v, v)));
            });
            uint sum2 = k.SumAll(k.Get(acc, k.F32), sh, lid, Kernels.NormWg, subgroups);
            k.Barrier();   // the new x is visible to every thread
            uint scale2 = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(k.FDiv(sum2, k.ToF(n)), eps)));
            // 3. one thread per 32-element block: Q8_0 of rmsnorm(x) * w2 (same rounding as KernelsQuant.Q80)
            k.For(b, lid, k.UDiv(n, k.C(32)), k.C(Kernels.NormWg), delegate
            {
                uint o = k.IMul(k.Get(b, k.U32), k.C(32));
                k.Set(amax, k.CF(0));
                k.For(j, k.C(0), k.C(32), k.C(1), delegate
                {
                    uint idx = k.IAdd(o, k.Get(j, k.U32));
                    uint v = k.FMul(k.FMul(k.Ld(x, idx), scale2), k.Ld(w2, idx));
                    k.Set(amax, k.FMax(k.Get(amax, k.F32), k.Abs(v)));
                });
                uint dq = k.FDiv(k.Get(amax, k.F32), k.CF(127f));
                uint id = k.Select(k.F32, k.FNe(dq, k.CF(0)), k.FDiv(k.CF(1f), dq), k.CF(0));
                for (uint wv = 0; wv < 8; wv++)
                {
                    uint word = k.C(0);
                    for (uint e = 0; e < 4; e++)
                    {
                        uint idx = k.IAdd(o, k.C(wv * 4 + e));
                        uint v = k.FMul(k.FMul(k.Ld(x, idx), scale2), k.Ld(w2, idx));
                        uint t = k.FMul(v, id);
                        uint r = k.FMul(k.Sign(t), k.Floor(k.FAdd(k.Abs(t), k.CF(0.5f))));
                        uint qb = k.And(k.AsU(k.FToS(r)), k.C(0xFF));
                        word = k.Or(word, e == 0 ? qb : k.Shl(qb, k.C(8 * e)));
                    }
                    k.St(xq, k.IAdd(k.IMul(k.Get(b, k.U32), k.C(8)), k.C(wv)), word);
                }
                k.St(xd, k.Get(b, k.U32), k.F16(dq));
            });
            return k.Finish();
        }

        // Final logit softcap: x = tanh(x / cap) * cap. bindings: 0 x. push: n, cap(bits)
        public static uint[] Softcap()
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf x = k.Buffer(0, k.F32, false);
            k.Push(2);
            k.Begin();
            uint i = k.GlobalId();
            uint n = k.PushU(0), cap = k.PushF(1);
            k.If(k.ULt(i, n), delegate
            {
                k.St(x, i, k.FMul(k.S.Glsl(k.F32, Spv.GlslTanh, k.FDiv(k.Ld(x, i), cap)), cap));
            });
            return k.Finish();
        }

        // Q4_0 weights repacked as nibble words (4 per block: byte j holds element j low and j+16 high) + fp16 scales
        // packed two per word (the file's own precision; 18 bytes per block like GGUF, so no extra memory traffic),
        // times Q8_0 activations: sum_b d_w d_x sum_j (q_j - 8) x_j. One workgroup per row; the row sum uses subgroup
        // arithmetic when available. With integer dot products the nibbles become signed bytes (q - 8) in-register:
        // ((n | 0x80..) - 0x08..) ^ 0x80..
        // bindings: 0 wq u32, 1 wd u32 (fp16 pairs), 2 xq u32, 3 xd f32, 4 y f32. push: rows, nb, groupsX, addTo, stride
        public static uint[] MatVecQ4_0(bool dot) { return MatVecQ4_0(dot, false); }

        public static uint[] MatVecQ4_0(bool dot, bool subgroups)
        {
            KB k = new KB(Kernels.Wg);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.U32, true), xq = k.Buffer(2, k.U32, true),
                xd = k.Buffer(3, k.F32, true), y = k.Buffer(4, k.F32, false);
            k.Push(5);
            KB.Shared sh = k.SharedArray(k.F32, Kernels.Wg);
            k.Begin();
            uint bv = k.Local(k.U32), sum = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), gx = k.PushU(2), addTo = k.PushU(3), stride = k.PushU(4);
            uint r = k.IAdd(k.GroupX(), k.IMul(k.GroupY(), gx));
            uint lid = k.LocalId();
            k.Set(sum, k.CF(0));
            k.If(k.ULt(r, rows), delegate
            {
                uint rowBase = k.IMul(r, stride);   // stored rows may be padded (GemmaGpu.RowPad)
                k.For(bv, lid, nb, k.C(Kernels.Wg), delegate
                {
                    uint b = k.Get(bv, k.U32);
                    uint blk = k.IAdd(rowBase, b);
                    uint wBase = k.IMul(blk, k.C(4)), xBase = k.IMul(b, k.C(8));
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
                    uint dw = KernelsAttention.HalfAt(k, wd, blk);
                    uint scale = k.FMul(dw, k.Ld(xd, b));
                    k.Set(sum, k.FAdd(k.Get(sum, k.F32), k.FMul(k.SToF(isum), scale)));
                });
            });
            uint total = k.SumAll(k.Get(sum, k.F32), sh, lid, Kernels.Wg, subgroups);
            k.If(k.ULt(r, rows), delegate
            {
                k.If(k.IEq(lid, k.C(0)), delegate { k.StoreRow(y, r, total, addTo); });
            });
            return k.Finish();
        }

    }
}
