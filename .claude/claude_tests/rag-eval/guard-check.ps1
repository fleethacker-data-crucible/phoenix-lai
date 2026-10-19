# Pre-flight for the RAG evaluation: every prompt through the server's own ShellGuard.IsRunRequest, so an automated run
# never trips the run guard (3 requests warn, 6 lock the chat for 5 minutes).
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\rag-eval\guard-check.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..\..')).Path
Add-Type -Path (Join-Path $root 'src\ShellGuard.cs') -ReferencedAssemblies 'System.dll', 'System.Core.dll'
$p = Get-Content (Join-Path $here 'prompts.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$bad = 0
foreach ($lang in $p.PSObject.Properties) {
    foreach ($q in $lang.Value.prompts) {
        if ([Phoenix.ShellGuard]::IsRunRequest($lang.Value.prefix + $q)) { $bad++; "RUN REQUEST [$($lang.Name)]: $q" }
    }
}
"$bad prompts would trip the run guard"
