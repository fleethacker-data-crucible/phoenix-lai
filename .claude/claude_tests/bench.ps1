# Speed benchmark (Windows PowerShell 5.1): pp512 / tg128 per model, like llama-bench.
#   powershell.exe -NoProfile -STA -File tests\bench.ps1 [-Models a.gguf,b.gguf] [-Reps 2]
param([string[]]$Models, [int]$Pp = 512, [int]$Tg = 128, [int]$Reps = 2, [switch]$Profile)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path   # tests live in .claude\claude_tests
if (-not $Models) {
    $Models = @('gemma-4-12b-it-qat-q4_0.gguf')
}
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src'), $here -Recurse -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
[Phoenix.Tests.Bench]::Profile = [bool]$Profile
foreach ($m in $Models) {
    $path = Join-Path (Join-Path $root 'models') $m
    if (Test-Path $path) { [void][Phoenix.Tests.Bench]::Run($path, $Pp, $Tg, $Reps, [Console]::Out) } else { Write-Host "missing $m" }
}
