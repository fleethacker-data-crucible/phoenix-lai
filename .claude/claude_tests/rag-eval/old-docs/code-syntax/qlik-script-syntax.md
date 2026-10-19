# Qlik Script syntax reference (Phoenix)

For Phoenix models writing and checking Qlik load script (Qlik Sense and QlikView data load editor), and Qlik chart expressions. Each `###` entry is one statement, prefix, function or expression feature: what it does, its exact syntax, every argument, an example, and the mistakes to check for.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply. Script keywords and function names are case-insensitive. Field names, table names and variable names ARE case-sensitive.

## Checklist: before you answer with Qlik script

- Every statement ends with `;`. A `LOAD` reads from a source given after it: `FROM`, `RESIDENT`, `INLINE`, `AUTOGENERATE`, or the preceding load or SQL statement (preceding load).
- Field and table names with spaces or special characters go in `[brackets]` or `"double quotes"`. Text values go in 'single quotes'.
- Qlik associates tables automatically on EVERY field name they share. Two shared fields make a synthetic key (`$Syn`). Rename or qualify the fields so that each pair of tables shares one key field.
- A `LOAD` with exactly the same field names as an earlier table is auto-concatenated into it. Use `NoConcatenate` to keep a separate table, and `Concatenate (Table)` to append on purpose.
- `RESIDENT` reads a table already in memory. `DROP TABLE` temporary tables afterwards, so their fields do not associate.
- Variables: `SET` stores the text as written, while `LET` evaluates it first. Use `$(vName)` for dollar-sign expansion. A text value used in an expression needs quotes: `'$(vText)'`.
- A `WHERE` on a preceding load filters that load. `Exists(field, value)` checks values already loaded in the field.
- Aggregation in the script (`Sum`, `Count`) needs `GROUP BY` listing every non-aggregated field.
- Dates: set the `DateFormat`/`TimestampFormat` variables, or parse with `Date#(text, 'fmt')` and then format with `Date(num, 'fmt')`. `Date#` reads text, `Date` formats a number.
- Comments: `//` to the end of the line, `/* … */` for blocks, or a `REM` statement.
- In chart expressions, use set analysis `{<Field={'value'}>}` inside an aggregation, not `If()` filtering, for performance.
- `IF` is a function `If(cond, then, else)` in expressions. The script control statement is `IF … THEN … ELSEIF … ELSE … END IF`.

## Script structure

### Script basics, comments, tabs (Qlik script syntax)

**Syntax**

```qlik
// line comment
/* block comment */
REM statement-style comment;
SET ThousandSep=',';  SET DecimalSep='.';  SET MoneyFormat='$#,##0.00;-$#,##0.00';
SET TimeFormat='h:mm:ss TT';  SET DateFormat='M/D/YYYY';  SET TimestampFormat='M/D/YYYY h:mm:ss[.fff] TT';
SET FirstWeekDay=6;  SET BrokenWeeks=1;  SET ReferenceDay=0;  SET FirstMonthOfYear=1;  SET CollationLocale='en-US';
SET MonthNames='Jan;Feb;Mar;Apr;May;Jun;Jul;Aug;Sep;Oct;Nov;Dec';  SET DayNames='Mon;Tue;Wed;Thu;Fri;Sat;Sun';
```

**Check**

- These format variables are generated at the top of a new app. They control how dates and numbers are interpreted when loading text. Change them only on purpose.

### LOAD (Qlik script statement)

**Syntax**

```qlik
[<label>:]
[<prefixes>] LOAD [DISTINCT] <fieldlist>
{ FROM <file> [<format-spec>]
| FROM_FIELD (<table>, <field>) [<format-spec>]
| INLINE [ <data> ] [<format-spec>]
| RESIDENT <table>
| AUTOGENERATE <n>
| (nothing: preceding load from the statement below) }
[WHERE <criterion> | WHILE <criterion>]
[GROUP BY <fieldlist>]
[ORDER BY <field> [ASC | DESC] [, ...]];
```

**Parts**

- `<fieldlist>`: `*`, or `<expr> [AS <alias>]` items separated by commas. The expressions may use any script function.
- `<label>:`: the table name. Without one, the table is named after the source.
- `DISTINCT`: only unique rows.
- `WHERE`: filters rows. `WHILE`: repeats a row while the condition holds (with `IterNo()`).
- `GROUP BY`: needed with aggregation functions.
- `ORDER BY`: only with `RESIDENT` loads.
- `<format-spec>`: in parentheses, `(txt | qvd | ooxml | biff | html | xml | json | parquet | fix, codepage is utf8, embedded labels | explicit labels | no labels, delimiter is ',' | '\t' | ';', msq, header is n lines, table is [Sheet1], comment is //, no eof, filters(...))`.

**Example**

```qlik
Sales:
LOAD
    OrderID,
    CustomerID,
    Date(Date#(OrderDate, 'YYYY-MM-DD'), 'DD/MM/YYYY') AS OrderDate,
    Year(Date#(OrderDate, 'YYYY-MM-DD')) AS OrderYear,
    Quantity * UnitPrice AS Amount
FROM [lib://Data/sales.csv]
(txt, utf8, embedded labels, delimiter is ',', msq)
WHERE Quantity > 0;
```

**Check**

- In a data load script, file paths use `lib://Connection/...` in Qlik Sense (or relative paths in QlikView and legacy mode).
- `ORDER BY` works only on `RESIDENT` loads.

### Preceding load (Qlik script technique)

A `LOAD` with no source reads the output of the `LOAD` or `SELECT` directly below it.

**Syntax**

```qlik
Orders:
LOAD *, Month(OrderDate) AS OrderMonth;          // runs second, sees the fields below
LOAD OrderID, Date#(OrderDateText, 'YYYYMMDD') AS OrderDate, Amount
FROM [lib://Data/orders.qvd] (qvd);               // runs first
```

**Check**

- Only the bottom statement has a `FROM` (or `SQL SELECT`). A preceding load cannot use `GROUP BY` on the base of a `SQL SELECT`; use RESIDENT for that.

### INLINE and AUTOGENERATE (Qlik script data sources)

**Syntax**

```qlik
Regions:
LOAD * INLINE [
Region, Manager
North, Ann
South, Bob
];                                                // first line = field names; delimiter ','
LOAD * INLINE "Code|Name
A|Alpha" (delimiter is '|');
Numbers:
LOAD RecNo() AS N AUTOGENERATE 100;               // 100 rows, no source fields
Calendar:
LOAD Date($(vMinDate) + IterNo() - 1) AS Date
AUTOGENERATE 1 WHILE $(vMinDate) + IterNo() - 1 <= $(vMaxDate);
```

**Check**

- INLINE data may use `[ ]`, `" "` or `' '` around the block. A value containing a comma needs quotes, or another delimiter.

### RESIDENT, NoConcatenate, DROP, RENAME, STORE (Qlik script statements)

**Syntax**

```qlik
Temp:
LOAD * FROM [lib://Data/x.qvd] (qvd);
Final:
NoConcatenate
LOAD CustomerID, Sum(Amount) AS Total
RESIDENT Temp
GROUP BY CustomerID;
DROP TABLE Temp;                                  // or DROP TABLES A, B;
DROP FIELD Comment [FROM Final];                  // or DROP FIELDS a, b;
RENAME TABLE Final TO Customers;  RENAME FIELD Total TO [Total Sales];
RENAME FIELDS USING FieldMap;                     // mapping table old → new
STORE Customers INTO [lib://Data/customers.qvd] (qvd);     // (txt) for CSV, (parquet)
STORE Field1, Field2 FROM Customers INTO [lib://Data/c.csv] (txt, delimiter is ';');
```

**Check**

- Without `NoConcatenate`, `Final` would be appended to `Temp` if the field names matched exactly, and then dropped together with `Temp`.

### SQL SELECT and connections (Qlik script statements)

**Syntax**

```qlik
LIB CONNECT TO 'MyDatabase (domain_user)';        // Qlik Sense data connection
ODBC CONNECT TO [DSN name];  OLEDB CONNECT TO [...];   // QlikView / legacy
Customers:
LOAD CustomerID, Upper(Name) AS Name;             // optional preceding load in Qlik syntax
SQL SELECT CustomerID, Name FROM dbo.Customers WHERE Active = 1;   // passed to the database as-is
DISCONNECT;
```

**Check**

- The text after `SQL` is sent to the database, so it uses THAT database's SQL dialect, not Qlik functions. Qlik functions belong in a preceding LOAD.

## Table prefixes

### CONCATENATE, JOIN, KEEP (Qlik script prefixes)

**Syntax**

```qlik
Concatenate [(<table>)] LOAD ... ;                // append rows (fields can differ; missing ones become null)
[Inner | Outer | Left | Right] Join [(<table>)] LOAD ... ;   // merge into the table on all common fields (default Outer)
[Inner | Left | Right] Keep [(<table>)] LOAD ... ;            // reduce both tables like a join, but keep them separate
```

**Example**

```qlik
LEFT JOIN (Orders)
LOAD CustomerID, Country RESIDENT Customers;     // adds Country to Orders by CustomerID
```

**Check**

- `JOIN` matches on ALL the fields with the same name. Load only the key and the new fields, or rows multiply and fields get mismatched.
- Without `(<table>)`, `JOIN` and `CONCATENATE` target the most recently loaded table.

### MAPPING and ApplyMap (Qlik script prefix and function)

**Syntax**

```qlik
CountryMap:
MAPPING LOAD CountryCode, CountryName FROM [lib://Data/countries.csv] (txt, embedded labels);   // exactly 2 columns
Orders:
LOAD *, ApplyMap('CountryMap', CountryCode, 'Unknown') AS Country FROM ...;
MAP CountryCode USING CountryMap;                 // automatic mapping of a field in later loads
UNMAP CountryCode;
```

**Arguments of ApplyMap**

- `'map_name'`: the mapping table name, as a string in quotes.
- `expression`: the value to look up (the first column of the map).
- `default`: optional, returned when there is no match. Without it, the input value itself is returned.

**Check**

- A mapping table must have exactly two columns. It is dropped automatically at the end of the script, and cannot be read with RESIDENT.
- ApplyMap is a left lookup that never duplicates rows, which makes it safer than JOIN for adding a single attribute.

### QUALIFY, UNQUALIFY, ALIAS (Qlik script statements)

**Syntax**

```qlik
QUALIFY *;           // later fields are named Table.Field
UNQUALIFY CustomerID;   // except the key
QUALIFY Amount, Date;  UNQUALIFY *;
ALIAS ID AS CustomerID, NAME AS CustomerName;     // rename on load
```

### Other prefixes: CROSSTABLE, GENERIC, HIERARCHY, INTERVALMATCH, BUFFER, FIRST, SAMPLE, ADD, REPLACE (Qlik script prefixes)

**Syntax**

```qlik
CrossTable (Month, Sales [, 1]) LOAD Region, Jan, Feb, Mar FROM ...;      // unpivot: 1 = number of qualifier (kept) columns
Generic LOAD Key, Attribute, Value RESIDENT EAV;                          // one table per attribute
Hierarchy (NodeID, ParentID, NodeName [, ParentName, PathSource, PathName, '/', Depth]) LOAD NodeID, ParentID, NodeName FROM ...;
HierarchyBelongsTo (NodeID, ParentID, NodeName, AncestorID, AncestorName [, DepthDiff]) LOAD ...;
IntervalMatch (EventTime [, KeyField]) LOAD Start, End [, KeyField] RESIDENT Shifts;   // link points to intervals
Buffer [(Incremental | Stale [After] n [Days | Hours])] LOAD ... ;        // cache a load as a QVD
First 1000 LOAD * FROM ...;     Sample 0.1 LOAD * FROM ...;
Add [Only] LOAD ...;  Replace [Only] LOAD ...;                             // partial reload
Semantic LOAD ...;  Unless <cond> LOAD ...;  When <cond> LOAD ...;
```

**Check**

- In `CrossTable(AttrField, DataField, n)`, the first n columns are kept, and every other column becomes a row.

## Variables and dollar-sign expansion

### SET, LET and $(variable) (Qlik script statements)

**Syntax**

```qlik
SET vPath = 'lib://Data/';               // stored as written, without the outer quotes: lib://Data/
SET vExpr = Sum(Sales);                  // stores the text Sum(Sales)
LET vToday = Today();                    // evaluated: the date number
LET vYear = Year(Today());
LET vMax = Peek('MaxDate', 0, 'MinMax');
LET vFile = '$(vPath)' & 'sales.csv';
LOAD * FROM [$(vPath)sales.csv] (txt);   // $( ) inserts the text before the line is parsed
LET vCount = NoOfRows('Sales');  TRACE Rows: $(vCount);
SET vSum = Sum({<Year={$(=Max(Year))}>} Sales);   // chart: $(vSum) ;  $(=expr) evaluates in the chart
LET vNull = Null();                      // deletes the variable
```

**Check**

- Expanding a text variable inside an expression needs quotes: `WHERE Region = '$(vRegion)'`. A number does not: `WHERE Year = $(vYear)`.
- `LET` with a date gives a number. Format it with `Date()` when it must be text: `LET vD = Date(Today(), 'YYYY-MM-DD');`.
- `$(vName)` with an undefined variable expands to nothing, which leaves invalid syntax (`WHERE Year = ;`).

### Dollar-sign expansion with parameters and expressions (Qlik syntax)

**Syntax**

```qlik
SET vMul = $1 * $2;            // parameters $1..$n
LET x = $(vMul(3, 4));         // 12
$(=Max(Year))                  // evaluate an expression (in charts, once per chart)
$(#vNumber)                    // always as a number with '.' decimal point
$(must_include=lib://x/inc.qvs)   $(include=lib://x/inc.qvs)   // include a script file (must_include errors if missing)
```

## Control statements

### IF, SWITCH, FOR, FOR EACH, DO, SUB, CALL, EXIT (Qlik script control statements)

**Syntax**

```qlik
IF <condition> THEN
    <statements>
ELSEIF <condition> THEN
    <statements>
ELSE
    <statements>
END IF

SWITCH <expression>
CASE <value> [, <value>]
    <statements>
DEFAULT
    <statements>
END SWITCH

FOR i = 1 TO 10 [STEP 1]
    <statements>          // use $(i)
NEXT [i]

FOR EACH vFile IN FileList('lib://Data/*.csv')      // also: 'a', 'b' | $(=list) | DirList(...) | FieldValueList('Field')
    LOAD *, '$(vFile)' AS Source FROM [$(vFile)] (txt, embedded labels);
NEXT vFile

DO [WHILE | UNTIL <condition>]
    <statements>
LOOP [WHILE | UNTIL <condition>]

SUB LoadTable (pName, pPath)
    [$(pName)]: LOAD * FROM [$(pPath)] (qvd);
END SUB
CALL LoadTable('Customers', 'lib://Data/customers.qvd');

EXIT SCRIPT [WHEN <cond> | UNLESS <cond>];   EXIT FOR [WHEN ...];   EXIT DO;   EXIT SUB;
```

**Check**

- Control statements are written WITHOUT a semicolon after `THEN`, `ELSE`, `END IF`, `NEXT`, `LOOP` and `END SUB`, and they must each be on their own line.
- A `SUB` must be defined before its `CALL`.

### Error handling, TRACE, SLEEP, EXECUTE, Directory (Qlik script statements)

**Syntax**

```qlik
SET ErrorMode = 0;          // 0 = ignore errors and continue, 1 = stop (default), 2 = fail without prompt
LOAD * FROM [lib://Data/maybe.csv] (txt);
IF ScriptError <> 0 THEN TRACE Load failed: $(ScriptErrorDetails); END IF
SET ErrorMode = 1;
TRACE Message text $(vVar);
SLEEP 1000;                 // milliseconds
Directory lib://Data/;      // QlikView/legacy: base folder for relative paths
```

## Script functions

### Inter-record and table functions: Peek, Previous, Exists, FieldValue, NoOfRows (Qlik script functions)

**Syntax**

```qlik
Peek('FieldName' [, row [, 'TableName']])      // row 0 = first, -1 = last (default); works on any loaded table
Previous(expression)                          // value from the previous input row of this load
Exists(field [, expression])                  // TRUE if the value was already loaded into field
FieldValue('Field', n)  FieldValueCount('Field')  FieldIndex('Field', value)  FieldName(n, 'Table')  FieldNumber('Field', 'Table')
NoOfRows('Table')  NoOfFields('Table')  NoOfTables()  TableName(n)  TableNumber('Table')
RowNo()  RecNo()  IterNo()  Lookup('ReturnField', 'MatchField', value [, 'Table'])
AutoNumber(expression [, 'id'])  AutoNumberHash128(a, b)  Hash128(a, b)  Hash256(...)
```

**Example**

```qlik
Running:
LOAD Date, Amount,
     If(Previous(Customer) = Customer, Peek('CumAmount') + Amount, Amount) AS CumAmount
RESIDENT Sales ORDER BY Customer, Date;
LOAD * FROM new.qvd (qvd) WHERE NOT Exists(OrderID);      // incremental: skip known keys
```

**Check**

- `RecNo()` counts input records and `RowNo()` counts loaded rows (after WHERE).
- `Exists` is checked against every value loaded so far, including the current load. Use `Exists(field, expr)` with care in the same table.

### Aggregation functions in the script (Qlik script functions)

**Syntax**

```qlik
Sum([DISTINCT] expr)  Count([DISTINCT] expr | *)  Avg(expr)  Min(expr [, rank])  Max(expr [, rank])  Only(expr)  Mode(expr)
FirstSortedValue([DISTINCT] value, sort_weight [, rank])  MinString(expr)  MaxString(expr)  Concat([DISTINCT] expr [, delimiter [, sort_weight]])
NumericCount(expr)  TextCount(expr)  NullCount(expr)  MissingCount(expr)  Stdev(expr)  Median(expr)  Fractile(expr, 0.9)  Correl(x, y)
```

**Check**

- Every field in the LOAD that is not inside an aggregation must be listed in `GROUP BY`.

### Conditional and logical functions (Qlik functions)

**Syntax**

```qlik
If(condition, then [, else])           Alt(expr1, expr2, ..., else)     // first valid number
Match(expr, 'a', 'b', ...)  → 1-based position, case-sensitive;  MixMatch(...) case-insensitive;  WildMatch(expr, '*a*', 'b?')
Pick(n, expr1, expr2, ...)  Class(expr, interval [, label [, offset]])   Coalesce(a, b, ...)  EmptyIsNull(expr)
IsNull(expr)  IsNum(expr)  IsText(expr)  IsPartialReload()   Null()  True() (-1)  False() (0)
```

**Check**

- In Qlik, true is -1 and false is 0.
- `Match` returns 0 when nothing matches. Use it as a condition: `If(Match(Region, 'N', 'S'), ...)`.

### String functions (Qlik functions)

**Syntax**

```qlik
a & b                   Len(s)  Upper(s)  Lower(s)  Capitalize(s)  Trim(s)  LTrim(s)  RTrim(s)  Purgechar(s, 'chars')  KeepChar(s, 'chars')
Left(s, n)  Right(s, n)  Mid(s, start [, n])  Index(s, sub [, n])  SubField(s, 'delim' [, n])  TextBetween(s, 'start', 'end' [, n])
Replace(s, from, to)  Repeat(s, n)  Chr(n)  Ord(s)  FindOneOf(s, 'chars')  SubStringCount(s, sub)
Text(expr)  Num(expr [, 'fmt' [, dec_sep, thou_sep]])  Num#(text [, 'fmt' [, dec, thou]])  Dual(text, number)  ApplyCodepage(s, cp)
Evaluate(s)  Hash128(s)  MapSubString('map', s)  Like: s Like 'A*'  (wildcards * ?)
```

**Example**

```qlik
LOAD SubField(Tags, ',') AS Tag RESIDENT Posts;     // without n: one row per piece
LOAD Num#(Replace(AmountText, ',', '.')) AS Amount FROM ...;
```

**Check**

- `SubField(s, ',')` with no index creates one row per piece. With an index, it returns a single piece.

### Date and time functions (Qlik functions)

**Syntax**

```qlik
Today([timer_mode])  Now([timer_mode])  Date(num [, 'fmt'])  Time(num [, 'fmt'])  Timestamp(num [, 'fmt'])  Interval(num [, 'fmt'])
Date#(text [, 'fmt'])  Time#(text, 'fmt')  Timestamp#(text, 'fmt')  Interval#(text, 'fmt')   MakeDate(Y [, M [, D]])  MakeTime(h [, m [, s]])
Year(d)  Month(d)  Day(d)  Week(d)  WeekDay(d)  Hour(t)  Minute(t)  Second(t)  DayNumberOfYear(d)
MonthStart(d [, shift])  MonthEnd(d [, shift])  MonthName(d)  QuarterStart  QuarterEnd  QuarterName  YearStart(d [, shift [, first_month]])  YearEnd  YearName
WeekStart(d [, shift [, first_day]])  WeekEnd  WeekName  DayStart(ts)  DayEnd  AddMonths(d, n [, mode])  AddYears(d, n)  Floor(ts) (date part)  Frac(ts) (time part)
InYear(d, base, shift [, first_month])  InMonth(d, base, shift)  InYearToDate(d, base, shift)  InQuarter  InWeek  InDay  Age(d, birth)  NetWorkDays(start, end [, holidays])
ConvertToLocalTime(ts, 'Paris')  GMT()  UTC()  LocalTime('Paris')  TimeZone()
```

**Format codes:** `YYYY` · `YY` · `M` · `MM` · `MMM` (Jan) · `MMMM` (January) · `D` · `DD` · `WWW` (Mon) · `WWWW` (Monday) · `h` · `hh` · `mm` (minutes) · `ss` · `fff` · `TT` (AM/PM).

**Example**

```qlik
LOAD Date(Floor(Timestamp#(EventTS, 'YYYY-MM-DD hh:mm:ss')), 'YYYY-MM-DD') AS EventDate FROM ...;
Calendar:
LOAD Date AS OrderDate, Year(Date) AS Year, 'Q' & Ceil(Month(Date) / 3) AS Quarter, Month(Date) AS Month, Week(Date) AS Week;
LOAD Date($(vMin) + RecNo() - 1) AS Date AUTOGENERATE $(vMax) - $(vMin) + 1;
```

**Check**

- In Qlik format codes, `MM` is the month and `mm` is minutes. Qlik dates are numbers (days since 1899-12-30) with a text representation (dual values).
- There is no `Quarter()` function. Build the quarter with `Ceil(Month(d)/3)`, or use `QuarterName(d)`.

### Number and math functions (Qlik functions)

**Syntax**

```qlik
Round(x [, step [, offset]])  Floor(x [, step])  Ceil(x [, step])  Fabs(x)  Fmod(a, b)  Mod(a, b)  Div(a, b)  Sign(x)  Sqrt(x)  Pow(x, y)  Exp(x)  Log(x)  Log10(x)
Rand()  Pi()  e()  Even(n)  Odd(n)  Frac(x)  BitCount(n)  Combin(n, k)  Permut(n, k)  Fact(n)
RangeSum(a, b, ...)  RangeAvg  RangeMin  RangeMax  RangeCount  RangeNullCount  RangeOnly  RangeConcat   // nulls treated as 0 in RangeSum
Num(x, '#,##0.00')  Money(x [, 'fmt'])
```

**Check**

- `Round(x, 0.01)` rounds to two decimals: the second argument is a STEP, not a number of digits.
- `a + b` is null if either value is null. `RangeSum(a, b)` treats nulls as 0.

### File and system functions (Qlik script functions)

**Syntax**

```qlik
FileList('lib://Data/*.csv')  DirList('lib://Data/*')  FileSize(path)  FileTime(path)  FileName()  FilePath()  FileBaseName()  FileExtension()  FileDir()
QvdCreateTime(path)  QvdNoOfRecords(path)  QvdNoOfFields(path)  QvdFieldName(path, n)  QvdTableName(path)
ReloadTime()  DocumentName()  DocumentPath()  DocumentTitle()  OSUser()  QlikViewVersion() / ProductVersion()  ComputerName()  GetFolderPath()
```

## Chart expressions

### Chart aggregation and set analysis (Qlik expression syntax)

**Syntax**

```qlik
Sum(Sales)
Sum({<Year={2024}, Region={'North', 'South'}>} Sales)              // filter Year and Region, ignore their selections
Sum({$<Year={$(=Max(Year))}>} Sales)                                // $ = current selections (default)
Sum({1<Product={'A*'}>} Sales)                                      // 1 = ignore all selections
Sum({<Year=, Month=>} Sales)                                        // clear the selections in Year and Month
Sum({<Customer=P({<Year={2024}>} Customer)>} Sales)                // P() possible / E() excluded values
Sum({<Sales={">1000"}>} Sales)  Sum({<Date={">=$(=YearStart(Today()))<=$(=Today())"}>} Sales)   // search strings
Sum({<Region-={'West'}>} Sales)   {<F+={...}>}  {<F*={...}>}  {<F/={...}>}                     // exclude / union / intersect / symmetric
Sum({$*BM01} Sales)  {1-$}  {$+1}                                   // bookmarks and set operators
Count(DISTINCT CustomerID)  Avg(Aggr(Sum(Sales), Customer))        // nested: Aggr over a dimension
Sum(TOTAL Sales)  Sum(TOTAL <Region> Sales)                         // ignore the chart dimensions (all / except Region)
Rank(Sum(Sales))  Above(Sum(Sales) [, offset [, count]])  Below()  Top()  Bottom()  RowNo([TOTAL])  ColumnNo()  Dimensionality()
GetSelectedCount(Field)  GetFieldSelections(Field [, sep [, max]])  GetCurrentSelections()  Only(Field)  Concat(DISTINCT Field, ', ')
```

**Check**

- A set expression goes INSIDE the aggregation, before the field: `Sum({<…>} Sales)`, not `{<…>} Sum(Sales)`.
- Text values in set modifiers need quotes (`{'North'}`), numbers do not, and search strings use double quotes (`{">100"}`).
- Write `Sum(Aggr(...))`: Aggr returns an array, and needs an outer aggregation in most charts.
