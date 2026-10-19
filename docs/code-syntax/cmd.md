# CMD syntax reference (Phoenix)

For Phoenix models writing and checking Windows Command Prompt (cmd.exe) commands and batch files (`.bat`, `.cmd`). Each `###` entry is one command or batch feature: what it does, its exact syntax, every switch, an example, and the mistakes to check for. Switch lists come from each command's own `/?` help on Windows 11.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply. Switches are case-insensitive and start with `/` (some tools also accept `-`).

## Essentials

Target: Windows cmd.exe batch files (`.bat` / `.cmd`). The user runs them; the assistant only writes them.

| Never in batch | Write instead |
|---|---|
| `set x = 5` (makes a variable named "x " with value " 5") | `set "x=5"`; numbers: `set /a n=5` |
| `%var%` changed and read inside the same ( ) block | `setlocal EnableDelayedExpansion` and `!var!` |
| `if %x% == abc` with possibly empty or spaced values | `if /i "%x%"=="abc"` (quotes on both sides) |
| `for %f in (*.txt)` inside a .bat | `for %%f in (*.txt) do ...` (doubled % in files) |
| `if errorlevel 0` (true for any code >= 0) | `if %errorlevel% neq 0 ( ... )` or `cmd && ok \|\| fail` |
| `exit` (closes the caller's window) | `exit /b 0` / `exit /b 1` |
| `rm`, `cp`, `ls`, `$var` (Unix / PowerShell) | `del`, `copy` / `xcopy` / `robocopy`, `dir`, `%var%` |
| `echo` of user input containing `& \| < >` unescaped | `^&`, `^\|`, `^<`, `^>` |

Always: start with `@echo off` and `setlocal`; quote every path (`"%~dp0data\in.txt"`); `%~dp0` is the script's folder; `%~1` the first argument without quotes; `goto :eof` returns from a `call :label`.

## Recipes

### Script skeleton with arguments and error codes (%~1, if not defined, exit /b)

**Example**

```bat
@echo off
setlocal
if "%~1"=="" (
  echo Usage: %~nx0 ^<folder^>
  exit /b 2
)
set "SRC=%~1"
if not exist "%SRC%\" (
  echo Folder not found: "%SRC%"
  exit /b 1
)
echo Processing "%SRC%"
exit /b 0
```

### Loop over files and count them (for %%f, setlocal EnableDelayedExpansion, set /a)

**Example**

```bat
@echo off
setlocal EnableDelayedExpansion
set /a count=0
for %%f in ("C:\Logs\*.log") do (
  set /a count+=1
  echo !count!: %%~nxf  %%~zf bytes
)
echo Total: %count%
```

### Copy a folder tree and test the result (robocopy exit codes)

**Example**

```bat
@echo off
robocopy "C:\Data" "D:\Backup\Data" /E /R:2 /W:5 /NP
if %errorlevel% geq 8 (
  echo Copy failed with code %errorlevel%
  exit /b 1
)
echo Copy finished
```

### Read a file line by line and parse fields (for /f tokens delims)

**Example**

```bat
@echo off
for /f "usebackq skip=1 tokens=1,2 delims=," %%a in ("C:\Data\users.csv") do (
  echo Name=%%a Email=%%b
)
```

### Date-stamped file names (locale-independent yyyy-MM-dd)

`%date%` depends on the regional settings, and `wmic` is removed by default from recent Windows 11, so ask PowerShell for the date:

**Example**

```bat
@echo off
for /f %%i in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"') do set "stamp=%%i"
copy "C:\Reports\report.csv" "D:\Archive\report-%stamp%.csv"
```

### Subroutines with call and return values (call :label, goto :eof)

**Example**

```bat
@echo off
setlocal
call :square 7 result
echo 7 squared is %result%
exit /b 0

:square
set /a "%~2=%~1 * %~1"
goto :eof
```

## Rules: before you answer with CMD / batch code

- In a batch file, `FOR` variables are written `%%i`. At the prompt they are `%i`. They are single letters and case-sensitive.
- A variable set inside a `( ... )` block (an `IF` or `FOR` body) is expanded when the block is parsed, so `%var%` shows the OLD value. Add `setlocal EnableDelayedExpansion` and read it as `!var!`.
- `SET var = value` (with spaces) creates a variable named `var ` with the value ` value`. Write `set "var=value"`, with the quotes around the whole assignment.
- Arithmetic needs `set /a`: `set /a n=n+1`. User input needs `set /p ans=Prompt: `.
- Quote paths with spaces: `"C:\Program Files\App\app.exe"`. Inside `IF` comparisons, quote both sides: `if "%x%"=="yes"`, so an empty value does not cause a syntax error.
- In batch files, `%` is written `%%` (literal percent, `FOR` variables). `!` needs `^!` when delayed expansion is on. Escape `& | < > ^ ( )` with `^` outside quotes.
- `ERRORLEVEL` checks: `if errorlevel 1` means "1 or higher". For an exact test use `if %errorlevel% neq 0`. Check it right after the command, before another command changes it.
- `CALL` runs another batch file and returns. Without `CALL`, control never comes back. Labels used with `goto` and `call :label` start with `:`.
- End scripts that must report status with `exit /b <code>`. Plain `exit` closes the whole cmd window.
- Use `rd /s /q` for folders and `del` for files. `del` with a folder name deletes the files in it, not the folder.
- Comment with `rem`. `::` breaks inside `( )` blocks.
- Never add `/f`, `/q` or `/y` to destructive commands unless the user asked for it. Show the user what will be deleted or changed.
- PowerShell syntax (`$var`, `Get-ChildItem`, `-eq`) is not valid in CMD, and CMD syntax (`%var%`, `set`) is not valid in PowerShell.

## Batch file structure

### Batch file skeleton (CMD batch syntax)

**Syntax**

```bat
@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem ---- arguments: %1 .. %9, all: %*, script folder: %~dp0
if "%~1"=="" (
    echo Usage: %~nx0 ^<folder^>
    exit /b 1
)
set "SRC=%~1"
call :process "%SRC%"
if errorlevel 1 exit /b %errorlevel%
echo Done.
exit /b 0

:process
rem ---- subroutine: arguments are %1, %2 ...; return with exit /b
echo Processing %~1
exit /b 0
```

**Parts**

- `@echo off`: do not print each command. The `@` hides that line itself.
- `setlocal`: changes to variables stay local to the script until `endlocal` or the end of the script.
- `exit /b <n>`: leave the script or subroutine, and set `ERRORLEVEL` to `n`.
- `:label`: a jump target for `goto label` and `call :label`.

**Check**

- Execution falls through into the subroutines unless the main part ends with `exit /b` (or `goto :eof`).
- Save batch files in ANSI or OEM encoding, or set `chcp 65001` for UTF-8 text. A UTF-8 BOM breaks the first line.

### Arguments and parameter modifiers %0-%9, %* and %~ (CMD batch syntax)

**Syntax**

```bat
%0 = the script as invoked   %1..%9 = arguments   %* = all arguments   shift [/n]  = move %2 into %1 ...
%~1    remove surrounding quotes
%~f1   full path            %~d1  drive (C:)        %~p1  path (\dir\)       %~n1  name without extension
%~x1   extension (.txt)     %~s1  short 8.3 path    %~a1  attributes          %~t1  date/time      %~z1  size in bytes
%~$PATH:1   first match of the file on PATH
%~dp0  folder of this script (ends with \)          %~nx0 the script file name
```

**Check**

- These modifiers work on `%0`-`%9` and on `FOR` variables (`%%~nxF`), not on normal variables. For `%var%`, use substring and replace syntax instead.

### Variables: SET, %var%, !var!, set /a, set /p (CMD built-in)

**Syntax**

```bat
SET                               :: list all variables
SET prefix                        :: list variables starting with prefix
set "name=value"                  :: assign (quotes keep stray spaces out)
set "name="                       :: delete
set /a result=(a+b)*2, n+=1, x=0x1F, y=7%%3   :: integer math; %% = modulo in a batch file
set /p "answer=Continue? [y/n] "  :: read a line from the user (or from redirected input)
echo %name%   echo !name! (with delayed expansion)
%var:~start,length%   %var:~-3%   %var:old=new%   %var:*prefix=%    :: substring / replace / remove up to prefix
```

**Arguments of set /a:** the operators are `( ) ! ~ - * / % + - << >> & ^ | = *= /= %= += -= &= ^= |= <<= >>= ,`. Values are 32-bit signed integers. Variable names inside `set /a` need no `%`.

**Built-in dynamic variables:** `%CD%`, `%DATE%`, `%TIME%`, `%RANDOM%` (0-32767), `%ERRORLEVEL%`, `%CMDEXTVERSION%`, `%CMDCMDLINE%`, `%HIGHESTNUMANODENUMBER%`. Common environment variables: `%USERPROFILE%`, `%APPDATA%`, `%LOCALAPPDATA%`, `%TEMP%`, `%PROGRAMFILES%`, `%PROGRAMFILES(X86)%`, `%SYSTEMROOT%`, `%WINDIR%`, `%COMPUTERNAME%`, `%USERNAME%`, `%PATH%`, `%PATHEXT%`, `%OS%`.

**Check**

- `set /a` does integer math only: `set /a x=7/2` gives 3. There are no decimals.
- `%DATE%` and `%TIME%` depend on the locale. Do not parse them for file names on other machines. Use `wmic os get localdatetime` or PowerShell instead.
- A leading zero means octal: `set /a x=08` is an error.

### SETLOCAL and ENDLOCAL (CMD built-in)

**Syntax**

```bat
SETLOCAL [EnableExtensions | DisableExtensions] [EnableDelayedExpansion | DisableDelayedExpansion]
ENDLOCAL
endlocal & set "result=%value%"     :: pass a value out of the local scope
```

**Example**

```bat
setlocal EnableDelayedExpansion
set count=0
for %%f in (*.txt) do (
    set /a count+=1
    echo !count!: %%f
)
echo Total: !count!
endlocal
```

### ECHO, REM, PAUSE, CLS, TITLE, COLOR (CMD built-ins)

**Syntax**

```bat
ECHO [ON | OFF]        ECHO message        ECHO.   (blank line)     ECHO(%var%   (safe even when var is empty or "on")
REM comment            :: comment (not inside ( ) blocks)
PAUSE                  :: "Press any key to continue . . ."   ( pause >nul  = silent )
CLS                    :: clear the screen
TITLE text             :: window title
COLOR [bf]             :: b = background, f = foreground hex digit: 0 black 1 blue 2 green 3 aqua 4 red 5 purple 6 yellow 7 white 8 gray 9-F light variants  (color 0A)
```

**Check**

- `echo %var%` prints "ECHO is off." when `var` is empty. Write `echo(%var%`.
- To print special characters, escape them: `echo 5 ^> 3`, `echo a ^& b`, `echo 100%%`.

### Redirection and pipes (CMD syntax)

**Syntax**

```bat
command > file.txt        :: stdout to file (overwrite)
command >> file.txt       :: append
command 2> err.txt        :: stderr
command > all.txt 2>&1    :: stdout and stderr to the same file (order matters)
command > nul 2>&1        :: discard all output
command < input.txt       :: stdin from a file
cmd1 | cmd2               :: pipe stdout of cmd1 into cmd2
cmd1 & cmd2               :: run cmd2 after cmd1
cmd1 && cmd2              :: run cmd2 only if cmd1 succeeded (errorlevel 0)
cmd1 || cmd2              :: run cmd2 only if cmd1 failed
( cmd1 & cmd2 ) > out.txt :: group
```

**Check**

- Write `2>&1` after the file redirection: `> file 2>&1`. `2>&1 > file` sends errors to the screen.
- `echo text> file` writes no trailing space, while `echo text > file` writes a trailing space.

## Flow control

### IF (CMD built-in)

**Syntax**

```bat
IF [NOT] ERRORLEVEL number command
IF [NOT] string1==string2 command
IF [NOT] EXIST filename command
IF [/I] string1 compare-op string2 command
IF CMDEXTVERSION number command
IF DEFINED variable command
IF condition (command) ELSE (command)
```

**Arguments**

- `NOT`: inverts the condition.
- `ERRORLEVEL number`: true if the last exit code is greater than or equal to `number`.
- `string1==string2`: exact, case-sensitive comparison.
- `EXIST filename`: a file or folder exists. Use `EXIST "folder\"` (trailing backslash) to test for a folder.
- `/I`: case-insensitive comparison.
- `compare-op`: `EQU` equal, `NEQ` not equal, `LSS` less, `LEQ` less or equal, `GTR` greater, `GEQ` greater or equal. Both sides are compared as numbers when both are numeric, otherwise as strings.
- `DEFINED variable`: the variable exists. Give the name without `%`.
- `ELSE`: must be on the same line as the closing `)` of the IF part.

**Example**

```bat
if not exist "%TARGET%\" (
    mkdir "%TARGET%"
) else (
    echo Folder exists
)
if /i "%choice%"=="y" goto :yes
if %errorlevel% neq 0 echo Failed with %errorlevel%
```

**Check**

- Write `) else (` on one line. Putting `else` on its own line is a syntax error.
- `if %x%==` fails with a syntax error when `x` is empty. Quote both sides.
- `if "%a%" GTR "%b%"` compares strings, so `"10"` sorts before `"9"`. For numbers, leave out the quotes and make sure both values are set.

### FOR (CMD built-in)

**Syntax**

```bat
FOR %%v IN (set) DO command                          :: each item / file matching wildcards in set
FOR /D %%v IN (set) DO command                       :: directories matching the wildcards
FOR /R [[drive:]path] %%v IN (set) DO command        :: recurse the folder tree; set "." = every folder
FOR /L %%v IN (start,step,end) DO command            :: numeric loop, end inclusive
FOR /F ["options"] %%v IN (file-set) DO command      :: each line of files
FOR /F ["options"] %%v IN ("string") DO command      :: parse a string
FOR /F ["options"] %%v IN ('command') DO command     :: each line of a command's output
```

**Options of /F** (inside one quoted string)

- `eol=c`: the comment character, whose lines are skipped. The default is `;`.
- `skip=n`: skip the first n lines.
- `delims=xxx`: the delimiter characters (default space and tab). `delims=` means none, so the whole line is read.
- `tokens=x,y,m-n,*`: the fields to assign to `%%v`, `%%w`, … in order. `*` takes the rest of the line.
- `usebackq`: lets the file-set be quoted (`"file name.txt"`), and uses backquotes for commands (`` `cmd` ``).

**Example**

```bat
for %%f in ("C:\Logs\*.log") do echo %%~nxf  %%~zf bytes
for /r "%~dp0src" %%f in (*.cs) do echo %%f
for /l %%i in (1,1,5) do echo Step %%i
for /f "usebackq tokens=1,2 delims=," %%a in ("data file.csv") do echo Name=%%a Value=%%b
for /f "tokens=*" %%l in ('dir /b /ad') do echo Folder: %%l
for /f "skip=1 tokens=2 delims==" %%v in ('wmic os get localdatetime /value') do set "dt=%%v"
```

**Check**

- In a batch file, use `%%f`. At the prompt, use `%f`.
- `FOR /F` skips empty lines, and skips lines that start with the `eol` character (`;`).
- A command with special characters inside `('...')` needs escaping: `('dir ^| find "x"')`.
- Use `!var!` to update variables inside the loop. See SETLOCAL.

### GOTO, CALL, EXIT, SHIFT (CMD built-ins)

**Syntax**

```bat
GOTO label            GOTO :EOF             :: end the current script/subroutine
CALL [drive:][path]file.bat [args]          :: run another batch file and return
CALL :label [args]                          :: call a subroutine in this file
CALL command                                :: run a command with a second round of % expansion
EXIT [/B] [exitCode]                        :: /B = leave the script (not cmd.exe), set ERRORLEVEL
SHIFT [/n]                                  :: shift arguments left, starting at %n
```

**Check**

- Running `other.bat` without `CALL` from a batch file never returns.
- `goto` cannot jump into the middle of a `( )` block, and inside one it breaks the block.

### CHOICE and TIMEOUT (CMD commands)

**Syntax**

```bat
CHOICE [/C choices] [/N] [/CS] [/T timeout /D choice] [/M "text"]
TIMEOUT /T seconds [/NOBREAK]
```

**Arguments**

- `/C choices`: the allowed keys (default `YN`). `ERRORLEVEL` is set to the position of the key pressed (1 for the first).
- `/N`: hide the choice list.
- `/CS`: case-sensitive.
- `/T timeout /D choice`: pick the default after `timeout` seconds (0-9999).
- `/M text`: the prompt message.
- `TIMEOUT /T seconds`: wait (-1 waits for a key).
- `/NOBREAK`: ignore key presses, and stop only on Ctrl+C.

**Example**

```bat
choice /c YNC /m "Save changes"
if errorlevel 3 goto cancel
if errorlevel 2 goto no
timeout /t 5 /nobreak >nul
```

**Check**

- Test the `errorlevel` values of `CHOICE` from the highest down, because `if errorlevel 1` is also true for 2 and 3.

## Files and folders

### DIR (CMD built-in)

**Syntax**

```bat
DIR [drive:][path][filename] [/A[[:]attributes]] [/B] [/C] [/D] [/L] [/N] [/O[[:]sortorder]] [/P] [/Q] [/R] [/S] [/T[[:]timefield]] [/W] [/X] [/4]
```

**Switches**

- `/A[:]attributes`: `D` directories, `R` read-only, `H` hidden, `A` ready for archiving, `S` system, `I` not content indexed, `L` reparse points, `O` offline, `-` prefix = not. `/A` alone shows everything.
- `/B`: bare format, names only.
- `/C`: thousand separators (the default). `/-C` turns them off.
- `/D`: wide format sorted by column.
- `/L`: lowercase.
- `/N`: long list format, with names on the right.
- `/O[:]sortorder`: `N` name, `S` size, `E` extension, `D` date/time, `G` directories first, `-` prefix = reverse (`/O-D` newest first).
- `/P`: pause after each screen.
- `/Q`: show the owner.
- `/R`: show alternate data streams.
- `/S`: include subfolders.
- `/T[:]timefield`: `C` created, `A` last accessed, `W` last written.
- `/W`: wide format.
- `/X`: show 8.3 short names.
- `/4`: four-digit years.

**Example**

```bat
dir /b /s /a-d "C:\Projects\*.cs" > files.txt
dir /o-d /tw *.log
```

### COPY (CMD built-in)

**Syntax**

```bat
COPY [/D] [/V] [/N] [/Y | /-Y] [/Z] [/L] [/A | /B] source [/A | /B] [+ source [/A | /B] [+ ...]] [destination [/A | /B]]
```

**Switches**

- `source`: the file or files (wildcards allowed) to copy. Several sources joined with `+` are concatenated.
- `destination`: the target file or folder.
- `/A`: ASCII text. Copying stops at Ctrl+Z.
- `/B`: binary file.
- `/D`: let the destination be created decrypted.
- `/V`: verify the written files.
- `/N`: use short names.
- `/Y`: overwrite without asking.
- `/-Y`: ask before overwriting.
- `/Z`: restartable mode for network copies.
- `/L`: copy a symbolic link itself, not its target.

**Example**

```bat
copy /y "%~dp0config.default.ini" "%APPDATA%\MyApp\config.ini"
copy /b part1.bin + part2.bin whole.bin
```

**Check**

- `COPY` does not copy folders or subfolders. Use `XCOPY` or `ROBOCOPY` for those.

### XCOPY (CMD command)

**Syntax**

```bat
XCOPY source [destination] [/A | /M] [/D[:m-d-y]] [/P] [/S [/E]] [/V] [/W] [/C] [/I] [/-I] [/Q] [/F] [/L] [/G] [/H] [/R] [/T] [/U] [/K] [/N] [/O] [/X] [/Y] [/-Y] [/Z] [/B] [/J] [/EXCLUDE:file1[+file2]...] [/COMPRESS] [/[-]SPARSE] [/NOCLONE]
```

**Switches**

- `/A`: only files with the archive attribute, leaving the attribute unchanged.
- `/M`: only files with the archive attribute, then clear it.
- `/D[:m-d-y]`: only files changed on or after the date. With no date, only files newer than the destination.
- `/EXCLUDE:file1[+file2]`: files that list strings, one per line. Any path containing one of them is skipped.
- `/P`: ask before each file.
- `/S`: subfolders, except empty ones.
- `/E`: subfolders, including empty ones (use it with `/S`).
- `/V`: verify sizes.
- `/W`: wait for a key first.
- `/C`: continue after errors.
- `/I`: when the destination does not exist and there are several files, treat it as a folder.
- `/-I`: treat it as a file.
- `/Q`: quiet.
- `/F`: show full names.
- `/L`: list what would be copied, without copying.
- `/G`: copy encrypted files to a destination that does not support encryption.
- `/H`: include hidden and system files.
- `/R`: overwrite read-only files.
- `/T`: create the folder tree only (with `/E`, include empty folders).
- `/U`: only files that already exist in the destination.
- `/K`: keep attributes.
- `/N`: short names.
- `/O`: copy ownership and ACLs.
- `/X`: also copy audit settings.
- `/Y`, `/-Y`: overwrite without asking, or ask.
- `/Z`: restartable network mode.
- `/B`: copy symbolic links themselves.
- `/J`: unbuffered I/O, for big files.
- `/COMPRESS`: network compression.
- `/[-]SPARSE`: keep or drop the sparse state.
- `/NOCLONE`: no block cloning.

**Example**

```bat
xcopy "C:\Site" "D:\Backup\Site" /e /i /h /y
```

**Check**

- Without `/I`, XCOPY asks "File or Directory?" when the destination does not exist.

### ROBOCOPY (CMD command)

Robust file copy and mirror. Best for folder trees, big copies and backups.

**Syntax**

```bat
ROBOCOPY source destination [file [file]...] [options]
```

**Options** (the most used)

- `file ...`: file names or wildcards (default `*.*`).
- Copy: `/S` subfolders, but not empty ones · `/E` subfolders, including empty ones · `/LEV:n` only the top n levels · `/Z` restartable · `/B` backup mode · `/ZB` restartable, falling back to backup · `/J` unbuffered I/O · `/COPY:DATSOU` what to copy: D data, A attributes, T timestamps, S security, O owner, U auditing (default DAT) · `/DCOPY:DAT` for folders · `/SEC` (= `/COPY:DATS`) · `/COPYALL` (= `/COPY:DATSOU`) · `/NOCOPY` · `/MT[:n]` multithreaded, n threads (default 8, range 1-128).
- Mirror and move: `/MIR` mirror the tree (= `/E` plus `/PURGE`, which DELETES destination files missing from the source) · `/PURGE` · `/MOV` move files (delete them from the source after copying) · `/MOVE` move files and folders · `/CREATE` tree and empty files only.
- Selection: `/XF file...` exclude files · `/XD dir...` exclude folders · `/XO` skip older files · `/XN` skip newer files · `/XC` skip changed files · `/XX` skip extra destination files (no purge) · `/XL` skip "lonely" source-only files · `/IS` include same files · `/IT` include tweaked files · `/MAX:n` / `/MIN:n` size limits in bytes · `/MAXAGE:n` / `/MINAGE:n` age in days or a YYYYMMDD date · `/A` only archive-attribute files · `/M` same, then reset the attribute · `/IA:` and `/XA:` include or exclude by attributes `RASHCNETO` · `/XJ` exclude junctions · `/FFT` assume FAT file times (2-second granularity) · `/DST` compensate for DST.
- Retry: `/R:n` retries (default 1 million, so always set it) · `/W:n` seconds between retries (default 30).
- Logging: `/L` list only, no copy (dry run) · `/LOG:file` · `/LOG+:file` append · `/TEE` also print to the console · `/NP` no percentage · `/NFL` no file list · `/NDL` no folder list · `/NJH` no job header · `/NJS` no job summary · `/V` verbose · `/ETA` · `/BYTES` · `/UNILOG:file`.
- Other: `/MON:n` / `/MOT:m` watch and rerun · `/RH:hhmm-hhmm` run hours · `/IPG:n` gap between packets in ms (bandwidth) · `/SL` copy symbolic links as links · `/COMPRESS` · `/JOB:name` / `/SAVE:name`.

**Exit codes:** 0 = nothing copied · 1 = files copied · 2 = extra files at the destination · 4 = mismatches · 8 = some copies failed · 16 = fatal error. Values combine, so **a code of 8 or higher is an error**, and 1-7 is success.

**Example**

```bat
robocopy "C:\Data" "E:\Backup\Data" /e /z /r:2 /w:5 /mt:16 /xd node_modules .git /xf *.tmp /log:"%TEMP%\backup.log" /tee /np
if %errorlevel% geq 8 echo Backup FAILED & exit /b 1
```

**Check**

- `/MIR` and `/PURGE` delete files at the destination. Swapping the source and destination wipes data. Confirm the direction, and do a dry run with `/L` first.
- Do not end a quoted path with a backslash (`"C:\Data\"`): the `\"` escapes the quote. Write `"C:\Data"` or `"C:\Data\."`.
- ROBOCOPY returns 1 on success, so `if errorlevel 1` is wrong here. Use `geq 8`.

### MOVE, REN/RENAME (CMD built-ins)

**Syntax**

```bat
MOVE [/Y | /-Y] [drive:][path]filename1[,...] destination
MOVE [/Y | /-Y] [drive:][path]dirname1 dirname2          :: rename a folder
REN [drive:][path]filename1 filename2                    :: new name only, no path
```

**Switches**

- `/Y`: overwrite without asking.
- `/-Y`: ask first.

**Example**

```bat
move /y "*.log" "D:\Archive\"
ren "report.txt" "report-old.txt"
ren *.jpeg *.jpg
```

**Check**

- In `REN`, the second argument cannot contain a drive or folder. It is a new name in the same folder.

### DEL/ERASE and RD/RMDIR, MD/MKDIR (CMD built-ins)

**Syntax**

```bat
DEL [/P] [/F] [/S] [/Q] [/A[[:]attributes]] names
RD [/S] [/Q] [drive:]path
MD [drive:]path                :: creates any missing parent folders
```

**Switches**

- `names`: files or wildcards. A folder name deletes the files inside that folder.
- `/P`: ask before each file.
- `/F`: delete read-only files.
- `/S`: include the subfolders.
- `/Q`: do not ask on wildcards.
- `/A:attributes`: `R S H A I L O` (and `-` for not).
- `RD /S`: remove the folder with all its contents.
- `RD /Q`: do not ask.

**Example**

```bat
if exist "%TEMP%\build\" rd /s /q "%TEMP%\build"
md "%~dp0out\logs"
del /q "%~dp0out\*.tmp"
```

**Check**

- These commands do not use the Recycle Bin. Deleted files are gone.
- `del` does not remove folders, and `rd` without `/s` removes only an empty folder.

### ATTRIB (CMD command)

**Syntax**

```bat
ATTRIB [+R | -R] [+A | -A] [+S | -S] [+H | -H] [+O | -O] [+I | -I] [+X | -X] [+P | -P] [+U | -U] [drive:][path][filename] [/S [/D]] [/L]
```

**Switches**

- `+` sets and `-` clears an attribute: `R` read-only, `A` archive, `S` system, `H` hidden, `O` offline, `I` not content indexed, `X` no scrub, `P` pinned, `U` unpinned.
- `/S`: matching files in all subfolders.
- `/D`: folders too.
- `/L`: act on a symbolic link, not its target.

**Example**

```bat
attrib -r -h "C:\Data\*.*" /s /d
```

### TYPE, MORE, SORT, FIND, FINDSTR, FC, COMP, CLIP (CMD commands)

**Syntax**

```bat
TYPE [drive:][path]filename
MORE [/E [/C] [/P] [/S] [/Tn] [+n]] < file      command | MORE
SORT [/R] [/+n] [/M kb] [/L locale] [/REC recordbytes] [[drive:][path]file] [/T tempdir] [/O outfile]
FIND [/V] [/C] [/N] [/I] [/OFF[LINE]] "string" [[drive:][path]filename[ ...]]
FINDSTR [/B] [/E] [/L] [/R] [/S] [/I] [/X] [/V] [/N] [/M] [/O] [/P] [/F:file] [/C:string] [/G:file] [/D:dirlist] [/A:color] [/OFF[LINE]] strings [[drive:][path]filename[ ...]]
FC [/A] [/C] [/L] [/LBn] [/N] [/OFF[LINE]] [/T] [/U] [/W] [/nnnn] file1 file2      FC /B file1 file2
COMP [data1] [data2] [/D] [/A] [/L] [/N=number] [/C] [/OFF[LINE]] [/M]
CLIP  (command | clip   or   clip < file)
```

**Switches**

- SORT: `/R` reverse · `/+n` start comparing at column n · `/O file` output file · `/M kb` memory · `/L locale` · `/T dir` temp folder.
- FIND: `/V` lines NOT containing · `/C` count only · `/N` line numbers · `/I` ignore case. The string must be in double quotes.
- FINDSTR: `/B` match at line start · `/E` match at line end · `/L` literal · `/R` regex (the default for space-separated words) · `/S` search subfolders · `/I` ignore case · `/X` whole-line match · `/V` non-matching lines · `/N` line numbers · `/M` file names only · `/O` byte offsets · `/P` skip non-printable files · `/C:"text"` the exact phrase (spaces included) · `/G:file` patterns from a file · `/F:file` file list from a file · `/D:dirs` folders to search · `/A:attr` colors.
- FINDSTR regex: `.` any character · `*` zero or more of the previous · `^` line start · `$` line end · `[class]` · `[^class]` · `[x-y]` range · `\<` word start · `\>` word end · `\x` escape. There is no `+`, `?`, `{n}` or `|`.
- FC: `/B` binary · `/C` ignore case · `/W` ignore whitespace · `/N` line numbers · `/L` text (ASCII) · `/U` Unicode · `/T` keep tabs.

**Example**

```bat
findstr /s /i /n /c:"connection string" *.config *.json
type app.log | find /c "ERROR"
dir /b | sort /r > list.txt
fc /w old.txt new.txt
```

**Check**

- `findstr "a b"` matches lines with `a` OR `b`. Use `/c:"a b"` for the phrase.
- FINDSTR regex is limited: there is no `\d`, `+` or alternation `|`.

### WHERE, TREE, FORFILES (CMD commands)

**Syntax**

```bat
WHERE [/R dir] [/Q] [/F] [/T] pattern...            :: find files on PATH (or under /R dir)
TREE [drive:][path] [/F] [/A]
FORFILES [/P pathname] [/M searchmask] [/S] [/C command] [/D [+ | -] {yyyy-MM-dd | dd}]
```

**Switches**

- WHERE: `/R dir` search recursively from dir · `/Q` quiet, exit code only · `/F` quote the names · `/T` show size and date. A pattern like `$env:pattern` or `path:pattern` searches that list.
- TREE: `/F` also list files · `/A` use ASCII lines.
- FORFILES: `/P` the start folder (default `.`) · `/M` the mask (default `*`) · `/S` recurse · `/C "cmd /c ..."` the command to run for each file (default `cmd /c echo @file`) · `/D -30` files modified 30 or more days ago (`+` means on or after a date). Variables in `/C`: `@file`, `@fname`, `@ext`, `@path`, `@relpath`, `@isdir`, `@fsize`, `@fdate`, `@ftime`. Write quotes inside the command as `\"` or `0x22`.

**Example**

```bat
where /r C:\Tools *.exe
forfiles /p "D:\Logs" /s /m *.log /d -30 /c "cmd /c echo @path"
```

**Check**

- Do a dry run of `forfiles` with `echo`, and show the list, before using `del`.

### MKLINK, SUBST, PUSHD, POPD, CD/CHDIR (CMD built-ins)

**Syntax**

```bat
MKLINK [[/D] | [/H] | [/J]] Link Target    :: /D directory symlink, /H hard link, /J junction; default = file symlink
SUBST [drive1: [drive2:]path]   SUBST drive1: /D
CD [/D] [drive:][path]   CD ..   CD \   CD           :: /D also changes the drive
PUSHD path               POPD                        :: push / pop the directory (PUSHD maps UNC paths to a drive)
```

**Check**

- `cd D:\folder` from `C:` does not switch drives. Use `cd /d D:\folder` or `pushd`.
- Symbolic links need an admin shell or Developer Mode. Junctions (`/J`) do not.

## System and processes

### START (CMD built-in)

**Syntax**

```bat
START ["title"] [/D path] [/I] [/MIN] [/MAX] [/SEPARATE | /SHARED] [/LOW | /NORMAL | /HIGH | /REALTIME | /ABOVENORMAL | /BELOWNORMAL] [/NODE node] [/AFFINITY mask] [/WAIT] [/B] [command/program] [parameters]
```

**Switches**

- `"title"`: the window title. The first quoted argument is always taken as the title.
- `/D path`: the starting folder.
- `/I`: use the original environment.
- `/MIN`, `/MAX`: window state.
- `/WAIT`: wait for the program to exit.
- `/B`: no new window.
- The priority class: `/LOW`, `/NORMAL`, `/HIGH`, `/REALTIME`, `/ABOVENORMAL` or `/BELOWNORMAL`.
- `/NODE`, `/AFFINITY`: NUMA node and CPU mask.
- A document or URL opens with its associated program.

**Example**

```bat
start "" "C:\Program Files\App\app.exe" --flag
start "" /wait msiexec /i setup.msi /qn
start "" "%~dp0report.html"
```

**Check**

- A quoted program path in the first position is taken as the window title. Always pass an empty title first: `start "" "C:\path\app.exe"`.

### TASKLIST and TASKKILL (CMD commands)

**Syntax**

```bat
TASKLIST [/S system [/U user [/P [pwd]]]] [/M [module] | /SVC | /V] [/FI filter] [/FO {TABLE | LIST | CSV}] [/NH] [/APPS]
TASKKILL [/S system [/U user [/P [pwd]]]] {[/FI filter] [/PID processid | /IM imagename]} [/T] [/F]
```

**Switches**

- `/S`, `/U`, `/P`: a remote system and its credentials.
- `/M [module]`: processes using a DLL.
- `/SVC`: services in each process.
- `/V`: verbose.
- `/FI "filter"`: for example `"IMAGENAME eq notepad.exe"`, `"PID gt 1000"`, `"STATUS eq NOT RESPONDING"`, `"MEMUSAGE gt 100000"`, `"USERNAME eq x"`, `"WINDOWTITLE eq x*"`. The operators are `eq ne gt lt ge le`.
- `/FO`: the output format.
- `/NH`: no header.
- `TASKKILL /PID n` or `/IM name.exe`: the process to end (wildcard `*` allowed with `/IM`).
- `/T`: end the child processes too.
- `/F`: force.

**Example**

```bat
tasklist /fi "imagename eq node.exe" /fo csv /nh
taskkill /im notepad.exe /t
```

**Check**

- `/F` kills without saving. Prefer to close normally first, and confirm the process with the user.

### SC (service control) and NET (CMD commands)

**Syntax**

```bat
SC [\\server] query [service] [type= service|driver|all] [state= active|inactive|all]
SC [\\server] queryex | qc | start | stop | pause | continue | delete | description | failure | sdshow  <service> [args]
SC [\\server] config <service> [start= boot|system|auto|demand|disabled|delayed-auto] [binPath= "path"] [DisplayName= "name"] [obj= account] [password= pwd] [depend= svc1/svc2]
SC [\\server] create <service> binPath= "C:\path\svc.exe" [start= auto] [DisplayName= "Name"] [type= own|share|kernel]
NET START [service]      NET STOP service     NET USER [user [password | *] [/ADD | /DELETE] [/DOMAIN]]     NET LOCALGROUP group [user /ADD | /DELETE]
NET USE [devicename: | *] [\\computer\share [password | *]] [/USER:[domain\]user] [/PERSISTENT:{YES | NO}] [/DELETE]
NET SHARE [name[=drive:path] [/GRANT:user,{READ | CHANGE | FULL}] | name /DELETE]   NET VIEW [\\computer]   NET SESSION   NET TIME \\computer
```

**Check**

- SC options need a space AFTER the `=` and none before it: `start= auto`, not `start=auto` or `start = auto`.
- Most `SC config/create/delete` and `NET USER /ADD` commands need an elevated (admin) prompt.

### SCHTASKS (CMD command)

**Syntax**

```bat
SCHTASKS /Create [/S system [/U user [/P pwd]]] [/RU user [/RP pwd]] /SC schedule [/MO modifier] [/D day] [/M months] [/I idletime] /TN taskname /TR taskrun [/ST HH:mm] [/RI interval] [/ET HH:mm | /DU duration] [/K] [/SD date] [/ED date] [/IT] [/NP] [/Z] [/V1] [/F] [/RL {LIMITED | HIGHEST}] [/DELAY mmmm:ss]
SCHTASKS /Create /XML file.xml /TN taskname
SCHTASKS /Query [/TN taskname] [/FO {TABLE | LIST | CSV}] [/NH] [/V] [/XML]
SCHTASKS /Run /TN taskname     SCHTASKS /End /TN taskname     SCHTASKS /Delete /TN taskname [/F]
SCHTASKS /Change /TN taskname [/TR taskrun] [/ST HH:mm] [/ENABLE | /DISABLE] [/RU user]
```

**Arguments**

- `/SC`: `MINUTE`, `HOURLY`, `DAILY`, `WEEKLY`, `MONTHLY`, `ONCE`, `ONSTART`, `ONLOGON`, `ONIDLE` or `ONEVENT`.
- `/MO`: the repeat interval (every n minutes, hours, days or weeks), or `FIRST`/`LAST`… for monthly.
- `/D`: days, `MON`-`SUN` or `1`-`31`.
- `/TN`: the task name (a folder path is allowed: `"\MyTasks\Backup"`).
- `/TR`: the command to run. Quote the inner paths with `\"`.
- `/ST`: the start time. `/SD`, `/ED`: start and end dates.
- `/RU`: the run-as user (`SYSTEM` is allowed).
- `/RL HIGHEST`: run elevated.
- `/F`: overwrite or delete without asking.

**Example**

```bat
schtasks /create /tn "Nightly Backup" /tr "\"C:\Scripts\backup.bat\"" /sc daily /st 02:00 /rl highest /f
schtasks /query /tn "Nightly Backup" /v /fo list
```

### REG (CMD command)

**Syntax**

```bat
REG QUERY KeyName [/v ValueName | /ve] [/s] [/f Data [/k] [/d] [/c] [/e]] [/t Type] [/z] [/se Separator] [/reg:32 | /reg:64]
REG ADD KeyName [/v ValueName | /ve] [/t Type] [/s Separator] [/d Data] [/f] [/reg:32 | /reg:64]
REG DELETE KeyName [/v ValueName | /ve | /va] [/f]
REG COPY KeyName1 KeyName2 [/s] [/f]      REG EXPORT KeyName FileName.reg [/y]      REG IMPORT FileName.reg
REG SAVE KeyName FileName.hiv [/y]        REG RESTORE KeyName FileName.hiv          REG LOAD KeyName FileName   REG UNLOAD KeyName
REG COMPARE KeyName1 KeyName2 [/v ValueName | /ve] [/s] [/oa | /od | /os | /on]
```

**Arguments**

- `KeyName`: the root plus a path. The roots are `HKLM`, `HKCU`, `HKCR`, `HKU` and `HKCC`, written as `HKCU\Software\MyApp`, or `\\machine\HKLM\...` remotely.
- `/v`: the value name. `/ve`: the default (unnamed) value. `/va`: all values.
- `/t`: the type, `REG_SZ`, `REG_EXPAND_SZ`, `REG_MULTI_SZ`, `REG_DWORD`, `REG_QWORD`, `REG_BINARY` or `REG_NONE`.
- `/d`: the data.
- `/s`: recurse (QUERY), or the separator for `REG_MULTI_SZ` (ADD).
- `/f`: force, with no prompt.
- `/reg:32`, `/reg:64`: the registry view.

**Example**

```bat
reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Run"
reg add "HKCU\Software\MyApp" /v LogLevel /t REG_DWORD /d 2 /f
```

**Check**

- Quote key paths that contain spaces. Write `HKLM` changes only after showing them to the user, because they need admin rights and can break the system.

### Environment: SETX, PATH (CMD commands)

**Syntax**

```bat
SETX var "value" [/M]                 :: permanent; /M = machine-wide (admin); max 1024 chars
SETX var /K HKLM\path\value [/M]      :: from the registry
PATH [[drive:]path[;...][;%PATH%]]    PATH ;   (clear)
```

**Check**

- `SETX` changes only new windows. The current window keeps its old value, so also run `set` when you need the value now.
- `setx PATH "%PATH%;C:\new"` merges the user and machine PATH into the user PATH, and truncates it at 1024 characters. Warn the user, and prefer the Environment Variables dialog or PowerShell.

### Network: IPCONFIG, PING, TRACERT, NSLOOKUP, NETSTAT, CURL (CMD commands)

**Syntax**

```bat
IPCONFIG [/all | /release [adapter] | /renew [adapter] | /flushdns | /displaydns | /registerdns]
PING [-t] [-a] [-n count] [-l size] [-f] [-i TTL] [-w timeout] [-4 | -6] target
TRACERT [-d] [-h maxhops] [-w timeout] [-4 | -6] target
NSLOOKUP [-type=A|AAAA|MX|TXT|NS|CNAME|SOA|PTR] name [server]
NETSTAT [-a] [-b] [-e] [-f] [-n] [-o] [-p proto] [-r] [-s] [-t] [interval]
CURL [options] URL      (curl.exe ships with Windows 10 1803+)
```

**Switches**

- PING: `-t` until stopped · `-n count` (default 4) · `-l size` buffer size · `-w ms` timeout · `-a` resolve names · `-4`/`-6`.
- NETSTAT: `-a` all connections · `-n` numeric · `-o` owning PID · `-b` executable name (admin) · `-p tcp|udp` · `-r` routes · `-s` statistics · `interval` repeat every n seconds.
- CURL: `-o file` · `-O` remote file name · `-L` follow redirects · `-I` headers only · `-X POST` · `-H "Header: v"` · `-d data` · `--data-binary @file` · `-u user:pass` · `-k` skip TLS verification · `-s` silent · `-f` fail on HTTP errors · `--retry n`.

**Example**

```bat
ping -n 1 -w 1000 server01 >nul && echo up || echo down
netstat -ano | findstr :8080
```

### System info and power: VER, HOSTNAME, WHOAMI, SYSTEMINFO, DRIVERQUERY, SHUTDOWN, CHKDSK, SFC (CMD commands)

**Syntax**

```bat
VER   HOSTNAME   WHOAMI [/USER | /GROUPS | /PRIV | /ALL] [/FO TABLE|LIST|CSV]   SYSTEMINFO [/S system] [/FO format]
DRIVERQUERY [/V] [/SI] [/FO format]   VOL [drive:]   DATE [/T]   TIME [/T]
SHUTDOWN [/s | /r | /g | /h | /l | /a | /p] [/t seconds] [/f] [/m \\computer] [/c "comment"] [/d [p|u:]xx:yy] [/o] [/fw]
CHKDSK [volume] [/F] [/R] [/X] [/scan] [/spotfix]      SFC /SCANNOW | /VERIFYONLY | /SCANFILE=file
```

**Switches**

- SHUTDOWN: `/s` shut down · `/r` restart · `/g` restart and reopen apps · `/h` hibernate · `/l` log off · `/p` power off now · `/a` abort a pending shutdown · `/t n` delay in seconds · `/f` force-close apps · `/o` open advanced boot options (with `/r`) · `/m \\pc` a remote computer · `/c` comment.
- CHKDSK: `/F` fix errors · `/R` find bad sectors (implies `/F`) · `/X` dismount first · `/scan` online scan.
- `DATE` and `TIME` without `/T` ask to SET the clock. Always use `/T` to only display it.

**Check**

- `shutdown`, `chkdsk /f` and `sfc` affect the whole machine. Give them to the user to run. Do not slip them into scripts unless the user asked.

### Permissions: ICACLS and TAKEOWN (CMD commands)

**Syntax**

```bat
ICACLS name [/grant[:r] sid:perm[...]] [/deny sid:perm] [/remove[:g | :d] sid] [/reset] [/inheritance:e|d|r] [/setowner user] [/T] [/C] [/L] [/Q]
ICACLS name /save aclfile [/T]      ICACLS directory /restore aclfile
TAKEOWN /F filename [/A] [/R [/D prompt]]
```

**Arguments**

- `perm`: `F` full, `M` modify, `RX` read and execute, `R` read, `W` write, `D` delete. The inheritance flags are `(OI)` object inherit, `(CI)` container inherit, `(IO)` inherit only and `(NP)` no propagation.
- `/grant:r`: replace the user's existing grants.
- `/T`: recurse.
- `/C`: continue after errors.
- `/inheritance:d`: disable inheritance and copy the inherited ACEs. `:r` removes them, and `:e` enables inheritance.
- TAKEOWN `/A`: give ownership to Administrators. `/R`: recurse.

**Example**

```bat
icacls "D:\Shared" /grant "Users:(OI)(CI)RX" /T
```

### Archives and hashing: TAR, EXPAND, COMPACT, CERTUTIL (CMD commands)

**Syntax**

```bat
TAR -c [-z] -f archive.tar[.gz] [-C dir] files...    TAR -x -f archive.zip [-C destdir]    TAR -t -f archive   (bsdtar: also reads .zip)
EXPAND source.cab -F:* destination
COMPACT [/C | /U] [/S[:dir]] [/A] [/I] [/F] [/Q] [/EXE[:XPRESS4K|XPRESS8K|XPRESS16K|LZX]] [filename]
CERTUTIL -hashfile file [MD5 | SHA1 | SHA256 | SHA384 | SHA512]
CERTUTIL -encode infile outfile.b64      CERTUTIL -decode infile.b64 outfile
```

**Check**

- `tar` on Windows can extract `.zip` files, but `tar -c` creates tar archives, not zip files.

### Code page and console: CHCP, MODE, PROMPT (CMD commands)

**Syntax**

```bat
CHCP [nnn]          :: 65001 = UTF-8, 437 = US OEM, 1252 = Western ANSI
MODE CON: COLS=120 LINES=40      MODE COMx[:] [BAUD=b] [PARITY=p] [DATA=d] [STOP=s]
PROMPT [text]       :: $P path  $G >  $D date  $T time  $N drive  $_ newline  $$ $
```
