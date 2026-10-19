# What to gather

Download these on a machine with internet, then copy them in by hand. Keep models outside the repo (e.g. `D:\Models\`); put the reference docs in `docs\ref\`.

## Models (GGUF)
| # | File | Approx size | Source (Hugging Face) | Used for |
|---|------|------|--------|---------|
| 1 | `gemma-4-12b-it-qat-q4_0.gguf` | 7.0 GB | Gemma 4 12B instruct, QAT q4_0 | Chat model; all gates |
| 2 | `embeddinggemma-2-Q8_0.gguf` | 0.3 GB | EmbeddingGemma | Docs search (RAG) |

PhoenixLAI runs Gemma 4 only (Llama / Mistral support removed 2026-10-17).

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
