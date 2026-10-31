# Coding tasks for coding-eval.ps1: each has an id, a language (csharp = C# 5 static method compiled by Add-Type in
# Windows PowerShell 5.1; powershell = a PowerShell 5.1 function), the prompt and a test. A C# test calls the method through
# $call (a scriptblock: & $call 'Name' @(args)); a PowerShell test calls the function directly. A test throws on failure.
@(
  # ---------------- C# 5 ----------------
  @{ id = 'cs-palindrome'; lang = 'csharp'
     prompt = 'Write a C# static method `bool IsPalindrome(string s)` that returns true when s reads the same forwards and backwards, comparing only letters and digits and ignoring case. null returns false; an empty string returns true.'
     test = { if (-not (& $call 'IsPalindrome' @('A man, a plan, a canal: Panama'))) { throw 'panama' }; if (& $call 'IsPalindrome' @('race a car')) { throw 'race' }; if (-not (& $call 'IsPalindrome' @(''))) { throw 'empty' }; if (& $call 'IsPalindrome' @($null)) { throw 'null' }; if (-not (& $call 'IsPalindrome' @('1 a2A 1'))) { throw 'digits' } } }
  @{ id = 'cs-fizzbuzz'; lang = 'csharp'
     prompt = 'Write a C# static method `string[] FizzBuzz(int n)` returning the FizzBuzz sequence for 1..n: "Fizz" for multiples of 3, "Buzz" for multiples of 5, "FizzBuzz" for both, otherwise the number as text. n <= 0 returns an empty array.'
     test = { $r = & $call 'FizzBuzz' @(15); if ($r.Length -ne 15 -or $r[2] -ne 'Fizz' -or $r[4] -ne 'Buzz' -or $r[14] -ne 'FizzBuzz' -or $r[0] -ne '1') { throw 'seq' }; if ((& $call 'FizzBuzz' @(0)).Length -ne 0) { throw 'zero' } } }
  @{ id = 'cs-csv'; lang = 'csharp'
     prompt = 'Write a C# static method `string[] ParseCsvLine(string line)` that splits one CSV line on commas. Fields may be wrapped in double quotes; inside quotes commas are literal and "" is an escaped quote. Return the field values without the surrounding quotes. An empty line returns one empty field.'
     test = { $r = @(& $call 'ParseCsvLine' @('a,"b,c",d')); if (($r -join '|') -ne 'a|b,c|d') { throw 'basic' }; $r = @(& $call 'ParseCsvLine' @('"say ""hi""",,x')); if (($r -join '|') -ne 'say "hi"||x') { throw 'escape' }; $r = @(& $call 'ParseCsvLine' @('')); if ($r.Length -ne 1 -or $r[0] -ne '') { throw 'empty' }; $r = @(& $call 'ParseCsvLine' @('a,')); if ($r.Length -ne 2) { throw 'trailing' } } }
  @{ id = 'cs-roman-to-int'; lang = 'csharp'
     prompt = 'Write a C# static method `int RomanToInt(string s)` converting a valid Roman numeral (I, V, X, L, C, D, M with subtractive forms like IV, IX, XL, XC, CD, CM) to an integer.'
     test = { foreach ($p in @(@('III', 3), @('LVIII', 58), @('MCMXCIV', 1994), @('MMMCMXCIX', 3999), @('XLIV', 44))) { if ((& $call 'RomanToInt' @($p[0])) -ne $p[1]) { throw $p[0] } } } }
  @{ id = 'cs-int-to-roman'; lang = 'csharp'
     prompt = 'Write a C# static method `string IntToRoman(int n)` converting 1..3999 to a Roman numeral using subtractive notation (4 = IV, 9 = IX, 40 = XL, 90 = XC, 400 = CD, 900 = CM).'
     test = { foreach ($p in @(@(3, 'III'), @(58, 'LVIII'), @(1994, 'MCMXCIV'), @(3999, 'MMMCMXCIX'), @(444, 'CDXLIV'))) { if ((& $call 'IntToRoman' @($p[0])) -ne $p[1]) { throw "$($p[0])" } } } }
  @{ id = 'cs-merge-intervals'; lang = 'csharp'
     prompt = 'Write a C# static method `int[][] MergeIntervals(int[][] intervals)` where each interval is {start, end} (inclusive). Merge overlapping or touching intervals ({1,3} and {3,5} merge) and return them sorted by start. The input order is arbitrary and may be empty.'
     test = { $in = [int[][]]@([int[]]@(8, 10), [int[]]@(1, 3), [int[]]@(2, 6), [int[]]@(15, 18), [int[]]@(6, 7)); $r = & $call 'MergeIntervals' @(,$in); if ((($r | % { $_ -join '-' }) -join ',') -ne '1-7,8-10,15-18') { throw 'merge' }; $r = & $call 'MergeIntervals' @(,[int[][]]@()); if ($r.Length -ne 0) { throw 'empty' } } }
  @{ id = 'cs-levenshtein'; lang = 'csharp'
     prompt = 'Write a C# static method `int Levenshtein(string a, string b)` returning the edit distance (insertions, deletions, substitutions each cost 1). Treat null as an empty string.'
     test = { foreach ($p in @(@('kitten', 'sitting', 3), @('', 'abc', 3), @('flaw', 'lawn', 2), @('same', 'same', 0), @($null, 'ab', 2))) { if ((& $call 'Levenshtein' @($p[0], $p[1])) -ne $p[2]) { throw "$($p[0])/$($p[1])" } } } }
  @{ id = 'cs-word-freq'; lang = 'csharp'
     prompt = 'Write a C# static method `Dictionary<string, int> WordFrequency(string text)` that counts words case-insensitively. A word is a run of letters, digits or apostrophes; keys are lowercase. null or empty text returns an empty dictionary.'
     test = { $r = & $call 'WordFrequency' @("The cat and the hat. THE end, don't stop!"); if ($r['the'] -ne 3 -or $r["don't"] -ne 1 -or $r['cat'] -ne 1 -or $r.Count -ne 7) { throw 'counts' }; if ((& $call 'WordFrequency' @('')).Count -ne 0) { throw 'empty' } } }
  @{ id = 'cs-brackets'; lang = 'csharp'
     prompt = 'Write a C# static method `bool IsBalanced(string s)` that returns true when every (, [ and { in s is closed by the matching bracket in the right order. Other characters are ignored.'
     test = { foreach ($p in @(@('([]{})', $true), @('([)]', $false), @('((', $false), @('a(b)c[d]{e}', $true), @('', $true), @('}', $false))) { if ((& $call 'IsBalanced' @($p[0])) -ne $p[1]) { throw $p[0] } } } }
  @{ id = 'cs-topk'; lang = 'csharp'
     prompt = 'Write a C# static method `int[] TopKFrequent(int[] nums, int k)` returning the k most frequent values, ordered by frequency (highest first) and then by value (smallest first) on ties.'
     test = { $r = & $call 'TopKFrequent' @([int[]]@(1, 1, 1, 2, 2, 3, 4, 4, 5), 3); if (($r -join ',') -ne '1,2,4') { throw 'basic' }; $r = & $call 'TopKFrequent' @([int[]]@(5, 3, 5, 3, 9), 2); if (($r -join ',') -ne '3,5') { throw 'ties' } } }
  @{ id = 'cs-first-index'; lang = 'csharp'
     prompt = 'Write a C# static method `int FirstIndexOf(int[] sorted, int target)` that uses binary search on an ascending array (duplicates allowed) and returns the index of the first occurrence of target, or -1.'
     test = { $a = [int[]]@(1, 2, 2, 2, 3, 5, 8); foreach ($p in @(@(2, 1), @(1, 0), @(8, 6), @(4, -1), @(9, -1), @(0, -1))) { if ((& $call 'FirstIndexOf' @($a, $p[0])) -ne $p[1]) { throw "$($p[0])" } }; if ((& $call 'FirstIndexOf' @([int[]]@(), 1)) -ne -1) { throw 'empty' } } }
  @{ id = 'cs-snake-case'; lang = 'csharp'
     prompt = 'Write a C# static method `string ToSnakeCase(string name)` converting camelCase or PascalCase to snake_case. A run of capitals is one word ("parseHTTPResponse" -> "parse_http_response", "XMLParser" -> "xml_parser", "userId2" -> "user_id2").'
     test = { foreach ($p in @(@('parseHTTPResponse', 'parse_http_response'), @('XMLParser', 'xml_parser'), @('userId2', 'user_id2'), @('simple', 'simple'), @('MyClass', 'my_class'))) { if ((& $call 'ToSnakeCase' @($p[0])) -ne $p[1]) { throw $p[0] } } } }

  # ---------------- PowerShell 5.1 ----------------
  @{ id = 'ps-initials'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Get-Initials` with a mandatory string parameter -Name that returns the uppercase first letter of each word (words separated by spaces or hyphens), e.g. "john ronald-reuel tolkien" -> "JRRT".'
     test = { if ((Get-Initials -Name 'john ronald-reuel tolkien') -ne 'JRRT') { throw 'jrrt' }; if ((Get-Initials -Name '  ada   lovelace ') -ne 'AL') { throw 'spaces' } } }
  @{ id = 'ps-titlecase'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `ConvertTo-TitleCase` with a parameter -Text that returns the text with each word capitalised and the rest of the word lowercase ("hello WORLD foo" -> "Hello World Foo"). Keep the original spacing.'
     test = { if ((ConvertTo-TitleCase -Text 'hello WORLD foo') -cne 'Hello World Foo') { throw 'basic' }; if ((ConvertTo-TitleCase -Text 'a  b') -cne 'A  B') { throw 'spacing' } } }
  @{ id = 'ps-ext-counts'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Get-ExtensionCount` with a parameter -Path (string array of file paths) that returns a hashtable mapping each lowercase extension without the dot to how many paths have it. Paths without an extension count under the key "" (empty string).'
     test = { $h = Get-ExtensionCount -Path @('a.TXT', 'b.txt', 'c:\x\y.ps1', 'README', 'd.tar.gz'); if ($h['txt'] -ne 2 -or $h['ps1'] -ne 1 -or $h[''] -ne 1 -or $h['gz'] -ne 1 -or $h.Count -ne 4) { throw 'counts' } } }
  @{ id = 'ps-ipv4'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Test-IPv4` with a parameter -Address that returns $true only for a dotted IPv4 address of four decimal numbers 0-255 without leading zeros (so "01.2.3.4" is invalid), otherwise $false.'
     test = { foreach ($p in @(@('203.0.113.7', $true), @('255.255.255.255', $true), @('256.1.1.1', $false), @('1.2.3', $false), @('01.2.3.4', $false), @('a.b.c.d', $false), @('0.0.0.0', $true), @('1.2.3.4.5', $false))) { if ((Test-IPv4 -Address $p[0]) -ne $p[1]) { throw $p[0] } } } }
  @{ id = 'ps-median'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Get-Median` with a parameter -Values (double array) that returns the median as a double (the mean of the two middle values for an even count). An empty array returns $null.'
     test = { if ((Get-Median -Values @(3, 1, 2)) -ne 2) { throw 'odd' }; if ((Get-Median -Values @(4, 1, 3, 2)) -ne 2.5) { throw 'even' }; if ($null -ne (Get-Median -Values @())) { throw 'empty' } } }
  @{ id = 'ps-chunks'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Split-Chunk` with parameters -Items (object array) and -Size (int) that returns the items split into consecutive chunks of at most Size items. Return each chunk as an array (use the comma operator so chunks are not flattened).'
     test = { $c = @(Split-Chunk -Items @(1, 2, 3, 4, 5) -Size 2); if ($c.Count -ne 3 -or ($c[0] -join ',') -ne '1,2' -or ($c[2] -join ',') -ne '5') { throw 'chunks' }; $c = @(Split-Chunk -Items @(1, 2) -Size 5); if ($c.Count -ne 1 -or $c[0].Count -ne 2) { throw 'one' } } }
  @{ id = 'ps-keyvalue'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `ConvertFrom-KeyValue` with a parameter -Text like "a=1; b = two;c=" that returns a hashtable: pairs separated by semicolons, key and value split at the first "=", both trimmed; empty pairs are skipped.'
     test = { $h = ConvertFrom-KeyValue -Text 'a=1; b = two;c=;;d=x=y'; if ($h['a'] -ne '1' -or $h['b'] -ne 'two' -or $h['c'] -ne '' -or $h['d'] -ne 'x=y' -or $h.Count -ne 4) { throw 'pairs' } } }
  @{ id = 'ps-fibonacci'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Get-Fibonacci` with a parameter -Count returning the first Count Fibonacci numbers as [long] values starting 0, 1, 1, 2. Count 0 returns nothing.'
     test = { $f = @(Get-Fibonacci -Count 10); if (($f -join ',') -ne '0,1,1,2,3,5,8,13,21,34') { throw 'ten' }; if (@(Get-Fibonacci -Count 0).Count -ne 0) { throw 'zero' }; $b = @(Get-Fibonacci -Count 90); if ($b[89] -ne 1779979416004714189) { throw 'long' } } }
  @{ id = 'ps-dedupe'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Remove-DuplicateLine` with a parameter -Lines (string array) that returns the lines with case-insensitive duplicates removed, keeping the first occurrence and the original order.'
     test = { $r = @(Remove-DuplicateLine -Lines @('b', 'A', 'a', 'c', 'B', 'd')); if (($r -join ',') -cne 'b,A,c,d') { throw 'dedupe' } } }
  @{ id = 'ps-weekdays'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Get-WeekdayCount` with [datetime] parameters -Start and -End that returns how many Monday-Friday dates lie between them, both dates included. If End is before Start return 0.'
     test = { if ((Get-WeekdayCount -Start ([datetime]'2026-10-26') -End ([datetime]'2026-11-08')) -ne 10) { throw 'two weeks' }; if ((Get-WeekdayCount -Start ([datetime]'2026-10-31') -End ([datetime]'2026-11-01')) -ne 0) { throw 'weekend' }; if ((Get-WeekdayCount -Start ([datetime]'2026-10-28') -End ([datetime]'2026-10-28')) -ne 1) { throw 'one' }; if ((Get-WeekdayCount -Start ([datetime]'2026-11-02') -End ([datetime]'2026-10-01')) -ne 0) { throw 'reversed' } } }
  @{ id = 'ps-format-bytes'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Format-Bytes` with a [long] parameter -Bytes returning a size string with base 1024 units B, KB, MB, GB, TB: under 1024 it is "<n> B"; otherwise one decimal place with an invariant-culture dot, e.g. 1536 -> "1.5 KB", 1048576 -> "1.0 MB".'
     test = { foreach ($p in @(@(0, '0 B'), @(1023, '1023 B'), @(1536, '1.5 KB'), @(1048576, '1.0 MB'), @(5368709120, '5.0 GB'), @(1099511627776, '1.0 TB'))) { if ((Format-Bytes -Bytes $p[0]) -ne $p[1]) { throw "$($p[0])" } } } }
  @{ id = 'ps-compare-version'; lang = 'powershell'
     prompt = 'Write a PowerShell 5.1 function `Compare-Version` with string parameters -A and -B (dotted numeric versions of any length, missing parts count as 0) returning 1 if A is newer, -1 if older, 0 if equal.'
     test = { foreach ($p in @(@('1.10.0', '1.9.3', 1), @('1.0', '1.0.0', 0), @('2', '10', -1), @('1.2.3.4', '1.2.3', 1), @('0.9', '0.10', -1))) { if ((Compare-Version -A $p[0] -B $p[1]) -ne $p[2]) { throw "$($p[0]) vs $($p[1])" } } } }
)


