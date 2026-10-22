// What the chat pipeline (WebServer: chat, tool loop, code check, compact, skill assist) needs from a model, so the same
// code runs in the server on the local engine (ChatEngine) and in the Phoenix Client on any OpenAI /v1 provider
// (RemoteChatModel, Providers.cs). Same for embeddings (docs search, project index): IEmbedder, local Embedder or
// RemoteEmbedder. C# 5 only.
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public interface IChatModel : IDisposable
    {
        string ModelPath { get; }   // a file path (local engine) or the provider's model id (remote)
        string ModelName { get; }
        string Backend { get; }     // "GPU: <card>" / "CPU" (local) or "<provider name>" (remote)
        int ContextLength { get; }
        bool HasVision { get; }
        int UsedTokens { get; }     // tokens of the conversation the model holds (remote: from the last reply's usage)
        Action<int, int> PrefillProgress { get; set; }
        GenerationStats Generate(List<ChatMessage> messages, SamplerSettings settings, int maxTokens, TextCallback onText, CancelCheck cancel);
        // Local-engine features (KV cache): Instant Recall and pre-reading. A remote model does nothing here.
        string RecallDir { get; set; }
        int RecallFiles { get; }
        long RecallBytes { get; }
        bool PreRead(string text, CancelCheck cancel);
        void RegisterRecall(string text);
        void WarmUp();
        bool Vram(out ulong used, out ulong budget);
    }

    public interface IEmbedder : IDisposable
    {
        string ModelPath { get; }
        string Backend { get; }
        int Dim { get; }
        float[] Query(string text);
        float[] Document(string title, string text);
        float[] Embed(string text);
    }
}
