// Phase 7 gate: k-quant formats (Q4_K, Q5_K, Q6_K) on CPU and GPU, and an 8B Q4_K_M model end to end. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static unsafe class Phase7Gate
    {
        sealed class GpuForward : Phase4Gate.IForward
        {
            readonly LlamaGpu _m;
            public GpuForward(LlamaGpu m) { _m = m; }
            public float[] Forward(int token, int pos) { return _m.Forward(token, pos); }
        }

        // 1B models are re-quantized from the Q8_0 file on the dev machine (llama-quantize --allow-requantize).
        static readonly string[] Models =
        {
            "Llama-3.2-1B-Instruct-Q4_K_M-requant.gguf",
            "Llama-3.2-1B-Instruct-Q5_K_M-requant.gguf",
            "Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf",
        };

        public static List<string> Run(string modelsDir, string refsDir, TextWriter output)
        {
            List<string> fail = new List<string>();
            using (GpuDevice gpu = GpuDevice.Create())
            {
                foreach (string name in Models)
                {
                    string path = Path.Combine(modelsDir, name);
                    string refs = Path.Combine(refsDir, Path.GetFileNameWithoutExtension(name));
                    if (!File.Exists(path)) { fail.Add(name + ": model file missing, not run"); continue; }
                    if (!File.Exists(Path.Combine(refs, "logprobs.tsv"))) { fail.Add(name + ": no llama.cpp reference in " + refs + " (run dev\\ref\\make-refs.ps1)"); continue; }
                    output.WriteLine(name);
                    RunModel(gpu, path, refs, name.Contains("-1B-"), fail, output);
                }
            }
            return fail;
        }

        static void RunModel(GpuDevice gpu, string path, string refs, bool small, List<string> fail, TextWriter output)
        {
            using (GgufFile g = GgufFile.Open(path))
            {
                Tokenizer tok = new Tokenizer(g);
                CheckMatVecs(gpu, g, fail, output);

                List<KeyValuePair<string, string>> cases = Phase4Gate.Cases(Path.Combine(refs, "greedy.txt"));
                string lp = Path.Combine(refs, "logprobs.tsv");
                if (small)
                {
                    output.Write("  cpu ");
                    Phase4Gate.CompareLogprobs(tok, cases, lp, delegate { return new Phase4Gate.CpuForward(new LlamaCpu(g, 128)); }, fail, output);
                }

                Stopwatch load = Stopwatch.StartNew();
                using (LlamaGpu gm = new LlamaGpu(gpu, g, 128))
                {
                    load.Stop();
                    output.WriteLine("  gpu: {0:N0} MB of weights in {1:N1} s ({2:N0} MB in system memory)", gm.WeightBytes / 1048576.0, load.Elapsed.TotalSeconds, gpu.FallbackBytes / 1048576.0);
                    output.Write("  gpu ");
                    Phase4Gate.CompareLogprobs(tok, cases, lp, delegate { gm.Reset(); return new GpuForward(gm); }, fail, output);

                    // Greedy text from the first prompt, plus speed.
                    List<int> ids = tok.Encode(cases[0].Key, true, true);
                    int n0 = ids.Count;
                    Stopwatch sw = Stopwatch.StartNew();
                    float[] logits = null;
                    for (int p = 0; p < 40 && p < 120; p++)
                    {
                        if (p < ids.Count) logits = gm.Forward(ids[p], p);
                        if (p + 1 >= ids.Count) { int next = CpuRef.ArgMax(logits); if (next == tok.EosId) break; ids.Add(next); }
                    }
                    sw.Stop();
                    output.WriteLine("  text: {0}", tok.Decode(ids.GetRange(1, ids.Count - 1), false).Replace("\n", "\\n"));
                    output.WriteLine("  speed: {0:N1} ms/token ({1:N1} tokens/s)", sw.Elapsed.TotalMilliseconds / ids.Count, ids.Count / sw.Elapsed.TotalSeconds);
                }
            }
        }

        // GPU vs CPU matvec on the first tensor of each k-quant type, random activations.
        static void CheckMatVecs(GpuDevice gpu, GgufFile g, List<string> fail, TextWriter output)
        {
            HashSet<GgmlType> seen = new HashSet<GgmlType>();
            foreach (GgufTensor t in g.Tensors)
            {
                if (t.Dims.Length != 2 || seen.Contains(t.Type)) continue;
                if (t.Type != GgmlType.Q4_K && t.Type != GgmlType.Q5_K && t.Type != GgmlType.Q6_K) continue;
                seen.Add(t.Type);
                CheckMatVec(gpu, t, fail, output);
            }
        }

        static void CheckMatVec(GpuDevice gpu, GgufTensor t, List<string> fail, TextWriter output)
        {
            Weight w = new Weight(t);
            int rows = Math.Min(w.Rows, 4096);   // a slice of rows is enough
            Random r = new Random(7);
            float[] x = new float[w.Cols];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(r.NextDouble() * 4 - 2);

            Weight slice = new Weight(t);
            slice.Rows = rows;
            float[] want = new float[rows];
            CpuRef.MatVec(slice, x, new CpuRef.Q8Activations(w.Cols), want);

            int nb = w.Cols / CpuRef.QK_K;
            int bytes = t.Type == GgmlType.Q6_K ? KernelsK.Q6_KPaddedBytes : CpuRef.KBlockBytes(t.Type);
            byte[] blocks = new byte[(long)rows * nb * bytes];
            for (long b = 0; b < (long)rows * nb; b++)
                for (int j = 0; j < CpuRef.KBlockBytes(t.Type); j++) blocks[b * bytes + j] = t.Data[b * CpuRef.KBlockBytes(t.Type) + j];

            using (GpuBuffer gw = gpu.CreateBuffer((ulong)blocks.Length, false))
            using (GpuBuffer gx = gpu.CreateBuffer((ulong)x.Length * 4, true))
            using (GpuBuffer gq = gpu.CreateBuffer((ulong)x.Length, true))
            using (GpuBuffer gd = gpu.CreateBuffer((ulong)(nb + 1) * 4, true))
            using (GpuBuffer gy = gpu.CreateBuffer((ulong)rows * 4, true))
            using (GpuKernel kq = gpu.CreateKernel(KernelsK.QuantizeQ8K(), 3, 4, 2))
            using (GpuKernel km = gpu.CreateKernel(KernelsK.MatVec(t.Type), 4, 12, 2))
            {
                fixed (byte* pb = blocks) gpu.Upload(gw, 0, (IntPtr)pb, (ulong)blocks.Length);
                gx.Upload(x, 0);
                uint gxs = (uint)Math.Min(rows, 65535), gys = (uint)((rows + gxs - 1) / gxs);
                gpu.Begin();
                gpu.Record(kq, new GpuBuffer[] { gx, gq, gd }, new uint[] { (uint)nb }, (uint)((nb + 63) / 64), 1, 1);
                gpu.Record(km, new GpuBuffer[] { gw, gq, gd, gy }, new uint[] { (uint)rows, (uint)nb, gxs }, gxs, gys, 1);
                gpu.SubmitAndWait();
                float[] got = new float[rows];
                gy.Download(got, 0);

                double worstRel = 0;
                for (int i = 0; i < rows; i++)
                {
                    double rel = Math.Abs(want[i] - got[i]) / Math.Max(1.0, Math.Abs(want[i]));
                    if (!(rel <= worstRel)) worstRel = rel;
                }
                output.WriteLine("  matvec {0,-5} {1} [{2}x{3}]: max rel diff {4:G3}", GgmlTypes.Name(t.Type), t.Name, rows, w.Cols, worstRel);
                if (!(worstRel <= 1e-4)) fail.Add(t.Name + " (" + GgmlTypes.Name(t.Type) + "): GPU vs CPU matvec rel diff " + worstRel.ToString("G3"));
            }
        }
    }
}
