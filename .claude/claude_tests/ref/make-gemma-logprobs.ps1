# DEV MACHINE ONLY. Teacher-forced log-probabilities from llama.cpp for a Gemma 4 chat reply, for the Gemma gate.
# Writes ref\outputs\<model>\gemma-logprobs.tsv: one line per position of the reply (token id, llama.cpp logprob,
# llama.cpp top-1 id), plus the token ids of the whole sequence on the first line.
#   pwsh -NoProfile -File .claude\claude_tests\ref\make-gemma-logprobs.ps1 [-Model models\gemma-4-12b-it-qat-q4_0.gguf] [-Ngl 0]
param([string]$Model = 'models\gemma-4-12b-it-qat-q4_0.gguf', [int]$Ngl = 0, [int]$Port = 8090, [int]$NPredict = 64,
      [string]$Question = 'Explain in two sentences what a hash map is and when to use one.',
      [string]$LlamaServer = $(if ($env:LLAMA_SERVER) { $env:LLAMA_SERVER } else { 'llama-server' }))
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
Set-Location $root
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$base = "http://127.0.0.1:$Port"
$outDir = Join-Path '.claude\claude_tests\ref\outputs' ([IO.Path]::GetFileNameWithoutExtension($Model))
New-Item -ItemType Directory $outDir -Force | Out-Null

$srv = Start-Process $LlamaServer -ArgumentList '-m', $Model, '-c', '1024', '-ngl', $Ngl, '-fa', 'off', '--host', '127.0.0.1', '--port', $Port, '-np', '1' -PassThru -WindowStyle Hidden
try {
    for ($i = 0; $i -lt 240; $i++) { try { Invoke-RestMethod "$base/health" | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }
    # Same template as Engine.GemmaTemplate (thinking off).
    $prompt = "<|turn>user`n$Question<turn|>`n<|turn>model`n<|channel>thought`n<channel|>"
    $ids = (Invoke-RestMethod "$base/tokenize" -Method Post -ContentType 'application/json' -Body (@{ content = $prompt; add_special = $true; parse_special = $true } | ConvertTo-Json)).tokens
    $gen = Invoke-RestMethod "$base/completion" -Method Post -ContentType 'application/json' -Body (@{ prompt = @($ids); n_predict = $NPredict; temperature = 0; cache_prompt = $true; return_tokens = $true } | ConvertTo-Json -Depth 5)
    $reply = @($gen.tokens)
    $all = @($ids) + $reply
    $out = @(("# ids`t" + ($all -join ' ')), "# prompt_len`t$($ids.Count)", "# pos`ttoken`tlogprob`ttop1   (log P(token at pos | tokens before pos), llama.cpp -fa off, ngl $Ngl)")
    for ($p = $ids.Count; $p -lt $all.Count; $p++) {
        $body = @{ prompt = @($all[0..($p - 1)]); n_predict = 1; n_probs = 50; temperature = 0; cache_prompt = $true } | ConvertTo-Json -Depth 5
        $r = Invoke-RestMethod "$base/completion" -Method Post -Body $body -ContentType 'application/json'
        $top = $r.completion_probabilities[0].top_logprobs
        $hit = $top | Where-Object { $_.id -eq $all[$p] } | Select-Object -First 1
        $lp = if ($hit) { ([double]$hit.logprob).ToString('R', [Globalization.CultureInfo]::InvariantCulture) } else { 'nan' }
        $out += "$p`t$($all[$p])`t$lp`t$($top[0].id)"
    }
    $out | Set-Content (Join-Path $outDir 'gemma-logprobs.tsv') -Encoding utf8
    "reply: " + $gen.content
    "wrote $($all.Count - $ids.Count) positions to $outDir\gemma-logprobs.tsv"
} finally { Stop-Process $srv -Force }
