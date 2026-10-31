// Canvas view: viewport (pan/zoom), input, tools (select/draw/edit), snapping, overlay drawing, command line.
import { bridge } from './bridge';
import { Doc, SNAP_CEN, SNAP_END, SNAP_INT, SNAP_MID, SNAP_QUAD, SNAP_INS, textBox, Ent } from './doc';
import { Renderer, View, s2w, w2s, drawText } from './render';
import { fmtNum, h, aciRgb, rgbCss } from './util';

type Pt = [number, number];
export interface ToolSpec { name: string; prompt: string }
const ALIASES: Record<string, string> = { l: 'line', line: 'line', pl: 'pline', pline: 'pline', c: 'circle', circle: 'circle', m: 'move', move: 'move', co: 'copy', cp: 'copy', copy: 'copy', e: 'erase', erase: 'erase', del: 'erase', s: 'stretch', stretch: 'stretch', o: 'offset', offset: 'offset', mi: 'mirror', mirror: 'mirror', ro: 'rotate', rotate: 'rotate', z: 'zoom', zoom: 'zoom', ze: 'zoomext', dli: 'dim', dim: 'dim', dimlin: 'dim', dimlinear: 'dim', u: 'undo', undo: 'undo', redo: 'redo', la: 'layer', layer: 'layer', ed: 'text', text: 'text', ddedit: 'text', sel: 'select', select: 'select', esc: 'cancel', pan: 'pan', p: 'pan', regen: 'regen', re: 'redraw', redraw: 'redraw', r: 'redraw', open: 'open', new: 'new', save: 'save', saveas: 'saveas', qsave: 'save', export: 'exportdwg', pdf: 'pdf', plot: 'pdf', print: 'pdf', snap: 'snap', sn: 'snap', grid: 'grid', ortho: 'ortho', polar: 'polar', osnap: 'osnap', os: 'osnap', f3: 'osnap', all: 'selectall', pr: 'props', props: 'props', properties: 'props', di: 'dist', dist: 'dist', id: 'id', rightclick: 'rightclick' };
/** Polar tracking increments F10 / POLAR cycles through (AutoCAD POLARANG; default 90°). */
const POLAR_INCS = [90, 45];

export class CanvasView {
  el: HTMLElement; wrap: HTMLElement; glc: HTMLCanvasElement; ovc: HTMLCanvasElement; coord: HTMLElement; hint: HTMLElement;
  rend: Renderer; doc: Doc | null = null;
  view: View = { cx: 0, cy: 0, scale: 1, w: 800, h: 600, dpr: window.devicePixelRatio || 1 };
  selection = new Set<string>();
  cursor: Pt | null = null; cursorScreen: Pt | null = null; snapPt: { x: number; y: number; k: number } | null = null; snapOn = true;
  tool = 'select'; step = 0; pts: Pt[] = []; toolSel: string[] = []; pending: any = {}; lastTool = 'line';
  dragStart: Pt | null = null; dragMode: 'none' | 'pan' | 'box' = 'none'; panStart: { x: number; y: number; cx: number; cy: number } | null = null;
  onSelectionChanged: (() => void) | null = null; onPrompt: ((text: string) => void) | null = null; onHistory: ((line: string) => void) | null = null;
  onDblClickText: ((id: string) => void) | null = null; onCommand: ((name: string) => boolean) | null = null;
  frameTimes: number[] = []; raf = 0; needDraw = true; dark = false; dirtyStats = '';
  textEditEl: HTMLTextAreaElement | null = null;
  lastDist = 10; lastRadius = 10;
  /** Drafting modes (status-bar pills, AutoCAD keys): F8 ortho, F10 polar tracking, F7 grid display, F9 grid snap; Shift inverts ortho while held. */
  orthoOn = false; polarOn = false; polarInc = 90; gridOn = false; gridSnapOn = false; gridSpacing = 10; shiftDown = false;
  /** Right click: 'enter' (confirm / repeat, AutoCAD "fast right click") or 'menu' (small context menu). */
  rightClickMode: 'enter' | 'menu' = 'enter';
  onModes: (() => void) | null = null; onFocusRail: ((which: 'layers' | 'props') => void) | null = null;
  viewStack: { cx: number; cy: number; scale: number }[] = []; lastWheelPush = 0; lastMiddleDown = 0; menuEl: HTMLElement | null = null;

  constructor() {
    this.el = h(`<div class="card canvas-card"></div>`);
    this.wrap = h(`<div class="canvas-wrap" tabindex="0"></div>`);
    this.glc = document.createElement('canvas'); this.ovc = document.createElement('canvas'); this.ovc.className = 'ov';
    this.coord = h(`<div class="coord">—</div>`); this.hint = h(`<div class="drop-hint"><div><b>NexysDraft</b>DWG 또는 DXF 파일을 여기에 끌어다 놓거나 OPEN (Ctrl+O)</div></div>`);
    this.wrap.append(this.glc, this.ovc, this.hint, this.coord); this.el.append(this.wrap);
    this.rend = new Renderer(this.glc, this.ovc);
    new ResizeObserver(() => { this.resize(); }).observe(this.wrap);
    this.bind();
    try { if (localStorage.getItem('nx.rightclick') === 'menu') this.rightClickMode = 'menu'; } catch { }
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
      if (e.button === 2) { if (this.rightClickMode === 'menu') this.openMenu(s); else this.rightClick(); return; }
      if (e.button !== 0) return;
      if (this.toolWantsPoint()) { this.point(this.effectivePoint(p, e.shiftKey), e.shiftKey); return; }
      // select tool / selection step: click pick or start box
      this.dragStart = p; this.dragMode = 'box'; this.pending.shift = e.shiftKey; w.setPointerCapture(e.pointerId);
    });
    w.addEventListener('pointermove', e => {
      const s = this.toScreen(e); const p = this.toWorld(e); this.cursorScreen = s; this.cursor = p; this.shiftDown = e.shiftKey;
      if (this.dragMode === 'pan' && this.panStart) { this.view.cx = this.panStart.cx - (s[0] - this.panStart.x) / this.view.scale; this.view.cy = this.panStart.cy + (s[1] - this.panStart.y) / this.view.scale; this.invalidate(); return; }
      if (this.doc && this.snapOn && this.toolWantsPoint() && this.dragMode !== 'box') this.snapPt = this.doc.snap(p[0], p[1], this.pxTol(10), this.tool === 'move' || this.tool === 'copy' || this.tool === 'stretch' ? new Set(this.toolSel) : null); else this.snapPt = null;
      const ep = this.toolWantsPoint() ? this.effectivePoint(p, e.shiftKey) : p; const base = this.basePoint();
      this.coord.textContent = `${fmtNum(ep[0], 3)}, ${fmtNum(ep[1], 3)}` + (this.snapPt && this.snapOn && this.toolWantsPoint() ? `  ${snapName(this.snapPt.k)}` : '') + (base && this.toolWantsPoint() ? `  ${fmtNum(Math.hypot(ep[0] - base[0], ep[1] - base[1]), 3)} < ${fmtNum(Math.atan2(ep[1] - base[1], ep[0] - base[0]) * 180 / Math.PI, 2)}°` : '');
      this.invalidate();
    });
    w.addEventListener('pointerup', e => {
      if (this.dragMode === 'pan') { this.dragMode = 'none'; this.panStart = null; return; }
      if (this.dragMode === 'box' && this.dragStart) {
        const p = this.toWorld(e); const d = Math.hypot(p[0] - this.dragStart[0], p[1] - this.dragStart[1]) * this.view.scale;
        if (d < 4) this.pick(p, this.pending.shift); else this.box(this.dragStart, p, this.pending.shift);
        this.dragStart = null; this.dragMode = 'none'; this.invalidate();
      }
    });
    w.addEventListener('dblclick', e => { if (this.tool !== 'select' || !this.doc) return; const p = this.toWorld(e); const id = this.doc.hit(p[0], p[1], this.pxTol()); const ent = id ? this.doc.ents.get(id) : null; if (ent && (ent.t === 'T' || ent.t === 'M')) this.editText(id!); });
    w.addEventListener('pointerleave', () => { this.cursor = null; this.snapPt = null; this.invalidate(); });
    // keys reach key() through the document handler in main.ts (a second listener here ran Enter / Space twice)
  }
  key(e: KeyboardEvent) {
    if (this.textEditEl) return;
    if (e.key === 'Escape') { const idle = this.tool === 'select'; this.closeMenu(); this.cancel(); if (idle && this.selection.size) this.setSelection([]); e.preventDefault(); return; }
    if (e.key === 'Delete' && this.selection.size && this.tool === 'select') { this.run('erase'); e.preventDefault(); return; }
    if (e.key === 'Enter' || e.key === ' ') { if (this.tool === 'select') { this.run(this.lastTool); } else this.enter(); e.preventDefault(); return; }
  }
  /** Enter on an idle command line / right click with nothing running: repeat the last command (AutoCAD). */
  rightClick() { if (this.tool === 'select') this.run(this.lastTool); else this.enter(); }
  openMenu(s: Pt) {
    this.closeMenu(); const idle = this.tool === 'select';
    const items: [string, string][] = idle ? [['repeat', `반복 ${this.lastTool.toUpperCase()}`], ['zoomext', '전체 보기 (Z E)'], ['zoomprev', '이전 보기 (Z P)'], ['selectall', '전체 선택 (Ctrl+A)']] : [['enter', '확정 (Enter)'], ['cancel', '취소 (Esc)'], ['zoomext', '전체 보기 (Z E)']];
    const m = h(`<div class="ctxmenu">${items.map(([k, t]) => `<button type="button" data-m="${k}">${t}</button>`).join('')}</div>`);
    m.style.left = s[0] + 'px'; m.style.top = s[1] + 'px';
    m.addEventListener('pointerdown', ev => ev.stopPropagation());
    m.addEventListener('click', ev => { const k = ((ev.target as HTMLElement).closest('[data-m]') as HTMLElement | null)?.dataset.m; this.closeMenu(); if (k === 'repeat') this.run(this.lastTool); else if (k === 'enter') this.enter(); else if (k === 'cancel') this.cancel(); else if (k === 'zoomext') this.zoomExtents(); else if (k === 'zoomprev') this.zoomPrevious(); else if (k === 'selectall') this.run('selectall'); this.wrap.focus(); });
    this.wrap.append(m); this.menuEl = m;
  }
  closeMenu() { this.menuEl?.remove(); this.menuEl = null; }

  // ---------------------------------------------------------------- drafting modes and point constraints
  toggleMode(m: 'ortho' | 'polar' | 'grid' | 'snap' | 'osnap', on?: boolean) {
    switch (m) {
      case 'ortho': this.orthoOn = on ?? !this.orthoOn; if (this.orthoOn) this.polarOn = false; this.say(`<Ortho ${this.orthoOn ? 'on' : 'off'}>`); break;
      case 'polar': this.polarOn = on ?? !this.polarOn; if (this.polarOn) this.orthoOn = false; this.say(`<Polar ${this.polarOn ? 'on' : 'off'}> ${this.polarInc}°`); break;
      case 'grid': this.gridOn = on ?? !this.gridOn; this.say(`<Grid ${this.gridOn ? 'on' : 'off'}> ${fmtNum(this.gridSpacing, 4)}`); break;
      case 'snap': this.gridSnapOn = on ?? !this.gridSnapOn; this.say(`<Snap ${this.gridSnapOn ? 'on' : 'off'}> ${fmtNum(this.gridSpacing, 4)}`); break;
      case 'osnap': this.snapOn = on ?? !this.snapOn; this.say(`<Osnap ${this.snapOn ? 'on' : 'off'}>`); break;
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
    }
    return null;
  }
  /** Raw cursor → picked point: object snap wins (AutoCAD), else grid snap (F9), then ortho (F8, inverted while Shift is held) or polar tracking (F10). */
  effectivePoint(p: Pt, shift: boolean): Pt {
    if (this.snapPt && this.snapOn) return [this.snapPt.x, this.snapPt.y];
    let q: Pt = p;
    if (this.gridSnapOn && this.gridSpacing > 0) { const g = this.gridSpacing; q = [Math.round(q[0] / g) * g, Math.round(q[1] / g) * g]; }
    return this.constrain(q, shift);
  }
  constrain(q: Pt, shift: boolean): Pt {
    const base = this.basePoint(); if (!base || this.tool === 'circle') return q;
    const dx = q[0] - base[0], dy = q[1] - base[1];
    if (this.orthoOn !== shift) return Math.abs(dx) >= Math.abs(dy) ? [q[0], base[1]] : [base[0], q[1]];
    if (this.polarOn && (dx || dy)) {
      const inc = this.polarInc * Math.PI / 180; const a = Math.atan2(dy, dx); const k = Math.round(a / inc); const ta = k * inc;
      if (Math.abs(a - ta) <= 5 * Math.PI / 180) { const len = dx * Math.cos(ta) + dy * Math.sin(ta); const c = Math.round(Math.cos(ta) * 1e12) / 1e12, s = Math.round(Math.sin(ta) * 1e12) / 1e12; return [base[0] + len * c, base[1] + len * s]; }
    }
    return q;
  }

  // ---------------------------------------------------------------- selection
  pick(p: Pt, add: boolean) {
    if (!this.doc) return; const id = this.doc.hit(p[0], p[1], this.pxTol());
    if (this.tool !== 'select' && this.step === 0 && this.needsSelection()) { if (id) { this.toolSel.includes(id) ? this.toolSel.splice(this.toolSel.indexOf(id), 1) : this.toolSel.push(id); this.say(`${this.toolSel.length} selected — Enter to continue`); } this.invalidate(); return; }
    if (this.tool === 'offset' && this.step === 1) { if (id) { this.pending.id = id; this.step = 2; this.prompt('Specify point on side to offset:'); } return; }
    if (!add) this.selection.clear();
    if (id) { if (add && this.selection.has(id)) this.selection.delete(id); else this.selection.add(id); }
    this.onSelectionChanged?.(); this.invalidate();
  }
  box(a: Pt, b: Pt, add: boolean) {
    if (!this.doc) return; const crossing = b[0] < a[0]; const ids = this.doc.select(a[0], a[1], b[0], b[1], crossing);
    if (this.tool !== 'select' && this.step === 0 && this.needsSelection()) { for (const id of ids) if (!this.toolSel.includes(id)) this.toolSel.push(id); if (this.tool === 'stretch') this.pending.window = [a, b]; this.say(`${this.toolSel.length} selected — Enter to continue`); this.invalidate(); return; }
    if (!add) this.selection.clear(); for (const id of ids) this.selection.add(id);
    this.onSelectionChanged?.(); this.invalidate();
  }
  setSelection(ids: string[]) { this.selection = new Set(ids); this.onSelectionChanged?.(); this.invalidate(); }

  // ---------------------------------------------------------------- tools
  needsSelection() { return ['move', 'copy', 'erase', 'rotate', 'mirror', 'stretch'].includes(this.tool); }
  toolWantsPoint() {
    if (this.tool === 'select' || this.tool === 'pan') return false;
    if (this.needsSelection() && this.step === 0) return false;
    if (this.tool === 'offset' && this.step <= 1) return false;
    if (this.tool === 'text' || this.tool === 'layer' || this.tool === 'zoomopt') return false;
    return true;
  }
  say(text: string) { this.onHistory?.(text); }
  prompt(text: string) { this.onPrompt?.(text); }
  cancel() { this.tool = 'select'; this.step = 0; this.pts = []; this.toolSel = []; this.pending = {}; this.prompt('Command:'); this.invalidate(); }
  /** Start a command by name or alias (command line / toolbar). Returns false when unknown. */
  run(cmd: string, arg?: string): boolean {
    const c = cmd.trim().toLowerCase(); if (!c) return true;
    const name = ALIASES[c] ?? c;
    if (this.onCommand?.(name)) return true;
    switch (name) {
      case 'cancel': this.cancel(); return true;
      case 'zoomext': this.zoomExtents(); this.say('ZOOM Extents'); return true;
      case 'zoom': { const a = (arg ?? '').trim().toLowerCase(); if (a === '') { this.start('zoomopt'); return true; } return this.zoomOption(a); }
      case 'regen': this.doc?.geom.clear(); if (this.doc) this.doc.ver++; this.invalidate(); return true;
      case 'redraw': this.invalidate(); this.say('REDRAW'); return true;
      case 'ortho': case 'polar': case 'grid': case 'snap': case 'osnap': {
        const a = (arg ?? '').trim().toLowerCase(); const n = parseFloat(a);
        if (name === 'polar' && n > 0) { this.polarInc = n; this.toggleMode('polar', true); return true; }
        if ((name === 'grid' || name === 'snap') && n > 0) { this.gridSpacing = n; this.toggleMode(name, true); return true; }
        this.toggleMode(name, a === 'on' ? true : a === 'off' ? false : undefined); return true;
      }
      case 'rightclick': { const a = (arg ?? '').trim().toLowerCase(); this.rightClickMode = a === 'menu' ? 'menu' : a === 'enter' ? 'enter' : this.rightClickMode === 'menu' ? 'enter' : 'menu'; try { localStorage.setItem('nx.rightclick', this.rightClickMode); } catch { } this.say(`Right click = ${this.rightClickMode === 'menu' ? 'menu' : 'Enter (confirm / repeat)'}`); return true; }
      case 'props': this.onFocusRail?.('props'); return true;
      case 'dist': case 'id': this.start(name); return true;
      case 'selectall': if (this.doc) { this.selection = new Set(this.doc.order.filter(h => this.doc!.visible(this.doc!.ents.get(h)!))); this.onSelectionChanged?.(); this.invalidate(); } return true;
      case 'undo': bridge.call('undo'); return true;
      case 'redo': bridge.call('redo'); return true;
      case 'select': this.cancel(); return true;
      case 'pan': this.tool = 'pan'; this.prompt('PAN — drag, Esc to end'); return true;
      case 'text': { const id = Array.from(this.selection)[0]; const e = id ? this.doc?.ents.get(id) : null; if (e && (e.t === 'T' || e.t === 'M')) this.editText(id!); else this.say('Select a TEXT or MTEXT first (or double-click it)'); return true; }
      case 'line': case 'pline': case 'circle': case 'move': case 'copy': case 'erase': case 'rotate': case 'mirror': case 'stretch': case 'offset': case 'dim': this.start(name, arg); return true;
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
    this.tool = name; this.step = 0; this.pts = []; this.pending = {}; if (name !== 'zoomwin' && name !== 'zoomopt') this.lastTool = name;
    this.toolSel = this.needsSelection() ? Array.from(this.selection) : [];
    if (name === 'erase' && this.toolSel.length) { this.finishErase(); return; }
    if (this.needsSelection()) { if (this.toolSel.length) { this.step = 1; this.afterSelection(); } else this.prompt(`${name.toUpperCase()} — Select objects (click / window / crossing), Enter to continue:`); return; }
    switch (name) {
      case 'line': this.prompt('LINE — Specify first point:'); break;
      case 'pline': this.prompt('PLINE — Specify start point:'); break;
      case 'circle': this.prompt('CIRCLE — Specify center point:'); break;
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
    if (this.needsSelection() && this.step === 0) { if (!this.toolSel.length) { this.cancel(); return; } this.step = 1; this.afterSelection(); return; }
    if (this.tool === 'pline') { this.finishPline(false); return; }
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
    if (this.tool === 'pline' && (low === 'c' || low === 'close')) { this.finishPline(true); return true; }
    if (this.tool === 'mirror' && this.step === 3) { this.finishMirror(low.startsWith('y')); return true; }
    if (this.tool === 'offset' && this.step === 0) { const d = parseFloat(t); if (d > 0) { this.lastDist = d; this.step = 1; this.prompt(`OFFSET ${d} — Select object to offset:`); } return true; }
    if (this.tool === 'circle' && this.step === 1) { const r = parseFloat(t); if (r > 0) { this.finishCircle(r); } return true; }
    if (this.tool === 'rotate' && this.step === 2) { const a = parseFloat(t); if (isFinite(a)) this.finishRotate(a); return true; }
    const p = this.parsePoint(t);
    if (p) { this.point(p, false); return true; }
    const n = parseFloat(t);
    const base = this.basePoint();
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
      else if (this.tool === 'dim' && this.pts.length === 2) { const [p1, p2] = this.pts; const horiz = Math.abs(cur[1] - (p1[1] + p2[1]) / 2) >= Math.abs(cur[0] - (p1[0] + p2[0]) / 2); const s1 = w2s(v, horiz ? p1[0] : cur[0], horiz ? cur[1] : p1[1]), s2 = w2s(v, horiz ? p2[0] : cur[0], horiz ? cur[1] : p2[1]); const q1 = w2s(v, p1[0], p1[1]), q2 = w2s(v, p2[0], p2[1]); ctx.beginPath(); ctx.moveTo(q1[0], q1[1]); ctx.lineTo(s1[0], s1[1]); ctx.lineTo(s2[0], s2[1]); ctx.lineTo(q2[0], q2[1]); ctx.stroke(); }
      else if ((this.tool === 'move' || this.tool === 'copy' || this.tool === 'stretch') && this.step === 2) {
        const dx = cur[0] - last[0], dy = cur[1] - last[1]; ctx.strokeStyle = sel; ctx.setLineDash([4, 3]); ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); ctx.setLineDash([]);
        ctx.globalAlpha = 0.7; let n = 0; for (const id of this.toolSel) { if (++n > 2000) break; const g = doc.geomOf(id); if (g) this.strokeGeom(ctx, g, v, dx, dy, 1, 0, 0); } ctx.globalAlpha = 1;
      }
      else if (this.tool === 'rotate' && this.step === 2) { const ang = Math.atan2(cur[1] - last[1], cur[0] - last[0]); ctx.setLineDash([4, 3]); ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); ctx.setLineDash([]); ctx.globalAlpha = 0.7; let n = 0; for (const id of this.toolSel) { if (++n > 2000) break; const g = doc.geomOf(id); if (g) this.strokeGeom(ctx, g, v, 0, 0, 1, ang, 0, last); } ctx.globalAlpha = 1; }
    }
    if (this.tool === 'mirror' && this.step === 3 && this.pts.length === 2 && doc) { const [p0, p1] = this.pts; ctx.strokeStyle = sel; ctx.globalAlpha = 0.7; let n = 0; for (const id of this.toolSel) { if (++n > 2000) break; const g = doc.geomOf(id); if (g) this.strokeGeomMirror(ctx, g, v, p0, p1); } ctx.globalAlpha = 1; }
    // polar / ortho tracking ray from the base point
    const base = this.basePoint();
    if (base && cur && this.toolWantsPoint() && (this.polarOn || this.orthoOn !== this.shiftDown) && !(this.snapPt && this.snapOn)) { const a = w2s(v, base[0], base[1]), b = w2s(v, cur[0], cur[1]); const dx = b[0] - a[0], dy = b[1] - a[1], l = Math.hypot(dx, dy); if (l > 1) { ctx.strokeStyle = T.track; ctx.setLineDash([2, 4]); ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(a[0] + dx / l * 4000, a[1] + dy / l * 4000); ctx.stroke(); ctx.setLineDash([]); } }
    // selection box
    if (this.dragMode === 'box' && this.dragStart && this.cursor) { const a = w2s(v, this.dragStart[0], this.dragStart[1]), b = w2s(v, this.cursor[0], this.cursor[1]); const crossing = this.cursor[0] < this.dragStart[0]; ctx.strokeStyle = crossing ? T.crossing : T.window; ctx.setLineDash(crossing ? [4, 3] : []); ctx.strokeRect(a[0], a[1], b[0] - a[0], b[1] - a[1]); ctx.setLineDash([]); ctx.fillStyle = crossing ? T.crossingFill : T.windowFill; ctx.fillRect(a[0], a[1], b[0] - a[0], b[1] - a[1]); }
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
    this._tok = { sel: g('--canvas-sel', '#E8601C'), snap: g('--canvas-snap', '#2E9E5B'), grid: g('--canvas-grid', '#E4E1DA'), cursor: g('--canvas-crosshair', 'rgba(23,24,26,0.3)'), grip: g('--canvas-grip', '#17181A'), track: g('--canvas-track', '#2E9E5B'), window: g('--canvas-window', '#2F6FD6'), windowFill: g('--canvas-window-fill', 'rgba(47,111,214,0.08)'), crossing: g('--canvas-crossing', '#2E9E5B'), crossingFill: g('--canvas-crossing-fill', 'rgba(46,158,91,0.08)') };
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
