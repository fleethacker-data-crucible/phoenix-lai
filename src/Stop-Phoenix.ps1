# Compatibility name from the former combined app. Stop only the client; the independent model server stays up.
param([string]$Root = (Split-Path -Parent $PSScriptRoot), [int]$WaitSeconds = 30)
& (Join-Path $PSScriptRoot 'Stop-PhoenixClient.ps1') -Root $Root -WaitSeconds $WaitSeconds
