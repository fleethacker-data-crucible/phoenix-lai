// Loopback-only chat server: serves html\, streams chat, switches models, and lets the model work in a local folder.
// C# 5 only. The ONLY file allowed to use networking or start a process (enforced by run.ps1): it listens on
// 127.0.0.1 only, never makes outgoing connections, and only starts msedge.exe.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Phoenix
{
    public sealed class WebServer : IDisposable
    {
        const int MaxToolSteps = 6;

        readonly string _htmlDir, _modelsDir;
        readonly HttpListener _listener = new HttpListener();
        readonly object _engineLock = new object();     // one generation / model load at a time
        ChatEngine _engine;
        Workspace _workspace;
        string _loading;
        public int ContextLength = 4096;
        public readonly int Port;
        public readonly ChatLog Log;
        public readonly Rag Rag;
        int _requestId;

        public WebServer(string htmlDir, string modelsDir, string logsDir, int port)
        {
            _htmlDir = Path.GetFullPath(htmlDir);
            _modelsDir = Path.GetFullPath(modelsDir);
            Port = port;
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            _listener.Start();
            Log = new ChatLog(logsDir);
            Rag = new Rag(Path.Combine(Directory.GetParent(_htmlDir).FullName, "docs"));
            Log.Text("server started on " + Url);
            Log.Json("server_start", "\"url\":" + Json.Quote(Url));
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        public string Url { get { return "http://127.0.0.1:" + Port + "/chat.html"; } }

        // Starts the server (optionally loading a model), opens Edge in app mode, runs until that window closes.
        public static int Run(string root, string modelPath, int ctx)
        {
            Application.EnableVisualStyles();
            using (WebServer server = StartOnFreePort(Path.Combine(root, "html"), Path.Combine(root, "models"), Path.Combine(root, "logs")))
            {
                server.ContextLength = ctx;
                if (!string.IsNullOrEmpty(modelPath))
                {
                    string m = modelPath;
                    ThreadPool.QueueUserWorkItem(delegate { try { server.LoadModel(m); } catch { } });
                }
                string edge = FindEdge();
                if (edge != null)
                {
                    // A private profile folder gives a separate Edge process, so we know when the app window closes.
                    string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLLMStudio", "edge-profile");
                    Directory.CreateDirectory(profile);
                    ProcessStartInfo psi = new ProcessStartInfo(edge,
                        "--app=" + server.Url + " --user-data-dir=\"" + profile + "\" --no-first-run --window-size=1100,800");
                    psi.UseShellExecute = false;
                    using (Process p = Process.Start(psi)) p.WaitForExit();
                }
                else
                {
                    MessageBox.Show("Microsoft Edge was not found. Open this address in any browser:\n\n" + server.Url +
                        "\n\nClose this box to stop the server.", "Local LLM Studio");
                }
                lock (server._engineLock) { if (server._engine != null) server._engine.Dispose(); server._engine = null; }
                server.Log.Text("server stopped");
                server.Log.Json("server_stop", null);
            }
            return 0;
        }

        static WebServer StartOnFreePort(string htmlDir, string modelsDir, string logsDir)
        {
            for (int port = 8765; port < 8800; port++)
            {
                try { return new WebServer(htmlDir, modelsDir, logsDir, port); }
                catch (HttpListenerException) { }
            }
            throw new InvalidOperationException("No free local port between 8765 and 8799.");
        }

        static string FindEdge()
        {
            string[] roots = { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"),
                               Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            foreach (string r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                string p = Path.Combine(r, "Microsoft", "Edge", "Application", "msedge.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // Loads a model from models\ (by file name) or a full local path; replaces the current one.
        public void LoadModel(string nameOrPath)
        {
            string path = Path.IsPathRooted(nameOrPath) ? nameOrPath : Path.Combine(_modelsDir, nameOrPath);
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new FileNotFoundException("Model not found: " + nameOrPath);
            lock (_engineLock)
            {
                _loading = Path.GetFileName(path);
                Stopwatch sw = Stopwatch.StartNew();
                Log.Text("loading model " + _loading + " (context " + ContextLength + ")");
                try
                {
                    if (_engine != null) { _engine.Dispose(); _engine = null; }
                    _engine = ChatEngine.Load(path, ContextLength, true, delegate(string m) { Log.Text("  " + m); });
                    Log.Text("loaded " + _engine.ModelName + " on " + _engine.Backend + " in " + ChatLog.Ms(sw.Elapsed.TotalSeconds) + " ms");
                    // Remember it so the next start opens the same model (only for models inside models\).
                    if (string.Equals(Path.GetDirectoryName(path), _modelsDir, StringComparison.OrdinalIgnoreCase))
                        try { File.WriteAllText(Path.Combine(_modelsDir, "last-model.txt"), Path.GetFileName(path)); } catch { }
                    Log.Json("model_load", "\"file\":" + Json.Quote(_loading) + ",\"model\":" + Json.Quote(_engine.ModelName) + ",\"backend\":" +
                        Json.Quote(_engine.Backend) + ",\"context\":" + ContextLength + ",\"ms\":" + ChatLog.Ms(sw.Elapsed.TotalSeconds));
                }
                catch (Exception ex)
                {
                    Log.Text("model load FAILED: " + ex.Message);
                    Log.Json("model_load_error", "\"file\":" + Json.Quote(_loading) + ",\"error\":" + Json.Quote(ex.Message));
                    throw;
                }
                finally { _loading = null; }
            }
        }

        void Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(delegate { Handle(ctx); });
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            try
            {
                // Only same-machine pages may talk to us (blocks DNS-rebinding and other sites' scripts).
                string host = req.Headers["Host"] ?? "";
                if (host != "127.0.0.1:" + Port) { Send(res, 403, "text/plain", "Forbidden"); return; }
                string origin = req.Headers["Origin"];
                if (origin != null && origin != "http://127.0.0.1:" + Port) { Send(res, 403, "text/plain", "Forbidden"); return; }

                string path = req.Url.AbsolutePath;
                if (path.StartsWith("/api/"))
                {
                    try { Api(req, res, path); }
                    catch (Exception ex)
                    {
                        Log.Text("API " + path + " error: " + ex.Message);
                        Log.Json("api_error", "\"path\":" + Json.Quote(path) + ",\"error\":" + Json.Quote(ex.Message));
                        SendJson(res, 400, "{\"error\":" + Json.Quote(ex.Message) + "}");
                    }
                    return;
                }
                if (req.HttpMethod == "GET") { ServeFile(res, path == "/" ? "/chat.html" : path); return; }
                Send(res, 405, "text/plain", "Method not allowed");
            }
            catch (Exception ex)
            {
                try { Send(res, 500, "text/plain", ex.Message); } catch { }
            }
        }

        void Api(HttpListenerRequest req, HttpListenerResponse res, string path)
        {
            bool post = req.HttpMethod == "POST";
            Dictionary<string, object> body = post ? ReadJson(req) : null;
            string q = req.QueryString["path"];

            if (path == "/api/info") { SendJson(res, 200, Info()); return; }
            if (path == "/api/models")
            {
                StringBuilder sb = new StringBuilder("{\"models\":[");
                bool first = true;
                if (Directory.Exists(_modelsDir))
                {
                    List<string> files = new List<string>(Directory.GetFiles(_modelsDir, "*.gguf"));
                    files.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"name\":").Append(Json.Quote(Path.GetFileName(f))).Append(",\"mb\":").Append(new FileInfo(f).Length / 1048576).Append('}');
                    }
                }
                SendJson(res, 200, sb.Append("]}").ToString());
                return;
            }
            if (path == "/api/load" && post)
            {
                string name = Json.GetStr(body, "name", null);
                if (name == null || name.IndexOfAny(new char[] { '/', '\\', ':' }) >= 0) throw new ArgumentException("Give a model file name from the models folder.");
                int ctx = (int)Json.GetNum(body, "context", ContextLength);
                if (ctx >= 256) ContextLength = ctx;
                LoadModel(name);
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/folder" && post)
            {
                string folder = Json.GetStr(body, "path", "");
                _workspace = folder.Length == 0 ? null : new Workspace(folder);
                Log.Text(_workspace != null ? "folder connected: " + _workspace.Root : "folder disconnected");
                Log.Json("folder", "\"path\":" + (_workspace != null ? Json.Quote(_workspace.Root) : "null"));
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/browse") { SendJson(res, 200, Browse(q)); return; }
            if (path == "/api/list") { SendJson(res, 200, "{\"entries\":" + RequireWorkspace().ListJson(q ?? ".") + "}"); return; }
            if (path == "/api/ls") { Log.Text("/ls " + (q ?? ".")); SendJson(res, 200, "{\"text\":" + Json.Quote(RequireWorkspace().List(q ?? ".")) + "}"); return; }
            if (path == "/api/cat") { Log.Text("/cat " + (q ?? "")); SendJson(res, 200, "{\"text\":" + Json.Quote(RequireWorkspace().Read(q ?? "")) + "}"); return; }
            if (path == "/api/chat" && post) { Chat(body, res); return; }
            if (path == "/api/index" && post)
            {
                bool started = Rag.StartReindex(delegate(string m) { Log.Text(m); });
                if (started) Log.Text("rag: reindex started (" + Rag.DocsDir + ")");
                SendJson(res, 200, Rag.StatusJson());
                return;
            }
            if (path == "/api/rag")
            {
                if (post && body.ContainsKey("enabled")) { Rag.Enabled = body["enabled"] is bool && (bool)body["enabled"]; Log.Text("rag: " + (Rag.Enabled ? "on" : "off")); }
                string query = req.QueryString["q"];
                if (string.IsNullOrEmpty(query)) { SendJson(res, 200, Rag.StatusJson()); return; }
                Stopwatch sw = Stopwatch.StartNew();
                List<RagHit> hits = Rag.Search(query, null);
                SendJson(res, 200, "{\"ms\":" + Json.Num(Math.Round(sw.Elapsed.TotalMilliseconds, 2)) + ",\"hits\":" + HitsJson(hits, true) + "}");
                return;
            }
            SendJson(res, 404, "{\"error\":\"unknown endpoint\"}");
        }

        // Local folder picker: drives at the top level, then subfolders. Network drives/shares are not offered.
        static string Browse(string path)
        {
            StringBuilder sb = new StringBuilder();
            if (string.IsNullOrEmpty(path))
            {
                sb.Append("{\"path\":\"\",\"parent\":null,\"dirs\":[");
                bool first = true;
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                    if (!d.IsReady) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"name\":").Append(Json.Quote(d.Name)).Append(",\"path\":").Append(Json.Quote(d.RootDirectory.FullName)).Append('}');
                }
                return sb.Append("]}").ToString();
            }
            if (path.StartsWith(@"\\") || path.StartsWith("//")) throw new ArgumentException("Network folders are not allowed.");
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Folder not found: " + full);
            DirectoryInfo parent = Directory.GetParent(full);
            sb.Append("{\"path\":").Append(Json.Quote(full)).Append(",\"parent\":").Append(parent != null ? Json.Quote(parent.FullName) : "\"\"").Append(",\"dirs\":[");
            List<string> dirs = new List<string>();
            try { dirs.AddRange(Directory.GetDirectories(full)); } catch (UnauthorizedAccessException) { }
            dirs.Sort(StringComparer.OrdinalIgnoreCase);
            bool f2 = true;
            foreach (string d in dirs)
            {
                FileAttributes a;
                try { a = File.GetAttributes(d); } catch { continue; }
                if ((a & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                if (!f2) sb.Append(',');
                f2 = false;
                sb.Append("{\"name\":").Append(Json.Quote(Path.GetFileName(d))).Append(",\"path\":").Append(Json.Quote(d)).Append('}');
            }
            return sb.Append("]}").ToString();
        }

        const int MaxRulesChars = 16000;

        // RULES.md files that apply right now: the app's own (next to src\ and html\) and the connected folder's.
        List<KeyValuePair<string, string>> RulesFiles()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            string appRules = Path.Combine(Directory.GetParent(_htmlDir).FullName, "RULES.md");
            if (File.Exists(appRules)) found.Add(new KeyValuePair<string, string>("app", appRules));
            Workspace w = _workspace;
            if (w != null)
            {
                string folderRules = Path.Combine(w.Root, "RULES.md");
                if (File.Exists(folderRules) && !string.Equals(Path.GetFullPath(folderRules), Path.GetFullPath(appRules), StringComparison.OrdinalIgnoreCase))
                    found.Add(new KeyValuePair<string, string>("folder", folderRules));
            }
            return found;
        }

        // Text added to the system prompt so the model follows the user's rules. Read fresh for every message.
        string RulesPrompt(List<KeyValuePair<string, string>> files)
        {
            if (files.Count == 0) return null;
            StringBuilder sb = new StringBuilder("Follow these rules in every reply. They come from the user's RULES.md and override your defaults:\n");
            foreach (KeyValuePair<string, string> f in files)
            {
                string text;
                try { text = File.ReadAllText(f.Value, Encoding.UTF8).Trim(); } catch { continue; }
                if (text.Length > MaxRulesChars) text = text.Substring(0, MaxRulesChars);
                if (text.Length > 0) sb.Append('\n').Append(text).Append('\n');
            }
            return sb.ToString();
        }

        Workspace RequireWorkspace()
        {
            if (_workspace == null) throw new InvalidOperationException("No folder connected. Use /folder <path>.");
            return _workspace;
        }

        string RulesJson()
        {
            List<KeyValuePair<string, string>> files = RulesFiles();
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < files.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json.Quote(files[i].Key)); }
            return sb.Append(']').ToString();
        }

        string Info()
        {
            ChatEngine e = _engine;
            Workspace w = _workspace;
            return "{\"model\":" + (e != null ? Json.Quote(e.ModelName) : "null") +
                   ",\"file\":" + (e != null ? Json.Quote(Path.GetFileName(e.ModelPath)) : "null") +
                   ",\"backend\":" + (e != null ? Json.Quote(e.Backend) : "null") +
                   ",\"context\":" + ContextLength +
                   ",\"loading\":" + (_loading != null ? Json.Quote(_loading) : "null") +
                   ",\"folder\":" + (w != null ? Json.Quote(w.Root) : "null") +
                   ",\"folderName\":" + (w != null ? Json.Quote(Path.GetFileName(w.Root)) : "null") +
                   ",\"git\":" + (w != null ? w.GitJson() : "null") +
                   ",\"rules\":" + RulesJson() +
                   ",\"rag\":" + Rag.StatusJson() + "}";
        }

        static string HitsJson(List<RagHit> hits, bool withText)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < hits.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"n\":").Append(i + 1).Append(",\"file\":").Append(Json.Quote(hits[i].File)).Append(",\"title\":").Append(Json.Quote(hits[i].Title))
                  .Append(",\"score\":").Append(Json.Num(Math.Round(hits[i].Score, 2)));
                if (withText) sb.Append(",\"text\":").Append(Json.Quote(hits[i].Text));
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        // POST /api/chat {messages:[{role,content}], temperature, top_k, top_p, repeat_penalty, max_tokens}
        // Server-sent events: {"t":text} tokens, {"tool":{op,path,ok,msg}} file actions, {"step":n} a new model turn
        // after tool results, then {"done":true, append:[messages to add to history], ...stats}.
        void Chat(Dictionary<string, object> o, HttpListenerResponse res)
        {
            if (o == null || !o.ContainsKey("messages")) throw new ArgumentException("messages required");
            List<ChatMessage> msgs = new List<ChatMessage>();
            foreach (object m in (List<object>)o["messages"])
            {
                Dictionary<string, object> mm = m as Dictionary<string, object>;
                string role = Json.GetStr(mm, "role", "user"), content = Json.GetStr(mm, "content", "");
                if (role != "system" && role != "user" && role != "assistant") continue;
                msgs.Add(new ChatMessage(role, content));
            }
            SamplerSettings s = new SamplerSettings();
            s.Temperature = (float)Json.GetNum(o, "temperature", 0.7);
            s.TopK = (int)Json.GetNum(o, "top_k", 40);
            s.TopP = (float)Json.GetNum(o, "top_p", 0.95);
            s.RepeatPenalty = (float)Json.GetNum(o, "repeat_penalty", 1.1);
            int maxTokens = Math.Max(1, (int)Json.GetNum(o, "max_tokens", 1024));

            int rid = Interlocked.Increment(ref _requestId);
            Stopwatch total = Stopwatch.StartNew();
            string lastUser = "";
            for (int i = msgs.Count - 1; i >= 0; i--) if (msgs[i].Role == "user") { lastUser = msgs[i].Text; break; }
            string settingsTxt = string.Format(System.Globalization.CultureInfo.InvariantCulture, "temp {0} top_k {1} top_p {2} repeat {3} max {4}",
                s.Temperature, s.TopK, s.TopP, s.RepeatPenalty, maxTokens);
            ChatEngine engNow = _engine; Workspace wsNow = _workspace;
            Log.Text("#" + rid + " USER (" + msgs.Count + " messages in conversation; " + settingsTxt + "; model " +
                     (engNow != null ? Path.GetFileName(engNow.ModelPath) : "none") + "; folder " + (wsNow != null ? wsNow.Root : "none") + ")\n" + lastUser);
            Log.Json("request", "\"id\":" + rid + ",\"messages\":" + msgs.Count + ",\"user\":" + Json.Quote(lastUser) +
                     ",\"model\":" + (engNow != null ? Json.Quote(Path.GetFileName(engNow.ModelPath)) : "null") +
                     ",\"folder\":" + (wsNow != null ? Json.Quote(wsNow.Root) : "null") + ",\"settings\":" + Json.Quote(settingsTxt));

            res.StatusCode = 200;
            res.ContentType = "text/event-stream; charset=utf-8";
            res.Headers["Cache-Control"] = "no-store";
            res.SendChunked = true;
            Stream outS = res.OutputStream;
            bool gone = false;
            TextCallback write = delegate(string line)
            {
                if (gone) return;
                try
                {
                    byte[] b = Encoding.UTF8.GetBytes("data: " + line + "\n\n");
                    outS.Write(b, 0, b.Length);
                    outS.Flush();
                }
                catch { gone = true; } // browser closed or pressed Stop
            };

            lock (_engineLock)
            {
                try
                {
                    if (_engine == null) throw new InvalidOperationException("No model loaded. Use /models and /model <n>.");
                    Workspace ws = _workspace;
                    List<KeyValuePair<string, string>> rulesFiles = RulesFiles();
                    string rules = RulesPrompt(rulesFiles);
                    if (rules != null)
                    {
                        int ri = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                        if (ri >= 0) msgs[ri] = new ChatMessage("system", rules + "\n" + msgs[ri].Text);
                        else msgs.Insert(0, new ChatMessage("system", rules));
                        List<string> names = new List<string>();
                        foreach (KeyValuePair<string, string> f in rulesFiles) names.Add(f.Key + " " + f.Value);
                        Log.Text("#" + rid + " RULES " + string.Join(" + ", names.ToArray()));
                    }
                    if (ws != null)
                    {
                        // Tool instructions go into the system message (added if the client sent none).
                        int si = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                        string tools = ws.ToolInstructions();
                        if (si >= 0) msgs[si] = new ChatMessage("system", msgs[si].Text + "\n\n" + tools);
                        else msgs.Insert(0, new ChatMessage("system", tools));
                    }

                    // Retrieved docs go into the newest user message only (not the system prompt), so the cached
                    // conversation prefix stays valid and old turns don't keep spending context on old references.
                    if (Rag.Enabled)
                    {
                        int ui = msgs.FindLastIndex(delegate(ChatMessage m) { return m.Role == "user"; });
                        if (ui >= 0)
                        {
                            int pi = ui > 0 ? msgs.FindLastIndex(ui - 1, delegate(ChatMessage m) { return m.Role == "user"; }) : -1;
                            Stopwatch rsw = Stopwatch.StartNew();
                            List<RagHit> hits = Rag.Search(msgs[ui].Text, pi >= 0 ? msgs[pi].Text : null);
                            if (hits.Count > 0)
                            {
                                msgs[ui] = new ChatMessage("user", Rag.PromptBlock(hits) + "\nUser message:\n" + msgs[ui].Text);
                                write("{\"rag\":" + HitsJson(hits, false) + "}");
                                List<string> srcs = new List<string>();
                                foreach (RagHit h in hits) srcs.Add(h.File + (h.Title.Length > 0 ? " › " + h.Title : "") + " (" + h.Score.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ")");
                                Log.Text("#" + rid + " RAG " + hits.Count + " chunks in " + ChatLog.Ms(rsw.Elapsed.TotalSeconds) + " ms: " + string.Join("; ", srcs.ToArray()));
                                Log.Json("rag", "\"id\":" + rid + ",\"ms\":" + ChatLog.Ms(rsw.Elapsed.TotalSeconds) + ",\"hits\":" + HitsJson(hits, false));
                            }
                        }
                    }

                    StringBuilder append = new StringBuilder("[");
                    GenerationStats st = null;
                    int totalTokens = 0;
                    double genSeconds = 0;
                    for (int step = 0; step < MaxToolSteps && !gone; step++)
                    {
                        if (step > 0) write("{\"step\":" + step + "}");
                        StringBuilder reply = new StringBuilder();
                        Stopwatch stepWatch = Stopwatch.StartNew();
                        double ttft = -1;
                        st = _engine.Generate(msgs, s, maxTokens, delegate(string piece)
                        {
                            if (ttft < 0) ttft = stepWatch.Elapsed.TotalSeconds;
                            reply.Append(piece);
                            write("{\"t\":" + Json.Quote(piece) + "}");
                        }, delegate { return gone; });
                        double stepSec = stepWatch.Elapsed.TotalSeconds;
                        Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "#{0} ASSISTANT step {1}: first token {2} ms | prompt {3} new + {4} cached tokens in {5} ms ({6:F1} tok/s) | " +
                            "generated {7} tokens in {8} ms ({9:F1} tok/s) | step {10} ms | stop: {11}{12}\n{13}",
                            rid, step, ttft < 0 ? "-" : ChatLog.Ms(ttft), st.PromptTokens, st.ReusedTokens, ChatLog.Ms(st.PromptSeconds), st.PromptTokensPerSecond,
                            st.GeneratedTokens, ChatLog.Ms(st.GenerateSeconds), st.TokensPerSecond, ChatLog.Ms(stepSec), st.StopReason,
                            gone ? " (client disconnected / stopped)" : "", reply.ToString()));
                        Log.Json("reply", "\"id\":" + rid + ",\"step\":" + step + ",\"ttft_ms\":" + (ttft < 0 ? "null" : ChatLog.Ms(ttft)) +
                            ",\"prompt_tokens\":" + st.PromptTokens + ",\"cached_tokens\":" + st.ReusedTokens + ",\"prompt_ms\":" + ChatLog.Ms(st.PromptSeconds) +
                            ",\"gen_tokens\":" + st.GeneratedTokens + ",\"gen_ms\":" + ChatLog.Ms(st.GenerateSeconds) +
                            ",\"tok_s\":" + Json.Num(Math.Round(st.TokensPerSecond, 1)) + ",\"stop\":" + Json.Quote(st.StopReason) +
                            ",\"client_gone\":" + (gone ? "true" : "false") + ",\"text\":" + Json.Quote(reply.ToString()));
                        totalTokens += st.GeneratedTokens;
                        genSeconds += st.GenerateSeconds;
                        string replyText = reply.ToString();
                        msgs.Add(new ChatMessage("assistant", replyText));
                        if (append.Length > 1) append.Append(',');
                        append.Append("{\"role\":\"assistant\",\"content\":").Append(Json.Quote(replyText)).Append('}');
                        if (ws == null || gone) break;

                        List<Workspace.Action> actions = ws.Execute(replyText);
                        if (actions.Count == 0) break;
                        bool needsAnswer = false;
                        StringBuilder results = new StringBuilder("Tool results:\n");
                        foreach (Workspace.Action a in actions)
                        {
                            write("{\"tool\":{\"op\":" + Json.Quote(a.Op) + ",\"path\":" + Json.Quote(a.Path) + ",\"ok\":" + (a.Ok ? "true" : "false") +
                                  ",\"msg\":" + Json.Quote(a.Message) + "}}");
                            Log.Text("#" + rid + " TOOL " + (a.Ok ? "ok   " : "FAIL ") + a.Message);
                            Log.Json("tool", "\"id\":" + rid + ",\"op\":" + Json.Quote(a.Op) + ",\"path\":" + Json.Quote(a.Path) +
                                     ",\"ok\":" + (a.Ok ? "true" : "false") + ",\"msg\":" + Json.Quote(a.Message));
                            results.Append("- ").Append(a.Message).Append('\n');
                            if (a.Content != null)
                            {
                                results.Append("```\n").Append(a.Content).Append("\n```\n");
                                needsAnswer = true;
                            }
                            if (!a.Ok) needsAnswer = true;
                        }
                        // Reads, listings and failures go back to the model; plain successful writes end the turn.
                        if (!needsAnswer) break;
                        msgs.Add(new ChatMessage("user", results.ToString()));
                        append.Append(",{\"role\":\"user\",\"content\":").Append(Json.Quote(results.ToString())).Append('}');
                    }
                    append.Append(']');
                    double tps = genSeconds > 0 ? totalTokens / genSeconds : 0;
                    Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0} DONE total {1} ms, {2} tokens generated, {3:F1} tok/s",
                        rid, ChatLog.Ms(total.Elapsed.TotalSeconds), totalTokens, tps));
                    Log.Json("done", "\"id\":" + rid + ",\"total_ms\":" + ChatLog.Ms(total.Elapsed.TotalSeconds) + ",\"gen_tokens\":" + totalTokens +
                             ",\"tok_s\":" + Json.Num(Math.Round(tps, 1)));
                    write("{\"done\":true,\"append\":" + append + ",\"tokens\":" + totalTokens + ",\"tps\":" + Json.Num(Math.Round(tps, 1)) +
                          ",\"ctx_used\":" + _engine.UsedTokens + ",\"ctx_size\":" + _engine.ContextLength +
                          (st != null ? ",\"prompt_tokens\":" + st.PromptTokens + ",\"cached\":" + st.ReusedTokens + ",\"stop\":" + Json.Quote(st.StopReason) : "") + "}");
                }
                catch (Exception ex)
                {
                    Log.Text("#" + rid + " ERROR " + ex.Message);
                    Log.Json("error", "\"id\":" + rid + ",\"error\":" + Json.Quote(ex.Message));
                    write("{\"error\":" + Json.Quote(ex.Message) + "}");
                }
            }
            try { outS.Close(); } catch { }
        }

        static Dictionary<string, object> ReadJson(HttpListenerRequest req)
        {
            string body;
            using (StreamReader r = new StreamReader(req.InputStream, Encoding.UTF8)) body = r.ReadToEnd();
            return body.Trim().Length == 0 ? new Dictionary<string, object>() : Json.Parse(body) as Dictionary<string, object>;
        }

        void ServeFile(HttpListenerResponse res, string urlPath)
        {
            string rel = Uri.UnescapeDataString(urlPath).TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(_htmlDir, rel));
            if (!full.StartsWith(_htmlDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
            {
                Send(res, 404, "text/plain", "Not found");
                return;
            }
            string ext = Path.GetExtension(full).ToLowerInvariant();
            string type = ext == ".html" ? "text/html; charset=utf-8" : ext == ".js" ? "text/javascript; charset=utf-8"
                        : ext == ".css" ? "text/css; charset=utf-8" : ext == ".svg" ? "image/svg+xml" : ext == ".png" ? "image/png"
                        : ext == ".ttf" ? "font/ttf" : ext == ".woff2" ? "font/woff2" : "application/octet-stream";
            byte[] body = File.ReadAllBytes(full);
            res.StatusCode = 200;
            res.ContentType = type;
            res.Headers["Cache-Control"] = "no-store";
            res.ContentLength64 = body.Length;
            res.OutputStream.Write(body, 0, body.Length);
            res.OutputStream.Close();
        }

        static void SendJson(HttpListenerResponse res, int code, string json) { Send(res, code, "application/json; charset=utf-8", json); }

        static void Send(HttpListenerResponse res, int code, string type, string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            res.StatusCode = code;
            res.ContentType = type;
            res.ContentLength64 = b.Length;
            res.OutputStream.Write(b, 0, b.Length);
            res.OutputStream.Close();
        }

        public void Dispose()
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
            Rag.Dispose();
        }
    }
}
