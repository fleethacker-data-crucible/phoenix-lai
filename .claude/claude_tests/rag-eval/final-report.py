"""Final RAG report: both models, three arms (RAG off / old sheets / new sheets), every language, one page.
  python -X utf8 .claude\\claude_tests\\rag-eval\\final-report.py results\\uniform-e4b results\\uniform-e2b > out.html
Also writes results\\FINAL-REPORT.html. Dev-only.
"""
import html, json, os, sys

here = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(here, 'rag-eval.py'), encoding='utf-8').read().replace("\nif __name__ == '__main__':\n    main()", '')
g = {'__file__': os.path.join(here, 'rag-eval.py')}
exec(compile(src, 'rag-eval.py', 'exec'), g)
LABEL, METHOD = g['LABEL'], g['METHOD']
ARMS = ('off', 'old', 'new')
NAMES = {'off': 'RAG off', 'old': 'Old sheets', 'new': 'New sheets'}
E = html.escape


def load(d):
    data = json.load(open(os.path.join(here, d, 'results.json'), encoding='utf-8'))
    return data['meta'], data['results']


def stats(rs, lang, arm, which='final'):
    x = [r for r in rs if r['lang'] == lang and r['arm'] == arm and not r.get('error')]
    if not x: return None
    clean = sum(1 for r in x if not r[which + '_problems'])
    return {'n': len(x), 'clean': clean, 'pct': 100.0 * clean / len(x), 'problems': sum(len(r[which + '_problems']) for r in x),
            'tok': round(sum(max(0, r['ctx_used'] - r['tokens']) for r in x) / len(x)), 'secs': sum(r['seconds'] for r in x) / len(x),
            'drafts': sum(1 for r in x if not r['draft_problems'])}


def cell(st, best):
    if not st: return '<td class="na">-</td>'
    cls = 'best' if best else ''
    return f'<td class="{cls}"><b>{st["pct"]:.0f}%</b> <small>{st["clean"]}/{st["n"]}</small><div class="bar"><i style="width:{st["pct"]:.0f}%"></i></div></td>'


def model_section(meta, rs):
    langs = [l for l in LABEL if any(r['lang'] == l for r in rs)]
    tot = {a: [0, 0, 0, 0] for a in ARMS}   # clean, n, drafts clean, tokens sum
    rows = ''
    helps = hurts = ties = 0
    for l in langs:
        st = {a: stats(rs, l, a) for a in ARMS}
        for a in ARMS:
            if st[a]:
                tot[a][0] += st[a]['clean']; tot[a][1] += st[a]['n']; tot[a][2] += st[a]['drafts']; tot[a][3] += st[a]['tok'] * st[a]['n']
        top = max((st[a]['clean'] for a in ARMS if st[a]), default=0)
        d = (st['new']['clean'] - st['off']['clean']) if st['new'] and st['off'] else 0
        if d > 0: helps += 1
        elif d < 0: hurts += 1
        else: ties += 1
        eff = ('<span class="up">+%d</span>' % d) if d > 0 else ('<span class="down">%d</span>' % d) if d < 0 else '<span class="eq">=</span>'
        how = {'tool': 'real tools', 'tool+rules': 'tools + rules', 'rules': 'rules only'}[METHOD[l]]
        dr = ' / '.join(f'{st[a]["drafts"]}' if st[a] else '-' for a in ARMS)
        rows += (f'<tr><td class="lang">{E(LABEL[l])}<small>{how}</small></td>' + ''.join(cell(st[a], st[a] and st[a]['clean'] == top) for a in ARMS) +
                 f'<td class="eff">{eff}</td><td class="dr">{dr}</td><td class="tk">{st["off"]["tok"] if st["off"] else "-"} → {st["new"]["tok"] if st["new"] else "-"}</td></tr>')
    heads = ''.join(f'<div class="big {a}"><b>{100.0 * tot[a][0] / max(1, tot[a][1]):.1f}%</b><span>{NAMES[a]}: {tot[a][0]}/{tot[a][1]} clean · first drafts clean {tot[a][2]}/{tot[a][1]}</span></div>' for a in ARMS)
    return f'''<section class="model"><h2>{E(meta.get("model", ""))}</h2><div class="head">{heads}</div>
<p class="verdict">New sheets vs RAG off: better in <b>{helps}</b> languages, equal in <b>{ties}</b>, worse in <b>{hurts}</b>.</p>
<table><tr><th>Language</th><th>RAG off</th><th>Old sheets</th><th>New sheets</th><th>New vs off</th><th>Clean first drafts<br><small>off / old / new</small></th><th>Prompt tokens<br><small>off → new</small></th></tr>{rows}</table></section>'''


def main():
    sections = ''
    for d in sys.argv[1:]:
        meta, rs = load(d)
        sections += model_section(meta, rs)
    doc = f'''<!doctype html><html><head><meta charset="utf-8"><title>Final RAG evaluation</title><style>
:root{{--bg:#fbfaf7;--ink:#1d1d1f;--soft:#55555c;--line:#e4e1da;--off:#9aa0a6;--old:#7f9cc9;--new:#c8962b;--ok:#2f7d4f;--bad:#b3261e}}
body{{margin:0;background:var(--bg);color:var(--ink);font:16px/1.5 "Segoe UI",system-ui,sans-serif}} main{{max-width:1300px;margin:0 auto;padding:40px 48px}}
h1{{font-size:34px;margin:0 0 6px}} .meta{{color:var(--soft);font-size:14.5px;max-width:1150px;margin-bottom:10px}}
.model{{margin-top:34px}} .model h2{{font-size:22px;margin:0 0 12px;border-bottom:2px solid var(--line);padding-bottom:6px}}
.head{{display:grid;grid-template-columns:repeat(3,1fr);gap:14px;margin-bottom:10px}}
.big{{background:#fff;border:1px solid var(--line);border-left:6px solid var(--off);border-radius:10px;padding:14px 20px}} .big.old{{border-left-color:var(--old)}} .big.new{{border-left-color:var(--new)}}
.big b{{display:block;font-size:36px;line-height:1.1}} .big span{{color:var(--soft);font-size:13.5px}}
.verdict{{font-size:16px;margin:10px 0 14px}}
table{{width:100%;border-collapse:collapse;background:#fff;border:1px solid var(--line);font-size:14px}}
th,td{{padding:8px 12px;border-bottom:1px solid var(--line);text-align:left;vertical-align:middle}} th{{background:#f5f3ee;font-size:12.5px;text-transform:uppercase;letter-spacing:.4px;color:var(--soft)}}
td.lang{{font-weight:600}} td.lang small{{display:block;font-weight:400;color:var(--soft);font-size:12px}}
td small{{color:var(--soft)}} .bar{{height:6px;background:#f1efe9;border-radius:3px;margin-top:4px;overflow:hidden}} .bar i{{display:block;height:100%;background:var(--off)}}
td.best .bar i{{background:var(--new)}} td.best b{{color:#7a5a14}}
.eff{{font-size:18px;font-weight:700;text-align:center}} .up{{color:var(--ok)}} .down{{color:var(--bad)}} .eq{{color:var(--soft)}}
.dr,.tk{{color:var(--soft);font-size:13px}}
</style></head><body><main>
<h1>Final evaluation: does the syntax library make the models write correct code?</h1>
<div class="meta">Same frozen server code in every arm (deterministic code checker, automatic C# 5 rewrites and the fix loop are on in all three); only the documents differ:
<b>RAG off</b> (no library) · <b>Old sheets</b> (the library before this work, retrieved with today's code) · <b>New sheets</b> (rewritten: Essentials card + task recipes + version-aware retrieval).
Greedy sampling, skills off, one fresh chat per prompt, 178 prompts per arm across 16 languages. Every final reply is scored with real tools where they exist
(Windows PowerShell 5.1 / 7 parsers + command and parameter lookup, csc.exe for .NET Framework 4.8, DuckDB executing the SQL against test tables, sqlglot, node --check,
cscript compile, the Excel formula parser, html5lib, tinycss2 + Chrome's CSS property list); CMD, DAX, Power Query M and Qlik have no offline parser and are scored by rule lists.
A highlighted cell is the best arm for that language.</div>
{sections}
</main></body></html>'''
    out = os.path.join(here, 'results', 'FINAL-REPORT.html')
    open(out, 'w', encoding='utf-8').write(doc)
    print(out)


main()
