// A local folder the model may list, read and write through simple tags in its replies. C# 5 only.
//   <list path="."/>                      list a directory
//   <read path="src/app.js"/>             read a text file
//   <write path="index.html"> ... </write> create or overwrite a text file (previous version backed up)
// Paths are relative to the workspace root and can never leave it. Network (UNC) paths are refused.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoenix
{
    public sealed class Workspace
    {
        public const int MaxReadBytes = 200 * 1024, MaxWriteBytes = 2 * 1024 * 1024, MaxListEntries = 300;
        public const string BackupDir = ".llm-backup";

        public readonly string Root;

        public sealed class Action
        {
            public string Op, Path, Message, Content;   // Content: file text for read/list results fed back to the model
            public bool Ok;
        }

        public Workspace(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("No folder given.");
            if (path.StartsWith(@"\\") || path.StartsWith("//")) throw new ArgumentException("Network folders are not allowed; use a local folder.");
            string full = System.IO.Path.GetFullPath(path.Trim().Trim('"'));
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Folder not found: " + full);
            DriveInfo drive = new DriveInfo(System.IO.Path.GetPathRoot(full));
            if (drive.DriveType == DriveType.Network) throw new ArgumentException("Network drives are not allowed; use a local folder.");
            Root = full.TrimEnd(System.IO.Path.DirectorySeparatorChar);
        }

        // Resolves a relative path inside the workspace, or throws.
        public string Resolve(string rel)
        {
            if (rel == null) rel = ".";
            rel = rel.Trim().Replace('/', System.IO.Path.DirectorySeparatorChar);
            if (System.IO.Path.IsPathRooted(rel)) throw new ArgumentException("Use a path relative to the workspace folder.");
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, rel)).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            if (full != Root && !full.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Path is outside the workspace folder.");
            return full;
        }

        public string Relative(string full)
        {
            return full.Length <= Root.Length ? "." : full.Substring(Root.Length + 1).Replace('\\', '/');
        }

        // Directory entries as JSON: [{"name","path","dir","size"}], folders first; paths relative to the workspace root.
        public string ListJson(string rel)
        {
            string dir = Resolve(rel);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("No such folder: " + rel);
            StringBuilder sb = new StringBuilder("[");
            int n = 0;
            List<string> dirs = new List<string>(Directory.GetDirectories(dir)), files = new List<string>(Directory.GetFiles(dir));
            dirs.Sort(StringComparer.OrdinalIgnoreCase); files.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string d in dirs)
            {
                string name = System.IO.Path.GetFileName(d);
                if (name == BackupDir || name == ".git" || n >= MaxListEntries) continue;
                if (n++ > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Json.Quote(name)).Append(",\"path\":").Append(Json.Quote(Relative(d))).Append(",\"dir\":true}");
            }
            foreach (string f in files)
            {
                if (n >= MaxListEntries) break;
                if (n++ > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Json.Quote(System.IO.Path.GetFileName(f))).Append(",\"path\":").Append(Json.Quote(Relative(f)))
                  .Append(",\"dir\":false,\"size\":").Append(new FileInfo(f).Length).Append('}');
            }
            return sb.Append(']').ToString();
        }

        // Current git branch and commit, read straight from the .git folder (no git.exe). "null" when not a repository.
        public string GitJson()
        {
            try
            {
                string dir = Root, gitDir = null, repoRoot = null;
                while (dir != null)
                {
                    string g = System.IO.Path.Combine(dir, ".git");
                    if (Directory.Exists(g)) { gitDir = g; repoRoot = dir; break; }
                    if (File.Exists(g))
                    {
                        // Worktree / submodule: ".git" file containing "gitdir: <path>".
                        string line = File.ReadAllText(g).Trim();
                        if (line.StartsWith("gitdir:")) gitDir = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir, line.Substring(7).Trim()));
                        repoRoot = dir;
                        break;
                    }
                    DirectoryInfo parent = Directory.GetParent(dir);
                    dir = parent != null ? parent.FullName : null;
                }
                if (gitDir == null || !File.Exists(System.IO.Path.Combine(gitDir, "HEAD"))) return "null";
                string head = File.ReadAllText(System.IO.Path.Combine(gitDir, "HEAD")).Trim();
                string branch = null, commit = null;
                if (head.StartsWith("ref:"))
                {
                    string refName = head.Substring(4).Trim();
                    branch = refName.StartsWith("refs/heads/") ? refName.Substring(11) : refName;
                    commit = ResolveRef(gitDir, refName);
                }
                else commit = head;
                int changed = ChangedFiles(gitDir, repoRoot);
                return "{\"branch\":" + (branch != null ? Json.Quote(branch) : "null") +
                       ",\"commit\":" + (commit != null ? Json.Quote(commit.Length > 7 ? commit.Substring(0, 7) : commit) : "null") +
                       ",\"detached\":" + (branch == null ? "true" : "false") +
                       ",\"changed\":" + (changed >= 0 ? changed.ToString() : "null") + "}";
            }
            catch { return "null"; }
        }

        // Tracked files that look modified or deleted, from .git/index (formats v2/v3): an entry counts as changed when the
        // file is missing or its size or modification second differs from what the index recorded - the same quick stat
        // check git does before hashing. Untracked files are not counted. Returns -1 when the index can't be read.
        static int ChangedFiles(string gitDir, string repoRoot)
        {
            try
            {
                string indexPath = System.IO.Path.Combine(gitDir, "index");
                if (!File.Exists(indexPath)) return 0;
                byte[] b = File.ReadAllBytes(indexPath);
                if (b.Length < 12 || b[0] != 'D' || b[1] != 'I' || b[2] != 'R' || b[3] != 'C') return -1;
                uint version = BE32(b, 4), count = BE32(b, 8);
                if (version != 2 && version != 3) return -1;
                int pos = 12, changed = 0;
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                for (uint i = 0; i < count && i < 50000; i++)
                {
                    int start = pos;
                    uint mtime = BE32(b, pos + 8), mode = BE32(b, pos + 24), size = BE32(b, pos + 36);
                    int flags = (b[pos + 60] << 8) | b[pos + 61];
                    pos += 62;
                    if ((flags & 0x4000) != 0) pos += 2;           // v3 extended flags
                    int nul = Array.IndexOf(b, (byte)0, pos);
                    string path = Encoding.UTF8.GetString(b, pos, nul - pos);
                    pos = start + ((nul - start + 8) & ~7);          // entries are NUL-padded to a multiple of 8
                    if ((mode & 0xF000) == 0xE000) continue;         // submodule
                    if (((flags >> 12) & 3) != 0) continue;          // merge stage entries
                    string full = System.IO.Path.Combine(repoRoot, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                    FileInfo fi = new FileInfo(full);
                    if (!fi.Exists) { changed++; continue; }
                    uint m = (uint)(fi.LastWriteTimeUtc - epoch).TotalSeconds;
                    if ((uint)fi.Length != size || m != mtime) changed++;
                }
                return changed;
            }
            catch { return -1; }
        }

        static uint BE32(byte[] b, int i) { return (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]); }

        static string ResolveRef(string gitDir, string refName)
        {
            // Worktrees keep shared refs in the common dir.
            string common = gitDir;
            string commonFile = System.IO.Path.Combine(gitDir, "commondir");
            if (File.Exists(commonFile)) common = System.IO.Path.GetFullPath(System.IO.Path.Combine(gitDir, File.ReadAllText(commonFile).Trim()));
            foreach (string baseDir in new string[] { gitDir, common })
            {
                string loose = System.IO.Path.Combine(baseDir, refName.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (File.Exists(loose)) return File.ReadAllText(loose).Trim();
                string packed = System.IO.Path.Combine(baseDir, "packed-refs");
                if (File.Exists(packed))
                    foreach (string line in File.ReadAllLines(packed))
                        if (line.EndsWith(" " + refName)) return line.Substring(0, line.IndexOf(' '));
            }
            return null; // new repository with no commits yet
        }

        public string List(string rel)
        {
            string dir = Resolve(rel);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("No such folder: " + rel);
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (string d in Directory.GetDirectories(dir))
            {
                if (System.IO.Path.GetFileName(d) == BackupDir) continue;
                if (n++ >= MaxListEntries) break;
                sb.Append(Relative(d)).Append("/\n");
            }
            foreach (string f in Directory.GetFiles(dir))
            {
                if (n++ >= MaxListEntries) { sb.Append("... (more)\n"); break; }
                sb.Append(Relative(f)).Append("  (").Append(new FileInfo(f).Length).Append(" bytes)\n");
            }
            return n == 0 ? "(empty)\n" : sb.ToString();
        }

        public string Read(string rel)
        {
            string f = Resolve(rel);
            if (!File.Exists(f)) throw new FileNotFoundException("No such file: " + rel);
            byte[] b = File.ReadAllBytes(f);
            for (int i = 0; i < Math.Min(b.Length, 8000); i++) if (b[i] == 0) throw new InvalidDataException(rel + " looks like a binary file.");
            string text = Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, MaxReadBytes));
            return b.Length > MaxReadBytes ? text + "\n... (truncated at " + MaxReadBytes + " bytes)" : text;
        }

        // Writes text, keeping the previous version under .llm-backup\<timestamp>\. Returns a status message.
        public string Write(string rel, string content)
        {
            string f = Resolve(rel);
            if (f == Root || Directory.Exists(f)) throw new ArgumentException("Cannot write to a folder.");
            if (Relative(f).StartsWith(BackupDir)) throw new ArgumentException("The backup folder is read-only.");
            byte[] data = new UTF8Encoding(false).GetBytes(content);
            if (data.Length > MaxWriteBytes) throw new ArgumentException("File too large.");
            string backup = null;
            if (File.Exists(f))
            {
                backup = System.IO.Path.Combine(Root, BackupDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"), Relative(f).Replace('/', '\\'));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));
                File.Copy(f, backup, true);
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
            File.WriteAllBytes(f, data);
            return (backup != null ? "updated " : "created ") + Relative(f) + " (" + data.Length + " bytes" +
                   (backup != null ? ", previous version in " + Relative(backup) : "") + ")";
        }

        static readonly Regex WriteTag = new Regex("<write\\s+path\\s*=\\s*\"([^\"]+)\"\\s*>\\r?\\n?(.*?)</write>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex ReadTag = new Regex("<read\\s+path\\s*=\\s*\"([^\"]+)\"\\s*/?>", RegexOptions.IgnoreCase);
        static readonly Regex ListTag = new Regex("<list(?:\\s+path\\s*=\\s*\"([^\"]*)\")?\\s*/?>", RegexOptions.IgnoreCase);

        // Carries out every tag in a model reply, in order of appearance.
        public List<Action> Execute(string reply)
        {
            List<KeyValuePair<int, Action>> found = new List<KeyValuePair<int, Action>>();
            foreach (Match m in WriteTag.Matches(reply))
            {
                Action a = new Action(); a.Op = "write"; a.Path = m.Groups[1].Value;
                string body = m.Groups[2].Value;
                // Models often wrap file content in a code fence; strip it.
                Match fence = Regex.Match(body, "^\\s*```[^\\n]*\\n(.*?)\\n?```\\s*$", RegexOptions.Singleline);
                if (fence.Success) body = fence.Groups[1].Value + "\n";
                a.Content = body;
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            foreach (Match m in ReadTag.Matches(reply))
            {
                Action a = new Action(); a.Op = "read"; a.Path = m.Groups[1].Value;
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            foreach (Match m in ListTag.Matches(reply))
            {
                Action a = new Action(); a.Op = "list"; a.Path = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : ".";
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            found.Sort(delegate(KeyValuePair<int, Action> x, KeyValuePair<int, Action> y) { return x.Key.CompareTo(y.Key); });

            List<Action> done = new List<Action>();
            foreach (KeyValuePair<int, Action> kv in found)
            {
                Action a = kv.Value;
                try
                {
                    if (a.Op == "write") { a.Message = Write(a.Path, a.Content); a.Content = null; }
                    else if (a.Op == "read") { a.Content = Read(a.Path); a.Message = "read " + a.Path + " (" + a.Content.Length + " chars)"; }
                    else { a.Content = List(a.Path); a.Message = "listed " + a.Path; }
                    a.Ok = true;
                }
                catch (Exception ex) { a.Ok = false; a.Message = a.Op + " " + a.Path + " failed: " + ex.Message; a.Content = null; }
                done.Add(a);
            }
            return done;
        }

        public string ToolInstructions()
        {
            return "You can work with files in the user's project folder. To act, put these tags in your reply:\n" +
                   "<list path=\".\"/>   list a folder\n" +
                   "<read path=\"relative/path\"/>   read a file\n" +
                   "<write path=\"relative/path\">\nfull file content\n</write>   create or replace a file (always write the complete file)\n" +
                   "Use paths relative to the project folder. After <read> or <list> you will receive the results and can continue. " +
                   "When the user asks you to create or change a file, do it with <write> instead of only showing the code. " +
                   "Keep explanations short.\n\nExample:\nUser: make hello.txt that says hi\nAssistant: <write path=\"hello.txt\">\nhi\n</write>\nCreated hello.txt.\n\n" +
                   "Project folder contents:\n" + List(".");
        }
    }
}
