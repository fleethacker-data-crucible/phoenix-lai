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
    public sealed partial class WebServer : IDisposable
    {
        const int MaxToolSteps = 6;

        readonly string _htmlDir, _modelsDir;
        readonly HttpListener _listener = new HttpListener();
        readonly object _engineLock = new object();     // one generation / model load at a time
        ChatEngine _engine;
        Workspace _workspace;
        string _loading;
        string _loadError;
        string _memWarning;  // set after a load that left Windows almost out of commit memory   // last model load / GPU failure, shown red in the status bar; cleared by the next good load
        public int ContextLength = 0;   // 0 = auto (ContextPlanner picks it at load); the loaded model's is _engine.ContextLength
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
            Workspace.EnsureProjectsRoot();
            ChatEngine.KvCache = ReadKv();
            Rag = new Rag(Path.Combine(Directory.GetParent(_htmlDir).FullName, "docs"), _modelsDir);
            // An outdated or missing index is rebuilt in the background (otherwise the docs look empty and the model is
            // never told about them). The GPU embedder submits one layer per job and holds GpuGate, so it cannot collide
            // with a model upload (the 2026-10-16 driver reset); it falls back to the CPU on device loss.
            if (Rag.NeedsRebuild || Rag.HasUnindexedDocs())
            {
                Log.Text("rag: index is missing or outdated; rebuilding");
                Rag.StartReindex(delegate(string m) { Log.Text(m); });
            }
            Log.Text("server started on " + Url);
            StartApi();
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
            // Opens WindowWidth x WindowHeight, centered horizontally, WindowTop px below the top of the primary screen
            // (the browser's remembered placement is cleared first, or it would win over these flags).
            ForgetWindowPlacement(profile);
            DisableMiniMenu(profile);
            System.Drawing.Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            int w = Math.Min(WindowWidth, wa.Width), h = Math.Min(WindowHeight, Math.Max(400, wa.Height - WindowTop));
            int x = wa.Left + (wa.Width - w) / 2, y = wa.Top + Math.Min(WindowTop, Math.Max(0, wa.Height - h));
            ProcessStartInfo psi = new ProcessStartInfo(exe,
                "--app=" + url + " --user-data-dir=\"" + profile + "\" --no-first-run --no-default-browser-check" +
                " --window-size=" + w + "," + h + " --window-position=" + x + "," + y);
            psi.UseShellExecute = false;
            ShellGuard.CheckLaunch(psi.FileName, psi.Arguments);
            return Process.Start(psi);
        }

        const int WindowWidth = 1300, WindowHeight = 800, WindowTop = 100;

        // Removes the browser's saved app-window placement ("app_window_placement" in the profile's Preferences) so the
        // window opens where the launch flags say. Preferences is JSON; the key's object is cut out by brace matching.
        static void ForgetWindowPlacement(string profile)
        {
            try
            {
                string prefs = Path.Combine(profile, "Default", "Preferences");
                if (!File.Exists(prefs)) return;
                string t = File.ReadAllText(prefs, Encoding.UTF8);
                const string key = "\"app_window_placement\":";
                int k = t.IndexOf(key, StringComparison.Ordinal);
                if (k < 0) return;
                int i = k + key.Length;
                if (i >= t.Length || t[i] != '{') return;
                int depth = 0, end = -1;
                bool inStr = false;
                for (int j = i; j < t.Length; j++)
                {
                    char c = t[j];
                    if (inStr) { if (c == '\\') j++; else if (c == '"') inStr = false; continue; }
                    if (c == '"') inStr = true;
                    else if (c == '{') depth++;
                    else if (c == '}' && --depth == 0) { end = j + 1; break; }
                }
                if (end < 0) return;
                // Drop the member and one adjoining comma.
                int from = k, to = end;
                if (to < t.Length && t[to] == ',') to++;
                else if (from > 0 && t[from - 1] == ',') from--;
                File.WriteAllText(prefs, t.Substring(0, from) + t.Substring(to), new UTF8Encoding(false));
            }
            catch { }
        }

        // Turns off Edge's text-selection mini menu for the app's private profile only (the user's own Edge is untouched):
        // the profile preference edge_quick_search.show_mini_menu = false, written before every launch because Edge
        // rewrites Preferences while it runs. Chrome ignores the key.
        static void DisableMiniMenu(string profile)
        {
            try
            {
                string dir = Path.Combine(profile, "Default"), prefs = Path.Combine(dir, "Preferences");
                Directory.CreateDirectory(dir);
                string t = File.Exists(prefs) ? File.ReadAllText(prefs, Encoding.UTF8).Trim() : "";
                if (t.Length < 2 || t[0] != '{') t = "{}";
                const string section = "\"edge_quick_search\":{", key = "\"show_mini_menu\":";
                int sx = t.IndexOf(section, StringComparison.Ordinal);
                if (sx < 0)
                    t = "{" + section + key + "false}" + (t.Length > 2 ? "," : "") + t.Substring(1);
                else
                {
                    int body = sx + section.Length;
                    int close = t.IndexOf('}', body);   // the section holds only scalars and flat lists before its close
                    int kx = t.IndexOf(key, body, StringComparison.Ordinal);
                    if (kx >= 0 && (close < 0 || kx < close))
                    {
                        int v = kx + key.Length, ve = v;
                        while (ve < t.Length && char.IsLetter(t[ve])) ve++;
                        t = t.Substring(0, v) + "false" + t.Substring(ve);
                    }
                    else t = t.Substring(0, body) + key + "false" + (t[body] == '}' ? "" : ",") + t.Substring(body);
                }
                File.WriteAllText(prefs, t, new UTF8Encoding(false));
            }
            catch { }
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
                        MessageBox.Show("There is no compatible browsers installed. Please get either Chrome or Edge installed before this program can run.",
                            "PhoenixAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

        // "auto" (default: Chrome, else Edge), "chrome" or "edge", as chosen in /settings.
        static string PreferredBrowser()
        {
            try { string b = File.ReadAllText(BrowserFile).Trim().ToLowerInvariant(); if (b == "chrome" || b == "edge" || b == "auto") return b; } catch { }
            return "auto";
        }

        // Path of the preferred browser (auto = Chrome), or of the other one if it is not installed; null if neither is.
        static string FindBrowser(string preferred, out string used)
        {
            if (preferred == "auto") preferred = "chrome";
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
        // Folder access master switch (/folder on|off), kept across restarts. Off: no folder can be connected, the
        // connected one is dropped, and the model gets no file tools at all.
        static string FolderAccessFile { get { return Path.Combine(AppDataDir, "folder-access.txt"); } }
        bool _folderAccess = ReadFolderAccess();

        static bool ReadFolderAccess()
        {
            try { return !File.Exists(FolderAccessFile) || File.ReadAllText(FolderAccessFile).Trim() != "off"; } catch { return false; }
        }

        // Where models run (/device gpu|cpu), kept across restarts. GPU still falls back to the CPU if it fails.
        static string DeviceFile { get { return Path.Combine(AppDataDir, "device.txt"); } }
        // CPU mode is switched off for now (GPU only): a saved "cpu" choice is ignored and /api/device refuses it.
        public const bool CpuModeAllowed = false;
        bool _useGpu = !CpuModeAllowed || ReadUseGpu();

        static bool ReadUseGpu()
        {
            try { return !File.Exists(DeviceFile) || File.ReadAllText(DeviceFile).Trim() != "cpu"; } catch { return true; }
        }

        // Whether a model can use the q8 / q4 KV cache (read from its header once per file version): Llama-family models
        // whose KV width is a multiple of 64; Gemma keeps fp16 (ChatEngine.KvFor).
        static readonly Dictionary<string, bool> _kvqCache = new Dictionary<string, bool>();
        static bool SupportsQuantKv(string file)
        {
            string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
            lock (_kvqCache)
            {
                bool ok;
                if (_kvqCache.TryGetValue(key, out ok)) return ok;
                try
                {
                    using (GgufFile g = GgufFile.Open(file))
                    {
                        string arch = g.GetString("general.architecture");
                        if (GemmaConfig.IsGemma(arch)) ok = false;
                        else { ModelConfig m = ModelConfig.FromGguf(g); ok = m.NHeadKv * m.HeadDim % 64 == 0; }
                    }
                }
                catch { ok = false; }
                _kvqCache[key] = ok;
                return ok;
            }
        }

        int _recallBusy;
        volatile bool _recallStop;
        volatile string _recallProgress;

        // /mode auto | accept, kept across restarts. Accept: the model's file actions wait for the user's Accept / Deny.
        static string ModeFile { get { return Path.Combine(AppDataDir, "mode.txt"); } }
        bool _acceptMode = ReadAcceptMode();
        int _approvalId;
        readonly Dictionary<int, KeyValuePair<ManualResetEvent, bool>> _approvals = new Dictionary<int, KeyValuePair<ManualResetEvent, bool>>();
        static bool ReadAcceptMode() { try { return File.ReadAllText(ModeFile).Trim() == "accept"; } catch { return false; } }

        // KV cache format for GPU Llama models (/settings: f16, q8, q4), kept across restarts.
        static string KvFile { get { return Path.Combine(AppDataDir, "kv-cache.txt"); } }
        static KvType ReadKv()
        {
            try { string s = File.ReadAllText(KvFile).Trim(); return s == "f16" ? KvType.F16 : s == "q4" ? KvType.Q4 : KvType.Q8; } catch { return KvType.Q8; }
        }

        static string RecentFile { get { return Path.Combine(AppDataDir, "recent-folders.txt"); } }

        static List<string> RecentFolders()
        {
            List<string> list = new List<string>();
            try
            {
                foreach (string line in File.ReadAllLines(RecentFile, Encoding.UTF8))
                {
                    string f = line.Trim();   // project paths ("/my-app/src"); old absolute entries are dropped
                    if (f.StartsWith("/") && f.IndexOf("..") < 0 && Directory.Exists(Path.Combine(Workspace.ProjectsRoot, f.Trim('/').Replace('/', '\\')))) list.Add(f);
                }
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
            bool ok = pref == "auto" ? used != null : used == pref;
            return "{\"browser\":" + Json.Quote(pref) + ",\"used\":" + (used != null ? Json.Quote(used) : "null") + ",\"installed\":" + (ok ? "true" : "false") +
                   ",\"fallback\":" + (used != null && !ok ? Json.Quote(used) : "null") + "}";
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
                Log.Text("loading model " + _loading + " (context " + (ContextLength > 0 ? ContextLength.ToString() : "auto") + ")");
                try
                {
                    if (_engine != null) { _engine.Dispose(); _engine = null; }
                    _loadError = null;
                    _engine = ChatEngine.Load(path, ContextLength, _useGpu, delegate(string m) { Log.Text("  " + m); });
                    if (_workspace != null) _engine.RecallDir = Path.Combine(_workspace.Root, ".phoenix", "recall");
                    Log.Text("loaded " + _engine.ModelName + " on " + _engine.Backend + " in " + ChatLog.Ms(sw.Elapsed.TotalSeconds) + " ms");
                    // The weights now live in VRAM: give the RAM the file upload used back to Windows.
                    // Warm up before reporting ready (the UI shows "loading" until then): first prompt runs at full speed.
                    Stopwatch wsw = Stopwatch.StartNew();
                    if (_engine.Backend.StartsWith("GPU")) try { _engine.WarmUp(); } catch (Exception ex) { Log.Text("  warm-up failed: " + ex.Message); }
                    Log.Text("  warmed up in " + ChatLog.Ms(wsw.Elapsed.TotalSeconds) + " ms");
                    // The docs-search model (~2.8 s to load) comes up in the background after "ready": the chat model is usable
                    // at once. It holds the GPU lock while loading, so it never overlaps a chat's GPU work (a chat sent in those
                    // seconds waits for it, as before).
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Stopwatch esw = Stopwatch.StartNew();
                        try { lock (GpuGate.Lock) Rag.WarmUpNow(); Log.Text("docs search ready on " + (Rag.EmbedBackend ?? "none") + " in " + ChatLog.Ms(esw.Elapsed.TotalSeconds) + " ms"); }
                        catch (Exception ex) { Log.Text("docs search warm-up failed: " + ex.Message); }
                    });
                    if (_engine.Backend.StartsWith("GPU")) Log.Text("  released " + MemoryTrim.Trim() + " MB of RAM after the upload");
                    long commit = MemoryTrim.AvailableCommit();
                    _memWarning = commit < (1536L << 20) ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "Windows has only {0:F1} GB of memory left to give other programs (its page file is very small), so opening more programs may fail. " +
                        "Use a smaller model, or let Windows manage the page file size (System > About > Advanced system settings > Performance > Advanced > Virtual memory).",
                        commit / 1073741824.0) : null;
                    if (_memWarning != null) Log.Text("  WARNING: " + _memWarning);
                    // Remember it so the next start opens the same model (only for models inside models\).
                    if (string.Equals(Path.GetDirectoryName(path), _modelsDir, StringComparison.OrdinalIgnoreCase))
                        try { File.WriteAllText(Path.Combine(_modelsDir, "last-model.txt"), Path.GetFileName(path)); } catch { }
                    Log.Json("model_load", "\"file\":" + Json.Quote(_loading) + ",\"model\":" + Json.Quote(_engine.ModelName) + ",\"backend\":" +
                        Json.Quote(_engine.Backend) + ",\"context\":" + _engine.ContextLength + ",\"ms\":" + ChatLog.Ms(sw.Elapsed.TotalSeconds));
                }
                catch (Exception ex)
                {
                    _loadError = ex.Message;
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
                        sb.Append("{\"name\":").Append(Json.Quote(Path.GetFileName(f))).Append(",\"mb\":").Append(new FileInfo(f).Length / 1048576)
                          .Append(",\"kvq\":").Append(SupportsQuantKv(f) ? "true" : "false").Append('}');
                    }
                }
                SendJson(res, 200, sb.Append("],\"root\":").Append(Json.Quote(Workspace.ProjectsRoot)).Append("}").ToString());
                return;
            }
            if (path == "/api/load" && post)
            {
                string name = Json.GetStr(body, "name", null);
                if (name == null || name.IndexOfAny(new char[] { '/', '\\', ':' }) >= 0) throw new ArgumentException("Give a model file name from the models folder.");
                // context: tokens, or 0 / missing = auto
                int ctx = (int)Json.GetNum(body, "context", ContextLength);
                ContextLength = ctx >= 256 ? ctx : 0;
                LoadModel(name);
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/recent-folders")
            {
                StringBuilder sb = new StringBuilder("{\"folders\":[");
                List<string> list = RecentFolders();
                // Then every project in the projects root that is not already listed.
                try
                {
                    foreach (string d in Directory.GetDirectories(Workspace.ProjectsRoot))
                    {
                        string f = "/" + Path.GetFileName(d);
                        if (!list.Exists(delegate(string x) { return string.Equals(x, f, StringComparison.OrdinalIgnoreCase); })) list.Add(f);
                    }
                }
                catch { }
                for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json.Quote(list[i])); }
                SendJson(res, 200, sb.Append("]}").ToString());
                return;
            }
            if (path == "/api/folder-access" && post)
            {
                bool on = body.ContainsKey("on") && body["on"] is bool && (bool)body["on"];
                _folderAccess = on;
                if (!on) _workspace = null;
                try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(FolderAccessFile, on ? "on" : "off"); } catch { }
                Log.Text("folder access " + (on ? "ON" : "OFF (folder disconnected, file tools disabled)"));
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/folder" && post)
            {
                string folder = Json.GetStr(body, "path", "");
                if (folder.Length > 0 && !_folderAccess) throw new InvalidOperationException("Folder access is off. Turn it on with /folder on.");
                _workspace = folder.Length == 0 ? null : new Workspace(folder);
                if (_workspace != null) RememberFolder(_workspace.Display);
                ChatEngine eRec = _engine;
                if (eRec != null) eRec.RecallDir = _workspace != null ? Path.Combine(_workspace.Root, ".phoenix", "recall") : null;
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
                    if (b != "edge" && b != "chrome" && b != "auto") throw new ArgumentException("Browser must be auto, chrome or edge.");
                    Directory.CreateDirectory(AppDataDir);
                    File.WriteAllText(BrowserFile, b);
                    Log.Text("browser set to " + b);
                }
                SendJson(res, 200, BrowserJson());
                return;
            }
            if (path == "/api/vram") { SendJson(res, 200, VramProcessesJson()); return; }
            if (path == "/api/kv" && post)
            {
                string kv = Json.GetStr(body, "kv", "q8").ToLowerInvariant();
                KvType t = kv == "f16" ? KvType.F16 : kv == "q4" ? KvType.Q4 : KvType.Q8;
                bool changed = t != ChatEngine.KvCache;
                ChatEngine.KvCache = t;
                try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(KvFile, KernelsKv.Name(t)); } catch { }
                Log.Text("KV cache: " + KernelsKv.Name(t));
                ChatEngine cur = _engine;
                if (changed && cur != null) LoadModel(cur.ModelPath);   // the cache is allocated at load
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/recall" && post)
            {
                // /recall: pre-read every text file of the connected project (Instant Recall), newest first. Runs in the
                // background; a chat message stops it (it holds the engine only one file at a time).
                if (_workspace == null) throw new InvalidOperationException("Connect a project first: /folder <project_folder>.");
                if (_engine == null) throw new InvalidOperationException("No model loaded.");
                if (Interlocked.CompareExchange(ref _recallBusy, 1, 0) != 0) { SendJson(res, 200, "{\"busy\":true}"); return; }
                Workspace wsR = _workspace;
                Thread t = new Thread(delegate()
                {
                    int done = 0, saved = 0; Stopwatch rsw = Stopwatch.StartNew();
                    try
                    {
                        List<string> files = new List<string>(Directory.GetFiles(wsR.Root, "*", SearchOption.AllDirectories));
                        files.RemoveAll(delegate(string f) { string r = wsR.Relative(f); return r.StartsWith(".") || r.Contains("/.") || new FileInfo(f).Length > 256 * 1024; });
                        files.Sort(delegate(string a, string b) { return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)); });
                        foreach (string f in files)
                        {
                            if (_recallStop) break;
                            string text;
                            try { text = wsR.Read(wsR.Relative(f)); } catch { continue; }
                            if (text.IndexOf('\0') >= 0) continue;
                            _recallProgress = (done + 1) + "/" + files.Count + " " + wsR.Relative(f);
                            lock (_engineLock)
                            {
                                ChatEngine e = _engine;
                                if (e == null || _recallStop) break;
                                if (e.PreRead(text, delegate { return _recallStop; })) saved++;
                            }
                            done++;
                        }
                        Log.Text("recall: pre-read " + saved + " new file(s) of " + done + " in " + ChatLog.Ms(rsw.Elapsed.TotalSeconds) + " ms" + (_recallStop ? " (stopped by a chat)" : ""));
                    }
                    catch (Exception ex) { Log.Text("recall: FAILED " + ex.Message); }
                    finally { _recallProgress = null; _recallStop = false; _recallBusy = 0; }
                });
                t.IsBackground = true; t.Priority = ThreadPriority.BelowNormal; t.Start();
                Log.Text("recall: pre-reading " + wsR.Display);
                SendJson(res, 200, "{\"started\":true}");
                return;
            }
            if (path == "/api/mode" && post)
            {
                _acceptMode = Json.GetStr(body, "mode", "auto").ToLowerInvariant() == "accept";
                try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(ModeFile, _acceptMode ? "accept" : "auto"); } catch { }
                Log.Text("mode: " + (_acceptMode ? "accept (file actions need approval)" : "auto"));
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/approve" && post)
            {
                int aid = (int)Json.GetNum(body, "id", -1);
                bool allow = body.ContainsKey("allow") && body["allow"] is bool && (bool)body["allow"];
                lock (_approvals)
                {
                    KeyValuePair<ManualResetEvent, bool> p;
                    if (_approvals.TryGetValue(aid, out p)) { _approvals[aid] = new KeyValuePair<ManualResetEvent, bool>(p.Key, allow); p.Key.Set(); }
                }
                SendJson(res, 200, "{}");
                return;
            }
            if (path == "/api/device" && post)
            {
                // Switches CPU/GPU and reloads the current model there (the chat history stays; its cache is rebuilt).
                bool gpu = Json.GetStr(body, "device", "gpu").ToLowerInvariant() != "cpu";
                if (!gpu && !CpuModeAllowed) throw new InvalidOperationException("CPU mode is turned off for now; models run on the GPU.");
                _useGpu = gpu;
                try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(DeviceFile, gpu ? "gpu" : "cpu"); } catch { }
                Log.Text("device: " + (gpu ? "GPU" : "CPU"));
                ChatEngine cur = _engine;
                if (cur != null) LoadModel(cur.ModelPath);
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/unload" && post)
            {
                // Frees the model's VRAM (weights + context cache); /model loads one again.
                lock (_engineLock) { if (_engine != null) { Log.Text("unloaded " + Path.GetFileName(_engine.ModelPath)); _engine.Dispose(); _engine = null; MemoryTrim.Trim(); } }
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
            if (!_folderAccess) throw new InvalidOperationException("Folder access is off. Turn it on with /folder on.");
            if (_workspace == null) throw new InvalidOperationException("No folder connected. Use /folder <project_folder>.");
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
            Workspace w = _folderAccess ? _workspace : null;
            ChatEngine er = _engine;
            return "{\"recall\":{\"files\":" + (er != null ? er.RecallFiles : 0) + ",\"mb\":" + (er != null ? er.RecallBytes >> 20 : 0) +
                   ",\"busy\":" + (_recallProgress != null ? Json.Quote(_recallProgress) : "null") + "},\"mode\":\"" + (_acceptMode ? "accept" : "auto") + "\",\"device\":" + (_useGpu ? "\"gpu\"" : "\"cpu\"") + ",\"kv\":\"" + KernelsKv.Name(ChatEngine.KvCache) + "\",\"folderAccess\":" + (_folderAccess ? "true" : "false") + ",\"model\":" + (e != null ? Json.Quote(e.ModelName) : "null") +
                   ",\"file\":" + (e != null ? Json.Quote(Path.GetFileName(e.ModelPath)) : "null") +
                   ",\"backend\":" + (e != null ? Json.Quote(e.Backend) : "null") +
                   ",\"context\":" + (e != null ? e.ContextLength : ContextLength) + ",\"contextAuto\":" + (ContextLength <= 0 ? "true" : "false") +
                   ",\"loading\":" + (_loading != null ? Json.Quote(_loading) : "null") +
                   ",\"loadError\":" + (_loadError != null ? Json.Quote(_loadError) : "null") +
                   ",\"memWarning\":" + (_memWarning != null ? Json.Quote(_memWarning) : "null") +
                   ",\"folder\":" + (w != null ? Json.Quote(w.Display) : "null") +
                   ",\"projectsRoot\":" + Json.Quote(Workspace.ProjectsRoot) +
                   ",\"folderName\":" + (w != null ? Json.Quote(Path.GetFileName(w.Root)) : "null") +
                   ",\"git\":" + (w != null ? w.GitJson() : "null") +
                   ",\"rules\":" + RulesJson() +
                   ",\"rag\":" + Rag.StatusJson() +
                   ",\"vram\":" + VramJson(e) + ",\"ram\":" + MemoryTrim.RamJson() +
                   ",\"version\":" + Json.Quote(Version()) + "}";
        }

        // App version from src\__version.info (read on each request, so editing it needs no restart).
        string Version()
        {
            try { return File.ReadAllText(Path.Combine(Directory.GetParent(_htmlDir).FullName, "src", "__version.info")).Trim(); }
            catch { return ""; }
        }

        // "used" = this process on the GPU (model, cache and docs-search embedder: the driver reports usage per process),
        // "budget" = what the GPU can give this process right now, its own use included (drops when others take VRAM).
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
            object writeLock = new object();
            TextCallback write = delegate(string line)
            {
                lock (writeLock)
                {
                    if (gone) return;
                    try
                    {
                        byte[] b = Encoding.UTF8.GetBytes("data: " + line + "\n\n");
                        outS.Write(b, 0, b.Length);
                        outS.Flush();
                    }
                    catch { gone = true; } // browser closed or pressed Stop
                }
            };
            // A Stop is only noticed when a write fails, and nothing is written while a long prompt is read: a comment
            // line every second keeps checking (SSE clients ignore lines starting with ':').
            System.Threading.Timer heartbeat = new System.Threading.Timer(delegate
            {
                lock (writeLock)
                {
                    if (gone) return;
                    try { byte[] b = Encoding.UTF8.GetBytes(": ping\n\n"); outS.Write(b, 0, b.Length); outS.Flush(); }
                    catch { gone = true; }
                }
            }, null, 1000, 1000);

            if (_recallBusy != 0) _recallStop = true;   // a chat takes priority over the /recall pre-read
            lock (_engineLock)
            {
                try
                {
                    if (_engine == null) throw new InvalidOperationException("No model loaded. Use /models and /model <n>.");
                    // Progress while the prompt is read (shown until the first token; matters on the CPU).
                    int lastPct = -1;
                    _engine.PrefillProgress = delegate(int done, int todo)
                    {
                        int pct = todo > 0 ? done * 100 / todo : 100;
                        if (pct / 5 != lastPct / 5) { lastPct = pct; write("{\"pf\":" + pct + "}"); }
                    };
                    Workspace ws = _folderAccess ? _workspace : null;
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

                    // Code requests: every step is generated out of sight ("w" events, a collapsed box in the UI), the code is
                    // then checked against the syntax reference, and only the checked (or corrected) reply is shown.
                    bool codeMode = Rag.IsCodeRequest(lastUser);
                    bool nudged = false;
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
                        int toolEnd = -1;   // end of the first <read>/<list> tag: the model must wait for its result
                        st = _engine.Generate(msgs, s, maxTokens, delegate(string piece)
                        {
                            if (ttft < 0) ttft = stepWatch.Elapsed.TotalSeconds;
                            if (toolEnd >= 0) return;
                            reply.Append(piece);
                            write("{\"t\":" + Json.Quote(piece) + "}");
                            if (ws != null) toolEnd = Workspace.QueryTagEnd(reply.ToString());
                        }, delegate { return gone || toolEnd >= 0; });
                        // Anything after a read/list tag would be invented results; the real ones follow in the next step.
                        if (toolEnd >= 0 && toolEnd < reply.Length) reply.Length = toolEnd;
                        double stepSec = stepWatch.Elapsed.TotalSeconds;
                        Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "#{0} ASSISTANT step {1}: first token {2} ms | prompt {3} new{14} + {4} cached tokens in {5} ms ({6:F1} tok/s) | " +
                            "generated {7} tokens in {8} ms ({9:F1} tok/s) | step {10} ms | stop: {11}{12}\n{13}",
                            rid, step, ttft < 0 ? "-" : ChatLog.Ms(ttft), st.PromptTokens, st.ReusedTokens, ChatLog.Ms(st.PromptSeconds), st.PromptTokensPerSecond,
                            st.GeneratedTokens, ChatLog.Ms(st.GenerateSeconds), st.TokensPerSecond, ChatLog.Ms(stepSec), st.StopReason,
                            gone ? " (client disconnected / stopped)" : "", reply.ToString(),
                            st.RecalledTokens > 0 ? " (" + st.RecalledTokens + " of them recalled, not read)" : ""));
                        Log.Json("reply", "\"id\":" + rid + ",\"step\":" + step + ",\"ttft_ms\":" + (ttft < 0 ? "null" : ChatLog.Ms(ttft)) +
                            ",\"prompt_tokens\":" + st.PromptTokens + ",\"cached_tokens\":" + st.ReusedTokens + ",\"prompt_ms\":" + ChatLog.Ms(st.PromptSeconds) +
                            ",\"gen_tokens\":" + st.GeneratedTokens + ",\"gen_ms\":" + ChatLog.Ms(st.GenerateSeconds) +
                            ",\"tok_s\":" + Json.Num(Math.Round(st.TokensPerSecond, 1)) + ",\"stop\":" + Json.Quote(st.StopReason) +
                            ",\"client_gone\":" + (gone ? "true" : "false") + ",\"text\":" + Json.Quote(reply.ToString()));
                        totalTokens += st.GeneratedTokens;
                        genSeconds += st.GenerateSeconds;
                        string replyText = reply.ToString();
                        // Code requests: the reply streamed normally; its code is now checked against the syntax reference
                        // (a collapsed "Checking…" line under it) before any file is written. A correction replaces the
                        // reply on screen ("replace" event).
                        if (!gone && HasCode(replyText))   // every reply with code is checked, whatever the request looked like
                        {
                            GenerationStats cst;
                            string checkedText = CheckCode(rid, msgs, replyText, s, maxTokens, write, delegate { return gone; }, out cst);
                            totalTokens += cst.GeneratedTokens;
                            genSeconds += cst.GenerateSeconds;
                            if (checkedText != replyText) { replyText = checkedText; write("{\"replace\":" + Json.Quote(replyText) + "}"); }
                        }
                        msgs.Add(new ChatMessage("assistant", replyText));
                        if (append.Length > 1) append.Append(',');
                        append.Append("{\"role\":\"assistant\",\"content\":").Append(Json.Quote(replyText)).Append('}');
                        if (ws == null || gone) break;

                        // Accept mode: every list / read / write waits for the user's Accept or Deny (an "approve" event; the
                        // answer comes back on /api/approve). A closed window or Stop counts as Deny.
                        Func<Workspace.Action, bool> approve = null;
                        if (_acceptMode)
                            approve = delegate(Workspace.Action a)
                            {
                                int aid = Interlocked.Increment(ref _approvalId);
                                ManualResetEvent ev = new ManualResetEvent(false);
                                lock (_approvals) _approvals[aid] = new KeyValuePair<ManualResetEvent, bool>(ev, false);
                                write("{\"approve\":{\"id\":" + aid + ",\"op\":" + Json.Quote(a.Op) + ",\"path\":" + Json.Quote(a.Path) +
                                      ",\"bytes\":" + (a.Content != null ? Encoding.UTF8.GetByteCount(a.Content) : 0) + "}}");
                                Log.Text("#" + rid + " APPROVAL asked: " + a.Op + " " + a.Path);
                                while (!ev.WaitOne(500)) if (gone) break;
                                bool ok;
                                lock (_approvals) { ok = _approvals[aid].Value && !gone; _approvals.Remove(aid); }
                                ev.Dispose();
                                Log.Text("#" + rid + " APPROVAL " + (ok ? "accepted" : "denied") + ": " + a.Op + " " + a.Path);
                                return ok;
                            };
                        List<Workspace.Action> actions = ws.Execute(replyText, approve);
                        if (actions.Count == 0)
                        {
                            // The model said it would look at / change files but used no tag ("First, let me check index.html.")
                            // and the turn would just end: tell it once to go ahead.
                            if (!nudged && AnnouncesAction(replyText))
                            {
                                nudged = true;
                                string go = "Go ahead: do it now with the tags (<list>, <read>, <write>) in your reply.";
                                Log.Text("#" + rid + " NUDGE: announced a file action without a tag");
                                msgs.Add(new ChatMessage("user", go));
                                append.Append(",{\"role\":\"user\",\"content\":").Append(Json.Quote(go)).Append('}');
                                continue;
                            }
                            break;
                        }
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
                                if (a.Op == "read") _engine.RegisterRecall(a.Content);   // Instant Recall for this file text
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
                        _loadError = "GPU reset by the driver";
                        Log.Text("#" + rid + " GPU device lost; reloading " + Path.GetFileName(file));
                        ThreadPool.QueueUserWorkItem(delegate { try { LoadModel(file); } catch { } });
                    }
                }
            }
            heartbeat.Dispose();
            lock (writeLock) { try { outS.Close(); } catch { } }
        }

        // "let me check index.html", "I will start by reading ...", "I'll now update ...": an announced file action.
        static readonly System.Text.RegularExpressions.Regex AnnounceRe = new System.Text.RegularExpressions.Regex(
            @"\b(let me|i will|i'll|i am going to|i'm going to|first,? i|now i will|next,? i)\b[^.\n]{0,60}\b(check|read|look at|open|list|see|inspect|create|write|update|modify|edit|add|change)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        static bool AnnouncesAction(string reply)
        {
            string tail = reply.Length > 400 ? reply.Substring(reply.Length - 400) : reply;   // the announcement ends the reply
            return AnnounceRe.IsMatch(tail) && !HasCode(reply);
        }

        static bool HasCode(string reply)
        {
            return reply.IndexOf("```", StringComparison.Ordinal) >= 0 || reply.IndexOf("<write", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Second pass for a reply with code: the model checks its own code against the syntax-reference sections that
        // match the code and answers LOOKS GOOD or with the corrected reply. Streams "w" events and the phase.
        // Returns the reply to show: the draft when it passed (or the check went off the rails), else the correction.
        string CheckCode(int rid, List<ChatMessage> msgs, string draft, SamplerSettings s, int maxTokens, TextCallback write, CancelCheck cancel, out GenerationStats cst)
        {
            write("{\"phase\":\"checking\",\"lang\":" + Json.Quote(Rag.LanguageName(draft)) + "}");
            Stopwatch sw = Stopwatch.StartNew();
            // Certain problems (missing usings) are fixed in code first; the model then judges the fixed version.
            List<string> lint = new List<string>();
            draft = CodeLint.FixUsings(draft, lint);
            if (lint.Count > 0) write("{\"w\":" + Json.Quote("\n\n— fixed: " + string.Join(", ", lint.ToArray()) + " —") + "}");
            string code = CodeOf(draft);
            // Only sections that are not already in the conversation, at most two: the ones sent with the request are
            // still in the model's cache, so re-sending them would cost seconds of prompt reading (and push Gemma's
            // sliding-window cache past what the next step can reuse).
            List<RagHit> hits = new List<RagHit>();
            if (Rag.Enabled)
            {
                string sent = "";
                foreach (ChatMessage m in msgs) if (m.Role == "user") sent += m.Text;
                foreach (RagHit h in Rag.Search(code, null))
                    if (hits.Count < 2 && sent.IndexOf(h.Text.Trim(), StringComparison.Ordinal) < 0) hits.Add(h);
            }
            bool hadSections = msgs.Exists(delegate(ChatMessage m) { return m.Role == "user" && m.Text.StartsWith("Reference sections from my syntax library"); });
            StringBuilder p = new StringBuilder("Check the code in your reply above for mistakes: syntax errors, wrong type or method names, " +
                "wrong API usage, and logic errors" + (hadSections ? ", using the reference sections in my message where they apply" : "") +
                ". Do not change anything that is already correct, and do not use <read> or <list>.");
            if (hits.Count > 0)
            {
                p.Append(" These further reference sections may also apply:\n\n");
                for (int i = 0; i < hits.Count; i++)
                    p.Append('[').Append(i + 1).Append("] ").Append(hits[i].Title).Append('\n').Append(hits[i].Text.Trim()).Append("\n\n");
            }
            else p.Append("\n\n");
            p.Append("If everything is correct, reply with exactly: LOOKS GOOD\n" +
                     "Otherwise reply with your complete corrected reply in the same format (including any <write> tags) and nothing else.");
            List<ChatMessage> check = new List<ChatMessage>(msgs);
            check.Add(new ChatMessage("assistant", draft));
            check.Add(new ChatMessage("user", p.ToString()));
            write("{\"w\":" + Json.Quote("Checking against " + (hadSections ? "the reference sections sent with the request" : "the code alone") +
                (hits.Count > 0 ? " and " + hits.Count + " more" : "") + (lint.Count > 0 ? "; fixed first: " + string.Join(", ", lint.ToArray()) : "") + ".\n\n") + "}");
            SamplerSettings cs = s.Clone();
            cs.Temperature = Math.Min(cs.Temperature, 0.2f);
            StringBuilder verdict = new StringBuilder();
            cst = _engine.Generate(check, cs, maxTokens, delegate(string piece)
            {
                verdict.Append(piece);
                write("{\"w\":" + Json.Quote(piece) + "}");
            }, cancel);
            string v = verdict.ToString().Trim();
            bool good = v.TrimStart('*', '"', '`', ' ').StartsWith("LOOKS GOOD", StringComparison.OrdinalIgnoreCase);
            // A correction must keep the reply's form: code, and no file queries (those would start another round).
            bool fixedIt = !good && HasCode(v) && Workspace.QueryTagEnd(v) < 0;
            if (fixedIt) v = CodeLint.FixUsings(v, new List<string>());   // the rewrite may drop usings again
            // Verdict for the status line: good (green) = passed; fixed (gold) = the check found and corrected possible
            // mistakes; wrong (red) = definite syntax errors (found by CodeLint) that were corrected; unsure (gold) = the
            // check gave no clear answer and the code was kept. ref: whether syntax-reference sections were used.
            string outcome = lint.Count > 0 ? "wrong" : good ? "good" : fixedIt ? "fixed" : "unsure";
            write("{\"phase\":\"" + outcome + "\",\"ref\":" + (hits.Count > 0 || hadSections ? "true" : "false") + "}");
            Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "#{0} CHECK {1} in {2} ms ({3} reference sections, lint {4}; prompt {5} new + {6} cached tokens, {7} generated){8}",
                rid, good ? "looks good" : fixedIt ? "corrected the code" : "unclear verdict, kept the draft", ChatLog.Ms(sw.Elapsed.TotalSeconds),
                hits.Count, lint.Count == 0 ? "clean" : string.Join(" ", lint.ToArray()), cst.PromptTokens, cst.ReusedTokens, cst.GeneratedTokens, good ? "" : "\n" + v));
            return fixedIt ? v : draft;
        }

        static bool IsCSharp(string reply, string code)
        {
            return System.Text.RegularExpressions.Regex.IsMatch(reply, @"```\s*(csharp|cs|c#)\b|<write\s+path\s*=\s*""[^""]*\.cs""", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
                   System.Text.RegularExpressions.Regex.IsMatch(code, @"\busing System\b|\bnamespace\s+\w|\bpublic\s+(static\s+)?(class|void|async)\b");
        }

        // The code in a reply (fenced blocks and <write> bodies) as the reference-search query.
        static string CodeOf(string reply)
        {
            StringBuilder sb = new StringBuilder();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(reply,
                "```[^\\n]*\\n([\\s\\S]*?)```|<write[^>]*>([\\s\\S]*?)</write>"))
                sb.Append(m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value : m.Groups[2].Value).Append('\n');
            string code = sb.Length > 0 ? sb.ToString() : reply;
            return code.Length > 3000 ? code.Substring(0, 3000) : code;
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
            StopApi();
            Rag.Dispose();
        }
    }
}
