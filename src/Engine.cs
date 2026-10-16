// Chat engine: model loading (GPU with CPU fallback), Llama 3 chat template, KV-cache reuse across turns,
// sampling and streaming. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Phoenix
{
    public interface ILanguageModel : IDisposable
    {
        // Runs one token at position pos (positions must be fed in order); returns logits.
        float[] Forward(int token, int pos);
        // Feeds tokens[start .. start+count) at positions start..start+count-1; returns the last logits.
        float[] Prefill(IList<int> tokens, int start, int count);
        void Reset();
        int ContextLength { get; }
        int VocabSize { get; }
        // Earliest cached position that can be kept after `fed` tokens (0 = any; ring caches return more).
        int MinReuse(int fed);
        // Instant Recall: save the KV cache of positions pos0.. and put it back at another position (re-rotated).
        bool CanRecall { get; }
        KvSnapshot CaptureKv(int pos0, int count);
        void SpliceKv(KvSnapshot s, int dst0);
    }

    public sealed class ChatMessage
    {
        public string Role;   // "system", "user", "assistant"
        public string Text;
        public ChatMessage(string role, string text) { Role = role; Text = text; }
    }

    public sealed class GenerationStats
    {
        public int PromptTokens, ReusedTokens, GeneratedTokens, RecalledTokens;   // RecalledTokens: part of PromptTokens spliced, not read
        public double PromptSeconds, GenerateSeconds;
        public string StopReason = "";
        public double PromptTokensPerSecond { get { return PromptSeconds > 0 ? PromptTokens / PromptSeconds : 0; } }
        public double TokensPerSecond { get { return GenerateSeconds > 0 ? GeneratedTokens / GenerateSeconds : 0; } }
    }

    public delegate void TextCallback(string text);
    public delegate bool CancelCheck();

    public sealed class ChatEngine : IDisposable
    {
        public readonly string ModelPath, ModelName, Backend;
        readonly List<int> _banned = new List<int>();   // control tokens the model may not generate (see constructor)
        public readonly Tokenizer Tokenizer;
        public readonly int ContextLength;

        readonly GgufFile _gguf;
        readonly GpuDevice _gpu;
        readonly ILanguageModel _model;
        readonly List<int> _fed = new List<int>();   // tokens whose K/V are in the cache, by position
        // Chat template family, detected from the vocabulary.
        readonly bool _mistral, _gemma;
        readonly int _turn, _turnEnd, _channel, _channelEnd;   // Gemma 4 (<|turn>role ... <turn|>)
        readonly int _eot, _startHeader, _endHeader;           // Llama 3
        readonly int _inst, _instEnd, _sys, _sysEnd;            // Mistral ([INST] ... [/INST])

        ChatEngine(string path, GgufFile g, GpuDevice gpu, ILanguageModel model, string backend, int ctx)
        {
            ModelPath = path; _gguf = g; _gpu = gpu; _model = model; Backend = backend; ContextLength = ctx;
            ModelName = g.Has("general.name") ? g.GetString("general.name") : Path.GetFileNameWithoutExtension(path);
            Tokenizer = new Tokenizer(g);
            _eot = TokenId("<|eot_id|>");
            _startHeader = TokenId("<|start_header_id|>");
            _endHeader = TokenId("<|end_header_id|>");
            _inst = TokenId("[INST]");
            _instEnd = TokenId("[/INST]");
            _sys = TokenId("[SYSTEM_PROMPT]");
            _sysEnd = TokenId("[/SYSTEM_PROMPT]");
            _turn = TokenId("<|turn>");
            _turnEnd = TokenId("<turn|>");
            _channel = TokenId("<|channel>");
            _channelEnd = TokenId("<channel|>");
            // Gemma's native tool-call / thinking control tokens are never sampled: with the file-tool instructions in the
            // prompt it would otherwise open a native tool call as its first token and the reply ended empty (2026-10-16).
            // The app's own tools are plain-text tags (<read>, <write>, <list>), which it then uses instead.
            foreach (string name in new string[] { "<|tool>", "<tool|>", "<|tool_call>", "<tool_call|>", "<|tool_response>", "<tool_response|>", "<|think|>", "<|channel>", "<channel|>" })
            {
                int id = TokenId(name);
                if (id >= 0) _banned.Add(id);
            }
            _gemma = _turn >= 0 && _turnEnd >= 0;
            if (_gemma) _eot = _turnEnd;
            _mistral = !_gemma && _startHeader < 0 && _inst >= 0 && _instEnd >= 0;
            if (!_gemma && !_mistral && (_startHeader < 0 || _endHeader < 0))
                throw new NotSupportedException("Model has no supported chat template (Llama 3, Mistral or Gemma 4).");
        }

        int TokenId(string text)
        {
            for (int i = 0; i < Tokenizer.VocabSize; i++) if (Tokenizer.Tokens[i] == text) return i;
            return -1;
        }

        // Gemma 4 template (thinking off): <bos><|turn>system\nsys<turn|>\n<|turn>user\nmsg<turn|>\n<|turn>model\n
        // <|channel>thought\n<channel|> -- the empty thought channel tells the model to answer directly.
        List<int> GemmaTemplate(IList<ChatMessage> messages)
        {
            List<int> ids = new List<int>();
            ids.Add(Tokenizer.BosId);
            foreach (ChatMessage m in messages)
            {
                ids.Add(_turn);
                if (m.Role == "assistant")
                {
                    // A model turn is written exactly as it was generated (header, empty thought channel, the sampled
                    // tokens), so the KV cache of the reply is reused instead of re-reading it on the next request.
                    ids.AddRange(Tokenizer.Encode("model\n", false, false));
                    ids.Add(_channel);
                    ids.AddRange(Tokenizer.Encode("thought\n", false, false));
                    ids.Add(_channelEnd);
                    ids.AddRange(ReplyIds(m.Text));
                }
                else ids.AddRange(EncodeBody(m.Role + "\n", m.Text.Trim(), ids.Count));
                ids.Add(_turnEnd);
                ids.AddRange(Tokenizer.Encode("\n", false, false));
            }
            ids.Add(_turn);
            ids.AddRange(Tokenizer.Encode("model\n", false, false));
            ids.Add(_channel);
            ids.AddRange(Tokenizer.Encode("thought\n", false, false));
            ids.Add(_channelEnd);
            return ids;
        }

        // Loads a local .gguf file. Uses the GPU when Vulkan works, otherwise the CPU reference path.
        // KV cache format for GPU Llama models (q8 by default: 64/64 top-1 vs fp16 at half the memory). Gemma always
        // keeps fp16: its values have large outlier channels that 8 / 4-bit blocks lose (measured 54/64), and its
        // sliding-window cache is small anyway.
        public static KvType KvCache = KvType.Q8;

        public static KvType KvFor(GgufFile g)
        {
            return GemmaConfig.IsGemma(g.GetString("general.architecture")) ? KvType.F16 : KvCache;
        }

        public static ChatEngine Load(string path, int contextLength, bool allowGpu, TextCallback log)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Model file not found.", path);
            lock (GpuGate.Lock) return LoadLocked(path, contextLength, allowGpu, log);
        }

        static ChatEngine LoadLocked(string path, int contextLength, bool allowGpu, TextCallback log)
        {
            GgufFile g = GgufFile.Open(path);
            GpuDevice gpu = null;
            ILanguageModel model = null;
            string backend;
            try
            {
                if (allowGpu)
                {
                    try
                    {
                        string garch = g.GetString("general.architecture");
                        if (garch == "gemma-embedding2") throw new NotSupportedException("This is an embedding model (used for docs search), not a chat model.");
                        gpu = GpuDevice.Create();
                        int ctx = contextLength;
                        if (ctx <= 0)
                        {
                            ulong used, budget;
                            string why;
                            // VRAM, and system commit: on Windows the driver backs video memory with commit charge, so with a
                            // small page file a big cache can leave other programs unable to start (seen 2026-10-16).
                            if (gpu.MemoryBudget(out used, out budget))
                                ctx = ContextPlanner.Choose(g, path, true, (long)(budget - Math.Min(used, budget)), MemoryTrim.AvailableCommit(), out why);
                            else { ctx = 8192; why = "auto context 8192 (the driver does not report free VRAM)"; }
                            if (log != null) log(why);
                        }
                        KvType kv = KvFor(g);
                        if (log != null) log("KV cache " + KernelsKv.Name(kv) + (GemmaConfig.IsGemma(garch) && KvCache != KvType.F16 ? " (Gemma keeps fp16)" : ""));
                        model = GemmaConfig.IsGemma(garch) ? (ILanguageModel)new GemmaGpu(gpu, g, ctx, kv) : new LlamaGpu(gpu, g, ctx, kv);
                        contextLength = ctx;
                    }
                    catch (Exception ex)
                    {
                        if (log != null) log("GPU unavailable (" + ex.Message + "); using the CPU.");
                        if (model != null) model.Dispose();
                        if (gpu != null) gpu.Dispose();
                        model = null; gpu = null;
                    }
                }
                if (model != null)
                {
                    backend = "GPU: " + gpu.DeviceName + (gpu.FallbackBytes > 0 ? " (partly in system memory)" : "");
                }
                else
                {
                    string arch = g.GetString("general.architecture");
                    if (arch == "gemma-embedding2") throw new NotSupportedException("This is an embedding model (used for docs search), not a chat model.");
                    if (contextLength <= 0 || allowGpu)   // auto, or a GPU context that fell back here: size it for RAM
                    {
                        int requested = contextLength;
                        string why;
                        contextLength = ContextPlanner.Choose(g, path, false, MemoryTrim.AvailableRam(), MemoryTrim.AvailableCommit(), out why);
                        if (requested > 0) contextLength = Math.Min(requested, contextLength);
                        if (log != null) log(why);
                    }
                    model = GemmaConfig.IsGemma(arch) ? (ILanguageModel)new GemmaCpu(g, contextLength) : new LlamaCpu(g, contextLength);
                    backend = "CPU";
                }
                return new ChatEngine(path, g, gpu, model, backend, contextLength);
            }
            catch
            {
                if (model != null) model.Dispose();
                if (gpu != null) gpu.Dispose();
                g.Dispose();
                throw;
            }
        }

        // ---- Instant Recall ----
        // File contents the model read (registered by the server) are encoded as their own token run inside a message,
        // so their position in the prompt is known. The first time such a run is read, its KV cache is captured; when the
        // same file text comes up again (next message, another chat) it is spliced in instead of read: ~12x faster to the
        // first token for a 1,300-token file on Gemma 12B (4.7 s -> 0.4 s). Snapshots live in RAM up to RecallBudget.
        public static long RecallBudget = 3L << 30;
        const int RecallMinTokens = 64;
        readonly Dictionary<string, List<int>> _segIds = new Dictionary<string, List<int>>();
        readonly Dictionary<string, KvSnapshot> _recall = new Dictionary<string, KvSnapshot>();
        readonly List<string> _recallOrder = new List<string>();   // least recently used first
        long _recallBytes;
        sealed class Span { public int Start, Len; public string Key; }
        List<Span> _spans = new List<Span>();

        public void RegisterRecall(string text)
        {
            if (text == null || text.Length < 300 || _segIds.ContainsKey(text) || !_model.CanRecall) return;
            _segIds[text] = Tokenizer.Encode(text, false, false);
            if (_segIds.Count > 256) { foreach (string k in new List<string>(_segIds.Keys)) { if (!_recall.ContainsKey(k)) { _segIds.Remove(k); break; } } }
        }

        public long RecallBytes { get { return _recallBytes; } }
        public int RecallFiles { get { return _recall.Count; } }

        // Encodes a message body; registered file texts inside it become their own token runs (recorded in _spans).
        List<int> EncodeBody(string prefix, string text, int at)
        {
            List<int> ids = new List<int>();
            string rest = text;
            bool any = true;
            while (any)
            {
                any = false;
                int best = -1; string seg = null;
                foreach (string s in _segIds.Keys)
                {
                    int i = rest.IndexOf(s, StringComparison.Ordinal);
                    if (i >= 0 && (best < 0 || i < best)) { best = i; seg = s; }
                }
                if (seg == null) break;
                ids.AddRange(Tokenizer.Encode(prefix + rest.Substring(0, best), false, false));
                prefix = "";
                Span sp = new Span(); sp.Start = at + ids.Count; sp.Len = _segIds[seg].Count; sp.Key = seg;
                _spans.Add(sp);
                ids.AddRange(_segIds[seg]);
                rest = rest.Substring(best + seg.Length);
                any = true;
            }
            ids.AddRange(Tokenizer.Encode(prefix + rest, false, false));
            return ids;
        }

        void StoreRecall(string key, KvSnapshot s)
        {
            if (_recall.ContainsKey(key)) return;
            _recall[key] = s; _recallOrder.Add(key); _recallBytes += s.Bytes;
            while (_recallBytes > RecallBudget && _recallOrder.Count > 1)
            {
                string old = _recallOrder[0]; _recallOrder.RemoveAt(0);
                _recallBytes -= _recall[old].Bytes; _recall.Remove(old);
            }
        }

        // Token ids of recent replies exactly as sampled, by their text. Re-encoding a reply's text usually gives other
        // tokens than the model produced (BPE merges differ), which broke the prompt-cache match at that reply and made
        // every follow-up (the code check, the next message) re-read it: 1,264 tokens = 4-5 s on Gemma 12B.
        readonly Dictionary<string, List<int>> _sampled = new Dictionary<string, List<int>>();
        readonly Queue<string> _sampledOrder = new Queue<string>();

        List<int> ReplyIds(string text)
        {
            List<int> ids;
            if (_sampled.TryGetValue(text, out ids)) return ids;
            return Tokenizer.Encode(text.Trim(), false, false);
        }

        void RememberReply(string text, List<int> ids)
        {
            if (text.Length == 0 || _sampled.ContainsKey(text)) return;
            _sampled[text] = ids;
            _sampledOrder.Enqueue(text);
            while (_sampledOrder.Count > 32) _sampled.Remove(_sampledOrder.Dequeue());
        }

        // Llama 3 instruct template; ends with an open assistant header.
        public List<int> Template(IList<ChatMessage> messages)
        {
            _spans = new List<Span>();
            if (_gemma) return GemmaTemplate(messages);
            if (_mistral) return MistralTemplate(messages);
            List<int> ids = new List<int>();
            ids.Add(Tokenizer.BosId);
            foreach (ChatMessage m in messages)
            {
                ids.Add(_startHeader);
                ids.AddRange(Tokenizer.Encode(m.Role, false, false));
                ids.Add(_endHeader);
                if (m.Role == "assistant")
                {
                    // as generated: "\n\n" then the sampled tokens (KV cache reuse, see ReplyIds)
                    ids.AddRange(Tokenizer.Encode("\n\n", false, false));
                    ids.AddRange(ReplyIds(m.Text));
                }
                else ids.AddRange(EncodeBody("\n\n", m.Text.Trim(), ids.Count));
                ids.Add(_eot);
            }
            ids.Add(_startHeader);
            ids.AddRange(Tokenizer.Encode("assistant", false, false));
            ids.Add(_endHeader);
            ids.AddRange(Tokenizer.Encode("\n\n", false, false));
            return ids;
        }

        // Mistral template (Devstral / Mistral Small): <s>[SYSTEM_PROMPT]sys[/SYSTEM_PROMPT][INST]user[/INST]reply</s>...
        // Message text is encoded without special-token parsing, so user text cannot inject control tokens.
        List<int> MistralTemplate(IList<ChatMessage> messages)
        {
            List<int> ids = new List<int>();
            ids.Add(Tokenizer.BosId);
            foreach (ChatMessage m in messages)
            {
                string text = m.Text.Trim();
                if (m.Role == "system")
                {
                    if (_sys < 0) { ids.Add(_inst); ids.AddRange(Tokenizer.Encode(text, false, false)); ids.Add(_instEnd); continue; }
                    ids.Add(_sys); ids.AddRange(Tokenizer.Encode(text, false, false)); ids.Add(_sysEnd);
                }
                else if (m.Role == "user")
                {
                    ids.Add(_inst); ids.AddRange(Tokenizer.Encode(text, false, false)); ids.Add(_instEnd);
                }
                else
                {
                    ids.AddRange(Tokenizer.Encode(text, false, false)); ids.Add(Tokenizer.EosId);
                }
            }
            return ids;
        }

        // Runs a short throwaway generation so every GPU pipeline is created and the first real prompt starts at full
        // speed; the cache is cleared afterwards.
        public void WarmUp()
        {
            SamplerSettings s = new SamplerSettings(); s.Temperature = 0;
            List<ChatMessage> m = new List<ChatMessage>();
            m.Add(new ChatMessage("system", "Warm up.")); m.Add(new ChatMessage("user", "Hi"));
            Generate(m, s, 2, null, null);
            ClearCache();
        }

        public void ClearCache() { _fed.Clear(); _model.Reset(); }

        // VRAM of the GPU this model runs on (this process's use and its current budget); false on the CPU.
        public bool Vram(out ulong used, out ulong budget)
        {
            used = 0; budget = 0;
            return _gpu != null && _gpu.MemoryBudget(out used, out budget);
        }

        // Tokens currently held in the context (KV cache) for the conversation.
        public int UsedTokens { get { return _fed.Count; } }

        // Generates a reply to the conversation. Streams text through onText; returns stats.
        // Oldest non-system messages are dropped when the prompt would not leave room for maxTokens.
        // Called after each prompt chunk with (tokens done, tokens to do), e.g. to show progress on the slow CPU path.
        public Action<int, int> PrefillProgress;

        public GenerationStats Generate(List<ChatMessage> messages, SamplerSettings settings, int maxTokens, TextCallback onText, CancelCheck cancel)
        {
            lock (GpuGate.Lock) return GenerateLocked(messages, settings, maxTokens, onText, cancel);
        }

        GenerationStats GenerateLocked(List<ChatMessage> messages, SamplerSettings settings, int maxTokens, TextCallback onText, CancelCheck cancel)
        {
            GenerationStats st = new GenerationStats();
            List<ChatMessage> msgs = new List<ChatMessage>(messages);
            List<int> prompt = Template(msgs);
            int room = Math.Max(16, Math.Min(maxTokens, ContextLength / 2));
            while (prompt.Count + room > ContextLength)
            {
                int drop = msgs.FindIndex(delegate(ChatMessage m) { return m.Role != "system"; });
                if (drop < 0 || drop >= msgs.Count - 1) break;
                msgs.RemoveAt(drop);
                prompt = Template(msgs);
            }
            if (prompt.Count >= ContextLength) throw new InvalidOperationException("The message is too long for the context window (" + ContextLength + " tokens).");

            // Reuse the cached prefix.
            int reuse = 0;
            while (reuse < _fed.Count && reuse < prompt.Count - 1 && _fed[reuse] == prompt[reuse]) reuse++;
            if (reuse > 0 && reuse < _model.MinReuse(_fed.Count)) reuse = 0;   // ring cache no longer holds what that would need
            _fed.RemoveRange(reuse, _fed.Count - reuse);
            st.ReusedTokens = reuse;
            st.PromptTokens = prompt.Count - reuse;

            Stopwatch sw = Stopwatch.StartNew();
            float[] logits = null;
            // The prompt is fed in chunks so Stop stays responsive: 512 tokens on the GPU (~1-2 s each with batched
            // prefill), 8 on the CPU (a few tokens per second there, so a 512 chunk would ignore Stop for minutes).
            int chunk = Backend == "CPU" ? 8 : 512;
            List<Span> spans = _spans.FindAll(delegate(Span s) { return s.Start >= reuse && s.Start + s.Len < prompt.Count; });
            int pos = reuse, si = 0;
            while (pos < prompt.Count)
            {
                Span sp = si < spans.Count ? spans[si] : null;
                int end = sp != null ? sp.Start : prompt.Count;
                for (int i = pos; i < end; i += chunk)
                {
                    if (cancel != null && cancel()) { st.StopReason = "stopped"; return st; }
                    int n = Math.Min(chunk, end - i);
                    logits = _model.Prefill(prompt, i, n);
                    for (int j = 0; j < n; j++) _fed.Add(prompt[i + j]);
                    if (PrefillProgress != null) PrefillProgress(i + n - reuse, prompt.Count - reuse);
                }
                if (sp == null) break;
                si++;
                KvSnapshot snap;
                if (_recall.TryGetValue(sp.Key, out snap) && snap.Count == sp.Len)
                {
                    // seen before: splice its cache instead of reading it
                    _model.SpliceKv(snap, sp.Start);
                    for (int j = 0; j < sp.Len; j++) _fed.Add(prompt[sp.Start + j]);
                    st.RecalledTokens += sp.Len;
                    _recallOrder.Remove(sp.Key); _recallOrder.Add(sp.Key);
                }
                else if (sp.Len >= RecallMinTokens && _gemma)
                {
                    // First read of this file: read it on its own (after just the turn header), save that snapshot, then
                    // rebuild the conversation up to here and splice the file in. A snapshot taken inside the conversation
                    // would carry the question asked then (measured: a later chat about Write() got an answer about
                    // Resolve()); read alone, it is neutral and reusable by any later chat.
                    List<int> alone = new List<int>();
                    alone.Add(Tokenizer.BosId); alone.Add(_turn); alone.AddRange(Tokenizer.Encode("user\n", false, false));
                    int at = alone.Count;
                    alone.AddRange(_segIds[sp.Key]);
                    _model.Reset();
                    for (int i = 0; i < alone.Count; i += chunk)
                    {
                        if (cancel != null && cancel()) { _fed.Clear(); st.StopReason = "stopped"; return st; }
                        _model.Prefill(alone, i, Math.Min(chunk, alone.Count - i));
                    }
                    KvSnapshot fresh = _model.CaptureKv(at, sp.Len);
                    StoreRecall(sp.Key, fresh);
                    _fed.Clear();
                    for (int i = 0; i < sp.Start; i += chunk)
                    {
                        if (cancel != null && cancel()) { st.StopReason = "stopped"; return st; }
                        int n = Math.Min(chunk, sp.Start - i);
                        logits = _model.Prefill(prompt, i, n);
                        for (int j = 0; j < n; j++) _fed.Add(prompt[i + j]);
                    }
                    _model.SpliceKv(fresh, sp.Start);
                    for (int j = 0; j < sp.Len; j++) _fed.Add(prompt[sp.Start + j]);
                }
                else
                {
                    for (int i = sp.Start; i < sp.Start + sp.Len; i += chunk)
                    {
                        if (cancel != null && cancel()) { st.StopReason = "stopped"; return st; }
                        int n = Math.Min(chunk, sp.Start + sp.Len - i);
                        logits = _model.Prefill(prompt, i, n);
                        for (int j = 0; j < n; j++) _fed.Add(prompt[i + j]);
                    }
                }
                pos = sp.Start + sp.Len;
                if (PrefillProgress != null) PrefillProgress(pos - reuse, prompt.Count - reuse);
            }
            st.PromptSeconds = sw.Elapsed.TotalSeconds;

            Sampler sampler = new Sampler(settings);
            StringBuilder emitted = new StringBuilder();
            List<int> sampledIds = new List<int>();
            List<int> history = new List<int>(prompt);
            Decoder utf8 = Encoding.UTF8.GetDecoder();
            char[] chars = new char[64];
            sw.Restart();
            while (true)
            {
                foreach (int b in _banned) if (b < logits.Length) logits[b] = float.NegativeInfinity;
                int next = sampler.Sample(logits, history);
                if (next == _eot || next == Tokenizer.EosId) { st.StopReason = "end of turn"; break; }
                if (Tokenizer.IsSpecial(next)) { st.StopReason = "special token " + Encoding.UTF8.GetString(Tokenizer.TokenBytes(next, true)); break; }

                byte[] bytes = Tokenizer.TokenBytes(next, false);
                int nc = utf8.GetChars(bytes, 0, bytes.Length, chars, 0, false);
                if (nc > 0) { string piece = new string(chars, 0, nc); emitted.Append(piece); if (onText != null) onText(piece); }
                sampledIds.Add(next);
                st.GeneratedTokens++;
                history.Add(next);

                if (st.GeneratedTokens >= maxTokens) { st.StopReason = "max tokens"; break; }
                if (_fed.Count >= ContextLength) { st.StopReason = "context full"; break; }
                if (cancel != null && cancel()) { st.StopReason = "stopped"; break; }
                logits = _model.Forward(next, _fed.Count);
                _fed.Add(next);
            }
            st.GenerateSeconds = sw.Elapsed.TotalSeconds;
            RememberReply(emitted.ToString(), sampledIds);
            return st;
        }

        public void Dispose()
        {
            _model.Dispose();
            if (_gpu != null) _gpu.Dispose();
            _gguf.Dispose();
        }
    }
}
