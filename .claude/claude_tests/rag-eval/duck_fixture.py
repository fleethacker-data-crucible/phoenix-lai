"""DuckDB test fixture for the RAG evaluation: a folder with sample CSV / JSON / Parquet files and a database with the
tables the DuckDB and SQL prompts name, so answers are executed for real (EXPLAIN for queries, statements run in a
transaction that is rolled back). Dev-only.
  run(sql) -> list of problems (empty = the statements bind and run).
"""
import os, re, tempfile

_dir = None


def folder():
    global _dir
    if _dir: return _dir
    import duckdb
    _dir = tempfile.mkdtemp(prefix='phx-duck-')
    os.makedirs(os.path.join(_dir, 'logs'))
    w = lambda name, text: open(os.path.join(_dir, name), 'w', encoding='utf-8').write(text)
    w('sales.csv', 'sale_date,region,product,category,amount,customer_id\n2024-01-05,North,Widget,Tools,120.5,1\n2024-02-11,South,Gadget,Toys,80,2\n2024-03-02,North,Gadget,Toys,45.25,1\n')
    w('events_2024_01.csv', 'event_id,user_id,event_type,ts\n1,10,login,2024-01-01 10:00:00\n2,11,click,2024-01-02 11:00:00\n')
    w('events_2024_02.csv', 'event_id,user_id,event_type,ts\n3,10,logout,2024-02-01 09:00:00\n')
    w('events.csv', 'event_id,user_id,event_type,ts\n1,10,login,2024-01-01 10:00:00\n2,11,click,2024-01-02 11:00:00\n')
    w(os.path.join('logs', 'app1.csv'), 'ts,level,message\n2024-01-01 10:00:00,ERROR,disk full\n')
    w(os.path.join('logs', 'app2.csv'), 'ts,level,message,host\n2024-01-02 10:00:00,INFO,ok,web1\n')
    w('events.json', '{"j": {"user": {"name": "ann"}}, "tags": ["a", "b"]}\n{"j": {"user": {"name": "bob"}}, "tags": ["c"]}\n')
    w('users.json', '[{"id": 1, "name": "Ann", "address": {"city": "Oslo"}, "tags": ["x", "y"]}]')
    con = duckdb.connect(os.path.join(_dir, 'test.db'))
    con.execute("""
        CREATE TABLE customers (customer_id INTEGER PRIMARY KEY, name VARCHAR, email VARCHAR, city VARCHAR, signup_date DATE);
        CREATE TABLE orders (order_id INTEGER PRIMARY KEY, customer_id INTEGER, order_date DATE, amount DECIMAL(12,2), status VARCHAR,
                             discount DECIMAL(12,2), internal_note VARCHAR, raw_payload VARCHAR, product_id INTEGER, quantity INTEGER);
        CREATE TABLE products (product_id INTEGER PRIMARY KEY, name VARCHAR, price DECIMAL(10,2), category VARCHAR);
        CREATE TABLE staging_products (product_id INTEGER, name VARCHAR, price DECIMAL(10,2), category VARCHAR);
        CREATE TABLE sales (sale_id INTEGER, sale_date DATE, region VARCHAR, product VARCHAR, category VARCHAR, amount DECIMAL(12,2), customer_id INTEGER);
        CREATE TABLE employees (employee_id INTEGER, name VARCHAR, last_name VARCHAR, manager_id INTEGER, department_id INTEGER, salary DECIMAL(12,2), hire_date DATE);
        CREATE TABLE users (user_id INTEGER, email VARCHAR, name VARCHAR, created_at TIMESTAMP, last_login TIMESTAMP);
        CREATE TABLE raw_products (price_text VARCHAR, city VARCHAR, name VARCHAR);
        CREATE TABLE events (event_id INTEGER, user_id INTEGER, event_type VARCHAR, ts TIMESTAMP);
        CREATE TABLE sessions (session_id INTEGER, user_id INTEGER, started_at TIMESTAMP, ended_at TIMESTAMP);
        INSERT INTO customers VALUES (1, 'Ann', 'ann@x.com', 'Oslo', DATE '2023-05-01'), (2, 'Bob', 'bob@x.com', 'Rome', DATE '2024-01-10');
        INSERT INTO orders VALUES (1, 1, DATE '2024-01-05', 120.50, 'shipped', 0, NULL, NULL, 1, 2), (2, 2, DATE '2024-02-11', 80, 'new', 5, NULL, NULL, 2, 1);
        INSERT INTO products VALUES (1, 'Widget', 10, 'Tools'), (2, 'Gadget', 20, 'Toys');
    """)
    con.execute("COPY orders TO '" + os.path.join(_dir, 'orders.parquet').replace('\\', '/') + "' (FORMAT parquet)")
    con.close()
    return _dir


def run(sql, dialect='duckdb'):
    """Problems running the SQL in a scratch copy of the test database (nothing persists)."""
    import duckdb, shutil, uuid
    d = folder()
    db = os.path.join(d, 'run-' + uuid.uuid4().hex[:8] + '.db')
    shutil.copy(os.path.join(d, 'test.db'), db)
    cwd = os.getcwd(); os.chdir(d)
    probs = []
    try:
        con = duckdb.connect(db)
        stmts = [s for s in split(sql) if s.strip()]
        for s in stmts:
            try: con.execute(s)
            except Exception as e:
                msg = str(e).split('\n')[0]
                # CREATE of a name the fixture already has: the answer is fine, replace the fixture's object
                m = re.match(r'\s*CREATE\s+(TABLE|SEQUENCE|VIEW)\s+(?:IF\s+NOT\s+EXISTS\s+)?([\w."]+)', s, re.I)
                if m and 'already exists' in msg:
                    try:
                        con.execute('DROP ' + m.group(1) + ' IF EXISTS ' + m.group(2) + ' CASCADE'); con.execute(s); continue
                    except Exception as e2:
                        msg = str(e2).split('\n')[0]
                probs.append(msg[:200])
        con.close()
    finally:
        os.chdir(cwd)
        try: os.remove(db)
        except OSError: pass
    return probs


def split(sql):
    """Statements separated by ; outside quotes and comments."""
    out, cur, q, i = [], [], None, 0
    while i < len(sql):
        c = sql[i]
        if q:
            cur.append(c)
            if c == q: q = None
        elif c in "'\"": q = c; cur.append(c)
        elif sql.startswith('--', i):
            j = sql.find('\n', i); j = len(sql) if j < 0 else j; i = j; continue
        elif c == ';': out.append(''.join(cur)); cur = []
        else: cur.append(c)
        i += 1
    out.append(''.join(cur))
    return out


if __name__ == '__main__':
    import sys
    text = open(sys.argv[1], encoding='utf-8').read()
    heading, bad, n = '', 0, 0
    for m in re.finditer(r'(?m)^### (.*)$|^```sql\n([\s\S]*?)^```', text):
        if m.group(1): heading = m.group(1); continue
        n += 1
        p = run(m.group(2))
        if p: bad += 1; print('FAIL', heading, '|', ' ; '.join(p))
    print(n, 'blocks,', bad, 'failed')
