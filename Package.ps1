# Packs Phoenix into Phoenix_LAI_YYYY_MM_DD_HHMM.tar.gz and Phoenix_LAI_YYYY_MM_DD_HHMM.package (the .tar.gz as base64),
# both kept in this folder.
#   1. stamps __version.info with YYYY_MMDD_HHMM and writes the same version to VERSION as X.Y.Z
#      (yyyy.[int]MMdd.[int]HHmm, leading zeros stripped: 2026_0905_0930 -> 2026.905.930) for the release-zip workflow
#   2. writes every file into a .tar.gz (except .git, logs, models\*.gguf, worktrees, earlier packages and .lnk shortcuts;
#      -IncludeModels keeps the models)
#   3. encodes the .tar.gz as base64, decodes it again and checks it is byte-for-byte the .tar.gz (SHA-256 + length)
# Windows PowerShell 5.1: powershell.exe -NoProfile -ExecutionPolicy Bypass -File Package.ps1 [-IncludeModels]
# Unpack: Unpack.ps1 turns the .package back into the .tar.gz; extract it with: tar -xzf <file>.tar.gz
param([switch]$IncludeModels)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$now = Get-Date
$version = $now.ToString('yyyy_MMdd_HHmm')
$name = 'Phoenix_LAI_' + $now.ToString('yyyy_MM_dd_HHmm')
$gz = Join-Path $root "$name.tar.gz"
$pkg = Join-Path $root "$name.package"

[IO.File]::WriteAllText((Join-Path $root '__version.info'), $version)
# Same stamp as semver (must match tests\Version.Tests.ps1). ASCII digits only; UTF-8 without BOM.
$semver = '{0}.{1}.{2}' -f $now.Year, [int]$now.ToString('MMdd'), [int]$now.ToString('HHmm')
[IO.File]::WriteAllText((Join-Path $root 'VERSION'), $semver + "`n", (New-Object Text.UTF8Encoding $false))
"version $version ($semver)"

# Relative paths (backslashes) that are left out.
function Skip([string]$rel) {
    if ($rel -match '^(\.git|logs|\.claude\\worktrees|docs\\\.index)(\\|$)') { return $true }
    if ($rel -notmatch '\\' -and $rel -match '\.(zip|package|b64|7z|gz)$') { return $true }   # packages in the root
    if ($rel -match '\.lnk$') { return $true }   # shortcuts record this computer's name and SID; Phoenix_LAI.bat makes them
    if (-not $IncludeModels -and $rel -match '\.gguf$') { return $true }
    return $false
}

# ---- tar (ustar) writer: 512-byte header per file, the data padded to 512, two empty blocks at the end
function Put([byte[]]$h, [int]$at, [int]$len, [string]$text) {
    $b = [Text.Encoding]::UTF8.GetBytes($text)
    if ($b.Length -gt $len) { throw "tar field too long: $text" }
    [Array]::Copy($b, 0, $h, $at, $b.Length)
}
function Octal([long]$n, [int]$digits) { [Convert]::ToString($n, 8).PadLeft($digits, '0') }
function TarHeader([string]$path, [long]$size, [DateTime]$mtime) {
    $h = New-Object byte[] 512
    $prefix = ''
    if ([Text.Encoding]::UTF8.GetByteCount($path) -gt 100) {
        # ustar: split at a '/' into prefix (<= 155 bytes) and name (<= 100 bytes)
        $cut = $path.LastIndexOf('/', [Math]::Min($path.Length - 1, 155))
        while ($cut -gt 0 -and [Text.Encoding]::UTF8.GetByteCount($path.Substring($cut + 1)) -gt 100) { $cut = $path.LastIndexOf('/', $cut - 1) }
        if ($cut -le 0) { throw "path too long for tar: $path" }
        $prefix = $path.Substring(0, $cut); $path = $path.Substring($cut + 1)
    }
    Put $h 0 100 $path
    Put $h 100 8 ((Octal 420 7) + "`0")                       # mode 0644
    Put $h 108 8 ((Octal 0 7) + "`0")                         # uid
    Put $h 116 8 ((Octal 0 7) + "`0")                         # gid
    Put $h 124 12 ((Octal $size 11) + "`0")
    $unix = [long]($mtime.ToUniversalTime() - (New-Object DateTime 1970, 1, 1, 0, 0, 0, ([DateTimeKind]::Utc))).TotalSeconds
    Put $h 136 12 ((Octal $unix 11) + "`0")
    Put $h 148 8 '        '                                   # checksum: spaces while summing
    $h[156] = [byte][char]'0'                                 # regular file
    Put $h 257 6 "ustar`0"
    Put $h 263 2 '00'
    Put $h 345 155 $prefix
    $sum = 0; foreach ($x in $h) { $sum += $x }
    Put $h 148 8 ((Octal $sum 6) + "`0 ")
    return , $h
}

foreach ($f in @($gz, $pkg)) { if (Test-Path $f) { Remove-Item $f -Force } }
$count = 0
$zero = New-Object byte[] 512
$fs = [IO.File]::Open($gz, 'CreateNew')
try {
    $gzs = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
    try {
        foreach ($file in Get-ChildItem $root -Recurse -File -Force) {
            $rel = $file.FullName.Substring($root.Length).TrimStart('\')
            if (Skip $rel) { continue }
            $h = TarHeader ($rel.Replace('\', '/')) $file.Length $file.LastWriteTime
            $gzs.Write($h, 0, 512)
            $in = [IO.File]::OpenRead($file.FullName)
            try { $in.CopyTo($gzs, 1MB) } finally { $in.Dispose() }
            $pad = (512 - ($file.Length % 512)) % 512
            if ($pad) { $gzs.Write($zero, 0, $pad) }
            $count++
        }
        $gzs.Write($zero, 0, 512); $gzs.Write($zero, 0, 512)
    } finally { $gzs.Dispose() }
} finally { $fs.Dispose() }
'{0}: {1} files, {2:N1} MB' -f (Split-Path $gz -Leaf), $count, ((Get-Item $gz).Length / 1MB)

# .tar.gz -> base64, streamed
$in = [IO.File]::OpenRead($gz); $out = [IO.File]::Create($pkg)
try {
    $cs = New-Object Security.Cryptography.CryptoStream($out, (New-Object Security.Cryptography.ToBase64Transform), 'Write')
    $in.CopyTo($cs, 1MB); $cs.FlushFinalBlock(); $cs.Dispose()
} finally { $in.Dispose(); $out.Dispose() }

# Check: decode the .package again and compare with the .tar.gz.
function HashOf([IO.Stream]$s) { $sha = [Security.Cryptography.SHA256]::Create(); try { [BitConverter]::ToString($sha.ComputeHash($s)) } finally { $sha.Dispose() } }
$g = [IO.File]::OpenRead($gz)
try { $gzHash = HashOf $g; $gzLen = $g.Length } finally { $g.Dispose() }
$p = [IO.File]::OpenRead($pkg)
$dec = New-Object Security.Cryptography.CryptoStream($p, (New-Object Security.Cryptography.FromBase64Transform), 'Read')
$ms = New-Object IO.MemoryStream
try { $dec.CopyTo($ms, 1MB) } finally { $dec.Dispose(); $p.Dispose() }
$ms.Position = 0
$pkgHash = HashOf $ms; $pkgLen = $ms.Length; $ms.Dispose()
if ($pkgHash -ne $gzHash -or $pkgLen -ne $gzLen) {
    throw "Check FAILED: the decoded .package ($pkgLen bytes) does not match the .tar.gz ($gzLen bytes)."
}
"check OK: decoded package = .tar.gz ($gzLen bytes, SHA-256 $($gzHash.Replace('-','').Substring(0,16))...)"
'{0}: {1:N1} MB' -f (Split-Path $pkg -Leaf), ((Get-Item $pkg).Length / 1MB)
