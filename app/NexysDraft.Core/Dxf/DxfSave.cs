using NexysDraft.Core.Model;

namespace NexysDraft.Core.Dxf;

/// <summary>
/// DXF version on save (draft4 H1). nanoCAD 5 opens DXF up to AC1027 (2013) and rejects u04's AC1032 DXF ("This version of file format is
/// not supported"). Auto: a source newer than AC1027 is written by the R2000 model writer (font compatibility, paper space and Korean
/// handled as for DWG); otherwise the Preserve writer keeps the original version. The user can force either.
/// </summary>
public static class DxfSave
{
    public enum Ver { Auto, R2000, Keep }
    /// <summary>Newest DXF version nanoCAD 5 opens (user check, draft3).</summary>
    public const string NanoCadNewest = "AC1027";

    public static Ver Parse(string? s) => (s ?? "").Trim().ToLowerInvariant() switch { "r2000" or "ac1015" => Ver.R2000, "keep" or "preserve" or "original" => Ver.Keep, _ => Ver.Auto };
    public static bool TooNew(Document d) => string.CompareOrdinal(d.Version, NanoCadNewest) > 0;

    /// <summary>draft5f: an AC1015+ DXF whose LAYER records have no 390 (plot style) — AutoCAD answers "Error in LAYER Table. Did not receive
    /// PlotStyleName" and discards it (DWG TrueView 2027). NexysDraft 0.4.3/0.4.4 wrote such files; keeping their structure would keep the fault.</summary>
    public static bool NoPlotStyle(Document d) => string.CompareOrdinal(d.Version, "AC1015") >= 0 && d.Layers.Any(l => l.Raw != null && !l.Raw.Any(g => g.Code == 390));

    public static (DxfWriter.Mode mode, string reason) Choose(Document d, Ver v) => v switch
    {
        Ver.R2000 => (DxfWriter.Mode.R2000, $"R2000 (chosen; original {d.Version})"),
        Ver.Keep => (DxfWriter.Mode.Preserve, $"original version {d.Version} kept (chosen)"),
        _ => d.FromLibreDwg ? (DxfWriter.Mode.R2000, $"R2000: opened from DWG (LibreDWG output is regenerated so AutoCAD can read it)")
           : d.ObjectsRaw.Count == 0 || d.HeaderRaw.Count == 0 ? (DxfWriter.Mode.R2000, "R2000: new drawing (no source structure to keep; written complete so AutoCAD can read it)")
           : NoPlotStyle(d) ? (DxfWriter.Mode.R2000, "R2000: the source layers carry no plot style handle (AutoCAD rejects such a file; written complete)")
           : TooNew(d) ? (DxfWriter.Mode.R2000, $"R2000: original {d.Version} is newer than {NanoCadNewest} (nanoCAD 5 limit)") : (DxfWriter.Mode.Preserve, $"original version {d.Version} kept (≤ {NanoCadNewest})"),
    };
}
