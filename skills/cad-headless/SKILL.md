---
name: cad-headless
description: Run other CAD tools headless on this PC (KiCad kicad-cli, FreeCAD freecadcmd, Blender) to produce DXF, then hand the DXF to NexysDraft's CLI for dimensions, DWG and searchable PDF. Use when a drawing must come out of a PCB, a 3D part or a scene without opening any GUI.
---

# Headless CAD → DXF → NexysDraft

Detect first, then use only what is installed. The recipes below were run on the development PC (2026-10-30): KiCad 10.0.5 and FreeCAD 1.1 present, Blender absent.

## Detection (PowerShell)
```powershell
$cands = @{
  kicad   = @("$env:LOCALAPPDATA\Programs\KiCad\*\bin\kicad-cli.exe", "C:\Program Files\KiCad\*\bin\kicad-cli.exe")
  freecad = @("$env:LOCALAPPDATA\Programs\FreeCAD*\bin\freecadcmd.exe", "C:\Program Files\FreeCAD*\bin\FreeCADCmd.exe")
  blender = @("C:\Program Files\Blender Foundation\Blender*\blender.exe", "$env:LOCALAPPDATA\Programs\Blender*\blender.exe")
}
foreach ($k in $cands.Keys) { $hit = $cands[$k] | ForEach-Object { Get-ChildItem $_ -ErrorAction SilentlyContinue } | Sort-Object FullName -Descending | Select-Object -First 1
  if (-not $hit) { $hit = (Get-Command "$k*" -ErrorAction SilentlyContinue | Select-Object -First 1).Source }
  "{0}: {1}" -f $k, ($(if ($hit) { $hit.FullName ?? $hit } else { "not installed" })) }
```
Only write a recipe for a tool the detection found. NexysDraft CLI: `NexysDraft.exe --script job.txt --out dir --json` (skills/nexysdraft/SKILL.md).

## Recipe 1 — KiCad PCB outline + silkscreen → DXF → PDF (kicad-cli)
```powershell
$k = "$env:LOCALAPPDATA\Programs\KiCad\10.0"
& "$k\bin\kicad-cli.exe" pcb export dxf --layers "Edge.Cuts,F.SilkS" --mode-single --output-units mm `
   --output C:\work\board.dxf "$k\share\kicad\demos\ecc83\ecc83-pp.kicad_pcb"
```
`--output` must be a file path (a folder path fails with "생성에 실패"). Then the NexysDraft job:
```
open C:\work\board.dxf
query entities
pdf C:\work\board.pdf
```
Measured: 632 entities (LINE 564, CIRCLE 68; layers Edge.Cuts / F.Silkscreen), PDF 1 page A3. KiCad plots text as strokes, so the PDF has no searchable text.

## Recipe 2 — FreeCAD part → top-view DXF → dimension → PDF (freecadcmd)
`plate.py`:
```python
import FreeCAD, Part, importDXF
doc = FreeCAD.newDocument("plate")
plate = Part.makeBox(200, 120, 5)
for x, y in [(20, 20), (180, 20), (20, 100), (180, 100)]:
    plate = plate.cut(Part.makeCylinder(4, 5, FreeCAD.Vector(x, y, 0)))
top = doc.addObject("Part::Feature", "Top")
top.Shape = Part.Compound([e for e in plate.Edges if all(abs(v.Point.z - 5) < 1e-6 for v in e.Vertexes)])
doc.recompute()
importDXF.export([top], r"C:\work\plate.dxf")
```
```powershell
& "$env:LOCALAPPDATA\Programs\FreeCAD 1.1\bin\freecadcmd.exe" plate.py
```
NexysDraft job:
```
open C:\work\plate.dxf
layer new DIM 4
layer current DIM
dli 0,0 200,0 100,-25
pdf C:\work\plate.pdf
```
Measured: DXF AC1014 with LINE 4 + CIRCLE 4, dimension 200, PDF 1 page, pdftotext finds "200".

## Recipe 3 — Blender
Not installed on the development PC: no recipe is given (detection prints "not installed"). When it is present, the route is `blender -b scene.blend --python-expr "..."` writing an SVG/DXF of a camera view, then the NexysDraft job of Recipe 1.

## Gate
- Detection lists each tool with a path or "not installed".
- Recipe 1 and 2 jobs end with `"ok": true` in result.json and a 1-page PDF.
