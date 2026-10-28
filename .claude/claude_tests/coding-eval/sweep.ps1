# DEV MACHINE ONLY. Sampling settings for coding on one server: runs coding-eval.ps1 per configuration.
#   powershell.exe -NoProfile -File .claude\claude_tests\coding-eval\sweep.ps1 [-BaseUrl ...] [-Label phoenix]
param([string]$BaseUrl = 'http://localhost:1893/v1', [string]$Model = 'gemma-4-26B_q4_0-it', [string]$Label = 'phoenix', [int]$Samples = 3)
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
for ($i = 0; $i -lt 120; $i++) { try { Invoke-RestMethod "$BaseUrl/models" -TimeoutSec 3 | Out-Null; break } catch { Start-Sleep 2 } }
# temperature, top_p, top_k, repeat_penalty, samples (greedy runs are deterministic: one sample)
$configs = @(
    @(0, 0.95, 64, 1.0, 1), @(0, 0.95, 64, 1.1, 1),
    @(0.2, 0.95, 64, 1.0, $Samples), @(0.3, 0.9, 40, 1.0, $Samples), @(0.6, 0.95, 64, 1.0, $Samples), @(1.0, 0.95, 64, 1.0, $Samples)
)
foreach ($c in $configs) {
    & powershell.exe -NoProfile -File (Join-Path $here 'coding-eval.ps1') -BaseUrl $BaseUrl -Model $Model -Label $Label `
        -Temperature $c[0] -TopP $c[1] -TopK $c[2] -RepeatPenalty $c[3] -Samples $c[4] | Select-Object -Last 1
}
