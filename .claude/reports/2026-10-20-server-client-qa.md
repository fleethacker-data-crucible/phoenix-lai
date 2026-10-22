# Server / client split: QA and functionality report (2026-10-20, branch dev-server-redo)

Model: `gemma-4-e2b_q4_0-it` (smallest), AMD RX 6700 XT, Windows PowerShell 5.1. Locality checks pass, compile OK.

## Results

| Suite | Result |
|---|---|
| OpenAI Python SDK 3.35.1 against `localhost:1234/v1` (`openai-compat.py`) | **32 / 32 pass** |
| Client: connections, keys, pass-through, security (`client-qa.py`, Phoenix server only) | **24 / 24 pass** |
| Client UI in the browser (add connection, test, save, chat) | pass |
| Dashboard live data, console commands, restart | pass |
| Studio chat on the server's own port (regression) | pass |

### OpenAI /v1 (server)
Models list / retrieve / 404 `model_not_found` · chat · developer + system roles · multi-turn · `max_tokens` and
`max_completion_tokens` → `length` · `stop` · streaming with `stream_options.include_usage` (single role chunk, usage
chunk, `[DONE]`) · `n=2` plain and streamed · `seed` reproducible · tools: call → result round trip, forced function,
`tool_choice: none`, streamed `tool_calls` · `response_format` `json_object` and `json_schema` · vision (`image_url`
data URI) · `/completions` plain, streamed, FIM `suffix` · embeddings (SDK default base64, float, `dimensions` with
re-normalisation) · OpenAI-shaped 400 / 404 errors · 4 clients at once (queued, all answered).

Speed on E2B: ~118 t/s generation, ~486 t/s prompt (console `bench 128`).

### Client
- Key typed once → Windows Credential Manager (`PhoenixLAI.Connections`); never in `connections.json`, never sent back
  to the page, field cleared on save; edit without a key keeps it; "remove the stored key" deletes it; delete removes it.
- All key tests run against the Phoenix server itself with its optional API key on (console `apikey set` / `new` /
  `off`, kept in Credential Manager `PhoenixLAI.Server`, never logged): no key / wrong key → 401 `invalid_api_key`,
  right key → every /v1 call ✓. llama.cpp is not used by any test (it stays a dev reference for comparing outputs only).
- Pass-through, plain and streamed, to both servers; the key goes as `Authorization: Bearer`.
- Refused targets: example.com, 10.0.0.5, 192.168.1.2, `//evil.com` paths, `@` paths, `127.0.0.1.nip.io`.
- Run guard applies to connection chats (3rd run request → WARNING banner).
- Foreign Host / Origin → 403; only `html\` is served.

## Found and fixed during QA
1. With no saved model, the connection test picked the alphabetically first model and made Phoenix swap E2B → 12B (27 s).
   Now it prefers the server's loaded model (`"state": "loaded"`), in the test and in the UI.
2. Thinking models can send `reasoning_content` first: the client now shows it as a collapsible
   "Thinking" line, and the test leaves room for it.
3. The studio page's start-up calls (skills, projects, models, recent folders) got 404 in the client; now empty lists.
4. Server ports narrowed to 8765–8789 so they never collide with the client's 8790–8799.

## Not machine-tested
- Clicking the tray menu itself. Its Restart/Stop/Start run the same `ServerHost` code as the console's `restart`,
  which was tested (stop, listeners back, model reloaded).
- The NVIDIA target machine.
