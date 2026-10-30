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

    public static (DxfWriter.Mode mode, string reason) Choose(Document d, Ver v) => v switch
    {
        Ver.R2000 => (DxfWriter.Mode.R2000, $"R2000 (chosen; original {d.Version})"),
        Ver.Keep => (DxfWriter.Mode.Preserve, $"original version {d.Version} kept (chosen)"),
        _ => TooNew(d) ? (DxfWriter.Mode.R2000, $"R2000: original {d.Version} is newer than {NanoCadNewest} (nanoCAD 5 limit)") : (DxfWriter.Mode.Preserve, $"original version {d.Version} kept (≤ {NanoCadNewest})"),
    };
}
