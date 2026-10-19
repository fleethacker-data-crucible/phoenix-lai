# Dev check: indexes a copy of docs\ (never touches docs\.index), prints what each language request retrieves, then
# proves a same-size, same-time edit is re-indexed.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\rag-check.ps1 [-NoEmbed]
param([switch]$NoEmbed)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp

$work = Join-Path $env:TEMP 'phx-rag-check'
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
New-Item -ItemType Directory -Path $work | Out-Null
Copy-Item -Recurse (Join-Path $root 'docs\*') $work -Exclude '.index'
if (Test-Path (Join-Path $work '.index')) { Remove-Item -Recurse -Force (Join-Path $work '.index') }

$models = if ($NoEmbed) { Join-Path $work 'no-models' } else { Join-Path $root 'models' }
function Index($rag) {
    [void]$rag.StartReindex($null)   # no PowerShell callback: it would run on the indexer's thread (no runspace)
    do { Start-Sleep -Milliseconds 500; $s = $rag.StatusJson() } while ($s -match '"indexing":"')
    $s
}
$rag = New-Object Phoenix.Rag($work, $models)
Index $rag

$queries = @(
    'write a powershell script that copies files newer than 7 days', 'in powershell 7 how do I use ForEach-Object -Parallel?',
    'write a C# class with a property and constructor', 'write an html form with an email input and a label', 'how do I center a div with css flexbox?',
    'write javascript to fetch json and handle errors', 'write a batch file that loops over lines with for /f', 'write a sql left join with group by and having',
    'write an oracle sql query for the top 10 rows using NVL and TO_CHAR', 'how do I read csv files and pivot in duckdb?', 'write qlik script with applymap and a resident load',
    'how do I merge two tables and expand them in power query?', 'write a dax measure for year to date sales with CALCULATE', 'write excel vba to loop rows and delete blanks',
    'write an excel formula using xlookup with if_not_found', 'write vbscript to read a text file line by line')
foreach ($q in $queries) {
    $hits = $rag.Search($q, $null)
    $top = @($hits | Select-Object -First 3 | ForEach-Object { $_.File + ' > ' + $_.Title })
    "{0}`n    {1}" -f $q, ($(if ($top.Count) { $top -join "`n    " } else { '(nothing)' }))
}

# Same size, same timestamp, different content: must be re-indexed.
$f = Join-Path $work 'code-syntax\cmd.md'
$t = (Get-Item $f).LastWriteTimeUtc
$b = [IO.File]::ReadAllBytes($f); $i = [Array]::IndexOf($b, [byte][char]'Q'); $b[$i] = [byte][char]'Z'
[IO.File]::WriteAllBytes($f, $b); (Get-Item $f).LastWriteTimeUtc = $t
Index $rag
$rag.Dispose()
