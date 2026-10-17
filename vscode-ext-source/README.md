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

The extension only talks to the local server: the chat page on `127.0.0.1:8765` and the API on `127.0.0.1:1234`. It has no settings, no telemetry and no dependencies. Start `PhoenixLAI_App` first. If the server was started after VS Code, click the refresh button in the Phoenix view.
