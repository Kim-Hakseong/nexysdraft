// App shell: header / FILE rail · canvas · LAYERS + PROPERTIES rails / command bar / footer; state + doc + patch events; keyboard; drop; test hooks.
import { bridge } from './bridge';
import { Doc } from './doc';
import { I } from './icons';
import { CommandBar, FileRail, LayerRail, PropsRail, showLossThenSave } from './rails';
import { installTests } from './test';
import { esc, fmtInt, h } from './util';
import { CanvasView } from './view';

let S: any = null; let doc: Doc | null = null;
const app = document.getElementById('app')!;
const hdr = h(`<header class="hdr"></header>`); const main = h(`<main class="main"></main>`); const ftr = h(`<footer class="ftr"><span class="d"></span><span class="txt"></span><span class="r"></span></footer>`);
const files = new FileRail(); const layers = new LayerRail(); const props = new PropsRail(); const cmd = new CommandBar(); const view = new CanvasView();
const left = h(`<aside class="aside"></aside>`); const right = h(`<aside class="aside right"></aside>`); const center = h(`<div class="center"></div>`);
const toolbar = h(`<div class="toolbar"></div>`);
left.append(files.el); right.append(layers.el, props.el); center.append(view.el); view.el.prepend(toolbar);
main.append(left, center, right); app.append(hdr, main, cmd.el, ftr);
let frameInfo = '';

const TOOLS: [string, string, string, string][] = [['select', 'SELECT', I.cursor(), 'Select (Esc)'], ['line', 'LINE', I.line(), 'Line (L)'], ['pline', 'PLINE', I.pline(), 'Polyline (PL)'], ['circle', 'CIRCLE', I.circle(), 'Circle (C)'], ['rect', 'RECT', I.rect(), 'Rectangle (REC)'], ['arc', 'ARC', I.arc(), 'Arc, 3 points (A)'], ['ellipse', 'ELLIPSE', I.ellipse(), 'Ellipse (EL)'], ['polygon', 'POLYGON', I.polygon(), 'Polygon (POL)'], ['point', 'POINT', I.point(), 'Point (PO)'], ['dtext', 'TEXT', I.text(), 'Text (DT) · multiline text (T)'], ['dim', 'DIM', I.dim(), 'Linear dimension (DLI)'], ['move', 'MOVE', I.move(), 'Move (M)'], ['copy', 'COPY', I.copy(), 'Copy (CO)'], ['rotate', 'ROTATE', I.rotate(), 'Rotate (RO)'], ['mirror', 'MIRROR', I.mirror(), 'Mirror (MI)'], ['offset', 'OFFSET', I.offset(), 'Offset (O)'], ['stretch', 'STRETCH', I.stretch(), 'Stretch (S)'], ['erase', 'ERASE', I.erase(), 'Erase (E / Del)']];
function renderToolbar() {
  const EDIT = new Set(['line', 'pline', 'circle', 'rect', 'arc', 'ellipse', 'polygon', 'point', 'dtext', 'dim', 'move', 'copy', 'rotate', 'mirror', 'offset', 'stretch', 'erase']);   // icon only (tooltip = name + alias)
  toolbar.innerHTML = TOOLS.map(([n, l, ic, t], i) => `${n === 'move' ? '<div class="vsep"></div>' : ''}<button type="button" class="tool${EDIT.has(n) ? ' edit' : ''}${view.tool === n ? ' on' : ''}" data-tool="${n}" title="${esc(t)}" aria-label="${l}">${ic}<span>${l}</span></button>`).join('') +
    `<div class="vsep"></div><button type="button" class="tool" data-a="zoomext" title="Zoom extents (Z E · wheel double-click)">${I.zoomFit()}<span>FIT</span></button>` +
    `<button type="button" class="tool cvtheme" data-a="cvtheme" title="Drawing background: ${cvLabel()} (app theme ${S?.theme ?? 'light'}) — click to switch">${canvasDark() ? I.theme() : I.sun()}<span>BG ${cvLabel().toUpperCase()}</span></button>`;
}
toolbar.addEventListener('click', e => { const t = e.target as HTMLElement; const n = (t.closest('[data-tool]') as HTMLElement | null)?.dataset.tool; if (n) { if (n === 'select') view.cancel(); else view.run(n); renderToolbar(); view.wrap.focus(); return; } const a = (t.closest('[data-a]') as HTMLElement | null)?.dataset.a; if (a === 'zoomext') view.zoomExtents(); if (a === 'cvtheme') { const order = ['auto', 'light', 'dark']; const cur = S?.canvasTheme ?? 'auto'; bridge.call('setCanvasTheme', { theme: order[(order.indexOf(cur) + 1) % 3] }); } });
/** Drawing-area background: 'auto' follows the app theme, 'light' / 'dark' are fixed (draft5 I2). */
function canvasDark() { const c = S?.canvasTheme ?? 'auto'; return c === 'dark' || (c === 'auto' && S?.theme === 'dark'); }
function cvLabel() { const c = S?.canvasTheme ?? 'auto'; return c === 'auto' ? 'auto' : c; }

function renderHeader() {
  if (!S) return; const app = S.app ?? {}; const f = S.file;
  const tool = (a: string, icon: string, label: string, title: string, disabled = false) => `<button type="button" class="tool" data-a="${a}" aria-label="${label}" title="${title}"${disabled ? ' disabled' : ''}>${icon}<span>${label}</span></button>`;
  hdr.innerHTML = `<div class="hdr-brand"><div class="hdr-logo" data-a="about" title="About NexysDraft (F1)"><img src="brand/Logo.png" alt="Nexys"></div><div class="hdr-name"><b>NexysDraft</b><small>${esc((app.versionText || '').toUpperCase())}${app.libredwg ? ' · LIBREDWG ' + app.libredwg : ''}</small></div></div>
    <div class="hdr-tools">${tool('open', I.folder(), 'OPEN', 'Open DWG / DXF (Ctrl+O)')}${tool('save', I.save(), 'SAVE', 'Save (Ctrl+S)', !f)}${tool('savedwg', I.save(), 'DWG', 'Save as DWG R2000 via LibreDWG (Ctrl+Shift+S)', !f)}${tool('pdf', I.pdf(), 'PDF', 'Export searchable PDF: merged + per sheet + overview (Ctrl+P)', !f)}<div class="vsep"></div>${tool('undo', I.undo(), 'UNDO', S.undo?.can ? `Undo ${S.undo.name} (Ctrl+Z)` : 'Undo (Ctrl+Z)', !S.undo?.can)}${tool('redo', I.redo(), 'REDO', S.undo?.canRedo ? `Redo ${S.undo.redoName} (Ctrl+Y)` : 'Redo (Ctrl+Y)', !S.undo?.canRedo)}<div class="vsep"></div>${tool('theme', S.theme === 'dark' ? I.sun() : I.theme(), 'THEME', 'Light / dark (Ctrl+T)')}</div>
    <button type="button" class="tool icon" data-a="help" aria-label="HELP" title="단축키 · 사용법 (F1)">${I.help()}</button>
    <div class="led-pill"><span class="led ${S.status?.led ?? 'idle'}"></span><span class="led-text">${(S.status?.led ?? 'idle').toUpperCase()}</span></div>`;
}
hdr.addEventListener('click', e => {
  const a = ((e.target as HTMLElement).closest('[data-a]') as HTMLElement | null)?.dataset.a; if (!a) return;
  if (a === 'open') bridge.call('openDialog'); else if (a === 'save') bridge.call('save'); else if (a === 'savedwg') showLossThenSave(); else if (a === 'pdf') bridge.call('exportPdfDialog');
  else if (a === 'undo') bridge.call('undo'); else if (a === 'redo') bridge.call('redo'); else if (a === 'theme') bridge.call('setTheme', { theme: S?.theme === 'dark' ? 'light' : 'dark' });
  else if (a === 'help' || a === 'about') showHelp();
});
function renderFooter() {
  if (!S) return; (ftr.querySelector('.d') as HTMLElement).className = 'd ' + (S.status?.led ?? 'idle');
  ftr.querySelector('.txt')!.textContent = S.status?.text || (S.file ? `${S.file.name} · ${fmtInt(S.file.entities)} entities` : 'Open a DWG/DXF file or drop it onto the window');
  ftr.querySelector('.r')!.textContent = frameInfo;
  renderModes();
  const c = ftr.querySelector('.cancel'); if (S.status?.saving) { if (!c) ftr.insertBefore(h(`<button class="pill h26 cancel" data-a="cancelSave">CANCEL SAVE</button>`), ftr.querySelector('.r')); } else c?.remove();
}
const modesEl = h(`<span class="modes"></span>`); ftr.insertBefore(modesEl, ftr.querySelector('.r'));
const MODES: [string, string, string][] = [['snap', 'SNAP', 'Grid snap (F9)'], ['grid', 'GRID', 'Grid display (F7)'], ['ortho', 'ORTHO', 'Ortho (F8 · hold Shift = temporary)'], ['polar', 'POLAR', 'Polar tracking (F10)'], ['osnap', 'OSNAP', 'Object snap (F3)']];
function renderModes() {
  const on: Record<string, boolean> = { snap: view.gridSnapOn, grid: view.gridOn, ortho: view.orthoOn, polar: view.polarOn, osnap: view.snapOn };
  modesEl.innerHTML = MODES.map(([k, l, t]) => `<button type="button" class="${on[k] ? 'on' : ''}" data-mode="${k}" title="${t}">${l}${k === 'polar' && view.polarOn ? ' ' + view.polarInc + '°' : ''}</button>`).join('');
}
modesEl.addEventListener('click', e => { const k = ((e.target as HTMLElement).closest('[data-mode]') as HTMLElement | null)?.dataset.mode as any; if (k) { view.toggleMode(k); view.wrap.focus(); } });
document.addEventListener('nx-frame', (e: any) => { const d = e.detail; frameInfo = `frame ${d.ms.toFixed(1)} ms · ${fmtInt(d.segs)} segs · ${d.texts} texts`; ftr.querySelector('.r')!.textContent = frameInfo; });

function applyState(next: any) {
  S = next; (window as any).nxState = S; document.documentElement.dataset.theme = S.theme; view.setDark(canvasDark()); layers.dark = S.theme === 'dark';   // layer swatches sit on the app panel, not on the drawing
  renderHeader(); renderFooter(); files.render(S);
  if (doc && S.layers) { if (doc.setLayers(S.layers)) { doc.computeBBox(); view.invalidate(); } }
  if (!S.file && doc) { doc = null; view.setDoc(null); }
  layers.render(S, doc); props.render(S, doc, Array.from(view.selection)); renderToolbar();
}
bridge.on('state', m => applyState(m.state));
bridge.on('doc', m => {
  if (!m.doc) { doc = null; view.setDoc(null); layers.render(S ?? {}, null); props.render(S ?? {}, null, []); return; }
  const t0 = performance.now(); doc = new Doc(); doc.load(m.doc); view.setDoc(doc); const ms = performance.now() - t0;
  const t1 = performance.now(); view.drawNow(); view.needDraw = false; const first = performance.now() - t1;
  bridge.call('perf', { key: 'page_load_ms', value: ms }); bridge.call('perf', { key: 'page_first_draw_ms', value: first }); cmd.add(`Loaded ${doc.order.length} entities (${ms.toFixed(0)} ms page, first frame ${first.toFixed(0)} ms)`);
  if (S) { layers.render(S, doc); props.render(S, doc, []); }
});
bridge.on('patch', m => { if (!doc) return; doc.patch(m.upsert ?? [], m.remove ?? [], m.blocks ?? null); view.invalidate(); if (S) { layers.render(S, doc); props.render(S, doc, Array.from(view.selection)); } });
bridge.on('status', m => { if (!S) return; S.status = { text: m.text, led: m.led, saving: !!m.saving }; renderFooter(); if (typeof m.text === 'string' && m.text.startsWith('PDF:')) cmd.add(m.text); const led = hdr.querySelector('.led'); if (led) { led.className = 'led ' + m.led; (hdr.querySelector('.led-text') as HTMLElement).textContent = m.led.toUpperCase(); } });
ftr.addEventListener('click', e => { if ((e.target as HTMLElement).closest('[data-a=cancelSave]')) bridge.call('save.cancel'); });

view.onSelectionChanged = () => { props.render(S ?? {}, doc, Array.from(view.selection)); bridge.call('select', { ids: Array.from(view.selection) }); };
view.onPrompt = t => { cmd.prompt(t); renderToolbar(); if (view.wantsText()) cmd.input.focus(); };   // text entry: the command line takes the keys (Korean IME composes there)
cmd.spaceLiteral = () => view.wantsText();
view.onHistory = t => cmd.add(t);
view.onModes = () => renderModes();
view.onFocusRail = which => { if (which === 'props') { props.el.scrollIntoView({ block: 'nearest' }); (props.body.querySelector('select, textarea, input') as HTMLElement | null)?.focus(); cmd.add('PROPERTIES — rail focused'); } };
view.onCommand = name => {
  switch (name) {
    case 'open': bridge.call('openDialog'); return true; case 'save': bridge.call('save'); return true; case 'saveas': showLossThenSave(false); return true; case 'exportdwg': showLossThenSave(); return true; case 'pdf': bridge.call('exportPdfDialog'); return true;
    case 'layer': layers.input.focus(); cmd.add('LAYER — rail focused (type a name + Enter = new layer)'); return true;
    case 'new': bridge.call('new'); return true;
  }
  return false;
};
cmd.onSubmit = text => {
  const t = text.trim(); if (t) cmd.add('> ' + t);
  if (view.tool !== 'select' && view.typed(t)) { renderToolbar(); return; }
  if (!t) { view.enter(); if (view.tool === 'select') view.run(view.lastTool); renderToolbar(); return; }
  const [name, ...rest] = t.split(/\s+/);
  if (!view.run(name, rest.join(' '))) cmd.add(`Unknown command: ${name}`);
  renderToolbar();
};
cmd.onEscape = () => { view.cancel(); renderToolbar(); view.wrap.focus(); };

// keyboard (global): command shortcuts; typing a letter while the canvas has focus goes to the command line
document.addEventListener('keydown', e => {
  const t = e.target as HTMLElement; const typing = t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT';
  if (e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 'o') { e.preventDefault(); bridge.call('openDialog'); return; }
  if (e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 's') { e.preventDefault(); bridge.call('save'); return; }
  if (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === 's') { e.preventDefault(); showLossThenSave(); return; }
  if (e.ctrlKey && e.key.toLowerCase() === 'z') { e.preventDefault(); bridge.call('undo'); return; }
  if (e.ctrlKey && e.key.toLowerCase() === 'y') { e.preventDefault(); bridge.call('redo'); return; }
  if (e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 'p') { e.preventDefault(); bridge.call('exportPdfDialog'); return; }
  if (e.ctrlKey && e.key.toLowerCase() === 't') { e.preventDefault(); bridge.call('setTheme', { theme: S?.theme === 'dark' ? 'light' : 'dark' }); return; }
  if (e.ctrlKey && e.key.toLowerCase() === 'a' && !typing) { e.preventDefault(); view.run('selectall'); return; }
  if (e.key === 'F1') { e.preventDefault(); showHelp(); return; }
  const fk: Record<string, 'osnap' | 'grid' | 'ortho' | 'snap' | 'polar'> = { F3: 'osnap', F7: 'grid', F8: 'ortho', F9: 'snap', F10: 'polar' };
  if (fk[e.key]) { e.preventDefault(); if (e.key === 'F10') { hostF10(); return; } view.toggleMode(fk[e.key]); return; }
  if (e.key === 'Shift') { view.shiftDown = true; view.invalidate(); }
  if (typing) return;
  if (e.key === 'Escape') { view.key(e); renderToolbar(); return; }
  if (e.key === 'Delete') { view.key(e); return; }
  if (e.key === 'Enter' || e.key === ' ') { view.key(e); renderToolbar(); return; }
  // AutoCAD: typing anywhere goes to the command line (the focus moves before the character is inserted)
  if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey && /[a-zA-Z0-9@.,<\-]/.test(e.key)) { cmd.input.focus(); }
});

// F10 / Alt: the WPF shell swallows them (Windows menu keys) and forwards F10 here as a 'hostkey' message; a page keydown for the same press is ignored (dedupe 150 ms)
let lastF10 = 0;
function hostF10() { const now = performance.now(); if (now - lastF10 < 150) return; lastF10 = now; view.toggleMode('polar'); }
bridge.on('hostkey', m => { if (m.key === 'F10') hostF10(); });
document.addEventListener('keyup', e => { if (e.key === 'Shift') { view.shiftDown = false; view.invalidate(); } });
window.addEventListener('blur', () => { view.shiftDown = false; });

// file drop
document.addEventListener('dragover', e => { e.preventDefault(); });
document.addEventListener('drop', e => { e.preventDefault(); if (e.dataTransfer?.files?.length) bridge.postFiles(e.dataTransfer.files); });

function showHelp() {
  if (document.querySelector('.help')) return;
  const dlg = h(`<div class="modal-bg help"><div class="modal"><h3>NexysDraft — 사용법</h3>
    <p><b>열기/저장</b> Ctrl+O 열기(DWG는 LibreDWG로 변환) · Ctrl+S 저장 · Ctrl+Shift+S 다른 이름으로(사전 점검) · Ctrl+P PDF(합본 + 시트별 + 개요).</p>
    <p><b>보기</b> 휠 = 커서 기준 줌, 휠 드래그/Alt+드래그 = 팬, <b>휠 더블클릭</b> = 전체 보기(Z E), Z P = 이전 보기. 툴바 BG = 도면 배경(AUTO/LIGHT/DARK).</p>
    <p><b>선택</b> 클릭, 왼→오 창(안에 든 것), 오→왼 교차(걸친 것), Shift = 추가/제거, Ctrl+A 전체, Del 삭제.</p>
    <p><b>명령줄</b> 글자를 치면 바로 명령줄로 · Enter/Space = 확정·마지막 명령 반복 · Esc = 취소 · 우클릭 짧게(&lt;250 ms) = 확정·반복, 길게 = 메뉴(RIGHTCLICK TIMED/ENTER/MENU) · <code>x,y</code> · <code>@dx,dy</code> · <code>@길이&lt;각도</code> · 숫자 = 직접 거리. L · PL · C · <b>REC</b> 사각형 · <b>A</b> 호(3점) · <b>EL</b> 타원 · <b>POL</b> 다각형 · <b>PO</b> 점 · <b>DT</b> 텍스트 · <b>T</b> 여러 줄 텍스트 · DLI · M · CO · RO · MI · O · S · E · U · Z(A/E/W/P) · RE 재생성 · R 다시 그리기 · LA · PR · DI 거리 · ID 좌표.</p>
    <p><b>그리기 보조</b> <b>Shift</b> 누르는 동안 임시 직교 · F8 직교 · F10 극좌표(90°, POLAR 45) · F3 객체 스냅(끝점 □ 중점 △ 중심 ○ 교차 × 사분점 ◇) · F7 그리드 · F9 그리드 스냅. 상태줄 알약으로도 전환. 전체 표: docs/keys.md. <b>텍스트</b> 더블클릭 = 편집.</p>
    <ul><li>MVP 제외: 트림·연장·필렛·동적 블록·해치 편집·3D·XREF 편집·플롯 스타일.</li><li>DWG 쓰기는 LibreDWG(소스 빌드) 한계로 R2000. 저장 전 사전 점검(읽기 가능 여부·DXF 권장 사유)을 보여준다.</li></ul>
    <div class="btns"><button class="pill primary" data-b="ok">CLOSE</button></div></div></div>`);
  dlg.addEventListener('click', e => { if ((e.target as HTMLElement).closest('[data-b]') || e.target === dlg) dlg.remove(); });
  document.body.append(dlg);
}

// pre-check reasons → select and zoom to the listed handles
document.addEventListener('nx-zoom-handles', (e: any) => {
  const ids: string[] = (e.detail || []).filter((h: string) => doc?.ents.has(h)); if (!doc || !ids.length) return;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity; for (const h of ids) { const b = doc.geomOf(h)?.bbox; if (!b) continue; x0 = Math.min(x0, b[0]); y0 = Math.min(y0, b[1]); x1 = Math.max(x1, b[2]); y1 = Math.max(y1, b[3]); }
  view.setSelection(ids); if (x0 <= x1) { const m = Math.max(x1 - x0, y1 - y0, 1) * 0.15; view.zoomWindow([x0 - m, y0 - m], [x1 + m, y1 + m]); }
});
installTests({ view, cmd, getDoc: () => doc, getState: () => S, layers, props });
bridge.call('state').then(st => applyState(st));
bridge.post({ cmd: 'ready' });
