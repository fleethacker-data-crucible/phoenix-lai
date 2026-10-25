# Stops every running Phoenix LAI server on this machine, without knowing PIDs (TechStack "Lifecycle rules").
# The server normally stops itself when its app window closes, or with End session (/quit). This is the fallback from a
# shell: it reads the runtime files the server writes in logs\ (phoenix-server-<pid>.json), then looks for any PowerShell
# still running src\run.ps1 (also through PhoenixLAI_App.lnk). Each is first asked to stop cleanly (a reply, model load
# or indexing in progress finishes), then force-stopped after -WaitSeconds. Windows PowerShell 5.1.
#   powershell.exe -NoProfile -File src\Stop-Phoenix.ps1 [-Quiet] [-Force] [-WaitSeconds 15] [-IncludeLockHolder]
#     -Force              no clean stop: Stop-Process at once
#     -IncludeLockHolder  also stop whoever holds the model lock (a test or benchmark that loaded a model)
param([switch]$Quiet, [switch]$Force, [int]$WaitSeconds = 15, [switch]$IncludeLockHolder, [string]$Root)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Root) { $Root = Split-Path -Parent $here }
. (Join-Path $here 'Phoenix-Process.ps1')
$running = @(Get-PhoenixServers -Root $Root -IncludeLockHolder:$IncludeLockHolder)
if ($running.Count -eq 0) {
    if (-not $Quiet) { Write-Host 'No Phoenix server running.' -ForegroundColor DarkGray }
    exit 0
}
foreach ($s in $running) { Write-Host ('Stopping ' + (Format-PhoenixServer $s) + '...') -ForegroundColor Yellow }
$stopped = Stop-PhoenixServers -Root $Root -IncludeLockHolder:$IncludeLockHolder -WaitSeconds $WaitSeconds -Force:$Force
foreach ($id in $stopped) { Write-Host ('Stopped PID ' + $id + '.') -ForegroundColor Yellow }
exit 0
