# Phoenix LAI server: runs the local models and serves them as an OpenAI-compatible API on http://localhost:1234/v1.
# No chat window: it sits in the notification area (right-click: Start, Stop, Restart, Console, Dashboard, Exit).
# Windows PowerShell 5.1 only (same checks and in-memory compile as run.ps1).
#   powershell.exe -NoProfile -STA -WindowStyle Hidden -File src\run-phoenix-server.ps1
#   powershell.exe -NoProfile -STA -File src\run-phoenix-server.ps1 -Model models\gemma-4-e2b_q4_0-it.gguf -Context 8192
param(
    [string]$Model,
    [int]$Context = 0
)
# First run: create or repair Phoenix_Client.lnk and Phoenix_LAI_Server.lnk in the project root (made on this
# computer, never tracked). Best effort: a missing or blocked WScript.Shell never stops the launch.
try { Import-Module (Join-Path $PSScriptRoot 'PhoenixShortcuts.psm1') -Force; $null = Sync-PhoenixShortcut -Root (Split-Path -Parent $PSScriptRoot) } catch { $null = $_ }
$args2 = @{ Role = 'Server'; Context = $Context }
if ($Model) { $args2.Model = $Model }
& (Join-Path $PSScriptRoot 'run.ps1') @args2
exit $LASTEXITCODE
