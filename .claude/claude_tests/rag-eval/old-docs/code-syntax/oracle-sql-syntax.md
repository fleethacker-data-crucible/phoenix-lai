# Oracle SQL syntax reference (Phoenix)

For Phoenix models writing and checking Oracle Database SQL and PL/SQL (Oracle 19c, 21c and 23ai, with version notes). Each `###` entry is one statement, clause, function or PL/SQL construct: what it does, its exact syntax, every argument, an example, and the mistakes to check for. Standard SQL that Oracle shares is in `sql-syntax.md`. This sheet covers what is Oracle-specific or different.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply · **23ai+** / **12c+** = minimum version.

## Checklist: before you answer with Oracle SQL

- Before 23ai, there is no `LIMIT`, no `SELECT` without `FROM`, and no `BOOLEAN` column type. Use `FETCH FIRST n ROWS ONLY` (12c+) or `ROWNUM`, and `FROM DUAL` for expressions: `SELECT SYSDATE FROM DUAL;`.
- No `AS` before table aliases: `FROM employees e`, not `FROM employees AS e` (that is an error). `AS` is fine for column aliases.
- In Oracle, the empty string `''` IS NULL: `WHERE col = ''` matches nothing. Use `IS NULL`.
- Identifiers are stored in UPPER CASE unless double-quoted. `"MyTable"` must then always be quoted with exactly that case. Before 12.2, names are at most 30 bytes; from 12.2, 128.
- Use `VARCHAR2`, not `VARCHAR`. `NUMBER(p,s)` covers integers and decimals. `DATE` includes a time (to the second), so `TRUNC(d)` removes it.
- Concatenate with `||` (`CONCAT` takes only 2 arguments). String positions are 1-based (`SUBSTR`, `INSTR`).
- Dates: never rely on the implicit format. Use `DATE '2024-01-31'`, `TIMESTAMP '2024-01-31 10:00:00'` or `TO_DATE('31/01/2024', 'DD/MM/YYYY')`. In format masks, `MM` is the month and `MI` the minutes.
- `ROWNUM` is assigned before ORDER BY. Sort in a subquery first, or use `FETCH FIRST`.
- PL/SQL blocks end with `END;`, followed by `/` on its own line in SQL*Plus and SQLcl scripts. Every statement ends with `;`.
- DDL (`CREATE`, `ALTER`, `DROP`, `TRUNCATE`) commits implicitly and cannot be rolled back.
- `EXCEPT` is `MINUS` before 21c. Write `WITH` without `RECURSIVE`. Recursive CTEs need a column alias list.
- For bind variables, use `:name` (never concatenate user input). Inside PL/SQL, use `EXECUTE IMMEDIATE … USING`.

## Queries

### SELECT in Oracle (Oracle SQL statement)

**Syntax**

```sql
[WITH <name> [(<cols>)] AS (<query>) [, ...]]
SELECT [DISTINCT | UNIQUE | ALL] <select_list>
FROM <table> [<alias>] [, ...] [{INNER | LEFT | RIGHT | FULL} [OUTER] JOIN <t2> <a2> ON <cond>] [CROSS | OUTER] APPLY (<query>)
[WHERE <cond>]
[START WITH <cond>] [CONNECT BY [NOCYCLE] PRIOR <child> = <parent>]
[GROUP BY {<expr> | ROLLUP (...) | CUBE (...) | GROUPING SETS (...)}] [HAVING <cond>]
[MODEL ...]
[ORDER BY <expr> [ASC | DESC] [NULLS {FIRST | LAST}] [, ...] | ORDER SIBLINGS BY ...]
[OFFSET <n> {ROW | ROWS}] [FETCH {FIRST | NEXT} [<n> | <p> PERCENT] {ROW | ROWS} {ONLY | WITH TIES}]
[FOR UPDATE [OF <col>] [NOWAIT | WAIT <s> | SKIP LOCKED]];
```

**Example**

```sql
SELECT e.employee_id, e.last_name, d.department_name, e.salary
FROM employees e
JOIN departments d ON d.department_id = e.department_id
WHERE e.hire_date >= DATE '2020-01-01'
ORDER BY e.salary DESC
FETCH FIRST 10 ROWS ONLY;
```

**Check**

- `FETCH FIRST` and `OFFSET` are 12c+. On 11g, use `SELECT * FROM (SELECT ... ORDER BY ...) WHERE ROWNUM <= 10`.
- `CROSS APPLY` and `OUTER APPLY` are 12c+.

### DUAL, ROWNUM, ROWID, pseudocolumns (Oracle SQL)

**Syntax**

```sql
SELECT SYSDATE, USER, SYS_GUID() FROM DUAL;          -- DUAL: one-row dummy table (optional in 23ai+)
ROWNUM                 -- 1, 2, 3 ... in the order rows are fetched (before ORDER BY)
ROWID                  -- physical row address
LEVEL                  -- depth in CONNECT BY queries
CONNECT_BY_ISLEAF  CONNECT_BY_ISCYCLE  CONNECT_BY_ROOT col  SYS_CONNECT_BY_PATH(col, '/')
<sequence>.NEXTVAL  <sequence>.CURRVAL
ORA_ROWSCN
```

**Check**

- `WHERE ROWNUM = 2` and `WHERE ROWNUM > 1` return no rows. Only `<`, `<=` and `= 1` work directly.
- `ROWNUM` with `ORDER BY` in the same SELECT numbers the rows before sorting. Wrap the query in a subquery.

### Old-style outer join (+) (Oracle SQL syntax)

**Syntax**

```sql
SELECT ... FROM a, b WHERE a.id = b.a_id(+);    -- LEFT JOIN: (+) on the optional (deficient) side
```

**Check**

- This is legacy syntax. Prefer ANSI `LEFT JOIN`. The `(+)` form cannot express a FULL join, and cannot be mixed with ANSI joins in one query block.

### Hierarchical queries: START WITH ... CONNECT BY (Oracle SQL clause)

**Syntax**

```sql
SELECT LEVEL, LPAD(' ', 2 * (LEVEL - 1)) || last_name AS tree, SYS_CONNECT_BY_PATH(last_name, '/') AS path
FROM employees
START WITH manager_id IS NULL
CONNECT BY [NOCYCLE] PRIOR employee_id = manager_id
ORDER SIBLINGS BY last_name;
```

**Parts**

- `START WITH`: the root rows.
- `PRIOR`: marks the parent side of the condition.
- `NOCYCLE`: stops at loops (find them with `CONNECT_BY_ISCYCLE`).
- `ORDER SIBLINGS BY`: sorts within each level and keeps the tree order.

**Example**

```sql
SELECT LEVEL AS n FROM DUAL CONNECT BY LEVEL <= 10;    -- number generator 1..10
```

### Recursive WITH (Oracle SQL clause)

**Syntax**

```sql
WITH org (employee_id, manager_id, lvl) AS (           -- column list is required
    SELECT employee_id, manager_id, 1 FROM employees WHERE manager_id IS NULL
    UNION ALL
    SELECT e.employee_id, e.manager_id, o.lvl + 1 FROM employees e JOIN org o ON e.manager_id = o.employee_id
)
[SEARCH DEPTH FIRST BY employee_id SET ord]
[CYCLE employee_id SET is_cycle TO 'Y' DEFAULT 'N']
SELECT * FROM org;
```

**Check**

- Oracle does not accept the `RECURSIVE` keyword.

### PIVOT and UNPIVOT (Oracle SQL clauses)

**Syntax**

```sql
SELECT * FROM (SELECT region, quarter, sales FROM fact)
PIVOT (SUM(sales) FOR quarter IN ('Q1' AS q1, 'Q2' AS q2, 'Q3' AS q3, 'Q4' AS q4));
SELECT * FROM wide_table
UNPIVOT [INCLUDE NULLS | EXCLUDE NULLS] (sales FOR quarter IN (q1 AS 'Q1', q2 AS 'Q2'));
```

**Check**

- The IN list must be literal values. There are no dynamic pivot columns without dynamic SQL (or `PIVOT XML`).

### Analytic (window) functions (Oracle SQL functions)

**Syntax**

```sql
<function>(args) OVER ([PARTITION BY ...] [ORDER BY ...] [{ROWS | RANGE} BETWEEN ... AND ...])
ROW_NUMBER() RANK() DENSE_RANK() PERCENT_RANK() CUME_DIST() NTILE(n)
LAG(expr [, offset [, default]]) [IGNORE NULLS] OVER (...)   LEAD(...)   FIRST_VALUE(expr [IGNORE NULLS])   LAST_VALUE(...)   NTH_VALUE(expr, n)
RATIO_TO_REPORT(expr) OVER (...)   LISTAGG(expr, ',') WITHIN GROUP (ORDER BY ...) OVER (PARTITION BY ...)
MAX(sal) KEEP (DENSE_RANK FIRST ORDER BY hire_date) OVER (PARTITION BY dept)
```

**Example**

```sql
SELECT department_id, last_name, salary,
       RANK() OVER (PARTITION BY department_id ORDER BY salary DESC) AS rnk,
       salary - LAG(salary, 1, salary) OVER (PARTITION BY department_id ORDER BY hire_date) AS diff
FROM employees;
```

### Aggregate functions specific to Oracle (Oracle SQL functions)

**Syntax**

```sql
LISTAGG([DISTINCT] expr [, 'delimiter']) [ON OVERFLOW {ERROR | TRUNCATE ['...'] [WITH | WITHOUT COUNT]}] WITHIN GROUP (ORDER BY expr)
COLLECT(expr)   MEDIAN(expr)   STATS_MODE(expr)   APPROX_COUNT_DISTINCT(expr)   APPROX_PERCENTILE(0.5) WITHIN GROUP (ORDER BY x)
<agg> KEEP (DENSE_RANK {FIRST | LAST} ORDER BY expr)    -- e.g. MAX(status) KEEP (DENSE_RANK LAST ORDER BY changed_at)
GROUPING(col)  GROUPING_ID(col1, col2)  GROUP_ID()
```

**Check**

- `LISTAGG` raises ORA-01489 when the result exceeds 4000 bytes (or 32767 with extended strings). Add `ON OVERFLOW TRUNCATE` (12.2+).

## Functions

### NULL functions: NVL, NVL2, COALESCE, NULLIF, DECODE, LNNVL (Oracle SQL functions)

**Syntax**

```sql
NVL(expr, replacement)                    -- replacement when expr is NULL (both args always evaluated)
NVL2(expr, if_not_null, if_null)
COALESCE(e1, e2, ...)                     -- first non-NULL, short-circuits
NULLIF(a, b)                              -- NULL if a = b
DECODE(expr, search1, result1 [, search2, result2 ...] [, default])   -- treats NULL = NULL as a match
LNNVL(condition)                          -- TRUE when the condition is FALSE or UNKNOWN
```

**Example**

```sql
SELECT last_name, NVL(commission_pct, 0) * salary AS bonus,
       DECODE(status, 'A', 'Active', 'I', 'Inactive', 'Unknown') AS status_text
FROM employees;
```

**Check**

- `NVL` converts the second argument to the type of the first: `NVL(num_col, 'n/a')` fails with ORA-01722. Write `NVL(TO_CHAR(num_col), 'n/a')`.

### String functions (Oracle SQL functions)

**Syntax**

```sql
s1 || s2                  CONCAT(s1, s2)             LENGTH(s)  LENGTHB(s)   UPPER(s)  LOWER(s)  INITCAP(s)
SUBSTR(s, start [, len])  -- start 1-based; negative start counts from the end
INSTR(s, sub [, start [, occurrence]])   -- 0 when absent
LPAD(s, n [, pad])  RPAD(s, n [, pad])   TRIM([{LEADING | TRAILING | BOTH} [ch] FROM] s)  LTRIM(s [, set])  RTRIM(s [, set])
REPLACE(s, find [, replacement])   TRANSLATE(s, from_chars, to_chars)   REVERSE(s)   ASCII(c)  CHR(n)  -- CHR(10) newline
SOUNDEX(s)   NLS_UPPER(s)   UNISTR('\00e9')   DUMP(expr)
REGEXP_LIKE(s, pattern [, match_param])               -- condition, in WHERE
REGEXP_SUBSTR(s, pattern [, pos [, occurrence [, match_param [, subexpr]]]])
REGEXP_INSTR(s, pattern [, pos [, occurrence [, return_opt [, match_param [, subexpr]]]]])
REGEXP_REPLACE(s, pattern [, replace [, pos [, occurrence [, match_param]]]])
REGEXP_COUNT(s, pattern [, pos [, match_param]])
-- match_param: 'i' ignore case, 'c' case-sensitive, 'n' dot matches newline, 'm' multiline, 'x' ignore whitespace
```

**Example**

```sql
SELECT REGEXP_SUBSTR('a,b,,c', '[^,]+', 1, LEVEL) AS item
FROM DUAL CONNECT BY REGEXP_SUBSTR('a,b,,c', '[^,]+', 1, LEVEL) IS NOT NULL;   -- split a CSV string
```

**Check**

- `REPLACE(s, 'x')` with no third argument deletes `'x'`.

### Number functions (Oracle SQL functions)

**Syntax**

```sql
ROUND(n [, d])  TRUNC(n [, d])  CEIL(n)  FLOOR(n)  MOD(a, b)  REMAINDER(a, b)  ABS(n)  SIGN(n)  POWER(b, e)  SQRT(n)
EXP(n)  LN(n)  LOG(base, n)  GREATEST(a, b, ...)  LEAST(a, b, ...)  WIDTH_BUCKET(x, lo, hi, n)  BITAND(a, b)
DBMS_RANDOM.VALUE([lo, hi])   DBMS_RANDOM.STRING('U', 8)
```

**Check**

- `GREATEST` and `LEAST` return NULL if ANY argument is NULL.

### Date and time functions (Oracle SQL functions)

**Syntax**

```sql
SYSDATE  SYSTIMESTAMP  CURRENT_DATE  CURRENT_TIMESTAMP  LOCALTIMESTAMP  DBTIMEZONE  SESSIONTIMEZONE
d + 1                      -- add 1 day;  d + 1/24 = add 1 hour;  d2 - d1 = days (NUMBER)
ADD_MONTHS(d, n)   MONTHS_BETWEEN(d2, d1)   LAST_DAY(d)   NEXT_DAY(d, 'MONDAY')
TRUNC(d [, 'MM' | 'YYYY' | 'Q' | 'IW' | 'DD' | 'HH24' | 'MI'])   ROUND(d [, fmt])
EXTRACT({YEAR | MONTH | DAY | HOUR | MINUTE | SECOND} FROM d)       -- HOUR..SECOND need a TIMESTAMP
NUMTODSINTERVAL(n, 'DAY' | 'HOUR' | 'MINUTE' | 'SECOND')   NUMTOYMINTERVAL(n, 'YEAR' | 'MONTH')
INTERVAL '7' DAY   INTERVAL '2' HOUR   TO_TIMESTAMP_TZ(...)   FROM_TZ(ts, 'Europe/Paris')   ts AT TIME ZONE 'UTC'   CAST(ts AS DATE)
```

**Example**

```sql
SELECT * FROM orders
WHERE order_date >= TRUNC(SYSDATE, 'MM')            -- first day of this month
  AND order_date <  ADD_MONTHS(TRUNC(SYSDATE, 'MM'), 1);
```

**Check**

- `TRUNC(d)` sets the time to midnight. `WHERE order_date = DATE '2024-01-31'` misses rows with times, so use a range.
- `ADD_MONTHS(DATE '2024-01-31', 1)` returns Feb 29 (the last day of the month).

### Conversion: TO_CHAR, TO_DATE, TO_NUMBER, CAST and format models (Oracle SQL functions)

**Syntax**

```sql
TO_CHAR(date | number [, 'fmt' [, 'nlsparam']])
TO_DATE(string [DEFAULT value ON CONVERSION ERROR] [, 'fmt' [, 'nlsparam']])
TO_TIMESTAMP(string, 'fmt')   TO_NUMBER(string [DEFAULT n ON CONVERSION ERROR] [, 'fmt'])
CAST(expr AS type [DEFAULT v ON CONVERSION ERROR])   VALIDATE_CONVERSION(expr AS type [, fmt])   -- 12.2+
```

**Date format elements:** `YYYY` 4-digit year · `YY` · `RRRR`/`RR` (century rule) · `MM` month 01-12 · `MON` JAN · `MONTH` JANUARY · `DD` day of month · `DDD` day of year · `DY` MON · `DAY` MONDAY · `D` day of week (NLS) · `HH24` 0-23 · `HH`/`HH12` · `AM`/`PM` · `MI` minutes · `SS` seconds · `SSSSS` seconds after midnight · `FF[1-9]` fractional seconds · `TZH:TZM` · `TZR` region · `Q` quarter · `WW` week of year · `IW` ISO week · `IYYY` ISO year · `J` Julian day · `FM` no padding · `FX` exact match · `"text"` literal.

**Number format elements:** `9` digit · `0` leading zero · `.` or `D` decimal · `,` or `G` group · `$` · `L` local currency · `MI` trailing minus · `PR` <negative> · `S` sign · `EEEE` scientific · `X` hex · `FM`.

**Example**

```sql
SELECT TO_CHAR(SYSDATE, 'YYYY-MM-DD HH24:MI:SS') AS now_text,
       TO_CHAR(1234567.891, 'FM9G999G990D00') AS amount,
       TO_DATE('31/01/2024 14:05', 'DD/MM/YYYY HH24:MI') AS d
FROM DUAL;
```

**Check**

- `'YYYY-MM-DD HH:MM:SS'` is a classic bug: `MM` is the month, so the minutes are `MI`, and `HH` is 12-hour. Use `HH24:MI:SS`.
- `TO_CHAR(d, 'Month')` pads to 9 characters. Add `FM`: `'FMMonth'`.

### JSON in Oracle (Oracle SQL functions)

**Syntax**

```sql
JSON_VALUE(doc, '$.a.b' [RETURNING NUMBER] [NULL | ERROR | DEFAULT x ON ERROR])
JSON_QUERY(doc, '$.items' [WITH WRAPPER])   JSON_EXISTS(doc, '$.a?(@ > 10)')
JSON_TABLE(doc, '$.items[*]' COLUMNS (id NUMBER PATH '$.id', name VARCHAR2(100) PATH '$.name', NESTED PATH '$.tags[*]' COLUMNS (tag VARCHAR2(30) PATH '$')))
JSON_OBJECT('id' VALUE id, 'name' VALUE name)   JSON_ARRAYAGG(expr ORDER BY x)   JSON_OBJECTAGG(KEY k VALUE v)
t.doc.a.b                       -- simple dot notation (needs a table alias and an IS JSON check / JSON type)
col JSON                        -- native JSON type 21c+
```

## Changing data

### INSERT, multi-table INSERT, RETURNING (Oracle SQL statements)

**Syntax**

```sql
INSERT INTO t (c1, c2) VALUES (v1, v2);
INSERT INTO t (c1, c2) SELECT ... ;
INSERT INTO t (c1, c2) VALUES (1, 'a'), (2, 'b');        -- 23ai+ only; older: INSERT ALL or UNION ALL select
INSERT ALL
    INTO t1 (a) VALUES (x)
    INTO t2 (b) VALUES (y)
SELECT x, y FROM src;
INSERT FIRST WHEN amt > 1000 THEN INTO big VALUES (id, amt) ELSE INTO small VALUES (id, amt) SELECT id, amt FROM src;
INSERT /*+ APPEND */ INTO t SELECT ...;                  -- direct-path load
INSERT INTO t (...) VALUES (...) RETURNING id INTO :new_id;   -- PL/SQL or bind variable
```

**Check**

- Before 23ai, a multi-row `VALUES (…), (…)` is a syntax error (ORA-00933). Use `INSERT ALL … SELECT * FROM DUAL`, or `INSERT … SELECT … FROM DUAL UNION ALL SELECT …`.

### UPDATE, DELETE, MERGE in Oracle (Oracle SQL statements)

**Syntax**

```sql
UPDATE t SET c1 = v1, c2 = (SELECT ... ) WHERE ...;
UPDATE t SET (c1, c2) = (SELECT a, b FROM s WHERE s.id = t.id) WHERE EXISTS (SELECT 1 FROM s WHERE s.id = t.id);
UPDATE t1 SET t1.x = t2.y FROM t1 JOIN t2 ON ...;         -- 23ai+ only
DELETE [FROM] t WHERE ...;
MERGE INTO target t
USING (SELECT ...) s
ON (t.id = s.id)                                          -- parentheses required
WHEN MATCHED THEN UPDATE SET t.c = s.c [WHERE ...] [DELETE WHERE ...]
WHEN NOT MATCHED THEN INSERT (id, c) VALUES (s.id, s.c) [WHERE ...];
```

**Check**

- In `MERGE … UPDATE SET`, you cannot update the columns used in the `ON` clause (ORA-38104).
- `UPDATE … FROM` is 23ai+. Before that, use a correlated subquery or MERGE.

## Defining data (DDL)

### Oracle data types (Oracle SQL types)

**Syntax**

```sql
VARCHAR2(n [BYTE | CHAR])   -- up to 4000 bytes (32767 with MAX_STRING_SIZE=EXTENDED)
NVARCHAR2(n)  CHAR(n)  NCHAR(n)  CLOB  NCLOB  LONG (deprecated)
NUMBER [(p [, s])]  -- p 1-38; NUMBER(10) integer; NUMBER(12,2) money
BINARY_FLOAT  BINARY_DOUBLE  FLOAT(b)  INTEGER (= NUMBER(38))
DATE                -- date + time to the second
TIMESTAMP [(f)] [WITH [LOCAL] TIME ZONE]   INTERVAL YEAR TO MONTH   INTERVAL DAY TO SECOND
RAW(n)  BLOB  BFILE  ROWID  UROWID  XMLTYPE  JSON (21c+)  BOOLEAN (23ai+ in SQL; PL/SQL always)  VECTOR (23ai+)
```

**Check**

- `VARCHAR2(10)` defaults to 10 BYTES, so multi-byte characters fit fewer than 10 characters. Use `VARCHAR2(10 CHAR)` when that matters.
- Before 23ai there is no BOOLEAN column type. Use `NUMBER(1)` or `CHAR(1)` with a CHECK constraint.

### CREATE TABLE in Oracle (Oracle SQL statement)

**Syntax**

```sql
CREATE [GLOBAL TEMPORARY | PRIVATE TEMPORARY] TABLE [IF NOT EXISTS (23ai+)] [schema.]name (
    id        NUMBER GENERATED {ALWAYS | BY DEFAULT [ON NULL]} AS IDENTITY [(START WITH 1 INCREMENT BY 1)] PRIMARY KEY,   -- 12c+
    name      VARCHAR2(100 CHAR) NOT NULL,
    status    VARCHAR2(10) DEFAULT 'NEW' CONSTRAINT chk_status CHECK (status IN ('NEW', 'DONE')),
    dept_id   NUMBER REFERENCES departments (department_id) [ON DELETE {CASCADE | SET NULL}],
    total     NUMBER GENERATED ALWAYS AS (qty * price) VIRTUAL,
    created   DATE DEFAULT SYSDATE,
    CONSTRAINT uq_name UNIQUE (name)
)
[TABLESPACE ts] [PARTITION BY RANGE (created) INTERVAL (NUMTOYMINTERVAL(1, 'MONTH')) (PARTITION p0 VALUES LESS THAN (DATE '2024-01-01'))]
[ON COMMIT {DELETE | PRESERVE} ROWS];   -- for global temporary tables
CREATE TABLE copy_t AS SELECT * FROM t WHERE 1 = 0;     -- structure only
```

**Check**

- Before 12c there is no IDENTITY: use a SEQUENCE with a trigger or `DEFAULT seq.NEXTVAL` (12c+).
- Oracle has no `ON UPDATE CASCADE` and no `ON DELETE RESTRICT` (the default is NO ACTION).

### ALTER TABLE, sequences, synonyms, indexes, views (Oracle DDL)

**Syntax**

```sql
ALTER TABLE t ADD (col VARCHAR2(50) [DEFAULT x] [NOT NULL]);
ALTER TABLE t MODIFY (col VARCHAR2(100) [NOT NULL | NULL]);         -- not ALTER COLUMN
ALTER TABLE t DROP (col1, col2);   ALTER TABLE t DROP COLUMN col;    ALTER TABLE t RENAME COLUMN a TO b;   ALTER TABLE t RENAME TO t2;
ALTER TABLE t ADD CONSTRAINT pk_t PRIMARY KEY (id);   ALTER TABLE t {ENABLE | DISABLE} CONSTRAINT c;   ALTER TABLE t DROP CONSTRAINT c;
CREATE SEQUENCE seq [START WITH 1] [INCREMENT BY 1] [MINVALUE n] [MAXVALUE n | NOMAXVALUE] [CACHE 20 | NOCACHE] [CYCLE | NOCYCLE];
seq.NEXTVAL   seq.CURRVAL (only after NEXTVAL in the session)
CREATE [UNIQUE | BITMAP] INDEX idx ON t (col1 [, col2 DESC]) [TABLESPACE ts] [ONLINE];   CREATE INDEX ix ON t (UPPER(name));   -- function-based
CREATE [OR REPLACE] [FORCE] VIEW v AS SELECT ... [WITH READ ONLY | WITH CHECK OPTION];
CREATE MATERIALIZED VIEW mv [BUILD IMMEDIATE] [REFRESH {FAST | COMPLETE | FORCE} ON {COMMIT | DEMAND}] AS SELECT ...;
CREATE [OR REPLACE] [PUBLIC] SYNONYM s FOR schema.object;
DROP TABLE t [CASCADE CONSTRAINTS] [PURGE];   FLASHBACK TABLE t TO BEFORE DROP;   TRUNCATE TABLE t;
COMMENT ON TABLE t IS 'text';   COMMENT ON COLUMN t.c IS 'text';
```

**Check**

- Oracle changes a column with `MODIFY`, not `ALTER COLUMN`. A dropped table goes to the recycle bin unless `PURGE` is given.

### Data dictionary views (Oracle catalog)

**Syntax**

```sql
USER_TABLES  ALL_TABLES  DBA_TABLES          USER_TAB_COLUMNS (table_name, column_name, data_type, data_length, nullable)
USER_CONSTRAINTS (constraint_type: P, U, R, C)  USER_CONS_COLUMNS  USER_INDEXES  USER_IND_COLUMNS  USER_VIEWS  USER_SEQUENCES
USER_OBJECTS (object_type, status)  USER_SOURCE (PL/SQL text)  USER_ERRORS  USER_TRIGGERS  USER_SYNONYMS  USER_TAB_PRIVS
V$SESSION  V$SQL  V$VERSION  NLS_SESSION_PARAMETERS
SELECT column_name, data_type FROM user_tab_columns WHERE table_name = 'EMPLOYEES' ORDER BY column_id;   -- names in UPPER CASE
```

## PL/SQL

### Anonymous block (PL/SQL structure)

**Syntax**

```sql
[DECLARE
    <variable> <type> [NOT NULL] [:= <value>];
    <constant> CONSTANT <type> := <value>;
    <cursor, type, exception declarations>]
BEGIN
    <statements>;
[EXCEPTION
    WHEN <exception> [OR <exception>] THEN <statements>;
    WHEN OTHERS THEN <statements>;]
END;
/
```

**Example**

```sql
SET SERVEROUTPUT ON
DECLARE
    v_count  PLS_INTEGER;
    v_name   employees.last_name%TYPE;
BEGIN
    SELECT COUNT(*) INTO v_count FROM employees WHERE department_id = 10;
    SELECT last_name INTO v_name FROM employees WHERE employee_id = 100;
    DBMS_OUTPUT.PUT_LINE('Count: ' || v_count || ', name: ' || v_name);
EXCEPTION
    WHEN NO_DATA_FOUND THEN DBMS_OUTPUT.PUT_LINE('Not found');
END;
/
```

**Check**

- Assignment is `:=`, while `=` is comparison. A `SELECT` in PL/SQL needs `INTO`, and must return exactly one row (otherwise NO_DATA_FOUND or TOO_MANY_ROWS).
- `DBMS_OUTPUT` shows nothing unless `SET SERVEROUTPUT ON` (or the tool's output panel) is enabled.
- In SQL*Plus and SQLcl scripts, the `/` on its own line runs the block.

### Variables, types, %TYPE, %ROWTYPE, records, collections (PL/SQL declarations)

**Syntax**

```sql
v_id      NUMBER(10) := 0;           v_flag BOOLEAN := FALSE;        v_text VARCHAR2(200);   -- PL/SQL VARCHAR2 up to 32767
v_sal     employees.salary%TYPE;     -- same type as the column
r_emp     employees%ROWTYPE;         -- whole row: r_emp.last_name
TYPE t_rec IS RECORD (id NUMBER, name VARCHAR2(50));   r t_rec;
TYPE t_num_tab IS TABLE OF NUMBER;                      -- nested table (initialize: t_num_tab())
TYPE t_map IS TABLE OF VARCHAR2(100) INDEX BY VARCHAR2(30);   -- associative array: m('key') := 'v'
TYPE t_arr IS VARRAY(10) OF VARCHAR2(20);
SUBTYPE t_money IS NUMBER(12, 2);
coll.COUNT  coll.FIRST  coll.LAST  coll.NEXT(i)  coll.PRIOR(i)  coll.EXISTS(i)  coll.EXTEND[(n)]  coll.DELETE[(i)]  coll.TRIM
```

**Check**

- A nested table or varray must be initialized (`:= t_num_tab()`) and `EXTEND`ed before you assign to an index (ORA-06531, ORA-06533). An associative array needs neither.

### IF, CASE, loops (PL/SQL control statements)

**Syntax**

```sql
IF <cond> THEN <stmts>; [ELSIF <cond> THEN <stmts>;] [ELSE <stmts>;] END IF;      -- ELSIF, not ELSEIF
CASE <expr> WHEN <v> THEN <stmts>; [...] [ELSE <stmts>;] END CASE;
CASE WHEN <cond> THEN <stmts>; [...] END CASE;
LOOP <stmts>; EXIT WHEN <cond>; END LOOP;
WHILE <cond> LOOP <stmts>; END LOOP;
FOR i IN [REVERSE] 1 .. 10 LOOP <stmts>; END LOOP;       -- i is implicit, read-only
FOR r IN (SELECT ... FROM ...) LOOP <use r.col>; END LOOP;   -- implicit cursor loop
<<outer>> LOOP ... EXIT outer WHEN ...; CONTINUE [WHEN cond]; END LOOP outer;
NULL;     -- empty statement
GOTO label;   <<label>>
```

**Check**

- `ELSE IF` opens a new IF that needs its own `END IF`. Use `ELSIF`.
- A `CASE` statement without ELSE raises CASE_NOT_FOUND when nothing matches.

### Cursors (PL/SQL)

**Syntax**

```sql
CURSOR c_emp (p_dept NUMBER) IS SELECT employee_id, salary FROM employees WHERE department_id = p_dept [FOR UPDATE];
OPEN c_emp(10);  FETCH c_emp INTO v_id, v_sal;  EXIT WHEN c_emp%NOTFOUND;  CLOSE c_emp;
c%FOUND  c%NOTFOUND  c%ROWCOUNT  c%ISOPEN       SQL%ROWCOUNT  SQL%FOUND  (after DML)
UPDATE employees SET ... WHERE CURRENT OF c_emp;
v_rc SYS_REFCURSOR;  OPEN v_rc FOR SELECT ...;  OPEN v_rc FOR 'SELECT ... WHERE x = :1' USING v_x;
```

**Check**

- Test `%NOTFOUND` right after `FETCH`. Testing it before the first fetch returns NULL.

### BULK COLLECT and FORALL (PL/SQL bulk binding)

**Syntax**

```sql
SELECT col BULK COLLECT INTO v_tab FROM t WHERE ...;
FETCH c BULK COLLECT INTO v_tab LIMIT 1000;
FORALL i IN 1 .. v_ids.COUNT [SAVE EXCEPTIONS]
    UPDATE t SET flag = 'Y' WHERE id = v_ids(i);
SQL%BULK_ROWCOUNT(i)   SQL%BULK_EXCEPTIONS
```

**Example**

```sql
DECLARE
    TYPE t_ids IS TABLE OF orders.order_id%TYPE;
    v_ids t_ids;
    CURSOR c IS SELECT order_id FROM orders WHERE status = 'OLD';
BEGIN
    OPEN c;
    LOOP
        FETCH c BULK COLLECT INTO v_ids LIMIT 1000;
        EXIT WHEN v_ids.COUNT = 0;
        FORALL i IN 1 .. v_ids.COUNT
            UPDATE orders SET status = 'ARCHIVED' WHERE order_id = v_ids(i);
        COMMIT;
    END LOOP;
    CLOSE c;
END;
/
```

**Check**

- With `LIMIT`, exit on `v_ids.COUNT = 0`, not on `c%NOTFOUND` before processing, or the last partial batch is lost.

### Procedures and functions (PL/SQL subprograms)

**Syntax**

```sql
CREATE [OR REPLACE] PROCEDURE name (
    p_in     IN  NUMBER,
    p_out    OUT VARCHAR2,
    p_inout  IN OUT NOCOPY t_tab,
    p_opt    IN  VARCHAR2 DEFAULT 'X'
) [AUTHID {DEFINER | CURRENT_USER}] {IS | AS}
    <declarations>
BEGIN
    <statements>;
[EXCEPTION ...]
END [name];
/
CREATE [OR REPLACE] FUNCTION name (p IN NUMBER) RETURN VARCHAR2 [DETERMINISTIC] [PARALLEL_ENABLE] [RESULT_CACHE] {IS | AS}
BEGIN
    RETURN <value>;
END;
/
EXEC name(1, :out_var);   BEGIN name(p_in => 1, p_out => v); END;   SELECT fn(col) FROM t;
SHOW ERRORS   -- in SQL*Plus after a compile error; or SELECT * FROM user_errors;
```

**Check**

- Parameters have no length: `p VARCHAR2`, not `VARCHAR2(10)`.
- A function called from SQL cannot change the database (no DML) unless it is an autonomous transaction.
- Every code path of a function must `RETURN` a value (ORA-06503).

### Packages (PL/SQL)

**Syntax**

```sql
CREATE OR REPLACE PACKAGE pkg AS
    c_max CONSTANT NUMBER := 100;
    PROCEDURE do_it (p_id IN NUMBER);
    FUNCTION get_name (p_id IN NUMBER) RETURN VARCHAR2;
END pkg;
/
CREATE OR REPLACE PACKAGE BODY pkg AS
    g_counter NUMBER := 0;                    -- private package state
    PROCEDURE do_it (p_id IN NUMBER) IS BEGIN g_counter := g_counter + 1; END do_it;
    FUNCTION get_name (p_id IN NUMBER) RETURN VARCHAR2 IS v VARCHAR2(100);
    BEGIN SELECT last_name INTO v FROM employees WHERE employee_id = p_id; RETURN v; END get_name;
END pkg;
/
-- call: pkg.do_it(1);  pkg.get_name(1)
```

**Check**

- The body's signatures must match the specification exactly, including parameter names and modes.

### Exceptions (PL/SQL error handling)

**Syntax**

```sql
EXCEPTION
    WHEN NO_DATA_FOUND THEN ...
    WHEN TOO_MANY_ROWS THEN ...
    WHEN DUP_VAL_ON_INDEX THEN ...
    WHEN ZERO_DIVIDE | VALUE_ERROR | INVALID_NUMBER | CURSOR_ALREADY_OPEN | INVALID_CURSOR | COLLECTION_IS_NULL | SUBSCRIPT_BEYOND_COUNT | CASE_NOT_FOUND THEN ...
    WHEN e_custom THEN ...
    WHEN OTHERS THEN
        DBMS_OUTPUT.PUT_LINE(SQLCODE || ': ' || SQLERRM);
        DBMS_OUTPUT.PUT_LINE(DBMS_UTILITY.FORMAT_ERROR_BACKTRACE);
        RAISE;                                         -- re-raise
-- declare / raise:
e_custom EXCEPTION;   PRAGMA EXCEPTION_INIT(e_custom, -2292);   RAISE e_custom;
RAISE_APPLICATION_ERROR(-20001, 'Message' [, TRUE]);   -- code range -20000 .. -20999
```

**Check**

- `WHEN OTHERS THEN NULL;` hides every error. Log the error and `RAISE`.
- User error codes must be between -20000 and -20999.

### Dynamic SQL: EXECUTE IMMEDIATE, DBMS_SQL (PL/SQL)

**Syntax**

```sql
EXECUTE IMMEDIATE '<sql or plsql text>' [INTO v1, v2 | BULK COLLECT INTO tab] [USING [IN | OUT | IN OUT] b1, b2] [RETURNING INTO r];
EXECUTE IMMEDIATE 'DELETE FROM ' || DBMS_ASSERT.SQL_OBJECT_NAME(p_table) || ' WHERE id = :1' USING p_id;
EXECUTE IMMEDIATE 'CREATE TABLE tmp_x (id NUMBER)';
```

**Check**

- Pass values as binds (`:1`, `USING`), never by concatenation. Table and column names cannot be binds, so validate them with `DBMS_ASSERT`.
- The SQL text must not end with `;`, but PL/SQL text (`'BEGIN … END;'`) must.

### Triggers (PL/SQL)

**Syntax**

```sql
CREATE [OR REPLACE] TRIGGER trg_name
{BEFORE | AFTER | INSTEAD OF} {INSERT | UPDATE [OF col] | DELETE} [OR ...] ON table
[REFERENCING NEW AS n OLD AS o]
[FOR EACH ROW] [WHEN (new.amount > 0)]
[FOLLOWS other_trg] [ENABLE | DISABLE]
BEGIN
    IF INSERTING THEN :NEW.created := SYSDATE; END IF;
    IF UPDATING('STATUS') THEN :NEW.changed := SYSDATE; END IF;
    IF DELETING THEN ... END IF;
END;
/
```

**Check**

- Inside the body, use `:NEW` and `:OLD` with a colon. In the WHEN clause, write `new` and `old` without the colon.
- A row trigger that queries its own table raises ORA-04091 (mutating table). Use a compound trigger or another design.

### Useful built-in packages (Oracle PL/SQL)

**Syntax**

```sql
DBMS_OUTPUT.PUT_LINE(text)    DBMS_OUTPUT.PUT(text)    DBMS_OUTPUT.ENABLE(NULL)
DBMS_LOCK.SLEEP(s) / DBMS_SESSION.SLEEP(s) (18c+)
DBMS_UTILITY.FORMAT_ERROR_STACK  FORMAT_ERROR_BACKTRACE  GET_TIME
DBMS_SCHEDULER.CREATE_JOB(job_name => 'J1', job_type => 'PLSQL_BLOCK', job_action => 'BEGIN p; END;', repeat_interval => 'FREQ=DAILY;BYHOUR=2', enabled => TRUE)
DBMS_STATS.GATHER_TABLE_STATS(USER, 'T')   DBMS_METADATA.GET_DDL('TABLE', 'T')   DBMS_LOB.SUBSTR(clob, 4000, 1)  DBMS_LOB.GETLENGTH
DBMS_RANDOM.VALUE  DBMS_CRYPTO.HASH(UTL_RAW.CAST_TO_RAW(s), DBMS_CRYPTO.HASH_SH256)  UTL_FILE.FOPEN/PUT_LINE/FCLOSE  APEX_JSON / JSON_OBJECT_T
```

### Transactions and session settings (Oracle SQL)

**Syntax**

```sql
COMMIT;  ROLLBACK;  SAVEPOINT sp1;  ROLLBACK TO sp1;  SET TRANSACTION READ ONLY;
LOCK TABLE t IN EXCLUSIVE MODE [NOWAIT];
ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD HH24:MI:SS';
ALTER SESSION SET CURRENT_SCHEMA = hr;   ALTER SESSION SET TIME_ZONE = 'UTC';
PRAGMA AUTONOMOUS_TRANSACTION;   -- in a subprogram: commits independently (logging)
```
