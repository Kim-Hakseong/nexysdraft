using System.Globalization;
using NexysDraft.Core.Geometry;
using NexysDraft.Core.Model;

namespace NexysDraft.Core.Edit;

/// <summary>Standard linetypes (acadiso.lin values, millimetres at LTSCALE 1): positive = dash, negative = gap, 0 = dot.</summary>
public static class StdLinetypes
{
    public static readonly Dictionary<string, (string desc, double[] pattern)> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CENTER"] = ("Center ____ _ ____ _ ____ _", new[] { 31.75, -6.35, 6.35, -6.35 }),
        ["CENTER2"] = ("Center (.5x) ___ _ ___ _ ___ _", new[] { 19.05, -3.175, 3.175, -3.175 }),
        ["HIDDEN"] = ("Hidden __ __ __ __ __ __", new[] { 6.35, -3.175 }),
        ["HIDDEN2"] = ("Hidden (.5x) _ _ _ _ _ _ _ _", new[] { 3.175, -1.5875 }),
        ["DASHED"] = ("Dashed __ __ __", new[] { 12.7, -6.35 }),
        ["DASHED2"] = ("Dashed (.5x) _ _ _ _ _ _", new[] { 6.35, -3.175 }),
        ["PHANTOM"] = ("Phantom ______ __ __ ______", new[] { 31.75, -6.35, 6.35, -6.35, 6.35, -6.35 }),
        ["PHANTOM2"] = ("Phantom (.5x) ___ _ _ ___ _ _", new[] { 15.875, -3.175, 3.175, -3.175, 3.175, -3.175 }),
        ["DASHDOT"] = ("Dash dot __ . __ . __ .", new[] { 12.7, -6.35, 0, -6.35 }),
        ["DOT"] = ("Dot . . . . . . . .", new[] { 0, -6.35 }),
    };
    public static bool IsContinuous(string name) => name == "" || name.Equals("Continuous", StringComparison.OrdinalIgnoreCase) || name.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) || name.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Hatch patterns the command line can create: line families as in acadiso.pat (angle, origin, shift along the line, spacing, dashes).</summary>
public static class StdHatch
{
    public readonly record struct Family(double Angle, double X, double Y, double Shift, double Spacing, double[] Dashes);
    public static readonly Dictionary<string, Family[]> All = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ANSI31"] = new[] { new Family(45, 0, 0, 0, 3.175, Array.Empty<double>()) },
        ["ANSI32"] = new[] { new Family(45, 0, 0, 0, 9.525, Array.Empty<double>()), new Family(45, 4.49013, 0, 0, 9.525, Array.Empty<double>()) },
        ["ANSI37"] = new[] { new Family(45, 0, 0, 0, 3.175, Array.Empty<double>()), new Family(135, 0, 0, 0, 3.175, Array.Empty<double>()) },
        ["LINE"] = new[] { new Family(0, 0, 0, 0, 3.175, Array.Empty<double>()) },
        ["NET"] = new[] { new Family(0, 0, 0, 0, 3.175, Array.Empty<double>()), new Family(90, 0, 0, 0, 3.175, Array.Empty<double>()) },
        ["DASH"] = new[] { new Family(0, 0, 0, 3.175, 3.175, new[] { 3.175, -3.175 }) },
    };

    /// <summary>The pattern definition groups of a HATCH (78, then 53/43/44/45/46/79/49… per family) for a pattern at a scale and angle.</summary>
    public static List<Group> Definition(Family[] fams, double scale, double angleDeg)
    {
        var g = new List<Group>(); string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        g.Add(new Group(78, fams.Length.ToString(CultureInfo.InvariantCulture)));
        double ha = angleDeg * Math.PI / 180, ch = Math.Cos(ha), sh = Math.Sin(ha);
        foreach (var f in fams)
        {
            double a = (f.Angle + angleDeg) * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
            double bx = (f.X * ch - f.Y * sh) * scale, by = (f.X * sh + f.Y * ch) * scale;                 // base point: rotated by the hatch angle
            double ox = (f.Shift * ca - f.Spacing * sa) * scale, oy = (f.Shift * sa + f.Spacing * ca) * scale;   // offset: shift along + spacing across the line
            g.Add(new Group(53, F(f.Angle + angleDeg))); g.Add(new Group(43, F(bx))); g.Add(new Group(44, F(by))); g.Add(new Group(45, F(ox))); g.Add(new Group(46, F(oy)));
            g.Add(new Group(79, f.Dashes.Length.ToString(CultureInfo.InvariantCulture))); foreach (var d in f.Dashes) g.Add(new Group(49, F(d * scale)));
        }
        return g;
    }
}

/// <summary>The lines of a hatch pattern clipped to its boundary loops (even-odd), for the screen and the PDF.</summary>
public static class HatchPattern
{
    sealed class Fam { public double Angle, Bx, By, Ox, Oy; public List<double> Dashes = new(); }
    static List<Fam> Families(Hatch h)
    {
        var list = new List<Fam>(); Fam? cur = null; bool on = false;
        double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
        foreach (var g in h.Tail)
        {
            if (g.Code == 78) { on = true; continue; }
            if (!on) continue;
            switch (g.Code)
            {
                case 53: cur = new Fam { Angle = D(g.Value) }; list.Add(cur); break;
                case 43: if (cur != null) cur.Bx = D(g.Value); break;
                case 44: if (cur != null) cur.By = D(g.Value); break;
                case 45: if (cur != null) cur.Ox = D(g.Value); break;
                case 46: if (cur != null) cur.Oy = D(g.Value); break;
                case 49: cur?.Dashes.Add(D(g.Value)); break;
                case 79: break;
                default: if (g.Code is 98 or 47 or 10 or 20) on = false; break;
            }
        }
        return list;
    }

    /// <summary>Segments x0,y0,x1,y1… of the pattern lines inside the loops; empty for a solid hatch, a hatch without pattern data, or more than
    /// <paramref name="maxSegments"/> pieces (the caller then shows the outline only).</summary>
    public static List<double> Lines(Hatch h, int maxSegments = 4000)
    {
        var res = new List<double>(); if (h.Solid) return res;
        var fams = Families(h); if (fams.Count == 0) return res;
        var loops = h.Loops.Select(l => Tess.HatchLoop(l)).Where(l => l.Count >= 6).ToList(); if (loops.Count == 0) return res;
        var cuts = new List<double>();
        foreach (var f in fams)
        {
            double a = f.Angle * Math.PI / 180, ux = Math.Cos(a), uy = Math.Sin(a), nx = -uy, ny = ux;
            double s = f.Ox * nx + f.Oy * ny; if (Math.Abs(s) < 1e-9) continue;      // spacing across the line
            double shift = f.Ox * ux + f.Oy * uy, b0 = f.Bx * nx + f.By * ny, t00 = f.Bx * ux + f.By * uy;
            double mn = double.MaxValue, mx = double.MinValue;
            foreach (var lp in loops) for (int i = 0; i < lp.Count; i += 2) { double d = lp[i] * nx + lp[i + 1] * ny; if (d < mn) mn = d; if (d > mx) mx = d; }
            double k0 = (mn - b0) / s, k1 = (mx - b0) / s; if (k0 > k1) (k0, k1) = (k1, k0);
            long ka = (long)Math.Ceiling(k0 - 1e-9), kb = (long)Math.Floor(k1 + 1e-9); if (kb - ka > maxSegments) return new List<double>();
            double period = f.Dashes.Sum(Math.Abs); bool dashed = f.Dashes.Count > 0 && period > 1e-9;
            for (long k = ka; k <= kb; k++)
            {
                double dn = b0 + k * s; cuts.Clear();
                foreach (var lp in loops)
                {
                    int n = lp.Count / 2;
                    for (int i = 0; i < n; i++)
                    {
                        double x0 = lp[i * 2], y0 = lp[i * 2 + 1], x1 = lp[((i + 1) % n) * 2], y1 = lp[((i + 1) % n) * 2 + 1];
                        double d0 = x0 * nx + y0 * ny - dn, d1 = x1 * nx + y1 * ny - dn;
                        if ((d0 < 0) == (d1 < 0)) continue;               // half-open rule: an edge counts when its ends are on different sides
                        double t = d0 / (d0 - d1); cuts.Add((x0 + (x1 - x0) * t) * ux + (y0 + (y1 - y0) * t) * uy);
                    }
                }
                if (cuts.Count < 2) continue; cuts.Sort();
                double org = t00 + k * shift;                              // where the dash pattern starts on this line
                for (int i = 0; i + 1 < cuts.Count; i += 2)
                {
                    double ta = cuts[i], tb = cuts[i + 1]; if (tb - ta < 1e-9) continue;
                    if (!dashed) { Add(res, dn, nx, ny, ux, uy, ta, tb); }
                    else
                    {
                        double pos = org + Math.Floor((ta - org) / period) * period;
                        for (int guard = 0; pos < tb && guard < 100000; guard++)
                            foreach (var dlen in f.Dashes)
                            {
                                double len = Math.Abs(dlen), e = pos + len;
                                if (dlen >= 0) { double qa = Math.Max(pos, ta), qb = Math.Min(dlen == 0 ? pos + period * 0.02 : e, tb); if (qb > qa) Add(res, dn, nx, ny, ux, uy, qa, qb); }
                                pos = e; if (pos >= tb) break;
                            }
                    }
                    if (res.Count / 4 > maxSegments) return new List<double>();
                }
            }
        }
        return res;
    }
    static void Add(List<double> res, double dn, double nx, double ny, double ux, double uy, double ta, double tb)
    { res.Add(nx * dn + ux * ta); res.Add(ny * dn + uy * ta); res.Add(nx * dn + ux * tb); res.Add(ny * dn + uy * tb); }
}

public sealed partial class Editor
{
    /// <summary>Make sure a linetype exists in the drawing (standard names only); returns its name as stored. "Continuous" always exists.</summary>
    public string EnsureLinetype(string name)
    {
        if (name.Equals("Continuous", StringComparison.OrdinalIgnoreCase)) return "Continuous";
        var have = Doc.Linetypes.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); if (have != null) return have.Name;
        if (!StdLinetypes.All.TryGetValue(name, out var def)) throw new ArgumentException("unknown linetype " + name + " (Continuous, " + string.Join(", ", StdLinetypes.All.Keys) + ")");
        var lt = new LineType { Name = name.ToUpperInvariant(), Description = def.desc, Pattern = def.pattern.ToList(), Length = def.pattern.Sum(Math.Abs), Handle = Doc.NewHandle() };
        Doc.Linetypes.Add(lt); Doc.Dirty = true; return lt.Name;
    }
    public Change AddLayerWithLinetype(string name, int color, string linetype) => AddLayer(name, color, EnsureLinetype(linetype));
    /// <summary>Linetype of the selected entities (a standard name, Continuous, or BYLAYER).</summary>
    public Change SetLinetype(IEnumerable<string> handles, string linetype)
    {
        string lt = linetype.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) ? "BYLAYER" : EnsureLinetype(linetype);
        var c = Begin("Linetype");
        foreach (var (i, e) in Resolve(handles)) { var after = e.Clone(); after.Raw = null; after.Dirty = true; after.Linetype = lt; Doc.Entities[i] = after; c.Replaced.Add((i, e, after)); }
        Commit(c); return c;
    }

    /// <summary>Dimension text: the override with "&lt;&gt;" replaced by the measured value, or the value alone.</summary>
    static string DimText(Dimension d, double meas, int dec, string prefix = "")
    {
        string v = meas.ToString("F" + dec, CultureInfo.InvariantCulture); if (dec > 0 && v.Contains('.')) v = v.TrimEnd('0').TrimEnd('.');
        if (d.Text is { Length: > 0 }) return d.Text.Contains("<>") ? d.Text.Replace("<>", v) : d.Text;
        return prefix + v;
    }

    /// <summary>Diameter (or radius) dimension of a circle / arc: the dimension line runs through the centre at <paramref name="angleDeg"/>,
    /// arrow heads at the curve, the text ("Ø160" / "R10", or <paramref name="text"/> with &lt;&gt; = the value) outside beyond the far point.</summary>
    public Change AddRadialDimension(double cx, double cy, double r, double angleDeg, bool diameter, string layer, string? text = null)
    {
        if (r <= 0) throw new ArgumentException("radius must be positive");
        var d = new Dimension { Type = (diameter ? 3 : 4) | 32, Style = Doc.HeaderString("$DIMSTYLE", "Standard"), Attach = 5, LineSpacingStyle = 1, Text = text ?? "" };
        double a = angleDeg * Math.PI / 180; var u = new V3(Math.Cos(a), Math.Sin(a)); var c = new V3(cx, cy);
        d.P15 = c + u * r; d.DefPoint = diameter ? c - u * r : c; d.Measurement = diameter ? 2 * r : r; d.HasMeasurement = true;
        var blk = new Block { Name = NextAnonymous("*D"), Flags = 1, Handle = Doc.NewHandle(), EndHandle = Doc.NewHandle(), RecordHandle = Doc.NewHandle() };
        BuildRadialDim(d, blk);
        d.Block = blk.Name; Doc.Blocks[blk.Name] = blk; Doc.BlockOrder.Add(blk.Name);
        d.Handle = Doc.NewHandle(); d.Layer = layer; d.Dirty = true; d.Owner = Doc.ModelSpaceHandle;
        var ch = Begin("Dimension"); ch.BlocksAdded.Add((blk.Name, blk)); Doc.Entities.Add(d); ch.Added.Add((Doc.Entities.Count - 1, d)); Commit(ch); return ch;
    }
    void BuildRadialDim(Dimension d, Block blk)
    {
        double sc = Doc.HeaderDouble("$DIMSCALE", 1); if (sc <= 0) sc = 1;
        double asz = Doc.HeaderDouble("$DIMASZ", 2.5) * sc, txt = Doc.HeaderDouble("$DIMTXT", 2.5) * sc, gap = Doc.HeaderDouble("$DIMGAP", 0.625) * sc;
        int dec = (int)Doc.HeaderDouble("$DIMDEC", 2); string tstyle = Doc.HeaderString("$DIMTXSTY", "Standard"); if (Doc.Styles.All(s => !s.Name.Equals(tstyle, StringComparison.OrdinalIgnoreCase))) tstyle = "Standard";
        bool dia = d.Kind == 3; var far = d.P15; var dir = far - d.DefPoint; double len = dir.Len2; var u = dir * (1 / len); var n = new V3(-u.Y, u.X);
        string text = DimText(d, d.Measurement, dec, dia ? "Ø" : "R");
        double tw = text.Length * txt * 0.8;                                  // rough width of the text
        d.LeaderLen = gap * 2 + asz; var tail = far + u * d.LeaderLen;        // short tail beyond the curve, then the text
        d.TextMid = tail + u * (gap + tw / 2);
        blk.Entities.Clear(); var items = new List<Entity>();
        items.Add(new Line { P0 = d.DefPoint, P1 = tail, Color = 0, Linetype = "BYBLOCK" });
        V3 Tri(V3 tip, V3 back, double k) => tip + back * asz + n * (asz / 6 * k);
        bool room = len > 2 * asz + 1;
        void Arrow(V3 tip, V3 back) => items.Add(new Solid { P0 = tip, P1 = Tri(tip, back, 1), P2 = Tri(tip, back, -1), P3 = Tri(tip, back, -1), Color = 0, Linetype = "BYBLOCK" });
        Arrow(far, room ? u * -1 : u);
        if (dia) Arrow(d.DefPoint, room ? u : u * -1);
        double rot = Math.Atan2(u.Y, u.X) * 180 / Math.PI; if (rot < 0) rot += 360; if (rot > 90 && rot <= 270) rot -= 180;
        items.Add(new Text { Pos = d.TextMid, Align = d.TextMid, Height = txt, Rotation = Arc.Norm(rot), Style = tstyle, HAlign = 1, VAlign = 2, Value = text, Color = 0, Linetype = "BYBLOCK" });
        foreach (var e in items) { e.Handle = Doc.NewHandle(); e.Layer = "0"; e.Dirty = true; e.Owner = blk.RecordHandle; blk.Entities.Add(e); }
    }

    /// <summary>One closed boundary of a new hatch: vertices with bulges (a circle is two vertices with bulge 1).</summary>
    public static List<PVertex> CircleLoop(double cx, double cy, double r) => new() { new PVertex { X = cx - r, Y = cy, Bulge = 1 }, new PVertex { X = cx + r, Y = cy, Bulge = 1 } };

    /// <summary>HATCH over closed loops (the first is the outer boundary, the others are islands: even-odd). Pattern SOLID or a name from
    /// <see cref="StdHatch.All"/>; scale and angle as in AutoCAD.</summary>
    public Change AddHatch(string pattern, double scale, double angleDeg, IReadOnlyList<List<PVertex>> loops, string layer)
    {
        if (loops.Count == 0 || loops.Any(l => l.Count < 2)) throw new ArgumentException("hatch needs at least one closed loop");
        bool solid = pattern.Equals("SOLID", StringComparison.OrdinalIgnoreCase);
        if (!solid && !StdHatch.All.ContainsKey(pattern)) throw new ArgumentException("unknown hatch pattern " + pattern + " (SOLID, " + string.Join(", ", StdHatch.All.Keys) + ")");
        if (scale <= 0) scale = 1;
        var h = new Hatch { Pattern = pattern.ToUpperInvariant(), Solid = solid, Style = 0, PatternType = 1, PatternAngle = angleDeg, PatternScale = scale };
        for (int i = 0; i < loops.Count; i++)
        {
            var ed = new HatchEdge { Type = 0, PolyClosed = true, Poly = new List<PVertex>(loops[i]), PolyHasBulge = loops[i].Any(v => v.Bulge != 0) };
            var lp = new HatchLoop { Type = 2 | (i == 0 ? 1 : 16) }; lp.Edges.Add(ed); h.Loops.Add(lp);
        }
        if (!solid) h.Tail.AddRange(StdHatch.Definition(StdHatch.All[pattern], scale, angleDeg));
        return Add(h, layer, "Hatch");
    }
}
