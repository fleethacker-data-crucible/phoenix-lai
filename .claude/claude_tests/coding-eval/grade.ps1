# Grades one reply for coding-eval.ps1 (run in its own powershell.exe 5.1 process): prints PASS, or FAIL: reason.
#   grade.ps1 <csharp|powershell> <dir with code.txt and test.ps1>
param([string]$Lang, [string]$Dir)
$ErrorActionPreference = 'Stop'
try {
    $code = Get-Content (Join-Path $Dir 'code.txt') -Raw
    $test = [scriptblock]::Create((Get-Content (Join-Path $Dir 'test.ps1') -Raw))
    if ($Lang -eq 'csharp') {
        # using directives first, the rest wrapped in a class unless the reply declares one
        $lines = $code -split "`r?`n"
        $usings = @($lines | Where-Object { $_ -match '^\s*using\s+[\w.]+\s*;\s*$' })
        $body = ($lines | Where-Object { $_ -notmatch '^\s*using\s+[\w.]+\s*;\s*$' }) -join "`n"
        if ($body -notmatch '\b(class|struct)\s+\w+') { $body = "public static class Solution`n{`n$body`n}" }
        $ns = @('System', 'System.Collections.Generic', 'System.Linq', 'System.Text', 'System.Text.RegularExpressions') + @($usings | ForEach-Object { ($_ -replace '^\s*using\s+', '' -replace '\s*;\s*$', '').Trim() })
        $src = (($ns | Select-Object -Unique | ForEach-Object { "using $_;" }) -join "`n") + "`n" + $body
        $types = Add-Type -TypeDefinition $src -Language CSharp -ReferencedAssemblies 'System.Core' -PassThru -WarningAction SilentlyContinue
        $call = {
            param([string]$name, [object[]]$a)
            $m = $null
            foreach ($t in $types) { $m = $t.GetMethod($name, [Reflection.BindingFlags]'Public,NonPublic,Static'); if ($m) { break } }
            if (-not $m) { throw "no static method $name" }
            $m.Invoke($null, [object[]]$a)
        }.GetNewClosure()
        & $test
    } else {
        . ([scriptblock]::Create($code))
        & $test
    }
    'PASS'
} catch {
    'FAIL: ' + ($_.Exception.Message -replace '\s+', ' ')
}

