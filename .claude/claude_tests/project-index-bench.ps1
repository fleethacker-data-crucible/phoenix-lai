# Dev benchmark for the project index: builds a test project in Phoenix_Projects (this repo's src\, html\ and docs\
# plus generated files), then times the first sweep, a one-file update, the file watcher, searches and a reload.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\project-index-bench.ps1 [-Extra 2000] [-Embed]
param([int]$Extra = 2000, [switch]$Embed)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp

$proj = Join-Path $env:USERPROFILE 'Phoenix_Projects\phx-index-bench'
if (Test-Path $proj) { Remove-Item -Recurse -Force $proj }
New-Item -ItemType Directory $proj | Out-Null
Copy-Item -Recurse (Join-Path $root 'src') (Join-Path $proj 'src')
Copy-Item -Recurse (Join-Path $root 'html') (Join-Path $proj 'html')
Copy-Item -Recurse (Join-Path $root 'docs\code-syntax') (Join-Path $proj 'docs')
New-Item -ItemType Directory (Join-Path $proj 'gen') | Out-Null
for ($i = 0; $i -lt $Extra; $i++) {
    $body = "namespace Gen`r`n{`r`n    public static class Widget$i`r`n    {`r`n        public static int Compute$i(int x) { return x * $i + $($i % 7); }`r`n`r`n" +
            "        public static string Describe$i(string name)`r`n        {`r`n            return name + "" widget number $i"";`r`n        }`r`n    }`r`n}`r`n"
    [IO.File]::WriteAllText((Join-Path $proj "gen\Widget$i.cs"), $body)
}
$files = @(Get-ChildItem -Recurse -File $proj).Count
$bytes = (Get-ChildItem -Recurse -File $proj | Measure-Object Length -Sum).Sum
"test project: $files files, {0:F1} MB" -f ($bytes / 1MB)

$rag = $null
$embedFn = $null
if ($Embed) { [Phoenix.Dbg]::Attach((New-Object Phoenix.ChatLog((Join-Path $env:TEMP 'phxdbg'))), (Join-Path $env:TEMP 'phxdbg')); $rag = New-Object Phoenix.Rag((Join-Path $env:TEMP 'phx-empty-docs'), (Join-Path $root 'models')); $embedFn = [Phoenix.ProjectIndex]::EmbedderOf($rag) }
$busy = $null   # no PowerShell callbacks: the index calls them on its own threads

$ix = New-Object Phoenix.ProjectIndex($proj, $embedFn, $busy)
"first sweep (cold):   " + $ix.Start()

# one-file update, called directly (the model's write path)
$f = Join-Path $proj 'src\Workspace.cs'
$t = [IO.File]::ReadAllText($f)
[IO.File]::WriteAllText($f, $t.Replace('public string Resolve(string rel)', "public string Resolve(string rel)   // touched"))
$ms = 0.0
[void]$ix.Update([string[]]@($f), [ref]$ms)
"one-file update:      {0:F2} ms (src\Workspace.cs, {1} KB)" -f $ms, [int]((Get-Item $f).Length / 1KB)
$ms2 = 0.0
[void]$ix.Update([string[]]@($f), [ref]$ms2)
"unchanged re-check:   {0:F2} ms" -f $ms2

# watcher: an outside save, then wait for the index to see a new symbol
$g = Join-Path $proj 'gen\Watched.cs'
$sw = [Diagnostics.Stopwatch]::StartNew()
[IO.File]::WriteAllText($g, "class Watched { void ZebraUniqueMethodName() { } }")
while ($sw.ElapsedMilliseconds -lt 5000) {
    if (@($ix.Search('where is ZebraUniqueMethodName defined', $false)).Count -gt 0) { break }
    Start-Sleep -Milliseconds 10
}
"watcher save->found:  {0} ms (includes the 250 ms quiet period)" -f $sw.ElapsedMilliseconds

foreach ($q in 'where is ProjectRootOf defined', 'fix the chat save function SaveChat in chat.js', 'how does the KV cache ring buffer work in GemmaGpu',
               'Describe1234 returns the wrong text', 'change the trash icon color in the sidebar css') {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $h = @($ix.Search($q, $false))
    "search {0,5:F1} ms  {1,-55} -> {2}" -f $sw.Elapsed.TotalMilliseconds, $q, (($h | Select-Object -First 2 | ForEach-Object { $_.File + ':' + $_.Start }) -join '  ')
}

$ix.Save()
"saved index:          {0:F1} MB" -f ((Get-Item $ix.IndexFile).Length / 1MB)
$ix.Dispose()
$ix2 = New-Object Phoenix.ProjectIndex($proj, $embedFn, $busy)
"reload + sweep:       " + $ix2.Start()
if ($Embed) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p0 = $ix2.PendingVectors
    Start-Sleep -Seconds 20
    $p1 = $ix2.PendingVectors
    "embedding:            {0} sections in 20 s ({1} still pending) on {2}" -f ($p0 - $p1), $p1, $rag.EmbedBackend
}
$ix2.Dispose()
if ($rag) { $rag.Dispose() }
Remove-Item -Recurse -Force $proj
