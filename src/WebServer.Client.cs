// Phoenix Client side of WebServer (IsClient): the model is the active OpenAI /v1 provider's (Providers.cs), picked from
// that provider's /models. C# 5 only. No networking here: every request goes through Providers.Outgoing.
//   GET  /api/models          the active provider's chat models (the page's model picker)
//   POST /api/load {name}     use that model of the active provider (no GPU work in the client)
//   POST /client/use {id}     switch provider (then its saved model)
//   /client/providers, /client/provider-save | -delete | -models | -test   Providers.Api
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        List<Providers.ModelInfo> _providerModels;   // the active provider's last /models answer
        string _providerError;                         // why the active provider gave no models ("" when fine)

        // Uses `model` of the active provider; null = its saved model, else the one it has loaded, else its first.
        public void UseProviderModel(string model)
        {
            Provider p = Providers.Active;
            string err;
            List<Providers.ModelInfo> list = Providers.ListModels(p, Providers.Key(p), out err);
            _providerModels = list;
            _providerError = err ?? "";
            if (list == null) { lock (_engineLock) { _engine = null; _loadedSig = null; } throw new InvalidOperationException(p.Name + ": " + err); }
            Providers.ModelInfo pick = null;
            string want = model ?? p.Model;
            if (!string.IsNullOrEmpty(want)) pick = list.Find(delegate(Providers.ModelInfo m) { return m.Id == want; });
            if (pick == null && model != null) throw new ArgumentException(p.Name + " has no model \"" + model + "\".");
            if (pick == null) pick = list.Find(delegate(Providers.ModelInfo m) { return m.Loaded; });
            if (pick == null && list.Count > 0) pick = list[0];
            lock (_engineLock)
            {
                if (pick == null) { _engine = null; _loadedSig = null; Log.Text("provider " + p.Name + ": no chat models"); return; }
                _engine = new RemoteChatModel(p, pick.Id, ContextLength > 0 ? ContextLength : pick.Context);
                _loadedSig = p.Id + "|" + pick.Id;
                _loadError = null;
            }
            if (p.Model != pick.Id)
            {
                List<Provider> all = Providers.All();
                Provider saved = all.Find(delegate(Provider x) { return x.Id == p.Id; });
                if (saved != null) { saved.Model = pick.Id; Providers.Save(all); }
            }
            Log.Text("model: " + pick.Id + " on " + p.Name + " (" + p.Url + ")");
            Log.Json("model_load", "\"file\":" + Json.Quote(pick.Id) + ",\"model\":" + Json.Quote(pick.Id) + ",\"backend\":" + Json.Quote(p.Name) + ",\"context\":" + _engine.ContextLength);
        }

        // The client with no model yet (just started, or the provider was not answering before): pick the active
        // provider's saved model now. Throws with the provider's error when it still does not answer. No-op on the server.
        void EnsureClientModel()
        {
            if (IsClient && _engine == null) UseProviderModel(null);
        }

        // The model picker's list: the active provider's chat models, with Phoenix's per-model sampling defaults.
        string ProviderModelsJson()
        {
            Provider p = Providers.Active;
            string err;
            List<Providers.ModelInfo> list = Providers.ListModels(p, Providers.Key(p), out err);
            if (list != null) _providerModels = list;
            _providerError = err ?? "";
            StringBuilder sb = new StringBuilder("{\"models\":[");
            if (list != null)
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"name\":").Append(Json.Quote(list[i].Id)).Append(",\"mb\":0,\"kvq\":false,\"context\":").Append(list[i].Context)
                      .Append(",\"defaults\":").Append(SystemPrompts.DefaultsJson(list[i].Id)).Append('}');
                }
            // every provider with its state and last known models (the picker groups them; offline ones are greyed out)
            sb.Append("],\"groups\":[");
            List<Provider> all = Providers.All();
            for (int i = 0; i < all.Count; i++)
            {
                List<Providers.ModelInfo> ms;
                string state = Providers.State(all[i], out ms);
                if (all[i].Id == p.Id && list != null) ms = list;
                if (i > 0) sb.Append(',');
                sb.Append("{\"provider\":").Append(Providers.Json1(all[i])).Append(",\"state\":").Append(Json.Quote(state)).Append(",\"models\":[");
                if (ms != null)
                    for (int j = 0; j < ms.Count; j++) { if (j > 0) sb.Append(','); sb.Append("{\"name\":").Append(Json.Quote(ms[j].Id)).Append(",\"context\":").Append(ms[j].Context).Append('}'); }
                sb.Append("]}");
            }
            return sb.Append("],\"provider\":").Append(Providers.Json1(p)).Append(",\"error\":").Append(err != null ? Json.Quote(err) : "null")
                     .Append(",\"root\":").Append(Json.Quote(Workspace.ProjectsRoot)).Append("}").ToString();
        }

        // The page's /api/* calls that work differently in the client; null = not one of them.
        string ClientApi(string path, bool post, Dictionary<string, object> body)
        {
            if (path == "/api/models") return ProviderModelsJson();
            if (path == "/api/load" && post)
            {
                string name = Json.GetStr(body, "name", null);
                if (string.IsNullOrEmpty(name)) throw new ArgumentException("Pick a model from the list.");
                int ctx = (int)Json.GetNum(body, "context", 0);
                ContextLength = ctx >= 256 ? ctx : 0;
                // a model of another provider (the picker lists them all): that provider becomes the active one
                string pid = Json.GetStr(body, "provider", null);
                if (!string.IsNullOrEmpty(pid) && pid != Providers.Active.Id) Providers.SetActive(pid);
                Stopwatch sw = Stopwatch.StartNew();
                UseProviderModel(name);
                return Info();
            }
            if (path == "/api/unload" && post) { lock (_engineLock) { _engine = null; _loadedSig = null; } return Info(); }
            if (path == "/client/use" && post)
            {
                Providers.SetActive(Json.GetStr(body, "id", ""));
                lock (_engineLock) { _engine = null; _loadedSig = null; }
                try { UseProviderModel(null); } catch (Exception ex) { _loadError = ex.Message; }
                return Info();
            }
            return Providers.Api(path, post, body, delegate(string m) { Log.Text(m); });
        }

        // Paths the client has nothing for: the server's GPU, VRAM, device and Instant Recall settings.
        static bool ServerOnlyPath(string path)
        {
            return path == "/api/gpu" || path == "/api/vram" || path == "/api/device" || path == "/api/recall" || path == "/api/pretype" ||
                   path == "/api/server-status" || path == "/api/console";
        }

        // Paths the server no longer serves: the chat page and everything behind it live in the Phoenix Client.
        static bool ClientOnlyPath(string path)
        {
            if (path == "/api/models" || path == "/api/load" || path == "/api/unload" || path == "/api/gpu" || path == "/api/vram" ||
                path == "/api/debug" || path == "/api/api-format" || path == "/api/api-port" || path == "/api/sampling" || path == "/api/server-status" || path == "/api/console" || path == "/api/bye" || path == "/api/info" || path == "/api/device")
                return false;
            return path.StartsWith("/api/");
        }

        string ProviderInfoJson()
        {
            Provider p = Providers.Active;
            return ",\"client\":true,\"provider\":" + Providers.Json1(p) + ",\"providerError\":" + (string.IsNullOrEmpty(_providerError) ? "null" : Json.Quote(_providerError));
        }
    }
}
