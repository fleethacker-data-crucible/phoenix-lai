@echo off
rem Launches Local LLM Studio with no console window (Windows PowerShell 5.1).
start "" powershell.exe -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0run.ps1"
