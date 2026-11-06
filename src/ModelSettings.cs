// Per-model generation settings, kept by the Phoenix server (not the client). C# 5 only.
// The client is a plain OpenAI /v1 client: it sends messages and nothing model-specific, so it works with any provider
// (cloud models refused the top_p / top_k the client used to send, 2026-11-04). A request that leaves a value out gets
// the server's setting for the model that answers it; a value the request does send still wins (OpenAI semantics).
// Defaults: Google's recommended Gemma 4 sampling (docs\gemma-4\gemma-4-overview.md, "Recommended Settings":
// temperature 1.0, top_p 0.95, top_k 64), no repeat penalty and no reply limit (until the context is full).
// Changed per model with the console ("sampling") or the dashboard; saved in %LOCALAPPDATA%\LocalLLMStudio\model-settings.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Phoenix
{
    public sealed class ModelSampling
    {
        public double Temperature = ModelSettings.GemmaTemperature;
        public int TopK = ModelSettings.GemmaTopK;
        public double TopP = ModelSettings.GemmaTopP;
        public double RepeatPenalty = 1.0;   // 1 = off
        public int MaxTokens = 0;            // 0 = no limit

        public ModelSampling Clone() { return (ModelSampling)MemberwiseClone(); }

        public string ToJson()
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            return "{\"temperature\":" + Temperature.ToString("R", ic) + ",\"top_k\":" + TopK + ",\"top_p\":" + TopP.ToString("R", ic) +
                   ",\"repeat_penalty\":" + RepeatPenalty.ToString("R", ic) + ",\"max_tokens\":" + MaxTokens + "}";
        }

        public override string ToString()
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            return "temperature " + Temperature.ToString(ic) + ", top_k " + TopK + ", top_p " + TopP.ToString(ic) +
                   ", repeat_penalty " + RepeatPenalty.ToString(ic) + ", max_tokens " + (MaxTokens > 0 ? MaxTokens.ToString(ic) : "none");
        }
    }

    public static class ModelSettings
    {
        public const double GemmaTemperature = 1.0, GemmaTopP = 0.95;
        public const int GemmaTopK = 64;
        public static readonly string[] Keys = { "temperature", "top_k", "top_p", "repeat_penalty", "max_tokens" };

        static readonly object _gate = new object();
        static Dictionary<string, ModelSampling> _byModel;   // model file name (lower case) -> settings changed from the defaults

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalLLMStudio", "model-settings.json"); }
        }

        static string KeyOf(string model)
        {
            string n = Path.GetFileName(model ?? "").ToLowerInvariant();
            return n.EndsWith(".gguf") ? n : n + ".gguf";
        }

        // The settings for a model (a copy): what was saved for it, else the Gemma 4 defaults.
        public static ModelSampling For(string model)
        {
            lock (_gate)
            {
                Load();
                ModelSampling s;
                return _byModel.TryGetValue(KeyOf(model), out s) ? s.Clone() : new ModelSampling();
            }
        }

        public static bool IsDefault(string model) { lock (_gate) { Load(); return !_byModel.ContainsKey(KeyOf(model)); } }

        // Sets one value ("temperature", "top_k", "top_p", "repeat_penalty", "max_tokens") for a model and saves it.
        public static ModelSampling Set(string model, string key, double v)
        {
            lock (_gate)
            {
                ModelSampling s = For(model);
                Apply(s, key, v);
                _byModel[KeyOf(model)] = s;
                Save();
                return s.Clone();
            }
        }

        // Replaces all of a model's values at once (the dashboard's Save).
        public static ModelSampling SetAll(string model, Dictionary<string, object> o)
        {
            lock (_gate)
            {
                ModelSampling s = For(model);
                foreach (string k in Keys) if (o != null && o.ContainsKey(k) && o[k] is double) Apply(s, k, (double)o[k]);
                _byModel[KeyOf(model)] = s;
                Save();
                return s.Clone();
            }
        }

        // Back to the Gemma 4 defaults for a model.
        public static ModelSampling Reset(string model)
        {
            lock (_gate) { Load(); _byModel.Remove(KeyOf(model)); Save(); return new ModelSampling(); }
        }

        static void Apply(ModelSampling s, string key, double v)
        {
            switch (key)
            {
                case "temperature":
                    if (v < 0 || v > 2) throw new ArgumentException("temperature must be from 0 to 2 (0 = always the most likely token).");
                    s.Temperature = v; break;
                case "top_k":
                    if (v < 0 || v > 1000 || v != Math.Floor(v)) throw new ArgumentException("top_k must be a whole number from 0 to 1000 (0 = off).");
                    s.TopK = (int)v; break;
                case "top_p":
                    if (v <= 0 || v > 1) throw new ArgumentException("top_p must be greater than 0 and at most 1 (1 = off).");
                    s.TopP = v; break;
                case "repeat_penalty":
                    if (v < 1 || v > 2) throw new ArgumentException("repeat_penalty must be from 1 to 2 (1 = off).");
                    s.RepeatPenalty = v; break;
                case "max_tokens":
                    if (v < 0 || v > 1000000 || v != Math.Floor(v)) throw new ArgumentException("max_tokens must be a whole number, 0 = no limit.");
                    s.MaxTokens = (int)v; break;
                default:
                    throw new ArgumentException("Unknown setting \"" + key + "\": use " + string.Join(", ", Keys) + ".");
            }
        }

        // Fills in what an API request left out with the answering model's settings. Values the request sent stay.
        public static void FillRequest(SamplerSettings s, Dictionary<string, object> o, string model, ref int maxTokens)
        {
            ModelSampling m = For(model);
            if (!o.ContainsKey("temperature")) s.Temperature = (float)m.Temperature;
            if (!o.ContainsKey("top_k")) s.TopK = m.TopK;
            if (!o.ContainsKey("top_p")) s.TopP = (float)m.TopP;
            if (!o.ContainsKey("repeat_penalty") && !o.ContainsKey("frequency_penalty") && !o.ContainsKey("presence_penalty")) s.RepeatPenalty = (float)m.RepeatPenalty;
            if (!o.ContainsKey("max_tokens") && !o.ContainsKey("max_completion_tokens") && m.MaxTokens > 0) maxTokens = m.MaxTokens;
        }

        static void Load()
        {
            if (_byModel != null) return;
            _byModel = new Dictionary<string, ModelSampling>();
            try
            {
                if (!File.Exists(FilePath)) return;
                Dictionary<string, object> all = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8)) as Dictionary<string, object>;
                if (all == null) return;
                foreach (KeyValuePair<string, object> kv in all)
                {
                    Dictionary<string, object> o = kv.Value as Dictionary<string, object>;
                    if (o == null) continue;
                    ModelSampling s = new ModelSampling();
                    foreach (string k in Keys)
                        if (o.ContainsKey(k) && o[k] is double) { try { Apply(s, k, (double)o[k]); } catch { } }   // a bad value keeps the default
                    _byModel[KeyOf(kv.Key)] = s;
                }
            }
            catch { }
        }

        static void Save()
        {
            StringBuilder sb = new StringBuilder("{");
            bool first = true;
            foreach (KeyValuePair<string, ModelSampling> kv in _byModel)
            {
                if (!first) sb.Append(",\n "); first = false;
                sb.Append(Json.Quote(kv.Key)).Append(':').Append(kv.Value.ToJson());
            }
            sb.Append("}\n");
            try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)); File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false)); } catch { }
        }
    }
}
