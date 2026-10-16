# Kernel microbenchmarks (Windows PowerShell 5.1). Edit the variant list in tests\KernelBenchVariants.cs.
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path   # tests live in .claude\claude_tests
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src'), $here -Recurse -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
[Phoenix.Tests.KernelBenchVariants]::Run((Join-Path $root 'models'), [Console]::Out)
