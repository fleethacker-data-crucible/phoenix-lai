// /compact: the model condenses the conversation it has been sent into one summary. The chat page keeps the old
// messages on screen and from then on sends the model only the summary. A conversation larger than the context is
// summarised in parts, each part folding into the summary so far. C# 5 only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Phoenix
{
    public sealed partial class WebServer
    {
        const string CompactGuide =
            "You compact a conversation between a user and a coding assistant into a summary the assistant will continue " +
            "from; the original messages will no longer be available. Keep everything needed to carry on the work:\n" +
            "- the user's goals and requirements, and their preferences (languages, versions, style)\n" +
            "- decisions made and why, and approaches that were rejected\n" +
            "- file names, paths, function / class names, commands, settings, versions and error messages, exactly as written\n" +
            "- code that is still being worked on: keep its final version or key signatures verbatim in code blocks\n" +
            "- what is done, what is still open, and the last thing being worked on\n" +
            "Drop greetings, repetition, superseded code and anything resolved that no longer matters. Write only the summary, " +
            "as short headed sections with bullets, no preamble.";

        void Compact(Dictionary<string, object> o, Stream outS)
        {
            List<string> lines = new List<string>();
            List<object> history = o != null && o.ContainsKey("messages") ? o["messages"] as List<object> : null;
            if (history != null)
                foreach (object h in history)
                {
                    Dictionary<string, object> hm = h as Dictionary<string, object>;
                    string role = Json.GetStr(hm, "role", ""), text = Json.GetStr(hm, "content", "").Trim();
                    if ((role == "user" || role == "assistant") && text.Length > 0) lines.Add((role == "user" ? "USER: " : "ASSISTANT: ") + text);
                }

            bool gone = false;
            object writeLock = new object();
            TextCallback write = delegate(string line)
            {
                lock (writeLock)
                {
                    if (gone) return;
                    try { byte[] b = Encoding.UTF8.GetBytes("data: " + line + "\n\n"); outS.Write(b, 0, b.Length); outS.Flush(); }
                    catch { gone = true; }
                }
            };
            int rid = Interlocked.Increment(ref _requestId);
            Log.Text("#" + rid + " COMPACT " + lines.Count + " messages");
            if (_recallBusy != 0) _recallStop = true;
            if (_pretypeBusy != 0) _pretypeStop = true;
            try
            {
                if (lines.Count == 0) throw new InvalidOperationException("Nothing to compact yet.");
                lock (_engineLock)
                {
                    EnsureClientModel();
                    if (_engine == null) throw new InvalidOperationException("No model loaded.");
                    _engine.PrefillProgress = null;
                    // Parts sized to about a third of the context (~3.5 characters a token), leaving room for the summary
                    // so far and the new summary.
                    int partChars = Math.Max(4000, _engine.ContextLength);
                    List<string> parts = new List<string>();
                    StringBuilder cur = new StringBuilder();
                    foreach (string l in lines)
                    {
                        string m = l.Length > partChars ? l.Substring(0, partChars) + "\n[...]" : l;
                        if (cur.Length > 0 && cur.Length + m.Length > partChars) { parts.Add(cur.ToString()); cur.Length = 0; }
                        cur.Append(m).Append("\n\n");
                    }
                    if (cur.Length > 0) parts.Add(cur.ToString());

                    SamplerSettings s = new SamplerSettings();
                    s.Temperature = 0.3f; s.TopK = 64; s.TopP = 0.95f; s.RepeatPenalty = 1f;
                    Stopwatch sw = Stopwatch.StartNew();
                    string summary = "";
                    int tokens = 0;
                    for (int i = 0; i < parts.Count && !gone; i++)
                    {
                        string ask = summary.Length == 0
                            ? "Summarise this conversation:\n\n" + parts[i]
                            : "Summary of the conversation so far:\n\n" + summary + "\n\nIt continues:\n\n" + parts[i] +
                              "\n\nWrite the updated summary of the whole conversation.";
                        List<ChatMessage> msgs = new List<ChatMessage>();
                        msgs.Add(new ChatMessage("system", CompactGuide));
                        msgs.Add(new ChatMessage("user", ask));
                        write("{\"part\":" + (i + 1) + ",\"parts\":" + parts.Count + "}");
                        StringBuilder reply = new StringBuilder();
                        bool last = i == parts.Count - 1;
                        GenerationStats st = _engine.Generate(msgs, s, 2048, delegate(string piece)
                        {
                            reply.Append(piece);
                            if (last) write("{\"t\":" + Json.Quote(piece) + "}");
                        }, delegate { return gone; });
                        summary = reply.ToString().Trim();
                        tokens = st.GeneratedTokens;
                    }
                    Log.Text("#" + rid + " COMPACT done in " + ChatLog.Ms(sw.Elapsed.TotalSeconds) + " ms, " + parts.Count + " part(s), " + tokens + " tokens\n" + summary);
                    write("{\"done\":true,\"summary\":" + Json.Quote(summary) + ",\"tokens\":" + tokens + "}");
                }
            }
            catch (Exception ex) { write("{\"error\":" + Json.Quote(ex.Message) + "}"); }
            try { outS.Close(); } catch { }
        }
    }
}
