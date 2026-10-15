// Llama-family forward pass on the CPU (one token at a time). C# 5 only.
// Graph follows llm_build_llama in src/models/llama.cpp (MIT, llama.cpp).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Phoenix
{
    public sealed unsafe class LlamaCpu : ILanguageModel
    {
        public readonly int NEmbd, NFf, NLayer, NHead, NHeadKv, HeadDim, NRot, NVocab, MaxCtx;
        public readonly float Eps, RopeBase;

        sealed class Layer
        {
            public float* AttnNorm, FfnNorm;
            public Weight Wq, Wk, Wv, Wo, Gate, Up, Down;
            public float[] KCache, VCache; // [MaxCtx * kvDim], values rounded through fp16 like ggml's f16 cache
        }

        readonly Weight _tokEmbd, _output;
        readonly float* _outputNorm;
        readonly float* _ropeFreqs;
        readonly Layer[] _layers;
        readonly int _kvDim;

        // Scratch buffers.
        readonly float[] _x, _xb, _q, _qHalf, _k, _v, _attn, _ffGate, _ffUp, _logits, _cs;
        readonly CpuRef.Q8Activations _qEmbd, _qFf;

        public LlamaCpu(GgufFile g, int maxCtx)
        {
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

            _tokEmbd = W(g, "token_embd.weight");
            _output = g.TensorsByName.ContainsKey("output.weight") ? W(g, "output.weight") : _tokEmbd; // tied embeddings
            NVocab = _output.Rows;
            _outputNorm = F32(g, "output_norm.weight");
            _ropeFreqs = g.TensorsByName.ContainsKey("rope_freqs.weight") ? F32(g, "rope_freqs.weight") : null;

            _layers = new Layer[NLayer];
            for (int i = 0; i < NLayer; i++)
            {
                string p = "blk." + i + ".";
                Layer l = new Layer();
                l.AttnNorm = F32(g, p + "attn_norm.weight");
                l.FfnNorm = F32(g, p + "ffn_norm.weight");
                l.Wq = W(g, p + "attn_q.weight");
                l.Wk = W(g, p + "attn_k.weight");
                l.Wv = W(g, p + "attn_v.weight");
                l.Wo = W(g, p + "attn_output.weight");
                l.Gate = W(g, p + "ffn_gate.weight");
                l.Up = W(g, p + "ffn_up.weight");
                l.Down = W(g, p + "ffn_down.weight");
                l.KCache = new float[(long)maxCtx * _kvDim];
                l.VCache = new float[(long)maxCtx * _kvDim];
                _layers[i] = l;
            }

            _x = new float[NEmbd]; _xb = new float[NEmbd];
            _q = new float[NHead * HeadDim]; _qHalf = new float[NHead * HeadDim]; _k = new float[_kvDim]; _v = new float[_kvDim];
            _attn = new float[NHead * HeadDim];
            _ffGate = new float[NFf]; _ffUp = new float[NFf];
            _logits = new float[NVocab];
            _cs = new float[NRot];
            _qEmbd = new CpuRef.Q8Activations(Math.Max(NEmbd, NHead * HeadDim));
            _qFf = new CpuRef.Q8Activations(NFf);
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

        // Diagnostics: when >= 0, Forward stops after this many layers and returns the residual stream instead of logits.
        public int DebugLayers = -1;

        // Run one token at position pos; returns logits (shared buffer, valid until the next call).
        public float[] Forward(int token, int pos)
        {
            if (pos >= MaxCtx) throw new InvalidOperationException("Context full.");
            CpuRef.GetRow(_tokEmbd, token, _x);
            CpuRef.RopeTable(pos, NRot, RopeBase, _ropeFreqs, _cs);

            int group = NHead / NHeadKv;
            float scale = 1.0f / (float)Math.Sqrt(HeadDim);

            int nRun = DebugLayers >= 0 ? Math.Min(DebugLayers, NLayer) : NLayer;
            for (int il = 0; il < nRun; il++)
            {
                Layer l = _layers[il];

                // --- attention ---
                CpuRef.RmsNorm(_x, l.AttnNorm, Eps, _xb);
                CpuRef.MatVec(l.Wq, _xb, _qEmbd, _q);
                CpuRef.MatVec(l.Wk, _xb, _qEmbd, _k);
                CpuRef.MatVec(l.Wv, _xb, _qEmbd, _v);
                CpuRef.Rope(_q, NHead, HeadDim, NRot, _cs);
                CpuRef.Rope(_k, NHeadKv, HeadDim, NRot, _cs);

                int cOff = pos * _kvDim;
                for (int i = 0; i < _kvDim; i++)
                {
                    l.KCache[cOff + i] = Half.Round(_k[i]);
                    l.VCache[cOff + i] = Half.Round(_v[i]);
                }

                // ggml's non-flash path: Q is converted to f16 for the dot with the f16 K cache.
                for (int i = 0; i < _q.Length; i++) _qHalf[i] = Half.Round(_q[i]);

                float[] kc = l.KCache, vc = l.VCache, qh = _qHalf, attn = _attn;
                int kvDim = _kvDim, headDim = HeadDim, n = pos + 1;
                Parallel.For(0, NHead, delegate(int h)
                {
                    int kvOff = (h / group) * headDim;
                    int qOff = h * headDim;
                    float[] scores = new float[n];
                    float max = float.NegativeInfinity;
                    for (int t = 0; t < n; t++)
                    {
                        int ko = t * kvDim + kvOff;
                        float s = 0;
                        for (int d = 0; d < headDim; d++) s += qh[qOff + d] * kc[ko + d];
                        s *= scale;
                        scores[t] = s;
                        if (s > max) max = s;
                    }
                    double sum = 0;
                    for (int t = 0; t < n; t++) { scores[t] = (float)Math.Exp(scores[t] - max); sum += scores[t]; }
                    float inv = (float)(1.0 / sum);
                    for (int d = 0; d < headDim; d++) attn[qOff + d] = 0;
                    for (int t = 0; t < n; t++)
                    {
                        float p = Half.Round(scores[t] * inv); // ggml converts KQ to f16 for the f16 V product
                        int vo = t * kvDim + kvOff;
                        for (int d = 0; d < headDim; d++) attn[qOff + d] += p * vc[vo + d];
                    }
                });

                CpuRef.MatVec(l.Wo, _attn, _qEmbd, _xb);
                for (int i = 0; i < NEmbd; i++) _x[i] += _xb[i];

                // --- feed-forward (SwiGLU) ---
                CpuRef.RmsNorm(_x, l.FfnNorm, Eps, _xb);
                CpuRef.MatVec(l.Gate, _xb, _qEmbd, _ffGate);
                CpuRef.MatVec(l.Up, _xb, _qEmbd, _ffUp);
                for (int i = 0; i < NFf; i++) _ffGate[i] = CpuRef.Silu(_ffGate[i]) * _ffUp[i];
                CpuRef.MatVec(l.Down, _ffGate, _qFf, _xb);
                for (int i = 0; i < NEmbd; i++) _x[i] += _xb[i];
            }

            if (DebugLayers >= 0) return (float[])_x.Clone();
            CpuRef.RmsNorm(_x, _outputNorm, Eps, _xb);
            CpuRef.MatVec(_output, _xb, _qEmbd, _logits);
            return _logits;
        }

        public int ContextLength { get { return MaxCtx; } }
        public int VocabSize { get { return NVocab; } }
        public void Reset() { }
        public void Dispose() { }

        // Greedy generation: feeds the prompt, then appends argmax tokens.
        public List<int> Greedy(IList<int> prompt, int nPredict, int stopToken)
        {
            List<int> outIds = new List<int>();
            float[] logits = null;
            for (int i = 0; i < prompt.Count; i++) logits = Forward(prompt[i], i);
            int pos = prompt.Count;
            for (int n = 0; n < nPredict; n++)
            {
                int next = CpuRef.ArgMax(logits);
                outIds.Add(next);
                if (next == stopToken) break;
                logits = Forward(next, pos++);
            }
            return outIds;
        }
    }
}
