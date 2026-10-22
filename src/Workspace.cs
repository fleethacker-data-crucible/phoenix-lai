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
            public List<KeyValuePair<string, string>> Changes;   // edit: (find, replace) pairs
            public int From, To;                                  // read: a line range (To > 0)
            public bool All, OutlineOnly;                       // read: lines="all" / answered with the outline only
            public string Query;                                  // search: the text to find
            public bool Ok;
        }

        // Hard rule: the model only ever works inside <user home>\Phoenix_Projects. The root is fixed here, cannot be
        // configured, and every folder is given relative to it ("/my-app/src"); absolute paths are refused.
        public static readonly string ProjectsRoot = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Phoenix_Projects");

        public static readonly string PathRule = "Folders are always inside " + ProjectsRoot + " (fixed, cannot be changed). " +
            "Give the project folder relative to it, e.g. /folder my-project or /folder my-project/src";

        // Creates the projects root if it is missing (called at server startup).
        public static void EnsureProjectsRoot() { Directory.CreateDirectory(ProjectsRoot); }

        // The connected folder relative to the projects root: "/my-project/src".
        public readonly string Display;

        public Workspace(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("No folder given. " + PathRule);
            string p = path.Trim().Trim('"', '\'').Replace('\\', '/');
            // Anything naming a drive, share, device or home shortcut is refused: only paths under the projects root.
            if (p.IndexOf(':') >= 0 || p.StartsWith("//") || p.StartsWith("~"))
                throw new ArgumentException("Only subfolders of the projects root are allowed. " + PathRule);
            p = p.Trim('/');
            if (p.Length == 0) throw new ArgumentException("Pick a project folder, not the projects root itself. " + PathRule);
            foreach (string seg in p.Split('/'))
                if (seg == ".." || seg == "." || seg.Length == 0) throw new ArgumentException("'.', '..' and empty parts are not allowed. " + PathRule);
            EnsureProjectsRoot();
            string root = System.IO.Path.GetFullPath(ProjectsRoot).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, p.Replace('/', System.IO.Path.DirectorySeparatorChar)))
                .TrimEnd(System.IO.Path.DirectorySeparatorChar);
            if (!full.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only subfolders of the projects root are allowed. " + PathRule);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("No folder /" + p + " in " + root + ". " + PathRule);
            // A junction or symlink could point outside the root; none is allowed between the root and the folder.
            for (string d = full; d.Length > root.Length; d = System.IO.Path.GetDirectoryName(d))
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Linked folders (junctions/symlinks) are not allowed: /" + d.Substring(root.Length + 1).Replace('\\', '/'));
            Root = full;
            Display = "/" + full.Substring(root.Length + 1).Replace('\\', '/');
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
            // No links inside the project either: a junction would lead the model out of the projects root.
            for (string d = full; d.Length > Root.Length; d = System.IO.Path.GetDirectoryName(d))
                if ((File.Exists(d) || Directory.Exists(d)) && (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Linked files and folders (junctions/symlinks) are not allowed.");
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
            if (!File.Exists(f))
            {
                // the model reaching for its syntax library through the project tools
                if (System.Text.RegularExpressions.Regex.IsMatch(rel, @"code-syntax|\brag\b|\.index", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new FileNotFoundException("No such file: " + rel + ". That is the document library, not a project file: its matching sections are already added to the user's message when relevant. Answer from those sections (or your own knowledge) without any tool tag.");
                throw new FileNotFoundException("No such file: " + rel);
            }
            byte[] b = File.ReadAllBytes(f);
            for (int i = 0; i < Math.Min(b.Length, 8000); i++) if (b[i] == 0) throw new InvalidDataException(rel + " looks like a binary file.");
            string text = Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, MaxReadBytes));
            return b.Length > MaxReadBytes ? text + "\n... (truncated at " + MaxReadBytes + " bytes)" : text;
        }

        // Writes text, keeping the previous version under .llm-backup\<timestamp>\. Returns a status message.
        public string Write(string rel, string content)
        {
            string f = Resolve(rel);
            ShellGuard.CheckWrite(f);
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

        // Undoes a model's file change. backupRel: the copy its write / edit / delete kept in the backup folder, put back at
        // rel (the current file, if any, is backed up first, so an undo can be undone too). No backup: the file was created
        // by the change, so it is deleted (moved to the backup folder).
        public string Undo(string rel, string backupRel)
        {
            string f = Resolve(rel);
            if (string.IsNullOrEmpty(backupRel)) return Delete(rel).Replace("deleted ", "undid: removed ");
            string b = Resolve(backupRel);
            if (!Relative(b).StartsWith(BackupDir + "/")) throw new ArgumentException("Not a backup copy: " + backupRel);
            if (!File.Exists(b)) throw new FileNotFoundException("The backup copy is gone: " + backupRel);
            ShellGuard.CheckWrite(f);
            if (File.Exists(f))
            {
                string keep = System.IO.Path.Combine(Root, BackupDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-undo", Relative(f).Replace('/', '\\'));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(keep));
                File.Copy(f, keep, true);
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(f));
            File.Copy(b, f, true);
            return "undid: restored " + Relative(f) + " from " + Relative(b);
        }

        // Lines from..to (1-based, inclusive) of a file, numbered "120| text": small models lose track in big files, and
        // a range costs a fraction of the context of the whole file.
        // Files longer than this are answered with their outline unless the model asks for lines="all".
        public const int WholeReadLines = 300;
        // The project index's outline of a file (full path -> "- Name (L12)" lines), set by the server; null = none.
        public Func<string, string> Outline;

        static int CountLines(string s) { int n = 1; foreach (char c in s) if (c == '\n') n++; return n; }

        string OutlineFor(string rel, int lines)
        {
            string outline = null;
            try { if (Outline != null) outline = Outline(Resolve(rel)); } catch { }
            StringBuilder sb = new StringBuilder();
            sb.Append(rel).Append(" has ").Append(lines).Append(" lines, too many to read whole. ");
            if (outline != null)
                sb.Append("Its outline (functions and types with their first line):\n").Append(outline);
            else
            {
                // no outline (not indexed): the start of the file, numbered, to orient by
                sb.Append("Its first 60 lines:\n");
                string[] ls = Read(rel).Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < Math.Min(60, ls.Length); i++) sb.Append(i + 1).Append("| ").Append(ls[i]).Append('\n');
            }
            sb.Append("Read the part you need with <read path=\"").Append(rel).Append("\" lines=\"START-END\"/> (numbered lines), ")
              .Append("or <search query=\"...\" path=\"").Append(rel).Append("\"/>. Only if you truly need all of it: lines=\"all\".");
            return sb.ToString();
        }

        public string ReadLines(string rel, int from, int to)
        {
            string[] lines = Read(rel).Replace("\r\n", "\n").Split('\n');
            if (from < 1) from = 1;
            if (to > lines.Length) to = lines.Length;
            if (from > to) throw new ArgumentException(rel + " has " + lines.Length + " lines; lines " + from + "-" + to + " are out of range.");
            if (to - from > 1000) to = from + 1000;
            StringBuilder sb = new StringBuilder();
            for (int i = from; i <= to; i++) sb.Append(i).Append("| ").Append(lines[i - 1]).Append('\n');
            if (to < lines.Length) sb.Append("... (").Append(lines.Length).Append(" lines in all)\n");
            return sb.ToString();
        }

        // Text search across the project (case-insensitive, plain text): "path:line: text" per hit. Skips the backup folder,
        // .git, build output and binary or very large files.
        public string Search(string query, string rel)
        {
            if (string.IsNullOrEmpty(query) || query.Trim().Length < 2) throw new ArgumentException("Search for at least 2 characters.");
            // path= may be a folder (searched with its subfolders) or a single file
            string dir = Resolve(string.IsNullOrEmpty(rel) ? "." : rel);
            bool oneFile = File.Exists(dir);
            if (!oneFile && !Directory.Exists(dir)) throw new DirectoryNotFoundException("No such file or folder: " + rel);
            StringBuilder sb = new StringBuilder();
            int hits = 0, files = 0;
            Stack<string> todo = new Stack<string>();
            todo.Push(dir);
            while (todo.Count > 0 && hits < MaxSearchHits)
            {
                string d = todo.Pop();
                if (!oneFile)
                    foreach (string sub in Directory.GetDirectories(d))
                    {
                        string n = System.IO.Path.GetFileName(sub);
                        if (n == BackupDir || n == ".git" || n == ".phoenix" || n == "node_modules" || n == "bin" || n == "obj") continue;
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                        todo.Push(sub);
                    }
                foreach (string f in oneFile ? new[] { d } : Directory.GetFiles(d))
                {
                    if (hits >= MaxSearchHits) break;
                    FileInfo fi = new FileInfo(f);
                    if (fi.Length > 1024 * 1024) continue;
                    byte[] b = File.ReadAllBytes(f);
                    bool binary = false;
                    for (int i = 0; i < Math.Min(b.Length, 8000) && !binary; i++) binary = b[i] == 0;
                    if (binary) continue;
                    files++;
                    string[] lines = Encoding.UTF8.GetString(b).Replace("\r\n", "\n").Split('\n');
                    for (int i = 0; i < lines.Length && hits < MaxSearchHits; i++)
                    {
                        if (lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string l = lines[i].Trim();
                        if (l.Length > 200) l = l.Substring(0, 200) + "...";
                        sb.Append(Relative(f)).Append(':').Append(i + 1).Append(": ").Append(l).Append('\n');
                        hits++;
                    }
                }
            }
            if (hits == 0) return "No matches for \"" + query + "\" (" + files + " files searched).\n";
            if (hits >= MaxSearchHits) sb.Append("... (first ").Append(MaxSearchHits).Append(" matches; search for something more specific)\n");
            return sb.ToString();
        }
        public const int MaxSearchHits = 60;

        // The lines of a file most like the text the model tried to <find>, numbered, so a failed edit can be corrected
        // at once (small models misquote: wrong indentation, a changed word) instead of re-reading the whole file.
        // After a failed <find>: a small file whole (numbered), else the closest lines, else the first lines. A small model
        // retried the same stale <find> 6 times when it got nothing to compare against (2026-10-17).
        static string CurrentLines(string text, string find)
        {
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            if (lines.Length <= 150)
            {
                StringBuilder sb = new StringBuilder("\nCurrent contents (" + lines.Length + " lines):\n");
                for (int i = 0; i < lines.Length; i++) sb.Append(i + 1).Append("| ").Append(lines[i]).Append('\n');
                return sb.Append(MissingLines(lines, find.Replace("\r\n", "\n").Split('\n'))).ToString();
            }
            string near = ClosestLines(text.Replace("\r\n", "\n"), find.Replace("\r\n", "\n"));
            if (near.Length > 0) return near;
            StringBuilder head = new StringBuilder("\nIts first 40 lines (read other lines with lines=\"START-END\"):\n");
            for (int i = 0; i < 40; i++) head.Append(i + 1).Append("| ").Append(lines[i]).Append('\n');
            return head.ToString();
        }

        static string ClosestLines(string text, string find)
        {
            string[] lines = text.Split('\n'), want = find.Split('\n');
            if (lines.Length == 0 || want.Length == 0) return "";
            int bestAt = -1; double best = 0;
            for (int i = 0; i + want.Length <= lines.Length; i++)
            {
                double s = 0;
                for (int j = 0; j < want.Length; j++) s += Similar(lines[i + j], want[j]);
                if (s > best) { best = s; bestAt = i; }
            }
            if (bestAt < 0 || best / want.Length < 0.3) return "";
            StringBuilder sb = new StringBuilder("\nClosest lines in the file:\n");
            int a = Math.Max(0, bestAt - 1), z = Math.Min(lines.Length, bestAt + want.Length + 1);
            for (int i = a; i < z; i++) sb.Append(i + 1).Append("| ").Append(lines[i]).Append('\n');
            // Name the <find> lines that are nowhere in the file: a model that remembered code a later edit removed
            // retried the same <find> twice, the closest lines alone did not tell it what was missing (2026-10-19).
            return sb.Append(MissingLines(lines, want)).ToString();
        }

        static string MissingLines(string[] lines, string[] want)
        {
            HashSet<string> have = new HashSet<string>();
            foreach (string l in lines) have.Add(l.Trim());
            StringBuilder sb = new StringBuilder();
            int n = 0;
            foreach (string w in want)
            {
                if (w.Trim().Length == 0 || have.Contains(w.Trim()) || n >= 8) continue;
                if (n++ == 0) sb.Append("These <find> lines are not in the file at all (it was changed since; do not quote them):\n");
                sb.Append("  ").Append(w.Trim()).Append('\n');
            }
            return sb.ToString();
        }

        // Share of words in common (0..1), ignoring case and spacing.
        static double Similar(string x, string y)
        {
            string[] wx = Regex.Split(x.Trim().ToLowerInvariant(), "\\W+"), wy = Regex.Split(y.Trim().ToLowerInvariant(), "\\W+");
            if (x.Trim().Length == 0 && y.Trim().Length == 0) return 1;
            HashSet<string> sx = new HashSet<string>(wx); sx.Remove("");
            int common = 0, total = 0;
            foreach (string w in wy) { if (w.Length == 0) continue; total++; if (sx.Contains(w)) common++; }
            return total == 0 ? 0 : (double)common / Math.Max(total, sx.Count);
        }

        // Changes part of a file in place: each (find, replace) pair replaces one spot. The find text must occur exactly
        // once; if it does not match exactly, a match ignoring leading / trailing whitespace on each line is tried (small
        // models often get indentation wrong). Much cheaper than <write> for a small change: only the changed part is
        // generated. The previous version goes to the backup folder, as with Write.
        public string Edit(string rel, IList<KeyValuePair<string, string>> changes) { return Edit(rel, changes, false); }

        // Whether every <edit> in a reply would apply to the files as they are now (nothing is written): null if they all
        // would, else the first problem. Used to vet a code-check rewrite before it replaces a reply that works.
        public string EditProblem(string reply)
        {
            foreach (Match m in EditTag.Matches(reply))
            {
                List<KeyValuePair<string, string>> ch = new List<KeyValuePair<string, string>>();
                foreach (Match fr in FindReplace.Matches(m.Groups[2].Value)) ch.Add(new KeyValuePair<string, string>(fr.Groups[1].Value, fr.Groups[2].Value));
                try { Edit(m.Groups[1].Value, ch, true); }
                catch (Exception ex) { return ex.Message.Split('\n')[0]; }
            }
            return null;
        }

        string Edit(string rel, IList<KeyValuePair<string, string>> changes, bool dryRun)
        {
            string f = Resolve(rel);
            ShellGuard.CheckWrite(f);
            if (!File.Exists(f)) throw new FileNotFoundException("No file " + Relative(f) + " (use <write> to create it).");
            if (Relative(f).StartsWith(BackupDir)) throw new ArgumentException("The backup folder is read-only.");
            if (changes.Count == 0) throw new ArgumentException("No <find>/<replace> pair in the edit.");
            string text = File.ReadAllText(f);
            bool crlf = text.Contains("\r\n");
            string t = text.Replace("\r\n", "\n");
            int added = 0, removed = 0, skipped = 0;
            foreach (KeyValuePair<string, string> c in changes)
            {
                string find = c.Key.Replace("\r\n", "\n").Trim('\n'), repl = c.Value.Replace("\r\n", "\n").Trim('\n');
                if (find.Trim().Length == 0) throw new ArgumentException("An empty <find> matches nothing; quote the lines to change.");
                int at = t.IndexOf(find, StringComparison.Ordinal);
                if (at >= 0)
                {
                    if (t.IndexOf(find, at + 1, StringComparison.Ordinal) >= 0)
                        throw new ArgumentException("The <find> text occurs more than once in " + Relative(f) + "; include more surrounding lines.");
                    // Only indentation before the match on its line: the match covers whole lines, so take the indentation
                    // in too, and give it to a replacement written without any (otherwise it doubles or vanishes).
                    int ls = t.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
                    if (at > 0 && t[at - 1] == '\n') ls = at;
                    string indent = t.Substring(ls, at - ls);
                    if (indent.Trim().Length == 0 && indent.Length > 0)
                    {
                        if (repl.Length > 0 && repl[0] != ' ' && repl[0] != '\t')
                        {
                            string[] rl = repl.Split('\n');
                            for (int k = 0; k < rl.Length; k++) if (rl[k].Length > 0) rl[k] = indent + rl[k];
                            repl = string.Join("\n", rl);
                        }
                        at = ls;
                        find = indent + find;
                    }
                    t = t.Substring(0, at) + repl + t.Substring(at + find.Length);
                }
                else
                {
                    string[] lines = t.Split('\n'), want = find.Split('\n');
                    int hit = -1, hits = 0;
                    for (int i = 0; i + want.Length <= lines.Length; i++)
                    {
                        bool ok = true;
                        for (int j = 0; j < want.Length && ok; j++) ok = lines[i + j].Trim() == want[j].Trim();
                        if (ok) { hits++; if (hit < 0) hit = i; }
                    }
                    // a model that repeats a change it already made (retry rounds resend every edit): the replacement is
                    // there already, so this one is done, not a failure
                    if (hits == 0 && repl.Trim().Length > 0 && LineMatches(lines, repl.Split('\n')) > 0) { skipped++; continue; }
                    if (hits == 0) throw new ArgumentException("The <find> text was not found in " + Relative(f) + ": the file is not what you remember " +
                        "(it may have changed since you last saw it). Copy <find> from its current lines below." + CurrentLines(t, find));
                    if (hits > 1) throw new ArgumentException("The <find> text occurs " + hits + " times in " + Relative(f) + "; include more surrounding lines.");
                    List<string> outL = new List<string>(lines);
                    outL.RemoveRange(hit, want.Length);
                    outL.InsertRange(hit, repl.Length == 0 ? new string[0] : repl.Split('\n'));
                    t = string.Join("\n", outL.ToArray());
                }
                removed += find.Split('\n').Length;
                added += repl.Length == 0 ? 0 : repl.Split('\n').Length;
            }
            byte[] data = new UTF8Encoding(false).GetBytes(crlf ? t.Replace("\n", "\r\n") : t);
            if (data.Length > MaxWriteBytes) throw new ArgumentException("File too large.");
            if (dryRun) return null;
            if (skipped == changes.Count) return "no change to " + Relative(f) + ": the edit was already applied";
            string backup = System.IO.Path.Combine(Root, BackupDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"), Relative(f).Replace('/', '\\'));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));
            File.Copy(f, backup, true);
            File.WriteAllBytes(f, data);
            return "edited " + Relative(f) + " (+" + added + " -" + removed + " lines, previous version in " + Relative(backup) + ")";
        }

        // How many places `want` occurs in `lines`, comparing lines without their indentation.
        static int LineMatches(string[] lines, string[] want)
        {
            int hits = 0;
            for (int i = 0; i + want.Length <= lines.Length; i++)
            {
                bool ok = true;
                for (int j = 0; j < want.Length && ok; j++) ok = lines[i + j].Trim() == want[j].Trim();
                if (ok) hits++;
            }
            return hits;
        }

        static readonly Regex EditTag =new Regex("<edit\\s+path\\s*=\\s*\"([^\"]+)\"\\s*>(.*?)</edit>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        static readonly Regex FindReplace = new Regex("<find>\\r?\\n?(.*?)</find>\\s*<replace>\\r?\\n?(.*?)</replace>", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Deletes a file by moving it into the backup folder (recoverable; never a permanent delete). Files only.
        public string Delete(string rel)
        {
            string f = Resolve(rel);
            if (f == Root || Directory.Exists(f)) throw new ArgumentException("Only files can be deleted, not folders.");
            if (!File.Exists(f)) throw new FileNotFoundException("No file " + Relative(f) + ".");
            if (Relative(f).StartsWith(BackupDir)) throw new ArgumentException("The backup folder is read-only.");
            string backup = System.IO.Path.Combine(Root, BackupDir, DateTime.Now.ToString("yyyyMMdd-HHmmss"), Relative(f).Replace('/', '\\'));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(backup));
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(f, backup);
            return "deleted " + Relative(f) + " (moved to " + Relative(backup) + ")";
        }

        static readonly Regex DeleteTag = new Regex("<delete\\s+path\\s*=\\s*\"([^\"]+)\"\\s*/?>", RegexOptions.IgnoreCase);
        static readonly Regex WriteTag = new Regex("<write\\s+path\\s*=\\s*\"([^\"]+)\"\\s*>\\r?\\n?(.*?)</write>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        // <read path="x"/>, <read path="x" lines="120-180"/> or <read path="x" lines="all"/> (a big file whole)
        static readonly Regex ReadTag = new Regex("<read\\s+path\\s*=\\s*\"([^\"]+)\"(?:\\s+lines\\s*=\\s*\"\\s*(?:(\\d+)\\s*-\\s*(\\d+)|(all))\\s*\")?\\s*/?>", RegexOptions.IgnoreCase);
        static readonly Regex CommandTag = new Regex("<(" + string.Join("|", ShellGuard.CommandTags) + ")\\b[^>]*>", RegexOptions.IgnoreCase);
        static readonly Regex ListTag = new Regex("<list(?:\\s+path\\s*=\\s*\"([^\"]*)\")?\\s*/?>", RegexOptions.IgnoreCase);
        // <search query="text"/> or <search query="text" path="src"/>
        static readonly Regex SearchTag = new Regex("<search\\s+query\\s*=\\s*\"([^\"]+)\"(?:\\s+path\\s*=\\s*\"([^\"]*)\")?\\s*/?>", RegexOptions.IgnoreCase);

        // End index of the first complete <read .../>, <list .../> or <search .../> tag in a reply, or -1.
        public static int QueryTagEnd(string reply)
        {
            Match m = null;
            foreach (Match x in new Match[] { ReadTag.Match(reply), ListTag.Match(reply), SearchTag.Match(reply) })
                if (x.Success && (m == null || x.Index < m.Index)) m = x;
            if (m == null) return -1;
            // A <write> or <edit> that opens before it holds file content (which may mention tags): leave it alone.
            foreach (string open in new string[] { "write", "edit" })
            {
                int w = reply.IndexOf("<" + open, StringComparison.OrdinalIgnoreCase);
                if (w >= 0 && w < m.Index && reply.IndexOf("</" + open + ">", w, StringComparison.OrdinalIgnoreCase) < 0) return -1;
            }
            return m.Index + m.Length;
        }

        // Carries out every tag in a model reply, in order of appearance.
        public List<Action> Execute(string reply) { return Execute(reply, null); }

        // approve: asked before each list / read / write (Accept mode); false = denied, the model is told so. null = all allowed.
        public List<Action> Execute(string reply, Func<Action, bool> approve)
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
                if (m.Groups[2].Success) { a.From = int.Parse(m.Groups[2].Value); a.To = int.Parse(m.Groups[3].Value); }
                a.All = m.Groups[4].Success;
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            foreach (Match m in SearchTag.Matches(reply))
            {
                Action a = new Action(); a.Op = "search"; a.Query = m.Groups[1].Value;
                a.Path = m.Groups[2].Success && m.Groups[2].Value.Length > 0 ? m.Groups[2].Value : ".";
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            foreach (Match m in EditTag.Matches(reply))
            {
                Action a = new Action(); a.Op = "edit"; a.Path = m.Groups[1].Value;
                a.Changes = new List<KeyValuePair<string, string>>();
                foreach (Match fr in FindReplace.Matches(m.Groups[2].Value))
                    a.Changes.Add(new KeyValuePair<string, string>(fr.Groups[1].Value, fr.Groups[2].Value));
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            foreach (Match m in DeleteTag.Matches(reply))
            {
                Action a = new Action(); a.Op = "delete"; a.Path = m.Groups[1].Value;
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            foreach (Match m in ListTag.Matches(reply))
            {
                Action a = new Action(); a.Op = "list"; a.Path = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : ".";
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            // Command-style tags (<run>, <exec>, ...) are never carried out; the model is told they were refused. File
            // contents and code blocks are not looked at: an HTML file's <script> is content, not a request.
            string outside = Regex.Replace(reply, "<write\\s[^>]*>[\\s\\S]*?(</write>|$)|<edit\\s[^>]*>[\\s\\S]*?(</edit>|$)|```[\\s\\S]*?(```|$)", delegate(Match c) { return new string(' ', c.Length); }, RegexOptions.IgnoreCase);
            foreach (Match m in CommandTag.Matches(outside))
            {
                Action a = new Action(); a.Op = "refused"; a.Path = m.Groups[1].Value.ToLowerInvariant();
                found.Add(new KeyValuePair<int, Action>(m.Index, a));
            }
            found.Sort(delegate(KeyValuePair<int, Action> x, KeyValuePair<int, Action> y) { return x.Key.CompareTo(y.Key); });

            List<Action> done = new List<Action>();
            foreach (KeyValuePair<int, Action> kv in found)
            {
                Action a = kv.Value;
                try
                {
                    if (a.Op == "refused") { a.Ok = false; a.Message = "<" + a.Path + "> " + ShellGuard.Refusal; a.Content = null; done.Add(a); continue; }
                    if (approve != null && !approve(a))
                    {
                        a.Ok = false; a.Message = a.Op + " " + a.Path + " was denied by the user"; a.Content = null; done.Add(a); continue;
                    }
                    if (a.Op == "write") { a.Message = Write(a.Path, a.Content); a.Content = null; }
                    else if (a.Op == "delete") { a.Message = Delete(a.Path); a.Content = null; }
                    else if (a.Op == "edit") { a.Message = Edit(a.Path, a.Changes); a.Content = null; }
                    else if (a.Op == "read" && a.To > 0) { a.Content = ReadLines(a.Path, a.From, a.To); a.Message = "read " + a.Path + " lines " + a.From + "-" + a.To; }
                    else if (a.Op == "read")
                    {
                        a.Content = Read(a.Path);
                        int lines = CountLines(a.Content);
                        if (!a.All && lines > WholeReadLines)
                        {
                            // a big file: its outline instead of all of it, so the model reads just the lines it needs
                            a.Content = OutlineFor(a.Path, lines);
                            a.OutlineOnly = true;
                            a.Message = "read " + a.Path + ": outline only (" + lines + " lines; read the lines you need)";
                        }
                        else a.Message = "read " + a.Path + " (" + a.Content.Length + " chars)";
                    }
                    else if (a.Op == "search") { a.Content = Search(a.Query, a.Path); a.Message = "searched \"" + a.Query + "\"" + (a.Path != "." ? " in " + a.Path : ""); }
                    else { a.Content = List(a.Path); a.Message = "listed " + a.Path; }
                    a.Ok = true;
                }
                catch (Exception ex) { a.Ok = false; a.Message = a.Op + " " + a.Path + " failed: " + ex.Message; a.Content = null; }
                done.Add(a);
            }
            return done;
        }

        // The project's top level for the system prompt: names only, no sizes and no dot-folders (.phoenix, .llm-backup,
        // .git). The system prompt starts every request, so anything in it that changes (a size after each edit) made the
        // model re-read the whole conversation: 5.7k tokens, 25 s on Gemma 12B (2026-10-16). Use <list> for details.
        string StableList()
        {
            List<string> names = new List<string>();
            foreach (string d in Directory.GetDirectories(Root))
            {
                string n = System.IO.Path.GetFileName(d);
                if (!n.StartsWith(".")) names.Add(n + "/");
            }
            foreach (string f in Directory.GetFiles(Root)) names.Add(System.IO.Path.GetFileName(f));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            if (names.Count > MaxListEntries) { names.RemoveRange(MaxListEntries, names.Count - MaxListEntries); names.Add("..."); }
            return names.Count == 0 ? "(empty)\n" : string.Join("\n", names.ToArray()) + "\n";
        }

        public string ToolInstructions()
        {
            return "You can work with files in the user's project folder (" + Display + "); these tags only reach that folder. " +
                   "To act, put these tags in your reply:\n" +
                   "<list path=\".\"/>   list a folder\n" +
                   "<read path=\"relative/path\"/>   read a file\n" +
                   "<read path=\"relative/path\" lines=\"120-180\"/>   read only those lines (numbered). A file over " + WholeReadLines + " lines read without lines= returns its outline (functions with line numbers): then read just the lines you need\n" +
                   "<search query=\"text\"/>   find text in every project file (path:line: text); add path=\"folder or file\" to search only there\n" +
                   "<edit path=\"relative/path\">\n<find>\nexact lines now in the file\n</find>\n<replace>\nthe new lines\n</replace>\n</edit>   change part of an existing file " +
                   "(several <find>/<replace> pairs allowed; quote enough lines that the <find> text is unique)\n" +
                   "<write path=\"relative/path\">\nfull file content\n</write>   create a new file, or replace a whole file\n" +
                   "<delete path=\"relative/path\"/>   delete a file (a copy is kept in the backup folder)\n" +
                   "Use paths relative to the project folder. After <read>, <search> or <list> you will receive the results and can continue. To find where something is, <search> first instead of reading every file." +
                   "When the user asks you to create, change or delete a file, do it with the tags instead of only showing the code. " +
                   "To change an existing file use <edit> with only the changed part, not <write>: rewriting a whole file is slow. Read the file first if you have not seen it. " +
                   "Never say you will check, read or change a file without putting the tag in that same reply: act, don't announce. " +
                   "You cannot run commands, scripts or programs, and cannot create executable files; this is permanent. " +
                   ShellGuard.RunAdvice +
                   "A project is active, so files are the result: when you create or change a file, do it with the tags only and do NOT " +
                   "also show the code in a code block; after the tags say in one short sentence what you changed. Show code in a code " +
                   "block only when the user asks to see it, or for code that is not meant for a file. " +
                   "Keep explanations short.\n\nExamples:\nUser: make hello.txt that says hi\nAssistant: <write path=\"hello.txt\">\nhi\n</write>\nCreated hello.txt.\n\n" +
                   "User: in hello.txt change hi to hello\nAssistant: <edit path=\"hello.txt\">\n<find>\nhi\n</find>\n<replace>\nhello\n</replace>\n</edit>\nChanged it.\n\n" +
                   "Project folder contents:\n" + StableList();
        }
    }
}
