# DEV MACHINE ONLY. Generates llama.cpp reference outputs for a model (CPU, -fa off):
#   <OutDir>\greedy.txt    greedy completions (-n 24) for dev\ref\prompts.txt
#   <OutDir>\logprobs.tsv  teacher-forced log P(token) for every position of those completions
#   pwsh -NoProfile -File dev\ref\make-refs.ps1 -Model models\X.gguf -OutDir dev\ref\outputs\X
param(
    [Parameter(Mandatory = $true)][string]$Model,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [int]$Port = 8089,
    [int]$GpuLayers = 0
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..\..'))
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Greedy completions.
$res = @()
foreach ($p in (Get-Content dev\ref\prompts.txt -Encoding utf8)) {
    $t = llama-completion -m $Model -p $p -n 24 -c 512 -ngl $GpuLayers --temp 0 -s 1 -no-cnv -fa off 2>$null | Out-String
    $res += "### PROMPT: $p`n$($t.Trim())`n"
}
$res | Set-Content (Join-Path $OutDir 'greedy.txt') -Encoding utf8
"# llama-completion -m $Model -p <prompt> -n 24 -c 512 -ngl $GpuLayers --temp 0 -s 1 -no-cnv -fa off" | Set-Content (Join-Path $OutDir 'greedy.cmd.txt')

# Teacher-forced logprobs through a localhost llama-server.
$base = "http://127.0.0.1:$Port"
$srv = Start-Process llama-server -ArgumentList '-m', $Model, '-c', '512', '-ngl', $GpuLayers, '-fa', 'off', '--host', '127.0.0.1', '--port', $Port, '-np', '1' -PassThru -WindowStyle Hidden
try {
    for ($i = 0; $i -lt 240; $i++) { try { Invoke-RestMethod "$base/health" | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }

    $texts = @(); $cur = $null
    foreach ($line in Get-Content (Join-Path $OutDir 'greedy.txt') -Encoding utf8) {
        if ($line.StartsWith('### PROMPT: ')) { if ($cur -ne $null) { $texts += $cur.Trim() }; $cur = ''; continue }
        if ($cur -ne $null) { $cur = if ($cur) { "$cur`n$line" } else { $line } }
    }
    if ($cur -ne $null) { $texts += $cur.Trim() }

    $out = @("# case`tpos`ttoken`tlogprob   (log P(token at pos | tokens before pos), llama.cpp -fa off)")
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
    $out | Set-Content (Join-Path $OutDir 'logprobs.tsv') -Encoding utf8
} finally { Stop-Process $srv -Force }
