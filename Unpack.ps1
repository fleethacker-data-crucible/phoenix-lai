# Unpacks a Phoenix package into a folder of the same name, next to it:
#   Phoenix_LAI_YYYY_MM_DD_HHMM.package -> .tar.gz (decoded from base64, kept) -> files in Phoenix_LAI_YYYY_MM_DD_HHMM\
#   Phoenix_LAI_YYYY_MM_DD_HHMM.tar.gz  -> files in Phoenix_LAI_YYYY_MM_DD_HHMM\
# With no -Package, uses the newest Phoenix_LAI_*.package or Phoenix_LAI_*.tar.gz in this folder.
# Windows PowerShell 5.1 only, nothing external: powershell.exe -NoProfile -ExecutionPolicy Bypass -File Unpack.ps1 [-Package <file>]
param([string]$Package)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Package) {
    $newest = Get-ChildItem $root -File | Where-Object { $_.Name -like 'Phoenix_LAI_*.package' -or $_.Name -like 'Phoenix_LAI_*.tar.gz' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw "No Phoenix_LAI_*.package or Phoenix_LAI_*.tar.gz in $root." }
    $Package = $newest.FullName
}
$Package = (Resolve-Path $Package).Path
if ($Package -match '\.package$') { $base = $Package.Substring(0, $Package.Length - 8) }
elseif ($Package -match '\.tar\.gz$') { $base = $Package.Substring(0, $Package.Length - 7) }
else { throw "Give a .package or a .tar.gz file: $Package" }
$gz = "$base.tar.gz"
$dest = $base

# ---- .package -> .tar.gz (base64 decode; an existing .tar.gz is replaced)
if ($Package -match '\.package$') {
    $tmp = "$gz.tmp"
    $in = [IO.File]::OpenRead($Package); $out = [IO.File]::Create($tmp)
    try {
        $dec = New-Object Security.Cryptography.CryptoStream($in, (New-Object Security.Cryptography.FromBase64Transform), 'Read')
        $dec.CopyTo($out, 1MB); $dec.Dispose()
    } catch { $out.Dispose(); Remove-Item $tmp -Force -ErrorAction SilentlyContinue; throw } finally { $in.Dispose(); $out.Dispose() }
    Move-Item $tmp $gz -Force
    '{0}: {1:N1} MB' -f (Split-Path $gz -Leaf), ((Get-Item $gz).Length / 1MB)
}

# ---- .tar.gz -> files (ustar reader: regular files and folders; everything stays inside $dest)
if (Test-Path $dest) { throw "$dest already exists; move or delete it first." }
New-Item -ItemType Directory $dest | Out-Null
$destFull = [IO.Path]::GetFullPath($dest).TrimEnd('\') + '\'

function ReadFull([IO.Stream]$s, [byte[]]$buf, [int]$len) {
    $got = 0
    while ($got -lt $len) { $n = $s.Read($buf, $got, $len - $got); if ($n -le 0) { break }; $got += $n }
    return $got
}
function Field([byte[]]$h, [int]$at, [int]$len) {
    $end = $at; while ($end -lt $at + $len -and $h[$end] -ne 0) { $end++ }
    return [Text.Encoding]::UTF8.GetString($h, $at, $end - $at)
}

$count = 0
$f = [IO.File]::OpenRead($gz)
try {
    $g = New-Object IO.Compression.GZipStream($f, [IO.Compression.CompressionMode]::Decompress)
    try {
        $h = New-Object byte[] 512
        $buf = New-Object byte[] 1MB
        while ((ReadFull $g $h 512) -eq 512) {
            $name = Field $h 0 100
            if (-not $name) { break }                                   # the empty end blocks
            $prefix = Field $h 345 155
            if ($prefix) { $name = "$prefix/$name" }
            $sizeText = (Field $h 124 12).Trim()
            $size = if ($sizeText) { [Convert]::ToInt64($sizeText, 8) } else { 0L }
            $type = [char]$h[156]
            $target = [IO.Path]::GetFullPath((Join-Path $dest ($name.Replace('/', '\'))))
            if (-not $target.StartsWith($destFull, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe path in the package: $name" }
            if ($type -eq '5') { New-Item -ItemType Directory -Force $target | Out-Null }
            elseif ($type -eq '0' -or $type -eq [char]0) {
                New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
                $o = [IO.File]::Create($target)
                try {
                    $left = $size
                    while ($left -gt 0) {
                        $n = ReadFull $g $buf ([int][Math]::Min($left, $buf.Length))
                        if ($n -le 0) { throw "The package ends in the middle of $name." }
                        $o.Write($buf, 0, $n); $left -= $n
                    }
                } finally { $o.Dispose() }
                $mtimeText = (Field $h 136 12).Trim()
                if ($mtimeText) { [IO.File]::SetLastWriteTimeUtc($target, (New-Object DateTime 1970, 1, 1, 0, 0, 0, ([DateTimeKind]::Utc)).AddSeconds([Convert]::ToInt64($mtimeText, 8))) }
                $count++
            }
            else {
                # links and other entries are not used by Package.ps1: skip their data
                $left = $size
                while ($left -gt 0) { $n = ReadFull $g $buf ([int][Math]::Min($left, $buf.Length)); if ($n -le 0) { break }; $left -= $n }
            }
            $pad = (512 - ($size % 512)) % 512
            if ($pad) { [void](ReadFull $g $buf $pad) }
        }
    } finally { $g.Dispose() }
} finally { $f.Dispose() }
'{0}\: {1} files' -f (Split-Path $dest -Leaf), $count
