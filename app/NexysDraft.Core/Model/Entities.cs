using System.Globalization;

namespace NexysDraft.Core.Model;

/// <summary>3D point / vector (the CAD is 2D; Z is carried through untouched).</summary>
public record struct V3(double X, double Y, double Z = 0)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public double Len2 => Math.Sqrt(X * X + Y * Y);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"({X:R},{Y:R},{Z:R})");
}

/// <summary>2D affine transform: x' = A x + B y + E, y' = C x + D y + F (Z unchanged).</summary>
public readonly record struct Xform(double A, double B, double C, double D, double E, double F)
{
    public static readonly Xform Identity = new(1, 0, 0, 1, 0, 0);
    public static Xform Translate(double dx, double dy) => new(1, 0, 0, 1, dx, dy);
    public static Xform Rotate(double cx, double cy, double deg)
    {
        double r = deg * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
        return new(c, -s, s, c, cx - c * cx + s * cy, cy - s * cx - c * cy);
    }
    public static Xform Scale(double cx, double cy, double k) => new(k, 0, 0, k, cx - k * cx, cy - k * cy);
    /// <summary>Reflection across the line p0→p1.</summary>
    public static Xform Mirror(double x0, double y0, double x1, double y1)
    {
        double dx = x1 - x0, dy = y1 - y0, l2 = dx * dx + dy * dy; if (l2 == 0) return Identity;
        double a = (dx * dx - dy * dy) / l2, b = 2 * dx * dy / l2;
        // reflect (x - p0) then add p0
        return new(a, b, b, -a, x0 - a * x0 - b * y0, y0 - b * x0 + a * y0);
    }
    public V3 Apply(V3 p) => new(A * p.X + B * p.Y + E, C * p.X + D * p.Y + F, p.Z);
    public V3 ApplyVector(V3 v) => new(A * v.X + B * v.Y, C * v.X + D * v.Y, v.Z);
    public double Det => A * D - B * C;
    public bool IsMirror => Det < 0;
    /// <summary>Uniform scale factor (sqrt |det|).</summary>
    public double ScaleFactor => Math.Sqrt(Math.Abs(Det));
    /// <summary>Rotation in degrees for a similarity transform (mirror handled by the caller).</summary>
    public double RotationDeg => Math.Atan2(C, A) * 180 / Math.PI;
    public double ApplyAngleDeg(double deg)
    {
        double r = deg * Math.PI / 180; var v = ApplyVector(new V3(Math.Cos(r), Math.Sin(r)));
        return Math.Atan2(v.Y, v.X) * 180 / Math.PI;
    }
}

public readonly record struct Group(int Code, string Value);

public abstract class Entity
{
    public string Handle = "";
    public string Layer = "0";
    /// <summary>ACI color: 256 = BYLAYER, 0 = BYBLOCK, 1..255 index; negative = layer off (layers only).</summary>
    public int Color = 256;
    public int? TrueColor;
    public string Linetype = "BYLAYER";
    public double LtScale = 1;
    public int Lineweight = -1;
    public bool PaperSpace;
    public bool Invisible;
    public string Owner = "";
    /// <summary>All groups as read (verbatim rewrite when the entity was not edited).</summary>
    public List<Group>? Raw;
    /// <summary>Groups the model does not interpret (102 blocks, xdata 1000+, proxy 310 …) — appended by the model writer.</summary>
    public List<Group> Extra = new();
    public bool Dirty;
    public abstract string DxfName { get; }
    protected abstract Entity CloneCore();
    public Entity Clone()
    {
        var e = CloneCore();
        e.Handle = Handle; e.Layer = Layer; e.Color = Color; e.TrueColor = TrueColor; e.Linetype = Linetype; e.LtScale = LtScale; e.Lineweight = Lineweight; e.PaperSpace = PaperSpace; e.Invisible = Invisible; e.Owner = Owner;
        e.Raw = Raw == null ? null : new List<Group>(Raw); e.Extra = new List<Group>(Extra); e.Dirty = Dirty;
        return e;
    }
    /// <summary>Apply a 2D similarity/mirror transform; marks the entity dirty (raw groups are dropped by the writer).</summary>
    public void Transform(Xform t) { TransformCore(t); Dirty = true; }
    protected abstract void TransformCore(Xform t);
    /// <summary>Points that take part in STRETCH (grips): index → point; <see cref="SetGrip"/> moves one.</summary>
    public virtual IReadOnlyList<V3> Grips => Array.Empty<V3>();
    public virtual void SetGrip(int i, V3 p) { }
}

public sealed class Line : Entity
{
    public V3 P0, P1; public V3 Normal = new(0, 0, 1); public double Thickness;
    public override string DxfName => "LINE";
    protected override Entity CloneCore() => new Line { P0 = P0, P1 = P1, Normal = Normal, Thickness = Thickness };
    protected override void TransformCore(Xform t) { P0 = t.Apply(P0); P1 = t.Apply(P1); }
    public override IReadOnlyList<V3> Grips => new[] { P0, P1 };
    public override void SetGrip(int i, V3 p) { if (i == 0) P0 = p; else P1 = p; Dirty = true; }
}

public sealed class Circle : Entity
{
    public V3 Center; public double Radius; public double Thickness; public V3 Normal = new(0, 0, 1);
    public override string DxfName => "CIRCLE";
    protected override Entity CloneCore() => new Circle { Center = Center, Radius = Radius, Thickness = Thickness };
    protected override void TransformCore(Xform t) { Center = t.Apply(Center); Radius *= t.ScaleFactor; }
    public override IReadOnlyList<V3> Grips => new[] { Center };
    public override void SetGrip(int i, V3 p) { Center = p; Dirty = true; }
}

public sealed class Arc : Entity
{
    public V3 Center; public double Radius; public double StartDeg, EndDeg; public double Thickness;
    public override string DxfName => "ARC";
    protected override Entity CloneCore() => new Arc { Center = Center, Radius = Radius, StartDeg = StartDeg, EndDeg = EndDeg, Thickness = Thickness };
    protected override void TransformCore(Xform t)
    {
        Center = t.Apply(Center); Radius *= t.ScaleFactor;
        double a0 = t.ApplyAngleDeg(StartDeg), a1 = t.ApplyAngleDeg(EndDeg);
        if (t.IsMirror) (a0, a1) = (a1, a0);
        StartDeg = Norm(a0); EndDeg = Norm(a1);
    }
    public static double Norm(double d) { d %= 360; if (d < 0) d += 360; return d; }
    public V3 PointAt(double deg) => new(Center.X + Radius * Math.Cos(deg * Math.PI / 180), Center.Y + Radius * Math.Sin(deg * Math.PI / 180), Center.Z);
    public override IReadOnlyList<V3> Grips => new[] { Center };
    public override void SetGrip(int i, V3 p) { Center = p; Dirty = true; }
}

public sealed class Ellipse : Entity
{
    public V3 Center, Major; public double Ratio = 1; public double StartParam, EndParam = Math.PI * 2;
    public override string DxfName => "ELLIPSE";
    protected override Entity CloneCore() => new Ellipse { Center = Center, Major = Major, Ratio = Ratio, StartParam = StartParam, EndParam = EndParam };
    protected override void TransformCore(Xform t)
    {
        Center = t.Apply(Center); Major = t.ApplyVector(Major);
        if (t.IsMirror) { double s = StartParam, e = EndParam; StartParam = Math.PI * 2 - e; EndParam = Math.PI * 2 - s; if (Math.Abs((e - s) - Math.PI * 2) < 1e-9) { StartParam = 0; EndParam = Math.PI * 2; } }
    }
    public override IReadOnlyList<V3> Grips => new[] { Center };
    public override void SetGrip(int i, V3 p) { Center = p; Dirty = true; }
}

public struct PVertex { public double X, Y, Bulge, StartWidth, EndWidth; public double Z; }

public sealed class LwPolyline : Entity
{
    public List<PVertex> Vertices = new(); public bool Closed; public double ConstWidth; public double Elevation; public bool Plinegen; public V3 Normal = new(0, 0, 1);
    public override string DxfName => "LWPOLYLINE";
    protected override Entity CloneCore() => new LwPolyline { Vertices = new List<PVertex>(Vertices), Closed = Closed, ConstWidth = ConstWidth, Elevation = Elevation, Plinegen = Plinegen, Normal = Normal };
    protected override void TransformCore(Xform t)
    {
        double k = t.ScaleFactor; bool m = t.IsMirror;
        for (int i = 0; i < Vertices.Count; i++) { var v = Vertices[i]; var p = t.Apply(new V3(v.X, v.Y)); v.X = p.X; v.Y = p.Y; if (m) v.Bulge = -v.Bulge; v.StartWidth *= k; v.EndWidth *= k; Vertices[i] = v; }
        ConstWidth *= k;
    }
    public override IReadOnlyList<V3> Grips => Vertices.Select(v => new V3(v.X, v.Y, Elevation)).ToList();
    public override void SetGrip(int i, V3 p) { var v = Vertices[i]; v.X = p.X; v.Y = p.Y; Vertices[i] = v; Dirty = true; }
}

/// <summary>Heavy POLYLINE (2D/3D) with its VERTEX children.</summary>
public sealed class Polyline : Entity
{
    public List<PVertex> Vertices = new(); public bool Closed; public bool Is3D; public int Flags; public double StartWidth, EndWidth; public double Elevation; public V3 Normal = new(0, 0, 1);
    /// <summary>Raw groups of each VERTEX and of the SEQEND (verbatim rewrite).</summary>
    public List<List<Group>>? VertexRaw; public List<Group>? SeqendRaw; public string SeqendHandle = "";
    public override string DxfName => "POLYLINE";
    protected override Entity CloneCore() => new Polyline { Vertices = new List<PVertex>(Vertices), Closed = Closed, Is3D = Is3D, Flags = Flags, StartWidth = StartWidth, EndWidth = EndWidth, Elevation = Elevation, Normal = Normal, SeqendHandle = SeqendHandle, VertexRaw = VertexRaw?.Select(v => new List<Group>(v)).ToList(), SeqendRaw = SeqendRaw == null ? null : new List<Group>(SeqendRaw) };
    protected override void TransformCore(Xform t)
    {
        double k = t.ScaleFactor; bool m = t.IsMirror;
        for (int i = 0; i < Vertices.Count; i++) { var v = Vertices[i]; var p = t.Apply(new V3(v.X, v.Y, v.Z)); v.X = p.X; v.Y = p.Y; if (m) v.Bulge = -v.Bulge; v.StartWidth *= k; v.EndWidth *= k; Vertices[i] = v; }
        StartWidth *= k; EndWidth *= k;
    }
    public override IReadOnlyList<V3> Grips => Vertices.Select(v => new V3(v.X, v.Y, v.Z)).ToList();
    public override void SetGrip(int i, V3 p) { var v = Vertices[i]; v.X = p.X; v.Y = p.Y; Vertices[i] = v; Dirty = true; }
}

public sealed class Text : Entity
{
    public V3 Pos, Align; public double Height = 2.5, Rotation, WidthFactor = 1, Oblique; public string Style = "Standard"; public int HAlign, VAlign, GenFlags; public string Value = ""; public V3 Normal = new(0, 0, 1); public double Thickness;
    /// <summary>ATTRIB (inside an INSERT) / ATTDEF (inside a block): text plus tag and flags.</summary>
    public bool IsAttrib, IsAttdef; public string AttribTag = ""; public int AttribFlags; public string Prompt = "";
    public override string DxfName => IsAttrib ? "ATTRIB" : IsAttdef ? "ATTDEF" : "TEXT";
    protected override Entity CloneCore() => new Text { Pos = Pos, Align = Align, Height = Height, Rotation = Rotation, WidthFactor = WidthFactor, Oblique = Oblique, Style = Style, HAlign = HAlign, VAlign = VAlign, GenFlags = GenFlags, Value = Value, Normal = Normal, Thickness = Thickness, IsAttrib = IsAttrib, IsAttdef = IsAttdef, AttribTag = AttribTag, AttribFlags = AttribFlags, Prompt = Prompt };
    protected override void TransformCore(Xform t)
    {
        double w = Height * WidthFactor * 0.8 * Math.Max(1, Value.Length);   // approximate advance (no font metrics here)
        var p0 = Pos; Pos = t.Apply(Pos); Align = t.Apply(Align); Height *= t.ScaleFactor; w *= t.ScaleFactor;
        double r = t.ApplyAngleDeg(Rotation);
        if (t.IsMirror && Readable(ref r) && HAlign == 0 && VAlign == 0)
        {
            // MIRRTEXT=0: the text box is mirrored but the text stays readable — the insertion moves to the other end of the mirrored span
            double rr = (r + 180) * Math.PI / 180; Pos = new V3(Pos.X + w * Math.Cos(rr), Pos.Y + w * Math.Sin(rr), Pos.Z);
        }
        Rotation = Arc.Norm(r);
    }
    /// <summary>Flip a mirrored reading direction back into the readable half-plane; true when flipped.</summary>
    internal static bool Readable(ref double deg) { deg = Arc.Norm(deg); if (deg > 90 && deg <= 270) { deg = Arc.Norm(deg + 180); return true; } return false; }
    public override IReadOnlyList<V3> Grips => new[] { HAlign != 0 || VAlign != 0 ? Align : Pos };
    public override void SetGrip(int i, V3 p) { var d = p - (HAlign != 0 || VAlign != 0 ? Align : Pos); Pos += d; Align += d; Dirty = true; }
    /// <summary>Insertion point that governs the text's placement (alignment point when aligned).</summary>
    public V3 Anchor => HAlign != 0 || VAlign != 0 ? Align : Pos;
}

public sealed class MText : Entity
{
    public V3 Pos, XAxis = new(1, 0, 0); public double Height = 2.5, Width, Rotation, LineSpacing = 1; public int Attach = 1, DirFlag = 1, LineSpacingStyle = 2; public string Style = "Standard";
    /// <summary>Raw MTEXT content (format codes included).</summary>
    public string Value = ""; public V3 Normal = new(0, 0, 1); public bool HasXAxis;
    public override string DxfName => "MTEXT";
    protected override Entity CloneCore() => new MText { Pos = Pos, XAxis = XAxis, Height = Height, Width = Width, Rotation = Rotation, LineSpacing = LineSpacing, Attach = Attach, DirFlag = DirFlag, LineSpacingStyle = LineSpacingStyle, Style = Style, Value = Value, Normal = Normal, HasXAxis = HasXAxis };
    public double RotationDeg => HasXAxis ? Math.Atan2(XAxis.Y, XAxis.X) * 180 / Math.PI : Rotation;
    protected override void TransformCore(Xform t)
    {
        Pos = t.Apply(Pos); Height *= t.ScaleFactor; Width *= t.ScaleFactor;
        double r = t.ApplyAngleDeg(RotationDeg);
        if (t.IsMirror && Text.Readable(ref r) && Attach is 1 or 4 or 7)
        {
            double w = Width > 0 ? Width : Height * 0.8 * Math.Max(1, Value.Length);
            double rr = (r + 180) * Math.PI / 180; Pos = new V3(Pos.X + w * Math.Cos(rr), Pos.Y + w * Math.Sin(rr), Pos.Z);
        }
        Rotation = Arc.Norm(r); XAxis = new V3(Math.Cos(Rotation * Math.PI / 180), Math.Sin(Rotation * Math.PI / 180)); HasXAxis = true;
    }
    public override IReadOnlyList<V3> Grips => new[] { Pos };
    public override void SetGrip(int i, V3 p) { Pos = p; Dirty = true; }
}

public sealed class Attrib
{
    public string Tag = ""; public string Value = ""; public int Flags; public Text Text = new(); public List<Group>? Raw; public string Handle = ""; public bool IsAttdef; public string Prompt = "";
    public Attrib Clone() => new() { Tag = Tag, Value = Value, Flags = Flags, Text = (Text)Text.Clone(), Raw = Raw == null ? null : new List<Group>(Raw), Handle = Handle, IsAttdef = IsAttdef, Prompt = Prompt };
    public bool Invisible => (Flags & 1) != 0;
}

public sealed class Insert : Entity
{
    public string Block = ""; public V3 Pos; public double SX = 1, SY = 1, SZ = 1, Rotation; public int Cols = 1, Rows = 1; public double ColSpacing, RowSpacing; public V3 Normal = new(0, 0, 1);
    public List<Attrib> Attribs = new(); public List<Group>? SeqendRaw; public string SeqendHandle = "";
    public override string DxfName => "INSERT";
    protected override Entity CloneCore() => new Insert { Block = Block, Pos = Pos, SX = SX, SY = SY, SZ = SZ, Rotation = Rotation, Cols = Cols, Rows = Rows, ColSpacing = ColSpacing, RowSpacing = RowSpacing, Normal = Normal, Attribs = Attribs.Select(a => a.Clone()).ToList(), SeqendRaw = SeqendRaw == null ? null : new List<Group>(SeqendRaw), SeqendHandle = SeqendHandle };
    protected override void TransformCore(Xform t)
    {
        Pos = t.Apply(Pos); double k = t.ScaleFactor; SX *= k; SY *= k; SZ *= k;
        Rotation = Arc.Norm(t.ApplyAngleDeg(Rotation));   // mirror: x axis reflected, y axis flipped by the sign of SY
        if (t.IsMirror) SY = -SY;
        foreach (var a in Attribs) a.Text.Transform(t);
        ColSpacing *= k; RowSpacing *= k;
    }
    public override IReadOnlyList<V3> Grips => new[] { Pos };
    public override void SetGrip(int i, V3 p) { var d = p - Pos; Pos = p; foreach (var a in Attribs) { a.Text.Pos += d; a.Text.Align += d; } Dirty = true; }
}

public sealed class HatchEdge
{
    /// <summary>1 line, 2 arc, 3 ellipse, 4 spline, 0 = polyline vertex list.</summary>
    public int Type; public V3 P0, P1; public V3 Center; public double Radius, StartDeg, EndDeg; public bool Ccw = true; public V3 Major; public double Ratio;
    public List<PVertex> Poly = new(); public bool PolyClosed; public bool PolyHasBulge;
    public List<Group> Raw = new();   // spline edges and anything else: kept verbatim
    public HatchEdge Clone() => new() { Type = Type, P0 = P0, P1 = P1, Center = Center, Radius = Radius, StartDeg = StartDeg, EndDeg = EndDeg, Ccw = Ccw, Major = Major, Ratio = Ratio, Poly = new List<PVertex>(Poly), PolyClosed = PolyClosed, PolyHasBulge = PolyHasBulge, Raw = new List<Group>(Raw) };
}

public sealed class HatchLoop
{
    public int Type; public List<HatchEdge> Edges = new(); public List<string> SourceHandles = new();
    public bool IsPolyline => (Type & 2) != 0;
    public HatchLoop Clone() => new() { Type = Type, Edges = Edges.Select(e => e.Clone()).ToList(), SourceHandles = new List<string>(SourceHandles) };
}

public sealed class Hatch : Entity
{
    public string Pattern = "SOLID"; public bool Solid = true; public bool Associative; public int Style, PatternType; public double PatternAngle, PatternScale = 1; public bool DoubleFlag; public double Elevation; public V3 Normal = new(0, 0, 1);
    public List<HatchLoop> Loops = new();
    /// <summary>Pattern definition lines and the trailing groups (seed points, gradient data …) exactly as read.</summary>
    public List<Group> Tail = new();
    public override string DxfName => "HATCH";
    protected override Entity CloneCore() => new Hatch { Pattern = Pattern, Solid = Solid, Associative = Associative, Style = Style, PatternType = PatternType, PatternAngle = PatternAngle, PatternScale = PatternScale, DoubleFlag = DoubleFlag, Elevation = Elevation, Normal = Normal, Loops = Loops.Select(l => l.Clone()).ToList(), Tail = new List<Group>(Tail) };
    protected override void TransformCore(Xform t)
    {
        double k = t.ScaleFactor; bool m = t.IsMirror;
        foreach (var l in Loops) foreach (var e in l.Edges)
            {
                switch (e.Type)
                {
                    case 0: for (int i = 0; i < e.Poly.Count; i++) { var v = e.Poly[i]; var p = t.Apply(new V3(v.X, v.Y)); v.X = p.X; v.Y = p.Y; if (m) v.Bulge = -v.Bulge; e.Poly[i] = v; } break;
                    case 1: e.P0 = t.Apply(e.P0); e.P1 = t.Apply(e.P1); break;
                    case 2: { e.Center = t.Apply(e.Center); e.Radius *= k; double a0 = t.ApplyAngleDeg(e.StartDeg), a1 = t.ApplyAngleDeg(e.EndDeg); if (m) { e.Ccw = !e.Ccw; } e.StartDeg = a0; e.EndDeg = a1; break; }
                    case 3: e.Center = t.Apply(e.Center); e.Major = t.ApplyVector(e.Major); if (m) e.Ccw = !e.Ccw; break;
                }
            }
        Associative = false; foreach (var l in Loops) l.SourceHandles.Clear();
        PatternAngle = t.ApplyAngleDeg(PatternAngle); PatternScale *= k;
    }
}

public sealed class Dimension : Entity
{
    public string Block = ""; public V3 DefPoint, TextMid, P13, P14, P15, P16, Insert; public int Type; public int Attach = 5; public string Text = ""; public string Style = "Standard"; public double Rotation, HorizDir, Measurement, TextRotation, LeaderLen, ObliqueAngle; public int LineSpacingStyle; public double LineSpacing = 1; public V3 Normal = new(0, 0, 1); public bool HasMeasurement;
    public override string DxfName => "DIMENSION";
    public int Kind => Type & 15;
    public bool UserTextPos => (Type & 128) != 0;
    protected override Entity CloneCore() => new Dimension { Block = Block, DefPoint = DefPoint, TextMid = TextMid, P13 = P13, P14 = P14, P15 = P15, P16 = P16, Insert = Insert, Type = Type, Attach = Attach, Text = Text, Style = Style, Rotation = Rotation, HorizDir = HorizDir, Measurement = Measurement, TextRotation = TextRotation, LeaderLen = LeaderLen, ObliqueAngle = ObliqueAngle, LineSpacingStyle = LineSpacingStyle, LineSpacing = LineSpacing, Normal = Normal, HasMeasurement = HasMeasurement };
    protected override void TransformCore(Xform t)
    {
        DefPoint = t.Apply(DefPoint); TextMid = t.Apply(TextMid); P13 = t.Apply(P13); P14 = t.Apply(P14); P15 = t.Apply(P15); P16 = t.Apply(P16); Insert = t.Apply(Insert);
        Rotation = Arc.Norm(t.ApplyAngleDeg(Rotation)); HorizDir = Arc.Norm(t.ApplyAngleDeg(HorizDir)); Measurement *= t.ScaleFactor;
    }
    public override IReadOnlyList<V3> Grips => new[] { P13, P14, DefPoint, TextMid };
    public override void SetGrip(int i, V3 p) { switch (i) { case 0: P13 = p; break; case 1: P14 = p; break; case 2: DefPoint = p; break; case 3: TextMid = p; break; } Dirty = true; }
}

public sealed class Solid : Entity
{
    public V3 P0, P1, P2, P3; public string Name = "SOLID"; public double Thickness; public V3 Normal = new(0, 0, 1);
    public override string DxfName => Name;
    protected override Entity CloneCore() => new Solid { P0 = P0, P1 = P1, P2 = P2, P3 = P3, Name = Name, Thickness = Thickness, Normal = Normal };
    protected override void TransformCore(Xform t) { P0 = t.Apply(P0); P1 = t.Apply(P1); P2 = t.Apply(P2); P3 = t.Apply(P3); }
    public override IReadOnlyList<V3> Grips => new[] { P0, P1, P2, P3 };
    public override void SetGrip(int i, V3 p) { switch (i) { case 0: P0 = p; break; case 1: P1 = p; break; case 2: P2 = p; break; default: P3 = p; break; } Dirty = true; }
}

public sealed class Point : Entity
{
    public V3 Pos; public double Angle;
    public override string DxfName => "POINT";
    protected override Entity CloneCore() => new Point { Pos = Pos, Angle = Angle };
    protected override void TransformCore(Xform t) { Pos = t.Apply(Pos); }
    public override IReadOnlyList<V3> Grips => new[] { Pos };
    public override void SetGrip(int i, V3 p) { Pos = p; Dirty = true; }
}

public sealed class Spline : Entity
{
    public int Degree = 3, Flags; public List<double> Knots = new(), Weights = new(); public List<V3> Control = new(), Fit = new(); public V3 StartTangent, EndTangent; public bool HasTangents; public V3 Normal = new(0, 0, 1); public double KnotTol = 1e-7, CtrlTol = 1e-7, FitTol = 1e-9;
    public override string DxfName => "SPLINE";
    public bool Closed => (Flags & 1) != 0;
    protected override Entity CloneCore() => new Spline { Degree = Degree, Flags = Flags, Knots = new List<double>(Knots), Weights = new List<double>(Weights), Control = new List<V3>(Control), Fit = new List<V3>(Fit), StartTangent = StartTangent, EndTangent = EndTangent, HasTangents = HasTangents, Normal = Normal, KnotTol = KnotTol, CtrlTol = CtrlTol, FitTol = FitTol };
    protected override void TransformCore(Xform t) { for (int i = 0; i < Control.Count; i++) Control[i] = t.Apply(Control[i]); for (int i = 0; i < Fit.Count; i++) Fit[i] = t.Apply(Fit[i]); StartTangent = t.ApplyVector(StartTangent); EndTangent = t.ApplyVector(EndTangent); }
    public override IReadOnlyList<V3> Grips => Control;
    public override void SetGrip(int i, V3 p) { Control[i] = p; Dirty = true; }
}

/// <summary>Any entity the model does not interpret (LEADER, MLEADER, ACAD_TABLE, VIEWPORT, 3DSOLID …): raw groups only.</summary>
public sealed class Unknown : Entity
{
    public string Name = ""; public List<List<Group>>? Children; public List<Group>? SeqendRaw;
    public override string DxfName => Name;
    protected override Entity CloneCore() => new Unknown { Name = Name, Children = Children?.Select(c => new List<Group>(c)).ToList(), SeqendRaw = SeqendRaw == null ? null : new List<Group>(SeqendRaw) };
    protected override void TransformCore(Xform t) { }
}

public sealed class Layer
{
    public string Name = "0"; public int Color = 7; public bool Off; public string Linetype = "Continuous"; public int Flags; public int Lineweight = -3; public bool Plot = true; public string Handle = ""; public int? TrueColor; public List<Group>? Raw; public bool Dirty;
    public bool Frozen { get => (Flags & 1) != 0; set => Flags = value ? Flags | 1 : Flags & ~1; }
    public bool Locked { get => (Flags & 4) != 0; set => Flags = value ? Flags | 4 : Flags & ~4; }
}

public sealed class LineType { public string Name = ""; public string Description = ""; public List<double> Pattern = new(); public double Length; public string Handle = ""; public List<Group>? Raw; }
public sealed class TextStyle { public string Name = ""; public string Font = "txt"; public string BigFont = ""; public double Height, WidthFactor = 1, Oblique; public int Flags, GenFlags; public string Handle = ""; public List<Group>? Raw; public string? TrueTypeFamily; }

public sealed class Block
{
    public string Name = ""; public V3 Base; public int Flags; public string Handle = "", EndHandle = "", RecordHandle = ""; public string Layer = "0"; public string XrefPath = "";
    public List<Entity> Entities = new();
    public List<Group>? Raw, EndRaw, RecordRaw;
    public bool IsAnonymous => Name.StartsWith("*");
    public bool IsLayout => Name.StartsWith("*Model_Space", StringComparison.OrdinalIgnoreCase) || Name.StartsWith("*Paper_Space", StringComparison.OrdinalIgnoreCase);
}

/// <summary>A raw table (VPORT, VIEW, UCS, APPID, DIMSTYLE, BLOCK_RECORD) kept for verbatim rewrite.</summary>
public sealed class RawTable { public string Name = ""; public List<Group> Head = new(); public List<List<Group>> Entries = new(); }

public sealed class Document
{
    public string Path = ""; public string Version = "AC1015"; public string CodePage = "ANSI_949"; public bool SourceUtf8;
    public List<Group> HeaderRaw = new();
    public List<Group> ClassesRaw = new();
    public List<RawTable> RawTables = new();
    public List<Group> ObjectsRaw = new();
    public List<(string name, List<Group> groups)> OtherSections = new();
    public List<Layer> Layers = new();
    public List<LineType> Linetypes = new();
    public List<TextStyle> Styles = new();
    public Dictionary<string, Block> Blocks = new(StringComparer.OrdinalIgnoreCase);
    public List<string> BlockOrder = new();
    public List<Entity> Entities = new();
    public List<string> Warnings = new();
    /// <summary>Entity type histogram of the last read (ENTITIES section, model + paper space).</summary>
    public Dictionary<string, int> TypeCounts = new();
    public int UnknownCount => Entities.Count(e => e is Unknown);
    public bool Dirty;
    public double TokenizeMs, ParseMs;
    public long HandSeed = 0x100;
    public Dictionary<string, string> Header = new();

    public string NewHandle() { string h = HandSeed.ToString("X"); HandSeed++; return h; }
    /// <summary>Empty drawing: layer 0, DASHED linetype, Standard style on malgun.ttf (TrueType, so Korean renders in every CAD), model/paper space blocks.</summary>
    public static Document CreateNew(string codePage = "ANSI_949")
    {
        var d = new Document { CodePage = codePage, Version = "AC1015", Path = "" };
        d.Layers.Add(new Layer { Name = "0", Handle = d.NewHandle(), Dirty = true });
        d.Linetypes.Add(new LineType { Name = "DASHED", Description = "Dashed __ __ __", Pattern = { 12.7, -6.35 }, Handle = d.NewHandle() });
        d.Styles.Add(new TextStyle { Name = "Standard", Font = "malgun.ttf", TrueTypeFamily = "Malgun Gothic", Handle = d.NewHandle() });
        d.Blocks["*Model_Space"] = new Block { Name = "*Model_Space", RecordHandle = d.NewHandle(), Handle = d.NewHandle(), EndHandle = d.NewHandle() };
        d.Blocks["*Paper_Space"] = new Block { Name = "*Paper_Space", RecordHandle = d.NewHandle(), Handle = d.NewHandle(), EndHandle = d.NewHandle() };
        d.BlockOrder.Add("*Model_Space"); d.BlockOrder.Add("*Paper_Space");
        d.Header["$INSUNITS"] = "4"; d.Header["$DIMSCALE"] = "1.0"; d.Header["$DIMTXT"] = "2.5"; d.Header["$DIMASZ"] = "2.5"; d.Header["$CLAYER"] = "0";
        return d;
    }
    public Layer? LayerOf(string name) => Layers.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
    /// <summary>BLOCK_RECORD handle of *Model_Space (owner of model space entities).</summary>
    public string ModelSpaceHandle => SpaceHandle("*Model_Space");
    public string PaperSpaceHandle => SpaceHandle("*Paper_Space");
    string SpaceHandle(string name)
    {
        if (Blocks.TryGetValue(name, out var b) && b.RecordHandle != "") return b.RecordHandle;
        var t = RawTables.FirstOrDefault(t => t.Name.Equals("BLOCK_RECORD", StringComparison.OrdinalIgnoreCase));
        if (t != null) foreach (var e in t.Entries) if (e.Any(g => g.Code == 2 && g.Value.Equals(name, StringComparison.OrdinalIgnoreCase))) return e.FirstOrDefault(g => g.Code == 5).Value ?? "";
        return "";
    }
    public Layer EnsureLayer(string name, int color = 7)
    {
        var l = LayerOf(name); if (l != null) return l;
        l = new Layer { Name = name, Color = color, Handle = NewHandle(), Dirty = true }; Layers.Add(l); Dirty = true; return l;
    }
    public Entity? Find(string handle) => Entities.FirstOrDefault(e => e.Handle == handle);
    public double HeaderDouble(string name, double dflt) => Header.TryGetValue(name, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : dflt;
    public string HeaderString(string name, string dflt) => Header.TryGetValue(name, out var s) ? s : dflt;
    public int VersionYear => Version switch { "AC1009" => 12, "AC1012" => 13, "AC1014" => 14, "AC1015" => 2000, "AC1018" => 2004, "AC1021" => 2007, "AC1024" => 2010, "AC1027" => 2013, "AC1032" => 2018, _ => 2000 };
    /// <summary>Effective ACI color of an entity (BYLAYER resolved; BYBLOCK → 7 when not inside a block).</summary>
    public int EffectiveColor(Entity e)
    {
        if (e.Color is > 0 and < 256) return e.Color;
        if (e.Color == 0) return 7;
        var l = LayerOf(e.Layer); return l != null ? Math.Abs(l.Color) : 7;
    }
}
