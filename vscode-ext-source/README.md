# PhoenixLAI

Puts the local PhoenixLAI studio into VS Code:

- **Chat panel.** Pick **PhoenixLAI** in the model picker of VS Code's Chat view. It works in Ask, Edit and Agent modes. In Agent mode, VS Code's own tools (files, search, terminal) run with VS Code's approvals.
- **Inline completion.** Suggestions appear as ghost text while you type. They use fill-in-the-middle on models that support it, and plain continuation otherwise.

The extension only talks to `http://127.0.0.1:1234/v1`, the studio's loopback API. It has no settings, no telemetry and no dependencies. Start `PhoenixLAI_App` first. The answers come from whichever chat model the studio has loaded.

Requires VS Code 1.104 or newer. No GitHub sign-in is needed for your own models (VS Code 1.122+).
