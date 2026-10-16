const $ = id => document.getElementById(id);
const chatEl = $('chat'), input = $('input'), promptLine = $('promptLine');
const mirror = $('mirror');
let history = [], info = {}, models = [], controller = null, browseAt = '', filesAt = '.';
const settings = { temperature: 0.7, top_k: 40, top_p: 0.95, repeat_penalty: 1.1, max_tokens: 2048,
                   system: 'You are a concise, expert assistant running locally.' };
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
  if (!e.target.classList.contains('copy')) return;
  const pre = e.target.closest('.code').querySelector('pre');
  navigator.clipboard.writeText(pre.textContent).then(() => { e.target.textContent = 'Copied'; setTimeout(() => e.target.textContent = 'Copy', 1200); });
});

function scrollDown() { chatEl.scrollTop = chatEl.scrollHeight; }

// Status line: one centered row of icon + value items separated by "|". Empty items are left out.
const SICON = {
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
function statusItem(icon, text, lower) {
  return '<span class="item' + (lower ? ' lower' : '') + '"><svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
         'stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">' + SICON[icon] + '</svg>' + esc(text) + '</span>';
}
function drawStatus(offline) {
  const items = [];
  if (offline) items.push(statusItem('model', 'server offline'));
  else {
    items.push(statusItem('folder', info.folder ? info.folderName : 'no folder'));
    const g = info.git;
    if (g) {
      items.push(statusItem('branch', (g.branch || 'detached') + (g.commit ? ' @' + g.commit : '')));
      if (g.changed !== null && g.changed !== undefined) items.push(statusItem('changes', g.changed ? g.changed + ' changed' : 'clean'));
    }
    if (info.rules && info.rules.length) items.push(statusItem('rules', 'rules: ' + info.rules.join(' + ')));
    const rg = info.rag;
    if (rg && rg.indexing) items.push(statusItem('rag', 'indexing: ' + rg.indexing));
    else if (rg && rg.chunks) items.push(statusItem('rag', 'rag' + (rg.enabled ? '' : ' off') + ': ' + rg.files + ' docs · ' + rg.chunks + ' chunks'));
    if (lastTps) items.push(statusItem('speed', lastTps + ' t/s'));
    if (info.file) items.push(statusItem('ctx', ctxUsed.toLocaleString() + '/' + (info.context || 0).toLocaleString() + ' tokens'));
    items.push(statusItem('model', info.loading ? 'loading ' + info.loading.replace(/\.gguf$/i, '') + '…' : (info.file ? info.file.replace(/\.gguf$/i, '') : 'no model'), true));
    if (info.backend) items.push(statusItem('gpu', info.backend.replace(/^GPU: /, ''), true));
  }
  $('status').innerHTML = items.join('<span class="sep">|</span>');
}
async function refresh() {
  try { info = await api('/api/info'); } catch (e) { drawStatus(true); return; }
  drawStatus(false);
  const ver = $('ver'); if (ver && info.version) ver.textContent = 'v.' + info.version;
  if (info.loading || (info.rag && info.rag.indexing)) setTimeout(refresh, 700);
}
// Keep the status line (git changes, model state) current.
setInterval(() => { if (!controller) refresh(); }, 4000);

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

// ---------- folder browser window ----------
async function browse(path) {
  browseAt = path || '';
  let r;
  try { r = await api('/api/browse?path=' + encodeURIComponent(browseAt)); } catch (e) { $('dirList').innerHTML = '<div class="err">' + esc(e.message) + '</div>'; return; }
  $('browsePath').textContent = r.path || 'This PC';
  let html = r.parent !== null ? '<div data-path="' + esc(r.parent) + '">' + ICON_DIR + '..</div>' : '';
  for (const d of r.dirs) html += '<div data-path="' + esc(d.path) + '">' + ICON_DIR + esc(d.name) + '</div>';
  $('dirList').innerHTML = html || '<div style="cursor:default;color:var(--dim)">(no subfolders)</div>';
  $('connect').disabled = !r.path;
}
$('dirList').addEventListener('click', e => { const d = e.target.closest('[data-path]'); if (d) browse(d.dataset.path); });
function openFolder() { $('folderModal').classList.add('show'); browse(info.folder || browseAt); }
$('closeFolder').addEventListener('click', () => $('folderModal').classList.remove('show'));
$('connect').addEventListener('click', async () => {
  try { info = await api('/api/folder', { path: browseAt }); } catch (e) { alert(e.message); return; }
  $('folderModal').classList.remove('show');
  await refresh();
  systemLine('Connected ' + info.folderName + ' — the model can now list, read and write files there.');
});
$('disconnect').addEventListener('click', async () => {
  await api('/api/folder', { path: '' });
  $('folderModal').classList.remove('show');
  await refresh(); $('files').classList.remove('show');
  systemLine('Folder disconnected.');
});

// ---------- settings window ----------
async function openSettings() {
  $('sContext').value = info.context || 4096; $('sMax').value = settings.max_tokens; $('sTemp').value = settings.temperature;
  $('sTopK').value = settings.top_k; $('sTopP').value = settings.top_p; $('sRepeat').value = settings.repeat_penalty; $('sSystem').value = settings.system;
  $('loadNote').textContent = '';
  try { models = (await api('/api/models')).models; } catch (e) { models = []; }
  $('modelList').innerHTML = models.map(m => '<label class="' + (m.name === info.file ? 'current' : '') + '"><input type="radio" name="model" value="' + esc(m.name) + '"' +
    (m.name === info.file ? ' checked' : '') + '>' + esc(m.name.replace(/\.gguf$/i, '').toLowerCase()) + '<span class="mb">' + (m.mb / 1024).toFixed(1) + ' GB</span></label>').join('') ||
    '<div class="note">No .gguf files in the models folder.</div>';
  $('settingsModal').classList.add('show');
}

$('closeSettings').addEventListener('click', () => $('settingsModal').classList.remove('show'));
$('applySettings').addEventListener('click', async () => {
  settings.max_tokens = +$('sMax').value; settings.temperature = +$('sTemp').value; settings.top_k = +$('sTopK').value;
  settings.top_p = +$('sTopP').value; settings.repeat_penalty = +$('sRepeat').value; settings.system = $('sSystem').value; save();
  const sel = document.querySelector('input[name=model]:checked');
  const ctx = +$('sContext').value;
  if (sel && (sel.value !== info.file || ctx !== info.context)) {
    $('applySettings').disabled = true; $('loadNote').textContent = 'Loading ' + sel.value + '…';
    setTimeout(refresh, 300);
    const t0 = performance.now();
    try { info = await api('/api/load', { name: sel.value, context: ctx }); }
    catch (e) { $('loadNote').textContent = 'Could not load: ' + e.message; $('applySettings').disabled = false; return; }
    $('applySettings').disabled = false;
    systemLine('Loaded ' + info.model + ' on ' + info.backend + ' in ' + ((performance.now() - t0) / 1000).toFixed(1) + ' s, context ' + info.context + '.');
    history = []; ctxUsed = 0;
  }
  $('settingsModal').classList.remove('show');
  refresh();
});
// Welcome screen: shown on start and after every New chat, with the prompt line under it.
function welcome() {
  promptLine.remove();
  chatEl.innerHTML = '<div class="empty" id="empty"><img class="logo" src="assets/phoenix_logo.png" alt="PhoenixAI">' +
    // The empty .sub line keeps the hint where it always was; the version hangs below it without moving anything.
    '<div class="sub">&nbsp;</div>' +
    '<div class="hint">Everything runs locally on this machine. Use /help to list all commands.' +
    '<div class="ver" id="ver">' + (info.version ? 'v.' + esc(info.version) : '') + '</div></div></div>';
  placePrompt();
}
// The prompt always sits at the end of the conversation.
function placePrompt() { chatEl.appendChild(promptLine); drawPrompt(); scrollDown(); }
function newChat() { if (controller) controller.abort(); history = []; ctxUsed = 0; if (info.file) drawStatus(false); welcome(); document.querySelectorAll('.modal.show').forEach(m => m.classList.remove('show')); input.focus(); }
welcome();
$('newChat').addEventListener('click', newChat);

// Right-click menu.
const menu = $('ctxmenu');
document.addEventListener('contextmenu', e => {
  if (e.target.closest('.modal')) return;               // normal menu inside windows
  e.preventDefault();
  const sel = window.getSelection().toString();
  menu.querySelector('[data-cmd=copy]').classList.toggle('off', !sel);
  menu.querySelector('[data-cmd=stop]').classList.toggle('off', !controller);
  menu.querySelector('[data-cmd=files]').classList.toggle('off', !info.folder);
  menu.querySelector('[data-cmd=files]').textContent = $('files').classList.contains('show') ? 'Hide files' : 'Files';
  menu.classList.add('show');
  const w = menu.offsetWidth, h = menu.offsetHeight;
  menu.style.left = Math.min(e.clientX, innerWidth - w - 6) + 'px';
  menu.style.top = Math.min(e.clientY, innerHeight - h - 6) + 'px';
  menu.dataset.sel = sel;
});
document.addEventListener('mousedown', e => { if (!e.target.closest('#ctxmenu')) menu.classList.remove('show'); });
menu.addEventListener('click', e => {
  const item = e.target.closest('[data-cmd]');
  if (!item) return;
  menu.classList.remove('show');
  switch (item.dataset.cmd) {
    case 'new': newChat(); break;
    case 'settings': openSettings(); break;
    case 'folder': openFolder(); break;
    case 'files': if ($('files').classList.contains('show')) $('files').classList.remove('show'); else showFiles(filesAt); break;
    case 'copy': navigator.clipboard.writeText(menu.dataset.sel || ''); break;
    case 'stop': if (controller) controller.abort(); break;
    case 'index': reindex(); break;
  }
});
// Clicking anywhere in the conversation (without selecting text) puts you back at the prompt.
chatEl.addEventListener('mouseup', e => {
  if (e.button !== 0 || window.getSelection().toString() || e.target.closest('button, a, .code')) return;
  if (promptLine.isConnected) input.focus();
});
document.addEventListener('keydown', e => {
  if (e.key === 'Escape') {
    if (controller) controller.abort();
    menu.classList.remove('show');
    document.querySelectorAll('.modal.show').forEach(m => m.classList.remove('show'));
  }
});

// ---------- chat ----------
// Notices keep the welcome screen (and the prompt's place under it); they sit just above the prompt.
function systemLine(text) {
  const d = document.createElement('div'); d.className = 'meta'; d.style.margin = '0 0 18px'; d.style.whiteSpace = 'pre-wrap'; d.textContent = text;
  chatEl.insertBefore(d, promptLine.isConnected ? promptLine : null); scrollDown();
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
// Slash commands, A-Z. `use` is the full syntax shown by /help; commands without `run` take arguments.
const COMMANDS = [
  { name: '/files', use: '/files', desc: 'show or hide the connected folder\'s files', run: () => { if (!info.folder) systemLine('No folder connected — use /folder.'); else if ($('files').classList.contains('show')) $('files').classList.remove('show'); else showFiles(filesAt); } },
  { name: '/folder', use: '/folder', desc: 'connect a project folder the model can read and write', run: () => openFolder() },
  { name: '/help', use: '/help', desc: 'list all commands with full syntax', run: () => systemLine('Commands:\n' + COMMANDS.map(c => '  ' + c.use + '  —  ' + c.desc).join('\n')) },
  { name: '/index', use: '/index', desc: 'reindex new and changed files in docs\\', run: () => reindex() },
  { name: '/new', use: '/new', desc: 'start a new chat', run: () => newChat() },
  { name: '/rag', use: '/rag [on | off | <words>]', desc: 'docs status, turn retrieval on/off, or preview a search',
    subs: [{ name: 'on', desc: 'add matching docs to every message' }, { name: 'off', desc: 'stop adding docs to messages' },
           { name: '<words>', desc: 'preview which docs a message would retrieve' }] },
  { name: '/settings', use: '/settings', desc: 'model, context and sampling settings', run: () => openSettings() }
];

async function command(text) {
  const [cmd, ...rest] = text.split(/\s+/), arg = rest.join(' ');
  if (cmd === '/reindex') { await reindex(); return true; }
  const c = COMMANDS.find(c => c.name === cmd.toLowerCase());
  if (!c) { systemLine('Unknown command ' + cmd + ' — type /help for the list.'); return true; }
  if (c.run) { await c.run(); return true; }
  if (cmd === '/rag') {
    if (arg === 'on' || arg === 'off') { await api('/api/rag', { enabled: arg === 'on' }); systemLine('Docs retrieval ' + arg + '.'); refresh(); return true; }
    if (!arg) { const r = (await api('/api/info')).rag; systemLine('Docs: ' + r.files + ' files, ' + r.chunks + ' chunks, retrieval ' + (r.enabled ? 'on' : 'off') + '. Commands: /index, /rag on|off, /rag <words>.'); return true; }
    const r = await api('/api/rag?q=' + encodeURIComponent(arg));
    systemLine(r.hits.length ? r.hits.length + ' chunks in ' + r.ms + ' ms: ' + r.hits.map(h => '[' + h.n + '] ' + h.file + (h.title ? ' › ' + h.title : '') + ' (' + h.score + ')').join('  ') : 'No matching docs (' + r.ms + ' ms).');
    return true;
  }
  return false;
}

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
  if (!info.file) { systemLine('No model loaded — right-click > Settings and pick one.'); return; }
  input.value = ''; autosize();
  const empty = $('empty'); if (empty) empty.remove();
  promptLine.remove();   // the prompt comes back under the reply when it is done

  const turn = document.createElement('div'); turn.className = 'turn';
  turn.innerHTML = '<div class="user"></div>';
  turn.querySelector('.user').textContent = text;
  chatEl.appendChild(turn);
  let out = document.createElement('div'); out.className = 'reply'; turn.appendChild(out);
  // Typewriter: `reply` is everything received, `shown` how much is on screen. The cursor sits right after the last
  // shown character (also inside code boxes); it is solid while characters are appearing and blinks when paused.
  let reply = '', shown = 0, lastGrow = 0, raf = 0;
  const MARK = '\u0001';
  const draw = typing => {
    const html = render(reply.slice(0, shown) + MARK, true);
    out.innerHTML = html.replace(MARK, '<span class="caret' + (typing ? ' typing' : '') + '"></span>');
    scrollDown();
  };
  const tick = () => {
    const backlog = reply.length - shown;
    if (backlog > 0) {
      shown += Math.max(1, Math.ceil(backlog / 6));   // catch up smoothly when tokens arrive in bursts
      lastGrow = performance.now();
      draw(true);
    } else if (performance.now() - lastGrow > 450) draw(false);
  };
  const flush = () => { shown = reply.length; };
  const paint = () => {};
  draw(false);
  // A timer (not requestAnimationFrame) so the reveal keeps going when the window is hidden or minimized.
  raf = setInterval(tick, 16);

  history.push({ role: 'user', content: text });
  const messages = settings.system ? [{ role: 'system', content: settings.system }, ...history] : [...history];
  controller = new AbortController();
  let appended = false, t0 = performance.now(), firstAt = 0, wrote = false;
  try {
    const body = { messages, temperature: settings.temperature, top_k: settings.top_k, top_p: settings.top_p,
                   repeat_penalty: settings.repeat_penalty, max_tokens: settings.max_tokens };
    const res = await fetch('/api/chat', { method: 'POST', signal: controller.signal, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
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
        if (ev.t !== undefined) { if (!firstAt) firstAt = performance.now(); reply += ev.t; }
        else if (ev.rag) {
          const d = document.createElement('div'); d.className = 'sources';
          // One short line (files used); click it to see the exact sections that were added.
          const files = [...new Set(ev.rag.map(h => h.file))];
          d.innerHTML = '<b>docs</b> ' + ev.rag.length + ' section' + (ev.rag.length > 1 ? 's' : '') + ' from ' + files.map(esc).join(', ') +
            '<div class="detail">' + ev.rag.map(h => '[' + h.n + '] ' + esc(h.file) + (h.title ? ' › ' + esc(h.title) : '')).join('<br>') + '</div>';
          d.title = 'Click to show which sections';
          d.addEventListener('click', () => d.classList.toggle('open'));
          turn.insertBefore(d, out);
        } else if (ev.tool) {
          flush(); out.innerHTML = render(reply, false);
          const t = document.createElement('div'); t.className = 'tool' + (ev.tool.ok ? '' : ' fail');
          t.textContent = (ev.tool.ok ? '✓ ' : '✗ ') + ev.tool.msg; turn.appendChild(t);
          if (ev.tool.op === 'write' && ev.tool.ok) wrote = true;
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
          turn.appendChild(m);
          lastTps = ev.tps; if (ev.ctx_used !== undefined) ctxUsed = ev.ctx_used; drawStatus(false);
        }
      }
    }
  } catch (e) {
    const m = document.createElement('div'); m.className = 'meta'; m.textContent = e.name === 'AbortError' ? 'stopped' : 'error: ' + e.message; turn.appendChild(m);
  }
  // Let the typewriter finish revealing what arrived, then render the final text without a cursor.
  while (shown < reply.length && !document.hidden && !(controller && controller.signal.aborted)) await new Promise(r => setTimeout(r, 16));
  clearInterval(raf);
  out.innerHTML = render(reply, false);
  if (!appended) history.push({ role: 'assistant', content: reply });
  controller = null;
  placePrompt(); input.focus();
  if (wrote) { refresh(); if ($('files').classList.contains('show')) showFiles(filesAt); }
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
  autosize();
});
// Command list while typing "/name": filtered A-Z, arrows to move, Tab or click to complete, Enter to run.
const cmdBox = $('cmds');
let cmdMatches = [], cmdSel = 0;
// Every command plus its sub-commands (e.g. "/rag on"), A-Z. `run`: completing it can run at once.
const CMD_ENTRIES = [];
for (const c of COMMANDS) {
  // One row per command; sub-commands shown inline, e.g. "/rag on|off|<words>".
  CMD_ENTRIES.push({ name: c.name, label: c.name + (c.subs ? ' ' + c.subs.map(s => s.name).join('|') : ''), desc: c.desc, run: !!c.run, insert: c.name + (c.run ? '' : ' ') });
}
function drawCmds() {
  const v = input.value.toLowerCase();
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
input.addEventListener('input', () => { cmdSel = 0; drawCmds(); });
input.addEventListener('blur', () => setTimeout(() => { if (document.activeElement !== input) cmdBox.classList.remove('show'); }, 100));
input.addEventListener('keydown', e => {
  if (cmdBox.classList.contains('show')) {
    const c = cmdMatches[cmdSel];
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { e.preventDefault(); cmdSel = (cmdSel + (e.key === 'ArrowDown' ? 1 : cmdMatches.length - 1)) % cmdMatches.length; drawCmds(); return; }
    if (e.key === 'Tab') { e.preventDefault(); completeCmd(c); return; }
    // Enter on a partly typed name picks the highlighted command; it runs at once unless it takes arguments.
    if (e.key === 'Enter' && !e.shiftKey && input.value.toLowerCase().trim() !== c.name) { e.preventDefault(); completeCmd(c); if (!c.run) return; }
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
alignChevrons();
document.fonts.ready.then(alignChevrons);
document.fonts.addEventListener('loadingdone', alignChevrons);

refresh();
// Ready to type as soon as the page loads (and whenever the window comes back to the front).
window.addEventListener('load', () => input.focus());
window.addEventListener('focus', () => { if (promptLine.isConnected && !document.querySelector('.modal.show')) input.focus(); });
input.focus();
