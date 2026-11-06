# Packs the Phoenix VS Code extension into ..\vscode-ext\phoenixlai-<version>.vsix. Windows PowerShell 5.1, no
# Node, no npm, no vsce: a .vsix is a zip with a manifest. Install it with: code --install-extension <file>.vsix
#   powershell.exe -NoProfile -File vscode-ext-source\build.ps1
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$outDir = Join-Path (Split-Path -Parent $here) 'vscode-ext'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$pkg = Get-Content (Join-Path $here 'package.json') -Raw | ConvertFrom-Json
$name = $pkg.name; $version = $pkg.version; $publisher = $pkg.publisher

# Icon: a plain triangle pointing up.
$icon = Join-Path $here 'icon.png'
$bmp = New-Object System.Drawing.Bitmap 128, 128
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
$pts = [System.Drawing.PointF[]]@((New-Object System.Drawing.PointF 64, 14), (New-Object System.Drawing.PointF 118, 112), (New-Object System.Drawing.PointF 10, 112))
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 106, 0))
$g.FillPolygon($brush, $pts)
$g.Dispose(); $brush.Dispose()
$bmp.Save($icon, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()

$files = @('package.json', 'extension.js', 'icon.png', 'README.md', 'media/triangle.svg')
foreach ($f in $files) { if (-not (Test-Path (Join-Path $here $f))) { throw "missing $f" } }

# Locality check: the extension may only reach 127.0.0.1:1893.
$js = Get-Content (Join-Path $here 'extension.js') -Raw
if ($js -match 'https?://(?!127\.0\.0\.1|\$\{HOST\})' -or $js -notmatch 'const HOST = "127\.0\.0\.1";' -or $js -match 'require\("(https|net|tls|dgram)"\)') { throw 'extension.js references a non-loopback URL or a forbidden module' }
# child_process only to start its own client (approved 2026-10-22): one spawn, of powershell.exe on run-phoenix-vscode.ps1, no shell.
if ([regex]::Matches($js, '\bcp\.\w+\(').Count -ne 1 -or $js -notmatch 'cp\.spawn\(ps, \[[^\]]*"-File", script\]' -or
    $js -notmatch '"run-phoenix-vscode\.ps1"' -or $js -notmatch '"powershell\.exe"\);' -or $js -match 'shell:\s*true|exec(File|Sync)?\(|fork\(') {
    throw 'extension.js may only spawn powershell.exe on src\run-phoenix-vscode.ps1'
}

$manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011" xmlns:d="http://schemas.microsoft.com/developer/vsx-schema-design/2011">
  <Metadata>
    <Identity Language="en-US" Id="$name" Version="$version" Publisher="$publisher" />
    <DisplayName>$($pkg.displayName)</DisplayName>
    <Description xml:space="preserve">$($pkg.description)</Description>
    <Tags>AI,Chat</Tags>
    <Categories>AI,Chat</Categories>
    <GalleryFlags>Public</GalleryFlags>
    <Properties>
      <Property Id="Microsoft.VisualStudio.Code.Engine" Value="$($pkg.engines.vscode)" />
      <Property Id="Microsoft.VisualStudio.Code.ExtensionKind" Value="ui" />
    </Properties>
    <Icon>extension/icon.png</Icon>
  </Metadata>
  <Installation>
    <InstallationTarget Id="Microsoft.VisualStudio.Code" />
  </Installation>
  <Dependencies />
  <Assets>
    <Asset Type="Microsoft.VisualStudio.Code.Manifest" Path="extension/package.json" Addressable="true" />
    <Asset Type="Microsoft.VisualStudio.Services.Content.Details" Path="extension/README.md" Addressable="true" />
    <Asset Type="Microsoft.VisualStudio.Services.Icons.Default" Path="extension/icon.png" Addressable="true" />
  </Assets>
</PackageManifest>
"@
$types = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension=".json" ContentType="application/json" />
  <Default Extension=".js" ContentType="application/javascript" />
  <Default Extension=".md" ContentType="text/markdown" />
  <Default Extension=".png" ContentType="image/png" />
  <Default Extension=".svg" ContentType="image/svg+xml" />
  <Default Extension=".vsixmanifest" ContentType="text/xml" />
</Types>
"@

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory $outDir | Out-Null }
$vsix = Join-Path $outDir "$name-$version.vsix"
if (Test-Path $vsix) { Remove-Item $vsix }
$zip = [System.IO.Compression.ZipFile]::Open($vsix, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    $utf8 = New-Object System.Text.UTF8Encoding $false
    foreach ($e in @(@('extension.vsixmanifest', $manifest), @('[Content_Types].xml', $types))) {
        $w = New-Object System.IO.StreamWriter ($zip.CreateEntry($e[0]).Open()), $utf8
        $w.Write($e[1]); $w.Dispose()
    }
    foreach ($f in $files) { [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $here $f), "extension/$f") }
}
finally { $zip.Dispose() }
Write-Host "Built $vsix"
