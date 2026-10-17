// EmbeddingGemma 2 on the GPU: the whole sequence goes through each layer at once (KernelsBatch). Same graph as
// GemmaCpu.Embed; weights are widened to fp16 on upload (the model is small) and activations stay f32, so results
// match llama.cpp to ~1e-4 in cosine. C# 5 only.
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public sealed unsafe class GemmaEmbedGpu : IDisposable
    {
        public readonly GemmaConfig C;
        readonly GpuDevice _gpu;
        readonly bool _ownsGpu;
        readonly Weight _tokEmbd;
        readonly List<IDisposable> _owned = new List<IDisposable>();

        sealed class Layer
        {
            public GpuBuffer AttnNorm, QNorm, KNorm, PostAttnNorm, FfnNorm, PostFfnNorm, PlePostNorm, Wq, Wk, Wv, Wo, Gate, Up, Down, PleGate, PleProj;
            public int HeadDim, NHeadKv, KvDim, NFf, NRot, QDim;
            public bool Swa, HasV;
            public float RopeBase, OutScale = 1f;
        }

        readonly Layer[] _layers;
        readonly GpuBuffer _outputNorm, _output, _pleProj, _pleProjNorm;
        readonly GpuKernel _kMm, _kNorm, _kHeadNorm, _kRope, _kGelu, _kScale, _kCopy;
        readonly Dictionary<uint, GpuKernel> _kAttn = new Dictionary<uint, GpuKernel>();   // by head size
        readonly Dictionary<uint, uint> _attnTq = new Dictionary<uint, uint>();
        readonly int _maxQ, _maxKv, _maxFf;

        // Sequence buffers, (re)allocated for the longest sequence seen.
        int _cap;
        GpuBuffer _x, _xb, _tmp, _q, _k, _v, _attn, _g, _u, _ple, _csSwa, _csGlobal, _emb;

        public GemmaEmbedGpu(GgufFile g, GpuDevice gpu)
        {
            C = GemmaConfig.FromGguf(g);
            if (!C.Embedding) throw new NotSupportedException("Not an embedding model.");
            _ownsGpu = gpu == null;
            _gpu = gpu ?? GpuDevice.Create();
            try
            {
                _kMm = Kernel(KernelsBatch.MatMulF16(), 3, 3, "mm f16");
                _kNorm = Kernel(KernelsBatch.RowNorm(), 3, 4, "row norm");
                _kHeadNorm = Kernel(KernelsGemma.HeadNorm(), 3, 3, "head norm");
                _kRope = Kernel(KernelsBatch.RopeNeoxRows(), 2, 4, "rope rows");
                foreach (int hd in C.HeadDim)
                {
                    uint h = (uint)hd, tq, kc;
                    if (_kAttn.ContainsKey(h)) continue;
                    KernelsBatch.AttnTile(h, _gpu.MaxSharedMemory, out tq, out kc);
                    if (h % (2 * kc) != 0 || (_gpu.MaxSharedMemory != 0 && KernelsBatch.AttnSharedBytes(h, tq, kc) > _gpu.MaxSharedMemory))
                        throw new NotSupportedException("Head size " + h + " does not fit the embedder's attention kernel.");
                    _kAttn[h] = Kernel(KernelsBatch.AttentionBi(h, tq, kc), 4, 5, "attention bi " + h);
                    _attnTq[h] = tq;
                }
                _kGelu = Kernel(KernelsBatch.GeluMulRows(), 2, 4, "gelu*u rows");
                _kScale = Kernel(KernelsBatch.Scale(), 1, 2, "scale");
                _kCopy = Kernel(KernelsBatch.Copy(), 2, 1, "copy");

                _tokEmbd = new Weight(T(g, "token_embd.weight"));
                _outputNorm = F32(T(g, "output_norm.weight"));
                _output = W16(T(g, "output.weight"));
                if (C.NEmbdPerLayer > 0) { _pleProj = W16(T(g, "per_layer_model_proj.weight")); _pleProjNorm = F32(T(g, "per_layer_proj_norm.weight")); }
                _layers = new Layer[C.NLayer];
                for (int i = 0; i < C.NLayer; i++)
                {
                    string p = "blk." + i + ".";
                    Layer l = new Layer();
                    l.Swa = C.Swa[i]; l.HeadDim = C.HeadDim[i]; l.NHeadKv = C.NHeadKv[i]; l.KvDim = l.NHeadKv * l.HeadDim;
                    l.NFf = C.NFf[i]; l.NRot = C.NRot[i]; l.QDim = C.NHead * l.HeadDim;
                    l.RopeBase = l.Swa ? C.RopeBaseSwa : C.RopeBase;
                    l.AttnNorm = F32(T(g, p + "attn_norm.weight")); l.QNorm = F32(T(g, p + "attn_q_norm.weight")); l.KNorm = F32(T(g, p + "attn_k_norm.weight"));
                    l.PostAttnNorm = F32(T(g, p + "post_attention_norm.weight")); l.FfnNorm = F32(T(g, p + "ffn_norm.weight")); l.PostFfnNorm = F32(T(g, p + "post_ffw_norm.weight"));
                    l.Wq = W16(T(g, p + "attn_q.weight")); l.Wk = W16(T(g, p + "attn_k.weight"));
                    l.HasV = g.TensorsByName.ContainsKey(p + "attn_v.weight");
                    if (l.HasV) l.Wv = W16(T(g, p + "attn_v.weight"));
                    l.Wo = W16(T(g, p + "attn_output.weight"));
                    l.Gate = W16(T(g, p + "ffn_gate.weight")); l.Up = W16(T(g, p + "ffn_up.weight")); l.Down = W16(T(g, p + "ffn_down.weight"));
                    if (g.TensorsByName.ContainsKey(p + "layer_output_scale.weight")) l.OutScale = *(float*)T(g, p + "layer_output_scale.weight").Data;
                    if (C.NEmbdPerLayer > 0) { l.PleGate = W16(T(g, p + "inp_gate.weight")); l.PleProj = W16(T(g, p + "proj.weight")); l.PlePostNorm = F32(T(g, p + "post_norm.weight")); }
                    _maxQ = Math.Max(_maxQ, l.QDim); _maxKv = Math.Max(_maxKv, l.KvDim); _maxFf = Math.Max(_maxFf, l.NFf);
                    _layers[i] = l;
                }
            }
            catch { Dispose(); throw; }
        }

        static GgufTensor T(GgufFile g, string name)
        {
            GgufTensor t;
            if (!g.TensorsByName.TryGetValue(name, out t)) throw new KeyNotFoundException("Missing tensor " + name);
            return t;
        }

        GpuKernel Kernel(uint[] spirv, int bindings, int pushWords, string name)
        {
            GpuKernel k = _gpu.CreateKernel(spirv, bindings, (uint)pushWords * 4, 4096);
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

        GpuBuffer F32(GgufTensor t)
        {
            if (t.Type != GgmlType.F32) throw new NotSupportedException(t.Name + " must be f32.");
            GpuBuffer b = Buf((ulong)t.SizeBytes, BufferKind.Device);
            _gpu.Upload(b, 0, (IntPtr)t.Data, (ulong)t.SizeBytes);
            return b;
        }

        // Any supported weight -> fp16 rows (dequantized on the CPU once).
        GpuBuffer W16(GgufTensor t)
        {
            Weight w = new Weight(t);
            if (w.Cols % (int)KernelsBatch.MmK != 0) throw new NotSupportedException(t.Name + ": columns must be a multiple of " + KernelsBatch.MmK + ".");
            GpuBuffer b = Buf((ulong)w.Rows * (ulong)w.Cols * 2, BufferKind.Device);
            float[] row = new float[w.Cols];
            int rowsPer = Math.Max(1, (8 << 20) / (w.Cols * 2));
            ushort[] chunk = new ushort[(long)rowsPer * w.Cols];
            for (int r0 = 0; r0 < w.Rows; r0 += rowsPer)
            {
                int nr = Math.Min(rowsPer, w.Rows - r0);
                for (int r = 0; r < nr; r++)
                {
                    CpuRef.GetRow(w, r0 + r, row);
                    for (int j = 0; j < w.Cols; j++) chunk[(long)r * w.Cols + j] = Half.FromFloat(row[j]);
                }
                fixed (ushort* p = chunk) _gpu.Upload(b, (ulong)r0 * (ulong)w.Cols * 2, (IntPtr)p, (ulong)nr * (ulong)w.Cols * 2);
            }
            return b;
        }

        void Ensure(int n)
        {
            if (n <= _cap) return;
            foreach (GpuBuffer old in new GpuBuffer[] { _x, _xb, _tmp, _q, _k, _v, _attn, _g, _u, _ple, _csSwa, _csGlobal, _emb })
                if (old != null) { _owned.Remove(old); old.Dispose(); }
            int cap = Math.Max(n, 128);
            ulong N = (ulong)cap;
            _x = Buf(N * (ulong)C.NEmbd * 4, BufferKind.Device); _xb = Buf(N * (ulong)C.NEmbd * 4, BufferKind.Device); _tmp = Buf(N * (ulong)C.NEmbd * 4, BufferKind.Device);
            _q = Buf(N * (ulong)_maxQ * 4, BufferKind.Device); _k = Buf(N * (ulong)_maxKv * 4, BufferKind.Device); _v = Buf(N * (ulong)_maxKv * 4, BufferKind.Device);
            _attn = Buf(N * (ulong)_maxQ * 4, BufferKind.Device);
            _g = Buf(N * (ulong)_maxFf * 4, BufferKind.Device); _u = Buf(N * (ulong)_maxFf * 4, BufferKind.Device);
            _ple = Buf(N * (ulong)Math.Max(1, C.NEmbdPerLayer * C.NLayer) * 4, BufferKind.Device);
            int rotSwa = 0, rotGlobal = 0;
            foreach (Layer l in _layers) { if (l.Swa) rotSwa = l.NRot; else rotGlobal = l.NRot; }
            _csSwa = Buf(N * (ulong)Math.Max(rotSwa, 1) * 4, BufferKind.Upload); _csGlobal = Buf(N * (ulong)Math.Max(rotGlobal, 1) * 4, BufferKind.Upload);
            _emb = Buf(N * (ulong)C.NEmbdOut * 4, BufferKind.Readback);
            _cap = cap;
        }

        static uint Bits(float f) { return *(uint*)&f; }
        static uint Groups(long n, uint size) { return (uint)((n + size - 1) / size); }

        void Mm(GpuBuffer w, GpuBuffer x, GpuBuffer y, int rows, int cols, int n)
        {
            _gpu.Record(_kMm, new GpuBuffer[] { w, x, y }, new uint[] { (uint)rows, (uint)cols, (uint)n }, Groups(rows, KernelsBatch.MmRows), Groups(n, KernelsBatch.MmTokens), 1);
        }

        // Unit-length mean-pooled embedding of a token sequence (BOS/EOS included by the caller). Each layer is its own
        // short GPU submission (tens of ms) so no job comes near Windows' ~2 s GPU timeout, and the whole call holds
        // GpuGate so it never competes with the chat model for the GPU.
        public float[] Embed(IList<int> tokens)
        {
            lock (GpuGate.Lock) return EmbedLocked(tokens);
        }

        float[] EmbedLocked(IList<int> tokens)
        {
            int n = tokens.Count, d = C.NEmbd, pl = C.NEmbdPerLayer;
            if (n > KernelsBatch.MaxSeq) throw new ArgumentException("Sequence too long for the GPU embedder.");
            Ensure(n);
            // Token embeddings (scaled) and the per-position rope tables, written by the CPU.
            float[] host = new float[(long)n * d], row = new float[d];
            float sc = (float)Math.Sqrt(d);
            for (int t = 0; t < n; t++) { CpuRef.GetRow(_tokEmbd, tokens[t], row); for (int i = 0; i < d; i++) host[(long)t * d + i] = row[i] * sc; }
            GpuBuffer xUp = Buf((ulong)host.Length * 4, BufferKind.Upload);
            xUp.Upload(host, 0);
            UploadRope(_csSwa, true, n);
            UploadRope(_csGlobal, false, n);

            uint eps = Bits(C.Eps);
            _gpu.Begin();
            _gpu.Record(_kCopy, new GpuBuffer[] { xUp, _x }, new uint[] { (uint)(n * d) }, Groups((long)n * d, Kernels.Wg), 1, 1);
            if (pl > 0)
            {
                // per-layer inputs: rmsnorm(proj(x) / sqrt(d)) for every layer slice
                Mm(_pleProj, _x, _ple, pl * C.NLayer, d, n);
                _gpu.Record(_kScale, new GpuBuffer[] { _ple }, new uint[] { (uint)(n * pl * C.NLayer), Bits(1f / (float)Math.Sqrt(d)) }, Groups((long)n * pl * C.NLayer, Kernels.Wg), 1, 1);
                _gpu.Record(_kNorm, new GpuBuffer[] { _ple, _ple, _pleProjNorm }, new uint[] { (uint)pl, eps, 0, Bits(1f) }, (uint)(n * C.NLayer), 1, 1);
            }
            for (int il = 0; il < C.NLayer; il++)
            {
                Layer l = _layers[il];
                int hd = l.HeadDim;
                _gpu.Record(_kNorm, new GpuBuffer[] { _x, _xb, l.AttnNorm }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)n, 1, 1);
                Mm(l.Wq, _xb, _q, l.QDim, d, n);
                Mm(l.Wk, _xb, _k, l.KvDim, d, n);
                if (l.HasV) Mm(l.Wv, _xb, _v, l.KvDim, d, n);
                else _gpu.Record(_kCopy, new GpuBuffer[] { _k, _v }, new uint[] { (uint)(n * l.KvDim) }, Groups((long)n * l.KvDim, Kernels.Wg), 1, 1);
                _gpu.Record(_kHeadNorm, new GpuBuffer[] { _q, l.QNorm, _q }, new uint[] { (uint)hd, eps, 1 }, (uint)(n * C.NHead), 1, 1, false);
                _gpu.Record(_kHeadNorm, new GpuBuffer[] { _k, l.KNorm, _k }, new uint[] { (uint)hd, eps, 1 }, (uint)(n * l.NHeadKv), 1, 1, false);
                _gpu.Record(_kHeadNorm, new GpuBuffer[] { _v, l.KNorm, _v }, new uint[] { (uint)hd, eps, 0 }, (uint)(n * l.NHeadKv), 1, 1);
                GpuBuffer cs = l.Swa ? _csSwa : _csGlobal;
                uint half = (uint)(l.NRot / 2);
                _gpu.Record(_kRope, new GpuBuffer[] { _q, cs }, new uint[] { (uint)C.NHead, (uint)hd, half, (uint)n }, Groups((long)n * C.NHead * half, Kernels.Wg), 1, 1, false);
                _gpu.Record(_kRope, new GpuBuffer[] { _k, cs }, new uint[] { (uint)l.NHeadKv, (uint)hd, half, (uint)n }, Groups((long)n * l.NHeadKv * half, Kernels.Wg), 1, 1);
                _gpu.Record(_kAttn[(uint)hd], new GpuBuffer[] { _q, _k, _v, _attn }, new uint[] { (uint)n, (uint)hd, (uint)C.NHead, (uint)l.NHeadKv, l.Swa ? (uint)(C.NSwa / 2) : 0u }, Groups(n, _attnTq[(uint)hd]), (uint)C.NHead, 1);
                Mm(l.Wo, _attn, _tmp, d, l.QDim, n);
                _gpu.Record(_kNorm, new GpuBuffer[] { _x, _tmp, l.PostAttnNorm }, new uint[] { (uint)d, eps, 1, Bits(1f) }, (uint)n, 1, 1);
                _gpu.Record(_kNorm, new GpuBuffer[] { _x, _xb, l.FfnNorm }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)n, 1, 1);
                Mm(l.Gate, _xb, _g, l.NFf, d, n);
                Mm(l.Up, _xb, _u, l.NFf, d, n);
                _gpu.Record(_kGelu, new GpuBuffer[] { _g, _u }, new uint[] { (uint)l.NFf, (uint)n, (uint)l.NFf, 0 }, Groups((long)n * l.NFf, Kernels.Wg), 1, 1);
                Mm(l.Down, _g, _tmp, d, l.NFf, n);
                bool ple = pl > 0;
                _gpu.Record(_kNorm, new GpuBuffer[] { _x, _tmp, l.PostFfnNorm }, new uint[] { (uint)d, eps, 1, Bits(ple ? 1f : l.OutScale) }, (uint)n, 1, 1);
                if (ple)
                {
                    Mm(l.PleGate, _x, _g, pl, d, n);
                    _gpu.Record(_kGelu, new GpuBuffer[] { _g, _ple }, new uint[] { (uint)pl, (uint)n, (uint)(pl * C.NLayer), (uint)(il * pl) }, Groups((long)n * pl, Kernels.Wg), 1, 1);
                    Mm(l.PleProj, _g, _tmp, d, pl, n);
                    _gpu.Record(_kNorm, new GpuBuffer[] { _x, _tmp, l.PlePostNorm }, new uint[] { (uint)d, eps, 1, Bits(l.OutScale) }, (uint)n, 1, 1);
                }
                _gpu.SubmitAndWait();   // one submission per layer
                _gpu.Begin();
            }
            _gpu.Record(_kNorm, new GpuBuffer[] { _x, _xb, _outputNorm }, new uint[] { (uint)d, eps, 0, Bits(1f) }, (uint)n, 1, 1);
            Mm(_output, _xb, _emb, C.NEmbdOut, d, n);
            _gpu.SubmitAndWait();
            _owned.Remove(xUp); xUp.Dispose();

            float[] all = new float[(long)n * C.NEmbdOut];
            _emb.Download(all, 0);
            float[] pooled = new float[C.NEmbdOut];
            for (int t = 0; t < n; t++) for (int i = 0; i < pooled.Length; i++) pooled[i] += all[(long)t * C.NEmbdOut + i];
            double norm = 0;
            for (int i = 0; i < pooled.Length; i++) { pooled[i] /= n; norm += (double)pooled[i] * pooled[i]; }
            float inv = norm > 0 ? (float)(1.0 / Math.Sqrt(norm)) : 0f;
            for (int i = 0; i < pooled.Length; i++) pooled[i] *= inv;
            return pooled;
        }

        void UploadRope(GpuBuffer dst, bool swa, int n)
        {
            Layer l = null;
            foreach (Layer x in _layers) if (x.Swa == swa) { l = x; break; }
            if (l == null) return;
            float[] cs = new float[(long)n * l.NRot], one = new float[l.NRot];
            for (int t = 0; t < n; t++)
            {
                CpuRef.RopeTable(t, l.NRot, l.RopeBase, null, one);
                Array.Copy(one, 0, cs, (long)t * l.NRot, l.NRot);
            }
            dst.Upload(cs, 0);
        }

        public void Dispose()
        {
            for (int i = _owned.Count - 1; i >= 0; i--) _owned[i].Dispose();
            _owned.Clear();
            if (_ownsGpu && _gpu != null) _gpu.Dispose();
        }
    }
}
