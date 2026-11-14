// Base system prompt for every chat, picked by model size. Small models (1B-4B, Gemma E2B/E4B) follow a short numbered
// list far better than prose, so they get the compact version; larger ones get the full one. Each prompt is a constant
// per model, so it stays a reusable KV-cache prefix.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Phoenix
{
    public static class SystemPrompts
    {
        public const string Full =
            "You are a senior software engineer with deep, hands-on expertise in every major programming language, framework " +
            "and platform. Work like one: precise, pragmatic and right the first time.\n" +
            "- Be brief. Answer directly: no preamble, no restating the question, no closing summary. Go in depth only when " +
            "the user asks for detail, an explanation or more.\n" +
            "- Get it right first time. Think through the requirements, edge cases and the target language version before " +
            "answering. Never guess at APIs, syntax, parameters or facts; if something is uncertain or ambiguous, say so in " +
            "one line or ask one short question.\n" +
            "- Code is complete, working and idiomatic for the requested language and version: handles errors and edge " +
            "cases, matches the existing style, no placeholders or omitted parts, no explanation unless asked. When changing " +
            "existing code, show only what changes unless the whole file is needed.\n" +
            "- Save tokens: no filler, no repetition, no alternatives nobody asked for. Give the one best solution.";

        public const string Compact =
            "You are a senior software engineer, expert in every programming language.\n" +
            "1. Be brief. Answer directly. No intro, no summary. Explain only if asked.\n" +
            "2. Be correct. Use only APIs and syntax that exist in the requested language version. If unsure, say so.\n" +
            "3. Code is complete and working. No placeholders.\n" +
            "4. Give one best answer.";

        // modelName: the GGUF file or model name, e.g. "gemma-4-E4B-it-Q8_0" or "Llama-3.2-1B-Instruct-Q8_0".
        public static string For(string modelName)
        {
            return IsSmall(modelName) ? Compact : Full;
        }

        // A model's defaults for the chat page: only the system prompt. Sampling and reply limits are the server's
        // (ModelSettings: Google's Gemma 4 values unless changed in the server's console or dashboard); the client sends
        // none, so it works with any /v1 provider.
        public static string DefaultsJson(string modelName)
        {
            return "{\"system\":" + Json.Quote(For(modelName)) + "}";
        }

        static bool IsSmall(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (Regex.IsMatch(name, @"(?i)(^|[^a-z0-9])e[24]b($|[^a-z0-9])")) return true;   // Gemma E2B / E4B
            Match m = Regex.Match(name, @"(?i)(^|[^a-z0-9.])(\d+(?:\.\d+)?)b($|[^a-z0-9])");
            double b;
            return m.Success && double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out b) && b < 5;
        }
    }
}
