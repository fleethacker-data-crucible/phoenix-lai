"""Dev only: runs every powershell block of a Markdown file through the rag-eval PowerShell checker (5.1 parser plus
command and parameter lookup in the installed shell). Stricter than verify-syntax-docs.py, which only parses.
  python .claude\\tools\\check-md-ps.py <file.md> [powershell.exe | pwsh]
"""
import json, os, re, subprocess, sys, tempfile

here = os.path.dirname(os.path.abspath(__file__))
checker = os.path.join(os.path.dirname(here), 'claude_tests', 'rag-eval', 'check-ps.ps1')
text = open(sys.argv[1], encoding='utf-8').read().replace('\r\n', '\n')
exe = sys.argv[2] if len(sys.argv) > 2 else 'powershell.exe'
heading, bad, n = '', 0, 0
for m in re.finditer(r'^### (.*)$|^```powershell\n([\s\S]*?)^```', text, re.M):
    if m.group(1): heading = m.group(1); continue
    n += 1
    f = tempfile.NamedTemporaryFile('w', suffix='.ps1', delete=False, encoding='utf-8-sig'); f.write(m.group(2)); f.close()
    out = subprocess.run([exe, '-NoProfile', '-File', checker, '-Path', f.name], capture_output=True, text=True).stdout.strip().splitlines()
    os.unlink(f.name)
    j = json.loads(out[-1]) if out else {'parse': ['checker failed']}
    probs = [x for k in ('parse', 'commands', 'params') for x in (j.get(k) or []) if x]
    if probs:
        bad += 1
        print('FAIL', heading or '(top)', '|', ' ; '.join(map(str, probs)))
print(n, 'blocks,', bad, 'with problems')
