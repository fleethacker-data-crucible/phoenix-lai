"""Dev only: puts the hand-written Essentials card and Recipes (.claude\\tools\\sheet-parts\\<sheet>.md) into a syntax
sheet, in front of its rules checklist, and reports each Essentials card's size (it must stay one retrieval chunk,
at most Rag.MaxChunkChars = 2400 characters, because the server sends it with every request in that language).
  python .claude\\tools\\splice-sheet-parts.py                 every part that has a sheet
  python .claude\\tools\\splice-sheet-parts.py --sizes         only report the card sizes of the sheets as they are
A sheet still named <x>-syntax.md is renamed to <x>.md. Running it again replaces the previous Essentials/Recipes.
"""
import os, re, sys

root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
docs = os.path.join(root, 'docs', 'code-syntax')
parts = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'sheet-parts')


def card_size(text):
    m = re.search(r'(?ms)^## Essentials\n(.*?)(?=^## )', text)
    return len(m.group(1).strip()) if m else 0


if '--sizes' not in sys.argv:
    for f in sorted(os.listdir(parts)):
        name = f[:-3]
        old, new = os.path.join(docs, name + '-syntax.md'), os.path.join(docs, name + '.md')
        src = new if os.path.exists(new) else old
        if not os.path.exists(src):
            print('no sheet for', f); continue
        text = open(src, encoding='utf-8').read().replace('\r\n', '\n')
        part = open(os.path.join(parts, f), encoding='utf-8').read().strip() + '\n'
        # drop a previous Essentials / Recipes, then insert before the rules (the old checklist) or the first section
        text = re.sub(r'(?ms)^## (Essentials|Recipes)\n.*?(?=^## (?!Essentials|Recipes))', '', text)
        text = re.sub(r'(?m)^## Checklist: before you answer with', '## Rules: before you answer with', text)
        at = re.search(r'(?m)^## ', text)
        text = text[:at.start()] + part + '\n' + text[at.start():] if at else text + '\n' + part
        open(new, 'w', encoding='utf-8', newline='\n').write(text)
        if src == old: os.remove(old)
        print(os.path.basename(new), 'written')

for f in sorted(os.listdir(docs)):
    if f.endswith('.md'):
        n = card_size(open(os.path.join(docs, f), encoding='utf-8').read().replace('\r\n', '\n'))
        print('%-28s Essentials %5d chars%s' % (f, n, '  TOO BIG (over 2400)' if n > 2400 else '' if n else '  (none)'))
