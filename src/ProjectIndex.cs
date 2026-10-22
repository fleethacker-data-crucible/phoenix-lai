// Project index: a live search index of the active project's files, so the model finds code without listing and
// reading its way there. C# 5 only.
//   In memory, updated one file at a time: a save re-chunks just that file, swaps its keyword postings (BM25) and keeps
//   the vectors of every section whose text did not change. That part is plain CPU work and takes milliseconds.
//   Vectors (EmbeddingGemma, shared with the docs library) trail behind on a background thread, one section per GPU job
//   and only while the chat is idle, so meaning search catches up without ever slowing a reply.
//   Saves are noticed two ways: the model's own write/edit/delete (Touch, synchronous) and a FileSystemWatcher for
//   everything else (debounced). Activation also sweeps the folder for changes made while Phoenix was closed.
//   Kept in <project>\.phoenix\index\project.idx (written a few seconds after the last change).
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Phoenix
{
    public sealed class ProjectIndex : IDisposable
    {
        sealed class Chunk
        {
            public FileRec File;
            public string Title, Symbol, Text;
            public int Start, End;                 // 1-based line range
            public Dictionary<ulong, int> Tf;      // term -> count (title and text)
            public int Len;
            public ulong TextHash;
            public float[] Vec;                    // null: not embedded yet
        }

        sealed class FileRec
        {
            public string Rel;
            public long Size, Mtime;
            public byte[] Hash;
            public List<Chunk> Chunks = new List<Chunk>();
            public List<string> Symbols = new List<string>();   // "Name (L12)"
        }

        public sealed class Hit
        {
            public string File, Symbol, Text;
            public int Start, End;
            public float Score;
        }

        public readonly string Root, IndexFile;
        readonly Func<IEmbedder> _embedder;
        readonly Func<bool> _busy;
        readonly object _lock = new object();
        readonly Dictionary<string, FileRec> _files = new Dictionary<string, FileRec>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<ulong, Dictionary<Chunk, int>> _post = new Dictionary<ulong, Dictionary<Chunk, int>>();
        long _totalLen;
        int _chunkCount, _vecDim;
        FileSystemWatcher _watcher;
        readonly HashSet<string> _queue = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly AutoResetEvent _wake = new AutoResetEvent(false), _embedWake = new AutoResetEvent(false);
        Thread _updater, _embedThread;
        volatile bool _stop;
        long _dirtyAt;   // ticks of the last change not yet saved; 0 = saved
        public string LastSweep;

        public const int TopK = 5, BudgetChars = 5000;
        const int TargetChars = 1400, MaxChars = 2400;
        const long MaxFileBytes = 1L << 20;

        static readonly string[] SkipDirs = { ".git", ".phoenix", ".llm-backup", ".vs", ".vscode", ".idea", "node_modules", "bin", "obj",
            "dist", "build", "out", "target", "packages", "__pycache__", ".venv", "venv", ".next", "coverage" };
        static readonly string[] TextExt = { ".cs", ".csx", ".vb", ".fs", ".java", ".kt", ".js", ".mjs", ".cjs", ".jsx", ".ts", ".tsx", ".vue", ".svelte",
            ".py", ".rb", ".php", ".go", ".rs", ".c", ".h", ".cpp", ".hpp", ".cc", ".swift", ".m", ".sql", ".ps1", ".psm1", ".psd1", ".bat", ".cmd",
            ".sh", ".vbs", ".bas", ".cls", ".dax", ".pq", ".qvs", ".html", ".htm", ".css", ".scss", ".less", ".xml", ".xaml", ".json", ".yaml",
            ".yml", ".toml", ".ini", ".config", ".md", ".markdown", ".txt", ".rst", ".csproj", ".sln", ".gradle", ".r" };

        // The docs library's embedding model, as the embedder source (one copy in memory).
        public static Func<IEmbedder> EmbedderOf(Rag rag) { return rag.SharedEmbedder; }

        public ProjectIndex(string root, Func<IEmbedder> embedder, Func<bool> gpuBusy)
        {
            Root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            IndexFile = Path.Combine(Root, ".phoenix", "index", "project.idx");
            _embedder = embedder; _busy = gpuBusy ?? delegate { return false; };
        }

        // Loads the saved index, sweeps the folder for changes (synchronously: CPU only), then starts the watcher and the
        // background threads. Returns how long the sweep took, for the log.
        public string Start()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            try { Load(); } catch (Exception ex) { Dbg.Note("project index", "saved index unreadable (" + ex.Message + "); rebuilding"); lock (_lock) ClearAll(); }
            int loaded = _files.Count;
            int changed = Sweep();
            LastSweep = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} files, {1} sections ({2} loaded, {3} changed) in {4} ms",
                _files.Count, _chunkCount, loaded, changed, sw.ElapsedMilliseconds);
            try
            {
                _watcher = new FileSystemWatcher(Root);
                _watcher.IncludeSubdirectories = true;
                _watcher.InternalBufferSize = 64 * 1024;
                _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                _watcher.Changed += OnFs; _watcher.Created += OnFs; _watcher.Deleted += OnFs;
                _watcher.Renamed += delegate(object s, RenamedEventArgs e) { Enqueue(e.OldFullPath); Enqueue(e.FullPath); };
                _watcher.Error += delegate { ThreadPool.QueueUserWorkItem(delegate { if (!_stop) Sweep(); }); };   // buffer overflow: re-check all
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex) { Dbg.Note("project index", "file watcher unavailable: " + ex.Message); }
            _updater = new Thread(UpdateLoop); _updater.IsBackground = true; _updater.Name = "project index"; _updater.Start();
            _embedThread = new Thread(EmbedLoop); _embedThread.IsBackground = true; _embedThread.Name = "project embed";
            _embedThread.Priority = ThreadPriority.BelowNormal; _embedThread.Start();
            return LastSweep;
        }

        void OnFs(object sender, FileSystemEventArgs e) { Enqueue(e.FullPath); }

        void Enqueue(string full)
        {
            if (_stop || full == null) return;
            lock (_queue) _queue.Add(full);
            _wake.Set();
        }

        // Waits for 250 ms without new events, then indexes what changed.
        void UpdateLoop()
        {
            while (!_stop)
            {
                _wake.WaitOne(2000);
                if (_stop) break;
                while (_wake.WaitOne(250)) { if (_stop) return; }
                List<string> batch;
                lock (_queue) { batch = new List<string>(_queue); _queue.Clear(); }
                if (batch.Count > 0) { try { Update(batch); } catch (Exception ex) { Dbg.Note("project index", "update failed: " + ex.Message); } }
                if (_dirtyAt != 0 && DateTime.UtcNow.Ticks - _dirtyAt > TimeSpan.TicksPerSecond * 3) { try { Save(); } catch (Exception ex) { Dbg.Note("project index", "save failed: " + ex.Message); } }
            }
        }

        // Re-indexes these files (full paths; files or folders, existing or deleted) right now. Returns how many files
        // changed in the index; ms is the time taken.
        public int Update(IEnumerable<string> fullPaths, out double ms)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            int n = Update(fullPaths);
            ms = sw.Elapsed.TotalMilliseconds;
            return n;
        }

        public int Update(IEnumerable<string> fullPaths)
        {
            int changed = 0;
            foreach (string full0 in fullPaths)
            {
                string full = Path.GetFullPath(full0).TrimEnd(Path.DirectorySeparatorChar);
                string rel = Rel(full);
                if (rel == null || Skipped(rel)) continue;
                if (Directory.Exists(full))
                {
                    // Walk, not EnumerateFiles(AllDirectories): it skips junctions (which could reach outside the project),
                    // hidden and skipped folders, and an unreadable subfolder instead of aborting the whole update.
                    try { if ((new DirectoryInfo(full).Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue; } catch { continue; }
                    foreach (string f in Walk(full)) if (IndexOne(f, Rel(f))) changed++;
                    continue;
                }
                if (File.Exists(full)) { if (IndexOne(full, rel)) changed++; continue; }
                // gone: the file, or a whole folder
                lock (_lock)
                {
                    List<string> drop = new List<string>();
                    foreach (string k in _files.Keys) if (k.Equals(rel, StringComparison.OrdinalIgnoreCase) || k.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase)) drop.Add(k);
                    foreach (string k in drop) { RemoveFile(_files[k]); _files.Remove(k); changed++; }
                }
            }
            if (changed > 0) { _dirtyAt = DateTime.UtcNow.Ticks; _embedWake.Set(); }
            return changed;
        }

        // Every file in the project against the index: new, changed (size/time, then content) and removed files.
        int Sweep()
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string[]> todo = new List<string[]>();
            foreach (string f in Walk(Root))
            {
                string rel = Rel(f);
                if (rel == null) continue;
                seen.Add(rel);
                FileRec old;
                lock (_lock) _files.TryGetValue(rel, out old);
                if (old != null)
                {
                    FileInfo fi = new FileInfo(f);
                    if (fi.Length == old.Size && fi.LastWriteTimeUtc.Ticks == old.Mtime) continue;   // quick check; content decides below
                }
                todo.Add(new[] { f, rel });
            }
            int changed = 0;
            // hashing and chunking in parallel (the postings update inside IndexOne is under the lock)
            System.Threading.Tasks.Parallel.ForEach(todo, delegate(string[] fr) { if (IndexOne(fr[0], fr[1])) Interlocked.Increment(ref changed); });
            lock (_lock)
            {
                List<string> gone = new List<string>();
                foreach (string k in _files.Keys) if (!seen.Contains(k)) gone.Add(k);
                foreach (string k in gone) { RemoveFile(_files[k]); _files.Remove(k); changed++; }
            }
            if (changed > 0) { _dirtyAt = DateTime.UtcNow.Ticks; _embedWake.Set(); }
            return changed;
        }

        IEnumerable<string> Walk(string dir)
        {
            Stack<string> st = new Stack<string>();
            st.Push(dir);
            while (st.Count > 0)
            {
                string d = st.Pop();
                string[] files = null, dirs = null;
                try { files = Directory.GetFiles(d); dirs = Directory.GetDirectories(d); } catch { continue; }
                foreach (string f in files) { if (!IsText(f)) continue; string r = Rel(f); if (r != null && !Skipped(r)) yield return f; }
                foreach (string s in dirs)
                {
                    string name = Path.GetFileName(s);
                    if (Array.IndexOf(SkipDirs, name.ToLowerInvariant()) >= 0 || name.StartsWith(".")) continue;
                    try { if ((new DirectoryInfo(s).Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue; } catch { continue; }
                    st.Push(s);
                }
            }
        }

        static bool IsText(string f) { return Array.IndexOf(TextExt, Path.GetExtension(f).ToLowerInvariant()) >= 0; }

        string Rel(string full)
        {
            if (!full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return full.Substring(Root.Length + 1).Replace('\\', '/');
        }

        static bool Skipped(string rel)
        {
            if (rel.Equals("RULES.md", StringComparison.OrdinalIgnoreCase)) return true;   // already in every prompt
            string[] parts = rel.Split('/');
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i].StartsWith(".") || Array.IndexOf(SkipDirs, parts[i].ToLowerInvariant()) >= 0) return true;
            return parts.Length > 0 && parts[parts.Length - 1].StartsWith(".") && !IsText(parts[parts.Length - 1]);
        }

        // (Re)indexes one file if its content changed; true if the index changed.
        bool IndexOne(string full, string rel)
        {
            if (!IsText(full)) return false;
            byte[] bytes;
            FileInfo fi;
            try
            {
                fi = new FileInfo(full);
                if (fi.Length > MaxFileBytes) return RemoveIfPresent(rel);
                bytes = ReadShared(full);
            }
            catch (IOException) { Enqueue(full); return false; }   // still being written: try again shortly
            catch (UnauthorizedAccessException) { return false; }
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
            lock (_lock)
            {
                FileRec old;
                if (_files.TryGetValue(rel, out old) && TextFiles.SameHash(old.Hash, hash))
                {
                    old.Size = fi.Length; old.Mtime = fi.LastWriteTimeUtc.Ticks;   // touched, not changed
                    return false;
                }
            }
            if (LooksBinary(bytes)) return RemoveIfPresent(rel);
            string text = TextFiles.Decode(bytes);
            FileRec rec = new FileRec();
            rec.Rel = rel; rec.Size = fi.Length; rec.Mtime = fi.LastWriteTimeUtc.Ticks; rec.Hash = hash;
            Chunker.Split(rec, text);
            lock (_lock)
            {
                // sections whose text is unchanged keep their vectors
                FileRec old;
                if (_files.TryGetValue(rel, out old))
                {
                    Dictionary<ulong, float[]> vecs = new Dictionary<ulong, float[]>();
                    foreach (Chunk c in old.Chunks) if (c.Vec != null) vecs[c.TextHash] = c.Vec;
                    foreach (Chunk c in rec.Chunks) { float[] v; if (vecs.TryGetValue(c.TextHash, out v)) c.Vec = v; }
                    RemoveFile(old);
                }
                _files[rel] = rec;
                AddFile(rec);
            }
            return true;
        }

        bool RemoveIfPresent(string rel)
        {
            lock (_lock)
            {
                FileRec old;
                if (!_files.TryGetValue(rel, out old)) return false;
                RemoveFile(old); _files.Remove(rel); return true;
            }
        }

        static byte[] ReadShared(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] b = new byte[fs.Length];
                int off = 0;
                while (off < b.Length) { int r = fs.Read(b, off, b.Length - off); if (r <= 0) break; off += r; }
                return b;
            }
        }

        static bool LooksBinary(byte[] b)
        {
            if (b.Length >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF))) return false;   // UTF-16 text
            int n = Math.Min(b.Length, 8000);
            for (int i = 0; i < n; i++) if (b[i] == 0) return true;
            return false;
        }

        // Postings: caller holds _lock.
        void AddFile(FileRec f)
        {
            foreach (Chunk c in f.Chunks)
            {
                foreach (KeyValuePair<ulong, int> t in c.Tf)
                {
                    Dictionary<Chunk, int> p;
                    if (!_post.TryGetValue(t.Key, out p)) { p = new Dictionary<Chunk, int>(); _post[t.Key] = p; }
                    p[c] = t.Value;
                }
                _totalLen += c.Len; _chunkCount++;
                if (c.Vec != null && _vecDim == 0) _vecDim = c.Vec.Length;
            }
        }

        void RemoveFile(FileRec f)
        {
            foreach (Chunk c in f.Chunks)
            {
                foreach (ulong t in c.Tf.Keys)
                {
                    Dictionary<Chunk, int> p;
                    if (_post.TryGetValue(t, out p)) { p.Remove(c); if (p.Count == 0) _post.Remove(t); }
                }
                _totalLen -= c.Len; _chunkCount--;
            }
        }

        void ClearAll() { _files.Clear(); _post.Clear(); _totalLen = 0; _chunkCount = 0; }

        public int FileCount { get { lock (_lock) return _files.Count; } }
        public int ChunkCount { get { lock (_lock) return _chunkCount; } }
        public int PendingVectors { get { lock (_lock) { int n = 0; foreach (FileRec f in _files.Values) foreach (Chunk c in f.Chunks) if (c.Vec == null) n++; return n; } } }

        // ---- embedding in the background ----

        void EmbedLoop()
        {
            while (!_stop)
            {
                _embedWake.WaitOne(3000);
                while (!_stop)
                {
                    if (_busy()) { Thread.Sleep(250); continue; }   // the chat has the GPU: wait
                    Chunk next = null;
                    lock (_lock)
                        foreach (FileRec f in _files.Values)
                        {
                            foreach (Chunk c in f.Chunks) if (c.Vec == null) { next = c; break; }
                            if (next != null) break;
                        }
                    if (next == null) break;
                    IEmbedder e = null;
                    try { e = _embedder != null ? _embedder() : null; } catch { }
                    if (e == null) { Thread.Sleep(5000); break; }   // no embedding model: keyword search only
                    float[] v;
                    try { v = e.Document(next.File.Rel + " > " + next.Title, next.Text); }
                    catch (Exception ex) { Dbg.Note("project index", "embedding failed: " + ex.Message); Thread.Sleep(2000); continue; }
                    lock (_lock) { next.Vec = v; if (_vecDim == 0) _vecDim = v.Length; }
                    _dirtyAt = DateTime.UtcNow.Ticks;
                    Thread.Sleep(15);   // short gaps: the GPU is never held for long stretches
                }
            }
        }

        // ---- search ----

        // Best sections for a message: keyword (BM25) hits, plus meaning hits when the vectors are there, fused.
        public List<Hit> Search(string message, bool useVectors)
        {
            List<Hit> hits = new List<Hit>();
            if (string.IsNullOrEmpty(message)) return hits;
            List<ulong> terms = new List<ulong>();
            Terms.Extract(message, terms);
            Dictionary<ulong, float> q = new Dictionary<ulong, float>();
            foreach (ulong t in terms) { float w = Terms.IsCommon(t) ? 0.75f : 1f, cur; if (!q.TryGetValue(t, out cur) || cur < w) q[t] = w; }
            float[] qv = null;
            if (useVectors && _vecDim > 0)
            {
                IEmbedder e = null;
                try { e = _embedder != null ? _embedder() : null; } catch { }
                if (e != null && e.Dim == _vecDim) { try { qv = e.Query(message); } catch { } }
            }
            lock (_lock)
            {
                if (_chunkCount == 0) return hits;
                Dictionary<Chunk, float> score = new Dictionary<Chunk, float>();
                Dictionary<Chunk, int> matched = new Dictionary<Chunk, int>();
                int primary = 0;
                float avg = Math.Max(1f, (float)_totalLen / Math.Max(1, _chunkCount));
                foreach (KeyValuePair<ulong, float> t in q)
                {
                    if (t.Value >= 1f) primary++;
                    Dictionary<Chunk, int> p;
                    if (!_post.TryGetValue(t.Key, out p)) continue;
                    float idf = (float)Math.Log(1 + (_chunkCount - p.Count + 0.5) / (p.Count + 0.5)) * t.Value;
                    foreach (KeyValuePair<Chunk, int> e in p)
                    {
                        float tf = e.Value, s;
                        score.TryGetValue(e.Key, out s);
                        score[e.Key] = s + idf * tf * (Rag.K1 + 1) / (tf + Rag.K1 * (1 - Rag.B + Rag.B * e.Key.Len / avg));
                        if (t.Value >= 1f) { int m; matched.TryGetValue(e.Key, out m); matched[e.Key] = m + 1; }
                    }
                }
                List<Chunk> kw = new List<Chunk>();
                foreach (KeyValuePair<Chunk, float> s in score)
                {
                    int m; matched.TryGetValue(s.Key, out m);
                    if (m >= 2 || (m >= 1 && m >= 0.3f * primary)) kw.Add(s.Key);
                }
                kw.Sort(delegate(Chunk a, Chunk b) { return score[b].CompareTo(score[a]); });
                if (kw.Count > 0) { float floor = score[kw[0]] * 0.35f; kw.RemoveAll(delegate(Chunk c) { return score[c] < floor; }); }

                List<Chunk> vec = new List<Chunk>();
                Dictionary<Chunk, float> cos = new Dictionary<Chunk, float>();
                if (qv != null)
                {
                    double mean = 0; int n = 0; float best = float.NegativeInfinity;
                    foreach (FileRec f in _files.Values)
                        foreach (Chunk c in f.Chunks)
                        {
                            if (c.Vec == null || c.Vec.Length != qv.Length) continue;
                            float d = 0;
                            for (int k = 0; k < qv.Length; k++) d += c.Vec[k] * qv[k];
                            cos[c] = d; mean += d; n++; if (d > best) best = d;
                        }
                    if (n > 0)
                    {
                        float fl = (float)Math.Max(mean / n + 0.1, best - 0.1);
                        foreach (KeyValuePair<Chunk, float> c in cos) if (c.Value >= fl) vec.Add(c.Key);
                        vec.Sort(delegate(Chunk a, Chunk b) { return cos[b].CompareTo(cos[a]); });
                    }
                }
                Dictionary<Chunk, float> fused = new Dictionary<Chunk, float>();
                for (int i = 0; i < kw.Count; i++) { float f; fused.TryGetValue(kw[i], out f); fused[kw[i]] = f + 1f / (60 + i + 1); }
                for (int i = 0; i < vec.Count; i++) { float f; fused.TryGetValue(vec[i], out f); fused[vec[i]] = f + 1f / (60 + i + 1); }
                List<Chunk> order = new List<Chunk>(fused.Keys);
                order.Sort(delegate(Chunk a, Chunk b) { return fused[b].CompareTo(fused[a]); });
                // Sections that DECLARE an identifier the message names go first: "change the default PrefillChunk" must
                // show `int PrefillChunk = 512;`, not only the methods that use it (2026-10-17: the model edited a use).
                HashSet<FileRec> named = new HashSet<FileRec>();
                foreach (FileRec f in _files.Values)
                {
                    string name = Path.GetFileName(f.Rel);
                    if (message.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) named.Add(f);
                }
                if (named.Count == _files.Count) named.Clear();
                List<Chunk> defs = Definitions(message, named);
                foreach (Chunk c in defs) if (!fused.ContainsKey(c)) fused[c] = 1f;
                order.RemoveAll(delegate(Chunk c) { return defs.Contains(c); });
                order.InsertRange(0, defs);
                // A file named in the message ("in Rag.cs ...") goes before every other file, keeping the order within each.
                if (named.Count > 0)
                {
                    List<Chunk> first = order.FindAll(delegate(Chunk c) { return named.Contains(c.File); });
                    order.RemoveAll(delegate(Chunk c) { return named.Contains(c.File); });
                    order.InsertRange(0, first);
                }
                int used = 0;
                foreach (Chunk c in order)
                {
                    if (hits.Count >= TopK) break;
                    if (used + c.Text.Length > BudgetChars && hits.Count > 0) continue;
                    used += c.Text.Length;
                    Hit h = new Hit();
                    h.File = c.File.Rel; h.Symbol = c.Symbol; h.Text = c.Text; h.Start = c.Start; h.End = c.End; h.Score = fused[c];
                    hits.Add(h);
                }
            }
            return hits;
        }

        // Identifiers in a message: words that look like code names (camelCase/PascalCase with an inner capital,
        // snake_case, or with digits after letters), at most 6.
        static readonly Regex IdentRe = new Regex(@"\b[A-Za-z_][A-Za-z0-9_]{2,}\b");

        // Sections holding a declaration of an identifier the message names (caller holds _lock). A declaration: the name
        // after a type or keyword and before = ; ( { :  (int X = 5;  void X(  class X  const X =  function X(  def X(  $X =).
        List<Chunk> Definitions(string message, HashSet<FileRec> preferred)
        {
            List<Chunk> found = new List<Chunk>();
            List<string> ids = new List<string>();
            foreach (Match m in IdentRe.Matches(message))
            {
                string w = m.Value;
                bool inner = false;
                for (int i = 1; i < w.Length && !inner; i++) inner = char.IsUpper(w[i]) && char.IsLower(w[i - 1]);
                if (!(inner || w.IndexOf('_') > 0 || (char.IsUpper(w[0]) && w.Length > 3 && w.ToUpperInvariant() != w))) continue;
                if (Regex.IsMatch(message, @"\b" + Regex.Escape(w) + @"\.[A-Za-z]{1,5}\b")) continue;   // a file name (GemmaGpu.cs), not a code name
                if (!ids.Contains(w)) ids.Add(w);
                if (ids.Count >= 6) break;
            }
            foreach (string id in ids)
            {
                Regex decl = new Regex(@"(?:\b(?:const|let|var|function|def|fn|func|class|struct|interface|enum|record|type|sub|Sub|Function|Dim|Const|SET|LET|Public|Private)\s+|" +
                    @"\b[\w<>\[\],.?*&]+\s+|\$)" + Regex.Escape(id) + @"\s*(?:=(?!=)|;|\(|\{|:(?!:)|$)", RegexOptions.Multiline);
                // only sections that contain the name at all (postings), then the declaration pattern on their lines
                List<ulong> t = new List<ulong>();
                Terms.Extract(id, t);
                if (t.Count == 0) continue;
                Dictionary<Chunk, int> p;
                if (!_post.TryGetValue(t[0], out p)) continue;
                int mine = 0;
                List<Chunk> cands = new List<Chunk>(p.Keys);
                if (preferred.Count > 0) cands.Sort(delegate(Chunk x, Chunk y) { return (preferred.Contains(y.File) ? 1 : 0).CompareTo(preferred.Contains(x.File) ? 1 : 0); });
                foreach (Chunk c in cands)
                {
                    if (found.Contains(c)) continue;
                    foreach (Match m in decl.Matches(c.Text))
                    {
                        // the word before must not be return / new / a call: "return X(" is a use
                        string before = c.Text.Substring(Math.Max(0, m.Index - 12), Math.Min(12, m.Index)) + m.Value;
                        if (Regex.IsMatch(before, @"\b(return|new|await|throw|yield|else|in|of|is|as|typeof|nameof)\s+[\w$]*" + Regex.Escape(id) + @"\b")) continue;
                        if (Regex.IsMatch(m.Value, @"^\s*[\w.]+\s*\(.*" + Regex.Escape(id))) continue;
                        found.Add(c); mine++; break;
                    }
                    if (mine >= 2 || found.Count >= 4) break;
                }
            }
            return found;
        }

        // The message with the matching project code in front (file and line numbers, so the model can read or edit the
        // exact place), plus what else each of those files contains.
        public string Wrap(List<Hit> hits, string message)
        {
            if (hits.Count == 0) return message;
            StringBuilder sb = new StringBuilder("Matching code from my project (found by the project index; paths are relative to the project folder):\n\n");
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Hit h in hits)
            {
                files.Add(h.File);
                sb.Append(h.File).Append(" lines ").Append(h.Start).Append('-').Append(h.End)
                  .Append(string.IsNullOrEmpty(h.Symbol) ? "" : " (" + h.Symbol + ")").Append(":\n```\n").Append(h.Text.TrimEnd()).Append("\n```\n\n");
            }
            lock (_lock)
            {
                StringBuilder map = new StringBuilder();
                foreach (string f in files)
                {
                    FileRec r;
                    if (!_files.TryGetValue(f, out r) || r.Symbols.Count == 0) continue;
                    map.Append("- ").Append(f).Append(": ").Append(string.Join(", ", r.Symbols.GetRange(0, Math.Min(r.Symbols.Count, 40)).ToArray()))
                       .Append(r.Symbols.Count > 40 ? ", ..." : "").Append('\n');
                }
                if (map.Length > 0) sb.Append("What these files contain (with line numbers):\n").Append(map).Append('\n');
            }
            sb.Append("---\nThese sections are the exact, current contents of those lines (the index is updated on every save); " +
                      "code shown earlier in this chat may be out of date, so go by these. " +
                      "Use them directly: when the exact place to change is shown here, write the <edit> now, copying the <find> lines from above, " +
                      "without reading or searching for it again. If the place is not shown (for example the line where a value is declared or set), " +
                      "<search> for it first; never change a different line to get the same effect.\n\n").Append(message);
            return sb.ToString();
        }

        // A file's outline for the model ("- Name (L12)" per function or type), or null when the index does not have it.
        public string Outline(string fullPath)
        {
            string rel = Rel(Path.GetFullPath(fullPath));
            if (rel == null) return null;
            lock (_lock)
            {
                FileRec r;
                if (!_files.TryGetValue(rel, out r) || r.Symbols.Count == 0) return null;
                StringBuilder sb = new StringBuilder();
                int n = Math.Min(r.Symbols.Count, 300);
                for (int i = 0; i < n; i++) sb.Append("- ").Append(r.Symbols[i]).Append('\n');
                if (r.Symbols.Count > n) sb.Append("- ... (").Append(r.Symbols.Count - n).Append(" more)\n");
                return sb.ToString();
            }
        }

        // ---- chunking and symbols ----

        static class Chunker
        {
            // A declaration that starts a section: types, functions, methods, SQL objects, markdown headings.
            static readonly Regex TypeRe = new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|partial|unsafe|readonly|ref|new|export|default|final|data|pub|declare)\s+)*" +
                @"(class|struct|interface|enum|record|namespace|module|trait|impl|object)\s+([A-Za-z_][\w]*)");
            static readonly Regex FuncRe = new Regex(@"^\s*(?:(?:public|private|protected|internal|static|abstract|sealed|virtual|override|async|extern|unsafe|new|export|default|pub|final|inline|const)\s+)*" +
                @"(?:(?:function|def|fn|func|sub|Sub|Function|Private Sub|Public Sub|Private Function|Public Function)\s+([A-Za-z_][\w]*)" +
                @"|[A-Za-z_][\w<>\[\],.?*&: ]*?\s+([A-Za-z_]\w*)\s*\([^;=]*$" +
                @"|([A-Za-z_]\w*)\s*\([^;=)]*\)\s*\{\s*$)");
            static readonly Regex ArrowRe = new Regex(@"^\s*(?:export\s+)?(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=\s*(?:async\s*)?(?:function\b|\([^)]*\)\s*=>|[A-Za-z_$][\w$]*\s*=>)");
            static readonly Regex PsFuncRe = new Regex(@"^\s*function\s+([\w-]+)", RegexOptions.IgnoreCase);
            static readonly Regex SqlRe = new Regex(@"^\s*CREATE\s+(?:OR\s+REPLACE\s+)?(?:TABLE|VIEW|PROCEDURE|PROC|FUNCTION|PACKAGE(?:\s+BODY)?|TRIGGER|MACRO|INDEX)\s+(?:IF\s+NOT\s+EXISTS\s+)?([\w.\[\]""]+)", RegexOptions.IgnoreCase);
            static readonly Regex MdRe = new Regex(@"^(#{1,4})\s+(.+?)\s*#*\s*$");
            static readonly HashSet<string> NotNames = new HashSet<string>(new[] { "if", "for", "foreach", "while", "switch", "catch", "using", "lock",
                "return", "else", "do", "try", "fixed", "checked", "unchecked", "new", "typeof", "sizeof", "nameof", "default", "when", "with", "await", "throw", "base", "this" });

            static string Symbol(string line, string ext)
            {
                Match m;
                if (ext == ".md" || ext == ".markdown") { m = MdRe.Match(line); return m.Success ? m.Groups[2].Value : null; }
                if (ext == ".sql") { m = SqlRe.Match(line); return m.Success ? m.Groups[1].Value : null; }
                if (ext == ".ps1" || ext == ".psm1") { m = PsFuncRe.Match(line); if (m.Success) return m.Groups[1].Value; }
                if (line.Length > 300 || line.TrimStart().StartsWith("//") || line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith("*")) return null;
                m = TypeRe.Match(line);
                if (m.Success) return m.Groups[1].Value + " " + m.Groups[2].Value;
                m = ArrowRe.Match(line);
                if (m.Success) return m.Groups[1].Value;
                m = FuncRe.Match(line);
                if (m.Success)
                {
                    string name = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
                    if (name.Length > 0 && !NotNames.Contains(name) && !line.TrimEnd().EndsWith(";")) return name;
                }
                return null;
            }

            public static void Split(FileRec rec, string text)
            {
                string ext = Path.GetExtension(rec.Rel).ToLowerInvariant();
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                StringBuilder cur = new StringBuilder();
                int start = 1;
                string sym = null;
                for (int i = 0; i < lines.Length; i++)
                {
                    string l = lines[i];
                    string s = Symbol(l, ext);
                    if (s != null) rec.Symbols.Add(s + " (L" + (i + 1) + ")");
                    // a declaration starts a new section once the current one has some substance
                    bool cut = (s != null && cur.Length >= 200) || cur.Length >= MaxChars ||
                               (cur.Length >= TargetChars && l.Trim().Length == 0);
                    if (cut && cur.Length > 0) { Emit(rec, cur.ToString(), start, i, sym); cur.Length = 0; start = i + 1; sym = null; }
                    if (s != null && sym == null) sym = s;
                    cur.Append(l).Append('\n');
                }
                if (cur.ToString().Trim().Length > 0) Emit(rec, cur.ToString(), start, lines.Length, sym);
            }

            public static void Emit(FileRec rec, string text, int start, int end, string sym)
            {
                if (text.Trim().Length == 0) return;
                Chunk c = new Chunk();
                c.File = rec; c.Text = text.TrimEnd('\n'); c.Start = start; c.End = Math.Max(start, end); c.Symbol = sym;
                c.Title = sym ?? ("lines " + c.Start + "-" + c.End);
                c.Tf = new Dictionary<ulong, int>();
                List<ulong> t = new List<ulong>();
                Terms.Extract(rec.Rel, t);          // the path counts: "Workspace.cs" finds the file by name
                Terms.Extract(c.Title, t);
                Terms.Extract(c.Text, t);
                foreach (ulong h in t) { int v; c.Tf.TryGetValue(h, out v); c.Tf[h] = v + 1; }
                c.Len = t.Count;
                c.TextHash = Terms.Hash(c.Title + "\n" + c.Text);
                rec.Chunks.Add(c);
            }
        }

        // ---- persistence ----

        const int Version = 3;   // 3: symbols also after unsafe/readonly/ref modifiers

        public void Save()
        {
            List<FileRec> files;
            lock (_lock) { files = new List<FileRec>(_files.Values); _dirtyAt = 0; }
            Directory.CreateDirectory(Path.GetDirectoryName(IndexFile));
            string tmp = IndexFile + ".tmp";
            using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            using (BinaryWriter w = new BinaryWriter(fs, Encoding.UTF8))
            {
                w.Write("PHXPIDX"); w.Write(Version);
                lock (_lock)
                {
                    w.Write(files.Count);
                    foreach (FileRec f in files)
                    {
                        w.Write(f.Rel); w.Write(f.Size); w.Write(f.Mtime); w.Write(f.Hash);
                        w.Write(f.Symbols.Count);
                        foreach (string sy in f.Symbols) w.Write(sy);
                        w.Write(f.Chunks.Count);
                        foreach (Chunk c in f.Chunks)
                        {
                            w.Write(c.Start); w.Write(c.End); w.Write(c.Symbol ?? ""); w.Write(c.Text);
                            w.Write(c.Vec != null ? c.Vec.Length : 0);
                            if (c.Vec != null) foreach (float x in c.Vec) w.Write(x);
                        }
                    }
                }
            }
            if (File.Exists(IndexFile)) File.Replace(tmp, IndexFile, null); else File.Move(tmp, IndexFile);
        }

        void Load()
        {
            if (!File.Exists(IndexFile)) return;
            using (FileStream fs = new FileStream(IndexFile, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (BinaryReader r = new BinaryReader(fs, Encoding.UTF8))
            {
                if (r.ReadString() != "PHXPIDX" || r.ReadInt32() != Version) throw new InvalidDataException("old format");
                int nf = r.ReadInt32();
                lock (_lock)
                {
                    ClearAll();
                    for (int i = 0; i < nf; i++)
                    {
                        FileRec f = new FileRec();
                        f.Rel = r.ReadString(); f.Size = r.ReadInt64(); f.Mtime = r.ReadInt64(); f.Hash = r.ReadBytes(32);
                        int ns = r.ReadInt32();
                        for (int k = 0; k < ns; k++) f.Symbols.Add(r.ReadString());
                        int nc = r.ReadInt32();
                        for (int k = 0; k < nc; k++)
                        {
                            int st = r.ReadInt32(), en = r.ReadInt32();
                            string sy = r.ReadString(), t = r.ReadString();
                            int d = r.ReadInt32();
                            float[] v = null;
                            if (d > 0) { v = new float[d]; for (int j = 0; j < d; j++) v[j] = r.ReadSingle(); }
                            Chunker.Emit(f, t, st, en, sy.Length > 0 ? sy : null);
                            if (f.Chunks.Count > 0 && f.Chunks[f.Chunks.Count - 1].Text == t) f.Chunks[f.Chunks.Count - 1].Vec = v;
                        }
                        _files[f.Rel] = f;
                        AddFile(f);
                    }
                }
            }
        }

        public void Dispose()
        {
            _stop = true;
            try { if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); } } catch { }
            _wake.Set(); _embedWake.Set();
            try { if (_updater != null) _updater.Join(2000); } catch { }
            try { if (_embedThread != null) _embedThread.Join(2000); } catch { }
            try { if (_dirtyAt != 0) Save(); } catch { }
        }
    }
}
