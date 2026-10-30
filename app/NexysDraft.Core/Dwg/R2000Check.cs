using NexysDraft.Core.Dxf;
using NexysDraft.Core.Model;

namespace NexysDraft.Core.Dwg;

public sealed record CheckItem(string Kind, int Count, string Detail, List<string> Handles, bool Blocking);

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
    public IEnumerable<string> Reasons => Items.Where(i => i.Blocking).Select(i => $"{i.Kind} ×{i.Count}: {i.Detail}");
}

/// <summary>
/// DWG (R2000, LibreDWG dxf2dwg) pre-check (draft3 G2). Blocking reasons are the things measured to make the DWG unreadable or lossy in other CAD
/// programs (ODA File Converter as proxy, nanoCAD confirmed for u02/u04): dynamic blocks, broken (nameless / unresolved) block references, raster
/// IMAGE, entities newer than R2000, the dxf2dwg time estimate over the limit, and the MTEXT-heavy drawings dxf2dwg 0.14 writes into files
/// ODA reads as empty (u01, u07 — docs/libredwg.md 7b). Non-blocking items (true colours, annotative scale, font fixes) are listed for the user.
/// </summary>
public static class R2000Check
{
    /// <summary>dxf2dwg run time over which a DWG save is not recommended (the gate timeout).</summary>
    public static double MaxSeconds { get; set; } = 300;
    /// <summary>Plan B (LibreDWG not replaced by a better build): DWG is never the default.</summary>
    public static bool PlanB { get; set; }
    /// <summary>MTEXT count in model space above which dxf2dwg 0.14 output was unreadable in ODA (u01 187, u07 679 vs u02 4, u04 2).</summary>
    public static int MaxMText { get; set; } = 100;

    public static double EstimateSeconds(long r2000DxfBytes) { double mb = r2000DxfBytes / 1048576.0; return 1 + 12 * mb * mb; }

    public static R2000CheckResult Run(Document doc, bool fontCompat = true)
    {
        var r = new R2000CheckResult { PlanB = PlanB };
        var loss = R2000Loss.Detect(doc);
        IEnumerable<Entity> All() { foreach (var e in doc.Entities) yield return e; foreach (var b in doc.Blocks.Values) foreach (var e in b.Entities) yield return e; }
        List<string> H(IEnumerable<Entity> es) => es.Take(20).Select(e => e.Handle).ToList();
        foreach (var l in loss)
        {
            bool blocking = l.Kind is "dynamic-block" or "unresolved-block" or "nameless-block" or "entity";
            List<string> handles = l.Kind switch
            {
                "unresolved-block" => H(All().OfType<Insert>().Where(i => i.Block.Length == 0 || !doc.Blocks.ContainsKey(i.Block))),
                "entity" => H(doc.Entities.Where(e => !R2000Loss.R2000Entities.Contains(e.DxfName))),
                _ => new List<string>(),
            };
            r.Items.Add(new CheckItem(l.Kind, l.Count, l.Detail, handles, blocking));
        }
        var images = doc.Entities.Where(e => e.DxfName == "IMAGE").ToList();
        if (images.Count > 0) r.Items.Add(new CheckItem("image", images.Count, "raster IMAGE (image definition objects are not written)", H(images), true));
        int mtext = doc.Entities.Count(e => e is MText && !e.PaperSpace);
        if (mtext > MaxMText) r.Items.Add(new CheckItem("mtext-heavy", mtext, $"{mtext} MTEXT in model space (> {MaxMText}): dxf2dwg 0.14 wrote such drawings as files other CAD programs read as empty", H(doc.Entities.Where(e => e is MText)), true));
        var splines = All().Where(e => e is Spline).ToList();
        if (splines.Count > 0) r.Items.Add(new CheckItem("spline", splines.Count, "SPLINE: dxf2dwg 0.14 may drop splines (generated sample g04)", H(splines), false));
        var bytes = DxfWriter.Bytes(doc, DxfWriter.Mode.R2000, out _);
        r.R2000DxfBytes = bytes.Length; r.EstimateSeconds = EstimateSeconds(bytes.Length);
        if (r.EstimateSeconds > MaxSeconds) r.Items.Add(new CheckItem("slow", 1, $"estimated dxf2dwg time {r.EstimateSeconds:F0} s > {MaxSeconds:F0} s (R2000 DXF {bytes.Length / 1048576.0:F1} MB)", new(), true));
        if (fontCompat) r.FontFixes = FontCompat.Plan(doc);
        r.Recommended = PlanB || !r.DwgOk ? "dxf" : "dwg";
        return r;
    }
}
