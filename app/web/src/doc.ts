// Client-side document: entity records from the engine (PageDoc), tessellated geometry cache, grid index, hit testing and snapping.
export interface Run { t: string; f?: string | null; b?: number; i?: number; hf?: number; wf?: number; u?: number; c?: number | null; abs?: number }
export interface Ent {
  h: string; t: string; l: string; c: number; tc?: number; lt?: string; inv?: number; lw?: number; p?: number[]; cl?: number; w?: number; ws?: number[]; heavy?: number; spline?: number; ctrl?: number[];
  ht?: number; rot?: number; s?: string; ha?: number; va?: number; wf?: number; st?: string; font?: string; shx?: number; attr?: string; attdef?: number; ob?: number; bw?: number; ud?: number;
  at?: number; ls?: number; raw?: string; lines?: Run[][];
  blk?: string; attrs?: Ent[]; grid?: number[]; gridf?: number[];
  solid?: number; pat?: string; loops?: number[][]; hl?: number[]; lts?: number; kind?: number; meas?: number; name?: string;
}
export interface Layer { name: string; color: number; tc?: number | null; off: boolean; frozen: boolean; locked: boolean; lt: string; lw?: number }
export interface Block { name: string; bx: number; by: number; ents: Ent[] }
export interface TextItem { x: number; y: number; h: number; rot: number; text?: string; lines?: Run[][]; font: string; shx: boolean; ha: number; va: number; wf: number; ax: number; ay: number; w: number; at: number; ls: number; col: number; mtext: boolean; ob: number; bw: number; ud: number; h0: string }
/** segs = the geometry (hit test, snaps, selection highlight); draw = what is painted when the linetype is not continuous (the dashes). */
export interface Part { col: number; segs: Float32Array; draw?: Float32Array }
export interface Geom { parts: Part[]; fills: { col: number; poly: Float32Array }[]; texts: TextItem[]; bbox: [number, number, number, number] | null; snaps: number[] /* x,y,kind triples */ }
export const SNAP_END = 1, SNAP_MID = 2, SNAP_CEN = 3, SNAP_INT = 4, SNAP_QUAD = 5, SNAP_INS = 6;

const FONT_FILES: Record<string, string> = { malgun: 'Malgun Gothic', malgunbd: 'Malgun Gothic', gulim: 'Gulim', gulimche: 'GulimChe', batang: 'Batang', batangche: 'BatangChe', dotum: 'Dotum', dotumche: 'DotumChe', gungsuh: 'Gungsuh', arial: 'Arial', arialbd: 'Arial', times: 'Times New Roman', cour: 'Courier New', calibri: 'Calibri', tahoma: 'Tahoma', verdana: 'Verdana', segoeui: 'Segoe UI', consola: 'Consolas', hygothic: 'HY견고딕', hyhwpeq: 'HyhwpEQ' };
/** Font family for a style font file / family name; .shx (and unknown) → Malgun Gothic fallback. */
export function fontFamily(font: string | undefined, shx: boolean | undefined): string {
  if (!font) return 'Malgun Gothic';
  const f = font.trim(); const low = f.toLowerCase();
  if (low.endsWith('.shx') || shx) return 'Malgun Gothic';
  const base = low.replace(/^.*[\\/]/, '').replace(/\.(ttf|ttc|otf)$/, '');
  if (FONT_FILES[base]) return FONT_FILES[base];
  if (/\.(ttf|ttc|otf)$/.test(low)) return base;
  return f;
}

export class Doc {
  ents = new Map<string, Ent>();
  order: string[] = [];
  blocks = new Map<string, Block>();
  layers: Layer[] = [];
  layerMap = new Map<string, Layer>();
  geom = new Map<string, Geom>();
  bbox: [number, number, number, number] | null = null;
  counts: Record<string, number> = {};
  unknown = 0; path = ''; version = ''; codepage = '';
  // grid index
  private cell = 1; private gx0 = 0; private gy0 = 0; private cols = 1; private rows = 1; private grid: string[][] = []; private indexed = new Set<string>();
  ver = 0;
  /** Linetype patterns by lower-case name (dash > 0, gap < 0, dot 0) and the global scale $LTSCALE. */
  ltypes = new Map<string, number[]>(); ltscale = 1;

  load(snap: any) {
    this.ents.clear(); this.order = []; this.blocks.clear(); this.geom.clear(); this.layers = snap.layers ?? []; this.layerMap.clear(); for (const l of this.layers) this.layerMap.set(l.name.toLowerCase(), l);
    for (const [k, b] of Object.entries<any>(snap.blocks ?? {})) this.blocks.set(k.toLowerCase(), b as Block);
    this.ltypes.clear(); for (const t of (snap.ltypes ?? []) as { name: string; pattern: number[] }[]) if (t.pattern && t.pattern.length >= 2) this.ltypes.set(t.name.toLowerCase(), t.pattern); this.ltscale = snap.ltscale > 0 ? snap.ltscale : 1;
    for (const e of snap.ents as Ent[]) { this.ents.set(e.h, e); this.order.push(e.h); }
    this.counts = snap.counts ?? {}; this.unknown = snap.unknown ?? 0; this.path = snap.path ?? ''; this.version = snap.version ?? ''; this.codepage = snap.codepage ?? '';
    const t0 = performance.now(); this.computeBBox(); this.phase.geom = performance.now() - t0;
    const t1 = performance.now(); this.rebuildIndex(); this.phase.index = performance.now() - t1; this.ver++;
  }
  phase = { geom: 0, index: 0 };
  setLayers(layers: Layer[]) {
    const before = this.layers.map(l => `${l.name}|${l.off}|${l.frozen}|${l.color}|${l.lt}`).join(';');
    this.layers = layers; this.layerMap.clear(); for (const l of layers) this.layerMap.set(l.name.toLowerCase(), l);
    const after = layers.map(l => `${l.name}|${l.off}|${l.frozen}|${l.color}|${l.lt}`).join(';');
    if (before !== after) { this.geom.clear(); this.ver++; return true; }
    return false;
  }
  patch(upsert: Ent[], remove: string[], blocks: Record<string, Block> | null) {
    for (const [k, b] of Object.entries(blocks ?? {})) this.blocks.set(k.toLowerCase(), b);
    for (const h of remove ?? []) { if (this.ents.delete(h)) { const i = this.order.indexOf(h); if (i >= 0) this.order.splice(i, 1); } this.geom.delete(h); this.unindex(h); }
    for (const e of upsert ?? []) { if (!this.ents.has(e.h)) this.order.push(e.h); this.ents.set(e.h, e); this.geom.delete(e.h); this.unindex(e.h); this.index(e.h); }
    if (blocks && Object.keys(blocks).length) for (const e of this.ents.values()) if ((e.t === 'I' || e.t === 'D') && e.blk && blocks[e.blk]) { this.geom.delete(e.h); }
    this.computeBBox(); this.ver++;
  }
  /** Linetype table from the engine state (a linetype may be added after the drawing was loaded). Returns true when the dashes must be rebuilt. */
  setLinetypes(types: { name: string; pattern: number[] }[] | null | undefined, scale: number): boolean {
    const sig = (m: Map<string, number[]>, s: number) => s + '|' + Array.from(m).map(([k, v]) => k + ':' + v.join(',')).join(';');
    const before = sig(this.ltypes, this.ltscale); const next = new Map<string, number[]>(); for (const t of types ?? []) if (t.pattern && t.pattern.length >= 2) next.set(t.name.toLowerCase(), t.pattern);
    const sc = scale > 0 ? scale : 1; if (sig(next, sc) === before) return false;
    this.ltypes = next; this.ltscale = sc; this.geom.clear(); this.ver++; return true;
  }
  layer(name: string) { return this.layerMap.get(name.toLowerCase()); }
  visible(e: Ent): boolean { if (e.inv) return false; const l = this.layer(e.l); return !(l && (l.off || l.frozen)); }

  // ---------------------------------------------------------------- geometry
  geomOf(h: string): Geom | null {
    let g = this.geom.get(h); if (g) return g;
    const e = this.ents.get(h); if (!e) return null;
    g = this.build(e, null, 0); this.geom.set(h, g); return g;
  }
  /** Resolve the ACI colour of an entity inside an insert chain. */
  private colorOf(e: Ent, ctx: { col: number; layer: string } | null): number {
    if (e.c === 0) return ctx ? ctx.col : 7;
    if (e.c === 256 || e.c == null) { const ln = ctx && e.l === '0' ? ctx.layer : e.l; const l = this.layer(ln); return l ? Math.abs(l.color) : 7; }
    return e.c;
  }
  private build(e: Ent, ctx: { col: number; layer: string; m: number[] } | null, depth: number): Geom {
    if (!ctx) { const fast = this.buildFast(e); if (fast) { if (e.t !== 'T') { const pt = this.patternOf(e, null); if (pt) for (const part of fast.parts) { const d = dashSegs(part.segs, pt.pat, pt.scale); if (d) part.draw = d; } } return fast; } }
    return this.buildGeneral(e, ctx, depth);
  }
  /** The dash pattern an entity is drawn with, or null for a continuous line. BYLAYER takes the layer's linetype (the insert's layer for layer 0 inside a block). */
  private patternOf(e: Ent, ctx: { layer: string } | null): { pat: number[]; scale: number } | null {
    let name = e.lt;
    if (!name) { const l = this.layer(ctx && e.l === '0' ? ctx.layer : e.l); name = l?.lt; }
    if (!name) return null; const low = name.toLowerCase(); if (low === 'continuous' || low === 'bylayer' || low === 'byblock') return null;
    const pat = this.ltypes.get(low); if (!pat) return null;
    return { pat, scale: this.ltscale * (e.lts && e.lts > 0 ? e.lts : 1) };
  }
  /** Closure-free path for the common top-level entities (lines, circles, arcs, plain polylines, text): the 100k open gate. */
  private buildFast(e: Ent): Geom | null {
    const p = e.p; const col = this.colorOf(e, null);
    switch (e.t) {
      case 'L': { if (!p) return null; const segs = new Float32Array([p[0], p[1], p[2], p[3]]); const bbox: [number, number, number, number] = [Math.min(p[0], p[2]), Math.min(p[1], p[3]), Math.max(p[0], p[2]), Math.max(p[1], p[3])]; return { parts: [{ col, segs }], fills: [], texts: [], bbox, snaps: [p[0], p[1], SNAP_END, p[2], p[3], SNAP_END, (p[0] + p[2]) / 2, (p[1] + p[3]) / 2, SNAP_MID] }; }
      case 'C': { if (!p) return null; const pts = arcPts(p[0], p[1], p[2], 0, 360); const segs = polyToSegs(pts, false); return { parts: [{ col, segs }], fills: [], texts: [], bbox: [p[0] - p[2], p[1] - p[2], p[0] + p[2], p[1] + p[2]], snaps: [p[0], p[1], SNAP_CEN, p[0] + p[2], p[1], SNAP_QUAD, p[0], p[1] + p[2], SNAP_QUAD, p[0] - p[2], p[1], SNAP_QUAD, p[0], p[1] - p[2], SNAP_QUAD] }; }
      case 'A': { if (!p) return null; const pts = arcPts(p[0], p[1], p[2], p[3], p[4]); const segs = polyToSegs(pts, false); let sw = p[4] - p[3]; while (sw <= 0) sw += 360; const am = (p[3] + sw / 2) * Math.PI / 180; return { parts: [{ col, segs }], fills: [], texts: [], bbox: bboxOf(pts), snaps: [pts[0], pts[1], SNAP_END, pts[pts.length - 2], pts[pts.length - 1], SNAP_END, p[0], p[1], SNAP_CEN, p[0] + p[2] * Math.cos(am), p[1] + p[2] * Math.sin(am), SNAP_MID] }; }
      case 'P': {
        if (!p || e.ws) return null;   // a polyline with a width goes the general way (filled outline)
        const n = p.length / 3; const pts: number[] = []; const snaps: number[] = []; const closed = !!e.cl;
        for (let i = 0; i < n; i++) {
          const x = p[i * 3], y = p[i * 3 + 1], b = p[i * 3 + 2]; if (i === 0) pts.push(x, y); if (!e.spline) snaps.push(x, y, SNAP_END);
          if (i === n - 1 && !closed) break;
          const nx = p[((i + 1) % n) * 3], ny = p[((i + 1) % n) * 3 + 1];
          if (Math.abs(b) < 1e-12) { pts.push(nx, ny); if (!e.spline) snaps.push((x + nx) / 2, (y + ny) / 2, SNAP_MID); } else bulgePts(x, y, nx, ny, b, pts);
        }
        return { parts: [{ col, segs: polyToSegs(pts, false) }], fills: [], texts: [], bbox: bboxOf(pts), snaps };
      }
      case 'T': {
        if (!p) return null; const anchor = (e.ha || e.va) ? [p[2], p[3]] : [p[0], p[1]];
        const t: TextItem = { x: p[0], y: p[1], ax: anchor[0], ay: anchor[1], h: e.ht ?? 2.5, rot: e.rot ?? 0, text: e.s ?? '', font: fontFamily(e.font, !!e.shx), shx: !!e.shx, ha: e.ha ?? 0, va: e.va ?? 0, wf: e.wf ?? 1, w: 0, at: 0, ls: 1, col, mtext: false, ob: e.ob ?? 0, bw: e.bw ?? 0, ud: e.ud ?? 0, h0: e.h };
        return { parts: [], fills: [], texts: [t], bbox: textBox(t), snaps: [anchor[0], anchor[1], SNAP_INS] };
      }
    }
    return null;
  }
  private buildGeneral(e: Ent, ctx: { col: number; layer: string; m: number[] } | null, depth: number): Geom {
    const g: Geom = { parts: [], fills: [], texts: [], bbox: null, snaps: [] };
    const acc: number[] = []; const col = this.colorOf(e, ctx);
    const M = ctx?.m ?? [1, 0, 0, 1, 0, 0];
    const T = (x: number, y: number): [number, number] => [M[0] * x + M[2] * y + M[4], M[1] * x + M[3] * y + M[5]];
    const kscale = Math.sqrt(Math.abs(M[0] * M[3] - M[1] * M[2]));
    const mrot = Math.atan2(M[1], M[0]) * 180 / Math.PI;
    const seg = (x0: number, y0: number, x1: number, y1: number) => { const a = T(x0, y0), b = T(x1, y1); acc.push(a[0], a[1], b[0], b[1]); };
    const poly = (pts: number[], closed: boolean) => { for (let i = 0; i + 3 < pts.length; i += 2) seg(pts[i], pts[i + 1], pts[i + 2], pts[i + 3]); if (closed && pts.length >= 4) seg(pts[pts.length - 2], pts[pts.length - 1], pts[0], pts[1]); };
    const snap = (x: number, y: number, k: number) => { const p = T(x, y); g.snaps.push(p[0], p[1], k); };
    const p = e.p ?? [];
    switch (e.t) {
      case 'L': seg(p[0], p[1], p[2], p[3]); snap(p[0], p[1], SNAP_END); snap(p[2], p[3], SNAP_END); snap((p[0] + p[2]) / 2, (p[1] + p[3]) / 2, SNAP_MID); break;
      case 'C': { poly(arcPts(p[0], p[1], p[2], 0, 360), true); snap(p[0], p[1], SNAP_CEN); for (const a of [0, 90, 180, 270]) snap(p[0] + p[2] * Math.cos(a * Math.PI / 180), p[1] + p[2] * Math.sin(a * Math.PI / 180), SNAP_QUAD); break; }
      case 'A': { const pts = arcPts(p[0], p[1], p[2], p[3], p[4]); poly(pts, false); snap(pts[0], pts[1], SNAP_END); snap(pts[pts.length - 2], pts[pts.length - 1], SNAP_END); snap(p[0], p[1], SNAP_CEN); let sw = p[4] - p[3]; while (sw <= 0) sw += 360; const am = (p[3] + sw / 2) * Math.PI / 180; snap(p[0] + p[2] * Math.cos(am), p[1] + p[2] * Math.sin(am), SNAP_MID); break; }
      case 'E': { const pts = ellPts(p[0], p[1], p[2], p[3], p[4], p[5], p[6]); poly(pts, false); snap(p[0], p[1], SNAP_CEN); snap(pts[0], pts[1], SNAP_END); snap(pts[pts.length - 2], pts[pts.length - 1], SNAP_END); break; }
      case 'P': {
        const n = p.length / 3; const pts: number[] = []; const closed = !!e.cl; const ws = e.ws; const hw: number[] = [];
        for (let i = 0; i < n; i++) {
          const x = p[i * 3], y = p[i * 3 + 1], b = p[i * 3 + 2]; if (i === 0) pts.push(x, y); if (!e.spline) snap(x, y, SNAP_END);
          const last = i === n - 1; if (last && !closed) break;
          const nx = p[((i + 1) % n) * 3], ny = p[((i + 1) % n) * 3 + 1]; const before = pts.length / 2;
          if (Math.abs(b) < 1e-12) { pts.push(nx, ny); if (!e.spline) snap((x + nx) / 2, (y + ny) / 2, SNAP_MID); } else bulgePts(x, y, nx, ny, b, pts);
          if (ws) { const k = pts.length / 2 - before; const a = (ws[i * 2] ?? 0) / 2, z = (ws[i * 2 + 1] ?? 0) / 2; for (let j = 0; j < k; j++) hw.push(a + (z - a) * j / k, a + (z - a) * (j + 1) / k); }
        }
        poly(pts, false);
        // width (constant or tapering): the outline is filled, as in AutoCAD
        if (ws) for (const q of widePolys(pts, hw, closed)) { const o = new Float32Array(q.length); for (let i = 0; i < q.length; i += 2) { const t = T(q[i], q[i + 1]); o[i] = t[0]; o[i + 1] = t[1]; } g.fills.push({ col, poly: o }); }
        break;
      }
      case 'S': { seg(p[0], p[1], p[2], p[3]); seg(p[2], p[3], p[6], p[7]); seg(p[6], p[7], p[4], p[5]); seg(p[4], p[5], p[0], p[1]); const q: number[] = []; for (const [x, y] of [[p[0], p[1]], [p[2], p[3]], [p[6], p[7]], [p[4], p[5]]]) { const t = T(x, y); q.push(t[0], t[1]); } g.fills.push({ col, poly: new Float32Array(q) }); for (let i = 0; i < 8; i += 2) snap(p[i], p[i + 1], SNAP_END); break; }
      case 'PT': { seg(p[0] - 1e-9, p[1], p[0] + 1e-9, p[1]); snap(p[0], p[1], SNAP_END); break; }
      case 'H': {
        if (e.hl && e.hl.length >= 4) { for (let i = 0; i + 3 < e.hl.length; i += 4) seg(e.hl[i], e.hl[i + 1], e.hl[i + 2], e.hl[i + 3]); break; }   // pattern lines (computed by the engine); no outline, as in AutoCAD
        for (const loop of e.loops ?? []) { poly(loop, true); if (e.solid) { const q: number[] = []; for (let i = 0; i < loop.length; i += 2) { const t = T(loop[i], loop[i + 1]); q.push(t[0], t[1]); } g.fills.push({ col, poly: new Float32Array(q) }); } }
        break;
      }
      case 'T': {
        const anchor = (e.ha || e.va) ? [p[2], p[3]] : [p[0], p[1]]; const a = T(anchor[0], anchor[1]); const ins = T(p[0], p[1]);
        g.texts.push({ x: ins[0], y: ins[1], ax: a[0], ay: a[1], h: (e.ht ?? 2.5) * kscale, rot: (e.rot ?? 0) + mrot, text: e.s ?? '', font: fontFamily(e.font, !!e.shx), shx: !!e.shx, ha: e.ha ?? 0, va: e.va ?? 0, wf: e.wf ?? 1, w: 0, at: 0, ls: 1, col, mtext: false, ob: e.ob ?? 0, bw: e.bw ?? 0, ud: e.ud ?? 0, h0: e.h });
        snap(anchor[0], anchor[1], SNAP_INS); break;
      }
      case 'M': {
        const a = T(p[0], p[1]); const r0 = e.lines?.[0]?.[0]; const hM = (e.ht && e.ht > 0) ? e.ht : (r0?.abs && r0.hf ? r0.hf : 2.5);
        g.texts.push({ x: a[0], y: a[1], ax: a[0], ay: a[1], h: hM * kscale, rot: (e.rot ?? 0) + mrot, lines: e.lines ?? [], font: fontFamily(e.font, !!e.shx), shx: !!e.shx, ha: 0, va: 0, wf: 1, w: (e.w ?? 0) * kscale, at: e.at ?? 1, ls: e.ls ?? 1, col, mtext: true, ob: 0, bw: 0, ud: 0, h0: e.h });
        snap(p[0], p[1], SNAP_INS); break;
      }
      case 'I': case 'D': {
        const b = this.blocks.get((e.blk ?? '').toLowerCase());
        if (e.t === 'I') snap(p[0], p[1], SNAP_INS);
        if (e.t === 'D') { snap(p[4], p[5], SNAP_END); snap(p[6], p[7], SNAP_END); }
        if (b && depth < 8) {
          const sx = e.t === 'I' ? p[2] : 1, sy = e.t === 'I' ? p[3] : 1, rot = e.t === 'I' ? (p[4] ?? 0) * Math.PI / 180 : 0, ix = e.t === 'I' ? p[0] : 0, iy = e.t === 'I' ? p[1] : 0;
          const cr = Math.cos(rot), sr = Math.sin(rot);
          const cols = e.grid?.[0] ?? 1, rows = e.grid?.[1] ?? 1, cs = e.gridf?.[0] ?? 0, rs = e.gridf?.[1] ?? 0;
          for (let r = 0; r < rows; r++) for (let c = 0; c < cols; c++) {
            // local: (x - bx) * sx, (y - by) * sy → rotate → translate (+ grid offset in block units)
            const ox = c * cs, oy = r * rs;
            const L = [sx * cr, sx * sr, -sy * sr, sy * cr, ix + (ox * cr - oy * sr), iy + (ox * sr + oy * cr)];
            L[4] -= b.bx * L[0] + b.by * L[2]; L[5] -= b.bx * L[1] + b.by * L[3];
            const W = mul(M, L);
            const ictx = { col, layer: e.l, m: W };
            for (const ce of b.ents) {
              if (ce.inv) continue; const cl = this.layer(ce.l); if (cl && ce.l !== '0' && (cl.off || cl.frozen)) continue;
              if (ce.t === 'T' && ce.attdef && e.t === 'I') continue;   // ATTDEF inside a block: shown only through the insert's ATTRIB values
              const cg = this.build(ce, ictx, depth + 1);
              for (const part of cg.parts) g.parts.push(part); for (const f of cg.fills) g.fills.push(f); for (const t of cg.texts) g.texts.push(t);
            }
          }
          if (e.t === 'I') for (const at of e.attrs ?? []) { const ag = this.build(at, { col, layer: e.l, m: M }, depth + 1); for (const t of ag.texts) g.texts.push(t); }
        } else if (e.t === 'D') {
          // no block: draw a plain linear dimension from the definition points
          seg(p[4], p[5], p[0], p[1]); seg(p[6], p[7], p[0], p[1]);
          g.texts.push({ x: T(p[2], p[3])[0], y: T(p[2], p[3])[1], ax: T(p[2], p[3])[0], ay: T(p[2], p[3])[1], h: 2.5 * kscale, rot: e.rot ?? 0, text: e.s && e.s !== '<>' ? e.s : (e.meas ?? 0).toFixed(2), font: 'Malgun Gothic', shx: true, ha: 1, va: 2, wf: 1, w: 0, at: 0, ls: 1, col, mtext: false, ob: 0, bw: 0, ud: 0, h0: e.h });
        }   // INSERT of an unresolved block: nothing to draw (no stray cross inflating the extents)
        break;
      }
      default: break;
    }
    if (acc.length) { const part: Part = { col, segs: new Float32Array(acc) }; if (e.t === 'L' || e.t === 'C' || e.t === 'A' || e.t === 'E' || e.t === 'P') { const pt = this.patternOf(e, ctx); if (pt) { const d = dashSegs(part.segs, pt.pat, pt.scale * kscale); if (d) part.draw = d; } } g.parts.push(part); }
    // bbox from parts, fills and text anchors
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    const add = (x: number, y: number) => { if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; };
    for (const part of g.parts) for (let i = 0; i < part.segs.length; i += 2) add(part.segs[i], part.segs[i + 1]);
    for (const f of g.fills) for (let i = 0; i < f.poly.length; i += 2) add(f.poly[i], f.poly[i + 1]);
    for (const t of g.texts) { const [bx0, by0, bx1, by1] = textBox(t); add(bx0, by0); add(bx1, by1); }
    g.bbox = x0 <= x1 ? [x0, y0, x1, y1] : null;
    return g;
  }

  computeBBox() {
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity; let n = 0;
    for (const h of this.order) { const e = this.ents.get(h)!; if (!this.visible(e)) continue; const g = this.geomOf(h); if (!g?.bbox) continue; const b = g.bbox; if (!isFinite(b[0]) || Math.abs(b[0]) > 1e14 || Math.abs(b[2]) > 1e14) continue; if (b[0] < x0) x0 = b[0]; if (b[1] < y0) y0 = b[1]; if (b[2] > x1) x1 = b[2]; if (b[3] > y1) y1 = b[3]; n++; }
    this.bbox = n > 0 ? [x0, y0, x1, y1] : null;
  }

  // ---------------------------------------------------------------- index
  rebuildIndex() {
    this.indexed.clear(); this.grid = [];
    const b = this.bbox; if (!b) { this.cols = this.rows = 1; this.grid = [[]]; return; }
    const n = Math.max(1, this.order.length); const w = Math.max(1e-9, b[2] - b[0]), hgt = Math.max(1e-9, b[3] - b[1]);
    const cells = Math.min(400 * 400, Math.max(16, n / 4)); this.cell = Math.sqrt(w * hgt / cells) || 1;
    this.cols = Math.min(2000, Math.max(1, Math.ceil(w / this.cell) + 1)); this.rows = Math.min(2000, Math.max(1, Math.ceil(hgt / this.cell) + 1)); this.gx0 = b[0]; this.gy0 = b[1];
    this.grid = new Array(this.cols * this.rows); for (let i = 0; i < this.grid.length; i++) this.grid[i] = [];
    for (const h of this.order) this.index(h);
  }
  private cellRange(b: [number, number, number, number]) {
    const c0 = Math.max(0, Math.min(this.cols - 1, Math.floor((b[0] - this.gx0) / this.cell))), c1 = Math.max(0, Math.min(this.cols - 1, Math.floor((b[2] - this.gx0) / this.cell)));
    const r0 = Math.max(0, Math.min(this.rows - 1, Math.floor((b[1] - this.gy0) / this.cell))), r1 = Math.max(0, Math.min(this.rows - 1, Math.floor((b[3] - this.gy0) / this.cell)));
    return [c0, c1, r0, r1];
  }
  private index(h: string) {
    if (!this.grid.length) return; const g = this.geomOf(h); if (!g?.bbox) return;
    const [c0, c1, r0, r1] = this.cellRange(g.bbox); if ((c1 - c0 + 1) * (r1 - r0 + 1) > 5000) { this.grid[0].push(h); this.indexed.add(h); return; }
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) this.grid[r * this.cols + c].push(h);
    this.indexed.add(h);
  }
  private unindex(h: string) { if (!this.indexed.has(h)) return; for (const cell of this.grid) { const i = cell.indexOf(h); if (i >= 0) cell.splice(i, 1); } this.indexed.delete(h); }
  /** Entity ids whose bbox intersects the rect. */
  query(x0: number, y0: number, x1: number, y1: number): Set<string> {
    const out = new Set<string>(); if (!this.grid.length) return out;
    const [c0, c1, r0, r1] = this.cellRange([Math.min(x0, x1), Math.min(y0, y1), Math.max(x0, x1), Math.max(y0, y1)]);
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) for (const h of this.grid[r * this.cols + c]) out.add(h);
    for (const h of this.grid[0]) out.add(h);
    const ax0 = Math.min(x0, x1), ay0 = Math.min(y0, y1), ax1 = Math.max(x0, x1), ay1 = Math.max(y0, y1);
    for (const h of Array.from(out)) { const b = this.geomOf(h)?.bbox; if (!b || b[2] < ax0 || b[0] > ax1 || b[3] < ay0 || b[1] > ay1) out.delete(h); }
    return out;
  }
  /** Window (inside) or crossing selection. */
  select(x0: number, y0: number, x1: number, y1: number, crossing: boolean): string[] {
    const ax0 = Math.min(x0, x1), ay0 = Math.min(y0, y1), ax1 = Math.max(x0, x1), ay1 = Math.max(y0, y1); const out: string[] = [];
    for (const h of this.query(ax0, ay0, ax1, ay1)) {
      const e = this.ents.get(h); if (!e || !this.visible(e)) continue; const l = this.layer(e.l); if (l?.locked) continue;
      const b = this.geomOf(h)!.bbox!;
      if (b[0] >= ax0 && b[2] <= ax1 && b[1] >= ay0 && b[3] <= ay1) { out.push(h); continue; }
      if (crossing) { const g = this.geomOf(h)!; if (this.crosses(g, ax0, ay0, ax1, ay1)) out.push(h); }
    }
    return out;
  }
  private crosses(g: Geom, x0: number, y0: number, x1: number, y1: number): boolean {
    for (const part of g.parts) for (let i = 0; i + 3 < part.segs.length; i += 4) if (segRect(part.segs[i], part.segs[i + 1], part.segs[i + 2], part.segs[i + 3], x0, y0, x1, y1)) return true;
    for (const f of g.fills) for (let i = 0; i + 3 < f.poly.length; i += 2) if (segRect(f.poly[i], f.poly[i + 1], f.poly[i + 2], f.poly[i + 3], x0, y0, x1, y1)) return true;
    for (const t of g.texts) { const b = textBox(t); if (!(b[2] < x0 || b[0] > x1 || b[3] < y0 || b[1] > y1)) return true; }
    return false;
  }
  /** Nearest entity within tol (world units) of a point. */
  hit(x: number, y: number, tol: number): string | null {
    let best: string | null = null, bestD = tol;
    for (const h of this.query(x - tol, y - tol, x + tol, y + tol)) {
      const e = this.ents.get(h); if (!e || !this.visible(e)) continue; const l = this.layer(e.l); if (l?.locked) continue;
      const g = this.geomOf(h)!; let d = Infinity;
      for (const part of g.parts) for (let i = 0; i + 3 < part.segs.length; i += 4) { const dd = distSeg(x, y, part.segs[i], part.segs[i + 1], part.segs[i + 2], part.segs[i + 3]); if (dd < d) d = dd; }
      for (const f of g.fills) if (pointInPoly(x, y, f.poly)) d = 0;
      for (const t of g.texts) { const b = textBox(t); if (x >= b[0] - tol && x <= b[2] + tol && y >= b[1] - tol && y <= b[3] + tol) d = Math.min(d, tol * 0.9); }
      if (d < bestD) { bestD = d; best = h; }
    }
    return best;
  }
  /** Snap candidates near a point: entity snap points + intersections of nearby segments. */
  snap(x: number, y: number, tol: number, exclude: Set<string> | null): { x: number; y: number; k: number } | null {
    let best: { x: number; y: number; k: number } | null = null; let bestD = tol;
    const ids = Array.from(this.query(x - tol * 4, y - tol * 4, x + tol * 4, y + tol * 4)).filter(h => !exclude?.has(h) && this.visible(this.ents.get(h)!));
    for (const h of ids) {
      const g = this.geomOf(h)!;
      for (let i = 0; i + 2 < g.snaps.length; i += 3) { const d = Math.hypot(g.snaps[i] - x, g.snaps[i + 1] - y); if (d < bestD) { bestD = d; best = { x: g.snaps[i], y: g.snaps[i + 1], k: g.snaps[i + 2] }; } }
    }
    // intersections among the nearest segments
    const near: number[][] = [];
    for (const h of ids.slice(0, 40)) { const g = this.geomOf(h)!; for (const part of g.parts) for (let i = 0; i + 3 < part.segs.length; i += 4) { if (distSeg(x, y, part.segs[i], part.segs[i + 1], part.segs[i + 2], part.segs[i + 3]) <= tol * 2) near.push([part.segs[i], part.segs[i + 1], part.segs[i + 2], part.segs[i + 3], near.length]); if (near.length > 200) break; } }
    for (let a = 0; a < near.length; a++) for (let b = a + 1; b < near.length; b++) {
      const ip = segInt(near[a][0], near[a][1], near[a][2], near[a][3], near[b][0], near[b][1], near[b][2], near[b][3]); if (!ip) continue;
      const d = Math.hypot(ip[0] - x, ip[1] - y); if (d < bestD || (best && best.k !== SNAP_INT && d <= bestD + tol * 0.25 && d < tol)) { bestD = d; best = { x: ip[0], y: ip[1], k: SNAP_INT }; }
    }
    return best;
  }
}

// ---------------------------------------------------------------- geometry helpers
/** Cut a run of segments (x0,y0,x1,y1 …, consecutive ones continue the pattern) into the dashes of a linetype. Null = draw it continuous
 *  (degenerate pattern, or more than 60 000 dashes — a pattern that small would look solid anyway). */
export function dashSegs(segs: Float32Array, pattern: number[], scale: number): Float32Array | null {
  const pat = pattern.map(v => v * scale); const period = pat.reduce((a, v) => a + Math.abs(v), 0); if (!(period > 1e-9)) return null;
  let total = 0; for (let i = 0; i + 3 < segs.length; i += 4) total += Math.hypot(segs[i + 2] - segs[i], segs[i + 3] - segs[i + 1]);
  if (total / period * pat.length > 60000 || total < period * 0.5) return null;
  const dot = period * 0.02; const out: number[] = []; let k = 0, left = Math.abs(pat[0]) || dot, px = NaN, py = NaN;
  for (let i = 0; i + 3 < segs.length; i += 4) {
    const x0 = segs[i], y0 = segs[i + 1], x1 = segs[i + 2], y1 = segs[i + 3]; const len = Math.hypot(x1 - x0, y1 - y0); if (len <= 0) continue;
    if (!(Math.abs(x0 - px) < 1e-6 * (1 + Math.abs(x0)) && Math.abs(y0 - py) < 1e-6 * (1 + Math.abs(y0)))) { k = 0; left = Math.abs(pat[0]) || dot; }   // a new run starts the pattern again
    const ux = (x1 - x0) / len, uy = (y1 - y0) / len; let s = 0;
    while (s < len - 1e-12) {
      const step = Math.min(left, len - s);
      if (pat[k] >= 0) out.push(x0 + ux * s, y0 + uy * s, x0 + ux * (s + step), y0 + uy * (s + step));
      s += step; left -= step;
      if (left <= 1e-12) { k = (k + 1) % pat.length; left = Math.abs(pat[k]) || dot; }
    }
    px = x1; py = y1;
  }
  return new Float32Array(out);
}
function polyToSegs(pts: number[], closed: boolean): Float32Array {
  const n = pts.length / 2; if (n < 2) return new Float32Array(0); const m = closed ? n : n - 1; const out = new Float32Array(m * 4);
  for (let i = 0; i < m; i++) { const j = (i + 1) % n; out[i * 4] = pts[i * 2]; out[i * 4 + 1] = pts[i * 2 + 1]; out[i * 4 + 2] = pts[j * 2]; out[i * 4 + 3] = pts[j * 2 + 1]; }
  return out;
}
function bboxOf(pts: number[]): [number, number, number, number] | null {
  if (pts.length < 2) return null; let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (let i = 0; i < pts.length; i += 2) { const x = pts[i], y = pts[i + 1]; if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
  return [x0, y0, x1, y1];
}
export function mul(a: number[], b: number[]): number[] {
  // a ∘ b: apply b first, then a (2D affine [a,b,c,d,e,f])
  return [a[0] * b[0] + a[2] * b[1], a[1] * b[0] + a[3] * b[1], a[0] * b[2] + a[2] * b[3], a[1] * b[2] + a[3] * b[3], a[0] * b[4] + a[2] * b[5] + a[4], a[1] * b[4] + a[3] * b[5] + a[5]];
}
export function arcPts(cx: number, cy: number, r: number, a0: number, a1: number): number[] {
  let sw = a1 - a0; if (sw <= 0) sw += 360; if (Math.abs(a1 - a0) < 1e-9) sw = 360;
  const n = Math.max(4, Math.min(96, Math.ceil(sw / 360 * 64))); const out: number[] = [];
  for (let i = 0; i <= n; i++) { const a = (a0 + sw * i / n) * Math.PI / 180; out.push(cx + r * Math.cos(a), cy + r * Math.sin(a)); }
  return out;
}
export function ellPts(cx: number, cy: number, mx: number, my: number, ratio: number, s: number, e: number): number[] {
  let sw = e - s; while (sw <= 1e-12) sw += Math.PI * 2; const nx = -my * ratio, ny = mx * ratio; const n = Math.max(8, Math.min(128, Math.ceil(sw / (Math.PI * 2) * 72))); const out: number[] = [];
  for (let i = 0; i <= n; i++) { const t = s + sw * i / n; out.push(cx + mx * Math.cos(t) + nx * Math.sin(t), cy + my * Math.cos(t) + ny * Math.sin(t)); }
  return out;
}
/** Outlines of a polyline with width. `pts` = the tessellated path (x, y …), `hw` = half width at the start and at the end of every segment of it.
 *  One polygon per stretch of continuous width (mitred corners, square ends); a closed polyline of one width gives a ring (outer loop + inner loop, joined by a bridge). */
export function widePolys(pts: number[], hw: number[], closed: boolean): number[][] {
  const out: number[][] = []; const m = pts.length / 2 - 1; if (m < 1) return out;
  type Seg = { x0: number; y0: number; x1: number; y1: number; nx: number; ny: number; a: number; z: number };
  const segs: Seg[] = [];
  for (let i = 0; i < m; i++) { const x0 = pts[i * 2], y0 = pts[i * 2 + 1], x1 = pts[i * 2 + 2], y1 = pts[i * 2 + 3]; const dx = x1 - x0, dy = y1 - y0, l = Math.hypot(dx, dy); if (l < 1e-12) continue; segs.push({ x0, y0, x1, y1, nx: -dy / l, ny: dx / l, a: hw[i * 2] ?? 0, z: hw[i * 2 + 1] ?? 0 }); }
  const joined = (p: Seg, q: Seg) => Math.abs(p.z - q.a) <= 1e-9 * Math.max(1, p.z) && p.z > 0;
  const mitre = (p: Seg, q: Seg, h: number): [number, number] => { let mx = p.nx + q.nx, my = p.ny + q.ny; const l = Math.hypot(mx, my); if (l < 1e-6) return [p.nx * h, p.ny * h]; const k = Math.min(2 / l, 4) * h / l; return [mx * k, my * k]; };
  const wide = (s: Seg) => s.a > 0 || s.z > 0;
  const n = segs.length; if (!n) return out;
  if (closed && n > 2 && segs.every(wide) && segs.every((s, i) => joined(s, segs[(i + 1) % n]))) {
    const L: number[] = [], R: number[] = [];
    for (let i = 0; i < n; i++) { const p = segs[(i + n - 1) % n], q = segs[i]; const o = mitre(p, q, q.a); L.push(q.x0 + o[0], q.y0 + o[1]); R.push(q.x0 - o[0], q.y0 - o[1]); }
    const ring = [...L, L[0], L[1], R[0], R[1]]; for (let i = n - 1; i >= 1; i--) ring.push(R[i * 2], R[i * 2 + 1]); ring.push(R[0], R[1]);
    out.push(ring); return out;
  }
  let i = 0;
  while (i < n) {
    if (!wide(segs[i])) { i++; continue; }
    let j = i; while (j + 1 < n && wide(segs[j + 1]) && joined(segs[j], segs[j + 1])) j++;
    const L: number[] = [], R: number[] = []; const s0 = segs[i], s1 = segs[j];
    L.push(s0.x0 + s0.nx * s0.a, s0.y0 + s0.ny * s0.a); R.push(s0.x0 - s0.nx * s0.a, s0.y0 - s0.ny * s0.a);
    for (let k = i; k < j; k++) { const o = mitre(segs[k], segs[k + 1], segs[k].z); L.push(segs[k].x1 + o[0], segs[k].y1 + o[1]); R.push(segs[k].x1 - o[0], segs[k].y1 - o[1]); }
    L.push(s1.x1 + s1.nx * s1.z, s1.y1 + s1.ny * s1.z); R.push(s1.x1 - s1.nx * s1.z, s1.y1 - s1.ny * s1.z);
    const poly = [...L]; for (let k = R.length / 2 - 1; k >= 0; k--) poly.push(R[k * 2], R[k * 2 + 1]);
    out.push(poly); i = j + 1;
  }
  return out;
}
export function bulgePts(x0: number, y0: number, x1: number, y1: number, b: number, out: number[]) {
  const theta = 4 * Math.atan(b); const dx = x1 - x0, dy = y1 - y0, chord = Math.hypot(dx, dy); if (chord < 1e-12) { out.push(x1, y1); return; }
  const r = chord / (2 * Math.sin(Math.abs(theta) / 2)); const mx = (x0 + x1) / 2, my = (y0 + y1) / 2; const hh = Math.sqrt(Math.max(0, r * r - chord * chord / 4)); const s = b > 0 ? 1 : -1;
  const cx = mx - s * hh * dy / chord, cy = my + s * hh * dx / chord; const a0 = Math.atan2(y0 - cy, x0 - cx);
  const n = Math.max(2, Math.min(48, Math.ceil(Math.abs(theta) / (Math.PI / 16))));
  for (let i = 1; i <= n; i++) { const a = a0 + theta * i / n; if (i === n) out.push(x1, y1); else out.push(cx + r * Math.cos(a), cy + r * Math.sin(a)); }
}
export function distSeg(px: number, py: number, x0: number, y0: number, x1: number, y1: number): number {
  const dx = x1 - x0, dy = y1 - y0; const l2 = dx * dx + dy * dy; let t = l2 > 0 ? ((px - x0) * dx + (py - y0) * dy) / l2 : 0; t = Math.max(0, Math.min(1, t));
  return Math.hypot(px - (x0 + dx * t), py - (y0 + dy * t));
}
export function segInt(x1: number, y1: number, x2: number, y2: number, x3: number, y3: number, x4: number, y4: number): [number, number] | null {
  const d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4); if (Math.abs(d) < 1e-12) return null;
  const t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d, u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / d;
  if (t < -1e-9 || t > 1 + 1e-9 || u < -1e-9 || u > 1 + 1e-9) return null;
  return [x1 + t * (x2 - x1), y1 + t * (y2 - y1)];
}
function segRect(x0: number, y0: number, x1: number, y1: number, rx0: number, ry0: number, rx1: number, ry1: number): boolean {
  if ((x0 >= rx0 && x0 <= rx1 && y0 >= ry0 && y0 <= ry1) || (x1 >= rx0 && x1 <= rx1 && y1 >= ry0 && y1 <= ry1)) return true;
  return !!(segInt(x0, y0, x1, y1, rx0, ry0, rx1, ry0) || segInt(x0, y0, x1, y1, rx1, ry0, rx1, ry1) || segInt(x0, y0, x1, y1, rx1, ry1, rx0, ry1) || segInt(x0, y0, x1, y1, rx0, ry1, rx0, ry0));
}
export function pointInPoly(x: number, y: number, poly: Float32Array): boolean {
  let inside = false; const n = poly.length / 2;
  for (let i = 0, j = n - 1; i < n; j = i++) { const xi = poly[i * 2], yi = poly[i * 2 + 1], xj = poly[j * 2], yj = poly[j * 2 + 1]; if ((yi > y) !== (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside; }
  return inside;
}
/** Approximate text box (world units, unrotated extent around the anchor, rotated corners folded into an axis-aligned box). */
export function textBox(t: TextItem): [number, number, number, number] {
  const lines = t.mtext ? (t.lines ?? []) : [[{ t: t.text ?? '' }]];
  const nl = Math.max(1, lines.length); let maxLen = 1; for (const l of lines) maxLen = Math.max(maxLen, l.reduce((a, r) => a + r.t.length * (r.hf ?? 1), 0));
  const w = t.mtext && t.w > 0 ? Math.max(t.w, 1e-9) : t.h * 0.75 * maxLen * t.wf; const lh = t.h * (t.mtext ? 1.667 * t.ls : 1); const hgt = nl * lh;
  let ox = 0, oy = 0;   // offset of the box's lower-left from the anchor
  if (t.mtext) { const col = (t.at - 1) % 3, row = Math.floor((t.at - 1) / 3); ox = col === 0 ? 0 : col === 1 ? -w / 2 : -w; oy = row === 0 ? -hgt : row === 1 ? -hgt / 2 : 0; }
  else { ox = t.ha === 1 || t.ha === 4 ? -w / 2 : t.ha === 2 ? -w : 0; oy = t.va === 2 || t.ha === 4 ? -t.h / 2 : t.va === 3 ? -t.h : 0; }
  const r = t.rot * Math.PI / 180, cr = Math.cos(r), sr = Math.sin(r); const ax = t.mtext ? t.x : t.ax, ay = t.mtext ? t.y : t.ay;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (const [lx, ly] of [[ox, oy], [ox + w, oy], [ox, oy + hgt], [ox + w, oy + hgt]]) { const x = ax + lx * cr - ly * sr, y = ay + lx * sr + ly * cr; if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
  return [x0, y0, x1, y1];
}
