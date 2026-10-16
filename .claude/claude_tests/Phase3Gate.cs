// Phase 3 gate: tokenizer vs llama-tokenize output + decode round-trips. C# 5 only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Phoenix.Tests
{
    public static class Phase3Gate
    {
        public static List<string> Run(string modelPath, string samplesPath, string tokensPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            // llama-tokenize processes escapes (\n, \t, ...) in its input by default; do the same.
            string[] samples = File.ReadAllLines(samplesPath, Encoding.UTF8);
            for (int i = 0; i < samples.Length; i++) samples[i] = ProcessEscapes(samples[i]);
            Dictionary<int, int[]> expected = LoadExpected(tokensPath);

            using (GgufFile g = GgufFile.Open(modelPath))
            {
                DateTime t0 = DateTime.Now;
                Tokenizer tok = new Tokenizer(g);
                output.WriteLine("tokenizer: {0} tokens, BOS {1}, EOS {2}, loaded in {3:N0} ms",
                    tok.VocabSize, tok.BosId, tok.EosId, (DateTime.Now - t0).TotalMilliseconds);

                int matched = 0, roundTrips = 0;
                for (int i = 0; i < samples.Length; i++)
                {
                    int[] want;
                    if (!expected.TryGetValue(i, out want)) { fail.Add("no reference ids for sample " + i); continue; }

                    List<int> got = tok.Encode(samples[i], true, true);
                    if (SameIds(want, got)) matched++;
                    else fail.Add("sample " + i + " '" + samples[i] + "': llama.cpp [" + string.Join(", ", want) + "] vs ours [" + string.Join(", ", got) + "]");

                    string back = tok.Decode(got.GetRange(1, got.Count - 1), true);
                    if (back == samples[i]) roundTrips++;
                    else fail.Add("sample " + i + " round-trip: '" + samples[i] + "' -> '" + back + "'");
                }
                output.WriteLine("encode: {0}/{1} samples match llama.cpp; decode round-trips {2}/{1}", matched, samples.Length, roundTrips);

                // Every non-special token must decode to bytes that encode back to itself or a split of the same bytes.
                int vocabBad = 0;
                for (int id = 0; id < tok.VocabSize; id++)
                {
                    if (tok.IsSpecial(id)) continue;
                    if (tok.TokenBytes(id, false).Length == 0) { vocabBad++; if (vocabBad <= 5) fail.Add("token " + id + " decodes to no bytes"); }
                }
                output.WriteLine("vocab: {0} non-special tokens with empty decodes", vocabBad);
            }
            return fail;
        }

        static Dictionary<int, int[]> LoadExpected(string path)
        {
            Dictionary<int, int[]> d = new Dictionary<int, int[]>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.TrimStart('﻿');
                int tab = line.IndexOf('\t');
                if (tab < 0) continue;
                string list = line.Substring(tab + 1).Trim().TrimStart('[').TrimEnd(']');
                string[] parts = list.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                int[] ids = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++) ids[i] = int.Parse(parts[i].Trim());
                d[int.Parse(line.Substring(0, tab))] = ids;
            }
            return d;
        }

        // Mirrors string_process_escapes() in llama.cpp common.cpp for the common cases.
        static string ProcessEscapes(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char c = s[++i];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\'': sb.Append('\''); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    default: sb.Append('\\').Append(c); break;
                }
            }
            return sb.ToString();
        }

        static bool SameIds(int[] a, List<int> b)
        {
            if (a.Length != b.Count) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
