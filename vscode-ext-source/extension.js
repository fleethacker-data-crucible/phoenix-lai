// Phoenix for VS Code: the server's own chat (file tools, Instant Recall, docs search, approvals) in a VS Code
// sidebar, connected to the folder VS Code has open, plus inline code completion. Zero dependencies: VS Code API +
// Node's http only. Talks only to this computer: the Phoenix Client's chat page on 127.0.0.1:8790-8799 and the Phoenix server's API on 127.0.0.1 at its saved port (default 1893).
"use strict";
const vscode = require("vscode");
const http = require("http");
const os = require("os");
const path = require("path");
const fs = require("fs");

const HOST = "127.0.0.1";
// The Phoenix server's API port: what the server saved (console apiport / dashboard), else 1893. Read on every use, so a
// port change in the server reaches completion at once, with no VS Code restart.
const DEFAULT_API_PORT = 1893;
const API_PORT_FILE = path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"), "LocalLLMStudio", "api-port.txt");
function apiPort() {
  try {
    const p = parseInt(fs.readFileSync(API_PORT_FILE, "utf8").trim(), 10);
    if (p >= 1024 && p <= 65535) return p;
  } catch (e) { /* no file yet: the default */ }
  return DEFAULT_API_PORT;
}
const CHAT_PORTS = [8790, 8799]; // the Phoenix Client takes the first free port in this range
const PROJECTS_ROOT = path.join(os.homedir(), "Phoenix_Projects"); // the server's project jail

// ---------------------------------------------------------------- HTTP (loopback only)

function request(port, method, urlPath, body, token, onEvent) {
  return new Promise((resolve, reject) => {
    const data = body ? Buffer.from(JSON.stringify(body), "utf8") : null;
    const headers = data ? { "Content-Type": "application/json", "Content-Length": data.length } : {};
    const req = http.request({ host: HOST, port, path: urlPath, method, headers, timeout: onEvent ? 0 : 3000 }, (res) => {
      res.setEncoding("utf8");
      let text = "";
      let buf = "";
      res.on("data", (chunk) => {
        if (!onEvent) { text += chunk; return; }
        buf += chunk;
        let nl;
        while ((nl = buf.indexOf("\n")) >= 0) {
          const line = buf.slice(0, nl).trim();
          buf = buf.slice(nl + 1);
          if (!line.startsWith("data:")) continue;
          const payload = line.slice(5).trim();
          if (payload === "[DONE]") continue;
          try { onEvent(JSON.parse(payload)); } catch (e) { /* partial or comment line */ }
        }
      });
      res.on("end", () => {
        if (res.statusCode !== 200) {
          let msg = text;
          try { const j = JSON.parse(text); msg = (j.error && j.error.message) || j.error || text; } catch (e) { /* raw */ }
          reject(new Error(msg || "HTTP " + res.statusCode));
        } else resolve(text);
      });
      res.on("error", reject);
    });
    req.on("timeout", () => req.destroy(new Error("timeout")));
    req.on("error", reject);
    if (token) {
      if (token.isCancellationRequested) { req.destroy(); resolve(""); return; }
      token.onCancellationRequested(() => { req.destroy(); resolve(""); });
    }
    req.end(data || undefined);
  });
}

// The Phoenix Client's chat page port, or 0 when the client is not running.
async function findChatPort() {
  for (let p = CHAT_PORTS[0]; p <= CHAT_PORTS[1]; p++) {
    try {
      const info = JSON.parse(await request(p, "GET", "/api/info"));
      if (info && "folderAccess" in info) return p;
    } catch (e) { /* not this port */ }
  }
  return 0;
}

// True while the server answers on this port.
async function alive(port) {
  try { const info = JSON.parse(await request(port, "GET", "/api/info")); return !!info && "folderAccess" in info; } catch (e) { return false; }
}

// "/Test" for a VS Code folder inside Phoenix_Projects, else null (the server only opens folders there).
function projectPathFor(folder) {
  const rel = path.relative(PROJECTS_ROOT, folder);
  if (!rel || rel.startsWith("..") || path.isAbsolute(rel)) return null;
  return "/" + rel.split(path.sep).join("/");
}

// ---------------------------------------------------------------- Chat view (the server's chat page)

class ChatView {
  constructor() {
    this.view = null;
    this.port = 0;
  }

  resolveWebviewView(view) {
    this.view = view;
    view.webview.options = { enableScripts: true };
    view.webview.onDidReceiveMessage(async (m) => {
      if (!m) return;
      if (m.type === 'open') openProjectFile(m.path, !!m.auto);
      else if (m.type === 'diag') {
        const errors = await errorsIn(m.paths);
        view.webview.postMessage({ type: 'phoenix-diag-result', errors, excerpts: errors.length ? await excerptsFor(errors) : [] });
      }
      else if (m.type === 'ready') this.sendEditor();
    });
    view.onDidChangeVisibility(() => { if (view.visible && !this.port) this.refresh(); });
    this.refresh();
    // Follow the server: when it stops (its window was closed) show "not running"; when it starts again, reconnect.
    const watch = setInterval(async () => {
      if (!this.view) return;
      const up = this.port ? await alive(this.port) : false;
      if (this.port && !up) { this.port = 0; this.view.webview.html = page(null); }
      else if (!this.port && (await findChatPort())) this.refresh();
    }, 4000);
    view.onDidDispose(() => clearInterval(watch));
  }

  async refresh() {
    if (!this.view) return;
    this.port = await findChatPort();
    if (!this.port) {
      this.view.webview.html = page(null);
      return;
    }
    const proj = await connectFolder(this.port, false);
    this.view.webview.html = page(this.port);
    // Instant Recall: pre-read the project in the background once per folder per session, so files open instantly later.
    if (proj && proj !== this.recalled) {
      this.recalled = proj;
      request(this.port, "POST", "/api/recall", {}).catch(() => {});
    }
  }

  // The open file, cursor and selection (inside the project only), for the chat to send with the next message.
  sendEditor() {
    if (!this.view) return;
    this.view.webview.postMessage({ type: 'phoenix-editor', editor: editorState() });
  }
}

function editorState() {
  const ed = vscode.window.activeTextEditor;
  if (!ed || ed.document.uri.scheme !== "file") return null;
  const root = projectRoot();
  const full = ed.document.uri.fsPath;
  if (!root || !full.startsWith(root + path.sep)) return null;
  const doc = ed.document, sel = ed.selection;
  const st = { file: path.relative(root, full).split(path.sep).join("/"), line: sel.active.line + 1 };
  if (!sel.isEmpty) {
    let text = doc.getText(sel);
    if (text.length > 6000) text = text.slice(0, 6000) + "\n... (selection cut at 6000 characters)";
    st.selection = text; st.selStart = sel.start.line + 1; st.selEnd = sel.end.line + 1;
  } else {
    const a = Math.max(0, sel.active.line - 15), z = Math.min(doc.lineCount - 1, sel.active.line + 15);
    st.around = doc.getText(new vscode.Range(a, 0, z, doc.lineAt(z).text.length));
    st.aroundStart = a + 1;
  }
  return st;
}

// The workspace folder that is a Phoenix project (inside Phoenix_Projects), or null.
function projectRoot() {
  const f = (vscode.workspace.workspaceFolders || []).find((x) => projectPathFor(x.uri.fsPath));
  return f ? f.uri.fsPath : null;
}

// VS Code's errors for files the model just changed (waits a moment so language servers catch up).
async function errorsIn(paths) {
  const root = projectRoot();
  if (!root || !Array.isArray(paths)) return [];
  await new Promise((r) => setTimeout(r, 2500));
  const out = [];
  for (const rel of paths) {
    if (typeof rel !== "string") continue;
    const full = path.resolve(root, rel);
    if (!full.startsWith(root + path.sep)) continue;
    for (const d of vscode.languages.getDiagnostics(vscode.Uri.file(full)))
      if (d.severity === vscode.DiagnosticSeverity.Error) out.push({ path: rel, line: d.range.start.line + 1, message: d.message.split("\n")[0] });
  }
  return out;
}

// The current text around the errors (error lines +-4, overlapping ranges merged, at most 150 lines in all), so the
// model fixes what is in the file now instead of what it remembers writing.
async function excerptsFor(errors) {
  const root = projectRoot();
  const out = [];
  let budget = 150;
  for (const rel of [...new Set(errors.map((e) => e.path))]) {
    let doc;
    try { doc = await vscode.workspace.openTextDocument(vscode.Uri.file(path.resolve(root, rel))); } catch (e) { continue; }
    const ranges = [];
    for (const ln of errors.filter((e) => e.path === rel).map((e) => e.line).sort((a, b) => a - b)) {
      const s = Math.max(1, ln - 4), t = Math.min(doc.lineCount, ln + 4), last = ranges[ranges.length - 1];
      if (last && s <= last[1] + 1) last[1] = Math.max(last[1], t); else ranges.push([s, t]);
    }
    for (const [s, t0] of ranges) {
      if (budget <= 0) break;
      const t = Math.min(t0, s + budget - 1);
      const lines = [];
      for (let i = s; i <= t; i++) lines.push(doc.lineAt(i - 1).text);
      out.push({ path: rel, start: s, end: t, text: lines.join("\n") });
      budget -= t - s + 1;
    }
  }
  return out;
}

function page(port) {
  if (!port) {
    return `<!doctype html><html><body style="font-family:var(--vscode-font-family);color:var(--vscode-foreground);padding:12px">
<p><b>The Phoenix Client is not running.</b></p><p>Start <code>PhoenixLAI_Client</code> (or <code>PhoenixLAI_App</code>); this view reconnects by itself when it is up.</p></body></html>`;
  }
  const origin = `http://${HOST}:${port}`;
  const nonce = Math.random().toString(36).slice(2) + Date.now().toString(36);
  // The chat page is another origin, so it cannot see VS Code's theme variables: read them here and post them in,
  // again whenever the theme changes (VS Code updates the body class and the variables on this page).
  return `<!doctype html><html><head>
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; frame-src ${origin}; style-src 'unsafe-inline'; script-src 'nonce-${nonce}';">
<style>html,body{margin:0;padding:0;height:100%;overflow:hidden;background:var(--vscode-sideBar-background)}iframe{border:0;width:100%;height:100%;display:block}</style>
</head><body><iframe id="chat" src="${origin}/vscode.html" allow="clipboard-read; clipboard-write"></iframe>
<script nonce="${nonce}">
(function () {
  const frame = document.getElementById('chat');
  const css = getComputedStyle(document.documentElement);
  const v = (...names) => { for (const n of names) { const x = css.getPropertyValue(n).trim(); if (x) return x; } return ''; };
  const probe = document.createElement('span'); document.body.appendChild(probe);
  const rgbOf = (c) => { probe.style.color = ''; probe.style.color = c; const m = getComputedStyle(probe).color.match(/\\d+(\\.\\d+)?/g); return m ? m.slice(0, 3).map(Number) : null; };
  function theme() {
    const accent = v('--vscode-textLink-foreground', '--vscode-focusBorder', '--vscode-button-background');
    const rgb = rgbOf(accent) || [0, 122, 204];
    const lighter = 'rgb(' + rgb.map(c => Math.round(c + (255 - c) * 0.35)).join(',') + ')';
    const light = document.body.classList.contains('vscode-light') || document.body.classList.contains('vscode-high-contrast-light');
    return {
      kind: light ? 'light' : 'dark',
      bg: v('--vscode-sideBar-background', '--vscode-editor-background'),
      panel: v('--vscode-editorWidget-background', '--vscode-sideBar-background'),
      fg: v('--vscode-foreground', '--vscode-editor-foreground'),
      soft: v('--vscode-foreground'),
      dim: v('--vscode-descriptionForeground', '--vscode-disabledForeground'),
      line: v('--vscode-panel-border', '--vscode-widget-border', '--vscode-input-border') || 'rgba(128,128,128,.25)',
      accent: accent, accent2: lighter, accentRgb: rgb.join(','),
      code: v('--vscode-textCodeBlock-background', '--vscode-editor-background'),
      ok: v('--vscode-testing-iconPassed', '--vscode-charts-green'),
      err: v('--vscode-errorForeground'),
      font: v('--vscode-font-family'),
      mono: v('--vscode-editor-font-family'),
      size: v('--vscode-font-size'),
    };
  }
  // Activity lines in the chat ask to open a file: pass it to the extension.
  const vsc = acquireVsCodeApi();
  let lastEditor = null;
  const toFrame = (m) => { try { frame.contentWindow.postMessage(m, '${origin}'); } catch (e) {} };
  window.addEventListener('message', (e) => {
    const d = e.data || {};
    if (e.origin === '${origin}') {
      // from the chat page
      if (d.type === 'phoenix-open' && typeof d.path === 'string') vsc.postMessage({ type: 'open', path: d.path, auto: !!d.auto });
      else if (d.type === 'phoenix-diag' && Array.isArray(d.paths)) vsc.postMessage({ type: 'diag', paths: d.paths });
    } else if (d.type === 'phoenix-editor' || d.type === 'phoenix-diag-result') {
      // from the extension
      if (d.type === 'phoenix-editor') lastEditor = d;
      toFrame(d);
    }
  });
  frame.addEventListener('load', () => { if (lastEditor) toFrame(lastEditor); vsc.postMessage({ type: 'ready' }); });
  const send = () => { try { frame.contentWindow.postMessage({ type: 'phoenix-theme', theme: theme() }, '${origin}'); } catch (e) {} };
  frame.addEventListener('load', () => { send(); setTimeout(send, 500); });
  new MutationObserver(send).observe(document.body, { attributes: true, attributeFilter: ['class'] });
  new MutationObserver(send).observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
})();
</script></body></html>`;
}

// Opens a file the model touched, by its path relative to the connected project (never outside it).
// auto: opened because the model started editing it; focus stays in the chat, and a file that does not exist yet
// (being created) is opened once it appears.
async function openProjectFile(rel, auto) {
  const folders = vscode.workspace.workspaceFolders || [];
  const f = folders.find((x) => projectPathFor(x.uri.fsPath));
  if (!f || typeof rel !== "string") return;
  const root = f.uri.fsPath;
  const full = path.resolve(root, rel);
  if (full !== root && !full.startsWith(root + path.sep)) return;
  if (auto) {
    for (let i = 0; i < 240 && !fs.existsSync(full); i++) await new Promise((r) => setTimeout(r, 500));
    if (!fs.existsSync(full)) return;
  }
  try {
    const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(full));
    await vscode.window.showTextDocument(doc, { preview: !auto, preserveFocus: auto });
  } catch (e) {
    if (!auto) vscode.window.showWarningMessage("Phoenix: cannot open " + rel + " (" + e.message + ")");
  }
}

// Points the server at the folder VS Code has open (when it is inside Phoenix_Projects).
async function connectFolder(port, loud) {
  const folders = vscode.workspace.workspaceFolders || [];
  const proj = folders.map((f) => projectPathFor(f.uri.fsPath)).find((p) => p);
  if (!proj) {
    if (loud) vscode.window.showWarningMessage(`Phoenix only works on folders inside ${PROJECTS_ROOT}. Open one there (e.g. ${path.join(PROJECTS_ROOT, "Test")}).`);
    return false;
  }
  try {
    await request(port, "POST", "/api/folder", { path: proj });
    return proj;
  } catch (e) {
    vscode.window.showWarningMessage("Phoenix could not open " + proj + ": " + e.message);
    return false;
  }
}

// ---------------------------------------------------------------- Inline code completion (fill in the middle)

const PREFIX_CHARS = 6000;
const SUFFIX_CHARS = 2000;

const inlineProvider = {
  async provideInlineCompletionItems(document, position, context, token) {
    await new Promise((r) => setTimeout(r, 300)); // debounce: typing cancels this token
    if (token.isCancellationRequested) return null;
    const start = document.offsetAt(position);
    const all = document.getText();
    const prefix = all.slice(Math.max(0, start - PREFIX_CHARS), start);
    const suffix = all.slice(start, start + SUFFIX_CHARS);
    if (!prefix.trim()) return null;
    const line = document.lineAt(position.line).text;
    const lineBefore = line.slice(0, position.character);
    const lineRest = line.slice(position.character);
    let text = "";
    try {
      // No sampling fields: the Phoenix server's per-model settings apply (dashboard / console "sampling", Gemma 4's
      // recommended values by default). max_tokens and stop only keep a ghost-text suggestion short.
      await request(apiPort(), "POST", "/v1/completions",
        { model: "phoenixlai", prompt: prefix, suffix, max_tokens: 96, stream: true, stop: ["\n\n\n"] },
        token, (ev) => {
          const ch = ev.choices && ev.choices[0];
          if (ch && typeof ch.text === "string") text += ch.text;
        });
    } catch (e) {
      return null;
    }
    if (token.isCancellationRequested) return null;
    text = trimCompletion(text, suffix, lineRest);
    if (text && !lineBefore.trim() && lineBefore.length && text.startsWith(lineBefore)) text = text.slice(lineBefore.length);
    if (!text) return null;
    return [new vscode.InlineCompletionItem(text, new vscode.Range(position, position))];
  },
};

// Keep a completion from repeating what already follows the cursor, and stop it at the end of the current block.
function trimCompletion(text, suffix, lineRest) {
  if (!text) return "";
  text = text.replace(/^\s*```[\w-]*\n?/, "").replace(/\n?```\s*$/, "").replace(/<CURSOR>/g, "");
  const nextLine = suffix.split("\n").find((l) => l.trim().length > 0);
  if (nextLine) {
    const at = text.indexOf("\n" + nextLine.trim());
    if (at >= 0) text = text.slice(0, at);
    const lines = text.split("\n");
    const cut = lines.findIndex((l, i) => i > 0 && l.trim() === nextLine.trim());
    if (cut > 0) text = lines.slice(0, cut).join("\n");
  }
  if (lineRest.trim() && text.endsWith(lineRest)) text = text.slice(0, text.length - lineRest.length);
  return text.replace(/\s+$/, "");
}

// ---------------------------------------------------------------- Activation

let selTimer = null;

// RULES.md in the project: the server adds it to every chat about this project (coding style, framework, no-go areas).
async function createRules() {
  const root = projectRoot();
  if (!root) { vscode.window.showWarningMessage("Open a folder inside " + PROJECTS_ROOT + " first."); return; }
  const f = path.join(root, "RULES.md");
  if (!fs.existsSync(f)) {
    fs.writeFileSync(f, "# Rules for Phoenix in this project\n\n" +
      "Phoenix reads this file with every message about this project. Keep it short.\n\n" +
      "- Language / framework: \n- Style: (e.g. 2-space indent, single quotes)\n- Do not touch: (e.g. vendor/, generated files)\n- Notes: \n");
  }
  const doc = await vscode.workspace.openTextDocument(vscode.Uri.file(f));
  await vscode.window.showTextDocument(doc);
}

function activate(context) {
  const chat = new ChatView();
  context.subscriptions.push(
    vscode.window.registerWebviewViewProvider("phoenixlai.chat", chat, { webviewOptions: { retainContextWhenHidden: true } }),
    vscode.commands.registerCommand("phoenixlai.reconnect", () => chat.refresh()),
    vscode.commands.registerCommand("phoenixlai.connectFolder", async () => {
      const port = chat.port || (await findChatPort());
      if (!port) { vscode.window.showWarningMessage("The Phoenix Client is not running. Start PhoenixLAI_Client (or PhoenixLAI_App) first."); return; }
      if (await connectFolder(port, true)) chat.refresh();
    }),
    vscode.workspace.onDidChangeWorkspaceFolders(() => chat.refresh()),
    vscode.window.onDidChangeActiveTextEditor(() => chat.sendEditor()),
    vscode.window.onDidChangeTextEditorSelection(() => { clearTimeout(selTimer); selTimer = setTimeout(() => chat.sendEditor(), 300); }),
    vscode.commands.registerCommand("phoenixlai.createRules", createRules),
    vscode.languages.registerInlineCompletionItemProvider([{ scheme: "file" }, { scheme: "untitled" }], inlineProvider)
  );
}

function deactivate() {}

module.exports = { activate, deactivate };
