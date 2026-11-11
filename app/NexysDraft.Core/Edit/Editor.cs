using System.Globalization;
using NexysDraft.Core.Model;
using NexysDraft.Core.MTextFmt;

namespace NexysDraft.Core.Edit;

/// <summary>One undoable change: entities replaced (by handle), added, removed; layers changed.</summary>
public sealed class Change
{
    public string Name = "";
    public List<(int index, Entity before, Entity after)> Replaced = new();
    public List<(int index, Entity e)> Added = new();
    public List<(int index, Entity e)> Removed = new();
    public List<(Layer before, Layer after)> Layers = new();
    public List<Layer> LayersAdded = new();
    public List<(string name, Block block)> BlocksAdded = new();
    public IEnumerable<string> Touched => Replaced.Select(r => r.after.Handle).Concat(Added.Select(a => a.e.Handle)).Concat(Removed.Select(r => r.e.Handle));
}

/// <summary>
/// Editing commands on a <see cref="Document"/> with undo/redo. Every command records the entities it replaces (clone before), adds
/// and removes; undo re-inserts the clones at their old indices. Geometry is 2D (similarity transforms + per-vertex stretch).
/// </summary>
public sealed partial class Editor
{
    public Document Doc { get; }
    readonly LinkedList<Change> _undo = new(); readonly Stack<Change> _redo = new();
    public const int MaxDepth = 100;
    public event Action<Change>? Applied;
    public bool CanUndo => _undo.Count > 0; public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo.Last!.Value.Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo.Peek().Name : null;
    public Editor(Document doc) { Doc = doc; }

    // ------------------------------------------------------------------ core
    Change Begin(string name) => new() { Name = name };
    void Commit(Change c)
    {
        if (c.Replaced.Count == 0 && c.Added.Count == 0 && c.Removed.Count == 0 && c.Layers.Count == 0 && c.LayersAdded.Count == 0) return;
        _undo.AddLast(c); while (_undo.Count > MaxDepth) _undo.RemoveFirst(); _redo.Clear(); Doc.Dirty = true; Applied?.Invoke(c);
    }
    public Change? Undo()
    {
        if (_undo.Count == 0) return null; var c = _undo.Last!.Value; _undo.RemoveLast();
        foreach (var (_, e) in c.Added.OrderByDescending(a => a.index)) Doc.Entities.Remove(e);
        foreach (var (i, before, after) in c.Replaced) { int k = Doc.Entities.IndexOf(after); if (k >= 0) Doc.Entities[k] = before; else Doc.Entities.Insert(Math.Min(i, Doc.Entities.Count), before); }
        foreach (var (i, e) in c.Removed.OrderBy(r => r.index)) Doc.Entities.Insert(Math.Min(i, Doc.Entities.Count), e);
        foreach (var (before, after) in c.Layers) { int k = Doc.Layers.IndexOf(after); if (k >= 0) Doc.Layers[k] = before; }
        foreach (var l in c.LayersAdded) Doc.Layers.Remove(l);
        foreach (var (name, _) in c.BlocksAdded) { Doc.Blocks.Remove(name); Doc.BlockOrder.Remove(name); }
        _redo.Push(c); Doc.Dirty = true; Applied?.Invoke(Invert(c)); return c;
    }
    public Change? Redo()
    {
        if (_redo.Count == 0) return null; var c = _redo.Pop();
        foreach (var (i, e) in c.Removed.OrderByDescending(r => r.index)) Doc.Entities.Remove(e);
        foreach (var (i, before, after) in c.Replaced) { int k = Doc.Entities.IndexOf(before); if (k >= 0) Doc.Entities[k] = after; else Doc.Entities.Insert(Math.Min(i, Doc.Entities.Count), after); }
        foreach (var (i, e) in c.Added.OrderBy(a => a.index)) Doc.Entities.Insert(Math.Min(i, Doc.Entities.Count), e);
        foreach (var (before, after) in c.Layers) { int k = Doc.Layers.IndexOf(before); if (k >= 0) Doc.Layers[k] = after; }
        foreach (var l in c.LayersAdded) if (!Doc.Layers.Contains(l)) Doc.Layers.Add(l);
        foreach (var (name, b) in c.BlocksAdded) { Doc.Blocks[name] = b; if (!Doc.BlockOrder.Contains(name)) Doc.BlockOrder.Add(name); }
        _undo.AddLast(c); Doc.Dirty = true; Applied?.Invoke(c); return c;
    }
    static Change Invert(Change c)
    {
        var inv = new Change { Name = c.Name };
        inv.Replaced.AddRange(c.Replaced.Select(r => (r.index, r.after, r.before))); inv.Added.AddRange(c.Removed); inv.Removed.AddRange(c.Added);
        inv.Layers.AddRange(c.Layers.Select(l => (l.after, l.before)));
        return inv;
    }

    List<(int index, Entity e)> Resolve(IEnumerable<string> handles)
    {
        var set = new HashSet<string>(handles); var list = new List<(int, Entity)>();
        for (int i = 0; i < Doc.Entities.Count; i++) if (set.Contains(Doc.Entities[i].Handle)) list.Add((i, Doc.Entities[i]));
        return list;
    }

    /// <summary>Replace each selected entity by a transformed clone (undoable).</summary>
    Change TransformSel(string name, IEnumerable<string> handles, Xform t)
    {
        var c = Begin(name);
        foreach (var (i, e) in Resolve(handles))
        {
            var after = e.Clone(); after.Transform(t);
            if (after is Dimension d && d.Block != "" && Doc.Blocks.TryGetValue(d.Block, out var blk)) TransformDimBlock(d, blk, t, c);
            Doc.Entities[i] = after; c.Replaced.Add((i, e, after));
        }
        Commit(c); return c;
    }
    /// <summary>A dimension's anonymous block is private to it: transform its entities in place (undo restores the clones too).</summary>
    void TransformDimBlock(Dimension d, Block blk, Xform t, Change c)
    {
        var nb = new Block { Name = blk.Name, Base = blk.Base, Flags = blk.Flags, Handle = blk.Handle, EndHandle = blk.EndHandle, RecordHandle = blk.RecordHandle, Layer = blk.Layer, Raw = blk.Raw, EndRaw = blk.EndRaw, RecordRaw = blk.RecordRaw };
        foreach (var e in blk.Entities) { var ce = e.Clone(); ce.Transform(t); nb.Entities.Add(ce); }
        Doc.Blocks[blk.Name] = nb;
    }

    // ------------------------------------------------------------------ commands
    public Change Move(IEnumerable<string> handles, double dx, double dy) => TransformSel("Move", handles, Xform.Translate(dx, dy));
    public Change Rotate(IEnumerable<string> handles, double cx, double cy, double deg) => TransformSel("Rotate", handles, Xform.Rotate(cx, cy, deg));
    public Change Scale(IEnumerable<string> handles, double cx, double cy, double k) => TransformSel("Scale", handles, Xform.Scale(cx, cy, k));

    public Change Delete(IEnumerable<string> handles)
    {
        var c = Begin("Erase");
        foreach (var (i, e) in Resolve(handles).OrderByDescending(x => x.index)) { Doc.Entities.RemoveAt(i); c.Removed.Add((i, e)); }
        Commit(c); return c;
    }

    public Change Copy(IEnumerable<string> handles, double dx, double dy) => CopyTransformed("Copy", handles, Xform.Translate(dx, dy));
    public Change Mirror(IEnumerable<string> handles, double x0, double y0, double x1, double y1, bool keepOriginal)
        => keepOriginal ? CopyTransformed("Mirror", handles, Xform.Mirror(x0, y0, x1, y1)) : TransformSel("Mirror", handles, Xform.Mirror(x0, y0, x1, y1));
    Change CopyTransformed(string name, IEnumerable<string> handles, Xform t)
    {
        var c = Begin(name);
        foreach (var (_, e) in Resolve(handles))
        {
            var n = e.Clone(); n.Handle = Doc.NewHandle(); n.Raw = null; n.Dirty = true; n.Transform(t);
            if (n is Dimension d && d.Block != "" && Doc.Blocks.TryGetValue(d.Block, out var blk)) { var nb = CloneBlock(blk, NextAnonymous("*D")); foreach (var be in nb.Entities) be.Transform(t); d.Block = nb.Name; c.BlocksAdded.Add((nb.Name, nb)); }
            if (n is Insert ins) foreach (var a in ins.Attribs) a.Handle = a.Text.Handle = Doc.NewHandle();
            if (n is Polyline pl) { pl.VertexRaw = null; pl.SeqendHandle = ""; pl.SeqendRaw = null; }
            if (n is Insert ins2) { ins2.SeqendHandle = ""; ins2.SeqendRaw = null; }
            Doc.Entities.Add(n); c.Added.Add((Doc.Entities.Count - 1, n));
        }
        Commit(c); return c;
    }
    Block CloneBlock(Block b, string name)
    {
        var nb = new Block { Name = name, Base = b.Base, Flags = b.Flags | 1, Layer = b.Layer, Handle = Doc.NewHandle(), EndHandle = Doc.NewHandle(), RecordHandle = Doc.NewHandle() };
        foreach (var e in b.Entities) { var ce = e.Clone(); ce.Handle = Doc.NewHandle(); ce.Raw = null; ce.Dirty = true; ce.Owner = nb.RecordHandle; nb.Entities.Add(ce); }
        Doc.Blocks[name] = nb; Doc.BlockOrder.Add(name); return nb;
    }
    string NextAnonymous(string prefix) { int n = 1; while (Doc.Blocks.ContainsKey(prefix + n)) n++; return prefix + n; }

    /// <summary>STRETCH: grips inside the window move by (dx,dy); entities whose grips are all inside move as a whole.</summary>
    public Change Stretch(IEnumerable<string> handles, double wx0, double wy0, double wx1, double wy1, double dx, double dy)
    {
        var c = Begin("Stretch"); double ax = Math.Min(wx0, wx1), bx = Math.Max(wx0, wx1), ay = Math.Min(wy0, wy1), by = Math.Max(wy0, wy1);
        bool In(V3 p) => p.X >= ax && p.X <= bx && p.Y >= ay && p.Y <= by;
        foreach (var (i, e) in Resolve(handles))
        {
            var g = e.Grips; if (g.Count == 0) continue;
            int inside = g.Count(In); if (inside == 0) continue;
            var after = e.Clone(); after.Raw = null; after.Dirty = true;
            if (inside == g.Count || after is Circle or Arc or Text or MText or Insert or Ellipse or Point) { after.Transform(Xform.Translate(dx, dy)); if (after is Dimension dd && Doc.Blocks.TryGetValue(dd.Block, out var bb)) TransformDimBlock(dd, bb, Xform.Translate(dx, dy), c); }
            else
            {
                for (int k = 0; k < g.Count; k++) if (In(g[k])) after.SetGrip(k, new V3(g[k].X + dx, g[k].Y + dy, g[k].Z));
                if (after is Dimension d) RegenerateDimension(d, c);
            }
            Doc.Entities[i] = after; c.Replaced.Add((i, e, after));
        }
        Commit(c); return c;
    }

    /// <summary>OFFSET of a line, circle, arc or polyline by <paramref name="dist"/> towards <paramref name="side"/> (a point).</summary>
    public Change? Offset(string handle, double dist, V3 side)
    {
        var src = Doc.Find(handle); if (src == null) return null;
        Entity? n = null;
        switch (src)
        {
            case Line l:
                {
                    double dx = l.P1.X - l.P0.X, dy = l.P1.Y - l.P0.Y, len = Math.Sqrt(dx * dx + dy * dy); if (len < 1e-12) return null;
                    double nx = -dy / len, ny = dx / len; double s = (side.X - l.P0.X) * nx + (side.Y - l.P0.Y) * ny >= 0 ? 1 : -1;
                    var nl = (Line)l.Clone(); nl.P0 = new V3(l.P0.X + nx * dist * s, l.P0.Y + ny * dist * s, l.P0.Z); nl.P1 = new V3(l.P1.X + nx * dist * s, l.P1.Y + ny * dist * s, l.P1.Z); n = nl; break;
                }
            case Circle ci:
                {
                    double d = (side - ci.Center).Len2; double r = d >= ci.Radius ? ci.Radius + dist : ci.Radius - dist; if (r <= 0) return null;
                    var nc = (Circle)ci.Clone(); nc.Radius = r; n = nc; break;
                }
            case Arc ar:
                {
                    double d = (side - ar.Center).Len2; double r = d >= ar.Radius ? ar.Radius + dist : ar.Radius - dist; if (r <= 0) return null;
                    var na = (Arc)ar.Clone(); na.Radius = r; n = na; break;
                }
            case LwPolyline pl:
                {
                    var np = OffsetPolyline(pl, dist, side); if (np == null) return null; n = np; break;
                }
            default: return null;
        }
        n.Handle = Doc.NewHandle(); n.Raw = null; n.Dirty = true;
        var c = Begin("Offset"); Doc.Entities.Add(n); c.Added.Add((Doc.Entities.Count - 1, n)); Commit(c); return c;
    }

    static LwPolyline? OffsetPolyline(LwPolyline pl, double dist, V3 side)
    {
        int n = pl.Vertices.Count; if (n < 2) return null;
        // side: sign from the nearest segment's normal
        int best = 0; double bestD = double.MaxValue; double sgn = 1;
        for (int i = 0; i < (pl.Closed ? n : n - 1); i++)
        {
            var a = pl.Vertices[i]; var b = pl.Vertices[(i + 1) % n]; double dx = b.X - a.X, dy = b.Y - a.Y, l2 = dx * dx + dy * dy; if (l2 < 1e-18) continue;
            double t = Math.Clamp(((side.X - a.X) * dx + (side.Y - a.Y) * dy) / l2, 0, 1); double px = a.X + dx * t, py = a.Y + dy * t; double d = (side.X - px) * (side.X - px) + (side.Y - py) * (side.Y - py);
            if (d < bestD) { bestD = d; best = i; sgn = ((side.X - a.X) * -dy + (side.Y - a.Y) * dx) >= 0 ? 1 : -1; }
        }
        var res = (LwPolyline)pl.Clone(); res.Vertices = new List<PVertex>();
        // offset each segment line and intersect consecutive offset lines (bulges ignored for the vertex positions, kept as is)
        var segs = new List<(double x0, double y0, double x1, double y1)>();
        int segCount = pl.Closed ? n : n - 1;
        for (int i = 0; i < segCount; i++)
        {
            var a = pl.Vertices[i]; var b = pl.Vertices[(i + 1) % n]; double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy); if (l < 1e-12) { segs.Add((a.X, a.Y, b.X, b.Y)); continue; }
            double nx = -dy / l * dist * sgn, ny = dx / l * dist * sgn; segs.Add((a.X + nx, a.Y + ny, b.X + nx, b.Y + ny));
        }
        for (int i = 0; i < n; i++)
        {
            V3 p;
            if (!pl.Closed && i == 0) p = new V3(segs[0].x0, segs[0].y0);
            else if (!pl.Closed && i == n - 1) p = new V3(segs[^1].x1, segs[^1].y1);
            else
            {
                var s1 = segs[(i - 1 + segCount) % segCount]; var s2 = segs[i % segCount];
                var ip = Intersect(s1.x0, s1.y0, s1.x1, s1.y1, s2.x0, s2.y0, s2.x1, s2.y1); p = ip ?? new V3(s2.x0, s2.y0);
            }
            var v = pl.Vertices[i]; v.X = p.X; v.Y = p.Y; res.Vertices.Add(v);
        }
        return res;
    }
    public static V3? Intersect(double x1, double y1, double x2, double y2, double x3, double y3, double x4, double y4)
    {
        double d = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4); if (Math.Abs(d) < 1e-12) return null;
        double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / d; return new V3(x1 + t * (x2 - x1), y1 + t * (y2 - y1));
    }

    // ------------------------------------------------------------------ text, layers
    public Change? SetText(string handle, string value)
    {
        var e = Doc.Find(handle); if (e == null) return null; int i = Doc.Entities.IndexOf(e);
        var after = e.Clone(); after.Raw = null; after.Dirty = true;
        if (after is Text t) t.Value = value;
        else if (after is MText m) m.Value = value;
        else return null;
        var c = Begin("Text"); Doc.Entities[i] = after; c.Replaced.Add((i, e, after)); Commit(c); return c;
    }
    /// <summary>Edit MTEXT by plain text: keeps the leading format codes of the first run, replaces the content.</summary>
    public Change? SetMTextPlain(string handle, string plain)
    {
        var e = Doc.Find(handle) as MText; if (e == null) return null;
        string prefix = ""; var v = e.Value;
        // keep one leading {\f…; or \f…; prefix so the font survives
        int k = 0; if (v.StartsWith("{\\f") || v.StartsWith("{\\F") || v.StartsWith("\\f") || v.StartsWith("\\F")) { int semi = v.IndexOf(';'); if (semi > 0) { prefix = v[..(semi + 1)]; k = 1; } }
        string body = MTextParser.EscapePlain(plain);
        string value = k == 1 && prefix.StartsWith("{") ? prefix + body + "}" : prefix + body;
        return SetText(handle, value);
    }

    public Change SetLayer(IEnumerable<string> handles, string layer)
    {
        var c = Begin("Layer"); Doc.EnsureLayer(layer);
        foreach (var (i, e) in Resolve(handles)) { var after = e.Clone(); after.Raw = null; after.Dirty = true; after.Layer = layer; Doc.Entities[i] = after; c.Replaced.Add((i, e, after)); }
        Commit(c); return c;
    }
    public Change AddLayer(string name, int color = 7, string linetype = "Continuous")
    {
        var c = Begin("New layer");
        if (Doc.LayerOf(name) == null) { var l = new Layer { Name = name, Color = color, Linetype = linetype, Handle = Doc.NewHandle(), Dirty = true }; Doc.Layers.Add(l); c.LayersAdded.Add(l); }
        Commit(c); return c;
    }
    public Change? SetLayerState(string name, bool? off = null, bool? frozen = null, bool? locked = null, int? color = null, string? linetype = null)
    {
        var l = Doc.LayerOf(name); if (l == null) return null; int i = Doc.Layers.IndexOf(l);
        var after = new Layer { Name = l.Name, Color = color ?? l.Color, Off = off ?? l.Off, Linetype = linetype ?? l.Linetype, Flags = l.Flags, Lineweight = l.Lineweight, Plot = l.Plot, Handle = l.Handle, TrueColor = color != null ? null : l.TrueColor, Raw = l.Raw, Dirty = true };
        if (frozen != null) after.Frozen = frozen.Value; if (locked != null) after.Locked = locked.Value;
        var c = Begin("Layer state"); Doc.Layers[i] = after; c.Layers.Add((l, after)); Commit(c); return c;
    }

    // ------------------------------------------------------------------ drawing
    public Change Add(Entity e, string layer, string name = "Draw")
    {
        e.Handle = Doc.NewHandle(); e.Layer = layer; e.Dirty = true; e.Owner = Doc.ModelSpaceHandle;
        var c = Begin(name); Doc.Entities.Add(e); c.Added.Add((Doc.Entities.Count - 1, e)); Commit(c); return c;
    }
    public Change AddLine(double x0, double y0, double x1, double y1, string layer) => Add(new Line { P0 = new V3(x0, y0), P1 = new V3(x1, y1) }, layer, "Line");
    public Change AddCircle(double cx, double cy, double r, string layer) => Add(new Circle { Center = new V3(cx, cy), Radius = r }, layer, "Circle");
    // draft5e basic shapes (RECTANG / POLYGON go through AddPolyline)
    public Change AddArc(double cx, double cy, double r, double startDeg, double endDeg, string layer) => Add(new Arc { Center = new V3(cx, cy), Radius = r, StartDeg = Arc.Norm(startDeg), EndDeg = Arc.Norm(endDeg) }, layer, "Arc");
    /// <summary>Full ellipse: major axis end point relative to the centre, minor/major ratio (0 &lt; ratio ≤ 1).</summary>
    public Change AddEllipse(double cx, double cy, double mx, double my, double ratio, string layer) => Add(new Ellipse { Center = new V3(cx, cy), Major = new V3(mx, my), Ratio = Math.Clamp(ratio, 1e-6, 1) }, layer, "Ellipse");
    public Change AddPoint(double x, double y, string layer) => Add(new Point { Pos = new V3(x, y) }, layer, "Point");
    /// <summary>Text style for new text: $TEXTSTYLE when it exists, else Standard, else the first style.</summary>
    public string CurrentTextStyle() { string s = Doc.HeaderString("$TEXTSTYLE", "Standard"); if (Doc.Styles.Any(t => t.Name.Equals(s, StringComparison.OrdinalIgnoreCase))) return s; return Doc.Styles.FirstOrDefault(t => t.Name.Equals("Standard", StringComparison.OrdinalIgnoreCase))?.Name ?? Doc.Styles.FirstOrDefault()?.Name ?? "Standard"; }
    public Change AddText(double x, double y, double height, double rotationDeg, string value, string layer) => Add(new Text { Pos = new V3(x, y), Align = new V3(x, y), Height = height, Rotation = Arc.Norm(rotationDeg), Value = value, Style = CurrentTextStyle() }, layer, "Text");
    /// <summary>MTEXT, top-left attachment at (x, y); value uses \P for line breaks.</summary>
    public Change AddMText(double x, double y, double height, double width, string value, string layer) => Add(new MText { Pos = new V3(x, y), Height = height, Width = width, Attach = 1, Value = value, Style = CurrentTextStyle() }, layer, "MText");
    public Change AddPolyline(IEnumerable<(double x, double y)> pts, bool closed, string layer)
    {
        var p = new LwPolyline { Closed = closed }; foreach (var (x, y) in pts) p.Vertices.Add(new PVertex { X = x, Y = y });
        return Add(p, layer, "Polyline");
    }

    /// <summary>Linear (rotated) dimension between p1 and p2 with the dimension line through <paramref name="dimLine"/>; angle 0 = horizontal, 90 = vertical.</summary>
    public Change AddLinearDimension(V3 p1, V3 p2, V3 dimLine, double angleDeg, string layer, string? text = null)
    {
        var d = new Dimension { Text = text ?? "", Type = 0 | 32, P13 = p1, P14 = p2, Rotation = angleDeg, Style = Doc.HeaderString("$DIMSTYLE", "Standard"), Attach = 5, LineSpacingStyle = 1 };
        var blk = new Block { Name = NextAnonymous("*D"), Flags = 1, Handle = Doc.NewHandle(), EndHandle = Doc.NewHandle(), RecordHandle = Doc.NewHandle() };
        BuildLinearDim(d, blk, dimLine);
        d.Block = blk.Name; Doc.Blocks[blk.Name] = blk; Doc.BlockOrder.Add(blk.Name);
        d.Handle = Doc.NewHandle(); d.Layer = layer; d.Dirty = true; d.Owner = Doc.ModelSpaceHandle;
        var c = Begin("Dimension"); c.BlocksAdded.Add((blk.Name, blk)); Doc.Entities.Add(d); c.Added.Add((Doc.Entities.Count - 1, d)); Commit(c); return c;
    }

    /// <summary>Dimension block geometry (ISO-25 style defaults scaled by $DIMSCALE): extension lines, dimension line, filled arrowheads, measurement text.</summary>
    public void BuildLinearDim(Dimension d, Block blk, V3 dimLine)
    {
        double sc = Doc.HeaderDouble("$DIMSCALE", 1); if (sc <= 0) sc = 1;
        double asz = Doc.HeaderDouble("$DIMASZ", 2.5) * sc, exo = Doc.HeaderDouble("$DIMEXO", 0.625) * sc, exe = Doc.HeaderDouble("$DIMEXE", 1.25) * sc, txt = Doc.HeaderDouble("$DIMTXT", 2.5) * sc, gap = Doc.HeaderDouble("$DIMGAP", 0.625) * sc;
        int dec = (int)Doc.HeaderDouble("$DIMDEC", 2); string tstyle = Doc.HeaderString("$DIMTXSTY", "Standard"); if (Doc.Styles.All(s => !s.Name.Equals(tstyle, StringComparison.OrdinalIgnoreCase))) tstyle = "Standard";
        double a = d.Rotation * Math.PI / 180; var ux = new V3(Math.Cos(a), Math.Sin(a)); var uy = new V3(-Math.Sin(a), Math.Cos(a));
        double Dot(V3 p, V3 u) => p.X * u.X + p.Y * u.Y;
        // dimension line passes through dimLine, direction ux; projections of p1/p2 on it
        double off = Dot(dimLine, uy);
        var q1 = ux * Dot(d.P13, ux) + uy * off; var q2 = ux * Dot(d.P14, ux) + uy * off;
        double meas = Math.Abs(Dot(d.P14, ux) - Dot(d.P13, ux)); d.Measurement = meas; d.HasMeasurement = true;
        d.DefPoint = q2; d.TextMid = (q1 + q2) * 0.5 + uy * (txt / 2 + gap);
        string text = DimText(d, meas, dec);
        blk.Entities.Clear();
        var items = new List<Entity>();
        double dir1 = Math.Sign(off - Dot(d.P13, uy)), dir2 = Math.Sign(off - Dot(d.P14, uy)); if (dir1 == 0) dir1 = 1; if (dir2 == 0) dir2 = 1;
        items.Add(new Line { P0 = d.P13 + uy * (exo * dir1), P1 = q1 + uy * (exe * dir1), Color = 0, Linetype = "BYBLOCK" });
        items.Add(new Line { P0 = d.P14 + uy * (exo * dir2), P1 = q2 + uy * (exe * dir2), Color = 0, Linetype = "BYBLOCK" });
        bool inside = meas > 2 * asz + 1;
        var dirq = (q2 - q1); double l = dirq.Len2; var u = l > 0 ? dirq * (1 / l) : ux;
        items.Add(new Line { P0 = inside ? q1 + u * asz : q1 - u * asz, P1 = inside ? q2 - u * asz : q2 + u * asz, Color = 0, Linetype = "BYBLOCK" });
        V3 Tri(V3 tip, V3 dirIn, V3 side, double k) => tip + dirIn * asz + side * (asz / 6 * k);
        var s1 = inside ? u : u * -1; var s2 = inside ? u * -1 : u;
        items.Add(new Solid { P0 = q1, P1 = Tri(q1, s1, uy, 1), P2 = Tri(q1, s1, uy, -1), P3 = Tri(q1, s1, uy, -1), Color = 0, Linetype = "BYBLOCK" });
        items.Add(new Solid { P0 = q2, P1 = Tri(q2, s2, uy, 1), P2 = Tri(q2, s2, uy, -1), P3 = Tri(q2, s2, uy, -1), Color = 0, Linetype = "BYBLOCK" });
        double rot = d.Rotation; if (rot > 90 && rot <= 270) rot -= 180;
        items.Add(new Text { Pos = d.TextMid, Align = d.TextMid, Height = txt, Rotation = Arc.Norm(rot), Style = tstyle, HAlign = 1, VAlign = 2, Value = text, Color = 0, Linetype = "BYBLOCK" });
        foreach (var e in items) { e.Handle = Doc.NewHandle(); e.Layer = "0"; e.Dirty = true; e.Owner = blk.RecordHandle; blk.Entities.Add(e); }
    }
    /// <summary>Rebuild every linear (rotated) dimension block from the current $DIM* header values (used after text auto-sizing). Not undoable by itself.</summary>
    public int RegenerateDimensions()
    {
        int n = 0;
        foreach (var d in Doc.Entities.OfType<Dimension>().Where(x => x.Kind is 3 or 4 && x.Block != "" && Doc.Blocks.ContainsKey(x.Block)).ToList())
        {
            var blk = Doc.Blocks[d.Block]; var nb = new Block { Name = blk.Name, Base = blk.Base, Flags = blk.Flags, Handle = blk.Handle, EndHandle = blk.EndHandle, RecordHandle = blk.RecordHandle, Layer = blk.Layer };
            BuildRadialDim(d, nb); Doc.Blocks[blk.Name] = nb; d.Dirty = true; d.Raw = null; n++;
        }
        foreach (var d in Doc.Entities.OfType<Dimension>().Where(x => x.Kind == 0 && x.Block != "" && Doc.Blocks.ContainsKey(x.Block)).ToList())
        {
            var blk = Doc.Blocks[d.Block];
            var nb = new Block { Name = blk.Name, Base = blk.Base, Flags = blk.Flags, Handle = blk.Handle, EndHandle = blk.EndHandle, RecordHandle = blk.RecordHandle, Layer = blk.Layer };
            BuildLinearDim(d, nb, d.DefPoint); Doc.Blocks[blk.Name] = nb; d.Dirty = true; d.Raw = null; n++;
        }
        if (n > 0) Doc.Dirty = true;
        return n;
    }

    void RegenerateDimension(Dimension d, Change c)
    {
        if (d.Kind != 0 || !Doc.Blocks.TryGetValue(d.Block, out var blk)) return;
        var nb = new Block { Name = blk.Name, Base = blk.Base, Flags = blk.Flags, Handle = blk.Handle, EndHandle = blk.EndHandle, RecordHandle = blk.RecordHandle, Layer = blk.Layer };
        BuildLinearDim(d, nb, d.DefPoint); Doc.Blocks[blk.Name] = nb;
    }
}
