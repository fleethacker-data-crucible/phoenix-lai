# Stops only this checkout's Phoenix Client through its verified loopback identity. Never stops the model server.
param([string]$Root = (Split-Path -Parent $PSScriptRoot), [int]$WaitSeconds = 30)
$ErrorActionPreference = 'Stop'
$rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\')
$logs = Join-Path $rootPath 'logs'
if (-not (Test-Path -LiteralPath $logs)) { return }
foreach ($file in @(Get-ChildItem -LiteralPath $logs -Filter 'phoenix-client-*.json' -File)) {
    $info = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ($info.role -ne 'Client' -or [IO.Path]::GetFullPath([string]$info.root).TrimEnd('\') -ne $rootPath) { continue }
    $clientPid = [int]$info.pid
    if ($clientPid -eq $PID) { continue }
    $process = Get-CimInstance Win32_Process -Filter "ProcessId=$clientPid" -ErrorAction SilentlyContinue
    if (-not $process) { Remove-Item -LiteralPath $file.FullName -Force; continue }
    $launcher = Join-Path $rootPath 'src\run-phoenix-client.ps1'
    $direct = Join-Path $rootPath 'src\run.ps1'
    $commandLine = [string]$process.CommandLine
    if ($commandLine -match '-EncodedCommand\s+(\S+)') {
        try { $commandLine += ' ' + [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($Matches[1])) } catch { }
    }
    $owns = $commandLine.IndexOf($launcher, [StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        ($commandLine.IndexOf($direct, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $commandLine -match '-Role\s+["'']?Client\b')
    if (-not $owns) { throw "Client PID $clientPid no longer matches this checkout; refusing to stop it." }
    $port = [int]$info.port
    if ($port -lt 1 -or $port -gt 65535) { throw 'Invalid client runtime port.' }
    $base = "http://127.0.0.1:$port"
    $id = Invoke-RestMethod -Uri "$base/__fleethacker/id" -TimeoutSec 3
    if ($id.tool -ne 'phoenix-lai-client' -or [int]$id.pid -ne $clientPid) { throw 'Client identity changed; refusing to stop it.' }
    $null = Invoke-RestMethod -Uri "$base/api/quit" -Method Post -ContentType 'application/json' -Body '{}' -TimeoutSec 3
    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Process -Id $clientPid -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 250 }
    if (Get-Process -Id $clientPid -ErrorAction SilentlyContinue) { throw 'The client is still finishing work; replacement cancelled. No process was force-stopped.' }
    Write-Host "Stopped Phoenix Client PID $clientPid."
}
