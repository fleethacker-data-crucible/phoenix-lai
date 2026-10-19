# Dev only: rebuilds docs\code-syntax\power-shell-5.1.md and power-shell-7.md in the Phoenix syntax-sheet format.
#   python .claude\tools\make-ps-syntax.py <PowerShell-Docs clone>\reference <ps5-params.json> <ps7-params.json>
# Command list, syntax lines, descriptions and examples come from ps-source\ (the previous sheets, (read from the shells themselves);
# each parameter's type, mandatory/position/pipeline/aliases/valid values from dump-ps-params.ps1 (the installed shell);
# a one-sentence description of each parameter, and each command's output types, from Microsoft's PowerShell-Docs
# (CC BY 4.0). The hand-written language part is .claude\tools\ps-language.md; ps-essentials-{5,7}.md replace its checklist.
import json, os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from importlib import import_module
FALLBACK = import_module('ps-param-fallback').FALLBACK

root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
docs = os.path.join(root, 'docs', 'code-syntax')
tools = os.path.dirname(os.path.abspath(__file__))
ref, p5, p7 = sys.argv[1], sys.argv[2], sys.argv[3]


def load_entries(path):
    text = open(path, encoding='utf-8-sig').read().replace('\r\n', '\n')
    out = []
    module = None
    for block in re.split(r'\n(?=## |### )', text):
        if block.startswith('## '):
            module = block[3:].split('\n')[0].strip()
            continue
        if not block.startswith('### '):
            continue
        name = block[4:].split('\n')[0].strip()
        meta = re.search(r'\n(PowerShell [^\n]*)\n', block)
        body = block.split('\n', 1)[1].strip()
        parts = re.split(r'\n(?:Syntax|Example|Examples):\n', '\n' + body.split('\n', 2)[2] if meta else body)
        desc = parts[0].strip()
        syn = re.search(r'Syntax:\n\n```powershell\n(.*?)\n```', block, re.S)
        ex = re.search(r'Examples?:\n\n```powershell\n(.*?)\n```', block, re.S)
        out.append(dict(name=name, module=module, meta=meta.group(1) if meta else '', desc=desc,
                        syntax=syn.group(1).strip() if syn else name, example=ex.group(1).strip() if ex else ''))
    return out


def clean(s):
    s = re.sub(r'\[([^\]]+)\]\([^)]*\)', r'\1', s)
    s = s.replace('**', '').replace('`', '')
    s = re.sub(r'\s+', ' ', s).strip()
    return s


def first_sentence(par):
    par = clean(par)
    m = re.match(r'(.+?[.!?])(\s|$)', par)
    s = m.group(1) if m else par
    if len(s) < 70:
        m2 = re.match(r'(.+?[.!?]\s+.+?[.!?])(\s|$)', par)
        if m2 and len(m2.group(1)) < 260:
            s = m2.group(1)
    return s[:300]


doc_index = {}


def index_docs(ver):
    d = {}
    base = os.path.join(ref, ver)
    for dp, dn, fn in os.walk(base):
        for f in fn:
            if f.endswith('.md') and not f.startswith('about_'):
                d.setdefault(f[:-3].lower(), os.path.join(dp, f))
    return d


def doc_info(ver, name):
    path = doc_index[ver].get(name.lower())
    if not path:
        return {}, []
    t = open(path, encoding='utf-8-sig').read().replace('\r\n', '\n')
    params = {}
    pm = re.search(r'\n## PARAMETERS\n(.*?)(\n## (?!#))', t, re.S)
    if pm:
        for blk in re.split(r'\n### ', '\n' + pm.group(1)):
            if not blk.startswith('-'):
                continue
            pname = blk.split('\n')[0].strip()[1:]
            body = blk.split('\n', 1)[1] if '\n' in blk else ''
            prose = body.split('```')[0]
            paras = [p for p in re.split(r'\n\s*\n', prose) if p.strip() and not p.strip().startswith('>')]
            dv = re.search(r'Default value:\s*(.*)', body)
            wc = re.search(r'Accept wildcard characters:\s*True', body)
            params[pname.lower()] = dict(desc=first_sentence(paras[0]) if paras else '',
                                         default=(dv.group(1).strip() if dv else ''), wildcard=bool(wc))
    outs = []
    om = re.search(r'\n## OUTPUTS\n(.*?)(\n## (?!#)|$)', t, re.S)
    if om:
        for h in re.findall(r'\n### (.+)', '\n' + om.group(1)):
            h = clean(h)
            outs.append(h.split('.')[-1] if h.startswith('System.') or h.startswith('Microsoft.') else h)
    return params, outs


def arg_lines(entry, meta, dinfo):
    lines = []
    for p in meta.get('params', []):
        d = dinfo.get(p['name'].lower(), {})
        bits = []
        t = p['type']
        bits.append('switch' if p['switch'] or t == 'SwitchParameter' else '<' + t + '>')
        if p['mandatory']:
            bits.append('**required**')
        if p['position'] >= 0:
            bits.append('position ' + str(p['position']))
        if p['pipeline'] or p['byName']:
            bits.append('pipeline ' + ('by value' if p['pipeline'] else '') + (' and ' if p['pipeline'] and p['byName'] else '') + ('by property name' if p['byName'] else ''))
        al = [a for a in (p.get('aliases') or []) if a]
        if al:
            bits.append('alias ' + ', '.join('-' + a for a in al))
        vs = p.get('validate') or []
        if vs:
            bits.append('values: ' + ', '.join(str(v) for v in vs[:14]) + (' …' if len(vs) > 14 else ''))
        dv = d.get('default', '')
        if dv and dv.lower() not in ('none', 'false', '', 'true') and not p['switch']:
            bits.append('default ' + clean(dv))
        if d.get('wildcard'):
            bits.append('wildcards')
        desc = d.get('desc', '')
        if not desc and p['name'] in FALLBACK:
            noun = re.sub(r'(?<=[a-z])(?=[A-Z])', ' ', entry['name'].split('-')[-1]).lower()
            desc = FALLBACK[p['name']].replace('{noun}', noun)
        lines.append('- `-' + p['name'] + '` ' + ' · '.join(bits) + (' — ' + desc if desc else ''))
    return lines


def render(entries, params, ver, label, shell, lang_file, out_name, intro):
    o = []
    o.append('# ' + label + ' syntax reference (Phoenix)\n')
    o.append(intro)
    lang = open(os.path.join(tools, 'ps-language.md'), encoding='utf-8').read().replace('{PS}', label)
    for k, v in EDITION[lang_file].items():
        lang = lang.replace('{' + k + '}', v)
    # The hand-written Essentials card and task Recipes (ps-essentials-5.md / -7.md) replace the checklist: the card is
    # sent with every request about this shell, the recipes match requests worded as tasks.
    ess = os.path.join(tools, 'ps-essentials-' + lang_file + '.md')
    if os.path.exists(ess):
        card = open(ess, encoding='utf-8').read().strip() + '\n'
        lang = re.sub(r'(?s)^## Checklist.*?(?=\n## )', lambda m: card, lang.strip(), count=1)
    o.append(lang.strip() + '\n')
    mods = []
    for e in entries:
        if e['module'] not in mods:
            mods.append(e['module'])
    for mod in mods:
        o.append('## Commands: ' + mod + '\n')
        for e in [x for x in entries if x['module'] == mod]:
            kind = 'function' if '· function' in e['meta'] else 'cmdlet'
            al = re.search(r'aliases: (.*)$', e['meta'])
            o.append('### ' + e['name'] + ' (' + label + ' ' + kind + ')\n')
            o.append('Module ' + mod + (' · aliases: ' + al.group(1) if al else '') + '\n')
            o.append(e['desc'] + '\n')
            o.append('**Syntax** (one line per parameter set)\n\n```powershell\n' + e['syntax'] + '\n```\n')
            meta = params.get(e['name'], {})
            dinfo, outs = doc_info(ver, e['name'])
            al = arg_lines(e, meta, dinfo)
            if al:
                o.append('**Arguments**\n\n' + '\n'.join(al) + '\n')
            if outs:
                o.append('**Output:** ' + '; '.join(dict.fromkeys(outs)) + '\n')
            if e['example']:
                o.append('**Example**\n\n```powershell\n' + e['example'] + '\n```\n')
    path = os.path.join(docs, out_name)
    open(path, 'w', encoding='utf-8', newline='\n').write('\n'.join(o))
    print(out_name, len(entries), 'commands', os.path.getsize(path), 'bytes')


EDITION = {
    '5': {
        'EDITION_CHECKS': (
            "- This is Windows PowerShell 5.1. It has none of the 7-only syntax: no ternary `a ? b : c`, `??`, `??=`, `?.`, `&&` or `||` "
            "between commands, `ForEach-Object -Parallel`, `` `e `` or `` `u{} `` escapes, `ConvertFrom-Json -AsHashtable`, `Get-Content -AsByteStream`, "
            "`$IsWindows`, or `Join-Path` with more than one child path. Use `if/else`, `if ($LASTEXITCODE -eq 0)`, `-Encoding Byte`.\n"
            "- File encodings in 5.1: `Set-Content` and `Add-Content` write ANSI (the system code page); `Out-File` and `>` write UTF-16 LE. "
            "Pass `-Encoding UTF8` (which writes a BOM) when another program reads the file."),
        'EDITION_ESCAPES': '',
        'EDITION_OPERATORS': "# 5.1 has no ternary (?:), ??, ??=, ?. or && / ||: use if/else and $?",
        'EDITION_FILES': (
            "- Encoding in 5.1: `-Encoding UTF8` writes a BOM. For UTF-8 without a BOM, use "
            "`[IO.File]::WriteAllText($full, $text, (New-Object System.Text.UTF8Encoding $false))`. Read raw bytes with `-Encoding Byte`."),
    },
    '7': {
        'EDITION_CHECKS': (
            "- PowerShell 7 adds syntax that Windows PowerShell 5.1 rejects: the ternary `a ? b : c`, `??`, `??=`, `?.`, and `&&` / `||` "
            "between commands, `ForEach-Object -Parallel`. Avoid these when the script must also run on 5.1 (`powershell.exe`).\n"
            "- File encoding in 7 is UTF-8 without a BOM by default for every cmdlet. `-Encoding Byte` is gone: read bytes with `-AsByteStream`.\n"
            "- On Linux and macOS, paths and command names are case-sensitive and the Windows-only modules are missing. Check `$IsWindows`."),
        'EDITION_ESCAPES': ', `` `e `` escape character (ESC), `` `u{263A} `` Unicode code point',
        'EDITION_OPERATORS': (
            "$ok ? 'yes' : 'no'                  # ternary (7 only)\n"
            "$x ?? 'default'   $x ??= 'default'  # null-coalescing / assign if null (7 only)\n"
            "${obj}?.Prop   ${arr}?[0]           # null-conditional access (7 only; braces needed around the name)\n"
            "cmd1 && cmd2     cmd1 || cmd2       # pipeline chains: run cmd2 if cmd1 succeeded / failed (7 only)\n"
            "<command> &                         # run as a background job (7 only)"),
        'EDITION_FILES': (
            "- Encoding in 7: the default is UTF-8 without a BOM; `-Encoding utf8BOM` adds one. Read bytes with `Get-Content -AsByteStream`.\n\n"
            "### ForEach-Object -Parallel (PowerShell 7 syntax)\n\n"
            "Runs the script block for each input object on parallel runspaces (threads). PowerShell 7 only.\n\n"
            "**Syntax**\n\n```powershell\n<input> | ForEach-Object -Parallel { <statements using $_ and $using:var> } [-ThrottleLimit <int>] [-TimeoutSeconds <int>] [-AsJob]\n```\n\n"
            "**Arguments**\n\n"
            "- `-Parallel <scriptblock>`: the code, run in a separate runspace per item. Caller variables are visible only as `$using:name`.\n"
            "- `-ThrottleLimit <int>`: the maximum number of items running at once (default 5).\n"
            "- `-TimeoutSeconds <int>`: stops waiting after this many seconds.\n"
            "- `-AsJob`: returns a job instead of the results.\n\n"
            "**Example**\n\n```powershell\n$servers = 'a', 'b', 'c'\n$timeout = 2\n$servers | ForEach-Object -Parallel {\n"
            "    [pscustomobject]@{ Server = $_; Up = Test-Connection -TargetName $_ -Count 1 -TimeoutSeconds $using:timeout -Quiet }\n} -ThrottleLimit 10\n```\n\n"
            "**Check**\n\n- Functions and variables of the caller are not defined inside the block. Pass values with `$using:` and define helper functions inside the block.\n"
            "- `$using:` values are copies. Assigning to `$using:x` is an error, so collect results from the output.\n"
            "- Output order is not guaranteed."),
    },
}

COMMON = ('`[<CommonParameters>]` = -Verbose, -Debug, -ErrorAction, -WarningAction, -InformationAction, -ErrorVariable, '
          '-WarningVariable, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable.')


def intro(label, exe, src_note):
    return (f'For Phoenix models writing and checking {label} code. Part 1 is the language (variables, operators, flow, '
            f'functions, errors, strings, the pitfalls that break scripts). Part 2 is every command that ships with {label} '
            f'({exe}): what it does, its exact syntax, every argument, its output and an example.\n\n'
            f'**Reading the syntax:** `[-Name]` optional, `<Type>` value type, `[[-Name] <Type>]` optional positional, '
            f'`{{A | B}}` pick one, {COMMON} Each syntax line is one parameter set: arguments from two different sets cannot be '
            f'combined.\n\n'
            f'**Reading the arguments:** `<Type>` the value type, `switch` takes no value (`-Force`, or `-Force:$false`), '
            f'**required** must be given (or the shell prompts), `position N` can be passed without its name in that place (0 = first), '
            f'`pipeline` accepts input from `|`, `values:` the only accepted values, `wildcards` accepts `*` and `?`.\n\n'
            f'**Sources:** {src_note}\n')


def main():
    doc_index['5.1'] = index_docs('5.1')
    doc_index['7.6'] = index_docs('7.6')
    e5 = load_entries(os.path.join(tools, 'ps-source', 'power-shell-5.md'))
    e7 = load_entries(os.path.join(tools, 'ps-source', 'power-shell-7.md'))
    m5 = json.load(open(p5, encoding='utf-8-sig'))
    m7 = json.load(open(p7, encoding='utf-8-sig'))
    render(e5, m5, '5.1', 'PowerShell 5.1', 'powershell.exe', '5', 'power-shell-5.1.md',
           intro('Windows PowerShell 5.1', 'powershell.exe',
                 'command list and syntax lines read from Windows PowerShell 5.1.26100 (Get-Command -Syntax); parameter facts '
                 'read from the same shell; parameter descriptions and output types summarized from Microsoft\'s PowerShell-Docs '
                 'reference/5.1 (CC BY 4.0).'))
    render(e7, m7, '7.6', 'PowerShell 7', 'pwsh.exe', '7', 'power-shell-7.md',
           intro('PowerShell 7.6', 'pwsh.exe',
                 'command list and syntax lines read from PowerShell 7.6.7 (Get-Command -Syntax); parameter facts read from the '
                 'same shell; parameter descriptions and output types summarized from Microsoft\'s PowerShell-Docs reference/7.6 '
                 '(CC BY 4.0).'))


main()
