// Phoenix Client: the Providers list (left panel, at the bottom) and its add / edit window. A provider is any OpenAI /v1
// server: the Phoenix server, LM Studio, Ollama, a LAN box or an internet API, given by its base URL and API key. The
// key goes straight into Windows Credential Manager when saved and never comes back to the page. Clicking a provider
// makes it the active one: the model picker then lists its models (from its own /models), and chats go to it.
(function () {
  if (!$('providerList')) return;   // vscode.html: no providers panel
  const EDIT_ICON = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z"/></svg>';
  let providers = [], active = null, editing = null;

  async function loadProviders() {
    try { const j = await api('/client/providers'); providers = j.providers; active = j.active; } catch (e) { providers = []; }
    drawProviders();
  }
  function where(p) { return p.url.replace(/^https?:[/][/]/, ''); }
  // up = green, down = red, busy (sending or receiving) = blue, unknown = grey; the active one also while a reply streams
  let status = {};
  const STATE_TEXT = { up: 'online', down: 'offline', busy: 'sending / receiving', unknown: 'checking…' };
  function stateOf(p) { return p.id === active && controller ? 'busy' : status[p.id] || (p.id === active && info.providerError ? 'down' : 'unknown'); }
  function drawProviders() {
    $('providerList').innerHTML = providers.map(p => '<div class="sitem st-' + stateOf(p) + (p.id === active ? ' on' : '') + '" data-id="' + esc(p.id) + '" title="' +
      esc(p.name + ' · ' + STATE_TEXT[stateOf(p)] + ' · ' + p.url + (p.hasKey ? ' · key in Credential Manager' : ' · no key') + (p.model ? ' · ' + p.model : '')) + '"><i class="cdot"></i><span>' + esc(p.name) + '</span>' +
      (p.local ? '' : '<b class="ptag">cloud</b>') + '<b class="cedit" title="Edit this provider">' + EDIT_ICON + '</b></div>').join('');
  }

  async function useProvider(id, announce) {
    if (controller) return;
    let r;
    try { r = await api('/client/use', { id }); } catch (e) { systemLine('Could not switch: ' + e.message); return; }
    info = r; active = id; history = []; ctxUsed = 0;
    await loadModelList(); drawProviders(); drawStatus(false);
    const p = providers.find(x => x.id === id);
    if (announce) systemLine(info.providerError ? (p ? p.name : 'The provider') + ' is not answering: ' + info.providerError
      : 'Now on ' + (p ? p.name : id) + (info.file ? ', model ' + info.file : ' (no models offered)') + '.');
  }
  window.providerNames = () => providers.map(p => p.name);
  window.useProviderNamed = async name => {
    if (!name) { systemLine('Providers: ' + providers.map(p => p.name + (p.id === active ? ' (current)' : '')).join(', ') + '. + in the left panel adds one.'); return; }
    const n = name.toLowerCase(), p = providers.find(x => x.name.toLowerCase() === n) || providers.find(x => x.name.toLowerCase().includes(n));
    if (!p) { systemLine('No provider matches "' + name + '".'); return; }
    await useProvider(p.id, true);
  };

  $('providerList').addEventListener('click', e => {
    const it = e.target.closest('.sitem'); if (!it) return;
    const p = providers.find(x => x.id === it.dataset.id); if (!p) return;
    if (e.target.closest('.cedit')) { openProvider(p); return; }
    if (p.id !== active) useProvider(p.id, true);
  });
  $('newProvider').onclick = () => openProvider(null);

  // ---------- the add / edit window ----------
  function fillModels(list, pick) {
    const sel = $('pModel');
    sel.innerHTML = list.length ? list.map(id => '<option value="' + esc(id) + '"' + (id === pick ? ' selected' : '') + '>' + esc(id) + '</option>').join('')
      : '<option value="">' + (pick ? esc(pick) : '(load the models first)') + '</option>';
    if (pick && list.length && !list.includes(pick)) sel.insertAdjacentHTML('afterbegin', '<option value="' + esc(pick) + '" selected>' + esc(pick) + ' (not offered now)</option>');
  }
  // The model list is a themed dropdown (a native <select> list is drawn by Windows and cannot be styled); the hidden
  // select underneath still holds the value, so fillModels / form() work on it as before.
  function syncModelDd() {
    const sel = $('pModel'), opt = sel.options[sel.selectedIndex];
    $('pModelLabel').textContent = opt ? opt.textContent : '';
    // every option, invisible, in the label's grid cell: the field is as wide as the longest one (at least 100px)
    $('pModelSize').innerHTML = [...sel.options].map(o => '<span>' + esc(o.textContent) + '</span>').join('');
    $('pModelMenu').innerHTML = [...sel.options].map((o, i) => '<button type="button" data-i="' + i + '"' + (i === sel.selectedIndex ? ' class="on"' : '') + '>' + esc(o.textContent) + '</button>').join('');
  }
  new MutationObserver(syncModelDd).observe($('pModel'), { childList: true, subtree: true, attributes: true });
  $('pModelBtn').onclick = e => { e.stopPropagation(); syncModelDd(); $('pModelMenu').classList.toggle('show'); };
  $('pModelMenu').onclick = e => {
    const b = e.target.closest('button'); if (!b) return;
    $('pModel').selectedIndex = +b.dataset.i; syncModelDd(); $('pModelMenu').classList.remove('show'); $('pModelBtn').focus();
  };
  document.addEventListener('click', e => { if (!e.target.closest('.dd')) $('pModelMenu').classList.remove('show'); });
  $('pModelMenu').addEventListener('keydown', e => { if (e.key === 'Escape') { e.stopPropagation(); $('pModelMenu').classList.remove('show'); $('pModelBtn').focus(); } });
  function form() { return { id: editing ? editing.id : undefined, name: $('pName').value.trim(), url: $('pUrl').value.trim(), model: $('pModel').value, localContext: $('pLocal').checked }; }
  function drawLocalNote() {
    const on = $('pLocal').checked, here = editing && editing.phoenix;
    $('pLocalNote').textContent = on
      ? (here ? ' · docs sections, syntax sheets, RULES.md, project files and skills go with each message (this PC only)'
              : ' · ON: file tools work, and project files the model reads, RULES.md, docs sections and skills are SENT TO THIS PROVIDER')
      : ' · off: this provider gets only what you type (no docs, project files, RULES.md or skills; no file tools)';
    $('pLocalNote').classList.toggle('warn', on && !here);
  }
  $('pLocal').addEventListener('change', drawLocalNote);
  function openProvider(p) {
    editing = p;
    $('pName').value = p ? p.name : '';
    $('pUrl').value = p ? p.url : '';
    $('pKey').value = '';
    $('pKey').placeholder = p && p.hasKey ? '•••••••• stored in Credential Manager (paste a new key to replace it)' : 'paste the key (optional)';
    $('pClear').checked = false; $('pClearRow').style.display = p && p.hasKey ? '' : 'none';
    $('pDelete').style.display = p && !p.phoenix ? '' : 'none';
    // local context: always on for the Phoenix Local Server (greyed out); off by default for every other provider, saved with it
    const builtIn = !!(p && p.phoenix);
    $('pLocal').checked = builtIn || !!(p && p.localContext);
    $('pLocal').disabled = builtIn;
    $('pLocalRow').classList.toggle('locked', builtIn);
    $('pLocalRow').title = builtIn ? 'Always on: the Phoenix Local Server runs on this PC' : '';
    drawLocalNote();
    $('pName').readOnly = $('pUrl').readOnly = !!(p && p.phoenix);   // built in: fixed name and address
    drawFeatures(p ? p.features : null, p && p.testedAt ? 'from the test on ' + p.testedAt : null);
    fillModels([], p ? p.model : '');
    $('pModelNote').textContent = "asks the provider's /models with the URL and key above";
    $('pTests').innerHTML = '<span class="hint">Runs the OpenAI /v1 calls the chat uses: models, chat, streaming, embeddings.</span>';
    $('provModal').classList.add('show');
    setTimeout(() => (p ? (p.phoenix ? $('pModelBtn') : $('pKey')) : $('pName')).focus(), 50);
    if (p) loadModels(true);
  }
  // The provider's /v1 features: allowed (green) or not (grey, struck out; the client never sends them)
  let featureList = [];
  api('/client/features').then(j => { featureList = j.featureList; }).catch(() => {});
  window.featureLabel = k => (featureList.find(x => x.key === k) || { label: k }).label;
  function drawFeatures(f, note) {
    const el = $('pFeatures'); if (!el) return;
    if (!f) { el.innerHTML = '<span class="hint">Not tested yet: Test checks every OpenAI /v1 feature. Until then the client tries them all.</span>'; return; }
    el.innerHTML = featureList.map(x => {
      const v = f[x.key];
      return '<span class="feat ' + (v === true ? 'yes' : v === false ? 'no' : 'unk') + '" title="' + esc(v === true ? 'allowed' : v === false ? 'not allowed: the client does not use it' : 'not tested') + '">' + (v === true ? '✓ ' : v === false ? '✗ ' : '? ') + esc(x.label) + '</span>';
    }).join('') + (note ? '<span class="hint fnote">' + esc(note) + '</span>' : '');
  }
  function closeProvider() { $('pKey').value = ''; $('provModal').classList.remove('show'); if (promptLine.isConnected) input.focus(); }
  async function loadModels(quiet) {
    const f = form(), key = $('pKey').value.trim();
    if (!f.url) { $('pModelNote').textContent = 'Give the base URL first.'; return; }
    $('pModelNote').textContent = 'Asking ' + f.url + '/models…';
    try {
      const j = await api('/client/provider-models', { id: f.id, url: f.url, key: key || undefined });
      fillModels(j.models, f.model || (editing && editing.model) || j.models[0] || '');
      $('pModelNote').textContent = j.models.length + ' model' + (j.models.length === 1 ? '' : 's') + ' from the provider';
    } catch (e) { $('pModelNote').textContent = quiet ? 'Could not list the models: ' + e.message : 'Could not list the models: ' + e.message; }
  }
  $('pLoad').onclick = () => loadModels(false);
  $('closeProv').onclick = closeProvider;
  $('provModal').addEventListener('keydown', e => {
    if (e.key === 'Escape') { e.stopPropagation(); closeProvider(); }
    else if (e.key === 'Enter' && e.target.tagName !== 'BUTTON' && e.target.tagName !== 'SELECT') { e.preventDefault(); $('pSave').click(); }
  });
  $('pTest').onclick = async () => {
    const f = form(), key = $('pKey').value.trim();
    $('pTests').innerHTML = '<span class="hint">Testing ' + esc(f.url) + '…</span>';
    $('pTest').disabled = true;
    try {
      const j = await api('/client/provider-test', { id: f.id, url: f.url, model: f.model, key: key || undefined });
      const mark = { ok: '✓', warn: '!', fail: '✗', na: '–' };
      $('pTests').innerHTML = j.results.map(r => '<div class="row"><span class="' + r.state + '">' + mark[r.state] + '</span><span>' + esc(r.name) + '</span><span class="ms">' + r.ms + ' ms</span><span class="d">' + esc(r.detail) + '</span></div>').join('') +
        (j.info || []).map(s => '<details class="pinfo"' + (s.title === 'Connection' || s.title.startsWith('GET /models (') ? ' open' : '') + '><summary>' + esc(s.title) + '</summary>' +
          (s.rows.length ? '<div class="kv">' + s.rows.map(r => '<span class="k">' + esc(r[0]) + '</span><span>' + esc(r[1]) + '</span>').join('') + '</div>' : '') +
          (s.raw != null ? '<details class="praw"><summary>raw JSON</summary><pre>' + esc(JSON.stringify(s.raw, null, 2)) + '</pre></details>' : '') + '</details>').join('');
      if (j.models && j.models.length) fillModels(j.models, f.model || j.models[0]);
      if (j.featureList) featureList = j.featureList;
      drawFeatures(j.features, j.saved ? 'saved: the client now uses only the allowed ones' : 'not saved (save the provider, then test it at this address)');
      if (j.saved) loadProviders();
    } catch (e) { $('pTests').innerHTML = '<div class="row"><span class="fail">✗</span><span>test</span><span></span><span class="d">' + esc(e.message) + '</span></div>'; }
    finally { $('pTest').disabled = false; }
  };
  $('pSave').onclick = async () => {
    const f = form(), key = $('pKey').value.trim();
    if (!f.name) { $('pName').focus(); return; }
    if (!f.url) { $('pUrl').focus(); return; }
    const body = Object.assign({}, f);
    if (key) body.key = key; else if ($('pClear').checked) body.clearKey = true;
    $('pKey').value = '';   // the key leaves the page now: Credential Manager keeps it
    try {
      const j = await api('/client/provider-save', body);
      closeProvider();
      await loadProviders();
      const isNew = !editing;
      if (isNew || j.provider.id === active) await useProvider(j.provider.id, true);
      else systemLine('Saved ' + j.provider.name + (j.provider.hasKey ? ' (API key in Windows Credential Manager).' : '.'));
    } catch (e) { $('pTests').innerHTML = '<div class="row"><span class="fail">✗</span><span>save</span><span></span><span class="d">' + esc(e.message) + '</span></div>'; }
  };
  $('pDelete').onclick = async () => {
    if (!editing || !confirm('Delete the provider "' + editing.name + '" and its stored API key?')) return;
    const was = editing.id === active;
    try { await api('/client/provider-delete', { id: editing.id }); } catch (e) { systemLine(e.message); }
    closeProvider(); await loadProviders();
    if (was) await useProvider('phoenix', true);
  };

  // keep the list's "on" dot in step with the page's info (refresh() polls it)
  setInterval(() => { if (info.provider && info.provider.id !== active) { active = info.provider.id; drawProviders(); } }, 2000);
  // live status: the server checks in the background, so this poll answers at once
  let lastDrawn = '', reconnect = false;
  window.activeProviderDown = () => !!active && status[active] === 'down';
  window.providersChanged = async () => { await loadProviders(); if (info.provider) active = info.provider.id; drawProviders(); };
  async function pollStatus() {
    try { status = (await api('/client/provider-status')).status || {}; } catch (e) { status = {}; }
    // the model picker works only while some provider is online
    const anyUp = providers.some(p => status[p.id] === 'up' || status[p.id] === 'busy');
    const known = providers.some(p => status[p.id] && status[p.id] !== 'unknown');
    const pick = $('modelPick');
    if (pick) {
      const dis = known && !anyUp;
      if (pick.classList.contains('dis') !== dis) { pick.classList.toggle('dis', dis); pick.title = dis ? 'No provider is online' : ''; if (dis) $('modelMenu').classList.remove('show'); drawStatus(false); }
      else if (window.activeProviderDown() !== (pick.dataset.down === '1')) { pick.dataset.down = window.activeProviderDown() ? '1' : ''; drawStatus(false); }
    }
    // the active provider came back: reconnect (its models, the top bar) without waiting for the user
    // (the conversation is kept, unlike a switch); tried again each second until its model is back (it may still be starting)
    if (active && (status[active] === 'down' || info.providerError)) reconnect = true;
    if (reconnect && active && status[active] === 'up' && !controller) {
      try { info = await api('/client/use', { id: active }); await loadModelList(); drawStatus(false); } catch (e) { }
      if (!info.providerError && info.model) {
        reconnect = false;
        systemLine((providers.find(x => x.id === active) || { name: 'The provider' }).name + ' is back, model ' + info.model + '.');
      }
    }
    const key = providers.map(stateOf).join() + active;
    if (key !== lastDrawn) { lastDrawn = key; drawProviders(); }
  }
  setInterval(pollStatus, 1000);

  // ---------- the client's own back end stopped ----------
  // Two missed checks in a row (about 2 s): blur the page and offer Exit (close this window). The client is a program
  // of its own (the server knows nothing of it): started again from PhoenixLAI_Client.lnk, it adopts this window, which
  // then reloads by itself.
  let missed = 0, downShown = false;
  async function clientAlive() {
    try { const r = await fetch('/client/provider-status', { cache: 'no-store', signal: AbortSignal.timeout(1500) }); return r.ok; } catch (e) { return false; }
  }
  function showDown(text, note) {
    $('downText').textContent = text; $('downNote').textContent = note || '';
    if (!downShown) { downShown = true; $('downModal').classList.add('show'); $('downExit').focus(); }
  }
  async function watchBackend() {
    const alive = await clientAlive();
    if (alive) {
      missed = 0;
      if (downShown) { $('downNote').textContent = 'back, reloading…'; location.reload(); }
      return;
    }
    if (++missed >= 2 && !downShown) showDown("The Phoenix Client has stopped. Start it again with Phoenix_Client.lnk (this window then reconnects by itself), or exit.", 'waiting for the client…');
  }
  setInterval(watchBackend, 1000);
  $('downExit').onclick = () => {
    window.close();
    setTimeout(() => { $('downNote').textContent = 'close this window to exit'; }, 400);
  };
  setInterval(() => { const key = providers.map(stateOf).join() + active; if (key !== lastDrawn) { lastDrawn = key; drawProviders(); } }, 200);   // blue as soon as a chat starts
  loadProviders();
})();
