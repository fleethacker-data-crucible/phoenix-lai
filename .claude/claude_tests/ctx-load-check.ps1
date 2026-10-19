# Dev check: loads each Gemma model with the auto context, answers a short question, then finds a fact placed at the
# start of a long prompt (past the old 32K cap) to prove positions beyond 32K work.
#   powershell.exe -NoProfile -STA -File .claude\claude_tests\ctx-load-check.ps1 [-Models E2b,E4b,12b] [-LongTokens 40000]
param([string[]]$Models = @("E2b", "E4b", "12b"), [int]$LongTokens = 40000, [int]$Context = 0)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here '..\..')).Path
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll' | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }) -CompilerParameters $cp

$greedy = New-Object Phoenix.SamplerSettings
$greedy.Temperature = 0
$filler = 'The river bank was quiet in the morning and the birds sang over the water while the town slowly woke up. '
foreach ($m in $Models) {
    $path = (Get-ChildItem (Join-Path $root 'models') -Filter "gemma-4-$m*.gguf" | Where-Object { $_.Name -notmatch 'mmproj' } | Select-Object -First 1).FullName
    Write-Host "== $m"
    $logLines = New-Object System.Collections.Generic.List[string]
    $eng = [Phoenix.ChatEngine]::Load($path, $Context, $true, [Phoenix.TextCallback] { param($m) Write-Host "  $m" })
    try {
        Write-Host ("  context {0} on {1}" -f $eng.ContextLength, $eng.Backend)
        $msgs = New-Object 'System.Collections.Generic.List[Phoenix.ChatMessage]'
        $msgs.Add((New-Object Phoenix.ChatMessage('user', 'What is the capital of France? Answer in one word.')))
        $sb = New-Object System.Text.StringBuilder
        $st = $eng.Generate($msgs, $greedy, 16, [Phoenix.TextCallback] { param($t) [void]$sb.Append($t) }, $null)
        Write-Host ("  short: '{0}' ({1})" -f $sb.ToString().Trim(), $st.StopReason)

        if ($LongTokens -gt 0 -and $eng.ContextLength -gt $LongTokens + 512) {
            $reps = [int]($LongTokens / 22)
            $text = 'Remember this: the secret code is 7419. ' + ($filler * $reps) + "`n`nWhat is the secret code? Answer with the number only."
            $msgs = New-Object 'System.Collections.Generic.List[Phoenix.ChatMessage]'
            $msgs.Add((New-Object Phoenix.ChatMessage('user', $text)))
            $sb = New-Object System.Text.StringBuilder
            $sw = [Diagnostics.Stopwatch]::StartNew()
            $st = $eng.Generate($msgs, $greedy, 16, [Phoenix.TextCallback] { param($t) [void]$sb.Append($t) }, $null)
            Write-Host ("  long: {0} prompt tokens in {1:F1} s, answer '{2}' ({3})" -f $st.PromptTokens, $st.PromptSeconds, $sb.ToString().Trim(), $st.StopReason)
        }
    }
    finally { $eng.Dispose() }
}
