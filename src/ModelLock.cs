// One loaded chat model per machine. C# 5 only.
// A second Phoenix process (another server, a test, a gate or a benchmark) loading a model while one is loaded put two
// models in VRAM at once (2026-10-17, twice). Whoever loads a chat model holds %LOCALAPPDATA%\LocalLLMStudio\model.lock
// open for writing (others may read it, to say who has it); Windows drops the handle when that process exits, even
// after a crash, so the lock never outlives its process. Re-entrant within a process (a reload in the same server).
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Phoenix
{
    public static class ModelLock
    {
        static readonly object _gate = new object();
        static FileStream _held;
        static int _count;

        static string LockPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLLMStudio", "model.lock"); }
        }

        // Takes the lock for this process, or throws if another process holds it.
        public static void Acquire(string modelPath)
        {
            lock (_gate)
            {
                if (_held != null) { _count++; Write(modelPath); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(LockPath));
                FileStream fs;
                try { fs = new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read); }
                catch (IOException)
                {
                    throw new InvalidOperationException("Another Phoenix process " + Holder() + " already has a model loaded. " +
                        "Unload it there (or close it) first; two models at once do not fit in the video memory.");
                }
                _held = fs; _count = 1;
                Write(modelPath);
            }
        }

        // Throws if another process holds the lock (without taking it): checked before a load allocates anything.
        public static void Check()
        {
            lock (_gate)
            {
                if (_held != null || !File.Exists(LockPath)) return;
                try { using (new FileStream(LockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { } }
                catch (IOException)
                {
                    throw new InvalidOperationException("Another Phoenix process " + Holder() + " already has a model loaded. " +
                        "Unload it there (or close it) first; two models at once do not fit in the video memory.");
                }
            }
        }

        public static void Release()
        {
            lock (_gate)
            {
                if (_held == null || --_count > 0) return;
                try { _held.Dispose(); } catch { }
                _held = null;
            }
        }

        static void Write(string modelPath)
        {
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(Process.GetCurrentProcess().Id + "\n" + Path.GetFileName(modelPath ?? "") + "\n");
                _held.SetLength(0); _held.Write(b, 0, b.Length); _held.Flush();
            }
            catch { }
        }

        // "(PID 1234, gemma-4-12b...gguf)" from the holder's note, or "".
        static string Holder()
        {
            try
            {
                using (FileStream fs = new FileStream(LockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader r = new StreamReader(fs, Encoding.UTF8))
                {
                    string pid = r.ReadLine(), model = r.ReadLine();
                    return "(PID " + pid + (string.IsNullOrEmpty(model) ? "" : ", " + model) + ")";
                }
            }
            catch { return ""; }
        }
    }
}
