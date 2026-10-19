# DuckDB SQL syntax reference (Phoenix)

For Phoenix models writing and checking DuckDB SQL (DuckDB 1.x, the in-process analytical database). Each `###` entry is one statement, clause, function or file reader: what it does, its exact syntax, every argument, an example, and the mistakes to check for. Standard SQL that DuckDB shares is in `sql-syntax.md`. DuckDB follows PostgreSQL closely and adds "friendly SQL" extensions, listed here.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply.

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

## Rules: before you answer with DuckDB SQL

- Read files directly in `FROM`: `FROM 'data.csv'`, `read_csv('*.csv')`, `read_parquet('s3://…')`. You do not need to create a table first.
- Use `LIMIT n` (also `OFFSET`). Strings take single quotes. Identifiers with spaces or capitals take double quotes. Unquoted identifiers are case-insensitive.
- Integer division: `/` returns a DOUBLE (`5 / 2 = 2.5`), and `//` is integer division.
- Lists are 1-based: `list[1]` is the first element. Slices are `list[2:3]`, inclusive.
- `GROUP BY ALL` and `ORDER BY ALL` are valid. `SELECT * EXCLUDE (col)` and `SELECT * REPLACE (expr AS col)` are valid. `QUALIFY` filters window results.
- Lambdas use `x -> x + 1` (or the newer `lambda x: x + 1`). Use them only inside list functions (`list_transform`, `list_filter`, `list_reduce`).
- Date parts and formats: `strftime(ts, '%Y-%m-%d')` formats, `strptime(text, '%d/%m/%Y')` parses, `date_trunc('month', d)`, `date_diff('day', a, b)`. These use `%` codes, not Oracle `YYYY` masks.
- `COPY … TO 'file.parquet' (FORMAT parquet)`: options go in parentheses after the file name.
- Extensions: `INSTALL httpfs; LOAD httpfs;` (most autoload). Remote files need httpfs, and S3 credentials go through `CREATE SECRET`.
- `TRY_CAST(x AS INTEGER)` returns NULL instead of an error. Use it for dirty data.
- A persistent database file is opened with `duckdb my.db` (CLI) or `ATTACH 'my.db'`. Without one, everything is in memory and lost when the session ends.

## Reading and writing files

### FROM a file, read_csv (DuckDB table function)

**Syntax**

```sql
SELECT * FROM 'data.csv';                          -- auto-detect by extension (.csv .tsv .parquet .json .gz)
SELECT * FROM read_csv('path/*.csv' | ['a.csv', 'b.csv'] [, option := value ...]);
FROM read_csv('sales.csv', header = true, delim = ';', columns = {'id': 'INTEGER', 'name': 'VARCHAR'});
```

**Options** (all optional, auto-detected by default)

- `header` (BOOL): the first line is the header.
- `delim` / `sep` (VARCHAR): the separator.
- `quote`, `escape` (VARCHAR).
- `new_line`.
- `skip` (BIGINT): lines to skip.
- `comment` (VARCHAR).
- `columns` (STRUCT): column names and types. It turns off type detection.
- `column_types` / `types` / `dtypes`: override some types.
- `names` / `column_names` (VARCHAR[]).
- `auto_detect` (BOOL, default true).
- `sample_size` (BIGINT, -1 = all rows).
- `all_varchar` (BOOL).
- `dateformat`, `timestampformat` (VARCHAR, strptime codes).
- `decimal_separator`.
- `nullstr` (VARCHAR or list).
- `ignore_errors` (BOOL).
- `store_rejects` (BOOL).
- `null_padding` (BOOL).
- `max_line_size`.
- `parallel`.
- `filename` (BOOL or VARCHAR): adds the source file column.
- `union_by_name` (BOOL): match columns across files by name.
- `hive_partitioning` (BOOL).
- `compression` (`auto`, `gzip`, `zstd`, `none`).
- `encoding` (`utf-8`, `utf-16`, `latin-1`).
- `strict_mode`.

**Example**

```sql
CREATE TABLE sales AS
SELECT * FROM read_csv('exports/sales_*.csv', union_by_name = true, filename = true, dateformat = '%d/%m/%Y');
```

**Check**

- Options use `name = value` or `name := value`, comma-separated, inside the function call.
- `read_csv_auto` is the older name of `read_csv`, which auto-detects by default.

### read_parquet and read_json (DuckDB table functions)

**Syntax**

```sql
SELECT * FROM read_parquet('data/*.parquet' | [list] [, binary_as_string := true] [, filename := true] [, file_row_number := true] [, hive_partitioning := true] [, union_by_name := true]);
SELECT * FROM 'data/year=*/month=*/*.parquet';      -- hive partitions become columns
SELECT * FROM read_json('events.json' [, format := 'auto' | 'array' | 'newline_delimited' | 'unstructured'] [, records := true] [, columns := {...}] [, maximum_depth := n] [, union_by_name := true]);
SELECT * FROM read_json_objects('x.json');   SELECT * FROM read_ndjson('x.ndjson');
SELECT * FROM parquet_schema('f.parquet');   SELECT * FROM parquet_metadata('f.parquet');
SELECT * FROM read_text('notes/*.txt');      SELECT * FROM read_blob('img/*.png');
SELECT * FROM glob('data/*.csv');            -- list files
```

**Check**

- Parquet columns are read lazily. Selecting only the needed columns, and filtering, is much faster than `SELECT *`.

### COPY TO / COPY FROM, EXPORT / IMPORT DATABASE (DuckDB statements)

**Syntax**

```sql
COPY <table | (query)> TO 'file.ext' [(FORMAT {csv | parquet | json}, HEADER [true], DELIMITER ',', COMPRESSION {zstd | snappy | gzip | none},
                                       PARTITION_BY (col, ...), OVERWRITE_OR_IGNORE, PER_THREAD_OUTPUT, ROW_GROUP_SIZE n, FILENAME_PATTERN 'x_{i}')];
COPY <table> FROM 'file.csv' [(FORMAT csv, HEADER, DELIMITER ';', ...)];
EXPORT DATABASE 'dir' [(FORMAT parquet)];   IMPORT DATABASE 'dir';
```

**Example**

```sql
COPY (SELECT * FROM sales WHERE year = 2024) TO 'out/sales_2024.parquet' (FORMAT parquet, COMPRESSION zstd);
COPY sales TO 'out/sales' (FORMAT parquet, PARTITION_BY (year, month), OVERWRITE_OR_IGNORE);
```

**Check**

- For `COPY FROM`, the table must already exist, with matching columns. To create and load at once, use `CREATE TABLE … AS SELECT * FROM 'file'`.

### ATTACH, USE, DETACH, other databases (DuckDB statements)

**Syntax**

```sql
ATTACH [DATABASE] [IF NOT EXISTS] 'file.duckdb' [AS alias] [(READ_ONLY)];
ATTACH 'file.sqlite' AS lite (TYPE sqlite);          -- sqlite extension
ATTACH 'dbname=x host=... user=...' AS pg (TYPE postgres, READ_ONLY);   ATTACH 'host=... database=x' AS my (TYPE mysql);
USE alias;   DETACH alias;   SHOW DATABASES;   SELECT * FROM alias.main.tablename;
CREATE SECRET [name] (TYPE s3, KEY_ID '...', SECRET '...', REGION 'us-east-1');   -- or PROVIDER credential_chain
```

## Friendly SQL extensions

### SELECT * EXCLUDE / REPLACE / RENAME, COLUMNS() (DuckDB SELECT extensions)

**Syntax**

```sql
SELECT * EXCLUDE (col1, col2) FROM t;
SELECT * REPLACE (round(price, 2) AS price, upper(name) AS name) FROM t;
SELECT * RENAME (old AS new) FROM t;
SELECT COLUMNS('sales_.*') FROM t;                  -- regex on column names
SELECT max(COLUMNS(* EXCLUDE id)) FROM t;           -- apply to many columns
SELECT COLUMNS(c -> c LIKE '%_amt') FROM t;         -- lambda filter
SELECT t.* , *  ...;  SELECT #1, #2 FROM t;         -- positional column references
```

### FROM-first, GROUP BY ALL, ORDER BY ALL, QUALIFY, LIMIT % (DuckDB query extensions)

**Syntax**

```sql
FROM t;                                   -- = SELECT * FROM t
FROM t SELECT a, b WHERE a > 1;
SELECT city, year, sum(sales) FROM t GROUP BY ALL;     -- groups by every non-aggregated column
SELECT * FROM t ORDER BY ALL [DESC];      -- by all columns, left to right
SELECT *, row_number() OVER (PARTITION BY g ORDER BY ts DESC) AS rn FROM t QUALIFY rn = 1;
SELECT * FROM t LIMIT 10%;                -- percentage
SELECT * FROM t USING SAMPLE 10% [(bernoulli | reservoir | system)];   SELECT * FROM t USING SAMPLE 1000 ROWS;
SELECT a, b FROM t WHERE a > 0 ORDER BY b NULLS LAST LIMIT 5 OFFSET 10;
```

**Example**

```sql
-- latest row per customer
FROM orders QUALIFY row_number() OVER (PARTITION BY customer_id ORDER BY order_ts DESC) = 1;
```

### DESCRIBE, SUMMARIZE, SHOW (DuckDB utility statements)

**Syntax**

```sql
DESCRIBE t;   DESCRIBE SELECT * FROM 'file.parquet';     -- column names and types
SUMMARIZE t;  SUMMARIZE SELECT * FROM 'data.csv';        -- min, max, approx_unique, avg, std, quartiles, null %
SHOW TABLES;  SHOW ALL TABLES;  SHOW t;
PRAGMA table_info('t');   SELECT * FROM duckdb_tables();  duckdb_columns()  duckdb_functions()  duckdb_settings()  duckdb_extensions()
EXPLAIN [ANALYZE] SELECT ...;
```

### PIVOT and UNPIVOT (DuckDB statements)

**Syntax**

```sql
PIVOT <table | (query)> ON <column(s) whose values become columns> [IN (v1, v2, ...)] USING <aggregate(s)> [GROUP BY <rows>] [ORDER BY ...];
UNPIVOT <table> ON <columns | COLUMNS(* EXCLUDE (id))> INTO NAME <name_col> VALUE <value_col>;
-- SQL-standard form:
SELECT * FROM t PIVOT (sum(sales) FOR quarter IN ('Q1', 'Q2', 'Q3', 'Q4') GROUP BY region);
SELECT * FROM t UNPIVOT (sales FOR quarter IN (q1, q2, q3, q4));
```

**Example**

```sql
PIVOT sales ON year USING sum(amount) GROUP BY region;      -- one column per distinct year (dynamic)
UNPIVOT monthly ON jan, feb, mar INTO NAME month VALUE amount;
```

### Special joins: ASOF, POSITIONAL, SEMI, ANTI, LATERAL (DuckDB joins)

**Syntax**

```sql
SELECT * FROM trades t ASOF JOIN quotes q ON t.symbol = q.symbol AND t.ts >= q.ts;   -- nearest earlier quote
SELECT * FROM a POSITIONAL JOIN b;                 -- row 1 with row 1, etc.
SELECT * FROM a SEMI JOIN b ON a.id = b.id;        -- rows of a that have a match (no duplication)
SELECT * FROM a ANTI JOIN b ON a.id = b.id;        -- rows of a without a match
SELECT * FROM a, LATERAL (SELECT * FROM b WHERE b.a_id = a.id ORDER BY b.ts DESC LIMIT 1);
SELECT * FROM a JOIN b USING (id);  SELECT * FROM a NATURAL JOIN b;
```

**Check**

- An `ASOF JOIN` needs exactly one inequality (`>=`, `>`, `<=` or `<`) plus any number of equality conditions.

## Types and literals

### DuckDB data types (DuckDB types)

**Syntax**

```sql
BOOLEAN  TINYINT SMALLINT INTEGER BIGINT HUGEINT (128-bit)  UTINYINT USMALLINT UINTEGER UBIGINT UHUGEINT
FLOAT (REAL)  DOUBLE  DECIMAL(p, s)  VARCHAR (TEXT, STRING)  BLOB  BIT  UUID  JSON (json extension)
DATE  TIME  TIMESTAMP  TIMESTAMPTZ  TIMESTAMP_S / _MS / _NS  INTERVAL
INTEGER[]  /  LIST                     -- list: [1, 2, 3]
INTEGER[3]  /  ARRAY                   -- fixed-size array
STRUCT(a INTEGER, b VARCHAR)           -- struct: {'a': 1, 'b': 'x'}   access s.a or s['a']
MAP(VARCHAR, INTEGER)                  -- map: MAP {'k': 1}            access m['k']
UNION(num INTEGER, txt VARCHAR)        ENUM ('a', 'b')  -- CREATE TYPE mood AS ENUM ('sad', 'ok', 'happy');
```

**Literals:** `DATE '2024-01-31'` · `TIMESTAMP '2024-01-31 10:00:00'` · `INTERVAL 3 DAY` · `INTERVAL '2 hours'` · `[1, 2]` · `{'k': 'v'}` · `'\x41'::BLOB` · `1_000_000`.

**Check**

- `x::TYPE` and `CAST(x AS TYPE)` are equivalent. Use `TRY_CAST` for data that may not convert.

## Functions

### List functions (DuckDB functions)

**Syntax**

```sql
[1, 2, 3]   list_value(1, 2)   l[1]  l[-1]  l[2:3]   len(l) / array_length(l)   list_contains(l, x) / list_has(l, x)   list_position(l, x)
list_append(l, x)  list_prepend(x, l)  list_concat(a, b) / a || b  list_distinct(l)  list_unique(l) (count)  list_sort(l [, 'DESC'])  list_reverse_sort(l)
list_slice(l, a, b)  flatten(ll)  list_aggregate(l, 'sum') / list_sum(l)  list_min  list_max  list_avg  list_has_any(a, b)  list_has_all(a, b)
list_transform(l, x -> x * 2)  list_filter(l, x -> x > 0)  list_reduce(l, (a, x) -> a + x)  [x * 2 FOR x IN l IF x > 0]  -- list comprehension
unnest(l)  -- one row per element;  unnest(struct_col)  generate_series(1, 10)  range(0, 10, 2)
string_split(s, ',')  -- → list;  array_to_string(l, ', ') / list_aggr(l, 'string_agg', ',')
list(x) / array_agg(x)  -- aggregate rows into a list
```

**Example**

```sql
SELECT id, unnest(string_split(tags, ',')) AS tag FROM posts;
SELECT list_filter([1, -2, 3], x -> x > 0);   -- [1, 3]
```

### String functions (DuckDB functions)

**Syntax**

```sql
s || t  concat(a, b, ...)  concat_ws(sep, a, b)  length(s)  strlen(s) (bytes)  upper(s)  lower(s)  (no initcap: write upper(s[1]) || lower(s[2:]))
s[1:3]  substring(s, start, len)  left(s, n)  right(s, n)  trim(s [, chars])  ltrim  rtrim  lpad(s, n, c)  rpad(s, n, c)  repeat(s, n)  reverse(s)
replace(s, from, to)  translate(s, from, to)  position(sub IN s) / strpos(s, sub) / instr(s, sub)  contains(s, sub)  starts_with(s, p)  ends_with(s, p)
s LIKE 'a%'  s ILIKE 'a%'  s SIMILAR TO 're'  s GLOB '*.csv'
regexp_matches(s, re [, 'i'])  regexp_extract(s, re [, group | name_list])  regexp_extract_all(s, re [, group])  regexp_replace(s, re, repl [, 'g'])  regexp_split_to_array(s, re)
split_part(s, sep, n)  string_split(s, sep)  format('{} has {:.2f}', name, x)  printf('%s %d', a, b)  md5(s)  sha256(s)  hash(x)
levenshtein(a, b)  jaro_winkler_similarity(a, b)  jaccard(a, b)  damerau_levenshtein(a, b)  ascii(s)  chr(n)  unicode(s)  to_base64(blob)  from_base64(s)
string_agg(s, ', ' ORDER BY s)  -- aggregate
```

**Check**

- By default, `regexp_replace` replaces only the first match. Pass `'g'` to replace all of them.

### Date and time functions (DuckDB functions)

**Syntax**

```sql
current_date  current_timestamp / now()  today()
date_trunc('year' | 'quarter' | 'month' | 'week' | 'day' | 'hour' | 'minute', ts)    date_part('dow', d) / extract(year FROM d)
date_diff('day' | 'month' | ..., start, end) / datediff(...)   date_sub('day', start, end)   date_add(d, INTERVAL 7 DAY) / d + INTERVAL 7 DAY
year(d) month(d) day(d) dayofweek(d) (0 = Sunday) isodow(d) dayofyear(d) week(d) quarter(d) hour(ts) minute(ts) second(ts)
last_day(d)  make_date(y, m, d)  make_timestamp(y, m, d, h, mi, s)  epoch(ts) / epoch_ms(ts)  to_timestamp(epoch_seconds)  age(ts1, ts2)
strftime(ts, '%Y-%m-%d %H:%M:%S')      strptime('31/01/2024', '%d/%m/%Y')   try_strptime(...)
time_bucket(INTERVAL '15 minutes', ts)   generate_series(DATE '2024-01-01', DATE '2024-12-31', INTERVAL 1 MONTH)
```

**Format codes:** `%Y` year · `%y` 2-digit year · `%m` month 01-12 · `%-m` month without padding · `%d` day · `%H` 0-23 · `%I` 1-12 · `%M` minute · `%S` second · `%f` microseconds · `%p` AM/PM · `%b`/`%B` month name · `%a`/`%A` weekday · `%j` day of year · `%z` UTC offset · `%Z` time zone.

**Check**

- In `strftime` codes, `%M` means MINUTES and `%m` means MONTH.

### Aggregate and statistical functions (DuckDB functions)

**Syntax**

```sql
count(*)  count(x)  count(DISTINCT x)  sum  avg/mean  min  max  any_value(x)  first(x ORDER BY t)  last(x ORDER BY t)
arg_max(x, by) / max_by(x, by)  arg_min(x, by) / min_by(x, by)    -- value of x at the max of by
median(x)  quantile_cont(x, 0.9) / quantile_disc  mode(x)  approx_count_distinct(x)  approx_quantile(x, 0.5)  histogram(x)
stddev_samp  stddev_pop  var_samp  var_pop  corr(y, x)  covar_pop  regr_slope(y, x)  entropy(x)  kurtosis(x)  skewness(x)
bool_and(c)  bool_or(c)  bit_and  bit_or  string_agg(s, ',')  list(x)  histogram(x)
<agg> FILTER (WHERE cond)      -- conditional aggregate
```

**Example**

```sql
SELECT customer_id, arg_max(status, updated_at) AS latest_status, count(*) FILTER (WHERE status = 'error') AS errors
FROM events GROUP BY ALL;
```

### Window functions in DuckDB (DuckDB functions)

**Syntax**

```sql
row_number()  rank()  dense_rank()  percent_rank()  cume_dist()  ntile(n)  lag(x [, n [, default]])  lead(...)  first_value(x)  last_value(x)  nth_value(x, n)
<agg>(x) OVER ([PARTITION BY ...] [ORDER BY ...] [ROWS | RANGE | GROUPS BETWEEN ... AND ...] [EXCLUDE ...])
SELECT ..., sum(x) OVER w FROM t WINDOW w AS (PARTITION BY g ORDER BY ts);
lag(x IGNORE NULLS) OVER (...)
```

### Conditional and utility functions (DuckDB functions)

**Syntax**

```sql
CASE WHEN ... THEN ... ELSE ... END   if(cond, a, b)   coalesce(a, b)  ifnull(a, b)  nullif(a, b)  greatest(a, b)  least(a, b)
typeof(x)  TRY_CAST(x AS type)  try(expr)  error('msg')  uuid() / gen_random_uuid()  random()  setseed(0.5)
round(x, d)  floor  ceil  abs  pow(x, y) / x ** y / x ^ y  sqrt  ln  log10  log2  exp  sign  x // y (integer division)  x % y  bit_count(x)  factorial(n)
```

### JSON functions (DuckDB json extension)

**Syntax**

```sql
j->'$.a'  j->>'$.a' (as text)  j['a']  json_extract(j, '$.a.b')  json_extract_string(j, '$.a')  json_value(j, '$.a')
json_array_length(j [, path])  json_keys(j)  json_type(j)  json_valid(s)  json_structure(j)  json_transform(j, '{"a": "INTEGER"}')
to_json(x)  json_object('k', v)  json_array(a, b)  json_group_array(x)  json_group_object(k, v)  unnest(json_extract(j, '$.items[*]'))
```

## DDL and DML

### CREATE TABLE, INSERT, UPDATE, DELETE in DuckDB (DuckDB statements)

**Syntax**

```sql
CREATE [OR REPLACE] [TEMP] TABLE [IF NOT EXISTS] t (id INTEGER PRIMARY KEY, name VARCHAR NOT NULL, amount DECIMAL(12,2) DEFAULT 0, tags VARCHAR[], CHECK (amount >= 0));
CREATE [OR REPLACE] TABLE t AS SELECT ...;           -- CTAS
CREATE SEQUENCE seq START 1;  CREATE TABLE t (id INTEGER DEFAULT nextval('seq'), ...);
INSERT INTO t [BY NAME] SELECT ...;                  -- BY NAME matches columns by name
INSERT [OR REPLACE | OR IGNORE] INTO t VALUES (1, 'a'), (2, 'b');
INSERT INTO t VALUES (...) ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name;   ON CONFLICT DO NOTHING
INSERT INTO t ... RETURNING *;
UPDATE t SET x = s.x FROM s WHERE t.id = s.id;   DELETE FROM t WHERE ...;   TRUNCATE t;
MERGE INTO t USING s ON t.id = s.id WHEN MATCHED THEN UPDATE SET ... WHEN NOT MATCHED THEN INSERT ...;   -- 1.4+
ALTER TABLE t ADD COLUMN c INTEGER;  ALTER TABLE t RENAME COLUMN a TO b;  ALTER TABLE t ALTER c TYPE BIGINT;  ALTER TABLE t DROP COLUMN c;
CREATE VIEW v AS SELECT ...;  CREATE INDEX idx ON t (col);  DROP TABLE IF EXISTS t;
```

**Check**

- `ON CONFLICT` needs a PRIMARY KEY or UNIQUE constraint on the target columns.

### Macros, CTEs and recursive queries (DuckDB statements)

**Syntax**

```sql
CREATE [OR REPLACE] MACRO add_tax(x, rate := 0.2) AS x * (1 + rate);              -- scalar macro
CREATE MACRO top_n(tbl, n) AS TABLE SELECT * FROM query_table(tbl) LIMIT n;       -- table macro
SELECT add_tax(price) FROM items;   SELECT * FROM top_n('items', 5);
WITH RECURSIVE r(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM r WHERE n < 10) SELECT * FROM r;
WITH cte AS MATERIALIZED (SELECT ...) SELECT ...;
```

### Settings, extensions and pragmas (DuckDB statements)

**Syntax**

```sql
SET threads = 8;  SET memory_limit = '4GB';  SET temp_directory = 'tmp/';  SET preserve_insertion_order = false;  RESET threads;
SELECT current_setting('threads');
INSTALL httpfs; LOAD httpfs;   INSTALL spatial; LOAD spatial;   INSTALL excel; LOAD excel;   -- extensions (many autoload)
SELECT * FROM 'https://example.com/data.parquet';                                           -- needs httpfs
SELECT * FROM read_xlsx('book.xlsx', sheet = 'Sheet1');                                      -- excel extension 1.2+
PRAGMA database_size;  CHECKPOINT;  VACUUM;  .mode / .read / .open  -- the last three are CLI dot commands, not SQL
```

**Check**

- CLI dot commands (`.tables`, `.mode csv`, `.output file`) work only in the `duckdb` shell, not through the Python, R or JDBC APIs.
