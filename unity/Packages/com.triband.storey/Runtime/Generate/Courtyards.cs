#nullable enable
using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// Courtyards and atria (<see cref="VoidData"/>): openings through a building from a storey's floor up through the
    /// roof. A courtyard has facades facing into it (windows, bands, a door onto it on its bottom storey), paving at its
    /// bottom and a parapet round it on a flat roof; the walls stand just inside its outline, as a building's outer walls
    /// stand just outside its own. An atrium is inside the building: the slabs above its bottom storey are cut, with rails
    /// round each opening, under a skylight on a flat roof. Storey's own.
    /// </summary>
    public static class Courtyards
    {
        /// <summary>A courtyard roof's parapet and an atrium's skylight curb, and the rails' height.</summary>
        public const double CurbH = 0.4, RailH = 1.02;

        /// <summary>The void is built: a polygon, and a bottom storey the building has.</summary>
        public static bool Live(BuildingData b, VoidData v) => v.shape.Count >= 3 && v.bottom >= 0 && v.bottom < b.floors.Count;

        /// <summary>
        /// The slab at level k is cut by the void: every level above its bottom, the roof included (an atrium under a
        /// pitched roof stops at the top storey's ceiling).
        /// </summary>
        public static bool HoleAt(BuildingData b, VoidData v, int k)
        {
            int N = b.floors.Count;
            if (!Live(b, v) || k <= v.bottom || k > N) return false;
            return !(k == N && v.kind == VoidKind.Atrium && Roofs.IsPitched(b));
        }

        /// <summary>Storey k has the courtyard's facades round it.</summary>
        public static bool WallsAt(BuildingData b, VoidData v, int k) => v.kind == VoidKind.Courtyard && Live(b, v) && k >= v.bottom && k < b.floors.Count;

        /// <summary>Storey k is open round the atrium, behind rails.</summary>
        public static bool RailsAt(BuildingData b, VoidData v, int k) => v.kind == VoidKind.Atrium && Live(b, v) && k > v.bottom && k < b.floors.Count;

        /// <summary>Where on storey k a point is taken by a void (open, or a courtyard's walls): nothing stands or walks there.</summary>
        public static bool Taken(BuildingData b, VoidData v, int k) => WallsAt(b, v, k) || RailsAt(b, v, k);

        public static List<Vec2> World(BuildingData b, List<Vec2> fp)
        {
            var o = new List<Vec2>(fp.Count); foreach (var p in fp) o.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z)); return o;
        }

        /// <summary>The polygon grown (or shrunk, d &lt; 0) by d with mitred corners; empty when it vanishes.</summary>
        public static List<List<Vec2>> Offset(List<Vec2> fp, double d)
        {
            var path = new PathD(fp.Count); foreach (var p in fp) path.Add(new PointD(p.x, p.z));
            var res = Clipper.InflatePaths(new PathsD { path }, d, JoinType.Miter, EndType.Polygon, 4, 6);
            var o = new List<List<Vec2>>();
            foreach (var r in res) { var l = new List<Vec2>(r.Count); foreach (var q in r) l.Add(new Vec2(q.x, q.y)); if (l.Count >= 3) o.Add(l); }
            return o;
        }

        /// <summary>The courtyard's door: the middle of its longest edge.</summary>
        public static (int edge, double t) Door(VoidData v)
        {
            int best = 0; double bl = -1;
            for (int i = 0; i < v.shape.Count; i++) { double L = Geo.EdgeLen(v.shape, i); if (L > bl + 1e-9) { bl = L; best = i; } }
            return (best, 0.5);
        }

        /// <summary>The parameter ranges (0..1) of segment a–c that lie inside a polygon.</summary>
        public static List<(double, double)> InsideRanges(List<Vec2> poly, Vec2 a, Vec2 c)
        {
            double dx = c.x - a.x, dz = c.z - a.z;
            var ts = new List<double> { 0, 1 };
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                double ex = q.x - p.x, ez = q.z - p.z, den = dx * ez - dz * ex; if (Math.Abs(den) < 1e-12) continue;
                double t = ((p.x - a.x) * ez - (p.z - a.z) * ex) / den, u = ((p.x - a.x) * dz - (p.z - a.z) * dx) / den;
                if (t > 0 && t < 1 && u >= -1e-9 && u <= 1 + 1e-9) ts.Add(t);
            }
            ts.Sort();
            var o = new List<(double, double)>();
            for (int j = 0; j < ts.Count - 1; j++)
            {
                double t0 = ts[j], t1 = ts[j + 1]; if (t1 - t0 < 1e-9) continue;
                double tm = (t0 + t1) / 2;
                if (Geo.Pip(poly, a.x + dx * tm, a.z + dz * tm)) o.Add((t0, t1));
            }
            return o;
        }

        /// <summary>Interior walls (building-local) with the parts inside a void open on storey k taken out.</summary>
        public static List<WallData> ClipOut(BuildingData b, int k, List<WallData> walls)
        {
            foreach (var v in b.voids)
            {
                if (!Taken(b, v, k)) continue;
                var next = new List<WallData>();
                foreach (var wl in walls)
                {
                    double L = Geo.Hypot(wl.b.x - wl.a.x, wl.b.z - wl.a.z);
                    var cut = InsideRanges(v.shape, wl.a, wl.b);
                    if (cut.Count == 0) { next.Add(wl); continue; }
                    double cur = 0;
                    void Keep(double t0, double t1)
                    {
                        if ((t1 - t0) * L < 0.3) return;
                        var w = new WallData { a = new Vec2(wl.a.x + (wl.b.x - wl.a.x) * t0, wl.a.z + (wl.b.z - wl.a.z) * t0), b = new Vec2(wl.a.x + (wl.b.x - wl.a.x) * t1, wl.a.z + (wl.b.z - wl.a.z) * t1) };
                        foreach (var d in wl.doors) if (d.t > t0 && d.t < t1) w.doors.Add(new DoorData { t = (d.t - t0) / (t1 - t0) });
                        next.Add(w);
                    }
                    foreach (var (t0, t1) in cut) { if (t0 > cur) Keep(cur, t0); cur = t1; }
                    if (cur < 1) Keep(cur, 1);
                }
                walls = next;
            }
            return walls;
        }

        // ---- courtyard facades ------------------------------------------------------------------

        /// <summary>The courtyard's outline as walls: world points, and which way is "out" (into the courtyard).</summary>
        sealed class Ring { public List<Vec2> Wp = null!; public bool flag; public Geo.Corner[] cor = null!; }

        static Ring RingOf(BuildingData b, VoidData v)
        {
            bool ccw = Geo.Area2(v.shape) > 0;
            // the building's outer walls face out of its outline; the courtyard's face into its own, so its winding reads the other way
            return new Ring { Wp = World(b, v.shape), flag = !ccw, cor = Geo.Corners(v.shape, !ccw) };
        }

        /// <summary>The openings along edge i of the courtyard on storey k: its door on the bottom storey, and windows by the style's bays.</summary>
        public static List<Opening> Ops(BuildingData b, VoidData v, int k, int i, double L, double uS, double uE)
        {
            var st = Derived.StyleAt(b, k); double h = Derived.FloorH(b, k), spn = uE - uS;
            var ops = new List<Opening>();
            var (de, dt) = Door(v);
            if (k == v.bottom && i == de && spn > Dim.DOOR_EXT + 0.6)
            {
                double c = Math.Max(uS + Dim.DOOR_EXT / 2 + 0.3, Math.Min(uE - Dim.DOOR_EXT / 2 - 0.3, dt * L));
                ops.Add(new Opening { u0 = c - Dim.DOOR_EXT / 2, u1 = c + Dim.DOOR_EXT / 2, y0 = 0, y1 = Math.Min(2.6, h - 0.4), door = true });
            }
            var ws = Facade.WindowSpec(st.windows, h, st);
            var (n, bay) = Facade.BayLayout(st, uS, uE);
            if (ws != null && ws.y1 - ws.y0 > 0.4 && n > 0)
                for (int j = 0; j < n; j++)
                {
                    double c = uS + (j + 0.5) * bay, ww = ws.full != null ? bay - ws.full.Value : Math.Min(ws.w, bay - 0.5);
                    if (ww < 0.45) continue;
                    double u0 = c - ww / 2, u1 = c + ww / 2; bool blocked = false;
                    foreach (var o in ops) if (o.door && u1 > o.u0 - 0.3 && u0 < o.u1 + 0.3) blocked = true;
                    if (!blocked) ops.Add(new Opening { u0 = u0, u1 = u1, y0 = ws.y0, y1 = ws.y1, full = ws.full != null });
                }
            ops.Sort((p, q) => p.u0.CompareTo(q.u0));
            return ops;
        }

        /// <summary>
        /// The courtyard's facades on storey k. LOD0 (<paramref name="gl"/> given): panels with inner faces, see-through
        /// glass, frames, bands and collision; LOD1: the outside only, with opaque panes.
        /// </summary>
        public static void Walls(MeshBuilder op, MeshBuilder? gl, List<Seg>? sg, BuildingData b, VoidData v, int k, Palette C)
        {
            var g = RingOf(b, v); int n = g.Wp.Count; double T = Dim.T_EXT, y = Derived.FloorBase(b, k), h = Derived.FloorH(b, k);
            bool shell = Derived.ShellAt(b, k), lod1 = gl == null, bands = Derived.StyleAt(b, k).bands && k < b.floors.Count - 1;
            for (int i = 0; i < n; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.flag); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L); var (uS, uE) = Facade.Span(m, e.L);
                var ops = Ops(b, v, k, i, e.L, uS, uE);
                var wc = Geo.WallCtx(e.a, e.u, e.w, e.L);
                op.Ctx(wc, 1); gl?.Ctx(wc, 1);
                if (lod1)
                {
                    Facade.WallPanel(op, F, m, 0, T, y, h, ops, C.wall, C.wall, new Facade.PanelOpt { inner = false, revealFrom = T * 0.45, threshold = k == v.bottom });
                    foreach (var o in ops)
                    {
                        Facade.Pane(op, F, o.u0, o.u1, y + o.y0, y + o.y1, T * 0.45, o.door ? C.door : C.glassDark, false);
                        if (o.door) op.OBox(F, o.u0 - 0.35, o.u1 + 0.35, y + o.y1 + 0.1, y + o.y1 + 0.24, T, T + 1.1, C.trim, C.trim, Skip.In);
                    }
                }
                else
                {
                    Facade.WallPanel(op, F, m, 0, T, y, h, ops, C.wall, C.inner, new Facade.PanelOpt { inner = !shell, threshold = k == v.bottom, revealFrom = shell ? T * 0.45 : 0 });
                    Facade.Dress(op, gl!, F, ops, y, C, shell, Derived.StyleAt(b, k));
                }
                if (bands) { var (bh, bd, bc) = Lod0.Shared.Band(Derived.StyleAt(b, k), C); Facade.StripPieces(op, F, m, new List<(double, double)> { m.Span(T, T + bd) }, y + h - bh, y + h, T, T + bd, bc, bc, Skip.In); }
                if (sg != null)
                {
                    var (cs, ce) = m.Span(T / 2, T / 2);
                    foreach (var (s1, e1) in shell ? new List<(double, double)> { (cs, ce) } : Facade.SolidRanges(cs, ce, ops)) Facade.PushSeg(sg, F, s1, e1, T / 2, T / 2);
                }
            }
        }

        /// <summary>The courtyard's paving on its bottom storey's floor, inside its walls.</summary>
        public static void Pave(MeshBuilder op, BuildingData b, VoidData v, Palette C)
        {
            double y = Derived.FloorBase(b, v.bottom);
            op.Ctx(null, 0);
            foreach (var ring in Offset(World(b, v.shape), -Dim.T_EXT))
            {
                var tris = Triangulate.Shape(ring, new List<List<Vec2>>());
                var pts = new List<P3>(ring.Count); foreach (var p in ring) pts.Add(new P3(p.x, y, p.z));
                op.PolyTris(pts, tris, new P3(0, 1, 0), C.roof);
            }
        }

        /// <summary>A flat roof round a courtyard: the parapet (or a curb), standing on the courtyard's walls.</summary>
        public static void RoofEdge(MeshBuilder op, List<Seg>? sg, BuildingData b, VoidData v, Palette C)
        {
            var g = RingOf(b, v); double y = Derived.RoofY(b), T = Dim.T_EXT; bool parapet = Derived.StyleAt(b, b.floors.Count).parapet;
            for (int i = 0; i < g.Wp.Count; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.flag); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L);
                op.Ctx(Geo.WallCtx(e.a, e.u, e.w, e.L), 1);
                if (parapet)
                {
                    Facade.StripPieces(op, F, m, new List<(double, double)> { m.Span(0, T) }, y, y + 1.0, 0, T, C.wall, C.inner, Skip.Top | Skip.Bot);
                    Facade.StripPieces(op, F, m, new List<(double, double)> { m.Span(-0.04, T + 0.05) }, y + 1.0, y + 1.1, -0.04, T + 0.05, C.trim, null);
                }
                else Facade.StripPieces(op, F, m, new List<(double, double)> { m.Span(0, T) }, y, y + 0.3, 0, T, C.trim, C.trim, Skip.Bot);
                if (sg != null) { var (cs, ce) = m.Span(T / 2, T / 2); Facade.PushSeg(sg, F, cs, ce, T / 2, T / 2); }
            }
        }

        // ---- atria -----------------------------------------------------------------------------

        /// <summary>The atrium's outline as edges facing out of it (onto the floors round it).</summary>
        static Ring Outward(BuildingData b, VoidData v)
        {
            bool ccw = Geo.Area2(v.shape) > 0;
            return new Ring { Wp = World(b, v.shape), flag = ccw, cor = Geo.Corners(v.shape, ccw) };
        }

        /// <summary>Rails round the atrium on storey k, at the slab's edge: a top rail, and posts at most 1.6 m apart.</summary>
        public static void Rails(MeshBuilder op, List<Seg>? sg, BuildingData b, VoidData v, int k, Palette C)
        {
            var g = Outward(b, v); double y = Derived.FloorBase(b, k), w0 = 0.02, w1 = 0.06;
            op.Ctx(null, 0);
            for (int i = 0; i < g.Wp.Count; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.flag); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L);
                Facade.StripPieces(op, F, m, new List<(double, double)> { m.Span(w0, w1) }, y + RailH - 0.07, y + RailH, w0, w1, C.rail, C.rail);
                var (s, en) = m.Span(w0, w1);
                int posts = Math.Max(1, (int)Math.Ceiling((en - s - 0.2) / 1.6));
                for (int j = 0; j <= posts; j++)
                {
                    double u = s + 0.1 + (en - s - 0.2) * j / posts;
                    if (j == posts) continue;   // the next edge's first post stands at this corner
                    op.OBox(F, u - 0.02, u + 0.02, y, y + RailH - 0.08, w0, w1, C.rail, null, Skip.Bot);
                }
                if (sg != null) Facade.PushSeg(sg, F, m.Span(0.04, 0.04).Item1, m.Span(0.04, 0.04).Item2, 0.04, 0.04);
            }
        }

        /// <summary>
        /// The atrium's skylight on a flat roof: a curb round the opening and a glass roof over it. <paramref name="gl"/>
        /// takes the glass (LOD0); without it the glass is opaque (LOD1).
        /// </summary>
        public static void Skylight(MeshBuilder op, MeshBuilder? gl, List<Seg>? sg, BuildingData b, VoidData v, Palette C)
        {
            var g = Outward(b, v); double y = Derived.RoofY(b), w1 = 0.2;
            op.Ctx(null, 0);
            for (int i = 0; i < g.Wp.Count; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.flag); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L);
                Facade.StripPieces(op, F, m, new List<(double, double)> { m.Span(0, w1) }, y, y + CurbH, 0, w1, C.trim, C.trim, Skip.Bot);
                if (sg != null) { var (cs, ce) = m.Span(w1 / 2, w1 / 2); Facade.PushSeg(sg, F, cs, ce, w1 / 2, w1 / 2); }
            }
            var ring = g.Wp; var tris = Triangulate.Shape(ring, new List<List<Vec2>>());
            var pts = new List<P3>(ring.Count); foreach (var p in ring) pts.Add(new P3(p.x, y + CurbH - 0.02, p.z));
            if (gl != null) { gl.Ctx(null, 0); gl.PolyTris(pts, tris, new P3(0, 1, 0), C.glass); gl.PolyTris(pts, tris, new P3(0, -1, 0), C.glass); }
            else op.PolyTris(pts, tris, new P3(0, 1, 0), C.glassDark);
        }

        // ---- pitched roofs -----------------------------------------------------------------------

        /// <summary>
        /// A pitched roof with courtyards through it: the roof is cut back to the courtyards' walls, and walls rise from
        /// their tops to the roof's underside all round each opening, facing into it.
        /// </summary>
        public static List<RoofPart> CutRoof(BuildingData b, List<RoofPart> parts, double top)
        {
            var cuts = new List<List<Vec2>>();
            foreach (var v in b.voids) if (v.kind == VoidKind.Courtyard && HoleAt(b, v, b.floors.Count)) cuts.AddRange(Offset(World(b, v.shape), -Dim.T_EXT));
            if (cuts.Count == 0) return parts;
            var cut = new PathsD(); foreach (var r in cuts) { var p = new PathD(); foreach (var q in r) p.Add(new PointD(q.x, q.z)); cut.Add(p); }
            var roofs = parts.FindAll(p => p.Kind == RoofKind.Roof && !p.Dormer && Math.Abs(p.N.y) > 1e-6);
            var out_ = Roofs.CutParts(parts, cut);
            foreach (var ring in cuts)
            {
                bool ccw = Geo.Area2(ring) > 0; int n = ring.Count;
                for (int i = 0; i < n; i++)
                {
                    var a = ring[i]; var c = ring[(i + 1) % n]; double L = Geo.Hypot(c.x - a.x, c.z - a.z); if (L < 1e-4) continue;
                    var prof = new List<(double t, double y)>();
                    foreach (var p in roofs)
                    {
                        var plan = new List<Vec2>(p.Pts.Count); foreach (var q in p.Pts) plan.Add(new Vec2(q.x, q.z));
                        var o = p.Pts[0];
                        double Y(double x, double z) => o.y - (p.N.x * (x - o.x) + p.N.z * (z - o.z)) / p.N.y;
                        foreach (var (t0, t1) in InsideRanges(plan, a, c))
                        {
                            prof.Add((t0 * L, Y(a.x + (c.x - a.x) * t0, a.z + (c.z - a.z) * t0)));
                            prof.Add((t1 * L, Y(a.x + (c.x - a.x) * t1, a.z + (c.z - a.z) * t1)));
                        }
                    }
                    if (prof.Count < 2) continue;
                    prof.Sort((p, q) => p.t != q.t ? p.t.CompareTo(q.t) : p.y.CompareTo(q.y));
                    var ux = (c.x - a.x) / L; var uz = (c.z - a.z) / L;
                    var nIn = ccw ? new P3(-uz, 0, ux) : new P3(uz, 0, -ux);   // into the courtyard
                    var wp = new List<P3> { new P3(a.x + ux * prof[0].t, top, a.z + uz * prof[0].t) };
                    double lastT = -1, lastY = double.NaN;
                    foreach (var (t, yy) in prof)
                    {
                        double y2 = Math.Max(yy, top);
                        if (Math.Abs(t - lastT) < 1e-6 && Math.Abs(y2 - lastY) < 1e-6) continue;
                        wp.Add(new P3(a.x + ux * t, y2, a.z + uz * t)); lastT = t; lastY = y2;
                    }
                    wp.Add(new P3(a.x + ux * prof[prof.Count - 1].t, top, a.z + uz * prof[prof.Count - 1].t));
                    if (wp.Count >= 3) out_.Add(new RoofPart { Pts = wp, N = nIn, Kind = RoofKind.Wall });
                }
            }
            return out_;
        }
    }
}
