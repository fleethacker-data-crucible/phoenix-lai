# Dev only: exports every PowerShell command on this machine with its parameters (and their aliases) to
# docs\checkers\ps51-commands.tsv (Windows PowerShell 5.1) or ps7-commands.tsv (PowerShell 7): the lists the server's
# code checker (src\CodeCheck.cs) uses to catch invented commands and parameters with no shell on hand. Run it in each:
#   powershell.exe -NoProfile -File .claude\tools\make-ps-commands.ps1
#   pwsh -NoProfile -File .claude\tools\make-ps-commands.ps1
# Format, one line per command:  Name<TAB>param|alias|alias,param,...   and one line per alias:  alias<TAB>=Target
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$out = Join-Path $root ('docs\checkers\' + $(if ($PSVersionTable.PSVersion.Major -ge 7) { 'ps7-commands.tsv' } else { 'ps51-commands.tsv' }))
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
# Import every installed module first: a function from a module that is not loaded (Compress-Archive, the ScheduledTasks
# functions) reports no parameters.
Get-Module -ListAvailable | Select-Object -ExpandProperty Name -Unique | ForEach-Object { Import-Module $_ -ErrorAction SilentlyContinue -WarningAction SilentlyContinue -DisableNameChecking }
$lines = New-Object System.Collections.Generic.List[string]
$seen = @{}
foreach ($c in Get-Command -CommandType Cmdlet, Function -ErrorAction SilentlyContinue) {
    if ($seen.ContainsKey($c.Name) -or $c.Name -notmatch '^[A-Za-z]+-[A-Za-z0-9]+$') { continue }
    $p = $null
    try { $p = $c.Parameters } catch { }
    if (-not $p) { continue }
    $seen[$c.Name] = 1
    $all = @{}
    foreach ($v in $p.Values) { $all[$v.Name] = @($v.Aliases) }
    # Provider (dynamic) parameters: -File, -Directory, -Raw, -Encoding ... exist only for a path on that drive.
    if ($p.ContainsKey('Path') -and $c.ModuleName -eq 'Microsoft.PowerShell.Management') {
        foreach ($drive in 'C:\', 'HKLM:\', 'Cert:\', 'Env:\') {
            try { foreach ($v in (Get-Command $c.Name -ArgumentList $drive -ErrorAction Stop).Parameters.Values) { if (-not $all.ContainsKey($v.Name)) { $all[$v.Name] = @($v.Aliases) } } } catch { }
        }
    }
    $ps = foreach ($k in $all.Keys) { (@($k) + $all[$k]) -join '|' }
    $lines.Add($c.Name + "`t" + ($ps -join ','))
}
foreach ($a in Get-Alias) { $lines.Add($a.Name + "`t=" + $a.Definition) }
$lines.Sort([StringComparer]::OrdinalIgnoreCase)
[IO.File]::WriteAllLines($out, $lines, (New-Object System.Text.UTF8Encoding $false))
"{0} commands, {1} aliases -> {2}" -f $seen.Count, ($lines.Count - $seen.Count), $out
