# Phase gate runner. Windows PowerShell 5.1 only.
#   powershell.exe -NoProfile -STA -File tests\gate.ps1 -Phase 2
param(
    [Parameter(Mandatory = $true)][int]$Phase,
    [string]$Model
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path   # tests live in .claude\claude_tests
if (-not $Model) { $Model = Join-Path $root 'models\gemma-4-12b-it-qat-q4_0.gguf' }

if ($PSVersionTable.PSVersion.Major -ne 5) {
    Write-Host -ForegroundColor Red "Run with Windows PowerShell 5.1 (powershell.exe), not $($PSVersionTable.PSVersion)."
    exit 2
}

& powershell.exe -NoProfile -File (Join-Path $root 'src\run.ps1') -CheckOnly
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }

$paths = @(Get-ChildItem (Join-Path $root 'src'), $here -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
Add-Type -Path $paths -CompilerParameters $cp

$Model = (Resolve-Path $Model).Path
$refs = Join-Path $here ('ref\outputs\' +[IO.Path]::GetFileNameWithoutExtension($Model))   # from ref\make-refs.ps1
$out = [Console]::Out
switch ($Phase) {
    2 { $fail = [Phoenix.Tests.Phase2Gate]::Run($Model, (Join-Path $refs 'load-log.txt'), $out) }
    3 { $fail = [Phoenix.Tests.Phase3Gate]::Run($Model, (Join-Path $here 'ref\samples.txt'), (Join-Path $refs 'tokens.tsv'), $out) }
    4 { $fail = [Phoenix.Tests.Phase4Gate]::Run($Model, (Join-Path $refs 'gemma-logprobs.tsv'), $out) }
    5 { $fail = [Phoenix.Tests.Phase5Gate]::Run($out) }
    6 { $fail = [Phoenix.Tests.KernelTests]::Run($out); if ($fail.Count -eq 0) { $fail = [Phoenix.Tests.Phase6Gate]::Run($Model, (Join-Path $refs 'gemma-logprobs.tsv'), $out) } }
    7 { $fail = [Phoenix.Tests.Phase7Gate]::Run($Model, (Join-Path $refs 'gemma-logprobs.tsv'), $out) }
    8 { $fail = [Phoenix.Tests.Phase8Gate]::Run($Model, $out) }
    9 { $fail = [Phoenix.Tests.Phase9Gate]::Run($Model, (Join-Path $root 'src\Rag.cs'), $out) }
    default { Write-Host "No gate for phase $Phase."; exit 2 }
}

if ($fail.Count -gt 0) {
    Write-Host "Phase $Phase gate FAILED ($($fail.Count)):" -ForegroundColor Red
    $fail | Select-Object -First 50 | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Phase $Phase gate passed." -ForegroundColor Green
exit 0
