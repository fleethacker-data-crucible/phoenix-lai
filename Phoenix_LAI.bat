@echo off
rem Phoenix LAI: creates or repairs Phoenix_LAI_Server.lnk and Phoenix_Client.lnk in this folder, then starts the
rem server and the client through them. Use -NoStart to only make the shortcuts. No admin rights are needed.
%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0src\Start-PhoenixLAI.ps1" %*
