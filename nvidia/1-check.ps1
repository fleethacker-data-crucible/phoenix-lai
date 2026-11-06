# Step 1 of the NVIDIA test: can this machine run Phoenix at all? Read-only: it changes no setting, installs nothing
# and makes no network call. Takes under a minute.
#   powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File nvidia\1-check.ps1
# The window stays open at the end until Enter is pressed (-NoPause skips that, for automated runs).
param([switch]$NoPause)

$ErrorActionPreference = 'Continue'
# Everything this window shows also goes to nvidia\logs\<script>-<date>-<time>.log
$logDir = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir ('{0}-{1}.log' -f [IO.Path]::GetFileNameWithoutExtension($MyInvocation.MyCommand.Name), (Get-Date -Format 'yyyyMMdd-HHmmss'))
try { Start-Transcript -Path $logFile | Out-Null } catch { }
function Close-Script([int]$code) {
    Write-Host ''; Write-Host "Log saved: $logFile"
    try { Stop-Transcript | Out-Null } catch { }
    if (-not $NoPause) { Write-Host ''; Read-Host 'Press Enter to close' | Out-Null }
    exit $code
}
trap { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; Close-Script 1 }
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
$results = Join-Path $here 'results'
New-Item -ItemType Directory -Force -Path $results | Out-Null
$report = Join-Path $results 'check.txt'
$lines = New-Object System.Collections.Generic.List[string]
$problems = New-Object System.Collections.Generic.List[string]

function Say([string]$s) { $lines.Add($s); Write-Host $s }
function Section([string]$s) { Say ''; Say "=== $s" }
function Problem([string]$s) { $problems.Add($s); Say "!!! $s" }
function Try-Get([scriptblock]$b) { try { & $b } catch { "(error: $($_.Exception.Message))" } }

Say "Phoenix NVIDIA check, $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') on $env:COMPUTERNAME"
Say "Phoenix folder: $root"

Section 'Machine'
$cs = Get-CimInstance Win32_ComputerSystem
$os = Get-CimInstance Win32_OperatingSystem
Say "model: $($cs.Manufacturer) $($cs.Model)"
Say "Windows: $($os.Caption) $($os.Version) build $($os.BuildNumber), $($os.OSArchitecture)"
Say ("RAM: {0:N1} GB total, {1:N1} GB free" -f ($cs.TotalPhysicalMemory / 1GB), ($os.FreePhysicalMemory * 1KB / 1GB))
Say ("commit (RAM + page file): {0:N1} GB total, {1:N1} GB free" -f ($os.TotalVirtualMemorySize * 1KB / 1GB), ($os.FreeVirtualMemory * 1KB / 1GB))
Get-CimInstance Win32_Processor | ForEach-Object { Say "CPU: $($_.Name.Trim()), $($_.NumberOfCores) cores / $($_.NumberOfLogicalProcessors) threads" }
Get-CimInstance Win32_LogicalDisk -Filter "DriveType=3" | ForEach-Object { Say ("disk {0} {1:N0} GB free of {2:N0} GB" -f $_.DeviceID, ($_.FreeSpace / 1GB), ($_.Size / 1GB)) }
$bat = Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue
if ($bat) { Say "battery: $($bat.EstimatedChargeRemaining)%, status $($bat.BatteryStatus) (2 = on AC power, 1 = on battery)"; if ($bat.BatteryStatus -eq 1) { Problem 'Running on battery: plug in the charger before testing (laptop GPUs slow down a lot on battery).' } }
Say "power plan: $(Try-Get { (powercfg.exe /getactivescheme) -join ' ' })"

Section 'Graphics cards (Windows)'
Get-CimInstance Win32_VideoController | ForEach-Object {
    Say "$($_.Name): driver $($_.DriverVersion) dated $($_.DriverDate), status $($_.Status), $($_.CurrentHorizontalResolution)x$($_.CurrentVerticalResolution), PNP $($_.PNPDeviceID)"
}
# The real VRAM size (Win32_VideoController.AdapterRAM stops at 4 GB)
$cls = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
Get-ChildItem $cls -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^\d{4}$' } | ForEach-Object {
    $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
    if ($p.DriverDesc) {
        $mem = $p.'HardwareInformation.qwMemorySize'
        Say ("  {0}: VRAM {1}, driver {2}" -f $p.DriverDesc, $(if ($mem) { '{0:N1} GB' -f ($mem / 1GB) } else { 'shared (no dedicated memory reported)' }), $p.DriverVersion)
    }
}
$nv = @(Get-CimInstance Win32_VideoController | Where-Object { $_.Name -match 'NVIDIA' })
if ($nv.Count -eq 0) { Problem 'No NVIDIA graphics card is visible to Windows. Phoenix will use whatever GPU Vulkan offers (see the probe), or the CPU.' }
$smi = Join-Path $env:SystemRoot 'System32\nvidia-smi.exe'
if (Test-Path $smi) { Say ''; Say 'nvidia-smi:'; & $smi --query-gpu=name,driver_version,memory.total,memory.used,pstate,power.limit,clocks.max.sm,pcie.link.gen.max --format=csv 2>&1 | ForEach-Object { Say "  $_" } }
else { Say '(nvidia-smi not found: normal on some managed laptops)' }

Section 'Vulkan'
$vk = Join-Path $env:SystemRoot 'System32\vulkan-1.dll'
if (Test-Path $vk) { $v = (Get-Item $vk).VersionInfo; Say "vulkan-1.dll: $($v.FileVersion) ($($v.CompanyName))" } else { Problem 'C:\Windows\System32\vulkan-1.dll is missing: the graphics driver did not install Vulkan. Phoenix would run on the CPU only (very slow).' }
foreach ($k in 'HKLM:\SOFTWARE\Khronos\Vulkan\Drivers', 'HKLM:\SOFTWARE\WOW6432Node\Khronos\Vulkan\Drivers') {
    $item = Get-Item $k -ErrorAction SilentlyContinue
    if ($item) { $item.Property | ForEach-Object { Say "  driver manifest: $_" } }
}
Get-ChildItem $cls -ErrorAction SilentlyContinue | ForEach-Object {
    $p = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
    if ($p.VulkanDriverName) { Say "  $($p.DriverDesc) Vulkan driver: $($p.VulkanDriverName -join ', ')" }
}
$gd = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers' -ErrorAction SilentlyContinue
Say "GPU timeout (TDR): TdrDelay $(if ($gd.TdrDelay) { $gd.TdrDelay } else { 'default 2' }) s, TdrLevel $(if ($null -ne $gd.TdrLevel) { $gd.TdrLevel } else { 'default 3' })"
$pref = Get-ItemProperty 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences' -ErrorAction SilentlyContinue
if ($pref) { $pref.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object { Say "  graphics preference: $($_.Name) = $($_.Value)" } }
else { Say '  graphics preference: none set (Windows decides per app)' }

Section 'PowerShell and the C# compiler (Phoenix compiles itself at every start)'
Say "PowerShell $($PSVersionTable.PSVersion), language mode $($ExecutionContext.SessionState.LanguageMode)"
if ($ExecutionContext.SessionState.LanguageMode -ne 'FullLanguage') { Problem "PowerShell is in $($ExecutionContext.SessionState.LanguageMode) mode (AppLocker / WDAC). Phoenix cannot compile itself in this mode." }
Get-ExecutionPolicy -List | ForEach-Object { Say "  execution policy $($_.Scope): $($_.ExecutionPolicy)" }
$rel = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
Say ".NET Framework release $rel $(if ($rel -ge 528040) { '(4.8 or newer)' } else { '(older than 4.8)' })"
if (-not $rel -or $rel -lt 528040) { Problem '.NET Framework 4.8 is not installed.' }
try {
    $cp = New-Object System.CodeDom.Compiler.CompilerParameters
    $cp.GenerateInMemory = $true
    $cp.CompilerOptions = '/unsafe /optimize+'
    Add-Type -CompilerParameters $cp -TypeDefinition 'public static unsafe class PhoenixCompileTest { public static int Run() { int x = 41; int* p = &x; return *p + 1; } }'
    if ([PhoenixCompileTest]::Run() -eq 42) { Say 'C# compiler: OK (unsafe code compiles and runs)' } else { Problem 'C# compiler: test code gave a wrong answer' }
} catch { Problem "C# compiler blocked: $($_.Exception.Message)  (Phoenix needs csc.exe from .NET Framework; AppLocker or antivirus may block it)" }

Section 'Loopback ports (Phoenix serves its own window on 127.0.0.1)'
foreach ($port in 8780, 8790, 1893) {   # first server, client and API ports (edge-ops PORTS.md)
    $l = New-Object System.Net.HttpListener
    $l.Prefixes.Add("http://127.0.0.1:$port/")
    try { $l.Start(); Say "127.0.0.1:${port}: free and usable"; $l.Stop() }
    catch { Problem "127.0.0.1:${port} cannot be opened: $($_.Exception.Message) (is Phoenix already running?)" }
    finally { $l.Close() }
}

Section 'Browser for the Phoenix window'
$found = $false
foreach ($b in @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe", "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe", "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe")) {
    if (Test-Path $b) { Say "$b  $((Get-Item $b).VersionInfo.ProductVersion)"; $found = $true }
}
if (-not $found) { Problem 'Neither Chrome nor Edge was found: Phoenix has no window to open.' }

Section 'Folders'
$isAdmin = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Say "running as $env:USERNAME, elevated (admin): $isAdmin  (Phoenix needs no admin rights)"
# The Phoenix folder itself takes logs\ and nvidia\results\, so it must be writable too (not under Program Files)
foreach ($d in (Join-Path $root 'logs'), $results, "$env:USERPROFILE\Phoenix_Projects", "$env:USERPROFILE\.phoenix", "$env:LOCALAPPDATA\LocalLLMStudio") {
    try {
        New-Item -ItemType Directory -Force -Path $d | Out-Null
        $t = Join-Path $d ".write-test-$PID"
        Set-Content -Path $t -Value 'x'; Remove-Item $t
        Say "$d  writable"
    } catch { Problem "$d is not writable: $($_.Exception.Message)" }
}
$gp = "$env:LOCALAPPDATA\LocalLLMStudio\gpu-profile.txt"
Say "GPU profile file: $(if (Test-Path $gp) { (Get-Content $gp -Raw).Trim() } else { 'not set (auto)' })"

Section 'Models'
Get-ChildItem (Join-Path $root 'models') -Filter *.gguf -ErrorAction SilentlyContinue | ForEach-Object { Say ("{0}  {1:N2} GB" -f $_.Name, ($_.Length / 1GB)) }
foreach ($m in 'gemma-4-E4b_q4_0-it.gguf', 'embeddinggemma-2-Q8_0.gguf') { if (-not (Test-Path (Join-Path $root "models\$m"))) { Problem "models\$m is missing." } }

Section 'Phoenix locality checks (src\run.ps1 -CheckOnly)'
$out = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'src\run.ps1') -CheckOnly 2>&1
$out | ForEach-Object { Say "  $_" }
if ($LASTEXITCODE -ne 0) { Problem "src\run.ps1 -CheckOnly failed (exit $LASTEXITCODE)." }

Section 'Summary'
if ($problems.Count -eq 0) { Say 'No problems found. Next: close Phoenix and run nvidia\2-probe.ps1.' }
else { Say "$($problems.Count) problem(s):"; $problems | ForEach-Object { Say "  - $_" } }
$lines | Set-Content -Encoding UTF8 $report
Write-Host ''
Write-Host "Saved to $report"
Close-Script $problems.Count
