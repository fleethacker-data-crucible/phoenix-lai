"""How long each rag-eval validator takes on a recorded reply (dev-only).
  python -X utf8 .claude\\claude_tests\\rag-eval\\validator-timing.py results\\final-e4b
"""
import json, os, sys, time

here = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(here, 'rag-eval.py'), encoding='utf-8').read().replace("\nif __name__ == '__main__':\n    main()", '')
g = {'__file__': os.path.join(here, 'rag-eval.py')}
exec(compile(src, 'rag-eval.py', 'exec'), g)
data = json.load(open(os.path.join(here, sys.argv[1], 'results.json'), encoding='utf-8'))
seen = set()
for r in data['results']:
    if r['lang'] in seen: continue
    seen.add(r['lang'])
    t = time.time(); g['check'](r['lang'], r['final'], r['prompt']); a = time.time() - t
    t = time.time(); g['check'](r['lang'], r['final'], r['prompt']); b = time.time() - t
    print('%-12s first %.1f s, again %.1f s' % (r['lang'], a, b))
