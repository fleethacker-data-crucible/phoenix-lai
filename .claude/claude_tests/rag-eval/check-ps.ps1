# Checks a PowerShell script the way Windows PowerShell 5.1 sees it, without running it. Dev-only (RAG evaluation).
#   powershell.exe -NoProfile -File check-ps.ps1 -Path script.ps1      -> one line of JSON
# parse: syntax errors from the 5.1 parser (PowerShell 7-only syntax such as ?? ?. && || fails here)
# commands: names that are no command, alias or function on this machine (functions the script defines are fine)
# params: -Parameter names a cmdlet does not have (unique prefixes and aliases are accepted, as PowerShell does)
param([Parameter(Mandatory = $true)][string]$Path)
$ErrorActionPreference = 'Stop'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)
$parse = @($errors | ForEach-Object { 'line ' + $_.Extent.StartLineNumber + ': ' + $_.Message })
$defined = @{}
$ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $true) | ForEach-Object { $defined[$_.Name.ToLowerInvariant()] = 1 }
$commands = New-Object System.Collections.Generic.List[string]
$params = New-Object System.Collections.Generic.List[string]
$cache = @{}
foreach ($c in $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] }, $true)) {
    $name = $c.GetCommandName()
    if (-not $name -or $name -notmatch '^[\w.-]+$' -or $defined.ContainsKey($name.ToLowerInvariant())) { continue }   # not a name: debris of a syntax error
    if ($name -match '[\\/:]|\.(exe|cmd|bat|ps1|com)$' -or $c.InvocationOperator -ne 'Unknown') { continue }   # paths, & and . calls
    if (-not $cache.ContainsKey($name)) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($cmd -and $cmd.CommandType -eq 'Alias') { $cmd = $cmd.ResolvedCommand }
        $cache[$name] = $cmd
    }
    $cmd = $cache[$name]
    if (-not $cmd) { $commands.Add('line ' + $c.Extent.StartLineNumber + ': ' + $name); continue }
    if ($cmd.CommandType -eq 'Application' -or -not $cmd.Parameters) { continue }
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($p in $cmd.Parameters.Values) { $names.Add($p.Name.ToLowerInvariant()); foreach ($a in $p.Aliases) { $names.Add($a.ToLowerInvariant()) } }
    foreach ($e in $c.CommandElements) {
        if ($e -isnot [System.Management.Automation.Language.CommandParameterAst]) { continue }
        $pn = $e.ParameterName.ToLowerInvariant()
        if ($names.Contains($pn)) { continue }
        $prefix = @($names | Where-Object { $_.StartsWith($pn) } | Select-Object -Unique)
        if ($prefix.Count -eq 1) { continue }
        $params.Add('line ' + $e.Extent.StartLineNumber + ': ' + $cmd.Name + ' -' + $e.ParameterName)
    }
}
[pscustomobject]@{ parse = $parse; commands = @($commands); params = @($params) } | ConvertTo-Json -Compress -Depth 4
