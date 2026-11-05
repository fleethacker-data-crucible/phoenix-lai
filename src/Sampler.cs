// Token sampling: repetition penalty, temperature, top-k, top-p. C# 5 only.
// Penalty semantics follow llama_sampler_penalties (llama-sampler.cpp, MIT, llama.cpp).
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public sealed class SamplerSettings
    {
        // Defaults: Google's recommended Gemma 4 sampling (temperature 1.0, top_p 0.95, top_k 64), no repeat penalty.
        public float Temperature = 1.0f;   // <= 0: greedy
        public int TopK = 64;              // <= 0: disabled
        public float TopP = 0.95f;         // >= 1: disabled
        public float RepeatPenalty = 1.0f; // 1: disabled
        public int RepeatLastN = 64;
        public int Seed = -1;              // -1: random

        public SamplerSettings Clone() { return (SamplerSettings)MemberwiseClone(); }
    }

    public sealed class Sampler
    {
        readonly SamplerSettings _s;
        readonly Random _rng;
        float[] _work;
        int[] _idx;

        public Sampler(SamplerSettings s)
        {
            _s = s.Clone();
            _rng = _s.Seed >= 0 ? new Random(_s.Seed) : new Random();
        }

        // history: tokens seen so far (prompt + generated), used for the repetition penalty.
        public int Sample(float[] logits, IList<int> history)
        {
            int n = logits.Length;
            if (_work == null || _work.Length != n) { _work = new float[n]; _idx = new int[n]; }
            float[] l = _work;
            Array.Copy(logits, l, n);

            if (_s.RepeatPenalty != 1f && _s.RepeatLastN != 0 && history != null)
            {
                HashSet<int> seen = new HashSet<int>();
                int start = _s.RepeatLastN < 0 ? 0 : Math.Max(0, history.Count - _s.RepeatLastN);
                for (int i = start; i < history.Count; i++)
                {
                    int t = history[i];
                    if (t < 0 || t >= n || !seen.Add(t)) continue;
                    if (l[t] <= 0) l[t] *= _s.RepeatPenalty; else l[t] /= _s.RepeatPenalty;
                }
            }

            if (_s.Temperature <= 0) return CpuRef.ArgMax(l);

            // Candidates sorted by logit (descending), limited to top-k.
            int k = _s.TopK > 0 ? Math.Min(_s.TopK, n) : n;
            int count = TopCandidates(l, k);

            // Softmax with temperature over the candidates.
            double max = l[_idx[0]];
            double[] p = new double[count];
            double sum = 0;
            for (int i = 0; i < count; i++) { p[i] = Math.Exp((l[_idx[i]] - max) / _s.Temperature); sum += p[i]; }
            for (int i = 0; i < count; i++) p[i] /= sum;

            // Top-p: smallest prefix whose probability reaches TopP (at least one token).
            int keep = count;
            if (_s.TopP < 1f)
            {
                double cum = 0;
                for (int i = 0; i < count; i++)
                {
                    cum += p[i];
                    if (cum >= _s.TopP) { keep = i + 1; break; }
                }
            }

            double total = 0;
            for (int i = 0; i < keep; i++) total += p[i];
            double r = _rng.NextDouble() * total;
            for (int i = 0; i < keep; i++)
            {
                r -= p[i];
                if (r <= 0) return _idx[i];
            }
            return _idx[keep - 1];
        }

        // Fills _idx[0..k) with the indices of the k largest logits, sorted descending. Returns k.
        int TopCandidates(float[] l, int k)
        {
            int n = l.Length;
            if (k >= n)
            {
                for (int i = 0; i < n; i++) _idx[i] = i;
                Array.Sort(_idx, 0, n, new ByLogitDesc(l));
                return n;
            }
            // Keep a small sorted buffer of the best k (k is small in practice).
            int filled = 0;
            for (int i = 0; i < n; i++)
            {
                float v = l[i];
                if (filled == k && v <= l[_idx[k - 1]]) continue;
                int pos = filled < k ? filled++ : k - 1;
                while (pos > 0 && l[_idx[pos - 1]] < v) { _idx[pos] = _idx[pos - 1]; pos--; }
                _idx[pos] = i;
            }
            return filled;
        }

        sealed class ByLogitDesc : IComparer<int>
        {
            readonly float[] _l;
            public ByLogitDesc(float[] l) { _l = l; }
            public int Compare(int a, int b) { return _l[b].CompareTo(_l[a]); }
        }
    }
}
