# Dev check: the auto context each model would get right now, and which syntax sheets a request maps to.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\plan-check.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp

$gpu = [Phoenix.GpuDevice]::Create()
[UInt64]$used = 0; [UInt64]$budget = 0
[void]$gpu.MemoryBudget([ref]$used, [ref]$budget)
$free = [long]($budget - [Math]::Min($used, $budget))
$commit = [Phoenix.MemoryTrim]::AvailableCommit()
foreach ($m in Get-ChildItem (Join-Path $root 'models') -Filter '*.gguf' | Where-Object { $_.Name -notmatch 'mmproj|embedding' }) {
    $g = [Phoenix.GgufFile]::Open($m.FullName)
    try { $sw = ''; [Phoenix.GemmaGpu]::SwaSlack = [Phoenix.ContextPlanner]::ChooseSlack($g, $m.FullName, $free, $commit, 0, [ref]$sw); $why = ''; $ctx = [Phoenix.ContextPlanner]::Choose($g, $m.FullName, $true, $free, $commit, [ref]$why); $cn = [long]0; $mb = [Phoenix.ContextPlanner]::MinBytes($g, $m.FullName, $true, [ref]$cn); "{0,-32} {1}; {2}; min {3:F1} GB" -f $m.Name, $why, $sw, ($mb / 1GB) } catch { "{0,-32} {1}" -f $m.Name, $_.Exception.InnerException.Message }
    finally { $g.Dispose() }
}
$gpu.Dispose()

$detect = [Phoenix.Rag].GetMethod('LanguageName', [Reflection.BindingFlags]'Public,Static')
$sheet = [Phoenix.Rag].GetMethod('SheetLanguage', [Reflection.BindingFlags]'NonPublic,Static')
foreach ($q in @('write a powershell script to copy files', 'oracle query with NVL', 'duckdb read a parquet file', 'write a sql join',
                 'a DAX measure for YTD sales', 'power query to unpivot columns', 'qlik load script with applymap', 'excel vba macro to loop rows',
                 'an excel formula to look up a price', 'vbscript to read a file', 'batch file to copy a folder', 'html form with a select',
                 'css grid layout', 'javascript fetch example', "Total := CALCULATE(SUM(Sales[Amt]))", "@echo off`nsetlocal")) {
    "{0,-45} -> {1}" -f $q, $detect.Invoke($null, @($q))
}
foreach ($n in 'oracle-sql', 'power-shell-5.1', 'power-shell-7', 'csharp', 'sql', 'excel-formulas', 'oracle-sql-syntax') { "{0,-25} -> {1}" -f $n, $sheet.Invoke($null, @($n)) }
