// Which app windows are open, and when the server should stop itself (TechStack "Lifecycle rules"). C# 5 only.
// Every window posts /api/heartbeat every 5 s and a "leaving" beacon on pagehide. A window counts as open until 20 s
// after its last heartbeat, or 5 s after it said it was leaving with no heartbeat since: a reload says "leaving" and
// checks in again (with a new page id) well inside that, so it never stops the server. With several windows open the
// server stops only when none is left. The chat inside VS Code (page ids starting "vs-") does not keep the server up:
// closing the app window ends the session, as before. A minimized window is "hidden" and Chrome then runs its timers
// only about once a minute, so a page that said it is hidden gets HiddenTimeout instead of 20 s (closing it still sends
// the leaving beacon, so the 5 s rule applies). Time is passed in, so the tests can simulate it.
using System;
using System.Collections.Generic;

namespace Phoenix
{
    public sealed class Presence
    {
        public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(20);
        public static readonly TimeSpan LeavingGrace = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan HiddenTimeout = TimeSpan.FromSeconds(75);
        // A window that never checks in (the browser failed to open the page) still ends the session after this long.
        public static readonly TimeSpan FirstContactTimeout = TimeSpan.FromSeconds(60);

        sealed class Page { public DateTime Last; public DateTime LeftAt; public bool Leaving, Hidden; }

        readonly object _gate = new object();
        readonly Dictionary<string, Page> _pages = new Dictionary<string, Page>();
        readonly DateTime _started;
        bool _windowSeen;

        public Presence(DateTime startedUtc) { _started = startedUtc; }

        // Page ids are short random strings made by chat.js: letters, digits and '-' only, at most 40 characters.
        public static bool ValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 40) return false;
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-')) return false;
            return true;
        }

        static bool IsAppWindow(string id) { return !id.StartsWith("vs-", StringComparison.Ordinal); }

        // A check-in that does not say whether the page is hidden (the /api/info status poll) keeps what it said last.
        public void Beat(string id, DateTime now) { Beat(id, now, null); }

        public void Beat(string id, DateTime now, bool? hidden)
        {
            if (!ValidId(id)) return;
            lock (_gate)
            {
                Page p;
                if (!_pages.TryGetValue(id, out p)) { p = new Page(); _pages[id] = p; }
                p.Last = now; p.Leaving = false;
                if (hidden.HasValue) p.Hidden = hidden.Value;
                if (IsAppWindow(id)) _windowSeen = true;
            }
        }

        public void Leave(string id, DateTime now)
        {
            if (!ValidId(id)) return;
            lock (_gate)
            {
                Page p;
                if (_pages.TryGetValue(id, out p)) { p.Leaving = true; p.LeftAt = now; }
            }
        }

        static bool Alive(Page p, DateTime now)
        {
            if (p.Leaving) return now - p.LeftAt < LeavingGrace;
            return now - p.Last < (p.Hidden ? HiddenTimeout : HeartbeatTimeout);
        }

        // App windows open now (pages that are gone are forgotten).
        public int OpenWindows(DateTime now)
        {
            lock (_gate)
            {
                List<string> gone = new List<string>();
                int n = 0;
                foreach (KeyValuePair<string, Page> kv in _pages)
                {
                    if (!Alive(kv.Value, now)) gone.Add(kv.Key);
                    else if (IsAppWindow(kv.Key)) n++;
                }
                foreach (string k in gone) _pages.Remove(k);
                return n;
            }
        }

        // True when the app window is gone: it checked in once and no window is open now, or none ever checked in.
        public bool Gone(DateTime now)
        {
            int open = OpenWindows(now);
            lock (_gate)
            {
                if (!_windowSeen) return now - _started >= FirstContactTimeout;
                return open == 0;
            }
        }

        // The stop rule as one decision, for the server's tick and the tests: "stop", "wait" (a stop is due but a background
        // job is still running: stop on the next tick after it ends) or "run".
        public static string Decide(bool stopRequested, bool autoStop, bool windowGone, string busyWith)
        {
            if (!stopRequested && !(autoStop && windowGone)) return "run";
            return busyWith != null ? "wait" : "stop";
        }
    }
}
