# Packs Phoenix into Phoenix_LAI_YYYY_MM_DD_HHMM.package (a base64-encoded .zip) in this folder.
#   1. stamps __version.info with YYYY_MMDD_HHMM
#   2. zips every file (except .git, logs, models\*.gguf, worktrees and earlier packages; -IncludeModels keeps the models)
#   3. encodes the zip as base64, decodes it again and checks it is byte-for-byte the zip (SHA-256 + length)
#   4. deletes the zip, leaving only the .package
# Windows PowerShell 5.1: powershell.exe -NoProfile -ExecutionPolicy Bypass -File Package.ps1 [-IncludeModels]
param([switch]$IncludeModels)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$now = Get-Date
$version = $now.ToString('yyyy_MMdd_HHmm')
$name = 'Phoenix_LAI_' + $now.ToString('yyyy_MM_dd_HHmm')
$zip = Join-Path $root "$name.zip"
$pkg = Join-Path $root "$name.package"

[IO.File]::WriteAllText((Join-Path $root '__version.info'), $version)
"version $version"

# Relative paths (backslashes) that are left out.
function Skip([string]$rel) {
    if ($rel -match '^(\.git|logs|\.claude\\worktrees|docs\\\.index)(\\|$)') { return $true }
    if ($rel -notmatch '\\' -and $rel -match '\.(zip|package|b64|7z)$') { return $true }   # packages in the root
    if (-not $IncludeModels -and $rel -match '\.gguf$') { return $true }
    return $false
}

foreach ($f in @($zip, $pkg)) { if (Test-Path $f) { Remove-Item $f -Force } }
$count = 0
$fs = [IO.File]::Open($zip, 'CreateNew')
try {
    $archive = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $root -Recurse -File -Force) {
            $rel = $file.FullName.Substring($root.Length).TrimStart('\')
            if (Skip $rel) { continue }
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $rel.Replace('\', '/'), [IO.Compression.CompressionLevel]::Optimal)
            $count++
        }
    } finally { $archive.Dispose() }
} finally { $fs.Dispose() }
'{0}: {1} files, {2:N1} MB' -f (Split-Path $zip -Leaf), $count, ((Get-Item $zip).Length / 1MB)

# zip -> base64, streamed (76-character lines are not used: one plain base64 text)
$in = [IO.File]::OpenRead($zip); $out = [IO.File]::Create($pkg)
try {
    $cs = New-Object Security.Cryptography.CryptoStream($out, (New-Object Security.Cryptography.ToBase64Transform), 'Write')
    $in.CopyTo($cs, 1MB); $cs.FlushFinalBlock(); $cs.Dispose()
} finally { $in.Dispose(); $out.Dispose() }

# Check: decode the .package again and compare with the zip.
function HashOf([IO.Stream]$s) { $sha = [Security.Cryptography.SHA256]::Create(); try { [BitConverter]::ToString($sha.ComputeHash($s)) } finally { $sha.Dispose() } }
$z = [IO.File]::OpenRead($zip)
try { $zipHash = HashOf $z; $zipLen = $z.Length } finally { $z.Dispose() }
$p = [IO.File]::OpenRead($pkg)
$dec = New-Object Security.Cryptography.CryptoStream($p, (New-Object Security.Cryptography.FromBase64Transform), 'Read')
$ms = New-Object IO.MemoryStream
try { $dec.CopyTo($ms, 1MB) } finally { $dec.Dispose(); $p.Dispose() }
$ms.Position = 0
$pkgHash = HashOf $ms; $pkgLen = $ms.Length; $ms.Dispose()
if ($pkgHash -ne $zipHash -or $pkgLen -ne $zipLen) {
    throw "Check FAILED: the decoded .package ($pkgLen bytes) does not match the zip ($zipLen bytes). The zip is kept."
}
"check OK: decoded package = zip ($zipLen bytes, SHA-256 $($zipHash.Replace('-','').Substring(0,16))...)"

Remove-Item $zip -Force
'{0}: {1:N1} MB' -f (Split-Path $pkg -Leaf), ((Get-Item $pkg).Length / 1MB)
