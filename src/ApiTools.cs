// OpenAI tool calling for the API server (WebServer.OpenAi.cs), model-agnostic: the request's tool definitions are put
// into the system prompt, the model answers a call as <tool_call>{"name": ..., "arguments": {...}}</tool_call>, and the
// reply is parsed back into OpenAI tool_calls. Earlier calls and tool results in the history are rendered in the same
// text form, so the model sees its own format. C# 5 only. The server only describes and parses calls: it never runs a
// tool (the client does, e.g. VS Code with its own approvals), so ShellGuard's rule for the Phoenix Client's model is unchanged.
using System;
using System.Collections.Generic;
using System.Text;

namespace Phoenix
{
    public sealed class ApiToolCall
    {
        public string Id, Name, Arguments;   // Arguments: a JSON object as text (OpenAI sends it as a string)
    }

    public static class ApiTools
    {
        public const string Open = "<tool_call>", Close = "</tool_call>";
        const string Mark = "<tool_call";   // models write "<tool_call{...}>" too (Gemma 4 measured 2026-10-16)

        // The system-prompt block for the request's tools; null when there are none or tool_choice is "none".
        public static string Prompt(Dictionary<string, object> o)
        {
            List<object> tools = o.ContainsKey("tools") ? o["tools"] as List<object> : null;
            if (tools == null || tools.Count == 0) return null;
            object choice = o.ContainsKey("tool_choice") ? o["tool_choice"] : null;
            if (choice is string && (string)choice == "none") return null;
            string forced = null;
            Dictionary<string, object> cd = choice as Dictionary<string, object>;
            if (cd != null && cd.ContainsKey("function")) forced = Json.GetStr(cd["function"] as Dictionary<string, object>, "name", null);

            StringBuilder sb = new StringBuilder();
            // Compact on purpose: VS Code's agent sends ~55 tools with long descriptions and full JSON schemas, which as
            // written cost a 12B model minutes of prefill (2026-10-16). One line per tool: name(arg: type, arg?: type) - the
            // first sentence of its description.
            sb.Append("# Tools\n\nYou act on the user's workspace and computer through these tools. When the user asks you to do something ");
            sb.Append("a tool can do, call the tool: never say you cannot, and never tell the user to do it by hand. File operations no ");
            sb.Append("tool covers (delete, move, rename, run, build, install) go through the terminal / command tool; the user approves ");
            sb.Append("each command. (\"?\" marks an optional argument.)\n");
            foreach (object t in tools)
            {
                Dictionary<string, object> td = t as Dictionary<string, object>;
                Dictionary<string, object> f = td != null && td.ContainsKey("function") ? td["function"] as Dictionary<string, object> : td;
                if (f == null) continue;
                string name = Json.GetStr(f, "name", null);
                if (name == null) continue;
                sb.Append("- ").Append(name).Append('(').Append(Args(f.ContainsKey("parameters") ? f["parameters"] as Dictionary<string, object> : null)).Append(')');
                string desc = FirstSentence(Json.GetStr(f, "description", ""));
                if (desc.Length > 0) sb.Append(" - ").Append(desc);
                sb.Append('\n');
            }
            sb.Append("\nTo call a tool, reply with one block per call, exactly in this form and with nothing after the last block:\n");
            sb.Append(Open).Append("{\"name\": \"tool_name\", \"arguments\": {\"arg\": \"value\"}}").Append(Close).Append('\n');
            sb.Append("The arguments must be valid JSON. After a call you get the result in a <tool_response> ");
            sb.Append("block; then continue, call another tool, or give the final answer as plain text.");
            if (forced != null) sb.Append("\nYou must call the tool \"").Append(forced).Append("\" now.");
            else if (choice is string && (string)choice == "required") sb.Append("\nYou must call at least one tool now.");
            else sb.Append("\nOnly call a tool when it helps; otherwise answer directly.");
            return sb.ToString();
        }

        // "path: string, limit?: number, mode: "a"|"b"" from a JSON schema's top-level properties.
        static string Args(Dictionary<string, object> schema)
        {
            Dictionary<string, object> props = schema != null && schema.ContainsKey("properties") ? schema["properties"] as Dictionary<string, object> : null;
            if (props == null) return "";
            HashSet<string> req = new HashSet<string>();
            List<object> rl = schema.ContainsKey("required") ? schema["required"] as List<object> : null;
            if (rl != null) foreach (object r in rl) if (r is string) req.Add((string)r);
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, object> kv in props)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(kv.Key).Append(req.Contains(kv.Key) ? ": " : "?: ").Append(TypeOf(kv.Value as Dictionary<string, object>));
            }
            return sb.ToString();
        }

        static string TypeOf(Dictionary<string, object> p)
        {
            if (p == null) return "any";
            List<object> en = p.ContainsKey("enum") ? p["enum"] as List<object> : null;
            if (en != null && en.Count > 0 && en.Count <= 6)
            {
                List<string> vals = new List<string>();
                foreach (object e in en) vals.Add(e is string ? "\"" + e + "\"" : Convert.ToString(e, System.Globalization.CultureInfo.InvariantCulture));
                return string.Join("|", vals.ToArray());
            }
            object ty = p.ContainsKey("type") ? p["type"] : null;
            string t = ty as string ?? "any";
            if (t == "array")
            {
                Dictionary<string, object> items = p.ContainsKey("items") ? p["items"] as Dictionary<string, object> : null;
                return TypeOf(items) + "[]";
            }
            return t;
        }

        static string FirstSentence(string d)
        {
            d = d.Trim().Replace("\r", " ").Replace("\n", " ");
            int end = d.IndexOf(". ", StringComparison.Ordinal);
            if (end > 0) d = d.Substring(0, end + 1);
            if (d.Length > 160) d = d.Substring(0, 157) + "...";
            return d;
        }

        // An assistant message from the history with its tool_calls, as the text the model would have written.
        public static string AssistantText(Dictionary<string, object> m, string content)
        {
            List<object> calls = m.ContainsKey("tool_calls") ? m["tool_calls"] as List<object> : null;
            if (calls == null || calls.Count == 0) return content;
            StringBuilder sb = new StringBuilder(content ?? "");
            foreach (object c in calls)
            {
                Dictionary<string, object> f = c is Dictionary<string, object> && ((Dictionary<string, object>)c).ContainsKey("function")
                    ? ((Dictionary<string, object>)c)["function"] as Dictionary<string, object> : null;
                if (f == null) continue;
                string args = Json.GetStr(f, "arguments", null);
                if (args == null) args = f.ContainsKey("arguments") ? Json.Write(f["arguments"]) : "{}";
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(Open).Append("{\"name\": ").Append(Json.Write(Json.GetStr(f, "name", ""))).Append(", \"arguments\": ").Append(args.Trim()).Append('}').Append(Close);
            }
            return sb.ToString();
        }

        // A tool result from the history ({role: "tool", tool_call_id, name?, content}).
        public static string ResultText(Dictionary<string, object> m, string content, Dictionary<string, string> namesById)
        {
            string id = Json.GetStr(m, "tool_call_id", "");
            string name = Json.GetStr(m, "name", null);
            if (name == null && namesById.ContainsKey(id)) name = namesById[id];
            return "<tool_response name=\"" + (name ?? "tool") + "\">\n" + content + "\n</tool_response>";
        }

        // Length of the reply text that is surely ordinary content: up to the first <tool_call>, holding back a tail that
        // could still become one.
        public static int SafeLength(StringBuilder text)
        {
            string s = text.ToString();
            int at = s.IndexOf(Mark, StringComparison.Ordinal);
            if (at >= 0) return at;
            for (int k = Math.Min(Mark.Length - 1, s.Length); k > 0; k--)
                if (string.CompareOrdinal(s, s.Length - k, Mark, 0, k) == 0) return s.Length - k;
            return s.Length;
        }

        // Splits a finished reply into its content and its tool calls (empty list: a plain answer). Accepts "parameters"
        // for "arguments", a missing close tag at the end, and a reply that is only a bare {"name", "arguments"} object
        // naming a known tool (Llama 3.1's own habit).
        public static List<ApiToolCall> Parse(string reply, HashSet<string> known, out string content)
        {
            List<ApiToolCall> calls = new List<ApiToolCall>();
            int first = reply.IndexOf(Mark, StringComparison.Ordinal);
            if (first < 0)
            {
                string t = reply.Trim();
                if (t.StartsWith("```")) { int nl = t.IndexOf('\n'); if (nl > 0 && t.EndsWith("```")) t = t.Substring(nl + 1, t.Length - nl - 4).Trim(); }
                ApiToolCall bare = t.StartsWith("{") ? One(t, known, true) : null;
                if (bare != null) { calls.Add(bare); content = ""; return calls; }
                content = reply;
                return calls;
            }
            content = reply.Substring(0, first).TrimEnd();
            int pos = first;
            while (pos >= 0)
            {
                // the JSON object after the marker, whatever wraps it ("<tool_call>{..}</tool_call>", "<tool_call{..}>")
                int brace = reply.IndexOf('{', pos + Mark.Length);
                int end = brace >= 0 ? ObjectEnd(reply, brace) : -1;
                if (end < 0) break;
                ApiToolCall c = One(reply.Substring(brace, end - brace), known, false);
                if (c != null) calls.Add(c);
                pos = reply.IndexOf(Mark, end, StringComparison.Ordinal);
            }
            if (calls.Count == 0) content = reply;   // nothing parseable: return the text as it is
            return calls;
        }

        // Index just past the JSON object starting at reply[start] == '{' (strings and escapes respected); -1 if unclosed.
        static int ObjectEnd(string s, int start)
        {
            int depth = 0;
            bool str = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (str) { if (c == '\\') i++; else if (c == '"') str = false; continue; }
                if (c == '"') str = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return i + 1;
            }
            return -1;
        }

        static ApiToolCall One(string body, HashSet<string> known, bool strict)
        {
            Dictionary<string, object> d;
            try { d = Json.Parse(body) as Dictionary<string, object>; }
            catch { return null; }
            if (d == null) return null;
            string name = Json.GetStr(d, "name", null);
            if (name == null || (strict && !known.Contains(name))) return null;
            object a = d.ContainsKey("arguments") ? d["arguments"] : d.ContainsKey("parameters") ? d["parameters"] : null;
            string args = a is string ? (string)a : a == null ? "{}" : Json.Write(a);
            ApiToolCall c = new ApiToolCall();
            c.Id = "call_" + Guid.NewGuid().ToString("N").Substring(0, 24);
            c.Name = name;
            c.Arguments = args;
            return c;
        }

        public static HashSet<string> Names(Dictionary<string, object> o)
        {
            HashSet<string> names = new HashSet<string>();
            List<object> tools = o.ContainsKey("tools") ? o["tools"] as List<object> : null;
            if (tools != null)
                foreach (object t in tools)
                {
                    Dictionary<string, object> td = t as Dictionary<string, object>;
                    Dictionary<string, object> f = td != null && td.ContainsKey("function") ? td["function"] as Dictionary<string, object> : td;
                    string n = Json.GetStr(f, "name", null);
                    if (n != null) names.Add(n);
                }
            return names;
        }

        // OpenAI tool_calls array; index fields only for stream deltas.
        public static string CallsJson(List<ApiToolCall> calls, bool withIndex)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < calls.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('{');
                if (withIndex) sb.Append("\"index\":").Append(i).Append(',');
                sb.Append("\"id\":").Append(Json.Quote(calls[i].Id)).Append(",\"type\":\"function\",\"function\":{\"name\":")
                  .Append(Json.Quote(calls[i].Name)).Append(",\"arguments\":").Append(Json.Quote(calls[i].Arguments)).Append("}}");
            }
            return sb.Append(']').ToString();
        }
    }
}
