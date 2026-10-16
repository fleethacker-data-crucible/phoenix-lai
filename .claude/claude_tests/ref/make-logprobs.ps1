# DEV MACHINE ONLY. Generates .claude\claude_tests\ref\outputs\logprobs.tsv from llama.cpp (llama-server on localhost).
# For each reference completion in greedy.txt, records llama.cpp's log-probability of every next token
# (teacher-forced), so the CPU forward pass can be compared position by position.
#   pwsh -NoProfile -File .claude\claude_tests\ref\make-logprobs.ps1
param([string]$Model = 'models\Llama-3.2-1B-Instruct-Q8_0.gguf', [int]$Port = 8089)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')   # project root
Set-Location $root
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$base = "http://127.0.0.1:$Port"

$srv = Start-Process llama-server -ArgumentList '-m', $Model, '-c', '512', '-ngl', '0', '-fa', 'off', '--host', '127.0.0.1', '--port', $Port, '-np', '1' -PassThru -WindowStyle Hidden
try {
    for ($i = 0; $i -lt 120; $i++) { try { Invoke-RestMethod "$base/health" | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }

    # Reference texts: prompt + completion blocks from greedy.txt.
    $texts = @(); $cur = $null
    foreach ($line in Get-Content .claude\claude_tests\ref\outputs\greedy.txt -Encoding utf8) {
        if ($line.StartsWith('### PROMPT: ')) { if ($cur -ne $null) { $texts += $cur.Trim() }; $cur = ''; continue }
        if ($cur -ne $null) { $cur = if ($cur) { "$cur`n$line" } else { $line } }
    }
    if ($cur -ne $null) { $texts += $cur.Trim() }

    $out = @("# case`tpos`ttoken`tlogprob   (log P(token at pos | tokens before pos), llama.cpp -fa off, CPU)")
    for ($c = 0; $c -lt $texts.Count; $c++) {
        $ids = (Invoke-RestMethod "$base/tokenize" -Method Post -ContentType 'application/json' -Body (@{ content = $texts[$c]; add_special = $true } | ConvertTo-Json)).tokens
        for ($p = 1; $p -lt $ids.Count; $p++) {
            $body = @{ prompt = @($ids[0..($p - 1)]); n_predict = 1; n_probs = 100; temperature = 0; cache_prompt = $false } | ConvertTo-Json -Depth 5
            $r = Invoke-RestMethod "$base/completion" -Method Post -Body $body -ContentType 'application/json'
            $hit = $r.completion_probabilities[0].top_logprobs | Where-Object { $_.id -eq $ids[$p] } | Select-Object -First 1
            if ($hit) { $out += "$c`t$p`t$($ids[$p])`t$($hit.logprob.ToString('R', [Globalization.CultureInfo]::InvariantCulture))" }
        }
        Write-Host "case $c : $($ids.Count) tokens"
    }
    $out | Set-Content .claude\claude_tests\ref\outputs\logprobs.tsv -Encoding utf8
} finally { Stop-Process $srv -Force }
