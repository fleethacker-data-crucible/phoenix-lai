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
        IChatModel _engine;
        Workspace _workspace;
        string _loading;
        string _loadedSig;   // what the loaded engine was loaded with (LoadModel skips an identical reload)
        string _loadError;
        string _memWarning;  // set after a load that left Windows almost out of commit memory   // last model load / GPU failure, shown red in the status bar; cleared by the next good load
        public int ContextLength = 0;   // 0 = auto (ContextPlanner picks it at load); the loaded model's is _engine.ContextLength
        public readonly int Port;
        public readonly ChatLog Log;
        public readonly Rag Rag;
        int _requestId, _clientChats;

        // IsClient: the Phoenix Client (run-phoenix-client.ps1, ports 8790-8799): the chat page with every feature
        // (projects, file tools, docs search, skills, code check, compact), its model calls going to the active OpenAI /v1
        // provider (RemoteChatModel) and docs-search embeddings to the Phoenix server's /v1/embeddings. No engine, no GPU.
        // Otherwise the Phoenix server (run-phoenix-server.ps1): the local engine, the /v1 API, dashboard and console.
        public readonly bool IsClient;

        public WebServer(string htmlDir, string modelsDir, string logsDir, int port) : this(htmlDir, modelsDir, logsDir, port, false) { }

        public WebServer(string htmlDir, string modelsDir, string logsDir, int port, bool client)
        {
            IsClient = client;
            _htmlDir = Path.GetFullPath(htmlDir);
            _modelsDir = Path.GetFullPath(modelsDir);
            Port = port;
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            _listener.Start();
            Log = new ChatLog(logsDir, client ? "client" : "session");
            Dbg.Attach(Log, AppDataDir);
            Workspace.EnsureProjectsRoot();
            Rag = new Rag(Path.Combine(Directory.GetParent(_htmlDir).FullName, "docs"), client ? null : _modelsDir);
            if (client) Rag.EmbedderFactory = delegate { return new RemoteEmbedder(Providers.Find(Providers.PhoenixId)); };
            CodeCheck.PsDataPath = Path.Combine(Directory.GetParent(_htmlDir).FullName, "docs", "checkers", "ps51-commands.tsv");
            // An outdated or missing index is rebuilt in the background (otherwise the docs look empty and the model is
            // never told about them). The GPU embedder submits one layer per job and holds GpuGate, so it cannot collide
            // with a model upload (the 2026-10-16 driver reset); it falls back to the CPU on device loss.
            if (Rag.NeedsRebuild || Rag.HasUnindexedDocs())
            {
                Log.Text("rag: index is missing or outdated; rebuilding");
                Rag.StartReindex(delegate(string m) { Log.Text(m); });
            }
            Log.Text((client ? "Phoenix Client started on " : "server started on ") + Url);
            if (!client) StartApi();
            Log.Json("server_start", "\"url\":" + Json.Quote(Url));
            Thread t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        public string Url { get { return "http://127.0.0.1:" + Port + "/chat.html"; } }
        public string BaseUrl { get { return "http://127.0.0.1:" + Port + "/"; } }

        // The Phoenix Client (run-phoenix-client.ps1): opens its window and runs until it is closed. One client per user:
        // a second launch brings the open window forward. The mutex is freed by Windows if we crash.
        public static int RunClient(string root)
        {
            Application.EnableVisualStyles();
            bool first;
            using (Mutex single = new Mutex(true, @"Local\PhoenixAI.Client", out first))
            {
                if (!first)
                {
                    EventWaitHandle show;
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out show)) using (show) show.Set();
                    return 0;
                }
                try { return RunServer(root, null, 0); }
                finally { single.ReleaseMutex(); }
            }
        }

        const string ShowEventName = @"Local\PhoenixAI.ClientShow";

        // The VS Code extension's own Phoenix Client (run-phoenix-vscode.ps1, started by the extension; approved 2026-10-22):
        // the same client (same providers, keys, settings and chats on disk) on its own port, with no window of its own.
        // It is independent of the Phoenix Client window and stays up while a VS Code chat ("vs-" pages) checks in; one per
        // user (VS Code windows share it). The extension finds it by /api/info "instance":"vscode".
        public static int RunVsCode(string root)
        {
            bool first;
            using (Mutex single = new Mutex(true, @"Local\PhoenixAI.ClientVsCode", out first))
            {
                if (!first) return 0;
                try
                {
                    using (WebServer server = StartOnFreePort(Path.Combine(root, "html"), Path.Combine(root, "models"), Path.Combine(root, "logs"), true))
                    {
                        server.Embedded = true;
                        server.Log.Text("VS Code client: no window, runs while a VS Code chat is connected");
                        ThreadPool.QueueUserWorkItem(delegate { try { server.UseProviderModel(null); } catch (Exception ex) { server.Log.Text("provider: " + ex.Message); } });
                        // up to 2 minutes for the first VS Code chat to check in, then until the last one has been gone 30 s
                        DateTime start = DateTime.UtcNow;
                        while (server.VsCodePages() == 0 && DateTime.UtcNow - start < TimeSpan.FromMinutes(2)) Thread.Sleep(500);
                        DateTime lastSeen = DateTime.UtcNow;
                        while (DateTime.UtcNow - lastSeen < TimeSpan.FromSeconds(30))
                        {
                            if (server.VsCodePages() > 0) lastSeen = DateTime.UtcNow;
                            Thread.Sleep(500);
                        }
                        try { if (server._pindex != null) server._pindex.Dispose(); } catch { }   // saves the project index
                        server.Log.Text("VS Code client stopped (no VS Code chat connected)");
                        server.Log.Json("server_stop", null);
                    }
                    return 0;
                }
                finally { single.ReleaseMutex(); }
            }
        }

        // True for the VS Code extension's own client (RunVsCode).
        public bool Embedded;

        // VS Code chats ("vs-" pages) that checked in within the last 10 s.
        int VsCodePages()
        {
            lock (_pages)
            {
                int n = 0;
                foreach (KeyValuePair<string, DateTime> kv in _pages)
                    if (kv.Key.StartsWith("vs-") && DateTime.UtcNow - kv.Value < TimeSpan.FromSeconds(10)) n++;
                return n;
            }
        }

        // Opens the app window on `url` in the preferred browser; returns the browser process (or null if none found).
        internal static Process OpenWindow(string url, ChatLog log)
        {
            string pref = PreferredBrowser(), used;
            string exe = FindBrowser(pref, out used);
            if (exe == null) return null;
            // A private profile folder per browser keeps the app separate from the user's own browser windows.
            string profile = Path.Combine(AppDataDir, used + "-profile");
            Directory.CreateDirectory(profile);
            if (log != null) log.Text("opening " + used + (used != pref ? " (" + pref + " is not installed)" : "") + ": " + exe);
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

        // The client's window loop: start, open the window, run while any window is open, then shut down.
        static int RunServer(string root, string modelPath, int ctx)
        {
            using (WebServer server = StartOnFreePort(Path.Combine(root, "html"), Path.Combine(root, "models"), Path.Combine(root, "logs"), true))
            {
                server.ContextLength = ctx;
                // the active provider's saved model, so the first message needs no picking
                ThreadPool.QueueUserWorkItem(delegate { try { server.UseProviderModel(null); } catch (Exception ex) { server.Log.Text("provider: " + ex.Message); } });
                // Another launch of the app: bring the open window forward, or open one if every window was closed.
                EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                Thread showThread = new Thread(delegate()
                {
                    while (show.WaitOne())
                    {
                        if (server.OpenPages() > 0) { server._focusRequested = true; server.Log.Text("launched again: focusing the open window"); }
                        else { server.Log.Text("launched again: no window open, opening one"); Process p = OpenWindow(server.Url, server.Log); if (p != null) p.Dispose(); }
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
                    Process browser = OpenWindow(server.Url, server.Log);
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
                            "Phoenix LAI", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                try { if (server._pindex != null) server._pindex.Dispose(); } catch { }   // saves the project index
                lock (server._engineLock) { if (server._engine != null) server._engine.Dispose(); server._engine = null; }
                server.Log.Text("server stopped");
                server.Log.Json("server_stop", null);
            }
            return 0;
        }

        internal static WebServer StartOnFreePort(string htmlDir, string modelsDir, string logsDir) { return StartOnFreePort(htmlDir, modelsDir, logsDir, false); }

        // The server takes 8765-8789, the Phoenix Client 8790-8799.
        internal static WebServer StartOnFreePort(string htmlDir, string modelsDir, string logsDir, bool client)
        {
            int from = client ? 8790 : 8765, to = client ? 8800 : 8790;
            for (int port = from; port < to; port++)
            {
                try { return new WebServer(htmlDir, modelsDir, logsDir, port, client); }
                catch (HttpListenerException) { }
            }
            throw new InvalidOperationException("No free local port between " + from + " and " + (to - 1) + ".");
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
        // CPU mode is OFF (2026-10-20): models run on the GPU only; there are no CPU settings anywhere. The CPU-mode code is
        // commented out, not deleted (search "CPU mode (off)"); set CpuModeAllowed = true and uncomment it to bring it back.
        // The engine's own fallback to the CPU when a GPU load fails is unchanged.
        public const bool CpuModeAllowed = false;
        bool _useGpu = true;
        // CPU mode (off): bool _useGpu = !CpuModeAllowed || ReadUseGpu();

        static bool ReadUseGpu()
        {
            try { return !File.Exists(DeviceFile) || File.ReadAllText(DeviceFile).Trim() != "cpu"; } catch { return true; }
        }

        // Why the engine cannot run a model ("" when it can), read from its header once per file version: the same
        // config checks a load does (e.g. the Gemma 4 26B mixture-of-experts model is refused).
        static readonly Dictionary<string, string> _unsupportedCache = new Dictionary<string, string>();
        static string Unsupported(string file)
        {
            string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks;
            lock (_unsupportedCache)
            {
                string why;
                if (_unsupportedCache.TryGetValue(key, out why)) return why;
                try
                {
                    using (GgufFile g = GgufFile.Open(file))
                    {
                        if (!GemmaConfig.IsGemma(g.GetString("general.architecture"))) throw new NotSupportedException("Not a Gemma 4 model (PhoenixLAI runs Gemma 4 models).");
                        GemmaConfig.FromGguf(g);
                        // every weight must be in a format the engine runs (the GPU path's formats)
                        GgmlType[] ok = new GgmlType[] { GgmlType.F32, GgmlType.F16, GgmlType.BF16, GgmlType.Q4_0, GgmlType.Q8_0, GgmlType.Q4_K, GgmlType.Q5_K, GgmlType.Q6_K };
                        why = "";
                        foreach (GgufTensor t in g.Tensors)
                            if (Array.IndexOf(ok, t.Type) < 0) { why = t.Name + " is " + GgmlTypes.Name(t.Type) + ", a weight format the server cannot run."; break; }
                    }
                }
                catch (Exception ex) { why = ex.Message; }
                _unsupportedCache[key] = why;
                return why;
            }
        }

        // The least memory a load of the file needs (ContextPlanner.MinBytes), per file version and device.
        static readonly Dictionary<string, long[]> _needCache = new Dictionary<string, long[]>();
        static long[] Need(string file, bool gpu)
        {
            string key = file + "|" + File.GetLastWriteTimeUtc(file).Ticks + "|" + gpu;
            lock (_needCache)
            {
                long[] r;
                if (_needCache.TryGetValue(key, out r)) return r;
                long commit;
                using (GgufFile g = GgufFile.Open(file)) r = new long[] { ContextPlanner.MinBytes(g, file, gpu, out commit), commit };
                _needCache[key] = r;
                return r;
            }
        }

        // Total VRAM this process may use (read once from the driver; 0 = unknown).
        static long _vramTotal = -1;
        static long VramTotal()
        {
            if (_vramTotal >= 0) return _vramTotal;
            long t = 0;
            try { using (GpuDevice d = GpuDevice.Create()) { ulong u, b; if (d.MemoryBudget(out u, out b)) t = (long)b; } } catch { }
            return _vramTotal = t;
        }

        // Dedicated VRAM held by other programs (what a model switch cannot free); -1 when Windows does not report it.
        static long OtherVram()
        {
            try
            {
                int self = Process.GetCurrentProcess().Id;
                long sum = 0;
                InstanceDataCollection data = new PerformanceCounterCategory("GPU Process Memory").ReadCategory()["Dedicated Usage"];
                foreach (InstanceData d in data.Values)
                {
                    string inst = d.InstanceName;
                    int us = inst.IndexOf('_', 4), pid;
                    if (!inst.StartsWith("pid_") || us < 0 || !int.TryParse(inst.Substring(4, us - 4), out pid) || pid == self) continue;
                    sum += d.RawValue;
                }
                return sum;
            }
            catch { return -1; }
        }

        // Memory free for a model to load now: VRAM not held by other programs (GPU) or available RAM (CPU), and Windows
        // commit; what the loaded model holds counts as free, since loading another one releases it first.
        void FreeForModel(out long mem, out long commit)
        {
            IChatModel e = _engine;
            long[] cur = null;
            if (e != null) try { cur = Need(e.ModelPath, _useGpu); } catch { }
            commit = MemoryTrim.AvailableCommit() + (cur != null ? cur[1] - (3072L << 20) : 0);
            if (_useGpu)
            {
                long total = VramTotal(), other = OtherVram();
                mem = total > 0 && other >= 0 ? total - other : long.MaxValue;
            }
            else mem = MemoryTrim.AvailableRam();
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

        // A message's text: "content" as a string, or the joined "text" parts of an OpenAI-style content array.
        public static string TextOf(Dictionary<string, object> m)
        {
            object v;
            if (m == null || !m.TryGetValue("content", out v)) return "";
            return PartsText(v, "\n", "");
        }

        // Content given as a string, or the "text" parts of a list of parts joined by sep. A part with no "type"
        // counts as text when untypedType is "text" (the OpenAI API), and is skipped when it is "" (the chat page).
        static string PartsText(object c, string sep, string untypedType)
        {
            if (c is string) return (string)c;
            List<object> parts = c as List<object>;
            if (parts == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (object o in parts)
            {
                Dictionary<string, object> part = o as Dictionary<string, object>;
                if (part != null && Json.GetStr(part, "type", untypedType) == "text") { if (sb.Length > 0) sb.Append(sep); sb.Append(Json.GetStr(part, "text", "")); }
            }
            return sb.ToString();
        }

        // Images of a message: "images": ["data:image/png;base64,...", ...] (the chat page), or OpenAI-style content parts
        // {"type": "image_url", "image_url": {"url": "data:..."}}. Only inline data: URIs; nothing is ever downloaded.
        public static List<byte[]> ParseImages(Dictionary<string, object> m)
        {
            List<string> urls = new List<string>();
            object v;
            if (m.TryGetValue("images", out v) && v is List<object>)
                foreach (object o in (List<object>)v) if (o is string) urls.Add((string)o);
            if (m.TryGetValue("content", out v) && v is List<object>)
                foreach (object o in (List<object>)v)
                {
                    Dictionary<string, object> part = o as Dictionary<string, object>;
                    if (part == null || Json.GetStr(part, "type", "") != "image_url") continue;
                    object iu;
                    if (part.TryGetValue("image_url", out iu))
                    {
                        if (iu is string) urls.Add((string)iu);
                        else if (iu is Dictionary<string, object>) urls.Add(Json.GetStr((Dictionary<string, object>)iu, "url", ""));
                    }
                }
            if (urls.Count == 0) return null;
            List<byte[]> r = new List<byte[]>();
            foreach (string u in urls)
            {
                int comma = u.IndexOf(',');
                if (!u.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || comma < 0 || u.IndexOf(";base64", 0, comma, StringComparison.OrdinalIgnoreCase) < 0)
                    throw new ArgumentException("Images must be sent inline (data:image/...;base64); links are never fetched.");
                byte[] b = Convert.FromBase64String(u.Substring(comma + 1));
                if (b.Length > (40 << 20)) throw new ArgumentException("Image larger than 40 MB.");
                r.Add(b);
            }
            if (r.Count > 8) throw new ArgumentException("At most 8 images per message.");
            return r;
        }

        // Loads a model from models\ (by file name) or a full local path; replaces the current one.
        public void LoadModel(string nameOrPath)
        {
            string path = Path.IsPathRooted(nameOrPath) ? nameOrPath : Path.Combine(_modelsDir, nameOrPath);
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) throw new FileNotFoundException("Model not found: " + nameOrPath);
            // Never load the same model twice: a request that would load exactly what is already loaded (same file,
            // context, device), e.g. a second click or a request queued behind a load in progress, is a no-op.
            string sig = path.ToLowerInvariant() + "|" + ContextLength + "|" + _useGpu;
            lock (_engineLock)
            {
                if (_engine != null && _loadedSig == sig) { Log.Text(Path.GetFileName(path) + " is already loaded"); return; }
                _loading = Path.GetFileName(path);
                Stopwatch sw = Stopwatch.StartNew();
                Log.Text("loading model " + _loading + " (context " + (ContextLength > 0 ? ContextLength.ToString() : "auto") + ")");
                try
                {
                    if (_engine != null) { _engine.Dispose(); _engine = null; MemoryTrim.Trim(); }   // free the old one first
                    _loadedSig = null;
                    _loadError = null;
                    _engine = ChatEngine.Load(path, ContextLength, _useGpu, delegate(string m) { Log.Text("  " + m); });
                    _loadedSig = sig;
                    if (_workspace != null) _engine.RecallDir = Path.Combine(ProjectRootOf(_workspace) ?? _workspace.Root, ".phoenix", "recall");
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
        long _lastWindowTicks = DateTime.UtcNow.Ticks;   // last check-in of an app window (not the VS Code chat)
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
                // the chat inside VS Code (page ids starting "vs-") is not the app window: a second launch still opens one
                int n = 0;
                foreach (string k in _pages.Keys) if (!k.StartsWith("vs-")) n++;
                return n;
            }
        }

        void WaitWhilePagesActive(TimeSpan idle)
        {
            Thread.Sleep(3000);   // give a new window time to load and check in
            // Only the app window keeps the server up: closing it shuts the server down, even while the VS Code chat or
            // another API client is connected (they reconnect when it is started again). The 5 s grace covers a reload.
            while (OpenPages() > 0 || DateTime.UtcNow.Ticks - Interlocked.Read(ref _lastWindowTicks) < TimeSpan.FromSeconds(5).Ticks)
                Thread.Sleep(500);
        }

        void Handle(HttpListenerContext ctx)
        {
            Interlocked.Increment(ref _activeRequests);
            Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
            Stopwatch dsw = Dbg.On ? Stopwatch.StartNew() : null;
            try { HandleRequest(ctx); }
            finally
            {
                Interlocked.Exchange(ref _lastRequestTicks, DateTime.UtcNow.Ticks);
                Interlocked.Decrement(ref _activeRequests);
                if (dsw != null)
                {
                    // the pages poll /api/info and /api/vram every second or two: those only when slow or failing
                    string p = ctx.Request.Url.AbsolutePath; int code = 0;
                    try { code = ctx.Response.StatusCode; } catch { }
                    bool poll = p == "/api/info" || p == "/api/vram";
                    if (!poll || code != 200 || dsw.ElapsedMilliseconds > 250)
                        Dbg.Msg("http", ctx.Request.HttpMethod + " " + ctx.Request.Url.PathAndQuery + " -> " + code + " in " + dsw.ElapsedMilliseconds + " ms");
                }
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
                // HEAD (readiness checks): status only. A HEAD response may not carry a body, so Send would throw.
                if (req.HttpMethod == "HEAD") { res.StatusCode = host == "127.0.0.1:" + Port ? 200 : 403; res.ContentLength64 = 0; res.OutputStream.Close(); return; }
                if (host != "127.0.0.1:" + Port) { Send(res, 403, "text/plain", "Forbidden"); return; }
                string origin = req.Headers["Origin"];
                if (origin != null && origin != "http://127.0.0.1:" + Port) { Send(res, 403, "text/plain", "Forbidden"); return; }

                string path = req.Url.AbsolutePath;
                if (path.StartsWith("/api/") || (IsClient && path.StartsWith("/client/")))
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
                if (req.HttpMethod == "GET")
                {
                    // the server's own pages are the dashboard and the console; the chat window is the Phoenix Client
                    if (!IsClient && (path == "/chat.html" || path == "/vscode.html"))
                    { Send(res, 404, "text/plain; charset=utf-8", "The chat runs in the Phoenix Client now (PhoenixLAI_Client.lnk or src\\run-phoenix-client.ps1)."); return; }
                    ServeFile(res, path == "/" ? (IsClient ? "/chat.html" : "/dashboard.html") : path);
                    return;
                }
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
            if (IsClient)
            {
                if (ServerOnlyPath(path)) { SendJson(res, 404, "{\"error\":\"That is a Phoenix server setting (its dashboard or console), not the client's.\"}"); return; }
                string cr = ClientApi(path, post, body);
                if (cr != null) { SendJson(res, 200, cr); return; }
            }
            else if (ClientOnlyPath(path)) { SendJson(res, 404, "{\"error\":\"The chat runs in the Phoenix Client now (run-phoenix-client.ps1).\"}"); return; }
            string chatsReply = ChatsApi(path, post, body, req.QueryString["id"]);
            if (chatsReply != null) { SendJson(res, 200, chatsReply); return; }
            string skillsReply = SkillsApi(path, post, body, req.QueryString);
            if (skillsReply != null) { SendJson(res, 200, skillsReply); return; }
            if (path == "/api/skill-assist" && post)
            {
                res.StatusCode = 200; res.ContentType = "text/event-stream; charset=utf-8"; res.Headers["Cache-Control"] = "no-store"; res.SendChunked = true;
                SkillAssist(body, res.OutputStream);
                return;
            }
            if (path == "/api/compact" && post)
            {
                res.StatusCode = 200; res.ContentType = "text/event-stream; charset=utf-8"; res.Headers["Cache-Control"] = "no-store"; res.SendChunked = true;
                Compact(body, res.OutputStream);
                return;
            }
            if (page != null && !page.StartsWith("dash-")) SeeClient("page:" + page, page.StartsWith("vs-") ? "vscode" : "client", page.StartsWith("vs-") ? "VS Code chat" : "Phoenix Client", path);
            if (path == "/api/server-status") { SendJson(res, 200, ServerStatusJson()); return; }
            if (path == "/api/console" && post) { SendJson(res, 200, "{\"text\":" + Json.Quote(ConsoleRun(Json.GetStr(body, "line", ""))) + "}"); return; }
            if (path == "/api/bye") { if (page != null) lock (_pages) _pages.Remove(page); SendJson(res, 200, "{}"); return; }
            if (path == "/api/info")
            {
                if (page != null && page.Length <= 40) lock (_pages) _pages[page] = DateTime.UtcNow;
                if (page != null && !page.StartsWith("vs-")) Interlocked.Exchange(ref _lastWindowTicks, DateTime.UtcNow.Ticks);
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
                    long freeMem = -1, freeCommit = 0;
                    try { FreeForModel(out freeMem, out freeCommit); } catch { freeMem = -1; }
                    files.Sort(StringComparer.OrdinalIgnoreCase);
                    foreach (string f in files)
                    {
                        if (!IsListedModel(Path.GetFileName(f))) continue;   // no vision projectors (mmproj), no embedding model
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"name\":").Append(Json.Quote(Path.GetFileName(f))).Append(",\"mb\":").Append(new FileInfo(f).Length / 1048576)
                          .Append(",\"kvq\":false")   // Gemma keeps an fp16 cache: the page offers no q8 / q4
                          .Append(",\"defaults\":").Append(SystemPrompts.DefaultsJson(Path.GetFileName(f)));
                        string why = Unsupported(f);
                        if (why.Length > 0) sb.Append(",\"unsupported\":").Append(Json.Quote(why));
                        else if (freeMem >= 0)
                            try
                            {
                                // runnable but too big for the memory free now (smallest context): the page shows it red
                                long[] need = Need(f, _useGpu);
                                if (need[0] > freeMem || need[1] > freeCommit)
                                    sb.Append(",\"tooBig\":").Append(Json.Quote(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                        need[0] > freeMem ? "needs {0:F1} GB of {2}, {1:F1} GB free" : "needs {0:F1} GB of Windows commit, {1:F1} GB free",
                                        (need[0] > freeMem ? need[0] : need[1]) / 1073741824.0, (need[0] > freeMem ? freeMem : freeCommit) / 1073741824.0, _useGpu ? "VRAM" : "RAM")));
                            }
                            catch { }
                        sb.Append('}');
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
                SwitchProjectIndex();
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
                if (_workspace != null) _workspace.Outline = delegate(string f) { ProjectIndex pi = _pindex; return pi != null ? pi.Outline(f) : null; };
                if (_workspace != null) { RememberFolder(_workspace.Display); EnsureProjectLayout(ProjectRootOf(_workspace)); }
                SwitchProjectIndex();
                IChatModel eRec = _engine;
                if (eRec != null) eRec.RecallDir = _workspace != null ? Path.Combine(ProjectRootOf(_workspace) ?? _workspace.Root, ".phoenix", "recall") : null;
                Log.Text(_workspace != null ? "folder connected: " + _workspace.Root : "folder disconnected");
                Log.Json("folder", "\"path\":" + (_workspace != null ? Json.Quote(_workspace.Root) : "null"));
                SendJson(res, 200, Info());
                return;
            }
            if (path == "/api/undo" && post)
            {
                string msg = RequireWorkspace().Undo(Json.GetStr(body, "path", ""), Json.GetStr(body, "backup", null));
                Log.Text(msg);
                Log.Json("undo", "\"msg\":" + Json.Quote(msg));
                SendJson(res, 200, "{\"msg\":" + Json.Quote(msg) + "}");
                return;
            }
            if (path == "/api/list") { SendJson(res, 200, "{\"entries\":" + RequireWorkspace().ListJson(q ?? ".") + "}"); return; }
            if (path == "/api/ls") { Log.Text("/ls " + (q ?? ".")); SendJson(res, 200, "{\"text\":" + Json.Quote(RequireWorkspace().List(q ?? ".")) + "}"); return; }
            if (path == "/api/cat") { Log.Text("/cat " + (q ?? "")); SendJson(res, 200, "{\"text\":" + Json.Quote(RequireWorkspace().Read(q ?? "")) + "}"); return; }
            if (path == "/api/chat" && post)
            {
                Dbg.Source = page != null && page.StartsWith("vs-") ? "vscode" : "window";
                string who = Dbg.Source == "vscode" ? "VS Code chat" : "Phoenix Client";
                int arid = -Interlocked.Increment(ref _clientChats);   // its own ids: Chat() numbers its log lines
                ActivityStart(arid, who, "chat");
                try { Chat(body, res); } finally { ActivityEnd(arid, who, "chat", null, "done"); }
                return;
            }
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
            if (path == "/api/gpu")
            {
                // /gpu [auto|amd|nvidia] [safe|fast]: which GPU and how carefully to drive it (GpuDevice.ReadProfile); used
                // from the next model load
                string gp; bool gs;
                GpuDevice.ReadProfile(out gp, out gs);
                if (post)
                {
                    string p2 = Json.GetStr(body, "profile", gp).Trim().ToLowerInvariant();
                    string s2 = Json.GetStr(body, "safe", gs ? "on" : "off").Trim().ToLowerInvariant();
                    GpuDevice.WriteProfile(p2, s2 == "on" || s2 == "true" || s2 == "safe");
                    GpuDevice.ReadProfile(out gp, out gs);
                    Log.Text("GPU profile set to " + gp + (gs ? " safe" : "") + " (used from the next model load)");
                }
                IChatModel ge = _engine;
                SendJson(res, 200, "{\"profile\":" + Json.Quote(gp) + ",\"safe\":" + (gs ? "true" : "false") + ",\"file\":" + Json.Quote(GpuDevice.ProfileFile) +
                    ",\"backend\":" + (ge != null ? Json.Quote(ge.Backend) : "null") + "}");
                return;
            }
            if (path == "/api/vram") { SendJson(res, 200, VramProcessesJson()); return; }
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
                    IDisposable awake = StayAwake.Scope("Instant Recall");   // displays must not power down mid-load (StayAwake)
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
                                IChatModel e = _engine;
                                if (e == null || _recallStop) break;
                                Stopwatch fsw = Stopwatch.StartNew();
                                if (e.PreRead(text, delegate { return _recallStop; })) saved++;
                                Dbg.Msg("recall", "pre-read " + wsR.Relative(f) + " (" + text.Length + " chars) in " + fsw.ElapsedMilliseconds + " ms");
                            }
                            done++;
                        }
                        Log.Text("recall: pre-read " + saved + " new file(s) of " + done + " in " + ChatLog.Ms(rsw.Elapsed.TotalSeconds) + " ms" + (_recallStop ? " (stopped by a chat)" : ""));
                    }
                    catch (Exception ex) { Log.Text("recall: FAILED " + ex.Message); }
                    finally { awake.Dispose(); _recallProgress = null; _recallStop = false; _recallBusy = 0; }
                });
                t.IsBackground = true; t.Priority = ThreadPriority.BelowNormal; t.Start();
                Log.Text("recall: pre-reading " + wsR.Display);
                Dbg.Msg("recall", "background pre-read thread started");
                SendJson(res, 200, "{\"started\":true}");
                return;
            }
            if (path == "/api/pretype" && post) { StartPretype(body); SendJson(res, 200, "{}"); return; }
            if (path == "/api/debug" && post)
            {
                Dbg.Set(body != null && body.ContainsKey("on") && body["on"] is bool && (bool)body["on"]);
                SendJson(res, 200, "{\"debug\":" + (Dbg.On ? "true" : "false") + "}");
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
                // CPU mode (off): models run on the GPU only, so there is nothing to switch.
                if (Json.GetStr(body, "device", "gpu").ToLowerInvariant() == "cpu") throw new InvalidOperationException("CPU mode is turned off; models run on the GPU.");
                SendJson(res, 200, Info());
                // CPU mode (off): switched CPU/GPU and reloaded the current model there (the chat history stayed; its cache was rebuilt).
                // bool gpu = Json.GetStr(body, "device", "gpu").ToLowerInvariant() != "cpu";
                // if (!gpu && !CpuModeAllowed) throw new InvalidOperationException("CPU mode is turned off for now; models run on the GPU.");
                // _useGpu = gpu;
                // try { Directory.CreateDirectory(AppDataDir); File.WriteAllText(DeviceFile, gpu ? "gpu" : "cpu"); } catch { }
                // Log.Text("device: " + (gpu ? "GPU" : "CPU"));
                // IChatModel cur = _engine;
                // if (cur != null) LoadModel(cur.ModelPath);
                // SendJson(res, 200, Info());
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

        // RULES.md files that apply right now: the app's own (next to src\ and html\) and the active project's (its root).
        List<KeyValuePair<string, string>> RulesFiles()
        {
            List<KeyValuePair<string, string>> found = new List<KeyValuePair<string, string>>();
            string appRules = Path.Combine(Directory.GetParent(_htmlDir).FullName, "RULES.md");
            if (File.Exists(appRules)) found.Add(new KeyValuePair<string, string>("app", appRules));
            Workspace w = _workspace;
            if (w != null)
            {
                string folderRules = Path.Combine(ProjectRootOf(w) ?? w.Root, "RULES.md");   // the project's RULES.md, even in a subfolder
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
                try { text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(f.Value, Encoding.UTF8), @"<!--[\s\S]*?-->", "").Trim(); } catch { continue; }
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
            IChatModel e = _engine;
            Workspace w = _folderAccess ? _workspace : null;
            IChatModel er = _engine;
            return "{\"instance\":" + (Embedded ? "\"vscode\"" : IsClient ? "\"client\"" : "\"server\"") + ",\"debug\":" + (Dbg.On ? "true" : "false") + ",\"recall\":{\"files\":" + (er != null ? er.RecallFiles : 0) + ",\"mb\":" + (er != null ? er.RecallBytes >> 20 : 0) +
                   ",\"busy\":" + (_recallProgress != null ? Json.Quote(_recallProgress) : "null") + "},\"mode\":\"" + (_acceptMode ? "accept" : "auto") + "\",\"device\":" + (_useGpu ? "\"gpu\"" : "\"cpu\"") + ",\"folderAccess\":" + (_folderAccess ? "true" : "false") + ",\"model\":" + (e != null ? Json.Quote(e.ModelName) : "null") +
                   ",\"file\":" + (e != null ? Json.Quote(Path.GetFileName(e.ModelPath)) : "null") +
                   ",\"backend\":" + (e != null ? Json.Quote(e.Backend) : "null") +
                   ",\"vision\":" + (e != null && e.HasVision ? "true" : "false") +
                   ",\"user\":" + Json.Quote(Environment.UserName) + ",\"machine\":" + Json.Quote(Environment.MachineName) +
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
                   ",\"version\":" + Json.Quote(Version()) + (IsClient ? ProviderInfoJson() : "") + "}";
        }

        string Version() { return AppVersion(Directory.GetParent(_htmlDir).FullName); }

        // The one app version: <project root>\__version.info, for the server, the client and every page (read on each
        // request, so editing it needs no restart). "" when the file is missing.
        public static string AppVersion(string root)
        {
            try { return File.ReadAllText(Path.Combine(root, "__version.info")).Trim(); }
            catch { return ""; }
        }

        // "used" = this process on the GPU (model, cache and docs-search embedder: the driver reports usage per process),
        // "budget" = what the GPU can give this process right now, its own use included (drops when others take VRAM).
        static string VramJson(IChatModel e)
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
                sb.Append("{\"name\":").Append(Json.Quote(kv.Key == self ? name + " (Phoenix LAI)" : name)).Append(",\"pid\":").Append(kv.Key).Append(",\"bytes\":").Append(kv.Value).Append('}');
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

        // POST /api/chat {messages:[{role,content}], temperature, top_k, top_p, repeat_penalty, max_tokens, skills (default true)}
        // Server-sent events: {"t":text} tokens, {"tool":{op,path,ok,msg}} file actions, {"step":n} a new model turn
        // after tool results, then {"done":true, append:[messages to add to history], ...stats}.
        void Chat(Dictionary<string, object> o, HttpListenerResponse res)
        {
            if (o == null || !o.ContainsKey("messages")) throw new ArgumentException("messages required");
            List<ChatMessage> msgs = new List<ChatMessage>();
            foreach (object m in (List<object>)o["messages"])
            {
                Dictionary<string, object> mm = m as Dictionary<string, object>;
                string role = Json.GetStr(mm, "role", "user"), content = TextOf(mm);
                if (role != "system" && role != "user" && role != "assistant") continue;
                ChatMessage cm = new ChatMessage(role, content);
                if (role == "user") cm.Images = ParseImages(mm);
                msgs.Add(cm);
            }
            bool anyImage = msgs.Exists(delegate(ChatMessage x) { return x.Images != null; });
            try { EnsureClientModel(); } catch { }   // the client: the active provider's model, before checking it can read pictures
            if (anyImage && (_engine == null || !_engine.HasVision))
                throw new ArgumentException("The loaded model cannot read images: it needs its vision file (mmproj) in the models folder. Gemma 4 E2B, E4B and 12B have one.");
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
            IChatModel engNow = _engine; Workspace wsNow = _workspace;
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
            // Requests to run something: the model answers the first two (it cannot run anything; it gives the command),
            // the third gets a warning from the server, the fourth suspends the chat for 15 minutes (ShellGuard).
            string guardLevel; int guardSeconds;
            string guard = ShellGuard.CheckRunRequest(lastUser, out guardLevel, out guardSeconds);
            if (guard != null)
            {
                Log.Text("#" + rid + " RUN GUARD: " + guard);
                Log.Json("run_guard", "\"id\":" + rid + ",\"msg\":" + Json.Quote(guard));
                IChatModel eg = _engine;
                write("{\"guard\":{\"level\":" + Json.Quote(guardLevel) + ",\"seconds\":" + guardSeconds + ",\"text\":" + Json.Quote(guard) + "}}");
                write("{\"t\":" + Json.Quote(guard) + "}");
                write("{\"done\":true,\"append\":[{\"role\":\"assistant\",\"content\":" + Json.Quote(guard) + "}],\"tokens\":0,\"tps\":0" +
                      (eg != null ? ",\"ctx_used\":" + eg.UsedTokens + ",\"ctx_size\":" + eg.ContextLength : "") + "}");
                try { outS.Close(); } catch { }
                return;
            }
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
            if (_pretypeBusy != 0) _pretypeStop = true; // ...and over a typing-pause draft
            lock (_engineLock)
            {
                try
                {
                    EnsureClientModel();   // the client: the active provider's saved model
                    if (_engine == null) throw new InvalidOperationException("No model selected: pick one from the model list at the top (/model).");
                    // Progress while the prompt is read (shown until the first token; matters on the CPU).
                    int lastPct = -1;
                    _engine.PrefillProgress = delegate(int done, int todo)
                    {
                        int pct = todo > 0 ? done * 100 / todo : 100;
                        if (pct / 5 != lastPct / 5) { lastPct = pct; write("{\"pf\":" + pct + "}"); }
                    };
                    // no project files or file tools for a provider that gets no local context
                    Workspace ws = _folderAccess && LocalContextAllowed ? _workspace : null;
                    List<SkillMatch> skillsUsed;
                    List<RagHit> hits = PrepareMessages(msgs, ws, rid, true, !(o.ContainsKey("skills") && o["skills"] is bool && !(bool)o["skills"]), out skillsUsed);
                    if (hits.Count > 0) write("{\"rag\":" + HitsJson(hits, false) + "}");
                    if (skillsUsed.Count > 0)
                    {
                        StringBuilder sk = new StringBuilder("{\"skills\":[");
                        for (int i = 0; i < skillsUsed.Count; i++)
                            sk.Append(i > 0 ? "," : "").Append("{\"name\":").Append(Json.Quote(skillsUsed[i].Skill.Name)).Append(",\"scope\":").Append(Json.Quote(skillsUsed[i].Skill.Scope))
                              .Append(",\"called\":").Append(skillsUsed[i].Explicit ? "true" : "false").Append('}');
                        write(sk.Append("]}").ToString());
                    }

                    // Code requests: every step is generated out of sight ("w" events, a collapsed box in the UI), the code is
                    // then checked against the syntax reference, and only the checked (or corrected) reply is shown.
                    bool codeMode = Rag.IsCodeRequest(lastUser);
                    bool nudged = false;
                    StringBuilder append = new StringBuilder("[");
                    List<string> changedFiles = new List<string>();   // files the model wrote, edited or deleted (re-indexed at the end)
                    Dictionary<string, int> failures = new Dictionary<string, int>();   // failed tool calls this turn (loop guard)
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
                            if (ttft < 0) { ttft = stepWatch.Elapsed.TotalSeconds; if (step == 0) Log.Text("#" + rid + " first token " + ChatLog.Ms(total.Elapsed.TotalSeconds) + " ms after the request arrived (prepare " + ChatLog.Ms(total.Elapsed.TotalSeconds - ttft) + " ms)"); }
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
                            string checkedText = CheckCode(rid, msgs, replyText, hits, lastUser, s, maxTokens, write, delegate { return gone; }, out cst);
                            totalTokens += cst.GeneratedTokens;
                            genSeconds += cst.GenerateSeconds;
                            // A rewrite whose <edit> would not apply to the file (it quoted lines that are not there) never
                            // replaces a reply whose edit does apply: the original is kept and the line says it is unconfirmed.
                            if (checkedText != replyText && ws != null)
                            {
                                string bad = ws.EditProblem(checkedText);
                                if (bad != null && ws.EditProblem(replyText) == null)
                                {
                                    Log.Text("#" + rid + " CHECK rewrite discarded (its edit would fail: " + bad + "); kept the original reply");
                                    write("{\"phase\":\"unsure\"}");
                                    checkedText = replyText;
                                }
                            }
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
                                string go = "Go ahead: do it now with the tags (<list>, <search>, <read>, <edit>, <write>, <delete>) in your reply.";
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
                            if (a.Ok && (a.Op == "write" || a.Op == "edit" || a.Op == "delete") && ws != null)
                                try { changedFiles.Add(ws.Resolve(a.Path)); } catch { }
                            write("{\"tool\":{\"op\":" + Json.Quote(a.Op) + ",\"path\":" + Json.Quote(a.Path) + ",\"ok\":" + (a.Ok ? "true" : "false") +
                                  ",\"msg\":" + Json.Quote(a.Message) + "}}");
                            Log.Text("#" + rid + " TOOL " + (a.Ok ? "ok   " : "FAIL ") + a.Message);
                            Log.Json("tool", "\"id\":" + rid + ",\"op\":" + Json.Quote(a.Op) + ",\"path\":" + Json.Quote(a.Path) +
                                     ",\"ok\":" + (a.Ok ? "true" : "false") + ",\"msg\":" + Json.Quote(a.Message));
                            results.Append("- ").Append(a.Message).Append('\n');
                            if (a.Content != null)
                            {
                                if (a.Op == "read" && a.To == 0 && !a.OutlineOnly) _engine.RegisterRecall(a.Content);   // Instant Recall for this file text (whole files only)
                                results.Append("```\n").Append(a.Content).Append("\n```\n");
                                needsAnswer = true;
                            }
                            if (!a.Ok) needsAnswer = true;
                        }
                        // Reads, listings and failures go back to the model; plain successful writes end the turn.
                        if (!needsAnswer) break;
                        // The same failure twice in one turn: the model is going in circles, so stop and say so.
                        bool repeated = false;
                        foreach (Workspace.Action a in actions)
                        {
                            if (a.Ok) continue;
                            string sig = a.Op + "|" + a.Path + "|" + (a.Message.Length > 80 ? a.Message.Substring(0, 80) : a.Message);
                            int seen; failures.TryGetValue(sig, out seen); failures[sig] = seen + 1;
                            if (seen + 1 >= 2) repeated = true;
                        }
                        if (repeated)
                        {
                            string note = "Stopped: the same change failed twice (" + actions.Find(delegate(Workspace.Action x) { return !x.Ok; }).Message.Split('\n')[0] +
                                          "). Ask again, or say which lines to change.";
                            Log.Text("#" + rid + " LOOP " + note);
                            write("{\"tool\":{\"op\":\"stop\",\"path\":\"\",\"ok\":false,\"msg\":" + Json.Quote(note) + "}}");
                            break;
                        }
                        msgs.Add(new ChatMessage("user", results.ToString()));
                        append.Append(",{\"role\":\"user\",\"content\":").Append(Json.Quote(results.ToString())).Append('}');
                    }
                    append.Append(']');
                    string indexed = IndexModelChanges(changedFiles, rid);
                    if (indexed != null) write(indexed);
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

        // System additions (RULES.md, folder tools, docs-library note) and the docs excerpts for the newest user message,
        // exactly as a chat request builds them, so a typing-pause draft fills the same prompt prefix the real request
        // will reuse. Returns the docs sections added (log: write the RULES / RAG log lines).
        List<RagHit> PrepareMessages(List<ChatMessage> msgs, Workspace ws, int rid, bool log)
        {
            List<SkillMatch> skillsUsed;
            return PrepareMessages(msgs, ws, rid, log, true, out skillsUsed);
        }

        // The active provider may be sent local context: always on the server; in the client, only providers that allow it
        // (Provider.SendsLocalContext: the Phoenix Local Server for now).
        bool LocalContextAllowed { get { return !IsClient || Providers.Active.SendsLocalContext; } }

        // useSkills false: no skill catalog or skill text (the chat request's "skills": false, used by the RAG evaluation).
        List<RagHit> PrepareMessages(List<ChatMessage> msgs, Workspace ws, int rid, bool log, bool useSkills, out List<SkillMatch> skillsUsed)
        {
            skillsUsed = new List<SkillMatch>();
            if (!LocalContextAllowed)
            {
                // A provider that gets no local context: only the system prompt and what the user typed. No RULES.md, docs
                // sections, syntax sheets, project files, file tools or skills leave this PC.
                if (!msgs.Exists(delegate(ChatMessage m) { return m.Role == "system" && m.Text != null && m.Text.Trim().Length > 0; }))
                {
                    msgs.RemoveAll(delegate(ChatMessage m) { return m.Role == "system"; });
                    msgs.Insert(0, new ChatMessage("system", SystemPrompts.For(_engine != null ? Path.GetFileName(_engine.ModelPath) : null)));
                }
                const string noLocal = "You have no access to the user's files, folders or documents: never use file tags. When the user " +
                    "asks for code, a script or any other file, write it in a code block in your reply (complete, ready to copy).";
                int ni = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                msgs[ni] = new ChatMessage("system", msgs[ni].Text + "\n\n" + noLocal);
                if (log) Log.Text("#" + rid + " LOCAL CONTEXT not sent: " + Providers.Active.Name + " gets only what the user typed");
                return new List<RagHit>();
            }
            // what the user typed, before docs and project excerpts are added to it (skills match only this)
            int oui = msgs.FindLastIndex(delegate(ChatMessage m) { return m.Role == "user"; });
            string userText = oui >= 0 ? msgs[oui].Text : "";
            List<RagHit> hits = new List<RagHit>();
            // The chat page sends the model's system prompt (its default or the user's edit). Requests without one (API,
            // VS Code) get the default: short replies keep each turn fast and within the cache's rewind buffer (GemmaGpu.SwaSlack).
            int bi = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system" && m.Text != null && m.Text.Trim().Length > 0; });
            if (bi < 0)
            {
                msgs.RemoveAll(delegate(ChatMessage m) { return m.Role == "system"; });
                msgs.Insert(0, new ChatMessage("system", SystemPrompts.For(_engine != null ? Path.GetFileName(_engine.ModelPath) : null)));
            }
            List<KeyValuePair<string, string>> rulesFiles = RulesFiles();
            string rules = RulesPrompt(rulesFiles);
            if (rules != null)
            {
                int ri = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                if (ri >= 0) msgs[ri] = new ChatMessage("system", rules + "\n" + msgs[ri].Text);
                else msgs.Insert(0, new ChatMessage("system", rules));
                if (log)
                {
                    List<string> names = new List<string>();
                    foreach (KeyValuePair<string, string> f in rulesFiles) names.Add(f.Key + " " + f.Value);
                    Log.Text("#" + rid + " RULES " + string.Join(" + ", names.ToArray()));
                }
            }
            if (ws != null)
            {
                // Tool instructions go into the system message (added if the client sent none).
                int si = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                string tools = ws.ToolInstructions();
                if (si >= 0) msgs[si] = new ChatMessage("system", msgs[si].Text + "\n\n" + tools);
                else msgs.Insert(0, new ChatMessage("system", tools));
            }
            else
            {
                // No project active: files can only be made inside a project, so the model sends the user to create one.
                const string noProject = "No project is active, so you cannot create or save files: never use file tags. When the user " +
                    "asks for code, a script, a page or any other file, write it in a code block in your reply (complete, ready to copy). " +
                    "When they asked for a file to be created or saved, end with one short sentence saying that to save it as a file they " +
                    "create or select a project first (the + next to Projects in the sidebar, then click the project).";
                int si = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                if (si >= 0) msgs[si] = new ChatMessage("system", msgs[si].Text + "\n\n" + noProject);
                else msgs.Insert(0, new ChatMessage("system", noProject));
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
            int pui = msgs.FindLastIndex(delegate(ChatMessage m) { return m.Role == "user"; });
            string projQuery = pui >= 0 && ws != null ? msgs[pui].Text : null;
            if (Rag.Enabled)
            {
                int ui = msgs.FindLastIndex(delegate(ChatMessage m) { return m.Role == "user"; });
                if (ui >= 0)
                {
                    int pi = ui > 0 ? msgs.FindLastIndex(ui - 1, delegate(ChatMessage m) { return m.Role == "user"; }) : -1;
                    Stopwatch rsw = Stopwatch.StartNew();
                    hits = Rag.Search(msgs[ui].Text, pi >= 0 ? msgs[pi].Text : null);
                    if (hits.Count == 0 && libraryNote != null && Rag.IsLibraryQuestion(msgs[ui].Text))
                    { List<byte[]> keep = msgs[ui].Images; msgs[ui] = new ChatMessage("user", msgs[ui].Text + "\n\n(No sections of my document library match this; it has nothing on it.)"); msgs[ui].Images = keep; }
                    if (hits.Count > 0)
                    {
                        List<byte[]> keepImg = msgs[ui].Images;
                        msgs[ui] = new ChatMessage("user", Rag.WrapMessage(hits, msgs[ui].Text));
                        msgs[ui].Images = keepImg;
                        if (log)
                        {
                            List<string> srcs = new List<string>();
                            foreach (RagHit h in hits) srcs.Add(h.File + (h.Title.Length > 0 ? " › " + h.Title : "") + " (" + h.Score.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ")");
                            Log.Text("#" + rid + " RAG " + hits.Count + " chunks in " + ChatLog.Ms(rsw.Elapsed.TotalSeconds) + " ms: " + string.Join("; ", srcs.ToArray()));
                            Log.Json("rag", "\"id\":" + rid + ",\"ms\":" + ChatLog.Ms(rsw.Elapsed.TotalSeconds) + ",\"hits\":" + HitsJson(hits, false));
                        }
                    }
                }
            }
            AddProjectHits(msgs, pui, projQuery, rid, log);
            // Skills: the catalog goes into the system prompt, the matched skills in front of the newest user message (last,
            // so they come before the docs and project excerpts).
            List<Skill> skills = null;
            try { if (useSkills) skills = AllSkills(); } catch (Exception ex) { if (log) Log.Text("#" + rid + " SKILLS could not be read: " + ex.Message); }
            string catalog = skills != null ? Skills.CatalogNote(skills) : null;
            if (catalog != null)
            {
                int si3 = msgs.FindIndex(delegate(ChatMessage m) { return m.Role == "system"; });
                if (si3 >= 0) msgs[si3] = new ChatMessage("system", msgs[si3].Text + "\n\n" + catalog);
                else msgs.Insert(0, new ChatMessage("system", catalog));
                int ui2 = msgs.FindLastIndex(delegate(ChatMessage m) { return m.Role == "user"; });
                if (ui2 >= 0)
                {
                    skillsUsed = Skills.Match(skills, userText);
                    if (skillsUsed.Count > 0)
                    {
                        List<byte[]> keepImg2 = msgs[ui2].Images;
                        msgs[ui2] = new ChatMessage("user", Skills.Wrap(skillsUsed, msgs[ui2].Text));
                        msgs[ui2].Images = keepImg2;
                        if (log)
                        {
                            List<string> used = new List<string>();
                            foreach (SkillMatch m in skillsUsed) used.Add(m.Skill.Scope + " " + m.Skill.Name + (m.Explicit ? " (called)" : " (matched " + string.Join(", ", m.Terms.ToArray()) + ")"));
                            Log.Text("#" + rid + " SKILLS " + string.Join("; ", used.ToArray()));
                        }
                    }
                }
            }
            return hits;
        }

        // ---- answer while typing ----
        // On a typing pause the page sends the half-written message (/api/pretype). If the engine is free, the prompt is
        // prepared and read exactly as the real request would be, and a short greedy draft answer is generated. On Enter
        // the real request reuses the read prompt (prefix cache) and the draft serves as a source of guesses for
        // speculative decoding (only guesses the model itself would sample are kept). A real request stops a running
        // draft at once (_pretypeStop, checked every token and prompt chunk).
        public static bool AnswerWhileTyping = true;
        int _pretypeBusy;
        volatile bool _pretypeStop;
        const int PretypeDraftTokens = 160;

        void StartPretype(Dictionary<string, object> o)
        {
            if (!AnswerWhileTyping || _engine == null || o == null || !o.ContainsKey("messages")) return;
            if (Interlocked.CompareExchange(ref _pretypeBusy, 1, 0) != 0) return;
            List<ChatMessage> msgs = new List<ChatMessage>();
            foreach (object m in (List<object>)o["messages"])
            {
                Dictionary<string, object> mm = m as Dictionary<string, object>;
                string role = Json.GetStr(mm, "role", "user"), content = Json.GetStr(mm, "content", "");
                if (role == "system" || role == "user" || role == "assistant") msgs.Add(new ChatMessage(role, content));
            }
            _pretypeStop = false;
            Thread t = new Thread(delegate()
            {
                try
                {
                    Dbg.Source = "answer-while-typing";
                    if (!Monitor.TryEnter(_engineLock, 50)) return;   // a chat or load is running: no draft
                    try
                    {
                        ChatEngine e = _engine as ChatEngine;   // drafting needs the local engine's cache (not through /v1)
                        if (e == null || _pretypeStop) return;
                        PrepareMessages(msgs, _folderAccess ? _workspace : null, 0, false);
                        SamplerSettings s = new SamplerSettings(); s.Temperature = 0;
                        e.PrefillProgress = null;
                        Stopwatch sw = Stopwatch.StartNew();
                        GenerationStats st = e.Generate(msgs, s, PretypeDraftTokens, null, delegate { return _pretypeStop; });
                        if (!_pretypeStop) e.Hint = e.LastReplyIds;
                        Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "pretype: read {0} new prompt tokens, drafted {1} tokens in {2} ms{3}", st.PromptTokens, st.GeneratedTokens,
                            ChatLog.Ms(sw.Elapsed.TotalSeconds), _pretypeStop ? " (stopped by a send)" : ""));
                    }
                    finally { Monitor.Exit(_engineLock); }
                }
                catch (Exception ex) { Log.Text("pretype: " + ex.Message); }
                finally { _pretypeBusy = 0; }
            });
            t.IsBackground = true; t.Priority = ThreadPriority.BelowNormal; t.Start();
        }

        static bool HasCode(string reply)
        {
            return reply.IndexOf("```", StringComparison.Ordinal) >= 0 || reply.IndexOf("<write", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   reply.IndexOf("<replace>", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Second pass for a reply with code. 1. Certain problems: missing usings are fixed in code; CodeCheck finds what
        // would not compile or run in the target (C# 6+ syntax for C# 5, PS 7 syntax, invented commands, parameters and
        // framework members, SQL from the wrong dialect, unbalanced brackets) and the model is told exactly what to fix,
        // up to two rounds, keeping a rewrite only when it has fewer problems. 2. Languages CodeCheck does not cover get
        // the model's own review against the syntax-reference sections (LOOKS GOOD or a corrected reply); covered ones
        // skip it when clean (it rarely found anything and doubled the reply time). Streams "w" events and the phase.
        // userText: what the user typed (target versions and SQL dialect come from it). Returns the reply to show.
        string CheckCode(int rid, List<ChatMessage> msgs, string draft, List<RagHit> sentHits, string userText, SamplerSettings s, int maxTokens, TextCallback write, CancelCheck cancel, out GenerationStats cst)
        {
            write("{\"phase\":\"checking\",\"lang\":" + Json.Quote(Rag.LanguageName(draft)) + "}");
            Stopwatch sw = Stopwatch.StartNew();
            cst = new GenerationStats();
            SamplerSettings cs = s.Clone();
            cs.Temperature = Math.Min(cs.Temperature, 0.2f);
            List<string> lint = new List<string>();
            draft = CodeCheck.AutoFix(CodeLint.FixUsings(draft, lint), userText, lint);
            if (lint.Count > 0) write("{\"w\":" + Json.Quote("— fixed: " + string.Join(", ", lint.ToArray()) + " —\n") + "}");

            // 1. certain problems
            List<string> langs;
            List<CodeFinding> found = CodeCheck.Find(draft, userText, out langs);
            int firstFound = found.Count, rounds = 0;
            while (found.Count > 0 && rounds < 2 && !cancel())
            {
                rounds++;
                StringBuilder list = new StringBuilder("Checker: " + found.Count + " problem" + (found.Count > 1 ? "s" : "") + " to fix:\n");
                foreach (CodeFinding f in found) list.Append("- ").Append(f.ToString()).Append('\n');
                write("{\"w\":" + Json.Quote(list.ToString() + "\n") + "}");
                List<ChatMessage> fix = new List<ChatMessage>(msgs);
                fix.Add(new ChatMessage("assistant", draft));
                fix.Add(new ChatMessage("user", CodeCheck.FixPrompt(found)));
                StringBuilder rewrite = new StringBuilder();
                GenerationStats g = _engine.Generate(fix, cs, maxTokens, delegate(string piece) { rewrite.Append(piece); write("{\"w\":" + Json.Quote(piece) + "}"); }, cancel);
                AddStats(cst, g);
                write("{\"w\":\"\\n\\n\"}");
                string v = rewrite.ToString().Trim();
                if (!HasCode(v) || Workspace.QueryTagEnd(v) >= 0) break;
                v = CodeCheck.AutoFix(CodeLint.FixUsings(v, new List<string>()), userText, new List<string>());
                List<string> l2;
                List<CodeFinding> after = CodeCheck.Find(v, userText, out l2);
                if (after.Count >= found.Count) break;   // no better: keep the previous version
                draft = v; found = after;
            }
            bool covered = langs.Count > 0;
            foreach (string l in langs) if (!CodeCheck.Covers(l)) covered = false;
            if (firstFound > 0 || covered)
            {
                string outcome = found.Count > 0 ? "unsure" : firstFound > 0 || lint.Count > 0 ? "wrong" : "good";
                if (covered || found.Count > 0)
                {
                    write("{\"phase\":\"" + outcome + "\",\"ref\":" + (sentHits != null && sentHits.Count > 0 ? "true" : "false") + ",\"docs\":" + DocsJson(sentHits, null) + "}");
                    Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0} CHECK {1} in {2} ms: {3} certain problems found, {4} left after {5} fix round(s); lint {6}{7}",
                        rid, outcome, ChatLog.Ms(sw.Elapsed.TotalSeconds), firstFound, found.Count, rounds, lint.Count == 0 ? "clean" : string.Join(" ", lint.ToArray()),
                        found.Count > 0 ? "\n" + CodeCheck.FixPrompt(found) : ""));
                    return draft;
                }
            }

            // 2. the model reviews its own code (languages without a thorough checker)
            string code = CodeOf(draft);
            // Only sections that are not already in the conversation, at most two: the ones sent with the request are
            // still in the model's cache, so re-sending them would cost seconds of prompt reading (and push Gemma's
            // sliding-window cache past what the next step can reuse).
            List<RagHit> hits = new List<RagHit>();
            if (Rag.Enabled && LocalContextAllowed)
            {
                string sent = "";
                foreach (ChatMessage m in msgs) if (m.Role == "user") sent += m.Text;
                foreach (RagHit h in Rag.Search(code, null))
                    if (hits.Count < 2 && sent.IndexOf(h.Text.Trim(), StringComparison.Ordinal) < 0) hits.Add(h);
            }
            // docs sections in the newest user message (tool-result messages are skipped)
            int lastUser = msgs.FindLastIndex(delegate(ChatMessage m) { return m.Role == "user" && !m.Text.StartsWith("Tool results:"); });
            bool hadSections = lastUser >= 0 && msgs[lastUser].Text.Contains(Rag.SectionsHeader);
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
            StringBuilder verdict = new StringBuilder();
            GenerationStats rs = _engine.Generate(check, cs, maxTokens, delegate(string piece)
            {
                verdict.Append(piece);
                write("{\"w\":" + Json.Quote(piece) + "}");
            }, cancel);
            AddStats(cst, rs);
            string vv = verdict.ToString().Trim();
            bool good = vv.TrimStart('*', '"', '`', ' ').StartsWith("LOOKS GOOD", StringComparison.OrdinalIgnoreCase);
            // A correction must keep the reply's form: code, and no file queries (those would start another round).
            bool fixedIt = !good && HasCode(vv) && Workspace.QueryTagEnd(vv) < 0;
            if (fixedIt) vv = CodeLint.FixUsings(vv, new List<string>());   // the rewrite may drop usings again
            // Verdict for the status line: good (green) = passed; fixed (gold) = the check found and corrected possible
            // mistakes; wrong (red) = definite errors (found by CodeLint / CodeCheck) that were corrected; unsure (gold) =
            // the check gave no clear answer and the code was kept. ref: whether syntax-reference sections were used.
            string result = lint.Count > 0 || firstFound > 0 ? "wrong" : good ? "good" : fixedIt ? "fixed" : "unsure";
            write("{\"phase\":\"" + result + "\",\"ref\":" + (hits.Count > 0 || hadSections ? "true" : "false") + ",\"docs\":" + DocsJson(hadSections ? sentHits : null, hits) + "}");
            Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "#{0} CHECK {1} in {2} ms ({3} reference sections, lint {4}, {9} certain problems fixed; prompt {5} new + {6} cached tokens, {7} generated){8}",
                rid, good ? "looks good" : fixedIt ? "corrected the code" : "unclear verdict, kept the draft", ChatLog.Ms(sw.Elapsed.TotalSeconds),
                hits.Count, lint.Count == 0 ? "clean" : string.Join(" ", lint.ToArray()), rs.PromptTokens, rs.ReusedTokens, rs.GeneratedTokens, good ? "" : "\n" + vv, firstFound));
            return fixedIt ? vv : draft;
        }

        static void AddStats(GenerationStats total, GenerationStats g)
        {
            if (g == null) return;
            total.PromptTokens += g.PromptTokens; total.ReusedTokens += g.ReusedTokens; total.GeneratedTokens += g.GeneratedTokens;
            total.PromptSeconds += g.PromptSeconds; total.GenerateSeconds += g.GenerateSeconds;
        }

        // The library documents a check had in front of it, as a JSON array of file names.
        static string DocsJson(List<RagHit> a, List<RagHit> b)
        {
            List<string> docs = new List<string>();
            foreach (List<RagHit> l in new List<RagHit>[] { a, b })
                if (l != null) foreach (RagHit h in l) { string f = Path.GetFileName(h.File); if (!docs.Contains(f)) docs.Add(f); }
            StringBuilder dj = new StringBuilder("[");
            for (int i = 0; i < docs.Count; i++) dj.Append(i > 0 ? "," : "").Append(Json.Quote(docs[i]));
            return dj.Append(']').ToString();
        }

        // The code in a reply (fenced blocks and <write> bodies) as the reference-search query.
        static string CodeOf(string reply)
        {
            StringBuilder sb = new StringBuilder();
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(reply,
                "```[^\\n]*\\n([\\s\\S]*?)```|<write[^>]*>([\\s\\S]*?)</write>|<replace>()([\\s\\S]*?)</replace>"))
                sb.Append(m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value : m.Groups[2].Success && m.Groups[2].Length > 0 ? m.Groups[2].Value : m.Groups[4].Value).Append('\n');
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
