// KernelsFlash2 vs KernelsPrefill.Attention (plain, per query) on random data. C# 5 only.
using System;
using System.IO;

namespace Phoenix.Tests
{
    public static class Flash2Test
    {
        public static bool Run(TextWriter o)
        {
            bool ok = true;
            using (GpuDevice gpu = GpuDevice.Create())
            {
                ok &= Case(gpu, o, 256, 16, 8, 1536, 1024, 100, 70);
                ok &= Case(gpu, o, 256, 16, 8, 1536, 1024, 1500, 512);
                ok &= Case(gpu, o, 512, 16, 1, 3000, 0, 0, 512);
                ok &= Case(gpu, o, 512, 16, 1, 3000, 0, 1037, 33);
            }
            return ok;
        }

        static bool Case(GpuDevice gpu, TextWriter o, int hd, int nHead, int nKv, int ring, int window, int p0, int T)
        {
            Random rng = new Random(hd + p0 + T);
            int kvDim = nKv * hd;
            float[] q = new float[T * nHead * hd];
            for (int i = 0; i < q.Length; i++) q[i] = (float)(rng.NextDouble() * 2 - 1);
            uint[] kc = new uint[kvDim * ring / 2], vc = new uint[ring * kvDim / 2];
            for (int i = 0; i < kc.Length; i++) { kc[i] = Pack(rng); vc[i] = Pack(rng); }
            GpuBuffer bq = gpu.CreateBuffer((ulong)q.Length * 4, BufferKind.Upload), bk = gpu.CreateBuffer((ulong)kc.Length * 4, BufferKind.Upload);
            GpuBuffer bv = gpu.CreateBuffer((ulong)vc.Length * 4, BufferKind.Upload);
            GpuBuffer o1 = gpu.CreateBuffer((ulong)q.Length * 4, BufferKind.Readback), o2 = gpu.CreateBuffer((ulong)q.Length * 4, BufferKind.Readback);
            bq.Upload(q, 0); gpu.Upload(bk, kc); gpu.Upload(bv, vc);
            GpuKernel k1 = gpu.CreateKernel(KernelsPrefill.Attention(KvType.F16, KvType.F16), 4, 32, 4);
            GpuKernel k2 = gpu.CreateKernel(KernelsFlash2.Attention((uint)hd), 4, 36, 4);
            uint rows, keys;
            KernelsFlash2.Tile((uint)hd, out rows, out keys);
            uint P = rows / (uint)(nHead / nKv);
            gpu.Begin();
            gpu.Record(k1, new GpuBuffer[] { bq, bk, bv, o1 }, new uint[] { (uint)hd, (uint)kvDim, (uint)ring, (uint)nHead, (uint)nKv, (uint)p0, (uint)window, Bits(1f) }, (uint)T, (uint)nHead, 1);
            gpu.Record(k2, new GpuBuffer[] { bq, bk, bv, o2 }, new uint[] { (uint)hd, (uint)kvDim, (uint)ring, (uint)nHead, (uint)nKv, (uint)p0, (uint)window, Bits(1f), (uint)T },
                ((uint)T + P - 1) / P, (uint)nKv, 1);
            gpu.SubmitAndWait();
            float[] a = new float[q.Length], b = new float[q.Length];
            o1.Download(a, 0); o2.Download(b, 0);
            double md = 0; int at = 0;
            for (int i = 0; i < a.Length; i++) { double d = Math.Abs(a[i] - b[i]); if (!(d <= md)) { md = d; at = i; } }
            bool pass = md < 1e-4;
            o.WriteLine("flash2 hd {0} heads {1}/{2} p0 {3} T {4} window {5}: max diff {6:G3} at {7} ({8} vs {9}) {10}", hd, nHead, nKv, p0, T, window, md, at, a[at], b[at], pass ? "ok" : "FAIL");
            k1.Dispose(); k2.Dispose(); bq.Dispose(); bk.Dispose(); bv.Dispose(); o1.Dispose(); o2.Dispose();
            return pass;
        }

        static uint Pack(Random rng)
        {
            return (uint)Half.FromFloat((float)(rng.NextDouble() * 2 - 1)) | ((uint)Half.FromFloat((float)(rng.NextDouble() * 2 - 1)) << 16);
        }

        static uint Bits(float f) { return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0); }
    }
}
