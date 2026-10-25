// Skills API for the sidebar and the Skill Builder (see Skills.cs for the format). C# 5 only.
//   GET  /api/skills                         every skill (app + active project) with its checks
//   GET  /api/skill-get?scope=app&name=x     SKILL.md text and supporting files of one skill
//   POST /api/skill-save {scope, oldName, text, files:[{name,text}], removed:[names]}
//   POST /api/skill-delete {scope, name}     moves the folder to %LOCALAPPDATA%\LocalLLMStudio\skill-trash
//   POST /api/skill-check {text, message}    format checks for a draft, and whether a sample message would load it
//   POST /api/skill-assist {mode, request, draft, history}   the loaded model drafts or improves a SKILL.md (SSE)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        string AppSkillsDir { get { return Path.Combine(Directory.GetParent(_htmlDir).FullName, "skills"); } }
        string ProjectSkillsDir { get { return Skills.ProjectSkillsDir(ProjectRootOf(_workspace)); } }

        List<Skill> AllSkills() { return Skills.Load(AppSkillsDir, ProjectSkillsDir); }

        string SkillsBase(string scope)
        {
            if (scope == "project")
            {
                string d = ProjectSkillsDir;
                if (d == null) throw new InvalidOperationException("No project is open: open a project to keep skills with it, or save the skill to the app.");
                return d;
            }
            if (scope != "app") throw new ArgumentException("scope must be app or project.");
            return AppSkillsDir;
        }

        static string SkillJson(Skill s)
        {
            StringBuilder sb = new StringBuilder("{");
            sb.Append("\"name\":").Append(Json.Quote(s.Name)).Append(",\"description\":").Append(Json.Quote(s.Description))
              .Append(",\"scope\":").Append(Json.Quote(s.Scope ?? "")).Append(",\"valid\":").Append(s.Valid ? "true" : "false")
              .Append(",\"lines\":").Append(s.Body.Length == 0 ? 0 : s.Body.Split('\n').Length)
              .Append(",\"errors\":").Append(StrArray(s.Errors)).Append(",\"warnings\":").Append(StrArray(s.Warnings))
              .Append(",\"files\":").Append(StrArray(s.Files)).Append('}');
            return sb.ToString();
        }

        static string StrArray(List<string> items)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json.Quote(items[i])); }
            return sb.Append(']').ToString();
        }

        string SkillListJson()
        {
            List<Skill> all = AllSkills();
            StringBuilder sb = new StringBuilder("{\"skills\":[");
            for (int i = 0; i < all.Count; i++) { if (i > 0) sb.Append(','); sb.Append(SkillJson(all[i])); }
            string proj = ProjectRootOf(_workspace);
            sb.Append("],\"appDir\":").Append(Json.Quote(AppSkillsDir)).Append(",\"projectDir\":").Append(proj != null ? Json.Quote(ProjectSkillsDir) : "null")
              .Append(",\"project\":").Append(proj != null ? Json.Quote(Path.GetFileName(proj)) : "null").Append('}');
            return sb.ToString();
        }

        // A skill folder by scope and name (the name is checked, so it can never leave the skills folder).
        string SkillDir(string scope, string name)
        {
            if (name == null || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9]+(-[a-z0-9]+)*$") || name.Length > Skills.MaxName)
                throw new ArgumentException("Bad skill name.");
            return Path.Combine(SkillsBase(scope), name);
        }

        // Handles the skill endpoints (all but the streaming assistant): the JSON reply, or null when the path is not one.
        string SkillsApi(string path, bool post, Dictionary<string, object> body, System.Collections.Specialized.NameValueCollection query)
        {
            if (path == "/api/skills") return SkillListJson();
            if (path == "/api/skill-get")
            {
                string dir = SkillDir(query["scope"], query["name"]);
                string md = Path.Combine(dir, "SKILL.md");
                if (!File.Exists(md)) throw new FileNotFoundException("That skill is gone.");
                StringBuilder sb = new StringBuilder("{\"text\":").Append(Json.Quote(File.ReadAllText(md, Encoding.UTF8))).Append(",\"files\":[");
                bool first = true;
                foreach (string f in Directory.GetFiles(dir))
                {
                    string n = Path.GetFileName(f);
                    if (n.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase) || !Skills.FileNameRe.IsMatch(n)) continue;
                    if (!first) sb.Append(','); first = false;
                    sb.Append("{\"name\":").Append(Json.Quote(n)).Append(",\"text\":").Append(Json.Quote(File.ReadAllText(f, Encoding.UTF8))).Append('}');
                }
                return sb.Append("]}").ToString();
            }
            if (path == "/api/skill-save" && post)
            {
                string scope = Json.GetStr(body, "scope", "app"), oldName = Json.GetStr(body, "oldName", ""), text = Json.GetStr(body, "text", "");
                Skill s = Skills.Parse(text);
                if (!s.Valid) throw new ArgumentException("Fix the skill first: " + string.Join(" ", s.Errors.ToArray()));
                string dir = SkillDir(scope, s.Name);
                string baseDir = SkillsBase(scope);
                Directory.CreateDirectory(baseDir);
                if (oldName.Length > 0 && oldName != s.Name)
                {
                    string oldDir = SkillDir(scope, oldName);
                    if (Directory.Exists(dir)) throw new ArgumentException("A skill named " + s.Name + " already exists here.");
                    if (Directory.Exists(oldDir)) Directory.Move(oldDir, dir);
                }
                else if (oldName.Length == 0 && Directory.Exists(dir)) throw new ArgumentException("A skill named " + s.Name + " already exists here: open it from the list, or pick another name.");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "SKILL.md"), text.Replace("\r\n", "\n"), new UTF8Encoding(false));
                List<object> removed = body.ContainsKey("removed") ? body["removed"] as List<object> : null;
                if (removed != null)
                    foreach (object r in removed)
                    {
                        string n = r as string;
                        if (n != null && Skills.FileNameRe.IsMatch(n) && !n.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)) { string p = Path.Combine(dir, n); if (File.Exists(p)) File.Delete(p); }
                    }
                List<object> files = body.ContainsKey("files") ? body["files"] as List<object> : null;
                if (files != null)
                    foreach (object o in files)
                    {
                        Dictionary<string, object> f = o as Dictionary<string, object>;
                        string n = Json.GetStr(f, "name", "");
                        if (!Skills.FileNameRe.IsMatch(n) || n.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Bad supporting file name: " + n + " (letters, digits, . _ -, ending in .md or .txt).");
                        File.WriteAllText(Path.Combine(dir, n), Json.GetStr(f, "text", "").Replace("\r\n", "\n"), new UTF8Encoding(false));
                    }
                Log.Text("SKILL saved: " + scope + " " + s.Name + (oldName.Length > 0 && oldName != s.Name ? " (renamed from " + oldName + ")" : ""));
                return SkillListJson();
            }
            if (path == "/api/skill-delete" && post)
            {
                string scope = Json.GetStr(body, "scope", "app"), name = Json.GetStr(body, "name", "");
                string dir = SkillDir(scope, name);
                if (Directory.Exists(dir))
                {
                    string trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLLMStudio", "skill-trash");
                    Directory.CreateDirectory(trash);
                    Directory.Move(dir, Path.Combine(trash, name + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")));
                    Log.Text("SKILL deleted (moved to skill-trash): " + scope + " " + name);
                }
                return SkillListJson();
            }
            if (path == "/api/skill-check" && post)
            {
                Skill s = Skills.Parse(Json.GetStr(body, "text", ""));
                string message = Json.GetStr(body, "message", "");
                StringBuilder sb = new StringBuilder("{\"skill\":").Append(SkillJson(s));
                if (message.Length > 0)
                {
                    // Would this draft load for the message, and which skills would actually be given to the model?
                    SkillMatch self = Skills.Score(s, message);
                    List<Skill> all = AllSkills();
                    all.RemoveAll(delegate(Skill x) { return x.Name == s.Name; });
                    all.Add(s);
                    List<SkillMatch> winners = Skills.Match(all, message);
                    List<string> names = new List<string>();
                    foreach (SkillMatch w in winners) names.Add(w.Skill.Name);
                    // the words that match, also below the bar (so the builder can show what is missing)
                    sb.Append(",\"test\":{\"loads\":").Append(names.Contains(s.Name) ? "true" : "false")
                      .Append(",\"matches\":").Append(s.Valid && Skills.Loads(self) ? "true" : "false")
                      .Append(",\"score\":").Append(Json.Num(self.Score)).Append(",\"terms\":").Append(StrArray(self.Terms))
                      .Append(",\"winners\":").Append(StrArray(names)).Append('}');
                }
                return sb.Append('}').ToString();
            }
            return null;
        }

        // The model helps write a skill: mode "draft" (from a request), "improve" (the current draft, optionally with a
        // request), or "chat" (free conversation about the draft). Streams {"t":text} then {"done":true} to outS (an
        // event stream WebServer.cs opened).
        void SkillAssist(Dictionary<string, object> o, Stream outS)
        {
            string mode = Json.GetStr(o, "mode", "draft"), request = Json.GetStr(o, "request", "").Trim(), draft = Json.GetStr(o, "draft", "").Trim();
            StringBuilder user = new StringBuilder();
            if (mode == "draft")
                user.Append("Write a new skill from this request:\n").Append(request.Length > 0 ? request : "(no request given: ask what the skill should do)");
            else
            {
                user.Append("Here is the current SKILL.md:\n\n~~~skill\n").Append(draft).Append("\n~~~\n\n");
                user.Append(request.Length > 0 ? request : "Improve it: make the description name the tasks and key words that should trigger it, make the steps concrete, add a short example and a checklist, and cut anything generic.");
            }
            // Ground the draft in the syntax library when the request is about a language it covers.
            List<RagHit> hits = new List<RagHit>();
            try { if (Rag.Enabled && LocalContextAllowed) hits = Rag.Search(request + "\n" + draft, null); } catch { }
            string prompt = hits.Count > 0 ? Rag.WrapMessage(hits, user.ToString()) : user.ToString();

            List<ChatMessage> msgs = new List<ChatMessage>();
            msgs.Add(new ChatMessage("system", Skills.AuthorGuide));
            List<object> history = o != null && o.ContainsKey("history") ? o["history"] as List<object> : null;
            if (history != null && mode == "chat")
                foreach (object h in history)
                {
                    Dictionary<string, object> hm = h as Dictionary<string, object>;
                    string role = Json.GetStr(hm, "role", "");
                    if (role == "user" || role == "assistant") msgs.Add(new ChatMessage(role, Json.GetStr(hm, "content", "")));
                }
            msgs.Add(new ChatMessage("user", prompt));

            SamplerSettings s = new SamplerSettings();
            s.Temperature = 0.4f; s.TopK = 40; s.TopP = 0.95f; s.RepeatPenalty = 1.05f;

            bool gone = false;
            object writeLock = new object();
            TextCallback write = delegate(string line)
            {
                lock (writeLock)
                {
                    if (gone) return;
                    try { byte[] b = Encoding.UTF8.GetBytes("data: " + line + "\n\n"); outS.Write(b, 0, b.Length); outS.Flush(); }
                    catch { gone = true; }
                }
            };
            int rid = Interlocked.Increment(ref _requestId);
            Log.Text("#" + rid + " SKILL ASSIST " + mode + ": " + (request.Length > 300 ? request.Substring(0, 300) + "..." : request));
            if (hits.Count > 0) write("{\"rag\":" + HitsJson(hits, false) + "}");
            if (_recallBusy != 0) _recallStop = true;
            if (_pretypeBusy != 0) _pretypeStop = true;
            try
            {
                lock (_engineLock)
                {
                    EnsureClientModel();
                    if (_engine == null) throw new InvalidOperationException("No model loaded: pick one in the top bar first.");
                    _engine.PrefillProgress = null;
                    Stopwatch sw = Stopwatch.StartNew();
                    StringBuilder reply = new StringBuilder();
                    GenerationStats st = _engine.Generate(msgs, s, 2048, delegate(string piece) { reply.Append(piece); write("{\"t\":" + Json.Quote(piece) + "}"); },
                                                          delegate { return gone; });
                    Log.Text(string.Format(System.Globalization.CultureInfo.InvariantCulture, "#{0} SKILL ASSIST reply: {1} tokens in {2} ms ({3:F1} tok/s)\n{4}",
                        rid, st.GeneratedTokens, ChatLog.Ms(sw.Elapsed.TotalSeconds), st.TokensPerSecond, reply.ToString()));
                    write("{\"done\":true,\"tokens\":" + st.GeneratedTokens + ",\"tps\":" + Json.Num(Math.Round(st.TokensPerSecond, 1)) + "}");
                }
            }
            catch (Exception ex) { write("{\"error\":" + Json.Quote(ex.Message) + "}"); }
            try { outS.Close(); } catch { }
        }
    }
}
