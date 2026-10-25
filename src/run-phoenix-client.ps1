# Phoenix LAI client: the chat window for OpenAI /v1 servers on this computer (the Phoenix server).
# Connections are added in the left panel (+); their API keys go to Windows Credential Manager.
# It runs no model itself. Windows PowerShell 5.1 only (same checks and in-memory compile as run.ps1).
#   powershell.exe -NoProfile -STA -WindowStyle Hidden -File src\run-phoenix-client.ps1
& (Join-Path $PSScriptRoot 'run.ps1') -Role Client
exit $LASTEXITCODE
