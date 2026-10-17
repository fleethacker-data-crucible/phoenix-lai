// Speed benchmark mirroring llama-bench: pp512 (prompt tokens/s) and tg128 (generated tokens/s). C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static class Bench
    {
        public sealed class Result
        {
            public string Model;
            public double PromptTps, GenTps, LoadSeconds;
            public long WeightMB;
        }

        public static bool Profile;

        public static Result Run(string modelPath, int pp, int tg, int reps, TextWriter output)
        {
            Result res = new Result();
            res.Model = Path.GetFileNameWithoutExtension(modelPath);
            using (GgufFile g = GgufFile.Open(modelPath))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                Stopwatch load = Stopwatch.StartNew();
                using (GemmaGpu m = new GemmaGpu(gpu, g, Math.Max(1024, pp + tg + 16)))
                {
                    load.Stop();
                    res.LoadSeconds = load.Elapsed.TotalSeconds;
                    res.WeightMB = m.WeightBytes / 1048576;

                    // Deterministic pseudo-prompt of ordinary tokens (llama-bench uses random tokens too).
                    Random rng = new Random(1);
                    int[] prompt = new int[pp];
                    for (int i = 0; i < pp; i++) prompt[i] = 1000 + rng.Next(20000);

                    // Warm-up (pipeline creation, caches).
                    m.Prefill(prompt, 0, 8);
                    m.Forward(prompt[0], 8);

                    double bestPp = 0, bestTg = 0;
                    for (int r = 0; r < reps; r++)
                    {
                        m.Reset();
                        Stopwatch sw = Stopwatch.StartNew();
                        float[] logits = m.Prefill(prompt, 0, pp);
                        sw.Stop();
                        bestPp = Math.Max(bestPp, pp / sw.Elapsed.TotalSeconds);

                        int next = CpuRef.ArgMax(logits);
                        if (Profile && r == reps - 1) { gpu.ProfileMs.Clear(); gpu.ProfileCount.Clear(); gpu.Profiling = true; }
                        sw.Restart();
                        for (int i = 0; i < tg; i++)
                        {
                            logits = m.Forward(next, pp + i);
                            next = CpuRef.ArgMax(logits);
                        }
                        sw.Stop();
                        bestTg = Math.Max(bestTg, tg / sw.Elapsed.TotalSeconds);
                    }
                    res.PromptTps = bestPp; res.GenTps = bestTg;
                    if (Profile)
                    {
                        gpu.Profiling = false;
                        double total = 0;
                        foreach (double v in gpu.ProfileMs.Values) total += v;
                        List<KeyValuePair<string, double>> rows = new List<KeyValuePair<string, double>>(gpu.ProfileMs);
                        rows.Sort(delegate(KeyValuePair<string, double> a, KeyValuePair<string, double> b) { return b.Value.CompareTo(a.Value); });
                        output.WriteLine("  GPU time per generated token: {0:N2} ms", total / tg);
                        foreach (KeyValuePair<string, double> kv in rows)
                            output.WriteLine("    {0,-16} {1,7:N3} ms/token  {2,5:N1}%  ({3} dispatches/token)", kv.Key, kv.Value / tg, 100 * kv.Value / total, gpu.ProfileCount[kv.Key] / tg);
                    }
                    output.WriteLine("{0,-48} weights {1,6:N0} MB   pp{2} {3,8:N1} t/s   tg{4} {5,7:N1} t/s   (load {6:N1} s)",
                        res.Model, res.WeightMB, pp, res.PromptTps, tg, res.GenTps, res.LoadSeconds);
                }
            }
            return res;
        }
    }
}
