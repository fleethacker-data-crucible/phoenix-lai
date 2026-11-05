"""OpenAI /v1 compatibility suite for the Phoenix server, driven by the official OpenAI Python SDK (dev machine only).
   python .claude/claude_tests/openai-compat.py [base_url] [model]
Default: http://localhost:1893/v1, gemma-4-e2b_q4_0-it. Prints one PASS/FAIL line per check and a summary; exit 1 on failure.
"""
import base64, json, struct, sys, time, threading
from openai import OpenAI, NotFoundError, BadRequestError

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:1893/v1"
MODEL = sys.argv[2] if len(sys.argv) > 2 else "gemma-4-e2b_q4_0-it"
c = OpenAI(base_url=BASE, api_key="sk-any-key-works", timeout=600, max_retries=0)
results = []

def check(name, fn):
    t0 = time.time()
    try:
        detail = fn() or ""
        results.append((name, True, detail, time.time() - t0))
        print(f"PASS  {name:<44} {time.time()-t0:6.2f}s  {detail}")
    except Exception as e:
        results.append((name, False, repr(e), time.time() - t0))
        print(f"FAIL  {name:<44} {time.time()-t0:6.2f}s  {type(e).__name__}: {e}")

def msg(t): return [{"role": "user", "content": t}]

def t_models():
    ids = [m.id for m in c.models.list().data]
    assert MODEL in ids, ids
    assert not any("mmproj" in i for i in ids), "projectors must not be listed"
    assert not any("embed" in i.lower() for i in ids), "the embedding model must not be listed"
    return f"{len(ids)} models"
def t_retrieve():
    m = c.models.retrieve(MODEL); assert m.id == MODEL and m.object == "model"; return m.id
def t_retrieve_404():
    try: c.models.retrieve("no-such-model"); raise AssertionError("expected 404")
    except NotFoundError as e: assert e.body and "message" in e.body; return "404 model_not_found"
def t_chat():
    r = c.chat.completions.create(model=MODEL, messages=msg("Reply with exactly the word: pineapple"), temperature=0, max_tokens=20)
    ch = r.choices[0]
    assert r.object == "chat.completion" and r.id and r.created and ch.message.role == "assistant"
    assert "pineapple" in ch.message.content.lower(), ch.message.content
    assert ch.finish_reason == "stop" and r.usage.prompt_tokens > 0 and r.usage.completion_tokens > 0
    return repr(ch.message.content.strip())
def t_system_dev():
    r = c.chat.completions.create(model=MODEL, messages=[{"role": "developer", "content": "Always answer in uppercase letters only."},
        {"role": "system", "content": "Answer with one word."}, {"role": "user", "content": "What colour is the sky on a clear day?"}], temperature=0, max_tokens=10)
    t = r.choices[0].message.content.strip(); assert t, "empty"; return repr(t)
def t_multiturn():
    r = c.chat.completions.create(model=MODEL, temperature=0, max_tokens=20, messages=[
        {"role": "user", "content": "My name is Zorblat. Remember it."}, {"role": "assistant", "content": "Got it, Zorblat."},
        {"role": "user", "content": "What is my name? One word."}])
    t = r.choices[0].message.content; assert "zorblat" in t.lower(), t; return repr(t.strip())
def t_length():
    r = c.chat.completions.create(model=MODEL, messages=msg("Write a long story about a dragon."), max_tokens=5)
    assert r.choices[0].finish_reason == "length" and r.usage.completion_tokens <= 5, (r.choices[0].finish_reason, r.usage)
    return f"{r.usage.completion_tokens} tokens, finish length"
def t_max_completion_tokens():
    r = c.chat.completions.create(model=MODEL, messages=msg("Count to fifty."), max_completion_tokens=6)
    assert r.choices[0].finish_reason == "length" and r.usage.completion_tokens <= 6; return "honoured"
def t_stop():
    r = c.chat.completions.create(model=MODEL, messages=msg("Count from 1 to 10 separated by commas, digits only."), temperature=0, max_tokens=60, stop=["5"])
    t = r.choices[0].message.content; assert "5" not in t and "4" in t and r.choices[0].finish_reason == "stop", t; return repr(t)
def t_stream():
    chunks, text, roles, usage, finishes = 0, "", 0, None, []
    s = c.chat.completions.create(model=MODEL, messages=msg("Count from 1 to 8, separated by spaces."), temperature=0, max_tokens=40, stream=True, stream_options={"include_usage": True})
    for ch in s:
        chunks += 1
        assert ch.object == "chat.completion.chunk"
        if ch.usage: usage = ch.usage
        for x in ch.choices:
            if x.delta.role: roles += 1
            if x.delta.content: text += x.delta.content
            if x.finish_reason: finishes.append(x.finish_reason)
    assert roles == 1 and finishes == ["stop"] and usage and usage.completion_tokens > 0 and "8" in text, (roles, finishes, usage, text)
    return f"{chunks} chunks, usage {usage.completion_tokens} tokens"
def t_stream_no_usage():
    last = None
    for ch in c.chat.completions.create(model=MODEL, messages=msg("Say hi."), max_tokens=10, stream=True): last = ch
    assert last.usage is None and last.choices[0].finish_reason in ("stop", "length"); return "no usage chunk unless asked"
def t_n2():
    r = c.chat.completions.create(model=MODEL, messages=msg("Name a random fruit, one word."), n=2, temperature=1.0, max_tokens=8)
    assert [x.index for x in r.choices] == [0, 1] and all(x.message.content for x in r.choices); return " / ".join(x.message.content.strip() for x in r.choices)
def t_n2_stream():
    idx = set()
    for ch in c.chat.completions.create(model=MODEL, messages=msg("Say yes."), n=2, max_tokens=5, stream=True):
        for x in ch.choices: idx.add(x.index)
    assert idx == {0, 1}, idx; return "choices 0 and 1 streamed"
def t_seed():
    a = c.chat.completions.create(model=MODEL, messages=msg("Invent a name for a cat."), temperature=1.0, seed=1234, max_tokens=12).choices[0].message.content
    b = c.chat.completions.create(model=MODEL, messages=msg("Invent a name for a cat."), temperature=1.0, seed=1234, max_tokens=12).choices[0].message.content
    assert a == b, (a, b); return "same seed, same reply"
TOOLS = [{"type": "function", "function": {"name": "get_weather", "description": "Get the current weather for a city.",
          "parameters": {"type": "object", "properties": {"city": {"type": "string"}}, "required": ["city"]}}}]
def t_tools():
    r = c.chat.completions.create(model=MODEL, messages=msg("What is the weather in Paris right now?"), tools=TOOLS, tool_choice="required", temperature=0, max_tokens=120)
    ch = r.choices[0]; tc = ch.message.tool_calls
    assert ch.finish_reason == "tool_calls" and tc and tc[0].type == "function" and tc[0].function.name == "get_weather", ch
    args = json.loads(tc[0].function.arguments); assert "paris" in args["city"].lower(), args
    # round trip: the tool result goes back, the model answers in text
    r2 = c.chat.completions.create(model=MODEL, temperature=0, max_tokens=80, tools=TOOLS, messages=msg("What is the weather in Paris right now?") +
        [{"role": "assistant", "content": None, "tool_calls": [t.model_dump() for t in tc]},
         {"role": "tool", "tool_call_id": tc[0].id, "content": json.dumps({"temp_c": 21, "sky": "sunny"})}])
    t = r2.choices[0].message.content or ""; assert "21" in t or "sunny" in t.lower(), r2.choices[0]
    return f"call {tc[0].function.arguments} -> {t.strip()[:50]!r}"
def t_tools_forced():
    r = c.chat.completions.create(model=MODEL, messages=msg("Hello!"), tools=TOOLS, tool_choice={"type": "function", "function": {"name": "get_weather"}}, temperature=0, max_tokens=80)
    tc = r.choices[0].message.tool_calls; assert tc and tc[0].function.name == "get_weather"; return tc[0].function.arguments
def t_tools_none():
    r = c.chat.completions.create(model=MODEL, messages=msg("What is the weather in Rome?"), tools=TOOLS, tool_choice="none", temperature=0, max_tokens=40)
    assert not r.choices[0].message.tool_calls and r.choices[0].finish_reason != "tool_calls"; return "no call"
def t_tools_stream():
    calls = {}
    finish = None
    for ch in c.chat.completions.create(model=MODEL, messages=msg("Weather in Berlin?"), tools=TOOLS, tool_choice="required", temperature=0, max_tokens=120, stream=True):
        for x in ch.choices:
            for t in (x.delta.tool_calls or []):
                d = calls.setdefault(t.index, {"name": "", "args": ""})
                if t.function and t.function.name: d["name"] += t.function.name
                if t.function and t.function.arguments: d["args"] += t.function.arguments
            if x.finish_reason: finish = x.finish_reason
    assert finish == "tool_calls" and calls[0]["name"] == "get_weather" and "berlin" in json.loads(calls[0]["args"])["city"].lower(), (finish, calls)
    return calls[0]["args"]
def t_json_object():
    r = c.chat.completions.create(model=MODEL, messages=msg("Give a JSON object with keys name and age for a fictional person."), response_format={"type": "json_object"}, temperature=0, max_tokens=80)
    o = json.loads(r.choices[0].message.content); assert isinstance(o, dict) and "name" in o, o; return json.dumps(o)
def t_json_schema():
    schema = {"type": "object", "properties": {"city": {"type": "string"}, "country": {"type": "string"}}, "required": ["city", "country"]}
    r = c.chat.completions.create(model=MODEL, messages=msg("The capital of Japan, as JSON."), response_format={"type": "json_schema", "json_schema": {"name": "place", "schema": schema}}, temperature=0, max_tokens=60)
    o = json.loads(r.choices[0].message.content); assert "tokyo" in o["city"].lower(), o; return json.dumps(o)
def t_image():
    # 8x8 solid red PNG
    import zlib
    raw = b"".join(b"\x00" + b"\xff\x00\x00" * 8 for _ in range(8))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")
    url = "data:image/png;base64," + base64.b64encode(png).decode()
    r = c.chat.completions.create(model=MODEL, temperature=0, max_tokens=20, messages=[{"role": "user", "content": [
        {"type": "text", "text": "What single colour fills this image? One word."}, {"type": "image_url", "image_url": {"url": url}}]}])
    t = r.choices[0].message.content; assert "red" in t.lower(), t; return repr(t.strip())
def t_completions():
    r = c.completions.create(model=MODEL, prompt="The capital of France is", max_tokens=10, temperature=0)
    assert r.object == "text_completion" and r.choices[0].text and "paris" in r.choices[0].text.lower(), r.choices[0].text; return repr(r.choices[0].text.strip())
def t_completions_stream():
    t = "".join(ch.choices[0].text for ch in c.completions.create(model=MODEL, prompt="List three colours:", max_tokens=20, stream=True) if ch.choices)
    assert t.strip(); return repr(t.strip()[:40])
def t_fim():
    r = c.completions.create(model=MODEL, prompt="def add(a, b):\n    return ", suffix="\n\nprint(add(1, 2))\n", max_tokens=10, temperature=0)
    t = r.choices[0].text; assert "a" in t and "b" in t, t; return repr(t.strip())
def t_embed():
    r = c.embeddings.create(model=MODEL, input=["hello world", "goodbye moon"])   # the SDK asks for base64 and decodes it
    assert len(r.data) == 2 and len(r.data[0].embedding) > 100 and r.usage.total_tokens > 0
    assert "embed" not in r.model.lower(), "the reply names the internal embedding model: " + r.model
    n = sum(x * x for x in r.data[0].embedding) ** 0.5; assert abs(n - 1) < 0.05, n
    return f"{len(r.data[0].embedding)} dims (base64)"
def t_embed_float():
    r = c.embeddings.create(model=MODEL, input="hello", encoding_format="float")
    assert isinstance(r.data[0].embedding[0], float); return f"{len(r.data[0].embedding)} dims (float)"
def t_embed_dims():
    r = c.embeddings.create(model=MODEL, input="hello", dimensions=256)
    assert len(r.data[0].embedding) == 256; n = sum(x * x for x in r.data[0].embedding) ** 0.5; assert abs(n - 1) < 1e-3, n; return "256 dims, unit length"
def t_err_messages():
    try: c.chat.completions.create(model=MODEL, messages=[]); raise AssertionError("expected 400")
    except BadRequestError as e: assert e.body.get("message"); return "400: " + e.body["message"][:60]
def t_err_temp():
    try: c.chat.completions.create(model=MODEL, messages=msg("hi"), temperature=5); raise AssertionError("expected 400")
    except BadRequestError as e: return "400: " + e.body["message"][:60]
def t_err_json():
    import urllib.request
    req = urllib.request.Request(BASE + "/chat/completions", data=b"{not json", headers={"Content-Type": "application/json"})
    try: urllib.request.urlopen(req); raise AssertionError("expected 400")
    except urllib.error.HTTPError as e: b = json.loads(e.read()); assert e.code == 400 and b["error"]["type"] == "invalid_request_error"; return "400 invalid_request_error"
def t_unknown_url():
    import urllib.request
    try: urllib.request.urlopen(BASE + "/nothing-here"); raise AssertionError("expected 404")
    except urllib.error.HTTPError as e: assert e.code == 404 and "error" in json.loads(e.read()); return "404"
def t_parallel():
    out, errs = [], []
    def one(i):
        try: out.append(OpenAI(base_url=BASE, api_key=f"client-{i}", max_retries=0, timeout=600).chat.completions.create(model=MODEL, messages=msg(f"Say the number {i}."), temperature=0, max_tokens=8).choices[0].message.content)
        except Exception as e: errs.append(e)
    ts = [threading.Thread(target=one, args=(i,)) for i in range(4)]
    [t.start() for t in ts]; [t.join() for t in ts]
    assert not errs and len(out) == 4, errs; return "4 clients at once, all answered (queued)"

def t_queued_stream():
    # A stream that has to wait behind another request answers at once (headers + keep-alive), then streams normally.
    import http.client
    def open_stream(prompt, tokens):
        h = http.client.HTTPConnection("127.0.0.1", 1893, timeout=600)
        h.request("POST", "/v1/chat/completions", json.dumps({"model": MODEL, "stream": True, "max_tokens": tokens, "messages": msg(prompt)}), {"Content-Type": "application/json"})
        return h
    a = open_stream("Write 400 words about mountains.", 500)
    time.sleep(0.3)
    t0 = time.time(); b = open_stream("Say ok.", 10)
    rb = b.getresponse(); first = rb.readline(); ttfb = time.time() - t0
    assert rb.status == 200 and ttfb < 1.5, (rb.status, ttfb, first)
    body = first + rb.read(); a.getresponse().read()
    assert b"[DONE]" in body and b'"content"' in body, body[-300:]
    return f"queued stream answered in {ttfb*1000:.0f} ms, then completed"
def t_lmstudio_native():
    import urllib.request
    j = json.loads(urllib.request.urlopen(BASE.replace("/v1", "") + "/api/v1/models").read())
    keys = [m["key"] for m in j["models"]]
    assert MODEL in keys and not any("embed" in k or "mmproj" in k for k in keys), keys
    assert all(m["type"] == "llm" and "loaded_instances" in m for m in j["models"]); return f"{len(keys)} models, LM Studio shape"

for n, f in [("queued stream answers at once", t_queued_stream), ("LM Studio GET /api/v1/models", t_lmstudio_native), ("GET /models", t_models), ("GET /models/{id}", t_retrieve), ("GET /models/{unknown} -> 404", t_retrieve_404),
             ("chat", t_chat), ("chat developer + system roles", t_system_dev), ("chat multi-turn", t_multiturn),
             ("chat max_tokens -> length", t_length), ("chat max_completion_tokens", t_max_completion_tokens), ("chat stop", t_stop),
             ("chat stream + include_usage", t_stream), ("chat stream without usage", t_stream_no_usage), ("chat n=2", t_n2), ("chat n=2 stream", t_n2_stream),
             ("chat seed reproducible", t_seed), ("tools: call + result round trip", t_tools), ("tools: forced function", t_tools_forced),
             ("tools: tool_choice none", t_tools_none), ("tools: streamed tool_calls", t_tools_stream), ("response_format json_object", t_json_object),
             ("response_format json_schema", t_json_schema), ("vision: image_url data URI", t_image), ("completions", t_completions),
             ("completions stream", t_completions_stream), ("completions suffix (FIM)", t_fim), ("embeddings (SDK default base64)", t_embed),
             ("embeddings float", t_embed_float), ("embeddings dimensions", t_embed_dims), ("error: empty messages", t_err_messages),
             ("error: temperature out of range", t_err_temp), ("error: bad JSON body", t_err_json), ("error: unknown URL", t_unknown_url),
             ("4 parallel clients", t_parallel)]:
    check(n, f)
bad = [r for r in results if not r[1]]
print(f"\n{len(results) - len(bad)}/{len(results)} passed")
sys.exit(1 if bad else 0)
