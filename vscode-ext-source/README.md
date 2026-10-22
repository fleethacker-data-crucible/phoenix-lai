# Phoenix

Its own Phoenix Client inside VS Code: no client window needed. The extension starts a hidden Phoenix Client of its own (separate from the client window, which may run too or not) with the same providers, settings and chats.

- **Chat sidebar.** Click the triangle in the activity bar. Everything the client window has:
  - creating, editing and deleting files in your project (a deleted file is kept in `.llm-backup`)
  - the docs library, skills, approvals (`/mode accept`) and every `/command`
- **Providers.** The model chip in the composer lists every provider's models from its own `/models` (offline providers greyed out), just like the client's top bar; picking one switches to it. Settings (gear) shows the Providers list: click one to use it, + adds one, the pencil edits it (URL, API key in Credential Manager, test).
- **Automatic folder connection.** The client works on the folder VS Code has open, as long as it is inside `%USERPROFILE%\Phoenix_Projects`, e.g. `Phoenix_Projects\Test`.
- **Inline completion.** Ghost text while you type, from the Phoenix server's API on `127.0.0.1:1234`.

The extension talks only to this computer: its own Phoenix Client on `127.0.0.1:8790-8799` and the Phoenix server on `127.0.0.1:1234`. The one process it starts is Windows PowerShell running `srcun-phoenix-vscode.ps1` from the Phoenix folder that ran last (it stops by itself about 30 s after VS Code closes). Run `Phoenix_Client` or `Phoenix_LAI_Server` once so it knows where Phoenix is; start `Phoenix_LAI_Server` for local models and inline completion. No settings, no telemetry, no dependencies.
