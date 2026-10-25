# Phoenix LAI first run (Phoenix_LAI.bat): creates or repairs the two shortcuts in the project root, then starts the
# server and the client through them (no console window). Windows PowerShell 5.1, standard user, no admin.
#   Phoenix_LAI.bat              shortcuts + start the server, then the client
#   Phoenix_LAI.bat -NoStart     shortcuts only
#   Phoenix_LAI.bat -Server      shortcuts + server only      (-Client: shortcuts + client only)
# The shortcuts are made on this computer and are not tracked in git (see .gitignore). If Windows Script Host (COM)
# is not available, the shortcuts are skipped and Phoenix still starts.
param([switch]$NoStart, [switch]$Server, [switch]$Client)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'PhoenixShortcuts.psm1') -Force

$results = @(Sync-PhoenixShortcut -Root $root)
foreach ($r in $results) {
    if ($r.State -eq 'unavailable') { Write-Host "Shortcut $($r.Name) not made: $($r.Error)" -ForegroundColor Yellow }
    else { Write-Host "Shortcut $($r.Name): $($r.State)" }
}
if ($NoStart) { exit 0 }

$want = @()
if ($Server -or -not $Client) { $want += 'Phoenix_LAI_Server.lnk' }
if ($Client -or -not $Server) { $want += 'Phoenix_Client.lnk' }
$scripts = @{ 'Phoenix_LAI_Server.lnk' = 'run-phoenix-server.ps1'; 'Phoenix_Client.lnk' = 'run-phoenix-client.ps1' }
foreach ($name in $want) {
    $r = $results | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if ($r -and $r.State -ne 'unavailable' -and (Test-Path -LiteralPath $r.Path)) {
        # Through the shortcut (same as a double-click): conhost --headless, no window.
        Start-Process -FilePath $r.Path
    } else {
        # No shortcut: start the script directly with a hidden window.
        $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $file = Join-Path $PSScriptRoot $scripts[$name]
        Start-Process -FilePath $ps -ArgumentList @('-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', ('"' + $file + '"')) -WorkingDirectory $root -WindowStyle Hidden
    }
    Write-Host "Started $name"
    if ($name -eq 'Phoenix_LAI_Server.lnk' -and $want.Count -gt 1) { Start-Sleep -Seconds 2 }
}
exit 0
