## Essentials

Target: standard SQL (ISO SQL:2016) that PostgreSQL, SQL Server, Oracle, MySQL 8, SQLite and DuckDB all accept, unless the user names a database (then use that dialect: Oracle and DuckDB have their own sheets).

| Avoid (dialect-only) | Portable form |
|---|---|
| `SELECT TOP 10` / `LIMIT 10` | `ORDER BY ... FETCH FIRST 10 ROWS ONLY` (SQL Server: `OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY`) |
| `ISNULL(a, b)`, `IFNULL`, `NVL` | `COALESCE(a, b)` |
| `GETDATE()`, `NOW()`, `SYSDATE` | `CURRENT_DATE`, `CURRENT_TIMESTAMP` |
| `'a' + b`, `CONCAT` with many args | `'a' \|\| b` (SQL Server: `CONCAT(a, b)`) |
| `IF EXISTS ... UPDATE ELSE INSERT` | `MERGE INTO ... USING ... ON ... WHEN MATCHED ... WHEN NOT MATCHED ...` |
| `x = NULL`, `NOT IN (subquery with NULLs)` | `x IS NULL`, `NOT EXISTS (...)` |
| `"text"` for strings | `'text'` (double quotes are identifiers) |
| SELECT alias in WHERE | repeat the expression, or wrap in a subquery / CTE |

Always: every non-aggregated SELECT column is in GROUP BY. Filter groups with HAVING. A filter on the right table of a LEFT JOIN goes in ON. Guard division with `NULLIF(b, 0)` and cast integers before dividing. Parameters, never concatenated input. UPDATE / DELETE always have a WHERE.

## Recipes

### Top rows by a total (GROUP BY, ORDER BY, FETCH FIRST)

**Example**

```sql
SELECT c.customer_id, c.name, SUM(o.amount) AS total
FROM customers c
JOIN orders o ON o.customer_id = c.customer_id
GROUP BY c.customer_id, c.name
ORDER BY total DESC
FETCH FIRST 5 ROWS ONLY;
```

### Latest row per group (ROW_NUMBER window)

**Example**

```sql
WITH ranked AS (
  SELECT o.*, ROW_NUMBER() OVER (PARTITION BY customer_id ORDER BY order_date DESC) AS rn
  FROM orders o
)
SELECT customer_id, order_id, order_date, amount
FROM ranked
WHERE rn = 1;
```

### Customers with no orders (LEFT JOIN ... IS NULL, NOT EXISTS)

**Example**

```sql
SELECT c.customer_id, c.name
FROM customers c
WHERE NOT EXISTS (SELECT 1 FROM orders o WHERE o.customer_id = c.customer_id);
```

### Running total and month-over-month change (SUM OVER, LAG)

**Example**

```sql
WITH monthly AS (
  SELECT EXTRACT(YEAR FROM order_date) AS y, EXTRACT(MONTH FROM order_date) AS m, SUM(amount) AS revenue
  FROM orders
  GROUP BY EXTRACT(YEAR FROM order_date), EXTRACT(MONTH FROM order_date)
)
SELECT y, m, revenue,
       SUM(revenue) OVER (ORDER BY y, m) AS running_total,
       revenue - LAG(revenue) OVER (ORDER BY y, m) AS change
FROM monthly
ORDER BY y, m;
```

### Percentage of total and safe division (NULLIF, CAST)

**Example**

```sql
SELECT category,
       SUM(amount) AS revenue,
       CAST(100.0 * SUM(amount) / NULLIF(SUM(SUM(amount)) OVER (), 0) AS DECIMAL(5,2)) AS pct
FROM sales
GROUP BY category
ORDER BY revenue DESC;
```

### Conditional counts in one pass (SUM(CASE ...), COUNT(*) FILTER)

**Example**

```sql
SELECT customer_id,
       COUNT(*) AS orders,
       SUM(CASE WHEN status = 'shipped' THEN 1 ELSE 0 END) AS shipped,
       SUM(CASE WHEN status = 'cancelled' THEN 1 ELSE 0 END) AS cancelled
FROM orders
GROUP BY customer_id;
```

### Upsert (MERGE)

**Example**

```sql
MERGE INTO products AS p
USING staging_products AS s
ON p.product_id = s.product_id
WHEN MATCHED THEN UPDATE SET name = s.name, price = s.price
WHEN NOT MATCHED THEN INSERT (product_id, name, price) VALUES (s.product_id, s.name, s.price);
```

- MySQL has no MERGE: `INSERT ... ON DUPLICATE KEY UPDATE`. PostgreSQL 15+ and SQL Server have MERGE; older PostgreSQL / SQLite: `INSERT ... ON CONFLICT (id) DO UPDATE SET ...`.

### Remove duplicate rows but keep one (ROW_NUMBER in a CTE)

**Example**

```sql
DELETE FROM users
WHERE user_id IN (
  SELECT user_id FROM (
    SELECT user_id, ROW_NUMBER() OVER (PARTITION BY LOWER(email) ORDER BY user_id) AS rn
    FROM users
  ) d
  WHERE rn > 1
);
```

### Create tables with keys and constraints (CREATE TABLE, PRIMARY KEY, FOREIGN KEY, CHECK)

**Example**

```sql
CREATE TABLE customers (
  customer_id INTEGER PRIMARY KEY,
  name        VARCHAR(200) NOT NULL,
  email       VARCHAR(320) UNIQUE
);

CREATE TABLE orders (
  order_id    INTEGER PRIMARY KEY,
  customer_id INTEGER NOT NULL REFERENCES customers (customer_id),
  order_date  DATE NOT NULL,
  amount      DECIMAL(12,2) NOT NULL CHECK (amount >= 0),
  status      VARCHAR(20) DEFAULT 'new' NOT NULL
);
```

### Recursive hierarchy (WITH RECURSIVE)

**Example**

```sql
WITH RECURSIVE chain (employee_id, manager_id, depth) AS (
  SELECT employee_id, manager_id, 1 FROM employees WHERE manager_id = 100
  UNION ALL
  SELECT e.employee_id, e.manager_id, c.depth + 1
  FROM employees e JOIN chain c ON e.manager_id = c.employee_id
)
SELECT * FROM chain ORDER BY depth, employee_id;
```

- Oracle and SQL Server write `WITH` without `RECURSIVE`.
