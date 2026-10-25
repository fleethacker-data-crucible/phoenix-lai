# Phoenix LAI client: the chat window for OpenAI /v1 servers on this computer (the Phoenix server).
# Connections are added in the left panel (+); their API keys go to Windows Credential Manager.
# It runs no model itself. Windows PowerShell 5.1 only (same checks and in-memory compile as run.ps1).
#   powershell.exe -NoProfile -STA -WindowStyle Hidden -File src\run-phoenix-client.ps1
# First run: create or repair Phoenix_Client.lnk and Phoenix_LAI_Server.lnk in the project root (made on this
# computer, never tracked). Best effort: a missing or blocked WScript.Shell never stops the launch.
try { Import-Module (Join-Path $PSScriptRoot 'PhoenixShortcuts.psm1') -Force; $null = Sync-PhoenixShortcut -Root (Split-Path -Parent $PSScriptRoot) } catch { $null = $_ }
& (Join-Path $PSScriptRoot 'run.ps1') -Role Client
exit $LASTEXITCODE
