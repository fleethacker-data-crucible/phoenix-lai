# VBScript syntax reference (Phoenix)

For Phoenix models writing and checking VBScript 5.8 (`.vbs` files run by Windows Script Host, `cscript.exe` and `wscript.exe`, and classic ASP). Each `###` entry is one statement, function or object: what it does, its exact syntax, every argument, an example, and the mistakes to check for. VBScript is deprecated by Microsoft (a Windows feature on demand, to be removed in future Windows). Suggest PowerShell for new automation, and VBScript only when the user needs it.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply. VBScript is case-insensitive. `'` or `Rem` starts a comment, a line continues with ` _`, and `:` separates statements on one line.

## Checklist: before you answer with VBScript

- VBScript has ONE data type, Variant. `Dim x As String` is a syntax error (that is VBA): write `Dim x`. There are no typed parameters, and no `As` anywhere.
- Start the file with `Option Explicit`, and `Dim` every variable.
- Objects need `Set`: `Set fso = CreateObject("Scripting.FileSystemObject")`. Release them with `Set fso = Nothing`.
- In VBScript, `WScript` (the `WScript.Echo`, `WScript.Arguments` and `WScript.Quit` object) exists only under Windows Script Host. It does not exist in ASP or HTA.
- Calling a Sub with parentheses and two or more arguments is a syntax error: write `MySub a, b` or `Call MySub(a, b)`. A Function that returns a value uses parentheses: `x = MyFunc(a, b)`.
- There is no `Continue`, no `Return`, no `+=`, no `&&` / `||`, and no short-circuit `And`/`Or`. To return a value, assign it to the function name.
- `On Error Resume Next` is the only error handling, besides `On Error GoTo 0`. There is no `On Error GoTo label` and no try/catch. Check `Err.Number` after each risky call.
- Arrays: `Dim a(5)` has 6 elements (0 to 5). Dynamic arrays use `ReDim [Preserve]`. `Array(...)` and `Split(...)` are 0-based.
- Strings use "double quotes", with `""` for a quote inside. Concatenate with `&`. Line breaks are `vbCrLf`.
- `cscript //nologo script.vbs` runs the script in the console (output goes to stdout). `wscript` shows every `Echo` as a message box.
- Never write a script that deletes, kills processes or changes the registry without showing the user exactly what it targets.

## Language

### Variables, constants and arrays (VBScript statements)

**Syntax**

```vbscript
Option Explicit
Dim name, count, items(9)              ' items: 10 elements, 0..9
Dim grid(2, 3)                         ' 3 x 4
Dim dyn()  :  ReDim dyn(n)  :  ReDim Preserve dyn(n + 10)
Const MAX_ITEMS = 100, APP_NAME = "Tool"
Private x   ' / Public x   (script or class level)
count = 0
Set obj = CreateObject("Scripting.Dictionary")
UBound(arr [, dim])   LBound(arr)   Erase arr   IsArray(x)
```

**Check**

- `ReDim Preserve` can resize only the last dimension.
- `Const` values must be literals. A function call is not allowed.

### Operators (VBScript syntax)

**Syntax**

```vbscript
+  -  *  /  \ (integer division)  Mod  ^  &  (concatenation)
=  <>  <  >  <=  >=  Is (object identity)
And  Or  Not  Xor  Eqv  Imp           ' no short-circuit
```

**Check**

- `"5" + 3` gives 8, while `"5" & 3` gives "53". Use `&` for text.
- `=` is both assignment and comparison, depending on where it appears.

### If, Select Case (VBScript statements)

**Syntax**

```vbscript
If <condition> Then <statement> [Else <statement>]
If <condition> Then
    <statements>
ElseIf <condition> Then
    <statements>
Else
    <statements>
End If

Select Case <expression>
    Case <value1>, <value2>
        <statements>
    Case Else
        <statements>
End Select
```

**Check**

- VBScript `Case` accepts only lists of values. `Case Is > 5` and `Case 1 To 5` (the VBA forms) are syntax errors in VBScript. Use `Select Case True` with `Case x > 5`.

### Loops: For, For Each, Do, While (VBScript statements)

**Syntax**

```vbscript
For i = 1 To 10 [Step 2]
    <statements>   [Exit For]
Next
For Each item In collection
    <statements>
Next
Do [While | Until <condition>]
    <statements>   [Exit Do]
Loop
Do
    <statements>
Loop [While | Until <condition>]
While <condition>
    <statements>
Wend
```

**Check**

- Write `Next` without the variable name. `Next i` is allowed, but it is easy to get wrong in nested loops.
- There is no `Continue`. Wrap the body in an `If`.

### Sub and Function procedures (VBScript statements)

**Syntax**

```vbscript
[Public | Private] Sub <Name>([ByVal | ByRef] arg1, ...)
    <statements>   [Exit Sub]
End Sub

[Public | Private] Function <Name>([ByVal | ByRef] arg1, ...)
    <statements>
    <Name> = <return value>        ' Set <Name> = obj  for objects
    [Exit Function]
End Function

MySub a, b          ' call a Sub (no parentheses)
Call MySub(a, b)    ' or with Call
x = MyFunc(a, b)
```

**Check**

- Arguments are `ByRef` by default. There are no `Optional` parameters and no `ParamArray`: pass an array or `Null` instead.
- To return an object, write `Set MyFunc = obj`. Without `Set`, the function returns the object's default property, or fails.

### Classes (VBScript statement)

**Syntax**

```vbscript
Class Person
    Private m_name
    Public Property Get Name : Name = m_name : End Property
    Public Property Let Name(v) : m_name = v : End Property
    Public Function Greet() : Greet = "Hi " & m_name : End Function
    Private Sub Class_Initialize() : m_name = "" : End Sub
    Private Sub Class_Terminate() : End Sub
End Class
Dim p : Set p = New Person : p.Name = "Ann" : WScript.Echo p.Greet()
```

**Check**

- There is no inheritance and no `Implements`. `Property Set` is used for object values.

### Error handling: On Error, Err (VBScript statements)

**Syntax**

```vbscript
On Error Resume Next
<risky statement>
If Err.Number <> 0 Then
    WScript.Echo "Error " & Err.Number & ": " & Err.Description & " (" & Err.Source & ")"
    Err.Clear
End If
On Error GoTo 0           ' errors stop the script again
Err.Raise number [, source [, description]]
```

**Check**

- `On Error Resume Next` stays in effect for the rest of the procedure, and silently hides typos and later errors. Turn it off with `On Error GoTo 0` right after the risky statement.

### Built-in functions (VBScript functions)

**Syntax**

```vbscript
' strings (positions 1-based)
Len(s)  Left(s, n)  Right(s, n)  Mid(s, start [, n])  InStr([start,] s, find [, vbTextCompare])  InStrRev(s, find)  UCase(s)  LCase(s)  Trim(s)  LTrim  RTrim
Replace(s, find, repl [, start [, count [, compare]]])  Split(s [, delim [, count [, compare]]])  Join(arr [, delim])  StrComp(a, b [, compare])  StrReverse(s)  Space(n)  String(n, c)
Asc(c)  Chr(n)  AscW(c)  ChrW(n)  FormatNumber(n [, decimals])  FormatCurrency(n)  FormatPercent(n)  FormatDateTime(d [, vbShortDate | vbLongDate | vbShortTime | vbLongTime])
' conversion and checks
CStr  CInt  CLng  CDbl  CSng  CBool  CDate  CByte  CCur  Int(x)  Fix(x)  Round(x [, d])  Abs  Sgn  Sqr  Exp  Log  Rnd  Randomize  Hex(n)  Oct(n)
IsNumeric(x)  IsDate(x)  IsEmpty(x)  IsNull(x)  IsObject(x)  IsArray(x)  TypeName(x)  VarType(x)
' dates
Now  Date  Time  Timer  Year(d)  Month(d)  Day(d)  Hour(t)  Minute(t)  Second(t)  Weekday(d)  WeekdayName(n)  MonthName(n)
DateAdd("d" | "m" | "yyyy" | "h" | "n" | "s" | "ww" | "q", n, d)  DateDiff(interval, d1, d2)  DatePart(interval, d)  DateSerial(y, m, d)  TimeSerial(h, n, s)  DateValue(s)  TimeValue(s)
' misc
MsgBox(prompt [, buttons [, title]])  InputBox(prompt [, title [, default]])  CreateObject("ProgID")  GetObject([path] [, ProgID])  Eval(expr)  Execute stmt  ExecuteGlobal stmt
Escape(s)  Unescape(s)  GetRef("FuncName")  ScriptEngine  ScriptEngineMajorVersion
vbCrLf  vbCr  vbLf  vbTab  vbNullString  vbTextCompare  vbBinaryCompare  vbOKCancel  vbYesNo  vbYes  vbNo  vbInformation  vbExclamation  vbCritical  vbQuestion
```

**Check**

- In `DateAdd` and `DateDiff`, `"m"` means month and `"n"` means minute.
- There is no `Format()` function in VBScript. Use the `FormatNumber` and `FormatDateTime` family, or build the string by hand: `Right("0" & Month(d), 2)`.

## Windows Script Host objects

### WScript object (Windows Script Host)

**Syntax**

```vbscript
WScript.Echo "text" [, more]            ' console line (cscript) or message box (wscript)
WScript.StdOut.WriteLine "text"         ' cscript only;  WScript.StdErr.WriteLine;  WScript.StdIn.ReadLine
WScript.Arguments.Count   WScript.Arguments(0)   WScript.Arguments.Named("out")   WScript.Arguments.Unnamed(0)
WScript.ScriptFullName   WScript.ScriptName   WScript.FullName (host exe)   WScript.Version
WScript.Sleep 1000                      ' milliseconds
WScript.Quit [exitCode]
CreateObject("WScript.Shell")   CreateObject("WScript.Network")
```

**Example**

```vbscript
Option Explicit
If WScript.Arguments.Count < 1 Then
    WScript.Echo "Usage: cscript //nologo tool.vbs <folder>"
    WScript.Quit 1
End If
Dim folder : folder = WScript.Arguments(0)
```

**Check**

- `WScript.StdOut` exists only under `cscript`. Under `wscript`, it raises "The handle is invalid".

### FileSystemObject (Scripting Runtime)

**Syntax**

```vbscript
Set fso = CreateObject("Scripting.FileSystemObject")
fso.FileExists(p)  fso.FolderExists(p)  fso.DriveExists(d)  fso.GetFile(p)  fso.GetFolder(p)  fso.GetDrive(d)
fso.CreateFolder(p)  fso.DeleteFile p [, force]  fso.DeleteFolder p [, force]  fso.CopyFile src, dst [, overwrite]  fso.MoveFile src, dst  fso.CopyFolder src, dst [, overwrite]
fso.BuildPath(a, b)  fso.GetParentFolderName(p)  fso.GetFileName(p)  fso.GetBaseName(p)  fso.GetExtensionName(p)  fso.GetAbsolutePathName(p)  fso.GetTempName  fso.GetSpecialFolder(0 | 1 | 2)
Set ts = fso.OpenTextFile(path, iomode [, create [, format]])   ' iomode: 1 ForReading, 2 ForWriting, 8 ForAppending; format: 0 ASCII, -1 Unicode, -2 default
Set ts = fso.CreateTextFile(path [, overwrite [, unicode]])
ts.ReadLine  ts.ReadAll  ts.Read(n)  ts.AtEndOfStream  ts.WriteLine s  ts.Write s  ts.WriteBlankLines n  ts.SkipLine  ts.Close
f.Name  f.Path  f.Size  f.DateLastModified  f.DateCreated  f.Attributes  f.Type  f.Copy dst  f.Move dst  f.Delete
folder.Files  folder.SubFolders  folder.Name  folder.Size  folder.ParentFolder
```

**Example**

```vbscript
Const ForReading = 1
Dim fso, ts, line, n
Set fso = CreateObject("Scripting.FileSystemObject")
Set ts = fso.OpenTextFile("C:\Logs\app.log", ForReading)
Do Until ts.AtEndOfStream
    line = ts.ReadLine
    If InStr(1, line, "ERROR", vbTextCompare) > 0 Then n = n + 1
Loop
ts.Close
WScript.Echo n & " errors"
```

**Check**

- VBScript does not predefine the constants `ForReading`, `ForWriting` and `ForAppending`. Declare them with `Const`, or pass 1, 2 or 8.
- FileSystemObject cannot read UTF-8 correctly (only ANSI or UTF-16). Use `ADODB.Stream` with `Charset = "utf-8"` for UTF-8 files.
- `DeleteFile` and `DeleteFolder` bypass the Recycle Bin.

### WScript.Shell (Windows Script Host)

**Syntax**

```vbscript
Set sh = CreateObject("WScript.Shell")
sh.Run command [, windowStyle (0 hidden, 1 normal, 7 minimized) [, waitOnReturn]] → exit code (when waiting)
Set ex = sh.Exec(command) : ex.StdOut.ReadAll : ex.Status : ex.ExitCode      ' captures output (console window flashes)
sh.ExpandEnvironmentStrings("%TEMP%")   sh.Environment("PROCESS" | "USER" | "SYSTEM")("PATH")
sh.RegRead("HKCU\Software\X\Value")   sh.RegWrite "HKCU\Software\X\Value", data [, "REG_SZ" | "REG_DWORD" | "REG_EXPAND_SZ" | "REG_BINARY"]   sh.RegDelete "HKCU\Software\X\"
sh.SpecialFolders("Desktop" | "MyDocuments" | "AppData" | "Startup" | "Programs")   sh.CurrentDirectory
Set lnk = sh.CreateShortcut(path & ".lnk") : lnk.TargetPath = "..." : lnk.Arguments = "..." : lnk.WorkingDirectory = "..." : lnk.Save
sh.Popup(text [, secondsToWait [, title [, type]]])   sh.SendKeys "{ENTER}"   sh.AppActivate "Title"   sh.LogEvent 0, "message"
```

**Check**

- `sh.Run` needs quotes around paths with spaces, written as doubled quotes inside the string: `sh.Run """C:\Program Files\App\app.exe"" /x", 1, True`.
- In `RegRead` and `RegDelete`, a trailing backslash means a KEY, while no trailing backslash means a VALUE.

### Dictionary, ADODB.Stream, XMLHTTP, WMI (COM objects from VBScript)

**Syntax**

```vbscript
Set d = CreateObject("Scripting.Dictionary") : d.CompareMode = vbTextCompare
d.Add key, item   d(key) = item   d.Exists(key)   d.Item(key)   d.Keys   d.Items   d.Count   d.Remove key   d.RemoveAll
Set st = CreateObject("ADODB.Stream") : st.Type = 2 : st.Charset = "utf-8" : st.Open : st.LoadFromFile p : text = st.ReadText : st.Close
st.WriteText text : st.SaveToFile p, 2 (adSaveCreateOverWrite)
Set http = CreateObject("MSXML2.ServerXMLHTTP.6.0") : http.Open "GET", url, False : http.setRequestHeader "Accept", "application/json" : http.Send : http.Status : http.responseText
Set wmi = GetObject("winmgmts:\\.\root\cimv2")
For Each p In wmi.ExecQuery("SELECT Name, ProcessId FROM Win32_Process WHERE Name = 'notepad.exe'") : WScript.Echo p.Name, p.ProcessId : Next
Set rx = New RegExp : rx.Pattern = "\d+" : rx.Global = True : rx.IgnoreCase = True
rx.Test(s)  rx.Replace(s, "#")  For Each m In rx.Execute(s) : m.Value  m.FirstIndex  m.SubMatches(0) : Next
```

**Check**

- Reading `d(missingKey)` adds the key with an Empty value. Test `d.Exists(key)` first.
- `RegExp` is VBScript-only syntax (`New RegExp`). In VBA, use `CreateObject("VBScript.RegExp")`.
