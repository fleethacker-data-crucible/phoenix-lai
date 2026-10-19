# Converts Phoenix_LAI_Package.b64 (next to this script) back into Phoenix_LAI_Package.7z.
# powershell.exe -NoProfile -File Install_Phoenix_LAI_Package.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$b64 = Join-Path $root 'Phoenix_LAI_Package.b64'
$sevenZ = Join-Path $root 'Phoenix_LAI_Package.7z'
if (-not (Test-Path $b64)) { throw "Not found: $b64" }
if (Test-Path $sevenZ) { Remove-Item $sevenZ -Force }

# Stream-decode; FromBase64Transform skips whitespace/line breaks.
$in = [IO.File]::OpenRead($b64)
$out = [IO.File]::Create($sevenZ)
try {
    $xf = New-Object Security.Cryptography.FromBase64Transform([Security.Cryptography.FromBase64TransformMode]::IgnoreWhiteSpaces)
    $cs = New-Object Security.Cryptography.CryptoStream($in, $xf, 'Read')
    $cs.CopyTo($out, 1MB)
    $cs.Dispose()
} finally { $in.Dispose(); $out.Dispose() }

'Created {0} ({1:N1} MB)' -f $sevenZ, ((Get-Item $sevenZ).Length / 1MB)
