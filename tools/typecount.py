"""Entity counts by type in the ENTITIES section of DXF files (model / paper separately).
Usage: python -X utf8 -I tools/typecount.py <label=file.dxf> ..."""
import sys, re, collections

def counts(path):
    data = open(path, 'rb').read().replace(b'\r\n', b'\n')
    a = data.index(b'\nENTITIES\n'); b = data.index(b'\n  0\nENDSEC', a)
    lines = data[a:b].split(b'\n')
    model, paper = collections.Counter(), collections.Counter()
    cur, ps, sub = None, False, False
    def flush():
        if cur and not sub: (paper if ps else model)[cur] += 1
    i = 2
    while i < len(lines) - 1:
        code, val = lines[i].strip(), lines[i + 1].strip().decode('latin1')
        if code == b'0':
            flush(); cur, ps = val, False
            sub = val in ('VERTEX', 'SEQEND', 'ATTRIB')
        elif code == b'67' and val == '1': ps = True
        i += 2
    flush()
    return model, paper

cols = [a.split('=', 1) for a in sys.argv[1:]]
res = [(lab, *counts(p)) for lab, p in cols]
types = sorted(set().union(*[set(m) | set(p) for _, m, p in res]))
print('| type | ' + ' | '.join(l for l, _, _ in res) + ' |')
print('|---|' + '---|' * len(res))
for t in types:
    print(f'| {t} | ' + ' | '.join(f'{m[t]}' + (f' (+{p[t]} paper)' if p[t] else '') for _, m, p in res) + ' |')
print('| **total** | ' + ' | '.join(f'**{sum(m.values())}**' + (f' (+{sum(p.values())})' if sum(p.values()) else '') for _, m, p in res) + ' |')
