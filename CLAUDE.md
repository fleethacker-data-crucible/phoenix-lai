# CLAUDE.md

## Communication
- Keep responses brief. Lead with the result.
- Don't narrate tasks: no "Now I'll…", "Let me…", or step-by-step play-by-play. Just do the work, then report outcomes.
- No recaps of what the diff already shows. Mention only what failed, was skipped, or needs a decision.

## Project
Local LLM Studio: a C# (.NET 8, WinForms) app that runs Llama-family GGUF models on the GPU through Vulkan compute, with zero third-party dependencies. Full plan: `.claude/Local LLM Studio (C#) — Development Plan.md`.

## Hardware
- Dev machine: AMD Radeon RX 6700 XT (RDNA2, 12 GB VRAM), Vulkan 1.4 loader at `C:\Windows\System32\vulkan-1.dll`. Has run a 9B model.
- Target machine: 32 GB.

## Hard rules (never break)
- **No third-party code.** No NuGet `PackageReference`, `Reference`, or `NativeLibrary` entries. BCL only.
- **No network.** No `System.Net`, `HttpClient`, `Socket`, `WebRequest`, or `Process.Start`. No downloads, telemetry, or URL fields in the UI. Models load from local paths only.
- **P/Invoke allowlist:** `vulkan-1.dll` and `kernel32.dll` only. Never bundle or download Vulkan; use the driver-installed loader.
- **GPU kernels are SPIR-V** emitted by the in-repo C# `SpirvBuilder`, loaded via `vkCreateShaderModule`. No Vulkan SDK, glslc, or shader compiler DLLs. Baseline Vulkan 1.2 / SPIR-V 1.5; query optional features and fall back.
- No `.dll` files in the repo; no `Assembly.Load*` or `NativeLibrary`.

## Build
- **Production machine:** no installs, no SDK, nothing external; only PowerShell 5.1 + the driver's `vulkan-1.dll`. The studio must run there as-is.
- **Dev machine (this one):** dev/reference tools are allowed (e.g. llama.cpp to generate reference outputs, internet for specs). None of them may become a runtime dependency of the studio.
- **Windows PowerShell 5.1 only** (target machine has no PS7). `run.ps1` runs the locality checks, then compiles `src\*.cs` in memory via `Add-Type` with `CompilerParameters` (`/unsafe /optimize+`), then calls the entry point on an STA thread.
- Compiler is .NET Framework 4.8 CodeDom: **C# 5 only**. No `=>` members, `$""`, `?.`, `nameof`, tuples, `Span<T>`, `stackalloc` outside unsafe, `System.Numerics.Vector`, or `System.Runtime.Intrinsics`. CPU code is scalar + `Parallel.For` + unsafe pointers.
- Never compile or test under PowerShell 7 (`pwsh`). Always invoke `powershell.exe` (5.1) explicitly, e.g. `powershell.exe -NoProfile -STA -File run.ps1`, even when the shell tool itself is pwsh.
- No project file, no `dotnet`. Each run is a fresh `powershell -NoProfile -STA -File run.ps1` (Add-Type types can't reload).

## Modules
`Gguf` (memory-mapped parse) · `Tokenizer` (BPE/SentencePiece) · `Vulkan` (DllImport bindings) · `Kernels` (SPIR-V: dequant+matvec, RMSNorm, RoPE, softmax, attention, SiLU, add/mul) · `CpuRef` (scalar/parallel ground truth + fallback) · `Model` (forward pass, KV cache in VRAM) · `Sampler` · `Studio` (WinForms UI).

## Workflow
- Work phase by phase (see plan roadmap, phases 1–8). Don't start a phase until the previous gate test passes.
- `CpuRef` is ground truth; GPU output must match it (logits within 1e-2).
- Gates compare against llama.cpp outputs generated on the dev machine (saved under `dev\ref\outputs\`), plus self-checks (round-trips, GPU vs `CpuRef`).
- Use a 1B Q8_0 Llama model until phase 7. Llama-family only.
