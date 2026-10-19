## Essentials

Target: DuckDB 1.x (PostgreSQL-like SQL plus "friendly SQL"). Query files directly; no server, no CREATE needed first.

| Never in DuckDB | Write instead |
|---|---|
| `SELECT TOP 10` | `... LIMIT 10` |
| `ISNULL(a, b)`, `NVL(a, b)` | `COALESCE(a, b)` (or `ifnull(a, b)`) |
| `GETDATE()`, `SYSDATE` | `current_date`, `now()`, `current_timestamp` |
| `DATEADD(day, 7, d)`, `DATEDIFF(day, a, b)` | `d + INTERVAL 7 DAY`, `date_diff('day', a, b)` |
| `DATE('now')`, `date_sub(now(), INTERVAL 30 DAY)` | `current_date`, `current_date - INTERVAL 30 DAY` |
| `INTEGER PRIMARY KEY AUTOINCREMENT` (SQLite) | `CREATE SEQUENCE s;` then `id INTEGER PRIMARY KEY DEFAULT nextval('s')` |
| `TO_CHAR(d, 'YYYY-MM')`, `FORMAT(...)` | `strftime(d, '%Y-%m')`; parse with `strptime(s, '%d/%m/%Y')` |
| `AUTO_INCREMENT`, `IDENTITY`, `SERIAL` | `CREATE SEQUENCE s; ... id INTEGER DEFAULT nextval('s')` |
| `VARCHAR(MAX)`, `NVARCHAR(50)` | `VARCHAR` (no length needed) |
| `BULK INSERT`, `LOAD DATA INFILE` | `CREATE TABLE t AS FROM read_csv('f.csv')`, `INSERT INTO t FROM 'f.csv'` |
| `FROM DUAL`, `DECLARE @v` | `SELECT 1;`, `SET VARIABLE v = 1; ... getvariable('v')` |
| integer division `a / b` expecting an integer | `/` returns DOUBLE; `a // b` is integer division |

Always: files go in FROM (`FROM 'sales.csv'`, `read_csv('logs/*.csv')`, `read_parquet('x.parquet')`). Strings are 'single quoted'. `GROUP BY ALL` / `ORDER BY ALL` are fine. Lists are 1-based. `COPY (query) TO 'out.parquet' (FORMAT parquet)`.

## Recipes

### Query a CSV file directly and aggregate (read_csv, GROUP BY ALL)

**Example**

```sql
SELECT region, product, SUM(amount) AS revenue
FROM read_csv('sales.csv', header = true)
GROUP BY ALL
ORDER BY revenue DESC
LIMIT 10;
```

### Load files into a table and append more (CREATE TABLE AS, INSERT INTO ... FROM)

**Example**

```sql
CREATE OR REPLACE TABLE events AS FROM read_csv('events_2024_01.csv');
INSERT INTO events FROM read_csv('events_2024_02.csv');
SELECT count(*) AS rows FROM events;
```

### Read many files at once with the file name (glob, filename = true, union_by_name)

**Example**

```sql
SELECT filename, count(*) AS rows
FROM read_csv('logs/*.csv', filename = true, union_by_name = true)
GROUP BY filename
ORDER BY filename;
```

### Export a query to Parquet or CSV (COPY ... TO)

**Example**

```sql
COPY (SELECT * FROM orders WHERE order_date >= DATE '2024-01-01')
TO 'orders_2024.parquet' (FORMAT parquet);
COPY orders TO 'orders.csv' (HEADER, DELIMITER ',');
```

### Monthly totals with date functions (date_trunc, strftime)

**Example**

```sql
SELECT date_trunc('month', order_date) AS month,
       strftime(date_trunc('month', order_date), '%Y-%m') AS label,
       sum(amount) AS revenue
FROM orders
GROUP BY ALL
ORDER BY month;
```

### Keep the top row per group (QUALIFY with ROW_NUMBER)

**Example**

```sql
SELECT customer_id, order_id, order_date, amount
FROM orders
QUALIFY row_number() OVER (PARTITION BY customer_id ORDER BY order_date DESC) = 1;
```

### Pick or drop columns by pattern (SELECT * EXCLUDE, COLUMNS)

**Example**

```sql
SELECT * EXCLUDE (internal_note, raw_payload) FROM orders;
SELECT customer_id, max(COLUMNS('amount|discount')) FROM orders GROUP BY customer_id;
```

### Pivot and unpivot (PIVOT ... ON ... USING)

**Example**

```sql
PIVOT orders ON strftime(order_date, '%b') USING sum(amount) GROUP BY customer_id;
```

### Auto-numbered ids (CREATE SEQUENCE, nextval)

**Example**

```sql
CREATE SEQUENCE invoice_seq START 1;
CREATE TABLE invoices (
  invoice_id INTEGER PRIMARY KEY DEFAULT nextval('invoice_seq'),
  customer   VARCHAR NOT NULL,
  amount     DECIMAL(12,2) NOT NULL,
  created_at TIMESTAMP DEFAULT current_timestamp
);
INSERT INTO invoices (customer, amount) VALUES ('Ann', 120.50);
```

### Clean dirty values (TRY_CAST, regexp_replace, trim, NULLIF)

**Example**

```sql
SELECT TRY_CAST(regexp_replace(trim(price_text), '[^0-9.]', '', 'g') AS DECIMAL(10,2)) AS price,
       NULLIF(trim(city), '') AS city
FROM raw_products;
```

### Query JSON and nested data (read_json, ->>, unnest)

**Example**

```sql
SELECT j ->> '$.user.name' AS name, unnest(tags) AS tag
FROM read_json('events.json', columns = {j: 'JSON', tags: 'VARCHAR[]'});
```
