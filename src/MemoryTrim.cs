// Gives system RAM back after a model is uploaded to the GPU. C# 5 only; kernel32 only.
// Loading reads the whole .gguf through its memory mapping, which leaves several GB of the file in this process's working
// set ("In use" in Task Manager) although the GPU now holds the weights. Trimming moves those file-backed pages to the
// standby list, where Windows hands them to other programs at once (nothing is written to the page file). The few pages
// still used afterwards (token embedding rows, tokenizer) come back on demand.
using System;
using System.Runtime.InteropServices;

namespace Phoenix
{
    public static class MemoryTrim
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

        // Frees managed garbage left by the load (staging arrays), then empties the working set. Returns MB released.
        public static long Trim()
        {
            long before = Environment.WorkingSet;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try { SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); } catch { }
            return Math.Max(0, before - Environment.WorkingSet) >> 20;
        }
    }
}
