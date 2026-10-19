# Step 0 of the NVIDIA test: can this machine compile and run C# with /unsafe (pointers), the way Phoenix does?
# Takes a few seconds. No admin rights, no GPU, nothing installed or changed; one small temp file is created and deleted.
#   powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\0-unsafe-check.ps1
# The window stays open at the end until Enter is pressed (-NoPause skips that, for automated runs).
param([switch]$NoPause)

$ErrorActionPreference = 'Stop'
# Everything this window shows also goes to nvidia\logs\<script>-<date>-<time>.log
$logDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir ('{0}-{1}.log' -f [IO.Path]::GetFileNameWithoutExtension($MyInvocation.MyCommand.Name), (Get-Date -Format 'yyyyMMdd-HHmmss'))
try { Start-Transcript -Path $logFile | Out-Null } catch { }
function Close-Script([int]$code) {
    Write-Host ''; Write-Host "Log saved: $logFile"
    try { Stop-Transcript | Out-Null } catch { }
    if (-not $NoPause) { Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
    exit $code
}
trap { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; Close-Script 1 }
$failed = 0
function Pass([string]$s) { Write-Host "PASS  $s" -ForegroundColor Green }
function Fail([string]$s) { Write-Host "FAIL  $s" -ForegroundColor Red; $script:failed++ }

Write-Host "Phoenix /unsafe check on $env:COMPUTERNAME as $env:USERNAME"
Write-Host ''

# 1. Windows PowerShell 5.1 in full language mode (else nothing can be compiled)
if ($PSVersionTable.PSVersion.Major -eq 5) { Pass "Windows PowerShell $($PSVersionTable.PSVersion)" } else { Fail "PowerShell $($PSVersionTable.PSVersion): run this with powershell.exe (5.1)" }
$mode = $ExecutionContext.SessionState.LanguageMode
if ($mode -eq 'FullLanguage') { Pass "language mode $mode" } else { Fail "language mode $mode (C# cannot be compiled)" }

# 2. Plain C#, compiled in memory (what Data Noodler already proved)
try {
    Add-Type -TypeDefinition 'public static class PhxSafe { public static int Add(int a, int b) { return a + b; } }'
    if ([PhxSafe]::Add(40, 2) -eq 42) { Pass 'plain C# compiles and runs in memory' } else { Fail 'plain C# gave a wrong answer' }
} catch { Fail "plain C# compile: $($_.Exception.Message)" }

# 3. The same with /unsafe: every kind of pointer use Phoenix has
$code = @'
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

public static unsafe class PhxUnsafe
{
    [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();

    // pointer to a local
    public static int Pointer() { int x = 41; int* p = &x; *p += 1; return x; }

    // stackalloc (scratch memory on the stack)
    public static int Stack() { int* a = stackalloc int[64]; for (int i = 0; i < 64; i++) a[i] = i; return a[42]; }

    // fixed: pin a managed array and walk it with a pointer
    public static float Fixed() { float[] f = new float[100]; f[42] = 42f; fixed (float* p = f) return p[42]; }

    // unmanaged memory from Marshal (like Vulkan structures and strings)
    public static long Native()
    {
        IntPtr m = Marshal.AllocHGlobal(1 << 20);
        try { long* p = (long*)m; p[1000] = 42; return p[1000]; } finally { Marshal.FreeHGlobal(m); }
    }

    // a memory-mapped file read through a raw pointer (how Phoenix reads GGUF model files)
    public static int Mapped(string path)
    {
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 42, 5 });
        try
        {
            using (MemoryMappedFile mm = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read))
            using (MemoryMappedViewAccessor v = mm.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
            {
                byte* b = null;
                v.SafeMemoryMappedViewHandle.AcquirePointer(ref b);
                try { return b[3]; } finally { v.SafeMemoryMappedViewHandle.ReleasePointer(); }
            }
        }
        finally { File.Delete(path); }
    }

    // a native call from unsafe code (Phoenix calls vulkan-1.dll and kernel32.dll this way)
    public static bool Native2() { return GetCurrentProcessId() == (uint)System.Diagnostics.Process.GetCurrentProcess().Id; }
}
'@
$compiled = $false
try {
    $cp = New-Object System.CodeDom.Compiler.CompilerParameters
    $cp.GenerateInMemory = $true
    $cp.CompilerOptions = '/unsafe /optimize+'   # exactly what Phoenix uses
    'System.dll', 'System.Core.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
    Add-Type -TypeDefinition $code -CompilerParameters $cp
    Pass 'C# with /unsafe compiles in memory'
    $compiled = $true
} catch { Fail "C# with /unsafe does not compile: $($_.Exception.Message)" }

if ($compiled) {
    $tests = [ordered]@{
        'pointer to a variable'               = { [PhxUnsafe]::Pointer() -eq 42 }
        'stackalloc'                          = { [PhxUnsafe]::Stack() -eq 42 }
        'fixed (pinned array)'                = { [PhxUnsafe]::Fixed() -eq 42 }
        'unmanaged memory (Marshal)'          = { [PhxUnsafe]::Native() -eq 42 }
        'memory-mapped file via pointer'      = { [PhxUnsafe]::Mapped((Join-Path $env:TEMP "phoenix-unsafe-$PID.bin")) -eq 42 }
        'native call (kernel32.dll)'          = { [PhxUnsafe]::Native2() }
    }
    foreach ($t in $tests.Keys) {
        try { if (& $tests[$t]) { Pass $t } else { Fail "$t gave a wrong answer" } }
        catch { Fail "${t}: $($_.Exception.InnerException.Message) $($_.Exception.Message)" }
    }
}

Write-Host ''
if ($failed -eq 0) { Write-Host 'RESULT: /unsafe is not an issue on this machine.' -ForegroundColor Green }
else { Write-Host "RESULT: $failed check(s) failed: Phoenix cannot run here as it is. Note the FAIL lines." -ForegroundColor Red }
Close-Script $failed
