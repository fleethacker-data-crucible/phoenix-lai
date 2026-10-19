# Step 4 of the NVIDIA test: packs everything to bring back into one zip in nvidia\results. Read-only apart from
# writing that zip. The session logs hold your chat text: look through them first if they must not leave the machine.
#   powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\3-collect.ps1
# The window stays open at the end until Enter is pressed (-NoPause skips that, for automated runs).
param([switch]$NoPause)

$ErrorActionPreference = 'Continue'
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
$stage = Join-Path $results 'collect'
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

foreach ($f in 'check.txt', 'probe.txt') {
    $p = Join-Path $results $f
    if (Test-Path $p) { Copy-Item $p $stage } else { Write-Host -ForegroundColor Yellow "$f is missing: run $(if ($f -eq 'check.txt') { '1-check.ps1' } else { '2-probe.ps1' }) first." }
}

# Every run of the 0-3 scripts (their window output, nvidia\logs)
$scriptLogs = Get-ChildItem $logDir -Filter *.log -ErrorAction SilentlyContinue | Where-Object { $_.FullName -ne $logFile }
if ($scriptLogs) {
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'script-logs') | Out-Null
    $scriptLogs | ForEach-Object { Copy-Item $_.FullName (Join-Path $stage 'script-logs') }
}

# The newest Phoenix session logs (model load lines, GPU description, timings and, with /debug on, slow GPU jobs)
Get-ChildItem (Join-Path $root 'logs') -Filter 'session-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5 |
    ForEach-Object { Copy-Item $_.FullName $stage }

# Phoenix's own settings (small text files; the HMAC-signed guard files are left out)
$app = Join-Path $env:LOCALAPPDATA 'LocalLLMStudio'
if (Test-Path $app) {
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'settings') | Out-Null
    Get-ChildItem $app -Filter *.txt -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $stage 'settings') }
}

# Windows' own graphics report: exact driver, display and GPU details (about 30 seconds)
Write-Host 'Running dxdiag (about 30 seconds)...'
$dx = Join-Path $stage 'dxdiag.txt'
$proc = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\dxdiag.exe') -ArgumentList "/whql:off /t `"$dx`"" -PassThru -WindowStyle Hidden
if (-not $proc.WaitForExit(120000)) { Write-Host -ForegroundColor Yellow 'dxdiag took too long; skipped.' }

# Recent display-driver resets and crashes from the System event log (event 4101 = GPU timeout recovered)
$ev = @()
try {
    $ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = (Get-Date).AddDays(-2) } -ErrorAction Stop |
        Where-Object { $_.Id -eq 4101 -or $_.ProviderName -match 'nvlddmkm|Display|dxgkrnl' } | Select-Object -First 50 |
        ForEach-Object { "{0}  {1}  {2}  {3}" -f $_.TimeCreated, $_.Id, $_.ProviderName, ($_.Message -replace '\s+', ' ') })
} catch { }
if ($ev.Count -eq 0) { $ev = @('no display-driver events in the last 2 days') }
$ev | Set-Content -Encoding UTF8 (Join-Path $stage 'gpu-events.txt')
$ev = @()
try {
    $ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddDays(-2); ProviderName = 'Application Error', '.NET Runtime' } -ErrorAction Stop |
        Where-Object { $_.Message -match 'powershell' } | Select-Object -First 20 |
        ForEach-Object { "{0}  {1}  {2}" -f $_.TimeCreated, $_.ProviderName, ($_.Message -replace '\s+', ' ') })
} catch { }
if ($ev.Count -eq 0) { $ev = @('no PowerShell crashes in the last 2 days') }
$ev | Set-Content -Encoding UTF8 (Join-Path $stage 'crashes.txt')

$zip = Join-Path $results ("phoenix-nvidia-{0}-{1}.zip" -f $env:COMPUTERNAME, (Get-Date -Format 'yyyyMMdd-HHmm'))
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
Remove-Item -Recurse -Force $stage
Write-Host -ForegroundColor Green "Saved $zip"
Write-Host 'Bring this one file back to the dev machine.'
Close-Script 0
