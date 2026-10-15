# What to gather

Download these on a machine with internet, then copy them in by hand. Keep models outside the repo (e.g. `D:\Models\`); put the reference docs in `docs\ref\`.

## Models (GGUF)
| # | File | Approx size | Source (Hugging Face) | Used for |
|---|------|------|--------|---------|
| 1 | `Llama-3.2-1B-Instruct-Q8_0.gguf` | 1.3 GB | `bartowski/Llama-3.2-1B-Instruct-GGUF` | Phases 2–6 (main dev model) |
| 2 | `Meta-Llama-3.1-8B-Instruct-Q4_K_M.gguf` | 4.9 GB | `bartowski/Meta-Llama-3.1-8B-Instruct-GGUF` | Phase 7–8 (Q4_K + Q6_K tensors) |
| 3 | `Meta-Llama-3.1-8B-Instruct-Q5_K_M.gguf` | 5.7 GB | same repo | Phase 7 (Q5_K) — optional |
| 4 | `tinyllama-1.1b-chat-v1.0.Q8_0.gguf` | 1.2 GB | `TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF` | SentencePiece tokenizer path — optional |

## Reference documents (read-only specs, no code is used)
| File | Where | Why |
|------|-------|-----|
| GGUF spec | `github.com/ggml-org/ggml` → `docs/gguf.md` | File format for the `Gguf` reader |
| `ggml-common.h` | `github.com/ggml-org/llama.cpp` → `ggml/src/ggml-common.h` | Exact Q8_0 / Q4_K / Q5_K / Q6_K block layouts |
| `ggml-quants.c` | same repo → `ggml/src/ggml-quants.c` | Dequantize reference logic |
| `vulkan_core.h` | `github.com/KhronosGroup/Vulkan-Headers` → `include/vulkan/vulkan_core.h` | Struct layouts + function signatures for P/Invoke |
| SPIR-V spec (HTML or PDF) | `registry.khronos.org/SPIR-V/specs/unified1/SPIRV.html` | Opcodes and binary encoding for `SpirvBuilder` |
| `spirv.core.grammar.json` | `github.com/KhronosGroup/SPIRV-Headers` → `include/spirv/unified1/` | Machine-readable opcode table |
| GLSL.std.450 spec | `registry.khronos.org/SPIR-V/specs/unified1/GLSL.std.450.html` | `exp`, `sqrt`, `sin`/`cos` etc. in shaders |
| Vulkan spec, compute chapters (optional) | `registry.khronos.org/vulkan/specs/latest/html/vkspec.html` | Pipeline, descriptor, sync rules |

## Test text (optional)
| File | Why |
|------|-----|
| A few plain-English `.txt` files (~100 KB total, e.g. Project Gutenberg book) | Tokenizer round-trips and perplexity gate |

## Check on the target (32 GB) machine — nothing to download
Run in Windows PowerShell 5.1 and note the results:
```
$PSVersionTable.PSVersion
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full').Release   # want >= 528040 (.NET 4.8)
Test-Path C:\Windows\System32\vulkan-1.dll
Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion
```
Also confirm whether the 32 GB is system RAM or GPU VRAM.
