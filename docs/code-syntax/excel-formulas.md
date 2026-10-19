# Excel formulas syntax reference (Phoenix)

For Phoenix models writing and checking Microsoft Excel worksheet formulas (Excel 365 and 2021+, with version notes for older Excel). Part 1 covers formula syntax and the most used functions in depth. Part 2 is a catalog of every worksheet function by category, with its exact arguments.

**Notation:** `[arg]` optional · `...` repeatable · **365** = Microsoft 365 / Excel 2021+ only (dynamic arrays) · **2019** = Excel 2019+. Arguments are separated by commas in English Excel. Many locales use semicolons; function names are translated in non-English Excel.

## Essentials

Target: Excel for Microsoft 365 (dynamic arrays: FILTER, XLOOKUP, LET). Say so when a formula needs 365 / 2021+, and give the classic form (INDEX/MATCH, SUMPRODUCT) when the user names an older Excel.

| Never | Write instead |
|---|---|
| `=A1 == B1`, `!=`, `&&`, `\|\|` | `=A1=B1`, `<>`, `AND(...)`, `OR(...)` |
| `=VLOOKUP(x, A:B, 2)` (approximate, needs sorted data) | `=VLOOKUP(x, A:B, 2, FALSE)` or `=XLOOKUP(x, A:A, B:B, "not found")` |
| `=SUMIF(A:A, ">B1", C:C)` | `=SUMIF(A:A, ">"&B1, C:C)` (criteria are text) |
| `=SUMIFS(A:A, C:C)` with the sum range last | `=SUMIFS(sum_range, criteria_range1, criteria1, ...)` (sum range FIRST) |
| dates as text `"2024-01-31"` | `DATE(2024,1,31)`; show with `TEXT(d, "yyyy-mm-dd")` |
| `'text'` in single quotes | `"text"` (`""` for a quote inside); single quotes only around sheet names: `'My Sheet'!A1` |
| `=IF(A1>0, "pos", IF(A1<0, "neg"))` missing else | `=IFS(A1>0,"pos", A1<0,"neg", TRUE,"zero")` or full nested IFs |
| `IFERROR` around everything | `IFNA` for lookups; let real errors show |

Always: one formula per answer cell, starting with `=`; every `(` closed; ranges in SUMIFS / COUNTIFS of equal size. Dynamic arrays spill: leave the cells below / right empty (`#SPILL!` otherwise).

## Recipes

### Look up a value by key, with a default (XLOOKUP, INDEX/MATCH)

**Example**

```excel
=XLOOKUP(E2, Products[SKU], Products[Price], "not found", 0)
=IFNA(INDEX(Products!C:C, MATCH(E2, Products!A:A, 0)), "not found")
```

### Sum and count with several conditions (SUMIFS, COUNTIFS)

**Example**

```excel
=SUMIFS(Sales[Amount], Sales[Region], "North", Sales[Date], ">="&DATE(2024,1,1), Sales[Date], "<"&DATE(2025,1,1))
=COUNTIFS(Sales[Region], G2, Sales[Amount], ">"&H2)
```

### Filter and sort rows that match (FILTER, SORT, UNIQUE)

**Example**

```excel
=SORT(FILTER(Sales, Sales[Amount]>1000, "none"), 3, -1)
=SORT(UNIQUE(Sales[Customer]))
```

### Text: split, join and clean (TEXTSPLIT, TEXTJOIN, TRIM, SUBSTITUTE)

**Example**

```excel
=TEXTJOIN(", ", TRUE, FILTER(Staff[Name], Staff[Dept]=A2))
=TRIM(SUBSTITUTE(A2, CHAR(160), " "))
=TEXTBEFORE(A2, "@")
```

### Dates: month start and end, age, working days (EOMONTH, DATEDIF, NETWORKDAYS)

**Example**

```excel
=EOMONTH(A2, -1) + 1
=EOMONTH(A2, 0)
=DATEDIF(B2, TODAY(), "y")
=NETWORKDAYS(C2, D2, Holidays[Date])
```

### Name intermediate results (LET)

**Example**

```excel
=LET(total, SUM(Sales[Amount]), north, SUMIFS(Sales[Amount], Sales[Region], "North"), IF(total=0, 0, north/total))
```

### Rank and top N (RANK.EQ, LARGE, TAKE)

**Example**

```excel
=RANK.EQ(B2, $B$2:$B$100, 0)
=LARGE(Sales[Amount], 3)
=TAKE(SORT(Sales, 3, -1), 5)
```

## Rules: before you answer with an Excel formula

- A formula starts with `=`. Every `(` has its matching `)`. Text values are in "double quotes", with `""` for a quote inside. Numbers, TRUE/FALSE and cell references take no quotes.
- References: `A1` is relative, `$A$1` absolute, `$A1` and `A$1` mixed, `Sheet2!A1`, `'My Sheet'!A1` (quote names with spaces), `[Book.xlsx]Sheet1!A1`, `Table1[Column]`, `Table1[@Column]` (this row), and a whole column `A:A`.
- Use the right operators: `&` joins text, `<>` is not-equal, `^` is power, and `%` is percent. There are no `==`, `!=`, `&&` or `||`. Use `AND()` and `OR()`, or `*` and `+` for array logic.
- `VLOOKUP` needs `FALSE` (or 0) as the 4th argument for an exact match. Without it, the lookup does an approximate match on data that must be sorted, and returns WRONG results on unsorted data.
- Dynamic array functions (`FILTER`, `SORT`, `UNIQUE`, `SEQUENCE`, `XLOOKUP` returning ranges, `LET`, `LAMBDA`, `TEXTSPLIT`) need Excel 365 or 2021+. They spill into neighbor cells, and give `#SPILL!` if those cells are not empty.
- Dates are serial numbers (1 = 1900-01-01). Build them with `DATE(y,m,d)`, not the text `"2024-01-31"` (whose meaning depends on the locale). Format with `TEXT(d, "yyyy-mm-dd")` only for display.
- In `TEXT` format codes, `m` and `mm` after `h` mean minutes, otherwise month. In `"yyyy-mm-dd hh:mm"`, Excel reads the second `mm` as minutes.
- Criteria in `SUMIFS`, `COUNTIFS` and `AVERAGEIFS` are text: `">100"`, `"<>"&A1`, `"*apple*"`. Comparing to a cell needs concatenation: `">"&B1`, not `">B1"`.
- `SUMIFS` takes the sum range FIRST, while `SUMIF` takes it LAST. All the ranges in `SUMIFS` must have the same size.
- Wrap lookups in `IFERROR` or `IFNA` only when a missing value is expected. Otherwise the wrapper hides real errors.
- Avoid volatile functions in big sheets: `OFFSET`, `INDIRECT`, `TODAY`, `NOW`, `RAND` and `RANDBETWEEN` recalculate on every change.
- Errors: `#N/A` not found · `#VALUE!` wrong type · `#REF!` invalid reference · `#DIV/0!` · `#NAME?` unknown function or name, or text without quotes · `#NUM!` · `#NULL!` · `#SPILL!` · `#CALC!` (an empty array from FILTER, for example).

## Formula syntax

### Operators and precedence (Excel formula syntax)

**Syntax**

```excel
:  (range A1:B5)   ,  (union, inside functions)   space (intersection)   -   %   ^   * /   + -   &   = <> < > <= >=
=A1+B1   =A1*1.2   =A1^2   =A1&" "&B1   =A1>=10   =-A1^2  (= 25 when A1=5: negation before ^ !)
=SUM(A1:A10)  =SUM(Sheet2!B:B)  =Table1[@Qty]*Table1[@Price]  =A1#  (the spill range of A1, 365)  =@A1:A10 (implicit intersection)
```

**Check**

- `=-2^2` is 4 in Excel, because negation is applied before `^`. Write `=-(2^2)` for -4.
- Comparisons of text are case-insensitive: `="a"="A"` is TRUE. Use `EXACT()` for a case-sensitive comparison.

### Names, LET and LAMBDA (Excel formula syntax, 365)

**Syntax**

```excel
=LET(name1, value1, [name2, value2, ...], calculation)
=LAMBDA([parameter1, parameter2, ...], calculation)          ' define in Name Manager as e.g. TAX, use =TAX(A1)
=LAMBDA(x, x*2)(5)                                            ' call inline → 10
```

**Arguments of LET**

- `name1`: the first name. It must start with a letter, and cannot look like a cell reference.
- `value1`: the value or calculation assigned to `name1`.
- `calculation`: the final result, which can use every name. It is the last argument.

**Example**

```excel
=LET(sales, B2:B100, cost, C2:C100, margin, sales-cost, SUM(margin)/SUM(sales))
```

**Check**

- In LET, the names and values come in pairs, and the last argument is the result. An even number of arguments is an error.

## Key functions in depth

### IF, IFS, SWITCH, AND, OR, NOT, XOR, IFERROR, IFNA (Excel logical functions)

**Syntax**

```excel
=IF(logical_test, [value_if_true], [value_if_false])
=IFS(logical_test1, value1, [logical_test2, value2], ...)                      ' 2019; use TRUE, x as the final else
=SWITCH(expression, value1, result1, [value2, result2], ..., [default])         ' 2019
=AND(logical1, [logical2], ...)  =OR(logical1, ...)  =NOT(logical)  =XOR(logical1, ...)
=IFERROR(value, value_if_error)   =IFNA(value, value_if_na)
```

**Example**

```excel
=IF(A2>=90, "A", IF(A2>=80, "B", IF(A2>=70, "C", "F")))
=IFS(A2>=90, "A", A2>=80, "B", A2>=70, "C", TRUE, "F")
=IF(AND(B2>0, C2="Yes"), B2*0.1, 0)
```

**Check**

- `IFS` returns `#N/A` when no condition is true. End it with `TRUE, default`.
- `=IF(A1=1 OR 2, …)` is invalid. Write `=IF(OR(A1=1, A1=2), …)`.

### XLOOKUP (Excel lookup function, 365/2021)

**Syntax**

```excel
=XLOOKUP(lookup_value, lookup_array, return_array, [if_not_found], [match_mode], [search_mode])
```

**Arguments**

- `lookup_value`: the value to find.
- `lookup_array`: the one-column or one-row range to search.
- `return_array`: the range to return from, the same size as `lookup_array`. It can be several columns.
- `if_not_found`: returned when there is no match, instead of `#N/A`.
- `match_mode`: `0` exact (the default), `-1` exact or next smaller, `1` exact or next larger, `2` wildcard (`*`, `?`, `~`), `3` regex (365 only).
- `search_mode`: `1` first to last (the default), `-1` last to first, `2` binary search ascending, `-2` binary search descending.

**Example**

```excel
=XLOOKUP(E2, Products[SKU], Products[Price], "Not found")
=XLOOKUP(E2, A2:A100, B2:D100)                   ' returns 3 columns (spills)
=XLOOKUP(MAX(Dates), Dates, Values, , 0, -1)      ' last match
```

**Check**

- `lookup_array` and `return_array` must have the same number of rows (or columns), or the result is `#VALUE!`.

### VLOOKUP, HLOOKUP (Excel lookup functions)

**Syntax**

```excel
=VLOOKUP(lookup_value, table_array, col_index_num, [range_lookup])
=HLOOKUP(lookup_value, table_array, row_index_num, [range_lookup])
```

**Arguments**

- `lookup_value`: searched in the FIRST column of `table_array`.
- `table_array`: the range that contains the key column and the result columns.
- `col_index_num`: the column number to return, counted from 1 inside `table_array`.
- `range_lookup`: `FALSE` or `0` for an exact match (almost always what you want). `TRUE` or omitted gives an approximate match, which requires the first column sorted ascending.

**Example**

```excel
=VLOOKUP(A2, Customers!$A$2:$D$500, 3, FALSE)
```

**Check**

- Lock `table_array` with `$` before filling the formula down.
- VLOOKUP cannot return columns to the LEFT of the key. Use XLOOKUP or INDEX/MATCH.
- A hard-coded `col_index_num` breaks when columns are inserted.

### INDEX, MATCH, XMATCH (Excel lookup functions)

**Syntax**

```excel
=INDEX(array, row_num, [column_num])                 =INDEX(reference, row_num, [column_num], [area_num])
=MATCH(lookup_value, lookup_array, [match_type])     ' match_type: 1 (default) largest <= (sorted asc), 0 exact, -1 smallest >= (sorted desc)
=XMATCH(lookup_value, lookup_array, [match_mode], [search_mode])   ' 365; modes like XLOOKUP
```

**Example**

```excel
=INDEX(C2:C100, MATCH(F2, A2:A100, 0))                        ' left/right lookup
=INDEX(B2:E20, MATCH(H2, A2:A20, 0), MATCH(H3, B1:E1, 0))     ' 2-way lookup
```

**Check**

- MATCH defaults to `1` (approximate). Always pass `0` for an exact match.
- INDEX's `row_num` is relative to the array, not the sheet.

### SUMIFS, COUNTIFS, AVERAGEIFS, MAXIFS, MINIFS, SUMIF, COUNTIF (Excel conditional aggregation)

**Syntax**

```excel
=SUMIFS(sum_range, criteria_range1, criteria1, [criteria_range2, criteria2], ...)
=COUNTIFS(criteria_range1, criteria1, [criteria_range2, criteria2], ...)
=AVERAGEIFS(average_range, criteria_range1, criteria1, ...)
=MAXIFS(max_range, criteria_range1, criteria1, ...)   =MINIFS(min_range, criteria_range1, criteria1, ...)    ' 2019
=SUMIF(range, criteria, [sum_range])   =COUNTIF(range, criteria)   =AVERAGEIF(range, criteria, [average_range])
```

**Criteria forms:** `"North"` (equals) · `"<>North"` · `">100"` · `">="&B1` · `"*berry*"` (contains) · `"A?"` · `""` (blank) · `"<>"` (not blank) · `B1` (equals the cell) · `TODAY()` · `">"&DATE(2024,1,1)`.

**Example**

```excel
=SUMIFS(Sales[Amount], Sales[Region], "North", Sales[Date], ">="&DATE(2024,1,1), Sales[Date], "<"&DATE(2025,1,1))
=COUNTIFS(A:A, "<>", B:B, ">0")
```

**Check**

- All the ranges must have the same shape. `">"&A1` is right, while `">A1"` compares against the text "A1".
- In SUMIFS, criteria pairs are combined with AND. For OR, add several SUMIFS together, or use `SUM(SUMIFS(..., {"A","B"}))`.

### FILTER, SORT, SORTBY, UNIQUE, SEQUENCE, RANDARRAY (Excel dynamic array functions, 365/2021)

**Syntax**

```excel
=FILTER(array, include, [if_empty])                     ' include: a TRUE/FALSE array; AND = (c1)*(c2), OR = (c1)+(c2)
=SORT(array, [sort_index], [sort_order], [by_col])       ' sort_order 1 asc, -1 desc
=SORTBY(array, by_array1, [sort_order1], [by_array2, sort_order2], ...)
=UNIQUE(array, [by_col], [exactly_once])
=SEQUENCE(rows, [columns], [start], [step])
=RANDARRAY([rows], [columns], [min], [max], [integer])
```

**Example**

```excel
=FILTER(A2:D100, (C2:C100="North")*(D2:D100>1000), "No rows")
=SORT(UNIQUE(B2:B100))
=SORTBY(A2:B50, B2:B50, -1)
=SEQUENCE(12, 1, DATE(2024,1,1), 31)        ' then use EOMONTH for exact month starts
```

**Check**

- FILTER returns `#CALC!` when nothing matches. Always give `if_empty`.
- The spilled result needs empty cells below and to the right of it. Refer to the whole spill as `F2#`.

### TEXT, VALUE, CONCAT, TEXTJOIN, TEXTSPLIT, TEXTBEFORE, TEXTAFTER (Excel text functions)

**Syntax**

```excel
=TEXT(value, format_text)                    ' "0.00", "#,##0", "0%", "$#,##0.00", "yyyy-mm-dd", "dd/mm/yyyy", "mmm yyyy", "dddd", "hh:mm:ss", "[h]:mm"
=VALUE(text)   =NUMBERVALUE(text, [decimal_separator], [group_separator])
=CONCAT(text1, [text2], ...)   =TEXTJOIN(delimiter, ignore_empty, text1, [text2], ...)     ' 2019
=TEXTSPLIT(text, col_delimiter, [row_delimiter], [ignore_empty], [match_mode], [pad_with])  ' 365
=TEXTBEFORE(text, delimiter, [instance_num], [match_mode], [match_end], [if_not_found])     ' 365
=TEXTAFTER(text, delimiter, [instance_num], [match_mode], [match_end], [if_not_found])      ' 365
```

**Example**

```excel
="Total: "&TEXT(SUM(B2:B10), "$#,##0.00")
=TEXTJOIN(", ", TRUE, FILTER(A2:A50, B2:B50="Yes"))
=TEXTBEFORE(A2, "@")             ' user part of an email
```

**Check**

- `TEXT` returns text, so the result cannot be summed. Keep the number in its cell, and use number formatting.
- The format codes depend on the language in non-English Excel (for example `"JJJJ-MM-TT"` in German).

### Date functions in depth: DATE, EDATE, EOMONTH, DATEDIF, NETWORKDAYS, WORKDAY (Excel date functions)

**Syntax**

```excel
=DATE(year, month, day)            ' month/day overflow rolls: DATE(2024,13,1) = 2025-01-01
=EDATE(start_date, months)         =EOMONTH(start_date, months)     ' end of month; EOMONTH(d,-1)+1 = first of month
=DATEDIF(start_date, end_date, unit)  ' unit "Y" "M" "D" "YM" "YD" "MD" (undocumented, works)
=NETWORKDAYS(start_date, end_date, [holidays])   =NETWORKDAYS.INTL(start, end, [weekend], [holidays])
=WORKDAY(start_date, days, [holidays])           =WORKDAY.INTL(start, days, [weekend], [holidays])
=TODAY()  =NOW()  =YEAR(d)  =MONTH(d)  =DAY(d)  =WEEKDAY(d, [return_type])  =WEEKNUM(d, [return_type])  =ISOWEEKNUM(d)
```

**Example**

```excel
=DATEDIF(B2, TODAY(), "Y")                  ' age in whole years
=EOMONTH(A2, 0)                              ' last day of A2's month
=WORKDAY(A2, 10, Holidays!A:A)
```

**Check**

- `DATEDIF` gives `#NUM!` when the start is after the end.
- `WEEKDAY` defaults to type 1, where Sunday is 1. Pass 2 to make Monday 1.

### SUMPRODUCT and array math (Excel functions)

**Syntax**

```excel
=SUMPRODUCT(array1, [array2], [array3], ...)
=SUMPRODUCT((A2:A100="North")*(B2:B100>100)*C2:C100)        ' conditional sum, works in all versions
=SUM((A2:A100="North")*C2:C100)                              ' 365 (or Ctrl+Shift+Enter in old Excel)
```

**Check**

- Booleans need math to become numbers: `--(A2:A10="x")` or `(cond)*1`.

## Function catalog

### Lookup and reference functions (Excel catalog)

- `ADDRESS(row_num, column_num, [abs_num], [a1], [sheet_text])`: a cell address as text.
- `AREAS(reference)`: the number of areas in a reference.
- `CHOOSE(index_num, value1, [value2], ...)`: the value at a position.
- `CHOOSECOLS(array, col_num1, [col_num2], ...)` **365**: the chosen columns.
- `CHOOSEROWS(array, row_num1, ...)` **365**: the chosen rows.
- `COLUMN([reference])`: the column number.
- `COLUMNS(array)`: the number of columns.
- `DROP(array, rows, [columns])` **365**: removes rows or columns from the start (positive) or the end (negative).
- `EXPAND(array, rows, [columns], [pad_with])` **365**: grows an array.
- `FILTER(array, include, [if_empty])` **365**.
- `FORMULATEXT(reference)`: a cell's formula as text.
- `GETPIVOTDATA(data_field, pivot_table, [field1, item1], ...)`: a value from a PivotTable.
- `HLOOKUP(lookup_value, table_array, row_index_num, [range_lookup])`.
- `HSTACK(array1, [array2], ...)` **365**: joins arrays side by side.
- `HYPERLINK(link_location, [friendly_name])`: a clickable link.
- `INDEX(array, row_num, [column_num])`.
- `INDIRECT(ref_text, [a1])`: a reference from text (volatile).
- `LOOKUP(lookup_value, lookup_vector, [result_vector])`: approximate lookup in sorted data.
- `MATCH(lookup_value, lookup_array, [match_type])`.
- `OFFSET(reference, rows, cols, [height], [width])`: a shifted reference (volatile).
- `ROW([reference])`.
- `ROWS(array)`.
- `SORT(array, [sort_index], [sort_order], [by_col])` **365**.
- `SORTBY(array, by_array1, [sort_order1], ...)` **365**.
- `TAKE(array, rows, [columns])` **365**: the first rows or columns (or the last, when negative).
- `TOCOL(array, [ignore], [scan_by_column])` **365**: flattens to one column. `ignore` is 0 keep, 1 blanks, 2 errors, 3 both.
- `TOROW(array, [ignore], [scan_by_column])` **365**.
- `TRANSPOSE(array)`.
- `UNIQUE(array, [by_col], [exactly_once])` **365**.
- `VLOOKUP(lookup_value, table_array, col_index_num, [range_lookup])`.
- `VSTACK(array1, [array2], ...)` **365**: stacks arrays vertically.
- `WRAPCOLS(vector, wrap_count, [pad_with])` **365**.
- `WRAPROWS(vector, wrap_count, [pad_with])` **365**.
- `XLOOKUP(lookup_value, lookup_array, return_array, [if_not_found], [match_mode], [search_mode])` **365/2021**.
- `XMATCH(lookup_value, lookup_array, [match_mode], [search_mode])` **365/2021**.
- `GROUPBY(row_fields, values, function, [field_headers], [total_depth], [sort_order], [filter_array], [field_relationship])` **365**: a summary like a pivot.
- `PIVOTBY(row_fields, col_fields, values, function, [field_headers], [row_total_depth], [row_sort_order], [col_total_depth], [col_sort_order], [filter_array], [relative_to])` **365**.
- `TRIMRANGE(range, [trim_rows], [trim_cols])` **365**: trims blank edges (also `A:.A` dot syntax).
- `IMAGE(source, [alt_text], [sizing], [height], [width])` **365**.

### Math functions (Excel catalog)

- `ABS(number)`: absolute value.
- `ACOS(number)`, `ACOSH`, `ACOT`, `ACOTH`, `ASIN`, `ASINH`, `ATAN`, `ATAN2(x_num, y_num)`, `ATANH`, `COS`, `COSH`, `COT`, `COTH`, `CSC`, `CSCH`, `SEC`, `SECH`, `SIN`, `SINH`, `TAN`, `TANH`: trigonometry in radians.
- `AGGREGATE(function_num, options, ref1, [ref2], ...)`: SUM, AVERAGE and others, ignoring errors or hidden rows (`options` 6 = ignore errors).
- `ARABIC(text)` and `ROMAN(number, [form])`: Roman numerals.
- `BASE(number, radix, [min_length])` and `DECIMAL(text, radix)`.
- `CEILING.MATH(number, [significance], [mode])`, `CEILING(number, significance)`: round up to a multiple.
- `COMBIN(number, number_chosen)`, `COMBINA(number, number_chosen)`: combinations.
- `DEGREES(angle)`, `RADIANS(angle)`.
- `EVEN(number)`, `ODD(number)`: round up to the next even or odd number.
- `EXP(number)`: e^x.
- `FACT(number)`, `FACTDOUBLE(number)`.
- `FLOOR.MATH(number, [significance], [mode])`, `FLOOR(number, significance)`: round down to a multiple.
- `GCD(number1, ...)`, `LCM(number1, ...)`.
- `INT(number)`: round down to an integer (`INT(-1.5)` = -2).
- `LN(number)`, `LOG(number, [base])`, `LOG10(number)`.
- `MDETERM(array)`, `MINVERSE(array)`, `MMULT(array1, array2)`, `MUNIT(dimension)`: matrices.
- `MOD(number, divisor)`: the remainder, with the sign of the divisor.
- `MROUND(number, multiple)`: round to the nearest multiple.
- `MULTINOMIAL(number1, ...)`.
- `PI()`.
- `POWER(number, power)`.
- `PRODUCT(number1, ...)`.
- `QUOTIENT(numerator, denominator)`: the integer part of a division.
- `RAND()`, `RANDBETWEEN(bottom, top)`, `RANDARRAY(...)` **365**: random values (volatile).
- `ROUND(number, num_digits)`, `ROUNDDOWN(number, num_digits)`, `ROUNDUP(number, num_digits)`: a negative `num_digits` rounds to tens, hundreds and so on.
- `SEQUENCE(rows, [columns], [start], [step])` **365**.
- `SERIESSUM(x, n, m, coefficients)`.
- `SIGN(number)`.
- `SQRT(number)`, `SQRTPI(number)`.
- `SUBTOTAL(function_num, ref1, ...)`: 1-11 include hidden rows, 101-111 ignore them. Always ignores rows hidden by a filter.
- `SUM(number1, [number2], ...)`.
- `SUMIF(range, criteria, [sum_range])`, `SUMIFS(sum_range, criteria_range1, criteria1, ...)`.
- `SUMPRODUCT(array1, ...)`.
- `SUMSQ(number1, ...)`, `SUMX2MY2(array_x, array_y)`, `SUMX2PY2`, `SUMXMY2`.
- `TRUNC(number, [num_digits])`: cut off the decimals (`TRUNC(-1.5)` = -1).

### Statistical functions (Excel catalog)

- `AVEDEV(number1, ...)`.
- `AVERAGE(number1, ...)` ignores text and blanks. `AVERAGEA` counts text as 0.
- `AVERAGEIF(range, criteria, [average_range])`, `AVERAGEIFS(average_range, criteria_range1, criteria1, ...)`.
- `COUNT(value1, ...)` counts numbers. `COUNTA(value1, ...)` counts non-empty cells. `COUNTBLANK(range)`. `COUNTIF(range, criteria)`. `COUNTIFS(criteria_range1, criteria1, ...)`.
- `CORREL(array1, array2)`, `PEARSON(array1, array2)`, `RSQ(known_ys, known_xs)`, `COVARIANCE.P(array1, array2)`, `COVARIANCE.S`.
- `DEVSQ(number1, ...)`.
- `FORECAST.LINEAR(x, known_ys, known_xs)`, `FORECAST(x, known_ys, known_xs)`, `FORECAST.ETS(target_date, values, timeline, [seasonality], [data_completion], [aggregation])`, `FORECAST.ETS.CONFINT`, `FORECAST.ETS.SEASONALITY`, `FORECAST.ETS.STAT`.
- `FREQUENCY(data_array, bins_array)`.
- `GEOMEAN(number1, ...)`, `HARMEAN(number1, ...)`.
- `GROWTH(known_ys, [known_xs], [new_xs], [const])`, `LOGEST(...)`, `TREND(known_ys, [known_xs], [new_xs], [const])`, `LINEST(known_ys, [known_xs], [const], [stats])`.
- `INTERCEPT(known_ys, known_xs)`, `SLOPE(known_ys, known_xs)`, `STEYX(known_ys, known_xs)`.
- `KURT(number1, ...)`, `SKEW(number1, ...)`, `SKEW.P`.
- `LARGE(array, k)`, `SMALL(array, k)`: the k-th largest or smallest.
- `MAX(number1, ...)`, `MAXA`, `MAXIFS(max_range, criteria_range1, criteria1, ...)`, `MIN`, `MINA`, `MINIFS`.
- `MEDIAN(number1, ...)`, `MODE.SNGL(number1, ...)`, `MODE.MULT(number1, ...)`.
- `PERCENTILE.INC(array, k)`, `PERCENTILE.EXC(array, k)`, `QUARTILE.INC(array, quart)`, `QUARTILE.EXC`, `PERCENTRANK.INC(array, x, [significance])`, `PERCENTRANK.EXC`.
- `PERMUT(number, number_chosen)`, `PERMUTATIONA`.
- `RANK.EQ(number, ref, [order])`, `RANK.AVG(number, ref, [order])`. `order` is 0 for descending (the default) and 1 for ascending.
- `STANDARDIZE(x, mean, standard_dev)`.
- `STDEV.S(number1, ...)` for a sample, `STDEV.P` for a population, `STDEVA`, `STDEVPA`, `VAR.S`, `VAR.P`, `VARA`, `VARPA`.
- `TRIMMEAN(array, percent)`.
- Distributions: `NORM.DIST(x, mean, standard_dev, cumulative)`, `NORM.INV(probability, mean, standard_dev)`, `NORM.S.DIST(z, cumulative)`, `NORM.S.INV(probability)`, `BINOM.DIST(number_s, trials, probability_s, cumulative)`, `BINOM.INV`, `POISSON.DIST(x, mean, cumulative)`, `EXPON.DIST(x, lambda, cumulative)`, `T.DIST(x, deg_freedom, cumulative)`, `T.DIST.2T`, `T.DIST.RT`, `T.INV`, `T.INV.2T`, `T.TEST(array1, array2, tails, type)`, `CHISQ.DIST`, `CHISQ.INV`, `CHISQ.TEST(actual_range, expected_range)`, `F.DIST`, `F.INV`, `F.TEST`, `GAMMA.DIST`, `GAMMA.INV`, `GAMMA(x)`, `GAMMALN`, `BETA.DIST`, `BETA.INV`, `LOGNORM.DIST`, `LOGNORM.INV`, `WEIBULL.DIST`, `HYPGEOM.DIST`, `NEGBINOM.DIST`, `CONFIDENCE.NORM(alpha, standard_dev, size)`, `CONFIDENCE.T`, `Z.TEST(array, x, [sigma])`, `FISHER(x)`, `FISHERINV(y)`, `PHI(x)`, `GAUSS(z)`, `PROB(x_range, prob_range, lower_limit, [upper_limit])`.

### Text functions (Excel catalog)

- `ARRAYTOTEXT(array, [format])` **365**, `VALUETOTEXT(value, [format])` **365**.
- `CHAR(number)`, `CODE(text)`, `UNICHAR(number)`, `UNICODE(text)`. `CHAR(10)` is a line break (turn on Wrap Text).
- `CLEAN(text)`: removes non-printable characters. `TRIM(text)`: removes extra spaces, but not the non-breaking `CHAR(160)`.
- `CONCAT(text1, ...)` **2019**, `CONCATENATE(text1, ...)` (legacy), `TEXTJOIN(delimiter, ignore_empty, text1, ...)` **2019**.
- `DOLLAR(number, [decimals])`, `FIXED(number, [decimals], [no_commas])`, `TEXT(value, format_text)`.
- `EXACT(text1, text2)`: case-sensitive equality.
- `FIND(find_text, within_text, [start_num])` is case-sensitive. `SEARCH(find_text, within_text, [start_num])` is case-insensitive and allows wildcards. Both return `#VALUE!` when the text is not found.
- `LEFT(text, [num_chars])`, `RIGHT(text, [num_chars])`, `MID(text, start_num, num_chars)`. Positions are 1-based.
- `LEN(text)`.
- `LOWER(text)`, `UPPER(text)`, `PROPER(text)`.
- `NUMBERVALUE(text, [decimal_separator], [group_separator])`, `VALUE(text)`.
- `REPLACE(old_text, start_num, num_chars, new_text)` replaces by position. `SUBSTITUTE(text, old_text, new_text, [instance_num])` replaces by text.
- `REPT(text, number_times)`.
- `T(value)`: returns the value if it is text, else `""`.
- `TEXTAFTER(...)` **365**, `TEXTBEFORE(...)` **365**, `TEXTSPLIT(text, col_delimiter, [row_delimiter], [ignore_empty], [match_mode], [pad_with])` **365**.
- `REGEXTEST(text, pattern, [case_sensitivity])` **365**, `REGEXEXTRACT(text, pattern, [return_mode], [case_sensitivity])` **365**, `REGEXREPLACE(text, pattern, replacement, [occurrence], [case_sensitivity])` **365**.
- `TRANSLATE(text, [source_language], [target_language])` **365**, `DETECTLANGUAGE(text)` **365**.
- `ASC(text)`, `DBCS(text)`, `BAHTTEXT(number)`, `PHONETIC(reference)`.

### Date and time functions (Excel catalog)

- `DATE(year, month, day)`, `TIME(hour, minute, second)`.
- `DATEVALUE(date_text)`, `TIMEVALUE(time_text)`: turn text into a date or time serial.
- `DAY(serial)`, `MONTH(serial)`, `YEAR(serial)`, `HOUR(serial)`, `MINUTE(serial)`, `SECOND(serial)`.
- `DAYS(end_date, start_date)`, `DAYS360(start_date, end_date, [method])`, `DATEDIF(start_date, end_date, unit)`.
- `EDATE(start_date, months)`, `EOMONTH(start_date, months)`.
- `ISOWEEKNUM(date)`, `WEEKNUM(serial, [return_type])`, `WEEKDAY(serial, [return_type])`.
- `NETWORKDAYS(start, end, [holidays])`, `NETWORKDAYS.INTL(start, end, [weekend], [holidays])`. `weekend` is a code 1-17 or a string like `"0000011"`.
- `WORKDAY(start, days, [holidays])`, `WORKDAY.INTL(start, days, [weekend], [holidays])`.
- `NOW()`, `TODAY()` (both volatile).
- `YEARFRAC(start_date, end_date, [basis])`.

### Logical and information functions (Excel catalog)

- `AND`, `OR`, `NOT`, `XOR`, `IF`, `IFS` **2019**, `SWITCH` **2019**, `IFERROR`, `IFNA`, `TRUE()`, `FALSE()`.
- `LET(name1, value1, ..., calculation)` **365**, `LAMBDA([params], calculation)` **365**.
- `BYROW(array, lambda)` **365**, `BYCOL(array, lambda)` **365**, `MAP(array1, [array2], ..., lambda)` **365**, `REDUCE([initial_value], array, lambda(accumulator, value))` **365**, `SCAN([initial_value], array, lambda)` **365**, `MAKEARRAY(rows, cols, lambda(r, c))` **365**, `ISOMITTED(argument)` **365**.
- `CELL(info_type, [reference])`, `INFO(type_text)`, `ERROR.TYPE(error_val)`, `TYPE(value)`, `SHEET([value])`, `SHEETS([reference])`, `N(value)`, `NA()`.
- `ISBLANK(value)`, `ISERR(value)` (any error except #N/A), `ISERROR(value)`, `ISNA(value)`, `ISNUMBER(value)`, `ISTEXT(value)`, `ISNONTEXT(value)`, `ISLOGICAL(value)`, `ISREF(value)`, `ISFORMULA(reference)`, `ISEVEN(number)`, `ISODD(number)`.

**Example**

```excel
=BYROW(B2:E10, LAMBDA(r, SUM(r)))
=MAP(A2:A10, B2:B10, LAMBDA(a, b, IF(a>b, a, b)))
=REDUCE(0, A2:A10, LAMBDA(acc, v, acc + IF(v>0, v, 0)))
```

**Check**

- `ISBLANK` is FALSE for a cell that holds a formula returning `""`. Test with `A1=""` instead.

### Financial functions (Excel catalog)

- `PMT(rate, nper, pv, [fv], [type])`: the payment per period. The rate is per period, for example `5%/12` monthly. `type` is 0 for payment at the end of the period and 1 for the beginning.
- `PV(rate, nper, pmt, [fv], [type])`, `FV(rate, nper, pmt, [pv], [type])`, `NPER(rate, pmt, pv, [fv], [type])`, `RATE(nper, pmt, pv, [fv], [type], [guess])`.
- `IPMT(rate, per, nper, pv, [fv], [type])`, `PPMT(rate, per, nper, pv, [fv], [type])`, `CUMIPMT(rate, nper, pv, start_period, end_period, type)`, `CUMPRINC(...)`, `ISPMT(rate, per, nper, pv)`.
- `NPV(rate, value1, ...)`: the first value is discounted one period. For cash at t=0, write `=NPV(r, C2:C10)+C1`.
- `XNPV(rate, values, dates)`, `IRR(values, [guess])`, `XIRR(values, dates, [guess])`, `MIRR(values, finance_rate, reinvest_rate)`.
- `EFFECT(nominal_rate, npery)`, `NOMINAL(effect_rate, npery)`, `RRI(nper, pv, fv)`, `PDURATION(rate, pv, fv)`, `FVSCHEDULE(principal, schedule)`.
- `SLN(cost, salvage, life)`, `SYD(cost, salvage, life, per)`, `DB(cost, salvage, life, period, [month])`, `DDB(cost, salvage, life, period, [factor])`, `VDB(cost, salvage, life, start_period, end_period, [factor], [no_switch])`, `AMORLINC`, `AMORDEGRC`.
- Bonds: `PRICE(settlement, maturity, rate, yld, redemption, frequency, [basis])`, `YIELD(...)`, `DURATION(...)`, `MDURATION(...)`, `ACCRINT(issue, first_interest, settlement, rate, par, frequency, [basis], [calc_method])`, `ACCRINTM`, `COUPDAYBS`, `COUPDAYS`, `COUPDAYSNC`, `COUPNCD`, `COUPNUM`, `COUPPCD`, `DISC`, `INTRATE`, `RECEIVED`, `PRICEDISC`, `PRICEMAT`, `YIELDDISC`, `YIELDMAT`, `TBILLEQ`, `TBILLPRICE`, `TBILLYIELD`, `ODDFPRICE`, `ODDFYIELD`, `ODDLPRICE`, `ODDLYIELD`, `DOLLARDE(fractional_dollar, fraction)`, `DOLLARFR`.
- `STOCKHISTORY(stock, start_date, [end_date], [interval], [headers], [property0], ...)` **365**.

**Check**

- The `PMT` sign convention: money you receive is positive and money you pay out is negative. `=PMT(5%/12, 360, 200000)` gives a negative payment.

### Engineering functions (Excel catalog)

- `BIN2DEC(number)`, `BIN2HEX(number, [places])`, `BIN2OCT`, `DEC2BIN(number, [places])`, `DEC2HEX`, `DEC2OCT`, `HEX2BIN`, `HEX2DEC`, `HEX2OCT`, `OCT2BIN`, `OCT2DEC`, `OCT2HEX`.
- `BITAND(number1, number2)`, `BITOR`, `BITXOR`, `BITLSHIFT(number, shift_amount)`, `BITRSHIFT`.
- `CONVERT(number, from_unit, to_unit)`: for example `CONVERT(A1, "mi", "km")`, `"F"` to `"C"`, `"lbm"` to `"kg"`.
- `DELTA(number1, [number2])`, `GESTEP(number, [step])`, `ERF(lower_limit, [upper_limit])`, `ERF.PRECISE`, `ERFC`, `ERFC.PRECISE`.
- `BESSELI(x, n)`, `BESSELJ`, `BESSELK`, `BESSELY`.
- Complex numbers: `COMPLEX(real_num, i_num, [suffix])`, `IMABS`, `IMAGINARY`, `IMREAL`, `IMARGUMENT`, `IMCONJUGATE`, `IMCOS`, `IMDIV`, `IMEXP`, `IMLN`, `IMLOG10`, `IMLOG2`, `IMPOWER`, `IMPRODUCT`, `IMSIN`, `IMSQRT`, `IMSUB`, `IMSUM`, `IMTAN`, and the other `IM*` functions.

### Database, web and cube functions (Excel catalog)

- Database functions take `(database, field, criteria)`, where criteria is a range with headers: `DAVERAGE`, `DCOUNT`, `DCOUNTA`, `DGET`, `DMAX`, `DMIN`, `DPRODUCT`, `DSTDEV`, `DSTDEVP`, `DSUM`, `DVAR`, `DVARP`.
- `WEBSERVICE(url)`, `FILTERXML(xml, xpath)`, `ENCODEURL(text)` (Windows only).
- `CUBEMEMBER(connection, member_expression, [caption])`, `CUBEVALUE(connection, [member_expression1], ...)`, `CUBESET(connection, set_expression, [caption], [sort_order], [sort_by])`, `CUBESETCOUNT(set)`, `CUBERANKEDMEMBER(connection, set_expression, rank, [caption])`, `CUBEMEMBERPROPERTY(connection, member_expression, property)`, `CUBEKPIMEMBER(connection, kpi_name, kpi_property, [caption])`.
- `PY(...)` **365**: a Python in Excel cell, entered as `=PY(` or with Ctrl+Alt+Shift+P.
- `COPILOT(prompt, [context])` **365**: AI-generated results.
