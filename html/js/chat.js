const $ = id => document.getElementById(id);
// promptLine is kept as a reference: while a reply streams it is out of the page, so $() can't find it.
const chatEl = $('chat'), input = $('input'), promptLine = $('promptLine');
const mirror = $('mirror');
let history = [], info = {}, controller = null, filesAt = '.';
const settings = { pretype: true, typeEffect: true };
try { Object.assign(settings, JSON.parse(localStorage.getItem('phoenix.settings') || '{}')); } catch (e) {}
const save = () => { try { localStorage.setItem('phoenix.settings', JSON.stringify(settings)); } catch (e) {} };
// Each model's defaults come from the server (/api/models "defaults": Google's Gemma 4 sampling, reply limit and system
// prompt by model size); this is the fallback until the list arrives. An empty system prompt = the server's default.
const BASE_DEFAULTS = { temperature: 1.0, top_k: 64, top_p: 0.95, repeat_penalty: 1.0, max_tokens: 4096, system: '', context: 0, kv: 'q8' };
const MODEL_KEYS = ['temperature', 'top_k', 'top_p', 'repeat_penalty', 'max_tokens', 'system'];
// One-time move to per-model defaults: drop the old global sampling values and saved copies of the old defaults.
if (settings.defaultsV !== 2) {
  MODEL_KEYS.forEach(k => delete settings[k]);
  Object.values(settings.models || {}).forEach(c => MODEL_KEYS.forEach(k => delete c[k]));
  settings.defaultsV = 2; save();
}
function modelDefaults(name) { const m = chatModels.find(x => x.name === name); return Object.assign({}, BASE_DEFAULTS, m && m.defaults); }
// Settings per model, kept under settings.models by file name: only what differs from that model's defaults.
function ms(name) { return Object.assign(modelDefaults(name), (settings.models || {})[name] || {}); }

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

// Icons for the status items (top bar and left panel).
const SICON = {
  ram: '<rect x="3" y="7" width="18" height="10" rx="1.5"/><path d="M7 7v10M11 7v10M15 7v10M6 17v3M10 17v3M14 17v3M18 17v3"/>',
  speed: '<path d="M13 2 3 14h9l-1 8 10-12h-9z"/>',
  gpu: '<rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><path d="M9 1v3M15 1v3M9 20v3M15 20v3M20 9h3M20 14h3M1 9h3M1 14h3"/>',
  ctx: '<path d="m12 2 10 5-10 5L2 7z"/><path d="m2 17 10 5 10-5M2 12l10 5 10-5"/>',
  pc: '<rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/>',
  user: '<circle cx="12" cy="8" r="4"/><path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7"/>',
  rag: '<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V3H6.5A2.5 2.5 0 0 0 4 5.5z"/><path d="M4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5"/>'
};
let lastTps = '', ctxUsed = 0;
// Generation speed shown in the top bar: the average over the last 25 replies, recalculated after each one.
const tpsHistory = [];
function recordTps(tps) {
  if (!(tps > 0)) return;
  tpsHistory.push(tps); if (tpsHistory.length > 25) tpsHistory.shift();
  lastTps = (tpsHistory.reduce((a, b) => a + b, 0) / tpsHistory.length).toFixed(1);
}
function statusItem(icon, text, lower) {
  return '<span class="item' + (lower ? ' lower' : '') + '"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
         'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' + SICON[icon] + '</svg>' + esc(text) + '</span>';
}
function drawStatus(offline) {
  // Top bar: model drop-down, then speed | tokens | rag | memory. Left panel: the video card. All plain grey.
  const pDown = window.activeProviderDown && window.activeProviderDown();
  const tm = $('topModel'); if (tm) tm.textContent = offline ? 'server offline' : pDown ? (info.provider ? info.provider.name : 'provider') + ' offline' : info.file ? String(info.file).split(/[\\/]/).pop().replace(/\.gguf$/i, '') : 'Select model';
  const top = [], hw = [];
  if (!offline) {
    if (lastTps) top.push(statusItem('speed', lastTps + ' t/s avg'));
    if (info.file) top.push(statusItem('ctx', ctxUsed.toLocaleString() + '/' + (info.context || 0).toLocaleString() + ' tokens'));
    const rg = info.rag;
    if (rg) {
      const docs = rg.chunks ? rg.files + ' docs · ' + rg.chunks + ' chunks' : 'not indexed';
      top.push(statusItem('rag', !rg.enabled ? 'rag off' + (rg.chunks ? ': ' + docs : '') : rg.indexing ? 'indexing: ' + rg.indexing
        : rg.error ? 'rag error: ' + rg.error : !rg.chunks ? 'rag: not indexed (/index)' : 'rag: ' + docs + (rg.vectors ? ' · hybrid' : ' · keywords only')));
    }
    // The provider the model runs on (the Phoenix server, LM Studio, a cloud API...): its name, and whether it answered.
    const pv = info.provider;
    if (pv) top.push(statusItem('pc', pv.name + (info.providerError ? ': not answering' : info.loadError ? ': ' + info.loadError : '')));
  }
  // where and who: the computer's name, then the Windows user
  if (info.machine) hw.push(statusItem('pc', info.machine));
  if (info.user) hw.push(statusItem('user', info.user));
  const sh = $('sideHw'); if (sh) sh.innerHTML = hw.map(t => '<div>' + t + '</div>').join('');
  const ts = $('topSpeed'); if (ts) ts.innerHTML = top.join('<span class="sep">|</span>');
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
  if (info.version) document.title = 'Phoenix Client v.' + info.version;
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
// Each time the app window opens, the welcome logo starts on the other version (dark / light) than last time. A refresh
// (F5) keeps it: sessionStorage survives a reload of the window but not a new window.
try {
  if (!sessionStorage.getItem('phx.logoSet')) { settings.logoLight = !settings.logoLight; save(); sessionStorage.setItem('phx.logoSet', '1'); }
} catch (e) {}
function welcome() {
  promptLine.remove();
  chatEl.innerHTML = '<div class="empty" id="empty"><div class="logos' + (settings.logoLight ? ' light' : '') + '">' +
    '<img class="logo" src="assets/dark_phoenix_logo.png" alt="Phoenix LAI"><img class="logo alt" src="assets/light_phoenix_logo.png" alt=""></div>' +
    '<div class="hint">Everything runs locally on this machine. Use /help to list all commands.</div></div>';   // version: in the title bar
  placePrompt(); centerEmpty();
}
// Click the welcome logo: the dark and light versions dissolve into each other (the choice is remembered; one listener,
// so a chat restored after a reload keeps working).
chatEl.addEventListener('click', e => {
  const l = e.target.closest('.logos'); if (!l) return;
  settings.logoLight = !settings.logoLight; l.classList.toggle('light', settings.logoLight); save();
});
// The prompt always sits at the end of the conversation.
function placePrompt() { chatEl.appendChild(promptLine); drawPrompt(); syncComposer(); scrollDown(); }
function newChat() { if (controller) controller.abort(); history = []; if (typeof pendingImages !== 'undefined') { pendingImages = []; drawTray(); } ctxUsed = 0; forgetChat(); chatId = null; drawRecents(); if (info.file) drawStatus(false); welcome(); document.querySelectorAll('.modal.show').forEach(m => m.classList.remove('show')); input.focus(); }

// The conversation survives a reload (F5) of this window: what is on screen, the history sent to the model and the
// speed/context numbers are kept in sessionStorage (per window, gone when it closes). Only /new starts over.
const SAVE_KEY = 'phx.chat';
function saveChat() {
  try {
    const keep = promptLine.isConnected; if (keep) promptLine.remove();
    try { sessionStorage.setItem(SAVE_KEY, JSON.stringify({ html: chatEl.innerHTML, history, ctxUsed, tps: tpsHistory, composer: chatEl.style.getPropertyValue('--composer') })); }
    catch (e) {
      // pictures can outgrow the window's storage: keep the chat without them (the model forgets them after a reload)
      const lite = history.map(m => m.images ? { role: m.role, content: m.content } : m);
      const html = chatEl.innerHTML.replace(/<img src="data:[^"]*"/g, '<img class="gone" alt="picture"');
      sessionStorage.setItem(SAVE_KEY, JSON.stringify({ html, history: lite, ctxUsed, tps: tpsHistory, composer: chatEl.style.getPropertyValue('--composer') }));
    }
    if (keep) chatEl.appendChild(promptLine);
  } catch (e) {}
}
function forgetChat() { try { sessionStorage.removeItem(SAVE_KEY); sessionStorage.removeItem('phx.chatId'); } catch (e) {} }
function restoreChat() {
  try {
    const s = JSON.parse(sessionStorage.getItem(SAVE_KEY) || 'null');
    if (!s || !s.html) return false;
    promptLine.remove();
    if (s.composer) chatEl.style.setProperty('--composer', s.composer);   // the box's height from before: no jump while it is re-measured
    chatEl.innerHTML = s.html;
    chatEl.querySelectorAll('.caret').forEach(c => c.remove());
    chatEl.querySelectorAll('.logos').forEach(l => l.classList.toggle('light', !!settings.logoLight));   // this load's logo
    chatEl.querySelectorAll('.work.busy').forEach(w => w.classList.remove('busy'));
    history = s.history || []; ctxUsed = s.ctxUsed || 0;
    (s.tps || []).forEach(t => recordTps(t));
    placePrompt(); centerEmpty(); scrollDown();
    // Fonts and code blocks finish laying out after this and make the chat taller: keep it pinned to the newest line
    // while the page settles (a second and a half), so a reload never leaves the end below the view.
    const settleUntil = performance.now() + 1500;
    const pin = new ResizeObserver(() => { if (performance.now() < settleUntil) scrollDown(); else pin.disconnect(); });
    [chatEl, ...chatEl.children].forEach(c => pin.observe(c));
    document.fonts.ready.then(() => { if (performance.now() < settleUntil) scrollDown(); });
    setTimeout(() => pin.disconnect(), 1600);
    return true;
  } catch (e) { return false; }
}
// ---------- saved chats (Recents) and projects ----------
// Every chat is kept by the server (%LOCALAPPDATA%\LocalLLMStudio\chats) after each reply, under an id this window
// holds in sessionStorage; Recents lists them newest first and reopens one with a click. A project is a folder in
// Phoenix_Projects: + creates one, a click connects it (like /folder <name>).
const ID_KEY = 'phx.chatId';
let chatId = null, chatList = [], projectList = [];
try { chatId = sessionStorage.getItem(ID_KEY); } catch (e) {}
const TRASH_ICON = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6M10 11v6M14 11v6"/></svg>';
const PROJ_ICON = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/></svg>';
function chatTitle() {
  // the first message on screen (after /compact the history starts with the summary)
  const shown = chatEl.querySelector('.turn .user .ubody'), first = history.find(m => m.role === 'user');
  if (!first) return 'New chat';
  const t = String(shown ? shown.firstChild && shown.firstChild.nodeType === 3 ? shown.firstChild.nodeValue : shown.textContent : first.content || '').split('\n\n[context]')[0].replace(/\s+/g, ' ').trim();
  return t.length > 60 ? t.slice(0, 60).trim() + '…' : (t || 'Picture');
}
async function persistChat() {
  if (!history.length) return;
  try {
    if (!chatId) { chatId = (Date.now().toString(36) + '-' + Math.random().toString(36).slice(2, 10)); sessionStorage.setItem(ID_KEY, chatId); }
    const data = sessionStorage.getItem(SAVE_KEY);
    if (!data) return;
    chatList = (await api('/api/chat-save', { id: chatId, title: chatTitle(), project: info.folder || '', data })).chats;
    drawRecents();
  } catch (e) {}
}
function drawRecents() {
  const el = $('recentList'); if (!el) return;
  el.innerHTML = chatList.length ? chatList.map(c => '<div class="sitem' + (c.id === chatId ? ' on' : '') + '" data-id="' + esc(c.id) + '" title="' +
    esc(c.title + (c.project ? '  (' + c.project + ')' : '') + ' · ' + new Date(c.updated).toLocaleString()) + '"><span>' + esc(c.title) +
    '</span><b class="del" title="Delete this chat">' + TRASH_ICON + '</b></div>').join('') : '<div class="sempty">No chats yet</div>';
}
function drawProjects() {
  const el = $('projList'); if (!el) return;
  const cur = (info.folder || '').replace(/^\//, '').split('/')[0].toLowerCase();
  el.innerHTML = projectList.length ? projectList.map(p => '<div class="sitem' + (p.toLowerCase() === cur ? ' on' : '') + '" data-p="' + esc(p) + '" title="Connect ' + esc(p) + '">' +
    PROJ_ICON + '<span>' + esc(p) + '</span><b class="del" title="Remove this project">' + TRASH_ICON + '</b></div>').join('') : '<div class="sempty">No projects</div>';
}
async function loadSidebar() {
  try { chatList = (await api('/api/chats')).chats; } catch (e) {}
  try { projectList = (await api('/api/projects')).projects; } catch (e) {}
  drawRecents(); drawProjects();
  if (window.loadSkillList) window.loadSkillList();   // skills.js: app skills + the active project's
}
async function openChat(id) {
  if (controller) return;
  let d;
  try { d = (await api('/api/chat-get?id=' + encodeURIComponent(id))).data; } catch (e) { systemLine('Could not open that chat: ' + e.message); loadSidebar(); return; }
  try { sessionStorage.setItem(SAVE_KEY, d); sessionStorage.setItem(ID_KEY, id); } catch (e) {}
  chatId = id;
  if (typeof pendingImages !== 'undefined') { pendingImages = []; drawTray(); }
  if (!restoreChat()) { welcome(); return; }
  drawRecents();
  if (info.file) drawStatus(false);
  input.focus();
}
function newProjectBox() {
  const el = $('projList'); if (!el || el.querySelector('.pnew')) return;
  const box = document.createElement('input'); box.className = 'pnew'; box.placeholder = 'Project name'; box.maxLength = 64; box.spellcheck = false;
  const err = document.createElement('div'); err.className = 'perr';
  el.prepend(err); el.prepend(box); box.focus();
  const close = () => { box.remove(); err.remove(); };
  box.addEventListener('keydown', async e => {
    if (e.key === 'Escape') { e.stopPropagation(); close(); input.focus(); return; }
    if (e.key !== 'Enter') return;
    const name = box.value.trim(); if (!name) return;
    try { projectList = (await api('/api/project-create', { name })).projects; }
    catch (x) { err.textContent = x.message; return; }
    close(); await setFolder(name); newChat(); await loadSidebar();
  });
  box.addEventListener('blur', () => setTimeout(() => { if (!box.value.trim()) close(); }, 150));
}
if ($('recentList')) {
  $('recentList').addEventListener('click', async e => {
    const it = e.target.closest('.sitem'); if (!it) return;
    if (e.target.closest('.del')) {
      try { chatList = (await api('/api/chat-delete', { id: it.dataset.id })).chats; } catch (x) {}
      if (it.dataset.id === chatId) newChat(); else drawRecents();
      return;
    }
    if (it.dataset.id !== chatId) openChat(it.dataset.id);
  });
  $('projList').addEventListener('click', async e => {
    const it = e.target.closest('.sitem'); if (!it || controller) return;
    if (e.target.closest('.del')) {
      if (!confirm('Remove the project "' + it.dataset.p + '"?\n\nIts folder is moved to Phoenix_Projects\\.llm-backup, not erased.')) return;
      try { projectList = (await api('/api/project-delete', { name: it.dataset.p })).projects; } catch (x) { systemLine('Could not remove the project: ' + x.message); return; }
      await refresh(); drawProjects(); return;
    }
    // click = make it the active project (green); click the active one again = disconnect. Either way the chats shown
    // and saved switch to that project's .phoenix\chats (or the no-project folder), so start a fresh chat.
    const active = it.classList.contains('on');
    await setFolder(active ? 'close' : it.dataset.p);
    newChat(); await loadSidebar();
  });
  $('newProject').onclick = newProjectBox;
  loadSidebar();
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
  const d = document.createElement('div'); d.className = 'meta sysline'; d.textContent = text;
  chatEl.insertBefore(d, promptLine.isConnected ? promptLine : null); scrollDown();
  saveChat();
}

// /compact: the model summarises the history it has been sent; the messages stay on screen above a divider, and from
// then on the model gets only the summary (as the conversation's first exchange) plus what follows.
async function compact(auto) {
  if (controller) return;
  if (!info.file) { systemLine('No model loaded — use /model to pick one.'); return; }
  const n = history.length;
  if (n < 2) { systemLine('Nothing to compact yet.'); return; }
  const box = document.createElement('details'); box.className = 'work busy compacted';
  box.innerHTML = '<summary><span class="spin"></span><span class="lbl"></span></summary><pre class="worklog"></pre>';
  const lbl = box.querySelector('.lbl'), log = box.querySelector('.worklog');
  const what = (auto ? 'Context 80% full: compacting ' : 'Compacting ') + n + ' messages…';
  lbl.textContent = what;
  chatEl.insertBefore(box, promptLine.isConnected ? promptLine : null); scrollDown();
  controller = new AbortController();
  let summary = '', text = '', err = null, tokens = 0;
  try {
    const res = await fetch('/api/compact', { method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ messages: history.map(m => ({ role: m.role, content: m.content })) }) });
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
        if (ev.part) lbl.textContent = what + (ev.parts > 1 ? ' (part ' + ev.part + ' of ' + ev.parts + ')' : '');
        else if (ev.t !== undefined) { text += ev.t; log.textContent = text; }
        else if (ev.error) err = ev.error;
        else if (ev.done) { summary = ev.summary; tokens = ev.tokens; }
      }
    }
  } catch (e) { err = e.name === 'AbortError' ? 'stopped' : e.message; }
  controller = null;
  box.classList.remove('busy');
  if (!summary) {
    box.classList.add('bad'); lbl.textContent = '✗ Compact failed: ' + (err || 'no summary') + ' — nothing was changed';
  } else {
    history = [{ role: 'user', content: 'Summary of our conversation so far (the earlier messages were compacted into it):\n\n' + summary },
               { role: 'assistant', content: 'Understood. I will continue from this summary.' }];
    ctxUsed = 0; drawStatus(false);
    log.textContent = summary;
    box.classList.add('verified');
    lbl.textContent = '✓ ' + (auto ? 'Auto-compacted ' : 'Compacted ') + n + ' messages into a ' + tokens + '-token summary · the model sees only the summary from here (click to read it)';
  }
  placePrompt(); input.focus();
  saveChat(); persistChat();
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
// Phoenix Client: every provider with its state ("up" / "down" / "busy" / "unknown"), saved features and models.
let modelGroups = [];
// A model's display name: its .gguf file name (the name inside the file is often just a build folder).
function fileLabel(f) { return String(f || '').split(/[\\/]/).pop().replace(/\.gguf$/i, ''); }
// Size in billions of parameters from the file name ("E4B" = 4, "12B" = 12, "26B" = 26); unknown sizes sort last.
function modelSize(name) { const m = /(?:^|[-_.])E?(\d+(?:\.\d+)?)B(?=[-_.]|$)/i.exec(name); return m ? parseFloat(m[1]) : 1e9; }
// mmproj files are image/audio projectors for a chat model, not models you can chat with.
async function loadModelList() {
  try {
    const j = await api('/api/models');
    chatModels = j.models.filter(m => !/embed|mmproj/i.test(m.name))
      .sort((a, b) => modelSize(a.name) - modelSize(b.name) || a.name.localeCompare(b.name));
    modelGroups = j.groups || [];
  } catch (e) {}
}
loadModelList();
async function loadModel(name, ctx, provider) {
  systemLine('Loading ' + name + (ctx ? ', context ' + ctx : '') + '…');
  setTimeout(refresh, 300);
  const t0 = performance.now();
  const cfg = ms(name);
  try { info = await api('/api/load', { name, context: ctx === undefined ? cfg.context : ctx, kv: cfg.kv, provider }); }
  catch (e) { systemLine('Could not load: ' + e.message); return false; }
  systemLine('Loaded ' + fileLabel(info.file || name) + ' on ' + info.backend + ' in ' + ((performance.now() - t0) / 1000).toFixed(1) + ' s, context ' + Number(info.context).toLocaleString() + (info.contextAuto ? ' (auto)' : '') + '.');
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
  { name: '/image', use: '/image', desc: 'attach pictures to the next message (or paste / drop them; models with a vision file)', run: () => {
      if (!info.vision) { systemLine(info.file ? 'This model cannot read pictures: it needs its vision file (…mmproj.gguf) in the models folder.' : 'Load a model first.'); return; }
      imgPick.click();
    } },
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
      if (m.unsupported) { systemLine(m.name.replace(/\.gguf$/i, '') + ' is not supported: ' + m.unsupported); return; }
      await loadModel(m.name);
    } },
  { name: '/new', use: '/new', desc: 'start a new chat', run: () => newChat() },
  { name: '/compact', use: '/compact', desc: 'summarise the conversation so far: the chat stays on screen, the model keeps only the summary', run: () => compact() },
  { name: '/rag', use: '/rag [on | off | <words>]', desc: 'docs status, turn retrieval on/off, or preview a search', arg: true,
    subs: [{ name: 'on' }, { name: 'off' }, { name: '<words>' }],
    run: async arg => {
      if (arg === 'on' || arg === 'off') { await api('/api/rag', { enabled: arg === 'on' }); systemLine('Docs retrieval ' + arg + '.'); refresh(); return; }
      if (!arg) { const r = (await api('/api/info')).rag; systemLine('Docs: ' + r.files + ' files, ' + r.chunks + ' chunks, retrieval ' + (r.enabled ? 'on' : 'off') + '. Commands: /index, /rag on|off, /rag <words>.'); return; }
      const r = await api('/api/rag?q=' + encodeURIComponent(arg));
      systemLine(r.hits.length ? r.hits.length + ' chunks in ' + r.ms + ' ms: ' + r.hits.map(h => '[' + h.n + '] ' + h.file + (h.title ? ' › ' + h.title : '') + ' (' + h.score + ')').join('  ') : 'No matching docs (' + r.ms + ' ms).');
    } },
  // Instant Recall needs the local engine's cache, which a /v1 provider does not expose (Phoenix Client, 2026-10-20):
  // { name: '/recall', use: '/recall', desc: 'Instant Recall: pre-read every file of the project once, so the model can open them instantly later',
  //   run: async () => { await api('/api/recall', {}); systemLine('Instant Recall: pre-reading the project in the background (a chat message pauses it). Files read once open instantly afterwards, in any chat.'); refresh(); } },
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
  // CPU mode (off, 2026-10-20): models run on the GPU only, so /device is gone. Kept here, commented out:
  // { name: '/device', use: '/device gpu|cpu', desc: 'run the model on the GPU or the CPU (reloads the current model)', arg: true,
  //   options: () => [{ value: 'gpu', desc: 'video card (fast)' + (info.device !== 'cpu' ? ' (current)' : '') }, { value: 'cpu', desc: 'processor (slow; no video card needed)' + (info.device === 'cpu' ? ' (current)' : '') }],
  //   run: async arg => {
  //     const d = (arg || '').trim().toLowerCase();
  //     if (d !== 'gpu' && d !== 'cpu') { systemLine('Running on ' + (info.device === 'cpu' ? 'CPU' : 'GPU') + (info.backend ? ' (' + info.backend + ')' : '') + '. Use /device gpu or /device cpu.'); return; }
  //     if (info.model) systemLine('Switching to ' + d.toUpperCase() + ' and reloading ' + info.file + '…');
  //     try { info = await api('/api/device', { device: d }); } catch (e) { systemLine('Could not switch: ' + e.message); return; }
  //     ctxUsed = 0; await refresh();
  //     systemLine('Now on ' + d.toUpperCase() + (info.backend ? ': ' + fileLabel(info.file) + ' runs on ' + info.backend : ' (applies to the next model you load)') + '.');
  //   } },
  // The server's model memory is the server's business (its console: load, unload, vram), not the client's:
  // { name: '/unload', use: '/unload', desc: 'unload the model and free its video memory (/model loads one again)',
  //   run: async () => { info = await api('/api/unload', {}); history = []; ctxUsed = 0; systemLine('Model unloaded; its video memory is free.'); refresh(); } },
  // { name: '/vram', use: '/vram', desc: 'video memory: what Phoenix LAI uses, what is free, and which programs use the most',
  //   run: async () => {
  //     const r = await api('/api/vram'), v = (await api('/api/info')).vram, gb = b => (b / 1073741824).toFixed(2) + ' GB';
  //     let txt = v ? 'Phoenix LAI uses ' + gb(v.used) + ' of the ' + gb(v.budget) + ' it may use now (the rest of the card is taken by other programs).\n' : 'No GPU model loaded.\n';
  //     if (r.processes) txt += 'Largest users of video memory:\n' + r.processes.map(p => '  ' + gb(p.bytes).padStart(9) + '  ' + p.name).join('\n');
  //     else if (r.error) txt += 'Per-program use unavailable: ' + r.error;
  //     systemLine(txt);
  //   } },
  { name: '/provider', use: '/provider <name>', desc: 'switch to another OpenAI /v1 provider (Providers in the left panel; + adds one)', arg: true,
    options: () => (window.providerNames ? window.providerNames() : []).map(n => ({ value: n, desc: '' })),
    run: async arg => { if (window.useProviderNamed) await window.useProviderNamed(arg); } },
  { name: '/skills', use: '/skills [name]', desc: 'open the Skill Builder (a skill is called in chat with /<skill-name>)', arg: true,
    options: () => (window.skillNames ? window.skillNames() : []).map(n => ({ value: n, desc: 'edit' })),
    run: arg => {   // no name: the builder as it was (or the first skill)
      if (window.openSkillBuilder) window.openSkillBuilder(arg || undefined);
      else systemLine('The Skill Builder is in the Phoenix window (it needs more room than this panel). Skills you save there work here too: call one with /<skill-name>.');
    } },
  { name: '/settings', use: '/settings', desc: 'model, context, sampling, system prompt, docs and browser', run: () => openSettings() },
  { name: '/skin', use: '/skin phoenix|fleet', desc: 'the look: Phoenix (default) or the shared FleetHacker Fleet skin', arg: true,
    options: () => SKINS.map(k => ({ value: k.id, desc: k.desc + ((settings.skin || 'phoenix') === k.id ? ' (current)' : '') })),
    run: arg => {
      const v = (arg || '').trim().toLowerCase();
      if (!SKINS.some(k => k.id === v)) { systemLine('Skin: ' + (settings.skin || 'phoenix') + '. Use /skin phoenix or /skin fleet.'); return; }
      settings.skin = v; save(); applyLook();
      systemLine(v === 'fleet' ? 'Fleet skin on: navy and brass, Segoe UI. The accent and chat font are fixed while it is on; /skin phoenix switches back.' : 'Phoenix look on.');
    } }
];

async function command(text) {
  const sp = text.search(/\s/), cmd = sp < 0 ? text : text.slice(0, sp), arg = sp < 0 ? '' : text.slice(sp).trim();
  if (cmd === '/reindex') { await reindex(); return true; }
  const c = COMMANDS.find(c => c.name === cmd.toLowerCase());
  // /<skill-name> calls a skill: the message goes to the model, and the server puts the skill in front of it.
  if (!c && window.skillNames && window.skillNames().includes(cmd.slice(1).toLowerCase())) return false;
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
// Skins: Phoenix's own look (the default) or the optional Fleet skin (css/fleet-skin.css, scoped under html[data-skin="fleet"]).
const SKINS = [{ id: 'phoenix', label: 'Phoenix', desc: "Phoenix's own look" }, { id: 'fleet', label: 'Fleet', desc: 'the shared FleetHacker look' }];
const ACCENTS = ['#e8c15a', '#f08a3c', '#e85a5a', '#e85aa8', '#a77bf0', '#5a8ce8', '#4fc3e8', '#3cc8a0', '#7ad35a', '#d8d8e0'];
function hexRgb(h) { const n = parseInt(h.slice(1), 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; }
function applyLook() {
  document.documentElement.classList.toggle('no-banner', !settings.banner);
  document.documentElement.classList.toggle('grid', !!settings.grid);
  document.documentElement.dataset.skin = settings.skin === 'fleet' ? 'fleet' : 'phoenix';   // /skin: Phoenix's own look is the default skin
  document.documentElement.classList.toggle('light', !!settings.light);   // settings > appearance > light mode   // grid background + faint logo, else solid   // banner kept, off by default
  const acc = /^#[0-9a-f]{6}$/i.test(settings.accent || '') ? settings.accent : ACCENTS[0], rgb = hexRgb(acc), root = document.documentElement.style;
  root.setProperty('--accent', acc);
  root.setProperty('--accent-rgb', rgb.join(','));
  root.setProperty('--accent-2', 'rgb(' + rgb.map(c => Math.round(c + (255 - c) * 0.4)).join(',') + ')');   // lighter shade
  if (typeof drawBackground === 'function' && bgReady) drawBackground();   // the glows use the accent too
  const f = FONTS.find(x => x.id === settings.font) || FONTS[0];
  document.documentElement.style.setProperty('--chat', f.css);
  document.documentElement.style.setProperty('--chat-size', (settings.size || 14) + 'px');
  if (window.applyVscodeTheme) window.applyVscodeTheme();   // vscode.html: VS Code's theme wins over these settings
  syncSkinControls();
  alignChevrons();
}
function syncSkinControls() {
  const fixed = settings.skin === 'fleet';
  document.querySelectorAll('#sFont input, #sAccent button, #sAccent input').forEach(el => {
    el.disabled = fixed;
    if (fixed) el.setAttribute('aria-describedby', 'sSkinFixedNote');
    else el.removeAttribute('aria-describedby');
  });
  document.querySelectorAll('#sFont input').forEach(el => { el.checked = el.value === (fixed ? 'cascadia' : settings.font || 'syne'); });
  const accent = fixed ? getComputedStyle(document.documentElement).getPropertyValue('--accent').trim() : settings.accent || ACCENTS[0];
  document.querySelectorAll('#sAccent button').forEach(el => el.classList.toggle('on', el.dataset.c === accent));
  if ($('sAccentPick')) $('sAccentPick').value = accent;
  const hex = document.querySelector('#sAccent .hex'); if (hex) hex.textContent = accent;
  const note = $('sSkinFixedNote');
  if (note) note.hidden = !fixed;
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
  syncSkinControls();
}
$('sAccent').addEventListener('click', e => { const b = e.target.closest('button[data-c]'); if (!b) return; settings.accent = b.dataset.c; applyLook(); drawSwatches(); });
const radioValue = box => { const r = document.querySelector('#' + box + ' input:checked'); return r ? r.value : null; };
let openedBrowser = 'edge', openedGpu = null, lookBefore = null;
async function openSettings() {
  const rg = info.rag || {};
  $('sRag').checked = !!rg.enabled; $('ragNote').textContent = rg.chunks ? ' · ' + rg.files + ' docs, ' + rg.chunks + ' chunks' : rg.indexing ? ' · indexing: ' + rg.indexing : rg.error ? ' · index error: ' + rg.error : ' · not indexed: run /index';
  lookBefore = { font: settings.font, size: settings.size, accent: settings.accent, banner: settings.banner, grid: settings.grid, light: settings.light, skin: settings.skin };
  // (answer while I type needs the local engine's cache) $('sPretype').checked = settings.pretype !== false; $('sPretype').onchange = () => { settings.pretype = $('sPretype').checked; save(); };
  $('sTypeEffect').checked = settings.typeEffect !== false; $('sTypeEffect').onchange = () => { settings.typeEffect = $('sTypeEffect').checked; save(); };
  $('sBanner').checked = !!settings.banner;
  if ($('sGrid')) {   // not in the VS Code panel, which takes VS Code's theme
    $('sGrid').checked = !!settings.grid;
    $('sGrid').onchange = () => { settings.grid = $('sGrid').checked; applyLook(); };
  }
  if ($('sLight')) {
    $('sLight').checked = !!settings.light;
    $('sLight').onchange = () => { settings.light = $('sLight').checked; applyLook(); };
  }
  // Status banner ('LOCAL DEMO'), off by default. The stored key stays settings.banner, so saved preferences keep working.
  $('sBanner').onchange = () => { settings.banner = $('sBanner').checked; applyLook(); };
  radios('sFont', FONTS.map(f => ({ value: f.id, label: f.label, style: 'font-family:' + f.css.replace(/"/g, "'") })), settings.font || 'syne', v => { settings.font = v; applyLook(); });
  radios('sSize', SIZES.map(n => ({ value: n, label: n + 'px' })), settings.size || 14, v => { settings.size = +v; applyLook(); });
  drawSwatches();
  if ($('sSkin')) radios('sSkin', SKINS.map(k => ({ value: k.id, label: k.label })), settings.skin || 'phoenix', v => { settings.skin = v; applyLook(); });
  let b = { browser: 'auto', installed: true };
  try { b = await api('/api/browser'); } catch (e) {}
  openedBrowser = b.browser;
  radios('sBrowser', [{ value: 'chrome', label: 'Chrome' }, { value: 'edge', label: 'Edge' }, { value: 'auto', label: 'Auto' }], b.browser);
  $('browserNote').textContent = (b.browser === 'auto' ? 'Chrome first, Edge if Chrome is missing' + (b.used ? ' (now: ' + b.used + ')' : '') + ' · '
    : b.installed ? '' : b.fallback ? b.browser + ' is not installed, so ' + b.fallback + ' is used · ' : 'neither browser found · ') + 'used from the next start';
  // CPU mode (off): radios('sDevice', [{ value: 'gpu', label: 'GPU' }, { value: 'cpu', label: 'CPU' }], info.device === 'cpu' ? 'cpu' : 'gpu');
  // (server GPU setting) let g = { profile: 'auto', safe: false };
  // (server GPU setting) try { g = await api('/api/gpu'); } catch (e) {}
  // (server GPU setting) openedGpu = g;
  // (server GPU setting) radios('sGpu', [{ value: 'amd', label: 'AMD' }, { value: 'nvidia', label: 'NVIDIA' }, { value: 'auto', label: 'Auto' }], g.profile);
  // (server GPU setting) $('sGpuSafe').checked = !!g.safe;
  // (server GPU setting) $('gpuNote').textContent = 'NVIDIA: picks the NVIDIA card on two-GPU laptops and uses shorter GPU jobs · used from the next model load' + (g.backend ? ' · now: ' + g.backend : '');
  syncSkinControls();
  $('settingsModal').classList.add('show');
  $('settingsBody').scrollTop = 0;
}
function closeSettings(applied) {
  if (!applied && lookBefore) { settings.font = lookBefore.font; settings.size = lookBefore.size; settings.accent = lookBefore.accent; settings.banner = lookBefore.banner; settings.grid = lookBefore.grid; settings.light = lookBefore.light; settings.skin = lookBefore.skin; applyLook(); }   // undo the preview
  $('settingsModal').classList.remove('show');
}
$('closeSettings').addEventListener('click', () => closeSettings(false));
$('settingsNew').addEventListener('click', () => { closeSettings(true); save(); newChat(); });
$('applySettings').addEventListener('click', async () => {
  try {
    save();
    await api('/api/rag', { enabled: $('sRag').checked });
    const br = radioValue('sBrowser');
    if (br && br !== openedBrowser) { await api('/api/browser', { browser: br }); systemLine('Browser set to ' + br + ' — used from the next start.'); }
    // CPU mode (off): the device setting is gone.
    // const dev = radioValue('sDevice');
    // if (dev && dev !== (info.device === 'cpu' ? 'cpu' : 'gpu') && !controller) {
    //   systemLine('Switching to ' + dev.toUpperCase() + (info.file ? ' and reloading ' + info.file + '…' : '.'));
    //   info = await api('/api/device', { device: dev }); ctxUsed = 0;
    //   systemLine('Now on ' + dev.toUpperCase() + (info.backend ? ': ' + fileLabel(info.file) + ' runs on ' + info.backend : ' (applies to the next model you load)') + '.');
    // }
    // (server GPU setting) const gp = radioValue('sGpu'), gs = $('sGpuSafe').checked;
    // (server GPU setting) if (openedGpu && gp && (gp !== openedGpu.profile || gs !== !!openedGpu.safe)) {
    // (server GPU setting) await api('/api/gpu', { profile: gp, safe: gs ? 'on' : 'off' });
    // (server GPU setting) systemLine('GPU profile set to ' + gp + (gs ? ' (safe kernels)' : '') + ' — used from the next model load (/model).');
    // }
  } catch (e) {}
  closeSettings(true);
  refresh();
});
$('settingsModal').addEventListener('keydown', e => { if (e.key === 'Enter' && e.target.tagName !== 'TEXTAREA') { e.preventDefault(); $('applySettings').click(); } });

async function send() {
  const text = input.value.trim();
  if ((!text && !pendingImages.length) || controller) return;
  if (text.startsWith('/')) {
    input.value = ''; autosize();
    let handled = false;
    try { handled = await command(text); } catch (e) { systemLine(e.message); handled = true; }
    if (handled) return;
    input.value = text; autosize();
  }
  if (!info.file) { systemLine('No model loaded — use /model to pick one.'); return; }
  input.value = ''; autosize();
  // Auto-compact at 80% of the context: one summary now, instead of old messages silently dropping (and the whole
  // window being re-read) on every turn from here on.
  if (info.context && ctxUsed >= info.context * 0.8 && history.length >= 2) {
    await compact(true);
    if (controller) return;
  }
  // The message box stays fixed at the bottom: the turn goes in above it.

  const turn = document.createElement('div'); turn.className = 'turn';
  turn.innerHTML = '<div class="user"></div>';
  const images = pendingImages; pendingImages = []; drawTray();
  // the text on the › line, the pictures under it (aligned with the text)
  const ubody = document.createElement('div'); ubody.className = 'ubody'; ubody.textContent = text;
  if (images.length) {
    const box = document.createElement('div'); box.className = 'uimgs';
    images.forEach(src => { const im = document.createElement('img'); im.src = src; box.appendChild(im); });
    ubody.appendChild(box);
  }
  turn.querySelector('.user').appendChild(ubody);
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
    phase(p, lang, usedRef, docs) {
      this.ensure();
      if (lang) this.lang = lang;
      const sheet = this.lang === 'code' ? 'the syntax reference' : 'the ' + this.lang + ' syntax reference';
      const via = usedRef ? ' (checked against ' + sheet + ')' : ' (no ' + (this.lang === 'code' ? '' : this.lang + ' ') + 'reference sheet; checked by the model)';
      const labels = { checking: 'Checking against ' + sheet + '…', good: '✓ Looks good' + via, fixed: '⚠ Possible mistakes found and corrected' + via,
                       unsure: '⚠ Could not confirm the code is correct' + via, wrong: '✗ Syntax errors found and corrected' + via };
      // passed with library documents in front of it: one plain line naming them
      if (p === 'good' && usedRef && docs && docs.length) labels.good = '✓ Verified via RAG document' + (docs.length > 1 ? 's ' : ' ') + docs.join(', ');
      this.label.textContent = labels[p] || p;
      this.box.classList.toggle('busy', p === 'checking');
      this.box.classList.remove('ok', 'warn', 'bad', 'verified');
      if (p !== 'checking') this.box.classList.add(p === 'good' ? (usedRef && docs && docs.length ? 'verified' : 'ok') : p === 'wrong' ? 'bad' : 'warn');
    },
    done() { if (this.box) this.box.classList.remove('busy'); }
  };

  // vscode.html adds the editor's context (open file, selection) to what the model sees; the chat shows only your text
  history.push(images.length ? { role: 'user', content: text + (window.phxContext ? window.phxContext() : ''), images }
                             : { role: 'user', content: text + (window.phxContext ? window.phxContext() : '') });
  const cfg = ms(info.file);
  const messages = cfg.system ? [{ role: 'system', content: cfg.system }, ...history] : [...history];
  controller = new AbortController();
  let appended = false, t0 = performance.now(), firstAt = 0, wrote = false, prog = null;
  const edited = [];   // files the model wrote or edited this turn (vscode.html checks them for errors)
  try {
    const body = { messages, temperature: cfg.temperature, top_k: cfg.top_k, top_p: cfg.top_p,
                   repeat_penalty: cfg.repeat_penalty, max_tokens: cfg.max_tokens };
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
        else if (ev.skills) {
          // the skills the server gave the model for this message: one line above the reply, a click opens the skill
          const d = document.createElement('div'); d.className = 'sources skillsused';
          d.innerHTML = '<b>skill' + (ev.skills.length > 1 ? 's' : '') + '</b> ' + ev.skills.map(k => '<a data-skill="' + esc(k.name) + '" data-scope="' + esc(k.scope) + '">' + esc(k.name) + '</a>' +
            (k.called ? '' : ' <i>(matched)</i>')).join(', ');
          d.title = 'Instructions loaded for this message · click to open in the Skill Builder';
          turn.insertBefore(d, out);
        }
        else if (ev.indexed) {
          // the project index took in the model's file changes: a green line under the reply (and its check line)
          const n = document.createElement('details'); n.className = 'work verified indexed';
          const f = ev.indexed.files || [];
          n.innerHTML = '<summary><span class="lbl"></span></summary>';
          n.querySelector('.lbl').textContent = '✓ Changes indexed: ' + (f.length > 3 ? f.length + ' files' : f.join(', ')) + ' (' + ev.indexed.ms + ' ms)';
          turn.appendChild(n);
        }
        else if (ev.phase) { flush(); out.innerHTML = render(reply, false); work.phase(ev.phase, ev.lang, ev.ref, ev.docs); }
        else if (ev.approve) askApproval(ev.approve);
        else if (ev.replace !== undefined) { reply = ev.replace; shown = reply.length; out.innerHTML = render(reply, false); }
        else if (ev.rag) {
          const d = document.createElement('div'); d.className = 'sources';
          // One short line (files used); click it to see the exact sections that were added.
          const files = [...new Set(ev.rag.map(h => h.file))];
          // counts only (a library can hold many files); the names and sections are in the detail
          d.innerHTML = '<b>docs</b> searched the library: ' + ev.rag.length + ' section' + (ev.rag.length > 1 ? 's' : '') + ' from ' + files.length + ' document' + (files.length > 1 ? 's' : '') +
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
  saveChat(); persistChat();
  if (wrote) { refresh(); if ($('files').classList.contains('show')) showFiles(filesAt); }
  if (window.phxTurnDone) window.phxTurnDone(edited, turn);
}

// Mirror the textarea with a blinking block cursor at the caret position (the textarea itself is invisible).
function drawPrompt() {
  const v = input.value, at = input.selectionEnd == null ? v.length : input.selectionEnd;
  const caret = '<span class="caret"></span>';
  if (!v) { mirror.innerHTML = caret; return; }   // no placeholder: just the › and the cursor
  // A zero-width space keeps a trailing newline from collapsing.
  mirror.innerHTML = esc(v.slice(0, at)) + caret + esc(v.slice(at)) + (v.endsWith('\n') ? '\u200b' : '');
}
// Restart the blink on every keystroke so the cursor is always visible while typing.
function restartBlink() { const c = mirror.querySelector('.caret'); if (c) { c.style.animation = 'none'; void c.offsetWidth; c.style.animation = ''; } }
function autosize() { drawPrompt(); restartBlink(); syncComposer(); }
['input', 'keyup', 'click', 'select', 'focus'].forEach(ev => input.addEventListener(ev, autosize));
document.addEventListener('selectionchange', () => { if (document.activeElement === input) drawPrompt(); });
drawPrompt();

// Pictures (models with a vision file): paste or drop one anywhere, or /image to pick files. They wait as thumbnails on
// the message box's first line (click one to remove it; Backspace in an empty box removes the last) and go with the
// next message. Only the picture data is sent, inline; nothing is uploaded anywhere else.
var pendingImages = [];   // var: newChat may run before this line on a restored page
const imgTray = document.createElement('div'); imgTray.className = 'imgtray'; imgTray.style.display = 'none';
promptLine.prepend(imgTray);
const imgPick = document.createElement('input'); imgPick.type = 'file'; imgPick.accept = 'image/*'; imgPick.multiple = true; imgPick.hidden = true;
document.body.appendChild(imgPick);
imgPick.addEventListener('change', () => { [...imgPick.files].forEach(addImageFile); imgPick.value = ''; });
function drawTray() {
  imgTray.innerHTML = '';
  pendingImages.forEach((src, i) => {
    const im = document.createElement('img'); im.src = src; im.title = 'Click to remove';
    im.addEventListener('click', e => { e.stopPropagation(); pendingImages.splice(i, 1); drawTray(); input.focus(); });
    imgTray.appendChild(im);
  });
  imgTray.style.display = pendingImages.length ? '' : 'none';
  // thumbnails start exactly where the text does (after the ›)
  const tb = promptLine.querySelector('.typebox');
  imgTray.style.paddingLeft = Math.max(0, tb.getBoundingClientRect().left - promptLine.getBoundingClientRect().left - promptLine.clientLeft - parseFloat(getComputedStyle(promptLine).paddingLeft)) + 'px';
  syncComposer();
}
// PNG / JPEG / GIF / BMP go as they are; other formats (WebP ...) and very large pictures become a PNG of at most
// 2048 px on the long side (the model looks at ~1056 px at most).
function imageData(file) {
  return new Promise((ok, fail) => {
    const r = new FileReader();
    r.onerror = () => fail(r.error);
    r.onload = () => {
      const url = r.result, im = new Image();
      im.onerror = () => fail(new Error('That file is not a picture this window can read.'));
      im.onload = () => {
        const s = Math.min(1, 2048 / Math.max(im.width, im.height));
        if (s >= 1 && /^image\/(png|jpeg|gif|bmp)$/.test(file.type)) { ok(url); return; }
        const c = document.createElement('canvas'); c.width = Math.max(1, Math.round(im.width * s)); c.height = Math.max(1, Math.round(im.height * s));
        c.getContext('2d').drawImage(im, 0, 0, c.width, c.height);
        ok(c.toDataURL('image/png'));
      };
      im.src = url;
    };
    r.readAsDataURL(file);
  });
}
async function addImageFile(file) {
  if (!file || !/^image\//.test(file.type)) return;
  if (!info.file) { systemLine('Load a model first.'); return; }
  if (!info.vision) { systemLine('This model cannot read pictures: it needs its vision file (…mmproj.gguf) in the models folder.'); return; }
  if (pendingImages.length >= 8) { systemLine('At most 8 pictures per message.'); return; }
  try { pendingImages.push(await imageData(file)); drawTray(); input.focus(); } catch (e) { systemLine(e.message); }
}
document.addEventListener('paste', e => {
  const f = [...((e.clipboardData || {}).files || [])].filter(x => /^image\//.test(x.type));
  if (!f.length || document.querySelector('.modal.show')) return;
  e.preventDefault(); e.stopImmediatePropagation();
  f.forEach(addImageFile);
}, true);
document.addEventListener('dragover', e => { if (e.dataTransfer && [...e.dataTransfer.types].includes('Files')) e.preventDefault(); });
document.addEventListener('drop', e => {
  const f = e.dataTransfer ? [...e.dataTransfer.files].filter(x => /^image\//.test(x.type)) : [];
  if (!f.length) return;
  e.preventDefault();
  f.forEach(addImageFile);
});

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
  return;   // answer while I type needs the local engine's cache, which a /v1 provider does not expose
  if (!settings.pretype) return;
  pretypeTimer = setTimeout(() => {
    const text = input.value.trim();
    if (controller || !info.file || text.length < 8 || text.startsWith('/') || text === lastPretyped) return;
    lastPretyped = text;
    const sys = ms(info.file).system;
    const messages = sys ? [{ role: 'system', content: sys }, ...history, { role: 'user', content: text }] : [...history, { role: 'user', content: text }];
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
  if (e.key === 'Backspace' && !input.value && pendingImages.length) { e.preventDefault(); pendingImages.pop(); drawTray(); }
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
  // Message box: centre the › on the block cursor instead. The cursor is 1.05em tall, its bottom on the text-bottom
  // (baseline + descent), lifted 4px in all (#mirror -2px, .caret -2px; see chat.css).
  const size = parseFloat(getComputedStyle(mirror).fontSize), caretMid = x.fontBoundingBoxDescent - 0.525 * size - 4;
  document.documentElement.style.setProperty('--pchev-shift', (chevMid + caretMid).toFixed(2) + 'px');
  // Settings header: centre the › on the capitals of "SETTINGS" (same glyph and size as the chat's ›).
  const h2 = document.querySelector('#settingsModal header h2');
  if (h2) {
    const hs = getComputedStyle(h2);
    ctx.font = hs.fontWeight + ' ' + hs.fontSize + ' ' + hs.fontFamily;
    const S = ctx.measureText('S'), capMid = (S.actualBoundingBoxAscent - S.actualBoundingBoxDescent) / 2;
    document.documentElement.style.setProperty('--hchev-shift', (chevMid - capMid).toFixed(2) + 'px');
  }
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

// Model drop-down (chat.html: the top bar; vscode.html: the model chip in the composer): every provider's models;
// picking one loads it. toggleModelMenu(menu) opens / closes it, pickFromModelMenu(e, menu) handles a click in it.
async function toggleModelMenu(modelMenu) {
    if (modelMenu.classList.toggle('show')) {
      await loadModelList();
      // Phoenix Client: every provider's models, grouped; an offline provider and its models are greyed out
      if (modelGroups.length) {
        const off = ['down', 'unknown'];
        modelMenu.innerHTML = modelGroups.map(g => {
          const dead = off.includes(g.state), isActive = info.provider && g.provider.id === info.provider.id;
          const missing = g.provider.features ? Object.keys(g.provider.features).filter(k => !g.provider.features[k]) : null;
          const tip = g.provider.name + ' · ' + (dead ? (g.state === 'down' ? 'offline' : 'checking…') : 'online') +
            (missing ? (missing.length ? ' · not available: ' + missing.map(k => window.featureLabel ? window.featureLabel(k) : k).join(', ') : ' · every tested feature available') : ' · not tested yet (Test in its edit window)');
          return '<div class="mgroup st-' + g.state + '" title="' + esc(tip) + '"><i class="cdot"></i>' + esc(g.provider.name) + (dead ? ' <span class="mstate">' + (g.state === 'down' ? 'offline' : 'checking…') + '</span>' : '') + '</div>' +
            (g.models.length ? g.models.map(m => '<button data-m="' + esc(m.name) + '" data-p="' + esc(g.provider.id) + '"' +
              (dead ? ' class="off" title="' + esc(g.provider.name + ' is offline') + '"' : isActive && m.name === info.file ? ' class="on"' : '') + '><span>' + esc(m.name) + '</span>' +
              (isActive ? '<span class="mgear" title="Settings for this model">' + GEAR_ICON + '</span>' : '') + '</button>').join('')
              : '<div class="none">' + (dead ? 'offline' : 'no chat models') + '</div>');
        }).join('');
        return;
      }
      // models the server cannot run are greyed out (hover says why) and cannot be picked
      modelMenu.innerHTML = chatModels.length ? chatModels.map(m => '<button data-m="' + esc(m.name) + '"' +
        (m.unsupported ? ' class="off" title="' + esc('Not supported: ' + m.unsupported) + '"' : m.name === info.file ? ' class="on"'
          : m.tooBig ? ' class="big" title="' + esc('Will not fit in the memory free now: ' + m.tooBig) + '"' : '') + '>' +
        '<span>' + esc(m.name.replace(/\.gguf$/i, '')) + '</span><span class="mgear" title="Settings for this model">' + GEAR_ICON + '</span></button>').join('')
        : '<div class="none">' + esc(info.providerError ? (info.provider ? info.provider.name : 'The provider') + ': ' + info.providerError : 'No models from ' + (info.provider ? info.provider.name : 'the provider') + '.') + '</div>';
    }
}
function pickFromModelMenu(e, modelMenu) {
    const b = e.target.closest('button'); if (!b || b.classList.contains('off')) return; modelMenu.classList.remove('show');
    if (e.target.closest('.mgear')) { openModelSettings(b.dataset.m); return; }
    const otherProvider = b.dataset.p && info.provider && b.dataset.p !== info.provider.id;
    if ((b.dataset.m !== info.file || otherProvider) && !controller) loadModel(b.dataset.m, undefined, b.dataset.p).then(() => window.providersChanged && window.providersChanged());
}
if ($('modelMenu')) {
  const modelMenu = $('modelMenu');
  modelMenu.onclick = e => pickFromModelMenu(e, modelMenu);
  document.addEventListener('click', e => { if (!e.target.closest('.mpick')) modelMenu.classList.remove('show'); });
}

// Sidebar and top bar (chat.html only; vscode.html shares this script): shortcuts for the same /commands.
if ($('side')) {
  $('navNew').onclick = () => newChat();
  $('navSettings').onclick = () => openSettings();
  $('modelPick').onclick = e => {
    e.stopPropagation();
    if ($('modelPick').classList.contains('dis')) return;   // no provider online
    toggleModelMenu($('modelMenu'));
  };
}

// The message box is fixed to the bottom: keep the conversation's bottom padding in step with its height.
function syncComposer() { if (promptLine.offsetHeight) chatEl.style.setProperty('--composer', promptLine.offsetHeight + 'px'); }
new ResizeObserver(syncComposer).observe(promptLine);
syncComposer();

// Per-model settings window (gear next to a model in the drop-down): context, KV cache, sampling, reply limit and
// system prompt for that one model, saved in settings.models[name].
const GEAR_ICON = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/></svg>';
let mmName = null;
function fillModelSettings(c) {
  const m = chatModels.find(x => x.name === mmName), quant = (m ? m.kvq !== false : true) && info.device !== 'cpu';
  $('mContext').value = c.context ? c.context : '';
  $('mContext').placeholder = 'auto' + (mmName === info.file && info.contextAuto && info.context ? ' (now ' + info.context.toLocaleString() + ')' : '');
  // Only the cache types this model and engine support: q8 / q4 need a GPU and a model that keeps them accurate.
  const kvs = quant ? [{ value: 'q8', label: 'q8 (default)' }, { value: 'q4', label: 'q4' }, { value: 'f16', label: 'f16' }]
                    : [{ value: 'f16', label: info.device === 'cpu' ? 'f32' : 'f16' }];
  radios('mKv', kvs, quant ? c.kv : 'f16');
  $('mKvHint').textContent = quant ? 'memory per context token on the GPU: q8 = half of f16, q4 = a quarter · reloads the model'
    : info.device === 'cpu' ? 'CPU mode keeps the cache in f32' : 'this model keeps an f16 cache (q8 / q4 lose accuracy on it)';
  $('mMax').value = c.max_tokens; $('mTemp').value = c.temperature; $('mTopK').value = c.top_k; $('mTopP').value = c.top_p;
  $('mRepeat').value = c.repeat_penalty; $('mSystem').value = c.system;
  requestAnimationFrame(fitSystem);
  $('mNote').textContent = '';
}
function fitSystem() { const t = $('mSystem'); t.style.height = 'auto'; t.style.height = t.scrollHeight + 2 + 'px'; }   // +2: its border
$('mSystem').addEventListener('input', fitSystem);
function openModelSettings(name) {
  mmName = name;
  $('mmTitle').textContent = name.replace(/\.gguf$/i, '').toLowerCase();
  fillModelSettings(ms(name));
  $('modelModal').classList.add('show');
  $('modelModal').querySelector('.body').scrollTop = 0;
}
function closeModelSettings() { $('modelModal').classList.remove('show'); if (promptLine.isConnected) input.focus(); }
$('closeModel').onclick = closeModelSettings;
$('mReset').onclick = () => { const all = settings.models || {}, keep = all[mmName]; delete all[mmName]; fillModelSettings(ms(mmName)); if (keep) all[mmName] = keep; };   // shown only; saved with save
$('mSave').onclick = async () => {
  const num = (id, min, max, isInt) => { const v = Number($(id).value); return isFinite(v) && v >= min && v <= max && (!isInt || v === Math.floor(v)) ? v : null; };
  const ctxText = $('mContext').value.trim().toLowerCase();
  const c = { max_tokens: num('mMax', 1, 32768, true), temperature: num('mTemp', 0, 2), top_k: num('mTopK', 0, 1000, true), top_p: num('mTopP', 0, 1),
              repeat_penalty: num('mRepeat', 1, 2), context: ctxText === '' || ctxText === 'auto' || ctxText === '0' ? 0 : num('mContext', 256, 131072, true) };
  const bad = Object.keys(c).filter(k => c[k] === null);
  if (bad.length) { $('mNote').textContent = 'Check: ' + bad.join(', ').replace(/_/g, ' ') + '.'; return; }
  const kvIn = $('mKv').querySelector('input:checked'), before = ms(mmName);
  c.kv = kvIn && $('mKv').querySelector('input[value=q8]') ? kvIn.value : before.kv;   // only f16 offered: keep the preference
  c.system = $('mSystem').value;
  const def = modelDefaults(mmName);
  MODEL_KEYS.forEach(k => { if (c[k] === def[k] || (k === 'system' && c[k].trim() === String(def[k]).trim())) delete c[k]; });   // defaults stay live
  settings.models = settings.models || {}; settings.models[mmName] = c; save();
  closeModelSettings();
  // The loaded model picks up a new context or KV cache only on a reload.
  if (mmName === info.file && (c.context !== before.context || c.kv !== before.kv) && !controller) await loadModel(mmName);
};
$('modelModal').addEventListener('keydown', e => {
  if (e.key === 'Enter' && e.target.tagName !== 'TEXTAREA') { e.preventDefault(); $('mSave').click(); }
  else if (e.key === 'Escape') { e.stopPropagation(); closeModelSettings(); }
});

// Welcome logo: pinned at the centre of the chat area (a fixed top margin). Messages build up from the bottom underneath
// it and, once they reach it, push it up and out of view like any other line.
function centerEmpty() {
  const e = $('empty'); if (!e) return;
  const cs = getComputedStyle(chatEl), room = chatEl.clientHeight - parseFloat(cs.paddingTop) - parseFloat(cs.paddingBottom);
  // Keep the newest line where it is: if the view was at the bottom, it stays at the bottom after the change.
  const atBottom = chatEl.scrollHeight - chatEl.scrollTop - chatEl.clientHeight < 4;
  e.style.marginTop = Math.max(0, (room - e.offsetHeight) / 2) + 'px';
  if (atBottom) scrollDown();
}
window.addEventListener('resize', centerEmpty);
new ResizeObserver(centerEmpty).observe(promptLine);
document.addEventListener('load', e => { if (e.target.closest && e.target.closest('#empty')) centerEmpty(); }, true);
centerEmpty();
