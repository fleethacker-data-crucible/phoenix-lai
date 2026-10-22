# Turns a Phoenix_LAI_*.package (base64) back into its .zip, next to it.
# With no -Package, uses the newest Phoenix_LAI_*.package in this folder.
# Windows PowerShell 5.1: powershell.exe -NoProfile -ExecutionPolicy Bypass -File Unpack.ps1 [-Package <file>]
param([string]$Package)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if (-not $Package) {
    $newest = Get-ChildItem $root -Filter 'Phoenix_LAI_*.package' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $newest) { throw "No Phoenix_LAI_*.package in $root." }
    $Package = $newest.FullName
}
$Package = (Resolve-Path $Package).Path
$zip = [IO.Path]::ChangeExtension($Package, '.zip')
if (Test-Path $zip) { throw "$zip already exists; move or delete it first." }

$in = [IO.File]::OpenRead($Package); $out = [IO.File]::Create($zip)
try {
    $dec = New-Object Security.Cryptography.CryptoStream($in, (New-Object Security.Cryptography.FromBase64Transform), 'Read')
    $dec.CopyTo($out, 1MB); $dec.Dispose()
} catch { $out.Dispose(); Remove-Item $zip -Force -ErrorAction SilentlyContinue; throw } finally { $in.Dispose(); $out.Dispose() }

# Make sure it is a readable zip.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$a = [IO.Compression.ZipFile]::OpenRead($zip)
try { $n = $a.Entries.Count } finally { $a.Dispose() }
'{0}: {1} files, {2:N1} MB' -f (Split-Path $zip -Leaf), $n, ((Get-Item $zip).Length / 1MB)
