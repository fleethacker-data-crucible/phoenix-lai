// Text embeddings for docs search with EmbeddingGemma 2 (models\*.gguf with architecture "gemma-embedding2"):
// on the GPU (GemmaEmbedGpu, its own Vulkan device) when possible, else on the CPU (GemmaCpu.Embed). C# 5 only.
using System;
using System.Collections.Generic;
using System.IO;

namespace Phoenix
{
    public sealed class Embedder : IDisposable
    {
        public const int MaxTokens = 512;   // chunks are ~400 tokens; longer text is cut (the start carries the topic)

        public readonly string ModelPath, ModelName;
        public readonly int Dim;
        readonly GgufFile _g;
        readonly Tokenizer _tok;
        GemmaCpu _model;
        GemmaEmbedGpu _gpuModel;
        public string Backend { get; private set; }
        readonly object _lock = new object();

        Embedder(string path)
        {
            _g = GgufFile.Open(path);
            try
            {
                _tok = new Tokenizer(_g);
                // GPU (per-layer submissions, GpuGate) with the CPU as fallback.
                try { _gpuModel = new GemmaEmbedGpu(_g, null); Backend = "GPU"; MemoryTrim.Trim(); }
                catch { _gpuModel = null; }
                if (_gpuModel == null) { _model = new GemmaCpu(_g, MaxTokens + 8); Backend = "CPU"; }
                Dim = _gpuModel != null ? _gpuModel.C.NEmbdOut : _model.C.NEmbdOut;
                ModelPath = path;
                ModelName = Path.GetFileNameWithoutExtension(path);
            }
            catch { _g.Dispose(); throw; }
        }

        // The first embedding model in modelsDir, or null when there is none.
        public static Embedder Find(string modelsDir)
        {
            if (!Directory.Exists(modelsDir)) return null;
            foreach (string f in Directory.GetFiles(modelsDir, "*.gguf"))
            {
                if (Path.GetFileName(f).IndexOf("embed", StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (GgufFile g = GgufFile.Open(f))
                    if (!g.Has("general.architecture") || g.GetString("general.architecture") != "gemma-embedding2") continue;
                return new Embedder(f);
            }
            return null;
        }

        // EmbeddingGemma 2 task prefixes (model card): queries and documents for search.
        public float[] Query(string text) { return Embed("task: search result | query: " + text); }
        public float[] Document(string title, string text) { return Embed("title: " + (string.IsNullOrEmpty(title) ? "none" : title) + " | text: " + text); }

        // Unit-length vector (BOS + text + EOS, mean pooled).
        public float[] Embed(string text)
        {
            lock (_lock)
            {
                List<int> ids = _tok.Encode(text, true, false);
                if (ids.Count > MaxTokens - 1) ids.RemoveRange(MaxTokens - 1, ids.Count - (MaxTokens - 1));
                ids.Add(_tok.EosId);
                if (_gpuModel != null)
                {
                    try { return _gpuModel.Embed(ids); }
                    catch (GpuDeviceLostException)
                    {
                        // The GPU was reset: continue on the CPU (results are equivalent).
                        try { _gpuModel.Dispose(); } catch { }
                        _gpuModel = null;
                        _model = new GemmaCpu(_g, MaxTokens + 8);
                        Backend = "CPU";
                    }
                }
                return _model.Embed(ids);
            }
        }

        public void Dispose() { if (_gpuModel != null) _gpuModel.Dispose(); _g.Dispose(); }
    }
}
