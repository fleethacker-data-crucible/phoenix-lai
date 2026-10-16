// Phase 4 gate: CPU greedy completions vs llama.cpp (llama-completion --temp 0 -n 24). C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Phoenix.Tests
{
    public static class Phase4Gate
    {
        const int NPredict = 24;

        public static List<string> Run(string modelPath, string greedyPath, string logprobsPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            List<KeyValuePair<string, string>> cases = LoadCases(greedyPath);
            if (cases.Count == 0) fail.Add("no cases in " + greedyPath);

            using (GgufFile g = GgufFile.Open(modelPath))
            {
                Tokenizer tok = new Tokenizer(g);
                long totalTokens = 0;
                Stopwatch sw = Stopwatch.StartNew();

                foreach (KeyValuePair<string, string> c in cases)
                {
                    LlamaCpu model = new LlamaCpu(g, 128);
                    List<int> prompt = tok.Encode(c.Key, true, true);
                    List<int> gen = model.Greedy(prompt, NPredict, tok.EosId);
                    totalTokens += prompt.Count + gen.Count;

                    string got = (c.Key + tok.Decode(gen, false)).Trim();
                    string want = c.Value.Trim();
                    if (got == want)
                    {
                        output.WriteLine("  ok   '{0}'", c.Key);
                    }
                    else if (IsNearTie(g, tok, prompt, gen, want, output))
                    {
                        output.WriteLine("  tie  '{0}'", c.Key);
                    }
                    else
                    {
                        int same = 0;
                        while (same < got.Length && same < want.Length && got[same] == want[same]) same++;
                        fail.Add("'" + c.Key + "' diverges at char " + same + ":\n      llama.cpp: " + Show(want) + "\n      ours:      " + Show(got));
                        output.WriteLine("  FAIL '{0}'", c.Key);
                    }
                }
                sw.Stop();
                CompareLogprobs(tok, cases, logprobsPath, delegate { return new CpuForward(new LlamaCpu(g, 128)); }, fail, output);
                output.WriteLine("greedy: {0} cases, {1} forward passes in {2:N1} s ({3:N2} tokens/s on CPU)",
                    cases.Count, totalTokens, sw.Elapsed.TotalSeconds, totalTokens / sw.Elapsed.TotalSeconds);
            }
            return fail;
        }

        // Accumulation order differs from llama.cpp (it evaluates the prompt as a batch), so a near-tie
        // can flip. Accept a divergence only if llama.cpp's token is within TieLogits of our top choice there.
        const float TieLogits = 0.25f;

        static bool IsNearTie(GgufFile g, Tokenizer tok, List<int> prompt, List<int> gen, string want, TextWriter output)
        {
            List<int> ours = new List<int>(prompt); ours.AddRange(gen);
            List<int> theirs = tok.Encode(want, true, true);
            int k = 0;
            while (k < ours.Count && k < theirs.Count && ours[k] == theirs[k]) k++;
            if (k < prompt.Count || k >= ours.Count || k >= theirs.Count) return false;

            LlamaCpu model = new LlamaCpu(g, 128);
            float[] logits = null;
            for (int i = 0; i < k; i++) logits = model.Forward(ours[i], i);
            float gap = logits[ours[k]] - logits[theirs[k]];
            output.WriteLine("       token {0}: ours '{1}' {2:N4} vs llama.cpp '{3}' {4:N4} (gap {5:N4})",
                k, tok.Decode(new int[] { ours[k] }, false), logits[ours[k]],
                tok.Decode(new int[] { theirs[k] }, false), logits[theirs[k]], gap);
            return gap >= 0 && gap < TieLogits;
        }

        // Teacher-forced log-probabilities vs llama.cpp at every reference position.
        // Mean is the main signal; single positions near a tie can move ~0.25 from accumulation order alone.
        const double MaxLogprobDiff = 0.3, MeanLogprobDiff = 0.03;

        // A fresh model (empty KV cache) per case.
        public delegate IForward ModelFactory();

        public interface IForward { float[] Forward(int token, int pos); }

        public sealed class CpuForward : IForward
        {
            readonly LlamaCpu _m;
            public CpuForward(LlamaCpu m) { _m = m; }
            public float[] Forward(int token, int pos) { return _m.Forward(token, pos); }
        }

        public static List<KeyValuePair<string, string>> Cases(string greedyPath) { return LoadCases(greedyPath); }

        public static void CompareLogprobs(Tokenizer tok, List<KeyValuePair<string, string>> cases, string path, ModelFactory newModel, List<string> fail, TextWriter output)
        {
            CompareLogprobs(tok, cases, path, newModel, MeanLogprobDiff, MaxLogprobDiff, fail, output);
        }

        public static void CompareLogprobs(Tokenizer tok, List<KeyValuePair<string, string>> cases, string path, ModelFactory newModel,
            double meanLimit, double maxLimit, List<string> fail, TextWriter output)
        {
            Dictionary<int, List<double[]>> byCase = new Dictionary<int, List<double[]>>(); // {pos, token, logprob}
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.TrimStart('﻿');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] f = line.Split('\t');
                int c = int.Parse(f[0]);
                if (!byCase.ContainsKey(c)) byCase[c] = new List<double[]>();
                byCase[c].Add(new double[] { int.Parse(f[1]), int.Parse(f[2]), double.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture) });
            }

            double worst = 0, sum = 0; int n = 0;
            for (int c = 0; c < cases.Count; c++)
            {
                List<double[]> refs;
                if (!byCase.TryGetValue(c, out refs)) { fail.Add("no logprob reference for case " + c); continue; }
                List<int> ids = tok.Encode(cases[c].Value.Trim(), true, true);
                IForward model = newModel();
                double[] lp = new double[ids.Count];
                for (int p = 0; p + 1 < ids.Count; p++)
                {
                    float[] logits = model.Forward(ids[p], p);
                    double max = double.NegativeInfinity;
                    for (int i = 0; i < logits.Length; i++) if (logits[i] > max) max = logits[i];
                    double z = 0;
                    for (int i = 0; i < logits.Length; i++) z += Math.Exp(logits[i] - max);
                    lp[p + 1] = logits[ids[p + 1]] - max - Math.Log(z);
                }
                foreach (double[] r in refs)
                {
                    int pos = (int)r[0];
                    if (pos >= ids.Count || ids[pos] != (int)r[1]) { fail.Add("case " + c + " pos " + pos + ": token mismatch with reference"); continue; }
                    double d = Math.Abs(lp[pos] - r[2]);
                    sum += d; n++;
                    if (d > worst) worst = d;
                    if (d > maxLimit) fail.Add("case " + c + " pos " + pos + ": logprob " + lp[pos].ToString("F4") + " vs llama.cpp " + r[2].ToString("F4"));
                }
            }
            double mean = n > 0 ? sum / n : double.NaN;
            output.WriteLine("logprobs: {0} positions, mean |diff| {1:F4}, max |diff| {2:F4} (limits {3}, {4})", n, mean, worst, meanLimit.ToString("G3"), maxLimit.ToString("G3"));
            if (!(mean <= meanLimit)) fail.Add("mean logprob diff " + mean.ToString("F4") + " exceeds " + meanLimit.ToString("G3"));
        }

        static string Show(string s)
        {
            return s.Replace("\r", "\\r").Replace("\n", "\\n");
        }

        // "### PROMPT: <p>" followed by the full reference text (prompt + completion) until the next header.
        static List<KeyValuePair<string, string>> LoadCases(string path)
        {
            List<KeyValuePair<string, string>> cases = new List<KeyValuePair<string, string>>();
            string prompt = null;
            StringBuilder body = new StringBuilder();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.TrimStart('\uFEFF');
                if (line.StartsWith("### PROMPT: "))
                {
                    if (prompt != null) cases.Add(new KeyValuePair<string, string>(prompt, body.ToString()));
                    prompt = line.Substring("### PROMPT: ".Length);
                    body.Length = 0;
                    continue;
                }
                if (prompt != null) { if (body.Length > 0) body.Append('\n'); body.Append(line); }
            }
            if (prompt != null) cases.Add(new KeyValuePair<string, string>(prompt, body.ToString()));
            return cases;
        }
    }
}
