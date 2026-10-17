# Long-prompt prefill benchmark (Windows PowerShell 5.1):
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\pfbench.ps1 [-Model gemma-4-12b-it-qat-q4_0.gguf] [-Lengths 2557,9309] [-Profile] [-Old] [-RefDir dir] [-Text file] [-Logprobs ref\outputs\<model>\gemma-logprobs.tsv]
param([string]$Model = 'gemma-4-12b-it-qat-q4_0.gguf', [string]$Lengths = '2557,9309', [switch]$Profile, [switch]$Old, [string]$RefDir, [string]$Text, [string]$Logprobs)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src'), $here -Recurse -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
if ($RefDir) { [Phoenix.Tests.PrefillBench]::RefDir = $RefDir }
if ($Text) { [Phoenix.Tests.PrefillBench]::TextFile = (Resolve-Path $Text).Path }
if ($Logprobs) { [Phoenix.Tests.PrefillBench]::LogprobRef = (Resolve-Path $Logprobs).Path }
if ($Old) { [Phoenix.GemmaGpu]::UseFlash2 = $false }
[Phoenix.Tests.PrefillBench]::Run((Join-Path (Join-Path $root 'models') $Model), [int[]]($Lengths -split ','), [bool]$Profile, [Console]::Out)
