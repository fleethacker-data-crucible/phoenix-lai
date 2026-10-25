// Server console (console.html): a plain command prompt. Sends one line to /api/console and prints the answer.
// Phoenix's own commands only (help lists them); the server never runs a shell, a script or a program from here.
const $ = id => document.getElementById(id);
const term = $('term'), out = $('out'), cmd = $('cmd'), line = document.querySelector('.cmdline');
const hist = []; let hi = 0, busy = false;

let who = 'user@phoenix';
const escH = s => String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const bottom = () => { term.scrollTop = term.scrollHeight; };
// The typed line with the chat's block cursor where the caret is.
function drawPrompt() {
  const v = cmd.value, at = cmd.selectionEnd == null ? v.length : cmd.selectionEnd;
  $('mirror').innerHTML = escH(v.slice(0, at)) + '<span class="caret"></span>' + escH(v.slice(at));
}
['input', 'keyup', 'click', 'select'].forEach(e => cmd.addEventListener(e, drawPrompt));
function echo(text) {
  const d = document.createElement('div'); d.className = 'in';
  d.innerHTML = '<span class="who">' + escH(who) + '</span><span class="chev">›</span><span>' + escH(text) + '</span>';
  out.appendChild(d); bottom();
}
function print(text, cls) { const d = document.createElement('div'); if (cls) d.className = cls; d.textContent = text; out.appendChild(d); bottom(); }

async function run(text) {
  echo(text);
  if (text === 'clear' || text === 'cls') { out.innerHTML = ''; return; }
  busy = true; line.classList.add('busy');   // like a terminal: no prompt while a command runs
  try {
    const r = await fetch('/api/console', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ line: text }) });
    const j = await r.json();
    if (!r.ok || j.error) print(j.error || r.statusText, 'err res');
    else if (j.text) print(j.text, 'res');
  } catch (e) { print('The server is not answering: ' + e.message, 'err res'); }
  busy = false; line.classList.remove('busy'); cmd.focus(); bottom();
  if (/^restart/.test(text)) setTimeout(user, 3000);
}

// The prompt's name: the Windows user the server runs as.
async function user() {
  try { const s = await (await fetch('/api/server-status')).json(); if (s.user) { who = s.user + '@phoenix'; $('who').textContent = who; } } catch (e) { }
}

cmd.addEventListener('keydown', e => {
  if (busy) { e.preventDefault(); return; }
  if (e.key === 'Enter') { const v = cmd.value.trim(); cmd.value = ''; if (v) { hist.push(v); hi = hist.length; run(v); } else echo(''); drawPrompt(); }
  else if (e.key === 'ArrowUp') { if (hi > 0) { cmd.value = hist[--hi]; e.preventDefault(); setTimeout(drawPrompt, 0); } }
  else if (e.key === 'ArrowDown') { if (hi < hist.length) { cmd.value = hist[++hi] || ''; e.preventDefault(); setTimeout(drawPrompt, 0); } }
  else if (e.key === 'Tab') { e.preventDefault(); complete(e.shiftKey); }
  if (e.key !== 'Tab' && e.key !== 'Shift') tabState = null;   // any other key ends a Tab cycle
});

// Tab completion like cmd: Tab fills in the first match, Tab again cycles through the rest (Shift+Tab goes back).
// Completes command names, then each command's arguments (model names for load, on/off, apikey's words, ...).
let tabState = null, models = [];
async function loadModels() { try { models = (await (await fetch('/api/models')).json()).models.map(m => m.name.replace(/\.gguf$/i, '')); } catch (e) { } }
const ARGS = { load: () => models, debug: () => ['on', 'off'], apikey: () => ['new', 'set ', 'off'], bench: () => ['64', '128', '256', '512'], log: () => ['10', '30', '100'] };
const ANYWHERE = { load: true };   // model names: "load e2b" finds gemma-4-e2b_... (other words match from the start)
const CMDS = ['help', 'status', 'clients', 'recent', 'models', 'load', 'unload', 'bench', 'apitest', 'apikey', 'vram', 'debug', 'log', 'gc', 'restart', 'clear'];
function complete(back) {
  if (!tabState) {
    const v = cmd.value, sp = v.indexOf(' ');
    let base, word, list, anywhere = false;
    if (sp < 0) { base = ''; word = v; list = CMDS.map(c => ARGS[c] ? c + ' ' : c); }
    else { const c = v.slice(0, sp).toLowerCase(); base = v.slice(0, sp + 1); word = v.slice(sp + 1); list = ARGS[c] ? ARGS[c]() : []; anywhere = !!ANYWHERE[c]; }
    const w = word.toLowerCase(), m = list.filter(x => x.toLowerCase().startsWith(w))
      .concat(anywhere ? list.filter(x => !x.toLowerCase().startsWith(w) && x.toLowerCase().includes(w)) : []);
    if (!m.length) return;
    tabState = { base, m, i: back ? m.length - 1 : 0 };
  } else tabState.i = (tabState.i + (back ? -1 : 1) + tabState.m.length) % tabState.m.length;
  cmd.value = tabState.base + tabState.m[tabState.i];
  cmd.setSelectionRange(cmd.value.length, cmd.value.length);
  drawPrompt();
  if (tabState.m.length === 1) tabState = null;   // one match: the next Tab completes the argument after it
}
document.addEventListener('click', () => { if (!getSelection().toString()) cmd.focus(); });
print('Phoenix server console. Type help for the commands (Tab completes, ↑ ↓ history).', 'dimline');
drawPrompt(); user(); loadModels();
