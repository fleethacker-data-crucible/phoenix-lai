# DAX syntax reference (Phoenix)

For Phoenix models writing and checking DAX (Data Analysis Expressions) in Power BI, Analysis Services, Power Pivot for Excel and Fabric semantic models. Each `###` entry is one function group, statement or concept: what it does, its exact syntax, every argument, an example, and the mistakes to check for.

**Notation:** `<Table>` a table or table expression · `<Column>` a column reference `'Table'[Column]` · `<Expression>` a scalar expression · `[x]` optional · `...` repeatable. Function names are case-insensitive (written UPPER CASE by convention).

## Essentials

Target: DAX in Power BI / Analysis Services / Power Pivot (measures unless the user asks for a calculated column or table).

| Never in DAX | Write instead |
|---|---|
| `SUM(Sales[Amount] * Sales[Qty])` | `SUMX(Sales, Sales[Amount] * Sales[Qty])` (SUM takes one column) |
| `Sales[Amount] / Sales[Cost]` in a measure | `DIVIDE(SUM(Sales[Amount]), SUM(Sales[Cost]))` (bare columns need row context) |
| `IF(x = BLANK, ...)`, `x == y`, `!=`, `&&` with `AND` | `ISBLANK(x)`, `=`, `<>`, `&&` / `\|\|` |
| `CALCULATE([Sales], Sales[Year] = MAX(Sales[Year]))` | `VAR y = MAX(Sales[Year]) RETURN CALCULATE([Sales], Sales[Year] = y)` |
| `TOTALYTD` / `SAMEPERIODLASTYEAR` without a date table | a marked date table related to the fact table, then `TOTALYTD([Sales], 'Date'[Date])` |
| `FILTER(Sales, ...)` over a whole table for a simple condition | `CALCULATE([Sales], Sales[Region] = "North")` or `KEEPFILTERS(...)` |
| measure names without brackets / columns without table | `[Total Sales]` for measures, `Sales[Amount]` for columns |

Always: `Measure Name = <expression>` (one measure per definition); `VAR ... RETURN` for anything used twice; `DIVIDE` for division; time intelligence needs a contiguous Date table.

## Recipes

### Basic measures (SUM, DISTINCTCOUNT, DIVIDE, AVERAGEX)

**Example**

```dax
Total Sales = SUM ( Sales[Amount] )
Customers = DISTINCTCOUNT ( Sales[CustomerID] )
Avg Order Value = DIVIDE ( [Total Sales], DISTINCTCOUNT ( Sales[OrderID] ) )
Revenue = SUMX ( Sales, Sales[Quantity] * Sales[UnitPrice] )
```

### Year to date, previous year and growth (TOTALYTD, SAMEPERIODLASTYEAR)

**Example**

```dax
Sales YTD = TOTALYTD ( [Total Sales], 'Date'[Date] )
Sales PY = CALCULATE ( [Total Sales], SAMEPERIODLASTYEAR ( 'Date'[Date] ) )
Sales YoY % = DIVIDE ( [Total Sales] - [Sales PY], [Sales PY] )
```

### Share of total, ignoring a filter (ALL, ALLSELECTED, REMOVEFILTERS)

**Example**

```dax
Share of All Regions =
VAR AllRegions = CALCULATE ( [Total Sales], REMOVEFILTERS ( Sales[Region] ) )
RETURN DIVIDE ( [Total Sales], AllRegions )
```

### Filter a measure by a condition (CALCULATE, KEEPFILTERS)

**Example**

```dax
North Sales = CALCULATE ( [Total Sales], Sales[Region] = "North" )
Big Orders = CALCULATE ( DISTINCTCOUNT ( Sales[OrderID] ), KEEPFILTERS ( Sales[Amount] > 1000 ) )
```

### Rank and top N (RANKX, TOPN)

**Example**

```dax
Product Rank = RANKX ( ALL ( Products[Product] ), [Total Sales], , DESC, DENSE )
Top 5 Sales = CALCULATE ( [Total Sales], TOPN ( 5, ALL ( Products[Product] ), [Total Sales], DESC ) )
```

### A date table (CALENDAR, ADDCOLUMNS)

**Example**

```dax
Date =
ADDCOLUMNS (
    CALENDAR ( DATE ( 2020, 1, 1 ), DATE ( 2026, 12, 31 ) ),
    "Year", YEAR ( [Date] ),
    "Month Number", MONTH ( [Date] ),
    "Month", FORMAT ( [Date], "MMM" ),
    "Year Month", FORMAT ( [Date], "YYYY-MM" )
)
```

## Rules: before you answer with DAX

- A measure is `Name = <expression>`, or `Name := <expression>` in Power Pivot and DAX scripts. It returns ONE scalar value in the current filter context.
- A calculated column runs per row (row context) and is stored. Prefer measures for aggregations, because a column cannot react to slicers.
- Write columns fully qualified, `'Table Name'[Column]` (quotes only when the name has spaces or special characters), and measures without a table: `[Total Sales]`.
- Plain aggregations (`SUM`, `AVERAGE`, `MIN`, `MAX`, `COUNT`, `DISTINCTCOUNT`) take ONE column: `SUM(Sales[Amount])`, never `SUM(Sales[Qty] * Sales[Price])`. Use the iterators for expressions: `SUMX(Sales, Sales[Qty] * Sales[Price])`.
- `CALCULATE(<expr>, <filters>...)` changes the filter context. Simple Boolean filters (`Product[Color] = "Red"`) replace the filters on that column. Use `KEEPFILTERS` to intersect with them instead.
- Inside a row context (calculated columns, iterators), a measure reference triggers context transition, so the row becomes a filter. A raw column reference in a measure, outside an iterator, errors ("cannot be determined in the current context").
- Divide with `DIVIDE(numerator, denominator [, alternate])`, not `/`, to handle division by zero.
- Text is in "double quotes", and a quote inside is doubled: `"say ""hi"""`. Concatenate with `&`. `&&` is AND, `||` is OR, `IN { ... }` tests membership, and `<>` is not-equal.
- Time intelligence (`TOTALYTD`, `SAMEPERIODLASTYEAR`, `DATEADD`, `DATESYTD`) needs a proper date table: contiguous dates, marked as a date table, and related to the fact table.
- `VAR name = <expr> RETURN <expr>` makes code readable and evaluates each variable once. A variable is a constant: it is computed where it is defined, not where it is used.
- Comments are `--`, `//` or `/* */`.
- `BLANK()` is DAX's null. `BLANK() = 0` is TRUE and `BLANK() == 0` is FALSE (strict equality). Use `ISBLANK()`.

## Syntax and concepts

### Measures, calculated columns and calculated tables (DAX definitions)

**Syntax**

```dax
Total Sales = SUM ( Sales[Amount] )                                   -- measure
Margin % = DIVIDE ( [Total Sales] - [Total Cost], [Total Sales] )     -- measure using measures
Sales[Line Total] = Sales[Qty] * Sales[Unit Price]                    -- calculated column (row context)
Dates = CALENDAR ( DATE ( 2020, 1, 1 ), DATE ( 2025, 12, 31 ) )       -- calculated table
-- Power Pivot / DAX query syntax:
DEFINE MEASURE Sales[Total Sales] = SUM ( Sales[Amount] )
```

**Check**

- Measure names are unique across the whole model. A measure is referenced as `[Name]`. Do not prefix it with a table name.

### Operators (DAX syntax)

**Syntax**

```dax
+  -  *  /  ^                        -- arithmetic (^ power)
=  ==  <>  <  >  <=  >=              -- comparison (== strict: BLANK not equal to 0 / "")
&&  ||  NOT ( <expr> )               -- logical; AND ( a, b ), OR ( a, b ) take exactly two args
&                                    -- text concatenation
IN { "A", "B" }                      -- table constructor membership:  Product[Color] IN { "Red", "Blue" }
( 1, 2 ) IN { ( 1, 2 ), ( 3, 4 ) }   -- row tuples
{ 1, 2, 3 }   { ( "a", 1 ), ( "b", 2 ) }   -- table constructor (column Value / Value1, Value2)
```

**Check**

- `AND(a, b, c)` is an error: AND takes two arguments. Use `a && b && c`.

### Filter context, row context and context transition (DAX concepts)

**Example**

```dax
-- Calculated column: row context only; SUM ignores the row → total of the whole table
Sales[Wrong Share] = Sales[Amount] / SUM ( Sales[Amount] )
-- Context transition: CALCULATE turns the current row into a filter
Customer[Customer Sales] = CALCULATE ( SUM ( Sales[Amount] ) )            -- sales of this customer
-- Iterator creates row context; [measure] inside causes context transition per row
Avg Sales per Customer = AVERAGEX ( VALUES ( Customer[CustomerID] ), [Total Sales] )
```

**Check**

- A calculated column that uses `SUM(...)` without CALCULATE does not filter by the current row.
- Context transition inside a large iterator (`SUMX(Sales, [Measure])`) is slow. Iterate over a smaller table such as `VALUES(Customer[ID])`.

### VAR ... RETURN (DAX statement)

**Syntax**

```dax
VAR <name> = <expression>
[VAR <name2> = <expression>]
RETURN <expression>
```

**Example**

```dax
Sales YoY % =
VAR CurrentSales = [Total Sales]
VAR PriorSales = CALCULATE ( [Total Sales], SAMEPERIODLASTYEAR ( 'Date'[Date] ) )
RETURN
    DIVIDE ( CurrentSales - PriorSales, PriorSales )
```

**Check**

- A variable is evaluated once, in the context where it is DEFINED. `CALCULATE(CurrentSales, …)` does not recompute it. Put the expression itself inside CALCULATE.

## Aggregation and iterators

### Aggregation functions (DAX functions)

**Syntax**

```dax
SUM ( <Column> )   AVERAGE ( <Column> )   MIN ( <Column> | <a>, <b> )   MAX ( <Column> | <a>, <b> )   MEDIAN ( <Column> )
COUNT ( <Column> ) (numbers/dates)   COUNTA ( <Column> )   COUNTBLANK ( <Column> )   COUNTROWS ( [<Table>] )   DISTINCTCOUNT ( <Column> )   DISTINCTCOUNTNOBLANK ( <Column> )
APPROXIMATEDISTINCTCOUNT ( <Column> )   PRODUCT ( <Column> )   STDEV.S / STDEV.P / VAR.S / VAR.P ( <Column> )   PERCENTILE.INC ( <Column>, k )
```

**Check**

- `COUNTROWS(Sales)` counts rows, while `COUNT(Sales[Id])` counts non-blank numbers. Choose on purpose.
- `DISTINCTCOUNT` counts BLANK as a value.

### Iterator functions: SUMX, AVERAGEX, MAXX, MINX, COUNTX, RANKX, CONCATENATEX (DAX functions)

**Syntax**

```dax
SUMX ( <Table>, <Expression> )        AVERAGEX ( <Table>, <Expression> )   MINX ( <Table>, <Expression> [, <Variant>] )   MAXX ( ... )
COUNTX ( <Table>, <Expression> )      COUNTAX ( <Table>, <Expression> )    PRODUCTX ( <Table>, <Expression> )   MEDIANX ( <Table>, <Expression> )
CONCATENATEX ( <Table>, <Expression> [, <Delimiter> [, <OrderBy_Expression> [, ASC | DESC] ...]] )
RANKX ( <Table>, <Expression> [, <Value> [, ASC | DESC [, Skip | Dense]]] )
```

**Example**

```dax
Revenue = SUMX ( Sales, Sales[Qty] * RELATED ( Product[Price] ) )
Customer Rank = RANKX ( ALL ( Customer[Name] ), [Total Sales], , DESC, Dense )
Product List = CONCATENATEX ( VALUES ( Product[Name] ), Product[Name], ", ", Product[Name], ASC )
```

**Check**

- `RANKX` needs `ALL(...)` (or `ALLSELECTED`) on the ranked column. Otherwise each row ranks only against itself and every rank is 1.

## Filter functions

### CALCULATE and CALCULATETABLE (DAX functions)

**Syntax**

```dax
CALCULATE ( <Expression> [, <Filter1> [, <Filter2> ...]] )
CALCULATETABLE ( <Table> [, <Filter1> ...] )
-- <Filter>: a Boolean on one column (Product[Color] = "Red"), a table (FILTER(...), VALUES(...), DATESYTD(...)),
--           or a modifier: ALL / ALLEXCEPT / REMOVEFILTERS / KEEPFILTERS / USERELATIONSHIP / CROSSFILTER
```

**Example**

```dax
Red Sales = CALCULATE ( [Total Sales], Product[Color] = "Red" )
Red Sales Kept = CALCULATE ( [Total Sales], KEEPFILTERS ( Product[Color] = "Red" ) )
Big Orders = CALCULATE ( [Total Sales], FILTER ( Sales, Sales[Amount] > 1000 ) )      -- row filter over table
Big Orders (better) = CALCULATE ( [Total Sales], Sales[Amount] > 1000 )               -- column filter
Ship Date Sales = CALCULATE ( [Total Sales], USERELATIONSHIP ( Sales[ShipDate], 'Date'[Date] ) )
```

**Check**

- A Boolean filter can reference only ONE table's columns, and in older engines one column. For conditions over several columns, use `FILTER(ALL(T[a], T[b]), ...)`.
- Do not use `FILTER(Sales, ...)` on a whole large fact table when a column filter works. It is slower, and it also applies all the columns of that table as filters.

### FILTER, ALL, ALLEXCEPT, ALLSELECTED, REMOVEFILTERS, KEEPFILTERS, VALUES, DISTINCT (DAX functions)

**Syntax**

```dax
FILTER ( <Table>, <Condition> )                    -- iterates; returns the rows where the condition is TRUE
ALL ( [<Table> | <Column> [, <Column> ...]] )     -- removes filters (inside CALCULATE) / returns all rows (as a table)
ALLEXCEPT ( <Table>, <Column> [, <Column> ...] )  -- remove all filters on the table except these columns
ALLSELECTED ( [<Table> | <Column> ...] )          -- keep outer (slicer) filters, remove those from the visual
ALLNOBLANKROW ( <Table> | <Column> )   REMOVEFILTERS ( [<Table> | <Column> ...] )   KEEPFILTERS ( <Filter> )
VALUES ( <Table> | <Column> )                     -- distinct values in the current context (+ blank row for invalid relationships)
DISTINCT ( <Column> | <Table> )                   -- distinct values (no extra blank row)
```

**Example**

```dax
% of All Products = DIVIDE ( [Total Sales], CALCULATE ( [Total Sales], ALL ( Product ) ) )
% of Category = DIVIDE ( [Total Sales], CALCULATE ( [Total Sales], ALLEXCEPT ( Product, Product[Category] ) ) )
% of Visible = DIVIDE ( [Total Sales], CALCULATE ( [Total Sales], ALLSELECTED () ) )
```

### Filter inspection: SELECTEDVALUE, HASONEVALUE, ISFILTERED, ISCROSSFILTERED, ISINSCOPE (DAX functions)

**Syntax**

```dax
SELECTEDVALUE ( <Column> [, <AlternateResult>] )   -- the value when exactly one is in context, else alternate (BLANK)
HASONEVALUE ( <Column> )   HASONEFILTER ( <Column> )   ISFILTERED ( <Table> | <Column> )   ISCROSSFILTERED ( <Table> | <Column> )
ISINSCOPE ( <Column> )   -- true at that level of a hierarchy / matrix row
FILTERS ( <Column> )   EARLIER ( <Column> [, n] ) (legacy: use VAR)   SELECTEDMEASURE () (calculation groups)
```

**Example**

```dax
Title = "Sales for " & SELECTEDVALUE ( 'Date'[Year], "all years" )
Sales (rows only) = IF ( ISINSCOPE ( Product[Name] ), [Total Sales] )
```

## Relationships and tables

### RELATED, RELATEDTABLE, USERELATIONSHIP, CROSSFILTER, TREATAS, LOOKUPVALUE (DAX functions)

**Syntax**

```dax
RELATED ( <Column> )                      -- from the many side, get a column of the one side (row context)
RELATEDTABLE ( <Table> )                  -- from the one side, the related rows of the many side
USERELATIONSHIP ( <Column1>, <Column2> )  -- activate an inactive relationship (inside CALCULATE)
CROSSFILTER ( <Column1>, <Column2>, None | OneWay | Both | OneWay_LeftFiltersRight | OneWay_RightFiltersLeft )
TREATAS ( <Table expression>, <Column> [, <Column> ...] )   -- apply values as a filter on unrelated columns
LOOKUPVALUE ( <Result_Column>, <Search_Column>, <Search_Value> [, <Search_Column2>, <Search_Value2> ...] [, <Alternate>] )
```

**Example**

```dax
Sales[Category] = RELATED ( Product[Category] )
Customer[Order Count] = COUNTROWS ( RELATEDTABLE ( Sales ) )
Budget Filtered = CALCULATE ( SUM ( Budget[Amount] ), TREATAS ( VALUES ( Product[Category] ), Budget[Category] ) )
```

**Check**

- `RELATED` works only from the many side to the one side, and only with an existing relationship.
- `LOOKUPVALUE` errors when it finds several different results. Pass an alternate result, or make sure the key is unique.

### Table functions: SUMMARIZE, SUMMARIZECOLUMNS, ADDCOLUMNS, SELECTCOLUMNS, GROUPBY, TOPN, UNION, CROSSJOIN (DAX functions)

**Syntax**

```dax
SUMMARIZECOLUMNS ( <GroupBy_Column> [, ...] [, <FilterTable> ...] [, "<Name>", <Expression> ...] )   -- preferred for queries
SUMMARIZE ( <Table>, <GroupBy_Column> [, ...] )                                                     -- group only; add measures with ADDCOLUMNS
ADDCOLUMNS ( <Table>, "<Name>", <Expression> [, "<Name2>", <Expression2> ...] )
SELECTCOLUMNS ( <Table>, "<Name>", <Expression> [, ...] )
GROUPBY ( <Table>, <GroupBy_Column> [, ...], "<Name>", <X-aggregator over CURRENTGROUP ()> )
TOPN ( <N>, <Table>, <OrderBy_Expression> [, ASC | DESC] [, ...] )      -- ties can return more than N rows
UNION ( <Table1>, <Table2> [, ...] )   INTERSECT ( <T1>, <T2> )   EXCEPT ( <T1>, <T2> )   CROSSJOIN ( <T1>, <T2> [, ...] )
NATURALINNERJOIN ( <T1>, <T2> )   NATURALLEFTOUTERJOIN ( <T1>, <T2> )   GENERATE ( <T1>, <T2 expr> )   GENERATEALL ( ... )
DATATABLE ( "Col", INTEGER | STRING | DOUBLE | DATETIME | BOOLEAN | CURRENCY, ..., { { v1, v2 }, ... } )
ROW ( "<Name>", <Expression> [, ...] )   GENERATESERIES ( <Start>, <End> [, <Increment>] )   TOPNSKIP / WINDOW / OFFSET / INDEX (see window functions)
```

**Example**

```dax
Top 5 Products = CALCULATE ( [Total Sales], TOPN ( 5, ALL ( Product[Name] ), [Total Sales], DESC ) )
EVALUATE
SUMMARIZECOLUMNS ( 'Date'[Year], Product[Category], "Sales", [Total Sales] )
```

**Check**

- Do not add computed columns inside `SUMMARIZE` (it is error-prone). Use `ADDCOLUMNS(SUMMARIZE(...), "x", [Measure])` or `SUMMARIZECOLUMNS`.

### Window functions: WINDOW, OFFSET, INDEX, RANK, ROWNUMBER (DAX functions, 2022+)

**Syntax**

```dax
OFFSET ( <Delta> [, <Relation>] [, ORDERBY ( <col> [, ASC | DESC] ... )] [, <Blanks>] [, PARTITIONBY ( <col> ... )] [, MATCHBY ( ... )] )
INDEX ( <Position> [, <Relation>] [, ORDERBY (...)] [, <Blanks>] [, PARTITIONBY (...)] )
WINDOW ( <From> [, ABS | REL], <To> [, ABS | REL] [, <Relation>] [, ORDERBY (...)] [, <Blanks>] [, PARTITIONBY (...)] )
RANK ( [DENSE | SKIP] [, <Relation>] [, ORDERBY (...)] [, <Blanks>] [, PARTITIONBY (...)] )   ROWNUMBER ( [<Relation>] [, ORDERBY (...)] ... )
```

**Example**

```dax
Prev Month Sales = CALCULATE ( [Total Sales], OFFSET ( -1, ALLSELECTED ( 'Date'[Year Month] ), ORDERBY ( 'Date'[Year Month] ) ) )
Moving 3M = CALCULATE ( [Total Sales], WINDOW ( -2, REL, 0, REL, ALLSELECTED ( 'Date'[Year Month] ), ORDERBY ( 'Date'[Year Month] ) ) )
```

## Time intelligence

### Date tables: CALENDAR, CALENDARAUTO (DAX functions)

**Syntax**

```dax
CALENDAR ( <StartDate>, <EndDate> )    CALENDARAUTO ( [<FiscalYearEndMonth>] )
```

**Example**

```dax
Date =
ADDCOLUMNS (
    CALENDAR ( DATE ( 2020, 1, 1 ), DATE ( 2026, 12, 31 ) ),
    "Year", YEAR ( [Date] ),
    "Month Number", MONTH ( [Date] ),
    "Month", FORMAT ( [Date], "MMM" ),
    "Year Month", FORMAT ( [Date], "YYYY-MM" ),
    "Quarter", "Q" & QUARTER ( [Date] ),
    "Weekday", WEEKDAY ( [Date], 2 )
)
```

**Check**

- Mark the date table ("Mark as date table"), relate its Date column to the fact table's date (dates only, no times), and cover whole years with no gaps.
- Sort the "Month" text column by "Month Number" in the model.

### Time intelligence functions (DAX functions)

**Syntax**

```dax
TOTALYTD ( <Expression>, <Dates> [, <Filter>] [, "<YearEndDate MM-DD>"] )   TOTALQTD ( ... )   TOTALMTD ( ... )
DATESYTD ( <Dates> [, "<YearEnd>"] )   DATESQTD ( <Dates> )   DATESMTD ( <Dates> )
SAMEPERIODLASTYEAR ( <Dates> )   PARALLELPERIOD ( <Dates>, <n>, YEAR | QUARTER | MONTH )   PREVIOUSYEAR / PREVIOUSQUARTER / PREVIOUSMONTH / PREVIOUSDAY ( <Dates> )
NEXTYEAR / NEXTQUARTER / NEXTMONTH / NEXTDAY ( <Dates> )   DATEADD ( <Dates>, <n>, YEAR | QUARTER | MONTH | DAY )
DATESBETWEEN ( <Dates>, <Start>, <End> )   DATESINPERIOD ( <Dates>, <StartDate>, <n>, YEAR | QUARTER | MONTH | DAY )
FIRSTDATE ( <Dates> )  LASTDATE ( <Dates> )  STARTOFMONTH ( <Dates> )  ENDOFMONTH  STARTOFQUARTER  ENDOFQUARTER  STARTOFYEAR  ENDOFYEAR
OPENINGBALANCEMONTH ( <Expr>, <Dates> )  CLOSINGBALANCEMONTH ( <Expr>, <Dates> )  ...QUARTER / ...YEAR
```

**Example**

```dax
Sales YTD = TOTALYTD ( [Total Sales], 'Date'[Date] )
Sales FYTD = TOTALYTD ( [Total Sales], 'Date'[Date], "06-30" )          -- fiscal year ending June 30
Sales LY = CALCULATE ( [Total Sales], SAMEPERIODLASTYEAR ( 'Date'[Date] ) )
Rolling 12M = CALCULATE ( [Total Sales], DATESINPERIOD ( 'Date'[Date], MAX ( 'Date'[Date] ), -12, MONTH ) )
```

**Check**

- The `<Dates>` argument is the date table's column (`'Date'[Date]`), not the fact table's date column.
- `LASTDATE` returns a table, while `MAX('Date'[Date])` returns a scalar. Use `MAX` where a scalar is needed.

## Scalar functions

### Logical functions (DAX functions)

**Syntax**

```dax
IF ( <Test>, <ThenResult> [, <ElseResult>] )    IF.EAGER ( ... )
SWITCH ( <Expression>, <Value1>, <Result1> [, <Value2>, <Result2> ...] [, <Else>] )
SWITCH ( TRUE (), <Cond1>, <Result1>, <Cond2>, <Result2>, <Else> )       -- multi-condition idiom
AND ( a, b )  OR ( a, b )  NOT ( a )  TRUE ()  FALSE ()  IFERROR ( <Value>, <ValueIfError> )  COALESCE ( a, b, ... )
```

**Example**

```dax
Band = SWITCH ( TRUE (), [Total Sales] >= 100000, "A", [Total Sales] >= 10000, "B", "C" )
```

### Math and division (DAX functions)

**Syntax**

```dax
DIVIDE ( <Numerator>, <Denominator> [, <AlternateResult>] )   ROUND ( x, d )  ROUNDUP  ROUNDDOWN  MROUND ( x, multiple )  INT ( x )  TRUNC ( x [, d] )
CEILING ( x, sig )  FLOOR ( x, sig )  ABS  SIGN  SQRT  POWER ( x, y )  EXP  LN  LOG ( x [, base] )  LOG10  MOD ( a, b )  QUOTIENT ( a, b )
RAND ()  RANDBETWEEN ( lo, hi )  PI ()  EVEN  ODD  FACT  GCD  LCM  CURRENCY ( x )  CONVERT ( <Expr>, INTEGER | DOUBLE | STRING | BOOLEAN | CURRENCY | DATETIME )
```

### Text functions (DAX functions)

**Syntax**

```dax
CONCATENATE ( a, b )  (only 2 args; use &)   LEN ( t )  LEFT ( t, n )  RIGHT ( t, n )  MID ( t, start, n )   -- 1-based
UPPER  LOWER  TRIM  SUBSTITUTE ( t, old, new [, instance] )  REPLACE ( t, start, n, new )  REPT ( t, n )  EXACT ( a, b )
SEARCH ( find, within [, start [, NotFoundValue]] ) (case-insensitive)   FIND ( find, within [, start [, NotFoundValue]] ) (case-sensitive)
FORMAT ( <Value>, "<Format>" [, "<Locale>"] )   VALUE ( t )   FIXED ( x, d [, no_commas] )   UNICHAR ( n )  UNICODE ( t )  COMBINEVALUES ( delim, a, b )
CONTAINSSTRING ( within, find )  CONTAINSSTRINGEXACT ( ... )
```

**FORMAT codes:** `"0"` · `"0.00"` · `"#,##0"` · `"0%"` · `"$#,##0.00"` · `"Currency"` · `"Percent"` · `"Standard"` · `"yyyy-mm-dd"` · `"MMM yyyy"` · `"dddd"` · `"hh:nn:ss"` (nn = minutes) · `"Short Date"` · `"Long Date"`.

**Check**

- `FORMAT` returns TEXT. A measure that uses it sorts as text, and cannot be summed. Format in the visual's settings, or use dynamic format strings.
- `SEARCH` and `FIND` raise an error when the text is not found, unless you pass `NotFoundValue`.

### Date and time functions (DAX functions)

**Syntax**

```dax
DATE ( y, m, d )  TIME ( h, m, s )  DATEVALUE ( text )  TIMEVALUE ( text )  TODAY ()  NOW ()  UTCTODAY ()  UTCNOW ()
YEAR  MONTH  DAY  HOUR  MINUTE  SECOND  QUARTER ( d )  WEEKDAY ( d [, 1 | 2 | 3] )  WEEKNUM ( d [, 1 | 2 | 21] )
EDATE ( d, months )  EOMONTH ( d, months )  DATEDIFF ( start, end, SECOND | MINUTE | HOUR | DAY | WEEK | MONTH | QUARTER | YEAR )  YEARFRAC ( a, b [, basis] )
NETWORKDAYS ( start, end [, weekend [, holidays]] )
```

**Check**

- `DATEDIFF` counts boundaries crossed: from Dec 31 to Jan 1 is 1 year.

### Information functions (DAX functions)

**Syntax**

```dax
ISBLANK ( x )  ISERROR ( x )  ISNUMBER ( x )  ISTEXT ( x )  ISLOGICAL ( x )  ISNONTEXT ( x )  ISEVEN ( n )  ISODD ( n )  ISEMPTY ( <Table> )
CONTAINS ( <Table>, <Column>, <Value> [, ...] )  CONTAINSROW ( <Table>, <Value> ... )  USERPRINCIPALNAME ()  USERNAME ()  USEROBJECTID ()  BLANK ()
NAMEOF ( [Measure] )  PATH ( id, parent )  PATHITEM ( path, n [, type] )  PATHLENGTH ( path )  PATHCONTAINS ( path, item )
```

**Example**

```dax
-- Row-level security role filter on Region table:
[Manager Email] = USERPRINCIPALNAME ()
```

## DAX queries

### EVALUATE, DEFINE, ORDER BY (DAX query statements)

**Syntax**

```dax
DEFINE
    VAR __Year = 2024
    MEASURE Sales[Test Measure] = [Total Sales] * 2
    TABLE Top10 = TOPN ( 10, VALUES ( Product[Name] ), [Total Sales] )
    COLUMN Sales[Flag] = Sales[Amount] > 100
EVALUATE
    SUMMARIZECOLUMNS ( Product[Category], TREATAS ( { __Year }, 'Date'[Year] ), "Sales", [Total Sales] )
ORDER BY [Sales] DESC
START AT 1000
```

**Check**

- `EVALUATE` must return a table. Wrap a scalar in `{ }` or `ROW("x", ...)`.
