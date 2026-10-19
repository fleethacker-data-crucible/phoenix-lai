## Checklist: before you answer with {PS} code

- Every cmdlet name is Verb-Noun and exists in this sheet; never invent a cmdlet or a parameter. Check each argument against the command's **Arguments** list.
- Arguments from different syntax lines (parameter sets) are never combined, for example `-Path` with `-LiteralPath`.
- Comparison uses `-eq -ne -gt -lt -ge -le -like -match`, never `==`, `!=`, `>` or `<`. `>` is file redirection, so `if ($a > 5)` writes a file named 5.
- Variables start with `$`. Strings in `"double quotes"` expand `$var` and `$(expr)`; strings in `'single quotes'` do not.
- `$null` goes on the left in comparisons: `$null -eq $x`. With an array on the left, `-eq` filters the array instead.
- A function's output is everything it does not capture, not only what follows `return`. Use `[void]`, `$null =` or `| Out-Null` to drop unwanted output, such as from `ArrayList.Add` or `New-Item`.
- `$ErrorActionPreference = 'Stop'` or `-ErrorAction Stop` is needed before `try/catch` catches non-terminating cmdlet errors.
- Paths with `[ ]` need `-LiteralPath`. Paths with spaces need quotes.
- Use the call operator `&` to run a path or a command held in a variable: `& "C:\Program Files\x\app.exe" -a 1`.
- Line continuation is a backtick `` ` `` as the very last character, or a break after `|`, `,`, `{` or an operator. A space after the backtick breaks it.
- Never use aliases (`%`, `?`, `ls`, `gci`) in scripts you hand to the user. Use full names.
{EDITION_CHECKS}

## Language: variables, types and literals

### Variables and assignment ({PS} syntax)

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

### Automatic variables ({PS} syntax)

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

### Numbers, strings and literals ({PS} syntax)

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

**Escape sequences** (double quotes only, using the backtick): `` `n `` newline, `` `r `` carriage return, `` `t `` tab, `` `0 `` null, `` `a `` alert, `` `b `` backspace, `` `" `` quote, `` `$ `` dollar, `` `` `` backtick{EDITION_ESCAPES}.

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

### Arrays ({PS} syntax)

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

### Hashtables and custom objects ({PS} syntax)

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

### Type casting and conversion ({PS} syntax)

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

### Arithmetic and assignment operators ({PS} syntax)

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

### Comparison operators ({PS} syntax)

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

### Logical, string, type and redirection operators ({PS} syntax)

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
{EDITION_OPERATORS}
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

### if / elseif / else ({PS} statement)

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

### switch ({PS} statement)

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

### foreach statement and ForEach-Object ({PS} statement)

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

### for, while, do-while, do-until ({PS} statement)

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

### try / catch / finally, throw, trap ({PS} statement)

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

### function and param() ({PS} statement)

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

### Script files, param() and dot-sourcing ({PS} syntax)

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

### Script blocks and splatting ({PS} syntax)

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

### Classes and enums ({PS} syntax)

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

### The pipeline ({PS} syntax)

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

### Output streams and writing messages ({PS} syntax)

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

### String methods ({PS} syntax)

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

### Files and paths ({PS} syntax)

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
{EDITION_FILES}
