# Power Query M syntax reference (Phoenix)

For Phoenix models writing and checking Power Query M formulas, as used in Excel Get & Transform, Power BI, Dataflows and Fabric. Each `###` entry is one language construct or library function group: what it does, its exact syntax, every argument, an example, and the mistakes to check for.

**Notation:** `[x]` optional (in signatures, `optional x`) · `as type` = parameter type · `→` returns · `...` repeatable. **M is case-sensitive**: `Table.AddColumn` works and `table.addcolumn` does not, and `true`, `false`, `null`, `let`, `in`, `each` and `if` are lowercase.

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

## Rules: before you answer with M code

- A query is one `let … in …` expression. The steps are separated by commas, with NO comma after the last step before `in`, and `in` returns one step name.
- Step names with spaces or special characters are written `#"Step Name"`, and referenced the same way.
- Function names are case-sensitive, with the library prefix and a dot: `Text.Upper`, `Table.SelectRows`, `List.Sum`, `Date.Year`.
- `each _` is shorthand for `(_) => _`. Inside `each`, `[Column]` means `_[Column]` (the current row).
- Table functions take the table as the FIRST argument (the previous step), and they return a new table. Each step usually references the step before it.
- Column names are text in lists: `{"A", "B"}`. A list uses `{ }`, a record `[ ]`, and a table is built with `#table(...)` or `Table.FromRecords`.
- Comparison is `=` and not-equal is `<>`. The logical operators are `and`, `or` and `not` (not `&&`). Text concatenates with `&`, never `+`.
- Types: `Table.TransformColumnTypes(prev, {{"Col", type number}})`. The type names are `type text`, `type number`, `Int64.Type`, `type date`, `type datetime`, `type logical`, `Currency.Type`, `Percentage.Type` and `type any`.
- `null` propagates: `null + 1` is `null`. Comparing `null` with `<` gives `null`, which is treated as false in filters. Handle it with `?? default` or `if x = null`.
- Errors: `try expr otherwise default`, or `try expr` (a record with `HasError`, `Value`, `Error`). Raise one with `error "msg"` or `error Error.Record(...)`.
- Text positions are 0-based: `Text.Middle(s, 0, 3)`, `Text.PositionOf`. List items are 0-based too: `list{0}`. Row access is `table{0}` (a record), and `table{0}[Col]` gives a value.
- A date literal is `#date(2024, 1, 31)`, not `"2024-01-31"`. Parse text with `Date.FromText("2024-01-31")`, and pass the culture for local formats.

## Language

### let ... in, steps and #"quoted names" (M expression)

**Syntax**

```m
let
    Source = Excel.Workbook(File.Contents("C:\Data\sales.xlsx"), null, true),
    Sheet1 = Source{[Item = "Sheet1", Kind = "Sheet"]}[Data],
    #"Promoted Headers" = Table.PromoteHeaders(Sheet1, [PromoteAllScalars = true]),
    #"Changed Type" = Table.TransformColumnTypes(#"Promoted Headers", {{"Date", type date}, {"Amount", type number}}),
    #"Filtered Rows" = Table.SelectRows(#"Changed Type", each [Amount] > 0)
in
    #"Filtered Rows"
```

**Check**

- A comma after the last step, or a missing comma between steps, is the most common syntax error ("Token Comma expected" / "Token in expected").
- Every step name is unique, and steps can be referenced in any order. They are evaluated lazily, only when needed.

### Values, literals and operators (M language)

**Syntax**

```m
null  true  false  42  -1.5  1e3  0xff  #infinity  #nan
"text with ""quotes"" and #(lf) line feed #(tab) #(cr,lf)"           // escapes: #(lf) #(cr) #(tab) #(0041)
#date(2024, 1, 31)   #time(13, 45, 0)   #datetime(2024, 1, 31, 13, 45, 0)   #datetimezone(2024, 1, 31, 13, 45, 0, 1, 0)   #duration(1, 2, 30, 0)
#binary({0x01, 0x02})   {1, 2, 3}   {1..10}   {"a".."e"}   [Name = "Ann", Age = 30]   #table({"A", "B"}, {{1, "x"}, {2, "y"}})
+  -  *  /                 // arithmetic (no integer division operator: Number.IntegerDivide)
&                          // concatenate text, combine lists, merge records
=  <>  <  >  <=  >=        // comparison
and  or  not               // logical (short-circuit)
x ?? y                     // coalesce: y when x is null
x[Field]   x[Field]?       // record field (? = null if missing)
x{0}       x{0}?           // list item / table row (? = null if missing)
table[Column]              // column as a list
@name                      // reference itself (recursion)
```

**Check**

- `"a" + "b"` is an error. Use `&`. `"Total: " & 5` is also an error, so convert first: `"Total: " & Text.From(5)`.
- `{1..10}` builds the list. `List.Numbers(1, 10)` is the same.

### if then else (M expression)

**Syntax**

```m
if <condition> then <value> else <value>
if [Amount] > 1000 then "High" else if [Amount] > 100 then "Medium" else "Low"
```

**Check**

- There is no `elseif` keyword and no `end if`. Nest `else if`. The `else` branch is required.
- The condition must be logical: `if [Flag] then` fails when Flag is text `"true"`.

### Functions, each and _ (M expression)

**Syntax**

```m
(x as number, optional y as number) as number => x + (y ?? 0)
each [Amount] * 2                      // = (_) => _[Amount] * 2
each _ * 2                             // _ = the argument (a list item, a value)
(row) => row[Amount] * 2
let Double = (n) => n * 2 in Double(21)
List.Transform({1, 2, 3}, each _ * 10)
```

**Check**

- `each` takes exactly one parameter, `_`. For two parameters (an accumulator), use `(state, current) => ...`.
- Nested `each` hides the outer `_`. Name the outer parameter instead: `(row) => List.Select(list, each _ > row[Min])`.

### try, otherwise, error (M error handling)

**Syntax**

```m
try <expr> otherwise <fallback>
try <expr>                             // → [HasError = false, Value = ...] or [HasError = true, Error = [Reason, Message, Detail]]
try <expr> catch (e) => <expr using e[Message]>       // newer engines
error "Message"                        error Error.Record("Reason", "Message", detail)
Table.ReplaceErrorValues(tbl, {{"Col", null}})   Table.RemoveRowsWithErrors(tbl, {"Col"})   Table.SelectRowsWithErrors(tbl)
```

**Example**

```m
Table.AddColumn(Source, "Amount Num", each try Number.From([Amount]) otherwise null, type nullable number)
```

### Types and type conversion (M language)

**Syntax**

```m
type number  type text  type logical  type date  type time  type datetime  type datetimezone  type duration  type binary  type list  type record  type table  type function  type any  type null
Int64.Type  Int32.Type  Currency.Type  Percentage.Type  Decimal.Type  Double.Type  Single.Type   type nullable number
Number.From(x)  Number.FromText("1,5", "de-DE")  Text.From(x)  Date.From(x)  Date.FromText("31/01/2024", [Format = "dd/MM/yyyy", Culture = "en-GB"])
DateTime.FromText(t)  Time.FromText(t)  Duration.FromText(t)  Logical.From(x)  Logical.FromText("true")  Int64.From(x)  Currency.From(x)
Value.Type(x)  Value.Is(x, type number)  x is number  x as number
Table.TransformColumnTypes(tbl, {{"A", type text}, {"B", Int64.Type}} [, culture])
```

**Check**

- `Table.TransformColumnTypes` errors on values it cannot convert (the cells show Error). Pass a culture for local number and date formats, and clean the data first.

## Table functions

### Table creation and info (M Table library)

**Syntax**

```m
#table(columnNames as list | type table, rows as list)            #table({"A", "B"}, {{1, 2}, {3, 4}})
Table.FromRecords(records as list [, columns [, missingField]])   Table.FromRows(rows as list [, columns])   Table.FromColumns(lists as list [, columns])
Table.FromList(list, optional splitter, optional columns, optional default, optional extraValues)
Table.FromValue(value)   Record.ToTable(rec)   Table.ToRecords(t)   Table.ToRows(t)   Table.ToColumns(t)   Table.ToList(t [, combiner])
Table.RowCount(t)  Table.ColumnCount(t)  Table.ColumnNames(t)  Table.Schema(t)  Table.Profile(t)  Table.IsEmpty(t)  Table.HasColumns(t, cols)
Table.Column(t, "Col") (= t[Col])   Table.First(t [, default])  Table.Last(t)  Table.FirstN(t, n | condition)  Table.LastN(t, n)  Table.Range(t, offset [, count])
```

### Selecting and filtering rows (M Table library)

**Syntax**

```m
Table.SelectRows(table as table, condition as function) as table          // keep rows where condition is true
Table.RemoveRows(table, offset as number, optional count as number)
Table.Skip(table, count | condition)   Table.FirstN(table, count | condition)   Table.Distinct(table, optional equationCriteria)
Table.RemoveFirstN(table, count | condition)   Table.RemoveLastN(table, ...)   Table.AlternateRows(t, offset, skip, take)
Table.RemoveMatchingRows(t, rows as list, optional equationCriteria)   Table.SelectRowsWithErrors(t, cols)  Table.RemoveRowsWithErrors(t, cols)
Table.MatchesAllRows(t, condition)  Table.MatchesAnyRows(t, condition)  Table.FindText(t, text)  Table.PositionOf(t, row)
```

**Example**

```m
Table.SelectRows(Source, each [Country] = "US" and [Amount] >= 100 and [Date] >= #date(2024, 1, 1))
Table.SelectRows(Source, each List.Contains({"A", "B"}, [Code]))
Table.SelectRows(Source, each [Name] <> null and Text.StartsWith([Name], "Pro", Comparer.OrdinalIgnoreCase))
```

**Check**

- `Table.Distinct(t, {"Key"})` keeps the FIRST row per key, but row order is not guaranteed unless you sort and buffer (`Table.Buffer`) first.

### Adding and transforming columns (M Table library)

**Syntax**

```m
Table.AddColumn(table as table, newColumnName as text, columnGenerator as function, optional columnType as type) as table
Table.TransformColumns(table, transformOperations as list, optional defaultTransformation, optional missingField)
    // transformOperations: {{"Col", each Text.Upper(_), type text}, {"Col2", Number.Round}}
Table.AddIndexColumn(table, newColumnName, optional initialValue = 0, optional increment = 1, optional columnType)
Table.DuplicateColumn(t, "Col", "Col Copy")   Table.AddKey(t, {"Id"}, true)   Table.TransformRows(t, each ...) → list
Table.ReplaceValue(table, oldValue, newValue, replacer as function, columnsToSearch as list)
    // replacer: Replacer.ReplaceText (substring) | Replacer.ReplaceValue (whole value)
Table.FillDown(t, {"Col"})   Table.FillUp(t, {"Col"})
Table.CombineColumns(t, {"First", "Last"}, Combiner.CombineTextByDelimiter(" "), "Full Name")
Table.SplitColumn(t, "Full", Splitter.SplitTextByDelimiter(" ", QuoteStyle.Csv), {"First", "Last"})
```

**Example**

```m
#"Added Total" = Table.AddColumn(#"Changed Type", "Total", each [Qty] * [Price], type number),
#"Upper Names" = Table.TransformColumns(#"Added Total", {{"Name", Text.Proper, type text}}),
#"Replaced" = Table.ReplaceValue(#"Upper Names", "N/A", null, Replacer.ReplaceValue, {"Region"})
```

**Check**

- In `TransformColumns`, the function receives the cell value `_`, not the row. `[OtherColumn]` is not available there, so use `AddColumn` to compute from several columns.
- `Replacer.ReplaceText` replaces substrings in text columns. To replace whole values (numbers, null), use `Replacer.ReplaceValue`.

### Selecting, renaming and reordering columns (M Table library)

**Syntax**

```m
Table.SelectColumns(table, columns as list, optional missingField as MissingField.Type)   // MissingField.Error | .Ignore | .UseNull
Table.RemoveColumns(table, columns as list, optional missingField)
Table.RenameColumns(table, renames as list, optional missingField)          // {{"old", "new"}, {"old2", "new2"}}
Table.ReorderColumns(table, columnOrder as list, optional missingField)
Table.PrefixColumns(t, "pre")   Table.DemoteHeaders(t)   Table.PromoteHeaders(t, [PromoteAllScalars = true, Culture = "en-US"])
Table.TransformColumnNames(t, Text.Trim)   Table.ColumnsOfType(t, {type number})
```

**Check**

- The renames list is a list of pairs: `{{"a", "b"}}`. Writing `{"a", "b"}` (one level) is an error.
- Hard-coded column lists break when the source changes. Use `MissingField.Ignore`, or compute the list from `Table.ColumnNames`.

### Sorting, grouping and aggregating (M Table library)

**Syntax**

```m
Table.Sort(table, comparisonCriteria)          // {{"Col", Order.Descending}, "Col2"}   Order.Ascending | Order.Descending
Table.Group(table, key as any, aggregatedColumns as list, optional groupKind as GroupKind.Type, optional comparer)
    // aggregatedColumns: {{"Total", each List.Sum([Amount]), type number}, {"Rows", each Table.RowCount(_), Int64.Type}, {"All", each _, type table}}
Table.Max(t, "Col")  Table.Min(t, "Col")  Table.MaxN(t, "Col", n)  Table.MinN(t, "Col", n)  Table.Aggregate...
List.Sum(t[Amount])  List.Average(t[Amount])  List.Count(t[Id])  List.Max  List.Min  List.Median  List.Mode  List.StandardDeviation
```

**Example**

```m
Table.Group(Source, {"Region", "Year"}, {
    {"Sales", each List.Sum([Amount]), type nullable number},
    {"Orders", each Table.RowCount(_), Int64.Type},
    {"Last Order", each List.Max([Date]), type nullable date}
})
```

**Check**

- Inside the `Table.Group` aggregations, `_` is the sub-table of the group, and `[Amount]` is that column as a LIST. So use the `List.*` functions, not `Table.*` or scalar operators.
- `GroupKind.Local` groups only consecutive rows, which is faster but different from the default `GroupKind.Global`.

### Joining and combining tables (M Table library)

**Syntax**

```m
Table.NestedJoin(table1, key1, table2, key2, newColumnName as text, optional joinKind as JoinKind.Type, optional keyEqualityComparers)
Table.ExpandTableColumn(table, column as text, columnNames as list, optional newColumnNames as list)
Table.Join(table1, key1, table2, key2, optional joinKind, optional joinAlgorithm, optional keyEqualityComparers)   // flat; column names must not clash
JoinKind.Inner | JoinKind.LeftOuter | JoinKind.RightOuter | JoinKind.FullOuter | JoinKind.LeftAnti | JoinKind.RightAnti
Table.Combine(tables as list, optional columns)    // append (union by column name)
Table.ExpandRecordColumn(t, "Rec", {"a", "b"} [, {"Rec.a", "Rec.b"}])   Table.ExpandListColumn(t, "ListCol")
Table.FuzzyNestedJoin(t1, k1, t2, k2, "New", JoinKind.LeftOuter, [Threshold = 0.8, IgnoreCase = true])
```

**Example**

```m
#"Merged" = Table.NestedJoin(Orders, {"CustomerID"}, Customers, {"CustomerID"}, "Cust", JoinKind.LeftOuter),
#"Expanded" = Table.ExpandTableColumn(#"Merged", "Cust", {"Name", "Country"}, {"Customer Name", "Country"}),
#"Appended" = Table.Combine({Sales2023, Sales2024})
```

**Check**

- In `Table.Join`, both tables must not have other columns with the same name. Prefer `NestedJoin` followed by `Expand`.
- Keys are lists, even for a single key column: `{"CustomerID"}`. Join keys must have the same type.

### Pivot, unpivot, transpose (M Table library)

**Syntax**

```m
Table.Pivot(table, pivotValues as list, attributeColumn as text, valueColumn as text, optional aggregationFunction)
    // Table.Pivot(t, List.Distinct(t[Month]), "Month", "Sales", List.Sum)
Table.Unpivot(table, pivotColumns as list, attributeColumn as text, valueColumn as text)
Table.UnpivotOtherColumns(table, pivotColumns as list (kept columns), attributeColumn as text, valueColumn as text)
Table.Transpose(table, optional columns)
```

**Check**

- `UnpivotOtherColumns` takes the columns to KEEP, so it keeps working when new value columns appear.
- `Table.Pivot` without an aggregation errors when an attribute value repeats. Pass `List.Sum` or another aggregation.

## List, Text, Number and Date functions

### List functions (M List library)

**Syntax**

```m
List.Count(l)  List.Sum(l)  List.Average(l)  List.Min(l [, default])  List.Max(l)  List.Product(l)  List.Median(l)  List.Distinct(l)  List.Sort(l [, Order.Descending | comparer])
List.Transform(l, each ...)  List.Select(l, each ...)  List.Accumulate(l, seed, (state, current) => ...)  List.Generate(() => init, each cond, each next [, each selector])
List.Contains(l, value [, comparer])  List.ContainsAny(l, values)  List.ContainsAll(l, values)  List.PositionOf(l, value)  List.IsEmpty(l)  List.NonNullCount(l)
List.First(l [, default])  List.Last(l)  List.FirstN(l, n)  List.LastN(l, n)  List.Skip(l, n)  List.Range(l, offset, count)  List.Reverse(l)  List.Combine({l1, l2})
List.Zip({l1, l2})  List.Split(l, n)  List.Union({a, b})  List.Intersect({a, b})  List.Difference(a, b)  List.RemoveNulls(l)  List.RemoveItems(l, items)  List.ReplaceValue(l, old, new, Replacer.ReplaceValue)
List.Numbers(start, count [, increment])  List.Dates(start as date, count, #duration(1, 0, 0, 0))  List.Repeat(l, n)  List.Buffer(l)  List.MatchesAll(l, each ...)  List.MatchesAny(l, each ...)
```

**Example**

```m
List.Accumulate({1, 2, 3, 4}, 0, (total, x) => total + x)                       // 10
List.Generate(() => 1, each _ <= 100, each _ * 2)                              // {1, 2, 4, ..., 64}
```

### Text functions (M Text library)

**Syntax**

```m
Text.Length(t)  Text.Upper(t)  Text.Lower(t)  Text.Proper(t)  Text.Trim(t [, chars])  Text.TrimStart  Text.TrimEnd  Text.Clean(t)
Text.Start(t, n)  Text.End(t, n)  Text.Middle(t, start, optional count)  Text.Range(t, offset, optional count)   // 0-based
Text.PositionOf(t, sub [, Occurrence.First | .Last | .All [, comparer]])  Text.Contains(t, sub [, comparer])  Text.StartsWith(t, sub [, comparer])  Text.EndsWith(t, sub)
Text.Replace(t, old, new)  Text.Remove(t, chars as list)  Text.Select(t, chars as list)  Text.Insert(t, offset, new)  Text.ReplaceRange(t, offset, count, new)
Text.Split(t, separator)  Text.SplitAny(t, separators)  Text.Combine(list, optional separator)  Text.PadStart(t, n, "0")  Text.PadEnd(t, n)  Text.Repeat(t, n)  Text.Reverse(t)
Text.BeforeDelimiter(t, delim [, index])  Text.AfterDelimiter(t, delim [, index])  Text.BetweenDelimiters(t, start, end)
Text.From(value [, culture])  Text.Format("#{0} has #{1}", {name, n})  Text.NewGuid()  Text.ToList(t)  Character.FromNumber(n)  Character.ToNumber(c)
Comparer.OrdinalIgnoreCase  Comparer.Ordinal  Comparer.FromCulture("en-US", true)
```

**Check**

- `Text.Contains` is case-sensitive. Pass `Comparer.OrdinalIgnoreCase` as the third argument to ignore case.
- `Text.Remove` and `Text.Select` take lists of characters: `Text.Select(t, {"0".."9"})`.

### Number functions (M Number library)

**Syntax**

```m
Number.Round(x, optional digits, optional roundingMode)   // RoundingMode.ToEven (default!) | .AwayFromZero | .Up | .Down | .TowardZero
Number.RoundUp(x, digits)  Number.RoundDown(x, digits)  Number.RoundAwayFromZero(x, digits)  Number.IntegerDivide(a, b)  Number.Mod(a, b)
Number.Abs(x)  Number.Sign(x)  Number.Power(x, y)  Number.Sqrt(x)  Number.Exp(x)  Number.Ln(x)  Number.Log10(x)  Number.Log(x, base)  Number.Factorial(n)
Number.ToText(x, "N2" | "0.00" | "P1" | "C" [, culture])  Number.FromText(t [, culture])  Number.From(x)  Number.IsNaN(x)  Number.IsEven(x)  Number.IsOdd(x)
Number.Random()  Number.RandomBetween(lo, hi)  Number.BitwiseAnd(a, b)  Number.PI  Number.Epsilon  Number.MaxValue
```

**Check**

- `Number.Round(2.5)` returns 2 (banker's rounding). Use `Number.Round(x, 0, RoundingMode.AwayFromZero)` for the school rounding.

### Date, Time, DateTime and Duration functions (M libraries)

**Syntax**

```m
Date.Year(d)  Date.Month(d)  Date.Day(d)  Date.DayOfWeek(d [, Day.Monday])  Date.DayOfYear(d)  Date.WeekOfYear(d [, firstDay])  Date.QuarterOfYear(d)
Date.StartOfMonth(d)  Date.EndOfMonth(d)  Date.StartOfYear  Date.EndOfYear  Date.StartOfQuarter  Date.StartOfWeek(d [, Day.Monday])  Date.EndOfWeek
Date.AddDays(d, n)  Date.AddMonths(d, n)  Date.AddYears(d, n)  Date.AddWeeks(d, n)  Date.AddQuarters(d, n)  Date.DaysInMonth(d)  Date.IsLeapYear(d)
Date.MonthName(d [, culture])  Date.DayOfWeekName(d [, culture])  Date.ToText(d, [Format = "yyyy-MM-dd", Culture = "en-US"])  Date.FromText(t, [Format, Culture])
Date.IsInPreviousMonth(d)  Date.IsInCurrentYear(d)  Date.IsInYearToDate(d) ...   DateTime.LocalNow()  DateTime.FixedLocalNow()  DateTimeZone.UtcNow()
DateTime.Date(dt)  DateTime.Time(dt)  DateTime.From(x)  DateTime.ToText(dt, [Format = "yyyy-MM-dd HH:mm"])  DateTimeZone.SwitchZone(dtz, 0)  DateTimeZone.ToLocal(dtz)
Time.Hour(t)  Time.Minute(t)  Time.Second(t)  Duration.Days(dur)  Duration.Hours  Duration.TotalHours(dur)  Duration.TotalMinutes  d2 - d1  // → duration
```

**Format codes:** `yyyy` year · `MM` month · `MMM`/`MMMM` month name · `dd` day · `ddd`/`dddd` weekday · `HH` 0-23 · `hh` 1-12 · `mm` minutes · `ss` · `tt` AM/PM · `fff`.

**Example**

```m
Table.AddColumn(Source, "Month Start", each Date.StartOfMonth([Date]), type date)
Table.AddColumn(Source, "Days Open", each Duration.Days([Closed] - [Opened]), Int64.Type)
Calendar = List.Dates(#date(2024, 1, 1), 366, #duration(1, 0, 0, 0))
```

**Check**

- `Date.DayOfWeek` is 0-based, and the week starts on Sunday unless you pass `Day.Monday`.
- Subtracting two dates gives a duration. Use `Duration.Days(...)` to get a number.

### Record functions (M Record library)

**Syntax**

```m
[A = 1, B = 2]   r[A]   r[C]? (null if missing)   Record.Field(r, "A")  Record.FieldOrDefault(r, "C", 0)  Record.FieldNames(r)  Record.FieldValues(r)
Record.AddField(r, "C", 3)  Record.RemoveFields(r, {"A"})  Record.RenameFields(r, {{"A", "X"}})  Record.SelectFields(r, {"A"})  Record.Combine({r1, r2})  r1 & r2
Record.HasFields(r, "A")  Record.ToTable(r)  Record.FromList(values, names)  Record.TransformFields(r, {{"A", each _ * 2}})
```

## Data sources

### Data source functions (M connectors)

**Syntax**

```m
Csv.Document(File.Contents("C:\x.csv"), [Delimiter = ",", Columns = 5, Encoding = 65001, QuoteStyle = QuoteStyle.Csv])
Excel.Workbook(File.Contents(path), optional useHeaders as logical, optional delayTypes as logical)   // → table of Name, Data, Item, Kind, Hidden
Excel.CurrentWorkbook(){[Name = "Table1"]}[Content]                 // tables in the current Excel file
Folder.Files("C:\Data")  Folder.Contents(path)  File.Contents(path)  Json.Document(File.Contents(p) | Web.Contents(url))  Xml.Tables(...)  Lines.FromBinary(bin)
Web.Contents(url, [Query = [q = "x"], Headers = [Accept = "application/json"], RelativePath = "api/items", Timeout = #duration(0, 0, 1, 0)])
Sql.Database("server", "db", [Query = "SELECT ...", CommandTimeout = #duration(0, 0, 10, 0)])   Oracle.Database("server", [Query = "..."])  Odbc.Query("dsn=X", "SELECT ...")
SharePoint.Files("https://tenant.sharepoint.com/sites/x", [ApiVersion = 15])  OData.Feed(url)  Parquet.Document(bin)  Pdf.Tables(bin)
Value.NativeQuery(Sql.Database("srv", "db"), "SELECT * FROM t WHERE id = @id", [id = 5], [EnableFolding = true])
```

**Check**

- In `Web.Contents`, put the variable parts in `RelativePath` and `Query`, so that the Power BI service can refresh the source (dynamic URLs break scheduled refresh).
- Query folding: steps that a database can do (filter, select columns, group, join) are sent to it as SQL. Custom functions, `Table.Buffer`, `Table.AddIndexColumn` and similar steps break folding, so keep them late in the query.

### Combining files from a folder (M pattern)

**Example**

```m
let
    Source = Folder.Files("C:\Data\Monthly"),
    CsvOnly = Table.SelectRows(Source, each Text.Lower([Extension]) = ".csv"),
    Parsed = Table.AddColumn(CsvOnly, "Data", each Table.PromoteHeaders(Csv.Document([Content], [Delimiter = ",", Encoding = 65001]), [PromoteAllScalars = true])),
    Keep = Table.SelectColumns(Parsed, {"Name", "Data"}),
    Expanded = Table.ExpandTableColumn(Keep, "Data", Table.ColumnNames(Keep{0}[Data])),
    Typed = Table.TransformColumnTypes(Expanded, {{"Amount", type number}, {"Date", type date}}, "en-US")
in
    Typed
```

**Check**

- `Table.ColumnNames(Keep{0}[Data])` assumes that every file has the first file's columns. Use `Table.Combine(Keep[Data])` when the files differ.
