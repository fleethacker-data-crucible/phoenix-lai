// Gemma 4 (architecture "gemma4", text) and EmbeddingGemma 2 ("gemma-embedding2") on the CPU. C# 5 only.
// Graphs follow src/models/gemma4.cpp and gemma-embedding2.cpp in llama.cpp (MIT):
//  - embeddings scaled by sqrt(n_embd); per layer: RMSNorm -> Q/K/V (V = K when a layer has no V weight) with
//    per-head RMSNorm on Q and K (weighted) and V (plain), NeoX RoPE, attention with scale 1, post-attention norm,
//    GELU-gated FFN with pre and post norms, optional per-layer input (PLE), then the layer output scale;
//  - layers alternate sliding-window (local, head 256, rope base 1e4) and global (head 512, rope base 1e6 with
//    "proportional" rope_freqs factors); final norm, tied output, logit softcap.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Phoenix
{
    public sealed unsafe class GemmaConfig
    {
        public string Arch;
        public bool Embedding;            // gemma-embedding2: bidirectional, symmetric window, mean pooling + projection
        public int NEmbd, NLayer, NHead, NVocab, NSwa, NEmbdPerLayer, NEmbdOut;
        public int[] NHeadKv, NFf, HeadDim, NRot;
        public bool[] Swa;
        public float Eps, RopeBase, RopeBaseSwa, Softcap;

        public static bool IsGemma(string arch) { return arch == "gemma4" || arch == "gemma-embedding2"; }

        public static GemmaConfig FromGguf(GgufFile g)
        {
            GemmaConfig c = new GemmaConfig();
            string a = c.Arch = g.GetString("general.architecture");
            if (!IsGemma(a)) throw new NotSupportedException("Not a Gemma model: " + a);
            c.Embedding = a == "gemma-embedding2";
            c.NEmbd = (int)g.GetInt(a + ".embedding_length");
            c.NLayer = (int)g.GetInt(a + ".block_count");
            c.NHead = (int)g.GetInt(a + ".attention.head_count");
            c.NHeadKv = PerLayer(g, a + ".attention.head_count_kv", c.NLayer);
            c.NFf = PerLayer(g, a + ".feed_forward_length", c.NLayer);
            c.Swa = new bool[c.NLayer];
            object pat = g.Metadata[a + ".attention.sliding_window_pattern"].Value;
            Array pa = (Array)pat;
            for (int i = 0; i < c.NLayer; i++) c.Swa[i] = Convert.ToBoolean(pa.GetValue(i));
            c.NSwa = (int)g.GetInt(a + ".attention.sliding_window");
            int hd = (int)g.GetInt(a + ".attention.key_length"), hdSwa = (int)g.GetInt(a + ".attention.key_length_swa");
            int rot = (int)g.GetInt(a + ".rope.dimension_count");
            int rotSwa = g.Has(a + ".rope.dimension_count_swa") ? (int)g.GetInt(a + ".rope.dimension_count_swa") : hdSwa;
            c.HeadDim = new int[c.NLayer]; c.NRot = new int[c.NLayer];
            for (int i = 0; i < c.NLayer; i++) { c.HeadDim[i] = c.Swa[i] ? hdSwa : hd; c.NRot[i] = c.Swa[i] ? rotSwa : rot; }
            c.Eps = g.GetFloat(a + ".attention.layer_norm_rms_epsilon");
            c.RopeBase = g.GetFloat(a + ".rope.freq_base");
            c.RopeBaseSwa = g.Has(a + ".rope.freq_base_swa") ? g.GetFloat(a + ".rope.freq_base_swa") : 10000f;
            c.Softcap = g.Has(a + ".final_logit_softcapping") ? g.GetFloat(a + ".final_logit_softcapping") : 0f;
            c.NEmbdPerLayer = g.Has(a + ".embedding_length_per_layer_input") ? (int)g.GetInt(a + ".embedding_length_per_layer_input") : 0;
            c.NEmbdOut = g.Has(a + ".embedding_length_out") ? (int)g.GetInt(a + ".embedding_length_out") : 0;
            if (g.Has(a + ".attention.shared_kv_layers") && g.GetInt(a + ".attention.shared_kv_layers") != 0)
                throw new NotSupportedException("Gemma 4 models with shared KV layers (E2B/E4B) are not supported yet.");
            if (g.TensorsByName.ContainsKey("blk.0.ffn_gate_inp.weight"))
                throw new NotSupportedException("The Gemma 4 mixture-of-experts model (26B A4B) is not supported yet.");
            return c;
        }

        static int[] PerLayer(GgufFile g, string key, int n)
        {
            object v = g.Metadata[key].Value;
            int[] r = new int[n];
            Array arr = v as Array;
            for (int i = 0; i < n; i++) r[i] = arr != null ? Convert.ToInt32(arr.GetValue(i)) : Convert.ToInt32(v);
            return r;
        }
    }

    public sealed unsafe class GemmaCpu : ILanguageModel
    {
        public readonly GemmaConfig C;
        public readonly int MaxCtx;

        sealed class Layer
        {
            public float* AttnNorm, QNorm, KNorm, PostAttnNorm, FfnNorm, PostFfnNorm, PlePostNorm, RopeFreqs;
            public float OutScale = 1f;
            public Weight Wq, Wk, Wv, Wo, Gate, Up, Down, PleGate, PleProj;
            public int HeadDim, NHeadKv, KvDim, NFf, NRot;
            public float RopeBase;
            public bool Swa;
            public ushort[] KCache, VCache;   // [MaxCtx * KvDim] fp16, like ggml's f16 cache
        }

        readonly Weight _tokEmbd, _output, _pleModelProj;
        readonly float* _outputNorm, _pleProjNorm;
        readonly Layer[] _layers;
        readonly float _embdScale;

        // Scratch (sized for the largest layer).
        readonly float[] _x, _xb, _xb2, _q, _qHalf, _k, _v, _attn, _ffGate, _ffUp, _logits, _cs, _pleIn, _pleGate;
        readonly CpuRef.Q8Activations _qEmbd, _qWide;

        public GemmaCpu(GgufFile g, int maxCtx)
        {
            C = GemmaConfig.FromGguf(g);
            MaxCtx = maxCtx;
            _embdScale = (float)Math.Sqrt(C.NEmbd);
            _tokEmbd = W(g, "token_embd.weight");
            C.NVocab = _tokEmbd.Rows;
            _output = g.TensorsByName.ContainsKey("output.weight") ? W(g, "output.weight") : _tokEmbd;
            _outputNorm = F32(g, "output_norm.weight");
            if (C.NEmbdPerLayer > 0)
            {
                if (g.TensorsByName.ContainsKey("per_layer_token_embd.weight"))
                    throw new NotSupportedException("Per-layer token embeddings (Gemma 4 E2B/E4B) are not supported yet.");
                _pleModelProj = W(g, "per_layer_model_proj.weight");
                _pleProjNorm = F32(g, "per_layer_proj_norm.weight");
            }
            float* ropeFreqs = g.TensorsByName.ContainsKey("rope_freqs.weight") ? F32(g, "rope_freqs.weight") : null;

            _layers = new Layer[C.NLayer];
            int maxQ = 0, maxKv = 0, maxFf = 0, maxRot = 0;
            for (int i = 0; i < C.NLayer; i++)
            {
                string p = "blk." + i + ".";
                Layer l = new Layer();
                l.Swa = C.Swa[i]; l.HeadDim = C.HeadDim[i]; l.NHeadKv = C.NHeadKv[i]; l.KvDim = l.NHeadKv * l.HeadDim;
                l.NFf = C.NFf[i]; l.NRot = C.NRot[i];
                l.RopeBase = l.Swa ? C.RopeBaseSwa : C.RopeBase;
                // gemma4 global layers use proportional RoPE factors; EmbeddingGemma 2 does not.
                l.RopeFreqs = !l.Swa && !C.Embedding ? ropeFreqs : null;
                l.AttnNorm = F32(g, p + "attn_norm.weight");
                l.QNorm = F32(g, p + "attn_q_norm.weight");
                l.KNorm = F32(g, p + "attn_k_norm.weight");
                l.PostAttnNorm = F32(g, p + "post_attention_norm.weight");
                l.FfnNorm = F32(g, p + "ffn_norm.weight");
                l.PostFfnNorm = F32(g, p + "post_ffw_norm.weight");
                l.Wq = W(g, p + "attn_q.weight");
                l.Wk = W(g, p + "attn_k.weight");
                l.Wv = g.TensorsByName.ContainsKey(p + "attn_v.weight") ? W(g, p + "attn_v.weight") : null;   // null: V = K
                l.Wo = W(g, p + "attn_output.weight");
                l.Gate = W(g, p + "ffn_gate.weight");
                l.Up = W(g, p + "ffn_up.weight");
                l.Down = W(g, p + "ffn_down.weight");
                if (g.TensorsByName.ContainsKey(p + "layer_output_scale.weight")) l.OutScale = *F32(g, p + "layer_output_scale.weight");
                if (C.NEmbdPerLayer > 0)
                {
                    l.PleGate = W(g, p + "inp_gate.weight");
                    l.PleProj = W(g, p + "proj.weight");
                    l.PlePostNorm = F32(g, p + "post_norm.weight");
                }
                if (!C.Embedding)
                {
                    l.KCache = new ushort[(long)maxCtx * l.KvDim];
                    l.VCache = new ushort[(long)maxCtx * l.KvDim];
                }
                maxQ = Math.Max(maxQ, C.NHead * l.HeadDim); maxKv = Math.Max(maxKv, l.KvDim);
                maxFf = Math.Max(maxFf, l.NFf); maxRot = Math.Max(maxRot, l.NRot);
                _layers[i] = l;
            }

            _x = new float[C.NEmbd]; _xb = new float[C.NEmbd]; _xb2 = new float[C.NEmbd];
            _q = new float[maxQ]; _qHalf = new float[maxQ]; _k = new float[maxKv]; _v = new float[maxKv]; _attn = new float[maxQ];
            _ffGate = new float[maxFf]; _ffUp = new float[maxFf];
            _logits = C.Embedding ? null : new float[C.NVocab];
            _cs = new float[maxRot];
            _pleIn = C.NEmbdPerLayer > 0 ? new float[C.NEmbdPerLayer * C.NLayer] : null;
            _pleGate = C.NEmbdPerLayer > 0 ? new float[C.NEmbdPerLayer] : null;
            _qEmbd = new CpuRef.Q8Activations(Math.Max(C.NEmbd, maxQ));
            _qWide = new CpuRef.Q8Activations(maxFf);
        }

        static Weight W(GgufFile g, string name)
        {
            GgufTensor t;
            if (!g.TensorsByName.TryGetValue(name, out t)) throw new KeyNotFoundException("Missing tensor " + name);
            return new Weight(t);
        }

        static float* F32(GgufFile g, string name)
        {
            GgufTensor t;
            if (!g.TensorsByName.TryGetValue(name, out t)) throw new KeyNotFoundException("Missing tensor " + name);
            if (t.Type != GgmlType.F32) throw new NotSupportedException(name + " must be f32.");
            return (float*)t.Data;
        }

        // ---------------- chat model: one token at a time ----------------

        public float[] Forward(int token, int pos)
        {
            if (C.Embedding) throw new InvalidOperationException("EmbeddingGemma has no language-model head; use Embed().");
            if (pos >= MaxCtx) throw new InvalidOperationException("Context full.");
            CpuRef.GetRow(_tokEmbd, token, _x);
            for (int i = 0; i < C.NEmbd; i++) _x[i] *= _embdScale;

            for (int il = 0; il < C.NLayer; il++)
            {
                Layer l = _layers[il];
                int hd = l.HeadDim, nq = C.NHead * hd;
                float[] q = Exact(_q, nq), k = Exact(_k, l.KvDim), v = Exact(_v, l.KvDim), attn = Exact(_attn, nq);

                CpuRef.RmsNorm(_x, l.AttnNorm, C.Eps, _xb);
                CpuRef.MatVec(l.Wq, _xb, _qEmbd, q);
                CpuRef.MatVec(l.Wk, _xb, _qEmbd, k);
                if (l.Wv != null) CpuRef.MatVec(l.Wv, _xb, _qEmbd, v); else Array.Copy(k, v, l.KvDim);
                for (int h = 0; h < C.NHead; h++) CpuRef.RmsNormInPlace(q, h * hd, hd, l.QNorm, C.Eps);
                for (int h = 0; h < l.NHeadKv; h++) { CpuRef.RmsNormInPlace(k, h * hd, hd, l.KNorm, C.Eps); CpuRef.RmsNormPlain(v, h * hd, hd, C.Eps); }
                float[] cs = Exact(_cs, l.NRot);
                CpuRef.RopeTable(pos, l.NRot, l.RopeBase, l.RopeFreqs, cs);
                CpuRef.RopeNeox(q, C.NHead, hd, l.NRot, cs);
                CpuRef.RopeNeox(k, l.NHeadKv, hd, l.NRot, cs);

                long cOff = (long)pos * l.KvDim;
                for (int i = 0; i < l.KvDim; i++) { l.KCache[cOff + i] = Half.FromFloat(k[i]); l.VCache[cOff + i] = Half.FromFloat(v[i]); }
                float[] qh = Exact(_qHalf, nq);
                for (int i = 0; i < nq; i++) qh[i] = Half.Round(q[i]);   // ggml's non-flash path: Q in f16 against the f16 K cache

                int first = l.Swa ? Math.Max(0, pos - C.NSwa + 1) : 0;   // window: the last NSwa positions including this one
                Attend(l, qh, attn, first, pos);

                CpuRef.MatVec(l.Wo, attn, _qEmbd, _xb);
                PostAndFfn(l, il);
            }

            CpuRef.RmsNorm(_x, _outputNorm, C.Eps, _xb);
            CpuRef.MatVec(_output, _xb, _qEmbd, _logits);
            if (C.Softcap > 0)
                for (int i = 0; i < _logits.Length; i++) _logits[i] = (float)Math.Tanh(_logits[i] / C.Softcap) * C.Softcap;
            return _logits;
        }

        // Scores (scale 1: q/k are RMS-normalised), softmax, then V; KQ goes through f16 for the f16 V product like ggml.
        void Attend(Layer l, float[] qh, float[] attn, int first, int last)
        {
            int hd = l.HeadDim, kvDim = l.KvDim, group = C.NHead / l.NHeadKv, n = last - first + 1;
            ushort[] kc = l.KCache, vc = l.VCache;
            Parallel.For(0, C.NHead, delegate(int h)
            {
                int kvOff = (h / group) * hd, qOff = h * hd;
                float[] scores = new float[n];
                float max = float.NegativeInfinity;
                for (int t = 0; t < n; t++)
                {
                    long ko = (long)(first + t) * kvDim + kvOff;
                    float s = 0;
                    for (int d = 0; d < hd; d++) s += qh[qOff + d] * Half.ToFloat(kc[ko + d]);
                    scores[t] = s;
                    if (s > max) max = s;
                }
                double sum = 0;
                for (int t = 0; t < n; t++) { scores[t] = (float)Math.Exp(scores[t] - max); sum += scores[t]; }
                float inv = (float)(1.0 / sum);
                for (int d = 0; d < hd; d++) attn[qOff + d] = 0;
                for (int t = 0; t < n; t++)
                {
                    float p = Half.Round(scores[t] * inv);
                    long vo = (long)(first + t) * kvDim + kvOff;
                    for (int d = 0; d < hd; d++) attn[qOff + d] += p * Half.ToFloat(vc[vo + d]);
                }
            });
        }

        // _xb holds the attention output projection; finishes the layer on _x.
        void PostAndFfn(Layer l, int il)
        {
            // attn_out = x + norm(attn)
            CpuRef.RmsNorm(_xb, l.PostAttnNorm, C.Eps, _xb2);
            for (int i = 0; i < C.NEmbd; i++) _x[i] += _xb2[i];
            // ffn: down(gelu(gate) * up), post-norm, residual
            float[] gate = Exact(_ffGate, l.NFf), up = Exact(_ffUp, l.NFf);
            CpuRef.RmsNorm(_x, l.FfnNorm, C.Eps, _xb);
            CpuRef.MatVec(l.Gate, _xb, _qEmbd, gate);
            CpuRef.MatVec(l.Up, _xb, _qEmbd, up);
            for (int i = 0; i < l.NFf; i++) gate[i] = CpuRef.Gelu(gate[i]) * up[i];
            CpuRef.MatVec(l.Down, gate, _qWide, _xb);
            CpuRef.RmsNorm(_xb, l.PostFfnNorm, C.Eps, _xb2);
            for (int i = 0; i < C.NEmbd; i++) _x[i] += _xb2[i];
            if (l.OutScale != 1f) for (int i = 0; i < C.NEmbd; i++) _x[i] *= l.OutScale;
        }

        // The scratch arrays are allocated at the largest layer size; MatVec wants exact lengths, so keep per-size arrays.
        readonly Dictionary<long, float[]> _exact = new Dictionary<long, float[]>();
        float[] Exact(float[] buf, int n)
        {
            if (buf.Length == n) return buf;
            long key = ((long)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(buf) << 32) | (uint)n;
            float[] a;
            if (!_exact.TryGetValue(key, out a)) { a = new float[n]; _exact[key] = a; }
            return a;
        }

        public float[] Prefill(IList<int> tokens, int start, int count)
        {
            float[] logits = null;
            for (int i = start; i < start + count; i++) logits = Forward(tokens[i], i);
            return logits;
        }

        // Instant Recall is implemented for GemmaGpu (fp16 cache) only.
        public bool CanRecall { get { return false; } }
        public KvSnapshot CaptureKv(int pos0, int count) { throw new NotSupportedException(); }
        public void SpliceKv(KvSnapshot s, int dst0) { throw new NotSupportedException(); }

        public int ContextLength { get { return MaxCtx; } }
        public int VocabSize { get { return C.NVocab; } }
        public void Reset() { }
        public int MinReuse(int fed) { return 0; }
        public void Dispose() { }

        // ---------------- embedding model: whole sequence, bidirectional ----------------

        // Mean-pooled, L2-normalised embedding (NEmbdOut values) of a token sequence (BOS/EOS included by the caller).
        // Every weight is applied to the whole sequence at once (CpuRef.MatMul), which is what makes this fast enough.
        public float[] Embed(IList<int> tokens)
        {
            if (!C.Embedding) throw new InvalidOperationException("Not an embedding model.");
            int n = tokens.Count, d = C.NEmbd, pl = C.NEmbdPerLayer;
            float[][] xs = Rows(n, d), xb = Rows(n, d), tmp = Rows(n, d), tmp2 = Rows(n, d);
            for (int t = 0; t < n; t++)
            {
                CpuRef.GetRow(_tokEmbd, tokens[t], xs[t]);
                for (int i = 0; i < d; i++) xs[t][i] *= _embdScale;
            }
            float[][] ple = PerLayerInputs(xs);

            for (int il = 0; il < C.NLayer; il++)
            {
                Layer l = _layers[il];
                int hd = l.HeadDim, nq = C.NHead * hd;
                float[][] qs = Rows(n, nq), ks = Rows(n, l.KvDim), vs = Rows(n, l.KvDim), outs = Rows(n, nq);
                for (int t = 0; t < n; t++) CpuRef.RmsNorm(xs[t], l.AttnNorm, C.Eps, xb[t]);
                CpuRef.MatMul(l.Wq, xb, qs);
                CpuRef.MatMul(l.Wk, xb, ks);
                if (l.Wv != null) CpuRef.MatMul(l.Wv, xb, vs); else for (int t = 0; t < n; t++) Array.Copy(ks[t], vs[t], l.KvDim);
                Parallel.For(0, n, delegate(int t)
                {
                    for (int h = 0; h < C.NHead; h++) CpuRef.RmsNormInPlace(qs[t], h * hd, hd, l.QNorm, C.Eps);
                    for (int h = 0; h < l.NHeadKv; h++) { CpuRef.RmsNormInPlace(ks[t], h * hd, hd, l.KNorm, C.Eps); CpuRef.RmsNormPlain(vs[t], h * hd, hd, C.Eps); }
                    float[] cs = new float[l.NRot];
                    CpuRef.RopeTable(t, l.NRot, l.RopeBase, l.RopeFreqs, cs);
                    CpuRef.RopeNeox(qs[t], C.NHead, hd, l.NRot, cs);
                    CpuRef.RopeNeox(ks[t], l.NHeadKv, hd, l.NRot, cs);
                });
                // Bidirectional attention; sliding layers see a symmetric window of +-NSwa/2 (f32 throughout, no cache).
                int half = C.NSwa / 2, group = C.NHead / l.NHeadKv;
                Parallel.For(0, n, delegate(int t)
                {
                    float[] o = outs[t];
                    int lo = l.Swa ? Math.Max(0, t - half) : 0, hi = l.Swa ? Math.Min(n - 1, t + half) : n - 1;
                    float[] sc = new float[hi - lo + 1];
                    for (int h = 0; h < C.NHead; h++)
                    {
                        int qo = h * hd, ko = (h / group) * hd;
                        float max = float.NegativeInfinity;
                        fixed (float* q = &qs[t][qo])
                        {
                            for (int j = lo; j <= hi; j++)
                            {
                                fixed (float* k = &ks[j][ko])
                                {
                                    float s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                                    for (int e = 0; e < hd; e += 4) { s0 += q[e] * k[e]; s1 += q[e + 1] * k[e + 1]; s2 += q[e + 2] * k[e + 2]; s3 += q[e + 3] * k[e + 3]; }
                                    float sum0 = (s0 + s1) + (s2 + s3);
                                    sc[j - lo] = sum0; if (sum0 > max) max = sum0;
                                }
                            }
                        }
                        double sum = 0;
                        for (int j = lo; j <= hi; j++) { sc[j - lo] = (float)Math.Exp(sc[j - lo] - max); sum += sc[j - lo]; }
                        float inv = (float)(1.0 / sum);
                        fixed (float* op = &o[qo])
                        {
                            for (int j = lo; j <= hi; j++)
                            {
                                float p = sc[j - lo] * inv;
                                fixed (float* v = &vs[j][ko])
                                    for (int e = 0; e < hd; e++) op[e] += p * v[e];
                            }
                        }
                    }
                });
                // attn_out = x + norm(Wo attn)
                CpuRef.MatMul(l.Wo, outs, tmp);
                for (int t = 0; t < n; t++) { CpuRef.RmsNorm(tmp[t], l.PostAttnNorm, C.Eps, tmp2[t]); for (int i = 0; i < d; i++) xs[t][i] += tmp2[t][i]; }
                // ffn
                float[][] gate = Rows(n, l.NFf), up = Rows(n, l.NFf);
                for (int t = 0; t < n; t++) CpuRef.RmsNorm(xs[t], l.FfnNorm, C.Eps, xb[t]);
                CpuRef.MatMul(l.Gate, xb, gate);
                CpuRef.MatMul(l.Up, xb, up);
                Parallel.For(0, n, delegate(int t) { for (int i = 0; i < l.NFf; i++) gate[t][i] = CpuRef.Gelu(gate[t][i]) * up[t][i]; });
                CpuRef.MatMul(l.Down, gate, tmp);
                for (int t = 0; t < n; t++) { CpuRef.RmsNorm(tmp[t], l.PostFfnNorm, C.Eps, tmp2[t]); for (int i = 0; i < d; i++) xs[t][i] += tmp2[t][i]; }
                // per-layer input: x += norm(proj(gelu(gate(x)) * ple))
                if (pl > 0)
                {
                    float[][] pg = Rows(n, pl);
                    CpuRef.MatMul(l.PleGate, xs, pg);
                    for (int t = 0; t < n; t++) for (int i = 0; i < pl; i++) pg[t][i] = CpuRef.Gelu(pg[t][i]) * ple[t][il * pl + i];
                    CpuRef.MatMul(l.PleProj, pg, tmp);
                    for (int t = 0; t < n; t++) { CpuRef.RmsNorm(tmp[t], l.PlePostNorm, C.Eps, tmp2[t]); for (int i = 0; i < d; i++) xs[t][i] += tmp2[t][i]; }
                }
                if (l.OutScale != 1f) for (int t = 0; t < n; t++) for (int i = 0; i < d; i++) xs[t][i] *= l.OutScale;
            }

            // final norm and projection per token, then mean pooling and L2 normalisation
            for (int t = 0; t < n; t++) CpuRef.RmsNorm(xs[t], _outputNorm, C.Eps, xb[t]);
            float[][] emb = Rows(n, C.NEmbdOut);
            CpuRef.MatMul(_output, xb, emb);
            float[] pooled = new float[C.NEmbdOut];
            for (int t = 0; t < n; t++) for (int i = 0; i < pooled.Length; i++) pooled[i] += emb[t][i];
            double norm = 0;
            for (int i = 0; i < pooled.Length; i++) { pooled[i] /= n; norm += (double)pooled[i] * pooled[i]; }
            float invn = norm > 0 ? (float)(1.0 / Math.Sqrt(norm)) : 0f;
            for (int i = 0; i < pooled.Length; i++) pooled[i] *= invn;
            return pooled;
        }

        static float[][] Rows(int n, int m)
        {
            float[][] r = new float[n][];
            for (int i = 0; i < n; i++) r[i] = new float[m];
            return r;
        }

        // EmbeddingGemma 2 per-layer inputs: RMSNorm(proj(x) / sqrt(n_embd)) per layer slice, for every token.
        float[][] PerLayerInputs(float[][] xs)
        {
            int pl = C.NEmbdPerLayer;
            if (pl == 0) return null;
            float[][] all = Rows(xs.Length, pl * C.NLayer);
            CpuRef.MatMul(_pleModelProj, xs, all);
            float s = 1.0f / (float)Math.Sqrt(C.NEmbd);
            Parallel.For(0, xs.Length, delegate(int t)
            {
                for (int i = 0; i < all[t].Length; i++) all[t][i] *= s;
                for (int il = 0; il < C.NLayer; il++) CpuRef.RmsNormInPlace(all[t], il * pl, pl, _pleProjNorm, C.Eps);
            });
            return all;
        }
    }
}
