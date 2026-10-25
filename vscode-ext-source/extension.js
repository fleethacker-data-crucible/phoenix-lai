// Phoenix for VS Code: another Phoenix Client window in a VS Code sidebar (same chats, settings and providers; the
// model chip lists every provider's models), connected to the folder VS Code has open, plus inline code completion. Zero dependencies: VS Code API +
// Node's http + child_process only. Starts its own hidden Phoenix Client (src\run-phoenix-vscode.ps1, the one process it may
// start) and talks only to this computer: that client on 127.0.0.1:8790-8799 and the Phoenix server's API on 127.0.0.1:1234.
"use strict";
const vscode = require("vscode");
const http = require("http");
const os = require("os");
const path = require("path");
const fs = require("fs");
const cp = require("child_process");

const HOST = "127.0.0.1";
const API_PORT = 1234;
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

// The extension's own Phoenix Client (src\run-phoenix-vscode.ps1, "instance":"vscode"): port, or 0 when it is not running.
// The Phoenix Client window is a separate instance and is never used here.
async function findChatPort() {
  for (let p = CHAT_PORTS[0]; p <= CHAT_PORTS[1]; p++) {
    try {
      const info = JSON.parse(await request(p, "GET", "/api/info"));
      if (info && info.instance === "vscode") return p;
    } catch (e) { /* not this port */ }
  }
  return 0;
}

// True while our client answers on this port.
async function alive(port) {
  try { const info = JSON.parse(await request(port, "GET", "/api/info")); return !!info && info.instance === "vscode"; } catch (e) { return false; }
}

// Starts the extension's own Phoenix Client (no window; approved 2026-10-22): only Windows PowerShell 5.1 running the
// fixed script src\run-phoenix-vscode.ps1 of the Phoenix folder that last ran (phoenix-root.txt, written by run.ps1).
// It stops by itself about 30 s after the last VS Code chat is gone. Returns an error text, or "" when started.
let startedAt = 0;
function startClient() {
  if (Date.now() - startedAt < 90000) return "";   // already starting (compiling takes a few seconds)
  let root;
  try { root = fs.readFileSync(path.join(process.env.LOCALAPPDATA || "", "LocalLLMStudio", "phoenix-root.txt"), "utf8").trim(); }
  catch (e) { return "Run Phoenix_Client or Phoenix_LAI_Server once, so the extension knows where Phoenix is."; }
  const script = path.join(root, "src", "run-phoenix-vscode.ps1");
  if (!path.isAbsolute(root) || !fs.existsSync(script)) return "Phoenix is no longer at " + root + ". Run Phoenix_Client once from its new folder.";
  const ps = path.join(process.env.SystemRoot || "C:\\Windows", "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
  try {
    // not detached: a detached powershell.exe (no console) exits at once without running the script
    const child = cp.spawn(ps, ["-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-File", script],
      { cwd: root, windowsHide: true, stdio: "ignore" });
    child.on("error", () => { startedAt = 0; });
    child.unref();
  } catch (e) { return "Could not start Phoenix: " + e.message; }
  startedAt = Date.now();
  return "";
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
    // Follow our client: if it stops, start it again; once it is up, connect.
    const watch = setInterval(async () => {
      if (!this.view) return;
      const up = this.port ? await alive(this.port) : false;
      if (this.port && !up) { this.port = 0; this.view.webview.html = page(null, startClient()); }
      else if (!this.port && (await findChatPort())) this.refresh();
    }, 2000);
    view.onDidDispose(() => clearInterval(watch));
  }

  async refresh() {
    if (!this.view) return;
    this.port = await findChatPort();
    if (!this.port) {
      this.view.webview.html = page(null, startClient());
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

function page(port, problem) {
  if (!port) {
    const esc = (t) => String(t).replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" })[c]);
    return `<!doctype html><html><body style="font-family:var(--vscode-font-family);color:var(--vscode-foreground);padding:12px">` +
      (problem ? `<p><b>Phoenix could not start.</b></p><p>${esc(problem)}</p>`
        : `<p><b>Starting Phoenix…</b></p><p>This chat runs its own Phoenix Client (no window needed). Models come from its providers; start <code>Phoenix_LAI_Server</code> for the local ones.</p>`) +
      `</body></html>`;
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
      await request(API_PORT, "POST", "/v1/completions",
        { model: "phoenixlai", prompt: prefix, suffix, max_tokens: 96, temperature: 0.1, stream: true, stop: ["\n\n\n"] },
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
      if (!port) { vscode.window.showWarningMessage("Phoenix is still starting; try again in a moment."); chat.refresh(); return; }
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
