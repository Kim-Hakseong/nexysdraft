// window.nx.test — automation hooks for the host self-test: steps run as real DOM events on the page, results go back through the bridge.
import { bridge } from './bridge';
import { Doc } from './doc';
import { w2s } from './render';
import { CanvasView } from './view';
import { lang } from './i18n';

export function installTests(ctx: { view: CanvasView; cmd: any; getDoc: () => Doc | null; getState: () => any; layers: any; props: any }) {
  const v = ctx.view;
  const nextFrame = () => new Promise<void>(r => requestAnimationFrame(() => requestAnimationFrame(() => r())));
  const settle = async (ms = 60) => { await nextFrame(); await new Promise(r => setTimeout(r, ms)); await nextFrame(); };
  const rect = () => v.wrap.getBoundingClientRect();
  const ptr = (type: string, sx: number, sy: number, opts: any = {}) => { const r = rect(); v.wrap.dispatchEvent(new PointerEvent(type, { bubbles: true, clientX: r.left + sx, clientY: r.top + sy, button: opts.button ?? 0, buttons: opts.buttons ?? (type === 'pointerup' ? 0 : 1), shiftKey: !!opts.shift, altKey: !!opts.alt, pointerId: 1, pointerType: 'mouse' })); };
  const clickWorld = async (x: number, y: number, opts: any = {}) => { const [sx, sy] = w2s(v.view, x, y); ptr('pointermove', sx, sy, { buttons: 0 }); await nextFrame(); ptr('pointerdown', sx, sy, opts); ptr('pointerup', sx, sy, opts); await settle(20); };
  const dragWorld = async (x0: number, y0: number, x1: number, y1: number, opts: any = {}) => { const a = w2s(v.view, x0, y0), b = w2s(v.view, x1, y1); ptr('pointermove', a[0], a[1], { buttons: 0 }); ptr('pointerdown', a[0], a[1], opts); for (let i = 1; i <= 5; i++) ptr('pointermove', a[0] + (b[0] - a[0]) * i / 5, a[1] + (b[1] - a[1]) * i / 5, opts); ptr('pointerup', b[0], b[1], opts); await settle(20); };
  const frames = async (n: number, step: () => void) => { const out: number[] = []; for (let i = 0; i < n; i++) { step(); const t0 = performance.now(); v.drawNow(); v.needDraw = false; out.push(performance.now() - t0); await nextFrame(); } return out; };
  const T = {
    async run(tid: number, fn: () => Promise<any>) { let result: any; try { result = await fn(); } catch (e: any) { result = { error: String(e?.message ?? e) }; } bridge.post({ cmd: 'test', tid, result }); },
    settle, nextFrame,
    doc: () => ctx.getDoc(), state: () => ctx.getState(), view: v,
    stats() { const d = ctx.getDoc(); return { phase: d?.phase, ents: d?.order.length ?? 0, layers: d?.layers.length ?? 0, bbox: d?.bbox, selection: Array.from(v.selection), tool: v.tool, segs: v.rend.lastVertexCount / 2, texts: v.rend.drawnTexts, buildMs: v.rend.buildMs, frameMs: v.frameTimes.slice(-1)[0], webgl: !!v.rend.gl, drawnTexts: v.rend.drawnTexts }; },
    async zoomExtents() { v.zoomExtents(); await settle(); return v.view; },
    async wheel(n: number, dir = 1) { const r = rect(); const out: number[] = []; for (let i = 0; i < n; i++) { v.wrap.dispatchEvent(new WheelEvent('wheel', { bubbles: true, cancelable: true, clientX: r.left + r.width / 2 + i * 3, clientY: r.top + r.height / 2, deltaY: dir * (i % 2 === 0 ? -100 : 100) })); const t0 = performance.now(); v.drawNow(); v.needDraw = false; out.push(performance.now() - t0); await nextFrame(); } return out; },
    async pan(n: number) { return frames(n, () => v.panBy(7, 3)); },
    async zoomSteps(n: number) { return frames(n, () => v.zoomAt(v.view.w / 2, v.view.h / 2, 1.05)); },
    async dragPan(px: number) { const r = rect(); const cx = r.width / 2, cy = r.height / 2; const out: number[] = []; ptr('pointerdown', cx, cy, { button: 1, buttons: 4 }); for (let i = 1; i <= 10; i++) { ptr('pointermove', cx + px * i / 10, cy + px * i / 20, { buttons: 4 }); const t0 = performance.now(); v.drawNow(); v.needDraw = false; out.push(performance.now() - t0); await nextFrame(); } ptr('pointerup', cx + px, cy + px / 2, { button: 1, buttons: 0 }); return out; },
    clickWorld, dragWorld,
    /** One raw pointer event at a world point (no waiting): for timing the handlers. */
    raw(type: string, x: number, y: number, opts: any = {}) { const [sx, sy] = w2s(v.view, x, y); ptr(type, sx, sy, opts); },
    /** A hurried click: pressed at the world point, released `dx`,`dy` screen pixels away with no pause. */
    sloppyClick(x: number, y: number, dx: number, dy: number, opts: any = {}) { const [sx, sy] = w2s(v.view, x, y); ptr('pointermove', sx, sy, { buttons: 0 }); ptr('pointerdown', sx, sy, opts); ptr('pointermove', sx + dx, sy + dy, opts); ptr('pointerup', sx + dx, sy + dy, opts); return Array.from(v.selection); },
    /** Pointer move to a world point without a click → the point a click would pick and the tracking state. */
    async moveWorld(x: number, y: number, opts: any = {}) { const [sx, sy] = w2s(v.view, x, y); ptr('pointermove', sx, sy, { buttons: 0, shift: opts.shift }); await settle(opts.wait ?? 20); const ep = v.cursor && v.toolWantsPoint() ? v.effectivePoint(v.cursor, !!opts.shift) : v.cursor; return { pt: ep, trk: v.trk, trackPts: v.trackPts, snap: v.snapPt, box: v.boxClick, preview: v.pending.preview ?? null }; },
    hover() { return v.hoverId; },
    box() { return { boxClick: v.boxClick, dragMode: v.dragMode, start: v.dragStart, toolSel: v.toolSel.slice(), selection: Array.from(v.selection) }; },
    /** Wheel (middle button) double-click on the canvas centre — AutoCAD zoom extents. */
    async middleDouble() { const r = rect(); const cx = r.width / 2, cy = r.height / 2; for (let i = 0; i < 2; i++) { ptr('pointerdown', cx, cy, { button: 1, buttons: 4 }); ptr('pointerup', cx, cy, { button: 1, buttons: 0 }); await new Promise(res => setTimeout(res, 60)); } await settle(30); return { cx: v.view.cx, cy: v.view.cy, scale: v.view.scale }; },
    modes() { return { ortho: v.orthoOn, polar: v.polarOn, polarInc: v.polarInc, grid: v.gridOn, snap: v.gridSnapOn, osnap: v.snapOn, otrack: v.otrackOn, tool: v.tool, lastTool: v.lastTool, pills: Array.from(document.querySelectorAll('.ftr .modes button')).map(b => (b as HTMLElement).dataset.mode + ':' + b.classList.contains('on')) }; },
    /** Page CSS pixel of a world point (for pixel sampling in a capture), plus the device pixel ratio and an empty background probe point. */
    screenOf(x: number, y: number) { const r = rect(); const [sx, sy] = w2s(v.view, x, y); return { x: r.left + sx, y: r.top + sy, bgx: r.left + 30, bgy: r.top + r.height / 2, dpr: window.devicePixelRatio || 1, dark: v.dark }; },
    /** Right button pressed for `ms` milliseconds at the canvas centre (real pointer events, real elapsed time) → what the view did and whether a menu is open. */
    async rightPress(ms: number) { const r = rect(); const cx = r.width / 2, cy = r.height / 2; v.closeMenu(); v.lastRight = ''; ptr('pointerdown', cx, cy, { button: 2, buttons: 2 }); await new Promise(res => setTimeout(res, ms)); ptr('pointerup', cx, cy, { button: 2, buttons: 0 }); await settle(30); const menu = !!v.menuEl; const out = { did: v.lastRight, menu, tool: v.tool, mode: v.rightClickMode }; v.closeMenu(); return out; },
    history() { return ctx.cmd.log.slice(-40); },
    lang() { return lang(); },
    /** What the page shows in the current language (header and toolbar labels, card titles, prompt) and the side panel widths. */
    ui() { const q = (s: string) => (document.querySelector(s) as HTMLElement | null); const w = (s: string) => { const e = q(s); return e ? Math.round(e.getBoundingClientRect().width) : -1; }; const tb = q('.toolbar')!; return { lang: document.documentElement.lang, open: q('.hdr [data-a=open] span')?.textContent, line: q('.toolbar [data-tool=line]')?.getAttribute('title'), file: q('.aside .card-h .t')?.textContent, props: q('.aside.right .card:last-child .card-h .t')?.textContent, prompt: ctx.cmd.pr.textContent, left: w('.main > .aside:not(.right)'), right: w('.main > .aside.right'), canvas: w('.canvas-wrap'), toolbarOverflow: tb.scrollWidth > tb.clientWidth + 1, toolbarH: Math.round(tb.getBoundingClientRect().height), tools: tb.querySelectorAll('[data-tool]').length, toolsVisible: Array.from(tb.querySelectorAll('[data-tool]')).filter(b => { const r = b.getBoundingClientRect(), t = tb.getBoundingClientRect(); return r.width > 0 && r.right <= t.right + 1 && r.bottom <= t.bottom + 1; }).length }; },
    async cmd(text: string) { ctx.cmd.input.value = text; ctx.cmd.input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); await settle(30); return v.tool; },
    async key(key: string, opts: any = {}) { document.dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, ctrlKey: !!opts.ctrl, shiftKey: !!opts.shift })); await settle(30); },
    async selectWindow(x0: number, y0: number, x1: number, y1: number) { v.cancel(); await dragWorld(x0, y0, x1, y1); return Array.from(v.selection); },
    async pick(x: number, y: number, shift = false) { await clickWorld(x, y, { shift }); return Array.from(v.selection); },
    async dblclick(x: number, y: number) { const [sx, sy] = w2s(v.view, x, y); const r = rect(); v.wrap.dispatchEvent(new MouseEvent('dblclick', { bubbles: true, clientX: r.left + sx, clientY: r.top + sy })); await settle(30); return !!v.textEditEl; },
    async typeText(text: string, ctrlEnter = false) { const ta = v.textEditEl; if (!ta) return false; ta.value = text; ta.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, ctrlKey: ctrlEnter })); await settle(60); return true; },
    snapAt(x: number, y: number) { const d = ctx.getDoc(); if (!d) return null; return d.snap(x, y, 10 / v.view.scale, null); },
    hitAt(x: number, y: number) { const d = ctx.getDoc(); return d ? d.hit(x, y, 8 / v.view.scale) : null; },
    async hoverWorld(x: number, y: number) { const [sx, sy] = w2s(v.view, x, y); ptr('pointermove', sx, sy, { buttons: 0 }); await settle(20); return v.snapPt; },
    texts() { return v.rend.texts.map(t => ({ h: t.h0, text: t.mtext ? (t.lines ?? []).map(l => l.map(r => r.t).join('')).join('\n') : t.text, font: t.font })); },
    layerClick(name: string, toggle?: string) { const row = ctx.layers.body.querySelector(`[data-l="${CSS.escape(name)}"]`); if (!row) return false; (toggle ? row.querySelector(`[data-t=${toggle}]`) : row).dispatchEvent(new MouseEvent('click', { bubbles: true })); return true; },
    probe() { return (window as any).nxProbe ? (window as any).nxProbe() : null; },
    async setLayerRailName(name: string) { ctx.layers.input.value = name; ctx.layers.input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true })); await settle(60); },
    async saveCheck(preferDwg = true) { const { showLossThenSave } = await import('./rails'); await showLossThenSave(preferDwg); await settle(80); const d = document.querySelector('.save-check'); if (!d) return null; const res = { title: d.querySelector('h3')!.textContent, primary: (d.querySelector('.btns .primary') as HTMLElement)?.dataset.b, rows: d.querySelectorAll('table.reasons tr').length, beta: d.textContent!.includes('(beta)') }; return res; },
    closeModal() { document.querySelectorAll('.modal-bg').forEach(m => m.remove()); return true; },
    accentScan() { let n = 0; for (const el of Array.from(document.querySelectorAll('body *')) as HTMLElement[]) { const cs = getComputedStyle(el); if (cs.display === 'none') continue; for (const side of ['borderLeftColor', 'borderTopColor', 'borderRightColor', 'borderBottomColor'] as const) { const w = parseFloat((cs as any)[side.replace('Color', 'Width')]); if (w >= 1 && (cs as any)[side] === 'rgb(232, 96, 28)') n++; } } return n; },
  };
  (window as any).nx = { test: T, bridge };
}
