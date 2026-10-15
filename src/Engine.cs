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
    }

    public sealed class ChatMessage
    {
        public string Role;   // "system", "user", "assistant"
        public string Text;
        public ChatMessage(string role, string text) { Role = role; Text = text; }
    }

    public sealed class GenerationStats
    {
        public int PromptTokens, ReusedTokens, GeneratedTokens;
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
        public readonly Tokenizer Tokenizer;
        public readonly int ContextLength;

        readonly GgufFile _gguf;
        readonly GpuDevice _gpu;
        readonly ILanguageModel _model;
        readonly List<int> _fed = new List<int>();   // tokens whose K/V are in the cache, by position
        // Chat template family, detected from the vocabulary.
        readonly bool _mistral;
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
            _mistral = _startHeader < 0 && _inst >= 0 && _instEnd >= 0;
            if (!_mistral && (_startHeader < 0 || _endHeader < 0))
                throw new NotSupportedException("Model has neither Llama 3 nor Mistral chat-template tokens.");
        }

        int TokenId(string text)
        {
            for (int i = 0; i < Tokenizer.VocabSize; i++) if (Tokenizer.Tokens[i] == text) return i;
            return -1;
        }

        // Loads a local .gguf file. Uses the GPU when Vulkan works, otherwise the CPU reference path.
        public static ChatEngine Load(string path, int contextLength, bool allowGpu, TextCallback log)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Model file not found.", path);
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
                        gpu = GpuDevice.Create();
                        model = new LlamaGpu(gpu, g, contextLength);
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
                    model = new LlamaCpu(g, contextLength);
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

        // Llama 3 instruct template; ends with an open assistant header.
        public List<int> Template(IList<ChatMessage> messages)
        {
            if (_mistral) return MistralTemplate(messages);
            List<int> ids = new List<int>();
            ids.Add(Tokenizer.BosId);
            foreach (ChatMessage m in messages)
            {
                ids.Add(_startHeader);
                ids.AddRange(Tokenizer.Encode(m.Role, false, false));
                ids.Add(_endHeader);
                ids.AddRange(Tokenizer.Encode("\n\n" + m.Text.Trim(), false, false));
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

        public void ClearCache() { _fed.Clear(); _model.Reset(); }

        // Generates a reply to the conversation. Streams text through onText; returns stats.
        // Oldest non-system messages are dropped when the prompt would not leave room for maxTokens.
        public GenerationStats Generate(List<ChatMessage> messages, SamplerSettings settings, int maxTokens, TextCallback onText, CancelCheck cancel)
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
            _fed.RemoveRange(reuse, _fed.Count - reuse);
            st.ReusedTokens = reuse;
            st.PromptTokens = prompt.Count - reuse;

            Stopwatch sw = Stopwatch.StartNew();
            float[] logits = null;
            const int Chunk = 256; // prompt is fed in chunks so Stop stays responsive
            for (int i = reuse; i < prompt.Count; i += Chunk)
            {
                if (cancel != null && cancel()) { st.StopReason = "stopped"; return st; }
                int n = Math.Min(Chunk, prompt.Count - i);
                logits = _model.Prefill(prompt, i, n);
                for (int j = 0; j < n; j++) _fed.Add(prompt[i + j]);
            }
            st.PromptSeconds = sw.Elapsed.TotalSeconds;

            Sampler sampler = new Sampler(settings);
            List<int> history = new List<int>(prompt);
            Decoder utf8 = Encoding.UTF8.GetDecoder();
            char[] chars = new char[64];
            sw.Restart();
            while (true)
            {
                int next = sampler.Sample(logits, history);
                if (next == _eot || next == Tokenizer.EosId) { st.StopReason = "end of turn"; break; }
                if (Tokenizer.IsSpecial(next)) { st.StopReason = "special token"; break; }

                byte[] bytes = Tokenizer.TokenBytes(next, false);
                int nc = utf8.GetChars(bytes, 0, bytes.Length, chars, 0, false);
                if (nc > 0 && onText != null) onText(new string(chars, 0, nc));
                st.GeneratedTokens++;
                history.Add(next);

                if (st.GeneratedTokens >= maxTokens) { st.StopReason = "max tokens"; break; }
                if (_fed.Count >= ContextLength) { st.StopReason = "context full"; break; }
                if (cancel != null && cancel()) { st.StopReason = "stopped"; break; }
                logits = _model.Forward(next, _fed.Count);
                _fed.Add(next);
            }
            st.GenerateSeconds = sw.Elapsed.TotalSeconds;
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
