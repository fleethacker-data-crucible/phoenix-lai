## Essentials

Target: PowerShell 7.4+ (pwsh.exe), only when the user asks for 7, pwsh or PowerShell Core. For Windows PowerShell 5.1 (built into Windows) use the 5.1 sheet: none of the 7-only features below exist there.

| 7 adds (fine here) | Note |
|---|---|
| `$a ? 'x' : 'y'`, `$v ?? 'default'`, `$v ??= 1`, `${obj}?.Prop` | null-conditional member access needs braces: `${obj}?.Name` |
| `cmd1 && cmd2`, `cmd1 \|\| cmd2` | pipeline chain operators, by success of the previous pipeline |
| `ForEach-Object -Parallel { } -ThrottleLimit 4` | the block runs in other runspaces: read caller variables with `$using:name` |
| `ConvertFrom-Json -AsHashtable -Depth 64` | |
| `Get-Content -AsByteStream`, `-Encoding utf8NoBOM` (UTF-8 without BOM is the default) | `-Encoding Byte` no longer exists |
| `$IsWindows`, `$IsLinux`, `$IsMacOS` | test before using Windows-only cmdlets |
| `Join-Path a b c`, `` "`e[31m" ``, `$PSStyle` | |

Gone in 7 (5.1 only): `Get-WmiObject` (use `Get-CimInstance`), `-Encoding Byte`, many Windows-only modules unless the Windows compatibility layer loads them. Always: comparisons are `-eq -lt -like -match`; `-ErrorAction Stop` before `try/catch`; full cmdlet names; only parameters that exist (check the syntax lines below).

## Recipes

### Process items in parallel with a throttle (ForEach-Object -Parallel, $using:)

**Example**

```powershell
$prefix = 'host'
1..20 | ForEach-Object -Parallel {
    $name = $using:prefix + $_
    [pscustomobject]@{ Name = $name; Ok = (Test-Connection -TargetName $name -Count 1 -Quiet -TimeoutSeconds 1) }
} -ThrottleLimit 5
```

### Read JSON into a hashtable and default missing values (-AsHashtable, ??)

**Example**

```powershell
$cfg = Get-Content -Path './settings.json' -Raw | ConvertFrom-Json -AsHashtable
$dbHost = $cfg['settings']?['database']?['host'] ?? 'localhost'
Write-Output $dbHost
```

### Chain steps by success (&& and ||)

**Example**

```powershell
Copy-Item -Path './report.csv' -Destination './backup/report.csv' -ErrorAction Stop && Remove-Item -Path './report.csv'
Test-Path -Path './config.json' || Write-Warning 'config.json is missing'
```

### Read and write bytes and UTF-8 text (-AsByteStream, utf8NoBOM)

**Example**

```powershell
$bytes = Get-Content -Path './image.png' -AsByteStream -TotalCount 8
($bytes | ForEach-Object { $_.ToString('X2') }) -join ' '
Set-Content -Path './notes.txt' -Value 'hello' -Encoding utf8NoBOM
```

### Cross-platform paths and OS checks ($IsWindows, Join-Path with several children)

**Example**

```powershell
$base = if ($IsWindows) { $env:LOCALAPPDATA } else { Join-Path $HOME '.local' 'share' }
$logDir = Join-Path $base 'MyApp' 'logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
```
