// Canvas view: viewport (pan/zoom), input, tools (select/draw/edit), snapping, overlay drawing, command line.
import { bridge } from './bridge';
import { Doc, SNAP_CEN, SNAP_END, SNAP_INT, SNAP_MID, SNAP_QUAD, SNAP_INS, textBox, Ent, segInt } from './doc';
import { Renderer, View, s2w, w2s, drawText } from './render';
import { fmtNum, h, aciRgb, rgbCss } from './util';
import { resolveTrack, TrackRes } from './track';
import { L, xl } from './i18n';

type Pt = [number, number];
export interface ToolSpec { name: string; prompt: string }
const ALIASES: Record<string, string> = { l: 'line', line: 'line', pl: 'pline', pline: 'pline', c: 'circle', circle: 'circle', m: 'move', move: 'move', co: 'copy', cp: 'copy', copy: 'copy', e: 'erase', erase: 'erase', del: 'erase', s: 'stretch', stretch: 'stretch', o: 'offset', offset: 'offset', mi: 'mirror', mirror: 'mirror', ro: 'rotate', rotate: 'rotate', z: 'zoom', zoom: 'zoom', ze: 'zoomext', dli: 'dim', dim: 'dim', dimlin: 'dim', dimlinear: 'dim', u: 'undo', undo: 'undo', redo: 'redo', la: 'layer', layer: 'layer', ed: 'text', ddedit: 'text', text: 'dtext', dt: 'dtext', dtext: 'dtext', t: 'mtext', mt: 'mtext', mtext: 'mtext', rec: 'rect', rectang: 'rect', rectangle: 'rect', a: 'arc', arc: 'arc', el: 'ellipse', ellipse: 'ellipse', pol: 'polygon', polygon: 'polygon', po: 'point', point: 'point', sel: 'select', select: 'select', esc: 'cancel', pan: 'pan', p: 'pan', regen: 'regen', re: 'regen', redraw: 'redraw', r: 'redraw', open: 'open', new: 'new', save: 'save', saveas: 'saveas', qsave: 'save', export: 'exportdwg', pdf: 'pdf', plot: 'pdf', print: 'pdf', snap: 'snap', sn: 'snap', grid: 'grid', ortho: 'ortho', polar: 'polar', osnap: 'osnap', os: 'osnap', f3: 'osnap', all: 'selectall', pr: 'props', props: 'props', properties: 'props', di: 'dist', dist: 'dist', id: 'id', rightclick: 'rightclick', tr: 'trim', trim: 'trim', ex: 'extend', extend: 'extend', otrack: 'otrack', f11: 'otrack' };
/** Polar tracking increments F10 / POLAR cycles through (AutoCAD POLARANG; default 90°). */
const POLAR_INCS = [90, 45];
/** AutoCAD time-sensitive right-click duration (ms). */
export const RIGHT_MS = 250;
/** A press that moves less than this many screen pixels before the release is a click, not a drag box (a fast click always slides a little). */
export const DRAG_PX = 6;
/** After a press that picked an object, the gesture becomes a drag box only when it is clearly a drag: held this long, or moved this far. */
export const PICK_DRAG_MS = 200, PICK_DRAG_PX = 24;

// ---- basic shape geometry (same formulas as NexysDraft.Core.Edit.Shapes)
const MTEXT_NL = String.fromCharCode(92) + 'P';
/** MTEXT content escaping: backslash and braces are format characters. */
function escMText(s: string) { const bs = String.fromCharCode(92); return s.split(bs).join(bs + bs).split('{').join(bs + '{').split('}').join(bs + '}'); }
export function rectPts(a: Pt, b: Pt): Pt[] { const x0 = Math.min(a[0], b[0]), y0 = Math.min(a[1], b[1]), x1 = Math.max(a[0], b[0]), y1 = Math.max(a[1], b[1]); return [[x0, y0], [x1, y0], [x1, y1], [x0, y1]]; }
export function arc3(p1: Pt, p2: Pt, p3: Pt): { cx: number; cy: number; r: number; a0: number; a1: number } | null {
  const [x1, y1] = p1, [x2, y2] = p2, [x3, y3] = p3;
  const d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2)); const sc = Math.max(1e-12, Math.hypot(x2 - x1, y2 - y1), Math.hypot(x3 - x1, y3 - y1));
  if (Math.abs(d) < 1e-12 * sc * sc) return null;
  const s1 = x1 * x1 + y1 * y1, s2 = x2 * x2 + y2 * y2, s3 = x3 * x3 + y3 * y3;
  const cx = (s1 * (y2 - y3) + s2 * (y3 - y1) + s3 * (y1 - y2)) / d, cy = (s1 * (x3 - x2) + s2 * (x1 - x3) + s3 * (x2 - x1)) / d, r = Math.hypot(x1 - cx, y1 - cy);
  const A = (x: number, y: number) => { const a = Math.atan2(y - cy, x - cx) * 180 / Math.PI; return a < 0 ? a + 360 : a; };
  const a1 = A(x1, y1), a2 = A(x2, y2), a3 = A(x3, y3); const ccw = (f: number, t: number) => { const v = t - f; return v < 0 ? v + 360 : v; };
  return ccw(a1, a2) <= ccw(a1, a3) ? { cx, cy, r, a0: a1, a1: a3 } : { cx, cy, r, a0: a3, a1: a1 };
}
function arcPts(g: { cx: number; cy: number; r: number; a0: number; a1: number }): Pt[] { let sweep = g.a1 - g.a0; if (sweep <= 0) sweep += 360; const n = Math.max(8, Math.ceil(sweep / 4)); const out: Pt[] = []; for (let i = 0; i <= n; i++) { const a = (g.a0 + sweep * i / n) * Math.PI / 180; out.push([g.cx + g.r * Math.cos(a), g.cy + g.r * Math.sin(a)]); } return out; }
export function polygonPts(n: number, c: Pt, radius: number, inscribed: boolean, angleDeg: number): Pt[] {
  n = Math.min(1024, Math.max(3, n)); const step = 2 * Math.PI / n; let a = angleDeg * Math.PI / 180, rv = radius;
  if (!inscribed) { rv = radius / Math.cos(Math.PI / n); a += Math.PI / n; }
  const out: Pt[] = []; for (let k = 0; k < n; k++) out.push([c[0] + rv * Math.cos(a + k * step), c[1] + rv * Math.sin(a + k * step)]); return out;
}
export function ellipseByAxis(p0: Pt, p1: Pt, other: number): { cx: number; cy: number; mx: number; my: number; ratio: number } | null {
  const cx = (p0[0] + p1[0]) / 2, cy = (p0[1] + p1[1]) / 2, mx = p1[0] - cx, my = p1[1] - cy, half = Math.hypot(mx, my);
  if (half < 1e-12 || other <= 1e-12) return null;
  if (other <= half) return { cx, cy, mx, my, ratio: other / half };
  const ux = -my / half, uy = mx / half; return { cx, cy, mx: ux * other, my: uy * other, ratio: half / other };
}
function ellipsePts(g: { cx: number; cy: number; mx: number; my: number; ratio: number }): Pt[] { const out: Pt[] = []; const nx = -g.my * g.ratio, ny = g.mx * g.ratio; for (let i = 0; i < 72; i++) { const t = i * Math.PI * 2 / 72; out.push([g.cx + g.mx * Math.cos(t) + nx * Math.sin(t), g.cy + g.my * Math.cos(t) + ny * Math.sin(t)]); } return out; }

export class CanvasView {
  el: HTMLElement; wrap: HTMLElement; glc: HTMLCanvasElement; ovc: HTMLCanvasElement; coord: HTMLElement; hint: HTMLElement;
  rend: Renderer; doc: Doc | null = null;
  view: View = { cx: 0, cy: 0, scale: 1, w: 800, h: 600, dpr: window.devicePixelRatio || 1 };
  selection = new Set<string>();
  cursor: Pt | null = null; cursorScreen: Pt | null = null; snapPt: { x: number; y: number; k: number } | null = null; snapOn = true;
  tool = 'select'; step = 0; pts: Pt[] = []; toolSel: string[] = []; pending: any = {}; lastTool = 'line';
  dragStart: Pt | null = null; dragMode: 'none' | 'pan' | 'box' = 'none'; panStart: { x: number; y: number; cx: number; cy: number } | null = null;
  onSelectionChanged: (() => void) | null = null; onPrompt: ((text: string) => void) | null = null; onHistory: ((line: string) => void) | null = null;
  onDblClickText: ((id: string) => void) | null = null; onCommand: ((name: string, arg?: string) => boolean) | null = null;
  frameTimes: number[] = []; raf = 0; needDraw = true; dark = false; dirtyStats = '';
  textEditEl: HTMLTextAreaElement | null = null;
  lastDist = 10; lastRadius = 10; lastSides = 4; lastTextH = 2.5; lastTextRot = 0;
  /** Drafting modes (status-bar pills, AutoCAD keys): F8 ortho, F10 polar tracking, F7 grid display, F9 grid snap; Shift inverts ortho while held. */
  orthoOn = false; polarOn = true; polarInc = 90; gridOn = false; gridSnapOn = false; gridSpacing = 10; shiftDown = false;
  /** Right click (AutoCAD time-sensitive right-click): 'timed' = a click shorter than RIGHT_MS is Enter (confirm / repeat), a longer press opens the menu (default);
   *  'enter' = always Enter; 'menu' = always the context menu. RIGHTCLICK TIMED|ENTER|MENU. */
  rightClickMode: 'timed' | 'enter' | 'menu' = 'timed'; rightDownAt = 0; regenCount = 0;
  /** Object snap tracking (F11): osnap points acquired by resting the cursor on them; alignment paths run through them. */
  otrackOn = true; trackPts: Pt[] = []; trk: TrackRes | null = null; private hoverSnap: { x: number; y: number } | null = null; private hoverTimer = 0;
  /** Selection box opened by a click on empty space (AutoCAD implied window): the next click closes it. */
  boxClick = false; private downS: Pt | null = null; private pressPicked = 0;
  /** TRIM / EXTEND mode (AutoCAD TRIMEXTENDMODE): quick = every object is an edge, pick at once; standard = choose the edges first. */
  trimMode: 'quick' | 'standard' = (() => { try { return localStorage.getItem('nx.trimmode') === 'standard' ? 'standard' : 'quick'; } catch { return 'quick'; } })() as 'quick' | 'standard';
  /** Freehand fence of a TRIM / EXTEND drag (world points after the press point). */
  fencePath: Pt[] = [];
  private previewSeq = 0; private previewKey = '';
  onModes: (() => void) | null = null; onFocusRail: ((which: 'layers' | 'props') => void) | null = null;
  viewStack: { cx: number; cy: number; scale: number }[] = []; lastWheelPush = 0; lastMiddleDown = 0; menuEl: HTMLElement | null = null;

  constructor() {
    this.el = h(`<div class="card canvas-card"></div>`);
    this.wrap = h(`<div class="canvas-wrap" tabindex="0"></div>`);
    this.glc = document.createElement('canvas'); this.ovc = document.createElement('canvas'); this.ovc.className = 'ov';
    this.coord = h(`<div class="coord">—</div>`); this.hint = h(`<div class="drop-hint"><div><b>NexysDraft</b>${L('Drop a DWG or DXF file here, or OPEN (Ctrl+O)', 'DWG 또는 DXF 파일을 여기에 끌어다 놓거나 열기 (Ctrl+O)')}</div></div>`);
    this.wrap.append(this.glc, this.ovc, this.hint, this.coord); this.el.append(this.wrap);
    this.rend = new Renderer(this.glc, this.ovc);
    new ResizeObserver(() => { this.resize(); }).observe(this.wrap);
    this.bind();
    try { const rc = localStorage.getItem('nx.rightclick'); if (rc === 'menu' || rc === 'enter' || rc === 'timed') this.rightClickMode = rc; } catch { }
    const loop = () => { if (this.needDraw) { this.needDraw = false; this.drawNow(); } this.raf = requestAnimationFrame(loop); }; this.raf = requestAnimationFrame(loop);
  }
  resize() { const r = this.wrap.getBoundingClientRect(); this.view.w = Math.max(1, r.width); this.view.h = Math.max(1, r.height); this.view.dpr = window.devicePixelRatio || 1; this.invalidate(); }
  invalidate() { this.needDraw = true; }
  setDoc(doc: Doc | null) { this.doc = doc; this.rend.setDoc(doc); this.selection.clear(); this.cancel(); this.hint.style.display = doc ? 'none' : ''; if (doc) this.zoomExtents(); this.viewStack = []; this.invalidate(); }
  setDark(d: boolean) { this.dark = d; this.rend.dark = d; this.wrap.classList.toggle('cv-dark', d); this.wrap.classList.toggle('cv-light', !d); this._tok = null; this.invalidate(); }

  // ---------------------------------------------------------------- viewport
  /** Remember the current view for ZOOM Previous (Z P). */
  pushView() { const v = this.view; const top = this.viewStack[this.viewStack.length - 1]; if (top && top.cx === v.cx && top.cy === v.cy && top.scale === v.scale) return; this.viewStack.push({ cx: v.cx, cy: v.cy, scale: v.scale }); if (this.viewStack.length > 30) this.viewStack.shift(); }
  zoomPrevious() { const p = this.viewStack.pop(); if (!p) { this.say('ZOOM Previous — no previous view'); return; } this.view.cx = p.cx; this.view.cy = p.cy; this.view.scale = p.scale; this.invalidate(); }
  zoomExtents() {
    this.pushView();
    const b = this.doc?.bbox; if (!b) { this.view.cx = 0; this.view.cy = 0; this.view.scale = 1; this.invalidate(); return; }
    const w = Math.max(1e-9, b[2] - b[0]), hh = Math.max(1e-9, b[3] - b[1]);
    this.view.scale = Math.min((this.view.w - 40) / w, (this.view.h - 40) / hh); this.view.cx = (b[0] + b[2]) / 2; this.view.cy = (b[1] + b[3]) / 2; this.invalidate();
  }
  zoomWindow(a: Pt, b: Pt) { this.pushView(); const w = Math.max(1e-9, Math.abs(b[0] - a[0])), hh = Math.max(1e-9, Math.abs(b[1] - a[1])); this.view.scale = Math.min(this.view.w / w, this.view.h / hh); this.view.cx = (a[0] + b[0]) / 2; this.view.cy = (a[1] + b[1]) / 2; this.invalidate(); }
  zoomAt(sx: number, sy: number, factor: number) { const [wx, wy] = s2w(this.view, sx, sy); this.view.scale *= factor; const [nx, ny] = s2w(this.view, sx, sy); this.view.cx += wx - nx; this.view.cy += wy - ny; this.invalidate(); }
  panBy(dx: number, dy: number) { this.view.cx -= dx / this.view.scale; this.view.cy += dy / this.view.scale; this.invalidate(); }
  toWorld(ev: MouseEvent): Pt { const r = this.wrap.getBoundingClientRect(); return s2w(this.view, ev.clientX - r.left, ev.clientY - r.top); }
  toScreen(ev: MouseEvent): Pt { const r = this.wrap.getBoundingClientRect(); return [ev.clientX - r.left, ev.clientY - r.top]; }
  pxTol(px = 8) { return px / this.view.scale; }

  // ---------------------------------------------------------------- input
  private bind() {
    const w = this.wrap;
    w.addEventListener('contextmenu', e => e.preventDefault());
    w.addEventListener('wheel', e => { e.preventDefault(); const now = performance.now(); if (now - this.lastWheelPush > 600) this.pushView(); this.lastWheelPush = now; const [sx, sy] = this.toScreen(e); this.zoomAt(sx, sy, e.deltaY < 0 ? 1.2 : 1 / 1.2); }, { passive: false });
    w.addEventListener('pointerdown', e => {
      w.focus(); this.closeMenu(); const s = this.toScreen(e); const p = this.toWorld(e); this.shiftDown = e.shiftKey;
      if (e.button === 1) {
        // wheel double-click = zoom extents (AutoCAD); Shift + wheel drag is orbit in AutoCAD → ignored here (no 3D)
        const now = performance.now(); if (now - this.lastMiddleDown < 400) { this.lastMiddleDown = 0; this.dragMode = 'none'; this.zoomExtents(); this.say('ZOOM Extents (wheel double-click)'); e.preventDefault(); return; }
        this.lastMiddleDown = now; if (e.shiftKey) return; e.preventDefault();
      }
      if (e.button === 1 || (e.button === 0 && e.altKey) || (e.button === 0 && this.tool === 'pan')) { this.dragMode = 'pan'; this.panStart = { x: s[0], y: s[1], cx: this.view.cx, cy: this.view.cy }; w.setPointerCapture(e.pointerId); return; }
      if (e.button === 2) { this.rightDownAt = performance.now(); try { w.setPointerCapture(e.pointerId); } catch { } return; }   // decided on release (time-sensitive right click)
      if (e.button !== 0) return;
      if (this.toolWantsPoint()) { this.point(this.effectivePoint(p, e.shiftKey), e.shiftKey); return; }
      if (this.boxClick && this.dragStart) { const a = this.dragStart; this.boxClick = false; this.dragStart = null; this.dragMode = 'none'; this.box(a, p, this.pending.shift); this.invalidate(); return; }   // second click of a click-click box
      // select tool / selection step: click pick or start box
      // An object under the cursor is taken at the press (AutoCAD): a fast click that slides before the release still picks it.
      this.pending.shift = e.shiftKey;
      this.pressPicked = 0; this.fencePath = [];
      if (this.doc && this.doc.hit(p[0], p[1], this.pxTol())) {
        this.dragStart = null; this.dragMode = 'none'; this.pick(p, e.shiftKey);
        if (!this.canBox() || this.isTrim()) return;
        this.pressPicked = performance.now();   // a deliberate drag that starts on an object still opens a box (dense drawings leave no empty spot to start from)
      }
      this.dragStart = p; this.downS = s; this.dragMode = 'box'; try { w.setPointerCapture(e.pointerId); } catch { }
    });
    w.addEventListener('pointermove', e => {
      const s = this.toScreen(e); const p = this.toWorld(e); this.cursorScreen = s; this.cursor = p; this.shiftDown = e.shiftKey;
      if (this.dragMode === 'pan' && this.panStart) { this.view.cx = this.panStart.cx - (s[0] - this.panStart.x) / this.view.scale; this.view.cy = this.panStart.cy + (s[1] - this.panStart.y) / this.view.scale; this.invalidate(); return; }
      if (this.doc && this.snapOn && this.toolWantsPoint() && this.dragMode !== 'box') this.snapPt = this.doc.snap(p[0], p[1], this.pxTol(10), this.tool === 'move' || this.tool === 'copy' || this.tool === 'stretch' ? new Set(this.toolSel) : null); else this.snapPt = null;
      this.acquireWatch(); if (this.isTrim() && this.step === 0 && this.dragMode === 'none') this.trimHover(p, e.shiftKey);
      if (this.isTrim() && this.step === 0 && this.dragMode === 'box' && !this.boxClick && this.dragStart) { const q = this.fencePath[this.fencePath.length - 1] ?? this.dragStart; if (Math.hypot(p[0] - q[0], p[1] - q[1]) >= this.pxTol(3)) this.fencePath.push(p); }
      const ep = this.toolWantsPoint() ? this.effectivePoint(p, e.shiftKey) : p; const base = this.basePoint();
      this.coord.textContent = `${fmtNum(ep[0], 3)}, ${fmtNum(ep[1], 3)}` + (this.snapPt && this.snapOn && this.toolWantsPoint() ? `  ${snapName(this.snapPt.k)}` : '') + (base && this.toolWantsPoint() ? `  ${fmtNum(Math.hypot(ep[0] - base[0], ep[1] - base[1]), 3)} < ${fmtNum(Math.atan2(ep[1] - base[1], ep[0] - base[0]) * 180 / Math.PI, 2)}°` : '');
      this.invalidate();
    });
    w.addEventListener('pointerup', e => {
      if (e.button === 2) { if (this.rightDownAt) { const held = performance.now() - this.rightDownAt; this.rightDownAt = 0; this.rightRelease(held, this.toScreen(e)); } return; }
      if (this.dragMode === 'pan') { this.dragMode = this.boxClick ? 'box' : 'none'; this.panStart = null; return; }
      if (this.dragMode === 'box' && this.dragStart) {
        if (this.boxClick) return;   // click-click box: waits for the second click
        const p = this.toWorld(e); const s = this.toScreen(e); const d = this.downS ? Math.hypot(s[0] - this.downS[0], s[1] - this.downS[1]) : Math.hypot(p[0] - this.dragStart[0], p[1] - this.dragStart[1]) * this.view.scale;
        if (this.pressPicked) { const held = performance.now() - this.pressPicked; this.pressPicked = 0; if (d >= DRAG_PX && (held >= PICK_DRAG_MS || d >= PICK_DRAG_PX)) this.box(this.dragStart, p, this.pending.shift); }
        else if (d < DRAG_PX) { const start = this.dragStart; const hit = this.pick(p, this.pending.shift); if (!hit && this.canBox()) { this.boxClick = true; this.dragStart = start; this.dragMode = 'box'; this.invalidate(); return; } }
        else if (this.canBox() || this.isTrim()) this.box(this.dragStart, p, this.pending.shift);
        this.dragStart = null; this.dragMode = 'none'; this.invalidate();
      }
    });
    // the system took the pointer away (window switch, native drag): no release will come, so forget the half-made gesture
    w.addEventListener('pointercancel', () => { this.rightDownAt = 0; this.panStart = null; if (this.dragMode === 'pan') this.dragMode = this.boxClick ? 'box' : 'none'; else if (this.dragMode === 'box' && !this.boxClick) { this.dragStart = null; this.dragMode = 'none'; } this.invalidate(); });
    w.addEventListener('dblclick', e => { if (this.tool !== 'select' || !this.doc) return; const p = this.toWorld(e); const id = this.doc.hit(p[0], p[1], this.pxTol()); const ent = id ? this.doc.ents.get(id) : null; if (ent && (ent.t === 'T' || ent.t === 'M')) this.editText(id!); });
    w.addEventListener('pointerleave', () => { this.cursor = null; this.snapPt = null; this.acquireWatch(); this.invalidate(); });
    // keys reach key() through the document handler in main.ts (a second listener here ran Enter / Space twice)
  }
  key(e: KeyboardEvent) {
    if (this.textEditEl) return;
    if (e.key === 'Escape') { if (this.boxClick && this.tool === 'select') { this.endBox(); e.preventDefault(); return; } const idle = this.tool === 'select'; this.closeMenu(); this.cancel(); if (idle && this.selection.size) this.setSelection([]); e.preventDefault(); return; }
    if (e.key === 'Delete' && this.selection.size && this.tool === 'select') { this.run('erase'); e.preventDefault(); return; }
    if (e.key === 'Enter' || e.key === ' ') { if (this.tool === 'select') { this.run(this.lastTool); } else this.enter(); e.preventDefault(); return; }
  }
  /** Enter on an idle command line / right click with nothing running: repeat the last command (AutoCAD). */
  rightClick() { if (this.tool === 'select') this.run(this.lastTool); else this.enter(); }
  /** Right button released after `held` ms: timed mode → < RIGHT_MS = Enter, otherwise the context menu. Returns what it did. */
  rightRelease(held: number, s: Pt): 'enter' | 'menu' {
    if (this.boxClick) { this.endBox(); this.lastRight = 'enter'; return 'enter'; }
    const menu = this.rightClickMode === 'menu' || (this.rightClickMode === 'timed' && held >= RIGHT_MS);
    if (menu) this.openMenu(s); else this.rightClick();
    this.lastRight = menu ? 'menu' : 'enter'; return this.lastRight;
  }
  lastRight: 'enter' | 'menu' | '' = '';
  openMenu(s: Pt) {
    this.closeMenu(); const idle = this.tool === 'select';
    const items: [string, string][] = idle ? [['repeat', `${L('Repeat', '반복')} ${this.lastTool.toUpperCase()}`], ['zoomext', L('Zoom extents (Z E)', '전체 보기 (Z E)')], ['zoomprev', L('Previous view (Z P)', '이전 보기 (Z P)')], ['selectall', L('Select all (Ctrl+A)', '전체 선택 (Ctrl+A)')]] : [['enter', L('Confirm (Enter)', '확정 (Enter)')], ['cancel', L('Cancel (Esc)', '취소 (Esc)')], ['zoomext', L('Zoom extents (Z E)', '전체 보기 (Z E)')]];
    const m = h(`<div class="ctxmenu">${items.map(([k, t]) => `<button type="button" data-m="${k}">${t}</button>`).join('')}</div>`);
    m.style.left = s[0] + 'px'; m.style.top = s[1] + 'px';
    m.addEventListener('pointerdown', ev => ev.stopPropagation());
    m.addEventListener('click', ev => { const k = ((ev.target as HTMLElement).closest('[data-m]') as HTMLElement | null)?.dataset.m; this.closeMenu(); if (k === 'repeat') this.run(this.lastTool); else if (k === 'enter') this.enter(); else if (k === 'cancel') this.cancel(); else if (k === 'zoomext') this.zoomExtents(); else if (k === 'zoomprev') this.zoomPrevious(); else if (k === 'selectall') this.run('selectall'); this.wrap.focus(); });
    this.wrap.append(m); this.menuEl = m;
  }
  closeMenu() { this.menuEl?.remove(); this.menuEl = null; }

  // ---------------------------------------------------------------- drafting modes and point constraints
  toggleMode(m: 'ortho' | 'polar' | 'grid' | 'snap' | 'osnap' | 'otrack', on?: boolean) {
    switch (m) {
      case 'ortho': this.orthoOn = on ?? !this.orthoOn; if (this.orthoOn) this.polarOn = false; this.say(`<Ortho ${this.orthoOn ? 'on' : 'off'}>`); break;
      case 'polar': this.polarOn = on ?? !this.polarOn; if (this.polarOn) this.orthoOn = false; this.say(`<Polar ${this.polarOn ? 'on' : 'off'}> ${this.polarInc}°`); break;
      case 'grid': this.gridOn = on ?? !this.gridOn; this.say(`<Grid ${this.gridOn ? 'on' : 'off'}> ${fmtNum(this.gridSpacing, 4)}`); break;
      case 'snap': this.gridSnapOn = on ?? !this.gridSnapOn; this.say(`<Snap ${this.gridSnapOn ? 'on' : 'off'}> ${fmtNum(this.gridSpacing, 4)}`); break;
      case 'osnap': this.snapOn = on ?? !this.snapOn; this.say(`<Osnap ${this.snapOn ? 'on' : 'off'}>`); break;
      case 'otrack': this.otrackOn = on ?? !this.otrackOn; if (!this.otrackOn) this.trackPts = []; this.say(`<Object Snap Tracking ${this.otrackOn ? 'on' : 'off'}>`); break;
    }
    this.onModes?.(); this.invalidate();
  }
  /** The point the rubber band starts from (ortho / polar / direct distance are measured from it), or null. */
  basePoint(): Pt | null {
    if (!this.pts.length) return null;
    switch (this.tool) {
      case 'line': case 'pline': case 'dist': return this.pts[this.pts.length - 1];
      case 'move': case 'copy': case 'stretch': case 'rotate': return this.step === 2 ? this.pts[0] : null;
      case 'mirror': return this.step === 2 ? this.pts[0] : null;
      case 'dim': return this.pts.length === 1 ? this.pts[0] : null;
      case 'circle': return this.step === 1 ? this.pts[0] : null;
      case 'rect': return this.pts.length === 1 ? this.pts[0] : null;
      case 'arc': return this.pts.length ? this.pts[this.pts.length - 1] : null;
      case 'ellipse': return this.step === 1 ? this.pts[0] : this.step === 2 ? this.ellipseCenter() : null;
      case 'polygon': return this.step === 2 ? this.pts[0] : null;
      case 'mtext': return this.step === 1 ? this.pts[0] : null;
    }
    return null;
  }
  /** Raw cursor → picked point: object snap wins (AutoCAD), else grid snap (F9), then AutoTrack: ortho (F8, inverted while Shift is held), polar tracking (F10) and object snap tracking (F11). */
  effectivePoint(p: Pt, shift: boolean): Pt {
    if (this.snapPt && this.snapOn) { this.trk = null; return [this.snapPt.x, this.snapPt.y]; }
    let q: Pt = p;
    if (this.gridSnapOn && this.gridSpacing > 0) { const g = this.gridSpacing; q = [Math.round(q[0] / g) * g, Math.round(q[1] / g) * g]; }
    this.trk = this.track(q, shift, true); return this.trk ? this.trk.pt : q;
  }
  /** Ortho / polar from the base point only (direct distance entry goes along this). */
  constrain(q: Pt, shift: boolean): Pt { const r = this.track(q, shift, false); return r ? r.pt : q; }
  private track(q: Pt, shift: boolean, objects: boolean): TrackRes | null {
    const free = this.tool === 'circle' || this.tool === 'rect' || this.tool === 'mtext';   // radius / opposite corner: no ortho or polar
    const base = free ? null : this.basePoint();
    return resolveTrack(q, { base, ortho: this.orthoOn !== shift, polar: this.polarOn, polarInc: this.polarInc, pts: objects ? this.trackPts : [], otrack: objects && this.otrackOn && this.snapOn, tol: this.pxTol(10) });
  }
  /** Object snap tracking: a snap point the cursor rests on for 350 ms is acquired (resting on it again releases it). */
  private acquireWatch() {
    const sp = this.otrackOn && this.snapOn && this.toolWantsPoint() ? this.snapPt : null;
    if (sp && this.hoverSnap && sp.x === this.hoverSnap.x && sp.y === this.hoverSnap.y) return;
    clearTimeout(this.hoverTimer); this.hoverSnap = sp ? { x: sp.x, y: sp.y } : null; if (!sp) return;
    this.hoverTimer = window.setTimeout(() => { const cur = this.snapPt; if (cur && this.hoverSnap && cur.x === this.hoverSnap.x && cur.y === this.hoverSnap.y) this.acquire([cur.x, cur.y]); }, 350);
  }
  acquire(p: Pt) {
    const i = this.trackPts.findIndex(a => Math.hypot(a[0] - p[0], a[1] - p[1]) <= this.pxTol(2));
    if (i >= 0) this.trackPts.splice(i, 1); else { this.trackPts.push(p); if (this.trackPts.length > 7) this.trackPts.shift(); }
    this.invalidate();
  }

  // ---------------------------------------------------------------- selection
  /** Click on the canvas outside a point prompt. Returns false when nothing was under the cursor (the caller may open a selection box). */
  pick(p: Pt, add: boolean): boolean {
    if (!this.doc) return true; const id = this.doc.hit(p[0], p[1], this.pxTol());
    if (this.isTrim()) {
      if (this.step === 1) { if (id) { this.toolSel.includes(id) ? this.toolSel.splice(this.toolSel.indexOf(id), 1) : this.toolSel.push(id); this.say(`${this.toolSel.length} edge(s) — Enter to continue`); } this.invalidate(); return !!id; }
      if (id) this.trimAt([[id, p]], add); return !!id;   // a click on empty space starts a two-click fence (Quick) / crossing box (Standard)
    }
    if (this.tool !== 'select' && this.step === 0 && this.needsSelection()) { if (id) { this.toolSel.includes(id) ? this.toolSel.splice(this.toolSel.indexOf(id), 1) : this.toolSel.push(id); this.say(`${this.toolSel.length} selected — Enter to continue`); } this.invalidate(); return !!id; }
    if (this.tool === 'offset' && this.step === 1) { if (id) { this.pending.id = id; this.step = 2; this.prompt('Specify point on side to offset:'); } return true; }
    if (!add) this.selection.clear();
    if (id) { if (add && this.selection.has(id)) this.selection.delete(id); else this.selection.add(id); }
    this.onSelectionChanged?.(); this.invalidate(); return !!id;
  }
  box(a: Pt, b: Pt, add: boolean) {
    if (!this.doc) return;
    if (this.isTrim() && this.step === 0) {
      const path = this.fencePath.length ? [a, ...this.fencePath, b] : this.trimCrossing() ? [a, [b[0], a[1]] as Pt, b, [a[0], b[1]] as Pt, a] : [a, b]; this.fencePath = [];
      this.trimAt(this.fencePathPicks(path), add); return;
    }
    const crossing = b[0] < a[0]; const ids = this.doc.select(a[0], a[1], b[0], b[1], crossing);
    if (this.isTrim()) { for (const id of ids) if (!this.toolSel.includes(id)) this.toolSel.push(id); this.say(`${this.toolSel.length} edge(s) — Enter to continue`); this.invalidate(); return; }
    if (this.tool !== 'select' && this.step === 0 && this.needsSelection()) { for (const id of ids) if (!this.toolSel.includes(id)) this.toolSel.push(id); if (this.tool === 'stretch') this.pending.window = [a, b]; this.say(`${this.toolSel.length} selected — Enter to continue`); this.invalidate(); return; }
    if (!add) this.selection.clear(); for (const id of ids) this.selection.add(id);
    this.onSelectionChanged?.(); this.invalidate();
  }
  /** A click on empty space opens a window / crossing box here (object selection, cutting-edge selection). */
  canBox() { return this.tool === 'select' || (this.needsSelection() && this.step === 0) || this.isTrim(); }
  endBox() { this.boxClick = false; this.dragStart = null; if (this.dragMode === 'box') this.dragMode = 'none'; this.invalidate(); }

  // ---------------------------------------------------------------- trim / extend
  isTrim() { return this.tool === 'trim' || this.tool === 'extend'; }
  /** TRIM removes, EXTEND lengthens; Shift swaps the two for one pick (AutoCAD). */
  private trimExtends(shift: boolean) { return (this.tool === 'extend') !== shift; }
  private trimPrompt() {
    const ex = this.tool === 'extend'; const name = ex ? 'EXTEND' : 'TRIM';
    if (this.pending.erase) return `${name} — Select objects to erase (click / fence), Enter = back:`;
    return `${name} [${this.trimMode === 'quick' ? 'Quick' : 'Standard'}] — Select object to ${ex ? 'extend' : 'trim'}, Shift = ${ex ? 'trim' : 'extend'}${this.pending.edges ? ` [${this.pending.edges.length} edges]` : ''}  [T edges / F fence / C crossing / O mode / R erase / U undo], Enter = end:`;
  }
  private edgePrompt() { return `${this.tool.toUpperCase()} — Select ${this.tool === 'extend' ? 'boundary' : 'cutting'} edges (click / window / crossing), Enter when done (none = every object):`; }
  /** Two clicks on empty space: a crossing box (Standard mode, or after C) or a fence line (Quick mode, or after F). */
  trimCrossing(): boolean { return this.pending.crossing ?? (this.trimMode === 'standard'); }
  setTrimMode(m: 'quick' | 'standard') { this.trimMode = m; try { localStorage.setItem('nx.trimmode', m); } catch { } }
  /** One pick per object crossed by the path (the first crossing counts). */
  fencePathPicks(path: Pt[]): [string, Pt][] { const out: [string, Pt][] = []; const seen = new Set<string>(); for (let i = 0; i + 1 < path.length; i++) for (const k of this.fencePicks(path[i], path[i + 1])) if (!seen.has(k[0])) { seen.add(k[0]); out.push(k); } return out; }
  trimAt(picks: [string, Pt][], shift: boolean) {
    if (!picks.length) return;
    if (this.pending.erase) { bridge.call('edit.delete', { ids: picks.map(k => k[0]) }); this.say(`ERASE ${picks.length}`); this.pending.did = true; this.invalidate(); return; }
    const extend = this.trimExtends(shift); const args: any = { ids: picks.map(k => k[0]), pts: picks.flatMap(k => k[1]), extend }; if (this.pending.edges) args.edges = this.pending.edges;
    if (this.trimMode === 'quick' && !this.pending.edges) args.quick = true;   // Quick mode: an object that nothing cuts is erased (AutoCAD 2021+)
    this.pending.did = true; this.pending.preview = null; this.previewKey = '';
    bridge.call('edit.trim', args).then((r: any) => {
      const m: string[] = r?.messages ?? []; const n = picks.length - m.length;
      if (n > 0) this.say(`${extend ? 'EXTEND' : 'TRIM'} ${n} object(s)`);
      if (m.length) this.say(`${extend ? 'EXTEND' : 'TRIM'} — ${Array.from(new Set(m)).join('; ')}`);
      this.invalidate();
    }).catch(() => { });
  }
  /** Fence a → b: one pick per object the segment crosses, at the crossing point. */
  fencePicks(a: Pt, b: Pt): [string, Pt][] {
    const out: [string, Pt][] = []; if (!this.doc) return out;
    for (const id of this.doc.query(a[0], a[1], b[0], b[1])) {
      const e = this.doc.ents.get(id); if (!e || !this.doc.visible(e) || this.doc.layer(e.l)?.locked) continue; const g = this.doc.geomOf(id); if (!g) continue;
      let hit: Pt | null = null;
      for (const part of g.parts) { for (let i = 0; i + 3 < part.segs.length && !hit; i += 4) hit = segInt(a[0], a[1], b[0], b[1], part.segs[i], part.segs[i + 1], part.segs[i + 2], part.segs[i + 3]); if (hit) break; }
      if (hit) out.push([id, hit]);
    }
    return out;
  }
  /** Hover preview: ask the engine what the pick would remove / add (the latest answer wins). */
  private trimHover(p: Pt, shift: boolean) {
    if (!this.doc) return; const id = this.doc.hit(p[0], p[1], this.pxTol()); const extend = this.trimExtends(shift);
    if (!id) { if (this.pending.preview) { this.pending.preview = null; this.invalidate(); } this.previewKey = ''; this.previewSeq++; return; }
    const q = this.pxTol(3); const key = `${id}|${Math.round(p[0] / q)}|${Math.round(p[1] / q)}|${extend}|${this.doc.ver}`; if (key === this.previewKey) return; this.previewKey = key;
    const seq = ++this.previewSeq; const args: any = { h: id, x: p[0], y: p[1], extend };   // not 'id': that key is the request number of the bridge message
    if (this.pending.edges) args.edges = this.pending.edges;
    bridge.call('edit.trimPreview', args).then((r: any) => { if (seq !== this.previewSeq || !this.isTrim()) return; this.pending.preview = r?.ok ? { pts: r.pts, extend } : null; this.invalidate(); }).catch(() => { });
  }
  setSelection(ids: string[]) { this.selection = new Set(ids); this.onSelectionChanged?.(); this.invalidate(); }

  // ---------------------------------------------------------------- tools
  needsSelection() { return ['move', 'copy', 'erase', 'rotate', 'mirror', 'stretch'].includes(this.tool); }
  toolWantsPoint() {
    if (this.tool === 'select' || this.tool === 'pan' || this.isTrim()) return false;
    if (this.needsSelection() && this.step === 0) return false;
    if (this.tool === 'offset' && this.step <= 1) return false;
    if (this.tool === 'text' || this.tool === 'layer' || this.tool === 'zoomopt') return false;
    if (this.tool === 'polygon' && this.step === 0) return false;
    if (this.tool === 'dtext' && this.step >= 1) return false;
    if (this.tool === 'mtext' && this.step >= 2) return false;
    return true;
  }
  say(text: string) { this.onHistory?.(xl(text)); }
  prompt(text: string) { this.onPrompt?.(xl(text)); }
  cancel() { this.tool = 'select'; this.step = 0; this.pts = []; this.toolSel = []; this.pending = {}; this.trackPts = []; this.trk = null; this.boxClick = false; this.dragStart = null; if (this.dragMode === 'box') this.dragMode = 'none'; this.prompt('Command:'); this.invalidate(); }
  /** Start a command by name or alias (command line / toolbar). Returns false when unknown. */
  run(cmd: string, arg?: string): boolean {
    const c = cmd.trim().toLowerCase(); if (!c) return true;
    const name = ALIASES[c] ?? c;
    if (this.onCommand?.(name, arg)) return true;
    switch (name) {
      case 'cancel': this.cancel(); return true;
      case 'zoomext': this.zoomExtents(); this.say('ZOOM Extents'); return true;
      case 'zoom': { const a = (arg ?? '').trim().toLowerCase(); if (a === '') { this.start('zoomopt'); return true; } return this.zoomOption(a); }
      case 'regen': { this.doc?.geom.clear(); if (this.doc) this.doc.ver++; this.rend.setDoc(this.doc); this.regenCount++; this.invalidate(); this.say('REGEN — geometry and render buffers rebuilt'); return true; }
      case 'redraw': this.invalidate(); this.say('REDRAW'); return true;
      case 'ortho': case 'polar': case 'grid': case 'snap': case 'osnap': case 'otrack': {
        const a = (arg ?? '').trim().toLowerCase(); const n = parseFloat(a);
        if (name === 'polar' && n > 0) { this.polarInc = n; this.toggleMode('polar', true); return true; }
        if ((name === 'grid' || name === 'snap') && n > 0) { this.gridSpacing = n; this.toggleMode(name, true); return true; }
        this.toggleMode(name, a === 'on' ? true : a === 'off' ? false : undefined); return true;
      }
      case 'rightclick': { const a = (arg ?? '').trim().toLowerCase(); const order = ['timed', 'enter', 'menu'] as const; this.rightClickMode = a.startsWith('t') ? 'timed' : a.startsWith('e') ? 'enter' : a.startsWith('m') ? 'menu' : order[(order.indexOf(this.rightClickMode) + 1) % 3]; try { localStorage.setItem('nx.rightclick', this.rightClickMode); } catch { } this.say(`Right click = ${this.rightClickMode === 'timed' ? `time-sensitive (< ${RIGHT_MS} ms Enter, longer = menu)` : this.rightClickMode === 'menu' ? 'menu' : 'Enter (confirm / repeat)'}`); return true; }
      case 'props': this.onFocusRail?.('props'); return true;
      case 'dist': case 'id': this.start(name); return true;
      case 'selectall': if (this.doc) { this.selection = new Set(this.doc.order.filter(h => this.doc!.visible(this.doc!.ents.get(h)!))); this.onSelectionChanged?.(); this.invalidate(); } return true;
      case 'undo': bridge.call('undo'); return true;
      case 'redo': bridge.call('redo'); return true;
      case 'select': this.cancel(); return true;
      case 'pan': this.tool = 'pan'; this.prompt('PAN — drag, Esc to end'); return true;
      case 'text': { const id = Array.from(this.selection)[0]; const e = id ? this.doc?.ents.get(id) : null; if (e && (e.t === 'T' || e.t === 'M')) this.editText(id!); else this.say('Select a TEXT or MTEXT first (or double-click it)'); return true; }
      case 'line': case 'pline': case 'circle': case 'move': case 'copy': case 'erase': case 'rotate': case 'mirror': case 'stretch': case 'offset': case 'dim': case 'rect': case 'arc': case 'ellipse': case 'polygon': case 'point': case 'dtext': case 'mtext': case 'trim': case 'extend': this.start(name, arg); return true;
    }
    return false;
  }
  /** ZOOM option: A / E (extents), W (window), P (previous), or a scale factor. */
  zoomOption(a: string): boolean {
    if (a === 'e' || a === 'extents' || a === 'a' || a === 'all') { this.zoomExtents(); this.say('ZOOM ' + (a.startsWith('a') ? 'All' : 'Extents')); this.cancel(); return true; }
    if (a === 'w' || a === 'window') { this.start('zoomwin'); return true; }
    if (a === 'p' || a === 'previous') { this.zoomPrevious(); this.say('ZOOM Previous'); this.cancel(); return true; }
    const f = parseFloat(a); if (f > 0) { this.pushView(); this.view.scale *= f; this.invalidate(); } this.cancel(); return true;
  }
  start(name: string, arg?: string) {
    this.endBox(); this.tool = name; this.step = 0; this.pts = []; this.pending = {}; if (name !== 'zoomwin' && name !== 'zoomopt') this.lastTool = name;
    this.toolSel = this.needsSelection() ? Array.from(this.selection) : [];
    if (name === 'erase' && this.toolSel.length) { this.finishErase(); return; }
    if (this.needsSelection()) { if (this.toolSel.length) { this.step = 1; this.afterSelection(); } else this.prompt(`${name.toUpperCase()} — Select objects (click / window / crossing), Enter to continue:`); return; }
    switch (name) {
      case 'trim': case 'extend': if (this.trimMode === 'standard') { this.step = 1; this.prompt(this.edgePrompt()); } else this.prompt(this.trimPrompt()); break;
      case 'line': this.prompt('LINE — Specify first point:'); break;
      case 'pline': this.prompt('PLINE — Specify start point:'); break;
      case 'circle': this.prompt('CIRCLE — Specify center point:'); break;
      case 'rect': this.prompt('RECTANG — Specify first corner point:'); break;
      case 'arc': this.prompt('ARC — Specify start point:'); break;
      case 'ellipse': this.prompt('ELLIPSE — Specify axis endpoint:'); break;
      case 'polygon': { const n = parseInt(arg ?? '', 10); if (n >= 3) { this.lastSides = n; this.step = 1; this.prompt(`POLYGON ${n} sides — Specify center of polygon:`); } else this.prompt(`POLYGON — Enter number of sides <${this.lastSides}>:`); break; }
      case 'point': this.prompt('POINT — Specify a point:'); break;
      case 'dtext': this.prompt(`TEXT — Specify start point of text (height ${fmtNum(this.lastTextH, 3)}):`); break;
      case 'mtext': this.prompt('MTEXT — Specify first corner:'); break;
      case 'offset': { const d = parseFloat(arg ?? ''); if (d > 0) { this.lastDist = d; this.step = 1; this.prompt(`OFFSET ${d} — Select object to offset:`); } else this.prompt(`OFFSET — Specify offset distance <${fmtNum(this.lastDist, 3)}>:`); break; }
      case 'dim': this.prompt('DIMLINEAR — Specify first extension line origin:'); break;
      case 'zoomwin': this.prompt('ZOOM Window — first corner:'); break;
      case 'zoomopt': this.prompt('ZOOM — [All/Extents/Window/Previous] <Extents>:'); break;
      case 'dist': this.prompt('DIST — Specify first point:'); break;
      case 'id': this.prompt('ID — Specify point:'); break;
    }
    this.invalidate();
  }
  afterSelection() {
    switch (this.tool) {
      case 'move': this.prompt(`MOVE ${this.toolSel.length} — Specify base point:`); break;
      case 'copy': this.prompt(`COPY ${this.toolSel.length} — Specify base point:`); break;
      case 'rotate': this.prompt(`ROTATE ${this.toolSel.length} — Specify base point:`); break;
      case 'mirror': this.prompt(`MIRROR ${this.toolSel.length} — Specify first point of mirror line:`); break;
      case 'stretch': this.prompt(`STRETCH ${this.toolSel.length} — Specify base point:`); break;
      case 'erase': this.finishErase(); break;
    }
  }
  /** Enter / right click: finish the selection step or the current polyline etc. */
  enter() {
    if (this.isTrim()) {
      if (this.boxClick) { this.endBox(); return; }
      if (this.pending.erase) { this.pending.erase = false; this.prompt(this.trimPrompt()); return; }
      if (this.step === 1) { this.pending.edges = this.toolSel.length ? this.toolSel.slice() : null; this.step = 0; this.say(this.pending.edges ? `${this.pending.edges.length} edge(s) chosen` : 'every object is an edge'); this.prompt(this.trimPrompt()); this.invalidate(); return; }
      if (this.trimMode === 'quick' && !this.pending.edges && !this.pending.did && !this.pending.entered) { this.pending.entered = true; this.say('Every visible object is a cutting edge — pick the part to ' + (this.tool === 'extend' ? 'extend' : 'trim')); return; }   // classic habit: TR Enter Enter
      this.cancel(); return;
    }
    if (this.needsSelection() && this.step === 0) { if (!this.toolSel.length) { this.cancel(); return; } this.step = 1; this.afterSelection(); return; }
    if (this.tool === 'pline') { this.finishPline(false); return; }
    if (this.tool === 'polygon' && this.step === 0) { this.step = 1; this.prompt(`POLYGON ${this.lastSides} sides — Specify center of polygon:`); return; }
    if (this.tool === 'dtext' && this.step === 1) { this.step = 2; this.prompt(`TEXT — Specify rotation angle of text <${fmtNum(this.lastTextRot, 3)}>:`); return; }
    if (this.tool === 'dtext' && this.step === 2) { this.step = 3; this.prompt('TEXT — Enter text (Enter on an empty line ends):'); return; }
    if (this.tool === 'mtext' && this.step === 2) { this.finishMText(); return; }
    if (this.tool === 'line') { this.cancel(); return; }
    if (this.tool === 'offset' && this.step === 0) { this.step = 1; this.prompt(`OFFSET ${fmtNum(this.lastDist, 3)} — Select object to offset:`); return; }
    if (this.tool === 'mirror' && this.step === 3) { this.finishMirror(false); return; }
    if (this.tool === 'copy' && this.step >= 2) { this.cancel(); return; }
    this.cancel();
  }
  /** Typed input on the command line while a tool runs: coordinates "x,y", relative "@dx,dy", numbers (distance / radius / angle), "c" (close), "y/n". */
  typed(text: string): boolean {
    const t = text.trim(); if (!t) { this.enter(); return true; }
    if (this.tool === 'select') return false;
    const low = t.toLowerCase();
    if (this.tool === 'zoomopt') { this.zoomOption(low); return true; }
    if (this.isTrim()) {
      if (this.step === 0 && (low === 't' || low === 'b' || low === 'edges')) { this.endBox(); this.step = 1; this.toolSel = []; this.pending.preview = null; this.pending.erase = false; this.prompt(this.edgePrompt()); this.invalidate(); return true; }
      if (low === 'o' || low === 'mode' || low === 'q' || low === 'quick' || low === 's' || low === 'standard') {
        const to = low.startsWith('q') ? 'quick' : low.startsWith('s') ? 'standard' : this.trimMode === 'quick' ? 'standard' : 'quick'; this.setTrimMode(to); this.endBox();
        this.say(to === 'quick' ? 'Mode = Quick: every object is an edge; an object that nothing cuts is erased' : 'Mode = Standard: choose the edges first (Enter = every object)');
        this.toolSel = []; this.pending = {}; if (to === 'standard') { this.step = 1; this.prompt(this.edgePrompt()); } else { this.step = 0; this.prompt(this.trimPrompt()); } this.invalidate(); return true;
      }
      if (this.step === 0 && (low === 'f' || low === 'fence')) { this.pending.crossing = false; this.say('Fence: click two points on empty space, or drag across the objects'); return true; }
      if (this.step === 0 && (low === 'c' || low === 'crossing')) { this.pending.crossing = true; this.say('Crossing: click two corners on empty space'); return true; }
      if (this.step === 0 && (low === 'r' || low === 'erase')) { this.endBox(); this.pending.erase = true; this.pending.preview = null; this.prompt(this.trimPrompt()); this.invalidate(); return true; }
      if (low === 'u' || low === 'undo') { bridge.call('undo'); this.say('UNDO'); return true; }
      return false;
    }
    if (this.tool === 'pline' && (low === 'c' || low === 'close')) { this.finishPline(true); return true; }
    if (this.tool === 'mirror' && this.step === 3) { this.finishMirror(low.startsWith('y')); return true; }
    if (this.tool === 'offset' && this.step === 0) { const d = parseFloat(t); if (d > 0) { this.lastDist = d; this.step = 1; this.prompt(`OFFSET ${d} — Select object to offset:`); } return true; }
    if (this.tool === 'circle' && this.step === 1) { const r = parseFloat(t); if (r > 0) { this.finishCircle(r); } return true; }
    if (this.tool === 'rotate' && this.step === 2) { const a = parseFloat(t); if (isFinite(a)) this.finishRotate(a); return true; }
    if (this.tool === 'polygon' && this.step === 0) { const n = parseInt(t, 10); if (n >= 3 && n <= 1024) { this.lastSides = n; this.step = 1; this.prompt(`POLYGON ${n} sides — Specify center of polygon:`); } else this.say('POLYGON — number of sides 3 … 1024'); return true; }
    if (this.tool === 'polygon' && this.step === 2) {
      if (low === 'i' || low === 'c') { this.pending.inscribed = low === 'i'; this.prompt(this.polygonPrompt()); return true; }
      const r = parseFloat(t); if (r > 0 && this.parsePoint(t) === null) { this.finishPolygon(r, null); return true; }
    }
    if (this.tool === 'ellipse' && this.step === 2) { const d = parseFloat(t); if (d > 0 && this.parsePoint(t) === null) { this.finishEllipse(d); return true; } }
    if (this.tool === 'dtext' && this.step === 1) { const hh = parseFloat(t); if (hh > 0) { this.lastTextH = hh; this.step = 2; this.prompt(`TEXT — Specify rotation angle of text <${fmtNum(this.lastTextRot, 3)}>:`); } return true; }
    if (this.tool === 'dtext' && this.step === 2) { const a = parseFloat(t); if (isFinite(a)) { this.lastTextRot = a; this.step = 3; this.prompt('TEXT — Enter text (Enter on an empty line ends):'); } return true; }
    if (this.tool === 'dtext' && this.step === 3) { this.textLine(text); return true; }
    if (this.tool === 'mtext' && this.step === 2) { this.pending.lines = (this.pending.lines ?? []).concat([text]); this.prompt(`MTEXT — line ${this.pending.lines.length + 1} (Enter on an empty line ends):`); return true; }
    const p = this.parsePoint(t);
    if (p) { this.point(p, false); return true; }
    const n = parseFloat(t);
    const base = this.basePoint();
    if (isFinite(n) && /^-?[0-9.]+$/.test(t) && !base && this.cursor && this.toolWantsPoint() && this.trk && this.trk.guides.length === 1 && this.trk.guides[0].kind === 'track') {
      // distance typed while a tracking path shows: measured from the acquired point toward the cursor
      const g = this.trk.guides[0]; const sgn = (this.cursor[0] - g.ax) * g.dx + (this.cursor[1] - g.ay) * g.dy >= 0 ? 1 : -1; this.point([g.ax + g.dx * n * sgn, g.ay + g.dy * n * sgn], false); return true;
    }
    if (isFinite(n) && /^-?[\d.]+$/.test(t) && base && this.cursor && ['line', 'pline', 'move', 'copy', 'stretch', 'dist', 'dim'].includes(this.tool)) {
      // direct distance entry: the number goes along the rubber band as constrained now (ortho / polar / Shift)
      const c = this.constrain(this.cursor, this.shiftDown); const dx = c[0] - base[0], dy = c[1] - base[1]; const len = Math.hypot(dx, dy) || 1; this.point([base[0] + dx / len * n, base[1] + dy / len * n], false); return true;
    }
    return false;
  }
  /** Coordinate entry: absolute "x,y", relative "@dx,dy", polar "@length<angle" (from the last point) or "length<angle" (from the origin). */
  parsePoint(t: string): Pt | null {
    const rel = t.startsWith('@'); const body = (rel ? t.slice(1) : t).trim();
    const from = (): Pt => rel ? (this.basePoint() ?? this.pts[this.pts.length - 1] ?? [0, 0]) : [0, 0];
    let m = body.match(/^(-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?)\s*,\s*(-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?)$/i);
    if (m) { const o = from(); return [o[0] + parseFloat(m[1]), o[1] + parseFloat(m[2])]; }
    m = body.match(/^(-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?)\s*<\s*(-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?)$/i);
    if (m) { const o = from(); const len = parseFloat(m[1]), a = parseFloat(m[2]) * Math.PI / 180; return [o[0] + len * Math.cos(a), o[1] + len * Math.sin(a)]; }
    if (rel && body === '') return from();   // "@" = last point
    return null;
  }
  point(p: Pt, shift: boolean) {
    this.trackPts = []; this.trk = null;   // acquired points last until the point is picked (AutoCAD)
    switch (this.tool) {
      case 'line':
        this.pts.push(p);
        if (this.pts.length >= 2) { const a = this.pts[this.pts.length - 2]; bridge.call('draw.line', { x0: a[0], y0: a[1], x1: p[0], y1: p[1] }); this.say(`LINE ${fmtNum(a[0], 3)},${fmtNum(a[1], 3)} → ${fmtNum(p[0], 3)},${fmtNum(p[1], 3)}`); }
        this.prompt('LINE — Specify next point (Enter to end):'); break;
      case 'pline': this.pts.push(p); this.prompt(`PLINE ${this.pts.length} pts — Specify next point (Enter = end, C = close):`); break;
      case 'circle': if (this.step === 0) { this.pts = [p]; this.step = 1; this.prompt(`CIRCLE — Specify radius (point or value) <${fmtNum(this.lastRadius, 3)}>:`); } else this.finishCircle(Math.hypot(p[0] - this.pts[0][0], p[1] - this.pts[0][1])); break;
      case 'move': case 'copy': case 'stretch':
        if (this.step === 1) { this.pts = [p]; this.step = 2; this.prompt(`${this.tool.toUpperCase()} — Specify second point:`); }
        else {
          const dx = p[0] - this.pts[0][0], dy = p[1] - this.pts[0][1];
          if (this.tool === 'move') { bridge.call('edit.move', { ids: this.toolSel, dx, dy }); this.say(`MOVE ${this.toolSel.length} by ${fmtNum(dx, 3)},${fmtNum(dy, 3)}`); this.setSelection(this.toolSel); this.cancel(); }
          else if (this.tool === 'copy') { bridge.call('edit.copy', { ids: this.toolSel, dx, dy }); this.say(`COPY ${this.toolSel.length} by ${fmtNum(dx, 3)},${fmtNum(dy, 3)}`); this.prompt('COPY — Specify second point for another copy (Enter to end):'); }
          else { const win = this.pending.window; if (!win) { this.say('STRETCH needs a crossing window selection'); this.cancel(); break; } bridge.call('edit.stretch', { ids: this.toolSel, x0: win[0][0], y0: win[0][1], x1: win[1][0], y1: win[1][1], dx, dy }); this.say(`STRETCH ${this.toolSel.length} by ${fmtNum(dx, 3)},${fmtNum(dy, 3)}`); this.setSelection(this.toolSel); this.cancel(); }
        }
        break;
      case 'rotate': if (this.step === 1) { this.pts = [p]; this.step = 2; this.prompt('ROTATE — Specify rotation angle (value or point):'); } else { const a = Math.atan2(p[1] - this.pts[0][1], p[0] - this.pts[0][0]) * 180 / Math.PI; this.finishRotate(a); } break;
      case 'mirror': if (this.step === 1) { this.pts = [p]; this.step = 2; this.prompt('MIRROR — Specify second point of mirror line:'); } else if (this.step === 2) { this.pts.push(p); this.step = 3; this.prompt('MIRROR — Erase source objects? [Yes/No] <N>:'); } break;
      case 'offset': if (this.step === 2) { bridge.call('edit.offset', { id: this.pending.id, dist: this.lastDist, sx: p[0], sy: p[1] }); this.say(`OFFSET ${fmtNum(this.lastDist, 3)}`); this.step = 1; this.prompt(`OFFSET ${fmtNum(this.lastDist, 3)} — Select object to offset (Enter to end):`); } break;
      case 'dim':
        this.pts.push(p);
        if (this.pts.length === 1) this.prompt('DIMLINEAR — Specify second extension line origin:');
        else if (this.pts.length === 2) this.prompt('DIMLINEAR — Specify dimension line location:');
        else { const [a, b, c] = this.pts; const horiz = Math.abs(c[1] - (a[1] + b[1]) / 2) >= Math.abs(c[0] - (a[0] + b[0]) / 2) || Math.abs(a[1] - b[1]) < 1e-9; const angle = horiz ? 0 : 90; bridge.call('draw.dim', { x0: a[0], y0: a[1], x1: b[0], y1: b[1], dx: c[0], dy: c[1], angle }); this.say(`DIMLINEAR ${angle === 0 ? 'horizontal' : 'vertical'}`); this.cancel(); }
        break;
      case 'rect':
        if (this.pts.length === 0) { this.pts = [p]; this.prompt('RECTANG — Specify other corner point (or @dx,dy):'); }
        else { const a = this.pts[0]; if (Math.abs(p[0] - a[0]) < 1e-12 || Math.abs(p[1] - a[1]) < 1e-12) { this.say('RECTANG — the corners must differ in X and Y'); break; } bridge.call('draw.pline', { pts: rectPts(a, p).flat(), closed: true }); this.say(`RECTANG ${fmtNum(Math.abs(p[0] - a[0]), 3)} × ${fmtNum(Math.abs(p[1] - a[1]), 3)}`); this.cancel(); }
        break;
      case 'arc':
        this.pts.push(p);
        if (this.pts.length === 1) this.prompt('ARC — Specify second point of arc:');
        else if (this.pts.length === 2) this.prompt('ARC — Specify end point of arc:');
        else { const [a, b, c] = this.pts; const g = arc3(a, b, c); if (!g) { this.say('ARC — the three points are on one line'); this.pts.pop(); break; } bridge.call('draw.arc', { cx: g.cx, cy: g.cy, r: g.r, a0: g.a0, a1: g.a1 }); this.say(`ARC r ${fmtNum(g.r, 3)} ${fmtNum(g.a0, 2)}° → ${fmtNum(g.a1, 2)}°`); this.cancel(); }
        break;
      case 'ellipse':
        if (this.step === 0) { this.pts = [p]; this.step = 1; this.prompt('ELLIPSE — Specify other endpoint of axis:'); }
        else if (this.step === 1) { if (Math.hypot(p[0] - this.pts[0][0], p[1] - this.pts[0][1]) < 1e-12) break; this.pts.push(p); this.step = 2; this.prompt('ELLIPSE — Specify distance to other axis (point or value):'); }
        else { const c = this.ellipseCenter()!; this.finishEllipse(Math.hypot(p[0] - c[0], p[1] - c[1])); }
        break;
      case 'polygon':
        if (this.step === 1) { this.pts = [p]; this.step = 2; if (this.pending.inscribed === undefined) this.pending.inscribed = true; this.prompt(this.polygonPrompt()); }
        else if (this.step === 2) { const c = this.pts[0]; const r = Math.hypot(p[0] - c[0], p[1] - c[1]); if (r > 0) this.finishPolygon(r, Math.atan2(p[1] - c[1], p[0] - c[0]) * 180 / Math.PI); }
        break;
      case 'point': bridge.call('draw.point', { x: p[0], y: p[1] }); this.say(`POINT ${fmtNum(p[0], 3)},${fmtNum(p[1], 3)}`); this.cancel(); break;
      case 'dtext': if (this.step === 0) { this.pts = [p]; this.step = 1; this.prompt(`TEXT — Specify height <${fmtNum(this.lastTextH, 3)}>:`); } break;
      case 'mtext':
        if (this.step === 0) { this.pts = [p]; this.step = 1; this.prompt('MTEXT — Specify opposite corner (sets the width):'); }
        else if (this.step === 1) { this.pts.push(p); this.step = 2; this.pending.lines = []; this.prompt('MTEXT — Enter text, line 1 (Enter on an empty line ends):'); }
        break;
      case 'id': this.say(`ID  X = ${fmtNum(p[0], 4)}   Y = ${fmtNum(p[1], 4)}   Z = 0`); this.cancel(); break;
      case 'dist':
        this.pts.push(p);
        if (this.pts.length === 1) this.prompt('DIST — Specify second point:');
        else { const [a, b] = this.pts; const dx = b[0] - a[0], dy = b[1] - a[1]; this.say(`DIST  Distance = ${fmtNum(Math.hypot(dx, dy), 4)}   Angle in XY = ${fmtNum(Math.atan2(dy, dx) * 180 / Math.PI, 4)}°   Delta X = ${fmtNum(dx, 4)}   Delta Y = ${fmtNum(dy, 4)}`); this.cancel(); }
        break;
      case 'zoomwin': this.pts.push(p); if (this.pts.length === 2) { this.zoomWindow(this.pts[0], this.pts[1]); this.cancel(); } else this.prompt('ZOOM Window — opposite corner:'); break;
    }
    this.invalidate();
  }
  finishCircle(r: number) { if (r <= 0) return; this.lastRadius = r; const c = this.pts[0]; bridge.call('draw.circle', { cx: c[0], cy: c[1], r }); this.say(`CIRCLE ${fmtNum(c[0], 3)},${fmtNum(c[1], 3)} r ${fmtNum(r, 3)}`); this.cancel(); }
  ellipseCenter(): Pt | null { if (this.pts.length < 2) return this.pts.length === 1 ? this.pts[0] : null; const [a, b] = this.pts; return [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2]; }
  finishEllipse(other: number) { const [a, b] = this.pts; const g = ellipseByAxis(a, b, other); if (!g) return; bridge.call('draw.ellipse', g); this.say(`ELLIPSE ratio ${fmtNum(g.ratio, 4)}`); this.cancel(); }
  polygonPrompt() { return `POLYGON ${this.lastSides} sides — ${this.pending.inscribed === false ? 'Circumscribed' : 'Inscribed'}: specify radius (point or value), I = inscribed, C = circumscribed:`; }
  /** angleDeg null = typed radius: AutoCAD puts the bottom edge horizontal. */
  finishPolygon(r: number, angleDeg: number | null) {
    const n = this.lastSides, ins = this.pending.inscribed !== false, c = this.pts[0];
    const a = angleDeg ?? (ins ? -90 - 180 / n : -90);
    bridge.call('draw.pline', { pts: polygonPts(n, c, r, ins, a).flat(), closed: true }); this.say(`POLYGON ${n} sides, ${ins ? 'inscribed' : 'circumscribed'} r ${fmtNum(r, 3)}`); this.cancel();
  }
  /** One line of TEXT: create it, then move down one line and ask for the next (AutoCAD DTEXT); an empty line ends. */
  textLine(text: string) {
    if (text.trim() === '') { this.cancel(); return; }
    const p = this.pts[0], h = this.lastTextH, rot = this.lastTextRot;
    bridge.call('draw.text', { x: p[0], y: p[1], h, rot, text }); this.say(`TEXT "${text.slice(0, 40)}"`);
    const ang = (rot - 90) * Math.PI / 180, step = h * 1.6667; this.pts = [[p[0] + step * Math.cos(ang), p[1] + step * Math.sin(ang)]];
    this.prompt('TEXT — next line (Enter on an empty line ends):'); this.invalidate();
  }
  finishMText() {
    const lines: string[] = this.pending.lines ?? []; if (!lines.length || this.pts.length < 2) { this.cancel(); return; }
    const [a, b] = this.pts; const x = Math.min(a[0], b[0]), y = Math.max(a[1], b[1]), w = Math.abs(b[0] - a[0]);
    bridge.call('draw.mtext', { x, y, h: this.lastTextH, w, text: lines.map(escMText).join(MTEXT_NL) }); this.say(`MTEXT ${lines.length} line(s)`); this.cancel();
  }
  strokeWorld(ctx: CanvasRenderingContext2D, pts: Pt[], closed: boolean) { if (pts.length < 2) return; ctx.beginPath(); pts.forEach((q, i) => { const s = w2s(this.view, q[0], q[1]); if (i === 0) ctx.moveTo(s[0], s[1]); else ctx.lineTo(s[0], s[1]); }); if (closed) ctx.closePath(); ctx.stroke(); }
  /** The command line should keep spaces (text entry), not treat Space as Enter. */
  wantsText() { return (this.tool === 'dtext' && this.step === 3) || (this.tool === 'mtext' && this.step === 2); }
  finishPline(close: boolean) { if (this.pts.length >= 2) { bridge.call('draw.pline', { pts: this.pts.flat(), closed: close }); this.say(`PLINE ${this.pts.length} vertices${close ? ' (closed)' : ''}`); } this.cancel(); }
  finishRotate(deg: number) { const c = this.pts[0]; bridge.call('edit.rotate', { ids: this.toolSel, cx: c[0], cy: c[1], deg }); this.say(`ROTATE ${this.toolSel.length} by ${fmtNum(deg, 3)}°`); this.setSelection(this.toolSel); this.cancel(); }
  finishMirror(erase: boolean) { const [a, b] = this.pts; bridge.call('edit.mirror', { ids: this.toolSel, x0: a[0], y0: a[1], x1: b[0], y1: b[1], keep: !erase }); this.say(`MIRROR ${this.toolSel.length}${erase ? ' (source erased)' : ''}`); this.cancel(); }
  finishErase() { const ids = this.toolSel.length ? this.toolSel : Array.from(this.selection); if (ids.length) { bridge.call('edit.delete', { ids }); this.say(`ERASE ${ids.length}`); } this.selection.clear(); this.onSelectionChanged?.(); this.cancel(); }

  // ---------------------------------------------------------------- text editing (double-click)
  editText(id: string) {
    const e = this.doc?.ents.get(id); if (!e || !this.doc) return;
    const g = this.doc.geomOf(id); const t = g?.texts[0]; if (!t) return;
    const box = textBox(t); const s = w2s(this.view, box[0], box[3]);
    const ta = document.createElement('textarea'); ta.className = 'textedit'; ta.value = e.t === 'T' ? (e.s ?? '') : (e.lines ?? []).map(l => l.map(r => r.t).join('')).join('\n');
    ta.style.left = Math.max(4, s[0]) + 'px'; ta.style.top = Math.max(4, s[1]) + 'px'; ta.style.width = Math.max(160, (box[2] - box[0]) * this.view.scale + 20) + 'px'; ta.rows = e.t === 'T' ? 1 : Math.max(2, (e.lines?.length ?? 1) + 1);
    this.wrap.append(ta); this.textEditEl = ta; ta.focus(); ta.select();
    const done = (commit: boolean) => { if (!this.textEditEl) return; const v = ta.value; ta.remove(); this.textEditEl = null; if (commit && v !== (e.t === 'T' ? e.s : undefined)) { bridge.call('edit.text', { id, text: e.t === 'T' ? v.replace(/\r?\n/g, ' ') : v, plain: e.t === 'M' }); this.say(`TEXT edited: ${v.slice(0, 40)}`); } this.wrap.focus(); };
    ta.addEventListener('keydown', ev => { ev.stopPropagation(); if (ev.key === 'Escape') done(false); else if (ev.key === 'Enter' && (e.t === 'T' || ev.ctrlKey)) { ev.preventDefault(); done(true); } });
    ta.addEventListener('blur', () => done(true));
    this.onDblClickText?.(id);
  }

  // ---------------------------------------------------------------- drawing
  drawNow() {
    const t0 = performance.now();
    this.rend.draw(this.view, ctx => this.overlay(ctx));
    const ms = performance.now() - t0; this.frameTimes.push(ms); if (this.frameTimes.length > 200) this.frameTimes.shift();
    this.dirtyStats = `${ms.toFixed(1)} ms · ${(this.rend.lastVertexCount / 2).toLocaleString()} segs · ${this.rend.drawnTexts} texts`;
    document.dispatchEvent(new CustomEvent('nx-frame', { detail: { ms, segs: this.rend.lastVertexCount / 2, texts: this.rend.drawnTexts, build: this.rend.buildMs } }));
  }
  overlay(ctx: CanvasRenderingContext2D) {
    const v = this.view; const doc = this.doc; const T = this.tokens(); const sel = T.sel; const snapCol = T.snap;
    if (this.gridOn) this.drawGrid(ctx, T.grid);
    // selection highlight (thicker, accent2)
    if (doc) {
      ctx.lineWidth = 2; ctx.strokeStyle = sel; ctx.fillStyle = sel; let n = 0;
      const ids = this.tool !== 'select' && this.toolSel.length ? this.toolSel : Array.from(this.selection);
      for (const id of ids) { if (++n > 5000) break; const g = doc.geomOf(id); if (!g) continue; this.strokeGeom(ctx, g, v, 0, 0, 1, 0, 0); for (const t of g.texts) { const b = textBox(t); const a = w2s(v, b[0], b[3]), c = w2s(v, b[2], b[1]); ctx.strokeRect(a[0], a[1], c[0] - a[0], c[1] - a[1]); } }
      // grips
      ctx.fillStyle = T.grip;
      for (const id of ids.slice(0, 200)) { const g = doc.geomOf(id); if (!g) continue; for (let i = 0; i + 2 < g.snaps.length; i += 3) if (g.snaps[i + 2] === SNAP_END || g.snaps[i + 2] === SNAP_INS || g.snaps[i + 2] === SNAP_CEN) { const p = w2s(v, g.snaps[i], g.snaps[i + 1]); ctx.fillRect(p[0] - 3, p[1] - 3, 6, 6); } }
    }
    // rubber band / previews
    ctx.lineWidth = 1; ctx.setLineDash([]);
    const cur = this.cursor && this.toolWantsPoint() ? this.effectivePoint(this.cursor, this.shiftDown) : this.cursor;
    if (cur && this.pts.length && doc) {
      const last = this.pts[this.pts.length - 1]; const a = w2s(v, last[0], last[1]), b = w2s(v, cur[0], cur[1]); ctx.strokeStyle = sel;
      if (this.tool === 'line' || this.tool === 'pline' || this.tool === 'mirror' || this.tool === 'dim' && this.pts.length < 2) { ctx.beginPath(); if (this.tool === 'pline') { for (let i = 0; i < this.pts.length; i++) { const p = w2s(v, this.pts[i][0], this.pts[i][1]); if (i === 0) ctx.moveTo(p[0], p[1]); else ctx.lineTo(p[0], p[1]); } } else ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); }
      else if (this.tool === 'circle') { const r = Math.hypot(cur[0] - last[0], cur[1] - last[1]) * v.scale; ctx.beginPath(); ctx.arc(a[0], a[1], r, 0, Math.PI * 2); ctx.stroke(); }
      else if ((this.tool === 'rect' || this.tool === 'mtext' && this.step === 1) && this.pts.length === 1) { ctx.strokeRect(Math.min(a[0], b[0]), Math.min(a[1], b[1]), Math.abs(b[0] - a[0]), Math.abs(b[1] - a[1])); }
      else if (this.tool === 'arc') { const g = this.pts.length === 2 ? arc3(this.pts[0], this.pts[1], cur) : null; if (g) this.strokeWorld(ctx, arcPts(g), false); else { ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); } }
      else if (this.tool === 'ellipse' && this.step === 1) { ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); }
      else if (this.tool === 'ellipse' && this.step === 2) { const c = this.ellipseCenter()!; const g = ellipseByAxis(this.pts[0], this.pts[1], Math.hypot(cur[0] - c[0], cur[1] - c[1])); if (g) this.strokeWorld(ctx, ellipsePts(g), true); }
      else if (this.tool === 'polygon' && this.step === 2) { const c = this.pts[0]; const r = Math.hypot(cur[0] - c[0], cur[1] - c[1]); if (r > 0) this.strokeWorld(ctx, polygonPts(this.lastSides, c, r, this.pending.inscribed !== false, Math.atan2(cur[1] - c[1], cur[0] - c[0]) * 180 / Math.PI), true); }
      else if (this.tool === 'dim' && this.pts.length === 2) { const [p1, p2] = this.pts; const horiz = Math.abs(cur[1] - (p1[1] + p2[1]) / 2) >= Math.abs(cur[0] - (p1[0] + p2[0]) / 2); const s1 = w2s(v, horiz ? p1[0] : cur[0], horiz ? cur[1] : p1[1]), s2 = w2s(v, horiz ? p2[0] : cur[0], horiz ? cur[1] : p2[1]); const q1 = w2s(v, p1[0], p1[1]), q2 = w2s(v, p2[0], p2[1]); ctx.beginPath(); ctx.moveTo(q1[0], q1[1]); ctx.lineTo(s1[0], s1[1]); ctx.lineTo(s2[0], s2[1]); ctx.lineTo(q2[0], q2[1]); ctx.stroke(); }
      else if ((this.tool === 'move' || this.tool === 'copy' || this.tool === 'stretch') && this.step === 2) {
        const dx = cur[0] - last[0], dy = cur[1] - last[1]; ctx.strokeStyle = sel; ctx.setLineDash([4, 3]); ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); ctx.setLineDash([]);
        ctx.globalAlpha = 0.7; let n = 0; for (const id of this.toolSel) { if (++n > 2000) break; const g = doc.geomOf(id); if (g) this.strokeGeom(ctx, g, v, dx, dy, 1, 0, 0); } ctx.globalAlpha = 1;
      }
      else if (this.tool === 'rotate' && this.step === 2) { const ang = Math.atan2(cur[1] - last[1], cur[0] - last[0]); ctx.setLineDash([4, 3]); ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); ctx.setLineDash([]); ctx.globalAlpha = 0.7; let n = 0; for (const id of this.toolSel) { if (++n > 2000) break; const g = doc.geomOf(id); if (g) this.strokeGeom(ctx, g, v, 0, 0, 1, ang, 0, last); } ctx.globalAlpha = 1; }
    }
    if (this.tool === 'mirror' && this.step === 3 && this.pts.length === 2 && doc) { const [p0, p1] = this.pts; ctx.strokeStyle = sel; ctx.globalAlpha = 0.7; let n = 0; for (const id of this.toolSel) { if (++n > 2000) break; const g = doc.geomOf(id); if (g) this.strokeGeomMirror(ctx, g, v, p0, p1); } ctx.globalAlpha = 1; }
    // AutoTrack alignment paths (polar / ortho ray from the base point, tracking paths through acquired points) and acquired-point marks
    const wants = this.toolWantsPoint();
    if (wants && this.trackPts.length) { ctx.strokeStyle = T.track; ctx.lineWidth = 1.5; ctx.beginPath(); for (const a of this.trackPts) { const p = w2s(v, a[0], a[1]); ctx.moveTo(p[0] - 5, p[1]); ctx.lineTo(p[0] + 5, p[1]); ctx.moveTo(p[0], p[1] - 5); ctx.lineTo(p[0], p[1] + 5); } ctx.stroke(); ctx.lineWidth = 1; }
    if (wants && cur && this.trk && !(this.snapPt && this.snapOn)) {
      const far = (v.w + v.h) * 2; ctx.strokeStyle = T.track; ctx.setLineDash([2, 4]); ctx.beginPath();
      for (const g of this.trk.guides) { const a = w2s(v, g.ax, g.ay); const ux = g.dx, uy = -g.dy; if (g.kind === 'track') ctx.moveTo(a[0] - ux * far, a[1] - uy * far); else ctx.moveTo(a[0], a[1]); ctx.lineTo(a[0] + ux * far, a[1] + uy * far); }
      ctx.stroke(); ctx.setLineDash([]);
      if (this.trk.guides.length > 1) { const x = w2s(v, this.trk.pt[0], this.trk.pt[1]); ctx.beginPath(); ctx.moveTo(x[0] - 4, x[1] - 4); ctx.lineTo(x[0] + 4, x[1] + 4); ctx.moveTo(x[0] + 4, x[1] - 4); ctx.lineTo(x[0] - 4, x[1] + 4); ctx.stroke(); }
      if (this.trk.label && this.cursorScreen) { ctx.font = '11px ui-monospace, Consolas, monospace'; const tw = ctx.measureText(this.trk.label).width; let lx = this.cursorScreen[0] + 16, ly = this.cursorScreen[1] + 18; if (lx + tw + 12 > v.w) lx = this.cursorScreen[0] - tw - 28; if (ly + 20 > v.h) ly = this.cursorScreen[1] - 30; ctx.fillStyle = T.tipBg; ctx.fillRect(lx, ly, tw + 12, 18); ctx.strokeStyle = T.track; ctx.strokeRect(lx + 0.5, ly + 0.5, tw + 11, 17); ctx.fillStyle = T.tip; ctx.textBaseline = 'middle'; ctx.fillText(this.trk.label, lx + 6, ly + 9.5); ctx.textBaseline = 'alphabetic'; }
    }
    // TRIM / EXTEND hover preview: the part that goes (dashed) or comes (solid)
    if (this.isTrim() && this.pending.preview && this.dragMode === 'none') { const pv = this.pending.preview; const pts: Pt[] = []; for (let i = 0; i + 1 < pv.pts.length; i += 2) pts.push([pv.pts[i], pv.pts[i + 1]]); ctx.strokeStyle = sel; ctx.lineWidth = 3; ctx.setLineDash(pv.extend ? [] : [3, 4]); this.strokeWorld(ctx, pts, false); ctx.setLineDash([]); ctx.lineWidth = 1; }
    // fence (TRIM / EXTEND drag)
    const fenceRect = this.isTrim() && this.step === 0 && this.boxClick && this.trimCrossing();
    if (this.isTrim() && this.step === 0 && this.dragMode === 'box' && this.dragStart && this.cursor && !fenceRect) { ctx.strokeStyle = T.crossing; ctx.setLineDash([4, 3]); ctx.beginPath(); const a = w2s(v, this.dragStart[0], this.dragStart[1]); ctx.moveTo(a[0], a[1]); for (const q of this.boxClick ? [] : this.fencePath) { const s = w2s(v, q[0], q[1]); ctx.lineTo(s[0], s[1]); } const b = w2s(v, this.cursor[0], this.cursor[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); ctx.setLineDash([]); }
    // selection box
    if (this.dragMode === 'box' && this.dragStart && this.cursor && (fenceRect || !(this.isTrim() && this.step === 0))) { const a = w2s(v, this.dragStart[0], this.dragStart[1]), b = w2s(v, this.cursor[0], this.cursor[1]); const crossing = this.cursor[0] < this.dragStart[0]; ctx.strokeStyle = crossing ? T.crossing : T.window; ctx.setLineDash(crossing ? [4, 3] : []); ctx.strokeRect(a[0], a[1], b[0] - a[0], b[1] - a[1]); ctx.setLineDash([]); ctx.fillStyle = crossing ? T.crossingFill : T.windowFill; ctx.fillRect(a[0], a[1], b[0] - a[0], b[1] - a[1]); }
    // snap marker
    if (this.snapPt && this.snapOn && this.toolWantsPoint()) { const p = w2s(v, this.snapPt.x, this.snapPt.y); ctx.strokeStyle = snapCol; ctx.lineWidth = 2; const k = this.snapPt.k; ctx.beginPath();
      if (k === SNAP_END) ctx.rect(p[0] - 6, p[1] - 6, 12, 12); else if (k === SNAP_MID) { ctx.moveTo(p[0], p[1] - 7); ctx.lineTo(p[0] + 7, p[1] + 6); ctx.lineTo(p[0] - 7, p[1] + 6); ctx.closePath(); } else if (k === SNAP_CEN) ctx.arc(p[0], p[1], 6, 0, Math.PI * 2); else if (k === SNAP_INT) { ctx.moveTo(p[0] - 6, p[1] - 6); ctx.lineTo(p[0] + 6, p[1] + 6); ctx.moveTo(p[0] + 6, p[1] - 6); ctx.lineTo(p[0] - 6, p[1] + 6); } else if (k === SNAP_QUAD) { ctx.moveTo(p[0], p[1] - 7); ctx.lineTo(p[0] + 7, p[1]); ctx.lineTo(p[0], p[1] + 7); ctx.lineTo(p[0] - 7, p[1]); ctx.closePath(); } else { ctx.rect(p[0] - 5, p[1] - 5, 10, 10); ctx.moveTo(p[0] - 8, p[1]); ctx.lineTo(p[0] + 8, p[1]); }
      ctx.stroke(); ctx.lineWidth = 1; }
    // crosshair
    if (this.cursorScreen && this.cursor && this.dragMode !== 'pan') { const [sx, sy] = this.cursorScreen; ctx.strokeStyle = T.cursor; ctx.beginPath(); ctx.moveTo(sx - 30, sy); ctx.lineTo(sx + 30, sy); ctx.moveTo(sx, sy - 30); ctx.lineTo(sx, sy + 30); ctx.stroke(); ctx.strokeRect(sx - 4, sy - 4, 8, 8); }
  }
  /** Overlay colours for the canvas background (CSS tokens on .canvas-wrap, so they follow the canvas theme, not the app theme). */
  private _tok: Record<string, string> | null = null; private _tokDark: boolean | null = null;
  tokens(): Record<string, string> {
    if (this._tok && this._tokDark === this.dark) return this._tok;
    const cs = getComputedStyle(this.wrap); const g = (n: string, d: string) => cs.getPropertyValue(n).trim() || d;
    this._tok = { sel: g('--canvas-sel', '#E8601C'), snap: g('--canvas-snap', '#2E9E5B'), grid: g('--canvas-grid', '#E4E1DA'), cursor: g('--canvas-crosshair', 'rgba(23,24,26,0.3)'), grip: g('--canvas-grip', '#17181A'), track: g('--canvas-track', '#2E9E5B'), window: g('--canvas-window', '#2F6FD6'), windowFill: g('--canvas-window-fill', 'rgba(47,111,214,0.08)'), crossing: g('--canvas-crossing', '#2E9E5B'), crossingFill: g('--canvas-crossing-fill', 'rgba(46,158,91,0.08)'), tip: g('--canvas-coord', '#5F5E5A'), tipBg: g('--canvas-coord-bg', 'rgba(251,250,248,0.85)') };
    this._tokDark = this.dark; return this._tok;
  }
  /** F7 grid: dots at the grid spacing, coarsened ×5 / ×10 until they are at least 10 px apart. */
  private drawGrid(ctx: CanvasRenderingContext2D, col: string) {
    const v = this.view; let g = this.gridSpacing; if (!(g > 0)) return; let k = 0; while (g * v.scale < 10 && k++ < 12) g *= k % 2 ? 5 : 2;
    const [x0, y1] = s2w(v, 0, 0), [x1, y0] = s2w(v, v.w, v.h); const nx = Math.ceil((x1 - x0) / g), ny = Math.ceil((y1 - y0) / g); if (nx * ny > 40000) return;
    ctx.fillStyle = col; const ix0 = Math.floor(x0 / g), iy0 = Math.floor(y0 / g);
    for (let i = ix0; i <= ix0 + nx; i++) for (let j = iy0; j <= iy0 + ny; j++) { const p = w2s(v, i * g, j * g); ctx.fillRect(p[0] - 1, p[1] - 1, 2, 2); }
  }
  private strokeGeom(ctx: CanvasRenderingContext2D, g: { parts: { segs: Float32Array }[]; fills: { poly: Float32Array }[] }, v: View, dx: number, dy: number, k: number, ang: number, _z: number, c?: Pt) {
    const cr = Math.cos(ang), sr = Math.sin(ang);
    const T = (x: number, y: number): [number, number] => { if (c) { const lx = x - c[0], ly = y - c[1]; x = c[0] + lx * cr - ly * sr; y = c[1] + lx * sr + ly * cr; } return w2s(v, x + dx, y + dy); };
    ctx.beginPath();
    for (const p of g.parts) for (let i = 0; i + 3 < p.segs.length; i += 4) { const a = T(p.segs[i], p.segs[i + 1]), b = T(p.segs[i + 2], p.segs[i + 3]); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); }
    for (const f of g.fills) for (let i = 0; i < f.poly.length; i += 2) { const a = T(f.poly[i], f.poly[i + 1]); if (i === 0) ctx.moveTo(a[0], a[1]); else ctx.lineTo(a[0], a[1]); }
    ctx.stroke();
  }
  private strokeGeomMirror(ctx: CanvasRenderingContext2D, g: { parts: { segs: Float32Array }[] }, v: View, p0: Pt, p1: Pt) {
    const dx = p1[0] - p0[0], dy = p1[1] - p0[1], l2 = dx * dx + dy * dy || 1; const a = (dx * dx - dy * dy) / l2, b = 2 * dx * dy / l2;
    const T = (x: number, y: number): [number, number] => { const lx = x - p0[0], ly = y - p0[1]; return w2s(v, p0[0] + a * lx + b * ly, p0[1] + b * lx - a * ly); };
    ctx.beginPath(); for (const p of g.parts) for (let i = 0; i + 3 < p.segs.length; i += 4) { const s = T(p.segs[i], p.segs[i + 1]), e = T(p.segs[i + 2], p.segs[i + 3]); ctx.moveTo(s[0], s[1]); ctx.lineTo(e[0], e[1]); } ctx.stroke();
  }
}
export function snapName(k: number) { return k === SNAP_END ? 'END' : k === SNAP_MID ? 'MID' : k === SNAP_CEN ? 'CEN' : k === SNAP_INT ? 'INT' : k === SNAP_QUAD ? 'QUA' : k === SNAP_INS ? 'INS' : ''; }
export { drawText, aciRgb };
