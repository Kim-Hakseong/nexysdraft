---
name: nexysdraft
description: Draw and edit 2D CAD drawings (DWG / DXF) from a plain-language request, without opening a window. Use when the user asks to draw a part, plate, panel, floor plan or diagram, to create or modify a DWG/DXF file, to add dimensions or text, to trim/extend/move/copy objects, to replace text in a drawing, to export a drawing to PDF, or to list what a DWG/DXF contains. Works by writing a job script and running `NexysDraft.exe --script job.txt --out dir --json`, then reading result.json.
---

# NexysDraft CLI (`--script`)

`NexysDraft.exe --script <job.txt> [--out <dir>] [--json]` runs the job line by line with no window, writes `<dir>/result.json`
(`ok`, one record per line with `result`/`error`, `ms`), prints it to stdout with `--json`, exit code 0 = all lines ok, 1 = a line failed
(the failing line number and reason are in `errors`; later lines are not run), 2 = job file unreadable. Relative paths in `open` / `save` /
`pdf` are relative to the shell's current directory (not to `--out`, not to the job file): `cd` into the work folder first or write full
paths. `--out` is created when missing (default: the job file's folder).

**PowerShell does not wait for this program unless its output is piped.** Run it as `& "…NexysDraft.exe" --script job.txt --out . --json | Out-String`
(or from cmd / bash, which wait). Without the pipe the call returns at once, `$LASTEXITCODE` is empty and result.json appears seconds later.
 Quote strings with spaces: `text 1A2 "FT-101 열림"`.

## Where the program is
Not installed as a skill yet: in the release zip `NexysDraft.exe` is two folders above this file (`..\..\NexysDraft.exe`), in the repository it is `app\dist\NexysDraft.exe`. <!--NEXYSDRAFT_EXE-->

Windows only. Portable: `NexysDraft.exe` + `tools/libredwg/` next to it (DWG read/write runs LibreDWG as a separate process). The same
file works as a skill for Claude Code, Gemini CLI and Antigravity CLI; `NexysDraft.exe --install-skill` copies it to each of them with
the program path filled in.

## From a request to a drawing (how to work)
1. Decide the geometry in numbers first: units are millimetres, X to the right, Y up, angles counter-clockwise from +X. Put the lower-left
   corner of the part at 0,0 unless the user says otherwise. Ask only when a dimension that changes the shape is missing.
2. Use layers by purpose (outline, centre lines, DIM, TEXT) with `layer new NAME COLOR` + `layer current NAME` (colours: 1 red, 2 yellow,
   3 green, 4 cyan, 5 blue, 6 magenta, 7 black/white).
3. Write the whole job into one text file (UTF-8), end it with `query extents`, `query entities`, `save …` and, when the user should see
   it, `pdf …`. Run it once; do not run the program line by line.
4. Check `result.json`: `ok` must be true. Compare `query extents` with the size you intended (dimensions and text count too: a 200 × 120
   plate with dimensions outside it reports more — put a `query extents` before the dimensions when the bare size matters) and
   `query entities` → `byType` with the number of objects you drew (`rec` and `pol` are one LWPOLYLINE each). On an error, fix the line `errors[0].line` names and run the job again.
5. Not available from the command line — say so instead of improvising: fillet / chamfer / array / hatch, dimensions other than linear
   (`dli`), linetype assignment, PDF paper size or scale (the PDF is fitted to the detected sheet, A3 by default).
6. Tell the user the output paths. A DWG is R2000 and opens in AutoCAD / DWG TrueView; a DXF opens in any CAD.

## Commands (one per line, `#` comments)
| Command | Meaning |
|---|---|
| `new` | empty drawing (layer 0, DASHED, Standard = malgun.ttf) |
| `open PATH.dwg\|.dxf` | open (DWG via LibreDWG) |
| `save PATH.dxf [--ver r2000\|keep]` / `save PATH.dwg [--no-image]` | DXF: originals up to AC1027 keep their version and untouched bytes; newer originals (AC1032) are written as R2000 so nanoCAD 5 opens them (`--ver` forces either); pure ASCII (`\U+XXXX`). DWG = R2000 via dxf2dwg (about 1 s per 5 MB); `--no-image` leaves raster IMAGE entities out. The result reports `version` and `reason` |
| `pdf PATH.pdf [--merged] [--sheets] [--overview]` | searchable PDF set (Malgun Gothic subset): `PATH.pdf` one page per detected sheet (+ rest page), `PATH__001.pdf` … one file per page (`__rest.pdf`), `PATH__overview.pdf` whole model space on one page. No flag = all three, except a single-page drawing gets the merged file only (`note: "single sheet — merged only"`); named flags are followed as given. The result lists `files` (path, kind, pages, paper) |
| `select all\|none\|window X0,Y0 X1,Y1\|crossing X0,Y0 X1,Y1\|handles H…\|layer NAME\|type LINE` | selection for the edit commands |
| `layer new NAME [COLOR]`, `layer current NAME`, `layer set NAME on|off|frozen|thawed|locked|unlocked|COLOR` | layers (`current` creates when missing) |
| `l X0,Y0 X1,Y1 [X2,Y2 …]` | line(s) through the points on the current layer |
| `pl [c] X,Y X,Y …` | polyline, `c` = closed |
| `c CX,CY R` | circle |
| `rec X0,Y0 X1,Y1` | rectangle (closed LWPOLYLINE) from two corners |
| `a X1,Y1 X2,Y2 X3,Y3` | arc through three points (start, on the arc, end); the result reports centre, radius and the start / end angles (stored counter-clockwise). For a slot, two lines + two `a` arcs is the short way |
| `el X0,Y0 X1,Y1 HALF` | ellipse: one axis by its two ends, HALF = the other half axis |
| `pol N CX,CY R [c]` | regular polygon, N sides, radius R inscribed (or `c` = circumscribed), bottom edge horizontal |
| `po X,Y` | point |
| `dt X,Y H TEXT` | single-line text, height H (everything after H is the text; no quotes needed) |
| `mt X,Y W H TEXT` | MTEXT, top-left at X,Y, width W, height H (`\P` = new line) |
| `dli X0,Y0 X1,Y1 DX,DY [ANGLE]` | linear dimension between two points, dimension line through DX,DY (angle 0 horizontal / 90 vertical, else automatic) |
| `m DX,DY` · `co DX,DY` · `e` · `ro CX,CY DEG` · `mi X0,Y0 X1,Y1 [erase]` · `s X0,Y0 X1,Y1 DX,DY` | move / copy / erase / rotate / mirror / stretch the selection |
| `o HANDLE DIST SX,SY` | offset one line/circle/arc/polyline towards the side point |
| `tr X,Y [X,Y …]` | trim: each point lies on the part to remove; the object under it is cut at the nearest crossings with other objects on both sides (a circle becomes an arc, a closed polyline opens). Fails when the object crosses nothing there |
| `ex X,Y [X,Y …]` | extend: each point lies near the end to lengthen; that end of the line / arc / open polyline grows to the next object in its path |
| `text HANDLE STRING` | replace a TEXT value or MTEXT plain content |
| `replace OLD NEW` | substring replace in every TEXT / MTEXT / attribute |
| `undo` · `redo` | |
| `query entities|layers|extents|texts|selection` | JSON results in result.json |

## Example 1 — open a drawing, replace tag text, save as DWG
```
open C:\work\drawings\P-101.dwg
query entities
replace PT-101 PT-201
save C:\work\out\P-101_rev2.dxf
save C:\work\out\P-101_rev2.dwg
```
`result.json` → `lines[2].result.replaced` = number of strings changed; the DWG line reports the dxf2dwg time.

## Example 2 — rack panel outline with dimensions → PDF (the self-test `--skilltest` runs exactly this)
```
new
layer new PANEL 1
layer current PANEL
pl c 0,0 600,0 600,2000 0,2000
l 0,100 600,100
c 300,1000 40
layer new DIM 4
layer current DIM
dli 0,0 600,0 300,-120
dli 0,0 0,2000 -150,1000
layer new TEXT 3
layer current TEXT
query extents
save rack_panel.dxf
pdf rack_panel.pdf
```
Expected: `pdf` result `pages = 1`, `texts ≥ 2` (the two dimension values 600 and 2000), and `pdftotext rack_panel.pdf -` contains `600` and `2000`.

## Example 3 — a slot from two circles and two lines (trim)
```
new
c 0,0 10
c 40,0 10
l 0,10 40,10
l 0,-10 40,-10
tr 5,0 35,0
query entities
save slot.dxf
```
`tr 5,0` removes the right half of the first circle (the part of it nearest to 5,0 between the two tangent lines), `35,0` the left half of the
second: the result is 2 lines + 2 arcs.

## Example 4 — entity statistics as JSON
```
open C:\work\drawings\P-101.dwg
query entities
query layers
query texts
```
`lines[1].result.byType` holds counts per entity type, `lines[2].result` the layer table with counts, `lines[3].result` every TEXT/MTEXT
with handle, position and height (use the handle with `text HANDLE …`).

## Gate (what "works" means)
- `--skilltest` (self-test): runs Example 2 from this file, checks result.json ok, PDF pages = 1, and pdftotext finds `600` and `2000`.
- Open any sample DWG and `query entities` → `ok: true` and `total > 0`.
- A failing line stops the job with `errors[0].line` / `.error`; exit code 1.
