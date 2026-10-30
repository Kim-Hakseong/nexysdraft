// Renderer: WebGL line pass (all visible entity geometry in one buffer) + Canvas2D overlay (text, solid fills, selection, snaps, previews).
import { Doc, Geom, TextItem, Run } from './doc';
import { aciRgb, rgbCss } from './util';

export interface View { cx: number; cy: number; scale: number; w: number; h: number; dpr: number }
export const w2s = (v: View, x: number, y: number): [number, number] => [(x - v.cx) * v.scale + v.w / 2, v.h / 2 - (y - v.cy) * v.scale];
export const s2w = (v: View, sx: number, sy: number): [number, number] => [(sx - v.w / 2) / v.scale + v.cx, (v.h / 2 - sy) / v.scale + v.cy];

const VS = `attribute vec2 a_pos; attribute vec3 a_col; uniform vec2 u_center; uniform vec2 u_scale; varying vec3 v_col;
void main(){ vec2 p = (a_pos - u_center) * u_scale; gl_Position = vec4(p, 0.0, 1.0); v_col = a_col; }`;
const FS = `precision mediump float; varying vec3 v_col; void main(){ gl_FragColor = vec4(v_col, 1.0); }`;

export class Renderer {
  gl: WebGLRenderingContext | null; prog: WebGLProgram | null = null; posBuf: WebGLBuffer | null = null; colBuf: WebGLBuffer | null = null; nVerts = 0;
  uCenter: WebGLUniformLocation | null = null; uScale: WebGLUniformLocation | null = null; aPos = 0; aCol = 0;
  origin: [number, number] = [0, 0];
  dark = false; doc: Doc | null = null; builtVer = -1; builtDark = false;
  texts: TextItem[] = []; fills: { col: number; poly: Float32Array; bbox: number[] }[] = [];
  textBoxes: number[][] = [];
  lastDrawMs = 0; drawnTexts = 0; lastVertexCount = 0; buildMs = 0;
  constructor(public glCanvas: HTMLCanvasElement, public ovCanvas: HTMLCanvasElement) {
    this.gl = glCanvas.getContext('webgl', { antialias: true, alpha: true, preserveDrawingBuffer: true, premultipliedAlpha: false }) as WebGLRenderingContext | null;
    if (this.gl) this.initGl();
  }
  private initGl() {
    const gl = this.gl!; const mk = (t: number, src: string) => { const s = gl.createShader(t)!; gl.shaderSource(s, src); gl.compileShader(s); return s; };
    const p = gl.createProgram()!; gl.attachShader(p, mk(gl.VERTEX_SHADER, VS)); gl.attachShader(p, mk(gl.FRAGMENT_SHADER, FS)); gl.linkProgram(p); this.prog = p;
    this.aPos = gl.getAttribLocation(p, 'a_pos'); this.aCol = gl.getAttribLocation(p, 'a_col'); this.uCenter = gl.getUniformLocation(p, 'u_center'); this.uScale = gl.getUniformLocation(p, 'u_scale');
    this.posBuf = gl.createBuffer(); this.colBuf = gl.createBuffer();
  }
  setDoc(doc: Doc | null) { this.doc = doc; this.builtVer = -1; }

  /** Rebuild the line buffer (visible entities) and the text/fill lists. */
  rebuild() {
    const t0 = performance.now(); const doc = this.doc; const gl = this.gl;
    this.texts = []; this.fills = []; this.textBoxes = [];
    if (!doc) { this.nVerts = 0; this.builtVer = -1; return; }
    const b = doc.bbox; this.origin = b ? [(b[0] + b[2]) / 2, (b[1] + b[3]) / 2] : [0, 0];
    let total = 0; const geoms: Geom[] = [];
    for (const h of doc.order) { const e = doc.ents.get(h)!; if (!doc.visible(e)) continue; const g = doc.geomOf(h); if (!g) continue; geoms.push(g); for (const p of g.parts) total += p.segs.length / 2; }
    const pos = new Float32Array(total * 2); const col = new Uint8Array(total * 3); let vi = 0;
    const ox = this.origin[0], oy = this.origin[1];
    for (const g of geoms) {
      for (const p of g.parts) {
        const c = aciRgb(p.col, this.dark); const r = c >> 16, gg = (c >> 8) & 255, bb = c & 255; const n = p.segs.length / 2;
        for (let i = 0; i < n; i++) { pos[(vi + i) * 2] = p.segs[i * 2] - ox; pos[(vi + i) * 2 + 1] = p.segs[i * 2 + 1] - oy; col[(vi + i) * 3] = r; col[(vi + i) * 3 + 1] = gg; col[(vi + i) * 3 + 2] = bb; }
        vi += n;
      }
      for (const t of g.texts) this.texts.push(t);
      for (const f of g.fills) { let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity; for (let i = 0; i < f.poly.length; i += 2) { const x = f.poly[i], y = f.poly[i + 1]; if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; } this.fills.push({ col: f.col, poly: f.poly, bbox: [x0, y0, x1, y1] }); }
    }
    this.nVerts = vi; this.lastVertexCount = vi;
    if (gl) { gl.bindBuffer(gl.ARRAY_BUFFER, this.posBuf); gl.bufferData(gl.ARRAY_BUFFER, pos, gl.STATIC_DRAW); gl.bindBuffer(gl.ARRAY_BUFFER, this.colBuf); gl.bufferData(gl.ARRAY_BUFFER, col, gl.STATIC_DRAW); }
    this.builtVer = doc.ver; this.builtDark = this.dark; this.buildMs = performance.now() - t0;
  }

  draw(v: View, overlay: (ctx: CanvasRenderingContext2D) => void) {
    const t0 = performance.now();
    if (this.doc && (this.builtVer !== this.doc.ver || this.builtDark !== this.dark)) this.rebuild();
    const W = Math.max(1, Math.round(v.w * v.dpr)), H = Math.max(1, Math.round(v.h * v.dpr));
    if (this.glCanvas.width !== W || this.glCanvas.height !== H) { this.glCanvas.width = W; this.glCanvas.height = H; }
    if (this.ovCanvas.width !== W || this.ovCanvas.height !== H) { this.ovCanvas.width = W; this.ovCanvas.height = H; }
    const gl = this.gl;
    if (gl && this.prog) {
      gl.viewport(0, 0, W, H); gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
      if (this.nVerts > 0) {
        gl.useProgram(this.prog);
        gl.uniform2f(this.uCenter, v.cx - this.origin[0], v.cy - this.origin[1]); gl.uniform2f(this.uScale, 2 * v.scale / v.w, 2 * v.scale / v.h);
        gl.bindBuffer(gl.ARRAY_BUFFER, this.posBuf); gl.enableVertexAttribArray(this.aPos); gl.vertexAttribPointer(this.aPos, 2, gl.FLOAT, false, 0, 0);
        gl.bindBuffer(gl.ARRAY_BUFFER, this.colBuf); gl.enableVertexAttribArray(this.aCol); gl.vertexAttribPointer(this.aCol, 3, gl.UNSIGNED_BYTE, true, 0, 0);
        gl.drawArrays(gl.LINES, 0, this.nVerts);
      }
    }
    const ctx = this.ovCanvas.getContext('2d')!; ctx.setTransform(v.dpr, 0, 0, v.dpr, 0, 0); ctx.clearRect(0, 0, v.w, v.h);
    if (!gl) this.drawLinesCanvas(ctx, v);
    this.drawFills(ctx, v); this.drawTexts(ctx, v);
    overlay(ctx);
    this.lastDrawMs = performance.now() - t0;
  }
  /** Fallback when WebGL is unavailable: Canvas2D lines (slower). */
  private drawLinesCanvas(ctx: CanvasRenderingContext2D, v: View) {
    const doc = this.doc; if (!doc) return;
    for (const h of doc.order) { const e = doc.ents.get(h)!; if (!doc.visible(e)) continue; const g = doc.geomOf(h); if (!g) continue; for (const p of g.parts) { ctx.strokeStyle = rgbCss(aciRgb(p.col, this.dark)); ctx.beginPath(); for (let i = 0; i + 3 < p.segs.length; i += 4) { const a = w2s(v, p.segs[i], p.segs[i + 1]), b = w2s(v, p.segs[i + 2], p.segs[i + 3]); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); } ctx.stroke(); } }
  }
  private drawFills(ctx: CanvasRenderingContext2D, v: View) {
    const [wx0, wy0] = s2w(v, 0, v.h), [wx1, wy1] = s2w(v, v.w, 0);
    for (const f of this.fills) {
      if (f.bbox[2] < wx0 || f.bbox[0] > wx1 || f.bbox[3] < wy0 || f.bbox[1] > wy1) continue;
      if ((f.bbox[2] - f.bbox[0]) * v.scale < 1 && (f.bbox[3] - f.bbox[1]) * v.scale < 1) continue;
      ctx.fillStyle = rgbCss(aciRgb(f.col, this.dark)); ctx.beginPath();
      for (let i = 0; i < f.poly.length; i += 2) { const p = w2s(v, f.poly[i], f.poly[i + 1]); if (i === 0) ctx.moveTo(p[0], p[1]); else ctx.lineTo(p[0], p[1]); }
      ctx.closePath(); ctx.fill();
    }
  }
  private drawTexts(ctx: CanvasRenderingContext2D, v: View) {
    const [wx0, wy0] = s2w(v, 0, v.h), [wx1, wy1] = s2w(v, v.w, 0); let n = 0; this.drawnTexts = 0;
    for (const t of this.texts) {
      const px = t.h * v.scale; if (px < 2.5) continue;
      const ax = t.mtext ? t.x : t.ax, ay = t.mtext ? t.y : t.ay; const span = Math.max(t.w, t.h * 40);
      if (ax + span < wx0 || ax - span > wx1 || ay + span < wy0 || ay - span > wy1) continue;
      if (++n > 4000) break;
      drawText(ctx, v, t, rgbCss(aciRgb(t.col, this.dark)));
    }
    this.drawnTexts = n;
  }
}

/** Draw one TEXT / MTEXT item: alignment (72/73 or MTEXT attachment 71), rotation, width factor, run fonts (MTEXT \f), fallback Malgun Gothic. */
export function drawText(ctx: CanvasRenderingContext2D, v: View, t: TextItem, color: string) {
  const px = t.h * v.scale; const ax = t.mtext ? t.x : t.ax, ay = t.mtext ? t.y : t.ay; const s = w2s(v, ax, ay);
  ctx.save(); ctx.translate(s[0], s[1]); ctx.rotate(-t.rot * Math.PI / 180); ctx.fillStyle = color; ctx.textBaseline = 'alphabetic';
  const fam = `"${t.font}", "Malgun Gothic", "Segoe UI", sans-serif`;
  if (!t.mtext) {
    ctx.font = `${px}px ${fam}`; const text = t.text ?? ''; const wf = t.wf || 1; const w = ctx.measureText(text).width * wf;
    let dx = 0, dy = 0;   // from the anchor to the text's left baseline (canvas coordinates: y down)
    if (t.ha === 1 || t.ha === 4) dx = -w / 2; else if (t.ha === 2) dx = -w; else if (t.ha === 3 || t.ha === 5) { const ins = w2s(v, t.x, t.y); dx = -Math.hypot(s[0] - ins[0], s[1] - ins[1]); }
    if (t.va === 2 || t.ha === 4) dy = px * 0.36; else if (t.va === 3) dy = px * 0.72; else if (t.va === 1) dy = 0; else dy = 0;
    if (t.ha === 3 || t.ha === 5) { const ins = w2s(v, t.x, t.y); const d = Math.hypot(s[0] - ins[0], s[1] - ins[1]); const k = d > 0 ? d / Math.max(1e-6, ctx.measureText(text).width) : wf; ctx.scale(k, 1); dx = -d / k; }
    else if (wf !== 1) { ctx.scale(wf, 1); dx /= wf; }
    if (t.bw) { ctx.scale(-1, 1); dx = -dx - w / (wf || 1); }
    if (t.ob) ctx.transform(1, 0, -Math.tan(t.ob * Math.PI / 180), 1, 0, 0);
    ctx.fillText(text, dx, dy);
    ctx.restore(); return;
  }
  // MTEXT: lines of runs, attachment 1..9, width → wrap
  runScale = v.scale;
  const lh = Math.max(px, (t.lines?.[0]?.[0]?.abs && t.lines[0][0].hf ? t.lines[0][0].hf * v.scale : px)) * 1.667 * (t.ls || 1); const wpx = t.w > t.h * 2 ? t.w * v.scale : 0;   // rect_width = 1.0 after a dxf2dwg round trip: no wrapping
  const lines: Run[][] = [];
  for (const line of t.lines ?? []) {
    if (wpx <= 0) { lines.push(line); continue; }
    // greedy wrap by words
    let cur: Run[] = []; let curW = 0;
    for (const r of line) {
      const f = runFont(ctx, r, px, fam, t.font); const words = r.t.split(/(\s+)/);
      for (const wd of words) {
        if (!wd) continue; ctx.font = f; const ww = ctx.measureText(wd).width * (r.wf ?? 1);
        if (curW + ww > wpx && curW > 0 && wd.trim()) { lines.push(cur); cur = []; curW = 0; if (!wd.trim()) continue; }
        cur.push({ ...r, t: wd }); curW += ww;
      }
    }
    lines.push(cur);
  }
  const nl = Math.max(1, lines.length); const widths = lines.map(l => { let w = 0; for (const r of l) { ctx.font = runFont(ctx, r, px, fam, t.font); w += ctx.measureText(r.t).width * (r.wf ?? 1); } return w; });
  const boxW = wpx > 0 ? wpx : Math.max(...widths, 1); const boxH = nl * lh;
  const col = (t.at - 1) % 3, row = Math.floor((t.at - 1) / 3);
  const left = col === 0 ? 0 : col === 1 ? -boxW / 2 : -boxW; const top = row === 0 ? 0 : row === 1 ? -boxH / 2 : -boxH;
  for (let i = 0; i < nl; i++) {
    const l = lines[i]; let x = left + (col === 1 ? (boxW - widths[i]) / 2 : col === 2 ? boxW - widths[i] : 0); const y = top + i * lh + px * 0.95;
    for (const r of l) { ctx.font = runFont(ctx, r, px, fam, t.font); const wf = r.wf ?? 1; if (wf !== 1) { ctx.save(); ctx.scale(wf, 1); ctx.fillText(r.t, x / wf, y); ctx.restore(); } else ctx.fillText(r.t, x, y); const w = ctx.measureText(r.t).width * wf; if (r.u) { ctx.fillRect(x, y + px * 0.12, w, Math.max(1, px * 0.05)); } x += w; }
  }
  ctx.restore();
}
let runScale = 1;
function runFont(ctx: CanvasRenderingContext2D, r: Run, px: number, fam: string, base: string): string {
  const size = r.abs && r.hf ? r.hf * runScale : px * (r.hf ?? 1);   // H2.5; = absolute height (world units), H1.5x; = factor
  const f = r.f ? `"${r.f}", "${base}", "Malgun Gothic", sans-serif` : fam;
  return `${r.i ? 'italic ' : ''}${r.b ? '600 ' : ''}${size}px ${f}`;
}
