using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;

namespace NexysDraft.Core.Dwg;

/// <param name="Blocking">predicted to make the DWG unreadable in other CAD programs</param>
/// <param name="RecommendDxf">readable, but DXF is the recommended format (dynamic blocks lose their parameters — run-draft3 decision)</param>
public sealed record CheckItem(string Kind, int Count, string Detail, List<string> Handles, bool Blocking, bool RecommendDxf = false);

public sealed class R2000CheckResult
{
    public List<CheckItem> Items { get; } = new();
    public List<FontFix> FontFixes { get; set; } = new();
    public double EstimateSeconds { get; set; }
    public long R2000DxfBytes { get; set; }
    public bool DwgOk => !Items.Any(i => i.Blocking);
    /// <summary>"dwg" when the drawing passes the pre-check, else "dxf" (plan B forces "dxf").</summary>
    public string Recommended { get; set; } = "dwg";
    public bool PlanB { get; set; }
    /// <summary>Raster IMAGE is the only blocking reason: the dialog offers "save DWG without the n IMAGE entities" (draft4 H2).</summary>
    public bool ImageOnly => Items.Any(i => i.Blocking) && Items.Where(i => i.Blocking).All(i => i.Kind == "image");
    public int ImageCount => Items.Where(i => i.Kind == "image").Sum(i => i.Count);
    public IEnumerable<string> Reasons => Items.Where(i => i.Blocking || i.RecommendDxf).Select(i => $"{i.Kind} ×{i.Count}: {i.Detail}");
}

/// <summary>
/// DWG (R2000, LibreDWG dxf2dwg) pre-check (draft3 G2). Blocking reasons are the things measured to make the DWG unreadable or lossy in other CAD
/// programs (ODA File Converter as proxy, nanoCAD confirmed for u02/u04): dynamic blocks, raster IMAGE (u06: "Object improperly read"), entities
/// newer than R2000 and the dxf2dwg time estimate over the limit. Lossy but readable items are listed without blocking: nameless blocks and
/// unresolved inserts (the R2000 writer drops them, u04 passes ODA), invalid splines (knot count ≠ control + degree + 1; ODA drops them), font fixes.
/// The draft2 "MTEXT-heavy" rule is gone: u01/u07 failed because their ByLayer linetype had handle B, which the writer now renumbers (G0).
/// </summary>
public static class R2000Check
{
    /// <summary>dxf2dwg run time over which a DWG save is not recommended (the gate timeout).</summary>
    public static double MaxSeconds { get; set; } = 300;
    /// <summary>Plan B (LibreDWG 0.14 kept): DWG is never the default and the 0.14 time model applies.</summary>
    public static bool PlanB { get; set; }

    /// <summary>dxf2dwg time from the R2000 DXF size. Master build (draft3 G0): u03 10.1 MB 1.7 s, u05 4.7 MB 0.67 s. 0.14: u02 3.9 MB 238 s, u04 2.2 MB 136 s.</summary>
    public static double EstimateSeconds(long r2000DxfBytes) { double mb = r2000DxfBytes / 1048576.0; return PlanB ? 1 + 12 * mb * mb : 0.2 + 0.16 * mb; }

    public static bool SplineValid(Spline s) => s.Control.Count == 0 || s.Knots.Count == 0 || s.Knots.Count == s.Control.Count + s.Degree + 1;

    public static R2000CheckResult Run(Document doc, bool fontCompat = true)
    {
        var r = new R2000CheckResult { PlanB = PlanB };
        var loss = R2000Loss.Detect(doc);
        IEnumerable<Entity> All() { foreach (var e in doc.Entities) yield return e; foreach (var b in doc.Blocks.Values) foreach (var e in b.Entities) yield return e; }
        List<string> H(IEnumerable<Entity> es) => es.Take(20).Select(e => e.Handle).ToList();
        foreach (var l in loss)
        {
            bool blocking = l.Kind is "entity", recommendDxf = l.Kind is "dynamic-block";
            List<string> handles = l.Kind switch
            {
                "unresolved-block" => H(All().OfType<Insert>().Where(i => i.Block.Length == 0 || !doc.Blocks.ContainsKey(i.Block))),
                "entity" => H(doc.Entities.Where(e => !R2000Loss.R2000Entities.Contains(e.DxfName))),
                _ => new List<string>(),
            };
            r.Items.Add(new CheckItem(l.Kind, l.Count, l.Detail, handles, blocking, recommendDxf));
        }
        var images = doc.Entities.Where(e => e.DxfName == "IMAGE").ToList();
        if (images.Count > 0) r.Items.Add(new CheckItem("image", images.Count, "raster IMAGE (image definition objects are not written)", H(images), true));
        var badSplines = All().OfType<Spline>().Where(s => !SplineValid(s)).ToList();
        if (badSplines.Count > 0) r.Items.Add(new CheckItem("spline", badSplines.Count, "SPLINE with knot count ≠ control points + degree + 1: other CAD programs drop it", H(badSplines), false));
        var bytes = DxfWriter.Bytes(doc, DxfWriter.Mode.R2000, out _);
        r.R2000DxfBytes = bytes.Length; r.EstimateSeconds = EstimateSeconds(bytes.Length);
        if (r.EstimateSeconds > MaxSeconds) r.Items.Add(new CheckItem("slow", 1, $"estimated dxf2dwg time {r.EstimateSeconds:F0} s > {MaxSeconds:F0} s (R2000 DXF {bytes.Length / 1048576.0:F1} MB)", new(), true));
        if (fontCompat) r.FontFixes = FontCompat.Plan(doc);
        r.Recommended = PlanB || !r.DwgOk || r.Items.Any(i => i.RecommendDxf) ? "dxf" : "dwg";
        return r;
    }
}
