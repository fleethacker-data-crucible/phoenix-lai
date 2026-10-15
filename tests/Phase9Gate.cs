// Phase 9 gate: Devstral Small 2 (mistral3 architecture, tekken tokenizer, Q3_K / IQ2_XXS / IQ2_S / IQ3_S). C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static unsafe class Phase9Gate
    {
        const string Model = "Devstral-Small-2-24B-Instruct-2512-UD-IQ2_XXS.gguf";

        sealed class GpuForward : Phase4Gate.IForward
        {
            readonly LlamaGpu _m;
            public GpuForward(LlamaGpu m) { _m = m; }
            public float[] Forward(int token, int pos) { return _m.Forward(token, pos); }
        }

        public static List<string> Run(string modelsDir, string refsDir, string samplesPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            string path = Path.Combine(modelsDir, Model);
            string refs = Path.Combine(refsDir, Path.GetFileNameWithoutExtension(Model));
            if (!File.Exists(path)) { fail.Add(Model + " missing"); return fail; }

            // 1. Tokenizer (tekken) vs llama-tokenize.
            fail.AddRange(Phase3Gate.Run(path, samplesPath, Path.Combine(refs, "tokens.tsv"), output));

            using (GgufFile g = GgufFile.Open(path))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                ModelConfig c = ModelConfig.FromGguf(g);
                output.WriteLine("config: {0}, {1} layers, embd {2}, heads {3}/{4}x{5}, rope base {6}, yarn freq_scale {7}, attn_factor {8:R}, attn temp {9}/{10}",
                    c.Arch, c.NLayer, c.NEmbd, c.NHead, c.NHeadKv, c.HeadDim, c.RopeBase, c.FreqScale, c.AttnFactor, c.AttnTempScale, c.AttnTempFloor);

                // 2. GPU vs CPU matvec for every weight format in the file.
                HashSet<GgmlType> seen = new HashSet<GgmlType>();
                foreach (GgufTensor t in g.Tensors)
                {
                    if (t.Dims.Length != 2 || !seen.Add(t.Type)) continue;
                    if (KernelsIQ.Handles(t.Type)) CheckPacked(gpu, t, fail, output);
                }

                // 3. Chat template.
                using (ChatEngine eng = ChatEngine.Load(path, 512, false, null))
                {
                    List<ChatMessage> msgs = new List<ChatMessage>();
                    msgs.Add(new ChatMessage("system", "You are terse."));
                    msgs.Add(new ChatMessage("user", "Hi"));
                    msgs.Add(new ChatMessage("assistant", "Hello."));
                    msgs.Add(new ChatMessage("user", "Bye"));
                    string got = eng.Tokenizer.Decode(eng.Template(msgs), true);
                    string want = "<s>[SYSTEM_PROMPT]You are terse.[/SYSTEM_PROMPT][INST]Hi[/INST]Hello.</s>[INST]Bye[/INST]";
                    output.WriteLine("template: {0}", got);
                    if (got != want) fail.Add("Mistral template renders '" + got + "', expected '" + want + "'");
                }

                // 4. GPU forward pass vs llama.cpp logprobs, then a chat reply.
                string lp = Path.Combine(refs, "logprobs.tsv");
                if (!File.Exists(lp)) { fail.Add("no llama.cpp logprob reference in " + refs); return fail; }
                Tokenizer tok = new Tokenizer(g);
                Stopwatch load = Stopwatch.StartNew();
                using (LlamaGpu gm = new LlamaGpu(gpu, g, 512))
                {
                    load.Stop();
                    output.WriteLine("gpu: {0:N0} MB of weights in {1:N1} s ({2:N0} MB in system memory)", gm.WeightBytes / 1048576.0, load.Elapsed.TotalSeconds, gpu.FallbackBytes / 1048576.0);
                    // A 2-bit model is sensitive to accumulation order: llama.cpp's own GPU and CPU backends disagree on it.
                    // Allow that measured spread (backend-spread.tsv), never less than the default limits.
                    double meanLimit = 0.03, maxLimit = 0.3;
                    string spread = Path.Combine(refs, "backend-spread.tsv");
                    if (File.Exists(spread))
                    {
                        foreach (string line in File.ReadAllLines(spread))
                        {
                            if (line.StartsWith("#") || line.Trim().Length == 0) continue;
                            string[] f = line.Split('\t');
                            meanLimit = Math.Max(meanLimit, double.Parse(f[1], System.Globalization.CultureInfo.InvariantCulture));
                            maxLimit = Math.Max(maxLimit, 1.25 * double.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture));
                        }
                        output.WriteLine("limits from llama.cpp's own GPU-vs-CPU spread: mean {0:G3}, max {1:G3}", meanLimit, maxLimit);
                    }
                    output.Write("gpu ");
                    Phase4Gate.CompareLogprobs(tok, Phase4Gate.Cases(Path.Combine(refs, "greedy.txt")), lp, delegate { gm.Reset(); return new GpuForward(gm); },
                        meanLimit, maxLimit, fail, output);
                }
            }

            // 5. Chat through the engine (template + stop token + speed).
            using (ChatEngine eng = ChatEngine.Load(path, 2048, true, null))
            {
                List<ChatMessage> msgs = new List<ChatMessage>();
                msgs.Add(new ChatMessage("system", "You are a helpful coding assistant."));
                msgs.Add(new ChatMessage("user", "Write a Python function that returns the sum of a list of numbers. Reply with only the code."));
                SamplerSettings greedy = new SamplerSettings(); greedy.Temperature = 0;
                System.Text.StringBuilder reply = new System.Text.StringBuilder();
                GenerationStats st = eng.Generate(msgs, greedy, 160, delegate(string s) { reply.Append(s); }, null);
                output.WriteLine("chat ({0}): {1}", st.StopReason, reply.ToString().Trim().Replace("\n", "\\n"));
                output.WriteLine("speed: decode {0:N1} tokens/s, prompt {1:N1} tokens/s", st.TokensPerSecond, st.PromptTokensPerSecond);
                if (st.StopReason != "end of turn") fail.Add("chat reply did not end with </s> (" + st.StopReason + ")");
                if (!reply.ToString().Contains("def ") || !reply.ToString().Contains("return")) fail.Add("chat reply has no Python function");
            }
            return fail;
        }

        // GPU vs CPU matvec on real rows of a packed-format tensor, random activations.
        static void CheckPacked(GpuDevice gpu, GgufTensor t, List<string> fail, TextWriter output)
        {
            Weight w = new Weight(t);
            int rows = Math.Min(w.Rows, 2048);
            Random r = new Random(9);
            float[] x = new float[w.Cols];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(r.NextDouble() * 4 - 2);

            Weight slice = new Weight(t);
            slice.Rows = rows;
            float[] want = new float[rows];
            CpuRef.MatVec(slice, x, new CpuRef.Q8Activations(w.Cols), want);

            int nb = w.Cols / CpuRef.QK_K;
            long blocks = (long)rows * nb;
            int bb = CpuRef.KBlockBytes(t.Type), pb = KernelsIQ.PayloadBytes(t.Type);
            int dOff = KernelsIQ.DOffset(t.Type), pOff = KernelsIQ.PayloadOffset(t.Type);
            byte[] payload = new byte[blocks * pb];
            float[] d = new float[blocks];
            for (long b = 0; b < blocks; b++)
            {
                byte* src = t.Data + b * bb;
                d[b] = Half.ToFloat(*(ushort*)(src + dOff));
                for (int j = 0; j < pb; j++) payload[b * pb + j] = src[pOff + j];
            }
            uint[] tab = KernelsIQ.Tables();

            using (GpuBuffer gw = gpu.CreateBuffer((ulong)payload.Length, false))
            using (GpuBuffer gd = gpu.CreateBuffer((ulong)d.Length * 4, false))
            using (GpuBuffer gt = gpu.CreateBuffer((ulong)tab.Length * 4, false))
            using (GpuBuffer gx = gpu.CreateBuffer((ulong)x.Length * 4, true))
            using (GpuBuffer gq = gpu.CreateBuffer((ulong)x.Length, true))
            using (GpuBuffer gkd = gpu.CreateBuffer((ulong)(nb + 1) * 4, true))
            using (GpuBuffer gy = gpu.CreateBuffer((ulong)rows * 4, true))
            using (GpuKernel kq = gpu.CreateKernel(KernelsK.QuantizeQ8K(), 3, 4, 2))
            using (GpuKernel km = gpu.CreateKernel(KernelsIQ.MatVec(t.Type), 6, 16, 2))
            {
                fixed (byte* pp = payload) gpu.Upload(gw, 0, (IntPtr)pp, (ulong)payload.Length);
                gpu.Upload(gd, d);
                gpu.Upload(gt, tab);
                gx.Upload(x, 0);
                uint gxs = (uint)Math.Min(rows, 65535), gys = (uint)((rows + gxs - 1) / gxs);
                gpu.Begin();
                gpu.Record(kq, new GpuBuffer[] { gx, gq, gkd }, new uint[] { (uint)nb }, (uint)((nb + 63) / 64), 1, 1);
                gpu.Record(km, new GpuBuffer[] { gw, gd, gq, gkd, gy, gt }, new uint[] { (uint)rows, (uint)nb, gxs, 0 }, gxs, gys, 1);
                gpu.SubmitAndWait();
                float[] got = new float[rows];
                gy.Download(got, 0);

                double worstRel = 0;
                for (int i = 0; i < rows; i++)
                {
                    double rel = Math.Abs(want[i] - got[i]) / Math.Max(1.0, Math.Abs(want[i]));
                    if (!(rel <= worstRel)) worstRel = rel;
                }
                output.WriteLine("matvec {0,-8} {1} [{2}x{3}]: max rel diff {4:G3}", GgmlTypes.Name(t.Type), t.Name, rows, w.Cols, worstRel);
                if (!(worstRel <= 1e-4)) fail.Add(t.Name + " (" + GgmlTypes.Name(t.Type) + "): GPU vs CPU matvec rel diff " + worstRel.ToString("G3"));
            }
        }
    }
}
