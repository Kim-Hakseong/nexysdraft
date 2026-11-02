namespace NexysDraft.Core.Edit;

/// <summary>Geometry of the basic shape commands (draft5e): RECTANG, 3-point ARC, POLYGON, ELLIPSE by axis. The page (view.ts) uses the same formulas.</summary>
public static class Shapes
{
    static double Hyp(double x, double y) => Math.Sqrt(x * x + y * y);
    /// <summary>Closed rectangle from two opposite corners, counter-clockwise from the lower-left corner.</summary>
    public static List<(double x, double y)> Rectangle(double x0, double y0, double x1, double y1)
    {
        double a = Math.Min(x0, x1), b = Math.Min(y0, y1), c = Math.Max(x0, x1), d = Math.Max(y0, y1);
        return new() { (a, b), (c, b), (c, d), (a, d) };
    }

    /// <summary>Arc through three points (start, any point on the arc, end): centre, radius and counter-clockwise start/end angles in degrees. Null when collinear.</summary>
    public static (double cx, double cy, double r, double a0, double a1)? Arc3(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        double d = 2 * (x1 * (y2 - y3) + x2 * (y3 - y1) + x3 * (y1 - y2));
        double scale = Math.Max(1e-12, Math.Max(Hyp(x2 - x1, y2 - y1), Hyp(x3 - x1, y3 - y1)));
        if (Math.Abs(d) < 1e-12 * scale * scale) return null;
        double s1 = x1 * x1 + y1 * y1, s2 = x2 * x2 + y2 * y2, s3 = x3 * x3 + y3 * y3;
        double cx = (s1 * (y2 - y3) + s2 * (y3 - y1) + s3 * (y1 - y2)) / d, cy = (s1 * (x3 - x2) + s2 * (x1 - x3) + s3 * (x2 - x1)) / d;
        double r = Hyp(x1 - cx, y1 - cy);
        double A(double x, double y) { double a = Math.Atan2(y - cy, x - cx) * 180 / Math.PI; return a < 0 ? a + 360 : a; }
        double a1 = A(x1, y1), a2 = A(x2, y2), a3 = A(x3, y3);
        // counter-clockwise from a1: does it pass a2 before a3? then the arc runs a1 → a3, else a3 → a1
        double Ccw(double from, double to) { double t = to - from; return t < 0 ? t + 360 : t; }
        return Ccw(a1, a2) <= Ccw(a1, a3) ? (cx, cy, r, a1, a3) : (cx, cy, r, a3, a1);
    }

    /// <summary>Regular polygon: n sides, centre, radius. Inscribed = vertices on the circle, circumscribed = edge midpoints on it.
    /// <paramref name="angleDeg"/> = direction of the first vertex (inscribed) or first edge midpoint (circumscribed).</summary>
    public static List<(double x, double y)> Polygon(int n, double cx, double cy, double radius, bool inscribed, double angleDeg)
    {
        n = Math.Clamp(n, 3, 1024);
        double step = 2 * Math.PI / n, a = angleDeg * Math.PI / 180, rv = radius;
        if (!inscribed) { rv = radius / Math.Cos(Math.PI / n); a += Math.PI / n; }
        var pts = new List<(double, double)>(n);
        for (int k = 0; k < n; k++) pts.Add((cx + rv * Math.Cos(a + k * step), cy + rv * Math.Sin(a + k * step)));
        return pts;
    }

    /// <summary>Ellipse from the two ends of one axis and the other half-axis length: centre, major axis vector (relative), ratio ≤ 1.</summary>
    public static (double cx, double cy, double mx, double my, double ratio)? EllipseByAxis(double x0, double y0, double x1, double y1, double otherHalf)
    {
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, mx = x1 - cx, my = y1 - cy, half = Hyp(mx, my);
        if (half < 1e-12 || otherHalf <= 1e-12) return null;
        if (otherHalf <= half) return (cx, cy, mx, my, otherHalf / half);
        // the other axis is longer: it becomes the major axis (perpendicular), ratio = half / otherHalf
        double ux = -my / half, uy = mx / half;
        return (cx, cy, ux * otherHalf, uy * otherHalf, half / otherHalf);
    }
}
