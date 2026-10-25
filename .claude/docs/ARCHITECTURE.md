# Phoenix LAI: architecture map

State as of commit `ead94b6` (2026-10-21), branch `dev-coding-agent`. Everything below is traced from the source; file
references are relative to the repository root.

Phoenix LAI is two separate Windows programs plus a VS Code extension:

| Program | Started by | What it is |
|---|---|---|
| **Phoenix server** | `Phoenix_LAI_Server.lnk` | Runs Gemma 4 models on the GPU (Vulkan) and serves them as an OpenAI-compatible API on `http://localhost:1234/v1`. Tray icon, dashboard and console. Knows nothing about the client. |
| **Phoenix Client** | `Phoenix_Client.lnk` | The chat app, shown in a Chrome (or Edge) app window. An OpenAI `/v1` client for any provider (the Phoenix server is one of them), with projects and file tools, docs search, skills, code checks and chat history. |
| **VS Code extension** | installed `.vsix` | Shows the Phoenix Client's chat page in a VS Code side panel, and gives inline code completion from the server. |

```
 ┌───────────────────────────────┐          ┌──────────────────────────────────────┐
 │ PHOENIX SERVER                │   /v1    │ PHOENIX CLIENT                       │
 │ models on the GPU (Vulkan)    │◄─────────┤ chat window (Chrome --app)           │
 │ localhost:1234/v1   (API)     │          │ 127.0.0.1:8790-8798  (its own page)  │
 │ 127.0.0.1:8780-8789 (dashboard│          │ talks /v1 to any provider:           │
 │   and console pages only)     │          │   Phoenix Local Server (built in),   │
 │ tray icon                     │          │   LM Studio, Ollama, LAN, cloud      │
 └──────────────▲────────────────┘          └───────────────────▲──────────────────┘
                │ /v1 (code completion)                         │ chat page in an iframe
       ┌────────┴───────────────────────────────────────────────┴───┐
       │ VS CODE EXTENSION                                           │
       └─────────────────────────────────────────────────────────────┘
```

Both programs are the same C# code base (`src\*.cs`), compiled in memory at every start. One flag decides which half
runs: `WebServer.IsClient`.

---

## 1. Entry point: from the shortcut to running code

### 1.1 The shortcuts

The repository root holds two shortcuts, made by `.claude\tools\make-shortcut.ps1`:

| Shortcut | Runs |
|---|---|
| `Phoenix_Client.lnk` | `src\run-phoenix-client.ps1` |
| `Phoenix_LAI_Server.lnk` | `src\run-phoenix-server.ps1` |

Each shortcut's target is `C:\Windows\System32\conhost.exe --headless powershell.exe -NoProfile -STA
-ExecutionPolicy Bypass -EncodedCommand <…>`. `--headless` means no console window ever appears. The encoded command is
a few lines that call `GetStartupInfoW` (kernel32) to read the shortcut's own path (Windows passes it in
`STARTUPINFO.lpTitle` when a program starts from a shortcut), then run the named script in the `src\` folder next to
the shortcut. So the project folder can live anywhere: nothing in the shortcut is an absolute project path.

### 1.2 The launcher scripts

- `src\run-phoenix-server.ps1` calls `run.ps1 -Role Server` (optionally `-Model <gguf> -Context <n>`).
- `src\run-phoenix-client.ps1` calls `run.ps1 -Role Client`.

### 1.3 `src\run.ps1`: check, compile, start

1. **Refuses PowerShell 7.** Only Windows PowerShell 5.1 (`powershell.exe`) may run it.
2. **Locality checks** (`Test-Locality`, about 120 lines). Every source file, the `html\` folder and the repository are
   scanned; any failure stops the start with a red list. The full rule set is in [SECURITY.md](SECURITY.md) §2.
   `run.ps1 -CheckOnly` runs only this step.
3. **In-memory compile.** All `src\*.cs` files are compiled with the .NET Framework 4.8 CodeDom compiler through
   `Add-Type`, options `/unsafe /optimize+`, references `System.dll`, `System.Core.dll`, `System.Drawing.dll`,
   `System.Windows.Forms.dll` only. That compiler supports **C# 5** only. Nothing is written to disk, and types cannot
   be reloaded, so every run is a fresh `powershell.exe` process.
4. **Start the role:**
   - `-Role Server` → `[Phoenix.ServerHost]::Run($Root, $modelPath, $Context)`
   - `-Role Client` (also the default) → `[Phoenix.WebServer]::RunClient($Root)`

There is no all-in-one mode: the server and the client are always separate processes.

---

## 2. The Phoenix server

### 2.1 Startup (`src\ServerHost.cs`)

1. **Single instance:** mutex `Local\PhoenixAI.Server`. A second start shows "A Phoenix server is already running" and
   exits.
2. **`ServerHost.Start`** creates a `WebServer` with `IsClient = false` on the first free port in **8780-8789**
   (`WebServer.StartOnFreePort`), sets `ServerMode = true`, and loads `-Model` in the background if one was given.
   Otherwise no model loads until a request asks for one.
3. The `WebServer` constructor (`src\WebServer.cs`):
   - binds `http://127.0.0.1:<port>/` for the dashboard and console pages;
   - opens the session log (`logs\session-<date>.log` and `.jsonl`, `ChatLog`);
   - creates `Workspace.ProjectsRoot` (`%USERPROFILE%\Phoenix_Projects`) if missing;
   - opens the docs index (`Rag`, with the local embedding model from `models\`, used for `/v1/embeddings`) and
     rebuilds it in the background if it is missing or outdated;
   - **starts the OpenAI API** (`StartApi`, `src\WebServer.OpenAi.cs`) on `http://127.0.0.1:1234/` and
     `http://localhost:1234/`. If port 1234 is taken, the server runs without the API and says so.
4. **Tray** (`BuildTray`): a notification-area icon. A click opens `TrayPanel` (`src\TrayPanel.cs`), a dark mini
   dashboard updated every second (state, GPU load graph, VRAM, KV cache, connected clients, API address, GPU, model,
   uptime) with the buttons **Start/Stop, Restart, Console, Dashboard, Exit**. Console and Dashboard open
   `console.html` / `dashboard.html` in a browser app window.
5. `Application.Run()` until Exit; then the tray is removed and `Stop()` unloads the model and closes the listeners.

### 2.2 The OpenAI API (`src\WebServer.OpenAi.cs`)

Every request first passes the request checks ([SECURITY.md](SECURITY.md) §4): `Host` must be `127.0.0.1:1234` or
`localhost:1234`, `Origin` (if sent) must be the API's own address or a VS Code webview, and the optional API key must
match.

| Endpoint | What it does |
|---|---|
| `GET /v1/models` | The chat models in `models\` that **fit the memory free now** (see 2.4). Each entry: `id` (file name without `.gguf`), `object`, `created`, `type`, `state` (`loaded` / `not-loaded`), `max_context_length` for the loaded one, `owned_by`. |
| `GET /v1/models/{id}` | One model, or 404 `model_not_found` (with "does not fit" in the message when the file exists but is hidden). |
| `POST /v1/chat/completions` | Chat. Streaming (SSE) or whole. Supports `n` (1-8), `stop` (up to 4), `seed`, `max_tokens` / `max_completion_tokens`, `temperature`, `top_p`, `top_k`, `repeat_penalty`, `presence_penalty` / `frequency_penalty`, `response_format` (`json_object`, `json_schema`), `tools` + `tool_choice`, `stream_options.include_usage`, `logprobs` + `top_logprobs` (0-20), image input (`image_url` data URIs, models with a vision file). |
| `POST /v1/completions` | Text completion through the chat template; with `suffix` it is code fill-in-the-middle (FIM tokens if the model has them, otherwise an instructed completion). `logprobs` (0-20) in the legacy shape. |
| `POST /v1/embeddings` | EmbeddingGemma vectors (768 dims), `encoding_format` float or base64, `dimensions` (Matryoshka truncation, re-normalised). Accepts any model name: the embedding model is internal. |
| `GET /api/v1/models`, `/api/v0/*` | LM Studio's native paths, for tools configured for LM Studio. |

Model names:
- A known, fitting model name loads that model if another one is loaded.
- No name, or `phoenixlai` (what the VS Code extension sends), means the loaded model; with nothing loaded, the last
  used one (`models\last-model.txt`) if it fits, else the first model that fits.
- Any other name gets 404 `model_not_found`.

**Tool calling** (`src\ApiTools.cs`): the request's `tools` go into the system prompt as a compact list; the model
answers a call as `<tool_call>{"name":…,"arguments":{…}}</tool_call>`; the server parses that into OpenAI `tool_calls`.
**The server never runs a tool.** The calling program (for example a VS Code agent) runs it and sends the result back.

**Streaming details:** a stream starts at once with an SSE comment and sends `: waiting` every 2 s while it is queued
or the model loads, and `: reading n/total` while a long prompt is read, so clients do not time out; a client that
hangs up cancels the work. Requests are served one at a time (`_engineLock`); others wait in turn.

### 2.3 The engine (model loading and generation)

| Piece | File | Role |
|---|---|---|
| GGUF reader | `Gguf.cs` | Memory-mapped reader for model files (metadata, tensor table). |
| Tokenizer | `Tokenizer.cs` | Gemma 4 SentencePiece-style BPE (also the older byte-level BPE). |
| Vulkan | `Vulkan.cs`, `Gpu.cs` | Compute-only bindings to the driver's `vulkan-1.dll`; device, buffers, dispatch, `GpuGate`, device-lost recovery. |
| Kernels | `SpirvBuilder.cs`, `Kernels*.cs` | Every GPU kernel is SPIR-V generated by C# code at startup (no shader compiler): matvec / matmul for Q4_0, Q8_0, Q4_K-Q6_K, RMSNorm, RoPE, attention (decode, batched prefill, tiled "flash" and register-blocked versions), KV store, Instant Recall gather/scatter. |
| CPU reference | `CpuRef*.cs`, `GemmaCpu.cs` | Scalar + `Parallel.For` versions of every op: ground truth for the GPU, and the CPU fallback. |
| Gemma 4 on GPU | `GemmaGpu.cs` | The forward pass: fp16 KV cache in VRAM, sliding-window layers as rings, batched prefill, E2B/E4B per-layer embeddings, shared KV layers. |
| Vision | `Vision.cs` | Gemma 4 image encoders (ViT for E2B/E4B, encoder-free for 12B) from the model's `mmproj` file. |
| Embeddings | `Embedder.cs`, `GemmaEmbedGpu.cs` | EmbeddingGemma 2 on the GPU (own Vulkan device) or CPU. |
| Chat engine | `Engine.cs` (`ChatEngine`) | Loads a model (GPU, CPU fallback), renders the Gemma 4 chat template, reuses the KV cache across turns (exact sampled tokens), Instant Recall splices, prompt-lookup speculative decoding, logprob capture. |
| Sampler | `Sampler.cs` | Repetition penalty, temperature, top-k, top-p, seeds. |
| Context planner | `ContextPlanner.cs` | Picks the context length at load from free VRAM (or RAM) and Windows commit memory. |
| Model lock | `ModelLock.cs` | `%LOCALAPPDATA%\LocalLLMStudio\model.lock`: one loaded chat model per machine, across processes. |
| Memory trim | `MemoryTrim.cs` | Gives RAM back after the weights are on the GPU (kernel32). |
| Stay awake | `StayAwake.cs` | Keeps the displays on during long GPU work (a display power-down mid-GPU-work blacked out the screens on 2026-10-16). |
| GPU profile | `Gpu.cs` | `auto` / `amd` / `nvidia` (+ `safe`), from `%LOCALAPPDATA%\LocalLLMStudio\gpu-profile.txt` or `PHOENIX_GPU_PROFILE` / `PHOENIX_GPU_SAFE`. `nvidia` prefers vendor 0x10DE and halves the layers per GPU submit. |

Only **Gemma 4** models run (12B, E2B, E4B; the 31B file is present but needs 21 GB of VRAM). Other architectures are
refused at load.

### 2.4 Which models are offered (`ApiModelFiles`)

For each `.gguf` in `models\` that is a chat model (not an embedding model or a vision projector):
1. `Unsupported(f)`: refused if it is not Gemma 4 or has a weight format the engine cannot run.
2. `Need(f)`: the memory it needs at its smallest context (weights + KV cache), from `ContextPlanner.MinBytes`.
3. `FreeForModel`: VRAM not held by other programs (total budget minus other processes' dedicated use, from the
   "GPU Process Memory" performance counters), or available RAM on the CPU, plus Windows commit; the model already
   loaded counts as free.
4. Offered only if it fits. The list is rechecked at most every 3 s; each change is logged ("API models not offered: …").

### 2.5 Dashboard and console (server port 8780-8789)

- `dashboard.html` (`js\dashboard.js`) polls `GET /api/server-status` every second: load, memory, model, what the AI is
  doing, connected clients, recent requests.
- `console.html` (`js\console.js`) sends one line to `POST /api/console`. Commands (`WebServer.Dashboard.cs`):
  `help`, `status`, `clients`, `recent`, `models`, `load`, `unload`, `bench`, `apitest`, `vram`, `debug`, `log`, `gc`,
  `apikey new|set|off`, `restart`. Phoenix commands only: the console never runs a shell.
- The server port serves only those pages and the dashboard's `/api` calls; chat pages and client calls get 404.

### 2.6 Server data

| Where | What |
|---|---|
| `models\*.gguf` | Model files (copied in by hand; nothing is downloaded). |
| `logs\session-*.log` / `.jsonl` | Session log (requests, loads, API models note, errors). |
| `docs\.index\rag.bin` | Docs index (shared with the client, see §4). |
| `%LOCALAPPDATA%\LocalLLMStudio\` | `model.lock`, `gpu-profile.txt`, `device.txt`, `debug.txt`. |
| Credential Manager `PhoenixLAI.Server` | The optional API key (off by default). |

---

## 3. The Phoenix Client

### 3.1 Startup (`WebServer.RunClient`, `RunServer` in `src\WebServer.cs`)

1. **Single instance:** mutex `Local\PhoenixAI.Client`. A second start signals the event `Local\PhoenixAI.ClientShow`
   and exits; the running client then brings its window forward, or opens a new one if none is open.
2. A `WebServer` with `IsClient = true` on the first free port in **8790-8798**: listener, `logs\client-*.log`, the
   projects root, and the docs index with a **remote embedder** (`RemoteEmbedder`, the Phoenix server's
   `/v1/embeddings`). No engine, no GPU, no API.
3. The active provider's saved model is picked in the background (`UseProviderModel`).
4. **The window:** if a page from a previous run reconnects within 3 s, it is reused; otherwise `OpenWindow` starts
   Chrome (or Edge, setting Auto / Chrome / Edge) as `--app=http://127.0.0.1:<port>/chat.html` with a private profile
   (`%LOCALAPPDATA%\LocalLLMStudio\<browser>-profile`), 1300×800, centred. This is the only process the code ever starts
   (`ShellGuard.CheckLaunch`).
5. The client runs while its window is open, then for up to 75 s while any page still checks in (each page checks in
   every 2 s and says goodbye on close), then saves the project index and exits.

### 3.2 Providers (`src\Providers.cs`, `html\js\providers.js`)

- **Store:** `%LOCALAPPDATA%\LocalLLMStudio\providers.json` (id, name, base URL, model, tested features; never a key)
  and `active-provider.txt`. API keys are in Windows Credential Manager (`CredStore.cs`, resource
  `PhoenixLAI.Connections`), never in a file or sent back to the page.
- **Phoenix Local Server** is built in: first in the list, `http://localhost:1234/v1`, cannot be deleted, renamed or
  re-pointed (only its model and key change).
- **Outgoing requests:** only `Providers.Outgoing`, through `CheckTarget` (http/https only, no `user:password` in the
  URL), never following redirects, TLS 1.2+, system proxy for non-loopback hosts. See [SECURITY.md](SECURITY.md) §5.
- **Live status:** each provider is checked with a short `GET /models` (timeout 2.5 s) every 2 s on this PC or the LAN
  and every 15 s on the internet; every chat or embedding call also marks it up or down at once. The page polls
  `/client/provider-status` every second: green online, red offline, blue sending or receiving, grey checking. When the
  active provider comes back, the client reconnects and picks its model up again by itself.
- **Test** (provider window): model list, chat, streaming, embeddings, then 15 option probes (system message,
  `max_completion_tokens`, temperature/top_p, penalties, seed, stop, n, logprobs, JSON mode, JSON schema, tool calling,
  include_usage, image input, `/completions`, FIM), an unknown-model error check, the connection details and response
  headers, every model's `/models/{id}` record and LM Studio / Ollama native lists. The resulting **feature map** is
  saved with the provider.
- **Features in use:** `RemoteChatModel` sends only what the provider allows: no streaming → one whole reply; no system
  role → the system text goes in front of the first user message; refused sampling / seed / usage fields and pictures
  are left out; no embeddings → keyword search only. An untested provider gets everything.

### 3.3 The chat page (`html\chat.html`)

| File | Role |
|---|---|
| `js\chat.js` | Chat, streaming, slash commands (`/help` lists them), settings, model dropdown, status bar, recents, projects. |
| `js\providers.js` | Providers list, provider window (themed model dropdown, features, privacy toggle, test), live status, client-stopped dialog. |
| `js\skills.js` | Skills list and the Skill Builder. |
| `js\markdown.js`, `js\highlight.js` | Reply rendering and code highlighting (no libraries). |
| `css\chat.css`, `css\skills.css` | Theme (dark and light). |
| `vscode.html` + `js\vscode-*.js`, `css\vscode.css` | The same chat inside VS Code, in VS Code's theme. |

The page talks only to its own client on `127.0.0.1:<port>`: `/api/*` (chat, chats, projects, skills, settings) and
`/client/*` (providers). The model dropdown lists every provider's models grouped by provider, greys out offline ones,
and is disabled when no provider is online. If the client's back end stops, the page blurs and shows a dialog with
**Exit**; restarting `Phoenix_Client.lnk` reconnects the window by itself.

### 3.4 What happens when you send a message (`WebServer.Chat`)

1. **Run guard first** (`ShellGuard.CheckRunRequest`): a request to run something is counted; the 3rd within an hour
   gets a warning, the 6th locks chat input for 5 minutes. The model is not called for these.
2. **Model:** the active provider's (`EnsureClientModel` → `RemoteChatModel`).
3. **Building the prompt** (`PrepareMessages`):
   - **Phoenix Local Server** (allowed local context): system prompt (`SystemPrompts`, per model size) + RULES.md
     (app and project) + file-tool instructions when a project is open + the docs-library note + docs-search sections
     (for code requests: that language's syntax sheet, Essentials card first) + project-index matches + the skills
     catalog and matched skills.
   - **Any other provider:** the system prompt and what the user typed, plus a note that it has no file access. No
     RULES.md, docs, project files, file tools or skills. (`LocalContextAllowed`; see [SECURITY.md](SECURITY.md) §6.)
4. **Generation:** streamed from the provider (or whole, if it refused streaming).
5. **Code requests:** the draft is generated out of sight, checked by `CodeCheck` / `CodeLint` (C# 5, PowerShell 5.1 vs
   7, SQL dialects, VBScript, VBA, CMD, DAX, M, Qlik, Excel, HTML, CSS); findings go back to the model as exact fixes,
   up to two rounds; only the checked or corrected reply is shown.
6. **Tool loop** (only with a project open and local context allowed): the reply's tags are run (§3.5) and results go
   back to the model; at most **6 steps** per reply; the same failure twice stops the loop; a reply that only wrote
   files ends the turn.
7. **Auto-compact** at 80% of the context window (`/compact` by hand): the history is summarised by the model.
8. The chat is saved (`.phoenix\chats` of the project, or `%USERPROFILE%\.phoenix\chats` without one).

### 3.5 Projects and file tools (`src\Workspace.cs`)

- A project is a folder directly inside `%USERPROFILE%\Phoenix_Projects`, opened with `/folder /name` or a click under
  Projects. `EnsureProjectLayout` adds `RULES.md` and `.phoenix\{chats,index,memories,recall,skills}`.
- The model acts by writing **text tags** in its reply; the client parses and runs them. This needs no OpenAI tool
  calling: only chat completions.

| Tag | Action |
|---|---|
| `<list path="."/>` | List a folder (folders first, sizes). |
| `<read path="…"/>`, `lines="120-180"` | Read with line numbers; a file over 300 lines read without `lines=` returns an outline (functions with line numbers); 200 KB cap. |
| `<search query="…" path="…"/>` | Text search across the project (up to 60 hits, `path:line: text`). |
| `<edit path="…"><find>…</find><replace>…</replace></edit>` | In-place replace; the find text must occur exactly once; an indentation-insensitive match is tried next; on failure the closest lines come back. Several pairs per tag. |
| `<write path="…">…</write>` | Create or replace a whole file (2 MB cap). |
| `<delete path="…"/>` | Move the file to `.llm-backup`. |

Every edit, write and delete first copies the previous version to `.llm-backup\<time>\`. With `/mode accept`, each
action waits for Accept or Deny. After a change, the project index updates that file (`ProjectIndex.Touch`).
Jail and write rules: [SECURITY.md](SECURITY.md) §3.

### 3.6 Search features

| Feature | Files | What |
|---|---|---|
| Docs search (RAG) | `Rag.cs` | `docs\` (syntax sheets in `docs\code-syntax`, Gemma notes in `docs\gemma-4`): markdown-aware chunks, BM25 + EmbeddingGemma vectors fused by reciprocal rank, memory-mapped `docs\.index\rag.bin`, rebuilt incrementally (`/index`). Vectors come from the server; with it offline, keywords only (retried after 20 s). |
| Project index | `ProjectIndex.cs`, `WebServer.Project.cs` | Live index of the open project in `.phoenix\index\project.idx`: keyword updates in milliseconds on every save (model writes and a `FileSystemWatcher`), vectors in the background while the chat is idle. |
| Skills | `Skills.cs`, `WebServer.Skills.cs`, `js\skills.js` | Agent Skills folders (`SKILL.md` with name/description); app skills in `skills\`, project skills in `.phoenix\skills`. The catalog goes into the system prompt; matched or `/name`-called skills go in front of the newest user message. Skill Builder with model-assisted drafting. |

Dropped in the client because they need the server's KV cache: Instant Recall, answer-while-typing, and GPU settings.

### 3.7 Client data

| Where | What |
|---|---|
| `%LOCALAPPDATA%\LocalLLMStudio\` | `providers.json`, `active-provider.txt`, `browser.txt`, `folder-access.txt`, `mode.txt`, `recent-folders.txt`, `debug.txt`, `guard.dat`, `<browser>-profile\`, `skill-trash\` |
| `%USERPROFILE%\.phoenix\chats\` | Chats made without a project (older ones are moved here from `%LOCALAPPDATA%\LocalLLMStudio\chats`). |
| `%APPDATA%\PhoenixLAI\guard.dat` | Second copy of the run-guard lockout. |
| Credential Manager `PhoenixLAI.Connections` | Provider API keys. |
| `<project>\RULES.md`, `.phoenix\`, `.llm-backup\` | Project rules, chats, index, skills, backups. |
| `logs\client-*.log` / `.jsonl` | Client log (RULES / RAG / PROJECT / TOOL / CHECK / LOCAL CONTEXT lines). |

---

## 4. Shared pieces and how the programs relate

- **Code:** both programs compile the same `src\*.cs`; `IsClient` decides which half runs. `IChatModel`
  (`ChatModel.cs`) lets the chat pipeline run on the local engine (`ChatEngine`) or on any provider (`RemoteChatModel`);
  `IEmbedder` does the same for embeddings.
- **The only link between them is `/v1`.** The client is an ordinary OpenAI client of the server. With the server
  off, the client only shows the Phoenix Local Server red (and docs search drops to keywords). With the client closed,
  the server keeps serving other programs.
- **Docs index:** both open `docs\.index\rag.bin` and both rebuild it at startup when it is missing or outdated.
  (Possible clash if both rebuild at the same moment; not verified.)
- **Logs:** `logs\session-*` (server) and `logs\client-*` (client). `/debug on` adds step-by-step `[debug …]` lines.

---

## 5. VS Code extension (`vscode-ext-source\`, version 2.7.0)

Plain JavaScript, VS Code API and Node's `http` only, no npm. `build.ps1` packs `vscode-ext\phoenixlai-<ver>.vsix`.
- **Side panel:** an iframe of the Phoenix Client's `vscode.html` (`127.0.0.1:8790-8798`); it connects the client to
  the open VS Code folder when that folder is inside `Phoenix_Projects`.
- **Inline completion:** `POST /v1/completions` with `suffix` to the server on `127.0.0.1:1234` (model `phoenixlai`).

---

## 6. Development layout

| Folder | Contents |
|---|---|
| `src\` | All C# and the launcher scripts. |
| `html\` | Pages, scripts, styles, fonts, images (no external URLs). |
| `models\` | GGUF files (not in git). |
| `docs\` | The docs library searched by RAG; `docs\checkers\` holds the PowerShell command lists for CodeCheck. |
| `skills\` | App skills. |
| `nvidia\` | Read-only test kit for the NVIDIA target machine (checks, probe, collector, kernel and Gemma tests, references). |
| `vscode-ext-source\`, `vscode-ext\` | Extension source and built packages. |
| `.claude\` | Dev only, never copied to production: tests and gates (`claude_tests\`), tools, reports, plans, these docs. |

Tests: phase gates 2-11 (`.claude\claude_tests\gate.ps1 -Phase N`), kernel tests, prefill bench, CodeCheck tests,
ShellGuard cases, and the Python QA suites `openai-compat.py` (OpenAI SDK against the server), `client-qa.py`,
`client-e2e.py`.

## 7. Known gaps

- Some header comments are older than the code: `WebServer.OpenAi.cs` ("any API key is accepted", "/v1/models: chat
  and embedding"), `Workspace.cs` (lists 3 of the 6 tags), `run-phoenix-client.ps1` ("servers on this computer"),
  `extension.js` ("the server's own chat"), the `ServerMode` field comment ("all-in-one app window").
- The client assumes a 32,768-token window when a provider's `/models` does not report one.
- The client's command list still offers `/recall`, `/device` and `/vram`, which belong to the server; in the client
  they answer "That is a Phoenix server setting".
- The docs index is opened and possibly rebuilt by both programs (see §4).
- The development plan (`.claude\Local LLM Studio (C#) — Development Plan.md`) and `.claude\PHOENIX_AGENT_PLAN.md`
  describe older or proposed designs.
