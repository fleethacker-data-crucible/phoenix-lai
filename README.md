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

## Components

Phoenix consists of a local API server and a client interface.

### Local API Server (`src/WebServer.OpenAi.cs`)
- Acts as an OpenAI-compatible API server (`http://localhost:1234/v1`).
- Handles chat completions, completions, and embeddings.
- Does not perform external network calls.
- Processes requests against the loaded model (e.g., Gemma 4).

### Chat UI (`html/js/chat.js`)
- Provides the interactive chat interface within VS Code.
- Manages message rendering, layout (including a pinned welcome logo), and user input handling.

### RAG Client (Inferred functionality from `src/WebServer.cs`)
- Implements logic to retrieve relevant context from project files and the internal documentation library.
- Messages are processed by preparing context based on user input, system prompts, and loaded rules.

## Clients

### VS Code Extension
- A Phoenix LAI activity-bar view that embeds the studio chat page (iframe to `http://127.0.0.1:8765`) and connects the studio to the open VS Code folder when it is inside `Phoenix_Projects`, plus inline completion via `127.0.0.1:1234`.

### Loopback Web UI Client
- Serves a dedicated web interface for the chat/server: `http://127.0.0.1:8765`.

### Browser Client
- A separate mechanism to open the Phoenix window in your default browser (Chrome or Edge) when the service is running.

## SQL Reference

A comprehensive reference for standard SQL syntax, including portability advice, `JOIN` clauses, aggregate functions, and Data Definition Language (DDL). Refer to `docs/code-syntax/sql.md` for details on all operations from `SELECT` to `CREATE TABLE`.

## Getting Started

1. **Start the Application:** Execute `PhoenixLAI_App`.
2. **Project Context:** Ensure your project folder is within `%USERPROFILE%\Phoenix_Projects`.
3. **Access Interfaces:** Use the VS Code extension for integrated workflow or navigate to `http://127.0.0.1:8765` to access the standalone web client.

## Planned Improvements

- **API Refactoring:** The next iteration includes a split of the LLM API Server and all clients. This will enable the use of the standard `/v1/chat/completion` endpoint for all tasks, leveraging streaming for general requests, which will improve overall request handling and clarity.
