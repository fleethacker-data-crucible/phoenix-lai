# Dev only: closes every running Phoenix server (started by PhoenixLAI_App.lnk, run.ps1 or a test) so a test can load a
# model without two in VRAM. Same finder as src\Stop-Phoenix.ps1 (runtime files in logs\, run.ps1 command lines, the
# shortcut's -EncodedCommand decoded) plus whoever holds the model lock; no clean-stop wait. Prints what it closed; exit 0.
#   powershell.exe -NoProfile -File .claude\tools\close-phoenix.ps1
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
. (Join-Path $root 'src\Phoenix-Process.ps1')
$found = @(Get-PhoenixServers -Root $root -IncludeLockHolder)
if ($found.Count -eq 0) { 'No Phoenix server running.'; exit 0 }
$stopped = Stop-PhoenixServers -Root $root -IncludeLockHolder -Force
foreach ($s in $found) { if ($stopped -contains $s.Pid) { "Closed Phoenix $($s.Kind) (PID $($s.Pid))." } }
exit 0
