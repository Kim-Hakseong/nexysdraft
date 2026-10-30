---
name: nexysdraft
description: Drive NexysDraft (lightweight 2D CAD) without its window — write a job script, run `NexysDraft.exe --script job.txt --out dir --json`, read result.json. Use for opening DWG/DXF, replacing text, drawing simple geometry with dimensions, saving DXF/DWG, exporting searchable PDF, and querying entity/layer statistics.
---

# NexysDraft CLI (`--script`)

`NexysDraft.exe --script <job.txt> [--out <dir>] [--json]` runs the job line by line with no window, writes `<dir>/result.json`
(`ok`, one record per line with `result`/`error`, `ms`), prints it to stdout with `--json`, exit code 0 = all lines ok, 1 = a line failed
(the failing line number and reason are in `errors`; later lines are not run), 2 = job file unreadable. Paths may be relative to the
current directory. Quote strings with spaces: `text 1A2 "FT-101 열림"`.

Portable: `app/dist/NexysDraft.exe` + `tools/libredwg/` next to it (DWG read/write runs LibreDWG as a separate process). No install.

## Commands (one per line, `#` comments)
| Command | Meaning |
|---|---|
| `new` | empty drawing (layer 0, DASHED, Standard = malgun.ttf) |
| `open PATH.dwg|.dxf` | open (DWG via LibreDWG) |
| `save PATH.dxf` / `save PATH.dwg` | DXF keeps the original version and untouched bytes, pure ASCII (`\U+XXXX`); DWG = R2000 via dxf2dwg (slow for files > 1 MB, minutes) |
| `pdf PATH.pdf` | searchable PDF, one page per detected sheet (+ `__001.pdf` … per sheet), Malgun Gothic subset |
| `select all|none|window X0,Y0 X1,Y1|crossing X0,Y0 X1,Y1|handles H…|layer NAME|type LINE` | selection for the edit commands |
| `layer new NAME [COLOR]`, `layer current NAME`, `layer set NAME on|off|frozen|thawed|locked|unlocked|COLOR` | layers (`current` creates when missing) |
| `l X0,Y0 X1,Y1 [X2,Y2 …]` | line(s) through the points on the current layer |
| `pl [c] X,Y X,Y …` | polyline, `c` = closed |
| `c CX,CY R` | circle |
| `dli X0,Y0 X1,Y1 DX,DY [ANGLE]` | linear dimension between two points, dimension line through DX,DY (angle 0 horizontal / 90 vertical, else automatic) |
| `m DX,DY` · `co DX,DY` · `e` · `ro CX,CY DEG` · `mi X0,Y0 X1,Y1 [erase]` · `s X0,Y0 X1,Y1 DX,DY` | move / copy / erase / rotate / mirror / stretch the selection |
| `o HANDLE DIST SX,SY` | offset one line/circle/arc/polyline towards the side point |
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

## Example 3 — entity statistics as JSON
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
