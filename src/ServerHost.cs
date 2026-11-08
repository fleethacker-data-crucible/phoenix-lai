// Phoenix LAI server (run-phoenix-server.ps1): runs the models and serves them as an OpenAI-compatible API on
// localhost port 1893 (/v1), with no chat window. It lives in the notification area (system tray) until Exit; a click on
// the icon opens a mini dashboard (TrayPanel) with live status and these buttons:
//   Start / Stop / Restart   the server (Restart reloads the model that was loaded)
//   Console                  a command console for statistics, tests and debugging (Phoenix commands only, never a shell)
//   Dashboard                server load, connected clients, what the AI is doing, settings
// The server knows nothing of the Phoenix Client: the client is a separate program that reaches it only through /v1,
// like any other OpenAI client. C# 5 only. No networking here: the listeners are WebServer's; windows open through WebServer.OpenWindow.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Phoenix
{
    public static class ServerHost
    {
        static readonly object _gate = new object();
        static WebServer _server;
        static string _root;
        static int _ctx;
        static NotifyIcon _tray;
        public static DateTime StartedUtc;

        public static WebServer Current { get { return _server; } }

        public static int Run(string root, string modelPath, int ctx)
        {
            Application.EnableVisualStyles();
            bool first;
            // never two servers fighting over the GPU
            using (Mutex single = new Mutex(true, @"Local\PhoenixAI.Server", out first))
            {
                if (!first)
                {
                    MessageBox.Show("A Phoenix server is already running (see the Phoenix icon in the notification area).", "Phoenix LAI server", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                try
                {
                    _root = root; _ctx = ctx;
                    Start(modelPath);
                    BuildTray();
                    Application.Run();
                    _tray.Visible = false;
                    _tray.Dispose();
                    Stop();
                    return 0;
                }
                finally { single.ReleaseMutex(); }
            }
        }

        // Starts the listeners (dashboard port 8780-8789, OpenAI API on 1893) and, if given, loads a model in the background.
        public static void Start(string modelPath)
        {
            lock (_gate)
            {
                if (_server != null) return;
                WebServer s = WebServer.StartOnFreePort(Path.Combine(_root, "html"), Path.Combine(_root, "models"), Path.Combine(_root, "logs"));
                s.ContextLength = _ctx;
                s.ServerMode = true;
                _server = s;
                StartedUtc = DateTime.UtcNow;
                s.Log.Text("server mode: OpenAI API " + (s.ApiUrl ?? "NOT available (port " + WebServer.ApiPort + " in use)") + ", dashboard " + s.BaseUrl + "dashboard.html");
                if (!string.IsNullOrEmpty(modelPath))
                {
                    string m = modelPath;
                    ThreadPool.QueueUserWorkItem(delegate { try { s.LoadModel(m); } catch { } });
                }
            }
            Notify("Phoenix server started", "OpenAI API on " + (Current != null && Current.ApiUrl != null ? Current.ApiUrl : "(port " + WebServer.ApiPort + " busy)"));
        }

        // Stops listening and frees the model (VRAM). Returns the loaded model's path, for Restart.
        public static string Stop()
        {
            lock (_gate)
            {
                WebServer s = _server;
                if (s == null) return null;
                _server = null;
                string model = s.Shutdown();
                return model;
            }
        }

        public static void Restart()
        {
            string model;
            lock (_gate) { model = Stop(); Start(model); }
        }

        // From a request thread (the console): answer first, then restart.
        public static void RestartLater()
        {
            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(500); try { Restart(); } catch (Exception ex) { Notify("Restart failed", ex.Message); } });
        }

        // A click on the tray icon (left or right) opens the mini dashboard (TrayPanel): live status plus the actions.
        static void BuildTray()
        {
            _panel = new TrayPanel(_root);
            _panel.Clicked += delegate(string a)
            {
                switch (a)
                {
                    case "start": Run(delegate { Start(null); }); break;
                    case "stop": Run(delegate { Stop(); Notify("Phoenix server stopped", "The model is unloaded and the API is closed."); }); break;
                    case "restart": Run(Restart); break;
                    case "console": Open("console.html"); break;
                    case "dashboard": Open("dashboard.html"); break;
                    case "exit": Application.ExitThread(); break;
                }
            };
            _tray = new NotifyIcon();
            _tray.Icon = LoadIcon();
            _tray.Text = "Phoenix LAI server";
            _tray.MouseUp += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) _panel.ShowNear(Cursor.Position); };
            _tray.Visible = true;
            System.Windows.Forms.Timer tip = new System.Windows.Forms.Timer();
            tip.Interval = 2000;
            tip.Tick += delegate
            {
                RefreshTip();
                // POST /__fleethacker/shutdown: the same as the tray's Exit (runs here, on the UI thread)
                WebServer cur = _server;
                if (cur != null && cur.ShutdownRequested) Application.ExitThread();
            };
            tip.Start();
            RefreshTip();
        }

        static TrayPanel _panel;

        static void RefreshTip()
        {
            WebServer s = _server;
            string model = s != null ? s.LoadedModelFile : null;
            string t = s != null ? "Phoenix server · " + (model ?? "no model") : "Phoenix server · stopped";
            _tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;   // the tray tooltip limit
        }

        // Tray actions run off the UI thread (a model unload can take a moment); the menu updates when done.
        static void Run(ThreadStart work)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { work(); } catch (Exception ex) { Notify("Phoenix server", ex.Message); }
            });
        }

        static void Open(string page)
        {
            WebServer s = _server;
            if (s == null) return;
            try { Process p = WebServer.OpenWindow(s.BaseUrl + page, s.Log); if (p != null) p.Dispose(); else MessageBox.Show("Chrome or Edge is needed to show " + page + ".", "Phoenix LAI server"); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Phoenix LAI server"); }
        }

        static void Notify(string title, string text)
        {
            NotifyIcon t = _tray;
            if (t == null) return;
            try { t.BalloonTipTitle = title; t.BalloonTipText = text; t.ShowBalloonTip(3000); } catch { }
        }

        // The tray icon is drawn at the tray's own size for this display by FleetTrayArt.cs (edge-ops ui-kit/tray
        // v1.0.0, commit c2172f7), so Windows never shrinks it; the old 32 px PNG through GetHicon was small and blurry.
        static Icon LoadIcon()
        {
            try { return FleetTray.Art.ForApp("phoenix", null); }
            catch { return SystemIcons.Application; }
        }
    }
}
