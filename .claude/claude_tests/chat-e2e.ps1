# Dev end-to-end test of the chat flow through the real server code, on a private port (8799) with no window, so a
# running Phoenix app is not touched. Sends a conversation like the page does (history = typed text + the server's
# "append"), and reports per request: tokens read new vs reused from the cache, tool results and timing.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\chat-e2e.ps1 [-Model gemma-4-12b-q4_0-it-qat.gguf] [-Context 16384]
param([string]$Model = 'gemma-4-12b-q4_0-it-qat.gguf', [int]$Context = 16384, [string]$Scenario = 'edits')
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp

$logs = Join-Path $env:TEMP 'phx-e2e-logs'
if (Test-Path $logs) { Remove-Item -Recurse -Force $logs }
$proj = Join-Path $env:USERPROFILE 'Phoenix_Projects\phx-e2e'
if (Test-Path $proj) { Remove-Item -Recurse -Force $proj }
New-Item -ItemType Directory (Join-Path $proj 'engine') | Out-Null
Copy-Item (Join-Path $root 'src\GemmaGpu.cs'), (Join-Path $root 'src\Rag.cs') (Join-Path $proj 'engine')

$port = 8799
$srv = New-Object Phoenix.WebServer((Join-Path $root 'html'), (Join-Path $root 'models'), $logs, $port)
$srv.ContextLength = $Context
$loop = [Delegate]::CreateDelegate([Threading.ThreadStart], $srv, 'Loop')
$t = New-Object Threading.Thread($loop); $t.IsBackground = $true; $t.Start()
$base = "http://127.0.0.1:$port"

function Post($path, $obj) {
    $json = $obj | ConvertTo-Json -Depth 10 -Compress
    $r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri ($base + $path) -ContentType 'application/json' -Body ([Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 900
    $r.Content
}

if ($Scenario -eq 'edits') { [void](Post '/api/folder' @{ path = 'phx-e2e' }) }
"load: " + ((Post '/api/load' @{ name = $Model }) -replace '.*"context":(\d+).*', 'context $1')

$history = New-Object System.Collections.ArrayList
$asks = if ($Scenario -eq 'edits') { @('In GemmaGpu.cs, change the default PrefillChunk from 512 to 256.', 'Also in GemmaGpu.cs, change ChunkMin from 32 to 64.', 'And in Rag.cs, change TopK from 6 to 8.') }
        else { @('Write a DAX measure for year-to-date sales from Sales[Amount] using a Date table.', 'What is the difference between a measure and a calculated column?', 'Explain in detail how CALCULATE changes the filter context.') }
$n = 0
foreach ($ask in $asks) {
    $n++
    [void]$history.Add(@{ role = 'user'; content = $ask })
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $body = Post '/api/chat' @{ messages = @($history); temperature = 0; max_tokens = 1024 }
    $done = $null
    foreach ($line in ($body -split "`n")) { $l = $line.Trim(); if ($l.StartsWith('data: {"done":true')) { $done = $l.Substring(6) | ConvertFrom-Json } }
    if (-not $done) { throw "turn ${n}: no done event in the reply stream" }
    if ($done) { foreach ($m in $done.append) { [void]$history.Add(@{ role = $m.role; content = $m.content }) } }
    "turn {0}: {1:F1} s" -f $n, $sw.Elapsed.TotalSeconds
}
Start-Sleep -Milliseconds 500
$log = Get-ChildItem $logs -Filter *.log | Select-Object -First 1
Get-Content $log.FullName | Where-Object { $_ -match '#\d+ (ASSISTANT step|TOOL|CHECK|INDEXED|LOOP|PROJECT|RAG)' } | ForEach-Object { if ($_.Length -gt 200) { $_.Substring(0, 200) } else { $_ } }
if ($Scenario -eq 'edits') { "--- file changes:"; Get-ChildItem -Recurse (Join-Path $proj 'engine') -Filter *.cs | ForEach-Object { Select-String -Path $_.FullName -Pattern 'PrefillChunk = |ChunkMin = |TopK = ' | ForEach-Object { "  " + $_.Filename + ":" + $_.LineNumber + ": " + $_.Line.Trim() } } }
[void](Post '/api/unload' @{})
Remove-Item -Recurse -Force $proj
[Environment]::Exit(0)
