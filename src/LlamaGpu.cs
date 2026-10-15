// Llama-family forward pass on the GPU through Vulkan compute. C# 5 only.
// Same graph and rounding points as LlamaCpu, which is its ground truth.
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public sealed unsafe class LlamaGpu : IDisposable
    {
        public readonly int NEmbd, NFf, NLayer, NHead, NHeadKv, HeadDim, NRot, NVocab, MaxCtx;
        public readonly float Eps, RopeBase;
        public long WeightBytes;

        // A weight matrix on the GPU. Q8_0: A = qs bytes, B = f32 scales. K-quants: A = blocks (Q6_K padded), B unused.
        sealed class GW { public GgmlType Type; public GpuBuffer A, B; public int Rows, Cols, Nb; }

        sealed class Layer
        {
            public GpuBuffer AttnNorm, FfnNorm, KCache, VCache;
            public GW Wq, Wk, Wv, Wo, Gate, Up, Down;
        }

        readonly GpuDevice _gpu;
        readonly Weight _tokEmbd;
        readonly float* _ropeFreqs;
        readonly GW _output;
        readonly GpuBuffer _outputNorm;
        readonly Layer[] _layers;
        readonly int _kvDim;
        readonly List<IDisposable> _owned = new List<IDisposable>();

        readonly GpuKernel _kQuant, _kQuantK, _kMvQ4K, _kMvQ5K, _kMvQ6K, _kMatVec, _kNorm, _kRope, _kKv, _kAttn, _kAdd, _kSilu;
        readonly GpuBuffer _x, _xb, _xb2, _q, _k, _v, _attn, _g, _u, _xq, _xd, _kq, _kd, _cs, _scores, _logits;
        readonly float[] _xHost, _csHost, _logitsHost;

        public LlamaGpu(GpuDevice gpu, GgufFile g, int maxCtx)
        {
            _gpu = gpu;
            string arch = g.GetString("general.architecture");
            if (arch != "llama") throw new NotSupportedException("Architecture '" + arch + "' is not supported yet.");
            NEmbd = (int)g.GetInt("llama.embedding_length");
            NFf = (int)g.GetInt("llama.feed_forward_length");
            NLayer = (int)g.GetInt("llama.block_count");
            NHead = (int)g.GetInt("llama.attention.head_count");
            NHeadKv = g.Has("llama.attention.head_count_kv") ? (int)g.GetInt("llama.attention.head_count_kv") : NHead;
            HeadDim = g.Has("llama.attention.key_length") ? (int)g.GetInt("llama.attention.key_length") : NEmbd / NHead;
            NRot = g.Has("llama.rope.dimension_count") ? (int)g.GetInt("llama.rope.dimension_count") : HeadDim;
            Eps = g.GetFloat("llama.attention.layer_norm_rms_epsilon");
            RopeBase = g.Has("llama.rope.freq_base") ? g.GetFloat("llama.rope.freq_base") : 10000f;
            MaxCtx = maxCtx;
            _kvDim = NHeadKv * HeadDim;
            if (NEmbd % 32 != 0 || NFf % 32 != 0) throw new NotSupportedException("Dimensions must be multiples of 32.");

            try
            {
                _kQuant = Kernel(Kernels.QuantizeQ8(), 3, 1);
                _kMatVec = Kernel(Kernels.MatVecQ8(), 5, 3);
                _kQuantK = Kernel(KernelsK.QuantizeQ8K(), 3, 1);
                _kMvQ4K = Kernel(KernelsK.MatVec(GgmlType.Q4_K), 4, 3);
                _kMvQ5K = Kernel(KernelsK.MatVec(GgmlType.Q5_K), 4, 3);
                _kMvQ6K = Kernel(KernelsK.MatVec(GgmlType.Q6_K), 4, 3);
                _kNorm = Kernel(Kernels.RmsNorm(), 3, 2);
                _kRope = Kernel(Kernels.Rope(), 2, 3);
                _kKv = Kernel(Kernels.KvStore(), 4, 2);
                _kAttn = Kernel(Kernels.Attention(), 5, 6);
                _kAdd = Kernel(Kernels.Add(), 2, 1);
                _kSilu = Kernel(Kernels.SiluMul(), 2, 1);

                _tokEmbd = new Weight(T(g, "token_embd.weight"));
                _output = UploadW(g.TensorsByName.ContainsKey("output.weight") ? T(g, "output.weight") : T(g, "token_embd.weight"));
                NVocab = _output.Rows;
                _outputNorm = UploadF32(T(g, "output_norm.weight"));
                _ropeFreqs = g.TensorsByName.ContainsKey("rope_freqs.weight") ? (float*)T(g, "rope_freqs.weight").Data : null;

                _layers = new Layer[NLayer];
                for (int i = 0; i < NLayer; i++)
                {
                    string p = "blk." + i + ".";
                    Layer l = new Layer();
                    l.AttnNorm = UploadF32(T(g, p + "attn_norm.weight"));
                    l.FfnNorm = UploadF32(T(g, p + "ffn_norm.weight"));
                    l.Wq = UploadW(T(g, p + "attn_q.weight"));
                    l.Wk = UploadW(T(g, p + "attn_k.weight"));
                    l.Wv = UploadW(T(g, p + "attn_v.weight"));
                    l.Wo = UploadW(T(g, p + "attn_output.weight"));
                    l.Gate = UploadW(T(g, p + "ffn_gate.weight"));
                    l.Up = UploadW(T(g, p + "ffn_up.weight"));
                    l.Down = UploadW(T(g, p + "ffn_down.weight"));
                    l.KCache = Buf((ulong)maxCtx * (ulong)_kvDim * 4, false);
                    l.VCache = Buf((ulong)maxCtx * (ulong)_kvDim * 4, false);
                    _layers[i] = l;
                }

                int qDim = NHead * HeadDim, maxIn = Math.Max(Math.Max(NEmbd, qDim), NFf);
                _x = Buf((ulong)NEmbd * 4, true); _xb = Buf((ulong)NEmbd * 4, true); _xb2 = Buf((ulong)NEmbd * 4, true);
                _q = Buf((ulong)qDim * 4, true); _k = Buf((ulong)_kvDim * 4, true); _v = Buf((ulong)_kvDim * 4, true);
                _attn = Buf((ulong)qDim * 4, true);
                _g = Buf((ulong)NFf * 4, true); _u = Buf((ulong)NFf * 4, true);
                _xq = Buf((ulong)maxIn, true); _xd = Buf((ulong)(maxIn / 32) * 4, true);
                _kq = Buf((ulong)maxIn, true); _kd = Buf((ulong)(maxIn / 256 + 1) * 4, true);
                _cs = Buf((ulong)NRot * 4, true);
                _scores = Buf((ulong)NHead * (ulong)maxCtx * 4, false);
                _logits = Buf((ulong)NVocab * 4, true);
                _xHost = new float[NEmbd]; _csHost = new float[NRot]; _logitsHost = new float[NVocab];
            }
            catch { Dispose(); throw; }
        }

        static GgufTensor T(GgufFile g, string name)
        {
            GgufTensor t;
            if (!g.TensorsByName.TryGetValue(name, out t)) throw new KeyNotFoundException("Missing tensor " + name);
            return t;
        }

        GpuKernel Kernel(uint[] spirv, int bindings, int pushWords)
        {
            GpuKernel k = _gpu.CreateKernel(spirv, bindings, (uint)pushWords * 4, 1024);
            _owned.Add(k);
            return k;
        }

        GpuBuffer Buf(ulong bytes, bool hostVisible)
        {
            GpuBuffer b = _gpu.CreateBuffer(Math.Max(bytes, 4), hostVisible);
            _owned.Add(b);
            return b;
        }

        GpuBuffer UploadF32(GgufTensor t)
        {
            if (t.Type != GgmlType.F32) throw new NotSupportedException(t.Name + " must be f32.");
            GpuBuffer b = Buf((ulong)t.SizeBytes, false);
            _gpu.Upload(b, 0, (IntPtr)t.Data, (ulong)t.SizeBytes);
            WeightBytes += t.SizeBytes;
            return b;
        }

        GW UploadW(GgufTensor t)
        {
            switch (t.Type)
            {
                case GgmlType.Q8_0: return UploadQ8_0(t);
                case GgmlType.Q4_K:
                case GgmlType.Q5_K:
                case GgmlType.Q6_K: return UploadK(t);
            }
            throw new NotSupportedException(t.Name + " is " + GgmlTypes.Name(t.Type) + "; the GPU supports q8_0, q4_K, q5_K and q6_K.");
        }

        // Repacks block_q8_0 {fp16 d; int8 qs[32]} into aligned arrays: qs bytes (4 per u32 word) and f32 scales.
        GW UploadQ8_0(GgufTensor t)
        {
            Weight w = new Weight(t);
            GW q = new GW();
            q.Type = t.Type; q.Rows = w.Rows; q.Cols = w.Cols; q.Nb = w.Cols / 32;
            q.A = Buf((ulong)w.Rows * (ulong)w.Cols, false);
            q.B = Buf((ulong)w.Rows * (ulong)q.Nb * 4, false);

            int rowsPerChunk = Math.Max(1, (16 << 20) / w.Cols);
            byte[] qs = new byte[(long)rowsPerChunk * w.Cols];
            float[] d = new float[(long)rowsPerChunk * q.Nb];
            for (int r0 = 0; r0 < w.Rows; r0 += rowsPerChunk)
            {
                int nr = Math.Min(rowsPerChunk, w.Rows - r0);
                for (int r = 0; r < nr; r++)
                {
                    byte* row = w.Data + (long)(r0 + r) * w.RowBytes;
                    for (int b = 0; b < q.Nb; b++)
                    {
                        byte* blk = row + b * CpuRef.BlockQ8_0Bytes;
                        d[r * q.Nb + b] = Half.ToFloat(*(ushort*)blk);
                        long o = (long)r * w.Cols + b * 32;
                        for (int j = 0; j < 32; j++) qs[o + j] = blk[2 + j];
                    }
                }
                fixed (byte* pq = qs) _gpu.Upload(q.A, (ulong)r0 * (ulong)w.Cols, (IntPtr)pq, (ulong)nr * (ulong)w.Cols);
                fixed (float* pd = d) _gpu.Upload(q.B, (ulong)r0 * (ulong)q.Nb * 4, (IntPtr)pd, (ulong)nr * (ulong)q.Nb * 4);
            }
            WeightBytes += (long)w.Rows * w.Cols + (long)w.Rows * q.Nb * 4;
            return q;
        }

        // K-quant blocks go up as-is (Q4_K/Q5_K are whole words); Q6_K blocks are padded from 210 to 212 bytes.
        GW UploadK(GgufTensor t)
        {
            Weight w = new Weight(t);
            if (w.Cols % CpuRef.QK_K != 0) throw new NotSupportedException(t.Name + ": rows must be a multiple of 256.");
            GW q = new GW();
            q.Type = t.Type; q.Rows = w.Rows; q.Cols = w.Cols; q.Nb = w.Cols / CpuRef.QK_K;
            long blocks = (long)w.Rows * q.Nb;
            if (t.Type != GgmlType.Q6_K)
            {
                q.A = Buf((ulong)t.SizeBytes, false);
                _gpu.Upload(q.A, 0, (IntPtr)t.Data, (ulong)t.SizeBytes);
                WeightBytes += t.SizeBytes;
                return q;
            }
            q.A = Buf((ulong)blocks * KernelsK.Q6_KPaddedBytes, false);
            int blocksPerChunk = (16 << 20) / KernelsK.Q6_KPaddedBytes;
            byte[] buf = new byte[(long)blocksPerChunk * KernelsK.Q6_KPaddedBytes];
            for (long b0 = 0; b0 < blocks; b0 += blocksPerChunk)
            {
                int nbk = (int)Math.Min(blocksPerChunk, blocks - b0);
                fixed (byte* dst = buf)
                {
                    for (int i = 0; i < nbk; i++)
                    {
                        byte* src = t.Data + (b0 + i) * CpuRef.BlockQ6_KBytes;
                        byte* o = dst + (long)i * KernelsK.Q6_KPaddedBytes;
                        for (int j = 0; j < CpuRef.BlockQ6_KBytes; j++) o[j] = src[j];
                        o[210] = 0; o[211] = 0;
                    }
                    _gpu.Upload(q.A, (ulong)b0 * KernelsK.Q6_KPaddedBytes, (IntPtr)dst, (ulong)nbk * KernelsK.Q6_KPaddedBytes);
                }
            }
            WeightBytes += blocks * KernelsK.Q6_KPaddedBytes;
            return q;
        }

        static uint Bits(float f) { return *(uint*)&f; }
        static uint Groups(int n, uint size) { return (uint)((n + size - 1) / size); }

        void Norm(GpuBuffer x, GpuBuffer w, GpuBuffer y)
        {
            _gpu.Record(_kNorm, new GpuBuffer[] { x, w, y }, new uint[] { (uint)NEmbd, Bits(Eps) }, 1, 1, 1);
        }

        // Quantizes x into the activation format(s) the following matvecs need (Q8_0 and/or Q8_K).
        void Quant(GpuBuffer x, int n, params GW[] users)
        {
            bool q80 = false, q8k = false;
            foreach (GW w in users) { if (w.Type == GgmlType.Q8_0) q80 = true; else q8k = true; }
            if (q80)
            {
                int nb = n / 32;
                _gpu.Record(_kQuant, new GpuBuffer[] { x, _xq, _xd }, new uint[] { (uint)nb }, Groups(nb, Kernels.Wg), 1, 1);
            }
            if (q8k)
            {
                int nb = n / CpuRef.QK_K;
                _gpu.Record(_kQuantK, new GpuBuffer[] { x, _kq, _kd }, new uint[] { (uint)nb }, Groups(nb, Kernels.Wg), 1, 1);
            }
        }

        // Uses the activations last quantized by Quant().
        void MatVec(GW w, GpuBuffer y)
        {
            uint gx = (uint)Math.Min(w.Rows, 65535), gy = (uint)((w.Rows + gx - 1) / gx);
            uint[] push = new uint[] { (uint)w.Rows, (uint)w.Nb, gx };
            switch (w.Type)
            {
                case GgmlType.Q8_0: _gpu.Record(_kMatVec, new GpuBuffer[] { w.A, w.B, _xq, _xd, y }, push, gx, gy, 1); return;
                case GgmlType.Q4_K: _gpu.Record(_kMvQ4K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1); return;
                case GgmlType.Q5_K: _gpu.Record(_kMvQ5K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1); return;
                case GgmlType.Q6_K: _gpu.Record(_kMvQ6K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1); return;
            }
            throw new NotSupportedException(GgmlTypes.Name(w.Type));
        }

        void Add(GpuBuffer x, GpuBuffer y, int n)
        {
            _gpu.Record(_kAdd, new GpuBuffer[] { x, y }, new uint[] { (uint)n }, Groups(n, Kernels.Wg), 1, 1);
        }

        // Diagnostics: when >= 0, Forward stops after this many layers and returns the residual stream instead of logits.
        public int DebugLayers = -1;

        // Run one token at position pos; returns logits (shared buffer, valid until the next call).
        public float[] Forward(int token, int pos)
        {
            if (pos >= MaxCtx) throw new InvalidOperationException("Context full.");
            CpuRef.GetRow(_tokEmbd, token, _xHost);
            _x.Upload(_xHost, 0);
            CpuRef.RopeTable(pos, NRot, RopeBase, _ropeFreqs, _csHost);
            _cs.Upload(_csHost, 0);

            int qDim = NHead * HeadDim;
            float scale = 1.0f / (float)Math.Sqrt(HeadDim);
            uint halfRot = (uint)(NRot / 2);

            _gpu.Begin();
            int nRun = DebugLayers >= 0 ? Math.Min(DebugLayers, NLayer) : NLayer;
            for (int il = 0; il < nRun; il++)
            {
                Layer l = _layers[il];

                Norm(_x, l.AttnNorm, _xb);
                Quant(_xb, NEmbd, l.Wq, l.Wk, l.Wv);
                MatVec(l.Wq, _q);
                MatVec(l.Wk, _k);
                MatVec(l.Wv, _v);
                _gpu.Record(_kRope, new GpuBuffer[] { _q, _cs }, new uint[] { (uint)NHead, (uint)HeadDim, halfRot }, Groups(NHead * (int)halfRot, Kernels.Wg), 1, 1);
                _gpu.Record(_kRope, new GpuBuffer[] { _k, _cs }, new uint[] { (uint)NHeadKv, (uint)HeadDim, halfRot }, Groups(NHeadKv * (int)halfRot, Kernels.Wg), 1, 1);
                _gpu.Record(_kKv, new GpuBuffer[] { _k, _v, l.KCache, l.VCache }, new uint[] { (uint)_kvDim, (uint)pos }, Groups(_kvDim, Kernels.Wg), 1, 1);
                _gpu.Record(_kAttn, new GpuBuffer[] { _q, l.KCache, l.VCache, _scores, _attn },
                    new uint[] { (uint)(pos + 1), (uint)HeadDim, (uint)_kvDim, (uint)(NHead / NHeadKv), (uint)MaxCtx, Bits(scale) }, (uint)NHead, 1, 1);
                Quant(_attn, qDim, l.Wo);
                MatVec(l.Wo, _xb2);
                Add(_x, _xb2, NEmbd);

                Norm(_x, l.FfnNorm, _xb);
                Quant(_xb, NEmbd, l.Gate, l.Up);
                MatVec(l.Gate, _g);
                MatVec(l.Up, _u);
                _gpu.Record(_kSilu, new GpuBuffer[] { _g, _u }, new uint[] { (uint)NFf }, Groups(NFf, Kernels.Wg), 1, 1);
                Quant(_g, NFf, l.Down);
                MatVec(l.Down, _xb2);
                Add(_x, _xb2, NEmbd);
            }
            if (DebugLayers >= 0)
            {
                _gpu.SubmitAndWait();
                float[] hidden = new float[NEmbd];
                _x.Download(hidden, 0);
                return hidden;
            }
            Norm(_x, _outputNorm, _xb);
            Quant(_xb, NEmbd, _output);
            MatVec(_output, _logits);
            _gpu.SubmitAndWait();

            _logits.Download(_logitsHost, 0);
            return _logitsHost;
        }

        // Starts a new sequence. The KV cache needs no clearing: attention only reads positions < pos + 1.
        public void Reset() { }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
            _owned.Clear();
        }
    }
}
