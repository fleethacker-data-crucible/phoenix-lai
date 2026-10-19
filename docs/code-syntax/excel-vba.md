# Excel VBA syntax reference (Phoenix)

For Phoenix models writing and checking VBA (Visual Basic for Applications 7.1) macros for Microsoft Excel. Each `###` entry is one statement, function, object or method: what it does, its exact syntax, every argument, an example, and the mistakes to check for.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply. VBA is case-insensitive (the editor fixes the casing). Lines continue with ` _` (space and underscore) at the end, and `'` starts a comment.

## Essentials

Target: Excel VBA (VBA 7, Excel 2016 / 365), code in a standard module.

| Never in VBA | Write instead |
|---|---|
| `Dim a, b As Long` (a is a Variant) | `Dim a As Long, b As Long` |
| `x += 1`, `&&`, `\|\|`, `!=` | `x = x + 1`, `And`, `Or`, `<>` |
| `return x` | `FunctionName = x` then `Exit Function` |
| `rng = Range("A1")` for an object | `Set rng = Range("A1")` |
| `Range("A1")` with no sheet in a macro that runs anywhere | `ws.Range("A1")` with `Set ws = ThisWorkbook.Worksheets("Data")` |
| `.Select` / `.Activate` / `Selection` | work on the objects directly |
| `Cells(i, 1).Value` in a loop over 100,000 rows | read the range into an array: `arr = ws.Range("A2:C" & last).Value`, loop the array, write back once |
| last row `Range("A65536").End(xlUp)` | `ws.Cells(ws.Rows.Count, "A").End(xlUp).Row` |
| `try / catch` | `On Error GoTo Handler` ... `Exit Sub` ... `Handler:` |

Always: `Option Explicit`; `Long` not `Integer` for rows; turn off `Application.ScreenUpdating` / `Calculation` for big jobs and restore them in the error handler; delete rows bottom-up (`For i = last To 2 Step -1`).

## Recipes

### Last row, loop rows and delete blanks (End(xlUp), Step -1)

**Example**

```vba
Option Explicit

Sub DeleteBlankRows()
    Dim ws As Worksheet, last As Long, i As Long
    Set ws = ThisWorkbook.Worksheets("Data")
    last = ws.Cells(ws.Rows.Count, "A").End(xlUp).Row
    Application.ScreenUpdating = False
    For i = last To 2 Step -1
        If Len(Trim$(ws.Cells(i, "A").Value)) = 0 Then ws.Rows(i).Delete
    Next i
    Application.ScreenUpdating = True
End Sub
```

### Fast processing with arrays (Range.Value to a Variant array and back)

**Example**

```vba
Sub DoubleColumnB()
    Dim ws As Worksheet, last As Long, arr As Variant, i As Long
    Set ws = ThisWorkbook.Worksheets("Data")
    last = ws.Cells(ws.Rows.Count, "B").End(xlUp).Row
    If last < 2 Then Exit Sub
    arr = ws.Range("B2:B" & last).Value
    For i = 1 To UBound(arr, 1)
        If IsNumeric(arr(i, 1)) Then arr(i, 1) = arr(i, 1) * 2
    Next i
    ws.Range("B2:B" & last).Value = arr
End Sub
```

### Error handling that restores settings (On Error GoTo, Resume)

**Example**

```vba
Sub SafeImport()
    On Error GoTo Handler
    Application.ScreenUpdating = False
    Application.Calculation = xlCalculationManual
    ' work here
CleanUp:
    Application.Calculation = xlCalculationAutomatic
    Application.ScreenUpdating = True
    Exit Sub
Handler:
    MsgBox "Error " & Err.Number & ": " & Err.Description, vbExclamation
    Resume CleanUp
End Sub
```

### Count or sum with a Dictionary (Scripting.Dictionary, late binding)

**Example**

```vba
Sub TotalsPerCustomer()
    Dim ws As Worksheet, d As Object, last As Long, i As Long, k As Variant
    Set ws = ThisWorkbook.Worksheets("Orders")
    Set d = CreateObject("Scripting.Dictionary")
    last = ws.Cells(ws.Rows.Count, "A").End(xlUp).Row
    For i = 2 To last
        d(ws.Cells(i, "A").Value) = d(ws.Cells(i, "A").Value) + ws.Cells(i, "B").Value
    Next i
    i = 2
    For Each k In d.Keys
        ws.Cells(i, "E").Value = k
        ws.Cells(i, "F").Value = d(k)
        i = i + 1
    Next k
End Sub
```

### Loop over workbooks in a folder (Dir, Workbooks.Open, Close)

**Example**

```vba
Sub CombineFolder()
    Dim path As String, f As String, wb As Workbook, target As Worksheet, nextRow As Long
    path = "C:\Reports\"
    Set target = ThisWorkbook.Worksheets("All")
    f = Dir(path & "*.xlsx")
    Do While Len(f) > 0
        Set wb = Workbooks.Open(path & f, ReadOnly:=True)
        nextRow = target.Cells(target.Rows.Count, "A").End(xlUp).Row + 1
        wb.Worksheets(1).UsedRange.Offset(1).Copy target.Cells(nextRow, "A")
        wb.Close SaveChanges:=False
        f = Dir()
    Loop
End Sub
```

### A user-defined worksheet function (Function, Optional, Application.Volatile off)

**Example**

```vba
Public Function TaxedPrice(price As Double, Optional rate As Double = 0.2) As Double
    TaxedPrice = Round(price * (1 + rate), 2)
End Function
```

## Rules: before you answer with VBA

- Start every module with `Option Explicit`, and declare every variable with `Dim x As Type`. In `Dim a, b As Long`, only `b` is a Long (and `a` is a Variant).
- Object variables are assigned with `Set`: `Set ws = ThisWorkbook.Worksheets("Data")`. Assigning an object without `Set` is error 91 or 438. Values are assigned without `Set`.
- Fully qualify ranges: `ws.Range("A1")`, `ws.Cells(r, c)`. A bare `Range`, `Cells` or `ActiveSheet` depends on whichever sheet is active. Avoid `Select` and `Activate`.
- `Cells(row, column)`: the row comes first, and both are 1-based. Use `Long` (not `Integer`) for row numbers, because Excel has 1,048,576 rows and Integer overflows above 32,767.
- Find the last row with `ws.Cells(ws.Rows.Count, "A").End(xlUp).Row`, not `UsedRange`, and not loops until empty.
- Strings use "double quotes", and a quote inside is doubled (`""`). Concatenate with `&`, not `+`.
- Comparison is `=`, not-equal is `<>`, and the logic operators are `And`, `Or`, `Not`. There are no `==`, `!=`, `&&` or `||`. Note that `And` and `Or` do NOT short-circuit.
- `If … Then` on one line needs no `End If`. The block form needs `End If`. `ElseIf` is one word.
- Arrays from `Range.Value` are 2-D and 1-based: `arr(row, col)`. `Dim a(5)` has 6 elements (0 to 5) unless `Option Base 1` is set.
- Error handling: `On Error GoTo Handler` … `Exit Sub` … `Handler:`. Never leave `On Error Resume Next` on for the whole procedure: reset it with `On Error GoTo 0`.
- Speed: write whole arrays to ranges, and turn `Application.ScreenUpdating`, `Calculation` and `EnableEvents` off during long work. Restore them in the error handler too.
- Saving a workbook with macros needs `.xlsm` (FileFormat 52). The code runs only when the user enables macros.

## Modules and procedures

### Sub and Function procedures (VBA statements)

**Syntax**

```vba
Option Explicit                                   ' first line of every module

[Public | Private] [Static] Sub <Name>([<arglist>])
    <statements>
    [Exit Sub]
End Sub

[Public | Private] [Static] Function <Name>([<arglist>]) [As <Type>]
    <statements>
    <Name> = <return value>                       ' assign the return value to the function name
    [Exit Function]
End Function

' arglist item: [Optional] [ByVal | ByRef] [ParamArray] <name>[()] [As <Type>] [= <default>]
```

**Parts**

- `Public` is the default for procedures in standard modules. `Private` makes the procedure visible only inside its module.
- `ByRef` is the DEFAULT, so the caller's variable can be changed. Use `ByVal` to pass a copy.
- `Optional`: the parameter may be omitted. Test a Variant parameter with `IsMissing(x)`, or give it a default value.
- `ParamArray args()`: a variable number of arguments, as a Variant array. It must be the last parameter.
- A Function used in a worksheet cell (a UDF) must be `Public`, in a standard module, and must not change other cells.

**Example**

```vba
Public Function AddTax(ByVal amount As Double, Optional ByVal rate As Double = 0.2) As Double
    AddTax = amount * (1 + rate)
End Function

Sub Demo()
    Dim total As Double
    total = AddTax(100)          ' parentheses when using the return value
    MsgBox "Total: " & Format(total, "0.00")
    Call LogMessage("done")      ' Call + parentheses, or:  LogMessage "done"
End Sub
```

**Check**

- When you call a Sub without `Call`, write the arguments with no parentheses: `LogMessage "a", 1`. `LogMessage("a", 1)` is a syntax error. With one argument, `LogMessage("a")` passes the argument ByVal by accident.
- A Function without `Name = …` returns the default value (0, "" or Empty).

### Variables, constants and data types (VBA statements)

**Syntax**

```vba
Dim <name> [As [New] <Type>] [, <name2> As <Type2>]
Private <name> As <Type>   ' module level        Public <name> As <Type>   ' all modules (standard module)
Static <name> As <Type>    ' keeps its value between calls
Const <NAME> [As <Type>] = <value>      Public Const VERSION As String = "1.0"
Dim arr(1 To 10) As Long   Dim grid(1 To 5, 1 To 3) As Variant   Dim dyn() As String : ReDim dyn(1 To n) : ReDim Preserve dyn(1 To n + 10)
```

**Types:** `Boolean` · `Byte` · `Integer` (16-bit, up to 32,767) · `Long` (32-bit) · `LongLong` (64-bit Office only) · `LongPtr` · `Single` · `Double` · `Currency` (fixed 4 decimals) · `Decimal` (only inside a Variant) · `Date` · `String` · `String * n` (fixed length) · `Object` · `Variant` (the default) · user-defined `Type` · class names (`Worksheet`, `Range`, `Collection`, `Dictionary`).

**Type suffixes (legacy):** `%` Integer, `&` Long, `!` Single, `#` Double, `@` Currency, `$` String.

**Check**

- `ReDim Preserve` can change only the LAST dimension of an array.
- `Dim x As New Class` auto-creates the object on first use, which hides `Nothing` checks. Prefer `Dim x As Class` followed by `Set x = New Class`.

### User-defined types, Enums and class modules (VBA statements)

**Syntax**

```vba
Public Type Employee            ' in a standard module (declarations section)
    Name As String
    Salary As Currency
End Type

Public Enum Status
    stNew = 1
    stDone = 2
End Enum

' Class module "CPerson":
Private pName As String
Public Property Get Name() As String: Name = pName: End Property
Public Property Let Name(ByVal v As String): pName = v: End Property
Public Property Set Owner(ByVal v As Object): Set pOwner = v: End Property
Private Sub Class_Initialize(): End Sub
Private Sub Class_Terminate(): End Sub
' use:  Dim p As CPerson: Set p = New CPerson: p.Name = "Ann"
```

## Control flow

### If ... Then ... ElseIf ... Else (VBA statement)

**Syntax**

```vba
If <condition> Then <statement> [Else <statement>]          ' single line, no End If
If <condition> Then
    <statements>
ElseIf <condition> Then
    <statements>
Else
    <statements>
End If
```

**Check**

- `Else If` (two words) opens a NEW nested If that needs its own `End If`. Use `ElseIf`.
- `If x = 1 Or 2 Then` is always true. Write `If x = 1 Or x = 2 Then`.
- `And` evaluates both sides, so `If Not rng Is Nothing And rng.Value > 0` errors when `rng` is Nothing. Nest the two Ifs instead.

### Select Case (VBA statement)

**Syntax**

```vba
Select Case <expression>
    Case <value>[, <value2>]
        <statements>
    Case <low> To <high>
        <statements>
    Case Is <comparison-op> <value>        ' Case Is >= 90
        <statements>
    Case Else
        <statements>
End Select
```

**Example**

```vba
Select Case score
    Case Is >= 90: grade = "A"
    Case 80 To 89: grade = "B"
    Case 70, 75: grade = "C"
    Case Else: grade = "F"
End Select
```

### For ... Next, For Each, Do ... Loop, While ... Wend (VBA statements)

**Syntax**

```vba
For <counter> = <start> To <end> [Step <step>]
    <statements>
    [Exit For]
Next [<counter>]

For Each <element> In <collection | array>
    <statements>
Next [<element>]

Do [{While | Until} <condition>]
    <statements>
    [Exit Do]
Loop
Do
    <statements>
Loop [{While | Until} <condition>]

While <condition>: <statements>: Wend      ' legacy; no Exit
```

**Example**

```vba
Dim r As Long, lastRow As Long
lastRow = ws.Cells(ws.Rows.Count, "A").End(xlUp).Row
For r = lastRow To 2 Step -1                     ' backwards when deleting rows
    If ws.Cells(r, "C").Value = "" Then ws.Rows(r).Delete
Next r
Dim c As Range
For Each c In ws.Range("B2:B" & lastRow).Cells
    If IsNumeric(c.Value) Then c.Value = c.Value * 1.1
Next c
```

**Check**

- Delete rows from the bottom up (`Step -1`). Counting up skips the row after each deleted one.
- VBA has no `Continue`. Use an `If` around the body, or `GoTo NextItem` with a label before `Next`.
- `For Each` over an array needs a Variant loop variable.

### With, GoTo, Exit, End (VBA statements)

**Syntax**

```vba
With <object>
    .Property = value
    .Method args
End With
GoTo <label>          ' label: at line start, e.g.  NextItem:
Exit Sub | Exit Function | Exit For | Exit Do | Exit Property
End                   ' stops ALL code and resets globals (avoid)
Stop                  ' break into the debugger
DoEvents              ' let Excel process events
```

### Error handling: On Error, Err, Resume (VBA statements)

**Syntax**

```vba
On Error GoTo <label>          ' jump to handler
On Error Resume Next           ' ignore errors (check Err.Number right after)
On Error GoTo 0                ' turn handling off (errors stop the code again)
On Error GoTo -1               ' clear the current exception inside a handler
Resume | Resume Next | Resume <label>
Err.Number  Err.Description  Err.Source  Err.Raise number, [source], [description]  Err.Clear
' custom errors: vbObjectError + 513 ...
```

**Example**

```vba
Sub SafeImport()
    On Error GoTo Fail
    Application.ScreenUpdating = False
    Application.Calculation = xlCalculationManual
    ' ... work ...
Cleanup:
    Application.Calculation = xlCalculationAutomatic
    Application.ScreenUpdating = True
    Exit Sub
Fail:
    MsgBox "Error " & Err.Number & ": " & Err.Description, vbExclamation
    Resume Cleanup
End Sub
```

**Check**

- Put `Exit Sub` before the handler label, or the handler runs on success too.
- After `On Error Resume Next`, test `If Err.Number <> 0 Then` right away, then `On Error GoTo 0`.

## Excel object model

### Application, Workbooks and Workbook (Excel VBA objects)

**Syntax**

```vba
ThisWorkbook                      ' the workbook containing the code
ActiveWorkbook                    ' the active one (may differ)
Workbooks("Book.xlsx")            ' an open workbook by name
Set wb = Workbooks.Open(Filename:=path, ReadOnly:=True, UpdateLinks:=0, Password:="")
Set wb = Workbooks.Add
wb.Save   wb.SaveAs Filename:=path, FileFormat:=xlOpenXMLWorkbookMacroEnabled (52) | xlOpenXMLWorkbook (51) | xlCSVUTF8 (62) | xlCSV (6)
wb.SaveCopyAs path   wb.Close SaveChanges:=False   wb.FullName  wb.Path  wb.Name  wb.Worksheets  wb.Sheets
Application.ScreenUpdating = False   Application.EnableEvents = False   Application.DisplayAlerts = False
Application.Calculation = xlCalculationManual | xlCalculationAutomatic   Application.Calculate   Application.StatusBar = "Working..." | False
Application.WorksheetFunction.VLookup(...)   Application.Run "Macro"   Application.OnTime Now + TimeValue("00:01:00"), "Macro"
Application.GetOpenFilename("Excel Files (*.xlsx), *.xlsx")  Application.GetSaveAsFilename  Application.InputBox(prompt, title, , , , , , 8)  ' Type 8 = range
Application.Wait Now + TimeSerial(0, 0, 2)   Application.Union(r1, r2)   Application.Intersect(r1, r2)   Application.CutCopyMode = False
```

**Check**

- Saving as `.xlsx` (FileFormat 51) from a macro workbook DROPS the macros. Use 52 for `.xlsm`.
- Always set `DisplayAlerts`, `ScreenUpdating` and `EnableEvents` back to True, including in error handlers.

### Worksheets and Sheets (Excel VBA objects)

**Syntax**

```vba
Set ws = ThisWorkbook.Worksheets("Data")      ' by tab name
Set ws = ThisWorkbook.Worksheets(1)           ' by position (fragile)
Set ws = Sheet1                               ' by code name (VBA project name, stable)
ws.Name = "New Name"   ws.Visible = xlSheetVisible | xlSheetHidden | xlSheetVeryHidden   ws.Activate   ws.Select
Set ws = wb.Worksheets.Add(After:=wb.Worksheets(wb.Worksheets.Count))
ws.Copy After:=wb.Sheets(1)   ws.Move Before:=...   ws.Delete (prompts unless DisplayAlerts = False)
ws.Protect Password:="x", UserInterfaceOnly:=True   ws.Unprotect "x"   ws.UsedRange   ws.Cells.Clear   ws.ListObjects("Table1")
ws.Range("A1").CurrentRegion   ws.AutoFilterMode = False   ws.Calculate
' exists check:
Function SheetExists(nm As String) As Boolean
    On Error Resume Next: SheetExists = Not ThisWorkbook.Worksheets(nm) Is Nothing: On Error GoTo 0
End Function
```

**Check**

- `Sheets` includes chart sheets, while `Worksheets` does not.
- A missing sheet name gives error 9 ("Subscript out of range").

### Range and Cells (Excel VBA objects)

**Syntax**

```vba
ws.Range("A1")  ws.Range("A1:C10")  ws.Range("A1", "C10")  ws.Range("Total")(named)  ws.Range(ws.Cells(1, 1), ws.Cells(10, 3))
ws.Cells(row, col)  ws.Cells(5, "B")  ws.Rows(3)  ws.Columns("C")  ws.Range("A:A")  ws.Range("2:2")
rng.Value / .Value2 (no Currency/Date conversion, faster)  rng.Text (as displayed)  rng.Formula = "=SUM(A1:A9)"  rng.FormulaR1C1 = "=R[-1]C*2"
rng.Formula2 = "=FILTER(A2:A9,B2:B9>0)"     ' dynamic array formula (365)
rng.Offset(rowOff, colOff)  rng.Resize(rows, cols)  rng.Cells(i, j)  rng.Rows.Count  rng.Columns.Count  rng.Row  rng.Column  rng.Address(False, False)
rng.End(xlUp | xlDown | xlToLeft | xlToRight)   rng.CurrentRegion   rng.EntireRow  rng.EntireColumn  rng.SpecialCells(xlCellTypeBlanks | xlCellTypeVisible | xlCellTypeConstants | xlCellTypeFormulas)
rng.Copy Destination:=ws2.Range("A1")   rng.PasteSpecial xlPasteValues   rng.ClearContents  rng.Clear  rng.Delete xlShiftUp  rng.Insert xlShiftDown
rng.NumberFormat = "0.00" | "yyyy-mm-dd" | "@"   rng.Font.Bold = True   rng.Interior.Color = RGB(255, 255, 0)   rng.HorizontalAlignment = xlCenter
rng.Borders.LineStyle = xlContinuous   rng.AutoFit (on Columns)   rng.Merge   rng.WrapText = True   rng.Name = "MyRange"
rng.Find(What:="x", LookIn:=xlValues, LookAt:=xlWhole | xlPart, MatchCase:=False, SearchOrder:=xlByRows)  ' returns Nothing if not found
rng.Replace What:="a", Replacement:="b", LookAt:=xlPart   rng.Sort Key1:=rng.Cells(1, 2), Order1:=xlDescending, Header:=xlYes
rng.AutoFilter Field:=2, Criteria1:=">100"   rng.RemoveDuplicates Columns:=Array(1, 2), Header:=xlYes   rng.TextToColumns ...
```

**Example**

```vba
Dim ws As Worksheet, lastRow As Long, data As Variant, i As Long
Set ws = ThisWorkbook.Worksheets("Data")
lastRow = ws.Cells(ws.Rows.Count, "A").End(xlUp).Row
data = ws.Range("A2:C" & lastRow).Value              ' 2-D array, 1-based
For i = 1 To UBound(data, 1)
    data(i, 3) = data(i, 1) * data(i, 2)
Next i
ws.Range("A2:C" & lastRow).Value = data              ' write back in one step
```

**Check**

- `Range.Find` returns `Nothing` when nothing is found. Test `If Not f Is Nothing Then` before using it.
- `Range("A1:A1").Value` is a single value, not an array. Handle the one-cell case separately when reading `.Value` into an array.
- `SpecialCells` raises error 1004 when no cells match. Wrap it in `On Error Resume Next`.
- `.Formula` expects English function names and commas. `.FormulaLocal` uses the user's language.

### ListObjects (Excel tables) (Excel VBA objects)

**Syntax**

```vba
Set lo = ws.ListObjects("Sales")   Set lo = ws.ListObjects.Add(xlSrcRange, ws.Range("A1").CurrentRegion, , xlYes)  : lo.Name = "Sales"
lo.DataBodyRange  lo.HeaderRowRange  lo.ListColumns("Amount").DataBodyRange  lo.ListRows.Count  lo.ListRows.Add  lo.Range.AutoFilter Field:=1, Criteria1:="x"
lo.ListColumns.Add.Name = "New"   lo.Resize ws.Range("A1:D50")   lo.ShowTotals = True   lo.TableStyle = "TableStyleMedium2"
```

**Check**

- `lo.DataBodyRange` is `Nothing` when the table has no rows. Check for it before using it.

## Built-in functions

### String functions (VBA functions)

**Syntax**

```vba
Len(s)  Left(s, n)  Right(s, n)  Mid(s, start [, n])  InStr([start,] s, find [, vbTextCompare])  InStrRev(s, find)   ' 1-based, 0 = not found
UCase(s)  LCase(s)  StrConv(s, vbProperCase)  Trim(s)  LTrim(s)  RTrim(s)  Replace(s, find, repl [, start [, count [, compare]]])
Split(s [, delim [, limit [, compare]]]) → 0-based array   Join(arr [, delim])   StrComp(a, b [, vbTextCompare])  StrReverse(s)  Space(n)  String(n, "x")
Format(value, "0.00" | "#,##0" | "yyyy-mm-dd" | "hh:nn:ss" | "Percent" | ">")  CStr(x)  Str(n)  Val(s)  Asc(c)  Chr(n)  ChrW(n)  AscW(c)
vbCrLf  vbLf  vbCr  vbTab  vbNullString   s Like "A*#"   ' Like: * ? # [a-z] [!a-z]
```

**Check**

- In `Format`, minutes are `nn` (the `mm` code after `hh` also works). `InStr` is case-sensitive unless you pass `vbTextCompare`.

### Conversion and type-check functions (VBA functions)

**Syntax**

```vba
CInt(x)  CLng(x)  CLngLng(x)  CDbl(x)  CSng(x)  CCur(x)  CDec(x)  CBool(x)  CDate(x)  CStr(x)  CVar(x)  CByte(x)
IsNumeric(x)  IsDate(x)  IsEmpty(x)  IsNull(x)  IsArray(x)  IsObject(x)  IsMissing(x)  IsError(x)  TypeName(x)  VarType(x)
x Is Nothing   TypeOf x Is Range
```

**Check**

- `CInt` and `CLng` use banker's rounding (`CInt(2.5)` = 2). `Int()` and `Fix()` truncate.
- An empty cell is `Empty`, not `""` and not `Null`. `IsEmpty(c.Value)` is true for a blank cell, and `c.Value = ""` is true for both.

### Math, date and time functions (VBA functions)

**Syntax**

```vba
Abs(x)  Sgn(x)  Sqr(x)  Exp(x)  Log(x) (natural)  Int(x)  Fix(x)  Round(x, d) (banker's)  Rnd  Randomize  x Mod y  x \ y (integer division)  x ^ y
Now  Date  Time  Timer (seconds since midnight)  DateSerial(y, m, d)  TimeSerial(h, n, s)  DateValue(s)  TimeValue(s)
Year(d)  Month(d)  Day(d)  Weekday(d [, vbMonday])  Hour(t)  Minute(t)  Second(t)  MonthName(m)  WeekdayName(n)
DateAdd("d" | "m" | "yyyy" | "h" | "n" | "s" | "ww" | "q", n, d)   DateDiff("d", d1, d2)   DatePart("q", d)
Application.WorksheetFunction.Round(x, 2)   ' school rounding, like Excel's ROUND
```

**Check**

- In `DateAdd`, `"m"` means MONTH and `"n"` means minutes.
- VBA `Round` rounds to even. Use `WorksheetFunction.Round` for Excel's rounding.

### Arrays, Collection and Dictionary (VBA)

**Syntax**

```vba
Dim a() As Variant: a = Array(1, 2, 3)       ' Array() is 0-based
LBound(a [, dim])  UBound(a [, dim])  Erase a  IsArray(a)  Filter(arr, "x" [, True [, vbTextCompare]])
Dim col As New Collection: col.Add item [, key]  col(1)  col("key")  col.Count  col.Remove 1     ' 1-based; key lookup errors if missing
Dim d As Object: Set d = CreateObject("Scripting.Dictionary")        ' late binding (or reference Microsoft Scripting Runtime: Dim d As New Scripting.Dictionary)
d.CompareMode = vbTextCompare   d(key) = value   d.Add key, value   d.Exists(key)  d.Item(key)  d.Keys  d.Values  d.Count  d.Remove key  d.RemoveAll
WorksheetFunction.Transpose(rng.Value)       ' 2-D column → 1-D
```

**Check**

- `d.Add` errors on a duplicate key, while `d(key) = value` adds or replaces. Reading `d(missingKey)` silently ADDS the key with an Empty value, so test `Exists` first.
- `Scripting.Dictionary` is Windows-only. It does not exist on Mac Excel.

### MsgBox and InputBox (VBA functions)

**Syntax**

```vba
MsgBox(prompt [, buttons [, title]]) → vbOK | vbCancel | vbYes | vbNo | vbRetry | vbAbort | vbIgnore
' buttons: vbOKOnly vbOKCancel vbYesNo vbYesNoCancel vbRetryCancel + vbInformation vbExclamation vbCritical vbQuestion + vbDefaultButton2
InputBox(prompt [, title [, default]]) → String ("" on Cancel)
Application.InputBox(Prompt, Title, Default, , , , , Type)   ' Type: 1 number, 2 text, 4 boolean, 8 range, 64 array; returns False on Cancel
```

**Example**

```vba
If MsgBox("Delete " & n & " rows?", vbYesNo + vbQuestion, "Confirm") = vbNo Then Exit Sub
```

## Files, events and other topics

### File and folder operations (VBA statements and FileSystemObject)

**Syntax**

```vba
Dir(path [, vbDirectory])  ' first match; Dir() again for the next; "" when done
MkDir path  RmDir path  Kill pattern  FileCopy src, dst  Name old As new  FileLen(p)  FileDateTime(p)  CurDir  ChDir p
Open path For Input | Output | Append | Binary As #f : Line Input #f, s : Print #f, s : Close #f    ' f = FreeFile
Dim fso As Object: Set fso = CreateObject("Scripting.FileSystemObject")
fso.FileExists(p)  fso.FolderExists(p)  fso.CreateFolder(p)  fso.GetFolder(p).Files  fso.CopyFile src, dst, True  fso.DeleteFile p  fso.GetBaseName(p)  fso.GetExtensionName(p)  fso.BuildPath(a, b)
Set ts = fso.OpenTextFile(p, 1 (read) | 2 (write) | 8 (append), True) : ts.ReadAll / ts.ReadLine / ts.WriteLine s / ts.AtEndOfStream : ts.Close
ThisWorkbook.Path & Application.PathSeparator & "file.csv"
```

**Example**

```vba
Dim f As String
f = Dir(ThisWorkbook.Path & "\in\*.csv")
Do While f <> ""
    Debug.Print f
    f = Dir()
Loop
```

**Check**

- `Kill` deletes permanently, without the Recycle Bin. Confirm with the user first.
- `Open … For Output` truncates the file.

### Workbook and worksheet events (Excel VBA event procedures)

**Syntax**

```vba
' In ThisWorkbook module:
Private Sub Workbook_Open(): End Sub
Private Sub Workbook_BeforeSave(ByVal SaveAsUI As Boolean, Cancel As Boolean): End Sub
Private Sub Workbook_BeforeClose(Cancel As Boolean): End Sub
Private Sub Workbook_SheetChange(ByVal Sh As Object, ByVal Target As Range): End Sub
' In a worksheet module:
Private Sub Worksheet_Change(ByVal Target As Range)
    If Intersect(Target, Me.Range("B2:B100")) Is Nothing Then Exit Sub
    Application.EnableEvents = False
    On Error GoTo Done
    Target.Offset(0, 1).Value = Now
Done:
    Application.EnableEvents = True
End Sub
Private Sub Worksheet_SelectionChange(ByVal Target As Range): End Sub
Private Sub Worksheet_BeforeDoubleClick(ByVal Target As Range, Cancel As Boolean): End Sub
Private Sub Worksheet_Activate(): End Sub   Private Sub Worksheet_Calculate(): End Sub
```

**Check**

- An event handler that changes cells triggers itself again. Set `Application.EnableEvents = False` around the change, and turn it back on in every exit path.
- An event procedure must sit in the matching object module (ThisWorkbook or Sheet1), with the exact name and signature.

### WorksheetFunction and Evaluate (VBA calling Excel functions)

**Syntax**

```vba
Application.WorksheetFunction.Sum(rng)  .VLookup(key, table, col, False)  .Match(key, rng, 0)  .CountIf(rng, ">0")  .Max(rng)  .XLookup(...)
Application.VLookup(...)           ' without WorksheetFunction: returns an error value instead of raising (check with IsError)
Evaluate("SUM(A1:A10)")   [A1:A10].Value   Application.Evaluate("=TODAY()")
```

**Check**

- `WorksheetFunction.VLookup` or `.Match` with no match raises runtime error 1004. Use `Application.Match` with `IsError`, or an error handler.

### UserForms and controls (VBA)

**Syntax**

```vba
UserForm1.Show [vbModeless]   Unload Me   Me.Hide
Private Sub UserForm_Initialize(): Me.ComboBox1.List = Array("A", "B"): End Sub
Private Sub CommandButton1_Click(): MsgBox Me.TextBox1.Value: End Sub
Me.ListBox1.AddItem "x"  Me.ListBox1.ListIndex  Me.ListBox1.Selected(i)  Me.CheckBox1.Value  Me.OptionButton1.Value
```

### Calling Windows APIs (VBA Declare)

**Syntax**

```vba
#If VBA7 Then
    Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As Long
#Else
    Private Declare Function GetTickCount Lib "kernel32" () As Long
#End If
```

**Check**

- 64-bit Office requires `PtrSafe`, and `LongPtr` for handles and pointers.
