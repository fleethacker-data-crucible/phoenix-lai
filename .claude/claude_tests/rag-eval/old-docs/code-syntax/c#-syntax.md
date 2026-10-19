# C# syntax reference (Phoenix)

For Phoenix models writing and checking C# code. Each `###` entry is one keyword, statement, operator or API: what it does, its exact syntax, every part, an example, and the mistakes to check for. Every feature newer than C# 5 is marked with the version that introduced it, so code for an older compiler can be checked. For example, .NET Framework's built-in compiler (csc.exe, CodeDom, `Add-Type` in Windows PowerShell 5.1) accepts C# 5 only.

**Notation:** `[x]` optional · `{a | b}` choose one · `...` repeatable · `<name>` you supply · **C# N+** needs language version N or later.

**Language versions:** C# 5 (2012, .NET 4.5: async/await, caller info) · C# 6 (2015: `?.`, `$""`, `nameof`, `=>` members, auto-property initializers) · C# 7.0-7.3 (2017: tuples, `out var`, pattern matching, local functions, `ref` locals, `Span<T>`) · C# 8 (2019: nullable reference types, `switch` expressions, ranges `^1`/`..`, `using` declarations, default interface members, async streams) · C# 9 (2020: records, `init`, top-level statements, `new()` target-typed) · C# 10 (2021: file-scoped namespaces, global using, record structs) · C# 11 (2022: raw string literals, `required`, list patterns, generic math) · C# 12 (2023: primary constructors for classes, collection expressions `[1, 2]`) · C# 13 (2024: `params` collections, `\e`, new `lock` type) · C# 14 (2025: `field` keyword, extension members, null-conditional assignment).

## Checklist: before you answer with C# code

- Every statement ends with `;`. Every `{` has a matching `}`. `if`, `for` and `while` conditions are in parentheses.
- Every name exists: types need their `using` directive (`System.Collections.Generic` for `List<T>`, `System.Linq` for LINQ, `System.IO`, `System.Text`, `System.Threading.Tasks`).
- Types match: no implicit narrowing (`int x = 3.5;` is an error), `string` is not `char` (`'a'` vs `"a"`), and integer division truncates (`5 / 2 == 2`).
- Equality is `==` and assignment is `=`. `if (x = 5)` does not compile for an `int`.
- `string` comparisons that ignore case use `string.Equals(a, b, StringComparison.OrdinalIgnoreCase)`, not `ToLower()`.
- Do not modify a collection inside `foreach` over it (it throws InvalidOperationException). Iterate a copy (`.ToList()`) or use `for` backwards.
- Dispose `IDisposable` objects with `using`: files, streams, readers, writers, connections, HTTP responses, timers.
- `async` methods return `Task`, `Task<T>` or `ValueTask` (`void` only for event handlers), and their callers `await` them. Never use `.Result` or `.Wait()` in UI or ASP.NET code (deadlock).
- Check the language version. When the target is C# 5 (.NET Framework CodeDom), the code must not use `$"..."`, `?.`, `nameof`, `=>` on members, tuples `(a, b)`, `out var`, pattern `is T x`, `switch` expressions, `using var`, records, `default` literal without a type, local functions, or `Span<T>`.
- Floating point: compare with a tolerance (`Math.Abs(a - b) < 1e-9`), and use `decimal` for money.
- A `null` check comes before member access: `if (s != null && s.Length > 0)`, or `string.IsNullOrEmpty(s)`.

## Program structure

### Program, Main and namespaces (C# structure)

A C# program is a set of types in namespaces. Execution starts at `static void Main` (or `static int Main`, `static async Task Main` in C# 7.1+), or at top-level statements (C# 9+).

**Syntax**

```csharp
using System;                       // using directives first
using System.Collections.Generic;
using Alias = System.Text.StringBuilder;   // alias
using static System.Math;           // C# 6+: static members without the class name

namespace MyApp                     // block-scoped namespace
{
    internal static class Program
    {
        static int Main(string[] args)  // args = command-line arguments, no program name
        {
            Console.WriteLine("Hello " + (args.Length > 0 ? args[0] : "world"));
            return 0;                   // exit code
        }
    }
}
namespace MyApp.Tools;              // C# 10+: file-scoped namespace (rest of the file)
global using System.Linq;           // C# 10+: applies to the whole project
```

**Parts**

- `using <namespace>;` imports the namespace's types. It must come before any namespace or type declaration in the file.
- Valid `Main` signatures: `static void Main()`, `static void Main(string[] args)`, `static int Main(...)`, `static Task Main(...)` and `static Task<int> Main(...)` (C# 7.1+).
- Top-level statements (C# 9+): statements written directly in one file of the project, with no class and no `Main`. `args` is available.

**Check**

- There is exactly one entry point per program. Top-level statements are allowed in only one file.
- A `using` directive (import) is different from a `using` statement (dispose). See `using statement`.

### Comments and XML documentation (C# syntax)

**Syntax**

```csharp
// single line
/* multi
   line */
/// <summary>Adds two numbers.</summary>
/// <param name="a">First number.</param>
/// <returns>The sum.</returns>
/// <exception cref="OverflowException">On overflow in a checked context.</exception>
public static int Add(int a, int b) { return a + b; }
```

**Check**

- `/* */` comments do not nest.

### Preprocessor directives (C# syntax)

**Syntax**

```csharp
#define DEBUGLOG            // must come before any code in the file
#undef DEBUGLOG
#if DEBUG && !NET48
#elif NET6_0_OR_GREATER
#else
#endif
#region Helpers
#endregion
#warning text   #error text
#pragma warning disable CS0168, CS0219
#pragma warning restore CS0168
#nullable enable | disable | restore      // C# 8+
#line 200 "file.cs"
```

**Check**

- `#if` symbols are defined by `#define` or by the build (DEBUG, TRACE, NET48, NET8_0, NET5_0_OR_GREATER…). They are not C# constants.

## Types and variables

### Built-in value types (C# types)

**Syntax**

| Keyword | .NET type | Size | Range / precision | Literal |
|---|---|---|---|---|
| `bool` | System.Boolean | 1 B | true/false | `true` |
| `byte` / `sbyte` | Byte / SByte | 1 B | 0..255 / -128..127 | `(byte)1` |
| `short` / `ushort` | Int16 / UInt16 | 2 B | ±32 767 / 0..65 535 | `(short)1` |
| `int` / `uint` | Int32 / UInt32 | 4 B | ±2.1e9 / 0..4.29e9 | `1`, `1u` |
| `long` / `ulong` | Int64 / UInt64 | 8 B | ±9.2e18 / 0..1.8e19 | `1L`, `1UL` |
| `nint` / `nuint` | IntPtr / UIntPtr | 4/8 B | pointer size (C# 9+) | |
| `char` | Char | 2 B | UTF-16 code unit | `'a'`, `'\n'`, `'\u00e9'` |
| `float` | Single | 4 B | ~7 digits | `1.5f` |
| `double` | Double | 8 B | ~15-16 digits | `1.5`, `1e-3`, `1.5d` |
| `decimal` | Decimal | 16 B | 28-29 digits, base 10 | `1.5m` |

Integer literals: `0x1F` hex, `0b1010` binary (C# 7+), `1_000_000` digit separators (C# 7+).

**Check**

- `float f = 1.5;` is an error: use `1.5f`. `decimal d = 1.5;` is an error: use `1.5m`.
- Integer overflow wraps silently unless the code is in a `checked` context.
- `0.1 + 0.2 != 0.3` in double. Use `decimal` for money.

### Variables, var, const, readonly (C# syntax)

**Syntax**

```csharp
int count = 0;                   // declaration with initializer
int a = 1, b = 2;                // several of one type
var name = "Ann";                // C# 3+: type inferred from the initializer (still static typing)
const double Pi = 3.14159;       // compile-time constant: literal values only
static readonly DateTime Start = DateTime.Now;   // field set once (declaration or constructor)
string s = default(string);      // default value: null / 0 / false
int n = default;                 // C# 7.1+: default literal
dynamic d = GetObject();         // C# 4+: member access checked at run time
```

**Check**

- `var` needs an initializer, and that initializer cannot be `null` alone: `var x;` and `var x = null;` are errors.
- A local variable must be assigned before it is read (CS0165).
- `const` works only for primitives, strings, enums and null. Use `static readonly` for objects and arrays.

### Strings and string literals (C# syntax)

`string` (System.String) is an immutable sequence of UTF-16 chars. Every "modifying" method returns a new string.

**Syntax**

```csharp
string s = "Tab\tQuote\" Backslash\\ Newline\n Unicode\u00e9";
string path = @"C:\temp\file.txt";            // verbatim: no escapes, "" is a quote, may span lines
string msg = $"Hi {name}, you are {age} ({age:D3}) {price:C2} {when:yyyy-MM-dd} {x,8:F2}";   // C# 6+: interpolation
string both = $@"C:\Users\{user}\docs";       // interpolated verbatim (also @$ in C# 8+)
string raw = """
    He said "hi" and \n stays literal
    """;                                       // C# 11+: raw string literal
string concat = "a" + 1 + 2;                  // "a12"
string fmt = string.Format("{0} has {1:N0} items", name, count);   // all versions
```

**Escapes:** `\'` `\"` `\\` `\0` `\a` `\b` `\f` `\n` `\r` `\t` `\v` `\uXXXX` `\xH[H][H][H]` `\UXXXXXXXX` `\e` (C# 13+).

**Check**

- `$"..."` is C# 6+. For C# 5, use `string.Format` or `+`.
- To write a literal brace in `$""` or `string.Format`, double it: `{{` and `}}`.
- Building strings in a loop with `+=` is O(n²). Use `StringBuilder`.
- `"1" + 2 + 3` is `"123"`, while `1 + 2 + "3"` is `"33"`.

### String methods (C# System.String API)

**Syntax**

```csharp
s.Length   s[i]   s.Substring(start[, length])   s.IndexOf("x"[, start][, StringComparison])   s.LastIndexOf('x')
s.Contains("x")   s.StartsWith("x"[, StringComparison])   s.EndsWith("x")   s.Equals(t, StringComparison.OrdinalIgnoreCase)
s.ToUpper()   s.ToLowerInvariant()   s.Trim()   s.TrimStart('0')   s.TrimEnd('/', '\\')   s.PadLeft(5, '0')   s.PadRight(10)
s.Replace("a", "b")   s.Insert(2, "x")   s.Remove(2[, count])   s.Split(',')   s.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)
string.Join(", ", items)   string.Concat(a, b)   string.IsNullOrEmpty(s)   string.IsNullOrWhiteSpace(s)   string.Compare(a, b, StringComparison.Ordinal)
s.ToCharArray()   new string('-', 20)   s.Normalize()   s.Format(...)
```

**Arguments**

- `StringComparison`: `Ordinal` (exact bytes, fastest, for identifiers and paths), `OrdinalIgnoreCase`, `CurrentCulture` (user-facing text), `InvariantCulture`.
- `Substring(start, length)` throws `ArgumentOutOfRangeException` if `start + length > Length`.

**Check**

- Strings are immutable: `s.Trim();` alone does nothing. Write `s = s.Trim();`.
- `s.Contains("x", StringComparison.OrdinalIgnoreCase)` exists only in .NET Core 2.1+ (not .NET Framework). Use `s.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0`.
- `Split(string)` with one string argument exists only in .NET Core 2.0+. On .NET Framework, use `Split(new[] { "ab" }, StringSplitOptions.None)`.

### StringBuilder (C# System.Text API)

Mutable string buffer for building text in loops.

**Syntax**

```csharp
using System.Text;
var sb = new StringBuilder([capacity]);
sb.Append(x).Append(',');  sb.AppendLine("line");  sb.AppendFormat("{0:F2}", v);
sb.Insert(0, "x");  sb.Remove(start, length);  sb.Replace("a", "b");  sb.Clear();  sb.Length = 0;
string result = sb.ToString();
```

**Example**

```csharp
var sb = new StringBuilder();
for (int i = 0; i < 3; i++) sb.Append(i).Append(i < 2 ? "," : "");
Console.WriteLine(sb.ToString());   // 0,1,2
```

### Arrays (C# types)

**Syntax**

```csharp
int[] a = new int[5];                     // zero-filled, fixed length
int[] b = { 1, 2, 3 };                    // initializer
var c = new[] { "x", "y" };               // implicitly typed array
int[,] grid = new int[3, 4];              // rectangular: grid[r, c], grid.GetLength(0)
int[][] jag = new int[3][];               // jagged: jag[0] = new int[2];
int[] d = [1, 2, 3];                      // C# 12+: collection expression
a.Length   Array.Sort(a)   Array.Reverse(a)   Array.IndexOf(a, 3)   Array.Copy(src, dst, n)   Array.Resize(ref a, 10)
Array.Empty<int>()   (int[])a.Clone()   a[^1] (C# 8+: last)   a[1..3] (C# 8+: range, a copy)
```

**Check**

- Indexes run from 0 to `Length - 1`. A loop `i <= a.Length` throws `IndexOutOfRangeException`.
- Arrays are reference types: `b = a` copies the reference, not the elements.
- Use `.Length` for arrays and `.Count` for `List<T>` (`.Count()` is LINQ).

### Nullable value types (C# types)

**Syntax**

```csharp
int? n = null;                    // Nullable<int>
if (n.HasValue) Console.WriteLine(n.Value);
int v = n ?? 0;                   // null-coalescing: value or default
int w = n.GetValueOrDefault();    // 0 when null
n ??= 5;                          // C# 8+: assign if null
```

**Check**

- `n.Value` throws `InvalidOperationException` when `n` is null.
- An arithmetic result with a null operand is null: `int? r = n + 1;`.

### Nullable reference types (C# 8+ feature)

With `#nullable enable` (or `<Nullable>enable</Nullable>`), `string` means non-null and `string?` means it may be null. The compiler warns instead of failing.

**Syntax**

```csharp
#nullable enable
string name = "x";       // never null
string? maybe = null;    // may be null
int len = maybe!.Length; // ! null-forgiving operator: "trust me"
if (maybe is not null) { len = maybe.Length; }   // C# 9+: is not
```

**Check**

- `?` on a reference type does nothing at run time. It only drives warnings.
- `!` hides a warning and does not prevent a `NullReferenceException`.

### Type conversion and casting (C# syntax)

**Syntax**

```csharp
long l = intValue;                  // implicit widening
int i = (int)3.99;                  // explicit cast: truncates to 3
int j = (int)Math.Round(3.5);       // 4 (banker's rounding: 2.5 rounds to 2; use MidpointRounding.AwayFromZero)
string s = i.ToString();  s = i.ToString("N2", CultureInfo.InvariantCulture);
int k = int.Parse("42");            // throws FormatException on bad input
if (int.TryParse(text, out int m))  // C# 7+: out var; C# 5: int m; int.TryParse(text, out m)
double d = Convert.ToDouble("1.5", CultureInfo.InvariantCulture);
object o = 42; int u = (int)o;      // unboxing must be the exact type
string t = o as string;             // as: null instead of exception (reference/nullable types only)
if (o is string str) { }            // C# 7+: type pattern with variable
```

**Check**

- `(int)someLong` overflows silently. Use `checked((int)x)` or `Convert.ToInt32` to get an exception.
- Parsing depends on the culture: `double.Parse("1.5")` fails on a German machine. Pass `CultureInfo.InvariantCulture` for data.
- `(int)(object)5L` throws `InvalidCastException`. Unbox to the exact type first: `(int)(long)o`.

### Enums (C# types)

**Syntax**

```csharp
enum Color { Red, Green = 5, Blue }        // Red=0, Green=5, Blue=6; underlying int
enum Small : byte { A = 1, B = 2 }
[Flags] enum Access { None = 0, Read = 1, Write = 2, Exec = 4, All = Read | Write | Exec }
Color c = Color.Blue;  int v = (int)c;  Color p = (Color)5;
string name = c.ToString();  Color parsed = (Color)Enum.Parse(typeof(Color), "Red", true);
Enum.TryParse("Red", out Color r);   Enum.GetValues(typeof(Color));   Enum.IsDefined(typeof(Color), 7)
bool canRead = (acc & Access.Read) != 0;   acc.HasFlag(Access.Read)   // HasFlag: .NET 4+
```

**Check**

- Any integer can be cast to an enum, even one with no named value. Validate with `Enum.IsDefined`.
- `[Flags]` values must be powers of two.

### Tuples (C# 7+ feature)

**Syntax**

```csharp
(int Min, int Max) range = (1, 10);       // ValueTuple with named elements
var t = (Name: "Ann", Age: 30);
Console.WriteLine(t.Name + t.Item2);
(int q, int r) = (17 / 5, 17 % 5);        // deconstruction
static (bool ok, string error) Validate(string s) { return (s.Length > 0, "empty"); }
var old = Tuple.Create(1, "a");           // System.Tuple (all versions): old.Item1, immutable class
```

**Check**

- Tuple syntax `(a, b)` is C# 7+, and on .NET Framework 4.6.2 and earlier it needs the System.ValueTuple package. For C# 5, use `Tuple<T1, T2>`, `out` parameters or a small class.

## Operators

### Arithmetic, assignment and increment operators (C# operators)

**Syntax**

```csharp
+  -  *  /  %                // integer / truncates toward zero; % keeps the sign of the left operand
x += 1; x -= 1; x *= 2; x /= 2; x %= 3; x <<= 1; x >>= 1; x &= m; x |= m; x ^= m;
x++  ++x  x--  --x           // post-increment returns the old value, pre-increment the new
checked(a * b)   unchecked(a * b)
```

**Check**

- `7 / 2` is `3`, and `7 / 2.0` is `3.5`. Cast one operand when you want a fractional result: `(double)a / b`.
- Integer `/ 0` throws `DivideByZeroException`, while `double / 0` is `Infinity`.
- `-7 % 3` is `-1`.

### Comparison, logical and bitwise operators (C# operators)

**Syntax**

```csharp
==  !=  <  >  <=  >=
&&  ||  !                    // short-circuit logical
&  |  ^  ~                   // bitwise (and non-short-circuit logical on bool)
<<  >>  >>>                  // shifts; >>> unsigned right shift (C# 11+)
cond ? a : b                 // conditional
a ?? b                       // null-coalescing
a ??= b                      // C# 8+
obj?.Member   arr?[0]        // C# 6+: null-conditional (result is null if obj is null)
x is T   x is T v   x as T   typeof(T)   sizeof(int)   nameof(x) (C# 6+)   default(T)
```

**Precedence (high to low):** primary `x.y f(x) a[i] x++ new typeof` · unary `+ - ! ~ ++x (T)x await` · `* / %` · `+ -` · `<< >>` · `< > <= >= is as` · `== !=` · `&` · `^` · `|` · `&&` · `||` · `??` · `?:` · assignment and lambda `= += => `.

**Check**

- `==` on two `object` variables compares references, even when both hold equal strings. Use `.Equals` or `string.Equals`.
- `a & b == c` parses as `a & (b == c)`. Use parentheses with bitwise operators.
- `?.` is C# 6+. For C# 5, write `x != null ? x.Member : null`.

### Lambda expressions and the => operator (C# syntax)

**Syntax**

```csharp
x => x * 2                              // one parameter, expression body
(x, y) => x + y
() => Console.WriteLine("hi")
(int x) => { var y = x * 2; return y; } // statement body
static x => x + 1                       // C# 9+: static lambda (no captures)
Func<int, int> twice = x => x * 2;      // Func<in..., out TResult>
Action<string> print = s => Console.WriteLine(s);
Predicate<int> isEven = n => n % 2 == 0;
public int Area => W * H;               // C# 6+: expression-bodied member (property)
public override string ToString() => Name;   // C# 6+ for methods; C# 7+ for constructors and accessors
```

**Check**

- Lambdas capture variables, not values. In C# 5+, the variable of a `foreach` is fresh on each iteration, but a `for` loop variable is shared: copy it first (`int j = i;`).
- An expression-bodied member (`=>` on a method or property) is C# 6+. For C# 5, write `{ get { return W * H; } }`.

## Statements and control flow

### if / else (C# statement)

**Syntax**

```csharp
if (<bool expression>) <statement or { block }>
else if (<bool expression>) { ... }
else { ... }
```

**Example**

```csharp
if (age >= 18) Console.WriteLine("adult");
else if (age >= 13) Console.WriteLine("teen");
else Console.WriteLine("child");
```

**Check**

- The condition must be `bool`. `if (count)` is an error: write `if (count != 0)`.
- Use braces for multi-line bodies. Without braces, only the first statement belongs to the `if`.

### switch statement (C# statement)

**Syntax**

```csharp
switch (<expression>)
{
    case <constant>:
    case <constant2>:              // stacked labels share a body
        <statements>
        break;                     // every section must end with break, return, throw, goto or continue
    case int n when n > 100:       // C# 7+: type pattern + when guard
        break;
    case null:                     // C# 7+
        break;
    default:
        break;
}
```

**Check**

- C# has no fall-through from a section with statements: `case 1: x(); case 2:` is error CS0163. Use `goto case 2;` when you mean it.
- C# 5 switch case labels are constants only (int, char, string, enum, bool). Patterns and `when` are C# 7+.

### switch expression (C# 8+ expression)

**Syntax**

```csharp
var result = <value> switch
{
    <pattern> => <expression>,
    <pattern> when <condition> => <expression>,
    _ => <default expression>          // discard: anything else
};
```

**Example**

```csharp
string size = n switch { < 0 => "negative", 0 => "zero", <= 10 => "small", _ => "large" };   // relational patterns C# 9+
decimal tax = shape switch { Circle c => c.R * c.R, Rect { W: var w, H: var h } => w * h, _ => 0 };
```

**Check**

- Arms are separated by commas, and the whole thing ends with `;`.
- A value that matches no arm throws `SwitchExpressionException` at run time. Add `_ =>`.

### Pattern matching (C# 7+ feature)

**Syntax**

```csharp
x is int n                     // type pattern + designation (C# 7)
x is null      x is not null   // C# 7 / C# 9
x is > 0 and < 10              // relational + logical patterns (C# 9)
x is 1 or 2 or 3               // C# 9
p is { Name: "Ann", Age: >= 18 }    // property pattern (C# 8)
p is (0, 0)                    // positional pattern (C# 8, needs Deconstruct)
arr is [1, _, .. var rest]     // list pattern (C# 11)
```

**Check**

- A variable declared in a pattern is definitely assigned only where the pattern matched: inside `if (x is int n) { ... }`.

### for loop (C# statement)

**Syntax**

```csharp
for ([<initializer>]; [<condition>]; [<iterator>]) <statement or block>
for (int i = 0; i < n; i++) { }
for (int i = n - 1; i >= 0; i--) { }          // backwards: safe to remove items
for (int i = 0, j = 10; i < j; i++, j--) { }
for (;;) { }                                   // infinite
```

**Check**

- Off-by-one: use `i < arr.Length`, not `<=`.
- Removing items while counting up skips elements. Loop backwards instead, or use `list.RemoveAll(pred)`.

### foreach loop (C# statement)

Iterates any `IEnumerable` / `IEnumerable<T>` (arrays, lists, dictionaries, LINQ queries, strings).

**Syntax**

```csharp
foreach (<type | var> <name> in <collection>) <statement or block>
foreach (var kv in dict) Console.WriteLine(kv.Key + "=" + kv.Value);
foreach (var (key, value) in dict) { }        // C# 7+ deconstruction (.NET Core 2.0+ KeyValuePair.Deconstruct)
await foreach (var item in asyncStream) { }   // C# 8+
```

**Check**

- The loop variable is read-only: `foreach (var x in list) x = 0;` is an error.
- Adding to or removing from the collection inside the loop throws `InvalidOperationException` ("Collection was modified").
- A LINQ query runs again on every `foreach`. Call `.ToList()` once when it is reused.

### while and do-while loops (C# statement)

**Syntax**

```csharp
while (<condition>) { <body> }          // may run 0 times
do { <body> } while (<condition>);      // runs at least once; note the ;
```

**Example**

```csharp
string line;
while ((line = reader.ReadLine()) != null) { Process(line); }
```

### break, continue, return, goto, yield (C# statements)

**Syntax**

```csharp
break;               // leave the innermost loop or switch
continue;            // next iteration of the innermost loop
return;  return <expr>;
goto <label>;  goto case 3;  goto default;
yield return <expr>;  yield break;    // iterator methods returning IEnumerable<T> / IEnumerator<T>
```

**Example**

```csharp
static IEnumerable<int> Evens(int max)
{
    for (int i = 0; i <= max; i += 2) yield return i;
}
```

**Check**

- An iterator method runs lazily, only when it is enumerated. Argument checks inside it run late, so validate in a non-iterator wrapper.
- `yield` cannot appear inside a `try` block with a `catch`, or inside a `finally`.

### Exceptions: try / catch / finally / throw (C# statement)

**Syntax**

```csharp
try { <statements> }
catch (FileNotFoundException ex) { <handle; ex.Message, ex.FileName> }
catch (IOException ex) when (ex.HResult == -2147024864) { }   // C# 6+: exception filter
catch (Exception ex) { Log(ex); throw; }                       // throw; rethrows, keeping the stack trace
finally { <always runs: cleanup> }
throw new ArgumentNullException("name");                       // C# 6+: nameof(name)
throw new InvalidOperationException("msg", innerException);
var x = y ?? throw new ArgumentNullException(nameof(y));       // C# 7+: throw expression
```

**Common exception types:** `ArgumentException`, `ArgumentNullException`, `ArgumentOutOfRangeException`, `InvalidOperationException`, `NotSupportedException`, `NotImplementedException`, `NullReferenceException`, `IndexOutOfRangeException`, `KeyNotFoundException`, `FormatException`, `InvalidCastException`, `OverflowException`, `DivideByZeroException`, `IOException`, `FileNotFoundException`, `DirectoryNotFoundException`, `UnauthorizedAccessException`, `TimeoutException`, `OperationCanceledException` / `TaskCanceledException`, `ObjectDisposedException`, `AggregateException`.

**Check**

- Order catch blocks from most specific to least specific. A base type listed first makes the later blocks unreachable (CS0160).
- `throw ex;` resets the stack trace. Use `throw;`.
- Do not catch `Exception` just to ignore it. At least log it.
- `await` inside `catch` and `finally` needs C# 6+.

### using statement and IDisposable (C# statement)

Calls `Dispose()` when the block ends, even after an exception.

**Syntax**

```csharp
using (var reader = new StreamReader(path)) { <use reader> }
using (var a = new A()) using (var b = new B()) { }    // stacked
using var file = File.OpenWrite(path);                // C# 8+: disposed at the end of the enclosing scope
await using var conn = new AsyncResource();           // C# 8+: IAsyncDisposable
```

**Check**

- Do not return an object created in a `using` from inside that block. It is already disposed when the caller gets it.
- `using var` is C# 8+. For C# 5, use the block form.

### lock, checked, fixed, unsafe (C# statements)

**Syntax**

```csharp
private readonly object _gate = new object();
lock (_gate) { <critical section> }        // Monitor.Enter/Exit; one thread at a time
checked { int z = x * y; }                 // overflow throws OverflowException
unchecked { int h = a * 31 + b; }
unsafe { int* p = &x; }                    // needs /unsafe
fixed (byte* p = buffer) { <p is pinned> } // pin a managed array inside unsafe
```

**Check**

- Never `lock (this)`, `lock (typeof(X))` or `lock` on a string. Lock a private readonly object.
- `await` is not allowed inside a `lock` block. Use `SemaphoreSlim.WaitAsync` instead.

## Methods and parameters

### Methods (C# member)

**Syntax**

```csharp
[<access>] [static] [async] [virtual | override | abstract | sealed override | new] <ReturnType> <Name>[<T>](<parameters>) [where T : <constraint>]
{
    <statements>
    return <value>;          // required on every path unless the return type is void
}
public static int Add(int a, int b) { return a + b; }
public static int Add(int a, int b, int c) { return a + b + c; }   // overload: different parameter list
```

**Parts**

- Access: `public`, `private` (the default for members), `protected`, `internal` (the default for top-level types), `protected internal`, `private protected` (C# 7.2+).
- `static`: called on the type (`Math.Max`), with no `this`.
- `virtual`: may be overridden. `override` replaces a base virtual or abstract method. `abstract` has no body (abstract classes only). `sealed override` stops further overriding. `new` hides a base member.

**Check**

- Every code path of a non-void method must return a value (CS0161).
- Overloads cannot differ only by return type.

### Parameters: ref, out, in, params, optional, named (C# syntax)

**Syntax**

```csharp
void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }     // caller: Swap(ref x, ref y)
bool TryGet(string key, out int value) { value = 0; return false; } // must assign every out
void Print(in BigStruct s) { }                                     // C# 7.2+: read-only by reference
int Sum(params int[] nums) { return nums.Sum(); }                  // Sum(1, 2, 3)
void Log(string msg, int level = 1, string tag = "app") { }        // optional: compile-time constants
Log("hi", tag: "db");                                              // named argument (C# 4+)
void Who([CallerMemberName] string caller = "") { }                // C# 5+: System.Runtime.CompilerServices
```

**Check**

- `ref` and `out` must also be written at the call site.
- `params` must be the last parameter. Optional parameters come after the required ones.
- `out var x` at the call site is C# 7+. In C# 5, declare `int x;` first.
- An `async` method cannot have `ref`, `out` or `in` parameters.

### Local functions (C# 7+ feature)

**Syntax**

```csharp
int Factorial(int n)
{
    return Inner(n);
    int Inner(int k) { return k <= 1 ? 1 : k * Inner(k - 1); }   // declared inside a method
    static int Pure(int k) => k * 2;                              // C# 8+: static local function
}
```

**Check**

- C# 7+. For C# 5, use a private method or a `Func<>` lambda.

### Extension methods (C# 3+ feature)

**Syntax**

```csharp
public static class StringExtensions               // static, non-generic, top-level class
{
    public static bool IsBlank(this string s)       // 'this' on the first parameter
    {
        return string.IsNullOrWhiteSpace(s);
    }
}
// usage (namespace imported): if (name.IsBlank()) { }
```

**Check**

- The class must be `static` and not nested. Callers need the `using` for its namespace.

## Classes, structs, interfaces and records

### Classes and objects (C# type)

**Syntax**

```csharp
[<access>] [static | abstract | sealed | partial] class <Name>[<T>] [: <BaseClass>][, <IInterface>...] [where T : ...]
{
    private int _count;                              // field
    public const int Max = 10;                       // constant
    public static int Instances;                     // static field
    public string Name { get; set; }                 // auto-property
    public <Name>(string name) { Name = name; Instances++; }   // constructor
    public <Name>() : this("none") { }               // chain to another constructor
    static <Name>() { }                              // static constructor (runs once)
    ~<Name>() { }                                    // finalizer (rarely needed)
    public override string ToString() { return Name; }
}
var p = new Person("Ann");                // object creation
Person q = new("Bob");                    // C# 9+: target-typed new
var r = new Person { Name = "Cy" };       // object initializer (C# 3+)
```

**Check**

- When a class declares a constructor with parameters, it has no default constructor unless one is written.
- A derived class constructor calls the base with `: base(args)` when the base has no parameterless constructor.
- `static` classes cannot be instantiated or inherited, and all their members must be static.

### Properties (C# member)

**Syntax**

```csharp
public int Age { get; set; }                               // auto-property (C# 3+)
public int Id { get; private set; }                        // restricted setter
public string Name { get; set; } = "none";                 // C# 6+: initializer
public int Max { get; }                                    // C# 6+: getter-only (set in the constructor)
public DateTime Born { get; init; }                        // C# 9+: settable only during initialization
public required string Key { get; set; }                   // C# 11+: must be set by the initializer
private int _w;
public int Width
{
    get { return _w; }
    set { if (value < 0) throw new ArgumentOutOfRangeException("value"); _w = value; }   // 'value' is the new value
}
public int Area => Width * Height;                         // C# 6+: expression-bodied read-only property
public string this[int i] { get { return _items[i]; } set { _items[i] = value; } }   // indexer
```

**Check**

- C# 5 has no property initializers, getter-only auto-properties or `=>` properties. Use a backing field or set the value in the constructor.
- A property whose getter returns the property itself (`get { return Width; }`) recurses forever. Return the backing field.

### Inheritance, abstract, virtual, override (C# feature)

**Syntax**

```csharp
public abstract class Shape
{
    public abstract double Area();                    // must be overridden
    public virtual string Describe() { return "shape with area " + Area(); }
}
public sealed class Circle : Shape
{
    public double R { get; set; }
    public Circle(double r) { R = r; }
    public override double Area() { return Math.PI * R * R; }
    public override string Describe() { return "circle, " + base.Describe(); }
}
```

**Check**

- A class has a single base class, but it can implement many interfaces.
- `abstract` members belong only in `abstract` classes, and every one must be overridden in a concrete subclass.
- Overriding needs `virtual`, `abstract` or `override` on the base member. Otherwise use `new`, which hides the member and does not dispatch polymorphically.

### Interfaces (C# type)

**Syntax**

```csharp
public interface IRepository<T> where T : class
{
    T Find(int id);                     // members are public, with no body
    void Add(T item);
    int Count { get; }
    event EventHandler Changed;
    void Log(string m) { Console.WriteLine(m); }   // C# 8+: default implementation (.NET Core 3.0+ only)
}
public class MemRepo : IRepository<Item> { /* implements every member as public */ }
void IDisposable.Dispose() { }          // explicit implementation: callable only through the interface
```

**Check**

- Every interface member must be implemented `public` (or explicitly). A missing member is CS0535.

### Structs (C# type)

Value types, copied on assignment and stored inline. Use them for small, immutable data (16 bytes or less is a good guide).

**Syntax**

```csharp
public struct Point
{
    public readonly int X, Y;
    public Point(int x, int y) { X = x; Y = y; }   // before C# 11, a constructor must assign every field
    public double Length() { return Math.Sqrt(X * X + Y * Y); }
}
public readonly struct Money { }       // C# 7.2+
public record struct Pos(int X, int Y);   // C# 10+
```

**Check**

- Changing a struct taken from a list or a property changes a copy: `list[0].X = 5` is an error (CS1612).
- Structs cannot inherit (they can implement interfaces). Before C# 10, they have no parameterless constructor of their own.

### Records (C# 9+ type)

Reference types (or `record struct`, C# 10+) with value equality, `ToString`, deconstruction and `with` copies.

**Syntax**

```csharp
public record Person(string Name, int Age);              // positional: init-only properties
public record Employee(string Name, int Age, string Dept) : Person(Name, Age);
var a = new Person("Ann", 30);
var b = a with { Age = 31 };                             // non-destructive copy
bool same = a == new Person("Ann", 30);                  // true: value equality
var (n, age) = a;                                        // deconstruct
```

**Check**

- C# 9+ only. For older compilers, write a class with readonly properties and override `Equals` and `GetHashCode`.

### Generics and constraints (C# 2+ feature)

**Syntax**

```csharp
public class Box<T> { public T Value; }
public static T Max<T>(T a, T b) where T : IComparable<T> { return a.CompareTo(b) >= 0 ? a : b; }
where T : struct            // value type
where T : class             // reference type
where T : new()             // public parameterless constructor (must be last)
where T : BaseClass, IFoo   // base class first, then interfaces
where T : notnull           // C# 8+
where T : unmanaged         // C# 7.3+
where T : Enum / Delegate   // C# 7.3+
interface IProducer<out T> { T Get(); }   interface IConsumer<in T> { void Put(T x); }   // variance (interfaces/delegates)
default(T)   typeof(T)
```

**Check**

- Without a constraint, you cannot call methods on `T` beyond `object`'s, compare with `<`, or do arithmetic. Arithmetic on `T` needs C# 11 generic math, `INumber<T>`.
- `T == null` does not compile for unconstrained T in older versions. Use `EqualityComparer<T>.Default.Equals(x, default(T))`.

### Delegates and events (C# feature)

**Syntax**

```csharp
public delegate int BinaryOp(int a, int b);            // custom delegate type
BinaryOp add = (a, b) => a + b;   int r = add(2, 3);
Func<int, int, int> mul = (a, b) => a * b;             // built-in: Func<..., TResult>, Action<...>, Predicate<T>
public event EventHandler<MyEventArgs> Done;            // event: subscribers use += and -=
protected virtual void OnDone(MyEventArgs e)
{
    var h = Done;                                       // copy for thread safety (C# 6+: Done?.Invoke(this, e))
    if (h != null) h(this, e);
}
button.Click += (s, e) => Console.WriteLine("clicked");
```

**Check**

- Raising an event with no subscribers throws `NullReferenceException`. Check for null first.
- Unsubscribe from long-lived publishers to avoid memory leaks. A lambda cannot be removed unless it was stored in a variable.

### Operator overloading and conversions (C# feature)

**Syntax**

```csharp
public static Vec operator +(Vec a, Vec b) { return new Vec(a.X + b.X, a.Y + b.Y); }
public static bool operator ==(Vec a, Vec b) { return a.X == b.X && a.Y == b.Y; }
public static bool operator !=(Vec a, Vec b) { return !(a == b); }      // == and != come in pairs
public static implicit operator double(Vec v) { return v.Length; }
public static explicit operator Vec(double d) { return new Vec(d, 0); }
```

**Check**

- When you overload `==`, also override `Equals(object)` and `GetHashCode()`.

### Equality, Equals and GetHashCode (C# feature)

**Syntax**

```csharp
public override bool Equals(object obj)
{
    var o = obj as Point;
    return o != null && X == o.X && Y == o.Y;
}
public override int GetHashCode() { unchecked { return X * 397 ^ Y; } }   // .NET Core: HashCode.Combine(X, Y)
public bool Equals(Point other) { ... }    // implement IEquatable<Point>
```

**Check**

- Equal objects must return equal hash codes. Otherwise `Dictionary` and `HashSet` lose them.
- Do not use mutable fields in `GetHashCode` for objects stored as dictionary keys.

## Collections (System.Collections.Generic)

### List<T> (C# collection)

Growable array. Fast index and append; insert and remove in the middle are O(n).

**Syntax**

```csharp
var list = new List<int>();   var names = new List<string> { "a", "b" };   new List<int>(capacity)
list.Add(x);  list.AddRange(items);  list.Insert(i, x);  list.Remove(x) /*bool*/;  list.RemoveAt(i);  list.RemoveAll(p => p < 0);  list.Clear();
list[i]  list.Count  list.Contains(x)  list.IndexOf(x)  list.Find(p => p > 3)  list.FindAll(pred)  list.FindIndex(pred)  list.Exists(pred)
list.Sort();  list.Sort((a, b) => a.CompareTo(b));  list.Reverse();  list.ToArray();  list.BinarySearch(x)  list.ForEach(Console.WriteLine);
list.GetRange(index, count)  list.AsReadOnly()  list.TrueForAll(pred)
```

**Check**

- `list[i]` on an index ≥ `Count` throws `ArgumentOutOfRangeException`, even when the capacity is larger.
- `Remove(x)` removes only the first match.

### Dictionary<TKey, TValue> (C# collection)

Hash map from keys to values, with O(1) lookup.

**Syntax**

```csharp
var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
var m = new Dictionary<string, int> { { "a", 1 }, { "b", 2 } };    // C# 6+: { ["a"] = 1 }
d.Add("k", 1);           // throws ArgumentException if the key exists
d["k"] = 2;              // add or replace
int v = d["k"];          // throws KeyNotFoundException if missing
if (d.TryGetValue("k", out v)) { }
d.ContainsKey("k")  d.ContainsValue(1)  d.Remove("k")  d.Count  d.Keys  d.Values  d.Clear()
foreach (KeyValuePair<string, int> kv in d) { kv.Key; kv.Value; }
d.GetValueOrDefault("k")   d.TryAdd("k", 1)     // .NET Core 2.0+ only
```

**Check**

- Use `TryGetValue` rather than `ContainsKey` followed by the indexer (one lookup instead of two).
- The enumeration order is not guaranteed. Sort the keys when the order matters, or use `SortedDictionary`.
- Changing the dictionary while enumerating it throws `InvalidOperationException`.

### HashSet, Queue, Stack, LinkedList, SortedDictionary (C# collections)

**Syntax**

```csharp
var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
set.Add(x) /*false if present*/  set.Contains(x)  set.Remove(x)  set.UnionWith(o)  set.IntersectWith(o)  set.ExceptWith(o)  set.IsSubsetOf(o)
var q = new Queue<int>();   q.Enqueue(1);  int a = q.Dequeue();  q.Peek();  q.Count     // FIFO; Dequeue on empty throws
var st = new Stack<int>();  st.Push(1);  int b = st.Pop();  st.Peek();                   // LIFO
var ll = new LinkedList<int>();  ll.AddFirst(1);  ll.AddLast(2);  ll.Remove(node);
var sd = new SortedDictionary<string, int>();  var sl = new SortedList<int, string>();  var ss = new SortedSet<int>();
var pq = new PriorityQueue<string, int>();  pq.Enqueue("job", 5);  pq.Dequeue();        // .NET 6+
ConcurrentDictionary<K, V>, ConcurrentQueue<T>, BlockingCollection<T>                   // System.Collections.Concurrent: thread-safe
```

**Check**

- `List`, `Dictionary` and the others are not thread-safe for writes. Use a `lock` or the Concurrent collections.

## LINQ (System.Linq)

### LINQ query methods (C# 3+ System.Linq API)

Composable operators over `IEnumerable<T>`. Most of them are deferred: the query runs when it is enumerated.

**Syntax**

```csharp
using System.Linq;
src.Where(x => x.Age > 18)          src.Select(x => x.Name)            src.SelectMany(x => x.Orders)
src.OrderBy(x => x.Last).ThenBy(x => x.First)    src.OrderByDescending(k)   src.Reverse()
src.GroupBy(x => x.Dept)            // IEnumerable<IGrouping<TKey, T>>: g.Key, g.Count(), g.Sum(...)
src.Join(other, a => a.Id, b => b.OwnerId, (a, b) => new { a.Name, b.Total })
src.GroupJoin(...)  src.Zip(o, (a, b) => a + b)  src.Distinct()  src.Union(o)  src.Intersect(o)  src.Except(o)  src.Concat(o)
src.Skip(n)  src.Take(n)  src.SkipWhile(p)  src.TakeWhile(p)  src.Chunk(n) (.NET 6+)  src.DistinctBy(k) (.NET 6+)  src.MaxBy(k) (.NET 6+)
src.First()  src.First(p)  src.FirstOrDefault()  src.Last()  src.Single()  src.SingleOrDefault()  src.ElementAt(i)
src.Any()  src.Any(p)  src.All(p)  src.Contains(x)  src.Count()  src.Count(p)  src.Sum(x => x.Qty)  src.Average(k)  src.Min(k)  src.Max(k)  src.Aggregate((a, b) => a + b)
src.ToList()  src.ToArray()  src.ToDictionary(k => k.Id)  src.ToLookup(k => k.Dept)  src.ToHashSet() (.NET 4.7.2+)  src.Cast<T>()  src.OfType<T>()
Enumerable.Range(1, 10)  Enumerable.Repeat("x", 3)  Enumerable.Empty<T>()  src.DefaultIfEmpty()  src.AsEnumerable()
```

**Example**

```csharp
var top = orders.Where(o => o.Year == 2024)
                .GroupBy(o => o.Customer)
                .Select(g => new { Customer = g.Key, Total = g.Sum(o => o.Amount) })
                .OrderByDescending(x => x.Total)
                .Take(5)
                .ToList();
```

**Check**

- `First()`, `Single()`, `Last()`, `Min()`, `Max()` and `Average()` throw on an empty sequence. Use the `OrDefault` forms, or check `Any()` first.
- `Single()` also throws when more than one element matches.
- `ToDictionary` throws on a duplicate key. Use `GroupBy` or `ToLookup` instead.
- Deferred execution: captured variables are read when the query runs, not when it is written.
- `Count()` on a `List` works, but `.Count` (the property) is direct.

### LINQ query syntax (C# 3+ syntax)

**Syntax**

```csharp
var q = from c in customers
        where c.City == "Paris"
        join o in orders on c.Id equals o.CustomerId      // 'equals', not ==
        let total = o.Qty * o.Price
        orderby total descending, c.Name
        group new { c.Name, total } by c.Name into g
        select new { Name = g.Key, Sum = g.Sum(x => x.total) };
```

**Check**

- A query expression must end with `select` or `group ... by`. `join` uses the `equals` keyword.

## Async and threads

### async and await (C# 5+ feature)

**Syntax**

```csharp
public async Task<string> LoadAsync(string path, CancellationToken ct = default(CancellationToken))
{
    using (var r = new StreamReader(path))
        return await r.ReadToEndAsync();
}
public async Task SaveAsync() { await File.WriteAllTextAsync(p, s); }     // .NET Core 2.0+ API
string text = await LoadAsync("a.txt");
await Task.WhenAll(t1, t2);   Task<int> first = await Task.WhenAny(t1, t2);
await Task.Delay(500, ct);
await Task.Run(() => CpuWork());               // offload CPU-bound work
await something.ConfigureAwait(false);         // library code: do not resume on the captured context
```

**Check**

- An `async` method returns `Task` or `Task<T>`. Use `async void` only for event handlers, because its exceptions crash the process.
- Await every task. A forgotten `await` lets errors vanish, and the code continues before the work is done (warning CS4014).
- Never block on async code with `.Result` or `.Wait()` on a UI or ASP.NET thread (deadlock). Make the caller async.
- `await` is not allowed in a `lock` block, and in C# 5 not in a `catch` or `finally` either.
- Async `Main` is C# 7.1+.

### Tasks, threads and cancellation (C# System.Threading API)

**Syntax**

```csharp
Task t = Task.Run(() => Work());            Task<int> ti = Task.Run(() => Compute());   int r = await ti;
Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));   cts.Cancel();   token.ThrowIfCancellationRequested();
Parallel.For(0, n, i => { ... });            Parallel.ForEach(items, item => { ... });
var t2 = new Thread(() => Work()) { IsBackground = true };   t2.Start();   t2.Join();
Interlocked.Increment(ref counter);   Interlocked.CompareExchange(ref x, newV, expected);
var sem = new SemaphoreSlim(3);   await sem.WaitAsync();   try { } finally { sem.Release(); }
Thread.Sleep(100) (blocks)  vs  await Task.Delay(100) (does not block)
```

**Check**

- `counter++` from several threads loses updates. Use `Interlocked` or `lock`.
- UI controls can only be touched on the UI thread: use `Invoke` / `Dispatcher`, or await from the UI context.

## Files, I/O and common APIs

### Files and directories (C# System.IO API)

**Syntax**

```csharp
using System.IO;
string text = File.ReadAllText(path[, Encoding.UTF8]);   string[] lines = File.ReadAllLines(path);   byte[] bytes = File.ReadAllBytes(path);
File.WriteAllText(path, text[, encoding]);  File.AppendAllText(path, text);  File.WriteAllLines(path, lines);  File.WriteAllBytes(path, bytes);
foreach (string line in File.ReadLines(path)) { }        // streams lazily: big files
File.Exists(p)  File.Copy(src, dst[, overwrite])  File.Move(src, dst)  File.Delete(p)  File.GetLastWriteTimeUtc(p)
Directory.Exists(d)  Directory.CreateDirectory(d) /* also creates parents, no error if it exists */  Directory.Delete(d, recursive: true)
Directory.GetFiles(d, "*.txt", SearchOption.AllDirectories)   Directory.EnumerateFiles(d, "*.cs")   Directory.GetDirectories(d)
Path.Combine(a, b, c)  Path.GetFileName(p)  Path.GetFileNameWithoutExtension(p)  Path.GetExtension(p)  Path.GetDirectoryName(p)
Path.GetFullPath(p)  Path.ChangeExtension(p, ".bak")  Path.GetTempPath()  Path.GetTempFileName()  Path.GetInvalidFileNameChars()
using (var sw = new StreamWriter(path, append: false, encoding: Encoding.UTF8)) sw.WriteLine("x");
using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { }
var fi = new FileInfo(p); fi.Length; fi.LastWriteTime;   var di = new DirectoryInfo(d); di.GetFiles();
```

**Check**

- Relative paths resolve against `Environment.CurrentDirectory`, not the program folder. Use `AppDomain.CurrentDomain.BaseDirectory` or `AppContext.BaseDirectory`.
- Build paths with `Path.Combine`, not string concatenation.
- `File.WriteAllText` with no encoding writes UTF-8 without a BOM. `Encoding.UTF8` writes a BOM.
- `File.Move(src, dst, overwrite)` exists only in .NET Core 3.0+. On .NET Framework, delete the target first.

### Console I/O (C# System.Console API)

**Syntax**

```csharp
Console.WriteLine("x = {0}, y = {1:F2}", x, y);   Console.Write("no newline");   Console.Error.WriteLine("err");
string line = Console.ReadLine();       // null at end of input
ConsoleKeyInfo k = Console.ReadKey(true);   Console.ForegroundColor = ConsoleColor.Red;  Console.ResetColor();  Console.Clear();
Environment.Exit(code);   Environment.GetCommandLineArgs();   Environment.GetEnvironmentVariable("PATH");   Environment.NewLine
```

### Math and numbers (C# System.Math API)

**Syntax**

```csharp
Math.Abs(x)  Math.Max(a, b)  Math.Min(a, b)  Math.Pow(x, y)  Math.Sqrt(x)  Math.Floor(x)  Math.Ceiling(x)  Math.Truncate(x)
Math.Round(x, 2[, MidpointRounding.AwayFromZero])  Math.Sign(x)  Math.Log(x)  Math.Log10(x)  Math.Exp(x)  Math.Sin(r)  Math.PI  Math.E
Math.Clamp(v, lo, hi) (.NET Core 2.0+)   int.MaxValue  double.NaN  double.IsNaN(d)  double.Epsilon  decimal.Round(m, 2)
var rnd = new Random();  rnd.Next(1, 7) /* 1..6, max exclusive */  rnd.NextDouble()   Random.Shared (.NET 6+)
```

**Check**

- `Math.Round(2.5)` returns 2 (banker's rounding). Pass `MidpointRounding.AwayFromZero` to get 3.
- `new Random()` in a tight loop gives repeated sequences on .NET Framework. Create one instance and reuse it.
- `rnd.Next(1, 6)` never returns 6.

### Dates and times (C# System.DateTime API)

**Syntax**

```csharp
DateTime now = DateTime.Now;  DateTime utc = DateTime.UtcNow;  DateTime today = DateTime.Today;
var d = new DateTime(2024, 1, 31, 13, 45, 0);   d.AddDays(7)  d.AddMonths(-1)  d.AddHours(2)   d.Date  d.Year  d.DayOfWeek  d.DayOfYear
TimeSpan span = end - start;  span.TotalMinutes  span.Days   TimeSpan.FromSeconds(90)
d.ToString("yyyy-MM-dd HH:mm:ss")   // MM = month, mm = minute; HH = 24h, hh = 12h; tt = AM/PM
DateTime.ParseExact("31/01/2024", "dd/MM/yyyy", CultureInfo.InvariantCulture)   DateTime.TryParse(s, out d)
DateTimeOffset.Now   DateOnly / TimeOnly (.NET 6+)   Stopwatch sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds   // System.Diagnostics
```

**Check**

- `MM` means month and `mm` means minutes. Mixing them up is a classic bug.
- Store and compare times in UTC, and convert to local time only for display.
- `DateTime` is immutable: `d.AddDays(1);` alone does nothing. Assign the result.

### Regular expressions (C# System.Text.RegularExpressions API)

**Syntax**

```csharp
using System.Text.RegularExpressions;
bool ok = Regex.IsMatch(s, @"^\d{3}-\d{4}$");
Match m = Regex.Match(s, @"(?<year>\d{4})-(?<mon>\d{2})");  if (m.Success) { string y = m.Groups["year"].Value; }
foreach (Match x in Regex.Matches(s, @"\w+")) { }
string r = Regex.Replace(s, @"\s+", " ");   string[] parts = Regex.Split(s, @"[,;]\s*");
var re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline, TimeSpan.FromSeconds(1));
Regex.Escape(userText)
```

**Check**

- Use verbatim strings `@"\d"` for patterns. In `"\d"`, `\d` is an invalid C# escape (CS1009).
- Escape user input with `Regex.Escape`, and set a timeout for untrusted patterns.

### JSON and XML (C# APIs)

**Syntax**

```csharp
// .NET Core 3.0+ / .NET 5+: System.Text.Json
string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
var person = JsonSerializer.Deserialize<Person>(json);
// .NET Framework: System.Web.Script.Serialization.JavaScriptSerializer (System.Web.Extensions.dll) or DataContractJsonSerializer
// XML: System.Xml.Linq
var doc = XDocument.Load(path);  foreach (var e in doc.Descendants("item")) { string id = (string)e.Attribute("id"); }
var x = new XElement("root", new XElement("child", "text"));  x.Save(path);
```

**Check**

- By default, System.Text.Json matches property names case-sensitively. Set `PropertyNameCaseInsensitive = true` when the JSON uses camelCase.
- System.Text.Json does not ship with .NET Framework.

### HTTP requests (C# System.Net.Http API)

**Syntax**

```csharp
private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };   // one shared instance
string body = await Http.GetStringAsync(url);
using (var resp = await Http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json")))
{
    resp.EnsureSuccessStatusCode();
    string result = await resp.Content.ReadAsStringAsync();
}
```

**Check**

- Do not create a new `HttpClient` per request (socket exhaustion). Reuse one instance.
- `EnsureSuccessStatusCode()` throws on 4xx and 5xx. Check `resp.IsSuccessStatusCode` to handle them yourself.

## Keywords

### Reserved and contextual keywords (C# keywords)

**Reserved** (cannot be identifiers unless prefixed with `@`, as in `@class`): abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while.

**Contextual** (keywords only in certain places): add allows and alias args ascending async await by descending dynamic equals extension field file from get global group init into join let managed nameof nint not notnull nuint on or orderby partial record remove required scoped select set unmanaged value var when where with yield.

**Check**

- `@` lets you use a keyword as a name (`int @event = 1;`). Avoid it unless you must interoperate.
