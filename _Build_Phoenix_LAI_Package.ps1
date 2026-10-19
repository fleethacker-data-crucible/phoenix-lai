# Packs the whole app into Phoenix_LAI_Package.7z, then encodes it as Phoenix_LAI_Package.b64.
# Run on the dev machine: powershell.exe -NoProfile -File Make_Phoenix_LAI_Package.ps1 [-IncludeModels]
param([switch]$IncludeModels)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$name = 'Phoenix_LAI_Package'
$sevenZ = Join-Path $root "$name.7z"
$b64 = Join-Path $root "$name.b64"

$exe = $null
$cmd = Get-Command 7z.exe -ErrorAction SilentlyContinue
if ($cmd) { $exe = $cmd.Source }
foreach ($p in @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe")) {
    if (-not $exe -and (Test-Path $p)) { $exe = $p }
}
if (-not $exe) { throw '7-Zip (7z.exe) not found. Install 7-Zip on this dev machine or add it to PATH.' }

foreach ($f in @($sevenZ, $b64)) { if (Test-Path $f) { Remove-Item $f -Force } }

$excludes = @('-xr!.git', '-xr!logs', "-x!$name.7z", "-x!$name.b64")
if (-not $IncludeModels) { $excludes += '-xr!models\*.gguf' }

Push-Location $root
try {
    & $exe a -t7z -mx=9 $sevenZ '*' $excludes | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "7z failed with exit code $LASTEXITCODE" }
} finally { Pop-Location }

# Stream the base64 so large archives never sit in memory.
$in = [IO.File]::OpenRead($sevenZ)
$out = [IO.File]::Create($b64)
try {
    $cs = New-Object Security.Cryptography.CryptoStream($out, (New-Object Security.Cryptography.ToBase64Transform), 'Write')
    $in.CopyTo($cs, 1MB)
    $cs.FlushFinalBlock()
    $cs.Dispose()
} finally { $in.Dispose(); $out.Dispose() }

'{0}: {1:N1} MB' -f (Split-Path $sevenZ -Leaf), ((Get-Item $sevenZ).Length / 1MB)
'{0}: {1:N1} MB' -f (Split-Path $b64 -Leaf), ((Get-Item $b64).Length / 1MB)
