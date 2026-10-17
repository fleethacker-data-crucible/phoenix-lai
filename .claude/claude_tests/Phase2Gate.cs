// Phase 2 gate: GGUF reader vs llama.cpp load log + self-consistency. C# 5 only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoenix.Tests
{
    public static class Phase2Gate
    {
        public static List<string> Run(string modelPath, string logPath, TextWriter output)
        {
            List<string> fail = new List<string>();
            using (GgufFile g = GgufFile.Open(modelPath))
            {
                output.WriteLine("GGUF v{0}, {1} KV, {2} tensors, alignment {3}, data @ {4:N0}",
                    g.Version, g.Keys.Count, g.Tensors.Count, g.Alignment, g.DataOffset);

                CompareWithLog(g, File.ReadAllLines(logPath, Encoding.UTF8), fail, output);
                CheckLayout(g, fail, output);
                CheckGemmaShapes(g, fail, output);
            }
            return fail;
        }

        // --- 1. llama.cpp load log ---

        static void CompareWithLog(GgufFile g, string[] log, List<string> fail, TextWriter output)
        {
            Regex header = new Regex(@"loaded meta data with (\d+) key-value pairs and (\d+) tensors .*\(version GGUF V(\d+)");
            Regex kv = new Regex(@"- kv\s+(\d+):\s+(\S+)\s+(\S+)\s+= (.*)$");
            Regex typeCount = new Regex(@"- type\s+(\S+):\s+(\d+) tensors");

            int kvChecked = 0, typesChecked = 0;
            bool sawHeader = false;
            Dictionary<string, int> ourTypeCounts = new Dictionary<string, int>();
            foreach (GgufTensor t in g.Tensors)
            {
                string n = GgmlTypes.Name(t.Type);
                int c; ourTypeCounts.TryGetValue(n, out c); ourTypeCounts[n] = c + 1;
            }

            foreach (string line in log)
            {
                Match m = header.Match(line);
                if (m.Success)
                {
                    if (sawHeader) break; // llama.cpp loads the file twice; compare the first dump only
                    sawHeader = true;
                    Expect(fail, "KV count", m.Groups[1].Value, g.Keys.Count.ToString());
                    Expect(fail, "tensor count", m.Groups[2].Value, g.Tensors.Count.ToString());
                    Expect(fail, "version", m.Groups[3].Value, g.Version.ToString());
                    continue;
                }

                m = kv.Match(line);
                if (m.Success)
                {
                    int idx = int.Parse(m.Groups[1].Value);
                    string key = m.Groups[2].Value;
                    if (idx >= g.Keys.Count) { fail.Add("log has kv " + idx + " beyond our count"); continue; }
                    Expect(fail, "kv " + idx + " key", key, g.Keys[idx]);
                    GgufValue v = g.Metadata[g.Keys[idx]];
                    Expect(fail, "kv " + idx + " (" + key + ") type", m.Groups[3].Value, TypeString(v));
                    string want = m.Groups[4].Value.TrimEnd();
                    if (v.Type != GgufType.Array)
                    {
                        string got = ValueString(v);
                        if (want.EndsWith("...") ? !got.StartsWith(want.Substring(0, want.Length - 3)) : want != got)
                            fail.Add("kv " + idx + " (" + key + ") value: log '" + want + "' vs ours '" + got + "'");
                    }
                    kvChecked++;
                    continue;
                }

                m = typeCount.Match(line);
                if (m.Success)
                {
                    int ours; ourTypeCounts.TryGetValue(m.Groups[1].Value, out ours);
                    Expect(fail, "count of " + m.Groups[1].Value + " tensors", m.Groups[2].Value, ours.ToString());
                    typesChecked++;
                }
            }

            if (!sawHeader) fail.Add("load log has no metadata header line");
            if (kvChecked != g.Keys.Count) fail.Add("checked " + kvChecked + " KV lines, file has " + g.Keys.Count);
            if (typesChecked != ourTypeCounts.Count) fail.Add("log lists " + typesChecked + " tensor types, file has " + ourTypeCounts.Count);
            output.WriteLine("log: {0} KV pairs and {1} tensor-type counts compared", kvChecked, typesChecked);
        }

        static string TypeString(GgufValue v)
        {
            if (v.Type == GgufType.Array) return "arr[" + ShortType(v.ArrayType) + "," + v.ArrayLength + "]";
            return ShortType(v.Type);
        }

        // Names from gguf_type_name() in gguf.cpp.
        static string ShortType(GgufType t)
        {
            switch (t)
            {
                case GgufType.UInt8: return "u8";    case GgufType.Int8: return "i8";
                case GgufType.UInt16: return "u16";  case GgufType.Int16: return "i16";
                case GgufType.UInt32: return "u32";  case GgufType.Int32: return "i32";
                case GgufType.Float32: return "f32"; case GgufType.Bool: return "bool";
                case GgufType.String: return "str";  case GgufType.Array: return "arr";
                case GgufType.UInt64: return "u64";  case GgufType.Int64: return "i64";
                case GgufType.Float64: return "f64";
            }
            return "?";
        }

        static string ValueString(GgufValue v)
        {
            switch (v.Type)
            {
                case GgufType.String: return ((string)v.Value).Replace("\n", "\\n");
                case GgufType.Float32: return ((float)v.Value).ToString("F6", CultureInfo.InvariantCulture);
                case GgufType.Float64: return ((double)v.Value).ToString("F6", CultureInfo.InvariantCulture);
                case GgufType.Bool: return (bool)v.Value ? "true" : "false";
            }
            return Convert.ToString(v.Value, CultureInfo.InvariantCulture);
        }

        // --- 2. data section layout ---

        static void CheckLayout(GgufFile g, List<string> fail, TextWriter output)
        {
            List<GgufTensor> byOffset = new List<GgufTensor>(g.Tensors);
            byOffset.Sort(delegate(GgufTensor a, GgufTensor b) { return a.Offset.CompareTo(b.Offset); });

            long end = 0, total = 0;
            foreach (GgufTensor t in byOffset)
            {
                if (t.Offset < end) fail.Add("tensor " + t.Name + " overlaps the previous tensor");
                end = t.Offset + t.SizeBytes;
                total += t.SizeBytes;
            }
            long dataLen = g.FileLength - g.DataOffset;
            if (GgufFile.AlignUp(end, g.Alignment) < dataLen - g.Alignment || end > dataLen)
                fail.Add("data section is " + dataLen + " bytes but tensors end at " + end);
            output.WriteLine("layout: {0} tensors, {1:N0} bytes of weights, data section {2:N0} bytes", byOffset.Count, total, dataLen);
        }

        // --- 3. tensor shapes agree with the hyperparameters (Gemma 4) ---

        static void CheckGemmaShapes(GgufFile g, List<string> fail, TextWriter output)
        {
            GemmaConfig c = GemmaConfig.FromGguf(g);
            int checkedCount = 0;
            long nVocab = g.GetArray<string>("tokenizer.ggml.tokens").Length;
            Shape(g, fail, ref checkedCount, "token_embd.weight", c.NEmbd, nVocab);
            Shape(g, fail, ref checkedCount, "output_norm.weight", c.NEmbd);
            for (int i = 0; i < c.NLayer; i++)
            {
                string p = "blk." + i + ".";
                long q = (long)c.NHead * c.HeadDim[i], kv = (long)c.NHeadKv[i] * c.HeadDim[i];
                Shape(g, fail, ref checkedCount, p + "attn_norm.weight", c.NEmbd);
                Shape(g, fail, ref checkedCount, p + "attn_q.weight", c.NEmbd, q);
                Shape(g, fail, ref checkedCount, p + "attn_k.weight", c.NEmbd, kv);
                if (g.TensorsByName.ContainsKey(p + "attn_v.weight")) Shape(g, fail, ref checkedCount, p + "attn_v.weight", c.NEmbd, kv);
                Shape(g, fail, ref checkedCount, p + "attn_output.weight", q, c.NEmbd);
                Shape(g, fail, ref checkedCount, p + "ffn_gate.weight", c.NEmbd, c.NFf[i]);
                Shape(g, fail, ref checkedCount, p + "ffn_up.weight", c.NEmbd, c.NFf[i]);
                Shape(g, fail, ref checkedCount, p + "ffn_down.weight", c.NFf[i], c.NEmbd);
            }
            output.WriteLine("shapes: {0} tensors match hyperparameters (embd {1}, layers {2}, heads {3}, vocab {4})",
                checkedCount, c.NEmbd, c.NLayer, c.NHead, nVocab);
        }

        static void Shape(GgufFile g, List<string> fail, ref int count, string name, params long[] dims)
        {
            GgufTensor t;
            if (!g.TensorsByName.TryGetValue(name, out t)) { fail.Add("missing tensor " + name); return; }
            count++;
            if (t.Dims.Length != dims.Length) { fail.Add(name + " has " + t.Dims.Length + " dims, expected " + dims.Length); return; }
            for (int i = 0; i < dims.Length; i++)
                if (t.Dims[i] != dims[i]) { fail.Add(name + " is [" + string.Join(",", t.Dims) + "], expected [" + string.Join(",", dims) + "]"); return; }
        }

        static void Expect(List<string> fail, string what, string want, string got)
        {
            if (want != got) fail.Add(what + ": log '" + want + "' vs ours '" + got + "'");
        }
    }
}
