"""Self-test of the rag-eval validators: every verified recipe of the matching sheet must score clean (no false alarms),
and a set of known-bad snippets must be caught. Dev-only.
  python -X utf8 .claude\\claude_tests\\rag-eval\\validator-selftest.py
"""
import importlib.util, os, re, sys

here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location('ev', os.path.join(here, 'rag-eval.py'))
sys.argv = ['x', '--report', here]   # keep main() from running
ev = importlib.util.module_from_spec(spec)
src = open(os.path.join(here, 'rag-eval.py'), encoding='utf-8').read().replace("\nif __name__ == '__main__':\n    main()", '')
exec(compile(src, 'rag-eval.py', 'exec'), ev.__dict__)
parts = os.path.join(os.path.dirname(os.path.dirname(here)), 'tools', 'sheet-parts')
tools = os.path.join(os.path.dirname(os.path.dirname(here)), 'tools')
SHEET = {'vbscript': ('vbscript.md', 'vbscript'), 'vba': ('excel-vba.md', 'vba'), 'cmd': ('cmd.md', 'bat'), 'dax': ('dax.md', 'dax'),
         'powerquery': ('power-query.md', 'powerquery'), 'qlik': ('qlik-script.md', 'qlik'), 'excel': ('excel-formulas.md', 'excel'),
         'html': ('html.md', 'html'), 'css': ('css.md', 'css'), 'powershell7': ('../ps-essentials-7.md', 'powershell')}
BAD = {'vbscript': 'Dim x As String\nx = 1', 'vba': 'Sub A()\nIf x = 1 Then\nMsgBox 1\nEnd Sub', 'cmd': 'for %f in (*.txt) do echo %f',
       'dax': 'Total = SUM(Sales[Qty] * Sales[Price])', 'powerquery': 'let\n  A = Table.SelectRows(S, [x] > 1),\nin\n  A',
       'qlik': 'T:\nLOAD * FROM x.csv\nU:\nLOAD 1 AUTOGENERATE 1;', 'excel': '=VLOOKUP(A2, B:C, 2)', 'html': '<div><p>x</div></span>',
       'css': '.a { text-color: red; width: 10; }', 'powershell7': 'Get-ChildItem -Pth .'}
fa = miss = 0
for lang, (sheet, tag) in SHEET.items():
    text = open(os.path.join(parts if not sheet.startswith('..') else tools, sheet.replace('../', '')), encoding='utf-8').read()
    n = 0
    for m in re.finditer(r'(?m)^### (.*)$|^```\w*\n([\s\S]*?)^```', text):
        if m.group(1) or not m.group(2): continue
        n += 1
        p = ev.check(lang, '```' + tag + '\n' + m.group(2) + '```', '')
        if p: fa += 1; print('FALSE ALARM', lang, '|', p[:3])
    p = ev.check(lang, '```' + tag + '\n' + BAD[lang] + '\n```', '')
    if not p: miss += 1; print('MISSED BAD', lang)
    print('%-12s %2d recipes, bad snippet -> %s' % (lang, n, p[:2]))
print('false alarms', fa, 'missed bad', miss)
