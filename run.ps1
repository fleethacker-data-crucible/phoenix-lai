# Local LLM Studio launcher. Windows PowerShell 5.1 only.
#   powershell.exe -NoProfile -STA -File run.ps1             # check, compile, launch
#   powershell.exe -NoProfile -STA -File run.ps1 -CheckOnly  # locality checks only
#   powershell.exe -NoProfile -STA -File run.ps1 -SmokeTest  # open window, auto-close
param(
    [switch]$CheckOnly,
    [switch]$SmokeTest,
    [string]$Root
)

$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = Split-Path -Parent $MyInvocation.MyCommand.Path }

if ($PSVersionTable.PSVersion.Major -ne 5) {
    Write-Host -ForegroundColor Red "Run with Windows PowerShell 5.1 (powershell.exe), not $($PSVersionTable.PSVersion)."
    exit 2
}

$src = Join-Path $Root 'src'

# Framework assemblies the studio may reference. Nothing else is ever passed to the compiler.
$References = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')

$DllAllowlist = @('vulkan-1.dll', 'kernel32.dll')

$BannedPatterns = [ordered]@{
    'System.Net'             = 'network namespace'
    'HttpClient'             = 'network client'
    'WebClient'              = 'network client'
    'WebRequest'             = 'network client'
    'Socket'                 = 'network socket'
    'Process.Start'          = 'launching processes'
    'Assembly.Load'          = 'loading external assemblies'
    'NativeLibrary'          = 'loading native libraries'
    'LoadLibrary'            = 'loading native libraries'
    'http://'                = 'URL in source'
    'https://'               = 'URL in source'
}

function Test-Locality {
    $failures = New-Object System.Collections.Generic.List[string]
    $files = @(Get-ChildItem $src -Recurse -Filter *.cs)

    if ($files.Count -eq 0) { $failures.Add("No source files in $src") }

    # 1. No binaries shipped in the repo (models/ and dev/ref are outside the studio).
    $binaries = Get-ChildItem $Root -Recurse -File -Include *.dll, *.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(models|\.git|dev\\ref)\\' }
    foreach ($b in $binaries) { $failures.Add("Binary in repo: $($b.FullName)") }

    foreach ($f in $files) {
        $lines = Get-Content $f.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $where = "$($f.Name):$($i + 1)"

            # 2. Banned APIs / URLs.
            foreach ($p in $BannedPatterns.Keys) {
                if ($line.IndexOf($p, [StringComparison]::Ordinal) -ge 0) {
                    $failures.Add("$where uses '$p' ($($BannedPatterns[$p]))")
                }
            }

            # 3. P/Invoke targets must be on the allowlist.
            foreach ($m in [regex]::Matches($line, '(DllImport|LibraryImport)\s*\(\s*"([^"]+)"')) {
                $dll = $m.Groups[2].Value.ToLowerInvariant()
                if (-not $dll.EndsWith('.dll')) { $dll += '.dll' }
                if ($DllAllowlist -notcontains $dll) { $failures.Add("$where imports '$dll' (not on allowlist)") }
            }

            # 4. No assembly references smuggled in via source directives.
            if ($line -match '^\s*#r\s|extern\s+alias') { $failures.Add("$where adds an assembly reference") }
        }
    }
    return ,$failures
}

$failures = Test-Locality
if ($failures.Count -gt 0) {
    Write-Host "Locality checks FAILED:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 1
}
Write-Host "Locality checks passed."
if ($CheckOnly) { exit 0 }

$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
$References | ForEach-Object { [void]$cp.ReferencedAssemblies.Add($_) }

$paths = @(Get-ChildItem $src -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
Add-Type -Path $paths -CompilerParameters $cp

$smokeMs = 0
if ($SmokeTest) { $smokeMs = 1500 }
exit ([Phoenix.App]::Run($smokeMs))
