"""dev PC check (not part of the product): open DWG/DXF files in an installed nanoCAD window and report how much of the canvas the drawing fills
after nanoCAD's own zoom on open. A drawing whose extents are broken shows as a dot or not at all.
Usage: python -X utf8 -I tools/nanocheck.py file [file …]      prints: fill_w fill_h pixels file     (GOOD when the lit box spans > 25 % of the canvas one way)"""
import sys, os, subprocess, tempfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
def fill(path, seconds=60):
    png = os.path.join(tempfile.gettempdir(), 'nanocheck_%d.png' % os.getpid())
    if os.path.exists(png): os.remove(png)
    subprocess.run(['powershell', '-ExecutionPolicy', 'Bypass', '-File', os.path.join(HERE, 'nanocad_gui.ps1'), os.path.abspath(path), str(seconds), png], capture_output=True, timeout=seconds + 60)
    if not os.path.exists(png): return None
    im = Image.open(png).convert('RGB'); W, H = im.size
    x0, y0, x1, y1 = 40, 132, W - 40, H - 244           # the model canvas of the 1936 x 1168 window
    px = im.load(); xs = []; ys = []
    if px[(x0 + x1) // 2, y0 + 5] == px[x0 + 5, (y0 + y1) // 2] and sum(px[x0 + 5, (y0 + y1) // 2]) > 150: return None   # grey client area: no document was opened
    for y in range(y0, y1, 1):
        for x in range(x0, x1, 1):
            if x < 270 and y > y1 - 200: continue      # UCS icon corner
            r, g, b = px[x, y]
            if r + g + b > 150: xs.append(x); ys.append(y)
    if not xs: return (0.0, 0.0, 0)
    return ((max(xs) - min(xs)) / (x1 - x0), (max(ys) - min(ys)) / (y1 - y0), len(xs))
def good(path, seconds=60):
    f = fill(path, seconds); return bool(f) and (f[0] > 0.25 or f[1] > 0.25)
if __name__ == '__main__':
    for p in sys.argv[1:]:
        f = fill(p); print('%s  %s' % ('none' if f is None else '%.3f %.3f %7d %s' % (f[0], f[1], f[2], 'GOOD' if (f[0] > 0.25 or f[1] > 0.25) else 'BAD'), p), flush=True)
