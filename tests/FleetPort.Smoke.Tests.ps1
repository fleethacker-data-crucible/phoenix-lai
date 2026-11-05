# Pester 5 smoke tests for the FleetHacker port block (edge-ops PORTS.md). Run: Invoke-Pester -Path .\tests -Output Detailed
# Runs on Windows PowerShell 5.1 and pwsh. The launcher tests run src\run.ps1's server port check in a child PowerShell
# against a fake Phoenix server on scratch ports 18780-18781 (temporary registry); they never compile or start Phoenix.
BeforeAll {
    $script:Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $script:Src = Join-Path $script:Root 'src'
    $script:Exe = (Get-Process -Id $PID).Path
    Import-Module (Join-Path $script:Src 'FleetPort.psm1') -Force
    function Compare-FleetVersionText($a, $b) { & (Get-Module FleetPort) { param($x, $y) Compare-FleetVersion $x $y } $a $b }
    function Read-Src([string]$Name) { [IO.File]::ReadAllText((Join-Path $script:Src $Name)) }

    # A fake Phoenix server: answers /__fleethacker/id like WebServer.cs (tool, version, pid; no path).
    function Start-FakePhoenix([int]$Port, [string]$Version) {
        $ps = [PowerShell]::Create()
        [void]$ps.AddScript({
            param($port, $version)
            $l = New-Object System.Net.HttpListener
            $l.Prefixes.Add("http://localhost:$port/")
            $l.Start()
            try {
                while ($l.IsListening) {
                    $ctx = $l.GetContext()
                    if ($ctx.Request.Url.AbsolutePath -eq '/__fleethacker/shutdown') { $ctx.Response.StatusCode = 202; $ctx.Response.Close(); break }
                    $body = '{"tool":"phoenix-lai-server","version":"' + $version + '","pid":' + $PID + '}'
                    $b = [Text.Encoding]::UTF8.GetBytes($body)
                    $ctx.Response.ContentType = 'application/json'
                    $ctx.Response.OutputStream.Write($b, 0, $b.Length)
                    $ctx.Response.Close()
                }
            } catch { } finally { $l.Close() }
        }).AddArgument($Port).AddArgument($Version)
        $h = $ps.BeginInvoke()
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ((Test-FleetPortFree -Port $Port) -and $sw.ElapsedMilliseconds -lt 5000) { Start-Sleep -Milliseconds 50 }
        return @{ PS = $ps; Handle = $h; Port = $Port }
    }
    function Stop-FakePhoenix($f) {
        if (-not $f) { return }
        try { [void](Invoke-WebRequest -UseBasicParsing -Method Post -TimeoutSec 5 -Uri "http://localhost:$($f.Port)/__fleethacker/shutdown") } catch { }
        [void]$f.Handle.AsyncWaitHandle.WaitOne(5000)
        $f.PS.Dispose()
    }

    # A temporary root: src\FleetPort.psm1, a scratch registry, __version.info, and a script that runs only the
    # "if ($Role -eq 'Server') { ... }" block of src\run.ps1 and then prints CONTINUED (= would compile and start).
    $script:Tmp = Join-Path ([IO.Path]::GetTempPath()) ('phoenix-fleet-' + [guid]::NewGuid().ToString('N'))
    $tsrc = Join-Path $script:Tmp 'src'
    New-Item -ItemType Directory -Force $tsrc | Out-Null
    Copy-Item (Join-Path $script:Src 'FleetPort.psm1') $tsrc
    Set-Content -LiteralPath (Join-Path $tsrc 'FleetPorts.psd1') -Encoding Ascii -Value "@{ 'phoenix-lai-server' = @{ Start = 18780; Size = 2; Policy = 'exclusive' } }"
    Set-Content -LiteralPath (Join-Path $script:Tmp '__version.info') -Encoding Ascii -Value '2026_1022_160032'
    $ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $script:Src 'run.ps1'), [ref]$null, [ref]$null)
    $block = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.IfStatementAst] -and $n.Extent.Text -like "if (`$Role -eq 'Server')*" }, $false) | Select-Object -First 1
    $script:ServerBlock = if ($block) { $block.Extent.Text } else { '' }
    $script:Probe = Join-Path $script:Tmp 'probe.ps1'
    Set-Content -LiteralPath $script:Probe -Encoding Ascii -Value ("`$ErrorActionPreference = 'Stop'`n`$Role = 'Server'`n`$Root = '$($script:Tmp)'`n`$src = Join-Path `$Root 'src'`n" + $script:ServerBlock + "`nWrite-Output CONTINUED`nexit 0")
    function Invoke-Probe {
        $env:PHOENIX_FLEET_NO_MESSAGEBOX = '1'
        $out = & $script:Exe -NoProfile -ExecutionPolicy Bypass -File $script:Probe 2>&1 | Out-String
        return @{ Out = $out; Code = $LASTEXITCODE }
    }
}

AfterAll { Remove-Item Env:\PHOENIX_FLEET_NO_MESSAGEBOX -ErrorAction SilentlyContinue; if ($script:Tmp) { Remove-Item -LiteralPath $script:Tmp -Recurse -Force -ErrorAction SilentlyContinue } }

Describe 'Port registry and blocks' {
    It 'vendors the edge-ops registry lines for Phoenix' {
        $reg = Import-PowerShellDataFile (Join-Path $script:Src 'FleetPorts.psd1')
        $reg['phoenix-lai-server'].Start | Should -Be 8780; $reg['phoenix-lai-server'].Size | Should -Be 10; $reg['phoenix-lai-server'].Policy | Should -Be 'exclusive'
        $reg['phoenix-lai-client'].Start | Should -Be 8790; $reg['phoenix-lai-client'].Size | Should -Be 9; $reg['phoenix-lai-client'].Policy | Should -Be 'multi'
        $reg['phoenix-lai-api'].Start | Should -Be 1234; $reg['phoenix-lai-api'].Policy | Should -Be 'exclusive'
        $reg['phoenix-lai-dev'].Start | Should -Be 8799; $reg['phoenix-lai-dev'].Policy | Should -Be 'restart'
    }
    It 'WebServer.cs scans only inside the blocks (server 8780-8789, client 8790-8798)' {
        Read-Src 'WebServer.cs' | Should -Match ([regex]::Escape('int from = client ? 8790 : 8780, to = client ? 8799 : 8790;'))
        Read-Src 'WebServer.cs' | Should -Not -Match '\b8765\b'
    }
    It 'the API stays on 1234' { Read-Src 'WebServer.OpenAi.cs' | Should -Match 'public const int ApiPort = 1234;' }
}

Describe 'WebServer.cs /__fleethacker endpoints' {
    BeforeAll { $script:ws = Read-Src 'WebServer.cs' }
    It 'adds a localhost prefix next to 127.0.0.1 (http.sys matches on Host)' {
        $script:ws | Should -Match ([regex]::Escape('_listener.Prefixes.Add("http://localhost:" + port + "/");'))
        $script:ws | Should -Match ([regex]::Escape('_listener.Prefixes.Add("http://127.0.0.1:" + port + "/");'))
    }
    It 'routes /__fleethacker/ before the 127.0.0.1-only Host check, and nothing else gets localhost' {
        $i = $script:ws.IndexOf('if (req.Url.AbsolutePath.StartsWith("/__fleethacker/")) { FleetHacker(req, res, host); return; }')
        $j = $script:ws.IndexOf('if (host != "127.0.0.1:" + Port) { Send(res, 403')
        $i | Should -BeGreaterThan 0; $j | Should -BeGreaterThan $i
    }
    It 'checks Host, Origin and a loopback caller, and reports tool, version and pid (no path)' {
        $script:ws | Should -Match 'host != a && host != b'
        $script:ws | Should -Match 'origin != null && origin != "http://127\.0\.0\.1:" \+ Port && origin != "http://localhost:" \+ Port'
        $script:ws | Should -Match '!req\.IsLocal'
        $script:ws | Should -Match 'IsClient \? "phoenix-lai-client" : "phoenix-lai-server"'
        $m = $script:ws.Substring($script:ws.IndexOf('void FleetHacker('), $script:ws.IndexOf('public static string FleetVersion(') - $script:ws.IndexOf('void FleetHacker('))
        $m | Should -Match 'pid'
        $m | Should -Not -Match 'path\\"'
    }
    It 'shutdown needs POST and X-FleetHacker-Shutdown: 1, and every run loop honours it' {
        $script:ws | Should -Match 'req\.HttpMethod != "POST" \|\| req\.Headers\["X-FleetHacker-Shutdown"\] != "1"'
        ([regex]::Matches($script:ws, 'ShutdownRequested')).Count | Should -BeGreaterOrEqual 6
        Read-Src 'ServerHost.cs' | Should -Match 'cur\.ShutdownRequested\) Application\.ExitThread\(\);'
    }
    It 'the C# version text matches the launcher (__version.info underscores become dots)' {
        $script:ws | Should -Match ([regex]::Escape("Replace('_', '.')"))
        Read-Src 'run.ps1' | Should -Match ([regex]::Escape("-replace '_', '.'"))
        (Compare-FleetVersionText '2026.1022.160032' '2026.1021.235959') | Should -Be 1
    }
    It 'still passes run.ps1''s locality checks' {
        $text = Read-Src 'run.ps1'
        $pre = $text.Substring(0, $text.IndexOf('$failures = Test-Locality'))
        $pre = $pre -replace '(?s)if \(\$PSVersionTable\.PSVersion\.Major -ne 5\) \{.*?\n\}', '' -replace '(?s)^.*?\nparam\(.*?\n\)\r?\n', ''
        $Root = $script:Root; $src = $script:Src
        . ([scriptblock]::Create(($pre -replace '\$src = Join-Path \$Root .src.', '')))
        @(Test-Locality) | Should -BeNullOrEmpty
    }
}

Describe 'run.ps1 -Role Server port check (exclusive)' {
    It 'found the server block in run.ps1, before the compile' {
        $script:ServerBlock | Should -Match 'Resolve-FleetPort -Tool ''phoenix-lai-server'''
        $t = Read-Src 'run.ps1'
        $t.IndexOf("if (`$Role -eq 'Server') {") | Should -BeLessThan $t.IndexOf('Add-Type -Path $paths')
    }
    It 'nothing running: goes on to compile and start' {
        $r = Invoke-Probe
        $r.Out | Should -Match 'CONTINUED'; $r.Code | Should -Be 0
    }
    It 'same version running: says so and exits 0 without compiling' {
        $f = Start-FakePhoenix 18780 '2026.1022.160032'
        try { $r = Invoke-Probe } finally { Stop-FakePhoenix $f }
        $r.Out | Should -Match 'already running \(http://127\.0\.0\.1:18780/dashboard\.html'
        $r.Out | Should -Not -Match 'CONTINUED'; $r.Code | Should -Be 0
    }
    It 'newer version running (stale launcher): reuses it, exits 0' {
        $f = Start-FakePhoenix 18780 '2026.1030.000000'
        try { $r = Invoke-Probe } finally { Stop-FakePhoenix $f }
        $r.Out | Should -Match 'already running'; $r.Out | Should -Not -Match 'CONTINUED'; $r.Code | Should -Be 0
    }
    It 'older version running: refuses with a clear error, exits 1, never stops it' {
        $f = Start-FakePhoenix 18780 '2026.1021.120000'
        try {
            $r = Invoke-Probe
            (Test-FleetSelf -Port 18780 -Tool 'phoenix-lai-server') | Should -Not -BeNullOrEmpty
        } finally { Stop-FakePhoenix $f }
        $r.Out | Should -Match 'not stopped automatically'; $r.Out | Should -Not -Match 'CONTINUED'; $r.Code | Should -Be 1
    }
}
