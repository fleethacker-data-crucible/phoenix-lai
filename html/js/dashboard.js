// Server dashboard (dashboard.html on the server's own port): polls /api/server-status every second.
const $ = id => document.getElementById(id);
const esc = s => String(s == null ? '' : s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[c]);
const gb = b => (b / 1073741824).toFixed(2) + ' GB';
const cpuHist = [], tpsHist = [];
let last = null, modelsLoaded = false;

function spark(id, data, max) {
  const n = 60, m = Math.max(max || 0, ...data, 1);
  const pts = data.slice(-n).map((v, i, a) => ((i + n - a.length) * 120 / (n - 1)).toFixed(1) + ',' + (46 - v / m * 44).toFixed(1));
  $(id).innerHTML = pts.length > 1 ? '<polyline fill="none" stroke="var(--accent)" stroke-width="1.5" vector-effect="non-scaling-stroke" points="' + pts.join(' ') + '"/>' : '';
}
function kv(rows) { return rows.filter(r => r).map(r => '<b>' + esc(r[0]) + '</b><span>' + esc(r[1]) + '</span>').join(''); }
function ago(s) { return s < 60 ? s + ' s' : s < 3600 ? Math.floor(s / 60) + ' min' : Math.floor(s / 3600) + ' h'; }
function up(s) { const h = Math.floor(s / 3600), m = Math.floor(s % 3600 / 60); return (h ? h + 'h ' : '') + m + 'm ' + (s % 60) + 's'; }

async function poll() {
  let s;
  try { const r = await fetch('/api/server-status'); s = await r.json(); }
  catch (e) { $('state').className = 'state off'; $('state').lastElementChild.textContent = 'not running'; setTimeout(poll, 2000); return; }
  last = s;
  $('state').className = 'state on';
  $('state').lastElementChild.textContent = 'running · uptime ' + up(s.uptime) + (s.version ? ' · v' + s.version : '');
  const a = s.activity;
  $('act').innerHTML = a.state === 'idle' ? '<span class="empty">Idle' + (s.model ? ': ' + esc(s.model) + ' is loaded and waiting' : ': no model loaded') + '</span>'
    : a.state === 'loading' ? 'Loading <span class="gen">' + esc(a.what) + '</span>…'
    : '<span class="gen">Generating</span> ' + esc(a.what) + ' for <b>' + esc(a.source) + '</b> · ' + a.tokens + ' tokens · ' + a.seconds + ' s' + (a.tps ? ' · ' + a.tps + ' t/s' : '') + (a.active > 1 ? ' · ' + (a.active - 1) + ' more waiting' : '');
  $('model').innerHTML = kv([['file', s.model || 'none'], ['runs on', s.backend || '–'], ['context', s.context ? s.context.toLocaleString() + ' tokens' : '–'],
    ['vision', s.vision ? 'yes' : 'no'], s.loadError ? ['last error', s.loadError] : null]);
  $('cpu').innerHTML = s.cpu.toFixed(1) + '<small> % cpu</small>';
  $('cpuBar').style.width = Math.min(100, s.cpu) + '%';
  cpuHist.push(s.cpu); if (cpuHist.length > 60) cpuHist.shift(); spark('cpuSpark', cpuHist, 100);
  $('proc').innerHTML = kv([['threads', s.threads], ['pid', s.pid], ['in flight', a.active]]);
  const ram = s.ram || {}, v = s.vram;
  $('mem').innerHTML = kv([['process', gb(s.workingSet) + ' working set'], ['private', gb(s.private)], v ? ['vram', gb(v.used) + ' of ' + gb(v.budget)] : ['vram', 'no GPU model'],
    ram.total ? ['system ram', gb(ram.total - (ram.avail - ram.used)) + ' used of ' + gb(ram.total)] : null]) + (v ? '<div class="bar"><i style="width:' + Math.min(100, v.used / v.budget * 100).toFixed(1) + '%"></i></div>' : '');
  const tps = a.state === 'generating' ? a.tps : 0;
  tpsHist.push(tps); if (tpsHist.length > 60) tpsHist.shift(); spark('tpsSpark', tpsHist);
  const lastDone = s.recent.find(r => r.tps > 0);
  $('tps').innerHTML = (tps || (lastDone ? lastDone.tps : 0)).toFixed(1) + '<small> t/s ' + (tps ? 'now' : 'last') + '</small>';
  $('totals').innerHTML = kv([['requests', s.totals.requests], ['tokens out', s.totals.tokens.toLocaleString()]]);
  $('clients').innerHTML = s.clients.length ? '<table><tr><th>client</th><th>kind</th><th class="num">requests</th><th>last seen</th><th>last call</th></tr>' +
    s.clients.map(c => '<tr><td><span class="dot' + (c.connected ? ' on' : '') + '"></span>' + esc(c.name) + '</td><td>' + esc(c.kind) + '</td><td class="num">' + c.requests +
      '</td><td>' + ago(c.lastSeconds) + ' ago</td><td>' + esc(c.last) + '</td></tr>').join('') + '</table>' : '<div class="empty">No clients yet. Point any OpenAI client at ' + esc(s.api || 'the API') + '.</div>';
  $('recent').innerHTML = s.recent.length ? '<table><tr><th>time</th><th>from</th><th>kind</th><th class="num">ms</th><th class="num">in</th><th class="num">out</th><th class="num">t/s</th><th>finish</th></tr>' +
    s.recent.map(r => '<tr><td>' + r.at + '</td><td>' + esc(r.source) + '</td><td>' + esc(r.kind) + '</td><td class="num">' + r.ms + '</td><td class="num">' + r.prompt +
      '</td><td class="num">' + r.generated + '</td><td class="num">' + r.tps.toFixed(1) + '</td><td>' + esc(r.finish) + '</td></tr>').join('') + '</table>' : '<div class="empty">No requests yet.</div>';
  // CPU mode (off): $('devNote').textContent = 'now ' + s.device.toUpperCase() + ' · switching reloads the model';
  $('dbgNote').textContent = s.debug ? 'on: every step goes to the session log' : 'off';
  $('eps').textContent = (s.api ? 'OpenAI ' + s.api + (s.apiKey ? ' (API key required)' : ' (no API key)') : 'OpenAI API not listening (port 1234 busy)');
  if (!modelsLoaded) loadModels();
  setTimeout(poll, 1000);
}

async function loadModels() {
  modelsLoaded = true;
  try {
    const j = await (await fetch('/api/models')).json();
    $('sModel').innerHTML = j.models.filter(m => !/embed|mmproj/i.test(m.name)).map(m => '<option' + (last && m.name === last.model ? ' selected' : '') + '>' + esc(m.name) + '</option>').join('');
  } catch (e) { modelsLoaded = false; }
}
async function post(path, body) {
  const r = await fetch(path, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body || {}) });
  const j = await r.json().catch(() => ({}));
  if (!r.ok || j.error) throw new Error(j.error || r.statusText);
  return j;
}
function note(t) { $('sNote').textContent = t; setTimeout(() => { $('sNote').innerHTML = 'Endpoints: <span id="eps"></span>'; }, 6000); }
$('sLoad').onclick = async () => { const n = $('sModel').value; note('Loading ' + n + '…'); try { await post('/api/load', { name: n }); note('Loaded ' + n + '.'); } catch (e) { note('Could not load: ' + e.message); } };
$('sUnload').onclick = async () => { try { await post('/api/unload'); note('Model unloaded.'); } catch (e) { note(e.message); } };
// CPU mode (off): document.querySelectorAll('[data-dev]').forEach(b => b.onclick = async () => { note('Switching to ' + b.dataset.dev.toUpperCase() + '…'); try { await post('/api/device', { device: b.dataset.dev }); note('Now on ' + b.dataset.dev.toUpperCase() + '.'); } catch (e) { note(e.message); } });
$('sDebug').onclick = async () => { try { await post('/api/debug', { on: !(last && last.debug) }); } catch (e) { note(e.message); } };
poll();
