// Llama-family forward pass on the GPU through Vulkan compute. C# 5 only.
// Same graph and rounding points as LlamaCpu, which is its ground truth.
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public sealed unsafe class LlamaGpu : ILanguageModel
    {
        public readonly int NEmbd, NFf, NLayer, NHead, NHeadKv, HeadDim, NRot, NVocab, MaxCtx;
        public readonly float Eps, RopeBase;
        public readonly ModelConfig Config;
        public long WeightBytes;
        int Ring { get { return (MaxCtx + 7) & ~7; } }   // cache slots (a multiple of 8: fp16 pairs, q8 / q4 words)
        public readonly KvType Kv, KvV;   // K / V cache formats                      // KV cache format (fp16, or 8 / 4-bit with KernelsKv)
        // Words of one cache value group the decode store handles per thread: 2 values (fp16) or 64 (quantized).
        int KvStoreGroups(int values) { return Kv == KvType.F16 && KvV == KvType.F16 ? values / 2 : values / 64; }

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

        readonly Dictionary<GgmlType, GpuKernel> _kMvIQ = new Dictionary<GgmlType, GpuKernel>();
        readonly GpuKernel[] _kQ80 = new GpuKernel[3], _kQ8K = new GpuKernel[3];   // by KernelsQuant.Mode
        GpuBuffer _iqTables;
        readonly GpuKernel _kQuant, _kQuantK, _kMvQ4K, _kMvQ5K, _kMvQ6K, _kMatVec, _kNorm, _kRope, _kKv, _kAttn, _kAdd, _kSilu, _kAttnScores, _kAttnSoftmax, _kAttnOut, _kAttnReduce, _kKvT, _kRopeP;
        GpuBuffer _params, _indirect;
        IntPtr _graph;            // recorded decode pass, replayed every token
        readonly uint[] _paramHost = new uint[KernelsAttention.ParamWords], _indirectHost = new uint[6];
        GpuBuffer _attnPart;
        readonly GpuBuffer _x, _xb, _xb2, _q, _k, _v, _attn, _g, _u, _xq, _xd, _kq, _kd, _cs, _scores, _logits;
        readonly float[] _xHost, _csHost, _logitsHost;

        public LlamaGpu(GpuDevice gpu, GgufFile g, int maxCtx) : this(gpu, g, maxCtx, KvType.F16, KvType.F16) { }
        public LlamaGpu(GpuDevice gpu, GgufFile g, int maxCtx, KvType kv) : this(gpu, g, maxCtx, kv, kv) { }

        public LlamaGpu(GpuDevice gpu, GgufFile g, int maxCtx, KvType kv, KvType kvV)
        {
            _gpu = gpu;
            Config = ModelConfig.FromGguf(g);
            NEmbd = Config.NEmbd; NFf = Config.NFf; NLayer = Config.NLayer; NHead = Config.NHead; NHeadKv = Config.NHeadKv;
            HeadDim = Config.HeadDim; NRot = Config.NRot; Eps = Config.Eps; RopeBase = Config.RopeBase;
            MaxCtx = maxCtx;
            _kvDim = NHeadKv * HeadDim;
            Kv = _kvDim % 64 == 0 ? kv : KvType.F16;
            KvV = _kvDim % 64 == 0 ? kvV : KvType.F16;
            if (NEmbd % 32 != 0 || NFf % 32 != 0) throw new NotSupportedException("Dimensions must be multiples of 32.");

            try
            {
                _kQuant = null;
                foreach (KernelsQuant.Mode md in new KernelsQuant.Mode[] { KernelsQuant.Mode.Plain, KernelsQuant.Mode.SiluMul, KernelsQuant.Mode.RmsNorm })
                {
                    string suffix = md == KernelsQuant.Mode.Plain ? "" : md == KernelsQuant.Mode.SiluMul ? " +silu" : " +norm";
                    _kQ80[(int)md] = Kernel(KernelsQuant.Q80(md, gpu.SubgroupArithmetic), KernelsQuant.Bindings(md), KernelsQuant.PushWords, "quant q8_0" + suffix);
                    _kQ8K[(int)md] = Kernel(KernelsQuant.Q8K(md, gpu.SubgroupArithmetic), KernelsQuant.Bindings(md), KernelsQuant.PushWords, "quant q8_K" + suffix);
                }
                _kMatVec = Kernel(Kernels.MatVecQ8(gpu.IntegerDot), 5, 4, "matvec q8_0");
                _kQuantK = null;
                _kMvQ4K = Kernel(KernelsK.MatVec(GgmlType.Q4_K, gpu.SubgroupArithmetic, gpu.IntegerDot), 4, 4, "matvec q4_K");
                _kMvQ5K = Kernel(KernelsK.MatVec(GgmlType.Q5_K, gpu.SubgroupArithmetic, gpu.IntegerDot), 4, 4, "matvec q5_K");
                _kMvQ6K = Kernel(KernelsK.MatVec(GgmlType.Q6_K, gpu.SubgroupArithmetic, gpu.IntegerDot), 4, 4, "matvec q6_K");
                _kNorm = Kernel(Kernels.RmsNorm(), 3, 2, "rmsnorm");
                _kRope = Kernel(Kernels.Rope(), 2, 4, "rope");
                _kKv = Kernel(Kernels.KvStore(), 4, 2, "kv store");
                _kAttn = Kernel(Kernels.Attention(), 5, 6, "attention");
                uint grp = (uint)(NHead / NHeadKv);
                if (NHead % NHeadKv == 0 && grp <= KernelsAttention.MaxGroup && HeadDim <= KernelsAttention.MaxHeadDim)
                {
                    _kAttnScores = Kernel(KernelsAttention.Scores(grp, KernelsAttention.MaxHeadDim, Kv), 4, 5, "attn scores");
                    _kAttnSoftmax = Kernel(KernelsAttention.Softmax(), 2, 1, "attn softmax");
                    _kAttnOut = Kernel(KernelsAttention.OutputPartial(grp, KvV), 4, 5, "attn out");
                    _kAttnReduce = Kernel(KernelsAttention.OutputReduce(), 3, 1, "attn reduce");
                    _kKvT = Kernel(KernelsKv.Store(Kv, KvV), 5, 2, "kv store " + KernelsKv.Name(Kv) + "/" + KernelsKv.Name(KvV));
                    _kRopeP = Kernel(KernelsAttention.Rope(), 3, 4, "rope");
                }
                _kAdd = Kernel(Kernels.Add(), 2, 1, "add");
                _kSilu = Kernel(Kernels.SiluMul(), 2, 1, "silu*up");

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
                    // Wide attention path: packed fp16 cache (2 bytes per value); the simple fallback kernels use f32.
                    bool wide = _kAttnScores != null;
                    l.KCache = Buf(wide ? KernelsKv.CacheBytes(Kv, Ring, _kvDim) : (ulong)Ring * (ulong)_kvDim * 4, false);
                    l.VCache = Buf(wide ? KernelsKv.CacheBytes(KvV, Ring, _kvDim) : (ulong)Ring * (ulong)_kvDim * 4, false);
                    _layers[i] = l;
                }

                int qDim = NHead * HeadDim, maxIn = Math.Max(Math.Max(NEmbd, qDim), NFf);
                // Per-token inputs written by the CPU; everything else stays in VRAM.
                _x = Buf((ulong)NEmbd * 4, BufferKind.Upload); _cs = Buf((ulong)NRot * 4, BufferKind.Upload);
                _xb = Buf((ulong)NEmbd * 4, false); _xb2 = Buf((ulong)NEmbd * 4, false);
                _q = Buf((ulong)qDim * 4, false); _k = Buf((ulong)_kvDim * 4, false); _v = Buf((ulong)_kvDim * 4, false);
                _attn = Buf((ulong)qDim * 4, false);
                _g = Buf((ulong)NFf * 4, false); _u = Buf((ulong)NFf * 4, false);
                _xq = Buf((ulong)maxIn, false); _xd = Buf((ulong)(maxIn / 32) * 4, false);
                _kq = Buf((ulong)maxIn, false); _kd = Buf((ulong)(maxIn / 256 + 1) * 4, false);
                _scores = Buf((ulong)NHead * (ulong)maxCtx * 4, false);
                _logits = Buf((ulong)NVocab * 4, true);
                _params = Buf((ulong)KernelsAttention.ParamWords * 4, BufferKind.Upload);
                _indirect = Buf(6 * 4, BufferKind.Upload);
                if (_kAttnOut != null)
                    _attnPart = Buf((ulong)((maxCtx + KernelsAttention.Chunk - 1) / KernelsAttention.Chunk) * (ulong)(NHead * HeadDim) * 4, false);
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
            return Kernel(spirv, bindings, pushWords, "kernel");
        }

        GpuKernel Kernel(uint[] spirv, int bindings, int pushWords, string name)
        {
            GpuKernel k = _gpu.CreateKernel(spirv, bindings, (uint)pushWords * 4, 1024);
            k.Name = name;
            _owned.Add(k);
            return k;
        }

        GpuBuffer Buf(ulong bytes, bool hostVisible)
        {
            return Buf(bytes, hostVisible ? BufferKind.Readback : BufferKind.Device);
        }

        GpuBuffer Buf(ulong bytes, BufferKind kind)
        {
            GpuBuffer b = _gpu.CreateBuffer(Math.Max(bytes, 4), kind);
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
            if (KernelsIQ.Handles(t.Type)) return UploadPacked(t);
            throw new NotSupportedException(t.Name + " is " + GgmlTypes.Name(t.Type) + "; the GPU supports q8_0, q3_K-q6_K, iq2_xxs, iq2_s and iq3_s.");
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

        // Q3_K / IQ formats: payload words (block without its fp16 d) + one f32 scale per block.
        GW UploadPacked(GgufTensor t)
        {
            Weight w = new Weight(t);
            if (w.Cols % CpuRef.QK_K != 0) throw new NotSupportedException(t.Name + ": rows must be a multiple of 256.");
            GW q = new GW();
            q.Type = t.Type; q.Rows = w.Rows; q.Cols = w.Cols; q.Nb = w.Cols / CpuRef.QK_K;
            long blocks = (long)w.Rows * q.Nb;
            int bb = CpuRef.KBlockBytes(t.Type), pb = KernelsIQ.PayloadBytes(t.Type);
            int dOff = KernelsIQ.DOffset(t.Type), pOff = KernelsIQ.PayloadOffset(t.Type);
            q.A = Buf((ulong)blocks * (ulong)pb, false);
            q.B = Buf((ulong)blocks * 4, false);
            int perChunk = (16 << 20) / pb;
            byte[] payload = new byte[(long)perChunk * pb];
            float[] d = new float[perChunk];
            for (long b0 = 0; b0 < blocks; b0 += perChunk)
            {
                int n = (int)Math.Min(perChunk, blocks - b0);
                fixed (byte* dst = payload)
                {
                    for (int i = 0; i < n; i++)
                    {
                        byte* src = t.Data + (b0 + i) * bb;
                        d[i] = Half.ToFloat(*(ushort*)(src + dOff));
                        byte* o = dst + (long)i * pb;
                        for (int j = 0; j < pb; j++) o[j] = src[pOff + j];
                    }
                    _gpu.Upload(q.A, (ulong)b0 * (ulong)pb, (IntPtr)dst, (ulong)n * (ulong)pb);
                }
                fixed (float* pd = d) _gpu.Upload(q.B, (ulong)b0 * 4, (IntPtr)pd, (ulong)n * 4);
            }
            WeightBytes += blocks * (pb + 4);

            GpuKernel k;
            if (!_kMvIQ.TryGetValue(t.Type, out k)) _kMvIQ[t.Type] = Kernel(KernelsIQ.MatVec(t.Type), 6, 4, "matvec " + GgmlTypes.Name(t.Type));
            if (_iqTables == null)
            {
                uint[] tab = KernelsIQ.Tables();
                _iqTables = Buf((ulong)tab.Length * 4, false);
                _gpu.Upload(_iqTables, tab);
            }
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
            Quant(KernelsQuant.Mode.Plain, x, null, n, users);
        }

        // Fused transform + quantize: SiluMul quantizes silu(x) * aux, RmsNorm quantizes x / rms(x) * aux.
        void Quant(KernelsQuant.Mode mode, GpuBuffer x, GpuBuffer aux, int n, params GW[] users)
        {
            bool q80 = false, q8k = false;
            foreach (GW w in users) { if (w.Type == GgmlType.Q8_0) q80 = true; else q8k = true; }
            uint[] push = { 0, (uint)n, Bits(Eps) };
            GpuBuffer[] q0 = mode == KernelsQuant.Mode.Plain ? new GpuBuffer[] { x, _xq, _xd } : new GpuBuffer[] { x, _xq, _xd, aux };
            GpuBuffer[] qk = mode == KernelsQuant.Mode.Plain ? new GpuBuffer[] { x, _kq, _kd } : new GpuBuffer[] { x, _kq, _kd, aux };
            if (q80)
            {
                int nb = n / 32;
                push[0] = (uint)nb;
                _gpu.Record(_kQ80[(int)mode], q0, (uint[])push.Clone(), Groups(nb, 8), 1, 1, !q8k);
            }
            if (q8k)
            {
                int nb = n / CpuRef.QK_K;
                push[0] = (uint)nb;
                _gpu.Record(_kQ8K[(int)mode], qk, (uint[])push.Clone(), (uint)nb, 1, 1);
            }
        }

        // Uses the activations last quantized by Quant().
        void MatVec(GW w, GpuBuffer y)
        {
            MatVec(w, y, true);
        }

        void MatVec(GW w, GpuBuffer y, bool barrierAfter)
        {
            MatVec(w, y, barrierAfter, false);
        }

        // addInto: y += W·x (fused residual add) instead of y = W·x.
        void MatVec(GW w, GpuBuffer y, bool barrierAfter, bool addInto)
        {
            uint gx = (uint)Math.Min(w.Rows, 65535), gy = (uint)((w.Rows + gx - 1) / gx);
            uint[] push = new uint[] { (uint)w.Rows, (uint)w.Nb, gx, addInto ? 1u : 0u };
            switch (w.Type)
            {
                case GgmlType.Q8_0: _gpu.Record(_kMatVec, new GpuBuffer[] { w.A, w.B, _xq, _xd, y }, push, gx, gy, 1, barrierAfter); return;
                case GgmlType.Q4_K: _gpu.Record(_kMvQ4K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1, barrierAfter); return;
                case GgmlType.Q5_K: _gpu.Record(_kMvQ5K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1, barrierAfter); return;
                case GgmlType.Q6_K: _gpu.Record(_kMvQ6K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1, barrierAfter); return;
            }
            GpuKernel kiq;
            if (_kMvIQ.TryGetValue(w.Type, out kiq))
            {
                _gpu.Record(kiq, new GpuBuffer[] { w.A, w.B, _kq, _kd, y, _iqTables }, push, gx, gy, 1, barrierAfter);
                return;
            }
            throw new NotSupportedException(GgmlTypes.Name(w.Type));
        }

        void Add(GpuBuffer x, GpuBuffer y, int n)
        {
            _gpu.Record(_kAdd, new GpuBuffer[] { x, y }, new uint[] { (uint)n }, Groups(n, Kernels.Wg), 1, 1);
        }

        // Diagnostics: when >= 0, Forward stops after this many layers and returns the residual stream instead of logits.
        public int DebugLayers = -1;

        // Wide decode attention (n and chunk count come from the params buffer). With indirect = true the two
        // n-dependent grids come from the indirect-args buffer, so the recording does not depend on the position.
        void Attention(Layer l, int n, float scale, bool indirect)
        {
            uint group = (uint)(NHead / NHeadKv);
            if (_kAttnScores != null)
            {
                uint chunks = (uint)((n + KernelsAttention.Chunk - 1) / KernelsAttention.Chunk);
                GpuBuffer[] sb = { _q, l.KCache, _scores, _params };
                uint[] sp = { (uint)HeadDim, (uint)_kvDim, (uint)MaxCtx, Bits(scale), (uint)Ring };
                if (indirect) _gpu.RecordIndirect(_kAttnScores, sb, sp, _indirect, 0);
                else _gpu.Record(_kAttnScores, sb, sp, Groups(n, KernelsAttention.KeysPerGroup), (uint)NHeadKv, 1);
                _gpu.Record(_kAttnSoftmax, new GpuBuffer[] { _scores, _params }, new uint[] { (uint)MaxCtx }, (uint)NHead, 1, 1);
                GpuBuffer[] ob = { _scores, l.VCache, _attnPart, _params };
                uint[] op = { (uint)HeadDim, (uint)_kvDim, (uint)MaxCtx, (uint)NHead, (uint)Ring };
                if (indirect) _gpu.RecordIndirect(_kAttnOut, ob, op, _indirect, 12);
                else _gpu.Record(_kAttnOut, ob, op, chunks, (uint)NHeadKv, 1);
                _gpu.Record(_kAttnReduce, new GpuBuffer[] { _attnPart, _attn, _params }, new uint[] { (uint)(NHead * HeadDim) }, Groups(NHead * HeadDim, Kernels.Wg), 1, 1);
                return;
            }
            _gpu.Record(_kAttn, new GpuBuffer[] { _q, l.KCache, l.VCache, _scores, _attn },
                new uint[] { (uint)n, (uint)HeadDim, (uint)_kvDim, group, (uint)MaxCtx, Bits(scale) }, (uint)NHead, 1, 1);
        }

        // Run one token at position pos; returns logits (shared buffer, valid until the next call).
        public float[] Forward(int token, int pos)
        {
            if (pos >= MaxCtx) throw new InvalidOperationException("Context full.");
            CpuRef.GetRow(_tokEmbd, token, _xHost);
            _x.Upload(_xHost, 0);
            Config.RopeTable(pos, _ropeFreqs, _csHost);
            _cs.Upload(_csHost, 0);

            int n = pos + 1;
            uint chunks = (uint)((n + KernelsAttention.Chunk - 1) / KernelsAttention.Chunk);
            _paramHost[0] = (uint)pos; _paramHost[1] = (uint)n; _paramHost[2] = chunks; _paramHost[3] = Bits(Config.AttnTemperature(pos)); _paramHost[4] = 0;
            _params.UploadWords(_paramHost);
            _indirectHost[0] = Groups(n, KernelsAttention.KeysPerGroup); _indirectHost[1] = (uint)NHeadKv; _indirectHost[2] = 1;
            _indirectHost[3] = chunks; _indirectHost[4] = (uint)NHeadKv; _indirectHost[5] = 1;
            _indirect.UploadWords(_indirectHost);

            bool replay = _kAttnScores != null && !_gpu.Profiling && DebugLayers < 0;
            if (replay)
            {
                if (_graph == IntPtr.Zero)
                {
                    IntPtr g = _gpu.AllocateCommandBuffer();
                    _gpu.BeginReusable(g);
                    RecordPass(pos, true);
                    _gpu.EndReusable();
                    _graph = g;
                }
                _gpu.SubmitReusable(_graph);
            }
            else
            {
                _gpu.Begin();
                RecordPass(pos, false);
                _gpu.SubmitAndWait();
            }

            if (DebugLayers >= 0)
            {
                float[] hidden = new float[NEmbd];
                _x.Download(hidden, 0);
                return hidden;
            }
            _logits.Download(_logitsHost, 0);
            return _logitsHost;
        }

        // Records one decode step. Position-dependent values come from _params / _indirect (or pos when not replaying).
        void RecordPass(int pos, bool indirect)
        {
            int qDim = NHead * HeadDim;
            float scale = 1.0f / (float)Math.Sqrt(HeadDim);
            uint halfRot = (uint)(NRot / 2);
            int nRun = DebugLayers >= 0 ? Math.Min(DebugLayers, NLayer) : NLayer;
            for (int il = 0; il < nRun; il++)
            {
                Layer l = _layers[il];

                Quant(KernelsQuant.Mode.RmsNorm, _x, l.AttnNorm, NEmbd, l.Wq, l.Wk, l.Wv);
                MatVec(l.Wq, _q, false);   // Q, K, V are independent: let them overlap
                MatVec(l.Wk, _k, false);
                MatVec(l.Wv, _v);
                if (_kRopeP != null)
                {
                    _gpu.Record(_kRopeP, new GpuBuffer[] { _q, _cs, _params }, new uint[] { (uint)NHead, (uint)HeadDim, halfRot, 1 }, Groups(NHead * (int)halfRot, Kernels.Wg), 1, 1, false);
                    _gpu.Record(_kRopeP, new GpuBuffer[] { _k, _cs, _params }, new uint[] { (uint)NHeadKv, (uint)HeadDim, halfRot, 0 }, Groups(NHeadKv * (int)halfRot, Kernels.Wg), 1, 1);
                    _gpu.Record(_kKvT, new GpuBuffer[] { _k, _v, l.KCache, l.VCache, _params }, new uint[] { (uint)_kvDim, (uint)Ring }, Groups(KvStoreGroups(_kvDim), Kernels.Wg), 1, 1);
                }
                else
                {
                    _gpu.Record(_kRope, new GpuBuffer[] { _q, _cs }, new uint[] { (uint)NHead, (uint)HeadDim, halfRot, Bits(Config.AttnTemperature(pos)) }, Groups(NHead * (int)halfRot, Kernels.Wg), 1, 1, false);
                    _gpu.Record(_kRope, new GpuBuffer[] { _k, _cs }, new uint[] { (uint)NHeadKv, (uint)HeadDim, halfRot, Bits(1f) }, Groups(NHeadKv * (int)halfRot, Kernels.Wg), 1, 1);
                    _gpu.Record(_kKv, new GpuBuffer[] { _k, _v, l.KCache, l.VCache }, new uint[] { (uint)_kvDim, (uint)pos }, Groups(_kvDim, Kernels.Wg), 1, 1);
                }
                Attention(l, pos + 1, scale, indirect);
                Quant(_attn, qDim, l.Wo);
                MatVec(l.Wo, _x, true, true);             // x += Wo·attn

                Quant(KernelsQuant.Mode.RmsNorm, _x, l.FfnNorm, NEmbd, l.Gate, l.Up);
                MatVec(l.Gate, _g, false); // gate and up overlap
                MatVec(l.Up, _u);
                Quant(KernelsQuant.Mode.SiluMul, _g, _u, NFf, l.Down);
                MatVec(l.Down, _x, true, true);           // x += Down·(silu(gate)·up)
            }
            if (DebugLayers >= 0) return;
            Quant(KernelsQuant.Mode.RmsNorm, _x, _outputNorm, NEmbd, _output);
            MatVec(_output, _logits);
        }

        // Starts a new sequence. The KV cache needs no clearing: attention only reads positions < pos + 1.
        public void Reset() { }
        public int MinReuse(int fed) { return 0; }

        // Feeds tokens[start .. start+count) at positions start.. ; returns the logits after the last one. Chunks of up to
        // PrefillChunk tokens go through the batched kernels (KernelsPrefillK) when every matrix is Q4_K / Q6_K; a short
        // tail (< 8 tokens) and other models use the decode pass.
        public float[] Prefill(IList<int> tokens, int start, int count)
        {
            float[] logits = null;
            int i = start, end = start + count;
            if (UsePrefill && PrefillSupported())
            {
                // Chunk buffers (~100 MB) are allocated on first use; with no VRAM left the model prefills token by token.
                try { EnsurePrefill(); }
                catch (Exception) { _prefillState = -1; }
                while (_prefillState > 0 && end - i >= 8)
                {
                    int T = Math.Min(PrefillChunk, end - i);
                    logits = PrefillChunkRun(tokens, i, T);
                    i += T;
                }
            }
            for (; i < end; i++) logits = Forward(tokens[i], i);
            return logits;
        }

        // ---- batched prefill ----
        public static int PrefillChunk = 512;   // tokens per chunk
        public static bool UsePrefill = true;
        const uint MmTok = 16;                  // tokens per matmul workgroup
        const int LayersPerSubmit = 8;          // short GPU jobs (no driver timeout on long prompts)
        GpuKernel _kMmQ4K, _kMmQ6K, _kRowNorm, _kRopeRows, _kKvRows, _kAttnPre, _kCopyRange;
        bool _flash;
        uint _flashTq, _flashKc;
        GpuBuffer _pX, _pXb, _pQ, _pK, _pV, _pAtt, _pG, _pU, _pKq, _pKd, _pCs, _pUp;
        int _prefillState;   // 0 unknown, 1 supported, -1 not

        bool PrefillSupported()
        {
            if (_prefillState != 0) return _prefillState > 0;
            bool ok = _gpu.IntegerDot && _kAttnScores != null && Config.AttnTempScale == 0 && NRot == HeadDim && DebugLayers < 0;
            if (ok)
                foreach (Layer l in _layers)
                    foreach (GW w in new GW[] { l.Wq, l.Wk, l.Wv, l.Wo, l.Gate, l.Up, l.Down })
                        if (w.Type != GgmlType.Q4_K && w.Type != GgmlType.Q6_K) ok = false;
            _prefillState = ok ? 1 : -1;
            return ok;
        }

        void EnsurePrefill()
        {
            if (_pUp != null) return;   // the last allocation: everything before it exists
            _kMmQ4K = Kernel(KernelsPrefillK.MatMulK(GgmlType.Q4_K, MmTok), 4, 4, "pf matmul q4_K");
            _kMmQ6K = Kernel(KernelsPrefillK.MatMulK(GgmlType.Q6_K, MmTok), 4, 4, "pf matmul q6_K");
            _kRowNorm = Kernel(KernelsBatch.RowNorm(), 3, 4, "pf row norm");
            _kRopeRows = Kernel(KernelsPrefillK.RopeRows(), 2, 4, "pf rope");
            _kKvRows = Kernel(KernelsKv.StoreRows(Kv, KvV), 4, 5, "pf kv store " + KernelsKv.Name(Kv) + "/" + KernelsKv.Name(KvV));
            uint hdMax = ((uint)HeadDim + 31) & ~31u;
            KernelsFlash.Tile(hdMax, _gpu.MaxSharedMemory, out _flashTq, out _flashKc);
            _flash = GemmaGpu.UseFlash && _flashTq == 16 && (_gpu.MaxSharedMemory == 0 || KernelsFlash.SharedBytes(hdMax, _flashTq, _flashKc) <= _gpu.MaxSharedMemory);
            _kAttnPre = _flash ? Kernel(KernelsFlash.Attention(Kv, KvV, hdMax, _flashTq, _flashKc), 4, 9, "pf attention (tiled)") : Kernel(KernelsPrefill.Attention(Kv, KvV), 4, 8, "pf attention");
            _kCopyRange = Kernel(KernelsPrefill.CopyRange(), 2, 3, "pf copy");
            ulong T = (ulong)PrefillChunk, d = (ulong)NEmbd, qDim = (ulong)(NHead * HeadDim);
            ulong maxIn = (ulong)Math.Max(Math.Max(NEmbd, NHead * HeadDim), NFf);
            _pX = Buf(T * d * 4, false); _pXb = Buf(T * d * 4, false);
            _pQ = Buf(T * qDim * 4, false); _pK = Buf(T * (ulong)_kvDim * 4, false); _pV = Buf(T * (ulong)_kvDim * 4, false);
            _pAtt = Buf(T * qDim * 4, false);
            _pG = Buf(T * (ulong)NFf * 4, false); _pU = Buf(T * (ulong)NFf * 4, false);
            _pKq = Buf(T * maxIn, false); _pKd = Buf(T * (maxIn / 256) * 4, false);
            _pCs = Buf(T * (ulong)NRot * 4, BufferKind.Upload);
            _pUp = Buf(T * d * 4, BufferKind.Upload);
        }

        // Q8_K-quantizes n values of x (T rows back to back); SiluMul quantizes silu(x) * aux.
        void RowQuantK(KernelsQuant.Mode mode, GpuBuffer x, GpuBuffer aux, int n)
        {
            int nb = n / CpuRef.QK_K;
            GpuBuffer[] b = mode == KernelsQuant.Mode.Plain ? new GpuBuffer[] { x, _pKq, _pKd } : new GpuBuffer[] { x, _pKq, _pKd, aux };
            _gpu.Record(_kQ8K[(int)mode], b, new uint[] { (uint)nb, (uint)n, Bits(Eps) }, (uint)nb, 1, 1);
        }

        void Mm(GW w, GpuBuffer y, int T, bool addTo, bool barrierAfter)
        {
            _gpu.Record(w.Type == GgmlType.Q4_K ? _kMmQ4K : _kMmQ6K, new GpuBuffer[] { w.A, _pKq, _pKd, y },
                new uint[] { (uint)w.Rows, (uint)w.Nb, (uint)T, addTo ? 1u : 0u }, Groups(w.Rows, KernelsPrefillK.Threads), Groups(T, MmTok), 1, barrierAfter);
        }

        float[] PrefillChunkRun(IList<int> tokens, int p0, int T)
        {
            if (p0 + T > MaxCtx) throw new InvalidOperationException("Context full.");
            EnsurePrefill();
            int d = NEmbd, qDim = NHead * HeadDim;
            float[] host = new float[T * d], row = new float[d], cs = new float[T * NRot], one = new float[NRot];
            for (int t = 0; t < T; t++)
            {
                CpuRef.GetRow(_tokEmbd, tokens[p0 + t], row);
                Array.Copy(row, 0, host, t * d, d);
                Config.RopeTable(p0 + t, _ropeFreqs, one);
                Array.Copy(one, 0, cs, t * NRot, NRot);
            }
            _pUp.Upload(host, 0);
            _pCs.Upload(cs, 0);
            uint eps = Bits(Eps), half = (uint)(NRot / 2);
            float scale = 1.0f / (float)Math.Sqrt(HeadDim);

            _gpu.Begin();
            _gpu.Record(_kCopyRange, new GpuBuffer[] { _pUp, _pX }, new uint[] { (uint)(T * d), 0, 0 }, Groups(T * d, Kernels.Wg), 1, 1);
            for (int il = 0; il < NLayer; il++)
            {
                Layer l = _layers[il];
                _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, _pXb, l.AttnNorm }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)T, 1, 1);
                RowQuantK(KernelsQuant.Mode.Plain, _pXb, null, T * d);
                Mm(l.Wq, _pQ, T, false, false);
                Mm(l.Wk, _pK, T, false, false);
                Mm(l.Wv, _pV, T, false, true);
                _gpu.Record(_kRopeRows, new GpuBuffer[] { _pQ, _pCs }, new uint[] { (uint)NHead, (uint)HeadDim, half, (uint)T }, Groups(T * NHead * (int)half, Kernels.Wg), 1, 1, false);
                _gpu.Record(_kRopeRows, new GpuBuffer[] { _pK, _pCs }, new uint[] { (uint)NHeadKv, (uint)HeadDim, half, (uint)T }, Groups(T * NHeadKv * (int)half, Kernels.Wg), 1, 1);
                for (uint par = 0; par < KernelsKv.Passes(Kv); par++)
                    _gpu.Record(_kKvRows, new GpuBuffer[] { _pK, _pV, l.KCache, l.VCache }, new uint[] { (uint)_kvDim, (uint)Ring, (uint)p0, (uint)T, par }, Groups(T * KvStoreGroups(_kvDim), Kernels.Wg), 1, 1);
                _gpu.Record(_kAttnPre, new GpuBuffer[] { _pQ, l.KCache, l.VCache, _pAtt },
                    _flash ? new uint[] { (uint)HeadDim, (uint)_kvDim, (uint)Ring, (uint)NHead, (uint)NHeadKv, (uint)p0, 0u, Bits(scale), (uint)T }
                           : new uint[] { (uint)HeadDim, (uint)_kvDim, (uint)Ring, (uint)NHead, (uint)NHeadKv, (uint)p0, 0u, Bits(scale) },
                    _flash ? Groups(T, _flashTq) : (uint)T, (uint)NHead, 1);
                RowQuantK(KernelsQuant.Mode.Plain, _pAtt, null, T * qDim);
                Mm(l.Wo, _pX, T, true, true);                      // x += Wo·attn

                _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, _pXb, l.FfnNorm }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)T, 1, 1);
                RowQuantK(KernelsQuant.Mode.Plain, _pXb, null, T * d);
                Mm(l.Gate, _pG, T, false, false);
                Mm(l.Up, _pU, T, false, true);
                RowQuantK(KernelsQuant.Mode.SiluMul, _pG, _pU, T * NFf);
                Mm(l.Down, _pX, T, true, true);                    // x += Down·(silu(gate)·up)

                if ((il + 1) % LayersPerSubmit == 0 && il + 1 < NLayer) { _gpu.SubmitAndWait(); _gpu.Begin(); }
            }
            // The last token's hidden state goes to the decode buffer for the output head (every row's for PrefillAll).
            int first = _allRows ? 0 : T - 1;
            for (int r = first; r < T; r++)
            {
                _gpu.Record(_kCopyRange, new GpuBuffer[] { _pX, _x }, new uint[] { (uint)d, (uint)(r * d), 0 }, Groups(d, Kernels.Wg), 1, 1);
                Quant(KernelsQuant.Mode.RmsNorm, _x, _outputNorm, NEmbd, _output);
                MatVec(_output, _allRows ? _lgAll[r] : _logits);
            }
            _gpu.SubmitAndWait();
            if (_allRows) return null;
            _logits.Download(_logitsHost, 0);
            return _logitsHost;
        }

        // ---- speculative verification ----
        bool _allRows;
        GpuBuffer[] _lgAll;

        public float[][] PrefillAll(IList<int> tokens, int start, int count)
        {
            if (!UsePrefill || !PrefillSupported() || count < 1 || count > 16 || start + count > MaxCtx) return null;
            try { EnsurePrefill(); } catch (Exception) { _prefillState = -1; return null; }
            if (_lgAll == null) { _lgAll = new GpuBuffer[16]; for (int i = 0; i < 16; i++) _lgAll[i] = Buf((ulong)NVocab * 4, true); }
            _allRows = true;
            try { PrefillChunkRun(tokens, start, count); }
            finally { _allRows = false; }
            float[][] r = new float[count][];
            for (int i = 0; i < count; i++) { r[i] = new float[NVocab]; _lgAll[i].Download(r[i], 0); }
            return r;
        }

        // Instant Recall is implemented for GemmaGpu (fp16 cache) only.
        public bool CanRecall { get { return false; } }
        public KvSnapshot CaptureKv(int pos0, int count) { throw new NotSupportedException(); }
        public void SpliceKv(KvSnapshot s, int dst0) { throw new NotSupportedException(); }

        public int ContextLength { get { return MaxCtx; } }
        public int VocabSize { get { return NVocab; } }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
            _owned.Clear();
        }
    }
}
