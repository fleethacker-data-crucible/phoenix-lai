// Gemma 4 (text) forward pass on the GPU through Vulkan compute. C# 5 only.
// Same graph and rounding points as GemmaCpu, which is its ground truth. Weights: Q4_0 / Q8_0 / Q4_K-Q6_K.
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public static class GemmaGpuFactory
    {
        public static ILanguageModel Create(GpuDevice gpu, GgufFile g, int maxCtx) { return new GemmaGpu(gpu, g, maxCtx); }
    }

    public sealed unsafe class GemmaGpu : ILanguageModel
    {
        public readonly GemmaConfig C;
        public readonly int MaxCtx;
        public long WeightBytes, KvBytes;
        bool _prefillOk = true;   // batched prefill needs every layer weight in Q4_0 (Gemma QAT); otherwise token by token

        // Stride: blocks per stored row (Q4_0 rows carry RowPad extra blocks; see UploadBlocks).
        sealed class GW { public GgmlType Type; public GpuBuffer A, B; public int Rows, Cols, Nb, Stride; }

        // Q4_0 rows are padded by 2 blocks (32 bytes): with rows a power-of-two number of bytes apart (e.g. 4096 or
        // 8192 columns = 2048 / 4096 bytes), every thread of a workgroup hits the same memory channel and the prefill
        // matmul runs at a third of its speed (RX 6700 XT: attn_output 2.1 -> 6.7, ffn_down 3.6 -> 7.1 TMAC/s).
        const int RowPad = 2;

        sealed class Layer
        {
            public GpuBuffer AttnNorm, QNorm, KNorm, PostAttnNorm, FfnNorm, PostFfnNorm, PlePostNorm, KCache, VCache;
            public GW Wq, Wk, Wv, Wo, Gate, Up, Down, PleGate, PleProj;
            public float OutScale = 1f;
            public int HeadDim, NHeadKv, KvDim, NFf, NRot, Ring;
            public bool Swa;
            public bool Shared;   // E2B/E4B: no K/V of its own; KCache/VCache/Ring are the source layer's (C.KvSrc)
            // mixture of experts (26B A4B): router (f32), fused gate|up and down experts (all experts, rows stacked), norms
            public GpuBuffer GateInp, GateInpT, GateInpS, DownScale, PreFfwNorm2, PostFfwNorm1, PostFfwNorm2;   // GateInpT: [n][nExp] (prefill)
            public GW GateUpExp, DownExp;
        }

        // E2B/E4B per-layer inputs: the f16 model projection on the GPU, the token rows (Q6_K, ~2 GB) stay mapped on the CPU.
        readonly Weight _pleTok;
        readonly GpuBuffer _pleProjW, _pleProjNorm, _pleTokUp, _pleProjOut, _pleAll;
        readonly float[] _pleTokHost;
        GpuKernel _kMmF16, _kPle, _kCopy;
        GpuBuffer _pPleTok, _pPleProj, _pPle;
        int Pl { get { return C.NEmbdPerLayer; } }

        // Sliding-window layers keep a ring of the last NSwa + SwaSlack positions instead of the whole context (all caches
        // are fp16, packed): Gemma 4 12B at 32K context needs ~1.4 GB of cache instead of ~11 GB. The slack lets the chat
        // engine keep its cache when a new prompt rewinds up to SwaSlack tokens (MinReuse); a longer rewind re-reads it.
        // 4096 (was 512): each message's docs and project sections are dropped from the history on the next turn, so the
        // next prompt rewinds to the start of the previous message (~2-3K tokens back). With 512 that re-read the whole
        // conversation every turn on Gemma 12B (24-74 s, 2026-10-17); 4096 costs it ~1.1 GB of VRAM (320 KB per position
        // over its sliding-window layers; E2B/E4B 12-40 MB per 1024), which ContextPlanner takes out of the auto context.
        // Static (not const) so tests can force the ring to wrap early.
        public static int SwaSlack = 512;   // set per load by ChatEngine from ContextPlanner.ChooseSlack (512..4096)

        // Per-kind (sliding / global) state: rope table, attention params, indirect grids, attention kernels.
        sealed class Kind
        {
            public int HeadDim, NHeadKv, NRot;
            public float RopeBase;
            public float* RopeFreqs;
            public GpuBuffer Cs, Params, Indirect;
            public float[] CsHost;
            public uint[] ParamHost = new uint[KernelsAttention.ParamWords], IndirectHost = new uint[6];
            public GpuKernel Scores, Out;
        }

        readonly GpuDevice _gpu;
        readonly Weight _tokEmbd;
        readonly GW _output;
        readonly GpuBuffer _outputNorm, _one;
        readonly Layer[] _layers;
        readonly Kind _swa, _global;
        readonly List<IDisposable> _owned = new List<IDisposable>();
        readonly float _embdScale;

        readonly GpuKernel[] _kQ80 = new GpuKernel[4], _kQ8K = new GpuKernel[4];
        readonly GpuKernel _kMvQ8, _kMvQ4, _kMvQ4K, _kMvQ5K, _kMvQ6K, _kRope, _kHeadNorm, _kNormAdd, _kNormAddQ, _kSoftcap, _kKvT, _kSoftmax, _kReduce;
        IntPtr _graph;
        GpuKernel _kRouterLogits, _kRouterSelect, _kMoeGateUp, _kMoeDown, _kNorm2;
        GpuBuffer _route, _rlogits, _mlp, _moe, _xq2, _xd2, _g2, _u2;

        // Batched prefill (KernelsPrefill): chunk buffers, allocated on first use.
        int _chunkCap;                               // largest chunk: PrefillChunk, or MoePrefillChunk for the 26B
        int _chunk;                                  // current chunk size (adaptive, see Prefill)
        public static int MoePrefillChunk = 2048;    // experts read every expert once per chunk: 512 -> 1,040, 2048 -> 1,200 tok/s (8060S, pp2048)
        const int ChunkMin = 32;
        const double ChunkSlowMs = 400, ChunkFastMs = 150;
        public static int PrefillChunk = 512;       // tokens per chunk (larger chunks keep the GPU busier: 64 -> 241, 512 -> 328 tok/s)
        const int LayersPerSubmit = 12;              // GPU submissions stay far below Windows' ~2 s timeout
        int SubmitLayers { get { return _gpu.LayersPerSubmit(C.Moe ? 6 : LayersPerSubmit); } }   // 26B: 2048-token chunks, half the layers per submit
        public static bool UsePrefill = true;
        public static uint MmTok = 24;               // tokens per prefill matmul tile (RX 6700 XT sweep: 16 6.0, 24 7.4, 32 6.6 TMAC/s)
        public static uint MmRpt = 1;                // rows per thread of the prefill matmul tile (64 * MmRpt rows per workgroup)
        GpuKernel _kMm, _kMmAny, _kKvRows, _kAttnPre, _kCopyRange, _kRowNorm, _kRopeRows, _kCopyRows;
        public static bool UseFlash = true;
        bool _flash;
        readonly Dictionary<uint, GpuKernel> _kFlash = new Dictionary<uint, GpuKernel>();
        readonly Dictionary<uint, uint> _flashTq = new Dictionary<uint, uint>();
        public static bool UseFlash2 = true;          // register-blocked, KV-head-shared tiles (KernelsFlash2; fp16 KV only)
        readonly Dictionary<uint, GpuKernel> _kFlash2 = new Dictionary<uint, GpuKernel>();
        GpuBuffer _pX, _pXb, _pTmp, _pQ, _pK, _pV, _pAtt, _pG, _pU, _pXq, _pXd, _pCsSwa, _pCsGlobal, _pUp;
        int _maxQ, _maxKv, _maxFf;
        readonly GpuBuffer _x, _xb, _q, _k, _v, _attn, _g, _u, _xq, _xd, _kq, _kd, _scores, _attnPart, _logits;
        readonly float[] _xHost, _logitsHost;

        public readonly KvType Kv, KvV;   // K / V cache formats   // KV cache format (fp16, or 8 / 4-bit with KernelsKv)
        int KvStoreGroups(int values) { return Kv == KvType.F16 && KvV == KvType.F16 ? values / 2 : values / 64; }

        public GemmaGpu(GpuDevice gpu, GgufFile g, int maxCtx) : this(gpu, g, maxCtx, KvType.F16, KvType.F16) { }
        public GemmaGpu(GpuDevice gpu, GgufFile g, int maxCtx, KvType kv) : this(gpu, g, maxCtx, kv, kv) { }

        bool _locked;   // holds ModelLock (released in Dispose)

        public GemmaGpu(GpuDevice gpu, GgufFile g, int maxCtx, KvType kv, KvType kvV)
        {
            ModelLock.Check();   // one loaded model per machine: refuse before allocating anything
            _gpu = gpu;
            Kv = kv; KvV = kvV;
            C = GemmaConfig.FromGguf(g);
            _chunkCap = C.Moe ? Math.Max(PrefillChunk, MoePrefillChunk) : PrefillChunk;
            _chunk = _chunkCap;
            if (C.Embedding) throw new NotSupportedException("EmbeddingGemma runs on the CPU.");
            MaxCtx = maxCtx;
            _embdScale = (float)Math.Sqrt(C.NEmbd);
            try
            {
                foreach (KernelsQuant.Mode md in new KernelsQuant.Mode[] { KernelsQuant.Mode.Plain, KernelsQuant.Mode.GeluMul, KernelsQuant.Mode.RmsNorm })
                {
                    _kQ80[(int)md] = Kernel(KernelsQuant.Q80(md, gpu.SubgroupArithmetic), KernelsQuant.Bindings(md), KernelsQuant.PushWords, "quant q8_0 " + md);
                    _kQ8K[(int)md] = Kernel(KernelsQuant.Q8K(md, gpu.SubgroupArithmetic), KernelsQuant.Bindings(md), KernelsQuant.PushWords, "quant q8_K " + md);
                }
                _kMvQ8 = Kernel(Kernels.MatVecQ8(gpu.IntegerDot), 5, 4, "matvec q8_0");
                _kMvQ4 = Kernel(KernelsGemma.MatVecQ4_0(gpu.IntegerDot, gpu.SubgroupArithmetic), 5, 5, "matvec q4_0");
                _kMvQ4K = Kernel(KernelsK.MatVec(GgmlType.Q4_K, gpu.SubgroupArithmetic, gpu.IntegerDot), 4, 4, "matvec q4_K");
                _kMvQ5K = Kernel(KernelsK.MatVec(GgmlType.Q5_K, gpu.SubgroupArithmetic, gpu.IntegerDot), 4, 4, "matvec q5_K");
                _kMvQ6K = Kernel(KernelsK.MatVec(GgmlType.Q6_K, gpu.SubgroupArithmetic, gpu.IntegerDot), 4, 4, "matvec q6_K");
                _kRope = Kernel(KernelsGemma.RopeNeox(), 2, 3, "rope neox");
                _kHeadNorm = Kernel(KernelsGemma.HeadNorm(), 3, 3, "head norm");
                _kNormAdd = Kernel(KernelsGemma.NormAdd(), 3, 3, "norm add");
                _kNormAddQ = Kernel(KernelsGemma.NormAddQuant(gpu.SubgroupArithmetic), 6, 3, "norm add + quant");
                _kSoftcap = Kernel(KernelsGemma.Softcap(), 1, 2, "softcap");
                _kKvT = Kernel(KernelsKv.Store(Kv, KvV), 5, 2, "kv store " + KernelsKv.Name(Kv) + "/" + KernelsKv.Name(KvV));
                _kSoftmax = Kernel(KernelsAttention.Softmax(), 2, 1, "attn softmax");
                _kReduce = Kernel(KernelsAttention.OutputReduce(), 3, 1, "attn reduce");

                _tokEmbd = new Weight(T(g, "token_embd.weight"));
                C.NVocab = _tokEmbd.Rows;
                _output = UploadW(g.TensorsByName.ContainsKey("output.weight") ? T(g, "output.weight") : T(g, "token_embd.weight"));
                _outputNorm = UploadF32(T(g, "output_norm.weight"));
                float* ropeFreqs = g.TensorsByName.ContainsKey("rope_freqs.weight") ? (float*)T(g, "rope_freqs.weight").Data : null;

                _layers = new Layer[C.NLayer];
                int maxQ = 0, maxKv = 0, maxFf = 0;
                for (int i = 0; i < C.NLayer; i++)
                {
                    string p = "blk." + i + ".";
                    Layer l = new Layer();
                    l.Swa = C.Swa[i]; l.HeadDim = C.HeadDim[i]; l.NHeadKv = C.NHeadKv[i]; l.KvDim = l.NHeadKv * l.HeadDim;
                    l.NFf = C.NFf[i]; l.NRot = C.NRot[i]; l.Shared = C.KvSrc[i] != i;
                    l.AttnNorm = UploadF32(T(g, p + "attn_norm.weight"));
                    l.QNorm = UploadF32(T(g, p + "attn_q_norm.weight"));
                    l.KNorm = l.Shared ? null : UploadF32(T(g, p + "attn_k_norm.weight"));
                    l.PostAttnNorm = UploadF32(T(g, p + "post_attention_norm.weight"));
                    l.FfnNorm = UploadF32(T(g, p + "ffn_norm.weight"));
                    l.PostFfnNorm = UploadF32(T(g, p + "post_ffw_norm.weight"));
                    l.Wq = UploadW(T(g, p + "attn_q.weight"));
                    l.Wk = l.Shared ? null : UploadW(T(g, p + "attn_k.weight"));
                    l.Wv = !l.Shared && g.TensorsByName.ContainsKey(p + "attn_v.weight") ? UploadW(T(g, p + "attn_v.weight")) : null;
                    l.Wo = UploadW(T(g, p + "attn_output.weight"));
                    l.Gate = UploadW(T(g, p + "ffn_gate.weight"));
                    l.Up = UploadW(T(g, p + "ffn_up.weight"));
                    l.Down = UploadW(T(g, p + "ffn_down.weight"));
                    if (g.TensorsByName.ContainsKey(p + "layer_output_scale.weight")) l.OutScale = *(float*)T(g, p + "layer_output_scale.weight").Data;
                    if (C.Moe)
                    {
                        l.GateInp = UploadF32(T(g, p + "ffn_gate_inp.weight"));
                        l.GateInpT = UploadTransposedF32(T(g, p + "ffn_gate_inp.weight"));
                        l.GateInpS = UploadF32(T(g, p + "ffn_gate_inp.scale"));
                        l.DownScale = UploadF32(T(g, p + "ffn_down_exps.scale"));
                        l.PreFfwNorm2 = UploadF32(T(g, p + "pre_ffw_norm_2.weight"));
                        l.PostFfwNorm1 = UploadF32(T(g, p + "post_ffw_norm_1.weight"));
                        l.PostFfwNorm2 = UploadF32(T(g, p + "post_ffw_norm_2.weight"));
                        l.GateUpExp = UploadW(T(g, p + "ffn_gate_up_exps.weight"));
                        l.DownExp = UploadW(T(g, p + "ffn_down_exps.weight"));
                        if (l.GateUpExp.Type != GgmlType.Q4_0 || l.DownExp.Type != GgmlType.Q4_0)
                            throw new NotSupportedException("Mixture-of-experts weights must be q4_0 on the GPU.");
                        maxFf = Math.Max(maxFf, C.NExpertUsed * C.NFfExp);
                    }
                    if (C.NEmbdPerLayer > 0)
                    {
                        l.PleGate = UploadW(T(g, p + "inp_gate.weight"));
                        l.PleProj = UploadW(T(g, p + "proj.weight"));
                        l.PlePostNorm = UploadF32(T(g, p + "post_norm.weight"));
                    }
                    if (l.Shared)
                    {
                        Layer src = _layers[C.KvSrc[i]];   // an earlier layer: its cache already holds each position
                        l.Ring = src.Ring; l.KCache = src.KCache; l.VCache = src.VCache;
                    }
                    else
                    {
                        l.Ring = (Math.Min(maxCtx, l.Swa ? C.NSwa + SwaSlack : maxCtx) + 7) & ~7;   // a multiple of 8 (q4 words)
                        ulong kb = KernelsKv.CacheBytes(Kv, l.Ring, l.KvDim), vb = KernelsKv.CacheBytes(KvV, l.Ring, l.KvDim);
                        l.KCache = Buf(kb, BufferKind.Device);
                        l.VCache = Buf(vb, BufferKind.Device);
                        KvBytes += (long)(kb + vb);
                    }
                    maxQ = Math.Max(maxQ, C.NHead * l.HeadDim); maxKv = Math.Max(maxKv, l.KvDim); maxFf = Math.Max(maxFf, l.NFf);
                    _layers[i] = l;
                    if (!Q4(l.Wq) || !Q4(l.Wk) || !Q4(l.Wv) || !Q4(l.Wo) || !Q4(l.Gate) || !Q4(l.Up) || !Q4(l.Down) || !Q4(l.PleGate) || !Q4(l.PleProj)) _prefillOk = false;
                }
                if (C.Moe)
                {
                    _kRouterLogits = Kernel(KernelsMoe.RouterLogits(gpu.SubgroupArithmetic), 4, 4, "moe router logits");
                    _kRouterSelect = Kernel(KernelsMoe.RouterSelect(), 3, 2, "moe router select");
                    _kMoeGateUp = Kernel(KernelsMoe.GateUp(gpu.IntegerDot, gpu.SubgroupArithmetic), 7, 4, "moe gate|up q4_0");
                    _kMoeDown = Kernel(KernelsMoe.Down(gpu.IntegerDot, gpu.SubgroupArithmetic), 6, 4, "moe down q4_0");
                    _kNorm2 = Kernel(KernelsMoe.Norm2(), 5, 2, "moe norm2");
                    _route = Buf(2 * KernelsMoe.MaxUsed * 4, BufferKind.Device);
                    _rlogits = Buf((ulong)C.NExpert * 4, BufferKind.Device);
                    _mlp = Buf((ulong)C.NEmbd * 4, BufferKind.Device);
                    int nIn = Math.Max(C.NEmbd, C.NExpertUsed * C.NFfExp);
                    _xq2 = Buf((ulong)nIn, BufferKind.Device); _xd2 = Buf((ulong)(nIn / 32) * 4, BufferKind.Device);
                    _g2 = Buf((ulong)(C.NExpertUsed * C.NFfExp) * 4, BufferKind.Device); _u2 = Buf((ulong)(C.NExpertUsed * C.NFfExp) * 4, BufferKind.Device);
                    _moe = Buf((ulong)C.NEmbd * 4, BufferKind.Device);
                }
                _swa = MakeKind(true, ropeFreqs);
                _global = MakeKind(false, ropeFreqs);

                int maxIn = Math.Max(Math.Max(C.NEmbd, maxQ), maxFf);
                _x = Buf((ulong)C.NEmbd * 4, BufferKind.Upload);
                _xb = Buf((ulong)C.NEmbd * 4, BufferKind.Device);
                _q = Buf((ulong)maxQ * 4, BufferKind.Device); _k = Buf((ulong)maxKv * 4, BufferKind.Device); _v = Buf((ulong)maxKv * 4, BufferKind.Device);
                _attn = Buf((ulong)maxQ * 4, BufferKind.Device);
                _g = Buf((ulong)maxFf * 4, BufferKind.Device); _u = Buf((ulong)maxFf * 4, BufferKind.Device);
                _xq = Buf((ulong)maxIn, BufferKind.Device); _xd = Buf((ulong)(maxIn / 32) * 4, BufferKind.Device);
                _kq = Buf((ulong)maxIn, BufferKind.Device); _kd = Buf((ulong)(maxIn / 256 + 1) * 4, BufferKind.Device);
                _scores = Buf((ulong)C.NHead * (ulong)maxCtx * 4, BufferKind.Device);
                _attnPart = Buf((ulong)((maxCtx + KernelsAttention.Chunk - 1) / KernelsAttention.Chunk) * (ulong)maxQ * 4, BufferKind.Device);
                _logits = Buf((ulong)C.NVocab * 4, BufferKind.Readback);
                _one = Buf(4, BufferKind.Device);
                _xHost = new float[C.NEmbd]; _logitsHost = new float[C.NVocab];
                _maxQ = maxQ; _maxKv = maxKv; _maxFf = maxFf;
                if (C.NEmbdPerLayer > 0)
                {
                    if (!g.TensorsByName.ContainsKey("per_layer_token_embd.weight")) throw new NotSupportedException("Per-layer inputs without per_layer_token_embd.");
                    int n = Pl * C.NLayer;
                    _pleTok = new Weight(T(g, "per_layer_token_embd.weight"));
                    GgufTensor pw = T(g, "per_layer_model_proj.weight");
                    if (pw.Type != GgmlType.F16) throw new NotSupportedException("per_layer_model_proj must be f16.");
                    _pleProjW = Buf((ulong)pw.SizeBytes, BufferKind.Device);
                    _gpu.Upload(_pleProjW, 0, (IntPtr)pw.Data, (ulong)pw.SizeBytes);
                    WeightBytes += pw.SizeBytes;
                    _pleProjNorm = UploadF32(T(g, "per_layer_proj_norm.weight"));
                    _pleTokUp = Buf((ulong)n * 4, BufferKind.Upload);
                    _pleProjOut = Buf((ulong)n * 4, BufferKind.Device);
                    _pleAll = Buf((ulong)n * 4, BufferKind.Device);
                    _pleTokHost = new float[n];
                    _kMmF16 = Kernel(KernelsBatch.MatMulF16(), 3, 3, "mm f16");
                    _kPle = Kernel(KernelsGemma.PleInputs(), 4, 6, "per-layer inputs");
                    _kCopy = Kernel(KernelsPrefill.CopyRange(), 2, 3, "copy range");
                }
            }
            catch { Dispose(); throw; }
            ModelLock.Acquire(g.Path); _locked = true;
        }

        Kind MakeKind(bool swa, float* ropeFreqs)
        {
            Kind kd = new Kind();
            Layer l = null;
            foreach (Layer x in _layers) if (x.Swa == swa) { l = x; break; }
            if (l == null) return null;
            kd.HeadDim = l.HeadDim; kd.NHeadKv = l.NHeadKv; kd.NRot = l.NRot;
            kd.RopeBase = swa ? C.RopeBaseSwa : C.RopeBase;
            kd.RopeFreqs = swa ? null : ropeFreqs;
            kd.Cs = Buf((ulong)kd.NRot * 4, BufferKind.Upload);
            kd.CsHost = new float[kd.NRot];
            kd.Params = Buf((ulong)KernelsAttention.ParamWords * 4, BufferKind.Upload);
            kd.Indirect = Buf(6 * 4, BufferKind.Upload);
            if (C.NHead % kd.NHeadKv != 0) throw new NotSupportedException("Query heads must be a multiple of KV heads.");
            uint group = (uint)(C.NHead / kd.NHeadKv);
            uint need = KernelsAttention.ScoresShared(group, (uint)kd.HeadDim);
            if (_gpu.MaxSharedMemory != 0 && need > _gpu.MaxSharedMemory)
                throw new NotSupportedException("This GPU has " + _gpu.MaxSharedMemory + " bytes of shared memory; Gemma attention needs " + need + ".");
            kd.Scores = Kernel(KernelsAttention.Scores(group, (uint)kd.HeadDim, Kv), 4, 5, "attn scores " + (swa ? "local" : "global"));
            kd.Out = Kernel(KernelsAttention.OutputPartial(group, KvV), 4, 5, "attn out " + (swa ? "local" : "global"));
            return kd;
        }

        static GgufTensor T(GgufFile g, string name)
        {
            GgufTensor t;
            if (!g.TensorsByName.TryGetValue(name, out t)) throw new KeyNotFoundException("Missing tensor " + name);
            return t;
        }

        GpuKernel Kernel(uint[] spirv, int bindings, int pushWords, string name)
        {
            GpuKernel k = _gpu.CreateKernel(spirv, bindings, (uint)pushWords * 4, 1024);
            k.Name = name;
            _owned.Add(k);
            return k;
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
            GpuBuffer b = Buf((ulong)t.SizeBytes, BufferKind.Device);
            _gpu.Upload(b, 0, (IntPtr)t.Data, (ulong)t.SizeBytes);
            WeightBytes += t.SizeBytes;
            return b;
        }

        // A 2-D f32 tensor [rows][cols] uploaded as [cols][rows].
        GpuBuffer UploadTransposedF32(GgufTensor t)
        {
            if (t.Type != GgmlType.F32 || t.Dims.Length != 2) throw new NotSupportedException(t.Name + " must be 2-D f32.");
            int cols = (int)t.Dims[0], rows = (int)t.Dims[1];
            float* src = (float*)t.Data;
            float[] tr = new float[(long)rows * cols];
            for (int r = 0; r < rows; r++) for (int c = 0; c < cols; c++) tr[(long)c * rows + r] = src[(long)r * cols + c];
            GpuBuffer b = Buf((ulong)t.SizeBytes, BufferKind.Device);
            fixed (float* p = tr) _gpu.Upload(b, 0, (IntPtr)p, (ulong)t.SizeBytes);
            WeightBytes += t.SizeBytes;
            return b;
        }

        GW UploadW(GgufTensor t)
        {
            switch (t.Type)
            {
                case GgmlType.Q8_0: return UploadBlocks(t, 32, CpuRef.BlockQ8_0Bytes, 32, false);
                case GgmlType.Q4_0: return UploadBlocks(t, 32, CpuRef.BlockQ4_0Bytes, 16, true);
                case GgmlType.Q4_K:
                case GgmlType.Q5_K:
                case GgmlType.Q6_K: return UploadK(t);
            }
            throw new NotSupportedException(t.Name + " is " + GgmlTypes.Name(t.Type) + "; Gemma on the GPU supports q4_0, q8_0 and q4_K-q6_K.");
        }

        // Q8_0 / Q4_0: {fp16 d; payload} blocks -> payload bytes (whole words) + scales (f32, or the original fp16 bits
        // packed two per word when halfScales).
        GW UploadBlocks(GgufTensor t, int qk, int blockBytes, int payload, bool halfScales)
        {
            Weight w = new Weight(t);
            w.Rows = (int)(t.ElementCount / w.Cols);   // 3-D expert tensors: every expert's rows, stacked
            GW q = new GW();
            q.Type = t.Type; q.Rows = w.Rows; q.Cols = w.Cols; q.Nb = w.Cols / qk;
            q.Stride = halfScales ? q.Nb + RowPad : q.Nb;   // Q4_0 (fp16 scales) padded; Q8_0 kernels expect packed rows
            long slots = (long)w.Rows * q.Stride;
            q.A = Buf((ulong)slots * (ulong)payload, BufferKind.Device);
            q.B = Buf(halfScales ? (ulong)((slots + 1) / 2) * 4 : (ulong)slots * 4, BufferKind.Device);
            int rowsPer = Math.Max(2, ((16 << 20) / (q.Stride * payload)) & ~1);   // even row count: fp16 pairs stay aligned
            byte[] pay = new byte[(long)rowsPer * q.Stride * payload];
            ushort[] hs = halfScales ? new ushort[((long)rowsPer * q.Stride + 1) & ~1] : null;
            float[] d = halfScales ? null : new float[(long)rowsPer * q.Stride];
            for (int r0 = 0; r0 < w.Rows; r0 += rowsPer)
            {
                int nr = Math.Min(rowsPer, w.Rows - r0);
                Array.Clear(pay, 0, pay.Length);
                if (hs != null) Array.Clear(hs, 0, hs.Length);
                // Repack rows in parallel (one row per task, whole-block copies): single-threaded byte copies made the
                // CPU the bottleneck of loading (Gemma 12B: 7 GB at ~0.4 GB/s).
                int rr0 = r0, nb = q.Nb, stride = q.Stride;
                byte* data = t.Data;
                fixed (byte* pp0 = pay)
                {
                    byte* pp = pp0;
                    System.Threading.Tasks.Parallel.For(0, nr, delegate(int r)
                    {
                        byte* src = data + (long)(rr0 + r) * nb * blockBytes;
                        long slot = (long)r * stride;
                        byte* dst = pp + slot * payload;
                        for (int b = 0; b < nb; b++, src += blockBytes, dst += payload, slot++)
                        {
                            if (halfScales) hs[slot] = *(ushort*)src; else d[slot] = Half.ToFloat(*(ushort*)src);
                            Buffer.MemoryCopy(src + 2, dst, payload, payload);
                        }
                    });
                }
                long n = (long)nr * q.Stride;
                fixed (byte* pp = pay) _gpu.Upload(q.A, (ulong)((long)r0 * q.Stride) * (ulong)payload, (IntPtr)pp, (ulong)n * (ulong)payload);
                if (halfScales) { fixed (ushort* ph = hs) _gpu.Upload(q.B, (ulong)((long)r0 * q.Stride) * 2, (IntPtr)ph, (ulong)((n + 1) & ~1) * 2); }
                else fixed (float* pd = d) _gpu.Upload(q.B, (ulong)((long)r0 * q.Stride) * 4, (IntPtr)pd, (ulong)n * 4);
            }
            WeightBytes += slots * (payload + (halfScales ? 2 : 4));
            return q;
        }

        GW UploadK(GgufTensor t)
        {
            Weight w = new Weight(t);
            if (w.Cols % CpuRef.QK_K != 0) throw new NotSupportedException(t.Name + ": rows must be a multiple of 256.");
            GW q = new GW();
            q.Type = t.Type; q.Rows = w.Rows; q.Cols = w.Cols; q.Nb = w.Cols / CpuRef.QK_K; q.Stride = q.Nb;
            long blocks = (long)w.Rows * q.Nb;
            if (t.Type != GgmlType.Q6_K)
            {
                q.A = Buf((ulong)t.SizeBytes, BufferKind.Device);
                _gpu.Upload(q.A, 0, (IntPtr)t.Data, (ulong)t.SizeBytes);
                WeightBytes += t.SizeBytes;
                return q;
            }
            q.A = Buf((ulong)blocks * KernelsK.Q6_KPaddedBytes, BufferKind.Device);
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

        static bool Q4(GW w) { return w == null || w.Type == GgmlType.Q4_0; }
        static uint Bits(float f) { return *(uint*)&f; }
        static uint Groups(int n, uint size) { return (uint)((n + size - 1) / size); }

        void Quant(KernelsQuant.Mode mode, GpuBuffer x, GpuBuffer aux, int n, params GW[] users)
        {
            bool q80 = false, q8k = false;
            foreach (GW w in users) { if (w.Type == GgmlType.Q8_0 || w.Type == GgmlType.Q4_0) q80 = true; else q8k = true; }
            uint[] push = { 0, (uint)n, Bits(C.Eps) };
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

        void MatVec(GW w, GpuBuffer y, bool barrierAfter)
        {
            uint gx = (uint)Math.Min(w.Rows, 65535), gy = (uint)((w.Rows + gx - 1) / gx);
            uint[] push = w.Type == GgmlType.Q4_0 ? new uint[] { (uint)w.Rows, (uint)w.Nb, gx, 0u, (uint)w.Stride } : new uint[] { (uint)w.Rows, (uint)w.Nb, gx, 0u };
            switch (w.Type)
            {
                case GgmlType.Q8_0: _gpu.Record(_kMvQ8, new GpuBuffer[] { w.A, w.B, _xq, _xd, y }, push, gx, gy, 1, barrierAfter); return;
                case GgmlType.Q4_0: _gpu.Record(_kMvQ4, new GpuBuffer[] { w.A, w.B, _xq, _xd, y }, push, gx, gy, 1, barrierAfter); return;
                case GgmlType.Q4_K: _gpu.Record(_kMvQ4K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1, barrierAfter); return;
                case GgmlType.Q5_K: _gpu.Record(_kMvQ5K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1, barrierAfter); return;
                case GgmlType.Q6_K: _gpu.Record(_kMvQ6K, new GpuBuffer[] { w.A, _kq, _kd, y }, push, gx, gy, 1, barrierAfter); return;
            }
            throw new NotSupportedException(GgmlTypes.Name(w.Type));
        }

        // True when every given weight (null ignored) takes Q8_0 activations, so a fused NormAddQuant can feed them.
        static bool Q80Users(params GW[] ws)
        {
            foreach (GW w in ws) if (w != null && w.Type != GgmlType.Q4_0 && w.Type != GgmlType.Q8_0) return false;
            return true;
        }

        // Per-token host values for one kind: rope table, params (window), indirect grids.
        void Prepare(Kind kd, int pos)
        {
            if (kd == null) return;
            CpuRef.RopeTable(pos, kd.NRot, kd.RopeBase, kd.RopeFreqs, kd.CsHost);
            kd.Cs.Upload(kd.CsHost, 0);
            int first = kd == _swa ? Math.Max(0, pos - C.NSwa + 1) : 0;
            int n = pos + 1 - first;
            uint chunks = (uint)((n + KernelsAttention.Chunk - 1) / KernelsAttention.Chunk);
            kd.ParamHost[0] = (uint)pos; kd.ParamHost[1] = (uint)n; kd.ParamHost[2] = chunks; kd.ParamHost[3] = Bits(1f); kd.ParamHost[4] = (uint)first;
            kd.Params.UploadWords(kd.ParamHost);
            kd.IndirectHost[0] = Groups(n, KernelsAttention.KeysPerGroup); kd.IndirectHost[1] = (uint)kd.NHeadKv; kd.IndirectHost[2] = 1;
            kd.IndirectHost[3] = chunks; kd.IndirectHost[4] = (uint)kd.NHeadKv; kd.IndirectHost[5] = 1;
            kd.Indirect.UploadWords(kd.IndirectHost);
        }

        // Image soft tokens (negative ids, see ImageTokens): their embedding rows, set by the engine when it has a vision
        // tower. BidiImages: an image's tokens attend to each other both ways on sliding-window layers (Gemma 4 12B).
        public Func<int, float[]> ImageRow;
        public bool BidiImages;

        public float[] Forward(int token, int pos)
        {
            if (pos >= MaxCtx) throw new InvalidOperationException("Context full.");
            if (token < 0) return ForwardEmbedding(ImageRow(token), pos);
            CpuRef.GetRow(_tokEmbd, token, _xHost);
            for (int i = 0; i < _xHost.Length; i++) _xHost[i] *= _embdScale;
            return ForwardX(token, pos);
        }

        // One position from a ready-made input embedding (an image token): no sqrt(d) scale; E2B/E4B use the
        // per-layer row of token 0 (padding), like llama.cpp.
        public float[] ForwardEmbedding(float[] embd, int pos)
        {
            if (pos >= MaxCtx) throw new InvalidOperationException("Context full.");
            Array.Copy(embd, _xHost, C.NEmbd);
            return ForwardX(0, pos);
        }

        // Host rows of the per-layer token embedding, pre-scaled by sqrt(pl).
        void PleTokRow(int token, float[] dst, int off)
        {
            CpuRef.GetRow(_pleTok, token, _pleTokHost);
            float ts = (float)Math.Sqrt(Pl);
            for (int i = 0; i < _pleTokHost.Length; i++) dst[off + i] = _pleTokHost[i] * ts;
        }

        float[] ForwardX(int pleToken, int pos)
        {
            _x.Upload(_xHost, 0);
            if (_pleTok != null)
            {
                float[] row = new float[Pl * C.NLayer];
                PleTokRow(pleToken, row, 0);
                _pleTokUp.Upload(row, 0);
            }
            Prepare(_swa, pos);
            Prepare(_global, pos);

            if (!_gpu.Profiling)
            {
                if (_graph == IntPtr.Zero)
                {
                    IntPtr gr = _gpu.AllocateCommandBuffer();
                    _gpu.BeginReusable(gr);
                    RecordPass();
                    _gpu.EndReusable();
                    _graph = gr;
                }
                _gpu.SubmitReusable(_graph);
            }
            else
            {
                _gpu.Begin();
                RecordPass();
                _gpu.SubmitAndWait();
            }
            _logits.Download(_logitsHost, 0);   // softcap already applied on the GPU
            return _logitsHost;
        }

        // One decode step; position-dependent values come from each kind's params / indirect buffers.
        void RecordPass()
        {
            bool inputReady = false;   // the previous layer's fused norm already quantized this layer's input
            if (_pleTok != null) RecordPleInputs(_x, _pleTokUp, _pleProjOut, _pleAll, 1);
            for (int il = 0; il < C.NLayer; il++)
            {
                Layer l = _layers[il];
                Kind kd = l.Swa ? _swa : _global;
                int hd = l.HeadDim, qDim = C.NHead * hd;
                uint half = (uint)(l.NRot / 2), eps = Bits(C.Eps);

                if (!inputReady)
                {
                    if (l.Wv != null) Quant(KernelsQuant.Mode.RmsNorm, _x, l.AttnNorm, C.NEmbd, l.Wq, l.Wk, l.Wv);
                    else if (l.Wk != null) Quant(KernelsQuant.Mode.RmsNorm, _x, l.AttnNorm, C.NEmbd, l.Wq, l.Wk);
                    else Quant(KernelsQuant.Mode.RmsNorm, _x, l.AttnNorm, C.NEmbd, l.Wq);
                }
                if (l.Shared)
                {
                    // Q only: K/V of this position are already in the source layer's cache (same kind, same rope).
                    MatVec(l.Wq, _q, true);
                    _gpu.Record(_kHeadNorm, new GpuBuffer[] { _q, l.QNorm, _q }, new uint[] { (uint)hd, eps, 1 }, (uint)C.NHead, 1, 1);
                    _gpu.Record(_kRope, new GpuBuffer[] { _q, kd.Cs }, new uint[] { (uint)C.NHead, (uint)hd, half }, Groups(C.NHead * (int)half, Kernels.Wg), 1, 1);
                }
                else
                {
                    MatVec(l.Wq, _q, false);
                    if (l.Wv != null) { MatVec(l.Wk, _k, false); MatVec(l.Wv, _v, true); }
                    else MatVec(l.Wk, _k, true);
                    // V = plain RMSNorm of V (or of the raw K when the layer has no V weight), then the weighted Q/K norms.
                    _gpu.Record(_kHeadNorm, new GpuBuffer[] { l.Wv != null ? _v : _k, l.KNorm, _v }, new uint[] { (uint)hd, eps, 0 }, (uint)l.NHeadKv, 1, 1, l.Wv == null);   // barrier when it reads K (normed next)
                    _gpu.Record(_kHeadNorm, new GpuBuffer[] { _q, l.QNorm, _q }, new uint[] { (uint)hd, eps, 1 }, (uint)C.NHead, 1, 1, false);
                    _gpu.Record(_kHeadNorm, new GpuBuffer[] { _k, l.KNorm, _k }, new uint[] { (uint)hd, eps, 1 }, (uint)l.NHeadKv, 1, 1);
                    _gpu.Record(_kRope, new GpuBuffer[] { _q, kd.Cs }, new uint[] { (uint)C.NHead, (uint)hd, half }, Groups(C.NHead * (int)half, Kernels.Wg), 1, 1, false);
                    _gpu.Record(_kRope, new GpuBuffer[] { _k, kd.Cs }, new uint[] { (uint)l.NHeadKv, (uint)hd, half }, Groups(l.NHeadKv * (int)half, Kernels.Wg), 1, 1);
                    _gpu.Record(_kKvT, new GpuBuffer[] { _k, _v, l.KCache, l.VCache, kd.Params }, new uint[] { (uint)l.KvDim, (uint)l.Ring }, Groups(KvStoreGroups(l.KvDim), Kernels.Wg), 1, 1);
                }

                // attention (scale 1)
                _gpu.RecordIndirect(kd.Scores, new GpuBuffer[] { _q, l.KCache, _scores, kd.Params }, new uint[] { (uint)hd, (uint)l.KvDim, (uint)MaxCtx, Bits(1f), (uint)l.Ring }, kd.Indirect, 0);
                _gpu.Record(_kSoftmax, new GpuBuffer[] { _scores, kd.Params }, new uint[] { (uint)MaxCtx }, (uint)C.NHead, 1, 1);
                _gpu.RecordIndirect(kd.Out, new GpuBuffer[] { _scores, l.VCache, _attnPart, kd.Params }, new uint[] { (uint)hd, (uint)l.KvDim, (uint)MaxCtx, (uint)C.NHead, (uint)l.Ring }, kd.Indirect, 12);
                _gpu.Record(_kReduce, new GpuBuffer[] { _attnPart, _attn, kd.Params }, new uint[] { (uint)qDim }, Groups(qDim, Kernels.Wg), 1, 1);

                Quant(KernelsQuant.Mode.Plain, _attn, null, qDim, l.Wo);
                MatVec(l.Wo, _xb, true);
                if (Q80Users(l.Gate, l.Up))
                    _gpu.Record(_kNormAddQ, new GpuBuffer[] { _x, _xb, l.PostAttnNorm, l.FfnNorm, _xq, _xd }, new uint[] { (uint)C.NEmbd, eps, Bits(1f) }, 1, 1, 1);
                else
                {
                    _gpu.Record(_kNormAdd, new GpuBuffer[] { _x, _xb, l.PostAttnNorm }, new uint[] { (uint)C.NEmbd, eps, Bits(1f) }, 1, 1, 1);
                    Quant(KernelsQuant.Mode.RmsNorm, _x, l.FfnNorm, C.NEmbd, l.Gate, l.Up);
                }
                GpuBuffer lastNorm = l.PostFfnNorm, ffn = _xb;
                if (C.Moe && Q80Users(l.Gate, l.Up, l.Down)) { RecordFfnMoe(l); ffn = _mlp; }
                else
                {
                    MatVec(l.Gate, _g, false);
                    MatVec(l.Up, _u, true);
                    Quant(KernelsQuant.Mode.GeluMul, _g, _u, l.NFf, l.Down);
                    MatVec(l.Down, _xb, true);
                    if (C.Moe) { RecordMoe(l); ffn = _mlp; }
                }
                if (_pleTok != null)
                {
                    // x += norm(ffn); then x += norm(proj(gelu(inp_gate(x)) * ple[il])) (the output scale comes after)
                    _gpu.Record(_kNormAdd, new GpuBuffer[] { _x, _xb, l.PostFfnNorm }, new uint[] { (uint)C.NEmbd, eps, Bits(1f) }, 1, 1, 1);
                    Quant(KernelsQuant.Mode.Plain, _x, null, C.NEmbd, l.PleGate);
                    MatVec(l.PleGate, _g, false);
                    _gpu.Record(_kCopy, new GpuBuffer[] { _pleAll, _u }, new uint[] { (uint)Pl, (uint)(il * Pl), 0 }, Groups(Pl, Kernels.Wg), 1, 1);
                    Quant(KernelsQuant.Mode.GeluMul, _g, _u, Pl, l.PleProj);
                    MatVec(l.PleProj, _xb, true);
                    lastNorm = l.PlePostNorm;
                }
                Layer next = il + 1 < C.NLayer ? _layers[il + 1] : null;
                inputReady = next != null && Q80Users(next.Wq, next.Wk, next.Wv);
                if (inputReady)
                    _gpu.Record(_kNormAddQ, new GpuBuffer[] { _x, ffn, lastNorm, next.AttnNorm, _xq, _xd }, new uint[] { (uint)C.NEmbd, eps, Bits(l.OutScale) }, 1, 1, 1);
                else
                    _gpu.Record(_kNormAdd, new GpuBuffer[] { _x, ffn, lastNorm }, new uint[] { (uint)C.NEmbd, eps, Bits(l.OutScale) }, 1, 1, 1);
            }
            Quant(KernelsQuant.Mode.RmsNorm, _x, _outputNorm, C.NEmbd, _output);
            MatVec(_output, _logits, true);
            if (C.Softcap > 0)
                _gpu.Record(_kSoftcap, new GpuBuffer[] { _logits }, new uint[] { (uint)C.NVocab, Bits(C.Softcap) }, Groups(C.NVocab, Kernels.Wg), 1, 1);
        }

        // Routed experts of one decode step on attn_out (_x); the shared MLP's output is in _xb. Leaves
        // norm1(mlp) + norm2(experts) in _mlp. Routing never leaves the GPU (KernelsMoe).
        void RecordMoe(Layer l)
        {
            int d = C.NEmbd, nf = C.NFfExp, k = C.NExpertUsed;
            uint eps = Bits(C.Eps);
            _gpu.Record(_kRouterLogits, new GpuBuffer[] { _x, l.GateInpS, l.GateInp, _rlogits }, new uint[] { (uint)d, eps, Bits(1f / (float)Math.Sqrt(d)), (uint)C.NExpert }, (uint)C.NExpert, 1, 1);
            _gpu.Record(_kRouterSelect, new GpuBuffer[] { _rlogits, l.DownScale, _route }, new uint[] { (uint)C.NExpert, (uint)k }, 1, 1, 1, false);
            Quant(KernelsQuant.Mode.RmsNorm, _x, l.PreFfwNorm2, d, l.GateUpExp);
            GW gu = l.GateUpExp, dn = l.DownExp;
            uint guRows = (uint)(2 * nf), dnRows = (uint)d;
            _gpu.Record(_kMoeGateUp, new GpuBuffer[] { gu.A, gu.B, _xq, _xd, _g, _u, _route }, new uint[] { guRows, (uint)gu.Nb, (uint)gu.Stride, (uint)nf }, guRows, (uint)k, 1);
            Quant(KernelsQuant.Mode.GeluMul, _g, _u, k * nf, dn);
            _gpu.Record(_kMoeDown, new GpuBuffer[] { dn.A, dn.B, _xq, _xd, _moe, _route }, new uint[] { dnRows, (uint)dn.Nb, (uint)dn.Stride, (uint)k }, dnRows, 1, 1);
            _gpu.Record(_kNorm2, new GpuBuffer[] { _xb, _moe, l.PostFfwNorm1, l.PostFfwNorm2, _mlp }, new uint[] { (uint)d, eps }, 1, 1, 1);
        }

        // Q8_0 quantization of x (mode as KernelsQuant) into the given buffers.
        void Q80To(KernelsQuant.Mode mode, GpuBuffer x, GpuBuffer aux, int n, GpuBuffer xq, GpuBuffer xd, bool barrierAfter)
        {
            int nb = n / 32;
            GpuBuffer[] b = mode == KernelsQuant.Mode.Plain ? new GpuBuffer[] { x, xq, xd } : new GpuBuffer[] { x, xq, xd, aux };
            _gpu.Record(_kQ80[(int)mode], b, new uint[] { (uint)nb, (uint)n, Bits(C.Eps) }, Groups(nb, 8), 1, 1, barrierAfter);
        }

        // The whole feed-forward of a 26B decode step (attn_out in _x, ffn_norm(x) already quantized in _xq), scheduled so
        // the shared MLP and the routed experts overlap: 6 barriers instead of 9, and the small router kernels run beside
        // the big matvecs instead of alone. The experts use their own activation buffers (_xq2, _g2, _u2). Leaves
        // norm1(mlp) + norm2(experts) in _mlp.
        void RecordFfnMoe(Layer l)
        {
            int d = C.NEmbd, nf = C.NFfExp, k = C.NExpertUsed;
            uint eps = Bits(C.Eps);
            GW gu = l.GateUpExp, dn = l.DownExp;
            uint guRows = (uint)(2 * nf), dnRows = (uint)d;
            // A: shared gate / up, router logits, the experts' input
            MatVec(l.Gate, _g, false);
            MatVec(l.Up, _u, false);
            _gpu.Record(_kRouterLogits, new GpuBuffer[] { _x, l.GateInpS, l.GateInp, _rlogits }, new uint[] { (uint)d, eps, Bits(1f / (float)Math.Sqrt(d)), (uint)C.NExpert }, (uint)C.NExpert, 1, 1, false);
            Q80To(KernelsQuant.Mode.RmsNorm, _x, l.PreFfwNorm2, d, _xq2, _xd2, true);
            // B: shared GELU, top-k
            Q80To(KernelsQuant.Mode.GeluMul, _g, _u, l.NFf, _xq, _xd, false);
            _gpu.Record(_kRouterSelect, new GpuBuffer[] { _rlogits, l.DownScale, _route }, new uint[] { (uint)C.NExpert, (uint)k }, 1, 1, 1, true);
            // C: shared down, experts' gate|up
            MatVec(l.Down, _xb, false);
            _gpu.Record(_kMoeGateUp, new GpuBuffer[] { gu.A, gu.B, _xq2, _xd2, _g2, _u2, _route }, new uint[] { guRows, (uint)gu.Nb, (uint)gu.Stride, (uint)nf }, guRows, (uint)k, 1, true);
            // D-F: experts' GELU, down (weighted sum), the two branch norms
            Q80To(KernelsQuant.Mode.GeluMul, _g2, _u2, k * nf, _xq2, _xd2, true);
            _gpu.Record(_kMoeDown, new GpuBuffer[] { dn.A, dn.B, _xq2, _xd2, _moe, _route }, new uint[] { dnRows, (uint)dn.Nb, (uint)dn.Stride, (uint)k }, dnRows, 1, 1, true);
            _gpu.Record(_kNorm2, new GpuBuffer[] { _xb, _moe, l.PostFfwNorm1, l.PostFfwNorm2, _mlp }, new uint[] { (uint)d, eps }, 1, 1, 1, true);
        }

        // Per-layer inputs of T rows of x (each row the scaled input embedding): out[l][t][pl]. tok holds the token
        // rows pre-scaled by sqrt(pl).
        void RecordPleInputs(GpuBuffer x, GpuBuffer tok, GpuBuffer proj, GpuBuffer outB, int T)
        {
            int n = Pl * C.NLayer;
            _gpu.Record(_kMmF16, new GpuBuffer[] { _pleProjW, x, proj }, new uint[] { (uint)n, (uint)C.NEmbd, (uint)T },
                Groups(n, KernelsBatch.MmRows), Groups(T, KernelsBatch.MmTokens), 1);
            _gpu.Record(_kPle, new GpuBuffer[] { proj, tok, _pleProjNorm, outB },
                new uint[] { (uint)Pl, (uint)C.NLayer, (uint)T, Bits(C.Eps), Bits(1f / (float)Math.Sqrt(C.NEmbd)), Bits(1f / (float)Math.Sqrt(2.0)) },
                (uint)(T * C.NLayer), 1, 1);
        }

        public void Reset() { }

        // Earliest position the engine may keep cached when it has fed `fed` tokens: rewinding further would need
        // sliding-window positions the ring has already overwritten.
        public int MinReuse(int fed)
        {
            int ring = int.MaxValue;
            foreach (Layer l in _layers) if (l.Swa) ring = Math.Min(ring, l.Ring);
            if (ring == int.MaxValue || ring >= MaxCtx) return 0;
            return Math.Max(0, fed - (ring - C.NSwa));
        }

        // Feeds tokens[start .. start+count) at positions start..; returns the logits after the last one. Chunks of up to
        // PrefillChunk tokens go through the batched kernels; a short tail (< 8 tokens) uses the decode pass.
        public float[] Prefill(IList<int> tokens, int start, int count)
        {
            float[] logits = null;
            int i = start, end = start + count;
            if (UsePrefill && _prefillOk)
            {
                while (i < end)
                {
                    // Chunk size adapts so one submit stays short: attention work grows with the position (and with the
                    // cache stride, i.e. the context size), and a 128K context made a 512-token chunk at position 20K
                    // run past the Windows GPU timeout (device lost, 2026-10-17).
                    int T = ImageTokens.Fit(tokens, i, Math.Min(_chunkCap, _chunk), end);   // an image stays in one chunk
                    if (BidiImages)
                    {
                        // the two-way span covers one image per chunk: a second image starts the next chunk
                        int b0, b1;
                        ImageTokens.FirstRun(tokens, i, T, out b0, out b1);
                        if (b1 > 0) for (int k = b1; k < i + T; k++) if (tokens[k] < 0) { T = k - i; break; }
                    }
                    bool img = false;
                    for (int k = i; k < i + T && !img; k++) img = tokens[k] < 0;
                    if (T == 0 || (end - i < 8 && !img)) break;   // a short text tail goes through the decode pass
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    logits = PrefillChunkRun(tokens, i, T);
                    double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                    // the timeout applies per submit: a chunk is split every LayersPerSubmit layers
                    ms *= Math.Min(1.0, (double)SubmitLayers / C.NLayer);
                    if (ms > ChunkSlowMs && _chunk > ChunkMin) _chunk = Math.Max(ChunkMin, _chunk / 2);
                    else if (ms < ChunkFastMs && T == _chunk && _chunk < _chunkCap) _chunk = Math.Min(_chunkCap, _chunk * 2);
                    i += T;
                }
            }
            for (; i < end; i++) logits = Forward(tokens[i], i);
            return logits;
        }

        void EnsurePrefill()
        {
            if (_kMm != null) return;
            _kMm = Kernel(KernelsPrefill.MatMulQ4Q8Tile(_gpu.IntegerDot, MmRpt, MmTok, true), 5, 4, "pf matmul q4_0");
            _coop = UseCoop && _gpu.CoopMat && _gpu.SubgroupSize == 64;
            if (_coop)
            {
                _kCoop = Kernel(KernelsCoop.MatMul(false, CoopBm, CoopBn, CoopSr), 4, 4, "pf matmul coop f16");
                _kGeluMul = Kernel(KernelsCoop.GeluMul(), 2, 1, "pf gelu mul");
            }
            _kMmAny = Kernel(KernelsPrefill.MatMulQ4Q8(_gpu.IntegerDot, 1), 5, 4, "pf matmul q4_0 (any)");
            _kKvRows = Kernel(KernelsKv.StoreRows(Kv, KvV), 4, 5, "pf kv store " + KernelsKv.Name(Kv) + "/" + KernelsKv.Name(KvV));
            // tiled attention when shared memory allows (KernelsFlash: ~16x less K/V traffic at long context)
            // one tiled kernel per head size (Gemma: 256 sliding-window, 512 global)
            _flash = UseFlash;
            if (UseFlash2 && Kv == KvType.F16 && KvV == KvType.F16)
                foreach (Layer l in _layers)
                {
                    uint hdT = ((uint)l.HeadDim + 31) & ~31u;
                    if (_kFlash2.ContainsKey(hdT) || hdT != (uint)l.HeadDim || !KernelsFlash2.Fits(hdT, C.NHead, l.NHeadKv) || l.KvDim % 2 != 0) continue;
                    _kFlash2[hdT] = Kernel(KernelsFlash2.Attention(hdT), 4, 9, "pf attention reg " + hdT);
                }
            _kAttnPre = Kernel(KernelsPrefill.Attention(Kv, KvV), 4, 10, "pf attention");
            if (_flash)
                foreach (Layer l in _layers)
                {
                    uint hdT = ((uint)l.HeadDim + 31) & ~31u;
                    if (_kFlash.ContainsKey(hdT)) continue;
                    uint tq, kc;
                    KernelsFlash.Tile(hdT, _gpu.MaxSharedMemory, out tq, out kc);
                    // 8 x 8 tiles (512-wide heads under a 32 KB limit) measured slower than the plain kernel: keep it there
                    if (tq < 16 || (_gpu.MaxSharedMemory != 0 && KernelsFlash.SharedBytes(hdT, tq, kc) > _gpu.MaxSharedMemory)) continue;
                    _kFlash[hdT] = Kernel(KernelsFlash.Attention(Kv, KvV, hdT, tq, kc), 4, 11, "pf attention tiled " + hdT);
                    _flashTq[hdT] = tq;
                }
            _kCopyRange = Kernel(KernelsPrefill.CopyRange(), 2, 3, "pf copy");
            _kRowNorm = Kernel(KernelsBatch.RowNorm(), 3, 4, "pf row norm");
            _kRopeRows = Kernel(KernelsBatch.RopeNeoxRows(), 2, 4, "pf rope");
            _kCopyRows = Kernel(KernelsBatch.Copy(), 2, 1, "pf v=k");
            ulong T = (ulong)_chunkCap, d = (ulong)C.NEmbd;
            int maxIn = Math.Max(Math.Max(C.NEmbd, _maxQ), _maxFf);
            _pX = Buf(T * d * 4, BufferKind.Device); _pXb = Buf(T * d * 4, BufferKind.Device); _pTmp = Buf(T * d * 4, BufferKind.Device);
            _pQ = Buf(T * (ulong)_maxQ * 4, BufferKind.Device); _pK = Buf(T * (ulong)_maxKv * 4, BufferKind.Device); _pV = Buf(T * (ulong)_maxKv * 4, BufferKind.Device);
            _pAtt = Buf(T * (ulong)_maxQ * 4, BufferKind.Device);
            _pG = Buf(T * (ulong)_maxFf * 4, BufferKind.Device); _pU = Buf(T * (ulong)_maxFf * 4, BufferKind.Device);
            _pXq = Buf(T * (ulong)maxIn, BufferKind.Device); _pXd = Buf(T * (ulong)(maxIn / 32) * 4, BufferKind.Device);
            _pCsSwa = Buf(T * (ulong)(_swa != null ? _swa.NRot : 2) * 4, BufferKind.Upload);
            _pCsGlobal = Buf(T * (ulong)(_global != null ? _global.NRot : 2) * 4, BufferKind.Upload);
            _pUp = Buf(T * d * 4, BufferKind.Upload);
            if (_pleTok != null)
            {
                ulong n = (ulong)(Pl * C.NLayer);
                _pPleTok = Buf(T * n * 4, BufferKind.Upload);
                _pPleProj = Buf(T * n * 4, BufferKind.Device);
                _pPle = Buf(T * n * 4, BufferKind.Device);
            }
            if (C.Moe)
            {
                ulong ne = (ulong)C.NExpert, k = (ulong)C.NExpertUsed;
                _kBucket = Kernel(KernelsMoe.Bucket(), 3, 2, "pf moe bucket");
                if (C.NExpert >= 64 && 1024 % C.NExpert == 0 && C.NEmbd % 64 == 0) _kRouterRows = Kernel(KernelsMoe.RouterLogitsRows((uint)C.NExpert), 4, 4, "pf moe router");
                _kGm = Kernel(KernelsMoe.GroupedMm(_gpu.IntegerDot, GmRpt), 8, 6, "pf moe grouped mm");
                if (_coop) _kCoopG = Kernel(KernelsCoop.MatMul(true, GmCoopBm, GmCoopBn, GmCoopSr), 7, 6, "pf moe coop f16");
                _kCombine = Kernel(KernelsMoe.Combine(), 3, 2, "pf moe combine");
                _pLogits = Buf(T * ne * 4, BufferKind.Device);
                _pRoute = Buf(T * 2 * k * 4, BufferKind.Device);
                _pList = Buf(ne * T * 4, BufferKind.Device);
                _pCnt = Buf(ne * 4, BufferKind.Device);
                _pYd = Buf(T * k * d * 4, BufferKind.Device);
                _pMoe = Buf(T * d * 4, BufferKind.Device);
                _pMlp = Buf(T * d * 4, BufferKind.Device);
            }
        }

        // Routed experts of T prompt rows (attn_out in _pX, the shared MLP's down output in _pTmp): route, gather each
        // expert's tokens, grouped gate|up and down, weighted sum per token; leaves norm1(mlp) + norm2(experts) in _pMlp.
        public static uint GmRpt = 4;
        public static uint GmCoopBm = 128, GmCoopBn = 32, GmCoopSr = 4;  // expert tiles on the matrix units (rows x tokens, subgroup rows; 8060S sweep)
        public static uint CoopBm = 64, CoopBn = 64, CoopSr = 2;         // dense tiles
        GpuKernel _kBucket, _kGm, _kCombine, _kRouterRows;
        GpuBuffer _pLogits, _pRoute, _pList, _pCnt, _pYd, _pMoe, _pMlp;

        void RecordMoeRows(Layer l, int T)
        {
            int d = C.NEmbd, nf = C.NFfExp, k = C.NExpertUsed;
            uint eps = Bits(C.Eps), tiles = Groups(T, KernelsMoe.GmTok);
            if (_kRouterRows != null)
                _gpu.Record(_kRouterRows, new GpuBuffer[] { _pX, l.GateInpS, l.GateInpT, _pLogits }, new uint[] { (uint)d, eps, Bits(1f / (float)Math.Sqrt(d)), (uint)T }, Groups(T, KernelsMoe.RtTok), 1, 1);
            else
                _gpu.Record(_kRouterLogits, new GpuBuffer[] { _pX, l.GateInpS, l.GateInp, _pLogits }, new uint[] { (uint)d, eps, Bits(1f / (float)Math.Sqrt(d)), (uint)C.NExpert }, (uint)C.NExpert, (uint)T, 1);
            _gpu.Record(_kRouterSelect, new GpuBuffer[] { _pLogits, l.DownScale, _pRoute }, new uint[] { (uint)C.NExpert, (uint)k }, (uint)T, 1, 1);
            _gpu.Record(_kBucket, new GpuBuffer[] { _pRoute, _pList, _pCnt }, new uint[] { (uint)T, (uint)k }, (uint)C.NExpert, 1, 1);
            _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, _pXb, l.PreFfwNorm2 }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)T, 1, 1);
            PfIn(_pXb, null, KernelsQuant.Mode.Plain, T * d);
            GW gu = l.GateUpExp, dn = l.DownExp;
            if (_coop)
            {
                uint ctiles = Groups(T, GmCoopBn);
                _gpu.Record(_kCoopG, new GpuBuffer[] { gu.A, gu.B, _pIn, _pG, _pU, _pList, _pCnt },
                    new uint[] { (uint)(2 * nf), (uint)gu.Nb, (uint)gu.Stride, (uint)T, (uint)k, (uint)nf }, (uint)(2 * nf) / GmCoopBm, ctiles, (uint)C.NExpert);
                PfIn(_pG, _pU, KernelsQuant.Mode.GeluMul, T * k * nf);
                _gpu.Record(_kCoopG, new GpuBuffer[] { dn.A, dn.B, _pIn, _pYd, _pYd, _pList, _pCnt },
                    new uint[] { (uint)d, (uint)dn.Nb, (uint)dn.Stride, (uint)T, 1, 0 }, (uint)d / GmCoopBm, ctiles, (uint)C.NExpert);
            }
            else
            {
                _gpu.Record(_kGm, new GpuBuffer[] { gu.A, gu.B, _pXq, _pXd, _pG, _pU, _pList, _pCnt },
                    new uint[] { (uint)(2 * nf), (uint)gu.Nb, (uint)gu.Stride, (uint)T, (uint)k, (uint)nf }, Groups(2 * nf, 64 * GmRpt), tiles, (uint)C.NExpert);
                PfIn(_pG, _pU, KernelsQuant.Mode.GeluMul, T * k * nf);
                _gpu.Record(_kGm, new GpuBuffer[] { dn.A, dn.B, _pXq, _pXd, _pYd, _pYd, _pList, _pCnt },
                    new uint[] { (uint)d, (uint)dn.Nb, (uint)dn.Stride, (uint)T, 1, 0 }, Groups(d, 64 * GmRpt), tiles, (uint)C.NExpert);
            }
            _gpu.Record(_kCombine, new GpuBuffer[] { _pYd, _pRoute, _pMoe }, new uint[] { (uint)d, (uint)k }, Groups(d, Kernels.Wg), (uint)T, 1);
            _gpu.Record(_kNorm2, new GpuBuffer[] { _pTmp, _pMoe, l.PostFfwNorm1, l.PostFfwNorm2, _pMlp }, new uint[] { (uint)d, eps }, (uint)T, 1, 1);
        }

        void RowQuant(GpuBuffer x, GpuBuffer aux, KernelsQuant.Mode mode, int n)
        {
            int nb = n / 32;
            GpuBuffer[] b = mode == KernelsQuant.Mode.Plain ? new GpuBuffer[] { x, _pXq, _pXd } : new GpuBuffer[] { x, _pXq, _pXd, aux };
            _gpu.Record(_kQ80[(int)mode], b, new uint[] { (uint)nb, (uint)n, Bits(C.Eps) }, Groups(nb, 8), 1, 1);
        }

        // Prefill matmul input: with cooperative matrices the f32 rows are read as they are (f16 tiles; GeluMul applied in
        // place), otherwise quantized to Q8_0 for the integer kernels.
        public static bool UseCoop = true;
        bool _coop;
        GpuBuffer _pIn;
        GpuKernel _kCoop, _kCoopG, _kGeluMul;

        void PfIn(GpuBuffer x, GpuBuffer aux, KernelsQuant.Mode mode, int n)
        {
            if (!_coop) { RowQuant(x, aux, mode, n); return; }
            if (mode == KernelsQuant.Mode.GeluMul) _gpu.Record(_kGeluMul, new GpuBuffer[] { x, aux }, new uint[] { (uint)n }, Groups(n, Kernels.Wg), 1, 1);
            _pIn = x;
        }

        static bool CoopShape(GW w) { return w.Type == GgmlType.Q4_0 && w.Rows % CoopBm == 0 && w.Cols % KernelsCoop.Bk == 0; }

        void Mm(GW w, GpuBuffer y, int T, bool barrierAfter)
        {
            if (_coop)
            {
                if (CoopShape(w))
                {
                    _gpu.Record(_kCoop, new GpuBuffer[] { w.A, w.B, _pIn, y }, new uint[] { (uint)w.Rows, (uint)w.Nb, (uint)T, (uint)w.Stride },
                        (uint)(w.Rows / CoopBm), Groups(T, CoopBn), 1, barrierAfter);
                    return;
                }
                RowQuant(_pIn, null, KernelsQuant.Mode.Plain, T * w.Cols);   // another shape: quantize for the integer kernel
                barrierAfter = true;   // the next fallback re-quantizes into the same buffer
            }
            bool fast = w.Nb % (int)KernelsPrefill.MmKb == 0;
            _gpu.Record(fast ? _kMm : _kMmAny, new GpuBuffer[] { w.A, w.B, _pXq, _pXd, y }, new uint[] { (uint)w.Rows, (uint)w.Nb, (uint)T, (uint)w.Stride },
                Groups(w.Rows, fast ? KernelsPrefill.MmThreads * MmRpt : KernelsPrefill.MmThreads), Groups(T, fast ? MmTok : KernelsPrefill.MmTok), 1, barrierAfter);
        }

        void UploadRopeRows(Kind kd, GpuBuffer dst, int p0, int T)
        {
            if (kd == null) return;
            float[] all = new float[T * kd.NRot], one = new float[kd.NRot];
            for (int t = 0; t < T; t++)
            {
                CpuRef.RopeTable(p0 + t, kd.NRot, kd.RopeBase, kd.RopeFreqs, one);
                Array.Copy(one, 0, all, t * kd.NRot, kd.NRot);
            }
            dst.Upload(all, 0);
        }

        float[] PrefillChunkRun(IList<int> tokens, int p0, int T)
        {
            if (p0 + T > MaxCtx) throw new InvalidOperationException("Context full.");
            int d = C.NEmbd;
            float[] host = new float[T * d], row = new float[d];
            int[] pleTokens = new int[T];
            for (int t = 0; t < T; t++)
            {
                int tok = tokens[p0 + t];
                if (tok < 0)   // image soft token: the vision tower's row, unscaled; per-layer row of token 0 (padding)
                {
                    Array.Copy(ImageRow(tok), 0, host, t * d, d);
                    pleTokens[t] = 0;
                    continue;
                }
                CpuRef.GetRow(_tokEmbd, tok, row);
                for (int i = 0; i < d; i++) host[t * d + i] = row[i] * _embdScale;
                pleTokens[t] = tok;
            }
            int b0 = 0, b1 = 0;
            if (BidiImages) ImageTokens.FirstRun(tokens, p0, T, out b0, out b1);
            return PrefillRun(host, pleTokens, p0, T, b0, b1);
        }

        // Batched pass over T input rows (host: [T][d], already scaled) at positions p0..p0+T-1. pleTokens: the token of
        // each row for the E2B/E4B per-layer embedding (0 for image rows). [bidi0, bidi1): positions (an image) whose
        // sliding-window layers attend to each other both ways (Gemma 4 12B image tokens); empty = causal everywhere.
        float[] PrefillRun(float[] host, int[] pleTokens, int p0, int T, int bidi0, int bidi1)
        {
            EnsurePrefill();
            int d = C.NEmbd;
            _pUp.Upload(host, 0);
            if (_pleTok != null)
            {
                float[] tok = new float[(long)T * Pl * C.NLayer];
                for (int t = 0; t < T; t++) PleTokRow(pleTokens[t], tok, t * Pl * C.NLayer);
                _pPleTok.Upload(tok, 0);
            }
            UploadRopeRows(_swa, _pCsSwa, p0, T);
            UploadRopeRows(_global, _pCsGlobal, p0, T);
            uint eps = Bits(C.Eps);

            _gpu.Begin();
            _gpu.Record(_kCopyRange, new GpuBuffer[] { _pUp, _pX }, new uint[] { (uint)(T * d), 0, 0 }, Groups(T * d, Kernels.Wg), 1, 1);
            if (_pleTok != null) RecordPleInputs(_pX, _pPleTok, _pPleProj, _pPle, T);
            for (int il = 0; il < C.NLayer; il++)
            {
                Layer l = _layers[il];
                int hd = l.HeadDim, qDim = C.NHead * hd;
                uint half = (uint)(l.NRot / 2);
                GpuBuffer cs = l.Swa ? _pCsSwa : _pCsGlobal;

                _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, _pXb, l.AttnNorm }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)T, 1, 1);
                PfIn(_pXb, null, KernelsQuant.Mode.Plain, T * d);
                if (l.Shared)
                {
                    Mm(l.Wq, _pQ, T, true);
                    _gpu.Record(_kHeadNorm, new GpuBuffer[] { _pQ, l.QNorm, _pQ }, new uint[] { (uint)hd, eps, 1 }, (uint)(T * C.NHead), 1, 1);
                    _gpu.Record(_kRopeRows, new GpuBuffer[] { _pQ, cs }, new uint[] { (uint)C.NHead, (uint)hd, half, (uint)T }, Groups(T * C.NHead * (int)half, Kernels.Wg), 1, 1);
                }
                else
                {
                Mm(l.Wq, _pQ, T, false);
                if (l.Wv != null) { Mm(l.Wk, _pK, T, false); Mm(l.Wv, _pV, T, true); }
                else
                {
                    Mm(l.Wk, _pK, T, true);
                    _gpu.Record(_kCopyRows, new GpuBuffer[] { _pK, _pV }, new uint[] { (uint)(T * l.KvDim) }, Groups(T * l.KvDim, Kernels.Wg), 1, 1);
                }
                _gpu.Record(_kHeadNorm, new GpuBuffer[] { _pV, l.KNorm, _pV }, new uint[] { (uint)hd, eps, 0 }, (uint)(T * l.NHeadKv), 1, 1, false);
                _gpu.Record(_kHeadNorm, new GpuBuffer[] { _pQ, l.QNorm, _pQ }, new uint[] { (uint)hd, eps, 1 }, (uint)(T * C.NHead), 1, 1, false);
                _gpu.Record(_kHeadNorm, new GpuBuffer[] { _pK, l.KNorm, _pK }, new uint[] { (uint)hd, eps, 1 }, (uint)(T * l.NHeadKv), 1, 1);
                _gpu.Record(_kRopeRows, new GpuBuffer[] { _pQ, cs }, new uint[] { (uint)C.NHead, (uint)hd, half, (uint)T }, Groups(T * C.NHead * (int)half, Kernels.Wg), 1, 1, false);
                _gpu.Record(_kRopeRows, new GpuBuffer[] { _pK, cs }, new uint[] { (uint)l.NHeadKv, (uint)hd, half, (uint)T }, Groups(T * l.NHeadKv * (int)half, Kernels.Wg), 1, 1);
                for (uint par = 0; par < KernelsKv.Passes(Kv); par++)
                    _gpu.Record(_kKvRows, new GpuBuffer[] { _pK, _pV, l.KCache, l.VCache }, new uint[] { (uint)l.KvDim, (uint)l.Ring, (uint)p0, (uint)T, par }, Groups(T * KvStoreGroups(l.KvDim), Kernels.Wg), 1, 1);
                }
                uint hdT = ((uint)hd + 31) & ~31u;
                // the register-blocked kernel is causal only: an image's two-way span on a sliding-window layer takes the others
                if (_kFlash2.ContainsKey(hdT) && KernelsFlash2.Fits(hdT, C.NHead, l.NHeadKv) && l.Ring % 2 == 0 && !(l.Swa && bidi1 > bidi0))
                {
                    uint rows, keys;
                    KernelsFlash2.Tile(hdT, out rows, out keys);
                    _gpu.Record(_kFlash2[hdT], new GpuBuffer[] { _pQ, l.KCache, l.VCache, _pAtt },
                        new uint[] { (uint)hd, (uint)l.KvDim, (uint)l.Ring, (uint)C.NHead, (uint)l.NHeadKv, (uint)p0, l.Swa ? (uint)C.NSwa : 0u, Bits(1f), (uint)T },
                        Groups(T, rows / (uint)(C.NHead / l.NHeadKv)), (uint)l.NHeadKv, 1);
                }
                else if (_flash && _kFlash.ContainsKey(hdT))
                    _gpu.Record(_kFlash[hdT], new GpuBuffer[] { _pQ, l.KCache, l.VCache, _pAtt },
                        new uint[] { (uint)hd, (uint)l.KvDim, (uint)l.Ring, (uint)C.NHead, (uint)l.NHeadKv, (uint)p0, l.Swa ? (uint)C.NSwa : 0u, Bits(1f), (uint)T, l.Swa ? (uint)bidi0 : 0u, l.Swa ? (uint)bidi1 : 0u },
                        Groups(T, _flashTq[hdT]), (uint)C.NHead, 1);
                else
                    _gpu.Record(_kAttnPre, new GpuBuffer[] { _pQ, l.KCache, l.VCache, _pAtt },
                        new uint[] { (uint)hd, (uint)l.KvDim, (uint)l.Ring, (uint)C.NHead, (uint)l.NHeadKv, (uint)p0, l.Swa ? (uint)C.NSwa : 0u, Bits(1f), l.Swa ? (uint)bidi0 : 0u, l.Swa ? (uint)bidi1 : 0u }, (uint)T, (uint)C.NHead, 1);
                PfIn(_pAtt, null, KernelsQuant.Mode.Plain, T * qDim);
                Mm(l.Wo, _pTmp, T, true);
                _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, _pTmp, l.PostAttnNorm }, new uint[] { (uint)d, eps, 1, Bits(1f) }, (uint)T, 1, 1);

                _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, _pXb, l.FfnNorm }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)T, 1, 1);
                PfIn(_pXb, null, KernelsQuant.Mode.Plain, T * d);
                Mm(l.Gate, _pG, T, false);
                Mm(l.Up, _pU, T, true);
                PfIn(_pG, _pU, KernelsQuant.Mode.GeluMul, T * l.NFf);
                Mm(l.Down, _pTmp, T, true);
                GpuBuffer pFfn = _pTmp;
                if (C.Moe) { RecordMoeRows(l, T); pFfn = _pMlp; }
                bool ple = _pleTok != null;
                _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, pFfn, l.PostFfnNorm }, new uint[] { (uint)d, eps, 1, Bits(ple ? 1f : l.OutScale) }, (uint)T, 1, 1);
                if (ple)
                {
                    PfIn(_pX, null, KernelsQuant.Mode.Plain, T * d);
                    Mm(l.PleGate, _pG, T, false);
                    _gpu.Record(_kCopyRange, new GpuBuffer[] { _pPle, _pU }, new uint[] { (uint)(T * Pl), (uint)(il * T * Pl), 0 }, Groups(T * Pl, Kernels.Wg), 1, 1);
                    PfIn(_pG, _pU, KernelsQuant.Mode.GeluMul, T * Pl);
                    Mm(l.PleProj, _pTmp, T, true);
                    _gpu.Record(_kRowNorm, new GpuBuffer[] { _pX, _pTmp, l.PlePostNorm }, new uint[] { (uint)d, eps, 1, Bits(l.OutScale) }, (uint)T, 1, 1);
                }

                if ((il + 1) % SubmitLayers == 0 && il + 1 < C.NLayer) { _gpu.SubmitAndWait(); _gpu.Begin(); }
            }
            if (_allRows) { _gpu.SubmitAndWait(); return null; }   // PrefillRows: rows' logits on demand (RowLogits)
            RecordHead(T - 1);
            _gpu.SubmitAndWait();
            _logits.Download(_logitsHost, 0);
            return _logitsHost;
        }

        // Output head for row r of the last batched pass (its hidden state is still in _pX).
        void RecordHead(int r)
        {
            int d = C.NEmbd;
            _gpu.Record(_kCopyRange, new GpuBuffer[] { _pX, _x }, new uint[] { (uint)d, (uint)(r * d), 0 }, Groups(d, Kernels.Wg), 1, 1);
            Quant(KernelsQuant.Mode.RmsNorm, _x, _outputNorm, C.NEmbd, _output);
            MatVec(_output, _logits, true);
            if (C.Softcap > 0)
                _gpu.Record(_kSoftcap, new GpuBuffer[] { _logits }, new uint[] { (uint)C.NVocab, Bits(C.Softcap) }, Groups(C.NVocab, Kernels.Wg), 1, 1);
        }

        // ---- speculative verification ----
        bool _allRows;

        public bool PrefillRows(IList<int> tokens, int start, int count)
        {
            if (!UsePrefill || !_prefillOk || count < 1 || count > 16 || start + count > MaxCtx) return false;
            _allRows = true;
            try { PrefillChunkRun(tokens, start, count); }
            finally { _allRows = false; }
            return true;
        }

        public float[] RowLogits(int row)
        {
            _gpu.Begin();
            RecordHead(row);
            _gpu.SubmitAndWait();
            float[] r = new float[C.NVocab];
            _logits.Download(r, 0);
            return r;
        }

        // ---- Instant Recall: save the KV cache of a token range and put it back elsewhere (KernelsRecall) ----
        GpuKernel _kGather, _kScatter;
        GpuBuffer _recallUp, _recallDown;
        ulong _recallBytes;

        void EnsureRecall(ulong bytes)
        {
            if (_kGather == null)
            {
                _kGather = Kernel(KernelsRecall.Gather(), 3, 5, "recall gather");
                _kScatter = Kernel(KernelsRecall.Scatter(), 4, 8, "recall scatter");
            }
            if (bytes <= _recallBytes) return;
            if (_recallUp != null) { _owned.Remove(_recallUp); _recallUp.Dispose(); _owned.Remove(_recallDown); _recallDown.Dispose(); }
            _recallUp = Buf(bytes, BufferKind.Upload);
            _recallDown = Buf(bytes, BufferKind.Readback);
            _recallBytes = bytes;
        }

        static ulong SnapBytes(int kvDim, int m) { int mPad = (m + 1) & ~1; return (ulong)kvDim * (ulong)mPad * 2 + (ulong)m * (ulong)kvDim * 2; }

        public bool CanRecall { get { return Kv == KvType.F16; } }

        // The cache of positions pos0 .. pos0+count-1 (they must still be in every layer's ring). Sliding-window
        // layers keep only their last Ring positions, which is all a later token can attend to.
        public KvSnapshot CaptureKv(int pos0, int count)
        {
            KvSnapshot s = new KvSnapshot();
            s.Pos0 = pos0; s.Count = count;
            s.Layers = new KvSnapshot.Layer[_layers.Length];
            ulong max = 0;
            foreach (Layer l in _layers) max = Math.Max(max, SnapBytes(l.KvDim, Math.Min(count, l.Ring)));
            EnsureRecall(max);
            for (int il = 0; il < _layers.Length; il++)
            {
                Layer l = _layers[il];
                if (l.Shared)   // its cache is the source layer's, saved there (splicing it twice would rotate K twice)
                {
                    KvSnapshot.Layer none = new KvSnapshot.Layer();
                    none.Src0 = pos0; none.M = 0; none.Words = new uint[0];
                    s.Layers[il] = none;
                    continue;
                }
                int m = Math.Min(count, l.Ring), src0 = pos0 + count - m, mPad = (m + 1) & ~1;
                ulong bytes = SnapBytes(l.KvDim, m);
                _gpu.Begin();
                _gpu.Record(_kGather, new GpuBuffer[] { l.KCache, l.VCache, _recallDown }, new uint[] { (uint)l.KvDim, (uint)l.Ring, (uint)src0, (uint)m, (uint)mPad },
                    Groups((int)(bytes / 4), Kernels.Wg), 1, 1);
                _gpu.SubmitAndWait();
                KvSnapshot.Layer sl = new KvSnapshot.Layer();
                sl.Src0 = src0; sl.M = m; sl.Words = new uint[bytes / 4];
                fixed (uint* p = sl.Words) Buffer.MemoryCopy((void*)_recallDown.Mapped, p, bytes, bytes);
                s.Layers[il] = sl;
            }
            return s;
        }

        // Writes a snapshot at positions dst0 .. dst0+Count-1 of this sequence, re-rotating K by dst0 - Pos0.
        public void SpliceKv(KvSnapshot s, int dst0)
        {
            if (dst0 + s.Count > MaxCtx) throw new InvalidOperationException("Context full.");
            ulong max = 0;
            foreach (KvSnapshot.Layer sl in s.Layers) max = Math.Max(max, (ulong)sl.Words.Length * 4);
            EnsureRecall(max);
            int delta = dst0 - s.Pos0;
            float[] csSwa = _swa != null ? new float[_swa.NRot] : null, csGlobal = _global != null ? new float[_global.NRot] : null;
            if (_swa != null) CpuRef.RopeTable(delta, _swa.NRot, _swa.RopeBase, _swa.RopeFreqs, csSwa);
            if (_global != null) CpuRef.RopeTable(delta, _global.NRot, _global.RopeBase, _global.RopeFreqs, csGlobal);
            if (_recallCsSwa == null) { _recallCsSwa = Buf(4096, BufferKind.Upload); _recallCsGlobal = Buf(4096, BufferKind.Upload); }
            if (csSwa != null) _recallCsSwa.Upload(csSwa, 0);
            if (csGlobal != null) _recallCsGlobal.Upload(csGlobal, 0);
            for (int il = 0; il < _layers.Length; il++)
            {
                Layer l = _layers[il];
                if (l.Shared) continue;
                KvSnapshot.Layer sl = s.Layers[il];
                ulong bytes = (ulong)sl.Words.Length * 4;
                fixed (uint* p = sl.Words) Buffer.MemoryCopy(p, (void*)_recallUp.Mapped, bytes, bytes);
                int mPad = (sl.M + 1) & ~1, at = dst0 + (sl.Src0 - s.Pos0);
                int half = l.NRot / 2, perHead = l.HeadDim / 2;
                uint threads = (uint)(l.NHeadKv * perHead * sl.M + sl.M * l.KvDim / 2);
                _gpu.Begin();
                for (uint par = 0; par < 2; par++)
                    _gpu.Record(_kScatter, new GpuBuffer[] { _recallUp, l.Swa ? _recallCsSwa : _recallCsGlobal, l.KCache, l.VCache },
                        new uint[] { (uint)l.KvDim, (uint)l.Ring, (uint)at, (uint)sl.M, (uint)mPad, (uint)l.HeadDim, (uint)half, par }, Groups((int)threads, Kernels.Wg), 1, 1);
                _gpu.SubmitAndWait();
            }
        }
        GpuBuffer _recallCsSwa, _recallCsGlobal;

        public int ContextLength { get { return MaxCtx; } }
        public int VocabSize { get { return C.NVocab; } }

        public void Dispose()
        {
            if (_locked) { _locked = false; ModelLock.Release(); }
            for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
            _owned.Clear();
        }
    }
}
