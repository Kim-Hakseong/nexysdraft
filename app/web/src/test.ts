// window.nx.test — automation hooks for the host self-test: steps run as real DOM events on the page, results go back through the bridge.
import { bridge } from './bridge';
import { Doc } from './doc';
import { w2s } from './render';
import { CanvasView } from './view';

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
