## Essentials

Target: Power Query M (Excel Get & Transform / Power BI). A query is one `let ... in` expression.

| Never in M | Write instead |
|---|---|
| `x == y`, `!=`, `&&`, `\|\|`, `!x` | `x = y`, `<>`, `and`, `or`, `not x` |
| `if a then b` (no else) | `if a then b else c` (else is required) |
| `"a" + "b"`, `'text'` | `"a" & "b"`; text only in "double quotes" (`""` inside) |
| `Table.SelectRows(t, [Amount] > 0)` | `Table.SelectRows(t, each [Amount] > 0)` (a function: `each`) |
| a step name with spaces unquoted: `Changed Type = ...` | `#"Changed Type" = ...` |
| a comma after the last step before `in` | no comma before `in` |
| `null` tests with `= null` on numbers that may be text | `[x] = null or [x] = ""`, `Value.Is([x], type number)` |
| case-insensitive by default | M is case-sensitive: `Table.SelectRows`, `Text.Contains`, true/false lowercase |

Always: each step refers to the previous step by name; types with `Table.TransformColumnTypes`; `try ... otherwise` for values that may fail to convert.

## Recipes

### Load a CSV, promote headers and set types (Csv.Document, Table.PromoteHeaders)

**Example**

```powerquery
let
    Source = Csv.Document(File.Contents("C:\Data\sales.csv"), [Delimiter = ",", Encoding = 65001, QuoteStyle = QuoteStyle.Csv]),
    Promoted = Table.PromoteHeaders(Source, [PromoteAllScalars = true]),
    Typed = Table.TransformColumnTypes(Promoted, {{"Date", type date}, {"Region", type text}, {"Amount", type number}})
in
    Typed
```

### Filter rows, add a column and group (Table.SelectRows, Table.AddColumn, Table.Group)

**Example**

```powerquery
let
    Source = Excel.CurrentWorkbook(){[Name = "Sales"]}[Content],
    Recent = Table.SelectRows(Source, each [Date] >= #date(2024, 1, 1) and [Amount] > 0),
    WithTax = Table.AddColumn(Recent, "Amount incl. tax", each [Amount] * 1.2, type number),
    Grouped = Table.Group(WithTax, {"Region"}, {{"Total", each List.Sum([Amount]), type number}, {"Orders", each Table.RowCount(_), Int64.Type}})
in
    Grouped
```

### Combine all files in a folder (Folder.Files, Table.Combine)

**Example**

```powerquery
let
    Files = Folder.Files("C:\Data\Monthly"),
    CsvOnly = Table.SelectRows(Files, each Text.Lower([Extension]) = ".csv"),
    Parsed = Table.AddColumn(CsvOnly, "Data", each Table.PromoteHeaders(Csv.Document([Content], [Delimiter = ","]))),
    Combined = Table.Combine(Parsed[Data])
in
    Combined
```

### Merge two tables (Table.NestedJoin, Table.ExpandTableColumn)

**Example**

```powerquery
let
    Orders = Excel.CurrentWorkbook(){[Name = "Orders"]}[Content],
    Customers = Excel.CurrentWorkbook(){[Name = "Customers"]}[Content],
    Merged = Table.NestedJoin(Orders, {"CustomerID"}, Customers, {"CustomerID"}, "Customer", JoinKind.LeftOuter),
    Expanded = Table.ExpandTableColumn(Merged, "Customer", {"Name", "City"}, {"Customer Name", "City"})
in
    Expanded
```

### Clean text and convert safely (Text.Trim, try ... otherwise, Table.ReplaceValue)

**Example**

```powerquery
let
    Source = Excel.CurrentWorkbook(){[Name = "Raw"]}[Content],
    Trimmed = Table.TransformColumns(Source, {{"Name", Text.Trim, type text}}),
    Price = Table.AddColumn(Trimmed, "Price", each try Number.From(Text.Remove([PriceText], {"$", ","})) otherwise null, type nullable number),
    NoBlank = Table.ReplaceValue(Price, "", null, Replacer.ReplaceValue, {"City"})
in
    NoBlank
```

### Unpivot month columns into rows (Table.UnpivotOtherColumns)

**Example**

```powerquery
let
    Source = Excel.CurrentWorkbook(){[Name = "Budget"]}[Content],
    Unpivoted = Table.UnpivotOtherColumns(Source, {"Department"}, "Month", "Amount")
in
    Unpivoted
```
