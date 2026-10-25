"""Phoenix Client providers QA (dev machine): providers, API keys, model lists, connection tests, network rules.
   python .claude/claude_tests/client-qa.py [client_port] [server_chat_port]
Needs run-phoenix-server.ps1 (API on 1234, console on server_chat_port, default 8765) and run-phoenix-client.ps1 (8790).
Turns the server's API key on (console: apikey set), adds a provider for it with the key, and checks: the key reaches
Windows Credential Manager and never the file or the page; the model list comes from the provider's API; the test runs;
a wrong / cleared key gets 401; switching provider changes the model list; an internet (https) provider is accepted and
its answer (401 without a key) shown; a redirect is not followed; bad URLs are refused. Cleans up at the end.
"""
import json, os, sys, time, subprocess, urllib.request, urllib.error

CP = int(sys.argv[1]) if len(sys.argv) > 1 else 8790
SP = int(sys.argv[2]) if len(sys.argv) > 2 else 8765
KEY = "phx-qa-" + os.urandom(9).hex()
BASE = f"http://127.0.0.1:{CP}"
PHX = "http://127.0.0.1:1234/v1"
STORE = os.path.join(os.environ["LOCALAPPDATA"], "LocalLLMStudio", "providers.json")
VAULT_PS = os.path.join(os.path.dirname(__file__), "vault-read.ps1")
results, made = [], []

def http(url, body=None, headers=None):
    data = json.dumps(body).encode() if body is not None else None
    r = urllib.request.Request(url, data=data, method="POST" if data else "GET", headers={"Content-Type": "application/json", **(headers or {})})
    try:
        with urllib.request.urlopen(r, timeout=600) as resp:
            t = resp.read().decode("utf-8"); return resp.status, (json.loads(t) if t else None)
    except urllib.error.HTTPError as e:
        t = e.read().decode("utf-8")
        try: return e.code, json.loads(t)
        except Exception: return e.code, t

def api(path, body=None, headers=None): return http(BASE + path, body, headers)
def console(line): return http(f"http://127.0.0.1:{SP}/api/console", {"line": line})[1]["text"]
def vault(id_):
    out = subprocess.run(["powershell.exe", "-NoProfile", "-File", VAULT_PS, id_], capture_output=True, text=True).stdout.strip()
    return None if out == "<none>" else out

def check(name, fn):
    t0 = time.time()
    try:
        d = fn() or ""; results.append((name, True)); print(f"PASS  {name:<48} {time.time()-t0:6.2f}s  {d}")
    except Exception as e:
        results.append((name, False)); print(f"FAIL  {name:<48} {time.time()-t0:6.2f}s  {type(e).__name__}: {e}")

st = {}
def save(**kw):
    b = {"name": "QA Phoenix", "url": PHX, "model": ""}; b.update(kw); return api("/client/provider-save", b)

def t_list():
    s, j = api("/client/providers"); assert s == 200 and any(p["id"] == "phoenix" for p in j["providers"]), j
    st["was_active"] = j["active"]; return f"{len(j['providers'])} provider(s), Phoenix server built in, active {j['active']}"
def t_key_on():
    assert "API key set" in console("apikey set " + KEY)
    s, _ = http(PHX + "/models"); assert s == 401; return "server requires a key now"
def t_models_with_key():
    s, j = api("/client/provider-models", {"url": PHX, "key": KEY}); assert s == 200 and j["models"], j
    assert not any("embed" in m or "mmproj" in m for m in j["models"]); return f"Load models: {len(j['models'])} chat models from the provider's API"
def t_models_without_key():
    s, j = api("/client/provider-models", {"url": PHX}); assert s == 400 and "401" in j["error"], j; return "no key -> the provider's 401 shown"
def t_save():
    s, j = save(key=KEY); assert s == 200 and j["provider"]["hasKey"] and KEY not in json.dumps(j), j
    st["id"] = j["provider"]["id"]; made.append(st["id"]); return st["id"]
def t_not_in_file():
    assert KEY not in open(STORE, encoding="utf-8").read(); return "providers.json holds no key"
def t_in_vault():
    assert vault(st["id"]) == KEY; return "Credential Manager: PhoenixLAI.Connections / " + st["id"]
def t_list_hides():
    s, j = api("/client/providers"); assert KEY not in json.dumps(j); return "list hides the key"
def t_test():
    s, j = api("/client/provider-test", {"id": st["id"]}); by = {r["name"]: r for r in j["results"]}
    assert not [r for r in j["results"] if r["state"] == "fail"], j["results"]
    assert "requires the key" in by["API key"]["detail"]; return ", ".join(f"{r['name']}:{r['state']}" for r in j["results"])
def t_use():
    s, j = api("/client/use", {"id": st["id"]}); assert s == 200 and j["provider"]["id"] == st["id"] and j["file"], j
    s, m = api("/api/models"); assert m["provider"]["id"] == st["id"] and m["models"]; return f"active, model {j['file']}"
def t_chat_through():
    s, t = None, None
    r = urllib.request.Request(BASE + "/api/chat?page=qa", data=json.dumps({"messages": [{"role": "user", "content": "Reply with the word mango."}], "max_tokens": 20, "temperature": 0}).encode(), headers={"Content-Type": "application/json"})
    body = urllib.request.urlopen(r, timeout=300).read().decode()
    text = "".join(json.loads(l[6:]).get("t", "") for l in body.split("\n") if l.startswith("data: "))
    assert "mango" in text.lower(), body[-300:]; return "chat through the keyed provider: " + repr(text.strip())
def t_wrong_key():
    save(id=st["id"], key="phx-wrong"); assert vault(st["id"]) == "phx-wrong"
    s, j = api("/client/use", {"id": st["id"]}); assert j.get("providerError") and "401" in j["providerError"], j
    return "wrong key -> " + j["providerError"][:60]
def t_clear_key():
    s, j = save(id=st["id"], clearKey=True); assert not j["provider"]["hasKey"] and vault(st["id"]) is None; return "key removed from Credential Manager"
def t_edit_keeps_key():
    save(id=st["id"], key=KEY); s, j = save(id=st["id"], name="QA Phoenix renamed", url="http://localhost:1234/v1")
    assert j["provider"]["hasKey"] and vault(st["id"]) == KEY and j["provider"]["name"] == "QA Phoenix renamed"; return "edit without a key keeps it"
def t_internet():
    s, j = api("/client/provider-models", {"url": "https://api.openai.com/v1"})
    assert s == 400 and "401" in j["error"], j; return "https internet provider reached: " + j["error"][:60]
def t_internet_save():
    s, j = save(name="QA cloud", url="https://api.openai.com/v1"); assert s == 200 and not j["provider"]["local"], j
    made.append(j["provider"]["id"]); return "saved, marked cloud (not local)"
def t_redirect():
    s, j = api("/client/provider-models", {"url": "http://github.com/v1"})   # 301 -> https: must not be followed
    assert s == 400 and "redirect" in j["error"].lower(), j; return "not followed: " + j["error"][:70]
def t_bad_urls():
    out = []
    for u in ["ftp://example.com/v1", "file:///C:/Windows", "http://user:pass@127.0.0.1:1234/v1", "not a url", "javascript:alert(1)"]:
        s, j = save(name="bad", url=u); assert s == 400, (u, s, j); out.append(u.split(":")[0])
    return "refused: " + ", ".join(out)
def t_phoenix_stays():
    s, j = api("/client/provider-delete", {"id": "phoenix"}); assert s == 400; return "the Phoenix server cannot be deleted"
def t_host_origin():
    s, _ = api("/client/providers", headers={"Host": "evil.example"}); assert s == 403
    s, _ = api("/client/providers", headers={"Origin": "http://evil.example"}); assert s == 403; return "foreign Host / Origin -> 403"
def t_delete():
    console("apikey off")   # first, so the provider switched back to answers at once
    api("/client/use", {"id": st.get("was_active") or "phoenix"})
    for m in list(made): api("/client/provider-delete", {"id": m}); made.remove(m)
    assert vault(st["id"]) is None; return "providers and keys removed, server key off"

try:
    for n, f in [("provider list", t_list), ("server key on", t_key_on), ("load models with key", t_models_with_key),
                 ("load models without key", t_models_without_key), ("save with key", t_save), ("key never in providers.json", t_not_in_file),
                 ("key in Windows Credential Manager", t_in_vault), ("list hides key", t_list_hides), ("provider test", t_test),
                 ("switch provider", t_use), ("chat through it", t_chat_through), ("wrong key -> 401", t_wrong_key), ("clear key", t_clear_key),
                 ("edit keeps key", t_edit_keeps_key), ("internet provider (https)", t_internet), ("save internet provider", t_internet_save),
                 ("redirect not followed", t_redirect), ("bad URLs refused", t_bad_urls), ("Phoenix provider stays", t_phoenix_stays),
                 ("Host / Origin checks", t_host_origin), ("cleanup", t_delete)]:
        check(n, f)
finally:
    try: console("apikey off")
    except Exception: pass
    for m in made: api("/client/provider-delete", {"id": m})
bad = [r for r in results if not r[1]]
print(f"\n{len(results) - len(bad)}/{len(results)} passed")
sys.exit(1 if bad else 0)
