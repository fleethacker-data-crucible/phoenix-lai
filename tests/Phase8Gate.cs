// Phase 8 gate: sampler, chat template, chat engine on the 8B model (speed target 30+ tokens/s), scripted UI. C# 5 only.
using System;
using System.Collections.Generic;
using System.IO;

namespace Phoenix.Tests
{
    public static class Phase8Gate
    {
        const double TargetTokensPerSecond = 30;

        public static List<string> Run(string modelsDir, TextWriter output)
        {
            List<string> fail = new List<string>();
            SamplerTests(fail, output);

            string big = Path.Combine(modelsDir, "Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf");
            string small = Path.Combine(modelsDir, "Llama-3.2-1B-Instruct-Q8_0.gguf");
            if (File.Exists(big)) EngineTests(big, fail, output); else fail.Add("8B model missing: " + big);
            if (File.Exists(small)) UiTest(small, fail, output); else fail.Add("1B model missing: " + small);
            return fail;
        }

        static void SamplerTests(List<string> fail, TextWriter output)
        {
            float[] logits = { 1f, 3f, 2f, -1f, 2.5f };

            SamplerSettings greedy = new SamplerSettings(); greedy.Temperature = 0; greedy.RepeatPenalty = 1;
            Check(new Sampler(greedy).Sample(logits, null) == 1, "temperature 0 picks the argmax", fail);

            SamplerSettings k1 = new SamplerSettings(); k1.Temperature = 1; k1.TopK = 1; k1.RepeatPenalty = 1; k1.Seed = 1;
            Sampler s1 = new Sampler(k1);
            bool allTop = true;
            for (int i = 0; i < 200; i++) if (s1.Sample(logits, null) != 1) allTop = false;
            Check(allTop, "top-k 1 always picks the argmax", fail);

            // Penalty: positive logit divided, negative multiplied (llama.cpp semantics). Token 1 (3.0) penalized by 2
            // becomes 1.5, so token 4 (2.5) wins under greedy.
            SamplerSettings pen = new SamplerSettings(); pen.Temperature = 0; pen.RepeatPenalty = 2f; pen.RepeatLastN = 64;
            Check(new Sampler(pen).Sample(logits, new int[] { 1 }) == 4, "repeat penalty demotes a repeated token", fail);

            // Top-p 0.5 over probabilities {0.6, 0.3, 0.1}: only the first token survives.
            float[] l3 = { (float)Math.Log(0.6), (float)Math.Log(0.3), (float)Math.Log(0.1) };
            SamplerSettings tp = new SamplerSettings(); tp.Temperature = 1; tp.TopK = 0; tp.TopP = 0.5f; tp.RepeatPenalty = 1; tp.Seed = 3;
            Sampler sp = new Sampler(tp);
            bool onlyFirst = true;
            for (int i = 0; i < 500; i++) if (sp.Sample(l3, null) != 0) onlyFirst = false;
            Check(onlyFirst, "top-p 0.5 keeps only the 0.6 token", fail);

            // Plain temperature-1 sampling follows the distribution.
            SamplerSettings t1 = new SamplerSettings(); t1.Temperature = 1; t1.TopK = 0; t1.TopP = 1; t1.RepeatPenalty = 1; t1.Seed = 4;
            Sampler st = new Sampler(t1);
            int[] counts = new int[3];
            for (int i = 0; i < 20000; i++) counts[st.Sample(l3, null)]++;
            double f0 = counts[0] / 20000.0, f2 = counts[2] / 20000.0;
            Check(Math.Abs(f0 - 0.6) < 0.02 && Math.Abs(f2 - 0.1) < 0.02, "temperature 1 matches the distribution (" + f0.ToString("F3") + ", " + f2.ToString("F3") + ")", fail);

            // Same seed, same choices.
            SamplerSettings sd = new SamplerSettings(); sd.Temperature = 1; sd.TopK = 0; sd.RepeatPenalty = 1; sd.Seed = 42;
            Sampler a = new Sampler(sd), b = new Sampler(sd);
            bool same = true;
            for (int i = 0; i < 100; i++) if (a.Sample(logits, null) != b.Sample(logits, null)) same = false;
            Check(same, "a fixed seed is reproducible", fail);

            output.WriteLine("sampler: {0}", fail.Count == 0 ? "all checks passed" : fail.Count + " failed");
        }

        static void EngineTests(string path, List<string> fail, TextWriter output)
        {
            using (ChatEngine eng = ChatEngine.Load(path, 4096, true, delegate(string m) { output.WriteLine("  " + m); }))
            {
                output.WriteLine("engine: {0} on {1}", eng.ModelName, eng.Backend);
                if (!eng.Backend.StartsWith("GPU")) fail.Add("8B model did not load on the GPU");

                // Template check.
                List<ChatMessage> msgs = new List<ChatMessage>();
                msgs.Add(new ChatMessage("system", "You are terse."));
                msgs.Add(new ChatMessage("user", "Hi"));
                string rendered = eng.Tokenizer.Decode(eng.Template(msgs), true);
                string want = "<|begin_of_text|><|start_header_id|>system<|end_header_id|>\n\nYou are terse.<|eot_id|>" +
                              "<|start_header_id|>user<|end_header_id|>\n\nHi<|eot_id|><|start_header_id|>assistant<|end_header_id|>\n\n";
                Check(rendered == want, "chat template renders the Llama 3 format", fail);

                SamplerSettings greedy = new SamplerSettings(); greedy.Temperature = 0;

                // Turn 1: short factual answer, must end its turn by itself.
                msgs = new List<ChatMessage>();
                msgs.Add(new ChatMessage("system", "You are a helpful assistant."));
                msgs.Add(new ChatMessage("user", "What is the capital of France? Answer in one word."));
                System.Text.StringBuilder r1 = new System.Text.StringBuilder();
                GenerationStats s1 = eng.Generate(msgs, greedy, 32, delegate(string t) { r1.Append(t); }, null);
                output.WriteLine("  turn 1: \"{0}\" ({1})", r1.ToString().Trim(), s1.StopReason);
                Check(r1.ToString().Contains("Paris"), "turn 1 answers Paris", fail);
                Check(s1.StopReason == "end of turn", "turn 1 ends with <|eot_id|>", fail);

                // Turn 2: longer reply, reuses the KV cache; measures decode speed.
                msgs.Add(new ChatMessage("assistant", r1.ToString()));
                msgs.Add(new ChatMessage("user", "Write a paragraph of about 150 words about the ocean."));
                System.Text.StringBuilder r2 = new System.Text.StringBuilder();
                GenerationStats s2 = eng.Generate(msgs, greedy, 200, delegate(string t) { r2.Append(t); }, null);
                output.WriteLine("  turn 2: {0} tokens, {1} reused from the cache, {2} new prompt tokens", s2.GeneratedTokens, s2.ReusedTokens, s2.PromptTokens);
                output.WriteLine("  speed: decode {0:N1} tokens/s, prompt {1:N1} tokens/s (target decode >= {2})",
                    s2.TokensPerSecond, s2.PromptTokensPerSecond, TargetTokensPerSecond);
                output.WriteLine("  text: {0}...", r2.ToString().Replace("\n", " ").Substring(0, Math.Min(140, r2.Length)));
                Check(s2.ReusedTokens > 20, "turn 2 reuses the cached conversation", fail);
                Check(s2.GeneratedTokens >= 100, "turn 2 produces a long reply", fail);
                Check(s2.TokensPerSecond >= TargetTokensPerSecond, "8B decode speed " + s2.TokensPerSecond.ToString("F1") + " tokens/s >= " + TargetTokensPerSecond, fail);
            }
        }

        static void UiTest(string path, List<string> fail, TextWriter output)
        {
            string reply = null;
            try { reply = App.RunScripted(path, "What is the capital of France? Answer in one word.", 24, 120000); }
            catch (Exception ex) { fail.Add("scripted UI run: " + ex.Message); return; }
            output.WriteLine("ui: scripted chat replied \"{0}\"", reply.Trim());
            Check(reply.Contains("Paris"), "the studio window streams a reply", fail);
        }

        static void Check(bool ok, string what, List<string> fail)
        {
            if (!ok) fail.Add(what);
        }
    }
}
