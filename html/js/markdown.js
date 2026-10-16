// Reply rendering for the chat page: code fences, markdown and tool tags. No dependencies, no network.
function esc(s) { return String(s).replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

// Reply rendering: code fences become boxes with a copy button; <write>/<read>/<list> tags are highlighted.
function render(text, streaming) {
  // File writes become code boxes labelled with the path (inner code fences dropped).
  text = text.replace(/<write\s+path\s*=\s*"([^"]+)"\s*>\r?\n?([\s\S]*?)(<\/write>|$)/g, (m, p, c) =>
    '```✎ ' + p + '\n' + c.replace(/^\s*```[^\n]*\n/, '').replace(/\n?```\s*$/, '') + '\n```');
  const parts = text.split('```');
  let html = '';
  parts.forEach((part, i) => {
    if (i % 2 === 1) {
      const nl = part.indexOf('\n');
      const lang = nl >= 0 ? part.slice(0, nl).trim() : '';
      const code = nl >= 0 ? part.slice(nl + 1) : part;
      const done = i < parts.length - 1 || !streaming;
      html += '<div class="code"><header><span>' + esc(lang || 'code') + '</span>' +
              (done ? '<button class="copy">Copy</button>' : '') + '</header><pre>' + esc(code.replace(/\n$/, '')) + '</pre></div>';
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
    .replace(/&lt;\/?(read|list)\b[\s\S]*?&gt;/g, m => '<span class="tag">' + m + '</span>')
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
