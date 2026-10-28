# DEV MACHINE ONLY. Coding accuracy + speed of an OpenAI /v1 server (the Phoenix server, or llama-server for comparison):
# every task of tasks.ps1 is asked as a chat (streamed), the first code block of the reply is graded by its test in a
# separate Windows PowerShell 5.1 process (30 s limit), and the time to first token and decode speed are recorded.
#   powershell.exe -NoProfile -File .claude\claude_tests\coding-eval\coding-eval.ps1 -BaseUrl http://localhost:1893/v1 -Model gemma-4-26B_q4_0-it -Label phoenix
# Sampling is sent explicitly (min_p 0: Phoenix has no min-p), so both servers sample the same way.
param([string]$BaseUrl = 'http://localhost:1893/v1', [string]$Model = 'gemma-4-26B_q4_0-it', [string]$Label = 'phoenix',
      [double]$Temperature = 0, [double]$TopP = 0.95, [int]$TopK = 64, [double]$RepeatPenalty = 1.0, [int]$Samples = 1,
      [int]$MaxTokens = 1536, [string]$System = 'default', [string]$Only = '', [string]$OutDir = '', [int]$Seed = 1)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutDir) { $OutDir = Join-Path $here 'results' }
New-Item -ItemType Directory $OutDir -Force | Out-Null
$tasks = & (Join-Path $here 'tasks.ps1')
if ($Only) { $tasks = @($tasks | Where-Object { $_.id -like $Only }) }
if ($System -eq 'default') {
    # Phoenix's own system prompt for large models (src\SystemPrompts.cs Full), sent to both servers
    $src = Get-Content (Join-Path $here '..\..\..\src\SystemPrompts.cs') -Raw
    $full = [regex]::Match($src, 'Full =\s*(.*?);', 'Singleline').Groups[1].Value
    $System = ([regex]::Matches($full, '"((?:[^"\\]|\\.)*)"') | ForEach-Object { $_.Groups[1].Value }) -join ''
    $System = $System -replace '\\n', "`n"
}
$inv = [Globalization.CultureInfo]::InvariantCulture

function Ask([string]$prompt, [int]$seed, [string]$lang) {
    $msgs = @()
    if ($System) { $msgs += @{ role = 'system'; content = $System } }
    $msgs += @{ role = 'user'; content = $prompt + $(if ($lang -eq 'csharp') { "`nTarget C# 5 (.NET Framework 4.8): no C# 6 or later features." } else { '' }) + "`n`nReply with the code in one fenced code block." }
    $body = @{ model = $Model; messages = $msgs; temperature = $Temperature; top_p = $TopP; top_k = $TopK; min_p = 0; repeat_penalty = $RepeatPenalty
               max_tokens = $MaxTokens; stream = $true; stream_options = @{ include_usage = $true }; seed = $seed } | ConvertTo-Json -Depth 6
    $req = [Net.HttpWebRequest]::Create("$BaseUrl/chat/completions")
    $req.Method = 'POST'; $req.ContentType = 'application/json'; $req.Timeout = 600000; $req.ReadWriteTimeout = 600000
    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
    $resp = $req.GetResponse()
    $rd = New-Object IO.StreamReader($resp.GetResponseStream(), [Text.Encoding]::UTF8)
    $text = New-Object Text.StringBuilder; $first = $null; $last = $null; $tokens = $null; $chunks = 0
    while ($null -ne ($line = $rd.ReadLine())) {
        if (-not $line.StartsWith('data: ')) { continue }
        $d = $line.Substring(6)
        if ($d -eq '[DONE]') { break }
        $j = $d | ConvertFrom-Json
        if ($j.usage) { $tokens = [int]$j.usage.completion_tokens }
        foreach ($c in @($j.choices)) {
            if ($null -eq $c -or $null -eq $c.delta) { continue }
            $piece = $c.delta.content
            if (-not $piece) { $piece = $c.delta.reasoning_content }
            if ($piece) { if ($null -eq $first) { $first = $sw.Elapsed.TotalSeconds }; $last = $sw.Elapsed.TotalSeconds; $chunks++; [void]$text.Append([string]$c.delta.content) }
        }
    }
    $rd.Close(); $resp.Close()
    if ($null -eq $tokens) { $tokens = $chunks }
    $decode = if ($last -gt $first -and $tokens -gt 1) { ($tokens - 1) / ($last - $first) } else { 0 }
    return @{ text = $text.ToString(); ttft = $first; total = $sw.Elapsed.TotalSeconds; tokens = $tokens; decode = $decode }
}

function Code([string]$reply, [string]$lang) {
    $m = [regex]::Matches($reply, '```[ \t]*([A-Za-z#+0-9-]*)[ \t]*\r?\n(.*?)```', 'Singleline')
    if ($m.Count -eq 0) { return $reply }
    foreach ($x in $m) { $tag = $x.Groups[1].Value.ToLowerInvariant(); if (($lang -eq 'csharp' -and $tag -match '^(cs|csharp|c#)$') -or ($lang -eq 'powershell' -and $tag -match '^(ps1?|powershell|pwsh)$')) { return $x.Groups[2].Value } }
    return $m[0].Groups[2].Value
}

function Grade($task, [string]$code) {
    $dir = Join-Path $env:TEMP ('phx-eval-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $dir | Out-Null
    try {
        Set-Content (Join-Path $dir 'code.txt') $code -Encoding UTF8
        Set-Content (Join-Path $dir 'test.ps1') $task.test.ToString() -Encoding UTF8
        $p = Start-Process powershell.exe -ArgumentList '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $here 'grade.ps1'), $task.lang, $dir `
            -PassThru -WindowStyle Hidden -RedirectStandardOutput (Join-Path $dir 'out.txt') -RedirectStandardError (Join-Path $dir 'err.txt')
        if (-not $p.WaitForExit(30000)) { try { $p.Kill() } catch { }; return 'FAIL: timeout' }
        $out = (Get-Content (Join-Path $dir 'out.txt') -Raw) + (Get-Content (Join-Path $dir 'err.txt') -Raw)
        if ($out -match '(?m)^PASS\s*$') { return 'PASS' }
        $msg = ([regex]::Match($out, '(?m)^FAIL: .*$')).Value
        if (-not $msg) { $msg = 'FAIL: ' + ($out -replace '\s+', ' ').Trim() }
        return $msg.Substring(0, [Math]::Min(200, $msg.Length))
    } finally { Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue }
}

$rows = @()
foreach ($t in $tasks) {
    for ($k = 0; $k -lt $Samples; $k++) {
        $r = Ask $t.prompt ($Seed + $k) $t.lang
        $g = Grade $t (Code $r.text $t.lang)
        $rows += [pscustomobject]@{ id = $t.id; lang = $t.lang; sample = $k; result = $g; pass = ($g -eq 'PASS'); ttft = $r.ttft; total = $r.total; tokens = $r.tokens; decode = $r.decode; reply = $r.text }
        Write-Host ("{0,-22} {1,-5} {2,5} tok {3,6:F1} t/s  ttft {4,5:F2} s  {5}" -f $t.id, $k, $r.tokens, $r.decode, $r.ttft, $g)
    }
}
$n = $rows.Count; $pass = @($rows | Where-Object { $_.pass }).Count
$tok = ($rows | Measure-Object tokens -Sum).Sum
$dec = ($rows | Where-Object { $_.decode -gt 0 } | Measure-Object decode -Average).Average
$ttft = ($rows | Measure-Object ttft -Average).Average
$summary = [pscustomobject]@{ label = $Label; model = $Model; temperature = $Temperature; top_p = $TopP; top_k = $TopK; repeat_penalty = $RepeatPenalty; samples = $Samples
    tasks = $tasks.Count; runs = $n; passed = $pass; pass_rate = [Math]::Round($pass / [Math]::Max(1, $n), 4)
    cs_pass = @($rows | Where-Object { $_.pass -and $_.lang -eq 'csharp' }).Count; ps_pass = @($rows | Where-Object { $_.pass -and $_.lang -eq 'powershell' }).Count
    avg_decode_tps = [Math]::Round($dec, 2); avg_ttft_s = [Math]::Round($ttft, 3); total_tokens = $tok }
$name = '{0}-t{1}-p{2}-k{3}-r{4}-s{5}' -f $Label, $Temperature.ToString($inv), $TopP.ToString($inv), $TopK, $RepeatPenalty.ToString($inv), $Samples
@{ summary = $summary; rows = $rows } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutDir "$name.json") -Encoding UTF8
"{0}: {1}/{2} passed ({3:P1}), C# {4}, PS {5}; decode {6:F1} t/s, ttft {7:F2} s, {8} tokens" -f $name, $pass, $n, ($pass / [Math]::Max(1, $n)), $summary.cs_pass, $summary.ps_pass, $dec, $ttft, $tok

