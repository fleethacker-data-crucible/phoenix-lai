// Minimal Vulkan 1.x compute bindings over the driver's vulkan-1.dll. C# 5 only.
// Layouts and values from dev/ref/vulkan/vulkan_core.h. x64 only: dispatchable handles are IntPtr,
// non-dispatchable handles are ulong.
using System;
using System.Runtime.InteropServices;

namespace Phoenix
{
    public static class VkSType
    {
        public const int ApplicationInfo = 0, InstanceCreateInfo = 1, DeviceQueueCreateInfo = 2, DeviceCreateInfo = 3,
            SubmitInfo = 4, MemoryAllocateInfo = 5, FenceCreateInfo = 8, BufferCreateInfo = 12,
            ShaderModuleCreateInfo = 16, PipelineShaderStageCreateInfo = 18, ComputePipelineCreateInfo = 29,
            PipelineLayoutCreateInfo = 30, DescriptorSetLayoutCreateInfo = 32, DescriptorPoolCreateInfo = 33,
            DescriptorSetAllocateInfo = 34, WriteDescriptorSet = 35, CommandPoolCreateInfo = 39,
            CommandBufferAllocateInfo = 40, CommandBufferBeginInfo = 42, MemoryBarrier = 46;
    }

    public static class VkConst
    {
        public const uint QueueCompute = 0x2;
        public const uint BufferUsageTransferSrc = 0x1, BufferUsageTransferDst = 0x2, BufferUsageStorage = 0x20;
        public const uint MemoryDeviceLocal = 0x1, MemoryHostVisible = 0x2, MemoryHostCoherent = 0x4, MemoryHostCached = 0x8;
        public const int DescriptorTypeStorageBuffer = 7;
        public const uint ShaderStageCompute = 0x20;
        public const int PipelineBindPointCompute = 1;
        public const uint CommandPoolResetCommandBuffer = 0x2;
        public const uint CommandBufferUsageOneTimeSubmit = 0x1;
        public const uint DescriptorPoolFreeDescriptorSet = 0x1;
        public const uint PipelineStageComputeShader = 0x800, PipelineStageTransfer = 0x1000, PipelineStageHost = 0x4000;
        public const uint AccessShaderRead = 0x20, AccessShaderWrite = 0x40, AccessTransferRead = 0x800,
            AccessTransferWrite = 0x1000, AccessHostRead = 0x2000, AccessHostWrite = 0x4000;
        public const ulong WholeSize = ulong.MaxValue;

        public static uint MakeApiVersion(uint major, uint minor, uint patch) { return (major << 22) | (minor << 12) | patch; }
        public static string ApiVersionString(uint v) { return (v >> 22) + "." + ((v >> 12) & 0x3FF) + "." + (v & 0xFFF); }
    }

    [StructLayout(LayoutKind.Sequential)] public struct VkApplicationInfo
    { public int sType; public IntPtr pNext; public IntPtr pApplicationName; public uint applicationVersion; public IntPtr pEngineName; public uint engineVersion; public uint apiVersion; }

    [StructLayout(LayoutKind.Sequential)] public struct VkInstanceCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public IntPtr pApplicationInfo; public uint enabledLayerCount; public IntPtr ppEnabledLayerNames; public uint enabledExtensionCount; public IntPtr ppEnabledExtensionNames; }

    [StructLayout(LayoutKind.Sequential)] public struct VkQueueFamilyProperties
    { public uint queueFlags; public uint queueCount; public uint timestampValidBits; public uint granularityW, granularityH, granularityD; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDeviceQueueCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public uint queueFamilyIndex; public uint queueCount; public IntPtr pQueuePriorities; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDeviceCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public uint queueCreateInfoCount; public IntPtr pQueueCreateInfos; public uint enabledLayerCount; public IntPtr ppEnabledLayerNames; public uint enabledExtensionCount; public IntPtr ppEnabledExtensionNames; public IntPtr pEnabledFeatures; }

    [StructLayout(LayoutKind.Sequential)] public struct VkBufferCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public ulong size; public uint usage; public int sharingMode; public uint queueFamilyIndexCount; public IntPtr pQueueFamilyIndices; }

    [StructLayout(LayoutKind.Sequential)] public struct VkMemoryRequirements
    { public ulong size; public ulong alignment; public uint memoryTypeBits; }

    [StructLayout(LayoutKind.Sequential)] public struct VkMemoryAllocateInfo
    { public int sType; public IntPtr pNext; public ulong allocationSize; public uint memoryTypeIndex; }

    [StructLayout(LayoutKind.Sequential)] public struct VkShaderModuleCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public UIntPtr codeSize; public IntPtr pCode; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDescriptorSetLayoutBinding
    { public uint binding; public int descriptorType; public uint descriptorCount; public uint stageFlags; public IntPtr pImmutableSamplers; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDescriptorSetLayoutCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public uint bindingCount; public IntPtr pBindings; }

    [StructLayout(LayoutKind.Sequential)] public struct VkPushConstantRange
    { public uint stageFlags; public uint offset; public uint size; }

    [StructLayout(LayoutKind.Sequential)] public struct VkPipelineLayoutCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public uint setLayoutCount; public IntPtr pSetLayouts; public uint pushConstantRangeCount; public IntPtr pPushConstantRanges; }

    [StructLayout(LayoutKind.Sequential)] public struct VkPipelineShaderStageCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public uint stage; public ulong module; public IntPtr pName; public IntPtr pSpecializationInfo; }

    [StructLayout(LayoutKind.Sequential)] public struct VkComputePipelineCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public VkPipelineShaderStageCreateInfo stage; public ulong layout; public ulong basePipelineHandle; public int basePipelineIndex; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDescriptorPoolSize
    { public int type; public uint descriptorCount; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDescriptorPoolCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public uint maxSets; public uint poolSizeCount; public IntPtr pPoolSizes; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDescriptorSetAllocateInfo
    { public int sType; public IntPtr pNext; public ulong descriptorPool; public uint descriptorSetCount; public IntPtr pSetLayouts; }

    [StructLayout(LayoutKind.Sequential)] public struct VkDescriptorBufferInfo
    { public ulong buffer; public ulong offset; public ulong range; }

    [StructLayout(LayoutKind.Sequential)] public struct VkWriteDescriptorSet
    { public int sType; public IntPtr pNext; public ulong dstSet; public uint dstBinding; public uint dstArrayElement; public uint descriptorCount; public int descriptorType; public IntPtr pImageInfo; public IntPtr pBufferInfo; public IntPtr pTexelBufferView; }

    [StructLayout(LayoutKind.Sequential)] public struct VkCommandPoolCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; public uint queueFamilyIndex; }

    [StructLayout(LayoutKind.Sequential)] public struct VkCommandBufferAllocateInfo
    { public int sType; public IntPtr pNext; public ulong commandPool; public int level; public uint commandBufferCount; }

    [StructLayout(LayoutKind.Sequential)] public struct VkCommandBufferBeginInfo
    { public int sType; public IntPtr pNext; public uint flags; public IntPtr pInheritanceInfo; }

    [StructLayout(LayoutKind.Sequential)] public struct VkSubmitInfo
    { public int sType; public IntPtr pNext; public uint waitSemaphoreCount; public IntPtr pWaitSemaphores; public IntPtr pWaitDstStageMask; public uint commandBufferCount; public IntPtr pCommandBuffers; public uint signalSemaphoreCount; public IntPtr pSignalSemaphores; }

    [StructLayout(LayoutKind.Sequential)] public struct VkFenceCreateInfo
    { public int sType; public IntPtr pNext; public uint flags; }

    [StructLayout(LayoutKind.Sequential)] public struct VkMemoryBarrier
    { public int sType; public IntPtr pNext; public uint srcAccessMask; public uint dstAccessMask; }

    [StructLayout(LayoutKind.Sequential)] public struct VkBufferCopy
    { public ulong srcOffset; public ulong dstOffset; public ulong size; }

    public static unsafe class Vk
    {

        [DllImport("vulkan-1.dll")] public static extern int vkCreateInstance(VkInstanceCreateInfo* info, IntPtr alloc, IntPtr* instance);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyInstance(IntPtr instance, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkEnumeratePhysicalDevices(IntPtr instance, uint* count, IntPtr* devices);
        [DllImport("vulkan-1.dll")] public static extern void vkGetPhysicalDeviceProperties(IntPtr phys, byte* props);          // VkPhysicalDeviceProperties (824 bytes)
        [DllImport("vulkan-1.dll")] public static extern void vkGetPhysicalDeviceMemoryProperties(IntPtr phys, byte* props);    // VkPhysicalDeviceMemoryProperties (520 bytes)
        [DllImport("vulkan-1.dll")] public static extern void vkGetPhysicalDeviceQueueFamilyProperties(IntPtr phys, uint* count, VkQueueFamilyProperties* props);
        [DllImport("vulkan-1.dll")] public static extern int vkCreateDevice(IntPtr phys, VkDeviceCreateInfo* info, IntPtr alloc, IntPtr* device);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyDevice(IntPtr device, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern void vkGetDeviceQueue(IntPtr device, uint family, uint index, IntPtr* queue);
        [DllImport("vulkan-1.dll")] public static extern int vkDeviceWaitIdle(IntPtr device);

        [DllImport("vulkan-1.dll")] public static extern int vkCreateBuffer(IntPtr device, VkBufferCreateInfo* info, IntPtr alloc, ulong* buffer);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyBuffer(IntPtr device, ulong buffer, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern void vkGetBufferMemoryRequirements(IntPtr device, ulong buffer, VkMemoryRequirements* req);
        [DllImport("vulkan-1.dll")] public static extern int vkAllocateMemory(IntPtr device, VkMemoryAllocateInfo* info, IntPtr alloc, ulong* memory);
        [DllImport("vulkan-1.dll")] public static extern void vkFreeMemory(IntPtr device, ulong memory, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkBindBufferMemory(IntPtr device, ulong buffer, ulong memory, ulong offset);
        [DllImport("vulkan-1.dll")] public static extern int vkMapMemory(IntPtr device, ulong memory, ulong offset, ulong size, uint flags, IntPtr* data);
        [DllImport("vulkan-1.dll")] public static extern void vkUnmapMemory(IntPtr device, ulong memory);

        [DllImport("vulkan-1.dll")] public static extern int vkCreateShaderModule(IntPtr device, VkShaderModuleCreateInfo* info, IntPtr alloc, ulong* module);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyShaderModule(IntPtr device, ulong module, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkCreateDescriptorSetLayout(IntPtr device, VkDescriptorSetLayoutCreateInfo* info, IntPtr alloc, ulong* layout);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyDescriptorSetLayout(IntPtr device, ulong layout, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkCreatePipelineLayout(IntPtr device, VkPipelineLayoutCreateInfo* info, IntPtr alloc, ulong* layout);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyPipelineLayout(IntPtr device, ulong layout, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkCreateComputePipelines(IntPtr device, ulong cache, uint count, VkComputePipelineCreateInfo* infos, IntPtr alloc, ulong* pipelines);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyPipeline(IntPtr device, ulong pipeline, IntPtr alloc);

        [DllImport("vulkan-1.dll")] public static extern int vkCreateDescriptorPool(IntPtr device, VkDescriptorPoolCreateInfo* info, IntPtr alloc, ulong* pool);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyDescriptorPool(IntPtr device, ulong pool, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkAllocateDescriptorSets(IntPtr device, VkDescriptorSetAllocateInfo* info, ulong* sets);
        [DllImport("vulkan-1.dll")] public static extern int vkFreeDescriptorSets(IntPtr device, ulong pool, uint count, ulong* sets);
        [DllImport("vulkan-1.dll")] public static extern void vkUpdateDescriptorSets(IntPtr device, uint writeCount, VkWriteDescriptorSet* writes, uint copyCount, IntPtr copies);

        [DllImport("vulkan-1.dll")] public static extern int vkCreateCommandPool(IntPtr device, VkCommandPoolCreateInfo* info, IntPtr alloc, ulong* pool);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyCommandPool(IntPtr device, ulong pool, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkAllocateCommandBuffers(IntPtr device, VkCommandBufferAllocateInfo* info, IntPtr* buffers);
        [DllImport("vulkan-1.dll")] public static extern int vkResetCommandBuffer(IntPtr cmd, uint flags);
        [DllImport("vulkan-1.dll")] public static extern int vkBeginCommandBuffer(IntPtr cmd, VkCommandBufferBeginInfo* info);
        [DllImport("vulkan-1.dll")] public static extern int vkEndCommandBuffer(IntPtr cmd);
        [DllImport("vulkan-1.dll")] public static extern void vkCmdBindPipeline(IntPtr cmd, int bindPoint, ulong pipeline);
        [DllImport("vulkan-1.dll")] public static extern void vkCmdBindDescriptorSets(IntPtr cmd, int bindPoint, ulong layout, uint firstSet, uint count, ulong* sets, uint dynCount, uint* dynOffsets);
        [DllImport("vulkan-1.dll")] public static extern void vkCmdPushConstants(IntPtr cmd, ulong layout, uint stages, uint offset, uint size, void* values);
        [DllImport("vulkan-1.dll")] public static extern void vkCmdDispatch(IntPtr cmd, uint x, uint y, uint z);
        [DllImport("vulkan-1.dll")] public static extern void vkCmdCopyBuffer(IntPtr cmd, ulong src, ulong dst, uint count, VkBufferCopy* regions);
        [DllImport("vulkan-1.dll")] public static extern void vkCmdPipelineBarrier(IntPtr cmd, uint srcStage, uint dstStage, uint depFlags,
            uint memBarrierCount, VkMemoryBarrier* memBarriers, uint bufBarrierCount, IntPtr bufBarriers, uint imgBarrierCount, IntPtr imgBarriers);

        [DllImport("vulkan-1.dll")] public static extern int vkQueueSubmit(IntPtr queue, uint count, VkSubmitInfo* submits, ulong fence);
        [DllImport("vulkan-1.dll")] public static extern int vkCreateFence(IntPtr device, VkFenceCreateInfo* info, IntPtr alloc, ulong* fence);
        [DllImport("vulkan-1.dll")] public static extern void vkDestroyFence(IntPtr device, ulong fence, IntPtr alloc);
        [DllImport("vulkan-1.dll")] public static extern int vkWaitForFences(IntPtr device, uint count, ulong* fences, uint waitAll, ulong timeout);
        [DllImport("vulkan-1.dll")] public static extern int vkResetFences(IntPtr device, uint count, ulong* fences);

        public static void Check(int result, string what)
        {
            if (result != 0) throw new InvalidOperationException(what + " failed: VkResult " + result + ".");
        }
    }
}
