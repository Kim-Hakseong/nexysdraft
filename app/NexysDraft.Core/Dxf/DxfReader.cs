using System.Globalization;
using System.Text;
using NexysDraft.Core.Model;

namespace NexysDraft.Core.Dxf;

/// <summary>
/// ASCII DXF reader (R12 … 2018). Entities the model knows become typed objects (raw groups kept for a verbatim rewrite); everything
/// else — header, classes, the other tables, objects, unknown entities — is kept as raw group lists so a DXF→DXF save loses nothing.
/// </summary>
public sealed class DxfReader
{
    readonly List<Group> _g = new();
    readonly Document _doc = new();
    int _i;
    Encoding _cp = DxfCodec.CodePageEncoding("");   // also runs the codec static ctor (code page provider registration)
    int _utf8Values, _cpValues;

    public static Document Read(string path)
    {
        var r = new DxfReader();
        r._doc.Path = path;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        r.Tokenize(File.ReadAllBytes(path)); r._doc.TokenizeMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
        r.Parse(); r._doc.ParseMs = sw.Elapsed.TotalMilliseconds;
        return r._doc;
    }
    public static Document ReadBytes(byte[] bytes, string path = "")
    {
        var r = new DxfReader(); r._doc.Path = path; r.Tokenize(bytes); r.Parse(); return r._doc;
    }

    // ------------------------------------------------------------------ tokenizer
    void Tokenize(byte[] b)
    {
        if (b.Length > 22 && Encoding.ASCII.GetString(b, 0, 18) == "AutoCAD Binary DXF") throw new NotSupportedException("binary DXF is not supported (convert with dwg2dxf)");
        // code page first: scan the header text for $DWGCODEPAGE / $ACADVER
        string head = Encoding.ASCII.GetString(b, 0, Math.Min(b.Length, 20000));
        int k = head.IndexOf("$DWGCODEPAGE", StringComparison.Ordinal);
        if (k >= 0) { var lines = head[(k + 12)..].Split('\n'); if (lines.Length > 2) _doc.CodePage = lines[2].Trim(); }
        int v = head.IndexOf("$ACADVER", StringComparison.Ordinal);
        if (v >= 0) { var lines = head[(v + 8)..].Split('\n'); if (lines.Length > 2) _doc.Version = lines[2].Trim(); }
        _cp = DxfCodec.CodePageEncoding(_doc.CodePage);
        if (_doc.VersionYear >= 2007 && k < 0) _cp = new UTF8Encoding(false);
        int pos = 0, n = b.Length; int lineNo = 0; int code = 0; bool haveCode = false;
        _g.Capacity = Math.Max(16, n / 12);
        while (pos < n)
        {
            int end = Array.IndexOf(b, (byte)'\n', pos); if (end < 0) end = n;
            int e2 = end; if (e2 > pos && b[e2 - 1] == '\r') e2--;
            if (!haveCode)
            {
                // group code: integer, may be padded with spaces
                int c = 0, p = pos; bool neg = false, any = false;
                while (p < e2 && b[p] == ' ') p++;
                if (p < e2 && b[p] == '-') { neg = true; p++; }
                while (p < e2 && b[p] >= '0' && b[p] <= '9') { c = c * 10 + (b[p] - '0'); p++; any = true; }
                if (!any) { if (lineNo == 0 && e2 - pos > 0) throw new FormatException("not a DXF file"); pos = end + 1; lineNo++; continue; }
                code = neg ? -c : c; haveCode = true;
            }
            else
            {
                string val;
                if (IsStringCode(code)) { val = DxfCodec.Uncaret(DxfCodec.Unescape(DxfCodec.Decode(new ReadOnlySpan<byte>(b, pos, e2 - pos), _cp, out bool u8))); if (u8) _utf8Values++; else if (val.Any(ch => ch >= 0x80)) _cpValues++; }
                else { int a0 = pos, a1 = e2; while (a0 < a1 && b[a0] == ' ') a0++; while (a1 > a0 && b[a1 - 1] == ' ') a1--; val = a1 > a0 ? Encoding.ASCII.GetString(b, a0, a1 - a0) : ""; }
                if (code != 999) _g.Add(new Group(code, val));
                haveCode = false;
            }
            pos = end + 1; lineNo++;
        }
        _doc.SourceUtf8 = _utf8Values > 0 && _utf8Values >= _cpValues;
    }
    static bool IsStringCode(int c) => c is (>= 0 and <= 9) or 100 or 101 or 102 or 105 or (>= 300 and <= 369) or (>= 390 and <= 399) or (>= 410 and <= 419) or (>= 430 and <= 439) or (>= 470 and <= 481) or 999 or (>= 1000 and <= 1009);

    // ------------------------------------------------------------------ sections
    void Parse()
    {
        _i = 0;
        while (_i < _g.Count)
        {
            var g = _g[_i];
            if (g.Code == 0 && g.Value == "EOF") break;
            if (g.Code == 0 && g.Value == "SECTION")
            {
                string name = _i + 1 < _g.Count && _g[_i + 1].Code == 2 ? _g[_i + 1].Value : "";
                _i += 2;
                switch (name.ToUpperInvariant())
                {
                    case "HEADER": ParseHeader(); break;
                    case "CLASSES": _doc.ClassesRaw = TakeUntilEndsec(); break;
                    case "TABLES": ParseTables(); break;
                    case "BLOCKS": ParseBlocks(); break;
                    case "ENTITIES": ParseEntities(); break;
                    case "OBJECTS": _doc.ObjectsRaw = TakeUntilEndsec(); break;
                    default: _doc.OtherSections.Add((name, TakeUntilEndsec())); break;
                }
                continue;
            }
            _i++;
        }
        foreach (var e in _doc.Entities) Count(e);
        // handseed from header, else max handle + 1
        long seed = DxfCodec.ParseHandle(_doc.HeaderString("$HANDSEED", "0"));
        long max = 0;
        foreach (var e in AllEntities()) max = Math.Max(max, DxfCodec.ParseHandle(e.Handle));
        foreach (var l in _doc.Layers) max = Math.Max(max, DxfCodec.ParseHandle(l.Handle));
        foreach (var b in _doc.Blocks.Values) { max = Math.Max(max, DxfCodec.ParseHandle(b.Handle)); max = Math.Max(max, DxfCodec.ParseHandle(b.EndHandle)); max = Math.Max(max, DxfCodec.ParseHandle(b.RecordHandle)); }
        foreach (var g in _g) if (g.Code == 5 || g.Code == 105) max = Math.Max(max, DxfCodec.ParseHandle(g.Value));   // every handle in the file (tables, vertices, attribs, objects)
        _doc.HandSeed = Math.Max(seed, max + 1);
    }
    IEnumerable<Entity> AllEntities() { foreach (var e in _doc.Entities) yield return e; foreach (var b in _doc.Blocks.Values) foreach (var e in b.Entities) yield return e; }
    void Count(Entity e) { string n = e.DxfName; _doc.TypeCounts[n] = _doc.TypeCounts.GetValueOrDefault(n) + 1; }

    List<Group> TakeUntilEndsec()
    {
        var list = new List<Group>();
        while (_i < _g.Count) { var g = _g[_i++]; if (g.Code == 0 && g.Value == "ENDSEC") break; list.Add(g); }
        return list;
    }

    void ParseHeader()
    {
        _doc.HeaderRaw = TakeUntilEndsec();
        for (int j = 0; j < _doc.HeaderRaw.Count; j++)
        {
            var g = _doc.HeaderRaw[j]; if (g.Code != 9) continue;
            // value = next group(s): a point stores "x y z", scalars the single value
            if (j + 1 < _doc.HeaderRaw.Count && _doc.HeaderRaw[j + 1].Code != 9)
            {
                var v = _doc.HeaderRaw[j + 1];
                if (v.Code == 10 && j + 2 < _doc.HeaderRaw.Count && _doc.HeaderRaw[j + 2].Code == 20) _doc.Header[g.Value] = v.Value + " " + _doc.HeaderRaw[j + 2].Value + (j + 3 < _doc.HeaderRaw.Count && _doc.HeaderRaw[j + 3].Code == 30 ? " " + _doc.HeaderRaw[j + 3].Value : "");
                else _doc.Header[g.Value] = v.Value;
            }
        }
        if (_doc.Header.TryGetValue("$ACADVER", out var ver)) _doc.Version = ver.Trim();
        if (_doc.Header.TryGetValue("$DWGCODEPAGE", out var cp)) _doc.CodePage = cp.Trim();
    }

    // ------------------------------------------------------------------ tables
    void ParseTables()
    {
        while (_i < _g.Count)
        {
            var g = _g[_i];
            if (g.Code == 0 && g.Value == "ENDSEC") { _i++; return; }
            if (g.Code == 0 && g.Value == "TABLE")
            {
                _i++;
                var head = new List<Group>(); string name = "";
                while (_i < _g.Count && _g[_i].Code != 0) { if (_g[_i].Code == 2 && name == "") name = _g[_i].Value; head.Add(_g[_i]); _i++; }
                var entries = new List<List<Group>>();
                while (_i < _g.Count && !(_g[_i].Code == 0 && _g[_i].Value == "ENDTAB")) entries.Add(TakeRecord());
                if (_i < _g.Count) _i++;   // ENDTAB
                switch (name.ToUpperInvariant())
                {
                    case "LAYER": foreach (var e in entries) _doc.Layers.Add(ParseLayer(e)); _doc.RawTables.Add(new RawTable { Name = name, Head = head }); break;
                    case "LTYPE": foreach (var e in entries) _doc.Linetypes.Add(ParseLtype(e)); _doc.RawTables.Add(new RawTable { Name = name, Head = head }); break;
                    case "STYLE": foreach (var e in entries) _doc.Styles.Add(ParseStyle(e)); _doc.RawTables.Add(new RawTable { Name = name, Head = head }); break;
                    case "BLOCK_RECORD": _doc.RawTables.Add(new RawTable { Name = name, Head = head, Entries = entries }); break;
                    default: _doc.RawTables.Add(new RawTable { Name = name, Head = head, Entries = entries }); break;
                }
                continue;
            }
            _i++;
        }
    }

    /// <summary>Groups of one record: from the current code 0 up to (not including) the next code 0.</summary>
    List<Group> TakeRecord()
    {
        var r = new List<Group> { _g[_i++] };
        while (_i < _g.Count && _g[_i].Code != 0) r.Add(_g[_i++]);
        return r;
    }

    static Layer ParseLayer(List<Group> r)
    {
        var l = new Layer { Raw = r };
        foreach (var g in r)
            switch (g.Code)
            {
                case 2: l.Name = g.Value; break;
                case 5: l.Handle = g.Value; break;
                case 62: { int c = DxfCodec.ParseInt(g.Value); l.Off = c < 0; l.Color = Math.Abs(c); break; }
                case 420: l.TrueColor = DxfCodec.ParseInt(g.Value); break;
                case 6: l.Linetype = g.Value; break;
                case 70: l.Flags = DxfCodec.ParseInt(g.Value); break;
                case 370: l.Lineweight = DxfCodec.ParseInt(g.Value); break;
                case 290: l.Plot = DxfCodec.ParseInt(g.Value) != 0; break;
            }
        return l;
    }
    static LineType ParseLtype(List<Group> r)
    {
        var l = new LineType { Raw = r };
        foreach (var g in r)
            switch (g.Code) { case 2: l.Name = g.Value; break; case 3: l.Description = g.Value; break; case 5: l.Handle = g.Value; break; case 40: l.Length = DxfCodec.ParseDouble(g.Value); break; case 49: l.Pattern.Add(DxfCodec.ParseDouble(g.Value)); break; }
        return l;
    }
    static TextStyle ParseStyle(List<Group> r)
    {
        var s = new TextStyle { Raw = r };
        foreach (var g in r)
            switch (g.Code)
            {
                case 2: s.Name = g.Value; break; case 3: s.Font = g.Value; break; case 4: s.BigFont = g.Value; break; case 5: s.Handle = g.Value; break;
                case 40: s.Height = DxfCodec.ParseDouble(g.Value); break; case 41: s.WidthFactor = DxfCodec.ParseDouble(g.Value); break; case 50: s.Oblique = DxfCodec.ParseDouble(g.Value); break;
                case 70: s.Flags = DxfCodec.ParseInt(g.Value); break; case 71: s.GenFlags = DxfCodec.ParseInt(g.Value); break;
                case 1000: if (s.TrueTypeFamily == null && g.Value.Length > 0 && !g.Value.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) && !g.Value.StartsWith("Annotative", StringComparison.OrdinalIgnoreCase)) s.TrueTypeFamily = g.Value; break;   // ACAD xdata: TrueType family name
                case 1071: if (s.FontFlags == 0) s.FontFlags = DxfCodec.ParseInt(g.Value); break;
            }
        return s;
    }

    // ------------------------------------------------------------------ blocks
    void ParseBlocks()
    {
        Block? cur = null;
        while (_i < _g.Count)
        {
            var g = _g[_i];
            if (g.Code == 0 && g.Value == "ENDSEC") { _i++; return; }
            if (g.Code == 0 && g.Value == "BLOCK")
            {
                var r = TakeRecord();
                cur = new Block { Raw = r };
                for (int j = 0; j < r.Count; j++)
                {
                    var x = r[j];
                    switch (x.Code)
                    {
                        case 2: if (cur.Name == "") cur.Name = x.Value; break;
                        case 5: cur.Handle = x.Value; break; case 8: cur.Layer = x.Value; break; case 70: cur.Flags = DxfCodec.ParseInt(x.Value); break; case 1: cur.XrefPath = x.Value; break; case 330: cur.RecordHandle = x.Value.Trim() == "0" ? "" : x.Value; break;
                        case 10: cur.Base = new V3(DxfCodec.ParseDouble(x.Value), cur.Base.Y, cur.Base.Z); break; case 20: cur.Base = cur.Base with { Y = DxfCodec.ParseDouble(x.Value) }; break; case 30: cur.Base = cur.Base with { Z = DxfCodec.ParseDouble(x.Value) }; break;
                    }
                }
                string key = cur.Name.Length == 0 ? "*NONAME~1" : cur.Name; int n = 1; while (_doc.Blocks.ContainsKey(key)) key = (cur.Name.Length == 0 ? "*NONAME" : cur.Name) + "~" + n++;   // nameless blocks (lost names in damaged DWGs) must not match INSERTs with an empty name — u04 drew a 2,255-line block 8.6× in model space
                _doc.Blocks[key] = cur; _doc.BlockOrder.Add(key);
                continue;
            }
            if (g.Code == 0 && g.Value == "ENDBLK")
            {
                var r = TakeRecord();
                if (cur != null) { cur.EndRaw = r; foreach (var x in r) if (x.Code == 5) cur.EndHandle = x.Value; }
                continue;
            }
            if (g.Code == 0) { var e = ParseEntity(); if (e != null && cur != null) cur.Entities.Add(e); continue; }
            _i++;
        }
    }

    void ParseEntities()
    {
        while (_i < _g.Count)
        {
            var g = _g[_i];
            if (g.Code == 0 && g.Value == "ENDSEC") { _i++; return; }
            if (g.Code == 0) { var e = ParseEntity(); if (e != null) _doc.Entities.Add(e); continue; }
            _i++;
        }
    }

    // ------------------------------------------------------------------ entities
    static V3 Pt(List<Group> r, int code, V3 d = default)
    {
        double x = d.X, y = d.Y, z = d.Z;
        for (int j = 0; j < r.Count; j++) { int c = r[j].Code; if (c == code) x = DxfCodec.ParseDouble(r[j].Value); else if (c == code + 10) y = DxfCodec.ParseDouble(r[j].Value); else if (c == code + 20) z = DxfCodec.ParseDouble(r[j].Value); }
        return new V3(x, y, z);
    }
    static double D(List<Group> r, int code, double d = 0) { foreach (var g in r) if (g.Code == code) return DxfCodec.ParseDouble(g.Value); return d; }
    static int I(List<Group> r, int code, int d = 0) { foreach (var g in r) if (g.Code == code) return DxfCodec.ParseInt(g.Value); return d; }
    static string S(List<Group> r, int code, string d = "") { foreach (var g in r) if (g.Code == code) return g.Value; return d; }
    static bool Has(List<Group> r, int code) { foreach (var g in r) if (g.Code == code) return true; return false; }

    Entity? ParseEntity()
    {
        var r = TakeRecord(); string name = r[0].Value.ToUpperInvariant();
        Entity e;
        switch (name)
        {
            case "LINE":
                {
                    double x0 = 0, y0 = 0, z0 = 0, x1 = 0, y1 = 0, z1 = 0, nx = 0, ny = 0, nz = 1, th = 0;
                    foreach (var g in r)
                        switch (g.Code) { case 10: x0 = DxfCodec.ParseDouble(g.Value); break; case 20: y0 = DxfCodec.ParseDouble(g.Value); break; case 30: z0 = DxfCodec.ParseDouble(g.Value); break; case 11: x1 = DxfCodec.ParseDouble(g.Value); break; case 21: y1 = DxfCodec.ParseDouble(g.Value); break; case 31: z1 = DxfCodec.ParseDouble(g.Value); break; case 210: nx = DxfCodec.ParseDouble(g.Value); break; case 220: ny = DxfCodec.ParseDouble(g.Value); break; case 230: nz = DxfCodec.ParseDouble(g.Value); break; case 39: th = DxfCodec.ParseDouble(g.Value); break; }
                    e = new Line { P0 = new V3(x0, y0, z0), P1 = new V3(x1, y1, z1), Normal = new V3(nx, ny, nz), Thickness = th }; break;
                }
            case "CIRCLE": { var c = new Circle { Center = Pt(r, 10), Radius = D(r, 40), Normal = Pt(r, 210, new V3(0, 0, 1)), Thickness = D(r, 39) }; if (c.Normal.Z < 0) c.Center = c.Center with { X = -c.Center.X }; e = c; break; }
            case "ARC":
                {
                    var a = new Arc { Center = Pt(r, 10), Radius = D(r, 40), StartDeg = D(r, 50), EndDeg = D(r, 51, 360), Thickness = D(r, 39) }; var nrm = Pt(r, 210, new V3(0, 0, 1));
                    if (nrm.Z < 0) { a.Center = a.Center with { X = -a.Center.X }; (a.StartDeg, a.EndDeg) = (Arc.Norm(180 - a.EndDeg), Arc.Norm(180 - a.StartDeg)); }
                    e = a; break;
                }
            case "ELLIPSE": e = new Ellipse { Center = Pt(r, 10), Major = Pt(r, 11), Ratio = D(r, 40, 1), StartParam = D(r, 41), EndParam = D(r, 42, Math.PI * 2) }; break;
            case "LWPOLYLINE":
                {
                    var p = new LwPolyline { Closed = (I(r, 70) & 1) != 0, Plinegen = (I(r, 70) & 128) != 0, Elevation = D(r, 38), Normal = Pt(r, 210, new V3(0, 0, 1)) };
                    if (Has(r, 43)) p.ConstWidth = D(r, 43);
                    PVertex v = default; bool open = false;
                    foreach (var g in r)
                    {
                        switch (g.Code)
                        {
                            case 10: if (open) p.Vertices.Add(v); v = new PVertex { X = DxfCodec.ParseDouble(g.Value), StartWidth = p.ConstWidth, EndWidth = p.ConstWidth }; open = true; break;
                            case 20: v.Y = DxfCodec.ParseDouble(g.Value); break;
                            case 42: v.Bulge = DxfCodec.ParseDouble(g.Value); break;
                            case 40: v.StartWidth = DxfCodec.ParseDouble(g.Value); break;
                            case 41: v.EndWidth = DxfCodec.ParseDouble(g.Value); break;
                        }
                    }
                    if (open) p.Vertices.Add(v);
                    if (p.Normal.Z < 0) for (int j = 0; j < p.Vertices.Count; j++) { var vv = p.Vertices[j]; vv.X = -vv.X; vv.Bulge = -vv.Bulge; p.Vertices[j] = vv; }
                    e = p; break;
                }
            case "POLYLINE":
                {
                    var p = new Polyline { Flags = I(r, 70), StartWidth = D(r, 40), EndWidth = D(r, 41), Elevation = Pt(r, 10).Z, Normal = Pt(r, 210, new V3(0, 0, 1)) };
                    p.Closed = (p.Flags & 1) != 0; p.Is3D = (p.Flags & 8) != 0; p.VertexRaw = new();
                    while (_i < _g.Count && _g[_i].Code == 0 && _g[_i].Value.Equals("VERTEX", StringComparison.OrdinalIgnoreCase))
                    {
                        var vr = TakeRecord(); p.VertexRaw.Add(vr); int vf = I(vr, 70);
                        if ((vf & 16) != 0 || (vf & 8) != 0) continue;   // spline frame control point / mesh — skip (bezier/spline-fit polylines keep their fit vertices)
                        var pv = Pt(vr, 10); p.Vertices.Add(new PVertex { X = pv.X, Y = pv.Y, Z = pv.Z, Bulge = D(vr, 42), StartWidth = D(vr, 40, p.StartWidth), EndWidth = D(vr, 41, p.EndWidth) });
                    }
                    if (_i < _g.Count && _g[_i].Code == 0 && _g[_i].Value.Equals("SEQEND", StringComparison.OrdinalIgnoreCase)) { p.SeqendRaw = TakeRecord(); p.SeqendHandle = S(p.SeqendRaw, 5); }
                    if (p.Normal.Z < 0 && !p.Is3D) for (int j = 0; j < p.Vertices.Count; j++) { var vv = p.Vertices[j]; vv.X = -vv.X; vv.Bulge = -vv.Bulge; p.Vertices[j] = vv; }
                    e = p; break;
                }
            case "TEXT": e = ParseText(r); break;
            case "ATTDEF": case "ATTRIB": { var t = ParseText(r); t.IsAttrib = name == "ATTRIB"; t.IsAttdef = name == "ATTDEF"; t.AttribTag = S(r, 2); t.AttribFlags = I(r, 70); t.Prompt = S(r, 3); e = t; break; }
            case "MTEXT":
                {
                    var m = new MText { Pos = Pt(r, 10), Height = D(r, 40), Width = D(r, 41), Attach = I(r, 71, 1), DirFlag = I(r, 72, 1), Style = S(r, 7, "Standard"), LineSpacingStyle = I(r, 73, 2), LineSpacing = D(r, 44, 1), Normal = Pt(r, 210, new V3(0, 0, 1)) };
                    var sb = new StringBuilder(); foreach (var g in r) if (g.Code == 3) sb.Append(g.Value); foreach (var g in r) if (g.Code == 1) sb.Append(g.Value);
                    m.Value = sb.ToString();
                    if (Has(r, 11)) { m.XAxis = Pt(r, 11); m.HasXAxis = true; m.Rotation = m.RotationDeg; } else m.Rotation = D(r, 50);
                    e = m; break;
                }
            case "INSERT":
                {
                    var ins = new Insert { Block = S(r, 2), Pos = Pt(r, 10), SX = D(r, 41, 1), SY = D(r, 42, 1), SZ = D(r, 43, 1), Rotation = D(r, 50), Cols = I(r, 70, 1), Rows = I(r, 71, 1), ColSpacing = D(r, 44), RowSpacing = D(r, 45), Normal = Pt(r, 210, new V3(0, 0, 1)) };
                    if (I(r, 66) == 1)
                    {
                        while (_i < _g.Count && _g[_i].Code == 0 && _g[_i].Value.Equals("ATTRIB", StringComparison.OrdinalIgnoreCase))
                        {
                            var ar = TakeRecord(); var t = ParseText(ar); t.Raw = ar; t.IsAttrib = true; t.AttribTag = S(ar, 2); t.AttribFlags = I(ar, 70); t.Handle = S(ar, 5); t.Layer = S(ar, 8, "0"); t.Color = I(ar, 62, 256);
                            ins.Attribs.Add(new Attrib { Tag = t.AttribTag, Value = t.Value, Flags = t.AttribFlags, Text = t, Raw = ar, Handle = t.Handle });
                        }
                        if (_i < _g.Count && _g[_i].Code == 0 && _g[_i].Value.Equals("SEQEND", StringComparison.OrdinalIgnoreCase)) { ins.SeqendRaw = TakeRecord(); ins.SeqendHandle = S(ins.SeqendRaw, 5); }
                    }
                    if (ins.Normal.Z < 0) { ins.Pos = ins.Pos with { X = -ins.Pos.X }; ins.SX = -ins.SX; ins.Normal = new V3(0, 0, 1); }
                    e = ins; break;
                }
            case "HATCH": e = ParseHatch(r); break;
            case "DIMENSION":
                {
                    var d = new Dimension { Block = S(r, 2), DefPoint = Pt(r, 10), TextMid = Pt(r, 11), P13 = Pt(r, 13), P14 = Pt(r, 14), P15 = Pt(r, 15), P16 = Pt(r, 16), Insert = Pt(r, 12), Type = I(r, 70), Attach = I(r, 71, 5), Text = S(r, 1), Style = S(r, 3, "Standard"), Rotation = D(r, 50), HorizDir = D(r, 51), TextRotation = D(r, 53), LeaderLen = D(r, 40), ObliqueAngle = D(r, 52), LineSpacingStyle = I(r, 72), LineSpacing = D(r, 41, 1), Normal = Pt(r, 210, new V3(0, 0, 1)) };
                    if (Has(r, 42)) { d.Measurement = D(r, 42); d.HasMeasurement = true; }
                    e = d; break;
                }
            case "SOLID": case "TRACE": case "3DFACE": e = new Solid { Name = name, P0 = Pt(r, 10), P1 = Pt(r, 11), P2 = Pt(r, 12), P3 = Has(r, 13) ? Pt(r, 13) : Pt(r, 12), Thickness = D(r, 39), Normal = Pt(r, 210, new V3(0, 0, 1)) }; break;
            case "POINT": e = new Point { Pos = Pt(r, 10), Angle = D(r, 50) }; break;
            case "SPLINE":
                {
                    var s = new Spline { Degree = I(r, 71, 3), Flags = I(r, 70), Normal = Pt(r, 210, new V3(0, 0, 1)), KnotTol = D(r, 42, 1e-7), CtrlTol = D(r, 43, 1e-7), FitTol = D(r, 44, 1e-9) };
                    V3 c = default, f = default; bool co = false, fo = false;
                    foreach (var g in r)
                    {
                        switch (g.Code)
                        {
                            case 40: s.Knots.Add(DxfCodec.ParseDouble(g.Value)); break;
                            case 41: s.Weights.Add(DxfCodec.ParseDouble(g.Value)); break;
                            case 10: if (co) s.Control.Add(c); c = new V3(DxfCodec.ParseDouble(g.Value), 0, 0); co = true; break;
                            case 20: c = c with { Y = DxfCodec.ParseDouble(g.Value) }; break;
                            case 30: c = c with { Z = DxfCodec.ParseDouble(g.Value) }; break;
                            case 11: if (fo) s.Fit.Add(f); f = new V3(DxfCodec.ParseDouble(g.Value), 0, 0); fo = true; break;
                            case 21: f = f with { Y = DxfCodec.ParseDouble(g.Value) }; break;
                            case 31: f = f with { Z = DxfCodec.ParseDouble(g.Value) }; break;
                        }
                    }
                    if (co) s.Control.Add(c); if (fo) s.Fit.Add(f);
                    if (Has(r, 12)) { s.StartTangent = Pt(r, 12); s.EndTangent = Pt(r, 13); s.HasTangents = true; }
                    e = s; break;
                }
            default:
                {
                    var u = new Unknown { Name = name };
                    // entities with children (POLYLINE-like sequences of other kinds): VERTEX … SEQEND after e.g. POLYFACE handled above; keep any trailing SEQEND
                    if (I(r, 66) == 1 || name == "POLYLINE")
                    {
                        u.Children = new();
                        while (_i < _g.Count && _g[_i].Code == 0 && _g[_i].Value is "VERTEX" or "ATTRIB") u.Children.Add(TakeRecord());
                        if (_i < _g.Count && _g[_i].Code == 0 && _g[_i].Value == "SEQEND") u.SeqendRaw = TakeRecord();
                    }
                    e = u; break;
                }
        }
        e.Raw = r;
        foreach (var g in r)
        {
            switch (g.Code)
            {
                case 5: e.Handle = g.Value; break; case 8: e.Layer = g.Value; break; case 6: e.Linetype = g.Value; break; case 62: e.Color = DxfCodec.ParseInt(g.Value); break; case 420: e.TrueColor = DxfCodec.ParseInt(g.Value); break;
                case 48: e.LtScale = DxfCodec.ParseDouble(g.Value); break; case 370: e.Lineweight = DxfCodec.ParseInt(g.Value); break; case 67: e.PaperSpace = DxfCodec.ParseInt(g.Value) == 1; break; case 60: e.Invisible = DxfCodec.ParseInt(g.Value) == 1; break; case 330: if (e.Owner == "") e.Owner = g.Value; break;
            }
        }
        // extra groups the model writer must carry: 102 blocks, xdata, proxy graphics, reactors
        bool in102 = false;
        foreach (var g in r)
        {
            if (g.Code == 102) { in102 = !g.Value.StartsWith("}"); e.Extra.Add(g); continue; }
            if (in102 || g.Code >= 1000) e.Extra.Add(g);
        }
        return e;
    }

    static Text ParseText(List<Group> r)
    {
        var t = new Text { Pos = Pt(r, 10), Align = Has(r, 11) ? Pt(r, 11) : Pt(r, 10), Height = D(r, 40), Rotation = D(r, 50), WidthFactor = D(r, 41, 1), Oblique = D(r, 51), Style = S(r, 7, "Standard"), HAlign = I(r, 72), VAlign = I(r, 73, I(r, 74)), GenFlags = I(r, 71), Value = DxfCodec.Unescape(S(r, 1)), Normal = Pt(r, 210, new V3(0, 0, 1)), Thickness = D(r, 39) };
        if (t.Normal.Z < 0) { t.Pos = t.Pos with { X = -t.Pos.X }; t.Align = t.Align with { X = -t.Align.X }; t.Rotation = Arc.Norm(180 - t.Rotation); t.Normal = new V3(0, 0, 1); }
        return t;
    }

    static Hatch ParseHatch(List<Group> r)
    {
        var h = new Hatch { Pattern = S(r, 2, "SOLID"), Solid = I(r, 70) == 1, Associative = I(r, 71) == 1, Style = I(r, 75), PatternType = I(r, 76), PatternAngle = D(r, 52), PatternScale = D(r, 41, 1), DoubleFlag = I(r, 77) == 1, Elevation = Pt(r, 10).Z, Normal = Pt(r, 210, new V3(0, 0, 1)) };
        int j = 0;
        // skip to the loop count (91)
        while (j < r.Count && r[j].Code != 91) j++;
        if (j >= r.Count) return h;
        int loops = DxfCodec.ParseInt(r[j].Value); j++;
        for (int L = 0; L < loops && j < r.Count; L++)
        {
            if (r[j].Code != 92) break;
            var loop = new HatchLoop { Type = DxfCodec.ParseInt(r[j].Value) }; j++;
            if (loop.IsPolyline)
            {
                var edge = new HatchEdge { Type = 0 };
                edge.PolyHasBulge = I2(r, ref j, 72) == 1; edge.PolyClosed = I2(r, ref j, 73) == 1; int nv = I2(r, ref j, 93);
                for (int k = 0; k < nv && j < r.Count; k++)
                {
                    var v = new PVertex();
                    if (r[j].Code == 10) { v.X = DxfCodec.ParseDouble(r[j].Value); j++; }
                    if (j < r.Count && r[j].Code == 20) { v.Y = DxfCodec.ParseDouble(r[j].Value); j++; }
                    if (j < r.Count && r[j].Code == 42) { v.Bulge = DxfCodec.ParseDouble(r[j].Value); j++; }
                    edge.Poly.Add(v);
                }
                loop.Edges.Add(edge);
            }
            else
            {
                int ne = I2(r, ref j, 93);
                for (int k = 0; k < ne && j < r.Count; k++)
                {
                    if (r[j].Code != 72) break;
                    var edge = new HatchEdge { Type = DxfCodec.ParseInt(r[j].Value) }; j++;
                    switch (edge.Type)
                    {
                        case 1: edge.P0 = new V3(D2(r, ref j, 10), D2(r, ref j, 20)); edge.P1 = new V3(D2(r, ref j, 11), D2(r, ref j, 21)); break;
                        case 2: edge.Center = new V3(D2(r, ref j, 10), D2(r, ref j, 20)); edge.Radius = D2(r, ref j, 40); edge.StartDeg = D2(r, ref j, 50); edge.EndDeg = D2(r, ref j, 51); edge.Ccw = I2(r, ref j, 73) == 1; break;
                        case 3: edge.Center = new V3(D2(r, ref j, 10), D2(r, ref j, 20)); edge.Major = new V3(D2(r, ref j, 11), D2(r, ref j, 21)); edge.Ratio = D2(r, ref j, 40); edge.StartDeg = D2(r, ref j, 50); edge.EndDeg = D2(r, ref j, 51); edge.Ccw = I2(r, ref j, 73) == 1; break;
                        case 4:
                            {
                                // spline edge: degree 94, rational 73, periodic 74, knots 95, control 96, then 40×knots, (10,20[,42])×control, 97 fit, 11/21 fit, 12/22 13/23 tangents
                                int deg = I2(r, ref j, 94); edge.Raw.Add(new Group(94, deg.ToString()));
                                int rat = I2(r, ref j, 73); edge.Raw.Add(new Group(73, rat.ToString()));
                                int per = I2(r, ref j, 74); edge.Raw.Add(new Group(74, per.ToString()));
                                int nk = I2(r, ref j, 95); edge.Raw.Add(new Group(95, nk.ToString()));
                                int nc = I2(r, ref j, 96); edge.Raw.Add(new Group(96, nc.ToString()));
                                for (int q = 0; q < nk && j < r.Count && r[j].Code == 40; q++) edge.Raw.Add(r[j++]);
                                for (int q = 0; q < nc && j < r.Count && r[j].Code == 10; q++) { edge.Raw.Add(r[j++]); if (j < r.Count && r[j].Code == 20) edge.Raw.Add(r[j++]); if (j < r.Count && r[j].Code == 42) edge.Raw.Add(r[j++]); }
                                if (j < r.Count && r[j].Code == 97) { int nf = DxfCodec.ParseInt(r[j].Value); edge.Raw.Add(r[j++]); for (int q = 0; q < nf && j < r.Count && r[j].Code == 11; q++) { edge.Raw.Add(r[j++]); if (j < r.Count && r[j].Code == 21) edge.Raw.Add(r[j++]); } }
                                while (j < r.Count && r[j].Code is 12 or 22 or 13 or 23) edge.Raw.Add(r[j++]);
                                break;
                            }
                    }
                    loop.Edges.Add(edge);
                }
            }
            if (j < r.Count && r[j].Code == 97) { int ns = DxfCodec.ParseInt(r[j].Value); j++; for (int k = 0; k < ns && j < r.Count && r[j].Code == 330; k++) loop.SourceHandles.Add(r[j++].Value); }
            else while (j < r.Count && r[j].Code == 330) loop.SourceHandles.Add(r[j++].Value);
            h.Loops.Add(loop);   // draft1 bug: loops were parsed but never attached — every hatch read from a file lost its boundary (nanoCAD "해치 미표시", ODA HATCH N/0)
        }
        // everything after the loops (pattern lines, seed points, gradient) verbatim; also the codes that precede 91 but are not modelled
        h.Tail = r.Skip(j).Where(g => g.Code is not (75 or 76 or 77 or 52 or 41 or 102) && g.Code < 1000).ToList();
        return h;
    }
    static int I2(List<Group> r, ref int j, int code) { if (j < r.Count && r[j].Code == code) return DxfCodec.ParseInt(r[j++].Value); return 0; }
    static double D2(List<Group> r, ref int j, int code) { if (j < r.Count && r[j].Code == code) return DxfCodec.ParseDouble(r[j++].Value); return 0; }
}
