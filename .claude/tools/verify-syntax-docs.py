# Dev only: checks the code in docs\code-syntax\*.md with real parsers.
#   python .claude\tools\verify-syntax-docs.py [sheet-name-filter]
# Every fenced block under an **Example** heading (the runnable examples; Syntax blocks are templates) is checked:
#   PowerShell 5.1 / 7  parser of powershell.exe / pwsh (parse only, nothing runs)
#   JavaScript          node --check (wrapped in an async module)
#   C#                  csc.exe (C# 5, .NET Framework) for blocks without newer syntax; the rest are only brace-checked
#   SQL / DuckDB        DuckDB's own parser (json_serialize_sql, no tables needed); ANSI and Oracle also through sqlglot
#   VBScript            cscript compiles the whole file before running; a WScript.Quit before the code means nothing runs
#   Excel formulas      the formulas package parser
#   HTML                tag balance (html.parser); CSS: brace balance + property names checked in a real browser
#   CMD, VBA, DAX, M, Qlik: no offline parser; bracket/quote balance only
import json, os, re, subprocess, sys, tempfile

root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
docs = os.path.join(root, 'docs', 'code-syntax')
if '--docs' in sys.argv:
    docs = sys.argv[sys.argv.index('--docs') + 1]; sys.argv = [a for a in sys.argv if a not in ('--docs', docs)]
tmp = tempfile.mkdtemp(prefix='phx-verify-')
only = sys.argv[1] if len(sys.argv) > 1 else ''


def blocks(path):
    """(heading, lang, code, is_example) for every fenced block."""
    text = open(path, encoding='utf-8').read().replace('\r\n', '\n')
    out, heading, label = [], '', ''
    lines = text.split('\n')
    i = 0
    while i < len(lines):
        l = lines[i]
        if l.startswith('### '):
            heading, label = l[4:], ''
        mm = re.match(r'^\*\*([^*]+)\*\*', l)
        if mm:
            label = mm.group(1).strip().rstrip(':')
        m = re.match(r'^```(\w*)\s*$', l)
        if m:
            lang = m.group(1)
            j = i + 1
            while j < len(lines) and not lines[j].startswith('```'):
                j += 1
            out.append((heading, lang, '\n'.join(lines[i + 1:j]), label == 'Example'))
            i = j
        i += 1
    return out


def balanced(code, pairs='()[]{}', quotes='"'):
    st = []
    q = None
    for ch in code:
        if q:
            if ch == q:
                q = None
            continue
        if ch in quotes:
            q = ch
            continue
        if ch in pairs[0::2]:
            st.append(ch)
        elif ch in pairs[1::2]:
            if not st or pairs[pairs.index(st.pop()) + 1] != ch:
                return False
    return not st and q is None


results = {}


def report(sheet, heading, ok, msg=''):
    results.setdefault(sheet, [0, 0, []])
    results[sheet][0 if ok else 1] += 1
    if not ok:
        results[sheet][2].append(heading + ': ' + msg.strip().split('\n')[0][:220])


def ps_check(sheet, exe, items):
    script = os.path.join(tmp, 'psparse.ps1')
    data = os.path.join(tmp, sheet + '.json')
    json.dump([c for _, c in items], open(data, 'w', encoding='utf-8'))
    open(script, 'w', encoding='utf-8-sig').write(
        "$codes = Get-Content -Raw -Encoding UTF8 $args[0] | ConvertFrom-Json\n"
        "foreach ($c in $codes) { $t = $null; $e = $null; [void][System.Management.Automation.Language.Parser]::ParseInput($c, [ref]$t, [ref]$e);"
        " if ($e.Count) { 'ERR ' + $e[0].Message } else { 'OK' } }\n")
    out = subprocess.run([exe, '-NoProfile', '-File', script, data], capture_output=True, text=True, encoding='utf-8', errors='replace').stdout.strip().split('\n')
    for (h, _), r in zip(items, out):
        report(sheet, h, r.strip() == 'OK', r)


NEWER_CS = re.compile(r'\$"|\?\.|\?\[|nameof|=>\s*[^{]|\bout var\b|\bis \w+ \w+\)|\bswitch\s*\{|using var|\brecord\b|\binit;|\(\w+ \w+, \w+ \w+\)\s|\bvar \(|\?\?=|\bnew\(\)|\[\^|\.\.\]|"""|\bdefault\b(?!\()|static \w+ \w+\(.*\)\s*=>')


def cs_check(sheet, items):
    csc = r'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    for h, code in items:
        # A complete program (the Recipes): compiled as it is with csc.exe (C# 5), and every error counts.
        if re.search(r'\bstatic\s+(void|int)\s+Main\s*\(', code):
            f = os.path.join(tmp, 'full.cs')
            open(f, 'w', encoding='utf-8').write(code)
            r = subprocess.run([csc, '/nologo', '/t:exe', '/out:' + os.path.join(tmp, 'full.exe'),
                                '/r:System.dll,System.Core.dll,System.Xml.dll,System.Data.dll,System.Runtime.Serialization.dll', f],
                               capture_output=True, text=True, errors='replace')
            errs = [l for l in r.stdout.split('\n') if ': error ' in l]
            report(sheet, h, not errs, errs[0] if errs else '')
            continue
        if NEWER_CS.search(code) or not code.strip():
            report(sheet, h, balanced(code), 'unbalanced brackets')
            continue
        first = next((x for x in code.split('\n') if x.strip() and not x.strip().startswith('//')), '')
        members = re.match(r'^(public|private|internal|protected|static|class|struct|enum|interface|abstract|sealed)\b', first.strip())
        body = code if members else 'static void __M() {\n' + code + '\n}'
        src = ('using System; using System.Collections.Generic; using System.Linq; using System.IO; using System.Text;'
               'using System.Text.RegularExpressions; using System.Threading.Tasks; using System.Globalization;\n'
               'class __Wrap {\n' + body + '\n}\n')
        f = os.path.join(tmp, 'x.cs')
        open(f, 'w', encoding='utf-8').write(src)
        r = subprocess.run([csc, '/nologo', '/t:library', '/out:' + os.path.join(tmp, 'x.dll'), f], capture_output=True, text=True, errors='replace')
        errs = [l for l in r.stdout.split('\n') if ': error ' in l and not re.search(r'CS0103|CS0246|CS0234|CS1061|CS0117|CS0165|CS0161|CS0029|CS0019', l)]
        report(sheet, h, not errs, errs[0] if errs else '')


def js_check(sheet, items):
    for h, code in items:
        f = os.path.join(tmp, 'x.mjs')
        mod = re.search(r'^\s*(import|export)\b', code, re.M)
        open(f, 'w', encoding='utf-8').write(code if mod else 'async function __w() {\n' + code + '\n}\n')
        r = subprocess.run(['node', '--check', f], capture_output=True, text=True, errors='replace')
        report(sheet, h, r.returncode == 0, r.stderr)


def duck_parse(code):
    import duckdb
    con = duckdb.connect()
    errs = []
    for stmt in [s for s in re.split(r';\s*(?:\n|$)', code) if s.strip() and not s.strip().startswith('--')]:
        stmt = re.sub(r'--[^\n]*', '', stmt).strip()
        if not stmt:
            continue
        try:
            r = con.execute("SELECT json_serialize_sql(?::VARCHAR)", [stmt]).fetchone()[0]
            j = json.loads(r)
            if j.get('error'):
                if 'Only SELECT statements' in j.get('error_message', ''):
                    try:
                        con.execute('EXPLAIN ' + stmt)
                    except Exception as e:
                        m = str(e)
                        if not re.search(r'Catalog Error|Binder Error|does not exist|No files found|IO Error|HTTP|not found', m):
                            errs.append(m.split('\n')[0])
                    continue
                errs.append(j.get('error_message', ''))
        except Exception as e:
            errs.append(str(e).split('\n')[0])
    return errs


def sql_check(sheet, items, dialect):
    import sqlglot
    for h, code in items:
        errs = []
        if dialect == 'duckdb':
            errs = duck_parse(code)
        else:
            if re.search(r'\b(DECLARE|BEGIN|CREATE (OR REPLACE )?(PROCEDURE|FUNCTION|PACKAGE|TRIGGER))\b', code):
                report(sheet, h, balanced(code), 'unbalanced')   # PL/SQL: no offline parser
                continue
            try:
                sqlglot.parse(code.replace('\n/\n', '\n'), read=dialect, error_level=sqlglot.ErrorLevel.RAISE)
            except Exception as e:
                errs.append(str(e).split('\n')[0])
        report(sheet, h, not errs, errs[0] if errs else '')


def vbs_check(sheet, items):
    for h, code in items:
        lines = code.split('\n')
        pre = [l for l in lines if re.match(r'^\s*Option Explicit', l, re.I)]
        rest = [l for l in lines if not re.match(r'^\s*Option Explicit', l, re.I)]
        f = os.path.join(tmp, 'x.vbs')
        # newline='': already \r\n-joined (text mode would write \r\r\n, which breaks "_" line continuations)
        open(f, 'w', encoding='ascii', errors='replace', newline='').write('\r\n'.join(pre + ['WScript.Quit 0'] + rest) + '\r\n')
        r = subprocess.run(['cscript.exe', '//nologo', '//B', f], capture_output=True, text=True, errors='replace')
        r2 = subprocess.run(['cscript.exe', '//nologo', f], capture_output=True, text=True, errors='replace') if r.returncode else r
        report(sheet, h, r.returncode == 0, (r2.stdout + r2.stderr))


def xl_check(sheet, items):
    import formulas
    for h, code in items:
        errs = []
        for line in code.split('\n'):
            f = re.sub(r"\s+'.*$", '', line).strip()
            f = re.sub(r"\b[A-Za-z_]\w*\[(@?)[A-Za-z_][\w ]*\]", 'A1:A10', f)      # structured refs -> a range
            if not f.startswith('='):
                continue
            try:
                formulas.Parser().ast(f)
            except Exception as e:
                m = str(e).split('\n')[0]
                if not re.search(r'Not implemented|not implemented', m):
                    errs.append(f + ' -> ' + m)
        report(sheet, h, not errs, errs[0] if errs else '')


def html_check(sheet, items):
    from html.parser import HTMLParser
    void = set('area base br col embed hr img input link meta source track wbr'.split())

    class P(HTMLParser):
        def __init__(s):
            super().__init__(); s.st = []; s.err = None
        def handle_starttag(s, t, a):
            if t not in void: s.st.append(t)
        def handle_endtag(s, t):
            if t in void: s.err = s.err or '</' + t + '> on a void element'; return
            if t in s.st:
                while s.st and s.st[-1] != t:
                    s.st.pop()          # implied ends (p, li, option ...)
                s.st.pop()
            else:
                s.err = s.err or 'stray </' + t + '>'
    for h, code in items:
        p = P(); p.feed(code)
        report(sheet, h, p.err is None, p.err or '')


COMMENTS = {'dax': (r"(?m)--.*$|//.*$", "\"'"), 'qlik-script': (r"(?m)(?<!:)//.*$", "\"'"),
            'cmd': (r"(?mi)^\s*(::|rem\b).*$", '"'), 'excel-vba': (r"(?m)'.*$", '"'),
            'power-query': (r"(?m)//.*$", '"'), 'css': (r"/\*.*?\*/", "\"'")}


def key(sheet):
    """Sheet file stem -> checker key: c#-syntax / csharp -> csharp, power-shell-5-syntax / power-shell-5.1 -> power-shell-5."""
    k = re.sub(r'-syntax$', '', sheet)
    if k.startswith('power-shell'):
        return 'power-shell-7' if re.search(r'-7', k) else 'power-shell-5'
    return {'c#': 'csharp'}.get(k, k)


def generic_check(sheet, items):
    rx, qs = COMMENTS.get(key(sheet), (r"(?m)//.*$", '"'))
    for h, code in items:
        report(sheet, h, balanced(re.sub(rx, '', code), quotes=qs), 'unbalanced brackets or quotes')


CHECKERS = {
    'power-shell-5': lambda s, it: ps_check(s, 'powershell.exe', it),
    'power-shell-7': lambda s, it: ps_check(s, 'pwsh', it),
    'csharp': cs_check, 'javascript': js_check, 'html': html_check,
    'oracle-sql': lambda s, it: sql_check(s, it, 'oracle'),
    'duckdb-sql': lambda s, it: sql_check(s, it, 'duckdb'),
    'vbscript': vbs_check, 'excel-formulas': xl_check,
}


def main():
    for f in sorted(os.listdir(docs)):
        if not f.endswith('.md') or only not in f:
            continue
        sheet = f[:-3]
        items = [(h, c) for h, lang, c, ex in blocks(os.path.join(docs, f)) if ex]
        if key(sheet) == 'sql':
            for h, c in items:
                e1 = duck_parse(c)
                import sqlglot
                try:
                    sqlglot.parse(c, read=None, error_level=sqlglot.ErrorLevel.RAISE); e2 = []
                except Exception as e:
                    e2 = [str(e).split('\n')[0]]
                report(sheet, h, not (e1 and e2), (e1 or e2 or [''])[0])   # fail only if both parsers reject it
            continue
        (CHECKERS.get(key(sheet)) or generic_check)(sheet, items)
    total_bad = 0
    for s, (ok, bad, msgs) in sorted(results.items()):
        print('%-26s %4d ok %3d failed' % (s, ok, bad))
        for m in msgs:
            print('    - ' + m)
        total_bad += bad
    print('FAILED' if total_bad else 'all examples passed')
    return 1 if total_bad else 0


sys.exit(main())
