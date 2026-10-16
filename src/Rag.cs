// Local retrieval (RAG) over the docs\ folder: markdown-aware chunking, BM25 keyword search, and a memory-mapped
// binary index (docs\.index\rag.bin) that is rebuilt incrementally by Rag.Reindex and swapped in when complete.
// C# 5 only.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Phoenix
{
    public sealed class RagHit
    {
        public string File, Title, Text;
        public float Score;
    }

    public sealed class Rag : IDisposable
    {
        // Tuned defaults: ~400-token chunks, BM25 k1/b from the literature, ~1500 tokens of context per message.
        public const int TargetChunkChars = 1600, MaxChunkChars = 2400, MinChunkChars = 300;
        public const float K1 = 1.2f, B = 0.75f;
        public int TopK = 6, BudgetChars = 6000;
        // A chunk must match >= 2 of the message's terms (or >= MinCoverage of them for short messages) and score
        // >= MinRelative of the best hit, so chit-chat and off-topic questions retrieve nothing.
        public float MinRelative = 0.35f, MinCoverage = 0.3f;
        public bool Enabled = true;

        static readonly string[] TextExtensions = { ".md", ".markdown", ".txt", ".rst", ".sql", ".cs", ".py", ".js", ".ts", ".tsx", ".jsx",
            ".java", ".c", ".h", ".cpp", ".hpp", ".go", ".rs", ".ps1", ".psm1", ".sh", ".json", ".yaml", ".yml", ".xml", ".html", ".css", ".ini", ".toml" };
        const long MaxFileBytes = 32L << 20;

        public readonly string DocsDir, IndexPath;
        readonly object _swapLock = new object();
        RagIndex _index;
        int _busy;
        string _progress, _lastResult, _lastError;

        public Rag(string docsDir)
        {
            DocsDir = Path.GetFullPath(docsDir);
            IndexPath = Path.Combine(DocsDir, ".index", "rag.bin");
            Directory.CreateDirectory(DocsDir);
            string fresh = IndexPath + ".new";
            if (!File.Exists(IndexPath) && File.Exists(fresh)) File.Move(fresh, IndexPath);   // finish an interrupted swap
            try { if (File.Exists(IndexPath)) _index = new RagIndex(IndexPath); } catch (Exception ex) { _lastError = "index unreadable: " + ex.Message; }
        }

        public bool Indexing { get { return _busy != 0; } }

        public string StatusJson()
        {
            RagIndex ix = _index;
            return "{\"enabled\":" + (Enabled ? "true" : "false") +
                   ",\"files\":" + (ix != null ? ix.FileCount : 0) + ",\"chunks\":" + (ix != null ? ix.ChunkCount : 0) +
                   ",\"indexing\":" + (_busy != 0 ? Json.Quote(_progress ?? "starting") : "null") +
                   ",\"last\":" + (_lastResult != null ? Json.Quote(_lastResult) : "null") +
                   ",\"error\":" + (_lastError != null ? Json.Quote(_lastError) : "null") + "}";
        }

        // Starts a background reindex. Returns false if one is already running.
        public bool StartReindex(Action<string> log)
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
            _lastError = null;
            Thread t = new Thread(delegate()
            {
                try { _lastResult = Reindex(); if (log != null) log("rag: " + _lastResult); }
                catch (Exception ex) { _lastError = ex.Message; if (log != null) log("rag: reindex FAILED: " + ex.Message); }
                finally { _progress = null; _busy = 0; }
            });
            t.IsBackground = true;
            t.Priority = ThreadPriority.BelowNormal;
            t.Start();
            return true;
        }

        sealed class FileEntry
        {
            public string Rel, Full;
            public long Size, Mtime;
            public byte[] Hash;
            public List<string[]> Chunks;   // {title, text}
            public bool Reused;
        }

        // Rebuilds the index from docs\, reusing chunks of files whose size+time (or content hash) did not change.
        string Reindex()
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            _progress = "scanning";
            List<FileEntry> files = new List<FileEntry>();
            string indexDir = Path.GetDirectoryName(IndexPath);
            foreach (string f in Directory.EnumerateFiles(DocsDir, "*", SearchOption.AllDirectories))
            {
                if (f.StartsWith(indexDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if (Array.IndexOf(TextExtensions, Path.GetExtension(f).ToLowerInvariant()) < 0) continue;
                FileInfo fi = new FileInfo(f);
                if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || fi.Length > MaxFileBytes) continue;
                FileEntry e = new FileEntry();
                e.Full = f; e.Rel = f.Substring(DocsDir.Length + 1).Replace('\\', '/');
                e.Size = fi.Length; e.Mtime = fi.LastWriteTimeUtc.Ticks;
                files.Add(e);
            }
            files.Sort(delegate(FileEntry a, FileEntry b) { return string.CompareOrdinal(a.Rel, b.Rel); });

            // Reuse from the current index where possible.
            RagIndex old = _index;
            Dictionary<string, int> oldFiles = old != null ? old.FileMap() : new Dictionary<string, int>();
            int changed = 0, done = 0, removed = 0;
            foreach (string k in oldFiles.Keys) { bool still = false; foreach (FileEntry e in files) if (e.Rel == k) { still = true; break; } if (!still) removed++; }
            Parallel.For(0, files.Count, delegate(int i)
            {
                FileEntry e = files[i];
                int oi;
                if (old != null && oldFiles.TryGetValue(e.Rel, out oi) && old.FileSize(oi) == e.Size && old.FileMtime(oi) == e.Mtime)
                {
                    e.Hash = old.FileHash(oi); e.Chunks = old.FileChunks(oi); e.Reused = true;
                }
                else
                {
                    byte[] bytes = File.ReadAllBytes(e.Full);
                    using (SHA256 sha = SHA256.Create()) e.Hash = sha.ComputeHash(bytes);
                    if (old != null && oldFiles.TryGetValue(e.Rel, out oi) && SameHash(old.FileHash(oi), e.Hash)) { e.Chunks = old.FileChunks(oi); e.Reused = true; }
                    else { e.Chunks = Chunker.Split(e.Rel, DecodeText(bytes)); Interlocked.Increment(ref changed); }
                }
                _progress = "chunking " + Interlocked.Increment(ref done) + "/" + files.Count;
            });

            _progress = "building index";
            string tmp = IndexPath + ".tmp", fresh = IndexPath + ".new";
            Directory.CreateDirectory(Path.GetDirectoryName(IndexPath));
            int chunkCount = RagIndex.Write(tmp, files.ConvertAll(delegate(FileEntry e) { return new RagIndex.Source(e.Rel, e.Size, e.Mtime, e.Hash, e.Chunks); }));
            if (File.Exists(fresh)) File.Delete(fresh);
            File.Move(tmp, fresh);

            // Swap: searches hold _swapLock, so none is reading the old mapping while it is replaced.
            lock (_swapLock)
            {
                if (_index != null) { _index.Dispose(); _index = null; }
                if (File.Exists(IndexPath)) File.Delete(IndexPath);
                File.Move(fresh, IndexPath);
                _index = new RagIndex(IndexPath);
            }
            return string.Format("{0} files, {1} chunks ({2} new/changed, {3} removed) in {4:F1} s",
                files.Count, chunkCount, changed, removed, sw.Elapsed.TotalSeconds);
        }

        static bool SameHash(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static string DecodeText(byte[] b)
        {
            string s = new UTF8Encoding(false, false).GetString(b);
            if (s.Length > 0 && s[0] == '﻿') s = s.Substring(1);
            return s.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        // Best chunks for a query, within TopK and BudgetChars. `context` (e.g. the previous question) counts half.
        public List<RagHit> Search(string query, string context)
        {
            List<RagHit> hits = new List<RagHit>();
            if (string.IsNullOrEmpty(query)) return hits;
            Dictionary<ulong, float> q = new Dictionary<ulong, float>();
            AddQueryTerms(q, query, 1f);
            if (!string.IsNullOrEmpty(context)) AddQueryTerms(q, context, 0.5f);
            if (q.Count == 0) return hits;
            lock (_swapLock)
            {
                if (_index == null) return hits;
                return _index.Search(q, TopK, BudgetChars, MinRelative, MinCoverage);
            }
        }

        // Weights: 1 = topic word of the message, 0.75 = common word (scores but does not count as a match),
        // 0.5 = word from the previous message. Each term counts once.
        static void AddQueryTerms(Dictionary<ulong, float> q, string text, float w)
        {
            List<ulong> terms = new List<ulong>();
            Terms.Extract(text, terms);
            foreach (ulong t in terms)
            {
                float v = w >= 1f && Terms.IsCommon(t) ? 0.75f : w, cur;
                if (!q.TryGetValue(t, out cur) || cur < v) q[t] = v;
            }
        }

        // Prompt block placed before the user's question.
        public static string PromptBlock(List<RagHit> hits)
        {
            StringBuilder sb = new StringBuilder("[Docs library search results (RAG)]\n" +
                "The app searched the user's local docs library (the \"RAG\", \"docs\" or \"documents\" the user may mention) for this " +
                "message and found the excerpts below. They ARE the user's RAG documents: when asked what is in the RAG or docs, " +
                "describe these excerpts. Use them when relevant, prefer them over memory, and cite them as [n]. " +
                "Only these best-matching excerpts are shown, not the whole library.\n");
            for (int i = 0; i < hits.Count; i++)
                sb.Append("\n[").Append(i + 1).Append("] ").Append(hits[i].File).Append(hits[i].Title.Length > 0 ? " › " + hits[i].Title : "")
                  .Append('\n').Append(hits[i].Text.Trim()).Append('\n');
            return sb.ToString();
        }

        public void Dispose()
        {
            lock (_swapLock) { if (_index != null) _index.Dispose(); _index = null; }
        }
    }

    // Term extraction shared by indexing and search: lowercase words; identifiers also split into their parts
    // (snake_case, camelCase, letter/digit), so "UploadQ8_0" matches "upload", "q8", "uploadq8_0".
    static class Terms
    {
        static readonly HashSet<string> Stop = new HashSet<string>(new string[] {
            "the", "a", "an", "of", "to", "i", "you", "me", "my", "we", "our", "please", "can", "could", "would", "should",
            "what", "how", "does", "do", "this", "that", "it", "its", "be", "are", "was", "there", "here", "about", "some" });

        // Everyday words: searchable (SQL keywords like IN, AND, FROM stay indexed) but never enough to trigger retrieval.
        static readonly HashSet<ulong> Common = Hashes("is in on at for with as by and or not from into all any get set use make want need " +
            "today now new one will just like good great nice thanks thank work hello hi hey yes no ok okay also then than so if when which " +
            "who why where have has had were been being am but more most very really much many way thing time help know think tell show give " +
            "write explain let try see look sure right wrong fine well again still other same each every");

        static HashSet<ulong> Hashes(string words)
        {
            HashSet<ulong> h = new HashSet<ulong>();
            foreach (string w in words.Split(' ')) h.Add(Hash(w));
            return h;
        }

        public static bool IsCommon(ulong h) { return Common.Contains(h); }

        public static void Extract(string s, List<ulong> output)
        {
            int n = s.Length, i = 0;
            while (i < n)
            {
                while (i < n && !IsWord(s[i])) i++;
                int start = i;
                while (i < n && IsWord(s[i])) i++;
                if (i > start) Word(s, start, i, output);
            }
        }

        static bool IsWord(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        static void Word(string s, int start, int end, List<ulong> output)
        {
            int len = end - start;
            if (len > 64) return;   // hashes, base64 and the like
            bool split = false;
            for (int k = start + 1; k < end; k++)
                if (s[k] == '_' || (char.IsUpper(s[k]) && char.IsLower(s[k - 1])) || (char.IsDigit(s[k]) != char.IsDigit(s[k - 1]))) { split = true; break; }
            string whole = s.Substring(start, len).ToLowerInvariant().Trim('_');
            if (whole.Length >= 2 && !Stop.Contains(whole)) output.Add(Hash(whole));
            if (!split) return;
            int p = start;
            for (int k = start + 1; k <= end; k++)
            {
                bool cut = k == end || s[k] == '_' || s[k - 1] == '_' || (char.IsUpper(s[k]) && char.IsLower(s[k - 1]));
                if (!cut) continue;
                string part = s.Substring(p, k - p).ToLowerInvariant().Trim('_');
                if (part.Length >= 2 && part != whole && !Stop.Contains(part)) output.Add(Hash(part));
                p = k;
            }
        }

        // Light English suffix folding (rows/row, deleted/deleting/delete) for plain words only; identifiers stay exact.
        static string Stem(string w)
        {
            for (int i = 0; i < w.Length; i++) if (w[i] < 'a' || w[i] > 'z') return w;
            if (w.Length > 5 && w.EndsWith("ies")) w = w.Substring(0, w.Length - 3) + "y";
            else if (w.Length > 5 && w.EndsWith("ing")) w = w.Substring(0, w.Length - 3);
            else if (w.Length > 4 && w.EndsWith("ed")) w = w.Substring(0, w.Length - 2);
            else if (w.Length > 3 && w.EndsWith("s") && !w.EndsWith("ss") && !w.EndsWith("us") && !w.EndsWith("is")) w = w.Substring(0, w.Length - 1);
            if (w.Length > 4 && w.EndsWith("e")) w = w.Substring(0, w.Length - 1);
            return w;
        }

        public static ulong Hash(string s)
        {
            s = Stem(s);
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 1099511628211UL; }
            return h == 0 ? 1 : h;   // 0 marks an empty slot in the index
        }
    }

    // Splits a document into chunks of about TargetChunkChars. Markdown: sections by heading (title = heading path),
    // never cutting inside a fenced code block unless the block alone is over the hard limit. Other text: by blank lines.
    static class Chunker
    {
        public static List<string[]> Split(string rel, string text)
        {
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            bool md = ext == ".md" || ext == ".markdown";
            List<string[]> sections = new List<string[]>();   // {title, text}
            string[] headings = new string[7];
            StringBuilder cur = new StringBuilder();
            string curTitle = "";
            bool fence = false;
            string fenceMark = null;
            foreach (string line in text.Split('\n'))
            {
                string t = line.TrimStart();
                if (md && (t.StartsWith("```") || t.StartsWith("~~~")))
                {
                    string mark = t.Substring(0, 3);
                    if (!fence) { fence = true; fenceMark = mark; }
                    else if (mark == fenceMark) fence = false;
                }
                else if (md && !fence && t.StartsWith("#"))
                {
                    int level = 0;
                    while (level < t.Length && t[level] == '#') level++;
                    if (level <= 6 && level < t.Length && t[level] == ' ')
                    {
                        Flush(sections, curTitle, cur);
                        headings[level] = t.Substring(level + 1).Trim().TrimEnd('#').Trim();
                        for (int k = level + 1; k <= 6; k++) headings[k] = null;
                        List<string> path = new List<string>();
                        for (int k = 1; k <= 6; k++) if (!string.IsNullOrEmpty(headings[k])) path.Add(headings[k]);
                        curTitle = string.Join(" › ", path.ToArray());
                    }
                }
                cur.Append(line).Append('\n');
            }
            Flush(sections, curTitle, cur);

            // Merge tiny sections into the following one (e.g. a heading with one line) and split big ones.
            List<string[]> chunks = new List<string[]>();
            string pendTitle = null;
            StringBuilder pend = new StringBuilder();
            foreach (string[] sec in sections)
            {
                if (pend.Length > 0 && pend.Length + sec[1].Length <= TargetChunkChars)
                {
                    pend.Append('\n').Append(sec[1]);
                    string leaf = sec[0].Substring(sec[0].LastIndexOf('›') + 1).Trim();
                    if (leaf.Length > 0 && pendTitle.IndexOf(leaf) < 0) pendTitle += (pendTitle.Length > 0 ? " + " : "") + leaf;
                    continue;
                }
                if (pend.Length > 0) { SplitBig(chunks, pendTitle, pend.ToString(), md); pend.Length = 0; }
                pendTitle = sec[0];
                pend.Append(sec[1]);
                if (pend.Length >= Rag.MinChunkChars) { SplitBig(chunks, pendTitle, pend.ToString(), md); pend.Length = 0; }
            }
            if (pend.Length > 0) SplitBig(chunks, pendTitle, pend.ToString(), md);
            return chunks;
        }

        const int TargetChunkChars = Rag.TargetChunkChars;

        static void Flush(List<string[]> sections, string title, StringBuilder cur)
        {
            string s = cur.ToString().Trim('\n');
            cur.Length = 0;
            if (s.Trim().Length > 0) sections.Add(new string[] { title, s });
        }

        // Cuts a section at paragraph breaks outside code fences; the last short paragraph is repeated as overlap.
        static void SplitBig(List<string[]> chunks, string title, string text, bool md)
        {
            if (text.Length <= Rag.MaxChunkChars) { chunks.Add(new string[] { title, text }); return; }
            List<string> paras = new List<string>();
            StringBuilder p = new StringBuilder();
            bool fence = false;
            foreach (string line in text.Split('\n'))
            {
                string t = line.TrimStart();
                if (md && (t.StartsWith("```") || t.StartsWith("~~~"))) fence = !fence;
                if (!fence && line.Trim().Length == 0 && p.Length > 0) { paras.Add(p.ToString()); p.Length = 0; continue; }
                p.Append(line).Append('\n');
                if (p.Length >= Rag.MaxChunkChars) { paras.Add(p.ToString()); p.Length = 0; }   // giant block: hard cut by lines
            }
            if (p.Length > 0) paras.Add(p.ToString());

            StringBuilder c = new StringBuilder();
            string lastPara = null;
            foreach (string para in paras)
            {
                if (c.Length > 0 && c.Length + para.Length > TargetChunkChars)
                {
                    chunks.Add(new string[] { title, c.ToString().TrimEnd() });
                    c.Length = 0;
                    if (lastPara != null && lastPara.Length < 400 && lastPara.IndexOf("```") < 0) c.Append(lastPara).Append('\n');
                }
                c.Append(para).Append('\n');
                lastPara = para;
            }
            if (c.ToString().Trim().Length > 0) chunks.Add(new string[] { title, c.ToString().TrimEnd() });
        }
    }

    // On-disk index. Layout (little-endian):
    //   header   "PHXRAG01", u32 files, u32 chunks, u32 tableSize (power of 2), u32 reserved, f32 avgLen, u32 pad,
    //            u64 offsets: files, chunks, table, postings, blob
    //   files    {u64 pathOff, u32 pathLen, u32 firstChunk, u32 chunkCount, u32 pad, i64 size, i64 mtime, byte[32] sha256}  (72 B)
    //   chunks   {u64 titleOff, u32 titleLen, u32 textLen (text follows title in blob), u32 fileIdx, u32 docLen} (24 B)
    //   table    open addressing on FNV-1a term hash: {u64 hash, u32 postingOff, u32 df}                       (16 B)
    //   postings {u32 chunk, u32 tf}, grouped by term
    //   blob     UTF-8 strings
    unsafe sealed class RagIndex : IDisposable
    {
        public sealed class Source
        {
            public string Rel; public long Size, Mtime; public byte[] Hash; public List<string[]> Chunks;
            public Source(string rel, long size, long mtime, byte[] hash, List<string[]> chunks) { Rel = rel; Size = size; Mtime = mtime; Hash = hash; Chunks = chunks; }
        }

        const int HeaderBytes = 72, FileBytes = 72, ChunkBytes = 24, SlotBytes = 16;
        static readonly byte[] Magic = Encoding.ASCII.GetBytes("PHXRAG01");

        MemoryMappedFile _mmf;
        MemoryMappedViewAccessor _view;
        byte* _base;
        public readonly int FileCount, ChunkCount;
        readonly int _tableSize;
        readonly float _avgLen;
        readonly byte* _files, _chunks, _table, _postings, _blob;

        public RagIndex(string path)
        {
            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            try { _mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, null, HandleInheritability.None, false); }
            catch { fs.Dispose(); throw; }
            _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref _base);
            _base += _view.PointerOffset;
            for (int i = 0; i < 8; i++) if (_base[i] != Magic[i]) throw new InvalidDataException("Not a PhoenixAI RAG index.");
            FileCount = *(int*)(_base + 8); ChunkCount = *(int*)(_base + 12); _tableSize = *(int*)(_base + 16);
            _avgLen = *(float*)(_base + 24);
            _files = _base + *(long*)(_base + 32); _chunks = _base + *(long*)(_base + 40); _table = _base + *(long*)(_base + 48);
            _postings = _base + *(long*)(_base + 56); _blob = _base + *(long*)(_base + 64);
        }

        string Str(long off, int len) { return len == 0 ? "" : new string((sbyte*)(_blob + off), 0, len, Encoding.UTF8); }

        byte* FileRec(int i) { return _files + (long)i * FileBytes; }
        public string FilePath(int i) { byte* r = FileRec(i); return Str(*(long*)r, *(int*)(r + 8)); }
        public long FileSize(int i) { return *(long*)(FileRec(i) + 24); }
        public long FileMtime(int i) { return *(long*)(FileRec(i) + 32); }
        public byte[] FileHash(int i) { byte[] h = new byte[32]; byte* r = FileRec(i) + 40; for (int k = 0; k < 32; k++) h[k] = r[k]; return h; }

        public Dictionary<string, int> FileMap()
        {
            Dictionary<string, int> m = new Dictionary<string, int>();
            for (int i = 0; i < FileCount; i++) m[FilePath(i)] = i;
            return m;
        }

        public List<string[]> FileChunks(int i)
        {
            byte* r = FileRec(i);
            int first = *(int*)(r + 12), count = *(int*)(r + 16);
            List<string[]> list = new List<string[]>(count);
            for (int c = first; c < first + count; c++) list.Add(new string[] { ChunkTitle(c), ChunkText(c) });
            return list;
        }

        byte* ChunkRec(int c) { return _chunks + (long)c * ChunkBytes; }
        string ChunkTitle(int c) { byte* r = ChunkRec(c); return Str(*(long*)r, *(int*)(r + 8)); }
        string ChunkText(int c) { byte* r = ChunkRec(c); return Str(*(long*)r + *(int*)(r + 8), *(int*)(r + 12)); }

        public List<RagHit> Search(Dictionary<ulong, float> query, int topK, int budgetChars, float minRelative, float minCoverage)
        {
            float[] scores = new float[ChunkCount];
            byte[] matched = new byte[ChunkCount];
            List<int> touched = new List<int>();
            int mask = _tableSize - 1, primaryTerms = 0;
            foreach (KeyValuePair<ulong, float> q in query)
            {
                if (q.Value >= 1f) primaryTerms++;
                int df = 0, off = 0;
                for (int slot = (int)(q.Key & (ulong)mask); ; slot = (slot + 1) & mask)
                {
                    byte* s = _table + (long)slot * SlotBytes;
                    ulong h = *(ulong*)s;
                    if (h == 0) break;
                    if (h == q.Key) { off = *(int*)(s + 8); df = *(int*)(s + 12); break; }
                }
                float idf = (float)Math.Log(1 + (ChunkCount - df + 0.5) / (df + 0.5)) * q.Value;
                uint* p = (uint*)(_postings + (long)off * 8);
                for (int k = 0; k < df; k++)
                {
                    int c = (int)p[2 * k];
                    float tf = p[2 * k + 1], len = *(int*)(ChunkRec(c) + 20);
                    if (scores[c] == 0) touched.Add(c);
                    scores[c] += idf * tf * (Rag.K1 + 1) / (tf + Rag.K1 * (1 - Rag.B + Rag.B * len / _avgLen));
                    if (q.Value >= 1f && matched[c] < 255) matched[c]++;
                }
            }
            touched.RemoveAll(delegate(int c) { return matched[c] == 0 || (matched[c] < 2 && matched[c] < minCoverage * primaryTerms); });
            touched.Sort(delegate(int a, int b) { return scores[b].CompareTo(scores[a]); });
            List<RagHit> hits = new List<RagHit>();
            if (touched.Count == 0) return hits;
            float floor = scores[touched[0]] * minRelative;
            int used = 0;
            foreach (int c in touched)
            {
                if (hits.Count >= topK || scores[c] < floor) break;
                string text = ChunkText(c);
                if (used + text.Length > budgetChars && hits.Count > 0) continue;   // a smaller one may still fit
                used += text.Length;
                RagHit h = new RagHit();
                h.File = FilePath(*(int*)(ChunkRec(c) + 16)); h.Title = ChunkTitle(c); h.Text = text; h.Score = scores[c];
                hits.Add(h);
            }
            return hits;
        }

        // Writes a complete index file; returns the number of chunks.
        public static int Write(string path, List<Source> files)
        {
            int nChunks = 0;
            foreach (Source f in files) nChunks += f.Chunks.Count;
            string[] titles = new string[nChunks], texts = new string[nChunks];
            int[] fileOf = new int[nChunks];
            int ci = 0;
            for (int i = 0; i < files.Count; i++)
                foreach (string[] ch in files[i].Chunks) { titles[ci] = ch[0]; texts[ci] = ch[1]; fileOf[ci] = i; ci++; }

            // Term frequencies per chunk (title words count too), in parallel.
            Dictionary<ulong, int>[] tfs = new Dictionary<ulong, int>[nChunks];
            int[] docLen = new int[nChunks];
            Parallel.For(0, nChunks, delegate(int c)
            {
                List<ulong> t = new List<ulong>();
                Terms.Extract(titles[c], t);
                Terms.Extract(texts[c], t);
                Dictionary<ulong, int> d = new Dictionary<ulong, int>();
                foreach (ulong h in t) { int v; d.TryGetValue(h, out v); d[h] = v + 1; }
                tfs[c] = d; docLen[c] = t.Count;
            });
            long totalLen = 0;
            for (int c = 0; c < nChunks; c++) totalLen += docLen[c];

            // Group postings by term (chunks ascending within each term).
            Dictionary<ulong, List<long>> byTerm = new Dictionary<ulong, List<long>>();
            for (int c = 0; c < nChunks; c++)
                foreach (KeyValuePair<ulong, int> kv in tfs[c])
                {
                    List<long> l;
                    if (!byTerm.TryGetValue(kv.Key, out l)) { l = new List<long>(); byTerm[kv.Key] = l; }
                    l.Add(((long)c << 32) | (uint)Math.Min(kv.Value, 65535));
                }
            int tableSize = 16;
            while (tableSize < byTerm.Count * 2) tableSize <<= 1;   // load factor <= 0.5: short probes

            ulong[] slotHash = new ulong[tableSize];
            int[] slotOff = new int[tableSize], slotDf = new int[tableSize];
            long nPost = 0;
            foreach (KeyValuePair<ulong, List<long>> kv in byTerm)
            {
                int slot = (int)(kv.Key & (ulong)(tableSize - 1));
                while (slotHash[slot] != 0) slot = (slot + 1) & (tableSize - 1);
                slotHash[slot] = kv.Key; slotOff[slot] = (int)nPost; slotDf[slot] = kv.Value.Count;
                nPost += kv.Value.Count;
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                MemoryStream blob = new MemoryStream();
                long filesOff = HeaderBytes, chunksOff = filesOff + (long)files.Count * FileBytes;
                long tableOff = Align(chunksOff + (long)nChunks * ChunkBytes), postOff = tableOff + (long)tableSize * SlotBytes;
                long blobOff = postOff + nPost * 8;

                w.Write(Magic); w.Write(files.Count); w.Write(nChunks); w.Write(tableSize); w.Write(0);
                w.Write(nChunks > 0 ? (float)totalLen / nChunks : 1f); w.Write(0);
                w.Write(filesOff); w.Write(chunksOff); w.Write(tableOff); w.Write(postOff); w.Write(blobOff);

                int first = 0;
                for (int i = 0; i < files.Count; i++)
                {
                    Source f = files[i];
                    long po; int pl; Put(blob, f.Rel, out po, out pl);
                    w.Write(po); w.Write(pl); w.Write(first); w.Write(f.Chunks.Count); w.Write(0); w.Write(f.Size); w.Write(f.Mtime);
                    w.Write(f.Hash != null && f.Hash.Length == 32 ? f.Hash : new byte[32]);
                    first += f.Chunks.Count;
                }
                for (int c = 0; c < nChunks; c++)
                {
                    long to; int tl, xl; long xo;
                    Put(blob, titles[c], out to, out tl); Put(blob, texts[c], out xo, out xl);
                    w.Write(to); w.Write(tl); w.Write(xl); w.Write(fileOf[c]); w.Write(docLen[c]);
                }
                while (fs.Position < tableOff) w.Write((byte)0);
                for (int s = 0; s < tableSize; s++) { w.Write(slotHash[s]); w.Write(slotOff[s]); w.Write(slotDf[s]); }
                // Postings go at each term's assigned offset.
                long[][] lists = new long[tableSize][];
                foreach (KeyValuePair<ulong, List<long>> kv in byTerm)
                {
                    int slot = (int)(kv.Key & (ulong)(tableSize - 1));
                    while (slotHash[slot] != kv.Key) slot = (slot + 1) & (tableSize - 1);
                    lists[slot] = kv.Value.ToArray();
                }
                long[] ordered = new long[nPost];
                for (int s = 0; s < tableSize; s++) if (lists[s] != null) Array.Copy(lists[s], 0, ordered, slotOff[s], lists[s].Length);
                for (long k = 0; k < nPost; k++) { w.Write((uint)(ordered[k] >> 32)); w.Write((uint)(ordered[k] & 0xFFFFFFFF)); }
                w.Flush();
                blob.WriteTo(fs);
            }
            return nChunks;
        }

        static long Align(long v) { return (v + 15) & ~15L; }

        static void Put(MemoryStream blob, string s, out long off, out int len)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            off = blob.Length; len = b.Length;
            blob.Write(b, 0, b.Length);
        }

        public void Dispose()
        {
            if (_view != null)
            {
                if (_base != null) _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _base = null;
                _view.Dispose(); _view = null;
            }
            if (_mmf != null) { _mmf.Dispose(); _mmf = null; }
        }
    }
}
