// Parallel activation quantizers with optional fused input transforms. Results match CpuRef.QuantizeQ8_K / QuantizeQ8_0
// applied to the same input. C# 5 only.
//   Mode.Plain:   quantize x
//   Mode.SiluMul: quantize silu(x) * u        (feed-forward gate · up, for the down projection)
//   Mode.RmsNorm: quantize x / rms(x) * w     (layer norm feeding the next matvecs); every workgroup computes rms(x)
using System;

namespace Phoenix
{
    public static class KernelsQuant
    {
        public enum Mode { Plain = 0, SiluMul = 1, RmsNorm = 2 }

        public static int Bindings(Mode m) { return m == Mode.Plain ? 3 : 4; }
        public const int PushWords = 3; // nBlocks, n, eps(bits)

        // Loads element idx of the (transformed) input.
        static uint Input(KB k, Mode mode, KB.Buf x, KB.Buf aux, uint idx, uint scale)
        {
            uint v = k.Ld(x, idx);
            if (mode == Mode.SiluMul)
            {
                uint silu = k.FDiv(v, k.FAdd(k.CF(1f), k.Exp(k.S.Value(Op.FNegate, k.F32, v))));
                return k.FMul(silu, k.Ld(aux, idx));
            }
            if (mode == Mode.RmsNorm) return k.FMul(k.FMul(v, scale), k.Ld(aux, idx));
            return v;
        }

        // 1 / sqrt(mean(x^2) + eps) over the whole vector, computed by this workgroup (64 threads).
        static uint RmsScale(KB k, KB.Buf x, uint n, uint eps, uint lid, KB.Shared sh, bool subgroups, uint iLocal, uint accLocal)
        {
            k.Set(accLocal, k.CF(0));
            k.For(iLocal, lid, n, k.C(64), delegate
            {
                uint v = k.Ld(x, k.Get(iLocal, k.U32));
                k.Set(accLocal, k.FAdd(k.Get(accLocal, k.F32), k.FMul(v, v)));
            });
            uint sum = k.SumAll(k.Get(accLocal, k.F32), sh, lid, 64, subgroups);
            k.Barrier(); // sh is reused below
            uint mean = k.FDiv(sum, k.ToF(n));
            return k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(mean, eps)));
        }

        // Q8_K: one workgroup (64 threads) per 256-element block; thread t owns elements 4t..4t+3 and writes kq word t.
        // The max-|x| search keeps the first index on ties, like the CPU loop.
        // bindings: 0 x, 1 kq, 2 kd, [3 u or w]. push: nBlocks, n, eps(bits)
        public static uint[] Q8K(Mode mode, bool subgroups)
        {
            KB k = new KB(64);
            KB.Buf x = k.Buffer(0, k.F32, true), kq = k.Buffer(1, k.U32, false), kd = k.Buffer(2, k.F32, false);
            KB.Buf aux = mode == Mode.Plain ? null : k.Buffer(3, k.F32, true);
            k.Push(PushWords);
            KB.Shared sAbs = k.SharedArray(k.F32, 64), sVal = k.SharedArray(k.F32, 64), sIdx = k.SharedArray(k.U32, 64);
            k.Begin();
            uint best = k.Local(k.F32), bestVal = k.Local(k.F32), bestIdx = k.Local(k.U32), it = k.Local(k.U32), acc = k.Local(k.F32);
            uint n = k.PushU(1), eps = k.PushF(2);
            uint b = k.GroupX(), lid = k.LocalId();
            uint scale = mode == Mode.RmsNorm ? RmsScale(k, x, n, eps, lid, sAbs, subgroups, it, acc) : 0;
            uint o = k.IAdd(k.IMul(b, k.C(256)), k.IMul(lid, k.C(4)));
            uint[] v = new uint[4];
            for (int i = 0; i < 4; i++) v[i] = Input(k, mode, x, aux, k.IAdd(o, k.C((uint)i)), scale);

            k.Set(best, k.Abs(v[0])); k.Set(bestVal, v[0]); k.Set(bestIdx, k.IMul(lid, k.C(4)));
            for (int i = 1; i < 4; i++)
            {
                uint a = k.Abs(v[i]);
                uint gt = k.S.Value(Op.FOrdGreaterThan, k.Bool, a, k.Get(best, k.F32));
                k.Set(bestVal, k.Select(k.F32, gt, v[i], k.Get(bestVal, k.F32)));
                k.Set(bestIdx, k.Select(k.U32, gt, k.IAdd(k.IMul(lid, k.C(4)), k.C((uint)i)), k.Get(bestIdx, k.U32)));
                k.Set(best, k.Select(k.F32, gt, a, k.Get(best, k.F32)));
            }
            k.St(sAbs, lid, k.Get(best, k.F32)); k.St(sVal, lid, k.Get(bestVal, k.F32)); k.St(sIdx, lid, k.Get(bestIdx, k.U32));
            k.Barrier();
            for (uint s = 32; s > 0; s /= 2)
            {
                uint half = s;
                k.If(k.ULt(lid, k.C(half)), delegate
                {
                    uint other = k.IAdd(lid, k.C(half));
                    uint a0 = k.Ld(sAbs, lid), a1 = k.Ld(sAbs, other);
                    uint i0 = k.Ld(sIdx, lid), i1 = k.Ld(sIdx, other);
                    uint gt = k.S.Value(Op.FOrdGreaterThan, k.Bool, a1, a0);
                    uint eq = k.S.Value(Op.FOrdEqual, k.Bool, a1, a0);
                    uint take = k.S.Value(Op.LogicalOr, k.Bool, gt, k.S.Value(Op.LogicalAnd, k.Bool, eq, k.ULt(i1, i0)));
                    k.St(sAbs, lid, k.Select(k.F32, take, a1, a0));
                    k.St(sVal, lid, k.Select(k.F32, take, k.Ld(sVal, other), k.Ld(sVal, lid)));
                    k.St(sIdx, lid, k.Select(k.U32, take, i1, i0));
                });
                k.Barrier();
            }
            uint amax = k.Ld(sAbs, k.C(0)), max = k.Ld(sVal, k.C(0));
            uint nonzero = k.FNe(amax, k.CF(0));
            uint iscale = k.Select(k.F32, nonzero, k.FDiv(k.CF(-127f), max), k.CF(0));
            uint word = k.C(0);
            for (int i = 0; i < 4; i++)
            {
                uint q = k.FToS(k.RoundEven(k.FMul(iscale, v[i])));
                uint qb = k.And(k.AsU(k.SMin(q, k.CI(127))), k.C(0xFF));
                word = k.Or(word, i == 0 ? qb : k.Shl(qb, k.C((uint)(8 * i))));
            }
            k.St(kq, k.IAdd(k.IMul(b, k.C(64)), lid), word);
            k.If(k.IEq(lid, k.C(0)), delegate { k.St(kd, b, k.Select(k.F32, nonzero, k.FDiv(k.CF(1f), iscale), k.CF(0))); });
            return k.Finish();
        }

        public static uint[] Q8K() { return Q8K(Mode.Plain, false); }

        // Q8_0: 8 threads per 32-element block (thread owns one output word), 8 blocks per 64-thread workgroup.
        // bindings: 0 x, 1 xq, 2 xd, [3 u or w]. push: nBlocks, n, eps(bits)
        public static uint[] Q80(Mode mode, bool subgroups)
        {
            KB k = new KB(64);
            KB.Buf x = k.Buffer(0, k.F32, true), xq = k.Buffer(1, k.U32, false), xd = k.Buffer(2, k.F32, false);
            KB.Buf aux = mode == Mode.Plain ? null : k.Buffer(3, k.F32, true);
            k.Push(PushWords);
            KB.Shared sh = k.SharedArray(k.F32, 64);
            k.Begin();
            uint it = k.Local(k.U32), acc = k.Local(k.F32);
            uint nb = k.PushU(0), n = k.PushU(1), eps = k.PushF(2);
            uint lid = k.LocalId();
            uint scale = mode == Mode.RmsNorm ? RmsScale(k, x, n, eps, lid, sh, subgroups, it, acc) : 0;
            uint b = k.IAdd(k.IMul(k.GroupX(), k.C(8)), k.Shr(lid, k.C(3)));
            uint lane = k.And(lid, k.C(7));
            uint valid = k.ULt(b, nb);
            uint bSafe = k.Select(k.U32, valid, b, k.C(0));
            uint o = k.IAdd(k.IMul(bSafe, k.C(32)), k.IMul(lane, k.C(4)));
            uint[] v = new uint[4];
            uint amax = k.CF(0);
            for (int i = 0; i < 4; i++)
            {
                v[i] = Input(k, mode, x, aux, k.IAdd(o, k.C((uint)i)), scale);
                amax = k.FMax(amax, k.Abs(v[i]));
            }
            k.St(sh, lid, amax);
            k.Barrier();
            for (uint s = 4; s > 0; s /= 2)
            {
                uint half = s;
                k.If(k.ULt(lane, k.C(half)), delegate { k.St(sh, lid, k.FMax(k.Ld(sh, lid), k.Ld(sh, k.IAdd(lid, k.C(half))))); });
                k.Barrier();
            }
            uint am = k.Ld(sh, k.IMul(k.Shr(lid, k.C(3)), k.C(8)));
            uint d = k.FDiv(am, k.CF(127f));
            uint id = k.Select(k.F32, k.FNe(d, k.CF(0)), k.FDiv(k.CF(1f), d), k.CF(0));
            uint word = k.C(0);
            for (int i = 0; i < 4; i++)
            {
                uint t = k.FMul(v[i], id);
                uint r = k.FMul(k.Sign(t), k.Floor(k.FAdd(k.Abs(t), k.CF(0.5f))));
                uint qb = k.And(k.AsU(k.FToS(r)), k.C(0xFF));
                word = k.Or(word, i == 0 ? qb : k.Shl(qb, k.C((uint)(8 * i))));
            }
            k.If(valid, delegate
            {
                k.St(xq, k.IAdd(k.IMul(b, k.C(8)), lane), word);
                k.If(k.IEq(lane, k.C(0)), delegate { k.St(xd, b, k.F16(d)); });
            });
            return k.Finish();
        }

        public static uint[] Q80() { return Q80(Mode.Plain, false); }
    }
}
