// Phase 6 gate: GPU forward pass vs CpuRef (LlamaCpu), logits at every position. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static class Phase6Gate
    {
        sealed class GpuForward : Phase4Gate.IForward
        {
            readonly LlamaGpu _m;
            public GpuForward(LlamaGpu m) { _m = m; }
            public float[] Forward(int token, int pos) { return _m.Forward(token, pos); }
        }

        // CPU vs GPU logits are informational: float accumulation order differs, and the BOS token's very large
        // activations (|x| ~ 400) amplify that to ~0.1-0.5 in the logits. Accuracy is gated against llama.cpp instead.
        const float MinTopTokenAgreement = 0.9f;
        const string Prompt = "The capital of France is Paris. The Eiffel Tower is located in Paris. The";
        const int Generate = 12;

        public static List<string> Run(string modelPath, string greedyPath, string logprobsPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            using (GgufFile g = GgufFile.Open(modelPath))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                Tokenizer tok = new Tokenizer(g);
                Stopwatch load = Stopwatch.StartNew();
                LlamaGpu gm = new LlamaGpu(gpu, g, 128);
                load.Stop();
                output.WriteLine("gpu: {0}, {1:N0} MB of weights uploaded in {2:N1} s ({3:N0} MB fell back to system memory)", gpu.DeviceName, gm.WeightBytes / 1048576.0, load.Elapsed.TotalSeconds, gpu.FallbackBytes / 1048576.0);
                try
                {
                    LlamaCpu cm = new LlamaCpu(g, 128);
                    List<int> ids = tok.Encode(Prompt, true, true);
                    int promptLen = ids.Count;

                    float worst = 0;
                    int worstPos = -1, argmaxMismatches = 0;
                    double gpuSeconds = 0;
                    for (int pos = 0; pos < promptLen + Generate; pos++)
                    {
                        float[] cpu = (float[])cm.Forward(ids[pos], pos).Clone();
                        Stopwatch sw = Stopwatch.StartNew();
                        float[] gpuLogits = gm.Forward(ids[pos], pos);
                        sw.Stop();
                        gpuSeconds += sw.Elapsed.TotalSeconds;

                        float maxDiff = 0;
                        for (int i = 0; i < cpu.Length; i++)
                        {
                            float d = Math.Abs(cpu[i] - gpuLogits[i]);
                            if (!(d <= maxDiff)) maxDiff = d; // NaN-safe
                        }
                        int ac = CpuRef.ArgMax(cpu), ag = CpuRef.ArgMax(gpuLogits);
                        if (ac != ag) argmaxMismatches++;
                        if (!(maxDiff <= worst)) { worst = maxDiff; worstPos = pos; }

                        // Continue greedily from the CPU's choice so both paths see the same tokens.
                        if (pos + 1 >= ids.Count) ids.Add(ac);
                    }
                    int n = promptLen + Generate;
                    if (n - argmaxMismatches < MinTopTokenAgreement * n) fail.Add(argmaxMismatches + "/" + n + " positions where GPU and CPU disagree on the top token");
                    output.WriteLine("cpu vs gpu: {0} positions, worst max |logit diff| {1:G4} at pos {2}, top-token mismatches {3}",
                        n, worst, worstPos, argmaxMismatches);

                    Phase4Gate.CompareLogprobs(tok, Phase4Gate.Cases(greedyPath), logprobsPath,
                        delegate { gm.Reset(); return new GpuForward(gm); }, fail, output);
                    output.WriteLine("text: {0}", tok.Decode(ids.GetRange(1, ids.Count - 1), false).Replace("\n", "\\n"));
                    output.WriteLine("speed: GPU {0:N1} ms/token ({1:N1} tokens/s, one submit per token, unoptimized)",
                        gpuSeconds * 1000 / n, n / gpuSeconds);
                }
                finally { gm.Dispose(); }
            }
            return fail;
        }
    }
}
