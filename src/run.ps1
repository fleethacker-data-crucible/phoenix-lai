# Phoenix LAI launcher (entry point; PhoenixLAI_App.lnk runs this). Windows PowerShell 5.1 only.
#   powershell.exe -NoProfile -STA -File src\run.ps1                     # checks, compile, chat in an Edge app window
#   powershell.exe -NoProfile -STA -File src\run.ps1 -Model models\x.gguf -Context 8192
#   powershell.exe -NoProfile -STA -File src\run.ps1 -CheckOnly          # locality checks only
param(
    [switch]$CheckOnly,
    [switch]$Web,               # accepted for older shortcuts; the web chat is the only UI
    [string]$Model,
    [int]$Context = 0,   # 0 = auto: the largest context that fits the free VRAM / RAM
    [string]$Root
)

$ErrorActionPreference = 'Stop'
# The project root is the folder above src\ (this script lives in src\).
if (-not $Root) { $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path) }

if ($PSVersionTable.PSVersion.Major -ne 5) {
    Write-Host -ForegroundColor Red "Run with Windows PowerShell 5.1 (powershell.exe), not $($PSVersionTable.PSVersion)."
    exit 2
}

$src = Join-Path $Root 'src'

# Framework assemblies the studio may reference. Nothing else is ever passed to the compiler.
$References = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')

# The one exception to "no network / no processes": src\WebServer.cs may use HttpListener on 127.0.0.1 and start Edge.
$LoopbackFile = 'WebServer.cs'
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

    # 1. No binaries shipped in the repo (models/, .git and the dev-only .claude/ folder are outside the studio).
    $binaries = Get-ChildItem $Root -Recurse -File -Include *.dll, *.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(models|\.git|\.claude|dev)\\' }
    foreach ($b in $binaries) { $failures.Add("Binary in repo: $($b.FullName)") }

    foreach ($f in $files) {
        $lines = @(Get-Content $f.FullName)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $where = "$($f.Name):$($i + 1)"

            # 2. Banned APIs / URLs (the loopback server file gets a narrow exception, checked below).
            $isLoopback = $f.Name -eq $LoopbackFile -or $f.Name -eq 'WebServer.OpenAi.cs'
            foreach ($p in $BannedPatterns.Keys) {
                if ($isLoopback -and $LoopbackAllowed -contains $p) { continue }
                if ($line.IndexOf($p, [StringComparison]::Ordinal) -ge 0) {
                    $failures.Add("$where uses '$p' ($($BannedPatterns[$p]))")
                }
            }
            if ($isLoopback) {
                foreach ($p in $AlwaysBanned) {
                    if ($line.IndexOf($p, [StringComparison]::Ordinal) -ge 0) { $failures.Add("$where uses '$p' (not allowed even in the loopback server)") }
                }
                # Every http:// must be the loopback address.
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

# Default model: the last one loaded (models\last-model.txt), else an 8B, else the largest .gguf.
if (-not $Model) {
    $modelsDir = Join-Path $Root 'models'
    $last = Join-Path $modelsDir 'last-model.txt'
    if (Test-Path $last) {
        $name = (Get-Content $last -TotalCount 1).Trim()
        if ($name -and (Test-Path (Join-Path $modelsDir $name))) { $Model = Join-Path $modelsDir $name }
    }
    if (-not $Model) {
        $all = @(Get-ChildItem $modelsDir -Filter *.gguf -ErrorAction SilentlyContinue | Sort-Object Length -Descending)
        $pick = $all | Where-Object { $_.Name -match '8B' } | Select-Object -First 1
        if (-not $pick) { $pick = $all | Select-Object -First 1 }
        if ($pick) { $Model = $pick.FullName }
    }
}
# No model is fine: the chat opens and one can be picked from Settings.
$modelPath = if ($Model) { (Resolve-Path $Model).Path } else { $null }
exit ([Phoenix.WebServer]::Run($Root, $modelPath, $Context))
