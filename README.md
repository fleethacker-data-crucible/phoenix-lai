# Phoenix LAI

> local Gemma models on your GPU behind an OpenAI-compatible API, plus a Chrome app client.

**Local AI for coding, on locked-down Windows machines.** Phoenix LAI runs Google's Gemma 4 models on your own GPU and
gives you a chat app that can work on your project files. It needs nothing installed: only Windows PowerShell 5.1 and
the GPU driver that is already there. No admin rights, no SDK, no downloads, no third-party code, and nothing leaves the
machine unless you add a provider that is somewhere else.

It is two programs that work alone or together:

| | **Phoenix server** | **Phoenix Client** |
|---|---|---|
| Start | `Phoenix_LAI_Server.lnk` | `Phoenix_Client.lnk` |
| What it does | Runs Gemma 4 models on your GPU and serves them as an OpenAI-compatible API on `http://localhost:1893/v1` | The chat app: talks to any OpenAI `/v1` provider, with projects, file editing, docs search, skills and code checks |
| Lives in | The notification area (tray icon, dashboard, console) | A Chrome (or Edge) app window |
| Needs the other? | No: any OpenAI client can use it (VS Code agents, scripts, the OpenAI SDK) | No: it can use LM Studio, Ollama, a LAN box or a cloud API; the Phoenix server is just the built-in provider |

A VS Code extension (`vscode-ext\phoenixlai-2.7.0.vsix`) puts the client's chat in a VS Code side panel and adds
inline code completion from the server.

---

## Requirements

- Windows 10 or 11, x64, with a GPU whose driver provides Vulkan 1.2 or newer (AMD, NVIDIA, Intel). Without a usable
  GPU the server can run on the CPU (slowly).
- Windows PowerShell 5.1 (built in). PowerShell 7 is not used.
- Chrome or Microsoft Edge (for the client's window).
- Gemma 4 model files (`.gguf`) copied into `models\` by hand:
  - chat: `gemma-4-e2b_q4_0-it.gguf`, `gemma-4-e4b_q4_0-it.gguf`, `gemma-4-12b-q4_0-it-qat.gguf` (31B is supported but
    needs about 21 GB of VRAM);
  - pictures (optional): the matching `…-mmproj.gguf`;
  - docs search: `embeddinggemma-2-Q8_0.gguf`.

The dev machine is an AMD Radeon RX 6700 XT (12 GB); the target is an HP ZBook with an NVIDIA GPU and 32 GB RAM.

## Quick start

1. Put the model files in `models\`.
2. **First time only:** double-click **`Phoenix_LAI.bat`**. It creates `Phoenix_LAI_Server.lnk` and
   `Phoenix_Client.lnk` in this folder (no admin rights), then starts the server and the client. Use
   `Phoenix_LAI.bat -NoStart` to make the shortcuts only. After that, use the shortcuts.
3. Double-click **`Phoenix_LAI_Server.lnk`**. A Phoenix icon appears in the notification area. The first start
   checks and compiles the code (a few seconds). Nothing loads until a model is asked for.
4. Double-click **`Phoenix_Client.lnk`**. The chat window opens. Pick a model at the top and type.
5. To work on files, create a project with the **+** next to Projects (a folder in `%USERPROFILE%\Phoenix_Projects`)
   and click it.

The folder can live anywhere: the shortcuts find it from their own location.

The shortcuts are made on your computer and are not in git (`*.lnk` is in `.gitignore`), because a Windows shortcut
file records the computer's name and your Windows account ID. Every start through `Phoenix_LAI.bat`,
`src\run-phoenix-server.ps1` or `src\run-phoenix-client.ps1` re-creates a missing shortcut and repairs one that no
longer matches; if Windows Script Host is blocked, Phoenix still starts and only the shortcut is skipped. `Package.ps1`
leaves `.lnk` files out of packages: after unpacking, run `Phoenix_LAI.bat` once.

## The Phoenix server

- **OpenAI-compatible API** on `http://localhost:1893/v1` (the same paths as LM Studio, on its own port so both can run):
  `GET /v1/models`, `POST /v1/chat/completions` (streaming, `n`, `stop`, `seed`, JSON mode and schema, tool calling,
  image input, `logprobs`), `POST /v1/completions` (with `suffix` for code fill-in), `POST /v1/embeddings`.
- **Only models that fit** in the memory free right now are listed; a request for a model that does not fit, or an
  unknown name, gets `404 model_not_found`.
- **Fast:** every GPU kernel is generated as SPIR-V by the C# code itself and run through the driver's own Vulkan
  loader; batched and tiled prompt reading, KV-cache reuse across turns, prompt-lookup speculative decoding, Instant
  Recall (files read before come back from a saved cache).
- **Tray panel:** live GPU load, VRAM, KV cache, connected clients, model, uptime; Start/Stop, Restart, Console,
  Dashboard, Exit.
- **Console** commands: `help`, `status`, `clients`, `recent`, `models`, `load`, `unload`, `bench`, `apitest`, `vram`,
  `debug`, `log`, `gc`, `apikey new|set|off`, `restart`.
- **Ports** (FleetHacker port blocks, edge-ops `PORTS.md`): dashboard and console on the first free port in
  `127.0.0.1:8780-8789` (was 8765-8789), the Phoenix Client on `8790-8798`, the API on `1893`. Starting the server
  while one is already running shows a message and exits; an older running server is never stopped (close it from the
  tray, then start the new one). Both listeners answer `GET /__fleethacker/id`; `POST /__fleethacker/shutdown` (header
  `X-FleetHacker-Shutdown: 1`) exits as the tray's Exit does.
- **Optional API key** (off by default): `apikey new` in the console; stored in Windows Credential Manager.
- **GPU profile:** auto, AMD or NVIDIA (+ safe kernels), in `%LOCALAPPDATA%\LocalLLMStudio\gpu-profile.txt` or the
  `PHOENIX_GPU_PROFILE` / `PHOENIX_GPU_SAFE` environment variables.

## The Phoenix Client

### Providers
- The **Phoenix Local Server** is built in (`http://localhost:1893/v1`). Add others with the **+** under Providers:
  name, base URL, optional API key (saved in Windows Credential Manager, never in a file).
- Each provider shows its state live: **green** online, **red** offline, **blue** sending or receiving.
- **Test** checks 18 OpenAI features (streaming, system messages, sampling, JSON, tools, image input, embeddings…) and
  shows everything the provider reports. The client then never sends a feature the provider refused.
- The model menu at the top lists every provider's models; offline providers are greyed out.

### Working on files
- Open a project from the sidebar, or `/folder /name`. The model then lists, reads, searches, edits, writes and deletes
  files **inside that project only**, with plain text tags in its replies (any model that follows instructions can do
  it; no OpenAI tool calling needed).
- Every change keeps the previous version in `.llm-backup\` in the project. `/mode accept` asks before every action.
- The model **never runs commands**: asked to, it gives you the command to run yourself.
- Code in a reply is checked before you see it (C# 5, PowerShell 5.1 vs 7, SQL dialects, VBScript, VBA, CMD, DAX,
  Power Query, Qlik, Excel formulas, HTML, CSS); problems go back to the model to fix.

### Search and memory
- **Docs search:** the `docs\` library (16 syntax sheets: C#, PowerShell 5.1 and 7, Oracle SQL, SQL, DuckDB SQL,
  JavaScript, VBScript, Excel VBA, CMD, DAX, Power Query, Qlik script, Excel formulas, HTML, CSS; plus Gemma notes) is
  searched for each message;
  `/index` updates it after you add files.
- **Project index:** the open project is indexed live, so the model finds code without reading every file.
- **Skills:** reusable instructions (`SKILL.md` folders) in `skills\` or a project's `.phoenix\skills`; matched
  automatically or called with `/<skill-name>`; built with the Skill Builder.
- **RULES.md** in a project (and in the app folder) is added to every prompt.
- `/compact` summarises a long chat (automatic at 80% of the context).

### Privacy
**Your files, docs, RULES.md and skills are sent only to the Phoenix Local Server.** Every other provider (LAN or cloud)
gets only what you type, and cannot use the file tools. Each provider's window shows this as a fixed "send local
context" setting.

### Commands
Type `/` for the list, `/help` for full syntax: `/new`, `/model`, `/provider`, `/folder`, `/files`, `/image`, `/index`,
`/rag`, `/compact`, `/mode`, `/skills`, `/settings`, `/debug`, `/unload`.

## VS Code extension

Install `vscode-ext\phoenixlai-2.7.0.vsix` (Extensions → … → Install from VSIX). The **Phoenix LAI** view in the
activity bar shows the client's chat, connected to the open folder when it is inside `Phoenix_Projects`. Inline code
completion uses the server. Rebuild with `vscode-ext-source\build.ps1`.

## Security in short

- Two local listeners only, both on `127.0.0.1` with `Host` and `Origin` checks; the server never connects out; the
  client connects only to the providers you add (http/https, no redirects, TLS 1.2+).
- No third-party code, no DLLs in the repo, native calls only to `vulkan-1.dll` and `kernel32.dll`.
- The model never runs a program; it cannot create executable files or write into `.git`, `.vscode`, `.idea` or
  `.phoenix`; projects are jailed in `Phoenix_Projects` (no `..`, absolute paths or junctions).
- Every start runs `src\run.ps1`'s locality checks first and refuses to run if any rule is broken.

Full details: `.claude\docs\SECURITY.md` (development copy, not in the repo).

## Project layout

```
Phoenix_LAI.bat           first run: makes the two shortcuts below (not in git), then starts Phoenix
Phoenix_Client.lnk        start the client (made on this computer)
Phoenix_LAI_Server.lnk    start the server (made on this computer)
CLAUDE.md                 project rules for development
README.md                 this file
__version.info            the app version (yyyy_MMdd_HHmm stamp from Package.ps1)
VERSION                   the same version as X.Y.Z for the release-zip workflow: yyyy.[int]MMdd.[int]HHmm, leading zeros stripped (2026_0905_0930 -> 2026.905.930); bump both together
src\                      all C# code and the launcher scripts (run.ps1 checks and compiles at every start)
html\                     the chat, dashboard and console pages
models\                   GGUF model files (not in git)
docs\                     the docs library searched by the chat
skills\                   app skills
logs\                     server and client logs
nvidia\                   test kit for the NVIDIA target machine
vscode-ext-source\        VS Code extension source;  vscode-ext\  built packages
.claude\                  development only, not in the repo (.gitignore): tests, tools, reports, plans, docs
```

## Documentation

| Document | What it covers |
|---|---|
| `.claude\docs\ARCHITECTURE.md` (local, not in the repo) | From the shortcut to running code; how the server and the client work, file by file |
| `.claude\docs\SECURITY.md` (local, not in the repo) | Every security rule, where it is enforced, how it was tested, and its limits |
| `.claude\docs\CHANGELOG.md` (local, not in the repo) | Every change since 2026-10-15 |
| [CLAUDE.md](CLAUDE.md) | Development rules (hard rules, build, workflow) |
| [nvidia/NVIDIA.md](nvidia/NVIDIA.md) | Testing on the NVIDIA target machine |

## Development

- Code is C# 5 (the .NET Framework 4.8 compiler inside Windows PowerShell 5.1); no project file, no `dotnet`. Every run
  compiles `src\*.cs` in memory: `powershell.exe -NoProfile -STA -File src\run.ps1 -Role Client|Server`
  (`-CheckOnly` runs only the locality checks).
- Tests live in `.claude\claude_tests\`: phase gates (`gate.ps1 -Phase N`, the GPU output compared against llama.cpp
  references), kernel tests, benchmarks, CodeCheck and ShellGuard cases, and the Python QA suites
  (`openai-compat.py`, `client-qa.py`, `client-e2e.py`). llama.cpp is a development reference only, never a runtime
  dependency.
- Close Phoenix before model tests (`.claude\tools\close-phoenix.ps1`): one chat model per machine.

## Client lifecycle

The Phoenix Client owns its chat window: heartbeats and a leaving beacon stop only the client when its last app window closes. End session (or `/quit`) waits for active requests and indexing to finish. The independent tray/model server keeps running. Use `src/Stop-PhoenixClient.ps1` for a verified, graceful stop of clients from this checkout; `src/Stop-Phoenix.ps1` is a compatibility alias. No process is force-stopped. Launch the client with `-Stay` or set `PHOENIX_CLIENT_AUTO_STOP=0` to disable automatic stopping; `-Replace` requests a graceful stop before relaunch. The VS Code client retains its separate connection-based lifetime.

Development validation: `powershell.exe -NoProfile -File .claude/claude_tests/client-lifecycle-test.ps1` compiles the complete C# 5 source set and exercises the lifecycle policy and routes without loading a model.
