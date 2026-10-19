// Saved chats (the sidebar's Recents) and projects (folders in Phoenix_Projects). C# 5 only.
//   Chats live in the active project's .phoenix\chats (the project the connected folder belongs to), or with no
//   project in <user home>\.phoenix\chats: <id>.json is the page's own saved state (what is on screen and the
//   history), <id>.meta is {title, updated, project} so the list never reads the big files.
//   A project is a folder directly inside Workspace.ProjectsRoot. Its layout (EnsureProjectLayout):
//     RULES.md            the project's rules (read into every prompt; the starter text is a comment, so it adds nothing)
//     .phoenix\chats      saved chats            .phoenix\index     the project index (map of files and functions)
//     .phoenix\memories   project memories       .phoenix\recall    Instant Recall snapshots
//     .phoenix\skills     project skills (SKILL.md folders, see Skills.cs and WebServer.Skills.cs)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        static string AppChatsDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".phoenix", "chats"); } }

        // The project folder (directly under the projects root) that the connected folder belongs to, or null.
        static string ProjectRootOf(Workspace w)
        {
            if (w == null) return null;
            string root = Path.GetFullPath(Workspace.ProjectsRoot).TrimEnd(Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(w.Root);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            string first = full.Substring(root.Length + 1).Split(Path.DirectorySeparatorChar)[0];
            return Path.Combine(root, first);
        }

        // Where chats are saved and listed now: the active project's .phoenix\chats, else the app's own folder.
        string ChatsDir { get { string p = ProjectRootOf(_workspace); return p != null ? Path.Combine(p, ".phoenix", "chats") : AppChatsDir; } }

        const string RulesStarter =
            "<!-- RULES.md: rules for this project. Phoenix adds everything outside this comment to every prompt while the\r\n" +
            "     project is active. Write one rule per line, for example:\r\n" +
            "     - Target .NET Framework 4.8 and C# 5.\r\n" +
            "     - Never add third-party packages. -->\r\n";

        // Creates the project's .phoenix folders (hidden) and a starter RULES.md when they are missing.
        public static void EnsureProjectLayout(string projectRoot)
        {
            if (projectRoot == null || !Directory.Exists(projectRoot)) return;
            string ph = Path.Combine(projectRoot, ".phoenix");
            foreach (string sub in new[] { "chats", "index", "memories", "recall", "skills" }) Directory.CreateDirectory(Path.Combine(ph, sub));
            try { new DirectoryInfo(ph).Attributes |= FileAttributes.Hidden; } catch { }
            string rules = Path.Combine(projectRoot, "RULES.md");
            if (!File.Exists(rules)) File.WriteAllText(rules, RulesStarter, new UTF8Encoding(false));
        }
        static readonly Regex ChatId = new Regex(@"^[a-z0-9-]{8,64}$");
        const int MaxChats = 500;

        string ChatPath(string id, string ext)
        {
            if (id == null || !ChatId.IsMatch(id)) throw new ArgumentException("Bad chat id.");
            return Path.Combine(ChatsDir, id + ext);
        }

        // Handles the chat and project endpoints: the JSON reply, or null when the path is not one of them.
        string ChatsApi(string path, bool post, Dictionary<string, object> body, string queryId)
        {
            if (path == "/api/chats") return ChatListJson();
            if (path == "/api/chat-get")
            {
                string f = ChatPath(queryId, ".json");
                if (!File.Exists(f)) throw new FileNotFoundException("That chat is gone.");
                return "{\"data\":" + Json.Quote(File.ReadAllText(f, Encoding.UTF8)) + "}";
            }
            if (path == "/api/chat-save" && post)
            {
                string id = Json.GetStr(body, "id", null);
                string data = Json.GetStr(body, "data", "");
                Directory.CreateDirectory(ChatsDir);
                string title = Json.GetStr(body, "title", "").Trim();
                if (title.Length > 80) title = title.Substring(0, 80).TrimEnd() + "…";
                // the big file first, then the meta, each written to a temp file and swapped in (a crash keeps the old copy)
                WriteSwap(ChatPath(id, ".json"), data);
                WriteSwap(ChatPath(id, ".meta"), "{\"title\":" + Json.Quote(title.Length > 0 ? title : "New chat") +
                    ",\"updated\":" + (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds +
                    ",\"project\":" + Json.Quote(Json.GetStr(body, "project", "")) + "}");
                PruneChats();
                return ChatListJson();
            }
            if (path == "/api/chat-delete" && post)
            {
                string id = Json.GetStr(body, "id", null);
                foreach (string ext in new[] { ".json", ".meta" }) { string f = ChatPath(id, ext); if (File.Exists(f)) File.Delete(f); }
                if (Log != null) Log.Text("chat deleted: " + id);
                return ChatListJson();
            }
            if (path == "/api/projects") return ProjectListJson();
            if (path == "/api/project-delete" && post)
            {
                // Not erased: moved into Phoenix_Projects\.llm-backup\<name>-<time> (like the model's delete), so it can be restored.
                string name = ValidProjectName(Json.GetStr(body, "name", ""));
                string dir = Path.Combine(Workspace.ProjectsRoot, name);
                if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("There is no project \"" + name + "\".");
                if (_workspace != null && (_workspace.Root + Path.DirectorySeparatorChar).StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    _workspace = null;   // it was the connected folder
                string bak = Path.Combine(Workspace.ProjectsRoot, ".llm-backup");
                Directory.CreateDirectory(bak);
                try { new DirectoryInfo(bak).Attributes |= FileAttributes.Hidden; } catch { }
                string dest = Path.Combine(bak, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.Move(dir, dest);
                if (Log != null) Log.Text("project removed: " + dir + " (moved to " + dest + ")");
                return ProjectListJson();
            }
            if (path == "/api/project-create" && post)
            {
                string name = ValidProjectName(Json.GetStr(body, "name", ""));
                Workspace.EnsureProjectsRoot();
                string dir = Path.Combine(Workspace.ProjectsRoot, name);
                if (Directory.Exists(dir)) throw new InvalidOperationException("A project named \"" + name + "\" already exists.");
                Directory.CreateDirectory(dir);
                EnsureProjectLayout(dir);
                if (Log != null) Log.Text("project created: " + dir);
                return ProjectListJson();
            }
            return null;
        }

        static void WriteSwap(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }

        // One folder name: no path parts, nothing Windows refuses, no leading dot (hidden), not too long.
        static readonly string[] Reserved = { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                                              "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };
        static string ValidProjectName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("Give the project a name.");
            if (name.Length > 64) throw new ArgumentException("Project names are at most 64 characters.");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new ArgumentException("A project name cannot contain \\ / : * ? \" < > |.");
            if (name.StartsWith(".") || name.EndsWith(".") || name.EndsWith(" ")) throw new ArgumentException("A project name cannot start or end with a dot, or end with a space.");
            string stem = name.Split('.')[0].ToUpperInvariant();
            if (Array.IndexOf(Reserved, stem) >= 0) throw new ArgumentException("\"" + name + "\" is a name Windows reserves.");
            return name;
        }

        static string ProjectListJson()
        {
            List<string> names = new List<string>();
            try
            {
                Workspace.EnsureProjectsRoot();
                foreach (string d in Directory.GetDirectories(Workspace.ProjectsRoot))
                {
                    if ((new DirectoryInfo(d).Attributes & FileAttributes.Hidden) != 0 || Path.GetFileName(d).StartsWith(".")) continue;
                    names.Add(Path.GetFileName(d));
                }
            }
            catch { }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            StringBuilder sb = new StringBuilder("{\"root\":" + Json.Quote(Workspace.ProjectsRoot) + ",\"projects\":[");
            for (int i = 0; i < names.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json.Quote(names[i])); }
            return sb.Append("]}").ToString();
        }

        sealed class ChatMeta { public string Id, Json; public long Updated; }

        List<ChatMeta> ChatMetas()
        {
            List<ChatMeta> list = new List<ChatMeta>();
            MoveOldChats();
            if (!Directory.Exists(ChatsDir)) return list;
            foreach (string f in Directory.GetFiles(ChatsDir, "*.meta"))
            {
                try
                {
                    string text = File.ReadAllText(f, Encoding.UTF8);
                    Dictionary<string, object> m = Json.Parse(text) as Dictionary<string, object>;
                    if (m == null) continue;
                    ChatMeta c = new ChatMeta();
                    c.Id = Path.GetFileNameWithoutExtension(f);
                    c.Updated = (long)Json.GetNum(m, "updated", 0);
                    c.Json = "{\"id\":" + Json.Quote(c.Id) + ",\"title\":" + Json.Quote(Json.GetStr(m, "title", "Chat")) +
                             ",\"updated\":" + c.Updated + ",\"project\":" + Json.Quote(Json.GetStr(m, "project", "")) + "}";
                    list.Add(c);
                }
                catch { }
            }
            list.Sort(delegate(ChatMeta a, ChatMeta b) { return b.Updated.CompareTo(a.Updated); });
            return list;
        }

        // Chats saved before they moved to <user home>\.phoenix\chats (they were in %LOCALAPPDATA%\LocalLLMStudio\chats).
        static void MoveOldChats()
        {
            string old = Path.Combine(AppDataDir, "chats");
            if (!Directory.Exists(old)) return;
            try
            {
                Directory.CreateDirectory(AppChatsDir);
                foreach (string f in Directory.GetFiles(old))
                {
                    string dst = Path.Combine(AppChatsDir, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Move(f, dst);
                }
                if (Directory.GetFiles(old).Length == 0) Directory.Delete(old);
            }
            catch { }
        }

        string ChatListJson()
        {
            List<ChatMeta> list = ChatMetas();
            StringBuilder sb = new StringBuilder("{\"chats\":[");
            for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); sb.Append(list[i].Json); }
            string proj = ProjectRootOf(_workspace);
            return sb.Append("],\"project\":").Append(proj != null ? Json.Quote(Path.GetFileName(proj)) : "null").Append('}').ToString();
        }

        // Keeps the newest MaxChats.
        void PruneChats()
        {
            List<ChatMeta> list = ChatMetas();
            for (int i = MaxChats; i < list.Count; i++)
                foreach (string ext in new[] { ".json", ".meta" })
                    try { File.Delete(ChatPath(list[i].Id, ext)); } catch { }
        }
    }
}
