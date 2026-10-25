# Phoenix LAI: change log

Newest first. Commit hashes refer to this repository (`git show <hash>`). Measurements are from the dev machine (AMD RX
6700 XT, 12 GB) unless noted. Sources: the commit messages, `.claude\reports\`, and the work sessions.

The project was called **Local LLM Studio**, then **PhoenixAI**, and since 2026-10-16 **Phoenix LAI**.

---

## 2026-10-25 · FleetHacker port blocks (branch `ports/fleet-port`)

- Ports moved to the approved edge-ops blocks (`PORTS.md`, `src\FleetPorts.psd1`): **server dashboard 8780-8789**
  (exclusive; was 8765-8789, which overlapped Lasso, Synapse, MailHawk and Keystone), **Phoenix Client and VS Code client
  8790-8798** (multi; was 8790-8799), **API 1234** unchanged, **8799** kept for the dev test (`chat-e2e.ps1`).
- `WebServer.cs`: `GET /__fleethacker/id` (`{"tool":"phoenix-lai-server"|"phoenix-lai-client","version","pid"}`, no path,
  so nothing can force-stop Phoenix) and `POST /__fleethacker/shutdown` (header `X-FleetHacker-Shutdown: 1`; the server
  exits as from the tray, the client and VS Code client end their run loop). A `localhost:<port>` prefix is added for
  these paths only; everything else still requires `Host: 127.0.0.1:<port>`.
- `run.ps1 -Role Server` checks the block with `src\FleetPort.psm1` (vendored from edge-ops 6e326f9) before the compile:
  a running server of the same or a newer version gets a message and exit 0; an older one is refused with a clear message
  (never stopped). The client keeps its one-window mutex.
- `nvidia\1-check.ps1`, `nvidia\2-probe.ps1`, `NVIDIA.md`, the dev tests and docs use the new ports. The VS Code
  extension still scans 8790-8799 (it finds the client inside 8790-8798; not rebuilt here).

## 2026-10-21 · `ead94b6` · Server and client fully separated

Merged into `master`; branch `dev-coding-agent` created from it. This commit also carries the 2026-10-20 split work
below.

### Architecture
- **Two programs, never one process.** Server (`Phoenix_LAI_Server.lnk`, `run.ps1 -Role Server`) and client
  (`Phoenix_Client.lnk`, `-Role Client`, also the default). The all-in-one mode (`run.ps1` with no role, the old
  `PhoenixLAI_App.lnk`) is removed.
- **The server knows nothing of the client:** the `/api/restart-client` endpoint (added and removed the same day),
  `StartClientInBackground`, `ClientRunning` and the tray's **Client** button are gone. The tray now has Start/Stop,
  Restart, Console, Dashboard, Exit.
- **The client is an OpenAI `/v1` client for any provider.** The server being offline only turns the Phoenix Local
  Server red; the client keeps working.

### Server (`/v1` API)
- `/v1/models` lists only models that **fit the memory free now** (VRAM, or RAM on the CPU, plus Windows commit; the
  loaded model counts as free). Rechecked at most every 3 s; changes logged. On the dev machine the 31B model
  (needs 21 GB, 10 GB free) is hidden.
- Requests for a hidden model get 404 `model_not_found`, "is not available: it does not fit the memory free on this
  machine now"; with nothing loaded and no name, only a fitting model is loaded.
- **Unknown model names get 404 `model_not_found`** (OpenAI behaviour). `phoenixlai` (the VS Code extension's name)
  and no name mean the loaded model.
- **`logprobs` / `top_logprobs`** (0-20) on chat, `/completions` (legacy shape) and streams; values from the raw
  distribution; special tokens by name (`<turn|>`); requests with logprobs skip prompt-lookup speculation.

### Client: providers
- Phoenix Local Server built in: always first, `http://localhost:1234/v1` (was `127.0.0.1`), cannot be deleted,
  renamed or re-pointed; saved lists are corrected on load.
- **Live status per provider:** green online, red offline, blue sending/receiving, grey checking. Background
  `GET /models` checks every 2 s (this PC, LAN) or 15 s (internet); every call marks up/down at once. Offline shows in
  about 2.5-5 s; back online in under a second. When the active provider returns, the client reconnects and picks its
  model again (conversation kept).
- **Test shows everything:** connection (reach, TLS, proxy, key required, all response headers), every model's full
  record, `/models/{id}`, LM Studio and Ollama native lists, a sample reply's raw JSON, and 15 option probes plus an
  unknown-model error check. 
- **Feature map saved per provider** (18 features). The client never sends a refused feature: whole replies without
  streaming, system text merged into the first user message, no refused sampling/seed/usage fields, no pictures, no
  embeddings. A test result applies to the model in use at once.
- **Model dropdown lists every provider's models**, grouped, with status dots; offline providers greyed out; picking a
  model of another provider switches provider; disabled when no provider is online ("… offline").
- **Local context only to the Phoenix Local Server** (approved): other providers get the system prompt and what the user
  types; no RULES.md, docs, syntax sheets, project files, file tools or skills. Greyed-out "send local context" toggle
  in each provider's window. Verified with a recording relay.
- Credential Manager lookups cached (a missing key no longer throws every 2 s).

### Client: UI
- Client-stopped dialog: the page blurs (3 px) and offers **Exit**; restarting the client reconnects the window.
- Provider window: fixed title "OPENAI V1 COMPATIBLE PROVIDER SETTINGS" in a 30 px header; section heading removed;
  themed model dropdown (as wide as the longest model name, at least 100 px, rounded hover, white names) with a gold
  **load models** button; features as green / struck-out chips; privacy section.
- Popup borders grey instead of gold; side panel shows machine name, then user name (provider line removed); the
  Skills / Projects / Providers **+** buttons use the same 16 px icon as New chat.

### Docs and rules
- `CLAUDE.md`: split rule rewritten (fully separate), local-context rule, entry points `Phoenix_Client.lnk` /
  `Phoenix_LAI_Server.lnk`.
- New `.claude\docs\` (this log, `ARCHITECTURE.md`, `SECURITY.md`) and a root `README.md`.

## 2026-10-20 · (in `ead94b6`) · First server / client split

- `run.ps1 -Role Server`: `ServerHost.cs` (tray, single instance `Local\PhoenixAI.Server`), `TrayPanel.cs` (mini
  dashboard), `html\dashboard.html` and `console.html` (`WebServer.Dashboard.cs`: `/api/server-status`,
  `/api/console` with `status`, `clients`, `recent`, `models`, `load`, `unload`, `bench`, `apitest`, `vram`, `debug`,
  `log`, `gc`, `apikey`, `restart`).
- `run.ps1 -Role Client`: `WebServer` with `IsClient` on ports 8790-8799; the chat with every feature runs in the client
  and calls the active provider through `IChatModel` (`ChatModel.cs`, `RemoteChatModel`); embeddings through
  `RemoteEmbedder`.
- **Providers (approved):** add any OpenAI `/v1` provider by URL (this PC, LAN, internet). Only `Providers.cs` makes
  outgoing requests, via `Outgoing` → `WebRequest.Create(CheckTarget(…))`: http/https, no credentials in the URL, no
  redirects, TLS 1.2+ (`run.ps1` rule 4c). Keys in Windows Credential Manager (`CredStore.cs`, WinRT `PasswordVault`
  by reflection). Server's own optional API key (console `apikey`).
- Server ports narrowed to 8765-8789 so they never collide with the client's.
- QA (`.claude\reports\2026-10-20-server-client-qa.md`): OpenAI Python SDK 32/32 (`openai-compat.py`), client 24/24
  (`client-qa.py`). Fixed: test picked the alphabetically first model (forced a model swap); `reasoning_content`
  shown as a "Thinking" line; start-up calls returning 404 in the client.
- Root clean-up: packaging scripts (`_Package_Build.ps1`, `_Package_Unpack.ps1`), `model_links.txt` and the VS Code
  workspace file removed; `__version.info` moved to the root (one version for server, client and pages).
- VS Code extension 2.6.1 and 2.7.0: the side panel embeds the Phoenix Client's page.

## 2026-10-18

- `9294c75` Document library searched for every message with topics; CPU mode back on with a Settings toggle.
- `1f17e34` Merge of `claude/sharp-pascal-290387` (Gemma only, faster prefill) into `merge-test`.
- `980d436` **Per-model defaults and system prompts** (Google's Gemma 4 settings: temperature 1.0, top_k 64, top_p
  0.95, no repeat penalty, 4096-token reply; full or compact senior-engineer prompt by model size; "defaults" restores
  them). **`/compact`** summarises the history, automatic at 80%. Package build / install scripts, code checks,
  skills, project index, NVIDIA test kit.

## 2026-10-17

- `e5358d2` **Gemma 4 E2B / E4B** (per-layer embeddings, shared KV layers, CPU and GPU). **Vision** via `mmproj`
  (gemma4v ViT for E2B/E4B, encoder-free gemma4uv for 12B, Pillow-exact preprocessing). Pictures by paste, drop or
  `/image`; the API takes `data:` images. Model list: grey = cannot run, red = will not fit. Gates 10 (text) and 11
  (vision). Studio shell redesign (sidebar, top bar, per-model settings).
- `222d2f4` **Gemma only:** Llama / Mistral support removed (LlamaGpu, LlamaCpu, ModelConfig, k-quant prefill, IQ
  GPU kernels, their templates, the q8/q4 KV setting). Non-Gemma models refused. Gates 2-9 re-based on Gemma 4 12B
  with fresh llama.cpp references.
- `5f67862` Merge of `claude/wizardly-pasteur-c88358` into `vscode-native-chat`.
- `5cd2201` **Debug mode** (`/debug on|off`): every step and first-chance exception as `[debug …]` log lines; prompt and
  reply logging by source; displays kept awake during indexing (`StayAwake`).
- `fe3439f` **Faster long-prompt prefill:** register-blocked attention sharing K/V across a KV head's query heads.
  Gemma 12B: 2,557 tokens 294 → 470 tok/s; 9,309 tokens 188 → 384 tok/s. `pfbench.ps1`.
- `91328bd` **Fast GPU embedder:** tiled f16 matmul and flash-style bidirectional attention, about 1,440 → 139 ms per
  512-token chunk.
- `7619d96` **VS Code extension 2.5** (side panel on the chat, edits as activity lines, undo, editor context, RULES.md
  command, reconnects). **Model file tools:** `edit` (find/replace in place), `search`, read line ranges, `delete` (to
  `.llm-backup`), closest-lines hint after a failed edit, stable folder listing (keeps the prompt cache valid).
  **Run guard:** 3rd run request warns (red WARNING banner), 6th locks chat for 5 minutes, lockout survives restarts
  (two HMAC-signed files); `run.ps1` refuses to start without the guard. KV-cache stash keeps a long conversation
  through short side requests. PowerShell 5.1 / 7 syntax sheets (every shipped command).

## 2026-10-16

- `8d12dd3` **VS Code native chat:** Phoenix LAI as a VS Code model provider; **OpenAI tool calling** in the API
  (`ApiTools.cs`, the server never runs a tool); `/v1/completions` with `suffix` (FIM); `max_context_length` in
  `/v1/models`; SSE pings while reading the prompt so Stop cancels prefill; zero-dependency extension with inline
  completion and a PS 5.1 `.vsix` packer.
- `71fa148` Fixed a deadlock on a chat sent right after a model load; models told they can see the syntax library;
  language questions search it.
- `c3e1c18` **Answer while typing** (typing pauses send the draft; the server pre-reads the prompt and drafts an answer;
  time to first word 2.24 → 1.37 s), lazy verification rows, typing effect.
- `dad1837` **Renamed to PhoenixLAI** (window, launcher, dialogs, docs); dark / light logos.
- `b01aded` Docs-search warm-up in the background after "ready"; implementation report.
- `cfa10cc` Test gates follow the current models; all pass.
- `2d0fabc` **Prompt-lookup speculative decoding:** tokens that followed the same 3-4 tokens earlier are guessed and
  verified in one batched pass; output unchanged; adaptive (stops under 70% kept). Llama 8B rewriting a file 57.1 →
  71.6 tok/s.
- `781467f` **Tiled ("flash") prefill attention:** 16 queries × 16 keys per workgroup. Llama 8B prompt 350 → 466 tok/s.
- `05997f6` **Instant Recall on disk** (`.phoenix\recall`, 8 GB per project, LRU) and `/recall` pre-read; the model may
  not write into `.phoenix`. A 16,185-token file after a restart: about 100 s → 1.3 s.
- `cd832b9` **Instant Recall:** files the model reads are recalled from a saved KV snapshot instead of re-read (keys
  re-rotated by the position difference). 6,067-token file in a new chat: 32.2 s → 0.56 s.
- `44fc21b` Audit report (`.claude\reports\2026-10-16-audit.md`).
- `da66a04` Audit pass 2: Gemma sliding-window ring 1,536 → 512 slack slots (−0.31 GB VRAM), dead kernels removed,
  **API refuses `Origin: null`**.
- `da7d64d` Audit pass 1: replies templated with the exact sampled tokens (code check 7.6 → 2.1 s), Gemma load 14.9 →
  7.9 s, **writes into `.git`, `.vscode`, `.idea`, `desktop.ini`, `.gitattributes`, `.gitmodules` refused** (indirect
  execution).
- `2e1b0e3` **Code check flow** (every reply with code checked, verdict line), **`/mode accept` approvals**, GPU-only
  for a time, Gemma control-token ban (empty replies), nudge when the model announces without a tag, prompt status row.
- `ad08760` 8-bit / 4-bit KV cache for Llama models (later removed with Llama).
- `5b39cd6` **Auto context** from free VRAM / RAM and Windows commit (`ContextPlanner`), live memory status, CPU stop
  and progress, chat survives reload (F5).
- `654fde0` **Checked code generation** (draft and self-check out of sight, only the checked reply shown; `CodeLint`
  adds missing C# usings), VS Code Dark+ highlighting, language badges, RAG topic folders, status colours; browser
  setting Auto (Chrome, else Edge), 1300×800 window.
- `0623db2` Batched prefill for Llama k-quant models (65 → 350 tok/s); warm-up before ready.
- `41fe139` **RAM released after GPU load** (`MemoryTrim`, 12B: 2.8 GB → 34 MB), `/device gpu|cpu`, `/folder on|off`.
- `1d7068a` **Shell ban** (`ShellGuard`: the model never runs PowerShell, cmd or any program; command tags refused; no
  executable files; enforced by `run.ps1`), **project jail** (`%USERPROFILE%\Phoenix_Projects`; absolute paths, `..`,
  junctions refused), **OpenAI-compatible API on `:1234`** (`/v1/models`, chat with streaming, completions,
  embeddings; LM Studio paths); RAG rebuilds an outdated index at start.
- `ead4eee` **Gemma 4 + EmbeddingGemma:** Gemma 4 decode and prefill on the GPU (fp16 ring KV cache, fused norms,
  batched Q4_0 matmul), EmbeddingGemma embedder, hybrid BM25 + vector RAG (index v2), GPU device-lost recovery,
  `GpuGate`, `/vram`, `/unload`, single-instance server.

## 2026-10-15 · The beginning

- `66b5a17` Chat UI polish: full markdown, `/` command list, `/files`, `/folder`, `/new`, `/settings`; RAG excerpts
  labelled as the user's docs library; version on the welcome screen; server stays up while a window still polls.
- `cb8d6e8` **Web chat UI** (`WebServer.cs` on 127.0.0.1 serving `html\chat.html` in an Edge app window; model
  switching, folder tools list/read/write, git status, RULES.md, session logs); WinForms UI removed; hidden launcher
  (`.lnk` → `conhost --headless` → `run.ps1`); **docs RAG** (`Rag.cs`, BM25, `docs\.index\rag.bin`, `/index`); tests
  moved to `.claude\claude_tests`.
- `b143297` Devstral (Mistral 3) support and GPU speed work: YaRN RoPE, tekken tokenizer, Q3_K / IQ2 / IQ3 formats,
  packed integer dot products, split decode attention, recorded decode pass, fused quantizers. 1B Q8_0 194.5 tok/s
  (llama.cpp Vulkan 78.1). (Removed later with Gemma-only.)
- `bb40e77` `Studio.bat` launcher (replaced by the hidden `.lnk` launcher).
- `51a5acf` **Phase 8:** studio UI (WinForms), sampler, chat engine (Llama 3 template, KV reuse, streaming). 8B at
  42 tok/s.
- `71c0641` **Phase 7:** Q4_K, Q5_K, Q6_K on CPU and GPU; Llama 3.1 8B Q4_K_M runs (~52 tok/s).
- `d78b872` **Phases 5-6:** Vulkan compute bindings, SPIR-V emitter, GPU forward pass (~65 tok/s on the 1B model);
  GPU logprobs within llama.cpp's own CPU/GPU spread.
- `e362a57` **Phases 3-4:** Llama 3 BPE tokenizer (20/20 vs llama-tokenize), CPU reference forward pass (logprobs mean
  |diff| 0.012 vs llama.cpp).
- `e2f5256` **Phase 2:** memory-mapped GGUF reader and gate.
- `801bb86` **Phase 1:** skeleton, PS 5.1 launcher compiling C# in memory, locality checks (no binaries, network,
  processes, assembly loading, URLs; P/Invoke only `vulkan-1.dll` / `kernel32.dll`); llama.cpp as a dev reference only.

---

## Approvals and rule changes (from `CLAUDE.md`)

| Date | Change |
|---|---|
| 2026-10-15 | Loopback chat server: only `WebServer.cs` may use `HttpListener` (127.0.0.1) and `Process.Start` (Chrome/Edge `--app` only). |
| 2026-10-16 | Model tools: list, read, edit, write, delete (delete moves to `.llm-backup`). Run guard and lockout. OpenAI-compatible API on `localhost:1234`. |
| 2026-10-17 | Llama / Mistral support removed: Gemma 4 only. |
| 2026-10-20 | Server / client split; the client may reach any OpenAI `/v1` provider by URL (only via `Providers.cs`). |
| 2026-10-21 | Fully separate programs; local context only to the Phoenix Local Server. |
