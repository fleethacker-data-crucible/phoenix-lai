# Local LLM Studio (C#) — Development Plan

Oct 15, 2026 · @Christopher Staggs

## The rule

The studio is buildable under the rule, with one unavoidable exception: the GPU can only be reached through the Vulkan loader that the display driver already installs.

- **No third-party DLLs.** No NuGet packages, no ILGPU, Silk.NET, Vortice, LLamaSharp, ONNX Runtime, or bundled native libraries. Code uses only the .NET base class library.
- **No network.** No `System.Net` usage, no telemetry, no model downloads. GGUF files are copied in by hand.
- **Driver exception.** `vulkan-1.dll` (the Khronos loader) ships with AMD, NVIDIA and Intel display drivers in `System32`. The studio calls it via `DllImport`; nothing is added to the machine. If even this is off-limits, the studio is CPU-only.
- **Allowed system calls.** `vulkan-1.dll` (GPU) and `kernel32.dll` (optional memory/affinity tweaks). Nothing else.

## Architecture

GPU kernels are compute shaders in SPIR-V, emitted as binary by a C# emitter and compiled by the driver itself (`vkCreateShaderModule`), so no Vulkan SDK, glslc, or shader compiler DLL is needed.

| Module | Responsibility | Built from |
| --- | --- | --- |
| `Gguf` | Parse header, metadata, tensor table; map weights | `System.IO.MemoryMappedFiles` |
| `Tokenizer` | BPE / SentencePiece from GGUF metadata | BCL only |
| `Vulkan` | \~60-80 `DllImport` signatures: instance, device, queues, buffers/memory, descriptor sets, compute pipelines, command buffers, fences | `vulkan-1.dll` |
| `Kernels` | SPIR-V compute shaders: dequant+matvec (Q8\_0, Q4\_K, Q6\_K, F16), RMSNorm, RoPE, softmax, attention, SiLU, add/mul | SPIR-V binary via `SpirvBuilder` |
| `CpuRef` | Same ops on CPU, scalar + `Parallel.For`; ground truth and fallback | BCL only |
| `Model` | Llama-family forward pass, KV cache in VRAM | `Vulkan` + `Kernels` |
| `Sampler` | Temperature, top-k, top-p, repetition penalty | BCL only |
| `Studio` | Chat UI: model picker, settings, streaming output, tokens/sec | WinForms (.NET Framework 4.8) |

SPIR-V is binary, so a small C# emitter (`SpirvBuilder`) is built first; kernels are written against it. Start with a few simple shaders, then add helpers once patterns repeat.

## Build through PowerShell

No .NET SDK, no installs. Windows PowerShell 5.1 compiles the C# source in memory with `Add-Type` (.NET Framework 4.8 CodeDom compiler, **C# 5**) and runs it in-process. No SIMD intrinsics are available; the GPU does the heavy work and `CpuRef` is scalar + `Parallel.For`.

1. Source lives in `src\*.cs`. No project file, no NuGet, no restore.
2. `run.ps1` runs the locality checks first; any failure aborts.
3. Compile: `Add-Type -Path (Get-ChildItem src -Recurse -Filter *.cs) -CompilerParameters` with `/unsafe /optimize+` and references System.Windows.Forms, System.Drawing, ...`.
4. Launch: call the studio's static entry point (e.g. `[Studio.App]::Run()`) (`powershell -STA`).
5. Types loaded by `Add-Type` cannot be reloaded in the same session; each run starts a fresh `powershell -NoProfile -STA -File run.ps1`.

## Roadmap

Eight phases, each closed by a test that must pass before the next starts. Use a small Llama-family model (1B, Q8\_0) until phase 7.

1. **Skeleton + run script.** `run.ps1`, empty WinForms window, locality checks wired in. *Gate:* window opens from a fresh `powershell` 5.1; checks reject a planted violation.
2. **GGUF reader.** Print metadata and tensor list. *Gate:* tensor offsets/sizes consistent with file length; shapes match metadata hyperparameters.
3. **Tokenizer.** *Gate:* encode→decode round-trips 50 sample strings; special tokens match GGUF metadata.
4. **CPU reference forward pass.** F32 math, Q8\_0 dequant. *Gate:* coherent greedy completions on 20 prompts; perplexity on a fixed local text in a sane range.
5. **Vulkan binding + SPIR-V emitter.** Instance, device, buffers, upload/download, dispatch a trivial SPIR-V "add" shader. *Gate:* GPU result equals CPU result.
6. **GPU forward pass.** Port every op as a SPIR-V compute shader; KV cache on GPU. *Gate:* every kernel matches its CPU op on random inputs (bit-exact or within 1e-6); GPU teacher-forced logprobs match llama.cpp as closely as `CpuRef` does (mean |diff| <= 0.03); GPU and CPU agree on >= 90% of top tokens. (A fixed 1e-2 logit tolerance proved unreachable: the BOS token's ~400-magnitude activations amplify float accumulation-order differences to ~0.1-0.5.)
7. **Quant formats.** Q4\_K, Q5\_K, Q6\_K dequant+matvec. *Gate:* runs a 7B-8B Q4\_K\_M model coherently.
8. **Studio + speed.** Chat history, streaming, settings, tokens/sec meter; fused kernels, batched prefill. *Gate:* 8B Q4 at a usable speed (target 30+ tokens/sec).

## Enforcing "local only"

`run.ps1` refuses to compile unless all checks pass.

- [ ] `Add-Type` references only BCL / framework assemblies; no `.dll` files in the repo.
- [ ] Source contains no `System.Net`, `HttpClient`, `Socket`, `WebRequest`, or `Process.Start`.
- [ ] Every `DllImport` / `LibraryImport` target is on the allowlist: `vulkan-1.dll`, `kernel32.dll`.
- [ ] Source contains no `Assembly.Load*` or `NativeLibrary` calls.
- [ ] Model loader accepts only local file paths; no URL fields anywhere in the UI.



## Risks and expectations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| SPIR-V emitter and shaders are slow to write and debug | Phase 5-7 take longest | Keep `CpuRef` as ground truth; validate emitted SPIR-V structure in tests |
| Speed below llama.cpp | Likely 2-5x slower at first | Fuse dequant+matvec; tune block sizes per kernel |
| Driver feature gaps | Shaders fail to load | Target Vulkan 1.2 / SPIR-V 1.5 baseline; query features (16-bit storage, subgroups) and fall back |
| No external reference implementation | Bugs in phases 2-4 harder to spot | Self-consistency gates, round-trip tests, perplexity; `CpuRef` kept simple and readable |
| New model architectures | Each needs custom code | Llama-family only until the core is stable |

Hardware: dev machine is an AMD Radeon RX 6700 XT (RDNA2, 12 GB VRAM, Vulkan 1.4 loader); it has run a 9B model. Target machine has 32 GB. Tune for subgroup size 32/64 (RDNA wave32/wave64).
