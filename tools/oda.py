"""ODA File Converter wrapper for the development-PC gates (never used by the product).

Usage: python -X utf8 -I tools/oda.py <file.dwg|file.dxf> [more files] [--to DXF|DWG] [--ver ACAD2018] [--out dir]
Reads config/dev.json for the converter path (written by F0 discovery). Prints one line per file:
  OK <out path> <ms>   or   ERR <message>   or   SKIP (converter missing)
Each input is staged into its own folder (ODA converts folders), audit on (.err files reported).
"""
import json, os, shutil, subprocess, sys, time, tempfile, pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent

def converter():
    p = ROOT / 'config' / 'dev.json'
    if not p.exists(): return None
    try:
        exe = json.loads(p.read_text(encoding='utf-8')).get('odaFileConverter')
        return exe if exe and os.path.exists(exe) else None
    except Exception:
        return None

def convert(src, to='DXF', ver='ACAD2018', out_dir=None, timeout=600):
    exe = converter()
    if not exe: return ('SKIP', None, 0, 'ODA File Converter not configured (config/dev.json)')
    src = pathlib.Path(src)
    work = pathlib.Path(tempfile.mkdtemp(prefix='oda_'))
    inp = work / 'in'; outp = work / 'out'; inp.mkdir(); outp.mkdir()
    staged = inp / ('in' + src.suffix.lower())
    shutil.copy2(src, staged)
    t0 = time.time()
    cmd = [exe, str(inp), str(outp), ver, to, '0', '1', '*' + src.suffix.lower()]
    try:
        subprocess.run(cmd, timeout=timeout, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    except subprocess.TimeoutExpired:
        return ('ERR', None, int((time.time() - t0) * 1000), 'timeout')
    ms = int((time.time() - t0) * 1000)
    errs = list(outp.glob('*.err'))
    msg = ''
    for e in errs:
        msg += e.read_text(encoding='utf-8', errors='replace').strip().replace('\n', ' | ')
    outs = [f for f in outp.iterdir() if f.suffix.lower() == '.' + to.lower()]
    if not outs:
        return ('ERR', None, ms, msg or 'no output')
    dest = pathlib.Path(out_dir) if out_dir else src.parent
    dest.mkdir(parents=True, exist_ok=True)
    final = dest / (src.stem + '.oda.' + to.lower())
    shutil.copy2(outs[0], final)
    shutil.rmtree(work, ignore_errors=True)
    return ('OK' if not msg else 'WARN', final, ms, msg)

if __name__ == '__main__':
    args = sys.argv[1:]; to = 'DXF'; ver = 'ACAD2018'; out = None; files = []
    i = 0
    while i < len(args):
        a = args[i]
        if a == '--to': to = args[i + 1]; i += 2
        elif a == '--ver': ver = args[i + 1]; i += 2
        elif a == '--out': out = args[i + 1]; i += 2
        else: files.append(a); i += 1
    for f in files:
        st, p, ms, msg = convert(f, to, ver, out)
        print(f'{st}\t{f}\t{p or ""}\t{ms} ms\t{msg}')
