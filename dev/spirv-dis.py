# DEV MACHINE ONLY. Disassembles a SPIR-V word dump (space-separated uint32s) using the Khronos grammar.
# AMD's driver crashes on malformed SPIR-V instead of reporting it, so check new shaders with this.
#   python dev/spirv-dis.py words.txt
import json, os, sys

here = os.path.dirname(os.path.abspath(__file__))
g = json.load(open(os.path.join(here, 'ref', 'spirv', 'spirv.core.grammar.json')))
ops = {i['opcode']: i for i in g['instructions']}

w = [int(x) for x in open(sys.argv[1]).read().split()]
print('magic %08x  version %08x  bound %d' % (w[0], w[1], w[3]))
i = 5
while i < len(w):
    n, op = w[i] >> 16, w[i] & 0xFFFF
    info = ops.get(op)
    if n == 0 or info is None:
        print('BAD instruction at word %d: opcode %d, length %d' % (i, op, n))
        sys.exit(1)
    kinds = [o['kind'] for o in info.get('operands', [])]
    args = w[i + 1:i + n]
    text = []
    for k, a in zip(kinds + ['…'] * len(args), args):
        text.append(('%' + str(a)) if k in ('IdResult', 'IdResultType', 'IdRef') else str(a))
    print('%-24s %s' % (info['opname'], ' '.join(text)))
    i += n
