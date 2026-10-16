// BPE tokenizers. C# 5 only. Ported from llm_tokenizer_bpe in llama-vocab.cpp (MIT, llama.cpp).
//  - "gpt2" (Llama 3 "llama-bpe", Mistral "tekken"): byte-level BPE after a regex pre-tokenizer.
//  - "gemma4": SentencePiece-style BPE: spaces become U+2581, merges run over whole lines of raw UTF-8,
//    unknown characters fall back to <0xXX> byte tokens.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Phoenix
{
    public sealed class Tokenizer
    {
        // tokenizer.ggml.token_type values (llama_token_type).
        const int TypeNormal = 1, TypeUnknown = 2, TypeControl = 3, TypeUserDefined = 4, TypeUnused = 5, TypeByte = 6;

        // Llama 3 pre-tokenizer, adapted form used by llama.cpp.
        const string Llama3Pattern =
            @"(?:'[sS]|'[tT]|'[rR][eE]|'[vV][eE]|'[mM]|'[lL][lL]|'[dD])|[^\r\n\p{L}\p{N}]?\p{L}+|\p{N}{1,3}| ?[^\s\p{L}\p{N}]+[\r\n]*|\s*[\r\n]+|\s+(?!\S)|\s+";

        // Mistral "tekken" pre-tokenizer (original tokenizer.json form; .NET supports the Unicode classes directly).
        const string TekkenPattern =
            @"[^\r\n\p{L}\p{N}]?[\p{Lu}\p{Lt}\p{Lm}\p{Lo}\p{M}]*[\p{Ll}\p{Lm}\p{Lo}\p{M}]+|[^\r\n\p{L}\p{N}]?[\p{Lu}\p{Lt}\p{Lm}\p{Lo}\p{M}]+[\p{Ll}\p{Lm}\p{Lo}\p{M}]*|\p{N}| ?[^\s\p{L}\p{N}]+[\r\n/]*|\s*[\r\n]+|\s+(?!\S)|\s+";

        public readonly string[] Tokens;
        public readonly int[] TokenTypes;
        public readonly int BosId = -1, EosId = -1;
        public readonly bool AddBos = true;

        readonly Dictionary<string, int> _vocab;
        readonly Dictionary<string, int> _mergeRank;
        readonly List<KeyValuePair<string, int>> _specials;   // longest first
        readonly Regex _pre;
        readonly bool _spm;                    // gemma4 style (see top)
        readonly int[] _byteTokens;            // <0xXX> ids (gemma4), -1 if missing
        readonly Dictionary<string, int[]> _cache = new Dictionary<string, int[]>();

        static readonly char[] ByteToChar = new char[256];
        static readonly Dictionary<char, byte> CharToByte = new Dictionary<char, byte>();

        static Tokenizer()
        {
            // GPT-2 bytes_to_unicode(): printable bytes map to themselves, the rest to U+0100 upward.
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                bool printable = (b >= 0x21 && b <= 0x7E) || (b >= 0xA1 && b <= 0xAC) || (b >= 0xAE && b <= 0xFF);
                char c = printable ? (char)b : (char)(256 + n++);
                ByteToChar[b] = c;
                CharToByte[c] = (byte)b;
            }
        }

        public Tokenizer(GgufFile g)
        {
            string model = g.GetString("tokenizer.ggml.model");
            string pattern = null;
            if (model == "gemma4") _spm = true;
            else if (model != "gpt2") throw new NotSupportedException("Tokenizer model '" + model + "' is not supported yet.");
            else
            {
                string pre = g.Has("tokenizer.ggml.pre") ? g.GetString("tokenizer.ggml.pre") : "default";
                if (pre == "llama-bpe") pattern = Llama3Pattern;
                else if (pre == "tekken") pattern = TekkenPattern;
                else throw new NotSupportedException("Pre-tokenizer '" + pre + "' is not supported yet.");
            }

            Tokens = g.GetArray<string>("tokenizer.ggml.tokens");
            TokenTypes = g.Has("tokenizer.ggml.token_type") ? g.GetArray<int>("tokenizer.ggml.token_type") : null;
            if (g.Has("tokenizer.ggml.bos_token_id")) BosId = (int)g.GetInt("tokenizer.ggml.bos_token_id");
            if (g.Has("tokenizer.ggml.eos_token_id")) EosId = (int)g.GetInt("tokenizer.ggml.eos_token_id");
            if (g.Has("tokenizer.ggml.add_bos_token")) AddBos = (bool)g.Metadata["tokenizer.ggml.add_bos_token"].Value;
            if (_spm) AddBos = true;   // llama.cpp forces BOS for Gemma 4

            _vocab = new Dictionary<string, int>(Tokens.Length, StringComparer.Ordinal);
            for (int i = 0; i < Tokens.Length; i++)
                if (!_vocab.ContainsKey(Tokens[i])) _vocab.Add(Tokens[i], i);

            string[] merges = g.GetArray<string>("tokenizer.ggml.merges");
            _mergeRank = new Dictionary<string, int>(merges.Length, StringComparer.Ordinal);
            for (int i = 0; i < merges.Length; i++)
                if (!_mergeRank.ContainsKey(merges[i])) _mergeRank.Add(merges[i], i);

            _specials = new List<KeyValuePair<string, int>>();
            if (TokenTypes != null)
                for (int i = 0; i < Tokens.Length; i++)
                    if (TokenTypes[i] == TypeControl || TokenTypes[i] == TypeUserDefined)
                        _specials.Add(new KeyValuePair<string, int>(Tokens[i], i));
            _specials.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return b.Key.Length.CompareTo(a.Key.Length); });

            if (pattern != null) _pre = new Regex(pattern, RegexOptions.CultureInvariant);
            if (_spm)
            {
                _byteTokens = new int[256];
                for (int b = 0; b < 256; b++)
                {
                    int id;
                    _byteTokens[b] = _vocab.TryGetValue("<0x" + b.ToString("X2") + ">", out id) ? id : -1;
                }
            }
        }

        public int VocabSize { get { return Tokens.Length; } }

        public bool IsSpecial(int id)
        {
            return TokenTypes != null && (TokenTypes[id] == TypeControl || TokenTypes[id] == TypeUserDefined);
        }

        // parseSpecial: treat special-token text like "<|eot_id|>" as that token (llama.cpp default).
        public List<int> Encode(string text, bool addBos, bool parseSpecial)
        {
            List<int> ids = new List<int>();
            if (addBos && AddBos && BosId >= 0) ids.Add(BosId);

            int start = 0;
            while (start < text.Length)
            {
                int specialAt = text.Length, specialId = -1, specialLen = 0;
                if (parseSpecial) FindSpecial(text, start, out specialAt, out specialId, out specialLen);

                if (specialAt > start) EncodeRaw(text.Substring(start, specialAt - start), ids);
                if (specialId < 0) break;
                ids.Add(specialId);
                start = specialAt + specialLen;
            }
            return ids;
        }

        void FindSpecial(string text, int from, out int at, out int id, out int len)
        {
            at = text.Length; id = -1; len = 0;
            for (int i = 0; i < _specials.Count; i++)
            {
                int p = text.IndexOf(_specials[i].Key, from, StringComparison.Ordinal);
                if (p >= 0 && p < at) { at = p; id = _specials[i].Value; len = _specials[i].Key.Length; }
            }
        }

        void EncodeRaw(string text, List<int> ids)
        {
            if (_spm) { EncodeRawSpm(text, ids); return; }
            foreach (Match m in _pre.Matches(text))
            {
                int[] word;
                if (!_cache.TryGetValue(m.Value, out word))
                {
                    word = EncodeWord(m.Value);
                    if (_cache.Count < 100000) _cache[m.Value] = word;
                }
                ids.AddRange(word);
            }
        }

        // gemma4: spaces -> U+2581, then runs of newlines (a whole token when the vocabulary has it) and the text
        // between them (BPE over its characters, byte tokens for anything unknown).
        void EncodeRawSpm(string text, List<int> ids)
        {
            text = text.Replace(' ', '\u2581');
            int i = 0;
            while (i < text.Length)
            {
                int j = i;
                bool newlines = text[i] == '\n';
                while (j < text.Length && (text[j] == '\n') == newlines) j++;
                string run = text.Substring(i, j - i);
                i = j;
                int whole;
                if (newlines && _vocab.TryGetValue(run, out whole)) { ids.Add(whole); continue; }
                int[] cached;
                if (!_cache.TryGetValue(run, out cached))
                {
                    cached = BpeSpm(run);
                    if (_cache.Count < 100000 && run.Length < 256) _cache[run] = cached;
                }
                ids.AddRange(cached);
            }
        }

        // BPE with a priority queue over a linked list of symbols (fast on long lines): always merge the lowest-rank
        // adjacent pair, leftmost first, as llm_tokenizer_bpe_session does.
        int[] BpeSpm(string word)
        {
            List<string> sym = new List<string>(word.Length);
            for (int k = 0; k < word.Length; k++)
            {
                if (char.IsHighSurrogate(word[k]) && k + 1 < word.Length) { sym.Add(word.Substring(k, 2)); k++; }
                else sym.Add(word[k].ToString());
            }
            int n = sym.Count;
            int[] prev = new int[n], next = new int[n];
            for (int k = 0; k < n; k++) { prev[k] = k - 1; next[k] = k + 1 < n ? k + 1 : -1; }
            SortedSet<long> queue = new SortedSet<long>();   // (rank << 32) | left index: lowest rank, then leftmost
            for (int k = 0; k + 1 < n; k++) Enqueue(queue, sym, k, k + 1);
            while (queue.Count > 0)
            {
                long top = queue.Min;
                queue.Remove(top);
                int left = (int)(top & 0xffffffff), rank = (int)(top >> 32), right = next[left];
                if (sym[left] == null || right < 0) continue;
                int r;
                if (!_mergeRank.TryGetValue(sym[left] + " " + sym[right], out r) || r != rank) continue;   // stale entry
                sym[left] = sym[left] + sym[right];
                sym[right] = null;
                next[left] = next[right];
                if (next[right] >= 0) prev[next[right]] = left;
                if (prev[left] >= 0) Enqueue(queue, sym, prev[left], left);
                if (next[left] >= 0) Enqueue(queue, sym, left, next[left]);
            }
            List<int> outIds = new List<int>();
            for (int k = 0; k >= 0 && k < n; k = next[k])
            {
                int id;
                if (_vocab.TryGetValue(sym[k], out id)) { outIds.Add(id); continue; }
                foreach (byte b in Encoding.UTF8.GetBytes(sym[k]))
                {
                    if (_byteTokens[b] < 0) throw new InvalidDataException("No token for byte 0x" + b.ToString("X2") + ".");
                    outIds.Add(_byteTokens[b]);
                }
            }
            return outIds.ToArray();
        }

        void Enqueue(SortedSet<long> queue, List<string> sym, int left, int right)
        {
            int r;
            if (_mergeRank.TryGetValue(sym[left] + " " + sym[right], out r)) queue.Add(((long)r << 32) | (uint)left);
        }

        int[] EncodeWord(string word)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(word);
            StringBuilder sb = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++) sb.Append(ByteToChar[bytes[i]]);
            string mapped = sb.ToString();

            // ignore_merges (Llama 3): a word that is already a token is emitted whole.
            int whole;
            if (_vocab.TryGetValue(mapped, out whole)) return new int[] { whole };

            List<string> sym = new List<string>(mapped.Length);
            for (int i = 0; i < mapped.Length; i++) sym.Add(mapped[i].ToString());

            // Repeatedly merge the adjacent pair with the lowest rank (leftmost on ties).
            while (sym.Count > 1)
            {
                int best = -1, bestRank = int.MaxValue;
                for (int i = 0; i + 1 < sym.Count; i++)
                {
                    int r;
                    if (_mergeRank.TryGetValue(sym[i] + " " + sym[i + 1], out r) && r < bestRank) { bestRank = r; best = i; }
                }
                if (best < 0) break;
                sym[best] = sym[best] + sym[best + 1];
                sym.RemoveAt(best + 1);
            }

            List<int> outIds = new List<int>(sym.Count);
            foreach (string s in sym)
            {
                int id;
                if (_vocab.TryGetValue(s, out id)) { outIds.Add(id); continue; }
                // Fallback: one token per byte.
                foreach (char c in s)
                {
                    if (_vocab.TryGetValue(c.ToString(), out id)) outIds.Add(id);
                    else throw new InvalidDataException("No token for byte 0x" + ((int)CharToByte[c]).ToString("X2") + ".");
                }
            }
            return outIds.ToArray();
        }

        // Token ids -> text. Special tokens are rendered as their text when renderSpecial is true.
        public string Decode(IList<int> ids, bool renderSpecial)
        {
            List<byte> bytes = new List<byte>();
            foreach (int id in ids) AppendTokenBytes(id, renderSpecial, bytes);
            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        // Raw bytes for one token (for streaming output, where a token may end mid-character).
        public byte[] TokenBytes(int id, bool renderSpecial)
        {
            List<byte> bytes = new List<byte>();
            AppendTokenBytes(id, renderSpecial, bytes);
            return bytes.ToArray();
        }

        void AppendTokenBytes(int id, bool renderSpecial, List<byte> bytes)
        {
            if (id < 0 || id >= Tokens.Length) throw new ArgumentOutOfRangeException("id");
            string t = Tokens[id];
            if (IsSpecial(id))
            {
                if (renderSpecial) bytes.AddRange(Encoding.UTF8.GetBytes(t));
                return;
            }
            if (_spm)
            {
                if (TokenTypes != null && TokenTypes[id] == TypeByte && t.Length == 6) { bytes.Add(Convert.ToByte(t.Substring(3, 2), 16)); return; }
                bytes.AddRange(Encoding.UTF8.GetBytes(t.Replace('\u2581', ' ')));
                return;
            }
            foreach (char c in t)
            {
                byte b;
                if (CharToByte.TryGetValue(c, out b)) bytes.Add(b);
                else bytes.AddRange(Encoding.UTF8.GetBytes(c.ToString()));
            }
        }
    }
}
