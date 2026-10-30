using NexysDraft.Core.Model;

namespace NexysDraft.Core.Geometry;

public static class Tess
{
    /// <summary>Points of a bulge segment (p0→p1, bulge = tan(θ/4)); excludes p0, includes p1.</summary>
    public static void Bulge(double x0, double y0, double x1, double y1, double bulge, List<double> outXY, int maxSeg = 32)
    {
        if (Math.Abs(bulge) < 1e-12) { outXY.Add(x1); outXY.Add(y1); return; }
        double theta = 4 * Math.Atan(bulge); double dx = x1 - x0, dy = y1 - y0, chord = Math.Sqrt(dx * dx + dy * dy);
        if (chord < 1e-12) { outXY.Add(x1); outXY.Add(y1); return; }
        double r = chord / (2 * Math.Sin(Math.Abs(theta) / 2));
        double mx = (x0 + x1) / 2, my = (y0 + y1) / 2; double h = Math.Sqrt(Math.Max(0, r * r - chord * chord / 4)); double s = bulge > 0 ? 1 : -1;
        double cx = mx - s * h * dy / chord, cy = my + s * h * dx / chord;
        double a0 = Math.Atan2(y0 - cy, x0 - cx);
        int n = Math.Max(2, Math.Min(maxSeg, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 12))));
        for (int i = 1; i <= n; i++) { double a = a0 + theta * i / n; if (i == n) { outXY.Add(x1); outXY.Add(y1); } else { outXY.Add(cx + r * Math.Cos(a)); outXY.Add(cy + r * Math.Sin(a)); } }
    }

    public static List<double> Arc(double cx, double cy, double r, double a0Deg, double a1Deg, bool ccw = true, int segPer30 = 1)
    {
        var pts = new List<double>(); double a0 = a0Deg * Math.PI / 180, a1 = a1Deg * Math.PI / 180;
        double sweep = a1 - a0; if (ccw) { while (sweep <= 0) sweep += Math.PI * 2; } else { while (sweep >= 0) sweep -= Math.PI * 2; }
        int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 12)) * segPer30);
        for (int i = 0; i <= n; i++) { double a = a0 + sweep * i / n; pts.Add(cx + r * Math.Cos(a)); pts.Add(cy + r * Math.Sin(a)); }
        return pts;
    }

    public static List<double> EllipseArc(V3 c, V3 major, double ratio, double s, double e, bool ccw = true)
    {
        var pts = new List<double>(); double mx = major.X, my = major.Y; double nx = -my * ratio, ny = mx * ratio;
        double sweep = e - s; if (ccw) { while (sweep <= 1e-12) sweep += Math.PI * 2; } else { while (sweep >= -1e-12) sweep -= Math.PI * 2; }
        int n = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 16)));
        for (int i = 0; i <= n; i++) { double t = s + sweep * i / n; pts.Add(c.X + mx * Math.Cos(t) + nx * Math.Sin(t)); pts.Add(c.Y + my * Math.Cos(t) + ny * Math.Sin(t)); }
        return pts;
    }

    /// <summary>Spline → polyline (xy pairs): NURBS evaluation from control points/knots, else Catmull-Rom through fit points.</summary>
    public static List<double> Spline(Spline s, int perSpan = 8)
    {
        var pts = new List<double>();
        if (s.Control.Count >= 2 && s.Knots.Count >= s.Control.Count + s.Degree + 1)
        {
            int p = s.Degree, n = s.Control.Count; var U = s.Knots; bool rational = s.Weights.Count == n;
            double u0 = U[p], u1 = U[n]; int spans = Math.Max(1, n - p); int samples = spans * perSpan;
            for (int i = 0; i <= samples; i++)
            {
                double u = u0 + (u1 - u0) * i / samples; if (i == samples) u = u1 - 1e-12 * Math.Abs(u1 - u0);
                int k = p; while (k < n - 1 && u >= U[k + 1]) k++; if (k > n - 1) k = n - 1;
                var d = new V3[p + 1]; var w = new double[p + 1];
                for (int j = 0; j <= p; j++) { int idx = k - p + j; double wj = rational ? s.Weights[idx] : 1; d[j] = s.Control[idx] * wj; w[j] = wj; }
                for (int r = 1; r <= p; r++) for (int j = p; j >= r; j--) { int idx = k - p + j; double den = U[idx + p - r + 1] - U[idx]; double a = den == 0 ? 0 : (u - U[idx]) / den; d[j] = d[j - 1] * (1 - a) + d[j] * a; w[j] = w[j - 1] * (1 - a) + w[j] * a; }
                pts.Add(d[p].X / (w[p] == 0 ? 1 : w[p])); pts.Add(d[p].Y / (w[p] == 0 ? 1 : w[p]));
            }
            return pts;
        }
        var f = s.Fit.Count >= 2 ? s.Fit : s.Control;
        if (f.Count < 2) return pts;
        for (int i = 0; i < f.Count - 1; i++)
        {
            V3 p0 = f[Math.Max(0, i - 1)], p1 = f[i], p2 = f[i + 1], p3 = f[Math.Min(f.Count - 1, i + 2)];
            for (int j = 0; j < perSpan; j++)
            {
                double t = (double)j / perSpan, t2 = t * t, t3 = t2 * t;
                pts.Add(0.5 * (2 * p1.X + (-p0.X + p2.X) * t + (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 + (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3));
                pts.Add(0.5 * (2 * p1.Y + (-p0.Y + p2.Y) * t + (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 + (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3));
            }
        }
        pts.Add(f[^1].X); pts.Add(f[^1].Y);
        return pts;
    }

    /// <summary>Hatch loop → closed polygon (xy pairs).</summary>
    public static List<double> HatchLoop(HatchLoop loop)
    {
        var pts = new List<double>();
        foreach (var e in loop.Edges)
        {
            switch (e.Type)
            {
                case 0:
                    for (int i = 0; i < e.Poly.Count; i++)
                    {
                        var v = e.Poly[i]; if (i == 0) { pts.Add(v.X); pts.Add(v.Y); }
                        if (i < e.Poly.Count - 1) Bulge(v.X, v.Y, e.Poly[i + 1].X, e.Poly[i + 1].Y, v.Bulge, pts);
                        else if (e.PolyClosed || true) Bulge(v.X, v.Y, e.Poly[0].X, e.Poly[0].Y, v.Bulge, pts);
                    }
                    break;
                case 1: if (pts.Count == 0) { pts.Add(e.P0.X); pts.Add(e.P0.Y); } pts.Add(e.P1.X); pts.Add(e.P1.Y); break;
                case 2: { var a = Arc(e.Center.X, e.Center.Y, e.Radius, e.StartDeg, e.EndDeg, e.Ccw); if (pts.Count > 0) a.RemoveRange(0, 2); pts.AddRange(a); break; }
                case 3: { var a = EllipseArc(e.Center, e.Major, e.Ratio, e.StartDeg * Math.PI / 180, e.EndDeg * Math.PI / 180, e.Ccw); if (pts.Count > 0) a.RemoveRange(0, 2); pts.AddRange(a); break; }
                case 4:
                    {
                        // spline edge: control points from the raw groups (10/20) — evaluated as a Catmull-Rom through fit points when given (11/21), else through the control polygon
                        var fit = new List<V3>(); var ctl = new List<V3>(); V3 cur = default; int mode = 0;
                        foreach (var g in e.Raw)
                        {
                            if (g.Code == 10) { cur = new V3(Dxf.DxfCodec.ParseDouble(g.Value), 0); mode = 1; }
                            else if (g.Code == 20 && mode == 1) { cur = cur with { Y = Dxf.DxfCodec.ParseDouble(g.Value) }; ctl.Add(cur); }
                            else if (g.Code == 11) { cur = new V3(Dxf.DxfCodec.ParseDouble(g.Value), 0); mode = 2; }
                            else if (g.Code == 21 && mode == 2) { cur = cur with { Y = Dxf.DxfCodec.ParseDouble(g.Value) }; fit.Add(cur); }
                        }
                        var sp = new Spline { Fit = fit.Count >= 2 ? fit : new(), Control = ctl };
                        var a = Spline(sp); if (pts.Count > 0 && a.Count >= 2) a.RemoveRange(0, 2); pts.AddRange(a); break;
                    }
            }
        }
        return pts;
    }
}

public static class Bounds
{
    public static (double x0, double y0, double x1, double y1)? Of(Document doc)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue; bool any = false;
        void Add(double x, double y) { if (double.IsNaN(x) || double.IsNaN(y) || Math.Abs(x) > 1e15 || Math.Abs(y) > 1e15) return; any = true; if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
        foreach (var e in doc.Entities.Where(e => !e.PaperSpace))
        {
            var b = Of(e, doc, 0); if (b == null) continue; Add(b.Value.x0, b.Value.y0); Add(b.Value.x1, b.Value.y1);
        }
        return any ? (x0, y0, x1, y1) : null;
    }

    public static (double x0, double y0, double x1, double y1)? Of(Entity e, Document doc, int depth)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue; bool any = false;
        void Add(double x, double y) { any = true; if (x < x0) x0 = x; if (y < y0) y0 = y; if (x > x1) x1 = x; if (y > y1) y1 = y; }
        switch (e)
        {
            case Line l: Add(l.P0.X, l.P0.Y); Add(l.P1.X, l.P1.Y); break;
            case Circle c: Add(c.Center.X - c.Radius, c.Center.Y - c.Radius); Add(c.Center.X + c.Radius, c.Center.Y + c.Radius); break;
            case Arc a: { var p = Tess.Arc(a.Center.X, a.Center.Y, a.Radius, a.StartDeg, a.EndDeg); for (int i = 0; i < p.Count; i += 2) Add(p[i], p[i + 1]); break; }
            case Ellipse el: { var p = Tess.EllipseArc(el.Center, el.Major, el.Ratio, el.StartParam, el.EndParam); for (int i = 0; i < p.Count; i += 2) Add(p[i], p[i + 1]); break; }
            case LwPolyline p: foreach (var v in p.Vertices) Add(v.X, v.Y); break;
            case Polyline p: foreach (var v in p.Vertices) Add(v.X, v.Y); break;
            case Text t: { double w = t.Height * t.WidthFactor * 1.2 * Math.Max(1, t.Value.Length); var a = t.Anchor; Add(a.X, a.Y); Add(a.X + w, a.Y + t.Height); Add(a.X - (t.HAlign >= 1 ? w : 0), a.Y - (t.VAlign >= 1 ? t.Height : 0)); break; }
            case MText m:
                {
                    // width: the rect width when plausible, else the longest line (dxf2dwg round trips leave rect_width = 1.0)
                    var lines = MTextFmt.MTextParser.Parse(m.Value); int longest = lines.Count == 0 ? 1 : lines.Max(l => l.Plain.Length);
                    double h = m.Height > 0 ? m.Height : 2.5; double w = m.Width >= h * 2 ? m.Width : Math.Max(1, longest) * h * 1.6; double hgt = Math.Max(1, lines.Count) * h * 1.667;   // 1.6·h per character: TrueType glyphs are set from the cap height, so a line is ~1.5–2× the "text height" per char
                    double r = m.RotationDeg * Math.PI / 180, cr = Math.Cos(r), sr = Math.Sin(r);
                    int col = (m.Attach - 1) % 3, row = (m.Attach - 1) / 3; double ox = col == 0 ? 0 : col == 1 ? -w / 2 : -w, oy = row == 0 ? -hgt : row == 1 ? -hgt / 2 : 0;
                    foreach (var (lx, ly) in new[] { (ox, oy), (ox + w, oy), (ox, oy + hgt), (ox + w, oy + hgt) }) Add(m.Pos.X + lx * cr - ly * sr, m.Pos.Y + lx * sr + ly * cr);
                    break;
                }
            case Insert ins:
                {
                    if (depth < 4 && doc.Blocks.TryGetValue(ins.Block, out var b))
                    {
                        double cr = Math.Cos(ins.Rotation * Math.PI / 180), sr = Math.Sin(ins.Rotation * Math.PI / 180);
                        foreach (var ce in b.Entities)
                        {
                            var cb = Of(ce, doc, depth + 1); if (cb == null) continue;
                            foreach (var (px, py) in new[] { (cb.Value.x0, cb.Value.y0), (cb.Value.x1, cb.Value.y0), (cb.Value.x0, cb.Value.y1), (cb.Value.x1, cb.Value.y1) })
                            {
                                double lx = (px - b.Base.X) * ins.SX, ly = (py - b.Base.Y) * ins.SY;
                                Add(ins.Pos.X + lx * cr - ly * sr, ins.Pos.Y + lx * sr + ly * cr);
                            }
                        }
                    }
                    // an INSERT of an empty or unresolved block draws nothing → no extent (AutoCAD rule; u04's AVE_RENDER/AVE_GLOBAL inserts sit 30,000 units away)
                    break;
                }
            case Hatch h: foreach (var l in h.Loops) { var p = Tess.HatchLoop(l); for (int i = 0; i < p.Count; i += 2) Add(p[i], p[i + 1]); } break;
            case Dimension d: Add(d.DefPoint.X, d.DefPoint.Y); Add(d.P13.X, d.P13.Y); Add(d.P14.X, d.P14.Y); Add(d.TextMid.X, d.TextMid.Y); if (d.Block != "" && depth < 4 && doc.Blocks.TryGetValue(d.Block, out var db)) foreach (var ce in db.Entities) { var cb = Of(ce, doc, depth + 1); if (cb != null) { Add(cb.Value.x0, cb.Value.y0); Add(cb.Value.x1, cb.Value.y1); } } break;
            case Solid s: Add(s.P0.X, s.P0.Y); Add(s.P1.X, s.P1.Y); Add(s.P2.X, s.P2.Y); Add(s.P3.X, s.P3.Y); break;
            case Point pt: Add(pt.Pos.X, pt.Pos.Y); break;
            case Spline sp: foreach (var c in sp.Control) Add(c.X, c.Y); foreach (var f in sp.Fit) Add(f.X, f.Y); break;
        }
        return any ? (x0, y0, x1, y1) : null;
    }
}
