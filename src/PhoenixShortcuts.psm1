# Phoenix LAI shortcuts: Phoenix_Client.lnk and Phoenix_LAI_Server.lnk in the project root.
# They are made on this computer (never tracked in git: a .lnk file records the computer name and the user's SID).
# Phoenix_LAI.bat, run-phoenix-client.ps1 and run-phoenix-server.ps1 call Sync-PhoenixShortcut, which creates a
# shortcut when it is missing and repairs it when it differs from Get-PhoenixShortcutSpec. No admin: the files go
# in the project folder the user already owns. Windows PowerShell 5.1.
#
# How the shortcuts work (no absolute project path inside them, so the folder can be moved anywhere):
#  - Target is the Windows console host in headless mode: conhost.exe --headless powershell.exe ... -> no window.
#  - When Windows starts a program from a shortcut it passes the shortcut's own path in STARTUPINFO.lpTitle
#    (STARTF_TITLEISLINKNAME). The short encoded command reads it and runs the script in src\ next to the shortcut.

$script:Shortcuts = @(
    @{ Name = 'Phoenix_Client.lnk'; Script = 'run-phoenix-client.ps1'; Description = 'Phoenix LAI client (chat with OpenAI /v1 connections)'; Icon = 13 },
    @{ Name = 'Phoenix_LAI_Server.lnk'; Script = 'run-phoenix-server.ps1'; Description = 'Phoenix LAI server (models + OpenAI API, tray)'; Icon = 15 }
)

# The encoded command for one script in src\ (STARTUPINFOW on x64: lpTitle is the pointer at byte offset 24).
function Get-PhoenixShortcutCommand {
    param([Parameter(Mandatory)][string]$Script)
    if ($Script -notmatch '^[A-Za-z0-9_.-]+\.ps1$') { throw "Not a script name in src\: $Script" }
    return 'Add-Type ''public class S{[System.Runtime.InteropServices.DllImport("kernel32")]public static extern void GetStartupInfoW(byte[] b);}'';' +
        '$b=New-Object byte[] 104;[S]::GetStartupInfoW($b);' +
        '$t=[Runtime.InteropServices.Marshal]::PtrToStringUni([IntPtr][BitConverter]::ToInt64($b,24));' +
        '& (Join-Path (Split-Path $t) ''src\' + $Script + ''')'
}

<#
  The shortcuts Phoenix needs in $Root (the project root), as plain objects:
  Name, Path, TargetPath, Arguments, WorkingDirectory, Description, IconLocation, WindowStyle.
  Only Path depends on $Root; nothing inside the shortcut does.
#>
function Get-PhoenixShortcutSpec {
    param([Parameter(Mandatory)][string]$Root, [string[]]$Name)
    foreach ($s in $script:Shortcuts) {
        if ($Name -and $Name -notcontains $s.Name) { continue }
        $enc = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes((Get-PhoenixShortcutCommand -Script $s.Script)))
        $arguments = '--headless %SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand ' + $enc
        if ($arguments.Length -gt 1023) { throw "Shortcut arguments too long ($($arguments.Length))." }
        [pscustomobject]@{
            Name             = $s.Name
            Path             = [IO.Path]::Combine($Root, $s.Name)
            TargetPath       = '%SystemRoot%\System32\conhost.exe'
            Arguments        = $arguments
            WorkingDirectory = ''
            Description      = $s.Description
            IconLocation     = '%SystemRoot%\System32\shell32.dll,' + $s.Icon
            WindowStyle      = 1
        }
    }
}

# Expands %VARS% and compares case-insensitively (WScript.Shell returns some fields expanded).
function Test-PhoenixShortcutValue([string]$Have, [string]$Want) {
    $a = [Environment]::ExpandEnvironmentVariables(('' + $Have)).Trim()
    $b = [Environment]::ExpandEnvironmentVariables(('' + $Want)).Trim()
    return [string]::Equals($a, $b, [StringComparison]::OrdinalIgnoreCase)
}

# True when a WScript.Shell shortcut object already matches the spec.
function Test-PhoenixShortcutMatch {
    param([Parameter(Mandatory)]$Shortcut, [Parameter(Mandatory)]$Spec)
    foreach ($f in 'TargetPath', 'Arguments', 'WorkingDirectory', 'Description', 'IconLocation') {
        if (-not (Test-PhoenixShortcutValue $Shortcut.$f $Spec.$f)) { return $false }
    }
    return ([int]$Shortcut.WindowStyle -eq [int]$Spec.WindowStyle)
}

<#
  Creates each shortcut when missing and repairs it when it differs; leaves a matching one alone (no write).
  Returns one object per shortcut: Name, Path, State = created | repaired | unchanged | unavailable, Error.
  Never throws: 'unavailable' means WScript.Shell (COM) could not be used, and the caller just goes on.
  -OpenShortcut is a test hook for the WScript.Shell CreateShortcut call (it opens an existing file too).
#>
function Sync-PhoenixShortcut {
    param(
        [Parameter(Mandatory)][string]$Root,
        [string[]]$Name,
        [scriptblock]$OpenShortcut = { param($p) (New-Object -ComObject WScript.Shell).CreateShortcut($p) }
    )
    foreach ($spec in @(Get-PhoenixShortcutSpec -Root $Root -Name $Name)) {
        $state = 'unavailable'; $err = $null
        try {
            $existed = Test-Path -LiteralPath $spec.Path
            $lnk = & $OpenShortcut $spec.Path
            if (-not $lnk) { throw 'WScript.Shell returned no shortcut.' }
            if ($existed -and (Test-PhoenixShortcutMatch -Shortcut $lnk -Spec $spec)) {
                $state = 'unchanged'
            } else {
                $lnk.TargetPath = $spec.TargetPath
                $lnk.Arguments = $spec.Arguments
                $lnk.WorkingDirectory = $spec.WorkingDirectory
                $lnk.Description = $spec.Description
                $lnk.IconLocation = $spec.IconLocation
                $lnk.WindowStyle = $spec.WindowStyle
                $lnk.Save()
                if ($existed) { $state = 'repaired' } else { $state = 'created' }
            }
        } catch { $state = 'unavailable'; $err = $_.Exception.Message }
        [pscustomobject]@{ Name = $spec.Name; Path = $spec.Path; State = $state; Error = $err }
    }
}

Export-ModuleMember -Function Get-PhoenixShortcutCommand, Get-PhoenixShortcutSpec, Test-PhoenixShortcutMatch, Sync-PhoenixShortcut
