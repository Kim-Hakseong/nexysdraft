using NexysDraft.Core.Model;

namespace NexysDraft.Core.Dwg;

public sealed record LossWarning(string Kind, int Count, string Detail);

/// <summary>What an R2000 DWG (the only version LibreDWG 0.14 writes) cannot carry: entity types newer than R2000, dynamic block data,
/// true colours / transparency, annotative scaling, newer object types. The save dialog shows this list before writing.</summary>
public static class R2000Loss
{
    /// <summary>Entity names that exist in R2000 (anything else is dropped from the R2000 DXF with a warning).</summary>
    public static readonly HashSet<string> R2000Entities = new(StringComparer.OrdinalIgnoreCase) { "3DFACE", "3DSOLID", "ACAD_PROXY_ENTITY", "ARC", "ATTDEF", "ATTRIB", "BODY", "CIRCLE", "DIMENSION", "ELLIPSE", "HATCH", "IMAGE", "INSERT", "LEADER", "LINE", "LWPOLYLINE", "MLINE", "MTEXT", "OLEFRAME", "OLE2FRAME", "POINT", "POLYLINE", "RAY", "REGION", "SEQEND", "SHAPE", "SOLID", "SPLINE", "TEXT", "TOLERANCE", "TRACE", "VERTEX", "VIEWPORT", "XLINE" };
    static readonly HashSet<string> R2000Objects = new(StringComparer.OrdinalIgnoreCase) { "ACDBDICTIONARYWDFLT", "ACDBPLACEHOLDER", "DICTIONARY", "DICTIONARYVAR", "DIMASSOC", "GROUP", "IDBUFFER", "IMAGEDEF", "IMAGEDEF_REACTOR", "LAYER_INDEX", "LAYOUT", "MLINESTYLE", "OBJECT_PTR", "PLOTSETTINGS", "RASTERVARIABLES", "SORTENTSTABLE", "SPATIAL_INDEX", "SPATIAL_FILTER", "VBA_PROJECT", "XRECORD", "ACAD_PROXY_OBJECT", "LAYERFILTER", "WIPEOUTVARIABLES" };

    public static List<LossWarning> Detect(Document doc)
    {
        var w = new List<LossWarning>();
        var newer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int trueColor = 0, transparency = 0, annotative = 0;
        void Scan(IEnumerable<Entity> es)
        {
            foreach (var e in es)
            {
                if (!R2000Entities.Contains(e.DxfName)) newer[e.DxfName] = newer.GetValueOrDefault(e.DxfName) + 1;
                if (e.TrueColor != null) trueColor++;
                var raw = e.Raw ?? e.Extra;
                if (raw != null) foreach (var g in raw) { if (g.Code == 440) transparency++; if (g.Code == 1001 && g.Value.Equals("AcadAnnotative", StringComparison.OrdinalIgnoreCase)) annotative++; }
                if (e is Insert ins) foreach (var a in ins.Attribs) if (a.Raw != null && a.Raw.Any(g => g.Code == 1001 && g.Value.Equals("AcadAnnotative", StringComparison.OrdinalIgnoreCase))) annotative++;
            }
        }
        Scan(doc.Entities); foreach (var b in doc.Blocks.Values) Scan(b.Entities);
        foreach (var kv in newer.OrderByDescending(k => k.Value)) w.Add(new LossWarning("entity", kv.Value, $"{kv.Key} is newer than R2000 — dropped"));
        if (trueColor > 0) w.Add(new LossWarning("truecolor", trueColor, "true colours (code 420) → nearest ACI index"));
        if (transparency > 0) w.Add(new LossWarning("transparency", transparency, "object transparency (code 440) is lost"));
        if (annotative > 0) w.Add(new LossWarning("annotative", annotative, "annotative scaling (AcadAnnotative xdata) is lost"));
        // dynamic blocks: evaluation graph / block representation objects, *U anonymous representations, AcDbBlockRepBTag xdata
        int dyn = 0; var dynBlocks = new List<string>();
        foreach (var b in doc.Blocks.Values)
        {
            bool isDyn = b.RecordRaw != null && b.RecordRaw.Any(g => g.Code == 1001 && (g.Value.Equals("AcDbBlockRepBTag", StringComparison.OrdinalIgnoreCase) || g.Value.Equals("AcDbDynamicBlockGUID", StringComparison.OrdinalIgnoreCase) || g.Value.Equals("AcDbDynamicBlockTrueName", StringComparison.OrdinalIgnoreCase)));
            if (isDyn) { dyn++; dynBlocks.Add(b.Name); }
        }
        int evalGraphs = 0, newerObjects = 0; var objNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < doc.ObjectsRaw.Count; i++)
        {
            var g = doc.ObjectsRaw[i]; if (g.Code != 0) continue;
            string n = g.Value.ToUpperInvariant();
            if (n is "ACAD_EVALUATION_GRAPH" or "BLOCKREPRESENTATION" or "ACDB_BLOCKREPRESENTATION_DATA" || n.StartsWith("BLOCK") && n != "BLOCK_RECORD") evalGraphs++;
            else if (!R2000Objects.Contains(n)) { newerObjects++; objNames[n] = objNames.GetValueOrDefault(n) + 1; }
        }
        if (dyn > 0 || evalGraphs > 0) w.Add(new LossWarning("dynamic-block", Math.Max(dyn, evalGraphs), $"dynamic block definitions become static ({string.Join(", ", dynBlocks.Take(5))}{(dynBlocks.Count > 5 ? ", …" : "")}; {evalGraphs} evaluation objects)"));
        if (newerObjects > 0) w.Add(new LossWarning("object", newerObjects, "objects newer than R2000 dropped: " + string.Join(", ", objNames.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key}×{k.Value}"))));
        int unresolved = 0; var unresolvedHandles = new List<string>();
        void ScanIns(IEnumerable<Entity> es) { foreach (var e in es) if (e is Insert i && (i.Block.Length == 0 || !doc.Blocks.ContainsKey(i.Block))) { unresolved++; if (unresolvedHandles.Count < 5) unresolvedHandles.Add(i.Handle); } }
        ScanIns(doc.Entities); foreach (var b in doc.Blocks.Values) ScanIns(b.Entities);
        if (unresolved > 0) w.Add(new LossWarning("unresolved-block", unresolved, "block references without a (named) block definition are dropped: " + string.Join(", ", unresolvedHandles)));
        int nameless = doc.Blocks.Values.Count(b => b.Name.Length == 0);
        if (nameless > 0) w.Add(new LossWarning("nameless-block", nameless, "block definitions without a name are dropped"));
        if (doc.VersionYear >= 2007 && doc.SourceUtf8) w.Add(new LossWarning("encoding", 1, $"text re-encoded from UTF-8 to {doc.CodePage} (characters outside it become \\U+XXXX)"));
        return w;
    }
}
