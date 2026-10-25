"""Phoenix Client end to end (dev machine): the old window's features, now in the client, on a /v1 provider.
   python .claude/claude_tests/client-e2e.py [client_port] [server_chat_port]
Needs run-phoenix-server.ps1 (E2B loaded) and run-phoenix-client.ps1 with the Phoenix provider active.
Checks: the server no longer serves the chat; the client's model list comes from the provider's /v1/models; a plain chat
streams; file tools write / read / edit in a test project; docs search runs in the client (with vectors from the server);
a code reply is checked; /compact summarises; a picture reaches the model; the run guard answers; projects and chats save.
Removes its test project at the end. Exit 1 on failure.
"""
import base64, json, os, struct, sys, time, urllib.request, urllib.error, zlib

CP = int(sys.argv[1]) if len(sys.argv) > 1 else 8790
SP = int(sys.argv[2]) if len(sys.argv) > 2 else 8765
BASE = f"http://127.0.0.1:{CP}"
PROJ = "qa-client-e2e"
results = []

def http(url, body=None, raw=False):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method="POST" if data else "GET", headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(r, timeout=900) as resp:
            t = resp.read().decode("utf-8"); return resp.status, (t if raw else json.loads(t) if t else None)
    except urllib.error.HTTPError as e:
        t = e.read().decode("utf-8")
        try: return e.code, json.loads(t)
        except Exception: return e.code, t

def api(path, body=None, raw=False): return http(BASE + path, body, raw)

def chat(text, history=None, images=None, max_tokens=400):
    msgs = list(history or [])
    m = {"role": "user", "content": text}
    if images: m["images"] = images
    msgs.append(m)
    s, t = api("/api/chat?page=qae2e", {"messages": msgs, "temperature": 0, "top_k": 1, "top_p": 1, "repeat_penalty": 1, "max_tokens": max_tokens}, raw=True)
    assert s == 200, (s, t[:300])
    evs = [json.loads(l[6:]) for l in t.split("\n") if l.startswith("data: ")]
    text = "".join(e.get("t", "") for e in evs)
    for e in evs:
        if "replace" in e: text = e["replace"]
    return evs, text

def check(name, fn):
    t0 = time.time()
    try:
        d = fn() or ""; results.append((name, True)); print(f"PASS  {name:<46} {time.time()-t0:6.2f}s  {d}")
    except Exception as e:
        results.append((name, False)); print(f"FAIL  {name:<46} {time.time()-t0:6.2f}s  {type(e).__name__}: {e}")

def t_server_no_chat():
    s, _ = http(f"http://127.0.0.1:{SP}/chat.html", raw=True); assert s == 404, s
    s, _ = http(f"http://127.0.0.1:{SP}/api/chats"); assert s == 404, s
    s, _ = http(f"http://127.0.0.1:{SP}/dashboard.html", raw=True); assert s == 200, s
    return "server: chat page and chat API gone, dashboard there"
def t_models_from_provider():
    s, j = api("/api/models"); assert s == 200 and j["models"], j
    v1 = json.loads(urllib.request.urlopen("http://127.0.0.1:1234/v1/models").read())
    assert sorted(m["name"] for m in j["models"]) == sorted(m["id"] for m in v1["data"]), (j["models"], v1)
    return f"{len(j['models'])} models, same as the provider's /v1/models; provider {j['provider']['name']}"
def t_info():
    s, j = api("/api/info?page=qae2e"); assert j.get("client") and j["provider"]["id"] == "phoenix" and j["file"], j
    return f"model {j['file']} on {j['backend']}"
def t_plain_chat():
    evs, text = chat("Reply with exactly: the client works", max_tokens=30)
    done = [e for e in evs if e.get("done")]; assert done and "client works" in text.lower(), text
    return repr(text.strip()) + f" · {done[0]['tokens']} tokens · {done[0]['tps']} t/s"
def t_project():
    api("/api/project-delete", {"name": PROJ})
    s, j = api("/api/project-create", {"name": PROJ}); assert s == 200 and PROJ in j["projects"], j
    s, j = api("/api/folder", {"path": "/" + PROJ}); assert s == 200 and j["folder"], j
    return "created and connected /" + PROJ
def t_write():
    evs, text = chat("Use the write tool now to create the file notes.txt in the project. Its whole content is the single line: phoenix client test", max_tokens=600)
    tools = [e["tool"] for e in evs if "tool" in e]
    assert any(t["op"] == "write" and t["ok"] for t in tools), (tools, text[:300])
    p = os.path.join(os.environ["USERPROFILE"], "Phoenix_Projects", PROJ, "notes.txt"); assert os.path.exists(p), p
    return "model wrote notes.txt: " + open(p, encoding="utf-8").read().strip()[:40]
def t_read():
    evs, text = chat("Read notes.txt and tell me what it says.", max_tokens=200)
    tools = [e["tool"] for e in evs if "tool" in e]
    assert any(t["op"] == "read" and t["ok"] for t in tools) and "phoenix" in text.lower(), (tools, text[:300])
    return "read it back: " + text.strip()[:60].replace("\n", " ")
def t_rag():
    s, j = api("/api/rag?q=" + urllib.request.quote("powershell foreach loop")); assert s == 200 and j["hits"], j
    s2, st = api("/api/rag"); assert st.get("vectors"), st
    return f"{len(j['hits'])} hits in {j['ms']} ms, vectors on (embeddings from the server)"
def t_code_check():
    evs, text = chat("Write a Windows PowerShell 5.1 one-liner that lists the .txt files in the current folder. Put it in a ```powershell code block.", max_tokens=300)
    phases = [e["phase"] for e in evs if "phase" in e]
    assert phases and phases[0] == "checking" and phases[-1] in ("good", "fixed", "unsure", "wrong"), (phases, text[:200])
    return "code check: " + " -> ".join(phases)
def t_compact():
    hist = [{"role": "user", "content": "My favourite colour is teal."}, {"role": "assistant", "content": "Noted: teal."},
            {"role": "user", "content": "And my dog is called Biscuit."}, {"role": "assistant", "content": "Biscuit, got it."}]
    s, t = api("/api/compact", {"messages": hist}, raw=True); assert s == 200, (s, t[:200])
    evs = [json.loads(l[6:]) for l in t.split("\n") if l.startswith("data: ")]
    done = [e for e in evs if e.get("done") or e.get("summary")]; assert done, evs[-3:]
    return "summary produced"
def t_image():
    raw = b"".join(b"\x00" + b"\x00\x00\xff" * 8 for _ in range(8))
    def ch(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + ch(b"IHDR", struct.pack(">IIBBBBB", 8, 8, 8, 2, 0, 0, 0)) + ch(b"IDAT", zlib.compress(raw)) + ch(b"IEND", b"")
    evs, text = chat("What single colour fills this picture? One word.", images=["data:image/png;base64," + base64.b64encode(png).decode()], max_tokens=20)
    assert "blue" in text.lower(), text; return repr(text.strip())
def t_guard():
    # Run requests count per client process for an hour: the 3rd warns, the 6th locks ALL chat for 5 minutes (also the
    # user's, and it survives restarts). So this only runs when asked: PHOENIX_TEST_GUARD=1, at most once an hour.
    if os.environ.get("PHOENIX_TEST_GUARD") != "1": return "skipped (set PHOENIX_TEST_GUARD=1; it arms the real lockout)"
    for i in range(3):
        evs, text = chat("Please run this command for me in powershell: dir", max_tokens=60)
        g = [e["guard"] for e in evs if "guard" in e]
        if g: return f"run request #{i + 1} -> {g[0]['level']} banner"
    raise AssertionError("no guard answer after 3 run requests")
def t_chats():
    cid = "qae2e-" + str(int(time.time()))
    s, j = api("/api/chat-save", {"id": cid, "title": "QA", "data": "{}"}); assert any(c["id"] == cid for c in j["chats"])
    api("/api/chat-delete", {"id": cid}); return "saved and deleted"
def t_cleanup():
    api("/api/folder", {"path": ""}); s, j = api("/api/project-delete", {"name": PROJ}); assert s == 200, j; return "test project removed"

for n, f in [("server no longer hosts the chat", t_server_no_chat), ("model list from the provider", t_models_from_provider), ("client info", t_info),
             ("plain chat (streamed)", t_plain_chat), ("project create + connect", t_project), ("file tools: write", t_write), ("file tools: read", t_read),
             ("docs search in the client", t_rag), ("code check", t_code_check), ("/compact", t_compact), ("picture", t_image), ("run guard", t_guard),
             ("saved chats", t_chats), ("cleanup", t_cleanup)]:
    check(n, f)
bad = [r for r in results if not r[1]]
print(f"\n{len(results) - len(bad)}/{len(results)} passed")
sys.exit(1 if bad else 0)
