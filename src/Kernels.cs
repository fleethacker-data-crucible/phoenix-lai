// Compute kernels as SPIR-V, built with a small structured-code helper over SpirvBuilder. C# 5 only.
// Each kernel mirrors the matching CpuRef / LlamaCpu step so results can be compared directly.
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public delegate void KBody();

    // Kernel builder: typed helpers for buffers, push constants, shared memory, loops and branches.
    public sealed class KB
    {
        public sealed class Buf { public uint Var, Elem, Ptr; }
        public sealed class Shared { public uint Var, Elem, Ptr; }

        public readonly SpirvBuilder S = new SpirvBuilder();
        public readonly uint Void, U32, I32, F32, Bool, V3U;
        readonly List<uint> _iface = new List<uint>();
        readonly uint _localSize;
        uint _fn, _push, _gid, _lid, _wg;

        public KB(uint localSize)
        {
            _localSize = localSize;
            Void = S.TypeVoid(); U32 = S.TypeUInt(32); I32 = S.TypeInt(32); F32 = S.TypeFloat(32); Bool = S.TypeBool();
            V3U = S.TypeVector(U32, 3);
        }

        // ----- declarations (call before Begin) -----

        public Buf Buffer(uint binding, uint elem, bool readOnly)
        {
            Buf b = new Buf();
            b.Var = S.StorageBuffer(binding, elem, 4, readOnly);
            b.Elem = elem;
            b.Ptr = S.TypePointer(Spv.StorageStorageBuffer, elem);
            return b;
        }

        // Shared array / buffer element helpers for uvec4 (16-byte) loads.
        public uint Comp(uint vec, uint index) { return S.Value(Op.CompositeExtract, U32, vec, index); }

        public void Push(int count)
        {
            uint[] m = new uint[count];
            for (int i = 0; i < count; i++) m[i] = U32;
            _push = S.PushConstants(m);
        }

        public Shared SharedArray(uint elem, uint length)
        {
            Shared s = new Shared();
            uint arr = S.TypeArray(elem, C(length));
            s.Var = S.GlobalVariable(S.TypePointer(Spv.StorageWorkgroup, arr), Spv.StorageWorkgroup);
            s.Elem = elem;
            s.Ptr = S.TypePointer(Spv.StorageWorkgroup, elem);
            return s;
        }

        // ----- function -----

        public void Begin()
        {
            _fn = S.BeginFunction(Void);
            S.Label(S.NewId());
        }

        // Function-scope variable. Must be called right after Begin(), before any other instruction.
        public uint Local(uint type)
        {
            return S.Value(Op.Variable, S.TypePointer(Spv.StorageFunction, type), Spv.StorageFunction);
        }

        public uint[] Finish()
        {
            S.Instr(Op.Return);
            S.EndFunction();
            S.Name(_fn, "main");
            S.EntryPoint(_fn, "main", _iface.ToArray(), _localSize, 1, 1);
            return S.ToWords();
        }

        // ----- builtins -----

        uint BuiltIn(ref uint var, uint which, uint component)
        {
            if (var == 0) { var = S.BuiltInInput(which, V3U); _iface.Add(var); }
            return S.Value(Op.CompositeExtract, U32, S.Load(V3U, var), component);
        }

        public uint GlobalId() { return BuiltIn(ref _gid, Spv.BuiltInGlobalInvocationId, 0); }
        public uint LocalId() { return BuiltIn(ref _lid, Spv.BuiltInLocalInvocationId, 0); }
        public uint GroupX() { return BuiltIn(ref _wg, Spv.BuiltInWorkgroupId, 0); }
        public uint GroupY() { return BuiltIn(ref _wg, Spv.BuiltInWorkgroupId, 1); }

        // ----- constants, push constants, memory -----

        public uint C(uint v) { return S.ConstUInt(v); }
        public uint CI(int v) { return S.ConstInt(v); }
        public uint CF(float v) { return S.ConstFloat(v); }

        public uint PushU(uint index) { return S.Load(U32, S.AccessChain(S.TypePointer(Spv.StoragePushConstant, U32), _push, C(index))); }
        public uint PushF(uint index) { return S.Value(Op.Bitcast, F32, PushU(index)); }

        public uint Ld(Buf b, uint index) { return S.Load(b.Elem, S.AccessChain(b.Ptr, b.Var, C(0), index)); }
        public void St(Buf b, uint index, uint value) { S.Store(S.AccessChain(b.Ptr, b.Var, C(0), index), value); }
        public uint Ld(Shared s, uint index) { return S.Load(s.Elem, S.AccessChain(s.Ptr, s.Var, index)); }
        public void St(Shared s, uint index, uint value) { S.Store(S.AccessChain(s.Ptr, s.Var, index), value); }
        public uint Get(uint local, uint type) { return S.Load(type, local); }
        public void Set(uint local, uint value) { S.Store(local, value); }

        // Workgroup barrier that also orders workgroup and storage-buffer memory.
        public void Barrier()
        {
            S.Instr(Op.ControlBarrier, C(Spv.ScopeWorkgroup), C(Spv.ScopeWorkgroup),
                C(Spv.SemanticsAcquireRelease | Spv.SemanticsUniformMemory | Spv.SemanticsWorkgroupMemory));
        }

        // ----- arithmetic -----

        public uint IAdd(uint a, uint b) { return S.Value(Op.IAdd, U32, a, b); }
        public uint IMul(uint a, uint b) { return S.Value(Op.IMul, U32, a, b); }
        public uint UDiv(uint a, uint b) { return S.Value(Op.UDiv, U32, a, b); }
        public uint UMod(uint a, uint b) { return S.Value(Op.UMod, U32, a, b); }
        public uint ULt(uint a, uint b) { return S.Value(Op.ULessThan, Bool, a, b); }
        public uint IEq(uint a, uint b) { return S.Value(Op.IEqual, Bool, a, b); }
        public uint And(uint a, uint b) { return S.Value(Op.BitwiseAnd, U32, a, b); }
        public uint Or(uint a, uint b) { return S.Value(Op.BitwiseOr, U32, a, b); }
        public uint Shl(uint a, uint n) { return S.Value(Op.ShiftLeftLogical, U32, a, n); }
        public uint Shr(uint a, uint n) { return S.Value(Op.ShiftRightLogical, U32, a, n); }
        public uint ISubS(uint a, uint b) { return S.Value(Op.ISub, I32, a, b); }
        public uint AsI(uint u) { return S.Value(Op.Bitcast, I32, u); }
        public uint SMin(uint a, uint b) { return S.Glsl(I32, Spv.GlslSMin, a, b); }
        public uint RoundEven(uint f) { return S.Glsl(F32, Spv.GlslRoundEven, f); }

        // Byte at byteIndex of a u32 buffer (little-endian), as u32.
        public uint ByteAt(Buf b, uint byteIndex)
        {
            uint word = Ld(b, Shr(byteIndex, C(2)));
            return And(Shr(word, IMul(And(byteIndex, C(3)), C(8))), C(0xFF));
        }

        // fp16 in the low (high=false) or high half of a word, as f32.
        public uint HalfOf(uint word, bool high)
        {
            uint v2 = S.Glsl(S.TypeVector(F32, 2), Spv.GlslUnpackHalf2x16, word);
            return S.Value(Op.CompositeExtract, F32, v2, high ? 1u : 0u);
        }
        public uint IAddS(uint a, uint b) { return S.Value(Op.IAdd, I32, a, b); }
        public uint IMulS(uint a, uint b) { return S.Value(Op.IMul, I32, a, b); }

        // Packed 4 x 8-bit dot product (SPV_KHR_integer_dot_product), result i32.
        //   signedB = true: both operands signed bytes (OpSDot); false: a signed, b unsigned bytes (OpSUDot).
        public uint Dot4(uint a, uint b, bool signedB)
        {
            S.Capability(Spv.CapabilityDotProduct);
            S.Capability(Spv.CapabilityDotProductInput4x8BitPacked);
            S.Extension("SPV_KHR_integer_dot_product");
            return S.Value(signedB ? Op.SDot : Op.SUDot, I32, a, b, Spv.PackedVectorFormat4x8Bit);
        }

        // acc + dot(a, b) for packed signed 4 x 8-bit vectors (OpSDotAccSat; saturation never triggers for these sizes):
        // one instruction instead of a dot and an add.
        public uint Dot4Acc(uint a, uint b, uint acc)
        {
            S.Capability(Spv.CapabilityDotProduct);
            S.Capability(Spv.CapabilityDotProductInput4x8BitPacked);
            S.Extension("SPV_KHR_integer_dot_product");
            return S.Value(Op.SDotAccSat, I32, a, b, acc, Spv.PackedVectorFormat4x8Bit);
        }

        // Signed byte k (0..3) of a packed word, sign-extended.
        public uint SByte(uint word, int k)
        {
            uint shifted = k == 3 ? word : Shl(word, C((uint)(24 - 8 * k)));
            uint asInt = S.Value(Op.Bitcast, I32, shifted);
            return S.Value(Op.ShiftRightArithmetic, I32, asInt, C(24));
        }

        public uint FAdd(uint a, uint b) { return S.Value(Op.FAdd, F32, a, b); }
        public uint FSub(uint a, uint b) { return S.Value(Op.FSub, F32, a, b); }
        public uint FMul(uint a, uint b) { return S.Value(Op.FMul, F32, a, b); }
        public uint FDiv(uint a, uint b) { return S.Value(Op.FDiv, F32, a, b); }
        public uint FNe(uint a, uint b) { return S.Value(Op.FOrdNotEqual, Bool, a, b); }
        public uint ToF(uint u) { return S.Value(Op.ConvertUToF, F32, u); }
        public uint SToF(uint i) { return S.Value(Op.ConvertSToF, F32, i); }
        public uint FToS(uint f) { return S.Value(Op.ConvertFToS, I32, f); }
        public uint AsU(uint i) { return S.Value(Op.Bitcast, U32, i); }
        public uint F16(uint f) { return S.Value(Op.QuantizeToF16, F32, f); }
        // Stores value as fp16 into half-element hi of a u32 buffer (two halves per word, read-modify-write).
        public void StHalf(Buf b, uint hi, uint value)
        {
            uint wi = Shr(hi, C(1)), old = Ld(b, wi);
            uint h = And(S.Glsl(U32, Spv.GlslPackHalf2x16, S.Value(Op.CompositeConstruct, S.TypeVector(F32, 2), value, CF(0))), C(0xFFFF));
            uint odd = IEq(And(hi, C(1)), C(1));
            St(b, wi, Select(U32, odd, Or(And(old, C(0xFFFF)), Shl(h, C(16))), Or(And(old, C(0xFFFF0000)), h)));
        }
        public uint Select(uint type, uint cond, uint a, uint b) { return S.Value(Op.Select, type, cond, a, b); }
        public uint Abs(uint f) { return S.Glsl(F32, Spv.GlslFAbs, f); }
        public uint Sign(uint f) { return S.Glsl(F32, Spv.GlslFSign, f); }
        public uint Floor(uint f) { return S.Glsl(F32, Spv.GlslFloor, f); }
        public uint Exp(uint f) { return S.Glsl(F32, Spv.GlslExp, f); }
        public uint Sqrt(uint f) { return S.Glsl(F32, Spv.GlslSqrt, f); }
        public uint FMax(uint a, uint b) { return S.Glsl(F32, Spv.GlslFMax, a, b); }

        // ----- structured control flow -----

        public void If(uint cond, KBody then)
        {
            uint body = S.NewId(), merge = S.NewId();
            S.Instr(Op.SelectionMerge, merge, Spv.SelectionControlNone);
            S.Instr(Op.BranchConditional, cond, body, merge);
            S.Label(body);
            then();
            S.Instr(Op.Branch, merge);
            S.Label(merge);
        }

        // for (i = start; i < end; i += step) body   — i is a Local(U32); end and step are loop-invariant ids.
        public void For(uint i, uint start, uint end, uint step, KBody body)
        {
            uint header = S.NewId(), bodyL = S.NewId(), cont = S.NewId(), merge = S.NewId();
            Set(i, start);
            S.Instr(Op.Branch, header);
            S.Label(header);
            uint cond = ULt(Get(i, U32), end);
            S.Instr(Op.LoopMerge, merge, cont, Spv.LoopControlNone);
            S.Instr(Op.BranchConditional, cond, bodyL, merge);
            S.Label(bodyL);
            body();
            S.Instr(Op.Branch, cont);
            S.Label(cont);
            Set(i, IAdd(Get(i, U32), step));
            S.Instr(Op.Branch, header);
            S.Label(merge);
        }

        // Scalar u32 builtins (SubgroupSize, SubgroupId, ...).
        readonly Dictionary<uint, uint> _scalarBuiltins = new Dictionary<uint, uint>();
        public uint ScalarBuiltIn(uint which)
        {
            uint var;
            if (!_scalarBuiltins.TryGetValue(which, out var))
            {
                var = S.BuiltInInput(which, U32);
                _iface.Add(var);
                _scalarBuiltins[which] = var;
            }
            return S.Load(U32, var);
        }

        // Workgroup-wide float sum, returned in every thread. With subgroup arithmetic: one subgroupAdd per subgroup,
        // the partials in sh[0..numSubgroups), one barrier. Otherwise a shared-memory tree over sh[0..size).
        public uint SumAll(uint value, Shared sh, uint lid, uint size, bool subgroups)
        {
            if (!subgroups)
            {
                St(sh, lid, value);
                Reduce(sh, lid, size, FAdd);
                return Ld(sh, C(0));
            }
            S.Capability(Spv.CapabilityGroupNonUniform);
            S.Capability(Spv.CapabilityGroupNonUniformArithmetic);
            uint part = S.Value(Op.GroupNonUniformFAdd, F32, C(Spv.ScopeSubgroup), Spv.GroupOperationReduce, value);
            uint sgId = ScalarBuiltIn(Spv.BuiltInSubgroupId), sgLane = ScalarBuiltIn(Spv.BuiltInSubgroupLocalInvocationId);
            uint nSg = ScalarBuiltIn(Spv.BuiltInNumSubgroups);
            If(IEq(sgLane, C(0)), delegate { St(sh, sgId, part); });
            Barrier();
            // Every thread adds the per-subgroup partials (at most size/8 of them) in a fixed order.
            uint total = CF(0);
            for (uint i = 0; i < size / 8; i++)
                total = FAdd(total, Select(F32, ULt(C(i), nSg), Ld(sh, C(i)), CF(0)));
            return total;
        }

        // Matvec output: y[r] = value, or y[r] += value when addTo == 1 (fused residual add).
        public void StoreRow(Buf y, uint r, uint value, uint addTo)
        {
            St(y, r, Select(F32, IEq(addTo, C(1)), FAdd(Ld(y, r), value), value));
        }

        // Tree reduction of sh[0..size) into sh[0]; op combines two floats. Ends with a barrier.
        public void Reduce(Shared sh, uint lid, uint size, Func<uint, uint, uint> op)
        {
            Barrier();
            for (uint s = size / 2; s > 0; s /= 2)
            {
                uint half = s;
                If(ULt(lid, C(half)), delegate { St(sh, lid, op(Ld(sh, lid), Ld(sh, IAdd(lid, C(half))))); });
                Barrier();
            }
        }
    }

    public static class Kernels
    {
        public const uint Wg = 64;       // default workgroup size
        public const uint NormWg = 256;

        // y[r] = sum_b isum(b) * (wd * xd)  — Q8_0 weights repacked as (qs words, f32 scales). One workgroup per row.
        // bindings: 0 wq u32, 1 wd f32, 2 xq u32, 3 xd f32, 4 y f32. push: rows, nb, groupsX
        public static uint[] MatVecQ8()
        {
            return MatVecQ8(false);
        }

        // dot: packed 4x8-bit integer dot products (OpSDot) for the inner products.
        public static uint[] MatVecQ8(bool dot)
        {
            KB k = new KB(Wg);
            KB.Buf wq = k.Buffer(0, k.U32, true), wd = k.Buffer(1, k.F32, true), xq = k.Buffer(2, k.U32, true),
                xd = k.Buffer(3, k.F32, true), y = k.Buffer(4, k.F32, false);
            k.Push(4);
            KB.Shared sh = k.SharedArray(k.F32, Wg);
            k.Begin();
            uint bv = k.Local(k.U32), sum = k.Local(k.F32);
            uint rows = k.PushU(0), nb = k.PushU(1), gx = k.PushU(2), addTo = k.PushU(3);
            uint r = k.IAdd(k.GroupX(), k.IMul(k.GroupY(), gx));
            uint lid = k.LocalId();
            k.Set(sum, k.CF(0));
            k.If(k.ULt(r, rows), delegate
            {
                uint rowBase = k.IMul(r, nb);
                k.For(bv, lid, nb, k.C(Wg), delegate
                {
                    uint b = k.Get(bv, k.U32);
                    uint blk = k.IAdd(rowBase, b);
                    uint wBase = k.IMul(blk, k.C(8)), xBase = k.IMul(b, k.C(8));
                    uint isum = k.CI(0);
                    for (uint w = 0; w < 8; w++)
                    {
                        uint ww = k.Ld(wq, k.IAdd(wBase, k.C(w)));
                        uint xw = k.Ld(xq, k.IAdd(xBase, k.C(w)));
                        if (dot) isum = k.IAddS(isum, k.Dot4(ww, xw, true));
                        else for (int kk = 0; kk < 4; kk++) isum = k.IAddS(isum, k.IMulS(k.SByte(ww, kk), k.SByte(xw, kk)));
                    }
                    uint scale = k.FMul(k.Ld(wd, blk), k.Ld(xd, b));
                    k.Set(sum, k.FAdd(k.Get(sum, k.F32), k.FMul(k.SToF(isum), scale)));
                });
            });
            k.St(sh, lid, k.Get(sum, k.F32));
            k.Reduce(sh, lid, Wg, k.FAdd);
            k.If(k.ULt(r, rows), delegate
            {
                k.If(k.IEq(lid, k.C(0)), delegate { k.StoreRow(y, r, k.Ld(sh, k.C(0)), addTo); });
            });
            return k.Finish();
        }

        // y = x / sqrt(mean(x^2) + eps) * w. Single workgroup. bindings: 0 x, 1 w, 2 y. push: n, eps(bits)
        public static uint[] RmsNorm()
        {
            KB k = new KB(NormWg);
            KB.Buf x = k.Buffer(0, k.F32, true), w = k.Buffer(1, k.F32, true), y = k.Buffer(2, k.F32, false);
            k.Push(2);
            KB.Shared sh = k.SharedArray(k.F32, NormWg);
            k.Begin();
            uint i = k.Local(k.U32), acc = k.Local(k.F32);
            uint n = k.PushU(0), eps = k.PushF(1);
            uint lid = k.LocalId();
            k.Set(acc, k.CF(0));
            k.For(i, lid, n, k.C(NormWg), delegate
            {
                uint v = k.Ld(x, k.Get(i, k.U32));
                k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(v, v)));
            });
            k.St(sh, lid, k.Get(acc, k.F32));
            k.Reduce(sh, lid, NormWg, k.FAdd);
            uint mean = k.FDiv(k.Ld(sh, k.C(0)), k.ToF(n));
            uint scale = k.FDiv(k.CF(1f), k.Sqrt(k.FAdd(mean, eps)));
            k.For(i, lid, n, k.C(NormWg), delegate
            {
                uint idx = k.Get(i, k.U32);
                k.St(y, idx, k.FMul(k.FMul(k.Ld(x, idx), scale), k.Ld(w, idx)));
            });
            return k.Finish();
        }

        // Normal (adjacent-pair) RoPE with a host-computed cos/sin table, then x *= post (attention temperature; 1 for K).
        // bindings: 0 x, 1 cs (cos,sin per pair). push: nHeads, headDim, halfRot, post(bits)
        public static uint[] Rope()
        {
            KB k = new KB(Wg);
            KB.Buf x = k.Buffer(0, k.F32, false), cs = k.Buffer(1, k.F32, true);
            k.Push(4);
            k.Begin();
            uint p = k.GlobalId();
            uint nHeads = k.PushU(0), headDim = k.PushU(1), half = k.PushU(2), post = k.PushF(3);
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

        // KV cache write with fp16 rounding. bindings: 0 k, 1 v, 2 kc, 3 vc. push: kvDim, pos
        public static uint[] KvStore()
        {
            KB k = new KB(Wg);
            KB.Buf kk = k.Buffer(0, k.F32, true), v = k.Buffer(1, k.F32, true), kc = k.Buffer(2, k.F32, false), vc = k.Buffer(3, k.F32, false);
            k.Push(2);
            k.Begin();
            uint i = k.GlobalId();
            uint kvDim = k.PushU(0), pos = k.PushU(1);
            k.If(k.ULt(i, kvDim), delegate
            {
                uint off = k.IAdd(k.IMul(pos, kvDim), i);
                k.St(kc, off, k.F16(k.Ld(kk, i)));
                k.St(vc, off, k.F16(k.Ld(v, i)));
            });
            return k.Finish();
        }

        // One workgroup per query head: scores, softmax, weighted sum of V (LlamaCpu attention, ggml f16 rounding).
        // bindings: 0 q, 1 kc, 2 vc, 3 scores scratch [nHead*maxCtx], 4 out. push: n, headDim, kvDim, group, maxCtx, scale(bits)
        public static uint[] Attention()
        {
            KB k = new KB(Wg);
            KB.Buf q = k.Buffer(0, k.F32, true), kc = k.Buffer(1, k.F32, true), vc = k.Buffer(2, k.F32, true),
                sc = k.Buffer(3, k.F32, false), outB = k.Buffer(4, k.F32, false);
            k.Push(6);
            KB.Shared sh = k.SharedArray(k.F32, Wg);
            k.Begin();
            uint j = k.Local(k.U32), d = k.Local(k.U32), acc = k.Local(k.F32), mx = k.Local(k.F32), sum = k.Local(k.F32);
            uint n = k.PushU(0), headDim = k.PushU(1), kvDim = k.PushU(2), group = k.PushU(3), maxCtx = k.PushU(4), scale = k.PushF(5);
            uint h = k.GroupX(), lid = k.LocalId();
            uint kvOff = k.IMul(k.UDiv(h, group), headDim), qOff = k.IMul(h, headDim), sBase = k.IMul(h, maxCtx);

            // 1. scores and running max
            k.Set(mx, k.CF(float.NegativeInfinity));
            k.For(j, lid, n, k.C(Wg), delegate
            {
                uint t = k.Get(j, k.U32);
                uint ko = k.IAdd(k.IMul(t, kvDim), kvOff);
                k.Set(acc, k.CF(0));
                k.For(d, k.C(0), headDim, k.C(1), delegate
                {
                    uint dd = k.Get(d, k.U32);
                    uint qv = k.F16(k.Ld(q, k.IAdd(qOff, dd)));
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(qv, k.Ld(kc, k.IAdd(ko, dd)))));
                });
                uint s = k.FMul(k.Get(acc, k.F32), scale);
                k.St(sc, k.IAdd(sBase, t), s);
                k.Set(mx, k.FMax(k.Get(mx, k.F32), s));
            });
            k.St(sh, lid, k.Get(mx, k.F32));
            k.Reduce(sh, lid, Wg, k.FMax);
            uint m = k.Ld(sh, k.C(0));
            k.Barrier();

            // 2. exponentials and their sum
            k.Set(sum, k.CF(0));
            k.For(j, lid, n, k.C(Wg), delegate
            {
                uint idx = k.IAdd(sBase, k.Get(j, k.U32));
                uint e = k.Exp(k.FSub(k.Ld(sc, idx), m));
                k.St(sc, idx, e);
                k.Set(sum, k.FAdd(k.Get(sum, k.F32), e));
            });
            k.St(sh, lid, k.Get(sum, k.F32));
            k.Reduce(sh, lid, Wg, k.FAdd);
            uint inv = k.FDiv(k.CF(1f), k.Ld(sh, k.C(0)));

            // 3. out[d] = sum_t f16(p_t) * v[t][d]
            k.For(d, lid, headDim, k.C(Wg), delegate
            {
                uint dd = k.Get(d, k.U32);
                k.Set(acc, k.CF(0));
                k.For(j, k.C(0), n, k.C(1), delegate
                {
                    uint t = k.Get(j, k.U32);
                    uint p = k.F16(k.FMul(k.Ld(sc, k.IAdd(sBase, t)), inv));
                    k.Set(acc, k.FAdd(k.Get(acc, k.F32), k.FMul(p, k.Ld(vc, k.IAdd(k.IAdd(k.IMul(t, kvDim), kvOff), dd)))));
                });
                k.St(outB, k.IAdd(qOff, dd), k.Get(acc, k.F32));
            });
            return k.Finish();
        }

        // x[i] += y[i]. bindings: 0 x, 1 y. push: n
        public static uint[] Add()
        {
            KB k = new KB(Wg);
            KB.Buf x = k.Buffer(0, k.F32, false), y = k.Buffer(1, k.F32, true);
            k.Push(1);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate { k.St(x, i, k.FAdd(k.Ld(x, i), k.Ld(y, i))); });
            return k.Finish();
        }

        // g[i] = silu(g[i]) * u[i]. bindings: 0 g, 1 u. push: n
        public static uint[] SiluMul()
        {
            KB k = new KB(Wg);
            KB.Buf g = k.Buffer(0, k.F32, false), u = k.Buffer(1, k.F32, true);
            k.Push(1);
            k.Begin();
            uint i = k.GlobalId();
            k.If(k.ULt(i, k.PushU(0)), delegate
            {
                uint v = k.Ld(g, i);
                uint silu = k.FDiv(v, k.FAdd(k.CF(1f), k.Exp(k.S.Value(Op.FNegate, k.F32, v))));
                k.St(g, i, k.FMul(silu, k.Ld(u, i)));
            });
            return k.Finish();
        }
    }
}
