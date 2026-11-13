// OpenAI-compatible API server, the same paths LM Studio uses, on its own port (http://localhost:1893/v1), for VS Code extensions
// (Continue, Cline, Roo, ...), the Phoenix client and any other OpenAI /v1 client. C# 5 only. Loopback only: bound to
// 127.0.0.1 / localhost, never makes outgoing connections. The API gives plain model access: no folder tools, no RAG, and
// no commands (ShellGuard); tool calls are only described to the model and returned to the client, which decides whether
// to run them. Any API key is accepted (the server is reachable from this computer only).
//   GET  /v1/models                 models in models\ (chat and embedding)
//   GET  /v1/models/{id}            one model (404 model_not_found for an unknown id)
//   POST /v1/chat/completions       messages -> reply (stream: true gives server-sent events); n, stop, seed, max_tokens /
//                                   max_completion_tokens, tools + tool_choice, response_format (json_object / json_schema),
//                                   stream_options.include_usage, logprobs + top_logprobs (0-20); an unknown model name is
//                                   404 model_not_found (no name, or "phoenixlai", = the loaded model)
//   POST /v1/completions            prompt -> reply (the prompt is sent as one user message through the chat template);
//                                   with "suffix" it is code completion: fill-in-the-middle, no chat template
//   Chat requests with "tools" get OpenAI tool_calls back (ApiTools.cs); the server never runs a tool itself.
//   POST /v1/embeddings             input (string or array) -> vectors (EmbeddingGemma); encoding_format float|base64, dimensions
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
        sealed class ApiModelNotFound : Exception
        {
            public ApiModelNotFound(string id, bool tooBig)
                : base(tooBig ? "The model '" + id + "' is not available: it does not fit the memory free on this machine now" : "The model '" + id + "' does not exist") { }
        }

        // The API port: 1893 by default (not LM Studio's 1234, so both can run side by side). Changed live with the console
        // command "apiport <n>" or the dashboard's port box; kept in %LOCALAPPDATA%\LocalLLMStudio\api-port.txt, which the
        // Phoenix client also reads for the Phoenix Local Server's URL.
        public const int DefaultApiPort = 1893;
        static string ApiPortFile { get { return Path.Combine(AppDataDir, "api-port.txt"); } }
        static int _apiPort = SavedApiPort();
        public static int ApiPort { get { return _apiPort; } }

        public static int SavedApiPort()
        {
            try
            {
                int p;
                if (File.Exists(ApiPortFile) && int.TryParse(File.ReadAllText(ApiPortFile).Trim(), out p) && ApiPortProblem(p) == null) return p;
            }
            catch { }
            return DefaultApiPort;
        }

        // null when p can be the API port, else why not
        public static string ApiPortProblem(int p)
        {
            if (p < 1024 || p > 65535) return "the port must be a number from 1024 to 65535";
            if (p >= 8765 && p <= 8799) return "ports 8765-8799 are Phoenix's own dashboard and client pages";
            return null;
        }

        // The model whose sampling the console and dashboard show and change: the loaded one, else the one loading.
        string SamplingModel()
        {
            IChatModel e = _engine;
            if (e != null) return Path.GetFileName(e.ModelPath);
            return _loading;
        }

        HttpListener _api;
        readonly object _apiGate = new object();
        string _lastToolList;   // logged when a client's tool set changes
        public string ApiUrl { get { return _api != null ? "http://localhost:" + ApiPort + "/v1" : null; } }

        // Moves the API to another port now. The new port is opened first: if it is busy the API stays where it is.
        // Requests still running on the old port are cut off. Returns what happened, for the console and the dashboard.
        public string SetApiPort(int p)
        {
            string why = ApiPortProblem(p);
            if (why != null) throw new ArgumentException(why + ".");
            lock (_apiGate)
            {
                if (p == _apiPort && _api != null) return "The API is already on port " + p + ".";
                HttpListener l;
                try { l = OpenApi(p); }
                catch (Exception ex) { throw new InvalidOperationException("Port " + p + " could not be opened (" + ex.Message + "); the API stays on " + (_api != null ? "port " + _apiPort : "no port") + "."); }
                StopApi();
                _apiPort = p;
                RunApi(l);
                try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(ApiPortFile, p.ToString()); } catch { }
            }
            Log.Text("API port set to " + p + ": " + ApiUrl);
            return "API now on " + ApiUrl + " (saved). Point clients (Fairlead, the VS Code extension, the Phoenix client's provider) at this port.";
        }

        static HttpListener OpenApi(int port)
        {
            HttpListener l = new HttpListener();
            l.Prefixes.Add("http://127.0.0.1:" + port + "/");
            l.Prefixes.Add("http://localhost:" + port + "/");
            l.Start();
            return l;
        }

        void StartApi()
        {
            try { RunApi(OpenApi(ApiPort)); }
            catch (Exception ex) { _api = null; Log.Text("API server NOT started: port " + ApiPort + " is in use (" + ex.Message + ")"); }
        }

        void RunApi(HttpListener l)
        {
            {
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
                Log.Text("API server on " + ApiUrl + " (" + ApiFormatName + ")");
            }
        }

        void StopApi() { try { if (_api != null) { _api.Stop(); _api.Close(); } } catch { } _api = null; }

        // Browsers may only call from loopback pages or VS Code webviews; tools without an Origin (Node, curl) are fine.
        static bool ApiOriginOk(string origin)
        {
            // No Origin: a program (VS Code extension host, curl, the Phoenix client). "null" is refused: that is what a
            // downloaded HTML file or a sandboxed frame sends, and such a page could otherwise drive the model.
            if (origin == null) return true;
            if (origin.StartsWith("vscode-webview://") || origin.StartsWith("vscode-file://")) return true;
            string o = origin.ToLowerInvariant();
            return o == "http://127.0.0.1:" + ApiPort || o == "http://localhost:" + ApiPort;
        }

        // Where an API answer goes: the HTTP response, or a buffer (the console's in-process self-test).
        sealed class ApiOut
        {
            public HttpListenerResponse Res;
            public MemoryStream Buffer;
            public Func<Stream, Stream> Wrap;   // the Anthropic API turns the OpenAI events into its own (WebServer.Anthropic.cs)
            Stream _s;
            public Stream BeginStream()
            {
                if (Res == null) return _s = Buffer;
                Res.StatusCode = 200;
                Res.ContentType = "text/event-stream; charset=utf-8";
                Res.Headers["Cache-Control"] = "no-cache";
                Res.SendChunked = true;
                return _s = Wrap != null ? Wrap(Res.OutputStream) : Res.OutputStream;
            }
            public void Json(int code, string json)
            {
                if (Res != null) { SendJson(Res, code, json); return; }
                byte[] b = Encoding.UTF8.GetBytes(json);
                Buffer.Write(b, 0, b.Length);
            }
            public void End() { if (Res != null) try { _s.Close(); } catch { } }
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
                { ApiError(res, 403, "forbidden: local clients only", "permission_error", null); return; }
                if (origin != null)
                {
                    res.Headers["Access-Control-Allow-Origin"] = origin;
                    res.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, OpenAI-Organization, OpenAI-Project, X-Stainless-OS, X-Stainless-Lang, X-Stainless-Package-Version, X-Stainless-Runtime, X-Stainless-Runtime-Version, X-Stainless-Arch, X-Stainless-Retry-Count, X-Stainless-Timeout, x-api-key, anthropic-version, anthropic-beta, anthropic-dangerous-direct-browser-access";
                    res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                }
                if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }
                if (req.HttpMethod == "HEAD") { res.StatusCode = 200; res.ContentLength64 = 0; res.Close(); return; }   // no body allowed
                string path = req.Url.AbsolutePath.TrimEnd('/');
                if (path == "/api/v1/models" && req.HttpMethod == "GET") { SendJson(res, 200, LmStudioModels()); return; }   // LM Studio 0.4's native list
                if (path.StartsWith("/api/v0/")) path = "/v1/" + path.Substring(8);   // LM Studio's REST alias
                string ua = req.Headers["User-Agent"];
                SeeClient("api:" + (ua ?? "unknown"), "api", ClientName(ua), req.HttpMethod + " " + path);
                // Anthropic's shapes (errors, model list) for /v1/messages, a request with anthropic-version, or the anthropic-only format
                bool anth = AnthropicOn && (path.StartsWith("/v1/messages") || req.Headers["anthropic-version"] != null || !OpenAiOn);
                string xKey = req.Headers["x-api-key"];   // Anthropic clients send the key here
                if (!ApiKeyOk(req.Headers["Authorization"]) && !(xKey != null && ApiKeyOk("Bearer " + xKey)))
                {
                    const string km = "Incorrect API key provided. This Phoenix server requires its API key (console: apikey).";
                    if (anth) AnthError(res, 401, km, "authentication_error");
                    else ApiError(res, 401, km, "invalid_request_error", "invalid_api_key");
                    return;
                }
                bool post = req.HttpMethod == "POST";
                Dictionary<string, object> body;
                try { body = post ? ReadJson(req) : null; }
                catch (Exception ex) { ApiError(res, 400, "We could not parse the JSON body of your request. (" + ex.Message + ")", "invalid_request_error", null); return; }
                if (post && body == null) { ApiError(res, 400, "The request body must be a JSON object.", "invalid_request_error", null); return; }
                if (path == "/v1/models" && !post) { SendJson(res, 200, anth ? AnthModelsList() : ModelsList()); return; }
                if (path.StartsWith("/v1/models/") && !post && anth)
                {
                    string aid = Uri.UnescapeDataString(path.Substring(11)), one = AnthModelJson(aid);
                    if (one == null) AnthError(res, 404, "model: " + aid, "not_found_error"); else SendJson(res, 200, one);
                    return;
                }
                if (path.StartsWith("/v1/models/") && !post)
                {
                    string id = Uri.UnescapeDataString(path.Substring(11));
                    string one = ModelJson(id);
                    if (one == null) { ApiError(res, 404, new ApiModelNotFound(id, ModelFileInModelsDir(id)).Message, "invalid_request_error", "model_not_found", "model"); return; }
                    SendJson(res, 200, one);
                    return;
                }
                Dbg.Source = "api" + (ua != null ? " (" + ua + ")" : "");
                ApiOut o = new ApiOut(); o.Res = res;
                string src = ClientName(ua);
                if (AnthropicOn && path == "/v1/messages" && post) { Messages(body, res, src); return; }
                if (AnthropicOn && path == "/v1/messages/count_tokens" && post) { SendJson(res, 200, CountTokensJson(body)); return; }
                if (AnthropicOn && path.StartsWith("/v1/messages")) { AnthError(res, post ? 404 : 405, post ? "Not found: " + path : "Use POST for " + path + ".", post ? "not_found_error" : "invalid_request_error"); return; }
                if (OpenAiOn && path == "/v1/chat/completions" && post) { Completion(body, o, true, src); return; }
                if (OpenAiOn && path == "/v1/completions" && post) { Completion(body, o, false, src); return; }
                if (path == "/v1/embeddings" && post) { Embeddings(body, res); return; }   // every format: the client's docs search uses it
                if (anth) { AnthError(res, 404, "Not found: " + req.HttpMethod + " " + path + " (this server hosts the " + ApiFormatName + " API format; dashboard Settings or console apiformat)", "not_found_error"); return; }
                if (path.StartsWith("/v1/") && (path == "/v1/chat/completions" || path == "/v1/completions" || path == "/v1/embeddings"))
                { ApiError(res, 405, "Use POST for " + path + ".", "invalid_request_error", null); return; }
                ApiError(res, 404, "Invalid URL (" + req.HttpMethod + " " + path + ")", "invalid_request_error", "unknown_url");
            }
            catch (ApiModelNotFound ex)
            {
                Log.Text("API " + req.Url.AbsolutePath + " error: " + ex.Message);
                try { ApiError(res, 404, ex.Message, "invalid_request_error", "model_not_found", "model"); } catch { }
            }
            catch (ArgumentException ex)
            {
                Log.Text("API " + req.Url.AbsolutePath + " error: " + ex.Message);
                try { ApiError(res, 400, ex.Message, "invalid_request_error", null); } catch { }
            }
            catch (Exception ex)
            {
                Log.Text("API " + req.Url.AbsolutePath + " error: " + ex.Message);
                try { ApiError(res, 500, ex.Message, "server_error", null); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        // ---------- optional API key ----------
        // Off by default (any key, or none, is accepted: the API is reachable from this computer only). Set with the
        // console's apikey command; kept in Windows Credential Manager (resource PhoenixLAI.Server), never in a file.
        // With a key set, every /v1 request needs "Authorization: Bearer <key>", as OpenAI's API does.
        public const string ApiKeyResource = "PhoenixLAI.Server";
        static string _apiKey = LoadApiKey();
        static string LoadApiKey() { try { return CredStore.Read(ApiKeyResource, "api-key"); } catch { return null; } }

        public static bool ApiKeyRequired { get { return !string.IsNullOrEmpty(_apiKey); } }

        public static void SetApiKey(string key)
        {
            if (string.IsNullOrEmpty(key)) CredStore.Delete(ApiKeyResource, "api-key");
            else CredStore.Write(ApiKeyResource, "api-key", key);
            _apiKey = string.IsNullOrEmpty(key) ? null : key;
        }

        static bool ApiKeyOk(string authorization)
        {
            string want = _apiKey;
            if (string.IsNullOrEmpty(want)) return true;
            string got = authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization.Substring(7).Trim() : "";
            // constant-time compare: the reply time says nothing about how much of a guess was right
            int diff = got.Length ^ want.Length;
            for (int i = 0; i < want.Length; i++) diff |= want[i] ^ (i < got.Length ? got[i] : 0);
            return diff == 0;
        }

        static void ApiError(HttpListenerResponse res, int code, string msg, string type, string errCode, string param = null)
        {
            SendJson(res, code, ErrorJson(msg, type, errCode, param));
        }

        static string ErrorJson(string msg, string type, string errCode, string param)
        {
            return "{\"error\":{\"message\":" + Json.Quote(msg) + ",\"type\":" + Json.Quote(type) + ",\"param\":" + (param != null ? Json.Quote(param) : "null") +
                   ",\"code\":" + (errCode != null ? Json.Quote(errCode) : "null") + "}}";
        }

        static bool IsEmbeddingFile(string name) { return name.IndexOf("embed", StringComparison.OrdinalIgnoreCase) >= 0; }
        static bool IsProjector(string name) { return name.IndexOf("mmproj", StringComparison.OrdinalIgnoreCase) >= 0; }
        // A model any list may show: never a vision projector or the embedding model.
        static bool IsListedModel(string name) { return !IsProjector(name) && !IsEmbeddingFile(name); }

        // created: the file's time (OpenAI's field is when the model was made available)
        string ModelEntry(string f)
        {
            string id = Path.GetFileNameWithoutExtension(f);
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"id\":").Append(Json.Quote(id)).Append(",\"object\":\"model\",\"created\":")
              .Append((long)(File.GetLastWriteTimeUtc(f) - new DateTime(1970, 1, 1)).TotalSeconds).Append(",\"type\":")
              .Append(IsEmbeddingFile(id) ? "\"embeddings\"" : "\"llm\"");
            IChatModel e = _engine;   // LM Studio's fields: the loaded model's state and context window
            if (e != null && string.Equals(Path.GetFileNameWithoutExtension(e.ModelPath), id, StringComparison.OrdinalIgnoreCase))
                sb.Append(",\"state\":\"loaded\",\"max_context_length\":").Append(e.ContextLength);
            else sb.Append(",\"state\":\"not-loaded\"");
            return sb.Append(",\"owned_by\":\"organization_owner\"}").ToString();
        }

        // Models shown to clients: chat models only, and only those this machine can run now. Vision projectors are part of
        // a chat model, and the embedding model is internal (docs search); /v1/embeddings still answers, with it, whatever
        // model name the request gives. A model the server cannot run (Unsupported) or that does not fit the memory free
        // now (its smallest context: VRAM not held by other programs on the GPU, RAM on the CPU; the loaded model counts as
        // free, a switch unloads it first) is left out, so /v1/models, /v1/models/{id} and the request model check all
        // treat it as unknown (404 model_not_found). Checked again at most every 3 s (other programs' VRAM use changes).
        List<string> ApiModelFiles()
        {
            lock (_fitLock)
            {
                if (_fitList != null && (DateTime.UtcNow - _fitAt).TotalSeconds < 3) return new List<string>(_fitList);
                List<string> list = new List<string>();
                List<string> hidden = new List<string>();
                long freeMem = -1, freeCommit = 0;
                try { FreeForModel(out freeMem, out freeCommit); } catch { freeMem = -1; }
                if (Directory.Exists(_modelsDir))
                    foreach (string f in Directory.GetFiles(_modelsDir, "*.gguf"))
                    {
                        if (!IsListedModel(Path.GetFileName(f))) continue;
                        string why = Unsupported(f);
                        if (why.Length == 0 && freeMem >= 0)
                            try
                            {
                                long[] need = Need(f, _useGpu);
                                if (need[0] > freeMem) why = string.Format(System.Globalization.CultureInfo.InvariantCulture, "needs {0:F1} GB of {2}, {1:F1} GB free", need[0] / 1073741824.0, freeMem / 1073741824.0, _useGpu ? "VRAM" : "RAM");
                                else if (need[1] > freeCommit) why = string.Format(System.Globalization.CultureInfo.InvariantCulture, "needs {0:F1} GB of Windows commit, {1:F1} GB free", need[1] / 1073741824.0, freeCommit / 1073741824.0);
                            }
                            catch { }
                        if (why.Length == 0) list.Add(f); else hidden.Add(Path.GetFileNameWithoutExtension(f) + " (" + why + ")");
                    }
                list.Sort(StringComparer.OrdinalIgnoreCase);
                hidden.Sort(StringComparer.OrdinalIgnoreCase);
                string note = string.Join("; ", hidden.ToArray());
                if (note != _fitNote) { _fitNote = note; Log.Text(hidden.Count == 0 ? "API models: every model fits" : "API models not offered (do not fit or cannot run): " + note); }
                _fitList = list; _fitAt = DateTime.UtcNow;
                return new List<string>(list);
            }
        }
        readonly object _fitLock = new object();
        List<string> _fitList;
        DateTime _fitAt;
        string _fitNote;

        string ModelsList()
        {
            StringBuilder sb = new StringBuilder("{\"object\":\"list\",\"data\":[");
            bool first = true;
            foreach (string f in ApiModelFiles())
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(ModelEntry(f));
            }
            return sb.Append("]}").ToString();
        }

        // GET /api/v1/models in LM Studio's native format (tools such as OpenCode's LM Studio provider ask for it):
        // {models: [{type, publisher, key, display_name, architecture, quantization, size_bytes, params_string,
        // loaded_instances: [{id, config: {context_length}}], max_context_length, format, capabilities}]}. The same chat
        // models as /v1/models; key = the /v1 model id.
        string LmStudioModels()
        {
            StringBuilder sb = new StringBuilder("{\"models\":[");
            bool first = true;
            IChatModel e = _engine;
            foreach (string f in ApiModelFiles())
            {
                string id = Path.GetFileNameWithoutExtension(f);
                bool loaded = e != null && string.Equals(Path.GetFileNameWithoutExtension(e.ModelPath), id, StringComparison.OrdinalIgnoreCase);
                System.Text.RegularExpressions.Match pm = System.Text.RegularExpressions.Regex.Match(id, @"(?i)(?:^|[-_])(e?\d+(?:\.\d+)?b)(?=[-_]|$)");
                System.Text.RegularExpressions.Match qm = System.Text.RegularExpressions.Regex.Match(id, @"(?i)(q\d_[0-9a-z]+|f16|bf16)");
                bool vision = File.Exists(Path.Combine(_modelsDir, Path.GetFileName(f).Replace(".gguf", "") + "-mmproj.gguf")) || VisionFileFor(f) != null;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"type\":\"llm\",\"publisher\":\"phoenix\",\"key\":").Append(Json.Quote(id)).Append(",\"display_name\":").Append(Json.Quote(id))
                  .Append(",\"architecture\":\"gemma4\",\"quantization\":").Append(qm.Success ? "{\"name\":" + Json.Quote(qm.Value.ToUpperInvariant()) + ",\"bits_per_weight\":" + BitsOf(qm.Value) + "}" : "null")
                  .Append(",\"size_bytes\":").Append(new FileInfo(f).Length).Append(",\"params_string\":").Append(pm.Success ? Json.Quote(pm.Groups[1].Value.ToUpperInvariant()) : "null")
                  .Append(",\"loaded_instances\":[");
                if (loaded) sb.Append("{\"id\":").Append(Json.Quote(id)).Append(",\"config\":{\"context_length\":").Append(e.ContextLength).Append("}}");
                sb.Append("],\"max_context_length\":").Append(loaded ? e.ContextLength : 131072).Append(",\"format\":\"gguf\"")
                  .Append(",\"capabilities\":{\"vision\":").Append(vision ? "true" : "false").Append(",\"trained_for_tool_use\":true}}");
            }
            return sb.Append("]}").ToString();
        }

        static string BitsOf(string quant)
        {
            string q = quant.ToLowerInvariant();
            if (q == "f16" || q == "bf16") return "16";
            return q.Length > 1 && char.IsDigit(q[1]) ? q[1].ToString() : "null";
        }

        // The vision projector next to a chat model (…-mmproj.gguf sharing its name stem), or null.
        string VisionFileFor(string modelFile)
        {
            string stem = Path.GetFileNameWithoutExtension(modelFile).ToLowerInvariant();
            int cut = stem.IndexOf("_q");
            if (cut < 0) cut = stem.IndexOf("-q");
            if (cut > 0) stem = stem.Substring(0, cut);
            foreach (string p in Directory.GetFiles(_modelsDir, "*mmproj*.gguf"))
                if (Path.GetFileName(p).ToLowerInvariant().StartsWith(stem)) return p;
            return null;
        }

        // True when models\<id>.gguf is a file in the models folder itself. id comes from the request.
        // A separator or a drive is rejected, then the canonical path must stay in that folder, so ".."
        // and an absolute path never reach the disk.
        bool ModelFileInModelsDir(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            string name = id.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? id : id + ".gguf";
            if (name.Contains("..") || name.Contains("/") || name.Contains("\\") || name.Contains(":")) return false;
            string root = _modelsDir;
            if (string.IsNullOrEmpty(root)) return false;
            if (root.Length > 3)
            {
                char tail = root[root.Length - 1];
                if (tail == Path.DirectorySeparatorChar || tail == Path.AltDirectorySeparatorChar)
                    root = root.Substring(0, root.Length - 1);
            }
            string full;
            try { full = Path.GetFullPath(Path.Combine(root, name)); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (IOException) { return false; }
            string parent = Path.GetDirectoryName(full);
            if (parent == null || !string.Equals(parent, root, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(Path.GetFileName(full), name, StringComparison.OrdinalIgnoreCase)) return false;
            string prefix = root;
            char end = root[root.Length - 1];
            if (end != Path.DirectorySeparatorChar && end != Path.AltDirectorySeparatorChar)
                prefix = root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            return File.Exists(full);
        }

        string ModelJson(string id)
        {
            foreach (string f in ApiModelFiles())
                if (string.Equals(Path.GetFileNameWithoutExtension(f), id, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(f), id, StringComparison.OrdinalIgnoreCase))
                    return ModelEntry(f);
            return null;
        }

        // The chat model the request names (file name, with or without .gguf); loads it if another one is loaded.
        // An unknown or empty name uses the loaded model, or the last used one (as LM Studio does, so clients configured
        // with any model name work).
        // Two API clients asking for different models made the server swap 13-15 GB of weights every 20-60 s (18 loads in
        // one session) until the GPU stopped responding (2026-11-04, twice). A request may swap the loaded model only when
        // the last swap is at least SwapCooldown old; before that it is answered by the model already loaded.
        static readonly TimeSpan SwapCooldown = TimeSpan.FromMinutes(2);
        DateTime _lastApiSwap = DateTime.MinValue;
        DateTime _swapNoteAt = DateTime.MinValue;
        // After a GPU reset, no load of any kind starts from the API until this time (the driver is still recovering).
        DateTime _gpuRecoverUntil = DateTime.MinValue;
        DateTime _lastDeviceLoss = DateTime.MinValue;

        void EnsureModel(string requested)
        {
            DateTime now = DateTime.UtcNow;
            if (now < _gpuRecoverUntil)
                throw new InvalidOperationException("The GPU was reset by the driver; Phoenix is waiting " +
                    Math.Ceiling((_gpuRecoverUntil - now).TotalSeconds) + " s for it to recover before loading a model. Try again shortly.");
            string file = null;
            if (!string.IsNullOrEmpty(requested) && requested.IndexOfAny(new char[] { '/', '\\', ':' }) < 0)
            {
                string f = requested.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) ? requested : requested + ".gguf";
                if (File.Exists(Path.Combine(_modelsDir, f)) && !IsEmbeddingFile(f) && !IsProjector(f)) file = f;
            }
            IChatModel e = _engine;
            if (file != null && (e == null || !string.Equals(Path.GetFileName(e.ModelPath), file, StringComparison.OrdinalIgnoreCase)))
            {
                if (e != null && now - _lastApiSwap < SwapCooldown)
                {
                    if (now - _swapNoteAt > TimeSpan.FromSeconds(30))
                    {
                        _swapNoteAt = now;
                        Log.Text("API asked for " + file + " but " + Path.GetFileName(e.ModelPath) + " was loaded " +
                            Math.Floor((now - _lastApiSwap).TotalSeconds) + " s ago; answering with the loaded model (no swap within " +
                            SwapCooldown.TotalMinutes + " min)");
                    }
                    return;
                }
                _lastApiSwap = now;
                LoadModel(file);
                return;
            }
            if (e != null) return;
            // nothing loaded and no model named: the last used one, else the first, of the models that fit now
            List<string> fit = ApiModelFiles();
            string last = Path.Combine(_modelsDir, "last-model.txt");
            if (File.Exists(last))
            {
                string n = File.ReadAllText(last).Trim();
                if (fit.Exists(delegate(string x) { return string.Equals(Path.GetFileName(x), n, StringComparison.OrdinalIgnoreCase); })) { LoadModel(n); return; }
            }
            if (fit.Count > 0) { LoadModel(Path.GetFileName(fit[0])); return; }
            throw new InvalidOperationException("No chat model in the models folder fits the memory free now.");
        }

        // OpenAI message content is a string or a list of parts ({type: "text", text}); only text is used.
        static string ContentText(object c) { return PartsText(c, "", "text"); }

        // The response_format instruction (json_object / json_schema), or null for text.
        static string FormatPrompt(Dictionary<string, object> o)
        {
            Dictionary<string, object> rf = o.ContainsKey("response_format") ? o["response_format"] as Dictionary<string, object> : null;
            string type = Json.GetStr(rf, "type", "text");
            if (type == "json_object") return "Reply with one valid JSON object only: no prose, no explanation and no Markdown code fences.";
            if (type == "json_schema")
            {
                Dictionary<string, object> js = rf.ContainsKey("json_schema") ? rf["json_schema"] as Dictionary<string, object> : null;
                object schema = js != null && js.ContainsKey("schema") ? js["schema"] : null;
                return "Reply with one valid JSON value only (no prose, no explanation, no Markdown code fences) that matches this JSON Schema" +
                       (js != null && js.ContainsKey("name") ? " (\"" + Json.GetStr(js, "name", "") + "\")" : "") + ":\n" + (schema != null ? Json.Write(schema) : "{}");
            }
            if (type != "text") throw new ArgumentException("response_format.type must be text, json_object or json_schema.");
            return null;
        }

        // A JSON reply without the code fence a model sometimes adds anyway.
        static string StripFence(string t)
        {
            string s = t.Trim();
            if (!s.StartsWith("```")) return t;
            int nl = s.IndexOf('\n');
            if (nl < 0) return t;
            s = s.Substring(nl + 1);
            int end = s.LastIndexOf("```", StringComparison.Ordinal);
            return (end >= 0 ? s.Substring(0, end) : s).Trim();
        }

        // In-process: the API's exact answer (JSON, or the event stream as text). For the console's self-test.
        string CompletionText(Dictionary<string, object> o, bool chat)
        {
            ApiOut a = new ApiOut(); a.Buffer = new MemoryStream();
            Completion(o, a, chat, "console");
            return Encoding.UTF8.GetString(a.Buffer.ToArray());
        }

        void Completion(Dictionary<string, object> o, ApiOut res, bool chat, string source)
        {
            if (o == null) throw new ArgumentException("JSON body required");
            List<ChatMessage> msgs = new List<ChatMessage>();
            string toolPrompt = chat ? ApiTools.Prompt(o) : null;
            HashSet<string> toolNames = toolPrompt != null ? ApiTools.Names(o) : null;
            if (toolNames != null)
            {
                string list = string.Join(", ", new List<string>(toolNames).ToArray());
                if (list != _lastToolList) { _lastToolList = list; Log.Text("API tools offered: " + list); }
            }
            string formatPrompt = chat ? FormatPrompt(o) : null;
            // tool_choice "required" or {"type": "function", "function": {"name": ...}}: the reply must be a tool call
            object toolChoice = o.ContainsKey("tool_choice") ? o["tool_choice"] : null;
            Dictionary<string, object> tcd = toolChoice as Dictionary<string, object>;
            string forcedTool = tcd != null && tcd.ContainsKey("function") ? Json.GetStr(tcd["function"] as Dictionary<string, object>, "name", null) : null;
            bool mustCall = toolNames != null && (forcedTool != null || (toolChoice is string && (string)toolChoice == "required"));
            Dictionary<string, string> callNames = new Dictionary<string, string>();
            bool fim = !chat && o.ContainsKey("suffix");
            string fimPrefix = null, fimSuffix = null;
            if (chat)
            {
                List<object> list = o.ContainsKey("messages") ? o["messages"] as List<object> : null;
                if (list == null) throw new ArgumentException("Missing required parameter: 'messages'.");
                if (list.Count == 0) throw new ArgumentException("Invalid 'messages': empty array. Expected an array with minimum length 1.");
                foreach (object m in list)
                {
                    Dictionary<string, object> mm = m as Dictionary<string, object>;
                    if (mm == null) continue;
                    string role = Json.GetStr(mm, "role", "user"), text = ContentText(mm.ContainsKey("content") ? mm["content"] : null);
                    if (role == "developer") role = "system";
                    else if (role == "tool" || role == "function") { role = "user"; text = ApiTools.ResultText(mm, text, callNames); }
                    else if (role == "assistant")
                    {
                        List<object> tc = mm.ContainsKey("tool_calls") ? mm["tool_calls"] as List<object> : null;
                        if (tc != null)
                            foreach (object c in tc)
                            {
                                Dictionary<string, object> cd = c as Dictionary<string, object>;
                                Dictionary<string, object> fd = cd != null && cd.ContainsKey("function") ? cd["function"] as Dictionary<string, object> : null;
                                string cid = Json.GetStr(cd, "id", null), cname = Json.GetStr(fd, "name", null);
                                if (cid != null && cname != null) callNames[cid] = cname;
                            }
                        text = ApiTools.AssistantText(mm, text);
                    }
                    if (role != "system" && role != "user" && role != "assistant") continue;
                    // The templates expect one leading system message: later ones are merged into it.
                    int si = msgs.FindIndex(delegate(ChatMessage x) { return x.Role == "system"; });
                    if (role == "system" && si >= 0) { msgs[si] = new ChatMessage("system", msgs[si].Text + "\n\n" + text); continue; }
                    if (role == "system") { msgs.Insert(0, new ChatMessage("system", text)); continue; }
                    // Templates expect user / assistant turns to alternate (several tool results in a row are one turn).
                    List<byte[]> imgs = role == "user" ? ParseImages(mm) : null;   // inline data: images only
                    if (msgs.Count > 0 && msgs[msgs.Count - 1].Role == role)
                    {
                        ChatMessage prev = msgs[msgs.Count - 1];
                        ChatMessage merged = new ChatMessage(role, prev.Text + "\n\n" + text);
                        merged.Images = prev.Images;
                        if (imgs != null) { if (merged.Images == null) merged.Images = imgs; else merged.Images.AddRange(imgs); }
                        msgs[msgs.Count - 1] = merged;
                        continue;
                    }
                    ChatMessage cm = new ChatMessage(role, text);
                    cm.Images = imgs;
                    msgs.Add(cm);
                }
                foreach (string extra in new string[] { toolPrompt, formatPrompt })
                {
                    if (extra == null) continue;
                    int si = msgs.FindIndex(delegate(ChatMessage x) { return x.Role == "system"; });
                    if (si >= 0) msgs[si] = new ChatMessage("system", msgs[si].Text + "\n\n" + extra);
                    else msgs.Insert(0, new ChatMessage("system", extra));
                }
            }
            else if (fim)
            {
                object p = o["prompt"];
                List<object> pl = p as List<object>;
                fimPrefix = pl != null && pl.Count > 0 ? ContentText(pl[0]) : ContentText(p);
                fimSuffix = Json.GetStr(o, "suffix", "");
            }
            else
            {
                if (!o.ContainsKey("prompt")) throw new ArgumentException("Missing required parameter: 'prompt'.");
                object p = o["prompt"];
                List<object> pl = p as List<object>;
                msgs.Add(new ChatMessage("user", pl != null && pl.Count > 0 ? ContentText(pl[0]) : ContentText(p)));
            }
            if (!fim && !msgs.Exists(delegate(ChatMessage x) { return x.Role == "user"; })) throw new ArgumentException("The messages must contain at least one user message.");

            // Values the request sends win; what it leaves out comes from the answering model's server settings
            // (ModelSettings.FillRequest, once the model is known), which default to Google's Gemma 4 sampling.
            SamplerSettings s = new SamplerSettings();
            s.Temperature = (float)Json.GetNum(o, "temperature", ModelSettings.GemmaTemperature);
            s.TopK = (int)Json.GetNum(o, "top_k", ModelSettings.GemmaTopK);
            s.TopP = (float)Json.GetNum(o, "top_p", ModelSettings.GemmaTopP);
            s.RepeatPenalty = (float)Json.GetNum(o, "repeat_penalty", 1.0);
            if (s.Temperature < 0 || s.Temperature > 2) throw new ArgumentException("temperature must be between 0 and 2.");
            if (s.TopP <= 0 || s.TopP > 1) throw new ArgumentException("top_p must be greater than 0 and at most 1.");
            double fp = Json.GetNum(o, "frequency_penalty", 0), pp = Json.GetNum(o, "presence_penalty", 0);
            if (fp < -2 || fp > 2 || pp < -2 || pp > 2) throw new ArgumentException("frequency_penalty and presence_penalty must be between -2 and 2.");
            if (!o.ContainsKey("repeat_penalty") && (fp > 0 || pp > 0)) s.RepeatPenalty = 1f + (float)Math.Max(fp, pp) * 0.5f;
            int seed = -1;
            if (o.ContainsKey("seed") && o["seed"] is double) seed = (int)(double)o["seed"];
            int maxTokens = (int)Json.GetNum(o, "max_completion_tokens", Json.GetNum(o, "max_tokens", -1));
            if (o.ContainsKey("max_tokens") && o["max_tokens"] is double && (double)o["max_tokens"] < 1 || o.ContainsKey("max_completion_tokens") && o["max_completion_tokens"] is double && (double)o["max_completion_tokens"] < 1)
                throw new ArgumentException("max_tokens must be at least 1.");
            if (maxTokens <= 0) maxTokens = int.MaxValue;   // OpenAI default: until the context is full
            int nChoices = (int)Json.GetNum(o, "n", 1);
            if (nChoices < 1 || nChoices > 8) throw new ArgumentException("n must be between 1 and 8.");
            if (fim) nChoices = 1;
            bool stream = o.ContainsKey("stream") && o["stream"] is bool && (bool)o["stream"];
            bool usageInStream = false;
            Dictionary<string, object> so = o.ContainsKey("stream_options") ? o["stream_options"] as Dictionary<string, object> : null;
            if (so != null && so.ContainsKey("include_usage") && so["include_usage"] is bool) usageInStream = (bool)so["include_usage"];
            List<string> stops = new List<string>();
            object stopObj = o.ContainsKey("stop") ? o["stop"] : null;
            if (stopObj is string && ((string)stopObj).Length > 0) stops.Add((string)stopObj);
            List<object> stopList = stopObj as List<object>;
            if (stopList != null) foreach (object x in stopList) if (x is string && ((string)x).Length > 0) stops.Add((string)x);
            if (stops.Count > 4) throw new ArgumentException("At most 4 stop sequences.");
            int maxStop = 0;
            foreach (string x in stops) maxStop = Math.Max(maxStop, x.Length);

            // logprobs: chat takes logprobs true + top_logprobs 0-20; /completions takes logprobs as the number of alternatives
            int lpTop = -1;
            if (chat)
            {
                bool want = o.ContainsKey("logprobs") && o["logprobs"] is bool && (bool)o["logprobs"];
                if (o.ContainsKey("top_logprobs") && o["top_logprobs"] is double)
                {
                    if (!want) throw new ArgumentException("top_logprobs requires logprobs to be true.");
                    lpTop = (int)(double)o["top_logprobs"];
                    if (lpTop < 0 || lpTop > 20) throw new ArgumentException("top_logprobs must be between 0 and 20.");
                }
                else if (want) lpTop = 0;
            }
            else if (o.ContainsKey("logprobs") && o["logprobs"] is double)
            {
                lpTop = (int)(double)o["logprobs"];
                if (lpTop < 0 || lpTop > 20) throw new ArgumentException("logprobs must be between 0 and 20.");
            }

            // An unknown model name is refused, as OpenAI does; no name (or "phoenixlai", the VS Code extension's) = the loaded model.
            string askedModel = Json.GetStr(o, "model", null);
            if (!string.IsNullOrEmpty(askedModel) && !string.Equals(askedModel, "phoenixlai", StringComparison.OrdinalIgnoreCase) && ModelJson(askedModel) == null)
                throw new ApiModelNotFound(askedModel, ModelFileInModelsDir(askedModel));

            string id = (chat ? "chatcmpl-" : "cmpl-") + Guid.NewGuid().ToString("N").Substring(0, 24);
            long created = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            string obj = chat ? "chat.completion" : "text_completion";
            int rid = Interlocked.Increment(ref _requestId);
            Log.Text("#" + rid + " API " + (chat ? "chat" : fim ? "code completion" : "completion") + " from " + source + " (" + (fim ? fimPrefix.Length + "+" + fimSuffix.Length + " chars" : msgs.Count + " messages") +
                (toolNames != null ? ", " + toolNames.Count + " tools" : "") + (nChoices > 1 ? ", n " + nChoices : "") + ", stream " + stream + ")");

            // A stream answers at once: headers and an SSE comment now, then one every 2 s while the request waits its turn or
            // its model loads. Silence there made clients give up (OpenCode dropped a queued request, 2026-10-20).
            Stream early = stream ? res.BeginStream() : null;
            object wl = new object();
            bool waiting = early != null, earlyGone = false;
            System.Threading.Timer keepAlive = null;
            if (early != null)
            {
                try { byte[] b = Encoding.UTF8.GetBytes(": queued\n\n"); early.Write(b, 0, b.Length); early.Flush(); } catch { earlyGone = true; }
                keepAlive = new System.Threading.Timer(delegate
                {
                    lock (wl)
                    {
                        if (!waiting || earlyGone) return;
                        try { byte[] b = Encoding.UTF8.GetBytes(": waiting\n\n"); early.Write(b, 0, b.Length); early.Flush(); } catch { earlyGone = true; }
                    }
                }, null, 2000, 2000);
            }
            try
            {
            lock (_engineLock)
            {
                if (earlyGone) { Log.Text("#" + rid + " API client hung up while queued"); return; }
                EnsureModel(Json.GetStr(o, "model", null));
                lock (wl) waiting = false;   // from here on only this request writes to the stream
                ChatEngine eng = (ChatEngine)_engine;   // the API runs in the server, on the local engine
                ModelSettings.FillRequest(s, o, eng.ModelPath, ref maxTokens);
                eng.PrefillProgress = null;
                string modelId = Path.GetFileNameWithoutExtension(eng.ModelPath);
                Stream outS = early;
                bool gone = false;
                Action<string> sse = delegate(string json)
                {
                    if (gone) return;
                    try { byte[] b = Encoding.UTF8.GetBytes("data: " + json + "\n\n"); outS.Write(b, 0, b.Length); outS.Flush(); }
                    catch { gone = true; }
                };
                string head = "{\"id\":\"" + id + "\",\"object\":\"" + (chat ? "chat.completion.chunk" : "text_completion") + "\",\"created\":" + created +
                              ",\"model\":" + Json.Quote(modelId) + ",\"system_fingerprint\":\"phoenix\",\"choices\":[";
                int promptTotal = 0, genTotal = 0, cachedTotal = 0;
                StringBuilder choicesJson = new StringBuilder();
                ActivityStart(rid, source, chat ? "chat" : fim ? "code completion" : "completion");
                GenerationStats last = null; string lastFinish = null;
                try
                {
                    for (int ci = 0; ci < nChoices && !gone; ci++)
                    {
                        int choiceIdx = ci;
                        if (seed >= 0) s.Seed = seed + ci;
                        bool stopped = false;
                        StringBuilder text = new StringBuilder();
                        int sent = 0;   // characters of this choice's text already streamed
                        bool roleSent = false;
                        List<TokenLogprob> lps = lpTop >= 0 ? new List<TokenLogprob>() : null;
                        int lpSent = 0, lpChars = 0;   // tokens (and their characters) already sent in stream chunks
                        Func<string, string, string> chunk = delegate(string delta, string finish)
                        {
                            string lpJson = "null";
                            if (lps != null && delta != null)
                            {
                                // the tokens that make up this delta (a stream holds back text that may start a stop sequence)
                                int to = lpSent, chars = lpChars;
                                while (to < lps.Count && chars < sent + delta.Length) { chars += lps[to].Text.Length; to++; }
                                lpJson = LogprobsJson(lps, lpSent, to, chat, lpTop, lpChars);
                                lpSent = to; lpChars = chars;
                            }
                            string choice;
                            if (chat)
                            {
                                string d = "{}";
                                if (delta != null) { d = (!roleSent ? "{\"role\":\"assistant\",\"content\":" : "{\"content\":") + Json.Quote(delta) + "}"; roleSent = true; }
                                else if (!roleSent && finish == null) { d = "{\"role\":\"assistant\",\"content\":\"\"}"; roleSent = true; }
                                choice = "{\"index\":" + choiceIdx + ",\"delta\":" + d;
                            }
                            else choice = "{\"index\":" + choiceIdx + ",\"text\":" + Json.Quote(delta ?? "");
                            choice += ",\"logprobs\":" + lpJson + ",\"finish_reason\":" + (finish == null ? "null" : Json.Quote(finish)) + "}";
                            return head + choice + "]}";
                        };
                        if (stream && chat) sse(chunk(null, null));   // the role first, as OpenAI does
                        // Streams what is certain not to be the start of a stop sequence.
                        Action flush = delegate()
                        {
                            int safe = stopped ? text.Length : Math.Max(sent, text.Length - Math.Max(0, maxStop - 1));
                            if (toolNames != null && !stopped) safe = Math.Max(sent, Math.Min(safe, ApiTools.SafeLength(text)));   // a tool call is never streamed as text
                            if (mustCall && !stopped) safe = sent;   // a required call: nothing streams until the call is there
                            if (stream && safe > sent) { string d = text.ToString(sent, safe - sent); sse(chunk(d, null)); sent = safe; }
                        };
                        TextCallback onPiece = delegate(string piece)
                        {
                            if (stopped) return;
                            text.Append(piece);
                            foreach (string x in stops)
                            {
                                int at = text.ToString().IndexOf(x, Math.Max(0, text.Length - piece.Length - x.Length), StringComparison.Ordinal);
                                if (at >= 0) { text.Length = at; stopped = true; break; }
                            }
                            flush();
                        };
                        bool hitStop = false;
                        CancelCheck cancel = delegate { if (stopped) hitStop = true; return gone || stopped; };
                        // While a long prompt is read: log its size once, and on a stream send an SSE comment after each chunk.
                        // A client that has hung up (VS Code's Stop) then makes the write fail, which cancels the prefill.
                        bool logged = false;
                        eng.PrefillProgress = delegate(int done, int total)
                        {
                            if (!logged && total >= 2048) { logged = true; Log.Text("#" + rid + " API reading " + total + " prompt tokens"); }
                            if (stream && !gone) { try { byte[] b = Encoding.UTF8.GetBytes(": reading " + done + "/" + total + "\n\n"); outS.Write(b, 0, b.Length); outS.Flush(); } catch { gone = true; } }
                        };
                        GenerationStats st;
                        eng.Logprobs = lps; eng.LogprobTop = Math.Max(0, lpTop);
                        try { st = fim ? eng.GenerateFim(fimPrefix, fimSuffix, s, maxTokens, onPiece, cancel) : eng.Generate(msgs, s, maxTokens, onPiece, cancel); }
                        finally { eng.PrefillProgress = null; eng.Logprobs = null; }
                        last = st;
                        if (gone) Log.Text("#" + rid + " API client hung up (" + st.StopReason + ")");
                        List<ApiToolCall> calls = null;
                        if (toolNames != null)
                        {
                            string content;
                            calls = ApiTools.Parse(text.ToString(), toolNames, out content);
                            // tool_choice "required" or a named function: OpenAI guarantees a call. A model that answered in
                            // text instead is asked once more, plainly (small models skip the instruction on small talk).
                            if (calls.Count == 0 && mustCall && !gone)
                            {
                                List<ChatMessage> again = new List<ChatMessage>(msgs);
                                again.Add(new ChatMessage("assistant", text.ToString()));
                                again.Add(new ChatMessage("user", "Call the tool " + (forcedTool != null ? "\"" + forcedTool + "\" " : "") + "now: reply with only the " +
                                    ApiTools.Open + "{\"name\": ..., \"arguments\": {...}}" + ApiTools.Close + " block, nothing else."));
                                Log.Text("#" + rid + " API tool call required but none came; asking again");
                                text.Length = 0; stopped = false; hitStop = false;
                                if (lps != null) { lps.Clear(); lpSent = 0; lpChars = 0; }
                                eng.Logprobs = lps;
                                GenerationStats st2;
                                try { st2 = eng.Generate(again, s, maxTokens, onPiece, cancel); }
                                finally { eng.Logprobs = null; }
                                st.GeneratedTokens += st2.GeneratedTokens; st.GenerateSeconds += st2.GenerateSeconds;
                                calls = ApiTools.Parse(text.ToString(), toolNames, out content);
                            }
                            if (calls.Count > 0) { text.Length = 0; text.Append(content); if (sent > text.Length) sent = text.Length; }
                            else calls = null;
                        }
                        if (formatPrompt != null && !stream) { string t = StripFence(text.ToString()); text.Length = 0; text.Append(t); }
                        stopped = true; flush();
                        string finishReason = calls != null ? "tool_calls" : !hitStop && (st.StopReason == "max tokens" || st.StopReason == "context full") ? "length" : "stop";
                        lastFinish = finishReason;
                        promptTotal = st.PromptTokens + st.ReusedTokens; cachedTotal = st.ReusedTokens; genTotal += st.GeneratedTokens;
                        Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0} API done{6}: {1} prompt ({2} cached), {3} generated at {4:F1} tok/s, {5}",
                            rid, promptTotal, st.ReusedTokens, st.GeneratedTokens, st.TokensPerSecond, finishReason, nChoices > 1 ? " (choice " + ci + ")" : ""));
                        if (calls != null) Log.Text("#" + rid + " API tool calls: " + string.Join(", ", calls.ConvertAll(delegate(ApiToolCall c) { return c.Name; }).ToArray()));
                        if (stream)
                        {
                            if (calls != null)
                                sse(head + "{\"index\":" + ci + ",\"delta\":{" + (!roleSent ? "\"role\":\"assistant\"," : "") +
                                    "\"tool_calls\":" + ApiTools.CallsJson(calls, true) + "},\"logprobs\":null,\"finish_reason\":null}]}");
                            sse(chunk(null, finishReason));
                            continue;
                        }
                        string lpAll = "null";
                        if (lps != null)
                        {
                            // only the tokens of the text returned (a stop sequence cuts it short)
                            int to = 0, chars = 0;
                            while (to < lps.Count && chars < text.Length) { chars += lps[to].Text.Length; to++; }
                            lpAll = LogprobsJson(lps, 0, to, chat, lpTop, 0);
                        }
                        if (ci > 0) choicesJson.Append(',');
                        choicesJson.Append(chat
                            ? "{\"index\":" + ci + ",\"message\":{\"role\":\"assistant\",\"content\":" + (calls != null && text.Length == 0 ? "null" : Json.Quote(text.ToString())) +
                              (calls != null ? ",\"tool_calls\":" + ApiTools.CallsJson(calls, false) : "") + ",\"refusal\":null},\"logprobs\":" + lpAll + ",\"finish_reason\":\"" + finishReason + "\"}"
                            : "{\"index\":" + ci + ",\"text\":" + Json.Quote(text.ToString()) + ",\"logprobs\":" + lpAll + ",\"finish_reason\":\"" + finishReason + "\"}");
                    }
                }
                finally { ActivityEnd(rid, source, chat ? "chat" : fim ? "fim" : "completion", last, lastFinish); }
                string usage = "{\"prompt_tokens\":" + promptTotal + ",\"completion_tokens\":" + genTotal + ",\"total_tokens\":" + (promptTotal + genTotal) +
                               ",\"prompt_tokens_details\":{\"cached_tokens\":" + cachedTotal + "}}";
                if (stream)
                {
                    if (usageInStream && !gone) sse(head + "],\"usage\":" + usage + "}");
                    if (!gone) { try { byte[] b = Encoding.UTF8.GetBytes("data: [DONE]\n\n"); outS.Write(b, 0, b.Length); } catch { } }
                    res.End();
                    return;
                }
                res.Json(200, "{\"id\":\"" + id + "\",\"object\":\"" + obj + "\",\"created\":" + created + ",\"model\":" + Json.Quote(modelId) +
                    ",\"system_fingerprint\":\"phoenix\",\"choices\":[" + choicesJson + "],\"usage\":" + usage + "}");
            }
            }
            catch (Exception ex)
            {
                if (ex is GpuDeviceLostException) RecoverFromDeviceLoss(rid);
                if (early == null) throw;   // not streaming yet: the caller answers with an HTTP error
                // headers are already sent: the error goes into the stream, as OpenAI does
                lock (wl) waiting = false;
                Log.Text("#" + rid + " API error in stream: " + ex.Message);
                try
                {
                    byte[] b = Encoding.UTF8.GetBytes("data: " + ErrorJson(ex.Message, ex is ArgumentException ? "invalid_request_error" : "server_error", null, null) + "\n\ndata: [DONE]\n\n");
                    early.Write(b, 0, b.Length); early.Flush();
                }
                catch { }
                res.End();
            }
            finally { if (keepAlive != null) keepAlive.Dispose(); }
        }

        // OpenAI's logprobs object for tokens [from, to): chat {"content": [{token, logprob, bytes, top_logprobs}]}, or the
        // legacy /completions shape {tokens, token_logprobs, top_logprobs, text_offset}. offset: characters before 'from'.
        static string LogprobsJson(List<TokenLogprob> l, int from, int to, bool chat, int top, int offset)
        {
            StringBuilder sb = new StringBuilder();
            if (chat)
            {
                sb.Append("{\"content\":[");
                for (int i = from; i < to; i++)
                {
                    if (i > from) sb.Append(',');
                    AppendToken(sb, l[i]);
                    sb.Length--;   // reopen the object for its top_logprobs
                    sb.Append(",\"top_logprobs\":[");
                    for (int j = 0; j < l[i].Top.Count && j < top; j++) { if (j > 0) sb.Append(','); AppendToken(sb, l[i].Top[j]); }
                    sb.Append("]}");
                }
                return sb.Append("],\"refusal\":null}").ToString();
            }
            StringBuilder toks = new StringBuilder(), lps = new StringBuilder(), tops = new StringBuilder(), offs = new StringBuilder();
            for (int i = from; i < to; i++)
            {
                string sep = i > from ? "," : "";
                toks.Append(sep).Append(Json.Quote(l[i].Text));
                lps.Append(sep).Append(Json.Num(l[i].Logprob));
                offs.Append(sep).Append(offset);
                offset += l[i].Text.Length;
                tops.Append(sep).Append('{');
                HashSet<string> seen = new HashSet<string>();
                for (int j = 0; j < l[i].Top.Count && j < top; j++)
                {
                    if (!seen.Add(l[i].Top[j].Text)) continue;
                    if (seen.Count > 1) tops.Append(',');
                    tops.Append(Json.Quote(l[i].Top[j].Text)).Append(':').Append(Json.Num(l[i].Top[j].Logprob));
                }
                tops.Append('}');
            }
            return "{\"tokens\":[" + toks + "],\"token_logprobs\":[" + lps + "],\"top_logprobs\":" + (top > 0 ? "[" + tops + "]" : "null") + ",\"text_offset\":[" + offs + "]}";
        }

        static void AppendToken(StringBuilder sb, TokenLogprob t)
        {
            sb.Append("{\"token\":").Append(Json.Quote(t.Text)).Append(",\"logprob\":").Append(Json.Num(t.Logprob)).Append(",\"bytes\":[");
            for (int i = 0; i < t.Bytes.Length; i++) { if (i > 0) sb.Append(','); sb.Append(t.Bytes[i]); }
            sb.Append("]}");
        }

        // The driver reset the GPU (a too-long job, or another program): the engine's device is gone for good. Drop it and
        // load the same model on a fresh device in the background; requests meanwhile get "loading" errors, not a dead server.
        void RecoverFromDeviceLoss(int rid)
        {
            // Reloading at once hit the driver while it was still recovering: the reload failed or fell back to the CPU
            // (284 s) and a second reset followed (2026-11-04). Now: wait RecoverDelay, try once, and after a second reset
            // within RepeatWindow do not reload at all (load it again by hand from the console or dashboard).
            TimeSpan RecoverDelay = TimeSpan.FromSeconds(20), RepeatWindow = TimeSpan.FromMinutes(10);
            string file = null;
            bool repeat;
            lock (_engineLock)
            {
                IChatModel e = _engine;
                if (e == null) return;
                file = e.ModelPath;
                try { e.Dispose(); } catch { }
                _engine = null;
                _loadedSig = null;
                DateTime now = DateTime.UtcNow;
                repeat = now - _lastDeviceLoss < RepeatWindow;
                _lastDeviceLoss = now;
                _gpuRecoverUntil = now + RecoverDelay;
                _loadError = repeat ? "GPU reset twice within " + RepeatWindow.TotalMinutes + " min; not reloading. Load a model by hand when ready."
                                    : "GPU reset by the driver; reloading in " + RecoverDelay.TotalSeconds + " s";
            }
            if (repeat)
            {
                // keep the API from loading anything by itself until someone loads a model by hand
                _gpuRecoverUntil = DateTime.MaxValue;
                Log.Text("#" + rid + " GPU device lost again within " + RepeatWindow.TotalMinutes + " min; NOT reloading " + Path.GetFileName(file) +
                    ". Load a model from the console or dashboard when ready.");
                return;
            }
            Log.Text("#" + rid + " GPU device lost; reloading " + Path.GetFileName(file) + " in " + RecoverDelay.TotalSeconds + " s");
            string f = file;
            int delayMs = (int)RecoverDelay.TotalMilliseconds;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(delayMs);
                try { LoadModel(f); } catch (Exception ex) { Log.Text("reload after GPU reset FAILED: " + ex.Message + " (not retrying)"); }
            });
        }

        void Embeddings(Dictionary<string, object> o, HttpListenerResponse res)
        {
            object input = o != null && o.ContainsKey("input") ? o["input"] : null;
            List<string> texts = new List<string>();
            if (input is string) texts.Add((string)input);
            List<object> il = input as List<object>;
            if (il != null) foreach (object x in il) if (x is string) texts.Add((string)x);
            if (texts.Count == 0) throw new ArgumentException("input (string or array of strings) required");
            if (texts.Count > 2048) throw new ArgumentException("At most 2048 inputs per request.");
            string fmt = Json.GetStr(o, "encoding_format", "float");
            if (fmt != "float" && fmt != "base64") throw new ArgumentException("encoding_format must be float or base64.");
            int dims = (int)Json.GetNum(o, "dimensions", 0);
            StringBuilder sb = new StringBuilder("{\"object\":\"list\",\"data\":[");
            string modelId = null;
            int tokens = 0;
            int rid = Interlocked.Increment(ref _requestId);
            ActivityStart(rid, "api", "embeddings");
            try
            {
                for (int i = 0; i < texts.Count; i++)
                {
                    float[] v = Rag.EmbedRaw(texts[i], out modelId);
                    if (dims > 0 && dims < v.Length)
                    {
                        // Matryoshka truncation (EmbeddingGemma is trained for it), then unit length again
                        float[] t = new float[dims]; double n2 = 0;
                        for (int j = 0; j < dims; j++) { t[j] = v[j]; n2 += v[j] * v[j]; }
                        float inv = n2 > 0 ? (float)(1.0 / Math.Sqrt(n2)) : 0;
                        for (int j = 0; j < dims; j++) t[j] *= inv;
                        v = t;
                    }
                    tokens += Math.Max(1, texts[i].Length / 4);
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"object\":\"embedding\",\"index\":").Append(i).Append(",\"embedding\":");
                    if (fmt == "base64")
                    {
                        byte[] b = new byte[v.Length * 4];
                        Buffer.BlockCopy(v, 0, b, 0, b.Length);   // little-endian float32, as OpenAI sends it
                        sb.Append(Json.Quote(Convert.ToBase64String(b)));
                    }
                    else
                    {
                        sb.Append('[');
                        for (int j = 0; j < v.Length; j++) { if (j > 0) sb.Append(','); sb.Append(v[j].ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                        sb.Append(']');
                    }
                    sb.Append('}');
                }
            }
            finally { ActivityEnd(rid, "api", "embeddings", null, "stop"); }
            // the name the client asked for (the embedding model itself is internal and never shown)
            string asked = Json.GetStr(o, "model", "");
            if (asked.Length == 0) { IChatModel ce = _engine; asked = ce != null ? Path.GetFileNameWithoutExtension(ce.ModelPath) : "phoenix"; }
            sb.Append("],\"model\":").Append(Json.Quote(asked)).Append(",\"usage\":{\"prompt_tokens\":").Append(tokens).Append(",\"total_tokens\":").Append(tokens).Append("}}");
            SendJson(res, 200, sb.ToString());
        }
    }
}
