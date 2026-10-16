# PhoenixAI — implementation report (2026-10-16, second pass)

Everything from the audit's recommendation list, plus **Instant Recall**. All numbers are measured on the dev machine
(RX 6700 XT 12 GB, Gemma 4 12B QAT Q4_0 and Llama 3.1 8B Q4_K_M). All test gates (phases 2–9) and the accuracy checks
against llama.cpp pass after every change.

## What it saved

| Change | Before | After | Saving |
|---|---|---|---|
| **Instant Recall** — 6,067-token file, new chat (Gemma) | 32.2 s to first token | **0.56 s** | **58×** |
| **Instant Recall** — 16,185-token file after a full restart (from disk) | ~100 s | **1.3 s** | **~75×** |
| Instant Recall in the real app (same 16K file, two chats) | ~100 s each | **1.7 s / 0.86 s** | — |
| Code check after a long reply (exact-token reuse) | 7.6 s | **2.1 s** | 3.6× |
| Gemma 12B load (warm disk cache) | 14.9 s | **7.9 s** | 1.9× |
| Model ready after load (docs-search model moved off the critical path) | +3.1 s | **+0 s** | 3 s sooner |
| Llama 8B prompt reading (tiled attention) | 350 tok/s | **466 tok/s** | +33% |
| Gemma long-file prompt reading, 5.6K tokens at 0.5–6K context | 188 tok/s | **210 tok/s** | +12% |
| Llama 8B rewriting a file (prompt-lookup speculation) | 57.1 tok/s | **71.6 tok/s** | +25% |
| Gemma 12B VRAM for the sliding-window cache | 0.78 GB | **0.47 GB** | −0.31 GB (~20K more tokens of context room) |

Accuracy after all of it: Gemma 48/48 top-1 vs llama.cpp (mean |Δlogprob| 0.0067), Llama 8B gate 137 positions mean
0.0162 (limit 0.03), batched vs token-by-token 24/24.

## Instant Recall (the "one thing")

**What it does.** Every file the model reads is read once *on its own* and its KV cache (what the model computed for
it) is saved — in RAM (3 GB) and on disk in the project (`.phoenix\recall`, 8 GB per project, least-recently-used
pruned). Whenever the same file text appears again — the next message, a new chat, after a restart — the saved cache is
copied into the conversation at the new position instead of the model reading the file again. `/recall` pre-reads a
whole project in the background (a chat message pauses it), so even first reads are instant.

**How.**
- `KernelsRecall.Gather` copies a position range out of the fp16 ring caches; `KernelsRecall.Scatter` writes it back
  elsewhere and **re-rotates every key by the position difference**. RoPE is a rotation by `position × θ` per dimension
  pair, so a key computed at position *s* becomes the key at *p* with one more rotation by *p − s*. Verified: GPU result
  vs a CPU rotation agree within fp16 rounding (0.0005 / 0.0001).
- The engine encodes file texts as their own token runs, so their position in every prompt is known; the prefill loop
  splices known runs instead of computing them (`Engine.EncodeBody`, `GenerateLocked`).

**What we learned building it (and why it is "read alone").**
- Control: a snapshot spliced back at the same place and context is **bit-exact** (32/32, Δ 0.0000).
- A snapshot taken *inside* a chat carried that chat's question: a later chat about `Write()` got an answer about
  `Resolve()`. So snapshots are now always taken with the file read on its own; answers in later chats were correct
  (Resolve / Write / ToolInstructions, three chats, one file).
- Reading alone vs in context changes the model's internal state somewhat (measured: one changed word of context moves
  token-level agreement to 25/32; a pure 4-position shift keeps 30/32, Δ 0.047). The *answers* stay correct and on
  topic — they are worded differently, not wrong.

**Cost.** Disk and RAM: Gemma's 40 sliding-window layers need the last 1,024 positions of a file (320 KB/token) plus
16 KB/token for the 8 global layers — about 0.4–0.8 GB per large file. Llama models do not use it yet (their 8-bit KV
cache needs a requantizing splice; next step).

## Other changes

- **Tiled ("flash") prefill attention** (`KernelsFlash`): 16 queries × 16 keys per workgroup, K/V staged once in shared
  memory. The RX 6700 XT driver allows 32 KB of shared memory, so Gemma's 512-wide global heads keep the plain kernel
  (an 8×8 tile measured slower).
- **Prompt-lookup speculative decoding**: when the last 3–4 tokens appeared earlier in the context, the tokens that
  followed are guessed and checked in one batched pass (`PrefillAll` returns every row's logits); a guess is kept only
  if sampling the model there gives that token, so the output distribution is unchanged. Adaptive: stops for the reply
  when under 70% of guesses are kept (Gemma: 41% kept → speculation would have cost 36%, so it switches itself off).
- **Docs-search model warm-up in the background** after "ready", still under the GPU lock (no overlap with chat GPU
  work — the cause of the 2026-10-16 driver reset).
- **Test gates** follow the current models (8B renamed; removed models are skipped instead of failing). All pass.
- Writes into `.phoenix` are refused (the model can't tamper with recall snapshots).

## Considered and not done (with the measurement that decided it)

| Idea | Why not |
|---|---|
| Speculative decoding with Llama 3.2 1B drafting for 8B | 1B runs 196 tok/s vs 8B 62 tok/s here — only ~3× faster; 4 drafts cost about one verify, net ~1.1×. Prompt-lookup drafting (free guesses) does better and also covers Gemma. |
| Vulkan pipeline cache on disk | AMD's driver already caches compiled kernels across launches: 195 ms per kernel on a cold launch, **1 ms** afterwards. Nothing left to save. |
| Loading the docs-search model in parallel with the chat model | Concurrent GPU work on two devices is what reset the driver on 2026-10-16. Moved it after "ready" instead (same user-visible gain, no risk). |
| Batched CPU prompt reading | CPU mode is turned off for now, as requested. |

## Commits

`cd832b9` Instant Recall · `05997f6` Instant Recall on disk + /recall · `781467f` tiled prefill attention ·
`2d0fabc` prompt-lookup speculation · gates + background warm-up (this report's commit).
