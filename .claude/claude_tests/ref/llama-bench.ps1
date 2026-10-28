# DEV MACHINE ONLY. llama.cpp speed via llama-server timings (LM Studio's builds ship no llama-bench): pp / tg like bench.ps1.
#   powershell.exe -NoProfile -File .claude\claude_tests\ref\llama-bench.ps1 -Model models\gemma-4-26B_q4_0-it.gguf [-Fa on] [-Reps 3]
param([string]$Model = 'models\gemma-4-26B_q4_0-it.gguf', [int]$Pp = 512, [int]$Tg = 128, [int]$Reps = 3, [string]$Fa = 'on',
      [int]$Port = 8091, [string]$Extra = '',
      [string]$LlamaServer = $(if ($env:LLAMA_SERVER) { $env:LLAMA_SERVER } else { 'llama-server' }))
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
Set-Location $root
$base = "http://127.0.0.1:$Port"
$args2 = @('-m', $Model, '-c', '4096', '-ngl', '99', '-fa', $Fa, '--host', '127.0.0.1', '--port', $Port, '-np', '1', '--no-warmup') + @($Extra -split ' ' | Where-Object { $_ })
$srv = Start-Process $LlamaServer -ArgumentList $args2 -PassThru -WindowStyle Hidden
try {
    for ($i = 0; $i -lt 600; $i++) { try { Invoke-RestMethod "$base/health" | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }
    $rnd = New-Object Random 7
    $pps = @(); $tgs = @()
    for ($r = 0; $r -le $Reps; $r++) {   # rep 0 is a warm-up
        $ids = @(2) + @(1..($Pp - 1) | ForEach-Object { $rnd.Next(1000, 200000) })
        $body = @{ prompt = $ids; n_predict = $Tg; temperature = 0; cache_prompt = $false; ignore_eos = $true } | ConvertTo-Json -Depth 4
        $t = (Invoke-RestMethod "$base/completion" -Method Post -ContentType 'application/json' -Body $body).timings
        if ($r -gt 0) { $pps += [double]$t.prompt_per_second; $tgs += [double]$t.predicted_per_second }
    }
    $ppAvg = ($pps | Measure-Object -Average).Average; $tgAvg = ($tgs | Measure-Object -Average).Average
    "llama.cpp {0} -fa {1} {2}: pp{3} {4:F1} t/s  tg{5} {6:F1} t/s  (pp {7}; tg {8})" -f [IO.Path]::GetFileNameWithoutExtension($Model), $Fa, $Extra, $Pp, $ppAvg, $Tg, $tgAvg, (($pps | % { $_.ToString('F1') }) -join ' '), (($tgs | % { $_.ToString('F1') }) -join ' ')
} finally { Stop-Process $srv -Force }
