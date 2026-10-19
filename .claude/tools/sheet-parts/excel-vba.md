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
