// Reply rendering for the chat page: code fences, markdown and tool tags. No dependencies, no network.
function esc(s) { return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

const COPY_ICON = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="11" height="11" rx="2"/><path d="M5 15V6a2 2 0 0 1 2-2h9"/></svg>';
const CHECK_ICON = '<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>';

// Reply rendering: code fences become boxes with a language badge, VS Code colors and a copy icon; <write>/<read>/<list> tags are highlighted.
function render(text, streaming) {
  // VS Code page: edits happen behind the scenes. A write shows as one activity line ("Editing x…" while it streams,
  // then "Edited x"), click to open the file in VS Code; read / list / delete tags become activity lines too.
  if (window.PHX_VSCODE) {
    const act = (cls, verb, path, extra) => '\n\n<div class="act ' + cls + '" data-path="' + esc(path) + '"><span class="ic"></span>' + verb +
      ' <b>' + esc(path) + '</b>' + (extra ? '<span class="x">' + extra + '</span>' : '') + '</div>\n\n';
    text = text.replace(/<write\s+path\s*=\s*"([^"]+)"\s*>\r?\n?([\s\S]*?)(<\/write>|$)/g, (m, p, c, end) => {
      const lines = c.replace(/^\s*```[^\n]*\n/, '').replace(/\n?```\s*$/, '').split('\n').length;
      return end ? act('edit done', 'Edited', p, lines + ' lines') : act('edit busy', 'Editing', p, lines + ' lines…');
    });
    text = text.replace(/<edit\s+path\s*=\s*"([^"]+)"\s*>([\s\S]*?)(<\/edit>|$)/g, (m, p, body, end) => {
      let add = 0, del = 0;
      body.replace(/<find>\r?\n?([\s\S]*?)(<\/find>|$)/g, (x, f) => { del += f.replace(/\n$/, '').split('\n').length; return x; });
      body.replace(/<replace>\r?\n?([\s\S]*?)(<\/replace>|$)/g, (x, r) => { const t = r.replace(/\n$/, ''); add += t ? t.split('\n').length : 0; return x; });
      return end ? act('edit done', 'Edited', p, '+' + add + ' −' + del) : act('edit busy', 'Editing', p, '');
    });
    text = text.replace(/<delete\s+path\s*=\s*"([^"]+)"\s*\/?>/g, (m, p) => act('del', 'Deleted', p));
    text = text.replace(/<read\s+path\s*=\s*"([^"]+)"(?:\s+lines\s*=\s*"([^"]*)")?\s*\/?>/g, (m, p, l) => act('read', 'Read', p, l ? 'lines ' + l : ''));
    text = text.replace(/<search\s+query\s*=\s*"([^"]+)"(?:\s+path\s*=\s*"([^"]*)")?\s*\/?>/g, (m, q, p) => act('list', 'Searched', q, p && p !== '.' ? 'in ' + p : ''));
    text = text.replace(/<list(?:\s+path\s*=\s*"([^"]*)")?\s*\/?>/g, (m, p) => act('list', 'Listed', p && p !== '.' ? p : 'project'));
    // keep the activity lines out of markdown escaping
    const acts = [];
    text = text.replace(/\n\n(<div class="act[\s\S]*?<\/div>)\n\n/g, (m, h) => { acts.push(h); return '\n\n\u0000' + (acts.length - 1) + '\u0000\n\n'; });
    return renderFences(text, streaming).replace(/(?:<p>)?\u0000(\d+)\u0000(?:<\/p>)?/g, (m, i) => acts[+i]);
  }
  // Edits become a diff box labelled with the path: removed lines "- ", added lines "+ ".
  text = text.replace(/<edit\s+path\s*=\s*"([^"]+)"\s*>([\s\S]*?)(<\/edit>|$)/g, (m, p, body) => {
    const out = [];
    body.replace(/<find>\r?\n?([\s\S]*?)(?:<\/find>|$)\s*(?:<replace>\r?\n?([\s\S]*?)(?:<\/replace>|$))?/g, (x, f, r) => {
      f.replace(/\n$/, '').split('\n').forEach(l => out.push('- ' + l));
      const t = r === undefined ? '' : r.replace(/\n$/, '');
      if (t) t.split('\n').forEach(l => out.push('+ ' + l));
      return x;
    });
    return '```✎ ' + p + ' (edit)\n' + out.join('\n') + '\n```';
  });
  // File writes become code boxes labelled with the path (inner code fences dropped).
  text = text.replace(/<write\s+path\s*=\s*"([^"]+)"\s*>\r?\n?([\s\S]*?)(<\/write>|$)/g, (m, p, c) =>
    '```✎ ' + p + '\n' + c.replace(/^\s*```[^\n]*\n/, '').replace(/\n?```\s*$/, '') + '\n```');
  return renderFences(text, streaming);
}

// Code fences become code boxes; everything else is markdown.
function renderFences(text, streaming) {
  const parts = text.split('```');
  let html = '';
  parts.forEach((part, i) => {
    if (i % 2 === 1) {
      const nl = part.indexOf('\n');
      const tag = nl >= 0 ? part.slice(0, nl).trim() : '';
      const code = (nl >= 0 ? part.slice(nl + 1) : part).replace(/\n$/, '');
      const done = i < parts.length - 1 || !streaming;
      // "✎ path" marks a file the model wrote: the path goes right, the language comes from its extension.
      const file = tag.startsWith('✎') ? tag.slice(1).trim() : '';
      const lang = detectLang(file ? '' : tag, code, file);
      const info = LANGS[lang];
      const badge = info ? '<span class="badge" style="background:' + info.color + ';color:' + badgeText(info.color) + '">' + esc(info.name) + '</span>'
                         : '<span class="badge plain">' + esc(tag && !file ? tag : 'code') + '</span>';
      const box = '<div class="code"><header>' + badge + '<span class="grow"></span>' +
              (file ? '<span class="file">' + esc(file) + '</span>' : '') +
              (done ? '<button class="copy" title="Copy">' + COPY_ICON + '</button>' : '') +
              '</header><pre>' + highlight(code, lang) + '</pre></div>';
      // VS Code page: code stays out of the chat, folded into a one-line chip (click to look); a short shell command
      // (what the model gives you to run, since it never runs anything itself) stays open with its copy button.
      const command = /^(powershell|pwsh|ps1?|shell|sh|bash|cmd|bat|console|terminal)$/i.test(tag) && code.split('\n').length <= 4;
      html += window.PHX_VSCODE && !command
        ? '<details class="fold"><summary>' + esc(info ? info.name : 'code') + ' · ' + code.split('\n').length + ' lines</summary>' + box + '</details>'
        : box;
    } else {
      html += markdown(part);
    }
  });
  return html;
}

// Markdown for everything outside code fences: headings, paragraphs, bold/italic/strike, inline code, lists (nested,
// ordered, task), quotes, rules, tables. Links show as text only (nothing is ever loaded from a URL).
function inlineMd(t) {
  const codes = [];
  t = t.replace(/`([^`\n]+)`/g, (m, c) => { codes.push('<code class="inline">' + esc(c) + '</code>'); return '\u0002' + (codes.length - 1) + '\u0003'; });
  t = esc(t)
    .replace(/&lt;\/?(read|list|search|delete)\b[\s\S]*?&gt;/g, m => '<span class="tag">' + m + '</span>')
    .replace(/!?\[([^\]]+)\]\(([^)\s]+)[^)]*\)/g, (m, txt, url) => '<span class="link" title="' + url + '">' + txt + '</span>')
    .replace(/\*\*\*(?=\S)([\s\S]*?\S)\*\*\*/g, '<strong><em>$1</em></strong>')
    .replace(/\*\*(?=\S)([\s\S]*?\S)\*\*/g, '<strong>$1</strong>')
    .replace(/__(?=\S)([\s\S]*?\S)__/g, '<strong>$1</strong>')
    .replace(/(^|[^*\w])\*(?=[^\s*])([^*\n]*?[^\s*])\*(?!\w)/g, '$1<em>$2</em>')
    .replace(/(^|[^\w])_(?=\S)([^_\n]*?\S)_(?!\w)/g, '$1<em>$2</em>')
    .replace(/~~(?=\S)([\s\S]*?\S)~~/g, '<del>$1</del>');
  return t.replace(/\u0002(\d+)\u0003/g, (m, i) => codes[+i]);
}
const LIST_RE = /^(\s*)([-*+]|\d+[.)])\s+(.*)$/;
function markdown(src) {
  const lines = src.replace(/\r/g, '').split('\n');
  let html = '', para = [], i = 0;
  const flush = () => { if (para.length) { html += '<p>' + para.map(inlineMd).join('<br>') + '</p>'; para = []; } };
  while (i < lines.length) {
    const line = lines[i], t = line.trim();
    let m;
    if (!t) { flush(); i++; continue; }
    if ((m = t.match(/^(#{1,6})\s+(.*?)\s*#*$/))) { flush(); html += '<h' + m[1].length + '>' + inlineMd(m[2]) + '</h' + m[1].length + '>'; i++; continue; }
    if (/^([-*_])(\s*\1){2,}$/.test(t)) { flush(); html += '<hr>'; i++; continue; }
    if (t.startsWith('>')) {
      flush();
      const q = [];
      while (i < lines.length && lines[i].trim().startsWith('>')) q.push(lines[i++].trim().replace(/^>\s?/, ''));
      html += '<blockquote>' + markdown(q.join('\n')) + '</blockquote>';
      continue;
    }
    // Table: a header row followed by a |---|---| separator.
    if (t.includes('|') && i + 1 < lines.length && /^\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?$/.test(lines[i + 1].trim())) {
      flush();
      const cells = r => r.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(c => c.trim());
      const head = cells(line), aligns = cells(lines[i + 1]).map(c => c.endsWith(':') ? (c.startsWith(':') ? 'center' : 'right') : '');
      const td = (tag, c, k) => '<' + tag + (aligns[k] ? ' style="text-align:' + aligns[k] + '"' : '') + '>' + inlineMd(c) + '</' + tag + '>';
      i += 2;
      let rows = '';
      while (i < lines.length && lines[i].trim() && lines[i].includes('|')) rows += '<tr>' + cells(lines[i++]).map((c, k) => td('td', c, k)).join('') + '</tr>';
      html += '<div class="tablewrap"><table><thead><tr>' + head.map((c, k) => td('th', c, k)).join('') + '</tr></thead><tbody>' + rows + '</tbody></table></div>';
      continue;
    }
    if (LIST_RE.test(line)) {
      flush();
      // The list block: items, their continuation lines and nested items (a blank line only if the list goes on).
      const block = [];
      while (i < lines.length && (LIST_RE.test(lines[i]) || (lines[i].trim() && /^\s+/.test(lines[i])) ||
             (!lines[i].trim() && i + 1 < lines.length && (LIST_RE.test(lines[i + 1]) || /^\s+\S/.test(lines[i + 1]))))) block.push(lines[i++]);
      html += listMd(block);
      continue;
    }
    para.push(t);
    i++;
  }
  flush();
  return html;
}
function listMd(block) {
  const indentOf = l => l.match(/^\s*/)[0].replace(/\t/g, '    ').length;
  const base = indentOf(block.find(l => LIST_RE.test(l)));
  const items = [];
  let ordered = null, start = 1;
  for (const l of block) {
    const m = l.match(LIST_RE);
    if (m && indentOf(l) <= base + 1) {
      if (ordered === null) { ordered = /\d/.test(m[2]); if (ordered) start = parseInt(m[2], 10); }
      items.push({ text: [m[3]], sub: [] });
    } else if (items.length) {
      const it = items[items.length - 1];
      if (LIST_RE.test(l) || it.sub.length) it.sub.push(l); else if (l.trim()) it.text.push(l.trim());
    }
  }
  const tag = ordered ? 'ol' : 'ul';
  return '<' + tag + (ordered && start !== 1 ? ' start="' + start + '"' : '') + '>' + items.map(it => {
    const body = it.text.map(inlineMd).join('<br>').replace(/^\[([ xX])\]\s+/, (m, c) => (c === ' ' ? '☐ ' : '☑ '));
    const sub = it.sub.filter(l => l.trim());
    return '<li>' + body + (sub.length ? listMd(sub) : '') + '</li>';
  }).join('') + '</' + tag + '>';
}
