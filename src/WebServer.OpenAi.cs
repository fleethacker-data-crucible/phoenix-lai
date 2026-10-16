// OpenAI-compatible API server, the same paths and port LM Studio uses (http://localhost:1234/v1), for VS Code extensions
// (Continue, Cline, Roo, ...) and other local tools. C# 5 only. Loopback only: bound to 127.0.0.1 / localhost, never makes
// outgoing connections. The API gives plain model access: no folder tools, no RAG, and no commands (ShellGuard).
//   GET  /v1/models                 models in models\ (chat and embedding)
//   POST /v1/chat/completions       messages -> reply (stream: true gives server-sent events)
//   POST /v1/completions            prompt -> reply (the prompt is sent as one user message through the chat template)
//   POST /v1/embeddings             input (string or array) -> vectors (EmbeddingGemma)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        public const int ApiPort = 1234;   // LM Studio's default, so tools configured for it work unchanged
        HttpListener _api;
        public string ApiUrl { get { return _api != null ? "http://localhost:" + ApiPort + "/v1" : null; } }

        void StartApi()
        {
            try
            {
                HttpListener l = new HttpListener();
                l.Prefixes.Add("http://127.0.0.1:" + ApiPort + "/");
                l.Prefixes.Add("http://localhost:" + ApiPort + "/");
                l.Start();
                _api = l;
                Thread t = new Thread(delegate()
                {
                    while (l.IsListening)
                    {
                        HttpListenerContext ctx;
                        try { ctx = l.GetContext(); } catch { return; }
                        ThreadPool.QueueUserWorkItem(delegate { ApiRequest(ctx); });
                    }
                });
                t.IsBackground = true;
                t.Start();
                Log.Text("API server on " + ApiUrl + " (OpenAI-compatible)");
            }
            catch (Exception ex) { _api = null; Log.Text("API server NOT started: port " + ApiPort + " is in use (" + ex.Message + ")"); }
        }

        void StopApi() { try { if (_api != null) { _api.Stop(); _api.Close(); } } catch { } }

        // Browsers may only call from loopback pages or VS Code webviews; tools without an Origin (Node, curl) are fine.
        static bool ApiOriginOk(string origin)
        {
            // No Origin: a program (VS Code extension host, curl). "null" is refused: that is what a downloaded HTML file
            // or a sandboxed frame sends, and such a page could otherwise drive the model.
            if (origin == null) return true;
            if (origin.StartsWith("vscode-webview://") || origin.StartsWith("vscode-file://")) return true;
            string o = origin.ToLowerInvariant();
            return o == "http://127.0.0.1:" + ApiPort || o == "http://localhost:" + ApiPort;
        }

        void ApiRequest(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            Interlocked.Increment(ref _activeRequests);
            try
            {
                string host = (req.Headers["Host"] ?? "").ToLowerInvariant();
                string origin = req.Headers["Origin"];
                if ((host != "127.0.0.1:" + ApiPort && host != "localhost:" + ApiPort) || !ApiOriginOk(origin))
                { ApiError(res, 403, "forbidden: local clients only"); return; }
                if (origin != null)
                {
                    res.Headers["Access-Control-Allow-Origin"] = origin;
                    res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
                    res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                }
                if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }
                string path = req.Url.AbsolutePath.TrimEnd('/');
                if (path.StartsWith("/api/v0/")) path = "/v1/" + path.Substring(8);   // LM Studio's REST alias
                bool post = req.HttpMethod == "POST";
                Dictionary<string, object> body = post ? ReadJson(req) : null;
                if (path == "/v1/models" && !post) { SendJson(res, 200, ModelsList()); return; }
                if (path.StartsWith("/v1/models/") && !post)
                {
                    string id = Uri.UnescapeDataString(path.Substring(11));
                    SendJson(res, 200, "{\"id\":" + Json.Quote(id) + ",\"object\":\"model\",\"owned_by\":\"organization_owner\"}");
                    return;
                }
                if (path == "/v1/chat/completions" && post) { Completion(body, res, true); return; }
                if (path == "/v1/completions" && post) { Completion(body, res, false); return; }
                if (path == "/v1/embeddings" && post) { Embeddings(body, res); return; }
                ApiError(res, 404, "unknown endpoint " + req.HttpMethod + " " + path);
            }
            catch (Exception ex)
            {
                Log.Text("API " + req.Url.AbsolutePath + " error: " + ex.Message);
                try { ApiError(res, 400, ex.Message); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        static void ApiError(HttpListenerResponse res, int code, string msg)
        {
            SendJson(res, code, "{\"error\":{\"message\":" + Json.Quote(msg) + ",\"type\":\"invalid_request_error\",\"code\":null}}");
        }

        static bool IsEmbeddingFile(string name) { return name.IndexOf("embed", StringComparison.OrdinalIgnoreCase) >= 0; }

        string ModelsList()
        {
            StringBuilder sb = new StringBuilder("{\"object\":\"list\",\"data\":[");
            bool first = true;
            if (Directory.Exists(_modelsDir))
                foreach (string f in Directory.GetFiles(_modelsDir, "*.gguf"))
                {
                    string id = Path.GetFileNameWithoutExtension(f);
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(Json.Quote(id)).Append(",\"object\":\"model\",\"type\":")
                      .Append(IsEmbeddingFile(id) ? "\"embeddings\"" : "\"llm\"").Append(",\"owned_by\":\"organization_owner\"}");
                }
            return sb.Append("]}").ToString();
        }

        // The chat model the request names (file name, with or without .gguf); loads it if another one is loaded.
        // An unknown or empty name uses the loaded model, or the last used one.
        void EnsureModel(string requested)
        {
            string file = null;
            if (!string.IsNullOrEmpty(requested) && requested.IndexOfAny(new char[] { '/', '\\', ':' }) < 0)
            {
                string f = requested.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? requested : requested + ".gguf";
                if (File.Exists(Path.Combine(_modelsDir, f)) && !IsEmbeddingFile(f)) file = f;
            }
            ChatEngine e = _engine;
            if (file != null && (e == null || !string.Equals(Path.GetFileName(e.ModelPath), file, StringComparison.OrdinalIgnoreCase))) { LoadModel(file); return; }
            if (e != null) return;
            string last = Path.Combine(_modelsDir, "last-model.txt");
            if (File.Exists(last)) { string n = File.ReadAllText(last).Trim(); if (File.Exists(Path.Combine(_modelsDir, n))) { LoadModel(n); return; } }
            foreach (string g in Directory.GetFiles(_modelsDir, "*.gguf"))
                if (!IsEmbeddingFile(Path.GetFileName(g))) { LoadModel(Path.GetFileName(g)); return; }
            throw new InvalidOperationException("No chat model in the models folder.");
        }

        // OpenAI message content is a string or a list of parts ({type: "text", text}); only text is used.
        static string ContentText(object c)
        {
            if (c == null) return "";
            if (c is string) return (string)c;
            List<object> parts = c as List<object>;
            if (parts == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (object p in parts)
            {
                Dictionary<string, object> d = p as Dictionary<string, object>;
                if (d != null && Json.GetStr(d, "type", "text") == "text") sb.Append(Json.GetStr(d, "text", ""));
            }
            return sb.ToString();
        }

        void Completion(Dictionary<string, object> o, HttpListenerResponse res, bool chat)
        {
            if (o == null) throw new ArgumentException("JSON body required");
            List<ChatMessage> msgs = new List<ChatMessage>();
            if (chat)
            {
                List<object> list = o.ContainsKey("messages") ? o["messages"] as List<object> : null;
                if (list == null || list.Count == 0) throw new ArgumentException("messages required");
                foreach (object m in list)
                {
                    Dictionary<string, object> mm = m as Dictionary<string, object>;
                    if (mm == null) continue;
                    string role = Json.GetStr(mm, "role", "user"), text = ContentText(mm.ContainsKey("content") ? mm["content"] : null);
                    if (role == "developer") role = "system";
                    else if (role == "tool" || role == "function") { role = "user"; text = "Tool result:\n" + text; }
                    if (role != "system" && role != "user" && role != "assistant") continue;
                    // The templates expect one leading system message: later ones are merged into it.
                    int si = msgs.FindIndex(delegate(ChatMessage x) { return x.Role == "system"; });
                    if (role == "system" && si >= 0) { msgs[si] = new ChatMessage("system", msgs[si].Text + "\n\n" + text); continue; }
                    if (role == "system") { msgs.Insert(0, new ChatMessage("system", text)); continue; }
                    msgs.Add(new ChatMessage(role, text));
                }
            }
            else
            {
                object p = o.ContainsKey("prompt") ? o["prompt"] : null;
                List<object> pl = p as List<object>;
                msgs.Add(new ChatMessage("user", pl != null && pl.Count > 0 ? ContentText(pl[0]) : ContentText(p)));
            }
            if (msgs.Count == 0 || msgs[msgs.Count - 1].Role == "assistant" && msgs.Count == 1) throw new ArgumentException("no user message");

            SamplerSettings s = new SamplerSettings();
            s.Temperature = (float)Json.GetNum(o, "temperature", 0.7);
            s.TopK = (int)Json.GetNum(o, "top_k", 40);
            s.TopP = (float)Json.GetNum(o, "top_p", 0.95);
            s.RepeatPenalty = (float)Json.GetNum(o, "repeat_penalty", 1.1);
            double fp = Json.GetNum(o, "frequency_penalty", 0);
            if (fp > 0 && !o.ContainsKey("repeat_penalty")) s.RepeatPenalty = 1f + (float)fp * 0.5f;
            if (o.ContainsKey("seed") && o["seed"] is double) s.Seed = (int)(double)o["seed"];
            int maxTokens = (int)Json.GetNum(o, "max_tokens", Json.GetNum(o, "max_completion_tokens", -1));
            if (maxTokens <= 0) maxTokens = int.MaxValue;   // OpenAI default: until the context is full
            bool stream = o.ContainsKey("stream") && o["stream"] is bool && (bool)o["stream"];
            bool usageInStream = false;
            Dictionary<string, object> so = o.ContainsKey("stream_options") ? o["stream_options"] as Dictionary<string, object> : null;
            if (so != null && so.ContainsKey("include_usage") && so["include_usage"] is bool) usageInStream = (bool)so["include_usage"];
            List<string> stops = new List<string>();
            object stopObj = o.ContainsKey("stop") ? o["stop"] : null;
            if (stopObj is string && ((string)stopObj).Length > 0) stops.Add((string)stopObj);
            List<object> stopList = stopObj as List<object>;
            if (stopList != null) foreach (object x in stopList) if (x is string && ((string)x).Length > 0) stops.Add((string)x);
            int maxStop = 0;
            foreach (string x in stops) maxStop = Math.Max(maxStop, x.Length);

            string id = (chat ? "chatcmpl-" : "cmpl-") + Guid.NewGuid().ToString("N").Substring(0, 24);
            long created = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            string obj = chat ? "chat.completion" : "text_completion";
            int rid = Interlocked.Increment(ref _requestId);
            Log.Text("#" + rid + " API " + (chat ? "chat" : "completion") + " (" + msgs.Count + " messages, stream " + stream + ")");

            lock (_engineLock)
            {
                EnsureModel(Json.GetStr(o, "model", null));
                ChatEngine eng = _engine;
                eng.PrefillProgress = null;
                string modelId = Path.GetFileNameWithoutExtension(eng.ModelPath);
                Stream outS = null;
                bool gone = false, stopped = false;
                StringBuilder text = new StringBuilder();
                int sent = 0;   // characters of text already streamed
                if (stream)
                {
                    res.StatusCode = 200;
                    res.ContentType = "text/event-stream; charset=utf-8";
                    res.Headers["Cache-Control"] = "no-cache";
                    res.SendChunked = true;
                    outS = res.OutputStream;
                }
                Action<string> sse = delegate(string json)
                {
                    if (gone) return;
                    try { byte[] b = Encoding.UTF8.GetBytes("data: " + json + "\n\n"); outS.Write(b, 0, b.Length); outS.Flush(); }
                    catch { gone = true; }
                };
                Func<string, string, string> chunk = delegate(string delta, string finish)
                {
                    string choice = chat
                        ? "{\"index\":0,\"delta\":" + (delta == null ? "{}" : (sent == 0 && delta.Length >= 0 && finish == null ? "{\"role\":\"assistant\",\"content\":" : "{\"content\":") + Json.Quote(delta) + "}")
                        : "{\"index\":0,\"text\":" + Json.Quote(delta ?? "");
                    choice += ",\"logprobs\":null,\"finish_reason\":" + (finish == null ? "null" : Json.Quote(finish)) + "}";
                    return "{\"id\":\"" + id + "\",\"object\":\"" + (chat ? "chat.completion.chunk" : "text_completion") + "\",\"created\":" + created +
                           ",\"model\":" + Json.Quote(modelId) + ",\"system_fingerprint\":\"phoenix\",\"choices\":[" + choice + "]}";
                };
                // Streams what is certain not to be the start of a stop sequence.
                Action flush = delegate()
                {
                    int safe = stopped ? text.Length : Math.Max(sent, text.Length - Math.Max(0, maxStop - 1));
                    if (stream && safe > sent) { string d = text.ToString(sent, safe - sent); sse(chunk(d, null)); sent = safe; }
                };
                GenerationStats st = eng.Generate(msgs, s, maxTokens, delegate(string piece)
                {
                    if (stopped) return;
                    text.Append(piece);
                    foreach (string x in stops)
                    {
                        int at = text.ToString().IndexOf(x, Math.Max(0, text.Length - piece.Length - x.Length), StringComparison.Ordinal);
                        if (at >= 0) { text.Length = at; stopped = true; break; }
                    }
                    flush();
                }, delegate { return gone || stopped; });
                stopped = true; flush();
                string finishReason = st.StopReason == "max tokens" || st.StopReason == "context full" ? "length" : "stop";
                int promptTokens = st.PromptTokens + st.ReusedTokens;
                string usage = "{\"prompt_tokens\":" + promptTokens + ",\"completion_tokens\":" + st.GeneratedTokens + ",\"total_tokens\":" + (promptTokens + st.GeneratedTokens) + "}";
                Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0} API done: {1} prompt ({2} cached), {3} generated at {4:F1} tok/s, {5}",
                    rid, promptTokens, st.ReusedTokens, st.GeneratedTokens, st.TokensPerSecond, finishReason));
                if (stream)
                {
                    sse(chunk(null, finishReason));
                    if (usageInStream)
                        sse("{\"id\":\"" + id + "\",\"object\":\"chat.completion.chunk\",\"created\":" + created + ",\"model\":" + Json.Quote(modelId) + ",\"choices\":[],\"usage\":" + usage + "}");
                    if (!gone) { try { byte[] b = Encoding.UTF8.GetBytes("data: [DONE]\n\n"); outS.Write(b, 0, b.Length); } catch { } }
                    try { outS.Close(); } catch { }
                    return;
                }
                string choiceFull = chat
                    ? "{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + Json.Quote(text.ToString()) + "},\"logprobs\":null,\"finish_reason\":\"" + finishReason + "\"}"
                    : "{\"index\":0,\"text\":" + Json.Quote(text.ToString()) + ",\"logprobs\":null,\"finish_reason\":\"" + finishReason + "\"}";
                SendJson(res, 200, "{\"id\":\"" + id + "\",\"object\":\"" + obj + "\",\"created\":" + created + ",\"model\":" + Json.Quote(modelId) +
                    ",\"system_fingerprint\":\"phoenix\",\"choices\":[" + choiceFull + "],\"usage\":" + usage + "}");
            }
        }

        void Embeddings(Dictionary<string, object> o, HttpListenerResponse res)
        {
            object input = o != null && o.ContainsKey("input") ? o["input"] : null;
            List<string> texts = new List<string>();
            if (input is string) texts.Add((string)input);
            List<object> il = input as List<object>;
            if (il != null) foreach (object x in il) if (x is string) texts.Add((string)x);
            if (texts.Count == 0) throw new ArgumentException("input (string or array of strings) required");
            StringBuilder sb = new StringBuilder("{\"object\":\"list\",\"data\":[");
            string modelId = null;
            int tokens = 0;
            for (int i = 0; i < texts.Count; i++)
            {
                float[] v = Rag.EmbedRaw(texts[i], out modelId);
                tokens += Math.Max(1, texts[i].Length / 4);
                if (i > 0) sb.Append(',');
                sb.Append("{\"object\":\"embedding\",\"index\":").Append(i).Append(",\"embedding\":[");
                for (int j = 0; j < v.Length; j++) { if (j > 0) sb.Append(','); sb.Append(v[j].ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                sb.Append("]}");
            }
            sb.Append("],\"model\":").Append(Json.Quote(modelId)).Append(",\"usage\":{\"prompt_tokens\":").Append(tokens).Append(",\"total_tokens\":").Append(tokens).Append("}}");
            SendJson(res, 200, sb.ToString());
        }
    }
}
