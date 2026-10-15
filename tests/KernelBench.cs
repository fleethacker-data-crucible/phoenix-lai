// Kernel microbenchmark: a k-quant matvec variant over several real weight tensors (more bytes than the GPU's
// last-level cache, so weights stream from VRAM as in real decoding). Reports GB/s of weight bytes. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static unsafe class KernelBench
    {
        public delegate uint[] Variant();

        // tensorFormat: e.g. "blk.{0}.ffn_up.weight", layers 0..count-1. Kernel bindings: w, kq, kd, y; push rows, nb, gx.
        // padTo: per-block byte stride on the GPU (0 = native block size).
        public static double MatVecK(string modelPath, string tensorFormat, int count, string label, Variant variant, int rowsPerGroup, int padTo, TextWriter output)
        {
            using (GgufFile g = GgufFile.Open(modelPath))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                List<GpuBuffer> weights = new List<GpuBuffer>();
                GgufTensor t0 = g.TensorsByName[string.Format(tensorFormat, 0)];
                Weight w0 = new Weight(t0);
                int nb = w0.Cols / CpuRef.QK_K, native = CpuRef.KBlockBytes(t0.Type), stride = padTo > 0 ? padTo : native;
                long totalBytes = 0;
                try
                {
                    for (int li = 0; li < count; li++)
                    {
                        GgufTensor t = g.TensorsByName[string.Format(tensorFormat, li)];
                        long nBlocks = t.SizeBytes / native;
                        byte[] blocks = new byte[nBlocks * stride];
                        for (long b = 0; b < nBlocks; b++)
                            for (int j = 0; j < native; j++) blocks[b * stride + j] = t.Data[b * native + j];
                        GpuBuffer gw = gpu.CreateBuffer((ulong)blocks.Length, false);
                        weights.Add(gw);
                        fixed (byte* pb = blocks) gpu.Upload(gw, 0, (IntPtr)pb, (ulong)blocks.Length);
                        totalBytes += t.SizeBytes;
                    }

                    Random r = new Random(3);
                    float[] x = new float[w0.Cols];
                    for (int i = 0; i < x.Length; i++) x[i] = (float)(r.NextDouble() * 2 - 1);
                    float[] want = new float[w0.Rows];
                    CpuRef.MatVec(w0, x, new CpuRef.Q8Activations(w0.Cols), want);

                    using (GpuBuffer gx = gpu.CreateBuffer((ulong)x.Length * 4, BufferKind.Upload))
                    using (GpuBuffer gq = gpu.CreateBuffer((ulong)x.Length, false))
                    using (GpuBuffer gd = gpu.CreateBuffer((ulong)(nb + 1) * 4, false))
                    using (GpuBuffer gy = gpu.CreateBuffer((ulong)w0.Rows * 4, true))
                    using (GpuKernel kq = gpu.CreateKernel(KernelsK.QuantizeQ8K(), 3, 4, 2))
                    using (GpuKernel km = gpu.CreateKernel(variant(), 4, 16, (uint)count + 2))
                    {
                        km.Name = label;
                        gx.Upload(x, 0);
                        int groups = (w0.Rows + rowsPerGroup - 1) / rowsPerGroup;
                        uint gxs = (uint)Math.Min(groups, 65535), gys = (uint)((groups + gxs - 1) / gxs);
                        uint[] push = { (uint)w0.Rows, (uint)nb, gxs, 0 };

                        gpu.Begin();
                        gpu.Record(kq, new GpuBuffer[] { gx, gq, gd }, new uint[] { (uint)nb }, (uint)((nb + 63) / 64), 1, 1);
                        gpu.SubmitAndWait();

                        Stopwatch warm = Stopwatch.StartNew();
                        while (warm.ElapsedMilliseconds < 600)
                        {
                            gpu.Begin();
                            for (int i = 0; i < 40; i++) gpu.Record(km, new GpuBuffer[] { weights[i % count], gq, gd, gy }, push, gxs, gys, 1);
                            gpu.SubmitAndWait();
                        }
                        gpu.ProfileMs.Clear(); gpu.ProfileCount.Clear(); gpu.Profiling = true;
                        int reps = 20 * count;
                        gpu.Begin();
                        for (int i = 0; i < reps; i++) gpu.Record(km, new GpuBuffer[] { weights[i % count], gq, gd, gy }, push, gxs, gys, 1);
                        gpu.SubmitAndWait();
                        gpu.Profiling = false;
                        double ms = gpu.ProfileMs[label] / reps;

                        // Correctness on layer 0.
                        gpu.Begin();
                        gpu.Record(km, new GpuBuffer[] { weights[0], gq, gd, gy }, push, gxs, gys, 1);
                        gpu.SubmitAndWait();
                        float[] got = new float[w0.Rows];
                        gy.Download(got, 0);
                        double worst = 0;
                        for (int i = 0; i < w0.Rows; i++) worst = Math.Max(worst, Math.Abs(want[i] - got[i]) / Math.Max(1.0, Math.Abs(want[i])));
                        double gbs = (totalBytes / count) / (ms / 1000) / 1e9;
                        output.WriteLine("  {0,-30} {1} x{2} [{3}x{4}]  {5,7:N3} ms  {6,6:N1} GB/s  rel err {7:G2}",
                            label, GgmlTypes.Name(t0.Type), count, w0.Rows, w0.Cols, ms, gbs, worst);
                        return gbs;
                    }
                }
                finally { foreach (GpuBuffer b in weights) b.Dispose(); }
            }
        }
    }
}
