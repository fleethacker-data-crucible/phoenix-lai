// Client-side lifecycle, reconciled from fork/app-lifecycle. The independent model server stays running.
// C# 5; no networking or process creation here. Host/Origin validation remains in WebServer.cs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        readonly Presence _clientPresence = new Presence(DateTime.UtcNow);
        volatile bool _clientQuitRequested;
        public bool ClientAutoStop = true;

        public static bool ClientAutoStopSetting()
        {
            string setting = Environment.GetEnvironmentVariable("PHOENIX_CLIENT_AUTO_STOP");
            if (setting == null) setting = Environment.GetEnvironmentVariable("PHOENIX_AUTO_STOP");
            if (setting != null && setting.Trim() == "0") return false;
            try
            {
                string file = Path.Combine(AppDataDir, "auto-stop.txt");
                if (File.Exists(file) && File.ReadAllText(file).Trim() == "0") return false;
            }
            catch { }
            return true;
        }

        bool ClientEndRequested { get { return (ShutdownRequested || _clientQuitRequested) && ClientBusyWith() == null; } }

        string ClientBusyWith()
        {
            if (_loading != null) return "the model selection";
            if (Rag.Indexing) return "docs indexing";
            if (_recallBusy != 0 || _pretypeBusy != 0) return "background model work";
            if (Interlocked.CompareExchange(ref _activeRequests, 0, 0) > 0) return "the request in progress";
            return null;
        }

        // Called only by IsClient, after the existing Host/Origin checks. The tray server never serves these routes.
        string ClientLifecycleApi(string path, bool post, Dictionary<string, object> body, string page)
        {
            if (path != "/api/heartbeat" && path != "/api/bye" && path != "/api/quit") return null;
            if (!post) throw new InvalidOperationException("Client lifecycle routes require POST.");
            if (path == "/api/quit")
            {
                _clientQuitRequested = true;
                // The current quit request itself is active; the loop waits until it and any writes finish.
                return "{\"stopping\":true,\"waitingFor\":\"active requests and indexing\"}";
            }
            bool leaving = path == "/api/bye" || (body != null && body.ContainsKey("leaving") && body["leaving"] is bool && (bool)body["leaving"]);
            if (leaving)
            {
                _clientPresence.Leave(page, DateTime.UtcNow);
                if (page != null) lock (_pages) _pages.Remove(page);
            }
            else
            {
                bool hidden = body != null && body.ContainsKey("hidden") && body["hidden"] is bool && (bool)body["hidden"];
                _clientPresence.Beat(page, DateTime.UtcNow, hidden);
            }
            return "{\"autoStop\":" + (ClientAutoStop ? "true" : "false") + "}";
        }

        void WaitForClientStop()
        {
            while (true)
            {
                string decision = Presence.Decide(ShutdownRequested || _clientQuitRequested, ClientAutoStop,
                    _clientPresence.Gone(DateTime.UtcNow), ClientBusyWith());
                if (decision == "stop") return;
                Thread.Sleep(250);
            }
        }

        sealed class ClientRuntime : IDisposable
        {
            readonly string _path;
            public ClientRuntime(WebServer server, string root)
            {
                int pid;
                using (Process me = Process.GetCurrentProcess()) pid = me.Id;
                string logs = Path.Combine(root, "logs");
                Directory.CreateDirectory(logs);
                _path = Path.Combine(logs, "phoenix-client-" + pid + ".json");
                string json = "{\"pid\":" + pid + ",\"port\":" + server.Port + ",\"url\":" + Json.Quote(server.BaseUrl) +
                    ",\"root\":" + Json.Quote(Path.GetFullPath(root)) + ",\"role\":\"Client\",\"startedAt\":" + Json.Quote(DateTime.UtcNow.ToString("o")) + "}";
                File.WriteAllText(_path, json, new UTF8Encoding(false));
            }
            public void Dispose() { try { if (File.Exists(_path)) File.Delete(_path); } catch { } }
        }
    }
}
