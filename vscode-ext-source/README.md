# Phoenix

The local Phoenix server inside VS Code:

- **Chat sidebar.** Click the triangle in the activity bar. This is the server's own chat, with all of its features:
  - creating, editing and deleting files in your project (a deleted file is kept in `.llm-backup`)
  - Instant Recall, so files it has read before are fast to re-read
  - the docs library
  - approvals (`/mode accept`)
  - every `/command`
- **Automatic folder connection.** The server works on the folder VS Code has open, as long as it is inside `%USERPROFILE%\Phoenix_Projects` (the server's project jail), e.g. `Phoenix_Projects\Test`.
- **Inline completion.** Suggestions appear as ghost text while you type.
- **The full Phoenix Client, laid out like VS Code's Chat view (2.9.0).** A view-title bar (New Chat, Show Chats, Settings); the model picker in the input box lists every provider's models; Settings holds Providers (add or edit: name, base URL, API format OpenAI /v1 or Anthropic /v1, API key, model, test connection) and Skills (list and Skill Builder), plus docs, appearance and app settings, exactly as in the Phoenix Client window.
- **Model settings live on the server (2.8.0).** The extension and its chat send no sampling settings (temperature, top_p, top_k, repeat penalty, reply limit). The Phoenix server applies its own per-model settings, Gemma 4's recommended values by default (temperature 1.0, top_p 0.95, top_k 64); change them in the server's dashboard or with the console command `sampling`. The chat's per-model window keeps only the system prompt.

The extension only talks to the local server: the chat page on `127.0.0.1:8765` and the API on `127.0.0.1:1893` (or the port set with the server's `apiport` console command or dashboard box, read from `%LOCALAPPDATA%\LocalLLMStudio\api-port.txt`). It has no settings, no telemetry and no dependencies. Start `PhoenixLAI_App` first. If the server was started after VS Code, click the refresh button in the Phoenix view.
