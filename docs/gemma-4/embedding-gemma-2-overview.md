# EmbeddingGemma 2 Overview

## What is EmbeddingGemma 2?
EmbeddingGemma 2 is an open multimodal embedding model from Google DeepMind (Apache 2.0 license), built on Gemma 4 advances. It maps text (including code), images, video, and audio, alone or mixed, into one shared 768-dimensional vector space. It is built for consumer hardware (phones, laptops) and low-latency on-device use: semantic search, retrieval-augmented generation (RAG), classification, and clustering.

Hugging Face model ID: `google/embeddinggemma-2`.

## Key Features
- **Four modalities, one space:** text, images, video, and audio share a single 768-dimensional embedding space.
- **Multilingual and code:** supports 100+ languages; about 14% better on code tasks than EmbeddingGemma 1.
- **Flexible footprint:** a 270M text backbone plus optional vision (170M) and audio (300M) encoders, so you load only the modalities you need.
- **Matryoshka Representation Learning (MRL):** embeddings can be truncated to 512, 256, or 128 dimensions for up to 6x lower vector storage cost with small quality loss.
- **Context window:** 8,192 tokens, enough for minutes of audio or video.
- **Task prefixes:** short text instructions steer embeddings toward search, classification, clustering, and similarity.

## Specifications

| Property | Value |
|---|---|
| Total parameters | 740M |
| Text backbone | 130M transformer + 140M embedder (270M text total) |
| Vision encoder | 170M |
| Audio encoder | 300M |
| Layers | 24 |
| Model / hidden dimension | 512 / 2048 |
| Attention | GQA/MQA, 4 heads, KV-heads 2 (local) / 1 (global), local:global 5:1 |
| Sliding window | 1024 tokens |
| Vocabulary | 262,144 |
| Activation | Gated FFN with GELU |
| Pooling | Mean pooling; projection 512 to 768 |
| Native output dimension | 768 |
| MRL dimensions | 768, 512, 256, 128 |
| Context window | 8,192 tokens |

## Benchmark Results (768d, full precision)

| Modality | Benchmark | Metric | EmbeddingGemma 2 | EmbeddingGemma 1 |
|---|---|---|---|---|
| Text | MTEB multilingual v2 | Mean(Task) | 61.36 | 61.15 |
| Code | MTEB code v1 | NDCG@10 | 78.68 | 68.76 |
| Image | MIEB lite | Mean(TaskType) | 64.64 | - |
| Image | MMEB v2 Image | Hit@1 | 57.28 | - |
| Visual docs | MMEB v2 VisDoc | NDCG@5 | 67.84 | - |
| Video | MMEB v2 Video | Hit@1 | 50.67 | - |
| Audio | MSEB Retrieval | MRR@10 | 69.54 | - |
| Audio | MAEB | Mean(Task) | 49.39 | - |

## Quality vs. Vector Size (MRL Truncation)

| Dimension | Compression | MTEB multilingual | MTEB English | MTEB code | MIEB lite | MMEB v2 | MSEB | MAEB |
|---|---|---|---|---|---|---|---|---|
| 768d | 1:1 | 61.36 | 68.46 | 78.68 | 64.64 | 59.01 | 69.54 | 49.39 |
| 512d | 1:1.5 | 61.17 | 68.41 | 77.24 | 64.32 | 58.38 | 69.18 | 49.21 |
| 256d | 1:3 | 60.41 | 67.78 | 76.18 | 63.13 | 56.24 | 66.76 | 48.91 |
| 128d | 1:6 | 57.89 | 65.68 | 71.41 | 59.06 | 45.65 | 56.71 | 46.92 |

Takeaway: quality is close to lossless down to 256d. 128d suits text-only workloads and hurts multimodal quality noticeably, so test it on your own data first.

## Quick Start
Install: `pip install -U sentence-transformers transformers`

```python
from sentence_transformers import SentenceTransformer

model = SentenceTransformer("google/embeddinggemma-2")
query_emb = model.encode("What causes the northern lights?", prompt_name="SearchQuery")
doc_emb = model.encode("The northern lights are caused by charged particles from the sun.", prompt_name="Document")
print(model.similarity(query_emb, doc_emb))
```

## Best Practices

### 1. Task instruction prefixes (text only)
- Prefixes improve quality; omitting them still works but is less precise.
- Do not add prefixes to images, video, or audio.
- **Asymmetric tasks (retrieval):** query prefix for queries, document prefix for corpus items.
- **Symmetric tasks (classification, similarity):** same prefix for every input.
- Format titled documents as `title: {title} | text: {content}`. Use `title: none` when there is no title. Note that `prompt_name="Document"` applies `title: none`, so titled documents must be formatted manually.

| Use case | Type | Prompt name | Query prefix |
|---|---|---|---|
| Web/document search | Asymmetric | SearchQuery | `task: search result \| query: {query}` |
| Question answering | Asymmetric | QuestionAnswering | `task: question answering \| query: {question}` |
| Fact checking | Asymmetric | FactChecking | `task: fact checking \| query: {claim}` |
| Code search | Asymmetric | CodeRetrieval | `task: code retrieval \| query: {query}` |
| Classification | Symmetric | Classification | `task: classification \| query: {content}` |
| Clustering | Symmetric | Clustering | `task: clustering \| query: {content}` |
| Similarity | Symmetric | SentenceSimilarity | `task: sentence similarity \| query: {content}` |

### 2. Load only the encoders you need
Pass `config_kwargs` to SentenceTransformer to skip unused encoders (syntax varies by library).

| Modalities | config_kwargs | Effective size |
|---|---|---|
| Text only | `{"vision_config": None, "audio_config": None}` | 270M |
| Text + image | `{"audio_config": None}` | 440M |
| Text + audio | `{"vision_config": None}` | 570M |
| Full multimodal | `{}` | 740M |

### 3. Truncating dimensions
- Truncated vectors must be L2-normalized again before cosine similarity. Skipping this silently degrades ranking.
- Queries and documents must use the same dimension.
- Easiest approach: `model.encode(query, truncate_dim=128, normalize_embeddings=True)` (use 512, 256, or 128).

### 4. Numerical precision
- Use `bfloat16` (recommended on supported hardware) or `float32`.
- Never use `float16`: activations exceed its range, giving NaN or silently degraded embeddings with no error.
- Example: `dtype = torch.bfloat16 if torch.cuda.is_bf16_supported() else torch.float32`, then `SentenceTransformer("google/embeddinggemma-2", model_kwargs={"torch_dtype": dtype})`.

### 5. Multimodal and interleaved input
- One input can mix text with images, video, and audio, sharing a single 8,192-token budget.
- Placeholders mark media positions in the text: `<|image|>`, `<|video|>`, `<|audio|>`. Each is filled in order from the matching key.
- The call returns one embedding for the whole input, comparable to any other EmbeddingGemma 2 embedding (e.g., a text-only query).
- Example: `model.encode({"text": "Waterproof running shoes. <|image|> Grip test: <|video|>", "image": ["shoe.jpg"], "video": "demo.mp4"})`

| Modality | Token cost | Max input (alone) |
|---|---|---|
| Text | 1 token per subword | 8,192 tokens |
| Image | 280 tokens (default) | ~29 images |
| Video | 140 tokens per frame (default) | ~58 frames |
| Audio | 25 tokens per second | ~327 seconds |

- Mixing modalities shrinks what fits of each.
- **Vision token budget:** 70 to 1120 tokens per image or frame. Higher gives better fine detail but more latency. A low budget fits up to ~114 images or frames.
- **Sampling defaults:** video at 1 frame per second (configurable); audio as mono 16 kHz.

## Training Data and Safety
- **Data:** web text (140+ languages), code, images, video, audio, and paired cross-modal samples, with a January 2025 cutoff. Filtered for CSAM, personal and sensitive data, and quality and safety.
- **Safety:** this is a pre-trained embedding model with no post-training alignment or output moderation. Mitigation focused on filtering training data. Deployers must add their own safeguards (such as retrieval filtering and fairness testing) and follow the Gemma Prohibited Use Policy.

## Intended Uses
- **Retrieval:** semantic search across text, code, images, video, and audio; RAG over knowledge bases; code search; spoken-query search.
- **Classification:** sentiment, content moderation, audio event detection, image categorization.
- **Clustering:** grouping documents or feedback; finding near-duplicates across modalities.
- **Semantic similarity:** recommendations, duplicate detection, paraphrase detection, cross-lingual alignment.
- **Fact verification:** retrieving evidence for a claim.

## Limitations and Risks
- Performance may differ across the 100+ supported languages.
- Weaker on open-ended or very complex tasks, and on sarcasm, nuance, and figurative language.
- Omitting task prefixes on text lowers quality.
- Training data biases can carry into embeddings; monitor and test for bias.
- Embeddings can be misused to organize or retrieve content in misleading or harmful ways.
- Privacy: data was filtered, but developers should still follow privacy regulations.

## Summary
EmbeddingGemma 2 is among the strongest multimodal embedding models under 1B parameters, aimed at on-device and edge AI.

## Links
- Model card: https://ai.google.dev/gemma/docs/embeddinggemma/model_card_2 (Google notes this site is deprecated in favor of gemma.ai)
- New docs: https://gemma.ai/models/embeddinggemma/model-card-v2/
- Hugging Face: https://huggingface.co/google/embeddinggemma-2
- Launch blog: https://blog.google/innovation-and-ai/technology/developers-tools/embeddinggemma-2
