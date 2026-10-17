# Gemma 4 Overview

## What is Gemma 4?
Gemma 4 is Google DeepMind's family of open-weight models, released under the Apache 2.0 license. It accepts text and images as input (audio on select sizes) and produces text. Both pre-trained and instruction-tuned versions are available. Context windows reach 256K tokens, and the models cover 140+ languages in pre-training (35+ work well out of the box).

## Model Sizes
Five sizes target everything from phones to workstations:

| Model | Type | Params | Context | Modalities |
|---|---|---|---|---|
| E2B | Dense | 2.3B effective (5.1B with embeddings) | 128K | Text, image, audio |
| E4B | Dense | 4.5B effective (8B with embeddings) | 128K | Text, image, audio |
| 12B Unified | Dense | ~12B | 256K | Text, image, audio |
| 26B A4B | Mixture-of-Experts | 25.2B total, 3.8B active | 256K | Text, image |
| 31B | Dense | 30.7B | 256K | Text, image |

- **"E" (effective):** small models use Per-Layer Embeddings, so lookup-only embedding tables inflate total size but not compute.
- **"Unified" (12B):** no separate vision/audio encoders; raw image patches and audio feed straight into one decoder, lowering latency and allowing single-pass fine-tuning.
- **"A" (active, 26B A4B):** only ~4B parameters run per token, making it much faster than its size suggests.
- **Architecture:** alternates local sliding-window attention with global attention for speed and low memory on long inputs.

## Key Capabilities
- Configurable step-by-step thinking mode for reasoning
- Long-context understanding
- Image understanding: OCR, documents/PDFs, charts, UI screens, handwriting, object detection
- Video understanding via frame sequences
- Mixed text and image prompts
- Native function calling for agents, plus native system-prompt support
- Coding: generation, completion, fixing
- Audio (E2B, E4B, 12B): speech recognition and speech translation

## Performance Highlights
The 31B model leads the family (e.g., ~85% MMLU Pro, ~89% AIME 2026, ~84% GPQA Diamond, Codeforces ELO ~2150). The 26B A4B comes close on most tests, and the 12B is strong for its size. Every Gemma 4 model substantially beats Gemma 3 27B on reasoning, coding, and agentic benchmarks.

## Recommended Settings
- **Sampling:** temperature 1.0, top_p 0.95, top_k 64.
- **Thinking:** enable by putting the thinking token at the start of the system prompt; remove it to disable.
- **Multi-turn chats:** keep only final answers in history, not earlier thoughts (except in tool-call turns).
- **Input order:** images before text; audio after text.
- **Image detail:** token budgets of 70, 140, 280, 560, or 1120. Use low budgets for classification, captioning, or video; high budgets for OCR and document parsing.
- **Limits:** audio up to 30 seconds; video up to 60 seconds (at 1 frame/sec).

## Training Data
Web text (140+ languages), code, math, images, and audio, with a January 2025 cutoff. Data was filtered for CSAM, personal/sensitive information, and content quality and safety.

## Safety
Gemma 4 went through the same safety evaluations as Google's Gemini models, covering child safety, dangerous content, sexually explicit content, hate speech, and harassment. Google reports major safety improvements over Gemma 3 and 3n with few unjustified refusals.

## Intended Uses
Text generation, chatbots, summarization, image data extraction, audio transcription and voice interaction, NLP/VLM research, language learning, and knowledge exploration.

## Limitations
- Quality depends on training data; biases and gaps carry through.
- Struggles with very open-ended or highly complex tasks and with nuance, sarcasm, or figurative language.
- Not a knowledge base: may produce incorrect or outdated facts.
- Can lack common-sense reasoning.
- Developers should add their own content-safety safeguards, monitor for bias, and follow privacy regulations.

## Learn More
Docs: https://gemma.ai/models/gemma-4/ | Model card: https://gemma.ai/models/gemma-4/model-card/ | Hugging Face: https://huggingface.co/collections/google/gemma-4
