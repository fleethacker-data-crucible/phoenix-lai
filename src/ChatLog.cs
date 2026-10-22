// Session log for the chat server: a readable .log and a machine-readable .jsonl, both under logs\. C# 5 only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Phoenix
{
    public sealed class ChatLog
    {
        readonly object _lock = new object();
        public readonly string TextPath, JsonPath;

        public ChatLog(string logsDir) : this(logsDir, "session") { }

        // prefix: "session" (the server) or "client" (run-phoenix-client.ps1)
        public ChatLog(string logsDir, string prefix)
        {
            Directory.CreateDirectory(logsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            TextPath = Path.Combine(logsDir, prefix + "-" + stamp + ".log");
            JsonPath = Path.Combine(logsDir, prefix + "-" + stamp + ".jsonl");
        }

        static string Now() { return DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture); }

        // Readable line(s), prefixed with the time.
        public void Text(string text)
        {
            lock (_lock)
            {
                try { File.AppendAllText(TextPath, "[" + Now() + "] " + text.Replace("\n", "\n               ") + "\n", Encoding.UTF8); }
                catch { } // logging must never break chatting
            }
        }

        // One JSON object per line; `fields` is the inside of the object without braces, e.g. "\"a\":1,\"b\":\"x\"".
        public void Json(string evt, string fields)
        {
            lock (_lock)
            {
                try
                {
                    string line = "{\"time\":" + Phoenix.Json.Quote(DateTime.Now.ToString("o", CultureInfo.InvariantCulture)) +
                                  ",\"event\":" + Phoenix.Json.Quote(evt) + (string.IsNullOrEmpty(fields) ? "" : "," + fields) + "}\n";
                    File.AppendAllText(JsonPath, line, Encoding.UTF8);
                }
                catch { }
            }
        }

        public static string Ms(double seconds) { return ((int)Math.Round(seconds * 1000)).ToString(CultureInfo.InvariantCulture); }
    }

    // Debug mode (/debug on|off, kept across restarts): every step the server takes and every exception any code throws,
    // written to the session .log as "[debug <area>] ..." lines. Off: nothing is written and the calls cost one check.
    //   Dbg.Msg("rag", "embedded chunk 12/729 in 1442 ms")     a step
    //   Dbg.Error("api", ex)                                    an exception that was handled (with its stack)
    // Every exception thrown anywhere (first-chance, including the ones code catches and ignores) is logged too, with the
    // method that threw it; Windows' own noise (a closed browser connection) is rate-limited, never hidden.
    public static class Dbg
    {
        static ChatLog _log;
        static volatile bool _on;
        static string _flagFile;
        [ThreadStatic] static bool _inHook;
        static readonly object _rate = new object();
        static string _lastKey;
        static int _repeats;
        static DateTime _lastAt;

        public static bool On { get { return _on; } }

        // Who asked for the current generation on this thread ("window", "vscode", "api", "answer-while-typing"...),
        // so a logged prompt and reply say where they came from.
        [ThreadStatic] public static string Source;

        // Debug mode: the full prompt exactly as the model sees it (system instructions, files, docs, tool results).
        public static void Prompt(IList<ChatMessage> msgs)
        {
            if (!_on || _log == null) return;
            StringBuilder sb = new StringBuilder("[debug prompt from " + (Source ?? "?") + "] " + msgs.Count + " messages:");
            foreach (ChatMessage m in msgs) sb.Append("\n----- ").Append(m.Role).Append(" -----\n").Append(m.Text);
            _log.Text(sb.ToString());
        }

        // Debug mode: the model's complete raw reply (tool tags included).
        public static void Reply(string text, string stop)
        {
            if (!_on || _log == null) return;
            _log.Text("[debug reply to " + (Source ?? "?") + "] (" + stop + ")\n" + text);
        }

        // Called once at startup: where to write, and where the on/off choice is kept.
        public static void Attach(ChatLog log, string appDataDir)
        {
            _log = log;
            _flagFile = Path.Combine(appDataDir, "debug.txt");
            bool on = false;
            try { on = File.Exists(_flagFile) && File.ReadAllText(_flagFile).Trim() == "on"; } catch { }
            AppDomain.CurrentDomain.FirstChanceException += delegate(object s, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e) { FirstChance(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (_log != null) _log.Text("UNHANDLED " + (ex != null ? ex.ToString() : Convert.ToString(e.ExceptionObject)));
            };
            Set(on);
        }

        public static void Set(bool on)
        {
            _on = on;
            try { if (_flagFile != null) { Directory.CreateDirectory(Path.GetDirectoryName(_flagFile)); File.WriteAllText(_flagFile, on ? "on" : "off"); } } catch { }
            if (_log != null) _log.Text("debug mode " + (on ? "ON: every step and every exception is logged" : "off"));
        }

        // Written whether or not debug mode is on: events that must never go unnoticed (a GPU fallback, a slow index).
        public static void Note(string area, string text)
        {
            if (_log != null) _log.Text(area + ": " + text);
        }

        public static void Msg(string area, string text)
        {
            if (!_on || _log == null) return;
            _log.Text("[debug " + area + " t" + System.Threading.Thread.CurrentThread.ManagedThreadId + "] " + text);
        }

        // An exception the code handled (it logs it here so nothing is lost); always shown in debug mode.
        public static void Error(string area, Exception ex)
        {
            if (!_on || _log == null || ex == null) return;
            _log.Text("[debug " + area + " ERROR] " + ex.GetType().Name + ": " + ex.Message + "\n" + (ex.StackTrace ?? "(no stack)"));
        }

        // Every exception at the moment it is thrown, before any catch: type, message and the method that threw it.
        static void FirstChance(Exception ex)
        {
            if (!_on || _log == null || _inHook) return;
            _inHook = true;
            try
            {
                string where = "";
                System.Reflection.MethodBase site = null;
                try { site = ex.TargetSite; } catch { }
                if (site != null) where = "thrown in " + (site.DeclaringType != null ? site.DeclaringType.FullName + "." : "") + site.Name + "; called from ";
                System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(2, false);
                for (int i = 0; i < st.FrameCount && i < 4; i++)
                {
                    System.Reflection.MethodBase m = st.GetFrame(i).GetMethod();
                    if (m == null) continue;
                    if (where.Length > 0) where += " <- ";
                    where += (m.DeclaringType != null ? m.DeclaringType.Name + "." : "") + m.Name;
                }
                // and the first frame in the server's own code (the function that called into .NET)
                for (int i = 0; i < st.FrameCount; i++)
                {
                    System.Reflection.MethodBase m = st.GetFrame(i).GetMethod();
                    Type t = m != null ? m.DeclaringType : null;
                    while (t != null && t.DeclaringType != null) t = t.DeclaringType;   // compiler-made closures: their outer class
                    if (t != null && t.Namespace == "Phoenix" && t != typeof(Dbg))
                    {
                        if (i >= 4) where += " ... in Phoenix." + t.Name + "." + m.Name;
                        break;
                    }
                }
                string key = ex.GetType().Name + "|" + ex.Message + "|" + where;
                lock (_rate)
                {
                    // the same exception over and over (a polling client that went away): one line, then a count
                    if (key == _lastKey && (DateTime.UtcNow - _lastAt).TotalSeconds < 10) { _repeats++; return; }
                    if (_repeats > 0) _log.Text("[debug exception] (previous exception repeated " + _repeats + " more times)");
                    _lastKey = key; _repeats = 0; _lastAt = DateTime.UtcNow;
                }
                _log.Text("[debug exception] " + ex.GetType().Name + ": " + ex.Message + "  at " + where);
            }
            catch { }
            finally { _inHook = false; }
        }
    }
}
