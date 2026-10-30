using System.Globalization;
using System.Text;
using NexysDraft.Core.Dwg;
using NexysDraft.Core.Model;

namespace NexysDraft.Core.Dxf;

/// <summary>
/// ASCII DXF writer. <see cref="Mode.Preserve"/> rewrites the file as read (same version; untouched entities, tables, objects verbatim;
/// edited/new entities from the model). <see cref="Mode.R2000"/> generates a clean AC1015 file from the model (the input of LibreDWG's
/// dxf2dwg): fixed skeleton (tables, layouts, dictionaries), entities newer than R2000 dropped with warnings.
/// </summary>
public sealed class DxfWriter
{
    public enum Mode { Preserve, R2000 }
    readonly Document _doc; readonly Mode _mode; readonly Encoding _enc; MemoryStream _ms = new();
    /// <summary>R2000 mode: write the full AutoCAD-style OBJECTS skeleton (layouts, plot settings, mline style). Off by default — see the OBJECTS note in BuildR2000.</summary>
    public static bool FullObjects { get; set; }
    readonly bool _fullObjects = FullObjects;
    Dictionary<string, FontFix>? _fontFixes;
    /// <summary>R2000 mode: hatch loops as tessellated polyline loops and dimension blocks written explicitly (dxf2dwg 0.14 drops edge-type hatch boundaries — docs/libredwg.md).</summary>
    public static bool HatchAsPolylineLoops { get; set; } = true;
    public List<string> Warnings { get; } = new();
    readonly int _year;
    string _msHandle = "", _psHandle = "";

    DxfWriter(Document doc, Mode mode)
    {
        _doc = doc; _mode = mode;
        _year = mode == Mode.R2000 ? 2000 : doc.VersionYear;
        _enc = _year >= 2007 ? new UTF8Encoding(false) : DxfCodec.CodePageEncoding(doc.CodePage);
    }

    public static List<string> Write(Document doc, string path, Mode mode, IReadOnlyList<FontFix>? fontFixes = null)
    {
        var w = new DxfWriter(doc, mode) { _fontFixes = fontFixes?.ToDictionary(f => f.Style, StringComparer.OrdinalIgnoreCase) };
        var bytes = w.Build();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, bytes);
        return w.Warnings;
    }
    public static byte[] Bytes(Document doc, Mode mode, out List<string> warnings) { var w = new DxfWriter(doc, mode); var b = w.Build(); warnings = w.Warnings; return b; }

    // ------------------------------------------------------------------ primitives
    void W(int code, string value)
    {
        var c = Encoding.ASCII.GetBytes(code.ToString(CultureInfo.InvariantCulture).PadLeft(3) + "\r\n"); _ms.Write(c, 0, c.Length);
        var v = DxfCodec.Encode(value, _enc); _ms.Write(v, 0, v.Length); _ms.WriteByte(13); _ms.WriteByte(10);
    }
    void W(int code, double v) => W(code, DxfCodec.FormatDouble(v));
    void W(int code, int v) => W(code, v.ToString(CultureInfo.InvariantCulture));
    void P(int code, V3 p, bool z = true) { W(code, p.X); W(code + 10, p.Y); if (z) W(code + 20, p.Z); }
    void Raw(IEnumerable<Group> gs) { foreach (var g in gs) if (Ok(g.Code)) W(g.Code, g.Value); }
    bool Ok(int code) => _mode != Mode.R2000 || !(code is >= 420 and <= 449 || code is 347 or 348 or 284 or 390 or 430 or 440);

    byte[] Build()
    {
        if (_mode == Mode.R2000) BuildR2000(); else BuildPreserve();
        return _ms.ToArray();
    }

    // ------------------------------------------------------------------ preserve
    void BuildPreserve()
    {
        W(0, "SECTION"); W(2, "HEADER");
        for (int i = 0; i < _doc.HeaderRaw.Count; i++)
        {
            var g = _doc.HeaderRaw[i];
            if (g.Code == 9 && g.Value == "$HANDSEED" && i + 1 < _doc.HeaderRaw.Count) { W(9, "$HANDSEED"); W(5, (_doc.HandSeed + 64).ToString("X")); i++; continue; }
            W(g.Code, g.Value);
        }
        W(0, "ENDSEC");
        if (_doc.ClassesRaw.Count > 0 || _doc.VersionYear >= 2000) { W(0, "SECTION"); W(2, "CLASSES"); Raw(_doc.ClassesRaw); W(0, "ENDSEC"); }
        W(0, "SECTION"); W(2, "TABLES");
        foreach (var t in _doc.RawTables)
        {
            W(0, "TABLE"); string th = t.Head.FirstOrDefault(g => g.Code == 5).Value ?? "";
            Raw(t.Head);
            switch (t.Name.ToUpperInvariant())
            {
                case "LAYER": foreach (var l in _doc.Layers) WriteLayer(l, th); break;
                case "LTYPE": foreach (var l in _doc.Linetypes) if (l.Raw != null) Raw(l.Raw); else WriteLtype(l, th); break;
                case "STYLE": foreach (var s in _doc.Styles) EmitStyle(s, th, true); break;
                case "BLOCK_RECORD":
                    foreach (var e in t.Entries) Raw(e);
                    foreach (var b in _doc.Blocks.Values) if (b.RecordRaw == null && !t.Entries.Any(e => e.Any(g => g.Code == 2 && g.Value.Equals(b.Name, StringComparison.OrdinalIgnoreCase)))) WriteBlockRecord(b, th, null);
                    break;
                default: foreach (var e in t.Entries) Raw(e); break;
            }
            W(0, "ENDTAB");
        }
        W(0, "ENDSEC");
        W(0, "SECTION"); W(2, "BLOCKS");
        foreach (var key in _doc.BlockOrder) WriteBlock(_doc.Blocks[key]);
        W(0, "ENDSEC");
        W(0, "SECTION"); W(2, "ENTITIES");
        foreach (var e in _doc.Entities) WriteEntity(e, e.Owner);
        W(0, "ENDSEC");
        W(0, "SECTION"); W(2, "OBJECTS"); Raw(_doc.ObjectsRaw); W(0, "ENDSEC");
        foreach (var (name, groups) in _doc.OtherSections) { if (name.Equals("THUMBNAILIMAGE", StringComparison.OrdinalIgnoreCase)) continue; W(0, "SECTION"); W(2, name); Raw(groups); W(0, "ENDSEC"); }
        W(0, "EOF");
    }

    // ------------------------------------------------------------------ R2000 clean file
    string _tLtype = "", _tLayer = "", _tStyle = "", _tView = "", _tUcs = "", _tAppid = "", _tDimstyle = "", _tBlockRec = "", _tVport = "";
    string TableHandle(string name) { var t = _doc.RawTables.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); var h = t?.Head.FirstOrDefault(g => g.Code == 5).Value; return string.IsNullOrEmpty(h) ? _doc.NewHandle() : h; }

    void BuildR2000()
    {
        // body first (allocates handles), header last with the final HANDSEED
        _tVport = TableHandle("VPORT"); _tLtype = TableHandle("LTYPE"); _tLayer = TableHandle("LAYER"); _tStyle = TableHandle("STYLE"); _tView = TableHandle("VIEW"); _tUcs = TableHandle("UCS"); _tAppid = TableHandle("APPID"); _tDimstyle = TableHandle("DIMSTYLE"); _tBlockRec = TableHandle("BLOCK_RECORD");
        _msHandle = _doc.ModelSpaceHandle; if (_msHandle == "") _msHandle = _doc.NewHandle();
        _psHandle = _doc.PaperSpaceHandle; if (_psHandle == "") _psHandle = _doc.NewHandle();
        string hRoot = _doc.NewHandle(), hGroup = _doc.NewHandle(), hPsn = _doc.NewHandle(), hPlaceholder = _doc.NewHandle(), hMl = _doc.NewHandle(), hMlStd = _doc.NewHandle(), hPlot = _doc.NewHandle(), hLayouts = _doc.NewHandle(), hLayoutM = _doc.NewHandle(), hLayout1 = _doc.NewHandle();
        var sw = new MemoryStream(); SwapStream(sw);
        // CLASSES (empty is valid)
        W(0, "SECTION"); W(2, "CLASSES"); W(0, "ENDSEC");
        W(0, "SECTION"); W(2, "TABLES");
        // VPORT
        W(0, "TABLE"); W(2, "VPORT"); W(5, _tVport); W(330, "0"); W(100, "AcDbSymbolTable"); W(70, 1);
        var vp = _doc.RawTables.FirstOrDefault(t => t.Name.Equals("VPORT", StringComparison.OrdinalIgnoreCase));
        if (false && vp != null && vp.Entries.Count > 0) foreach (var e in vp.Entries) Raw(e);   // R2000: VPORT regenerated
        else
        {
            var (mn, mx) = Extents();
            W(0, "VPORT"); W(5, _doc.NewHandle()); W(330, _tVport); W(100, "AcDbSymbolTableRecord"); W(100, "AcDbViewportTableRecord"); W(2, "*Active"); W(70, 0);
            W(10, 0.0); W(20, 0.0); W(11, 1.0); W(21, 1.0); W(12, (mn.X + mx.X) / 2); W(22, (mn.Y + mx.Y) / 2); W(13, 0.0); W(23, 0.0); W(14, 10.0); W(24, 10.0); W(15, 10.0); W(25, 10.0); W(16, 0.0); W(26, 0.0); W(36, 1.0); W(17, 0.0); W(27, 0.0); W(37, 0.0);
            W(40, Math.Max(1, mx.Y - mn.Y) * 1.1); W(41, 1.5); W(42, 50.0); W(43, 0.0); W(44, 0.0); W(50, 0.0); W(51, 0.0); W(71, 0); W(72, 100); W(73, 1); W(74, 3); W(75, 0); W(76, 0); W(77, 0); W(78, 0);
        }
        W(0, "ENDTAB");
        // LTYPE
        W(0, "TABLE"); W(2, "LTYPE"); W(5, _tLtype); W(330, "0"); W(100, "AcDbSymbolTable"); W(70, _doc.Linetypes.Count + 3);
        var ltNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in new[] { "ByBlock", "ByLayer", "Continuous" }) { var l = _doc.Linetypes.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)); if (l == null) { WriteLtype(new LineType { Name = n, Description = n == "Continuous" ? "Solid line" : "", Handle = _doc.NewHandle() }, _tLtype); ltNames.Add(n); } }
        foreach (var l in _doc.Linetypes)
        {
            if (!ltNames.Add(l.Name)) continue;
            // ByBlock/ByLayer/Continuous get fresh handles: LibreDWG dxf2dwg keeps the source handle, and a source ByLayer at
            // handle B (u01/u06/u07, docs/libredwg-build.md) yields a DWG that ODA reads as empty. Layers refer to linetypes by name.
            bool std = l.Name.Equals("ByBlock", StringComparison.OrdinalIgnoreCase) || l.Name.Equals("ByLayer", StringComparison.OrdinalIgnoreCase) || l.Name.Equals("Continuous", StringComparison.OrdinalIgnoreCase);
            WriteLtype(std ? new LineType { Name = l.Name, Description = l.Description, Pattern = l.Pattern, Length = l.Length, Handle = _doc.NewHandle() } : l, _tLtype);
        }   // R2000: always regenerated — raw LibreDWG LTYPE/STYLE entries make dxf2dwg write a DWG that ODA rejects silently (0-byte output, work/oda/exp5 v7e)
        W(0, "ENDTAB");
        // LAYER
        W(0, "TABLE"); W(2, "LAYER"); W(5, _tLayer); W(330, "0"); W(100, "AcDbSymbolTable"); W(70, _doc.Layers.Count + 1);
        if (!_doc.Layers.Any(l => l.Name == "0")) WriteLayer(new Layer { Name = "0", Handle = _doc.NewHandle(), Dirty = true }, _tLayer);
        foreach (var l in _doc.Layers) WriteLayer(l, _tLayer);
        W(0, "ENDTAB");
        // STYLE
        W(0, "TABLE"); W(2, "STYLE"); W(5, _tStyle); W(330, "0"); W(100, "AcDbSymbolTable"); W(70, _doc.Styles.Count + 1);
        if (!_doc.Styles.Any(s => s.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase))) WriteStyle(new TextStyle { Name = "Standard", Font = "txt", Handle = _doc.NewHandle() }, _tStyle);
        foreach (var s in _doc.Styles) EmitStyle(s, _tStyle, false);
        W(0, "ENDTAB");
        W(0, "TABLE"); W(2, "VIEW"); W(5, _tView); W(330, "0"); W(100, "AcDbSymbolTable"); W(70, 0); W(0, "ENDTAB");
        W(0, "TABLE"); W(2, "UCS"); W(5, _tUcs); W(330, "0"); W(100, "AcDbSymbolTable"); W(70, 0); W(0, "ENDTAB");
        // APPID
        W(0, "TABLE"); W(2, "APPID"); W(5, _tAppid); W(330, "0"); W(100, "AcDbSymbolTable");
        var ap = _doc.RawTables.FirstOrDefault(t => t.Name.Equals("APPID", StringComparison.OrdinalIgnoreCase));
        var appids = ap?.Entries ?? new List<List<Group>>();
        W(70, appids.Count + 1);
        if (!appids.Any(e => e.Any(g => g.Code == 2 && g.Value == "ACAD"))) { W(0, "APPID"); W(5, _doc.NewHandle()); W(330, _tAppid); W(100, "AcDbSymbolTableRecord"); W(100, "AcDbRegAppTableRecord"); W(2, "ACAD"); W(70, 0); }
        foreach (var e in appids) Raw(e);
        W(0, "ENDTAB");
        // DIMSTYLE
        W(0, "TABLE"); W(2, "DIMSTYLE"); W(5, _tDimstyle); W(330, "0"); W(100, "AcDbSymbolTable");
        var ds = _doc.RawTables.FirstOrDefault(t => t.Name.Equals("DIMSTYLE", StringComparison.OrdinalIgnoreCase));
        var dsEntries = ds?.Entries ?? new List<List<Group>>();
        W(70, dsEntries.Count + 1); W(100, "AcDbDimStyleTable");
        if (dsEntries.Count == 0) { W(0, "DIMSTYLE"); W(105, _doc.NewHandle()); W(330, _tDimstyle); W(100, "AcDbSymbolTableRecord"); W(100, "AcDbDimStyleTableRecord"); W(2, "Standard"); W(70, 0); W(41, 2.5); W(42, 0.625); W(44, 1.25); W(140, 2.5); W(147, 0.625); W(77, 1); W(271, 2); W(272, 2); W(279, 1); }
        else foreach (var e in dsEntries) Raw(e);
        W(0, "ENDTAB");
        // BLOCK_RECORD
        var blocks = _doc.BlockOrder.Select(k => _doc.Blocks[k]).Where(b => !b.IsLayout && b.Name.Length > 0).ToList();   // nameless blocks cannot be referenced (R2000Loss reports them)
        int droppedLayouts = _doc.Blocks.Values.Count(b => b.Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase) && !b.Name.Equals("*Paper_Space", StringComparison.OrdinalIgnoreCase));
        if (droppedLayouts > 0) Warnings.Add($"{droppedLayouts} additional paper-space layout(s) dropped (R2000 export keeps Model and one Layout)");
        foreach (var b in blocks) if (string.IsNullOrEmpty(b.RecordHandle) || b.RecordHandle.Trim() == "0") b.RecordHandle = _doc.NewHandle();
        // BLOCK / ENDBLK entities always get fresh (high) handles: dxf2dwg 0.14 reserves low handles for the objects it synthesizes and reports
        // "Duplicate handle 1F" (the source's *Paper_Space ENDBLK) — the resulting DWG then comes out of ODA as a 0-byte file (u01, u06, u07)
        foreach (var b in blocks) { b.Handle = _doc.NewHandle(); b.EndHandle = _doc.NewHandle(); }
        W(0, "TABLE"); W(2, "BLOCK_RECORD"); W(5, _tBlockRec); W(330, "0"); W(100, "AcDbSymbolTable"); W(70, blocks.Count + 2);
        WriteBlockRecord(new Block { Name = "*Model_Space", RecordHandle = _msHandle }, _tBlockRec, _fullObjects ? hLayoutM : null);
        WriteBlockRecord(new Block { Name = "*Paper_Space", RecordHandle = _psHandle }, _tBlockRec, _fullObjects ? hLayout1 : null);
        foreach (var b in blocks) WriteBlockRecord(b, _tBlockRec, null);
        W(0, "ENDTAB");
        W(0, "ENDSEC");
        // BLOCKS
        W(0, "SECTION"); W(2, "BLOCKS");
        var ms = _doc.Blocks.TryGetValue("*Model_Space", out var msb) ? msb : new Block { Name = "*Model_Space" };
        var ps = _doc.Blocks.TryGetValue("*Paper_Space", out var psb) ? psb : new Block { Name = "*Paper_Space" };
        ms.RecordHandle = _msHandle; ps.RecordHandle = _psHandle; ms.Handle = _doc.NewHandle(); ms.EndHandle = _doc.NewHandle(); ps.Handle = _doc.NewHandle(); ps.EndHandle = _doc.NewHandle();
        WriteBlockClean(ms); WriteBlockClean(ps);
        foreach (var b in blocks) WriteBlockClean(b);
        W(0, "ENDSEC");
        // ENTITIES
        W(0, "SECTION"); W(2, "ENTITIES");
        foreach (var e in _doc.Entities) WriteEntity(e, e.PaperSpace ? _psHandle : _msHandle);
        W(0, "ENDSEC");
        // OBJECTS: only the root dictionary. LAYOUT/PLOTSETTINGS/MLINESTYLE/placeholder objects written here make dxf2dwg 0.14 produce a DWG
        // that ODA ("Object improperly read") and nanoCAD (RECOVER, "AcDbLayout ModelType flag invalid") reject; with the root dictionary alone
        // dxf2dwg synthesizes its own layouts and the DWG reads cleanly (work/oda/exp varE, docs/libredwg.md).
        if (_fullObjects)
        {
            W(0, "SECTION"); W(2, "OBJECTS");
            W(0, "DICTIONARY"); W(5, hRoot); W(330, "0"); W(100, "AcDbDictionary"); W(281, 1);
            W(3, "ACAD_GROUP"); W(350, hGroup); W(3, "ACAD_LAYOUT"); W(350, hLayouts); W(3, "ACAD_MLINESTYLE"); W(350, hMl); W(3, "ACAD_PLOTSETTINGS"); W(350, hPlot); W(3, "ACAD_PLOTSTYLENAME"); W(350, hPsn);
            W(0, "DICTIONARY"); W(5, hGroup); W(330, hRoot); W(100, "AcDbDictionary"); W(281, 1);
            W(0, "ACDBDICTIONARYWDFLT"); W(5, hPsn); W(330, hRoot); W(100, "AcDbDictionary"); W(281, 1); W(3, "Normal"); W(350, hPlaceholder); W(100, "AcDbDictionaryWithDefault"); W(340, hPlaceholder);
            W(0, "ACDBPLACEHOLDER"); W(5, hPlaceholder); W(330, hPsn);
            W(0, "DICTIONARY"); W(5, hMl); W(330, hRoot); W(100, "AcDbDictionary"); W(281, 1); W(3, "Standard"); W(350, hMlStd);
            W(0, "MLINESTYLE"); W(5, hMlStd); W(330, hMl); W(100, "AcDbMlineStyle"); W(2, "Standard"); W(70, 0); W(3, ""); W(62, 256); W(51, 90.0); W(52, 90.0); W(71, 2); W(49, 0.5); W(62, 256); W(6, "BYLAYER"); W(49, -0.5); W(62, 256); W(6, "BYLAYER");
            W(0, "DICTIONARY"); W(5, hPlot); W(330, hRoot); W(100, "AcDbDictionary"); W(281, 1);
            W(0, "DICTIONARY"); W(5, hLayouts); W(330, hRoot); W(100, "AcDbDictionary"); W(281, 1); W(3, "Layout1"); W(350, hLayout1); W(3, "Model"); W(350, hLayoutM);
            WriteLayout(hLayoutM, hLayouts, "Model", 0, _msHandle);
            WriteLayout(hLayout1, hLayouts, "Layout1", 1, _psHandle);
        }
        else
        {
            W(0, "SECTION"); W(2, "OBJECTS");
            W(0, "DICTIONARY"); W(5, hRoot); W(330, "0"); W(100, "AcDbDictionary"); W(281, 1);
        }
        W(0, "ENDSEC");
        W(0, "EOF");
        var bodyBytes = _ms.ToArray();
        // HEADER
        SwapStream(new MemoryStream());
        W(0, "SECTION"); W(2, "HEADER");
        W(9, "$ACADVER"); W(1, "AC1015"); W(9, "$ACADMAINTVER"); W(70, 6); W(9, "$DWGCODEPAGE"); W(3, _doc.CodePage is { Length: > 0 } cp ? cp : "ANSI_949");
        HeaderPoint("$INSBASE", new V3(0, 0, 0)); var (emn, emx) = Extents(); HeaderPoint("$EXTMIN", emn); HeaderPoint("$EXTMAX", emx);
        HeaderPoint("$LIMMIN", new V3(0, 0), false); HeaderPoint("$LIMMAX", new V3(420, 297), false);
        HeaderVar("$ORTHOMODE", 70, "0"); HeaderVar("$REGENMODE", 70, "1"); HeaderVar("$FILLMODE", 70, "1"); HeaderVar("$QTEXTMODE", 70, "0"); HeaderVar("$MIRRTEXT", 70, "0");
        HeaderVar("$LTSCALE", 40, _doc.HeaderString("$LTSCALE", "1.0")); HeaderVar("$ATTMODE", 70, "1"); HeaderVar("$TEXTSIZE", 40, _doc.HeaderString("$TEXTSIZE", "2.5")); HeaderVar("$TRACEWID", 40, "1.0");
        HeaderVar("$TEXTSTYLE", 7, _doc.HeaderString("$TEXTSTYLE", "Standard")); HeaderVar("$CLAYER", 8, _doc.LayerOf(_doc.HeaderString("$CLAYER", "0")) != null ? _doc.HeaderString("$CLAYER", "0") : "0");
        HeaderVar("$CELTYPE", 6, "ByLayer"); HeaderVar("$CECOLOR", 62, "256"); HeaderVar("$CELTSCALE", 40, "1.0"); HeaderVar("$DISPSILH", 70, "0");
        foreach (var dv in new[] { ("$DIMSCALE", 40, "1.0"), ("$DIMASZ", 40, "2.5"), ("$DIMEXO", 40, "0.625"), ("$DIMDLI", 40, "3.75"), ("$DIMRND", 40, "0.0"), ("$DIMDLE", 40, "0.0"), ("$DIMEXE", 40, "1.25"), ("$DIMTP", 40, "0.0"), ("$DIMTM", 40, "0.0"), ("$DIMTXT", 40, "2.5"), ("$DIMCEN", 40, "2.5"), ("$DIMTSZ", 40, "0.0"), ("$DIMTOL", 70, "0"), ("$DIMLIM", 70, "0"), ("$DIMTIH", 70, "0"), ("$DIMTOH", 70, "0"), ("$DIMSE1", 70, "0"), ("$DIMSE2", 70, "0"), ("$DIMTAD", 70, "1"), ("$DIMZIN", 70, "8"), ("$DIMBLK", 1, ""), ("$DIMASO", 70, "1"), ("$DIMSHO", 70, "1"), ("$DIMPOST", 1, ""), ("$DIMAPOST", 1, ""), ("$DIMALT", 70, "0"), ("$DIMALTD", 70, "3"), ("$DIMALTF", 40, "0.03937"), ("$DIMLFAC", 40, "1.0"), ("$DIMTOFL", 70, "1"), ("$DIMTVP", 40, "0.0"), ("$DIMTIX", 70, "0"), ("$DIMSOXD", 70, "0"), ("$DIMSAH", 70, "0"), ("$DIMBLK1", 1, ""), ("$DIMBLK2", 1, ""), ("$DIMSTYLE", 2, "Standard"), ("$DIMCLRD", 70, "0"), ("$DIMCLRE", 70, "0"), ("$DIMCLRT", 70, "0"), ("$DIMTFAC", 40, "1.0"), ("$DIMGAP", 40, "0.625"), ("$DIMJUST", 70, "0"), ("$DIMSD1", 70, "0"), ("$DIMSD2", 70, "0"), ("$DIMTOLJ", 70, "0"), ("$DIMTZIN", 70, "8"), ("$DIMALTZ", 70, "0"), ("$DIMALTTZ", 70, "0"), ("$DIMUPT", 70, "0"), ("$DIMDEC", 70, "2"), ("$DIMTDEC", 70, "2"), ("$DIMALTU", 70, "2"), ("$DIMALTTD", 70, "3"), ("$DIMTXSTY", 7, "Standard"), ("$DIMAUNIT", 70, "0"), ("$DIMADEC", 70, "0"), ("$DIMALTRND", 40, "0.0"), ("$DIMAZIN", 70, "0"), ("$DIMDSEP", 70, "46"), ("$DIMATFIT", 70, "3"), ("$DIMFRAC", 70, "0"), ("$DIMLDRBLK", 1, ""), ("$DIMLUNIT", 70, "2"), ("$DIMLWD", 70, "-2"), ("$DIMLWE", 70, "-2"), ("$DIMTMOVE", 70, "0") })
            HeaderVar(dv.Item1, dv.Item2, _doc.Header.TryGetValue(dv.Item1, out var hv) && dv.Item1 != "$DIMSTYLE" && dv.Item1 != "$DIMTXSTY" && dv.Item1 != "$DIMBLK" && dv.Item1 != "$DIMBLK1" && dv.Item1 != "$DIMBLK2" && dv.Item1 != "$DIMLDRBLK" ? hv : dv.Item3);
        HeaderVar("$LUNITS", 70, _doc.HeaderString("$LUNITS", "2")); HeaderVar("$LUPREC", 70, _doc.HeaderString("$LUPREC", "4")); HeaderVar("$SKETCHINC", 40, "1.0"); HeaderVar("$FILLETRAD", 40, "0.0"); HeaderVar("$AUNITS", 70, _doc.HeaderString("$AUNITS", "0")); HeaderVar("$AUPREC", 70, _doc.HeaderString("$AUPREC", "0"));
        HeaderVar("$MENU", 1, "."); HeaderVar("$ELEVATION", 40, "0.0"); HeaderVar("$PELEVATION", 40, "0.0"); HeaderVar("$THICKNESS", 40, "0.0"); HeaderVar("$LIMCHECK", 70, "0"); HeaderVar("$CHAMFERA", 40, "0.0"); HeaderVar("$CHAMFERB", 40, "0.0"); HeaderVar("$CHAMFERC", 40, "0.0"); HeaderVar("$CHAMFERD", 40, "0.0"); HeaderVar("$SKPOLY", 70, "0");
        HeaderVar("$TDCREATE", 40, "2460000.5"); HeaderVar("$TDUCREATE", 40, "2460000.5"); HeaderVar("$TDUPDATE", 40, "2460000.5"); HeaderVar("$TDUUPDATE", 40, "2460000.5"); HeaderVar("$TDINDWG", 40, "0.0"); HeaderVar("$TDUSRTIMER", 40, "0.0"); HeaderVar("$USRTIMER", 70, "1"); HeaderVar("$ANGBASE", 50, "0.0"); HeaderVar("$ANGDIR", 70, "0");
        HeaderVar("$PDMODE", 70, _doc.HeaderString("$PDMODE", "0")); HeaderVar("$PDSIZE", 40, _doc.HeaderString("$PDSIZE", "0.0")); HeaderVar("$PLINEWID", 40, "0.0"); HeaderVar("$SPLFRAME", 70, "0"); HeaderVar("$SPLINETYPE", 70, "6"); HeaderVar("$SPLINESEGS", 70, "8"); HeaderVar("$HANDSEED", 5, (_doc.HandSeed + 1).ToString("X"));
        HeaderVar("$SURFTAB1", 70, "6"); HeaderVar("$SURFTAB2", 70, "6"); HeaderVar("$SURFTYPE", 70, "6"); HeaderVar("$SURFU", 70, "6"); HeaderVar("$SURFV", 70, "6"); HeaderVar("$UCSBASE", 2, ""); HeaderVar("$UCSNAME", 2, ""); HeaderPoint("$UCSORG", new V3(0, 0, 0)); HeaderPoint("$UCSXDIR", new V3(1, 0, 0)); HeaderPoint("$UCSYDIR", new V3(0, 1, 0));
        HeaderVar("$UCSORTHOREF", 2, ""); HeaderVar("$UCSORTHOVIEW", 70, "0"); HeaderPoint("$UCSORGTOP", default); HeaderPoint("$UCSORGBOTTOM", default); HeaderPoint("$UCSORGLEFT", default); HeaderPoint("$UCSORGRIGHT", default); HeaderPoint("$UCSORGFRONT", default); HeaderPoint("$UCSORGBACK", default);
        HeaderVar("$PUCSBASE", 2, ""); HeaderVar("$PUCSNAME", 2, ""); HeaderPoint("$PUCSORG", default); HeaderPoint("$PUCSXDIR", new V3(1, 0, 0)); HeaderPoint("$PUCSYDIR", new V3(0, 1, 0)); HeaderVar("$PUCSORTHOREF", 2, ""); HeaderVar("$PUCSORTHOVIEW", 70, "0"); HeaderPoint("$PUCSORGTOP", default); HeaderPoint("$PUCSORGBOTTOM", default); HeaderPoint("$PUCSORGLEFT", default); HeaderPoint("$PUCSORGRIGHT", default); HeaderPoint("$PUCSORGFRONT", default); HeaderPoint("$PUCSORGBACK", default);
        HeaderVar("$USERI1", 70, "0"); HeaderVar("$USERI2", 70, "0"); HeaderVar("$USERI3", 70, "0"); HeaderVar("$USERI4", 70, "0"); HeaderVar("$USERI5", 70, "0"); HeaderVar("$USERR1", 40, "0.0"); HeaderVar("$USERR2", 40, "0.0"); HeaderVar("$USERR3", 40, "0.0"); HeaderVar("$USERR4", 40, "0.0"); HeaderVar("$USERR5", 40, "0.0");
        HeaderVar("$WORLDVIEW", 70, "1"); HeaderVar("$SHADEDGE", 70, "3"); HeaderVar("$SHADEDIF", 70, "70"); HeaderVar("$TILEMODE", 70, "1"); HeaderVar("$MAXACTVP", 70, "64"); HeaderPoint("$PINSBASE", default); HeaderVar("$PLIMCHECK", 70, "0"); HeaderPoint("$PEXTMIN", new V3(1e20, 1e20, 1e20)); HeaderPoint("$PEXTMAX", new V3(-1e20, -1e20, -1e20)); HeaderPoint("$PLIMMIN", default, false); HeaderPoint("$PLIMMAX", new V3(420, 297), false);
        HeaderVar("$UNITMODE", 70, "0"); HeaderVar("$VISRETAIN", 70, "1"); HeaderVar("$PLINEGEN", 70, "0"); HeaderVar("$PSLTSCALE", 70, "1"); HeaderVar("$TREEDEPTH", 70, "3020"); HeaderVar("$CMLSTYLE", 2, "Standard"); HeaderVar("$CMLJUST", 70, "0"); HeaderVar("$CMLSCALE", 40, "1.0"); HeaderVar("$PROXYGRAPHICS", 70, "1"); HeaderVar("$MEASUREMENT", 70, _doc.HeaderString("$MEASUREMENT", "1"));
        HeaderVar("$CELWEIGHT", 370, "-1"); HeaderVar("$ENDCAPS", 280, "0"); HeaderVar("$JOINSTYLE", 280, "0"); HeaderVar("$LWDISPLAY", 290, "0"); HeaderVar("$INSUNITS", 70, _doc.HeaderString("$INSUNITS", "4")); HeaderVar("$HYPERLINKBASE", 1, ""); HeaderVar("$STYLESHEET", 1, ""); HeaderVar("$XEDIT", 290, "1"); HeaderVar("$CEPSNTYPE", 380, "0"); HeaderVar("$PSTYLEMODE", 290, "1"); HeaderVar("$FINGERPRINTGUID", 2, "{00000000-0000-0000-0000-000000000000}"); HeaderVar("$VERSIONGUID", 2, "{00000000-0000-0000-0000-000000000001}"); HeaderVar("$EXTNAMES", 290, "1"); HeaderVar("$PSVPSCALE", 40, "0.0"); HeaderVar("$OLESTARTUP", 290, "0");
        W(0, "ENDSEC");
        var headerBytes = _ms.ToArray();
        SwapStream(new MemoryStream());
        _ms.Write(headerBytes); _ms.Write(bodyBytes);
    }
    void SwapStream(MemoryStream s) { _ms = s; }
    void HeaderVar(string name, int code, string value) { W(9, name); W(code, value); }
    void HeaderPoint(string name, V3 p, bool z = true) { W(9, name); P(10, p, z); }

    (V3 min, V3 max) Extents()
    {
        if (_doc.Header.TryGetValue("$EXTMIN", out var a) && _doc.Header.TryGetValue("$EXTMAX", out var b))
        {
            var pa = a.Split(' '); var pb = b.Split(' ');
            if (pa.Length >= 2 && pb.Length >= 2) { var mn = new V3(DxfCodec.ParseDouble(pa[0]), DxfCodec.ParseDouble(pa[1])); var mx = new V3(DxfCodec.ParseDouble(pb[0]), DxfCodec.ParseDouble(pb[1])); if (mn.X < mx.X && mn.Y < mx.Y && Math.Abs(mn.X) < 1e15) return (mn, mx); }
        }
        var bb = Geometry.Bounds.Of(_doc); return bb == null ? (new V3(0, 0), new V3(100, 100)) : (new V3(bb.Value.x0, bb.Value.y0), new V3(bb.Value.x1, bb.Value.y1));
    }

    void WriteLayout(string h, string owner, string name, int tab, string blockRecord)
    {
        W(0, "LAYOUT"); W(5, h); W(330, owner); W(100, "AcDbPlotSettings"); W(1, ""); W(2, "none_device"); W(4, ""); W(6, ""); W(40, 0.0); W(41, 0.0); W(42, 0.0); W(43, 0.0); W(44, 0.0); W(45, 0.0); W(46, 0.0); W(47, 0.0); W(48, 0.0); W(49, 0.0); W(140, 0.0); W(141, 0.0); W(142, 1.0); W(143, 1.0); W(70, 1712); W(72, 0); W(73, 0); W(74, 0); W(7, ""); W(75, 0); W(147, 1.0); W(148, 0.0); W(149, 0.0);
        W(100, "AcDbLayout"); W(1, name); W(70, 1); W(71, tab); W(10, 0.0); W(20, 0.0); W(11, 12.0); W(21, 9.0); W(12, 0.0); W(22, 0.0); W(32, 0.0); W(14, 0.0); W(24, 0.0); W(34, 0.0); W(15, 0.0); W(25, 0.0); W(35, 0.0); W(146, 0.0); W(13, 0.0); W(23, 0.0); W(33, 0.0); W(16, 1.0); W(26, 0.0); W(36, 0.0); W(17, 0.0); W(27, 1.0); W(37, 0.0); W(76, 0); W(330, blockRecord);
    }

    // ------------------------------------------------------------------ tables
    void WriteLayer(Layer l, string tableHandle)
    {
        if (l.Raw != null && !l.Dirty && _mode == Mode.Preserve) { Raw(l.Raw); return; }
        if (string.IsNullOrEmpty(l.Handle)) l.Handle = _doc.NewHandle();
        W(0, "LAYER"); W(5, l.Handle); W(330, tableHandle); W(100, "AcDbSymbolTableRecord"); W(100, "AcDbLayerTableRecord"); W(2, l.Name); W(70, l.Flags); W(62, l.Off ? -Math.Max(1, l.Color) : l.Color);
        if (l.TrueColor != null && _year >= 2004) W(420, l.TrueColor.Value);
        W(6, l.Linetype); if (!l.Plot) W(290, 0); W(370, l.Lineweight);
    }
    void WriteLtype(LineType l, string tableHandle)
    {
        if (string.IsNullOrEmpty(l.Handle)) l.Handle = _doc.NewHandle();
        W(0, "LTYPE"); W(5, l.Handle); W(330, tableHandle); W(100, "AcDbSymbolTableRecord"); W(100, "AcDbLinetypeTableRecord"); W(2, l.Name); W(70, 0); W(3, l.Description); W(72, 65); W(73, l.Pattern.Count); W(40, l.Pattern.Count > 0 ? l.Pattern.Sum(Math.Abs) : 0.0);
        foreach (var p in l.Pattern) { W(49, p); W(74, 0); }
    }
    void WriteStyle(TextStyle s, string tableHandle)
    {
        if (string.IsNullOrEmpty(s.Handle)) s.Handle = _doc.NewHandle();
        W(0, "STYLE"); W(5, s.Handle); W(330, tableHandle); W(100, "AcDbSymbolTableRecord"); W(100, "AcDbTextStyleTableRecord"); W(2, s.Name); W(70, s.Flags); W(40, s.Height); W(41, s.WidthFactor); W(50, s.Oblique); W(71, s.GenFlags); W(42, 2.5); W(3, s.Font); W(4, s.BigFont);
        if (s.TrueTypeFamily != null) { W(1001, "ACAD"); W(1000, s.TrueTypeFamily); W(1071, s.FontFlags != 0 ? s.FontFlags : 34); }   // TrueType family (u01 'PDF Arial' form)
    }
    /// <summary>Style as written: the font-compat replacement when one is planned for it.</summary>
    void EmitStyle(TextStyle s, string tableHandle, bool preserveRaw)
    {
        if (_fontFixes != null && _fontFixes.TryGetValue(s.Name, out var f)) { WriteStyle(FontCompat.Applied(s, f), tableHandle); return; }
        if (preserveRaw && s.Raw != null) Raw(s.Raw); else WriteStyle(s, tableHandle);
    }
    void WriteBlockRecord(Block b, string tableHandle, string? layout)
    {
        if (b.RecordRaw != null && _mode == Mode.Preserve) { Raw(b.RecordRaw); return; }
        if (string.IsNullOrEmpty(b.RecordHandle) || b.RecordHandle.Trim() == "0") b.RecordHandle = _doc.NewHandle();
        W(0, "BLOCK_RECORD"); W(5, b.RecordHandle); W(330, tableHandle); W(100, "AcDbSymbolTableRecord"); W(100, "AcDbBlockTableRecord"); W(2, b.Name); if (layout != null) W(340, layout);
    }

    // ------------------------------------------------------------------ blocks
    void WriteBlock(Block b)
    {
        if (b.Raw != null) Raw(b.Raw); else WriteBlockBegin(b);
        foreach (var e in b.Entities) WriteEntity(e, e.Owner != "" ? e.Owner : b.RecordHandle);
        if (b.EndRaw != null) Raw(b.EndRaw); else WriteBlockEnd(b);
    }
    void WriteBlockClean(Block b)
    {
        WriteBlockBegin(b);
        foreach (var e in b.Entities) WriteEntity(e, b.RecordHandle);
        WriteBlockEnd(b);
    }
    void WriteBlockBegin(Block b)
    {
        if (string.IsNullOrEmpty(b.Handle)) b.Handle = _doc.NewHandle();
        W(0, "BLOCK"); W(5, b.Handle); W(330, b.RecordHandle); W(100, "AcDbEntity"); if (b.Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase)) W(67, 1); W(8, b.Layer is { Length: > 0 } ? b.Layer : "0"); W(100, "AcDbBlockBegin"); W(2, b.Name); W(70, b.Flags); P(10, b.Base); W(3, b.Name); W(1, b.XrefPath);
    }
    void WriteBlockEnd(Block b)
    {
        if (string.IsNullOrEmpty(b.EndHandle)) b.EndHandle = _doc.NewHandle();
        W(0, "ENDBLK"); W(5, b.EndHandle); W(330, b.RecordHandle); W(100, "AcDbEntity"); if (b.Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase)) W(67, 1); W(8, b.Layer is { Length: > 0 } ? b.Layer : "0"); W(100, "AcDbBlockEnd");
    }

    // ------------------------------------------------------------------ entities
    void WriteEntity(Entity e, string owner)
    {
        if (_mode == Mode.R2000 && !R2000Loss.R2000Entities.Contains(e.DxfName)) return;   // counted by R2000Loss.Detect
        if (_mode == Mode.R2000 && e is Insert ins0 && (ins0.Block.Length == 0 || !_doc.Blocks.ContainsKey(ins0.Block))) return;   // unresolved block reference — counted by R2000Loss.Detect
        if (_mode == Mode.Preserve && e.Raw != null && !e.Dirty)
        {
            Raw(e.Raw);
            switch (e)
            {
                case Polyline pl: if (pl.VertexRaw != null) foreach (var v in pl.VertexRaw) Raw(v); if (pl.SeqendRaw != null) Raw(pl.SeqendRaw); break;
                case Insert ins: foreach (var a in ins.Attribs) if (a.Raw != null) Raw(a.Raw); else WriteText(a.Text, ins.Handle); if (ins.SeqendRaw != null) Raw(ins.SeqendRaw); else if (ins.Attribs.Count > 0) WriteSeqend(ins.SeqendHandle, ins.Handle, ins.Layer); break;
                case Unknown u: if (u.Children != null) foreach (var c in u.Children) Raw(c); if (u.SeqendRaw != null) Raw(u.SeqendRaw); break;
            }
            return;
        }
        if (e is Unknown un)
        {
            if (un.Raw == null) return;
            Raw(un.Raw); if (un.Children != null) foreach (var c in un.Children) Raw(c); if (un.SeqendRaw != null) Raw(un.SeqendRaw);
            return;
        }
        if (string.IsNullOrEmpty(e.Handle)) e.Handle = _doc.NewHandle();
        Common(e, owner);
        switch (e)
        {
            case Line l: W(100, "AcDbLine"); if (l.Thickness != 0) W(39, l.Thickness); P(10, l.P0); P(11, l.P1); break;
            case Circle c: W(100, "AcDbCircle"); if (c.Thickness != 0) W(39, c.Thickness); P(10, c.Center); W(40, c.Radius); break;
            case Arc a: W(100, "AcDbCircle"); if (a.Thickness != 0) W(39, a.Thickness); P(10, a.Center); W(40, a.Radius); W(100, "AcDbArc"); W(50, a.StartDeg); W(51, a.EndDeg); break;
            case Ellipse el: W(100, "AcDbEllipse"); P(10, el.Center); P(11, el.Major); W(40, el.Ratio); W(41, el.StartParam); W(42, el.EndParam); break;
            case LwPolyline p:
                W(100, "AcDbPolyline"); W(90, p.Vertices.Count); W(70, (p.Closed ? 1 : 0) | (p.Plinegen ? 128 : 0)); if (p.ConstWidth != 0) W(43, p.ConstWidth); if (p.Elevation != 0) W(38, p.Elevation);
                foreach (var v in p.Vertices) { W(10, v.X); W(20, v.Y); if (v.StartWidth != p.ConstWidth || v.EndWidth != p.ConstWidth) { W(40, v.StartWidth); W(41, v.EndWidth); } if (v.Bulge != 0) W(42, v.Bulge); }
                break;
            case Polyline pl:
                {
                    W(100, pl.Is3D ? "AcDb3dPolyline" : "AcDb2dPolyline"); W(66, 1); P(10, new V3(0, 0, pl.Elevation)); W(70, pl.Flags); if (pl.StartWidth != 0) W(40, pl.StartWidth); if (pl.EndWidth != 0) W(41, pl.EndWidth);
                    for (int i = 0; i < pl.Vertices.Count; i++)
                    {
                        var v = pl.Vertices[i]; string vh = pl.VertexRaw != null && i < pl.VertexRaw.Count ? pl.VertexRaw[i].FirstOrDefault(g => g.Code == 5).Value ?? "" : "";
                        if (string.IsNullOrEmpty(vh)) vh = _doc.NewHandle();
                        W(0, "VERTEX"); W(5, vh); W(330, pl.Handle); W(100, "AcDbEntity"); W(8, pl.Layer); W(100, "AcDbVertex"); W(100, pl.Is3D ? "AcDb3dPolylineVertex" : "AcDb2dVertex"); W(10, v.X); W(20, v.Y); W(30, v.Z);
                        if (v.StartWidth != 0) W(40, v.StartWidth); if (v.EndWidth != 0) W(41, v.EndWidth); if (v.Bulge != 0) W(42, v.Bulge); W(70, pl.Is3D ? 32 : 0);
                    }
                    if (string.IsNullOrEmpty(pl.SeqendHandle)) pl.SeqendHandle = _doc.NewHandle();
                    WriteSeqend(pl.SeqendHandle, pl.Handle, pl.Layer);
                    break;
                }
            case Text t: WriteTextBody(t); break;
            case MText m:
                {
                    W(100, "AcDbMText"); P(10, m.Pos); W(40, m.Height); W(41, m.Width); W(71, m.Attach); W(72, m.DirFlag);
                    string v = m.Value;
                    // LibreDWG 0.14 dxf2dwg stores MTEXT with text_height 0 (verified with its own DXF: docs/libredwg.md); an inline absolute height code keeps the size readable in CAD programs
                    if (_mode == Mode.R2000 && m.Height > 0 && !v.StartsWith("\\H", StringComparison.OrdinalIgnoreCase) && !v.StartsWith("{\\H", StringComparison.OrdinalIgnoreCase)) v = "\\H" + DxfCodec.FormatDouble(m.Height) + ";" + v;
                    int at = 0; while (v.Length - at > 250) { W(3, v.Substring(at, 250)); at += 250; } W(1, v[at..]);
                    W(7, m.Style); P(11, m.HasXAxis ? m.XAxis : new V3(Math.Cos(m.Rotation * Math.PI / 180), Math.Sin(m.Rotation * Math.PI / 180), 0)); W(73, m.LineSpacingStyle); W(44, m.LineSpacing);
                    break;
                }
            case Insert ins:
                {
                    W(100, "AcDbBlockReference"); if (ins.Attribs.Count > 0) W(66, 1); W(2, ins.Block); P(10, ins.Pos); W(41, ins.SX); W(42, ins.SY); W(43, ins.SZ); W(50, ins.Rotation);
                    if (ins.Cols != 1 || ins.Rows != 1) { W(70, ins.Cols); W(71, ins.Rows); W(44, ins.ColSpacing); W(45, ins.RowSpacing); }
                    Xdata(e);
                    foreach (var a in ins.Attribs) WriteText(a.Text, ins.Handle);
                    if (ins.Attribs.Count > 0) { if (string.IsNullOrEmpty(ins.SeqendHandle)) ins.SeqendHandle = _doc.NewHandle(); WriteSeqend(ins.SeqendHandle, ins.Handle, ins.Layer); }
                    return;
                }
            case Hatch h:
                {
                    W(100, "AcDbHatch"); P(10, new V3(0, 0, h.Elevation)); P(210, h.Normal); W(2, h.Pattern); W(70, h.Solid ? 1 : 0); W(71, _mode == Mode.R2000 ? 0 : h.Associative ? 1 : 0); W(91, h.Loops.Count);
                    foreach (var loop in h.Loops)
                    {
                        if (_mode == Mode.R2000 && HatchAsPolylineLoops && !(loop.IsPolyline && loop.Edges.Count > 0 && loop.Edges[0].Type == 0))
                        {
                            // edge loops (line/arc/ellipse/spline edges) → one tessellated polyline loop: dxf2dwg 0.14 writes edge loops without boundary paths and ODA/nanoCAD then drop the hatch
                            var pts = Geometry.Tess.HatchLoop(loop); int n = pts.Count / 2;
                            if (n >= 3 && Math.Abs(pts[0] - pts[^2]) < 1e-9 && Math.Abs(pts[1] - pts[^1]) < 1e-9) n--;   // closing point repeated
                            W(92, (loop.Type & ~2 & ~4) | 2 | (loop.Type & 1)); W(72, 0); W(73, 1); W(93, Math.Max(n, 0));
                            for (int i = 0; i < n; i++) { W(10, pts[i * 2]); W(20, pts[i * 2 + 1]); }
                            W(97, 0);
                            continue;
                        }
                        W(92, loop.Type);
                        if (loop.IsPolyline && loop.Edges.Count > 0 && loop.Edges[0].Type == 0)
                        {
                            var ed = loop.Edges[0]; W(72, ed.PolyHasBulge ? 1 : 0); W(73, ed.PolyClosed ? 1 : 0); W(93, ed.Poly.Count);
                            foreach (var v in ed.Poly) { W(10, v.X); W(20, v.Y); if (ed.PolyHasBulge) W(42, v.Bulge); }
                        }
                        else
                        {
                            W(93, loop.Edges.Count);
                            foreach (var ed in loop.Edges)
                            {
                                W(72, ed.Type);
                                switch (ed.Type)
                                {
                                    case 1: W(10, ed.P0.X); W(20, ed.P0.Y); W(11, ed.P1.X); W(21, ed.P1.Y); break;
                                    case 2: W(10, ed.Center.X); W(20, ed.Center.Y); W(40, ed.Radius); W(50, ed.StartDeg); W(51, ed.EndDeg); W(73, ed.Ccw ? 1 : 0); break;
                                    case 3: W(10, ed.Center.X); W(20, ed.Center.Y); W(11, ed.Major.X); W(21, ed.Major.Y); W(40, ed.Ratio); W(50, ed.StartDeg); W(51, ed.EndDeg); W(73, ed.Ccw ? 1 : 0); break;
                                    default: foreach (var g in ed.Raw) W(g.Code, g.Value); break;
                                }
                            }
                        }
                        if (_mode == Mode.R2000) W(97, 0);   // dxf2dwg 0.14: "Unknown DXF code 330 for HATCH" → corrupt object (ODA "Object improperly read") — no source boundary handles in R2000 output
                        else { W(97, loop.SourceHandles.Count); foreach (var sh in loop.SourceHandles) W(330, sh); }
                    }
                    W(75, h.Style); W(76, h.PatternType); if (!h.Solid) { W(52, h.PatternAngle); W(41, h.PatternScale); W(77, h.DoubleFlag ? 1 : 0); }
                    bool tailHasSeed = h.Tail.Any(g => g.Code == 98);
                    Raw(h.Tail);
                    if (!tailHasSeed) { W(98, 1); W(10, 0.0); W(20, 0.0); }
                    break;
                }
            case Dimension d:
                {
                    W(100, "AcDbDimension"); if (d.Block != "") W(2, d.Block); P(10, d.DefPoint); P(11, d.TextMid); W(70, d.Type); W(71, d.Attach); W(72, d.LineSpacingStyle == 0 ? 1 : d.LineSpacingStyle); W(41, d.LineSpacing); if (d.HasMeasurement) W(42, d.Measurement);
                    if (d.Text != "") W(1, d.Text); if (d.TextRotation != 0) W(53, d.TextRotation); if (d.HorizDir != 0) W(51, d.HorizDir); W(3, d.Style);
                    switch (d.Kind)
                    {
                        case 0: W(100, "AcDbAlignedDimension"); if (d.Insert != default) P(12, d.Insert); P(13, d.P13); P(14, d.P14); W(50, d.Rotation); if (d.ObliqueAngle != 0) W(52, d.ObliqueAngle); W(100, "AcDbRotatedDimension"); break;
                        case 1: W(100, "AcDbAlignedDimension"); if (d.Insert != default) P(12, d.Insert); P(13, d.P13); P(14, d.P14); if (d.ObliqueAngle != 0) W(52, d.ObliqueAngle); break;
                        case 2: W(100, "AcDb2LineAngularDimension"); P(13, d.P13); P(14, d.P14); P(15, d.P15); P(16, d.P16); break;
                        case 3: W(100, "AcDbDiametricDimension"); P(15, d.P15); W(40, d.LeaderLen); break;
                        case 4: W(100, "AcDbRadialDimension"); P(15, d.P15); W(40, d.LeaderLen); break;
                        case 5: W(100, "AcDb3PointAngularDimension"); P(13, d.P13); P(14, d.P14); P(15, d.P15); break;
                        case 6: W(100, "AcDbOrdinateDimension"); P(13, d.P13); P(14, d.P14); break;
                    }
                    break;
                }
            case Solid s: W(100, s.Name == "3DFACE" ? "AcDbFace" : "AcDbTrace"); P(10, s.P0); P(11, s.P1); P(12, s.P2); P(13, s.P3); if (s.Thickness != 0) W(39, s.Thickness); break;
            case Point pt: W(100, "AcDbPoint"); P(10, pt.Pos); if (pt.Angle != 0) W(50, pt.Angle); break;
            case Spline sp:
                W(100, "AcDbSpline"); W(70, sp.Flags); W(71, sp.Degree); W(72, sp.Knots.Count); W(73, sp.Control.Count); W(74, sp.Fit.Count); W(42, sp.KnotTol); W(43, sp.CtrlTol); W(44, sp.FitTol);
                if (sp.HasTangents) { P(12, sp.StartTangent); P(13, sp.EndTangent); }
                foreach (var k in sp.Knots) W(40, k); foreach (var wgt in sp.Weights) W(41, wgt); foreach (var c in sp.Control) P(10, c); foreach (var f in sp.Fit) P(11, f);
                break;
        }
        Xdata(e);
    }

    void Common(Entity e, string owner)
    {
        W(0, e.DxfName); W(5, e.Handle);
        foreach (var g in e.Extra) if (g.Code == 102 || _in102(g)) W(g.Code, g.Value);
        if (!string.IsNullOrEmpty(owner)) W(330, owner);
        W(100, "AcDbEntity"); if (e.PaperSpace) W(67, 1); W(8, e.Layer);
        if (!e.Linetype.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) && e.Linetype != "") W(6, e.Linetype);
        if (e.Color != 256) W(62, e.Color);
        if (e.TrueColor != null && _year >= 2004) W(420, e.TrueColor.Value);
        if (e.Lineweight != -1) W(370, e.Lineweight);
        if (e.LtScale != 1) W(48, e.LtScale);
        if (e.Invisible) W(60, 1);
    }
    bool _in102Flag;
    bool _in102(Group g) { if (g.Code == 102) { _in102Flag = !g.Value.StartsWith("}"); return true; } return _in102Flag; }
    void Xdata(Entity e) { bool skip = false; foreach (var g in e.Extra) { if (g.Code == 102) { skip = !g.Value.StartsWith("}"); continue; } if (skip) continue; if (g.Code >= 1000) W(g.Code, g.Value); } }

    void WriteText(Text t, string owner) { if (string.IsNullOrEmpty(t.Handle)) t.Handle = _doc.NewHandle(); Common(t, owner); WriteTextBody(t); Xdata(t); }
    void WriteTextBody(Text t)
    {
        W(100, "AcDbText"); if (t.Thickness != 0) W(39, t.Thickness); P(10, t.Pos); W(40, t.Height); W(1, t.Value); if (t.Rotation != 0) W(50, t.Rotation); if (t.WidthFactor != 1) W(41, t.WidthFactor); if (t.Oblique != 0) W(51, t.Oblique); W(7, t.Style); if (t.GenFlags != 0) W(71, t.GenFlags); if (t.HAlign != 0) W(72, t.HAlign);
        if (t.HAlign != 0 || t.VAlign != 0) P(11, t.Align);
        if (t.IsAttrib) { W(100, "AcDbAttribute"); W(2, t.AttribTag); W(70, t.AttribFlags); if (t.VAlign != 0) W(74, t.VAlign); }
        else if (t.IsAttdef) { W(100, "AcDbAttributeDefinition"); W(3, t.Prompt); W(2, t.AttribTag); W(70, t.AttribFlags); if (t.VAlign != 0) W(74, t.VAlign); }
        else { W(100, "AcDbText"); if (t.VAlign != 0) W(73, t.VAlign); }
    }
    void WriteSeqend(string handle, string owner, string layer) { W(0, "SEQEND"); W(5, handle); W(330, owner); W(100, "AcDbEntity"); W(8, layer); }
}
