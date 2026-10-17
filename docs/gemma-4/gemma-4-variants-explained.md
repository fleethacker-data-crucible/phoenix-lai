# Gemma 4 Variants Explained (QAT Q4_0 GGUF)

## Overview
Gemma 4 GGUF checkpoints on Hugging Face share one naming pattern: `gemma-4-{size}-it-qat-q4_0-gguf`. Five sizes exist: 12b, 26b-a4b, 31b, e2b, e4b. This document explains what each part of the name means and which variant to pick.

## Naming Suffix: What Each Part Means
- **`it`**: instruction-tuned, so the model is chat- and assistant-ready.
- **`qat`**: quantization-aware training. Quantization is simulated during training, so the model keeps its quality at low precision.
- **`q4_0`**: 4-bit weights, roughly 4x smaller than 16-bit.
- **`gguf`**: file format used by llama.cpp, LM Studio, and Ollama-style local tools.

## Variant Comparison

| Variant | Architecture | Modalities | Context | Q4 memory | Main advantage |
|---|---|---|---|---|---|
| gemma-4-12b-it-qat-q4_0-gguf | Dense, encoder-free ("Unified") | Text, image, audio | 256K | ~6.7 GB | Best balance of quality and size for laptops and mid-range GPUs |
| gemma-4-26b-a4b-it-qat-q4_0-gguf | Mixture-of-Experts | Text, image | 256K | ~14.4 GB | Speed near a 4B model with much higher quality |
| gemma-4-31b-it-qat-q4_0-gguf | Dense | Text, image | 256K | ~17.5 GB | Highest quality in the family |
| gemma-4-e2b-it-qat-q4_0-gguf | Small dense | Text, image, audio | 128K | ~2.9 GB | Runs on phones and edge devices |
| gemma-4-e4b-it-qat-q4_0-gguf | Small dense | Text, image, audio | 128K | ~4.5 GB | Stronger than E2B, still phone and laptop friendly |

## What "E" Means in Gemma 4 E2B and E4B
"E" stands for **effective parameters**.
- E2B and E4B use **Per-Layer Embeddings (PLE)**. Each decoder layer gets its own small embedding for every token.
- These embedding tables are large but only used for fast lookups, so they add storage but little compute.
- Example: Gemma 4 E2B stores 5.1B parameters but computes like a 2.3B model. E4B stores 8B but computes like a 4.5B model.
- **Advantage:** small compute cost and fast on-device speed.
- **Trade-off:** the file and memory footprint are larger than the "2B" or "4B" name suggests.

## What "A" Means in Gemma 4 26B A4B
"A" stands for **active parameters**.
- Gemma 4 26B A4B is a **Mixture-of-Experts (MoE)** model with 25.2B total parameters and 3.8B active per token.
- It has 128 experts. For each token, 8 experts plus 1 shared expert run.
- **Advantage:** generation speed close to a 4B model, with quality near a much larger model.
- **Trade-off:** all ~26B parameters must still be loaded into memory, so memory need is close to a dense 26B model, not a 4B one.

## What "Unified" Means in Gemma 4 12B
- The 12B has no separate vision or audio encoders.
- Image patches and audio waveforms pass through lightweight linear layers directly into one decoder-only transformer.
- **Advantage:** lower multimodal latency, and the whole model can be fine-tuned in one pass.

## Which Variant Should I Pick?
- **Phone or low RAM:** E2B or E4B.
- **Laptop or mid-range GPU:** 12B.
- **Fast and smart:** 26B A4B.
- **Best possible quality:** 31B.

## Related Docs
- Full overview: https://gemma.ai/models/gemma-4/
- Model card: https://gemma.ai/models/gemma-4/model-card/
- QAT collection: https://huggingface.co/collections/google/gemma-4-qat-q4-0
