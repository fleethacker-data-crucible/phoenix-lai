# SQL syntax reference (Phoenix)

For Phoenix models writing and checking standard SQL, the official ISO/IEC 9075 language (SQL:2016 and SQL:2023), which every major database implements with its own extensions. Each `###` entry is one statement, clause, operator or function: what it does, its exact syntax, every part, an example, and the mistakes to check for. When the user names a database, also use that dialect's sheet: `oracle-sql-syntax.md` for Oracle, `duckdb-sql-syntax.md` for DuckDB. For SQL Server, PostgreSQL, MySQL or SQLite, the notes below flag where they differ from the standard.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply · UPPER CASE = keyword (SQL keywords are case-insensitive).

## Checklist: before you answer with SQL

- Clause order: `SELECT … FROM … [JOIN …] WHERE … GROUP BY … HAVING … [WINDOW …] ORDER BY … OFFSET … FETCH`. They are evaluated in the order FROM → WHERE → GROUP BY → HAVING → SELECT → ORDER BY, so a SELECT alias cannot be used in WHERE (but it can in ORDER BY).
- Every non-aggregated column in SELECT (and in HAVING and ORDER BY) appears in GROUP BY.
- Filter rows in `WHERE` and groups in `HAVING`. Aggregates (`SUM`, `COUNT`) are not allowed in WHERE.
- NULL is unknown: `x = NULL` is never true. Use `IS NULL` and `IS NOT NULL`. `NOT IN (subquery)` returns no rows if the subquery yields a NULL, so use `NOT EXISTS`.
- Strings use single quotes `'text'` (double a quote inside: `'O''Brien'`). Double quotes `"Name"` are identifiers.
- Every JOIN has an `ON` or `USING` condition (or is an explicit `CROSS JOIN`). A missing condition multiplies the rows.
- A filter on the right table of a LEFT JOIN placed in WHERE turns it into an inner join. Put it in `ON`.
- `UPDATE` and `DELETE` without `WHERE` change every row. Check the WHERE clause, and run it as a `SELECT` first.
- Integer division truncates in many engines (`5 / 2 = 2`). Cast first: `CAST(a AS DECIMAL(10,2)) / b`. Guard division with `NULLIF(b, 0)`.
- Never build SQL by concatenating user input. Use parameters (`?`, `:name`, `@p`).
- Row limits differ by dialect: the standard and Oracle 12c+ use `FETCH FIRST n ROWS ONLY`, PostgreSQL, MySQL, SQLite and DuckDB use `LIMIT n`, and SQL Server uses `TOP (n)` or `OFFSET … FETCH`.
- Without ORDER BY, row order is not guaranteed, even with LIMIT or FETCH.

## Querying data

### SELECT (SQL statement)

**Syntax**

```sql
[WITH [RECURSIVE] <cte> AS (<query>) [, ...]]
SELECT [ALL | DISTINCT] <select_list>
FROM <table_reference> [, ...]
[WHERE <condition>]
[GROUP BY [ALL | DISTINCT] <grouping_element> [, ...]]
[HAVING <condition>]
[WINDOW <name> AS (<window_spec>) [, ...]]
[ORDER BY <expr> [ASC | DESC] [NULLS {FIRST | LAST}] [, ...]]
[OFFSET <n> {ROW | ROWS}]
[FETCH {FIRST | NEXT} [<n> [PERCENT]] {ROW | ROWS} {ONLY | WITH TIES}];
```

**Parts**

- `<select_list>`: `*`, `t.*`, or expressions with optional aliases: `expr [AS] alias`.
- `DISTINCT`: removes duplicate result rows (across all the selected columns).
- `FROM`: tables, views, subqueries (`(SELECT …) AS t`), joins and table functions.
- `ORDER BY`: accepts column names, aliases, expressions or positions (`ORDER BY 2`, discouraged).
- `OFFSET … FETCH`: paging. `WITH TIES` also returns rows that tie with the last one (it needs ORDER BY).

**Example**

```sql
SELECT c.customer_id, c.name, SUM(o.amount) AS total
FROM customers AS c
JOIN orders AS o ON o.customer_id = c.customer_id
WHERE o.order_date >= DATE '2024-01-01'
GROUP BY c.customer_id, c.name
HAVING SUM(o.amount) > 1000
ORDER BY total DESC
FETCH FIRST 10 ROWS ONLY;
```

**Check**

- `SELECT DISTINCT a, b` is distinct on the pair (a, b), not on `a` alone.
- Use an alias defined in SELECT only in ORDER BY. In WHERE, repeat the expression or wrap the query in a subquery.

### WHERE conditions and predicates (SQL clause)

**Syntax**

```sql
WHERE <expr> {= | <> | != | < | > | <= | >=} <expr>
  AND | OR | NOT
  <expr> [NOT] BETWEEN <low> AND <high>               -- inclusive on both ends
  <expr> [NOT] IN (<value>, ... | <subquery>)
  <expr> [NOT] LIKE <pattern> [ESCAPE '<char>']        -- % any string, _ one character
  <expr> IS [NOT] NULL
  <expr> IS [NOT] DISTINCT FROM <expr>                 -- NULL-safe comparison
  [NOT] EXISTS (<subquery>)
  <expr> {= | < | > ...} {ALL | ANY | SOME} (<subquery>)
  <expr> [NOT] SIMILAR TO <regex-like pattern>
  <expr> IS [NOT] {TRUE | FALSE | UNKNOWN}
```

**Check**

- `AND` binds tighter than `OR`: `a OR b AND c` means `a OR (b AND c)`. Use parentheses.
- `BETWEEN '2024-01-01' AND '2024-01-31'` on a timestamp misses times after midnight on the 31st. Use `>= start AND < next_day`.
- In standard SQL, `LIKE` is case-sensitive. Whether it is depends on the collation in SQL Server and MySQL. Use `LOWER(col) LIKE LOWER(pattern)` when it must be case-insensitive.
- `!=` is widely supported, but `<>` is the standard operator.

### JOIN (SQL clause)

**Syntax**

```sql
FROM a [INNER] JOIN b ON <condition>
FROM a LEFT [OUTER] JOIN b ON <condition>        -- all rows of a; b columns NULL when no match
FROM a RIGHT [OUTER] JOIN b ON <condition>
FROM a FULL [OUTER] JOIN b ON <condition>
FROM a CROSS JOIN b                               -- every combination
FROM a JOIN b USING (<col> [, ...])               -- equal-named columns, appear once
FROM a NATURAL JOIN b                             -- all same-named columns (fragile, avoid)
FROM a CROSS JOIN LATERAL (<subquery using a>) AS x    -- correlated table (SQL Server: CROSS APPLY)
FROM a LEFT JOIN LATERAL (<subquery>) AS x ON TRUE     -- (SQL Server: OUTER APPLY)
```

**Example**

```sql
SELECT e.name, d.name AS dept, m.name AS manager
FROM employees e
LEFT JOIN departments d ON d.dept_id = e.dept_id
LEFT JOIN employees m ON m.emp_id = e.manager_id;     -- self join
```

**Check**

- Joining to a table with several matching rows multiplies the rows, which inflates SUM and COUNT. Aggregate in a subquery first, or use EXISTS.
- To find rows with no match (an anti join), write `LEFT JOIN b ON … WHERE b.key IS NULL`, or `NOT EXISTS`.

### GROUP BY, HAVING, ROLLUP, CUBE, GROUPING SETS (SQL clauses)

**Syntax**

```sql
GROUP BY <expr> [, ...]
GROUP BY ROLLUP (a, b)            -- (a,b), (a), ()  : subtotals + grand total
GROUP BY CUBE (a, b)              -- every combination
GROUP BY GROUPING SETS ((a, b), (a), ())
HAVING <condition on aggregates>
GROUPING(<col>)                   -- 1 when the row is a subtotal for that column
```

**Example**

```sql
SELECT region, product, SUM(sales) AS s, GROUPING(product) AS is_subtotal
FROM fact_sales
GROUP BY ROLLUP (region, product)
ORDER BY region, product;
```

### Aggregate functions (SQL functions)

**Syntax**

```sql
COUNT(*)  COUNT(expr)  COUNT(DISTINCT expr)  SUM(expr)  AVG(expr)  MIN(expr)  MAX(expr)
STDDEV_POP(x)  STDDEV_SAMP(x)  VAR_POP(x)  VAR_SAMP(x)  CORR(y, x)  COVAR_POP(y, x)  REGR_SLOPE(y, x)
EVERY(cond)  ANY_VALUE(x) (2023)  LISTAGG(expr, ', ') WITHIN GROUP (ORDER BY expr)    -- PostgreSQL / SQL Server: STRING_AGG(expr, ', ')
PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY x)  PERCENTILE_DISC(0.9) WITHIN GROUP (ORDER BY x)
ARRAY_AGG(x ORDER BY y)  JSON_ARRAYAGG(x)  JSON_OBJECTAGG(k VALUE v)
<aggregate>(...) FILTER (WHERE <condition>)          -- conditional aggregate (PostgreSQL, DuckDB, SQLite)
```

**Check**

- `COUNT(col)` skips NULLs, while `COUNT(*)` counts every row. `SUM` of no rows is NULL, not 0, so write `COALESCE(SUM(x), 0)`.
- `AVG` of an integer column truncates in some engines (SQL Server). Cast first: `AVG(CAST(x AS DECIMAL(12,2)))`.
- Where `FILTER` is not supported, use `SUM(CASE WHEN cond THEN x END)`.

### Window functions (SQL OVER clause)

**Syntax**

```sql
<function>(...) OVER (
    [PARTITION BY <expr> [, ...]]
    [ORDER BY <expr> [ASC | DESC] [NULLS FIRST | LAST] [, ...]]
    [{ROWS | RANGE | GROUPS} {BETWEEN <start> AND <end> | <start>} [EXCLUDE {CURRENT ROW | GROUP | TIES | NO OTHERS}]]
)
<start>/<end>: UNBOUNDED PRECEDING | <n> PRECEDING | CURRENT ROW | <n> FOLLOWING | UNBOUNDED FOLLOWING
Ranking:     ROW_NUMBER()  RANK()  DENSE_RANK()  PERCENT_RANK()  CUME_DIST()  NTILE(n)
Navigation:  LAG(expr [, offset [, default]])  LEAD(...)  FIRST_VALUE(expr)  LAST_VALUE(expr)  NTH_VALUE(expr, n)
Aggregates:  SUM/AVG/COUNT/MIN/MAX(...) OVER (...)
```

**Example**

```sql
SELECT order_id, customer_id, amount,
       SUM(amount) OVER (PARTITION BY customer_id ORDER BY order_date ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total,
       ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY order_date DESC) AS rn
FROM orders;
-- latest order per customer:
SELECT * FROM (SELECT o.*, ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY order_date DESC) AS rn FROM orders o) t WHERE rn = 1;
```

**Check**

- A window function cannot appear in WHERE or GROUP BY. Filter it in an outer query (or with `QUALIFY` in DuckDB, Snowflake and BigQuery).
- With ORDER BY, the default frame is `RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW`, so `LAST_VALUE` returns the current row. Specify `ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING`.

### Subqueries and CTEs: WITH, WITH RECURSIVE (SQL syntax)

**Syntax**

```sql
WITH <name> [(<col>, ...)] AS (<query>) [, <name2> AS (...)]
SELECT ... FROM <name> ...;
WITH RECURSIVE <name> (<cols>) AS (
    <anchor query>
    UNION ALL
    <recursive query referencing name>
)
SELECT ...;
-- scalar subquery: (SELECT MAX(x) FROM t) ; correlated: WHERE x > (SELECT AVG(x) FROM t t2 WHERE t2.g = t.g)
```

**Example**

```sql
WITH RECURSIVE org (emp_id, name, manager_id, depth) AS (
    SELECT emp_id, name, manager_id, 0 FROM employees WHERE manager_id IS NULL
    UNION ALL
    SELECT e.emp_id, e.name, e.manager_id, o.depth + 1
    FROM employees e JOIN org o ON e.manager_id = o.emp_id
)
SELECT * FROM org ORDER BY depth, name;
```

**Check**

- A scalar subquery must return at most one row, or the query fails at run time.
- `RECURSIVE` is required in the standard, PostgreSQL, MySQL, SQLite and DuckDB, but not allowed in SQL Server and Oracle (which write plain `WITH`). Always include a stop condition.

### Set operations: UNION, INTERSECT, EXCEPT (SQL operators)

**Syntax**

```sql
<query> UNION [ALL | DISTINCT] <query>
<query> INTERSECT [ALL] <query>
<query> EXCEPT [ALL] <query>              -- Oracle: MINUS (EXCEPT also in 21c+)
[ORDER BY ...]                            -- once, at the end
```

**Check**

- Both queries need the same number of columns, with compatible types, matched by position.
- `UNION` removes duplicates (slow). Use `UNION ALL` when duplicates are impossible or wanted.

### CASE, COALESCE, NULLIF (SQL expressions)

**Syntax**

```sql
CASE WHEN <cond> THEN <result> [WHEN ...] [ELSE <result>] END       -- searched
CASE <expr> WHEN <value> THEN <result> [...] [ELSE <result>] END    -- simple
COALESCE(a, b, c)            -- first non-NULL
NULLIF(a, b)                 -- NULL if a = b, else a  (avoid divide by zero: x / NULLIF(y, 0))
```

**Check**

- When no WHEN matches and there is no ELSE, the result is NULL.
- Every branch should return a compatible type.

## Changing data

### INSERT (SQL statement)

**Syntax**

```sql
INSERT INTO <table> [(<col>, ...)] VALUES (<v>, ...) [, (<v>, ...) ...];
INSERT INTO <table> [(<col>, ...)] <select query>;
INSERT INTO <table> DEFAULT VALUES;
-- upsert dialects: PostgreSQL/SQLite/DuckDB ON CONFLICT (key) DO UPDATE SET ... | MySQL ON DUPLICATE KEY UPDATE ... | standard: MERGE
-- returning: PostgreSQL/SQLite/DuckDB RETURNING * | SQL Server OUTPUT inserted.* | Oracle RETURNING ... INTO
```

**Check**

- Always list the columns explicitly. Positional inserts break when the table changes.

### UPDATE (SQL statement)

**Syntax**

```sql
UPDATE <table> [AS alias]
SET <col> = <expr> [, <col2> = <expr2> ...]
[FROM <other tables>]               -- PostgreSQL/SQL Server/DuckDB extension
[WHERE <condition>];
UPDATE t SET (a, b) = (SELECT x, y FROM s WHERE s.id = t.id) WHERE ...;   -- standard row form
```

**Check**

- Without WHERE, every row is updated. Run the WHERE as a `SELECT COUNT(*)` first.
- A correlated `SET col = (SELECT …)` sets NULL where the subquery finds no row. Add `WHERE EXISTS (…)`.

### DELETE and TRUNCATE (SQL statements)

**Syntax**

```sql
DELETE FROM <table> [WHERE <condition>];
TRUNCATE TABLE <table>;                  -- removes all rows fast; usually cannot be filtered or rolled back (Oracle, MySQL)
```

### MERGE (SQL statement, upsert)

**Syntax**

```sql
MERGE INTO <target> [AS] t
USING <source table or (subquery)> [AS] s
ON (<match condition>)
WHEN MATCHED [AND <cond>] THEN UPDATE SET t.col = s.col [, ...]
WHEN MATCHED [AND <cond>] THEN DELETE
WHEN NOT MATCHED [BY TARGET] [AND <cond>] THEN INSERT (<cols>) VALUES (<s.cols>)
[WHEN NOT MATCHED BY SOURCE THEN DELETE];         -- SQL Server / PostgreSQL 17 / DuckDB extension
```

**Example**

```sql
MERGE INTO inventory AS t
USING staging AS s ON (t.sku = s.sku)
WHEN MATCHED THEN UPDATE SET qty = s.qty, updated_at = CURRENT_TIMESTAMP
WHEN NOT MATCHED THEN INSERT (sku, qty, updated_at) VALUES (s.sku, s.qty, CURRENT_TIMESTAMP);
```

**Check**

- The source must have at most one row per target key, or MERGE fails ("unable to get a stable set of rows"). SQL Server requires the statement to end with `;`.

## Defining data (DDL)

### CREATE TABLE (SQL statement)

**Syntax**

```sql
CREATE [TEMPORARY] TABLE [IF NOT EXISTS] <name> (
    <col> <type> [NOT NULL | NULL] [DEFAULT <expr>] [GENERATED ALWAYS AS IDENTITY | GENERATED BY DEFAULT AS IDENTITY]
              [GENERATED ALWAYS AS (<expr>) [STORED]] [PRIMARY KEY] [UNIQUE] [CHECK (<cond>)] [REFERENCES <t>(<col>) [ON DELETE CASCADE | SET NULL | RESTRICT]],
    ...,
    [CONSTRAINT <name>] PRIMARY KEY (<col>, ...),
    [CONSTRAINT <name>] UNIQUE (<col>, ...),
    [CONSTRAINT <name>] FOREIGN KEY (<col>) REFERENCES <table>(<col>) [ON DELETE {CASCADE | SET NULL | SET DEFAULT | RESTRICT | NO ACTION}] [ON UPDATE ...],
    [CONSTRAINT <name>] CHECK (<condition>)
);
CREATE TABLE <name> AS <query> [WITH [NO] DATA];
```

**Example**

```sql
CREATE TABLE orders (
    order_id    INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id INTEGER NOT NULL REFERENCES customers(customer_id),
    order_date  DATE NOT NULL DEFAULT CURRENT_DATE,
    amount      DECIMAL(12, 2) NOT NULL CHECK (amount >= 0),
    status      VARCHAR(20) DEFAULT 'new'
);
```

**Check**

- The identity syntax differs: SQL Server uses `INT IDENTITY(1,1)`, MySQL `AUTO_INCREMENT`, SQLite `INTEGER PRIMARY KEY`, and older PostgreSQL `SERIAL`.
- `IF NOT EXISTS` is not supported by Oracle before 23ai, or by SQL Server.

### Data types (SQL standard types)

**Syntax**

```sql
CHAR(n)  VARCHAR(n)  CHARACTER VARYING(n)  CLOB  NCHAR(n)  NVARCHAR(n)                 -- text
SMALLINT  INTEGER/INT  BIGINT  DECIMAL(p, s)  NUMERIC(p, s)  REAL  DOUBLE PRECISION  FLOAT(p)  DECFLOAT
BOOLEAN  DATE  TIME [(p)] [WITH TIME ZONE]  TIMESTAMP [(p)] [WITH TIME ZONE]  INTERVAL YEAR TO MONTH  INTERVAL DAY TO SECOND
BINARY(n)  VARBINARY(n)  BLOB  JSON (2023)  ARRAY  ROW  MULTISET
```

**Check**

- `DECIMAL(p, s)`: `p` is the total number of digits and `s` the digits after the point, so `DECIMAL(5,2)` holds up to 999.99.
- Use DECIMAL, not FLOAT, for money.

### ALTER TABLE, DROP, CREATE INDEX, CREATE VIEW (SQL statements)

**Syntax**

```sql
ALTER TABLE <t> ADD [COLUMN] <col> <type> [constraints];
ALTER TABLE <t> DROP [COLUMN] <col> [CASCADE | RESTRICT];
ALTER TABLE <t> ALTER [COLUMN] <col> {SET DEFAULT <expr> | DROP DEFAULT | SET NOT NULL | DROP NOT NULL | SET DATA TYPE <type>};
ALTER TABLE <t> ADD CONSTRAINT <name> {PRIMARY KEY (...) | UNIQUE (...) | FOREIGN KEY (...) REFERENCES ... | CHECK (...)};
ALTER TABLE <t> DROP CONSTRAINT <name>;   ALTER TABLE <t> RENAME TO <new>;   ALTER TABLE <t> RENAME COLUMN <a> TO <b>;
DROP TABLE [IF EXISTS] <t> [CASCADE | RESTRICT];
CREATE [UNIQUE] INDEX <name> ON <t> (<col> [ASC | DESC], ...);   DROP INDEX <name>;
CREATE [OR REPLACE] VIEW <name> [(<cols>)] AS <query> [WITH [CASCADED | LOCAL] CHECK OPTION];
CREATE MATERIALIZED VIEW <name> AS <query>;      -- vendor feature (PostgreSQL, Oracle)
CREATE SCHEMA <name>;   CREATE SEQUENCE <s> START WITH 1 INCREMENT BY 1;   NEXT VALUE FOR <s>
```

**Check**

- Column type changes differ by dialect: SQL Server and the standard use `ALTER COLUMN c TYPE`, Oracle uses `MODIFY (c type)`, and MySQL uses `MODIFY COLUMN`. Check the dialect.
- DROP is permanent. Warn the user, and suggest a backup.

## Transactions and security

### Transactions: COMMIT, ROLLBACK, SAVEPOINT (SQL statements)

**Syntax**

```sql
START TRANSACTION [ISOLATION LEVEL {READ UNCOMMITTED | READ COMMITTED | REPEATABLE READ | SERIALIZABLE}] [READ ONLY | READ WRITE];
-- BEGIN [TRANSACTION]; in PostgreSQL/SQL Server/SQLite/DuckDB
SAVEPOINT <name>;   ROLLBACK TO SAVEPOINT <name>;   RELEASE SAVEPOINT <name>;
COMMIT [WORK];   ROLLBACK [WORK];
SET TRANSACTION ISOLATION LEVEL <level>;
```

**Check**

- In Oracle, PostgreSQL and SQL Server, DDL behaves differently inside transactions: Oracle and MySQL commit implicitly on DDL.

### GRANT and REVOKE (SQL statements)

**Syntax**

```sql
GRANT {SELECT | INSERT | UPDATE [(<cols>)] | DELETE | REFERENCES | USAGE | EXECUTE | ALL PRIVILEGES} [, ...]
    ON [TABLE] <object> TO {<user> | <role> | PUBLIC} [WITH GRANT OPTION];
REVOKE [GRANT OPTION FOR] <privileges> ON <object> FROM <user> [CASCADE | RESTRICT];
CREATE ROLE <name>;   GRANT <role> TO <user>;
```

## Functions

### String functions (SQL functions)

**Syntax**

```sql
a || b                              -- concatenation (SQL Server: + or CONCAT; MySQL: CONCAT)
CONCAT(a, b, ...)                   -- NULL-safe in most engines (not Oracle for 3+ args: only 2)
CHAR_LENGTH(s) / CHARACTER_LENGTH(s)  OCTET_LENGTH(s)   -- LENGTH(s) in most dialects; SQL Server LEN(s)
UPPER(s)  LOWER(s)
SUBSTRING(s FROM start [FOR length])   SUBSTRING(s, start, length)  -- 1-based; Oracle SUBSTR
POSITION(sub IN s)                     -- 1-based, 0 if absent; Oracle INSTR, SQL Server CHARINDEX(sub, s)
TRIM([LEADING | TRAILING | BOTH] [chars] FROM s)   LTRIM(s)  RTRIM(s)
OVERLAY(s PLACING new FROM start [FOR len])   REPLACE(s, from, to)   LEFT(s, n)  RIGHT(s, n)   LPAD(s, n, c)  RPAD(s, n, c)
REGEXP_LIKE(s, pattern)  REGEXP_REPLACE(s, pattern, repl)  REGEXP_SUBSTR(s, pattern)   -- SQL:2008 names; support varies
NORMALIZE(s)  TRANSLATE(s, from_chars, to_chars)
```

**Check**

- `NULL || 'x'` is NULL in the standard and PostgreSQL (but `'x'` in Oracle). Wrap the value in `COALESCE`.
- String positions start at 1, not 0.

### Numeric functions (SQL functions)

**Syntax**

```sql
ABS(x)  MOD(a, b)  POWER(x, y)  SQRT(x)  EXP(x)  LN(x)  LOG10(x)  LOG(b, x)  FLOOR(x)  CEILING(x) / CEIL(x)
ROUND(x [, d])  TRUNC(x [, d]) (TRUNCATE in MySQL)  SIGN(x)  PI()  RANDOM() / RAND()  GREATEST(a, b, ...)  LEAST(a, b, ...)
SIN  COS  TAN  ASIN  ACOS  ATAN  ATAN2(y, x)  WIDTH_BUCKET(x, min, max, n)
```

### Date and time functions (SQL functions)

**Syntax**

```sql
CURRENT_DATE   CURRENT_TIME   CURRENT_TIMESTAMP   LOCALTIME   LOCALTIMESTAMP
DATE '2024-01-31'   TIMESTAMP '2024-01-31 13:45:00'   TIME '13:45:00'   INTERVAL '7' DAY   INTERVAL '1-6' YEAR TO MONTH
EXTRACT({YEAR | MONTH | DAY | HOUR | MINUTE | SECOND | TIMEZONE_HOUR} FROM <datetime>)
<date> + INTERVAL '1' MONTH     <ts2> - <ts1>  -- interval
CAST('2024-01-31' AS DATE)
-- vendor: SQL Server DATEADD(day, 7, d), DATEDIFF(day, a, b), GETDATE(); MySQL DATE_ADD(d, INTERVAL 7 DAY), NOW();
--         PostgreSQL d + 7, date_trunc('month', ts), AGE(); Oracle d + 7, ADD_MONTHS(d, 1), TRUNC(d, 'MM'), SYSDATE
```

**Check**

- Date arithmetic and formatting differ the most between dialects. Name the dialect, and use its functions.
- Do not compare dates as strings unless they are ISO `YYYY-MM-DD`.

### CAST and conversion (SQL functions)

**Syntax**

```sql
CAST(<expr> AS <type>)           -- CAST('42' AS INTEGER), CAST(price AS DECIMAL(10,2)), CAST(ts AS DATE)
-- vendor: SQL Server CONVERT(type, expr, style), TRY_CAST; PostgreSQL expr::type; Oracle TO_NUMBER/TO_CHAR/TO_DATE; DuckDB TRY_CAST
```

**Check**

- A CAST of bad data raises an error. Use `TRY_CAST` (SQL Server, DuckDB, Snowflake) or validate first.

### JSON functions (SQL:2016 functions)

**Syntax**

```sql
JSON_VALUE(doc, '$.path' [RETURNING type])     -- scalar
JSON_QUERY(doc, '$.path')                      -- object / array
JSON_EXISTS(doc, '$.path')
JSON_OBJECT(KEY 'a' VALUE x, 'b' VALUE y)      JSON_ARRAY(x, y)
JSON_TABLE(doc, '$.items[*]' COLUMNS (id INT PATH '$.id', name VARCHAR(50) PATH '$.name')) AS jt
doc IS JSON
```
