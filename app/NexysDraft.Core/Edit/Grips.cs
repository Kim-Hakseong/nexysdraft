using NexysDraft.Core.Model;

namespace NexysDraft.Core.Edit;

public sealed partial class Editor
{
    /// <summary>Grip edit: the grip of <paramref name="handle"/> at <paramref name="grip"/> (within <paramref name="tol"/>) goes to <paramref name="to"/>.
    /// End of a line / vertex of a polyline: that point moves (the object gets longer or shorter). Middle of a line, centre of a circle or arc,
    /// insertion point of a text or block: the object moves. Quadrant of a circle, middle of an arc: the radius changes. End of an arc: its angle changes.</summary>
    public Change? MoveGrip(string handle, V3 grip, V3 to, double tol)
    {
        var found = Resolve(new[] { handle }).ToList(); if (found.Count == 0) return null;
        var (i, e) = found[0]; var l0 = Doc.LayerOf(e.Layer); if (l0 != null && l0.Locked) return null;
        var c = Begin("Grip"); var after = e.Clone(); after.Raw = null; after.Dirty = true;
        double D(V3 a, V3 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        V3 delta = new(to.X - grip.X, to.Y - grip.Y, 0);
        void Shift() { after.Transform(Xform.Translate(delta.X, delta.Y)); if (after is Dimension dd && Doc.Blocks.TryGetValue(dd.Block, out var bb)) TransformDimBlock(dd, bb, Xform.Translate(delta.X, delta.Y), c); }
        bool done = false;
        switch (after)
        {
            case Line ln:
                {
                    double d0 = D(grip, ln.P0), d1 = D(grip, ln.P1);
                    if (Math.Min(d0, d1) <= tol) { if (d0 <= d1) ln.P0 = new V3(to.X, to.Y, ln.P0.Z); else ln.P1 = new V3(to.X, to.Y, ln.P1.Z); }
                    else Shift();
                    done = true; break;
                }
            case Circle ci:
                {
                    if (D(grip, ci.Center) <= tol) Shift(); else { double r = D(to, ci.Center); if (r <= 1e-9) return null; ci.Radius = r; }
                    done = true; break;
                }
            case Arc ar:
                {
                    if (D(grip, ar.Center) <= tol) { Shift(); done = true; break; }
                    double ang = Arc.Norm(Math.Atan2(to.Y - ar.Center.Y, to.X - ar.Center.X) * 180 / Math.PI);
                    double ds = D(grip, ar.PointAt(ar.StartDeg)), de = D(grip, ar.PointAt(ar.EndDeg));
                    if (Math.Min(ds, de) <= tol) { if (ds <= de) ar.StartDeg = ang; else ar.EndDeg = ang; }
                    else { double r = D(to, ar.Center); if (r <= 1e-9) return null; ar.Radius = r; }
                    done = true; break;
                }
        }
        if (!done)
        {
            var g = e.Grips; int best = -1; double bd = double.MaxValue;
            for (int k = 0; k < g.Count; k++) { double d = D(grip, g[k]); if (d < bd) { bd = d; best = k; } }
            bool rigid = after is Text or MText or Insert or Ellipse or Point;
            if (best < 0 || bd > tol)
            {
                // the middle of a polyline segment moves that segment (both of its vertices); anything else moves the object
                int seg = -1;
                if (after is LwPolyline or Polyline) for (int k = 0; k < g.Count; k++) { int n = (k + 1) % g.Count; if (n == 0 && !IsClosed(after)) break; var m = new V3((g[k].X + g[n].X) / 2, (g[k].Y + g[n].Y) / 2, 0); if (D(grip, m) <= tol) { seg = k; break; } }
                if (seg >= 0) { int n = (seg + 1) % g.Count; after.SetGrip(seg, new V3(g[seg].X + delta.X, g[seg].Y + delta.Y, g[seg].Z)); after.SetGrip(n, new V3(g[n].X + delta.X, g[n].Y + delta.Y, g[n].Z)); }
                else Shift();
            }
            else if (rigid || g.Count == 1) Shift();
            else { after.SetGrip(best, new V3(to.X, to.Y, g[best].Z)); if (after is Dimension d) RegenerateDimension(d, c); }
        }
        Doc.Entities[i] = after; c.Replaced.Add((i, e, after));
        Commit(c); return c;
    }
    static bool IsClosed(Entity e) => e is LwPolyline lw ? lw.Closed : e is Polyline pl && pl.Closed;
}
