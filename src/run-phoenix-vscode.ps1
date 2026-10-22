# Phoenix LAI VS Code client: the Phoenix Client for the VS Code extension, started by the extension itself (approved
# 2026-10-22). Same providers, keys, settings and chats as the Phoenix Client window, but its own process and port, no
# window; it stops by itself about 30 s after the last VS Code chat is gone. Windows PowerShell 5.1 only.
#   powershell.exe -NoProfile -STA -WindowStyle Hidden -File src\run-phoenix-vscode.ps1
& (Join-Path $PSScriptRoot 'run.ps1') -Role VsCode
exit $LASTEXITCODE
