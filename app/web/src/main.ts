// App shell: header / FILE rail · canvas · LAYERS + PROPERTIES rails / command bar / footer; state + doc + patch events; keyboard; drop; test hooks.
import { bridge } from './bridge';
import { Doc } from './doc';
import { I } from './icons';
import { CommandBar, FileRail, LayerRail, PropsRail, showLossThenSave } from './rails';
import { installTests } from './test';
import { esc, fmtInt, h, H, Hreset } from './util';
import { CanvasView } from './view';
import { L, lang, setLang, xl } from './i18n';

let S: any = null; let doc: Doc | null = null;
const app = document.getElementById('app')!;
const hdr = h(`<header class="hdr"></header>`); const main = h(`<main class="main"></main>`); const ftr = h(`<footer class="ftr"><span class="d"></span><span class="txt"></span><span class="r"></span></footer>`);
const files = new FileRail(); const layers = new LayerRail(); const props = new PropsRail(); const cmd = new CommandBar(); const view = new CanvasView();
const left = h(`<aside class="aside"></aside>`); const right = h(`<aside class="aside right"></aside>`); const center = h(`<div class="center"></div>`);
const toolbar = h(`<div class="toolbar"></div>`);
left.append(files.el); right.append(layers.el, props.el); center.append(view.el); view.el.prepend(toolbar);
main.append(left, center, right); app.append(hdr, main, cmd.el, ftr);
let frameInfo = '';

const TOOLS: [string, string, string, string][] = [
  ['select', L('SELECT', '선택'), I.cursor(), L('Select (Esc)', '선택 (Esc)')], ['line', L('LINE', '선'), I.line(), L('Line (L)', '선 (L)')], ['pline', L('PLINE', '폴리선'), I.pline(), L('Polyline (PL)', '폴리선 (PL)')],
  ['circle', L('CIRCLE', '원'), I.circle(), L('Circle (C)', '원 (C)')], ['rect', L('RECT', '사각형'), I.rect(), L('Rectangle (REC)', '사각형 (REC)')], ['arc', L('ARC', '호'), I.arc(), L('Arc, 3 points (A)', '호, 3점 (A)')],
  ['ellipse', L('ELLIPSE', '타원'), I.ellipse(), L('Ellipse (EL)', '타원 (EL)')], ['polygon', L('POLYGON', '다각형'), I.polygon(), L('Polygon (POL)', '다각형 (POL)')], ['point', L('POINT', '점'), I.point(), L('Point (PO)', '점 (PO)')],
  ['dtext', L('TEXT', '문자'), I.text(), L('Text (DT) · multiline text (T)', '문자 (DT) · 여러 줄 문자 (T)')], ['dim', L('DIM', '치수'), I.dim(), L('Linear dimension (DLI)', '선형 치수 (DLI)')],
  ['move', L('MOVE', '이동'), I.move(), L('Move (M)', '이동 (M)')], ['copy', L('COPY', '복사'), I.copy(), L('Copy (CO)', '복사 (CO)')], ['rotate', L('ROTATE', '회전'), I.rotate(), L('Rotate (RO)', '회전 (RO)')],
  ['mirror', L('MIRROR', '대칭'), I.mirror(), L('Mirror (MI)', '대칭 (MI)')], ['offset', L('OFFSET', '간격띄우기'), I.offset(), L('Offset (O)', '간격띄우기 (O)')], ['stretch', L('STRETCH', '신축'), I.stretch(), L('Stretch (S)', '신축 (S)')],
  ['trim', L('TRIM', '자르기'), I.trim(), L('Trim (TR) — click the part to remove · Shift+click = extend', '자르기 (TR) — 지울 부분 클릭 · Shift+클릭 = 연장')],
  ['extend', L('EXTEND', '연장'), I.extend(), L('Extend (EX) — click near the end to lengthen · Shift+click = trim', '연장 (EX) — 늘릴 끝 쪽 클릭 · Shift+클릭 = 자르기')],
  ['erase', L('ERASE', '지우기'), I.erase(), L('Erase (E / Del)', '지우기 (E / Del)')]];
// side panels (FILE on the left, LAYERS + PROPERTIES on the right) can be hidden so the drawing takes the width; remembered per PC
const panels = { left: true, right: true };
try { panels.left = localStorage.getItem('nx.panel.left') !== '0'; panels.right = localStorage.getItem('nx.panel.right') !== '0'; } catch { }
function applyPanels() { main.classList.toggle('hide-left', !panels.left); main.classList.toggle('hide-right', !panels.right); view.resize(); }
function togglePanel(which: 'left' | 'right' | 'both', on?: boolean) {
  if (which === 'both') { const show = on ?? !(panels.left || panels.right); panels.left = panels.right = show; }
  else panels[which] = on ?? !panels[which];
  try { localStorage.setItem('nx.panel.left', panels.left ? '1' : '0'); localStorage.setItem('nx.panel.right', panels.right ? '1' : '0'); } catch { }
  applyPanels(); renderToolbar();
}
function renderToolbar() {
  const EDIT = new Set(['select', 'line', 'pline', 'circle', 'rect', 'arc', 'ellipse', 'polygon', 'point', 'dtext', 'dim', 'move', 'copy', 'rotate', 'mirror', 'offset', 'stretch', 'trim', 'extend', 'erase']);   // icon only (tooltip = name + alias)
  H(toolbar).innerHTML = `<button type="button" class="tool icon ptog${panels.left ? ' shown' : ''}" data-a="pleft" aria-pressed="${panels.left}" title="${L('Show / hide the left panel (Ctrl+2) · both panels: Ctrl+0', '왼쪽 패널 보이기 / 숨기기 (Ctrl+2) · 양쪽 모두: Ctrl+0')}">${I.panelLeft()}</button>` + TOOLS.map(([n, l, ic, t], i) => `${n === 'move' ? '<div class="vsep"></div>' : ''}<button type="button" class="tool${EDIT.has(n) ? ' edit' : ''}${view.tool === n ? ' on' : ''}" data-tool="${n}" title="${esc(t)}" aria-label="${l}">${ic}<span>${l}</span></button>`).join('') +
    `<div class="vsep"></div><button type="button" class="tool" data-a="zoomext" title="${L('Zoom extents (Z E · wheel double-click)', '전체 보기 (Z E · 휠 더블클릭)')}">${I.zoomFit()}</button>` +
    `<button type="button" class="tool cvtheme" data-a="cvtheme" title="${L('Drawing background', '도면 배경')}: ${cvLabel()} (${L('app theme', '앱 테마')} ${S?.theme ?? 'light'}) — ${L('click to switch', '클릭해서 전환')}">${canvasDark() ? I.theme() : I.sun()}<span>${L('BG', '배경')} ${cvLabel().toUpperCase()}</span></button>` +
    `<button type="button" class="tool icon ptog${panels.right ? ' shown' : ''}" data-a="pright" aria-pressed="${panels.right}" title="${L('Show / hide the right panel (Ctrl+1) · both panels: Ctrl+0', '오른쪽 패널 보이기 / 숨기기 (Ctrl+1) · 양쪽 모두: Ctrl+0')}">${I.panelRight()}</button>`;
}
toolbar.addEventListener('click', e => { const t = e.target as HTMLElement; const n = (t.closest('[data-tool]') as HTMLElement | null)?.dataset.tool; if (n) { if (n === 'select') view.cancel(); else view.run(n); renderToolbar(); view.wrap.focus(); return; } const a = (t.closest('[data-a]') as HTMLElement | null)?.dataset.a; if (a === 'zoomext') view.zoomExtents(); if (a === 'pleft') togglePanel('left'); if (a === 'pright') togglePanel('right'); if (a === 'cvtheme') { const order = ['auto', 'light', 'dark']; const cur = S?.canvasTheme ?? 'auto'; bridge.call('setCanvasTheme', { theme: order[(order.indexOf(cur) + 1) % 3] }); } });
/** Drawing-area background: 'auto' follows the app theme, 'light' / 'dark' are fixed (draft5 I2). */
function canvasDark() { const c = S?.canvasTheme ?? 'auto'; return c === 'dark' || (c === 'auto' && S?.theme === 'dark'); }
function cvLabel() { const c = S?.canvasTheme ?? 'auto'; return c === 'auto' ? 'auto' : c; }

function renderHeader() {
  if (!S) return; const app = S.app ?? {}; const f = S.file;
  const tool = (a: string, icon: string, label: string, title: string, disabled = false) => `<button type="button" class="tool" data-a="${a}" aria-label="${label}" title="${title}"${disabled ? ' disabled' : ''}>${icon}<span>${label}</span></button>`;
  H(hdr).innerHTML = `<div class="hdr-brand"><div class="hdr-logo" data-a="about" title="About NexysDraft (F1)"><img src="brand/Logo.png" alt="Nexys"></div><div class="hdr-name"><b>NexysDraft</b><small>${esc((app.versionText || '').toUpperCase())}${app.libredwg ? ' · LIBREDWG ' + app.libredwg : ''}</small></div></div>
    <div class="hdr-tools">${tool('open', I.folder(), L('OPEN', '열기'), L('Open DWG / DXF (Ctrl+O)', 'DWG / DXF 열기 (Ctrl+O)'))}${tool('save', I.save(), L('SAVE', '저장'), L('Save (Ctrl+S)', '저장 (Ctrl+S)'), !f)}${tool('savedwg', I.save(), 'DWG', L('Save as DWG R2000 via LibreDWG (Ctrl+Shift+S)', 'DWG R2000으로 저장, LibreDWG (Ctrl+Shift+S)'), !f)}${tool('pdf', I.pdf(), 'PDF', L('Export searchable PDF: merged + per sheet + overview (Ctrl+P)', '검색 가능한 PDF 내보내기: 합본 + 시트별 + 개요 (Ctrl+P)'), !f)}<div class="vsep"></div>${tool('undo', I.undo(), L('UNDO', '실행 취소'), (S.undo?.can ? `${L('Undo', '실행 취소')} ${S.undo.name}` : L('Undo', '실행 취소')) + ' (Ctrl+Z)', !S.undo?.can)}${tool('redo', I.redo(), L('REDO', '다시 실행'), (S.undo?.canRedo ? `${L('Redo', '다시 실행')} ${S.undo.redoName}` : L('Redo', '다시 실행')) + ' (Ctrl+Y)', !S.undo?.canRedo)}<div class="vsep"></div>${tool('theme', S.theme === 'dark' ? I.sun() : I.theme(), L('THEME', '테마'), L('Light / dark (Ctrl+T)', '밝게 / 어둡게 (Ctrl+T)'))}${tool('lang', I.globe(), lang() === 'ko' ? '한국어' : 'ENGLISH', '언어 / Language: 한국어 ↔ English (LANG KO · LANG EN)')}</div>
    <button type="button" class="tool icon" data-a="help" aria-label="HELP" title="${L('Keys · how to use (F1)', '단축키 · 사용법 (F1)')}">${I.help()}</button>
    <div class="led-pill"><span class="led ${S.status?.led ?? 'idle'}"></span><span class="led-text">${(S.status?.led ?? 'idle').toUpperCase()}</span></div>`;
}
hdr.addEventListener('click', e => {
  const a = ((e.target as HTMLElement).closest('[data-a]') as HTMLElement | null)?.dataset.a; if (!a) return;
  if (a === 'open') bridge.call('openDialog'); else if (a === 'save') bridge.call('save'); else if (a === 'savedwg') showLossThenSave(); else if (a === 'pdf') bridge.call('exportPdfDialog');
  else if (a === 'undo') bridge.call('undo'); else if (a === 'redo') bridge.call('redo'); else if (a === 'theme') bridge.call('setTheme', { theme: S?.theme === 'dark' ? 'light' : 'dark' });
  else if (a === 'lang') setLang(lang() === 'ko' ? 'en' : 'ko');
  else if (a === 'help' || a === 'about') showHelp();
});
function renderFooter() {
  if (!S) return; (ftr.querySelector('.d') as HTMLElement).className = 'd ' + (S.status?.led ?? 'idle');
  ftr.querySelector('.txt')!.textContent = S.status?.text || (S.file ? `${S.file.name} · ${fmtInt(S.file.entities)} ${L('entities', '객체')}` : L('Open a DWG/DXF file or drop it onto the window', 'DWG/DXF 파일을 열거나 창에 끌어다 놓으세요'));
  ftr.querySelector('.r')!.textContent = frameInfo;
  renderModes();
  const c = ftr.querySelector('.cancel'); if (S.status?.saving) { if (!c) ftr.insertBefore(h(`<button class="pill h26 cancel" data-a="cancelSave">${L('CANCEL SAVE', '저장 취소')}</button>`), ftr.querySelector('.r')); } else c?.remove();
}
const modesEl = h(`<span class="modes"></span>`); ftr.insertBefore(modesEl, ftr.querySelector('.r'));
const MODES: [string, string, string][] = [['snap', 'SNAP', L('Grid snap (F9)', '그리드 스냅 (F9)')], ['grid', 'GRID', L('Grid display (F7)', '그리드 표시 (F7)')], ['ortho', 'ORTHO', L('Ortho (F8 · hold Shift = temporary)', '직교 (F8 · Shift 누르는 동안 임시)')], ['polar', 'POLAR', L('Polar tracking (F10)', '극좌표 추적 (F10)')], ['osnap', 'OSNAP', L('Object snap (F3)', '객체 스냅 (F3)')], ['otrack', 'OTRACK', L('Object snap tracking (F11) — rest the cursor on a snap point, then move along the dotted path', '객체 스냅 추적 (F11) — 스냅 점에 커서를 잠깐 올린 뒤 점선을 따라 이동')]];
function renderModes() {
  const on: Record<string, boolean> = { snap: view.gridSnapOn, grid: view.gridOn, ortho: view.orthoOn, polar: view.polarOn, osnap: view.snapOn, otrack: view.otrackOn };
  H(modesEl).innerHTML = MODES.map(([k, l, t]) => `<button type="button" class="${on[k] ? 'on' : ''}" data-mode="${k}" title="${t}">${l}${k === 'polar' && view.polarOn ? ' ' + view.polarInc + '°' : ''}</button>`).join('');
}
modesEl.addEventListener('click', e => { const k = ((e.target as HTMLElement).closest('[data-mode]') as HTMLElement | null)?.dataset.mode as any; if (k) { view.toggleMode(k); view.wrap.focus(); } });
document.addEventListener('nx-frame', (e: any) => { const d = e.detail; frameInfo = `frame ${d.ms.toFixed(1)} ms · ${fmtInt(d.segs)} segs · ${d.texts} texts`; ftr.querySelector('.r')!.textContent = frameInfo; });

function applyState(next: any) {
  S = next; (window as any).nxState = S; document.documentElement.dataset.theme = S.theme; view.setDark(canvasDark()); layers.dark = S.theme === 'dark';   // layer swatches sit on the app panel, not on the drawing
  renderHeader(); renderFooter(); files.render(S);
  if (doc && S.layers) { const a = doc.setLinetypes(S.ltypes, S.ltscale ?? 1); const b = doc.setLayers(S.layers); if (a || b) { doc.computeBBox(); view.rend.setDoc(doc); view.invalidate(); } }
  if (!S.file && doc) { doc = null; view.setDoc(null); }
  layers.render(S, doc); props.render(S, doc, Array.from(view.selection)); renderToolbar();
}
bridge.on('state', m => applyState(m.state));
bridge.on('doc', m => {
  if (!m.doc) { doc = null; view.setDoc(null); layers.render(S ?? {}, null); props.render(S ?? {}, null, []); return; }
  const t0 = performance.now(); doc = new Doc(); doc.load(m.doc); view.setDoc(doc); const ms = performance.now() - t0;
  const t1 = performance.now(); view.drawNow(); view.needDraw = false; const first = performance.now() - t1;
  bridge.call('perf', { key: 'page_load_ms', value: ms }); bridge.call('perf', { key: 'page_first_draw_ms', value: first }); cmd.add(xl(`Loaded ${doc.order.length} entities (${ms.toFixed(0)} ms page, first frame ${first.toFixed(0)} ms)`));
  if (S) { layers.render(S, doc); props.render(S, doc, []); }
});
bridge.on('patch', m => { if (!doc) return; doc.patch(m.upsert ?? [], m.remove ?? [], m.blocks ?? null); view.invalidate(); if (S) { layers.render(S, doc); props.render(S, doc, Array.from(view.selection)); } });
bridge.on('status', m => { if (!S) return; S.status = { text: m.text, led: m.led, saving: !!m.saving }; renderFooter(); if (typeof m.text === 'string' && m.text.startsWith('PDF:')) cmd.add(m.text); const led = hdr.querySelector('.led'); if (led) { Hreset(hdr); led.className = 'led ' + m.led; (hdr.querySelector('.led-text') as HTMLElement).textContent = m.led.toUpperCase(); } });
ftr.addEventListener('click', e => { if ((e.target as HTMLElement).closest('[data-a=cancelSave]')) bridge.call('save.cancel'); });

view.onSelectionChanged = () => { props.render(S ?? {}, doc, Array.from(view.selection)); bridge.call('select', { ids: Array.from(view.selection) }); };
view.onPrompt = t => { cmd.prompt(t); renderToolbar(); if (view.wantsText()) cmd.input.focus(); };   // text entry: the command line takes the keys (Korean IME composes there)
cmd.spaceLiteral = () => view.wantsText();
view.onHistory = t => cmd.add(t);
view.onModes = () => renderModes();
view.onFocusRail = which => { if (which === 'props') { props.el.scrollIntoView({ block: 'nearest' }); (props.body.querySelector('select, textarea, input') as HTMLElement | null)?.focus(); cmd.add(xl('PROPERTIES — rail focused')); } };
view.onCommand = (name, arg) => {
  switch (name) {
    case 'lang': case 'language': { const a = (arg ?? '').trim().toLowerCase(); const to = a.startsWith('k') || a.startsWith('한') ? 'ko' : a.startsWith('e') ? 'en' : (lang() === 'ko' ? 'en' : 'ko'); if (to === lang()) cmd.add(`LANG = ${to}`); else setLang(to); return true; }
    case 'skill': case 'installskill': installSkill().then(t => t.split('\n').slice(1, 4).forEach(l => cmd.add(l))); return true;
    case 'panels': case 'panel': { const a = (arg ?? '').trim().toLowerCase(); togglePanel(a.startsWith('l') ? 'left' : a.startsWith('r') ? 'right' : 'both'); cmd.add(`PANELS — left ${panels.left ? 'on' : 'off'}, right ${panels.right ? 'on' : 'off'}`); return true; }
    case 'open': bridge.call('openDialog'); return true; case 'save': bridge.call('save'); return true; case 'saveas': showLossThenSave(false); return true; case 'exportdwg': showLossThenSave(); return true; case 'pdf': bridge.call('exportPdfDialog'); return true;
    case 'layer': layers.input.focus(); cmd.add(xl('LAYER — rail focused (type a name + Enter = new layer)')); return true;
    case 'new': bridge.call('new'); return true;
  }
  return false;
};
cmd.onSubmit = text => {
  const t = text.trim(); if (t) cmd.add('> ' + t);
  if (view.tool !== 'select' && view.typed(t)) { renderToolbar(); return; }
  if (!t) { view.enter(); if (view.tool === 'select') view.run(view.lastTool); renderToolbar(); return; }
  const [name, ...rest] = t.split(/\s+/);
  if (!view.run(name, rest.join(' '))) cmd.add(xl(`Unknown command: ${name}`));
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
  if (e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === '1' || e.key === '2' || e.key === '0')) { e.preventDefault(); togglePanel(e.key === '1' ? 'right' : e.key === '2' ? 'left' : 'both'); return; }
  if (e.key === 'F1') { e.preventDefault(); showHelp(); return; }
  const fk: Record<string, 'osnap' | 'grid' | 'ortho' | 'snap' | 'polar' | 'otrack'> = { F3: 'osnap', F7: 'grid', F8: 'ortho', F9: 'snap', F10: 'polar', F11: 'otrack' };
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

/** Copy the agent skill (SKILL.md with this program's path) to the per-user skill folders of Antigravity CLI, Claude Code and Gemini CLI. */
async function installSkill(): Promise<string> {
  const r: any = await bridge.call('installSkill').catch(() => null);
  const head = r?.ok ? L('Installed. Open a new agent session and ask in plain words.', '설치했습니다. 에이전트를 새로 열고 말로 요청하세요.') : L('The skill could not be installed.', '스킬을 설치하지 못했습니다.');
  const text = head + (r?.text ? '\n' + r.text : ''); cmd.add(head); return text;
}
function showHelp() {
  if (document.querySelector('.help')) return;
  const ko = `<h3>NexysDraft — 사용법</h3>
    <p><b>열기/저장</b> Ctrl+O 열기(DWG는 LibreDWG로 변환) · Ctrl+S 저장 · Ctrl+Shift+S 다른 이름으로(사전 점검) · Ctrl+P PDF(합본 + 시트별 + 개요).</p>
    <p><b>보기</b> 휠 = 커서 기준 줌, 휠 드래그/Alt+드래그 = 팬, <b>휠 더블클릭</b> = 전체 보기(Z E), Z P = 이전 보기. 툴바 배경 버튼 = 도면 배경(AUTO/LIGHT/DARK). <b>패널 숨기기</b>: 툴바 양 끝 버튼 또는 Ctrl+2(왼쪽) · Ctrl+1(오른쪽) · Ctrl+0(양쪽).</p>
    <p><b>선택</b> 클릭 · 빈 곳을 클릭하고 마우스를 옮긴 뒤 다시 클릭(또는 드래그): <b>오른쪽으로 = 파란 실선 창</b>(완전히 안에 든 것만), <b>왼쪽으로 = 초록 점선 교차</b>(조금이라도 걸친 것 전부) · Shift = 추가/제거 · Ctrl+A 전체 · Del 삭제 · Esc 취소.</p>
    <p><b>명령줄</b> 글자를 치면 바로 명령줄로 · Enter/Space = 확정·마지막 명령 반복 · Esc = 취소 · 우클릭 짧게(&lt;250 ms) = 확정·반복, 길게 = 메뉴(RIGHTCLICK TIMED/ENTER/MENU) · <code>x,y</code> · <code>@dx,dy</code> · <code>@길이&lt;각도</code> · 숫자 = 직접 거리. L · PL · C · <b>REC</b> 사각형 · <b>A</b> 호(3점) · <b>EL</b> 타원 · <b>POL</b> 다각형 · <b>PO</b> 점 · <b>DT</b> 문자 · <b>T</b> 여러 줄 문자 · DLI · M · CO · RO · MI · O · S · <b>TR</b> 자르기 · <b>EX</b> 연장 · E · U · Z(A/E/W/P) · RE 재생성 · R 다시 그리기 · LA · PR · DI 거리 · ID 좌표.</p>
    <p><b>자르기/연장</b> TR: 지울 부분을 클릭(다른 객체와 만나는 곳까지 잘림) · EX: 늘릴 끝 쪽을 클릭(가장 가까운 객체까지) · Shift+클릭 = 반대 동작 · 드래그 = 선에 걸친 것 한꺼번에 · T = 기준 객체 직접 지정.</p>
    <p><b>그리기 보조</b> <b>Shift</b> 누르는 동안 임시 직교 · F8 직교 · F10 극좌표 추적(90°, POLAR 45) · <b>F11 객체 스냅 추적</b>(스냅 점에 커서를 잠깐 올려 + 표시가 생기면 그 점의 수평·수직 점선을 따라감) · F3 객체 스냅(끝점 □ 중점 △ 중심 ○ 교차 × 사분점 ◇) · F7 그리드 · F9 그리드 스냅. 상태줄 알약으로도 전환. 전체 표: docs/keys.md. <b>문자</b> 더블클릭 = 편집.</p>
    <p><b>언어</b> 위쪽의 지구본 버튼 또는 명령 LANG KO / LANG EN.</p>
    <ul><li>제외: 필렛·동적 블록·해치 편집·3D·XREF 편집·플롯 스타일.</li><li>DWG 쓰기는 LibreDWG(소스 빌드) 한계로 R2000. 저장 전 사전 점검(읽기 가능 여부·DXF 권장 사유)을 보여준다.</li></ul>
    <p><b>AI 에이전트로 그리기</b> 아래 버튼을 한 번 누르면 이 PC의 Antigravity CLI · Claude Code · Gemini CLI에 NexysDraft 스킬이 설치됩니다. 그 뒤 에이전트를 새로 열고 "200×120 판에 구멍 4개 그려서 DWG로 저장해줘"처럼 말로 시키면 됩니다. 프로그램 폴더를 옮기면 다시 누르세요.</p>
    <p class="skill-out" hidden></p>
    <div class="btns"><button class="pill" data-b="skill">AI 에이전트 스킬 설치</button><button class="pill primary" data-b="ok">닫기</button></div>`;
  const en = `<h3>NexysDraft — how to use</h3>
    <p><b>Open / save</b> Ctrl+O open (DWG is converted by LibreDWG) · Ctrl+S save · Ctrl+Shift+S save as (pre-check) · Ctrl+P PDF (merged + per sheet + overview).</p>
    <p><b>View</b> wheel = zoom at the cursor, wheel drag / Alt+drag = pan, <b>wheel double-click</b> = zoom extents (Z E), Z P = previous view. Toolbar BG button = drawing background (AUTO / LIGHT / DARK). <b>Hide panels</b>: the buttons at both ends of the toolbar, or Ctrl+2 (left) · Ctrl+1 (right) · Ctrl+0 (both).</p>
    <p><b>Select</b> click · click on empty space, move, click again (or drag): <b>to the right = blue solid window</b> (only what is completely inside), <b>to the left = green dashed crossing</b> (everything it touches) · Shift = add / remove · Ctrl+A all · Del erase · Esc cancel.</p>
    <p><b>Command line</b> typing goes straight to the command line · Enter / Space = confirm · repeat the last command · Esc = cancel · short right click (&lt;250 ms) = confirm · repeat, long = menu (RIGHTCLICK TIMED/ENTER/MENU) · <code>x,y</code> · <code>@dx,dy</code> · <code>@length&lt;angle</code> · a number = direct distance. L · PL · C · <b>REC</b> rectangle · <b>A</b> arc (3 points) · <b>EL</b> ellipse · <b>POL</b> polygon · <b>PO</b> point · <b>DT</b> text · <b>T</b> multiline text · DLI · M · CO · RO · MI · O · S · <b>TR</b> trim · <b>EX</b> extend · E · U · Z (A/E/W/P) · RE regen · R redraw · LA · PR · DI distance · ID point.</p>
    <p><b>Trim / extend</b> TR: click the part to remove (cut where it meets other objects) · EX: click near the end to lengthen (to the nearest object) · Shift+click = the other one · drag = everything the line crosses · T = choose the edges yourself.</p>
    <p><b>Drafting aids</b> hold <b>Shift</b> = temporary ortho · F8 ortho · F10 polar tracking (90°, POLAR 45) · <b>F11 object snap tracking</b> (rest the cursor on a snap point until a + appears, then follow the dotted horizontal / vertical path through it) · F3 object snap (end □ mid △ centre ○ intersection × quadrant ◇) · F7 grid · F9 grid snap. The status bar pills switch them too. Full table: docs/keys.md. Double-click a <b>text</b> = edit.</p>
    <p><b>Language</b> the globe button at the top, or the commands LANG KO / LANG EN.</p>
    <ul><li>Not included: fillet, dynamic blocks, hatch editing, 3D, XREF editing, plot styles.</li><li>DWG is written as R2000 (LibreDWG). A pre-check before saving shows whether other CAD programs will read it and why DXF may be the better choice.</li></ul>
    <p><b>Drawing with an AI agent</b> One click on the button below installs the NexysDraft skill for Antigravity CLI · Claude Code · Gemini CLI on this PC. Then open a new agent session and ask in plain words, e.g. "draw a 200 × 120 plate with four holes and save it as DWG". Click again after moving the program folder.</p>
    <p class="skill-out" hidden></p>
    <div class="btns"><button class="pill" data-b="skill">INSTALL AI AGENT SKILL</button><button class="pill primary" data-b="ok">CLOSE</button></div>`;
  const dlg = h(`<div class="modal-bg help"><div class="modal">${L(en, ko)}</div></div>`);
  dlg.addEventListener('click', e => {
    const b = ((e.target as HTMLElement).closest('[data-b]') as HTMLElement | null)?.dataset.b;
    if (b === 'skill') { installSkill().then(t => { const o = dlg.querySelector('.skill-out') as HTMLElement; o.hidden = false; o.style.whiteSpace = 'pre-wrap'; o.textContent = t; }); return; }
    if (b || e.target === dlg) dlg.remove();
  });
  document.body.append(dlg);
}

// pre-check reasons → select and zoom to the listed handles
document.addEventListener('nx-zoom-handles', (e: any) => {
  const ids: string[] = (e.detail || []).filter((h: string) => doc?.ents.has(h)); if (!doc || !ids.length) return;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity; for (const h of ids) { const b = doc.geomOf(h)?.bbox; if (!b) continue; x0 = Math.min(x0, b[0]); y0 = Math.min(y0, b[1]); x1 = Math.max(x1, b[2]); y1 = Math.max(y1, b[3]); }
  view.setSelection(ids); if (x0 <= x1) { const m = Math.max(x1 - x0, y1 - y0, 1) * 0.15; view.zoomWindow([x0 - m, y0 - m], [x1 + m, y1 + m]); }
});
applyPanels();
installTests({ view, cmd, getDoc: () => doc, getState: () => S, layers, props });
bridge.call('state').then(st => applyState(st));
bridge.post({ cmd: 'ready' });
