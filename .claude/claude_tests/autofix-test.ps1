# Dev check of CodeCheck.AutoFix (+ CodeLint.FixUsings): every C# draft in the rag-eval results compiled with csc.exe
# (C# 5) before and after the deterministic rewrites. Shows each reply whose error count changed and the totals.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\autofix-test.ps1 [-Results <results.json>]...
param([string[]]$Results)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$tmp = Join-Path $env:TEMP 'phx-autofix'; New-Item -ItemType Directory -Force $tmp | Out-Null
if (-not $Results) { $Results = @(Get-ChildItem (Join-Path $root '.claude\claude_tests\rag-eval\results') -Recurse -Filter results.json | ForEach-Object { $_.FullName }) }

function Errors([string]$reply) {
    $blocks = [regex]::Matches($reply, '```[ \t]*(?:csharp|cs|c#)?[ \t]*\r?\n([\s\S]*?)```') | ForEach-Object { $_.Groups[1].Value }
    if (-not $blocks) { return -1 }
    $code = $blocks | Sort-Object Length -Descending | Select-Object -First 1
    $f = Join-Path $tmp 'p.cs'; [IO.File]::WriteAllText($f, $code)
    $target = if ($code -match '\bstatic\s+(async\s+)?(void|int|Task(<int>)?)\s+Main\s*\(') { 'exe' } else { 'library' }
    $out = & $csc /nologo "/t:$target" "/out:$tmp\o.bin" '/r:System.dll,System.Core.dll,System.Xml.dll,System.Xml.Linq.dll,System.Data.dll' $f 2>&1
    return @($out | Where-Object { $_ -match ': error ' }).Count
}

$before = 0; $after = 0; $cleanB = 0; $cleanA = 0; $n = 0
foreach ($path in $Results) {
    $data = Get-Content $path -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($r in $data.results) {
        if ($r.lang -ne 'csharp' -or $r.error) { continue }
        $n++
        $fixes = New-Object 'System.Collections.Generic.List[string]'
        $fixed = [Phoenix.CodeCheck]::AutoFix([Phoenix.CodeLint]::FixUsings($r.draft, $fixes), 'C# for .NET Framework 4.8. ' + $r.prompt, $fixes)
        $b = Errors $r.draft; $a = Errors $fixed
        $before += [Math]::Max(0, $b); $after += [Math]::Max(0, $a)
        if ($b -eq 0) { $cleanB++ }; if ($a -eq 0) { $cleanA++ }
        if ($a -ne $b) { "{0,-4} {1,3} -> {2,3}  {3}  [{4}]" -f $r.arm, $b, $a, $r.prompt.Substring(0, [Math]::Min(60, $r.prompt.Length)), ($fixes -join '; ') }
    }
}
"$n drafts: clean $cleanB -> $cleanA, compile errors $before -> $after"
