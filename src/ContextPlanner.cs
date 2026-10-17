// Picks the context length when a model loads ("auto"): the largest that fits the memory the model will live in,
// after its weights and a safety margin, capped at what the model was trained for. C# 5 only.
//   GPU: the device's free memory budget (VK_EXT_memory_budget: budget - this process's usage), fp16 KV cache,
//        Gemma's sliding-window layers as fixed-size rings.
//   CPU: available physical RAM (the weights are memory-mapped and need to stay resident to run at speed).
using System;
using System.IO;

namespace Phoenix
{
    public static class ContextPlanner
    {
        // Auto stops at 32K: small models use longer context poorly, and every token costs memory. A context typed in
        // /settings can go up to the model's limit.
        public const int Min = 2048, Step = 1024, Max = 32768;
        const long GpuMargin = 1536L << 20;      // docs-search embedder (~0.6 GB), prefill scratch, desktop growth
        const long CpuMargin = 2048L << 20;      // the rest of Windows and the user's programs
        const long CommitReserve = 3072L << 20;  // commit left for other programs (see Choose)

        // Memory per context token, and context-independent cache memory, for this model on this path.
        static void KvCost(GgufFile g, bool gpu, out long perToken, out long fixedBytes)
        {
            fixedBytes = 0;
            GemmaConfig c = GemmaConfig.FromGguf(g);
            perToken = gpu ? (long)c.NHead * 4 : 0;   // attention scores row
            for (int l = 0; l < c.NLayer; l++)
            {
                long kv = (long)c.NHeadKv[l] * c.HeadDim[l] * 2 * 2;   // K and V, fp16
                if (gpu && c.Swa[l]) fixedBytes += (c.NSwa + GemmaGpu.SwaSlack) * kv;
                else perToken += kv;
            }
        }

        static int Trained(GgufFile g)
        {
            string a = g.GetString("general.architecture");
            return g.Has(a + ".context_length") ? (int)g.GetInt(a + ".context_length") : 4096;
        }

        // freeBytes: free VRAM (GPU) or available RAM (CPU) for the whole model (weights + cache).
        // commitBytes: Windows commit still available. It binds on machines with a small page file: the GPU driver backs
        // video memory with commit, and the CPU path's cache is ordinary memory; the memory-mapped weights are not.
        // Returns the context, and why for the log.
        public static int Choose(GgufFile g, string path, bool gpu, long freeBytes, long commitBytes, out string why)
        {
            long perToken, fixedBytes;
            KvCost(g, gpu, out perToken, out fixedBytes);
            long weights = new FileInfo(path).Length;
            if (gpu) weights += weights / 50 + (256L << 20);   // row padding, prefill buffers
            long margin = gpu ? GpuMargin : CpuMargin;
            long room = freeBytes - weights - fixedBytes - margin;
            long commitRoom = commitBytes - CommitReserve - fixedBytes - (gpu ? weights : 0);
            bool commitBound = commitRoom < room;
            room = Math.Min(room, commitRoom);
            int trained = Math.Max(Min, Math.Min(Max, Trained(g)));
            int ctx = perToken > 0 ? (int)Math.Min(trained, Math.Max(0, room) / perToken) : trained;
            ctx = Math.Max(Min, ctx / Step * Step);
            why = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "auto context {0}: {1:F1} GB free {2}, {3:F1} GB commit free, weights {4:F1} GB, cache {5} KB/token{6}, trained for {7}{8}",
                ctx, freeBytes / 1073741824.0, gpu ? "VRAM" : "RAM", commitBytes / 1073741824.0, weights / 1073741824.0, perToken / 1024,
                fixedBytes > 0 ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " + {0:F2} GB fixed", fixedBytes / 1073741824.0) : "",
                Trained(g), commitBound ? "; limited by Windows commit (a larger page file would allow more)" : "");
            return ctx;
        }
    }
}
