"""STYLE records (raw groups) and text usage per style in a DXF.
Usage: python -X utf8 -I tools/styleuse.py file.dxf [--enc cp949]
Prints each STYLE's groups 2/3/4/70/1001/1000/1071 (raw bytes when non-ASCII or \\U+ escaped), then TEXT/MTEXT/ATTRIB counts per
style (all / Korean), and MTEXT inline \\f font codes."""
import sys, re, collections

BS = chr(92)

def main():
    path = sys.argv[1]
    raw = open(path, 'rb').read().replace(b'\r\n', b'\n')
    m = re.search(rb'\$ACADVER\s*\n\s*1\s*\n(\w+)', raw)
    ver = m.group(1).decode() if m else '?'
    enc = 'utf-8' if ver >= 'AC1021' else 'cp949'
    if '--enc' in sys.argv: enc = sys.argv[sys.argv.index('--enc') + 1]
    print('ACADVER', ver, 'enc', enc)
    lines = raw.split(b'\n')
    styles = []; cur = None; ent = None
    use = collections.defaultdict(lambda: [0, 0]); inline = collections.Counter()
    def korean(t):
        return any(ord(c) > 127 for c in t) or (BS + 'U+') in t or (BS + 'M+') in t
    def flush():
        if ent and ent['t'] in ('TEXT', 'MTEXT', 'ATTRIB', 'ATTDEF'):
            st = ent.get('7', 'Standard'); txt = ent.get('1', '') + ent.get('3', '')
            use[(ent['t'], st)][0] += 1
            if korean(txt): use[(ent['t'], st)][1] += 1
            for f in re.findall(re.escape(BS) + r'[fF]([^|;]*)', txt): inline[f] += 1
    for i in range(0, len(lines) - 1, 2):
        c = lines[i].strip().decode('ascii', 'replace'); v = lines[i + 1]; s = v.decode(enc, 'replace')
        if c == '0':
            flush(); ent = None
            if cur is not None: styles.append(cur); cur = None
            if s == 'STYLE': cur = []
            elif s in ('TEXT', 'MTEXT', 'ATTRIB', 'ATTDEF'): ent = {'t': s}
            continue
        if cur is not None: cur.append((c, s, v))
        if ent is not None:
            if c in ('1', '3') and c in ent: ent[c] += s
            else: ent.setdefault(c, s)
    flush()
    for st in styles:
        out = []
        for c, s, v in st:
            if c not in ('2', '3', '4', '70', '1001', '1000', '1071'): continue
            x = f'{c}={s!r}'
            if any(b > 127 for b in v) or b'\\U+' in v: x += f' raw={v!r}'
            out.append(x)
        print('STYLE', ' | '.join(out))
    print('--- usage (type, style): all / korean')
    for k, (a, n) in sorted(use.items()): print(k, a, n)
    print('--- inline fonts (MTEXT)')
    for k, n in inline.most_common(): print(repr(k), n)

main()
