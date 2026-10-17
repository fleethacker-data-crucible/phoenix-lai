// Keeps the displays and the system awake while the server runs long GPU work in the background (docs indexing,
// Instant Recall). On 2026-10-16 the displays powered down for idle seven minutes into a /index that kept the GPU fully
// busy, and never woke again (no driver reset was logged); a display power transition in the middle of sustained GPU
// compute is a known way to leave AMD screens black. While a Scope is open, Windows does not turn the displays off or
// sleep. C# 5 only; kernel32 only.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Phoenix
{
    public static class StayAwake
    {
        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);

        const uint EsContinuous = 0x80000000, EsSystemRequired = 0x00000001, EsDisplayRequired = 0x00000002;

        static readonly object _gate = new object();
        static int _count;
        static Thread _holder;
        static AutoResetEvent _release;

        // using (StayAwake.Scope("indexing")) { ... }  Nested and concurrent scopes are counted.
        public static IDisposable Scope(string why)
        {
            lock (_gate)
            {
                if (_count++ == 0)
                {
                    // The execution state belongs to a thread: one background thread holds it for all scopes.
                    _release = new AutoResetEvent(false);
                    AutoResetEvent ev = _release;
                    _holder = new Thread(delegate()
                    {
                        SetThreadExecutionState(EsContinuous | EsSystemRequired | EsDisplayRequired);
                        ev.WaitOne();
                        SetThreadExecutionState(EsContinuous);
                    });
                    _holder.IsBackground = true;
                    _holder.Start();
                    Dbg.Msg("power", "keeping displays awake (" + why + ")");
                }
            }
            return new Releaser(why);
        }

        sealed class Releaser : IDisposable
        {
            readonly string _why;
            bool _done;
            public Releaser(string why) { _why = why; }
            public void Dispose()
            {
                lock (_gate)
                {
                    if (_done) return;
                    _done = true;
                    if (--_count == 0 && _release != null) { _release.Set(); _release = null; Dbg.Msg("power", "displays may sleep again (" + _why + " done)"); }
                }
            }
        }
    }
}
