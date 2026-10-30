"""Bisect which ENTITIES records of an R2000 DXF make dxf2dwg's DWG unreadable for ODA (empty output / error).
Usage: python -X utf8 -I tools/bisect_dwg.py <r2000.dxf> <dxf2dwg.exe> <workdir>
Prints the minimal culprit records (type, handle, first groups)."""
import sys, os, subprocess, re, json, pathlib
sys.path.insert(0, os.path.dirname(__file__))
import oda

src, tool, work = sys.argv[1], sys.argv[2], pathlib.Path(sys.argv[3]); work.mkdir(parents=True, exist_ok=True)
data = open(src, 'rb').read()
i = data.index(b'  2\r\nENTITIES\r\n') + len(b'  2\r\nENTITIES\r\n'); j = data.index(b'  0\r\nENDSEC\r\n', i)
head, body, tail = data[:i], data[i:j], data[j:]
recs = re.split(rb'(?=  0\r\n(?!VERTEX|SEQEND|ATTRIB))', body); recs = [r for r in recs if r]
print('records', len(recs))
n = [0]
def ok(subset):
    n[0] += 1; p = work / f't{n[0]}.dxf'; d = work / f't{n[0]}.dwg'
    p.write_bytes(head + b''.join(subset) + tail)
    subprocess.run([tool, '-y', '-o', str(d), str(p)], capture_output=True, timeout=300)
    if not d.exists(): return False
    st, out, ms, msg = oda.convert(str(d), out_dir=str(work))
    good = st == 'OK' and out is not None and os.path.getsize(out) > 0
    return good
assert ok([]), 'empty drawing must pass'
if ok(recs): print('whole file passes'); sys.exit(0)
# find a minimal failing subset by halving (ddmin-lite): keep the failing half, else try both halves combined greedily
cur = recs
while len(cur) > 1:
    half = len(cur) // 2; a, b = cur[:half], cur[half:]
    if not ok(a): cur = a
    elif not ok(b): cur = b
    else: break
print('culprit set', len(cur))
for r in cur[:10]:
    txt = r.decode('latin1').split('\r\n'); t = txt[1] if len(txt) > 1 else '?'
    hnd = txt[txt.index('  5') + 1] if '  5' in txt else '?'
    print('  ', t, hnd, ' | '.join(txt[2:40])[:400])
