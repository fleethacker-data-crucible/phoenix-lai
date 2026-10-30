# No GPU, model, browser, live provider or user settings are needed. Compile the complete C# 5 source set in memory.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.CompilerOptions = '/unsafe /optimize+'
foreach ($r in 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll') { [void]$cp.ReferencedAssemblies.Add($r) }
Add-Type -Path @(Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object FullName) -CompilerParameters $cp
$script:checks = 0
function Check($label, $actual, $expected) {
    if ($actual -ne $expected) { throw "$label expected $expected; got $actual" }
    $script:checks++
}
$now = [datetime]::UtcNow
$p = New-Object Phoenix.Presence($now)
Check 'startup grace' ($p.Gone($now.AddSeconds(59))) $false
Check 'failed first contact' ($p.Gone($now.AddSeconds(60))) $true
$p.Beat('client-1',$now,$false)
Check 'visible window alive' ($p.Gone($now.AddSeconds(19))) $false
Check 'lost visible window expires' ($p.Gone($now.AddSeconds(21))) $true
$p.Beat('client-2',$now,$true)
$p.Beat('client-2',$now.AddSeconds(5)) # legacy status poll preserves hidden state
Check 'minimized heartbeat grace' ($p.Gone($now.AddSeconds(70))) $false
$p.Leave('client-2',$now.AddSeconds(70))
Check 'leaving grace' ($p.Gone($now.AddSeconds(74))) $false
Check 'closed window expires' ($p.Gone($now.AddSeconds(76))) $true
$p.Beat('reload-1',$now,$false);$p.Leave('reload-1',$now.AddSeconds(1));$p.Beat('reload-2',$now.AddSeconds(2),$false)
Check 'reload keeps client alive' ($p.Gone($now.AddSeconds(7))) $false
Check 'busy quit waits' ([Phoenix.Presence]::Decide($true,$true,$true,'save')) 'wait'
$decide = [Phoenix.Presence].GetMethod('Decide')
# Reflection preserves a CLR null; PowerShell's normal string binding converts $null to an empty string.
Check 'quit after save' ($decide.Invoke($null,@($true,$true,$true,$null))) 'stop'
Check 'stay disables automatic stop' ($decide.Invoke($null,@($false,$false,$true,$null))) 'run'
Check 'explicit quit overrides stay' ($decide.Invoke($null,@($true,$false,$false,$null))) 'stop'
Check 'invalid page ID' ([Phoenix.Presence]::ValidId('../bad')) $false

# Exercise the real route method without starting a listener or touching a user profile.
$server = [Runtime.Serialization.FormatterServices]::GetUninitializedObject([Phoenix.WebServer])
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
[Phoenix.WebServer].GetField('_clientPresence',$flags).SetValue($server,(New-Object Phoenix.Presence($now)))
[Phoenix.WebServer].GetField('_pages',$flags).SetValue($server,(New-Object 'System.Collections.Generic.Dictionary[string,datetime]'))
$method = [Phoenix.WebServer].GetMethod('ClientLifecycleApi',$flags)
$body = New-Object 'System.Collections.Generic.Dictionary[string,object]'
$body['hidden'] = $false
$reply = $method.Invoke($server,@('/api/heartbeat',$true,$body.PSObject.BaseObject,'window-1')) | ConvertFrom-Json
Check 'heartbeat accepted' $reply.autoStop $false # uninitialized object; field initializers are deliberately absent
$rejected = $false
try { $null = $method.Invoke($server,@('/api/quit',$false,$body.PSObject.BaseObject,'window-1')) } catch { $rejected = $true }
Check 'GET cannot quit' $rejected $true
$reply = $method.Invoke($server,@('/api/quit',$true,$body.PSObject.BaseObject,'window-1')) | ConvertFrom-Json
Check 'POST requests quit' $reply.stopping $true
Check 'quit does not signal independent model server' $server.ShutdownRequested $false
Write-Host "PASS: $script:checks client lifecycle checks; full C# source compiled with Windows PowerShell $($PSVersionTable.PSVersion)."
