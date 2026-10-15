// Vulkan compute device, buffers and kernels. C# 5 only.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Phoenix
{
    // Where a buffer lives.
    //   Device:   VRAM, GPU only.
    //   Upload:   written by the CPU every step, read by the GPU: VRAM through the CPU-visible window (resizable BAR / 256 MB BAR)
    //             when available, else host memory.
    //   Readback: written by the GPU, read by the CPU: host-cached memory.
    public enum BufferKind { Device, Upload, Readback }

    public sealed unsafe class GpuBuffer : IDisposable
    {
        public ulong Buffer, Memory, Size;
        public IntPtr Mapped;    // non-zero for host-visible buffers
        public bool DeviceLocal;
        internal IntPtr Device;

        public void Upload(float[] src, long dstOffsetFloats)
        {
            RequireMapped();
            if ((dstOffsetFloats + src.Length) * 4 > (long)Size) throw new ArgumentOutOfRangeException("src");
            Marshal.Copy(src, 0, Mapped + (int)(dstOffsetFloats * 4), src.Length);
        }

        public unsafe void UploadWords(uint[] src)
        {
            RequireMapped();
            if ((long)src.Length * 4 > (long)Size) throw new ArgumentOutOfRangeException("src");
            uint* d = (uint*)Mapped;
            for (int i = 0; i < src.Length; i++) d[i] = src[i];
        }

        public void Download(float[] dst, long srcOffsetFloats)
        {
            RequireMapped();
            if ((srcOffsetFloats + dst.Length) * 4 > (long)Size) throw new ArgumentOutOfRangeException("dst");
            Marshal.Copy(Mapped + (int)(srcOffsetFloats * 4), dst, 0, dst.Length);
        }

        void RequireMapped()
        {
            if (Mapped == IntPtr.Zero) throw new InvalidOperationException("Buffer is not host-visible.");
        }

        public void Dispose()
        {
            if (Device == IntPtr.Zero) return;
            if (Mapped != IntPtr.Zero) { Vk.vkUnmapMemory(Device, Memory); Mapped = IntPtr.Zero; }
            Vk.vkDestroyBuffer(Device, Buffer, IntPtr.Zero);
            Vk.vkFreeMemory(Device, Memory, IntPtr.Zero);
            Device = IntPtr.Zero;
        }
    }

    public sealed unsafe class GpuKernel : IDisposable
    {
        public ulong Module, SetLayout, PipelineLayout, Pipeline, DescriptorPool;
        public int BindingCount;
        public uint PushBytes;
        public string Name = "kernel";
        internal IntPtr Device;
        internal readonly Dictionary<string, ulong> Sets = new Dictionary<string, ulong>(); // cached by buffer tuple

        public void Dispose()
        {
            if (Device == IntPtr.Zero) return;
            Vk.vkDestroyPipeline(Device, Pipeline, IntPtr.Zero);
            Vk.vkDestroyPipelineLayout(Device, PipelineLayout, IntPtr.Zero);
            Vk.vkDestroyDescriptorPool(Device, DescriptorPool, IntPtr.Zero);
            Vk.vkDestroyDescriptorSetLayout(Device, SetLayout, IntPtr.Zero);
            Vk.vkDestroyShaderModule(Device, Module, IntPtr.Zero);
            Device = IntPtr.Zero;
        }
    }

    public sealed unsafe class GpuDevice : IDisposable
    {
        public IntPtr Instance, Physical, Device, Queue, Cmd;
        public uint QueueFamily;
        public string DeviceName;
        public uint ApiVersion, VendorId, DeviceType;
        public ulong DeviceLocalBytes;
        public ulong FallbackBytes;
        public uint SubgroupSize;          // threads per subgroup (wave) in compute shaders
        public bool SubgroupArithmetic;    // subgroupAdd etc. supported in compute shaders
        public bool IntegerDot;            // packed 4x8-bit integer dot product (shaderIntegerDotProduct) enabled
        public bool IntegerDotAccelerated; // ...and the hardware has native instructions for it   // "device-local" buffers that had to live in system memory
        const int VkErrorOutOfDeviceMemory = -2;

        ulong _cmdPool, _fence;
        bool _recording;

        // GPU timestamp profiling: per-kernel-name total milliseconds across submits.
        public bool Profiling;
        public float TimestampPeriodNs;
        public readonly Dictionary<string, double> ProfileMs = new Dictionary<string, double>();
        public readonly Dictionary<string, int> ProfileCount = new Dictionary<string, int>();
        const uint MaxQueries = 8192;
        ulong _queryPool;
        readonly List<string> _queryLabels = new List<string>();
        GpuBuffer _staging;
        const ulong StagingBytes = 64UL << 20;
        readonly uint[] _memFlags = new uint[32];
        uint _memTypeCount;
        readonly List<IntPtr> _strings = new List<IntPtr>();

        public static GpuDevice Create()
        {
            GpuDevice d = new GpuDevice();
            try { d.Init(); }
            catch { d.Dispose(); throw; }
            return d;
        }

        IntPtr Str(string s)
        {
            IntPtr p = Marshal.StringToHGlobalAnsi(s);
            _strings.Add(p);
            return p;
        }

        void Init()
        {
            // Instance
            VkApplicationInfo app = new VkApplicationInfo();
            app.sType = VkSType.ApplicationInfo;
            app.pApplicationName = Str("Local LLM Studio");
            app.pEngineName = Str("Phoenix");
            app.apiVersion = VkConst.MakeApiVersion(1, 3, 0);
            VkInstanceCreateInfo ici = new VkInstanceCreateInfo();
            ici.sType = VkSType.InstanceCreateInfo;
            ici.pApplicationInfo = (IntPtr)(&app);
            IntPtr inst;
            Vk.Check(Vk.vkCreateInstance(&ici, IntPtr.Zero, &inst), "vkCreateInstance");
            Instance = inst;

            // Physical device: prefer a discrete GPU with a compute queue.
            uint count = 0;
            Vk.Check(Vk.vkEnumeratePhysicalDevices(Instance, &count, null), "vkEnumeratePhysicalDevices");
            if (count == 0) throw new InvalidOperationException("No Vulkan devices found.");
            IntPtr[] devs = new IntPtr[count];
            fixed (IntPtr* pd = devs) Vk.Check(Vk.vkEnumeratePhysicalDevices(Instance, &count, pd), "vkEnumeratePhysicalDevices");

            int best = -1, bestScore = -1;
            uint bestFamily = 0;
            for (int i = 0; i < count; i++)
            {
                uint family;
                if (!FindComputeQueue(devs[i], out family)) continue;
                byte* props = stackalloc byte[1024];
                Vk.vkGetPhysicalDeviceProperties(devs[i], props);
                uint type = *(uint*)(props + 16);
                int score = type == 2 ? 3 : type == 1 ? 2 : 1; // discrete > integrated > other
                if (score > bestScore) { best = i; bestScore = score; bestFamily = family; }
            }
            if (best < 0) throw new InvalidOperationException("No Vulkan device with a compute queue.");
            Physical = devs[best];
            QueueFamily = bestFamily;

            byte* p = stackalloc byte[1024];
            Vk.vkGetPhysicalDeviceProperties(Physical, p);
            ApiVersion = *(uint*)(p + 0);
            VendorId = *(uint*)(p + 8);
            DeviceType = *(uint*)(p + 16);
            int len = 0;
            while (len < 256 && p[20 + len] != 0) len++;
            DeviceName = Encoding.UTF8.GetString(p + 20, len);
            TimestampPeriodNs = *(float*)(p + 720); // VkPhysicalDeviceLimits.timestampPeriod

            // Subgroup properties (Vulkan 1.1): VkPhysicalDeviceProperties2 { sType, pNext, properties[824] } -> chained
            // VkPhysicalDeviceSubgroupProperties { sType, pNext, subgroupSize, supportedStages, supportedOperations, quad }.
            if (ApiVersion >= VkConst.MakeApiVersion(1, 1, 0))
            {
                byte* sub = stackalloc byte[32];
                byte* p2 = stackalloc byte[16 + 824 + 64];
                for (int i = 0; i < 32; i++) sub[i] = 0;
                for (int i = 0; i < 16 + 824 + 64; i++) p2[i] = 0;
                *(int*)sub = 1000094000;            // VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_SUBGROUP_PROPERTIES
                *(int*)p2 = 1000059001;             // VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2
                *(IntPtr*)(p2 + 8) = (IntPtr)sub;
                Vk.vkGetPhysicalDeviceProperties2(Physical, p2);
                SubgroupSize = *(uint*)(sub + 16);
                uint stages = *(uint*)(sub + 20), ops = *(uint*)(sub + 24);
                SubgroupArithmetic = (stages & VkConst.ShaderStageCompute) != 0 && (ops & 0x4) != 0 && SubgroupSize >= 8;
            }

            byte* mp = stackalloc byte[1024];
            Vk.vkGetPhysicalDeviceMemoryProperties(Physical, mp);
            _memTypeCount = *(uint*)mp;
            for (int i = 0; i < _memTypeCount; i++) _memFlags[i] = *(uint*)(mp + 4 + i * 8);
            uint heapCount = *(uint*)(mp + 260);
            for (int i = 0; i < heapCount; i++)
            {
                ulong size = *(ulong*)(mp + 264 + i * 16);
                uint flags = *(uint*)(mp + 264 + i * 16 + 8);
                if ((flags & 1) != 0 && size > DeviceLocalBytes) DeviceLocalBytes = size; // VK_MEMORY_HEAP_DEVICE_LOCAL_BIT
            }

            // Integer dot product (Vulkan 1.3 core): VkPhysicalDeviceShaderIntegerDotProductFeatures { sType, pNext, VkBool32 }.
            byte* dotFeat = stackalloc byte[24];
            for (int i = 0; i < 24; i++) dotFeat[i] = 0;
            *(int*)dotFeat = 1000280000;
            if (ApiVersion >= VkConst.MakeApiVersion(1, 3, 0))
            {
                byte* f2 = stackalloc byte[16 + 224 + 16];          // VkPhysicalDeviceFeatures2 { sType, pNext, VkPhysicalDeviceFeatures (55 x VkBool32) }
                for (int i = 0; i < 16 + 224 + 16; i++) f2[i] = 0;
                *(int*)f2 = 1000059000;
                *(IntPtr*)(f2 + 8) = (IntPtr)dotFeat;
                Vk.vkGetPhysicalDeviceFeatures2(Physical, f2);
                IntegerDot = *(uint*)(dotFeat + 16) != 0;
                if (IntegerDot)
                {
                    byte* dp = stackalloc byte[16 + 30 * 4 + 8];       // VkPhysicalDeviceShaderIntegerDotProductProperties
                    byte* p2 = stackalloc byte[16 + 824 + 64];
                    for (int i = 0; i < 16 + 30 * 4 + 8; i++) dp[i] = 0;
                    for (int i = 0; i < 16 + 824 + 64; i++) p2[i] = 0;
                    *(int*)dp = 1000280001;
                    *(int*)p2 = 1000059001;
                    *(IntPtr*)(p2 + 8) = (IntPtr)dp;
                    Vk.vkGetPhysicalDeviceProperties2(Physical, p2);
                    // integerDotProduct4x8BitPackedSignedAccelerated
                    IntegerDotAccelerated = *(uint*)(dp + 16 + 4 * 4) != 0; // 4x8BitPacked signed (all our operands fit signed bytes)
                }
            }
            *(uint*)(dotFeat + 16) = IntegerDot ? 1u : 0u;

            // Logical device + queue
            float priority = 1.0f;
            VkDeviceQueueCreateInfo qci = new VkDeviceQueueCreateInfo();
            qci.sType = VkSType.DeviceQueueCreateInfo;
            qci.queueFamilyIndex = QueueFamily;
            qci.queueCount = 1;
            qci.pQueuePriorities = (IntPtr)(&priority);
            VkDeviceCreateInfo dci = new VkDeviceCreateInfo();
            dci.sType = VkSType.DeviceCreateInfo;
            dci.queueCreateInfoCount = 1;
            dci.pQueueCreateInfos = (IntPtr)(&qci);
            if (IntegerDot) dci.pNext = (IntPtr)dotFeat;
            IntPtr dev;
            Vk.Check(Vk.vkCreateDevice(Physical, &dci, IntPtr.Zero, &dev), "vkCreateDevice");
            Device = dev;
            IntPtr q;
            Vk.vkGetDeviceQueue(Device, QueueFamily, 0, &q);
            Queue = q;

            // One reusable command buffer + fence.
            VkCommandPoolCreateInfo cpi = new VkCommandPoolCreateInfo();
            cpi.sType = VkSType.CommandPoolCreateInfo;
            cpi.flags = VkConst.CommandPoolResetCommandBuffer;
            cpi.queueFamilyIndex = QueueFamily;
            ulong pool;
            Vk.Check(Vk.vkCreateCommandPool(Device, &cpi, IntPtr.Zero, &pool), "vkCreateCommandPool");
            _cmdPool = pool;
            VkCommandBufferAllocateInfo cai = new VkCommandBufferAllocateInfo();
            cai.sType = VkSType.CommandBufferAllocateInfo;
            cai.commandPool = _cmdPool;
            cai.level = 0; // primary
            cai.commandBufferCount = 1;
            IntPtr cmd;
            Vk.Check(Vk.vkAllocateCommandBuffers(Device, &cai, &cmd), "vkAllocateCommandBuffers");
            Cmd = cmd;
            VkFenceCreateInfo fci = new VkFenceCreateInfo();
            fci.sType = VkSType.FenceCreateInfo;
            ulong fence;
            Vk.Check(Vk.vkCreateFence(Device, &fci, IntPtr.Zero, &fence), "vkCreateFence");
            _fence = fence;
        }

        static bool FindComputeQueue(IntPtr phys, out uint family)
        {
            uint n = 0;
            Vk.vkGetPhysicalDeviceQueueFamilyProperties(phys, &n, null);
            VkQueueFamilyProperties[] props = new VkQueueFamilyProperties[n];
            fixed (VkQueueFamilyProperties* pp = props) Vk.vkGetPhysicalDeviceQueueFamilyProperties(phys, &n, pp);
            for (uint i = 0; i < n; i++)
                if ((props[i].queueFlags & VkConst.QueueCompute) != 0) { family = i; return true; }
            family = 0;
            return false;
        }

        int FindMemoryType(uint typeBits, uint required, uint preferred)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _memTypeCount; i++)
                {
                    if ((typeBits & (1u << i)) == 0) continue;
                    uint want = pass == 0 ? required | preferred : required;
                    if ((_memFlags[i] & want) == want) return i;
                }
            return -1;
        }

        // hostVisible: mapped, coherent memory the CPU can read and write directly.
        public GpuBuffer CreateBuffer(ulong size, bool hostVisible)
        {
            return CreateBuffer(size, hostVisible ? BufferKind.Readback : BufferKind.Device);
        }

        public GpuBuffer CreateBuffer(ulong size, BufferKind kind)
        {
            bool hostVisible = kind != BufferKind.Device;
            VkBufferCreateInfo bci = new VkBufferCreateInfo();
            bci.sType = VkSType.BufferCreateInfo;
            bci.size = size;
            bci.usage = VkConst.BufferUsageStorage | VkConst.BufferUsageTransferSrc | VkConst.BufferUsageTransferDst | VkConst.BufferUsageIndirect;
            ulong buf;
            Vk.Check(Vk.vkCreateBuffer(Device, &bci, IntPtr.Zero, &buf), "vkCreateBuffer");

            GpuBuffer b = new GpuBuffer();
            b.Device = Device;
            b.Buffer = buf;
            b.Size = size;
            try
            {
                VkMemoryRequirements req;
                Vk.vkGetBufferMemoryRequirements(Device, buf, &req);
                uint required = hostVisible ? VkConst.MemoryHostVisible | VkConst.MemoryHostCoherent : VkConst.MemoryDeviceLocal;
                uint preferred = kind == BufferKind.Readback ? VkConst.MemoryHostCached
                               : kind == BufferKind.Upload ? VkConst.MemoryDeviceLocal : 0;
                int type = FindMemoryType(req.memoryTypeBits, required, preferred);
                if (type < 0) throw new InvalidOperationException("No suitable memory type.");

                VkMemoryAllocateInfo mai = new VkMemoryAllocateInfo();
                mai.sType = VkSType.MemoryAllocateInfo;
                mai.allocationSize = req.size;
                mai.memoryTypeIndex = (uint)type;
                ulong mem;
                int res = Vk.vkAllocateMemory(Device, &mai, IntPtr.Zero, &mem);
                if (res == VkErrorOutOfDeviceMemory && !hostVisible)
                {
                    // VRAM budget exhausted (e.g. another app holds it): fall back to system memory the GPU can read.
                    int fallback = FindMemoryType(req.memoryTypeBits, VkConst.MemoryHostVisible, 0);
                    if (fallback >= 0)
                    {
                        type = fallback;
                        mai.memoryTypeIndex = (uint)type;
                        res = Vk.vkAllocateMemory(Device, &mai, IntPtr.Zero, &mem);
                        if (res == 0) FallbackBytes += req.size;
                    }
                }
                Vk.Check(res, "vkAllocateMemory(" + req.size + " bytes, type " + type + ")");
                b.Memory = mem;
                b.DeviceLocal = (_memFlags[type] & VkConst.MemoryDeviceLocal) != 0;
                Vk.Check(Vk.vkBindBufferMemory(Device, buf, mem, 0), "vkBindBufferMemory");
                if (hostVisible)
                {
                    IntPtr mapped;
                    Vk.Check(Vk.vkMapMemory(Device, mem, 0, VkConst.WholeSize, 0, &mapped), "vkMapMemory");
                    b.Mapped = mapped;
                }
            }
            catch { b.Dispose(); throw; }
            return b;
        }

        // A compute pipeline with `bindings` storage buffers at set 0 and `pushBytes` of push constants.
        public GpuKernel CreateKernel(uint[] spirv, int bindings, uint pushBytes, uint maxSetsInFlight)
        {
            GpuKernel k = new GpuKernel();
            k.Device = Device;
            k.BindingCount = bindings;
            k.PushBytes = pushBytes;
            try
            {
                fixed (uint* code = spirv)
                {
                    VkShaderModuleCreateInfo smi = new VkShaderModuleCreateInfo();
                    smi.sType = VkSType.ShaderModuleCreateInfo;
                    smi.codeSize = (UIntPtr)(spirv.Length * 4);
                    smi.pCode = (IntPtr)code;
                    ulong module;
                    Vk.Check(Vk.vkCreateShaderModule(Device, &smi, IntPtr.Zero, &module), "vkCreateShaderModule");
                    k.Module = module;
                }

                VkDescriptorSetLayoutBinding* lb = stackalloc VkDescriptorSetLayoutBinding[Math.Max(1, bindings)];
                for (int i = 0; i < bindings; i++)
                {
                    lb[i].binding = (uint)i;
                    lb[i].descriptorType = VkConst.DescriptorTypeStorageBuffer;
                    lb[i].descriptorCount = 1;
                    lb[i].stageFlags = VkConst.ShaderStageCompute;
                    lb[i].pImmutableSamplers = IntPtr.Zero;
                }
                VkDescriptorSetLayoutCreateInfo dli = new VkDescriptorSetLayoutCreateInfo();
                dli.sType = VkSType.DescriptorSetLayoutCreateInfo;
                dli.bindingCount = (uint)bindings;
                dli.pBindings = (IntPtr)lb;
                ulong setLayout;
                Vk.Check(Vk.vkCreateDescriptorSetLayout(Device, &dli, IntPtr.Zero, &setLayout), "vkCreateDescriptorSetLayout");
                k.SetLayout = setLayout;

                VkPushConstantRange range = new VkPushConstantRange();
                range.stageFlags = VkConst.ShaderStageCompute;
                range.size = pushBytes;
                VkPipelineLayoutCreateInfo pli = new VkPipelineLayoutCreateInfo();
                pli.sType = VkSType.PipelineLayoutCreateInfo;
                pli.setLayoutCount = 1;
                pli.pSetLayouts = (IntPtr)(&setLayout);
                pli.pushConstantRangeCount = pushBytes > 0 ? 1u : 0u;
                pli.pPushConstantRanges = (IntPtr)(&range);
                ulong pipeLayout;
                Vk.Check(Vk.vkCreatePipelineLayout(Device, &pli, IntPtr.Zero, &pipeLayout), "vkCreatePipelineLayout");
                k.PipelineLayout = pipeLayout;

                VkComputePipelineCreateInfo cpi = new VkComputePipelineCreateInfo();
                cpi.sType = VkSType.ComputePipelineCreateInfo;
                cpi.stage.sType = VkSType.PipelineShaderStageCreateInfo;
                cpi.stage.stage = VkConst.ShaderStageCompute;
                cpi.stage.module = k.Module;
                cpi.stage.pName = Str("main");
                cpi.layout = k.PipelineLayout;
                cpi.basePipelineIndex = -1;
                ulong pipeline;
                Vk.Check(Vk.vkCreateComputePipelines(Device, 0, 1, &cpi, IntPtr.Zero, &pipeline), "vkCreateComputePipelines");
                k.Pipeline = pipeline;

                VkDescriptorPoolSize ps = new VkDescriptorPoolSize();
                ps.type = VkConst.DescriptorTypeStorageBuffer;
                ps.descriptorCount = (uint)Math.Max(1, bindings) * maxSetsInFlight;
                VkDescriptorPoolCreateInfo dpi = new VkDescriptorPoolCreateInfo();
                dpi.sType = VkSType.DescriptorPoolCreateInfo;
                dpi.flags = VkConst.DescriptorPoolFreeDescriptorSet;
                dpi.maxSets = maxSetsInFlight;
                dpi.poolSizeCount = 1;
                dpi.pPoolSizes = (IntPtr)(&ps);
                ulong dpool;
                Vk.Check(Vk.vkCreateDescriptorPool(Device, &dpi, IntPtr.Zero, &dpool), "vkCreateDescriptorPool");
                k.DescriptorPool = dpool;
            }
            catch { k.Dispose(); throw; }
            return k;
        }

        // Records, submits and waits for a single dispatch.
        public void Dispatch(GpuKernel k, GpuBuffer[] buffers, byte[] push, uint groupsX, uint groupsY, uint groupsZ)
        {
            if (buffers.Length != k.BindingCount) throw new ArgumentException("Kernel expects " + k.BindingCount + " buffers.");
            if ((push == null ? 0 : push.Length) != k.PushBytes) throw new ArgumentException("Kernel expects " + k.PushBytes + " push-constant bytes.");

            ulong set;
            ulong layout = k.SetLayout;
            VkDescriptorSetAllocateInfo dai = new VkDescriptorSetAllocateInfo();
            dai.sType = VkSType.DescriptorSetAllocateInfo;
            dai.descriptorPool = k.DescriptorPool;
            dai.descriptorSetCount = 1;
            dai.pSetLayouts = (IntPtr)(&layout);
            Vk.Check(Vk.vkAllocateDescriptorSets(Device, &dai, &set), "vkAllocateDescriptorSets");
            try
            {
                int n = buffers.Length;
                VkDescriptorBufferInfo* infos = stackalloc VkDescriptorBufferInfo[Math.Max(1, n)];
                VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[Math.Max(1, n)];
                for (int i = 0; i < n; i++)
                {
                    infos[i].buffer = buffers[i].Buffer;
                    infos[i].offset = 0;
                    infos[i].range = VkConst.WholeSize;
                    writes[i] = new VkWriteDescriptorSet();
                    writes[i].sType = VkSType.WriteDescriptorSet;
                    writes[i].dstSet = set;
                    writes[i].dstBinding = (uint)i;
                    writes[i].descriptorCount = 1;
                    writes[i].descriptorType = VkConst.DescriptorTypeStorageBuffer;
                    writes[i].pBufferInfo = (IntPtr)(&infos[i]);
                }
                Vk.vkUpdateDescriptorSets(Device, (uint)n, writes, 0, IntPtr.Zero);

                Vk.Check(Vk.vkResetCommandBuffer(Cmd, 0), "vkResetCommandBuffer");
                VkCommandBufferBeginInfo bi = new VkCommandBufferBeginInfo();
                bi.sType = VkSType.CommandBufferBeginInfo;
                bi.flags = VkConst.CommandBufferUsageOneTimeSubmit;
                Vk.Check(Vk.vkBeginCommandBuffer(Cmd, &bi), "vkBeginCommandBuffer");
                Vk.vkCmdBindPipeline(Cmd, VkConst.PipelineBindPointCompute, k.Pipeline);
                Vk.vkCmdBindDescriptorSets(Cmd, VkConst.PipelineBindPointCompute, k.PipelineLayout, 0, 1, &set, 0, null);
                if (k.PushBytes > 0)
                    fixed (byte* pp = push) Vk.vkCmdPushConstants(Cmd, k.PipelineLayout, VkConst.ShaderStageCompute, 0, k.PushBytes, pp);
                Vk.vkCmdDispatch(Cmd, groupsX, groupsY, groupsZ);

                // Make shader writes visible to the host.
                VkMemoryBarrier mb = new VkMemoryBarrier();
                mb.sType = VkSType.MemoryBarrier;
                mb.srcAccessMask = VkConst.AccessShaderWrite;
                mb.dstAccessMask = VkConst.AccessHostRead;
                Vk.vkCmdPipelineBarrier(Cmd, VkConst.PipelineStageComputeShader, VkConst.PipelineStageHost, 0, 1, &mb, 0, IntPtr.Zero, 0, IntPtr.Zero);
                Vk.Check(Vk.vkEndCommandBuffer(Cmd), "vkEndCommandBuffer");

                Submit();
            }
            finally
            {
                Vk.vkFreeDescriptorSets(Device, k.DescriptorPool, 1, &set);
            }
        }

        // ---------- reusable command buffers (record once, submit many times) ----------

        IntPtr _defaultCmd;

        public IntPtr AllocateCommandBuffer()
        {
            VkCommandBufferAllocateInfo cai = new VkCommandBufferAllocateInfo();
            cai.sType = VkSType.CommandBufferAllocateInfo;
            cai.commandPool = _cmdPool;
            cai.level = 0;
            cai.commandBufferCount = 1;
            IntPtr cmd;
            Vk.Check(Vk.vkAllocateCommandBuffers(Device, &cai, &cmd), "vkAllocateCommandBuffers");
            return cmd;
        }

        // Starts recording into cmd; Record/RecordIndirect go there until EndReusable().
        public void BeginReusable(IntPtr cmd)
        {
            if (_recording) throw new InvalidOperationException("Already recording.");
            if (_defaultCmd == IntPtr.Zero) _defaultCmd = Cmd;
            Cmd = cmd;
            Vk.Check(Vk.vkResetCommandBuffer(Cmd, 0), "vkResetCommandBuffer");
            VkCommandBufferBeginInfo bi = new VkCommandBufferBeginInfo();
            bi.sType = VkSType.CommandBufferBeginInfo;
            Vk.Check(Vk.vkBeginCommandBuffer(Cmd, &bi), "vkBeginCommandBuffer");
            _recording = true;
            _queryLabels.Clear();
        }

        public void EndReusable()
        {
            if (!_recording) throw new InvalidOperationException("Not recording.");
            Barrier(VkConst.PipelineStageComputeShader | VkConst.PipelineStageTransfer, VkConst.AccessShaderWrite | VkConst.AccessTransferWrite,
                VkConst.PipelineStageHost, VkConst.AccessHostRead);
            Vk.Check(Vk.vkEndCommandBuffer(Cmd), "vkEndCommandBuffer");
            _recording = false;
            Cmd = _defaultCmd;
        }

        // Submits a recorded command buffer and waits for it.
        public void SubmitReusable(IntPtr cmd)
        {
            if (_recording) throw new InvalidOperationException("Still recording.");
            IntPtr saved = Cmd;
            Cmd = cmd;
            try { Submit(); } finally { Cmd = saved; }
        }

        // Dispatch whose group counts come from args (three u32 at offset), followed by a compute barrier.
        public void RecordIndirect(GpuKernel k, GpuBuffer[] buffers, uint[] push, GpuBuffer args, ulong offset)
        {
            if (!_recording) throw new InvalidOperationException("Call Begin() first.");
            if (buffers.Length != k.BindingCount) throw new ArgumentException("Kernel expects " + k.BindingCount + " buffers.");
            if ((push == null ? 0 : push.Length * 4) != k.PushBytes) throw new ArgumentException("Kernel expects " + k.PushBytes + " push-constant bytes.");
            ulong set = SetFor(k, buffers);
            Vk.vkCmdBindPipeline(Cmd, VkConst.PipelineBindPointCompute, k.Pipeline);
            Vk.vkCmdBindDescriptorSets(Cmd, VkConst.PipelineBindPointCompute, k.PipelineLayout, 0, 1, &set, 0, null);
            if (k.PushBytes > 0)
                fixed (uint* pp = push) Vk.vkCmdPushConstants(Cmd, k.PipelineLayout, VkConst.ShaderStageCompute, 0, k.PushBytes, pp);
            Vk.vkCmdDispatchIndirect(Cmd, args.Buffer, offset);
            Barrier(VkConst.PipelineStageComputeShader, VkConst.AccessShaderWrite,
                VkConst.PipelineStageComputeShader, VkConst.AccessShaderRead | VkConst.AccessShaderWrite);
            if (Profiling && _queryPool != 0 && _queryLabels.Count + 1 < MaxQueries)
            {
                _queryLabels.Add(k.Name);
                Vk.vkCmdWriteTimestamp(Cmd, VkConst.PipelineStageComputeShader, _queryPool, (uint)_queryLabels.Count);
            }
        }

        // ---------- batched recording ----------

        public void Begin()
        {
            if (_recording) throw new InvalidOperationException("Already recording.");
            Vk.Check(Vk.vkResetCommandBuffer(Cmd, 0), "vkResetCommandBuffer");
            VkCommandBufferBeginInfo bi = new VkCommandBufferBeginInfo();
            bi.sType = VkSType.CommandBufferBeginInfo;
            bi.flags = VkConst.CommandBufferUsageOneTimeSubmit;
            Vk.Check(Vk.vkBeginCommandBuffer(Cmd, &bi), "vkBeginCommandBuffer");
            _recording = true;
            _queryLabels.Clear();
            if (Profiling)
            {
                if (_queryPool == 0)
                {
                    VkQueryPoolCreateInfo qi = new VkQueryPoolCreateInfo();
                    qi.sType = VkSType.QueryPoolCreateInfo;
                    qi.queryType = 2; // VK_QUERY_TYPE_TIMESTAMP
                    qi.queryCount = MaxQueries;
                    ulong qp;
                    Vk.Check(Vk.vkCreateQueryPool(Device, &qi, IntPtr.Zero, &qp), "vkCreateQueryPool");
                    _queryPool = qp;
                }
                Vk.vkCmdResetQueryPool(Cmd, _queryPool, 0, MaxQueries);
                Vk.vkCmdWriteTimestamp(Cmd, VkConst.PipelineStageComputeShader, _queryPool, 0);
            }
        }

        ulong SetFor(GpuKernel k, GpuBuffer[] buffers)
        {
            StringBuilder key = new StringBuilder();
            for (int i = 0; i < buffers.Length; i++) key.Append(buffers[i].Buffer).Append(',');
            string ks = key.ToString();
            ulong set;
            if (k.Sets.TryGetValue(ks, out set)) return set;

            ulong layout = k.SetLayout;
            VkDescriptorSetAllocateInfo dai = new VkDescriptorSetAllocateInfo();
            dai.sType = VkSType.DescriptorSetAllocateInfo;
            dai.descriptorPool = k.DescriptorPool;
            dai.descriptorSetCount = 1;
            dai.pSetLayouts = (IntPtr)(&layout);
            Vk.Check(Vk.vkAllocateDescriptorSets(Device, &dai, &set), "vkAllocateDescriptorSets");

            int n = buffers.Length;
            VkDescriptorBufferInfo* infos = stackalloc VkDescriptorBufferInfo[Math.Max(1, n)];
            VkWriteDescriptorSet* writes = stackalloc VkWriteDescriptorSet[Math.Max(1, n)];
            for (int i = 0; i < n; i++)
            {
                infos[i].buffer = buffers[i].Buffer;
                infos[i].offset = 0;
                infos[i].range = VkConst.WholeSize;
                writes[i] = new VkWriteDescriptorSet();
                writes[i].sType = VkSType.WriteDescriptorSet;
                writes[i].dstSet = set;
                writes[i].dstBinding = (uint)i;
                writes[i].descriptorCount = 1;
                writes[i].descriptorType = VkConst.DescriptorTypeStorageBuffer;
                writes[i].pBufferInfo = (IntPtr)(&infos[i]);
            }
            Vk.vkUpdateDescriptorSets(Device, (uint)n, writes, 0, IntPtr.Zero);
            k.Sets.Add(ks, set);
            return set;
        }

        // Records one dispatch followed by a compute->compute barrier.
        public void Record(GpuKernel k, GpuBuffer[] buffers, uint[] push, uint groupsX, uint groupsY, uint groupsZ)
        {
            Record(k, buffers, push, groupsX, groupsY, groupsZ, true);
        }

        // barrierAfter = false lets the next dispatch overlap this one (use only when it does not read this one's output).
        public void Record(GpuKernel k, GpuBuffer[] buffers, uint[] push, uint groupsX, uint groupsY, uint groupsZ, bool barrierAfter)
        {
            if (!_recording) throw new InvalidOperationException("Call Begin() first.");
            if (buffers.Length != k.BindingCount) throw new ArgumentException("Kernel expects " + k.BindingCount + " buffers.");
            if ((push == null ? 0 : push.Length * 4) != k.PushBytes) throw new ArgumentException("Kernel expects " + k.PushBytes + " push-constant bytes.");
            ulong set = SetFor(k, buffers);
            Vk.vkCmdBindPipeline(Cmd, VkConst.PipelineBindPointCompute, k.Pipeline);
            Vk.vkCmdBindDescriptorSets(Cmd, VkConst.PipelineBindPointCompute, k.PipelineLayout, 0, 1, &set, 0, null);
            if (k.PushBytes > 0)
                fixed (uint* pp = push) Vk.vkCmdPushConstants(Cmd, k.PipelineLayout, VkConst.ShaderStageCompute, 0, k.PushBytes, pp);
            Vk.vkCmdDispatch(Cmd, groupsX, groupsY, groupsZ);
            if (barrierAfter)
                Barrier(VkConst.PipelineStageComputeShader, VkConst.AccessShaderWrite,
                    VkConst.PipelineStageComputeShader, VkConst.AccessShaderRead | VkConst.AccessShaderWrite);
            if (Profiling && _queryLabels.Count + 1 < MaxQueries)
            {
                _queryLabels.Add(k.Name);
                Vk.vkCmdWriteTimestamp(Cmd, VkConst.PipelineStageComputeShader, _queryPool, (uint)_queryLabels.Count);
            }
        }

        void Barrier(uint srcStage, uint srcAccess, uint dstStage, uint dstAccess)
        {
            VkMemoryBarrier mb = new VkMemoryBarrier();
            mb.sType = VkSType.MemoryBarrier;
            mb.srcAccessMask = srcAccess;
            mb.dstAccessMask = dstAccess;
            Vk.vkCmdPipelineBarrier(Cmd, srcStage, dstStage, 0, 1, &mb, 0, IntPtr.Zero, 0, IntPtr.Zero);
        }

        // Ends recording, submits, and waits. Host writes made before Begin() are visible to the GPU
        // (queue submission orders host-coherent writes); GPU writes are made visible to the host.
        public void SubmitAndWait()
        {
            if (!_recording) throw new InvalidOperationException("Call Begin() first.");
            Barrier(VkConst.PipelineStageComputeShader | VkConst.PipelineStageTransfer, VkConst.AccessShaderWrite | VkConst.AccessTransferWrite,
                VkConst.PipelineStageHost, VkConst.AccessHostRead);
            Vk.Check(Vk.vkEndCommandBuffer(Cmd), "vkEndCommandBuffer");
            _recording = false;
            Submit();
            if (Profiling && _queryLabels.Count > 0) CollectProfile();
        }

        // Copies host memory into any buffer (device-local ones go through a reusable staging buffer).
        public void Upload(GpuBuffer dst, ulong dstOffset, IntPtr src, ulong bytes)
        {
            if (dstOffset + bytes > dst.Size) throw new ArgumentOutOfRangeException("bytes");
            if (dst.Mapped != IntPtr.Zero)
            {
                Buffer.MemoryCopy((void*)src, (byte*)dst.Mapped + dstOffset, (long)bytes, (long)bytes);
                return;
            }
            if (_staging == null) _staging = CreateBuffer(StagingBytes, true);
            ulong done = 0;
            while (done < bytes)
            {
                ulong chunk = Math.Min(StagingBytes, bytes - done);
                Buffer.MemoryCopy((byte*)src + done, (void*)_staging.Mapped, (long)chunk, (long)chunk);
                Begin();
                VkBufferCopy region = new VkBufferCopy();
                region.srcOffset = 0;
                region.dstOffset = dstOffset + done;
                region.size = chunk;
                Vk.vkCmdCopyBuffer(Cmd, _staging.Buffer, dst.Buffer, 1, &region);
                SubmitAndWait();
                done += chunk;
            }
        }

        public void Upload(GpuBuffer dst, float[] src)
        {
            fixed (float* p = src) Upload(dst, 0, (IntPtr)p, (ulong)src.Length * 4);
        }

        public void Upload(GpuBuffer dst, uint[] src)
        {
            fixed (uint* p = src) Upload(dst, 0, (IntPtr)p, (ulong)src.Length * 4);
        }

        void CollectProfile()
        {
            int n = _queryLabels.Count + 1;
            ulong[] ts = new ulong[n];
            fixed (ulong* pt = ts)
                Vk.Check(Vk.vkGetQueryPoolResults(Device, _queryPool, 0, (uint)n, (UIntPtr)(n * 8), pt, 8, 0x1 | 0x2), "vkGetQueryPoolResults");
            for (int i = 1; i < n; i++)
            {
                double ms = (ts[i] - ts[i - 1]) * TimestampPeriodNs / 1e6;
                string label = _queryLabels[i - 1];
                double sum; ProfileMs.TryGetValue(label, out sum); ProfileMs[label] = sum + ms;
                int c; ProfileCount.TryGetValue(label, out c); ProfileCount[label] = c + 1;
            }
        }

        void Submit()
        {
            IntPtr cmd = Cmd;
            VkSubmitInfo si = new VkSubmitInfo();
            si.sType = VkSType.SubmitInfo;
            si.commandBufferCount = 1;
            si.pCommandBuffers = (IntPtr)(&cmd);
            ulong fence = _fence;
            Vk.Check(Vk.vkQueueSubmit(Queue, 1, &si, fence), "vkQueueSubmit");
            Vk.Check(Vk.vkWaitForFences(Device, 1, &fence, 1, ulong.MaxValue), "vkWaitForFences");
            Vk.Check(Vk.vkResetFences(Device, 1, &fence), "vkResetFences");
        }

        public void Dispose()
        {
            if (_staging != null) { _staging.Dispose(); _staging = null; }
            if (Device != IntPtr.Zero)
            {
                Vk.vkDeviceWaitIdle(Device);
                if (_fence != 0) Vk.vkDestroyFence(Device, _fence, IntPtr.Zero);
                if (_queryPool != 0) Vk.vkDestroyQueryPool(Device, _queryPool, IntPtr.Zero);
                if (_cmdPool != 0) Vk.vkDestroyCommandPool(Device, _cmdPool, IntPtr.Zero);
                Vk.vkDestroyDevice(Device, IntPtr.Zero);
                Device = IntPtr.Zero;
            }
            if (Instance != IntPtr.Zero) { Vk.vkDestroyInstance(Instance, IntPtr.Zero); Instance = IntPtr.Zero; }
            foreach (IntPtr p in _strings) Marshal.FreeHGlobal(p);
            _strings.Clear();
        }
    }
}
