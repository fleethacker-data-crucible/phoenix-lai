// Phase 9 gate: long prompts. The tiled prefill attention kernel vs the per-query kernel (Flash2Test); a real-text prompt
// past the sliding window read in 512-token chunks vs token by token (decode path); prompt speed. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static class Phase9Gate
    {
        const int Length = 1400;               // 2 full chunks + a short one, past the 1024-token window
        const double MaxLogprobDiff = 0.1;     // top token of the last position, chunked vs token by token
        const double MinPromptTps = 300;       // 2,557 tokens: ~470 tokens/s on the RX 6700 XT (2026-10-17)

        public static List<string> Run(string modelPath, string textPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            if (!Flash2Test.Run(output)) fail.Add("tiled prefill attention differs from the per-query kernel");
            using (GgufFile g = GgufFile.Open(modelPath))
            using (GpuDevice gpu = GpuDevice.Create())
            using (GemmaGpu m = new GemmaGpu(gpu, g, 2600))
            {
                List<int> text = new Tokenizer(g).Encode(File.ReadAllText(textPath), true, false);
                List<int> ids = new List<int>();
                while (ids.Count < 2557) ids.AddRange(text);
                float[] chunked = (float[])m.Prefill(ids, 0, Length).Clone();
                float[] single = null;
                for (int i = 0; i < Length; i++) single = m.Forward(ids[i], i);
                int a = CpuRef.ArgMax(chunked), b = CpuRef.ArgMax(single);
                double d = Math.Abs(Phase4Gate.LogProb(chunked, a) - Phase4Gate.LogProb(single, a));
                output.WriteLine("{0} tokens chunked vs token by token: top token {1} / {2}, |logprob diff| {3:G3}", Length, a, b, d);
                if (a != b) fail.Add("chunked prefill and token-by-token decode disagree on the top token");
                if (!(d <= MaxLogprobDiff)) fail.Add("chunked vs token-by-token logprob diff " + d.ToString("G3"));

                Stopwatch sw = Stopwatch.StartNew();
                m.Prefill(ids, 0, 2557);
                double tps = 2557 / sw.Elapsed.TotalSeconds;
                output.WriteLine("prompt speed: 2557 tokens at {0:N1} tokens/s (floor {1})", tps, MinPromptTps);
                if (tps < MinPromptTps) fail.Add("prompt speed " + tps.ToString("F1") + " tokens/s below " + MinPromptTps);
            }
            return fail;
        }
    }
}
