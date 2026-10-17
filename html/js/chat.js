const $ = id => document.getElementById(id);
// promptInfo is kept as a reference: while a reply streams the prompt line is out of the page, so $() can't find it.
const chatEl = $('chat'), input = $('input'), promptLine = $('promptLine'), promptInfo = $('promptInfo');
const mirror = $('mirror');
let history = [], info = {}, models = [], controller = null, filesAt = '.';
const DEFAULT_SYSTEM = 'You are a concise, expert assistant running locally.';
const settings = { temperature: 0.7, top_k: 40, top_p: 0.95, repeat_penalty: 1.1, max_tokens: 2048, system: DEFAULT_SYSTEM, pretype: true, typeEffect: true };
try { Object.assign(settings, JSON.parse(localStorage.getItem('phoenix.settings') || '{}')); } catch (e) {}
const save = () => { try { localStorage.setItem('phoenix.settings', JSON.stringify(settings)); } catch (e) {} };

const ICON_DIR = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>';
const ICON_FILE = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6"/></svg>';

function fmtSize(n) { return n < 1024 ? n + ' B' : n < 1048576 ? (n / 1024).toFixed(1) + ' KB' : (n / 1048576).toFixed(1) + ' MB'; }

async function api(path, body) {
  const opt = body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) };
  const r = await fetch(path, opt);
  const j = await r.json();
  if (!r.ok || j.error) throw new Error(j.error || r.statusText);
  return j;
}

chatEl.addEventListener('click', e => {
  const src = e.target.closest('.sources');
  if (src) { src.classList.toggle('open'); return; }
  const btn = e.target.closest('.copy');
  if (!btn) return;
  const pre = btn.closest('.code').querySelector('pre');
  navigator.clipboard.writeText(pre.textContent).then(() => { btn.innerHTML = CHECK_ICON; btn.classList.add('done'); setTimeout(() => { btn.innerHTML = COPY_ICON; btn.classList.remove('done'); }, 1200); });
});

function scrollDown() { if (glide) glide.delete(chatEl); chatEl.scrollTop = chatEl.scrollHeight; }

// Status line: one centered row of icon + value items separated by "|". Empty items are left out.
const SICON = {
  ram: '<rect x="3" y="7" width="18" height="10" rx="1.5"/><path d="M7 7v10M11 7v10M15 7v10M6 17v3M10 17v3M14 17v3M18 17v3"/>',
  folder: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  branch: '<circle cx="6" cy="5" r="2"/><circle cx="6" cy="19" r="2"/><circle cx="18" cy="8" r="2"/><path d="M6 7v10M18 10c0 4-6 3-11 7"/>',
  changes: '<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/>',
  speed: '<path d="M13 2 3 14h9l-1 8 10-12h-9z"/>',
  model: '<path d="m12 2 9 5v10l-9 5-9-5V7z"/><path d="m3 7 9 5 9-5M12 12v10"/>',
  gpu: '<rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M9 1v3M15 1v3M9 20v3M15 20v3M20 9h3M20 14h3M1 9h3M1 14h3"/>',
  ctx: '<path d="m12 2 10 5-10 5L2 7z"/><path d="m2 17 10 5 10-5M2 12l10 5 10-5"/>',
  rules: '<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6M8 13h8M8 17h5"/>',
  rag: '<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V3H6.5A2.5 2.5 0 0 0 4 5.5z"/><path d="M4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5"/>'
};
let lastTps = '', ctxUsed = 0;
// Generation speed shown in the status bar: the average over the last 25 replies, recalculated after each one.
const tpsHistory = [];
function recordTps(tps) {
  if (!(tps > 0)) return;
  tpsHistory.push(tps); if (tpsHistory.length > 25) tpsHistory.shift();
  lastTps = (tpsHistory.reduce((a, b) => a + b, 0) / tpsHistory.length).toFixed(1);
}
function statusItem(icon, text, lower, cls) {
  return '<span class="item' + (lower ? ' lower' : '') + (cls ? ' ' + cls : '') + '"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
         'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' + SICON[icon] + '</svg>' + esc(text) + '</span>';
}
function drawStatus(offline) {
  // Bottom bar: system state (git, rules, RAG, memory, GPU). Under the prompt: what this chat uses (folder, speed,
  // context, model), so the bottom bar keeps room.
  const items = [], near = [];
  if (offline) items.push(statusItem('model', 'server offline'));
  else {
    // Folder: green = connected, orange = access on but nothing connected, grey = folder access off.
    if (info.folderAccess === false) near.push(statusItem('folder', 'folder access off', false, 'off'));
    else near.push(statusItem('folder', info.folder ? info.folder : 'no folder', false, info.folder ? 'ok' : 'gold'));
    const g = info.git;
    if (g) {
      items.push(statusItem('branch', (g.branch || 'detached') + (g.commit ? ' @' + g.commit : '')));
      if (g.changed !== null && g.changed !== undefined) items.push(statusItem('changes', g.changed ? g.changed + ' changed' : 'clean'));
    }
    if (info.rules && info.rules.length) items.push(statusItem('rules', 'rules: ' + info.rules.join(' + ')));
    const rg = info.rag;
    // RAG: grey = off, green = indexed with vectors (hybrid search working), orange = indexing, not indexed,
    // keyword-only (no embedding model) or an error.
    if (rg) {
      const docs = rg.chunks ? rg.files + ' docs · ' + rg.chunks + ' chunks' : 'not indexed';
      if (!rg.enabled) items.push(statusItem('rag', 'rag off' + (rg.chunks ? ': ' + docs : ''), false, 'off'));
      else if (rg.indexing) items.push(statusItem('rag', 'indexing: ' + rg.indexing, false, 'warn'));
      else if (rg.error) items.push(statusItem('rag', 'rag error: ' + rg.error, false, 'warn'));
      else if (!rg.chunks) items.push(statusItem('rag', 'rag: not indexed (/index)', false, 'warn'));
      else if (!rg.vectors) items.push(statusItem('rag', 'rag: ' + docs + ' · keywords only', false, 'warn'));
      else items.push(statusItem('rag', 'rag: ' + docs + ' · hybrid', false, 'ok'));
    }
    if (info.recall && (info.recall.busy || info.recall.files)) near.push(statusItem('rag', info.recall.busy ? 'recall: ' + info.recall.busy : 'recall: ' + info.recall.files + ' files', false, info.recall.busy ? 'gold' : 'ok'));
    if (info.mode === 'accept') near.push(statusItem('rules', 'mode: accept', false, 'gold'));
    if (lastTps) near.push(statusItem('speed', lastTps + ' t/s avg', false, 'ok'));
    // Context: green < 70% used, gold < 90%, red beyond.
    if (info.file) {
      const f = info.context ? ctxUsed / info.context : 0;
      near.push(statusItem('ctx', ctxUsed.toLocaleString() + '/' + (info.context || 0).toLocaleString() + ' tokens', false, f < 0.7 ? 'ok' : f < 0.9 ? 'gold' : 'bad'));
    }
    // Memory the model lives in: VRAM in GPU mode, system RAM in CPU mode, as "this app uses / this app could have"
    // (both live: the right side drops when other programs take memory). Gold while a model loads, green when
    // loaded, red after a failed load, grey with no model.
    const gb = b => (b / 1073741824).toFixed(1);
    const cpu = info.device === 'cpu', mem = cpu ? 'ram' : 'vram', micon = cpu ? 'ram' : 'gpu';
    if (info.loading) items.push(statusItem(micon, mem + ': loading…', false, 'gold'));
    else if (info.loadError) items.push(statusItem(micon, mem + ': load failed', false, 'bad'));
    else if (cpu && info.ram && info.file) items.push(statusItem(micon, 'ram ' + gb(info.ram.used) + '/' + gb(info.ram.avail) + ' GB', false, 'ok'));
    else if (!cpu && info.vram) items.push(statusItem(micon, 'vram ' + gb(info.vram.used) + '/' + gb(info.vram.budget) + ' GB', false, 'ok'));
    else items.push(statusItem(micon, mem + ': no model', false, 'off'));
    // Model: gold while loading, green when loaded in memory, red after a failed load, grey with none.
    const mname = f => f.replace(/\.gguf$/i, '');
    if (info.loading) near.push(statusItem('model', 'loading ' + mname(info.loading) + '…', true, 'gold'));
    else if (info.file) near.push(statusItem('model', mname(info.file), true, 'ok'));
    else if (info.loadError) near.push(statusItem('model', 'model error', true, 'bad'));
    else near.push(statusItem('model', 'no model', true, 'off'));
    // Video card (GPU mode only): gold while detecting (model loading), green when the model runs on it, red on an
    // error or an unwanted CPU fallback, grey when idle.
    const onGpu = info.backend && /^GPU/.test(info.backend);
    if (cpu) { /* nothing: the RAM item says it all */ }
    else if (info.loading) items.push(statusItem('gpu', 'detecting gpu…', true, 'gold'));
    else if (info.loadError) items.push(statusItem('gpu', 'gpu error', true, 'bad'));
    else if (onGpu) items.push(statusItem('gpu', info.backend.replace(/^GPU: /, ''), true, 'ok'));
    else if (info.backend) items.push(statusItem('gpu', 'gpu unavailable: on cpu', true, 'bad'));
    else items.push(statusItem('gpu', 'gpu idle', true, 'off'));
  }
  $('status').innerHTML = items.join('<span class="sep">|</span>');
  promptInfo.innerHTML = near.join('<span class="sep">|</span>');
}
// This window's id: the server counts open windows by it, so launching the app again reuses this window.
const PAGE_ID = (window.PHX_VSCODE ? 'vs-' : '') + Math.random().toString(36).slice(2, 12);   // vs-: the VS Code chat, not an app window
window.addEventListener('pagehide', () => navigator.sendBeacon('/api/bye?page=' + PAGE_ID));
// Launched again while this window is open: come to the front (and flash the title in case Windows blocks that).
function bringForward() {
  window.focus();
  if (document.hasFocus()) { input.focus(); return; }
  const title = document.title; let n = 0;
  const t = setInterval(() => { document.title = n++ % 2 ? title : '● ' + title; if (n > 9 || document.hasFocus()) { clearInterval(t); document.title = title; } }, 500);
}
async function refresh() {
  try { info = await api('/api/info?page=' + PAGE_ID); } catch (e) { drawStatus(true); return; }
  if (info.focus) bringForward();
  drawStatus(false);
  const ver = $('ver'); if (ver && info.version) ver.textContent = 'v.' + info.version;
  if (info.version) document.title = 'Phoenix LAI v.' + info.version;
  if (info.loading || (info.rag && info.rag.indexing)) setTimeout(refresh, 700);
}
// Keep the status line (git changes, model state) current.
setInterval(() => { if (!controller) refresh(); }, 2000);

// ---------- folder panel (above the prompt) ----------
async function showFiles(path) {
  const box = $('files');
  if (!info.folder) { box.classList.remove('show'); return; }
  filesAt = path || '.';
  let entries = [];
  try { entries = (await api('/api/list?path=' + encodeURIComponent(filesAt))).entries; } catch (e) { box.innerHTML = '<div class="err" style="padding:8px">' + esc(e.message) + '</div>'; box.classList.add('show'); return; }
  let html = '<div class="head"><span><b>' + esc(info.folderName) + '</b>' + (filesAt !== '.' ? ' / ' + esc(filesAt) : '') + '</span><span>' + entries.length + ' items</span></div>';
  if (filesAt !== '.') {
    const up = filesAt.includes('/') ? filesAt.slice(0, filesAt.lastIndexOf('/')) : '.';
    html += '<div class="item" data-dir="' + esc(up) + '">' + ICON_DIR + '..</div>';
  }
  for (const e of entries)
    html += e.dir ? '<div class="item" data-dir="' + esc(e.path) + '">' + ICON_DIR + esc(e.name) + '/</div>'
                  : '<div class="item" data-file="' + esc(e.path) + '" title="Insert path">' + ICON_FILE + esc(e.name) + '<span class="size">' + fmtSize(e.size) + '</span></div>';
  box.innerHTML = html;
  box.classList.add('show');
}
$('files').addEventListener('click', e => {
  const item = e.target.closest('.item');
  if (!item) return;
  if (item.dataset.dir !== undefined) showFiles(item.dataset.dir);
  else { input.value += (input.value && !/\s$/.test(input.value) ? ' ' : '') + item.dataset.file; placePrompt(); input.focus(); autosize(); }
});

// ---------- folder ----------
// /folder /<project>[/sub] connects a folder inside the fixed projects root (<home>\Phoenix_Projects); /folder close
// disconnects; /folder on|off is the master switch (off = no folder access at all); /folder alone shows the state.
// Absolute paths are refused by the server with an explanation.
async function setFolder(arg) {
  if (!arg) {
    if (info.folderAccess === false) { systemLine('Folder access is OFF: the model has no file access. Use /folder on to allow it.'); return; }
    systemLine(info.folder ? 'Folder: ' + info.folder + '  (/folder close to disconnect, /folder off to disable folder access)' : 'Folder access is on, no folder connected. Use /folder <project_folder>, e.g. /folder my-app or /folder my-app/src (projects live in ' + (info.projectsRoot || 'Phoenix_Projects') + ')'); return;
  }
  let path = arg.trim().replace(/^["']|["']$/g, '');
  // "on my-app" / "my-app on": switch access on and connect in one go (the project may be written with or without a /)
  const both = /^on\s+(.+)$/i.exec(path) || /^(.+?)\s+on$/i.exec(path);
  if (both && info.folderAccess === false) {
    try { info = await api('/api/folder-access', { on: true }); } catch (e) { systemLine('Could not change folder access: ' + e.message); return; }
  }
  if (both) path = both[1].trim().replace(/^["']|["']$/g, '');
  if (/^(on|off)$/i.test(path)) {
    const on = path.toLowerCase() === 'on';
    try { info = await api('/api/folder-access', { on }); } catch (e) { systemLine('Could not change folder access: ' + e.message); return; }
    await refresh();
    if (!on) $('files').classList.remove('show');
    systemLine(on ? 'Folder access ON. Connect a project with /folder <project_folder>.' : 'Folder access OFF: folder disconnected and all file access disabled.');
    return;
  }
  if (info.folderAccess === false) { systemLine('Folder access is OFF. Use /folder on first.'); return; }
  const off = /^(close|none|disconnect)$/i.test(path);
  try { info = await api('/api/folder', { path: off ? '' : path }); }
  catch (e) {
    const name = path.replace(/^[\\/]+/, '');
    systemLine(/^No folder/.test(e.message) ? 'There is no project folder "' + name + '" in ' + (info.projectsRoot || 'Phoenix_Projects') + '. Create it there first, then run /folder ' + name + ' again.'
                                            : 'Could not connect: ' + e.message);
    return;
  }
  await refresh();
  if (off) { $('files').classList.remove('show'); systemLine('Folder disconnected.'); }
  else { systemLine('Connected ' + info.folder + ' — the model can now list, read and write files there.'); loadRecentFolders(); }
}

// Welcome screen: shown on start and after every New chat, with the prompt line under it.
function welcome() {
  promptLine.remove();
  chatEl.innerHTML = '<div class="empty" id="empty"><img class="logo" src="assets/dark_phoenix_logo.png" alt="Phoenix LAI">' +
    // The empty .sub line keeps the hint where it always was; the version hangs below it without moving anything.
    '<div class="sub">&nbsp;</div>' +
    '<div class="hint">Everything runs locally on this machine. Use /help to list all commands.' +
    '<div class="ver" id="ver">' + (info.version ? 'v.' + esc(info.version) : '') + '</div></div></div>';
  placePrompt();
}
// The prompt always sits at the end of the conversation.
function placePrompt() { chatEl.appendChild(promptLine); drawPrompt(); scrollDown(); }
function newChat() { if (controller) controller.abort(); history = []; ctxUsed = 0; forgetChat(); if (info.file) drawStatus(false); welcome(); document.querySelectorAll('.modal.show').forEach(m => m.classList.remove('show')); input.focus(); }

// The conversation survives a reload (F5) of this window: what is on screen, the history sent to the model and the
// speed/context numbers are kept in sessionStorage (per window, gone when it closes). Only /new starts over.
const SAVE_KEY = 'phx.chat';
function saveChat() {
  try {
    const keep = promptLine.isConnected; if (keep) promptLine.remove();
    sessionStorage.setItem(SAVE_KEY, JSON.stringify({ html: chatEl.innerHTML, history, ctxUsed, tps: tpsHistory }));
    if (keep) chatEl.appendChild(promptLine);
  } catch (e) {}
}
function forgetChat() { try { sessionStorage.removeItem(SAVE_KEY); } catch (e) {} }
function restoreChat() {
  try {
    const s = JSON.parse(sessionStorage.getItem(SAVE_KEY) || 'null');
    if (!s || !s.html) return false;
    promptLine.remove();
    chatEl.innerHTML = s.html;
    chatEl.querySelectorAll('.caret').forEach(c => c.remove());
    chatEl.querySelectorAll('.work.busy').forEach(w => w.classList.remove('busy'));
    history = s.history || []; ctxUsed = s.ctxUsed || 0;
    (s.tps || []).forEach(t => recordTps(t));
    placePrompt();
    return true;
  } catch (e) { return false; }
}
window.addEventListener('pagehide', saveChat);
if (!restoreChat()) welcome();

// No context menus at all (custom or the browser's): everything is a /command. Copy with Ctrl+C, stop with Esc.
document.addEventListener('contextmenu', e => e.preventDefault());
// Clicking anywhere in the conversation (without selecting text) puts you back at the prompt.
chatEl.addEventListener('mouseup', e => {
  if (e.button !== 0 || window.getSelection().toString() || e.target.closest('button, a, .code')) return;
  if (promptLine.isConnected) input.focus();
});
document.addEventListener('keydown', e => {
  if (e.key === 'Escape') {
    if (controller) controller.abort();
    if ($('settingsModal').classList.contains('show')) closeSettings(false);
    document.querySelectorAll('.modal.show').forEach(m => m.classList.remove('show'));
  }
});

// ---------- approvals (/mode accept) ----------
// One bar above the prompt per pending file action: what the model wants to do, Accept (green) or Deny (red).
function askApproval(a) {
  const what = a.op === 'write' ? 'write ' + a.path + ' (' + (a.bytes || 0).toLocaleString() + ' bytes)' : a.op === 'delete' ? 'delete ' + a.path : a.op === 'edit' ? 'edit ' + a.path : a.op === 'search' ? 'search the project' + (a.path && a.path !== '.' ? ' (' + a.path + ')' : '') : a.op === 'read' ? 'read ' + a.path : 'list ' + (a.path === '.' ? 'the project folder' : a.path);
  const bar = document.createElement('div'); bar.className = 'approve';
  bar.innerHTML = '<span class="q">The model wants to <b>' + esc(what) + '</b></span><button class="yes">Accept</button><button class="no">Deny</button>';
  chatEl.insertBefore(bar, promptLine.isConnected ? promptLine : null); scrollDown();
  const answer = async allow => {
    bar.querySelectorAll('button').forEach(b => b.disabled = true);
    bar.classList.add(allow ? 'accepted' : 'denied');
    bar.querySelector('.q').innerHTML = (allow ? '✓ Accepted: ' : '✗ Denied: ') + esc(what);
    try { await api('/api/approve', { id: a.id, allow }); } catch (e) {}
    setTimeout(() => bar.remove(), 1500);
  };
  bar.querySelector('.yes').onclick = () => answer(true);
  bar.querySelector('.no').onclick = () => answer(false);
}

// ---------- chat ----------
// Notices keep the welcome screen (and the prompt's place under it); they sit just above the prompt.
function systemLine(text) {
  const d = document.createElement('div'); d.className = 'meta'; d.style.marginBottom = '18px'; d.style.whiteSpace = 'pre-wrap'; d.textContent = text;
  chatEl.insertBefore(d, promptLine.isConnected ? promptLine : null); scrollDown();
  saveChat();
}

// Docs library: /index rebuilds what changed; /rag on|off; /rag <words> previews what a message would retrieve.
async function reindex() {
  let r;
  try { r = await api('/api/index', {}); } catch (e) { systemLine('Index failed: ' + e.message); return; }
  systemLine('Indexing docs…');
  while (r.indexing) { await new Promise(res => setTimeout(res, 400)); try { r = (await api('/api/info')).rag; } catch (e) { break; } }
  systemLine(r.error ? 'Index failed: ' + r.error : 'Docs index ready: ' + r.last + '.');
  refresh();
}
// Slash commands, A-Z. `use` is the full syntax shown by /help; `arg` = takes arguments (completion leaves a space).
// Model, sampling, docs and browser settings live in the /settings window.
// The last 10 connected folders (kept by the server), offered when you type "/folder ".
let recentFolders = [];
async function loadRecentFolders() { try { recentFolders = (await api('/api/recent-folders')).folders; } catch (e) {} }
loadRecentFolders();
// Chat models in the models folder (embedding models, used for docs search, can't chat). Refreshed when needed.
let chatModels = [];
async function loadModelList() { try { chatModels = (await api('/api/models')).models.filter(m => !/embed/i.test(m.name)); } catch (e) {} }
loadModelList();
async function loadModel(name, ctx) {
  systemLine('Loading ' + name + (ctx ? ', context ' + ctx : '') + '…');
  setTimeout(refresh, 300);
  const t0 = performance.now();
  try { info = await api('/api/load', { name, context: ctx === undefined ? (info.contextAuto ? 0 : info.context) : ctx }); }
  catch (e) { systemLine('Could not load: ' + e.message); return false; }
  systemLine('Loaded ' + info.model + ' on ' + info.backend + ' in ' + ((performance.now() - t0) / 1000).toFixed(1) + ' s, context ' + Number(info.context).toLocaleString() + (info.contextAuto ? ' (auto)' : '') + '.');
  if (info.memWarning) systemLine('⚠ ' + info.memWarning);
  history = []; ctxUsed = 0; refresh();
  return true;
}
const COMMANDS = [
  { name: '/files', use: '/files', desc: 'show or hide the connected folder\'s files', run: () => { if (!info.folder) systemLine('No folder connected — use /folder.'); else if ($('files').classList.contains('show')) $('files').classList.remove('show'); else showFiles(filesAt); } },
  { name: '/folder', use: '/folder <project_folder>  on|off', desc: 'connect a project in Phoenix_Projects (close = disconnect; on|off = allow or fully disable folder access)', arg: true,
    options: () => [{ value: 'on', desc: 'allow folder access' + (info.folderAccess !== false ? ' (current)' : '') }, { value: 'off', desc: 'disable all folder access' + (info.folderAccess === false ? ' (current)' : '') }]
      .concat(info.folderAccess === false ? [] : [{ value: 'close', desc: 'disconnect the current folder' }].concat(recentFolders.map((f, i) => ({ value: f, desc: f === info.folder ? 'connected' : i === 0 ? 'last used' : '' })))),
    run: arg => setFolder(arg) },
  { name: '/help', use: '/help', desc: 'list all commands with full syntax', run: () => systemLine('Commands:\n' + COMMANDS.map(c => '  ' + c.use + '  —  ' + c.desc).join('\n') + '\nKeys: Esc stops a reply · Ctrl+C copies selected text.') },
  { name: '/index', use: '/index', desc: 'reindex new and changed files in docs\\', run: () => reindex() },
  { name: '/model', use: '/model <name>', desc: 'switch model (Tab completes names from the models folder)', arg: true,
    options: () => chatModels.map(m => ({ value: m.name.replace(/\.gguf$/i, ''), desc: (m.mb / 1024).toFixed(1) + ' GB' + (m.name === info.file ? ' · loaded' : '') })),
    run: async arg => {
      await loadModelList();
      if (!arg) { systemLine('Models: ' + chatModels.map(m => m.name.replace(/\.gguf$/i, '') + (m.name === info.file ? ' (loaded)' : '')).join(', ')); return; }
      const a = arg.toLowerCase().replace(/\.gguf$/, '');
      const m = chatModels.find(x => x.name.toLowerCase().replace(/\.gguf$/, '') === a) || chatModels.find(x => x.name.toLowerCase().includes(a));
      if (!m) { systemLine('No model matches "' + arg + '". Type /model and a space to see the list.'); return; }
      if (m.name === info.file) { systemLine(m.name.replace(/\.gguf$/i, '') + ' is already loaded.'); return; }
      await loadModel(m.name);
    } },
  { name: '/new', use: '/new', desc: 'start a new chat', run: () => newChat() },
  { name: '/rag', use: '/rag [on | off | <words>]', desc: 'docs status, turn retrieval on/off, or preview a search', arg: true,
    subs: [{ name: 'on' }, { name: 'off' }, { name: '<words>' }],
    run: async arg => {
      if (arg === 'on' || arg === 'off') { await api('/api/rag', { enabled: arg === 'on' }); systemLine('Docs retrieval ' + arg + '.'); refresh(); return; }
      if (!arg) { const r = (await api('/api/info')).rag; systemLine('Docs: ' + r.files + ' files, ' + r.chunks + ' chunks, retrieval ' + (r.enabled ? 'on' : 'off') + '. Commands: /index, /rag on|off, /rag <words>.'); return; }
      const r = await api('/api/rag?q=' + encodeURIComponent(arg));
      systemLine(r.hits.length ? r.hits.length + ' chunks in ' + r.ms + ' ms: ' + r.hits.map(h => '[' + h.n + '] ' + h.file + (h.title ? ' › ' + h.title : '') + ' (' + h.score + ')').join('  ') : 'No matching docs (' + r.ms + ' ms).');
    } },
  { name: '/recall', use: '/recall', desc: 'Instant Recall: pre-read every file of the project once, so the model can open them instantly later',
    run: async () => { await api('/api/recall', {}); systemLine('Instant Recall: pre-reading the project in the background (a chat message pauses it). Files read once open instantly afterwards, in any chat.'); refresh(); } },
  { name: '/mode', use: '/mode auto|accept', desc: 'accept = approve each file read / write the model wants to do; auto = no prompts', arg: true,
    options: () => [{ value: 'auto', desc: 'no prompts' + (info.mode !== 'accept' ? ' (current)' : '') }, { value: 'accept', desc: 'Accept / Deny every file action' + (info.mode === 'accept' ? ' (current)' : '') }],
    run: async arg => {
      const m = (arg || '').trim().toLowerCase();
      if (m !== 'auto' && m !== 'accept') { systemLine('Mode: ' + (info.mode || 'auto') + '. Use /mode auto or /mode accept.'); return; }
      info = await api('/api/mode', { mode: m }); refresh();
      systemLine(m === 'accept' ? 'Mode: accept. The model will ask before every file list, read or write (docs/RAG excluded).' : 'Mode: auto. File actions run without asking.');
    } },
  { name: '/debug', use: '/debug on|off', desc: 'debug logging: every step the server takes and every error, in the session log (logs\)', arg: true,
    run: async a => {
      const v = (a || '').trim().toLowerCase();
      if (v !== 'on' && v !== 'off') { systemLine('Debug logging is ' + (info.debug ? 'on' : 'off') + '. Use /debug on or /debug off.'); return; }
      try { await api('/api/debug', { on: v === 'on' }); await refresh(); systemLine('Debug logging ' + (v === 'on' ? 'on: every step and every error goes to the session log in logs\.' : 'off.')); }
      catch (e) { systemLine('Could not change debug logging: ' + e.message); }
    } },
  { name: '/device', use: '/device gpu|cpu', desc: 'run the model on the GPU or the CPU (reloads the current model)', arg: true,
    options: () => [{ value: 'gpu', desc: 'video card (fast)' + (info.device !== 'cpu' ? ' (current)' : '') }, { value: 'cpu', desc: 'turned off for now (GPU only)' }],
    run: async arg => {
      const d = (arg || '').trim().toLowerCase();
      if (d !== 'gpu' && d !== 'cpu') { systemLine('Running on ' + (info.device === 'cpu' ? 'CPU' : 'GPU') + (info.backend ? ' (' + info.backend + ')' : '') + '. Use /device gpu or /device cpu.'); return; }
      if (info.model) systemLine('Switching to ' + d.toUpperCase() + ' and reloading ' + info.file + '…');
      try { info = await api('/api/device', { device: d }); } catch (e) { systemLine('Could not switch: ' + e.message); return; }
      ctxUsed = 0; await refresh();
      systemLine('Now on ' + d.toUpperCase() + (info.backend ? ': ' + info.model + ' runs on ' + info.backend : ' (applies to the next model you load)') + '.');
    } },
  { name: '/unload', use: '/unload', desc: 'unload the model and free its video memory (/model loads one again)',
    run: async () => { info = await api('/api/unload', {}); history = []; ctxUsed = 0; systemLine('Model unloaded; its video memory is free.'); refresh(); } },
  { name: '/vram', use: '/vram', desc: 'video memory: what Phoenix LAI uses, what is free, and which programs use the most',
    run: async () => {
      const r = await api('/api/vram'), v = (await api('/api/info')).vram, gb = b => (b / 1073741824).toFixed(2) + ' GB';
      let txt = v ? 'Phoenix LAI uses ' + gb(v.used) + ' of the ' + gb(v.budget) + ' it may use now (the rest of the card is taken by other programs).\n' : 'No GPU model loaded.\n';
      if (r.processes) txt += 'Largest users of video memory:\n' + r.processes.map(p => '  ' + gb(p.bytes).padStart(9) + '  ' + p.name).join('\n');
      else if (r.error) txt += 'Per-program use unavailable: ' + r.error;
      systemLine(txt);
    } },
  { name: '/settings', use: '/settings', desc: 'model, context, sampling, system prompt, docs and browser', run: () => openSettings() }
];

async function command(text) {
  const sp = text.search(/\s/), cmd = sp < 0 ? text : text.slice(0, sp), arg = sp < 0 ? '' : text.slice(sp).trim();
  if (cmd === '/reindex') { await reindex(); return true; }
  const c = COMMANDS.find(c => c.name === cmd.toLowerCase());
  if (!c) { systemLine('Unknown command ' + cmd + ' — type /help for the list.'); return true; }
  await c.run(arg);
  return true;
}

// ---------- settings window ----------
// Chat fonts: the ones bundled in htmlonts plus fonts every Windows install has (nothing is downloaded).
const FONTS = [
  { id: 'syne', label: 'Syne Mono', css: '"Syne Mono", Consolas, monospace' },
  { id: 'exo', label: 'Exo 2', css: '"Exo 2", "Segoe UI", sans-serif' },
  { id: 'inter', label: 'Inter', css: 'Inter, "Segoe UI", sans-serif' },
  { id: 'roboto', label: 'Roboto', css: 'Roboto, "Segoe UI", sans-serif' },
  { id: 'consolas', label: 'Consolas', css: 'Consolas, monospace' },
  { id: 'cascadia', label: 'Cascadia Mono', css: '"Cascadia Mono", Consolas, monospace' },
  { id: 'segoe', label: 'Segoe UI', css: '"Segoe UI", sans-serif' }
];
const SIZES = [12, 13, 14, 15, 16, 18];
// Accent colours: gold (the default) first. Picking one recolours everything that was gold.
const ACCENTS = ['#e8c15a', '#f08a3c', '#e85a5a', '#e85aa8', '#a77bf0', '#5a8ce8', '#4fc3e8', '#3cc8a0', '#7ad35a', '#d8d8e0'];
function hexRgb(h) { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
function applyLook() {
  document.documentElement.classList.toggle('no-banner', !settings.banner);   // banner kept, off by default
  const acc = /^#[0-9a-f]{6}$/i.test(settings.accent || '') ? settings.accent : ACCENTS[0], rgb = hexRgb(acc), root = document.documentElement.style;
  root.setProperty('--accent', acc);
  root.setProperty('--accent-rgb', rgb.join(','));
  root.setProperty('--accent-2', 'rgb(' + rgb.map(c => Math.round(c + (255 - c) * 0.4)).join(',') + ')');   // lighter shade
  if (typeof drawBackground === 'function' && bgReady) drawBackground();   // the glows use the accent too
  const f = FONTS.find(x => x.id === settings.font) || FONTS[0];
  document.documentElement.style.setProperty('--chat', f.css);
  document.documentElement.style.setProperty('--chat-size', (settings.size || 14) + 'px');
  if (window.applyVscodeTheme) window.applyVscodeTheme();   // vscode.html: VS Code's theme wins over these settings
  alignChevrons();
}
// A row of radio buttons; `value` is the selected one, `onPick` previews a choice immediately.
function radios(box, items, value, onPick) {
  $(box).innerHTML = items.map(it => '<label' + (it.style ? ' style="' + it.style + '"' : '') + '><input type="radio" name="' + box + '" value="' + esc(it.value) + '"' +
    (String(it.value) === String(value) ? ' checked' : '') + '><span>' + esc(it.label) + '</span></label>').join('');
  $(box).onchange = e => onPick && onPick(e.target.value);
}
function drawSwatches() {
  const cur = (settings.accent || ACCENTS[0]).toLowerCase(), custom = ACCENTS.indexOf(cur) < 0;
  $('sAccent').innerHTML = ACCENTS.map((c, i) => '<button type="button" title="' + (i ? c : 'gold (default)') + '" data-c="' + c + '" style="--c:' + c + '"' + (c === cur ? ' class="on"' : '') + '></button>').join('') +
    '<label class="custom' + (custom ? ' on' : '') + '" title="Pick any colour"><input type="color" id="sAccentPick" value="' + cur + '"></label><span class="hex">' + cur + '</span>';
  $('sAccentPick').addEventListener('input', e => { settings.accent = e.target.value.toLowerCase(); applyLook(); $('sAccent').querySelector('.hex').textContent = settings.accent;
    $('sAccent').querySelectorAll('button').forEach(b => b.classList.remove('on')); $('sAccent').querySelector('.custom').classList.add('on'); });
}
$('sAccent').addEventListener('click', e => { const b = e.target.closest('button[data-c]'); if (!b) return; settings.accent = b.dataset.c; applyLook(); drawSwatches(); });
const radioValue = box => { const r = document.querySelector('#' + box + ' input:checked'); return r ? r.value : null; };
let openedBrowser = 'edge', lookBefore = null;
async function openSettings() {
  $('sContext').value = info.contextAuto ? '' : (info.context || '');
  $('sContext').placeholder = 'auto' + (info.contextAuto && info.context ? ' (now ' + info.context.toLocaleString() + ')' : ''); $('sMax').value = settings.max_tokens; $('sTemp').value = settings.temperature;
  $('sTopK').value = settings.top_k; $('sTopP').value = settings.top_p; $('sRepeat').value = settings.repeat_penalty; $('sSystem').value = settings.system;
  const rg = info.rag || {};
  $('sRag').checked = !!rg.enabled; $('ragNote').textContent = rg.chunks ? ' · ' + rg.files + ' docs, ' + rg.chunks + ' chunks' : rg.indexing ? ' · indexing: ' + rg.indexing : rg.error ? ' · index error: ' + rg.error : ' · not indexed: run /index';
  $('loadNote').textContent = '';
  lookBefore = { font: settings.font, size: settings.size, accent: settings.accent, banner: settings.banner };
  $('sPretype').checked = settings.pretype !== false; $('sPretype').onchange = () => { settings.pretype = $('sPretype').checked; save(); };
  $('sTypeEffect').checked = settings.typeEffect !== false; $('sTypeEffect').onchange = () => { settings.typeEffect = $('sTypeEffect').checked; save(); };
  $('sBanner').checked = !!settings.banner;
  $('sBanner').onchange = () => { settings.banner = $('sBanner').checked; applyLook(); };
  radios('sFont', FONTS.map(f => ({ value: f.id, label: f.label, style: 'font-family:' + f.css.replace(/"/g, "'") })), settings.font || 'syne', v => { settings.font = v; applyLook(); });
  radios('sSize', SIZES.map(n => ({ value: n, label: n + 'px' })), settings.size || 14, v => { settings.size = +v; applyLook(); });
  drawSwatches();
  // Chat models only: embedding models (used for docs search) can't hold a conversation.
  try { models = (await api('/api/models')).models.filter(m => !/embed/i.test(m.name)); } catch (e) { models = []; }
  $('modelList').innerHTML = models.map(m => '<label class="' + (m.name === info.file ? 'current' : '') + '"><input type="radio" name="model" value="' + esc(m.name) + '"' +
    (m.name === info.file ? ' checked' : '') + '><span class="name">' + esc(m.name.replace(/\.gguf$/i, '').toLowerCase()) + '</span><span class="mb">' + (m.mb / 1024).toFixed(1) + ' GB</span></label>').join('') ||
    '<div class="note">No .gguf files in the models folder.</div>';
  let b = { browser: 'auto', installed: true };
  try { b = await api('/api/browser'); } catch (e) {}
  openedBrowser = b.browser;
  radios('sKv', [{ value: 'q8', label: 'q8 (default)' }, { value: 'q4', label: 'q4' }, { value: 'f16', label: 'f16' }], info.kv || 'q8');
  // q8 / q4 only for models (and the GPU mode) that support them: otherwise greyed out, shown as f16, preference kept.
  const kvFor = name => {
    const m = models.find(x => x.name === name), ok = m ? m.kvq !== false : true, gpu = info.device !== 'cpu';
    $('sKv').querySelectorAll('input').forEach(inp => {
      const off = inp.value !== 'f16' && (!ok || !gpu);
      inp.disabled = off; inp.parentElement.classList.toggle('disabled', off);
      if (!ok || !gpu) inp.checked = inp.value === 'f16'; else inp.checked = inp.value === (info.kv || 'q8');
    });
    $('kvNote').textContent = !gpu ? 'CPU mode keeps the cache in f32.' : !ok ? 'This model keeps an f16 cache (q8 / q4 lose accuracy on it).' : '';
  };
  kvFor((document.querySelector('input[name=model]:checked') || {}).value || info.file);
  $('modelList').onchange = e => { if (e.target.name === 'model') kvFor(e.target.value); };
  radios('sBrowser', [{ value: 'chrome', label: 'Chrome' }, { value: 'edge', label: 'Edge' }, { value: 'auto', label: 'Auto' }], b.browser);
  $('browserNote').textContent = (b.browser === 'auto' ? 'Chrome first, Edge if Chrome is missing' + (b.used ? ' (now: ' + b.used + ')' : '') + ' · '
    : b.installed ? '' : b.fallback ? b.browser + ' is not installed, so ' + b.fallback + ' is used · ' : 'neither browser found · ') + 'used from the next start';
  $('settingsModal').classList.add('show');
  $('settingsBody').scrollTop = 0;
}
function closeSettings(applied) {
  if (!applied && lookBefore) { settings.font = lookBefore.font; settings.size = lookBefore.size; settings.accent = lookBefore.accent; settings.banner = lookBefore.banner; applyLook(); }   // undo the preview
  $('settingsModal').classList.remove('show');
}
// Category bar: jump to a section.
$('settingsCats').addEventListener('click', e => {
  const b = e.target.closest('button'); if (!b) return;
  const body = $('settingsBody'), sec = $(b.dataset.cat);
  body.scrollTo({ top: sec.offsetTop - body.offsetTop - 4, behavior: 'smooth' });
});
// Highlight the category being looked at.
$('settingsBody').addEventListener('scroll', () => {
  const body = $('settingsBody'); let cur = 'catModel';
  for (const sec of body.querySelectorAll('section')) if (sec.offsetTop - body.offsetTop - 30 <= body.scrollTop) cur = sec.id;
  if (body.scrollTop + body.clientHeight >= body.scrollHeight - 4) cur = 'catApp';
  document.querySelectorAll('#settingsCats button').forEach(x => x.classList.toggle('on', x.dataset.cat === cur));
});
$('closeSettings').addEventListener('click', () => closeSettings(false));
$('settingsNew').addEventListener('click', () => { closeSettings(true); save(); newChat(); });
$('applySettings').addEventListener('click', async () => {
  const num = (id, min, max, isInt) => { const v = Number($(id).value); return isFinite(v) && v >= min && v <= max && (!isInt || v === Math.floor(v)) ? v : null; };
  const vals = { max_tokens: num('sMax', 1, 32768, true), temperature: num('sTemp', 0, 2), top_k: num('sTopK', 0, 1000, true), top_p: num('sTopP', 0, 1), repeat_penalty: num('sRepeat', 1, 2) };
  // empty or "auto" = automatic context (largest that fits the free VRAM / RAM); 0 is sent for it
  const ctxText = $('sContext').value.trim().toLowerCase();
  const ctx = ctxText === '' || ctxText === 'auto' || ctxText === '0' ? 0 : num('sContext', 256, 131072, true);
  const bad = Object.keys(vals).filter(k => vals[k] === null).concat(ctx === null ? ['context'] : []);
  if (bad.length) { $('loadNote').textContent = 'Check: ' + bad.join(', ').replace(/_/g, ' ') + '.'; return; }
  Object.assign(settings, vals); settings.system = $('sSystem').value; save();
  try {
    await api('/api/rag', { enabled: $('sRag').checked });
    const kvIn = document.querySelector('#sKv input:checked');
    const kv = kvIn && !document.querySelector('#sKv input[value=q8]').disabled ? kvIn.value : null;   // greyed out: keep the preference
    if (kv && kv !== info.kv) { systemLine('KV cache ' + kv + (info.file ? ': reloading ' + info.file + '…' : '.')); info = await api('/api/kv', { kv }); }
    const br = radioValue('sBrowser');
    if (br && br !== openedBrowser) { await api('/api/browser', { browser: br }); systemLine('Browser set to ' + br + ' — used from the next start.'); }
  } catch (e) {}
  const sel = document.querySelector('input[name=model]:checked');
  closeSettings(true);
  const ctxChanged = ctx === 0 ? !info.contextAuto : (info.contextAuto || ctx !== info.context);
  if (sel && (sel.value !== info.file || ctxChanged)) await loadModel(sel.value, ctx);
  refresh();
});
$('settingsModal').addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.tagName !== 'TEXTAREA') { e.preventDefault(); $('applySettings').click(); } });

async function send() {
  const text = input.value.trim();
  if (!text || controller) return;
  if (text.startsWith('/')) {
    input.value = ''; autosize();
    let handled = false;
    try { handled = await command(text); } catch (e) { systemLine(e.message); handled = true; }
    if (handled) return;
    input.value = text; autosize();
  }
  if (!info.file) { systemLine('No model loaded — use /model to pick one.'); return; }
  input.value = ''; autosize();
  const empty = $('empty'); if (empty) empty.remove();
  // The prompt line (with its status row) stays in place at the bottom: the turn goes in above it.

  const turn = document.createElement('div'); turn.className = 'turn';
  turn.innerHTML = '<div class="user"></div>';
  turn.querySelector('.user').textContent = text;
  chatEl.insertBefore(turn, promptLine.isConnected ? promptLine : null);
  input.focus();
  let out = document.createElement('div'); out.className = 'reply'; turn.appendChild(out);
  // Typewriter: `reply` is everything received, `shown` how much is on screen. The cursor sits right after the last
  // shown character (also inside code boxes); it is solid while characters are appearing and blinks when paused.
  let reply = '', shown = 0, lastGrow = 0, raf = 0;
  const MARK = '\u0001';
  // Typing effect (settings.typeEffect): now and then a neighbouring key is "hit" by mistake and backspaced a moment
  // later. Purely on screen: the text received and kept is never changed. Only while the reveal is nearly caught up,
  // so it never slows a fast stream noticeably.
  let typo = null;   // { ch, left: ticks shown before the backspace }
  const NEAR = { a: 'sq', b: 'vn', c: 'xv', d: 'sf', e: 'wr', f: 'dg', g: 'fh', h: 'gj', i: 'uo', j: 'hk', k: 'jl', l: 'k', m: 'n', n: 'bm', o: 'ip', p: 'o', q: 'w', r: 'et', s: 'ad', t: 'ry', u: 'yi', v: 'cb', w: 'qe', x: 'zc', y: 'tu', z: 'x' };
  const draw = typing => {
    const html = render(reply.slice(0, shown) + (typo ? typo.ch : '') + MARK, true);
    out.innerHTML = html.replace(MARK, '<span class="caret' + (typing ? ' typing' : '') + '"></span>');
    scrollDown();
  };
  const tick = () => {
    if (typo) { if (--typo.left <= 0) typo = null; lastGrow = performance.now(); draw(true); return; }
    const backlog = reply.length - shown;
    if (backlog > 0) {
      const next = reply[shown];
      if (settings.typeEffect && backlog < 24 && /[a-z]/.test(next) && Math.random() < 0.012) {
        const n = NEAR[next] || 'e';
        typo = { ch: n[Math.floor(Math.random() * n.length)], left: 7 };   // ~110 ms visible, then backspaced
        draw(true); return;
      }
      shown += Math.max(1, Math.ceil(backlog / 6));   // catch up smoothly when tokens arrive in bursts
      lastGrow = performance.now();
      draw(true);
    } else if (performance.now() - lastGrow > 450) draw(false);
  };
  const flush = () => { shown = reply.length; typo = null; };
  const paint = () => {};
  draw(false);
  // A timer (not requestAnimationFrame) so the reveal keeps going when the window is hidden or minimized.
  raf = setInterval(tick, 16);

  // Code requests: after the reply (shown normally) its code is checked against the syntax reference. A collapsed
  // line under the reply shows it: "Checking against the C# syntax reference…" then "✓ Complete — …"; opening it shows
  // what the check said.
  const work = {
    box: null, log: null, label: null, text: '', lang: 'code',
    ensure() {
      if (this.box) return;
      this.box = document.createElement('details'); this.box.className = 'work';
      this.box.innerHTML = '<summary><span class="spin"></span><span class="lbl"></span></summary><pre class="worklog"></pre>';
      this.label = this.box.querySelector('.lbl'); this.log = this.box.querySelector('.worklog');
      out.after(this.box);
    },
    add(t) { this.ensure(); this.text += t; this.log.textContent = this.text; if (this.box.open) this.log.scrollTop = this.log.scrollHeight; },
    // green = looks good, gold = possible problems found (corrected) or no clear answer, red = definite syntax errors
    // (corrected). The label says whether the language's syntax reference (RAG) was used.
    phase(p, lang, usedRef) {
      this.ensure();
      if (lang) this.lang = lang;
      const sheet = this.lang === 'code' ? 'the syntax reference' : 'the ' + this.lang + ' syntax reference';
      const via = usedRef ? ' (checked against ' + sheet + ')' : ' (no ' + (this.lang === 'code' ? '' : this.lang + ' ') + 'reference sheet; checked by the model)';
      const labels = { checking: 'Checking against ' + sheet + '…', good: '✓ Looks good' + via, fixed: '⚠ Possible mistakes found and corrected' + via,
                       unsure: '⚠ Could not confirm the code is correct' + via, wrong: '✗ Syntax errors found and corrected' + via };
      this.label.textContent = labels[p] || p;
      this.box.classList.toggle('busy', p === 'checking');
      this.box.classList.remove('ok', 'warn', 'bad');
      if (p !== 'checking') this.box.classList.add(p === 'good' ? 'ok' : p === 'wrong' ? 'bad' : 'warn');
    },
    done() { if (this.box) this.box.classList.remove('busy'); }
  };

  // vscode.html adds the editor's context (open file, selection) to what the model sees; the chat shows only your text
  history.push({ role: 'user', content: text + (window.phxContext ? window.phxContext() : '') });
  const messages = settings.system ? [{ role: 'system', content: settings.system }, ...history] : [...history];
  controller = new AbortController();
  let appended = false, t0 = performance.now(), firstAt = 0, wrote = false, prog = null;
  const edited = [];   // files the model wrote or edited this turn (vscode.html checks them for errors)
  try {
    const body = { messages, temperature: settings.temperature, top_k: settings.top_k, top_p: settings.top_p,
                   repeat_penalty: settings.repeat_penalty, max_tokens: settings.max_tokens };
    const res = await fetch('/api/chat?page=' + PAGE_ID, { method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    const reader = res.body.getReader(), dec = new TextDecoder();
    let buf = '';
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      buf += dec.decode(value, { stream: true });
      let i;
      while ((i = buf.indexOf('\n\n')) >= 0) {
        const line = buf.slice(0, i); buf = buf.slice(i + 2);
        if (!line.startsWith('data: ')) continue;
        const ev = JSON.parse(line.slice(6));
        if (ev.pf !== undefined) {
          // reading the prompt (slow on the CPU): a dim progress line until the first token arrives
          if (!firstAt && ev.pf < 100) {
            if (!prog) { prog = document.createElement('div'); prog.className = 'meta'; turn.insertBefore(prog, out); }
            prog.textContent = 'reading prompt… ' + ev.pf + '%';
          } else if (prog) { prog.remove(); prog = null; }
        }
        else if (ev.guard) showGuard(ev.guard);
        else if (ev.t !== undefined) { if (prog) { prog.remove(); prog = null; } if (!firstAt) firstAt = performance.now(); reply += ev.t; }
        else if (ev.w !== undefined) work.add(ev.w);
        else if (ev.phase) { flush(); out.innerHTML = render(reply, false); work.phase(ev.phase, ev.lang, ev.ref); }
        else if (ev.approve) askApproval(ev.approve);
        else if (ev.replace !== undefined) { reply = ev.replace; shown = reply.length; out.innerHTML = render(reply, false); }
        else if (ev.rag) {
          const d = document.createElement('div'); d.className = 'sources';
          // One short line (files used); click it to see the exact sections that were added.
          const files = [...new Set(ev.rag.map(h => h.file))];
          d.innerHTML = '<b>docs</b> ' + ev.rag.length + ' section' + (ev.rag.length > 1 ? 's' : '') + ' from ' + files.map(esc).join(', ') +
            '<div class="detail">' + ev.rag.map(h => '[' + h.n + '] ' + esc(h.file) + (h.title ? ' › ' + esc(h.title) : '')).join('<br>') + '</div>';
          d.title = 'Click to show which sections';
          turn.insertBefore(d, out);
        } else if (ev.tool) {
          flush(); out.innerHTML = render(reply, false);
          const t = document.createElement('div'); t.className = 'tool' + (ev.tool.ok ? '' : ' fail');
          t.textContent = (ev.tool.ok ? '✓ ' : '✗ ') + ev.tool.msg; t.dataset.op = ev.tool.op; t.dataset.path = ev.tool.path; turn.appendChild(t);
          if (ev.tool.ok && (ev.tool.op === 'write' || ev.tool.op === 'edit') && !edited.includes(ev.tool.path)) edited.push(ev.tool.path);
          if ((ev.tool.op === 'write' || ev.tool.op === 'edit' || ev.tool.op === 'delete') && ev.tool.ok) wrote = true;
        } else if (ev.step) {
          flush(); out.innerHTML = render(reply, false);
          reply = ''; shown = 0; out = document.createElement('div'); out.className = 'reply'; turn.appendChild(out); draw(false);
        } else if (ev.error) {
          const er = document.createElement('div'); er.className = 'err'; er.textContent = ev.error; turn.appendChild(er);
        } else if (ev.done) {
          history.push(...ev.append); appended = true;
          const m = document.createElement('div'); m.className = 'meta';
          const secs = ((performance.now() - t0) / 1000).toFixed(1), ttft = firstAt ? ((firstAt - t0) / 1000).toFixed(2) : '-';
          m.innerHTML = '<b>' + ev.tps + ' t/s</b> · ' + ev.tokens + ' tokens · ' + secs + ' s · first token ' + ttft + ' s';
          if (!ev.tokens) m.style.display = 'none';   // a reply from the server itself (run guard): no model ran, no stats
          turn.appendChild(m);
          recordTps(ev.tps); if (ev.ctx_used !== undefined) ctxUsed = ev.ctx_used; drawStatus(false);
        }
      }
    }
  } catch (e) {
    const m = document.createElement('div'); m.className = 'meta'; m.textContent = e.name === 'AbortError' ? 'stopped' : 'error: ' + e.message; turn.appendChild(m);
  }
  // Let the typewriter finish revealing what arrived, then render the final text without a cursor.
  while ((shown < reply.length || typo) && !document.hidden && !(controller && controller.signal.aborted)) await new Promise(r => setTimeout(r, 16));
  typo = null;
  clearInterval(raf);
  work.done();   // never leave a spinner running (stopped or failed requests included)
  out.innerHTML = render(reply, false);
  if (!appended) history.push({ role: 'assistant', content: reply });
  controller = null;
  placePrompt(); input.focus();
  saveChat();
  if (wrote) { refresh(); if ($('files').classList.contains('show')) showFiles(filesAt); }
  if (window.phxTurnDone) window.phxTurnDone(edited, turn);
}

// Mirror the textarea with a blinking block cursor at the caret position (the textarea itself is invisible).
function drawPrompt() {
  const v = input.value, at = input.selectionEnd == null ? v.length : input.selectionEnd;
  const caret = '<span class="caret"></span>';
  if (!v) { mirror.innerHTML = caret; return; }
  // A zero-width space keeps a trailing newline from collapsing.
  mirror.innerHTML = esc(v.slice(0, at)) + caret + esc(v.slice(at)) + (v.endsWith('\n') ? '\u200b' : '');
}
// Restart the blink on every keystroke so the cursor is always visible while typing.
function restartBlink() { const c = mirror.querySelector('.caret'); if (c) { c.style.animation = 'none'; void c.offsetWidth; c.style.animation = ''; } }
function autosize() { drawPrompt(); restartBlink(); }
['input', 'keyup', 'click', 'select', 'focus'].forEach(ev => input.addEventListener(ev, autosize));
document.addEventListener('selectionchange', () => { if (document.activeElement === input) drawPrompt(); });
drawPrompt();

// Start typing anywhere and it goes into the prompt (unless a window or another field has focus).
document.addEventListener('keydown', e => {
  if (e.defaultPrevented || e.ctrlKey || e.altKey || e.metaKey || e.key.length !== 1) return;
  const a = document.activeElement;
  if (a === input || document.querySelector('.modal.show') || (a && (a.tagName === 'INPUT' || a.tagName === 'TEXTAREA' || a.isContentEditable))) return;
  if (!promptLine.isConnected) return;          // the model is still answering
  e.preventDefault();
  input.focus();
  const at = input.selectionEnd == null ? input.value.length : input.selectionEnd;
  input.setRangeText(e.key, at, at, 'end');
  autosize(); cmdSel = 0; drawCmds();
});
// Pasting anywhere goes into the prompt too.
document.addEventListener('paste', e => {
  const a = document.activeElement;
  if (a === input || document.querySelector('.modal.show') || (a && (a.tagName === 'INPUT' || a.tagName === 'TEXTAREA')) || !promptLine.isConnected) return;
  e.preventDefault();
  input.focus();
  const text = (e.clipboardData || window.clipboardData).getData('text');
  input.setRangeText(text, input.value.length, input.value.length, 'end');
  unquoteFolderPath(); autosize();
});
// Command list while typing "/name": filtered A-Z, arrows to move, Tab or click to complete, Enter to run.
const cmdBox = $('cmds');
let cmdMatches = [], cmdSel = 0, cmdNavigated = false;   // navigated: the user moved through the list with the arrows
// Every command plus its sub-commands (e.g. "/rag on"), A-Z. `run`: completing it can run at once.
const CMD_ENTRIES = [];
for (const c of COMMANDS) {
  // One row per command; sub-commands shown inline, e.g. "/rag on|off|<words>".
  CMD_ENTRIES.push({ name: c.name, label: c.subs ? c.name + ' ' + c.subs.map(s => s.name).join('|') : c.use, desc: c.desc, run: !c.arg, insert: c.name + (c.arg ? ' ' : '') });
}
function drawCmds() {
  const v = input.value.toLowerCase();
  const sp = v.indexOf(' '), cmd = sp < 0 ? null : COMMANDS.find(c => c.name === v.slice(0, sp));
  if (cmd && cmd.options) {
    // "/model ll" lists the matching values (e.g. model names); Tab or Enter picks one.
    const typed = v.slice(sp + 1).trim();
    // Re-read the models folder when the list opens, so newly added files show up.
    if ((cmd.name === '/model' || cmd.name === '/folder') && Date.now() - (drawCmds.fetched || 0) > 3000) {
      drawCmds.fetched = Date.now(); (cmd.name === '/model' ? loadModelList() : loadRecentFolders()).then(drawCmds);
    }
    cmdMatches = cmd.options().filter(o => o.value.toLowerCase().includes(typed))
      .map(o => ({ name: (cmd.name + ' ' + o.value).toLowerCase(), label: o.value, desc: o.desc, run: true, insert: cmd.name + ' ' + o.value, option: true }));
    if (cmdMatches.length === 1 && cmdMatches[0].name === v.trim()) cmdMatches = [];
  } else
  // "/ra" lists matching commands; "/rag " (with a space) keeps showing /rag and its options while you pick one.
  cmdMatches = /^\/\S*$/.test(v) ? CMD_ENTRIES.filter(e => e.name.startsWith(v))
             : /^\/\S+ $/.test(v) ? CMD_ENTRIES.filter(e => e.name === v.trim() && e.label !== e.name) : [];
  if (!cmdMatches.length) { cmdBox.classList.remove('show'); return; }
  cmdSel = Math.min(cmdSel, cmdMatches.length - 1);
  cmdBox.innerHTML = cmdMatches.map((c, i) => '<div class="c' + (i === cmdSel ? ' sel' : '') + '" data-i="' + i + '"><b>' + esc(c.label) + '</b>' + esc(c.desc) + '</div>').join('') +
    '<div class="foot">type /help for every command with its full syntax · Tab to complete</div>';
  cmdBox.classList.add('show');
}
function completeCmd(c) { input.value = c.insert; input.focus(); autosize(); drawCmds(); }
cmdBox.addEventListener('mousedown', e => { const el = e.target.closest('.c'); if (el) { e.preventDefault(); completeCmd(cmdMatches[+el.dataset.i]); } });
// A path pasted after /folder loses its surrounding quotes ("D:\My Project" -> D:\My Project) right in the prompt.
function unquoteFolderPath() {
  const m = input.value.match(/^(\/folder\s+)"([^"]*)"(\s*)$/i);
  if (m) { input.value = m[1] + m[2]; autosize(); }
}
input.addEventListener('input', () => { unquoteFolderPath(); cmdSel = 0; cmdNavigated = false; drawCmds(); schedulePretype(); });

// Answer while typing: after a 1.2 s pause the half-written message goes to the server, which reads it into the model and
// drafts an answer while you finish (settings.pretype). Sending stops the draft at once; the reply is never affected.
let pretypeTimer = 0, lastPretyped = '';
function schedulePretype() {
  clearTimeout(pretypeTimer);
  if (!settings.pretype) return;
  pretypeTimer = setTimeout(() => {
    const text = input.value.trim();
    if (controller || !info.file || text.length < 8 || text.startsWith('/') || text === lastPretyped) return;
    lastPretyped = text;
    const messages = settings.system ? [{ role: 'system', content: settings.system }, ...history, { role: 'user', content: text }] : [...history, { role: 'user', content: text }];
    fetch('/api/pretype', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ messages }) }).catch(() => {});
  }, 1200);
}
input.addEventListener('blur', () => setTimeout(() => { if (document.activeElement !== input) cmdBox.classList.remove('show'); }, 100));
input.addEventListener('keydown', e => {
  if (cmdBox.classList.contains('show')) {
    const c = cmdMatches[cmdSel];
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); cmdNavigated = true; cmdSel = (cmdSel + (e.key === 'ArrowDown' ? 1 : cmdMatches.length - 1)) % cmdMatches.length; drawCmds(); return; }
    if (e.key === 'Tab') { e.preventDefault(); completeCmd(c); return; }
    // Enter on a partly typed name picks the highlighted command; it runs at once unless it takes arguments.
    // A typed or pasted value (a path, a model name) is used as typed unless you picked a suggestion with the arrows.
    if (e.key === 'Enter' && !e.shiftKey && input.value.toLowerCase().trim() !== c.name && (!c.option || cmdNavigated)) { e.preventDefault(); completeCmd(c); if (!c.run) return; }
  }
  if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); cmdBox.classList.remove('show'); send(); }
});

// Keep the › exactly level with the words, whatever the chat font: measure the ink of "›" and of lowercase "x" on a canvas
// and shift the › so their vertical centres match (both are already on the same baseline).
function alignChevrons() {
  const cs = getComputedStyle(document.documentElement), ctx = document.createElement('canvas').getContext('2d');
  ctx.font = '600 24px ' + cs.getPropertyValue('--sans');
  const c = ctx.measureText('›'), chevMid = (c.actualBoundingBoxAscent - c.actualBoundingBoxDescent) / 2;
  ctx.font = getComputedStyle(mirror).fontSize + ' ' + getComputedStyle(mirror).fontFamily;
  const x = ctx.measureText('x'), textMid = (x.actualBoundingBoxAscent - x.actualBoundingBoxDescent) / 2;
  document.documentElement.style.setProperty('--chev-shift', (chevMid - textMid).toFixed(2) + 'px');
}
applyLook();
document.fonts.ready.then(alignChevrons);
document.fonts.addEventListener('loadingdone', alignChevrons);

refresh();
// Ready to type as soon as the page loads (and whenever the window comes back to the front).
window.addEventListener('load', () => input.focus());
window.addEventListener('focus', () => { if (promptLine.isConnected && !document.querySelector('.modal.show')) input.focus(); });
input.focus();

// Mouse wheel, everywhere (chat, windows, file list, code blocks), always smooth and with no scrollbars:
//   wheel = scroll up/down · Ctrl + wheel = up/down at 2x (instead of zooming) · Shift + wheel = left/right.
// The area under the mouse that can scroll that way is used; each glides to its target over a few frames.
var glide = new Map();     // element -> { x, y } target scroll position (var: scrollDown may run before this line)
function canScroll(el, horizontal) {
  const s = getComputedStyle(el), o = horizontal ? s.overflowX : s.overflowY;
  return (o === 'auto' || o === 'scroll') && (horizontal ? el.scrollWidth > el.clientWidth : el.scrollHeight > el.clientHeight);
}
// Next frame: animation frame normally, a timer if frames are paused (some hosts throttle them), whichever comes first.
let glideQueued = false;
function glideLater() {
  if (glideQueued) return;
  glideQueued = true;
  const go = () => { if (glideQueued) { glideQueued = false; glideStep(); } };
  requestAnimationFrame(go); setTimeout(go, 20);
}
function glideStep() {
  for (const [el, t] of glide) {
    const dx = t.x - el.scrollLeft, dy = t.y - el.scrollTop;
    if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5) { el.scrollLeft = t.x; el.scrollTop = t.y; glide.delete(el); continue; }
    el.scrollLeft += dx * 0.28; el.scrollTop += dy * 0.28;
    // Stopped by an edge: drop the rest so the next wheel turn starts from where we are.
    if (Math.abs(t.x - el.scrollLeft - dx) < 0.01 && Math.abs(t.y - el.scrollTop - dy) < 0.01) glide.delete(el);
  }
  if (glide.size) glideLater();
}
document.addEventListener('wheel', e => {
  const horizontal = e.shiftKey && !e.ctrlKey;
  let delta = (e.deltaY || e.deltaX) * (e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? innerHeight : 1);
  if (e.ctrlKey) delta *= 2;
  e.preventDefault();   // also stops Ctrl + wheel zoom and the browser's own scroll
  for (let el = e.target; el && el !== document.documentElement; el = el.parentElement) {
    if (!canScroll(el, horizontal)) continue;
    const t = glide.get(el) || { x: el.scrollLeft, y: el.scrollTop };
    if (horizontal) t.x = Math.max(0, Math.min(el.scrollWidth - el.clientWidth, t.x + delta));
    else t.y = Math.max(0, Math.min(el.scrollHeight - el.clientHeight, t.y + delta));
    glide.set(el, t);
    glideLater();
    return;
  }
}, { passive: false });

// Background: grid fading out toward the edges, two soft gold glows and a faint blurred monochrome logo, painted once
// per size. Redrawn only after a resize settles; while dragging, the canvas is just stretched (no per-frame repaint).
const bgLogo = new Image();
bgLogo.src = 'assets/light_phoenix_logo.png';
function drawBackground() {
  const cv = $('bg'), dpr = window.devicePixelRatio || 1, W = innerWidth, H = innerHeight;
  if (!W || !H) return;   // hidden or minimized: draw when it has a size again
  cv.width = Math.round(W * dpr); cv.height = Math.round(H * dpr);
  const g = cv.getContext('2d'), css = getComputedStyle(document.documentElement), v = n => css.getPropertyValue(n).trim();
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.fillStyle = v('--bg'); g.fillRect(0, 0, W, H);

  // Grid on its own layer, masked by an ellipse (85% x 75% radii at 50%/35%, solid to 40%, gone at 100%).
  const grid = document.createElement('canvas'); grid.width = cv.width; grid.height = cv.height;
  const q = grid.getContext('2d'); q.setTransform(dpr, 0, 0, dpr, 0, 0);
  const lines = (step, color) => {
    q.fillStyle = color;
    for (let x = 0; x < W; x += step) q.fillRect(x, 0, 1, H);
    for (let y = 0; y < H; y += step) q.fillRect(0, y, W, 1);
  };
  lines(32, v('--grid')); lines(160, v('--grid-strong'));
  q.globalCompositeOperation = 'destination-in';
  q.translate(W * 0.5, H * 0.35); q.scale(W * 0.85, H * 0.75);
  const m = q.createRadialGradient(0, 0, 0, 0, 0, 1);
  m.addColorStop(0.4, '#000'); m.addColorStop(1, 'rgba(0,0,0,0)');
  q.fillStyle = m; q.fillRect(-2, -2, 4, 4);
  g.setTransform(1, 0, 0, 1, 0, 0); g.drawImage(grid, 0, 0); g.setTransform(dpr, 0, 0, dpr, 0, 0);

  // Gold glows at the top and bottom (600x300 and 500x260 ellipses, fading out by 70%).
  const glow = (cx, cy, rx, ry, a) => {
    g.save(); g.translate(cx, cy); g.scale(rx, ry);
    const r = g.createRadialGradient(0, 0, 0, 0, 0, 1);
    const rgb = v('--accent-rgb');
    r.addColorStop(0, 'rgba(' + rgb + ',' + a + ')'); r.addColorStop(0.7, 'rgba(' + rgb + ',0)');
    g.fillStyle = r; g.fillRect(-1, -1, 2, 2); g.restore();
  };
  glow(W / 2, 0, 600, 300, 0.08); glow(W / 2, H, 500, 260, 0.06);

}

// Watermark logo: its own canvas, painted once at a fixed 820px width (grayscale, slightly brightened, 6px blur, 3.5% opacity) and
// pinned to the window centre by CSS, so it never scales and stays centred even mid-resize.
function drawLogo() {
  if (!bgLogo.naturalWidth) return;
  const cv = $('bgLogo'), dpr = window.devicePixelRatio || 1, w = 820, h = Math.round(w * bgLogo.naturalHeight / bgLogo.naturalWidth), pad = 8;
  cv.width = (w + 2 * pad) * dpr; cv.height = (h + 2 * pad) * dpr;
  cv.style.width = (w + 2 * pad) + 'px'; cv.style.height = (h + 2 * pad) + 'px';
  const g = cv.getContext('2d');
  g.setTransform(dpr, 0, 0, dpr, 0, 0);
  g.globalAlpha = 0.035; g.filter = 'grayscale(1) brightness(1.15) blur(6px)';
  g.drawImage(bgLogo, pad, pad, w, h);
}
var bgReady = true;
let bgTimer = 0;
window.addEventListener('resize', () => { clearTimeout(bgTimer); bgTimer = setTimeout(drawBackground, 150); });
bgLogo.onload = drawLogo;
if (bgLogo.complete) drawLogo();
drawBackground();

// Centre the banner's capital letters exactly: flexbox centres the font's line box, not the letters' ink, so measure the
// ink of the text and shift it by the difference.
function centerBanner() {
  const span = document.querySelector('#banner span'), cs = getComputedStyle(span), size = parseFloat(cs.fontSize);
  const g = document.createElement('canvas').getContext('2d');
  g.font = cs.fontWeight + ' ' + cs.fontSize + ' ' + cs.fontFamily;
  const m = g.measureText(span.textContent);
  const lineTop = (size - (m.fontBoundingBoxAscent + m.fontBoundingBoxDescent)) / 2;    // line-height 1: content area centred
  const baseline = lineTop + m.fontBoundingBoxAscent;
  const inkMid = baseline - (m.actualBoundingBoxAscent - m.actualBoundingBoxDescent) / 2;
  document.documentElement.style.setProperty('--banner-shift', (size / 2 - inkMid).toFixed(2) + 'px');
}
centerBanner();
document.fonts.ready.then(centerBanner);

// ---------- run guard: big red flashing WARNING banner ----------
// The server sends {guard: {level, seconds, text}} when someone keeps asking the model to run things: "warn" on the
// third request (closes itself after a while, or with ✕), "suspend" while chat is locked (counts down, then closes).
let guardTimer = null;
function showGuard(g) {
  let el = document.getElementById('guardBanner');
  if (!el) {
    el = document.createElement('div'); el.id = 'guardBanner';
    el.innerHTML = '<div class="gb-title">⚠ WARNING ⚠</div><div class="gb-text"></div><div class="gb-count"></div><button class="gb-x" title="Close">✕</button>';
    document.body.appendChild(el);
    el.querySelector('.gb-x').onclick = () => { el.classList.remove('show'); clearInterval(guardTimer); };
  }
  el.querySelector('.gb-text').textContent = g.text || '';
  el.classList.toggle('locked', g.level === 'suspend');
  el.querySelector('.gb-x').style.display = g.level === 'suspend' ? 'none' : '';
  el.classList.add('show');
  clearInterval(guardTimer);
  const count = el.querySelector('.gb-count');
  if (g.level === 'suspend' && g.seconds > 0) {
    const end = Date.now() + g.seconds * 1000;
    const tick = () => {
      const s = Math.max(0, Math.round((end - Date.now()) / 1000));
      count.textContent = 'Chat locked · ' + Math.floor(s / 60) + ':' + String(s % 60).padStart(2, '0');
      if (s <= 0) { clearInterval(guardTimer); el.classList.remove('show'); }
    };
    tick(); guardTimer = setInterval(tick, 1000);
  } else {
    count.textContent = '';
    guardTimer = setTimeout(() => el.classList.remove('show'), 12000);
  }
}
