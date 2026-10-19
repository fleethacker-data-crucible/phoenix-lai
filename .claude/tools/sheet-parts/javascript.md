## Essentials

Target: modern JavaScript (ES2022) in Node.js 18+ and current browsers. In Node, a `.js` file is CommonJS unless package.json has `"type": "module"`: use `require` there, or name the file `.mjs` for `import`.

| Never | Write instead |
|---|---|
| `var x` | `const x` (or `let` when it changes) |
| `a == b` | `a === b` (`x == null` only to test null or undefined) |
| `arr.length()`, `arr.contains(x)`, `str.contains(x)` | `arr.length`, `arr.includes(x)`, `str.includes(x)` |
| `items.forEach(async x => await f(x))` | `for (const x of items) { await f(x); }` or `await Promise.all(items.map(f))` |
| `const data = fetch(url).json()` | `const res = await fetch(url); if (!res.ok) throw ...; const data = await res.json();` |
| `'Hi ${name}'` | `` `Hi ${name}` `` (backticks) |
| `import` in a CommonJS file / `require` in an ES module | match the module type of the file |
| `fs.readFile(path)` result used directly | `await fs.promises.readFile(path, 'utf8')` or `fs.readFileSync(path, 'utf8')` |
| `el.innerHTML = userText` | `el.textContent = userText` |
| `parseInt(s)` | `parseInt(s, 10)` or `Number(s)` |

Always: top-level `await` only in ES modules; elsewhere wrap in `async function main() { ... } main().catch(console.error);`. Check `res.ok` after `fetch`. Close what you open (streams, intervals, listeners).

## Recipes

### Read a file, process lines, write the result (fs.promises, Node)

**Example**

```javascript
const fs = require('fs');

async function main() {
  const text = await fs.promises.readFile('input.txt', 'utf8');
  const lines = text.split(/\r?\n/).filter(line => line.trim() !== '');
  const errors = lines.filter(line => line.includes('ERROR'));
  await fs.promises.writeFile('errors.txt', errors.join('\n'), 'utf8');
  console.log(`${errors.length} of ${lines.length} lines contain ERROR`);
}

main().catch(err => { console.error(err); process.exitCode = 1; });
```

### Read a huge file line by line (readline, createReadStream)

**Example**

```javascript
const fs = require('fs');
const readline = require('readline');

async function countMatches(path, word) {
  const rl = readline.createInterface({ input: fs.createReadStream(path), crlfDelay: Infinity });
  let count = 0;
  for await (const line of rl) {
    if (line.includes(word)) count++;
  }
  return count;
}

countMatches('big.log', 'ERROR').then(n => console.log(n));
```

### Fetch JSON from an API with errors handled (fetch, async/await)

**Example**

```javascript
async function getUser(id) {
  const res = await fetch(`https://api.example.com/users/${encodeURIComponent(id)}`);
  if (!res.ok) throw new Error(`HTTP ${res.status} for user ${id}`);
  return res.json();
}

getUser(7).then(u => console.log(u.name)).catch(err => console.error(err.message));
```

### Run async tasks in parallel or with a limit (Promise.all, Promise.allSettled)

**Example**

```javascript
async function processAll(ids, worker, limit = 4) {
  const results = [];
  let next = 0;
  async function run() {
    while (next < ids.length) {
      const i = next++;
      results[i] = await worker(ids[i]);
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, ids.length) }, run));
  return results;
}

const settled = Promise.allSettled([Promise.resolve(1), Promise.reject(new Error('x'))]);
settled.then(r => console.log(r.map(x => x.status)));
```

### Group, count and sort an array of objects (reduce, Map, sort)

**Example**

```javascript
const orders = [
  { customer: 'Ann', amount: 10.5 },
  { customer: 'Bob', amount: 3 },
  { customer: 'Ann', amount: 7 },
];
const totals = new Map();
for (const o of orders) totals.set(o.customer, (totals.get(o.customer) ?? 0) + o.amount);
const ranked = [...totals.entries()]
  .map(([customer, total]) => ({ customer, total }))
  .sort((a, b) => b.total - a.total);
console.log(ranked);
```

- `sort()` without a compare function sorts as strings (`[10, 9, 1].sort()` gives `[1, 10, 9]`).

### Debounce input and update the page safely (DOM, addEventListener, textContent)

**Example**

```javascript
function debounce(fn, ms) {
  let timer;
  return (...args) => {
    clearTimeout(timer);
    timer = setTimeout(() => fn(...args), ms);
  };
}

document.addEventListener('DOMContentLoaded', () => {
  const input = document.querySelector('#search');
  const out = document.querySelector('#result');
  if (!input || !out) return;
  input.addEventListener('input', debounce(e => {
    out.textContent = `You typed: ${e.target.value}`;
  }, 300));
});
```

### Parse and validate dates and numbers (Date, Number, Intl)

**Example**

```javascript
function parseIsoDate(s) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(s)) return null;
  const d = new Date(`${s}T00:00:00Z`);
  return Number.isNaN(d.getTime()) ? null : d;
}

const price = Number('19.99');
if (!Number.isFinite(price)) throw new Error('bad price');
console.log(new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(price));
console.log(parseIsoDate('2024-02-30'));
```

### A small class with private fields and a static factory (class, #private, static)

**Example**

```javascript
class Counter {
  #count = 0;
  static from(n) {
    const c = new Counter();
    for (let i = 0; i < n; i++) c.increment();
    return c;
  }
  increment() { this.#count++; return this; }
  get value() { return this.#count; }
}

console.log(Counter.from(3).increment().value);
```

### A minimal HTTP server (Node http module)

**Example**

```javascript
const http = require('http');

const server = http.createServer((req, res) => {
  if (req.method === 'GET' && req.url === '/health') {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ ok: true }));
    return;
  }
  res.writeHead(404, { 'Content-Type': 'text/plain' });
  res.end('Not found');
});

server.listen(3000, '127.0.0.1', () => console.log('listening on 3000'));
```

### Deep copy, merge and compare objects (structuredClone, spread, JSON)

**Example**

```javascript
const defaults = { retries: 3, log: { level: 'info' } };
const user = { log: { level: 'debug' } };
const merged = { ...defaults, ...user, log: { ...defaults.log, ...user.log } };
const copy = structuredClone(merged);
copy.log.level = 'warn';
console.log(merged.log.level, copy.log.level, JSON.stringify(merged) === JSON.stringify(copy));
```
