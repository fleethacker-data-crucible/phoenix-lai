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
