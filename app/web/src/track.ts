// AutoTrack: polar tracking (F10), ortho (F8) and object snap tracking (F11) resolved to one point and the alignment paths to draw.
// Pure functions (no DOM) so the host self-test can drive them through the real pointer path and check the numbers.
export type Pt = [number, number];
/** An alignment path: the line through (ax, ay) with unit direction (dx, dy). A polar / ortho path starts at the base point (a ray), a tracking path runs both ways through an acquired point. */
export interface Guide { ax: number; ay: number; dx: number; dy: number; kind: 'polar' | 'ortho' | 'track'; deg: number }
export interface TrackRes { pt: Pt; guides: Guide[]; label: string }
export interface TrackOpts {
  /** Rubber-band start (polar / ortho are measured from it), or null. */
  base: Pt | null; ortho: boolean; polar: boolean; polarInc: number;
  /** Acquired object-snap points (object snap tracking). */
  pts: Pt[]; otrack: boolean;
  /** Capture distance in world units (the aperture, about 10 px). */
  tol: number;
}
const clean = (v: number) => Math.round(v * 1e12) / 1e12;
const fmt = (v: number, d: number) => { const s = v.toFixed(d); return s.includes('.') ? s.replace(/0+$/, '').replace(/\.$/, '') : s; };
function unit(deg: number): Pt { const r = deg * Math.PI / 180; return [clean(Math.cos(r)), clean(Math.sin(r))]; }
function distToLine(q: Pt, g: Guide): number { return Math.abs((q[0] - g.ax) * -g.dy + (q[1] - g.ay) * g.dx); }
function project(q: Pt, g: Guide): Pt { const s = (q[0] - g.ax) * g.dx + (q[1] - g.ay) * g.dy; return [g.ax + g.dx * s, g.ay + g.dy * s]; }
function cross(a: Guide, b: Guide): Pt | null {
  const den = a.dx * b.dy - a.dy * b.dx; if (Math.abs(den) < 1e-9) return null;
  const t = ((b.ax - a.ax) * b.dy - (b.ay - a.ay) * b.dx) / den; return [a.ax + a.dx * t, a.ay + a.dy * t];
}
function normDeg(a: number) { a %= 360; if (a < 0) a += 360; return Math.abs(a - 360) < 1e-9 ? 0 : a; }
function labelOf(g: Guide, p: Pt): string {
  const len = Math.hypot(p[0] - g.ax, p[1] - g.ay); const ang = len > 0 ? normDeg(Math.atan2(p[1] - g.ay, p[0] - g.ax) * 180 / Math.PI) : g.deg;
  return `${g.kind === 'polar' ? 'Polar' : g.kind === 'ortho' ? 'Ortho' : 'Track'}: ${fmt(len, 3)} < ${fmt(ang, 2)}°`;
}

/** The tracking paths through the acquired points that pass within tol of q (horizontal and vertical, AutoCAD "track orthogonally only"). */
function trackGuides(q: Pt, o: TrackOpts): Guide[] {
  const out: Guide[] = []; if (!o.otrack) return out;
  for (const a of o.pts) {
    if (Math.hypot(q[0] - a[0], q[1] - a[1]) <= o.tol) continue;   // on the point itself: no direction yet
    for (const deg of [0, 90]) { const u = unit(deg); const g: Guide = { ax: a[0], ay: a[1], dx: u[0], dy: u[1], kind: 'track', deg }; if (distToLine(q, g) <= o.tol) out.push(g); }
  }
  return out;
}

/** Cursor point q → the tracked point, or null when no alignment path is near (the caller then uses q as it is). */
export function resolveTrack(q: Pt, o: TrackOpts): TrackRes | null {
  const tracks = trackGuides(q, o).sort((a, b) => distToLine(q, a) - distToLine(q, b));
  if (o.base && o.ortho) {
    // ortho is a hard constraint: the point is on the horizontal or vertical through the base; a tracking path may fix where
    const dx = q[0] - o.base[0], dy = q[1] - o.base[1]; const horiz = Math.abs(dx) >= Math.abs(dy);
    const g: Guide = { ax: o.base[0], ay: o.base[1], dx: horiz ? 1 : 0, dy: horiz ? 0 : 1, kind: 'ortho', deg: horiz ? (dx >= 0 ? 0 : 180) : (dy >= 0 ? 90 : 270) };
    const p = project(q, g);
    for (const t of tracks) { const x = cross(g, t); if (x && Math.hypot(x[0] - p[0], x[1] - p[1]) <= o.tol * 1.5) return { pt: x, guides: [g, t], label: labelOf(g, x) + '  ·  ' + labelOf(t, x) }; }
    return { pt: p, guides: [g], label: '' };
  }
  let polar: Guide | null = null;
  if (o.base && o.polar && o.polarInc > 0) {
    const dx = q[0] - o.base[0], dy = q[1] - o.base[1];
    if (dx || dy) {
      const k = Math.round(Math.atan2(dy, dx) * 180 / Math.PI / o.polarInc); const deg = normDeg(k * o.polarInc); const u = unit(deg);
      const g: Guide = { ax: o.base[0], ay: o.base[1], dx: u[0], dy: u[1], kind: 'polar', deg };
      if (distToLine(q, g) <= o.tol && dx * u[0] + dy * u[1] > 0) polar = g;
    }
  }
  const cands: Guide[] = polar ? [polar, ...tracks] : tracks;
  if (!cands.length) return null;
  // two paths that meet near the cursor: their intersection (polar × tracking, or two tracking paths)
  for (let i = 0; i < cands.length; i++) for (let j = i + 1; j < cands.length; j++) {
    const x = cross(cands[i], cands[j]); if (!x || Math.hypot(x[0] - q[0], x[1] - q[1]) > o.tol * 1.5) continue;
    if (cands[i].kind === 'polar' && (x[0] - cands[i].ax) * cands[i].dx + (x[1] - cands[i].ay) * cands[i].dy <= 0) continue;
    return { pt: x, guides: [cands[i], cands[j]], label: labelOf(cands[i], x) + '  ·  ' + labelOf(cands[j], x) };
  }
  const g = cands[0]; const p = project(q, g);
  return { pt: p, guides: [g], label: labelOf(g, p) };
}
