"""Which TABLES / BLOCKS records of an R2000 DXF (with ENTITIES emptied) make dxf2dwg's DWG unreadable for ODA?
Usage: python -X utf8 -I tools/bisect_sections.py <r2000.dxf> <dxf2dwg.exe> <workdir>"""
import sys, os, subprocess, re, pathlib
sys.path.insert(0, os.path.dirname(__file__))
import oda
src, tool, work = sys.argv[1], sys.argv[2], pathlib.Path(sys.argv[3]); work.mkdir(parents=True, exist_ok=True)
data = open(src, 'rb').read()
def section(name):
    a = data.index(b'  2\r\n' + name + b'\r\n') + len(b'  2\r\n' + name + b'\r\n'); b = data.index(b'  0\r\nENDSEC\r\n', a); return a, b
ea, eb = section(b'ENTITIES'); data = data[:ea] + data[eb:]
n = [0]
def ok(blob, tag):
    n[0] += 1; p = work / f'{tag}_{n[0]}.dxf'; d = work / f'{tag}_{n[0]}.dwg'; p.write_bytes(blob)
    subprocess.run([tool, '-y', '-o', str(d), str(p)], capture_output=True, timeout=300)
    if not d.exists(): return False
    st, out, ms, msg = oda.convert(str(d), out_dir=str(work))
    return st == 'OK' and out is not None and os.path.getsize(out) > 0
print('entities removed:', ok(data, 'base'))
# per table: drop all entries (keep TABLE head / ENDTAB) one table at a time
ta, tb = section(b'TABLES'); tables = data[ta:tb]
for t in [b'VPORT', b'LTYPE', b'LAYER', b'STYLE', b'VIEW', b'UCS', b'APPID', b'DIMSTYLE', b'BLOCK_RECORD']:
    m = re.search(rb'  0\r\nTABLE\r\n  2\r\n' + t + rb'\r\n.*?  0\r\nENDTAB\r\n', tables, re.S)
    if not m: continue
    seg = m.group(0); recs = re.split(rb'(?=  0\r\n' + t + rb'\r\n)', seg)
    kept = recs[0]
    if t == b'BLOCK_RECORD': kept += b''.join(r for r in recs[1:] if b'*Model_Space' in r or b'*Paper_Space' in r or b'*MODEL_SPACE' in r) + b'  0\r\nENDTAB\r\n'
    elif t == b'LAYER': kept += b''.join(r for r in recs[1:] if b'\r\n  2\r\n0\r\n' in r) + b'  0\r\nENDTAB\r\n'
    elif t == b'STYLE': kept += b''.join(r for r in recs[1:] if b'\r\n  2\r\nStandard\r\n' in r) + b'  0\r\nENDTAB\r\n'
    else: kept += b'  0\r\nENDTAB\r\n'
    blob = data[:ta] + tables.replace(seg, kept) + data[tb:]
    if t == b'BLOCK_RECORD':
        ba, bb = section(b'BLOCKS'); blocks = data[ba:bb]
        keepb = re.findall(rb'  0\r\nBLOCK\r\n.*?  0\r\nENDBLK\r\n.*?(?=  0\r\n(?:BLOCK|ENDSEC)\r\n|\Z)', blocks, re.S)
        keepb = [x for x in keepb if b'*Model_Space' in x or b'*Paper_Space' in x]
        blob = blob.replace(blocks, b''.join(keepb)) if blocks in blob else blob
    print(f'drop {t.decode():13s} entries → ODA ok: {ok(blob, t.decode())}')
# blocks only
ba, bb = section(b'BLOCKS'); blocks = data[ba:bb]
blks = re.split(rb'(?=  0\r\nBLOCK\r\n)', blocks); blks = [x for x in blks if x]
print('blocks', len(blks))
def blob_with(sub): return data[:ba] + b''.join(sub) + data[bb:]
layout = [x for x in blks if b'*Model_Space' in x or b'*Paper_Space' in x]
user = [x for x in blks if x not in layout]
print('only layout blocks → ODA ok:', ok(blob_with(layout), 'blk'))
cur = user
while len(cur) > 1:
    h = len(cur) // 2; a, b = cur[:h], cur[h:]
    if not ok(blob_with(layout + a), 'blk'): cur = a
    elif not ok(blob_with(layout + b), 'blk'): cur = b
    else: break
print('culprit blocks', len(cur))
for x in cur[:5]:
    t = x.decode('latin1').split('\r\n'); nm = t[t.index('  2') + 1] if '  2' in t else '?'
    print('  block', nm, 'entities', x.count(b'\r\n  0\r\n') - 1, ' | '.join(t[:30])[:300])
