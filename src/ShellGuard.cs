using System;
using System.IO;

namespace Phoenix
{
    // Hard security rule: the model never gets to run PowerShell, cmd or any other program or command.
    // Enforced in three places that do not depend on settings, prompts or the model:
    //   1. The model's only tools are <list>, <read> and <write> (Workspace); command-style tags are refused, not run.
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
            if (p.Contains("/.git/") || p.EndsWith("/.git") || p.Contains("/.vscode/") || p.Contains("/.idea/") || name == "desktop.ini" ||
                name == ".gitattributes" || name == ".gitmodules")
                throw new UnauthorizedAccessException("Refused: the model may not write into .git, .vscode or .idea, or tool configuration files that run commands.");
        }
    }
}
