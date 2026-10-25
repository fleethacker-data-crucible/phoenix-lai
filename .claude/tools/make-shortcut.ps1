# DEV TOOL. Creates or repairs the Phoenix shortcuts in <root> (Phoenix_Client.lnk and Phoenix_LAI_Server.lnk).
#   powershell.exe -NoProfile -File .claude\tools\make-shortcut.ps1 [-Root <project root>] [-Name Phoenix_Client.lnk]
# The shortcut spec lives in src\PhoenixShortcuts.psm1 (Get-PhoenixShortcutSpec); Phoenix_LAI.bat and the
# run-phoenix-*.ps1 scripts use the same code on first run. The .lnk files are not tracked in git: they record the
# computer name and the user's SID.
param([string]$Root, [string[]]$Name)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path }
Import-Module (Join-Path $Root 'src\PhoenixShortcuts.psm1') -Force
foreach ($r in @(Sync-PhoenixShortcut -Root $Root -Name $Name)) {
    if ($r.State -eq 'unavailable') { throw "$($r.Name): $($r.Error)" }
    "$($r.State) $($r.Path)"
}
