---
name: oracle-sql
description: Writes and fixes Oracle SQL and PL/SQL in Oracle's own dialect (VARCHAR2, NVL, DUAL, ROWNUM or FETCH FIRST, sequences, packages). Use when the request mentions Oracle, PL/SQL, an Oracle database, or SQL for Oracle, including queries, procedures, functions, triggers and packages.
---

# Oracle SQL and PL/SQL

Write Oracle's dialect, never generic or SQL Server or MySQL syntax.

## Rules

- Types: `VARCHAR2(n)` (not VARCHAR), `NUMBER(p,s)`, `DATE` (holds a time too), `TIMESTAMP`, `CLOB`. No `BOOLEAN` columns before 23ai: use `NUMBER(1)` or `CHAR(1)`.
- Empty string is NULL in Oracle: `'' IS NULL` is true. Never compare with `= ''`.
- Null handling: `NVL(a, b)`, `NVL2`, `COALESCE`. String join: `||`, not `+` or `CONCAT` with many arguments.
- Row limits: `FETCH FIRST n ROWS ONLY` (12c+). `ROWNUM` only for older versions, and apply it outside an ordered subquery.
- No `SELECT` without `FROM`: use `FROM DUAL`.
- Dates: `SYSDATE`, `TRUNC(d)`, `ADD_MONTHS`, `TO_DATE('2024-01-31', 'YYYY-MM-DD')`. Always give an explicit format mask.
- Identity: `GENERATED ALWAYS AS IDENTITY` (12c+) or a sequence `seq.NEXTVAL`.
- Upsert: `MERGE INTO ... USING ... ON (...) WHEN MATCHED THEN UPDATE ... WHEN NOT MATCHED THEN INSERT ...`.
- Bind variables (`:name`), never string-built SQL in PL/SQL; use `EXECUTE IMMEDIATE ... USING` when dynamic SQL is unavoidable.

## PL/SQL

- Blocks: `DECLARE ... BEGIN ... EXCEPTION ... END;` then `/` on its own line in SQL*Plus or SQLcl scripts.
- Anchor types: `v_name employees.last_name%TYPE;`, rows with `%ROWTYPE`.
- `SELECT ... INTO` raises `NO_DATA_FOUND` or `TOO_MANY_ROWS`: handle them where the logic needs it.
- Bulk work: `BULK COLLECT INTO` with `LIMIT`, and `FORALL` for DML, instead of row-by-row loops.
- Never `WHEN OTHERS THEN NULL;`. Log or re-raise with `RAISE;`.
- Output for testing: `DBMS_OUTPUT.PUT_LINE`, and tell the user to run `SET SERVEROUTPUT ON` first.

## Example

Request: top 5 customers by total order value this year.

```sql
SELECT c.customer_id, c.name, SUM(o.amount) AS total
FROM customers c
JOIN orders o ON o.customer_id = c.customer_id
WHERE o.order_date >= TRUNC(SYSDATE, 'YYYY')
GROUP BY c.customer_id, c.name
ORDER BY total DESC
FETCH FIRST 5 ROWS ONLY;
```

## Checklist

- [ ] Only Oracle syntax (no `TOP`, `LIMIT`, `ISNULL`, `GETDATE`, `AUTO_INCREMENT`).
- [ ] Every date literal has a format mask.
- [ ] No comparison with `''`.
- [ ] PL/SQL blocks end with `END;` and `/` in scripts.
