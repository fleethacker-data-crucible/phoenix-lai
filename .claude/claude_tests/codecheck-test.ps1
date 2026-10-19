# Dev check of src\CodeCheck.cs against replies whose real verdicts are known (rag-eval results.json files): for every
# reply, the deterministic checker's findings next to what the real tools found. Shows false alarms (checker finds
# something in code the tools passed) and misses (tools failed it, checker found nothing).
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\codecheck-test.ps1 [-Results <results.json>] [-Verbose]
param([string]$Results, [switch]$ShowAll)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
[Phoenix.CodeCheck]::PsDataPath = Join-Path $root 'docs\checkers\ps51-commands.tsv'
if (-not $Results) { $Results = (Get-ChildItem (Join-Path $root '.claude\claude_tests\rag-eval\results') -Recurse -Filter results.json | Sort-Object LastWriteTime | Select-Object -Last 1).FullName }
$data = Get-Content $Results -Raw -Encoding UTF8 | ConvertFrom-Json
$fa = 0; $miss = 0; $hit = 0; $clean = 0
foreach ($r in $data.results) {
    if ($r.error) { continue }
    $prefix = @{ powershell = 'Windows PowerShell 5.1. '; csharp = 'C# 5 for .NET Framework 4.8. '; oracle = 'Oracle SQL. '; sql = 'SQL. '; duckdb = 'DuckDB SQL. '
                 javascript = 'JavaScript. '; powershell7 = 'PowerShell 7. '; vbscript = 'VBScript. '; vba = 'Excel VBA. '; cmd = 'A Windows batch file. '
                 dax = 'DAX. '; powerquery = 'Power Query M. '; qlik = 'Qlik script. '; excel = 'Excel formulas. '; html = 'HTML. '; css = 'CSS. ' }[$r.lang]
    foreach ($which in 'draft', 'final') {
        $langs = $null
        $text = [Phoenix.CodeLint]::FixUsings($r.$which, (New-Object 'System.Collections.Generic.List[string]'))   # the server fixes usings first
        $f = [Phoenix.CodeCheck]::Find($text, $prefix + $r.prompt, [ref]$langs)
        $real = @($r.($which + '_problems'))
        $bad = $real.Count -gt 0
        if ($f.Count -gt 0 -and -not $bad) { $fa++; "FALSE ALARM [$($r.arm) $($r.lang) $($r.i) $which]"; $f | ForEach-Object { '   ' + $_.ToString() } }
        elseif ($f.Count -eq 0 -and $bad -and $text -ne $r.$which -and -not ($real | Where-Object { $_ -notmatch 'CS0246' })) { $hit++ }   # auto-fixed (missing using)
        elseif ($f.Count -eq 0 -and $bad) { $miss++; "MISSED [$($r.arm) $($r.lang) $($r.i) $which]: " + ($real -join ' | ') }
        elseif ($bad) { $hit++; if ($ShowAll) { "CAUGHT [$($r.arm) $($r.lang) $($r.i) $which]: " + ($f | ForEach-Object { $_.ToString() }) -join ' | ' } }
        else { $clean++ }
    }
}
"caught $hit, missed $miss, false alarms $fa, clean and quiet $clean"
