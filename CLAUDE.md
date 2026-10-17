# CLAUDE.md

## Communication
- Keep responses brief. Lead with the result.
- Don't narrate tasks: no "Now I'll…", "Let me…", or step-by-step play-by-play. Just do the work, then report outcomes.
- No recaps of what the diff already shows. Mention only what failed, was skipped, or needs a decision.

## Project
PhoenixLAI (Local LLM Studio): C# (.NET Framework 4.8, compiled in memory by PowerShell 5.1) that runs GGUF models on the GPU through Vulkan compute, with zero third-party dependencies. UI: `html\chat.html` (+ `html\css`, `html\js`) served by the loopback server and shown in a Chrome (or Edge) app window. No menus: everything is a `/command` (`/help`). Full plan: `.claude/Local LLM Studio (C#) — Development Plan.md`.

## Hardware
- Dev machine: AMD Radeon RX 6700 XT (RDNA2, 12 GB VRAM), Vulkan 1.4 loader at `C:\Windows\System32\vulkan-1.dll`. Has run a 9B model.
- Target machine: 32 GB.

## Hard rules (never break)
- **No third-party code.** No NuGet `PackageReference`, `Reference`, or `NativeLibrary` entries. BCL only.
- **No network.** No `System.Net`, `HttpClient`, `Socket`, `WebRequest`, or `Process.Start`. No downloads, telemetry, or URL fields in the UI. Models load from local paths only.
- **Single exception (approved 2026-10-15): loopback chat server.** Only `src/WebServer.cs` may use `System.Net.HttpListener` (bound to `http://127.0.0.1` only, never outgoing) and `Process.Start` (only to open `chrome.exe --app` or `msedge.exe --app`: settings Auto (default: Chrome, else Edge), Chrome or Edge; approved 2026-10-15). It rejects foreign `Host`/`Origin` headers and serves only files inside `html\`. `run.ps1` enforces this; `html\` may not reference external URLs.
- **Model never runs a shell** (`src/ShellGuard.cs`, enforced by `run.ps1`): its only tools are list/read/write; command tags are refused; no executable files.
- **Project jail:** `/folder` only opens subfolders of `%USERPROFILE%\Phoenix_Projects` (created at startup, hard-coded), given as `/project/sub`; absolute paths, `..` and junctions are refused.
- **API server (approved 2026-10-16):** `src/WebServer.OpenAi.cs` serves the OpenAI-compatible API on `http://localhost:1234/v1` (LM Studio paths: `/v1/models`, `/v1/chat/completions`, `/v1/completions`, `/v1/embeddings`), loopback only, no process starts, no RAG. Tool calling (`src/ApiTools.cs`): request `tools` go into the prompt and `<tool_call>` replies come back as OpenAI `tool_calls`; the server never runs a tool (the client does). `/v1/completions` with `suffix` = code completion (FIM tokens if the model has them, else an instructed chat completion).
- **VS Code extension:** `vscode-ext-source\` (plain JS, VS Code API + Node `http` only, no npm, no settings) registers PhoenixLAI as a model in VS Code's native Chat panel (`vscode.lm.registerLanguageModelChatProvider`) plus inline completion, talking only to `127.0.0.1:1234`. `vscode-ext-sourceuild.ps1` packs `vscode-ext\phoenixlai-<ver>.vsix` (PS 5.1 zip, no vsce).
- **P/Invoke allowlist:** `vulkan-1.dll` and `kernel32.dll` only. Never bundle or download Vulkan; use the driver-installed loader.
- **GPU kernels are SPIR-V** emitted by the in-repo C# `SpirvBuilder`, loaded via `vkCreateShaderModule`. No Vulkan SDK, glslc, or shader compiler DLLs. Baseline Vulkan 1.2 / SPIR-V 1.5; query optional features and fall back.
- No `.dll` files in the repo; no `Assembly.Load*` or `NativeLibrary`.

## Build
- **Production machine:** no installs, no SDK, nothing external; only PowerShell 5.1 + the driver's `vulkan-1.dll`. The studio must run there as-is.
- **Dev machine (this one):** dev/reference tools are allowed (e.g. llama.cpp to generate reference outputs, internet for specs). None of them may become a runtime dependency of the studio.
- **Entry point:** `PhoenixLAI_App.lnk` (root) -> `conhost --headless` -> `src\run.ps1`, no windows; it finds the project from the shortcut's own path, so the folder can live anywhere. Recreate it with `.claude\tools\make-shortcut.ps1`. No VBS/HTA/JS launchers.
- **Windows PowerShell 5.1 only** (target machine has no PS7). `src\run.ps1` runs the locality checks, then compiles `src\*.cs` in memory via `Add-Type` with `CompilerParameters` (`/unsafe /optimize+`), then calls the entry point on an STA thread.
- Compiler is .NET Framework 4.8 CodeDom: **C# 5 only**. No `=>` members, `$""`, `?.`, `nameof`, tuples, `Span<T>`, `stackalloc` outside unsafe, `System.Numerics.Vector`, or `System.Runtime.Intrinsics`. CPU code is scalar + `Parallel.For` + unsafe pointers.
- Never compile or test under PowerShell 7 (`pwsh`). Always invoke `powershell.exe` (5.1) explicitly, e.g. `powershell.exe -NoProfile -STA -File src\run.ps1`, even when the shell tool itself is pwsh.
- No project file, no `dotnet`. Each run is a fresh `powershell -NoProfile -STA -File src\run.ps1` (Add-Type types can't reload).
- Root holds only `PhoenixLAI_App.lnk`, `CLAUDE.md`, `.gitignore` and folders (`src`, `html`, `models`, `logs`, `docs`, `vscode-ext-source`, `vscode-ext`). Studio scripts live in `src\`.

## Modules
`Gguf` (memory-mapped parse) · `Tokenizer` (BPE/SentencePiece) · `Vulkan` (DllImport bindings) · `Kernels` (SPIR-V: dequant+matvec, RMSNorm, RoPE, softmax, attention, SiLU, add/mul) · `CpuRef` (scalar/parallel ground truth + fallback) · `GemmaGpu` / `GemmaCpu` (Gemma 4 forward pass, KV cache in VRAM) · `Sampler` · `Engine` (Gemma 4 chat template, KV reuse) · `WebServer` + `Workspace` + `ChatLog` (loopback chat API, folder tools, session logs) · `Rag` (docs\ library: `/index` builds the incremental BM25 index `docs\.index\rag.bin`, memory-mapped; top chunks go into the newest user message) · `html\chat.html` (UI).

## Workflow
- Work phase by phase (see plan roadmap, phases 1–8). Don't start a phase until the previous gate test passes.
- `CpuRef` is ground truth for each op (kernel tests); end to end, the GPU must match llama.cpp's logprobs as closely as `CpuRef` does.
- Tests, benchmarks and llama.cpp reference outputs live in `.claude\claude_tests\` (gates 2–9: `powershell.exe -NoProfile -STA -File .claude\claude_tests\gate.ps1 -Phase N`, default model `models\gemma-4-12b-it-qat-q4_0.gguf`; references in `ref\outputs\<model>\`, regenerated with `ref\make-refs.ps1` and `ref\make-gemma-logprobs.ps1`; prefill speed: `pfbench.ps1`), plus self-checks (round-trips, GPU vs `CpuRef`).
- **Gemma 4 only** (chat: `gemma-4-12b-it-qat-q4_0.gguf`; docs search: `embeddinggemma-2-Q8_0.gguf`). Llama / Mistral support was removed 2026-10-17; other architectures are refused at load.
