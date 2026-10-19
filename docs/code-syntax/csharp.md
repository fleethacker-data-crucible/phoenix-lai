# C# reference (Phoenix)

For Phoenix models writing C#. The default target is **C# 5 on .NET Framework 4.8** (csc.exe, CodeDom, `Add-Type` in Windows PowerShell 5.1): the only C# compiler on a Windows machine without Visual Studio or the .NET SDK. C# 5 code also compiles on every newer compiler, so it is the safe choice whenever the request names no version. Recipes are complete programs that compile with csc.exe.

## Essentials

Target: C# 5, .NET Framework 4.8, unless the user names a newer C# or .NET (Core, 5+).

| Never in C# 5 | Write instead |
|---|---|
| `$"Hi {name}"` | `string.Format("Hi {0}", name)` or `"Hi " + name` |
| `x?.Length`, `x?[0]` | `x == null ? 0 : x.Length` |
| `a ?? throw ...`, `??=` | `if (a == null) throw ...;` / `if (x == null) x = v;` |
| `nameof(x)` | `"x"` |
| `int F() => 1;`, `int P => 1;` | `int F() { return 1; }`, `int P { get { return 1; } }` |
| `{ get; }`, `{ get; set; } = 5;` | `{ get; private set; }`, set it in the constructor |
| `int.TryParse(s, out var n)`, `out int n` in a call | `int n; int.TryParse(s, out n)` |
| `x is Foo f`, `is null`, `switch` expressions | `Foo f = x as Foo; if (f != null)`, `== null`, `switch` statement |
| tuples `(int, string)`, `return (a, b);` | `Tuple<int, string>`, `Tuple.Create(a, b)`, or a small class |
| `static async Task Main` | `static void Main() { RunAsync().GetAwaiter().GetResult(); }` |
| `File.ReadAllTextAsync`, `WriteAllLinesAsync` | `File.ReadAllText` (or a `StreamReader` with `ReadToEndAsync()`) |
| `using var x = ...;`, `new()`, records, `init`, `namespace X;` | `using (var x = ...) { }`, `new List<int>()`, a class, `set`, `namespace X { }` |
| `string?`, `Span<T>`, `^1`, `a..b`, `1_000`, `0b101` | `string`, arrays, `list[list.Count - 1]`, `Substring` / `GetRange`, `1000`, `5` |

Always: every type has its `using` (`System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Text`, `System.Threading.Tasks`, `System.Text.RegularExpressions`). One complete file, a class with `static void Main(string[] args)`. Dispose files and streams with `using (...) { }`. Money is `decimal`. Compare strings with `StringComparison.OrdinalIgnoreCase`, not `ToLower()`.

## Recipes

### Read a text file line by line and count or filter lines (StreamReader, File.ReadLines)

**Example**

```csharp
using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "log.txt";
        int errors = 0;
        // File.ReadLines streams the file one line at a time (fine for huge files); File.ReadAllLines loads it all.
        foreach (string line in File.ReadLines(path))
        {
            if (line.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0) errors++;
        }
        Console.WriteLine("{0} lines contain ERROR", errors);
    }
}
```

- `string.Contains` has no StringComparison overload in .NET Framework: use `IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0`.

### Count word frequencies and print the most common (Dictionary, LINQ OrderByDescending, Take)

**Example**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        string text = File.ReadAllText(args.Length > 0 ? args[0] : "input.txt");
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(text, @"[A-Za-z']+"))
        {
            int n;
            counts.TryGetValue(m.Value, out n);   // n is 0 when the word is new
            counts[m.Value] = n + 1;
        }
        foreach (KeyValuePair<string, int> kv in counts.OrderByDescending(p => p.Value).ThenBy(p => p.Key).Take(10))
            Console.WriteLine("{0,-15} {1}", kv.Key, kv.Value);
    }
}
```

### List files recursively, filter by size or extension (Directory.EnumerateFiles, FileInfo)

**Example**

```csharp
using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : ".";
        const long limit = 100L * 1024 * 1024;   // long: 100 * 1024 * 1024 * 21 would overflow int
        foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            FileInfo fi = new FileInfo(file);
            if (fi.Length > limit) Console.WriteLine("{0,10:N1} MB  {1}", fi.Length / 1048576.0, fi.FullName);
        }
    }
}
```

- An unreadable folder throws `UnauthorizedAccessException` out of the whole enumeration: for system folders walk the tree yourself with a `try` / `catch` per folder.

### Hash a file (SHA-256 to lowercase hex)

**Example**

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

class Program
{
    static void Main(string[] args)
    {
        using (FileStream fs = File.OpenRead(args[0]))
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(fs);
            StringBuilder sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            Console.WriteLine(sb.ToString());
        }
    }
}
```

- `Convert.ToHexString` does not exist in .NET Framework; `BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()` also works.

### Parse a CSV line with quoted fields (state machine, StringBuilder)

**Example**

```csharp
using System;
using System.Collections.Generic;
using System.Text;

class Program
{
    public static List<string> SplitCsvLine(string line)
    {
        List<string> fields = new List<string>();
        StringBuilder cur = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }   // "" inside quotes
                else if (c == '"') quoted = false;
                else cur.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { fields.Add(cur.ToString()); cur.Length = 0; }
            else cur.Append(c);
        }
        fields.Add(cur.ToString());
        return fields;
    }

    static void Main()
    {
        foreach (string f in SplitCsvLine("1,\"Smith, John\",\"say \"\"hi\"\"\"")) Console.WriteLine("[" + f + "]");
    }
}
```

### Group, sum and sort with LINQ (GroupBy, Sum, OrderByDescending)

**Example**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

class Order
{
    public string Customer { get; set; }
    public decimal Amount { get; set; }
    public Order(string customer, decimal amount) { Customer = customer; Amount = amount; }
}

class Program
{
    static void Main()
    {
        List<Order> orders = new List<Order> { new Order("Ann", 10.5m), new Order("Bob", 3m), new Order("Ann", 7m) };
        var totals = orders.GroupBy(o => o.Customer)
                           .Select(g => new { Customer = g.Key, Total = g.Sum(o => o.Amount) })
                           .OrderByDescending(t => t.Total);
        foreach (var t in totals) Console.WriteLine("{0}: {1:C}", t.Customer, t.Total);
    }
}
```

- Auto-properties need both accessors in C# 5 (`{ get; set; }` or `{ get; private set; }`) and no initializer: set values in the constructor.

### A thread-safe cache or shared counter (lock, ConcurrentDictionary)

**Example**

```csharp
using System;
using System.Collections.Generic;

class LruCache<TKey, TValue>
{
    readonly int _capacity;
    readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>();
    readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new LinkedList<KeyValuePair<TKey, TValue>>();
    readonly object _lock = new object();

    public LruCache(int capacity) { _capacity = capacity; }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> node;
            if (!_map.TryGetValue(key, out node)) { value = default(TValue); return false; }
            _order.Remove(node); _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Put(TKey key, TValue value)
    {
        lock (_lock)
        {
            LinkedListNode<KeyValuePair<TKey, TValue>> old;
            if (_map.TryGetValue(key, out old)) { _order.Remove(old); _map.Remove(key); }
            else if (_map.Count >= _capacity) { _map.Remove(_order.Last.Value.Key); _order.RemoveLast(); }
            _map[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
        }
    }
}

class Program
{
    static void Main()
    {
        LruCache<string, int> c = new LruCache<string, int>(2);
        c.Put("a", 1); c.Put("b", 2); c.Put("c", 3);
        int v;
        Console.WriteLine(c.TryGet("a", out v) ? "a found" : "a evicted");
    }
}
```

- `default(TValue)` with the type: the bare `default` literal is C# 7.1.

### Run async code from Main, read files concurrently (async/await, Task.WhenAll)

**Example**

```csharp
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    static void Main(string[] args)
    {
        // C# 5 has no async Main: block once at the top.
        RunAsync(args).GetAwaiter().GetResult();
    }

    static async Task RunAsync(string[] args)
    {
        Task<string> a = ReadAsync(args[0]);
        Task<string> b = ReadAsync(args[1]);
        string[] both = await Task.WhenAll(a, b);
        Console.WriteLine("Combined length: {0}", both[0].Length + both[1].Length);
    }

    static async Task<string> ReadAsync(string path)
    {
        using (StreamReader r = new StreamReader(path))   // File.ReadAllTextAsync does not exist in .NET Framework
            return await r.ReadToEndAsync();
    }
}
```

### Serialize objects to XML and back (XmlSerializer)

**Example**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

public class Person   // XmlSerializer needs a public class with a public parameterless constructor
{
    public string Name { get; set; }
    public int Age { get; set; }
    public List<string> Emails { get; set; }
    public Person() { Emails = new List<string>(); }
}

class Program
{
    static void Main()
    {
        Person p = new Person();
        p.Name = "Ann"; p.Age = 30; p.Emails.Add("ann@example.com");
        XmlSerializer xs = new XmlSerializer(typeof(Person));
        using (StreamWriter w = new StreamWriter("person.xml")) xs.Serialize(w, p);
        Person back;
        using (StreamReader r = new StreamReader("person.xml")) back = (Person)xs.Deserialize(r);
        Console.WriteLine("{0}, {1}, {2}", back.Name, back.Age, back.Emails[0]);
    }
}
```

- XmlSerializer lives in System.Xml.dll: compile with `/r:System.Xml.dll`. There is no System.Text.Json in .NET Framework; for JSON use `System.Web.Script.Serialization.JavaScriptSerializer` (System.Web.Extensions.dll) or `DataContractJsonSerializer` (System.Runtime.Serialization.dll).

### Watch a folder for changes (FileSystemWatcher)

**Example**

```csharp
using System;
using System.IO;

class Program
{
    static void Main(string[] args)
    {
        using (FileSystemWatcher w = new FileSystemWatcher(args.Length > 0 ? args[0] : "."))
        {
            w.IncludeSubdirectories = true;
            w.Created += OnChanged;
            w.Changed += OnChanged;
            w.Deleted += OnChanged;
            w.Renamed += delegate(object s, RenamedEventArgs e) { Console.WriteLine("Renamed: {0} -> {1}", e.OldFullPath, e.FullPath); };
            w.EnableRaisingEvents = true;
            Console.WriteLine("Watching. Press Enter to stop.");
            Console.ReadLine();
        }
    }

    static void OnChanged(object sender, FileSystemEventArgs e)
    {
        Console.WriteLine("{0}: {1}", e.ChangeType, e.FullPath);
    }
}
```

### A generic binary search tree (generics, IComparable constraint, recursion)

**Example**

```csharp
using System;
using System.Collections.Generic;

class Bst<T> where T : IComparable<T>
{
    class Node
    {
        public T Value; public Node Left, Right;
        public Node(T value) { Value = value; }
    }
    Node _root;

    public void Insert(T value) { _root = Insert(_root, value); }
    static Node Insert(Node n, T value)
    {
        if (n == null) return new Node(value);
        if (value.CompareTo(n.Value) < 0) n.Left = Insert(n.Left, value); else n.Right = Insert(n.Right, value);
        return n;
    }

    public bool Contains(T value)
    {
        Node n = _root;
        while (n != null)
        {
            int c = value.CompareTo(n.Value);
            if (c == 0) return true;
            n = c < 0 ? n.Left : n.Right;
        }
        return false;
    }

    public IEnumerable<T> InOrder() { return Walk(_root); }
    static IEnumerable<T> Walk(Node n)
    {
        if (n == null) yield break;
        foreach (T v in Walk(n.Left)) yield return v;
        yield return n.Value;
        foreach (T v in Walk(n.Right)) yield return v;
    }
}

class Program
{
    static void Main()
    {
        Bst<int> t = new Bst<int>();
        foreach (int v in new[] { 5, 2, 8, 1 }) t.Insert(v);
        Console.WriteLine(string.Join(", ", t.InOrder()));
        Console.WriteLine(t.Contains(8));
    }
}
```

- Each local variable name may be declared only once per method scope chain: reusing `node` in a nested block and after it is error CS0136.

### Parse numbers and dates safely (int.TryParse, DateTime.TryParseExact, CultureInfo)

**Example**

```csharp
using System;
using System.Globalization;

class Program
{
    static void Main(string[] args)
    {
        int n;
        if (int.TryParse("42", out n)) Console.WriteLine(n + 1);
        decimal price;
        if (decimal.TryParse("19.99", NumberStyles.Number, CultureInfo.InvariantCulture, out price)) Console.WriteLine(price);
        DateTime d;
        if (DateTime.TryParseExact("2024-01-31", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            Console.WriteLine(d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
    }
}
```

### Return more than one value (out parameters, Tuple, a small class)

**Example**

```csharp
using System;

class Program
{
    static void MinMax(int[] values, out int min, out int max)
    {
        min = int.MaxValue; max = int.MinValue;
        foreach (int v in values) { if (v < min) min = v; if (v > max) max = v; }
    }

    static Tuple<int, int> MinMaxTuple(int[] values)
    {
        int min, max;
        MinMax(values, out min, out max);
        return Tuple.Create(min, max);   // read with .Item1 / .Item2
    }

    static void Main()
    {
        Tuple<int, int> r = MinMaxTuple(new[] { 4, 9, 1 });
        Console.WriteLine("{0}..{1}", r.Item1, r.Item2);
    }
}
```

### Run a regular expression and use its groups (Regex, Match, named groups)

**Example**

```csharp
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        string log = "2024-05-01 ERROR disk full\n2024-05-02 INFO ok";
        Regex re = new Regex(@"^(?<date>\d{4}-\d{2}-\d{2}) (?<level>\w+) (?<msg>.*)$", RegexOptions.Multiline);
        foreach (Match m in re.Matches(log))
            if (m.Groups["level"].Value == "ERROR") Console.WriteLine("{0}: {1}", m.Groups["date"].Value, m.Groups["msg"].Value);
        Console.WriteLine(Regex.Replace("a1b22c333", @"\d+", "#"));
    }
}
```

### Handle errors and clean up (try / catch / finally, specific exceptions, rethrow)

**Example**

```csharp
using System;
using System.IO;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            string text = File.ReadAllText(args.Length > 0 ? args[0] : "missing.txt");
            Console.WriteLine(text.Length);
            return 0;
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine("Not found: " + ex.FileName);
            return 2;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("I/O error: " + ex.Message);
            return 1;
        }
    }
}
```

- Rethrow with `throw;` (keeps the stack trace), never `throw ex;`. An exception filter (`catch (X e) when (...)`) is C# 6: test inside the catch and `throw;`.

### Query rows from a database (ADO.NET: SqlConnection, parameters, SqlDataReader)

**Example**

```csharp
using System;
using System.Data.SqlClient;

class Program
{
    static void Main()
    {
        string cs = "Server=.;Database=Shop;Integrated Security=true";
        using (SqlConnection con = new SqlConnection(cs))
        using (SqlCommand cmd = new SqlCommand("SELECT Id, Name FROM Customers WHERE City = @city", con))
        {
            cmd.Parameters.AddWithValue("@city", "Oslo");   // parameters, never string concatenation (SQL injection)
            con.Open();
            using (SqlDataReader r = cmd.ExecuteReader())
                while (r.Read()) Console.WriteLine("{0} {1}", r.GetInt32(0), r.GetString(1));
        }
    }
}
```

- Compile with `/r:System.Data.dll`.

### Build strings efficiently and format numbers (StringBuilder, string.Format, string.Join)

**Example**

```csharp
using System;
using System.Globalization;
using System.Text;

class Program
{
    static void Main()
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 1; i <= 3; i++) sb.AppendFormat("Row {0}: {1,8:N2}", i, i * 1234.5).AppendLine();
        Console.Write(sb.ToString());
        Console.WriteLine(string.Join(", ", new[] { "a", "b", "c" }));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1:yyyy-MM-dd} {2:P1}", 3.14159, DateTime.Today, 0.256));
    }
}
```

## Reference

### Program structure, Main, namespaces, using directives (C# 5)

```csharp
using System;                 // using directives first, one per namespace
using System.Collections.Generic;

namespace MyApp               // block namespace: { } around the types
{
    static class Program
    {
        static int Main(string[] args)   // or static void Main() / Main(string[] args)
        {
            return 0;         // exit code
        }
    }
}
```

**Check**

- Exactly one `Main`. Every statement is inside a method, every method inside a class.
- Missing `using` = error CS0246 "type or namespace could not be found".

### Variables, types, constants and conversions (C# 5)

```csharp
int n = 5; long big = 5000000000L; double d = 1.5; decimal money = 9.99m; bool ok = true; char c = 'x'; string s = "text";
var list = new List<string>();     // var: the type comes from the right side
const int Max = 10; static readonly DateTime Start = DateTime.Now;
int i = (int)3.9;                  // explicit cast truncates to 3
int parsed = int.Parse("42");      // throws on bad input; int.TryParse(s, out n) does not
string t = n.ToString(); double q = Convert.ToDouble("1.5", System.Globalization.CultureInfo.InvariantCulture);
int? maybe = null;                 // nullable value type; maybe.HasValue, maybe.Value, maybe ?? 0
```

**Check**

- Integer division truncates (`5 / 2 == 2`): use `5 / 2.0`. `int * int` overflows silently: use `long` for sizes.
- `??` (null-coalescing) is fine in C# 5; `?.` is not.

### Strings (C# 5: literals, verbatim, methods, comparison)

```csharp
string path = @"C:\Data\file.txt";          // verbatim: backslashes are literal, "" is a quote
string msg = string.Format("{0} has {1:N0} items", name, count);
bool same = string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
bool has = text.IndexOf("err", StringComparison.OrdinalIgnoreCase) >= 0;
string[] parts = line.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
string trimmed = s.Trim(); string upper = s.ToUpperInvariant(); bool empty = string.IsNullOrWhiteSpace(s);
string padded = id.ToString().PadLeft(6, '0'); string sub = s.Substring(start, length);
```

**Check**

- No `$"..."` (C# 6). `Split(',')` works; `Split(", ")` with a string needs the `string[]` overload: `s.Split(new[] { ", " }, StringSplitOptions.None)`.
- `string.Contains(x, StringComparison)` and `string.Split(char, options)` do not exist in .NET Framework.

### Collections: List, Dictionary, HashSet, Queue, Stack, arrays (C# 5)

```csharp
List<int> nums = new List<int> { 3, 1, 2 }; nums.Add(4); nums.Sort(); int first = nums[0]; nums.RemoveAt(0);
Dictionary<string, int> ages = new Dictionary<string, int>();
ages["ann"] = 30;                          // add or overwrite
int age; if (ages.TryGetValue("bob", out age)) Console.WriteLine(age);
foreach (KeyValuePair<string, int> kv in ages) Console.WriteLine(kv.Key + "=" + kv.Value);
HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); bool added = seen.Add("x");
Queue<int> q = new Queue<int>(); q.Enqueue(1); int head = q.Dequeue();
int[] arr = new int[10]; int[] lit = { 1, 2, 3 }; int len = arr.Length; Array.Sort(arr);
```

**Check**

- `dict[key]` throws `KeyNotFoundException` for a missing key: use `TryGetValue`. `Dictionary.GetValueOrDefault` does not exist in .NET Framework 4.8.
- Never add to or remove from a collection inside `foreach` over it.

### LINQ (System.Linq, C# 5)

```csharp
using System.Linq;
var adults = people.Where(p => p.Age >= 18).OrderBy(p => p.Name).Select(p => p.Name).ToList();
int total = items.Sum(i => i.Qty); double avg = items.Average(i => i.Price); bool any = items.Any(i => i.Qty == 0);
var byCity = people.GroupBy(p => p.City).Select(g => new { City = g.Key, Count = g.Count() });
var top3 = scores.OrderByDescending(s => s).Take(3); var firstOrNull = people.FirstOrDefault(p => p.Id == 7);
Dictionary<int, Person> byId = people.ToDictionary(p => p.Id);
```

**Check**

- Needs `using System.Linq;`. `MaxBy`, `MinBy`, `DistinctBy`, `Chunk`, `TakeLast` (before .NET Core) do not exist in .NET Framework 4.8: use `OrderByDescending(...).First()`, `GroupBy(...).Select(g => g.First())`.
- `FirstOrDefault` returns `null` (or `0`): check before use.

### Classes, properties, constructors, inheritance, interfaces (C# 5)

```csharp
public interface IShape { double Area(); }
public abstract class Shape : IShape
{
    public string Name { get; private set; }          // C# 5 auto-property: both accessors
    protected Shape(string name) { Name = name; }
    public abstract double Area();
    public override string ToString() { return Name + ": " + Area().ToString("F2"); }
}
public class Circle : Shape
{
    readonly double _r;
    public Circle(double r) : base("circle") { _r = r; }
    public override double Area() { return Math.PI * _r * _r; }
    public double Radius { get { return _r; } }       // read-only computed property
}
```

**Check**

- No `=>` bodies, no `{ get; }` alone, no property initializers (all C# 6).
- `static` classes cannot be instantiated; `sealed` cannot be inherited.

### Methods, parameters, ref/out/params, optional and named arguments (C# 5)

```csharp
static int Add(int a, int b = 0) { return a + b; }        // optional parameter
static void Swap(ref int a, ref int b) { int t = a; a = b; b = t; }
static bool TryHalf(int n, out int half) { half = n / 2; return n % 2 == 0; }
static int Sum(params int[] xs) { int s = 0; foreach (int x in xs) s += x; return s; }
int r = Add(b: 2, a: 1);                                   // named arguments
int h; bool even = TryHalf(10, out h);                     // declare the out variable first
```

### Control flow: if, switch, loops, break/continue (C# 5)

```csharp
if (x > 0) { } else if (x < 0) { } else { }
switch (cmd)                       // switch statement: every section ends with break / return / throw
{
    case "start": Start(); break;
    case "stop":
    case "halt": Stop(); break;
    default: Console.WriteLine("unknown"); break;
}
for (int i = 0; i < n; i++) { if (i % 2 == 0) continue; if (i > 7) break; }
foreach (string s in names) { } while (queue.Count > 0) { } do { } while (again);
string kind = n > 0 ? "positive" : "not positive";   // conditional operator
```

**Check**

- No fall-through: a non-empty `case` without `break` is error CS0163. No `case int n when ...` patterns, no `x switch { }` (C# 7/8).

### Exceptions and using (C# 5)

```csharp
try { Risky(); }
catch (FileNotFoundException ex) { Console.WriteLine(ex.FileName); }
catch (Exception ex) { Log(ex); throw; }        // rethrow, keeps the stack
finally { Cleanup(); }
using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false))) { w.WriteLine("x"); }   // UTF-8 without BOM
if (arg == null) throw new ArgumentNullException("arg");
```

### Files, paths and directories (System.IO, .NET Framework 4.8)

```csharp
string all = File.ReadAllText(path); string[] lines = File.ReadAllLines(path); byte[] data = File.ReadAllBytes(path);
File.WriteAllText(path, text); File.AppendAllText(path, line + Environment.NewLine); File.WriteAllLines(path, lines);
bool exists = File.Exists(path); File.Copy(src, dst, true); File.Move(src, dst); File.Delete(path);
Directory.CreateDirectory(dir); string[] files = Directory.GetFiles(dir, "*.log", SearchOption.AllDirectories);
string full = Path.Combine(dir, "sub", "a.txt"); string name = Path.GetFileName(full); string ext = Path.GetExtension(full);
string noExt = Path.GetFileNameWithoutExtension(full); string tmp = Path.GetTempFileName();
```

**Check**

- No `*Async` methods on `File` in .NET Framework (`ReadAllTextAsync`, `WriteAllTextAsync`, `ReadAllLinesAsync`): use the synchronous ones or a `StreamReader` / `StreamWriter` with `ReadToEndAsync` / `WriteAsync`.
- `File.Move(src, dst, true)` (overwrite) does not exist in .NET Framework: delete the target first.

### Tasks and threads (System.Threading, System.Threading.Tasks, C# 5)

```csharp
Task<int> t = Task.Run(() => Compute());             // background work
int result = await t;                                  // inside an async method
await Task.WhenAll(t1, t2); await Task.Delay(500);
Parallel.ForEach(files, f => Process(f));              // CPU-bound loop
CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
```

**Check**

- `async` methods return `Task` or `Task<T>` (`async void` only for event handlers). `ValueTask` is not in .NET Framework.
- Never `.Result` / `.Wait()` inside UI or ASP.NET code (deadlock); fine once in `Main` via `GetAwaiter().GetResult()`.

### Dates and times (DateTime, TimeSpan, formatting)

```csharp
DateTime now = DateTime.Now, utc = DateTime.UtcNow, today = DateTime.Today;
DateTime due = now.AddDays(30); TimeSpan age = now - start; double hours = age.TotalHours;
string iso = now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
DateTime firstOfMonth = new DateTime(now.Year, now.Month, 1);
```

**Check**

- `MM` = month, `mm` = minutes, `HH` = 24-hour. `DateOnly` / `TimeOnly` do not exist in .NET Framework.

## Newer than C# 5 (not available on .NET Framework's compiler)

Use these only when the user names C# 6+ / .NET Core / .NET 5+. Each line: the feature (version) and the C# 5 way.

- String interpolation `$"..."` (6) → `string.Format`. Null-conditional `?.` / `?[]` (6) → explicit null test. `nameof` (6) → string literal.
- Expression-bodied members `=>` (6/7) → normal bodies. Auto-property initializers and get-only `{ get; }` (6) → constructor + `private set`.
- `using static` (6) → qualify with the type. Exception filters `when` (6) → test inside `catch`, `throw;`.
- `out var` / inline `out T x` (7), discards `_` (7) → declare first. Tuples `(a, b)` and deconstruction (7) → `Tuple<>`, out params.
- Pattern matching `is T x`, `case T x when` (7), `is null` (7), `is not` (9) → `as` + null test, `== null`.
- Local functions (7) → private static methods. `default` literal (7.1) → `default(T)`. `async Task Main` (7.1) → `GetAwaiter().GetResult()`.
- `Span<T>`, `stackalloc` in safe code (7.2) → arrays. Throw expressions (7) → `if` + `throw`.
- Switch expressions (8), ranges `a..b` and indices `^1` (8), `using var` (8), `??=` (8), nullable reference types `string?` (8), default interface members (8).
- Records, `init`, target-typed `new()`, top-level statements (9). File-scoped `namespace X;`, `global using` (10). Raw strings `"""` (11), `required` (11), collection expressions `[1, 2]` (12), primary constructors (12).
