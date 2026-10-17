# DEV MACHINE ONLY. Generates llama.cpp reference outputs for a model (CPU, -fa off) into ref\outputs\<model>\:
#   load-log.txt   llama.cpp's load log (metadata dump, tensor-type counts)               -> gate 2
#   tokens.tsv     llama-tokenize ids for every line of ref\samples.txt                   -> gate 3
# The chat-reply reference (gemma-logprobs.tsv: llama.cpp's greedy reply and the log P of each of its tokens, gates
# 4, 6, 7) comes from make-gemma-logprobs.ps1. (Raw-text completions are not used: Gemma 4 without its chat template
# degenerates, in llama.cpp too.)
#   pwsh -NoProfile -File .claude\claude_tests\ref\make-refs.ps1 [-Model models\gemma-4-12b-it-qat-q4_0.gguf]
param(
    [string]$Model = 'models\gemma-4-12b-it-qat-q4_0.gguf',
    [int]$GpuLayers = 0
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..\..\..'))   # project root
$env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
$OutDir = Join-Path '.claude\claude_tests\ref\outputs' ([IO.Path]::GetFileNameWithoutExtension($Model))
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Load log (stderr of a one-token run).
$prev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
llama-completion -m $Model -p 'Hi' -n 1 -c 256 -ngl $GpuLayers -no-cnv -v 2>&1 | ForEach-Object { "$_" } | Set-Content (Join-Path $OutDir 'load-log.txt') -Encoding utf8

# Tokenizer ids (llama-tokenize processes escapes like \n in its input, as Phase3Gate does).
$tok = @(); $i = 0
foreach ($s in (Get-Content .claude\claude_tests\ref\samples.txt -Encoding utf8)) {
    $ids = (llama-tokenize -m $Model -p $s --ids --log-disable 2>$null | Out-String).Trim()
    $tok += "$i`t$ids"; $i++
}
$ErrorActionPreference = $prev
$tok | Set-Content (Join-Path $OutDir 'tokens.tsv') -Encoding utf8
