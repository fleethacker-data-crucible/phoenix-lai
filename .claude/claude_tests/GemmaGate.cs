// Gemma 4 gate (phase 10): teacher-forced log-probabilities of a chat reply vs llama.cpp (ref\make-gemma-logprobs.ps1),
// for the GPU batched prefill, the GPU token-by-token pass and (optionally) the CPU reference. C# 5 only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Phoenix.Tests
{
    public static class GemmaGate
    {
        sealed class Ref { public List<int> Ids = new List<int>(); public int PromptLen; public List<double> Lp = new List<double>(); public List<int> Top1 = new List<int>(); }

        static Ref Load(string tsv)
        {
            Ref r = new Ref();
            foreach (string line in File.ReadAllLines(tsv))
            {
                string[] f = line.Split('\t');
                if (line.StartsWith("# ids")) { foreach (string s in f[1].Split(' ')) if (s.Length > 0) r.Ids.Add(int.Parse(s)); }
                else if (line.StartsWith("# prompt_len")) r.PromptLen = int.Parse(f[1]);
                else if (!line.StartsWith("#") && f.Length >= 4)
                {
                    r.Lp.Add(f[2] == "nan" ? double.NaN : double.Parse(f[2], CultureInfo.InvariantCulture));
                    r.Top1.Add(int.Parse(f[3]));
                }
            }
            return r;
        }

        static double LogProb(float[] logits, int token)
        {
            double max = double.NegativeInfinity;
            foreach (float v in logits) if (v > max) max = v;
            double sum = 0;
            foreach (float v in logits) sum += Math.Exp(v - max);
            return logits[token] - max - Math.Log(sum);
        }

        static int Argmax(float[] l) { int b = 0; for (int i = 1; i < l.Length; i++) if (l[i] > l[b]) b = i; return b; }

        // mode: "prefill" (batched prompt), "decode" (every position through Forward), "cpu"
        static void Check(ILanguageModel m, Ref r, string mode, double tol, List<string> fail, TextWriter output)
        {
            float[] logits;
            if (mode == "prefill") logits = (float[])m.Prefill(r.Ids, 0, r.PromptLen).Clone();
            else { logits = null; for (int p = 0; p < r.PromptLen; p++) logits = m.Forward(r.Ids[p], p); }
            double maxd = 0, sumd = 0; int n = 0, agree = 0;
            for (int i = 0; i < r.Lp.Count; i++)
            {
                int pos = r.PromptLen + i, tok = r.Ids[pos];
                if (!double.IsNaN(r.Lp[i]))
                {
                    double d = Math.Abs(LogProb(logits, tok) - r.Lp[i]);
                    maxd = Math.Max(maxd, d); sumd += d; n++;
                }
                if (Argmax(logits) == r.Top1[i]) agree++;
                if (pos + 1 < r.Ids.Count) logits = m.Forward(tok, pos);
            }
            output.WriteLine("  {0,-8} logprob |diff| mean {1:F4} max {2:F4} over {3}; top-1 agrees {4}/{5}", mode, sumd / Math.Max(1, n), maxd, n, agree, r.Lp.Count);
            if (maxd > tol) fail.Add(mode + ": max logprob diff " + maxd.ToString("F4") + " > " + tol);
            if (agree < r.Lp.Count - 2) fail.Add(mode + ": top-1 agrees on only " + agree + "/" + r.Lp.Count);
        }

        static int Tok(Tokenizer tk, string s) { for (int i = 0; i < tk.VocabSize; i++) if (tk.Tokens[i] == s) return i; throw new Exception("no token " + s); }

        static double Cosine(float[] a, float[] b)
        {
            double ab = 0, aa = 0, bb = 0;
            for (int i = 0; i < a.Length; i++) { ab += (double)a[i] * b[i]; aa += (double)a[i] * a[i]; bb += (double)b[i] * b[i]; }
            return ab / Math.Sqrt(aa * bb);
        }

        // Vision: resize vs Pillow, CPU vs GPU tower, and the reply to an image prompt vs llama.cpp (vision-logprobs.tsv).
        public static List<string> RunVision(string model, string mmproj, string refsDir, bool cpu, TextWriter output)
        {
            List<string> fail = new List<string>();
            string imgDir = Path.Combine(refsDir, "..", "images");
            // 1. resize
            int th, tw;
            RgbImage.TargetSize(2160, 3840, 16, 280 * 9, 3, out th, out tw);
            output.WriteLine("vision: 3840x2160 -> {0}x{1} for 280 tokens", tw, th);
            if (tw != 1056 || th != 576) fail.Add("target size " + tw + "x" + th + ", expected 1056x576");
            RgbImage big = RgbImage.Decode(File.ReadAllBytes(Path.Combine(imgDir, "photo0_orig.png")));
            RgbImage mine = big.Resize(1056, 576), pil = RgbImage.Decode(File.ReadAllBytes(Path.Combine(imgDir, "photo0_resized_ref.png")));
            int diff = 0, maxd = 0;
            for (int i = 0; i < mine.Px.Length; i++) { int dd = Math.Abs(mine.Px[i] - pil.Px[i]); if (dd > 0) diff++; if (dd > maxd) maxd = dd; }
            output.WriteLine("  resize vs Pillow: {0} of {1} bytes differ (max {2})", diff, mine.Px.Length, maxd);
            if (maxd > 1) fail.Add("resize differs from Pillow by up to " + maxd);

            byte[] photo = File.ReadAllBytes(Path.Combine(imgDir, "photo0_960x672.png"));
            RgbImage pic = RgbImage.Decode(photo);
            using (GgufFile g = GgufFile.Open(model))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                // 2. CPU vs GPU tower (70 tokens keeps the CPU run short)
                if (cpu)
                {
                    VisionEncoder.MaxSoftTokens = 70;
                    float[][] a, b;
                    using (VisionEncoder ve = new VisionEncoder(mmproj, null)) a = ve.Encode(pic);
                    using (VisionEncoder ve = new VisionEncoder(mmproj, gpu)) b = ve.Encode(pic);
                    double minCos = 1;
                    for (int i = 0; i < a.Length; i++) minCos = Math.Min(minCos, Cosine(a[i], b[i]));
                    output.WriteLine("  tower CPU vs GPU: {0} tokens, min cosine {1:F5}", a.Length, minCos);
                    if (minCos < 0.999) fail.Add("vision tower GPU vs CPU cosine " + minCos.ToString("F5"));
                    VisionEncoder.MaxSoftTokens = 280;
                }

                // 3. image prompt vs llama.cpp
                string tsv = Path.Combine(refsDir, Path.GetFileNameWithoutExtension(model), "vision-logprobs.tsv");
                List<int> reply = new List<int>(); List<double> lp = new List<double>(); List<int> top1 = new List<int>();
                string question = null;
                foreach (string line in File.ReadAllLines(tsv))
                {
                    string[] f = line.Split('\t');
                    if (line.StartsWith("# question")) question = f[1];
                    else if (!line.StartsWith("#") && f.Length >= 4)
                    {
                        reply.Add(int.Parse(f[1])); top1.Add(int.Parse(f[3]));
                        lp.Add(f[2] == "nan" ? double.NaN : double.Parse(f[2], CultureInfo.InvariantCulture));
                    }
                }
                Tokenizer tk = new Tokenizer(g);
                foreach (bool quick in new bool[] { true, false })
                {
                    VisionEncoder.GeluQuick = quick;
                    float[][] rows;
                    string kind;
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    using (VisionEncoder ve = new VisionEncoder(mmproj, gpu)) { rows = ve.Encode(pic); kind = ve.Kind; }
                    long ms = sw.ElapsedMilliseconds;
                    if (kind == "gemma4uv" && !quick) break;   // no MLP in the unified embedder: one run is enough
                    List<int> ids = new List<int>();
                    ids.Add(tk.BosId); ids.Add(Tok(tk, "<|turn>")); ids.AddRange(tk.Encode("user\n", false, false));
                    ids.Add(Tok(tk, "<|image>"));
                    for (int i = 0; i < rows.Length; i++) ids.Add(ImageTokens.Make(1, i));
                    ids.Add(Tok(tk, "<image|>"));
                    ids.AddRange(tk.Encode(question, false, false));
                    ids.Add(Tok(tk, "<turn|>")); ids.AddRange(tk.Encode("\n", false, false));
                    ids.Add(Tok(tk, "<|turn>")); ids.AddRange(tk.Encode("model\n", false, false));
                    ids.Add(Tok(tk, "<|channel>")); ids.AddRange(tk.Encode("thought\n", false, false)); ids.Add(Tok(tk, "<channel|>"));
                    int promptLen = ids.Count;
                    ids.AddRange(reply);
                    using (GemmaGpu m = new GemmaGpu(gpu, g, 2048))
                    {
                        float[][] r = rows;
                        m.ImageRow = delegate(int t) { return r[ImageTokens.Index(t)]; };
                        m.BidiImages = kind == "gemma4uv" && Environment.GetEnvironmentVariable("GEMMA_NO_BIDI") == null;   // sanity: causal must differ
                        float[] logits = (float[])m.Prefill(ids, 0, promptLen).Clone();
                        double sum = 0, max = 0; int n = 0, agree = 0;
                        for (int i = 0; i < reply.Count; i++)
                        {
                            if (!double.IsNaN(lp[i])) { double d = Math.Abs(LogProb(logits, reply[i]) - lp[i]); sum += d; max = Math.Max(max, d); n++; }
                            if (Argmax(logits) == top1[i]) agree++;
                            if (i + 1 < reply.Count) logits = m.Forward(reply[i], promptLen + i);
                        }
                        string act = kind == "gemma4uv" ? "" : quick ? " gelu_quick (llama.cpp)" : " gelu_tanh (transformers)";
                        output.WriteLine("  {0}{1}: {2} image tokens in {3} ms; reply logprob |diff| mean {4:F4} max {5:F4}; top-1 {6}/{7}",
                            kind, act, rows.Length, ms, sum / Math.Max(1, n), max, agree, reply.Count);
                        if (quick && (max > 0.6 || agree < reply.Count - 2)) fail.Add(kind + " image reply differs from llama.cpp (max " + max.ToString("F3") + ", top-1 " + agree + "/" + reply.Count + ")");
                    }
                }
                VisionEncoder.GeluQuick = false;
            }
            return fail;
        }

        public static List<string> Run(string model, string refsDir, bool cpu, TextWriter output)
        {
            List<string> fail = new List<string>();
            string tsv = Path.Combine(refsDir, Path.GetFileNameWithoutExtension(model), "gemma-logprobs.tsv");
            Ref r = Load(tsv);
            output.WriteLine("{0}: prompt {1} tokens, reply {2}", Path.GetFileName(model), r.PromptLen, r.Lp.Count);
            using (GgufFile g = GgufFile.Open(model))
            {
                if (Environment.GetEnvironmentVariable("GEMMA_GATE_NOGPU") == null)   // CPU only: GEMMA_GATE_CPU=1 GEMMA_GATE_NOGPU=1
                using (GpuDevice gpu = GpuDevice.Create())
                {
                    foreach (string mode in new string[] { "prefill", "decode" })
                        using (GemmaGpu m = new GemmaGpu(gpu, g, 1024))
                        {
                            if (mode == "prefill") output.WriteLine("  weights {0:F2} GB, KV {1:F1} MB", m.WeightBytes / 1e9, m.KvBytes / 1e6);
                            Check(m, r, mode, 0.35, fail, output);
                        }
                }
                if (cpu)
                    using (GemmaCpu m = new GemmaCpu(g, 1024)) Check(m, r, "cpu", 0.35, fail, output);
            }
            return fail;
        }
    }
}
