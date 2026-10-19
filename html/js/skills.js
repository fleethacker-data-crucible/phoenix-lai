// Skills: the sidebar list and the Skill Builder (chat.html). A skill is a folder with SKILL.md in the Agent Skills format:
// frontmatter (name, description) and Markdown instructions, plus optional supporting files. The server lists, checks,
// saves and matches them (src\Skills.cs, src\WebServer.Skills.cs); the loaded model helps write them (/api/skill-assist).
(function () {
  if (!$('skillList')) return;
  let skills = [], dirs = {}, cur = null, dirty = false, aiBusy = null, aiThread = [], checkTimer = 0, lastCheck = null;

  const SKILL_ICON = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3 4 7v10l8 4 8-4V7z"/><path d="m4 7 8 4 8-4M12 11v10"/></svg>';
  const SPARK = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3v4M12 17v4M3 12h4M17 12h4M6 6l2.5 2.5M15.5 15.5 18 18M6 18l2.5-2.5M15.5 8.5 18 6"/></svg>';

  // Starting points: each is a complete, valid skill the user edits.
  const TEMPLATES = [
    { id: 'blank', label: 'Blank', name: 'new-skill', desc: 'Describe what this skill does. Use when ...',
      body: '# New skill\n\n## Steps\n\n1. \n\n## Rules\n\n- \n' },
    { id: 'language', label: 'Language expert', name: 'language-expert', desc: 'Writes and fixes code in LANGUAGE using its own idioms and syntax. Use when the request mentions LANGUAGE or its file types.',
      body: '# LANGUAGE\n\nWrite idiomatic LANGUAGE, never another language\'s syntax.\n\n## Rules\n\n- \n\n## Example\n\nRequest: \n\n```\n\n```\n\n## Checklist\n\n- [ ] Only LANGUAGE syntax.\n' },
    { id: 'workflow', label: 'Workflow', name: 'my-workflow', desc: 'Step-by-step workflow for TASK. Use when asked to TASK.',
      body: '# TASK\n\n## When to use\n\n- \n\n## Steps\n\n1. Read the files involved first.\n2. \n3. Check the result and say what was not verified.\n\n## Output format\n\n' },
    { id: 'review', label: 'Review checklist', name: 'review-checklist', desc: 'Reviews WHAT against a fixed checklist and reports ranked findings. Use when asked to review or check WHAT.',
      body: '# Review: WHAT\n\n## Checklist\n\n- [ ] \n- [ ] \n\n## Output format\n\nFor each problem: `file:line`, what is wrong, when it fails, the fix. Most severe first. Say plainly when nothing is wrong.\n' },
    { id: 'conventions', label: 'Project conventions', name: 'project-conventions', desc: 'This project\'s coding conventions: naming, structure, libraries and patterns to follow. Use when writing or changing code in this project.',
      body: '# Project conventions\n\n## Naming\n\n- \n\n## Structure\n\n- \n\n## Never\n\n- \n' }
  ];
  const SECTIONS = [
    ['When to use', '## When to use\n\n- \n'], ['Steps', '## Steps\n\n1. \n2. \n'], ['Rules', '## Rules\n\n- \n'],
    ['Example', '## Example\n\nRequest: \n\n```\n\n```\n'], ['Output format', '## Output format\n\n'], ['Checklist', '## Checklist\n\n- [ ] \n']
  ];
  const ACTIONS = [
    { id: 'draft', label: 'Draft from my idea', ask: 'Describe the skill in a sentence or two, then press Enter.' },
    { id: 'improve', label: 'Improve', req: '' },
    { id: 'desc', label: 'Sharpen description', req: 'Rewrite only the description so it names every task, language, tool and key word that should load this skill, in the third person, ending with "Use when ...". Keep the body unchanged.' },
    { id: 'example', label: 'Add an example', req: 'Add a short ## Example section: one realistic request and the exact output the assistant should give. Keep everything else.' },
    { id: 'checklist', label: 'Add a checklist', req: 'Add a ## Checklist section with the 3 to 6 things to verify before answering. Keep everything else.' },
    { id: 'tighten', label: 'Tighten', req: 'Make it shorter and more direct: imperative instructions only, cut anything a capable model already knows. Keep every rule that is specific to this skill.' }
  ];

  // ---------- SKILL.md text <-> fields ----------
  function parse(text) {
    text = String(text || '').replace(/\r\n/g, '\n').replace(/^﻿/, '');
    const out = { name: '', description: '', body: text, extra: [] };
    const m = /^---\n([\s\S]*?)\n---[ \t]*(\n|$)/.exec(text);
    if (!m) return out;
    out.body = text.slice(m[0].length).replace(/^\n+/, '');
    const lines = m[1].split('\n');
    for (let i = 0; i < lines.length; i++) {
      const kv = /^([A-Za-z][\w-]*)\s*:\s*(.*)$/.exec(lines[i]);
      if (!kv) continue;
      let v = kv[2].trim(); const key = kv[1].toLowerCase(), start = i;
      if (/^[>|]-?$/.test(v)) {
        const parts = [];
        while (i + 1 < lines.length && /^(\s|$)/.test(lines[i + 1])) parts.push(lines[++i].trim());
        v = parts.join(v[0] === '>' ? ' ' : '\n').trim();
      } else if (/^(["']).*\1$/.test(v) && v.length >= 2) v = v.slice(1, -1).replace(/\\"/g, '"').replace(/''/g, "'");
      else while (i + 1 < lines.length && /^\s{2,}\S/.test(lines[i + 1]) && !/^\s+[\w-]+\s*:/.test(lines[i + 1])) v += ' ' + lines[++i].trim();
      if (key === 'name') out.name = v; else if (key === 'description') out.description = v;
      else out.extra.push(lines.slice(start, i + 1).join('\n'));   // other keys (license, metadata ...) are kept as written
    }
    return out;
  }
  function yamlValue(v) {
    v = v.replace(/\s*\n\s*/g, ' ').trim();
    return /^[\s"'&*!|>%@`#,\[\]{}-]|: | #|:$/.test(v) ? '"' + v.replace(/\\/g, '\\\\').replace(/"/g, '\\"') + '"' : v;
  }
  function compose() {
    const name = $('skName').value.trim(), desc = $('skDesc').value.trim(), body = $('skBody').value.replace(/\s+$/, '');
    return '---\nname: ' + name + '\ndescription: ' + yamlValue(desc) + '\n' + (cur && cur.extra.length ? cur.extra.join('\n') + '\n' : '') + '---\n\n' + body + '\n';
  }

  // ---------- sidebar ----------
  async function loadSkillList() {
    try { const r = await api('/api/skills'); skills = r.skills; dirs = r; } catch (e) { return; }
    drawSidebar();
    if ($('skillModal').classList.contains('show')) drawList();
  }
  function status(s) { return !s.valid ? 'bad' : s.warnings.length ? 'warn' : 'ok'; }
  function drawSidebar() {
    $('skillList').innerHTML = skills.length ? skills.map(s => '<div class="sitem" data-n="' + esc(s.name) + '" data-s="' + esc(s.scope) + '" title="' + esc(s.description) + '">' +
      SKILL_ICON + '<span>' + esc(s.name) + '</span><i class="dot ' + status(s) + '"></i></div>').join('') : '<div class="sempty">No skills yet</div>';
  }
  $('skillList').addEventListener('click', e => { const it = e.target.closest('.sitem'); if (it) openBuilder(it.dataset.n, it.dataset.s); });
  $('newSkill').onclick = () => openBuilder(null);
  $('navSkills').onclick = () => openBuilder(skills.length ? undefined : null);
  // "skill: name" lines above replies open that skill
  $('chat').addEventListener('click', e => { const a = e.target.closest('.skillsused a[data-skill]'); if (a) openBuilder(a.dataset.skill, a.dataset.scope); });

  // ---------- the builder window ----------
  const modal = document.createElement('div');
  modal.className = 'modal'; modal.id = 'skillModal';
  modal.innerHTML =
    '<div class="window settings skillwin">' +
      '<header><h2><span class="chev">›</span> SKILL BUILDER <span class="skpath" id="skPath"></span></h2><button class="x" id="skClose" title="Close (Esc)">✕</button></header>' +
      '<div class="skgrid">' +
        '<aside class="sklist">' +
          '<input id="skFilter" placeholder="Filter skills" spellcheck="false">' +
          '<div id="skItems"></div>' +
          '<div class="section">new from template</div>' +
          '<div class="sktemplates" id="skTemplates"></div>' +
        '</aside>' +
        '<section class="skedit">' +
          '<div class="skrow">' +
            '<label class="field grow">name<input id="skName" spellcheck="false" maxlength="64" placeholder="lowercase-with-hyphens"><span class="hint" id="skNameHint"></span></label>' +
            '<div class="field">saved in<div class="radios" id="skScope"></div></div>' +
          '</div>' +
          '<label class="field">description <span class="count" id="skDescCount"></span><textarea id="skDesc" rows="3" spellcheck="true" placeholder="What the skill does, then: Use when ... (the words a matching request contains)."></textarea>' +
            '<span class="hint">the only part read when deciding whether a request needs this skill · third person · say when to use it</span></label>' +
          '<div class="sktabs" id="skTabs"><button data-t="body" class="on">instructions</button><button data-t="files">files <b id="skFileCount"></b></button><button data-t="preview">preview</button><button data-t="raw">SKILL.md</button></div>' +
          '<div class="skpane on" data-p="body">' +
            '<div class="skchips" id="skSections"></div>' +
            '<textarea id="skBody" spellcheck="false" placeholder="# Title&#10;&#10;Instructions in Markdown: steps, rules, an example, a checklist."></textarea>' +
            '<div class="hint" id="skBodyInfo"></div>' +
          '</div>' +
          '<div class="skpane" data-p="files"><div class="skfiles" id="skFiles"></div>' +
            '<div class="hint">supporting files sit next to SKILL.md (reference.md, examples.md ...). Name one in the instructions and the model gets it with the skill.</div></div>' +
          '<div class="skpane reply" data-p="preview" id="skPreview"></div>' +
          '<div class="skpane" data-p="raw"><textarea id="skRaw" spellcheck="false"></textarea><div class="hint">the file as saved · edits here update the fields</div></div>' +
        '</section>' +
        '<aside class="skai">' +
          '<div class="section">' + SPARK + ' assistant <span class="skmodel" id="skModel"></span></div>' +
          '<div class="skchips" id="skActions"></div>' +
          '<div class="skthread" id="skThread"></div>' +
          '<div class="skask"><textarea id="skAsk" rows="2" spellcheck="true" placeholder="Ask the model to write or change this skill…"></textarea><button id="skAskGo" class="primary">send</button></div>' +
          '<div class="section">trigger test</div>' +
          '<div class="sktest"><input id="skTest" spellcheck="false" placeholder="A message a user might send…"><div id="skTestOut" class="hint"></div></div>' +
        '</aside>' +
      '</div>' +
      '<footer><div class="skchecks" id="skChecks"></div><button id="skDelete" class="danger">delete</button><button id="skSave" class="primary">save</button></footer>' +
    '</div>';
  document.body.appendChild(modal);

  $('skTemplates').innerHTML = TEMPLATES.map(t => '<button data-t="' + t.id + '">' + esc(t.label) + '</button>').join('');
  $('skSections').innerHTML = SECTIONS.map((s, i) => '<button data-i="' + i + '">+ ' + esc(s[0]) + '</button>').join('');
  $('skActions').innerHTML = ACTIONS.map(a => '<button data-a="' + a.id + '">' + esc(a.label) + '</button>').join('');
  radios('skScope', [{ value: 'app', label: 'app' }, { value: 'project', label: 'project' }], 'app', () => { markDirty(); drawPath(); });

  function drawList() {
    const f = $('skFilter').value.trim().toLowerCase();
    const groups = [['app', 'App skills'], ['project', dirs.project ? 'Project: ' + dirs.project : 'Project (none open)']];
    $('skItems').innerHTML = groups.map(([sc, title]) => {
      const items = skills.filter(s => s.scope === sc && (!f || s.name.includes(f) || s.description.toLowerCase().includes(f)));
      return '<div class="skgroup">' + esc(title) + '</div>' + (items.length ? items.map(s => '<button class="skitem' + (cur && cur.orig === s.name && cur.origScope === s.scope ? ' on' : '') +
        '" data-n="' + esc(s.name) + '" data-s="' + sc + '"><i class="dot ' + status(s) + '"></i><span>' + esc(s.name) + '</span><small>' + s.lines + ' lines</small></button>').join('')
        : '<div class="sempty">' + (sc === 'project' && !dirs.project ? 'open a project to keep skills with it' : 'none') + '</div>');
    }).join('');
  }
  function drawPath() {
    const sc = radioValue('skScope') || 'app', name = $('skName').value.trim() || '<name>';
    const base = sc === 'project' ? dirs.projectDir : dirs.appDir;
    $('skPath').textContent = base ? base + '\\' + name + '\\SKILL.md' : 'no project open';
  }
  function markDirty() { dirty = true; scheduleCheck(); }

  async function openBuilder(name, scope) {
    if (!dirs.appDir) await loadSkillList();
    $('skModel').textContent = info.file ? '· ' + String(info.file).replace(/\.gguf$/i, '') : '· no model loaded';
    modal.classList.add('show');
    if (name === undefined) { if (!cur) { if (skills.length) select(skills[0].name, skills[0].scope); else newSkill('blank'); } }
    else if (name === null) newSkill('blank');
    else select(name, scope || (skills.find(s => s.name === name) || {}).scope || 'app');
    drawList();
  }
  // undefined: the skill that was open (or the first one) · null: a new skill · a name: that skill
  window.openSkillBuilder = name => openBuilder(name);
  window.loadSkillList = loadSkillList;
  window.skillNames = () => skills.filter(s => s.valid).map(s => s.name);

  function confirmLeave() { return !dirty || confirm('Discard the unsaved changes to this skill?'); }
  async function select(name, scope) {
    if (!confirmLeave()) return;
    let r;
    try { r = await api('/api/skill-get?scope=' + encodeURIComponent(scope) + '&name=' + encodeURIComponent(name)); }
    catch (e) { note(e.message); return; }
    load(r.text, r.files, name, scope);
  }
  function newSkill(tid) {
    if (!confirmLeave()) return;
    const t = TEMPLATES.find(x => x.id === tid) || TEMPLATES[0];
    let n = t.name, i = 2; while (skills.some(s => s.name === n)) n = t.name + '-' + i++;
    load('---\nname: ' + n + '\ndescription: ' + yamlValue(t.desc) + '\n---\n\n' + t.body, [], null, dirs.project ? 'project' : 'app');
    dirty = true; $('skName').focus(); $('skName').select();
  }
  function load(text, files, orig, scope) {
    const p = parse(text);
    cur = { orig, origScope: scope, extra: p.extra, files: files.map(f => ({ name: f.name, text: f.text })), removed: [] };
    $('skName').value = p.name; $('skDesc').value = p.description; $('skBody').value = p.body;
    radios('skScope', [{ value: 'app', label: 'app' }, { value: 'project', label: 'project' + (dirs.project ? '' : ' (none open)') }], scope, () => { markDirty(); drawPath(); });
    $('skDelete').style.visibility = orig ? 'visible' : 'hidden';
    aiThread = []; $('skThread').innerHTML = '<div class="hint">The loaded model can draft a whole skill from one sentence, or improve this one. Its drafts appear here; Apply puts one in the editor.</div>';
    $('skTestOut').textContent = '';
    dirty = false;
    tab('body'); drawFiles(); drawList(); drawPath(); scheduleCheck(0);
  }

  // ---------- tabs, sections, files ----------
  function tab(t) {
    if (t === 'raw') $('skRaw').value = compose();
    if (t === 'preview') $('skPreview').innerHTML = '<div class="skprevhead"><b>' + esc($('skName').value) + '</b><p>' + esc($('skDesc').value) + '</p></div>' + render($('skBody').value, false);
    $('skTabs').querySelectorAll('button').forEach(b => b.classList.toggle('on', b.dataset.t === t));
    modal.querySelectorAll('.skpane').forEach(p => p.classList.toggle('on', p.dataset.p === t));
  }
  $('skTabs').onclick = e => { const b = e.target.closest('button'); if (b) tab(b.dataset.t); };
  $('skRaw').addEventListener('input', () => {
    const p = parse($('skRaw').value);
    $('skName').value = p.name; $('skDesc').value = p.description; $('skBody').value = p.body; cur.extra = p.extra; markDirty(); drawPath();
  });
  $('skSections').onclick = e => {
    const b = e.target.closest('button'); if (!b) return;
    const ta = $('skBody'), s = SECTIONS[+b.dataset.i][1], v = ta.value;
    ta.value = v.replace(/\s*$/, '') + (v.trim() ? '\n\n' : '') + s; ta.focus(); ta.selectionStart = ta.selectionEnd = ta.value.length - 1;
    markDirty();
  };
  function drawFiles() {
    $('skFileCount').textContent = cur.files.length || '';
    $('skFiles').innerHTML = cur.files.map((f, i) => '<div class="skfile"><div class="skfilehead"><input data-i="' + i + '" class="fname" value="' + esc(f.name) + '" spellcheck="false">' +
      '<button data-rm="' + i + '" title="Remove this file">remove</button></div><textarea data-i="' + i + '" spellcheck="false">' + esc(f.text) + '</textarea></div>').join('') +
      '<button id="skAddFile">+ add a file</button>';
  }
  $('skFiles').addEventListener('input', e => {
    const i = +e.target.dataset.i; if (isNaN(i)) return;
    if (e.target.classList.contains('fname')) cur.files[i].name = e.target.value.trim(); else cur.files[i].text = e.target.value;
    markDirty();
  });
  $('skFiles').addEventListener('click', e => {
    if (e.target.id === 'skAddFile') { let n = 'reference.md', k = 2; while (cur.files.some(f => f.name === n)) n = 'reference-' + k++ + '.md'; cur.files.push({ name: n, text: '# Reference\n\n' }); drawFiles(); markDirty(); return; }
    const rm = e.target.dataset.rm; if (rm === undefined) return;
    const f = cur.files.splice(+rm, 1)[0]; if (f && cur.orig) cur.removed.push(f.name);
    drawFiles(); markDirty();
  });

  // ---------- checks (server-side, the same rules the server applies when it loads and matches) ----------
  function scheduleCheck(ms) { clearTimeout(checkTimer); checkTimer = setTimeout(runCheck, ms === undefined ? 350 : ms); }
  async function runCheck() {
    const msg = $('skTest').value.trim();
    let r; try { r = await api('/api/skill-check', { text: compose(), message: msg }); } catch (e) { return; }
    lastCheck = r;
    const s = r.skill, name = $('skName').value.trim();
    const taken = skills.some(x => x.name === name && !(cur.orig === name && cur.origScope === x.scope) && x.scope === (radioValue('skScope') || 'app'));
    const items = s.errors.map(t => ['bad', t]).concat(s.warnings.map(t => ['warn', t]));
    if (taken) items.unshift(['bad', 'another ' + (radioValue('skScope') || 'app') + ' skill is already called ' + name + '.']);
    if (!items.length) items.push(['ok', 'follows the skill format']);
    $('skChecks').innerHTML = items.map(([k, t]) => '<span class="' + k + '">' + (k === 'ok' ? '✓ ' : k === 'warn' ? '⚠ ' : '✗ ') + esc(t) + '</span>').join('');
    $('skSave').disabled = !s.valid || taken;
    $('skNameHint').textContent = !name ? '' : /^[a-z0-9]+(-[a-z0-9]+)*$/.test(name) ? 'call it in chat with /' + name : 'lowercase letters, digits and single hyphens';
    const d = $('skDesc').value.trim().length; $('skDescCount').textContent = d + ' / 1024'; $('skDescCount').classList.toggle('over', d > 1024);
    const lines = $('skBody').value.split('\n').length; $('skBodyInfo').textContent = lines + ' lines' + (lines > 500 ? ' · over 500: move detail into supporting files' : '');
    if (r.test) {
      const t = r.test;
      $('skTestOut').innerHTML = t.loads ? '<span class="ok">✓ loads for this message</span> · ' + (t.terms[0] && t.terms[0][0] === '/' ? 'called by name' : 'matched ' + esc(t.terms.join(', ')) + ' (score ' + t.score + ')')
        : t.matches ? '<span class="warn">⚠ matches, but ' + esc(t.winners.join(' and ')) + ' score higher</span>'
        : '<span class="bad">✗ would not load</span> · ' + (t.terms.length ? 'only ' + esc(t.terms.join(', ')) + ' matched' : 'no words in common with the name or description') + '. Add the words this message uses to the description, or call it with /' + esc(name);
    } else $('skTestOut').textContent = 'Type a message to see whether it would load this skill.';
  }
  ['skName', 'skDesc', 'skBody'].forEach(id => $(id).addEventListener('input', () => { markDirty(); if (id === 'skName') drawPath(); }));
  $('skTest').addEventListener('input', () => scheduleCheck(250));
  $('skFilter').addEventListener('input', drawList);
  $('skItems').onclick = e => { const b = e.target.closest('.skitem'); if (b) select(b.dataset.n, b.dataset.s); };
  $('skTemplates').onclick = e => { const b = e.target.closest('button'); if (b) newSkill(b.dataset.t); };

  function note(t, bad) {
    $('skChecks').innerHTML = '<span class="' + (bad === false ? 'ok' : 'bad') + '">' + esc(t) + '</span>';
  }
  $('skSave').onclick = async () => {
    const scope = radioValue('skScope') || 'app';
    const body = { scope, oldName: cur.orig && cur.origScope === scope ? cur.orig : '', text: compose(), files: cur.files, removed: cur.removed };
    try { const r = await api('/api/skill-save', body); skills = r.skills; dirs = r; }
    catch (e) { note(e.message); return; }
    const name = $('skName').value.trim();
    cur.orig = name; cur.origScope = scope; cur.removed = []; dirty = false;
    $('skDelete').style.visibility = 'visible';
    drawSidebar(); drawList(); scheduleCheck(0);
    setTimeout(() => note('✓ saved · call it in chat with /' + name + ', or just ask: it loads when a message matches', false), 380);
  };
  $('skDelete').onclick = async () => {
    if (!cur.orig || !confirm('Delete the skill "' + cur.orig + '"?\n\nIts folder is moved to %LOCALAPPDATA%\\LocalLLMStudio\\skill-trash, not erased.')) return;
    try { const r = await api('/api/skill-delete', { scope: cur.origScope, name: cur.orig }); skills = r.skills; dirs = r; } catch (e) { note(e.message); return; }
    dirty = false; drawSidebar();
    if (skills.length) select(skills[0].name, skills[0].scope); else newSkill('blank');
  };
  function close() {
    if (aiBusy) aiBusy.abort();
    if (!confirmLeave()) return;
    dirty = false; modal.classList.remove('show'); if (promptLine.isConnected) input.focus();
  }
  $('skClose').onclick = close;
  modal.addEventListener('keydown', e => {
    if (e.key === 'Escape') { e.stopPropagation(); close(); }
    else if (e.key === 's' && e.ctrlKey) { e.preventDefault(); if (!$('skSave').disabled) $('skSave').click(); }
  });

  // ---------- the assistant (the loaded model writes or changes the skill) ----------
  // Its skill drafts come between ~~~skill and ~~~ lines (tildes, so code blocks inside the skill survive).
  function extract(text) {
    const m = /~~~\s*skill[^\n]*\n([\s\S]*?)\n~~~[ \t]*(\n|$)/.exec(text) || /~~~\s*skill[^\n]*\n([\s\S]*)$/.exec(text);
    if (m) return { skill: repair(m[1]), rest: (text.slice(0, m.index) + text.slice(m.index + m[0].length)).trim() };
    const f = /^(---\s*\n)?name:[\s\S]*$/m.exec(text);   // a model that ignored the markers: from the frontmatter on
    return f ? { skill: repair(f[0].replace(/\n```\s*$/, '')), rest: text.slice(0, f.index).replace(/```\w*\s*$/, '').trim() } : { skill: null, rest: text };
  }
  // Small models often drop the opening --- of the frontmatter (or wrap the file in a code fence): put it back.
  function repair(s) {
    s = s.trim().replace(/^```\w*\n/, '').replace(/\n```$/, '');
    if (/^name\s*:/.test(s) && /\n---[ \t]*(\n|$)/.test(s)) s = '---\n' + s;
    return s + '\n';
  }
  function bubble(role, html) {
    const d = document.createElement('div'); d.className = 'skmsg ' + role; d.innerHTML = html;
    const th = $('skThread'); const h = th.querySelector(':scope > .hint'); if (h) h.remove();
    th.appendChild(d); th.scrollTop = th.scrollHeight; return d;
  }
  function draftCard(skill) {
    const p = parse(skill), lines = skill.split('\n').length;
    return '<div class="skdraft"><div><b>' + esc(p.name || 'skill') + '</b> · ' + lines + ' lines</div><p>' + esc(p.description) + '</p>' +
      '<button class="primary sm" data-apply>apply to editor</button> <button class="sm" data-view>view</button><pre class="skdraftbody">' + esc(skill) + '</pre></div>';
  }
  $('skThread').addEventListener('click', e => {
    const d = e.target.closest('.skmsg'); if (!d) return;
    if (e.target.dataset.view !== undefined) { d.querySelector('.skdraftbody').classList.toggle('show'); return; }
    if (e.target.dataset.apply === undefined) return;
    const p = parse(d.dataset.skill);
    $('skName').value = p.name || $('skName').value; $('skDesc').value = p.description; $('skBody').value = p.body; cur.extra = p.extra;
    markDirty(); drawPath(); tab('body');
    e.target.textContent = 'applied ✓'; e.target.disabled = true;
  });

  async function assist(mode, request, shown) {
    if (aiBusy) return;
    if (!info.file) { bubble('err', 'Load a model first (top bar), then ask again.'); return; }
    bubble('user', esc(shown || request));
    const out = bubble('ai', '<span class="caret typing"></span>');
    const ctrl = new AbortController(); aiBusy = ctrl;
    $('skAskGo').textContent = 'stop';
    let text = '', ref = null, t0 = performance.now();
    try {
      const res = await fetch('/api/skill-assist', { method: 'POST', signal: ctrl.signal, headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ mode, request, draft: compose(), history: aiThread.slice(-6) }) });
      const reader = res.body.getReader(), dec = new TextDecoder(); let buf = '';
      for (;;) {
        const { value, done } = await reader.read(); if (done) break;
        buf += dec.decode(value, { stream: true }); let i;
        while ((i = buf.indexOf('\n\n')) >= 0) {
          const line = buf.slice(0, i); buf = buf.slice(i + 2);
          if (!line.startsWith('data: ')) continue;
          const ev = JSON.parse(line.slice(6));
          if (ev.t !== undefined) {
            text += ev.t;
            const x = extract(text);
            out.innerHTML = (x.rest ? render(x.rest, true) : '') + (x.skill ? '<div class="skdraft writing"><b>writing SKILL.md…</b> ' + x.skill.split('\n').length + ' lines</div>' : '') + '<span class="caret typing"></span>';
            $('skThread').scrollTop = $('skThread').scrollHeight;
          } else if (ev.rag) ref = [...new Set(ev.rag.map(h => h.file))];
          else if (ev.error) throw new Error(ev.error);
          else if (ev.done) out.dataset.stats = ev.tokens + ' tokens · ' + ev.tps + ' t/s';
        }
      }
    } catch (e) { if (e.name !== 'AbortError') text += (text ? '\n\n' : '') + '⚠ ' + e.message; }
    aiBusy = null; $('skAskGo').textContent = 'send';
    const x = extract(text);
    out.dataset.skill = x.skill || '';
    out.innerHTML = (x.rest ? render(x.rest, false) : '') + (x.skill ? draftCard(x.skill) : '') +
      '<div class="meta">' + (ref ? 'grounded in ' + esc(ref.join(', ')) + ' · ' : '') + (out.dataset.stats || ((performance.now() - t0) / 1000).toFixed(1) + ' s') + '</div>';
    aiThread.push({ role: 'user', content: request }, { role: 'assistant', content: text });
    $('skThread').scrollTop = $('skThread').scrollHeight;
  }
  let pendingDraft = false;
  $('skActions').onclick = e => {
    const b = e.target.closest('button'); if (!b) return;
    const a = ACTIONS.find(x => x.id === b.dataset.a);
    if (a.id === 'draft') { pendingDraft = true; $('skAsk').placeholder = a.ask; $('skAsk').focus(); return; }
    assist('improve', a.req, a.label);
  };
  function askGo() {
    if (aiBusy) { aiBusy.abort(); return; }
    const t = $('skAsk').value.trim(); if (!t) return;
    $('skAsk').value = '';
    const blank = !$('skBody').value.trim() || /^# New skill/.test($('skBody').value);
    const mode = pendingDraft || blank ? 'draft' : aiThread.length ? 'chat' : 'improve';
    pendingDraft = false; $('skAsk').placeholder = 'Ask the model to write or change this skill…';
    assist(mode, t);
  }
  $('skAskGo').onclick = askGo;
  $('skAsk').addEventListener('keydown', e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); askGo(); } });

  loadSkillList();
})();
