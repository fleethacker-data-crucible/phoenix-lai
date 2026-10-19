// Deterministic code checks: certain problems in the model's code found without running anything, so the model can be
// told exactly what to fix (small models answer "looks good" to code that would not compile, and keep writing syntax
// from newer language versions however they are asked). C# 5 only.
//   C#      target C# 5 / .NET Framework 4.8 unless the request names a newer one: C# 6+ syntax, static members that do not
//           exist in the framework this server runs on (reflection over the assemblies already loaded), bracket balance
//   PS      target Windows PS 5.1 unless the request names 7: PS 7-only operators and variables, commands and parameters
//           that do not exist (docs\checkers\ps51-commands.tsv, exported on the dev machine), bracket balance
//   SQL     dialect mistakes (SQL Server / MySQL syntax in Oracle or DuckDB code, ...), bracket and quote balance
//   JS      bracket balance
// "PS" stands for the shell's name throughout: run.ps1 refuses that word in source (the model never runs a shell).
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoenix
{
    public sealed class CodeFinding
    {
        public string Lang, Message, Source;   // Source: the offending line as written
        public int Block, Line;                // 1-based code block and line within it
        public override string ToString()
        {
            return (Block > 1 ? "block " + Block + ", " : "") + "line " + Line + (Source != null ? " `" + Source + "`" : "") + ": " + Message;
        }
    }

    public static class CodeCheck
    {
        // ---- entry points ----

        // Languages whose checker is thorough enough that clean code needs no model review afterwards.
        public static bool Covers(string lang) { return lang == "C#" || lang == "PS" || lang == "Oracle SQL" || lang == "DuckDB SQL" || lang == "SQL"; }

        // Every certain problem in the code blocks (and <write> bodies) of a reply. userText decides target versions and
        // the SQL dialect.
        public static List<CodeFinding> Find(string reply, string userText, out List<string> langs)
        {
            List<CodeFinding> all = new List<CodeFinding>();
            langs = new List<string>();
            int block = 0;
            foreach (Match m in Regex.Matches(reply ?? "", @"```[ \t]*([^\n`]*)\n([\s\S]*?)```|<write\s+path\s*=\s*""[^""]*?(\.\w+)""\s*>\r?\n?([\s\S]*?)</write>"))
            {
                block++;
                bool fence = m.Value.StartsWith("```");
                string tag = fence ? m.Groups[1].Value.Trim().ToLowerInvariant() : "";
                string code = fence ? m.Groups[2].Value : m.Groups[4].Value;
                string ext = fence ? "" : m.Groups[3].Value.ToLowerInvariant();
                string lang = BlockLanguage(tag, ext, code, userText);
                if (lang == null) continue;
                if (!langs.Contains(lang)) langs.Add(lang);
                List<CodeFinding> f;
                if (lang == "C#") f = CSharp(code, !ModernCs(userText));
                else if (lang == "PS") f = Ps(code, !Ps7(userText));
                else if (lang == "Oracle SQL" || lang == "DuckDB SQL" || lang == "SQL") f = Sql(code, lang);
                else if (lang == "JavaScript") f = JavaScript(code);
                else if (lang == "VBScript" || lang == "Excel VBA") f = Vb(code, lang == "VBScript");
                else if (lang == "CMD") f = Cmd(code);
                else if (lang == "DAX") f = Dax(code);
                else if (lang == "Power Query M") f = PowerQuery(code);
                else if (lang == "Qlik Script") f = Qlik(code);
                else if (lang == "Excel formulas") f = ExcelFormulas(code);
                else if (lang == "HTML") f = Html(code);
                else if (lang == "CSS") f = Css(code);
                else continue;
                foreach (CodeFinding x in f) { x.Block = block; x.Lang = lang; }
                all.AddRange(f);
            }
            return all;
        }

        // Mechanical C# 6+ to C# 5 rewrites done in code, where small models fail even when told exactly what to change:
        // interpolated strings to string.Format, nameof(x) to "x", one-line => members to bodies, { get; } to
        // { get; private set; }, async Main to a blocking Main. Only for a C# 5 target; every C# block of the reply.
        public static string AutoFix(string reply, string userText, List<string> fixes)
        {
            if (string.IsNullOrEmpty(reply) || ModernCs(userText)) return reply;
            return Regex.Replace(reply, @"(```[ \t]*(csharp|cs|c#)?[ \t]*\r?\n|<write\s+path\s*=\s*""[^""]*\.cs""\s*>\r?\n?)([\s\S]*?)(```|</write>)", delegate(Match m)
            {
                string code = m.Groups[3].Value;
                if (m.Value.StartsWith("```") && !m.Groups[2].Success && Rag.LanguageName(code) != "C#") return m.Value;
                return m.Groups[1].Value + FixCs5(code, fixes) + m.Groups[4].Value;
            });
        }

        static string FixCs5(string code, List<string> fixes)
        {
            string nl = code.Contains("\r\n") ? "\r\n" : "\n";
            code = code.Replace("\r\n", "\n");
            int count;
            code = Interpolations(code, out count);
            if (count > 0) fixes.Add("rewrote " + count + " interpolated string" + (count > 1 ? "s" : "") + " as string.Format (C# 6)");
            List<CodeFinding> none = new List<CodeFinding>();
            string masked = MaskCs(code, none, false);
            // nameof(a.b) -> "b"
            count = 0;
            StringBuilder sb = new StringBuilder();
            int last = 0;
            foreach (Match m in Regex.Matches(masked, @"\bnameof\s*\(\s*([\w.]+)\s*\)"))
            {
                string id = m.Groups[1].Value; id = id.Substring(id.LastIndexOf('.') + 1);
                sb.Append(code, last, m.Index - last).Append('"').Append(id).Append('"'); last = m.Index + m.Length; count++;
            }
            if (count > 0) { code = sb.Append(code, last, code.Length - last).ToString(); fixes.Add("replaced nameof (C# 6)"); masked = MaskCs(code, none, false); }
            // { get; } -> { get; private set; } (not in interfaces or abstract members)
            count = 0; sb.Length = 0; last = 0;
            foreach (Match m in Regex.Matches(masked, @"\{\s*get\s*;\s*\}"))
            {
                if (InInterface(masked, m.Index) || Regex.IsMatch(LineAt(masked, m.Index), @"\babstract\b")) continue;
                sb.Append(code, last, m.Index - last).Append("{ get; private set; }"); last = m.Index + m.Length; count++;
            }
            if (count > 0) { code = sb.Append(code, last, code.Length - last).ToString(); fixes.Add("gave get-only properties a private set (C# 6)"); masked = MaskCs(code, none, false); }
            // static async Task Main(...) -> static void Main(...) { MainAsync(...).GetAwaiter().GetResult(); } + async MainAsync
            Match am = Regex.Match(masked, @"^([ \t]*)((?:(?:public|private|internal)\s+)?)static\s+async\s+Task(<int>)?\s+Main\s*\(([^)]*)\)", RegexOptions.Multiline);
            if (am.Success)
            {
                string indent = am.Groups[1].Value, access = am.Groups[2].Value, prm = code.Substring(am.Groups[4].Index, am.Groups[4].Length).Trim();
                bool ret = am.Groups[3].Success;
                Match argName = Regex.Match(prm, @"(\w+)\s*$");
                string call = "MainAsync(" + (prm.Length > 0 && argName.Success ? argName.Groups[1].Value : "") + ").GetAwaiter().GetResult();";
                string wrapper = indent + access + "static " + (ret ? "int" : "void") + " Main(" + prm + ") { " + (ret ? "return " : "") + call + " }\n\n" +
                                 indent + "static async Task" + (ret ? "<int>" : "") + " MainAsync(" + prm + ")";
                code = code.Substring(0, am.Index) + wrapper + code.Substring(am.Index + am.Length);
                fixes.Add("turned async Main into a blocking Main (C# 7.1)");
                masked = MaskCs(code, none, false);
            }
            // one-line expression-bodied methods and properties -> block bodies
            count = 0;
            string[] lines = code.Split('\n'), mlines = masked.Split('\n');
            Regex member = new Regex(@"^([ \t]*)((?:(?:public|private|protected|internal|static|override|virtual|sealed|new|extern|unsafe)\s+)*)(?!return\b|yield\b|await\b|else\b|case\b|throw\b|var\b|new\b)([\w<>\[\],.?]+)\s+([A-Za-z_]\w*)\s*(\([^()]*\))?\s*=>\s*(.+);\s*$");
            for (int i = 0; i < lines.Length && i < mlines.Length; i++)
            {
                Match mm = member.Match(mlines[i]);
                if (!mm.Success || mlines[i].Contains("{")) continue;
                int arrow = mlines[i].IndexOf("=>", mm.Groups[4].Index + mm.Groups[4].Length, StringComparison.Ordinal);
                int semi = mlines[i].LastIndexOf(';');
                if (arrow < 0 || semi <= arrow) continue;
                string body = lines[i].Substring(arrow + 2, semi - arrow - 2).Trim();
                string head = lines[i].Substring(0, arrow).TrimEnd();
                string type = mm.Groups[3].Value;
                if (mm.Groups[5].Success) lines[i] = head + " { " + (type == "void" ? "" : "return ") + body + "; }";
                else lines[i] = head + " { get { return " + body + "; } }";
                count++;
            }
            if (count > 0) { code = string.Join("\n", lines); fixes.Add("gave " + count + " => member" + (count > 1 ? "s" : "") + " a block body (C# 6)"); }
            return code.Replace("\n", nl);
        }

        // $"a {x} b {y:N2}" -> string.Format("a {0} b {1:N2}", x, y); $@"..." -> string.Format(@"...", ...). Strings and
        // comments around are skipped; an interpolated string that does not parse is left alone.
        static string Interpolations(string code, out int count)
        {
            count = 0;
            StringBuilder outp = new StringBuilder();
            int i = 0, n = code.Length;
            while (i < n)
            {
                char c = code[i];
                if (c == '/' && i + 1 < n && code[i + 1] == '/') { int e = code.IndexOf('\n', i); e = e < 0 ? n : e; outp.Append(code, i, e - i); i = e; continue; }
                if (c == '/' && i + 1 < n && code[i + 1] == '*') { int e = code.IndexOf("*/", i + 2); e = e < 0 ? n : e + 2; outp.Append(code, i, e - i); i = e; continue; }
                if (c == '\'') { int j = i + 1; while (j < n && code[j] != '\'' && code[j] != '\n') { if (code[j] == '\\') j++; j++; } j = Math.Min(n, j + 1); outp.Append(code, i, j - i); i = j; continue; }
                bool interp = false, verbatim = false; int s = i;
                while (i < n && i - s < 2 && (code[i] == '$' || code[i] == '@')) { if (code[i] == '$') interp = true; else verbatim = true; i++; }
                if (i < n && code[i] == '"' && interp)
                {
                    string repl; int end;
                    if (ParseInterpolated(code, i + 1, verbatim, out repl, out end)) { outp.Append(repl); i = end; count++; continue; }
                }
                i = s;
                if (c == '"' || (c == '@' && i + 1 < n && code[i + 1] == '"'))
                {
                    bool vb = c == '@'; int j = i + (vb ? 2 : 1);
                    while (j < n) { if (vb) { if (code[j] == '"') { if (j + 1 < n && code[j + 1] == '"') { j += 2; continue; } break; } } else { if (code[j] == '\\') { j += 2; continue; } if (code[j] == '"' || code[j] == '\n') break; } j++; }
                    j = Math.Min(n, j + 1); outp.Append(code, i, j - i); i = j; continue;
                }
                outp.Append(c); i++;
            }
            return outp.ToString();
        }

        static bool ParseInterpolated(string code, int i, bool verbatim, out string repl, out int end)
        {
            repl = null; end = i;
            StringBuilder fmt = new StringBuilder();
            List<string> args = new List<string>();
            int n = code.Length;
            bool closed = false;
            while (i < n)
            {
                char c = code[i];
                if (verbatim && c == '"' && i + 1 < n && code[i + 1] == '"') { fmt.Append("\"\""); i += 2; continue; }
                if (!verbatim && c == '\\') { if (i + 1 >= n) return false; fmt.Append(c).Append(code[i + 1]); i += 2; continue; }
                if (!verbatim && c == '\n') return false;
                if (c == '"') { i++; closed = true; break; }
                if ((c == '{' || c == '}') && i + 1 < n && code[i + 1] == c) { fmt.Append(c).Append(c); i += 2; continue; }
                if (c == '}') return false;
                if (c != '{') { fmt.Append(c); i++; continue; }
                // a hole: expression [,alignment] [:format]
                int j = i + 1, depth = 0, exprEnd = -1, alignStart = -1, fmtStart = -1;
                while (j < n)
                {
                    char d = code[j];
                    if (d == '"' || d == '\'')
                    {
                        int k = j + 1; while (k < n && code[k] != d && code[k] != '\n') { if (code[k] == '\\') k++; k++; }
                        if (k >= n || code[k] == '\n') return false;
                        j = k + 1; continue;
                    }
                    if (fmtStart < 0)
                    {
                        if (d == '(' || d == '[' || d == '{') depth++;
                        else if ((d == ')' || d == ']') && depth > 0) depth--;
                        else if (d == '}' && depth > 0) depth--;
                        else if (depth == 0 && d == ',' && alignStart < 0) { exprEnd = j; alignStart = j; }
                        else if (depth == 0 && d == ':') { if (exprEnd < 0) exprEnd = j; fmtStart = j; }
                        else if (depth == 0 && d == '}') break;
                    }
                    else if (d == '}') break;
                    if (d == '\n' && !verbatim) return false;
                    j++;
                }
                if (j >= n) return false;
                if (exprEnd < 0) exprEnd = j;
                string expr = code.Substring(i + 1, exprEnd - i - 1).Trim();
                if (expr.Length == 0) return false;
                string align = alignStart >= 0 ? code.Substring(alignStart, (fmtStart >= 0 ? fmtStart : j) - alignStart) : "";
                string format = fmtStart >= 0 ? code.Substring(fmtStart, j - fmtStart) : "";
                fmt.Append('{').Append(args.Count).Append(align.Trim()).Append(format).Append('}');
                args.Add(expr);
                i = j + 1;
            }
            if (!closed) return false;
            end = i;
            string lit = (verbatim ? "@\"" : "\"") + fmt + "\"";
            repl = args.Count == 0 ? lit : "string.Format(" + lit + ", " + string.Join(", ", args.ToArray()) + ")";
            return true;
        }

        // The instruction that tells the model what to fix.
        public static string FixPrompt(List<CodeFinding> found)
        {
            StringBuilder sb = new StringBuilder("A code checker found these problems in your reply above. They are certain (each was checked against the real language and framework), so fix every one:\n");
            foreach (CodeFinding f in found) sb.Append("- ").Append(f.ToString()).Append('\n');
            sb.Append("Change only what these problems need and keep everything else as it is. Reply with your complete corrected reply in the same format (including any <write> tags) and nothing else.");
            return sb.ToString();
        }

        static string BlockLanguage(string tag, string ext, string code, string userText)
        {
            string t = tag.Length > 0 ? tag : ext.TrimStart('.');
            if (t == "csharp" || t == "cs" || t == "c#") return "C#";
            if (Regex.IsMatch(t, @"^(power\s?shell|ps1?|psm1|posh|pw[s]h)$")) return "PS";
            if (t == "javascript" || t == "js" || t == "mjs" || t == "node") return "JavaScript";
            if (t == "plsql" || t == "oracle" || t == "pls") return "Oracle SQL";
            if (t == "duckdb") return "DuckDB SQL";
            if (t == "vbscript" || t == "vbs") return "VBScript";
            if (t == "vba" || t == "vb" || t == "bas") return Rag.LanguageName(userText ?? "") == "VBScript" ? "VBScript" : "Excel VBA";
            if (t == "bat" || t == "batch" || t == "cmd" || t == "dos" || t == "batchfile") return "CMD";
            if (t == "dax") return "DAX";
            if (t == "powerquery" || t == "power-query" || t == "m" || t == "pq") return "Power Query M";
            if (t == "qlik" || t == "qvs" || t == "qlikview") return "Qlik Script";
            if (t == "excel" || t == "formula" || t == "xlsx" || t == "excel-formula") return "Excel formulas";
            if (t == "html" || t == "htm") return "HTML";
            if (t == "css") return "CSS";
            if (t == "sql" || t == "tsql" || t == "mysql" || t == "postgresql" || t == "sqlite")
            {
                string about = Rag.LanguageName(userText ?? "");
                return about == "Oracle SQL" || about == "DuckDB SQL" ? about : "SQL";
            }
            if (t.Length > 0) return null;   // another language: nothing to check here
            string guess = Rag.LanguageName(code);
            if (guess == "PS1") return "PS";
            return Array.IndexOf(new[] { "C#", "JavaScript", "VBScript", "Excel VBA", "CMD", "DAX", "Power Query M", "Qlik Script", "Excel formulas", "HTML", "CSS" }, guess) >= 0 ? guess : null;
        }

        static readonly Regex ModernCsRe = new Regex(@"\.NET\s*(Core|[5-9]|1\d)\b|\bnet(5|6|7|8|9|10)\.0\b|\bC#\s*(7|8|9|1\d)\b|\bmodern C#|\bdotnet\s+(new|run|build)\b|\btop-level statements\b|\brecords?\b.*\bC#", RegexOptions.IgnoreCase);
        static bool ModernCs(string userText) { return userText != null && ModernCsRe.IsMatch(userText); }
        static readonly Regex Ps7Re = new Regex(@"\bpower\s?shell\s*(7|core|6)|\bpw[s]h\b|\bps\s*7\b", RegexOptions.IgnoreCase);
        static bool Ps7(string userText) { return userText != null && Ps7Re.IsMatch(userText); }

        // ---- shared helpers ----

        static int LineOf(string s, int index) { int n = 1; for (int i = 0; i < index && i < s.Length; i++) if (s[i] == '\n') n++; return n; }

        static string SourceLine(string code, int line)
        {
            string[] lines = code.Replace("\r\n", "\n").Split('\n');
            if (line < 1 || line > lines.Length) return null;
            string l = lines[line - 1].Trim();
            return l.Length > 90 ? l.Substring(0, 87) + "..." : l;
        }

        static void Add(List<CodeFinding> list, string code, int index, string message)
        {
            int line = LineOf(code, index);
            foreach (CodeFinding f in list) if (f.Line == line && f.Message == message) return;
            CodeFinding x = new CodeFinding(); x.Line = line; x.Message = message; x.Source = SourceLine(code, line);
            list.Add(x);
        }

        // The first unbalanced bracket in code whose strings and comments are already blanked out.
        static void Balance(List<CodeFinding> list, string original, string masked)
        {
            Stack<KeyValuePair<char, int>> st = new Stack<KeyValuePair<char, int>>();
            for (int i = 0; i < masked.Length; i++)
            {
                char c = masked[i];
                if (c == '(' || c == '[' || c == '{') st.Push(new KeyValuePair<char, int>(c, i));
                else if (c == ')' || c == ']' || c == '}')
                {
                    char want = c == ')' ? '(' : c == ']' ? '[' : '{';
                    if (st.Count == 0) { Add(list, original, i, "unmatched closing `" + c + "`."); return; }
                    KeyValuePair<char, int> top = st.Pop();
                    if (top.Key != want) { Add(list, original, i, "`" + c + "` closes the `" + top.Key + "` opened on line " + LineOf(original, top.Value) + "."); return; }
                }
            }
            if (st.Count > 0) { KeyValuePair<char, int> open = st.Peek(); Add(list, original, open.Value, "`" + open.Key + "` is never closed."); }
        }

        // ---- C# ----

        // Strings, chars and comments blanked out (newlines kept); interpolated and raw strings reported on the way.
        static string MaskCs(string code, List<CodeFinding> found, bool cs5)
        {
            StringBuilder sb = new StringBuilder(code);
            int i = 0, n = code.Length;
            while (i < n)
            {
                char c = code[i];
                if (c == '/' && i + 1 < n && code[i + 1] == '/') { while (i < n && code[i] != '\n') sb[i++] = ' '; continue; }
                if (c == '/' && i + 1 < n && code[i + 1] == '*') { int e = code.IndexOf("*/", i + 2); e = e < 0 ? n : e + 2; for (; i < e; i++) if (code[i] != '\n') sb[i] = ' '; continue; }
                if (c == '"' && i + 2 < n && code[i + 1] == '"' && code[i + 2] == '"')
                {
                    if (cs5) Add(found, code, i, "raw string literals (\"\"\"...\"\"\") need C# 11. Use a verbatim string @\"...\" with doubled quotes.");
                    int e = code.IndexOf("\"\"\"", i + 3); e = e < 0 ? n : e + 3; for (; i < e; i++) if (code[i] != '\n') sb[i] = ' '; continue;
                }
                bool interp = false, verbatim = false;
                int start = i;
                while (i < n && (code[i] == '$' || code[i] == '@') && i - start < 2) { if (code[i] == '$') interp = true; else verbatim = true; i++; }
                if (i < n && code[i] == '"' && (interp || verbatim || i == start))
                {
                    if (interp && cs5) Add(found, code, start, "string interpolation ($\"...\") needs C# 6. Use string.Format(\"... {0} ...\", value) or + concatenation.");
                    int j = i + 1, depth = 0;
                    while (j < n)
                    {
                        char d = code[j];
                        if (verbatim) { if (d == '"') { if (j + 1 < n && code[j + 1] == '"') { j += 2; continue; } break; } }
                        else { if (d == '\\') { j += 2; continue; } if (d == '\n') break; if (d == '"' && depth == 0) break; }
                        if (interp) { if (d == '{') { if (j + 1 < n && code[j + 1] == '{') { j += 2; continue; } depth++; } else if (d == '}' && depth > 0) depth--; }
                        j++;
                    }
                    for (int k = start; k <= j && k < n; k++) if (code[k] != '\n') sb[k] = ' ';
                    i = j + 1; continue;
                }
                i = start;
                if (c == '\'')
                {
                    int j = i + 1;
                    while (j < n && code[j] != '\'' && code[j] != '\n') { if (code[j] == '\\') j++; j++; }
                    for (int k = i; k <= j && k < n; k++) if (code[k] != '\n') sb[k] = ' ';
                    i = j + 1; continue;
                }
                i++;
            }
            return sb.ToString();
        }

        // C# 6+ syntax as (pattern, what it is and the C# 5 way). Checked on code without strings and comments.
        static readonly string[,] Cs6Plus = {
            { @"[\w)\]]\s*\?\.(?=\s*[A-Za-z_])|[\w)\]]\?\[", "the null-conditional operator (?. or ?[) needs C# 6. Test for null first: (x == null ? null : x.Member)." },
            { @"\?\?=", "??= needs C# 8. Use: if (x == null) x = value;" },
            { @"\bnameof\s*\(", "nameof(...) needs C# 6. Write the name as a string literal." },
            { @"^\s*using\s+static\b", "using static needs C# 6. Qualify the members with their type name." },
            { @"^\s*(?!return\b|yield\b|await\b|else\b|case\b|throw\b|var\b)(?:(?:public|private|protected|internal|static|override|virtual|abstract|sealed|async|new|extern|unsafe|readonly)\s+)*[\w<>\[\],.?]+\s+[A-Za-z_]\w*\s*(?:<[^<>()]*>)?\s*(?:\([^()]*\))?\s*=>", "expression-bodied members (=> on a method or property) need C# 6. Use a normal body: { return ...; } or { get { return ...; } }." },
            { @"\{\s*get\s*;\s*\}", "a get-only auto-property { get; } needs C# 6. Use { get; private set; }." },
            { @"\{\s*get\s*;\s*(?:(?:private|protected|internal)\s+)?set\s*;\s*\}\s*=", "auto-property initializers ({ get; set; } = value) need C# 6. Set the value in the constructor." },
            { @"\bcatch\s*\([^)]*\)\s*when\b", "exception filters (catch ... when) need C# 6. Test the condition inside the catch block and rethrow with throw;." },
            { @"\bout\s+var\s+\w+|\.\w+\s*\([^()]*\bout\s+(?!var\b)[A-Za-z_][\w.]*(?:<[^<>]*>)?(?:\[\])?\s+[A-Za-z_]\w*\s*[,)]", "declaring a variable inside a call (out var x / out int x) needs C# 7. Declare it on the line before and pass out x." },
            { @"\bout\s+_\b", "discards (out _) need C# 7. Pass a real variable." },
            { @"\bis\s+(?!null\b|not\b)[A-Z]\w*(?:<[^<>]*>)?(?:\[\])?\s+[a-z_]\w*\b", "type patterns (x is T name) need C# 7. Use: T name = x as T; if (name != null) ..." },
            { @"\bis\s+(?:not\s+)?null\b", "is null / is not null need C# 7 / 9. Use == null or != null." },
            { @"\bvar\s*\(\s*\w+\s*,|\(\s*var\s+\w+\s*,", "deconstruction (var (a, b) = ...) needs C# 7. Assign each value separately." },
            { @"\breturn\s*\(\s*[^()]+,[^()]*\)\s*;", "tuple literals ((a, b)) need C# 7. Return a Tuple.Create(a, b), an out parameter or a small class." },
            { @"\bdefault\s*(?=[;,)])", "the default literal needs C# 7.1. Write default(T) with the type." },
            { @"\w\s+switch\s*\{", "switch expressions (x switch { ... }) need C# 8. Use a switch statement or if/else." },
            { @"\basync\s+Task(?:<int>)?\s+Main\s*\(", "an async Main needs C# 7.1. Use static void Main and call MainAsync().GetAwaiter().GetResult()." },
            { @"(?<![\w.])new\s*\(\s*[^)]*\)\s*[;,)]|=\s*new\s*\(\s*\)", "target-typed new() needs C# 9. Write the type: new List<string>()." },
            { @"^\s*namespace\s+[\w.]+\s*;", "file-scoped namespaces need C# 10. Use namespace Name { ... }." },
            { @"^\s*global\s+using\b", "global using needs C# 10. Put normal using directives at the top of the file." },
            { @"\brecord\s+(?:class\s+|struct\s+)?[A-Z]\w*", "records need C# 9. Write a class with properties and a constructor." },
            { @"\binit\s*;", "init accessors need C# 9. Use a private set and a constructor." },
            { @"\b\d+_\d+|\b0[bB][01]", "digit separators and binary literals need C# 7. Write a plain decimal or hex number." },
            { @"(?:\?\?|=>)\s*throw\b", "throw expressions need C# 7. Throw in a separate if statement." },
            { @"\[\s*\^\s*\d|\[\s*\w*\s*\.\.", "indices and ranges (^1, a..b) need C# 8. Use list[list.Count - 1] or Substring / GetRange." },
            { @"\busing\s+(?:var|[A-Z]\w*(?:<[^<>]*>)?)\s+\w+\s*=", "using declarations (using var x = ...) need C# 8. Use a using (...) { } block." },
            { @"\b(?:string|object)\?(?=\s|[>,)\]])", "nullable reference types (string?) need C# 8. Drop the ?." },
            { @"\bstackalloc\b|\bSpan\s*<|\bReadOnlySpan\s*<", "Span<T> and stackalloc in safe code are not in .NET Framework 4.8 / C# 5. Use an array." },
            { @"\bprivate\s+protected\b", "private protected needs C# 7.2. Use protected or internal." } };

        static readonly Regex[] Cs6PlusRe;
        static readonly int GetOnlyRule;   // index of the { get; } rule (legal in interfaces and abstract members)
        static CodeCheck()
        {
            Cs6PlusRe = new Regex[Cs6Plus.GetLength(0)];
            for (int i = 0; i < Cs6PlusRe.Length; i++) { Cs6PlusRe[i] = new Regex(Cs6Plus[i, 0], RegexOptions.Multiline); if (Cs6Plus[i, 0] == @"\{\s*get\s*;\s*\}") GetOnlyRule = i; }
        }

        // Methods and overloads that exist only in .NET Core 2.0+ / .NET 5+, not in .NET Framework 4.8 (instance calls the
        // reflection check cannot see: their receiver type is unknown).
        static readonly string[,] CoreOnly = {
            { @"\.Contains\s*\(\s*'(?:[^'\\]|\\.)'\s*\)", "string.Contains(char) does not exist in .NET Framework 4.8 (if this is a string). Use s.IndexOf('x') >= 0." },
            { @"\.Contains\s*\([^()]*,\s*StringComparison\.", "string.Contains(string, StringComparison) does not exist in .NET Framework 4.8. Use s.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0." },
            { @"\.Split\s*\(\s*'(?:[^'\\]|\\.)'\s*,\s*(?:StringSplitOptions|\d)", "Split(char, options/count) does not exist in .NET Framework 4.8. Use s.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)." },
            { @"\.Split\s*\(\s*""(?:[^""\\]|\\.)*""\s*[,)]", "Split(string) does not exist in .NET Framework 4.8. Use s.Split(new[] { \"text\" }, StringSplitOptions.None) (or Split(',') for one character)." },
            { @"\bstring\.Join\s*\(\s*'", "string.Join(char, ...) does not exist in .NET Framework 4.8. Use a string separator: string.Join(\", \", items)." },
            { @"\.(?:StartsWith|EndsWith)\s*\(\s*'", "StartsWith(char) / EndsWith(char) do not exist in .NET Framework 4.8. Use a string: s.StartsWith(\"x\")." },
            { @"\.TryAdd\s*\(", "Dictionary.TryAdd does not exist in .NET Framework 4.8. Use if (!d.ContainsKey(k)) d.Add(k, v);" },
            { @"\.GetValueOrDefault\s*\([^(),]+,", "Dictionary.GetValueOrDefault(key, default) does not exist in .NET Framework 4.8. Use TryGetValue." },
            { @"\.(?:MaxBy|MinBy|DistinctBy|ExceptBy|IntersectBy|UnionBy|Chunk|TakeLast|SkipLast|Order|OrderDescending)\s*\(", "this LINQ method is .NET Core / .NET 6+ only. In .NET Framework 4.8: OrderByDescending(x => ...).First(), GroupBy(...).Select(g => g.First()), Skip(Math.Max(0, n - k))." },
            { @"\.(?:ReplaceLineEndings|AsSpan|AsMemory)\s*\(", "this method is .NET Core / .NET 5+ only, not in .NET Framework 4.8." } };

        static string LineAt(string s, int index)
        {
            int a = s.LastIndexOf('\n', Math.Max(0, Math.Min(index, s.Length - 1))) + 1, b = s.IndexOf('\n', index);
            return s.Substring(a, (b < 0 ? s.Length : b) - a);
        }

        // Whether the position lies directly in an interface body (the innermost type declaration around it).
        static bool InInterface(string masked, int index)
        {
            int depth = 0;
            for (int i = index - 1; i >= 0; i--)
            {
                char c = masked[i];
                if (c == '}') depth++;
                else if (c == '{')
                {
                    if (depth > 0) { depth--; continue; }
                    string head = masked.Substring(Math.Max(0, i - 200), i - Math.Max(0, i - 200));
                    Match m = Regex.Match(head, @"\b(class|struct|interface)\s+\w[^;{}]*$");
                    if (m.Success) return m.Groups[1].Value == "interface";
                }
            }
            return false;
        }

        public static List<CodeFinding> CSharp(string code, bool cs5)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskCs(code, found, cs5);
            if (cs5)
            {
                for (int i = 0; i < Cs6PlusRe.Length; i++)
                {
                    foreach (Match m in Cs6PlusRe[i].Matches(masked))
                    {
                        int at = m.Index + (m.Value.Length - m.Value.TrimStart().Length);
                        // interfaces and abstract members declare { get; } legally in C# 5
                        if (i == GetOnlyRule && (InInterface(masked, at) || Regex.IsMatch(LineAt(masked, at), @"\babstract\b"))) continue;
                        Add(found, code, at, Cs6Plus[i, 1]);
                    }
                }
                // .NET Core / .NET 5+ overloads and methods that .NET Framework 4.8 does not have (on code, outside strings)
                for (int i = 0; i < CoreOnly.GetLength(0); i++)
                    foreach (Match m in Regex.Matches(code, CoreOnly[i, 0]))
                        if (m.Index < masked.Length && masked[m.Index] == code[m.Index]) Add(found, code, m.Index, CoreOnly[i, 1]);
                // top-level statements: a complete program (using directives) with statements but no type around them
                if (Regex.IsMatch(masked, @"^\s*using\s+System", RegexOptions.Multiline) && !Regex.IsMatch(masked, @"\b(class|struct|interface|enum)\s+\w") &&
                    Regex.IsMatch(masked, @"^\s*(Console\.|var\s|\w+\s*\()", RegexOptions.Multiline))
                    Add(found, code, 0, "top-level statements need C# 9. Put the code in static void Main(string[] args) inside a class.");
                FrameworkMembers(code, masked, found);
                LocalFunctions(code, masked, found);
            }
            Balance(found, code, masked);
            return found;
        }

        static readonly Regex MethodHead = new Regex(@"^(?:(?:static|async|unsafe|private|public|internal|protected)\s+)*(?!(?:if|for|foreach|while|switch|catch|using|lock|fixed|else|do|try|return|new|throw|await|yield)\b)[\w<>\[\],.?]+\s+[A-Za-z_]\w*\s*(?:<[^<>()]*>)?\s*\([^()]*\)\s*$");
        static readonly Regex TypeHead = new Regex(@"\b(?:class|struct|interface|enum|namespace)\b");

        // Methods declared inside a method body (local functions) need C# 7. Each { is a type body (class, struct,
        // interface, enum, namespace) or a code body; a method header in front of a { inside a code body is a local function.
        static void LocalFunctions(string code, string masked, List<CodeFinding> found)
        {
            Stack<bool> isCode = new Stack<bool>();
            int start = 0;   // start of the current header (after the last ; { or })
            for (int i = 0; i < masked.Length; i++)
            {
                char c = masked[i];
                if (c == ';') { start = i + 1; continue; }
                if (c == '}') { if (isCode.Count > 0) isCode.Pop(); start = i + 1; continue; }
                if (c != '{') continue;
                string head = masked.Substring(start, i - start).Trim();
                bool parentCode = isCode.Count > 0 && isCode.Peek();
                bool type = TypeHead.IsMatch(head);
                if (parentCode && !type && MethodHead.IsMatch(Regex.Replace(head, @"\s+", " ")))
                    Add(found, code, start + (masked.Substring(start, i - start).Length - masked.Substring(start, i - start).TrimStart().Length),
                        "a method declared inside another method (a local function) needs C# 7. Move it out as a private static method of the class.");
                isCode.Push(!type);
                start = i + 1;
            }
        }

        // Type.Member uses where Type is a framework type in scope and Member does not exist on it (in the framework this
        // server runs on: .NET Framework 4.8, only assemblies already loaded, nothing is loaded for the check).
        static void FrameworkMembers(string code, string masked, List<CodeFinding> found)
        {
            List<string> ns = new List<string>();
            ns.Add("System");
            foreach (Match u in Regex.Matches(masked, @"^\s*using\s+([\w.]+)\s*;", RegexOptions.Multiline)) if (!ns.Contains(u.Groups[1].Value)) ns.Add(u.Groups[1].Value);
            HashSet<string> defined = new HashSet<string>();
            foreach (Match d in Regex.Matches(masked, @"\b(?:class|struct|interface|enum|delegate\s+\w+)\s+([A-Za-z_]\w*)")) defined.Add(d.Groups[1].Value);
            foreach (Match d in Regex.Matches(masked, @"\b[A-Za-z_][\w<>\[\],]*\s+([A-Z]\w*)\s*[=;,)]")) defined.Add(d.Groups[1].Value);   // variables / parameters with capital names
            Dictionary<string, Type> cache = new Dictionary<string, Type>();
            foreach (Match m in Regex.Matches(masked, @"(?<![\w.])([A-Z]\w*|string|int|long|short|byte|double|float|decimal|bool|char|object|uint|ulong)\s*\.\s*([A-Z]\w*)\b"))
            {
                string tn = m.Groups[1].Value, member = m.Groups[2].Value;
                if (defined.Contains(tn)) continue;
                Type t;
                if (!cache.TryGetValue(tn, out t)) { string alias; t = CsAliases.TryGetValue(tn, out alias) ? Type.GetType(alias) : FindType(tn, ns); cache[tn] = t; }
                if (t == null) continue;
                if (t.GetMember(member, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy).Length > 0) continue;
                if (t.GetNestedType(member) != null) continue;
                string near = Nearest(member, MemberNames(t));
                Add(found, code, m.Index, tn + "." + member + " does not exist in .NET Framework 4.8" + (near != null ? " (did you mean " + tn + "." + near + "?)" : "") + ".");
            }
        }

        static readonly Dictionary<string, string> CsAliases = new Dictionary<string, string> {
            { "string", "System.String" }, { "int", "System.Int32" }, { "long", "System.Int64" }, { "short", "System.Int16" }, { "byte", "System.Byte" },
            { "double", "System.Double" }, { "float", "System.Single" }, { "decimal", "System.Decimal" }, { "bool", "System.Boolean" },
            { "char", "System.Char" }, { "object", "System.Object" }, { "uint", "System.UInt32" }, { "ulong", "System.UInt64" } };

        static Type FindType(string name, List<string> namespaces)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.IsDynamic) continue;
                string an = a.GetName().Name;
                if (an != "mscorlib" && !an.StartsWith("System")) continue;   // framework assemblies only
                foreach (string ns in namespaces)
                {
                    Type t = null;
                    try { t = a.GetType(ns + "." + name, false); } catch { }
                    if (t != null && t.IsPublic) return t;
                }
            }
            return null;
        }

        static List<string> MemberNames(Type t)
        {
            List<string> names = new List<string>();
            foreach (MemberInfo mi in t.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy))
                if (!mi.Name.StartsWith("get_") && !mi.Name.StartsWith("set_") && !names.Contains(mi.Name)) names.Add(mi.Name);
            return names;
        }

        // The closest name (edit distance at most a third of the length, prefix matches first), or null.
        static string Nearest(string name, IEnumerable<string> names)
        {
            string best = null; int bestD = int.MaxValue;
            string low = name.ToLowerInvariant();
            foreach (string c in names)
            {
                string cl = c.ToLowerInvariant();
                int d = cl.StartsWith(low) || low.StartsWith(cl) ? Math.Abs(cl.Length - low.Length) / 2 : Distance(low, cl);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best != null && bestD <= Math.Max(2, name.Length / 3) ? best : null;
        }

        static int Distance(string a, string b)
        {
            int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) prev[j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= b.Length; j++) cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                int[] tmp = prev; prev = cur; cur = tmp;
            }
            return prev[b.Length];
        }

        // ---- PS ----

        static string MaskPs(string code)
        {
            StringBuilder sb = new StringBuilder(code);
            int i = 0, n = code.Length;
            Action<int, int> blank = delegate(int a, int b) { for (int k = a; k < b && k < n; k++) if (code[k] != '\n') sb[k] = ' '; };
            while (i < n)
            {
                char c = code[i];
                if (c == '<' && i + 1 < n && code[i + 1] == '#') { int e = code.IndexOf("#>", i + 2); e = e < 0 ? n : e + 2; blank(i, e); i = e; continue; }
                if (c == '#') { int e = code.IndexOf('\n', i); e = e < 0 ? n : e; blank(i, e); i = e; continue; }
                if (c == '@' && i + 1 < n && (code[i + 1] == '"' || code[i + 1] == '\''))
                {
                    string close = "\n" + code[i + 1] + "@";
                    int e = code.IndexOf(close, i + 2); e = e < 0 ? n : e + close.Length; blank(i, e); i = e; continue;
                }
                if (c == '\'') { int j = i + 1; while (j < n) { if (code[j] == '\'') { if (j + 1 < n && code[j + 1] == '\'') { j += 2; continue; } break; } j++; } blank(i, j + 1); i = j + 1; continue; }
                if (c == '"') { int j = i + 1; while (j < n && code[j] != '"') { if (code[j] == '`') j++; j++; } blank(i, j + 1); i = j + 1; continue; }
                i++;
            }
            return sb.ToString();
        }

        sealed class PsCommand { public string Name; public List<string> Params = new List<string>(); }
        sealed class PsData { public Dictionary<string, PsCommand> Commands; public Dictionary<string, string> Aliases; }
        static PsData _ps51, _ps7;
        static readonly object _psLock = new object();
        // Set by the server: docs\checkers\ps51-commands.tsv; ps7-commands.tsv is read from the same folder.
        public static string PsDataPath;

        static PsData LoadPs(bool ps51)
        {
            lock (_psLock)
            {
                PsData d = ps51 ? _ps51 : _ps7;
                if (d != null) return d;
                d = new PsData();
                d.Commands = new Dictionary<string, PsCommand>(StringComparer.OrdinalIgnoreCase);
                d.Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string path = PsDataPath == null ? null : ps51 ? PsDataPath : Path.Combine(Path.GetDirectoryName(PsDataPath), "ps7-commands.tsv");
                if (path != null && File.Exists(path))
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab <= 0) continue;
                        string name = line.Substring(0, tab), rest = line.Substring(tab + 1);
                        if (rest.StartsWith("=")) { d.Aliases[name] = rest.Substring(1); continue; }
                        PsCommand c = new PsCommand(); c.Name = name;
                        foreach (string p in rest.Split(',')) foreach (string a in p.Split('|')) if (a.Length > 0) c.Params.Add(a.ToLowerInvariant());
                        d.Commands[name] = c;
                    }
                if (ps51) _ps51 = d; else _ps7 = d;
                return d;
            }
        }

        static readonly HashSet<string> PsOperators = new HashSet<string>(("eq ne gt ge lt le like notlike match notmatch contains notcontains in notin replace split join " +
            "and or not xor band bor bxor bnot shl shr is isnot as f creplace ireplace csplit isplit ceq cne cgt cge clt cle clike cnotlike cmatch cnotmatch " +
            "ccontains cnotcontains cin cnotin ieq ine igt ige ilt ile ilike inotlike imatch inotmatch icontains inotcontains iin inotin").Split(' '));

        public static List<CodeFinding> Ps(string code, bool ps51)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskPs(code);
            if (ps51)
            {
                foreach (Match m in Regex.Matches(masked, @"\?\?=?")) Add(found, code, m.Index, "the ?? / ??= operators need PS 7. Use: if ($null -eq $x) { $x = 'default' }.");
                foreach (Match m in Regex.Matches(masked, @"\}\?[.\[]")) Add(found, code, m.Index, "null-conditional member access (${x}?.Name) needs PS 7. Test for $null first.");
                foreach (Match m in Regex.Matches(masked, @"&&|\|\|")) Add(found, code, m.Index, "the && and || pipeline chain operators need PS 7. Use separate statements with if ($?) { ... } or -and / -or in conditions.");
                foreach (Match m in Regex.Matches(masked, @"[\w)\]]\s+\?\s+[^{\s|][^\n]*?\s:\s", RegexOptions.Multiline))
                    if (!Regex.IsMatch(masked.Substring(Math.Max(0, m.Index - 2), 2), @"\|")) Add(found, code, m.Index, "the ternary operator (a ? b : c) needs PS 7. Use if ($a) { b } else { c }.");
                foreach (Match m in Regex.Matches(masked, @"\$(IsWindows|IsLinux|IsMacOS|IsCoreCLR|PSStyle)\b")) Add(found, code, m.Index, "$" + m.Groups[1].Value + " only exists in PS 6+.");
            }
            else
                foreach (Match m in Regex.Matches(masked, @"\$(?![{(])[A-Za-z_]\w*\?\.")) Add(found, code, m.Index, "in PS 7 a null-conditional on a variable needs braces: ${name}?.Member ($name?. reads a variable named 'name?').");
            PsCommands(code, masked, found, ps51);
            Balance(found, code, masked);
            return found;
        }

        // Commands that do not exist (when a real command is a close match: an invented name), and parameters a command
        // does not have (unique prefixes and aliases are allowed, as the shell allows them).
        static void PsCommands(string code, string masked, List<CodeFinding> found, bool ps51)
        {
            PsData data = LoadPs(ps51);
            if (data.Commands.Count == 0) return;
            string shell = ps51 ? "Windows PS 5.1" : "PS 7";
            HashSet<string> defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match d in Regex.Matches(masked, @"\bfunction\s+([\w-]+)", RegexOptions.IgnoreCase)) defined.Add(d.Groups[1].Value);
            foreach (Match m in Regex.Matches(masked, @"(?<![\w$.\-])([A-Za-z]+-[A-Za-z][A-Za-z0-9]*)\b(?!\s*=)"))
            {
                string name = m.Groups[1].Value;
                if (defined.Contains(name)) continue;
                // the command must stand in command position: line start, after | ; { ( = or a keyword
                string before = masked.Substring(0, m.Index).TrimEnd(' ', '\t');
                if (before.Length > 0 && "|;{(=\n".IndexOf(before[before.Length - 1]) < 0 && !Regex.IsMatch(before, @"(\b(return|throw|if|elseif|while|foreach)|&|\.)\s*$")) continue;
                PsCommand cmd;
                string target;
                if (!data.Commands.TryGetValue(name, out cmd) && data.Aliases.TryGetValue(name, out target)) data.Commands.TryGetValue(target, out cmd);
                if (cmd == null)
                {
                    // only a near-identical name counts as invented (a typo or a made-up variant); others may be from
                    // modules or platforms this list does not have
                    string near = Nearest(name, data.Commands.Keys);
                    if (near != null && Distance(name.ToLowerInvariant(), near.ToLowerInvariant()) <= 2)
                        Add(found, code, m.Index, name + " is not a " + shell + " command (did you mean " + near + "?).");
                    continue;
                }
                foreach (KeyValuePair<int, string> p in ParamsAfter(masked, m.Index + name.Length))
                {
                    string pn = p.Value.ToLowerInvariant();
                    if (cmd.Params.Contains(pn) || PsOperators.Contains(pn)) continue;
                    int prefix = 0;
                    foreach (string cp in cmd.Params) if (cp.StartsWith(pn)) prefix++;
                    if (prefix == 1) continue;
                    string near = Nearest(pn, cmd.Params);
                    Add(found, code, p.Key, cmd.Name + " has no parameter -" + p.Value + " in " + shell +
                        (prefix > 1 ? " (it is ambiguous: write the full name)" : near != null ? " (did you mean -" + near + "?)" : "") + ".");
                }
            }
        }

        // Parameter names (-Name) given to the command that ends at `from`, up to the end of its pipeline element; nested
        // (...), {...} and [...] are skipped (they hold other commands).
        static List<KeyValuePair<int, string>> ParamsAfter(string masked, int from)
        {
            List<KeyValuePair<int, string>> ps = new List<KeyValuePair<int, string>>();
            int depth = 0;
            for (int i = from; i < masked.Length; i++)
            {
                char c = masked[i];
                if (c == '(' || c == '{' || c == '[') { depth++; continue; }
                if (c == ')' || c == '}' || c == ']') { if (depth == 0) break; depth--; continue; }
                if (depth > 0) continue;
                if (c == '|' || c == ';') break;
                if (c == '\n') { if (i > 0 && masked[i - 1] == '`') continue; break; }
                if (c == '-' && (i == 0 || char.IsWhiteSpace(masked[i - 1])))
                {
                    Match m = Regex.Match(masked.Substring(i + 1, Math.Min(64, masked.Length - i - 1)), @"^([A-Za-z][A-Za-z0-9]*)(?=[\s:]|$)");
                    if (m.Success) ps.Add(new KeyValuePair<int, string>(i, m.Groups[1].Value));
                }
            }
            return ps;
        }

        // ---- SQL ----

        static string MaskSql(string code)
        {
            StringBuilder sb = new StringBuilder(code);
            int i = 0, n = code.Length;
            while (i < n)
            {
                char c = code[i];
                if (c == '-' && i + 1 < n && code[i + 1] == '-') { while (i < n && code[i] != '\n') sb[i++] = ' '; continue; }
                if (c == '/' && i + 1 < n && code[i + 1] == '*') { int e = code.IndexOf("*/", i + 2); e = e < 0 ? n : e + 2; for (; i < e; i++) if (code[i] != '\n') sb[i] = ' '; continue; }
                if (c == '\'')
                {
                    int j = i + 1;
                    while (j < n) { if (code[j] == '\'') { if (j + 1 < n && code[j + 1] == '\'') { j += 2; continue; } break; } j++; }
                    for (int k = i + 1; k < j && k < n; k++) if (code[k] != '\n') sb[k] = ' ';   // keep the quotes: '' stays visible
                    if (j >= n) return null;   // an unclosed string
                    i = j + 1; continue;
                }
                i++;
            }
            return sb.ToString();
        }

        // (pattern, problem) per dialect; "*" applies to Oracle and DuckDB (both differ from SQL Server and MySQL here).
        static readonly string[,] SqlRules = {
            { "Oracle SQL", @"\bSELECT\s+TOP\s+\d", "SELECT TOP n is SQL Server syntax. Oracle 12c+: ORDER BY ... FETCH FIRST n ROWS ONLY." },
            { "Oracle SQL", @"(?<!\bBULK\s+COLLECT\s+INTO\s+[\w$#.]+(?:\s*,\s*[\w$#.]+)*\s+)\bLIMIT\s+\d", "LIMIT is not Oracle syntax. Use FETCH FIRST n ROWS ONLY (12c+) or OFFSET m ROWS FETCH NEXT n ROWS ONLY." },
            { "Oracle SQL", @"\bISNULL\s*\(", "ISNULL is SQL Server. Use NVL(x, default) or COALESCE." },
            { "Oracle SQL", @"\bIFNULL\s*\(", "IFNULL is MySQL. Use NVL(x, default) or COALESCE." },
            { "Oracle SQL", @"\bGETDATE\s*\(|\bNOW\s*\(\s*\)|\bCURRENT_DATE\s*\(\s*\)", "use SYSDATE (date with time) or SYSTIMESTAMP in Oracle." },
            { "Oracle SQL", @"\bAUTO_INCREMENT\b|\bIDENTITY\s*\(\s*\d|\bSERIAL\b", "use GENERATED ALWAYS AS IDENTITY (12c+) or a sequence in Oracle." },
            { "Oracle SQL", @"\bDATEADD\s*\(|\bDATEDIFF\s*\(", "DATEADD / DATEDIFF are SQL Server. Oracle: date + n (days), ADD_MONTHS(d, n), d2 - d1 (days), MONTHS_BETWEEN." },
            { "Oracle SQL", @"\bLEN\s*\(|\bCHARINDEX\s*\(", "use LENGTH(s) and INSTR(s, sub) in Oracle." },
            { "Oracle SQL", @"\bN?VARCHAR\s*\(\s*MAX\s*\)", "VARCHAR(MAX) is SQL Server. Use VARCHAR2(4000) or CLOB." },
            { "Oracle SQL", @"(=|<>|!=)\s*''", "in Oracle '' is NULL, so = '' is never true. Use IS NULL / IS NOT NULL." },
            { "Oracle SQL", @"'\s*\+\s*\w|\w\s*\+\s*'", "strings are joined with || in Oracle, not +." },
            { "Oracle SQL", @"\bDECLARE\s+@|\B@\w+\s*=", "@variables are T-SQL. PL/SQL declares v_name TYPE; in DECLARE and binds :name." },
            { "Oracle SQL", @"\bBOOLEAN\b(?=[^;]*\b(CREATE\s+TABLE|ALTER\s+TABLE)\b)|\bCREATE\s+TABLE\b[^;]*\bBOOLEAN\b", "Oracle 19c tables have no BOOLEAN type. Use NUMBER(1) or CHAR(1)." },
            { "Oracle SQL", @"^\s*SELECT\b(?![\s\S]*?\bFROM\b)[^;]*;", "a SELECT needs FROM in Oracle: use FROM DUAL." },
            { "DuckDB SQL", @"\bSELECT\s+TOP\s+\d", "SELECT TOP n is SQL Server syntax. DuckDB: ... LIMIT n." },
            { "DuckDB SQL", @"\bISNULL\s*\(", "ISNULL is SQL Server. Use COALESCE(x, default) (or IFNULL) in DuckDB." },
            { "DuckDB SQL", @"\bGETDATE\s*\(|\bSYSDATE\b", "use current_date / now() / current_timestamp in DuckDB." },
            { "DuckDB SQL", @"\bDATEADD\s*\(", "DATEADD is SQL Server. DuckDB: date + INTERVAL 7 DAY, date_add(d, INTERVAL ...), date_diff('day', a, b)." },
            { "DuckDB SQL", @"\bAUTO_?INCREMENT\b|\bIDENTITY\s*\(\s*\d|\bSERIAL\b", "DuckDB has no AUTO_INCREMENT / AUTOINCREMENT / IDENTITY / SERIAL: CREATE SEQUENCE s; ... DEFAULT nextval('s')." },
            { "DuckDB SQL", @"\b(?:DATE|DATETIME)\s*\(\s*'now'", "DATE('now') is SQLite. DuckDB: current_date (or now() / current_timestamp); 30 days ago: current_date - INTERVAL 30 DAY." },
            { "DuckDB SQL", @"\bdate_sub\s*\(\s*(?!')", "DuckDB's date_sub('part', start, end) counts part boundaries between two dates; to go back in time subtract an interval: current_date - INTERVAL 30 DAY." },
            { "DuckDB SQL", @"\bDATE_ADD\s*\(\s*\w+\s*,\s*\d", "DATE_ADD(part, n, d) is not DuckDB syntax. Use d + INTERVAL n DAY (or date_add(d, INTERVAL n DAY))." },
            { "DuckDB SQL", @"\bNVL\s*\(", "NVL is Oracle. Use COALESCE in DuckDB." },
            { "DuckDB SQL", @"\bN?VARCHAR\s*\(\s*MAX\s*\)", "VARCHAR(MAX) is SQL Server. DuckDB's VARCHAR has no length: VARCHAR." },
            { "DuckDB SQL", @"\bDECLARE\s+@|\B@\w+\s*=", "@variables are T-SQL. DuckDB uses SET VARIABLE x = ...; getvariable('x') or a CTE." },
            { "DuckDB SQL", @"\bFROM\s+DUAL\b", "DuckDB needs no FROM DUAL: SELECT 1;" },
            { "SQL", @"\bDECLARE\s+@\w+[^;]*\bLIMIT\b|\bSELECT\s+TOP\s+\d[^;]*\bLIMIT\s+\d", "TOP and LIMIT mixed: pick one dialect (SQL Server TOP, MySQL / PostgreSQL / SQLite LIMIT)." } };

        public static List<CodeFinding> Sql(string code, string dialect)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskSql(code);
            if (masked == null) { Add(found, code, code.LastIndexOf('\''), "a string literal ('...') is never closed."); return found; }
            // a rule that looks inside string literals ('now') runs on the code without comments; the others without strings
            string noComments = Regex.Replace(code, @"--[^\n]*|/\*[\s\S]*?\*/", delegate(Match c) { return Regex.Replace(c.Value, @"[^\n]", " "); });
            for (int i = 0; i < SqlRules.GetLength(0); i++)
            {
                if (SqlRules[i, 0] != dialect) continue;
                bool inStrings = Regex.IsMatch(SqlRules[i, 1], @"'\w");
                foreach (Match m in Regex.Matches(inStrings ? noComments : masked, SqlRules[i, 1], RegexOptions.IgnoreCase | RegexOptions.Multiline))
                    Add(found, code, m.Index + (m.Value.Length - m.Value.TrimStart().Length), SqlRules[i, 2]);
            }
            Balance(found, code, masked);
            return found;
        }

        // ---- rule-checked languages: VBScript / VBA, CMD, DAX, Power Query M, Qlik, Excel formulas, HTML, CSS ----

        // Strings and comments blanked out (newlines kept). line: comment starters to the end of the line ("^" prefix =
        // only at the start of a line); block: {open, close} comment pairs; quotes: string delimiters (doubled = escaped).
        static string MaskGeneric(string code, string[] line, string[,] block, string quotes)
        {
            StringBuilder sb = new StringBuilder(code);
            int i = 0, n = code.Length;
            while (i < n)
            {
                bool done = false;
                foreach (string lc in line)
                {
                    bool atStart = lc.StartsWith("^");
                    string tok = atStart ? lc.Substring(1) : lc;
                    if (string.Compare(code, i, tok, 0, tok.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
                    if (tok == "//" && i > 0 && code[i - 1] == ':') continue;   // a scheme separator (Qlik's lib:// paths), not a comment
                    if (atStart)
                    {
                        int ls = i > 0 ? code.LastIndexOf('\n', i - 1) + 1 : 0;
                        if (code.Substring(ls, i - ls).Trim().Length > 0) continue;   // not the first thing on its line
                    }
                    int e = code.IndexOf('\n', i); e = e < 0 ? n : e;
                    for (int k = i; k < e; k++) sb[k] = ' ';
                    i = e; done = true; break;
                }
                if (done) continue;
                for (int b = 0; b < block.GetLength(0) && !done; b++)
                {
                    if (string.CompareOrdinal(code, i, block[b, 0], 0, block[b, 0].Length) != 0) continue;
                    int e = code.IndexOf(block[b, 1], i + block[b, 0].Length); e = e < 0 ? n : e + block[b, 1].Length;
                    for (int k = i; k < e; k++) if (code[k] != '\n') sb[k] = ' ';
                    i = e; done = true;
                }
                if (done) continue;
                char c = code[i];
                if (quotes.IndexOf(c) >= 0)
                {
                    int j = i + 1;
                    while (j < n) { if (code[j] == c) { if (j + 1 < n && code[j + 1] == c) { j += 2; continue; } break; } if (code[j] == '\n') break; j++; }
                    for (int k = i + 1; k < j && k < n; k++) if (code[k] != '\n') sb[k] = ' ';   // the quotes stay
                    i = j + 1; continue;
                }
                i++;
            }
            return sb.ToString();
        }

        static void Rules(List<CodeFinding> found, string code, string masked, string[,] rules, RegexOptions opt)
        {
            for (int i = 0; i < rules.GetLength(0); i++)
                foreach (Match m in Regex.Matches(masked, rules[i, 0], opt | RegexOptions.Multiline))
                    Add(found, code, m.Index + (m.Value.Length - m.Value.TrimStart().Length), rules[i, 1]);
        }

        static readonly string[,] VbScriptRules = {
            { @"\bDim\s+[\w(), ]*?\bAs\s+(?:New\s+)?\w", "VBScript has no types: Dim x (not Dim x As ...). As types are VBA." },
            { @"\b(?:Sub|Function)\s+\w+\s*\([^)\n]*\bAs\b|\)\s*As\s+\w+\s*$", "VBScript parameters and functions have no As types: Function F(a), Sub S(a)." },
            { @"\bOn\s+Error\s+GoTo\s+(?!0\b)\w+", "VBScript has no On Error GoTo label: use On Error Resume Next, then test Err.Number." },
            { @"^\s*(?!Set\b)\w+\s*=\s*(?:CreateObject|GetObject)\s*\(", "an object assignment needs Set: Set x = CreateObject(...)." } };
        static readonly string[,] VbRules = {
            { @"(?<![<>=!])(?:\+|-|\*|/|&)=", "VB has no compound assignment: x = x + 1." },
            { @"&&|\|\||!=|==", "VB uses And, Or, <> and = (not &&, ||, != or ==)." },
            { @"^\s*Return\s+\S", "a VB function returns by assigning its name: FunctionName = value, then Exit Function." },
            { @"^\s*(?:Try|Catch|Finally)\b", "VB has no try / catch: use On Error ... and check Err.Number." },
            { @"\bContinue\s+(?:For|Do)\b", "VB has no Continue: wrap the rest of the loop body in an If." } };

        public static List<CodeFinding> Vb(string code, bool vbscript)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskGeneric(code, new[] { "'", "^rem " }, new string[0, 2], "\"");
            Rules(found, code, masked, VbRules, RegexOptions.IgnoreCase);
            if (vbscript) Rules(found, code, masked, VbScriptRules, RegexOptions.IgnoreCase);
            else
            {
                // object variables assigned without Set
                foreach (Match d in Regex.Matches(masked, @"\b(?:Dim|Private|Public)\s+(\w+)\s+As\s+(?:Range|Worksheet|Workbook|Object|Collection|Chart|Shape|ListObject|PivotTable|Dictionary|Window|Name)\b", RegexOptions.IgnoreCase))
                    foreach (Match a in Regex.Matches(masked, @"^\s*" + d.Groups[1].Value + @"\s*=\s*\S", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                        Add(found, code, a.Index + (a.Value.Length - a.Value.TrimStart().Length), "an object variable needs Set: Set " + d.Groups[1].Value + " = ...");
            }
            // blocks that must be closed: multi-line If, For, Do, While, Sub, Function, Select Case, With
            string[,] pairs = { { @"^\s*If\b.*\bThen\s*$", @"^\s*End\s+If\b", "If ... Then without End If" }, { @"^\s*For\b", @"^\s*Next\b", "For without Next" },
                { @"^\s*Do\b", @"^\s*Loop\b", "Do without Loop" }, { @"^\s*While\b", @"^\s*Wend\b", "While without Wend" },
                { @"^\s*(?:(?:Public|Private|Static)\s+)?Sub\b", @"^\s*End\s+Sub\b", "Sub without End Sub" },
                { @"^\s*(?:(?:Public|Private|Static)\s+)?Function\b", @"^\s*End\s+Function\b", "Function without End Function" },
                { @"^\s*Select\s+Case\b", @"^\s*End\s+Select\b", "Select Case without End Select" }, { @"^\s*With\b", @"^\s*End\s+With\b", "With without End With" } };
            for (int i = 0; i < pairs.GetLength(0); i++)
            {
                MatchCollection open = Regex.Matches(masked, pairs[i, 0], RegexOptions.Multiline | RegexOptions.IgnoreCase), close = Regex.Matches(masked, pairs[i, 1], RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (open.Count != close.Count)
                    Add(found, code, (open.Count > close.Count ? open[open.Count - 1] : close[close.Count - 1]).Index,
                        pairs[i, 2] + ": " + open.Count + " opened, " + close.Count + " closed.");
            }
            Balance(found, code, Regex.Replace(masked, @"[\[\]{}]", " "));
            return found;
        }

        static readonly string[,] CmdRules = {
            { @"^\s*set\s+(?!/[ap]\b)""?[^=\s""]+\s+=", "set x = 5 makes a variable named \"x \" (with the space). Write set \"x=5\" (numbers: set /a x=5)." },
            { @"\bfor\s+(?:/[a-z]\s+(?:""[^""\n]*""\s+)?)?%[a-z]\b(?!%)", "in a .bat / .cmd file the loop variable is doubled: for %%f in (...) do ... %%f." },
            { @"\bif\s+errorlevel\s+0\b", "if errorlevel 0 is always true (it means >= 0). Use if %errorlevel% neq 0 (...) or if errorlevel 1." },
            { @"^\s*(?:rm|cp|mv|ls|touch|chmod|export|grep)\s", "this is a Unix command, not cmd: del, copy / robocopy, move, dir, type nul >, findstr." },
            { @"(?<![\w\\%!])\$[A-Za-z_]\w*", "batch variables are %name% (or !name! with delayed expansion), not $name." } };

        public static List<CodeFinding> Cmd(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskGeneric(code, new[] { "^rem ", "^::", "^@rem " }, new string[0, 2], "\"");
            Rules(found, code, masked, CmdRules, RegexOptions.IgnoreCase);
            Balance(found, code, Regex.Replace(Regex.Replace(masked, @"\^[()]", "  "), @"[\[\]{}]", " "));   // ^( is an escaped paren
            return found;
        }

        public static List<CodeFinding> Dax(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskGeneric(code, new[] { "//", "--" }, new string[,] { { "/*", "*/" } }, "\"'");
            Rules(found, code, masked, new string[,] {
                { @"!=", "DAX's not-equal is <>." },
                { @"\bBLANK\b(?!\s*\()", "BLANK is a function: BLANK()." },
                { @"\bSUM\s*\(\s*[^()]*?(?:\]|\))\s*[-+*/]\s*[^()]*\)", "SUM takes one column. For an expression per row use SUMX(Table, Table[A] * Table[B])." } }, RegexOptions.IgnoreCase);
            foreach (Match m in Regex.Matches(masked, @"\b(AND|OR)\s*\(", RegexOptions.IgnoreCase))
                if (TopLevelArgs(masked, m.Index + m.Length) > 2) Add(found, code, m.Index, m.Groups[1].Value.ToUpperInvariant() + "() takes exactly two arguments in DAX: nest them or use && / ||.");
            if (Regex.IsMatch(masked, @"\bVAR\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(masked, @"\bRETURN\b", RegexOptions.IgnoreCase))
                Add(found, code, Regex.Match(masked, @"\bVAR\b", RegexOptions.IgnoreCase).Index, "VAR needs a RETURN: VAR x = ... RETURN expression.");
            Balance(found, code, masked);
            return found;
        }

        // Arguments of the call whose ( ends at `from`, counted at the top level.
        static int TopLevelArgs(string masked, int from)
        {
            int depth = 0, args = 1;
            for (int i = from; i < masked.Length; i++)
            {
                char c = masked[i];
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') { if (depth == 0) return args; depth--; }
                else if (c == ',' && depth == 0) args++;
            }
            return args;
        }

        public static List<CodeFinding> PowerQuery(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskGeneric(code, new[] { "//" }, new string[,] { { "/*", "*/" } }, "\"");
            Rules(found, code, masked, new string[,] {
                { @"!=|==|&&|\|\|", "M uses <>, =, and, or (not !=, ==, &&, ||)." },
                { @"\bTable\.SelectRows\s*\([^,()]+,\s*\[", "Table.SelectRows needs a function as the condition: each [Column] > 0." },
                { @"\bTable\.AddColumn\s*\([^,()]+,\s*""[^""\n]*""\s*,\s*\[", "Table.AddColumn needs a function: each [A] * [B]." },
                { @",\s*\n\s*in\b", "no comma after the last step before in." },
                { @"(?<![\w.#])(?:True|False|Null|TRUE|FALSE|NULL)\b", "M is case-sensitive: true, false, null." },
                { @"\b(?:table|text|list|number|date|record|csv|excel)\.[A-Za-z]", "M library names are case-sensitive: Table.SelectRows, Text.Contains, List.Sum ..." } }, RegexOptions.None);
            int ifs = Regex.Matches(masked, @"\bif\b").Count, elses = Regex.Matches(masked, @"\belse\b").Count;
            if (ifs > elses) Add(found, code, Regex.Matches(masked, @"\bif\b")[ifs - 1].Index, "every if in M needs an else: if c then a else b.");
            Balance(found, code, masked);
            return found;
        }

        public static List<CodeFinding> Qlik(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskGeneric(code, new[] { "//", "^rem " }, new string[,] { { "/*", "*/" } }, "'\"");
            Rules(found, code, masked, new string[,] {
                { @"!=|==|&&|\|\|", "Qlik script uses <>, =, and, or." },
                { @"\bSELECT\b[^;]*\bFROM\s+\[?[^\s;\]]+\.(?:csv|txt|xlsx?|qvd)\b", "files are loaded with LOAD ... FROM [lib://...] (format spec); SELECT goes to a database connection." },
                { @"(?<![\w$])\$(?!\()[A-Za-z_]\w*", "variables expand as $(vName)." } }, RegexOptions.IgnoreCase);
            // a table label (Name:) must follow a finished statement
            string[] lines = masked.Split('\n');
            int pos = 0; string prev = "";
            foreach (string l in lines)
            {
                string t = l.Trim();
                if (Regex.IsMatch(t, @"^[\w\[\] ]+:$") && prev.Length > 0 && !prev.EndsWith(";")) Add(found, code, pos, "the statement before this table label does not end with ;.");
                if (t.Length > 0) prev = t;
                pos += l.Length + 1;
            }
            Balance(found, code, masked);
            return found;
        }

        public static List<CodeFinding> ExcelFormulas(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskGeneric(code, new string[0], new string[0, 2], "\"");
            Rules(found, code, masked, new string[,] {
                { @"==|!=|&&|\|\|", "Excel uses =, <>, AND(), OR() (not ==, !=, &&, ||)." },
                { @"[(,=&]\s*'[^'\n]*'(?!!)", "text in Excel formulas uses double quotes; single quotes only wrap sheet names ('My Sheet'!A1)." } }, RegexOptions.None);
            foreach (Match m in Regex.Matches(masked, @"\bVLOOKUP\s*\(", RegexOptions.IgnoreCase))
                if (TopLevelArgs(masked, m.Index + m.Length) == 3) Add(found, code, m.Index, "VLOOKUP without a 4th argument does an approximate match (wrong results on unsorted data): add FALSE.");
            foreach (Match m in Regex.Matches(code, @"""[<>=]{1,2}\$?[A-Z]{1,3}\$?\d+"""))
                Add(found, code, m.Index, "a criterion compared to a cell is built by joining: \">\"&B1, not \">B1\".");
            Balance(found, code, masked);
            return found;
        }

        static readonly HashSet<string> HtmlVoid = new HashSet<string>("area base br col embed hr img input link meta source track wbr".Split(' '));
        static readonly HashSet<string> HtmlOptionalEnd = new HashSet<string>("p li dt dd tr td th thead tbody tfoot option optgroup colgroup caption html head body rt rp".Split(' '));

        public static List<CodeFinding> Html(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            // comments, and the contents of script / style elements, are not markup
            string masked = Regex.Replace(code, @"<!--[\s\S]*?-->", delegate(Match m) { return Regex.Replace(m.Value, @"[^\n]", " "); });
            masked = Regex.Replace(masked, @"(<(script|style)\b[^>]*>)([\s\S]*?)(</\2\s*>)", delegate(Match m)
            {
                return m.Groups[1].Value + Regex.Replace(m.Groups[3].Value, @"[^\n]", " ") + m.Groups[4].Value;
            }, RegexOptions.IgnoreCase);
            Stack<KeyValuePair<string, int>> open = new Stack<KeyValuePair<string, int>>();
            Dictionary<string, int> ids = new Dictionary<string, int>();
            foreach (Match m in Regex.Matches(masked, @"<(/?)([a-zA-Z][\w-]*)((?:[^>""']|""[^""]*""|'[^']*')*?)(/?)>"))
            {
                string name = m.Groups[2].Value.ToLowerInvariant();
                bool close = m.Groups[1].Length > 0;
                if (HtmlVoid.Contains(name))
                {
                    if (close) Add(found, code, m.Index, "<" + name + "> is a void element: it has no closing tag.");
                    else if (name == "img" && !Regex.IsMatch(m.Groups[3].Value, @"\balt\s*=", RegexOptions.IgnoreCase)) Add(found, code, m.Index, "<img> needs an alt attribute (alt=\"\" when decorative).");
                }
                else if (!close && m.Groups[4].Length == 0) open.Push(new KeyValuePair<string, int>(name, m.Index));
                else if (close)
                {
                    // pop elements whose end tag may be left out until the match
                    while (open.Count > 0 && open.Peek().Key != name && HtmlOptionalEnd.Contains(open.Peek().Key)) open.Pop();
                    if (open.Count > 0 && open.Peek().Key == name) open.Pop();
                    else if (!HtmlOptionalEnd.Contains(name)) Add(found, code, m.Index, "</" + name + "> does not close the element that is open here" + (open.Count > 0 ? " (<" + open.Peek().Key + ">)" : "") + ".");
                }
                Match id = Regex.Match(m.Groups[3].Value, @"\bid\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase);
                if (id.Success && !close)
                {
                    int first;
                    if (ids.TryGetValue(id.Groups[1].Value, out first)) Add(found, code, m.Index, "id \"" + id.Groups[1].Value + "\" is used twice (ids must be unique).");
                    else ids[id.Groups[1].Value] = m.Index;
                }
            }
            foreach (KeyValuePair<string, int> o in open)
                if (!HtmlOptionalEnd.Contains(o.Key)) Add(found, code, o.Value, "<" + o.Key + "> is never closed.");
            return found;
        }

        public static List<CodeFinding> Css(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            string masked = MaskGeneric(code, new string[0], new string[,] { { "/*", "*/" } }, "\"'");
            Rules(found, code, masked, new string[,] {
                { @"^\s*//", "// is not a CSS comment (it breaks the next rule): use /* ... */." },
                { @"(?<![\w-])(?:text-color|font-color|background-colour|colour|border-colour|text-colour|font-colour)\s*:", "not a CSS property: use color / background-color / border-color." },
                { @"(?<![\w-])(?:width|height|min-width|max-width|min-height|max-height|margin(?:-(?:top|right|bottom|left))?|padding(?:-(?:top|right|bottom|left))?|top|right|bottom|left|font-size|gap|row-gap|column-gap|border-radius|border-width|outline-width|letter-spacing|text-indent)\s*:\s*-?(?:[1-9]\d*|\d+\.\d+)\s*(?:;|}|!|$)", "a length other than 0 needs a unit (px, rem, %, em ...)." },
                { @"\d\s+(?:px|em|rem|vh|vw|pt)\b", "no space between a number and its unit: 10px." },
                { @"calc\([^)]*\d(?:px|%|em|rem|vh|vw)?[+-]\d", "inside calc(), + and - need spaces on both sides: calc(100% - 20px)." },
                { @";\s*!important", "!important goes before the semicolon: color: red !important;" },
                { @"(?<![\w-])[a-z-]+\s*=\s*[^;{}=]+;", "a declaration is property: value; (with a colon)." } }, RegexOptions.IgnoreCase);
            Balance(found, code, masked);
            return found;
        }

        // ---- JavaScript ----

        public static List<CodeFinding> JavaScript(string code)
        {
            List<CodeFinding> found = new List<CodeFinding>();
            code = code.Replace("\r\n", "\n");
            StringBuilder sb = new StringBuilder(code);
            int i = 0, n = code.Length;
            char prevSig = '\0';
            while (i < n)
            {
                char c = code[i];
                if (c == '/' && i + 1 < n && code[i + 1] == '/') { while (i < n && code[i] != '\n') sb[i++] = ' '; continue; }
                if (c == '/' && i + 1 < n && code[i + 1] == '*') { int e = code.IndexOf("*/", i + 2); e = e < 0 ? n : e + 2; for (; i < e; i++) if (code[i] != '\n') sb[i] = ' '; continue; }
                if (c == '"' || c == '\'' || c == '`' || (c == '/' && "(=,:[!&|?{};+-*%<>~^".IndexOf(prevSig) >= 0))
                {
                    int j = i + 1; bool cls = false;
                    while (j < n)
                    {
                        char d = code[j];
                        if (d == '\\') { j += 2; continue; }
                        if (c == '/') { if (d == '[') cls = true; else if (d == ']') cls = false; else if (d == '/' && !cls) break; if (d == '\n') break; }
                        else if (d == c) break;
                        else if (d == '\n' && c != '`') break;
                        j++;
                    }
                    for (int k = i; k <= j && k < n; k++) if (code[k] != '\n') sb[k] = ' ';
                    i = j + 1; prevSig = 'a'; continue;
                }
                if (!char.IsWhiteSpace(c)) prevSig = c;
                i++;
            }
            Balance(found, code, sb.ToString());
            return found;
        }
    }
}
