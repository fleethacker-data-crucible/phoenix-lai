# Dev only: closes every running Phoenix server (started by PhoenixLAI_App.lnk, run.ps1 or a test) so a test can load a
# model without two in VRAM. Finds them by command line: run.ps1 directly, or the shortcut's -EncodedCommand (decoded),
# plus whoever holds the model lock. Prints what it closed; exit code 0 either way.
#   powershell.exe -NoProfile -File .claude\tools\close-phoenix.ps1
$self = $PID
$found = @{}
foreach ($p in Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'") {
    if ($p.ProcessId -eq $self -or -not $p.CommandLine) { continue }
    $cmd = $p.CommandLine
    if ($cmd -match '-EncodedCommand\s+(\S+)') {
        try { $cmd += ' ' + [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($Matches[1])) } catch { }
    }
    if ($cmd -match 'run\.ps1' -and $cmd -notmatch 'CheckOnly' -and $cmd -notmatch 'close-phoenix') { $found[$p.ProcessId] = 'server' }
}
$lock = Join-Path $env:LOCALAPPDATA 'LocalLLMStudio\model.lock'
try {
    $fs = [IO.File]::Open($lock, 'Open', 'Read', 'ReadWrite')
    $holder = (New-Object IO.StreamReader($fs)).ReadLine(); $fs.Dispose()
    if ($holder -match '^\d+$' -and [int]$holder -ne $self -and (Get-Process -Id ([int]$holder) -ErrorAction SilentlyContinue)) {
        try { [IO.File]::Open($lock, 'Open', 'ReadWrite', 'Read').Dispose() } catch { $found[[int]$holder] = 'model lock holder' }
    }
} catch { }
if ($found.Count -eq 0) { 'No Phoenix server running.'; exit 0 }
foreach ($id in $found.Keys) {
    $what = $found[$id]
    Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    "Closed Phoenix $what (PID $id)."
}
Start-Sleep -Milliseconds 800   # let Windows release the VRAM and the lock
