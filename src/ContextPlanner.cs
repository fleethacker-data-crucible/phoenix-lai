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
        // Auto goes up to 128K (or the model's trained limit, if lower) when the memory is there: a small model leaves
        // room for a much longer context than a big one. A context typed in /settings can go up to the model's limit.
        public const int Min = 2048, Step = 1024, Max = 131072;
        const long GpuMargin = 1536L << 20;      // docs-search embedder (~0.6 GB), prefill scratch, desktop growth
        const long CpuMargin = 2048L << 20;      // the rest of Windows and the user's programs
        const long CommitReserve = 3072L << 20;  // commit left for other programs (see Choose)

        // Memory per context token, and context-independent cache memory, for this model on this path (Gemma's
        // sliding-window rings sized for the current GemmaGpu.SwaSlack).
        static void KvCost(GgufFile g, bool gpu, out long perToken, out long fixedBytes) { KvCost(g, gpu, GemmaGpu.SwaSlack, out perToken, out fixedBytes); }

        static void KvCost(GgufFile g, bool gpu, int slack, out long perToken, out long fixedBytes)
        {
            fixedBytes = 0;
            string arch = g.GetString("general.architecture");
            if (GemmaConfig.IsGemma(arch))
            {
                GemmaConfig c = GemmaConfig.FromGguf(g);
                perToken = gpu ? (long)c.NHead * 4 : 0;   // attention scores row
                for (int l = 0; l < c.NLayer; l++)
                {
                    if (c.KvSrc[l] != l) continue;   // E2B/E4B shared-KV layer: reads an earlier layer's cache
                    long kv = (long)c.NHeadKv[l] * c.HeadDim[l] * 2 * 2;   // K and V, fp16
                    if (gpu && c.Swa[l]) fixedBytes += (c.NSwa + slack) * kv;
                    else perToken += kv;
                }
                return;
            }
            ModelConfig m = ModelConfig.FromGguf(g);
            long kvDim = (long)m.NHeadKv * m.HeadDim;
            double perValue = KernelsKv.BytesPerValue(ChatEngine.KvFor(g));   // fp16 2, q8 ~1.06, q4 ~0.56 bytes
            perToken = gpu ? (long)(m.NLayer * kvDim * 2 * perValue) + (long)m.NHead * 4 + (long)m.NHead * m.HeadDim * 4 / KernelsAttention.Chunk
                           : m.NLayer * kvDim * 2 * 4;   // the CPU path keeps f32
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
        // The least memory a load of this model needs: weights (as Choose counts them), the fixed cache, the margin and the
        // smallest context (Min). commitNeed: what it takes from Windows commit (GPU weights are backed by commit too).
        public static long MinBytes(GgufFile g, string path, bool gpu, out long commitNeed)
        {
            long perToken, fixedBytes;
            KvCost(g, gpu, MinSlack, out perToken, out fixedBytes);   // the smallest rewind buffer a load can use
            long weights = Weights(g, path, gpu), kv = fixedBytes + (long)Min * perToken;
            commitNeed = CommitReserve + kv + (gpu ? weights : 0);
            return weights + kv + (gpu ? GpuMargin : CpuMargin);
        }

        // Gemma's rewind buffer (GemmaGpu.SwaSlack: positions its sliding-window rings keep beyond the window), sized to the
        // memory at load. Bigger lets the next message rewind further without re-reading the whole conversation (each
        // message's added sections are dropped from the history, so it rewinds to the previous message: ~2-4K tokens), but
        // costs 12-320 KB per position (E2B .. 12B). Auto context: up to half the room left after the weights and the
        // smallest cache; a typed context: what is left after it. Never below MinSlack (the old fixed size).
        public const int MinSlack = 512, MaxSlack = 4096;

        public static int ChooseSlack(GgufFile g, string path, long freeBytes, long commitBytes, int context, out string why)
        {
            why = null;
            if (!GemmaConfig.IsGemma(g.GetString("general.architecture"))) return MinSlack;
            long perToken, fixedMin, fixedMax;
            KvCost(g, true, MinSlack, out perToken, out fixedMin);
            KvCost(g, true, MinSlack + 1024, out perToken, out fixedMax);
            long perSlack = (fixedMax - fixedMin) / 1024;   // bytes per extra position
            if (perSlack <= 0) return MinSlack;
            long weights = Weights(g, path, true);
            long room = Math.Min(freeBytes - weights - GpuMargin, commitBytes - CommitReserve - weights) - fixedMin -
                        (long)Math.Max(Min, context) * perToken;
            if (context <= 0) room /= 2;   // auto: the other half goes to the context
            long extra = Math.Max(0, Math.Min(MaxSlack - MinSlack, room / perSlack));
            int slack = MinSlack + (int)(extra / 512 * 512);
            why = "rewind buffer " + slack + " tokens (" + (slack * perSlack >> 20) + " MB)";
            return slack;
        }

        static long Weights(GgufFile g, string path, bool gpu)
        {
            long weights = new FileInfo(path).Length;
            GgufTensor ple;   // E2B/E4B per-layer token table: stays memory-mapped on the CPU, never uploaded
            if (gpu && g.TensorsByName.TryGetValue("per_layer_token_embd.weight", out ple)) weights -= ple.SizeBytes;
            // its vision tower (loaded on the first image) gets room too
            if (gpu && GemmaConfig.IsGemma(g.GetString("general.architecture")))
                try
                {
                    string mm = VisionEncoder.FindFor(path, (int)g.GetInt(g.GetString("general.architecture") + ".embedding_length"));
                    if (mm != null) weights += VisionEncoder.VramBytes(mm);
                }
                catch { }
            if (gpu) weights += weights / 50 + (256L << 20);   // row padding, prefill buffers
            return weights;
        }

        public static int Choose(GgufFile g, string path, bool gpu, long freeBytes, long commitBytes, out string why)
        {
            long perToken, fixedBytes;
            KvCost(g, gpu, out perToken, out fixedBytes);
            long weights = Weights(g, path, gpu);
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
