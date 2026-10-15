// GGUF reader. C# 5 only.
// Format: dev/ref/llama.cpp/ggml/include/gguf.h and gguf.md. Ported from gguf.cpp (MIT, llama.cpp).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace Phoenix
{
    public enum GgufType
    {
        UInt8 = 0, Int8 = 1, UInt16 = 2, Int16 = 3, UInt32 = 4, Int32 = 5, Float32 = 6,
        Bool = 7, String = 8, Array = 9, UInt64 = 10, Int64 = 11, Float64 = 12
    }

    // Values match enum ggml_type in ggml.h.
    public enum GgmlType
    {
        F32 = 0, F16 = 1, Q4_0 = 2, Q4_1 = 3, Q5_0 = 6, Q5_1 = 7, Q8_0 = 8, Q8_1 = 9,
        Q2_K = 10, Q3_K = 11, Q4_K = 12, Q5_K = 13, Q6_K = 14, Q8_K = 15,
        IQ2_XXS = 16, IQ3_S = 21, IQ2_S = 22,
        I8 = 24, I16 = 25, I32 = 26, I64 = 27, F64 = 28, BF16 = 30
    }

    public static class GgmlTypes
    {
        // Elements per block and bytes per block (sizeof(block_*) in ggml-common.h).
        public static bool TryGetLayout(GgmlType t, out int blockSize, out int typeSize)
        {
            switch (t)
            {
                case GgmlType.F32:  blockSize = 1;   typeSize = 4;   return true;
                case GgmlType.F16:  blockSize = 1;   typeSize = 2;   return true;
                case GgmlType.BF16: blockSize = 1;   typeSize = 2;   return true;
                case GgmlType.I8:   blockSize = 1;   typeSize = 1;   return true;
                case GgmlType.I16:  blockSize = 1;   typeSize = 2;   return true;
                case GgmlType.I32:  blockSize = 1;   typeSize = 4;   return true;
                case GgmlType.I64:  blockSize = 1;   typeSize = 8;   return true;
                case GgmlType.F64:  blockSize = 1;   typeSize = 8;   return true;
                case GgmlType.Q4_0: blockSize = 32;  typeSize = 18;  return true;
                case GgmlType.Q4_1: blockSize = 32;  typeSize = 20;  return true;
                case GgmlType.Q5_0: blockSize = 32;  typeSize = 22;  return true;
                case GgmlType.Q5_1: blockSize = 32;  typeSize = 24;  return true;
                case GgmlType.Q8_0: blockSize = 32;  typeSize = 34;  return true;
                case GgmlType.Q8_1: blockSize = 32;  typeSize = 36;  return true;
                case GgmlType.Q2_K: blockSize = 256; typeSize = 84;  return true;
                case GgmlType.Q3_K: blockSize = 256; typeSize = 110; return true;
                case GgmlType.Q4_K: blockSize = 256; typeSize = 144; return true;
                case GgmlType.Q5_K: blockSize = 256; typeSize = 176; return true;
                case GgmlType.Q6_K: blockSize = 256; typeSize = 210; return true;
                case GgmlType.Q8_K: blockSize = 256; typeSize = 292; return true;
                case GgmlType.IQ2_XXS: blockSize = 256; typeSize = 66; return true;
                case GgmlType.IQ2_S: blockSize = 256; typeSize = 82; return true;
                case GgmlType.IQ3_S: blockSize = 256; typeSize = 110; return true;
            }
            blockSize = 0; typeSize = 0;
            return false;
        }

        // Lower-case name as llama.cpp prints it (ggml_type_name).
        public static string Name(GgmlType t)
        {
            if (Enum.IsDefined(typeof(GgmlType), t)) return t.ToString().ToLowerInvariant().Replace("_k", "_K");
            return "type" + (int)t;
        }
    }

    public sealed class GgufValue
    {
        public GgufType Type;
        public GgufType ArrayType;   // valid when Type == Array
        public long ArrayLength;     // valid when Type == Array
        public object Value;         // scalar (boxed), string, or typed array (string[], int[], float[], ...)
    }

    public sealed class GgufTensor
    {
        public string Name;
        public long[] Dims;          // ne[0] is the contiguous (row) dimension
        public GgmlType Type;
        public long Offset;          // relative to the data section
        public long SizeBytes;
        public unsafe byte* Data;    // mapped pointer to the first byte

        public long ElementCount
        {
            get { long n = 1; for (int i = 0; i < Dims.Length; i++) n *= Dims[i]; return n; }
        }
    }

    public sealed unsafe class GgufFile : IDisposable
    {
        public const uint Magic = 0x46554747; // "GGUF" little-endian
        public const int DefaultAlignment = 32;

        public string Path;
        public long FileLength;
        public uint Version;
        public int Alignment;
        public long DataOffset;      // absolute offset of the tensor data section
        public readonly List<string> Keys = new List<string>();                        // file order
        public readonly Dictionary<string, GgufValue> Metadata = new Dictionary<string, GgufValue>();
        public readonly List<GgufTensor> Tensors = new List<GgufTensor>();
        public readonly Dictionary<string, GgufTensor> TensorsByName = new Dictionary<string, GgufTensor>();

        MemoryMappedFile _mmf;
        MemoryMappedViewAccessor _view;
        byte* _base;
        long _pos;

        public static GgufFile Open(string path)
        {
            GgufFile f = new GgufFile();
            try { f.Load(path); }
            catch { f.Dispose(); throw; }
            return f;
        }

        void Load(string path)
        {
            Path = System.IO.Path.GetFullPath(path);
            FileLength = new FileInfo(Path).Length;
            if (FileLength < 24) throw new InvalidDataException("File too small to be GGUF.");

            // Shared read access, so the same model can be opened more than once (and by other readers).
            FileStream fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                _mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, null, HandleInheritability.None, false);
            }
            catch { fs.Dispose(); throw; }
            _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
            _base += _view.PointerOffset;

            if (ReadU32() != Magic) throw new InvalidDataException("Not a GGUF file (bad magic).");
            Version = ReadU32();
            if (Version < 2 || Version > 3) throw new InvalidDataException("Unsupported GGUF version " + Version + ".");

            long nTensors = ReadI64();
            long nKv = ReadI64();
            if (nTensors < 0 || nKv < 0) throw new InvalidDataException("Negative tensor or KV count.");

            for (long i = 0; i < nKv; i++)
            {
                string key = ReadString();
                GgufType type = (GgufType)ReadI32();
                GgufValue v = new GgufValue();
                v.Type = type;
                if (type == GgufType.Array)
                {
                    v.ArrayType = (GgufType)ReadI32();
                    v.ArrayLength = ReadI64();
                    v.Value = ReadArray(v.ArrayType, v.ArrayLength);
                }
                else
                {
                    v.Value = ReadScalar(type);
                }
                if (Metadata.ContainsKey(key)) throw new InvalidDataException("Duplicate key " + key + ".");
                Keys.Add(key);
                Metadata.Add(key, v);
            }

            Alignment = DefaultAlignment;
            GgufValue align;
            if (Metadata.TryGetValue("general.alignment", out align))
            {
                Alignment = (int)Convert.ToUInt32(align.Value);
                if (Alignment <= 0 || (Alignment & (Alignment - 1)) != 0)
                    throw new InvalidDataException("general.alignment must be a power of two.");
            }

            for (long i = 0; i < nTensors; i++)
            {
                GgufTensor t = new GgufTensor();
                t.Name = ReadString();
                uint nDims = ReadU32();
                if (nDims == 0 || nDims > 4) throw new InvalidDataException("Tensor " + t.Name + " has " + nDims + " dims.");
                t.Dims = new long[nDims];
                for (int d = 0; d < nDims; d++)
                {
                    t.Dims[d] = ReadI64();
                    if (t.Dims[d] <= 0) throw new InvalidDataException("Tensor " + t.Name + " has a non-positive dim.");
                }
                t.Type = (GgmlType)ReadI32();
                t.Offset = (long)ReadU64();
                if (TensorsByName.ContainsKey(t.Name)) throw new InvalidDataException("Duplicate tensor " + t.Name + ".");
                Tensors.Add(t);
                TensorsByName.Add(t.Name, t);
            }

            DataOffset = AlignUp(_pos, Alignment);

            for (int i = 0; i < Tensors.Count; i++)
            {
                GgufTensor t = Tensors[i];
                int blockSize, typeSize;
                if (!GgmlTypes.TryGetLayout(t.Type, out blockSize, out typeSize))
                    throw new InvalidDataException("Tensor " + t.Name + " uses unsupported type " + GgmlTypes.Name(t.Type) + ".");
                if (t.Dims[0] % blockSize != 0)
                    throw new InvalidDataException("Tensor " + t.Name + " row is not a multiple of the block size.");
                if (t.Offset % Alignment != 0)
                    throw new InvalidDataException("Tensor " + t.Name + " offset is not aligned.");
                t.SizeBytes = t.ElementCount / blockSize * typeSize;
                if (DataOffset + t.Offset + t.SizeBytes > FileLength)
                    throw new InvalidDataException("Tensor " + t.Name + " extends past end of file.");
                t.Data = _base + DataOffset + t.Offset;
            }
        }

        public static long AlignUp(long x, int align)
        {
            return (x + align - 1) / align * align;
        }

        // Typed metadata helpers.
        public bool Has(string key) { return Metadata.ContainsKey(key); }

        public string GetString(string key)
        {
            return (string)Metadata[key].Value;
        }

        public long GetInt(string key)
        {
            return Convert.ToInt64(Metadata[key].Value);
        }

        public float GetFloat(string key)
        {
            return Convert.ToSingle(Metadata[key].Value);
        }

        public T[] GetArray<T>(string key)
        {
            return (T[])Metadata[key].Value;
        }

        // --- raw readers (all bounds-checked against the file length) ---

        void Need(long n)
        {
            if (n < 0 || _pos + n > FileLength) throw new InvalidDataException("Unexpected end of GGUF header.");
        }

        byte ReadU8() { Need(1); return _base[_pos++]; }
        ushort ReadU16() { Need(2); ushort v = *(ushort*)(_base + _pos); _pos += 2; return v; }
        uint ReadU32() { Need(4); uint v = *(uint*)(_base + _pos); _pos += 4; return v; }
        int ReadI32() { Need(4); int v = *(int*)(_base + _pos); _pos += 4; return v; }
        ulong ReadU64() { Need(8); ulong v = *(ulong*)(_base + _pos); _pos += 8; return v; }
        long ReadI64() { Need(8); long v = *(long*)(_base + _pos); _pos += 8; return v; }
        float ReadF32() { Need(4); float v = *(float*)(_base + _pos); _pos += 4; return v; }
        double ReadF64() { Need(8); double v = *(double*)(_base + _pos); _pos += 8; return v; }

        string ReadString()
        {
            long len = (long)ReadU64();
            if (len > int.MaxValue) throw new InvalidDataException("String too long.");
            Need(len);
            string s = Encoding.UTF8.GetString(_base + _pos, (int)len);
            _pos += len;
            return s;
        }

        object ReadScalar(GgufType t)
        {
            switch (t)
            {
                case GgufType.UInt8:   return ReadU8();
                case GgufType.Int8:    return (sbyte)ReadU8();
                case GgufType.UInt16:  return ReadU16();
                case GgufType.Int16:   return (short)ReadU16();
                case GgufType.UInt32:  return ReadU32();
                case GgufType.Int32:   return ReadI32();
                case GgufType.Float32: return ReadF32();
                case GgufType.Bool:    return ReadU8() != 0;
                case GgufType.String:  return ReadString();
                case GgufType.UInt64:  return ReadU64();
                case GgufType.Int64:   return ReadI64();
                case GgufType.Float64: return ReadF64();
            }
            throw new InvalidDataException("Unknown GGUF value type " + (int)t + ".");
        }

        object ReadArray(GgufType t, long n)
        {
            if (n < 0 || n > int.MaxValue) throw new InvalidDataException("Bad array length.");
            int count = (int)n;
            switch (t)
            {
                case GgufType.String:
                {
                    string[] a = new string[count];
                    for (int i = 0; i < count; i++) a[i] = ReadString();
                    return a;
                }
                case GgufType.UInt8:   { byte[] a = new byte[count];     CopyOut(a, count, 1); return a; }
                case GgufType.Int8:    { sbyte[] a = new sbyte[count];   CopyOut(a, count, 1); return a; }
                case GgufType.Bool:    { byte[] raw = new byte[count];   CopyOut(raw, count, 1);
                                         bool[] a = new bool[count]; for (int i = 0; i < count; i++) a[i] = raw[i] != 0; return a; }
                case GgufType.UInt16:  { ushort[] a = new ushort[count]; CopyOut(a, count, 2); return a; }
                case GgufType.Int16:   { short[] a = new short[count];   CopyOut(a, count, 2); return a; }
                case GgufType.UInt32:  { uint[] a = new uint[count];     CopyOut(a, count, 4); return a; }
                case GgufType.Int32:   { int[] a = new int[count];       CopyOut(a, count, 4); return a; }
                case GgufType.Float32: { float[] a = new float[count];   CopyOut(a, count, 4); return a; }
                case GgufType.UInt64:  { ulong[] a = new ulong[count];   CopyOut(a, count, 8); return a; }
                case GgufType.Int64:   { long[] a = new long[count];     CopyOut(a, count, 8); return a; }
                case GgufType.Float64: { double[] a = new double[count]; CopyOut(a, count, 8); return a; }
            }
            throw new InvalidDataException("Unsupported GGUF array type " + (int)t + ".");
        }

        void CopyOut(Array dest, int count, int elemSize)
        {
            long bytes = (long)count * elemSize;
            Need(bytes);
            if (bytes > 0)
            {
                System.Runtime.InteropServices.GCHandle h = System.Runtime.InteropServices.GCHandle.Alloc(dest, System.Runtime.InteropServices.GCHandleType.Pinned);
                try { Buffer.MemoryCopy(_base + _pos, (void*)h.AddrOfPinnedObject(), bytes, bytes); }
                finally { h.Free(); }
            }
            _pos += bytes;
        }

        public void Dispose()
        {
            if (_view != null)
            {
                if (_base != null) _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _base = null;
                _view.Dispose();
                _view = null;
            }
            if (_mmf != null) { _mmf.Dispose(); _mmf = null; }
        }
    }
}
