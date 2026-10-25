// vscode.html only: the composer box at the bottom (input on top; + new chat, / commands, model chip, Auto / Accept
// mode and a gold send button below). Uses chat.js's own functions, so it behaves exactly like typing in the Phoenix Client.
(function () {
  const $ = (id) => document.getElementById(id);
  const ctxEl = $('pbCtx'), line = $('promptLine'), send = $('pbSend'), modelChip = $('pbModel'), modeBtn = $('pbMode');

  function update() {
    line.classList.toggle('empty', !input.value);
    send.disabled = !input.value.trim();
    const file = (info && info.file) || '';
    const pDown = window.activeProviderDown && window.activeProviderDown();
    modelChip.textContent = info && info.loading ? 'loading…' : pDown ? (info.provider ? info.provider.name : 'provider') + ' offline'
      : file ? String(file).split(/[\\/]/).pop().replace(/\.gguf$/i, '') : 'Select model';
    modeBtn.textContent = info && info.mode === 'accept' ? 'Accept' : 'Auto';
    const ctx = (info && info.context) || 0, f = ctx ? ctxUsed / ctx : 0;
    const k = (n) => n >= 1000 ? (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'K' : String(n);
    ctxEl.textContent = ctx ? k(ctxUsed) + ' / ' + k(ctx) : '';
    ctxEl.className = 'ctx ' + (f < 0.7 ? 'ok' : f < 0.9 ? 'gold' : 'bad');
    ctxEl.title = ctx ? ctxUsed.toLocaleString() + ' of ' + ctx.toLocaleString() + ' context tokens used by this chat' : '';
    modelChip.title = 'Model: ' + (file || 'none') + (info && info.provider ? ' on ' + info.provider.name : '') + (info && info.folder ? ' · folder ' + info.folder : '') + ' (click to change)';
  }

  $('pbNew').onclick = () => newChat();
  $('pbCmd').onclick = () => {
    input.value = '/';
    input.focus();
    input.setSelectionRange(1, 1);
    input.dispatchEvent(new Event('input', { bubbles: true }));
  };
  // every provider's models, as in the Phoenix Client's top bar (the gear next to a model opens its settings)
  modelChip.onclick = (e) => { e.stopPropagation(); toggleModelMenu($('modelMenu')); };
  $('pbSettings').onclick = () => openSettings();
  modeBtn.onclick = async () => {
    const m = info && info.mode === 'accept' ? 'auto' : 'accept';
    try { info = await api('/api/mode', { mode: m }); } catch (e) { /* the client reports it */ }
    update();
    input.focus();
  };
  send.onclick = () => {
    if (!input.value.trim()) return;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
  };

  // Clicking anywhere in the box (not on a button) puts the cursor in the input.
  line.addEventListener('mousedown', (e) => {
    if (e.target.closest('button') || e.target === input) return;
    e.preventDefault();
    input.focus();
  });
  // When the model starts editing a file, open it in VS Code at once (focus stays here); the edit shows there when saved.
  const opened = new Set();
  new MutationObserver(() => {
    for (const a of document.querySelectorAll('.act.edit[data-path]')) {
      const key = a.dataset.path;
      if (a.dataset.opened || opened.has(key + '|' + history.length)) continue;
      a.dataset.opened = '1';
      opened.add(key + '|' + history.length);
      window.parent.postMessage({ type: 'phoenix-open', path: key, auto: true }, '*');
    }
  }).observe(document.getElementById('chat'), { childList: true, subtree: true });
  // Activity lines: click an edited / read file to open it in VS Code (the extension opens it inside the project).
  document.addEventListener('click', (e) => {
    const a = e.target.closest('.act[data-path]');
    if (!a || a.classList.contains('list') || a.classList.contains('del')) return;
    window.parent.postMessage({ type: 'phoenix-open', path: a.dataset.path }, '*');
  });
  // The composer's height (it grows with the text) positions the chat's bottom gap and the approval bar.
  new ResizeObserver(() => document.documentElement.style.setProperty('--composer-h', (line.offsetHeight + 6) + 'px')).observe(line);
  // ---- Editor context: the extension reports the open file, cursor and selection; they go with each message (not shown
  // in the chat), so "fix this" works without naming files. A chip above the input shows it; click it to leave it out.
  let editor = null, useEditor = true;
  const chip = document.createElement('div');
  chip.id = 'pbFile';
  chip.title = 'Sent with your message: the open file and your selection (click to leave it out)';
  line.insertBefore(chip, line.firstChild);
  chip.onclick = () => { useEditor = !useEditor; drawChip(); input.focus(); };
  function drawChip() {
    chip.style.display = editor ? '' : 'none';
    if (!editor) return;
    const sel = editor.selection ? editor.selection.split('\n').length : 0;
    chip.textContent = editor.file + (sel ? ' · ' + sel + ' selected line' + (sel > 1 ? 's' : '') : ':' + editor.line);
    chip.classList.toggle('off', !useEditor);
  }
  window.phxContext = () => {
    if (!editor || !useEditor) return '';
    let s = '\n\n[Open in the editor: ' + editor.file + ', cursor on line ' + editor.line + ']';
    if (editor.selection) s += '\nSelected (lines ' + editor.selStart + '-' + editor.selEnd + '):\n```\n' + editor.selection + '\n```';
    else if (editor.around) s += '\nAround the cursor (lines ' + editor.aroundStart + '-' + (editor.aroundStart + editor.around.split('\n').length - 1) + '):\n```\n' + editor.around + '\n```';
    return s;
  };

  // ---- After an edit: ask VS Code for errors in the files the model changed; if there are any, send them back so it
  // fixes them before it is done (at most 2 rounds per message of yours, and none after a fix that left the same errors).
  let autoRounds = 0, autoSending = false, lastErrors = '';
  input.addEventListener('keydown', (e) => { if (e.key === 'Enter' && !e.shiftKey && !autoSending) { autoRounds = 0; lastErrors = ''; } }, true);
  window.phxTurnDone = (edited) => {
    if (!edited || !edited.length || autoRounds >= 2) return;
    window.parent.postMessage({ type: 'phoenix-diag', paths: edited }, '*');
  };
  function onDiagnostics(errors, excerpts) {
    if (!errors || !errors.length || autoRounds >= 2 || input.value.trim()) return;
    // Same messages as last round (line numbers aside) = the fix did not help; stop instead of looping.
    const sig = errors.map((d) => d.path + ': ' + d.message).sort().join('\n');
    if (autoRounds > 0 && sig === lastErrors) return;
    lastErrors = sig;
    autoRounds++;
    const list = errors.slice(0, 15).map((d) => '- ' + d.path + ':' + d.line + ': ' + d.message).join('\n');
    const code = (excerpts || []).map((x) => '\nCurrent ' + x.path + ' lines ' + x.start + '-' + x.end + ':\n```\n' + x.text + '\n```').join('');
    input.value = 'VS Code reports ' + (errors.length === 1 ? 'an error' : errors.length + ' errors') + ' after your edit:\n' + list + code +
      '\nFix ' + (errors.length === 1 ? 'it' : 'them') + ' with <edit>, copying <find> from the current lines above' +
      (/redeclare|duplicate|already (been )?declared/i.test(sig) ? ' (a redeclared name usually means a block was pasted twice: remove the extra copy)' : '') + '.';
    autoSending = true;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    autoSending = false;
  }

  // ---- Undo: each successful write / edit / delete gets an Undo button (restores the .llm-backup copy).
  function addUndo(t) {
    if (t.dataset.undo || t.classList.contains('fail') || !['write', 'edit', 'delete'].includes(t.dataset.op)) return;
    t.dataset.undo = '1';
    const m = t.textContent.match(/(?:previous version in|moved to) (\S+?)\)\s*$/);
    const b = document.createElement('button');
    b.className = 'undo';
    b.textContent = 'Undo ' + (t.dataset.op === 'delete' ? 'delete of ' : t.dataset.op === 'write' && !m ? 'creating ' : 'changes to ') + t.dataset.path;
    b.onclick = async () => {
      b.disabled = true;
      try { const r = await api('/api/undo', { path: t.dataset.path, backup: m ? m[1] : null }); b.textContent = '✓ ' + r.msg.replace(/^undid: /, 'Undone: '); }
      catch (e) { b.textContent = 'Undo failed: ' + e.message; }
    };
    t.after(b);
  }
  new MutationObserver(() => document.querySelectorAll('.tool[data-op]').forEach(addUndo))
    .observe(document.getElementById('chat'), { childList: true, subtree: true });

  // ---- Messages from the extension (relayed by the webview page).
  window.addEventListener('message', (ev) => {
    if (typeof ev.origin !== 'string' || !ev.origin.startsWith('vscode-webview://')) return;
    const d = ev.data || {};
    if (d.type === 'phoenix-editor') { editor = d.editor; drawChip(); }
    else if (d.type === 'phoenix-diag-result') onDiagnostics(d.errors, d.excerpts);
  });
  drawChip();

  input.addEventListener('input', update);
  setInterval(update, 1000);   // model loads and mode changes made elsewhere (the client window, /commands)
  update();
})();
