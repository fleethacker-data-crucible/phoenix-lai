# DEV TOOL. Creates a shortcut in <root> that runs a script in src\ with no window at all and works from wherever the
# project folder is placed.
#   powershell.exe -NoProfile -File .claude\tools\make-shortcut.ps1 [-Root <project root>]      # Phoenix_Client.lnk -> the client
#   ... make-shortcut.ps1 -Script run-phoenix-server.ps1 -Name Phoenix_LAI_Server.lnk -Description 'Phoenix LAI server'
# The server and the client are separate programs: there is no all-in-one shortcut.
#
# How it works (no scripts besides PowerShell, no absolute project paths in the shortcut):
#  - Target is the Windows console host in headless mode: conhost.exe --headless powershell.exe ... -> no window.
#  - When Windows starts a program from a shortcut it passes the shortcut's own path in STARTUPINFO.lpTitle
#    (STARTF_TITLEISLINKNAME). The tiny encoded command reads it and runs src\run.ps1 next to the shortcut.
param([string]$Root, [string]$Script = 'run-phoenix-client.ps1', [string]$Name = 'Phoenix_Client.lnk', [string]$Description = 'Phoenix LAI client', [int]$Icon = 13)
$ErrorActionPreference = 'Stop'
if (-not $Root) { $Root = (Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\..')).Path }

# STARTUPINFOW on x64: lpTitle is the pointer at byte offset 24.
$code = 'Add-Type ''public class S{[System.Runtime.InteropServices.DllImport("kernel32")]public static extern void GetStartupInfoW(byte[] b);}'';' +
        '$b=New-Object byte[] 104;[S]::GetStartupInfoW($b);' +
        '$t=[Runtime.InteropServices.Marshal]::PtrToStringUni([IntPtr][BitConverter]::ToInt64($b,24));' +
        '& (Join-Path (Split-Path $t) ''src\' + $Script + ''')'
$enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
$args = '--headless %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand ' + $enc
if ($args.Length -gt 1023) { throw "Shortcut arguments too long ($($args.Length))." }

$sh = New-Object -ComObject WScript.Shell
$lnkPath = Join-Path $Root $Name
$lnk = $sh.CreateShortcut($lnkPath)
$lnk.TargetPath = '%SystemRoot%\System32\conhost.exe'
$lnk.Arguments = $args
$lnk.WorkingDirectory = ''
$lnk.Description = $Description
$lnk.IconLocation = '%SystemRoot%\System32\shell32.dll,' + $Icon
$lnk.Save()
"created $lnkPath (arguments $($args.Length) chars)"
