// Phase 6 gate: GPU decode path (GemmaGpu.Forward) vs CPU (GemmaCpu) and vs llama.cpp on a Gemma 4 chat reply:
// top-token agreement with the CPU, the greedy reply, teacher-forced log-probabilities. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static class Phase6Gate
    {
        // CPU vs GPU logits are informational (accumulation order differs); accuracy is gated against llama.cpp.
        const float MinTopTokenAgreement = 0.9f;
        const int CpuPositions = 24;

        public static List<string> Run(string modelPath, string refPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            if (!File.Exists(refPath)) { fail.Add("no reference " + refPath + " (run ref\\make-gemma-logprobs.ps1)"); return fail; }
            Phase4Gate.ChatRef r = Phase4Gate.ChatRef.Load(refPath);
            using (GgufFile g = GgufFile.Open(modelPath))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                Tokenizer tok = new Tokenizer(g);
                Stopwatch load = Stopwatch.StartNew();
                GemmaGpu gm = new GemmaGpu(gpu, g, r.Ids.Count + 64);
                load.Stop();
                output.WriteLine("gpu: {0}, {1:N0} MB of weights uploaded in {2:N1} s ({3:N0} MB fell back to system memory)", gpu.DeviceName, gm.WeightBytes / 1048576.0, load.Elapsed.TotalSeconds, gpu.FallbackBytes / 1048576.0);
                try
                {
                    GemmaCpu cm = new GemmaCpu(g, CpuPositions + 8);
                    int mismatches = 0;
                    for (int pos = 0; pos < CpuPositions; pos++)
                        if (CpuRef.ArgMax(cm.Forward(r.Ids[pos], pos)) != CpuRef.ArgMax(gm.Forward(r.Ids[pos], pos))) mismatches++;
                    if (CpuPositions - mismatches < MinTopTokenAgreement * CpuPositions) fail.Add(mismatches + "/" + CpuPositions + " positions where GPU and CPU disagree on the top token");
                    output.WriteLine("cpu vs gpu: {0} positions, top-token mismatches {1}", CpuPositions, mismatches);

                    // Greedy reply (decode path) vs llama.cpp's.
                    float[] logits = null;
                    for (int p = 0; p < r.PromptLen; p++) logits = gm.Forward(r.Ids[p], p);
                    int same = 0, n = r.Ids.Count - r.PromptLen;
                    Stopwatch sw = Stopwatch.StartNew();
                    for (int i = 0; i < n; i++)
                    {
                        int next = CpuRef.ArgMax(logits);
                        if (next != r.Ids[r.PromptLen + i]) break;
                        same++;
                        logits = gm.Forward(next, r.PromptLen + i);
                    }
                    output.WriteLine("greedy reply: first {0}/{1} tokens identical to llama.cpp ({2:N1} tokens/s)", same, n, same / sw.Elapsed.TotalSeconds);
                    if (same < n / 2) fail.Add("greedy reply diverges from llama.cpp after " + same + " tokens");

                    Phase4Gate.Compare(r, Phase4Gate.Sequential(gm, r), "gpu decode", fail, output);
                    output.WriteLine("reply: {0}", tok.Decode(r.Ids.GetRange(r.PromptLen, n), false).Replace("\n", "\\n"));
                }
                finally { gm.Dispose(); }
            }
            return fail;
        }
    }
}
