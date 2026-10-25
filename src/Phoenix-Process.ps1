# Dot-sourced helpers that find and stop running Phoenix servers without knowing their PIDs (TechStack "Lifecycle rules").
# Used by src\run.ps1 (one instance), src\Stop-Phoenix.ps1, _Package_Build.ps1 and the dev test scripts. Windows PowerShell 5.1.
#   . (Join-Path $root 'src\Phoenix-Process.ps1')
#   Get-PhoenixServers -Root $root            # what is running (runtime files first, then a command-line scan)
#   Stop-PhoenixServers -Root $root           # asks each to stop cleanly, then force-stops what is left; returns the PIDs

# Runtime files: logs\phoenix-server-<pid>.json, written by the server on start and deleted on stop. A file whose process
# is gone (a crash, a forced stop) is deleted here on sight.
function Get-PhoenixRuntimeFiles {
    param([string]$Root)
    $out = @()
    $logs = Join-Path $Root 'logs'
    if (-not (Test-Path $logs)) { return $out }
    foreach ($f in @(Get-ChildItem -Path $logs -Filter 'phoenix-server-*.json' -ErrorAction SilentlyContinue)) {
        $info = $null
        try { $info = Get-Content -Path $f.FullName -Raw | ConvertFrom-Json } catch { $info = $null }
        $proc = $null
        if ($info -and $info.pid) { $proc = Get-Process -Id ([int]$info.pid) -ErrorAction SilentlyContinue }
        if ($proc -and $proc.ProcessName -match '^(powershell|pwsh)$') {
            $out += [pscustomobject]@{ Pid = [int]$info.pid; Port = $info.port; Url = $info.url; Started = $info.startedAt; Kind = 'server'; File = $f.FullName }
        } else {
            Remove-Item -Path $f.FullName -Force -ErrorAction SilentlyContinue
        }
    }
    return $out
}

# PowerShell processes running src\run.ps1 (directly, or through PhoenixLAI_App.lnk's -EncodedCommand), except
# -CheckOnly runs and this process. With -IncludeLockHolder also whoever holds the model lock (a test or benchmark that
# loaded a model in-process).
function Get-PhoenixServers {
    param([string]$Root, [switch]$IncludeLockHolder)
    $found = @{}
    foreach ($r in @(Get-PhoenixRuntimeFiles -Root $Root)) { $found[$r.Pid] = $r }
    $mine = $PID
    try {
        foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='powershell.exe' OR Name='pwsh.exe'" -ErrorAction SilentlyContinue)) {
            if ($p.ProcessId -eq $mine -or -not $p.CommandLine) { continue }
            $cmd = $p.CommandLine
            if ($cmd -match '-EncodedCommand\s+(\S+)') {
                try { $cmd += ' ' + [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($Matches[1])) } catch { }
            }
            if ($cmd -match '(^|[\\/\s''"])run\.ps1' -and $cmd -notmatch 'CheckOnly' -and $cmd -notmatch 'Stop-Phoenix|close-phoenix') {
                if (-not $found.ContainsKey([int]$p.ProcessId)) {
                    $found[[int]$p.ProcessId] = [pscustomobject]@{ Pid = [int]$p.ProcessId; Port = $null; Url = $null; Started = $p.CreationDate; Kind = 'server'; File = $null }
                }
            }
        }
    } catch { }
    if ($IncludeLockHolder) {
        $lock = Join-Path $env:LOCALAPPDATA 'LocalLLMStudio\model.lock'
        try {
            $fs = [IO.File]::Open($lock, 'Open', 'Read', 'ReadWrite')
            $holder = (New-Object IO.StreamReader($fs)).ReadLine(); $fs.Dispose()
            if ($holder -match '^\d+$' -and [int]$holder -ne $mine -and -not $found.ContainsKey([int]$holder) -and (Get-Process -Id ([int]$holder) -ErrorAction SilentlyContinue)) {
                $held = $false
                try { [IO.File]::Open($lock, 'Open', 'ReadWrite', 'Read').Dispose() } catch { $held = $true }
                if ($held) { $found[[int]$holder] = [pscustomobject]@{ Pid = [int]$holder; Port = $null; Url = $null; Started = $null; Kind = 'model lock holder'; File = $null } }
            }
        } catch { }
    }
    return @($found.Values | Sort-Object Pid)
}

function Format-PhoenixServer {
    param($Server)
    $s = 'Phoenix ' + $Server.Kind + ' PID ' + $Server.Pid
    if ($Server.Url) { $s += ' (' + $Server.Url + ')' }
    return $s
}

# Stops every running Phoenix server and returns the PIDs stopped. First a clean stop (the server's Local\PhoenixAI.Quit
# event: it finishes a reply, model load or indexing in progress, saves the project index and exits), then, after
# -WaitSeconds, Stop-Process -Force for whatever is left. Runtime files of stopped servers are deleted.
function Stop-PhoenixServers {
    param([string]$Root, [switch]$IncludeLockHolder, [int]$WaitSeconds = 15, [switch]$Force)
    $list = @(Get-PhoenixServers -Root $Root -IncludeLockHolder:$IncludeLockHolder)
    $stopped = New-Object System.Collections.Generic.List[int]
    if ($list.Count -eq 0) { return ,$stopped }
    if (-not $Force) {
        $ev = $null
        try {
            if ([Threading.EventWaitHandle]::TryOpenExisting('Local\PhoenixAI.Quit', [ref]$ev)) { [void]$ev.Set(); $ev.Dispose() }
        } catch { }
        $until = (Get-Date).AddSeconds($WaitSeconds)
        while ((Get-Date) -lt $until) {
            $left = @($list | Where-Object { $_.Kind -eq 'server' -and (Get-Process -Id $_.Pid -ErrorAction SilentlyContinue) })
            if ($left.Count -eq 0) { break }
            Start-Sleep -Milliseconds 300
        }
    }
    foreach ($s in $list) {
        $p = Get-Process -Id $s.Pid -ErrorAction SilentlyContinue
        if ($p) {
            try { Stop-Process -Id $s.Pid -Force -ErrorAction Stop } catch { Write-Warning ('Could not stop ' + (Format-PhoenixServer $s) + ': ' + $_.Exception.Message); continue }
        }
        $stopped.Add($s.Pid)
        if ($s.File) { Remove-Item -Path $s.File -Force -ErrorAction SilentlyContinue }
    }
    if ($stopped.Count -gt 0) { Start-Sleep -Milliseconds 800 }   # let Windows release the VRAM, the port and the model lock
    [void](Get-PhoenixRuntimeFiles -Root $Root)                   # drops runtime files of anything that just went away
    return ,$stopped
}
