// Skills: reusable instructions in the Agent Skills format. A skill is a folder holding SKILL.md:
//   ---
//   name: oracle-plsql            lowercase letters, digits and single hyphens, at most 64 characters, = folder name
//   description: What it does and when to use it (third person, at most 1024 characters, no angle brackets).
//   ---
//   Markdown instructions (the body), optionally pointing to supporting files next to it (reference.md, examples.md).
// App skills live in <app>\skills\<name>, project skills in <project>\.phoenix\skills\<name> (a project skill with the
// same name wins). The system prompt lists every skill's name and description; a request that calls a skill (/name,
// "use the name skill") or matches its name and description gets the skill's body, plus the supporting files the body
// names, in front of the newest user message (like the docs excerpts, so the cached prompt prefix stays valid).
// Small models do not pick skills reliably on their own, so the server does the matching. C# 5 only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoenix
{
    public sealed class Skill
    {
        public string Name = "", Description = "", Body = "", Dir, Scope;   // Scope: "app" or "project"
        public List<string> Errors = new List<string>(), Warnings = new List<string>();
        public List<string> Files = new List<string>();   // supporting files next to SKILL.md (names only)
        public bool Valid { get { return Errors.Count == 0; } }
    }

    public sealed class SkillMatch
    {
        public Skill Skill; public double Score; public bool Explicit; public List<string> Terms = new List<string>();
    }

    public static class Skills
    {
        public const int MaxName = 64, MaxDescription = 1024, MaxBodyLines = 500;
        const int MaxBodyChars = 12000, MaxFileChars = 6000, MaxInjected = 2;
        // Letters, digits and single hyphens (code-review): one character per step, not a nested repeater.
        // The matcher gives up after one second.
        static readonly Regex NameRe = new Regex(@"^[a-z0-9](-?[a-z0-9])*$", RegexOptions.None, TimeSpan.FromSeconds(1));
        public static readonly Regex FileNameRe = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\.(md|txt)$", RegexOptions.IgnoreCase);

        // The name rule, without the length cap. A matcher timeout is a bad name, not a crash.
        static bool NameMatches(string name)
        {
            if (name == null) return false;
            try { return NameRe.IsMatch(name); }
            catch (RegexMatchTimeoutException) { return false; }
        }

        // A request's skill folder name: at most MaxName characters, then the name rule.
        // The length bound runs first so a huge query never reaches the matcher.
        public static bool IsSkillName(string name)
        {
            if (name == null || name.Length == 0 || name.Length > MaxName) return false;
            return NameMatches(name);
        }

        // ---- parsing and validation ----

        // Splits SKILL.md text into its frontmatter fields and body, and checks it against the format.
        public static Skill Parse(string text)
        {
            Skill s = new Skill();
            text = (text ?? "").Replace("\r\n", "\n");
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            Dictionary<string, string> fm = null;
            if (text.StartsWith("---\n"))
            {
                int end = text.IndexOf("\n---", 3);
                if (end >= 0)
                {
                    fm = Frontmatter(text.Substring(4, Math.Max(0, end - 3)));
                    int after = text.IndexOf('\n', end + 4);
                    s.Body = after < 0 ? "" : text.Substring(after + 1);
                }
            }
            if (fm == null) { s.Errors.Add("SKILL.md must start with a frontmatter block: a line with ---, then name: and description:, then ---."); s.Body = text; fm = new Dictionary<string, string>(); }
            string v;
            if (fm.TryGetValue("name", out v)) s.Name = v.Trim();
            if (fm.TryGetValue("description", out v)) s.Description = v.Trim();
            s.Body = s.Body.Trim('\n');
            Check(s);
            return s;
        }

        // Simple YAML: "key: value", quoted values, and folded (>) or literal (|) blocks of indented lines.
        static Dictionary<string, string> Frontmatter(string block)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = block.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                Match m = Regex.Match(lines[i], @"^([A-Za-z][\w-]*)\s*:\s*(.*)$");
                if (!m.Success) continue;
                string key = m.Groups[1].Value, val = m.Groups[2].Value.Trim();
                if (val == ">" || val == "|" || val == ">-" || val == "|-")
                {
                    StringBuilder sb = new StringBuilder();
                    while (i + 1 < lines.Length && (lines[i + 1].StartsWith(" ") || lines[i + 1].StartsWith("\t") || lines[i + 1].Trim().Length == 0))
                    {
                        i++;
                        if (sb.Length > 0) sb.Append(val[0] == '>' ? ' ' : '\n');
                        sb.Append(lines[i].Trim());
                    }
                    val = sb.ToString().Trim();
                }
                else if (val.Length >= 2 && ((val[0] == '"' && val[val.Length - 1] == '"') || (val[0] == '\'' && val[val.Length - 1] == '\'')))
                    val = val.Substring(1, val.Length - 2).Replace("\\\"", "\"").Replace("''", "'");
                else
                {
                    // a plain value may continue on indented lines
                    while (i + 1 < lines.Length && (lines[i + 1].StartsWith("  ") || lines[i + 1].StartsWith("\t")) && !Regex.IsMatch(lines[i + 1], @"^\s+[\w-]+\s*:")) { i++; val += " " + lines[i].Trim(); }
                }
                d[key] = val;
            }
            return d;
        }

        static void Check(Skill s)
        {
            if (s.Name.Length == 0) s.Errors.Add("name is missing.");
            else
            {
                if (s.Name.Length > MaxName) s.Errors.Add("name is longer than " + MaxName + " characters.");
                if (!NameMatches(s.Name)) s.Errors.Add("name may only use lowercase letters, digits and single hyphens (like code-review).");
            }
            if (s.Description.Length == 0) s.Errors.Add("description is missing: say what the skill does and when to use it.");
            else
            {
                if (s.Description.Length > MaxDescription) s.Errors.Add("description is longer than " + MaxDescription + " characters.");
                if (s.Description.IndexOf('<') >= 0 || s.Description.IndexOf('>') >= 0) s.Errors.Add("description may not contain < or >.");
                if (!Regex.IsMatch(s.Description, @"\b(use|used|when|whenever|for)\b", RegexOptions.IgnoreCase))
                    s.Warnings.Add("description should say when to use the skill (\"Use when ...\"): that is what the matching reads.");
                if (Regex.IsMatch(s.Description, @"^\s*(I|I'm|I'll|You|You'll)\b"))
                    s.Warnings.Add("description reads best in the third person (\"Reviews SQL ...\", not \"I review\" or \"You can\").");
                if (s.Description.Length < 40) s.Warnings.Add("description is very short: add the key words a request about this would contain.");
            }
            if (s.Body.Trim().Length == 0) s.Errors.Add("the instructions (body under the frontmatter) are empty.");
            else
            {
                int lines = s.Body.Split('\n').Length;
                if (lines > MaxBodyLines) s.Warnings.Add("the body has " + lines + " lines: keep SKILL.md under " + MaxBodyLines + " and move detail into supporting files.");
                if (s.Body.Length > MaxBodyChars) s.Warnings.Add("the body is over " + MaxBodyChars + " characters: only the first " + MaxBodyChars + " reach the model.");
            }
        }

        // ---- loading ----

        public static string ProjectSkillsDir(string projectRoot) { return projectRoot == null ? null : Path.Combine(projectRoot, ".phoenix", "skills"); }

        // Every skill: the app's, then the project's (a project skill replaces an app skill of the same name).
        public static List<Skill> Load(string appDir, string projectDir)
        {
            Dictionary<string, Skill> byName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
            List<Skill> order = new List<Skill>();
            LoadDir(appDir, "app", byName, order);
            LoadDir(projectDir, "project", byName, order);
            order.Sort(delegate(Skill a, Skill b) { return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); });
            return order;
        }

        static void LoadDir(string dir, string scope, Dictionary<string, Skill> byName, List<Skill> order)
        {
            if (dir == null || !Directory.Exists(dir)) return;
            string[] subs;
            try { subs = Directory.GetDirectories(dir); } catch { return; }
            foreach (string sub in subs)
            {
                string folder = Path.GetFileName(sub);
                if (folder.StartsWith(".")) continue;
                try { if ((new DirectoryInfo(sub).Attributes & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                string md = Path.Combine(sub, "SKILL.md");
                if (!File.Exists(md)) continue;
                Skill s;
                try { s = Parse(File.ReadAllText(md, Encoding.UTF8)); } catch { continue; }
                s.Dir = sub; s.Scope = scope;
                if (s.Name.Length == 0) s.Name = folder;
                else if (!string.Equals(s.Name, folder, StringComparison.Ordinal)) s.Warnings.Add("name (" + s.Name + ") differs from its folder (" + folder + ").");
                try
                {
                    foreach (string f in Directory.GetFiles(sub))
                    {
                        string n = Path.GetFileName(f);
                        if (!n.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) && FileNameRe.IsMatch(n)) s.Files.Add(n);
                    }
                    s.Files.Sort(StringComparer.OrdinalIgnoreCase);
                }
                catch { }
                Skill old;
                if (byName.TryGetValue(s.Name, out old)) order.Remove(old);
                byName[s.Name] = s; order.Add(s);
            }
        }

        // ---- matching ----

        static readonly HashSet<string> Stop = new HashSet<string>(("a an and are as at be by can do does for from has have how i in into is it its me my of on " +
            "or our please so that the their them then there these this to up use used uses using via want was we what when whenever where which " +
            "while who why will with would you your skill skills help make need needs any all some code write writes writing").Split(' '));

        static List<string> Words(string text)
        {
            List<string> w = new List<string>();
            string low = (text ?? "").ToLowerInvariant();
            foreach (Match m in Regex.Matches(low, @"[a-z0-9][a-z0-9#+]*"))
            {
                string t = m.Value;
                if (t.Length < 2 || Stop.Contains(t)) continue;
                w.Add(Stem(t));
            }
            // joined forms of slashed or hyphenated words, so "PL/SQL" also matches "plsql" and "c-sharp" "csharp"
            foreach (Match m in Regex.Matches(low, @"\b[a-z0-9]+(?:[/-][a-z0-9]+)+\b")) w.Add(Stem(m.Value.Replace("/", "").Replace("-", "")));
            return w;
        }

        // Crude English stemming, applied the same way to both sides: merges/merge/merged/merging -> merg.
        static string Stem(string t)
        {
            if (t.Length > 5 && t.EndsWith("ing")) t = t.Substring(0, t.Length - 3);
            else if (t.Length > 4 && t.EndsWith("ed")) t = t.Substring(0, t.Length - 2);
            else if (t.Length > 4 && t.EndsWith("es")) t = t.Substring(0, t.Length - 2);
            else if (t.Length > 3 && t.EndsWith("s") && !t.EndsWith("ss")) t = t.Substring(0, t.Length - 1);
            if (t.Length > 4 && t.EndsWith("e")) t = t.Substring(0, t.Length - 1);
            return t;
        }

        // How a message scores against one skill, whatever the bar (the Skill Builder's trigger test shows near misses).
        public static SkillMatch Score(Skill s, string message)
        {
            SkillMatch m = new SkillMatch(); m.Skill = s;
            if (string.IsNullOrEmpty(message)) return m;
            string low = message.ToLowerInvariant(), n = Regex.Escape(s.Name);
            if (s.Name.Length > 0 && (Regex.IsMatch(low, @"^\s*/" + n + @"(\s|$)") || Regex.IsMatch(low, @"\b" + n + @"\s+skill\b|\bskill\s+" + n + @"\b|\buse\s+(the\s+)?" + n + @"\b")))
            { m.Explicit = true; m.Score = 100; m.Terms.Add("/" + s.Name); return m; }
            HashSet<string> msg = new HashSet<string>(Words(message));
            Dictionary<string, double> weight = new Dictionary<string, double>();
            foreach (string w in Words(s.Name.Replace('-', ' '))) weight[w] = 2;
            foreach (string w in Words(s.Description)) if (!weight.ContainsKey(w)) weight[w] = 1;
            foreach (KeyValuePair<string, double> kv in weight)
                if (msg.Contains(kv.Key)) { m.Score += kv.Value; m.Terms.Add(kv.Key); }
            return m;
        }

        public static bool Loads(SkillMatch m) { return m.Explicit || (m.Terms.Count >= 2 && m.Score >= 3); }

        // How well a message matches each skill. Explicit calls: the message starts with /name, or says "name skill" /
        // "skill name" / "use name". Otherwise the distinct words of the message found in the skill's name (counting 2)
        // and description (1); a match needs two different words and a score of 3, so one shared word never loads a skill.
        public static List<SkillMatch> Match(List<Skill> skills, string message)
        {
            List<SkillMatch> res = new List<SkillMatch>();
            if (skills == null || skills.Count == 0 || string.IsNullOrEmpty(message)) return res;
            foreach (Skill s in skills)
            {
                if (!s.Valid) continue;
                SkillMatch m = Score(s, message);
                if (Loads(m)) res.Add(m);
            }
            res.Sort(delegate(SkillMatch a, SkillMatch b) { return b.Score.CompareTo(a.Score); });
            if (res.Count > MaxInjected) res.RemoveRange(MaxInjected, res.Count - MaxInjected);
            return res;
        }

        // ---- prompt text ----

        // One block for the system prompt: what skills exist (stable between messages, so the cached prefix holds).
        public static string CatalogNote(List<Skill> skills)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Skill s in skills)
            {
                if (!s.Valid) continue;
                string d = s.Description.Length > 220 ? s.Description.Substring(0, 217) + "..." : s.Description;
                sb.Append("- ").Append(s.Name).Append(": ").Append(d).Append('\n');
            }
            if (sb.Length == 0) return null;
            return "Skills: the user's saved instructions for recurring kinds of work. When a request matches one, its full text is " +
                   "given to you with the message under [Skill: name]; follow it closely, it overrides your usual habits. Available:\n" + sb.ToString().TrimEnd('\n');
        }

        // The newest user message with the matched skills in front of it.
        public static string Wrap(List<SkillMatch> used, string message)
        {
            StringBuilder sb = new StringBuilder();
            foreach (SkillMatch m in used)
            {
                Skill s = m.Skill;
                string body = s.Body.Length > MaxBodyChars ? s.Body.Substring(0, MaxBodyChars) + "\n[... cut]" : s.Body;
                sb.Append("[Skill: ").Append(s.Name).Append("]\n").Append(body.Trim()).Append('\n');
                // Supporting files the body names (reference.md, examples.md ...), within a budget.
                foreach (string f in s.Files)
                {
                    if (body.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    string text;
                    try { text = File.ReadAllText(Path.Combine(s.Dir, f), Encoding.UTF8); } catch { continue; }
                    if (text.Length > MaxFileChars) text = text.Substring(0, MaxFileChars) + "\n[... cut]";
                    sb.Append("\n[").Append(s.Name).Append(" / ").Append(f).Append("]\n").Append(text.Trim()).Append('\n');
                }
                sb.Append("[/Skill]\n\n");
            }
            sb.Append("Follow the skill above for this request. Put any code in a fenced code block tagged with its language.\n\n").Append(message);
            return sb.ToString();
        }

        // ---- the Skill Builder's assistant ----

        // How to write a skill, for the model that helps build one in the Skill Builder.
        public const string AuthorGuide =
            "You write skills for a local coding assistant. A skill is one Markdown file, SKILL.md, in exactly this format:\n\n" +
            "---\n" +
            "name: lowercase-words-with-hyphens\n" +
            "description: One or two sentences in the third person: what the skill does, then \"Use when ...\" with the words a matching request would contain.\n" +
            "---\n\n" +
            "# Title\n\n" +
            "Instructions in Markdown.\n\n" +
            "Rules for a good skill:\n" +
            "- name: lowercase letters, digits and single hyphens, at most 64 characters, specific (oracle-plsql-review, not helper).\n" +
            "- description: at most 1024 characters, no < or >. It is the only part read when deciding whether the skill applies, so name " +
            "the languages, tools, file types and tasks it covers.\n" +
            "- Body: direct, imperative instructions to the assistant (\"Check ...\", \"Never ...\"), not an essay. Use sections such as " +
            "## When to use, ## Steps (a numbered workflow), ## Rules (hard do / don't), ## Output format, ## Example (a short input and the " +
            "exact expected output), ## Checklist (what to verify before answering).\n" +
            "- Be concrete: real syntax, real commands for the user to run, real naming rules. Leave out anything a capable model already " +
            "knows; keep what is specific to this user or task. Keep it under 200 lines.\n" +
            "- The assistant can read, write, edit and list files in the user's project but never runs commands: a step that needs a command " +
            "gives the command for the user to run.\n\n" +
            "The file always starts with the --- line, before name:. " +
            "Always answer with the complete SKILL.md between a line ~~~skill and a line ~~~ (tildes, so code blocks inside the skill stay " +
            "intact), then at most two short sentences on what you changed. When the user only asks a question, answer it briefly " +
            "without a skill block. Never leave placeholders such as TODO or <fill in>.";
    }
}
