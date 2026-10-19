## Essentials

Target: VBScript 5.8 under Windows Script Host (`cscript //nologo file.vbs`). Not VBA, not VB.NET.

| Never in VBScript | Write instead |
|---|---|
| `Dim x As String`, `Function F(a As Long) As Long` | `Dim x`, `Function F(a)` (everything is a Variant) |
| `x += 1`, `a && b`, `a \|\| b` | `x = x + 1`, `a And b`, `a Or b` (no short circuit: nest the Ifs) |
| `Return value` | `F = value` (assign to the function name), then `Exit Function` |
| `Continue For`, `try / catch`, `On Error GoTo Handler` | `If ... Then` around the body; `On Error Resume Next` ... `If Err.Number <> 0 Then` ... `On Error GoTo 0` |
| `MySub(a, b)` | `MySub a, b` or `Call MySub(a, b)` |
| `obj = CreateObject("...")` | `Set obj = CreateObject("...")`, later `Set obj = Nothing` |
| `"a" + 1`, `'single quotes'` | `"a" & 1`; strings use "double quotes" ("" inside); `'` starts a comment |
| `Debug.Print`, `MsgBox` in cscript scripts | `WScript.Echo` |

Always: `Option Explicit` first and `Dim` every variable. `Dim a(5)` has 6 elements (0 to 5); `ReDim Preserve` to grow. Booleans compare with `= True` only when needed; `If x Then`. Check `Err.Number` right after each risky call under `On Error Resume Next`.

## Recipes

### Read a text file line by line (FileSystemObject, OpenTextFile, AtEndOfStream)

**Example**

```vbscript
Option Explicit
Dim fso, ts, line, count
Set fso = CreateObject("Scripting.FileSystemObject")
Set ts = fso.OpenTextFile("C:\Logs\app.log", 1)   ' 1 = ForReading
count = 0
Do Until ts.AtEndOfStream
    line = ts.ReadLine
    If InStr(1, line, "ERROR", vbTextCompare) > 0 Then count = count + 1
Loop
ts.Close
WScript.Echo count & " error lines"
```

### Write or append to a text file (CreateTextFile, OpenTextFile ForAppending)

**Example**

```vbscript
Option Explicit
Dim fso, ts
Set fso = CreateObject("Scripting.FileSystemObject")
Set ts = fso.OpenTextFile("C:\Temp\out.txt", 8, True)   ' 8 = ForAppending, True = create if missing
ts.WriteLine Now & " started"
ts.Close
Set ts = Nothing
Set fso = Nothing
```

### Loop over files in a folder and subfolders (Folder.Files, SubFolders, recursion)

**Example**

```vbscript
Option Explicit
Dim fso
Set fso = CreateObject("Scripting.FileSystemObject")

Sub Walk(folder)
    Dim f, sub1
    For Each f In folder.Files
        If LCase(fso.GetExtensionName(f.Name)) = "log" And f.Size > 1048576 Then WScript.Echo f.Path & vbTab & f.Size
    Next
    For Each sub1 In folder.SubFolders
        Walk sub1
    Next
End Sub

Walk fso.GetFolder("C:\Logs")
```

### Handle errors (On Error Resume Next, Err.Number, Err.Description)

**Example**

```vbscript
Option Explicit
Dim fso
Set fso = CreateObject("Scripting.FileSystemObject")
On Error Resume Next
fso.CopyFile "C:\Data\report.csv", "D:\Backup\", True
If Err.Number <> 0 Then
    WScript.Echo "Copy failed: " & Err.Description
    Err.Clear
Else
    fso.DeleteFile "C:\Data\report.csv"
End If
On Error GoTo 0
```

### Functions, return values and arrays (Function, Split, UBound, Join)

**Example**

```vbscript
Option Explicit

Function MaxOf(values)
    Dim i, m
    m = values(0)
    For i = 1 To UBound(values)
        If values(i) > m Then m = values(i)
    Next
    MaxOf = m
End Function

Dim parts
parts = Split("4,9,2", ",")
WScript.Echo MaxOf(Array(CLng(parts(0)), CLng(parts(1)), CLng(parts(2))))
WScript.Echo Join(parts, " | ")
```

### Command-line arguments and exit codes (WScript.Arguments, WScript.Quit)

**Example**

```vbscript
Option Explicit
If WScript.Arguments.Count < 1 Then
    WScript.Echo "Usage: cscript //nologo tool.vbs <path>"
    WScript.Quit 2
End If
Dim path
path = WScript.Arguments(0)
WScript.Echo "Path: " & path
WScript.Quit 0
```

### Dictionary of counts (Scripting.Dictionary, Exists, Keys)

**Example**

```vbscript
Option Explicit
Dim d, w, k
Set d = CreateObject("Scripting.Dictionary")
d.CompareMode = vbTextCompare
For Each w In Split("a b A c b", " ")
    If d.Exists(w) Then d(w) = d(w) + 1 Else d.Add w, 1
Next
For Each k In d.Keys
    WScript.Echo k & " = " & d(k)
Next
```
