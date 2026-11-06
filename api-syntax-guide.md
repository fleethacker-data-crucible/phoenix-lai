# OpenAI v1 API Syntax Guide

## Quick Start

This API provides an OpenAI-compatible gateway for accessing enterprise LLM providers. Use your STARK API key in the Authorization header to authenticate requests. An external OpenAI account is not required.

API keys and base URLs are environment-scoped. Keys automatically lock 14 days after unlock and can be unlocked at any time in the Keys tab.

``` powershell
cURL
# Ensure STARK_API_KEY is exported in your environment:
# export STARK_API_KEY="your-api-key"
curl -X POST https://XXX/v1/chat/completions \
  -H "Authorization: Bearer ${STARK_API_KEY:-YOUR_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "gemini-3.5-flash-lite-withtools",
    "messages": [
      {"role": "user", "content": "Hello, how are you?"}
    ]
  }'
```
## API Reference & Capabilities

`POST /v1/chat/completions`

Creates a chat completion for the given messages. Compatible with OpenAI's Chat Completions API. Note: POST /v1/chat/completions and POST /v1/embeddings are active Phase 0 endpoints; the OpenAI Responses API (POST /v1/responses) is planned for Phase 3.

Assist-lane model notice: Gemini Enterprise models ending in -assist (such as gemini-3.5-flash-assist) drop system messages and ignore tool definitions. When your pipeline requires system instructions or tool calling, use a standard model (e.g. gemini-3.5-flash or alias gemini-3.5-flash-withtools).

Request Body Parameters

``` json
{
  "model": "gemini-3.5-flash-lite-withtools",           // Required: Model identifier
  "messages": [                         // Required: Array of conversation messages
    {
      "role": "system",             // system, user, assistant, or tool
      "content": "You are a helpful assistant."
    },
    {
      "role": "user",
      "content": "Hello!"
    }
  ],
  "temperature": 0.7,                   // Optional: Sampling temperature (0-2)
  "max_tokens": 1000,                    // Optional: Maximum tokens to generate
  "stream": false                        // Optional: Enable Server-Sent Events (SSE) streaming
}
```

Example Response (HTTP 200 OK)

``` json
{
  "id": "chatcmpl-abc123xyz",
  "object": "chat.completion",
  "created": 1727500000,
  "model": "gemini-3.5-flash-lite-withtools",
  "choices": [
    {
      "index": 0,
      "message": {
        "role": "assistant",
        "content": "Hello! How can I assist you today?"
      },
      "finish_reason": "stop"
    }
  ],
  "usage": {
    "prompt_tokens": 12,
    "completion_tokens": 9,
    "total_tokens": 21
  }
}
```

Connect models to external tools and APIs using OpenAI-compatible function calling. Provide schema definitions in the tools array. When the model invokes a tool, execute your function locally and return the output with role: "tool" and the matching tool_call_id.


