using NexysDraft.Core.Geometry;
using NexysDraft.Core.Model;

namespace NexysDraft.Core.Edit;

/// <summary>
/// One piece of a curve: a straight segment or a circular arc. The parameter t runs 0 → 1 from the start to the end of the piece in the
/// drawing direction of its entity; values outside 0..1 are on the carrier (the infinite line / the full circle), which is what EXTEND uses.
/// An arc is stored counter-clockwise from <see cref="A0"/> by <see cref="Sweep"/> (radians); <see cref="Rev"/> = the entity runs it clockwise.
/// </summary>
public readonly record struct Prim(bool IsArc, double X0, double Y0, double X1, double Y1, double Cx, double Cy, double R, double A0, double Sweep, bool Rev)
{
    public const double Eps = 1e-9;
    const double Tau = Math.PI * 2;
    public static Prim Seg(double x0, double y0, double x1, double y1) => new(false, x0, y0, x1, y1, 0, 0, 0, 0, 0, false);
    public static Prim ArcOf(double cx, double cy, double r, double a0, double sweep, bool rev = false)
    {
        double s = rev ? a0 + sweep : a0, e = rev ? a0 : a0 + sweep;
        return new(true, cx + r * Math.Cos(s), cy + r * Math.Sin(s), cx + r * Math.Cos(e), cy + r * Math.Sin(e), cx, cy, r, a0, sweep, rev);
    }
    /// <summary>Polyline segment p0 → p1 with a bulge (tan of a quarter of the included angle; negative = clockwise).</summary>
    public static Prim Bulged(double x0, double y0, double x1, double y1, double bulge)
    {
        double dx = x1 - x0, dy = y1 - y0, chord = Math.Sqrt(dx * dx + dy * dy);
        if (Math.Abs(bulge) < 1e-12 || chord < 1e-12) return Seg(x0, y0, x1, y1);
        double theta = 4 * Math.Atan(Math.Abs(bulge)), r = chord / (2 * Math.Sin(theta / 2));
        double h = Math.Sqrt(Math.Max(0, r * r - chord * chord / 4)); if (Math.Abs(bulge) > 1) h = -h;   // more than a half circle: the centre is on the other side of the chord
        double s = bulge > 0 ? 1 : -1, cx = (x0 + x1) / 2 - s * h * dy / chord, cy = (y0 + y1) / 2 + s * h * dx / chord;
        var p = bulge > 0 ? new Prim(true, x0, y0, x1, y1, cx, cy, r, Math.Atan2(y0 - cy, x0 - cx), theta, false) : new Prim(true, x0, y0, x1, y1, cx, cy, r, Math.Atan2(y1 - cy, x1 - cx), theta, true);
        return p;
    }
    public double Length => IsArc ? R * Sweep : Math.Sqrt((X1 - X0) * (X1 - X0) + (Y1 - Y0) * (Y1 - Y0));
    public bool Degenerate => IsArc ? R < 1e-12 || Sweep < 1e-12 : Length < 1e-12;
    /// <summary>Parameter period of the carrier circle (a full turn), in t.</summary>
    public double Period => Tau / Sweep;
    public double AngleAt(double t) => A0 + Sweep * (Rev ? 1 - t : t);
    public (double x, double y) At(double t)
    {
        if (!IsArc) return (X0 + (X1 - X0) * t, Y0 + (Y1 - Y0) * t);
        if (t == 0) return (X0, Y0); if (t == 1) return (X1, Y1);
        double a = AngleAt(t); return (Cx + R * Math.Cos(a), Cy + R * Math.Sin(a));
    }
    /// <summary>Carrier parameter of a point on the carrier: any real for a line; for an arc the value nearest to the 0..1 range.</summary>
    public double ParamOf(double x, double y)
    {
        if (!IsArc) { double dx = X1 - X0, dy = Y1 - Y0, l2 = dx * dx + dy * dy; return l2 < 1e-24 ? 0 : ((x - X0) * dx + (y - Y0) * dy) / l2; }
        double d = Math.Atan2(y - Cy, x - Cx) - A0; d %= Tau; if (d < 0) d += Tau;
        double t = d / Sweep, per = Period;   // 0 ≤ t < per, counter-clockwise from A0
        if (t > 1 && per - t < t - 1) t -= per;   // nearer to the start than to the end
        return Rev ? 1 - t : t;
    }
    public bool Contains(double x, double y, out double t)
    {
        t = ParamOf(x, y); double tol = Eps + 1e-9 / Math.Max(Length, 1e-12);
        if (t < -tol || t > 1 + tol) return false;
        t = Math.Clamp(t, 0, 1); return true;
    }
    /// <summary>Nearest point of the piece to (x, y): its parameter (0..1) and the distance.</summary>
    public (double t, double d) Nearest(double x, double y)
    {
        double t;
        if (!IsArc) t = Math.Clamp(ParamOf(x, y), 0, 1);
        else { t = ParamOf(x, y); if (t < 0 || t > 1) { double d0 = Hyp(x - X0, y - Y0), d1 = Hyp(x - X1, y - Y1); t = d0 <= d1 ? 0 : 1; } }
        var p = At(t); return (t, Hyp(x - p.x, y - p.y));
    }
    static double Hyp(double a, double b) => Math.Sqrt(a * a + b * b);
    public (double x0, double y0, double x1, double y1) Box()
    {
        if (!IsArc) return (Math.Min(X0, X1), Math.Min(Y0, Y1), Math.Max(X0, X1), Math.Max(Y0, Y1));
        return (Cx - R, Cy - R, Cx + R, Cy + R);
    }

    /// <summary>Intersections of the carriers of two pieces (infinite lines / full circles).</summary>
    public static void Carriers(in Prim a, in Prim b, List<(double x, double y)> pts)
    {
        if (a.Degenerate || b.Degenerate) return;
        if (!a.IsArc && !b.IsArc)
        {
            double ax = a.X1 - a.X0, ay = a.Y1 - a.Y0, bx = b.X1 - b.X0, by = b.Y1 - b.Y0, den = ax * by - ay * bx;
            if (Math.Abs(den) < 1e-12 * a.Length * b.Length) return;   // parallel or on one line
            double t = ((b.X0 - a.X0) * by - (b.Y0 - a.Y0) * bx) / den; pts.Add((a.X0 + ax * t, a.Y0 + ay * t)); return;
        }
        if (a.IsArc && b.IsArc)
        {
            double dx = b.Cx - a.Cx, dy = b.Cy - a.Cy, d = Math.Sqrt(dx * dx + dy * dy); double tol = 1e-9 * Math.Max(a.R, b.R);
            if (d < 1e-12 * Math.Max(a.R, b.R) || d > a.R + b.R + tol || d < Math.Abs(a.R - b.R) - tol) return;
            double k = (a.R * a.R - b.R * b.R + d * d) / (2 * d), h2 = a.R * a.R - k * k, hh = h2 > 0 ? Math.Sqrt(h2) : 0;
            double mx = a.Cx + dx * k / d, my = a.Cy + dy * k / d;
            if (hh < 1e-7 * Math.Max(a.R, b.R)) { pts.Add((mx, my)); return; }
            pts.Add((mx - dy * hh / d, my + dx * hh / d)); pts.Add((mx + dy * hh / d, my - dx * hh / d)); return;
        }
        var l = a.IsArc ? b : a; var c = a.IsArc ? a : b;
        {
            double dx = l.X1 - l.X0, dy = l.Y1 - l.Y0, len = Math.Sqrt(dx * dx + dy * dy), ux = dx / len, uy = dy / len;
            double s = (c.Cx - l.X0) * ux + (c.Cy - l.Y0) * uy, px = l.X0 + ux * s, py = l.Y0 + uy * s;   // foot of the centre on the line
            double dist = Math.Sqrt((c.Cx - px) * (c.Cx - px) + (c.Cy - py) * (c.Cy - py)); if (dist > c.R * (1 + 1e-9)) return;
            double h2 = c.R * c.R - dist * dist, hh = h2 > 0 ? Math.Sqrt(h2) : 0;
            if (hh < 1e-7 * c.R) { pts.Add((px, py)); return; }
            pts.Add((px - ux * hh, py - uy * hh)); pts.Add((px + ux * hh, py + uy * hh));
        }
    }
}

/// <summary>What TRIM / EXTEND would do to one entity at one pick point (computed without touching the document).</summary>
public sealed class TrimResult
{
    public bool Extend;
    /// <summary>The entity is replaced by these (the first keeps its handle). Empty = nothing to do.</summary>
    public List<Entity> Pieces = new();
    /// <summary>The removed (TRIM) or added (EXTEND) part as a world polyline x,y,x,y… for the hover preview.</summary>
    public List<double> Preview = new();
    public string Reason = "";
    public bool Ok => Pieces.Count > 0;
}

public sealed partial class Editor
{
    /// <summary>Entity types TRIM / EXTEND change.</summary>
    public static bool Trimmable(Entity e) => e is Line or Arc or Circle or LwPolyline || (e is Polyline p && !p.Is3D && (p.Flags & (16 | 32 | 64)) == 0);

    /// <summary>The pieces of an entity as a cutting edge / as a target; null = the type takes no part.</summary>
    public static List<Prim>? PrimsOf(Entity e, out bool closed)
    {
        closed = false; var list = new List<Prim>();
        switch (e)
        {
            case Line l: list.Add(Prim.Seg(l.P0.X, l.P0.Y, l.P1.X, l.P1.Y)); return list;
            case Circle c: closed = true; list.Add(Prim.ArcOf(c.Center.X, c.Center.Y, c.Radius, 0, Math.PI * 2)); return list;
            case Arc a: { double s = a.StartDeg * Math.PI / 180, sw = Model.Arc.Norm(a.EndDeg - a.StartDeg) * Math.PI / 180; if (sw < 1e-12) sw = Math.PI * 2; list.Add(Prim.ArcOf(a.Center.X, a.Center.Y, a.Radius, s, sw)); return list; }
            case LwPolyline p: closed = p.Closed && p.Vertices.Count > 2; AddPoly(list, p.Vertices, closed); return list;
            case Polyline p when !p.Is3D && (p.Flags & (16 | 32 | 64)) == 0: closed = p.Closed && p.Vertices.Count > 2; AddPoly(list, p.Vertices, closed); return list;
            case Ellipse el: { var pts = EllipsePts(el, 2048); for (int i = 0; i + 3 < pts.Count; i += 2) list.Add(Prim.Seg(pts[i], pts[i + 1], pts[i + 2], pts[i + 3])); return list; }   // cutting edge only, as a 2048-gon (error ≈ 1.2e-6 × radius)
            case Spline sp: { var pts = Tess.Spline(sp, 64); for (int i = 0; i + 3 < pts.Count; i += 2) list.Add(Prim.Seg(pts[i], pts[i + 1], pts[i + 2], pts[i + 3])); return list; }   // cutting edge only, tessellated
        }
        return null;
    }
    static void AddPoly(List<Prim> list, List<PVertex> v, bool closed)
    {
        int n = v.Count, segs = closed ? n : n - 1;
        for (int i = 0; i < segs; i++) { var a = v[i]; var b = v[(i + 1) % n]; list.Add(Prim.Bulged(a.X, a.Y, b.X, b.Y, a.Bulge)); }
    }
    static List<double> EllipsePts(Ellipse el, int full)
    {
        double sw = el.EndParam - el.StartParam; while (sw <= 1e-12) sw += Math.PI * 2; int n = Math.Max(8, (int)Math.Ceiling(full * sw / (Math.PI * 2)));
        double mx = el.Major.X, my = el.Major.Y, nx = -my * el.Ratio, ny = mx * el.Ratio; var pts = new List<double>(n * 2 + 2);
        for (int i = 0; i <= n; i++) { double t = el.StartParam + sw * i / n; pts.Add(el.Center.X + mx * Math.Cos(t) + nx * Math.Sin(t)); pts.Add(el.Center.Y + my * Math.Cos(t) + ny * Math.Sin(t)); }
        return pts;
    }
    bool EdgeVisible(Entity e) { if (e.Invisible) return false; var l = Doc.LayerOf(e.Layer); return l == null || !(l.Off || l.Frozen); }

    /// <summary>Cutting edges / boundaries for a target: the given handles, or every visible curve of the same space (AutoCAD quick mode).</summary>
    IEnumerable<Entity> EdgesFor(Entity target, ICollection<string>? edges)
    {
        foreach (var e in Doc.Entities)
        {
            if (ReferenceEquals(e, target) || e.PaperSpace != target.PaperSpace) continue;
            if (edges != null) { if (!edges.Contains(e.Handle)) continue; }
            else if (!EdgeVisible(e)) continue;
            if (e is Line or Arc or Circle or LwPolyline or Polyline or Ellipse or Spline) yield return e;
        }
    }

    /// <summary>Nearest global parameter (piece index + t) of a point on a list of pieces.</summary>
    static (double u, double d) NearestU(List<Prim> prims, V3 p)
    {
        double bu = 0, bd = double.MaxValue;
        for (int i = 0; i < prims.Count; i++) { if (prims[i].Degenerate) continue; var (t, d) = prims[i].Nearest(p.X, p.Y); if (d < bd) { bd = d; bu = i + t; } }
        return (bu, bd);
    }
    /// <summary>Points along u0 → u1. u may run outside 0..n on the first / last piece (EXTEND); with <paramref name="wrap"/> it passes the seam of a closed curve.</summary>
    static void Sample(List<Prim> prims, double u0, double u1, List<double> pts, bool wrap = false)
    {
        int n = prims.Count; double span = u1 - u0; int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(span) * 48));
        for (int k = 0; k <= steps; k++)
        {
            double u = u0 + span * k / steps; int i; double t;
            if (wrap) { double m = u % n; if (m < 0) m += n; i = Math.Min(n - 1, (int)Math.Floor(m)); t = m - i; }
            else if (u < 0) { i = 0; t = u; }
            else { i = Math.Min(n - 1, (int)Math.Floor(u)); t = u - i; }
            var p = prims[i].At(t); pts.Add(p.x); pts.Add(p.y);
        }
    }

    /// <summary>Compute TRIM (or EXTEND) of one entity at a pick point against the cutting edges; the document is not changed.</summary>
    public TrimResult ComputeTrim(Entity target, V3 pick, ICollection<string>? edges, bool extend)
    {
        var res = new TrimResult { Extend = extend };
        if (!Trimmable(target)) { res.Reason = $"{target.DxfName} cannot be {(extend ? "extended" : "trimmed")} (line, arc, circle, polyline)"; return res; }
        var prims = PrimsOf(target, out bool closed)!; int n = prims.Count;
        if (n == 0 || prims.All(p => p.Degenerate)) { res.Reason = "zero-length object"; return res; }
        var (up, _) = NearestU(prims, pick);
        var hits = new List<(double x, double y)>();
        if (extend)
        {
            if (closed) { res.Reason = "a closed object cannot be extended"; return res; }
            bool atEnd = up >= n / 2.0; int pi = atEnd ? n - 1 : 0; var tp = prims[pi]; if (tp.Degenerate) { res.Reason = "zero-length end segment"; return res; }
            double best = double.NaN; double lim = tp.IsArc ? tp.Period : double.PositiveInfinity;
            foreach (var e in EdgesFor(target, edges))
            {
                var eps = PrimsOf(e, out _); if (eps == null) continue;
                foreach (var ep in eps)
                {
                    hits.Clear(); Prim.Carriers(tp, ep, hits);
                    foreach (var (x, y) in hits)
                    {
                        if (!ep.Contains(x, y, out _)) continue;
                        double t = tp.ParamOf(x, y);
                        if (tp.IsArc) { double per = tp.Period; if (atEnd) { while (t <= 1 + Prim.Eps) t += per; while (t - per > 1 + Prim.Eps) t -= per; if (t >= per - Prim.Eps) continue; } else { while (t >= -Prim.Eps) t -= per; while (t + per < -Prim.Eps) t += per; if (t <= 1 - per + Prim.Eps) continue; } }
                        double tol = Prim.Eps + 1e-9 / Math.Max(tp.Length, 1e-12);
                        if (atEnd) { if (t > 1 + tol && (double.IsNaN(best) || t < best)) best = t; }
                        else { if (t < -tol && (double.IsNaN(best) || t > best)) best = t; }
                    }
                }
            }
            if (double.IsNaN(best)) { res.Reason = "no boundary edge in that direction"; return res; }
            var np = tp.At(best); var after = target.Clone(); after.Raw = null; after.Dirty = true;
            switch (after)
            {
                case Line l: if (atEnd) l.P1 = new V3(np.x, np.y, l.P1.Z); else l.P0 = new V3(np.x, np.y, l.P0.Z); break;
                case Arc a: { double deg = Model.Arc.Norm(tp.AngleAt(best) * 180 / Math.PI); if (atEnd) a.EndDeg = deg; else a.StartDeg = deg; break; }
                case LwPolyline p: ExtendPoly(p.Vertices, tp, atEnd, best, np); break;
                case Polyline p: ExtendPoly(p.Vertices, tp, atEnd, best, np); p.VertexRaw = null; break;
            }
            res.Pieces.Add(after);
            Sample(new List<Prim> { tp }, atEnd ? 1 : best, atEnd ? best : 0, res.Preview);
            return res;
        }

        // TRIM: parameters where a cutting edge crosses the target
        var cuts = new List<double>(); var tb = Box(prims);
        foreach (var e in EdgesFor(target, edges))
        {
            var bb = Bounds.Of(e, Doc, 0); if (bb != null && (bb.Value.x1 < tb.x0 || bb.Value.x0 > tb.x1 || bb.Value.y1 < tb.y0 || bb.Value.y0 > tb.y1)) continue;
            var eps = PrimsOf(e, out _); if (eps == null) continue;
            foreach (var ep in eps)
            {
                var eb = ep.Box(); if (eb.x1 < tb.x0 || eb.x0 > tb.x1 || eb.y1 < tb.y0 || eb.y0 > tb.y1) continue;
                for (int i = 0; i < n; i++)
                {
                    var tp = prims[i]; if (tp.Degenerate) continue;
                    hits.Clear(); Prim.Carriers(tp, ep, hits);
                    foreach (var (x, y) in hits) if (ep.Contains(x, y, out _) && tp.Contains(x, y, out double t)) cuts.Add(i + t);
                }
            }
        }
        double tolU = 1e-9;
        if (closed) for (int i = 0; i < cuts.Count; i++) if (cuts[i] >= n - tolU) cuts[i] -= n;   // the seam of a closed curve is one point
        cuts.Sort(); var uniq = new List<double>(); foreach (var c in cuts) { if (!closed && (c < tolU || c > n - tolU)) continue; if (uniq.Count == 0 || c - uniq[^1] > tolU) uniq.Add(Math.Max(0, c)); }
        if (uniq.Count == 0 || (closed && uniq.Count < 2)) { res.Reason = closed && uniq.Count == 1 ? "a closed object needs two cutting points" : "the object does not cross a cutting edge"; return res; }
        double? lo = null, hi = null;
        foreach (var c in uniq) { if (c < up) lo = c; else if (c > up && hi == null) hi = c; }
        if (closed)
        {
            double a = hi ?? uniq[0] + n, b = lo ?? uniq[^1] - n;   // keep a → b + n going forward; the removed part is b → a around the pick
            if (a >= n) { a -= n; b -= n; }
            if (b + n - a < tolU) { res.Reason = "nothing would remain"; return res; }
            res.Pieces.Add(Piece(target, prims, n, a, b + n, true));
            Sample(prims, b, a, res.Preview, true);
            return res;
        }
        if (lo != null) res.Pieces.Add(Piece(target, prims, n, 0, lo.Value, false));
        if (hi != null) res.Pieces.Add(Piece(target, prims, n, hi.Value, n, false));
        Sample(prims, lo ?? 0, hi ?? n, res.Preview);
        return res;
    }
    static (double x0, double y0, double x1, double y1) Box(List<Prim> prims)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
        foreach (var p in prims) { var b = p.Box(); x0 = Math.Min(x0, b.x0); y0 = Math.Min(y0, b.y0); x1 = Math.Max(x1, b.x1); y1 = Math.Max(y1, b.y1); }
        double m = 1e-9 * Math.Max(1, Math.Max(x1 - x0, y1 - y0)); return (x0 - m, y0 - m, x1 + m, y1 + m);
    }
    static void ExtendPoly(List<PVertex> v, Prim tp, bool atEnd, double t, (double x, double y) np)
    {
        int k = atEnd ? v.Count - 1 : 0; var q = v[k]; q.X = np.x; q.Y = np.y; v[k] = q;
        if (!tp.IsArc) return;
        int bi = atEnd ? v.Count - 2 : 0; var bv = v[bi]; double sweep = tp.Sweep * (atEnd ? t : 1 - t);   // included angle of the longer arc
        bv.Bulge = Math.Tan(sweep / 4) * (tp.Rev ? -1 : 1); v[bi] = bv;
    }

    /// <summary>The part u0 → u1 of an entity as a new entity of the right type (u1 may pass n on a closed curve).</summary>
    static Entity Piece(Entity src, List<Prim> prims, int n, double u0, double u1, bool closedSrc)
    {
        Entity e;
        switch (src)
        {
            case Line l: { var a = prims[0].At(u0); var b = prims[0].At(u1); var nl = (Line)l.Clone(); nl.P0 = new V3(a.x, a.y, l.P0.Z); nl.P1 = new V3(b.x, b.y, l.P1.Z); e = nl; break; }
            case Arc ar: { var na = (Arc)ar.Clone(); na.StartDeg = Model.Arc.Norm(prims[0].AngleAt(u0) * 180 / Math.PI); na.EndDeg = Model.Arc.Norm(prims[0].AngleAt(u1) * 180 / Math.PI); e = na; break; }
            case Circle c: { var na = new Arc { Center = c.Center, Radius = c.Radius, Thickness = c.Thickness, StartDeg = Model.Arc.Norm(u0 * 360), EndDeg = Model.Arc.Norm(u1 * 360) }; CopyProps(c, na); e = na; break; }
            case LwPolyline p: { var np = (LwPolyline)p.Clone(); np.Vertices = SubPoly(p.Vertices, prims, n, u0, u1); np.Closed = false; e = np; break; }
            case Polyline p: { var np = (Polyline)p.Clone(); np.Vertices = SubPoly(p.Vertices, prims, n, u0, u1); np.Closed = false; np.Flags &= ~1; np.VertexRaw = null; e = np; break; }
            default: throw new InvalidOperationException();
        }
        e.Raw = null; e.Dirty = true; return e;
    }
    static void CopyProps(Entity s, Entity d) { d.Handle = s.Handle; d.Layer = s.Layer; d.Color = s.Color; d.TrueColor = s.TrueColor; d.Linetype = s.Linetype; d.LtScale = s.LtScale; d.Lineweight = s.Lineweight; d.PaperSpace = s.PaperSpace; d.Invisible = s.Invisible; d.Owner = s.Owner; }
    static List<PVertex> SubPoly(List<PVertex> v, List<Prim> prims, int n, double u0, double u1)
    {
        var res = new List<PVertex>(); int cnt = v.Count; const double tol = 1e-9;
        int i0 = (int)Math.Floor(u0 + tol); if (i0 >= n && u1 <= n) i0 = n - 1; double t0 = Math.Max(0, u0 - i0);
        int i1 = (int)Math.Ceiling(u1 - tol) - 1; double t1 = Math.Min(1, u1 - i1);   // last piece touched and where it ends
        for (int i = i0; i <= i1; i++)
        {
            var pr = prims[i % n]; var sv = v[i % cnt]; double a = i == i0 ? t0 : 0, b = i == i1 ? t1 : 1;
            var p = pr.At(a); var nv = sv; nv.X = p.x; nv.Y = p.y;
            nv.Bulge = pr.IsArc ? Math.Tan(pr.Sweep * (b - a) / 4) * (pr.Rev ? -1 : 1) : 0;
            res.Add(nv);
        }
        { var pr = prims[i1 % n]; var p = pr.At(t1); var ev = v[(i1 + 1) % cnt]; ev.X = p.x; ev.Y = p.y; ev.Bulge = 0; res.Add(ev); }
        return res;
    }

    /// <summary>TRIM / EXTEND at the pick points (one undo step). Each pick names an entity and a point on the part to remove (TRIM) or near the
    /// end to lengthen (EXTEND). <paramref name="edges"/> null = every visible curve is a cutting edge.</summary>
    public Change TrimExtend(IEnumerable<(string handle, V3 pick)> picks, ICollection<string>? edges, bool extend, List<string>? messages = null)
    {
        var c = Begin(extend ? "Extend" : "Trim");
        var family = new Dictionary<string, List<string>>();   // picked handle → the pieces it was split into during this command
        var edgeSet = edges == null ? null : new HashSet<string>(edges);
        foreach (var (handle, pick) in picks)
        {
            if (!family.TryGetValue(handle, out var fam)) family[handle] = fam = new List<string> { handle };
            Entity? target = null; double bd = double.MaxValue;
            foreach (var h in fam) { var cand = Doc.Find(h); if (cand == null) continue; var pr = PrimsOf(cand, out _); if (pr == null) continue; double d = NearestU(pr, pick).d; if (d < bd) { bd = d; target = cand; } }
            if (target == null) { messages?.Add("object not found"); continue; }
            var l = Doc.LayerOf(target.Layer); if (l != null && l.Locked) { messages?.Add("the object is on a locked layer"); continue; }
            var r = ComputeTrim(target, pick, edgeSet, extend);
            if (!r.Ok) { messages?.Add(r.Reason); continue; }
            int idx = Doc.Entities.IndexOf(target);
            var first = r.Pieces[0]; first.Handle = target.Handle; Doc.Entities[idx] = first; Replace(c, idx, target, first);
            for (int k = 1; k < r.Pieces.Count; k++)
            {
                var pc = r.Pieces[k]; pc.Handle = Doc.NewHandle(); if (pc is Polyline hp) { hp.SeqendHandle = ""; hp.SeqendRaw = null; }
                Doc.Entities.Add(pc); c.Added.Add((Doc.Entities.Count - 1, pc)); fam.Add(pc.Handle);
                if (edgeSet != null && edgeSet.Contains(target.Handle)) edgeSet.Add(pc.Handle);   // both halves of a cut edge stay cutting edges
            }
        }
        Commit(c); return c;
    }
    /// <summary>Record "before → after" in a change that may already hold an earlier step for the same entity.</summary>
    static void Replace(Change c, int index, Entity before, Entity after)
    {
        for (int i = 0; i < c.Replaced.Count; i++) if (ReferenceEquals(c.Replaced[i].after, before)) { c.Replaced[i] = (c.Replaced[i].index, c.Replaced[i].before, after); return; }
        for (int i = 0; i < c.Added.Count; i++) if (ReferenceEquals(c.Added[i].e, before)) { c.Added[i] = (c.Added[i].index, after); return; }
        c.Replaced.Add((index, before, after));
    }
    /// <summary>The trimmable entity nearest to a point (headless picks name a point, not a handle).</summary>
    public Entity? NearestTrimmable(V3 p)
    {
        Entity? best = null; double bd = double.MaxValue;
        foreach (var e in Doc.Entities) { if (!Trimmable(e) || !EdgeVisible(e)) continue; var pr = PrimsOf(e, out _); if (pr == null || pr.Count == 0) continue; double d = NearestU(pr, p).d; if (d < bd) { bd = d; best = e; } }
        return best;
    }
    public Change Trim(string handle, V3 pick, ICollection<string>? edges = null) => TrimExtend(new[] { (handle, pick) }, edges, false);
    public Change Extend(string handle, V3 pick, ICollection<string>? edges = null) => TrimExtend(new[] { (handle, pick) }, edges, true);
    /// <summary>Hover preview: the part TRIM would remove / EXTEND would add, or null with the reason.</summary>
    public TrimResult? PreviewTrim(string handle, V3 pick, ICollection<string>? edges, bool extend) { var e = Doc.Find(handle); return e == null ? null : ComputeTrim(e, pick, edges, extend); }
}
