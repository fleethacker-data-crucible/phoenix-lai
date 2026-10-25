"""Does the syntax library (RAG) make the model's code better? Dev-only evaluation, three arms:
  off  retrieval disabled            old  the previous sheets (old-docs\\, served by a second root)    new  the current sheets
Every arm runs the same server code (so the deterministic checker is in all three) with skills off, greedy sampling and
a fresh conversation per prompt; only the documents differ. Replies are scored with real tools:
  powershell  Windows PowerShell 5.1 parser + command and parameter lookup (check-ps.ps1)
  csharp      csc.exe from .NET Framework 4.8 (C# 5): compile errors
  oracle      sqlglot's Oracle parser on plain SQL + Oracle dialect mistakes
  sql, duckdb executed in DuckDB against test tables and files (duck_fixture.py): parse, bind and run errors
  javascript  node --check + mistakes that fail at run time (.length(), arr.contains, ESM import in a CommonJS file)
Both the first draft and the final reply (after the server's check pass) are scored.

  python rag-eval.py --arms off,new [--out results\\<dir>] [--langs ...] [--limit N]   server with the current docs
  python rag-eval.py --arms old --out results\\<dir>                                    server started on the old-docs root
  python rag-eval.py --report results\\<dir>                                            (re)write report.html
  python rag-eval.py --rescore results\\<dir>                                           re-check every reply, then report
"""
import argparse, html, json, os, re, subprocess, sys, tempfile, time, urllib.request
from datetime import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import duck_fixture

CSC = r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
SYSTEM = 'You are a concise, expert assistant running locally.'   # the chat page's default system prompt
ARMS = ('off', 'old', 'new')
ARM_LABEL = {'off': 'RAG off', 'old': 'Old RAG', 'new': 'New RAG'}
LABEL = {'powershell': 'PowerShell 5.1', 'csharp': 'C# (.NET Framework 4.8)', 'oracle': 'Oracle SQL', 'sql': 'SQL (standard)',
         'duckdb': 'DuckDB SQL', 'javascript': 'JavaScript (Node)', 'powershell7': 'PowerShell 7', 'vbscript': 'VBScript',
         'vba': 'Excel VBA', 'cmd': 'Batch (CMD)', 'dax': 'DAX', 'powerquery': 'Power Query M', 'qlik': 'Qlik script',
         'excel': 'Excel formulas', 'html': 'HTML', 'css': 'CSS'}
TESTABLE = ['powershell', 'csharp', 'oracle', 'sql', 'duckdb', 'javascript']   # phase A: languages with real validators


def post(base, path, body, stream=False):
    req = urllib.request.Request(base + path, data=json.dumps(body).encode(), headers={'Content-Type': 'application/json'})
    r = urllib.request.urlopen(req, timeout=900)
    return r if stream else json.loads(r.read().decode('utf-8'))


def ask(base, prompt):
    body = {'messages': [{'role': 'system', 'content': SYSTEM}, {'role': 'user', 'content': prompt}],
            'temperature': 0, 'top_k': 1, 'top_p': 1, 'repeat_penalty': 1.1, 'max_tokens': 1500, 'skills': False}
    t0 = time.time()
    r = post(base, '/api/chat', body, stream=True)
    draft, final, docs, done, buf, err, work = [], None, [], {}, '', None, []
    for raw in r:
        buf += raw.decode('utf-8')
        while '\n\n' in buf:
            line, buf = buf.split('\n\n', 1)
            if not line.startswith('data: '): continue
            ev = json.loads(line[6:])
            if 't' in ev: draft.append(ev['t'])
            elif 'replace' in ev: final = ev['replace']
            elif 'w' in ev: work.append(ev['w'])
            elif 'rag' in ev: docs = [h['file'] + (' > ' + h['title'] if h.get('title') else '') for h in ev['rag']]
            elif 'error' in ev: err = ev['error']
            elif 'done' in ev: done = ev
    d = ''.join(draft)
    return {'draft': d, 'final': final if final is not None else d, 'docs': docs, 'error': err, 'seconds': round(time.time() - t0, 1),
            'tokens': done.get('tokens', 0), 'ctx_used': done.get('ctx_used', 0), 'work': ''.join(work)[-3000:]}


def blocks(text, langs):
    out = []
    for m in re.finditer(r'```([^\n`]*)\n([\s\S]*?)```', text):
        tag = m.group(1).strip().lower()
        if not tag or tag in langs: out.append(m.group(2))
    return out


# ---------- validators: each returns a list of problems ----------
def check_powershell(text):
    code = '\n\n'.join(blocks(text, {'powershell', 'ps1', 'ps', 'posh', 'pwsh'}))
    if not code.strip(): return ['no PowerShell code block']
    with tempfile.NamedTemporaryFile('w', suffix='.ps1', delete=False, encoding='utf-8-sig') as f: f.write(code); path = f.name
    try:
        out = subprocess.run(['powershell.exe', '-NoProfile', '-File', os.path.join(HERE, 'check-ps.ps1'), '-Path', path],
                             capture_output=True, text=True, timeout=180).stdout.strip().splitlines()
        j = json.loads(out[-1]) if out else {'parse': ['checker failed'], 'commands': [], 'params': []}
    finally: os.unlink(path)
    lst = lambda v: v if isinstance(v, list) else ([v] if v else [])
    return ['syntax: ' + x for x in lst(j.get('parse'))] + ['unknown command: ' + x for x in lst(j.get('commands'))] + \
           ['unknown parameter: ' + x for x in lst(j.get('params'))]


def check_csharp(text):
    bs = blocks(text, {'csharp', 'cs', 'c#'})
    if not bs: return ['no C# code block']
    code = max(bs, key=len)
    d = tempfile.mkdtemp()
    src = os.path.join(d, 'Program.cs')
    open(src, 'w', encoding='utf-8').write(code)
    target = 'exe' if re.search(r'\bstatic\s+(async\s+)?(void|int|Task(<int>)?)\s+Main\s*\(', code) else 'library'
    r = subprocess.run([CSC, '/nologo', '/t:' + target, '/out:' + os.path.join(d, 'out.bin'),
                        '/r:System.dll,System.Core.dll,System.Xml.dll,System.Xml.Linq.dll,System.Data.dll,System.Runtime.Serialization.dll', src],
                       capture_output=True, text=True, timeout=120)
    errs = [re.sub(r'^.*?\((\d+),\d+\): error ', r'line \1: ', l).strip() for l in r.stdout.splitlines() if ': error ' in l]
    return ['compile: ' + e for e in dict.fromkeys(errs)]


ORACLE_PITFALLS = [
    (r'\bSELECT\s+TOP\s+\d', 'SQL Server TOP'), (r'\bLIMIT\s+\d', 'MySQL/Postgres LIMIT'), (r'\bISNULL\s*\(', 'SQL Server ISNULL'),
    (r'\bIFNULL\s*\(', 'MySQL IFNULL'), (r'\bGETDATE\s*\(', 'SQL Server GETDATE'), (r'\bNOW\s*\(\s*\)', 'MySQL NOW()'),
    (r'\bAUTO_INCREMENT\b', 'MySQL AUTO_INCREMENT'), (r'\bIDENTITY\s*\(\s*\d', 'SQL Server IDENTITY(1,1)'), (r'\bDATEADD\s*\(', 'SQL Server DATEADD'),
    (r'\bDATEDIFF\s*\(', 'SQL Server DATEDIFF'), (r'\bLEN\s*\(', 'SQL Server LEN'), (r'\bCHARINDEX\s*\(', 'SQL Server CHARINDEX'),
    (r'\bNVARCHAR\s*\(\s*MAX', 'SQL Server NVARCHAR(MAX)'), (r"(=|<>|!=)\s*''", "comparison with '' (always unknown in Oracle)"),
    (r'\bCREATE\s+TABLE\b[^;]*\bBOOLEAN\b', 'BOOLEAN column type (not in Oracle 19c tables)'), (r'\bSERIAL\b', 'Postgres SERIAL'),
    (r"'\s*\+\s*\w|\w\s*\+\s*'", "string concatenation with + (use ||)"), (r'\bDECLARE\s+@', 'T-SQL variable'),
    (r'\bSTRING_AGG\s*\(', 'STRING_AGG (Oracle: LISTAGG)'), (r'\bFROM\s+\w+\s+AS\s+\w+', 'AS before a table alias'),
    (r'^\s*SELECT\b(?![\s\S]*?\bFROM\b)[^;]*;', 'SELECT without FROM (use DUAL)'),
]
PLSQL = re.compile(r'^\s*(DECLARE|BEGIN|CREATE\s+(OR\s+REPLACE\s+)?(PROCEDURE|FUNCTION|PACKAGE|TRIGGER|TYPE))\b', re.I)


def check_oracle(text):
    import sqlglot
    from sqlglot.errors import ParseError
    bs = blocks(text, {'sql', 'plsql', 'oracle', 'pl/sql', 'pls'})
    if not bs: return ['no SQL code block']
    code = '\n'.join(bs)
    probs = []
    clean = re.sub(r'--[^\n]*|/\*[\s\S]*?\*/', '', code)
    for pat, what in ORACLE_PITFALLS:
        for m in re.finditer(pat, clean, re.I | re.M):
            # FETCH c BULK COLLECT INTO v LIMIT 1000 is valid PL/SQL
            if 'LIMIT' in what and re.search(r'BULK\s+COLLECT\s+INTO\s+[\w$#.,\s]+$', clean[max(0, m.start() - 120):m.start()], re.I): continue
            probs.append('dialect: ' + what + ' (' + m.group(0).strip()[:40] + ')')
    for unit in re.split(r'\n\s*/\s*\n', clean + '\n'):
        if not unit.strip() or PLSQL.search(unit): continue
        for stmt in [s for s in unit.split(';') if s.strip()]:
            if re.match(r'^\s*(SET|SHOW|EXEC|EXECUTE|COMMIT|ROLLBACK|SPOOL|PROMPT|/)\b', stmt, re.I): continue
            try: sqlglot.parse_one(stmt, read='oracle')
            except ParseError as e: probs.append('parse: ' + str(e).split('\n')[0][:120])
            except Exception: pass
    return probs


def check_sqlrun(text):
    bs = blocks(text, {'sql', 'postgresql', 'postgres', 'duckdb', 'psql'})
    if not bs: return ['no SQL code block']
    return ['run: ' + p for p in duck_fixture.run('\n;\n'.join(bs))]


JS_RULES = [
    (r'\.length\s*\(\s*\)', '.length() (length is a property)'),
    (r'(?<!classList)(?<!\.dataset)\.contains\s*\(', '.contains() does not exist on arrays or strings (use .includes)'),
    (r'\.size\s*\(\s*\)', '.size() (Map/Set size is a property)'),
]


def check_javascript(text, prompt=''):
    bs = blocks(text, {'javascript', 'js', 'node', 'mjs', 'cjs'})
    if not bs: return ['no JavaScript code block']
    probs = []
    for code in bs:
        browser = bool(re.search(r'\bdocument\.|\bwindow\.', code))
        esm = bool(re.search(r'^\s*(import\s+[\w{*]|export\s+(default|const|function|class|async|\{))', code, re.M))
        if esm and not browser and 'module' not in prompt.lower() and re.search(r'\brequire\s*\(', code):
            probs.append('module: mixes import/export with require')
        elif esm and not browser and 'module' not in prompt.lower():
            probs.append('module: ES import/export in a CommonJS .js file (Node fails with "Cannot use import statement outside a module")')
        f = tempfile.NamedTemporaryFile('w', suffix='.mjs' if esm else '.js', delete=False, encoding='utf-8')
        f.write(code if esm else '(async function () {\n' + code + '\n})();\n'); f.close()
        r = subprocess.run(['node', '--check', f.name], capture_output=True, text=True, errors='replace')
        os.unlink(f.name)
        if r.returncode: probs.append('syntax: ' + [l for l in r.stderr.splitlines() if l.strip()][-1][:160] if r.stderr.strip() else 'syntax error')
        bare = re.sub(r'//[^\n]*|/\*[\s\S]*?\*/|`(?:\\.|[^`])*`|"(?:\\.|[^"\n])*"|\'(?:\\.|[^\'\n])*\'', '""', code)
        for pat, what in JS_RULES:
            if re.search(pat, bare): probs.append('runtime: ' + what)
    return probs


def check_powershell7(text):
    code = '\n\n'.join(blocks(text, {'powershell', 'ps1', 'ps', 'posh', 'pwsh'}))
    if not code.strip(): return ['no PowerShell code block']
    with tempfile.NamedTemporaryFile('w', suffix='.ps1', delete=False, encoding='utf-8-sig') as f: f.write(code); path = f.name
    try:
        out = subprocess.run(['pwsh', '-NoProfile', '-File', os.path.join(HERE, 'check-ps.ps1'), '-Path', path],
                             capture_output=True, text=True, timeout=180).stdout.strip().splitlines()
        j = json.loads(out[-1]) if out else {'parse': ['checker failed']}
    finally: os.unlink(path)
    lst = lambda v: v if isinstance(v, list) else ([v] if v else [])
    return ['syntax: ' + x for x in lst(j.get('parse'))] + ['unknown command: ' + x for x in lst(j.get('commands'))] + \
           ['unknown parameter: ' + x for x in lst(j.get('params'))]


def cscript_compile(code):
    """VBScript compile errors: cscript parses the whole file before it runs anything, and the first statement quits."""
    lines = code.replace('\r\n', '\n').split('\n')
    pre = [l for l in lines if re.match(r'^\s*Option Explicit', l, re.I)]
    rest = [l for l in lines if not re.match(r'^\s*Option Explicit', l, re.I)]
    # newline='': the lines are already joined with \r\n (text mode would write \r\r\n, which breaks "_" continuations)
    f = tempfile.NamedTemporaryFile('w', suffix='.vbs', delete=False, encoding='ascii', errors='replace', newline='')
    f.write('\r\n'.join(pre + ['WScript.Quit 0'] + rest) + '\r\n'); f.close()
    r = subprocess.run(['cscript.exe', '//nologo', f.name], capture_output=True, text=True, errors='replace', timeout=60)
    os.unlink(f.name)
    if r.returncode == 0: return []
    m = re.search(r'\((\d+), \d+\) (.*)', r.stdout + r.stderr)
    return ['compile: ' + (('line %d: ' % (int(m.group(1)) - 1) + m.group(2).strip()) if m else (r.stdout + r.stderr).strip()[:160])]


def check_vbscript(text):
    bs = blocks(text, {'vbscript', 'vbs', 'vb'})
    if not bs: return ['no VBScript code block']
    return cscript_compile('\n'.join(bs))


def vba_to_vbscript(code):
    """Strips what VBScript lacks but VBA has (types, labels, named arguments, $ functions, Optional), so cscript can
    check the structure that remains: blocks, statements and expressions."""
    c = re.sub(r"'.*$", '', code, flags=re.M)
    c = re.sub(r'(?i)\bAs\s+(New\s+)?[\w.]+(\s*\(\s*\))?', '', c)
    c = re.sub(r'(?i)\bOptional\s+', '', c)
    c = re.sub(r'(?i)(\(|,)(\s*(?:ByVal\s+|ByRef\s+)?\w+)\s*=\s*[^,)]+', r'\1\2', c)   # parameter defaults
    c = re.sub(r'(?i)\bOn\s+Error\s+GoTo\s+(?!0\b)\w+', 'On Error Resume Next', c)
    c = re.sub(r'(?im)^\s*Resume\s+\w+\s*$', '', c)
    c = re.sub(r'(?m)^\s*[A-Za-z_]\w*:\s*$', '', c)                     # line labels
    c = re.sub(r'\b(\w+):=', '', c)                                       # named arguments
    c = re.sub(r'\b(Trim|Left|Right|Mid|UCase|LCase|Format|Chr|Str|String|Space|LTrim|RTrim|Dir|Environ)\$', r'\1', c)
    c = re.sub(r'(?im)^\s*(Public|Private|Global)\s+Const\b', 'Const', c)
    c = re.sub(r'(?im)^\s*Static\s+', 'Dim ', c)
    c = re.sub(r'(?im)\bDim\s+(\w+)\s*,\s*', r'Dim \1, ', c)
    c = re.sub(r'(?i)\bDebug\.Print\b', 'WScript.Echo', c)
    c = re.sub(r'(?im)^(\s*Next)\s+\w+\s*$', r'\1', c)                     # VBScript's Next takes no counter
    c = re.sub(r'(?i)#(\d{1,2}/\d{1,2}/\d{2,4})#', r'"\1"', c)
    return c


def check_vba(text):
    bs = blocks(text, {'vba', 'vb', 'bas', 'vbnet'})
    if not bs: return ['no VBA code block']
    code = '\n'.join(bs)
    probs = cscript_compile(vba_to_vbscript(code))
    clean = re.sub(r"'.*$|\"[^\"\n]*\"", '', code, flags=re.M)
    for pat, what in ((r'&&|\|\||!=|==', 'C-style operator'), (r'(?<![<>=!])(?:\+|-|\*|/|&)=', 'compound assignment'),
                      (r'(?im)^\s*Return\s+\S', 'Return value (assign to the function name)'), (r'(?im)^\s*(Try|Catch)\b', 'try/catch')):
        if re.search(pat, clean): probs.append('rule: ' + what)
    return probs


def balance(code, pairs='()[]{}'):
    st = []
    for ch in code:
        if ch in pairs[0::2]: st.append(ch)
        elif ch in pairs[1::2]:
            if not st or pairs[pairs.index(st.pop()) + 1] != ch: return False
    return not st


def rule_check(text, tags, rules, strip, pairs='()[]{}', name='code'):
    bs = blocks(text, tags)
    if not bs: return ['no ' + name + ' code block']
    probs = []
    for code in bs:
        clean = re.sub(strip, lambda m: re.sub(r'[^\n]', ' ', m.group(0)), code)
        for pat, what in rules:
            if re.search(pat, clean): probs.append('rule: ' + what)
        if not balance(clean, pairs): probs.append('rule: unbalanced brackets')
    return probs


def check_cmd(text):
    return rule_check(text, {'bat', 'batch', 'cmd', 'dos', 'batchfile', 'shell'}, [
        (r'(?im)^\s*set\s+(?!/[ap]\b)"?[^=\s"]+\s+=', 'set x = value (spaces become part of the name)'),
        (r'(?i)\bfor\s+(?:/[a-z]\s+(?:"[^"\n]*"\s+)?)?%[a-z]\b(?!%)', 'single % loop variable in a batch file'),
        (r'(?i)\bif\s+errorlevel\s+0\b', 'if errorlevel 0 (always true)'),
        (r'(?im)^\s*(rm|cp|mv|ls|touch|chmod|export)\s', 'Unix command'), (r'(?<![\w\\%!])\$[A-Za-z_]\w*', '$variable')],
        r'(?im)^\s*(rem\b|::).*$|"[^"\n]*"|\^[()]', '()', 'batch')


def check_dax(text):
    probs = rule_check(text, {'dax', 'daxquery'}, [
        (r'!=', '!= (DAX uses <>)'), (r'(?i)\bBLANK\b(?!\s*\()', 'BLANK without ()'),
        (r'(?i)\bSUM\s*\(\s*[^()]*?(?:\]|\))\s*[-+*/]\s*[^()]*\)', 'SUM over an expression (use SUMX)'),
        (r'(?is)^(?!.*\bRETURN\b).*\bVAR\b', 'VAR without RETURN')], r'//.*$|--.*$|/\*[\s\S]*?\*/|"(?:[^"]|"")*"|\'(?:[^\']|\'\')*\'', '()[]{}', 'DAX')
    for code in blocks(text, {'dax'}):
        for m in re.finditer(r'(?i)\b(AND|OR)\s*\(', code):
            depth, args = 0, 1
            for ch in code[m.end():]:
                if ch in '([{': depth += 1
                elif ch in ')]}':
                    if depth == 0: break
                    depth -= 1
                elif ch == ',' and depth == 0: args += 1
            if args > 2: probs.append('rule: ' + m.group(1).upper() + ' with more than two arguments')
    return probs


def check_m(text):
    probs = rule_check(text, {'powerquery', 'power-query', 'm', 'pq'}, [
        (r'!=|==|&&|\|\|', 'C-style operator'), (r'\bTable\.SelectRows\s*\([^,()]+,\s*\[', 'SelectRows without each'),
        (r',\s*\n\s*in\b', 'comma before in'), (r'(?<![\w.#])(True|False|Null|TRUE|FALSE)\b', 'wrong case for true/false/null'),
        (r'\b(table|text|list|number|record)\.[A-Za-z]', 'lower-case library name')], r'//.*$|/\*[\s\S]*?\*/|"(?:[^"]|"")*"', '()[]{}', 'M')
    for code in blocks(text, {'powerquery', 'power-query', 'm', 'pq'}):
        c = re.sub(r'"(?:[^"]|"")*"', '""', code)
        if len(re.findall(r'\bif\b', c)) > len(re.findall(r'\belse\b', c)): probs.append('rule: if without else')
    return probs


def check_qlik(text):
    probs = rule_check(text, {'qlik', 'qvs', 'qlikview', 'qliksense', 'sql'}, [
        (r'!=|==|&&|\|\|', 'C-style operator'), (r'(?i)\bSELECT\b[^;]*\bFROM\s+\[?[^\s;\]]+\.(csv|txt|xlsx?|qvd)\b', 'SELECT from a file (use LOAD)'),
        (r'(?<![\w$])\$(?!\()[A-Za-z_]\w*', '$variable without $( )')], r'(?<!:)//.*$|/\*[\s\S]*?\*/|\'(?:[^\']|\'\')*\'|"[^"\n]*"', '()[]{}', 'Qlik')
    for code in blocks(text, {'qlik', 'qvs', 'qlikview', 'qliksense', 'sql'}):
        prev = ''
        for l in re.sub(r'(?<!:)//.*$', '', code, flags=re.M).split('\n'):
            t = l.strip()
            if re.match(r'^[\w\[\] ]+:$', t) and prev and not prev.endswith(';'): probs.append('rule: statement before a table label has no ;')
            if t: prev = t
    return probs


def check_excel(text):
    import formulas
    bs = blocks(text, {'excel', 'formula', 'xlsx', 'excel-formula', ''})
    lines = [l.strip() for b in bs for l in b.split('\n')] if bs else [l.strip() for l in text.split('\n') if l.strip().startswith('=')]
    fs = [re.sub(r"\s+'.*$|\s+//.*$", '', l) for l in lines if l.startswith('=')]
    if not fs: return ['no Excel formula']
    probs = []
    for f in fs:
        g = re.sub(r"\b[A-Za-z_][\w.]*\[\[?@?[A-Za-z_][^\]]*\]\]?(?::\[[^\]]*\])?\]?", 'A1:A10', f)     # structured refs -> a range
        try: formulas.Parser().ast(g)
        except Exception as e:
            m = str(e).split('\n')[0]
            if not re.search(r'(?i)not implemented', m): probs.append('parse: ' + f[:60] + ' -> ' + m[:80])
        if re.search(r'==|!=|&&|\|\|', re.sub(r'"[^"]*"', '', f)): probs.append('rule: C-style operator')
        if re.search(r'(?i)\bVLOOKUP\s*\(', f):
            depth, args = 0, 1
            for ch in f[re.search(r'(?i)\bVLOOKUP\s*\(', f).end():]:
                if ch == '(': depth += 1
                elif ch == ')':
                    if depth == 0: break
                    depth -= 1
                elif ch == ',' and depth == 0: args += 1
            if args == 3: probs.append('rule: VLOOKUP without exact-match argument')
    return probs


def check_html(text):
    import html5lib
    bs = blocks(text, {'html', 'htm', 'xhtml'})
    if not bs: return ['no HTML code block']
    probs = []
    serious = ('unexpected-end-tag', 'end-tag-too-early', 'expected-closing-tag-but-got-eof', 'unexpected-end-tag-before-html',
               'adoption-agency-1.3', 'non-void-element-with-trailing-solidus', 'end-tag-too-early-named', 'unexpected-cell-end-tag',
               'duplicate-attribute', 'expected-attribute-name-but-got-eof', 'invalid-codepoint')
    for code in bs:
        p = html5lib.HTMLParser(strict=False)
        if re.search(r'(?i)<html|<!doctype', code): p.parse(code)
        else: p.parseFragment(code)
        for pos, err, data in p.errors:
            if err in serious: probs.append('html: ' + err + (' ' + str(data.get('name', '')) if isinstance(data, dict) else ''))
        clean = re.sub(r'<!--[\s\S]*?-->', '', code)
        for m in re.finditer(r'(?i)</(area|base|br|col|embed|hr|img|input|link|meta|source|track|wbr)\s*>', clean): probs.append('html: closing tag on void <' + m.group(1) + '>')
        ids = re.findall(r'(?i)\sid\s*=\s*["\']([^"\']+)', clean)
        for d in {i for i in ids if ids.count(i) > 1}: probs.append('html: duplicate id ' + d)
        for m in re.finditer(r'(?i)<img\b[^>]*>', clean):
            if not re.search(r'(?i)\balt\s*=', m.group(0)): probs.append('html: img without alt')
    return probs


_CSS_PROPS = None


def check_css(text):
    import tinycss2
    global _CSS_PROPS
    if _CSS_PROPS is None: _CSS_PROPS = set(open(os.path.join(HERE, 'css-properties.txt'), encoding='utf-8').read().split())
    bs = blocks(text, {'css', 'scss'})
    if not bs: return ['no CSS code block']
    probs = []
    lengths = re.compile(r'^(width|height|min-width|max-width|min-height|max-height|margin(-\w+)?|padding(-\w+)?|top|right|bottom|left|font-size|gap|row-gap|column-gap|border-radius|letter-spacing)$')

    def decls(rules):
        for r in rules:
            if r.type == 'error': probs.append('parse: ' + r.message); continue
            content = getattr(r, 'content', None)
            if content is None: continue
            if r.type == 'at-rule' and r.lower_at_keyword in ('media', 'supports', 'container', 'layer', 'document'):
                decls(tinycss2.parse_rule_list(content, skip_comments=True, skip_whitespace=True)); continue
            if r.type == 'at-rule' and r.lower_at_keyword in ('keyframes', 'font-face', 'page', 'property', 'import', 'charset'): continue
            for d in tinycss2.parse_blocks_contents(content, skip_comments=True, skip_whitespace=True):
                if d.type == 'error': probs.append('parse: ' + d.message); continue
                if d.type == 'qualified-rule':   # CSS nesting
                    decls([d]); continue
                if d.type != 'declaration': continue
                n = d.lower_name
                if not n.startswith('--') and not n.startswith('-') and n not in _CSS_PROPS: probs.append('property: unknown ' + n)
                vals = [t for t in d.value if t.type not in ('whitespace', 'comment')]
                if lengths.match(n) and any(t.type == 'number' and t.value != 0 for t in vals): probs.append('value: ' + n + ' number without a unit')

    for code in bs:
        if re.search(r'(?m)^\s*//', code): probs.append('parse: // comment')
        decls(tinycss2.parse_stylesheet(code, skip_comments=True, skip_whitespace=True))
        if not balance(re.sub(r'/\*[\s\S]*?\*/|"[^"\n]*"|\'[^\'\n]*\'', '', code), '{}()'): probs.append('parse: unbalanced braces')
    return probs


CHECKS = {'powershell': check_powershell, 'csharp': check_csharp, 'oracle': check_oracle, 'sql': check_sqlrun,
          'duckdb': check_sqlrun, 'javascript': None, 'powershell7': check_powershell7, 'vbscript': check_vbscript,
          'vba': check_vba, 'cmd': check_cmd, 'dax': check_dax, 'powerquery': check_m, 'qlik': check_qlik,
          'excel': check_excel, 'html': check_html, 'css': check_css}
# How each language is scored (shown in the report): real tools, or a rule list (no offline parser exists).
METHOD = {'powershell': 'tool', 'csharp': 'tool', 'oracle': 'tool+rules', 'sql': 'tool', 'duckdb': 'tool', 'javascript': 'tool+rules',
          'powershell7': 'tool', 'vbscript': 'tool', 'vba': 'tool+rules', 'cmd': 'rules', 'dax': 'rules', 'powerquery': 'rules',
          'qlik': 'rules', 'excel': 'tool+rules', 'html': 'tool', 'css': 'tool'}


def check(lang, text, prompt):
    return check_javascript(text, prompt) if lang == 'javascript' else CHECKS[lang](text)


# ---------- report ----------
def report(out, data):
    results, meta = data['results'], data['meta']
    langs = [l for l in LABEL if any(r['lang'] == l for r in results)]
    arms = [a for a in ARMS if any(r['arm'] == a for r in results)]
    E = html.escape
    def get(lang, arm, i): return next((r for r in results if r['lang'] == lang and r['arm'] == arm and r['i'] == i), None)
    def stats(lang, arm, which='final'):
        rs = [r for r in results if r['lang'] == lang and r['arm'] == arm and not r.get('error')]
        if not rs: return None
        errs = [len(r[which + '_problems']) for r in rs]
        return {'n': len(rs), 'clean': sum(1 for e in errs if e == 0), 'problems': sum(errs),
                'prompt': round(sum(max(0, r['ctx_used'] - r['tokens']) for r in rs) / len(rs)),
                'secs': round(sum(r['seconds'] for r in rs) / len(rs), 1)}
    tot = {a: {'n': 0, 'clean': 0, 'problems': 0} for a in arms}
    cards, rows, summary = [], [], []
    for lang in langs:
        s = {a: stats(lang, a) for a in arms}
        sd = {a: stats(lang, a, 'draft') for a in arms}
        for a in arms:
            if s[a]:
                for k in ('n', 'clean', 'problems'): tot[a][k] += s[a][k]
        bars = ''
        for a in arms:
            st = s[a]
            if not st: continue
            pct = 100 * st['clean'] / st['n']
            bars += (f'<div class="pair"><div class="lab">{ARM_LABEL[a]}</div><div class="bar"><div class="fill {a}" style="width:{pct:.0f}%"></div>'
                     f'<span>{pct:.0f}% clean · {st["clean"]}/{st["n"]} · {st["problems"]} problems</span></div></div>')
        sub = ' · '.join(f'{ARM_LABEL[a]}: drafts {sd[a]["clean"]}/{sd[a]["n"]} clean, prompt {s[a]["prompt"]:,} tok, {s[a]["secs"]} s'
                         for a in arms if s[a])
        best = max((a for a in arms if s[a]), key=lambda a: (s[a]['clean'], -s[a]['problems']))
        verdict = ''
        if s.get('new') and s.get('off'):
            d = s['new']['clean'] - s['off']['clean']
            verdict = ('<b class="good">New RAG helps: +%d clean</b>' % d if d > 0 else
                       '<b class="bad">New RAG hurts: %d clean</b>' % d if d < 0 else
                       '<b class="same">New RAG = RAG off</b>' + (' (both perfect)' if s['new']['clean'] == s['new']['n'] else ''))
        how = {'tool': 'scored by real tools', 'tool+rules': 'real tools + rules', 'rules': 'rule-based (no offline parser exists)'}[METHOD[lang]]
        cards.append(f'<section class="card"><h2>{LABEL[lang]} <small class="how">{how}</small></h2>{bars}<p class="v">{verdict}</p><p class="sub">{sub}</p></section>')
        summary.append((lang, s))
        for i in sorted({r['i'] for r in results if r['lang'] == lang}):
            any_r = next(get(lang, a, i) for a in arms if get(lang, a, i))
            cells = ''
            for a in arms:
                r = get(lang, a, i)
                if not r: cells += '<td>-</td>'; continue
                if r.get('error'): cells += '<td class="err">⚠ not scored: ' + E(r['error'][:90]) + '</td>'; continue
                p = r['final_problems']
                fixed = len(r['draft_problems']) > len(p)
                cells += ('<td class="ok">✓ clean' + (' <small>(fixed by check)</small>' if fixed else '') + '</td>') if not p else \
                         '<td class="bad">' + '<br>'.join(E(x[:110]) for x in p[:3]) + (f'<br>… +{len(p) - 3} more' if len(p) > 3 else '') + '</td>'
            rows.append(f'<tr><td class="lang">{E(LABEL[lang])}</td><td class="q">{E(any_r["prompt"])}</td>{cells}</tr>')
    heads = ''.join(f'<div class="big {a}"><b>{100 * tot[a]["clean"] / max(1, tot[a]["n"]):.0f}%</b><span>{ARM_LABEL[a]}: {tot[a]["clean"]}/{tot[a]["n"]} clean, {tot[a]["problems"]} problems</span></div>' for a in arms)
    th = ''.join(f'<th>{ARM_LABEL[a]}</th>' for a in arms)
    doc = f'''<!doctype html><html><head><meta charset="utf-8"><title>RAG evaluation</title><style>
:root{{--bg:#fbfaf7;--ink:#1d1d1f;--soft:#55555c;--line:#e4e1da;--off:#9aa0a6;--old:#7f9cc9;--new:#c8962b;--ok:#2f7d4f;--bad:#b3261e}}
body{{margin:0;background:var(--bg);color:var(--ink);font:16px/1.5 "Segoe UI",system-ui,sans-serif}}
main{{max-width:1400px;margin:0 auto;padding:40px 48px}}
h1{{font-size:34px;margin:0 0 4px;letter-spacing:-.5px}} .meta{{color:var(--soft);font-size:14px;margin-bottom:26px;max-width:1100px}}
.head{{display:grid;grid-template-columns:repeat({len(arms)},1fr);gap:16px;margin-bottom:26px}}
.big{{background:#fff;border:1px solid var(--line);border-left:6px solid var(--off);border-radius:10px;padding:16px 22px}} .big.old{{border-left-color:var(--old)}} .big.new{{border-left-color:var(--new)}}
.big b{{display:block;font-size:40px;line-height:1.1}} .big span{{color:var(--soft);font-size:14px}}
.cards{{display:grid;grid-template-columns:repeat(3,1fr);gap:16px;margin-bottom:32px}}
.card{{background:#fff;border:1px solid var(--line);border-radius:10px;padding:16px 20px}} .card h2{{margin:0 0 10px;font-size:19px}} .how{{font-size:12px;font-weight:400;color:var(--soft)}}
.pair{{display:grid;grid-template-columns:72px 1fr;align-items:center;gap:10px;margin:7px 0}} .lab{{font-size:13px;color:var(--soft);font-weight:600}}
.bar{{position:relative;height:28px;background:#f1efe9;border-radius:6px;overflow:hidden}} .fill{{height:100%}} .fill.off{{background:var(--off)}} .fill.old{{background:var(--old)}} .fill.new{{background:var(--new)}}
.bar span{{position:absolute;left:10px;top:4px;font-size:13.5px;font-weight:600}}
.v{{margin:10px 0 0;font-size:14px}} .good{{color:var(--ok)}} .bad{{color:var(--bad)}} .same{{color:var(--soft)}}
.sub{{color:var(--soft);font-size:12.5px;margin:8px 0 0}}
table{{width:100%;border-collapse:collapse;background:#fff;border:1px solid var(--line);font-size:13.5px}}
th,td{{text-align:left;vertical-align:top;padding:8px 11px;border-bottom:1px solid var(--line)}} th{{background:#f5f3ee;font-size:12.5px;text-transform:uppercase;letter-spacing:.5px;color:var(--soft)}}
td.ok{{color:var(--ok);font-weight:600}} td.ok small{{font-weight:400;color:var(--soft)}} td.bad{{color:var(--bad);font-family:Consolas,monospace;font-size:12px}} td.err{{color:#8a6d1f;font-size:12px}}
td.lang{{white-space:nowrap;color:var(--soft)}} td.q{{width:34%}} h3{{font-size:20px;margin:0 0 12px}}
</style></head><body><main>
<h1>Does the syntax library help? Old vs new sheets</h1>
<div class="meta">{E(meta.get("model", ""))} · greedy sampling · skills off · one fresh chat per prompt · same server code in every arm (deterministic checker included) · {E(meta.get("when", ""))}.
Scored with real tools: Windows PowerShell 5.1 parser + command/parameter lookup, csc.exe (.NET Framework 4.8), Oracle: sqlglot parser + dialect rules, SQL and DuckDB: executed in DuckDB against test tables and files, JavaScript: node --check + run-time mistakes.</div>
<div class="head">{heads}</div>
<div class="cards">{"".join(cards)}</div>
<h3>Every prompt (final replies)</h3>
<table><tr><th>Language</th><th>Prompt</th>{th}</tr>{"".join(rows)}</table>
</main></body></html>'''
    open(os.path.join(out, 'report.html'), 'w', encoding='utf-8').write(doc)
    return summary


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--base', default='http://127.0.0.1:8780')
    ap.add_argument('--arms', default='off,new')
    ap.add_argument('--langs', default=','.join(LABEL))
    ap.add_argument('--limit', type=int, default=0)
    ap.add_argument('--out')
    ap.add_argument('--report'); ap.add_argument('--rescore')
    a = ap.parse_args()
    if a.report or a.rescore:
        out = a.report or a.rescore
        data = json.load(open(os.path.join(out, 'results.json'), encoding='utf-8'))
        if a.rescore:
            for r in data['results']:
                r['draft_problems'] = check(r['lang'], r['draft'], r['prompt']); r['final_problems'] = check(r['lang'], r['final'], r['prompt'])
            json.dump(data, open(os.path.join(out, 'results.json'), 'w', encoding='utf-8'), indent=1)
        report(out, data); print('report', os.path.join(out, 'report.html')); return
    prompts = json.load(open(os.path.join(HERE, 'prompts.json'), encoding='utf-8'))
    info = json.loads(urllib.request.urlopen(a.base + '/api/info').read().decode())
    if not info.get('file'): sys.exit('Load a model first.')
    rag_before = info.get('rag', {}).get('enabled', True)
    out = a.out or os.path.join(HERE, 'results', datetime.now().strftime('%Y%m%d-%H%M%S'))
    os.makedirs(out, exist_ok=True)
    path = os.path.join(out, 'results.json')
    data = json.load(open(path, encoding='utf-8')) if os.path.exists(path) else {'meta': {}, 'results': []}
    data['meta'].update({'model': info['file'], 'when': datetime.now().strftime('%Y-%m-%d %H:%M')})
    done = {(r['arm'], r['lang'], r['i']) for r in data['results'] if not r.get('error')}
    data['results'] = [r for r in data['results'] if not r.get('error')]
    try:
        for arm in a.arms.split(','):
            post(a.base, '/api/rag', {'enabled': arm != 'off'})
            for lang in a.langs.split(','):
                ps = prompts[lang]['prompts'][: a.limit or None]
                for i, p in enumerate(ps):
                    if (arm, lang, i) in done: continue
                    # Guard: the model and the arm's retrieval setting must be what this run expects, before and after the
                    # answer (a second client on the same server once switched both mid-run); anything else aborts the run.
                    def state():
                        s = json.loads(urllib.request.urlopen(a.base + '/api/info').read().decode())
                        return s.get('file'), s.get('rag', {}).get('enabled')
                    f0, rag0 = state()
                    if f0 != info['file']: sys.exit('the loaded model changed to %s mid-run: another client is using the server' % f0)
                    if rag0 != (arm != 'off'): sys.exit('retrieval was switched %s mid-run: another client is using the server' % ('on' if rag0 else 'off'))
                    r = ask(a.base, prompts[lang]['prefix'] + p)
                    f1, rag1 = state()
                    if (f1, rag1) != (f0, rag0): sys.exit('the server state changed during an answer: another client is using the server')
                    if r['error'] and 'GPU' in r['error']:   # a driver reset: the server reloads the model; ask again once
                        time.sleep(20); r = ask(a.base, prompts[lang]['prefix'] + p)
                    # The arm's retrieval setting must hold (another client could have changed it): re-assert and re-ask.
                    if arm == 'off' and r['docs']:
                        post(a.base, '/api/rag', {'enabled': False}); r = ask(a.base, prompts[lang]['prefix'] + p)
                        if r['docs']: sys.exit('retrieval keeps switching back on: another client is changing it')
                    r.update({'arm': arm, 'lang': lang, 'i': i, 'prompt': p})
                    r['draft_problems'] = check(lang, r['draft'], p); r['final_problems'] = check(lang, r['final'], p)
                    data['results'].append(r)
                    print(f'[{arm}] {lang} {i + 1}/{len(ps)}: {len(r["final_problems"])} problems (draft {len(r["draft_problems"])}), '
                          f'{len(r["docs"])} sections, {r["seconds"]} s' + (' ERROR ' + r['error'] if r['error'] else ''), flush=True)
                    json.dump(data, open(path, 'w', encoding='utf-8'), indent=1)
    finally:
        post(a.base, '/api/rag', {'enabled': rag_before})
    report(out, data)
    print('report', os.path.join(out, 'report.html'))


if __name__ == '__main__':
    main()
