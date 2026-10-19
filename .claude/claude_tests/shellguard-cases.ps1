# Dev check of ShellGuard.IsRunRequest on messages with a known answer (true = a request for the model to run something).
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\shellguard-cases.ps1
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path
Add-Type -Path (Join-Path $root 'src\ShellGuard.cs') -ReferencedAssemblies 'System.dll', 'System.Core.dll'
$cases = [ordered]@{
    'npm start' = $true; 'python app.py' = $true; 'dotnet build' = $true; '.\run.ps1' = $true; 'git push origin main' = $true
    'node server.js' = $true; 'please run it' = $true; 'can you execute this script' = $true; 'pwsh -File build.ps1' = $true
    'python function that sorts a list' = $false; 'PowerShell 7. Write a script that lists files' = $false
    'PowerShell 5.1 script to zip a folder' = $false; 'how do I run this?' = $false; 'git commands for rebasing, explain' = $false
    'node.js example of reading a file' = $false; 'bash script that renames files' = $false
}
$bad = 0
foreach ($k in $cases.Keys) {
    $got = [Phoenix.ShellGuard]::IsRunRequest($k)
    $ok = $got -eq $cases[$k]
    if (-not $ok) { $bad++ }
    '{0} {1,-48} expected {2,-5} got {3}' -f $(if ($ok) { 'ok  ' } else { 'FAIL' }), $k, $cases[$k], $got
}
"$bad wrong"
