// Anthropic Messages API on the same port as the OpenAI API (http://localhost:1893/v1), for Claude-style clients (the
// Anthropic SDKs, tools that take an ANTHROPIC_BASE_URL). C# 5 only. The setting "API format" (dashboard, console
// apiformat, %LOCALAPPDATA%\LocalLLMStudio\api-format.txt) picks what the server hosts: openai (default), anthropic or both.
// A Messages request is translated into an OpenAI chat request, run through Completion (WebServer.OpenAi.cs) and the
// answer translated back, so both formats share one code path (tool calls, stop sequences, images, keep-alives).
//   POST /v1/messages                 messages + system -> message (stream: true gives Anthropic's SSE events); tools +
//                                     tool_choice (auto/any/tool/none), stop_sequences, max_tokens, temperature, top_p, top_k
//   POST /v1/messages/count_tokens    an estimate (characters / 4): the tokenizer belongs to the loaded engine
//   GET  /v1/models[/{id}]            Anthropic's list shape when the format is anthropic, or the request has anthropic-version
//   /v1/embeddings stays on in every format: the Phoenix client's docs search needs it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        // ---------- the setting ----------
        static string ApiFormatFile { get { return Path.Combine(AppDataDir, "api-format.txt"); } }
        static string _apiFormat = LoadApiFormat();

        static string LoadApiFormat()
        {
            try { if (File.Exists(ApiFormatFile)) { string f = File.ReadAllText(ApiFormatFile).Trim().ToLowerInvariant(); if (IsApiFormat(f)) return f; } } catch { }
            return "openai";
        }

        public static bool IsApiFormat(string f) { return f == "openai" || f == "anthropic" || f == "both"; }
        public static string ApiFormat { get { return _apiFormat; } }
        static bool OpenAiOn { get { return _apiFormat != "anthropic"; } }
        static bool AnthropicOn { get { return _apiFormat != "openai"; } }

        public static void SetApiFormat(string f)
        {
            f = (f ?? "").Trim().ToLowerInvariant();
            if (!IsApiFormat(f)) throw new ArgumentException("API format must be openai, anthropic or both.");
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(ApiFormatFile, f);
            _apiFormat = f;
        }

        public static string ApiFormatName { get { return _apiFormat == "both" ? "OpenAI + Anthropic" : _apiFormat == "anthropic" ? "Anthropic" : "OpenAI"; } }

        // ---------- errors and models ----------
        static void AnthError(HttpListenerResponse res, int code, string msg, string type)
        {
            SendJson(res, code, AnthErrorJson(msg, type));
        }

        static string AnthErrorJson(string msg, string type)
        {
            return "{\"type\":\"error\",\"error\":{\"type\":" + Json.Quote(type) + ",\"message\":" + Json.Quote(msg) + "}}";
        }

        static string AnthErrorType(int code)
        {
            return code == 401 ? "authentication_error" : code == 403 ? "permission_error" : code == 404 ? "not_found_error" : code >= 500 ? "api_error" : "invalid_request_error";
        }

        string AnthModelEntry(string f)
        {
            string id = Path.GetFileNameWithoutExtension(f);
            return "{\"type\":\"model\",\"id\":" + Json.Quote(id) + ",\"display_name\":" + Json.Quote(id) + ",\"created_at\":" +
                   Json.Quote(File.GetLastWriteTimeUtc(f).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture)) + "}";
        }

        string AnthModelsList()
        {
            List<string> files = ApiModelFiles();
            StringBuilder sb = new StringBuilder("{\"data\":[");
            for (int i = 0; i < files.Count; i++) { if (i > 0) sb.Append(','); sb.Append(AnthModelEntry(files[i])); }
            sb.Append("],\"has_more\":false,\"first_id\":").Append(files.Count > 0 ? Json.Quote(Path.GetFileNameWithoutExtension(files[0])) : "null")
              .Append(",\"last_id\":").Append(files.Count > 0 ? Json.Quote(Path.GetFileNameWithoutExtension(files[files.Count - 1])) : "null").Append('}');
            return sb.ToString();
        }

        string AnthModelJson(string id)
        {
            foreach (string f in ApiModelFiles())
                if (string.Equals(Path.GetFileNameWithoutExtension(f), id, StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileName(f), id, StringComparison.OrdinalIgnoreCase))
                    return AnthModelEntry(f);
            return null;
        }

        // ---------- request: Anthropic -> OpenAI ----------
        static Dictionary<string, object> D() { return new Dictionary<string, object>(); }

        static string BlocksText(object c)
        {
            if (c is string) return (string)c;
            List<object> l = c as List<object>;
            if (l == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (object b in l)
            {
                Dictionary<string, object> bd = b as Dictionary<string, object>;
                if (bd != null && Json.GetStr(bd, "type", "") == "text") { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(Json.GetStr(bd, "text", "")); }
            }
            return sb.ToString();
        }

        static Dictionary<string, object> ToOpenAi(Dictionary<string, object> a)
        {
            Dictionary<string, object> o = D();
            List<object> msgs = new List<object>();
            if (a.ContainsKey("system"))
            {
                string sys = BlocksText(a["system"]);
                if (sys.Length > 0) { Dictionary<string, object> sm = D(); sm["role"] = "system"; sm["content"] = sys; msgs.Add(sm); }
            }
            List<object> list = a.ContainsKey("messages") ? a["messages"] as List<object> : null;
            if (list == null) throw new ArgumentException("messages: Field required");
            foreach (object m in list)
            {
                Dictionary<string, object> mm = m as Dictionary<string, object>;
                if (mm == null) continue;
                string role = Json.GetStr(mm, "role", "user");
                object content = mm.ContainsKey("content") ? mm["content"] : "";
                if (content is string) { Dictionary<string, object> x = D(); x["role"] = role; x["content"] = content; msgs.Add(x); continue; }
                List<object> blocks = content as List<object>;
                if (blocks == null) continue;
                List<object> parts = new List<object>(), calls = new List<object>();
                foreach (object b in blocks)
                {
                    Dictionary<string, object> bd = b as Dictionary<string, object>;
                    if (bd == null) continue;
                    string t = Json.GetStr(bd, "type", "");
                    if (t == "text") { Dictionary<string, object> p = D(); p["type"] = "text"; p["text"] = Json.GetStr(bd, "text", ""); parts.Add(p); }
                    else if (t == "image")
                    {
                        Dictionary<string, object> src = bd.ContainsKey("source") ? bd["source"] as Dictionary<string, object> : null;
                        if (src == null || Json.GetStr(src, "type", "") != "base64") continue;   // url images would need a download: refused, as in /v1/chat
                        Dictionary<string, object> iu = D(); iu["url"] = "data:" + Json.GetStr(src, "media_type", "image/png") + ";base64," + Json.GetStr(src, "data", "");
                        Dictionary<string, object> p = D(); p["type"] = "image_url"; p["image_url"] = iu; parts.Add(p);
                    }
                    else if (t == "tool_use")
                    {
                        Dictionary<string, object> fn = D(); fn["name"] = Json.GetStr(bd, "name", "tool"); fn["arguments"] = Json.Write(bd.ContainsKey("input") ? bd["input"] : D());
                        Dictionary<string, object> c = D(); c["id"] = Json.GetStr(bd, "id", ""); c["type"] = "function"; c["function"] = fn; calls.Add(c);
                    }
                    else if (t == "tool_result")
                    {
                        string txt = BlocksText(bd.ContainsKey("content") ? bd["content"] : "");
                        if (bd.ContainsKey("is_error") && bd["is_error"] is bool && (bool)bd["is_error"]) txt = "Error: " + txt;
                        Dictionary<string, object> tm = D(); tm["role"] = "tool"; tm["tool_call_id"] = Json.GetStr(bd, "tool_use_id", ""); tm["content"] = txt; msgs.Add(tm);
                    }
                    // thinking / redacted_thinking / documents: not used by this model
                }
                if (parts.Count == 0 && calls.Count == 0) continue;
                Dictionary<string, object> om = D(); om["role"] = role; om["content"] = parts;
                if (calls.Count > 0) om["tool_calls"] = calls;
                msgs.Add(om);
            }
            o["messages"] = msgs;
            if (!a.ContainsKey("max_tokens")) throw new ArgumentException("max_tokens: Field required");
            foreach (string k in new string[] { "max_tokens", "temperature", "top_p", "top_k", "model", "stream" }) if (a.ContainsKey(k)) o[k] = a[k];
            List<object> stops = a.ContainsKey("stop_sequences") ? a["stop_sequences"] as List<object> : null;
            if (stops != null && stops.Count > 0) o["stop"] = stops.Count > 4 ? stops.GetRange(0, 4) : stops;

            List<object> tools = a.ContainsKey("tools") ? a["tools"] as List<object> : null;
            Dictionary<string, object> tc = a.ContainsKey("tool_choice") ? a["tool_choice"] as Dictionary<string, object> : null;
            string tct = tc != null ? Json.GetStr(tc, "type", "auto") : "auto";
            if (tools != null && tools.Count > 0 && tct != "none")
            {
                List<object> ot = new List<object>();
                foreach (object t in tools)
                {
                    Dictionary<string, object> td = t as Dictionary<string, object>;
                    if (td == null || !td.ContainsKey("input_schema")) continue;   // server tools (web_search, ...) have no schema
                    Dictionary<string, object> fn = D(); fn["name"] = Json.GetStr(td, "name", "tool"); fn["description"] = Json.GetStr(td, "description", ""); fn["parameters"] = td["input_schema"];
                    Dictionary<string, object> w = D(); w["type"] = "function"; w["function"] = fn; ot.Add(w);
                }
                if (ot.Count > 0)
                {
                    o["tools"] = ot;
                    if (tct == "any") o["tool_choice"] = "required";
                    else if (tct == "tool") { Dictionary<string, object> f = D(); f["name"] = Json.GetStr(tc, "name", ""); Dictionary<string, object> w = D(); w["type"] = "function"; w["function"] = f; o["tool_choice"] = w; }
                }
            }
            return o;
        }

        static string AnthStopReason(string finish)
        {
            return finish == "tool_calls" ? "tool_use" : finish == "length" ? "max_tokens" : "end_turn";
        }

        // ---------- POST /v1/messages ----------
        void Messages(Dictionary<string, object> body, HttpListenerResponse res, string source)
        {
            try
            {
                Dictionary<string, object> o = ToOpenAi(body);
                bool stream = o.ContainsKey("stream") && o["stream"] is bool && (bool)o["stream"];
                string model = Json.GetStr(body, "model", null);
                if (string.IsNullOrEmpty(model) || string.Equals(model, "phoenixlai", StringComparison.OrdinalIgnoreCase)) model = LoadedModelFile != null ? Path.GetFileNameWithoutExtension(LoadedModelFile) : "phoenixlai";
                if (stream)
                {
                    Dictionary<string, object> so = D(); so["include_usage"] = true; o["stream_options"] = so;
                    ApiOut a = new ApiOut(); a.Res = res;
                    string m = model;
                    a.Wrap = delegate(Stream raw) { return new AnthropicSse(raw, m); };
                    Completion(o, a, true, source);
                    return;
                }
                ApiOut b = new ApiOut(); b.Buffer = new MemoryStream();
                Completion(o, b, true, source);
                Dictionary<string, object> r = Json.Parse(Encoding.UTF8.GetString(b.Buffer.ToArray())) as Dictionary<string, object>;
                SendJson(res, 200, AnthMessageJson(r));
            }
            catch (ApiModelNotFound ex) { Log.Text("API /v1/messages error: " + ex.Message); AnthError(res, 404, ex.Message, "not_found_error"); }
            catch (ArgumentException ex) { Log.Text("API /v1/messages error: " + ex.Message); AnthError(res, 400, ex.Message, "invalid_request_error"); }
            catch (Exception ex) { Log.Text("API /v1/messages error: " + ex.Message); try { AnthError(res, 500, ex.Message, "api_error"); } catch { } }
        }

        static string AnthMessageJson(Dictionary<string, object> r)
        {
            List<object> choices = r != null && r.ContainsKey("choices") ? r["choices"] as List<object> : null;
            Dictionary<string, object> c0 = choices != null && choices.Count > 0 ? choices[0] as Dictionary<string, object> : null;
            Dictionary<string, object> msg = c0 != null && c0.ContainsKey("message") ? c0["message"] as Dictionary<string, object> : null;
            Dictionary<string, object> usage = r != null && r.ContainsKey("usage") ? r["usage"] as Dictionary<string, object> : null;
            Dictionary<string, object> det = usage != null && usage.ContainsKey("prompt_tokens_details") ? usage["prompt_tokens_details"] as Dictionary<string, object> : null;
            StringBuilder content = new StringBuilder();
            string text = msg != null ? Json.GetStr(msg, "content", null) : null;
            if (!string.IsNullOrEmpty(text)) content.Append("{\"type\":\"text\",\"text\":").Append(Json.Quote(text)).Append('}');
            List<object> calls = msg != null && msg.ContainsKey("tool_calls") ? msg["tool_calls"] as List<object> : null;
            if (calls != null)
                foreach (object c in calls)
                {
                    Dictionary<string, object> cd = c as Dictionary<string, object>;
                    Dictionary<string, object> fn = cd != null && cd.ContainsKey("function") ? cd["function"] as Dictionary<string, object> : null;
                    if (fn == null) continue;
                    if (content.Length > 0) content.Append(',');
                    content.Append(ToolUseJson(Json.GetStr(cd, "id", ""), Json.GetStr(fn, "name", ""), Json.GetStr(fn, "arguments", "{}")));
                }
            long prompt = (long)Json.GetNum(usage, "prompt_tokens", 0), cached = (long)Json.GetNum(det, "cached_tokens", 0);
            return "{\"id\":\"msg_" + Guid.NewGuid().ToString("N").Substring(0, 24) + "\",\"type\":\"message\",\"role\":\"assistant\",\"model\":" + Json.Quote(Json.GetStr(r, "model", "phoenixlai")) +
                   ",\"content\":[" + content + "],\"stop_reason\":" + Json.Quote(AnthStopReason(Json.GetStr(c0, "finish_reason", "stop"))) + ",\"stop_sequence\":null" +
                   ",\"usage\":{\"input_tokens\":" + (prompt - cached) + ",\"cache_read_input_tokens\":" + cached + ",\"cache_creation_input_tokens\":0,\"output_tokens\":" + (long)Json.GetNum(usage, "completion_tokens", 0) + "}}";
        }

        // Anthropic ids are toolu_...; the id the client sends back in tool_result only has to match this one.
        static string ToolUseJson(string id, string name, string args)
        {
            object input = null;
            try { input = Json.Parse(string.IsNullOrEmpty(args) ? "{}" : args); } catch { }
            if (!(input is Dictionary<string, object>)) input = D();
            return "{\"type\":\"tool_use\",\"id\":" + Json.Quote(string.IsNullOrEmpty(id) ? "toolu_" + Guid.NewGuid().ToString("N").Substring(0, 24) : id) +
                   ",\"name\":" + Json.Quote(name) + ",\"input\":" + Json.Write(input) + "}";
        }

        // POST /v1/messages/count_tokens: Anthropic's {input_tokens}. Characters / 4 (the engine's tokenizer is busy behind
        // the engine lock while a request runs, and clients only use this to plan their context).
        static string CountTokensJson(Dictionary<string, object> body)
        {
            Dictionary<string, object> o = ToOpenAiLoose(body);
            return "{\"input_tokens\":" + Math.Max(1, Json.Write(o).Length / 4) + "}";
        }

        static Dictionary<string, object> ToOpenAiLoose(Dictionary<string, object> body)
        {
            Dictionary<string, object> b = new Dictionary<string, object>(body);
            if (!b.ContainsKey("max_tokens")) b["max_tokens"] = 1.0;
            return ToOpenAi(b);
        }

        // ---------- streaming: OpenAI chunks -> Anthropic events ----------
        // Completion writes whole OpenAI SSE events ("data: {...}\n\n" or ": comment\n\n"); each is turned into Anthropic's
        // message_start / content_block_* / message_delta / message_stop events, comments into ping.
        sealed class AnthropicSse : Stream
        {
            readonly Stream _out;
            readonly string _model;
            readonly MemoryStream _pending = new MemoryStream();
            bool _started, _blockOpen, _done;
            int _index = -1;
            string _stop = "end_turn";
            long _in, _cached, _outTokens;

            public AnthropicSse(Stream output, string model) { _out = output; _model = model; }

            void Emit(string ev, string json)
            {
                byte[] b = Encoding.UTF8.GetBytes("event: " + ev + "\ndata: " + json + "\n\n");
                _out.Write(b, 0, b.Length);
            }

            void Start()
            {
                if (_started) return;
                _started = true;
                Emit("message_start", "{\"type\":\"message_start\",\"message\":{\"id\":\"msg_" + Guid.NewGuid().ToString("N").Substring(0, 24) + "\",\"type\":\"message\",\"role\":\"assistant\",\"model\":" +
                     Json.Quote(_model) + ",\"content\":[],\"stop_reason\":null,\"stop_sequence\":null,\"usage\":{\"input_tokens\":0,\"output_tokens\":0}}}");
            }

            void CloseBlock()
            {
                if (!_blockOpen) return;
                _blockOpen = false;
                Emit("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":" + _index + "}");
            }

            void Finish()
            {
                if (_done) return;
                _done = true;
                Start(); CloseBlock();
                Emit("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":" + Json.Quote(_stop) + ",\"stop_sequence\":null},\"usage\":{\"input_tokens\":" + (_in - _cached) +
                     ",\"cache_read_input_tokens\":" + _cached + ",\"output_tokens\":" + _outTokens + "}}");
                Emit("message_stop", "{\"type\":\"message_stop\"}");
            }

            void Event(string e)
            {
                if (_done) return;
                if (e.StartsWith(":")) { Start(); Emit("ping", "{\"type\":\"ping\"}"); return; }
                if (!e.StartsWith("data: ")) return;
                string data = e.Substring(6).Trim();
                if (data == "[DONE]") { Finish(); return; }
                Dictionary<string, object> d = Json.Parse(data) as Dictionary<string, object>;
                if (d == null) return;
                if (d.ContainsKey("error"))
                {
                    Dictionary<string, object> er = d["error"] as Dictionary<string, object>;
                    string t = Json.GetStr(er, "type", "server_error");
                    Start();
                    Emit("error", AnthErrorJson(Json.GetStr(er, "message", "error"), t == "invalid_request_error" ? t : "api_error"));
                    _done = true;
                    return;
                }
                Start();
                Dictionary<string, object> usage = d.ContainsKey("usage") ? d["usage"] as Dictionary<string, object> : null;
                if (usage != null)
                {
                    Dictionary<string, object> det = usage.ContainsKey("prompt_tokens_details") ? usage["prompt_tokens_details"] as Dictionary<string, object> : null;
                    _in = (long)Json.GetNum(usage, "prompt_tokens", 0); _cached = (long)Json.GetNum(det, "cached_tokens", 0); _outTokens = (long)Json.GetNum(usage, "completion_tokens", 0);
                }
                List<object> choices = d.ContainsKey("choices") ? d["choices"] as List<object> : null;
                Dictionary<string, object> c0 = choices != null && choices.Count > 0 ? choices[0] as Dictionary<string, object> : null;
                if (c0 == null || Json.GetNum(c0, "index", 0) != 0) return;   // n > 1 has no Anthropic form: the first choice only
                Dictionary<string, object> delta = c0.ContainsKey("delta") ? c0["delta"] as Dictionary<string, object> : null;
                string text = delta != null ? Json.GetStr(delta, "content", null) : null;
                if (!string.IsNullOrEmpty(text))
                {
                    if (!_blockOpen)
                    {
                        _index++; _blockOpen = true;
                        Emit("content_block_start", "{\"type\":\"content_block_start\",\"index\":" + _index + ",\"content_block\":{\"type\":\"text\",\"text\":\"\"}}");
                    }
                    Emit("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":" + _index + ",\"delta\":{\"type\":\"text_delta\",\"text\":" + Json.Quote(text) + "}}");
                }
                List<object> calls = delta != null && delta.ContainsKey("tool_calls") ? delta["tool_calls"] as List<object> : null;
                if (calls != null)
                    foreach (object c in calls)
                    {
                        Dictionary<string, object> cd = c as Dictionary<string, object>;
                        Dictionary<string, object> fn = cd != null && cd.ContainsKey("function") ? cd["function"] as Dictionary<string, object> : null;
                        if (fn == null) continue;
                        CloseBlock();
                        _index++;
                        string id = Json.GetStr(cd, "id", null);
                        if (string.IsNullOrEmpty(id)) id = "toolu_" + Guid.NewGuid().ToString("N").Substring(0, 24);
                        Emit("content_block_start", "{\"type\":\"content_block_start\",\"index\":" + _index + ",\"content_block\":{\"type\":\"tool_use\",\"id\":" + Json.Quote(id) +
                             ",\"name\":" + Json.Quote(Json.GetStr(fn, "name", "")) + ",\"input\":{}}}");
                        Emit("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":" + _index + ",\"delta\":{\"type\":\"input_json_delta\",\"partial_json\":" +
                             Json.Quote(Json.GetStr(fn, "arguments", "{}")) + "}}");
                        Emit("content_block_stop", "{\"type\":\"content_block_stop\",\"index\":" + _index + "}");
                    }
                string finish = Json.GetStr(c0, "finish_reason", null);
                if (finish != null) { CloseBlock(); _stop = AnthStopReason(finish); }
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _pending.Write(buffer, offset, count);
                byte[] all = _pending.ToArray();
                int from = 0;
                for (int i = 0; i + 1 < all.Length; i++)
                    if (all[i] == '\n' && all[i + 1] == '\n') { Event(Encoding.UTF8.GetString(all, from, i - from)); from = i + 2; i++; }
                _pending.SetLength(0);
                _pending.Write(all, from, all.Length - from);
            }

            public override void Flush() { _out.Flush(); }
            public override void Close() { try { Finish(); _out.Flush(); } catch { } _out.Close(); base.Close(); }
            public override bool CanRead { get { return false; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return true; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
        }
    }
}
