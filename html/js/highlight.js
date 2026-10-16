// Code block language detection and syntax highlighting with VS Code "Dark+" colors. No dependencies, no network.
//   detectLang(fenceTag, code, fileName) -> language id or ''
//   LANGS[id] = { name, color }            badge label and color (GitHub linguist colors)
//   highlight(code, lang) -> HTML           escaped code with <span class="hl-*"> tokens
'use strict';

const LANGS = {
  csharp: { name: 'C#', color: '#178600' }, python: { name: 'Python', color: '#3572A5' },
  javascript: { name: 'JavaScript', color: '#f1e05a' }, typescript: { name: 'TypeScript', color: '#3178c6' },
  sql: { name: 'SQL', color: '#e38c00' }, powershell: { name: 'PowerShell', color: '#2c6bb3' },
  bash: { name: 'Shell', color: '#89e051' }, json: { name: 'JSON', color: '#cbcb41' },
  html: { name: 'HTML', color: '#e34c26' }, xml: { name: 'XML', color: '#0060ac' }, css: { name: 'CSS', color: '#663399' },
  java: { name: 'Java', color: '#b07219' }, cpp: { name: 'C++', color: '#f34b7d' }, c: { name: 'C', color: '#8a8a8a' },
  go: { name: 'Go', color: '#00ADD8' }, rust: { name: 'Rust', color: '#dea584' }, yaml: { name: 'YAML', color: '#cb171e' },
  markdown: { name: 'Markdown', color: '#3a7bd5' }, text: { name: 'Text', color: '#6e6e6e' }
};

const LANG_ALIASES = {
  cs: 'csharp', 'c#': 'csharp', csharp: 'csharp', py: 'python', python: 'python', python3: 'python',
  js: 'javascript', javascript: 'javascript', jsx: 'javascript', mjs: 'javascript', node: 'javascript',
  ts: 'typescript', typescript: 'typescript', tsx: 'typescript', sql: 'sql', tsql: 'sql', mysql: 'sql', postgresql: 'sql', sqlite: 'sql',
  ps1: 'powershell', psm1: 'powershell', powershell: 'powershell', pwsh: 'powershell', posh: 'powershell',
  sh: 'bash', bash: 'bash', shell: 'bash', zsh: 'bash', console: 'bash', json: 'json', jsonc: 'json',
  html: 'html', htm: 'html', xml: 'xml', xaml: 'xml', csproj: 'xml', svg: 'xml', css: 'css', scss: 'css',
  java: 'java', cpp: 'cpp', 'c++': 'cpp', cc: 'cpp', cxx: 'cpp', hpp: 'cpp', h: 'c', c: 'c', go: 'go', golang: 'go',
  rs: 'rust', rust: 'rust', yaml: 'yaml', yml: 'yaml', md: 'markdown', markdown: 'markdown', txt: 'text', text: 'text', plaintext: 'text'
};

// Fence tag first, then the file extension of a <write>, then a guess from the code itself.
function detectLang(tag, code, fileName) {
  const t = (tag || '').trim().toLowerCase().split(/\s+/)[0];
  if (t && LANG_ALIASES[t]) return LANG_ALIASES[t];
  const ext = fileName && /\.([a-z0-9+#]+)$/i.exec(fileName);
  if (ext && LANG_ALIASES[ext[1].toLowerCase()]) return LANG_ALIASES[ext[1].toLowerCase()];
  return guessLang(code || '');
}

function guessLang(c) {
  const s = c.slice(0, 4000);
  const tests = [
    ['json', /^\s*[\[{]\s*("[^"]*"\s*:|\[|\{|\d|"|$)/.test(s) && !/;\s*$/m.test(s) && /"\s*:/.test(s) ? 5 : 0],
    ['html', (/<!DOCTYPE html|<html[\s>]|<(div|span|body|head|p|a|ul|table|script|style)[\s>]/i.test(s) ? 4 : 0)],
    ['xml', (/^\s*<\?xml|<\/?[A-Za-z]+:[A-Za-z]+|<Project\s/.test(s) ? 4 : 0)],
    ['csharp', score(s, [/\busing System/, /\bnamespace\s+[\w.]+/, /\bpublic\s+(static\s+)?(class|void|async|record|interface)\b/, /\bConsole\.Write/,
      /\bvar\s+\w+\s*=/, /\b(string|int|bool|double|decimal)\??\s+\w+\s*[=;),]/, /\{\s*get;\s*(set;)?\s*\}/, /\bList<\w+>/, /\bTask<|\bawait\b/, /=>\s*[^>]*;/])],
    ['java', score(s, [/\bpublic\s+static\s+void\s+main\s*\(String/, /\bSystem\.out\.print/, /\bimport\s+java\./, /\bextends\s+\w+\s*\{/, /@Override/])],
    ['python', score(s, [/^\s*def\s+\w+\s*\(.*\)\s*(->\s*[\w\[\], ]+)?:\s*$/m, /^\s*(import|from)\s+\w+/m, /\bprint\(/, /\bself\b/, /^\s*(if|for|while|elif|else|try|except|with|class)\b.*:\s*$/m, /\bNone\b|\bTrue\b|\bFalse\b/])],
    ['sql', score(s, [/^\s*(SELECT|INSERT\s+INTO|UPDATE|DELETE\s+FROM|CREATE\s+(TABLE|VIEW|INDEX|PROCEDURE)|ALTER\s+TABLE|WITH\s+\w+\s+AS)\b/im,
      /\bFROM\s+\w+/i, /\bWHERE\b/i, /\b(INNER|LEFT|RIGHT)\s+JOIN\b/i, /\bGROUP\s+BY\b|\bORDER\s+BY\b/i])],
    ['powershell', score(s, [/\$\w+\s*=/, /\b(Get|Set|New|Remove|Write|Invoke|Start|Test)-[A-Z]\w+/, /\bparam\s*\(/i, /-(eq|ne|lt|gt|like|match)\b/, /\|\s*(Where|ForEach|Select)-Object/])],
    ['bash', score(s, [/^#!\/bin\/(ba)?sh/, /^\s*(echo|export|sudo|apt|cd|ls|grep|chmod)\s/m, /\bfi\b|\bdone\b|\besac\b/, /\$\{?\w+\}?/])],
    ['typescript', score(s, [/:\s*(string|number|boolean|void|any)\b/, /\binterface\s+\w+\s*\{/, /\bimport\s+.*\s+from\s+['"]/, /\bexport\s+(const|function|class|type)\b/, /\btype\s+\w+\s*=/])],
    ['javascript', score(s, [/\b(const|let)\s+\w+\s*=/, /\bfunction\s+\w+\s*\(/, /\bconsole\.log\(/, /=>\s*\{/, /\bdocument\.|\bwindow\./, /\brequire\(['"]/])],
    ['cpp', score(s, [/#include\s*<(iostream|vector|string|map)>/, /\bstd::/, /\bcout\s*<</, /\btemplate\s*</])],
    ['c', score(s, [/#include\s*<(stdio|stdlib|string)\.h>/, /\bprintf\s*\(/, /\bmalloc\s*\(/, /\bint\s+main\s*\(/])],
    ['go', score(s, [/^package\s+\w+/m, /\bfunc\s+\w*\s*\(/, /\bfmt\.Print/, /:=/])],
    ['rust', score(s, [/\bfn\s+\w+\s*\(/, /\blet\s+mut\b/, /\bprintln!\(/, /\bimpl\s+\w+/, /->\s*[A-Z&]/])],
    // (selector parts are separated by required whitespace or a combinator, so the regex cannot backtrack exponentially)
    ['css', /^[ \t]*[.#]?[\w-]+(?:[ \t]*[,>+~][ \t]*[.#]?[\w-]+|[ \t]+[.#]?[\w-]+)*[ \t]*\{[^}]*:[^}]*;/m.test(s) && !/\bfunction\b|=>/.test(s) ? 3 : 0],
    ['yaml', /^\s*[\w-]+:\s*\S/m.test(s) && /^\s*-\s+\w/m.test(s) && !/[;{}]/.test(s) ? 2 : 0]
  ];
  let best = '', bestScore = 1;
  tests.forEach(([id, n]) => { if (n > bestScore) { best = id; bestScore = n; } });
  return best;
}
function score(s, res) { return res.reduce((n, r) => n + (r.test(s) ? 1 : 0), 0); }

// ---- highlighting ----
const KW = {
  csharp: { ctl: 'if else switch case default for foreach while do break continue return goto throw try catch finally yield await using lock when in',
    kw: 'using abstract as base bool byte char checked class const decimal delegate double enum event explicit extern false fixed float implicit int interface internal is long namespace new null object operator out override params private protected public readonly record ref sbyte sealed short sizeof stackalloc static string struct this true typeof uint ulong unchecked unsafe ushort var virtual void volatile async get set init value nameof dynamic where select from orderby group by into let join on equals ascending descending partial global required with not and or' },
  java: { ctl: 'if else switch case default for while do break continue return throw try catch finally',
    kw: 'abstract boolean byte char class const double enum extends final float implements import instanceof int interface long native new null package private protected public short static strictfp super synchronized this throws transient true false var void volatile record' },
  javascript: { ctl: 'if else switch case default for while do break continue return throw try catch finally await yield of in',
    kw: 'async class const debugger delete export extends false function import instanceof let new null super this true typeof undefined var void with static get set from as' },
  python: { ctl: 'if elif else for while break continue return raise try except finally with yield pass assert import from as await',
    kw: 'and or not is in def class lambda global nonlocal del True False None async self cls print len range' },
  sql: { ctl: 'case when then else end if begin return while',
    kw: 'select from where and or not in is null like between join inner left right full outer cross on as group by order having insert into values update set delete create alter drop table view index procedure function trigger primary key foreign references unique default distinct top limit offset union all exists with asc desc declare exec execute int varchar nvarchar char text date datetime decimal bit bigint float count sum avg min max cast convert coalesce isnull over partition row_number' },
  powershell: { ctl: 'if elseif else switch foreach for while do until break continue return throw try catch finally trap exit',
    kw: 'function param begin process end filter class enum using in true false null' },
  bash: { ctl: 'if then else elif fi case esac for while until do done in break continue return exit',
    kw: 'function local export readonly declare echo printf read cd source alias unset set shift true false sudo' },
  go: { ctl: 'if else switch case default for range break continue return goto fallthrough select defer go',
    kw: 'package import func var const type struct interface map chan nil true false string int int64 float64 bool byte rune error make new len append' },
  rust: { ctl: 'if else match for while loop break continue return in',
    kw: 'fn let mut const static struct enum impl trait pub use mod crate self Self super as where type ref move unsafe async await dyn true false Some None Ok Err' },
  c: { ctl: 'if else switch case default for while do break continue return goto',
    kw: 'auto char const double enum extern float int long register short signed sizeof static struct typedef union unsigned void volatile NULL true false bool' }
};
KW.typescript = { ctl: KW.javascript.ctl, kw: KW.javascript.kw + ' interface type enum implements private public protected readonly declare namespace abstract keyof any number string boolean never unknown' };
KW.cpp = { ctl: KW.c.ctl + ' try catch throw', kw: KW.c.kw + ' class namespace template typename public private protected virtual override new delete this nullptr using std auto constexpr inline operator friend explicit' };
const KWSET = {};
Object.keys(KW).forEach(l => {
  const ci = l === 'sql' || l === 'powershell';
  const norm = w => ci ? w.toLowerCase() : w;
  KWSET[l] = { ctl: new Set(KW[l].ctl.split(' ').map(norm)), kw: new Set(KW[l].kw.split(' ').map(norm)), ci };
});

function span(cls, text) { return '<span class="hl-' + cls + '">' + esc(text) + '</span>'; }

function highlight(code, lang) {
  if (!lang || lang === 'text' || lang === 'markdown') return esc(code);
  if (lang === 'json') return hlJson(code);
  if (lang === 'html' || lang === 'xml') return hlMarkup(code);
  if (lang === 'css') return hlCss(code);
  if (lang === 'yaml') return hlYaml(code);
  return hlCode(code, lang);
}

// C-like and script languages: one left-to-right scan with a sticky regex per token kind.
function hlCode(code, lang) {
  const k = KWSET[lang] || KWSET.c;
  const hashComment = lang === 'python' || lang === 'bash' || lang === 'powershell';
  const rules = [];
  if (lang === 'powershell') rules.push(['com', /<#[\s\S]*?(#>|$)/y]);
  if (hashComment) rules.push(['com', /#.*/y]);
  if (lang === 'sql') rules.push(['com', /--.*/y]);
  if (!hashComment || lang === 'powershell') rules.push(['com', /\/\/.*|\/\*[\s\S]*?(\*\/|$)/y]);
  if (lang === 'c' || lang === 'cpp' || lang === 'csharp') rules.push(['pre', /^[ \t]*#\s*\w+.*/my]);
  if (lang === 'python') rules.push(['str', /[rbfu]{0,2}("""[\s\S]*?("""|$)|'''[\s\S]*?('''|$))/iy]);
  if (lang === 'csharp') rules.push(['str', /[$@]{1,2}"(?:[^"]|"")*("|$)/y]);
  if (lang === 'javascript' || lang === 'typescript' || lang === 'go') rules.push(['str', /`(?:[^`\\]|\\.)*(`|$)/y]);
  if (lang === 'rust') rules.push(['str', /r#*"[\s\S]*?("#*|$)/y]);
  rules.push(['str', /"(?:[^"\\\n]|\\.)*("|$)|'(?:[^'\\\n]|\\.)*('|$)/y]);
  if (lang === 'powershell' || lang === 'bash') rules.push(['var', /\$\{?[\w:]+\}?/y]);
  if (lang === 'csharp' || lang === 'java' || lang === 'python' || lang === 'typescript') rules.push(['attr', /@\w+(\.\w+)*/y]);
  if (lang === 'powershell') rules.push(['fn', /\b[A-Z][a-z]+-[A-Z]\w*/y], ['op', /-(eq|ne|lt|le|gt|ge|like|notlike|match|notmatch|and|or|not|in|notin|contains|replace|split|join|f)\b/iy]);
  rules.push(['num', /\b(0x[0-9a-fA-F_]+|0b[01_]+|\d[\d_]*(\.\d+)?([eE][+-]?\d+)?)[uUlLfFdDmM]*\b/y]);
  rules.push(['id', /[A-Za-z_][\w]*!?/y]);
  rules.push(['ws', /\s+/y]);
  rules.push(['op', /[^\w\s]/y]);
  let out = '', i = 0, prevWord = '';
  while (i < code.length) {
    let hit = null;
    for (const [cls, re] of rules) {
      re.lastIndex = i;
      const m = re.exec(code);
      if (m && m[0].length) { hit = [cls, m[0]]; break; }
    }
    if (!hit) { out += esc(code[i]); i++; continue; }
    let [cls, text] = hit;
    if (cls === 'id') {
      const w = k.ci ? text.toLowerCase() : text;
      // a call or declaration follows: name( or name<T, U>( (looks at most 80 characters ahead)
      const after = /^\s*(\(|<[\w\s,?.]*(<[\w\s,?.]*>[\w\s,?.]*)?>\s*\()/.test(code.slice(i + text.length, i + text.length + 80));
      if (k.ctl.has(w)) cls = 'ctl';
      else if (k.kw.has(w)) cls = /^(true|false|null|none|nullptr|nil|undefined)$/i.test(w) ? 'kw' : 'kw';
      else if (lang === 'rust' && text.endsWith('!')) cls = 'fn';
      else if (after) cls = 'fn';
      else if (/^(class|struct|interface|enum|record|new|extends|implements|type|impl|trait|is|as)$/.test(prevWord) || (/^[A-Z]/.test(text) && lang !== 'sql' && lang !== 'python' && lang !== 'bash' && lang !== 'powershell' && !/^[A-Z0-9_]+$/.test(text))) cls = 'type';
      else if (/^[A-Z][A-Z0-9_]+$/.test(text)) cls = 'const';
      else cls = 'var';
      prevWord = w;
    } else if (cls !== 'ws') prevWord = '';
    out += cls === 'ws' || cls === 'op' ? esc(text) : span(cls, text);
    i += text.length;
  }
  return out;
}

function hlJson(code) {
  return code.replace(/("(?:[^"\\]|\\.)*")(\s*:)?|\b(true|false|null)\b|-?\d+(\.\d+)?([eE][+-]?\d+)?|[^"\w-]+|[\w-]+/g, (m, s, colon, lit) => {
    if (s) return colon ? span('prop', s) + esc(colon) : span('str', s);
    if (lit) return span('kw', m);
    if (/^-?\d/.test(m)) return span('num', m);
    return esc(m);
  });
}

function hlMarkup(code) {
  return code.replace(/<!--[\s\S]*?(-->|$)|<\/?[\w:-]+|\/?>|([\w:-]+)(=)("[^"]*"|'[^']*')|[^<>]+|[<>]/g, (m, x, an, eq, av) => {
    if (m.startsWith('<!--')) return span('com', m);
    if (/^<\/?[\w:-]/.test(m)) return esc(m.match(/^<\/?/)[0]) + span('tag', m.replace(/^<\/?/, ''));
    if (an) return span('prop', an) + esc(eq) + span('str', av);
    if (m === '>' || m === '/>' || m === '<') return span('punc', m);
    return esc(m);
  });
}

function hlCss(code) {
  return code.replace(/\/\*[\s\S]*?(\*\/|$)|([\w-]+)(\s*:)(?=[^{}]*[;}])|"[^"]*"|'[^']*'|#[0-9a-fA-F]{3,8}\b|-?\d+(\.\d+)?(px|em|rem|%|vh|vw|s|ms|deg)?|[.#]?[\w-]+|[^\w\s]|\s+/g, (m, c, prop, colon) => {
    if (m.startsWith('/*')) return span('com', m);
    if (prop) return span('prop', prop) + esc(colon);
    if (/^["']/.test(m)) return span('str', m);
    if (/^#[0-9a-fA-F]{3,8}$/.test(m) || /^-?\d/.test(m)) return span('num', m);
    if (/^[.#]?[\w-]+$/.test(m)) return span('tag', m);
    return esc(m);
  });
}

function hlYaml(code) {
  return code.split('\n').map(line => {
    const m = /^(\s*-?\s*)([\w.-]+)(\s*:)(.*)$/.exec(line);
    if (/^\s*#/.test(line)) return span('com', line);
    if (!m) return esc(line);
    const v = m[4];
    const val = /^\s*#/.test(v) ? span('com', v) : /^\s*-?\d/.test(v) ? span('num', v) : /^\s*(true|false|null|~)\s*$/.test(v) ? span('kw', v) : v.trim() ? span('str', v) : esc(v);
    return esc(m[1]) + span('prop', m[2]) + esc(m[3]) + val;
  }).join('\n');
}

// Badge text color: black on light language colors, white on dark ones.
function badgeText(hex) {
  const n = parseInt(hex.slice(1), 16), r = n >> 16, g = (n >> 8) & 255, b = n & 255;
  return (0.299 * r + 0.587 * g + 0.114 * b) > 150 ? '#111' : '#fff';
}
