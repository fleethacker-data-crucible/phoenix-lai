using System;
using System.IO;

namespace Phoenix
{
    // Hard security rule: the model never gets to run PowerShell, cmd or any other program or command.
    // Enforced in three places that do not depend on settings, prompts or the model:
    //   1. The model's only tools are file tools (list, search, read, edit, write, delete in Workspace); command-style tags
    //      are refused, not run, and a user who keeps asking to run things is warned, then suspended (CheckRunRequest).
    //   2. The model cannot write files Windows would execute on a double-click (CheckWrite).
    //   3. The app's single process launch (the chat window) passes CheckLaunch: only the browser, only in app mode on
    //      the loopback address. run.ps1 refuses to start if any source names a shell or starts a process elsewhere.
    public static class ShellGuard
    {
        static readonly string[] Browsers = { "msedge.exe", "chrome.exe" };

        // Extensions Windows runs (or hands to a script host) when the file is double-clicked. Source files such as .ps1 or
        // .js stay writable: opening them edits them, and nothing here ever runs them.
        static readonly string[] Executable = {
            ".exe", ".com", ".scr", ".pif", ".cpl", ".msi", ".msp", ".msc", ".dll", ".sys", ".drv", ".ocx",
            ".bat", ".cmd", ".psc1", ".vbs", ".vbe", ".jse", ".wsf", ".wsh", ".wsc",
            ".hta", ".lnk", ".url", ".scf", ".reg", ".inf", ".application", ".appref-ms", ".jar", ".gadget", ".settingcontent-ms" };

        // Tags a model might use to ask for a command; they are reported back as refused.
        public static readonly string[] CommandTags = {
            "run", "exec", "execute", "shell", "cmd", "command", "powershell", "pwsh", "bash", "sh", "terminal", "console", "script", "process" };

        public const string Refusal = "Refused: running commands or programs is permanently disabled in this app.";

        // For the model's instructions (Workspace.ToolInstructions): what to do when asked to run something.
        public const string RunAdvice =
            "When the user asks you to run, build, test or install something, say in one short sentence that you cannot run commands, " +
            "then give the exact command for them to run themselves in their terminal, in a ```powershell code block (run from the project folder). ";

        // ---- Requests from the user to run something. The model can never run anything; a user who keeps asking is
        // warned on the third request within an hour (a warning only), and on the
        // sixth the server stops accepting any message for 5 minutes.
        // Asking the model to run something, in any wording: "run this script", "I need you to run it", "can you execute the
        // build", "fire up the server", "test it for me", "let's install the packages", or a message that is just a command
        // ("npm start", ".\run.ps1"). Asking HOW ("how do I run this?", "what's the command to run the tests?") never counts.
        // Action: a verb that means "make it execute", alone or with what it acts on.
        const string Act =
            @"(re-?run|run|execute|exec|launch|invoke|trigger|deploy" +
            @"|fire\s+(it\s+|that\s+|this\s+)?up|kick\s+(it\s+|that\s+|this\s+)?off|spin\s+(it\s+|that\s+|this\s+)?up|boot\s+(it\s+|that\s+|this\s+)?up" +
            @"|start\s+(it|this|that|the\s+\w+|up|running)|restart\s+(it|this|that|the\s+\w+)" +
            @"|test\s+(it|this|that|them|the\s+\w+)|build\s+(it|this|that|the\s+\w+)|compile\s+(it|this|that|the\s+\w+)" +
            @"|install\s+(it|this|that|them|the\s+\w+|dependencies|packages|\w+\s+packages)|open\s+(it|this|that)\s+in\s+(the\s+)?(terminal|shell|console)" +
            @"|do\s+a\s+(test\s+|dry\s+)?run|give\s+it\s+a\s+(run|go|spin|whirl)|try\s+(running|executing|launching)|type\s+(it|this|that)\s+in\s+(the\s+)?(terminal|shell|console)" +
            @"|paste\s+(it|this|that)\s+(in|into)\s+(the\s+)?(terminal|shell|console))\b";
        // A request frame around the action.
        static readonly System.Text.RegularExpressions.Regex RunAsk = new System.Text.RegularExpressions.Regex(
            // imperative, at the start of a sentence or after a softener: "run it", "please run this", "go ahead and build it", "ok, test it"
            @"(^|[.!?;:\n]\s*|\b(please|pls|plz|now|just|then|and|also|so|ok(ay)?|alright|go\s+ahead\s+and|go\s+on\s+and|can\s+we|let'?s|lets|let\s+us)[,]?\s+)" + Act +
            // asked of the model: "can you run it", "would you mind running", "will you please execute"
            @"|\b(can|could|would|will|won'?t)\s+you\s+(please\s+|just\s+|now\s+|go\s+(ahead\s+and\s+)?)?" + Act +
            @"|\bwould\s+you\s+mind\s+(running|executing|launching|starting|testing|building|compiling|installing|deploying)\b" +
            // "I need / want / would like you to run", "I need it run", "we need you to execute"
            @"|\b(i|we)\s*('d|\s+would)?\s+(need|want|like|expect|require)\s+(you|phoenix|the\s+model|it)\s+to\s+" + Act +
            @"|\b(i|we)\s+(need|want)\s+(it|this|that|them|the\s+\w+)\s+(run|executed|launched|built|compiled|tested|installed|deployed)\b" +
            // "I am ordering you to run it", "I'm telling you to execute it", "I command you to run", "I demand you run it"
            @"|\b(order|ordering|ordered|command|commanding|commanded|tell|telling|told|ask|asking|asked|instruct|instructing|instructed|demand|demanding|direct|directing)\s+(you|phoenix|the\s+model)\s+(to\s+)?" + Act +
            @"|\byou('re|\s+are)\s+(going\s+to|gonna|supposed\s+to|required\s+to|allowed\s+to)\s+" + Act +
            // "you should / need to / must run", "you can run it now", "your job is to run"
            @"|\byou\s+(should|need\s+to|must|have\s+to|gotta|can|could|are\s+going\s+to|will)\s+(now\s+|just\s+|go\s+(ahead\s+and\s+)?)?" + Act +
            @"|\b(your\s+job|the\s+task)\s+is\s+to\s+" + Act +
            // "run it for me", "execute that now", "build it please"
            @"|\b(run|execute|launch|build|compile|test|install|deploy)\s+(it|this|that|them)\s+(for\s+me|now|please|already|right\s+now)\b" +
            // a message that is only a command line: "npm start", ".\run.ps1", "python app.py", "dotnet build"
            @"|^\s*(npm|npx|yarn|pnpm|node|python3?|py|pip3?|dotnet|cargo|go\s+run|java|javac|gcc|cmake|msbuild|git|docker|kubectl|choco|winget|scoop|powershell|pwsh|cmd|bash|sh)\s+\S+[^\n]*\s*$" +
            @"|^\s*\.{1,2}[\\/]\S+\.(ps1|bat|cmd|exe|sh|py)\b[^\n]*\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        // Questions about how to run something: never a request for the model to run it.
        static readonly System.Text.RegularExpressions.Regex HowTo = new System.Text.RegularExpressions.Regex(
            @"\bhow\s+(do|can|should|would|could|does|is)\s+(i|we|one|it|this|that)\b|\bhow\s+to\b|\bhow\s+would\s+i\b" +
            @"|\bwhat('s|\s+is|\s+are)?\s+(the\s+)?(command|commands|way|steps)\b|\bwhich\s+command\b|\bwhere\s+do\s+i\b" +
            @"|\b(show|tell|give|teach)\s+me\s+(how|the\s+command|the\s+steps|what\s+to\s+(type|run))\b|\bwhat\s+do\s+i\s+(type|run|enter)\b" +
            @"|\b(explain|instructions)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // ---- A lockout outlives a server restart and resists tampering. It is kept in two files, each signed:
        //   %LOCALAPPDATA%\LocalLLMStudio\guard.dat   and   %APPDATA%\PhoenixLAI\guard.dat  (no registry: production has none)
        // as "v1|<until UTC ticks>|<written UTC ticks>|<HMAC-SHA256>". The HMAC key comes from the machine name, the user's SID
        // and a fixed salt, so an edited copy, or one copied from another machine or user, does not check. A copy that fails
        // its check counts as tampering: a fresh lockout. Deleting one copy does nothing (the other holds and restores it).
        // Setting the clock back does not shorten it (the time left when it was written still has to pass). The model
        // cannot reach either file: its tools only touch Phoenix_Projects.
        static readonly string[] LockFiles = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLLMStudio", "guard.dat"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PhoenixLAI", "guard.dat") };

        static byte[] LockMacKey()
        {
            string sid = "";
            try { sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value; } catch { }
            using (System.Security.Cryptography.SHA256 h = System.Security.Cryptography.SHA256.Create())
                return h.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Environment.MachineName + "|" + sid + "|PhoenixLAI run guard v1"));
        }

        static string Sign(string body)
        {
            using (System.Security.Cryptography.HMACSHA256 m = new System.Security.Cryptography.HMACSHA256(LockMacKey()))
                return BitConverter.ToString(m.ComputeHash(System.Text.Encoding.UTF8.GetBytes(body))).Replace("-", "");
        }

        // A stored copy: null = missing; DateTime.MaxValue = present but tampered.
        static DateTime? ParseLock(string s, DateTime now)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string[] p = s.Trim().Split('|');
            if (p.Length != 4 || p[0] != "v1") return DateTime.MaxValue;
            if (!string.Equals(Sign(p[0] + "|" + p[1] + "|" + p[2]), p[3], StringComparison.OrdinalIgnoreCase)) return DateTime.MaxValue;
            long until, written;
            if (!long.TryParse(p[1], out until) || !long.TryParse(p[2], out written)) return DateTime.MaxValue;
            DateTime u = new DateTime(until, DateTimeKind.Utc), w = new DateTime(written, DateTimeKind.Utc);
            if (now < w) u = now + (u - w);   // the clock was set back: the time that was left still has to pass
            return u;
        }

        static DateTime LoadLockout()
        {
            DateTime now = DateTime.UtcNow, until = DateTime.MinValue;
            bool tampered = false, missing = false;
            foreach (string f in LockFiles)
            {
                string text = null;
                try { if (File.Exists(f)) text = File.ReadAllText(f); } catch { }
                DateTime? u = ParseLock(text, now);
                if (u == DateTime.MaxValue) tampered = true;
                else if (!u.HasValue) missing = true;
                else if (u.Value > until) until = u.Value;
            }
            if (tampered) { DateTime fresh = now.AddMinutes(SuspendMinutes); SaveLockout(fresh); return fresh; }   // edited: start a new lockout
            if (until > now && missing) SaveLockout(until);   // one copy was deleted: put it back
            return until;
        }

        static void SaveLockout(DateTime until)
        {
            string body = "v1|" + until.Ticks + "|" + DateTime.UtcNow.Ticks;
            string line = body + "|" + Sign(body);
            foreach (string f in LockFiles)
                try { Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, line); } catch { }
        }

        static readonly object _gate = new object();
        static readonly System.Collections.Generic.List<DateTime> _runAsks = new System.Collections.Generic.List<DateTime>();
        static DateTime _suspendedUntil = LoadLockout();   // a lockout from before a restart still holds
        public const int SuspendMinutes = 5;

        public static bool IsRunRequest(string userText)
        {
            if (string.IsNullOrEmpty(userText)) return false;
            // only the user's own words: the VS Code extension appends editor context after a blank line and a "[Open in" marker
            int ctx = userText.IndexOf("\n\n[Open in the editor", StringComparison.Ordinal);
            string t = ctx >= 0 ? userText.Substring(0, ctx) : userText;
            return RunAsk.IsMatch(t) && !HowTo.IsMatch(t);
        }

        // null: let the model answer. Otherwise the server's own reply (warning, suspension, or still suspended).
        // level: "warn" (third request) or "suspend" (locked out, now or still), for the red WARNING banner; seconds: lockout left.
        public static string CheckRunRequest(string userText) { string level; int seconds; return CheckRunRequest(userText, out level, out seconds); }

        public static string CheckRunRequest(string userText, out string level, out int seconds)
        {
            level = null; seconds = 0;
            lock (_gate)
            {
                DateTime now = DateTime.UtcNow;
                if (now < _suspendedUntil)
                {
                    level = "suspend"; seconds = (int)Math.Ceiling((_suspendedUntil - now).TotalSeconds);
                    SaveLockout(_suspendedUntil);   // refresh the written time (clock-rollback check)
                    int left = (int)Math.Ceiling((_suspendedUntil - now).TotalMinutes);
                    return "Chat is suspended for " + left + " more minute" + (left == 1 ? "" : "s") + " after repeated requests to run commands. " +
                           "Phoenix never runs commands, scripts or programs.";
                }
                if (!IsRunRequest(userText)) return null;
                _runAsks.RemoveAll(delegate(DateTime d) { return (now - d).TotalMinutes > 60; });
                _runAsks.Add(now);
                if (_runAsks.Count == 3)
                {
                    level = "warn";
                    return "Phoenix never runs commands, scripts or programs, and this is the third request to run something. " +
                           "Run the command yourself in your terminal. Keep asking and this chat will be suspended for " + SuspendMinutes + " minutes.";
                }
                if (_runAsks.Count >= 6)
                {
                    _runAsks.Clear();
                    _suspendedUntil = now.AddMinutes(SuspendMinutes);
                    SaveLockout(_suspendedUntil);
                    level = "suspend"; seconds = SuspendMinutes * 60;
                    return "Chat suspended: repeated requests to run commands. No messages are accepted for " + SuspendMinutes + " minutes. Phoenix never runs commands, scripts or programs.";
                }
                return null;
            }
        }

        // Throws unless this is the chat window: a known browser, app mode, loopback page.
        public static void CheckLaunch(string exe, string args)
        {
            string name = Path.GetFileName(exe ?? "").ToLowerInvariant();
            if (Array.IndexOf(Browsers, name) < 0) throw new UnauthorizedAccessException("ShellGuard: launching '" + name + "' is not allowed.");
            if (args == null || !args.StartsWith("--app=") || args.IndexOf("://127.0.0.1:", StringComparison.Ordinal) < 0 ||
                args.IndexOfAny(new char[] { '&', '|', '<', '>', '^', '%', '\n', '\r' }) >= 0)
                throw new UnauthorizedAccessException("ShellGuard: browser arguments are not allowed: " + args);
        }

        // Throws if the model tries to create a file type Windows would execute.
        public static void CheckWrite(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant().TrimEnd('.', ' ');
            foreach (string ext in Executable)
                if (name.EndsWith(ext)) throw new UnauthorizedAccessException("Refused: the model may not create executable or script files (" + ext + ").");
            if (name.IndexOf(':') >= 0) throw new UnauthorizedAccessException("Refused: alternate data streams are not allowed.");
            // Indirect execution: other tools run files from these places by themselves (git runs .git/hooks on commit,
            // VS Code runs .vscode/tasks.json "runOn: folderOpen" tasks, the shell runs desktop.ini handlers), so a model
            // write there would be a command run later. Refused.
            string p = path.Replace('\\', '/').ToLowerInvariant();
            if (p.Contains("/.git/") || p.EndsWith("/.git") || p.Contains("/.vscode/") || p.Contains("/.idea/") || p.Contains("/.phoenix/") || name == "desktop.ini" ||
                name == ".gitattributes" || name == ".gitmodules")
                throw new UnauthorizedAccessException("Refused: the model may not write into .git, .vscode, .idea or .phoenix, or tool configuration files that run commands.");
        }
    }
}
