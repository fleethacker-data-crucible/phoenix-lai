# Phoenix LAI launcher (run-phoenix-server.ps1 and run-phoenix-client.ps1 call it). Windows PowerShell 5.1 only.
# Two separate programs, never one process: the server (models + OpenAI API on localhost:1234/v1, tray) and the
# Phoenix Client (the chat window, an OpenAI /v1 client for any provider).
#   powershell.exe -NoProfile -STA -File src\run.ps1 -Role Client        # checks, compile, chat in a Chrome/Edge app window
#   powershell.exe -NoProfile -STA -File src\run.ps1 -Role Server -Model models\x.gguf -Context 8192
#   powershell.exe -NoProfile -STA -File src\run.ps1 -CheckOnly          # locality checks only
param(
    [switch]$CheckOnly,
    [switch]$Web,               # accepted for older shortcuts; the web chat is the only UI
    [string]$Model,
    [int]$Context = 0,   # 0 = auto: the largest context that fits the free VRAM / RAM
    [string]$Root,
    # Server = run-phoenix-server.ps1 (models + OpenAI API, tray); Client = run-phoenix-client.ps1 (the chat UI for any
    # OpenAI /v1 provider). There is no all-in-one mode: the client never runs inside the server.
    # VsCode = run-phoenix-vscode.ps1: the VS Code extension's own client (no window), started by the extension.
    [ValidateSet('Server', 'Client', 'VsCode')][string]$Role = 'Client'
)

$ErrorActionPreference = 'Stop'
# The project root is the folder above src\ (this script lives in src\).
if (-not $Root) { $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

if ($PSVersionTable.PSVersion.Major -ne 5) {
    Write-Host -ForegroundColor Red "Run with Windows PowerShell 5.1 (powershell.exe), not $($PSVersionTable.PSVersion)."
    exit 2
}

$src = Join-Path $Root 'src'

# Framework assemblies Phoenix may reference. Nothing else is ever passed to the compiler.
$References = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')

# The one exception to "no network / no processes": src\WebServer.cs may use HttpListener on 127.0.0.1 and start Edge.
$LoopbackFile = 'WebServer.cs'
# The Phoenix Client's providers (approved 2026-10-20): src\Providers.cs may call any OpenAI /v1 provider by URL (local,
# LAN or internet), through one guarded request function, Outgoing -> CheckTarget (rule 4c). Nothing else may.
$ClientFile = 'Providers.cs'
$ClientRequestOk = 'HttpWebRequest|WebRequest\.Create\(CheckTarget\(|WebRequest\.DefaultWebProxy'
$LoopbackAllowed = @('System.Net', 'Process.Start', 'http://')
$AlwaysBanned = @('HttpClient', 'WebClient', 'WebRequest', 'Socket', 'Dns.', 'TcpClient', 'TcpListener', 'UdpClient', 'https://')

$DllAllowlist = @('vulkan-1.dll', 'kernel32.dll')

$BannedPatterns = [ordered]@{
    'System.Net'             = 'network namespace'
    'HttpClient'             = 'network client'
    'WebClient'              = 'network client'
    'WebRequest'             = 'network client'
    'Socket'                 = 'network socket'
    'Process.Start'          = 'launching processes'
    'Assembly.Load'          = 'loading external assemblies'
    'NativeLibrary'          = 'loading native libraries'
    'LoadLibrary'            = 'loading native libraries'
    'http://'                = 'URL in source'
    'https://'               = 'URL in source'
}

function Test-Locality {
    $failures = New-Object System.Collections.Generic.List[string]
    $files = @(Get-ChildItem $src -Recurse -Filter *.cs)

    if ($files.Count -eq 0) { $failures.Add("No source files in $src") }

    # 1. No binaries shipped in the repo (models/, .git and the dev-only .claude/ folder are outside the app).
    $binaries = Get-ChildItem $Root -Recurse -File -Include *.dll, *.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(models|\.git|\.claude|dev)\\' }
    foreach ($b in $binaries) { $failures.Add("Binary in repo: $($b.FullName)") }

    foreach ($f in $files) {
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $where = "$($f.Name):$($i + 1)"

            # 2. Banned APIs / URLs (the loopback server file gets a narrow exception, checked below).
            $isLoopback = $f.Name -eq $LoopbackFile -or $f.Name -eq 'WebServer.OpenAi.cs' -or $f.Name -eq $ClientFile
            foreach ($p in $BannedPatterns.Keys) {
                if ($isLoopback -and $LoopbackAllowed -contains $p) { continue }
                if ($f.Name -eq $ClientFile -and $p -eq 'WebRequest' -and $line -match $ClientRequestOk) { continue }
                if ($line.IndexOf($p, [StringComparison]::Ordinal) -ge 0) {
                    $failures.Add("$where uses '$p' ($($BannedPatterns[$p]))")
                }
            }
            if ($isLoopback) {
                foreach ($p in $AlwaysBanned) {
                    # The client's one outgoing request type (HttpWebRequest, created only in Outgoing; checked in 4c)
                    if ($f.Name -eq $ClientFile -and $p -eq 'WebRequest' -and $line -match $ClientRequestOk) { continue }
                    if ($line.IndexOf($p, [StringComparison]::Ordinal) -ge 0) { $failures.Add("$where uses '$p' (not allowed even in the loopback server)") }
                }
                if ($f.Name -eq $ClientFile -and $line.IndexOf('Process.Start', [StringComparison]::Ordinal) -ge 0) { $failures.Add("$where starts a process (the client opens its window through WebServer.OpenWindow)") }
                # Every http:// written in the source must be the loopback address (provider URLs come from the user).
                foreach ($m in [regex]::Matches($line, 'http://([^"/:]*)')) {
                    if ($m.Groups[1].Value -ne '127.0.0.1' -and $m.Groups[1].Value -ne 'localhost') { $failures.Add("$where uses a non-loopback address 'http://$($m.Groups[1].Value)'") }
                }
            }

            # 2b. Hard rule: the model never runs a shell. No source may name one or start a process outside the guarded
            #     chat-window launch (ShellGuard.cs holds the refusal lists, WebServer.cs the one guarded Process.Start).
            if ($f.Name -ne 'ShellGuard.cs') {
                foreach ($m in [regex]::Matches($line, '(?i)powershell|pwsh|cmd\.exe|conhost|wscript|cscript|mshta|rundll32|Management\.Automation|ShellExecute(?!\s*=\s*false)|CreateProcess|WinExec|Interaction\.Shell')) {
                    $failures.Add("$where names '$($m.Value)' (the model may never run a shell)")
                }
            }

            if ($f.Name -eq 'WebServer.OpenAi.cs' -and $line -match 'Process\.|ShellGuard\.CheckLaunch') { $failures.Add("$where starts a process (the API server may not)") }

            # 3. P/Invoke targets must be on the allowlist.
            foreach ($m in [regex]::Matches($line, '(DllImport|LibraryImport)\s*\(\s*"([^"]+)"')) {
                $dll = $m.Groups[2].Value.ToLowerInvariant()
                if (-not $dll.EndsWith('.dll')) { $dll += '.dll' }
                if ($DllAllowlist -notcontains $dll) { $failures.Add("$where imports '$dll' (not on allowlist)") }
            }

            # 3b. The target must be a string literal so the allowlist check above can see it.
            if ($line -match '(DllImport|LibraryImport)\s*\(\s*[^"\s]') { $failures.Add("$where has a DllImport without a literal DLL name") }

            # 4. No assembly references smuggled in via source directives.
            if ($line -match '^\s*#r\s|extern\s+alias') { $failures.Add("$where adds an assembly reference") }
        }
    }
    # 4b. The only process launch must go through ShellGuard.CheckLaunch.
    $ws = Join-Path $src $LoopbackFile
    if (Test-Path $ws) {
        $t = [IO.File]::ReadAllText($ws)
        if ([regex]::Matches($t, 'Process\.Start\(').Count -ne 1 -or $t -notmatch 'ShellGuard\.CheckLaunch\([^)]*\);\s*return Process\.Start\(') {
            $failures.Add("$LoopbackFile must start exactly one process, right after ShellGuard.CheckLaunch")
        }
    }
    # 4c. The client's outgoing requests: exactly one creation, through CheckTarget (http/https only, no credentials in
    #     the URL), never following a redirect (a key cannot be forwarded to another host), TLS 1.2+.
    $cf = Join-Path $src $ClientFile
    if (-not (Test-Path $cf)) { $failures.Add("$ClientFile is missing") }
    else {
        $t = [IO.File]::ReadAllText($cf)
        if ([regex]::Matches($t, 'WebRequest\.Create\(').Count -ne 1 -or $t -notmatch 'WebRequest\.Create\(CheckTarget\(') { $failures.Add("$ClientFile must create requests only via WebRequest.Create(CheckTarget(...))") }
        foreach ($need in @('o.AllowAutoRedirect = false;', 'u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps', '!string.IsNullOrEmpty(u.UserInfo)', 'SecurityProtocolType.Tls12')) {
            if ($t.IndexOf($need, [StringComparison]::Ordinal) -lt 0) { $failures.Add("$ClientFile no longer contains '$need'") }
        }
    }
    # No other file may make an outgoing request (the server's API and chat port only listen).
    foreach ($f in $files) {
        if ($f.Name -eq $ClientFile) { continue }
        if ([IO.File]::ReadAllText($f.FullName) -match 'WebRequest\.Create|GetRequestStream') { $failures.Add("$($f.Name) makes an outgoing request (only $ClientFile may)") }
    }
    if (-not (Test-Path (Join-Path $src 'ShellGuard.cs'))) { $failures.Add("ShellGuard.cs is missing (the model may never run a shell)") }
    # 4b. The run guard (warning, 5-minute lockout that survives restarts) must be intact and wired in: the server will not
    #     start with it removed, unhooked or its lockout made non-persistent.
    $guardNeeds = [ordered]@{
        'ShellGuard.cs'   = @('public static string CheckRunRequest(string userText, out string level, out int seconds)', 'static DateTime _suspendedUntil = LoadLockout();',
                              'SaveLockout(_suspendedUntil);', 'static DateTime LoadLockout()', 'static string Sign(string body)', 'HMACSHA256', 'public const int SuspendMinutes = 5;')
        'WebServer.cs'    = @('ShellGuard.CheckRunRequest(lastUser, out guardLevel, out guardSeconds)')
        'Workspace.cs'    = @('ShellGuard.RunAdvice', 'ShellGuard.CheckWrite(f)', 'ShellGuard.Refusal')
    }
    foreach ($gf in $guardNeeds.Keys) {
        $gp = Join-Path $src $gf
        $gt = if (Test-Path $gp) { [IO.File]::ReadAllText($gp) } else { '' }
        foreach ($need in $guardNeeds[$gf]) {
            if ($gt.IndexOf($need, [StringComparison]::Ordinal) -lt 0) { $failures.Add("$gf no longer contains the run guard ('$need')") }
        }
    }
    # The guard reply must come before the model is ever called in Chat().
    $ws = [IO.File]::ReadAllText((Join-Path $src 'WebServer.cs'))
    $gAt = $ws.IndexOf('ShellGuard.CheckRunRequest(lastUser', [StringComparison]::Ordinal)
    $cAt = $ws.IndexOf('void Chat(Dictionary', [StringComparison]::Ordinal)
    $gEnd = $ws.IndexOf('.Generate(', $cAt, [StringComparison]::Ordinal)
    if ($gAt -lt 0 -or $cAt -lt 0 -or $gAt -lt $cAt -or ($gEnd -ge 0 -and $gAt -gt $gEnd)) { $failures.Add("WebServer.cs: the run guard must be checked in Chat() before the model runs") }
    # 5. The chat page may not pull anything from the network.
    $html = Join-Path $Root 'html'
    if (Test-Path $html) {
        foreach ($h in Get-ChildItem $html -Recurse -File -Include *.html, *.htm, *.js, *.css) {
            $text = [IO.File]::ReadAllText($h.FullName)
            foreach ($m in [regex]::Matches($text, '(https?:)?//[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}')) {
                $failures.Add("$($h.Name) references an external URL '$($m.Value)'")
            }
        }
    }
    return ,$failures
}

$failures = Test-Locality
if ($failures.Count -gt 0) {
    Write-Host "Locality checks FAILED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Locality checks passed."
if ($CheckOnly) { exit 0 }

$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
$References | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }

$paths = @(Get-ChildItem $src -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
Add-Type -Path $paths -CompilerParameters $cp

# Where Phoenix lives, for the VS Code extension (it starts src\run-phoenix-vscode.ps1 from here; the folder can move).
try {
    $appData = Join-Path $env:LOCALAPPDATA 'LocalLLMStudio'
    New-Item -ItemType Directory -Force $appData | Out-Null
    [IO.File]::WriteAllText((Join-Path $appData 'phoenix-root.txt'), $Root)
} catch { }

# No model loads at startup (unless -Model is given): the chat opens on "Select model" and one is picked from the drop-down.
$modelPath = if ($Model) { (Resolve-Path $Model).Path } else { $null }
switch ($Role) {
    'Server' { exit ([Phoenix.ServerHost]::Run($Root, $modelPath, $Context)) }
    'VsCode' { exit ([Phoenix.WebServer]::RunVsCode($Root)) }
    default  { exit ([Phoenix.WebServer]::RunClient($Root)) }
}
