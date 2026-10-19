# Dev only: dumps every installed command's parameters (name, type, mandatory, position, pipeline, aliases) as JSON,
# for make-ps-syntax.py. Run once under powershell.exe (5.1) and once under pwsh (7).
#   powershell.exe -NoProfile -File .claude\tools\dump-ps-params.ps1 -Out ps5-params.json
param([Parameter(Mandatory = $true)][string]$Out)
$common = [System.Management.Automation.PSCmdlet]::CommonParameters + [System.Management.Automation.PSCmdlet]::OptionalCommonParameters
Get-Module -ListAvailable | Where-Object { $_.ModuleBase -like "$PSHOME*" -or $_.Name -match '^(PackageManagement|PowerShellGet|PSReadLine|Microsoft\.PowerShell\.PSResourceGet|ThreadJob|Microsoft\.PowerShell\.ThreadJob)$' } |
    ForEach-Object { try { Import-Module $_.Name -ErrorAction Stop -WarningAction SilentlyContinue } catch {} }
$result = @{}
foreach ($c in Get-Command -CommandType Cmdlet, Function -ErrorAction SilentlyContinue) {
    $modName = if ($c.Module) { $c.Module.Name } else { $c.ModuleName }
    $ps = @()
    try { $params = $c.Parameters } catch { continue }
    if (-not $params) { continue }
    foreach ($p in $params.Values) {
        if ($common -contains $p.Name) { continue }
        $attrs = @($p.Attributes | Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] })
        $pos = @($attrs | Where-Object { $_.Position -ge 0 } | ForEach-Object { $_.Position }) | Select-Object -First 1
        $ps += [ordered]@{
            name = $p.Name; type = $p.ParameterType.Name; switch = $p.SwitchParameter
            mandatory = [bool]($attrs | Where-Object { $_.Mandatory }); position = if ($pos -ne $null) { $pos } else { -1 }
            pipeline = [bool]($attrs | Where-Object { $_.ValueFromPipeline }); byName = [bool]($attrs | Where-Object { $_.ValueFromPipelineByPropertyName })
            aliases = @($p.Aliases)
            validate = @($p.Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] } | ForEach-Object { $_.ValidValues } | Select-Object -Unique)
        }
    }
    $result[$c.Name] = [ordered]@{ module = $modName; params = $ps }
}
$result | ConvertTo-Json -Depth 6 -Compress | Set-Content -Path $Out -Encoding UTF8
"$($result.Count) commands -> $Out"
