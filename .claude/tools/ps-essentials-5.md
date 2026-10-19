## Essentials

Target: Windows PowerShell 5.1 (powershell.exe), the version built into Windows. Code for 5.1 also runs in 7; the reverse often fails.

| Never in 5.1 (7 only) | Write instead |
|---|---|
| `$a ? 'x' : 'y'` | `if ($a) { 'x' } else { 'y' }` |
| `$v ?? 'default'`, `$v ??= 1` | `if ($null -eq $v) { $v = 'default' }` |
| `cmd1 && cmd2`, `cmd1 \|\| cmd2` | `cmd1; if ($?) { cmd2 }` (exe: `if ($LASTEXITCODE -eq 0)`) |
| `ForEach-Object -Parallel { }` | `Start-Job` / `Wait-Job` / `Receive-Job`, or a runspace pool |
| `ConvertFrom-Json -AsHashtable`, `-Depth` on ConvertFrom-Json | loop `.PSObject.Properties` into `@{}`; ConvertFrom-Json has no -Depth in 5.1 |
| `Get-Content -AsByteStream` | `Get-Content -Encoding Byte -ReadCount 0` or `[IO.File]::ReadAllBytes($p)` |
| `-Encoding utf8NoBOM` | `[IO.File]::WriteAllText($p, $text, (New-Object Text.UTF8Encoding $false))` |
| `$IsWindows`, `$PSStyle`, `` "`e[" `` | Windows only: no test needed; `[char]27` for escape |
| `Join-Path a b c` (several children) | `Join-Path (Join-Path a b) c` or `[IO.Path]::Combine(a, b, c)` |

Always: comparisons are `-eq -ne -gt -lt -ge -le -like -match` (`>` writes a file). `$null` on the left: `$null -eq $x`. Full cmdlet names, no aliases (`%`, `?`, `gci`). `-ErrorAction Stop` (or `$ErrorActionPreference = 'Stop'`) so `try/catch` sees cmdlet errors. Only real parameters: check the command's syntax lines below.

```powershell
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$Days = 30
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
try {
    # work here
}
catch {
    Write-Error ("Failed: " + $_.Exception.Message)
    exit 1
}
```

## Recipes

### Find the largest files in a folder tree (Get-ChildItem -Recurse -File, Sort-Object, size in MB)

**Example**

```powershell
Get-ChildItem -Path 'C:\Data' -Recurse -File -ErrorAction SilentlyContinue |
    Sort-Object -Property Length -Descending |
    Select-Object -First 10 -Property FullName, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 2) } }
```

### Copy files changed recently, keeping the folder structure (LastWriteTime, Substring, New-Item -Force)

**Example**

```powershell
$source = 'C:\Logs'; $target = 'D:\Archive'; $since = (Get-Date).AddHours(-24)
Get-ChildItem -Path $source -Recurse -File -Filter '*.log' | Where-Object { $_.LastWriteTime -ge $since } | ForEach-Object {
    $relative = $_.FullName.Substring($source.Length).TrimStart('\')
    $dest = Join-Path $target $relative
    New-Item -ItemType Directory -Path (Split-Path $dest -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
}
```

### Read a JSON file, get a nested value with a default (ConvertFrom-Json, no ?? in 5.1)

**Example**

```powershell
$config = Get-Content -Path 'C:\App\settings.json' -Raw | ConvertFrom-Json
$dbHost = $null
if ($config.PSObject.Properties['settings'] -and $config.settings.PSObject.Properties['database']) { $dbHost = $config.settings.database.host }
if ([string]::IsNullOrEmpty($dbHost)) { $dbHost = 'localhost' }
Write-Output $dbHost
```

- `-Raw` reads the file as one string; without it ConvertFrom-Json gets one line at a time and fails on multi-line JSON.

### Turn JSON into a hashtable (5.1 has no -AsHashtable)

**Example**

```powershell
function ConvertTo-Hashtable {
    param([Parameter(ValueFromPipeline = $true)]$InputObject)
    process {
        if ($InputObject -is [System.Management.Automation.PSCustomObject]) {
            $h = @{}
            foreach ($p in $InputObject.PSObject.Properties) { $h[$p.Name] = ConvertTo-Hashtable $p.Value }
            return $h
        }
        if ($InputObject -is [System.Collections.IEnumerable] -and $InputObject -isnot [string]) { return @($InputObject | ForEach-Object { ConvertTo-Hashtable $_ }) }
        return $InputObject
    }
}
$h = Get-Content -Path '.\data.json' -Raw | ConvertFrom-Json | ConvertTo-Hashtable
```

### Write a text file as UTF-8 without a BOM (5.1 -Encoding UTF8 always adds a BOM)

**Example**

```powershell
$text = Get-Content -Path '.\in.txt' -Raw
$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path (Get-Location) 'out.txt'), $text, $utf8NoBom)
```

- .NET methods resolve relative paths against the process folder, not `Get-Location`: always pass a full path.

### Do the next step only if the previous one succeeded (5.1 has no && or ||)

**Example**

```powershell
try {
    Copy-Item -Path 'C:\Data\report.csv' -Destination 'D:\Backup\report.csv' -ErrorAction Stop
    Remove-Item -Path 'C:\Data\report.csv' -ErrorAction Stop
}
catch {
    Write-Warning ("Copy failed, original kept: " + $_.Exception.Message)
}
# for an .exe: & robocopy.exe $src $dst /E ; if ($LASTEXITCODE -lt 8) { 'ok' }
```

### Process items in parallel (5.1 has no ForEach-Object -Parallel: Start-Job)

**Example**

```powershell
$servers = 'srv1', 'srv2', 'srv3'
$jobs = foreach ($s in $servers) {
    Start-Job -ArgumentList $s -ScriptBlock { param($name) Test-Connection -ComputerName $name -Count 1 -Quiet }
}
$results = $jobs | Wait-Job | Receive-Job
$jobs | Remove-Job
```

- Jobs run in new processes: pass values with `-ArgumentList` (or `$using:name`); they do not see the caller's variables.

### Export objects to CSV and import them back (Export-Csv -NoTypeInformation, Import-Csv)

**Example**

```powershell
Get-Service | Select-Object -Property Name, DisplayName, Status, StartType |
    Export-Csv -Path 'C:\Temp\services.csv' -NoTypeInformation -Encoding UTF8
$rows = Import-Csv -Path 'C:\Temp\services.csv'
$rows | Where-Object { $_.Status -eq 'Running' } | Measure-Object | Select-Object -ExpandProperty Count
```

- 5.1 writes `#TYPE ...` as the first line unless you pass `-NoTypeInformation`.

### Read a large file line by line without loading it (StreamReader, Get-Content -ReadCount)

**Example**

```powershell
$count = 0
$reader = New-Object System.IO.StreamReader('C:\Logs\big.log')
try {
    while ($null -ne ($line = $reader.ReadLine())) { if ($line -like '*ERROR*') { $count++ } }
}
finally { $reader.Dispose() }
Write-Output $count
```

### Event log errors from the last day (Get-WinEvent -FilterHashtable)

**Example**

```powershell
Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 2; StartTime = (Get-Date).AddDays(-1) } -MaxEvents 50 -ErrorAction SilentlyContinue |
    Select-Object -Property TimeCreated, ProviderName, Id, Message
```

- Level 1 = critical, 2 = error, 3 = warning. `Get-EventLog -LogName System -EntryType Error -Newest 50` also works in 5.1.

### Zip a folder with a dated name (Compress-Archive)

**Example**

```powershell
$stamp = Get-Date -Format 'yyyy-MM-dd'
Compress-Archive -Path 'C:\Reports\*' -DestinationPath ("D:\Backup\reports-" + $stamp + ".zip") -Force
Expand-Archive -Path ("D:\Backup\reports-" + $stamp + ".zip") -DestinationPath 'D:\Restore' -Force
```

### Register a daily scheduled task (New-ScheduledTaskAction, New-ScheduledTaskTrigger, Register-ScheduledTask)

**Example**

```powershell
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File "D:\Scripts\backup.ps1"'
$trigger = New-ScheduledTaskTrigger -Daily -At '2:00 AM'
Register-ScheduledTask -TaskName 'Nightly backup' -Action $action -Trigger $trigger -User 'SYSTEM' -RunLevel Highest -Force
```

### Hash files and save the result as JSON (Get-FileHash, ConvertTo-Json)

**Example**

```powershell
Get-ChildItem -Path 'C:\Drop' -File |
    Get-FileHash -Algorithm SHA256 |
    Select-Object -Property Path, Hash |
    ConvertTo-Json -Depth 3 |
    Set-Content -Path 'C:\Drop\hashes.json' -Encoding UTF8
```

### Disk space per fixed drive (Get-CimInstance Win32_LogicalDisk)

**Example**

```powershell
Get-CimInstance -ClassName Win32_LogicalDisk -Filter 'DriveType = 3' |
    Select-Object -Property DeviceID,
        @{ Name = 'SizeGB'; Expression = { [math]::Round($_.Size / 1GB, 1) } },
        @{ Name = 'FreeGB'; Expression = { [math]::Round($_.FreeSpace / 1GB, 1) } } |
    Format-Table -AutoSize
```

### Is a TCP port listening locally or reachable remotely (Get-NetTCPConnection, Test-NetConnection)

**Example**

```powershell
function Test-PortListening {
    <#
    .SYNOPSIS
    Tells whether a TCP port is in the Listen state on this computer.
    .PARAMETER Port
    The TCP port number.
    #>
    param([Parameter(Mandatory = $true)][int]$Port)
    $c = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    return [bool]$c
}
Test-PortListening -Port 443
Test-NetConnection -ComputerName 'server01' -Port 1433 -InformationLevel Quiet
```

### Replace text in many files with a backup (Get-Content -Raw, -replace, Set-Content)

**Example**

```powershell
Get-ChildItem -Path 'C:\App' -Filter '*.config' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination ($_.FullName + '.bak') -Force
    $text = Get-Content -LiteralPath $_.FullName -Raw
    $text = $text -replace [regex]::Escape('old-server'), 'new-server'
    Set-Content -LiteralPath $_.FullName -Value $text -Encoding UTF8 -NoNewline
}
```

- `-replace` takes a regular expression: escape literal text with `[regex]::Escape()`.
