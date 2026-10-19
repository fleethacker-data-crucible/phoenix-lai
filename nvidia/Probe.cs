// NVIDIA probe: everything needed to fix Phoenix for a new GPU without touching the production machine. Compiled with
// src\*.cs and nvidia\tests\*.cs by nvidia\2-probe.ps1 (never by the server). Writes as it goes, so a crash or
// a GPU reset still leaves everything up to that point in the report. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Phoenix.Nvidia
{
    public static unsafe class Probe
    {
        [DllImport("vulkan-1.dll")] static extern int vkEnumerateDeviceExtensionProperties(IntPtr phys, IntPtr layer, uint* count, byte* props);

        static TextWriter _out;

        static void W(string s) { _out.WriteLine(s); _out.Flush(); Console.WriteLine(s); }

        static void Step(string name, Action a)
        {
            W("");
            W("=== " + name + "  [" + DateTime.Now.ToString("HH:mm:ss") + "]");
            Stopwatch sw = Stopwatch.StartNew();
            try { a(); W("--- " + name + " done in " + sw.Elapsed.TotalSeconds.ToString("F1") + " s"); }
            catch (Exception ex) { W("!!! " + name + " FAILED after " + sw.Elapsed.TotalSeconds.ToString("F1") + " s: " + ex); }
        }

        // root: the Phoenix folder; report: the file to write; models: chat models to test (full paths); refs: the
        // nvidia\ref folder (llama.cpp reference log-probabilities), or null
        public static int Run(string root, string report, string[] models, string refs)
        {
            int failures = 0;
            using (_out = new StreamWriter(report, false, new UTF8Encoding(false)))
            {
                W("Phoenix NVIDIA probe, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " on " + Environment.MachineName);
                W("root " + root);
                W(".NET " + Environment.Version + ", 64-bit process " + Environment.Is64BitProcess + ", " + Environment.ProcessorCount + " CPU threads");

                Step("Vulkan devices (raw)", DumpDevices);

                foreach (bool safe in new bool[] { false, true })
                {
                    string mode = safe ? "safe" : "normal";
                    Environment.SetEnvironmentVariable("PHOENIX_GPU_SAFE", safe ? "1" : "0");
                    foreach (string profile in new string[] { "auto", "nvidia" })
                    {
                        Environment.SetEnvironmentVariable("PHOENIX_GPU_PROFILE", profile);
                        Step("device picked: profile " + profile + ", " + mode, delegate()
                        {
                            using (GpuDevice d = GpuDevice.Create()) W(d.Describe());
                        });
                    }
                    Environment.SetEnvironmentVariable("PHOENIX_GPU_PROFILE", "nvidia");

                    Type kt = Type.GetType("Phoenix.Tests.KernelTests");
                    if (kt == null) W("(kernel tests not compiled in: nvidia\\tests is missing)");
                    else Step("kernel tests, GPU vs CPU (" + mode + ")", delegate()
                    {
                        GpuDevice.LongestJobMs = 0;
                        List<string> fail = (List<string>)kt.GetMethod("Run").Invoke(null, new object[] { _out });
                        _out.Flush();
                        W(fail.Count == 0 ? "KERNELS PASS (" + mode + ")" : "KERNELS FAIL (" + mode + "): " + string.Join(" | ", fail.ToArray()));
                        if (fail.Count > 0) failures++;
                    });

                    foreach (string model in models)
                    {
                        string name = Path.GetFileNameWithoutExtension(model);
                        Type gt = Type.GetType("Phoenix.Tests.GemmaGate");
                        if (gt == null || refs == null || !File.Exists(Path.Combine(refs, name, "gemma-logprobs.tsv")))
                            W("(no llama.cpp reference for " + name + ": accuracy gate skipped)");
                        else Step("accuracy vs llama.cpp: " + name + " (" + mode + ")", delegate()
                        {
                            GpuDevice.LongestJobMs = 0;
                            List<string> fail = (List<string>)gt.GetMethod("Run").Invoke(null, new object[] { model, refs, false, _out });
                            _out.Flush();
                            W((fail.Count == 0 ? "GATE PASS " : "GATE FAIL ") + name + " (" + mode + ")" + (fail.Count > 0 ? ": " + string.Join(" | ", fail.ToArray()) : "") +
                              "; longest GPU job " + GpuDevice.LongestJobMs.ToString("F0") + " ms");
                            if (fail.Count > 0) failures++;
                        });
                        Step("speed: " + name + " (" + mode + ")", delegate() { Speed(model); });
                    }
                }
                Environment.SetEnvironmentVariable("PHOENIX_GPU_SAFE", null);
                Environment.SetEnvironmentVariable("PHOENIX_GPU_PROFILE", null);
                W("");
                W(failures == 0 ? "PROBE RESULT: all checks passed" : "PROBE RESULT: " + failures + " check(s) failed (see FAIL lines above)");
            }
            return failures;
        }

        // Load time, prompt (prefill) and generation speed, longest single GPU job and VRAM, like the server would use it.
        static void Speed(string model)
        {
            using (GgufFile g = GgufFile.Open(model))
            using (GpuDevice gpu = GpuDevice.Create())
            {
                W(gpu.Describe());
                GpuDevice.LongestJobMs = 0;
                Stopwatch sw = Stopwatch.StartNew();
                using (GemmaGpu m = new GemmaGpu(gpu, g, 4096, KvType.F16))
                {
                    double load = sw.Elapsed.TotalSeconds;
                    W("load " + load.ToString("F1") + " s, weights " + (m.WeightBytes / 1e9).ToString("F2") + " GB, KV " + (m.KvBytes / 1e6).ToString("F0") + " MB" +
                      (gpu.FallbackBytes > 0 ? ", " + (gpu.FallbackBytes >> 20) + " MB had to go to system memory (VRAM full)" : ""));
                    Random rng = new Random(1);
                    foreach (int pp in new int[] { 128, 1024, 3000 })
                    {
                        int[] prompt = new int[pp];
                        for (int i = 0; i < pp; i++) prompt[i] = 1000 + rng.Next(20000);
                        m.Reset();
                        GpuDevice.LongestJobMs = 0;
                        sw.Restart();
                        float[] logits = m.Prefill(prompt, 0, pp);
                        double ps = sw.Elapsed.TotalSeconds;
                        double prefillJob = GpuDevice.LongestJobMs;
                        int tok = Argmax(logits), gen = 64;
                        GpuDevice.LongestJobMs = 0;
                        sw.Restart();
                        for (int i = 0; i < gen; i++) tok = Argmax(m.Forward(tok, pp + i));
                        double gs = sw.Elapsed.TotalSeconds;
                        W("prompt " + pp + " tokens: " + (pp / ps).ToString("F0") + " tok/s (" + ps.ToString("F2") + " s, longest GPU job " + prefillJob.ToString("F0") +
                          " ms); then " + gen + " generated: " + (gen / gs).ToString("F1") + " tok/s (longest GPU job " + GpuDevice.LongestJobMs.ToString("F0") + " ms)");
                    }
                    ulong used, budget;
                    if (gpu.MemoryBudget(out used, out budget)) W("VRAM after the runs: " + (used >> 20) + " MB used of a " + (budget >> 20) + " MB budget");
                }
            }
        }

        static int Argmax(float[] l) { int b = 0; for (int i = 1; i < l.Length; i++) if (l[i] > l[b]) b = i; return b; }

        static string Str(byte* p, int max) { int n = 0; while (n < max && p[n] != 0) n++; return Encoding.UTF8.GetString(p, n); }

        static string Ver(uint v) { return (v >> 22) + "." + ((v >> 12) & 0x3FF) + "." + (v & 0xFFF); }

        static void Zero(byte* p, int n) { for (int i = 0; i < n; i++) p[i] = 0; }

        // Every Vulkan device: properties, limits the kernels depend on, subgroups, features, memory heaps and types,
        // driver name and the full extension list.
        static void DumpDevices()
        {
            VkApplicationInfo app = new VkApplicationInfo();
            app.sType = VkSType.ApplicationInfo;
            app.apiVersion = VkConst.MakeApiVersion(1, 3, 0);
            VkInstanceCreateInfo ici = new VkInstanceCreateInfo();
            ici.sType = VkSType.InstanceCreateInfo;
            ici.pApplicationInfo = (IntPtr)(&app);
            IntPtr inst;
            Vk.Check(Vk.vkCreateInstance(&ici, IntPtr.Zero, &inst), "vkCreateInstance");
            try
            {
                uint count = 0;
                Vk.Check(Vk.vkEnumeratePhysicalDevices(inst, &count, null), "vkEnumeratePhysicalDevices");
                W(count + " Vulkan device(s)");
                IntPtr[] devs = new IntPtr[count];
                fixed (IntPtr* pd = devs) Vk.Check(Vk.vkEnumeratePhysicalDevices(inst, &count, pd), "vkEnumeratePhysicalDevices");
                for (int d = 0; d < count; d++)
                {
                    IntPtr ph = devs[d];
                    byte* p = stackalloc byte[1024];
                    Zero(p, 1024);
                    Vk.vkGetPhysicalDeviceProperties(ph, p);
                    uint api = *(uint*)p, drv = *(uint*)(p + 4), vendor = *(uint*)(p + 8), devId = *(uint*)(p + 12), type = *(uint*)(p + 16);
                    W("");
                    W("[device " + d + "] " + Str(p + 20, 256));
                    W("  vendor 0x" + vendor.ToString("X4") + ", device 0x" + devId.ToString("X4") + ", type " + type + " (1 integrated, 2 discrete), Vulkan " + Ver(api) +
                      ", driverVersion raw 0x" + drv.ToString("X8"));
                    byte* lim = p + 296;
                    W("  limits: maxStorageBufferRange " + *(uint*)(lim + 28) + ", maxPushConstantsSize " + *(uint*)(lim + 32) + ", maxMemoryAllocationCount " + *(uint*)(lim + 36) +
                      ", maxComputeSharedMemorySize " + *(uint*)(lim + 216) + ", maxComputeWorkGroupCount " + *(uint*)(lim + 220) + "x" + *(uint*)(lim + 224) + "x" + *(uint*)(lim + 228) +
                      ", maxComputeWorkGroupInvocations " + *(uint*)(lim + 232) + ", maxComputeWorkGroupSize " + *(uint*)(lim + 236) + "x" + *(uint*)(lim + 240) + "x" + *(uint*)(lim + 244) +
                      ", timestampPeriod " + *(float*)(p + 720) + " ns");

                    if (api >= VkConst.MakeApiVersion(1, 1, 0))
                    {
                        // subgroup + Vulkan 1.2 properties (driver name) + Vulkan 1.3 properties (subgroup size range)
                        byte* sub = stackalloc byte[32]; Zero(sub, 32);
                        byte* v12 = stackalloc byte[1024]; Zero(v12, 1024);
                        byte* v13 = stackalloc byte[512]; Zero(v13, 512);
                        byte* p2 = stackalloc byte[16 + 824 + 64]; Zero(p2, 16 + 824 + 64);
                        *(int*)sub = 1000094000;
                        *(int*)p2 = 1000059001;
                        *(IntPtr*)(p2 + 8) = (IntPtr)sub;
                        if (api >= VkConst.MakeApiVersion(1, 2, 0)) { *(int*)v12 = 52; *(IntPtr*)(sub + 8) = (IntPtr)v12; }
                        if (api >= VkConst.MakeApiVersion(1, 3, 0)) { *(int*)v13 = 54; *(IntPtr*)(v12 + 8) = (IntPtr)v13; }
                        Vk.vkGetPhysicalDeviceProperties2(ph, p2);
                        W("  subgroup: size " + *(uint*)(sub + 16) + ", stages 0x" + (*(uint*)(sub + 20)).ToString("X") + ", operations 0x" + (*(uint*)(sub + 24)).ToString("X") +
                          " (0x4 = arithmetic), quad in all stages " + *(uint*)(sub + 28));
                        if (api >= VkConst.MakeApiVersion(1, 2, 0))
                            W("  driver: id " + *(uint*)(v12 + 16) + ", name '" + Str(v12 + 20, 256) + "', info '" + Str(v12 + 276, 256) + "'");
                        if (api >= VkConst.MakeApiVersion(1, 3, 0))
                            W("  subgroup size control: min " + *(uint*)(v13 + 16) + ", max " + *(uint*)(v13 + 20) + ", max subgroups per workgroup " + *(uint*)(v13 + 24) +
                              ", required-size stages 0x" + (*(uint*)(v13 + 28)).ToString("X"));

                        // features: Vulkan 1.1 (16-bit storage), 1.2 (8-bit storage, float16, int8), 1.3 integer dot product
                        byte* f11 = stackalloc byte[128]; Zero(f11, 128);
                        byte* f12 = stackalloc byte[256]; Zero(f12, 256);
                        byte* dot = stackalloc byte[24]; Zero(dot, 24);
                        byte* f2 = stackalloc byte[16 + 224 + 16]; Zero(f2, 16 + 224 + 16);
                        *(int*)f2 = 1000059000;
                        if (api >= VkConst.MakeApiVersion(1, 2, 0))
                        {
                            *(int*)f11 = 49; *(int*)f12 = 51;
                            *(IntPtr*)(f2 + 8) = (IntPtr)f11; *(IntPtr*)(f11 + 8) = (IntPtr)f12;
                            if (api >= VkConst.MakeApiVersion(1, 3, 0)) { *(int*)dot = 1000280000; *(IntPtr*)(f12 + 8) = (IntPtr)dot; }
                        }
                        Vk.vkGetPhysicalDeviceFeatures2(ph, f2);
                        if (api >= VkConst.MakeApiVersion(1, 2, 0))
                            W("  features: storageBuffer16BitAccess " + *(uint*)(f11 + 16) + ", storageBuffer8BitAccess " + *(uint*)(f12 + 24) + ", shaderFloat16 " + *(uint*)(f12 + 44) +
                              ", shaderInt8 " + *(uint*)(f12 + 48) + (api >= VkConst.MakeApiVersion(1, 3, 0) ? ", shaderIntegerDotProduct " + *(uint*)(dot + 16) : ""));
                    }

                    byte* mp = stackalloc byte[1024]; Zero(mp, 1024);
                    Vk.vkGetPhysicalDeviceMemoryProperties(ph, mp);
                    uint types = *(uint*)mp, heaps = *(uint*)(mp + 260);
                    for (int h = 0; h < heaps; h++)
                        W("  heap " + h + ": " + (*(ulong*)(mp + 264 + h * 16) >> 20) + " MB, flags 0x" + (*(uint*)(mp + 264 + h * 16 + 8)).ToString("X") + " (1 = device-local)");
                    for (int t = 0; t < types; t++)
                    {
                        uint fl = *(uint*)(mp + 4 + t * 8);
                        W("  memory type " + t + ": heap " + *(uint*)(mp + 4 + t * 8 + 4) + ", flags 0x" + fl.ToString("X") + " (" +
                          ((fl & 1) != 0 ? "device-local " : "") + ((fl & 2) != 0 ? "host-visible " : "") + ((fl & 4) != 0 ? "coherent " : "") + ((fl & 8) != 0 ? "cached " : "") + ")");
                    }

                    uint n = 0;
                    vkEnumerateDeviceExtensionProperties(ph, IntPtr.Zero, &n, null);
                    byte[] ext = new byte[Math.Max(1, n) * 260];
                    fixed (byte* e = ext) vkEnumerateDeviceExtensionProperties(ph, IntPtr.Zero, &n, e);
                    List<string> names = new List<string>();
                    fixed (byte* e = ext) for (int i = 0; i < n; i++) names.Add(Str(e + i * 260, 256));
                    names.Sort(StringComparer.Ordinal);
                    W("  " + n + " extensions: " + string.Join(" ", names.ToArray()));
                }
            }
            finally { Vk.vkDestroyInstance(inst, IntPtr.Zero); }
        }
    }
}
