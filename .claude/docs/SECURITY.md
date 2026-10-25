# Phoenix LAI: security rules report

State as of commit `ead94b6` (2026-10-21). For each rule: what it says, where the code enforces it, how it was tested,
and what it does not cover. Rules marked **(hard rule)** are in `CLAUDE.md` and may only change with the owner's
explicit approval; each approved exception carries its date.

## 0. Threat model in one paragraph

Phoenix runs on a locked-down Windows machine (no admin rights, no installs, Windows PowerShell 5.1, the GPU driver's
`vulkan-1.dll`). The things it protects against: **a model** (local or cloud) that is wrong, confused or steered by text
it reads (prompt injection), **web pages** in a browser on the same PC (cross-site requests, DNS rebinding), **code
drift** (a future change quietly adding networking, processes or native code), and **leaks** of the user's files and
keys to providers that should not have them. It does not try to protect against other programs already running as the
same Windows user: those can read the same files and keys anyway.

---

## 1. Supply chain and build **(hard rules)**

| Rule | Enforcement | Notes |
|---|---|---|
| No third-party code: BCL only; no NuGet, `Reference`, `NativeLibrary` | `run.ps1` compiles with a fixed reference list (`System`, `System.Core`, `System.Drawing`, `System.Windows.Forms`); source scan bans `#r` and `extern alias` | |
| No `.dll` / `.exe` in the repo (outside `models\`, `.git`, `.claude`, `dev`) | `run.ps1` check 1 | |
| No `Assembly.Load*`, `NativeLibrary`, `LoadLibrary` | `run.ps1` banned-pattern scan | |
| P/Invoke only to `vulkan-1.dll` and `kernel32.dll`, always a literal DLL name | `run.ps1` checks 3 and 3b | `CredStore` reaches the Windows vault by WinRT reflection, not P/Invoke |
| GPU code is SPIR-V generated in C# (`SpirvBuilder`); no Vulkan SDK, shader compiler or bundled loader | Design; no such files exist | The driver's own `vulkan-1.dll` is used |
| C# 5 / .NET Framework 4.8 compiler only; never PowerShell 7 | `run.ps1` exits under PS 7 | |
| Production machine: nothing installed, nothing downloaded | Design | Models are copied in by hand |

## 2. `run.ps1` locality checks (the gate before every start)

Every start (server or client) runs `Test-Locality`; any failure stops the program with a red list. `-CheckOnly` runs
it alone.

1. No binaries in the repo.
2. Banned in all source except the noted files: `System.Net`, `HttpClient`, `WebClient`, `WebRequest`, `Socket`,
   `Process.Start`, `Assembly.Load`, `NativeLibrary`, `LoadLibrary`, `http://`, `https://`.
   - Exceptions: `WebServer.cs`, `WebServer.OpenAi.cs` and `Providers.cs` may use `System.Net`, `Process.Start` and
     `http://`, but `HttpClient`, `WebClient`, `Socket`, `Dns.`, `TcpClient`, `TcpListener`, `UdpClient` and
     `https://` stay banned even there. Every `http://` literal must be `127.0.0.1` or `localhost`.
   - `Providers.cs` may contain `WebRequest` only in the one guarded creation (rule 4c) and may not start a process.
   - `WebServer.OpenAi.cs` may not start a process at all.
2b. **No source except `ShellGuard.cs` may name a shell** (`powershell`, `pwsh`, `cmd.exe`, `conhost`, `wscript`,
    `cscript`, `mshta`, `rundll32`, `Management.Automation`, `ShellExecute`, `CreateProcess`, `WinExec`,
    `Interaction.Shell`). This is why code comments say "PS" instead of the shell's name.
3. DllImport allowlist and literal names (§1).
4. No assembly references in source.
4b. `WebServer.cs` contains **exactly one** `Process.Start(`, immediately after `ShellGuard.CheckLaunch(…);`.
4c. `Providers.cs` creates requests only via `WebRequest.Create(CheckTarget(…))`, exactly once, and still contains
    `AllowAutoRedirect = false`, the http/https scheme check, the `UserInfo` check and `SecurityProtocolType.Tls12`.
    **No other file may make an outgoing request** (`WebRequest.Create`, `GetRequestStream`).
4d. The run guard is intact: `ShellGuard.cs` exists and still contains `CheckRunRequest`, the persistent lockout
    (`LoadLockout`, `SaveLockout`, HMAC signing, `SuspendMinutes = 5`); `WebServer.cs` calls
    `ShellGuard.CheckRunRequest(lastUser, …)` inside `Chat()` **before** the model generates; `Workspace.cs` uses
    `ShellGuard.RunAdvice`, `ShellGuard.CheckWrite(f)` and `ShellGuard.Refusal`.
5. No file in `html\` references an external URL (`//host.tld`).

Limit: these are text scans. They stop accidental drift and make deliberate removal obvious; they are not a proof that
no code path does something equivalent by other means (for example reflection). Code review covers that.

## 3. The model never runs anything **(hard rule)**

| Layer | Enforcement (`src\ShellGuard.cs`, `src\Workspace.cs`) |
|---|---|
| Tools | The model's only tools are `list`, `read`, `search`, `edit`, `write`, `delete`. There is no run tool. Command-style tags (`<run>`, `<exec>`, `<shell>`, `<cmd>`, `<powershell>`, `<bash>`, `<terminal>`, `<script>`, `<process>`, …) outside file contents and code blocks are reported back as refused. |
| Advice | The tool instructions tell the model to give the command for the user to run, in a ```powershell block. |
| Executable files | `CheckWrite` refuses creating files Windows runs on double-click: `.exe .com .scr .pif .cpl .msi .msp .msc .dll .sys .drv .ocx .bat .cmd .psc1 .vbs .vbe .jse .wsf .wsh .wsc .hta .lnk .url .scf .reg .inf .application .appref-ms .jar .gadget .settingcontent-ms` (trailing dots and spaces trimmed). Source files such as `.ps1`, `.js`, `.py` **are** writable: opening them edits them, and nothing in Phoenix runs them. |
| Indirect execution | Writes into `.git\` (hooks run on commit), `.vscode\` (tasks can run on folder open), `.idea\`, `.phoenix\` (recall snapshots, project skills), and `desktop.ini`, `.gitattributes`, `.gitmodules` are refused. |
| Alternate data streams | A file name containing `:` is refused for writes. |
| Run guard | Asking the model to run something (any wording, or a message that is just a command line; "how do I run…" never counts) is counted. The 3rd within an hour gets a red WARNING banner; the 6th stops all chat input for 5 minutes. The lockout survives restarts: two HMAC-SHA256-signed files (`%LOCALAPPDATA%\LocalLLMStudio\guard.dat`, `%APPDATA%\PhoenixLAI\guard.dat`), key from machine name + user SID; an edited copy starts a fresh lockout, a deleted copy is restored from the other, setting the clock back does not shorten it. No registry. |
| The one process launch | `ShellGuard.CheckLaunch` allows only `chrome.exe` / `msedge.exe`, only with `--app=` on `://127.0.0.1:`, and no shell metacharacters (`& | < > ^ %` or newlines) in the arguments. |
| Console | The server console runs Phoenix's own commands only. |
| API | The server describes `tools` to the model and returns `tool_calls`; it never runs a tool. |

Tests: `.claude\claude_tests\shellguard-cases.ps1` (run-request wording), the 2026-10-16 audit (escape, absolute path,
executable and shell tags all blocked), `client-qa.py` (run guard applies to provider chats).

## 4. Local servers: loopback only **(hard rule, approved 2026-10-15 / 2026-10-16 / 2026-10-20)**

| Listener | Bound to | Checks |
|---|---|---|
| Client page server (8790-8798) | `http://127.0.0.1:<port>/` | `Host` must be exactly `127.0.0.1:<port>` (blocks DNS rebinding); a sent `Origin` must be the same address (blocks other sites' scripts); `HEAD` answers status only; files served only from inside `html\` (`ServeFile` resolves the path and requires it to start with `html\`). |
| Server dashboard (8780-8789) | `http://127.0.0.1:<port>/` | Same checks. Serves only the dashboard, the console and their `/api` calls; client and chat calls get 404. |
| OpenAI API (1234) | `http://127.0.0.1:1234/` and `http://localhost:1234/` | `Host` must be `127.0.0.1:1234` or `localhost:1234`; `Origin`, if sent, must be the API's own address or a `vscode-webview://` / `vscode-file://` page; **`Origin: null` is refused** (downloaded HTML files and sandboxed frames send it); optional API key (`Authorization: Bearer`, console `apikey new|set|off`, stored in Credential Manager `PhoenixLAI.Server`, off by default because the VS Code extension sends none, never logged). |

`Process.Start` is used once, for the client's app window (§3). There is no listener on any other address, and the
server makes no outgoing connection.

Tests: `client-qa.py` (foreign `Host` / `Origin` → 403, only `html\` served), `openai-compat.py` (32 checks with the
OpenAI SDK), API key tests (no key / wrong key → 401 `invalid_api_key`).

Limit: any program running as the same user can call these loopback ports with the right `Host` header. The client's
`/api` has no token: such a program could drive the client's chat and file tools, or read files of the open project
through `/api/cat` (still inside the project jail). It could already read those files directly, so this is not an escalation, but it is the trust boundary.

## 5. Outgoing requests: providers **(approved 2026-10-20)**

Only the client makes outgoing requests, and only to OpenAI `/v1` providers the user added.

| Rule | Enforcement (`src\Providers.cs`) |
|---|---|
| One way out | `Providers.Outgoing(url)` is the only request creation (`run.ps1` 4c). |
| URL shape | `CheckTarget`: absolute http/https URL with a host; no `user:password@` in it ("put the key in the key field"). |
| No forwarding | `AllowAutoRedirect = false`: a redirect is reported, never followed, so a key cannot be carried to another host. |
| TLS | TLS 1.2 (and 1.3 where Windows has it); never SSL3 / TLS 1.0. Certificates are checked by Windows. |
| Proxy | Loopback goes direct; other hosts use the Windows system proxy. |
| Keys | Stored in Windows Credential Manager (`PhoenixLAI.Connections`, user name = provider id) via the WinRT `PasswordVault`; never in `providers.json`, never sent back to the page (the field is cleared on save), sent only as `Authorization: Bearer` to their own provider. Looked up once and cached in memory; forgotten when changed or deleted. |
| Built-in provider | The Phoenix Local Server's address is fixed (`http://localhost:1234/v1`) and cannot be changed or deleted. |

Tests: `client-qa.py` (keys never in files or page; refused targets: `example.com`, private IPs typed as hosts in
paths, `//evil.com` paths, `@` paths, `127.0.0.1.nip.io`).

Limit: a provider URL may be plain `http://` on the LAN or internet. Its key and messages then travel unencrypted.
Nothing refuses or flags this today; the Providers list only tags providers outside this PC and the LAN as "cloud".

## 6. Local context goes only to allowed providers **(approved 2026-10-21)**

"Local context" = docs-search sections, syntax-sheet cards, RULES.md (app and project), project-index matches, files the
model reads, file-tool instructions and results, and skills.

| Rule | Enforcement |
|---|---|
| Only a provider with `Provider.SendsLocalContext` gets it; today that is **only the Phoenix Local Server** | `WebServer.LocalContextAllowed`; `PrepareMessages` returns early for other providers with only the system prompt, what the user typed, and a "no file access" note |
| No file tools for other providers | `Chat()` passes no workspace (`ws = null`) when local context is not allowed; tags in the reply are never executed (the tool loop needs a workspace) |
| Side paths | The code check's extra reference sections and the Skill Builder's docs sections are skipped for other providers |
| UI | Each provider's window shows a greyed-out "send local context" toggle (on for Phoenix, off for the rest) |

Test (2026-10-21): a recording relay posing as a provider received, for a request that asked to read `notes.txt`
and a PowerShell question with a project open: 2 messages, 874 characters, no RULES.md text, no file content, no docs
sections, no tool instructions, no project matches, no skills. The same request to the Phoenix Local Server carried
RULES.md, 4 docs sections, the project match and a read-tool call.

## 7. Project jail: what the model's file tools can reach **(hard rule)**

| Rule | Enforcement (`src\Workspace.cs`) |
|---|---|
| Projects only under `%USERPROFILE%\Phoenix_Projects` (hard-coded, created at start) | `new Workspace(path)`: the path is given as `/project/sub`; anything with `:`, starting with `//` or `~`, or containing `.`, `..` or empty parts is refused; the result must be a subfolder of the root (not the root itself) and must exist |
| No links on the way in | Every folder from the root down to the project is checked for a reparse point (junction / symlink) |
| Every tool path stays inside | `Resolve(rel)`: rooted paths refused; `GetFullPath(Combine(root, rel))` must equal the root or start with `root\` (case-insensitive); every existing component from the root to the target is checked for a reparse point |
| Size caps | Read 200 KB; write 2 MB; list 300 entries; search 60 hits |
| Backups | Edit, write and delete copy the previous version to `.llm-backup\<time>\` first; delete is a move there, never an erase; `.llm-backup` is read-only to the model and left out of listings |
| Approvals | `/mode accept`: every list, read, search, edit, write and delete waits for Accept or Deny; a closed window or Stop counts as Deny |
| Master switch | `/folder off` removes all file tools until turned on |
| Write rules | §3 (`CheckWrite`) |

Tests: 2026-10-16 audit (escape, absolute path, junctions blocked), `client-qa.py`.

Limits (not done; relevant to the agent plan's §5A): no check for 8.3 short names, device or reserved names (`CON`,
`NUL`…) or trailing dots in tool paths beyond `GetFullPath`; no re-check of the opened handle's final path (a
check-then-open race with a junction swapped in between is not covered); no automated test suite for the jail.
Reads of `.git` and other dot folders are allowed (only writes are refused).

## 8. Data at rest and logs

| Data | Where | Protection |
|---|---|---|
| Provider keys, server API key | Windows Credential Manager | Per-user, encrypted by Windows |
| Providers, settings, lockout | `%LOCALAPPDATA%\LocalLLMStudio`, `%APPDATA%\PhoenixLAI` | User profile; no secrets in them (lockout files are signed, not secret) |
| Chats, project index, recall snapshots | `<project>\.phoenix`, `%USERPROFILE%\.phoenix\chats` | User profile; the model cannot write `.phoenix` |
| Logs | `logs\` in the app folder | Request summaries; with `/debug on`, full prompts and replies (which can contain file contents) |
| Browser profile | `%LOCALAPPDATA%\LocalLLMStudio\<browser>-profile` | Separate from the user's own browser profile |

Debug mode should be off when working on sensitive files, because it writes them into `logs\`.

## 9. Prompt injection

- File and docs content are data. The model can be steered by text it reads, but everything it can then do is bounded
  by §3 and §7: it can change or delete files **inside the open project** (with backups, and with approvals in accept
  mode), and it can never run anything, reach the network, or leave the jail.
- Only the user changes modes, folders and settings; tool results cannot.
- Cloud providers get no local context (§6), so injected text in files cannot reach them through Phoenix.

## 10. Proposed and not approved

| Proposal | Status |
|---|---|
| A `run` tool for a coding agent, in an AppContainer + Job Object sandbox (no admin needed; on the production machine only PowerShell and built-in tools exist) | Assessed 2026-10-21; would need two rule changes (the model runs commands; the model writes scripts that run) and a probe on the target machine. **Not approved.** |
| Per-provider "send local context" switch for cloud providers | Assessed 2026-10-21. **Not approved**: the toggle is shown but fixed. |
| Agent-plan §5A jail hardening (handle re-check, reserved names, test suite) | Recommended; not started. |
