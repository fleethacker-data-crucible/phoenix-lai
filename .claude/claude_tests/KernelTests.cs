// Kernel-level checks: each GPU kernel vs the same math on the CPU, on random inputs. C# 5 only.
using System;
using System.Collections.Generic;
using System.IO;

namespace Phoenix.Tests
{
    public static unsafe class KernelTests
    {
        public static List<string> Run(TextWriter output)
        {
            List<string> fail = new List<string>();
            using (GpuDevice gpu = GpuDevice.Create())
            {
                Rope(gpu, fail, output);
                Attention(gpu, fail, output);
                Norm(gpu, fail, output);
                SiluMul(gpu, fail, output);
                Quant(gpu, fail, output);
            }
            if (!Flash2Test.Run(output)) fail.Add("flash2 attention");
            return fail;
        }

        static float[] Rand(Random r, int n, float scale)
        {
            float[] a = new float[n];
            for (int i = 0; i < n; i++) a[i] = (float)(r.NextDouble() * 2 - 1) * scale;
            return a;
        }

        static GpuBuffer Up(GpuDevice gpu, List<GpuBuffer> keep, float[] data)
        {
            GpuBuffer b = gpu.CreateBuffer((ulong)data.Length * 4, true);
            b.Upload(data, 0);
            keep.Add(b);
            return b;
        }

        static float[] Down(GpuBuffer b, int n)
        {
            float[] a = new float[n];
            b.Download(a, 0);
            return a;
        }

        static void Report(string name, float[] want, float[] got, float tol, List<string> fail, TextWriter output)
        {
            float worst = 0; int at = -1;
            for (int i = 0; i < want.Length; i++)
            {
                float d = Math.Abs(want[i] - got[i]);
                if (!(d <= worst)) { worst = d; at = i; }
            }
            output.WriteLine("  {0,-10} max |diff| {1:G4}{2}", name, worst, at >= 0 ? " at " + at + " (cpu " + want[at] + ", gpu " + got[at] + ")" : "");
            if (!(worst <= tol)) fail.Add(name + ": max |diff| " + worst.ToString("G4") + " > " + tol);
        }

        static void Run1(GpuDevice gpu, uint[] spirv, GpuBuffer[] bufs, uint[] push, uint gx)
        {
            using (GpuKernel k = gpu.CreateKernel(spirv, bufs.Length, (uint)push.Length * 4, 4))
            {
                gpu.Begin();
                gpu.Record(k, bufs, push, gx, 1, 1);
                gpu.SubmitAndWait();
            }
        }

        static void Rope(GpuDevice gpu, List<string> fail, TextWriter output)
        {
            Random r = new Random(1);
            int nHeads = 8, headDim = 64, nRot = 64;
            float[] x = Rand(r, nHeads * headDim, 3), cs = new float[nRot];
            CpuRef.RopeTable(7, nRot, 500000f, null, cs);
            List<GpuBuffer> keep = new List<GpuBuffer>();
            GpuBuffer gx = Up(gpu, keep, x), gcs = Up(gpu, keep, cs);
            float post = 1.25f;
            Run1(gpu, Kernels.Rope(), new GpuBuffer[] { gx, gcs }, new uint[] { (uint)nHeads, (uint)headDim, (uint)(nRot / 2), *(uint*)&post }, (uint)((nHeads * nRot / 2 + 63) / 64));
            CpuRef.Rope(x, nHeads, headDim, nRot, cs);
            for (int i = 0; i < x.Length; i++) x[i] *= post;
            Report("rope", x, Down(gx, x.Length), 1e-5f, fail, output);
            foreach (GpuBuffer b in keep) b.Dispose();
        }

        static void Attention(GpuDevice gpu, List<string> fail, TextWriter output)
        {
            Random r = new Random(2);
            int nHead = 4, nHeadKv = 2, headDim = 64, n = 5, maxCtx = 8, kvDim = nHeadKv * headDim, group = nHead / nHeadKv;
            float scale = 1f / 8f;
            float[] q = Rand(r, nHead * headDim, 1), kc = Rand(r, maxCtx * kvDim, 1), vc = Rand(r, maxCtx * kvDim, 1);
            for (int i = 0; i < kc.Length; i++) { kc[i] = Half.Round(kc[i]); vc[i] = Half.Round(vc[i]); }

            // CPU: same as LlamaCpu.Forward attention.
            float[] want = new float[nHead * headDim];
            for (int h = 0; h < nHead; h++)
            {
                int kvOff = (h / group) * headDim, qOff = h * headDim;
                float[] s = new float[n]; float max = float.NegativeInfinity;
                for (int t = 0; t < n; t++)
                {
                    float acc = 0;
                    for (int d = 0; d < headDim; d++) acc += Half.Round(q[qOff + d]) * kc[t * kvDim + kvOff + d];
                    s[t] = acc * scale; if (s[t] > max) max = s[t];
                }
                double sum = 0;
                for (int t = 0; t < n; t++) { s[t] = (float)Math.Exp(s[t] - max); sum += s[t]; }
                float inv = (float)(1.0 / sum);
                for (int t = 0; t < n; t++)
                {
                    float p = Half.Round(s[t] * inv);
                    for (int d = 0; d < headDim; d++) want[qOff + d] += p * vc[t * kvDim + kvOff + d];
                }
            }

            List<GpuBuffer> keep = new List<GpuBuffer>();
            GpuBuffer gq = Up(gpu, keep, q), gk = Up(gpu, keep, kc), gv = Up(gpu, keep, vc);
            GpuBuffer gs = Up(gpu, keep, new float[nHead * maxCtx]), go = Up(gpu, keep, new float[nHead * headDim]);
            uint sbits; unsafe { sbits = *(uint*)&scale; }
            Run1(gpu, Kernels.Attention(), new GpuBuffer[] { gq, gk, gv, gs, go },
                new uint[] { (uint)n, (uint)headDim, (uint)kvDim, (uint)group, (uint)maxCtx, sbits }, (uint)nHead);
            Report("attention", want, Down(go, want.Length), 1e-3f, fail, output);
            foreach (GpuBuffer b in keep) b.Dispose();
        }

        static unsafe void Norm(GpuDevice gpu, List<string> fail, TextWriter output)
        {
            Random r = new Random(3);
            int n = 2048;
            float[] x = Rand(r, n, 2), w = Rand(r, n, 1), want = new float[n];
            fixed (float* pw = w) CpuRef.RmsNorm(x, pw, 1e-5f, want);
            List<GpuBuffer> keep = new List<GpuBuffer>();
            GpuBuffer gx = Up(gpu, keep, x), gw = Up(gpu, keep, w), gy = Up(gpu, keep, new float[n]);
            float eps = 1e-5f;
            Run1(gpu, Kernels.RmsNorm(), new GpuBuffer[] { gx, gw, gy }, new uint[] { (uint)n, *(uint*)&eps }, 1);
            Report("rmsnorm", want, Down(gy, n), 1e-5f, fail, output);
            foreach (GpuBuffer b in keep) b.Dispose();
        }

        static void SiluMul(GpuDevice gpu, List<string> fail, TextWriter output)
        {
            Random r = new Random(4);
            int n = 1000;
            float[] g = Rand(r, n, 8), u = Rand(r, n, 2), want = new float[n];
            for (int i = 0; i < n; i++) want[i] = CpuRef.Silu(g[i]) * u[i];
            List<GpuBuffer> keep = new List<GpuBuffer>();
            GpuBuffer gg = Up(gpu, keep, g), gu = Up(gpu, keep, u);
            Run1(gpu, Kernels.SiluMul(), new GpuBuffer[] { gg, gu }, new uint[] { (uint)n }, (uint)((n + 63) / 64));
            Report("silu*up", want, Down(gg, n), 1e-5f, fail, output);
            foreach (GpuBuffer b in keep) b.Dispose();
        }

        static void Quant(GpuDevice gpu, List<string> fail, TextWriter output)
        {
            Random r = new Random(5);
            int n = 4096, nb = n / 32;
            float[] x = Rand(r, n, 4);
            CpuRef.Q8Activations q = new CpuRef.Q8Activations(n);
            CpuRef.QuantizeQ8_0(x, q);
            List<GpuBuffer> keep = new List<GpuBuffer>();
            GpuBuffer gx = Up(gpu, keep, x), gq = Up(gpu, keep, new float[n / 4]), gd = Up(gpu, keep, new float[nb]);
            Run1(gpu, KernelsQuant.Q80(), new GpuBuffer[] { gx, gq, gd }, new uint[] { (uint)nb, (uint)n, 0 }, (uint)((nb + 7) / 8));
            float[] d = Down(gd, nb), words = Down(gq, n / 4);
            int qBad = 0, dBad = 0;
            for (int b = 0; b < nb; b++) if (d[b] != Half.ToFloat(q.D[b])) dBad++;
            for (int i = 0; i < n; i++)
            {
                uint w = BitConverter.ToUInt32(BitConverter.GetBytes(words[i / 4]), 0);
                sbyte v = (sbyte)(w >> (8 * (i % 4)));
                if (v != q.Qs[i]) qBad++;
            }
            output.WriteLine("  {0,-10} scale mismatches {1}/{2}, quant mismatches {3}/{4}", "quant q8_0", dBad, nb, qBad, n);
            if (dBad > 0 || qBad > n / 1000) fail.Add("quantize: " + dBad + " scale and " + qBad + " value mismatches");
            foreach (GpuBuffer b in keep) b.Dispose();
            QuantK(gpu, fail, output);
        }

        static void QuantK(GpuDevice gpu, List<string> fail, TextWriter output)
        {
            Random r = new Random(6);
            int n = 4096, nb = n / 256;
            float[] x = Rand(r, n, 3);
            x[300] = 5f; x[301] = -5f;   // a tie in max |x| (first index must win)
            CpuRef.Q8Activations q = new CpuRef.Q8Activations(n);
            CpuRef.QuantizeQ8_K(x, q);
            List<GpuBuffer> keep = new List<GpuBuffer>();
            GpuBuffer gx = Up(gpu, keep, x), gq = Up(gpu, keep, new float[n / 4]), gd = Up(gpu, keep, new float[nb]);
            Run1(gpu, KernelsQuant.Q8K(), new GpuBuffer[] { gx, gq, gd }, new uint[] { (uint)nb, (uint)n, 0 }, (uint)nb);
            float[] d = Down(gd, nb), words = Down(gq, n / 4);
            int qBad = 0, dBad = 0;
            for (int b = 0; b < nb; b++) if (Math.Abs(d[b] - q.KD[b]) > 1e-6f * Math.Abs(q.KD[b])) dBad++;
            for (int i = 0; i < n; i++)
            {
                uint w = BitConverter.ToUInt32(BitConverter.GetBytes(words[i / 4]), 0);
                if ((sbyte)(w >> (8 * (i % 4))) != q.KQs[i]) qBad++;
            }
            output.WriteLine("  {0,-10} scale mismatches {1}/{2}, quant mismatches {3}/{4}", "quant q8_K", dBad, nb, qBad, n);
            if (dBad > 0 || qBad > 0) fail.Add("quant q8_K: " + dBad + " scale and " + qBad + " value mismatches");
            foreach (GpuBuffer b in keep) b.Dispose();
        }
    }
}
