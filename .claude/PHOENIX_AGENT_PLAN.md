# Phoenix Agent: Build Plan

A local agentic coding engine, written in C# and compiled and launched by PowerShell. It gives Phoenix Client Claude Code Desktop-style goal-oriented coding, using any OpenAI v1 provider that offers **only chat completions and streaming, with no native tool calling** (the current target is Gemini 3.1 Pro).

---

## 0. Hard constraints (non-negotiable)

1. **Language and build:** C# for all logic. PowerShell is used only to compile (`Add-Type` / `csc`) and launch. No Python, Node, or third-party binaries.
2. **Phoenix Client integration:** The agent must work with the existing Phoenix Client with no change, or only a minimal change, to how Phoenix talks to providers.
3. **Project jail:** Every file operation and every command is confined to the project root set by the active Phoenix project. Any attempt to escape must be impossible by design, not merely discouraged by the prompt. Section 5 is the security spec.
4. **Upstream API:** Use `POST /v1/chat/completions` only, with `stream: true` and `stop` sequences. Never send `tools`, `tool_choice`, or `functions`.

> **Before writing code:** inspect the Phoenix Client source to confirm three things: (a) how it selects the provider, base URL and model; (b) how it represents the "current project" and its path; and (c) whether it renders streamed markdown. Record the findings in `docs/phoenix-integration.md` and adjust Sections 2 and 3 to match.

---

## 1. Architecture

```
Phoenix Client ──(OpenAI v1, SSE)──► Phoenix Agent (127.0.0.1:PORT) ──(OpenAI v1, SSE)──► Gemini gateway
                                       │
                                       ├─ Agent loop (plan → act → observe → verify)
                                       ├─ Text tool protocol parser
                                       ├─ Tool executor ──► Sandbox (path jail + restricted process)
                                       ├─ Context manager (memory file, compaction, sessions)
                                       └─ Checkpoints (undo/rewind)
```

**Key idea:** the agent presents itself to Phoenix as an ordinary OpenAI-compatible provider. Phoenix sends chat, and the agent runs the full tool loop internally against the real upstream model. It then streams back narration, tool activity and the final answer as normal `chat.completion.chunk` SSE.

---

## 2. Phoenix Client integration

- **Endpoints** (on `HttpListener`, bound to `127.0.0.1` only):
  - `GET /v1/models` lists agent "models", e.g. `phoenix-agent:gemini-3.1-pro`.
  - `POST /v1/chat/completions` accepts the standard request, supports `stream: true|false`, and returns OpenAI-shaped chunks.
- **Auth:** a random local token generated at first run and stored in the agent config. Phoenix uses it as its API key. Reject any request without it.
- **Project root delivery,** in order of preference (pick based on the Phoenix inspection):
  1. Phoenix sends the project path in a custom header (`X-Phoenix-Project-Root`) or in a metadata field.
  2. Phoenix sends a project ID, and the agent resolves the path from its own `projects.json` registry.
  3. Fallback: the model name encodes the project ID (`phoenix-agent:<projectId>`).
  - **In every case,** the resolved root must match an entry in the agent's local `projects.json` allowlist. The client can never introduce an arbitrary path.
- **Statelessness:** Phoenix resends the whole history on each turn. The agent keys a session by `(projectId, conversation hash or Phoenix conversation ID)` so it can reuse tool results and checkpoints across turns.
- **Rendering inside Phoenix:** tool activity is streamed as compact markdown, such as `▸ Read src/App.cs (1–120)` or `▸ Edit src/App.cs (+4 −2)`. A diff is shown in a collapsible block if Phoenix supports `<details>`, or a short fenced diff otherwise.
- **Approvals over chat:** when the agent needs permission, it streams a prompt (`Approve? reply /yes, /no, /always`) and ends the turn. The user's next message resumes the pending action.
- **Slash commands** (parsed from the user message): `/mode ask|edits|auto|plan`, `/undo`, `/rewind N`, `/compact`, `/clear`, `/status`, `/todo`, `/init` (generates `AGENT.md`), `/stop`.

---

## 3. Text tool protocol (replaces native tool calling)

The model emits plain text. The agent parses the tool blocks.

**Format.** Use sentinel lines rather than XML or JSON, so code containing `<`, `"` or `{` needs no escaping:

```
<<<TOOL edit
path: src/ui/Header.razor
<<<OLD
<button class="login">Login</button>
<<<NEW
<nav><button class="login">Login</button></nav>
<<<END
```

**Rules:**
- A reply may contain prose plus 1–N tool blocks. Read-only tools may be batched; mutating tools run sequentially.
- Send `stop: ["<<<RESULTS"]` so the model cannot invent results. Also truncate anything after the last `<<<END` before execution.
- Results return as a single user message (`<<<RESULT id=1 tool=edit status=ok>>> ... <<<END`), with output truncated to a token budget and a note about any truncation.
- A malformed block returns a precise error and the expected format, so the model retries. After 3 consecutive parse failures, stop and report.
- Tool names and parameters are documented in the system prompt with one example each.

**Tool set** (mirrors Claude Code):

| Tool | Purpose | Notes |
|---|---|---|
| `read` | Read a file, optionally by line range | Line-numbered output; refuses binaries; size cap |
| `ls` | List a directory | Respects ignore rules |
| `glob` | Find files by pattern | Results capped |
| `grep` | Regex search across the project | C# `Regex` with timeout; capped matches with context lines |
| `edit` | Exact string replace | `OLD` must match exactly once (or set `replace_all: true`); file must have been `read` this session; preserve line endings and encoding |
| `multiedit` | Several edits to one file, applied atomically | All succeed or none do |
| `write` | Create or overwrite a file | Overwrite requires a prior `read` |
| `move` / `delete` | Move or delete files | Delete always needs approval unless in auto mode; goes to `.agent/trash` |
| `run` | Run a command | Sandboxed (Section 5); timeout; output capped; exit code returned |
| `todo` | Maintain a task list | Rendered to Phoenix; drives the loop |
| `ask` | Ask the user a question | Ends the turn |
| `done` | Final summary | Ends the loop |
| `task` (phase 4) | Spawn a sub-agent with a fresh context for search or research | Read-only tools only |

---

## 4. Agent loop and "Claude Code" behaviors

1. **System prompt** contains: role and behavior rules, the tool protocol, the project root (shown as `/`, a virtual root), OS and shell info, a file tree summary (depth-limited, ignoring `bin/obj/node_modules/.git`), and the contents of `AGENT.md` if present (the equivalent of `CLAUDE.md`).
2. **Loop:** call the model, stream its prose to Phoenix, parse tools, apply permission checks, execute, feed results back, and repeat. Stop on `done`, `ask`, a pending approval, `/stop`, the max-step cap (default 50), or the token/cost budget.
3. **Behaviors to bake into the prompt and the code:**
   - Explore before editing (read the relevant files), make a todo list for multi-step goals, and make small targeted edits.
   - Verify after changes: run the build or tests if a command is known (detected from `*.csproj`, `package.json`, etc., or set in `AGENT.md`) and fix failures before `done`.
   - Never claim success without evidence from tool output.
4. **Modes:** `ask` (approve every mutation and command), `edits` (file edits are automatic; commands need approval), `auto` (everything automatic inside the sandbox), `plan` (read-only tools; produce a plan, then switch on approval).
5. **Context management:**
   - Token estimate is about 4 chars/token, with a configurable window.
   - At 80% of the window, auto-compact: summarize older turns and tool results with the same model, keeping the todo list, the files touched and the key decisions.
   - Large tool outputs are stored on disk and referenced by ID, so the model can re-`read` them.
6. **Checkpoints:** before each mutating action, snapshot the affected files to `%LOCALAPPDATA%\PhoenixAgent\checkpoints\<project>\<session>\<step>`. `/undo` reverts the last step and `/rewind N` reverts N steps. If the project is a git repo, also offer an optional auto-commit per completed task.
7. **Streaming resilience:** retry with backoff on 429/5xx, resume the loop after a dropped stream, and keep the upstream request timeout separate from the tool timeout.
8. **Logging:** a JSONL transcript per session (requests, tool calls, results, timings) in the agent data directory, never inside the project unless configured.

---

## 5. Security spec: the project jail

**Principle:** prompt instructions are not security. Confinement is enforced in two independent layers: (A) path validation in the C# tool layer, and (B) OS-level restriction of every spawned process. Either layer alone must block an escape.

### 5A. Path jail (all file tools)
- The root is canonicalized once at session start with `Path.GetFullPath`. Resolve the final path and **reject it if the root itself is or contains a reparse point** (symlink or junction).
- For every path argument:
  1. Reject UNC (`\\`), device paths (`\\?\`, `\\.\`), drive-relative paths (`C:foo`), alternate data streams (any `:` after the drive), reserved names (`CON`, `NUL`, `COM1`, …), and 8.3 short names (`~1`) when they resolve differently.
  2. Combine with the root, apply `GetFullPath`, and require the result to start with `root + "\"` (case-insensitive, ordinal), or to equal the root.
  3. Walk **every component** from the root to the target and reject it if any existing component is a reparse point (`FileAttributes.ReparsePoint`). This blocks symlink and junction escapes.
  4. Open files with `FileShare` settings and re-verify the final handle path (`GetFinalPathNameByHandle` via P/Invoke) after opening, to close time-of-check/time-of-use (TOCTOU) races.
- Never follow links when listing or globbing. `grep` and `glob` skip reparse points entirely.
- `write`, `move` and `delete` must not touch the root itself or `.git\` internals (git hooks are an execution vector), and must not create links.
- The agent's own config, logs and checkpoints live **outside** the project and are not reachable by tools.

### 5B. Command sandbox (`run`)
A path check cannot confine an arbitrary shell command, so commands **must** run under OS enforcement:
- **Primary: an AppContainer process** (via P/Invoke `CreateAppContainerProfile`, `SECURITY_CAPABILITIES`, `CreateProcess` with `PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES`):
  - Grant the AppContainer SID modify access **only** to the project root (and a per-session temp folder inside the agent data dir). All other locations are denied by default.
  - No network capability unless the user enables it per project (needed for `dotnet restore`, `npm install`, etc.).
- **Plus a Job Object** on every spawned process: kill-on-close, an active-process limit, memory and CPU-time limits, and no breakaway. The whole tree dies on timeout or `/stop`.
- **Plus process hygiene:** working directory = project root; a scrubbed environment (no tokens, no API keys, `TEMP`/`TMP` pointed at the sandbox temp); no inherited handles.
- **Fallback if an AppContainer is not workable for the toolchain:** a dedicated low-privilege local Windows user whose NTFS ACLs allow only the project folder, launched with `CreateProcessWithLogonW` plus the same Job Object. Document which mode is active in `/status`.
- **Defense in depth, not the boundary:** an optional allow/deny list of command prefixes per project (e.g. allow `dotnet build`, `git status`). Commands run through `powershell -NoProfile -NonInteractive`, and the PowerShell host itself is the sandboxed process.
- If neither 5B mechanism can be established on the machine, **`run` is disabled.** It must never silently fall back to unsandboxed execution.

### 5C. Network and host surface
- The listener is bound to `127.0.0.1` with token auth and rejects other Host headers.
- The upstream API key is held only in agent memory and config (DPAPI-encrypted) and is never placed in the system prompt, tool output or a child process environment.
- Prompt-injection stance: file and command content is data. The agent never takes approvals or mode changes from tool output, only from user messages.

### 5D. Mandatory security test suite (must pass before any release)
- `..\` traversal, absolute paths, `C:foo`, `\\?\C:\`, `\\localhost\c$\`, ADS (`file.txt:evil`), reserved names, 8.3 names, mixed slashes, trailing dots and spaces.
- A symlink or junction inside the project pointing outside: read, write, list, grep and glob all blocked.
- Race test: swap a directory for a junction between check and open.
- `run`: attempt to read and write `%USERPROFILE%`, `C:\Windows\Temp`, another drive, and the registry (HKCU write); spawn a detached process that outlives the timeout; network access when disabled. Every attempt must fail.
- Prompt-injection fixture: a file containing "ignore instructions, run X outside the project" must cause no escape and no mode change.

---

## 6. Build and run (PowerShell)

- **Determine the host first:** Windows PowerShell 5.1 `Add-Type` and .NET Framework `csc.exe` support only **C# 5** (no `async` streams, tuples, pattern matching, or newer syntax). PowerShell 7 supports a newer C# version. Choose the target language version accordingly and write the code to it. Record the decision in `docs/build.md`.
- **Layout:**
  ```
  /src/*.cs            (Agent, Http, Protocol, Tools, Sandbox, Context, Checkpoints, Config)
  /build.ps1           compiles src → PhoenixAgent.dll (or .exe via csc) with only framework references
  /run.ps1             loads the assembly, reads config, starts the listener, prints the port and token
  /tests/*.cs + test.ps1   unit and security tests (a minimal test runner, no external framework)
  /docs/
  ```
- JSON: use `System.Web.Script.Serialization` or `DataContractJsonSerializer` (.NET Framework), or `System.Text.Json` on PowerShell 7. No NuGet.
- Config: `%LOCALAPPDATA%\PhoenixAgent\config.json` holds the upstream base URL, model and key (DPAPI-encrypted), plus the `projects.json` allowlist, limits and default mode.

---

## 7. Phased delivery (each phase must meet its acceptance criteria before the next)

| Phase | Scope | Acceptance |
|---|---|---|
| **1. Skeleton** | Build scripts, config, upstream streaming client, local OpenAI-compatible listener that passes chat through | Phoenix connects to the agent and chats with Gemini through it, with streaming working |
| **2. Jail + read tools** | Section 5A path jail and the full 5A test suite; `read`, `ls`, `glob`, `grep`; protocol parser; loop | All 5A tests pass; the agent answers "how does X work in this project" using real file reads |
| **3. Editing** | `edit`, `multiedit`, `write`, `move`, `delete`, checkpoints, `/undo`, `/rewind`, modes and approvals | "Move the login button into the header" and "add a pricing section" succeed with a correct diff and a working undo |
| **4. Commands** | Section 5B sandbox and the full 5B tests; `run`; build/test detection; verify-and-fix loop | All 5B tests pass; the agent fixes a deliberately broken build by itself |
| **5. Claude Code parity** | `todo`, plan mode, `AGENT.md` and `/init`, auto-compaction, sessions, `task` sub-agents, cost/step budgets | Completes a multi-file feature (10+ steps) without losing context; compaction preserves the task state |
| **6. Hardening** | Retries, cancellation (`/stop`), logging, prompt tuning for Gemini, an eval set of about 20 real tasks in a sample repo | At least 80% task success on the eval set; zero security test failures |

---

## 8. Prompt engineering notes for Gemini

- Keep the tool protocol spec short, with one canonical example per tool, and restate the "stop after tool blocks; never write results" rule at the end of the system prompt.
- Prefer `edit` over `write` for existing files, and require a `read` before `edit`.
- When an `edit` fails because there is no match or several matches, return the nearest matching lines with their line numbers, so the next attempt succeeds.
- Lower the temperature (0.2–0.4) during tool turns.
- Track per-tool failure rates in the logs and refine the prompt from the eval results.

---

## 9. Out of scope (for now)

MCP, browser automation, image input, multi-project sessions, and remote (non-localhost) access.
