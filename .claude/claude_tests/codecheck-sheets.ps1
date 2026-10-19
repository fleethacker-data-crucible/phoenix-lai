# Dev false-alarm test for src\CodeCheck.cs: every Example block of every syntax sheet (all verified with real tools
# by verify-syntax-docs.py) goes through CodeCheck.Find as if the model had written it. Any finding is a false alarm.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\codecheck-sheets.ps1
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true; $cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
[Phoenix.CodeCheck]::PsDataPath = Join-Path $root 'docs\checkers\ps51-commands.tsv'
# sheet -> (the user's words that set the target, the fence tag)
$ctx = @{
    'csharp' = 'C# for .NET Framework 4.8', 'csharp'; 'power-shell-5.1' = 'Windows PowerShell 5.1', 'powershell'; 'power-shell-7' = 'PowerShell 7 (pwsh)', 'powershell'
    'oracle-sql' = 'Oracle SQL', 'sql'; 'sql' = 'standard SQL', 'sql'; 'duckdb-sql' = 'DuckDB SQL', 'sql'; 'javascript' = 'JavaScript for Node', 'javascript'
    'vbscript' = 'VBScript', 'vbscript'; 'excel-vba' = 'Excel VBA', 'vba'; 'cmd' = 'a batch file', 'bat'; 'dax' = 'DAX', 'dax'
    'power-query' = 'Power Query M', 'powerquery'; 'qlik-script' = 'Qlik script', 'qlik'; 'excel-formulas' = 'Excel formula', 'excel'
    'html' = 'HTML', 'html'; 'css' = 'CSS', 'css'
}
$total = 0; $alarms = 0
foreach ($f in Get-ChildItem (Join-Path $root 'docs\code-syntax') -Filter *.md) {
    $sheet = $f.BaseName
    if (-not $ctx.ContainsKey($sheet)) { continue }
    $text = [IO.File]::ReadAllText($f.FullName) -replace "`r`n", "`n"
    $heading = ''; $label = ''
    $lines = $text -split "`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l.StartsWith('### ')) { $heading = $l.Substring(4); $label = '' }
        if ($l -match '^\*\*([^*]+)\*\*') { $label = $Matches[1].Trim().TrimEnd(':') }
        if ($l -match '^```(\w*)\s*$') {
            $j = $i + 1; while ($j -lt $lines.Count -and -not $lines[$j].StartsWith('```')) { $j++ }
            $code = ($lines[($i + 1)..($j - 1)] -join "`n"); $i = $j
            if ($label -ne 'Example') { continue }
            $total++
            $reply = '```' + $ctx[$sheet][1] + "`n" + $code + "`n" + '```'
            $langs = $null
            $found = [Phoenix.CodeCheck]::Find($reply, 'Write ' + $ctx[$sheet][0] + ' code.', [ref]$langs)
            if ($found.Count -gt 0) { $alarms++; "FALSE ALARM [$sheet] $heading"; $found | ForEach-Object { '    ' + $_.ToString() } }
        }
    }
}
"$total verified examples checked, $alarms with false alarms"
