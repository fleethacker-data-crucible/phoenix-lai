// Phase 7 gate: GPU batched prefill path (GemmaGpu.Prefill, the prefill kernels) vs llama.cpp on a Gemma 4 chat reply:
// for every reply position, the prompt and reply so far are read in one batch and the next token's log P is compared.
// C# 5 only.
using System.Collections.Generic;
using System.IO;

namespace Phoenix.Tests
{
    public static class Phase7Gate
    {
        public static List<string> Run(string modelPath, string refPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            if (!File.Exists(refPath)) { fail.Add("no reference " + refPath + " (run ref\\make-gemma-logprobs.ps1)"); return fail; }
            Phase4Gate.ChatRef r = Phase4Gate.ChatRef.Load(refPath);
            using (GgufFile g = GgufFile.Open(modelPath))
            using (GpuDevice gpu = GpuDevice.Create())
            using (GemmaGpu m = new GemmaGpu(gpu, g, r.Ids.Count + 64))
                Phase4Gate.Compare(r, delegate(int pos) { return m.Prefill(r.Ids, 0, pos); }, "gpu prefill", fail, output);
            return fail;
        }
    }
}
