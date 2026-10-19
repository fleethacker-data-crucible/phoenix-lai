# PowerShell 5.1 syntax reference (Phoenix)

For Phoenix models writing and checking Windows PowerShell 5.1 code. Part 1 is the language (variables, operators, flow, functions, errors, strings, the pitfalls that break scripts). Part 2 is every command that ships with Windows PowerShell 5.1 (powershell.exe): what it does, its exact syntax, every argument, its output and an example.

**Reading the syntax:** `[-Name]` optional, `<Type>` value type, `[[-Name] <Type>]` optional positional, `{A | B}` pick one, `[<CommonParameters>]` = -Verbose, -Debug, -ErrorAction, -WarningAction, -InformationAction, -ErrorVariable, -WarningVariable, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable. Each syntax line is one parameter set: arguments from two different sets cannot be combined.

**Reading the arguments:** `<Type>` the value type, `switch` takes no value (`-Force`, or `-Force:$false`), **required** must be given (or the shell prompts), `position N` can be passed without its name in that place (0 = first), `pipeline` accepts input from `|`, `values:` the only accepted values, `wildcards` accepts `*` and `?`.

**Sources:** command list and syntax lines read from Windows PowerShell 5.1.26100 (Get-Command -Syntax); parameter facts read from the same shell; parameter descriptions and output types summarized from Microsoft's PowerShell-Docs reference/5.1 (CC BY 4.0).

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

## Language: variables, types and literals

### Variables and assignment (PowerShell 5.1 syntax)

A variable is `$` followed by a name. It is created on first assignment and holds any .NET object. Names are case-insensitive. `${any name}` allows special characters.

**Syntax**

```powershell
$name = <expression>
[<type>]$name = <expression>          # typed: later assignments convert to the type or fail
$a, $b, $c = 1, 2, 3                  # multiple assignment
$first, $rest = 1, 2, 3, 4            # $rest gets @(2,3,4)
$x += 1; $x -= 1; $x *= 2; $x /= 2; $x %= 3; $x++; $x--
${env:PATH}; $env:TEMP                # environment variables
$script:count; $global:x; $local:y; $private:z; $using:v   # scopes
```

**Parts**

- `<type>`: a .NET type accelerator such as `[int] [long] [double] [decimal] [string] [char] [bool] [datetime] [timespan] [array] [hashtable] [psobject] [pscustomobject] [regex] [xml] [guid] [version] [scriptblock]`, or a full type name `[System.IO.FileInfo]`.
- Scope modifiers: `global:` session-wide, `script:` the script file, `local:` the current scope (default), `private:` invisible to child scopes, `using:` reads a caller variable inside `Invoke-Command`, `Start-Job` or `ForEach-Object -Parallel` script blocks.

**Example**

```powershell
[int]$count = "42"        # 42 (string converted)
$files = Get-ChildItem -Path C:\Logs -Filter *.log
$total = 0
foreach ($f in $files) { $total += $f.Length }
"{0} files, {1:N0} bytes" -f $files.Count, $total
```

**Check**

- A variable assigned inside a function or script block is local to it. Use `$script:` to change a script-level variable.
- `[int]$x = 3.5` rounds to 4 (banker's rounding: 2.5 becomes 2). Use `[math]::Floor()` or `[math]::Truncate()` when that matters.

### Automatic variables (PowerShell 5.1 syntax)

Variables the shell sets for you. Do not assign to them.

**Syntax**

```powershell
$_ / $PSItem      # current pipeline object (ForEach-Object, Where-Object, script-block parameters, catch: the error)
$args             # unbound arguments to a function or script (array)
$input            # pipeline input enumerator inside a function without process {}
$PSScriptRoot     # folder of the running script file ('' at the prompt)
$PSCommandPath    # full path of the running script file
$MyInvocation     # info about the current command (.MyCommand.Path, .InvocationName, .BoundParameters)
$PSBoundParameters# hashtable of parameters actually passed to the function
$LASTEXITCODE     # exit code of the last native program (.exe)
$?                # $true if the last command succeeded
$Error            # list of recent errors, newest at $Error[0]
$true $false $null
$HOME $PID $PWD $Host $PSVersionTable $PSHOME $ShellId
$Matches          # hashtable of captures after a successful -match on a scalar
$ExecutionContext $PSCulture $PSUICulture $IsWindows (7 only) $IsLinux (7 only) $IsMacOS (7 only)
$foreach $switch  # the enumerator of the current foreach / switch loop
```

**Example**

```powershell
$config = Join-Path $PSScriptRoot 'settings.json'
& git status
if ($LASTEXITCODE -ne 0) { throw "git failed with $LASTEXITCODE" }
if ('v1.2.3' -match '^v(\d+)\.(\d+)') { $major = [int]$Matches[1] }
```

**Check**

- `$_` exists only inside a pipeline script block, `switch` or `catch`. Inside a nested pipeline it refers to the inner one; save it first: `$outer = $_`.
- `$PSScriptRoot` is empty when code is pasted at a prompt. Use it only in `.ps1` or `.psm1` files.
- `$Matches` is set only when `-match` is applied to a single string, not to an array.

### Numbers, strings and literals (PowerShell 5.1 syntax)

**Syntax**

```powershell
42  -7  3.14  1e3  0x1F  1KB 1MB 1GB 1TB 1PB  10L (long)  1.5d (decimal)
'literal $not expanded'                  # single quotes: verbatim; '' is one quote
"expanded $var and $($obj.Prop) and `t tab"  # double quotes: variables, $( ) subexpressions, escapes
@"
multi-line expanding here-string, "quotes" free
"@                                        # closing "@ must start the line
@'
multi-line literal here-string
'@
```

**Escape sequences** (double quotes only, using the backtick): `` `n `` newline, `` `r `` carriage return, `` `t `` tab, `` `0 `` null, `` `a `` alert, `` `b `` backspace, `` `" `` quote, `` `$ `` dollar, `` `` `` backtick.

**Example**

```powershell
$name = 'World'
"Hello, $name! Today is $((Get-Date).DayOfWeek)."
'Cost: $5'                        # literal
"Path: $env:USERPROFILE\docs"
"$($user.Name) has $($user.Items.Count) items"   # property access needs $( )
```

**Check**

- In `"$obj.Name"`, only `$obj` expands and `.Name` is printed literally. Write `"$($obj.Name)"`.
- In `"$var:"` the colon makes a scope or drive reference. Write `"${var}:"`.
- The here-string opener `@"` must end its line, and the closer `"@` must be at column 0.

### Arrays (PowerShell 5.1 syntax)

An ordered, fixed-size collection of any objects.

**Syntax**

```powershell
$a = 1, 2, 3                 # comma operator
$a = @(1, 2, 3)              # array subexpression: always an array, even for 0 or 1 items
$a = @()                     # empty
$a = 1..10                   # range
$a[0]; $a[-1]; $a[1..3]; $a[0, 2, 4]   # index, last, slice, pick
$a.Count; $a.Length
$a += 4                      # creates a new array (slow in loops)
$a -contains 3; 3 -in $a     # membership
,$a                          # unary comma: wrap as one element (pass an array as one object)
[string[]]$names = 'a', 'b'  # typed array
$list = [System.Collections.Generic.List[string]]::new()   # growable list (fast Add)
```

**Example**

```powershell
$results = @(Get-ChildItem -Path . -Filter *.txt)   # @() so .Count works for 0 or 1 file
"Found $($results.Count) files"
$big = [System.Collections.Generic.List[int]]::new()
foreach ($i in 1..10000) { $big.Add($i) }
```

**Check**

- `$a += x` in a loop is O(n²). Use a `List[T]`, or assign the loop's output: `$a = foreach ($i in 1..n) { $i * 2 }`.
- A command that returns one object returns it unwrapped, not as an array. Wrap the call in `@( )` when you need `.Count` or indexing.
- `[System.Collections.ArrayList].Add()` returns the index. Write `[void]$list.Add($x)`.

### Hashtables and custom objects (PowerShell 5.1 syntax)

Key/value maps, and objects with named properties.

**Syntax**

```powershell
$h = @{ Name = 'x'; Size = 10 }          # hashtable (unordered)
$o = [ordered]@{ A = 1; B = 2 }          # keeps insertion order
$h['Name']; $h.Name; $h.Keys; $h.Values; $h.Count
$h.Add('k', 'v'); $h['k'] = 'v'; $h.Remove('k'); $h.ContainsKey('k')
$obj = [pscustomobject]@{ Name = 'x'; Size = 10 }   # object with properties, keeps order
$obj | Add-Member -NotePropertyName Extra -NotePropertyValue 1
$h.GetEnumerator() | Sort-Object Value   # iterate pairs (.Key, .Value)
```

**Example**

```powershell
$report = foreach ($f in Get-ChildItem *.log) {
    [pscustomobject]@{ File = $f.Name; KB = [math]::Round($f.Length / 1KB, 1); Modified = $f.LastWriteTime }
}
$report | Sort-Object KB -Descending | Format-Table -AutoSize
```

**Check**

- Hashtable keys are case-insensitive by default. Use `[hashtable]::new([StringComparer]::Ordinal)` for case-sensitive keys.
- Piping a hashtable sends one object, not its entries. Use `.GetEnumerator()`.
- Use `[pscustomobject]` for rows sent to `Export-Csv`, `Format-Table` or `ConvertTo-Json`. A hashtable exports badly.

### Type casting and conversion (PowerShell 5.1 syntax)

**Syntax**

```powershell
[int]'42'   [double]'3.5'   [datetime]'2024-01-31'   [bool]1   [string]42   [char]65
[int[]]('1','2')      $x -as [int]      # -as returns $null instead of throwing
$x -is [string]       $x -isnot [int]
[convert]::ToInt32('ff', 16)   [int]::Parse('42')   [int]::TryParse($s, [ref]$n)
[datetime]::ParseExact('31/01/2024', 'dd/MM/yyyy', $null)
$x.GetType().FullName
```

**Check**

- `[bool]'False'` is `$true`, because any non-empty string is true. Use `[bool]::Parse('False')` or compare the text.
- `[datetime]'01/02/2024'` always uses US month/day order (invariant culture), whatever the user's locale.
- The left operand decides the type: `'10' + 5` gives `'105'`, while `10 + '5'` gives 15. `'5' * 3` gives `'555'`.

## Language: operators

### Arithmetic and assignment operators (PowerShell 5.1 syntax)

**Syntax**

```powershell
+  -  *  /  %          # add/concatenate, subtract, multiply/repeat, divide, remainder
-band -bor -bxor -bnot -shl -shr    # bitwise
=  +=  -=  *=  /=  %=  ++  --
[math]::Round(2.567, 2)  [math]::Floor(x)  [math]::Ceiling(x)  [math]::Abs(x)  [math]::Pow(2,8)  [math]::Sqrt(x)  [math]::Max(a,b)  [math]::Min(a,b)
```

**Check**

- `/` always divides to a double when needed: `7/2` gives 3.5. Use `[math]::Floor(7/2)` or `[int][math]::Truncate(7/2)` for integer division.
- `%` is also the alias of `ForEach-Object` at the start of a pipeline segment. Write it with operands on both sides: `$a % 2`.

### Comparison operators (PowerShell 5.1 syntax)

All are case-insensitive. Add a `c` prefix for case-sensitive (`-ceq`, `-clike`, `-cmatch`) or an `i` prefix to state case-insensitive explicitly.

**Syntax**

```powershell
-eq -ne -gt -ge -lt -le             # equality / order
-like -notlike                      # wildcard: * ? [a-z]
-match -notmatch                    # regex; sets $Matches
-contains -notcontains              # collection contains value:  $list -contains 'x'
-in -notin                          # value in collection:        'x' -in $list
-is -isnot                          # type test
-replace                            # regex replace: 'a1b2' -replace '\d', '#'
```

**Arguments**

- With a scalar on the left, the operator returns `$true`/`$false`.
- With an array on the left, the operator returns the matching elements: `1,2,3,2 -eq 2` gives `2,2`.
- `-replace` takes `<pattern>, <replacement>`. In the replacement, `$1` or `${name}` refers to a group; keep it in single quotes so PowerShell does not expand it.

**Example**

```powershell
if ($name -like 'report_*.csv') { 'csv report' }
if ($line -match '^(?<key>\w+)=(?<val>.*)$') { $cfg[$Matches.key] = $Matches.val }
'2024-01-31' -replace '(\d+)-(\d+)-(\d+)', '$3/$2/$1'   # 31/01/2024
```

**Check**

- `==`, `!=`, `<` and `>` are wrong in PowerShell. `>` writes to a file.
- `-contains` is not a substring test. `'hello' -contains 'ell'` is `$false`. Use `-like '*ell*'` or `.Contains('ell')`.
- `-match` and `-replace` use regex. Escape literal text with `[regex]::Escape($s)`.

### Logical, string, type and redirection operators (PowerShell 5.1 syntax)

**Syntax**

```powershell
-and -or -xor -not  !                # logical
-split -join                         # 'a,b,c' -split ','   ;  $arr -join ', '
-f                                   # format: '{0:N2} {1,-10} {2:yyyy-MM-dd}' -f 3.14159, 'x', (Get-Date)
..                                   # range 1..5, 'a'..'e' (7 only for chars)
&  .                                 # call operator, dot-source operator
,                                    # array constructor / unary comma
$( )  @( )  ( )                      # subexpression, array subexpression, grouping
::                                   # static member: [math]::PI, [IO.Path]::Combine(a,b)
|                                    # pipeline
>  >>  2>  2>>  2>&1  *>  *>>        # redirection: output, append, error, error to output, all streams
# 5.1 has no ternary (?:), ??, ??=, ?. or && / ||: use if/else and $?
```

**Example**

```powershell
$parts = 'name=value' -split '=', 2      # max 2 pieces
$csv = $names -join ','
'{0,-20}{1,10:N0}' -f $f.Name, $f.Length
. .\helpers.ps1                          # load functions into the current scope
& $exePath --version 2>&1 | Out-File log.txt
```

**Check**

- `-split` uses regex. `'a.b' -split '.'` splits on every character. Write `-split '\.'` or `-split '.', 0, 'SimpleMatch'`.
- `-f` needs the values grouped when an argument is an expression: `'{0}' -f (Get-Date)`.
- `!` and `-not` bind tightly. Write `-not ($a -eq $b)`.

## Language: flow control

### if / elseif / else (PowerShell 5.1 statement)

**Syntax**

```powershell
if (<condition>) { <statements> }
elseif (<condition>) { <statements> }
else { <statements> }
```

**Arguments**

- `<condition>`: any expression. `$null`, `0`, `''`, `$false` and empty arrays are false. A one-element array is the truthiness of that element.

**Example**

```powershell
if (-not (Test-Path -Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
elseif ((Get-ChildItem $dir).Count -gt 100) { Write-Warning 'Folder is large' }
else { 'ok' }
```

**Check**

- Write `elseif` as one word. `else if` is invalid.
- A command used as a condition needs parentheses: `if ((Get-Service x).Status -eq 'Running')`.

### switch (PowerShell 5.1 statement)

Matches a value (or each element of a collection, or each line of a file) against cases. Unlike C, every matching case runs unless you use `break` or `continue`.

**Syntax**

```powershell
switch [-Regex | -Wildcard | -Exact] [-CaseSensitive] (<value>) {
    <case> { <statements> }
    { <scriptblock test using $_ > } { <statements> }
    default { <statements> }
}
switch [-Regex | -Wildcard | -Exact] [-CaseSensitive] -File <path> { ... }
```

**Arguments**

- `-Regex`: cases are regex patterns, and `$Matches` is set.
- `-Wildcard`: cases are wildcard patterns.
- `-Exact`: plain string match (the default).
- `-CaseSensitive`: case-sensitive matching.
- `-File`: the input is the lines of a file.
- `$_`: the current value. `break` leaves the switch; `continue` moves to the next input element.

**Example**

```powershell
switch -Regex ($line) {
    '^ERROR'   { $errors++; break }
    '^WARN'    { $warnings++; break }
    default    { $other++ }
}
```

**Check**

- Without `break`, a value matching two cases runs both.
- With an array input, the switch loops over the elements. Inside it, `break` stops the whole loop and `continue` goes to the next element.

### foreach statement and ForEach-Object (PowerShell 5.1 statement)

**Syntax**

```powershell
foreach ($item in <collection>) { <statements> }        # statement: fast, whole collection in memory
<pipeline> | ForEach-Object { <statements using $_> }    # cmdlet: streams one object at a time
<pipeline> | ForEach-Object -Begin { } -Process { } -End { }
$collection.ForEach({ $_ * 2 })                          # method (PowerShell 4+)
```

**Example**

```powershell
foreach ($svc in Get-Service -Name 'w*') { "{0}: {1}" -f $svc.Name, $svc.Status }
Get-ChildItem *.log | ForEach-Object { Rename-Item -Path $_.FullName -NewName ($_.BaseName + '.old') }
```

**Check**

- Inside `ForEach-Object`, `return` skips to the next object (like `continue`), while `break` and `continue` stop or skip the enclosing loop, or the whole script when there is none. In the `foreach` statement, use `continue` and `break` normally.
- The `foreach` keyword at the start of a pipeline segment is the alias of `ForEach-Object`. Write the full cmdlet name after `|`.

### for, while, do-while, do-until (PowerShell 5.1 statement)

**Syntax**

```powershell
for (<init>; <condition>; <repeat>) { <statements> }
while (<condition>) { <statements> }
do { <statements> } while (<condition>)    # runs at least once, loops while true
do { <statements> } until (<condition>)    # runs at least once, loops until true
break [<label>]; continue [<label>]
:outer foreach ($a in $x) { foreach ($b in $y) { if ($b) { continue outer } } }   # labels
```

**Example**

```powershell
for ($i = 0; $i -lt $items.Count; $i++) { "$i : $($items[$i])" }
$tries = 0
do { $ok = Test-Connection -ComputerName srv1 -Count 1 -Quiet; $tries++ } until ($ok -or $tries -ge 5)
```

**Check**

- The comparison in the loop condition uses `-lt` and `-le`, not `<`.

### try / catch / finally, throw, trap (PowerShell 5.1 statement)

**Syntax**

```powershell
try { <statements> }
catch [<ExceptionType>[, <ExceptionType>...]] { <statements; $_ is the ErrorRecord> }
catch { <any other error> }
finally { <always runs> }
throw <message or object>
throw                                   # rethrow inside catch
trap [<ExceptionType>] { <statements>; continue | break }
Write-Error -Message <text> [-ErrorAction Stop]
```

**Arguments**

- `$_`: the ErrorRecord. Useful members: `$_.Exception.Message`, `$_.Exception.GetType().FullName`, `$_.InvocationInfo.ScriptLineNumber`, `$_.ScriptStackTrace`, `$_.CategoryInfo`, `$_.FullyQualifiedErrorId`, `$_.TargetObject`.

**Example**

```powershell
try {
    $data = Get-Content -Path $file -ErrorAction Stop | ConvertFrom-Json
}
catch [System.Management.Automation.ItemNotFoundException] { Write-Warning "Missing: $file"; $data = $null }
catch { Write-Error "Could not read $file : $($_.Exception.Message)"; throw }
finally { Write-Verbose 'done' }
```

**Check**

- Most cmdlet errors are non-terminating and skip `catch` unless you add `-ErrorAction Stop` or set `$ErrorActionPreference = 'Stop'`.
- Native programs (`.exe`) never throw. Check `$LASTEXITCODE` after the call.
- The order of catch blocks matters: put specific types before a bare `catch`.

## Language: functions, scripts and script blocks

### function and param() (PowerShell 5.1 statement)

**Syntax**

```powershell
function <Verb-Noun> {
    [CmdletBinding(SupportsShouldProcess = $true, DefaultParameterSetName = '<set>')]
    [OutputType([<type>])]
    param(
        [Parameter(Mandatory, Position = 0, ValueFromPipeline, ValueFromPipelineByPropertyName, ParameterSetName = '<set>', HelpMessage = '<text>')]
        [Alias('<alias>')]
        [ValidateNotNullOrEmpty()] [ValidateSet('A','B')] [ValidateRange(1, 10)] [ValidatePattern('^\w+$')]
        [ValidateScript({ Test-Path $_ })] [ValidateLength(1, 64)] [ValidateCount(1, 5)] [AllowNull()] [AllowEmptyString()]
        [<type>]$Name = <default>,
        [switch]$Force
    )
    begin   { <once, before pipeline input> }
    process { <once per pipeline object; $_ or the ValueFromPipeline parameter> }
    end     { <once, after all input> }
}
function <name>($a, $b) { <statements> }    # simple form
```

**Arguments**

- `[CmdletBinding()]`: makes it an advanced function. It adds the common parameters (`-Verbose`, `-ErrorAction`, …) and `$PSCmdlet`.
- `SupportsShouldProcess`: adds `-WhatIf` and `-Confirm`. Guard changes with `if ($PSCmdlet.ShouldProcess($target, 'Delete')) { … }`.
- `Mandatory`: the shell prompts if it is missing.
- `Position`: lets the value be passed without the name.
- `ValueFromPipeline`: binds piped objects, and is used in `process {}`.
- `[switch]`: a boolean flag, true when present. Test it with `if ($Force)`.
- Default values apply only when the argument is not passed.

**Example**

```powershell
function Remove-OldLog {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory, ValueFromPipelineByPropertyName)][Alias('FullName')][string]$Path,
        [ValidateRange(1, 3650)][int]$Days = 30
    )
    process {
        $f = Get-Item -LiteralPath $Path
        if ($f.LastWriteTime -lt (Get-Date).AddDays(-$Days) -and $PSCmdlet.ShouldProcess($Path, 'Remove')) {
            Remove-Item -LiteralPath $Path
        }
    }
}
Get-ChildItem C:\Logs\*.log | Remove-OldLog -Days 90 -WhatIf
```

**Check**

- Call functions like commands: `Get-Thing -A 1 -B 2` or `Get-Thing 1 2`. Never write `Get-Thing(1, 2)`, which passes ONE array argument.
- Define a function before the line that calls it. Scripts run top to bottom.
- Without a `process {}` block, piped input is only in `$input`, and the `ValueFromPipeline` parameter holds just the last item.
- Everything not captured is output. `return $x` is the same as writing `$x` and then returning.

### Script files, param() and dot-sourcing (PowerShell 5.1 syntax)

**Syntax**

```powershell
# MyScript.ps1
#Requires -Version 5.1
#Requires -Modules ActiveDirectory
#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Path, [int]$Top = 10)
<statements>
exit <code>              # sets the process exit code / $LASTEXITCODE for the caller

.\MyScript.ps1 -Path C:\x -Top 5     # run (needs .\ or a full path)
. .\Lib.ps1                          # dot-source: run in the current scope (functions and variables stay)
& 'C:\path with spaces\s.ps1'
```

**Check**

- The `param()` block must be the first statement in the file, after comments and `#Requires`.
- Running scripts may be blocked by the execution policy. The user runs `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` or starts it with `-ExecutionPolicy Bypass -File script.ps1`.
- Relative paths resolve against the current location, not the script folder. Build paths with `$PSScriptRoot`.

### Script blocks and splatting (PowerShell 5.1 syntax)

**Syntax**

```powershell
$sb = { param($x) $x * 2 }
& $sb 21              # invoke (child scope)
. $sb 21              # invoke in current scope
$sb.Invoke(21)        # returns a collection
$params = @{ Path = 'C:\x'; Recurse = $true; Filter = '*.txt' }
Get-ChildItem @params                 # splat a hashtable (named)
$argsArr = 'C:\x', 'C:\y'; Copy-Item @argsArr     # splat an array (positional)
```

**Example**

```powershell
$mail = @{ To = 'a@x.com'; From = 'b@x.com'; Subject = 'Report'; SmtpServer = 'smtp.x.com' }
if ($attach) { $mail.Attachments = $attach }
Send-MailMessage @mail
```

**Check**

- Splat with `@params`, not `$params`. Passing `$params` sends the hashtable as one positional argument.
- A switch is splatted as `Recurse = $true`.

### Classes and enums (PowerShell 5.1 syntax)

PowerShell 5.0 and later.

**Syntax**

```powershell
enum Color { Red; Green = 5; Blue }
[Flags()] enum Access { Read = 1; Write = 2; Exec = 4 }
class Person [: BaseClass] [, IInterface] {
    [string]$Name
    hidden [int]$Id
    static [int]$Count = 0
    Person([string]$name) { $this.Name = $name; [Person]::Count++ }
    [string] Greet([string]$greeting) { return "$greeting, $($this.Name)" }
    static [Person] Create([string]$n) { return [Person]::new($n) }
}
$p = [Person]::new('Ann'); $p.Greet('Hi')
```

**Check**

- Class methods need an explicit `return` and a declared return type (`[void]` when they return nothing). Unlike functions, stray output is not returned.
- Inside a class, use `$this.` for members, and `$script:` or parameters for outside variables.
- A class used before its definition in the same file can fail to parse. Define classes first.

## Language: pipeline, output and formatting

### The pipeline (PowerShell 5.1 syntax)

Commands pass .NET objects (not text) from left to right. Each object flows through the entire pipeline before the next one starts.

**Syntax**

```powershell
Get-Process | Where-Object CPU -gt 100 | Sort-Object CPU -Descending | Select-Object -First 5 Name, CPU
Get-Process | Where-Object { $_.WorkingSet64 -gt 200MB }       # script-block filter
Get-ChildItem | Select-Object Name, @{ Name = 'KB'; Expression = { [math]::Round($_.Length / 1KB) } }   # calculated property
```

**Check**

- Filter left: use a cmdlet's own `-Filter`, `-Name` or `-Include` parameters before `Where-Object`, because it is faster.
- `Format-*` cmdlets go last. After `Format-Table`, the objects are formatting records and cannot be exported or used.
- `Select-Object` returns new objects. Use `-ExpandProperty` to get the raw values: `(Get-Process).Name` or `Select-Object -ExpandProperty Name`.

### Output streams and writing messages (PowerShell 5.1 syntax)

**Syntax**

```powershell
Write-Output $x           # stream 1 (success); same as just writing $x
Write-Error 'msg'         # stream 2
Write-Warning 'msg'       # stream 3
Write-Verbose 'msg'       # stream 4, shown with -Verbose
Write-Debug 'msg'         # stream 5, shown with -Debug
Write-Information 'msg'   # stream 6 (Write-Host also writes here in 5.0+)
Write-Host 'msg' -ForegroundColor Green   # display only, cannot be captured as data
$out = cmd 2>&1           # merge errors into output
cmd *> $null              # discard everything
```

**Check**

- Return data with output, not `Write-Host`. `Write-Host` text cannot be piped or assigned.
- Use `Write-Verbose` for progress chatter, so callers can silence it.

## Language: strings, regex and files

### String methods (PowerShell 5.1 syntax)

.NET `System.String` methods, available on every string.

**Syntax**

```powershell
$s.Length  $s.ToUpper()  $s.ToLower()  $s.Trim()  $s.TrimStart('0')  $s.TrimEnd('/')
$s.Substring(start[, length])  $s.IndexOf('x')  $s.LastIndexOf('x')  $s.Contains('x')
$s.StartsWith('x')  $s.EndsWith('x')  $s.Replace('a', 'b')   # literal replace, case-sensitive
$s.Split(',')  $s.PadLeft(10, '0')  $s.PadRight(10)  $s.Insert(0, 'x')  $s.Remove(0, 2)
[string]::IsNullOrEmpty($s)  [string]::IsNullOrWhiteSpace($s)  [string]::Join(',', $arr)  [string]::Format('{0}', $x)
```

**Check**

- `.Replace()` is literal and case-sensitive, while `-replace` is regex and case-insensitive.
- `.Contains()` is case-sensitive.
- `.Substring()` throws when start+length runs past the end. Check `.Length` first.

### Files and paths (PowerShell 5.1 syntax)

**Syntax**

```powershell
Get-Content -Path f.txt [-Raw] [-Encoding UTF8] [-TotalCount 10] [-Tail 10]
Set-Content -Path f.txt -Value $text [-Encoding UTF8]      # overwrite
Add-Content -Path f.txt -Value $line                       # append
$text | Out-File -FilePath f.txt [-Encoding utf8] [-Append]
Import-Csv f.csv [-Delimiter ';']   $rows | Export-Csv out.csv -NoTypeInformation
Get-Content f.json -Raw | ConvertFrom-Json    $obj | ConvertTo-Json -Depth 10 | Set-Content f.json
Join-Path $PSScriptRoot 'data\in.txt'   Split-Path $p -Parent   Split-Path $p -Leaf   [IO.Path]::GetExtension($p)
Test-Path -Path $p [-PathType Leaf | Container]
[IO.File]::ReadAllText($full)  [IO.File]::WriteAllText($full, $text)   # needs a full path
```

**Check**

- `Get-Content` without `-Raw` returns an array of lines. Use `-Raw` for one string, for example before `ConvertFrom-Json`.
- `ConvertTo-Json` defaults to `-Depth 2` and silently truncates deeper objects. Pass `-Depth`.
- .NET methods such as `[IO.File]` use the process directory, not `Get-Location`. Pass a full path (`(Resolve-Path x).Path` or `Join-Path $PWD x`).
- Encoding in 5.1: `-Encoding UTF8` writes a BOM. For UTF-8 without a BOM, use `[IO.File]::WriteAllText($full, $text, (New-Object System.Text.UTF8Encoding $false))`. Read raw bytes with `-Encoding Byte`.

## Commands: Microsoft.PowerShell.Core

### Add-History (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Adds entries (for example from a saved history file) to the current session's command history.

**Syntax** (one line per parameter set)

```powershell
Add-History [[-InputObject] <psobject[]>] [-Passthru] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject[]> · position 0 · pipeline by value — Specifies an array of entries to add to the history as HistoryInfo object to the session history.
- `-Passthru` switch — Indicates that this cmdlet returns a HistoryInfo object for each history entry.

**Output:** None; HistoryInfo

**Example**

```powershell
Import-Csv .\history.csv | Add-History
```

### Add-PSSnapin (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: asnp

Loads a registered Windows PowerShell snap-in (the older, pre-module extension format) into the session.

**Syntax** (one line per parameter set)

```powershell
Add-PSSnapin [-Name] <string[]> [-PassThru] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the name of the snap-in. This is the Name, not the AssemblyName or ModuleName.
- `-PassThru` switch — Indicates that this cmdlet returns an object that represents each added snap-in.

**Output:** None or System.Management.Automation.PSSnapInInfo

**Example**

```powershell
Add-PSSnapin Microsoft.Exchange.Management.PowerShell.SnapIn
```

### Clear-History (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: clhy

Removes entries from the session's command history, all of them or by ID, count or command text.

**Syntax** (one line per parameter set)

```powershell
Clear-History [[-Id] <int[]>] [[-Count] <int>] [-Newest] [-WhatIf] [-Confirm] [<CommonParameters>]

Clear-History [[-Count] <int>] [-CommandLine <string[]>] [-Newest] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Id` <Int32[]> · position 0 — Specifies the command history Id that Clear-History deletes. To display Id numbers, use the Get-History cmdlet.
- `-CommandLine` <String[]> · wildcards — Deletes command history from a PowerShell session. The string must be an exact match or use wildcards to match commands in the PowerShell session history displayed by Get-History.
- `-Count` <Int32> · position 1 — Specifies the number of history entries that Clear-History deletes. Commands are deleted in order, beginning with the oldest entry in the history.
- `-Newest` switch — When the Newest parameter is used, Clear-History deletes the newest entries in the history.

**Output:** None

**Example**

```powershell
Clear-History -Count 10 -Newest
```

### Clear-Host (PowerShell 5.1 function)

Module Microsoft.PowerShell.Core · aliases: clear, cls

Clears the console screen. Aliases: cls, clear.

**Syntax** (one line per parameter set)

```powershell
Clear-Host
```

**Output:** None

**Example**

```powershell
Clear-Host
```

### Connect-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: cnsn

Reconnects to remote sessions (PSSessions) that were disconnected, so you can keep using them.

**Syntax** (one line per parameter set)

```powershell
Connect-PSSession -Name <string[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-Session] <PSSession[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession -ComputerName <string[]> -InstanceId <guid[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-ComputerName] <string[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-ConnectionUri] <uri[]> [-ConfigurationName <string>] [-AllowRedirection] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-ConnectionUri] <uri[]> -InstanceId <guid[]> [-ConfigurationName <string>] [-AllowRedirection] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession -InstanceId <guid[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Connect-PSSession [-Id] <int[]> [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Session` <PSSession[]> · **required** · position 0 · pipeline by value and by property name — Specifies the disconnected sessions. Enter a variable that contains the PSSession objects or a command that creates or gets the PSSession objects, such as a Get-PSSession command.
- `-ComputerName` <String[]> · **required** · position 0 · alias -Cn — Specifies the computers on which the disconnected sessions are stored.
- `-ApplicationName` <String> · pipeline by property name — Specifies the name of an application. This cmdlet connects only to sessions that use the specified application.
- `-ConfigurationName` <String> · pipeline by property name — Connects only to sessions that use the specified session configuration.
- `-ConnectionUri` <Uri[]> · **required** · position 0 · pipeline by property name · alias -URI, -CU — Specifies the URIs of the connection endpoints for the disconnected sessions.
- `-AllowRedirection` switch — Indicates that this cmdlet allows redirection of this connection to an alternate URI.
- `-InstanceId` <Guid[]> · **required** — Specifies the instance IDs of the disconnected sessions.
- `-Name` <String[]> · **required** — Specifies the friendly names of the disconnected sessions.
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to connect to the disconnected session.
- `-Authentication` <AuthenticationMechanism> — Specifies the mechanism that is used to authenticate user credentials in the command to reconnect to the disconnected session.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to connect to the disconnected session.
- `-Port` <Int32> — Specifies the network port on the remote computer that is used to reconnect to the session.
- `-UseSSL` switch — Indicates that this cmdlet uses the Secure Sockets Layer (SSL) protocol to connect to the disconnected session.
- `-SessionOption` <PSSessionOption> — Specifies advanced options for the session. Enter a SessionOption object, such as one that you create by using the New-PSSessionOption cmdlet, or a hash table in which the keys are session option names and the values are session option values.
- `-ThrottleLimit` <Int32> — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies the IDs of the disconnected sessions. The Id parameter works only when the disconnected session was previously connected to the current session.

**Output:** PSSession

**Example**

```powershell
Get-PSSession -ComputerName Server01 | Connect-PSSession
```

### Debug-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Attaches the debugger to a running job (background, remote or workflow) so you can step through its script.

**Syntax** (one line per parameter set)

```powershell
Debug-Job [-Job] <Job> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Job [-Name] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Job [-Id] <int> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Job [-InstanceId] <guid> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Job` <Job> · **required** · position 0 · pipeline by value and by property name — Specifies a running job object. The simplest way to use this parameter is to save the results of a Get-Job command that returns the running job that you want to debug in a variable, and then specify the variable as the value of this parameter.
- `-Name` <String> · **required** · position 0 — Specifies a job by the friendly name of the job. When you start a job, you can specify a job name by adding the JobName parameter, in cmdlets such as Invoke-Command and Start-Job.
- `-Id` <Int32> · **required** · position 0 — Specifies the ID number of a running job. To get the ID number of a job, run the Get-Job cmdlet.
- `-InstanceId` <Guid> · **required** · position 0 — Specifies the instance ID GUID of a running job.

**Example**

```powershell
Debug-Job -Id 3
```

### Disable-PSRemoting (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Stops the computer's session configurations from accepting remote connections (local access still works).

**Syntax** (one line per parameter set)

```powershell
Disable-PSRemoting [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Force` switch — Forces the command to run without asking for user confirmation.

**Output:** None

**Example**

```powershell
Disable-PSRemoting -Force
```

### Disable-PSSessionConfiguration (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Denies access to one or more registered session configurations (endpoints) on this computer.

**Syntax** (one line per parameter set)

```powershell
Disable-PSSessionConfiguration [[-Name] <string[]>] [-Force] [-NoServiceRestart] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies an array of names of session configurations to disable. Enter one or more configuration names.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-NoServiceRestart` switch — Used to prevent the restart of the WSMan service. It is not necessary to restart the service to disable the configuration.

**Output:** None

**Example**

```powershell
Disable-PSSessionConfiguration -Name Microsoft.PowerShell -Force
```

### Disconnect-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: dnsn

Disconnects from a remote session but leaves it running on the remote computer, so you can reconnect later.

**Syntax** (one line per parameter set)

```powershell
Disconnect-PSSession [-Session] <PSSession[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Disconnect-PSSession [-Id] <int[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Disconnect-PSSession -Name <string[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Disconnect-PSSession -InstanceId <guid[]> [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Session` <PSSession[]> · **required** · position 0 · pipeline by value and by property name — Disconnects from the specified PSSessions. Enter PSSession objects, such as those that the New-PSSession cmdlet returns.
- `-IdleTimeoutSec` <Int32> · default 60 — Changes the idle timeout value of the disconnected PSSession. Enter a value in seconds.
- `-OutputBufferingMode` <OutputBufferingMode> · default Block — Determines how command output is managed in the disconnected session when the output buffer is full.
- `-ThrottleLimit` <Int32> · default 32 — Sets the throttle limit for the Disconnect-PSSession command.
- `-InstanceId` <Guid[]> · **required** · pipeline by property name — Disconnects from sessions with the specified instance IDs.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Disconnects from sessions with the specified session ID. Type one or more IDs (separated by commas), or use the range operator (..) to specify a range of IDs.
- `-Name` <String[]> · **required** · pipeline by property name · wildcards — Disconnects from sessions with the specified friendly names. Wildcards are permitted.

**Output:** PSSession

**Example**

```powershell
Get-PSSession -Name Work | Disconnect-PSSession
```

### Enable-PSRemoting (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Configures this computer to receive remote PowerShell commands (starts WinRM, creates listeners and firewall rules).

**Syntax** (one line per parameter set)

```powershell
Enable-PSRemoting [-Force] [-SkipNetworkProfileCheck] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-SkipNetworkProfileCheck` switch — Indicates that this cmdlet enables remoting on client versions of the Windows operating system when the computer is on a public network.

**Output:** String

**Example**

```powershell
Enable-PSRemoting -Force
```

### Enable-PSSessionConfiguration (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Re-enables session configurations that were disabled, allowing users to connect to them again.

**Syntax** (one line per parameter set)

```powershell
Enable-PSSessionConfiguration [[-Name] <string[]>] [-Force] [-SecurityDescriptorSddl <string>] [-SkipNetworkProfileCheck] [-NoServiceRestart] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies the names of session configurations to enable. Enter one or more configuration names.
- `-Force` switch — Indicates that the cmdlet does not prompt you for confirmation, and restarts the WinRM service without prompting.
- `-SecurityDescriptorSddl` <String> — Specifies a security descriptor with which this cmdlet replaces the security descriptor on the session configuration.
- `-SkipNetworkProfileCheck` switch — Indicates that this cmdlet enables the session configuration when the computer is on a public network.
- `-NoServiceRestart` switch — Indicates that the cmdlet does not restart the service.

**Output:** None

**Example**

```powershell
Enable-PSSessionConfiguration -Name Microsoft.PowerShell
```

### Enter-PSHostProcess (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Connects to another local process that hosts PowerShell (by ID or name) for interactive debugging.

**Syntax** (one line per parameter set)

```powershell
Enter-PSHostProcess [-Id] <int> [[-AppDomainName] <string>] [<CommonParameters>]

Enter-PSHostProcess [-Process] <Process> [[-AppDomainName] <string>] [<CommonParameters>]

Enter-PSHostProcess [-Name] <string> [[-AppDomainName] <string>] [<CommonParameters>]

Enter-PSHostProcess [-HostProcessInfo] <PSHostProcessInfo> [[-AppDomainName] <string>] [<CommonParameters>]
```

**Arguments**

- `-Process` <Process> · **required** · position 0 · pipeline by value — Specifies a process by the process object. The simplest way to use this parameter is to save the results of a Get-Process command that returns process that you want to enter in a variable, and then specify the variable as the value of this parameter.
- `-Id` <Int32> · **required** · position 0 — Specifies a process by the process ID. To get a process ID, run the Get-Process cmdlet.
- `-Name` <String> · **required** · position 0 — Specifies a process by the process name. To get a process name, run the Get-Process cmdlet.
- `-HostProcessInfo` <PSHostProcessInfo> · **required** · position 0 · pipeline by value — Specifies a PSHostProcessInfo object that can be connected to with PowerShell.
- `-AppDomainName` <String> · position 1 · default DefaultAppDomain — Specifies an application domain name to connect to if omitted, uses DefaultAppDomain.

**Example**

```powershell
Enter-PSHostProcess -Id 4321
```

### Enter-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: etsn

Starts an interactive session with a remote computer; commands you type run there until Exit-PSSession.

**Syntax** (one line per parameter set)

```powershell
Enter-PSSession [-ComputerName] <string> [-EnableNetworkAccess] [-Credential <pscredential>] [-ConfigurationName <string>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Enter-PSSession [[-Session] <PSSession>] [<CommonParameters>]

Enter-PSSession [[-ConnectionUri] <uri>] [-EnableNetworkAccess] [-Credential <pscredential>] [-ConfigurationName <string>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Enter-PSSession [-InstanceId <guid>] [<CommonParameters>]

Enter-PSSession [[-Id] <int>] [<CommonParameters>]

Enter-PSSession [-Name <string>] [<CommonParameters>]

Enter-PSSession [-VMId] <guid> [-Credential] <pscredential> [-ConfigurationName <string>] [<CommonParameters>]

Enter-PSSession [-VMName] <string> [-Credential] <pscredential> [-ConfigurationName <string>] [<CommonParameters>]

Enter-PSSession [-ContainerId] <string> [-ConfigurationName <string>] [-RunAsAdministrator] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String> · **required** · position 0 · pipeline by value and by property name · alias -Cn — Specifies a computer name. This cmdlet starts an interactive session with the specified remote computer.
- `-Session` <PSSession> · position 0 · pipeline by value and by property name — Specifies a PowerShell session (PSSession) to use for the interactive session.
- `-ConnectionUri` <Uri> · position 1 · pipeline by property name · alias -URI, -CU — Specifies a URI that defines the connection endpoint for the session. The URI must be fully qualified.
- `-InstanceId` <Guid> · pipeline by property name — Specifies the instance ID of an existing session. Enter-PSSession uses the specified session for the interactive session.
- `-Id` <Int32> · position 0 · pipeline by property name — Specifies the ID of an existing session. Enter-PSSession uses the specified session for the interactive session.
- `-Name` <String> · pipeline by property name — Specifies the friendly name of an existing session. Enter-PSSession uses the specified session for the interactive session.
- `-EnableNetworkAccess` switch — Indicates that this cmdlet adds an interactive security token to loopback sessions.
- `-VMId` <Guid> · **required** · position 0 · pipeline by value and by property name · alias -VMGuid — Specifies the ID of a virtual machine.
- `-VMName` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the name of a virtual machine.
- `-Credential` <PSCredential> · **required** · position 1 · pipeline by property name · default Current user — Specifies a user account that has permission to perform this action. The default is the current user.
- `-ContainerId` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the ID of a container.
- `-ConfigurationName` <String> · pipeline by property name — Specifies the session configuration that's used for the interactive session.
- `-RunAsAdministrator` switch — Indicates that the PSSession runs as administrator.
- `-Port` <Int32> — Specifies the network port on the remote computer that's used for this command.
- `-UseSSL` switch — Indicates that this cmdlet uses the Secure Sockets Layer (SSL) protocol to establish a connection to the remote computer.
- `-ApplicationName` <String> · pipeline by property name — Specifies the application name segment of the connection URI. Use this parameter to specify the application name when you are not using the ConnectionURI parameter in the command.
- `-AllowRedirection` switch — Allows redirection of this connection to an alternate Uniform Resource Identifier (URI).
- `-SessionOption` <PSSessionOption> — Sets advanced options for the session. Enter a SessionOption object, such as one that you create by using the New-PSSessionOption cmdlet, or a hash table in which the keys are session option names and the values are session option values.
- `-Authentication` <AuthenticationMechanism> — Specifies the mechanism that is used to authenticate the user's credentials.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** None

**Example**

```powershell
Enter-PSSession -ComputerName Server01 -Credential (Get-Credential)
```

### Exit-PSHostProcess (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Leaves the process session started by Enter-PSHostProcess.

**Syntax** (one line per parameter set)

```powershell
Exit-PSHostProcess [<CommonParameters>]
```

**Example**

```powershell
Exit-PSHostProcess
```

### Exit-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: exsn

Ends the interactive remote session started by Enter-PSSession and returns to the local prompt.

**Syntax** (one line per parameter set)

```powershell
Exit-PSSession [<CommonParameters>]
```

**Output:** None

**Example**

```powershell
Exit-PSSession
```

### Export-Console (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Saves the names of the snap-ins loaded in the session to a console file (.psc1).

**Syntax** (one line per parameter set)

```powershell
Export-Console [[-Path] <string>] [-Force] [-NoClobber] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · position 0 · pipeline by value and by property name · alias -PSPath — Specifies a path and file name for the console file (*.psc1). Enter an optional path and name.
- `-Force` switch — Indicates that this cmdlet overwrites the data in a console file without warning, even if the file has the read-only attribute.
- `-NoClobber` switch · alias -NoOverwrite — Indicates that this cmdlet does not overwrite an existing console file.

**Output:** FileInfo

**Example**

```powershell
Export-Console -Path .\MySession.psc1
```

### Export-ModuleMember (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Inside a script module, declares which functions, variables, aliases and cmdlets the module makes public.

**Syntax** (one line per parameter set)

```powershell
Export-ModuleMember [[-Function] <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Function` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies the functions that are exported from the script module file.
- `-Cmdlet` <String[]> · pipeline by property name · wildcards — Specifies the cmdlets that are exported from the script module file. Enter the cmdlet names.
- `-Variable` <String[]> · pipeline by property name · wildcards — Specifies the variables that are exported from the script module file.
- `-Alias` <String[]> · pipeline by property name · wildcards — Specifies the aliases that are exported from the script module file. Enter the alias names.

**Output:** None

**Example**

```powershell
Export-ModuleMember -Function Get-Report, Set-Report -Alias gr
```

### ForEach-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: %, foreach

Runs a script block once for each object coming down the pipeline ($_ is the current object). Alias: foreach, %.

**Syntax** (one line per parameter set)

```powershell
ForEach-Object [-Process] <scriptblock[]> [-InputObject <psobject>] [-Begin <scriptblock>] [-End <scriptblock>] [-RemainingScripts <scriptblock[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

ForEach-Object [-MemberName] <string> [-InputObject <psobject>] [-ArgumentList <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies the input objects. ForEach-Object runs the scriptblock or operation statement on each input object.
- `-Begin` <ScriptBlock> — Specifies a scriptblock that runs before this cmdlet processes any input objects.
- `-Process` <ScriptBlock[]> · **required** · position 0 — Specifies the operation that's performed on each input object. This scriptblock is run for every object in the pipeline.
- `-End` <ScriptBlock> — Specifies a scriptblock that runs after this cmdlet processes all input objects.
- `-RemainingScripts` <ScriptBlock[]> — Specifies all scriptblocks that aren't taken by the Process parameter.
- `-MemberName` <String> · **required** · position 0 · wildcards — Specifies the name of the member property to get or the member method to call.
- `-ArgumentList` <Object[]> · alias -Args — Specifies an array of arguments to a method call. For more information about the behavior of ArgumentList, see about_Splatting.

**Output:** PSObject

**Example**

```powershell
Get-ChildItem *.log | ForEach-Object { $_.Name.ToUpper() }
```

### Get-Command (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: gcm

Lists the commands available in the session (cmdlets, functions, aliases, scripts, programs), filtered by name, verb, noun or module.

**Syntax** (one line per parameter set)

```powershell
Get-Command [[-ArgumentList] <Object[]>] [-Verb <string[]>] [-Noun <string[]>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-TotalCount <int>] [-Syntax] [-ShowCommandInfo] [-All] [-ListImported] [-ParameterName <string[]>] [-ParameterType <PSTypeName[]>] [<CommonParameters>]

Get-Command [[-Name] <string[]>] [[-ArgumentList] <Object[]>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-CommandType <CommandTypes>] [-TotalCount <int>] [-Syntax] [-ShowCommandInfo] [-All] [-ListImported] [-ParameterName <string[]>] [-ParameterType <PSTypeName[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies an array of names. This cmdlet gets only commands that have the specified name.
- `-Verb` <String[]> · pipeline by property name · wildcards — Specifies an array of command verbs. This cmdlet gets commands, which include cmdlets, functions, and aliases, that have names that include the specified verb.
- `-Noun` <String[]> · pipeline by property name · wildcards — Specifies an array of command nouns. This cmdlet gets commands, which include cmdlets, functions, and aliases, that have names that include the specified noun.
- `-Module` <String[]> · pipeline by property name · alias -PSSnapin · wildcards — Specifies an array of modules. This cmdlet gets the commands that came from the specified modules or snap-ins.
- `-FullyQualifiedModule` <ModuleSpecification[]> · pipeline by property name — The value can be a module name or a full module specification. PowerShell searches the PSModulePath for the specified module.
- `-CommandType` <CommandTypes> · pipeline by property name · alias -Type — Specifies the types of commands that this cmdlet gets. Enter one or more command types.
- `-TotalCount` <Int32> · pipeline by property name — Specifies the number of commands to get. You can use this parameter to limit the output of a command.
- `-Syntax` switch · pipeline by property name — Indicates that this cmdlet gets only the following specified data about the command:
- `-ShowCommandInfo` switch — Indicates that this cmdlet displays command information.
- `-ArgumentList` <Object[]> · position 1 · alias -Args — Specifies an array of arguments. This cmdlet gets information about a cmdlet or function when it's used with the specified parameters ("arguments").
- `-All` switch · pipeline by property name — Indicates that this cmdlet gets all commands, including commands of the same type that have the same name.
- `-ListImported` switch · pipeline by property name — Indicates that this cmdlet gets only commands in the current session.
- `-ParameterName` <String[]> · wildcards — Specifies an array of parameter names. This cmdlet gets commands in the session that have the specified parameters.
- `-ParameterType` <PSTypeName[]> · wildcards — Specifies an array of parameter names. This cmdlet gets commands in the session that have parameters of the specified type.

**Output:** CommandInfo; AliasInfo; ApplicationInfo; CmdletInfo; FunctionInfo; WorkflowInfo

**Example**

```powershell
Get-Command -Verb Get -Module Microsoft.PowerShell.Management
```

### Get-Help (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Shows help for a command or concept (about_ topics), with options for examples, full detail or a single parameter.

**Syntax** (one line per parameter set)

```powershell
Get-Help [[-Name] <string>] [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [-Full] [<CommonParameters>]

Get-Help [[-Name] <string>] -Detailed [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -Examples [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -Parameter <string> [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -Online [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]

Get-Help [[-Name] <string>] -ShowWindow [-Path <string>] [-Category <string[]>] [-Component <string[]>] [-Functionality <string[]>] [-Role <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · position 0 · pipeline by property name · wildcards — Gets help about the specified command or concept. Enter the name of a cmdlet, function, provider, script, or workflow, such as Get-Member, a conceptual article name, such as about_Objects, or an alias, such as ls.
- `-Path` <String> · wildcards — Gets help that explains how the cmdlet works in the specified provider path.
- `-Category` <String[]> · values: Alias, Cmdlet, Provider, General, FAQ, Glossary, HelpFile, ScriptCommand, Function, Filter, ExternalScript, All, DefaultHelp, Workflow … — Displays help only for items in the specified category and their aliases.
- `-Component` <String[]> · wildcards — Displays commands with the specified component value, such as Exchange.
- `-Functionality` <String[]> · wildcards — Displays help for items with the specified functionality. Enter the functionality.
- `-Role` <String[]> · wildcards — Displays help customized for the specified user role. Enter a role.
- `-Detailed` switch · **required** — Adds parameter descriptions and examples to the basic help display. This parameter is effective only when the help files are installed on the computer.
- `-Full` switch — Displays the entire help article for a cmdlet. Full includes parameter descriptions and attributes, examples, input and output object types, and additional notes.
- `-Examples` switch · **required** — Displays only the name, synopsis, and examples. This parameter is effective only when the help files are installed on the computer.
- `-Parameter` <String> · **required** · wildcards — Displays only the detailed descriptions of the specified parameters. Wildcards are permitted.
- `-Online` switch · **required** — Displays the online version of a help article in the default browser. This parameter is valid only for cmdlet, function, workflow, and script help articles.
- `-ShowWindow` switch · **required** — Displays the help topic in a window for easier reading. The window includes a Find search feature and a Settings box that lets you set options for the display, including options to display only selected sections of a help topic.

**Output:** ExtendedCmdletHelpInfo; String; MamlCommandHelpInfo

**Example**

```powershell
Get-Help Get-Process -Examples
```

### Get-History (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: ghy, h, history

Lists the commands entered in the current session, with their IDs and run times.

**Syntax** (one line per parameter set)

```powershell
Get-History [[-Id] <long[]>] [[-Count] <int>] [<CommonParameters>]
```

**Arguments**

- `-Id` <Int64[]> · position 0 · pipeline by value — Specifies an array of the IDs of entries in the session history. Get-History gets only specified entries.
- `-Count` <Int32> · position 1 — Specifies the number of the most recent history entries that this cmdlet gets.

**Output:** HistoryInfo

**Example**

```powershell
Get-History -Count 5
```

### Get-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: gjb

Lists the background jobs in the current session and their state (Running, Completed, Failed...).

**Syntax** (one line per parameter set)

```powershell
Get-Job [[-Id] <int[]>] [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-InstanceId] <guid[]> [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-Name] <string[]> [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-State] <JobState> [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [<CommonParameters>]

Get-Job [-IncludeChildJob] [-ChildJobState <JobState>] [-HasMoreData <bool>] [-Before <datetime>] [-After <datetime>] [-Newest <int>] [-Command <string[]>] [<CommonParameters>]

Get-Job [-Filter] <hashtable> [<CommonParameters>]
```

**Arguments**

- `-IncludeChildJob` switch — Indicates that this cmdlet returns child jobs, in addition to parent jobs.
- `-ChildJobState` <JobState> — Gets only the child jobs that have the specified state.
- `-HasMoreData` <Boolean> — Indicates whether this cmdlet gets only jobs that have the specified HasMoreData property value.
- `-Before` <DateTime> — Gets completed jobs that ended before the specified date and time. Enter a DateTime object.
- `-After` <DateTime> — Gets completed jobs that ended after the specified date and time. Enter a DateTime object, such as one returned by the Get-Date cmdlet or a string that can be converted to a DateTime object, such as Dec 1, 2012 2:00 AM or 11/06.
- `-Newest` <Int32> — Specifies a number of jobs to get. This cmdlet gets the jobs that ended most recently.
- `-Id` <Int32[]> · position 0 · pipeline by property name — Specifies an array of IDs of jobs that this cmdlet gets.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies an array of instance friendly names of jobs that this cmdlet gets.
- `-InstanceId` <Guid[]> · **required** · position 0 · pipeline by property name — Specifies an array of instance IDs of jobs that this cmdlet gets. The default is all jobs.
- `-State` <JobState> · **required** · position 0 · pipeline by property name — Specifies a job state. This cmdlet gets only jobs in the specified state.
- `-Command` <String[]> · pipeline by property name · wildcards — Specifies an array of commands as strings. This cmdlet gets the jobs that include the specified commands.
- `-Filter` <Hashtable> · **required** · position 0 · pipeline by property name — Specifies a hash table of conditions. This cmdlet gets jobs that satisfy all of the conditions.

**Output:** RemotingJob

**Example**

```powershell
Get-Job -State Running
```

### Get-Module (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: gmo

Lists the modules imported in the session, or with -ListAvailable every module that can be imported.

**Syntax** (one line per parameter set)

```powershell
Get-Module [[-Name] <string[]>] [-FullyQualifiedName <ModuleSpecification[]>] [-All] [<CommonParameters>]

Get-Module [[-Name] <string[]>] -PSSession <PSSession> [-FullyQualifiedName <ModuleSpecification[]>] [-ListAvailable] [-PSEdition <string>] [-Refresh] [<CommonParameters>]

Get-Module [[-Name] <string[]>] -ListAvailable [-FullyQualifiedName <ModuleSpecification[]>] [-All] [-PSEdition <string>] [-Refresh] [<CommonParameters>]

Get-Module [[-Name] <string[]>] -CimSession <CimSession> [-FullyQualifiedName <ModuleSpecification[]>] [-ListAvailable] [-Refresh] [-CimResourceUri <uri>] [-CimNamespace <string>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value · wildcards — Specifies names or name patterns of modules that this cmdlet gets. Wildcard characters are permitted.
- `-FullyQualifiedName` <ModuleSpecification[]> · pipeline by property name — The value can be a module name, a full module specification, or a path to a module file.
- `-All` switch — Indicates that this cmdlet gets all modules in each module folder, including nested modules, manifest (.psd1) files, script module (.psm1) files, and binary module (.dll) files.
- `-ListAvailable` switch · **required** — By default, Get-Module only returns the modules that have been imported into the current session.
- `-PSEdition` <String> — Gets the modules that support specified edition of PowerShell.
- `-Refresh` switch — Indicates that this cmdlet refreshes the cache of installed commands. The command cache is created when the session starts.
- `-PSSession` <PSSession> · **required** — Gets the modules in the specified user-managed PowerShell session (PSSession).
- `-CimSession` <CimSession> · **required** — Specifies a CIM session on the remote computer. Enter a variable that contains the CIM session or a command that gets the CIM session, such as a Get-CimSession command.
- `-CimResourceUri` <Uri> — Specifies an alternate location for CIM modules. The default value is the resource URI of the Module Discovery WMI provider on the remote computer.
- `-CimNamespace` <String> — Specifies the namespace of an alternate CIM provider that exposes CIM modules.

**Output:** PSModuleInfo

**Example**

```powershell
Get-Module -ListAvailable -Name Az*
```

### Get-PSHostProcessInfo (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Lists the local processes that host a PowerShell engine (candidates for Enter-PSHostProcess).

**Syntax** (one line per parameter set)

```powershell
Get-PSHostProcessInfo [[-Name] <string[]>] [<CommonParameters>]

Get-PSHostProcessInfo [-Process] <Process[]> [<CommonParameters>]

Get-PSHostProcessInfo [-Id] <int[]> [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — Specifies a process by the process name. To get a process name, run the Get-Process cmdlet.
- `-Process` <Process[]> · **required** · position 0 · pipeline by value — Specifies a process by the process object. The simplest way to use this parameter is to save the results of a Get-Process command that returns process that you want to enter in a variable, and then specify the variable as the value of this parameter.
- `-Id` <Int32[]> · **required** · position 0 — Specifies a process by the process ID. To get a process ID, run the Get-Process cmdlet.

**Output:** PSHostProcessInfo

**Example**

```powershell
Get-PSHostProcessInfo -Name powershell
```

### Get-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: gsn

Lists the PSSessions created in this session, or the sessions on a remote computer.

**Syntax** (one line per parameter set)

```powershell
Get-PSSession [-Name <string[]>] [<CommonParameters>]

Get-PSSession [-ComputerName] <string[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession [-ComputerName] <string[]> -InstanceId <guid[]> [-ApplicationName <string>] [-ConfigurationName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession [-ConnectionUri] <uri[]> -InstanceId <guid[]> [-ConfigurationName <string>] [-AllowRedirection] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession [-ConnectionUri] <uri[]> [-ConfigurationName <string>] [-AllowRedirection] [-Name <string[]>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-ThrottleLimit <int>] [-State <SessionFilterState>] [-SessionOption <PSSessionOption>] [<CommonParameters>]

Get-PSSession -ContainerId <string[]> [-ConfigurationName <string>] [-Name <string[]>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -InstanceId <guid[]> -ContainerId <string[]> [-ConfigurationName <string>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -VMId <guid[]> [-ConfigurationName <string>] [-Name <string[]>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -InstanceId <guid[]> -VMId <guid[]> [-ConfigurationName <string>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -VMName <string[]> [-ConfigurationName <string>] [-Name <string[]>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession -InstanceId <guid[]> -VMName <string[]> [-ConfigurationName <string>] [-State <SessionFilterState>] [<CommonParameters>]

Get-PSSession [-InstanceId <guid[]>] [<CommonParameters>]

Get-PSSession [-Id] <int[]> [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · **required** · position 0 · pipeline by property name · alias -Cn — Specifies an array of names of computers. Gets the sessions that connect to the specified computers.
- `-ApplicationName` <String> · pipeline by property name · default All sessions — Specifies the name of an application. This cmdlet connects only to sessions that use the specified application.
- `-ConnectionUri` <Uri[]> · **required** · position 0 · pipeline by property name · alias -URI, -CU · default Http://localhost:5985/WSMAN — Specifies a URI that defines the connection endpoint for the temporary session in which the Get-PSSession command runs.
- `-ConfigurationName` <String> · pipeline by property name · default All sessions — Specifies the name of a configuration. This cmdlet gets only to sessions that use the specified session configuration.
- `-AllowRedirection` switch — Indicates that this cmdlet allows redirection of this connection to an alternate Uniform Resource Identifier (URI).
- `-Name` <String[]> · pipeline by property name · default All sessions · wildcards — Specifies an array of session names. This cmdlet gets only the sessions that have the specified friendly names.
- `-InstanceId` <Guid[]> · **required** · pipeline by property name · default All sessions — Specifies an array of instance IDs of sessions. This cmdlet gets only the sessions with the specified instance IDs.
- `-Credential` <PSCredential> · default Current user — Specifies a user credential. This cmdlet runs the command with the permissions of the specified user.
- `-Authentication` <AuthenticationMechanism> · default Default — Specifies the mechanism that is used to authenticate credentials for the session in which the Get-PSSession command runs.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to create the session in which the Get-PSSession command runs.
- `-Port` <Int32> · default 5985, 5986 — Specifies the specified network port that is used for the temporary connection in which the Get-PSSession command runs.
- `-UseSSL` switch — Indicates that this cmdlet uses the Secure Sockets Layer (SSL) protocol to establish the connection in which the Get-PSSession command runs.
- `-ThrottleLimit` <Int32> · default 32 — Specifies the maximum number of concurrent connections that can be established to run the Get-PSSession command.
- `-State` <SessionFilterState> · default All — Specifies a session state. This cmdlet gets only sessions in the specified state.
- `-SessionOption` <PSSessionOption> — Specifies advanced options for the session. Enter a SessionOption object, such as one that you create by using the New-PSSessionOption cmdlet, or a hash table in which the keys are session option names and the values are session option values.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name · default All sessions — Specifies an array of session IDs. This cmdlet gets only the sessions with the specified IDs.
- `-ContainerId` <String[]> · **required** · pipeline by property name — Specifies an array of IDs of containers. This cmdlet starts an interactive session with each of the specified containers.
- `-VMId` <Guid[]> · **required** · pipeline by property name · alias -VMGuid — Specifies an array of ID of virtual machines. This cmdlet starts an interactive session with each of the specified virtual machines.
- `-VMName` <String[]> · **required** · pipeline by property name — Specifies an array of names of virtual machines. This cmdlet starts an interactive session with each of the specified virtual machines.

**Output:** PSSession

**Example**

```powershell
Get-PSSession -ComputerName Server01
```

### Get-PSSessionCapability (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Shows which commands a given user can run in a constrained (JEA) session configuration.

**Syntax** (one line per parameter set)

```powershell
Get-PSSessionCapability [-ConfigurationName] <string> [-Username] <string> [-Full] [<CommonParameters>]
```

**Arguments**

- `-ConfigurationName` <String> · **required** · position 0 — Specifies the constrained session configuration (endpoint) that you are inspecting.
- `-Username` <String> · **required** · position 1 — Specifies the user whose capabilities you are inspecting.
- `-Full` switch — Indicates that this cmdlet returns the entire initial session state for the specified user at the specified constrained endpoint.

**Output:** AliasInfo; FunctionInfo; InitialSessionState

**Example**

```powershell
Get-PSSessionCapability -ConfigurationName HelpDesk -Username CONTOSO\Ann
```

### Get-PSSessionConfiguration (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Lists the session configurations (remoting endpoints) registered on this computer.

**Syntax** (one line per parameter set)

```powershell
Get-PSSessionConfiguration [[-Name] <string[]>] [-Force] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · default All session configurations on the local computer · wildcards — Gets only the session configurations with the specified name or name pattern.
- `-Force` switch — Suppresses the prompt to restart the WinRM service, if the service is not already running.

**Output:** PSSessionConfigurationCommands#PSSessionConfiguration

**Example**

```powershell
Get-PSSessionConfiguration | Format-Table Name, Permission
```

### Get-PSSnapin (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: gsnp

Lists the snap-ins loaded in the session, or with -Registered all snap-ins installed on the computer.

**Syntax** (one line per parameter set)

```powershell
Get-PSSnapin [[-Name] <string[]>] [-Registered] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — Specifies an array of snap-in names. This cmdlet gets only the specified Windows PowerShell snap-ins.
- `-Registered` switch — Indicates that this cmdlet gets the Windows PowerShell snap-ins that have been registered on the system even if they have not yet been added to the session.

**Output:** PSSnapInInfo

**Example**

```powershell
Get-PSSnapin -Registered
```

### Get-Verb (PowerShell 5.1 function)

Module Microsoft.PowerShell.Core

Lists the approved PowerShell verbs and their groups, for naming your own commands.

**Syntax** (one line per parameter set)

```powershell
Get-Verb [[-verb] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-verb` <String[]> · position 0 · pipeline by value · default All verbs · wildcards — Gets only the specified verbs. Enter the name of a verb or a name pattern.

**Output:** Selected.Microsoft.PowerShell.Commands.MemberDefinition

**Example**

```powershell
Get-Verb | Where-Object Group -eq Data
```

### Import-Module (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: ipmo

Loads a module (by name or path) into the session so its commands become available.

**Syntax** (one line per parameter set)

```powershell
Import-Module [-Name] <string[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-MinimumVersion <version>] [-MaximumVersion <string>] [-RequiredVersion <version>] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-Name] <string[]> -PSSession <PSSession> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-MinimumVersion <version>] [-MaximumVersion <string>] [-RequiredVersion <version>] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-Name] <string[]> -CimSession <CimSession> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-MinimumVersion <version>] [-MaximumVersion <string>] [-RequiredVersion <version>] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [-CimResourceUri <uri>] [-CimNamespace <string>] [<CommonParameters>]

Import-Module [-FullyQualifiedName] <ModuleSpecification[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-FullyQualifiedName] <ModuleSpecification[]> -PSSession <PSSession> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-Assembly] <Assembly[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]

Import-Module [-ModuleInfo] <psmoduleinfo[]> [-Global] [-Prefix <string>] [-Function <string[]>] [-Cmdlet <string[]>] [-Variable <string[]>] [-Alias <string[]>] [-Force] [-PassThru] [-AsCustomObject] [-ArgumentList <Object[]>] [-DisableNameChecking] [-NoClobber] [-Scope <string>] [<CommonParameters>]
```

**Arguments**

- `-Global` switch — Indicates that this cmdlet imports modules into the global session state so they're available to all commands in the session.
- `-Prefix` <String> — Specifies a prefix that this cmdlet adds to the nouns in the names of imported module members.
- `-Name` <String[]> · **required** · position 0 · pipeline by value · wildcards — Specifies the names of the modules to import. Enter the name of the module or the name of a file in the module, such as a .psd1, .psm1, .dll, or .ps1 file.
- `-FullyQualifiedName` <ModuleSpecification[]> · **required** · position 0 · pipeline by value — The value can be a module name, a full module specification, or a path to a module file.
- `-Assembly` <Assembly[]> · **required** · position 0 · pipeline by value — Specifies an array of assembly objects. This cmdlet imports the cmdlets and providers implemented in the specified assembly objects.
- `-Function` <String[]> · wildcards — Specifies an array of functions that this cmdlet imports from the module into the current session.
- `-Cmdlet` <String[]> · wildcards — Specifies an array of cmdlets that this cmdlet imports from the module into the current session.
- `-Variable` <String[]> · wildcards — Specifies an array of variables that this cmdlet imports from the module into the current session.
- `-Alias` <String[]> · wildcards — Specifies the aliases that this cmdlet imports from the module into the current session.
- `-Force` switch — This parameter causes a module to be loaded, or reloaded, over top of the current one.
- `-PassThru` switch — Returns an object representing the imported module. By default, this cmdlet doesn't generate any output.
- `-AsCustomObject` switch — Indicates that this cmdlet returns a custom object with members that represent the imported module members.
- `-MinimumVersion` <Version> · alias -Version — Specifies a minimum version. This cmdlet imports only a version of the module that's greater than or equal to the specified value.
- `-MaximumVersion` <String> — Specifies a maximum version. This cmdlet imports only a version of the module that's less than or equal to the specified value.
- `-RequiredVersion` <Version> — Specifies a version of the module that this cmdlet imports. If the version isn't installed, Import-Module generates an error.
- `-ModuleInfo` <PSModuleInfo[]> · **required** · position 0 · pipeline by value — Specifies an array of module objects to import. Enter a variable that contains the module objects, or a command that gets the module objects, such as the following command: Get-Module -ListAvailable.
- `-ArgumentList` <Object[]> · alias -Args — Specifies an array of arguments, or parameter values, that are passed to a script module during the Import-Module command.
- `-DisableNameChecking` switch — Indicates that this cmdlet suppresses the message that warns you when you import a cmdlet or function whose name includes an unapproved verb or a prohibited character.
- `-NoClobber` switch · alias -NoOverwrite — Prevents importing commands that have the same names as existing commands in the current session.
- `-Scope` <String> · values: Local, Global — Specifies a scope to import the module in.
- `-PSSession` <PSSession> · **required** — Specifies a PowerShell user-managed session (PSSession) from which this cmdlet imports modules into the current session.
- `-CimSession` <CimSession> · **required** — Specifies a CIM session on the remote computer. Enter a variable that contains the CIM session or a command that gets the CIM session, such as a Get-CimSession command.
- `-CimResourceUri` <Uri> — Specifies an alternate location for CIM modules. The default value is the resource URI of the Module Discovery WMI provider on the remote computer.
- `-CimNamespace` <String> — Specifies the namespace of an alternate CIM provider that exposes CIM modules.

**Output:** None; PSModuleInfo; PSCustomObject

**Example**

```powershell
Import-Module .\Tools\MyTools.psm1 -Force
```

### Invoke-Command (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: icm

Runs a script block on local or remote computers (or in PSSessions) and returns the results; can run as a job.

**Syntax** (one line per parameter set)

```powershell
Invoke-Command [-ScriptBlock] <scriptblock> [-NoNewScope] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-Session] <PSSession[]>] [-ScriptBlock] <scriptblock> [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-Session] <PSSession[]>] [-FilePath] <string> [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-ComputerName] <string[]>] [-ScriptBlock] <scriptblock> [-Credential <pscredential>] [-Port <int>] [-UseSSL] [-ConfigurationName <string>] [-ApplicationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-SessionName <string[]>] [-HideComputerName] [-JobName <string>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [-CertificateThumbprint <string>] [<CommonParameters>]

Invoke-Command [[-ComputerName] <string[]>] [-FilePath] <string> [-Credential <pscredential>] [-Port <int>] [-UseSSL] [-ConfigurationName <string>] [-ApplicationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-SessionName <string[]>] [-HideComputerName] [-JobName <string>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [[-ConnectionUri] <uri[]>] [-ScriptBlock] <scriptblock> [-Credential <pscredential>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-HideComputerName] [-JobName <string>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [-CertificateThumbprint <string>] [<CommonParameters>]

Invoke-Command [[-ConnectionUri] <uri[]>] [-FilePath] <string> [-Credential <pscredential>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-InDisconnectedSession] [-HideComputerName] [-JobName <string>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-EnableNetworkAccess] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-VMId] <guid[]> [-ScriptBlock] <scriptblock> -Credential <pscredential> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-ScriptBlock] <scriptblock> -Credential <pscredential> -VMName <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-VMId] <guid[]> [-FilePath] <string> -Credential <pscredential> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-FilePath] <string> -Credential <pscredential> -VMName <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-ScriptBlock] <scriptblock> -ContainerId <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-RunAsAdministrator] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Invoke-Command [-FilePath] <string> -ContainerId <string[]> [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AsJob] [-HideComputerName] [-JobName <string>] [-RunAsAdministrator] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]
```

**Arguments**

- `-Session` <PSSession[]> · position 0 — Specifies an array of sessions in which this cmdlet runs the command. Enter a variable that contains PSSession objects or a command that creates or gets the PSSession objects, such as a New-PSSession or Get-PSSession command.
- `-ComputerName` <String[]> · position 0 · alias -Cn · default Local computer — Specifies the computers on which the command runs. The default is the local computer.
- `-Credential` <PSCredential> · **required** · pipeline by property name · default Current user — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Port` <Int32> — Specifies the network port on the remote computer that is used for this command.
- `-UseSSL` switch — Indicates that this cmdlet uses the Secure Sockets Layer (SSL) protocol to establish a connection to the remote computer.
- `-ConfigurationName` <String> · pipeline by property name · default $PSSessionConfigurationName if set on the local computer, otherwise Microsoft.PowerShell — Specifies the session configuration that is used for the new PSSession.
- `-ApplicationName` <String> · pipeline by property name · default $PSSessionApplicationName if set on the local computer, otherwise WSMAN — Specifies the application name segment of the connection URI. Use this parameter to specify the application name when you aren't using the ConnectionURI parameter in the command.
- `-ThrottleLimit` <Int32> · default 32 — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-ConnectionUri` <Uri[]> · position 0 · alias -URI, -CU · default http://localhost:5985/WSMAN — Specifies a Uniform Resource Identifier (URI) that defines the connection endpoint of the session.
- `-AsJob` switch — Indicates that this cmdlet runs the command as a background job on a remote computer.
- `-InDisconnectedSession` switch · alias -Disconnected — Indicates that this cmdlet runs a command or script in a disconnected session.
- `-SessionName` <String[]> — Specifies a friendly name for a disconnected session. You can use the name to refer to the session in subsequent commands, such as a Get-PSSession command.
- `-HideComputerName` switch · alias -HCN — Indicates that this cmdlet omits the computer name of each object from the output display.
- `-JobName` <String> · default Job<n> — Specifies a friendly name for the background job. By default, jobs are named Job<n>, where <n> is an ordinal number.
- `-ScriptBlock` <ScriptBlock> · **required** · position 1 · alias -Command — Specifies the commands to run. Enclose the commands in braces ({}) to create a scriptblock.
- `-NoNewScope` switch — Indicates that this cmdlet runs the specified command in the current scope.
- `-FilePath` <String> · **required** · position 1 · alias -PSPath — Specifies a local script that this cmdlet runs on one or more remote computers.
- `-AllowRedirection` switch — Allows redirection of this connection to an alternate Uniform Resource Identifier (URI).
- `-SessionOption` <PSSessionOption> — Specifies advanced options for the session. Enter a SessionOption object, such as one that you create using the New-PSSessionOption cmdlet, or a hash table in which the keys are session option names and the values are session option values.
- `-Authentication` <AuthenticationMechanism> · default Default — Specifies the mechanism that's used to authenticate the user's credentials.
- `-EnableNetworkAccess` switch — Indicates that this cmdlet adds an interactive security token to loopback sessions.
- `-RunAsAdministrator` switch — Indicates that this cmdlet invokes a command as an Administrator.
- `-InputObject` <PSObject> · pipeline by value — Specifies input to the command. Enter a variable that contains the objects or type a command or expression that gets the objects.
- `-ArgumentList` <Object[]> · alias -Args — Supplies the values of parameters for the scriptblock. The parameters in the scriptblock are passed by position from the array value supplied to ArgumentList.
- `-VMId` <Guid[]> · **required** · position 0 · pipeline by property name · alias -VMGuid — Specifies an array of IDs of virtual machines.
- `-VMName` <String[]> · **required** · pipeline by property name — Specifies an array of names of virtual machines.
- `-ContainerId` <String[]> · **required** · pipeline by property name — Specifies an array of container IDs.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to connect to the disconnected session.

**Output:** PSRemotingJob; PSSession; Object

**Example**

```powershell
Invoke-Command -ComputerName Server01, Server02 -ScriptBlock { Get-Service Spooler }
```

### Invoke-History (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: ihy, r

Runs a command again from the session history, by ID or by the start of its text. Alias: r.

**Syntax** (one line per parameter set)

```powershell
Invoke-History [[-Id] <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Id` <String> · position 0 · pipeline by property name — Specifies the Id of a command in the history. You can type the Id number of the command or the first few characters of the command.

**Output:** None

**Example**

```powershell
Invoke-History -Id 12
```

### New-Module (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: nmo

Creates a dynamic module in memory from a script block; useful for quick, temporary function libraries.

**Syntax** (one line per parameter set)

```powershell
New-Module [-ScriptBlock] <scriptblock> [-Function <string[]>] [-Cmdlet <string[]>] [-ReturnResult] [-AsCustomObject] [-ArgumentList <Object[]>] [<CommonParameters>]

New-Module [-Name] <string> [-ScriptBlock] <scriptblock> [-Function <string[]>] [-Cmdlet <string[]>] [-ReturnResult] [-AsCustomObject] [-ArgumentList <Object[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by value — Specifies a name for the new module. You can also pipe a module name to New-Module.
- `-ScriptBlock` <ScriptBlock> · **required** · position 1 — Specifies the contents of the dynamic module. Enclose the contents in braces ({}) to create a scriptblock.
- `-Function` <String[]> · wildcards — Specifies an array of functions that this cmdlet exports from the module into the current session.
- `-Cmdlet` <String[]> — Specifies an array of cmdlets that this cmdlet exports from the module into the current session.
- `-ReturnResult` switch — Indicates that this cmdlet runs the scriptblock and returns the scriptblock results instead of returning a module object.
- `-AsCustomObject` switch — Indicates that this cmdlet returns a custom object that represents the dynamic module.
- `-ArgumentList` <Object[]> · alias -Args — Specifies an array of arguments which are parameter values that are passed to the scriptblock.

**Output:** PSModuleInfo; PSCustomObject; Object

**Example**

```powershell
New-Module -Name Temp -ScriptBlock { function Hello { 'hi' } } | Import-Module
```

### New-ModuleManifest (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Creates a module manifest (.psd1) that describes a module: version, author, files and exports.

**Syntax** (one line per parameter set)

```powershell
New-ModuleManifest [-Path] <string> [-NestedModules <Object[]>] [-Guid <guid>] [-Author <string>] [-CompanyName <string>] [-Copyright <string>] [-RootModule <string>] [-ModuleVersion <version>] [-Description <string>] [-ProcessorArchitecture <ProcessorArchitecture>] [-PowerShellVersion <version>] [-ClrVersion <version>] [-DotNetFrameworkVersion <version>] [-PowerShellHostName <string>] [-PowerShellHostVersion <version>] [-RequiredModules <Object[]>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-ScriptsToProcess <string[]>] [-RequiredAssemblies <string[]>] [-FileList <string[]>] [-ModuleList <Object[]>] [-FunctionsToExport <string[]>] [-AliasesToExport <string[]>] [-VariablesToExport <string[]>] [-CmdletsToExport <string[]>] [-DscResourcesToExport <string[]>] [-CompatiblePSEditions <string[]>] [-PrivateData <Object>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string>] [-HelpInfoUri <string>] [-PassThru] [-DefaultCommandPrefix <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 — Specifies the path and file name of the new module manifest. Enter a path and file name with a .psd1 file name extension, such as $PSHOME\Modules\MyModule\MyModule.psd1.
- `-NestedModules` <Object[]> — Specifies script modules (.psm1) and binary modules (.dll) that are imported into the module's session state.
- `-Guid` <Guid> · default A GUID generated for the module — Specifies a unique identifier for the module. The GUID can be used to distinguish among modules with the same name.
- `-Author` <String> · default Name of the current user — Specifies the module author.
- `-CompanyName` <String> · default "Unknown" — Identifies the company or vendor who created the module.
- `-Copyright` <String> · default (c) <year> <username>. All rights reserved. — Specifies a copyright statement for the module.
- `-RootModule` <String> · alias -ModuleToProcess — Specifies the primary or root file of the module. Enter the file name of a script (.ps1), a script module (.psm1), a module manifest(.psd1), an assembly (.dll), a cmdlet definition XML file (.cdxml), or a workflow (.xaml).
- `-ModuleVersion` <Version> · default 1.0 — Specifies the module's version.
- `-Description` <String> — Describes the contents of the module.
- `-ProcessorArchitecture` <ProcessorArchitecture> — Specifies the processor architecture that the module requires. Valid values are x86, AMD64, IA64, MSIL, and None (unknown or unspecified).
- `-PowerShellVersion` <Version> — Specifies the minimum version of PowerShell that works with this module.
- `-ClrVersion` <Version> — Specifies the minimum version of the Common Language Runtime (CLR) of the Microsoft .NET Framework that the module requires.
- `-DotNetFrameworkVersion` <Version> — Specifies the minimum version of the Microsoft .NET Framework that the module requires.
- `-PowerShellHostName` <String> — Specifies the name of the PowerShell host program that the module requires.
- `-PowerShellHostVersion` <Version> — Specifies the minimum version of the PowerShell host program that works with the module.
- `-RequiredModules` <Object[]> — Specifies modules that must be in the global session state. If the required modules aren't in the global session state, PowerShell imports them.
- `-TypesToProcess` <String[]> — Specifies the type files (.ps1xml) that run when the module is imported.
- `-FormatsToProcess` <String[]> — Specifies the formatting files (.ps1xml) that run when the module is imported.
- `-ScriptsToProcess` <String[]> — Specifies script (.ps1) files that run in the caller's session state when the module is imported.
- `-RequiredAssemblies` <String[]> — Specifies the assembly (.dll) files that the module requires. Enter the assembly file names.
- `-FileList` <String[]> — Specifies all items that are included in the module.
- `-ModuleList` <Object[]> — Lists all modules that are included in this module.
- `-FunctionsToExport` <String[]> · default * (all) · wildcards — Specifies the functions that the module exports. Wildcards are permitted.
- `-AliasesToExport` <String[]> · default * (all) · wildcards — Specifies the aliases that the module exports. Wildcards are permitted.
- `-VariablesToExport` <String[]> · default * (all) · wildcards — Specifies the variables that the module exports. Wildcards are permitted.
- `-CmdletsToExport` <String[]> · default * (all) · wildcards — Specifies the cmdlets that the module exports. Wildcards are permitted.
- `-DscResourcesToExport` <String[]> · wildcards — Specifies the Desired State Configuration (DSC) resources that the module exports.
- `-CompatiblePSEditions` <String[]> · values: Desktop, Core — Specifies the module's compatible PSEditions. For information about PSEdition, see Modules with compatible PowerShell Editions.
- `-PrivateData` <Object> — Specifies data that is passed to the module when it's imported.
- `-Tags` <String[]> — Specifies an array of tags.
- `-ProjectUri` <Uri> — Specifies the URL of a web page about this project.
- `-LicenseUri` <Uri> — Specifies the URL of licensing terms for the module.
- `-IconUri` <Uri> — Specifies the URL of an icon for the module. The specified icon is displayed on the gallery web page for the module.
- `-ReleaseNotes` <String> — Specifies release notes.
- `-HelpInfoUri` <String> — Specifies the internet address of the HelpInfo XML file for the module.
- `-PassThru` switch — Writes the resulting module manifest to the console and creates a .psd1 file.
- `-DefaultCommandPrefix` <String> — Specifies a prefix that is prepended to the nouns of all commands in the module when they're imported into a session.

**Output:** None; String

**Example**

```powershell
New-ModuleManifest -Path .\MyTools.psd1 -RootModule MyTools.psm1 -ModuleVersion 1.0.0 -Author 'Ann'
```

### New-PSRoleCapabilityFile (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Creates a role capability file (.psrc) that lists the commands a JEA role may use.

**Syntax** (one line per parameter set)

```powershell
New-PSRoleCapabilityFile [-Path] <string> [-Guid <guid>] [-Author <string>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-ModulesToImport <Object[]>] [-VisibleAliases <string[]>] [-VisibleCmdlets <Object[]>] [-VisibleFunctions <Object[]>] [-VisibleExternalCommands <string[]>] [-VisibleProviders <string[]>] [-ScriptsToProcess <string[]>] [-AliasDefinitions <IDictionary[]>] [-FunctionDefinitions <IDictionary[]>] [-VariableDefinitions <Object>] [-EnvironmentVariables <IDictionary>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-AssembliesToLoad <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 — Specifies the path and filename of the role capability file. The file must have a .psrc filename extension.
- `-Guid` <Guid> — Specifies a unique identifier for the role capability file. If you omit this parameter, New-PSRoleCapabilityFile generates a GUID for the file.
- `-Author` <String> — Specifies the user that created the role capability file.
- `-Description` <String> — Specifies a description for the role capability file.
- `-CompanyName` <String> — Identifies the company that created the role capability file. The default value is Unknown.
- `-Copyright` <String> — Specifies a copyright for the role capability file. If you omit this parameter, New-PSRoleCapabilityFile generates a copyright statement using the value of the Author parameter.
- `-ModulesToImport` <Object[]> — Specifies the modules that are automatically imported into sessions that use the role capability file.
- `-VisibleAliases` <String[]> · wildcards — Limits the aliases in the session to those aliases specified in the value of this parameter, plus any aliases that you define in the AliasDefinition parameter.
- `-VisibleCmdlets` <Object[]> · wildcards — Limits the cmdlets in the session to those specified in the value of this parameter.
- `-VisibleFunctions` <Object[]> · wildcards — Limits the functions in the session to those specified in the value of this parameter, plus any functions that you define in the FunctionDefinitions parameter.
- `-VisibleExternalCommands` <String[]> — Limits the external binaries, scripts and commands that can be executed in the session to those specified in the value of this parameter.
- `-VisibleProviders` <String[]> · wildcards — Limits the PowerShell providers in the session to those specified in the value of this parameter.
- `-ScriptsToProcess` <String[]> — Specifies scripts to add to sessions that use the role capability file.
- `-AliasDefinitions` <IDictionary[]> — Adds the specified aliases to sessions that use the role capability file.
- `-FunctionDefinitions` <IDictionary[]> — Adds the specified functions to sessions that expose the role capability.
- `-VariableDefinitions` <Object> — Specifies variables to add to sessions that use the role capability file.
- `-EnvironmentVariables` <IDictionary> — Specifies the environment variables for sessions that expose this role capability file.
- `-TypesToProcess` <String[]> — Specifies type files (.ps1xml) to add to sessions that use the role capability file.
- `-FormatsToProcess` <String[]> — Specifies the formatting files (.ps1xml) that run in sessions that use the role capability file.
- `-AssembliesToLoad` <String[]> — Specifies the assemblies to load into the sessions that use the role capability file.

**Example**

```powershell
New-PSRoleCapabilityFile -Path .\HelpDesk.psrc -VisibleCmdlets Get-Service, Restart-Service
```

### New-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: nsn

Opens a persistent connection (PSSession) to a local or remote computer that several commands can reuse.

**Syntax** (one line per parameter set)

```powershell
New-PSSession [[-ComputerName] <string[]>] [-Credential <pscredential>] [-Name <string[]>] [-EnableNetworkAccess] [-ConfigurationName <string>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-ThrottleLimit <int>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

New-PSSession [-ConnectionUri] <uri[]> [-Credential <pscredential>] [-Name <string[]>] [-EnableNetworkAccess] [-ConfigurationName <string>] [-ThrottleLimit <int>] [-AllowRedirection] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

New-PSSession [-VMId] <guid[]> -Credential <pscredential> [-Name <string[]>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [<CommonParameters>]

New-PSSession -Credential <pscredential> -VMName <string[]> [-Name <string[]>] [-ConfigurationName <string>] [-ThrottleLimit <int>] [<CommonParameters>]

New-PSSession [[-Session] <PSSession[]>] [-Name <string[]>] [-EnableNetworkAccess] [-ThrottleLimit <int>] [<CommonParameters>]

New-PSSession -ContainerId <string[]> [-Name <string[]>] [-ConfigurationName <string>] [-RunAsAdministrator] [-ThrottleLimit <int>] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · position 0 · pipeline by value and by property name · alias -Cn — Specifies an array of names of computers. This cmdlet creates a persistent connection (PSSession) to the specified computer.
- `-Credential` <PSCredential> · **required** · pipeline by property name · default Current user — Specifies a user account that has permission to do this action. The default is the current user.
- `-Session` <PSSession[]> · position 0 · pipeline by value and by property name — Specifies an array of PSSession objects that this cmdlet uses as a model for the new PSSession.
- `-Name` <String[]> — Specifies a friendly name for the PSSession.
- `-EnableNetworkAccess` switch — Indicates that this cmdlet adds an interactive security token to loopback sessions.
- `-ConfigurationName` <String> · pipeline by property name — Specifies the session configuration that is used for the new PSSession.
- `-VMId` <Guid[]> · **required** · position 0 · pipeline by property name · alias -VMGuid — Specifies an array of virtual machine IDs. This cmdlet starts a PowerShell Direct interactive session with each of the specified virtual machines.
- `-VMName` <String[]> · **required** · pipeline by property name — Specifies an array of names of virtual machines. This cmdlet starts a PowerShell Direct interactive session with each of the specified virtual machines.
- `-ContainerId` <String[]> · **required** · pipeline by property name — Specifies an array of IDs of containers. This cmdlet starts an interactive session with each of the specified containers.
- `-RunAsAdministrator` switch — Indicates that the PSSession runs as administrator.
- `-Port` <Int32> — Specifies the network port on the remote computer that is used for this connection.
- `-UseSSL` switch — Indicates that this cmdlet uses the SSL protocol to establish a connection to the remote computer.
- `-ApplicationName` <String> · pipeline by property name — Specifies the application name segment of the connection URI. Use this parameter to specify the application name when you are not using the ConnectionURI parameter in the command.
- `-ThrottleLimit` <Int32> — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-ConnectionUri` <Uri[]> · **required** · position 0 · pipeline by property name · alias -URI, -CU — Specifies a URI that defines the connection endpoint for the session. The URI must be fully qualified.
- `-AllowRedirection` switch — Indicates that this cmdlet allows redirection of this connection to an alternate Uniform Resource Identifier (URI).
- `-SessionOption` <PSSessionOption> — Specifies advanced options for the session. Enter a SessionOption object, such as one that you create by using the New-PSSessionOption cmdlet, or a hash table in which the keys are session option names and the values are session option values.
- `-Authentication` <AuthenticationMechanism> — Specifies the mechanism that is used to authenticate the user's credentials.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** PSSession

**Example**

```powershell
$s = New-PSSession -ComputerName Server01
```

### New-PSSessionConfigurationFile (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: npssc

Creates a session configuration file (.pssc) that defines a remoting endpoint (language mode, roles, visible commands).

**Syntax** (one line per parameter set)

```powershell
New-PSSessionConfigurationFile [-Path] <string> [-SchemaVersion <version>] [-Guid <guid>] [-Author <string>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-SessionType <SessionType>] [-TranscriptDirectory <string>] [-RunAsVirtualAccount] [-RunAsVirtualAccountGroups <string[]>] [-MountUserDrive] [-UserDriveMaximumSize <long>] [-GroupManagedServiceAccount <string>] [-ScriptsToProcess <string[]>] [-RoleDefinitions <IDictionary>] [-RequiredGroups <IDictionary>] [-LanguageMode <PSLanguageMode>] [-ExecutionPolicy <ExecutionPolicy>] [-PowerShellVersion <version>] [-ModulesToImport <Object[]>] [-VisibleAliases <string[]>] [-VisibleCmdlets <Object[]>] [-VisibleFunctions <Object[]>] [-VisibleExternalCommands <string[]>] [-VisibleProviders <string[]>] [-AliasDefinitions <IDictionary[]>] [-FunctionDefinitions <IDictionary[]>] [-VariableDefinitions <Object>] [-EnvironmentVariables <IDictionary>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-AssembliesToLoad <string[]>] [-Full] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 — Specifies the path and filename of the session configuration file. The file must have a .pssc file name extension.
- `-SchemaVersion` <Version> — Specifies the version of the session configuration file schema. The default value is "1.0.0.0".
- `-Guid` <Guid> — Specifies a unique identifier for the session configuration file. If you omit this parameter, New-PSSessionConfigurationFile generates a GUID for the file.
- `-Author` <String> — Specifies the author of the session configuration or the configuration file.
- `-Description` <String> — Specifies a description of the session configuration or the session configuration file.
- `-CompanyName` <String> · default Unknown — Specifies the company that created the session configuration or the configuration file.
- `-Copyright` <String> — Specifies a copyright the session configuration file. The value of this parameter is visible in the session configuration file, but it is not a property of the session configuration object.
- `-SessionType` <SessionType> — Specifies the type of session that is created by using the session configuration.
- `-TranscriptDirectory` <String> — Specifies the directory to place session transcripts for sessions using this session configuration.
- `-RunAsVirtualAccount` switch — Configures sessions using this session configuration to be run as the computer's (virtual) administrator account.
- `-RunAsVirtualAccountGroups` <String[]> — Specifies the security groups to be associated with the virtual account when a session that uses the session configuration is run as a virtual account.
- `-MountUserDrive` switch — Configures sessions that use this session configuration to expose the User: PSDrive.
- `-UserDriveMaximumSize` <Int64> — Specifies the maximum size for user drives exposed in sessions that use this session configuration.
- `-GroupManagedServiceAccount` <String> — Configures sessions using this session configuration to run under the context of the specified Group Managed Service Account.
- `-ScriptsToProcess` <String[]> — Adds the specified scripts to sessions that use the session configuration.
- `-RoleDefinitions` <IDictionary> — Specifies the mapping between security groups (or users) and role capabilities.
- `-RequiredGroups` <IDictionary> — Specifies conditional access rules for users connecting to sessions that use this session configuration.
- `-LanguageMode` <PSLanguageMode> — Determines which elements of the PowerShell language are permitted in sessions that use this session configuration.
- `-ExecutionPolicy` <ExecutionPolicy> — Specifies the execution policy of sessions that use the session configuration.
- `-PowerShellVersion` <Version> — Specifies the version of the PowerShell engine in sessions that use the session configuration.
- `-ModulesToImport` <Object[]> — Specifies the modules and snap-ins that are automatically imported into sessions that use the session configuration.
- `-VisibleAliases` <String[]> · wildcards — Limits the aliases in the session to those specified in the value of this parameter, plus any aliases that you define in the AliasDefinition parameter.
- `-VisibleCmdlets` <Object[]> · wildcards — Limits the cmdlets in the session to those specified in the value of this parameter.
- `-VisibleFunctions` <Object[]> · wildcards — Limits the functions in the session to those specified in the value of this parameter, plus any functions that you define in the FunctionDefinition parameter.
- `-VisibleExternalCommands` <String[]> · wildcards — Limits the external binaries, scripts, and commands that can be executed in the session to those specified in the value of this parameter.
- `-VisibleProviders` <String[]> · wildcards — Limits the PowerShell providers in the session to those specified in the value of this parameter.
- `-AliasDefinitions` <IDictionary[]> — Adds the specified aliases to sessions that use the session configuration.
- `-FunctionDefinitions` <IDictionary[]> — Adds the specified functions to sessions that use the session configuration.
- `-VariableDefinitions` <Object> — Adds the specified variables to sessions that use the session configuration.
- `-EnvironmentVariables` <IDictionary> — Adds environment variables to the session. Enter a hash table in which the keys are the environment variable names and the values are the environment variable values.
- `-TypesToProcess` <String[]> — Adds the specified .ps1xml type files to sessions that use the session configuration.
- `-FormatsToProcess` <String[]> — Specifies the formatting files (.ps1xml) that run in sessions that use the session configuration.
- `-AssembliesToLoad` <String[]> — Specifies the assemblies to load into the sessions that use the session configuration.
- `-Full` switch — Indicates that this operation includes all possible configuration properties in the session configuration file.

**Output:** None

**Example**

```powershell
New-PSSessionConfigurationFile -Path .\JEA.pssc -SessionType RestrictedRemoteServer
```

### New-PSSessionOption (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Creates an object with advanced PSSession options (timeouts, proxy, culture) to pass to New-PSSession and similar.

**Syntax** (one line per parameter set)

```powershell
New-PSSessionOption [-MaximumRedirection <int>] [-NoCompression] [-NoMachineProfile] [-Culture <cultureinfo>] [-UICulture <cultureinfo>] [-MaximumReceivedDataSizePerCommand <int>] [-MaximumReceivedObjectSize <int>] [-OutputBufferingMode <OutputBufferingMode>] [-MaxConnectionRetryCount <int>] [-ApplicationArguments <psprimitivedictionary>] [-OpenTimeout <int>] [-CancelTimeout <int>] [-IdleTimeout <int>] [-ProxyAccessType <ProxyAccessType>] [-ProxyAuthentication <AuthenticationMechanism>] [-ProxyCredential <pscredential>] [-SkipCACheck] [-SkipCNCheck] [-SkipRevocationCheck] [-OperationTimeout <int>] [-NoEncryption] [-UseUTF16] [-IncludePortInSPN] [<CommonParameters>]
```

**Arguments**

- `-MaximumRedirection` <Int32> · default 5 — Determines how many times PowerShell redirects a connection to an alternate Uniform Resource Identifier (URI) before the connection fails.
- `-NoCompression` switch — Turns off packet compression in the session. Compression uses more processor cycles, but it makes transmission faster.
- `-NoMachineProfile` switch — Prevents loading the user's Windows user profile. As a result, the session might be created faster, but user-specific registry settings, items such as environment variables, and certificates are not available in the session.
- `-Culture` <CultureInfo> — Specifies the culture to use for the session. Enter a culture name in <languagecode2>-<country/regioncode2> format (like ja-JP), a variable that contains a CultureInfo object, or a command that gets a CultureInfo object.
- `-UICulture` <CultureInfo> — Specifies the UI culture to use for the session.
- `-MaximumReceivedDataSizePerCommand` <Int32> — Specifies the maximum number of bytes that the local computer can receive from the remote computer in a single command.
- `-MaximumReceivedObjectSize` <Int32> · default 209715200 — Specifies the maximum size of an object that the local computer can receive from the remote computer.
- `-OutputBufferingMode` <OutputBufferingMode> — Determines how command output is managed in disconnected sessions when the output buffer becomes full.
- `-MaxConnectionRetryCount` <Int32> — Specifies the number of times that PowerShell attempts to make a connection to a target machine if the current attempt fails due to network issues.
- `-ApplicationArguments` <PSPrimitiveDictionary> — Specifies a PrimitiveDictionary that's sent to the remote session.
- `-OpenTimeout` <Int32> · alias -OpenTimeoutMSec · default 180000 (3 minutes) — Determines how long the client computer waits for the session connection to be established.
- `-CancelTimeout` <Int32> · alias -CancelTimeoutMSec · default 60000 — Determines how long PowerShell waits for a cancel operation (<kbd>CTRL</kbd>+<kbd>C</kbd>) to finish before ending it.
- `-IdleTimeout` <Int32> · alias -IdleTimeoutMSec — Determines how long the session stays open if the remote computer does not receive any communication from the local computer.
- `-ProxyAccessType` <ProxyAccessType> — Determines which mechanism is used to resolve the hostname.
- `-ProxyAuthentication` <AuthenticationMechanism> · default Negotiate — Specifies the authentication method that is used for proxy resolution.
- `-ProxyCredential` <PSCredential> — Specifies the credentials to use for proxy authentication. Enter a variable that contains a PSCredential object or a command that gets a PSCredential object, such as a Get-Credential command.
- `-SkipCACheck` switch — Specifies that when it connects over HTTPS, the client does not validate that the server certificate is signed by a trusted certification authority (CA).
- `-SkipCNCheck` switch — Specifies that the certificate common name (CN) of the server does not have to match the hostname of the server.
- `-SkipRevocationCheck` switch — Does not validate the revocation status of the server certificate.
- `-OperationTimeout` <Int32> · alias -OperationTimeoutMSec · default 180000 (3 minutes) — Determines the maximum time WinRM waits for positive connection tests from a live connection before initiating a connection time-out.
- `-NoEncryption` switch — Turns off data encryption.
- `-UseUTF16` switch — Indicates that this cmdlet encodes the request in UTF16 format instead of UTF8 format.
- `-IncludePortInSPN` switch — Includes the port number in the Service Principal Name (SPN) used for Kerberos authentication, for example, HTTP://<ComputerName>:5985.

**Output:** PSSessionOption

**Example**

```powershell
$o = New-PSSessionOption -OpenTimeout 60000; New-PSSession -ComputerName Server01 -SessionOption $o
```

### New-PSTransportOption (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Creates an object with advanced transport options (quotas, idle timeout) for a session configuration.

**Syntax** (one line per parameter set)

```powershell
New-PSTransportOption [-MaxIdleTimeoutSec <int>] [-ProcessIdleTimeoutSec <int>] [-MaxSessions <int>] [-MaxConcurrentCommandsPerSession <int>] [-MaxSessionsPerUser <int>] [-MaxMemoryPerSessionMB <int>] [-MaxProcessesPerSession <int>] [-MaxConcurrentUsers <int>] [-IdleTimeoutSec <int>] [-OutputBufferingMode <OutputBufferingMode>] [<CommonParameters>]
```

**Arguments**

- `-MaxIdleTimeoutSec` <Nullable`1> · pipeline by property name — Limits the idle time-out set for each session to the specified value. The default value is [int]::MaxValue (~25 days).
- `-ProcessIdleTimeoutSec` <Nullable`1> · pipeline by property name — Limits the time-out for each host process to the specified value. The default value, 0, means that there is no time-out value for the process.
- `-MaxSessions` <Nullable`1> · pipeline by property name — Limits the number of sessions that use the session configuration. The default value is 25.
- `-MaxConcurrentCommandsPerSession` <Nullable`1> · pipeline by property name — Limits the number of commands that can run at the same time in each session to the specified value.
- `-MaxSessionsPerUser` <Nullable`1> · pipeline by property name — Limits the number of sessions that use the session configuration and run with the credentials of a given user to the specified value.
- `-MaxMemoryPerSessionMB` <Nullable`1> · pipeline by property name — Limits the memory used by each session to the specified value. Enter a value in megabytes.
- `-MaxProcessesPerSession` <Nullable`1> · pipeline by property name — Limits the number of processes running in each session to the specified value.
- `-MaxConcurrentUsers` <Nullable`1> · pipeline by property name — Limits the number of users who can run commands at the same time in each session to the specified value.
- `-IdleTimeoutSec` <Nullable`1> · pipeline by property name — Determines how long each session stays open if the remote computer does not receive any communication from the local computer.
- `-OutputBufferingMode` <Nullable`1> · pipeline by property name — Determines how command output is managed in disconnected sessions when the output buffer becomes full.

**Output:** WSManConfigurationOption

**Example**

```powershell
$t = New-PSTransportOption -IdleTimeoutSec 7200; Set-PSSessionConfiguration -Name Microsoft.PowerShell -TransportOption $t
```

### Out-Default (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Sends output to the default formatter and output cmdlet; PowerShell adds it to the end of every pipeline automatically.

**Syntax** (one line per parameter set)

```powershell
Out-Default [-Transcript] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Transcript` switch — When you use this parameter, the output is only sent to the PowerShell transcript.
- `-InputObject` <PSObject> · pipeline by value — Accepts input to the cmdlet.

**Example**

```powershell
Get-Process | Out-Default
```

### Out-Host (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: oh

Writes output to the console, optionally one screen at a time with -Paging.

**Syntax** (one line per parameter set)

```powershell
Out-Host [-Paging] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Paging` switch — Indicates that Out-Host displays one page of output at a time. The page size is determined by the characteristics of the host.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects that are written to the console. Enter a variable that contains the objects, or type a command or expression that gets the objects.

**Output:** None

**Example**

```powershell
Get-Help about_Operators -Full | Out-Host -Paging
```

### Out-Null (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Discards output instead of sending it down the pipeline or showing it.

**Syntax** (one line per parameter set)

```powershell
Out-Null [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies the object to be sent to NULL (removed from pipeline). Enter a variable that contains the objects, or type a command or expression that gets the objects.

**Output:** None

**Example**

```powershell
New-Item -ItemType Directory .\Temp | Out-Null
```

### Receive-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: rcjb

Gets the results produced so far by background jobs; -Keep leaves them available for later.

**Syntax** (one line per parameter set)

```powershell
Receive-Job [-Job] <Job[]> [[-Location] <string[]>] [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Job] <Job[]> [[-ComputerName] <string[]>] [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Job] <Job[]> [[-Session] <PSSession[]>] [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Name] <string[]> [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-InstanceId] <guid[]> [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]

Receive-Job [-Id] <int[]> [-Keep] [-NoRecurse] [-Force] [-Wait] [-AutoRemoveJob] [-WriteEvents] [-WriteJobInResults] [<CommonParameters>]
```

**Arguments**

- `-Job` <Job[]> · **required** · position 0 · pipeline by value and by property name — Specifies the job for which results are being retrieved.
- `-ComputerName` <String[]> · position 1 · pipeline by property name · alias -Cn · default All computers available · wildcards — Specifies an array of names of computers.
- `-Location` <String[]> · position 1 · default All locations — Specifies an array of locations. This cmdlet gets only the results of jobs in the specified locations.
- `-Session` <PSSession[]> · position 1 · pipeline by property name · default All sessions — Specifies an array of sessions. This cmdlet gets the results of jobs that were run in the specified PowerShell session (PSSession).
- `-Keep` switch — Indicates that this cmdlet saves the aggregated stream data in the system, even after you have received them.
- `-NoRecurse` switch — Indicates that this cmdlet gets results only from the specified job. By default, Receive-Job also gets the results of all child jobs of the specified job.
- `-Force` switch — Indicates that this cmdlet continues waiting if jobs are in the Suspended or Disconnected state.
- `-Wait` switch — Indicates that this cmdlet suppresses the command prompt until all job results are received.
- `-AutoRemoveJob` switch — Indicates that this cmdlet deletes the job after it returns the job results.
- `-WriteEvents` switch — Indicates that this cmdlet reports changes in the job state while it waits for the job to finish.
- `-WriteJobInResults` switch — Indicates that this cmdlet returns the job object followed by the results.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies an array of friendly names. This cmdlet gets the results of jobs that have the specified names.
- `-InstanceId` <Guid[]> · **required** · position 0 · pipeline by property name · default All instances — Specifies an array of instance IDs. This cmdlet gets the results of jobs with the specified instance IDs.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies an array of IDs. This cmdlet gets the results of jobs with the specified IDs.

**Output:** PSObject

**Example**

```powershell
Receive-Job -Id 5 -Keep
```

### Receive-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: rcsn

Gets the results of commands that ran in a disconnected session and reconnects to it.

**Syntax** (one line per parameter set)

```powershell
Receive-PSSession [-Session] <PSSession> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-Id] <int> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ComputerName] <string> -Name <string> [-ApplicationName <string>] [-ConfigurationName <string>] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ComputerName] <string> -InstanceId <guid> [-ApplicationName <string>] [-ConfigurationName <string>] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-Port <int>] [-UseSSL] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ConnectionUri] <uri> -InstanceId <guid> [-ConfigurationName <string>] [-AllowRedirection] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-ConnectionUri] <uri> -Name <string> [-ConfigurationName <string>] [-AllowRedirection] [-OutTarget <OutTarget>] [-JobName <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-SessionOption <PSSessionOption>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-InstanceId] <guid> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Receive-PSSession [-Name] <string> [-OutTarget <OutTarget>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Session` <PSSession> · **required** · position 0 · pipeline by value and by property name — Specifies the disconnected session. Enter a variable that contains the PSSession or a command that creates or gets the PSSession, such as a Get-PSSession command.
- `-Id` <Int32> · **required** · position 0 · pipeline by value and by property name — Specifies the ID of a disconnected session. The Id parameter works only when the disconnected session was previously connected to the current session.
- `-ComputerName` <String> · **required** · position 0 · pipeline by property name · alias -Cn — Specifies the computer on which the disconnected session is stored. Sessions are stored on the computer that's at the server-side, or receiving end of a connection.
- `-ApplicationName` <String> · pipeline by property name — Specifies an application. This cmdlet connects only to sessions that use the specified application.
- `-ConfigurationName` <String> · pipeline by property name — Specifies the name of a session configuration. This cmdlet connects only to sessions that use the specified session configuration.
- `-ConnectionUri` <Uri> · **required** · position 0 · pipeline by property name · alias -URI, -CU · default http://localhost:5985/WSMAN — Specifies a URI that defines the connection endpoint that is used to reconnect to the disconnected session.
- `-AllowRedirection` switch — Indicates that this cmdlet allows redirection of this connection to an alternate Uniform Resource Identifier (URI).
- `-InstanceId` <Guid> · **required** · position 0 · pipeline by value and by property name — Specifies the instance ID of the disconnected session. The instance ID is a GUID that uniquely identifies a PSSession on a local or remote computer.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the friendly name of the disconnected session.
- `-OutTarget` <OutTarget> — Determines how the session results are returned.
- `-JobName` <String> — Specifies a friendly name for the job that Receive-PSSession returns.
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to connect to the disconnected session.
- `-Authentication` <AuthenticationMechanism> · default Default — Specifies the mechanism that's used to authenticate the user credentials in the command to reconnect to a disconnected session.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to connect to the disconnected session.
- `-Port` <Int32> — Specifies the remote computer's network port that's used to reconnect to the session.
- `-UseSSL` switch — Indicates that this cmdlet uses the Secure Sockets Layer (SSL) protocol to connect to the disconnected session.
- `-SessionOption` <PSSessionOption> — Specifies advanced options for the session. Enter a SessionOption object, such as one that you create by using the New-PSSessionOption cmdlet, or a hash table in which the keys are session option names and the values are session option values.

**Output:** Job; PSObject

**Example**

```powershell
Receive-PSSession -ComputerName Server01 -Name Nightly
```

### Register-ArgumentCompleter (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Registers a script block that supplies tab-completion values for a command's parameter.

**Syntax** (one line per parameter set)

```powershell
Register-ArgumentCompleter -CommandName <string[]> -ScriptBlock <scriptblock> [-Native] [<CommonParameters>]

Register-ArgumentCompleter -ParameterName <string> -ScriptBlock <scriptblock> [-CommandName <string[]>] [<CommonParameters>]
```

**Arguments**

- `-CommandName` <String[]> · **required** — Specifies the name of one or more commands to register the argument completer for.
- `-ParameterName` <String> · **required** — Specifies the name of the parameter the argument completer applies to.
- `-ScriptBlock` <ScriptBlock> · **required** — Specifies the commands to run to perform tab completion. The scriptblock you provide should return the values that complete the input.
- `-Native` switch — Indicates that the argument completer is for a native command where PowerShell can't complete parameter names.

**Output:** None

**Example**

```powershell
Register-ArgumentCompleter -CommandName Set-Color -ParameterName Name -ScriptBlock { 'Red','Green','Blue' }
```

### Register-PSSessionConfiguration (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Creates and registers a new session configuration (remoting endpoint) on this computer.

**Syntax** (one line per parameter set)

```powershell
Register-PSSessionConfiguration [-Name] <string> [-ProcessorArchitecture <string>] [-SessionType <PSSessionType>] [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Register-PSSessionConfiguration [-Name] <string> [-AssemblyName] <string> [-ConfigurationTypeName] <string> [-ProcessorArchitecture <string>] [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Register-PSSessionConfiguration [-Name] <string> -Path <string> [-ProcessorArchitecture <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-TransportOption <PSTransportOption>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ProcessorArchitecture` <String> · alias -PA · values: x86, amd64 — Determines whether a 32-bit or 64-bit version of the PowerShell process is started in sessions that use this session configuration.
- `-SessionType` <PSSessionType> — Specifies the type of session that is created by using the session configuration.
- `-Name` <String> · **required** · position 0 · pipeline by property name — Specifies a name for the session configuration. This parameter is required.
- `-AssemblyName` <String> · **required** · position 1 — Specifies the name of an assembly file (\*.dll) in which the configuration type is defined.
- `-ApplicationBase` <String> — Specifies the path of the assembly file (*.dll) that's specified in the value of the AssemblyName parameter.
- `-ConfigurationTypeName` <String> · **required** · position 2 — Specifies the fully qualified name of the Microsoft .NET Framework type that's used for this configuration.
- `-RunAsCredential` <PSCredential> — Specifies credentials for commands in the session. By default, commands run with the permissions of the current user.
- `-ThreadApartmentState` <ApartmentState> — Specifies the apartment state of the threading module to be used.
- `-ThreadOptions` <PSThreadOptions> — Specifies how threads are created and used when a command runs in the session.
- `-AccessMode` <PSSessionConfigurationAccessMode> — Enables and disables the session configuration and determines whether it can be used for remote or local sessions on the computer.
- `-UseSharedProcess` switch — Use only one process to host all sessions that are started by the same user and use the same session configuration.
- `-StartupScript` <String> — Specifies the fully qualified path of a PowerShell script. The specified script runs in the new session that uses the session configuration.
- `-MaximumReceivedDataSizePerCommandMB` <Nullable`1> — Specifies a limit for the amount of data that can be sent to this computer in any single remote command.
- `-MaximumReceivedObjectSizeMB` <Nullable`1> · default 10 — Specifies a limit for the amount of data that can be sent to this computer in any single object.
- `-SecurityDescriptorSddl` <String> — Specifies a Security Descriptor Definition Language (SDDL) string for the configuration.
- `-ShowSecurityDescriptorUI` switch — Indicates that this cmdlet displays a property sheet that helps you create the SDDL for the session configuration.
- `-Force` switch — Suppresses all user prompts and restarts the WinRM service without prompting.
- `-NoServiceRestart` switch — The intent of this parameter is to prevent the restart the WinRM service.
- `-PSVersion` <Version> · alias -PowerShellVersion — Specifies the version of PowerShell in sessions that use this session configuration.
- `-SessionTypeOption` <PSSessionTypeOption> — Specifies type-specific options for the session configuration. Enter a session type options object, such as the PSWorkflowExecutionOption object that the New-PSWorkflowExecutionOption cmdlet returns.
- `-TransportOption` <PSTransportOption> — Specifies the transport option.
- `-ModulesToImport` <Object[]> — Specifies the modules that are automatically imported into sessions that use the session configuration.
- `-Path` <String> · **required** — Specifies the path and filename of a session configuration file (.pssc), such as one created by New-PSSessionConfigurationFile.

**Output:** WSManConfigContainerElement

**Example**

```powershell
Register-PSSessionConfiguration -Name HelpDesk -Path .\JEA.pssc
```

### Remove-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: rjb

Deletes background jobs and their stored results from the session.

**Syntax** (one line per parameter set)

```powershell
Remove-Job [-Id] <int[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Job] <Job[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-InstanceId] <guid[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Name] <string[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Filter] <hashtable> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-State] <JobState> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Job [-Command <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Job` <Job[]> · **required** · position 0 · pipeline by value and by property name — Specifies the jobs to be deleted. Enter a variable that contains the jobs or a command that gets the jobs.
- `-Force` switch · alias -F — Deletes a job even if the job's state is Running. If the Force parameter isn't specified, Remove-Job doesn't delete running jobs.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Only deletes jobs with the specified friendly name. Wildcards are permitted.
- `-InstanceId` <Guid[]> · **required** · position 0 · pipeline by property name — Deletes jobs with the specified InstanceId. You can enter a comma-separated array.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Deletes background jobs with the specified Id. You can enter a comma-separated array.
- `-State` <JobState> · **required** · position 0 · pipeline by property name — Only deletes jobs with the specified state. To delete jobs with a state of Running, use the Force parameter.
- `-Command` <String[]> · pipeline by property name — Deletes jobs that include the specified words in the command. You can enter a comma-separated array.
- `-Filter` <Hashtable> · **required** · position 0 · pipeline by property name — Deletes jobs that satisfy all the conditions established in the associated hash table.

**Output:** None

**Example**

```powershell
Get-Job -State Completed | Remove-Job
```

### Remove-Module (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: rmo

Unloads a module from the session (the module files are not deleted).

**Syntax** (one line per parameter set)

```powershell
Remove-Module [-Name] <string[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Module [-FullyQualifiedName] <ModuleSpecification[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Module [-ModuleInfo] <psmoduleinfo[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by value · wildcards — Specifies the names of modules to remove. Wildcard characters are permitted.
- `-FullyQualifiedName` <ModuleSpecification[]> · **required** · position 0 · pipeline by value — The value can be a module name, a full module specification, or a path to a module file.
- `-ModuleInfo` <PSModuleInfo[]> · **required** · position 0 · pipeline by value — Specifies the module objects to remove. Enter a variable that contains a PSModuleInfo object or a command that gets a module object, such as a Get-Module command.
- `-Force` switch — Indicates that this cmdlet removes read-only modules. By default, Remove-Module removes only read-write modules.

**Output:** None

**Example**

```powershell
Remove-Module MyTools
```

### Remove-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: rsn

Closes PSSessions and frees their resources on the remote computer.

**Syntax** (one line per parameter set)

```powershell
Remove-PSSession [-Id] <int[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession [-Session] <PSSession[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -ContainerId <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -VMId <guid[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -VMName <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -InstanceId <guid[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession -Name <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSSession [-ComputerName] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Session` <PSSession[]> · **required** · position 0 · pipeline by value and by property name — Specifies the session objects of the PSSessions to close. Enter a variable that contains the PSSessions or a command that creates or gets the PSSessions, such as a New-PSSession or Get-PSSession command.
- `-ContainerId` <String[]> · **required** · pipeline by property name — Specifies an array of IDs of containers. This cmdlet removes sessions for each of the specified containers.
- `-VMId` <Guid[]> · **required** · pipeline by property name · alias -VMGuid — Specifies an array of ID of virtual machines. This cmdlet starts an interactive session with each of the specified virtual machines.
- `-VMName` <String[]> · **required** · pipeline by property name — Specifies an array of names of virtual machines. This cmdlet starts an interactive session with each of the specified virtual machines.
- `-InstanceId` <Guid[]> · **required** · pipeline by property name — Specifies an array of instance IDs. This cmdlet closes the PSSessions that have the specified instance IDs.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies an array of IDs of sessions. This cmdlet closes the PSSessions with the specified IDs.
- `-Name` <String[]> · **required** · pipeline by property name · wildcards — Specifies an array of friendly names of sessions. This cmdlet closes the PSSessions that have the specified friendly names.
- `-ComputerName` <String[]> · **required** · position 0 · pipeline by property name · alias -Cn · wildcards — Specifies an array of names of computers. This cmdlet closes the PSSessions that are connected to the specified computers.

**Output:** None

**Example**

```powershell
Get-PSSession | Remove-PSSession
```

### Remove-PSSnapin (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: rsnp

Removes a snap-in from the current session (it stays installed).

**Syntax** (one line per parameter set)

```powershell
Remove-PSSnapin [-Name] <string[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — Specifies the names of Windows PowerShell snap-ins to remove from the current session.
- `-PassThru` switch — Returns an object that represents the snap-in. By default, this cmdlet does not generate any output.

**Output:** None, System.Management.Automation.PSSnapInInfo

**Example**

```powershell
Remove-PSSnapin Microsoft.Exchange.Management.PowerShell.SnapIn
```

### Resume-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: rujb

Restarts workflow jobs that were suspended.

**Syntax** (one line per parameter set)

```powershell
Resume-Job [-Id] <int[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-Job] <Job[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-Name] <string[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-InstanceId] <guid[]> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-State] <JobState> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Job [-Filter] <hashtable> [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Job` <Job[]> · **required** · position 0 · pipeline by value and by property name — Specifies the jobs to be resumed. Enter a variable that contains the jobs or a command that gets the jobs.
- `-Wait` switch — Indicates that this cmdlet suppresses the command prompt until all job results are restarted.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name — Specifies an array of friendly names of jobs that this cmdlet resumes.
- `-InstanceId` <Guid[]> · **required** · position 0 · pipeline by property name — Specifies an array of instance IDs of jobs that this cmdlet resumes. The default is all jobs.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies an array of IDs for jobs that this cmdlet resumes.
- `-State` <JobState> · **required** · position 0 · pipeline by property name — Specifies the state of jobs to resume.
- `-Filter` <Hashtable> · **required** · position 0 · pipeline by property name — Specifies a hash table of conditions. This cmdlet resumes jobs that satisfy all of the conditions in the hash table.

**Output:** None, System.Management.Automation.Job

**Example**

```powershell
Resume-Job -Name Backup
```

### Save-Help (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Downloads the newest help files for modules to a folder, for installing on computers without internet.

**Syntax** (one line per parameter set)

```powershell
Save-Help [-DestinationPath] <string[]> [[-Module] <psmoduleinfo[]>] [[-UICulture] <cultureinfo[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [<CommonParameters>]

Save-Help [[-Module] <psmoduleinfo[]>] [[-UICulture] <cultureinfo[]>] -LiteralPath <string[]> [-FullyQualifiedModule <ModuleSpecification[]>] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [<CommonParameters>]
```

**Arguments**

- `-DestinationPath` <String[]> · **required** · position 0 — Specifies the path of the folder in which the help files are saved. Don't specify a filename or filename extension.
- `-LiteralPath` <String[]> · **required** · alias -PSPath — Specifies a path of the destination folder. Unlike the value of the DestinationPath parameter, the value of the LiteralPath parameter is used exactly as it's typed.
- `-Module` <PSModuleInfo[]> · position 1 · pipeline by value and by property name · alias -Name · wildcards — Specifies modules for which this cmdlet downloads help. Enter one or more module names or name patterns in a comma-separated list or in a file that has one module name on each line.
- `-FullyQualifiedModule` <ModuleSpecification[]> · pipeline by property name — The value can be a module name, a full module specification, or a path to a module file.
- `-UICulture` <CultureInfo[]> · position 2 · default Current UI culture — Specifies UI culture values for which this cmdlet gets updated help files.
- `-Credential` <PSCredential> — Specifies a user credential. This cmdlet runs the command by using credentials of a user who has permission to access the file system location specified by the DestinationPath parameter.
- `-UseDefaultCredentials` switch — Indicates that this cmdlet runs the command, including the web download, with the credentials of the current user.
- `-Force` switch — Indicates that this cmdlet doesn't follow the once-per-day limitation, skips version checking, and downloads files that exceed the 1 GB limit.

**Output:** None

**Example**

```powershell
Save-Help -DestinationPath \\Server\Help -Module * -Force
```

### Set-PSDebug (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Turns script tracing (-Trace 1/2), stepping (-Step) and strict variable checks on or off.

**Syntax** (one line per parameter set)

```powershell
Set-PSDebug [-Trace <int>] [-Step] [-Strict] [<CommonParameters>]

Set-PSDebug [-Off] [<CommonParameters>]
```

**Arguments**

- `-Trace` <Int32> — Specifies the trace level for each line in a script. Each line is traced as it's run.
- `-Step` switch — Turns on script stepping. Before each line runs, PowerShell prompts you to stop, continue, or enter a new interpreter level to inspect the state of the script.
- `-Strict` switch — Specifies that variables must be assigned a value before being referenced in a script.
- `-Off` switch — Turns off all script debugging features.

**Output:** None

**Example**

```powershell
Set-PSDebug -Trace 1
```

### Set-PSSessionConfiguration (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Changes the properties of a registered session configuration (permissions, startup script, quotas).

**Syntax** (one line per parameter set)

```powershell
Set-PSSessionConfiguration [-Name] <string> [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-PSSessionConfiguration [-Name] <string> [-AssemblyName] <string> [-ConfigurationTypeName] <string> [-ApplicationBase <string>] [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-PSVersion <version>] [-SessionTypeOption <PSSessionTypeOption>] [-TransportOption <PSTransportOption>] [-ModulesToImport <Object[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-PSSessionConfiguration [-Name] <string> -Path <string> [-RunAsCredential <pscredential>] [-ThreadApartmentState <ApartmentState>] [-ThreadOptions <PSThreadOptions>] [-AccessMode <PSSessionConfigurationAccessMode>] [-UseSharedProcess] [-StartupScript <string>] [-MaximumReceivedDataSizePerCommandMB <double>] [-MaximumReceivedObjectSizeMB <double>] [-SecurityDescriptorSddl <string>] [-ShowSecurityDescriptorUI] [-Force] [-NoServiceRestart] [-TransportOption <PSTransportOption>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by property name — Specifies the name of the session configuration that you want to change.
- `-AssemblyName` <String> · **required** · position 1 — Specifies the assembly name. This cmdlet creates a session configuration based on a class that is defined in an assembly.
- `-ApplicationBase` <String> — Specifies the path of the assembly file (*.dll) that is specified in the value of the AssemblyName parameter.
- `-ConfigurationTypeName` <String> · **required** · position 2 — Specifies the type of the session configuration that is defined in the assembly in the AssemblyName parameter.
- `-RunAsCredential` <PSCredential> — Specifies credentials for commands in the session. By default, commands run with the permissions of the current user.
- `-ThreadApartmentState` <ApartmentState> — Specifies the apartment state of the threading module to be used.
- `-ThreadOptions` <PSThreadOptions> — Specifies the thread options setting in the configuration. This setting defines how threads are created and used when a command is executed in the session.
- `-AccessMode` <PSSessionConfigurationAccessMode> — Enables and disables the session configuration and determines whether it can be used for remote or local sessions on the computer.
- `-UseSharedProcess` switch — Use only one process to host all sessions that are started by the same user and use the same session configuration.
- `-StartupScript` <String> — Specifies the startup script for the configuration. Enter the fully qualified path of a PowerShell script.
- `-MaximumReceivedDataSizePerCommandMB` <Nullable`1> — Specifies the limit on the amount of data that can be sent to this computer in any single remote command.
- `-MaximumReceivedObjectSizeMB` <Nullable`1> · default 10 — Specifies the limits on the amount of data that can be sent to this computer in any single object.
- `-SecurityDescriptorSddl` <String> — Specifies a different Security Descriptor Definition Language (SDDL) string for the configuration.
- `-ShowSecurityDescriptorUI` switch — When this parameter is used the cmdlet opens a property sheet that helps you create a new SDDL for the session configuration.
- `-Force` switch — Suppresses all user prompts, and restarts the WinRM service without prompting.
- `-NoServiceRestart` switch — Does not restart the WinRM service, and suppresses the prompt to restart the service.
- `-PSVersion` <Version> · alias -PowerShellVersion — Specifies the version of PowerShell in sessions that use this session configuration.
- `-SessionTypeOption` <PSSessionTypeOption> — Specifies type-specific options for the session configuration. Enter a session type options object, such as the PSWorkflowExecutionOption object that the New-PSWorkflowExecutionOption cmdlet returns.
- `-TransportOption` <PSTransportOption> — Specifies the transport options for the session configuration. Enter a transport options object, such as the WSManConfigurationOption object that the New-PSTransportOption cmdlet returns.
- `-ModulesToImport` <Object[]> — Specifies the modules and snap-ins that are automatically imported into sessions that use the session configuration.
- `-Path` <String> · **required** — Specifies the path of a session configuration file (.pssc), such as one created by the New-PSSessionConfigurationFile cmdlet.

**Output:** WSManConfigLeafElement

**Example**

```powershell
Set-PSSessionConfiguration -Name Microsoft.PowerShell -ShowSecurityDescriptorUI
```

### Set-StrictMode (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Makes the current scope stricter: errors for uninitialized variables, missing properties and bad function calls.

**Syntax** (one line per parameter set)

```powershell
Set-StrictMode -Version <version> [<CommonParameters>]

Set-StrictMode -Off [<CommonParameters>]
```

**Arguments**

- `-Off` switch · **required** — Indicates that this cmdlet turns strict mode off for the current scope and all child scopes.
- `-Version` <Version> · **required** · alias -v — Specifies the conditions that cause an error in strict mode. This parameter accepts any valid PowerShell version number.

**Output:** None

**Example**

```powershell
Set-StrictMode -Version Latest
```

### Start-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: sajb

Starts a background job that runs a script block or script without blocking the console.

**Syntax** (one line per parameter set)

```powershell
Start-Job [-ScriptBlock] <scriptblock> [[-InitializationScript] <scriptblock>] [-Name <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-RunAs32] [-PSVersion <version>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Start-Job [-DefinitionName] <string> [[-DefinitionPath] <string>] [[-Type] <string>] [<CommonParameters>]

Start-Job [-FilePath] <string> [[-InitializationScript] <scriptblock>] [-Name <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-RunAs32] [-PSVersion <version>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]

Start-Job [[-InitializationScript] <scriptblock>] -LiteralPath <string> [-Name <string>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-RunAs32] [-PSVersion <version>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [<CommonParameters>]
```

**Arguments**

- `-DefinitionName` <String> · **required** · position 0 — Specifies the definition name of the job that this cmdlet starts. Use this parameter to start custom job types that have a definition name, such as scheduled jobs.
- `-DefinitionPath` <String> · position 1 — Specifies path of the definition for the job that this cmdlet starts. Enter the definition path.
- `-Type` <String> · position 2 — Specifies the custom type for jobs started by Start-Job. Enter a custom job type name, such as PSScheduledJob for scheduled jobs or PSWorkflowJob for workflows jobs.
- `-Name` <String> · pipeline by property name — Specifies a friendly name for the new job. You can use the name to identify the job to other job cmdlets, such as the Stop-Job cmdlet.
- `-ScriptBlock` <ScriptBlock> · **required** · position 0 · alias -Command — Specifies the commands to run in the background job. To create a scriptblock, enclose the commands in curly braces ({}).
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to perform this action. If the Credential parameter isn't specified, the command uses the current user's credentials.
- `-FilePath` <String> · **required** · position 0 — Specifies a local script that Start-Job runs as a background job. Enter the path and file name of the script or use the pipeline to send a script path to Start-Job.
- `-LiteralPath` <String> · **required** · alias -PSPath — Specifies a local script that this cmdlet runs as a background job. Enter the path of a script on the local computer.
- `-Authentication` <AuthenticationMechanism> · default Default — Specifies the mechanism that is used to authenticate user credentials.
- `-InitializationScript` <ScriptBlock> · position 1 — Specifies commands that run before the job starts. To create a scriptblock, enclose the commands in curly braces ({}).
- `-RunAs32` switch — Indicates that Start-Job runs the job in a 32-bit process. RunAs32 forces the job to run in a 32-bit process, even on a 64-bit operating system.
- `-PSVersion` <Version> — Specifies a version. Start-Job runs the job with the version of PowerShell.
- `-InputObject` <PSObject> · pipeline by value — Specifies input to the command. Enter a variable that contains the objects, or type a command or expression that generates the objects.
- `-ArgumentList` <Object[]> · alias -Args — Specifies an array of arguments, or parameter values, for the script that is specified by the FilePath parameter or a command specified with the ScriptBlock parameter.

**Output:** PSRemotingJob

**Example**

```powershell
Start-Job -Name Hash -ScriptBlock { Get-FileHash C:\Data\*.zip }
```

### Stop-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: spjb

Stops running background jobs.

**Syntax** (one line per parameter set)

```powershell
Stop-Job [-Id] <int[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-Job] <Job[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-Name] <string[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-InstanceId] <guid[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-State] <JobState> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Job [-Filter] <hashtable> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Job` <Job[]> · **required** · position 0 · pipeline by value and by property name · default All jobs — Specifies the jobs that this cmdlet stops. Enter a variable that contains the jobs or a command that gets the jobs.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name · default All jobs · wildcards — Specifies friendly names of jobs that this cmdlet stops. Enter the job names in a comma-separated list or use wildcard characters (*) to enter a job name pattern.
- `-InstanceId` <Guid[]> · **required** · position 0 · pipeline by property name · default All jobs — Specifies the instance IDs of jobs that this cmdlet stops. The default is all jobs.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name · default All jobs — Specifies the IDs of jobs that this cmdlet stops. The default is all jobs in the current session.
- `-State` <JobState> · **required** · position 0 · pipeline by property name · default All jobs — Specifies a job state. This cmdlet stops only jobs in the specified state.
- `-Filter` <Hashtable> · **required** · position 0 · pipeline by property name — Specifies a hash table of conditions. This cmdlet stops jobs that satisfy every condition.

**Output:** None; PSRemotingJob

**Example**

```powershell
Stop-Job -Name Hash
```

### Suspend-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: sujb

Pauses workflow jobs at the next checkpoint so they can be resumed later.

**Syntax** (one line per parameter set)

```powershell
Suspend-Job [-Id] <int[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-Job] <Job[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-Name] <string[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-Filter] <hashtable> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-State] <JobState> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Job [-InstanceId] <guid[]> [-Force] [-Wait] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Job` <Job[]> · **required** · position 0 · pipeline by value and by property name — Specifies the workflow jobs that this cmdlet stops. Enter a variable that contains the workflow jobs or a command that gets the workflow jobs.
- `-Force` switch · alias -F — Suspends the workflow job immediately. This action could cause a loss of state and data.
- `-Wait` switch — Indicates that this cmdlet suppresses the command prompt until the workflow job is in the suspended state.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name — Specifies friendly names of jobs that this cmdlet suspends. Enter one or more workflow job names.
- `-InstanceId` <Guid[]> · **required** · position 0 · pipeline by property name — Specifies the instance IDs of jobs that this cmdlet suspends. The default is all jobs.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies the IDs of jobs that this cmdlet suspends.
- `-State` <JobState> · **required** · position 0 · pipeline by property name — Specifies a job state. This cmdlet stops only jobs in the specified state.
- `-Filter` <Hashtable> · **required** · position 0 · pipeline by property name — Specifies a hash table of conditions. This cmdlet suspends jobs that satisfy all of the conditions.

**Output:** Job

**Example**

```powershell
Suspend-Job -Name Backup
```

### TabExpansion2 (PowerShell 5.1 function)

Module Microsoft.PowerShell.Core

Built-in function that computes tab completions for an input string; editors and hosts call it.

**Syntax** (one line per parameter set)

```powershell
TabExpansion2 [-inputScript] <string> [-cursorColumn] <int> [[-options] <hashtable>] [<CommonParameters>]

TabExpansion2 [-ast] <Ast> [-tokens] <Token[]> [-positionOfCursor] <IScriptPosition> [[-options] <hashtable>] [<CommonParameters>]
```

**Arguments**

- `-inputScript` <String> · **required** · position 0 — A string that represents the script that you want to expand using tab completion.
- `-cursorColumn` <Int32> · **required** · position 1 — The column number of the cursor in the input script string. The cursor position is used to determine the token that gets expanded by tab completion.
- `-ast` <Ast> · **required** · position 0 — An abstract syntax tree (AST) object that represents the script that you want to expand using tab completion.
- `-tokens` <Token[]> · **required** · position 1 — An array of tokens parsed from the input script. The tokens are used to determine the token that gets expanded by tab completion.
- `-positionOfCursor` <IScriptPosition> · **required** · position 2 — The column number of the cursor in the input AST. The cursor position is used to determine the token that gets expanded by tab completion.
- `-options` <Hashtable> · position 3 — A hashtable of option values to pass to the CompleteInput() API.

**Output:** CommandCompletion

**Example**

```powershell
TabExpansion2 -inputScript 'Get-Ch' -cursorColumn 6 | Select-Object -ExpandProperty CompletionMatches
```

### Test-ModuleManifest (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Checks that a module manifest (.psd1) is valid and the files it lists exist.

**Syntax** (one line per parameter set)

```powershell
Test-ModuleManifest [-Path] <string> [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies a path and file name for the manifest file. Enter an optional path and name of the module manifest file that has the .psd1 file name extension.

**Output:** PSModuleInfo

**Example**

```powershell
Test-ModuleManifest .\MyTools.psd1
```

### Test-PSSessionConfigurationFile (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Validates the keys and values of a session configuration file (.pssc).

**Syntax** (one line per parameter set)

```powershell
Test-PSSessionConfigurationFile [-Path] <string> [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path and filename of a session configuration file (.pssc).

**Output:** Boolean

**Example**

```powershell
Test-PSSessionConfigurationFile -Path .\JEA.pssc
```

### Unregister-PSSessionConfiguration (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Deletes registered session configurations from the computer.

**Syntax** (one line per parameter set)

```powershell
Unregister-PSSessionConfiguration [-Name] <string> [-Force] [-NoServiceRestart] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by property name · wildcards — Specifies the names of the session configurations to delete. Enter one session configuration name or a configuration name pattern.
- `-Force` switch — Indicates that the cmdlet does not prompt you for confirmation, and restarts the WinRM service without prompting.
- `-NoServiceRestart` switch — Indicates that this cmdlet does not restart the WinRM service, and suppresses the prompt to restart the service.

**Output:** None

**Example**

```powershell
Unregister-PSSessionConfiguration -Name HelpDesk -Force
```

### Update-Help (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core

Downloads and installs the newest help files for modules (needs internet or a -SourcePath share).

**Syntax** (one line per parameter set)

```powershell
Update-Help [[-Module] <string[]>] [[-SourcePath] <string[]>] [[-UICulture] <cultureinfo[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Recurse] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-Help [[-Module] <string[]>] [[-UICulture] <cultureinfo[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-LiteralPath <string[]>] [-Recurse] [-Credential <pscredential>] [-UseDefaultCredentials] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Module` <String[]> · position 0 · pipeline by property name · alias -Name · wildcards — Updates help for the specified modules. Enter one or more module names or name patterns in a comma-separated list, or specify a file that lists one module name on each line.
- `-FullyQualifiedModule` <ModuleSpecification[]> · pipeline by property name — The value can be a module name, a full module specification, or a path to a module file.
- `-SourcePath` <String[]> · position 1 — Specifies a file system folder where Update-Help gets updated help files, instead of downloading them from the internet.
- `-LiteralPath` <String[]> · pipeline by property name · alias -PSPath — Specifies the folder for updated help files instead of downloading them from the internet.
- `-Recurse` switch — Performs a recursive search for help files in the specified directory.
- `-UICulture` <CultureInfo[]> · position 2 — Specifies UI culture values that Update-Help uses to get updated help files.
- `-Credential` <PSCredential> · default Current user — Specifies credentials of a user who has permission to access the file system location specified by SourcePath.
- `-UseDefaultCredentials` switch — Indicates that Update-Help runs the command, including the internet download, using the credentials of the current user.
- `-Force` switch — Indicates that this cmdlet doesn't follow the once-per-day limitation, skips version checking, and downloads files that exceed the 1 GB limit.

**Output:** None

**Example**

```powershell
Update-Help -Module Microsoft.PowerShell* -UICulture en-US
```

### Wait-Job (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: wjb

Waits until jobs finish (or a timeout passes) before continuing.

**Syntax** (one line per parameter set)

```powershell
Wait-Job [-Id] <int[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-Job] <Job[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-Name] <string[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-InstanceId] <guid[]> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-State] <JobState> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]

Wait-Job [-Filter] <hashtable> [-Any] [-Timeout <int>] [-Force] [<CommonParameters>]
```

**Arguments**

- `-Job` <Job[]> · **required** · position 0 · pipeline by value and by property name — Specifies the jobs for which this cmdlet waits. Enter a variable that contains the job objects or a command that gets the job objects.
- `-Any` switch — Indicates that this cmdlet returns the job object and continues execution when any job finishes.
- `-Timeout` <Int32> · alias -TimeoutSec — Specifies the maximum wait time for each job, in seconds. The default value, -1, indicates that the cmdlet waits until the job finishes.
- `-Force` switch — Indicates that this cmdlet continues to wait for jobs in the Suspended or Disconnected state.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name — Specifies friendly names of jobs for which this cmdlet waits.
- `-InstanceId` <Guid[]> · **required** · position 0 · pipeline by property name — Specifies an array of instance IDs of jobs for which this cmdlet waits.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies an array of IDs of jobs for which this cmdlet waits.
- `-State` <JobState> · **required** · position 0 · pipeline by property name — Specifies a job state. This cmdlet waits only for jobs in the specified state.
- `-Filter` <Hashtable> · **required** · position 0 · pipeline by property name — Specifies a hash table of conditions. This cmdlet waits for jobs that satisfy all of the conditions in the hash table.

**Output:** PSRemotingJob

**Example**

```powershell
Start-Job { Start-Sleep 5 } | Wait-Job -Timeout 30
```

### Where-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Core · aliases: ?, where

Filters pipeline objects, keeping those whose property values match a condition. Alias: where, ?.

**Syntax** (one line per parameter set)

```powershell
Where-Object [-Property] <string> [[-Value] <Object>] [-InputObject <psobject>] [-EQ] [<CommonParameters>]

Where-Object [-FilterScript] <scriptblock> [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotLike [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CEQ [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -GT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CGT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -LT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CLT [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -GE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CGE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -LE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CLE [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Like [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CLike [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotLike [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Match [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CMatch [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotMatch [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotMatch [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Contains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CContains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotContains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotContains [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -In [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CIn [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -NotIn [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -CNotIn [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -Is [-InputObject <psobject>] [<CommonParameters>]

Where-Object [-Property] <string> [[-Value] <Object>] -IsNot [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to filter. You can also pipe the objects to Where-Object.
- `-FilterScript` <ScriptBlock> · **required** · position 0 — Specifies the scriptblock that's used to filter the objects. Enclose the scriptblock in braces ({}).
- `-Property` <String> · **required** · position 0 — Specifies the name of a property of the input object. The property must be an instance property, not a static property.
- `-Value` <Object> · position 1 · wildcards — Specifies a property value. The parameter name, Value, is optional.
- `-EQ` switch · alias -IEQ — Indicates that this cmdlet gets objects if the property value is the same as the specified value.
- `-CEQ` switch · **required** — Indicates that this cmdlet gets objects if the property value is the same as the specified value.
- `-NE` switch · **required** · alias -INE — Indicates that this cmdlet gets objects if the property value is different than the specified value.
- `-CNE` switch · **required** — Indicates that this cmdlet gets objects if the property value is different than the specified value.
- `-GT` switch · **required** · alias -IGT — Indicates that this cmdlet gets objects if the property value is greater than the specified value.
- `-CGT` switch · **required** — Indicates that this cmdlet gets objects if the property value is greater than the specified value.
- `-LT` switch · **required** · alias -ILT — Indicates that this cmdlet gets objects if the property value is less than the specified value.
- `-CLT` switch · **required** — Indicates that this cmdlet gets objects if the property value is less-than the specified value.
- `-GE` switch · **required** · alias -IGE — Indicates that this cmdlet gets objects if the property value is greater than or equal to the specified value.
- `-CGE` switch · **required** — Indicates that this cmdlet gets objects if the property value is greater than or equal to the specified value.
- `-LE` switch · **required** · alias -ILE — Indicates that this cmdlet gets objects if the property value is less than or equal to the specified value.
- `-CLE` switch · **required** — Indicates that this cmdlet gets objects if the property value is less-than or equal to the specified value.
- `-Like` switch · **required** · alias -ILike — Indicates that this cmdlet gets objects if the property value matches a value that includes wildcard characters (*).
- `-CLike` switch · **required** — Indicates that this cmdlet gets objects if the property value matches a value that includes wildcard characters (*).
- `-NotLike` switch · **required** · alias -INotLike — Indicates that this cmdlet gets objects if the property value doesn't match a value that includes wildcard characters (*).
- `-CNotLike` switch · **required** — Indicates that this cmdlet gets objects if the property value doesn't match a value that includes wildcard characters.
- `-Match` switch · **required** · alias -IMatch — Indicates that this cmdlet gets objects if the property value matches the specified regular expression.
- `-CMatch` switch · **required** — Indicates that this cmdlet gets objects if the property value matches the specified regular expression.
- `-NotMatch` switch · **required** · alias -INotMatch — Indicates that this cmdlet gets objects when the property value doesn't match the specified regular expression.
- `-CNotMatch` switch · **required** — Indicates that this cmdlet gets objects if the property value doesn't match the specified regular expression.
- `-Contains` switch · **required** · alias -IContains — Indicates that this cmdlet gets objects if any item in the property value of the object is an exact match for the specified value.
- `-CContains` switch · **required** — Indicates that this cmdlet gets objects from a collection if the property value of the object is an exact match for the specified value.
- `-NotContains` switch · **required** · alias -INotContains — Indicates that this cmdlet gets objects if none of the items in the property value is an exact match for the specified value.
- `-CNotContains` switch · **required** — Indicates that this cmdlet gets objects if the property value of the object isn't an exact match for the specified value.
- `-In` switch · **required** · alias -IIn — Indicates that this cmdlet gets objects if the property value matches any of the specified values.
- `-CIn` switch · **required** — Indicates that this cmdlet gets objects if the property value includes the specified value.
- `-NotIn` switch · **required** · alias -INotIn — Indicates that this cmdlet gets objects if the property value isn't an exact match for any of the specified values.
- `-CNotIn` switch · **required** — Indicates that this cmdlet gets objects if the property value isn't an exact match for the specified value.
- `-Is` switch · **required** — Indicates that this cmdlet gets objects if the property value is an instance of the specified .NET type.
- `-IsNot` switch · **required** — Indicates that this cmdlet gets objects if the property value isn't an instance of the specified .NET type.

**Output:** Object

**Example**

```powershell
Get-Service | Where-Object Status -eq 'Running'
```

## Commands: Microsoft.PowerShell.Management

### Add-Computer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Joins the local (or a remote) computer to a domain or workgroup; a restart is needed to finish.

**Syntax** (one line per parameter set)

```powershell
Add-Computer [-DomainName] <string> -Credential <pscredential> [-ComputerName <string[]>] [-LocalCredential <pscredential>] [-UnjoinDomainCredential <pscredential>] [-OUPath <string>] [-Server <string>] [-Unsecure] [-Options <JoinOptions>] [-Restart] [-PassThru] [-NewName <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Add-Computer [-WorkgroupName] <string> [-ComputerName <string[]>] [-LocalCredential <pscredential>] [-Credential <pscredential>] [-Restart] [-PassThru] [-NewName <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · pipeline by value and by property name · default Local computer — Specifies the computers to add to a domain or workgroup. The default is the local computer.
- `-LocalCredential` <PSCredential> · default Current user — Specifies a user account that has permission to connect to the computers that are specified by the ComputerName parameter.
- `-UnjoinDomainCredential` <PSCredential> — Specifies a user account that has permission to remove the computers from their current domains.
- `-Credential` <PSCredential> · **required** · alias -DomainCredential — Specifies a user account that has permission to join the computers to a new domain.
- `-DomainName` <String> · **required** · position 0 · alias -DN, -Domain — Specifies the domain to which the computers are added. This parameter is required when adding the computers to a domain.
- `-OUPath` <String> · alias -OU — Specifies an organizational unit (OU) for the domain account. Enter the full distinguished name of the OU in quotation marks.
- `-Server` <String> · alias -DC — Specifies the name of a domain controller that adds the computer to the domain.
- `-Unsecure` switch — Performs an unsecure join to the specified domain.
- `-Options` <JoinOptions> — Specifies advanced options for the Add-Computer join operation. Enter one or more values in a comma-separated string.
- `-WorkgroupName` <String> · **required** · position 0 · alias -WGN — Specifies the name of a workgroup to which the computers are added. The default value is "WORKGROUP".
- `-Restart` switch — Restarts the computers that were added to the domain or workgroup. A restart is often required to make the change effective.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-NewName` <String> · pipeline by property name — Specifies a new name for the computer in the new domain. This parameter is valid only when one computer is being added or moved.
- `-Force` switch — Suppresses the user confirmation prompt. Without this parameter, Add-Computer requires you to confirm the addition of each computer.

**Output:** ComputerChangeInfo

**Example**

```powershell
Add-Computer -DomainName contoso.com -Credential CONTOSO\Admin -Restart
```

### Add-Content (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: ac

Appends text or other content to a file (creates the file if it does not exist). Alias: ac.

**Syntax** (one line per parameter set)

```powershell
Add-Content [-Path] <string[]> [-Value] <Object[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]

Add-Content [-Value] <Object[]> -LiteralPath <string[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]
```

**Arguments**

- `-Value` <Object[]> · **required** · position 1 · pipeline by value and by property name — Specifies the content to be added. Type a quoted string, such as This data is for internal use only, or specify an object that contains content, such as the DateTime object that Get-Date generates.
- `-PassThru` switch — Returns an object representing the added content. By default, this cmdlet doesn't generate any output.
- `-Path` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the path to the items that receive the additional content. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it's typed.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Force` switch — Overrides the read-only attribute, allowing you to add content to a read-only file.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-NoNewline` switch — Indicates that this cmdlet doesn't add a new line or carriage return to the content.
- `-Encoding` <FileSystemCmdletProviderEncoding> · default Default — Specifies the type of encoding for the target file. The default value is Default.
- `-Stream` <String> — Specifies an alternative data stream for content. If the stream doesn't exist, this cmdlet creates it.

**Output:** None; String

**Example**

```powershell
Add-Content -Path .\log.txt -Value "$(Get-Date) started"
```

### Checkpoint-Computer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Creates a System Restore point on the local computer (needs an elevated session).

**Syntax** (one line per parameter set)

```powershell
Checkpoint-Computer [-Description] <string> [[-RestorePointType] <string>] [<CommonParameters>]
```

**Arguments**

- `-Description` <String> · **required** · position 0 — Specifies a descriptive name for the restore point. This parameter is required.
- `-RestorePointType` <String> · position 1 · alias -RPT · values: APPLICATION_INSTALL, APPLICATION_UNINSTALL, DEVICE_DRIVER_INSTALL, MODIFY_SETTINGS, CANCELLED_OPERATION — Specifies the type of restore point. The default is APPLICATION_INSTALL.

**Output:** None

**Example**

```powershell
Checkpoint-Computer -Description 'Before driver update' -RestorePointType MODIFY_SETTINGS
```

### Clear-Content (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: clc

Empties a file (or other item's content) without deleting the item. Alias: clc.

**Syntax** (one line per parameter set)

```powershell
Clear-Content [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string>] [<CommonParameters>]

Clear-Content -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the paths to the items from which content is deleted. Wildcards are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the paths to the items from which content is deleted. Unlike the Path parameter, the value of LiteralPath is used exactly as it is typed.
- `-Filter` <String> · wildcards — Specifies a filter in the provider's format or language. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Specifies, as a string array, content that this cmdlet clears. The value of this parameter qualifies the Path parameter.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, strings that this cmdlet omits from the path to the content.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Stream` <String> — This is a dynamic parameter made available by the FileSystem provider.

**Output:** None

**Example**

```powershell
Clear-Content -Path .\log.txt
```

### Clear-EventLog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Deletes all entries from classic event logs on the local or remote computers.

**Syntax** (one line per parameter set)

```powershell
Clear-EventLog [-LogName] <string[]> [[-ComputerName] <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-LogName` <String[]> · **required** · position 0 · pipeline by property name · alias -LN — Specifies the event logs. Enter the log name (the value of the Log property not the LogDisplayName) of one or more event logs, separated by commas.
- `-ComputerName` <String[]> · position 1 · pipeline by property name · alias -Cn · default Local computer — Specifies a remote computer. The default is the local computer.

**Output:** None

**Example**

```powershell
Clear-EventLog -LogName Application
```

### Clear-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: cli

Clears an item's value without deleting it (for example a variable or registry value). Alias: cli.

**Syntax** (one line per parameter set)

```powershell
Clear-Item [-Path] <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Clear-Item -LiteralPath <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path to the items being cleared. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it is typed.
- `-Force` switch — Indicates that the cmdlet clears items that cannot otherwise be changed, such as read- only aliases.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None

**Example**

```powershell
Clear-Item -Path Env:\TEMPVAR
```

### Clear-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: clp

Clears the value of an item's property, keeping the property itself (often registry values). Alias: clp.

**Syntax** (one line per parameter set)

```powershell
Clear-ItemProperty [-Path] <string[]> [-Name] <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Clear-ItemProperty [-Name] <string> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path to the property being cleared. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it is typed.
- `-Name` <String> · **required** · position 1 · pipeline by property name · wildcards — Specifies the name of the property to be cleared, such as the name of a registry value.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Force` switch — Indicates that this cmdlet deletes properties from items that cannot otherwise be accessed by the user.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None; PSCustomObject

**Example**

```powershell
Clear-ItemProperty -Path HKCU:\Software\MyApp -Name LastRun
```

### Clear-RecycleBin (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Empties the Recycle Bin for one drive or all drives.

**Syntax** (one line per parameter set)

```powershell
Clear-RecycleBin [[-DriveLetter] <string[]>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-DriveLetter` <String[]> · position 0 · pipeline by property name — Specifies the recycle bin to clear for a single drive letter or an array of drive letters.
- `-Force` switch — Specifies that the user isn't prompted for confirmation to clear a recycle bin.

**Output:** None

**Example**

```powershell
Clear-RecycleBin -DriveLetter C -Force
```

### Complete-Transaction (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Commits the active transaction (registry provider transactions started with Start-Transaction).

**Syntax** (one line per parameter set)

```powershell
Complete-Transaction [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Output:** None

**Example**

```powershell
Complete-Transaction
```

### Convert-Path (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: cvpa

Converts a PowerShell path (with drives like HKLM: or ~) into the provider's native path. Alias: cvpa.

**Syntax** (one line per parameter set)

```powershell
Convert-Path [-Path] <string[]> [-UseTransaction] [<CommonParameters>]

Convert-Path -LiteralPath <string[]> [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the PowerShell path to be converted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies, as a string array, the path to be converted. The value of the LiteralPath parameter is used exactly as it's typed.

**Output:** String

**Example**

```powershell
Convert-Path ~\Documents
```

### Copy-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: copy, cp, cpi

Copies files, folders, registry keys or other items to a new location. Aliases: copy, cp, cpi.

**Syntax** (one line per parameter set)

```powershell
Copy-Item [-Path] <string[]> [[-Destination] <string>] [-Container] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-FromSession <PSSession>] [-ToSession <PSSession>] [<CommonParameters>]

Copy-Item [[-Destination] <string>] -LiteralPath <string[]> [-Container] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-FromSession <PSSession>] [-ToSession <PSSession>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies, as a string array, the path to the items to copy. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it's typed.
- `-Destination` <String> · position 1 · pipeline by property name · default Current directory — Specifies the path to the new location. The default is the current directory.
- `-Container` switch — Indicates that this cmdlet preserves container objects during the copy operation.
- `-Force` switch — Indicates that this cmdlet copies items that can't otherwise be changed, such as copying over a read-only file or alias.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies one or more path elements or patterns, such as "*.txt", to limit this cmdlet's operation.
- `-Exclude` <String[]> · wildcards — Specifies one or more path elements or patterns, such as "*.txt", to limit this cmdlet's operation.
- `-Recurse` switch — Indicates that this cmdlet does a recursive copy.
- `-PassThru` switch — Returns an object that represents the item with which you're working. By default, this cmdlet doesn't generate any output.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-FromSession` <PSSession> — This is a dynamic parameter made available by the FileSystem provider.
- `-ToSession` <PSSession> — This is a dynamic parameter made available by the FileSystem provider.

**Output:** None; PSObject

**Example**

```powershell
Copy-Item -Path .\src -Destination D:\Backup\src -Recurse -Force
```

### Copy-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: cpp

Copies a property and its value from one item to another (often between registry keys). Alias: cpp.

**Syntax** (one line per parameter set)

```powershell
Copy-ItemProperty [-Path] <string[]> [-Destination] <string> [-Name] <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Copy-ItemProperty [-Destination] <string> [-Name] <string> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies, as a string array, the path to the property to be copied.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the current location of the property. Unlike the Path parameter, the value of LiteralPath is used exactly as it is typed.
- `-Name` <String> · **required** · position 2 · pipeline by property name · alias -PSProperty — Specifies the name of the property to be copied.
- `-Destination` <String> · **required** · position 1 · pipeline by property name — Specifies the path to the destination location.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Force` switch — Forces the command to run without asking for user confirmation. Implementation varies from provider to provider.
- `-Filter` <String> · wildcards — Specifies a filter in the format or language of the provider. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — Specifies a user account that has permission to perform this action. The default is the current user.

**Output:** None; PSCustomObject

**Example**

```powershell
Copy-ItemProperty -Path HKCU:\Software\A -Destination HKCU:\Software\B -Name Theme
```

### Debug-Process (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Attaches the registered debugger to running processes.

**Syntax** (one line per parameter set)

```powershell
Debug-Process [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Process [-Id] <int[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Process -InputObject <Process[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name · alias -PID, -ProcessId — Specifies the process IDs of the processes to be debugged. The Id parameter name is optional.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name · alias -ProcessName — Specifies the names of the processes to be debugged. If there is more than one process with the same name, this cmdlet attaches a debugger to all processes with that name.
- `-InputObject` <Process[]> · **required** · pipeline by value — Specifies the process objects that represent processes to be debugged.

**Output:** None

**Example**

```powershell
Debug-Process -Name notepad
```

### Disable-ComputerRestore (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Turns off System Restore on the given drives.

**Syntax** (one line per parameter set)

```powershell
Disable-ComputerRestore [-Drive] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Drive` <String[]> · **required** · position 0 — Specifies the file system drives. Enter one or more file system drive letters, each followed by a colon and a backslash and enclosed in quotation marks, such as C:\ or D:\.

**Output:** None

**Example**

```powershell
Disable-ComputerRestore -Drive 'D:\'
```

### Enable-ComputerRestore (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Turns on System Restore on the given drives.

**Syntax** (one line per parameter set)

```powershell
Enable-ComputerRestore [-Drive] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Drive` <String[]> · **required** · position 0 — Specifies the file system drives. Enter one or more file system drive letters, each followed by a colon and a backslash and enclosed in quotation marks, such as C:\ or D:\.

**Output:** None

**Example**

```powershell
Enable-ComputerRestore -Drive 'C:\'
```

### Get-ChildItem (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: dir, gci, ls

Lists the files and folders (or registry keys, certificates...) in a location, optionally recursively and filtered. Aliases: dir, ls, gci.

**Syntax** (one line per parameter set)

```powershell
Get-ChildItem [[-Path] <string[]>] [[-Filter] <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Depth <uint32>] [-Force] [-Name] [-UseTransaction] [-Attributes <FlagsExpression[FileAttributes]>] [-FollowSymlink] [-Directory] [-File] [-Hidden] [-ReadOnly] [-System] [<CommonParameters>]

Get-ChildItem [[-Filter] <string>] -LiteralPath <string[]> [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Depth <uint32>] [-Force] [-Name] [-UseTransaction] [-Attributes <FlagsExpression[FileAttributes]>] [-FollowSymlink] [-Directory] [-File] [-Hidden] [-ReadOnly] [-System] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · position 0 · pipeline by value and by property name · default Current directory · wildcards — Specifies a path to one or more locations, or target paths, to be searched.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations, or target paths, to be searched.
- `-Filter` <String> · position 1 · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports filters.
- `-Include` <String[]> · wildcards — Specifies an array of one or more string patterns to be matched as the cmdlet gets child items.
- `-Exclude` <String[]> · wildcards — Specifies an array of one or more string patterns to be matched as the cmdlet gets child items.
- `-Recurse` switch · alias -s — Gets the items in the specified locations and in all child items of the locations.
- `-Depth` <UInt32> — This parameter was added in PowerShell 5.0 and enables you to control the depth of recursion.
- `-Force` switch — Allows the cmdlet to get items that otherwise can't be accessed by the user, such as hidden or system files.
- `-Name` switch — Gets only the names of the items in the location. The output is a string object that can be sent down the pipeline to other commands.
- `-Attributes` <FlagsExpression`1> — Gets files and folders with the specified attributes. This parameter supports all attributes and lets you specify complex combinations of attributes.
- `-FollowSymlink` switch — By default, the Get-ChildItem cmdlet displays symbolic links to directories found during recursion, but doesn't recurse into them.
- `-Directory` switch · alias -ad, -d — To get a list of directories, use the Directory parameter or the Attributes parameter with the Directory property.
- `-File` switch · alias -af — To get a list of files, use the File parameter. You can use the Recurse parameter with File.
- `-Hidden` switch · alias -ah, -h — To get only hidden items, use the Hidden parameter or the Attributes parameter with the Hidden property.
- `-ReadOnly` switch · alias -ar — To get only read-only items, use the ReadOnly parameter or the Attributes parameter ReadOnly property.
- `-System` switch · alias -as — Gets only system files and directories. To get only system files and folders, use the System parameter or Attributes parameter System property.

**Output:** AliasInfo; X509StoreLocation; X509Store; X509Certificate2; DictionaryEntry; DirectoryInfo; FileInfo; FunctionInfo; FilterInfo; RegistryKey; PSVariable; WSManConfigContainerElement; WSManConfigLeafElement; String

**Example**

```powershell
Get-ChildItem -Path C:\Projects -Filter *.ps1 -Recurse -File
```

### Get-Clipboard (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gcb

Reads the current Windows clipboard (text, files, image) into PowerShell. Alias: gcb.

**Syntax** (one line per parameter set)

```powershell
Get-Clipboard [-Format <ClipboardFormat>] [-TextFormatType <TextDataFormat>] [-Raw] [<CommonParameters>]
```

**Arguments**

- `-Format` <ClipboardFormat> — Specifies the type, or format, of the clipboard.
- `-TextFormatType` <TextDataFormat> — Specifies the text data format type of the clipboard.
- `-Raw` switch — Gets the entire contents of the clipboard. Multiline text is returned as a single multiline string rather than an array of strings.

**Output:** String; FileInfo; Stream; Image

**Example**

```powershell
Get-Clipboard -Format Text
```

### Get-ComputerInfo (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gin

Returns one object with many operating system and hardware properties (OS version, BIOS, memory...).

**Syntax** (one line per parameter set)

```powershell
Get-ComputerInfo [[-Property] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Property` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies, as a string array, the computer properties in which this cmdlet displays.

**Output:** ComputerInfo

**Example**

```powershell
Get-ComputerInfo -Property OsName, OsVersion, CsTotalPhysicalMemory
```

### Get-ComputerRestorePoint (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Lists the System Restore points on the local computer.

**Syntax** (one line per parameter set)

```powershell
Get-ComputerRestorePoint [[-RestorePoint] <int[]>] [<CommonParameters>]

Get-ComputerRestorePoint -LastStatus [<CommonParameters>]
```

**Arguments**

- `-RestorePoint` <Int32[]> · position 0 · default All restore points — Specifies the sequence numbers of the system restore points. You can specify either a single sequence number or a comma-separated array of sequence numbers.
- `-LastStatus` switch · **required** — Indicates that Get-ComputerRestorePoint gets the status of the most recent system restore operation.

**Output:** ManagementObject#root\default\SystemRestore or String

**Example**

```powershell
Get-ComputerRestorePoint | Select-Object SequenceNumber, Description, CreationTime
```

### Get-Content (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: cat, gc, type

Reads the content of a file (one string per line by default), or of other items. Aliases: cat, type, gc.

**Syntax** (one line per parameter set)

```powershell
Get-Content [-Path] <string[]> [-ReadCount <long>] [-TotalCount <long>] [-Tail <int>] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Delimiter <string>] [-Wait] [-Raw] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]

Get-Content -LiteralPath <string[]> [-ReadCount <long>] [-TotalCount <long>] [-Tail <int>] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Delimiter <string>] [-Wait] [-Raw] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]
```

**Arguments**

- `-ReadCount` <Int64> · pipeline by property name · default 1 — Specifies how many lines of content are sent through the pipeline at a time.
- `-TotalCount` <Int64> · pipeline by property name · alias -First, -Head — Specifies the number of lines from the beginning of a file or other item.
- `-Tail` <Int32> · pipeline by property name · alias -Last — Specifies the number of lines from the end of a file or other item. You can use the Tail parameter name or its alias, Last.
- `-Path` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the path to an item where Get-Content gets the content. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it's typed.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Force` switch — Force can override a read-only attribute or create directories to complete a file path.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Delimiter` <String> · default End-of-line character — Specifies the delimiter that Get-Content uses to divide the file into objects while it reads.
- `-Wait` switch — Causes the cmdlet to wait indefinitely, keeping the file open, until interrupted.
- `-Raw` switch — Ignores newline characters and returns the entire contents of a file in one string with the newlines preserved.
- `-Encoding` <FileSystemCmdletProviderEncoding> · default Default — Specifies the type of encoding for the target file. The default value is Default.
- `-Stream` <String> — Gets the contents of the specified alternate NTFS file stream from the file.

**Output:** Byte; String

**Example**

```powershell
Get-Content -Path .\app.log -Tail 20 -Wait
```

### Get-ControlPanelItem (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Lists Control Panel items, filtered by name or category.

**Syntax** (one line per parameter set)

```powershell
Get-ControlPanelItem [[-Name] <string[]>] [-Category <string[]>] [<CommonParameters>]

Get-ControlPanelItem -CanonicalName <string[]> [-Category <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies, as a string array, the names or name patterns of the control panel that this cmdlet gets.
- `-CanonicalName` <String[]> · **required** · wildcards — Specifies, as a string array, the control panel items by their canonical names or name patterns that this cmdlet gets.
- `-Category` <String[]> · wildcards — Specifies, as a string array, the categories of the control panel items in the specified categories that this cmdlet gets.

**Output:** ControlPanelItem

**Example**

```powershell
Get-ControlPanelItem -Category 'System and Security'
```

### Get-EventLog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Reads entries from classic event logs (Application, System, Security...) on local or remote computers.

**Syntax** (one line per parameter set)

```powershell
Get-EventLog [-LogName] <string> [[-InstanceId] <long[]>] [-ComputerName <string[]>] [-Newest <int>] [-After <datetime>] [-Before <datetime>] [-UserName <string[]>] [-Index <int[]>] [-EntryType <string[]>] [-Source <string[]>] [-Message <string>] [-AsBaseObject] [<CommonParameters>]

Get-EventLog [-ComputerName <string[]>] [-List] [-AsString] [<CommonParameters>]
```

**Arguments**

- `-LogName` <String> · **required** · position 0 · alias -LN · wildcards — Specifies the name of one event log. To find the log names use Get-EventLog -List.
- `-ComputerName` <String[]> · alias -Cn — This parameter specifies a remote computer's NetBIOS name, Internet Protocol (IP) address, or a fully qualified domain name (FQDN).
- `-Newest` <Int32> — Begins with the newest events and gets the specified number of events.
- `-After` <DateTime> — Gets events that occurred after a specified date and time. The After parameter date and time are excluded from the output.
- `-Before` <DateTime> — Gets events that occurred before a specified date and time. The Before parameter date and time are excluded from the output.
- `-UserName` <String[]> · wildcards — Specifies, as a string array, user names that are associated with events.
- `-InstanceId` <Int64[]> · position 1 — Specifies the Instance IDs to get from the event log. The parameter accepts a comma-separated string of values.
- `-Index` <Int32[]> — Specifies the index values to get from the event log. The parameter accepts a comma-separated string of values.
- `-EntryType` <String[]> · alias -ET · values: Error, Information, FailureAudit, SuccessAudit, Warning — Specifies, as a string array, the entry type of the events that this cmdlet gets.
- `-Source` <String[]> · alias -ABO · wildcards — Specifies, as a string array, sources that were written to the log that this cmdlet gets.
- `-Message` <String> · alias -MSG · wildcards — Specifies a string in the event message. You can use this parameter to search for messages that contain certain words or phrases.
- `-AsBaseObject` switch — Indicates that this cmdlet returns a standard System.Diagnostics.EventLogEntry object for each event.
- `-List` switch — Displays the list of event logs on the computer.
- `-AsString` switch — Indicates that this cmdlet returns the output as strings, instead of objects.

**Output:** String

**Example**

```powershell
Get-EventLog -LogName System -EntryType Error -Newest 20
```

### Get-HotFix (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Lists the Windows updates (hotfixes) installed on local or remote computers.

**Syntax** (one line per parameter set)

```powershell
Get-HotFix [[-Id] <string[]>] [-ComputerName <string[]>] [-Credential <pscredential>] [<CommonParameters>]

Get-HotFix [-Description <string[]>] [-ComputerName <string[]>] [-Credential <pscredential>] [<CommonParameters>]
```

**Arguments**

- `-Id` <String[]> · position 0 · alias -HFID — Filters the Get-HotFix results for specific hotfix Ids. Wildcards aren't accepted.
- `-Description` <String[]> · wildcards — Get-HotFix uses the Description parameter to specify hotfix types. Wildcards are permitted.
- `-ComputerName` <String[]> · pipeline by property name · alias -CN, -__Server, -IPAddress — Specifies a remote computer. Type the NetBIOS name, an Internet Protocol (IP) address, or a fully qualified domain name (FQDN) of a remote computer.
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to access the computer and run commands.

**Output:** ManagementObject#root\cimv2\Win32_QuickFixEngineering

**Example**

```powershell
Get-HotFix | Sort-Object InstalledOn -Descending | Select-Object -First 5
```

### Get-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gi

Gets the item at a path (a file, folder, registry key...) without listing its contents. Alias: gi.

**Syntax** (one line per parameter set)

```powershell
Get-Item [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]

Get-Item -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path to an item. This cmdlet gets the item at the specified location.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it's typed.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Force` switch — Indicates that this cmdlet gets items that can't otherwise be accessed, such as hidden items.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Stream` <String[]> · default No alternate file streams · wildcards — This is a dynamic parameter made available by the FileSystem provider.

**Output:** AliasInfo; X509StoreLocation; X509Store; X509Certificate2; DictionaryEntry; DirectoryInfo; FileInfo; FunctionInfo; FilterInfo; RegistryKey; PSVariable; WSManConfigContainerElement; WSManConfigLeafElement

**Example**

```powershell
Get-Item -Path C:\Windows\notepad.exe | Select-Object Name, Length, LastWriteTime
```

### Get-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gp

Gets the properties of an item, typically registry values or file attributes. Alias: gp.

**Syntax** (one line per parameter set)

```powershell
Get-ItemProperty [-Path] <string[]> [[-Name] <string[]>] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Get-ItemProperty [[-Name] <string[]>] -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies the path to the item or items.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the current location of the property. The value of LiteralPath is used exactly as it is typed.
- `-Name` <String[]> · position 1 · alias -PSProperty — Specifies the name of the property or properties to retrieve.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** Boolean; String; DateTime; FileInfo; DirectoryInfo

**Example**

```powershell
Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name ProductName
```

### Get-ItemPropertyValue (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gpv

Gets just the value of one or more properties of an item. Alias: gpv.

**Syntax** (one line per parameter set)

```powershell
Get-ItemPropertyValue [[-Path] <string[]>] [-Name] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Get-ItemPropertyValue [-Name] <string[]> -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · position 0 · pipeline by value and by property name — Specifies the path to the item or items.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the current location of the property. Unlike the Path parameter, the value of LiteralPath is used exactly as it is typed.
- `-Name` <String[]> · **required** · position 1 · alias -PSProperty — Specifies the name of the property or properties to retrieve.
- `-Filter` <String> · wildcards — Specifies a filter in the format or language of the provider. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes from the operation.
- `-Credential` <PSCredential> · pipeline by property name — Specifies a user account that has permission to perform this action. The default is the current user.

**Output:** PSObject

**Example**

```powershell
Get-ItemPropertyValue -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name CurrentBuild
```

### Get-Location (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gl, pwd

Shows the current working location, or the location stack. Aliases: pwd, gl.

**Syntax** (one line per parameter set)

```powershell
Get-Location [-PSProvider <string[]>] [-PSDrive <string[]>] [-UseTransaction] [<CommonParameters>]

Get-Location [-Stack] [-StackName <string[]>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-PSProvider` <String[]> · pipeline by property name — Gets the current location in the drive supported by the specified PowerShell provider.
- `-PSDrive` <String[]> · pipeline by property name — Gets the current location in the specified PowerShell drive.
- `-Stack` switch — Indicates that this cmdlet displays the locations added to the current location stack.
- `-StackName` <String[]> · pipeline by property name — Specifies, as a string array, the named location stacks. Enter one or more location stack names.

**Output:** PathInfo; PathInfoStack

**Example**

```powershell
Get-Location
```

### Get-Process (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gps, ps

Lists running processes on the local (or, in 5.1, a remote) computer. Aliases: ps, gps.

**Syntax** (one line per parameter set)

```powershell
Get-Process [[-Name] <string[]>] [-ComputerName <string[]>] [-Module] [-FileVersionInfo] [<CommonParameters>]

Get-Process [[-Name] <string[]>] -IncludeUserName [<CommonParameters>]

Get-Process -Id <int[]> [-ComputerName <string[]>] [-Module] [-FileVersionInfo] [<CommonParameters>]

Get-Process -Id <int[]> -IncludeUserName [<CommonParameters>]

Get-Process -InputObject <Process[]> -IncludeUserName [<CommonParameters>]

Get-Process -InputObject <Process[]> [-ComputerName <string[]>] [-Module] [-FileVersionInfo] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name · alias -ProcessName · wildcards — Specifies one or more processes by process name. You can specify multiple process names separated by commas and use wildcard characters.
- `-Id` <Int32[]> · **required** · pipeline by property name · alias -PID — Specifies one or more processes by process ID (PID). You can specify multiple IDs separated by commas.
- `-InputObject` <Process[]> · **required** · pipeline by value — Specifies one or more Process objects. Use a variable that contains the objects, or a command or expression that gets the objects.
- `-IncludeUserName` switch · **required** — Indicates that this command adds a UserName property to each returned Process object.
- `-ComputerName` <String[]> · pipeline by property name · alias -Cn · default Local computer — Specifies the computers for which this cmdlet gets running processes. The default is the local computer.
- `-Module` switch — Indicates that this cmdlet gets the modules that the process has loaded.
- `-FileVersionInfo` switch · alias -FV, -FVI — Indicates that this cmdlet gets the file version information for the program that runs in the process.

**Output:** Process; FileVersionInfo; ProcessModule

**Example**

```powershell
Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 10 Name, Id, WorkingSet64
```

### Get-PSDrive (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gdr

Lists the PowerShell drives in the session (file system, registry, Env:, Cert:...) with used and free space. Alias: gdr.

**Syntax** (one line per parameter set)

```powershell
Get-PSDrive [[-Name] <string[]>] [-Scope <string>] [-PSProvider <string[]>] [-UseTransaction] [<CommonParameters>]

Get-PSDrive [-LiteralName] <string[]> [-Scope <string>] [-PSProvider <string[]>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — Specifies, as a string array, the name or name of drives that this cmdlet gets in the operation.
- `-LiteralName` <String[]> · **required** · position 0 · pipeline by property name — Specifies the name of the drive.
- `-Scope` <String> · pipeline by property name — Specifies the scope in which this cmdlet gets the drives.
- `-PSProvider` <String[]> · pipeline by property name — Specifies, as a string array, the Windows PowerShell provider. This cmdlet gets only the drives supported by this provider.

**Output:** PSDriveInfo

**Example**

```powershell
Get-PSDrive -PSProvider FileSystem
```

### Get-PSProvider (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Lists the PowerShell providers (FileSystem, Registry, Certificate...) and their drives.

**Syntax** (one line per parameter set)

```powershell
Get-PSProvider [[-PSProvider] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-PSProvider` <String[]> · position 0 · pipeline by property name — Specifies the name or names of the PowerShell providers about which this cmdlet gets information.

**Output:** ProviderInfo

**Example**

```powershell
Get-PSProvider
```

### Get-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gsv

Lists Windows services and their status, on the local (or, in 5.1, remote) computer. Alias: gsv.

**Syntax** (one line per parameter set)

```powershell
Get-Service [[-Name] <string[]>] [-ComputerName <string[]>] [-DependentServices] [-RequiredServices] [-Include <string[]>] [-Exclude <string[]>] [<CommonParameters>]

Get-Service -DisplayName <string[]> [-ComputerName <string[]>] [-DependentServices] [-RequiredServices] [-Include <string[]>] [-Exclude <string[]>] [<CommonParameters>]

Get-Service [-ComputerName <string[]>] [-DependentServices] [-RequiredServices] [-Include <string[]>] [-Exclude <string[]>] [-InputObject <ServiceController[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · alias -ServiceName · wildcards — Specifies the service names of services to be retrieved. Wildcards are permitted.
- `-ComputerName` <String[]> · pipeline by property name · alias -Cn — Gets the services running on the specified computers. The default is the local computer.
- `-DependentServices` switch · alias -DS — Indicates that this cmdlet gets only the services that depend upon the specified service.
- `-RequiredServices` switch · alias -SDO, -ServicesDependedOn · wildcards — Indicates that this cmdlet gets only the services that this service requires.
- `-DisplayName` <String[]> · **required** · wildcards — Specifies, as a string array, the display names of services to be retrieved.
- `-Include` <String[]> · wildcards — Specifies, as a string array, a service or services that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, a service or services that this cmdlet excludes from the operation.
- `-InputObject` <ServiceController[]> · pipeline by value — Specifies ServiceController objects representing the services to be retrieved.

**Output:** ServiceController

**Example**

```powershell
Get-Service -Name W* | Where-Object Status -eq Stopped
```

### Get-TimeZone (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gtz

Shows the current time zone, or lists the time zones available. Alias: gtz.

**Syntax** (one line per parameter set)

```powershell
Get-TimeZone [[-Name] <string[]>] [<CommonParameters>]

Get-TimeZone -Id <string[]> [<CommonParameters>]

Get-TimeZone -ListAvailable [<CommonParameters>]
```

**Arguments**

- `-Id` <String[]> · **required** · pipeline by property name — Specifies, as a string array, the ID or IDs of the time zones that this cmdlet gets.
- `-ListAvailable` switch · **required** — Indicates that this cmdlet gets all available time zones.
- `-Name` <String[]> · position 0 · pipeline by value · wildcards — Specifies, as a string array, the name or names of the time zones that this cmdlet gets.

**Output:** TimeZoneInfo[]

**Example**

```powershell
Get-TimeZone -ListAvailable | Where-Object Id -like '*Eastern*'
```

### Get-Transaction (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Gets the active transaction (for providers that support transactions, such as the registry).

**Syntax** (one line per parameter set)

```powershell
Get-Transaction [<CommonParameters>]
```

**Output:** PSTransaction

**Example**

```powershell
Get-Transaction
```

### Get-WmiObject (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: gwmi

(Windows PowerShell only) Gets WMI class instances or class info; replaced by Get-CimInstance in PowerShell 7. Alias: gwmi.

**Syntax** (one line per parameter set)

```powershell
Get-WmiObject [-Class] <string> [[-Property] <string[]>] [-Filter <string>] [-Amended] [-DirectRead] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject [[-Class] <string>] [-Recurse] [-Amended] [-List] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject -Query <string> [-Amended] [-DirectRead] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject [-Amended] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]

Get-WmiObject [-Amended] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [<CommonParameters>]
```

**Arguments**

- `-Class` <String> · **required** · position 0 · alias -ClassName — Specifies the name of a WMI class. When this parameter is used, the cmdlet retrieves instances of the WMI class.
- `-Recurse` switch — Searches the current namespace and all other namespaces for the class name that is specified by the Class parameter.
- `-Property` <String[]> · position 1 — Specifies the WMI class properties that this cmdlet gets information from.
- `-Filter` <String> — Specifies a Where clause to use as a filter. Uses the syntax of the WMI Query Language (WQL).
- `-Amended` switch — Gets or sets a value that indicates whether the objects that are returned from WMI should contain amended information.
- `-DirectRead` switch — Specifies whether direct access to the WMI provider is requested for the specified class without any regard to its base class or to its derived classes.
- `-List` switch — Gets the names of the WMI classes in the WMI repository namespace that is specified by the Namespace parameter.
- `-Query` <String> · **required** — Runs the specified WMI Query Language (WQL) statement. This parameter does not support event queries.
- `-AsJob` switch — Runs the command as a background job. Use this parameter to run commands that take a long time to finish.
- `-Impersonation` <ImpersonationLevel> — Specifies the impersonation level to use.
- `-Authentication` <AuthenticationLevel> — Specifies the authentication level to be used with the WMI connection.
- `-Locale` <String> — Specifies the preferred locale for WMI objects. Enter a value in MS_<LCID> format.
- `-EnableAllPrivileges` switch — Enables all the privileges of the current user before the command makes the WMI call.
- `-Authority` <String> — Specifies the authority to use to authenticate the WMI connection. You can specify standard NTLM or Kerberos authentication.
- `-Credential` <PSCredential> — Specifies a user account that has permission to perform this action. The default is the current user.
- `-ThrottleLimit` <Int32> — Specifies the maximum number of WMI operations that can be executed simultaneously.
- `-ComputerName` <String[]> · alias -Cn — Specifies the target computer for the management operation. Enter a fully qualified domain name (FQDN), a NetBIOS name, or an IP address.
- `-Namespace` <String> · alias -NS — When used with the Class parameter, the Namespace parameter specifies the WMI repository namespace where the specified WMI class is located.

**Output:** PSObject or System.Management.Automation.RemotingJob

**Example**

```powershell
Get-WmiObject -Class Win32_LogicalDisk -Filter "DriveType=3"
```

### Invoke-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: ii

Performs the default action on an item, such as opening a file in its associated program. Alias: ii.

**Syntax** (one line per parameter set)

```powershell
Invoke-Item [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Invoke-Item -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path to the selected item.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it's typed.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None

**Example**

```powershell
Invoke-Item .\report.xlsx
```

### Invoke-WmiMethod (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: iwmi

(Windows PowerShell only) Calls a WMI method; use Invoke-CimMethod in PowerShell 7.

**Syntax** (one line per parameter set)

```powershell
Invoke-WmiMethod [-Class] <string> [-Name] <string> [[-ArgumentList] <Object[]>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> -InputObject <wmi> [-ArgumentList <Object[]>] [-AsJob] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> -Path <string> [-ArgumentList <Object[]>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-WmiMethod [-Name] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ManagementObject> · **required** · pipeline by value — Specifies a ManagementObject object to use as input. When this parameter is used, all other parameters except the Flag and Argument parameters are ignored.
- `-Path` <String> · **required** — Specifies the WMI object path of a WMI class, or specifies the WMI object path of an instance of a WMI class.
- `-Class` <String> · **required** · position 0 — Specifies the WMI class that contains a static method to call.
- `-Name` <String> · **required** · position 1 — Specifies the name of the method to be invoked. This parameter is mandatory and cannot be null or empty.
- `-ArgumentList` <Object[]> · position 2 · alias -Args — Specifies the parameters to pass to the called method. The value of this parameter must be an array of objects, and they must appear in the order required by the called method.
- `-AsJob` switch — Indicates that this cmdlet runs the command as a background job. Use this parameter to run commands that take a long time to finish.
- `-Impersonation` <ImpersonationLevel> — Specifies the impersonation level to use.
- `-Authentication` <AuthenticationLevel> — Specifies the authentication level to be used with the WMI connection.
- `-Locale` <String> — Specifies the preferred locale for WMI objects. Specify the value of the Locale parameter as an array in the MS_<LCID> format in the preferred order.
- `-EnableAllPrivileges` switch — Indicates that this cmdlet enables all the privileges of the current user before the command makes the WMI call.
- `-Authority` <String> — Specifies the authority to use to authenticate the WMI connection. You can specify standard Windows NT LAN Manager (NTLM) or Kerberos authentication.
- `-Credential` <PSCredential> — Specifies a user account that has permission to perform this action. The default is the current user.
- `-ThrottleLimit` <Int32> — Specifies a throttle value for the number of WMI operations that can be executed simultaneously.
- `-ComputerName` <String[]> · alias -Cn — Specifies, as a string array, the computers that this cmdlet runs the command on.
- `-Namespace` <String> · alias -NS — When used with the Class parameter, this parameter specifies the WMI repository namespace where the referenced WMI class or object is located.

**Output:** None

**Example**

```powershell
Invoke-WmiMethod -Class Win32_Process -Name Create -ArgumentList 'notepad.exe'
```

### Join-Path (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Combines a parent path and child path(s) into one path, adding separators correctly.

**Syntax** (one line per parameter set)

```powershell
Join-Path [-Path] <string[]> [-ChildPath] <string> [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · alias -PSPath · wildcards — Specifies the main path (or paths) to which the child-path is appended.
- `-ChildPath` <String> · **required** · position 1 · pipeline by property name · wildcards — Specifies the elements to append to the value of the Path parameter. Wildcards are permitted.
- `-Resolve` switch — Indicates that this cmdlet should attempt to resolve the joined path from the current provider.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** String

**Example**

```powershell
Join-Path -Path $env:USERPROFILE -ChildPath 'Documents\notes.txt'
```

### Limit-EventLog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Sets the maximum size, overflow action and retention of classic event logs.

**Syntax** (one line per parameter set)

```powershell
Limit-EventLog [-LogName] <string[]> [-ComputerName <string[]>] [-RetentionDays <int>] [-OverflowAction <OverflowAction>] [-MaximumSize <long>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-LogName` <String[]> · **required** · position 0 · alias -LN — Specifies the event logs. Enter the log name (the value of the Log property; not the LogDisplayName) of one or more event logs, separated by commas.
- `-ComputerName` <String[]> · alias -CN — Specifies remote computers. The default is the local computer.
- `-RetentionDays` <Int32> · alias -MRD — Specifies the minimum number of days that an event must remain in the event log.
- `-OverflowAction` <OverflowAction> · alias -OFA · values: OverwriteOlder, OverwriteAsNeeded, DoNotOverwrite — Specifies what happens when the event log reaches its maximum size.
- `-MaximumSize` <Int64> — Specifies the maximum size of the event logs in bytes. Enter a value between 64 kilobytes (KB) and 4 gigabytes (GB).

**Output:** None

**Example**

```powershell
Limit-EventLog -LogName Application -MaximumSize 64MB -OverflowAction OverwriteAsNeeded
```

### Move-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: mi, move, mv

Moves (or renames by moving) files, folders, registry keys and other items. Aliases: move, mv, mi.

**Syntax** (one line per parameter set)

```powershell
Move-Item [-Path] <string[]> [[-Destination] <string>] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Move-Item [[-Destination] <string>] -LiteralPath <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · default Current directory · wildcards — Specifies the path to the current location of the items. The default is the current directory.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the current location of the items. The value of LiteralPath is used exactly as it is typed.
- `-Destination` <String> · position 1 · pipeline by property name · default Current directory · wildcards — Specifies the path to the location where the items are being moved. The default is the current directory.
- `-Force` switch — Forces the command to run without asking for user confirmation. For the FileSystem provider, if a file exists at the target location, you can't overwrite it without using this parameter.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-PassThru` switch — Returns an object representing the moved item. By default, this cmdlet does not generate any output.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None; PSObject

**Example**

```powershell
Move-Item -Path .\*.log -Destination .\Archive
```

### Move-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: mp

Moves a property from one item to another (for example between registry keys). Alias: mp.

**Syntax** (one line per parameter set)

```powershell
Move-ItemProperty [-Path] <string[]> [-Destination] <string> [-Name] <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Move-ItemProperty [-Destination] <string> [-Name] <string[]> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path to the current location of the property. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the current location of the property. Unlike the Path parameter, the value of LiteralPath is used exactly as it is typed.
- `-Name` <String[]> · **required** · position 2 · pipeline by property name · alias -PSProperty — Specifies the name of the property to be moved.
- `-Destination` <String> · **required** · position 1 · pipeline by property name — Specifies the path to the destination location.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Force` switch — Forces the command to run without asking for user confirmation. Implementation varies from provider to provider.
- `-Filter` <String> · wildcards — Specifies a filter in the format or language of the provider. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Specifies, as a string array, a property or property that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, a property or property that this cmdlet excludes from the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None; PSCustomObject

**Example**

```powershell
Move-ItemProperty -Path HKCU:\Software\Old -Destination HKCU:\Software\New -Name Setting1
```

### New-EventLog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Creates a classic event log and/or registers an event source for writing events.

**Syntax** (one line per parameter set)

```powershell
New-EventLog [-LogName] <string> [-Source] <string[]> [[-ComputerName] <string[]>] [-CategoryResourceFile <string>] [-MessageResourceFile <string>] [-ParameterResourceFile <string>] [<CommonParameters>]
```

**Arguments**

- `-CategoryResourceFile` <String> · alias -CRF — Specifies the path to the file that contains category strings for the source events.
- `-ComputerName` <String[]> · position 2 · alias -CN · default Local computer — Creates the new event logs on the specified computers. The default is the local computer.
- `-LogName` <String> · **required** · position 0 · alias -LN — Specifies the name of the event log.
- `-MessageResourceFile` <String> · alias -MRF — Specifies the path to the file that contains message formatting strings for the source events.
- `-ParameterResourceFile` <String> · alias -PRF — Specifies the path to the file that contains strings used for parameter substitutions in event descriptions.
- `-Source` <String[]> · **required** · position 1 · alias -SRC — Specifies the names of the event log sources, such as application programs that write to the event log.

**Output:** EventLogEntry

**Example**

```powershell
New-EventLog -LogName Application -Source MyScript
```

### New-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: ni

Creates a new item: a file, folder, registry key, symbolic link, variable... Alias: ni.

**Syntax** (one line per parameter set)

```powershell
New-Item [-Path] <string[]> [-ItemType <string>] [-Value <Object>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

New-Item [[-Path] <string[]>] -Name <string> [-ItemType <string>] [-Value <Object>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by property name · default Current location — Specifies the path of the location of the new item. The default is the current location when Path is omitted.
- `-Name` <String> · **required** · pipeline by property name — Specifies the name of the new item. You can specify the name of the new item in the Name or Path parameter value, and you can specify the path of the new item in Name or Path value.
- `-ItemType` <String> · pipeline by property name · alias -Type — Specifies the provider-specified type of the new item. The available values of this parameter depend on the current provider you are using.
- `-Value` <Object> · pipeline by value and by property name · alias -Target — Specifies the value of the new item. You can also pipe a value to New-Item.
- `-Force` switch — Forces this cmdlet to create an item that writes over an existing read-only item.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** DictionaryEntry; DirectoryInfo; FileInfo; AliasInfo; FunctionInfo; PSVariable

**Example**

```powershell
New-Item -Path .\logs\today.txt -ItemType File -Force
```

### New-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Creates a new property on an item and sets its value (for example a registry value of a given type).

**Syntax** (one line per parameter set)

```powershell
New-ItemProperty [-Path] <string[]> [-Name] <string> [-PropertyType <string>] [-Value <Object>] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

New-ItemProperty [-Name] <string> -LiteralPath <string[]> [-PropertyType <string>] [-Value <Object>] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 — Specifies the path of the item. This parameter identifies the item to which this cmdlet adds the new property.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it is typed.
- `-Name` <String> · **required** · position 1 · pipeline by property name · alias -PSProperty — Specifies a name for the new property. If the property is a registry entry, this parameter specifies the name of the entry.
- `-PropertyType` <String> · pipeline by property name · alias -Type — Specifies the type of property that this cmdlet adds.
- `-Value` <Object> · pipeline by property name — Specifies the property value. If the property is a registry entry, this parameter specifies the value of the entry.
- `-Force` switch — Forces the cmdlet to create a property on an object that cannot otherwise be accessed by the user.
- `-Filter` <String> — Specifies a filter in the format or language of the provider. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> — Specifies, as a string array, a property or property that this cmdlet excludes from the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — Specifies a user account that has permission to perform this action. The default is the current user.

**Output:** PSCustomObject

**Example**

```powershell
New-ItemProperty -Path HKCU:\Software\MyApp -Name Enabled -Value 1 -PropertyType DWord
```

### New-PSDrive (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: mount, ndr

Creates a PowerShell drive mapped to a location (folder, registry key, network share), optionally persistent. Aliases: mount, ndr.

**Syntax** (one line per parameter set)

```powershell
New-PSDrive [-Name] <string> [-PSProvider] <string> [-Root] <string> [-Description <string>] [-Scope <string>] [-Persist] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by property name — Specifies a name for the new drive. For persistent mapped network drives, use a drive letter.
- `-PSProvider` <String> · **required** · position 1 · pipeline by property name — Specifies the PowerShell provider that supports drives of this kind.
- `-Root` <String> · **required** · position 2 · pipeline by property name — Specifies the data store location to which a PowerShell drive is mapped.
- `-Description` <String> · pipeline by property name — Specifies a brief text description of the drive. Type any string.
- `-Scope` <String> · pipeline by property name · default Local — Specifies a scope for the drive. The acceptable values for this parameter are: Global, Local, and Script, or a number relative to the current scope.
- `-Persist` switch · pipeline by property name — Indicates that this cmdlet creates a Windows mapped network drive. The Persist parameter is only available on Windows.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — Specifies a user account that has permission to do this action. The default is the current user.

**Output:** PSDriveInfo

**Example**

```powershell
New-PSDrive -Name P -PSProvider FileSystem -Root \\Server\Share -Persist
```

### New-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Creates a new Windows service entry for an executable (registry entry and service database).

**Syntax** (one line per parameter set)

```powershell
New-Service [-Name] <string> [-BinaryPathName] <string> [-DisplayName <string>] [-Description <string>] [-StartupType <ServiceStartMode>] [-Credential <pscredential>] [-DependsOn <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · alias -ServiceName — Specifies the name of the service. This parameter is required.
- `-BinaryPathName` <String> · **required** · position 1 — Specifies the path of the executable file for the service. This parameter is required.
- `-DisplayName` <String> — Specifies a display name for the service.
- `-Description` <String> — Specifies a description of the service.
- `-StartupType` <ServiceStartMode> · default Automatic — Sets the startup type of the service.
- `-Credential` <PSCredential> — Specifies the account used by the service as the Service Logon Account.
- `-DependsOn` <String[]> — Specifies the names of other services upon which the new service depends.

**Output:** ServiceController

**Example**

```powershell
New-Service -Name MySvc -BinaryPathName 'C:\Svc\mysvc.exe' -StartupType Automatic
```

### New-WebServiceProxy (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

(Windows PowerShell only) Creates a proxy object for calling a SOAP web service.

**Syntax** (one line per parameter set)

```powershell
New-WebServiceProxy [-Uri] <uri> [[-Class] <string>] [[-Namespace] <string>] [<CommonParameters>]

New-WebServiceProxy [-Uri] <uri> [[-Class] <string>] [[-Namespace] <string>] [-Credential <pscredential>] [<CommonParameters>]

New-WebServiceProxy [-Uri] <uri> [[-Class] <string>] [[-Namespace] <string>] [-UseDefaultCredential] [<CommonParameters>]
```

**Arguments**

- `-Uri` <Uri> · **required** · position 0 · alias -WL, -WSDL, -Path — Specifies the URI of the Web service. Enter a URI or the path and filename of a file that contains a service description.
- `-Class` <String> · position 1 · alias -FileName, -FN — Specifies a name for the proxy class that the cmdlet creates for the Web service.
- `-Namespace` <String> · position 2 · alias -NS — Specifies a namespace for the new class.
- `-Credential` <PSCredential> · alias -Cred — Specifies a user account that has permission to perform this action. The default is the current user.
- `-UseDefaultCredential` switch · alias -UDC — Indicates that this cmdlet uses the default credential. This cmdlet sets the UseDefaultCredential property in the resulting proxy object to True.

**Output:** A Web service proxy object

**Example**

```powershell
$p = New-WebServiceProxy -Uri 'http://server/service.asmx?WSDL'
```

### Pop-Location (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: popd

Returns to the location most recently saved by Push-Location. Alias: popd.

**Syntax** (one line per parameter set)

```powershell
Pop-Location [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-PassThru` switch — Passes an object that represents the location to the pipeline. By default, this cmdlet does not generate any output.
- `-StackName` <String> · pipeline by property name — Specifies the location stack from which the location is popped. Enter a location stack name.

**Output:** None; PathInfo

**Example**

```powershell
Pop-Location
```

### Push-Location (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: pushd

Saves the current location on a stack and optionally moves to a new one. Alias: pushd.

**Syntax** (one line per parameter set)

```powershell
Push-Location [[-Path] <string>] [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]

Push-Location [-LiteralPath <string>] [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · position 0 · pipeline by value and by property name · wildcards — Changes your location to the location specified by this path after it adds (pushes) the current location onto the top of the stack.
- `-LiteralPath` <String> · pipeline by property name · alias -PSPath — Specifies the path to the new location. Unlike the Path parameter, the value of the LiteralPath parameter is used exactly as it is typed.
- `-PassThru` switch — Passes an object representing the location to the pipeline. By default, this cmdlet does not generate any output.
- `-StackName` <String> · pipeline by property name · default Default stack — Specifies the location stack to which the current location is added. Enter a location stack name.

**Output:** None; PathInfo

**Example**

```powershell
Push-Location C:\Windows\System32
```

### Register-WmiEvent (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

(Windows PowerShell only) Subscribes to a WMI event, with an optional action script block.

**Syntax** (one line per parameter set)

```powershell
Register-WmiEvent [-Class] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-Credential <pscredential>] [-ComputerName <string>] [-Timeout <long>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-WmiEvent [-Query] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-Credential <pscredential>] [-ComputerName <string>] [-Timeout <long>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

**Arguments**

- `-Namespace` <String> · alias -NS — Specifies the namespace of the WMI class.
- `-Credential` <PSCredential> — Specifies a user account that has permission to perform this action. The default is the current user.
- `-ComputerName` <String> · alias -Cn — Specifies the name of the computer on which the command runs. The default is the local computer.
- `-Class` <String> · **required** · position 0 — Specifies the event to which you are subscribing. Enter the WMI class that generates the events.
- `-Query` <String> · **required** · position 0 — Specifies a query in WMI Query Language (WQL) that identifies the WMI event class, such as: select * from __InstanceDeletionEvent.
- `-Timeout` <Int64> · alias -TimeoutMSec — Specifies how long Windows PowerShell waits for this command to finish.
- `-SourceIdentifier` <String> · position 100 — Specifies a name that you select for the subscription. The name that you select must be unique in the current session.
- `-Action` <ScriptBlock> · position 101 — Specifies commands that handle the events. The commands in the Action parameter run when an event is raised instead of sending the event to the event queue.
- `-MessageData` <PSObject> — Specifies any additional data to be associated with this event subscription.
- `-SupportEvent` switch — Indicates that this cmdlet hides the event subscription. Use this parameter when the current subscription is part of a more complex event registration mechanism and it should not be discovered independently.
- `-Forward` switch — Indicates that this cmdlet sends events for this subscription to the session on the local computer.
- `-MaxTriggerCount` <Int32> — Specifies the maximum trigger count.

**Output:** None

**Example**

```powershell
Register-WmiEvent -Class Win32_ProcessStartTrace -SourceIdentifier ProcStart -Action { $Event.SourceEventArgs.NewEvent.ProcessName }
```

### Remove-Computer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Removes the local (or remote) computer from its domain, optionally joining a workgroup.

**Syntax** (one line per parameter set)

```powershell
Remove-Computer [[-UnjoinDomainCredential] <pscredential>] [-Restart] [-Force] [-PassThru] [-WorkgroupName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Computer -UnjoinDomainCredential <pscredential> [-LocalCredential <pscredential>] [-Restart] [-ComputerName <string[]>] [-Force] [-PassThru] [-WorkgroupName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-UnjoinDomainCredential` <PSCredential> · **required** · position 0 · alias -Credential — Specifies a user account that has permission to remove the computers from their current domains.
- `-LocalCredential` <PSCredential> — Specifies a user account that has permission to connect to the computers that the ComputerName parameter specifies.
- `-Restart` switch — Indicates that this cmdlet restarts the computers that are being removed.
- `-ComputerName` <String[]> · pipeline by value and by property name — Specifies the computers to be removed from their domains. The default is the local computer.
- `-Force` switch — Suppresses the user prompt. By default, Remove-Computer prompts you for confirmation before removing each computer.
- `-PassThru` switch — Returns the results of the command. Otherwise, this cmdlet does not generate any output.
- `-WorkgroupName` <String> — Specifies the name of a workgroup to which the computers are added when they are removed from their domains.

**Output:** ComputerChangeInfo

**Example**

```powershell
Remove-Computer -UnjoinDomainCredential CONTOSO\Admin -WorkgroupName WORKGROUP -Restart
```

### Remove-EventLog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Deletes a classic event log or unregisters an event source.

**Syntax** (one line per parameter set)

```powershell
Remove-EventLog [-LogName] <string[]> [[-ComputerName] <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-EventLog [[-ComputerName] <string[]>] [-Source <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · position 1 · alias -CN — Specifies a remote computer. The default is the local computer.
- `-LogName` <String[]> · **required** · position 0 · alias -LN — Specifies the event logs. Enter the log name of one or more event logs, separated by commas.
- `-Source` <String[]> · alias -SRC — Specifies the event sources that this cmdlet unregisters. Enter the source names, not the executable name, separated by commas.

**Output:** None

**Example**

```powershell
Remove-EventLog -Source MyScript
```

### Remove-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: del, erase, rd, ri, rm, rmdir

Deletes files, folders, registry keys, variables and other items. Aliases: del, rm, rmdir, rd, erase, ri.

**Syntax** (one line per parameter set)

```powershell
Remove-Item [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]

Remove-Item -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Recurse] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-Stream <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies a path of the items being removed. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it's typed.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Recurse` switch — Indicates that this cmdlet deletes the items in the specified locations and in all child items of the locations.
- `-Force` switch — Forces the cmdlet to remove items that can't otherwise be changed, such as hidden or read-only files or read-only aliases or variables.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Stream` <String[]> · wildcards — This is a dynamic parameter made available by the FileSystem provider.

**Output:** None

**Example**

```powershell
Remove-Item -Path .\Temp -Recurse -Force
```

### Remove-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: rp

Deletes a property and its value from an item (for example a registry value). Alias: rp.

**Syntax** (one line per parameter set)

```powershell
Remove-ItemProperty [-Path] <string[]> [-Name] <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Remove-ItemProperty [-Name] <string[]> -LiteralPath <string[]> [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path of the item whose properties are being removed. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the current location of the property. Unlike the Path parameter, the value of LiteralPath is used exactly as it is typed.
- `-Name` <String[]> · **required** · position 1 · pipeline by property name · alias -PSProperty · wildcards — Specifies the names of the properties to remove. Wildcard characters are permitted.
- `-Force` switch — Forces the cmdlet to remove a property of an object that cannot otherwise be accessed by the user.
- `-Filter` <String> · wildcards — Specifies a filter in the format or language of the provider. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies items that this cmdlet omits. The value of this parameter qualifies the Path parameter.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None

**Example**

```powershell
Remove-ItemProperty -Path HKCU:\Software\MyApp -Name LastRun
```

### Remove-PSDrive (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: rdr

Removes PowerShell drives and disconnects mapped network drives. Alias: rdr.

**Syntax** (one line per parameter set)

```powershell
Remove-PSDrive [-Name] <string[]> [-PSProvider <string[]>] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Remove-PSDrive [-LiteralName] <string[]> [-PSProvider <string[]>] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the names of the drives to remove. Do not type a colon (:) after the drive name.
- `-LiteralName` <String[]> · **required** · position 0 · pipeline by property name — Specifies the name of the drive.
- `-PSProvider` <String[]> · pipeline by property name — Specifies an array of PSProvider objects. This cmdlet removes and disconnects all of the drives associated with the specified PowerShell provider.
- `-Scope` <String> · pipeline by property name · default Local — Specifies a scope for the drive. The acceptable values for this parameter are: Global, Local, and Script, or a number relative to the current scope.
- `-Force` switch — Removes the current PowerShell drive.

**Output:** None

**Example**

```powershell
Remove-PSDrive -Name P
```

### Remove-WmiObject (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: rwmi

(Windows PowerShell only) Deletes a WMI instance; use Remove-CimInstance in PowerShell 7.

**Syntax** (one line per parameter set)

```powershell
Remove-WmiObject [-Class] <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject -InputObject <wmi> [-AsJob] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject -Path <string> [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-WmiObject [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ManagementObject> · **required** · pipeline by value — Specifies a ManagementObject object to use as input. When this parameter is used, all other parameters are ignored.
- `-Path` <String> · **required** — Specifies the WMI object path of a WMI class, or specifies the WMI object path of an instance of a WMI class to delete.
- `-Class` <String> · **required** · position 0 — Specifies the name of a WMI class that this cmdlet deletes.
- `-AsJob` switch — Indicates that this cmdlet run as a background job. Use this parameter to run commands that take a long time to finish.
- `-Impersonation` <ImpersonationLevel> — Specifies the impersonation level to use.
- `-Authentication` <AuthenticationLevel> — Specifies the authentication level to use for the WMI connection.
- `-Locale` <String> — Specifies the preferred locale for WMI objects. The Locale parameter is specified as an array in the MS_\<LCID\> format in the preferred order.
- `-EnableAllPrivileges` switch — Indicates that this cmdlet enables all the permissions of the current user before the command it makes the WMI call.
- `-Authority` <String> — Specifies the authority to use to authenticate the WMI connection. You can specify standard NTLM or Kerberos authentication.
- `-Credential` <PSCredential> — Specifies a user account that has permission to perform this action. The default is the current user.
- `-ThrottleLimit` <Int32> — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-ComputerName` <String[]> · alias -Cn — Specifies the name of the computer on which this cmdlet runs. The default is the local computer.
- `-Namespace` <String> · alias -NS — Specifies the WMI repository namespace where the referenced WMI class is located when it is used with the Class parameter.

**Output:** None, System.Management.Automation.RemotingJob

**Example**

```powershell
Get-WmiObject Win32_Process -Filter "Name='notepad.exe'" | Remove-WmiObject
```

### Rename-Computer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Renames the local or a remote computer (takes effect after a restart).

**Syntax** (one line per parameter set)

```powershell
Rename-Computer [-NewName] <string> [-ComputerName <string>] [-PassThru] [-DomainCredential <pscredential>] [-LocalCredential <pscredential>] [-Force] [-Restart] [-WsmanAuthentication <string>] [-Protocol <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String> · pipeline by property name · default Local Computer — Renames the specified remote computer. The default is the local computer.
- `-PassThru` switch — Returns the results of the command. Otherwise, this cmdlet does not generate any output.
- `-DomainCredential` <PSCredential> — Specifies a user account that has permission to connect to the domain.
- `-LocalCredential` <PSCredential> · default Current User — Specifies a user account that has permission to connect to the computer specified by the ComputerName parameter.
- `-NewName` <String> · **required** · position 0 · pipeline by property name — Specifies a new name for the computer. This parameter is required.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-Restart` switch — Indicates that this cmdlet restarts the computer that was renamed. A restart is often required to make the change effective.
- `-WsmanAuthentication` <String> · values: Default, Basic, Negotiate, CredSSP, Digest, Kerberos — Specifies the mechanism that is used to authenticate the user credentials when this cmdlet uses the WSMan protocol.
- `-Protocol` <String> · values: DCOM, WSMan — Specifies which protocol to use to rename the computer. The acceptable values for this parameter are: WSMan and DCOM.

**Output:** None; RenameComputerChangeInfo

**Example**

```powershell
Rename-Computer -NewName WS-042 -Restart
```

### Rename-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: ren, rni

Renames a file, folder, registry key or other item in place. Aliases: ren, rni.

**Syntax** (one line per parameter set)

```powershell
Rename-Item [-Path] <string> [-NewName] <string> [-Force] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Rename-Item [-NewName] <string> -LiteralPath <string> [-Force] [-PassThru] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the path of the item to rename.
- `-LiteralPath` <String> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it is typed.
- `-NewName` <String> · **required** · position 1 · pipeline by property name — Specifies the new name of the item. Enter only a name, not a path and name.
- `-Force` switch — Forces the cmdlet to rename items that can't otherwise be changed, such as hidden or read-only files or read-only aliases or variables.
- `-PassThru` switch — Returns an object that represents the item to the pipeline. By default, this cmdlet does not generate any output.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None; PSObject

**Example**

```powershell
Rename-Item -Path .\draft.txt -NewName final.txt
```

### Rename-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: rnp

Renames a property of an item (for example a registry value). Alias: rnp.

**Syntax** (one line per parameter set)

```powershell
Rename-ItemProperty [-Path] <string> [-Name] <string> [-NewName] <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Rename-ItemProperty [-Name] <string> [-NewName] <string> -LiteralPath <string> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies a path to the item.
- `-LiteralPath` <String> · **required** · pipeline by property name · alias -PSPath — Specifies a path to the item. Unlike the Path parameter, the value of LiteralPath is used exactly as it is typed.
- `-Name` <String> · **required** · position 1 · pipeline by property name · alias -PSProperty — Specifies the current name of the property to rename.
- `-NewName` <String> · **required** · position 2 · pipeline by property name — Specifies the new name for the property.
- `-PassThru` switch — Returns an object that represents the item property. By default, this cmdlet does not generate any output.
- `-Force` switch — Forces the cmdlet to rename a property of an object that cannot otherwise be accessed by the user.
- `-Filter` <String> · wildcards — Specifies a filter in the format or language of the provider. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Specifies only those items upon which the cmdlet acts, excluding all others.
- `-Exclude` <String[]> · wildcards — Specifies items that this cmdlet omits. The value of this parameter qualifies the Path parameter.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — Specifies a user account that has permission to perform this action. The default is the current user.

**Output:** None; PSCustomObject

**Example**

```powershell
Rename-ItemProperty -Path HKCU:\Software\MyApp -Name OldName -NewName NewName
```

### Reset-ComputerMachinePassword (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Resets the computer's machine account password in its domain.

**Syntax** (one line per parameter set)

```powershell
Reset-ComputerMachinePassword [-Server <string>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Server` <String> — Specifies the name of a domain controller to use when this cmdlet sets the computer account password.
- `-Credential` <PSCredential> — Specifies a user account that has permission to perform this action. The default is the current user.

**Output:** None

**Example**

```powershell
Reset-ComputerMachinePassword -Server DC01 -Credential CONTOSO\Admin
```

### Resolve-Path (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: rvpa

Resolves wildcards and relative paths into full paths. Alias: rvpa.

**Syntax** (one line per parameter set)

```powershell
Resolve-Path [-Path] <string[]> [-Relative] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Resolve-Path -LiteralPath <string[]> [-Relative] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the PowerShell path to resolve. This parameter is required.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to be resolved. The value of the LiteralPath parameter is used exactly as typed.
- `-Relative` switch — Indicates that this cmdlet returns a relative path.
- `-Credential` <PSCredential> · pipeline by property name — Specifies a user account that has permission to perform this action. The default is the current user.

**Output:** PathInfo; String

**Example**

```powershell
Resolve-Path .\*.csv
```

### Restart-Computer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Restarts the local or remote computers, optionally waiting until they come back.

**Syntax** (one line per parameter set)

```powershell
Restart-Computer [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-DcomAuthentication <AuthenticationLevel>] [-Impersonation <ImpersonationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-Force] [-Wait] [-Timeout <int>] [-For <WaitForServiceTypes>] [-Delay <int16>] [-WhatIf] [-Confirm] [<CommonParameters>]

Restart-Computer [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-Impersonation <ImpersonationLevel>] [-Force] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-AsJob` switch — Indicates that Restart-Computer runs as a background job.
- `-DcomAuthentication` <AuthenticationLevel> · alias -Authentication — Specifies the authentication level that is used for the WMI connection.
- `-Impersonation` <ImpersonationLevel> — Specifies the impersonation level that this cmdlet uses to call WMI. Restart-Computer uses WMI.
- `-WsmanAuthentication` <String> · values: Default, Basic, Negotiate, CredSSP, Digest, Kerberos — Specifies the mechanism that is used to authenticate the user credentials.
- `-Protocol` <String> · values: DCOM, WSMan — Specifies which protocol to use to restart the computers. The valid values are WSMan and DCOM.
- `-ComputerName` <String[]> · position 0 · pipeline by value and by property name · alias -CN, -__SERVER, -Server, -IPAddress — Specifies one computer name or a comma-separated array of computer names.
- `-Credential` <PSCredential> · position 1 · default Current user — Specifies a user account that has permission to do this action. The default is the current user.
- `-Force` switch · alias -f — Forces an immediate restart of the computer.
- `-ThrottleLimit` <Int32> — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-Wait` switch — Restart-Computer suppresses the PowerShell prompt and blocks the pipeline until the computers have restarted.
- `-Timeout` <Int32> · alias -TimeoutSec — Specifies the duration of the wait, in seconds. When the timeout elapses, Restart-Computer returns to the command prompt, even if the computers aren't restarted.
- `-For` <WaitForServiceTypes> — Specifies the behavior of PowerShell as it waits for the specified service or feature to become available after the computer restarts.
- `-Delay` <Int16> — Specifies the frequency of queries, in seconds. PowerShell queries the service specified by the For parameter to determine whether the service is available after the computer is restarted.

**Output:** None; RemotingJob

**Example**

```powershell
Restart-Computer -ComputerName Server01 -Force -Wait -For PowerShell
```

### Restart-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Stops and then starts one or more services.

**Syntax** (one line per parameter set)

```powershell
Restart-Service [-InputObject] <ServiceController[]> [-Force] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Restart-Service [-Name] <string[]> [-Force] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Restart-Service -DisplayName <string[]> [-Force] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name · alias -ServiceName · wildcards — Specifies the service names of the services to restart.
- `-InputObject` <ServiceController[]> · **required** · position 0 · pipeline by value — Specifies ServiceController objects that represent the services to restart.
- `-PassThru` switch — Returns an object that represents the service. By default, this cmdlet does not generate any output.
- `-DisplayName` <String[]> · **required** · wildcards — Specifies the display names of the services to restarted. Wildcard characters are permitted.
- `-Include` <String[]> · wildcards — Specifies services that this cmdlet restarts. The value of this parameter qualifies the Name parameter.
- `-Exclude` <String[]> · wildcards — Specifies services that this cmdlet omits. The value of this parameter qualifies the Name parameter.

**Output:** None; ServiceController

**Example**

```powershell
Restart-Service -Name Spooler -Force
```

### Restore-Computer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Starts a System Restore to a restore point on the local computer (restarts it).

**Syntax** (one line per parameter set)

```powershell
Restore-Computer [-RestorePoint] <int> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-RestorePoint` <Int32> · **required** · position 0 · alias -SequenceNumber, -SN, -RP — Specifies the sequence number of the restore point. To find the sequence number, use the Get-ComputerRestorePoint cmdlet.

**Output:** None

**Example**

```powershell
Restore-Computer -RestorePoint 253
```

### Resume-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Resumes paused services.

**Syntax** (one line per parameter set)

```powershell
Resume-Service [-InputObject] <ServiceController[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Service [-Name] <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Resume-Service -DisplayName <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name · alias -ServiceName — Specifies the service names of the services to be resumed.
- `-InputObject` <ServiceController[]> · **required** · position 0 · pipeline by value — Specifies ServiceController objects that represent the services to resumed.
- `-PassThru` switch — Returns an object that represents the service. By default, this cmdlet does not generate any output.
- `-DisplayName` <String[]> · **required** · wildcards — Specifies the display names of the services to be resumed. Wildcard characters are permitted.
- `-Include` <String[]> · wildcards — Specifies services to resume. The value of this parameter qualifies Name parameter.
- `-Exclude` <String[]> · wildcards — Specifies services that this cmdlet omits. The value of this parameter qualifies the Name parameter.

**Output:** None; ServiceController

**Example**

```powershell
Resume-Service -Name Spooler
```

### Set-Clipboard (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: scb

Puts text or file paths on the Windows clipboard, replacing or appending. Alias: scb.

**Syntax** (one line per parameter set)

```powershell
Set-Clipboard [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Clipboard [-Value] <string[]> [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Clipboard -Path <string[]> [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Clipboard -LiteralPath <string[]> [-Append] [-AsHtml] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Value` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies, as a string array, the content to copy to the clipboard.
- `-Append` switch — Indicates that the cmdlet should add to the clipboard instead of replacing it.
- `-Path` <String[]> · **required** · pipeline by property name · wildcards — Specifies the path to the item that's copied to the clipboard. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the item that's copied to the clipboard. Unlike Path, the value of LiteralPath is used exactly as it's written.
- `-AsHtml` switch — Indicates that the cmdlet renders the content as HTML to the clipboard.

**Output:** None

**Example**

```powershell
Get-Content .\notes.txt | Set-Clipboard
```

### Set-Content (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: sc

Writes content to a file, replacing what was there (creates the file if missing). Alias: sc (5.1 only).

**Syntax** (one line per parameter set)

```powershell
Set-Content [-Path] <string[]> [-Value] <Object[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]

Set-Content [-Value] <Object[]> -LiteralPath <string[]> [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [-NoNewline] [-Encoding <FileSystemCmdletProviderEncoding>] [-Stream <string>] [<CommonParameters>]
```

**Arguments**

- `-Value` <Object[]> · **required** · position 1 · pipeline by value and by property name — Specifies the new content for the item.
- `-PassThru` switch — Returns an object that represents the content. By default, this cmdlet does not generate any output.
- `-Path` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the path of the item that receives the content. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it is typed.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Force` switch — Forces the cmdlet to set the contents of a file, even if the file is read-only.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-NoNewline` switch — This is a dynamic parameter made available by the FileSystem provider.
- `-Encoding` <FileSystemCmdletProviderEncoding> · default Default — This is a dynamic parameter made available by the FileSystem provider.
- `-Stream` <String> — This is a dynamic parameter made available by the FileSystem provider.

**Output:** None; String

**Example**

```powershell
Set-Content -Path .\config.txt -Value 'mode=fast' -Encoding UTF8
```

### Set-Item (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: si

Changes the value of an item (variable, alias, environment variable, registry key default). Alias: si.

**Syntax** (one line per parameter set)

```powershell
Set-Item [-Path] <string[]> [[-Value] <Object>] [-Force] [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-Item [[-Value] <Object>] -LiteralPath <string[]> [-Force] [-PassThru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies a path of the location of the items. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it is typed.
- `-Value` <Object> · position 1 · pipeline by value and by property name — Specifies a new value for the item.
- `-Force` switch — Forces the cmdlet to set items that cannot otherwise be changed, such as read-only alias or variables.
- `-PassThru` switch — Passes an object that represents the item to the pipeline. By default, this cmdlet does not generate any output.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None; Object

**Example**

```powershell
Set-Item -Path Env:\APP_MODE -Value Production
```

### Set-ItemProperty (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: sp

Creates or changes the value of an item's property, often a registry value or file attribute. Alias: sp.

**Syntax** (one line per parameter set)

```powershell
Set-ItemProperty [-Path] <string[]> [-Name] <string> [-Value] <Object> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-ItemProperty [-Path] <string[]> -InputObject <psobject> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-ItemProperty [-Name] <string> [-Value] <Object> -LiteralPath <string[]> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-ItemProperty -LiteralPath <string[]> -InputObject <psobject> [-PassThru] [-Force] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the path of the items with the property to modify. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to one or more locations. The value of LiteralPath is used exactly as it is typed.
- `-Name` <String> · **required** · position 1 · pipeline by property name · alias -PSProperty — Specifies the name of the property.
- `-Value` <Object> · **required** · position 2 · pipeline by property name — Specifies the value of the property.
- `-InputObject` <PSObject> · **required** · pipeline by value and by property name — Specifies the object that has the properties that this cmdlet changes.
- `-PassThru` switch — Returns an object that represents the item property. By default, this cmdlet does not generate any output.
- `-Force` switch — Forces the cmdlet to set a property on items that cannot otherwise be accessed by the user.
- `-Filter` <String> · wildcards — Specifies a filter to qualify the Path parameter. The FileSystem provider is the only installed PowerShell provider that supports the use of filters.
- `-Include` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies, as a string array, an item or items that this cmdlet excludes in the operation.
- `-Credential` <PSCredential> · pipeline by property name · default Current user — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** None; PSCustomObject

**Example**

```powershell
Set-ItemProperty -Path HKCU:\Software\MyApp -Name Enabled -Value 0
```

### Set-Location (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: cd, chdir, sl

Changes the current working location (folder, registry key, drive). Aliases: cd, chdir, sl.

**Syntax** (one line per parameter set)

```powershell
Set-Location [[-Path] <string>] [-PassThru] [-UseTransaction] [<CommonParameters>]

Set-Location -LiteralPath <string> [-PassThru] [-UseTransaction] [<CommonParameters>]

Set-Location [-PassThru] [-StackName <string>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · position 0 · pipeline by value and by property name · wildcards — Specify the path of a new working location. If no path is provided, Set-Location defaults to the current user's home directory.
- `-LiteralPath` <String> · **required** · pipeline by property name · alias -PSPath — Specifies a path of the location. The value of the LiteralPath parameter is used exactly as it is typed.
- `-PassThru` switch — Returns a PathInfo object that represents the location. By default, this cmdlet does not generate any output.
- `-StackName` <String> · pipeline by property name — Specifies an existing location stack name that this cmdlet makes the current location stack.

**Output:** None; PathInfo; PathInfoStack

**Example**

```powershell
Set-Location -Path HKLM:\SOFTWARE
```

### Set-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Changes a service's properties (startup type, display name, description) or starts/stops/pauses it.

**Syntax** (one line per parameter set)

```powershell
Set-Service [-Name] <string> [-ComputerName <string[]>] [-DisplayName <string>] [-Description <string>] [-StartupType <ServiceStartMode>] [-Status <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Service [-ComputerName <string[]>] [-DisplayName <string>] [-Description <string>] [-StartupType <ServiceStartMode>] [-Status <string>] [-InputObject <ServiceController>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · pipeline by property name · alias -cn · default Local computer — Specifies one or more computers. For remote computers, type the NetBIOS name, an IP address, or a fully qualified domain name.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name · alias -ServiceName, -SN — Specifies the service name of the service to be changed. Wildcard characters aren't permitted.
- `-DisplayName` <String> · alias -DN — Specifies a new display name for the service.
- `-Description` <String> — Specifies a new description for the service.
- `-StartupType` <ServiceStartMode> · alias -StartMode, -SM, -ST · default Automatic — Sets the startup type of the service.
- `-Status` <String> · values: Running, Stopped, Paused — Specifies the status for the service.
- `-InputObject` <ServiceController> · pipeline by value — Specifies a ServiceController object that represents the service to change.
- `-PassThru` switch — Returns a ServiceController object that represents the services that were changed.

**Output:** None; ServiceController

**Example**

```powershell
Set-Service -Name Spooler -StartupType Manual
```

### Set-TimeZone (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: stz

Sets the system time zone. Alias: stz.

**Syntax** (one line per parameter set)

```powershell
Set-TimeZone [-Name] <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-TimeZone -Id <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-TimeZone [-InputObject] <TimeZoneInfo> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Id` <String> · **required** · pipeline by property name — Specifies the ID of the time zone that this cmdlet sets.
- `-InputObject` <TimeZoneInfo> · **required** · position 0 · pipeline by value — Specifies a TimeZoneInfo object to use as input.
- `-Name` <String> · **required** · position 0 — Specifies the name of the time zone that this cmdlet sets. A full list of Time Zone names can be obtained by running the following command: Get-TimeZone -ListAvailable.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None; TimeZoneInfo

**Example**

```powershell
Set-TimeZone -Id 'Eastern Standard Time'
```

### Set-WmiInstance (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: swmi

(Windows PowerShell only) Creates or updates a WMI instance; use Set-CimInstance in PowerShell 7.

**Syntax** (one line per parameter set)

```powershell
Set-WmiInstance [-Class] <string> [[-Arguments] <hashtable>] [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance -InputObject <wmi> [-Arguments <hashtable>] [-PutType <PutType>] [-AsJob] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance -Path <string> [-Arguments <hashtable>] [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-WmiInstance [-PutType <PutType>] [-AsJob] [-Impersonation <ImpersonationLevel>] [-Authentication <AuthenticationLevel>] [-Locale <string>] [-EnableAllPrivileges] [-Authority <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-ComputerName <string[]>] [-Namespace <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ManagementObject> · **required** · pipeline by value — Specifies a ManagementObject object to use as input. When this parameter is used, all other parameters ,except the Arguments parameter, are ignored.
- `-Path` <String> · **required** — Specifies a WMI object path of the instance that you want to create or update.
- `-Class` <String> · **required** · position 0 — Specifies the name of a WMI class.
- `-Arguments` <Hashtable> · position 2 · alias -Args, -Property — Specifies the name of the property to be changed and the new value for that property.
- `-PutType` <PutType> — Indicates whether to create or update the WMI instance.
- `-AsJob` switch — Indicates that this cmdlet runs as a background job. Use this parameter to run commands that take a long time to finish.
- `-Impersonation` <ImpersonationLevel> — Specifies the impersonation level to use.
- `-Authentication` <AuthenticationLevel> — Specifies the authentication level that must be used with the WMI connection.
- `-Locale` <String> — Specifies the preferred locale for WMI objects. The Locale parameter is specified in an array in the MS_<LCID> format in the preferred order.
- `-EnableAllPrivileges` switch — Indicates that this cmdlet enables all the permissions of the current user before the command it makes the WMI call.
- `-Authority` <String> — Specifies the authority to use to authenticate the WMI connection. You can specify standard NTLM or Kerberos authentication.
- `-Credential` <PSCredential> — Specifies a user account that has permission to perform this action. The default is the current user.
- `-ThrottleLimit` <Int32> — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-ComputerName` <String[]> · alias -Cn — Specifies the name of the computer on which this cmdlet runs. The default is the local computer.
- `-Namespace` <String> · alias -NS — Specifies the WMI repository namespace where the referenced WMI class is located when it's used with the Class parameter.

**Output:** None

**Example**

```powershell
Set-WmiInstance -Class Win32_Environment -Arguments @{ Name = 'MYVAR'; VariableValue = '1'; UserName = '<SYSTEM>' }
```

### Show-ControlPanelItem (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Opens Control Panel items by name or canonical name.

**Syntax** (one line per parameter set)

```powershell
Show-ControlPanelItem [-Name] <string[]> [<CommonParameters>]

Show-ControlPanelItem -CanonicalName <string[]> [<CommonParameters>]

Show-ControlPanelItem [[-InputObject] <ControlPanelItem[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies names of control panel items. Wildcard characters are permitted.
- `-CanonicalName` <String[]> · **required** · wildcards — Specifies control panel items by using the specified canonical names or name patterns.
- `-InputObject` <ControlPanelItem[]> · position 0 · pipeline by value — Specifies control panel items to open by submitting control panel item objects.

**Output:** None

**Example**

```powershell
Show-ControlPanelItem -Name 'Programs and Features'
```

### Show-EventLog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Opens Event Viewer showing the event logs of the local or a remote computer.

**Syntax** (one line per parameter set)

```powershell
Show-EventLog [[-ComputerName] <string>] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String> · position 0 · alias -CN — Specifies a remote computer. Show-EventLog displays the event logs from the specified computer in Event Viewer on the local computer.

**Output:** None

**Example**

```powershell
Show-EventLog -ComputerName Server01
```

### Split-Path (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Returns part of a path: the parent folder, leaf name, extension-less leaf, qualifier, or whether it is absolute.

**Syntax** (one line per parameter set)

```powershell
Split-Path [-Path] <string[]> [-Parent] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-Leaf] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-Qualifier] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-NoQualifier] [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path [-Path] <string[]> [-Resolve] [-IsAbsolute] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]

Split-Path -LiteralPath <string[]> [-Resolve] [-Credential <pscredential>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the paths to be split. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the paths to be split. Unlike Path, the value of LiteralPath is used exactly as it is typed.
- `-Qualifier` switch · position 1 · pipeline by property name — Indicates that this cmdlet returns only the qualifier of the specified path.
- `-NoQualifier` switch · pipeline by property name — Indicates that this cmdlet returns the path without the qualifier. For the FileSystem or registry providers, the qualifier is the drive of the provider path, such as C: or HKCU:.
- `-Parent` switch · pipeline by property name — Split-Path returns only the parent container portion of the path string supplied.
- `-Leaf` switch · pipeline by property name — When using the Leaf parameter, Split-Path returns only the last item in the path string supplied, regardless whether that item is a file or a directory.
- `-Resolve` switch — Indicates that this cmdlet displays the items that are referenced by the resulting split path instead of displaying the path elements.
- `-IsAbsolute` switch — Indicates that this cmdlet returns $true if the path is absolute and $false if it's relative.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Output:** String; Boolean

**Example**

```powershell
Split-Path -Path C:\Data\report.csv -Leaf
```

### Start-Process (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: saps, start

Starts a program or opens a file in a new process, with arguments, working folder, credentials or elevation. Aliases: start, saps.

**Syntax** (one line per parameter set)

```powershell
Start-Process [-FilePath] <string> [[-ArgumentList] <string[]>] [-Credential <pscredential>] [-WorkingDirectory <string>] [-LoadUserProfile] [-NoNewWindow] [-PassThru] [-RedirectStandardError <string>] [-RedirectStandardInput <string>] [-RedirectStandardOutput <string>] [-WindowStyle <ProcessWindowStyle>] [-Wait] [-UseNewEnvironment] [<CommonParameters>]

Start-Process [-FilePath] <string> [[-ArgumentList] <string[]>] [-WorkingDirectory <string>] [-PassThru] [-Verb <string>] [-WindowStyle <ProcessWindowStyle>] [-Wait] [<CommonParameters>]
```

**Arguments**

- `-FilePath` <String> · **required** · position 0 · alias -PSPath — Specifies the optional path and filename of the program that runs in the process.
- `-ArgumentList` <String[]> · position 1 · alias -Args — Specifies parameters or parameter values to use when this cmdlet starts the process.
- `-Credential` <PSCredential> · alias -RunAs · default Current user — Specifies a user account that has permission to perform this action. By default, the cmdlet uses the credentials of the current user.
- `-WorkingDirectory` <String> — Specifies the location that the new process should start in. The default is the location of the executable file or document being started.
- `-LoadUserProfile` switch · alias -Lup — Indicates that this cmdlet loads the Windows user profile stored in the HKEY_USERS registry key for the current user.
- `-NoNewWindow` switch · alias -nnw — Start the new process in the current console window. By default on Windows, PowerShell opens a new window.
- `-PassThru` switch — Returns a process object for each process that the cmdlet started. By default, this cmdlet doesn't generate any output.
- `-RedirectStandardError` <String> · alias -RSE — Specifies a file. This cmdlet sends any errors generated by the process to a file that you specify.
- `-RedirectStandardInput` <String> · alias -RSI — Specifies a file. This cmdlet reads input from the specified file.
- `-RedirectStandardOutput` <String> · alias -RSO — Specifies a file. This cmdlet sends the output generated by the process to a file that you specify.
- `-Verb` <String> — Specifies a verb to use when this cmdlet starts the process. The verbs that are available are determined by the filename extension of the file that runs in the process.
- `-WindowStyle` <ProcessWindowStyle> — Specifies the state of the window that's used for the new process. The default value is Normal.
- `-Wait` switch — Indicates that this cmdlet waits for the specified process and its descendants to complete before accepting more input.
- `-UseNewEnvironment` switch — Indicates that this cmdlet uses new environment variables specified for the process.

**Output:** None; Process

**Example**

```powershell
Start-Process -FilePath notepad.exe -ArgumentList .\notes.txt -Wait
```

### Start-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: sasv

Starts stopped services. Alias: sasv.

**Syntax** (one line per parameter set)

```powershell
Start-Service [-InputObject] <ServiceController[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Service [-Name] <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Service -DisplayName <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name · alias -ServiceName — Specifies the service names for the service to be started.
- `-InputObject` <ServiceController[]> · **required** · position 0 · pipeline by value — Specifies ServiceController objects representing the services to be started.
- `-PassThru` switch — Returns an object that represents the service. By default, this cmdlet does not generate any output.
- `-DisplayName` <String[]> · **required** · wildcards — Specifies the display names of the services to start. Wildcard characters are permitted.
- `-Include` <String[]> · wildcards — Specifies services that this cmdlet starts. The value of this parameter qualifies the Name parameter.
- `-Exclude` <String[]> · wildcards — Specifies services that this cmdlet omits. The value of this parameter qualifies the Name parameter.

**Output:** None; ServiceController

**Example**

```powershell
Start-Service -Name Spooler
```

### Start-Transaction (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Starts a transaction so supported provider operations (registry) can be committed or rolled back together.

**Syntax** (one line per parameter set)

```powershell
Start-Transaction [-Timeout <int>] [-Independent] [-RollbackPreference <RollbackSeverity>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Timeout` <Int32> · alias -TimeoutMins — Specifies the maximum time, in minutes, that the transaction is active.
- `-Independent` switch — Indicates that this cmdlet starts a transaction that is independent of any transactions in progress.
- `-RollbackPreference` <RollbackSeverity> — Specifies the conditions under which a transaction is automatically rolled back.

**Output:** None

**Example**

```powershell
Start-Transaction; New-Item HKCU:\Software\Test -UseTransaction; Complete-Transaction
```

### Stop-Computer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Shuts down the local or remote computers.

**Syntax** (one line per parameter set)

```powershell
Stop-Computer [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-Impersonation <ImpersonationLevel>] [-ThrottleLimit <int>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-AsJob` switch — Indicates that this cmdlet runs as a background job.
- `-DcomAuthentication` <AuthenticationLevel> · alias -Authentication · default Packet — Specifies the authentication level that this cmdlet uses with WMI. Stop-Computer uses WMI.
- `-WsmanAuthentication` <String> · values: Default, Basic, Negotiate, CredSSP, Digest, Kerberos · default Default — Specifies the mechanism that is used to authenticate the user credentials when this cmdlet uses the WSMan protocol.
- `-Protocol` <String> · values: DCOM, WSMan · default DCOM — Specifies which protocol to use to restart the computers. The acceptable values for this parameter are: WSMan and DCOM.
- `-ComputerName` <String[]> · position 0 · pipeline by property name · alias -CN, -__SERVER, -Server, -IPAddress — Specifies the computers to stop. The default is the local computer.
- `-Credential` <PSCredential> · position 1 · default Current user — Specifies a user account that has permission to do this action. The default is the current user.
- `-Impersonation` <ImpersonationLevel> · default Impersonate — Specifies the impersonation level to use when this cmdlet calls WMI. The default value is Impersonate.
- `-ThrottleLimit` <Int32> — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-Force` switch — Forces an immediate shut down of the computer.

**Output:** None; RemotingJob

**Example**

```powershell
Stop-Computer -ComputerName Server01 -Force
```

### Stop-Process (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: kill, spps

Stops running processes by name, ID or process object. Aliases: kill, spps.

**Syntax** (one line per parameter set)

```powershell
Stop-Process [-Id] <int[]> [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Process -Name <string[]> [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Process [-InputObject] <Process[]> [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · pipeline by property name · alias -ProcessName · wildcards — Specifies the process names of the processes to stop. You can type multiple process names, separated by commas, or use wildcard characters.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies the process IDs of the processes to stop. To specify multiple IDs, use commas to separate the IDs.
- `-InputObject` <Process[]> · **required** · position 0 · pipeline by value — Specifies the process objects to stop. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-PassThru` switch — Returns an object that represents the process. By default, this cmdlet does not generate any output.
- `-Force` switch — Stops the specified processes without prompting for confirmation. By default, Stop-Process prompts for confirmation before stopping any process that is not owned by the current user.

**Output:** None; Process

**Example**

```powershell
Stop-Process -Name notepad -Force
```

### Stop-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management · aliases: spsv

Stops running services (and with -Force, services that depend on them). Alias: spsv.

**Syntax** (one line per parameter set)

```powershell
Stop-Service [-InputObject] <ServiceController[]> [-Force] [-NoWait] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Service [-Name] <string[]> [-Force] [-NoWait] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Stop-Service -DisplayName <string[]> [-Force] [-NoWait] [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Force` switch — Forces the cmdlet to stop a service even if that service has dependent services.
- `-NoWait` switch — Indicates that this cmdlet uses the no wait option.
- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name · alias -ServiceName · wildcards — Specifies the service names of the services to stop. Wildcard characters are permitted.
- `-InputObject` <ServiceController[]> · **required** · position 0 · pipeline by value — Specifies ServiceController objects that represent the services to stop.
- `-PassThru` switch — Returns an object that represents the service. By default, this cmdlet does not generate any output.
- `-DisplayName` <String[]> · **required** · wildcards — Specifies the display names of the services to stop. Wildcard characters are permitted.
- `-Include` <String[]> · wildcards — Specifies services that this cmdlet stops. The value of this parameter qualifies the Name parameter.
- `-Exclude` <String[]> · wildcards — Specifies services that this cmdlet omits. The value of this parameter qualifies the Name parameter.

**Output:** None; ServiceController

**Example**

```powershell
Stop-Service -Name Spooler -Force
```

### Suspend-Service (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Pauses running services that support pausing.

**Syntax** (one line per parameter set)

```powershell
Suspend-Service [-InputObject] <ServiceController[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Service [-Name] <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Suspend-Service -DisplayName <string[]> [-PassThru] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name · alias -ServiceName · wildcards — Specifies the service names of the services to suspend. Wildcard characters are permitted.
- `-InputObject` <ServiceController[]> · **required** · position 0 · pipeline by value — Specifies ServiceController objects that represent the services to suspend.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-DisplayName` <String[]> · **required** · wildcards — Specifies the display names of the services to be suspended. Wildcard characters are permitted.
- `-Include` <String[]> · wildcards — Specifies services to suspend. The value of this parameter qualifies the Name parameter.
- `-Exclude` <String[]> · wildcards — Specifies services to omit from the specified services. The value of this parameter qualifies the Name parameter.

**Output:** None; ServiceController

**Example**

```powershell
Suspend-Service -Name Spooler
```

### Test-ComputerSecureChannel (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Checks (and with -Repair fixes) the secure channel between the computer and its domain.

**Syntax** (one line per parameter set)

```powershell
Test-ComputerSecureChannel [-Repair] [-Server <string>] [-Credential <pscredential>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Repair` switch — Indicates that this cmdlet removes and then rebuilds the channel established by the NetLogon service.
- `-Server` <String> — Specifies the domain controller to run the command. If this parameter is not specified, this cmdlet selects a default domain controller for the operation.
- `-Credential` <PSCredential> — Specifies a user account that has permission to perform this action. Type a user name, such as User01 or Domain01\User01, or enter a PSCredential object, such as one that the Get-Credential cmdlet returns.

**Output:** Boolean

**Example**

```powershell
Test-ComputerSecureChannel -Repair -Credential CONTOSO\Admin
```

### Test-Connection (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Sends ping (ICMP echo) requests to computers; in 7.x also -TcpPort and -Traceroute.

**Syntax** (one line per parameter set)

```powershell
Test-Connection [-ComputerName] <string[]> [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-BufferSize <int>] [-Count <int>] [-Impersonation <ImpersonationLevel>] [-ThrottleLimit <int>] [-TimeToLive <int>] [-Delay <int>] [<CommonParameters>]

Test-Connection [-ComputerName] <string[]> [-Source] <string[]> [-AsJob] [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-BufferSize <int>] [-Count <int>] [-Credential <pscredential>] [-Impersonation <ImpersonationLevel>] [-ThrottleLimit <int>] [-TimeToLive <int>] [-Delay <int>] [<CommonParameters>]

Test-Connection [-ComputerName] <string[]> [-DcomAuthentication <AuthenticationLevel>] [-WsmanAuthentication <string>] [-Protocol <string>] [-BufferSize <int>] [-Count <int>] [-Impersonation <ImpersonationLevel>] [-TimeToLive <int>] [-Delay <int>] [-Quiet] [<CommonParameters>]
```

**Arguments**

- `-AsJob` switch — Indicates that this cmdlet runs as a background job. When you specify the AsJob parameter, the command immediately returns an object that represents the background job.
- `-DcomAuthentication` <AuthenticationLevel> · alias -Authentication · default Packet (enumerated value of 4) — Specifies the authentication level that this cmdlet uses with WMI. Test-Connection uses WMI.
- `-WsmanAuthentication` <String> · values: Default, Basic, Negotiate, CredSSP, Digest, Kerberos · default Default — Specifies the mechanism that is used to authenticate the user credentials when this cmdlet uses the WSMan protocol.
- `-Protocol` <String> · values: DCOM, WSMan — Specifies a protocol. The acceptable values for this parameter are DCOM and WSMan.
- `-BufferSize` <Int32> · alias -Size, -Bytes, -BS · default 32 — Specifies the size, in bytes, of the buffer sent with this command. The default value is 32.
- `-ComputerName` <String[]> · **required** · position 0 · pipeline by property name · alias -CN, -IPAddress, -__SERVER, -Server, -Destination — Specifies the computers to ping. Type the computer names or type IP addresses in IPv4 or IPv6 format.
- `-Count` <Int32> · default 4 — Specifies the number of echo requests to send. The default value is 4.
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to send a ping request from the source computer.
- `-Source` <String[]> · **required** · position 1 · alias -FCN, -SRC · default Local computer — Specifies the names of the computers where the ping originates. Enter a comma-separated list of computer names.
- `-Impersonation` <ImpersonationLevel> · default Impersonate — Specifies the impersonation level to use when this cmdlet calls WMI. Test-Connection uses WMI.
- `-ThrottleLimit` <Int32> · default 32 — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-TimeToLive` <Int32> · alias -TTL · default 128 in Windows — Specifies the maximum times a packet can be forwarded. For every hop in gateways, routers etc.
- `-Delay` <Int32> · default 1 (second) — Specifies the interval between pings, in seconds.
- `-Quiet` switch — The Quiet parameter returns a Boolean value in a System.Boolean object.

**Output:** ManagementObject#root\cimv2\Win32_PingStatus; RemotingJob; Boolean

**Example**

```powershell
Test-Connection -ComputerName Server01 -Count 2
```

### Test-Path (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Returns True if a path exists (optionally only files with -PathType Leaf or folders with Container).

**Syntax** (one line per parameter set)

```powershell
Test-Path [-Path] <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PathType <TestPathType>] [-IsValid] [-Credential <pscredential>] [-UseTransaction] [-OlderThan <datetime>] [-NewerThan <datetime>] [<CommonParameters>]

Test-Path -LiteralPath <string[]> [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-PathType <TestPathType>] [-IsValid] [-Credential <pscredential>] [-UseTransaction] [-OlderThan <datetime>] [-NewerThan <datetime>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies a path to be tested. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies a path to be tested. Unlike Path, the value of the LiteralPath parameter is used exactly as it's typed.
- `-Filter` <String> · wildcards — Specifies a filter in the format or language of the provider. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Specifies paths that this cmdlet tests. The value of this parameter qualifies the Path parameter.
- `-Exclude` <String[]> · wildcards — Specifies items that this cmdlet omits. The value of this parameter qualifies the Path parameter.
- `-PathType` <TestPathType> · alias -Type — Specifies the type of the final element in the path. This cmdlet returns $true if the element is of the specified type and $false if it's not.
- `-IsValid` switch — Indicates that this cmdlet tests the syntax of the path, regardless of whether the elements of the path exist.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-OlderThan` <Nullable`1> — This is a dynamic parameter made available by the FileSystem provider.
- `-NewerThan` <Nullable`1> — This is a dynamic parameter made available by the FileSystem provider.

**Output:** Boolean

**Example**

```powershell
if (Test-Path -Path .\config.json -PathType Leaf) { 'found' }
```

### Undo-Transaction (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Rolls back the active transaction.

**Syntax** (one line per parameter set)

```powershell
Undo-Transaction [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Output:** None

**Example**

```powershell
Undo-Transaction
```

### Use-Transaction (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Runs a script block as part of the active transaction.

**Syntax** (one line per parameter set)

```powershell
Use-Transaction [-TransactedScript] <scriptblock> [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-TransactedScript` <ScriptBlock> · **required** · position 0 — Specifies the script block that is run in the transaction. Enter any valid script block enclosed in braces ({}).

**Output:** PSObject

**Example**

```powershell
Use-Transaction -TransactedScript { $sb.Append('x') } -UseTransaction
```

### Wait-Process (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Waits until the given processes have exited (optionally with a timeout).

**Syntax** (one line per parameter set)

```powershell
Wait-Process [-Name] <string[]> [[-Timeout] <int>] [<CommonParameters>]

Wait-Process [-Id] <int[]> [[-Timeout] <int>] [<CommonParameters>]

Wait-Process [[-Timeout] <int>] -InputObject <Process[]> [<CommonParameters>]
```

**Arguments**

- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name · alias -PID, -ProcessId — Specifies the process IDs of the processes. To specify multiple IDs, use commas to separate the IDs.
- `-Name` <String[]> · **required** · position 0 · pipeline by property name · alias -ProcessName — Specifies the process names of the processes. To specify multiple names, use commas to separate the names.
- `-Timeout` <Int32> · position 1 · alias -TimeoutSec — Specifies the maximum time, in seconds, that this cmdlet waits for the specified processes to stop.
- `-InputObject` <Process[]> · **required** · pipeline by value — Specifies the processes by submitting process objects. Enter a variable that contains the process objects, or type a command or expression that gets the process objects, such as the Get-Process cmdlet.

**Output:** None

**Example**

```powershell
Start-Process notepad -PassThru | Wait-Process -Timeout 60
```

### Write-EventLog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Management

Writes an entry to a classic event log for a registered source.

**Syntax** (one line per parameter set)

```powershell
Write-EventLog [-LogName] <string> [-Source] <string> [-EventId] <int> [[-EntryType] <EventLogEntryType>] [-Message] <string> [-Category <int16>] [-RawData <byte[]>] [-ComputerName <string>] [<CommonParameters>]
```

**Arguments**

- `-LogName` <String> · **required** · position 0 · alias -LN — Specifies the name of the log to which the event is written. Enter the log name.
- `-Source` <String> · **required** · position 1 · alias -SRC — Specifies the event source, which is typically the name of the application that is writing the event to the log.
- `-EntryType` <EventLogEntryType> · position 3 · alias -ET · values: Error, Information, FailureAudit, SuccessAudit, Warning — Specifies the entry type of the event. The acceptable values for this parameter are: Error, Warning, Information, SuccessAudit, and FailureAudit.
- `-Category` <Int16> — Specifies a task category for the event. Enter an integer that is associated with the strings in the category message file for the event log.
- `-EventId` <Int32> · **required** · position 2 · alias -ID, -EID — Specifies the event identifier. This parameter is required.
- `-Message` <String> · **required** · position 4 · alias -MSG — Specifies the event message. This parameter is required.
- `-RawData` <Byte[]> · alias -RD — Specifies the binary data that is associated with the event, in bytes.
- `-ComputerName` <String> · alias -CN — Specifies a remote computer. The default is the local computer.

**Output:** EventLogEntry

**Example**

```powershell
Write-EventLog -LogName Application -Source MyScript -EventId 100 -EntryType Information -Message 'Done'
```

## Commands: Microsoft.PowerShell.Utility

### Add-Member (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Adds custom properties or methods (NoteProperty, ScriptProperty, ScriptMethod...) to an object.

**Syntax** (one line per parameter set)

```powershell
Add-Member -InputObject <psobject> -TypeName <string> [-PassThru] [<CommonParameters>]

Add-Member [-MemberType] <PSMemberTypes> [-Name] <string> [[-Value] <Object>] [[-SecondValue] <Object>] -InputObject <psobject> [-TypeName <string>] [-Force] [-PassThru] [<CommonParameters>]

Add-Member [-NotePropertyName] <string> [-NotePropertyValue] <Object> -InputObject <psobject> [-TypeName <string>] [-Force] [-PassThru] [<CommonParameters>]

Add-Member [-NotePropertyMembers] <IDictionary> -InputObject <psobject> [-TypeName <string>] [-Force] [-PassThru] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · **required** · pipeline by value — Specifies the object to which the new member is added. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-MemberType` <PSMemberTypes> · **required** · position 0 · alias -Type — Specifies the type of the member to add. This parameter is required.
- `-Name` <String> · **required** · position 1 — Specifies the name of the member that this cmdlet adds.
- `-Value` <Object> · position 2 — Specifies the initial value of the added member. If you add an AliasProperty, CodeProperty, or ScriptProperty member, you can supply additional information using the SecondValue parameter.
- `-SecondValue` <Object> · position 3 — Specifies optional additional information about AliasProperty, ScriptProperty, or CodeProperty members.
- `-TypeName` <String> · **required** — Specifies a name for the type.
- `-Force` switch — By default, Add-Member can't add a new member if the object already has a member with the same.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet doesn't generate any output.
- `-NotePropertyName` <String> · **required** · position 0 — Specifies the note property name.
- `-NotePropertyValue` <Object> · **required** · position 1 — Specifies the note property value.
- `-NotePropertyMembers` <IDictionary> · **required** · position 0 — Specifies a hashtable or ordered dictionary that contains key-value pair representing NoteProperty names and their values.

**Output:** None; Object

**Example**

```powershell
$o = [pscustomobject]@{ Name = 'a' }; $o | Add-Member -NotePropertyName Size -NotePropertyValue 10
```

### Add-Type (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Compiles C# (or VB) source, or loads an assembly, so its .NET types can be used in the session.

**Syntax** (one line per parameter set)

```powershell
Add-Type [-TypeDefinition] <string> [-CodeDomProvider <CodeDomProvider>] [-CompilerParameters <CompilerParameters>] [-Language <Language>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type [-Name] <string> [-MemberDefinition] <string[]> [-CodeDomProvider <CodeDomProvider>] [-CompilerParameters <CompilerParameters>] [-Namespace <string>] [-UsingNamespace <string[]>] [-Language <Language>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type [-Path] <string[]> [-CompilerParameters <CompilerParameters>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type -LiteralPath <string[]> [-CompilerParameters <CompilerParameters>] [-ReferencedAssemblies <string[]>] [-OutputAssembly <string>] [-OutputType <OutputAssemblyType>] [-PassThru] [-IgnoreWarnings] [<CommonParameters>]

Add-Type -AssemblyName <string[]> [-PassThru] [-IgnoreWarnings] [<CommonParameters>]
```

**Arguments**

- `-CodeDomProvider` <CodeDomProvider> · alias -Provider · default C# compiler — Specifies a code generator or compiler. Add-Type uses the specified compiler to compile the source code.
- `-CompilerParameters` <CompilerParameters> · alias -CP — Specifies the options for the source code compiler. These options are sent to the compiler without revision.
- `-TypeDefinition` <String> · **required** · position 0 — Specifies the source code that contains the type definitions. Enter the source code in a string or here-string, or enter a variable that contains the source code.
- `-Name` <String> · **required** · position 0 — Specifies the name of the class to create. This parameter is required when generating a type from a member definition.
- `-MemberDefinition` <String[]> · **required** · position 1 — Specifies new properties or methods for the class. Add-Type generates the template code that's required to support the properties or methods.
- `-Namespace` <String> · alias -NS — By default, this command creates the type in the Microsoft.PowerShell.Commands.AddType.AutoGeneratedTypes namespace.
- `-UsingNamespace` <String[]> · alias -Using · default System namespace — Specifies other namespaces that are required for the class. This is much like the C# keyword, Using.
- `-Path` <String[]> · **required** · position 0 — Specifies the path to source code files or assembly DLL files that contain the types.
- `-LiteralPath` <String[]> · **required** · alias -PSPath — Specifies the path to source code files or assembly DLL files that contain the types.
- `-AssemblyName` <String[]> · **required** · alias -AN · wildcards — Specifies the name of an assembly that includes the types. Add-Type takes the types from the specified assembly.
- `-Language` <Language> · default CSharp — Specifies the language that is used in the source code. The Add-Type cmdlet uses the value of this parameter to select the appropriate CodeDomProvider.
- `-ReferencedAssemblies` <String[]> · alias -RA — Specifies the assemblies upon which the type depends. By default, Add-Type references System.dll and System.Management.Automation.dll.
- `-OutputAssembly` <String> · alias -OA · wildcards — Generates a DLL file for the assembly with the specified name in the location.
- `-OutputType` <OutputAssemblyType> · alias -OT — Specifies the output type of the output assembly. By default, no output type is specified.
- `-PassThru` switch — Returns a System.Runtime object that represents the types that were added.
- `-IgnoreWarnings` switch — Ignores compiler warnings. Use this parameter to prevent Add-Type from handling compiler warnings as errors.

**Output:** None; Type

**Example**

```powershell
Add-Type -TypeDefinition 'public static class Calc { public static int Add(int a, int b) { return a + b; } }'; [Calc]::Add(2, 3)
```

### Clear-Variable (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: clv

Empties the value of a variable but keeps the variable. Alias: clv.

**Syntax** (one line per parameter set)

```powershell
Clear-Variable [-Name] <string[]> [-Include <string[]>] [-Exclude <string[]>] [-Force] [-PassThru] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the name of the variable to be cleared. Wildcards are permitted.
- `-Include` <String[]> · wildcards — Specifies an array of items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies an array of items that this cmdlet omits in the operation. The value of this parameter qualifies the Name parameter.
- `-Force` switch — Allows the cmdlet to clear a variable even if it is read-only. Even using the Force parameter, the cmdlet cannot clear constants.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Scope` <String> — Specifies the scope in which this alias is valid.

**Output:** None; PSVariable

**Example**

```powershell
Clear-Variable -Name result
```

### Compare-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: compare, diff

Compares two sets of objects and shows which items are only in one side (<= or =>) or in both. Aliases: compare, diff (5.1).

**Syntax** (one line per parameter set)

```powershell
Compare-Object [-ReferenceObject] <psobject[]> [-DifferenceObject] <psobject[]> [-SyncWindow <int>] [-Property <Object[]>] [-ExcludeDifferent] [-IncludeEqual] [-PassThru] [-Culture <string>] [-CaseSensitive] [<CommonParameters>]
```

**Arguments**

- `-ReferenceObject` <PSObject[]> · **required** · position 0 — Specifies an array of objects used as a reference for comparison.
- `-DifferenceObject` <PSObject[]> · **required** · position 1 · pipeline by value — Specifies the objects that are compared to the reference objects.
- `-SyncWindow` <Int32> · default [int32]::MaxValue — Specifies the number of adjacent objects that Compare-Object inspects while looking for a match in a collection of objects.
- `-Property` <Object[]> — Specifies an array of properties of the reference and difference objects to compare.
- `-ExcludeDifferent` switch — Indicates that this cmdlet displays only the characteristics of compared objects that are equal.
- `-IncludeEqual` switch — IncludeEqual displays the matches between the reference and difference objects.
- `-PassThru` switch — When you use the PassThru parameter, Compare-Object omits the PSCustomObject wrapper around the compared objects and returns the differing objects, unchanged.
- `-Culture` <String> — Specifies the culture to use for comparisons.
- `-CaseSensitive` switch — Indicates that comparisons should be case-sensitive.

**Output:** None; PSCustomObject

**Example**

```powershell
Compare-Object (Get-Content .\a.txt) (Get-Content .\b.txt)
```

### Convert-String (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

(Windows PowerShell only) Reformats strings by following before/after examples.

**Syntax** (one line per parameter set)

```powershell
Convert-String -InputObject <string> [-Example <List[psobject]>] [<CommonParameters>]
```

**Arguments**

- `-Example` <List`1> · alias -E — Specifies a list of examples of the target format.
- `-InputObject` <String> · **required** · pipeline by value — Specifies a string to format.

**Output:** String

**Example**

```powershell
'Ann Smith' | Convert-String -Example 'John Doe=Doe, J.'
```

### ConvertFrom-Csv (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns CSV text (strings) into objects with one property per column.

**Syntax** (one line per parameter set)

```powershell
ConvertFrom-Csv [-InputObject] <psobject[]> [[-Delimiter] <char>] [-Header <string[]>] [<CommonParameters>]

ConvertFrom-Csv [-InputObject] <psobject[]> -UseCulture [-Header <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Delimiter` <Char> · position 1 · default comma (,) — Specifies the delimiter that separates the property values in the CSV data.
- `-UseCulture` switch · **required** — Uses the list separator for the current culture as the item delimiter.
- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value and by property name — Specifies the CSV strings to be converted to objects. Enter a variable that contains the CSV strings or type a command or expression that gets the CSV strings.
- `-Header` <String[]> — Specifies an alternate column header row for the imported string. The column header determines the property names of the objects created by ConvertFrom-Csv.

**Output:** PSObject

**Example**

```powershell
'Name,Age','Ann,30' | ConvertFrom-Csv
```

### ConvertFrom-Json (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns a JSON string into PowerShell objects (custom objects, or hash tables with -AsHashtable in 7).

**Syntax** (one line per parameter set)

```powershell
ConvertFrom-Json [-InputObject] <string> [<CommonParameters>]
```

**Arguments**

- `-InputObject` <String> · **required** · position 0 · pipeline by value — Specifies the JSON strings to convert to JSON objects. Enter a variable that contains the string, or type a command or expression that gets the string.

**Output:** PSCustomObject

**Example**

```powershell
Get-Content .\settings.json -Raw | ConvertFrom-Json
```

### ConvertFrom-SddlString (PowerShell 5.1 function)

Module Microsoft.PowerShell.Utility

Turns an SDDL security descriptor string into a readable object (owner, group, access rules).

**Syntax** (one line per parameter set)

```powershell
ConvertFrom-SddlString [-Sddl] <string> [-Type <Object>] [<CommonParameters>]
```

**Arguments**

- `-Sddl` <String> · **required** · position 0 · pipeline by value — Specifies the string representing the security descriptor in SDDL syntax.
- `-Type` <Object> · values: FileSystemRights, RegistryRights, ActiveDirectoryRights, MutexRights, SemaphoreRights, CryptoKeyRights, EventWaitHandleRights — Specifies the type of rights that SDDL string represents.

**Example**

```powershell
(Get-Acl C:\Windows).Sddl | ConvertFrom-SddlString
```

### ConvertFrom-String (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: CFS

(Windows PowerShell only) Parses text into objects using delimiters or an example template.

**Syntax** (one line per parameter set)

```powershell
ConvertFrom-String [-InputObject] <string> [-Delimiter <string>] [-PropertyNames <string[]>] [<CommonParameters>]

ConvertFrom-String [-InputObject] <string> [-TemplateFile <string[]>] [-TemplateContent <string[]>] [-IncludeExtent] [-UpdateTemplate] [<CommonParameters>]
```

**Arguments**

- `-Delimiter` <String> · alias -DEL — Specifies a regular expression that identifies the boundary between elements.
- `-PropertyNames` <String[]> · alias -PN — Specifies an array of property names to which to assign split values in the resulting object.
- `-TemplateFile` <String[]> · alias -TF — Specifies a file, as an array, that contains a template for the desired parsing of the string.
- `-TemplateContent` <String[]> · alias -TC — Specifies an expression, or an expression saved as a variable, that describes the properties to which this cmdlet assigns strings.
- `-IncludeExtent` switch · alias -IE — Indicates that this cmdlet includes an extent text property that is removed by default.
- `-UpdateTemplate` switch · alias -UT — Indicates that this cmdlet saves the results of a learning algorithm into a comment in the template file.
- `-InputObject` <String> · **required** · position 0 · pipeline by value — Specifies strings received from the pipeline, or a variable that contains a string object.

**Output:** PSCustomObject

**Example**

```powershell
'Ann 30','Bob 25' | ConvertFrom-String -PropertyNames Name, Age
```

### ConvertFrom-StringData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns lines of key=value text into a hash table.

**Syntax** (one line per parameter set)

```powershell
ConvertFrom-StringData [-StringData] <string> [<CommonParameters>]
```

**Arguments**

- `-StringData` <String> · **required** · position 0 · pipeline by value — Specifies the string to be converted. You can use this parameter or pipe a string to ConvertFrom-StringData.

**Output:** Hashtable

**Example**

```powershell
"name=Ann`nage=30" | ConvertFrom-StringData
```

### ConvertTo-Csv (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns objects into CSV strings (no file); see Export-Csv to write a file.

**Syntax** (one line per parameter set)

```powershell
ConvertTo-Csv [-InputObject] <psobject> [[-Delimiter] <char>] [-NoTypeInformation] [<CommonParameters>]

ConvertTo-Csv [-InputObject] <psobject> [-UseCulture] [-NoTypeInformation] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · **required** · position 0 · pipeline by value and by property name — Specifies the objects that are converted to CSV strings. Enter a variable that contains the objects or type a command or expression that gets the objects.
- `-Delimiter` <Char> · position 1 · default comma (,) — Specifies the delimiter to separate the property values in CSV strings.
- `-UseCulture` switch — Uses the list separator for the current culture as the item delimiter.
- `-NoTypeInformation` switch · alias -NTI — Removes the #TYPE information header from the output. This parameter became the default in PowerShell 6.0 and is included for backwards compatibility.

**Output:** String

**Example**

```powershell
Get-Process | Select-Object Name, Id | ConvertTo-Csv -NoTypeInformation
```

### ConvertTo-Html (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns objects into an HTML table or page.

**Syntax** (one line per parameter set)

```powershell
ConvertTo-Html [[-Property] <Object[]>] [[-Head] <string[]>] [[-Title] <string>] [[-Body] <string[]>] [-InputObject <psobject>] [-As <string>] [-CssUri <uri>] [-PostContent <string[]>] [-PreContent <string[]>] [<CommonParameters>]

ConvertTo-Html [[-Property] <Object[]>] [-InputObject <psobject>] [-As <string>] [-Fragment] [-PostContent <string[]>] [-PreContent <string[]>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be represented in HTML. Enter a variable that contains the objects or type a command or expression that gets the objects.
- `-Property` <Object[]> · position 0 — Includes the specified properties of the objects in the HTML. The value of the Property parameter can be a new calculated property.
- `-Body` <String[]> · position 3 — Specifies the text to add after the opening <BODY> tag. By default, there is no text in that position.
- `-Head` <String[]> · position 1 — Specifies the content of the <HEAD> tag. The default is <title\>HTML TABLE</title>.
- `-Title` <String> · position 2 — Specifies a title for the HTML file, that is, the text that appears between the <TITLE> tags.
- `-As` <String> · values: Table, List — Determines whether the object is formatted as a table or a list. Valid values are Table and List.
- `-CssUri` <Uri> · alias -cu, -uri — Specifies the Uniform Resource Identifier (URI) of the cascading style sheet (CSS) that is applied to the HTML file.
- `-Fragment` switch — Generates only an HTML table. The <HTML>, <HEAD>, <TITLE>, and <BODY> tags are omitted.
- `-PostContent` <String[]> — Specifies text to add after the closing </TABLE> tag. By default, there is no text in that position.
- `-PreContent` <String[]> — Specifies text to add before the opening <TABLE> tag. By default, there is no text in that position.

**Output:** String

**Example**

```powershell
Get-Service | ConvertTo-Html -Property Name, Status -Title Services | Out-File .\services.html
```

### ConvertTo-Json (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns objects into a JSON string; use -Depth for nested objects (default depth is 2).

**Syntax** (one line per parameter set)

```powershell
ConvertTo-Json [-InputObject] <Object> [-Depth <int>] [-Compress] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <Object> · **required** · position 0 · pipeline by value — Specifies the objects to convert to JSON format. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-Depth` <Int32> · default 2 — Specifies how many levels of contained objects are included in the JSON representation.
- `-Compress` switch — Omits white space and indented formatting in the output string.

**Output:** String

**Example**

```powershell
@{ name = 'Ann'; tags = @('a','b') } | ConvertTo-Json -Depth 3
```

### ConvertTo-Xml (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Creates an XML representation of objects.

**Syntax** (one line per parameter set)

```powershell
ConvertTo-Xml [-InputObject] <psobject> [-Depth <int>] [-NoTypeInformation] [-As <string>] [<CommonParameters>]
```

**Arguments**

- `-Depth` <Int32> — Specifies how many levels of contained objects are included in the XML representation.
- `-InputObject` <PSObject> · **required** · position 0 · pipeline by value — Specifies the object to be converted. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-NoTypeInformation` switch — Omits the Type attribute from the object nodes.
- `-As` <String> · values: Stream, String, Document — Determines the output format.

**Output:** String; XmlDocument

**Example**

```powershell
Get-Date | ConvertTo-Xml -As String
```

### Debug-Runspace (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Starts an interactive debugging session with another runspace in the process.

**Syntax** (one line per parameter set)

```powershell
Debug-Runspace [-Runspace] <runspace> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Runspace [-Name] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Runspace [-Id] <int> [-WhatIf] [-Confirm] [<CommonParameters>]

Debug-Runspace [-InstanceId] <guid> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Runspace` <Runspace> · **required** · position 0 · pipeline by value and by property name — Specifies a runspace object. The simplest way to provide a value for this parameter is to specify a variable that contains the results of a filtered Get-Runspace command.
- `-Name` <String> · **required** · position 0 — Specifies a runspace by its name. You can run Get-Runspace to show the names of runspaces.
- `-Id` <Int32> · **required** · position 0 — Specifies the ID number of a runspace. You can run Get-Runspace to show runspace IDs.
- `-InstanceId` <Guid> · **required** · position 0 — Specifies a runspace by its instance ID, a GUID that you can show by running Get-Runspace.

**Example**

```powershell
Debug-Runspace -Id 2
```

### Disable-PSBreakpoint (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: dbp

Turns off breakpoints without deleting them. Alias: dbp.

**Syntax** (one line per parameter set)

```powershell
Disable-PSBreakpoint [-Breakpoint] <Breakpoint[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-PSBreakpoint [-Id] <int[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-PassThru` switch — Returns an object representing the enabled breakpoints. By default, this cmdlet does not generate any output.
- `-Breakpoint` <Breakpoint[]> · **required** · position 0 · pipeline by value — Specifies the breakpoints to disable. Enter a variable that contains breakpoint objects or a command that gets breakpoint objects, such as a Get-PSBreakpoint command.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Disables the breakpoints with the specified breakpoint IDs. Enter the IDs or a variable that contains the IDs.

**Output:** None; Breakpoint

**Example**

```powershell
Get-PSBreakpoint | Disable-PSBreakpoint
```

### Disable-RunspaceDebug (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns off runspace debugging and releases a pending debugger stop.

**Syntax** (one line per parameter set)

```powershell
Disable-RunspaceDebug [[-RunspaceName] <string[]>] [<CommonParameters>]

Disable-RunspaceDebug [-Runspace] <runspace[]> [<CommonParameters>]

Disable-RunspaceDebug [-RunspaceId] <int[]> [<CommonParameters>]

Disable-RunspaceDebug [-RunspaceInstanceId] <guid[]> [<CommonParameters>]

Disable-RunspaceDebug [[-ProcessName] <string>] [[-AppDomainName] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-RunspaceName` <String[]> · position 0 — One or more Runspace names to be disabled.
- `-Runspace` <Runspace[]> · **required** · position 0 · pipeline by value and by property name — One or more Runspace objects to be disabled.
- `-RunspaceId` <Int32[]> · **required** · position 0 — One or more Runspace Id numbers to be disabled.
- `-RunspaceInstanceId` <Guid[]> · **required** · position 0 — One or more Runspace GUIDs to be disabled.
- `-ProcessName` <String> · position 0 — The name of the process that hosts the PowerShell runspace.
- `-AppDomainName` <String[]> · position 1 — The name of the application domain that hosts the PowerShell runspace.

**Example**

```powershell
Disable-RunspaceDebug -RunspaceName Worker
```

### Enable-PSBreakpoint (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: ebp

Turns breakpoints back on. Alias: ebp.

**Syntax** (one line per parameter set)

```powershell
Enable-PSBreakpoint [-Id] <int[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-PSBreakpoint [-Breakpoint] <Breakpoint[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-PassThru` switch — Returns an object representing the breakpoint being enabled. By default, this cmdlet doesn't generate any output.
- `-Breakpoint` <Breakpoint[]> · **required** · position 0 · pipeline by value — Specifies the breakpoints to enable. Provide a variable containing breakpoints or a command that gets breakpoint objects, such as Get-PSBreakpoint.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies the Id numbers of the breakpoints to enable. The default value is all breakpoints.

**Output:** None; Breakpoint

**Example**

```powershell
Enable-PSBreakpoint -Id 1, 2
```

### Enable-RunspaceDebug (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Enables debugging on runspaces so breakpoints hold until a debugger attaches.

**Syntax** (one line per parameter set)

```powershell
Enable-RunspaceDebug [[-RunspaceName] <string[]>] [-BreakAll] [<CommonParameters>]

Enable-RunspaceDebug [-RunspaceId] <int[]> [-BreakAll] [<CommonParameters>]

Enable-RunspaceDebug [-Runspace] <runspace[]> [-BreakAll] [<CommonParameters>]

Enable-RunspaceDebug [-RunspaceInstanceId] <guid[]> [<CommonParameters>]

Enable-RunspaceDebug [[-ProcessName] <string>] [[-AppDomainName] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-BreakAll` switch · position 1 — Causes any running command or script in the Runspace to stop in step mode, regardless of whether a debugger is currently attached.
- `-RunspaceName` <String[]> · position 0 — One or more Runspace names to be disabled.
- `-Runspace` <Runspace[]> · **required** · position 0 · pipeline by value and by property name — One or more Runspace objects to be disabled.
- `-RunspaceId` <Int32[]> · **required** · position 0 — One or more Runspace Id numbers to be disabled.
- `-RunspaceInstanceId` <Guid[]> · **required** · position 0 — One or more Runspace GUIDs to be disabled.
- `-ProcessName` <String> · position 0 — The name of the process that hosts the PowerShell runspace.
- `-AppDomainName` <String[]> · position 1 — The name of the application domain that hosts the PowerShell runspace.

**Example**

```powershell
Enable-RunspaceDebug -BreakAll
```

### Export-Alias (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: epal

Saves the session's aliases to a file (CSV or script) for reuse. Alias: epal.

**Syntax** (one line per parameter set)

```powershell
Export-Alias [-Path] <string> [[-Name] <string[]>] [-PassThru] [-As <ExportAliasFormat>] [-Append] [-Force] [-NoClobber] [-Description <string>] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Export-Alias [[-Name] <string[]>] -LiteralPath <string> [-PassThru] [-As <ExportAliasFormat>] [-Append] [-Force] [-NoClobber] [-Description <string>] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · wildcards — Specifies the path to the output file. Wildcards are permitted, but the resulting path value must resolve to a single file name.
- `-LiteralPath` <String> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the output file. Unlike Path, the value of the LiteralPath parameter is used exactly as it is typed.
- `-Name` <String[]> · position 1 · pipeline by property name · wildcards — Specifies the names as an array of the aliases to export. Wildcards are permitted.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-As` <ExportAliasFormat> — Specifies the output format. CSV is the default.
- `-Append` switch — Indicates that this cmdlet appends the output to the specified file, rather than overwriting the existing contents of that file.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-NoClobber` switch · alias -NoOverwrite — Indicates that this cmdlet prevents Export-Alias from overwriting any files, even if the Force parameter is used in the command.
- `-Description` <String> — Specifies the description of the exported file. The description appears as a comment at the top of the file, following the header information.
- `-Scope` <String> — Specifies the scope from which the aliases should be exported.

**Output:** None; AliasInfo

**Example**

```powershell
Export-Alias -Path .\aliases.ps1 -As Script
```

### Export-Clixml (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Saves objects to an XML (CLIXML) file that Import-Clixml can rebuild with types and values.

**Syntax** (one line per parameter set)

```powershell
Export-Clixml [-Path] <string> -InputObject <psobject> [-Depth <int>] [-Force] [-NoClobber] [-Encoding <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Export-Clixml -LiteralPath <string> -InputObject <psobject> [-Depth <int>] [-Force] [-NoClobber] [-Encoding <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Depth` <Int32> · default 2 — Specifies how many levels of contained objects are included in the XML representation.
- `-Path` <String> · **required** · position 0 — Specifies the path to the file where the XML representation of the object will be stored.
- `-LiteralPath` <String> · **required** · alias -PSPath — Specifies the path to the file where the XML representation of the object will be stored.
- `-InputObject` <PSObject> · **required** · pipeline by value — Specifies the object to be converted. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-NoClobber` switch · alias -NoOverwrite — Indicates that the cmdlet doesn't overwrite the contents of an existing file.
- `-Encoding` <String> · values: Unicode, UTF7, UTF8, ASCII, UTF32, BigEndianUnicode, Default, OEM · default Unicode — Specifies the type of encoding for the target file. The default value is Unicode.

**Output:** FileInfo

**Example**

```powershell
Get-Process | Export-Clixml -Path .\procs.xml
```

### Export-Csv (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: epcsv

Writes objects to a CSV file, one row per object. Alias: epcsv.

**Syntax** (one line per parameter set)

```powershell
Export-Csv [[-Path] <string>] [[-Delimiter] <char>] -InputObject <psobject> [-LiteralPath <string>] [-Force] [-NoClobber] [-Encoding <string>] [-Append] [-NoTypeInformation] [-WhatIf] [-Confirm] [<CommonParameters>]

Export-Csv [[-Path] <string>] -InputObject <psobject> [-LiteralPath <string>] [-Force] [-NoClobber] [-Encoding <string>] [-Append] [-UseCulture] [-NoTypeInformation] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · **required** · pipeline by value and by property name — Specifies the objects to export as CSV strings. Enter a variable that contains the objects or type a command or expression that gets the objects.
- `-Path` <String> · position 0 — A required parameter that specifies the location to save the CSV output file.
- `-LiteralPath` <String> · alias -PSPath — Specifies the path to the CSV output file. Unlike Path, the value of the LiteralPath parameter is used exactly as it is typed.
- `-Force` switch — This parameter allows Export-Csv to overwrite files with the Read Only attribute.
- `-NoClobber` switch · alias -NoOverwrite — Use this parameter so that Export-Csv does not overwrite an existing file.
- `-Encoding` <String> · values: Unicode, UTF7, UTF8, ASCII, UTF32, BigEndianUnicode, Default, OEM · default ASCII — Specifies the encoding for the exported CSV file. The default value is ASCII.
- `-Append` switch — Use this parameter so that Export-Csv adds CSV output to the end of the specified file.
- `-Delimiter` <Char> · position 1 · default comma (,) — Specifies a delimiter to separate the property values. The default is a comma (,).
- `-UseCulture` switch — Uses the list separator for the current culture as the item delimiter.
- `-NoTypeInformation` switch · alias -NTI — Removes the #TYPE information header from the output. This parameter became the default in PowerShell 6.0 and is included for backwards compatibility.

**Output:** None

**Example**

```powershell
Get-Service | Select-Object Name, Status | Export-Csv -Path .\svc.csv -NoTypeInformation
```

### Export-FormatData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Saves formatting data (views) from the session to a .format.ps1xml file.

**Syntax** (one line per parameter set)

```powershell
Export-FormatData -InputObject <ExtendedTypeDefinition[]> -Path <string> [-Force] [-NoClobber] [-IncludeScriptBlock] [<CommonParameters>]

Export-FormatData -InputObject <ExtendedTypeDefinition[]> -LiteralPath <string> [-Force] [-NoClobber] [-IncludeScriptBlock] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ExtendedTypeDefinition[]> · **required** · pipeline by value — Specifies the format data objects to be exported. Enter a variable that contains the objects or a command that gets the objects, such as a Get-FormatData command.
- `-Path` <String> · **required** · alias -FilePath — Specifies a location for the output file. Enter a path (optional) and file name with a format.ps1xml file name extension.
- `-LiteralPath` <String> · **required** · alias -PSPath — Specifies a location for the output file. Unlike the Path parameter, the value of LiteralPath is used exactly as it is typed.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-NoClobber` switch · alias -NoOverwrite — Indicates that the cmdlet does not overwrite existing files. By default, Export-FormatData overwrites files without warning unless the file has the read-only attribute.
- `-IncludeScriptBlock` switch — Indicates whether scriptblocks in the format data are exported.

**Output:** None

**Example**

```powershell
Get-FormatData -TypeName System.Diagnostics.Process | Export-FormatData -Path .\proc.format.ps1xml
```

### Export-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: epsn

Saves the commands from another session (proxy functions) as a module you can import later.

**Syntax** (one line per parameter set)

```powershell
Export-PSSession [-Session] <PSSession> [-OutputModule] <string> [[-CommandName] <string[]>] [[-FormatTypeName] <string[]>] [-Force] [-Encoding <string>] [-AllowClobber] [-ArgumentList <Object[]>] [-CommandType <CommandTypes>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Certificate <X509Certificate2>] [<CommonParameters>]
```

**Arguments**

- `-OutputModule` <String> · **required** · position 1 · alias -PSPath, -ModuleName · default $HOME\Documents\WindowsPowerShell\Modules — Specifies an optional path and name for the module created by Export-PSSession.
- `-Force` switch — Overwrites one or more existing output files, even if the file has the read-only attribute.
- `-Encoding` <String> · values: Unicode, UTF7, UTF8, ASCII, UTF32, BigEndianUnicode, Default, OEM · default UTF8 — Specifies the type of encoding for the target file. The default value is UTF8.
- `-CommandName` <String[]> · position 2 · alias -Name · default All commands in the session. · wildcards — Exports only the commands with the specified names or name patterns. Wildcards are permitted.
- `-AllowClobber` switch — Exports the specified commands, even if they have the same names as commands in the current session.
- `-ArgumentList` <Object[]> · alias -Args — Exports the variant of the command that results from using the specified arguments (parameter values).
- `-CommandType` <CommandTypes> · alias -Type · default All commands in the session. — Exports only the specified types of command objects. Use CommandType or its alias, Type.
- `-Module` <String[]> · alias -PSSnapin · default All commands in the session. — Exports only the commands in the specified PowerShell snap-ins and modules.
- `-FullyQualifiedModule` <ModuleSpecification[]> — The value can be a module name, a full module specification, or a path to a module file.
- `-FormatTypeName` <String[]> · position 3 — Exports formatting instructions only for the specified Microsoft .NET Framework types.
- `-Certificate` <X509Certificate2> — Specifies the client certificate that is used to sign the format files (*.Format.ps1xml) or script module files (.psm1) in the module that Export-PSSession creates.
- `-Session` <PSSession> · **required** · position 0 — Specifies the PSSession from which the commands are exported. Enter a variable that contains a session object or a command that gets a session object, such as a Get-PSSession command.

**Output:** FileInfo

**Example**

```powershell
Export-PSSession -Session $s -CommandName Get-Mailbox -OutputModule RemoteMail
```

### Format-Custom (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: fc

Formats output using a custom view (shows object structure). Alias: fc.

**Syntax** (one line per parameter set)

```powershell
Format-Custom [[-Property] <Object[]>] [-Depth <int>] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Property` <Object[]> · position 0 · wildcards — Specifies the object properties that appear in the display and the order in which they appear.
- `-Depth` <Int32> — Specifies the number of columns in the display.
- `-GroupBy` <Object> — Formats the output in groups based on a shared property or value. Enter an expression or a property of the output.
- `-View` <String> — Specifies the name of an alternate format or view. If you omit this parameter, Format-Custom uses a default custom view.
- `-ShowError` switch — Sends errors through the pipeline. This parameter is rarely used, but can be used as a debugging aid when you are formatting expressions in a Format-Custom command, and the expressions do not appear to be working.
- `-DisplayError` switch — Displays errors at the command line. This parameter is rarely used, but can be used as a debugging aid when you are formatting expressions in a Format-Custom command, and the expressions do not appear to be working.
- `-Force` switch — Directs the cmdlet to display all of the error information. Use with the DisplayError or ShowError parameters.
- `-Expand` <String> · values: CoreOnly, EnumOnly, Both · default EnumOnly — Formats the collection object, as well as the objects in the collection.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be formatted. Enter a variable that contains the objects or type a command or expression that gets the objects.

**Output:** Format

**Example**

```powershell
Get-Date | Format-Custom
```

### Format-Hex (PowerShell 5.1 function)

Module Microsoft.PowerShell.Utility · aliases: fhx

Shows a file, string or bytes as hexadecimal. Alias: fhx.

**Syntax** (one line per parameter set)

```powershell
Format-Hex [-Path] <string[]> [<CommonParameters>]

Format-Hex -LiteralPath <string[]> [<CommonParameters>]

Format-Hex -InputObject <Object> [-Encoding <string>] [-Raw] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · wildcards — Specifies the path to files. Use a dot (.) to specify the current location.
- `-LiteralPath` <String[]> · **required** · alias -PSPath — Specifies the complete path to a file. The value of LiteralPath is used exactly as it is typed.
- `-InputObject` <Object> · **required** · pipeline by value — Specifies the objects to be formatted. Enter a variable that contains the objects or type a command or expression that gets the objects.
- `-Encoding` <String> · values: Ascii, UTF32, UTF7, UTF8, BigEndianUnicode, Unicode · default ASCII — Specifies the encoding of the output. This only applies to [string] input.
- `-Raw` switch — By default Format-Hex opts for compact output of numeric data types: single-byte or double-byte sequences are used if the value is small enough.

**Output:** ByteCollection

**Example**

```powershell
Format-Hex -Path .\data.bin | Select-Object -First 4
```

### Format-List (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: fl

Formats output as a list, one property per line. Alias: fl.

**Syntax** (one line per parameter set)

```powershell
Format-List [[-Property] <Object[]>] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Property` <Object[]> · position 0 · wildcards — Specifies the object properties that appear in the display and the order in which they appear.
- `-GroupBy` <Object> — Specifies the output in groups based on a shared property or value. Enter an expression or a property of the output.
- `-View` <String> — Specifies the name of an alternate list format or view. You cannot use the Property and View parameters in the same command.
- `-ShowError` switch — Indicates that the cmdlet sends errors through the pipeline. This parameter is rarely used, but can be used as a debugging aid when you are formatting expressions in a Format-List command, and the expressions do not appear to be working.
- `-DisplayError` switch — Indicates that this cmdlet displays errors at the command line. This parameter is rarely used, but can be used as a debugging aid when you are formatting expressions in a Format-List command, and the expressions do not appear to be working.
- `-Force` switch — Indicates that this cmdlet displays all the error information. Use with the DisplayError or ShowError parameter.
- `-Expand` <String> · values: CoreOnly, EnumOnly, Both — Specifies the formatted collection object, as well as the objects in the collection.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be formatted. Enter a variable that contains the objects or type a command or expression that gets the objects.

**Output:** Format

**Example**

```powershell
Get-Process -Id $PID | Format-List *
```

### Format-Table (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: ft

Formats output as a table with chosen columns. Alias: ft.

**Syntax** (one line per parameter set)

```powershell
Format-Table [[-Property] <Object[]>] [-AutoSize] [-RepeatHeader] [-HideTableHeaders] [-Wrap] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-AutoSize` switch — Indicates that the cmdlet adjusts the column size and number of columns based on the width of the data.
- `-RepeatHeader` switch — Repeats displaying the header of a table after every screen full. The repeated header is useful when the output is piped to a pager such as less or more or paging with a screen reader.
- `-HideTableHeaders` switch — Omits the column headings from the table.
- `-Wrap` switch — Displays text that exceeds the column width on the next line. By default, text that exceeds the column width is truncated.
- `-Property` <Object[]> · position 0 · wildcards — Specifies the object properties that appear in the display and the order in which they appear.
- `-GroupBy` <Object> — Specifies sorted output in separate tables based on a property value. For example, you can use GroupBy to list services in separate tables based on their status.
- `-View` <String> — In PowerShell 5.1 and earlier versions, the default views are defined in *.format.ps1xml files stored in $PSHOME.
- `-ShowError` switch — This parameter sends errors through the pipeline. This parameter can be used as a debugging aid when you're formatting expressions in a Format-Table command and need to troubleshoot the expressions.
- `-DisplayError` switch — Indicates that the cmdlet displays errors on the command line. This parameter can be used as a debugging aid when you're formatting expressions in a Format-Table command and need to troubleshoot the expressions.
- `-Force` switch — Indicates that the cmdlet directs the cmdlet to display all the error information.
- `-Expand` <String> · values: CoreOnly, EnumOnly, Both — Specifies the format of the collection object and the objects in the collection.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to format. Enter a variable that contains the objects, or type a command or expression that gets the objects.

**Output:** Format

**Example**

```powershell
Get-Process | Format-Table Name, Id, CPU -AutoSize
```

### Format-Wide (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: fw

Formats objects as a wide table showing one property, in columns. Alias: fw.

**Syntax** (one line per parameter set)

```powershell
Format-Wide [[-Property] <Object>] [-AutoSize] [-Column <int>] [-GroupBy <Object>] [-View <string>] [-ShowError] [-DisplayError] [-Force] [-Expand <string>] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Property` <Object> · position 0 · wildcards — Specifies the object property that appears in the display. Wildcards are permitted.
- `-AutoSize` switch — Adjusts the column size and number of columns based on the width of the data.
- `-Column` <Int32> — Specifies the number of columns in the display. You cannot use the AutoSize and Column parameters in the same command.
- `-GroupBy` <Object> — Formats the output in groups based on a shared property or value. Enter an expression or a property of the output.
- `-View` <String> — Specifies the name of an alternate table format or view. You cannot use the Property and View parameters in the same command.
- `-ShowError` switch — Sends errors through the pipeline. This parameter is rarely used, but can be used as a debugging aid when you are formatting expressions in a Format-Wide command, and the expressions do not appear to be working.
- `-DisplayError` switch — Displays errors at the command line. This parameter is rarely used, but can be used as a debugging aid when you are formatting expressions in a Format-Wide command, and the expressions do not appear to be working.
- `-Force` switch — Indicates that this cmdlet overrides restrictions that prevent the command from succeeding, just so the changes do not compromise security.
- `-Expand` <String> · values: CoreOnly, EnumOnly, Both · default EnumOnly — Formats the collection object, as well as the objects in the collection.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to format. Enter a variable that contains the objects, or type a command or expression that gets the objects.

**Output:** Format

**Example**

```powershell
Get-ChildItem | Format-Wide -Column 4
```

### Get-Alias (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: gal

Lists aliases, or the alias for a command with -Definition. Alias: gal.

**Syntax** (one line per parameter set)

```powershell
Get-Alias [[-Name] <string[]>] [-Exclude <string[]>] [-Scope <string>] [<CommonParameters>]

Get-Alias [-Exclude <string[]>] [-Scope <string>] [-Definition <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · default All aliases · wildcards — Specifies the aliases that this cmdlet gets. Wildcards are permitted.
- `-Exclude` <String[]> · wildcards — Omits the specified items. The value of this parameter qualifies the Name and Definition parameters.
- `-Scope` <String> · default Local — Specifies the scope for which this cmdlet gets aliases.
- `-Definition` <String[]> · wildcards — Gets the aliases for the specified item. Enter the name of a cmdlet, function, script, file, or executable file.

**Output:** AliasInfo

**Example**

```powershell
Get-Alias -Definition Get-ChildItem
```

### Get-Culture (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Shows the culture (regional format) settings of the operating system.

**Syntax** (one line per parameter set)

```powershell
Get-Culture [<CommonParameters>]
```

**Output:** CultureInfo

**Example**

```powershell
(Get-Culture).DateTimeFormat.ShortDatePattern
```

### Get-Date (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Gets the current date and time, or a specific date, optionally formatted.

**Syntax** (one line per parameter set)

```powershell
Get-Date [[-Date] <datetime>] [-Year <int>] [-Month <int>] [-Day <int>] [-Hour <int>] [-Minute <int>] [-Second <int>] [-Millisecond <int>] [-DisplayHint <DisplayHintType>] [-Format <string>] [<CommonParameters>]

Get-Date [[-Date] <datetime>] [-Year <int>] [-Month <int>] [-Day <int>] [-Hour <int>] [-Minute <int>] [-Second <int>] [-Millisecond <int>] [-DisplayHint <DisplayHintType>] [-UFormat <string>] [<CommonParameters>]
```

**Arguments**

- `-Date` <DateTime> · position 0 · pipeline by value and by property name · alias -LastWriteTime — Specifies a date and time. Time is optional and if not specified, returns 00:00:00.
- `-Year` <Int32> — Specifies the year that is displayed. Enter a value from 1 to 9999.
- `-Month` <Int32> — Specifies the month that is displayed. Enter a value from 1 to 12.
- `-Day` <Int32> — Specifies the day of the month that is displayed. Enter a value from 1 to 31.
- `-Hour` <Int32> — Specifies the hour that is displayed. Enter a value from 0 to 23.
- `-Minute` <Int32> — Specifies the minute that is displayed. Enter a value from 0 to 59.
- `-Second` <Int32> — Specifies the second that is displayed. Enter a value from 0 to 59.
- `-Millisecond` <Int32> — Specifies the milliseconds in the date. Enter a value from 0 to 999.
- `-DisplayHint` <DisplayHintType> — Determines which elements of the date and time are displayed.
- `-UFormat` <String> — Displays the date and time in Unix format. The UFormat parameter outputs a string object.
- `-Format` <String> — Displays the date and time in the Microsoft .NET Framework format indicated by the format specifier.

**Output:** DateTime; String

**Example**

```powershell
Get-Date -Format 'yyyy-MM-dd HH:mm'
```

### Get-Event (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Gets events in the session's event queue (from Register-*Event subscriptions).

**Syntax** (one line per parameter set)

```powershell
Get-Event [[-SourceIdentifier] <string>] [<CommonParameters>]

Get-Event [-EventIdentifier] <int> [<CommonParameters>]
```

**Arguments**

- `-SourceIdentifier` <String> · position 0 · pipeline by property name — Specifies source identifiers for which this cmdlet gets events. The default is all events in the event queue.
- `-EventIdentifier` <Int32> · **required** · position 0 · pipeline by property name · alias -Id — Specifies the event identifiers for which this cmdlet gets events.

**Output:** PSEventArgs

**Example**

```powershell
Get-Event -SourceIdentifier ProcStart
```

### Get-EventSubscriber (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Lists the event subscriptions in the session.

**Syntax** (one line per parameter set)

```powershell
Get-EventSubscriber [[-SourceIdentifier] <string>] [-Force] [<CommonParameters>]

Get-EventSubscriber [-SubscriptionId] <int> [-Force] [<CommonParameters>]
```

**Arguments**

- `-SourceIdentifier` <String> · position 0 · pipeline by property name — Specifies the SourceIdentifier property value that gets only the event subscribers.
- `-SubscriptionId` <Int32> · **required** · position 0 · pipeline by property name · alias -Id — Specifies the subscription identifier that this cmdlet gets. By default, Get-EventSubscriber gets all event subscribers in the session.
- `-Force` switch · position 1 — Indicates that this cmdlet gets all event subscribers, including subscribers for events that are hidden by using the SupportEvent parameter of Register-ObjectEvent, Register-WmiEvent, and Register-EngineEvent.

**Output:** PSEventSubscriber

**Example**

```powershell
Get-EventSubscriber
```

### Get-FileHash (PowerShell 5.1 function)

Module Microsoft.PowerShell.Utility

Computes a file's hash (SHA256 by default; also SHA1, SHA384, SHA512, MD5).

**Syntax** (one line per parameter set)

```powershell
Get-FileHash [-Path] <string[]> [-Algorithm <string>] [<CommonParameters>]

Get-FileHash -LiteralPath <string[]> [-Algorithm <string>] [<CommonParameters>]

Get-FileHash -InputStream <Stream> [-Algorithm <string>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · wildcards — Specifies the path to one or more files as an array. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to a file. Unlike the Path parameter, the value of the LiteralPath parameter is used exactly as it is typed.
- `-InputStream` <Stream> · **required** — Specifies the input stream.
- `-Algorithm` <String> · values: SHA1, SHA256, SHA384, SHA512, MACTripleDES, MD5, RIPEMD160 · default SHA256 — Specifies the cryptographic hash function to use for computing the hash value of the contents of the specified file or stream.

**Output:** FileHash

**Example**

```powershell
Get-FileHash -Path .\setup.exe -Algorithm SHA256
```

### Get-FormatData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Gets the formatting data (views) loaded in the session for a type.

**Syntax** (one line per parameter set)

```powershell
Get-FormatData [[-TypeName] <string[]>] [-PowerShellVersion <version>] [<CommonParameters>]
```

**Arguments**

- `-TypeName` <String[]> · position 0 · wildcards — Specifies the type names that this cmdlet gets for the formatting data.
- `-PowerShellVersion` <Version> — Specify the version of PowerShell this cmdlet gets for the formatting data.

**Output:** ExtendedTypeDefinition

**Example**

```powershell
Get-FormatData -TypeName System.IO.FileInfo
```

### Get-Host (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Gets the object that represents the current host program (name, version, UI).

**Syntax** (one line per parameter set)

```powershell
Get-Host [<CommonParameters>]
```

**Output:** InternalHost

**Example**

```powershell
(Get-Host).Version
```

### Get-Member (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: gm

Lists the properties and methods of objects in the pipeline. Alias: gm.

**Syntax** (one line per parameter set)

```powershell
Get-Member [[-Name] <string[]>] [-InputObject <psobject>] [-MemberType <PSMemberTypes>] [-View <PSMemberViewTypes>] [-Static] [-Force] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies the object whose members are retrieved.
- `-Name` <String[]> · position 0 — Specifies the names of one or more properties or methods of the object.
- `-MemberType` <PSMemberTypes> · alias -Type — Specifies the member type that this cmdlet gets. The default is All.
- `-View` <PSMemberViewTypes> · default Adapted, Extended — Specifies that this cmdlet gets only particular types properties and methods.
- `-Static` switch — Indicates that this cmdlet gets only the static properties and methods of the object.
- `-Force` switch — Adds the intrinsic members and the compiler-generated get_ and set_ methods to the display.

**Output:** MemberDefinition

**Example**

```powershell
Get-Service | Get-Member -MemberType Property
```

### Get-PSBreakpoint (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: gbp

Lists the breakpoints set in the session. Alias: gbp.

**Syntax** (one line per parameter set)

```powershell
Get-PSBreakpoint [[-Script] <string[]>] [<CommonParameters>]

Get-PSBreakpoint -Variable <string[]> [-Script <string[]>] [<CommonParameters>]

Get-PSBreakpoint -Command <string[]> [-Script <string[]>] [<CommonParameters>]

Get-PSBreakpoint [-Type] <BreakpointType[]> [-Script <string[]>] [<CommonParameters>]

Get-PSBreakpoint [-Id] <int[]> [<CommonParameters>]
```

**Arguments**

- `-Script` <String[]> · position 0 · pipeline by value — Specifies an array of scripts that contain the breakpoints. Enter the path (optional) and names of one or more script files.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by value — Specifies the breakpoint IDs that this cmdlet gets. Enter the IDs in a comma-separated list.
- `-Variable` <String[]> · **required** — Specifies an array of variable breakpoints that are set on the specified variable names.
- `-Command` <String[]> · **required** — Specifies an array of command breakpoints that are set on the specified command names.
- `-Type` <BreakpointType[]> · **required** · position 0 · pipeline by value — Specifies an array of breakpoint types that this cmdlet gets. Enter one or more types.

**Output:** CommandBreakpoint; LineBreakpoint; VariableBreakpoint; Breakpoint

**Example**

```powershell
Get-PSBreakpoint -Script .\deploy.ps1
```

### Get-PSCallStack (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: gcs

Shows the current call stack (useful inside the debugger). Alias: gcs.

**Syntax** (one line per parameter set)

```powershell
Get-PSCallStack [<CommonParameters>]
```

**Output:** CallStackFrame

**Example**

```powershell
Get-PSCallStack
```

### Get-Random (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Returns a random number, or picks random items from a collection.

**Syntax** (one line per parameter set)

```powershell
Get-Random [[-Maximum] <Object>] [-SetSeed <int>] [-Minimum <Object>] [<CommonParameters>]

Get-Random [-InputObject] <Object[]> [-SetSeed <int>] [-Count <int>] [<CommonParameters>]
```

**Arguments**

- `-SetSeed` <Nullable`1> — Specifies a seed value for the random number generator. When you use SetSeed, the cmdlet generates pseudorandom numbers, which isn't cryptographically secure.
- `-Maximum` <Object> · position 0 — Specifies a maximum value for the random number. Get-Random returns a value that's less than the maximum (not equal).
- `-Minimum` <Object> — Specifies a minimum value for the random number. Enter an integer, a double-precision floating-point number, or an object that can be converted to an integer or double, such as a numeric string ("100").
- `-InputObject` <Object[]> · **required** · position 0 · pipeline by value — Specifies a collection of objects. Get-Random gets randomly selected objects in random order from the collection up to the number specified by Count.
- `-Count` <Int32> — Specifies the number of random objects to return. The default is 1.

**Output:** Int32; Int64; Double; PSObject

**Example**

```powershell
Get-Random -Minimum 1 -Maximum 101
```

### Get-Runspace (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Lists the runspaces in the PowerShell host process.

**Syntax** (one line per parameter set)

```powershell
Get-Runspace [[-Name] <string[]>] [<CommonParameters>]

Get-Runspace [-Id] <int[]> [<CommonParameters>]

Get-Runspace [-InstanceId] <guid[]> [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — Specifies the Name of a runspace
- `-Id` <Int32[]> · **required** · position 0 — Specifies the Id of a runspace
- `-InstanceId` <Guid[]> · **required** · position 0 — Specifies the instance ID GUID of a running job.

**Output:** Runspace

**Example**

```powershell
Get-Runspace
```

### Get-RunspaceDebug (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Shows the debugging options of runspaces.

**Syntax** (one line per parameter set)

```powershell
Get-RunspaceDebug [[-RunspaceName] <string[]>] [<CommonParameters>]

Get-RunspaceDebug [-Runspace] <runspace[]> [<CommonParameters>]

Get-RunspaceDebug [-RunspaceId] <int[]> [<CommonParameters>]

Get-RunspaceDebug [-RunspaceInstanceId] <guid[]> [<CommonParameters>]

Get-RunspaceDebug [[-ProcessName] <string>] [[-AppDomainName] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-RunspaceName` <String[]> · position 0 — One or more Runspace names to be disabled.
- `-Runspace` <Runspace[]> · **required** · position 0 · pipeline by value and by property name — One or more Runspace objects to be disabled.
- `-RunspaceId` <Int32[]> · **required** · position 0 — One or more Runspace Id numbers to be disabled.
- `-RunspaceInstanceId` <Guid[]> · **required** · position 0 — One or more Runspace GUIDs to be disabled.
- `-ProcessName` <String> · position 0 — The name of the process that hosts the PowerShell runspace.
- `-AppDomainName` <String[]> · position 1 — The name of the application domain that hosts the PowerShell runspace.

**Example**

```powershell
Get-RunspaceDebug
```

### Get-TraceSource (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Lists PowerShell components that are instrumented for tracing.

**Syntax** (one line per parameter set)

```powershell
Get-TraceSource [[-Name] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies the trace sources to get. Wildcards are permitted.

**Output:** PSTraceSource

**Example**

```powershell
Get-TraceSource -Name *Command*
```

### Get-TypeData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Gets the extended type data (added properties and methods) loaded in the session.

**Syntax** (one line per parameter set)

```powershell
Get-TypeData [[-TypeName] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-TypeName` <String[]> · position 0 · pipeline by value · wildcards — Specifies type data as an array only for the types with the specified names.

**Output:** TypeData

**Example**

```powershell
Get-TypeData -TypeName System.IO.FileInfo
```

### Get-UICulture (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Shows the user-interface language culture of the operating system.

**Syntax** (one line per parameter set)

```powershell
Get-UICulture [<CommonParameters>]
```

**Output:** CultureInfo; VistaCultureInfo

**Example**

```powershell
(Get-UICulture).Name
```

### Get-Unique (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: gu

Removes adjacent duplicates from a sorted list. Alias: gu.

**Syntax** (one line per parameter set)

```powershell
Get-Unique [-InputObject <psobject>] [-AsString] [<CommonParameters>]

Get-Unique [-InputObject <psobject>] [-OnType] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies input for Get-Unique. Enter a variable that contains the objects or type a command or expression that gets the objects.
- `-AsString` switch — Indicates that this cmdlet uses the data as a string. Without this parameter, data is treated as an object, so when you submit a collection of objects of the same type to Get-Unique, such as a collection of files, it returns just one (the first).
- `-OnType` switch — Indicates that this cmdlet returns only one object of each type.

**Output:** PSObject

**Example**

```powershell
1,1,2,3,3 | Get-Unique
```

### Get-Variable (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: gv

Lists variables in the session, or gets one by name. Alias: gv.

**Syntax** (one line per parameter set)

```powershell
Get-Variable [[-Name] <string[]>] [-ValueOnly] [-Include <string[]>] [-Exclude <string[]>] [-Scope <string>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies the name of the variable. Wildcards are permitted.
- `-ValueOnly` switch — Indicates that this cmdlet gets only the value of the variable.
- `-Include` <String[]> · wildcards — Specifies an array of items upon which the cmdlet will act, excluding all others.
- `-Exclude` <String[]> · wildcards — Specifies an array of items that this cmdlet excludes from the operation.
- `-Scope` <String> — Specifies the variables in the scope.The acceptable values for this parameter are:

**Output:** PSVariable; Object[]

**Example**

```powershell
Get-Variable -Name PS*
```

### Group-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: group

Groups objects that share a property value, with counts. Alias: group.

**Syntax** (one line per parameter set)

```powershell
Group-Object [[-Property] <Object[]>] [-NoElement] [-AsHashTable] [-AsString] [-InputObject <psobject>] [-Culture <string>] [-CaseSensitive] [<CommonParameters>]
```

**Arguments**

- `-NoElement` switch — Indicates that this cmdlet omits the members of a group from the results.
- `-AsHashTable` switch · alias -AHT — Indicates that this cmdlet returns the group as a hash table. The keys of the hash table are the property values by which the objects are grouped.
- `-AsString` switch — Indicates that this cmdlet converts the hash table keys to strings. By default, the hash table keys are instances of the grouped object.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to group. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-Property` <Object[]> · position 0 — Specifies the properties for grouping. The objects are arranged into named groups based on the value of the specified properties.
- `-Culture` <String> — Specifies the culture to use when comparing strings.
- `-CaseSensitive` switch — Indicates that this cmdlet makes the grouping case-sensitive. Without this parameter, the property values of objects in a group might have different cases.

**Output:** GroupInfo; Hashtable

**Example**

```powershell
Get-ChildItem | Group-Object Extension | Sort-Object Count -Descending
```

### Import-Alias (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: ipal

Imports aliases from a file created by Export-Alias. Alias: ipal.

**Syntax** (one line per parameter set)

```powershell
Import-Alias [-Path] <string> [-Scope <string>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Import-Alias -LiteralPath <string> [-Scope <string>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path to a file that includes exported alias information.
- `-LiteralPath` <String> · **required** · pipeline by value and by property name · alias -PSPath — Specifies the path to a file that includes exported alias information.
- `-Scope` <String> — Specifies the scope into which the aliases are imported.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Force` switch — Allows the cmdlet to import an alias that is already defined or is read only.

**Output:** None; AliasInfo

**Example**

```powershell
Import-Alias -Path .\aliases.csv
```

### Import-Clixml (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Rebuilds objects from a CLIXML file made by Export-Clixml.

**Syntax** (one line per parameter set)

```powershell
Import-Clixml [-Path] <string[]> [-IncludeTotalCount] [-Skip <uint64>] [-First <uint64>] [<CommonParameters>]

Import-Clixml -LiteralPath <string[]> [-IncludeTotalCount] [-Skip <uint64>] [-First <uint64>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies the path to the XML files.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the XML files. Unlike Path, the value of the LiteralPath parameter is used exactly as it's typed.
- `-IncludeTotalCount` switch — Reports the total number of objects in the data set followed by the selected objects.
- `-Skip` <UInt64> — Ignores the specified number of objects and then gets the remaining objects.
- `-First` <UInt64> — Gets only the specified number of objects. Enter the number of objects to get.

**Output:** PSObject

**Example**

```powershell
$procs = Import-Clixml -Path .\procs.xml
```

### Import-Csv (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: ipcsv

Reads a CSV file into objects, one per row. Alias: ipcsv.

**Syntax** (one line per parameter set)

```powershell
Import-Csv [[-Path] <string[]>] [[-Delimiter] <char>] [-LiteralPath <string[]>] [-Header <string[]>] [-Encoding <string>] [<CommonParameters>]

Import-Csv [[-Path] <string[]>] -UseCulture [-LiteralPath <string[]>] [-Header <string[]>] [-Encoding <string>] [<CommonParameters>]
```

**Arguments**

- `-Delimiter` <Char> · position 1 · default comma (,) — Specifies the delimiter that separates the property values in the CSV file.
- `-Path` <String[]> · position 0 · pipeline by value — Specifies the path to the CSV file to import. You can also pipe a path to Import-Csv.
- `-LiteralPath` <String[]> · pipeline by property name · alias -PSPath — Specifies the path to the CSV file to import. Unlike Path, the value of the LiteralPath parameter is used exactly as it's typed.
- `-UseCulture` switch · **required** — Uses the list separator for the current culture as the item delimiter.
- `-Header` <String[]> — Specifies an alternate column header row for the imported file. The column header determines the property names of the objects created by Import-Csv.
- `-Encoding` <String> · values: Unicode, UTF7, UTF8, ASCII, UTF32, BigEndianUnicode, Default, OEM · default Default — Specifies the type of encoding for the target file. The default value is Default.

**Output:** Object

**Example**

```powershell
Import-Csv -Path .\users.csv | Where-Object Department -eq IT
```

### Import-LocalizedData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Loads language-specific strings (from .psd1 files in culture folders) into a script.

**Syntax** (one line per parameter set)

```powershell
Import-LocalizedData [[-BindingVariable] <string>] [[-UICulture] <string>] [-BaseDirectory <string>] [-FileName <string>] [-SupportedCommand <string[]>] [<CommonParameters>]
```

**Arguments**

- `-BindingVariable` <String> · position 0 · alias -Variable — Specifies the variable into which the text strings are imported. Enter a variable name without a dollar sign ($).
- `-UICulture` <String> · position 1 — Specifies an alternate UI culture. The default is the value of the $PSUICulture automatic variable.
- `-BaseDirectory` <String> — Specifies the base directory where the .psd1 files are located. The default is the directory where the script is located.
- `-FileName` <String> — Specifies the name of the data file (.psd1) to be imported. Enter a filename.
- `-SupportedCommand` <String[]> — Specifies cmdlets and functions that generate only data.

**Output:** Hashtable

**Example**

```powershell
Import-LocalizedData -BindingVariable msg -FileName Messages.psd1
```

### Import-PowerShellDataFile (PowerShell 5.1 function)

Module Microsoft.PowerShell.Utility

Reads a .psd1 data file into a hash table without running code.

**Syntax** (one line per parameter set)

```powershell
Import-PowerShellDataFile [[-Path] <string[]>] [<CommonParameters>]

Import-PowerShellDataFile [-LiteralPath <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · position 0 · wildcards — The path to the file being imported. Wildcards are allowed but only the first matching file is imported.
- `-LiteralPath` <String[]> · pipeline by property name · alias -PSPath — The path to the file being imported. All characters in the path are treated as literal values.

**Output:** Hashtable

**Example**

```powershell
Import-PowerShellDataFile -Path .\MyTools.psd1
```

### Import-PSSession (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: ipsn

Imports commands from another session (for example a remote server) as local proxy commands.

**Syntax** (one line per parameter set)

```powershell
Import-PSSession [-Session] <PSSession> [[-CommandName] <string[]>] [[-FormatTypeName] <string[]>] [-Prefix <string>] [-DisableNameChecking] [-AllowClobber] [-ArgumentList <Object[]>] [-CommandType <CommandTypes>] [-Module <string[]>] [-FullyQualifiedModule <ModuleSpecification[]>] [-Certificate <X509Certificate2>] [<CommonParameters>]
```

**Arguments**

- `-Prefix` <String> — Specifies a prefix to the nouns in the names of imported commands.
- `-DisableNameChecking` switch — Indicates that this cmdlet suppresses the message that warns you when you import a cmdlet or function whose name includes an unapproved verb or a prohibited character.
- `-CommandName` <String[]> · position 2 · alias -Name — Specifies commands with the specified names or name patterns. Wildcards are permitted.
- `-AllowClobber` switch — Indicates that this cmdlet imports the specified commands, even if they have the same names as commands in the current session.
- `-ArgumentList` <Object[]> · alias -Args — Specifies an array of commands that results from using the specified arguments (parameter values).
- `-CommandType` <CommandTypes> · alias -Type — Specifies the type of command objects. The default value is Cmdlet.
- `-Module` <String[]> · alias -PSSnapin — Specifies and array of commands in the Windows PowerShell snap-ins and modules.
- `-FullyQualifiedModule` <ModuleSpecification[]> — The value can be a module name, a full module specification, or a path to a module file.
- `-FormatTypeName` <String[]> · position 3 — Specifies formatting instructions for the specified Microsoft .NET Framework types.
- `-Certificate` <X509Certificate2> — Specifies the client certificate that is used to sign the format files (*.Format.ps1xml) or script module files (.psm1) in the temporary module that Import-PSSession creates.
- `-Session` <PSSession> · **required** · position 0 — Specifies the PSSession from which the cmdlets are imported. Enter a variable that contains a session object or a command that gets a session object, such as a New-PSSession or Get-PSSession command.

**Output:** PSModuleInfo

**Example**

```powershell
Import-PSSession -Session $s -Module ActiveDirectory -Prefix Rem
```

### Invoke-Expression (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: iex

Runs a string as a PowerShell command; avoid with untrusted input. Alias: iex.

**Syntax** (one line per parameter set)

```powershell
Invoke-Expression [-Command] <string> [<CommonParameters>]
```

**Arguments**

- `-Command` <String> · **required** · position 0 · pipeline by value — Specifies the command or expression to run. Type the command or expression or enter a variable that contains the command or expression.

**Output:** None

**Example**

```powershell
Invoke-Expression 'Get-Date'
```

### Invoke-RestMethod (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: irm

Sends an HTTP(S) request to a REST API and turns JSON/XML responses into objects. Alias: irm.

**Syntax** (one line per parameter set)

```powershell
Invoke-RestMethod [-Uri] <uri> [-Method <WebRequestMethod>] [-UseBasicParsing] [-WebSession <WebRequestSession>] [-SessionVariable <string>] [-Credential <pscredential>] [-UseDefaultCredentials] [-CertificateThumbprint <string>] [-Certificate <X509Certificate>] [-UserAgent <string>] [-DisableKeepAlive] [-TimeoutSec <int>] [-Headers <IDictionary>] [-MaximumRedirection <int>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-ProxyUseDefaultCredentials] [-Body <Object>] [-ContentType <string>] [-TransferEncoding <string>] [-InFile <string>] [-OutFile <string>] [-PassThru] [<CommonParameters>]
```

**Arguments**

- `-Method` <WebRequestMethod> · default Default — Specifies the method used for the web request.
- `-UseBasicParsing` switch — Indicates that the cmdlet uses basic parsing. The cmdlet returns the raw HTML in a String object.
- `-Uri` <Uri> · **required** · position 0 — Specifies the Uniform Resource Identifier (URI) of the internet resource to which the web request is sent.
- `-WebSession` <WebRequestSession> — Specifies a web request session. Enter the variable name, including the dollar sign ($).
- `-SessionVariable` <String> · alias -SV — Creates a variable containing the web request session. Enter a variable name without the dollar sign ($) symbol.
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to send the request. The default is the current user.
- `-UseDefaultCredentials` switch — Uses the credentials of the current user to send the web request.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to send the request.
- `-Certificate` <X509Certificate> — Specifies the client certificate that's used for a secure web request.
- `-UserAgent` <String> — Specifies a user agent string for the web request.
- `-DisableKeepAlive` switch — Sets the KeepAlive value in the HTTP header to False. By default, KeepAlive is True.
- `-TimeoutSec` <Int32> · default 0 — Specifies how long the request can be pending before it times out. Enter a value in seconds.
- `-Headers` <IDictionary> — Specifies the headers of the web request. Enter a hash table or dictionary.
- `-MaximumRedirection` <Int32> · default 5 — Determines how many times Windows PowerShell redirects a connection to an alternate Uniform Resource Identifier (URI) before the connection fails.
- `-Proxy` <Uri> — Uses a proxy server for the request, rather than connecting directly to the Internet resource.
- `-ProxyCredential` <PSCredential> · default Current user — Specifies a user account that has permission to use the proxy server that's specified by the Proxy parameter.
- `-ProxyUseDefaultCredentials` switch — Uses the credentials of the current user to access the proxy server that is specified by the Proxy parameter.
- `-Body` <Object> · pipeline by value — Specifies the body of the request. The body is the content of the request that follows the headers.
- `-ContentType` <String> — Specifies the content type of the web request.
- `-TransferEncoding` <String> · values: chunked, compress, deflate, gzip, identity — Specifies a value for the transfer-encoding HTTP response header.
- `-InFile` <String> — Gets the content of the web request body from a file. Enter a path and filename.
- `-OutFile` <String> — Saves the response body in the specified output file. Enter a path and file name.
- `-PassThru` switch — This parameter is valid only when the OutFile parameter is also used in the command.

**Output:** Int64; String; XmlDocument; PSObject

**Example**

```powershell
Invoke-RestMethod -Uri https://api.example.com/items -Method Get
```

### Invoke-WebRequest (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: curl, iwr, wget

Sends an HTTP(S) request and returns the full response (status, headers, content); can download files. Aliases: iwr, curl/wget (5.1).

**Syntax** (one line per parameter set)

```powershell
Invoke-WebRequest [-Uri] <uri> [-UseBasicParsing] [-WebSession <WebRequestSession>] [-SessionVariable <string>] [-Credential <pscredential>] [-UseDefaultCredentials] [-CertificateThumbprint <string>] [-Certificate <X509Certificate>] [-UserAgent <string>] [-DisableKeepAlive] [-TimeoutSec <int>] [-Headers <IDictionary>] [-MaximumRedirection <int>] [-Method <WebRequestMethod>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-ProxyUseDefaultCredentials] [-Body <Object>] [-ContentType <string>] [-TransferEncoding <string>] [-InFile <string>] [-OutFile <string>] [-PassThru] [<CommonParameters>]
```

**Arguments**

- `-UseBasicParsing` switch — Indicates that the cmdlet uses the response object for HTML content without Document Object Model (DOM) parsing.
- `-Uri` <Uri> · **required** · position 0 — Specifies the Uniform Resource Identifier (URI) of the Internet resource to which the web request is sent.
- `-WebSession` <WebRequestSession> — Specifies a web request session. Enter the variable name, including the dollar sign ($).
- `-SessionVariable` <String> · alias -SV — Specifies a variable for which this cmdlet creates a web request session and saves it in the value.
- `-Credential` <PSCredential> — Specifies a user account that has permission to send the request. The default is the current user.
- `-UseDefaultCredentials` switch — Indicates that the cmdlet uses the credentials of the current user to send the web request.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to send the request.
- `-Certificate` <X509Certificate> — Specifies the client certificate that's used for a secure web request.
- `-UserAgent` <String> — Specifies a user agent string for the web request. The default user agent is similar to Mozilla/5.0 (Windows NT; Windows NT 6.1; en-US) WindowsPowerShell/3.0 with slight variations for each operating system and platform.
- `-DisableKeepAlive` switch — Indicates that the cmdlet sets the KeepAlive value in the HTTP header to False.
- `-TimeoutSec` <Int32> — Specifies how long the request can be pending before it times out. Enter a value in seconds.
- `-Headers` <IDictionary> — Specifies the headers of the web request. Enter a hash table or dictionary.
- `-MaximumRedirection` <Int32> — Specifies how many times PowerShell redirects a connection to an alternate Uniform Resource Identifier (URI) before the connection fails.
- `-Method` <WebRequestMethod> — Specifies the method used for the web request.
- `-Proxy` <Uri> — Specifies a proxy server for the request, rather than connecting directly to the Internet resource.
- `-ProxyCredential` <PSCredential> — Specifies a user account that has permission to use the proxy server specified by the Proxy parameter.
- `-ProxyUseDefaultCredentials` switch — Indicates that the cmdlet uses the credentials of the current user to access the proxy server that is specified by the Proxy parameter.
- `-Body` <Object> · pipeline by value — Specifies the body of the request. The body is the content of the request that follows the headers.
- `-ContentType` <String> — Specifies the content type of the web request.
- `-TransferEncoding` <String> · values: chunked, compress, deflate, gzip, identity — Specifies a value for the transfer-encoding HTTP response header.
- `-InFile` <String> — Gets the content of the web request body from a file. Enter a path and filename.
- `-OutFile` <String> — Specifies the output file for which this cmdlet saves the response body.
- `-PassThru` switch — Indicates that the cmdlet returns the results, in addition to writing them to a file.

**Output:** HtmlWebResponseObject

**Example**

```powershell
Invoke-WebRequest -Uri https://example.com/file.zip -OutFile .\file.zip
```

### Measure-Command (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Times how long a script block takes to run.

**Syntax** (one line per parameter set)

```powershell
Measure-Command [-Expression] <scriptblock> [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Objects bound to the InputObject parameter are optional input for the scriptblock passed to the Expression parameter.
- `-Expression` <ScriptBlock> · **required** · position 0 — Specifies the expression that is being timed. Enclose the expression in braces ({}).

**Output:** TimeSpan

**Example**

```powershell
Measure-Command { Get-ChildItem C:\Windows -Recurse -ErrorAction SilentlyContinue }
```

### Measure-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: measure

Counts objects and computes sum, average, min and max of a property, or lines/words/characters of text. Alias: measure.

**Syntax** (one line per parameter set)

```powershell
Measure-Object [[-Property] <string[]>] [-InputObject <psobject>] [-Sum] [-Average] [-Maximum] [-Minimum] [<CommonParameters>]

Measure-Object [[-Property] <string[]>] [-InputObject <psobject>] [-Line] [-Word] [-Character] [-IgnoreWhiteSpace] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be measured. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-Property` <String[]> · position 0 · wildcards — Specifies one or more properties to measure. If you do not specify any other measures, Measure-Object counts the objects that have the properties you specify.
- `-Sum` switch — Indicates that the cmdlet displays the sum of the values of the specified properties.
- `-Average` switch — Indicates that the cmdlet displays the average value of the specified properties.
- `-Maximum` switch — Indicates that the cmdlet displays the maximum value of the specified properties.
- `-Minimum` switch — Indicates that the cmdlet displays the minimum value of the specified properties.
- `-Line` switch — Indicates that the cmdlet counts the number of lines in the input objects.
- `-Word` switch — Indicates that the cmdlet counts the number of words in the input objects.
- `-Character` switch — Indicates that the cmdlet counts the number of characters in the input objects.
- `-IgnoreWhiteSpace` switch — Indicates that the cmdlet ignores white space in character counts. By default, white space is not ignored.

**Output:** GenericMeasureInfo; TextMeasureInfo

**Example**

```powershell
Get-ChildItem -File | Measure-Object Length -Sum -Average
```

### New-Alias (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: nal

Creates a new alias for a command. Alias: nal.

**Syntax** (one line per parameter set)

```powershell
New-Alias [-Name] <string> [-Value] <string> [-Description <string>] [-Option <ScopedItemOptions>] [-PassThru] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by property name — Specifies the new alias. You can use any alphanumeric characters in an alias, but the first character cannot be a number.
- `-Value` <String> · **required** · position 1 · pipeline by property name — Specifies the name of the cmdlet or command element that is being aliased.
- `-Description` <String> — Specifies a description of the alias. You can type any string.
- `-Option` <ScopedItemOptions> · default [System.Management.Automation.ScopedItemOptions]::None — Specifies the value of the Options property of the alias.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Scope` <String> — Specifies the scope of the new alias.
- `-Force` switch — Indicates that the cmdlet acts like Set-Alias if the alias named already exists.

**Output:** None; AliasInfo

**Example**

```powershell
New-Alias -Name np -Value notepad.exe
```

### New-Event (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Raises a custom event into the session's event queue.

**Syntax** (one line per parameter set)

```powershell
New-Event [-SourceIdentifier] <string> [[-Sender] <psobject>] [[-EventArguments] <psobject[]>] [[-MessageData] <psobject>] [<CommonParameters>]
```

**Arguments**

- `-SourceIdentifier` <String> · **required** · position 0 — Specifies a name for the new event. This parameter is required, and it must be unique in the session.
- `-Sender` <PSObject> · position 1 — Specifies the object that raises the event. The default is the PowerShell engine.
- `-EventArguments` <PSObject[]> · position 2 — Specifies an object that contains options for the event.
- `-MessageData` <PSObject> · position 3 — Specifies additional data associated with the event. The value of this parameter appears in the MessageData property of the event object.

**Output:** PSEventArgs

**Example**

```powershell
New-Event -SourceIdentifier Custom -MessageData 'hello'
```

### New-Guid (PowerShell 5.1 function)

Module Microsoft.PowerShell.Utility

Creates a new random GUID.

**Syntax** (one line per parameter set)

```powershell
New-Guid [<CommonParameters>]
```

**Output:** Guid

**Example**

```powershell
(New-Guid).Guid
```

### New-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Creates an instance of a .NET or COM object.

**Syntax** (one line per parameter set)

```powershell
New-Object [-TypeName] <string> [[-ArgumentList] <Object[]>] [-Property <IDictionary>] [<CommonParameters>]

New-Object [-ComObject] <string> [-Strict] [-Property <IDictionary>] [<CommonParameters>]
```

**Arguments**

- `-TypeName` <String> · **required** · position 0 — Specifies the fully qualified name of the .NET Framework class. You cannot specify both the TypeName parameter and the ComObject parameter.
- `-ComObject` <String> · **required** · position 0 — Specifies the programmatic identifier (ProgID) of the COM object.
- `-ArgumentList` <Object[]> · position 1 · alias -Args — Specifies an array of arguments to pass to the constructor of the .NET Framework class.
- `-Strict` switch — Indicates that the cmdlet generates a non-terminating error when a COM object that you attempt to create uses an interop assembly.
- `-Property` <IDictionary> — Sets property values and invokes methods of the new object.

**Output:** Object

**Example**

```powershell
$list = New-Object System.Collections.Generic.List[string]
```

### New-TemporaryFile (PowerShell 5.1 function)

Module Microsoft.PowerShell.Utility

Creates an empty temporary file in the user's temp folder and returns it.

**Syntax** (one line per parameter set)

```powershell
New-TemporaryFile [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Output:** FileInfo

**Example**

```powershell
$tmp = New-TemporaryFile
```

### New-TimeSpan (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Creates a TimeSpan (a length of time) from parts or between two dates.

**Syntax** (one line per parameter set)

```powershell
New-TimeSpan [[-Start] <datetime>] [[-End] <datetime>] [<CommonParameters>]

New-TimeSpan [-Days <int>] [-Hours <int>] [-Minutes <int>] [-Seconds <int>] [<CommonParameters>]
```

**Arguments**

- `-Start` <DateTime> · position 0 · pipeline by value and by property name · alias -LastWriteTime · default Current date and time — Specifies the start of a time span. Enter a string that represents the date and time, such as "3/15/09" or a DateTime object, such as one from a Get-Date command.
- `-End` <DateTime> · position 1 · pipeline by property name · default Current date and time — Specifies the end of a time span. The default value is the current date and time.
- `-Days` <Int32> — Specifies the days in the time span. The default value is 0.
- `-Hours` <Int32> — Specifies the hours in the time span. The default value is zero.
- `-Minutes` <Int32> — Specifies the minutes in the time span. The default value is 0.
- `-Seconds` <Int32> — Specifies the length of the time span in seconds. The default value is 0.

**Output:** TimeSpan

**Example**

```powershell
New-TimeSpan -Start (Get-Date '2026-01-01') -End (Get-Date)
```

### New-Variable (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: nv

Creates a variable, optionally read-only, constant or in a given scope. Alias: nv.

**Syntax** (one line per parameter set)

```powershell
New-Variable [-Name] <string> [[-Value] <Object>] [-Description <string>] [-Option <ScopedItemOptions>] [-Visibility <SessionStateEntryVisibility>] [-Force] [-PassThru] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by property name — Specifies a name for the new variable.
- `-Value` <Object> · position 1 · pipeline by value and by property name — Specifies the initial value of the variable.
- `-Description` <String> — Specifies a description of the variable.
- `-Option` <ScopedItemOptions> — Specifies the value of the Options property of the variable.
- `-Visibility` <SessionStateEntryVisibility> — Determines whether the variable is visible outside of the session in which it was created.
- `-Force` switch — Indicates that the cmdlet creates a variable with the same name as an existing read-only variable.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-Scope` <String> — Specifies the scope of the new variable.

**Output:** None; PSVariable

**Example**

```powershell
New-Variable -Name MaxItems -Value 100 -Option ReadOnly
```

### Out-File (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Sends output to a text file, as it would appear in the console.

**Syntax** (one line per parameter set)

```powershell
Out-File [-FilePath] <string> [[-Encoding] <string>] [-Append] [-Force] [-NoClobber] [-Width <int>] [-NoNewline] [-InputObject <psobject>] [-WhatIf] [-Confirm] [<CommonParameters>]

Out-File [[-Encoding] <string>] -LiteralPath <string> [-Append] [-Force] [-NoClobber] [-Width <int>] [-NoNewline] [-InputObject <psobject>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-FilePath` <String> · **required** · position 0 — Specifies the path to the output file.
- `-LiteralPath` <String> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the output file. The LiteralPath parameter is used exactly as it's typed.
- `-Encoding` <String> · position 1 · values: unknown, string, unicode, bigendianunicode, utf8, utf7, utf32, ascii, default, oem · default Unicode — Specifies the type of encoding for the target file. The default value is unicode.
- `-Append` switch — Adds the output to the end of an existing file. If no Encoding is specified, the cmdlet uses the default encoding.
- `-Force` switch — Overrides the read-only attribute and overwrites an existing read-only file.
- `-NoClobber` switch · alias -NoOverwrite — NoClobber prevents an existing file from being overwritten and displays a message that the file already exists.
- `-Width` <Int32> — Specifies the maximum number of characters in each line of output. Any additional characters are truncated, not wrapped.
- `-NoNewline` switch — Specifies that the content written to the file doesn't end with a newline character.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be written to the file. Enter a variable that contains the objects or type a command or expression that gets the objects.

**Output:** None

**Example**

```powershell
Get-Process | Out-File -FilePath .\procs.txt -Encoding utf8
```

### Out-GridView (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: ogv

Shows output in an interactive, filterable grid window; -PassThru returns the rows you select. Alias: ogv.

**Syntax** (one line per parameter set)

```powershell
Out-GridView [-InputObject <psobject>] [-Title <string>] [-PassThru] [<CommonParameters>]

Out-GridView [-InputObject <psobject>] [-Title <string>] [-Wait] [<CommonParameters>]

Out-GridView [-InputObject <psobject>] [-Title <string>] [-OutputMode <OutputModeOption>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies object that the cmdlet accepts as input for Out-GridView.
- `-Title` <String> — Specifies the text that appears in the title bar of the Out-GridView window.
- `-Wait` switch — Indicates that the cmdlet suppresses the command prompt and prevents Windows PowerShell from closing until the Out-GridView window is closed.
- `-OutputMode` <OutputModeOption> — Specifies the items that the interactive window sends down the pipeline as input to other commands.
- `-PassThru` switch — Indicates that the cmdlet sends items from the interactive window down the pipeline as input to other commands.

**Output:** None; PSObject

**Example**

```powershell
Get-Service | Out-GridView -PassThru | Restart-Service
```

### Out-Printer (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: lp

(Windows PowerShell only) Sends output to the default or a named printer.

**Syntax** (one line per parameter set)

```powershell
Out-Printer [[-Name] <string>] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · position 0 · alias -PrinterName — Sends the output to the specified printer. The parameter name Name is optional.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be sent to the printer. Enter a variable that contains the objects, or type a command or expression that gets the objects.

**Output:** None

**Example**

```powershell
Get-Content .\report.txt | Out-Printer
```

### Out-String (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Turns objects into the text they would display as (one string, or lines with -Stream).

**Syntax** (one line per parameter set)

```powershell
Out-String [-Stream] [-Width <int>] [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Stream` switch — By default, Out-String outputs a single string formatted as you would see it in the console including any blank headers or trailing newlines.
- `-Width` <Int32> — Specifies the number of characters in each line of output. Any additional characters are wrapped to the next line or truncated depending on the formatter cmdlet used.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be written to a string. Enter a variable that contains the objects, or type a command or expression that gets the objects.

**Output:** String

**Example**

```powershell
$text = Get-Process | Out-String
```

### Read-Host (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Reads a line of input from the user; -AsSecureString hides it.

**Syntax** (one line per parameter set)

```powershell
Read-Host [[-Prompt] <Object>] [-AsSecureString] [<CommonParameters>]
```

**Arguments**

- `-Prompt` <Object> · position 0 — Specifies the text of the prompt. Type a string.
- `-AsSecureString` switch — Indicates that the cmdlet displays asterisks (*) in place of the characters that the user types as input.

**Output:** String; SecureString

**Example**

```powershell
$name = Read-Host -Prompt 'Your name'
```

### Register-EngineEvent (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Subscribes to PowerShell engine events (such as exiting) and New-Event events.

**Syntax** (one line per parameter set)

```powershell
Register-EngineEvent [-SourceIdentifier] <string> [[-Action] <scriptblock>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

**Arguments**

- `-SourceIdentifier` <String> · **required** · position 100 — Specifies the source identifier of the event to which you are subscribing.
- `-Action` <ScriptBlock> · position 101 — Specifies commands to handle the events. The commands in the Action run when an event is raised, instead of sending the event to the event queue.
- `-MessageData` <PSObject> — This parameter is part of the base class for all Event cmdlets. The Register-EngineEvent doesn't use this parameter.
- `-SupportEvent` switch — Indicates that the cmdlet hides the event subscription. Add this parameter when the current subscription is part of a more complex event registration mechanism and it should not be discovered independently.
- `-Forward` switch — Indicates that the cmdlet sends events for this subscription to the session on the local computer.
- `-MaxTriggerCount` <Int32> — Specifies the maximum number of times that the action is executed for the event subscription.

**Output:** None; PSEventJob

**Example**

```powershell
Register-EngineEvent -SourceIdentifier PowerShell.Exiting -Action { 'bye' | Out-File .\exit.txt }
```

### Register-ObjectEvent (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Subscribes to an event of a .NET object, such as a timer or file watcher.

**Syntax** (one line per parameter set)

```powershell
Register-ObjectEvent [-InputObject] <psobject> [-EventName] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · **required** · position 0 — Specifies the .NET object that generates the events. Enter a variable that contains the object, or type a command or expression that gets the object.
- `-EventName` <String> · **required** · position 1 — Specifies the event to which you are subscribing.
- `-SourceIdentifier` <String> · position 100 — Specifies a name that you select for the subscription. The name that you select must be unique in the current session.
- `-Action` <ScriptBlock> · position 101 — Specifies the commands to handle the event. The commands in the Action run when an event is raised, instead of sending the event to the event queue.
- `-MessageData` <PSObject> — Specifies any additional data to be associated with this event subscription.
- `-SupportEvent` switch — Indicates that the cmdlet hides the event subscription. Use this parameter when the current subscription is part of a more complex event registration mechanism and should not be discovered independently.
- `-Forward` switch — Indicates that the cmdlet sends events for this subscription to a remote session.
- `-MaxTriggerCount` <Int32> — Specifies the maximum number of times an event can be triggered.

**Output:** None; PSEventJob

**Example**

```powershell
$t = New-Object Timers.Timer 1000; Register-ObjectEvent $t Elapsed -Action { Write-Host tick }; $t.Start()
```

### Remove-Event (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Deletes events from the session's event queue.

**Syntax** (one line per parameter set)

```powershell
Remove-Event [-SourceIdentifier] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-Event [-EventIdentifier] <int> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-SourceIdentifier` <String> · **required** · position 0 — Specifies the source identifier for which this cmdlet deletes events from.
- `-EventIdentifier` <Int32> · **required** · position 0 · pipeline by property name — Specifies the event identifier for which the cmdlet deletes. An EventIdentifier or SourceIdentifier parameter is required in every command.

**Output:** None

**Example**

```powershell
Remove-Event -SourceIdentifier ProcStart
```

### Remove-PSBreakpoint (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: rbp

Deletes breakpoints. Alias: rbp.

**Syntax** (one line per parameter set)

```powershell
Remove-PSBreakpoint [-Breakpoint] <Breakpoint[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-PSBreakpoint [-Id] <int[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Breakpoint` <Breakpoint[]> · **required** · position 0 · pipeline by value — Specifies the breakpoints to delete. Enter a variable that contains breakpoint objects or a command that gets breakpoint objects, such as a Get-PSBreakpoint command.
- `-Id` <Int32[]> · **required** · position 0 · pipeline by property name — Specifies breakpoint IDs for which this cmdlet deletes breakpoints.

**Output:** None

**Example**

```powershell
Get-PSBreakpoint | Remove-PSBreakpoint
```

### Remove-TypeData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Removes extended type data from the session.

**Syntax** (one line per parameter set)

```powershell
Remove-TypeData -TypeData <TypeData> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-TypeData [-TypeName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-TypeData -Path <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-TypeName` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the types that this cmdlet deletes all extended type data for.
- `-Path` <String[]> · **required** — Specifies an array of files that this cmdlet deletes from the session extended type data.
- `-TypeData` <TypeData> · **required** · pipeline by value — Specifies the type data that this cmdlet deletes from the session. This parameter is required.

**Output:** None

**Example**

```powershell
Remove-TypeData -TypeName My.Custom.Type
```

### Remove-Variable (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: rv

Deletes a variable and its value. Alias: rv.

**Syntax** (one line per parameter set)

```powershell
Remove-Variable [-Name] <string[]> [-Include <string[]>] [-Exclude <string[]>] [-Force] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies the name of the variable to be removed. The parameter name (Name) is optional.
- `-Include` <String[]> · wildcards — Specifies an array of items that this cmdlet deletes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies an array of items that this cmdlet omits from the operation.
- `-Force` switch — Indicates that the cmdlet removes a variable even if it is read-only. Even using the Force parameter, the cmdlet cannot remove a constant.
- `-Scope` <String> — Gets only the variables in the specified scope.

**Output:** None

**Example**

```powershell
Remove-Variable -Name temp
```

### Select-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: select

Picks properties of objects (including calculated ones), or the first/last/unique objects. Alias: select.

**Syntax** (one line per parameter set)

```powershell
Select-Object [[-Property] <Object[]>] [-InputObject <psobject>] [-ExcludeProperty <string[]>] [-ExpandProperty <string>] [-Unique] [-Last <int>] [-First <int>] [-Skip <int>] [-Wait] [<CommonParameters>]

Select-Object [[-Property] <Object[]>] [-InputObject <psobject>] [-ExcludeProperty <string[]>] [-ExpandProperty <string>] [-Unique] [-SkipLast <int>] [<CommonParameters>]

Select-Object [-InputObject <psobject>] [-Unique] [-Wait] [-Index <int[]>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies objects to send to the cmdlet through the pipeline. This parameter enables you to pipe objects to Select-Object.
- `-Property` <Object[]> · position 0 · wildcards — Specifies the properties to select. These properties are added as NoteProperty members to the output objects.
- `-ExcludeProperty` <String[]> · wildcards — Specifies the properties that this cmdlet excludes from the operation.
- `-ExpandProperty` <String> · wildcards — Specifies a property to select, and indicates that an attempt should be made to expand that property.
- `-Unique` switch — Specifies that if a subset of the input objects has identical properties and values, only a single member of the subset should be selected.
- `-Last` <Int32> — Specifies the number of objects to select from the end of a collection of input objects.
- `-First` <Int32> — Specifies the number of objects to select from the beginning of a collection of input objects.
- `-Skip` <Int32> — Skips (doesn't select) the specified number of items. By default, the Skip parameter counts from the beginning of the collection of objects.
- `-SkipLast` <Int32> — Skips (doesn't select) the specified number of items from the end of the list or array.
- `-Wait` switch — Indicates that the cmdlet turns off optimization. PowerShell runs commands in the order that they appear in the command pipeline and lets them generate all objects.
- `-Index` <Int32[]> — Selects objects from an array based on their index values. Enter the indexes in a comma-separated list.

**Output:** PSObject

**Example**

```powershell
Get-Process | Select-Object -First 5 Name, @{ Name = 'MB'; Expression = { $_.WorkingSet64 / 1MB } }
```

### Select-String (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: sls

Finds text matching a pattern in strings or files, like grep. Alias: sls.

**Syntax** (one line per parameter set)

```powershell
Select-String [-Pattern] <string[]> [-Path] <string[]> [-SimpleMatch] [-CaseSensitive] [-Quiet] [-List] [-Include <string[]>] [-Exclude <string[]>] [-NotMatch] [-AllMatches] [-Encoding <string>] [-Context <int[]>] [<CommonParameters>]

Select-String [-Pattern] <string[]> -InputObject <psobject> [-SimpleMatch] [-CaseSensitive] [-Quiet] [-List] [-Include <string[]>] [-Exclude <string[]>] [-NotMatch] [-AllMatches] [-Encoding <string>] [-Context <int[]>] [<CommonParameters>]

Select-String [-Pattern] <string[]> -LiteralPath <string[]> [-SimpleMatch] [-CaseSensitive] [-Quiet] [-List] [-Include <string[]>] [-Exclude <string[]>] [-NotMatch] [-AllMatches] [-Encoding <string>] [-Context <int[]>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · **required** · pipeline by value — Specifies the text to be searched. Enter a variable that contains the text, or type a command or expression that gets the text.
- `-Pattern` <String[]> · **required** · position 0 — Specifies the text to find on each line. The pattern value is treated as a regular expression.
- `-Path` <String[]> · **required** · position 1 · pipeline by property name · default Local directory · wildcards — Specifies the path to the files to search. Wildcards are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the files to be searched. The value of the LiteralPath parameter is used exactly as it's typed.
- `-SimpleMatch` switch — Indicates that the cmdlet uses a simple match rather than a regular expression match.
- `-CaseSensitive` switch — Indicates that the cmdlet matches are case-sensitive. By default, matches aren't case-sensitive.
- `-Quiet` switch — Indicates that the cmdlet returns a simple response instead of a MatchInfo object.
- `-List` switch — Only the first instance of matching text is returned from each input file.
- `-Include` <String[]> · wildcards — Includes the specified items. The value of this parameter qualifies the Path parameter.
- `-Exclude` <String[]> · wildcards — Exclude the specified items. The value of this parameter qualifies the Path parameter.
- `-NotMatch` switch — The NotMatch parameter finds text that doesn't match the specified pattern.
- `-AllMatches` switch — Indicates that the cmdlet searches for more than one match in each line of text.
- `-Encoding` <String> · values: unicode, utf7, utf8, utf32, ascii, bigendianunicode, default, oem · default Default — Specifies the type of encoding for the target file. The default value is default.
- `-Context` <Int32[]> — Captures the specified number of lines before and after the line that matches the pattern.

**Output:** MatchInfo; Boolean

**Example**

```powershell
Select-String -Path .\*.log -Pattern 'error' -SimpleMatch
```

### Select-Xml (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Finds nodes in XML documents or strings with an XPath query.

**Syntax** (one line per parameter set)

```powershell
Select-Xml [-XPath] <string> [-Xml] <XmlNode[]> [-Namespace <hashtable>] [<CommonParameters>]

Select-Xml [-XPath] <string> [-Path] <string[]> [-Namespace <hashtable>] [<CommonParameters>]

Select-Xml [-XPath] <string> -LiteralPath <string[]> [-Namespace <hashtable>] [<CommonParameters>]

Select-Xml [-XPath] <string> -Content <string[]> [-Namespace <hashtable>] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 1 · pipeline by property name · wildcards — Specifies the path and file names of the XML files to search. Wildcard characters are permitted.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the paths and file names of the XML files to search. Unlike Path, the value of the LiteralPath parameter is used exactly as it is typed.
- `-Xml` <XmlNode[]> · **required** · position 1 · pipeline by value and by property name · alias -Node — Specifies one or more XML nodes.
- `-Content` <String[]> · **required** · pipeline by value — Specifies a string that contains the XML to search. You can also pipe strings to Select-Xml.
- `-XPath` <String> · **required** · position 0 — Specifies an XPath search query. The query language is case-sensitive.
- `-Namespace` <Hashtable> — Specifies a hash table of the namespaces used in the XML. Use the format@{<namespaceName> = <namespaceValue>}.

**Output:** SelectXmlInfo

**Example**

```powershell
Select-Xml -Path .\app.config -XPath '//add[@key]' | ForEach-Object { $_.Node.key }
```

### Send-MailMessage (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Sends an email through an SMTP server (marked obsolete; works but is not recommended for new scripts).

**Syntax** (one line per parameter set)

```powershell
Send-MailMessage [-To] <string[]> [-Subject] <string> [[-Body] <string>] [[-SmtpServer] <string>] -From <string> [-Attachments <string[]>] [-Bcc <string[]>] [-BodyAsHtml] [-Encoding <Encoding>] [-Cc <string[]>] [-DeliveryNotificationOption <DeliveryNotificationOptions>] [-Priority <MailPriority>] [-Credential <pscredential>] [-UseSsl] [-Port <int>] [<CommonParameters>]
```

**Arguments**

- `-Attachments` <String[]> · pipeline by value · alias -PsPath — Specifies the path and file names of files to be attached to the email message.
- `-Bcc` <String[]> — Specifies the email addresses that receive a copy of the mail but aren't listed as recipients of the message.
- `-Body` <String> · position 2 — Specifies the content of the email message.
- `-BodyAsHtml` switch · alias -BAH — Specifies that the value of the Body parameter contains HTML.
- `-Encoding` <Encoding> · alias -BE · default Default — Specifies the type of encoding for the target file. The default value is Default.
- `-Cc` <String[]> — Specifies the email addresses to which a carbon copy (CC) of the email message is sent.
- `-DeliveryNotificationOption` <DeliveryNotificationOptions> · alias -DNO — Specifies the delivery notification options for the email message. You can specify multiple values.
- `-From` <String> · **required** — The From parameter is required. This parameter specifies the sender's email address.
- `-SmtpServer` <String> · position 3 · alias -ComputerName · default $PSEmailServer — Specifies the name of the SMTP server that sends the email message.
- `-Priority` <MailPriority> · default Normal — Specifies the priority of the email message. Normal is the default.
- `-Subject` <String> · **required** · position 1 · alias -sub — The Subject parameter isn't required. This parameter specifies the subject of the email message.
- `-To` <String[]> · **required** · position 0 — The To parameter is required. This parameter specifies the recipient's email address.
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to perform this action. The default is the current user.
- `-UseSsl` switch — The Secure Sockets Layer (SSL) protocol is used to establish a secure connection to the remote computer to send mail.
- `-Port` <Int32> · default 25 — Specifies an alternate port on the SMTP server. The default value is 25, which is the default SMTP port.

**Output:** None

**Example**

```powershell
Send-MailMessage -To ann@contoso.com -From bot@contoso.com -Subject Test -Body Hi -SmtpServer smtp.contoso.com
```

### Set-Alias (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: sal

Creates or changes an alias for a command. Alias: sal.

**Syntax** (one line per parameter set)

```powershell
Set-Alias [-Name] <string> [-Value] <string> [-Description <string>] [-Option <ScopedItemOptions>] [-PassThru] [-Scope <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by property name — Specifies the name of a new alias. An alias name can contain alphanumeric characters and hyphens.
- `-Value` <String> · **required** · position 1 · pipeline by property name — Specifies the name of the cmdlet or command that the alias runs. The Value parameter is the alias's Definition property.
- `-Description` <String> — Specifies a description of the alias. You can type any string.
- `-Option` <ScopedItemOptions> — Sets the Option property value of the alias. Values such as ReadOnly and Constant protect an alias from unintended changes.
- `-PassThru` switch — Returns an object that represents the alias. Use a format cmdlet such as Format-List to display the object.
- `-Scope` <String> · default Local — Specifies the scope this alias is valid in. The default value is Local.
- `-Force` switch — Use the Force parameter to change or delete an alias that has the Option parameter set to ReadOnly.

**Output:** None; AliasInfo

**Example**

```powershell
Set-Alias -Name ll -Value Get-ChildItem
```

### Set-Date (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Changes the system date and time (needs elevation).

**Syntax** (one line per parameter set)

```powershell
Set-Date [-Date] <datetime> [-DisplayHint <DisplayHintType>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-Date [-Adjust] <timespan> [-DisplayHint <DisplayHintType>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Date` <DateTime> · **required** · position 0 · pipeline by value and by property name — Changes the date and time to the specified values. You can type a new date in the short date format and a time in the standard time format for your locale.
- `-Adjust` <TimeSpan> · **required** · position 0 · pipeline by property name — Specifies the value for which this cmdlet adds or subtracts from the current date and time.
- `-DisplayHint` <DisplayHintType> — Specifies which elements of the date and time are displayed.

**Output:** DateTime

**Example**

```powershell
Set-Date -Adjust 00:05:00
```

### Set-PSBreakpoint (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: sbp

Sets a breakpoint on a script line, a command, or a variable read/write. Alias: sbp.

**Syntax** (one line per parameter set)

```powershell
Set-PSBreakpoint [-Script] <string[]> [-Line] <int[]> [[-Column] <int>] [-Action <scriptblock>] [<CommonParameters>]

Set-PSBreakpoint [[-Script] <string[]>] -Variable <string[]> [-Action <scriptblock>] [-Mode <VariableAccessMode>] [<CommonParameters>]

Set-PSBreakpoint [[-Script] <string[]>] -Command <string[]> [-Action <scriptblock>] [<CommonParameters>]
```

**Arguments**

- `-Action` <ScriptBlock> — Specifies commands that run at each breakpoint instead of breaking. Enter a scriptblock that contains the commands.
- `-Column` <Int32> · position 2 · default 1 — Specifies the column number of the column in the script file on which execution stops.
- `-Command` <String[]> · **required** · alias -C · wildcards — Sets a command breakpoint. Enter cmdlet names, such as Get-Process, or function names.
- `-Line` <Int32[]> · **required** · position 1 — Sets a line breakpoint in a script. Enter one or more line numbers, separated by commas.
- `-Script` <String[]> · **required** · position 0 — Specifies an array of script files that this cmdlet sets a breakpoint in.
- `-Variable` <String[]> · **required** · alias -V — Specifies an array of variables that this cmdlet sets breakpoints on. Enter a comma-separated list of variables without dollar signs ($).
- `-Mode` <VariableAccessMode> — Specifies the mode of access that triggers variable breakpoints. The default is Write.

**Output:** CommandBreakpoint; LineBreakpoint; VariableBreakpoint

**Example**

```powershell
Set-PSBreakpoint -Script .\deploy.ps1 -Line 42
```

### Set-TraceSource (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Configures, starts and stops tracing of PowerShell components.

**Syntax** (one line per parameter set)

```powershell
Set-TraceSource [-Name] <string[]> [[-Option] <PSTraceSourceOptions>] [-ListenerOption <TraceOptions>] [-FilePath <string>] [-Force] [-Debugger] [-PSHost] [-PassThru] [<CommonParameters>]

Set-TraceSource [-Name] <string[]> [-RemoveListener <string[]>] [<CommonParameters>]

Set-TraceSource [-Name] <string[]> [-RemoveFileListener <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies which components are traced. Enter the name of the trace source of each component.
- `-Option` <PSTraceSourceOptions> · position 1 · pipeline by property name — Specifies the type of events that are traced.
- `-ListenerOption` <TraceOptions> — Specifies optional data to the prefix of each trace message in the output.
- `-FilePath` <String> · alias -PSPath — Specifies a file that this cmdlet sends the trace output to. This parameter also selects the file trace listener.
- `-Force` switch — Indicates that the cmdlet overwrites a read-only file. Use with the FilePath parameter.
- `-Debugger` switch — Indicates that the cmdlet sends the trace output to the debugger. You can view the output in any user-mode or kernel mode debugger or in Microsoft Visual Studio.
- `-PSHost` switch — Indicates that this cmdlet sends the trace output to the PowerShell host.
- `-RemoveListener` <String[]> — Stops the trace by removing the trace listener.
- `-RemoveFileListener` <String[]> — Stops the trace by removing the file trace listener associated with the specified file.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None; PSTraceSource

**Example**

```powershell
Set-TraceSource -Name ParameterBinding -Option ExecutionFlow -PSHost
```

### Set-Variable (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: set, sv

Sets a variable's value (creating it if needed), scope or options. Alias: set, sv.

**Syntax** (one line per parameter set)

```powershell
Set-Variable [-Name] <string[]> [[-Value] <Object>] [-Include <string[]>] [-Exclude <string[]>] [-Description <string>] [-Option <ScopedItemOptions>] [-Force] [-Visibility <SessionStateEntryVisibility>] [-PassThru] [-Scope <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — Specifies the variable name.
- `-Value` <Object> · position 1 · pipeline by value and by property name — Specifies the value of the variable.
- `-Include` <String[]> · wildcards — Specifies an array of items that this cmdlet includes in the operation.
- `-Exclude` <String[]> · wildcards — Specifies an array of items that this cmdlet excludes from the operation.
- `-Description` <String> — Specifies the description of the variable.
- `-Option` <ScopedItemOptions> — Specifies the value of the Options property of the variable.
- `-Force` switch — Allows you to create a variable with the same name as an existing read-only variable, or to change the value of a read-only variable.
- `-Visibility` <SessionStateEntryVisibility> · default Public — Determines whether the variable is visible outside of the session in which it was created.
- `-PassThru` switch — Returns an object representing the new variable. By default, this cmdlet does not generate any output.
- `-Scope` <String> · default Local — Specifies the scope of the variable.The acceptable values for this parameter are:

**Output:** None; PSVariable

**Example**

```powershell
Set-Variable -Name count -Value 0 -Scope Script
```

### Show-Command (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: shcm

Opens a graphical window to build and run a command by filling in its parameters.

**Syntax** (one line per parameter set)

```powershell
Show-Command [[-Name] <string>] [-Height <double>] [-Width <double>] [-NoCommonParameter] [-ErrorPopup] [-PassThru] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · position 0 · alias -CommandName — Displays a command window for the specified command. Enter the name of one command, such as the name of a cmdlet, function, or CIM command.
- `-Height` <Double> — Specifies the height of the Show-Command window in pixels. Enter a value between 300 and the number of pixels in the screen resolution.
- `-Width` <Double> — Specifies the width of the Show-Command window in pixels. Enter a value between 300 and the number of pixels in the screen resolution.
- `-NoCommonParameter` switch — Indicates that this cmdlet omits the Common Parameters section of the command display.
- `-ErrorPopup` switch — Indicates that the cmdlet displays errors in a pop-up window, in addition to displaying them at the command line.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None; String; Object

**Example**

```powershell
Show-Command Get-ChildItem
```

### Sort-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: sort

Sorts objects by one or more properties, ascending or -Descending; -Unique removes duplicates. Alias: sort.

**Syntax** (one line per parameter set)

```powershell
Sort-Object [[-Property] <Object[]>] [-Descending] [-Unique] [-InputObject <psobject>] [-Culture <string>] [-CaseSensitive] [<CommonParameters>]
```

**Arguments**

- `-Descending` switch — Indicates that Sort-Object sorts the objects in descending order. The default is ascending order.
- `-Unique` switch — Indicates that Sort-Object eliminates duplicates and returns only the unique members of the collection.
- `-InputObject` <PSObject> · pipeline by value — To sort objects, send them down the pipeline to Sort-Object. If you use the InputObject parameter to submit a collection of items, Sort-Object receives one object that represents the collection.
- `-Property` <Object[]> · position 0 · default Default properties · wildcards — Specifies the property names that Sort-Object uses to sort the objects.
- `-Culture` <String> — Specifies the cultural configuration to use for sorts. Use Get-Culture to display the system's culture configuration.
- `-CaseSensitive` switch — Indicates that the sort is case-sensitive. By default, sorts aren't case-sensitive.

**Output:** PSObject

**Example**

```powershell
Get-ChildItem -File | Sort-Object Length -Descending
```

### Start-Sleep (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: sleep

Pauses for a number of seconds or milliseconds. Alias: sleep.

**Syntax** (one line per parameter set)

```powershell
Start-Sleep [-Seconds] <int> [<CommonParameters>]

Start-Sleep -Milliseconds <int> [<CommonParameters>]
```

**Arguments**

- `-Seconds` <Int32> · **required** · position 0 · pipeline by value and by property name — Specifies how long the resource sleeps in seconds. You can omit the parameter name or you can abbreviate it as s.
- `-Milliseconds` <Int32> · **required** · pipeline by property name — Specifies how long the resource sleeps in milliseconds. The parameter can be abbreviated as m.

**Output:** None

**Example**

```powershell
Start-Sleep -Seconds 5
```

### Tee-Object (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: tee

Saves pipeline output to a file or variable and also passes it on. Alias: tee.

**Syntax** (one line per parameter set)

```powershell
Tee-Object [-FilePath] <string> [-InputObject <psobject>] [-Append] [<CommonParameters>]

Tee-Object -LiteralPath <string> [-InputObject <psobject>] [<CommonParameters>]

Tee-Object -Variable <string> [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies the object to be saved and displayed. Enter a variable that contains the objects or type a command or expression that gets the objects.
- `-FilePath` <String> · **required** · position 0 · wildcards — Specifies a file that this cmdlet saves the object to Wildcard characters are permitted, but must resolve to a single file.
- `-LiteralPath` <String> · **required** · alias -PSPath — Specifies a file that this cmdlet saves the object to. Unlike FilePath, the value of the LiteralPath parameter is used exactly as it is typed.
- `-Append` switch — Indicates that the cmdlet appends the output to the specified file. Without this parameter, the new content replaces any existing content in the file without warning.
- `-Variable` <String> · **required** — Specifies a variable that the cmdlet saves the object to. Enter a variable name without the preceding dollar sign ($).

**Output:** PSObject

**Example**

```powershell
Get-Process | Tee-Object -FilePath .\procs.txt | Select-Object -First 3
```

### Trace-Command (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: trcm

Traces an expression or command, showing internal steps such as parameter binding.

**Syntax** (one line per parameter set)

```powershell
Trace-Command [-Name] <string[]> [-Expression] <scriptblock> [[-Option] <PSTraceSourceOptions>] [-InputObject <psobject>] [-ListenerOption <TraceOptions>] [-FilePath <string>] [-Force] [-Debugger] [-PSHost] [<CommonParameters>]

Trace-Command [-Name] <string[]> [-Command] <string> [[-Option] <PSTraceSourceOptions>] [-InputObject <psobject>] [-ArgumentList <Object[]>] [-ListenerOption <TraceOptions>] [-FilePath <string>] [-Force] [-Debugger] [-PSHost] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject> · pipeline by value — Specifies input to the expression that's being processed during the trace.
- `-Name` <String[]> · **required** · position 0 — Specifies an array of PowerShell components that are traced. Enter the name of the trace source of each component.
- `-Option` <PSTraceSourceOptions> · position 2 — Determines the type of events that are traced.
- `-Expression` <ScriptBlock> · **required** · position 1 — Specifies the expression that's being processed during the trace. Enclose the expression in braces ({}).
- `-Command` <String> · **required** · position 1 — Specifies a command that's being processed during the trace.
- `-ArgumentList` <Object[]> · alias -Args — Specifies the parameters and parameter values for the command being traced.
- `-ListenerOption` <TraceOptions> — Specifies optional data to the prefix of each trace message in the output.
- `-FilePath` <String> · alias -PSPath — Specifies a file that the cmdlet sends the trace output to. This parameter also selects the file trace listener.
- `-Force` switch — Forces the command to run without asking for user confirmation. Used with the FilePath parameter.
- `-Debugger` switch — Indicates that the cmdlet sends the trace output to the debugger. You can view the output in any user-mode or kernel mode debugger or in Visual Studio.
- `-PSHost` switch — Indicates that the cmdlet sends the trace output to the PowerShell host.

**Output:** PSObject

**Example**

```powershell
Trace-Command -Name ParameterBinding -Expression { Get-Item . } -PSHost
```

### Unblock-File (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Removes the downloaded-from-internet mark (Zone.Identifier) from files so scripts can run.

**Syntax** (one line per parameter set)

```powershell
Unblock-File [-Path] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Unblock-File -LiteralPath <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · wildcards — Specifies the files to unblock. Wildcard characters are supported.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the files to unblock. Unlike Path, the value of the LiteralPath parameter is used exactly as it is typed.

**Output:** None

**Example**

```powershell
Get-ChildItem .\Downloads\*.ps1 | Unblock-File
```

### Unregister-Event (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Cancels an event subscription.

**Syntax** (one line per parameter set)

```powershell
Unregister-Event [-SourceIdentifier] <string> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-Event [-SubscriptionId] <int> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-SourceIdentifier` <String> · **required** · position 0 · pipeline by property name — Specifies a source identifier that this cmdlet cancels event subscriptions.
- `-SubscriptionId` <Int32> · **required** · position 0 · pipeline by property name — Specifies a source identifier ID that this cmdlet cancels event subscriptions.
- `-Force` switch — Cancels all event subscriptions, including subscriptions that were hidden by using the SupportEvent parameter of Register-ObjectEvent, Register-WmiEvent, and Register-EngineEvent.

**Output:** None

**Example**

```powershell
Unregister-Event -SourceIdentifier ProcStart
```

### Update-FormatData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Reloads or adds formatting files (.format.ps1xml) in the session.

**Syntax** (one line per parameter set)

```powershell
Update-FormatData [[-AppendPath] <string[]>] [-PrependPath <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-AppendPath` <String[]> · position 0 · pipeline by value and by property name · alias -PSPath, -Path — Specifies formatting files that this cmdlet adds to the session. The files are loaded after PowerShell loads the built-in formatting files.
- `-PrependPath` <String[]> — Specifies formatting files that this cmdlet adds to the session. The files are loaded before PowerShell loads the built-in formatting files.

**Output:** None

**Example**

```powershell
Update-FormatData -PrependPath .\my.format.ps1xml
```

### Update-List (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Adds or removes items in a property that holds a collection.

**Syntax** (one line per parameter set)

```powershell
Update-List [[-Property] <string>] [-Add <Object[]>] [-Remove <Object[]>] [-InputObject <psobject>] [<CommonParameters>]

Update-List [[-Property] <string>] -Replace <Object[]> [-InputObject <psobject>] [<CommonParameters>]
```

**Arguments**

- `-Add` <Object[]> — Specifies the property values to be added to the collection. Enter the values in the order that they should appear in the collection.
- `-Remove` <Object[]> — Specifies the property values to be removed from the collection.
- `-Replace` <Object[]> · **required** — Specifies a new collection. This parameter replaces all items in the original collection with the items specified by this parameter.
- `-InputObject` <PSObject> · pipeline by value — Specifies the objects to be updated. You can also pipe the object to be updated to Update-List.
- `-Property` <String> · position 0 — Specifies the property that contains the collection that's being updated.

**Output:** Hashtable; Object

**Example**

```powershell
Get-Mailbox ann | Update-List -Property EmailAddresses -Add 'smtp:ann2@contoso.com' | Set-Mailbox
```

### Update-TypeData (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Adds extended type data (properties, methods) to types in the session.

**Syntax** (one line per parameter set)

```powershell
Update-TypeData [[-AppendPath] <string[]>] [-PrependPath <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-TypeData -TypeName <string> [-MemberType <PSMemberTypes>] [-MemberName <string>] [-Value <Object>] [-SecondValue <Object>] [-TypeConverter <type>] [-TypeAdapter <type>] [-SerializationMethod <string>] [-TargetTypeForDeserialization <type>] [-SerializationDepth <int>] [-DefaultDisplayProperty <string>] [-InheritPropertySerializationSet <bool>] [-StringSerializationSource <string>] [-DefaultDisplayPropertySet <string[]>] [-DefaultKeyPropertySet <string[]>] [-PropertySerializationSet <string[]>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-TypeData [-TypeData] <TypeData[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-MemberType` <PSMemberTypes> · values: NoteProperty, AliasProperty, ScriptProperty, CodeProperty, ScriptMethod, CodeMethod — Specifies the type of the member to add or change.
- `-MemberName` <String> — Specifies the name of a property or method.
- `-Value` <Object> — Specifies the value of the property or method.
- `-SecondValue` <Object> — Specifies additional values for AliasProperty, ScriptProperty, CodeProperty, or CodeMethod members.
- `-TypeConverter` <Type> — Specifies a type converter to convert values between different types. If a type converter is defined for a type, an instance of the type converter is used for the conversion.
- `-TypeAdapter` <Type> — Specifies the type of a type adapter, such as Microsoft.PowerShell.Cim.CimInstanceAdapter.
- `-SerializationMethod` <String> — Specifies a serialization method for the type. A serialization method determines which properties of the type are serialized and the technique that is used to serialize them.
- `-TargetTypeForDeserialization` <Type> — Specifies the type to which object of this type are converted when they are deserialized.
- `-SerializationDepth` <Int32> · default 1 — Specifies how many levels of type objects are serialized as strings. The default value 1 serializes the object and its properties.
- `-DefaultDisplayProperty` <String> — Specifies the property of the type that is displayed by the Format-Wide cmdlet when no other properties are specified.
- `-InheritPropertySerializationSet` <Nullable`1> — Indicates whether the set of properties that are serialized is inherited.
- `-StringSerializationSource` <String> — Specifies the name of a property of the type. The value of specified property is used as the serialization result.
- `-DefaultDisplayPropertySet` <String[]> — Specifies one or more properties of the type. These properties are displayed by the Format-List, Format-Table, and Format-Custom cmdlets when no other properties are specified.
- `-DefaultKeyPropertySet` <String[]> — Specifies one or more properties of the type. These properties are used by the Group-Object and Sort-Object cmdlets when no other properties are specified.
- `-PropertySerializationSet` <String[]> — Specifies the names of properties that are serialized. Use this parameter when the value of the SerializationMethod parameter is SpecificProperties.
- `-TypeName` <String> · **required** · pipeline by value — Specifies the name of the type to extend.
- `-Force` switch — Indicates that the cmdlet uses the specified type data, even if type data has already been specified for that type.
- `-TypeData` <TypeData[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of type data that this cmdlet adds to the session. Enter a variable that contains a TypeData object or a command that gets a TypeData object, such as a Get-TypeData command.
- `-AppendPath` <String[]> · position 0 · pipeline by value and by property name · alias -PSPath, -Path — Specifies the path to optional .ps1xml files. The specified files are loaded in the order that they are listed after the built-in files are loaded.
- `-PrependPath` <String[]> — Specifies the path to the optional .ps1xml files. The specified files are loaded in the order that they are listed before the built-in files are loaded.

**Output:** None

**Example**

```powershell
Update-TypeData -TypeName System.IO.FileInfo -MemberType ScriptProperty -MemberName SizeKB -Value { $this.Length / 1KB }
```

### Wait-Debugger (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Stops a script in the debugger before the next statement (a breakpoint in code).

**Syntax** (one line per parameter set)

```powershell
Wait-Debugger [<CommonParameters>]
```

**Output:** None

**Example**

```powershell
Wait-Debugger
```

### Wait-Event (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Waits until an event is raised (optionally for one source or with a timeout).

**Syntax** (one line per parameter set)

```powershell
Wait-Event [[-SourceIdentifier] <string>] [-Timeout <int>] [<CommonParameters>]
```

**Arguments**

- `-SourceIdentifier` <String> · position 0 · pipeline by property name — Specifies the source identifier that this cmdlet waits for events. By default, Wait-Event waits for any event.
- `-Timeout` <Int32> · alias -TimeoutSec · default -1 — Specifies the maximum time, in seconds, that Wait-Event waits for the event to occur.

**Output:** PSEventArgs

**Example**

```powershell
Wait-Event -SourceIdentifier Custom -Timeout 30
```

### Write-Debug (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Writes a debug message (shown when $DebugPreference or -Debug allows it).

**Syntax** (one line per parameter set)

```powershell
Write-Debug [-Message] <string> [<CommonParameters>]
```

**Arguments**

- `-Message` <String> · **required** · position 0 · pipeline by value · alias -Msg — Specifies the debug message to send to the console.

**Output:** None

**Example**

```powershell
Write-Debug "Value is $x"
```

### Write-Error (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Writes an error to the error stream (non-terminating unless -ErrorAction Stop).

**Syntax** (one line per parameter set)

```powershell
Write-Error [-Message] <string> [-Category <ErrorCategory>] [-ErrorId <string>] [-TargetObject <Object>] [-RecommendedAction <string>] [-CategoryActivity <string>] [-CategoryReason <string>] [-CategoryTargetName <string>] [-CategoryTargetType <string>] [<CommonParameters>]

Write-Error -Exception <Exception> [-Message <string>] [-Category <ErrorCategory>] [-ErrorId <string>] [-TargetObject <Object>] [-RecommendedAction <string>] [-CategoryActivity <string>] [-CategoryReason <string>] [-CategoryTargetName <string>] [-CategoryTargetType <string>] [<CommonParameters>]

Write-Error -ErrorRecord <ErrorRecord> [-RecommendedAction <string>] [-CategoryActivity <string>] [-CategoryReason <string>] [-CategoryTargetName <string>] [-CategoryTargetType <string>] [<CommonParameters>]
```

**Arguments**

- `-Exception` <Exception> · **required** — Specifies an exception object that represents the error. Use the properties of the object to describe the error.
- `-Message` <String> · **required** · position 0 · pipeline by value · alias -Msg — Specifies the message text of the error. If the text includes spaces or special characters, enclose it in quotation marks.
- `-ErrorRecord` <ErrorRecord> · **required** — Specifies an error record object that represents the error. Use the properties of the object to describe the error.
- `-Category` <ErrorCategory> · default NotSpecified — Specifies the category of the error. The default value is NotSpecified.
- `-ErrorId` <String> — Specifies an ID string to identify the error. The string should be unique to the error.
- `-TargetObject` <Object> — Specifies the object that was being processed when the error occurred.
- `-RecommendedAction` <String> — Specifies the action that the user should take to resolve or prevent the error.
- `-CategoryActivity` <String> · alias -Activity — Specifies the action that caused the error.
- `-CategoryReason` <String> · alias -Reason — Specifies how or why the activity caused the error.
- `-CategoryTargetName` <String> · alias -TargetName — Specifies the name of the object that was being processed when the error occurred.
- `-CategoryTargetType` <String> · alias -TargetType — Specifies the type of the object that was being processed when the error occurred.

**Output:** None

**Example**

```powershell
Write-Error -Message 'File not found' -Category ObjectNotFound
```

### Write-Host (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Writes text directly to the console with optional colors (not to the pipeline).

**Syntax** (one line per parameter set)

```powershell
Write-Host [[-Object] <Object>] [-NoNewline] [-Separator <Object>] [-ForegroundColor <ConsoleColor>] [-BackgroundColor <ConsoleColor>] [<CommonParameters>]
```

**Arguments**

- `-Object` <Object> · position 0 · pipeline by value — Objects to display in the host.
- `-NoNewline` switch — The string representations of the input objects are concatenated to form the output.
- `-Separator` <Object> — Specifies a separator string to insert between objects displayed by the host.
- `-ForegroundColor` <ConsoleColor> — Specifies the text color. There is no default.
- `-BackgroundColor` <ConsoleColor> — Specifies the background color. There is no default.

**Output:** None

**Example**

```powershell
Write-Host 'Done' -ForegroundColor Green
```

### Write-Information (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Writes a message to the information stream (stream 6), shown per $InformationPreference.

**Syntax** (one line per parameter set)

```powershell
Write-Information [-MessageData] <Object> [[-Tags] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-MessageData` <Object> · **required** · position 0 · alias -Msg — Specifies an informational message that you want to display to users as they run a script or command.
- `-Tags` <String[]> · position 1 — Specifies a simple string that you can use to sort and filter messages that you have added to the information stream with Write-Information.

**Output:** None

**Example**

```powershell
Write-Information 'Starting' -InformationAction Continue
```

### Write-Output (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility · aliases: echo, write

Sends objects down the pipeline (the default for unassigned values). Aliases: echo, write.

**Syntax** (one line per parameter set)

```powershell
Write-Output [-InputObject] <psobject[]> [-NoEnumerate] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value — Specifies the objects to send down the pipeline. Enter a variable that contains the objects, or type a command or expression that gets the objects.
- `-NoEnumerate` switch — By default, the Write-Output cmdlet always enumerates its output. The NoEnumerate parameter suppresses the default behavior, and prevents Write-Output from enumerating output.

**Output:** PSObject

**Example**

```powershell
Write-Output 'hello'
```

### Write-Progress (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Shows a progress bar with activity, status and percent complete.

**Syntax** (one line per parameter set)

```powershell
Write-Progress [-Activity] <string> [[-Status] <string>] [[-Id] <int>] [-PercentComplete <int>] [-SecondsRemaining <int>] [-CurrentOperation <string>] [-ParentId <int>] [-Completed] [-SourceId <int>] [<CommonParameters>]
```

**Arguments**

- `-Activity` <String> · **required** · position 0 — Specifies the first line of text in the heading above the status bar. This text describes the activity whose progress is being reported.
- `-Status` <String> · position 1 — Specifies the second line of text in the heading above the status bar.
- `-Id` <Int32> · position 2 — Specifies an ID that distinguishes each progress bar from the others. Use this parameter when you are creating more than one progress bar in a single command.
- `-PercentComplete` <Int32> — Specifies the percentage of the activity that's completed. Use the value -1 if the percentage complete is unknown or not applicable.
- `-SecondsRemaining` <Int32> — Specifies the projected number of seconds remaining until the activity is completed.
- `-CurrentOperation` <String> — Specifies the line of text below the progress bar. This text describes the operation that's currently taking place.
- `-ParentId` <Int32> — Specifies the parent activity of the current activity. Use the value -1 if the current activity has no parent activity.
- `-Completed` switch — Indicates whether the progress bar is visible. If this parameter is omitted, Write-Progress displays progress information.
- `-SourceId` <Int32> — Specifies the source of the record. You can use this in place of Id but can't be used with other parameters like ParentId.

**Output:** None

**Example**

```powershell
1..100 | ForEach-Object { Write-Progress -Activity Copying -PercentComplete $_; Start-Sleep -Milliseconds 20 }
```

### Write-Verbose (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Writes a verbose message (shown with -Verbose or $VerbosePreference = 'Continue').

**Syntax** (one line per parameter set)

```powershell
Write-Verbose [-Message] <string> [<CommonParameters>]
```

**Arguments**

- `-Message` <String> · **required** · position 0 · pipeline by value · alias -Msg — Specifies the message to display. This parameter is required.

**Output:** None

**Example**

```powershell
Write-Verbose 'Connecting to server' -Verbose
```

### Write-Warning (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Utility

Writes a warning message to the warning stream.

**Syntax** (one line per parameter set)

```powershell
Write-Warning [-Message] <string> [<CommonParameters>]
```

**Arguments**

- `-Message` <String> · **required** · position 0 · pipeline by value · alias -Msg — Specifies the warning message.

**Output:** None

**Example**

```powershell
Write-Warning 'Disk space is low'
```

## Commands: Microsoft.PowerShell.Security

### ConvertFrom-SecureString (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Turns a SecureString into an encrypted standard string (DPAPI by default, or with a key) for storing.

**Syntax** (one line per parameter set)

```powershell
ConvertFrom-SecureString [-SecureString] <securestring> [[-SecureKey] <securestring>] [<CommonParameters>]

ConvertFrom-SecureString [-SecureString] <securestring> [-Key <byte[]>] [<CommonParameters>]
```

**Arguments**

- `-SecureString` <SecureString> · **required** · position 0 · pipeline by value — Specifies the secure string to convert to an encrypted standard string.
- `-SecureKey` <SecureString> · position 1 — Specifies the encryption key as a secure string. The secure string value is converted to a byte array before being used as the key.
- `-Key` <Byte[]> — Specifies the encryption key as a byte array. If the key doesn't match the key used to encrypt the original secure string, the value returned is invalid.

**Output:** String

**Example**

```powershell
Read-Host -AsSecureString | ConvertFrom-SecureString | Set-Content .\pw.txt
```

### ConvertTo-SecureString (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Turns plain text or an encrypted standard string into a SecureString.

**Syntax** (one line per parameter set)

```powershell
ConvertTo-SecureString [-String] <string> [[-SecureKey] <securestring>] [<CommonParameters>]

ConvertTo-SecureString [-String] <string> [-AsPlainText] [-Force] [<CommonParameters>]

ConvertTo-SecureString [-String] <string> [-Key <byte[]>] [<CommonParameters>]
```

**Arguments**

- `-String` <String> · **required** · position 0 · pipeline by value — Specifies the string to convert to a secure string.
- `-AsPlainText` switch · position 1 — Specifies a plain text string to convert to a secure string. The secure string cmdlets help protect confidential text.
- `-Force` switch · position 2 — Confirms that you understand the implications of using the AsPlainText parameter and still want to use it.
- `-SecureKey` <SecureString> · position 1 — Specifies the encryption key used to convert the original secure string into the encrypted standard string.
- `-Key` <Byte[]> — Specifies the encryption key used to convert the original secure string into the encrypted standard string.

**Output:** SecureString

**Example**

```powershell
$pw = Get-Content .\pw.txt | ConvertTo-SecureString
```

### Get-Acl (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Gets the security descriptor (owner and access rules) of a file, folder or registry key.

**Syntax** (one line per parameter set)

```powershell
Get-Acl [[-Path] <string[]>] [-Audit] [-AllCentralAccessPolicies] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-UseTransaction] [<CommonParameters>]

Get-Acl -InputObject <psobject> [-Audit] [-AllCentralAccessPolicies] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-UseTransaction] [<CommonParameters>]

Get-Acl [-LiteralPath <string[]>] [-Audit] [-AllCentralAccessPolicies] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies the path to a resource. Get-Acl gets the security descriptor of the resource indicated by the path.
- `-InputObject` <PSObject> · **required** — Gets the security descriptor for the specified object. Enter a variable that contains the object or a command that gets the object.
- `-LiteralPath` <String[]> · pipeline by value and by property name · alias -PSPath — Specifies the path to a resource. Unlike Path, the value of the LiteralPath parameter is used exactly as it is typed.
- `-Audit` switch — Gets the audit data for the security descriptor from the system access control list (SACL).
- `-AllCentralAccessPolicies` switch — Gets information about all central access policies that are enabled on the computer.
- `-Filter` <String> · wildcards — Specifies a filter in the provider's format or language. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Gets only the specified items. The value of this parameter qualifies the Path parameter.
- `-Exclude` <String[]> · wildcards — Omits the specified items. The value of this parameter qualifies the Path parameter.

**Output:** FileSecurity; DirectorySecurity; RegistrySecurity

**Example**

```powershell
(Get-Acl C:\Data).Access | Format-Table IdentityReference, FileSystemRights
```

### Get-AuthenticodeSignature (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Gets the Authenticode (digital signature) status of a file.

**Syntax** (one line per parameter set)

```powershell
Get-AuthenticodeSignature [-FilePath] <string[]> [<CommonParameters>]

Get-AuthenticodeSignature -LiteralPath <string[]> [<CommonParameters>]

Get-AuthenticodeSignature -SourcePathOrExtension <string[]> -Content <byte[]> [<CommonParameters>]
```

**Arguments**

- `-FilePath` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path to the file to examine. Wildcards are permitted, but they must lead to a single file.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to the file being examined. Unlike FilePath, the value of the LiteralPath parameter is used exactly as it is typed.
- `-SourcePathOrExtension` <String[]> · **required** · pipeline by value and by property name — Path to the file or file type of the content for which the Authenticode signature is retrieved.
- `-Content` <Byte[]> · **required** · pipeline by property name — Contents of a file as a byte array for which the Authenticode signature is retrieved.

**Output:** Signature

**Example**

```powershell
Get-AuthenticodeSignature -FilePath .\script.ps1
```

### Get-CmsMessage (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Reads content encrypted in Cryptographic Message Syntax (CMS) format without decrypting it.

**Syntax** (one line per parameter set)

```powershell
Get-CmsMessage [-Content] <string> [<CommonParameters>]

Get-CmsMessage [-Path] <string> [<CommonParameters>]

Get-CmsMessage [-LiteralPath] <string> [<CommonParameters>]
```

**Arguments**

- `-Content` <String> · **required** · position 0 · pipeline by value — Specifies an encrypted string, or a variable containing an encrypted string.
- `-Path` <String> · **required** · position 1 — Specifies the path to encrypted content that you want to decrypt.
- `-LiteralPath` <String> · **required** · position 1 — Specifies the path to encrypted content that you want to get. Unlike Path, the value of LiteralPath is used exactly as it is typed.

**Example**

```powershell
Get-CmsMessage -Path .\secret.cms
```

### Get-Credential (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Prompts for a user name and password and returns a PSCredential object.

**Syntax** (one line per parameter set)

```powershell
Get-Credential [-Credential] <pscredential> [<CommonParameters>]

Get-Credential [[-UserName] <string>] -Message <string> [<CommonParameters>]
```

**Arguments**

- `-Credential` <PSCredential> · **required** · position 0 — Specifies a user name for the credential, such as User01 or Domain01\User01.
- `-Message` <String> · **required** — Specifies a message that appears in the authentication prompt. This parameter is designed for use in a function or script.
- `-UserName` <String> · position 0 · default None (blank) — Specifies a user name. The authentication prompt requests a password for the user name.

**Output:** PSCredential

**Example**

```powershell
$cred = Get-Credential -UserName CONTOSO\Ann -Message 'Server login'
```

### Get-ExecutionPolicy (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Shows the effective script execution policy, or the policy for each scope with -List.

**Syntax** (one line per parameter set)

```powershell
Get-ExecutionPolicy [[-Scope] <ExecutionPolicyScope>] [-List] [<CommonParameters>]
```

**Arguments**

- `-Scope` <ExecutionPolicyScope> · position 0 · pipeline by property name · default Effective execution policy — Specifies the scope that is affected by an execution policy.
- `-List` switch — Gets all execution policy values for the session. By default, Get-ExecutionPolicy gets only the effective execution policy.

**Output:** ExecutionPolicy

**Example**

```powershell
Get-ExecutionPolicy -List
```

### Get-PfxCertificate (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Reads a .pfx/.cer certificate file and returns the certificate.

**Syntax** (one line per parameter set)

```powershell
Get-PfxCertificate [-FilePath] <string[]> [<CommonParameters>]

Get-PfxCertificate -LiteralPath <string[]> [<CommonParameters>]
```

**Arguments**

- `-FilePath` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies the full path to the PFX file of the secured file. If you specify a value for this parameter, it is not necessary to type -FilePath at the command line.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — The full path to the PFX file of the secured file. Unlike FilePath, the value of the LiteralPath parameter is used exactly as it is typed.

**Output:** X509Certificate2

**Example**

```powershell
Get-PfxCertificate -FilePath .\cert.pfx
```

### New-FileCatalog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Creates a Windows catalog file (.cat) with hashes of files in a folder, for later integrity checks.

**Syntax** (one line per parameter set)

```powershell
New-FileCatalog [-CatalogFilePath] <string> [[-Path] <string[]>] [-CatalogVersion <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-CatalogVersion` <Int32> — Accepts 1.0 or 2.0 as possible values for specifying the catalog version.
- `-CatalogFilePath` <String> · **required** · position 0 · pipeline by value and by property name — The location and name of the catalog file (.cat) you are creating. If you specify only a folder path, the command creates a file named catalog.cat in that location.
- `-Path` <String[]> · position 1 · pipeline by value and by property name — Accepts a path or array of paths to files or folders that should be included in the catalog file.

**Output:** FileInfo

**Example**

```powershell
New-FileCatalog -Path .\MyModule -CatalogFilePath .\MyModule.cat -CatalogVersion 2.0
```

### Protect-CmsMessage (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Encrypts content with a certificate in CMS format.

**Syntax** (one line per parameter set)

```powershell
Protect-CmsMessage [-To] <CmsMessageRecipient[]> [-Content] <psobject> [[-OutFile] <string>] [<CommonParameters>]

Protect-CmsMessage [-To] <CmsMessageRecipient[]> [-Path] <string> [[-OutFile] <string>] [<CommonParameters>]

Protect-CmsMessage [-To] <CmsMessageRecipient[]> [-LiteralPath] <string> [[-OutFile] <string>] [<CommonParameters>]
```

**Arguments**

- `-To` <CmsMessageRecipient[]> · **required** · position 0 — Specifies one or more CMS message recipients, identified in any of the following formats:
- `-Content` <PSObject> · **required** · position 1 · pipeline by value — Specifies a PSObject that contains content that you want to encrypt.
- `-Path` <String> · **required** · position 1 — Specifies the path to content that you want to encrypt.
- `-LiteralPath` <String> · **required** · position 1 — Specifies the path to content that you want to encrypt. Unlike Path, the value of LiteralPath is used exactly as it is typed.
- `-OutFile` <String> · position 2 — Specifies the path and file name of a file to which you want to send the encrypted content.

**Example**

```powershell
Protect-CmsMessage -To .\recipient.cer -Content 'secret' -OutFile .\secret.cms
```

### Set-Acl (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Applies a security descriptor (owner, access rules) to a file, folder or registry key.

**Syntax** (one line per parameter set)

```powershell
Set-Acl [-Path] <string[]> [-AclObject] <Object> [[-CentralAccessPolicy] <string>] [-ClearCentralAccessPolicy] [-Passthru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-Acl [-InputObject] <psobject> [-AclObject] <Object> [-Passthru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]

Set-Acl [-AclObject] <Object> [[-CentralAccessPolicy] <string>] -LiteralPath <string[]> [-ClearCentralAccessPolicy] [-Passthru] [-Filter <string>] [-Include <string[]>] [-Exclude <string[]>] [-WhatIf] [-Confirm] [-UseTransaction] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Changes the security descriptor of the specified item. Enter the path to an item, such as a path to a file or registry key.
- `-InputObject` <PSObject> · **required** · position 0 · pipeline by property name — Changes the security descriptor of the specified object. Enter a variable that contains the object or a command that gets the object.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Changes the security descriptor of the specified item. Unlike Path, the value of the LiteralPath parameter is used exactly as it is typed.
- `-AclObject` <Object> · **required** · position 1 · pipeline by value — Specifies an ACL with the desired property values. Set-Acl changes the ACL of item specified by the Path or InputObject parameter to match the values in the specified security object.
- `-CentralAccessPolicy` <String> · position 2 · pipeline by property name — Establishes or changes the central access policy of the item. Enter the CAP ID or friendly name of a central access policy on the computer.
- `-ClearCentralAccessPolicy` switch — Removes the central access policy from the specified item.
- `-Passthru` switch — Returns an object that represents the security descriptor that was changed.
- `-Filter` <String> · wildcards — Specifies a filter in the provider's format or language. The value of this parameter qualifies the Path parameter.
- `-Include` <String[]> · wildcards — Changes only the specified items. The value of this parameter qualifies the Path parameter.
- `-Exclude` <String[]> · wildcards — Omits the specified items. The value of this parameter qualifies the Path parameter.

**Output:** None; FileSecurity

**Example**

```powershell
$acl = Get-Acl .\a.txt; Set-Acl -Path .\b.txt -AclObject $acl
```

### Set-AuthenticodeSignature (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Signs a script or file with a code-signing certificate.

**Syntax** (one line per parameter set)

```powershell
Set-AuthenticodeSignature [-FilePath] <string[]> [-Certificate] <X509Certificate2> [-IncludeChain <string>] [-TimestampServer <string>] [-HashAlgorithm <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-AuthenticodeSignature [-Certificate] <X509Certificate2> -LiteralPath <string[]> [-IncludeChain <string>] [-TimestampServer <string>] [-HashAlgorithm <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-AuthenticodeSignature [-Certificate] <X509Certificate2> -SourcePathOrExtension <string[]> -Content <byte[]> [-IncludeChain <string>] [-TimestampServer <string>] [-HashAlgorithm <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Certificate` <X509Certificate2> · **required** · position 1 — Specifies the certificate that will be used to sign the script or file.
- `-IncludeChain` <String> · values: signer, notroot, all · default NotRoot — Determines which certificates in the certificate trust chain are included in the digital signature.
- `-TimestampServer` <String> — Uses the specified time stamp server to add a time stamp to the signature.
- `-HashAlgorithm` <String> · default Null — Specifies the hashing algorithm that Windows uses to compute the digital signature for the file.
- `-Force` switch — Allows the cmdlet to append a signature to a read-only file. Even using the Force parameter, the cmdlet can't override security restrictions.
- `-FilePath` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies the path to a file that's being signed.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path to a file that's being signed. Unlike FilePath, the value of the LiteralPath parameter is used exactly as it's typed.
- `-SourcePathOrExtension` <String[]> · **required** · pipeline by value and by property name — This parameter appears in the syntax listing because it's defined in the base class that Set-AuthenticodeSignature is derived from.
- `-Content` <Byte[]> · **required** · pipeline by property name — This parameter appears in the syntax listing because it's defined in the base class that Set-AuthenticodeSignature is derived from.

**Output:** Signature

**Example**

```powershell
Set-AuthenticodeSignature -FilePath .\script.ps1 -Certificate (Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert)[0]
```

### Set-ExecutionPolicy (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Sets the script execution policy (Restricted, RemoteSigned, AllSigned, Bypass...) for a scope.

**Syntax** (one line per parameter set)

```powershell
Set-ExecutionPolicy [-ExecutionPolicy] <ExecutionPolicy> [[-Scope] <ExecutionPolicyScope>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ExecutionPolicy` <ExecutionPolicy> · **required** · position 0 · pipeline by value — Specifies the execution policy. If there are no Group Policies and each scope's execution policy is set to Undefined, then Restricted becomes the effective policy for all users.
- `-Scope` <ExecutionPolicyScope> · position 1 · pipeline by property name · default LocalMachine — Specifies the scope that's affected by an execution policy. The default scope is LocalMachine.
- `-Force` switch — Suppresses all the confirmation prompts. Use caution with this parameter to avoid unexpected results.

**Output:** None

**Example**

```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

### Test-FileCatalog (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Checks files against a catalog file (.cat) to see whether any were changed.

**Syntax** (one line per parameter set)

```powershell
Test-FileCatalog [-CatalogFilePath] <string> [[-Path] <string[]>] [-Detailed] [-FilesToSkip <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Detailed` switch — Returns more information a more detailed CatalogInformation object that contains the files tested, their expected/actual hashes, and an Authenticode signature of the catalog file if it's signed.
- `-FilesToSkip` <String[]> — An array of paths that should not be tested as part of the validation.
- `-CatalogFilePath` <String> · **required** · position 0 · pipeline by value and by property name — A path to a catalog file (.cat) that contains the hashes to be used for validation.
- `-Path` <String[]> · position 1 · pipeline by value and by property name — A folder or array of files that should be validated against the catalog file.

**Output:** CatalogValidationStatus; CatalogInformation

**Example**

```powershell
Test-FileCatalog -Path .\MyModule -CatalogFilePath .\MyModule.cat
```

### Unprotect-CmsMessage (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Security

Decrypts CMS-encrypted content with your certificate's private key.

**Syntax** (one line per parameter set)

```powershell
Unprotect-CmsMessage [-EventLogRecord] <EventLogRecord> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]

Unprotect-CmsMessage [-Content] <string> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]

Unprotect-CmsMessage [-Path] <string> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]

Unprotect-CmsMessage [-LiteralPath] <string> [[-To] <CmsMessageRecipient[]>] [-IncludeContext] [<CommonParameters>]
```

**Arguments**

- `-Content` <String> · **required** · position 0 · pipeline by value and by property name — Specifies an encrypted string, or a variable containing an encrypted string.
- `-EventLogRecord` <PSObject> · **required** · position 0 · pipeline by value — Specifies an event log record that contains a CMS encrypted message.
- `-Path` <String> · **required** · position 0 — Specifies the path to encrypted content that you want to decrypt.
- `-LiteralPath` <String> · **required** · position 0 — Specifies the path to encrypted content that you want to decrypt. Unlike Path, the value of LiteralPath is used exactly as it's typed.
- `-IncludeContext` switch — Determines whether to include the decrypted content in its original context, rather than output the decrypted content only.
- `-To` <CmsMessageRecipient[]> · position 1 — Specifies one or more CMS message recipients, identified in any of the following formats:

**Output:** String

**Example**

```powershell
Unprotect-CmsMessage -Path .\secret.cms
```

## Commands: Microsoft.PowerShell.Diagnostics

### Export-Counter (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Diagnostics

Saves performance counter samples (from Get-Counter or Import-Counter) to a .blg, .csv or .tsv log file.

**Syntax** (one line per parameter set)

```powershell
Export-Counter [-Path] <string> -InputObject <PerformanceCounterSampleSet[]> [-FileFormat <string>] [-MaxSize <uint32>] [-Force] [-Circular] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by property name · alias -PSPath — Specifies the path and file name of the output file. Enter a relative or absolute path on the local computer, or a Uniform Naming Convention (UNC) path to a remote computer, such as \\Computer\Share\file.blg.
- `-FileFormat` <String> — Specifies the output format of the output log file.
- `-MaxSize` <UInt32> — Specifies the maximum size of the output file in megabytes (MB).
- `-InputObject` <PerformanceCounterSampleSet[]> · **required** · pipeline by value and by property name — Specifies, as an array, the counter data to export. Enter a variable that contains the data or a command that gets the data, such as the Get-Counter or Import-Counter cmdlet.
- `-Force` switch — Overwrites and replaces an existing file if one exists in the location specified by the Path parameter.
- `-Circular` switch — Indicates that the output file is a circular log with first in, first out (FIFO) format.

**Output:** None

**Example**

```powershell
Get-Counter '\Processor(_Total)\% Processor Time' -MaxSamples 10 | Export-Counter -Path .\cpu.blg
```

### Get-Counter (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Diagnostics

Reads performance counter data (CPU, memory, disk...) from the local or remote computers.

**Syntax** (one line per parameter set)

```powershell
Get-Counter [[-Counter] <string[]>] [-SampleInterval <int>] [-MaxSamples <long>] [-Continuous] [-ComputerName <string[]>] [<CommonParameters>]

Get-Counter [-ListSet] <string[]> [-ComputerName <string[]>] [<CommonParameters>]
```

**Arguments**

- `-ListSet` <String[]> · **required** · position 0 · pipeline by value · wildcards — Lists the performance counter sets on the computers. Use an asterisk (*) to specify all counter sets.
- `-Counter` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies the path to one or more counter paths. Paths are input as a comma-separated array, a variable, or values from a text file.
- `-SampleInterval` <Int32> — Specifies the number of seconds between samples for each specified performance counter.
- `-MaxSamples` <Int64> — Specifies the number of samples to get from each specified performance counter.
- `-Continuous` switch — When Continuous is specified, Get-Counter gets samples until you press <kbd>CTRL</kbd>+<kbd>C</kbd>.
- `-ComputerName` <String[]> · alias -Cn — Specifies one computer name or a comma-separated array of remote computer names.

**Output:** CounterSet; PerformanceCounterSampleSet

**Example**

```powershell
Get-Counter '\Memory\Available MBytes' -SampleInterval 2 -MaxSamples 5
```

### Get-WinEvent (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Diagnostics

Gets events from event logs (classic and modern) and .evtx/.etl files, with fast filtering by hash table or XPath.

**Syntax** (one line per parameter set)

```powershell
Get-WinEvent [[-LogName] <string[]>] [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-FilterXPath <string>] [-Force] [-Oldest] [<CommonParameters>]

Get-WinEvent [-ListLog] <string[]> [-ComputerName <string>] [-Credential <pscredential>] [-Force] [<CommonParameters>]

Get-WinEvent [-ListProvider] <string[]> [-ComputerName <string>] [-Credential <pscredential>] [<CommonParameters>]

Get-WinEvent [-ProviderName] <string[]> [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-FilterXPath <string>] [-Force] [-Oldest] [<CommonParameters>]

Get-WinEvent [-Path] <string[]> [-MaxEvents <long>] [-Credential <pscredential>] [-FilterXPath <string>] [-Oldest] [<CommonParameters>]

Get-WinEvent [-FilterHashtable] <hashtable[]> [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-Force] [-Oldest] [<CommonParameters>]

Get-WinEvent [-FilterXml] <xml> [-MaxEvents <long>] [-ComputerName <string>] [-Credential <pscredential>] [-Oldest] [<CommonParameters>]
```

**Arguments**

- `-ListLog` <String[]> · **required** · position 0 · wildcards — Specifies the event logs. Enter the event log names in a comma-separated list.
- `-LogName` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies the event logs that this cmdlet get events from. Enter the event log names in a comma-separated list.
- `-ListProvider` <String[]> · **required** · position 0 · wildcards — Specifies the event log providers that this cmdlet gets. An event log provider is a program or service that writes events to the event log.
- `-ProviderName` <String[]> · **required** · position 0 · pipeline by property name · wildcards — Specifies, as a string array, the event log providers from which this cmdlet gets events.
- `-Path` <String[]> · **required** · position 0 · pipeline by property name · alias -PSPath · wildcards — Specifies the path to the event log files that this cmdlet get events from.
- `-MaxEvents` <Int64> — Specifies the maximum number of events that are returned. Enter an integer such as 100.
- `-ComputerName` <String> · alias -Cn · default Local computer — Specifies the name of the computer that this cmdlet gets events from the event logs.
- `-Credential` <PSCredential> · default Current user — Specifies a user account that has permission to perform this action. The default value is the current user.
- `-FilterXPath` <String> — Specifies an XPath query that this cmdlet select events from one or more logs.
- `-FilterXml` <XmlDocument> · **required** · position 0 — Specifies a structured XML query that this cmdlet selects events from one or more event logs.
- `-FilterHashtable` <Hashtable[]> · **required** · position 0 — Specifies a query in hash table format to select events from one or more event logs.
- `-Force` switch — Gets debug and analytic logs, in addition to other event logs. The Force parameter is required to get a debug or analytic log when the value of the Name parameter includes wildcard characters.
- `-Oldest` switch — Indicate that this cmdlet gets the events in oldest-first order. By default, events are returned in newest-first order.

**Output:** EventLogConfiguration; EventLogRecord; ProviderMetadata

**Example**

```powershell
Get-WinEvent -FilterHashtable @{ LogName = 'System'; Level = 2; StartTime = (Get-Date).AddDays(-1) }
```

### Import-Counter (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Diagnostics

Reads performance counter log files (.blg, .csv, .tsv) back into counter sample objects.

**Syntax** (one line per parameter set)

```powershell
Import-Counter [-Path] <string[]> [-StartTime <datetime>] [-EndTime <datetime>] [-Counter <string[]>] [-MaxSamples <long>] [<CommonParameters>]

Import-Counter [-Path] <string[]> -ListSet <string[]> [<CommonParameters>]

Import-Counter [-Path] <string[]> [-Summary] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · alias -PSPath · wildcards — Specifies the file paths of the files to be imported. This parameter is required.
- `-ListSet` <String[]> · **required** · wildcards — Specifies the performance counter sets that are represented in the exported files.
- `-StartTime` <DateTime> · default No start time — Specifies the start date and time in which this cmdlet gets counter data.
- `-EndTime` <DateTime> · default No end time — Specifies an end date and time that this cmdlet imports counter data between the StartTime and this parameter timestamps.
- `-Counter` <String[]> · default All counter · wildcards — Specifies, as a string array, the performance counters. By default, Import-Counter imports all data from all counters in the input files.
- `-Summary` switch — Indicates that this cmdlet gets a summary of the imported data, instead of getting individual counter data samples.
- `-MaxSamples` <Int64> · default No maximum — Specifies the maximum number of samples of each counter to import. By default, Get-Counter imports all of the data in the files specified by the Path parameter.

**Output:** CounterFileInfo

**Example**

```powershell
Import-Counter -Path .\cpu.blg | Select-Object -ExpandProperty CounterSamples
```

### New-WinEvent (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Diagnostics

Writes an event for a registered event provider (by provider name and event ID) to its log.

**Syntax** (one line per parameter set)

```powershell
New-WinEvent [-ProviderName] <string> [-Id] <int> [[-Payload] <Object[]>] [-Version <byte>] [<CommonParameters>]
```

**Arguments**

- `-ProviderName` <String> · **required** · position 0 — Specifies the event provider that writes the event to an event log, such as "Microsoft-Windows-PowerShell".
- `-Id` <Int32> · **required** · position 1 — Specifies an event Id that is registered in the event provider.
- `-Version` <Byte> — Specifies the version number of the event. PowerShell converts the number to the required Byte type.
- `-Payload` <Object[]> · position 2 — The payload is an array of values passed as positional arguments to the event template.

**Output:** None

**Example**

```powershell
New-WinEvent -ProviderName Microsoft-Windows-PowerShell -Id 45090 -Payload @('Test', 'Message')
```

## Commands: Microsoft.PowerShell.Host

### Start-Transcript (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Host

Records everything typed and shown in the session to a text file until Stop-Transcript.

**Syntax** (one line per parameter set)

```powershell
Start-Transcript [[-Path] <string>] [-Append] [-Force] [-NoClobber] [-IncludeInvocationHeader] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Transcript [[-LiteralPath] <string>] [-Append] [-Force] [-NoClobber] [-IncludeInvocationHeader] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-Transcript [[-OutputDirectory] <string>] [-Append] [-Force] [-NoClobber] [-IncludeInvocationHeader] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · position 0 — Specifies a location to the transcript file. Enter a path to a .txt file.
- `-LiteralPath` <String> · position 0 · alias -PSPath — Specifies a location to the transcript file. Unlike the Path parameter, the value of the LiteralPath parameter is used exactly as it's typed.
- `-OutputDirectory` <String> · position 0 — Specifies a specific path and folder in which to save a transcript. PowerShell automatically assigns the transcript name.
- `-Append` switch — Indicates that this cmdlet adds the new transcript to the end of an existing file.
- `-Force` switch — Allows the cmdlet to append the transcript to an existing read-only file.
- `-NoClobber` switch · alias -NoOverwrite — Indicates that this cmdlet doesn't overwrite an existing file. By default, if a transcript file exists in the specified path, Start-Transcript overwrites the file without warning.
- `-IncludeInvocationHeader` switch — Indicates that this cmdlet logs the time stamp when commands are run.

**Output:** String

**Example**

```powershell
Start-Transcript -Path C:\Logs\session.txt -Append
```

### Stop-Transcript (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.Host

Stops the transcript started by Start-Transcript and closes the file.

**Syntax** (one line per parameter set)

```powershell
Stop-Transcript [<CommonParameters>]
```

**Output:** String

**Example**

```powershell
Stop-Transcript
```

## Commands: Microsoft.PowerShell.Archive

### Compress-Archive (PowerShell 5.1 function)

Module Microsoft.PowerShell.Archive

Creates or updates a ZIP file from files and folders.

**Syntax** (one line per parameter set)

```powershell
Compress-Archive [-Path] <string[]> [-DestinationPath] <string> [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-Path] <string[]> [-DestinationPath] <string> -Update [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-Path] <string[]> [-DestinationPath] <string> -Force [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-DestinationPath] <string> -LiteralPath <string[]> -Update [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-DestinationPath] <string> -LiteralPath <string[]> -Force [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Compress-Archive [-DestinationPath] <string> -LiteralPath <string[]> [-CompressionLevel <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · pipeline by value and by property name · wildcards — Specifies the path or paths to the files that you want to add to the archive zipped file.
- `-LiteralPath` <String[]> · **required** · pipeline by property name · alias -PSPath — Specifies the path or paths to the files that you want to add to the archive zipped file.
- `-DestinationPath` <String> · **required** · position 1 — This parameter is required and specifies the path to the archive output file.
- `-CompressionLevel` <String> · values: Optimal, NoCompression, Fastest · default Optimal — Specifies how much compression to apply when you're creating the archive file.
- `-Update` switch · **required** — Updates the specified archive by replacing older file versions in the archive with newer file versions that have the same names.
- `-Force` switch · **required** — Use this parameter to overwrite an existing archive file.

**Output:** None

**Example**

```powershell
Compress-Archive -Path .\Reports\* -DestinationPath .\Reports.zip -Update
```

### Expand-Archive (PowerShell 5.1 function)

Module Microsoft.PowerShell.Archive

Extracts the contents of a ZIP file to a folder.

**Syntax** (one line per parameter set)

```powershell
Expand-Archive [-Path] <string> [[-DestinationPath] <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Expand-Archive [[-DestinationPath] <string>] -LiteralPath <string> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the path to the archive file.
- `-LiteralPath` <String> · **required** · pipeline by property name · alias -PSPath — Specifies the path to an archive file. Unlike the Path parameter, the value of LiteralPath is used exactly as it's typed.
- `-DestinationPath` <String> · position 1 · default A folder in the current location — By default, Expand-Archive creates a folder in the current location that's the same name as the ZIP file.
- `-Force` switch — Use this parameter to overwrite existing files. By default, Expand-Archive doesn't overwrite.

**Output:** None

**Example**

```powershell
Expand-Archive -Path .\Reports.zip -DestinationPath .\Reports -Force
```

## Commands: Microsoft.PowerShell.LocalAccounts

### Add-LocalGroupMember (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: algm

Adds users or groups to a local group.

**Syntax** (one line per parameter set)

```powershell
Add-LocalGroupMember [-Group] <LocalGroup> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Add-LocalGroupMember [-Name] <string> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Add-LocalGroupMember [-SID] <SecurityIdentifier> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Group` <LocalGroup> · **required** · position 0 — Specifies the security group to which this cmdlet adds members.
- `-Member` <LocalPrincipal[]> · **required** · position 1 · pipeline by value and by property name — Specifies an array of users or groups that this cmdlet adds to a security group.
- `-Name` <String> · **required** · position 0 — Specifies the name of the security group to which this cmdlet adds members.
- `-SID` <SecurityIdentifier> · **required** · position 0 — Specifies the security ID of the security group to which this cmdlet adds members.

**Output:** None

**Example**

```powershell
Add-LocalGroupMember -Group Administrators -Member CONTOSO\Ann
```

### Disable-LocalUser (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: dlu

Disables a local user account.

**Syntax** (one line per parameter set)

```powershell
Disable-LocalUser [-InputObject] <LocalUser[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-LocalUser [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-LocalUser [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <LocalUser[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of user accounts that this cmdlet disables. To obtain a user account, use the Get-LocalUser cmdlet.
- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of names of the user accounts that this cmdlet disables.
- `-SID` <SecurityIdentifier[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of user accounts that this cmdlet disables.

**Output:** None

**Example**

```powershell
Disable-LocalUser -Name Guest
```

### Enable-LocalUser (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: elu

Enables a local user account.

**Syntax** (one line per parameter set)

```powershell
Enable-LocalUser [-InputObject] <LocalUser[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-LocalUser [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-LocalUser [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <LocalUser[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of user accounts that this cmdlet enables. To obtain a user account, use the Get-LocalUser cmdlet.
- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of names of the user accounts that this cmdlet enables.
- `-SID` <SecurityIdentifier[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of user accounts that this cmdlet enables.

**Output:** None

**Example**

```powershell
Enable-LocalUser -Name Support
```

### Get-LocalGroup (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: glg

Lists local security groups.

**Syntax** (one line per parameter set)

```powershell
Get-LocalGroup [[-Name] <string[]>] [<CommonParameters>]

Get-LocalGroup [[-SID] <SecurityIdentifier[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name — Specifies an array of names of security groups that this cmdlet gets. You can use the wildcard character.
- `-SID` <SecurityIdentifier[]> · position 0 · pipeline by value and by property name — Specifies an array of security IDs (SIDs) of security groups that this cmdlet gets.

**Output:** LocalGroup

**Example**

```powershell
Get-LocalGroup
```

### Get-LocalGroupMember (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: glgm

Lists the members of a local group.

**Syntax** (one line per parameter set)

```powershell
Get-LocalGroupMember [-Name] <string> [[-Member] <string>] [<CommonParameters>]

Get-LocalGroupMember [-Group] <LocalGroup> [[-Member] <string>] [<CommonParameters>]

Get-LocalGroupMember [-SID] <SecurityIdentifier> [[-Member] <string>] [<CommonParameters>]
```

**Arguments**

- `-Group` <LocalGroup> · **required** · position 0 · pipeline by value and by property name — Specifies the security group from which this cmdlet gets members.
- `-Member` <String> · position 1 — Specifies a user or group that this cmdlet gets from a security group.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the name of the security group from which this cmdlet gets members.
- `-SID` <SecurityIdentifier> · **required** · position 0 · pipeline by value and by property name — Specifies the security ID of the security group from which this cmdlet gets members.

**Output:** LocalPrincipal

**Example**

```powershell
Get-LocalGroupMember -Group Administrators
```

### Get-LocalUser (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: glu

Lists local user accounts.

**Syntax** (one line per parameter set)

```powershell
Get-LocalUser [[-Name] <string[]>] [<CommonParameters>]

Get-LocalUser [[-SID] <SecurityIdentifier[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name · wildcards — Specifies an array of names of user accounts that this cmdlet gets. You can use the wildcard character.
- `-SID` <SecurityIdentifier[]> · position 0 · pipeline by value and by property name — Specifies an array of security IDs (SIDs) of user accounts that this cmdlet gets.

**Output:** LocalUser[]

**Example**

```powershell
Get-LocalUser | Where-Object Enabled
```

### New-LocalGroup (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: nlg

Creates a local security group.

**Syntax** (one line per parameter set)

```powershell
New-LocalGroup [-Name] <string> [-Description <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Description` <String> · pipeline by property name — Specifies a comment for the group. The maximum length is 256 characters.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies a name for the group. The maximum length is 48 characters.

**Output:** LocalGroup

**Example**

```powershell
New-LocalGroup -Name Testers -Description 'Test team'
```

### New-LocalUser (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: nlu

Creates a local user account.

**Syntax** (one line per parameter set)

```powershell
New-LocalUser [-Name] <string> -Password <securestring> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-Disabled] [-FullName <string>] [-PasswordNeverExpires] [-UserMayNotChangePassword] [-WhatIf] [-Confirm] [<CommonParameters>]

New-LocalUser [-Name] <string> -NoPassword [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-Disabled] [-FullName <string>] [-UserMayNotChangePassword] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-AccountExpires` <DateTime> · pipeline by property name — Specifies when the user account expires. You can use the Get-Date cmdlet to get a DateTime object.
- `-AccountNeverExpires` switch · pipeline by property name — Indicates that the account doesn't expire.
- `-Description` <String> · pipeline by property name — Specifies a comment for the user account. The maximum length is 48 characters.
- `-Disabled` switch · pipeline by property name — Indicates that this cmdlet creates the user account as disabled.
- `-FullName` <String> · pipeline by property name — Specifies the full name for the user account. The full name differs from the user name of the user account.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the user name for the user account.
- `-Password` <SecureString> · **required** · pipeline by property name — Specifies a password for the user account. You can use Read-Host -AsSecureString, Get-Credential, or ConvertTo-SecureString to create a SecureString object for the password.
- `-NoPassword` switch · **required** · pipeline by property name — Indicates that the user account doesn't have a password.
- `-PasswordNeverExpires` switch · pipeline by property name — Indicates whether the new user's password expires.
- `-UserMayNotChangePassword` switch · pipeline by property name — Indicates that the user can't change the password on the user account.

**Output:** LocalUser

**Example**

```powershell
New-LocalUser -Name Support -Password (Read-Host -AsSecureString) -FullName 'Support Account'
```

### Remove-LocalGroup (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: rlg

Deletes local security groups.

**Syntax** (one line per parameter set)

```powershell
Remove-LocalGroup [-InputObject] <LocalGroup[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroup [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroup [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <LocalGroup[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of security groups that this cmdlet deletes. To obtain groups, use the Get-LocalGroup cmdlet.
- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of names of the security groups that this cmdlet deletes.
- `-SID` <SecurityIdentifier[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of security IDs (SIDs) of security groups that this cmdlet deletes.

**Output:** None

**Example**

```powershell
Remove-LocalGroup -Name Testers
```

### Remove-LocalGroupMember (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: rlgm

Removes members from a local group.

**Syntax** (one line per parameter set)

```powershell
Remove-LocalGroupMember [-Group] <LocalGroup> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroupMember [-Name] <string> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalGroupMember [-SID] <SecurityIdentifier> [-Member] <LocalPrincipal[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Group` <LocalGroup> · **required** · position 0 — Specifies the security group from which this cmdlet removes members.
- `-Member` <LocalPrincipal[]> · **required** · position 1 · pipeline by value and by property name — Specifies an array of users or groups that this cmdlet removes from a security group.
- `-Name` <String> · **required** · position 0 — Specifies the name of the security group from which this cmdlet removes members.
- `-SID` <SecurityIdentifier> · **required** · position 0 — Specifies the security ID of the security group from which this cmdlet removes members.

**Output:** None

**Example**

```powershell
Remove-LocalGroupMember -Group Administrators -Member CONTOSO\Ann
```

### Remove-LocalUser (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: rlu

Deletes local user accounts.

**Syntax** (one line per parameter set)

```powershell
Remove-LocalUser [-InputObject] <LocalUser[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalUser [-Name] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-LocalUser [-SID] <SecurityIdentifier[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <LocalUser[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of user accounts that this cmdlet deletes. To obtain a user account, use the Get-LocalUser cmdlet.
- `-Name` <String[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of names of the user accounts that this cmdlet deletes.
- `-SID` <SecurityIdentifier[]> · **required** · position 0 · pipeline by value and by property name — Specifies an array of security IDs (SIDs) of user accounts that this cmdlet deletes.

**Output:** None

**Example**

```powershell
Remove-LocalUser -Name Support
```

### Rename-LocalGroup (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: rnlg

Renames a local group.

**Syntax** (one line per parameter set)

```powershell
Rename-LocalGroup [-InputObject] <LocalGroup> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalGroup [-Name] <string> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalGroup [-SID] <SecurityIdentifier> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <LocalGroup> · **required** · position 0 · pipeline by value and by property name — Specifies the security group that this cmdlet renames. To obtain a security group, use the Get-LocalGroup cmdlet.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the name of the security group that this cmdlet renames.
- `-NewName` <String> · **required** · position 1 — Specifies a new name for the security group.
- `-SID` <SecurityIdentifier> · **required** · position 0 · pipeline by value and by property name — Specifies the security ID (SID) of a security group that this cmdlet renames.

**Output:** None

**Example**

```powershell
Rename-LocalGroup -Name Testers -NewName QA
```

### Rename-LocalUser (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: rnlu

Renames a local user account.

**Syntax** (one line per parameter set)

```powershell
Rename-LocalUser [-InputObject] <LocalUser> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalUser [-Name] <string> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Rename-LocalUser [-SID] <SecurityIdentifier> [-NewName] <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <LocalUser> · **required** · position 0 · pipeline by value and by property name — Specifies the user account that this cmdlet renames. To obtain a user account, use the Get-LocalUser cmdlet.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the name of the user account that this cmdlet renames.
- `-NewName` <String> · **required** · position 1 — Specifies a new name for the user account.
- `-SID` <SecurityIdentifier> · **required** · position 0 · pipeline by value and by property name — Specifies the security ID (SID) of a user accounts that this cmdlet renames.

**Output:** None

**Example**

```powershell
Rename-LocalUser -Name Support -NewName HelpDesk
```

### Set-LocalGroup (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: slg

Changes a local group's description.

**Syntax** (one line per parameter set)

```powershell
Set-LocalGroup [-InputObject] <LocalGroup> -Description <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalGroup [-Name] <string> -Description <string> [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalGroup [-SID] <SecurityIdentifier> -Description <string> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Description` <String> · **required** — Specifies a comment for the group. The maximum length is 48 characters.
- `-InputObject` <LocalGroup> · **required** · position 0 · pipeline by value and by property name — Specifies the security group that this cmdlet changes. To obtain a security group, use the Get-LocalGroup cmdlet.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the name of the security group that this cmdlet changes.
- `-SID` <SecurityIdentifier> · **required** · position 0 · pipeline by value and by property name — Specifies the security ID (SID) of the security group that this cmdlet changes.

**Output:** None

**Example**

```powershell
Set-LocalGroup -Name QA -Description 'Quality assurance'
```

### Set-LocalUser (PowerShell 5.1 cmdlet)

Module Microsoft.PowerShell.LocalAccounts · aliases: slu

Changes a local user account (password, description, expiry...).

**Syntax** (one line per parameter set)

```powershell
Set-LocalUser [-Name] <string> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-FullName <string>] [-Password <securestring>] [-PasswordNeverExpires <bool>] [-UserMayChangePassword <bool>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalUser [-InputObject] <LocalUser> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-FullName <string>] [-Password <securestring>] [-PasswordNeverExpires <bool>] [-UserMayChangePassword <bool>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-LocalUser [-SID] <SecurityIdentifier> [-AccountExpires <datetime>] [-AccountNeverExpires] [-Description <string>] [-FullName <string>] [-Password <securestring>] [-PasswordNeverExpires <bool>] [-UserMayChangePassword <bool>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-AccountExpires` <DateTime> — Specifies when the user account expires. To obtain a DateTime object, use the Get-Date cmdlet.
- `-AccountNeverExpires` switch — Indicates that the account does not expire.
- `-Description` <String> — Specifies a comment for the user account. The maximum length is 48 characters.
- `-FullName` <String> — Specifies the full name for the user account. The full name differs from the user name of the user account.
- `-InputObject` <LocalUser> · **required** · position 0 · pipeline by value and by property name — Specifies the user account that this cmdlet changes. To obtain a user account, use the Get-LocalUser cmdlet.
- `-Name` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the name of the user account that this cmdlet changes.
- `-Password` <SecureString> — Specifies a password for the user account. If the user account is connected to a Microsoft account, do not set a password.
- `-PasswordNeverExpires` <Boolean> — Indicates whether the password expires.
- `-SID` <SecurityIdentifier> · **required** · position 0 · pipeline by value and by property name — Specifies the security ID (SID) of the user account that this cmdlet changes.
- `-UserMayChangePassword` <Boolean> — Indicates that the user can change the password on the user account.

**Output:** None

**Example**

```powershell
Set-LocalUser -Name HelpDesk -PasswordNeverExpires $true
```

## Commands: Microsoft.PowerShell.ODataUtils

### Export-ODataEndpointProxy (PowerShell 5.1 function)

Module Microsoft.PowerShell.ODataUtils

Generates a module of cmdlets for managing an OData endpoint's resources.

**Syntax** (one line per parameter set)

```powershell
Export-ODataEndpointProxy [-Uri] <string> [-OutputModule] <string> [[-MetadataUri] <string>] [[-Credential] <pscredential>] [[-CreateRequestMethod] <string>] [[-UpdateRequestMethod] <string>] [[-CmdletAdapter] <string>] [[-ResourceNameMapping] <hashtable>] [-Force] [[-CustomData] <hashtable>] [-AllowClobber] [-AllowUnsecureConnection] [[-Headers] <hashtable>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Uri` <String> · **required** · position 0 · pipeline by value and by property name — Specifies the URI of the endpoint.
- `-OutputModule` <String> · **required** · position 1 · pipeline by property name — Specifies the path and module name to which this cmdlet saves the generated module of proxy commands.
- `-MetadataUri` <String> · position 2 · pipeline by property name — Specifies the URI of the metadata of the endpoint.
- `-Credential` <PSCredential> · position 3 · pipeline by property name — Specifies a user account that has access to the OData endpoint. The default value is the current user.
- `-CreateRequestMethod` <String> · position 4 · pipeline by property name · values: Put, Post, Patch — Specifies the request method. The acceptable values for this parameter are: PUT, POST, and PATCH.
- `-UpdateRequestMethod` <String> · position 5 · pipeline by property name · values: Put, Post, Patch — Specifies the update request method. The acceptable values for this parameter are: PUT, POST, and PATCH.
- `-CmdletAdapter` <String> · position 6 · pipeline by property name · values: ODataAdapter, NetworkControllerAdapter, ODataV4Adapter — Specifies the cmdlet adapter. The acceptable values for this parameter are: ODataAdapter and NetworkControllerAdapter.
- `-ResourceNameMapping` <Hashtable> · position 7 · pipeline by property name — Specifies a hashtable that contains mappings that let you customize the generated cmdlets.
- `-Force` switch · position 8 · pipeline by property name — Indicates that this cmdlet overwrites an existing generated module of the same name in an existing Modules folder.
- `-CustomData` <Hashtable> · position 9 · pipeline by property name — Specifies a hash table of custom data.
- `-AllowClobber` switch · position 10 · pipeline by property name — Indicates that this cmdlet replaces an existing module.
- `-AllowUnsecureConnection` switch · position 11 · pipeline by property name — Indicates that this module can connect to URIs that are not SSL-secured.
- `-Headers` <Hashtable> · position 12 · pipeline by property name — Specifies the headers of the web request. Enter a hash table or dictionary.

**Example**

```powershell
Export-ODataEndpointProxy -Uri 'http://server/odata.svc' -OutputModule .\ODataProxy
```

## Commands: Microsoft.PowerShell.Operation.Validation

### Get-OperationValidation (PowerShell 5.1 function)

Module Microsoft.PowerShell.Operation.Validation

Finds Pester-based operational validation tests in installed modules.

**Syntax** (one line per parameter set)

```powershell
Get-OperationValidation [[-ModuleName] <string[]>] [-TestType <string[]>] [<CommonParameters>]
```

**Output:** PSCustomObject

**Example**

```powershell
Get-OperationValidation -ModuleName MyChecks
```

### Invoke-OperationValidation (PowerShell 5.1 function)

Module Microsoft.PowerShell.Operation.Validation

Runs operational validation tests found by Get-OperationValidation.

**Syntax** (one line per parameter set)

```powershell
Invoke-OperationValidation [-TestInfo <psobject[]>] [-IncludePesterOutput] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-OperationValidation [-testFilePath <string[]>] [-IncludePesterOutput] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-OperationValidation [-ModuleName <string[]>] [-TestType <string[]>] [-IncludePesterOutput] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Output:** PSCustomObject

**Example**

```powershell
Get-OperationValidation -ModuleName MyChecks | Invoke-OperationValidation
```

## Commands: Microsoft.WSMan.Management

### Connect-WSMan (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Connects to the WinRM service on a remote computer so the WSMan: drive can browse it.

**Syntax** (one line per parameter set)

```powershell
Connect-WSMan [[-ComputerName] <string>] [-ApplicationName <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Connect-WSMan [-ConnectionURI <uri>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

**Arguments**

- `-ApplicationName` <String> — Specifies the application name in the connection. The default value of the ApplicationName parameter is WSMAN.
- `-ComputerName` <String> · position 0 · alias -cn — Specifies the computer against which to run the management operation. The value can be a fully qualified domain name, a NetBIOS name, or an IP address.
- `-ConnectionURI` <Uri> — Specifies the connection endpoint.
- `-OptionSet` <Hashtable> · alias -os — Specifies a set of switches to a service to modify or refine the nature of the request.
- `-Port` <Int32> — Specifies the port to use when the client connects to the WinRM service.
- `-SessionOption` <SessionOption> · alias -so — Specifies extended options for the WS-Management session. Enter a SessionOption object that you create by using the New-WSManSessionOption cmdlet.
- `-UseSSL` switch — Specifies that the Secure Sockets Layer (SSL) protocol is used to establish a connection to the remote computer.
- `-Credential` <PSCredential> · pipeline by property name · alias -cred, -c — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Authentication` <AuthenticationMechanism> · alias -auth, -am — Specifies the authentication mechanism to be used at the server.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** None

**Example**

```powershell
Connect-WSMan -ComputerName Server01
```

### Disable-WSManCredSSP (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Turns off CredSSP authentication (client or server role).

**Syntax** (one line per parameter set)

```powershell
Disable-WSManCredSSP [-Role] <string> [<CommonParameters>]
```

**Arguments**

- `-Role` <String> · **required** · position 0 · values: Client, Server — Specifies whether to disable CredSSP as a client or as a server. The acceptable values for this parameter are: Client and Server.

**Output:** None

**Example**

```powershell
Disable-WSManCredSSP -Role Client
```

### Disconnect-WSMan (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Disconnects from a remote computer's WinRM service.

**Syntax** (one line per parameter set)

```powershell
Disconnect-WSMan [[-ComputerName] <string>] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String> · position 0 — Specifies the computer against which to run the management operation. The value can be a fully qualified domain name, a NetBIOS name, or an IP address.

**Output:** None

**Example**

```powershell
Disconnect-WSMan -ComputerName Server01
```

### Enable-WSManCredSSP (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Enables CredSSP authentication so credentials can be delegated to remote computers.

**Syntax** (one line per parameter set)

```powershell
Enable-WSManCredSSP [-Role] <string> [[-DelegateComputer] <string[]>] [-Force] [<CommonParameters>]
```

**Arguments**

- `-DelegateComputer` <String[]> · position 1 · wildcards — Specifies servers to which client credentials are delegated. The best practice is to use fully qualified domain names.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-Role` <String> · **required** · position 0 · values: Client, Server — Specifies whether to enable CredSSP as a client or as a server. The acceptable values for this parameter are: Client and Server.

**Output:** XmlElement

**Example**

```powershell
Enable-WSManCredSSP -Role Client -DelegateComputer Server01.contoso.com
```

### Get-WSManCredSSP (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Shows the computer's CredSSP configuration.

**Syntax** (one line per parameter set)

```powershell
Get-WSManCredSSP [<CommonParameters>]
```

**Output:** String

**Example**

```powershell
Get-WSManCredSSP
```

### Get-WSManInstance (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Gets management information for a resource instance by resource URI.

**Syntax** (one line per parameter set)

```powershell
Get-WSManInstance [-ResourceURI] <uri> [-ApplicationName <string>] [-ComputerName <string>] [-ConnectionURI <uri>] [-Dialect <uri>] [-Fragment <string>] [-OptionSet <hashtable>] [-Port <int>] [-SelectorSet <hashtable>] [-SessionOption <SessionOption>] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Get-WSManInstance [-ResourceURI] <uri> -Enumerate [-ApplicationName <string>] [-BasePropertiesOnly] [-ComputerName <string>] [-ConnectionURI <uri>] [-Dialect <uri>] [-Filter <string>] [-OptionSet <hashtable>] [-Port <int>] [-Associations] [-ReturnType <string>] [-SessionOption <SessionOption>] [-Shallow] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

**Arguments**

- `-ApplicationName` <String> — Specifies the application name in the connection. The default value of the ApplicationName parameter is WSMAN.
- `-BasePropertiesOnly` switch · alias -UBPO, -Base — Indicates that this cmdlet enumerates only the properties that are part of the base class that is specified by the ResourceURI parameter.
- `-ComputerName` <String> · alias -CN — Specifies the computer against which to run the management operation. The value can be a fully qualified domain name, a NetBIOS name, or an IP address.
- `-ConnectionURI` <Uri> · alias -CURI, -CU — Specifies the connection endpoint.
- `-Dialect` <Uri> — Specifies the dialect to use in the filter predicate. This can be any dialect that is supported by the remote service.
- `-Enumerate` switch · **required** — Indicates that this cmdlet returns all of the instances of a management resource.
- `-Filter` <String> — Specifies the filter expression for the enumeration. If you specify this parameter, you must also specify Dialect.
- `-Fragment` <String> — Specifies a section inside the instance that is to be updated or retrieved for the specified operation.
- `-OptionSet` <Hashtable> · pipeline by value and by property name · alias -OS — Specifies a set of switches to a service to modify or refine the nature of the request.
- `-Port` <Int32> — Specifies the port to use when the client connects to the WinRM service.
- `-Associations` switch — Indicates that this cmdlet gets association instances, not associated instances.
- `-ResourceURI` <Uri> · **required** · position 0 · pipeline by value and by property name · alias -RURI — Specifies the URI of the resource class or instance. The URI identifies a specific type of resource, such as disks or processes, on a computer.
- `-ReturnType` <String> · alias -RT · values: object, epr, objectandepr — Specifies the type of data to be returned.
- `-SelectorSet` <Hashtable> — Specifies a set of value pairs that are used to select particular management resource instances.
- `-SessionOption` <SessionOption> · alias -SO — Specifies extended options for the WS-Management session. Enter a SessionOption object that you create by using the New-WSManSessionOption cmdlet.
- `-Shallow` switch — Indicates that this cmdlet returns only instances of the base class that is specified in the resource URI.
- `-UseSSL` switch · alias -SSL — Specifies that the Secure Sockets Layer (SSL) protocol is used to establish a connection to the remote computer.
- `-Credential` <PSCredential> · pipeline by property name · alias -cred, -c — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Authentication` <AuthenticationMechanism> · alias -auth, -am — Specifies the authentication mechanism to be used at the server.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** XmlElement

**Example**

```powershell
Get-WSManInstance -ResourceURI winrm/config/listener -Enumerate
```

### Invoke-WSManAction (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Invokes an action (method) on a resource by resource URI and selectors.

**Syntax** (one line per parameter set)

```powershell
Invoke-WSManAction [-ResourceURI] <uri> [-Action] <string> [[-SelectorSet] <hashtable>] [-ConnectionURI <uri>] [-FilePath <string>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Invoke-WSManAction [-ResourceURI] <uri> [-Action] <string> [[-SelectorSet] <hashtable>] [-ApplicationName <string>] [-ComputerName <string>] [-FilePath <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

**Arguments**

- `-Action` <String> · **required** · position 1 — Specifies the method to run on the management object specified by the ResourceURI and selectors.
- `-ApplicationName` <String> · default Wsman — Specifies the application name in the connection. The default value of the ApplicationName parameter is WSMAN.
- `-ComputerName` <String> · alias -cn · default Localhost — Specifies the computer against which you want to run the management operation.
- `-ConnectionURI` <Uri> · alias -CURI, -CU — Specifies the connection endpoint.
- `-FilePath` <String> — Specifies the path of a file that is used to update a management resource.
- `-OptionSet` <Hashtable> · pipeline by value and by property name · alias -os — Passes a set of switches to a service to modify or refine the nature of the request.
- `-Port` <Int32> — Specifies the port to use when the client connects to the WinRM service.
- `-SelectorSet` <Hashtable> · position 2 · pipeline by value and by property name — Specifies a set of value pairs that are used to select particular management resource instances.
- `-SessionOption` <SessionOption> · alias -so — Defines a set of extended options for the WS-Management session. Enter a SessionOption object that you create by using the New-WSManSessionOption cmdlet.
- `-UseSSL` switch — Specifies that the Secure Sockets Layer (SSL) protocol is used to establish a connection to the remote computer.
- `-ValueSet` <Hashtable> — Specifies a hash table that helps modify a management resource. You specify the management resource using the ResourceURI and SelectorSet parameters.
- `-ResourceURI` <Uri> · **required** · position 0 · pipeline by value and by property name · alias -ruri — Contains the Uniform Resource Identifier (URI) of the resource class or instance.
- `-Credential` <PSCredential> · pipeline by property name · alias -cred, -c — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Authentication` <AuthenticationMechanism> · alias -auth, -am — Specifies the authentication mechanism to be used at the server.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** XmlElement

**Example**

```powershell
Invoke-WSManAction -Action StartService -ResourceURI wmicimv2/win32_service -SelectorSet @{ Name = 'Spooler' }
```

### New-WSManInstance (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Creates a new instance of a management resource (for example a WinRM listener).

**Syntax** (one line per parameter set)

```powershell
New-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ApplicationName <string>] [-ComputerName <string>] [-FilePath <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

New-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ConnectionURI <uri>] [-FilePath <string>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

**Arguments**

- `-ApplicationName` <String> · default Wsman — Specifies the application name in the connection. The default value of the ApplicationName parameter is WSMAN.
- `-ComputerName` <String> · alias -cn · default Localhost — Specifies the computer against which you want to run the management operation.
- `-ConnectionURI` <Uri> · alias -CURI, -CU — Specifies the connection endpoint.
- `-FilePath` <String> — Specifies the path of a file that is used to create a management resource.
- `-OptionSet` <Hashtable> · alias -os — Passes a set of switches to a service to modify or refine the nature of the request.
- `-Port` <Int32> — Specifies the port to use when the client connects to the WinRM service.
- `-ResourceURI` <Uri> · **required** · position 0 · alias -ruri — Contains the Uniform Resource Identifier (URI) of the resource class or instance.
- `-SelectorSet` <Hashtable> · **required** · position 1 · pipeline by value — Specifies a set of value pairs that are used to select particular management resource instances.
- `-SessionOption` <SessionOption> · alias -so — Defines a set of extended options for the WS-Management session. Enter a SessionOption object that you create using the New-WSManSessionOption cmdlet.
- `-UseSSL` switch — Specifies that the Secure Sockets Layer (SSL) protocol should be used to establish a connection to the remote computer.
- `-ValueSet` <Hashtable> — Specifies a hash table that helps modify a management resource. You specify the management resource using the ResourceURI parameter and the SelectorSet parameter.
- `-Credential` <PSCredential> · pipeline by property name · alias -cred, -c — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Authentication` <AuthenticationMechanism> · alias -auth, -am — Specifies the authentication mechanism to be used at the server.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** XmlElement

**Example**

```powershell
New-WSManInstance -ResourceURI winrm/config/Listener -SelectorSet @{ Address = '*'; Transport = 'HTTPS' } -ValueSet @{ Hostname = 'srv'; CertificateThumbprint = 'AB12...' }
```

### New-WSManSessionOption (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Creates session options (timeouts, proxy, skip checks) for WSMan cmdlets.

**Syntax** (one line per parameter set)

```powershell
New-WSManSessionOption [-ProxyAccessType <ProxyAccessType>] [-ProxyAuthentication <ProxyAuthentication>] [-ProxyCredential <pscredential>] [-SkipCACheck] [-SkipCNCheck] [-SkipRevocationCheck] [-SPNPort <int>] [-OperationTimeout <int>] [-NoEncryption] [-UseUTF16] [<CommonParameters>]
```

**Arguments**

- `-ProxyAccessType` <ProxyAccessType> — Specifies the mechanism by which the proxy server is located.
- `-ProxyAuthentication` <ProxyAuthentication> — Specifies the authentication method to use at the proxy.
- `-ProxyCredential` <PSCredential> — Specifies a user account that has permission to gain access through an intermediate Web proxy.
- `-SkipCACheck` switch — Specifies that, when it connects over HTTPS, the client does not validate that the server certificate is signed by a trusted certification authority (CA).
- `-SkipCNCheck` switch — Specifies that the certificate common name (CN) of the server does not have to match the host name of the server.
- `-SkipRevocationCheck` switch — Indicates that the connection does not validate the revocation status on the server certificate.
- `-SPNPort` <Int32> — Specifies a port number to append to the connection Service Principal Name (SPN) of the remote server.
- `-OperationTimeout` <Int32> · alias -OperationTimeoutMSec — Specifies the time-out, in milliseconds, for the WS-Management operation.
- `-NoEncryption` switch — Indicates that the connection does not use encryption for remote operations over HTTP.
- `-UseUTF16` switch — Indicates that the connection encodes the request in UTF16 format instead of UTF8 format.

**Output:** SessionOption

**Example**

```powershell
$o = New-WSManSessionOption -OperationTimeout 30000
```

### Remove-WSManInstance (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Deletes a management resource instance.

**Syntax** (one line per parameter set)

```powershell
Remove-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ApplicationName <string>] [-ComputerName <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Remove-WSManInstance [-ResourceURI] <uri> [-SelectorSet] <hashtable> [-ConnectionURI <uri>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

**Arguments**

- `-ApplicationName` <String> — Specifies the application name in the connection. The default value of the ApplicationName parameter is WSMAN.
- `-ComputerName` <String> · alias -cn — Specifies the computer against which to run the management operation. The value can be a fully qualified domain name, a NetBIOS name, or an IP address.
- `-ConnectionURI` <Uri> — Specifies the connection endpoint.
- `-OptionSet` <Hashtable> · alias -os — Specifies a set of switches to a service to modify or refine the nature of the request.
- `-Port` <Int32> — Specifies the port to use when the client connects to the WinRM service.
- `-ResourceURI` <Uri> · **required** · position 0 · alias -ruri — Specifies the URI of the resource class or instance. The URI is used to identify a specific type of resource, such as disks or processes, on a computer.
- `-SelectorSet` <Hashtable> · **required** · position 1 · pipeline by value and by property name — Specifies a set of value pairs that are used to select particular management resource instances.
- `-SessionOption` <SessionOption> · alias -so — Specifies extended options for the WS-Management session. Enter a SessionOption object that you create by using the New-WSManSessionOption cmdlet.
- `-UseSSL` switch · alias -ssl — Specifies that the Secure Sockets Layer (SSL) protocol is used to establish a connection to the remote computer.
- `-Credential` <PSCredential> · pipeline by property name · alias -cred, -c — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Authentication` <AuthenticationMechanism> · alias -auth, -am — Specifies the authentication mechanism to be used at the server.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** None

**Example**

```powershell
Remove-WSManInstance -ResourceURI winrm/config/Listener -SelectorSet @{ Address = '*'; Transport = 'HTTPS' }
```

### Set-WSManInstance (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Changes management settings of a resource.

**Syntax** (one line per parameter set)

```powershell
Set-WSManInstance [-ResourceURI] <uri> [[-SelectorSet] <hashtable>] [-ApplicationName <string>] [-ComputerName <string>] [-Dialect <uri>] [-FilePath <string>] [-Fragment <string>] [-OptionSet <hashtable>] [-Port <int>] [-SessionOption <SessionOption>] [-UseSSL] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]

Set-WSManInstance [-ResourceURI] <uri> [[-SelectorSet] <hashtable>] [-ConnectionURI <uri>] [-Dialect <uri>] [-FilePath <string>] [-Fragment <string>] [-OptionSet <hashtable>] [-SessionOption <SessionOption>] [-ValueSet <hashtable>] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

**Arguments**

- `-ApplicationName` <String> · default Wsman — Specifies the application name in the connection. The default value of the ApplicationName parameter is "WSMAN".
- `-ComputerName` <String> · alias -cn · default Localhost — Specifies the computer against which you want to run the management operation.
- `-ConnectionURI` <Uri> — Specifies the connection endpoint.
- `-Dialect` <Uri> · default http://schemas.microsoft.com/wbem/wsman/1/WQL — Specifies the dialect to use in the filter predicate. This can be any dialect that is supported by the remote service.
- `-FilePath` <String> · pipeline by property name — Specifies the path of a file used to update a management resource. For more information, see Example 4.
- `-Fragment` <String> — Specifies a section inside the instance that is to be updated or retrieved for the specified operation.
- `-OptionSet` <Hashtable> · alias -os — Passes a set of switches to a service to modify or refine the nature of the request.
- `-Port` <Int32> — Specifies the port to use when the client connects to the WinRM service.
- `-ResourceURI` <Uri> · **required** · position 0 · alias -ruri — Contains the Uniform Resource Identifier (URI) of the resource class or instance.
- `-SelectorSet` <Hashtable> · position 1 · pipeline by value and by property name — Specifies a set of value pairs that are used to select particular management resource instances.
- `-SessionOption` <SessionOption> · alias -so — Defines a set of extended options for the WS-Management session. Enter a SessionOption object that you create with the New-WSManSessionOption cmdlet.
- `-UseSSL` switch · alias -ssl — Specifies that the Secure Sockets Layer (SSL) protocol should be used to establish a connection to the remote computer.
- `-ValueSet` <Hashtable> · pipeline by property name — Specifies a hash table that helps modify a management resource. You specify the management resource by using the ResourceURI parameter and the SelectorSet parameter.
- `-Credential` <PSCredential> · pipeline by property name · alias -cred, -c — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Authentication` <AuthenticationMechanism> · alias -auth, -am — Specifies the authentication mechanism to be used at the server.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** None

**Example**

```powershell
Set-WSManInstance -ResourceURI winrm/config/service -ValueSet @{ MaxConcurrentOperationsPerUser = 100 }
```

### Set-WSManQuickConfig (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Configures this computer for WS-Management remoting (WinRM service, listener, firewall).

**Syntax** (one line per parameter set)

```powershell
Set-WSManQuickConfig [-UseSSL] [-Force] [-SkipNetworkProfileCheck] [<CommonParameters>]
```

**Arguments**

- `-UseSSL` switch — Specifies that the Secure Sockets Layer (SSL) protocol is used to establish a connection to the remote computer.
- `-Force` switch — Forces the command to run without asking for user confirmation.
- `-SkipNetworkProfileCheck` switch — Configures Windows client versions for remoting when the computer is on a public network.

**Output:** None

**Example**

```powershell
Set-WSManQuickConfig -Force
```

### Test-WSMan (PowerShell 5.1 cmdlet)

Module Microsoft.WSMan.Management

Tests whether the WinRM service is running and reachable on a computer.

**Syntax** (one line per parameter set)

```powershell
Test-WSMan [[-ComputerName] <string>] [-Authentication <AuthenticationMechanism>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-Credential <pscredential>] [-CertificateThumbprint <string>] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String> · position 0 · pipeline by value · alias -cn — Specifies the computer against which to run the management operation. The value can be a fully qualified domain name, a NetBIOS name, or an IP address.
- `-Authentication` <AuthenticationMechanism> · alias -auth, -am — Specifies the authentication mechanism to be used at the server.
- `-Port` <Int32> — Specifies the port to use when the client connects to the WinRM service.
- `-UseSSL` switch — Specifies that the Secure Sockets Layer (SSL) protocol is used to establish a connection to the remote computer.
- `-ApplicationName` <String> — Specifies the application name in the connection. The default value of the ApplicationName parameter is WSMAN.
- `-Credential` <PSCredential> · pipeline by property name · alias -cred, -c — Specifies a user account that has permission to perform this action. The default is the current user.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.

**Output:** None

**Example**

```powershell
Test-WSMan -ComputerName Server01
```

## Commands: CimCmdlets

### Export-BinaryMiLog (PowerShell 5.1 cmdlet)

Module CimCmdlets

Saves CIM instances to a binary MI log file.

**Syntax** (one line per parameter set)

```powershell
Export-BinaryMiLog [-Path] <string> [-InputObject <ciminstance>] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <CimInstance> · pipeline by value — Specifies the input to this cmdlet. You can use this parameter, or you can pipe the input to this cmdlet.
- `-Path` <String> · **required** · position 0 · wildcards — Specifies the path of the file in which to store the binary representation of the object.

**Output:** Object

**Example**

```powershell
Get-CimInstance Win32_Service | Export-BinaryMiLog -Path .\services.bmil
```

### Get-CimAssociatedInstance (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: gcai

Gets the CIM instances linked to a given instance by an association.

**Syntax** (one line per parameter set)

```powershell
Get-CimAssociatedInstance [-InputObject] <ciminstance> [[-Association] <string>] [-ResultClassName <string>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ResourceUri <uri>] [-ComputerName <string[]>] [-KeyOnly] [<CommonParameters>]

Get-CimAssociatedInstance [-InputObject] <ciminstance> [[-Association] <string>] -CimSession <CimSession[]> [-ResultClassName <string>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ResourceUri <uri>] [-KeyOnly] [<CommonParameters>]
```

**Arguments**

- `-Association` <String> · position 1 · pipeline by property name — Specifies the name of the association class. If you don't specify this parameter, the cmdlet returns all existing association objects of any type.
- `-ResultClassName` <String> — Specifies the class name of the associated instances. A CIM instance can be associated with one or more CIM instances.
- `-InputObject` <CimInstance> · **required** · position 0 · pipeline by value · alias -CimInstance — Specifies the input to this cmdlet. You can use this parameter, or you can pipe the input to this cmdlet.
- `-Namespace` <String> · pipeline by property name — Specifies the namespace for the CIM operation. The default namespace is root/CIMV2.
- `-OperationTimeoutSec` <UInt32> · pipeline by property name · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the computer.
- `-ResourceUri` <Uri> — Specifies the resource uniform resource identifier (URI) of the resource class or instance.
- `-ComputerName` <String[]> · alias -CN, -ServerName — Specifies the name of the computer on which you want to run the CIM operation.
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command using the specified CIM session. Enter a variable that contains the CIM session, or a command that creates or gets the CIM session, such as New-CimSession or Get-CimSession.
- `-KeyOnly` switch — Returns objects with only key properties populated. This reduces the amount of data that's transferred over the network.

**Output:** CimInstance

**Example**

```powershell
Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='C:'" | Get-CimAssociatedInstance -ResultClassName Win32_DiskPartition
```

### Get-CimClass (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: gcls

Lists CIM classes in a namespace, with their properties and methods.

**Syntax** (one line per parameter set)

```powershell
Get-CimClass [[-ClassName] <string>] [[-Namespace] <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-MethodName <string>] [-PropertyName <string>] [-QualifierName <string>] [<CommonParameters>]

Get-CimClass [[-ClassName] <string>] [[-Namespace] <string>] -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-MethodName <string>] [-PropertyName <string>] [-QualifierName <string>] [<CommonParameters>]
```

**Arguments**

- `-ClassName` <String> · position 0 · pipeline by property name · wildcards — Specifies the name of the CIM class for which to perform the operation.
- `-Namespace` <String> · position 1 · pipeline by property name — Specifies the namespace for CIM operation. The default namespace is root/CIMV2.
- `-OperationTimeoutSec` <UInt32> · pipeline by property name · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the computer.
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the cmdlet in a remote session or on a remote computer. Enter a computer name or a session object, such as the output of a New-CimSession or Get-CimSession cmdlet.
- `-ComputerName` <String[]> · pipeline by property name · alias -CN, -ServerName — Specifies the computer on which you want to run the CIM operation. You can specify a fully qualified domain name (FQDN) a NetBIOS name, or an IP address.
- `-MethodName` <String> · pipeline by property name · wildcards — Finds the classes that have a method matching this name. You can use wildcard characters with this parameter.
- `-PropertyName` <String> · pipeline by property name · wildcards — Finds the classes which have a property matching this name. You can use wildcard characters with this parameter.
- `-QualifierName` <String> · pipeline by property name · wildcards — Filters the classes by class level qualifier name. You can use wildcard characters with this parameter.

**Output:** CimClass

**Example**

```powershell
Get-CimClass -ClassName Win32_*Disk*
```

### Get-CimInstance (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: gcim

Gets instances of a CIM/WMI class (the modern replacement for Get-WmiObject).

**Syntax** (one line per parameter set)

```powershell
Get-CimInstance [-ClassName] <string> [-ComputerName <string[]>] [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance -CimSession <CimSession[]> -ResourceUri <uri> [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance -CimSession <CimSession[]> -Query <string> [-ResourceUri <uri>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [<CommonParameters>]

Get-CimInstance [-ClassName] <string> -CimSession <CimSession[]> [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance [-InputObject] <ciminstance> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [<CommonParameters>]

Get-CimInstance [-InputObject] <ciminstance> [-ResourceUri <uri>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [<CommonParameters>]

Get-CimInstance -ResourceUri <uri> [-ComputerName <string[]>] [-KeyOnly] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-Shallow] [-Filter <string>] [-Property <string[]>] [<CommonParameters>]

Get-CimInstance -Query <string> [-ResourceUri <uri>] [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-Shallow] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · **required** · pipeline by value — Specifies the CIM session to use for this cmdlet. Enter a variable that contains the CIM session or a command that creates or gets the CIM session, such as the New-CimSession or Get-CimSession cmdlets.
- `-ClassName` <String> · **required** · position 0 · pipeline by property name — Specifies the name of the CIM class for which to retrieve the CIM instances.
- `-ResourceUri` <Uri> · **required** · pipeline by property name — Specifies the resource uniform resource identifier (URI) of the resource class or instance.
- `-ComputerName` <String[]> · pipeline by property name · alias -CN, -ServerName — Specifies computer on which you want to run the CIM operation. You can specify a fully qualified domain name (FQDN), a NetBIOS name, or an IP address.
- `-KeyOnly` switch — Indicates that only objects with key properties populated are returned.
- `-Namespace` <String> · pipeline by property name — Specifies the namespace of CIM class.
- `-OperationTimeoutSec` <UInt32> · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the computer.
- `-InputObject` <CimInstance> · **required** · position 0 · pipeline by value · alias -CimInstance — Specifies a CIM instance object to use as input.
- `-Query` <String> · **required** · pipeline by property name — Specifies a query to run on the CIM server. If the value specified contains double quotes ", single quotes ', or a backslash \, you must escape those characters by prefixing them with the backslash character.
- `-QueryDialect` <String> · pipeline by property name — Specifies the query language used for the Query parameter. The acceptable values for this parameter are: WQL or CQL.
- `-Shallow` switch — Indicates that the instances of a class are returned without including the instances of any child classes.
- `-Filter` <String> · pipeline by property name — Specifies a where clause to use as a filter. Specify the clause in either the WQL or the CQL query language.
- `-Property` <String[]> · pipeline by property name · alias -SelectProperties — Specifies a set of instance properties to retrieve. Use this parameter when you need to reduce the size of the object returned, either in memory or over the network.

**Output:** CimInstance

**Example**

```powershell
Get-CimInstance -ClassName Win32_OperatingSystem | Select-Object Caption, LastBootUpTime
```

### Get-CimSession (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: gcms

Lists the CIM sessions created in this session.

**Syntax** (one line per parameter set)

```powershell
Get-CimSession [[-ComputerName] <string[]>] [<CommonParameters>]

Get-CimSession [-Id] <uint32[]> [<CommonParameters>]

Get-CimSession -InstanceId <guid[]> [<CommonParameters>]

Get-CimSession -Name <string[]> [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · position 0 · pipeline by property name · alias -CN, -ServerName · wildcards — Specifies the name of the computer to get CIM sessions connected to. Wildcard characters are permitted.
- `-Id` <UInt32[]> · **required** · position 0 · pipeline by property name — Specifies the identifier of the CIM session to get. For multiple IDs, use commas to separate the IDs or use the range operator (..) to specify a range of IDs.
- `-InstanceId` <Guid[]> · **required** · pipeline by property name — Specifies the instance IDs of the CIM session to get.
- `-Name` <String[]> · **required** · pipeline by property name · wildcards — Gets one or more CIM sessions which contain the specified friendly names.

**Output:** CimSession

**Example**

```powershell
Get-CimSession
```

### Import-BinaryMiLog (PowerShell 5.1 cmdlet)

Module CimCmdlets

Re-creates CIM objects from a binary MI log file.

**Syntax** (one line per parameter set)

```powershell
Import-BinaryMiLog [-Path] <string> [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · wildcards — Specifies the path of the file in which to store the binary representation of the object.

**Output:** Object

**Example**

```powershell
Import-BinaryMiLog -Path .\services.bmil
```

### Invoke-CimMethod (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: icim

Calls a method of a CIM class or instance.

**Syntax** (one line per parameter set)

```powershell
Invoke-CimMethod [-ClassName] <string> [[-Arguments] <IDictionary>] [-MethodName] <string> [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-ClassName] <string> [[-Arguments] <IDictionary>] [-MethodName] <string> -CimSession <CimSession[]> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -ResourceUri <uri> [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-InputObject] <ciminstance> [[-Arguments] <IDictionary>] [-MethodName] <string> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-InputObject] <ciminstance> [[-Arguments] <IDictionary>] [-MethodName] <string> [-ResourceUri <uri>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -ResourceUri <uri> -CimSession <CimSession[]> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-CimClass] <cimclass> [[-Arguments] <IDictionary>] [-MethodName] <string> [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [-CimClass] <cimclass> [[-Arguments] <IDictionary>] [-MethodName] <string> -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -Query <string> [-QueryDialect <string>] [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Invoke-CimMethod [[-Arguments] <IDictionary>] [-MethodName] <string> -Query <string> -CimSession <CimSession[]> [-QueryDialect <string>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ClassName` <String> · **required** · position 0 · pipeline by property name · alias -Class — Specifies the name of the CIM class for which to perform the operation.
- `-ResourceUri` <Uri> · **required** · pipeline by property name — Specifies the resource uniform resource identifier (URI) of the resource class or instance.
- `-CimClass` <CimClass> · **required** · position 0 · pipeline by value — Specifies a CIM class object that represents a CIM class definition on the server.
- `-Query` <String> · **required** · pipeline by property name — Specifies a query to run on the CIM server. A method is invoked on the instances received as a result of the query.
- `-QueryDialect` <String> · pipeline by property name · default WQL — Specifies the query language used for the Query parameter. The acceptable values for this parameter are: WQL or CQL.
- `-InputObject` <CimInstance> · **required** · position 0 · pipeline by value · alias -CimInstance — Specifies a CIM instance object to use as input to invoke a method. This parameter can only be used to invoke instance methods.
- `-ComputerName` <String[]> · pipeline by property name · alias -CN, -ServerName — Specifies the name of the computer on which you want to run the CIM operation.
- `-CimSession` <CimSession[]> · **required** · pipeline by value and by property name — Runs the command using the specified CIM session. Enter a variable that contains the CIM session, or a command that creates or gets the CIM session, such as the New-CimSession or Get-CimSession cmdlets.
- `-Arguments` <IDictionary> · position 1 · pipeline by property name — Specifies the parameters to pass to the called method. Specify the values for this parameter as name-value pairs, stored in a hash table.
- `-MethodName` <String> · **required** · position 2 · pipeline by property name · alias -Name — Specifies the name of the CIM method to invoke. This parameter is mandatory and cannot be null or empty.
- `-Namespace` <String> · pipeline by property name — Specifies the namespace for the CIM operation. The default namespace is root/CIMV2.
- `-OperationTimeoutSec` <UInt32> · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the computer.

**Output:** PSCustomObject

**Example**

```powershell
Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = 'notepad.exe' }
```

### New-CimInstance (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: ncim

Creates a new CIM instance on a CIM server.

**Syntax** (one line per parameter set)

```powershell
New-CimInstance [-ClassName] <string> [[-Property] <IDictionary>] [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [-ClassName] <string> [[-Property] <IDictionary>] -CimSession <CimSession[]> [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [[-Property] <IDictionary>] -ResourceUri <uri> -CimSession <CimSession[]> [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [[-Property] <IDictionary>] -ResourceUri <uri> [-Key <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [-CimClass] <cimclass> [[-Property] <IDictionary>] -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]

New-CimInstance [-CimClass] <cimclass> [[-Property] <IDictionary>] [-OperationTimeoutSec <uint32>] [-ComputerName <string[]>] [-ClientOnly] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-ClassName` <String> · **required** · position 0 · pipeline by property name — Specifies the name of the CIM class of which the operation creates an instance.
- `-ResourceUri` <Uri> · **required** · pipeline by property name — Specifies the resource uniform resource identifier (URI) of the resource class or instance.
- `-Key` <String[]> · pipeline by property name — Specifies the properties that are used as keys. CimSession and ComputerName cannot be used when Key is specified.
- `-CimClass` <CimClass> · **required** · position 0 · pipeline by value — Specifies a CIM class object that represents the type of the instance.
- `-Property` <IDictionary> · position 1 · pipeline by property name · alias -Arguments — Specifies the properties of the CIM instance using a hash table (name-value pairs).
- `-Namespace` <String> · pipeline by property name — Specifies the namespace of the class for the new instance. The default namespace is root/CIMV2.
- `-OperationTimeoutSec` <UInt32> · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the CIM server.
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command using the specified CIM session. Enter a variable that contains the CIM session, or a command that creates or gets the CIM session, such as the New-CimSession or Get-CimSession cmdlets.
- `-ComputerName` <String[]> · pipeline by property name · alias -CN, -ServerName — Specifies the name of the computer on which you want to run the CIM operation.
- `-ClientOnly` switch · alias -Local — Indicates that the instance is only created in PowerShell without going to the CIM server.

**Output:** Object

**Example**

```powershell
New-CimInstance -ClassName Win32_Environment -Property @{ Name = 'MYVAR'; VariableValue = '1'; UserName = '<SYSTEM>' }
```

### New-CimSession (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: ncms

Opens a CIM session to a local or remote computer (WSMan or DCOM) for CIM cmdlets.

**Syntax** (one line per parameter set)

```powershell
New-CimSession [[-ComputerName] <string[]>] [[-Credential] <pscredential>] [-Authentication <PasswordAuthenticationMechanism>] [-Name <string>] [-OperationTimeoutSec <uint32>] [-SkipTestConnection] [-Port <uint32>] [-SessionOption <CimSessionOptions>] [<CommonParameters>]

New-CimSession [[-ComputerName] <string[]>] [-CertificateThumbprint <string>] [-Name <string>] [-OperationTimeoutSec <uint32>] [-SkipTestConnection] [-Port <uint32>] [-SessionOption <CimSessionOptions>] [<CommonParameters>]
```

**Arguments**

- `-Authentication` <PasswordAuthenticationMechanism> · pipeline by property name — Specifies the authentication type used for the user's credentials.
- `-Credential` <PSCredential> · position 1 — Specifies a user account that has permission to perform this action. If Credential is not specified, the current user account is used.
- `-CertificateThumbprint` <String> · pipeline by property name — Specifies the digital public key certificate (X.509) of a user account that has permission to perform this action.
- `-ComputerName` <String[]> · position 0 · pipeline by property name · alias -CN, -ServerName — Specifies the name of the computer to which to create the CIM session.
- `-Name` <String> · pipeline by property name — Specifies a friendly name for the CIM session.
- `-OperationTimeoutSec` <UInt32> · pipeline by property name · alias -OT — Duration for which the cmdlet waits for a response from the server.
- `-SkipTestConnection` switch · pipeline by property name — By default, the New-CimSession cmdlet establishes a connection with a remote WS-Management endpoint for two reasons: to verify that the remote server is listening on the port number that is specified using the Port parameter, and to verify the specified account credentials.
- `-Port` <UInt32> · pipeline by property name — Specifies the network port on the remote computer that is used for this connection.
- `-SessionOption` <CimSessionOptions> · pipeline by property name — Sets advanced options for the new CIM session. Enter the name of a CimSessionOption object created using the New-CimSessionOption cmdlet.

**Output:** CimSession

**Example**

```powershell
$cs = New-CimSession -ComputerName Server01
```

### New-CimSessionOption (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: ncso

Creates options for New-CimSession, such as using DCOM instead of WSMan.

**Syntax** (one line per parameter set)

```powershell
New-CimSessionOption [-Protocol] <ProtocolType> [-UICulture <cultureinfo>] [-Culture <cultureinfo>] [<CommonParameters>]

New-CimSessionOption [-NoEncryption] [-SkipCACheck] [-SkipCNCheck] [-SkipRevocationCheck] [-EncodePortInServicePrincipalName] [-Encoding <PacketEncoding>] [-HttpPrefix <uri>] [-MaxEnvelopeSizeKB <uint32>] [-ProxyAuthentication <PasswordAuthenticationMechanism>] [-ProxyCertificateThumbprint <string>] [-ProxyCredential <pscredential>] [-ProxyType <ProxyType>] [-UseSsl] [-UICulture <cultureinfo>] [-Culture <cultureinfo>] [<CommonParameters>]

New-CimSessionOption [-Impersonation <ImpersonationType>] [-PacketIntegrity] [-PacketPrivacy] [-UICulture <cultureinfo>] [-Culture <cultureinfo>] [<CommonParameters>]
```

**Arguments**

- `-NoEncryption` switch — Specifies that data encryption is turned off.
- `-SkipCACheck` switch · pipeline by property name — Indicates that when connecting over HTTPS, the client does not validate that the server certificate is signed by a trusted certification authority (CA).
- `-SkipCNCheck` switch · pipeline by property name — Indicates that the certificate common name (CN) of the server does not need to match the hostname of the server.
- `-SkipRevocationCheck` switch · pipeline by property name — Indicates that the revocation check for server certificates is skipped.
- `-EncodePortInServicePrincipalName` switch · pipeline by property name — Indicates that the Kerberos connection is connecting to a service whose service principal name (SPN) includes the service port number.
- `-Encoding` <PacketEncoding> · pipeline by property name — Specifies the encoding used for the WsMan protocol. The acceptable values for this parameter are: Default, Utf8, or Utf16.
- `-HttpPrefix` <Uri> · pipeline by property name — Specifies the part of the HTTP URL after the computer name and port number.
- `-MaxEnvelopeSizeKB` <UInt32> · pipeline by property name — Specifies the size limit of WsMan XML messages for either direction.
- `-ProxyAuthentication` <PasswordAuthenticationMechanism> · pipeline by property name — Specifies the authentication method to use for proxy resolution. The acceptable values for this parameter are: Default, Digest, Negotiate, Basic, Kerberos, NtlmDomain, or CredSsp.
- `-ProxyCertificateThumbprint` <String> · pipeline by property name — Specifies the (x.509) digital public key certificate of a user account for proxy authentication.
- `-ProxyCredential` <PSCredential> — Specifies the credentials to use for proxy authentication.
- `-ProxyType` <ProxyType> · pipeline by property name · default InternetExplorer — Specifies the host name resolution mechanism to use. The acceptable values for this parameter are: None, WinHttp, Auto, or InternetExplorer.
- `-UseSsl` switch · pipeline by property name — Indicates that SSL should be used to establish a connection to the remote computer.
- `-Impersonation` <ImpersonationType> — Creates a DCOM session to Windows Management Instrumentation (WMI) using impersonation.
- `-PacketIntegrity` switch — Specifies that the DCOM session created to WMI uses the Component Object Model (COM) _PacketIntegrity_ functionality.
- `-PacketPrivacy` switch — Creates a DCOM session to WMI using the COM _PacketPrivacy_. By default, all CIM sessions created using DCOM have the PacketPrivacy parameter set to true.
- `-Protocol` <ProtocolType> · **required** · position 0 · pipeline by property name — Specifies the protocol to use. The acceptable values for this parameter are: Dcom, Default, or Wsman.
- `-UICulture` <CultureInfo> · pipeline by property name — Specifies the user interface culture to use for the CIM session.
- `-Culture` <CultureInfo> · pipeline by property name — Specifies the user interface culture to use for the CIM session.

**Output:** CimSessionOptions

**Example**

```powershell
New-CimSession -ComputerName OldServer -SessionOption (New-CimSessionOption -Protocol Dcom)
```

### Register-CimIndicationEvent (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: rcie

Subscribes to CIM indications (events) with a class name or WQL query.

**Syntax** (one line per parameter set)

```powershell
Register-CimIndicationEvent [-ClassName] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-CimIndicationEvent [-ClassName] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] -CimSession <CimSession> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-CimIndicationEvent [-Query] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] -CimSession <CimSession> [-Namespace <string>] [-QueryDialect <string>] [-OperationTimeoutSec <uint32>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]

Register-CimIndicationEvent [-Query] <string> [[-SourceIdentifier] <string>] [[-Action] <scriptblock>] [-Namespace <string>] [-QueryDialect <string>] [-OperationTimeoutSec <uint32>] [-ComputerName <string>] [-MessageData <psobject>] [-SupportEvent] [-Forward] [-MaxTriggerCount <int>] [<CommonParameters>]
```

**Arguments**

- `-Namespace` <String> — Specifies the namespace for the CIM operation. The default namespace is root/CIMV2.
- `-ClassName` <String> · **required** · position 0 — Specifies the indication class to which you are subscribing. You can use tab completion to browse the list of classes, because PowerShell gets a list of classes from the local WMI server to provide a list of class names.
- `-Query` <String> · **required** · position 0 — Specifies a query to run on the CIM server. If the value specified contains double quotes ", single quotes ', or a backslash \, you must escape those characters by prefixing them with the backslash character.
- `-QueryDialect` <String> · default WQL — Specifies the query language used for the Query parameter. The acceptable values for this parameter are: WQL or CQL.
- `-OperationTimeoutSec` <UInt32> · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the computer.
- `-CimSession` <CimSession> · **required** — Runs the command using the specified CIM session. Enter a variable that contains the CIM session, or a command that creates or gets the CIM session, such as the New-CimSession or Get-CimSession cmdlets.
- `-ComputerName` <String> · alias -CN, -ServerName — Specifies the name of the computer on which you want to run the CIM operation.
- `-SourceIdentifier` <String> · position 100 — Specifies a name for the subscription. The name that you specify must be unique in the current session.
- `-Action` <ScriptBlock> · position 101 — Specifies the commands that handle the events. The commands specified by this parameter run when an event is raised, instead of sending the event to the event queue.
- `-MessageData` <PSObject> — Specifies any additional data to associate with this event subscription.
- `-SupportEvent` switch — Indicates that the event subscription is hidden. Use this parameter when the current subscription is part of a more complex event registration mechanism and it should not be discovered independently.
- `-Forward` switch — Indicates that events for the subscription are forwarded to the session on the local computer.
- `-MaxTriggerCount` <Int32> — Parameter to indicate that the subscriber should be auto-unregistered after being triggered for specified times.

**Output:** Object

**Example**

```powershell
Register-CimIndicationEvent -ClassName Win32_ProcessStartTrace -SourceIdentifier ProcStart
```

### Remove-CimInstance (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: rcim

Deletes a CIM instance.

**Syntax** (one line per parameter set)

```powershell
Remove-CimInstance [-InputObject] <ciminstance> [-ResourceUri <uri>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimInstance [-InputObject] <ciminstance> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimInstance [-Query] <string> [[-Namespace] <string>] -CimSession <CimSession[]> [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimInstance [-Query] <string> [[-Namespace] <string>] [-ComputerName <string[]>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command using the specified CIM session. Enter a variable that contains the CIM session, or a command that creates or gets the CIM session, such as the New-CimSession or Get-CimSession cmdlets.
- `-ResourceUri` <Uri> · pipeline by property name — Specifies the resource uniform resource identifier (URI) of the resource class or instance.
- `-ComputerName` <String[]> · pipeline by property name · alias -CN, -ServerName — Specifies the name of the computer on which you want to run the CIM operation.
- `-Namespace` <String> · position 1 · pipeline by property name — Specifies the namespace for the CIM operation. The default namespace is root/CIMV2.
- `-OperationTimeoutSec` <UInt32> · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the computer.
- `-InputObject` <CimInstance> · **required** · position 0 · pipeline by value · alias -CimInstance — Specifies a CIM instance object to be removed from the CIM server. The object passed to the cmdlet is not changed, only the instance in the CIM server is removed.
- `-Query` <String> · **required** · position 0 · pipeline by property name — Specifies a query to run on the CIM server. You can specify the query dialect using the QueryDialect parameter.
- `-QueryDialect` <String> · pipeline by property name · default WQL — Specifies the query language used for the Query parameter. The acceptable values for this parameter are: WQL or CQL.

**Output:** None

**Example**

```powershell
Get-CimInstance Win32_Environment -Filter "Name='MYVAR'" | Remove-CimInstance
```

### Remove-CimSession (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: rcms

Closes CIM sessions.

**Syntax** (one line per parameter set)

```powershell
Remove-CimSession [-CimSession] <CimSession[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession [-ComputerName] <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession [-Id] <uint32[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession -InstanceId <guid[]> [-WhatIf] [-Confirm] [<CommonParameters>]

Remove-CimSession -Name <string[]> [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · **required** · position 0 · pipeline by value and by property name — Specifies the session objects of the CIM sessions to close.
- `-ComputerName` <String[]> · **required** · position 0 · pipeline by property name · alias -CN, -ServerName · wildcards — Specifies an array of names of computers. Removes the sessions that connect to the specified computers.
- `-Id` <UInt32[]> · **required** · position 0 · pipeline by property name — Specifies the ID of the CIM session to remove. Specify one or more IDs separated by commas, or use the range operator (..) to specify a range of IDs.
- `-InstanceId` <Guid[]> · **required** · pipeline by property name — Specifies the instance ID of the CIM session to remove. InstanceId is a Globally Unique Identifier (GUID) that uniquely identifies a CIM session.
- `-Name` <String[]> · **required** · pipeline by property name · wildcards — Specifies the friendly name of the CIM session to remove. You can use wildcard characters with this parameter.

**Output:** Object

**Example**

```powershell
Get-CimSession | Remove-CimSession
```

### Set-CimInstance (PowerShell 5.1 cmdlet)

Module CimCmdlets · aliases: scim

Changes properties of a CIM instance.

**Syntax** (one line per parameter set)

```powershell
Set-CimInstance [-InputObject] <ciminstance> [-ComputerName <string[]>] [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-Property <IDictionary>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-CimInstance [-Query] <string> -CimSession <CimSession[]> -Property <IDictionary> [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-CimInstance [-InputObject] <ciminstance> -CimSession <CimSession[]> [-ResourceUri <uri>] [-OperationTimeoutSec <uint32>] [-Property <IDictionary>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-CimInstance [-Query] <string> -Property <IDictionary> [-ComputerName <string[]>] [-Namespace <string>] [-OperationTimeoutSec <uint32>] [-QueryDialect <string>] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the cmdlets on a remote computer. Enter a computer name or a session object, such as the output of a New-CimSession or Get-CimSession cmdlet.
- `-ComputerName` <String[]> · pipeline by property name · alias -CN, -ServerName — Specifies the name of the computer on which you want to run the CIM operation.
- `-ResourceUri` <Uri> · pipeline by property name — Specifies the resource uniform resource identifier (URI) of the resource class or instance.
- `-Namespace` <String> · pipeline by property name — Specifies the namespace for the CIM operation. The default namespace is root/CIMV2.
- `-OperationTimeoutSec` <UInt32> · alias -OT — Specifies the amount of time that the cmdlet waits for a response from the computer.
- `-InputObject` <CimInstance> · **required** · position 0 · pipeline by value · alias -CimInstance — Specifies a CIM instance object to use as input.
- `-Query` <String> · **required** · position 0 · pipeline by property name — Specifies a query to run on the CIM server to retrieve CIM instances on which to run the cmdlet.
- `-QueryDialect` <String> · pipeline by property name — Specifies the query language used for the Query parameter. The acceptable values for this parameter are: WQL or CQL.
- `-Property` <IDictionary> · **required** · pipeline by property name · alias -Arguments — Specifies the properties of the CIM instance as a hash table (using name-value pairs).
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None; CimInstance

**Example**

```powershell
Get-CimInstance Win32_Environment -Filter "Name='MYVAR'" | Set-CimInstance -Property @{ VariableValue = '2' }
```

## Commands: PackageManagement

### Find-Package (PowerShell 5.1 cmdlet)

Module PackageManagement

Searches registered package sources for software packages.

**Syntax** (one line per parameter set)

```powershell
Find-Package [[-Name] <string[]>] [-IncludeDependencies] [-AllVersions] [-Source <string[]>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [<CommonParameters>]
```

**Arguments**

- `-IncludeDependencies` switch — Also returns or saves the packages that this one depends on.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Source` <String[]> · pipeline by property name — The package source (repository name or location) to use.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Proxy` <Uri> — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> — The account to use for the proxy server given in -Proxy.
- `-Name` <String[]> · position 0 — The name of the package, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String[]> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-PublishLocation` <String> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <String> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <String> — The URI to publish scripts to in this repository.
- `-Type` <String> · values: Module, Script, All — Limits the results to a kind of item: Module, Script or Both (or the resource type shown in the values).
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Includes` <String[]> · values: DscResource, Cmdlet, Function, Workflow, RoleCapability — Limits results to items that include these kinds: DscResource, Cmdlet, Function, Workflow, RoleCapability.
- `-DscResource` <String[]> — Finds modules that contain these DSC resources.
- `-RoleCapability` <String[]> — Finds modules that contain these JEA role capabilities.
- `-Command` <String[]> — Finds modules that contain these command names.

**Example**

```powershell
Find-Package -Name jquery -ProviderName NuGet
```

### Find-PackageProvider (PowerShell 5.1 cmdlet)

Module PackageManagement

Lists package providers available to install.

**Syntax** (one line per parameter set)

```powershell
Find-PackageProvider [[-Name] <string[]>] [-AllVersions] [-Source <string[]>] [-IncludeDependencies] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Force] [-ForceBootstrap] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the package provider, or several names. Find and Get commands accept wildcards.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Source` <String[]> · pipeline by property name — The package source (repository name or location) to use.
- `-IncludeDependencies` switch — Also returns or saves the package providers that this one depends on.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Proxy` <Uri> — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> — The account to use for the proxy server given in -Proxy.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package provider.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.

**Example**

```powershell
Find-PackageProvider -Name NuGet
```

### Get-Package (PowerShell 5.1 cmdlet)

Module PackageManagement

Lists packages installed through PackageManagement (and programs on Windows).

**Syntax** (one line per parameter set)

```powershell
Get-Package [[-Name] <string[]>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Get-Package [[-Name] <string[]>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-AdditionalArguments <string[]>] [<CommonParameters>]

Get-Package [[-Name] <string[]>] [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-PackageManagementProvider <string>] [-Type <string>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — The name of the package, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String[]> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-IncludeWindowsInstaller` switch — Includes packages installed by Windows Installer (msi) in the results.
- `-IncludeSystemComponent` switch — Includes packages marked as system components in the results.
- `-AdditionalArguments` <String[]> — Extra arguments passed to the package provider or installer.
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-Type` <String> · values: Module, Script, All — Limits the results to a kind of item: Module, Script or Both (or the resource type shown in the values).
- `-Scope` <String> · values: CurrentUser, AllUsers — Where to install: CurrentUser (your profile, no admin rights) or AllUsers (machine-wide, needs an elevated shell).
- `-AllowClobber` switch — Installs even if the module has commands with the same names as commands already on the system.
- `-SkipPublisherCheck` switch — Installs even when the new version is signed by a different publisher than the installed one (the check guards against tampering).
- `-InstallUpdate` switch — Installs updates of the dependencies too.
- `-NoPathUpdate` switch — Does not add the scripts install folder to the PATH environment variable.

**Example**

```powershell
Get-Package -ProviderName Programs
```

### Get-PackageProvider (PowerShell 5.1 cmdlet)

Module PackageManagement

Lists the package providers loaded or available on the computer.

**Syntax** (one line per parameter set)

```powershell
Get-PackageProvider [[-Name] <string[]>] [-ListAvailable] [-Force] [-ForceBootstrap] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — The name of the package provider, or several names. Find and Get commands accept wildcards.
- `-ListAvailable` switch — Lists every available item instead of only the current ones.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package provider.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.

**Example**

```powershell
Get-PackageProvider -ListAvailable
```

### Get-PackageSource (PowerShell 5.1 cmdlet)

Module PackageManagement

Lists the package sources registered for providers.

**Syntax** (one line per parameter set)

```powershell
Get-PackageSource [[-Name] <string>] [-Location <string>] [-Force] [-ForceBootstrap] [-ProviderName <string[]>] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · position 0 — The name of the package source, or several names. Find and Get commands accept wildcards.
- `-Location` <String> — The URI or folder of the package source.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package source.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String[]> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-PublishLocation` <String> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <String> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <String> — The URI to publish scripts to in this repository.

**Example**

```powershell
Get-PackageSource
```

### Import-PackageProvider (PowerShell 5.1 cmdlet)

Module PackageManagement

Loads a package provider into the session.

**Syntax** (one line per parameter set)

```powershell
Import-PackageProvider [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Force] [-ForceBootstrap] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the package provider, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package provider.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.

**Example**

```powershell
Import-PackageProvider -Name NuGet
```

### Install-Package (PowerShell 5.1 cmdlet)

Module PackageManagement

Installs software packages from a package source.

**Syntax** (one line per parameter set)

```powershell
Install-Package [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Source <string[]>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string[]>] [<CommonParameters>]

Install-Package [-InputObject] <SoftwareIdentity[]> [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]

Install-Package [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <SoftwareIdentity[]> · **required** · position 0 · pipeline by value — The object to act on, usually piped from the matching Find- or Get- command.
- `-Name` <String[]> · **required** · position 0 — The name of the package, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-Source` <String[]> · pipeline by property name — The package source (repository name or location) to use.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Proxy` <Uri> — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> — The account to use for the proxy server given in -Proxy.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String[]> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-IncludeWindowsInstaller` switch — Includes packages installed by Windows Installer (msi) in the results.
- `-IncludeSystemComponent` switch — Includes packages marked as system components in the results.
- `-AdditionalArguments` <String[]> — Extra arguments passed to the package provider or installer.
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-PublishLocation` <String> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <String> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <String> — The URI to publish scripts to in this repository.
- `-Type` <String> · values: Module, Script, All — Limits the results to a kind of item: Module, Script or Both (or the resource type shown in the values).
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Includes` <String[]> · values: DscResource, Cmdlet, Function, Workflow, RoleCapability — Limits results to items that include these kinds: DscResource, Cmdlet, Function, Workflow, RoleCapability.
- `-DscResource` <String[]> — Finds modules that contain these DSC resources.
- `-RoleCapability` <String[]> — Finds modules that contain these JEA role capabilities.
- `-Command` <String[]> — Finds modules that contain these command names.
- `-Scope` <String> · values: CurrentUser, AllUsers — Where to install: CurrentUser (your profile, no admin rights) or AllUsers (machine-wide, needs an elevated shell).
- `-AllowClobber` switch — Installs even if the module has commands with the same names as commands already on the system.
- `-SkipPublisherCheck` switch — Installs even when the new version is signed by a different publisher than the installed one (the check guards against tampering).
- `-InstallUpdate` switch — Installs updates of the dependencies too.
- `-NoPathUpdate` switch — Does not add the scripts install folder to the PATH environment variable.

**Example**

```powershell
Install-Package -Name Newtonsoft.Json -ProviderName NuGet -Scope CurrentUser
```

### Install-PackageProvider (PowerShell 5.1 cmdlet)

Module PackageManagement

Installs a package provider such as NuGet.

**Syntax** (one line per parameter set)

```powershell
Install-PackageProvider [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Credential <pscredential>] [-Scope <string>] [-Source <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-PackageProvider [-InputObject] <SoftwareIdentity[]> [-Scope <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 — The name of the package provider, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Scope` <String> · values: CurrentUser, AllUsers — Where to install: CurrentUser (your profile, no admin rights) or AllUsers (machine-wide, needs an elevated shell).
- `-Source` <String[]> · pipeline by property name — The package source (repository name or location) to use.
- `-InputObject` <SoftwareIdentity[]> · **required** · position 0 · pipeline by value — The object to act on, usually piped from the matching Find- or Get- command.
- `-Proxy` <Uri> — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> — The account to use for the proxy server given in -Proxy.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package provider.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.

**Example**

```powershell
Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force
```

### Register-PackageSource (PowerShell 5.1 cmdlet)

Module PackageManagement

Adds a package source for a provider.

**Syntax** (one line per parameter set)

```powershell
Register-PackageSource [[-Name] <string>] [[-Location] <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string>] [<CommonParameters>]

Register-PackageSource [[-Name] <string>] [[-Location] <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

**Arguments**

- `-Proxy` <Uri> — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> — The account to use for the proxy server given in -Proxy.
- `-Name` <String> · position 0 — The name of the package source, or several names. Find and Get commands accept wildcards.
- `-Location` <String> · position 1 — The URI or folder of the package source.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Trusted` switch — Marks the repository or source as trusted, so installing from it does not ask for confirmation.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package source.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-PublishLocation` <String> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <String> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <String> — The URI to publish scripts to in this repository.

**Example**

```powershell
Register-PackageSource -Name MyFeed -Location \\Server\Packages -ProviderName NuGet -Trusted
```

### Save-Package (PowerShell 5.1 cmdlet)

Module PackageManagement

Downloads packages to a folder without installing them.

**Syntax** (one line per parameter set)

```powershell
Save-Package [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-Source <string[]>] [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string[]>] [<CommonParameters>]

Save-Package -InputObject <SoftwareIdentity> [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Package [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [<CommonParameters>]

Save-Package [-Path <string>] [-LiteralPath <string>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [-Type <string>] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the package, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-Source` <String[]> · pipeline by property name — The package source (repository name or location) to use.
- `-Path` <String> — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-LiteralPath` <String> — The path, used exactly as typed: wildcard characters such as [ ] are not interpreted.
- `-InputObject` <SoftwareIdentity> · **required** · pipeline by value — The object to act on, usually piped from the matching Find- or Get- command.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Proxy` <Uri> — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> — The account to use for the proxy server given in -Proxy.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String[]> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-PublishLocation` <String> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <String> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <String> — The URI to publish scripts to in this repository.
- `-Type` <String> · values: Module, Script, All — Limits the results to a kind of item: Module, Script or Both (or the resource type shown in the values).
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Includes` <String[]> · values: DscResource, Cmdlet, Function, Workflow, RoleCapability — Limits results to items that include these kinds: DscResource, Cmdlet, Function, Workflow, RoleCapability.
- `-DscResource` <String[]> — Finds modules that contain these DSC resources.
- `-RoleCapability` <String[]> — Finds modules that contain these JEA role capabilities.
- `-Command` <String[]> — Finds modules that contain these command names.

**Example**

```powershell
Save-Package -Name Newtonsoft.Json -ProviderName NuGet -Path .\pkgs
```

### Set-PackageSource (PowerShell 5.1 cmdlet)

Module PackageManagement

Changes a registered package source (name, location, trust).

**Syntax** (one line per parameter set)

```powershell
Set-PackageSource [[-Name] <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Location <string>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string>] [<CommonParameters>]

Set-PackageSource -InputObject <PackageSource> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-PackageSource [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]

Set-PackageSource [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-NewLocation <string>] [-NewName <string>] [-Trusted] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

**Arguments**

- `-Proxy` <Uri> — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> — The account to use for the proxy server given in -Proxy.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Name` <String> · position 0 · alias -SourceName — The name of the package source, or several names. Find and Get commands accept wildcards.
- `-Location` <String> — The URI or folder of the package source.
- `-NewLocation` <String> — The new URI or path for the package source.
- `-NewName` <String> — The new name for the package source.
- `-Trusted` switch — Marks the repository or source as trusted, so installing from it does not ask for confirmation.
- `-InputObject` <PackageSource> · **required** · pipeline by value — The object to act on, usually piped from the matching Find- or Get- command.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package source.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-PublishLocation` <String> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <String> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <String> — The URI to publish scripts to in this repository.

**Example**

```powershell
Set-PackageSource -Name MyFeed -Trusted
```

### Uninstall-Package (PowerShell 5.1 cmdlet)

Module PackageManagement

Uninstalls packages installed through PackageManagement.

**Syntax** (one line per parameter set)

```powershell
Uninstall-Package [-InputObject] <SoftwareIdentity[]> [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Uninstall-Package [-Name] <string[]> [-RequiredVersion <string>] [-MinimumVersion <string>] [-MaximumVersion <string>] [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string[]>] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-IncludeWindowsInstaller] [-IncludeSystemComponent] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-AdditionalArguments <string[]>] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-Type <string>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]

Uninstall-Package [-AllVersions] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-Type <string>] [-Scope <string>] [-AllowClobber] [-SkipPublisherCheck] [-InstallUpdate] [-NoPathUpdate] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <SoftwareIdentity[]> · **required** · position 0 · pipeline by value — The object to act on, usually piped from the matching Find- or Get- command.
- `-Name` <String[]> · **required** · position 0 — The name of the package, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <String> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MinimumVersion` <String> — The lowest version to accept.
- `-MaximumVersion` <String> — The highest version to accept.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String[]> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-IncludeWindowsInstaller` switch — Includes packages installed by Windows Installer (msi) in the results.
- `-IncludeSystemComponent` switch — Includes packages marked as system components in the results.
- `-AdditionalArguments` <String[]> — Extra arguments passed to the package provider or installer.
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-Type` <String> · values: Module, Script, All — Limits the results to a kind of item: Module, Script or Both (or the resource type shown in the values).
- `-Scope` <String> · values: CurrentUser, AllUsers — Where to install: CurrentUser (your profile, no admin rights) or AllUsers (machine-wide, needs an elevated shell).
- `-AllowClobber` switch — Installs even if the module has commands with the same names as commands already on the system.
- `-SkipPublisherCheck` switch — Installs even when the new version is signed by a different publisher than the installed one (the check guards against tampering).
- `-InstallUpdate` switch — Installs updates of the dependencies too.
- `-NoPathUpdate` switch — Does not add the scripts install folder to the PATH environment variable.

**Example**

```powershell
Uninstall-Package -Name Newtonsoft.Json
```

### Unregister-PackageSource (PowerShell 5.1 cmdlet)

Module PackageManagement

Removes a registered package source.

**Syntax** (one line per parameter set)

```powershell
Unregister-PackageSource [[-Source] <string>] [-Location <string>] [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-ProviderName <string>] [<CommonParameters>]

Unregister-PackageSource -InputObject <PackageSource[]> [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-PackageSource [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]

Unregister-PackageSource [-Credential <pscredential>] [-Force] [-ForceBootstrap] [-WhatIf] [-Confirm] [-PackageManagementProvider <string>] [-PublishLocation <string>] [-ScriptSourceLocation <string>] [-ScriptPublishLocation <string>] [<CommonParameters>]
```

**Arguments**

- `-Source` <String> · position 0 · alias -Name — The package source (repository name or location) to use.
- `-Location` <String> — The URI or folder of the package source.
- `-InputObject` <PackageSource[]> · **required** · pipeline by value — The object to act on, usually piped from the matching Find- or Get- command.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing package source.
- `-ForceBootstrap` switch — Installs the NuGet package provider automatically, without asking, when it is missing.
- `-ProviderName` <String> · pipeline by property name · alias -Provider · values: Programs, msi, msu, PowerShellGet, nuget, psl, chocolatey — Limits the command to one or more package providers (for example NuGet, PowerShellGet, msi, Programs).
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).
- `-PublishLocation` <String> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <String> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <String> — The URI to publish scripts to in this repository.

**Example**

```powershell
Unregister-PackageSource -Name MyFeed
```

## Commands: PowerShellGet

### Find-Command (PowerShell 5.1 function)

Module PowerShellGet

Finds commands in modules published in a repository (PowerShell Gallery by default).

**Syntax** (one line per parameter set)

```powershell
Find-Command [[-Name] <string[]>] [-ModuleName <string>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-Tag <string[]>] [-Filter <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — The name of the command, or several names. Find and Get commands accept wildcards.
- `-ModuleName` <String> — The module the command belongs to, or the modules to search.
- `-MinimumVersion` <Version> — The lowest version to accept.
- `-MaximumVersion` <Version> — The highest version to accept.
- `-RequiredVersion` <Version> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Repository` <String[]> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.

**Example**

```powershell
Find-Command -Name Get-ADUser -Repository PSGallery
```

### Find-DscResource (PowerShell 5.1 function)

Module PowerShellGet

Finds DSC resources in modules published in a repository.

**Syntax** (one line per parameter set)

```powershell
Find-DscResource [[-Name] <string[]>] [-ModuleName <string>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-Tag <string[]>] [-Filter <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — The name of the dsc resource, or several names. Find and Get commands accept wildcards.
- `-ModuleName` <String> — The module the dsc resource belongs to, or the modules to search.
- `-MinimumVersion` <Version> — The lowest version to accept.
- `-MaximumVersion` <Version> — The highest version to accept.
- `-RequiredVersion` <Version> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Repository` <String[]> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.

**Example**

```powershell
Find-DscResource -Name xFirewall
```

### Find-Module (PowerShell 5.1 function)

Module PowerShellGet · aliases: fimo

Finds modules in a repository by name, tag or other criteria.

**Syntax** (one line per parameter set)

```powershell
Find-Module [[-Name] <string[]>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-IncludeDependencies] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-DscResource <string[]>] [-RoleCapability <string[]>] [-Command <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [-Credential <pscredential>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the module, or several names. Find and Get commands accept wildcards.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-IncludeDependencies` switch — Also returns or saves the modules that this one depends on.
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Includes` <String[]> · values: DscResource, Cmdlet, Function, RoleCapability — Limits results to items that include these kinds: DscResource, Cmdlet, Function, Workflow, RoleCapability.
- `-DscResource` <String[]> — Finds modules that contain these DSC resources.
- `-RoleCapability` <String[]> — Finds modules that contain these JEA role capabilities.
- `-Command` <String[]> — Finds modules that contain these command names.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Repository` <String[]> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Example**

```powershell
Find-Module -Name Pester -AllVersions
```

### Find-RoleCapability (PowerShell 5.1 function)

Module PowerShellGet

Finds JEA role capabilities in published modules.

**Syntax** (one line per parameter set)

```powershell
Find-RoleCapability [[-Name] <string[]>] [-ModuleName <string>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-Tag <string[]>] [-Filter <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 — The name of the role capability, or several names. Find and Get commands accept wildcards.
- `-ModuleName` <String> — The module the role capability belongs to, or the modules to search.
- `-MinimumVersion` <Version> — The lowest version to accept.
- `-MaximumVersion` <Version> — The highest version to accept.
- `-RequiredVersion` <Version> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Repository` <String[]> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.

**Example**

```powershell
Find-RoleCapability -Name HelpDesk
```

### Find-Script (PowerShell 5.1 function)

Module PowerShellGet

Finds scripts in a repository.

**Syntax** (one line per parameter set)

```powershell
Find-Script [[-Name] <string[]>] [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-AllVersions] [-IncludeDependencies] [-Filter <string>] [-Tag <string[]>] [-Includes <string[]>] [-Command <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Repository <string[]>] [-Credential <pscredential>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the script, or several names. Find and Get commands accept wildcards.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-IncludeDependencies` switch — Also returns or saves the scripts that this one depends on.
- `-Filter` <String> — A search string matched against names, descriptions and tags in the repository.
- `-Tag` <String[]> — Filters by tag (keywords set by the author); several tags must all match.
- `-Includes` <String[]> · values: Function, Workflow — Limits results to items that include these kinds: DscResource, Cmdlet, Function, Workflow, RoleCapability.
- `-Command` <String[]> — Finds modules that contain these command names.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Repository` <String[]> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.

**Example**

```powershell
Find-Script -Name Install-Git
```

### Get-InstalledModule (PowerShell 5.1 function)

Module PowerShellGet

Lists modules installed by PowerShellGet (Install-Module).

**Syntax** (one line per parameter set)

```powershell
Get-InstalledModule [[-Name] <string[]>] [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-AllVersions] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the installed module, or several names. Find and Get commands accept wildcards.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-AllVersions` switch — Includes every available version, not only the newest.

**Example**

```powershell
Get-InstalledModule
```

### Get-InstalledScript (PowerShell 5.1 function)

Module PowerShellGet

Lists scripts installed by PowerShellGet (Install-Script).

**Syntax** (one line per parameter set)

```powershell
Get-InstalledScript [[-Name] <string[]>] [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the installed script, or several names. Find and Get commands accept wildcards.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.

**Example**

```powershell
Get-InstalledScript
```

### Get-PSRepository (PowerShell 5.1 function)

Module PowerShellGet

Lists registered PowerShell repositories (PSGallery and others).

**Syntax** (one line per parameter set)

```powershell
Get-PSRepository [[-Name] <string[]>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the psrepository, or several names. Find and Get commands accept wildcards.

**Example**

```powershell
Get-PSRepository
```

### Install-Module (PowerShell 5.1 function)

Module PowerShellGet · aliases: inmo

Downloads and installs modules from a repository.

**Syntax** (one line per parameter set)

```powershell
Install-Module [-Name] <string[]> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Credential <pscredential>] [-Scope <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllowClobber] [-SkipPublisherCheck] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-Module [-InputObject] <psobject[]> [-Credential <pscredential>] [-Scope <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-AllowClobber] [-SkipPublisherCheck] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the module, or several names. Find and Get commands accept wildcards.
- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value and by property name — The object to act on, usually piped from the matching Find- or Get- command.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-Repository` <String[]> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Scope` <String> · values: CurrentUser, AllUsers — Where to install: CurrentUser (your profile, no admin rights) or AllUsers (machine-wide, needs an elevated shell).
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-AllowClobber` switch — Installs even if the module has commands with the same names as commands already on the system.
- `-SkipPublisherCheck` switch — Installs even when the new version is signed by a different publisher than the installed one (the check guards against tampering).
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing module.

**Example**

```powershell
Install-Module -Name Pester -Scope CurrentUser -Force
```

### Install-Script (PowerShell 5.1 function)

Module PowerShellGet

Downloads and installs scripts from a repository.

**Syntax** (one line per parameter set)

```powershell
Install-Script [-Name] <string[]> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Scope <string>] [-NoPathUpdate] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Install-Script [-InputObject] <psobject[]> [-Scope <string>] [-NoPathUpdate] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the script, or several names. Find and Get commands accept wildcards.
- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value and by property name — The object to act on, usually piped from the matching Find- or Get- command.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-Repository` <String[]> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Scope` <String> · values: CurrentUser, AllUsers — Where to install: CurrentUser (your profile, no admin rights) or AllUsers (machine-wide, needs an elevated shell).
- `-NoPathUpdate` switch — Does not add the scripts install folder to the PATH environment variable.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing script.

**Example**

```powershell
Install-Script -Name Install-Git -Scope CurrentUser
```

### New-ScriptFileInfo (PowerShell 5.1 function)

Module PowerShellGet

Creates a script file with a metadata comment block (version, author, description) for publishing.

**Syntax** (one line per parameter set)

```powershell
New-ScriptFileInfo [[-Path] <string>] -Description <string> [-Version <version>] [-Author <string>] [-Guid <guid>] [-CompanyName <string>] [-Copyright <string>] [-RequiredModules <Object[]>] [-ExternalModuleDependencies <string[]>] [-RequiredScripts <string[]>] [-ExternalScriptDependencies <string[]>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · position 0 · pipeline by property name — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-Version` <Version> — The version of the script file info to use or set.
- `-Author` <String> — The author name stored in the manifest.
- `-Description` <String> · **required** — The description text of the script file info.
- `-Guid` <Guid> — The unique identifier (GUID) of the script file info.
- `-CompanyName` <String> — The company or vendor stored in the manifest.
- `-Copyright` <String> — The copyright statement stored in the manifest.
- `-RequiredModules` <Object[]> — Modules that must be imported first, written to the manifest.
- `-ExternalModuleDependencies` <String[]> — Modules that this one needs but that are not in the gallery, listed for information.
- `-RequiredScripts` <String[]> — Scripts this script needs, written to its metadata.
- `-ExternalScriptDependencies` <String[]> — Scripts that this one needs but that are not in the gallery, listed for information.
- `-Tags` <String[]> — Tags (keywords) to store with the script file info, used when searching a gallery.
- `-ProjectUri` <Uri> — The project web site URI, stored in the manifest.
- `-LicenseUri` <Uri> — The license URI, stored in the manifest.
- `-IconUri` <Uri> — The icon image URI, stored in the manifest.
- `-ReleaseNotes` <String[]> — Release notes for this version, stored in the manifest.
- `-PassThru` switch — Returns an object for the script file info. By default the command writes no output.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing script file info.

**Example**

```powershell
New-ScriptFileInfo -Path .\Deploy.ps1 -Version 1.0 -Author Ann -Description 'Deploys the app'
```

### Publish-Module (PowerShell 5.1 function)

Module PowerShellGet · aliases: pumo

Publishes a module to a repository such as the PowerShell Gallery.

**Syntax** (one line per parameter set)

```powershell
Publish-Module -Name <string> [-RequiredVersion <version>] [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-FormatVersion <version>] [-ReleaseNotes <string[]>] [-Tags <string[]>] [-LicenseUri <uri>] [-IconUri <uri>] [-ProjectUri <uri>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Publish-Module -Path <string> [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-FormatVersion <version>] [-ReleaseNotes <string[]>] [-Tags <string[]>] [-LicenseUri <uri>] [-IconUri <uri>] [-ProjectUri <uri>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · pipeline by property name — The name of the module, or several names. Find and Get commands accept wildcards.
- `-Path` <String> · **required** · pipeline by property name — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-RequiredVersion` <Version> — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-NuGetApiKey` <String> — The API key for publishing to the gallery (from your gallery account).
- `-Repository` <String> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-FormatVersion` <Version> · values: 1.0 — The manifest format version.
- `-ReleaseNotes` <String[]> — Release notes for this version, stored in the manifest.
- `-Tags` <String[]> — Tags (keywords) to store with the module, used when searching a gallery.
- `-LicenseUri` <Uri> — The license URI, stored in the manifest.
- `-IconUri` <Uri> — The icon image URI, stored in the manifest.
- `-ProjectUri` <Uri> — The project web site URI, stored in the manifest.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing module.

**Example**

```powershell
Publish-Module -Path .\MyTools -NuGetApiKey $key
```

### Publish-Script (PowerShell 5.1 function)

Module PowerShellGet

Publishes a script to a repository.

**Syntax** (one line per parameter set)

```powershell
Publish-Script -Path <string> [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Publish-Script -LiteralPath <string> [-NuGetApiKey <string>] [-Repository <string>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · pipeline by property name — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-LiteralPath` <String> · **required** · pipeline by property name — The path, used exactly as typed: wildcard characters such as [ ] are not interpreted.
- `-NuGetApiKey` <String> — The API key for publishing to the gallery (from your gallery account).
- `-Repository` <String> — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing script.

**Example**

```powershell
Publish-Script -Path .\Deploy.ps1 -NuGetApiKey $key
```

### Register-PSRepository (PowerShell 5.1 function)

Module PowerShellGet

Registers a new PowerShell repository (for example an internal NuGet feed or file share).

**Syntax** (one line per parameter set)

```powershell
Register-PSRepository [-Name] <string> [-SourceLocation] <uri> [-PublishLocation <uri>] [-ScriptSourceLocation <uri>] [-ScriptPublishLocation <uri>] [-Credential <pscredential>] [-InstallationPolicy <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-PackageManagementProvider <string>] [<CommonParameters>]

Register-PSRepository -Default [-InstallationPolicy <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 — The name of the psrepository, or several names. Find and Get commands accept wildcards.
- `-SourceLocation` <Uri> · **required** · position 1 — The URI to search for modules in this repository.
- `-PublishLocation` <Uri> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <Uri> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <Uri> — The URI to publish scripts to in this repository.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Default` switch · **required** — Restores the default setting.
- `-InstallationPolicy` <String> · values: Trusted, Untrusted — Trusted (install without prompting) or Untrusted (prompt before installing).
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).

**Example**

```powershell
Register-PSRepository -Name Internal -SourceLocation \\Server\PSRepo -InstallationPolicy Trusted
```

### Save-Module (PowerShell 5.1 function)

Module PowerShellGet

Downloads a module and its dependencies to a folder without installing.

**Syntax** (one line per parameter set)

```powershell
Save-Module [-Name] <string[]> -Path <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Module [-Name] <string[]> -LiteralPath <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Module [-InputObject] <psobject[]> -LiteralPath <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Module [-InputObject] <psobject[]> -Path <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the module, or several names. Find and Get commands accept wildcards.
- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value and by property name — The object to act on, usually piped from the matching Find- or Get- command.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-Repository` <String[]> · pipeline by property name — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Path` <String> · **required** — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-LiteralPath` <String> · **required** — The path, used exactly as typed: wildcard characters such as [ ] are not interpreted.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing module.

**Example**

```powershell
Save-Module -Name Pester -Path .\Modules
```

### Save-Script (PowerShell 5.1 function)

Module PowerShellGet

Downloads a script to a folder without installing.

**Syntax** (one line per parameter set)

```powershell
Save-Script [-Name] <string[]> -Path <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Script [-Name] <string[]> -LiteralPath <string> [-MinimumVersion <version>] [-MaximumVersion <version>] [-RequiredVersion <version>] [-Repository <string[]>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Script [-InputObject] <psobject[]> -LiteralPath <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Save-Script [-InputObject] <psobject[]> -Path <string> [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the script, or several names. Find and Get commands accept wildcards.
- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value and by property name — The object to act on, usually piped from the matching Find- or Get- command.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-Repository` <String[]> · pipeline by property name — The registered repository (from Get-PSRepository / Get-PSResourceRepository) to use instead of searching them all, for example PSGallery.
- `-Path` <String> · **required** · pipeline by property name — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-LiteralPath` <String> · **required** · pipeline by property name — The path, used exactly as typed: wildcard characters such as [ ] are not interpreted.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing script.

**Example**

```powershell
Save-Script -Name Install-Git -Path .\Scripts
```

### Set-PSRepository (PowerShell 5.1 function)

Module PowerShellGet

Changes a registered repository (location, installation policy).

**Syntax** (one line per parameter set)

```powershell
Set-PSRepository [-Name] <string> [[-SourceLocation] <uri>] [-PublishLocation <uri>] [-ScriptSourceLocation <uri>] [-ScriptPublishLocation <uri>] [-Credential <pscredential>] [-InstallationPolicy <string>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-PackageManagementProvider <string>] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 — The name of the psrepository, or several names. Find and Get commands accept wildcards.
- `-SourceLocation` <Uri> · position 1 — The URI to search for modules in this repository.
- `-PublishLocation` <Uri> — The URI to publish modules to in this repository.
- `-ScriptSourceLocation` <Uri> — The URI to search for scripts in this repository.
- `-ScriptPublishLocation` <Uri> — The URI to publish scripts to in this repository.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-InstallationPolicy` <String> · values: Trusted, Untrusted — Trusted (install without prompting) or Untrusted (prompt before installing).
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-PackageManagementProvider` <String> — The PackageManagement provider to use for the repository (NuGet by default).

**Example**

```powershell
Set-PSRepository -Name PSGallery -InstallationPolicy Trusted
```

### Test-ScriptFileInfo (PowerShell 5.1 function)

Module PowerShellGet

Validates a script's metadata comment block.

**Syntax** (one line per parameter set)

```powershell
Test-ScriptFileInfo [-Path] <string> [<CommonParameters>]

Test-ScriptFileInfo -LiteralPath <string> [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by property name — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-LiteralPath` <String> · **required** · pipeline by property name — The path, used exactly as typed: wildcard characters such as [ ] are not interpreted.

**Example**

```powershell
Test-ScriptFileInfo -Path .\Deploy.ps1
```

### Uninstall-Module (PowerShell 5.1 function)

Module PowerShellGet

Uninstalls a module installed by PowerShellGet.

**Syntax** (one line per parameter set)

```powershell
Uninstall-Module [-Name] <string[]> [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-AllVersions] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Uninstall-Module [-InputObject] <psobject[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the module, or several names. Find and Get commands accept wildcards.
- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value and by property name — The object to act on, usually piped from the matching Find- or Get- command.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-AllVersions` switch — Includes every available version, not only the newest.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing module.

**Example**

```powershell
Uninstall-Module -Name Pester -AllVersions
```

### Uninstall-Script (PowerShell 5.1 function)

Module PowerShellGet

Uninstalls a script installed by PowerShellGet.

**Syntax** (one line per parameter set)

```powershell
Uninstall-Script [-Name] <string[]> [-MinimumVersion <version>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Uninstall-Script [-InputObject] <psobject[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the script, or several names. Find and Get commands accept wildcards.
- `-InputObject` <PSObject[]> · **required** · position 0 · pipeline by value and by property name — The object to act on, usually piped from the matching Find- or Get- command.
- `-MinimumVersion` <Version> · pipeline by property name — The lowest version to accept.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing script.

**Example**

```powershell
Uninstall-Script -Name Install-Git
```

### Unregister-PSRepository (PowerShell 5.1 function)

Module PowerShellGet

Removes a registered repository.

**Syntax** (one line per parameter set)

```powershell
Unregister-PSRepository [-Name] <string[]> [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · **required** · position 0 · pipeline by property name — The name of the psrepository, or several names. Find and Get commands accept wildcards.

**Example**

```powershell
Unregister-PSRepository -Name Internal
```

### Update-Module (PowerShell 5.1 function)

Module PowerShellGet · aliases: upmo

Installs the newest version of modules installed with Install-Module.

**Syntax** (one line per parameter set)

```powershell
Update-Module [[-Name] <string[]>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-Credential <pscredential>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the module, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing module.

**Example**

```powershell
Update-Module -Name Pester
```

### Update-ModuleManifest (PowerShell 5.1 function)

Module PowerShellGet

Changes values in a module manifest (.psd1).

**Syntax** (one line per parameter set)

```powershell
Update-ModuleManifest [-Path] <string> [-NestedModules <Object[]>] [-Guid <guid>] [-Author <string>] [-CompanyName <string>] [-Copyright <string>] [-RootModule <string>] [-ModuleVersion <version>] [-Description <string>] [-ProcessorArchitecture <ProcessorArchitecture>] [-CompatiblePSEditions <string[]>] [-PowerShellVersion <version>] [-ClrVersion <version>] [-DotNetFrameworkVersion <version>] [-PowerShellHostName <string>] [-PowerShellHostVersion <version>] [-RequiredModules <Object[]>] [-TypesToProcess <string[]>] [-FormatsToProcess <string[]>] [-ScriptsToProcess <string[]>] [-RequiredAssemblies <string[]>] [-FileList <string[]>] [-ModuleList <Object[]>] [-FunctionsToExport <string[]>] [-AliasesToExport <string[]>] [-VariablesToExport <string[]>] [-CmdletsToExport <string[]>] [-DscResourcesToExport <string[]>] [-PrivateData <hashtable>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-HelpInfoUri <uri>] [-PassThru] [-DefaultCommandPrefix <string>] [-ExternalModuleDependencies <string[]>] [-PackageManagementProviders <string[]>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by property name — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-NestedModules` <Object[]> — Modules loaded into the module session state.
- `-Guid` <Guid> — The unique identifier (GUID) of the module manifest.
- `-Author` <String> — The author name stored in the manifest.
- `-CompanyName` <String> — The company or vendor stored in the manifest.
- `-Copyright` <String> — The copyright statement stored in the manifest.
- `-RootModule` <String> — The main script or binary module file (.psm1 or .dll) of the module.
- `-ModuleVersion` <Version> — The version number of the module, written to the manifest.
- `-Description` <String> — The description text of the module manifest.
- `-ProcessorArchitecture` <ProcessorArchitecture> — The processor architecture the module needs (None, X86, Amd64, Arm, IA64).
- `-CompatiblePSEditions` <String[]> · values: Desktop, Core — The PowerShell editions the module supports: Desktop (5.1) and/or Core (6+).
- `-PowerShellVersion` <Version> — The minimum PowerShell version the module needs.
- `-ClrVersion` <Version> — The minimum CLR version (Desktop edition only).
- `-DotNetFrameworkVersion` <Version> — The minimum .NET Framework version (Desktop edition only).
- `-PowerShellHostName` <String> — The PowerShell host program the module needs.
- `-PowerShellHostVersion` <Version> — The minimum version of that host.
- `-RequiredModules` <Object[]> — Modules that must be imported first, written to the manifest.
- `-TypesToProcess` <String[]> — Type files (.ps1xml) to load with the module.
- `-FormatsToProcess` <String[]> — Format files (.ps1xml) to load with the module.
- `-ScriptsToProcess` <String[]> — Scripts that run in the caller session when the module is imported.
- `-RequiredAssemblies` <String[]> — Assemblies (.dll) loaded before the module.
- `-FileList` <String[]> — All files packaged with the module (for information).
- `-ModuleList` <Object[]> — All modules packaged with this module (for information).
- `-FunctionsToExport` <String[]> — Functions the module exports, written to the manifest (use explicit names, not *, for fast loading).
- `-AliasesToExport` <String[]> — Aliases the module exports, written to the manifest.
- `-VariablesToExport` <String[]> — Variables the module exports, written to the manifest.
- `-CmdletsToExport` <String[]> — Cmdlets the module exports, written to the manifest.
- `-DscResourcesToExport` <String[]> — DSC resources the module exports, written to the manifest.
- `-PrivateData` <Hashtable> — Private data stored in the manifest PrivateData section.
- `-Tags` <String[]> — Tags (keywords) to store with the module manifest, used when searching a gallery.
- `-ProjectUri` <Uri> — The project web site URI, stored in the manifest.
- `-LicenseUri` <Uri> — The license URI, stored in the manifest.
- `-IconUri` <Uri> — The icon image URI, stored in the manifest.
- `-ReleaseNotes` <String[]> — Release notes for this version, stored in the manifest.
- `-HelpInfoUri` <Uri> — The URI of the updatable help for the module.
- `-PassThru` switch — Returns an object for the module manifest. By default the command writes no output.
- `-DefaultCommandPrefix` <String> — A prefix added to the nouns of exported commands when imported.
- `-ExternalModuleDependencies` <String[]> — Modules that this one needs but that are not in the gallery, listed for information.
- `-PackageManagementProviders` <String[]> — The PackageManagement providers to search.

**Example**

```powershell
Update-ModuleManifest -Path .\MyTools.psd1 -ModuleVersion 1.1.0
```

### Update-Script (PowerShell 5.1 function)

Module PowerShellGet

Installs the newest version of scripts installed with Install-Script.

**Syntax** (one line per parameter set)

```powershell
Update-Script [[-Name] <string[]>] [-RequiredVersion <version>] [-MaximumVersion <version>] [-Proxy <uri>] [-ProxyCredential <pscredential>] [-Credential <pscredential>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by property name — The name of the script, or several names. Find and Get commands accept wildcards.
- `-RequiredVersion` <Version> · pipeline by property name — The exact version to use. Cannot be combined with MinimumVersion or MaximumVersion.
- `-MaximumVersion` <Version> · pipeline by property name — The highest version to accept.
- `-Proxy` <Uri> · pipeline by property name — A proxy server URI for the request, instead of connecting directly.
- `-ProxyCredential` <PSCredential> · pipeline by property name — The account to use for the proxy server given in -Proxy.
- `-Credential` <PSCredential> · pipeline by property name — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing script.

**Example**

```powershell
Update-Script -Name Install-Git
```

### Update-ScriptFileInfo (PowerShell 5.1 function)

Module PowerShellGet

Changes values in a script's metadata comment block.

**Syntax** (one line per parameter set)

```powershell
Update-ScriptFileInfo [-Path] <string> [-Version <version>] [-Author <string>] [-Guid <guid>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-RequiredModules <Object[]>] [-ExternalModuleDependencies <string[]>] [-RequiredScripts <string[]>] [-ExternalScriptDependencies <string[]>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-ScriptFileInfo [-LiteralPath] <string> [-Version <version>] [-Author <string>] [-Guid <guid>] [-Description <string>] [-CompanyName <string>] [-Copyright <string>] [-RequiredModules <Object[]>] [-ExternalModuleDependencies <string[]>] [-RequiredScripts <string[]>] [-ExternalScriptDependencies <string[]>] [-Tags <string[]>] [-ProjectUri <uri>] [-LicenseUri <uri>] [-IconUri <uri>] [-ReleaseNotes <string[]>] [-PassThru] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 · pipeline by property name — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-LiteralPath` <String> · **required** · position 0 · pipeline by property name — The path, used exactly as typed: wildcard characters such as [ ] are not interpreted.
- `-Version` <Version> — The version of the script file info to use or set.
- `-Author` <String> — The author name stored in the manifest.
- `-Guid` <Guid> — The unique identifier (GUID) of the script file info.
- `-Description` <String> — The description text of the script file info.
- `-CompanyName` <String> — The company or vendor stored in the manifest.
- `-Copyright` <String> — The copyright statement stored in the manifest.
- `-RequiredModules` <Object[]> — Modules that must be imported first, written to the manifest.
- `-ExternalModuleDependencies` <String[]> — Modules that this one needs but that are not in the gallery, listed for information.
- `-RequiredScripts` <String[]> — Scripts this script needs, written to its metadata.
- `-ExternalScriptDependencies` <String[]> — Scripts that this one needs but that are not in the gallery, listed for information.
- `-Tags` <String[]> — Tags (keywords) to store with the script file info, used when searching a gallery.
- `-ProjectUri` <Uri> — The project web site URI, stored in the manifest.
- `-LicenseUri` <Uri> — The license URI, stored in the manifest.
- `-IconUri` <Uri> — The icon image URI, stored in the manifest.
- `-ReleaseNotes` <String[]> — Release notes for this version, stored in the manifest.
- `-PassThru` switch — Returns an object for the script file info. By default the command writes no output.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing script file info.

**Example**

```powershell
Update-ScriptFileInfo -Path .\Deploy.ps1 -Version 1.1
```

## Commands: PSDesiredStateConfiguration

### Configuration (PowerShell 5.1 function)

Module PSDesiredStateConfiguration

DSC keyword (exposed as a function) that defines a Desired State Configuration; running it compiles .mof files.

**Syntax** (one line per parameter set)

```powershell
Configuration [[-ResourceModuleTuplesToImport] <List[Tuple[string[],ModuleSpecification[],version]]>] [[-OutputPath] <Object>] [[-Name] <Object>] [[-Body] <scriptblock>] [[-ArgsToBody] <hashtable>] [[-ConfigurationData] <hashtable>] [[-InstanceName] <string>] [<CommonParameters>]
```

**Arguments**

- `-ResourceModuleTuplesToImport` <List`1> · position 0 — Internal: the module and resource pairs to import into the configuration.
- `-OutputPath` <Object> · position 1 — The folder the output (for example MOF files) is written to.
- `-Name` <Object> · position 2 — The name of the configuration, or several names. Find and Get commands accept wildcards.
- `-Body` <ScriptBlock> · position 3 — The body of the request.
- `-ArgsToBody` <Hashtable> · position 4 — Arguments that are added to the request body.
- `-ConfigurationData` <Hashtable> · position 5 — Configuration data (a hashtable or .psd1 path) passed to the configuration.
- `-InstanceName` <String> · position 6 — The name of the configuration instance.

**Example**

```powershell
Configuration Web { Node localhost { WindowsFeature IIS { Name = 'Web-Server'; Ensure = 'Present' } } }; Web -OutputPath .\Web
```

### Disable-DscDebug (PowerShell 5.1 function)

Module PSDesiredStateConfiguration

Stops debugging of DSC resources.

**Syntax** (one line per parameter set)

```powershell
Disable-DscDebug [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Disable-DscDebug
```

### Enable-DscDebug (PowerShell 5.1 function)

Module PSDesiredStateConfiguration

Starts debugging of DSC resources (breaks into resources as they run).

**Syntax** (one line per parameter set)

```powershell
Enable-DscDebug [-BreakAll] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-BreakAll` switch — Breaks into the debugger at the first statement of every script.
- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Enable-DscDebug -BreakAll
```

### Get-DscConfiguration (PowerShell 5.1 function)

Module PSDesiredStateConfiguration · aliases: gcfg

Gets the current state of the configuration applied to the node.

**Syntax** (one line per parameter set)

```powershell
Get-DscConfiguration [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Get-DscConfiguration
```

### Get-DscConfigurationStatus (PowerShell 5.1 function)

Module PSDesiredStateConfiguration · aliases: gcfgs

Shows the status of recent configuration runs (success, failure, resources).

**Syntax** (one line per parameter set)

```powershell
Get-DscConfigurationStatus [-All] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]
```

**Arguments**

- `-All` switch — Includes every item.
- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Get-DscConfigurationStatus -All
```

### Get-DscLocalConfigurationManager (PowerShell 5.1 function)

Module PSDesiredStateConfiguration · aliases: glcm

Shows the Local Configuration Manager (LCM) settings of the node.

**Syntax** (one line per parameter set)

```powershell
Get-DscLocalConfigurationManager [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Get-DscLocalConfigurationManager
```

### Get-DscResource (PowerShell 5.1 function)

Module PSDesiredStateConfiguration

Lists DSC resources installed on the computer and their properties.

**Syntax** (one line per parameter set)

```powershell
Get-DscResource [[-Name] <string[]>] [[-Module] <Object>] [-Syntax] [<CommonParameters>]
```

**Arguments**

- `-Name` <String[]> · position 0 · pipeline by value and by property name — The name of the dsc resource, or several names. Find and Get commands accept wildcards.
- `-Module` <Object> · position 1 · pipeline by value and by property name — The module (name or ModuleSpecification) to use.
- `-Syntax` switch — Shows the syntax of the DSC resource instead of its details.

**Example**

```powershell
Get-DscResource -Name File -Syntax
```

### Invoke-DscResource (PowerShell 5.1 cmdlet)

Module PSDesiredStateConfiguration

Runs one method (Get, Set or Test) of a DSC resource directly.

**Syntax** (one line per parameter set)

```powershell
Invoke-DscResource [-Name] <string> [-Method] <string> -ModuleName <ModuleSpecification> -Property <hashtable> [<CommonParameters>]
```

**Arguments**

- `-Name` <String> · **required** · position 0 · pipeline by property name — The name of the dsc resource, or several names. Find and Get commands accept wildcards.
- `-Method` <String> · **required** · position 1 · pipeline by property name · values: Get, Set, Test — The DSC method to run: Get, Set or Test.
- `-ModuleName` <ModuleSpecification> · **required** · pipeline by property name — The module the dsc resource belongs to, or the modules to search.
- `-Property` <Hashtable> · **required** · pipeline by property name — The DSC resource properties to set or test, as a hashtable.

**Example**

```powershell
Invoke-DscResource -Name File -Method Test -ModuleName PSDesiredStateConfiguration -Property @{ DestinationPath = 'C:\x.txt'; Contents = 'hi' }
```

### New-DscChecksum (PowerShell 5.1 function)

Module PSDesiredStateConfiguration

Creates checksum files for DSC configuration documents and resources (for pull servers).

**Syntax** (one line per parameter set)

```powershell
New-DscChecksum [-Path] <string[]> [[-OutPath] <string>] [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String[]> · **required** · position 0 · alias -ConfigurationPath — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-OutPath` <String> · position 1 — The output path.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing dsc checksum.

**Example**

```powershell
New-DscChecksum -Path .\Web -Force
```

### Publish-DscConfiguration (PowerShell 5.1 cmdlet)

Module PSDesiredStateConfiguration · aliases: pbcfg

Sends a configuration to nodes without applying it yet.

**Syntax** (one line per parameter set)

```powershell
Publish-DscConfiguration [-Path] <string> [[-ComputerName] <string[]>] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Publish-DscConfiguration [-Path] <string> -CimSession <CimSession[]> [-Force] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing dsc configuration.
- `-ComputerName` <String[]> · position 1 · pipeline by value · alias -CN, -ServerName — The computer(s) to run against. Defaults to the local computer.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command on remote computers through these CIM sessions (New-CimSession).

**Example**

```powershell
Publish-DscConfiguration -Path .\Web
```

### Remove-DscConfigurationDocument (PowerShell 5.1 function)

Module PSDesiredStateConfiguration

Removes a configuration document (Current, Pending or Previous) from the node.

**Syntax** (one line per parameter set)

```powershell
Remove-DscConfigurationDocument -Stage <Stage> [-Force] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Stage` <Stage> · **required** — The stage of the configuration document to use: Current, Pending or Previous.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing dsc configuration document.
- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Remove-DscConfigurationDocument -Stage Pending
```

### Restore-DscConfiguration (PowerShell 5.1 function)

Module PSDesiredStateConfiguration · aliases: rtcfg

Rolls the node back to its previous configuration.

**Syntax** (one line per parameter set)

```powershell
Restore-DscConfiguration [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Restore-DscConfiguration
```

### Set-DscLocalConfigurationManager (PowerShell 5.1 cmdlet)

Module PSDesiredStateConfiguration · aliases: slcm

Applies LCM settings (meta-configuration) to nodes.

**Syntax** (one line per parameter set)

```powershell
Set-DscLocalConfigurationManager [-Path] <string> [[-ComputerName] <string[]>] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Set-DscLocalConfigurationManager [-Path] <string> -CimSession <CimSession[]> [-Force] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Path` <String> · **required** · position 0 — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing dsc local configuration manager.
- `-ComputerName` <String[]> · position 1 · pipeline by value · alias -CN, -ServerName — The computer(s) to run against. Defaults to the local computer.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command on remote computers through these CIM sessions (New-CimSession).

**Example**

```powershell
Set-DscLocalConfigurationManager -Path .\LcmConfig
```

### Start-DscConfiguration (PowerShell 5.1 cmdlet)

Module PSDesiredStateConfiguration · aliases: sacfg

Applies configurations (.mof files) to nodes.

**Syntax** (one line per parameter set)

```powershell
Start-DscConfiguration [[-Path] <string>] [[-ComputerName] <string[]>] [-Wait] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-DscConfiguration [[-Path] <string>] -CimSession <CimSession[]> [-Wait] [-Force] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-DscConfiguration [[-ComputerName] <string[]>] -UseExisting [-Wait] [-Force] [-Credential <pscredential>] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]

Start-DscConfiguration -CimSession <CimSession[]> -UseExisting [-Wait] [-Force] [-ThrottleLimit <int>] [-JobName <string>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Wait` switch — Waits until the operation completes before returning.
- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing dsc configuration.
- `-Path` <String> · position 0 — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-ComputerName` <String[]> · position 1 · pipeline by value · alias -CN, -ServerName — The computer(s) to run against. Defaults to the local computer.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-UseExisting` switch · **required** — Uses an existing item instead of creating a new one.
- `-JobName` <String> — The name of the background job.

**Example**

```powershell
Start-DscConfiguration -Path .\Web -Wait -Verbose
```

### Stop-DscConfiguration (PowerShell 5.1 function)

Module PSDesiredStateConfiguration

Stops a configuration that is currently running.

**Syntax** (one line per parameter set)

```powershell
Stop-DscConfiguration [-Force] [-CimSession <CimSession[]>] [-ThrottleLimit <int>] [-AsJob] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Force` switch — Runs without asking for confirmation and overrides restrictions, for example reinstalling or overwriting an existing dsc configuration.
- `-CimSession` <CimSession[]> · alias -Session — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-AsJob` switch — Runs the command as a background job and returns the job object.

**Example**

```powershell
Stop-DscConfiguration -Force
```

### Test-DscConfiguration (PowerShell 5.1 cmdlet)

Module PSDesiredStateConfiguration · aliases: tcfg

Tests whether the node matches its configuration (True/False or detailed).

**Syntax** (one line per parameter set)

```powershell
Test-DscConfiguration [[-ComputerName] <string[]>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-AsJob] [-Detailed] [<CommonParameters>]

Test-DscConfiguration [-Path] <string> [[-ComputerName] <string[]>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration [[-ComputerName] <string[]>] -ReferenceConfiguration <string> [-Credential <pscredential>] [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration [-Path] <string> -CimSession <CimSession[]> [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration -CimSession <CimSession[]> -ReferenceConfiguration <string> [-ThrottleLimit <int>] [-AsJob] [<CommonParameters>]

Test-DscConfiguration -CimSession <CimSession[]> [-ThrottleLimit <int>] [-AsJob] [-Detailed] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · position 1 · pipeline by value · alias -CN, -ServerName — The computer(s) to run against. Defaults to the local computer.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command on remote computers through these CIM sessions (New-CimSession).
- `-AsJob` switch — Runs the command as a background job and returns the job object.
- `-Path` <String> · **required** · position 0 — The path of the file or folder to use. Wildcards are not expanded unless the command says so.
- `-ReferenceConfiguration` <String> · **required** — The MOF configuration document to compare the node against.
- `-Detailed` switch — Returns detailed results.

**Example**

```powershell
Test-DscConfiguration -Detailed
```

### Update-DscConfiguration (PowerShell 5.1 cmdlet)

Module PSDesiredStateConfiguration · aliases: upcfg

Makes the node check its pull server for configuration changes now.

**Syntax** (one line per parameter set)

```powershell
Update-DscConfiguration [[-ComputerName] <string[]>] [-Wait] [-JobName <string>] [-Credential <pscredential>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]

Update-DscConfiguration -CimSession <CimSession[]> [-Wait] [-JobName <string>] [-ThrottleLimit <int>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Wait` switch — Waits until the operation completes before returning.
- `-JobName` <String> — The name of the background job.
- `-ComputerName` <String[]> · position 1 · pipeline by value · alias -CN, -ServerName — The computer(s) to run against. Defaults to the local computer.
- `-Credential` <PSCredential> — The account to use, as a PSCredential (from Get-Credential). Defaults to the current user.
- `-ThrottleLimit` <Int32> — The maximum number of concurrent operations (0 = the default).
- `-CimSession` <CimSession[]> · **required** · pipeline by value — Runs the command on remote computers through these CIM sessions (New-CimSession).

**Example**

```powershell
Update-DscConfiguration -Wait
```

## Commands: PSDiagnostics

### Disable-PSTrace (PowerShell 5.1 function)

Module PSDiagnostics

Turns off the Microsoft-Windows-PowerShell event provider's analytic and debug logs.

**Syntax** (one line per parameter set)

```powershell
Disable-PSTrace [-AnalyticOnly]
```

**Arguments**

- `-AnalyticOnly` switch — When this parameter is used, the cmdlet disables the Analytic event log of the Microsoft-Windows-PowerShell provider.

**Output:** None

**Example**

```powershell
Disable-PSTrace
```

### Disable-PSWSManCombinedTrace (PowerShell 5.1 function)

Module PSDiagnostics

Stops the combined PowerShell and WSMan trace session.

**Syntax** (one line per parameter set)

```powershell
Disable-PSWSManCombinedTrace
```

**Output:** None

**Example**

```powershell
Disable-PSWSManCombinedTrace
```

### Disable-WSManTrace (PowerShell 5.1 function)

Module PSDiagnostics

Stops the WSMan trace session.

**Syntax** (one line per parameter set)

```powershell
Disable-WSManTrace
```

**Output:** None

**Example**

```powershell
Disable-WSManTrace
```

### Enable-PSTrace (PowerShell 5.1 function)

Module PSDiagnostics

Turns on the Microsoft-Windows-PowerShell analytic and debug event logs.

**Syntax** (one line per parameter set)

```powershell
Enable-PSTrace [-Force] [-AnalyticOnly]
```

**Arguments**

- `-Force` switch — Used to force the change without prompting.
- `-AnalyticOnly` switch — When this parameter is used, the cmdlet enables the Analytic event log of the Microsoft-Windows-PowerShell provider.

**Output:** None

**Example**

```powershell
Enable-PSTrace -Force
```

### Enable-PSWSManCombinedTrace (PowerShell 5.1 function)

Module PSDiagnostics

Starts a trace session with both PowerShell and WSMan providers.

**Syntax** (one line per parameter set)

```powershell
Enable-PSWSManCombinedTrace [-DoNotOverwriteExistingTrace]
```

**Arguments**

- `-DoNotOverwriteExistingTrace` switch — By default, the events are written to $PSHOME\Traces\PSTrace.etl.

**Output:** None

**Example**

```powershell
Enable-PSWSManCombinedTrace
```

### Enable-WSManTrace (PowerShell 5.1 function)

Module PSDiagnostics

Starts a trace session for the WSMan providers.

**Syntax** (one line per parameter set)

```powershell
Enable-WSManTrace
```

**Output:** None

**Example**

```powershell
Enable-WSManTrace
```

### Get-LogProperties (PowerShell 5.1 function)

Module PSDiagnostics

Gets the properties of a Windows event log (enabled, size, retention).

**Syntax** (one line per parameter set)

```powershell
Get-LogProperties [-Name] <Object> [<CommonParameters>]
```

**Arguments**

- `-Name` <Object> · **required** · position 0 · pipeline by value — The name of the event provider.

**Output:** LogDetails

**Example**

```powershell
Get-LogProperties -Name 'Microsoft-Windows-PowerShell/Operational'
```

### Set-LogProperties (PowerShell 5.1 function)

Module PSDiagnostics

Changes the properties of a Windows event log.

**Syntax** (one line per parameter set)

```powershell
Set-LogProperties [-LogDetails] <LogDetails> [-Force] [<CommonParameters>]
```

**Arguments**

- `-LogDetails` <LogDetails> · **required** · position 0 · pipeline by value — The updated configuration settings to be assigned to the event log.
- `-Force` switch — Used to force the change without prompting.

**Output:** None

**Example**

```powershell
$p = Get-LogProperties 'Windows PowerShell'; $p.MaxLogSize = 20MB; Set-LogProperties -LogDetails $p
```

### Start-Trace (PowerShell 5.1 function)

Module PSDiagnostics

Starts an Event Tracing for Windows (ETW) session.

**Syntax** (one line per parameter set)

```powershell
Start-Trace [-SessionName] <string> [[-OutputFilePath] <string>] [[-ProviderFilePath] <string>] [-ETS] [-Format <Object>] [-MinBuffers <int>] [-MaxBuffers <int>] [-BufferSizeInKB <int>] [-MaxLogFileSizeInMB <int>] [<CommonParameters>]
```

**Arguments**

- `-SessionName` <String> · **required** · position 0 — The name of the Event Trace session. To stop a trace session you must know the session name.
- `-OutputFilePath` <String> · position 1 · default $Env:SystemDrive\PerfLogs\Admin — Path of the output log file or the DSN and log set name in a SQL database.
- `-ProviderFilePath` <String> · position 2 — File listing multiple Event Trace providers to enable.
- `-ETS` switch — Send commands to Event Trace Sessions directly without saving or scheduling.
- `-Format` <Object> · values: bin, bincirc, csv, tsv, sql · default bin — Specifies the log format for the data collector. For SQL database format, you must use the OutputFilePath option in the command line with the dsn!log value.
- `-MinBuffers` <Int32> · default 0 — Sets the minimum number of Event Trace Session buffers.
- `-MaxBuffers` <Int32> · default 256 — Sets the maximum number of Event Trace Session buffers.
- `-BufferSizeInKB` <Int32> · default 0 — Event Trace Session buffer size in kilobytes (KB).
- `-MaxLogFileSizeInMB` <Int32> · default 0 (no limit) — Sets the maximum log file size in megabytes (MB) or number of records for SQL logs.

**Output:** None

**Example**

```powershell
Start-Trace -SessionName MyTrace -OutputFilePath .\trace.etl -ProviderFilePath .\providers.txt
```

### Stop-Trace (PowerShell 5.1 function)

Module PSDiagnostics

Stops an ETW session.

**Syntax** (one line per parameter set)

```powershell
Stop-Trace [-SessionName] <Object> [-ETS] [<CommonParameters>]
```

**Arguments**

- `-SessionName` <Object> · **required** · position 0 — The name of the Event Trace session to be stopped.
- `-ETS` switch — Send commands to Event Trace Sessions directly without saving or scheduling.

**Output:** None

**Example**

```powershell
Stop-Trace -SessionName MyTrace
```

## Commands: PSReadLine

### Get-PSReadLineKeyHandler (PowerShell 5.1 cmdlet)

Module PSReadLine

Lists the key bindings of PSReadLine (the command-line editor).

**Syntax** (one line per parameter set)

```powershell
Get-PSReadLineKeyHandler [-Bound] [-Unbound] [<CommonParameters>]
```

**Arguments**

- `-Bound` switch — Indicates that this cmdlet returns functions that are bound.
- `-Unbound` switch — Indicates that this cmdlet returns functions that are unbound.

**Output:** KeyHandler

**Example**

```powershell
Get-PSReadLineKeyHandler -Bound
```

### Get-PSReadLineOption (PowerShell 5.1 cmdlet)

Module PSReadLine

Shows the current PSReadLine options (edit mode, colors, history settings).

**Syntax** (one line per parameter set)

```powershell
Get-PSReadLineOption [<CommonParameters>]
```

**Output:** PSConsoleReadLineOptions

**Example**

```powershell
Get-PSReadLineOption
```

### PSConsoleHostReadLine (PowerShell 5.1 function)

Module PSReadLine

The function the console calls to read each command line through PSReadLine; not meant to be called directly.

**Syntax** (one line per parameter set)

```powershell
PSConsoleHostReadLine
```

**Output:** None

**Example**

```powershell
PSConsoleHostReadLine
```

### Remove-PSReadLineKeyHandler (PowerShell 5.1 cmdlet)

Module PSReadLine

Removes a PSReadLine key binding.

**Syntax** (one line per parameter set)

```powershell
Remove-PSReadLineKeyHandler [-Chord] <string[]> [-ViMode <ViMode>] [<CommonParameters>]
```

**Arguments**

- `-Chord` <String[]> · **required** · position 0 · alias -Key — Specifies an array of keys or sequences of keys to be removed. A single binding is specified by using a single string.
- `-ViMode` <ViMode> — Specify which vi mode the binding applies to. Possible values are: Insert, Command.

**Output:** None

**Example**

```powershell
Remove-PSReadLineKeyHandler -Chord Ctrl+d
```

### Set-PSReadLineKeyHandler (PowerShell 5.1 cmdlet)

Module PSReadLine

Binds a key or chord to a PSReadLine function or your own script block.

**Syntax** (one line per parameter set)

```powershell
Set-PSReadLineKeyHandler [-Chord] <string[]> [-ScriptBlock] <scriptblock> [-BriefDescription <string>] [-Description <string>] [-ViMode <ViMode>] [<CommonParameters>]

Set-PSReadLineKeyHandler [-Chord] <string[]> [-Function] <string> [-ViMode <ViMode>] [<CommonParameters>]
```

**Arguments**

- `-ScriptBlock` <ScriptBlock> · **required** · position 1 — Specifies a scriptblock value to run when the chord is entered. PSReadLine passes one or two parameters to this scriptblock.
- `-BriefDescription` <String> — A brief description of the key binding. This description is displayed by the Get-PSReadLineKeyHandler cmdlet.
- `-Description` <String> · alias -LongDescription — Specifies a more detailed description of the key binding that's visible in the output of the Get-PSReadLineKeyHandler cmdlet.
- `-Chord` <String[]> · **required** · position 0 · alias -Key — The key or sequence of keys to be bound to a function or scriptblock. Use a single string to specify a single binding.
- `-ViMode` <ViMode> — Specify which vi mode the binding applies to.
- `-Function` <String> · **required** · position 1 · values: Abort, AcceptAndGetNext, AcceptLine, AddLine, BackwardChar, BackwardDeleteChar, BackwardDeleteLine, BackwardDeleteWord, BackwardKillLine, BackwardKillWord, BackwardWord, BeginningOfHistory, BeginningOfLine, CancelLine … — Specifies the name of an existing key handler provided by PSReadLine. This parameter lets you change existing key bindings, or bind a handler that's currently unbound.

**Output:** None

**Example**

```powershell
Set-PSReadLineKeyHandler -Key Tab -Function MenuComplete
```

### Set-PSReadLineOption (PowerShell 5.1 cmdlet)

Module PSReadLine

Changes PSReadLine behavior: edit mode, colors, history, prediction.

**Syntax** (one line per parameter set)

```powershell
Set-PSReadLineOption [-EditMode <EditMode>] [-ContinuationPrompt <string>] [-HistoryNoDuplicates] [-AddToHistoryHandler <Func[string,Object]>] [-CommandValidationHandler <Action[CommandAst]>] [-HistorySearchCursorMovesToEnd] [-MaximumHistoryCount <int>] [-MaximumKillRingCount <int>] [-ShowToolTips] [-ExtraPromptLineCount <int>] [-DingTone <int>] [-DingDuration <int>] [-BellStyle <BellStyle>] [-CompletionQueryItems <int>] [-WordDelimiters <string>] [-HistorySearchCaseSensitive] [-HistorySaveStyle <HistorySaveStyle>] [-HistorySavePath <string>] [-AnsiEscapeTimeout <int>] [-PromptText <string[]>] [-ViModeIndicator <ViModeStyle>] [-ViModeChangeHandler <scriptblock>] [-Colors <hashtable>] [<CommonParameters>]
```

**Arguments**

- `-EditMode` <EditMode> · default Windows — Specifies the command line editing mode. Using this parameter resets any key bindings set by Set-PSReadLineKeyHandler.
- `-ContinuationPrompt` <String> · default >> — Specifies the string displayed at the beginning of the subsequent lines when multi-line input is entered.
- `-HistoryNoDuplicates` switch — This option controls the recall behavior. Duplicate commands are still added to the history file.
- `-AddToHistoryHandler` <Func`2> — Specifies a ScriptBlock that controls how commands get added to PSReadLine history.
- `-CommandValidationHandler` <Action`1> — Specifies a ScriptBlock that is called from ValidateAndAcceptLine. If an exception is thrown, validation fails and the error is reported.
- `-HistorySearchCursorMovesToEnd` switch — Indicates that the cursor moves to the end of commands that you load from history by using a search.
- `-MaximumHistoryCount` <Int32> — Specifies the maximum number of commands to save in PSReadLine history.
- `-MaximumKillRingCount` <Int32> · default 10 — Specifies the maximum number of items stored in the kill ring.
- `-ShowToolTips` switch — When displaying possible completions, tooltips are shown in the list of completions.
- `-ExtraPromptLineCount` <Int32> · default 0 — Specifies the number of extra lines.
- `-DingTone` <Int32> · default 1221 — Specifies the tone in Hertz (Hz) of the beep when BellStyle is set to Audible
- `-DingDuration` <Int32> · default 50ms — Specifies the duration of the beep when BellStyle is set to Audible.
- `-BellStyle` <BellStyle> · default Audible — Specifies how PSReadLine responds to various error and ambiguous conditions.
- `-CompletionQueryItems` <Int32> · default 100 — Specifies the maximum number of completion items that are shown without prompting.
- `-WordDelimiters` <String> · default ;:,.[]{}()/\|^&*-=+'"--- — Specifies the characters that delimit words for functions like ForwardWord or KillWord.
- `-HistorySearchCaseSensitive` switch — Specifies that history searching is case-sensitive in functions like ReverseSearchHistory or HistorySearchBackward.
- `-HistorySaveStyle` <HistorySaveStyle> · default SaveIncrementally — Specifies how PSReadLine saves history.
- `-HistorySavePath` <String> · default A file named $($Host.Name)_history.txt in $Env:APPDATA\Microsoft\Windows\PowerShell\PSReadLine on Windows and $Env:XDG_DATA_HOME/powershell/PSReadLine or $HOME/.local/share/powershell/PSReadLine on non-Windows platforms — Specifies the path to the file where history is saved. Computers running Windows or non-Windows platforms store the file in different locations.
- `-AnsiEscapeTimeout` <Int32> · default 100 — This option is specific to Windows when input is redirected, for example, when running under tmux or screen.
- `-PromptText` <String[]> · default > — This parameter sets the value of the PromptText property. The default value is "> ".
- `-ViModeIndicator` <ViModeStyle> — This option sets the visual indicator for the current Vi mode. Either insert mode or command mode.
- `-ViModeChangeHandler` <ScriptBlock> — When the ViModeIndicator is set to Script, the scriptblock provided will be invoked every time the mode changes.
- `-Colors` <Hashtable> — The Colors parameter specifies various colors used by PSReadLine.

**Output:** None

**Example**

```powershell
Set-PSReadLineOption -EditMode Emacs -HistoryNoDuplicates
```

## Commands: PSScheduledJob

### Add-JobTrigger (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Adds triggers (when to run) to scheduled jobs.

**Syntax** (one line per parameter set)

```powershell
Add-JobTrigger [-InputObject] <ScheduledJobDefinition[]> [-Trigger] <ScheduledJobTrigger[]> [<CommonParameters>]

Add-JobTrigger [-Id] <int[]> [-Trigger] <ScheduledJobTrigger[]> [<CommonParameters>]

Add-JobTrigger [-Name] <string[]> [-Trigger] <ScheduledJobTrigger[]> [<CommonParameters>]
```

**Arguments**

- `-Trigger` <ScheduledJobTrigger[]> · **required** · position 1 · pipeline by value — Specifies the job triggers to add. Enter a hash table that specifies job triggers or a variable that contains ScheduledJobTrigger objects, or type a command or expression that gets ScheduledJobTrigger objects, such as a Get-JobTrigger command.
- `-Id` <Int32[]> · **required** · position 0 — Specifies the identification numbers of the scheduled jobs. Add-JobTrigger adds the job trigger to the specified scheduled jobs.
- `-Name` <String[]> · **required** · position 0 — Specifies the names of the scheduled jobs. Add-JobTrigger adds the job triggers to the specified scheduled jobs.
- `-InputObject` <ScheduledJobDefinition[]> · **required** · position 0 · pipeline by value — Specifies the scheduled jobs. Enter a variable that contains ScheduledJob objects or type a command or expression that gets ScheduledJob objects, such as a Get-ScheduledJob command.

**Output:** None

**Example**

```powershell
Add-JobTrigger -Name Backup -Trigger (New-JobTrigger -Daily -At 3am)
```

### Disable-JobTrigger (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Disables triggers of scheduled jobs without deleting them.

**Syntax** (one line per parameter set)

```powershell
Disable-JobTrigger [-InputObject] <ScheduledJobTrigger[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ScheduledJobTrigger[]> · **required** · position 0 · pipeline by value — Specifies the job trigger to be disabled. Enter a variable that contains ScheduledJobTrigger objects or type a command or expression that gets ScheduledJobTrigger objects, such as a Get-JobTrigger command.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None

**Example**

```powershell
Get-JobTrigger -Name Backup | Disable-JobTrigger
```

### Disable-ScheduledJob (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Disables a scheduled job so its triggers no longer start it.

**Syntax** (one line per parameter set)

```powershell
Disable-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-ScheduledJob [-Id] <int> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Disable-ScheduledJob [-Name] <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ScheduledJobDefinition> · **required** · position 0 · pipeline by value — Specifies the scheduled job to be disabled. Enter a variable that contains ScheduledJobDefinition objects or type a command or expression that gets ScheduledJobDefinition objects, such as a Get-ScheduledJob command.
- `-Id` <Int32> · **required** · position 0 — Disables the scheduled job with the specified identification number (ID).
- `-Name` <String> · **required** · position 0 — Disables the scheduled jobs with the specified names. Enter the name of a scheduled job.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None; ScheduledJobDefinition

**Example**

```powershell
Disable-ScheduledJob -Name Backup
```

### Enable-JobTrigger (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Re-enables triggers of scheduled jobs.

**Syntax** (one line per parameter set)

```powershell
Enable-JobTrigger [-InputObject] <ScheduledJobTrigger[]> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ScheduledJobTrigger[]> · **required** · position 0 · pipeline by value — Specifies the job trigger to enable. Enter a variable that contains ScheduledJobTrigger objects or type a command or expression that gets ScheduledJobTrigger objects, such as a Get-JobTrigger command.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None

**Example**

```powershell
Get-JobTrigger -Name Backup | Enable-JobTrigger
```

### Enable-ScheduledJob (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Re-enables a disabled scheduled job.

**Syntax** (one line per parameter set)

```powershell
Enable-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-ScheduledJob [-Id] <int> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]

Enable-ScheduledJob [-Name] <string> [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ScheduledJobDefinition> · **required** · position 0 · pipeline by value — Specifies the scheduled job to enable. Enter a variable that contains ScheduledJobDefinition objects or type a command or expression that gets ScheduledJobDefinition objects, such as a Get-ScheduledJob command.
- `-Id` <Int32> · **required** · position 0 — Enables the scheduled job with the specified identification number (ID).
- `-Name` <String> · **required** · position 0 — Enables the scheduled jobs with the specified names. Enter the name of a scheduled job.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.

**Output:** None; ScheduledJobDefinition

**Example**

```powershell
Enable-ScheduledJob -Name Backup
```

### Get-JobTrigger (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Gets the triggers of scheduled jobs.

**Syntax** (one line per parameter set)

```powershell
Get-JobTrigger [-InputObject] <ScheduledJobDefinition> [[-TriggerId] <int[]>] [<CommonParameters>]

Get-JobTrigger [-Id] <int> [[-TriggerId] <int[]>] [<CommonParameters>]

Get-JobTrigger [-Name] <string> [[-TriggerId] <int[]>] [<CommonParameters>]
```

**Arguments**

- `-TriggerId` <Int32[]> · position 1 — Gets the specified job triggers. Enter the trigger IDs of one or more job triggers of a scheduled job.
- `-InputObject` <ScheduledJobDefinition> · **required** · position 0 · pipeline by value — Specifies a scheduled job. Enter a variable that contains ScheduledJob objects or type a command or expression that gets ScheduledJob objects, such as a Get-ScheduledJob command.
- `-Id` <Int32> · **required** · position 0 — Specifies the identification number of a scheduled job. Get-JobTrigger gets the job trigger of the specified scheduled job.
- `-Name` <String> · **required** · position 0 — Specifies the name of a scheduled job. Get-JobTrigger gets the job trigger of the specified scheduled job.

**Output:** ScheduledJobTrigger

**Example**

```powershell
Get-JobTrigger -Name Backup
```

### Get-ScheduledJob (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Lists scheduled jobs on the local computer.

**Syntax** (one line per parameter set)

```powershell
Get-ScheduledJob [[-Id] <int[]>] [<CommonParameters>]

Get-ScheduledJob [-Name] <string[]> [<CommonParameters>]
```

**Arguments**

- `-Id` <Int32[]> · position 0 — Gets only the scheduled jobs with the specified identification number (ID).
- `-Name` <String[]> · **required** · position 0 — Gets only the scheduled jobs with the specified names. Enter one or more names of scheduled jobs on the computer.

**Output:** ScheduledJobDefinition

**Example**

```powershell
Get-ScheduledJob
```

### Get-ScheduledJobOption (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Gets the options (conditions, idle settings) of scheduled jobs.

**Syntax** (one line per parameter set)

```powershell
Get-ScheduledJobOption [-InputObject] <ScheduledJobDefinition> [<CommonParameters>]

Get-ScheduledJobOption [-Id] <int> [<CommonParameters>]

Get-ScheduledJobOption [-Name] <string> [<CommonParameters>]
```

**Arguments**

- `-Id` <Int32> · **required** · position 0 — Specifies the identification number of a scheduled job. Get-ScheduledJobOption gets the job options of the specified scheduled job.
- `-Name` <String> · **required** · position 0 · pipeline by property name — Specifies the names of scheduled jobs. Get-ScheduledJobOption gets the job options of the specified scheduled job.
- `-InputObject` <ScheduledJobDefinition> · **required** · position 0 · pipeline by value — Specifies a scheduled job. Enter a variable that contains a ScheduledJob object or type a command or expression that gets a ScheduledJob object, such as a Get-ScheduledJob command.

**Output:** ScheduledJobOptions

**Example**

```powershell
Get-ScheduledJobOption -Name Backup
```

### New-JobTrigger (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Creates a trigger (once, daily, weekly, at startup, at logon) for a scheduled job.

**Syntax** (one line per parameter set)

```powershell
New-JobTrigger -Once -At <datetime> [-RandomDelay <timespan>] [-RepetitionInterval <timespan>] [-RepetitionDuration <timespan>] [-RepeatIndefinitely] [<CommonParameters>]

New-JobTrigger -Daily -At <datetime> [-DaysInterval <int>] [-RandomDelay <timespan>] [<CommonParameters>]

New-JobTrigger -Weekly -At <datetime> -DaysOfWeek <DayOfWeek[]> [-WeeksInterval <int>] [-RandomDelay <timespan>] [<CommonParameters>]

New-JobTrigger -AtStartup [-RandomDelay <timespan>] [<CommonParameters>]

New-JobTrigger -AtLogOn [-RandomDelay <timespan>] [-User <string>] [<CommonParameters>]
```

**Arguments**

- `-DaysInterval` <Int32> — Specifies the number of days between occurrences on a daily schedule. For example, a value of 3 starts the scheduled job on days 1, 4, 7 and so on.
- `-WeeksInterval` <Int32> — Specifies the number of weeks between occurrences on a weekly job schedule.
- `-RandomDelay` <TimeSpan> — Enables a random delay that begins at the scheduled start time, and sets the maximum delay value.
- `-At` <DateTime> · **required** — Starts the job at the specified date and time. Enter a DateTime object, such as one that the Get-Date cmdlet returns, or a string that can be converted to a date and time, such as April 19, 2012 15:00, 12/31, or 3am.
- `-User` <String> — Specifies the users who trigger an AtLogon start of a scheduled job. Enter the name of a user in <UserName> or <Domain\Username> format or enter an asterisk (*) to represent all users.
- `-DaysOfWeek` <DayOfWeek[]> · **required** — Specifies the days of the week on which a weekly scheduled job runs. Enter day names, such as Monday or integers 0-6, where 0 represents Sunday.
- `-AtStartup` switch · **required** · position 0 — Starts the scheduled job when Windows starts.
- `-AtLogOn` switch · **required** · position 0 — Starts the scheduled job when the specified users log on to the computer.
- `-Once` switch · **required** · position 0 — Specifies a non-recurring (one time) or custom repeating schedule. To create a repeating schedule, use the Once parameter with the RepetitionDuration and RepetitionInterval parameters.
- `-RepetitionInterval` <TimeSpan> — Repeats the job at the specified time interval. For example, if the value of this parameter is 2 hours, the job is triggered every two hours.
- `-RepetitionDuration` <TimeSpan> — Repeats the job until the specified time expires. The repetition frequency is determined by the value of the RepetitionInterval parameter.
- `-RepeatIndefinitely` switch — This parameter, available starting in Windows PowerShell 4.0, eliminates the necessity of specifying a TimeSpan.MaxValue value for the RepetitionDuration parameter to run a scheduled job repeatedly, for an indefinite period.
- `-Daily` switch · **required** · position 0 — Specifies a recurring daily job schedule. Use the other parameters in the Daily parameter set to specify the schedule details.
- `-Weekly` switch · **required** · position 0 — Specifies a recurring weekly job schedule. Use the other parameters in the Weekly parameter set to specify the schedule details.

**Output:** ScheduledJobTrigger

**Example**

```powershell
New-JobTrigger -Weekly -DaysOfWeek Monday -At '9:00 AM'
```

### New-ScheduledJobOption (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Creates options for a scheduled job (run elevated, wake to run, network conditions).

**Syntax** (one line per parameter set)

```powershell
New-ScheduledJobOption [-RunElevated] [-HideInTaskScheduler] [-RestartOnIdleResume] [-MultipleInstancePolicy <TaskMultipleInstancePolicy>] [-DoNotAllowDemandStart] [-RequireNetwork] [-StopIfGoingOffIdle] [-WakeToRun] [-ContinueIfGoingOnBattery] [-StartIfOnBattery] [-IdleTimeout <timespan>] [-IdleDuration <timespan>] [-StartIfIdle] [<CommonParameters>]
```

**Arguments**

- `-RunElevated` switch — Runs the scheduled job with the permissions of a member of the Administrators group on the computer on which the job runs.
- `-HideInTaskScheduler` switch — Do not display the job in Task Scheduler. This value affects only the computer on which the job runs.
- `-RestartOnIdleResume` switch — Restarts a scheduled job when the computer becomes idle. This parameter works with the StopIfGoingOffIdle parameter, which suspends a running scheduled job if the computer becomes active (leaves the idle state).
- `-MultipleInstancePolicy` <TaskMultipleInstancePolicy> — Determines how the system responds to a request to start an instance of a scheduled job while another instance of the job is running.
- `-DoNotAllowDemandStart` switch — Start the job only when it is triggered. Users cannot start the job manually, such as by using the Run feature in Task Scheduler.
- `-RequireNetwork` switch — Runs the scheduled job only when network connections are available.
- `-StopIfGoingOffIdle` switch — Suspends a running scheduled job if the computer becomes active (not idle) while the job is running.
- `-WakeToRun` switch — Wakes the computer from a Hibernate or Sleep state at the scheduled start time so it can run the job.
- `-ContinueIfGoingOnBattery` switch — Do not stop the scheduled job if the computer switches to battery power (disconnects from AC power) while the job is running.
- `-StartIfOnBattery` switch — Starts the scheduled job even if the computer is running on batteries at the scheduled start time.
- `-IdleTimeout` <TimeSpan> — Specifies how long the scheduled job waits for the computer to be idle.
- `-IdleDuration` <TimeSpan> — Specifies how long the computer must be idle before the job starts. The default value is 10 minutes.
- `-StartIfIdle` switch — Starts the scheduled job if the computer has been idle for the time specified by the IdleDuration parameter before the time specified by the IdleTimeout parameter expires.

**Output:** ScheduledJobOptions

**Example**

```powershell
New-ScheduledJobOption -RunElevated -WakeToRun
```

### Register-ScheduledJob (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Creates a scheduled job: a script block or script run by Task Scheduler on triggers.

**Syntax** (one line per parameter set)

```powershell
Register-ScheduledJob [-Name] <string> [-ScriptBlock] <scriptblock> [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-ArgumentList <Object[]>] [-MaxResultCount <int>] [-RunNow] [-RunEvery <timespan>] [-WhatIf] [-Confirm] [<CommonParameters>]

Register-ScheduledJob [-Name] <string> [-FilePath] <string> [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-ArgumentList <Object[]>] [-MaxResultCount <int>] [-RunNow] [-RunEvery <timespan>] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-FilePath` <String> · **required** · position 1 — Specifies a script that the scheduled job runs. Enter the path to a .ps1 file on the local computer.
- `-ScriptBlock` <ScriptBlock> · **required** · position 1 — Specifies the commands that the scheduled job runs. Enclose the commands in curly braces ({}) to create a script block.
- `-Name` <String> · **required** · position 0 — Specifies a name for the scheduled job. The name is also used for all started instances of the scheduled job.
- `-Trigger` <ScheduledJobTrigger[]> — Specifies the triggers for the scheduled job. Enter one or more ScheduledJobTrigger objects, such as the objects that the New-JobTrigger cmdlet returns, or a hash table of job trigger keys and values.
- `-InitializationScript` <ScriptBlock> — Specifies the fully qualified path to a Windows PowerShell script (.ps1).
- `-RunAs32` switch — Runs the scheduled job in a 32-bit process.
- `-Credential` <PSCredential> — Specifies a user account that has permission to run the scheduled job.
- `-Authentication` <AuthenticationMechanism> — Specifies the mechanism that is used to authenticate the user's credentials.
- `-ScheduledJobOption` <ScheduledJobOptions> — Sets options for the scheduled job. Enter a ScheduledJobOptions object, such as one that you create by using the New-ScheduledJobOption cmdlet, or a hash table value.
- `-ArgumentList` <Object[]> — Specifies values for the parameters of the script that is specified by the FilePath parameter or for the command that is specified by the ScriptBlock parameter.
- `-MaxResultCount` <Int32> · default 32 — Specifies how many job result entries are maintained for the scheduled job.
- `-RunNow` switch — Starts a job immediately, as soon as the Register-ScheduledJob cmdlet is run.
- `-RunEvery` <TimeSpan> — Used to specify how often to run the job. For example, use this option to run a job every 15 minutes.

**Output:** ScheduledJobDefinition

**Example**

```powershell
Register-ScheduledJob -Name Backup -ScriptBlock { Copy-Item C:\Data D:\Backup -Recurse } -Trigger (New-JobTrigger -Daily -At 2am)
```

### Remove-JobTrigger (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Deletes triggers from scheduled jobs.

**Syntax** (one line per parameter set)

```powershell
Remove-JobTrigger [-InputObject] <ScheduledJobDefinition[]> [-TriggerId <int[]>] [<CommonParameters>]

Remove-JobTrigger [-Id] <int[]> [-TriggerId <int[]>] [<CommonParameters>]

Remove-JobTrigger [-Name] <string[]> [-TriggerId <int[]>] [<CommonParameters>]
```

**Arguments**

- `-TriggerId` <Int32[]> — Deletes only the specified job triggers. By default, Remove-JobTrigger deletes all triggers from the scheduled jobs.
- `-Id` <Int32[]> · **required** · position 0 — Specifies the identification numbers of the scheduled jobs. Remove-JobTrigger deletes job triggers from the specified scheduled jobs.
- `-Name` <String[]> · **required** · position 0 — Specifies the names of the scheduled jobs. Remove-JobTrigger deletes the job triggers from the specified scheduled jobs.
- `-InputObject` <ScheduledJobDefinition[]> · **required** · position 0 · pipeline by value — Specifies the scheduled jobs. Enter a variable that contains ScheduledJob objects or type a command or expression that gets ScheduledJob objects, such as a Get-ScheduledJob command.

**Output:** None

**Example**

```powershell
Remove-JobTrigger -Name Backup -TriggerId 1
```

### Set-JobTrigger (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Changes a scheduled job's trigger.

**Syntax** (one line per parameter set)

```powershell
Set-JobTrigger [-InputObject] <ScheduledJobTrigger[]> [-DaysInterval <int>] [-WeeksInterval <int>] [-RandomDelay <timespan>] [-At <datetime>] [-User <string>] [-DaysOfWeek <DayOfWeek[]>] [-AtStartup] [-AtLogOn] [-Once] [-RepetitionInterval <timespan>] [-RepetitionDuration <timespan>] [-RepeatIndefinitely] [-Daily] [-Weekly] [-PassThru] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ScheduledJobTrigger[]> · **required** · position 0 · pipeline by value — Specifies the job triggers. Enter a variable that contains ScheduledJobTrigger objects or type a command or expression that gets ScheduledJobTrigger objects, such as a Get-JobTrigger command.
- `-DaysInterval` <Int32> — Specifies the number of days between occurrences on a daily schedule. For example, a value of 3 starts the scheduled job on days 1, 4, 7 and so on.
- `-WeeksInterval` <Int32> — Specifies the number of weeks between occurrences on a weekly job schedule.
- `-RandomDelay` <TimeSpan> — Enables a random delay that begins at the scheduled start time, and sets the maximum delay value.
- `-At` <DateTime> — Starts the job at the specified date and time. Enter a DateTime object, such as one that the Get-Date cmdlet returns, or a string that can be converted to a time, such as April 19, 2012 15:00, 12/31/2013 9:00 PM, or 3am.
- `-User` <String> — Specifies the users who trigger an AtLogon start of a scheduled job. Enter the name of a user in <UserName> or <Domain>\<Username> format or enter an asterisk (*) to represent all users.
- `-DaysOfWeek` <DayOfWeek[]> — Specifies the days of the week on which a weekly scheduled job runs. Enter day names, such as Monday, Thursday, integers 0-6, where 0 represents Sunday, or an asterisk (*) to represent every day.
- `-AtStartup` switch — Starts the scheduled job when Windows starts.
- `-AtLogOn` switch — Starts the scheduled job when the specified users log on to the computer.
- `-Once` switch — Specifies a non-recurring (one time) schedule.
- `-RepetitionInterval` <TimeSpan> — Repeats the job at the specified time interval. For example, if the value of this parameter is 2 hours, the job is triggered every two hours.
- `-RepetitionDuration` <TimeSpan> — Repeats the job until the specified time expires. The repetition frequency is determined by the value of the RepetitionInterval parameter.
- `-RepeatIndefinitely` switch — This parameter, available starting in Windows PowerShell 4.0, eliminates the necessity of specifying a TimeSpan.MaxValue value for the RepetitionDuration parameter to run a scheduled job repeatedly, for an indefinite period.
- `-Daily` switch — Specifies a recurring daily job schedule. Use the other parameters in the Daily parameter set to specify the schedule details.
- `-Weekly` switch — Specifies a recurring weekly job schedule. Use the other parameters in the Weekly parameter set to specify the schedule details.
- `-PassThru` switch — Returns the job triggers that changed. By default, this cmdlet does not generate any output.

**Output:** None; ScheduledJobTrigger

**Example**

```powershell
Get-JobTrigger -Name Backup | Set-JobTrigger -At 4am
```

### Set-ScheduledJob (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Changes a scheduled job (script, triggers, credentials, options).

**Syntax** (one line per parameter set)

```powershell
Set-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-Name <string>] [-ScriptBlock <scriptblock>] [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-MaxResultCount <int>] [-PassThru] [-ArgumentList <Object[]>] [-RunNow] [-RunEvery <timespan>] [<CommonParameters>]

Set-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-Name <string>] [-FilePath <string>] [-Trigger <ScheduledJobTrigger[]>] [-InitializationScript <scriptblock>] [-RunAs32] [-Credential <pscredential>] [-Authentication <AuthenticationMechanism>] [-ScheduledJobOption <ScheduledJobOptions>] [-MaxResultCount <int>] [-PassThru] [-ArgumentList <Object[]>] [-RunNow] [-RunEvery <timespan>] [<CommonParameters>]

Set-ScheduledJob [-InputObject] <ScheduledJobDefinition> [-ClearExecutionHistory] [-PassThru] [<CommonParameters>]
```

**Arguments**

- `-Name` <String> — Specifies a new name for the scheduled job and instances of the scheduled job.
- `-FilePath` <String> — Specifies a script that the scheduled job runs. Enter the path to a .ps1 file on the local computer.
- `-ScriptBlock` <ScriptBlock> — Specifies the commands that the scheduled job runs. Enclose the commands in braces ({}) to create a script block.
- `-Trigger` <ScheduledJobTrigger[]> — Specifies the triggers for the scheduled job. Enter one or more ScheduledJobTrigger objects, such as the objects that the New-JobTrigger cmdlet returns, or a hash table of job trigger keys and values.
- `-InitializationScript` <ScriptBlock> — Specifies the fully qualified path to a Windows PowerShell script (.ps1).
- `-RunAs32` switch — Runs the scheduled job in a 32-bit process.
- `-Credential` <PSCredential> — Specifies a user account that has permission to run the scheduled job.
- `-Authentication` <AuthenticationMechanism> — Specifies the mechanism that is used to authenticate the user's credentials.
- `-ScheduledJobOption` <ScheduledJobOptions> — Sets options for the scheduled job. Enter a ScheduledJobOptions object, such as one that you create by using the New-ScheduledJobOption cmdlet, or a hash table value.
- `-InputObject` <ScheduledJobDefinition> · **required** · position 0 · pipeline by value — Specifies the scheduled job to be changed. Enter a variable that contains ScheduledJobDefinition objects or type a command or expression that gets ScheduledJobDefinition objects, such as a Get-ScheduledJob command.
- `-ClearExecutionHistory` switch — Deletes the current execution history and the saved results of the scheduled job.
- `-MaxResultCount` <Int32> — Specifies how many job result entries are maintained for the scheduled job.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-ArgumentList` <Object[]> — Specifies values for the parameters of the script that is specified by the FilePath parameter or for the command that is specified by the ScriptBlock parameter.
- `-RunNow` switch — Starts a job immediately, as soon as the Set-ScheduledJob cmdlet is run.
- `-RunEvery` <TimeSpan> — Used to specify how often to run the job. For example, use this option to run a job every 15 minutes.

**Output:** None; ScheduledJobDefinition

**Example**

```powershell
Set-ScheduledJob -Name Backup -MaxResultCount 20
```

### Set-ScheduledJobOption (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Changes the options of a scheduled job.

**Syntax** (one line per parameter set)

```powershell
Set-ScheduledJobOption [-InputObject] <ScheduledJobOptions> [-PassThru] [-RunElevated] [-HideInTaskScheduler] [-RestartOnIdleResume] [-MultipleInstancePolicy <TaskMultipleInstancePolicy>] [-DoNotAllowDemandStart] [-RequireNetwork] [-StopIfGoingOffIdle] [-WakeToRun] [-ContinueIfGoingOnBattery] [-StartIfOnBattery] [-IdleTimeout <timespan>] [-IdleDuration <timespan>] [-StartIfIdle] [<CommonParameters>]
```

**Arguments**

- `-InputObject` <ScheduledJobOptions> · **required** · position 0 · pipeline by value — Specifies the job options. Enter a variable that contains ScheduledJobOptions objects or type a command or expression that gets ScheduledJobOptions objects, such as a Get-ScheduledJobOption command.
- `-PassThru` switch — Returns an object representing the item with which you are working. By default, this cmdlet does not generate any output.
- `-RunElevated` switch — Runs the scheduled job with the permissions of a member of the Administrators group on the computer on which the job runs.
- `-HideInTaskScheduler` switch — Do not display the job in Task Scheduler. This value affects only the computer on which the job runs.
- `-RestartOnIdleResume` switch — Restarts a scheduled job when the computer becomes idle. This parameter works with the StopIfGoingOffIdle parameter, which suspends a running scheduled job if the computer becomes active (leaves the idle state).
- `-MultipleInstancePolicy` <TaskMultipleInstancePolicy> — Determines how the system responds to a request to start an instance of a scheduled job while another instance of the job is running.
- `-DoNotAllowDemandStart` switch — Start the job only when it is triggered. Users cannot start the job manually, such as by using the Run feature in Task Scheduler.
- `-RequireNetwork` switch — Runs the scheduled job only when network connections are available.
- `-StopIfGoingOffIdle` switch — Suspends a running scheduled job if the computer becomes active (not idle) while the job is running.
- `-WakeToRun` switch — Wakes the computer from a Hibernate or Sleep state at the scheduled start time so it can run the job.
- `-ContinueIfGoingOnBattery` switch — Do not stop the scheduled job if the computer switches to battery power (disconnects from AC power) while the job is running.
- `-StartIfOnBattery` switch — Starts the scheduled job even if the computer is running on batteries at the scheduled start time.
- `-IdleTimeout` <TimeSpan> — Specifies how long the computer must be idle before the job starts. The default value is 10 minutes.
- `-IdleDuration` <TimeSpan> — Specifies how long the computer must be idle before the job starts. The default value is 10 minutes.
- `-StartIfIdle` switch — Starts the scheduled job if the computer has been idle for the time specified by the IdleDuration parameter before the time specified by the IdleTimeout parameter expires.

**Output:** None; ScheduledJobOptions

**Example**

```powershell
Get-ScheduledJobOption -Name Backup | Set-ScheduledJobOption -RequireNetwork
```

### Unregister-ScheduledJob (PowerShell 5.1 cmdlet)

Module PSScheduledJob

Deletes scheduled jobs and their results.

**Syntax** (one line per parameter set)

```powershell
Unregister-ScheduledJob [-InputObject] <ScheduledJobDefinition[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-ScheduledJob [-Id] <int[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]

Unregister-ScheduledJob [-Name] <string[]> [-Force] [-WhatIf] [-Confirm] [<CommonParameters>]
```

**Arguments**

- `-Id` <Int32[]> · **required** · position 0 — Deletes the scheduled jobs with the specified identification numbers (ID).
- `-Name` <String[]> · **required** · position 0 — Deletes the scheduled jobs with the specified names. Enter the names of one or more scheduled jobs on the computer.
- `-InputObject` <ScheduledJobDefinition[]> · **required** · position 0 · pipeline by value — Specifies a scheduled job. Enter a variable that contains ScheduledJob objects or type a command or expression that gets ScheduledJob objects, such as a Get-ScheduledJob command.
- `-Force` switch — Deletes the scheduled job even if an instance of the job is running. By default, Unregister-ScheduledJob does not interrupt running jobs.

**Output:** None

**Example**

```powershell
Unregister-ScheduledJob -Name Backup
```

## Commands: PSWorkflow

### New-PSWorkflowExecutionOption (PowerShell 5.1 cmdlet)

Module PSWorkflow

Creates options for a workflow session configuration (persistence, limits).

**Syntax** (one line per parameter set)

```powershell
New-PSWorkflowExecutionOption [-PersistencePath <string>] [-MaxPersistenceStoreSizeGB <long>] [-PersistWithEncryption] [-MaxRunningWorkflows <int>] [-AllowedActivity <string[]>] [-OutOfProcessActivity <string[]>] [-EnableValidation] [-MaxDisconnectedSessions <int>] [-MaxConnectedSessions <int>] [-MaxSessionsPerWorkflow <int>] [-MaxSessionsPerRemoteNode <int>] [-MaxActivityProcesses <int>] [-ActivityProcessIdleTimeoutSec <int>] [-RemoteNodeSessionIdleTimeoutSec <int>] [-SessionThrottleLimit <int>] [-WorkflowShutdownTimeoutMSec <int>] [<CommonParameters>]
```

**Arguments**

- `-PersistencePath` <String> — Specifies the location on disk where workflow state and data are stored.
- `-MaxPersistenceStoreSizeGB` <Int64> · default 10 — Specifies the maximum size, in gigabytes, of the persistence store allocated to workflows that run in the session.
- `-PersistWithEncryption` switch — Indicates that the workflow encrypts the data in the persistence store.
- `-MaxRunningWorkflows` <Int32> · default 30 — Specifies that maximum number of workflows that can run in the session concurrently.
- `-AllowedActivity` <String[]> · default PSDefaultActivities — Specifies the activities that are permitted to run in the session.
- `-OutOfProcessActivity` <String[]> · default InlineScript — Determines which allowed activities (specified by the AllowedActivities parameter) run out-of-process.
- `-EnableValidation` switch — Verifies that all workflow activities in the session are included in the allowed activities list.
- `-MaxDisconnectedSessions` <Int32> · default 1000 — Specifies the maximum number of remote sessions that are in a disconnected state.
- `-MaxConnectedSessions` <Int32> · default 100 — Specifies the maximum number of remote sessions that are in an operational state.
- `-MaxSessionsPerWorkflow` <Int32> · default 5 — Specifies the maximum number of session that can be created to support each workflow.
- `-MaxSessionsPerRemoteNode` <Int32> · default 5 — Specifies the maximum number of sessions that can be connected to each remote node (target computer).
- `-MaxActivityProcesses` <Int32> · default 5 — Specifies the maximum number of processes that can be created in the session to support workflow activities.
- `-ActivityProcessIdleTimeoutSec` <Int32> · default 60 — Determines how long each activity host process is maintained after the process becomes idle.
- `-RemoteNodeSessionIdleTimeoutSec` <Int32> · default 60 — Specifies how long a session that is connected to a remote node (target computer) is maintained if it is idle.
- `-SessionThrottleLimit` <Int32> · default 100 — Specifies how many operations are created to support all workflows started in the session.
- `-WorkflowShutdownTimeoutMSec` <Int32> · default 500 — Specifies how long the session is maintained after all workflows in the session are forcibly suspended.

**Output:** PSWorkflowExecutionOption

**Example**

```powershell
New-PSWorkflowExecutionOption -MaxSessionsPerWorkflow 10
```

### New-PSWorkflowSession (PowerShell 5.1 function)

Module PSWorkflow · aliases: nwsn

Creates a PSSession that uses the workflow session configuration. Alias: nwsn.

**Syntax** (one line per parameter set)

```powershell
New-PSWorkflowSession [[-ComputerName] <string[]>] [-Credential <Object>] [-Name <string[]>] [-Port <int>] [-UseSSL] [-ApplicationName <string>] [-ThrottleLimit <int>] [-SessionOption <PSSessionOption>] [-Authentication <AuthenticationMechanism>] [-CertificateThumbprint <string>] [-EnableNetworkAccess] [<CommonParameters>]
```

**Arguments**

- `-ComputerName` <String[]> · position 0 · pipeline by value and by property name · alias -Cn · default Local computer — Creates a persistent connection (PSSession) to the specified computer.
- `-Credential` <Object> · pipeline by property name · default Current user — Specifies a user account that has permission to perform this action. The default is the current user.
- `-Name` <String[]> · default Session# — Specifies a friendly name for the workflow session. You can use the name with other cmdlets, such as Get-PSSession and Enter-PSSession.
- `-Port` <Int32> — Specifies the network port on the remote computer that is used for this connection.
- `-UseSSL` switch — Indicates that this cmdlet uses the Secure Sockets Layer (SSL) protocol to establish a connection to the remote computer.
- `-ApplicationName` <String> · pipeline by property name — Specifies the application name segment of the connection URI.
- `-ThrottleLimit` <Int32> · default 100 — Specifies the maximum number of concurrent connections that can be established to run this command.
- `-SessionOption` <PSSessionOption> — Specifies advanced options for the session. Enter a SessionOption object, such as one that you create by using the New-PSSessionOption cmdlet.
- `-Authentication` <AuthenticationMechanism> · default Default — Specifies the mechanism that is used to authenticate the user credentials.
- `-CertificateThumbprint` <String> — Specifies the digital public key certificate (X509) of a user account that has permission to perform this action.
- `-EnableNetworkAccess` switch — Indicates that this cmdlet adds an interactive security token to loopback sessions.

**Output:** PSSession

**Example**

```powershell
$ws = New-PSWorkflowSession -ComputerName Server01
```

## Commands: PSWorkflowUtility

### Invoke-AsWorkflow (PowerShell 5.1 cmdlet)

Module PSWorkflowUtility

Runs a command or expression as a Windows PowerShell workflow.

**Syntax** (one line per parameter set)

```powershell
Invoke-AsWorkflow [-CommandName <string>] [-Parameter <hashtable>] [<WorkflowCommonParameters>] [<CommonParameters>]

Invoke-AsWorkflow [-Expression <string>] [<WorkflowCommonParameters>] [<CommonParameters>]
```

**Output:** None

**Example**

```powershell
Invoke-AsWorkflow -Expression 'Get-Process' -PSComputerName Server01
```

## Commands: ISE

### Get-IseSnippet (PowerShell 5.1 function)

Module ISE

(PowerShell ISE only) Lists the code snippets you created in ISE.

**Syntax** (one line per parameter set)

```powershell
Get-IseSnippet [<CommonParameters>]
```

**Output:** FileInfo

**Example**

```powershell
Get-IseSnippet
```

### Import-IseSnippet (PowerShell 5.1 function)

Module ISE

(PowerShell ISE only) Loads snippets from a folder or module into the ISE session.

**Syntax** (one line per parameter set)

```powershell
Import-IseSnippet [-Path] <String> [-Recurse] [<CommonParameters>]

Import-IseSnippet [-Recurse] -Module <String> [-ListAvailable] [<CommonParameters>]
```

**Output:** None

**Example**

```powershell
Import-IseSnippet -Path \\Server\Share\Snippets -Recurse
```

### New-IseSnippet (PowerShell 5.1 function)

Module ISE

(PowerShell ISE only) Creates a reusable code snippet for ISE.

**Syntax** (one line per parameter set)

```powershell
New-IseSnippet [-Title] <String> [-Description] <String> [-Text] <String> [-Author <String>] [-CaretOffset <Int32>] [-Force] [<CommonParameters>]
```

**Output:** None

**Example**

```powershell
New-IseSnippet -Title Try-Catch -Description 'Try/catch block' -Text 'try { } catch { }'
```
