// The active project's live index (ProjectIndex): switched with the connected folder, fed by the model's own file
// writes, searched for every message while a project is active. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        ProjectIndex _pindex;
        readonly object _pindexLock = new object();

        // True while the chat model holds the engine (a reply, a load, an answer-while-typing draft): background
        // embedding waits so it never slows a reply down.
        bool EngineBusy()
        {
            if (Monitor.TryEnter(_engineLock)) { Monitor.Exit(_engineLock); return false; }
            return true;
        }

        // Called whenever the connected folder changes: keeps the index of the project it belongs to (or none).
        void SwitchProjectIndex()
        {
            string root = _folderAccess ? ProjectRootOf(_workspace) : null;
            ProjectIndex old = null, fresh = null;
            lock (_pindexLock)
            {
                if (_pindex != null && root != null && string.Equals(_pindex.Root, System.IO.Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                old = _pindex; _pindex = null;
                if (root != null) { fresh = new ProjectIndex(root, delegate { return Rag.SharedEmbedder(); }, EngineBusy); _pindex = fresh; }
            }
            if (old != null) ThreadPool.QueueUserWorkItem(delegate { try { old.Dispose(); } catch { } });
            if (fresh != null)
            {
                Thread t = new Thread(delegate()
                {
                    try { string r = fresh.Start(); Log.Text("project index " + System.IO.Path.GetFileName(fresh.Root) + ": " + r); }
                    catch (Exception ex) { Log.Text("project index failed: " + ex.Message); }
                });
                t.IsBackground = true; t.Start();
            }
        }

        // Adds the project code matching the newest user message (searched with its own text) in front of it.
        void AddProjectHits(List<ChatMessage> msgs, int ui, string query, int rid, bool log)
        {
            ProjectIndex pi = _pindex;
            if (pi == null || ui < 0 || string.IsNullOrEmpty(query)) return;
            Stopwatch sw = Stopwatch.StartNew();
            List<ProjectIndex.Hit> hits = pi.Search(query, Rag.IsCodeRequest(query));
            if (hits.Count == 0) return;
            List<byte[]> keep = msgs[ui].Images;
            msgs[ui] = new ChatMessage("user", pi.Wrap(hits, msgs[ui].Text));
            msgs[ui].Images = keep;
            if (log)
            {
                List<string> s = new List<string>();
                foreach (ProjectIndex.Hit h in hits) s.Add(h.File + ":" + h.Start + "-" + h.End + (h.Symbol != null ? " " + h.Symbol : ""));
                Log.Text("#" + rid + " PROJECT " + hits.Count + " sections in " + ChatLog.Ms(sw.Elapsed.TotalSeconds) + " ms: " + string.Join("; ", s.ToArray()));
            }
        }

        // After the model changed files: re-indexes them now and returns the event line for the page, or null.
        string IndexModelChanges(List<string> fullPaths, int rid)
        {
            ProjectIndex pi = _pindex;
            if (pi == null || fullPaths.Count == 0) return null;
            double ms;
            pi.Update(fullPaths, out ms);
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string f in fullPaths) names.Add(System.IO.Path.GetFileName(f));
            Log.Text("#" + rid + " INDEXED " + names.Count + " file(s) in " + ms.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            StringBuilder sb = new StringBuilder("{\"indexed\":{\"files\":[");
            int i = 0;
            foreach (string n in names) { if (i++ > 0) sb.Append(','); sb.Append(Json.Quote(n)); }
            return sb.Append("],\"ms\":").Append(Json.Num(Math.Round(ms, 1))).Append("}}").ToString();
        }
    }
}
