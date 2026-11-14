# Phoenix

Its own Phoenix Client inside VS Code: no client window needed. The extension starts a hidden Phoenix Client of its own (separate from the client window, which may run too or not) with the same providers, settings and chats.

- **Chat sidebar.** Click the triangle in the activity bar. Everything the client window has:
  - creating, editing and deleting files in your project (a deleted file is kept in `.llm-backup`)
  - Instant Recall, so files it has read before are fast to re-read
  - the docs library
  - approvals (`/mode accept`)
  - every `/command`
- **Automatic folder connection.** The server works on the folder VS Code has open, as long as it is inside `%USERPROFILE%\Phoenix_Projects` (the server's project jail), e.g. `Phoenix_Projects\Test`.
- **Inline completion.** Suggestions appear as ghost text while you type.
- **The full Phoenix Client, laid out like VS Code's Chat view (2.9.0).** A view-title bar (New Chat, Show Chats, Settings); the model picker in the input box lists every provider's models; Settings holds Providers (add or edit: name, base URL, API format OpenAI /v1 or Anthropic /v1, API key, model, test connection) and Skills (list and Skill Builder), plus docs, appearance and app settings, exactly as in the Phoenix Client window.
- **Model settings live on the server (2.8.0).** The extension and its chat send no sampling settings (temperature, top_p, top_k, repeat penalty, reply limit). The Phoenix server applies its own per-model settings, Gemma 4's recommended values by default (temperature 1.0, top_p 0.95, top_k 64); change them in the server's dashboard or with the console command `sampling`. The chat's per-model window keeps only the system prompt.

The extension talks only to this computer: its own Phoenix Client on `127.0.0.1:8790-8799` and the Phoenix server on its saved port (default 1893). The one process it starts is Windows PowerShell running `srcun-phoenix-vscode.ps1` from the Phoenix folder that ran last (it stops by itself about 30 s after VS Code closes). Run `Phoenix_Client` or `Phoenix_LAI_Server` once so it knows where Phoenix is; start `Phoenix_LAI_Server` for local models and inline completion. No settings, no telemetry, no dependencies.
