// Rails: FILE (left), LAYERS + PROPERTIES (right), command bar (bottom).
import { bridge } from './bridge';
import { Doc, Ent } from './doc';
import { I } from './icons';
import { esc, fmtBytes, fmtInt, fmtNum, h, H, aciRgb, rgbCss } from './util';
import { L } from './i18n';

export class FileRail {
  el = h(`<div class="card" style="flex:1"><div class="card-h"><span class="ic">${I.file()}</span><span class="t">${L('FILE', '파일')}</span><span class="m"></span></div><div class="scroll body"></div></div>`);
  body = this.el.querySelector('.body') as HTMLElement;
  render(S: any) {
    const f = S.file; const m = this.el.querySelector('.m') as HTMLElement; m.textContent = f ? `${fmtInt(f.entities)} ${L('ents', '객체')} · ${S.layers?.length ?? 0} ${L('layers', '레이어')}` : '';
    const counts = f?.counts ?? {}; const top = Object.entries<number>(counts).sort((a, b) => b[1] - a[1]).slice(0, 12);
    H(this.body).innerHTML = `<div class="btn-row"><button class="pill" data-a="new">${I.file(13)} ${L('NEW', '새 도면')}</button><button class="pill primary" data-a="open">${I.folder(13)} ${L('OPEN', '열기')}</button><button class="pill" data-a="save" ${f ? '' : 'disabled'}>${I.save(13)} ${L('SAVE', '저장')}</button><button class="pill" data-a="saveas" ${f ? '' : 'disabled'}>DXF…</button><button class="pill" data-a="savedwg" ${f ? '' : 'disabled'}>DWG…</button><button class="pill" data-a="pdf" ${f ? '' : 'disabled'}>${I.pdf(13)} PDF</button></div>` +
      (f ? `<div class="file-card"><div class="fn">${esc(f.name)}${f.dirty ? ` <span class="chip">${L('modified', '수정됨')}</span>` : ''}</div><div class="chips"><span class="chip">${esc(f.sourceDwg ? 'DWG' : 'DXF')}</span><span class="chip">${esc(f.version)}</span><span class="chip">${esc(f.codepage)}</span><span class="chip">${fmtBytes(f.size)}</span></div>
        <div class="big">${fmtInt(f.entities)}<small>${L('entities', '객체')}</small></div>
        <div class="kv"><span class="k">${L('LAYERS', '레이어')}</span><span class="v">${S.layers?.length ?? 0}</span><span class="k">${L('BLOCKS', '블록')}</span><span class="v">${f.blocks}</span><span class="k">${L('OPEN', '열기')}</span><span class="v">${f.openMs.toFixed(0)} ms</span>${f.sourceDwg ? `<span class="k">LIBREDWG</span><span class="v">${f.convertMs.toFixed(0)} ms</span>` : ''}${f.unknown ? `<span class="k">${L('UNSUPPORTED', '미지원')}</span><span class="v">${f.unknown}</span>` : ''}</div></div>
        <div class="lblh">${L('ENTITY TYPES', '객체 종류')}</div>${top.map(([k, v]) => `<div class="row"><span class="n">${esc(k)}</span><span class="m">${fmtInt(v)}</span></div>`).join('')}
        ${f.warnings?.length ? `<div class="lblh">${L('CONVERTER NOTES', '변환 메모')}</div>${f.warnings.slice(0, 6).map((w: string) => `<div class="row warn">${esc(w)}</div>`).join('')}` : ''}` : `<div class="empty">${L('Open a DWG (converted by LibreDWG) or a DXF.<br>Dropping a file works too.', 'DWG(LibreDWG 변환) 또는 DXF를 엽니다.<br>끌어다 놓아도 됩니다.')}</div>`) +
      (S.recent?.length ? `<div class="lblh">${L('RECENT', '최근 파일')}</div><div class="recent">${S.recent.map((p: string) => `<div class="row" data-open="${esc(p)}" title="${esc(p)}"><span class="n">${esc(p.split(/[\\/]/).pop())}</span></div>`).join('')}</div>` : '');
  }
  constructor() {
    this.body.addEventListener('click', e => {
      const t = e.target as HTMLElement; const a = (t.closest('[data-a]') as HTMLElement | null)?.dataset.a; const o = (t.closest('[data-open]') as HTMLElement | null)?.dataset.open;
      if (o) { bridge.call('open', { paths: [o] }); return; }
      if (a === 'open') bridge.call('openDialog'); else if (a === 'new') bridge.call('newDrawing'); else if (a === 'save') bridge.call('save'); else if (a === 'saveas') showLossThenSave(false); else if (a === 'savedwg') showLossThenSave(true); else if (a === 'pdf') bridge.call('exportPdfDialog');
    });
  }
}

/** Save dialog (draft3 G2, draft4 H1/H2): R2000 pre-check verdict, reasons with handles (double-click = zoom to them), font compatibility checkbox
 *  (remembered), DXF version (original / R2000 — R2000 by default when the original is newer than AC1027), and "save DWG without the n IMAGE"
 *  when IMAGE is the only blocking reason. Default button: DWG when the pre-check passes (and not plan B), else DXF. */
export async function showLossThenSave(preferDwg = true) {
  const r: any = await bridge.call('check');
  if (!r) return;
  const dwgDefault = r.recommended === 'dwg' && preferDwg;
  const fc = (window as any).nxState?.fontCompat !== false;
  const dxfVer = r.dxf?.dflt ?? 'keep';
  const rows = (r.items as any[]).map((i, k) => `<tr data-k="${k}" title="${L('Double-click: zoom to these objects', '더블클릭: 해당 객체로 줌')}"><td>${i.blocking || i.recommendDxf ? '<b>' + esc(i.kind) + '</b>' : esc(i.kind)} ×${i.count}</td><td>${esc(i.detail)}${i.handles?.length ? `<br><span class="chip">${esc(i.handles.slice(0, 6).join(' · '))}</span>` : ''}</td></tr>`).join('');
  const fixes = (r.fontFixes as any[]).map(f => `<tr><td>${esc(f.style)}</td><td>${esc(f.oldFont || L('(none)', '(없음)'))} → ${esc(f.newFont)}</td></tr>`).join('');
  const why = !r.dwgOk ? L('For the reasons below a DWG (R2000, LibreDWG) may not open in other CAD programs.', '아래 사유로 DWG(R2000, LibreDWG) 저장은 다른 CAD에서 열리지 않을 수 있습니다.')
    : r.recommended === 'dxf' && !r.planB ? L('The DWG opens, but dynamic block parameters are lost.', 'DWG는 열리지만 동적 블록의 매개변수가 빠집니다.') : r.planB ? L('DWG saving is beta.', 'DWG 저장은 beta입니다.') : L('R2000 pre-check passed.', 'R2000 사전 점검 통과.');
  const dlg = h(`<div class="modal-bg save-check"><div class="modal"><h3>${r.recommended === 'dwg' ? L('DWG save possible', 'DWG 저장 가능') : L('DXF recommended', 'DXF 권장')}</h3>
    <p>${why}${r.recommended === 'dxf' ? (dxfVer === 'r2000' ? L(' The DXF is written as R2000.', ' DXF는 R2000으로 저장됩니다.') : L(' The DXF keeps the original version.', ' DXF는 원본 그대로 저장됩니다.')) : ''}${r.imageOnly ? L(` Without the ${r.imageCount} IMAGE object(s) it can also be saved as DWG.`, ` IMAGE ${r.imageCount}개를 빼면 DWG로도 저장할 수 있습니다.`) : ''} ${L('Estimated DWG save time', '예상 DWG 저장 시간')} ${Math.max(1, Math.round(r.estimateSeconds))} s.</p>
    ${rows ? `<table class="reasons">${rows}</table>` : ''}
    <label style="display:flex;gap:8px;align-items:center;font-size:12px"><input type="checkbox" data-f="fc" ${fc ? 'checked' : ''}> ${L(`nanoCAD font compatibility (replace ${r.fontFixes.length} Korean text style(s) whose font is not on this PC)`, `nanoCAD 글꼴 호환 (PC에 없는 글꼴을 쓰는 한글 스타일 ${r.fontFixes.length}개 치환)`)}</label>
    ${fixes ? `<table>${fixes}</table>` : ''}
    <label style="display:flex;gap:8px;align-items:center;font-size:12px">${L('DXF version', 'DXF 버전')} <select data-f="ver"><option value="keep"${dxfVer === 'keep' ? ' selected' : ''}>${L('Keep original', '원본 유지')} (${esc(r.dxf?.source ?? '')})</option><option value="r2000"${dxfVer === 'r2000' ? ' selected' : ''}>R2000 (${L('nanoCAD compatible', 'nanoCAD 호환')})</option></select>${r.dxf?.tooNew ? `<span>${L(`the original is newer than ${esc(r.dxf.limit)}: R2000 by default`, `원본이 ${esc(r.dxf.limit)}보다 새 버전이라 R2000 기본`)}</span>` : ''}</label>
    <div class="btns"><button class="pill" data-b="cancel">${L('Cancel', '취소')}</button><button class="pill${dwgDefault ? '' : ' primary'}" data-b="dxf">${L('Save as DXF', 'DXF로 저장')}</button>${r.imageOnly ? `<button class="pill" data-b="dwg-noimage">${L(`Save DWG without ${r.imageCount} IMAGE`, `IMAGE ${r.imageCount}개 제외하고 DWG 저장`)}</button>` : ''}<button class="pill${dwgDefault ? ' primary' : ''}" data-b="dwg">${L('Save as DWG', 'DWG로 저장')}${r.planB || !r.dwgOk ? ' (beta)' : ''}</button></div></div></div>`);
  dlg.addEventListener('click', e => {
    const b = (e.target as HTMLElement).closest('[data-b]') as HTMLElement | null; if (!b && e.target !== dlg) return;
    const on = (dlg.querySelector('[data-f=fc]') as HTMLInputElement).checked;
    const ver = (dlg.querySelector('[data-f=ver]') as HTMLSelectElement).value;
    if (b?.dataset.b === 'dxf' || b?.dataset.b === 'dwg' || b?.dataset.b === 'dwg-noimage') { bridge.call('setFontCompat', { on }); bridge.call('saveDialog', { dwg: b.dataset.b !== 'dxf', noImage: b.dataset.b === 'dwg-noimage', dxfVer: ver, fontCompat: on }); }
    dlg.remove();
  });
  dlg.addEventListener('dblclick', e => { const tr = (e.target as HTMLElement).closest('[data-k]') as HTMLElement | null; if (!tr) return; const it = r.items[+tr.dataset.k!]; if (it?.handles?.length) { document.dispatchEvent(new CustomEvent('nx-zoom-handles', { detail: it.handles })); dlg.remove(); } });
  document.body.append(dlg);
  (dlg.querySelector(dwgDefault ? '[data-b=dwg]' : '[data-b=dxf]') as HTMLElement).focus();
}

export class LayerRail {
  el = h(`<div class="card" style="flex:1 1 55%"><div class="card-h"><span class="ic">${I.layers()}</span><span class="t">${L('LAYERS', '레이어')}</span><span class="m"></span></div><div class="scroll body"></div><div class="newlayer"><input placeholder="${L('New layer name', '새 레이어 이름')}" maxlength="60"><button class="pill h26" data-a="add">${I.plus(11)} ${L('NEW', '추가')}</button></div></div>`);
  body = this.el.querySelector('.body') as HTMLElement; input = this.el.querySelector('input') as HTMLInputElement;
  dark = false; counts = new Map<string, number>(); private countDoc: Doc | null = null; private countVer = -1;
  render(S: any, doc: Doc | null) {
    if (doc !== this.countDoc || (doc?.ver ?? -1) !== this.countVer) {   // counted once per change of the drawing, not on every state message (100k entities ≈ tens of ms per click)
      this.countDoc = doc; this.countVer = doc?.ver ?? -1; this.counts.clear(); if (doc) for (const e of doc.ents.values()) this.counts.set(e.l.toLowerCase(), (this.counts.get(e.l.toLowerCase()) ?? 0) + 1);
    }
    const layers: any[] = S.layers ?? []; (this.el.querySelector('.m') as HTMLElement).textContent = layers.length ? `${layers.length} · ${L('current', '현재')} ${S.currentLayer}` : '';
    H(this.body).innerHTML = layers.length ? layers.map(l => `<div class="lrow${l.name === S.currentLayer ? ' on' : ''}${l.off || l.frozen ? ' off' : ''}" data-l="${esc(l.name)}" title="${esc(l.name)} · ${esc(l.lt)}"><span class="sw" style="background:${rgbCss(aciRgb(l.color, this.dark))}"></span><span class="n">${esc(l.name)}</span><span class="m">${this.counts.get(l.name.toLowerCase()) ?? 0}</span>
      <button class="ib${l.off ? '' : ' on'}" data-t="off" title="${l.off ? L('On', '켜기') : L('Off', '끄기')}">${l.off ? I.eyeOff() : I.eye()}</button><button class="ib${l.frozen ? ' on' : ''}" data-t="frozen" title="${L('Freeze / thaw', '동결 / 해동')}">${I.snow()}</button><button class="ib${l.locked ? ' on' : ''}" data-t="locked" title="${L('Lock / unlock', '잠금 / 해제')}">${l.locked ? I.lock() : I.unlock()}</button></div>`).join('') : `<div class="empty">${L('No drawing is open.', '열린 도면이 없습니다.')}</div>`;
  }
  constructor() {
    this.body.addEventListener('click', e => {
      const t = e.target as HTMLElement; const row = t.closest('[data-l]') as HTMLElement | null; if (!row) return; const name = row.dataset.l!;
      const tb = (t.closest('[data-t]') as HTMLElement | null)?.dataset.t;
      if (tb) { const on = tb === 'off' ? !row.classList.contains('off') || row.querySelector('[data-t=off]')!.classList.contains('on') : !row.querySelector(`[data-t=${tb}]`)!.classList.contains('on'); const args: any = { name }; if (tb === 'off') args.off = row.querySelector('[data-t=off]')!.classList.contains('on'); else args[tb] = on; bridge.call('layer.set', args); return; }
      bridge.call('setLayer', { layer: name });
    });
    const add = () => { const n = this.input.value.trim(); if (!n) return; bridge.call('layer.add', { name: n, color: 7, current: true }); this.input.value = ''; };
    this.el.querySelector('[data-a=add]')!.addEventListener('click', add); this.input.addEventListener('keydown', e => { if (e.key === 'Enter') add(); e.stopPropagation(); });
  }
}

export class PropsRail {
  el = h(`<div class="card" style="flex:1 1 45%"><div class="card-h"><span class="ic">${I.sliders()}</span><span class="t">${L('PROPERTIES', '속성')}</span><span class="m"></span></div><div class="scroll body"></div></div>`);
  body = this.el.querySelector('.body') as HTMLElement; ids: string[] = []; doc: Doc | null = null;
  render(S: any, doc: Doc | null, ids: string[]) {
    this.ids = ids; this.doc = doc; (this.el.querySelector('.m') as HTMLElement).textContent = ids.length ? `${ids.length} ${L('selected', '선택됨')}` : '';
    if (!doc || !ids.length) { this.body.innerHTML = `<div class="empty">${L('Select objects to see their properties.<br>Click · window (→) · crossing (←), double-click a text = edit.', '객체를 선택하면 속성이 보입니다.<br>클릭 · 창(→) · 교차(←) 선택, 텍스트 더블클릭 = 편집.')}</div>`; return; }
    const types = new Map<string, number>(); const layersSel = new Map<string, number>(); let single: Ent | null = ids.length === 1 ? doc.ents.get(ids[0]) ?? null : null;
    for (const id of ids) { const e = doc.ents.get(id); if (!e) continue; types.set(typeName(e), (types.get(typeName(e)) ?? 0) + 1); layersSel.set(e.l, (layersSel.get(e.l) ?? 0) + 1); }
    const layerOpts = (S.layers ?? []).map((l: any) => `<option value="${esc(l.name)}"${layersSel.size === 1 && layersSel.has(l.name) ? ' selected' : ''}>${esc(l.name)}</option>`).join('');
    let detail = '';
    if (single) {
      const p = single.p ?? [];
      if (single.t === 'L') detail = `<div class="mono">P1 ${fmtNum(p[0])}, ${fmtNum(p[1])}<br>P2 ${fmtNum(p[2])}, ${fmtNum(p[3])}<br>LEN ${fmtNum(Math.hypot(p[2] - p[0], p[3] - p[1]))}</div>`;
      else if (single.t === 'C') detail = `<div class="mono">CENTER ${fmtNum(p[0])}, ${fmtNum(p[1])}<br>RADIUS ${fmtNum(p[2])}</div>`;
      else if (single.t === 'A') detail = `<div class="mono">CENTER ${fmtNum(p[0])}, ${fmtNum(p[1])}<br>RADIUS ${fmtNum(p[2])}<br>ANGLES ${fmtNum(p[3], 2)}° → ${fmtNum(p[4], 2)}°</div>`;
      else if (single.t === 'P') detail = `<div class="mono">VERTICES ${p.length / 3}${single.cl ? ' · closed' : ''}${single.spline ? ' · spline' : ''}</div>`;
      else if (single.t === 'I') detail = `<div class="mono">BLOCK ${esc(single.blk)}<br>AT ${fmtNum(p[0])}, ${fmtNum(p[1])}<br>SCALE ${fmtNum(p[2], 3)} × ${fmtNum(p[3], 3)} · ROT ${fmtNum(p[4], 2)}°</div>`;
      else if (single.t === 'T') detail = `<div class="f"><label>TEXT</label><textarea data-f="text">${esc(single.s)}</textarea></div><div class="mono">H ${fmtNum(single.ht ?? 0, 3)} · ROT ${fmtNum(single.rot ?? 0, 2)}° · ${esc(single.st)} (${esc(single.font ?? '')})</div><button class="pill h26" data-a="apply">${L('APPLY', '적용')}</button>`;
      else if (single.t === 'M') detail = `<div class="f"><label>MTEXT (plain)</label><textarea data-f="mtext">${esc((single.lines ?? []).map(l => l.map(r => r.t).join('')).join('\n'))}</textarea></div><div class="mono">H ${fmtNum(single.ht ?? 0, 3)} · W ${fmtNum(single.w ?? 0, 2)} · ${esc(single.st)}</div><button class="pill h26" data-a="apply">${L('APPLY', '적용')}</button>`;
      else if (single.t === 'D') detail = `<div class="mono">DIMENSION kind ${single.kind} · ${single.meas != null ? fmtNum(single.meas, 3) : ''}<br>${esc(single.s || '<>')}</div>`;
      else if (single.t === 'H') detail = `<div class="mono">HATCH ${esc(single.pat)} ${single.solid ? 'solid' : ''} · ${(single.loops ?? []).length} loops</div>`;
    }
    this.body.innerHTML = `<div class="props"><div class="sum">${ids.length}<small>${L('object' + (ids.length > 1 ? 's' : ''), '객체')}</small></div><div class="types">${Array.from(types).map(([k, v]) => `<span class="chip">${esc(k)} ${v}</span>`).join('')}</div>
      <div class="f"><label>${L('LAYER', '레이어')}</label><select data-f="layer">${layersSel.size > 1 ? `<option value="">${L('(varies)', '(여러 값)')}</option>` : ''}${layerOpts}</select></div>${detail}</div>`;
  }
  constructor() {
    this.body.addEventListener('change', e => { const t = e.target as HTMLSelectElement; if (t.dataset.f === 'layer' && t.value) bridge.call('edit.layer', { ids: this.ids, layer: t.value }); });
    this.body.addEventListener('click', e => {
      const a = ((e.target as HTMLElement).closest('[data-a]') as HTMLElement | null)?.dataset.a; if (a !== 'apply' || this.ids.length !== 1) return;
      const ta = this.body.querySelector('textarea') as HTMLTextAreaElement | null; if (!ta) return;
      bridge.call('edit.text', { id: this.ids[0], text: ta.value, plain: ta.dataset.f === 'mtext' });
    });
    this.body.addEventListener('keydown', e => e.stopPropagation());
  }
}
export function typeName(e: Ent) { return ({ L: 'LINE', C: 'CIRCLE', A: 'ARC', E: 'ELLIPSE', P: e.spline ? 'SPLINE' : e.heavy ? 'POLYLINE' : 'LWPOLYLINE', T: e.attr ? 'ATTRIB' : 'TEXT', M: 'MTEXT', I: 'INSERT', H: 'HATCH', D: 'DIMENSION', S: 'SOLID', PT: 'POINT', X: e.name ?? 'UNKNOWN' } as Record<string, string>)[e.t] ?? e.t; }

export class CommandBar {
  el = h(`<div class="cmdbar"><div class="cmd-hist"></div><div class="cmd-line"><span class="ic">${I.terminal()}</span><span class="pr">${L('Command:', '명령:')}</span><input spellcheck="false" autocomplete="off" placeholder="L · PL · C · REC · A · EL · POL · DT · T · M · CO · TR · EX · E · S · O · MI · RO · DLI · Z · DI · U"><span class="hint">${L('Enter/Space · short right click = confirm · repeat · long right click = menu · Esc = cancel · F3 OSNAP · F8 ORTHO · F10 POLAR · F11 OTRACK · Shift = temporary ortho', 'Enter/Space·짧은 우클릭 = 확정·반복 · 긴 우클릭 = 메뉴 · Esc = 취소 · F3 OSNAP · F8 ORTHO · F10 POLAR · F11 OTRACK · Shift = 임시 직교')}</span></div></div>`);
  hist = this.el.querySelector('.cmd-hist') as HTMLElement; input = this.el.querySelector('input') as HTMLInputElement; pr = this.el.querySelector('.pr') as HTMLElement;
  lines: string[] = []; onSubmit: ((text: string) => void) | null = null; onEscape: (() => void) | null = null;
  /** True while a command asks for text (TEXT / MTEXT content): Space types a space instead of confirming. */
  spaceLiteral: () => boolean = () => false;
  constructor() {
    this.input.addEventListener('keydown', e => {
      if (/^F\d+$/.test(e.key) || e.ctrlKey || e.key === 'Shift') return;   // F3/F7–F10, Ctrl+S/Z/… and Shift (temporary ortho) keep working while typing
      e.stopPropagation();
      if (e.key === ' ' && !this.spaceLiteral() && !this.input.value.includes('"')) { const v = this.input.value; this.input.value = ''; this.onSubmit?.(v); e.preventDefault(); return; }   // AutoCAD: Space = Enter
      if (e.key === 'Enter') { const v = this.input.value; this.input.value = ''; this.onSubmit?.(v); e.preventDefault(); }
      else if (e.key === 'Escape') { this.input.value = ''; this.onEscape?.(); e.preventDefault(); }
    });
  }
  prompt(text: string) { this.pr.textContent = text; }
  /** Full history (last 300 lines); the bar shows the last three. */
  log: string[] = [];
  add(line: string) { this.log.push(line); if (this.log.length > 300) this.log.shift(); this.lines.push(line); if (this.lines.length > 3) this.lines.shift(); this.hist.textContent = this.lines.join('\n'); }
}
