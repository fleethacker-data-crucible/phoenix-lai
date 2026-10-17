// Phase 4 gate: CPU forward pass (GemmaCpu) vs llama.cpp on a Gemma 4 chat reply: teacher-forced log P of every reply
// token (ref\outputs\<model>\gemma-logprobs.tsv, from ref\make-gemma-logprobs.ps1). C# 5 only.
// Shared helpers (reference loader, comparison) are used by the GPU gates too.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace Phoenix.Tests
{
    public static class Phase4Gate
    {
        // Mean is the main signal; single positions near a tie can move ~0.25 from accumulation order alone.
        public const double MeanLimit = 0.03, MaxLimit = 0.3;

        public sealed class ChatRef
        {
            public List<int> Ids = new List<int>();   // prompt + llama.cpp's greedy reply
            public int PromptLen;
            public List<int> Pos = new List<int>(), Token = new List<int>(), Top1 = new List<int>();
            public List<double> LogProb = new List<double>();

            public static ChatRef Load(string path)
            {
                ChatRef r = new ChatRef();
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.TrimStart('﻿');
                    if (line.Trim().Length == 0) continue;
                    string[] f = line.Split('\t');
                    if (line.StartsWith("# ids")) { foreach (string t in f[1].Trim().Split(' ')) r.Ids.Add(int.Parse(t)); continue; }
                    if (line.StartsWith("# prompt_len")) { r.PromptLen = int.Parse(f[1].Trim()); continue; }
                    if (line.StartsWith("#") || f[2] == "nan") continue;
                    r.Pos.Add(int.Parse(f[0])); r.Token.Add(int.Parse(f[1]));
                    r.LogProb.Add(double.Parse(f[2], CultureInfo.InvariantCulture)); r.Top1.Add(int.Parse(f[3]));
                }
                return r;
            }
        }

        public delegate float[] LogitsAt(int pos);   // logits predicting token pos (all tokens before it fed)

        public static double LogProb(float[] logits, int id)
        {
            double max = double.NegativeInfinity;
            for (int i = 0; i < logits.Length; i++) if (logits[i] > max) max = logits[i];
            double z = 0;
            for (int i = 0; i < logits.Length; i++) z += Math.Exp(logits[i] - max);
            return logits[id] - max - Math.Log(z);
        }

        public static void Compare(ChatRef r, LogitsAt at, string label, List<string> fail, TextWriter output)
        {
            double worst = 0, sum = 0; int top = 0;
            for (int i = 0; i < r.Pos.Count; i++)
            {
                float[] l = at(r.Pos[i]);
                double d = Math.Abs(LogProb(l, r.Token[i]) - r.LogProb[i]);
                sum += d; worst = Math.Max(worst, d);
                if (CpuRef.ArgMax(l) == r.Top1[i]) top++;
                if (d > MaxLimit) fail.Add(label + " pos " + r.Pos[i] + ": |logprob diff| " + d.ToString("F4"));
            }
            int n = r.Pos.Count;
            double mean = n > 0 ? sum / n : double.NaN;
            output.WriteLine("{0}: {1} reply positions, mean |logprob diff| {2:F4}, max {3:F4}, top-1 agrees {4}/{1} (limits {5}, {6})",
                label, n, mean, worst, top, MeanLimit, MaxLimit);
            if (!(mean <= MeanLimit)) fail.Add(label + ": mean logprob diff " + mean.ToString("F4") + " exceeds " + MeanLimit);
            if (top < n - 1) fail.Add(label + ": top-1 agrees at only " + top + "/" + n + " positions");
        }

        // Teacher forcing through Forward: one pass over the ids, logits kept for the reference positions.
        public static LogitsAt Sequential(ILanguageModel m, ChatRef r)
        {
            Dictionary<int, float[]> byPos = new Dictionary<int, float[]>();
            HashSet<int> want = new HashSet<int>(r.Pos);
            int last = 0; foreach (int p in r.Pos) last = Math.Max(last, p);
            for (int p = 0; p < last; p++)
            {
                float[] l = m.Forward(r.Ids[p], p);
                if (want.Contains(p + 1)) byPos[p + 1] = (float[])l.Clone();
            }
            return delegate(int pos) { return byPos[pos]; };
        }

        public static List<string> Run(string modelPath, string refPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            if (!File.Exists(refPath)) { fail.Add("no reference " + refPath + " (run ref\\make-gemma-logprobs.ps1)"); return fail; }
            ChatRef r = ChatRef.Load(refPath);
            using (GgufFile g = GgufFile.Open(modelPath))
            {
                Stopwatch sw = Stopwatch.StartNew();
                GemmaCpu m = new GemmaCpu(g, r.Ids.Count + 8);
                Compare(r, Sequential(m, r), "cpu", fail, output);
                output.WriteLine("cpu: {0} tokens in {1:N1} s", r.Ids.Count, sw.Elapsed.TotalSeconds);
            }
            return fail;
        }
    }
}
