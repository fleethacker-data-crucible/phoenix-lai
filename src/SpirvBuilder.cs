// SPIR-V binary emitter for compute shaders. C# 5 only.
// Opcodes and enums from dev/ref/spirv/spirv.h (unified1). Produces a module the driver compiles itself.
using System;
using System.Collections.Generic;
using System.Text;

namespace Phoenix
{
    public static class Op
    {
        public const ushort Name = 5, MemberName = 6, ExtInstImport = 11, ExtInst = 12, MemoryModel = 14, EntryPoint = 15,
            ExecutionMode = 16, Capability = 17, TypeVoid = 19, TypeBool = 20, TypeInt = 21, TypeFloat = 22, TypeVector = 23,
            TypeArray = 28, TypeRuntimeArray = 29, TypeStruct = 30, TypePointer = 32, TypeFunction = 33,
            ConstantTrue = 41, ConstantFalse = 42, Constant = 43, Function = 54, FunctionParameter = 55, FunctionEnd = 56,
            FunctionCall = 57, Variable = 59, Load = 61, Store = 62, AccessChain = 65, Decorate = 71, MemberDecorate = 72,
            CompositeConstruct = 80, CompositeExtract = 81, ConvertFToU = 109, ConvertFToS = 110, ConvertSToF = 111,
            ConvertUToF = 112, UConvert = 113, SConvert = 114, FConvert = 115, QuantizeToF16 = 116, Bitcast = 124,
            SNegate = 126, FNegate = 127, IAdd = 128, FAdd = 129, ISub = 130, FSub = 131, IMul = 132, FMul = 133,
            UDiv = 134, SDiv = 135, FDiv = 136, UMod = 137, SRem = 138, Dot = 148,
            LogicalOr = 166, LogicalAnd = 167, LogicalNot = 168, Select = 169, IEqual = 170, INotEqual = 171,
            UGreaterThan = 172, SGreaterThan = 173, UGreaterThanEqual = 174, SGreaterThanEqual = 175,
            ULessThan = 176, SLessThan = 177, ULessThanEqual = 178, SLessThanEqual = 179,
            FOrdEqual = 180, FOrdNotEqual = 182, FOrdLessThan = 184, FOrdGreaterThan = 186, FOrdLessThanEqual = 188, FOrdGreaterThanEqual = 190,
            ShiftRightLogical = 194, ShiftRightArithmetic = 195, ShiftLeftLogical = 196, BitwiseOr = 197, BitwiseXor = 198,
            BitwiseAnd = 199, Not = 200, BitFieldUExtract = 203, ControlBarrier = 224, MemoryBarrier = 225,
            Phi = 245, LoopMerge = 246, SelectionMerge = 247, Label = 248, Branch = 249, BranchConditional = 250, Return = 253;
    }

    public static class Spv
    {
        public const uint CapabilityShader = 1, CapabilityFloat16 = 9, CapabilityInt16 = 22, CapabilityInt8 = 39,
            CapabilityStorageBuffer16BitAccess = 4433, CapabilityStorageBuffer8BitAccess = 4448;
        public const uint AddressingLogical = 0, MemoryModelGLSL450 = 1;
        public const uint ExecutionModelGLCompute = 5, ExecutionModeLocalSize = 17;
        public const uint StorageInput = 1, StorageUniform = 2, StorageWorkgroup = 4, StorageFunction = 7,
            StoragePushConstant = 9, StorageStorageBuffer = 12;
        public const uint DecorationBlock = 2, DecorationArrayStride = 6, DecorationBuiltIn = 11, DecorationNonWritable = 24,
            DecorationNonReadable = 25, DecorationBinding = 33, DecorationDescriptorSet = 34, DecorationOffset = 35;
        public const uint BuiltInWorkgroupId = 26, BuiltInLocalInvocationId = 27, BuiltInGlobalInvocationId = 28,
            BuiltInLocalInvocationIndex = 29;
        public const uint ScopeDevice = 1, ScopeWorkgroup = 2;
        public const uint SemanticsAcquireRelease = 0x8, SemanticsUniformMemory = 0x40, SemanticsWorkgroupMemory = 0x100;
        public const uint FunctionControlNone = 0, SelectionControlNone = 0, LoopControlNone = 0;
        // GLSL.std.450 extended instructions
        public const uint GlslRoundEven = 2, GlslFAbs = 4, GlslFSign = 6, GlslFloor = 8, GlslSin = 13, GlslCos = 14, GlslTanh = 21,
            GlslExp = 27, GlslLog = 28, GlslSMin = 39, GlslUnpackHalf2x16 = 62, GlslSqrt = 31, GlslInverseSqrt = 32, GlslFMin = 37, GlslUMin = 38, GlslFMax = 40,
            GlslUMax = 41, GlslFma = 50;
    }

    // Emits instructions into the module sections in the order SPIR-V requires.
    public sealed class SpirvBuilder
    {
        public const uint Magic = 0x07230203;
        public const uint Version13 = 0x00010300;

        readonly List<uint> _capabilities = new List<uint>(), _extImports = new List<uint>(), _memoryModel = new List<uint>(),
            _entryPoints = new List<uint>(), _execModes = new List<uint>(), _debug = new List<uint>(),
            _annotations = new List<uint>(), _globals = new List<uint>(), _functions = new List<uint>();
        readonly Dictionary<string, uint> _typeCache = new Dictionary<string, uint>();
        readonly HashSet<uint> _caps = new HashSet<uint>();
        uint _nextId = 1;
        uint _glsl;

        public SpirvBuilder()
        {
            Capability(Spv.CapabilityShader);
            Emit(_memoryModel, Op.MemoryModel, Spv.AddressingLogical, Spv.MemoryModelGLSL450);
        }

        public uint NewId() { return _nextId++; }

        // ---------- encoding ----------

        static void Emit(List<uint> section, ushort opcode, params uint[] operands)
        {
            section.Add((uint)(operands.Length + 1) << 16 | opcode);
            section.AddRange(operands);
        }

        public static uint[] StringWords(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            uint[] w = new uint[b.Length / 4 + 1]; // always room for the terminating NUL
            for (int i = 0; i < b.Length; i++) w[i / 4] |= (uint)b[i] << (8 * (i % 4));
            return w;
        }

        static uint[] Concat(uint[] a, params uint[] b)
        {
            uint[] r = new uint[a.Length + b.Length];
            Array.Copy(a, r, a.Length);
            Array.Copy(b, 0, r, a.Length, b.Length);
            return r;
        }

        static uint[] Prepend(uint head, uint[] rest)
        {
            uint[] r = new uint[rest.Length + 1];
            r[0] = head;
            Array.Copy(rest, 0, r, 1, rest.Length);
            return r;
        }

        // ---------- module-level ----------

        public void Capability(uint cap)
        {
            if (_caps.Add(cap)) Emit(_capabilities, Op.Capability, cap);
        }

        public uint GlslStd450()
        {
            if (_glsl == 0) { _glsl = NewId(); Emit(_extImports, Op.ExtInstImport, Prepend(_glsl, StringWords("GLSL.std.450"))); }
            return _glsl;
        }

        public void EntryPoint(uint function, string name, uint[] interfaceVars, uint localX, uint localY, uint localZ)
        {
            uint[] ops = Concat(Prepend(Spv.ExecutionModelGLCompute, Prepend(function, StringWords(name))), interfaceVars);
            Emit(_entryPoints, Op.EntryPoint, ops);
            Emit(_execModes, Op.ExecutionMode, function, Spv.ExecutionModeLocalSize, localX, localY, localZ);
        }

        public void Name(uint id, string name) { Emit(_debug, Op.Name, Prepend(id, StringWords(name))); }
        public void Decorate(uint id, uint decoration, params uint[] args) { Emit(_annotations, Op.Decorate, Prepend(id, Prepend(decoration, args))); }
        public void MemberDecorate(uint structType, uint member, uint decoration, params uint[] args)
        {
            Emit(_annotations, Op.MemberDecorate, Prepend(structType, Prepend(member, Prepend(decoration, args))));
        }

        // ---------- types and constants (deduplicated) ----------

        uint CachedType(string key, ushort opcode, params uint[] operands)
        {
            uint id;
            if (_typeCache.TryGetValue(key, out id)) return id;
            id = NewId();
            Emit(_globals, opcode, Prepend(id, operands));
            _typeCache[key] = id;
            return id;
        }

        public uint TypeVoid() { return CachedType("void", Op.TypeVoid); }
        public uint TypeBool() { return CachedType("bool", Op.TypeBool); }
        public uint TypeUInt(uint width) { return CachedType("u" + width, Op.TypeInt, width, 0); }
        public uint TypeInt(uint width) { return CachedType("i" + width, Op.TypeInt, width, 1); }
        public uint TypeFloat(uint width) { return CachedType("f" + width, Op.TypeFloat, width); }
        public uint TypeVector(uint component, uint count) { return CachedType("v" + component + "x" + count, Op.TypeVector, component, count); }
        public uint TypePointer(uint storage, uint type) { return CachedType("p" + storage + "_" + type, Op.TypePointer, storage, type); }
        public uint TypeFunction(uint ret) { return CachedType("fn" + ret, Op.TypeFunction, ret); }
        public uint TypeArray(uint element, uint length) { return CachedType("a" + element + "_" + length, Op.TypeArray, element, length); }

        // Runtime arrays and structs carry decorations, so each call makes a new type.
        public uint TypeRuntimeArray(uint element, uint stride)
        {
            uint id = NewId();
            Emit(_globals, Op.TypeRuntimeArray, id, element);
            Decorate(id, Spv.DecorationArrayStride, stride);
            return id;
        }

        public uint TypeStruct(params uint[] members)
        {
            uint id = NewId();
            Emit(_globals, Op.TypeStruct, Prepend(id, members));
            return id;
        }

        // OpConstant is ResultType, ResultId, value (unlike type declarations, which start with the ResultId).
        uint CachedConstant(string key, uint type, uint bits)
        {
            uint id;
            if (_typeCache.TryGetValue(key, out id)) return id;
            id = NewId();
            Emit(_globals, Op.Constant, type, id, bits);
            _typeCache[key] = id;
            return id;
        }

        public uint ConstUInt(uint value) { return CachedConstant("cu" + value, TypeUInt(32), value); }
        public uint ConstInt(int value) { return CachedConstant("ci" + value, TypeInt(32), (uint)value); }
        public unsafe uint ConstFloat(float value)
        {
            uint bits = *(uint*)&value;
            return CachedConstant("cf" + bits, TypeFloat(32), bits);
        }

        public uint GlobalVariable(uint pointerType, uint storage)
        {
            uint id = NewId();
            Emit(_globals, Op.Variable, pointerType, id, storage);
            return id;
        }

        // A storage buffer at (set 0, binding) holding a runtime array of elementType: { T data[]; }
        public uint StorageBuffer(uint binding, uint elementType, uint stride, bool readOnly)
        {
            uint arr = TypeRuntimeArray(elementType, stride);
            uint st = TypeStruct(arr);
            Decorate(st, Spv.DecorationBlock);
            MemberDecorate(st, 0, Spv.DecorationOffset, 0);
            if (readOnly) MemberDecorate(st, 0, Spv.DecorationNonWritable);
            uint v = GlobalVariable(TypePointer(Spv.StorageStorageBuffer, st), Spv.StorageStorageBuffer);
            Decorate(v, Spv.DecorationDescriptorSet, 0);
            Decorate(v, Spv.DecorationBinding, binding);
            return v;
        }

        // Push constant block of 32-bit members at consecutive offsets.
        public uint PushConstants(params uint[] memberTypes)
        {
            uint st = TypeStruct(memberTypes);
            Decorate(st, Spv.DecorationBlock);
            for (uint i = 0; i < memberTypes.Length; i++) MemberDecorate(st, i, Spv.DecorationOffset, i * 4);
            return GlobalVariable(TypePointer(Spv.StoragePushConstant, st), Spv.StoragePushConstant);
        }

        public uint BuiltInInput(uint builtIn, uint type)
        {
            uint v = GlobalVariable(TypePointer(Spv.StorageInput, type), Spv.StorageInput);
            Decorate(v, Spv.DecorationBuiltIn, builtIn);
            return v;
        }

        // ---------- function bodies ----------

        public uint BeginFunction(uint returnType)
        {
            uint id = NewId();
            Emit(_functions, Op.Function, returnType, id, Spv.FunctionControlNone, TypeFunction(returnType));
            return id;
        }

        public void EndFunction() { Emit(_functions, Op.FunctionEnd); }

        public void Label(uint id) { Emit(_functions, Op.Label, id); }

        // Instruction with a result: opcode resultType result operands...
        public uint Value(ushort opcode, uint resultType, params uint[] operands)
        {
            uint id = NewId();
            Emit(_functions, opcode, Prepend(resultType, Prepend(id, operands)));
            return id;
        }

        // Instruction without a result.
        public void Instr(ushort opcode, params uint[] operands) { Emit(_functions, opcode, operands); }

        public uint Load(uint type, uint pointer) { return Value(Op.Load, type, pointer); }
        public void Store(uint pointer, uint value) { Instr(Op.Store, pointer, value); }
        public uint AccessChain(uint pointerType, uint baseVar, params uint[] indices) { return Value(Op.AccessChain, pointerType, Prepend(baseVar, indices)); }
        public uint Glsl(uint resultType, uint instruction, params uint[] args) { return Value(Op.ExtInst, resultType, Prepend(GlslStd450(), Prepend(instruction, args))); }

        // ---------- output ----------

        public uint[] ToWords()
        {
            List<uint> w = new List<uint>();
            w.Add(Magic);
            w.Add(Version13);
            w.Add(0);          // generator
            w.Add(_nextId);    // bound
            w.Add(0);          // schema
            w.AddRange(_capabilities); w.AddRange(_extImports); w.AddRange(_memoryModel);
            w.AddRange(_entryPoints); w.AddRange(_execModes); w.AddRange(_debug);
            w.AddRange(_annotations); w.AddRange(_globals); w.AddRange(_functions);
            return w.ToArray();
        }
    }

    // Shader library. Each method returns a SPIR-V module.
    public static class Shaders
    {
        public const uint AddLocalSize = 64;

        // c[i] = a[i] + b[i] for i < n.   bindings: 0 = a, 1 = b, 2 = c; push constant: uint n
        public static uint[] AddF32()
        {
            SpirvBuilder s = new SpirvBuilder();
            uint tVoid = s.TypeVoid(), tU32 = s.TypeUInt(32), tF32 = s.TypeFloat(32), tBool = s.TypeBool();
            uint tV3 = s.TypeVector(tU32, 3);

            uint gid = s.BuiltInInput(Spv.BuiltInGlobalInvocationId, tV3);
            uint a = s.StorageBuffer(0, tF32, 4, true);
            uint b = s.StorageBuffer(1, tF32, 4, true);
            uint c = s.StorageBuffer(2, tF32, 4, false);
            uint pc = s.PushConstants(tU32);
            uint pF32 = s.TypePointer(Spv.StorageStorageBuffer, tF32);
            uint pPcU32 = s.TypePointer(Spv.StoragePushConstant, tU32);
            uint zero = s.ConstUInt(0);

            uint main = s.BeginFunction(tVoid);
            s.Label(s.NewId());
            uint i = s.Value(Op.CompositeExtract, tU32, s.Load(tV3, gid), 0);
            uint n = s.Load(tU32, s.AccessChain(pPcU32, pc, zero));
            uint inRange = s.Value(Op.ULessThan, tBool, i, n);
            uint body = s.NewId(), end = s.NewId();
            s.Instr(Op.SelectionMerge, end, Spv.SelectionControlNone);
            s.Instr(Op.BranchConditional, inRange, body, end);

            s.Label(body);
            uint va = s.Load(tF32, s.AccessChain(pF32, a, zero, i));
            uint vb = s.Load(tF32, s.AccessChain(pF32, b, zero, i));
            uint sum = s.Value(Op.FAdd, tF32, va, vb);
            s.Store(s.AccessChain(pF32, c, zero, i), sum);
            s.Instr(Op.Branch, end);

            s.Label(end);
            s.Instr(Op.Return);
            s.EndFunction();

            s.Name(main, "main");
            s.EntryPoint(main, "main", new uint[] { gid }, AddLocalSize, 1, 1);
            return s.ToWords();
        }
    }
}
