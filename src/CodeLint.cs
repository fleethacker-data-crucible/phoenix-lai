// Adds the using directives missing from C# the model wrote (types and LINQ methods used without the namespace that
// provides them), in code, before anything else checks the reply. The other certain problems are CodeCheck's. C# 5 only.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoenix
{
    public static class CodeLint
    {
        // type (as written in code) -> namespace that provides it
        static readonly string[,] CsTypes = {
            { "List", "System.Collections.Generic" }, { "Dictionary", "System.Collections.Generic" }, { "HashSet", "System.Collections.Generic" },
            { "Queue", "System.Collections.Generic" }, { "Stack", "System.Collections.Generic" }, { "IEnumerable", "System.Collections.Generic" },
            { "IList", "System.Collections.Generic" }, { "IDictionary", "System.Collections.Generic" }, { "KeyValuePair", "System.Collections.Generic" },
            { "SortedDictionary", "System.Collections.Generic" }, { "LinkedList", "System.Collections.Generic" }, { "IReadOnlyList", "System.Collections.Generic" },
            { "StringBuilder", "System.Text" }, { "Encoding", "System.Text" }, { "Regex", "System.Text.RegularExpressions" }, { "Match", "System.Text.RegularExpressions" },
            { "Task", "System.Threading.Tasks" }, { "Parallel", "System.Threading.Tasks" }, { "Thread", "System.Threading" }, { "CancellationToken", "System.Threading" },
            { "File", "System.IO" }, { "Directory", "System.IO" }, { "Path", "System.IO" }, { "Stream", "System.IO" }, { "StreamReader", "System.IO" },
            { "StreamWriter", "System.IO" }, { "FileInfo", "System.IO" }, { "MemoryStream", "System.IO" },
            { "Stopwatch", "System.Diagnostics" }, { "Debug", "System.Diagnostics" }, { "CultureInfo", "System.Globalization" },
            { "ConcurrentDictionary", "System.Collections.Concurrent" }, { "ConcurrentQueue", "System.Collections.Concurrent" },
            { "JsonSerializer", "System.Text.Json" } };
        static readonly Regex LinqCall = new Regex(@"\.(Where|Select|SelectMany|OrderBy|OrderByDescending|ThenBy|GroupBy|Average|Sum|Min|Max|Count|Any|All|First|FirstOrDefault|Last|LastOrDefault|Single|SingleOrDefault|Skip|Take|Distinct|ToList|ToArray|ToDictionary|Aggregate|Zip|Concat|Contains)\s*\(");

        // Adds the missing using directives to every C# code block and <write> body of a reply. Returns the fixed reply;
        // `fixes` lists what was added. Deterministic, so it cannot make the code worse the way a model rewrite can.
        public static string FixUsings(string reply, List<string> fixes)
        {
            return Regex.Replace(reply, @"(```[ \t]*(?:csharp|cs|c#)?[ \t]*\r?\n|<write\s+path\s*=\s*""[^""]*\.cs""\s*>\r?\n?)([\s\S]*?)(```|</write>)", delegate(Match m)
            {
                string code = m.Groups[2].Value;
                List<string> missing = MissingNamespaces(code);
                if (missing.Count == 0) return m.Value;
                StringBuilder add = new StringBuilder();
                foreach (string ns in missing) { add.Append("using ").Append(ns).Append(";\n"); fixes.Add("added `using " + ns + ";`"); }
                // after the last using directive, else at the top
                Match last = null;
                foreach (Match u in Regex.Matches(code, @"^[ \t]*using\s+(static\s+)?[\w.]+\s*;[ \t]*\r?\n", RegexOptions.Multiline)) last = u;
                string fixedCode = last != null ? code.Substring(0, last.Index + last.Length) + add + code.Substring(last.Index + last.Length)
                                                : add + (Regex.IsMatch(code, @"^\s*(namespace|public|internal|static|class)") ? "\n" : "") + code;
                return m.Groups[1].Value + fixedCode + m.Groups[3].Value;
            });
        }

        // Namespaces whose types / LINQ methods the code uses without a using directive.
        static List<string> MissingNamespaces(string code)
        {
            List<string> found = new List<string>();
            // no comments, strings or chars (a '"' char literal must not start a string: CSV code is full of them)
            string body = Regex.Replace(code, @"//[^\n]*|/\*[\s\S]*?\*/|'(?:[^'\\\n]|\\.)+'|@""(?:[^""]|"""")*""|\$?""(?:[^""\\\n]|\\.)*""", " ");
            HashSet<string> usings = new HashSet<string>();
            foreach (Match m in Regex.Matches(body, @"^\s*using\s+(static\s+)?([\w.]+)\s*;", RegexOptions.Multiline)) usings.Add(m.Groups[2].Value);
            foreach (Match m in Regex.Matches(body, @"^\s*namespace\s+([\w.]+)", RegexOptions.Multiline)) usings.Add(m.Groups[1].Value);
            bool topLevel = Regex.IsMatch(body, @"\b(class|struct|record|interface)\s+\w") || usings.Count > 0;
            if (!topLevel) return found;   // a fragment: its usings are not shown, so nothing to say
            HashSet<string> reported = new HashSet<string>();
            for (int i = 0; i < CsTypes.GetLength(0); i++)
            {
                string type = CsTypes[i, 0], ns = CsTypes[i, 1];
                if (usings.Contains(ns) || reported.Contains(ns)) continue;
                // used as a type: List<..>, new StringBuilder(, File.ReadAll..., Task<..> / Task.Run
                if (!Regex.IsMatch(body, @"(?<![\w.])" + type + @"\s*(<|\(|\.|\[\]|\s+\w)")) continue;
                if (Regex.IsMatch(body, @"(class|struct|record|interface|enum)\s+" + type + @"\b")) continue;   // the code defines it
                if (Regex.IsMatch(body, @"\b" + Regex.Escape(ns) + @"\." + type + @"\b")) continue;          // fully qualified
                found.Add(ns);
                reported.Add(ns);
            }
            if (!usings.Contains("System.Linq") && LinqCall.IsMatch(body) && Regex.IsMatch(body, @"\b(List|IEnumerable|\w+\[\])\b|\.Where\(|\.Select\(|\.Average\("))
            {
                Match m = LinqCall.Match(body);
                // .Count( / .Contains( alone also exist without LINQ; only report other methods
                if (m.Groups[1].Value != "Count" && m.Groups[1].Value != "Contains") found.Add("System.Linq");
            }
            return found;
        }
    }
}
