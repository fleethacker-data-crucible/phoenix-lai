## Essentials

Target: Oracle Database 19c (SQL and PL/SQL) unless the user names 23ai. Code for 19c also runs on 21c and 23ai.

| Never in Oracle 19c | Write instead |
|---|---|
| `SELECT TOP 5 ...`, `LIMIT 5` | `ORDER BY ... FETCH FIRST 5 ROWS ONLY` |
| rows 21-40: `LIMIT 20 OFFSET 20` | `ORDER BY ... OFFSET 20 ROWS FETCH NEXT 20 ROWS ONLY` |
| `ISNULL(a, b)`, `IFNULL(a, b)` | `NVL(a, b)` or `COALESCE(a, b)` |
| `GETDATE()`, `NOW()` | `SYSDATE` (date + time), `SYSTIMESTAMP`, `TRUNC(SYSDATE)` (today) |
| `DATEADD(day, 7, d)`, `DATEDIFF(...)` | `d + 7`, `ADD_MONTHS(d, 1)`, `d2 - d1` (days), `MONTHS_BETWEEN(d2, d1)` |
| `'a' + 'b'`, `CONCAT(a, b, c)` | `'a' \|\| 'b' \|\| c` |
| `WHERE col = ''` | `WHERE col IS NULL` ('' is NULL in Oracle) |
| `AUTO_INCREMENT`, `IDENTITY(1,1)`, `SERIAL` | `id NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY` |
| `VARCHAR`, `VARCHAR(MAX)`, `TEXT`, `BOOLEAN` column | `VARCHAR2(n)`, `CLOB`, `NUMBER(1)` / `CHAR(1)` |
| `SELECT 1;`, `FROM t AS x` | `SELECT 1 FROM DUAL;`, `FROM t x` (no AS before table aliases) |
| `LEN(s)`, `CHARINDEX(x, s)`, `STRING_AGG` | `LENGTH(s)`, `INSTR(s, x)`, `LISTAGG(x, ', ') WITHIN GROUP (ORDER BY x)` |
| `DECLARE @v INT`, `PRINT` | PL/SQL `DECLARE v NUMBER; BEGIN ... END;` then `/`, `DBMS_OUTPUT.PUT_LINE` |
| `IF EXISTS (...) UPDATE ... ELSE INSERT` | `MERGE INTO t USING ... ON (...) WHEN MATCHED THEN UPDATE ... WHEN NOT MATCHED THEN INSERT ...` |

Always: date literals with an explicit format (`DATE '2024-01-31'` or `TO_DATE('31/01/2024', 'DD/MM/YYYY')`). Bind variables (`:name`), never concatenated input. A PL/SQL block ends with `END;` and a `/` line in scripts. Every `SELECT ... INTO` handles `NO_DATA_FOUND` where a row may be missing.

## Recipes

### Top N rows per ranking, and top N per group (FETCH FIRST, ROW_NUMBER)

**Example**

```sql
SELECT c.customer_id, c.name, SUM(o.amount) AS total
FROM customers c
JOIN orders o ON o.customer_id = c.customer_id
WHERE o.order_date >= TRUNC(SYSDATE, 'YYYY')
GROUP BY c.customer_id, c.name
ORDER BY total DESC
FETCH FIRST 5 ROWS ONLY;

SELECT region, product, revenue
FROM (SELECT region, product, revenue,
             ROW_NUMBER() OVER (PARTITION BY region ORDER BY revenue DESC) AS rn
      FROM sales_summary)
WHERE rn <= 3;
```

### Page through rows (OFFSET ... FETCH NEXT)

**Example**

```sql
SELECT order_id, order_date, amount
FROM orders
ORDER BY order_date, order_id
OFFSET 20 ROWS FETCH NEXT 20 ROWS ONLY;
```

### Upsert rows from a staging table (MERGE)

**Example**

```sql
MERGE INTO products p
USING staging_products s
ON (p.product_id = s.product_id)
WHEN MATCHED THEN
  UPDATE SET p.name = s.name, p.price = s.price
WHEN NOT MATCHED THEN
  INSERT (product_id, name, price) VALUES (s.product_id, s.name, s.price);
```

### Create a table with an identity key and defaults (GENERATED AS IDENTITY, DEFAULT SYSDATE)

**Example**

```sql
CREATE TABLE invoices (
  invoice_id    NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  customer_name VARCHAR2(200) NOT NULL,
  amount        NUMBER(12,2) NOT NULL,
  is_paid       NUMBER(1) DEFAULT 0 NOT NULL CHECK (is_paid IN (0, 1)),
  created_at    DATE DEFAULT SYSDATE NOT NULL
);
```

### Running totals and moving sums (SUM ... OVER)

**Example**

```sql
SELECT sale_date, amount,
       SUM(amount) OVER (ORDER BY sale_date ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total
FROM sales
ORDER BY sale_date;
```

### Find duplicates (GROUP BY ... HAVING COUNT(*) > 1)

**Example**

```sql
SELECT LOWER(email) AS email, COUNT(*) AS copies
FROM users
GROUP BY LOWER(email)
HAVING COUNT(*) > 1
ORDER BY copies DESC;
```

### Join values of a group into one string (LISTAGG)

**Example**

```sql
SELECT department_id,
       LISTAGG(last_name, ', ') WITHIN GROUP (ORDER BY last_name) AS staff
FROM employees
GROUP BY department_id;
```

### Pivot rows into columns (PIVOT, or SUM(CASE ...))

**Example**

```sql
SELECT *
FROM (SELECT region, TO_CHAR(sale_date, 'MON', 'NLS_DATE_LANGUAGE=ENGLISH') AS mon, amount FROM sales)
PIVOT (SUM(amount) FOR mon IN ('JAN' AS jan, 'FEB' AS feb, 'MAR' AS mar));
```

### Tree of employees under a manager (CONNECT BY, LEVEL)

**Example**

```sql
SELECT LEVEL AS depth, employee_id, LPAD(' ', 2 * (LEVEL - 1)) || last_name AS name
FROM employees
START WITH manager_id = 100
CONNECT BY PRIOR employee_id = manager_id
ORDER SIBLINGS BY last_name;
```

### Date ranges and date arithmetic (TRUNC, ADD_MONTHS, intervals)

**Example**

```sql
SELECT order_id, order_date
FROM orders
WHERE order_date >= TRUNC(SYSDATE) - 30
  AND order_date < TRUNC(SYSDATE) + 1;

SELECT TRUNC(SYSDATE, 'MM') AS month_start,
       LAST_DAY(SYSDATE) AS month_end,
       ADD_MONTHS(TRUNC(SYSDATE, 'MM'), -1) AS last_month_start,
       SYSTIMESTAMP + INTERVAL '2' HOUR AS in_two_hours
FROM DUAL;
```

### Stored procedure with parameters and error handling (CREATE PROCEDURE, EXCEPTION)

**Example**

```sql
CREATE OR REPLACE PROCEDURE raise_salary (
  p_department_id IN employees.department_id%TYPE,
  p_percent       IN NUMBER
) AS
BEGIN
  IF p_percent <= 0 OR p_percent > 50 THEN
    RAISE_APPLICATION_ERROR(-20001, 'Percent must be between 0 and 50');
  END IF;
  UPDATE employees
     SET salary = ROUND(salary * (1 + p_percent / 100), 2)
   WHERE department_id = p_department_id;
  IF SQL%ROWCOUNT = 0 THEN
    RAISE_APPLICATION_ERROR(-20002, 'No employees in department ' || p_department_id);
  END IF;
  COMMIT;
EXCEPTION
  WHEN OTHERS THEN
    ROLLBACK;
    RAISE;
END raise_salary;
/
```

### Bulk update with BULK COLLECT and FORALL (PL/SQL)

**Example**

```sql
DECLARE
  TYPE t_ids IS TABLE OF accounts.account_id%TYPE;
  v_ids t_ids;
  CURSOR c IS SELECT account_id FROM accounts WHERE last_login < ADD_MONTHS(SYSDATE, -12);
BEGIN
  OPEN c;
  LOOP
    FETCH c BULK COLLECT INTO v_ids LIMIT 1000;
    EXIT WHEN v_ids.COUNT = 0;
    FORALL i IN 1 .. v_ids.COUNT
      UPDATE accounts SET status = 'INACTIVE' WHERE account_id = v_ids(i);
  END LOOP;
  CLOSE c;
  COMMIT;
END;
/
```

### Read one value into a variable safely (SELECT INTO, NO_DATA_FOUND)

**Example**

```sql
DECLARE
  v_name customers.name%TYPE;
BEGIN
  SELECT name INTO v_name FROM customers WHERE customer_id = 42;
  DBMS_OUTPUT.PUT_LINE('Customer: ' || v_name);
EXCEPTION
  WHEN NO_DATA_FOUND THEN
    DBMS_OUTPUT.PUT_LINE('No customer 42');
END;
/
```

- Run `SET SERVEROUTPUT ON` first in SQL*Plus / SQLcl to see DBMS_OUTPUT.
