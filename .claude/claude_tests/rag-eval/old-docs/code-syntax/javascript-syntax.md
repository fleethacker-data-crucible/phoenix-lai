# JavaScript syntax reference (Phoenix)

For Phoenix models writing and checking JavaScript (ECMAScript 2024 and later, in browsers and Node.js). Each `###` entry is one statement, operator, built-in or API: what it does, its exact syntax, every argument, an example, and the mistakes to check for.

**Notation:** `[x]` optional · `a | b` one of · `...` repeatable · `→` returns · **ES20xx** = the version that added it.

## Checklist: before you answer with JavaScript

- Declare variables with `const` (default) or `let`. Never use `var`, and never assign an undeclared name (an implicit global, or a ReferenceError in strict mode and modules).
- Compare with `===` and `!==`. `==` converts types (`0 == ''` is true), except in the `x == null` idiom, which tests null or undefined.
- Every called method exists on that type: `array.length` (not `.length()`, not `.size`), `map.size`, `str.length`, `str.includes()`, `arr.includes()`. There is no `.contains()` on arrays or strings.
- Asynchronous calls (`fetch`, `response.json()`, any `async` function) are awaited with `await` inside an `async` function, or chained with `.then()`. Check `response.ok`, because fetch does not reject on HTTP 404 or 500.
- `forEach` cannot be `await`ed or stopped with `break`. Use `for...of`.
- `this` in a regular function depends on how it is called. Arrow functions take `this` from the enclosing scope. Do not use arrow functions as object methods that need `this`.
- Template literals use backticks: `` `Hi ${name}` ``. `'Hi ${name}'` with quotes does not interpolate.
- Objects and arrays are compared by reference: `[] === []` is false. Copy with spread or `structuredClone`, not `=`.
- Numbers are floats: `0.1 + 0.2 !== 0.3`. `parseInt` needs a radix: `parseInt(s, 10)`. `NaN !== NaN`, so test with `Number.isNaN`.
- DOM code runs after the element exists: use `defer`, `DOMContentLoaded`, or a script at the end of the body. `querySelector` returns `null` when nothing matches.
- To insert user text, use `textContent`, never `innerHTML` (XSS).
- `import`/`export` work only in modules (`<script type="module">`, `.mjs`, or `"type": "module"`). CommonJS uses `require` and `module.exports`.

## Basics

### Variables: let, const, var (JavaScript declarations)

**Syntax**

```js
const PI = 3.14159;            // block-scoped, cannot be reassigned (object contents can still change)
let count = 0;                 // block-scoped, reassignable
var old = 1;                   // function-scoped, hoisted (avoid)
let a = 1, b = 2;
const { name, age = 18, ...rest } = person;     // object destructuring with default and rest
const [first, , third = 0, ...others] = list;  // array destructuring (skip with a comma)
[a, b] = [b, a];                                // swap
```

**Check**

- `const` objects can be mutated (`obj.x = 1` works). Use `Object.freeze` to stop that.
- `let` and `const` are in the "temporal dead zone" before their declaration, so using them earlier throws ReferenceError.
- Redeclaring a `let` or `const` in the same scope is a SyntaxError.

### Types and typeof (JavaScript values)

**Primitives:** `string`, `number` (64-bit float), `bigint` (`10n`), `boolean`, `undefined`, `null`, `symbol`. Everything else is an `object`, including arrays, functions, dates and regexes.

**Syntax**

```js
typeof "a"        // "string"
typeof 1          // "number"       typeof 1n  // "bigint"
typeof true       // "boolean"      typeof undefined // "undefined"
typeof null       // "object" (historic bug)   typeof [] // "object"   typeof (() => 1) // "function"
Array.isArray(x)  x instanceof Date   x === null   x === undefined   x == null (null or undefined)
Number.isInteger(x)  Number.isFinite(x)  Number.isNaN(x)
```

**Falsy values:** `false`, `0`, `-0`, `0n`, `''`, `null`, `undefined`, `NaN`. Everything else is truthy, including `'0'`, `'false'`, `[]` and `{}`.

**Check**

- To test for an array, use `Array.isArray`, not `typeof`.
- `if (arr)` is true even for an empty array. Test `arr.length`.

### Strings and template literals (JavaScript values)

**Syntax**

```js
'single'  "double"  `template ${expr} spans
lines`
'It\'s'  "Tab\tNew\nline \\ \u00e9 \u{1F600}"
String.raw`C:\path\${no escape}`     // raw template: backslashes kept
tag`hello ${x}`                      // tagged template: tag(strings, ...values)
```

**String methods** (strings are immutable, so every method returns a new value):

```js
s.length  s[0]  s.at(-1) (ES2022)  s.charAt(i)  s.charCodeAt(i)  s.codePointAt(i)
s.indexOf(sub[, from])  s.lastIndexOf(sub)  s.includes(sub[, from])  s.startsWith(sub[, pos])  s.endsWith(sub[, len])  s.search(regex)
s.slice(start[, end])   // negative indexes count from the end
s.substring(start[, end])  s.toUpperCase()  s.toLowerCase()  s.toLocaleUpperCase(locale)
s.trim()  s.trimStart()  s.trimEnd()  s.padStart(len[, fill])  s.padEnd(len[, fill])  s.repeat(n)
s.split(separator[, limit])  s.concat(...strs)  s.replace(pattern, replacement | fn)  s.replaceAll(str | /g regex, repl) (ES2021)
s.match(regex)  s.matchAll(/g regex) (ES2020)  s.localeCompare(t[, locale, options])  s.normalize('NFC')  s.isWellFormed() (ES2024)
String(x)  x.toString()  String.fromCharCode(65)  String.fromCodePoint(128512)
```

**Check**

- `s.replace('a', 'b')` replaces only the FIRST match. Use `replaceAll`, or a `/a/g` regex.
- `.length` counts UTF-16 code units, so an emoji counts as 2. Use `[...s].length` to count code points.
- In a replacement string, `$&`, `$1` and `$<name>` are special. Use a function replacement for literal `$`.

### Numbers, BigInt and Math (JavaScript built-ins)

**Syntax**

```js
42  3.14  1e6  0xff  0o17  0b1010  1_000_000 (ES2021)  Infinity  NaN  10n (BigInt)
Number('42')  Number('')  // 0!     parseInt('42px', 10) // 42     parseFloat('3.5kg') // 3.5     +'7' // 7
n.toFixed(2) // string "3.14"   n.toPrecision(3)   n.toString(16)   n.toLocaleString('en-US', { style: 'currency', currency: 'USD' })
Number.MAX_SAFE_INTEGER (2**53-1)  Number.EPSILON  Number.parseFloat  Number.isSafeInteger(n)
Math.round(x)  Math.floor(x)  Math.ceil(x)  Math.trunc(x)  Math.abs(x)  Math.sign(x)  Math.max(...arr)  Math.min(a, b)
Math.pow(b, e)  b ** e  Math.sqrt(x)  Math.cbrt(x)  Math.hypot(a, b)  Math.random() // [0, 1)  Math.PI  Math.E
Math.log(x)  Math.log10(x)  Math.log2(x)  Math.exp(x)  Math.sin/cos/tan/atan2(y, x)  Math.clz32  Math.fround
BigInt(123)  10n ** 20n  BigInt.asIntN(64, x)
```

**Example**

```js
const randInt = (min, max) => Math.floor(Math.random() * (max - min + 1)) + min;   // inclusive
const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'EUR' }).format(1234.5);
```

**Check**

- `Math.round(-2.5)` is `-2`. `toFixed` returns a string. `Math.max()` of an empty array is `-Infinity`.
- `parseInt('08')` works in modern engines, but always pass the radix 10.
- BigInt and Number cannot be mixed in arithmetic (TypeError): `1n + 1` fails.

### Operators (JavaScript syntax)

**Syntax**

```js
+ - * / % **            ++ --          // arithmetic (+ also concatenates strings)
= += -= *= /= %= **= <<= >>= >>>= &= |= ^=   &&= ||= ??= (ES2021)
=== !== == != < > <= >=  // comparison
&& || !  ??             // logical; ?? = right side only if left is null/undefined (ES2020)
a?.b  a?.[k]  a?.(args) // optional chaining (ES2020): undefined if a is null/undefined
cond ? x : y            // conditional
& | ^ ~ << >> >>>       // bitwise (32-bit ints)
typeof x   x instanceof C   'key' in obj   delete obj.key   void 0   ...spread   ,
```

**Check**

- `a || b` returns `b` when `a` is any falsy value (`0`, `''`). Use `a ?? b` to keep `0` and `''`.
- `??` cannot be mixed with `||` or `&&` without parentheses (SyntaxError).
- `'5' + 3` is `'53'`, but `'5' - 3` is `2`.

## Control flow

### if / else and switch (JavaScript statements)

**Syntax**

```js
if (cond) { ... } else if (other) { ... } else { ... }
switch (expr) {           // compares with ===
  case 1:
  case 2:
    doA();
    break;                // without break, execution falls through to the next case
  case 'x': { const y = 1; break; }   // braces give a case its own scope
  default:
    doB();
}
```

**Check**

- A missing `break` falls through. Declaring `let` or `const` in a case without braces is an error when the name repeats.

### Loops: for, for...of, for...in, while, do...while (JavaScript statements)

**Syntax**

```js
for (let i = 0; i < arr.length; i++) { }
for (const item of iterable) { }            // values: arrays, strings, Map, Set, NodeList, generators
for (const [i, v] of arr.entries()) { }     // index + value
for (const [k, v] of Object.entries(obj)) { }
for (const key in obj) { if (Object.hasOwn(obj, key)) { } }   // keys (including inherited ones); not for arrays
while (cond) { }
do { } while (cond);
for await (const chunk of asyncIterable) { }  // inside async functions (ES2018)
label: for (...) { for (...) { continue label; } }
break; continue;
```

**Check**

- `for...in` over an array yields string indexes and inherited keys. Use `for...of`.
- Inside callbacks such as `arr.forEach(x => { break; })`, `break` is a SyntaxError and `return` only skips the current item.
- `for (var i...)` with async callbacks captures one shared `i`. Use `let`.

### Exceptions: try / catch / finally / throw (JavaScript statements)

**Syntax**

```js
try {
  risky();
} catch (err) {                     // or catch { } without a binding (ES2019)
  console.error(err.message, err.stack);
  if (err instanceof TypeError) { }
} finally {
  cleanup();
}
throw new Error('message', { cause: original });   // cause: ES2022
class ValidationError extends Error { constructor(msg) { super(msg); this.name = 'ValidationError'; } }
```

**Built-in errors:** `Error`, `TypeError`, `RangeError`, `SyntaxError`, `ReferenceError`, `URIError`, `EvalError`, `AggregateError`.

**Check**

- try/catch catches an async error only when the call is `await`ed inside the try. For promises chained with `.then()`, use `.catch()`.
- Throw `Error` objects, not strings, so a stack trace is kept.

## Functions

### Function declarations, expressions and arrows (JavaScript syntax)

**Syntax**

```js
function add(a, b = 0, ...rest) { return a + b; }       // declaration (hoisted)
const mul = function (a, b) { return a * b; };          // expression
const sq = x => x * x;                                  // arrow, implicit return
const make = (a, b) => ({ a, b });                      // wrap object literals in ()
const run = async () => { await task(); };
function* gen() { yield 1; yield* other(); }           // generator
async function* stream() { yield await next(); }        // async generator
(function () { /* IIFE */ })();
```

**Parts**

- Default parameters apply when the argument is `undefined`, not when it is `null`.
- `...rest` collects the remaining arguments into an array. It must be last.
- `arguments` (array-like) exists in regular functions only, not in arrows.

**Check**

- An arrow with a block body needs `return`: `x => { x * 2 }` returns `undefined`.
- Arrow functions have no own `this`, `arguments` or `prototype`, and cannot be called with `new`.

### this, call, apply, bind (JavaScript semantics)

**Syntax**

```js
obj.method()            // this = obj
fn()                    // this = undefined (strict / modules) or globalThis (sloppy mode)
new Fn()                // this = the new object
fn.call(thisArg, a, b)  fn.apply(thisArg, [a, b])  const bound = fn.bind(thisArg, a)
el.addEventListener('click', function () { this /* = el */ });
el.addEventListener('click', () => { this /* = outer this */ });
```

**Check**

- Passing `obj.method` as a callback loses `this`. Pass `() => obj.method()` or `obj.method.bind(obj)`.

### Closures and scope (JavaScript semantics)

A function remembers the variables of the scope where it was created.

**Example**

```js
function counter() {
  let n = 0;
  return { inc: () => ++n, get: () => n };
}
const c = counter(); c.inc(); c.get();   // 1
```

**Check**

- Closures keep their variables alive. In long-lived listeners, remove the listener to free memory.

## Objects and classes

### Object literals and Object methods (JavaScript built-ins)

**Syntax**

```js
const key = 'dyn';
const obj = {
  name: 'Ann', 'with-dash': 1, [key + 1]: true,     // computed key
  age,                                             // shorthand for age: age
  greet() { return `Hi ${this.name}`; },           // method
  get upper() { return this.name.toUpperCase(); }, set upper(v) { this.name = v; },
  ...defaults,                                     // spread (shallow copy)
};
obj.name  obj['with-dash']  obj?.a?.b  delete obj.age  'name' in obj  Object.hasOwn(obj, 'name') (ES2022)
Object.keys(o)  Object.values(o)  Object.entries(o)  Object.fromEntries(pairs)  Object.assign(target, ...src)
Object.freeze(o)  Object.isFrozen(o)  Object.seal(o)  Object.create(proto)  Object.getPrototypeOf(o)
Object.defineProperty(o, 'x', { value: 1, writable: false, enumerable: true, configurable: false })
Object.groupBy(items, x => x.type) (ES2024)   structuredClone(o)  // deep copy (no functions, DOM nodes)
JSON.parse(JSON.stringify(o))   // old-style deep copy: loses Dates, undefined, Map, Set
```

**Check**

- Spread and `Object.assign` copy only one level deep. Nested objects are still shared.
- Object key order: integer keys first in ascending order, then string keys in insertion order.

### Classes (JavaScript syntax, ES2015+)

**Syntax**

```js
class Animal {
  static count = 0;                 // static field (ES2022)
  #secret = 42;                     // private field (ES2022)
  name;                             // public field
  constructor(name) { this.name = name; Animal.count++; }
  speak() { return `${this.name} makes a sound`; }
  get secret() { return this.#secret; }
  set secret(v) { this.#secret = v; }
  static create(n) { return new Animal(n); }
  #helper() { }                     // private method
  static { /* static init block (ES2022) */ }
}
class Dog extends Animal {
  constructor(name) { super(name); }      // super() before using this
  speak() { return `${super.speak()} - woof`; }
}
const d = new Dog('Rex');  d instanceof Animal  // true
```

**Check**

- In a subclass constructor, call `super(...)` before touching `this` (ReferenceError otherwise).
- Classes are not hoisted like function declarations. Define them before use.
- Methods are not auto-bound. See "this, call, apply, bind".

## Arrays and collections

### Array creation and access (JavaScript built-ins)

**Syntax**

```js
const a = [1, 2, 3];  Array.of(7)  Array(3)  // 3 empty slots   Array.from({ length: 5 }, (_, i) => i)  Array.from(nodeList)  [...set]
a.length  a[0]  a.at(-1) (ES2022)  a.length = 0  // clear
Array.isArray(a)  a.indexOf(x)  a.lastIndexOf(x)  a.includes(x) (handles NaN)
```

### Array methods that MUTATE (JavaScript Array API)

**Syntax**

```js
a.push(...items) → new length        a.pop() → removed last          a.unshift(...items) → length      a.shift() → removed first
a.splice(start, deleteCount, ...items) → removed array
a.sort([compareFn]) → a              a.reverse() → a                 a.fill(value[, start, end]) → a    a.copyWithin(target, start[, end])
```

**Check**

- `sort()` with no compare function sorts as strings, so `[10, 9, 1].sort()` gives `[1, 10, 9]`. Use `(x, y) => x - y`.
- `sort`, `reverse` and `splice` change the original array. The non-mutating versions are `toSorted()`, `toReversed()` and `toSpliced()` (ES2023).

### Array methods that return new values (JavaScript Array API)

**Syntax**

```js
a.map((item, index, array) => newItem)          → new array, same length
a.filter(item => keep)                          → new array of matches
a.reduce((acc, item, i) => acc + item, initial) → single value (always pass initial)
a.reduceRight(fn, initial)
a.find(pred) → item | undefined   a.findIndex(pred) → index | -1   a.findLast(pred)  a.findLastIndex(pred) (ES2023)
a.some(pred) → bool   a.every(pred) → bool
a.forEach((item, i) => {}) → undefined
a.flat([depth = 1])  a.flatMap(fn)  a.slice([start[, end]])  a.concat(...arrays)  a.join([sep = ','])
a.keys()  a.values()  a.entries()   a.toSorted(fn)  a.toReversed()  a.toSpliced(...)  a.with(index, value) (ES2023)
```

**Example**

```js
const total = cart.filter(i => i.qty > 0).map(i => i.qty * i.price).reduce((s, v) => s + v, 0);
const byId = Object.fromEntries(users.map(u => [u.id, u]));
```

**Check**

- `map` with a block body needs `return`.
- `reduce` without an initial value throws on an empty array.
- `find` returns `undefined` when nothing matches. Check for it before using the result.
- `arr.map(parseInt)` passes the index as the radix. Write `arr.map(s => parseInt(s, 10))`.

### Map, Set, WeakMap, WeakSet (JavaScript built-ins)

**Syntax**

```js
const m = new Map([['a', 1]]);  m.set(k, v)  m.get(k)  m.has(k)  m.delete(k)  m.clear()  m.size  m.keys()  m.values()  m.entries()  m.forEach((v, k) => {})
const s = new Set([1, 2, 2]);   s.add(x)  s.has(x)  s.delete(x)  s.size  [...s]   // unique values
s.union(t)  s.intersection(t)  s.difference(t)  s.isSubsetOf(t) (ES2025)
Map.groupBy(items, fn) (ES2024)
new WeakMap()  new WeakSet()   // keys must be objects; not iterable
```

**Check**

- Map and Set use `.size`, not `.length`. A Map uses `get` and `set`, not `m[key]`.
- Keys compare by reference: `m.get({})` never finds an object key that was set with a different `{}`.

## Asynchronous code

### Promises (JavaScript built-in)

**Syntax**

```js
const p = new Promise((resolve, reject) => { setTimeout(() => resolve(42), 100); });
p.then(value => ..., error => ...).catch(err => ...).finally(() => ...);
Promise.all([p1, p2])        // all fulfill → array; rejects on the first rejection
Promise.allSettled([p1, p2]) // array of {status, value | reason}
Promise.race([p1, p2])       // first settled
Promise.any([p1, p2])        // first fulfilled; AggregateError if all reject
Promise.resolve(v)  Promise.reject(err)  Promise.withResolvers() (ES2024)
```

**Check**

- Return the promise in `.then` chains. Forgetting `return` breaks the chain.
- Always end a chain with `.catch()`. Unhandled rejections crash Node.js.

### async / await (JavaScript syntax, ES2017)

**Syntax**

```js
async function load(url) {
  try {
    const res = await fetch(url);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.json();
  } catch (err) {
    console.error(err);
    throw err;
  }
}
const [a, b] = await Promise.all([load('/a'), load('/b')]);   // run in parallel
// top-level await: allowed in ES modules only
```

**Check**

- `await` outside an `async` function is a SyntaxError, except at the top level of a module.
- Awaiting in a loop runs the calls one after another. For independent work, use `Promise.all`.
- An `async` function always returns a Promise. A caller without `await` gets the Promise, not the value.

### Timers and scheduling (JavaScript / Web API)

**Syntax**

```js
const id = setTimeout(fn, ms, ...args);  clearTimeout(id);
const iv = setInterval(fn, ms);  clearInterval(iv);
requestAnimationFrame(timestamp => draw());  cancelAnimationFrame(id);
queueMicrotask(fn);   const sleep = ms => new Promise(r => setTimeout(r, ms));   await sleep(500);
```

**Check**

- `setTimeout(fn(), 100)` calls `fn` immediately. Pass the function itself: `setTimeout(fn, 100)` or `setTimeout(() => fn(x), 100)`.

## Modules

### import / export (JavaScript ES modules)

**Syntax**

```js
export const x = 1;  export function f() {}  export class C {}
export default function main() {}       // one default per module
export { a, b as bee };  export * from './mod.js';  export { default as Thing } from './thing.js';
import main, { a, bee as b } from './mod.js';   // file extension required in browsers
import * as utils from './utils.js';
import './side-effect.js';
import data from './data.json' with { type: 'json' };   // import attributes (ES2025)
const mod = await import('./lazy.js');                  // dynamic import → Promise
// CommonJS (Node): const fs = require('fs'); module.exports = { f };  exports.g = g;
```

**Check**

- Browsers need `<script type="module">` and relative paths that start with `./` or `/`, with the extension (`./app.js`, not `app`).
- `import` statements are hoisted and must be at the top level, not inside `if` or functions. Use `import()` there.
- In one file, do not mix `require` with `import`.

## Built-in utilities

### JSON (JavaScript built-in)

**Syntax**

```js
JSON.stringify(value[, replacer | keysArray[, space]])   // JSON.stringify(obj, null, 2)
JSON.parse(text[, reviver])                               // throws SyntaxError on bad JSON
```

**Check**

- JSON keys and strings use double quotes, with no trailing commas and no comments.
- `undefined`, functions and symbols are dropped by `stringify`. A `Date` becomes an ISO string and does not come back as a Date. `Map` and `Set` become `{}`.
- Wrap `JSON.parse` of untrusted text in try/catch.

### Date and Intl (JavaScript built-ins)

**Syntax**

```js
new Date()  new Date(2024, 0, 31)  // months are 0-based: 0 = January
new Date('2024-01-31T10:00:00Z')  Date.now() // ms since epoch   d.getTime()
d.getFullYear()  d.getMonth() // 0-11   d.getDate() // 1-31   d.getDay() // 0 = Sunday   d.getHours()  d.getMinutes()  d.getSeconds()
d.setDate(d.getDate() + 7)   d.toISOString()  d.toLocaleDateString('en-GB', { year: 'numeric', month: 'short', day: 'numeric' })
new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium', timeStyle: 'short' }).format(d)
new Intl.RelativeTimeFormat('en', { numeric: 'auto' }).format(-1, 'day')   // "yesterday"
new Intl.NumberFormat('en-IN').format(1234567.891)   new Intl.Collator('sv').compare(a, b)   new Intl.PluralRules('en').select(1)
```

**Check**

- `getMonth()` is 0-based but `getDate()` is 1-based. `getYear()` is deprecated: use `getFullYear()`.
- `new Date('2024-01-31')` (date only) is parsed as UTC, while `new Date('2024-01-31T00:00')` is local time.

### Regular expressions (JavaScript RegExp)

**Syntax**

```js
const re = /^(?<year>\d{4})-(\d{2})$/gimsuyd;  new RegExp(pattern, flags)
// flags: g global, i ignore case, m multiline ^$, s dotAll, u unicode, v unicodeSets (ES2024), y sticky, d indices
re.test(str) → bool    re.exec(str) → match | null (advances lastIndex with g)
str.match(re)  str.matchAll(re)  str.replace(re, '$<year>')  str.split(/\s*,\s*/)  str.search(re)
m.groups.year   m.index   m[1]
\d \w \s \b . [abc] [^a-z] (x|y) (?:x) (?<n>x) \k<n> x* x+ x? x{2,4} x*? (?=x) (?!x) (?<=x) (?<!x) \p{L} (with u)
RegExp.escape(text) (ES2025)
```

**Check**

- A regex with the `g` flag keeps `lastIndex` between `test` and `exec` calls, so alternating results are a classic bug. Do not reuse global regexes for `test`.
- Escape user input before `new RegExp(input)`.
- In `new RegExp('\\d+')`, backslashes are doubled inside the string.

### console (JavaScript / Web API)

**Syntax**

```js
console.log(...args)  console.info  console.warn  console.error  console.debug  console.table(arrayOrObj)  console.dir(obj)
console.group('label') / console.groupEnd()  console.time('t') / console.timeEnd('t')  console.count('x')  console.assert(cond, msg)  console.trace()
```

## Browser DOM

### Selecting elements (JavaScript DOM API)

**Syntax**

```js
document.getElementById('id')                → Element | null
document.querySelector('.css > selector')    → first Element | null
document.querySelectorAll('li.item')         → static NodeList (has forEach, not map)
el.closest('.card')  el.matches('.active')  el.children  el.firstElementChild  el.nextElementSibling  el.parentElement
document.getElementsByClassName('x')  document.getElementsByTagName('p')    → live HTMLCollection
document.body  document.head  document.documentElement  document.forms.myForm  document.activeElement
```

**Check**

- An id in `getElementById` has no `#`, while a selector in `querySelector` does: `querySelector('#id')`.
- A NodeList is not an array. Use `[...list].map(...)` or `Array.from(list)`.
- Check for null before using the result.

### Changing content, attributes and classes (JavaScript DOM API)

**Syntax**

```js
el.textContent = 'safe text';        el.innerHTML = '<b>trusted html only</b>';   el.innerText  el.outerHTML
el.setAttribute('aria-expanded', 'true')  el.getAttribute('href')  el.removeAttribute('disabled')  el.hasAttribute('hidden')  el.toggleAttribute('hidden', force)
el.id  el.className  el.classList.add('a', 'b')  .remove('a')  .toggle('open'[, force])  .contains('a')  .replace('a', 'b')
el.dataset.userId  // data-user-id   el.style.backgroundColor = 'red'   el.style.setProperty('--gap', '8px')   el.hidden = true
input.value  input.checked  select.value  select.selectedIndex  input.disabled = true  form.reset()
```

**Check**

- Assigning `innerHTML` with user input is an XSS hole. Use `textContent` or create elements.
- `style` properties use camelCase in JavaScript (`el.style.fontSize`), but CSS names in `setProperty` (`'font-size'`).
- `input.value` is always a string. Convert it with `Number()` before doing arithmetic.

### Creating and inserting elements (JavaScript DOM API)

**Syntax**

```js
const li = document.createElement('li');  li.textContent = name;  li.className = 'item';
parent.append(li, 'text')  parent.prepend(x)  el.before(x)  el.after(x)  el.replaceWith(x)  el.remove()
parent.appendChild(node)  parent.insertBefore(node, ref)  parent.removeChild(node)  parent.replaceChildren(...nodes)
el.insertAdjacentHTML('beforebegin | afterbegin | beforeend | afterend', html)
const frag = document.createDocumentFragment();  node.cloneNode(true)  document.importNode(tpl.content, true)
```

### Events (JavaScript DOM API)

**Syntax**

```js
el.addEventListener(type, handler[, { once: true, passive: true, capture: false, signal: abortCtrl.signal }]);
el.removeEventListener(type, sameHandlerReference);
function handler(e) {
  e.target  e.currentTarget  e.type  e.preventDefault()  e.stopPropagation()  e.key  e.code  e.ctrlKey  e.shiftKey
  e.clientX  e.clientY  e.button  e.deltaY  e.detail
}
document.addEventListener('DOMContentLoaded', init);   window.addEventListener('load', fn);
list.addEventListener('click', e => { const btn = e.target.closest('button[data-id]'); if (btn) remove(btn.dataset.id); });   // delegation
el.dispatchEvent(new CustomEvent('saved', { detail: { id: 1 }, bubbles: true }));
```

**Common events:** click, dblclick, contextmenu, mousedown, mouseup, mousemove, mouseenter, mouseleave, pointerdown, pointermove, pointerup, wheel, keydown, keyup, input, change, submit, reset, focus, blur, focusin, focusout, scroll, resize, load, error, DOMContentLoaded, beforeunload, visibilitychange, dragstart, dragover, drop, touchstart, transitionend, animationend, paste, copy.

**Check**

- `addEventListener('click', handle())` calls `handle` immediately. Pass `handle`.
- `removeEventListener` needs the same function reference. An inline arrow cannot be removed.
- The event names have no `on` prefix in `addEventListener`.
- A form `submit` handler must call `e.preventDefault()` to stop the page from reloading.
- `keypress` is deprecated. Use `keydown` with `e.key`.

### fetch (JavaScript Web API)

**Syntax**

```js
const res = await fetch(url, {
  method: 'GET | POST | PUT | PATCH | DELETE',
  headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
  body: JSON.stringify(data),          // or FormData, URLSearchParams, Blob, string
  signal: AbortSignal.timeout(10000),  // or controller.signal
  credentials: 'same-origin | include | omit', mode: 'cors | same-origin | no-cors', cache: 'no-store', redirect: 'follow',
});
res.ok  res.status  res.statusText  res.headers.get('content-type')
await res.json()  await res.text()  await res.blob()  await res.arrayBuffer()  await res.formData()   res.body (ReadableStream)
const ctrl = new AbortController();  ctrl.abort();
const qs = new URLSearchParams({ q: 'x', page: 2 }).toString();   new URL('/api', location.origin)
```

**Check**

- fetch rejects only on network errors. HTTP 404 and 500 resolve normally, so check `res.ok`.
- The body can be read only once.
- Do not set `Content-Type` when sending `FormData`, because the browser adds the boundary.

### Storage, location, history, clipboard (JavaScript Web APIs)

**Syntax**

```js
localStorage.setItem('k', JSON.stringify(v));  JSON.parse(localStorage.getItem('k') ?? 'null');  localStorage.removeItem('k');  localStorage.clear()
sessionStorage (same API, per tab)
location.href  location.search  location.hash  location.pathname  location.reload()  location.assign(url)
new URLSearchParams(location.search).get('id')
history.pushState(state, '', '/new')  history.replaceState(...)  window.addEventListener('popstate', e => {})
await navigator.clipboard.writeText('text')   await navigator.clipboard.readText()
```

**Check**

- Storage holds strings only, so store values with `JSON.stringify`. `getItem` returns `null` for a missing key. Access can throw in private mode, so wrap it in try/catch.

### Window, layout and observers (JavaScript Web APIs)

**Syntax**

```js
window.innerWidth  innerHeight  scrollX  scrollY  devicePixelRatio  matchMedia('(prefers-color-scheme: dark)').matches
window.scrollTo({ top: 0, behavior: 'smooth' })  el.scrollIntoView({ behavior: 'smooth', block: 'start' })
el.getBoundingClientRect()  el.offsetWidth  el.clientHeight  el.scrollHeight  el.scrollTop  getComputedStyle(el).color
new ResizeObserver(entries => {}).observe(el)
new IntersectionObserver(entries => entries.forEach(e => e.isIntersecting), { threshold: 0.1 }).observe(el)
new MutationObserver(records => {}).observe(el, { childList: true, subtree: true, attributes: true })
alert(msg)  confirm(msg) → bool  prompt(msg, default) → string | null
```

## Node.js essentials

### Node.js modules: fs, path, process (JavaScript / Node API)

**Syntax**

```js
import fs from 'node:fs';  import { readFile, writeFile, mkdir, readdir, stat, rm } from 'node:fs/promises';  import path from 'node:path';
const text = await readFile('a.txt', 'utf8');  await writeFile('b.txt', text, 'utf8');  await mkdir('out', { recursive: true });
fs.readFileSync(p, 'utf8')  fs.existsSync(p)  fs.writeFileSync(p, data)
path.join(a, b)  path.resolve('rel')  path.basename(p[, ext])  path.dirname(p)  path.extname(p)
process.argv  process.env.HOME  process.exit(code)  process.cwd()  import.meta.url  import.meta.dirname (Node 20.11+)
// CommonJS only: __dirname  __filename  require()
```

**Check**

- `__dirname` does not exist in ES modules. Use `import.meta.dirname` or `path.dirname(fileURLToPath(import.meta.url))`.
- Without an encoding, `readFile` returns a Buffer, not a string.
