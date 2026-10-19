## Essentials

Target: Qlik Sense / QlikView load script.

| Never in Qlik script | Write instead |
|---|---|
| `SELECT ... FROM file.csv` | `LOAD ... FROM [lib://Data/file.csv] (txt, utf8, embedded labels, delimiter is ',');` |
| a statement without `;` | every statement ends with `;` |
| `==`, `!=`, `&&`, `\|\|` | `=`, `<>`, `and`, `or` |
| `'a' + 'b'` | `'a' & 'b'` (strings in single quotes) |
| `LET v = 5` then `$v` | `LET vMax = 5;` used as `$(vMax)` (dollar expansion) |
| two tables with the same column names loaded one after another, expecting two tables | they auto-concatenate: `NoConcatenate LOAD ...` |
| `JOIN` without a target, expecting SQL semantics | `LEFT JOIN (Orders) LOAD ...` joins on all common field names |
| unintended synthetic keys from shared fields | rename (`AS`), `QUALIFY`, or build one key field: `A & '|' & B AS %Key` |

Always: field names with spaces in `[brackets]`; `RESIDENT Table` loads from a table already in memory; `DROP TABLE` temp tables; `Date#(text, 'YYYY-MM-DD')` to read text dates, `Date(d, 'YYYY-MM-DD')` to format.

## Recipes

### Load a CSV and an inline mapping table (LOAD FROM, INLINE, MAPPING, ApplyMap)

**Example**

```qlik
RegionMap:
MAPPING LOAD * INLINE [
Code, Region
N, North
S, South
];

Sales:
LOAD
    OrderID,
    Date#(OrderDate, 'YYYY-MM-DD') AS OrderDate,
    ApplyMap('RegionMap', RegionCode, 'Unknown') AS Region,
    Amount
FROM [lib://Data/sales.csv]
(txt, utf8, embedded labels, delimiter is ',', msq);
```

### Aggregate from a loaded table (RESIDENT, GROUP BY, DROP TABLE)

**Example**

```qlik
Totals:
LOAD
    Region,
    Sum(Amount) AS TotalAmount,
    Count(OrderID) AS Orders
RESIDENT Sales
GROUP BY Region;
```

### Variables and dollar expansion (LET, SET, $())

**Example**

```qlik
LET vToday = Today();
SET vStartDate = '2024-01-01';

Recent:
NoConcatenate
LOAD *
RESIDENT Sales
WHERE OrderDate >= Date#('$(vStartDate)', 'YYYY-MM-DD');
```

### Join extra fields onto a table (LEFT JOIN)

**Example**

```qlik
LEFT JOIN (Sales)
LOAD
    CustomerID,
    CustomerName,
    City
FROM [lib://Data/customers.csv]
(txt, utf8, embedded labels, delimiter is ',');
```

### Incremental load (max date, WHERE, CONCATENATE, STORE)

**Example**

```qlik
Old:
LOAD * FROM [lib://Data/orders.qvd] (qvd);

MaxDate:
LOAD Max(OrderDate) AS MaxDate RESIDENT Old;
LET vMaxDate = Peek('MaxDate', 0, 'MaxDate');
DROP TABLE MaxDate;

CONCATENATE (Old)
LOAD * FROM [lib://Data/orders_new.csv]
(txt, utf8, embedded labels, delimiter is ',')
WHERE OrderDate > '$(vMaxDate)';

STORE Old INTO [lib://Data/orders.qvd] (qvd);
```

### A master calendar (AUTOGENERATE, IterNo, Date functions)

**Example**

```qlik
MinMax:
LOAD Min(OrderDate) AS MinDate, Max(OrderDate) AS MaxDate RESIDENT Sales;
LET vMin = Num(Peek('MinDate', 0, 'MinDate'));
LET vMax = Num(Peek('MaxDate', 0, 'MaxDate'));
DROP TABLE MinMax;

Calendar:
LOAD
    Date($(vMin) + IterNo() - 1) AS OrderDate,
    Year($(vMin) + IterNo() - 1) AS Year,
    Month($(vMin) + IterNo() - 1) AS Month
AUTOGENERATE 1
WHILE $(vMin) + IterNo() - 1 <= $(vMax);
```
