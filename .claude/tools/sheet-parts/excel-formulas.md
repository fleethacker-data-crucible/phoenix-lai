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
