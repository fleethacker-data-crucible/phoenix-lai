// Loopback-only chat server: serves html\, streams chat, switches models, and lets the model work in a local folder.
// C# 5 only. The ONLY file allowed to use networking or start a process (enforced by run.ps1): it listens on
// 127.0.0.1 only, never makes outgoing connections, and only starts msedge.exe or chrome.exe (app window).
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
            Rag = new Rag(Path.Combine(Directory.GetParent(_htmlDir).FullName, "docs"), _modelsDir);
            // No automatic rebuild or embedder warm-up at startup until the GPU embedder is made safe (it caused a driver
            // reset on 2026-10-16); an older index just waits for /index.
            if (Rag.NeedsRebuild) Log.Text("rag: index is from an older version; run /index to rebuild");
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
            // One server per user: a second launch just opens another window on the running one. (Two servers would fight
            // over the GPU and the loser falls back to the much slower CPU.) The mutex is freed by Windows if we crash.
            bool first;
            using (Mutex single = new Mutex(true, @"Local\PhoenixAI.Server", out first))
            {
                if (!first)
                {
                    // Ask the running server to show its window (it opens a new one only if none is open).
                    EventWaitHandle show;
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out show)) using (show) show.Set();
                    return 0;
                }
                try { return RunServer(root, modelPath, ctx); }
                finally { single.ReleaseMutex(); }
            }
        }

        const string ShowEventName = @"Local\PhoenixAI.Show";

        // Opens the app window on `url` in the preferred browser; returns the browser process (or null if none found).
        static Process OpenWindow(string url, WebServer log)
        {
            string pref = PreferredBrowser(), used;
            string exe = FindBrowser(pref, out used);
            if (exe == null) return null;
            // A private profile folder per browser keeps the app separate from the user's own browser windows.
            string profile = Path.Combine(AppDataDir, used + "-profile");
            Directory.CreateDirectory(profile);
            if (log != null) log.Log.Text("opening " + used + (used != pref ? " (" + pref + " is not installed)" : "") + ": " + exe);
            ProcessStartInfo psi = new ProcessStartInfo(exe,
                "--app=" + url + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check --window-size=1100,800");
            psi.UseShellExecute = false;
            return Process.Start(psi);
        }

        static int RunServer(string root, string modelPath, int ctx)
        {
            using (WebServer server = StartOnFreePort(Path.Combine(root, "html"), Path.Combine(root, "models"), Path.Combine(root, "logs")))
            {
                server.ContextLength = ctx;
                // Another launch of the app: bring the open window forward, or open one if every window was closed.
                EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                Thread showThread = new Thread(delegate()
                {
                    while (show.WaitOne())
                    {
                        if (server.OpenPages() > 0) { server._focusRequested = true; server.Log.Text("launched again: focusing the open window"); }
                        else { server.Log.Text("launched again: no window open, opening one"); Process p = OpenWindow(server.Url, server); if (p != null) p.Dispose(); }
                    }
                });
                showThread.IsBackground = true;
                showThread.Start();
                if (!string.IsNullOrEmpty(modelPath))
                {
                    string m = modelPath;
                    ThreadPool.QueueUserWorkItem(delegate { try { server.LoadModel(m); } catch { } });
                }
                // Windows left open from a previous run reconnect to us by themselves (same port): give them a moment to check
                // in, and if one does, bring it forward instead of opening another.
                for (int t = 0; t < 30 && server.OpenPages() == 0; t++) Thread.Sleep(100);
                if (server.OpenPages() > 0)
                {
                    server._focusRequested = true;
                    server.Log.Text("an app window from before is still open: reusing it");
                    server.WaitWhilePagesActive(TimeSpan.FromSeconds(75));
                }
                else
                {
                    Process browser = OpenWindow(server.Url, server);
                    if (browser != null)
                    {
                        using (browser) browser.WaitForExit();
                        // If an app window was already open, the browser hands the new one to that process and exits at once,
                        // so the process alone can't tell us when the user is done: stay up while any page is still talking to
                        // us (each window checks in every 2 s and says goodbye when it closes).
                        server.WaitWhilePagesActive(TimeSpan.FromSeconds(75));
                    }
                    else
                    {
                        MessageBox.Show("Neither Microsoft Edge nor Google Chrome was found. Open this address in any browser:\n\n" + server.Url +
                            "\n\nClose this box to stop the server.", "PhoenixAI");
                    }
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

        static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLLMStudio");
        static string BrowserFile { get { return Path.Combine(AppDataDir, "browser.txt"); } }

        // "edge" (default) or "chrome", as chosen with /browser.
        static string PreferredBrowser()
        {
            try { string b = File.ReadAllText(BrowserFile).Trim().ToLowerInvariant(); if (b == "chrome" || b == "edge") return b; } catch { }
            return "edge";
        }

        // Path of the preferred browser, or of the other one if the preferred is not installed; null if neither is.
        static string FindBrowser(string preferred, out string used)
        {
            string other = preferred == "chrome" ? "edge" : "chrome";
            string exe = BrowserPath(preferred);
            used = preferred;
            if (exe == null) { exe = BrowserPath(other); used = exe != null ? other : null; }
            return exe;
        }

        static string BrowserPath(string browser)
        {
            string rel = browser == "chrome" ? Path.Combine("Google", "Chrome", "Application", "chrome.exe") : Path.Combine("Microsoft", "Edge", "Application", "msedge.exe");
            string[] roots = { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                               Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            foreach (string r in roots)
            {
                if (string.IsNullOrEmpty(r)) continue;
                string p = Path.Combine(r, rel);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        // The last 10 connected folders, newest first (offered by /folder). Folders that no longer exist are skipped.
        const int MaxRecentFolders = 10;
        static string RecentFile { get { return Path.Combine(AppDataDir, "recent-folders.txt"); } }

        static List<string> RecentFolders()
        {
            List<string> list = new List<string>();
            try
            {
                foreach (string line in File.ReadAllLines(RecentFile, Encoding.UTF8))
                    if (line.Trim().Length > 0 && Directory.Exists(line.Trim())) list.Add(line.Trim());
            }
            catch { }
            return list;
        }

        static void RememberFolder(string root)
        {
            List<string> list = RecentFolders();
            list.RemoveAll(delegate(string f) { return string.Equals(f, root, StringComparison.OrdinalIgnoreCase); });
            list.Insert(0, root);
            if (list.Count > MaxRecentFolders) list.RemoveRange(MaxRecentFolders, list.Count - MaxRecentFolders);
            try { Directory.CreateDirectory(AppDataDir); File.WriteAllLines(RecentFile, list.ToArray(), Encoding.UTF8); } catch { }
        }

        static string BrowserJson()
        {
            string pref = PreferredBrowser(), used;
            FindBrowser(pref, out used);
            return "{\"browser\":" + Json.Quote(pref) + ",\"installed\":" + (used == pref ? "true" : "false") +
                   ",\"fallback\":" + (used != null && used != pref ? Json.Quote(used) : "null") + "}";
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

        long _lastRequestTicks = DateTime.UtcNow.Ticks;
        int _activeRequests;
        volatile bool _focusRequested;
        readonly Dictionary<string, DateTime> _pages = new Dictionary<string, DateTime>();   // page id -> last poll (UTC)

        // Windows that are open: polled within the last 75 s (minimized windows poll only about once a minute) and not closed.
        int OpenPages()
        {
            lock (_pages)
            {
                List<string> stale = new List<string>();
                foreach (KeyValuePair<string, DateTime> kv in _pages) if (DateTime.UtcNow - kv.Value > TimeSpan.FromSeconds(75)) stale.Add(kv.Key);
                foreach (string k in stale) _pages.Remove(k);
                return _pages.Count;
            }
        }

        void WaitWhilePagesActive(TimeSpan idle)
        {
            Thread.Sleep(3000);   // give a new window time to load and check in
            while (Thread.VolatileRead(ref _activeRequests) > 0 || OpenPages() > 0 ||
                   DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastRequestTicks) < TimeSpan.FromSeconds(5).Ticks)
                Thread.Sleep(500);
        }

        void Handle(HttpListenerContext ctx)
        {
            Interlocked.Increment(ref _activeRequests);
            Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
            try { HandleRequest(ctx); }
            finally
            {
                Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                Interlocked.Decrement(ref _activeRequests);
            }
        }

        void HandleRequest(HttpListenerContext ctx)
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

            string page = req.QueryString["page"];
            if (path == "/api/bye") { if (page != null) lock (_pages) _pages.Remove(page); SendJson(res, 200, "{}"); return; }
            if (path == "/api/info")
            {
                if (page != null && page.Length <= 40) lock (_pages) _pages[page] = DateTime.UtcNow;
                string json = Info();
                if (_focusRequested && page != null) { _focusRequested = false; json = json.Substring(0, json.Length - 1) + ",\"focus\":true}"; }
                SendJson(res, 200, json);
                return;
            }
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
            if (path == "/api/recent-folders")
            {
                StringBuilder sb = new StringBuilder("{\"folders\":[");
                List<string> list = RecentFolders();
                for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json.Quote(list[i])); }
                SendJson(res, 200, sb.Append("]}").ToString());
                return;
            }
            if (path == "/api/folder" && post)
            {
                string folder = Json.GetStr(body, "path", "");
                _workspace = folder.Length == 0 ? null : new Workspace(folder);
                if (_workspace != null) RememberFolder(_workspace.Root);
                Log.Text(_workspace != null ? "folder connected: " + _workspace.Root : "folder disconnected");
                Log.Json("folder", "\"path\":" + (_workspace != null ? Json.Quote(_workspace.Root) : "null"));
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/list") { SendJson(res, 200, "{\"entries\":" + RequireWorkspace().ListJson(q ?? ".") + "}"); return; }
            if (path == "/api/ls") { Log.Text("/ls " + (q ?? ".")); SendJson(res, 200, "{\"text\":" + Json.Quote(RequireWorkspace().List(q ?? ".")) + "}"); return; }
            if (path == "/api/cat") { Log.Text("/cat " + (q ?? "")); SendJson(res, 200, "{\"text\":" + Json.Quote(RequireWorkspace().Read(q ?? "")) + "}"); return; }
            if (path == "/api/chat" && post) { Chat(body, res); return; }
            if (path == "/api/browser")
            {
                if (post)
                {
                    string b = Json.GetStr(body, "browser", "").Trim().ToLowerInvariant();
                    if (b != "edge" && b != "chrome") throw new ArgumentException("Browser must be edge or chrome.");
                    Directory.CreateDirectory(AppDataDir);
                    File.WriteAllText(BrowserFile, b);
                    Log.Text("browser set to " + b);
                }
                SendJson(res, 200, BrowserJson());
                return;
            }
            if (path == "/api/vram") { SendJson(res, 200, VramProcessesJson()); return; }
            if (path == "/api/unload" && post)
            {
                // Frees the model's VRAM (weights + context cache); /model loads one again.
                lock (_engineLock) { if (_engine != null) { Log.Text("unloaded " + Path.GetFileName(_engine.ModelPath)); _engine.Dispose(); _engine = null; } }
                GC.Collect();
                SendJson(res, 200, Info());
                return;
            }
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
                   ",\"rag\":" + Rag.StatusJson() +
                   ",\"vram\":" + VramJson(e) +
                   ",\"version\":" + Json.Quote(Version()) + "}";
        }

        // App version from src\__version.info (read on each request, so editing it needs no restart).
        string Version()
        {
            try { return File.ReadAllText(Path.Combine(Directory.GetParent(_htmlDir).FullName, "src", "__version.info")).Trim(); }
            catch { return ""; }
        }

        static string VramJson(ChatEngine e)
        {
            ulong used, budget;
            if (e == null || !e.Vram(out used, out budget)) return "null";
            return "{\"used\":" + used + ",\"budget\":" + budget + "}";
        }

        // Dedicated VRAM per process from Windows' "GPU Process Memory" counters (no admin rights needed), largest first.
        static string VramProcessesJson()
        {
            Dictionary<int, long> byPid = new Dictionary<int, long>();
            try
            {
                // One bulk read of the whole category (reading counters one by one takes seconds).
                InstanceDataCollection data = new PerformanceCounterCategory("GPU Process Memory").ReadCategory()["Dedicated Usage"];
                foreach (InstanceData d in data.Values)
                {
                    // instance names look like "pid_1234_luid_0x..._phys_0"
                    string inst = d.InstanceName;
                    if (!inst.StartsWith("pid_")) continue;
                    int us = inst.IndexOf('_', 4);
                    int pid;
                    if (us < 0 || !int.TryParse(inst.Substring(4, us - 4), out pid)) continue;
                    long cur; byPid.TryGetValue(pid, out cur);
                    byPid[pid] = cur + d.RawValue;
                }
            }
            catch (Exception ex) { return "{\"error\":" + Json.Quote(ex.Message) + "}"; }
            List<KeyValuePair<int, long>> list = new List<KeyValuePair<int, long>>(byPid);
            list.Sort(delegate(KeyValuePair<int, long> a, KeyValuePair<int, long> b) { return b.Value.CompareTo(a.Value); });
            StringBuilder sb = new StringBuilder("{\"processes\":[");
            int n = 0;
            int self = Process.GetCurrentProcess().Id;
            foreach (KeyValuePair<int, long> kv in list)
            {
                if (kv.Value < (32L << 20) || n >= 12) continue;   // ignore < 32 MB
                string name;
                try { using (Process p = Process.GetProcessById(kv.Key)) name = p.ProcessName; } catch { name = "pid " + kv.Key; }
                if (n++ > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Json.Quote(kv.Key == self ? name + " (PhoenixAI)" : name)).Append(",\"pid\":").Append(kv.Key).Append(",\"bytes\":").Append(kv.Value).Append('}');
            }
            return sb.Append("]}").ToString();
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
                    string libraryNote = Rag.LibraryNote();
                    if (libraryNote != null)
                    {
                        int si2 = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                        if (si2 >= 0) msgs[si2] = new ChatMessage("system", msgs[si2].Text + "\n\n" + libraryNote);
                        else msgs.Insert(0, new ChatMessage("system", libraryNote));
                    }
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
                                msgs[ui] = new ChatMessage("user", Rag.WrapMessage(hits, msgs[ui].Text));
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
                    bool lost = ex is GpuDeviceLostException;
                    write("{\"error\":" + Json.Quote(lost ? "The GPU was reset by the driver; reloading the model. Send the message again in a moment." : ex.Message) + "}");
                    if (lost && _engine != null)
                    {
                        // The old device is gone: drop it and load the model again on a fresh one.
                        string file = _engine.ModelPath;
                        try { _engine.Dispose(); } catch { }
                        _engine = null;
                        Log.Text("#" + rid + " GPU device lost; reloading " + Path.GetFileName(file));
                        ThreadPool.QueueUserWorkItem(delegate { try { LoadModel(file); } catch { } });
                    }
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
