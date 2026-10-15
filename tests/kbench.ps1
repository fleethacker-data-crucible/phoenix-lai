# Kernel microbenchmarks (Windows PowerShell 5.1). Edit the variant list in tests\KernelBenchVariants.cs.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src'), (Join-Path $root 'tests') -Recurse -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
[Phoenix.Tests.KernelBenchVariants]::Run((Join-Path $root 'models'), [Console]::Out)
