using System.Globalization;
using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;

namespace NexysDraft.Tests;

/// <summary>Order-independent comparison of two documents: per entity type, sorted coordinate signatures matched pairwise within a tolerance.</summary>
public static class Compare
{
    public static readonly string[] CoordTypes = { "LINE", "LWPOLYLINE", "POLYLINE", "CIRCLE", "ARC", "ELLIPSE", "TEXT", "MTEXT", "INSERT" };
    public static readonly string[] CountTypes = { "HATCH", "DIMENSION" };

    public static double[]? Sig(Entity e) => e switch
    {
        Line l => new[] { l.P0.X, l.P0.Y, l.P1.X, l.P1.Y },
        Circle c => new[] { c.Center.X, c.Center.Y, c.Radius },
        Arc a => new[] { a.Center.X, a.Center.Y, a.Radius, a.StartDeg, a.EndDeg },
        Ellipse el => new[] { el.Center.X, el.Center.Y, el.Major.X, el.Major.Y, el.Ratio, el.StartParam, el.EndParam },
        LwPolyline p => p.Vertices.SelectMany(v => new[] { v.X, v.Y, v.Bulge }).Prepend(p.Closed ? 1 : 0).ToArray(),
        Polyline p => p.Vertices.SelectMany(v => new[] { v.X, v.Y, v.Bulge }).Prepend(p.Closed ? 1 : 0).ToArray(),
        Text t => new[] { t.Pos.X, t.Pos.Y, t.Height, t.Rotation },
        MText m => new[] { m.Pos.X, m.Pos.Y },   // height left out: LibreDWG 0.14 dxf2dwg writes MTEXT text_height 0 (docs/libredwg.md)
        Insert i => new[] { i.Pos.X, i.Pos.Y, i.SX, i.SY, i.Rotation },
        Hatch h => new[] { (double)h.Loops.Count },
        Dimension d => new[] { d.DefPoint.X, d.DefPoint.Y, d.P13.X, d.P13.Y, d.P14.X, d.P14.Y },
        Solid s => new[] { s.P0.X, s.P0.Y, s.P1.X, s.P1.Y, s.P2.X, s.P2.Y, s.P3.X, s.P3.Y },
        Point p => new[] { p.Pos.X, p.Pos.Y },
        Spline s => s.Control.SelectMany(c => new[] { c.X, c.Y }).ToArray(),
        _ => null,
    };
    static string TypeName(Entity e) => e is Text t && (t.IsAttrib || t.IsAttdef) ? e.DxfName : e.DxfName;

    public sealed class Result
    {
        public List<string> Mismatches = new(); public Dictionary<string, (int a, int b, int matched, double maxErr)> PerType = new();
        public bool Ok => Mismatches.Count == 0;
        public string Summary => string.Join("; ", PerType.Select(kv => $"{kv.Key} {kv.Value.a}/{kv.Value.b} matched {kv.Value.matched} maxErr {kv.Value.maxErr:E1}"));
    }

    /// <summary>Compare model-space entities of two documents. Types in <paramref name="coordTypes"/> must match coordinates within tol; <paramref name="countTypes"/> only by count.</summary>
    public static Result Documents(Document a, Document b, double tol, string[] coordTypes, string[] countTypes, bool includeBlocks = true, bool layersToo = true)
    {
        var r = new Result();
        if (layersToo && a.Layers.Count != b.Layers.Count) r.Mismatches.Add($"layers {a.Layers.Count} vs {b.Layers.Count}");
        var ea = Flatten(a, includeBlocks); var eb = Flatten(b, includeBlocks);
        foreach (var t in coordTypes.Concat(countTypes))
        {
            var la = ea.Where(e => TypeName(e) == t).ToList(); var lb = eb.Where(e => TypeName(e) == t).ToList();
            if (la.Count == 0 && lb.Count == 0) continue;
            int matched = 0; double maxErr = 0;
            if (la.Count != lb.Count) r.Mismatches.Add($"{t} count {la.Count} vs {lb.Count}");
            if (countTypes.Contains(t)) { r.PerType[t] = (la.Count, lb.Count, Math.Min(la.Count, lb.Count), 0); continue; }
            var sa = la.Select(Sig).Where(s => s != null).Select(s => s!).OrderBy(s => s, Cmp.Instance).ToList(); var sb = lb.Select(Sig).Where(s => s != null).Select(s => s!).OrderBy(s => s, Cmp.Instance).ToList();
            int n = Math.Min(sa.Count, sb.Count);
            // greedy: sorted order should align; tolerate small reorderings by nearest-neighbour search in a window
            var used = new bool[sb.Count];
            for (int i = 0; i < sa.Count; i++)
            {
                int best = -1; double bestErr = double.MaxValue;
                for (int j = Math.Max(0, i - 50); j < Math.Min(sb.Count, i + 50); j++) { if (used[j]) continue; double err = Err(sa[i], sb[j]); if (err < bestErr) { bestErr = err; best = j; } }
                if (best >= 0 && bestErr <= tol) { used[best] = true; matched++; maxErr = Math.Max(maxErr, bestErr); }
                else if (best >= 0 && r.Mismatches.Count < 50) r.Mismatches.Add($"{t} #{i} err {bestErr:E2} a=[{Fmt(sa[i])}] b=[{Fmt(sb[best])}]");
            }
            r.PerType[t] = (la.Count, lb.Count, matched, maxErr);
            if (matched < n) r.Mismatches.Add($"{t} matched {matched} of {n}");
        }
        return r;
    }
    static string Fmt(double[] s) => string.Join(",", s.Take(8).Select(v => v.ToString("G6", CultureInfo.InvariantCulture)));
    static double Err(double[] a, double[] b) { if (a.Length != b.Length) return double.MaxValue; double m = 0; for (int i = 0; i < a.Length; i++) m = Math.Max(m, Math.Abs(a[i] - b[i])); return m; }
    sealed class Cmp : IComparer<double[]> { public static readonly Cmp Instance = new(); public int Compare(double[]? x, double[]? y) { if (x == null || y == null) return 0; int n = Math.Min(x.Length, y.Length); for (int i = 0; i < n; i++) { int c = x[i].CompareTo(y[i]); if (c != 0) return c; } return x.Length.CompareTo(y.Length); } }
    public static List<Entity> Flatten(Document d, bool blocks)
    {
        var list = d.Entities.Where(e => !e.PaperSpace).ToList();
        if (blocks) foreach (var b in d.Blocks.Values) if (!b.IsLayout && !b.Name.StartsWith("*D", StringComparison.OrdinalIgnoreCase)) list.AddRange(b.Entities);
        foreach (var ins in list.OfType<Insert>().ToList()) foreach (var at in ins.Attribs) list.Add(at.Text);
        return list;
    }

    /// <summary>All text strings (TEXT value, MTEXT plain) of model space and blocks.</summary>
    public static List<(string kind, string text)> Texts(Document d)
    {
        var list = new List<(string, string)>();
        foreach (var e in Flatten(d, true))
        {
            if (e is Text t) list.Add((t.DxfName, t.Value));
            else if (e is MText m) list.Add(("MTEXT", MTextParser.Plain(m.Value)));
        }
        return list;
    }
}
