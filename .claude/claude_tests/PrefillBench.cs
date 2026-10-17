// Long-prompt prefill benchmark (Gemma 4): tok/s at several prompt lengths, per-kernel GPU time. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static class PrefillBench
    {
        public static string RefDir;
        public static string TextFile;   // real text (repeated as needed) instead of random tokens
        public static string LogprobRef; // llama.cpp gemma-logprobs.tsv: per-position logprob check

        static double LogProb(float[] l, int id)
        {
            double m = double.NegativeInfinity; foreach (float v in l) m = Math.Max(m, v);
            double s = 0; foreach (float v in l) s += Math.Exp(v - m);
            return l[id] - m - Math.Log(s);
        }

        static void CheckLogprobs(ILanguageModel m, TextWriter o)
        {
            string[] lines = File.ReadAllLines(LogprobRef);
            List<int> ids = new List<int>();
            foreach (string t in lines[0].Substring(lines[0].IndexOf('	') + 1).Split(' ')) ids.Add(int.Parse(t));
            double worst = 0, sum = 0; int n = 0, top = 0;
            foreach (string line in lines)
            {
                if (line.StartsWith("#") || line.Trim().Length == 0) continue;
                string[] f = line.Split('	');
                int pos = int.Parse(f[0]), tok = int.Parse(f[1]);
                double refLp = double.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
                m.Reset();
                float[] l = m.Prefill(ids, 0, pos);
                double d = Math.Abs(LogProb(l, tok) - refLp);
                worst = Math.Max(worst, d); sum += d; n++;
                if (CpuRef.ArgMax(l) == int.Parse(f[3])) top++;
            }
            o.WriteLine("llama.cpp logprobs: {0} positions, mean |diff| {1:G3}, max {2:G3}, top-1 agrees {3}/{0}", n, sum / n, worst, top);
        }   // logits per length: written on the first run, compared on later ones

        public static void Run(string modelPath, int[] lengths, bool profile, TextWriter o)
        {
            int max = 0;
            foreach (int n in lengths) max = Math.Max(max, n);
            using (GgufFile g = GgufFile.Open(modelPath))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                string arch = g.GetString("general.architecture");
                ILanguageModel m = new GemmaGpu(gpu, g, max + 64);
                try
                {
                    GemmaGpu gm = m as GemmaGpu;
                    if (gm != null)
                        o.WriteLine("layers {0}  heads {1}  kv heads {2}/{3}  head dim {4}/{5} (swa/global)  window {6}  shared {7} B",
                            gm.C.NLayer, gm.C.NHead, gm.C.NHeadKv[0], gm.C.NHeadKv[5], gm.C.HeadDim[0], gm.C.HeadDim[5], gm.C.NSwa, gpu.MaxSharedMemory);
                    if (LogprobRef != null) CheckLogprobs(m, o);
                    Random rng = new Random(1);
                    int[] prompt = new int[max];
                    for (int i = 0; i < max; i++) prompt[i] = 1000 + rng.Next(20000);
                    if (TextFile != null)
                    {
                        List<int> tk = new Tokenizer(g).Encode(File.ReadAllText(TextFile), true, false);
                        for (int i = 0; i < max; i++) prompt[i] = tk[i % tk.Count];
                    }
                    m.Prefill(prompt, 0, 16);   // warm-up
                    float[] first = null;
                    foreach (int n in lengths)
                    {
                        m.Reset();
                        if (profile) { gpu.ProfileMs.Clear(); gpu.ProfileCount.Clear(); gpu.Profiling = true; }
                        Stopwatch sw = Stopwatch.StartNew();
                        float[] logits = m.Prefill(prompt, 0, n);
                        sw.Stop();
                        gpu.Profiling = false;
                        if (first == null) first = (float[])logits.Clone();
                        int am = CpuRef.ArgMax(logits);
                        float[] srt = (float[])logits.Clone(); Array.Sort(srt); Array.Reverse(srt);
                        string top5 = "";
                        for (int j = 0; j < 5; j++) top5 += " " + Array.IndexOf(logits, srt[j]) + ":" + LogProb(logits, Array.IndexOf(logits, srt[j])).ToString("F4");
                        o.WriteLine("{0}  pp{1,-6} {2,8:N1} tok/s  ({3:N2} s)  top5{4}", Path.GetFileNameWithoutExtension(modelPath), n, n / sw.Elapsed.TotalSeconds, sw.Elapsed.TotalSeconds, top5);
                        if (RefDir != null)
                        {
                            string f = Path.Combine(RefDir, "logits-" + n + ".bin");
                            if (!File.Exists(f))
                            {
                                byte[] b = new byte[logits.Length * 4]; Buffer.BlockCopy(logits, 0, b, 0, b.Length); File.WriteAllBytes(f, b);
                            }
                            else
                            {
                                byte[] b = File.ReadAllBytes(f); float[] r = new float[b.Length / 4]; Buffer.BlockCopy(b, 0, r, 0, b.Length);
                                double md = 0; for (int i = 0; i < r.Length; i++) md = Math.Max(md, Math.Abs(r[i] - logits[i]));
                                o.WriteLine("    vs reference: max |logit diff| {0:G4}, argmax {1}, |logprob diff| of argmax {2:G3}", md, CpuRef.ArgMax(r) == am ? "same" : "DIFFERENT", Math.Abs(LogProb(r, am) - LogProb(logits, am)));
                            }
                        }
                        if (profile)
                        {
                            double total = 0;
                            foreach (double v in gpu.ProfileMs.Values) total += v;
                            List<KeyValuePair<string, double>> rows = new List<KeyValuePair<string, double>>(gpu.ProfileMs);
                            rows.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
                            o.WriteLine("    GPU total {0:N0} ms", total);
                            foreach (KeyValuePair<string, double> kv in rows)
                                if (kv.Value > total * 0.005) o.WriteLine("    {0,-28} {1,8:N0} ms {2,5:N1}%  ({3} dispatches)", kv.Key, kv.Value, 100 * kv.Value / total, gpu.ProfileCount[kv.Key]);
                        }
                    }
                }
                finally { m.Dispose(); }
            }
        }
    }
}
