# Phase gate runner. Windows PowerShell 5.1 only.
#   powershell.exe -NoProfile -STA -File tests\gate.ps1 -Phase 2
param(
    [Parameter(Mandatory = $true)][int]$Phase,
    [string]$Model
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..')).Path
if (-not $Model) { $Model = Join-Path $root 'models\Llama-3.2-1B-Instruct-Q8_0.gguf' }

if ($PSVersionTable.PSVersion.Major -ne 5) {
    Write-Host -ForegroundColor Red "Run with Windows PowerShell 5.1 (powershell.exe), not $($PSVersionTable.PSVersion)."
    exit 2
}

& powershell.exe -NoProfile -File (Join-Path $root 'run.ps1') -CheckOnly
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }

$paths = @(Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
Add-Type -Path $paths -CompilerParameters $cp

$Model = (Resolve-Path $Model).Path
$out = [Console]::Out
switch ($Phase) {
    2 { $fail = [Phoenix.Tests.Phase2Gate]::Run($Model, (Join-Path $root 'dev\ref\outputs\load-log.txt'), $out) }
    3 { $fail = [Phoenix.Tests.Phase3Gate]::Run($Model, (Join-Path $root 'dev\ref\samples.txt'), (Join-Path $root 'dev\ref\outputs\tokens.tsv'), $out) }
    4 { $fail = [Phoenix.Tests.Phase4Gate]::Run($Model, (Join-Path $root 'dev\ref\outputs\greedy.txt'), (Join-Path $root 'dev\ref\outputs\logprobs.tsv'), $out) }
    default { Write-Host "No gate for phase $Phase."; exit 2 }
}

if ($fail.Count -gt 0) {
    Write-Host "Phase $Phase gate FAILED ($($fail.Count)):" -ForegroundColor Red
    $fail | Select-Object -First 50 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Phase $Phase gate passed." -ForegroundColor Green
exit 0
