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

        [StructLayout(LayoutKind.Sequential)]
        struct MemoryStatusEx
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

        // RAM for the status bar's CPU-mode item, as JSON in bytes: "used" = this app with its model (working set,
        // which includes the resident part of the memory-mapped weights), "avail" = what it could have = its own use
        // plus the RAM free right now (so it drops when other programs take memory), "total" = installed.
        public static string RamJson()
        {
            MemoryStatusEx m = new MemoryStatusEx();
            m.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            if (!GlobalMemoryStatusEx(ref m)) return "null";
            long ws = Environment.WorkingSet;
            return "{\"used\":" + ws + ",\"avail\":" + (ws + (long)m.AvailPhys) + ",\"total\":" + m.TotalPhys + "}";
        }

        // Physical RAM available to programs right now, in bytes (the CPU path's auto context).
        public static long AvailableRam()
        {
            MemoryStatusEx m = new MemoryStatusEx();
            m.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            return GlobalMemoryStatusEx(ref m) ? (long)m.AvailPhys : 4L << 30;
        }

        // Commit charge still available (RAM + page file not yet promised to programs), in bytes.
        public static long AvailableCommit()
        {
            MemoryStatusEx m = new MemoryStatusEx();
            m.Length = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
            return GlobalMemoryStatusEx(ref m) ? (long)m.AvailPageFile : 8L << 30;
        }

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
