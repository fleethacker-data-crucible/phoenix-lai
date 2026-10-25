# Dev test of the lifecycle rules (TechStack "Lifecycle rules", src\Presence.cs + src\WebServer.Lifecycle.cs): the stop
# rule with simulated time (no 20 s sleeps), then the routes on a private port (8797) with no window, so a running Phoenix
# app is not touched. No model, no GPU. Exit code = number of failures.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\lifecycle-test.ps1
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5) { Write-Host -ForegroundColor Red "Run with Windows PowerShell 5.1 (powershell.exe)."; exit 2 }
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp

$script:fail = 0
function Check([string]$what, $got, $want) {
    if ($got -eq $want) { "  ok    $what" } else { $script:fail++; Write-Host "  FAIL  $what (got '$got', want '$want')" -ForegroundColor Red }
}

'presence rule (simulated time)'
$t0 = [DateTime]::UtcNow
function At([double]$s) { return $t0.AddSeconds($s) }
$p = New-Object Phoenix.Presence((At 0))
Check 'nothing checked in yet: not gone' $p.Gone((At 30)) $false
Check 'no window ever checked in: gone after 60 s' $p.Gone((At 61)) $true

$p = New-Object Phoenix.Presence((At 0))
$p.Beat('a1', (At 1))
Check 'window open after a heartbeat' $p.OpenWindows((At 2)) 1
Check '19 s after the last heartbeat: still open' $p.Gone((At 20)) $false
Check '20 s after the last heartbeat: gone' $p.Gone((At 21)) $true

$p = New-Object Phoenix.Presence((At 0))
$p.Beat('a1', (At 1)); $p.Leave('a1', (At 2))
Check '4 s after leaving: not gone yet' $p.Gone((At 6)) $false
Check '5 s after leaving with no heartbeat: gone' $p.Gone((At 7)) $true

# A reload: the old page says "leaving", the new page (new id) checks in at once.
$p = New-Object Phoenix.Presence((At 0))
$p.Beat('old', (At 1)); $p.Leave('old', (At 10)); $p.Beat('new', (At 10.5))
Check 'reload: not gone 5 s after the old page left' $p.Gone((At 16)) $false
Check 'reload: one window open' $p.OpenWindows((At 16)) 1
# A heartbeat after "leaving" from the same page (bfcache restore, pageshow) cancels it.
$p = New-Object Phoenix.Presence((At 0))
$p.Beat('a1', (At 1)); $p.Leave('a1', (At 2)); $p.Beat('a1', (At 3))
Check 'heartbeat after leaving cancels it' $p.Gone((At 8)) $false

# Two windows: closing one keeps the server up.
$p = New-Object Phoenix.Presence((At 0))
$p.Beat('a1', (At 1)); $p.Beat('a2', (At 1)); $p.Leave('a1', (At 5))
for ($s = 6; $s -le 40; $s += 5) { $p.Beat('a2', (At $s)) }
Check 'two windows, one closed: not gone' $p.Gone((At 42)) $false
$p.Leave('a2', (At 42))
Check 'two windows, both closed: gone' $p.Gone((At 48)) $true

# The VS Code chat (vs- ids) does not keep the server up.
$p = New-Object Phoenix.Presence((At 0))
$p.Beat('a1', (At 1)); $p.Leave('a1', (At 2))
for ($s = 2; $s -le 20; $s += 5) { $p.Beat('vs-x', (At $s)) }
Check 'only the VS Code chat left: gone' $p.Gone((At 20)) $true

# A minimized (hidden) window polls rarely: it gets 75 s, not 20 s.
$p = New-Object Phoenix.Presence((At 0))
$p.Beat('a1', (At 1), $true)
Check 'hidden window 60 s after its heartbeat: open' $p.Gone((At 61)) $false
Check 'hidden window 75 s after its heartbeat: gone' $p.Gone((At 77)) $true
$p = New-Object Phoenix.Presence((At 0))
$p.Beat('a1', (At 1), $true); $p.Beat('a1', (At 2))   # the status poll does not say: stays hidden
Check 'status poll keeps the hidden state' $p.Gone((At 40)) $false

Check 'bad page ids are ignored' ([Phoenix.Presence]::ValidId('a<b')) $false
Check 'long page ids are ignored' ([Phoenix.Presence]::ValidId(('x' * 41))) $false

'stop decision (never mid-write)'
Check 'window open, nothing running: run' ([Phoenix.Presence]::Decide($false, $true, $false, $null)) 'run'
Check 'window gone, nothing running: stop' ([Phoenix.Presence]::Decide($false, $true, $true, $null)) 'stop'
Check 'window gone, indexing: wait' ([Phoenix.Presence]::Decide($false, $true, $true, 'docs indexing')) 'wait'
Check 'window gone, auto-stop off: run' ([Phoenix.Presence]::Decide($false, $false, $true, $null)) 'run'
Check 'end session, reply running: wait' ([Phoenix.Presence]::Decide($true, $false, $false, 'the reply in progress')) 'wait'
Check 'end session, nothing running: stop' ([Phoenix.Presence]::Decide($true, $false, $false, $null)) 'stop'

'off switch'
$old = $env:PHOENIX_AUTO_STOP
$env:PHOENIX_AUTO_STOP = '0'
Check 'PHOENIX_AUTO_STOP=0 turns auto-stop off' ([Phoenix.WebServer]::AutoStopSetting()) $false
$env:PHOENIX_AUTO_STOP = $old

'routes (private port 8797)'
$logs = Join-Path $env:TEMP 'phx-lifecycle-logs'
if (Test-Path $logs) { Remove-Item -Recurse -Force $logs }
$port = 8797
$srv = New-Object Phoenix.WebServer((Join-Path $root 'html'), (Join-Path $root 'models'), $logs, $port)
$loop = [Delegate]::CreateDelegate([Threading.ThreadStart], $srv, 'Loop')
$th = New-Object Threading.Thread($loop); $th.IsBackground = $true; $th.Start()
$base = "http://127.0.0.1:$port"
function Status([string]$method, [string]$path, [string]$body, [hashtable]$headers) {
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Method $method -Uri ($base + $path) -ContentType 'application/json' -Body $body -Headers $headers -TimeoutSec 10
        return [int]$r.StatusCode
    } catch [System.Net.WebException] {
        if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } else { throw }
    }
}
try {
    Check 'POST /api/heartbeat' (Status 'POST' '/api/heartbeat?page=t1' '{"leaving":false}' @{}) 200
    Check 'GET /api/heartbeat is refused' (Status 'GET' '/api/heartbeat?page=t1' $null @{}) 400
    Check 'heartbeat from another site is refused' (Status 'POST' '/api/heartbeat?page=t1' '{"leaving":true}' @{ Origin = 'http://evil.example' }) 403
    Check 'end session from another site is refused' (Status 'POST' '/api/quit' '{}' @{ Origin = 'http://evil.example' }) 403
    Check 'old /api/bye beacon still accepted' (Status 'POST' '/api/bye?page=t1' '' @{}) 200
    Check 'stop not requested yet' $srv.StopRequested $false
    Check 'POST /api/quit' (Status 'POST' '/api/quit' '{}' @{ Origin = $base }) 200
    Check 'end session sets the stop request' $srv.StopRequested $true
} finally { $srv.Dispose() }

'stop script finds runtime files and removes stale ones'
. (Join-Path $root 'src\Phoenix-Process.ps1')
$fake = Join-Path $env:TEMP 'phx-lifecycle-root'
New-Item -ItemType Directory -Force (Join-Path $fake 'logs') | Out-Null
$dead = 999999
while (Get-Process -Id $dead -ErrorAction SilentlyContinue) { $dead-- }
$stale = Join-Path $fake ('logs\phoenix-server-' + $dead + '.json')
[IO.File]::WriteAllText($stale, '{"pid":' + $dead + ',"port":8765,"url":"x","startedAt":"x"}')
$live = Join-Path $fake ('logs\phoenix-server-' + $PID + '.json')
[IO.File]::WriteAllText($live, '{"pid":' + $PID + ',"port":8765,"url":"x","startedAt":"x"}')
$rt = @(Get-PhoenixRuntimeFiles -Root $fake)
Check 'live runtime file listed' @($rt | Where-Object { $_.Pid -eq $PID }).Count 1
Check 'stale runtime file deleted' (Test-Path $stale) $false
Remove-Item -Recurse -Force $fake

if ($script:fail -eq 0) { 'lifecycle: all checks passed' } else { Write-Host "lifecycle: $($script:fail) check(s) FAILED" -ForegroundColor Red }
exit $script:fail
