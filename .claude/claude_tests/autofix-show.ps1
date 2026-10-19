# Dev: shows CodeCheck.AutoFix on one rag-eval reply (lines that changed), and csc's errors after the fix.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\autofix-show.ps1 -Results <results.json> -Arm off -Lang csharp -Index 8
param([string]$Results, [string]$Arm, [string]$Lang = 'csharp', [int]$Index)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true; $cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp
$r = (Get-Content $Results -Raw -Encoding UTF8 | ConvertFrom-Json).results | Where-Object { $_.arm -eq $Arm -and $_.lang -eq $Lang -and $_.i -eq $Index } | Select-Object -First 1
$fixes = New-Object 'System.Collections.Generic.List[string]'
$fixed = [Phoenix.CodeCheck]::AutoFix([Phoenix.CodeLint]::FixUsings($r.draft, $fixes), 'C# for .NET Framework 4.8. ' + $r.prompt, $fixes)
$a = $r.draft -split "`n"; $b = $fixed -split "`n"
for ($i = 0; $i -lt [Math]::Max($a.Count, $b.Count); $i++) { if ($a[$i] -ne $b[$i]) { "- $($a[$i])"; "+ $($b[$i])" } }
$code = ([regex]::Matches($fixed, '```[ \t]*(?:csharp|cs|c#)?[ \t]*\r?\n([\s\S]*?)```') | ForEach-Object { $_.Groups[1].Value } | Sort-Object Length -Descending | Select-Object -First 1)
$f = Join-Path $env:TEMP 'phx-show.cs'; [IO.File]::WriteAllText($f, $code)
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /t:exe "/out:$env:TEMP\phx-show.exe" $f 2>&1 | Where-Object { $_ -match ': error ' } | Select-Object -First 8
