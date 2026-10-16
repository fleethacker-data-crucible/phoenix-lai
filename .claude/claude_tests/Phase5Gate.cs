// Phase 5 gate: Vulkan device + SPIR-V "add" shader must equal the CPU result exactly. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Phoenix.Tests
{
    public static class Phase5Gate
    {
        public static List<string> Run(TextWriter output)
        {
            List<string> fail = new List<string>();

            uint[] spirv = Shaders.AddF32();
            if (spirv.Length < 5 || spirv[0] != SpirvBuilder.Magic) fail.Add("SPIR-V module has a bad header");
            output.WriteLine("spirv: add shader is {0} words, id bound {1}", spirv.Length, spirv.Length > 3 ? spirv[3] : 0);

            using (GpuDevice gpu = GpuDevice.Create())
            {
                output.WriteLine("device: {0} (Vulkan {1}, vendor 0x{2:X4}, type {3}, {4:N1} GB device-local)",
                    gpu.DeviceName, VkConst.ApiVersionString(gpu.ApiVersion), gpu.VendorId, gpu.DeviceType, gpu.DeviceLocalBytes / 1073741824.0);

                using (GpuKernel add = gpu.CreateKernel(spirv, 3, 4, 4))
                {
                    // Sizes that are not multiples of the workgroup size exercise the bounds check.
                    foreach (int n in new int[] { 1, 63, 64, 65, 1000003 })
                        CheckAdd(gpu, add, n, fail, output);
                }
            }
            return fail;
        }

        static void CheckAdd(GpuDevice gpu, GpuKernel add, int n, List<string> fail, TextWriter output)
        {
            const float Sentinel = -12345.5f;
            Random rng = new Random(n);
            float[] a = new float[n], b = new float[n], c = new float[n + 1];
            for (int i = 0; i < n; i++) { a[i] = (float)(rng.NextDouble() * 200 - 100); b[i] = (float)(rng.NextDouble() * 200 - 100); }

            ulong bytes = (ulong)(n + 1) * 4; // one extra element that must stay untouched
            using (GpuBuffer ga = gpu.CreateBuffer(bytes, true))
            using (GpuBuffer gb = gpu.CreateBuffer(bytes, true))
            using (GpuBuffer gc = gpu.CreateBuffer(bytes, true))
            {
                ga.Upload(a, 0);
                gb.Upload(b, 0);
                float[] fill = new float[n + 1];
                for (int i = 0; i <= n; i++) fill[i] = Sentinel;
                gc.Upload(fill, 0);

                byte[] push = BitConverter.GetBytes((uint)n);
                uint groups = (uint)((n + Shaders.AddLocalSize - 1) / Shaders.AddLocalSize);
                Stopwatch sw = Stopwatch.StartNew();
                gpu.Dispatch(add, new GpuBuffer[] { ga, gb, gc }, push, groups, 1, 1);
                sw.Stop();
                gc.Download(c, 0);
            }

            int bad = 0;
            for (int i = 0; i < n; i++)
            {
                float want = a[i] + b[i];
                if (BitConverter.ToInt32(BitConverter.GetBytes(c[i]), 0) != BitConverter.ToInt32(BitConverter.GetBytes(want), 0))
                {
                    if (bad < 3) fail.Add("n=" + n + " c[" + i + "] = " + c[i].ToString("R") + ", CPU " + want.ToString("R"));
                    bad++;
                }
            }
            if (c[n] != Sentinel) fail.Add("n=" + n + " wrote past the end (c[n] = " + c[n] + ")");
            output.WriteLine("  add n={0,-8} mismatches {1}", n, bad);
        }
    }
}
