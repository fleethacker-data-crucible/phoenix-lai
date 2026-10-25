// When the server stops itself, and the runtime file that says it is running (TechStack "Lifecycle rules"). C# 5 only.
//  - The app window owns the server: /api/heartbeat every 5 s and a "leaving" beacon on pagehide (Presence.cs). It stops
//    20 s after the last heartbeat, or 5 s after "leaving" with no new heartbeat; a reload checks in again inside that.
//  - Never mid-write: while a model load, docs indexing, Instant Recall or a reply runs, the stop waits for it to end.
//  - End session (/quit, sidebar) asks for the same clean stop; src\Stop-Phoenix.ps1 does too, then force-stops.
//  - Off switch: run.ps1 -Stay / --stay, env PHOENIX_AUTO_STOP=0, or "0" in %LOCALAPPDATA%\LocalLLMStudio\auto-stop.txt.
//  - logs\phoenix-server-<pid>.json (pid, port, url, startedAt; there is no session token) is written on start and
//    deleted on stop; Stop-Phoenix.ps1 and run.ps1 read it to find a running server, and stale ones are removed.
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
        readonly Presence _presence = new Presence(DateTime.UtcNow);
        volatile bool _stopRequested;
        public bool AutoStop = true;
        public bool StopRequested { get { return _stopRequested; } }

        static string AutoStopFile { get { return Path.Combine(AppDataDir, "auto-stop.txt"); } }

        // False when PHOENIX_AUTO_STOP=0 (environment) or auto-stop.txt holds 0: the server then runs until End session.
        public static bool AutoStopSetting()
        {
            string env = Environment.GetEnvironmentVariable("PHOENIX_AUTO_STOP");
            if (env != null && env.Trim() == "0") return false;
            try { if (File.Exists(AutoStopFile) && File.ReadAllText(AutoStopFile).Trim() == "0") return false; } catch { }
            return true;
        }

        // What a stop has to wait for, or null when nothing that writes is running.
        string BusyWith()
        {
            if (_loading != null) return "the model load";
            if (Rag.Indexing) return "docs indexing";
            if (_recallBusy != 0) return "Instant Recall";
            if (EngineBusy()) return "the reply in progress";
            return null;
        }

        // POST /api/heartbeat?page=<id> {"leaving":bool,"hidden":bool}; POST /api/bye?page=<id> (older pages) = leaving;
        // POST /api/quit = End session. The page id is not a secret (there is no session token). POST only: a GET from
        // another site (an <img>) carries no Origin header, while a POST from another site has its Origin refused in
        // HandleRequest (as is a foreign Host). Returns the JSON reply, or null if the path is not a lifecycle route.
        string LifecycleApi(string path, bool post, Dictionary<string, object> body, string page)
        {
            if (path != "/api/heartbeat" && path != "/api/bye" && path != "/api/quit") return null;
            if (!post) throw new InvalidOperationException(path + " takes POST only.");
            if (path == "/api/quit")
            {
                string busy = BusyWith();
                if (!_stopRequested) Log.Text("end session requested" + (busy != null ? " (stops after " + busy + ")" : ""));
                _stopRequested = true;
                return "{\"stopping\":true,\"waitingFor\":" + (busy != null ? Json.Quote(busy) : "null") + "}";
            }
            bool leaving = path == "/api/bye" || (body != null && body.ContainsKey("leaving") && body["leaving"] is bool && (bool)body["leaving"]);
            if (leaving) _presence.Leave(page, DateTime.UtcNow);
            else _presence.Beat(page, DateTime.UtcNow, body != null && body.ContainsKey("hidden") && body["hidden"] is bool && (bool)body["hidden"]);
            return "{\"autoStop\":" + (AutoStop ? "true" : "false") + "}";
        }

        // Blocks until the server should stop: End session, or (auto-stop on) every app window is gone. Waits while a
        // background job runs and stops on the first tick after it ends.
        void WaitForStop()
        {
            string waitingFor = null;
            if (!AutoStop) Log.Text("auto-stop is off (-Stay / PHOENIX_AUTO_STOP=0): the server runs until End session or Stop-Phoenix.ps1");
            while (true)
            {
                string busy = BusyWith();
                bool gone = _presence.Gone(DateTime.UtcNow);
                string d = Presence.Decide(_stopRequested, AutoStop, gone, busy);
                if (d == "stop")
                {
                    Log.Text(_stopRequested ? "stopping: end session" : "stopping: the app window is closed (no heartbeat; PHOENIX_AUTO_STOP=0 keeps the server running)");
                    return;
                }
                if (d == "wait" && busy != waitingFor) Log.Text("stop is due; waiting for " + busy + " to finish");
                waitingFor = d == "wait" ? busy : null;
                Thread.Sleep(1000);
            }
        }

        // ---- runtime file

        static string RuntimeFilePath(string logsDir, int pid) { return Path.Combine(logsDir, "phoenix-server-" + pid + ".json"); }

        static string WriteRuntimeFile(string logsDir, WebServer server)
        {
            RemoveStaleRuntimeFiles(logsDir);
            int pid;
            using (Process me = Process.GetCurrentProcess()) pid = me.Id;
            string path = RuntimeFilePath(logsDir, pid);
            try
            {
                Directory.CreateDirectory(logsDir);
                string json = "{\"pid\":" + pid + ",\"port\":" + server.Port + ",\"url\":" + Json.Quote(server.Url) +
                              ",\"startedAt\":" + Json.Quote(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)) +
                              ",\"autoStop\":" + (server.AutoStop ? "true" : "false") + "}";
                File.WriteAllText(path, json, new UTF8Encoding(false));
            }
            catch (Exception ex) { server.Log.Text("could not write the runtime file: " + ex.Message); return null; }
            // Also on an abrupt end of the process (Ctrl+C in a console); run.ps1 deletes it in its finally as well.
            string p = path;
            AppDomain.CurrentDomain.ProcessExit += delegate { DeleteRuntimeFile(p); };
            return path;
        }

        static void DeleteRuntimeFile(string path)
        {
            if (path == null) return;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        // Runtime files whose process is gone (a crash, a forced stop) are deleted on sight.
        static void RemoveStaleRuntimeFiles(string logsDir)
        {
            try
            {
                if (!Directory.Exists(logsDir)) return;
                foreach (string f in Directory.GetFiles(logsDir, "phoenix-server-*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    int pid;
                    if (!int.TryParse(name.Substring("phoenix-server-".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid)) continue;
                    bool alive;
                    try { using (Process p = Process.GetProcessById(pid)) alive = !p.HasExited; }
                    catch { alive = false; }
                    if (!alive) DeleteRuntimeFile(f);
                }
            }
            catch { }
        }
    }
}
