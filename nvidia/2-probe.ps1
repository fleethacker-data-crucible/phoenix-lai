# Step 2 of the NVIDIA test: GPU details, GPU-vs-CPU kernel tests, accuracy against llama.cpp and speed, each in
# normal and safe mode. Uses the GPU for several minutes; Phoenix must be closed. Changes nothing on the machine.
#   powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\2-probe.ps1
#   ... -Big     also tests the 12B model (needs about 9 GB of free VRAM)
#   The window stays open at the end until Enter is pressed (-NoPause skips that, for automated runs).
param([switch]$Big, [switch]$NoPause)

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
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$results = Join-Path $here 'results'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$report = Join-Path $results 'probe.txt'

if ($PSVersionTable.PSVersion.Major -ne 5) { Write-Host -ForegroundColor Red "Run with Windows PowerShell 5.1 (powershell.exe)."; Close-Script 2 }

# Phoenix holds the GPU while it runs: both on one card would skew every number (and can reset the driver).
$busy = Get-NetTCPConnection -State Listen -LocalPort 8765, 1234 -ErrorAction SilentlyContinue
if ($busy) { Write-Host -ForegroundColor Red "Phoenix is running (port 8765/1234 is in use). Close its window, wait 10 seconds, then run this again."; Close-Script 3 }

# Everything the probe needs lives in nvidia\ (the dev-only .claude folder is not copied to production):
# nvidia\tests (GPU-vs-CPU kernel tests, accuracy gate) and nvidia\ref (llama.cpp reference log-probabilities).
$tests = Join-Path $here 'tests'
$srcFiles = @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName })
$files = $srcFiles + @(Join-Path $here 'Probe.cs')
$refs = $null
if (Test-Path (Join-Path $tests 'GemmaGate.cs')) {
    $files += @(Get-ChildItem $tests -Filter *.cs | ForEach-Object { $_.FullName })
    $refs = Join-Path $here 'ref'
} else {
    Write-Host -ForegroundColor Yellow "nvidia\tests is missing: kernel and accuracy tests are skipped (copy the whole nvidia folder)."
}

Write-Host "Compiling $($files.Count) files..."
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
try { Add-Type -Path $files -CompilerParameters $cp }
catch { "COMPILE FAILED: $($_.Exception.Message)" | Set-Content -Encoding UTF8 $report; Write-Host -ForegroundColor Red "Compile failed; see $report"; Close-Script 4 }

$models = @('gemma-4-E2b_q4_0-it.gguf', 'gemma-4-E4b_q4_0-it.gguf')
if ($Big) { $models += 'gemma-4-12b-q4_0-it-qat.gguf' }
$paths = @($models | ForEach-Object { Join-Path $root "models\$_" } | Where-Object { Test-Path $_ })
if ($paths.Count -eq 0) { Write-Host -ForegroundColor Yellow "No Gemma E2B/E4B model in models\: only the device and kernel tests run." }

Write-Host "Probing (takes several minutes; the screen may flicker once if the driver resets)..."
$fail = [Phoenix.Nvidia.Probe]::Run($root, $report, [string[]]$paths, $refs)
# the full report next to this run's window log (the window shows a shorter version)
Copy-Item $report ($logFile -replace '\.log$', '-report.txt') -ErrorAction SilentlyContinue
Write-Host ""
if ($fail -eq 0) { Write-Host -ForegroundColor Green "All checks passed. Report: $report" }
else { Write-Host -ForegroundColor Red "$fail check(s) failed. Report: $report" }
Close-Script $fail
