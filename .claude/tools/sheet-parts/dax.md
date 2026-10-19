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
