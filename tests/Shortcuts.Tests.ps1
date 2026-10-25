# Pester 5 tests for src\PhoenixShortcuts.psm1: the shortcut spec and the create-or-repair logic.
# The WScript.Shell COM call is replaced by a fake shortcut object, so these run on any OS and write no .lnk file.
# Run: Invoke-Pester -Path .\tests\Shortcuts.Tests.ps1 -Output Detailed
BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    Import-Module (Join-Path $script:RepoRoot 'src\PhoenixShortcuts.psm1') -Force

    # What the original tracked shortcuts ran, recovered from Phoenix_Client.lnk and Phoenix_LAI_Server.lnk.
    $script:Prefix = '--headless %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand '
    $script:Original = @{
        'Phoenix_Client.lnk'     = 'Add-Type ''public class S{[System.Runtime.InteropServices.DllImport("kernel32")]public static extern void GetStartupInfoW(byte[] b);}'';$b=New-Object byte[] 104;[S]::GetStartupInfoW($b);$t=[Runtime.InteropServices.Marshal]::PtrToStringUni([IntPtr][BitConverter]::ToInt64($b,24));& (Join-Path (Split-Path $t) ''src\run-phoenix-client.ps1'')'
        'Phoenix_LAI_Server.lnk' = 'Add-Type ''public class S{[System.Runtime.InteropServices.DllImport("kernel32")]public static extern void GetStartupInfoW(byte[] b);}'';$b=New-Object byte[] 104;[S]::GetStartupInfoW($b);$t=[Runtime.InteropServices.Marshal]::PtrToStringUni([IntPtr][BitConverter]::ToInt64($b,24));& (Join-Path (Split-Path $t) ''src\run-phoenix-server.ps1'')'
    }

    function New-FakeShortcut {
        param($Spec, [switch]$Blank)
        $o = [pscustomobject]@{ TargetPath = ''; Arguments = ''; WorkingDirectory = ''; Description = ''; IconLocation = ''; WindowStyle = 1; Saves = 0 }
        if (-not $Blank) { foreach ($f in 'TargetPath', 'Arguments', 'WorkingDirectory', 'Description', 'IconLocation', 'WindowStyle') { $o.$f = $Spec.$f } }
        $o | Add-Member -MemberType ScriptMethod -Name Save -Value { $this.Saves++ }
        return $o
    }
    function New-TempRoot { $r = Join-Path ([IO.Path]::GetTempPath()) ('phx-lnk-' + [guid]::NewGuid().ToString('N')); [void](New-Item -ItemType Directory -Path $r); return $r }
}

Describe 'Get-PhoenixShortcutSpec' {
    It 'returns the two shortcuts with the CONVENTIONS target (conhost.exe --headless powershell.exe)' {
        $specs = @(Get-PhoenixShortcutSpec -Root 'X:\anywhere\Phoenix')
        $specs.Name | Should -Be @('Phoenix_Client.lnk', 'Phoenix_LAI_Server.lnk')
        foreach ($s in $specs) {
            $s.TargetPath | Should -Be '%SystemRoot%\System32\conhost.exe'
            $s.Arguments | Should -BeLike '--headless *powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand *'
            $s.WorkingDirectory | Should -Be ''
            $s.WindowStyle | Should -Be 1
            $s.Arguments.Length | Should -BeLessOrEqual 1023
        }
    }
    It 'keeps the original arguments, icon and description of <_>' -ForEach @('Phoenix_Client.lnk', 'Phoenix_LAI_Server.lnk') {
        $s = Get-PhoenixShortcutSpec -Root 'C:\p' -Name $_
        $s.Arguments | Should -BeExactly ($script:Prefix + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($script:Original[$_])))
        $want = @{ 'Phoenix_Client.lnk' = @('%SystemRoot%\System32\shell32.dll,13', 'Phoenix LAI client (chat with OpenAI /v1 connections)'); 'Phoenix_LAI_Server.lnk' = @('%SystemRoot%\System32\shell32.dll,15', 'Phoenix LAI server (models + OpenAI API, tray)') }[$_]
        $s.IconLocation | Should -Be $want[0]
        $s.Description | Should -Be $want[1]
    }
    It 'puts no project path inside the shortcut: only Path depends on -Root' {
        $a = @(Get-PhoenixShortcutSpec -Root 'C:\one'); $b = @(Get-PhoenixShortcutSpec -Root 'D:\two\deeper')
        for ($i = 0; $i -lt $a.Count; $i++) {
            foreach ($f in 'TargetPath', 'Arguments', 'WorkingDirectory', 'Description', 'IconLocation') { $a[$i].$f | Should -BeExactly $b[$i].$f }
            $a[$i].Path | Should -Be ([IO.Path]::Combine('C:\one', $a[$i].Name))
            $a[$i].Path | Should -BeLike ('C:\one*' + $a[$i].Name)
        }
        ($a | ConvertTo-Json) | Should -Not -Match '(?i)users\\|desktop-|S-1-5-21'
    }
    It 'refuses a script name that is not a plain .ps1 in src\' {
        { Get-PhoenixShortcutCommand -Script '..\evil.ps1' } | Should -Throw
        { Get-PhoenixShortcutCommand -Script 'x.ps1'' ; calc' } | Should -Throw
    }
}

Describe 'Test-PhoenixShortcutMatch' {
    BeforeAll { $script:Spec = Get-PhoenixShortcutSpec -Root 'C:\p' -Name 'Phoenix_Client.lnk' }
    It 'treats an empty (null) field like an empty string' {
        $lnk = New-FakeShortcut $script:Spec; $lnk.WorkingDirectory = $null
        Test-PhoenixShortcutMatch -Shortcut $lnk -Spec $script:Spec | Should -BeTrue
    }
    It 'matches a shortcut that WScript.Shell reports with expanded paths and other casing' {
        $lnk = New-FakeShortcut $script:Spec
        $lnk.TargetPath = [Environment]::ExpandEnvironmentVariables($script:Spec.TargetPath).ToUpperInvariant()
        $lnk.IconLocation = [Environment]::ExpandEnvironmentVariables($script:Spec.IconLocation)
        Test-PhoenixShortcutMatch -Shortcut $lnk -Spec $script:Spec | Should -BeTrue
    }
    It 'does not match when <_> differs' -ForEach @('TargetPath', 'Arguments', 'WorkingDirectory', 'Description', 'IconLocation') {
        $lnk = New-FakeShortcut $script:Spec
        $lnk.$_ = 'D:\Dev\somewhere else'
        Test-PhoenixShortcutMatch -Shortcut $lnk -Spec $script:Spec | Should -BeFalse
    }
    It 'does not match a different window style' {
        $lnk = New-FakeShortcut $script:Spec; $lnk.WindowStyle = 7
        Test-PhoenixShortcutMatch -Shortcut $lnk -Spec $script:Spec | Should -BeFalse
    }
}

Describe 'Sync-PhoenixShortcut (COM mocked)' {
    BeforeEach { $script:Root = New-TempRoot }
    AfterEach { Remove-Item -LiteralPath $script:Root -Recurse -Force -ErrorAction SilentlyContinue }

    It 'creates both shortcuts when they are missing' {
        $made = @{}; $mk = ${function:New-FakeShortcut}
        $open = { param($p) $o = & $mk -Blank; $made[$p] = $o; $o }.GetNewClosure()
        $r = @(Sync-PhoenixShortcut -Root $script:Root -OpenShortcut $open)
        $r.State | Should -Be @('created', 'created')
        foreach ($s in Get-PhoenixShortcutSpec -Root $script:Root) {
            $made[$s.Path].Saves | Should -Be 1
            Test-PhoenixShortcutMatch -Shortcut $made[$s.Path] -Spec $s | Should -BeTrue
        }
    }
    It 'leaves a matching shortcut alone (no Save)' {
        $spec = Get-PhoenixShortcutSpec -Root $script:Root -Name 'Phoenix_Client.lnk'
        [IO.File]::WriteAllBytes($spec.Path, [byte[]](1, 2, 3))
        $lnk = New-FakeShortcut $spec
        $r = Sync-PhoenixShortcut -Root $script:Root -Name 'Phoenix_Client.lnk' -OpenShortcut ({ param($p) $null = $p; $lnk }.GetNewClosure())
        $r.State | Should -Be 'unchanged'
        $lnk.Saves | Should -Be 0
    }
    It 'repairs a shortcut that points somewhere else (an old absolute path)' {
        $spec = Get-PhoenixShortcutSpec -Root $script:Root -Name 'Phoenix_LAI_Server.lnk'
        [IO.File]::WriteAllBytes($spec.Path, [byte[]](1, 2, 3))
        $lnk = New-FakeShortcut $spec; $lnk.Arguments = '-File "D:\Dev\Phoenix\src\run-phoenix-server.ps1"'
        $r = Sync-PhoenixShortcut -Root $script:Root -Name 'Phoenix_LAI_Server.lnk' -OpenShortcut ({ param($p) $null = $p; $lnk }.GetNewClosure())
        $r.State | Should -Be 'repaired'
        $lnk.Saves | Should -Be 1
        $lnk.Arguments | Should -BeExactly $spec.Arguments
    }
    It 'is idempotent: a second run after creating changes nothing' {
        $store = @{}; $mk = ${function:New-FakeShortcut}
        $open = { param($p) if (-not $store.ContainsKey($p)) { $store[$p] = & $mk -Blank }; $store[$p] }.GetNewClosure()
        $first = @(Sync-PhoenixShortcut -Root $script:Root -OpenShortcut $open)
        foreach ($s in $first) { [IO.File]::WriteAllBytes($s.Path, [byte[]](1)) }   # the fake Save writes no file
        $second = @(Sync-PhoenixShortcut -Root $script:Root -OpenShortcut $open)
        $second.State | Should -Be @('unchanged', 'unchanged')
        ($store.Values | Measure-Object -Property Saves -Sum).Sum | Should -Be 2
    }
    It 'never throws when COM is unavailable; it reports unavailable' {
        $r = @(Sync-PhoenixShortcut -Root $script:Root -OpenShortcut { param($p) $null = $p; throw 'Retrieving the COM class factory failed' })
        $r.State | Should -Be @('unavailable', 'unavailable')
        $r[0].Error | Should -Match 'COM'
    }
    It 'reports unavailable when Save fails (for example a read-only folder)' {
        $open = { param($p) $null = $p; $o = New-FakeShortcut -Blank; $o | Add-Member -MemberType ScriptMethod -Name Save -Value { throw 'Access is denied' } -Force; $o }
        $r = Sync-PhoenixShortcut -Root $script:Root -Name 'Phoenix_Client.lnk' -OpenShortcut $open
        $r.State | Should -Be 'unavailable'
    }
}

Describe 'Launchers' {
    It 'Phoenix_LAI.bat runs src\Start-PhoenixLAI.ps1 with Windows PowerShell 5.1' {
        $bat = [IO.File]::ReadAllText((Join-Path $script:RepoRoot 'Phoenix_LAI.bat'))
        $bat | Should -Match 'powershell\.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0src\\Start-PhoenixLAI\.ps1" %\*'
    }
    It '<_> repairs the shortcuts best-effort before it starts' -ForEach @('run-phoenix-client.ps1', 'run-phoenix-server.ps1') {
        $text = [IO.File]::ReadAllText((Join-Path $script:RepoRoot ('src\' + $_)))
        $text | Should -Match 'try \{ Import-Module \(Join-Path \$PSScriptRoot ''PhoenixShortcuts\.psm1''\) -Force; \$null = Sync-PhoenixShortcut'
        $text | Should -Match 'catch \{ \$null = \$_ \}'
    }
    It 'no .lnk file is tracked, and .gitignore ignores *.lnk' {
        [IO.File]::ReadAllText((Join-Path $script:RepoRoot '.gitignore')) | Should -Match '(?m)^\*\.lnk\s*$'
        if (Get-Command git -ErrorAction SilentlyContinue) {
            Push-Location $script:RepoRoot
            try { $tracked = @(git ls-files -- '*.lnk' 2>$null) } finally { Pop-Location }
            $tracked | Should -BeNullOrEmpty
        }
    }
    It 'Package.ps1 leaves .lnk files out of the package' {
        [IO.File]::ReadAllText((Join-Path $script:RepoRoot 'Package.ps1')) | Should -Match 'rel -match ''\\\.lnk\$'''
    }
}
