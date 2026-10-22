// Phoenix Client: OpenAI /v1 providers (the Phoenix server, LM Studio, Ollama, a LAN box, an internet API) and the
// client's only way out to them. C# 5 only.
//   Store:    %LOCALAPPDATA%\LocalLLMStudio\providers.json (id, name, base URL, model; never a key). API keys live in
//             Windows Credential Manager (CredStore, resource PhoenixLAI.Connections) and are never sent back to the page.
//   Network:  approved 2026-10-20: the client may reach any OpenAI /v1 provider (local, LAN or internet) by its URL.
//             Every request is made by Outgoing() through CheckTarget(): http/https only, no user:password in the URL,
//             redirects never followed (a key cannot be forwarded to another host), TLS 1.2+; a key is only ever sent to
//             its own provider. run.ps1 rule 4c checks this file.
//   Models:   RemoteChatModel (IChatModel over streamed /chat/completions) and RemoteEmbedder (IEmbedder over /embeddings)
//             let the chat pipeline (WebServer) run on any provider.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace Phoenix
{
    public sealed class Provider
    {
        public string Id, Name, Url, Model = "";
        public bool IsPhoenix { get { return Id == Providers.PhoenixId; } }
        // What the last connection test found the provider allows (Providers.FeatureKeys); null = never tested, so
        // everything is tried. Saved in providers.json.
        public Dictionary<string, bool> Features;
        public string TestedAt = "";
        public bool Has(string feature) { bool v; return Features == null || !Features.TryGetValue(feature, out v) || v; }
        // Local context (docs-search sections, syntax sheets, RULES.md, project files and file tools, skills) goes only to
        // a provider that allows it. For now only the Phoenix Local Server (this PC) does; every other provider gets just
        // what the user types (the toggle in its window is shown off and greyed out).
        public bool SendsLocalContext { get { return IsPhoenix; } }
    }

    public static class Providers
    {
        public const string PhoenixId = "phoenix";
        public const string PhoenixUrl = "http" + "://localhost:1234/v1";
        public const string PhoenixName = "Phoenix Local Server";
        static readonly object _fileLock = new object();
        static readonly string AppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLLMStudio");
        static string StoreFile { get { return Path.Combine(AppDataDir, "providers.json"); } }
        static string OldStoreFile { get { return Path.Combine(AppDataDir, "connections.json"); } }
        static string ActiveFile { get { return Path.Combine(AppDataDir, "active-provider.txt"); } }

        static Providers()
        {
            // Streams and polling run at the same time: the default of 2 connections per host would stall them.
            ServicePointManager.DefaultConnectionLimit = 64;
            ServicePointManager.Expect100Continue = false;
            // TLS 1.2 (and 1.3 where Windows has it) for internet providers; never the old SSL3 / TLS 1.0
            try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
            catch { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
        }

        // ---------- store ----------
        public static List<Provider> All()
        {
            List<Provider> list = new List<Provider>();
            lock (_fileLock)
            {
                try
                {
                    if (!File.Exists(StoreFile) && File.Exists(OldStoreFile)) Migrate();
                    List<object> arr = File.Exists(StoreFile) ? Json.Parse(File.ReadAllText(StoreFile, Encoding.UTF8)) as List<object> : null;
                    if (arr != null)
                        foreach (object x in arr)
                        {
                            Dictionary<string, object> d = x as Dictionary<string, object>;
                            if (d == null || Json.GetStr(d, "id", null) == null) continue;
                            Provider p = new Provider();
                            p.Id = Json.GetStr(d, "id", null); p.Name = Json.GetStr(d, "name", "provider");
                            p.Url = Json.GetStr(d, "url", PhoenixUrl); p.Model = Json.GetStr(d, "model", "");
                            Dictionary<string, object> f = d.ContainsKey("features") ? d["features"] as Dictionary<string, object> : null;
                            if (f != null)
                            {
                                p.Features = new Dictionary<string, bool>();
                                foreach (KeyValuePair<string, object> kv in f) if (kv.Value is bool) p.Features[kv.Key] = (bool)kv.Value;
                            }
                            p.TestedAt = Json.GetStr(d, "testedAt", "");
                            list.Add(p);
                        }
                }
                catch { }
            }
            // The Phoenix Local Server is always there, first, at this PC's port 1234: never removed, renamed or re-pointed
            // (only its model and API key change).
            Provider ph = list.Find(delegate(Provider p) { return p.IsPhoenix; });
            if (ph == null || list.IndexOf(ph) != 0 || ph.Name != PhoenixName || ph.Url != PhoenixUrl)
            {
                if (ph == null) { ph = new Provider(); ph.Id = PhoenixId; } else list.Remove(ph);
                ph.Name = PhoenixName; ph.Url = PhoenixUrl;
                list.Insert(0, ph);
                Save(list);
            }
            return list;
        }

        // connections.json (host / port / path) from the first client: carried over once, keys stay where they are.
        static void Migrate()
        {
            List<object> arr = Json.Parse(File.ReadAllText(OldStoreFile, Encoding.UTF8)) as List<object>;
            List<Provider> list = new List<Provider>();
            if (arr != null)
                foreach (object x in arr)
                {
                    Dictionary<string, object> d = x as Dictionary<string, object>;
                    if (d == null) continue;
                    Provider p = new Provider();
                    p.Id = Json.GetStr(d, "id", null); p.Name = Json.GetStr(d, "name", "provider"); p.Model = Json.GetStr(d, "model", "");
                    string host = Json.GetStr(d, "host", "127.0.0.1");
                    p.Url = "http" + "://" + host + ":" + (int)Json.GetNum(d, "port", 1234) + Json.GetStr(d, "path", "/v1");
                    if (p.Id != null) list.Add(p);
                }
            Save(list);
        }

        public static void Save(List<Provider> list)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < list.Count; i++)
            {
                Provider p = list[i];
                if (i > 0) sb.Append(",\n");
                sb.Append("{\"id\":").Append(Json.Quote(p.Id)).Append(",\"name\":").Append(Json.Quote(p.Name)).Append(",\"url\":").Append(Json.Quote(p.Url))
                  .Append(",\"model\":").Append(Json.Quote(p.Model));
                if (p.Features != null) sb.Append(",\"features\":").Append(FeaturesJson(p)).Append(",\"testedAt\":").Append(Json.Quote(p.TestedAt));
                sb.Append('}');
            }
            lock (_fileLock)
            {
                Directory.CreateDirectory(AppDataDir);
                File.WriteAllText(StoreFile, sb.Append("]").ToString(), new UTF8Encoding(false));
            }
        }

        public static Provider Find(string id) { return All().Find(delegate(Provider p) { return p.Id == id; }); }

        public static Provider Active
        {
            get
            {
                string id = null;
                try { id = File.ReadAllText(ActiveFile).Trim(); } catch { }
                return (id != null ? Find(id) : null) ?? Find(PhoenixId);
            }
        }

        public static void SetActive(string id)
        {
            if (Find(id) == null) throw new ArgumentException("No such provider.");
            Directory.CreateDirectory(AppDataDir);
            File.WriteAllText(ActiveFile, id);
        }

        // Keys are looked up once and remembered (null = none): the status checks run every 2 s, and Credential Manager
        // answers a missing key with an exception. Saving or deleting a provider forgets its entry.
        static readonly Dictionary<string, string> _keys = new Dictionary<string, string>();
        public static string Key(Provider p)
        {
            string k;
            lock (_keys) if (_keys.TryGetValue(p.Id ?? "", out k)) return k;
            try { k = CredStore.Read(p.Id); } catch { k = null; }
            lock (_keys) _keys[p.Id ?? ""] = k;
            return k;
        }
        static bool HasKey(Provider p) { return !string.IsNullOrEmpty(Key(p)); }
        static void ForgetKey(string id) { lock (_keys) _keys.Remove(id ?? ""); }

        public static string Json1(Provider p)
        {
            bool key = false;
            key = HasKey(p);
            return "{\"id\":" + Json.Quote(p.Id) + ",\"name\":" + Json.Quote(p.Name) + ",\"url\":" + Json.Quote(p.Url) + ",\"model\":" + Json.Quote(p.Model) +
                   ",\"hasKey\":" + (key ? "true" : "false") + ",\"phoenix\":" + (p.IsPhoenix ? "true" : "false") + ",\"local\":" + (IsLocal(p.Url) ? "true" : "false") +
                   ",\"features\":" + (p.Features != null ? FeaturesJson(p) : "null") + ",\"testedAt\":" + Json.Quote(p.TestedAt) +
                   ",\"localContext\":" + (p.SendsLocalContext ? "true" : "false") + "}";
        }

        // ---------- features: what each provider allows, from its last connection test ----------
        // key, label shown in the page, and the test row that decides it. The client sends nothing a provider refused.
        public static readonly string[][] FeatureKeys = {
            new string[] { "chat", "chat", "POST /chat/completions" },
            new string[] { "stream", "streaming", "POST /chat/completions (stream)" },
            new string[] { "include_usage", "token usage in streams", "stream_options.include_usage" },
            new string[] { "system", "system message", "system message" },
            new string[] { "sampling", "temperature / top_p", "temperature / top_p" },
            new string[] { "penalties", "presence / frequency penalty", "presence / frequency_penalty" },
            new string[] { "seed", "seed", "seed" },
            new string[] { "stop", "stop sequences", "stop" },
            new string[] { "max_completion_tokens", "max_completion_tokens", "max_completion_tokens" },
            new string[] { "n", "several choices (n)", "n (several choices)" },
            new string[] { "logprobs", "logprobs", "logprobs / top_logprobs" },
            new string[] { "json_object", "JSON mode", "response_format json_object" },
            new string[] { "json_schema", "JSON schema", "response_format json_schema" },
            new string[] { "tools", "tool calling", "tools (tool_calls)" },
            new string[] { "vision", "image input", "image input (vision)" },
            new string[] { "embeddings", "embeddings", "POST /embeddings" },
            new string[] { "completions", "text completions", "POST /completions" },
            new string[] { "fim", "code fill-in (suffix)", "POST /completions + suffix (code fill-in)" },
        };

        static string FeaturesJson(Provider p)
        {
            StringBuilder sb = new StringBuilder("{");
            bool first = true;
            foreach (KeyValuePair<string, bool> kv in p.Features)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Json.Quote(kv.Key)).Append(':').Append(kv.Value ? "true" : "false");
            }
            return sb.Append('}').ToString();
        }

        // The feature map from a test's results: ok = allowed; anything else (refused, ignored, missing) = not.
        // A row the test never reached (it stopped early) leaves that feature out, so it stays "untested".
        static Dictionary<string, bool> FeaturesFrom(string testJson)
        {
            Dictionary<string, object> d = SafeParse(testJson) as Dictionary<string, object>;
            List<object> rows = d != null && d.ContainsKey("results") ? d["results"] as List<object> : null;
            Dictionary<string, bool> f = new Dictionary<string, bool>();
            if (rows == null) return f;
            foreach (string[] k in FeatureKeys)
                foreach (object x in rows)
                {
                    Dictionary<string, object> r = x as Dictionary<string, object>;
                    if (Json.GetStr(r, "name", null) != k[2]) continue;
                    string st = Json.GetStr(r, "state", "");
                    // chat and streaming only "warn" over details (no usage block): they still work
                    f[k[0]] = st == "ok" || (st == "warn" && (k[0] == "chat" || k[0] == "stream"));
                }
            return f;
        }

        static IEnumerable<string> NotAllowed(Provider p)
        {
            foreach (string[] k in FeatureKeys) if (!p.Has(k[0])) yield return k[1];
        }

        static string FeatureListJson()
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < FeatureKeys.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"key\":").Append(Json.Quote(FeatureKeys[i][0])).Append(",\"label\":").Append(Json.Quote(FeatureKeys[i][1])).Append('}');
            }
            return sb.Append(']').ToString();
        }

        // ---------- live status: online / offline / busy, for the dots in the Providers list ----------
        // Every chat or embedding call marks its provider up or down at once; between calls a short GET /models checks
        // each provider in the background (every 2 s on this PC or the LAN, every 15 s on the internet, so a cloud API's
        // rate limit is not spent on it). The page polls StatusJson and never waits for a check.
        static readonly object _stLock = new object();
        static readonly Dictionary<string, bool> _up = new Dictionary<string, bool>();
        static readonly Dictionary<string, DateTime> _checked = new Dictionary<string, DateTime>();
        static readonly Dictionary<string, int> _busy = new Dictionary<string, int>();
        static readonly HashSet<string> _checking = new HashSet<string>();
        static readonly Dictionary<string, List<ModelInfo>> _models = new Dictionary<string, List<ModelInfo>>();   // last /models per provider

        // "up", "down", "busy" or "unknown", and the provider's last known chat models (null: none seen yet).
        public static string State(Provider p, out List<ModelInfo> models)
        {
            lock (_stLock)
            {
                bool up;
                _models.TryGetValue(p.Id, out models);
                if (_busy.ContainsKey(p.Id)) return "busy";
                return !_up.TryGetValue(p.Id, out up) ? "unknown" : up ? "up" : "down";
            }
        }

        public static void Mark(string id, bool up)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_stLock) { _up[id] = up; _checked[id] = DateTime.UtcNow; }
        }

        public static void BeginUse(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_stLock) { int n; _busy.TryGetValue(id, out n); _busy[id] = n + 1; }
        }

        public static void EndUse(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (_stLock) { int n; _busy.TryGetValue(id, out n); if (n <= 1) _busy.Remove(id); else _busy[id] = n - 1; }
        }

        static void Check(Provider p)
        {
            bool up = false;
            try
            {
                HttpWebRequest o = Outgoing(p.Url + "/models");
                o.Timeout = 2500; o.ReadWriteTimeout = 2500; o.KeepAlive = false;
                string key = Key(p);
                if (!string.IsNullOrEmpty(key)) o.Headers["Authorization"] = "Bearer " + key;
                HttpWebResponse r;
                try { r = (HttpWebResponse)o.GetResponse(); }
                catch (WebException ex) { r = ex.Response as HttpWebResponse; }
                if (r != null)
                {
                    up = true;   // any HTTP answer (even 401) means the server is there
                    using (r)
                    {
                        if ((int)r.StatusCode == 200)
                        {
                            string text;
                            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) text = sr.ReadToEnd();
                            List<ModelInfo> list = ParseModels(text);
                            if (list != null) lock (_stLock) _models[p.Id] = list;   // the model picker lists these
                        }
                    }
                }
            }
            catch { }
            Mark(p.Id, up);
            lock (_stLock) _checking.Remove(p.Id);
        }

        public static string StatusJson()
        {
            StringBuilder sb = new StringBuilder("{\"status\":{");
            bool first = true;
            foreach (Provider p in All())
            {
                bool known, up, busy, start = false;
                lock (_stLock)
                {
                    known = _up.TryGetValue(p.Id, out up);
                    busy = _busy.ContainsKey(p.Id);
                    DateTime at;
                    double age = _checked.TryGetValue(p.Id, out at) ? (DateTime.UtcNow - at).TotalSeconds : 1e9;
                    if (!busy && !_checking.Contains(p.Id) && age >= (IsLocal(p.Url) ? 2 : 15)) { _checking.Add(p.Id); start = true; }
                }
                if (start) { Provider q = p; ThreadPool.QueueUserWorkItem(delegate { Check(q); }); }
                if (!first) sb.Append(',');
                first = false;
                sb.Append(Json.Quote(p.Id)).Append(":\"").Append(busy ? "busy" : !known ? "unknown" : up ? "up" : "down").Append('"');
            }
            return sb.Append("}}").ToString();
        }

        // ---------- the page's /client/* calls (routed here by WebServer in the client) ----------
        // Returns the JSON reply, or null when the path is not one of these.
        public static string Api(string path, bool post, Dictionary<string, object> body, Action<string> log)
        {
            if (path == "/client/providers")
            {
                StringBuilder sb = new StringBuilder("{\"providers\":[");
                List<Provider> list = All();
                for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json1(list[i])); }
                return sb.Append("],\"active\":").Append(Json.Quote(Active.Id)).Append("}").ToString();
            }
            if (path == "/client/provider-status") return StatusJson();
            if (path == "/client/provider-save" && post)
            {
                List<Provider> list = All();
                string id = Json.GetStr(body, "id", null);
                Provider p = id != null ? list.Find(delegate(Provider x) { return x.Id == id; }) : null;
                bool added = p == null;
                if (p == null) { p = new Provider(); p.Id = "p" + Guid.NewGuid().ToString("N").Substring(0, 10); list.Add(p); }
                if (p.IsPhoenix) { p.Name = PhoenixName; p.Url = PhoenixUrl; }   // fixed: only model and key change
                else
                {
                    string name = Json.GetStr(body, "name", "").Trim();
                    if (name.Length == 0 || name.Length > 60) throw new ArgumentException("Give the provider a name (up to 60 characters).");
                    p.Name = name;
                    p.Url = NormUrl(Json.GetStr(body, "url", ""));
                }
                p.Model = Json.GetStr(body, "model", "").Trim();
                // The key goes straight into Credential Manager; the store file never holds it.
                string key = Json.GetStr(body, "key", null);
                bool clear = body != null && body.ContainsKey("clearKey") && body["clearKey"] is bool && (bool)body["clearKey"];
                if (!string.IsNullOrEmpty(key)) CredStore.Write(p.Id, key.Trim());
                else if (clear) CredStore.Delete(p.Id);
                ForgetKey(p.Id);
                Save(list);
                if (log != null) log("provider " + (added ? "added" : "saved") + ": " + p.Name + " (" + p.Url + (CredStore.Has(p.Id) ? ", key in Credential Manager" : ", no key") + ")");
                return "{\"provider\":" + Json1(p) + "}";
            }
            if (path == "/client/provider-delete" && post)
            {
                string id = Json.GetStr(body, "id", "");
                if (id == PhoenixId) throw new ArgumentException("The Phoenix Local Server is built in and cannot be deleted.");
                List<Provider> list = All();
                list.RemoveAll(delegate(Provider x) { return x.Id == id; });
                Save(list);
                try { CredStore.Delete(id); } catch { }
                ForgetKey(id);
                if (Active.Id == id) SetActive(PhoenixId);
                if (log != null) log("provider deleted: " + id);
                return "{}";
            }
            if (path == "/client/provider-models" && post)
            {
                // the add window's "Load models": the URL and key as typed (or the saved key when editing)
                Provider p = FromBody(body);
                string key = Json.GetStr(body, "key", null);
                if (string.IsNullOrEmpty(key)) key = Key(p);
                string err;
                List<ModelInfo> models = ListModels(p, key, out err);
                if (models == null) throw new InvalidOperationException(err);
                StringBuilder sb = new StringBuilder("{\"models\":[");
                for (int i = 0; i < models.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json.Quote(models[i].Id)); }
                return sb.Append("]}").ToString();
            }
            if (path == "/client/provider-test" && post)
            {
                Provider p = FromBody(body);
                string key = Json.GetStr(body, "key", null);
                if (string.IsNullOrEmpty(key)) key = Key(p);
                string result = TestProvider(p, key);
                Dictionary<string, bool> feats = FeaturesFrom(result);
                // a saved provider tested at its saved address keeps what was found; the client then uses only that
                List<Provider> all = All();
                Provider saved = all.Find(delegate(Provider x) { return x.Id == p.Id; });
                bool kept = saved != null && saved.Url == p.Url && feats.Count > 0;
                if (kept)
                {
                    saved.Features = feats; saved.TestedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                    Save(all);
                    if (log != null) log("provider tested: " + saved.Name + ", not available: " + string.Join(", ", new List<string>(NotAllowed(saved)).ToArray()));
                }
                Provider shown = new Provider(); shown.Features = feats;
                return result.Substring(0, result.Length - 1) + ",\"features\":" + FeaturesJson(shown) + ",\"featureList\":" + FeatureListJson() + ",\"saved\":" + (kept ? "true" : "false") + "}";
            }
            if (path == "/client/features") return "{\"featureList\":" + FeatureListJson() + "}";
            return null;
        }

        // A provider from the page: a saved one by id, or the URL as typed in the add window.
        static Provider FromBody(Dictionary<string, object> body)
        {
            string id = Json.GetStr(body, "id", null), url = Json.GetStr(body, "url", null);
            Provider saved = id != null ? Find(id) : null;
            if (url == null)
            {
                if (saved == null) throw new ArgumentException("No such provider.");
                return saved;
            }
            Provider p = new Provider();
            p.Id = saved != null ? saved.Id : ""; p.Name = "test"; p.Url = NormUrl(url); p.Model = Json.GetStr(body, "model", "");
            return p;
        }

        // "http(s)://host[:port]/path" without a trailing slash; anything else is refused.
        static string NormUrl(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            CheckTarget(url + "/models");
            return url;
        }

        public static bool IsLocal(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.IsLoopback) return true;
            IPAddress ip;
            if (!IPAddress.TryParse(u.Host, out ip)) return u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || u.Host.IndexOf('.') < 0;
            byte[] b = ip.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254));
        }

        // ---------- the client's only outgoing request ----------
        // Any OpenAI /v1 provider by URL (local, LAN or internet; approved 2026-10-20). Never follows a redirect, so a
        // request (and its key) cannot be sent on to another host. Loopback goes direct; other hosts use the system proxy.
        public static HttpWebRequest Outgoing(string url)
        {
            HttpWebRequest o = (HttpWebRequest)WebRequest.Create(CheckTarget(url));
            o.AllowAutoRedirect = false;
            o.Proxy = o.RequestUri.IsLoopback ? null : WebRequest.DefaultWebProxy;
            o.KeepAlive = true;
            o.Timeout = 30000;
            o.ReadWriteTimeout = 600000;   // a long reply streams for minutes
            o.AllowReadStreamBuffering = false;
            o.UserAgent = "PhoenixLAI-Client";
            return o;
        }

        static Uri CheckTarget(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) throw new ArgumentException("Give the provider's base URL, e.g. http" + "://localhost:1234/v1.");
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("Provider URLs start with http or https (scheme, then the address).");
            if (!string.IsNullOrEmpty(u.UserInfo)) throw new ArgumentException("Put the API key in the key field, not in the URL.");
            if (u.Host.Length == 0) throw new ArgumentException("The URL has no host.");
            return u;
        }

        // ---------- calls ----------
        public sealed class ModelInfo { public string Id; public int Context; public bool Loaded; }

        // GET <url>/models; null + err when the provider does not answer with a model list.
        public static List<ModelInfo> ListModels(Provider p, string key, out string err)
        {
            int code;
            string text = Call(p, key, "GET", "/models", null, out code);
            Mark(p.Id, code != 0);
            err = null;
            if (code != 200) { err = code == 0 ? text : "HTTP " + code + ": " + Short(ErrorText(text)); return null; }
            List<ModelInfo> list = ParseModels(text);
            if (list == null) { err = "The provider's /models reply has no data list."; return null; }
            lock (_stLock) _models[p.Id ?? ""] = list;
            return list;
        }

        // A /models reply's chat models (embedding models and vision projectors left out); null when it has no data list.
        static List<ModelInfo> ParseModels(string text)
        {
            Dictionary<string, object> d = SafeParse(text) as Dictionary<string, object>;
            List<object> data = d != null && d.ContainsKey("data") ? d["data"] as List<object> : null;
            if (data == null) return null;
            List<ModelInfo> list = new List<ModelInfo>();
            foreach (object x in data)
            {
                Dictionary<string, object> m = x as Dictionary<string, object>;
                string id = Json.GetStr(m, "id", null);
                if (id == null || id.IndexOf("embed", StringComparison.OrdinalIgnoreCase) >= 0 || id.IndexOf("mmproj", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                ModelInfo mi = new ModelInfo();
                mi.Id = id;
                // context window: LM Studio / Phoenix "max_context_length", others "context_length" / "context_window"
                mi.Context = (int)Json.GetNum(m, "max_context_length", Json.GetNum(m, "context_length", Json.GetNum(m, "context_window", 0)));
                mi.Loaded = Json.GetStr(m, "state", "") == "loaded";
                list.Add(mi);
            }
            return list;
        }

        static string ErrorText(string body)
        {
            Dictionary<string, object> d = SafeParse(body) as Dictionary<string, object>;
            Dictionary<string, object> e = d != null && d.ContainsKey("error") ? d["error"] as Dictionary<string, object> : null;
            if (e != null) return Json.GetStr(e, "message", body);
            return d != null && d.ContainsKey("error") && d["error"] is string ? (string)d["error"] : body;
        }

        // One request; returns the body text (or the error), code 0 = no answer at all.
        static string Call(Provider p, string key, string method, string sub, string json, out int code)
        {
            WebHeaderCollection h;
            return Call(p, key, method, sub, json, out code, out h);
        }

        static string Call(Provider p, string key, string method, string sub, string json, out int code, out WebHeaderCollection headers)
        {
            code = 0; headers = null;
            try
            {
                HttpWebRequest o = Outgoing(p.Url + sub);
                o.Method = method;
                o.Timeout = 300000;   // the first chat may load a model
                if (!string.IsNullOrEmpty(key)) o.Headers["Authorization"] = "Bearer " + key;
                if (json != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(json);
                    o.ContentType = "application/json"; o.ContentLength = b.Length;
                    using (Stream w = o.GetRequestStream()) w.Write(b, 0, b.Length);
                }
                HttpWebResponse r;
                try { r = (HttpWebResponse)o.GetResponse(); }
                catch (WebException ex) { r = ex.Response as HttpWebResponse; if (r == null) return "not answering: " + ex.Message; }
                using (r)
                {
                    code = (int)r.StatusCode; headers = r.Headers;
                    if (code >= 300 && code < 400) return "the provider redirects to " + r.Headers["Location"] + " (redirects are not followed: use that URL)";
                    using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
                }
            }
            catch (Exception ex) { return ex.Message; }
        }

        // One whole (non-streamed) JSON request for a provider that refused streaming; throws with its error.
        public static Dictionary<string, object> PostJson(Provider p, string key, string sub, string json)
        {
            int code;
            BeginUse(p.Id);
            string text;
            try { text = Call(p, key, "POST", sub, json, out code); }
            finally { EndUse(p.Id); }
            Mark(p.Id, code != 0);
            if (code == 0) throw new InvalidOperationException(p.Name + " is not answering (" + p.Url + "): " + text);
            Dictionary<string, object> d = SafeParse(text) as Dictionary<string, object>;
            if (code != 200 || d == null) throw new InvalidOperationException(p.Name + ": " + (code != 200 ? "HTTP " + code + ": " : "") + Short(ErrorText(text)));
            return d;
        }

        public static object SafeParse(string s) { try { return Json.Parse(s); } catch { return null; } }
        static string Short(string s) { s = (s ?? "").Replace("\r", " ").Replace("\n", " "); return s.Length > 160 ? s.Substring(0, 160) + "…" : s; }

        // ---------- connection test ----------
        // Every OpenAI /v1 call the client uses: model list, chat (whole and streamed), text completion, embeddings, the
        // key and the error shape. Optional endpoints a provider does not offer are "not offered", not failures.
        public static string TestProvider(Provider c, string key)
        {
            StringBuilder sb = new StringBuilder("{\"results\":[");
            int n = 0;
            List<string> models = new List<string>();
            string loaded = null;
            Action<string, string, long, string> add = delegate(string name, string state, long ms, string detail)
            {
                if (n++ > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Json.Quote(name)).Append(",\"state\":").Append(Json.Quote(state)).Append(",\"ms\":").Append(ms)
                  .Append(",\"detail\":").Append(Json.Quote(detail)).Append('}');
            };
            List<string> info = new List<string>();
            List<string> allIds = new List<string>();
            int code; string text; WebHeaderCollection hdr;

            // the raw model list and the connection itself (TLS, reach, key, every response header: rate limits, versions)
            Stopwatch sw = Stopwatch.StartNew();
            text = Call(c, key, "GET", "/models", null, out code, out hdr);
            long listMs = sw.ElapsedMilliseconds;
            Dictionary<string, object> raw = code == 200 ? SafeParse(text) as Dictionary<string, object> : null;
            List<object> rawData = raw != null && raw.ContainsKey("data") ? raw["data"] as List<object> : null;
            if (rawData != null) foreach (object x in rawData) { string id = Json.GetStr(x as Dictionary<string, object>, "id", null); if (id != null) allIds.Add(id); }
            int noKey = -1;
            Call(c, null, "GET", "/models", null, out noKey);
            {
                Uri u = new Uri(c.Url);
                List<string[]> rows = new List<string[]>();
                rows.Add(new string[] { "base URL", c.Url });
                rows.Add(new string[] { "reach", u.IsLoopback ? "this PC" : IsLocal(c.Url) ? "local network" : "internet" });
                rows.Add(new string[] { "transport", u.Scheme == Uri.UriSchemeHttps ? "https (TLS 1.2+, certificate checked by Windows)" : "plain http" + (u.IsLoopback ? "" : " (not encrypted)") });
                rows.Add(new string[] { "proxy", u.IsLoopback ? "none (direct)" : "Windows system proxy, if set" });
                rows.Add(new string[] { "API key", (string.IsNullOrEmpty(key) ? "none sent" : "sent (Credential Manager or typed)") + " · " +
                    (noKey == 401 || noKey == 403 ? "the provider requires one" : noKey == 200 ? "the provider does not require one" : "without a key: HTTP " + noKey) });
                rows.Add(new string[] { "GET /models", code == 0 ? text : "HTTP " + code + " in " + listMs + " ms" });
                if (hdr != null) foreach (string h in hdr.AllKeys) rows.Add(new string[] { "header " + h.ToLowerInvariant(), hdr[h] });
                Section(info, "Connection", rows, null);
            }
            if (raw != null) Section(info, "GET /models (" + allIds.Count + " model" + (allIds.Count == 1 ? "" : "s") + ")", ModelRows(rawData), raw);

            sw = Stopwatch.StartNew();
            string err;
            List<ModelInfo> list = ListModels(c, key, out err);
            if (list == null) { add("GET /models", "fail", listMs, err); return Finish(sb, models, info); }
            foreach (ModelInfo m in list) { models.Add(m.Id); if (loaded == null && m.Loaded) loaded = m.Id; }
            add("GET /models", "ok", listMs, allIds.Count + " model(s), " + models.Count + " for chat");
            if (!string.IsNullOrEmpty(key))
                add("API key", "ok", 0, noKey == 401 || noKey == 403 ? "the provider requires the key and accepted it" : "accepted (this provider does not require a key)");
            string model = !string.IsNullOrEmpty(c.Model) && models.Contains(c.Model) ? c.Model : loaded ?? (models.Count > 0 ? models[0] : null);
            if (model == null) { add("chat", "fail", 0, "no chat model offered"); return Finish(sb, models, info); }
            string mq = Json.Quote(model);

            sw = Stopwatch.StartNew();
            text = Call(c, key, "POST", "/chat/completions", "{\"model\":" + mq + ",\"messages\":[{\"role\":\"user\",\"content\":\"Reply with the single word OK.\"}],\"max_tokens\":256,\"temperature\":0}", out code, out hdr);
            {
                Dictionary<string, object> d = SafeParse(text) as Dictionary<string, object>;
                if (d != null)
                {
                    List<string[]> rows = new List<string[]>();
                    foreach (KeyValuePair<string, object> kv in d) if (kv.Key != "choices") rows.Add(new string[] { kv.Key, kv.Value is string ? (string)kv.Value : Json.Write(kv.Value) });
                    if (hdr != null) foreach (string h in hdr.AllKeys) rows.Add(new string[] { "header " + h.ToLowerInvariant(), hdr[h] });
                    Section(info, "Sample chat reply (" + model + ")", rows, d);
                }
                string content = null, finish = null; bool usage = d != null && d.ContainsKey("usage");
                List<object> ch = d != null && d.ContainsKey("choices") ? d["choices"] as List<object> : null;
                if (ch != null && ch.Count > 0)
                {
                    Dictionary<string, object> c0 = ch[0] as Dictionary<string, object>;
                    Dictionary<string, object> msg = c0 != null && c0.ContainsKey("message") ? c0["message"] as Dictionary<string, object> : null;
                    content = Json.GetStr(msg, "content", null); finish = Json.GetStr(c0, "finish_reason", null);
                    if (string.IsNullOrEmpty(content) && msg != null && Json.GetStr(msg, "reasoning_content", "").Length > 0) content = "(thinking only) " + Json.GetStr(msg, "reasoning_content", "");
                }
                add("POST /chat/completions", code == 200 && content != null ? (usage && finish != null ? "ok" : "warn") : "fail", sw.ElapsedMilliseconds,
                    code == 200 ? (content != null ? "\"" + Short(content.Trim()) + "\" · " + model : "no choices[0].message.content") : "HTTP " + code + ": " + Short(ErrorText(text)));
            }

            sw = Stopwatch.StartNew();
            {
                int chunks = 0; bool done = false; long first = -1; StringBuilder streamed = new StringBuilder(); string serr = null;
                try
                {
                    RemoteChatModel.StreamRaw(c, key, "{\"model\":" + mq + ",\"messages\":[{\"role\":\"user\",\"content\":\"Count from 1 to 5.\"}],\"max_tokens\":64,\"temperature\":0,\"stream\":true}",
                        delegate(Dictionary<string, object> ev)
                        {
                            string piece = RemoteChatModel.DeltaText(ev, true);
                            if (!string.IsNullOrEmpty(piece)) { if (first < 0) first = sw.ElapsedMilliseconds; chunks++; streamed.Append(piece); }
                            return false;
                        }, delegate { return false; }, out done);
                }
                catch (Exception ex) { serr = ex.Message; }
                add("POST /chat/completions (stream)", serr == null && chunks > 0 && done ? "ok" : serr == null && chunks > 0 ? "warn" : "fail", sw.ElapsedMilliseconds,
                    serr != null ? serr : chunks + " chunks, first after " + first + " ms" + (done ? ", ended with [DONE]" : ", no [DONE]") + ": \"" + Short(streamed.ToString().Trim()) + "\"");
            }

            // embeddings with the provider's embedding model when it lists one (else the chat model, which some accept)
            string emb = null;
            foreach (string id in allIds) if (id.IndexOf("embed", StringComparison.OrdinalIgnoreCase) >= 0) { emb = id; break; }
            sw = Stopwatch.StartNew();
            text = Call(c, key, "POST", "/embeddings", "{\"model\":" + Json.Quote(emb ?? model) + ",\"input\":[\"hello world\"]}", out code);
            {
                Dictionary<string, object> d = SafeParse(text) as Dictionary<string, object>;
                List<object> data = d != null && d.ContainsKey("data") ? d["data"] as List<object> : null;
                int dims = 0;
                if (data != null && data.Count > 0) { Dictionary<string, object> e0 = data[0] as Dictionary<string, object>; List<object> v = e0 != null && e0.ContainsKey("embedding") ? e0["embedding"] as List<object> : null; if (v != null) dims = v.Count; }
                add("POST /embeddings", code == 200 && dims > 0 ? "ok" : "na", sw.ElapsedMilliseconds, code == 200 && dims > 0 ? dims + "-dim vectors · " + (emb ?? model) : "not offered (docs search uses the Phoenix server or keywords)" + (code > 0 && code != 200 ? " · HTTP " + code + ": " + Short(ErrorText(text)) : ""));
            }

            // ---------- what the provider allows: each OpenAI request option, tried on the chat model ----------
            string msgs = "\"messages\":[{\"role\":\"user\",\"content\":\"Reply with the single word OK.\"}]";
            Probe(c, key, add, "system message", "{\"model\":" + mq + ",\"messages\":[{\"role\":\"system\",\"content\":\"You answer in one word.\"},{\"role\":\"user\",\"content\":\"Say OK.\"}],\"max_tokens\":64}", null);
            Probe(c, key, add, "max_completion_tokens", "{\"model\":" + mq + "," + msgs + ",\"max_completion_tokens\":64}", null);
            Probe(c, key, add, "temperature / top_p", "{\"model\":" + mq + "," + msgs + ",\"max_tokens\":64,\"temperature\":0.7,\"top_p\":0.9}", null);
            Probe(c, key, add, "presence / frequency_penalty", "{\"model\":" + mq + "," + msgs + ",\"max_tokens\":64,\"presence_penalty\":0.5,\"frequency_penalty\":0.5}", null);
            Probe(c, key, add, "seed", "{\"model\":" + mq + "," + msgs + ",\"max_tokens\":64,\"seed\":42}", null);
            Probe(c, key, add, "stop", "{\"model\":" + mq + ",\"messages\":[{\"role\":\"user\",\"content\":\"Count from 1 to 9, comma separated.\"}],\"max_tokens\":64,\"temperature\":0,\"stop\":[\"5\"]}",
                delegate(Dictionary<string, object> d) { string t = FirstText(d); return t == null ? null : t.IndexOf('6') < 0 ? "" : "accepted but ignored (text went past the stop)"; });
            Probe(c, key, add, "n (several choices)", "{\"model\":" + mq + "," + msgs + ",\"max_tokens\":32,\"n\":2}",
                delegate(Dictionary<string, object> d) { List<object> ch = d.ContainsKey("choices") ? d["choices"] as List<object> : null; return ch != null && ch.Count >= 2 ? "" : "accepted but returned one choice"; });
            Probe(c, key, add, "logprobs / top_logprobs", "{\"model\":" + mq + "," + msgs + ",\"max_tokens\":8,\"logprobs\":true,\"top_logprobs\":3}",
                delegate(Dictionary<string, object> d) { return Json.Write(d).IndexOf("\"top_logprobs\":[{", StringComparison.Ordinal) >= 0 ? "" : "accepted but no logprobs returned"; });
            Probe(c, key, add, "response_format json_object", "{\"model\":" + mq + ",\"messages\":[{\"role\":\"user\",\"content\":\"Give a JSON object with a key ok set to true.\"}],\"max_tokens\":128,\"response_format\":{\"type\":\"json_object\"}}",
                delegate(Dictionary<string, object> d) { return SafeParse(StripFence(FirstText(d))) is Dictionary<string, object> ? "" : "accepted but the reply was not JSON"; });
            Probe(c, key, add, "response_format json_schema", "{\"model\":" + mq + ",\"messages\":[{\"role\":\"user\",\"content\":\"Name a colour.\"}],\"max_tokens\":128,\"response_format\":{\"type\":\"json_schema\",\"json_schema\":{\"name\":\"colour\",\"schema\":{\"type\":\"object\",\"properties\":{\"colour\":{\"type\":\"string\"}},\"required\":[\"colour\"]}}}}",
                delegate(Dictionary<string, object> d) { Dictionary<string, object> j = SafeParse(StripFence(FirstText(d))) as Dictionary<string, object>; return j != null && j.ContainsKey("colour") ? "" : "accepted but the reply did not follow the schema"; });
            Probe(c, key, add, "tools (tool_calls)", "{\"model\":" + mq + ",\"messages\":[{\"role\":\"user\",\"content\":\"What is the weather in Paris?\"}],\"max_tokens\":256,\"tool_choice\":\"required\",\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"description\":\"Current weather for a city\",\"parameters\":{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}}}]}",
                delegate(Dictionary<string, object> d) { return Json.Write(d).IndexOf("\"tool_calls\":[{", StringComparison.Ordinal) >= 0 ? "" : "accepted but no tool_calls came back"; });
            Probe(c, key, add, "stream_options.include_usage", null, null);   // reported from the stream test below
            {
                bool usage = false, done; string serr = null;
                sw = Stopwatch.StartNew();
                try
                {
                    RemoteChatModel.StreamRaw(c, key, "{\"model\":" + mq + "," + msgs + ",\"max_tokens\":16,\"stream\":true,\"stream_options\":{\"include_usage\":true}}",
                        delegate(Dictionary<string, object> ev) { if (ev.ContainsKey("usage") && ev["usage"] is Dictionary<string, object>) usage = true; return false; }, delegate { return false; }, out done);
                }
                catch (Exception ex) { serr = ex.Message; }
                ReplaceLast(sb, "stream_options.include_usage", serr != null ? "na" : usage ? "ok" : "warn", sw.ElapsedMilliseconds, serr ?? (usage ? "usage arrives in the last chunk" : "accepted but no usage chunk"));
            }
            Probe(c, key, add, "image input (vision)", "{\"model\":" + mq + ",\"messages\":[{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"What colour is this picture? One word.\"},{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==\"}}]}],\"max_tokens\":32}", null);
            Probe(c, key, add, "POST /completions","{\"model\":" + mq + ",\"prompt\":\"The capital of France is\",\"max_tokens\":8,\"temperature\":0}",
                delegate(Dictionary<string, object> d) { return FirstText(d) != null ? "" : "no choices[0].text"; });
            Probe(c, key, add, "POST /completions + suffix (code fill-in)", "{\"model\":" + mq + ",\"prompt\":\"def add(a, b):\\n    return \",\"suffix\":\"\\n\\nprint(add(1, 2))\",\"max_tokens\":16,\"temperature\":0}",
                delegate(Dictionary<string, object> d) { return FirstText(d) != null ? "" : "no choices[0].text"; });
            sw = Stopwatch.StartNew();
            text = Call(c, key, "POST", "/chat/completions", "{\"model\":\"phoenix-test-no-such-model\"," + msgs + ",\"max_tokens\":4}", out code);
            {
                Dictionary<string, object> d = SafeParse(text) as Dictionary<string, object>;
                bool shaped = d != null && d.ContainsKey("error") && d["error"] is Dictionary<string, object>;
                add("unknown model -> error", code >= 400 && shaped ? "ok" : code >= 400 ? "warn" : "warn", sw.ElapsedMilliseconds,
                    code >= 400 ? "HTTP " + code + (shaped ? " with an OpenAI error object: " : ", not OpenAI's error shape: ") + Short(ErrorText(text)) : "HTTP " + code + ": the provider answered anyway (it may map unknown names to its own model)");
            }

            // ---------- everything else the provider says about itself ----------
            // GET /models/{id} for each model, and the native lists LM Studio (/api/v0) and Ollama (/api) offer beside /v1.
            List<object> details = new List<object>();
            for (int i = 0; i < allIds.Count && i < 40; i++)
            {
                text = Call(c, key, "GET", "/models/" + Uri.EscapeDataString(allIds[i]), null, out code);
                object one = code == 200 ? SafeParse(text) : null;
                if (one != null) details.Add(one);
            }
            if (details.Count > 0) Section(info, "GET /models/{id} (each model)", null, details);
            string root = c.Url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? c.Url.Substring(0, c.Url.Length - 3) : null;
            if (root != null)
            {
                Provider r = new Provider(); r.Id = c.Id; r.Name = c.Name; r.Url = root;
                string[] native = { "/api/v0/models", "/api/v1/models", "/api/version", "/api/tags", "/api/ps" };
                foreach (string pth in native)
                {
                    text = Call(r, key, "GET", pth, null, out code);
                    object o = code == 200 ? SafeParse(text) : null;
                    if (o != null) Section(info, "GET " + root + pth + " (native API)", NativeRows(o), o);
                }
            }
            return Finish(sb, models, info);
        }

        // One option tried on /chat/completions (or /completions): ok, ignored (warn), or refused (na, with the reason).
        static void Probe(Provider c, string key, Action<string, string, long, string> add, string name, string json, Func<Dictionary<string, object>, string> check)
        {
            if (json == null) { add(name, "na", 0, ""); return; }   // a placeholder filled in later (ReplaceLast)
            Stopwatch sw = Stopwatch.StartNew();
            int code;
            string text = Call(c, key, "POST", json.IndexOf("\"prompt\":", StringComparison.Ordinal) >= 0 ? "/completions" : "/chat/completions", json, out code);
            Dictionary<string, object> d = SafeParse(text) as Dictionary<string, object>;
            if (code != 200 || d == null) { add(name, "na", sw.ElapsedMilliseconds, "not allowed · " + (code == 0 ? text : "HTTP " + code + ": " + Short(ErrorText(text)))); return; }
            string why = check != null ? check(d) : (FirstText(d) != null ? "" : "no reply text");
            string t = FirstText(d);
            add(name, why == null ? "warn" : why.Length == 0 ? "ok" : "warn", sw.ElapsedMilliseconds,
                why == null ? "no reply text" : why.Length == 0 ? "allowed" + (t != null ? ": \"" + Short(t.Trim()) + "\"" : "") : why);
        }

        // The last results row (a placeholder) with its real outcome.
        static void ReplaceLast(StringBuilder sb, string name, string state, long ms, string detail)
        {
            string ph = "{\"name\":" + Json.Quote(name) + ",\"state\":\"na\",\"ms\":0,\"detail\":\"\"}";
            int at = sb.ToString().LastIndexOf(ph, StringComparison.Ordinal);
            if (at < 0) return;
            sb.Remove(at, ph.Length).Insert(at, "{\"name\":" + Json.Quote(name) + ",\"state\":" + Json.Quote(state) + ",\"ms\":" + ms + ",\"detail\":" + Json.Quote(detail) + "}");
        }

        // choices[0].message.content (chat) or choices[0].text (completions), or null.
        static string FirstText(Dictionary<string, object> d)
        {
            List<object> ch = d != null && d.ContainsKey("choices") ? d["choices"] as List<object> : null;
            Dictionary<string, object> c0 = ch != null && ch.Count > 0 ? ch[0] as Dictionary<string, object> : null;
            if (c0 == null) return null;
            Dictionary<string, object> msg = c0.ContainsKey("message") ? c0["message"] as Dictionary<string, object> : null;
            string t = msg != null ? Json.GetStr(msg, "content", null) : Json.GetStr(c0, "text", null);
            return string.IsNullOrEmpty(t) ? null : t;
        }

        static string StripFence(string s)
        {
            s = (s ?? "").Trim();
            if (!s.StartsWith("```")) return s;
            int nl = s.IndexOf('\n'); int end = s.LastIndexOf("```", StringComparison.Ordinal);
            return nl > 0 && end > nl ? s.Substring(nl + 1, end - nl - 1).Trim() : s;
        }

        // One info section for the test area: a title, key/value rows, and the raw JSON the provider sent.
        static void Section(List<string> info, string title, List<string[]> rows, object raw)
        {
            StringBuilder s = new StringBuilder("{\"title\":").Append(Json.Quote(title)).Append(",\"rows\":[");
            if (rows != null)
                for (int i = 0; i < rows.Count; i++) { if (i > 0) s.Append(','); s.Append('[').Append(Json.Quote(rows[i][0])).Append(',').Append(Json.Quote(rows[i][1])).Append(']'); }
            s.Append("],\"raw\":").Append(raw != null ? Json.Write(raw) : "null").Append('}');
            info.Add(s.ToString());
        }

        // One row per model: its id, then every other field it reports.
        static List<string[]> ModelRows(List<object> data)
        {
            List<string[]> rows = new List<string[]>();
            if (data == null) return rows;
            foreach (object x in data)
            {
                Dictionary<string, object> m = x as Dictionary<string, object>;
                if (m == null) continue;
                StringBuilder v = new StringBuilder();
                foreach (KeyValuePair<string, object> kv in m)
                {
                    if (kv.Key == "id") continue;
                    if (v.Length > 0) v.Append(" · ");
                    v.Append(kv.Key).Append(' ').Append(kv.Value is string ? (string)kv.Value : Json.Write(kv.Value));
                }
                rows.Add(new string[] { Json.GetStr(m, "id", "?"), v.ToString() });
            }
            return rows;
        }

        static List<string[]> NativeRows(object o)
        {
            Dictionary<string, object> d = o as Dictionary<string, object>;
            List<object> list = d != null ? (d.ContainsKey("data") ? d["data"] as List<object> : d.ContainsKey("models") ? d["models"] as List<object> : null) : o as List<object>;
            if (list != null)
            {
                List<string[]> rows = new List<string[]>();
                foreach (object x in list)
                {
                    Dictionary<string, object> m = x as Dictionary<string, object>;
                    if (m != null && !m.ContainsKey("id")) { m = new Dictionary<string, object>(m); m["id"] = Json.GetStr(m, "name", Json.GetStr(m, "key", Json.GetStr(m, "model", "?"))); }
                    if (m != null) { List<object> one = new List<object>(); one.Add(m); rows.AddRange(ModelRows(one)); }
                }
                return rows;
            }
            List<string[]> kvs = new List<string[]>();
            if (d != null) foreach (KeyValuePair<string, object> kv in d) kvs.Add(new string[] { kv.Key, kv.Value is string ? (string)kv.Value : Json.Write(kv.Value) });
            return kvs;
        }

        static string Finish(StringBuilder sb, List<string> models, List<string> info)
        {
            sb.Append("],\"models\":[");
            for (int i = 0; i < models.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Json.Quote(models[i])); }
            sb.Append("],\"info\":[");
            for (int i = 0; i < info.Count; i++) { if (i > 0) sb.Append(','); sb.Append(info[i]); }
            return sb.Append("]}").ToString();
        }
    }

    // ---------- the chat model over a provider's /v1/chat/completions ----------
    public sealed class RemoteChatModel : IChatModel
    {
        readonly Provider _p;
        readonly string _model;
        int _ctx, _used;

        public RemoteChatModel(Provider p, string model, int context)
        {
            _p = p; _model = model; _ctx = context > 0 ? context : 32768;
        }

        public string ModelPath { get { return _model; } }
        public string ModelName { get { return _model; } }
        public string Backend { get { return _p.Name; } }
        public string ProviderId { get { return _p.Id; } }
        public int ContextLength { get { return _ctx; } }
        public bool HasVision { get { Refresh(); return _p.Has("vision"); } }   // untested: the provider says so if its model cannot read pictures

        // A test since this model was picked may have changed what the provider allows: use its saved features now.
        void Refresh()
        {
            Provider now = Providers.Find(_p.Id);
            if (now != null && now.Url == _p.Url) { _p.Features = now.Features; _p.TestedAt = now.TestedAt; }
        }
        public int UsedTokens { get { return _used; } }
        public Action<int, int> PrefillProgress { get; set; }
        public string RecallDir { get; set; }
        public int RecallFiles { get { return 0; } }
        public long RecallBytes { get { return 0; } }
        public bool PreRead(string text, CancelCheck cancel) { return false; }
        public void RegisterRecall(string text) { }
        public void WarmUp() { }
        public bool Vram(out ulong used, out ulong budget) { used = budget = 0; return false; }
        public void Dispose() { }

        public GenerationStats Generate(List<ChatMessage> messages, SamplerSettings s, int maxTokens, TextCallback onText, CancelCheck cancel)
        {
            Refresh();
            // Only what the provider's last test allowed (an untested provider gets everything): no stream -> one whole
            // reply; no system role -> the system text goes in front of the first user message; no sampling / seed /
            // usage fields it refused; no pictures it cannot read.
            bool stream = _p.Has("stream");
            if (!_p.Has("system") && messages.Exists(delegate(ChatMessage x) { return x.Role == "system"; }))
            {
                List<ChatMessage> merged = new List<ChatMessage>();
                string sys = "";
                foreach (ChatMessage m in messages) if (m.Role == "system") sys += (sys.Length > 0 ? "\n\n" : "") + m.Text;
                bool put = false;
                foreach (ChatMessage m in messages)
                {
                    if (m.Role == "system") continue;
                    if (!put && m.Role == "user") { ChatMessage u = new ChatMessage("user", sys + "\n\n" + m.Text); u.Images = m.Images; merged.Add(u); put = true; }
                    else merged.Add(m);
                }
                if (!put) merged.Insert(0, new ChatMessage("user", sys));
                messages = merged;
            }
            StringBuilder sb = new StringBuilder("{\"model\":").Append(Json.Quote(_model))
                .Append(stream ? ",\"stream\":true" + (_p.Has("include_usage") ? ",\"stream_options\":{\"include_usage\":true}" : "") : ",\"stream\":false").Append(",\"messages\":[");
            for (int i = 0; i < messages.Count; i++)
            {
                ChatMessage m = messages[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"role\":").Append(Json.Quote(m.Role)).Append(",\"content\":");
                if (m.Images != null && m.Images.Count > 0 && _p.Has("vision"))
                {
                    sb.Append("[{\"type\":\"text\",\"text\":").Append(Json.Quote(m.Text ?? "")).Append('}');
                    foreach (byte[] img in m.Images)
                        sb.Append(",{\"type\":\"image_url\",\"image_url\":{\"url\":").Append(Json.Quote("data:" + Mime(img) + ";base64," + Convert.ToBase64String(img))).Append("}}");
                    sb.Append(']');
                }
                else sb.Append(Json.Quote(m.Text ?? ""));
                sb.Append('}');
            }
            sb.Append("]");
            if (_p.Has("sampling")) { CultureInfoNum(sb, "temperature", s.Temperature); CultureInfoNum(sb, "top_p", s.TopP); }
            if (maxTokens > 0 && maxTokens < int.MaxValue) sb.Append(",\"max_tokens\":").Append(maxTokens);
            if (s.Seed > 0 && _p.Has("seed")) sb.Append(",\"seed\":").Append(s.Seed);
            if (_p.IsPhoenix)   // Phoenix's own sampling fields (other providers may refuse unknown ones)
            {
                sb.Append(",\"top_k\":").Append(s.TopK);
                CultureInfoNum(sb, "repeat_penalty", s.RepeatPenalty);
            }
            sb.Append('}');

            GenerationStats st = new GenerationStats();
            Stopwatch sw = Stopwatch.StartNew();
            double firstAt = -1;
            int pieces = 0;
            string finish = null;
            Dictionary<string, object> usage = null;
            bool done;
            if (!stream)
            {
                Dictionary<string, object> whole = Providers.PostJson(_p, Providers.Key(_p), "/chat/completions", sb.ToString());
                usage = whole.ContainsKey("usage") ? whole["usage"] as Dictionary<string, object> : null;
                List<object> wc = whole.ContainsKey("choices") ? whole["choices"] as List<object> : null;
                Dictionary<string, object> w0 = wc != null && wc.Count > 0 ? wc[0] as Dictionary<string, object> : null;
                Dictionary<string, object> wm = w0 != null && w0.ContainsKey("message") ? w0["message"] as Dictionary<string, object> : null;
                string all = Json.GetStr(wm, "content", "");
                finish = Json.GetStr(w0, "finish_reason", null);
                firstAt = 0;   // one whole reply: its time is all generation (prompt reading is not visible)
                if (all.Length > 0) { pieces = 1; Interlocked.Increment(ref ChatEngine.LiveTokens); if (onText != null) onText(all); }
            }
            else
            StreamRaw(_p, Providers.Key(_p), sb.ToString(), delegate(Dictionary<string, object> ev)
            {
                if (ev.ContainsKey("usage") && ev["usage"] is Dictionary<string, object>) usage = (Dictionary<string, object>)ev["usage"];
                List<object> ch = ev.ContainsKey("choices") ? ev["choices"] as List<object> : null;
                Dictionary<string, object> c0 = ch != null && ch.Count > 0 ? ch[0] as Dictionary<string, object> : null;
                string f = Json.GetStr(c0, "finish_reason", null);
                if (f != null) finish = f;
                string piece = DeltaText(ev, false);
                if (!string.IsNullOrEmpty(piece))
                {
                    if (firstAt < 0) firstAt = sw.Elapsed.TotalSeconds;
                    pieces++;
                    Interlocked.Increment(ref ChatEngine.LiveTokens);
                    if (onText != null) onText(piece);
                }
                return cancel != null && cancel();
            }, cancel, out done);
            double total = sw.Elapsed.TotalSeconds;
            int prompt = usage != null ? (int)Json.GetNum(usage, "prompt_tokens", 0) : 0;
            int cached = 0;
            Dictionary<string, object> pd = usage != null && usage.ContainsKey("prompt_tokens_details") ? usage["prompt_tokens_details"] as Dictionary<string, object> : null;
            if (pd != null) cached = (int)Json.GetNum(pd, "cached_tokens", 0);
            st.ReusedTokens = cached;
            st.PromptTokens = Math.Max(0, prompt - cached);
            st.GeneratedTokens = usage != null ? (int)Json.GetNum(usage, "completion_tokens", pieces) : pieces;
            st.PromptSeconds = firstAt >= 0 ? firstAt : total;
            st.GenerateSeconds = firstAt >= 0 ? Math.Max(0.001, total - firstAt) : 0;
            st.StopReason = cancel != null && cancel() ? "cancelled" : finish == "length" ? "max tokens" : finish == "tool_calls" ? "tool call" : "end of turn";
            if (usage != null) _used = (int)Json.GetNum(usage, "total_tokens", prompt + st.GeneratedTokens);
            return st;
        }

        static void CultureInfoNum(StringBuilder sb, string name, double v)
        {
            sb.Append(",\"").Append(name).Append("\":").Append(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        static string Mime(byte[] b)
        {
            if (b.Length > 3 && b[0] == 0x89 && b[1] == 0x50) return "image/png";
            if (b.Length > 2 && b[0] == 0xFF && b[1] == 0xD8) return "image/jpeg";
            if (b.Length > 3 && b[0] == 0x47 && b[1] == 0x49) return "image/gif";
            if (b.Length > 11 && b[8] == 0x57 && b[9] == 0x45) return "image/webp";
            return "image/png";
        }

        // The text of one streamed chunk: content, or (withThinking) a thinking model's reasoning.
        public static string DeltaText(Dictionary<string, object> ev, bool withThinking)
        {
            List<object> ch = ev.ContainsKey("choices") ? ev["choices"] as List<object> : null;
            Dictionary<string, object> c0 = ch != null && ch.Count > 0 ? ch[0] as Dictionary<string, object> : null;
            Dictionary<string, object> d = c0 != null && c0.ContainsKey("delta") ? c0["delta"] as Dictionary<string, object> : null;
            string piece = Json.GetStr(d, "content", null);
            if (string.IsNullOrEmpty(piece) && withThinking) piece = Json.GetStr(d, "reasoning_content", null);
            return piece;
        }

        // POSTs a streamed chat and calls onEvent for every data: event until [DONE] (onEvent returns true to stop).
        // A provider error (HTTP status or an error event) throws with the provider's message. cancel aborts the request
        // even while it waits (a timer checks it every 250 ms).
        public static void StreamRaw(Provider p, string key, string json, Func<Dictionary<string, object>, bool> onEvent, CancelCheck cancel, out bool done)
        {
            done = false;
            HttpWebRequest o = Providers.Outgoing(p.Url + "/chat/completions");
            o.Method = "POST";
            o.Timeout = 600000;
            o.Accept = "text/event-stream";
            if (!string.IsNullOrEmpty(key)) o.Headers["Authorization"] = "Bearer " + key;
            byte[] b = Encoding.UTF8.GetBytes(json);
            o.ContentType = "application/json"; o.ContentLength = b.Length;
            bool aborted = false;
            Timer watch = cancel == null ? null : new Timer(delegate { try { if (!aborted && cancel()) { aborted = true; o.Abort(); } } catch { } }, null, 250, 250);
            Providers.BeginUse(p.Id);
            try
            {
                HttpWebResponse r;
                try { using (Stream w = o.GetRequestStream()) w.Write(b, 0, b.Length); r = (HttpWebResponse)o.GetResponse(); }
                catch (WebException ex)
                {
                    if (aborted) return;
                    r = ex.Response as HttpWebResponse;
                    if (r == null) { Providers.Mark(p.Id, false); throw new InvalidOperationException(p.Name + " is not answering (" + p.Url + "): " + ex.Message); }
                }
                Providers.Mark(p.Id, true);
                using (r)
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                {
                    int code = (int)r.StatusCode;
                    if (code >= 300 && code < 400) throw new InvalidOperationException(p.Name + " redirects to " + r.Headers["Location"] + "; redirects are not followed, use that URL.");
                    if (code != 200)
                    {
                        string body = sr.ReadToEnd();
                        Dictionary<string, object> d = Providers.SafeParse(body) as Dictionary<string, object>;
                        Dictionary<string, object> e = d != null && d.ContainsKey("error") ? d["error"] as Dictionary<string, object> : null;
                        throw new InvalidOperationException(p.Name + ": " + (e != null ? Json.GetStr(e, "message", body) : "HTTP " + code + " " + (body.Length > 200 ? body.Substring(0, 200) : body)));
                    }
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!line.StartsWith("data:")) continue;
                        string data = line.Substring(5).Trim();
                        if (data == "[DONE]") { done = true; break; }
                        Dictionary<string, object> ev = Providers.SafeParse(data) as Dictionary<string, object>;
                        if (ev == null) continue;
                        if (ev.ContainsKey("error"))
                        {
                            Dictionary<string, object> e = ev["error"] as Dictionary<string, object>;
                            throw new InvalidOperationException(p.Name + ": " + (e != null ? Json.GetStr(e, "message", data) : data));
                        }
                        if (onEvent(ev)) { aborted = true; o.Abort(); break; }
                    }
                }
            }
            catch (WebException) { if (!aborted) throw; }
            catch (IOException) { if (!aborted) throw; }
            finally { if (watch != null) watch.Dispose(); Providers.EndUse(p.Id); }
        }
    }

    // ---------- embeddings over a provider's /v1/embeddings (docs search, project index) ----------
    public sealed class RemoteEmbedder : IEmbedder
    {
        readonly Provider _p;
        readonly int _dim;

        // Throws when the provider has no working /embeddings (the caller then uses keyword search only).
        public RemoteEmbedder(Provider p)
        {
            _p = p;
            if (!p.Has("embeddings")) throw new InvalidOperationException(p.Name + " does not offer embeddings (its last test).");
            _dim = Embed("dimension probe").Length;
        }

        public string ModelPath { get { return _p.Name + " embeddings"; } }
        public string Backend { get { return _p.Name; } }
        public int Dim { get { return _dim; } }
        // EmbeddingGemma's task prefixes (the Phoenix server embeds with it), the same as the local Embedder
        public float[] Query(string text) { return Embed("task: search result | query: " + text); }
        public float[] Document(string title, string text) { return Embed("title: " + (string.IsNullOrEmpty(title) ? "none" : title) + " | text: " + text); }
        public void Dispose() { }

        public float[] Embed(string text)
        {
            HttpWebRequest o = Providers.Outgoing(_p.Url + "/embeddings");
            o.Method = "POST";
            o.Timeout = 60000;
            string key = Providers.Key(_p);
            if (!string.IsNullOrEmpty(key)) o.Headers["Authorization"] = "Bearer " + key;
            byte[] b = Encoding.UTF8.GetBytes("{\"model\":\"embeddings\",\"input\":" + Json.Quote(text) + ",\"encoding_format\":\"float\"}");
            o.ContentType = "application/json"; o.ContentLength = b.Length;
            Providers.BeginUse(_p.Id);
            try
            {
            HttpWebResponse r;
            try { using (Stream w = o.GetRequestStream()) w.Write(b, 0, b.Length); r = (HttpWebResponse)o.GetResponse(); }
            catch (WebException ex) { Providers.Mark(_p.Id, ex.Response != null); throw; }
            Providers.Mark(_p.Id, true);
            using (r)
            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
            {
                Dictionary<string, object> d = Providers.SafeParse(sr.ReadToEnd()) as Dictionary<string, object>;
                List<object> data = d != null && d.ContainsKey("data") ? d["data"] as List<object> : null;
                Dictionary<string, object> e0 = data != null && data.Count > 0 ? data[0] as Dictionary<string, object> : null;
                List<object> v = e0 != null && e0.ContainsKey("embedding") ? e0["embedding"] as List<object> : null;
                if (v == null || v.Count == 0) throw new InvalidOperationException(_p.Name + " returned no embedding.");
                float[] f = new float[v.Count];
                for (int i = 0; i < f.Length; i++) f[i] = (float)Convert.ToDouble(v[i], System.Globalization.CultureInfo.InvariantCulture);
                return f;
            }
            }
            finally { Providers.EndUse(_p.Id); }
        }
    }
}
