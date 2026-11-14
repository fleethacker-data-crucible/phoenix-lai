// Server dashboard and console (server mode, run-phoenix-server.ps1). C# 5 only. No networking here: WebServer.cs routes
//   GET  /api/server-status   load, memory, model, what the AI is doing, connected clients, recent requests (dashboard.html)
//   POST /api/console {line}  one console command -> text (console.html). Phoenix's own commands only: the console never
//                             runs a shell, a script or a program (ShellGuard).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        public bool ServerMode;   // started by ServerHost (tray), not the all-in-one app window

        public string LoadedModelFile { get { IChatModel e = _engine; return e != null ? Path.GetFileName(e.ModelPath) : null; } }

        // Stops listening and frees the model and the project index. Returns the path of the model that was loaded.
        public string Shutdown()
        {
            string model = null;
            try { if (_pindex != null) _pindex.Dispose(); } catch { }
            lock (_engineLock) { if (_engine != null) { model = _engine.ModelPath; _engine.Dispose(); _engine = null; } }
            MemoryTrim.Trim();
            Log.Text("server stopped");
            Log.Json("server_stop", null);
            Dispose();
            return model;
        }

        // ---------- what the AI is doing ----------
        sealed class Activity { public string State = "idle", Source = "", What = ""; public int Rid; public DateTime Started; public long Tokens0; }
        Activity _act = new Activity();

        void ActivityStart(int rid, string source, string what)
        {
            Activity a = new Activity();
            a.State = "generating"; a.Rid = rid; a.Source = source; a.What = what; a.Started = DateTime.UtcNow; a.Tokens0 = Interlocked.Read(ref ChatEngine.LiveTokens);
            _act = a;
        }

        void ActivityEnd(int rid, string source, string kind, GenerationStats st, string finish)
        {
            Activity a = _act;
            double ms = a.Rid == rid ? (DateTime.UtcNow - a.Started).TotalMilliseconds : 0;
            if (a.Rid == rid) _act = new Activity();
            lock (_recent)
            {
                // without stats (client chats, embeddings): the pieces generated while it ran
                int gen = st != null ? st.GeneratedTokens : a.Rid == rid ? (int)(Interlocked.Read(ref ChatEngine.LiveTokens) - a.Tokens0) : 0;
                double tps = st != null ? st.TokensPerSecond : ms > 0 ? gen / (ms / 1000) : 0;
                _recent.Insert(0, new RecentRequest { At = DateTime.Now, Source = source, Kind = kind, Ms = ms, Prompt = st != null ? st.PromptTokens + st.ReusedTokens : 0,
                    Generated = gen, Tps = tps, Finish = finish ?? "" });
                if (_recent.Count > 40) _recent.RemoveAt(_recent.Count - 1);
                _totalRequests++;
                _totalTokens += gen;
            }
        }

        sealed class RecentRequest { public DateTime At; public string Source, Kind, Finish; public double Ms, Tps; public int Prompt, Generated; }
        readonly List<RecentRequest> _recent = new List<RecentRequest>();
        long _totalRequests, _totalTokens;

        // ---------- connected clients ----------
        sealed class ClientSeen { public string Name, Kind; public DateTime First, Last; public long Requests; public string LastPath; }
        readonly Dictionary<string, ClientSeen> _clients = new Dictionary<string, ClientSeen>();

        // Called for every request: API clients by their User-Agent, client / VS Code pages by page id.
        void SeeClient(string key, string kind, string name, string path)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (key.Length > 120) key = key.Substring(0, 120);
            lock (_clients)
            {
                ClientSeen c;
                if (!_clients.TryGetValue(key, out c))
                {
                    if (_clients.Count > 200) return;
                    c = new ClientSeen(); c.First = DateTime.UtcNow; c.Kind = kind; c.Name = name; _clients[key] = c;
                    if (Dbg.On) Dbg.Msg("clients", "new " + kind + " client: " + name);
                }
                c.Last = DateTime.UtcNow; c.Requests++; c.LastPath = path;
            }
        }

        static string ClientName(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return "unknown client";
            string ua = userAgent;
            if (ua.StartsWith("OpenAI/Python")) return "OpenAI Python SDK";
            if (ua.StartsWith("OpenAI/JS")) return "OpenAI JS SDK";
            if (ua.StartsWith("PhoenixLAI-Client")) return "Phoenix client";
            int sp = ua.IndexOf(' ');
            return sp > 0 ? ua.Substring(0, sp) : ua;
        }

        // ---------- status ----------
        TimeSpan _lastCpu; DateTime _lastCpuAt; double _cpuPct; long _lastTok; DateTime _lastTokAt; double _liveTps;

        public string ServerStatusJson()
        {
            Process me = Process.GetCurrentProcess();
            DateTime now = DateTime.UtcNow;
            lock (_recent)
            {
                TimeSpan cpu = me.TotalProcessorTime;
                if (_lastCpuAt != DateTime.MinValue && (now - _lastCpuAt).TotalMilliseconds > 200)
                    _cpuPct = (cpu - _lastCpu).TotalMilliseconds / (now - _lastCpuAt).TotalMilliseconds / Environment.ProcessorCount * 100;
                if (_lastCpuAt == DateTime.MinValue || (now - _lastCpuAt).TotalMilliseconds > 200) { _lastCpu = cpu; _lastCpuAt = now; }
                long tok = Interlocked.Read(ref ChatEngine.LiveTokens);
                if (_lastTokAt != DateTime.MinValue && (now - _lastTokAt).TotalMilliseconds > 200) _liveTps = (tok - _lastTok) / (now - _lastTokAt).TotalSeconds;
                if (_lastTokAt == DateTime.MinValue || (now - _lastTokAt).TotalMilliseconds > 200) { _lastTok = tok; _lastTokAt = now; }
            }
            IChatModel e = _engine;
            Activity a = _act;
            CultureInfo ic = CultureInfo.InvariantCulture;
            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"version\":").Append(Json.Quote(Version()));
            sb.Append(",\"user\":").Append(Json.Quote(Environment.UserName));
            sb.Append(",\"uptime\":").Append((long)(now - (ServerMode ? ServerHost.StartedUtc : _started)).TotalSeconds);
            sb.Append(",\"pid\":").Append(me.Id);
            sb.Append(",\"cpu\":").Append(Math.Round(Math.Max(0, _cpuPct), 1).ToString(ic));
            sb.Append(",\"threads\":").Append(me.Threads.Count);
            sb.Append(",\"workingSet\":").Append(me.WorkingSet64).Append(",\"private\":").Append(me.PrivateMemorySize64);
            sb.Append(",\"ram\":").Append(MemoryTrim.RamJson()).Append(",\"vram\":").Append(VramJson(e));
            sb.Append(",\"api\":").Append(ApiUrl != null ? Json.Quote(ApiUrl) : "null").Append(",\"dashboard\":").Append(Json.Quote(BaseUrl + "dashboard.html"));
            sb.Append(",\"model\":").Append(e != null ? Json.Quote(Path.GetFileName(e.ModelPath)) : "null");
            sb.Append(",\"backend\":").Append(e != null ? Json.Quote(e.Backend) : "null");
            sb.Append(",\"context\":").Append(e != null ? e.ContextLength : 0).Append(",\"contextSetting\":").Append(ContextLength);
            sb.Append(",\"vision\":").Append(e != null && e.HasVision ? "true" : "false");
            sb.Append(",\"gpuLoad\":").Append(Math.Round(GpuLoad(), 1).ToString(ic));
            sb.Append(",\"gpuName\":").Append(Json.Quote(GpuName(e)));
            // KV cache: the share of the context window the cached conversation fills
            sb.Append(",\"kvUsed\":").Append(e != null ? e.UsedTokens : 0).Append(",\"kvPct\":")
              .Append(e != null && e.ContextLength > 0 ? Math.Round(100.0 * e.UsedTokens / e.ContextLength, 1).ToString(ic) : "0");
            sb.Append(",\"device\":").Append(_useGpu ? "\"gpu\"" : "\"cpu\"");
            sb.Append(",\"loading\":").Append(_loading != null ? Json.Quote(_loading) : "null").Append(",\"loadError\":").Append(_loadError != null ? Json.Quote(_loadError) : "null");
            sb.Append(",\"apiKey\":").Append(ApiKeyRequired ? "true" : "false").Append(",\"apiFormat\":").Append(Json.Quote(ApiFormat)).Append(",\"apiPort\":").Append(ApiPort);
            string smodel = SamplingModel();
            sb.Append(",\"sampling\":").Append(smodel == null ? "null" : "{\"model\":" + Json.Quote(smodel) + ",\"default\":" + (ModelSettings.IsDefault(smodel) ? "true" : "false") +
                ",\"values\":" + ModelSettings.For(smodel).ToJson() + "}");
            sb.Append(",\"debug\":").Append(Dbg.On ? "true" : "false").Append(",\"rag\":").Append(Rag.StatusJson());
            string state = _loading != null ? "loading" : a.State;
            sb.Append(",\"activity\":{\"state\":").Append(Json.Quote(state)).Append(",\"source\":").Append(Json.Quote(_loading != null ? "model load" : a.Source))
              .Append(",\"what\":").Append(Json.Quote(_loading ?? a.What)).Append(",\"rid\":").Append(a.Rid)
              .Append(",\"seconds\":").Append(a.State == "idle" ? "0" : Math.Round((now - a.Started).TotalSeconds, 1).ToString(ic))
              .Append(",\"tokens\":").Append(a.State == "idle" ? 0 : Interlocked.Read(ref ChatEngine.LiveTokens) - a.Tokens0)
              .Append(",\"tps\":").Append(Math.Round(a.State == "idle" ? 0 : _liveTps, 1).ToString(ic)).Append(",\"active\":").Append(Thread.VolatileRead(ref _activeRequests)).Append('}');
            sb.Append(",\"totals\":{\"requests\":").Append(_totalRequests).Append(",\"tokens\":").Append(_totalTokens).Append('}');
            sb.Append(",\"clients\":[");
            lock (_clients)
            {
                List<ClientSeen> list = new List<ClientSeen>(_clients.Values);
                list.Sort(delegate(ClientSeen x, ClientSeen y) { return y.Last.CompareTo(x.Last); });
                int n = 0;
                foreach (ClientSeen c in list)
                {
                    double ago = (now - c.Last).TotalSeconds;
                    if (ago > 3600 || n >= 30) continue;
                    if (n++ > 0) sb.Append(',');
                    sb.Append("{\"name\":").Append(Json.Quote(c.Name)).Append(",\"kind\":").Append(Json.Quote(c.Kind)).Append(",\"requests\":").Append(c.Requests)
                      .Append(",\"lastSeconds\":").Append((long)ago).Append(",\"connected\":").Append(ago < (c.Kind == "api" ? 120 : 75) ? "true" : "false")
                      .Append(",\"last\":").Append(Json.Quote(c.LastPath ?? "")).Append('}');
                }
            }
            sb.Append("],\"recent\":[");
            lock (_recent)
                for (int i = 0; i < _recent.Count; i++)
                {
                    RecentRequest r = _recent[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"at\":").Append(Json.Quote(r.At.ToString("HH:mm:ss", ic))).Append(",\"source\":").Append(Json.Quote(r.Source)).Append(",\"kind\":").Append(Json.Quote(r.Kind))
                      .Append(",\"ms\":").Append((long)r.Ms).Append(",\"prompt\":").Append(r.Prompt).Append(",\"generated\":").Append(r.Generated)
                      .Append(",\"tps\":").Append(Math.Round(r.Tps, 1).ToString(ic)).Append(",\"finish\":").Append(Json.Quote(r.Finish)).Append('}');
                }
            return sb.Append("]}").ToString();
        }

        readonly DateTime _started = DateTime.UtcNow;

        // ---------- GPU load ----------
        // Whole-GPU load, 0-100 %, as Task Manager shows it: Windows' "GPU Engine" counters (no admin rights needed), summed
        // over the processes on each engine; the busiest engine is the load. Sampled once a second on a background thread
        // that starts with the first status request.
        static double _gpuLoad;
        static Thread _gpuThread;
        static string _gpuName;

        // The video card the server runs on: the loaded model's ("GPU: <name>"), else the card Vulkan picks, read once.
        static string GpuName(IChatModel e)
        {
            if (e != null && e.Backend != null && e.Backend.StartsWith("GPU: ")) return _gpuName = e.Backend.Substring(5);
            if (_gpuName != null) return _gpuName;
            try { using (GpuDevice d = GpuDevice.Create()) _gpuName = d.DeviceName; } catch { _gpuName = ""; }
            return _gpuName;
        }

        static double GpuLoad()
        {
            if (_gpuThread == null)
                lock (typeof(WebServer))
                    if (_gpuThread == null)
                    {
                        _gpuThread = new Thread(GpuSampler);
                        _gpuThread.IsBackground = true;
                        _gpuThread.Priority = ThreadPriority.BelowNormal;
                        _gpuThread.Start();
                    }
            return _gpuLoad;
        }

        static void GpuSampler()
        {
            Dictionary<string, PerformanceCounter> counters = new Dictionary<string, PerformanceCounter>();
            DateTime listed = DateTime.MinValue;
            while (true)
            {
                try
                {
                    // the engine instances change as programs start and stop: re-list every 5 s
                    if ((DateTime.UtcNow - listed).TotalSeconds > 5)
                    {
                        listed = DateTime.UtcNow;
                        HashSet<string> now = new HashSet<string>(new PerformanceCounterCategory("GPU Engine").GetInstanceNames());
                        foreach (string gone in new List<string>(counters.Keys)) if (!now.Contains(gone)) { counters[gone].Dispose(); counters.Remove(gone); }
                        foreach (string inst in now) if (!counters.ContainsKey(inst)) counters[inst] = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                    }
                    // instance: pid_<pid>_luid_<luid>_phys_<n>_eng_<n>_engtype_<type>; the engine is everything after the pid
                    Dictionary<string, double> byEngine = new Dictionary<string, double>();
                    foreach (KeyValuePair<string, PerformanceCounter> kv in counters)
                    {
                        float v;
                        try { v = kv.Value.NextValue(); } catch { continue; }
                        int at = kv.Key.IndexOf("_luid_", StringComparison.Ordinal);
                        string engine = at >= 0 ? kv.Key.Substring(at) : kv.Key;
                        double cur; byEngine.TryGetValue(engine, out cur);
                        byEngine[engine] = cur + v;
                    }
                    double max = 0;
                    foreach (double v in byEngine.Values) max = Math.Max(max, v);
                    _gpuLoad = Math.Min(100, max);
                }
                catch { _gpuLoad = 0; }
                Thread.Sleep(1000);
            }
        }

        // ---------- console ----------
        static readonly string[,] ConsoleCommands = {
            { "help", "this list" },
            { "status", "model, device, memory, API address, uptime" },
            { "clients", "programs and pages talking to the server" },
            { "recent", "the last requests with their speed" },
            { "models", "models the API offers (the loaded one marked *; clients pick one per request)" },
            { "load <name>", "load a model now (part of the file name is enough; Tab completes)" },
            { "unload", "unload the model and free its video memory" },
            { "bench [tokens]", "timed generation on the loaded model (default 128 tokens)" },
            { "apitest", "checks the OpenAI /v1 answers in-process (models, chat, completion, embeddings)" },
            { "vram", "video memory by program" },
            { "debug on|off", "step-by-step debug lines in the session log" },
            { "apikey [new|set <key>|off]", "require an API key on /v1 (kept in Windows Credential Manager)" },
            { "apiformat [openai|anthropic|both]", "which API format /v1 hosts: OpenAI (chat/completions), Anthropic (messages) or both" },
            { "sampling [<key> <value>|reset]", "the loaded model's generation settings (temperature, top_k, top_p, repeat_penalty, max_tokens); default Gemma 4's" },
            { "apiport [port]", "the port the /v1 API serves on (default " + DefaultApiPort + "); changes it now and saves it" },
            { "log [n]", "the last n lines of the session log (default 30)" },
            { "gc", "free unused memory back to Windows" },
            { "restart", "restart the server (reloads the model)" },
            { "clear", "clear the screen" } };

        // Every description starts in the same column: 3 spaces past the longest command.
        static readonly string ConsoleHelp = BuildHelp();
        static string BuildHelp()
        {
            int w = 0;
            for (int i = 0; i < ConsoleCommands.GetLength(0); i++) w = Math.Max(w, ConsoleCommands[i, 0].Length);
            StringBuilder sb = new StringBuilder("Phoenix console: the server's own commands (it never runs a shell, a script or a program).");
            for (int i = 0; i < ConsoleCommands.GetLength(0); i++) sb.Append('\n').Append(ConsoleCommands[i, 0].PadRight(w + 3)).Append(ConsoleCommands[i, 1]);
            return sb.ToString();
        }

        public string ConsoleRun(string line)
        {
            line = (line ?? "").Trim();
            int sp = line.IndexOf(' ');
            string cmd = (sp < 0 ? line : line.Substring(0, sp)).ToLowerInvariant(), arg = sp < 0 ? "" : line.Substring(sp + 1).Trim();
            Log.Text("console: " + (cmd == "apikey" && arg.StartsWith("set", StringComparison.OrdinalIgnoreCase) ? "apikey set ********" : line));   // never a key in the log
            CultureInfo ic = CultureInfo.InvariantCulture;
            switch (cmd)
            {
                case "": return "";
                case "help": case "?": return ConsoleHelp;
                case "status":
                {
                    IChatModel e = _engine;
                    Process me = Process.GetCurrentProcess();
                    TimeSpan up = DateTime.UtcNow - (ServerMode ? ServerHost.StartedUtc : _started);
                    ulong used = 0, budget = 0; bool v = e != null && e.Vram(out used, out budget);
                    return "version   " + Version() + "\n" +
                           "uptime    " + ((int)up.TotalHours).ToString(ic) + "h " + up.Minutes + "m " + up.Seconds + "s\n" +
                           "api       " + (ApiUrl ?? "not listening (port " + ApiPort + " in use)") + "\n" +
                           "model     " + (e != null ? Path.GetFileName(e.ModelPath) + " on " + e.Backend + ", context " + e.ContextLength : _loading != null ? "loading " + _loading : "none") + "\n" +
                           "device    " + (_useGpu ? "GPU" : "CPU") + "\n" +
                           (v ? "vram      " + Gb((long)used) + " used of " + Gb((long)budget) + "\n" : "") +
                           "memory    " + Gb(me.WorkingSet64) + " working set, " + Gb(me.PrivateMemorySize64) + " private\n" +
                           "requests  " + _totalRequests + " done, " + _totalTokens + " tokens generated, " + Thread.VolatileRead(ref _activeRequests) + " in flight\n" +
                           "api key   " + (ApiKeyRequired ? "required" : "not required") + "\n" +
                           "format    " + ApiFormatName + "\n" +
                           "debug     " + (Dbg.On ? "on" : "off");
                }
                case "clients":
                {
                    StringBuilder sb = new StringBuilder();
                    lock (_clients)
                        foreach (ClientSeen c in _clients.Values)
                            sb.AppendFormat(ic, "  {0,-28} {1,-7} {2,6} requests   last {3,5}s ago   {4}\n", Cut(c.Name, 28), c.Kind, c.Requests, (long)(DateTime.UtcNow - c.Last).TotalSeconds, c.LastPath);
                    return sb.Length > 0 ? "name                         kind    count\n" + sb.ToString().TrimEnd() : "No clients yet.";
                }
                case "recent":
                {
                    StringBuilder sb = new StringBuilder();
                    lock (_recent)
                        foreach (RecentRequest r in _recent)
                            sb.AppendFormat(ic, "  {0}  {1,-22} {2,-12} {3,6} ms  {4,6} in  {5,5} out  {6,6:F1} t/s  {7}\n", r.At.ToString("HH:mm:ss", ic), Cut(r.Source, 22), r.Kind, (long)r.Ms, r.Prompt, r.Generated, r.Tps, r.Finish);
                    return sb.Length > 0 ? sb.ToString().TrimEnd() : "No requests yet.";
                }
                case "models":
                {
                    if (!Directory.Exists(_modelsDir)) return "No models folder yet. Copy Gemma 4 .gguf files into " + _modelsDir + " and type models again.";
                    StringBuilder sb = new StringBuilder();
                    string loaded = LoadedModelFile;
                    foreach (string f in Directory.GetFiles(_modelsDir, "*.gguf"))
                    {
                        string n = Path.GetFileName(f);
                        if (!IsListedModel(n)) continue;   // no vision projectors, no embedding model
                        sb.AppendFormat(ic, "{0} {1,-44} {2,8:F2} GB\n", string.Equals(n, loaded, StringComparison.OrdinalIgnoreCase) ? "*" : " ", n, new FileInfo(f).Length / 1073741824.0);
                    }
                    return sb.Length > 0 ? sb.ToString().TrimEnd() : "No models in " + _modelsDir + ". Copy Gemma 4 .gguf files there.";
                }
                case "load":
                {
                    if (arg.Length == 0) return "Usage: load <name>  (models lists them; Tab completes)";
                    if (!Directory.Exists(_modelsDir)) return "No models folder yet. Copy Gemma 4 .gguf files into " + _modelsDir + " first.";
                    string pick = null;
                    foreach (string f in Directory.GetFiles(_modelsDir, "*.gguf"))
                    {
                        string n = Path.GetFileName(f);
                        if (!IsListedModel(n)) continue;
                        if (string.Equals(Path.GetFileNameWithoutExtension(n), arg, StringComparison.OrdinalIgnoreCase) || string.Equals(n, arg, StringComparison.OrdinalIgnoreCase)) { pick = n; break; }
                        if (pick == null && n.IndexOf(arg, StringComparison.OrdinalIgnoreCase) >= 0) pick = n;
                    }
                    if (pick == null) return "No model matches \"" + arg + "\". Type models for the list.";
                    Stopwatch sw = Stopwatch.StartNew();
                    LoadModel(pick);
                    IChatModel e = _engine;
                    return "Loaded " + Path.GetFileNameWithoutExtension(pick) + " on " + (e != null ? e.Backend.Replace("GPU: ", "") + ", context " + e.ContextLength.ToString("N0", ic) : "?") +
                           " in " + sw.Elapsed.TotalSeconds.ToString("F1", ic) + " s.";
                }
                case "unload":
                    lock (_engineLock)
                    {
                        if (_engine == null) return "No model loaded.";
                        string n = Path.GetFileNameWithoutExtension(_engine.ModelPath);
                        _engine.Dispose(); _engine = null; MemoryTrim.Trim();
                        Log.Text("unloaded " + n + " (console)");
                        return "Unloaded " + n + "; its video memory is free.";
                    }
                case "bench": return Bench(arg);
                case "apitest": return ApiSelfTest();
                case "vram":
                {
                    Dictionary<string, object> d = Json.Parse(VramProcessesJson()) as Dictionary<string, object>;
                    List<object> ps = d != null && d.ContainsKey("processes") ? d["processes"] as List<object> : null;
                    if (ps == null) return "Per-program video memory unavailable: " + Json.GetStr(d, "error", "?");
                    StringBuilder sb = new StringBuilder();
                    foreach (object o in ps) { Dictionary<string, object> p = (Dictionary<string, object>)o; sb.AppendFormat(ic, "  {0,10}  {1}\n", Gb((long)Json.GetNum(p, "bytes", 0)), Json.GetStr(p, "name", "")); }
                    return sb.ToString().TrimEnd();
                }
                case "debug":
                    if (arg != "on" && arg != "off") return "Debug logging is " + (Dbg.On ? "on" : "off") + ". Use debug on or debug off.";
                    Dbg.Set(arg == "on");
                    return "Debug logging " + arg + ".";
                case "log":
                {
                    int n;
                    if (!int.TryParse(arg, out n) || n <= 0) n = 30;
                    n = Math.Min(n, 500);
                    string[] lines;
                    try { using (FileStream fs = new FileStream(Log.TextPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (StreamReader r = new StreamReader(fs, Encoding.UTF8)) lines = r.ReadToEnd().Split('\n'); }
                    catch (Exception ex) { return "Could not read the log: " + ex.Message; }
                    int from = Math.Max(0, lines.Length - n - 1);
                    return string.Join("\n", lines, from, lines.Length - from).TrimEnd();
                }
                case "gc":
                {
                    long before = Process.GetCurrentProcess().WorkingSet64;
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long mb = MemoryTrim.Trim();
                    return "Freed " + mb + " MB (working set " + Gb(before) + " -> " + Gb(Process.GetCurrentProcess().WorkingSet64) + ").";
                }
                case "apikey":
                {
                    // apikey | apikey new | apikey set <key> | apikey off
                    string[] p = arg.Split(new char[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    string sub = p.Length > 0 ? p[0].ToLowerInvariant() : "";
                    const string note = "\nClients send it as \"Authorization: Bearer <key>\" (the OpenAI SDKs' api_key). The VS Code extension sends none: its inline completion stops while a key is required.";
                    if (sub == "")
                        return ApiKeyRequired ? "The API requires a key (stored in Windows Credential Manager, PhoenixLAI.Server). apikey off removes it; apikey new makes a fresh one."
                                              : "No API key: any client on this computer may use the API. apikey new makes one (shown once), apikey set <key> sets your own.";
                    if (sub == "off") { SetApiKey(null); Log.Text("API key removed"); return "API key removed: the API accepts any client on this computer again."; }
                    if (sub == "new")
                    {
                        byte[] b = new byte[24];
                        using (System.Security.Cryptography.RandomNumberGenerator r = System.Security.Cryptography.RandomNumberGenerator.Create()) r.GetBytes(b);
                        string k = "phx-" + Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');
                        SetApiKey(k);
                        Log.Text("API key set (new)");
                        return "New API key (shown only now; copy it into your clients):\n  " + k + note;
                    }
                    if (sub == "set")
                    {
                        string k = p.Length > 1 ? p[1].Trim() : "";
                        if (k.Length < 8 || k.IndexOf(' ') >= 0) return "Usage: apikey set <key> (at least 8 characters, no spaces).";
                        SetApiKey(k);
                        Log.Text("API key set");
                        return "API key set." + note;
                    }
                    return "Usage: apikey | apikey new | apikey set <key> | apikey off";
                }
                case "apiformat":
                {
                    string f = arg.ToLowerInvariant();
                    const string what = "\n  openai     /v1/chat/completions, /v1/completions (OpenAI SDKs, VS Code extensions, the Phoenix client)\n  anthropic  /v1/messages (Anthropic SDKs: base URL = the api address without /v1)\n  both       all of them\n/v1/embeddings stays on in every format (docs search).";
                    if (f == "") return "API format: " + ApiFormatName + "." + what;
                    if (!IsApiFormat(f)) return "Usage: apiformat openai | anthropic | both";
                    SetApiFormat(f);
                    Log.Text("API format set to " + ApiFormatName);
                    return "API format: " + ApiFormatName + " (in effect now)." + (f == "anthropic" ? "\nThe Phoenix client talks OpenAI /v1: it cannot chat through this server until the format is openai or both." : "");
                }
                case "sampling":
                {
                    string model = SamplingModel();
                    if (model == null) return "No model loaded: load one first (load <name>), then set its sampling.";
                    string[] a = arg.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    try
                    {
                        if (a.Length == 0)
                            return model + ": " + ModelSettings.For(model) + (ModelSettings.IsDefault(model) ? " (Gemma 4 defaults)" : "") +
                                   "\nChange: sampling <key> <value> (keys: " + string.Join(", ", ModelSettings.Keys) + ") · sampling reset = Gemma 4 defaults." +
                                   "\nThese apply when a request leaves the value out; a value a client sends still wins.";
                        if (a.Length == 1 && a[0].ToLowerInvariant() == "reset")
                        { ModelSampling r = ModelSettings.Reset(model); Log.Text("sampling for " + model + " reset to Gemma 4 defaults"); return model + ": " + r + " (Gemma 4 defaults, saved)."; }
                        double v;
                        if (a.Length != 2 || !double.TryParse(a[1], NumberStyles.Float, ic, out v)) return "Usage: sampling | sampling <key> <value> | sampling reset";
                        ModelSampling s = ModelSettings.Set(model, a[0].ToLowerInvariant(), v);
                        Log.Text("sampling for " + model + ": " + s);
                        return model + ": " + s + " (saved).";
                    }
                    catch (ArgumentException ex) { return "Not changed: " + ex.Message; }
                }
                case "apiport":
                {
                    if (arg == "") return "API port: " + ApiPort + (ApiUrl != null ? " (" + ApiUrl + ")" : " (not listening: the port is in use)") +
                        ". Change it with apiport <port>; default " + DefaultApiPort + ".";
                    int p;
                    if (!int.TryParse(arg, NumberStyles.None, ic, out p)) return "Usage: apiport <port>, a number from 1024 to 65535.";
                    try { return SetApiPort(p); }
                    catch (Exception ex) { return "Not changed: " + ex.Message; }
                }
                case "restart":
                    if (!ServerMode) return "restart works in server mode (run-phoenix-server.ps1).";
                    ServerHost.RestartLater();
                    return "Restarting: the console reconnects in a few seconds.";
                default: return "Unknown command \"" + cmd + "\". Type help.";
            }
        }

        static string Gb(long b) { return (b / 1073741824.0).ToString("F2", CultureInfo.InvariantCulture) + " GB"; }
        static string Cut(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n - 1) + "…" : s; }

        string Bench(string arg)
        {
            int n;
            if (!int.TryParse(arg, out n) || n <= 0) n = 128;
            n = Math.Min(n, 2048);
            lock (_engineLock)
            {
                IChatModel e = _engine;
                if (e == null) return "No model loaded: load <name> loads one (a client's first request also loads the model it asks for).";
                SamplerSettings s = new SamplerSettings();
                s.Temperature = 0; s.TopK = 1; s.TopP = 1; s.RepeatPenalty = 1;
                List<ChatMessage> msgs = new List<ChatMessage>();
                msgs.Add(new ChatMessage("user", "Write a long, detailed essay about the history of computing. Benchmark run " + Guid.NewGuid().ToString("N").Substring(0, 8) + "."));
                int rid = Interlocked.Increment(ref _requestId);
                ActivityStart(rid, "console", "bench " + n);
                Stopwatch sw = Stopwatch.StartNew();
                GenerationStats st = null;
                try { st = e.Generate(msgs, s, n, null, null); }
                finally { ActivityEnd(rid, "console", "bench", st, st != null ? st.StopReason : "error"); }
                CultureInfo ic = CultureInfo.InvariantCulture;
                return string.Format(ic, "{0} on {1}\n  prompt   {2} tokens in {3:F0} ms ({4:F1} t/s)\n  generate {5} tokens in {6:F0} ms ({7:F1} t/s)\n  total    {8:F2} s, stop: {9}",
                    Path.GetFileName(e.ModelPath), e.Backend, st.PromptTokens, st.PromptSeconds * 1000, st.PromptTokensPerSecond, st.GeneratedTokens, st.GenerateSeconds * 1000, st.TokensPerSecond, sw.Elapsed.TotalSeconds, st.StopReason);
            }
        }

        // The OpenAI /v1 answers, built in-process exactly as the API builds them, checked for the fields clients rely on.
        string ApiSelfTest()
        {
            StringBuilder sb = new StringBuilder();
            Action<string, bool, string> line = delegate(string name, bool ok, string detail) { sb.Append(ok ? "  PASS  " : "  FAIL  ").Append(name.PadRight(30)).Append(detail).Append('\n'); };
            Dictionary<string, object> d = Json.Parse(ModelsList()) as Dictionary<string, object>;
            List<object> data = d != null && d.ContainsKey("data") ? d["data"] as List<object> : null;
            line("GET /v1/models", d != null && Json.GetStr(d, "object", "") == "list" && data != null, data != null ? data.Count + " models" : "bad JSON");
            if (_engine == null) { sb.Append("  (no model loaded yet: chat, completion and embeddings are tested once a client has used one)\n"); return sb.ToString().TrimEnd(); }
            string[] reqs = {
                "{\"messages\":[{\"role\":\"user\",\"content\":\"Say OK.\"}],\"max_tokens\":8,\"temperature\":0}",
                "{\"messages\":[{\"role\":\"user\",\"content\":\"Say OK.\"}],\"max_tokens\":8,\"temperature\":0,\"stream\":true,\"stream_options\":{\"include_usage\":true}}",
                "{\"prompt\":\"1, 2, 3,\",\"max_tokens\":6,\"temperature\":0}" };
            string[] names = { "POST /v1/chat/completions", "  ... stream", "POST /v1/completions" };
            for (int i = 0; i < reqs.Length; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                string body;
                try { body = CompletionText(Json.Parse(reqs[i]) as Dictionary<string, object>, i < 2); }
                catch (Exception ex) { line(names[i], false, ex.Message); continue; }
                bool ok;
                string detail;
                if (i == 1)
                {
                    int chunks = 0; bool done = body.TrimEnd().EndsWith("data: [DONE]"), usage = body.Contains("\"usage\":{");
                    foreach (string l in body.Split('\n')) if (l.StartsWith("data: {")) chunks++;
                    ok = done && chunks > 1 && usage; detail = chunks + " events, [DONE] " + (done ? "yes" : "NO") + ", usage " + (usage ? "yes" : "NO");
                }
                else
                {
                    Dictionary<string, object> r = Json.Parse(body) as Dictionary<string, object>;
                    List<object> ch = r != null && r.ContainsKey("choices") ? r["choices"] as List<object> : null;
                    ok = ch != null && ch.Count == 1 && r.ContainsKey("usage") && r.ContainsKey("id") && r.ContainsKey("created");
                    detail = ok ? "finish " + Json.GetStr((Dictionary<string, object>)ch[0], "finish_reason", "?") : "missing choices / usage / id";
                }
                line(names[i], ok, detail + " (" + sw.ElapsedMilliseconds + " ms)");
            }
            try
            {
                string modelId; float[] v = Rag.EmbedRaw("hello world", out modelId);
                line("POST /v1/embeddings", v != null && v.Length > 0, (v != null ? v.Length : 0) + " dims");
            }
            catch (Exception ex) { line("POST /v1/embeddings", false, ex.Message); }
            return sb.ToString().TrimEnd();
        }
    }
}
