using NexysDraft.Core.Geometry;
using NexysDraft.Core.Model;

namespace NexysDraft.Core.Pdf;

public sealed record Sheet(double X0, double Y0, double X1, double Y1, string Label)
{
    public double W => X1 - X0; public double H => Y1 - Y0;
    public bool Contains(double x, double y) => x >= X0 && x <= X1 && y >= Y0 && y <= Y1;
}

/// <summary>
/// Drawing sheets laid out side by side in model space (the dwg2spdf rules, simplified): frame mode — a block inserted ≥2 times whose
/// boxes do not overlap and hold ≥60 % of the other content; rectangle mode — closed 4-vertex LWPOLYLINEs (or 4 LINEs) of one repeating
/// size; otherwise the whole drawing is one sheet. Sheets are returned in reading order (rows top→bottom, left→right).
/// </summary>
public static class SheetDetector
{
    const int MinSheets = 2; const double MinCoverage = 0.6, MaxOverlap = 0.05, SameSize = 0.005;

    public static List<Sheet> Detect(Document doc, out bool detected)
    {
        detected = false;
        var ents = doc.Entities.Where(e => !e.PaperSpace && e is not Unknown).ToList();
        var centres = new List<(double x, double y, string h)>();
        foreach (var e in ents) { var b = Bounds.Of(e, doc, 0); if (b == null) continue; centres.Add(((b.Value.x0 + b.Value.x1) / 2, (b.Value.y0 + b.Value.y1) / 2, e.Handle)); }
        var candidates = new List<(double cov, List<Sheet> sheets)>();
        // frame mode
        foreach (var grp in ents.OfType<Insert>().GroupBy(i => i.Block, StringComparer.OrdinalIgnoreCase))
        {
            if (grp.Count() < MinSheets || !doc.Blocks.TryGetValue(grp.Key, out var blk)) continue;
            var own = new HashSet<string>(grp.Select(i => i.Handle));
            var boxes = new List<Sheet>();
            foreach (var ins in grp) { var b = Bounds.Of(ins, doc, 0); if (b == null || b.Value.x1 - b.Value.x0 <= 0 || b.Value.y1 - b.Value.y0 <= 0) continue; boxes.Add(new Sheet(b.Value.x0, b.Value.y0, b.Value.x1, b.Value.y1, "")); }
            var other = centres.Where(c => !own.Contains(c.h)).ToList(); if (other.Count == 0) continue;
            var (cov, ok) = Believable(boxes, other); if (ok) candidates.Add((cov, boxes));
        }
        // rectangle mode
        if (candidates.Count == 0 || candidates.Max(c => c.cov) < 0.95)
        {
            var rects = new List<(double x0, double y0, double x1, double y1, string h)>();
            foreach (var e in ents)
            {
                if (e is LwPolyline p && p.Vertices.Count is 4 or 5 && (p.Closed || p.Vertices.Count == 5) && p.Vertices.All(v => Math.Abs(v.Bulge) < 1e-12)) { var r = AsRect(p.Vertices.Select(v => (v.X, v.Y)).ToList()); if (r != null) rects.Add((r.Value.x0, r.Value.y0, r.Value.x1, r.Value.y1, e.Handle)); }
                else if (e is Polyline hp && hp.Vertices.Count is 4 or 5 && (hp.Closed || hp.Vertices.Count == 5)) { var r = AsRect(hp.Vertices.Select(v => (v.X, v.Y)).ToList()); if (r != null) rects.Add((r.Value.x0, r.Value.y0, r.Value.x1, r.Value.y1, e.Handle)); }
            }
            // groups of the same size
            var groups = rects.GroupBy(r => (Math.Round(Math.Log(Math.Max(1e-9, r.x1 - r.x0)) / SameSize), Math.Round(Math.Log(Math.Max(1e-9, r.y1 - r.y0)) / SameSize))).Where(g => g.Count() >= MinSheets).OrderByDescending(g => g.Sum(r => (r.x1 - r.x0) * (r.y1 - r.y0))).Take(8);
            foreach (var g in groups)
            {
                var own = new HashSet<string>(g.Select(r => r.h)); var boxes = g.Select(r => new Sheet(r.x0, r.y0, r.x1, r.y1, "")).ToList();
                // drop rectangles nested inside another of the group
                boxes = boxes.Where(b => !boxes.Any(o => o != b && o.X0 <= b.X0 && o.Y0 <= b.Y0 && o.X1 >= b.X1 && o.Y1 >= b.Y1 && (o.W > b.W || o.H > b.H))).ToList();
                if (boxes.Count < MinSheets) continue;
                var other = centres.Where(c => !own.Contains(c.h)).ToList(); if (other.Count == 0) continue;
                var (cov, ok) = Believable(boxes, other); if (ok) candidates.Add((cov, boxes));
            }
        }
        if (candidates.Count == 0) return Fallback(doc);
        var best = candidates.OrderByDescending(c => c.cov).First();
        if (best.cov < MinCoverage || best.sheets.Count < MinSheets) return Fallback(doc);
        detected = true;
        return ReadingOrder(best.sheets);
    }

    static List<Sheet> Fallback(Document doc)
    {
        var b = Bounds.Of(doc); if (b == null) return new List<Sheet> { new(0, 0, 100, 100, "001") };
        return new List<Sheet> { new(b.Value.x0, b.Value.y0, b.Value.x1, b.Value.y1, "001") };
    }
    static (double cov, bool ok) Believable(List<Sheet> boxes, List<(double x, double y, string h)> centres)
    {
        if (boxes.Count < MinSheets) return (0, false);
        double inter = 0, total = 0;
        for (int i = 0; i < boxes.Count; i++) { total += boxes[i].W * boxes[i].H; for (int j = i + 1; j < boxes.Count; j++) { double w = Math.Min(boxes[i].X1, boxes[j].X1) - Math.Max(boxes[i].X0, boxes[j].X0), h = Math.Min(boxes[i].Y1, boxes[j].Y1) - Math.Max(boxes[i].Y0, boxes[j].Y0); if (w > 0 && h > 0) inter += w * h; } }
        if (total <= 0 || inter / total > MaxOverlap) return (0, false);
        int inside = centres.Count(c => boxes.Any(b => b.Contains(c.x, c.y)));
        return ((double)inside / centres.Count, true);
    }
    static (double x0, double y0, double x1, double y1)? AsRect(List<(double X, double Y)> pts)
    {
        if (pts.Count == 5) { if (Math.Abs(pts[0].X - pts[4].X) > 1e-9 || Math.Abs(pts[0].Y - pts[4].Y) > 1e-9) return null; pts = pts.Take(4).ToList(); }
        if (pts.Count != 4) return null;
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y); double tol = 1e-4 * Math.Max(x1 - x0, y1 - y0);
        if (Math.Min(x1 - x0, y1 - y0) <= 10 * tol) return null;
        foreach (var p in pts) { bool onX = Math.Abs(p.X - x0) <= tol || Math.Abs(p.X - x1) <= tol, onY = Math.Abs(p.Y - y0) <= tol || Math.Abs(p.Y - y1) <= tol; if (!onX || !onY) return null; }
        for (int i = 0; i < 4; i++) { var a = pts[i]; var b = pts[(i + 1) % 4]; int turns = (Math.Abs(a.X - b.X) > tol ? 1 : 0) + (Math.Abs(a.Y - b.Y) > tol ? 1 : 0); if (turns != 1) return null; }
        return (x0, y0, x1, y1);
    }
    static List<Sheet> ReadingOrder(List<Sheet> sheets)
    {
        double h = sheets.Average(s => s.H); var rows = new List<List<Sheet>>();
        foreach (var s in sheets.OrderByDescending(s => s.Y1)) { var row = rows.FirstOrDefault(r => Math.Abs(r[0].Y1 - s.Y1) < h * 0.5); if (row == null) rows.Add(new List<Sheet> { s }); else row.Add(s); }
        var ordered = rows.SelectMany(r => r.OrderBy(s => s.X0)).ToList();
        return ordered.Select((s, i) => s with { Label = (i + 1).ToString("D3") }).ToList();
    }
}
