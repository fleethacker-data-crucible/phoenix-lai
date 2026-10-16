// Session log for the chat server: a readable .log and a machine-readable .jsonl, both under logs\. C# 5 only.
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Phoenix
{
    public sealed class ChatLog
    {
        readonly object _lock = new object();
        public readonly string TextPath, JsonPath;

        public ChatLog(string logsDir)
        {
            Directory.CreateDirectory(logsDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            TextPath = Path.Combine(logsDir, "session-" + stamp + ".log");
            JsonPath = Path.Combine(logsDir, "session-" + stamp + ".jsonl");
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
}
