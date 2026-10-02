#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>A bridge as built: where it leaves one building's wall and meets the other's, in world space.</summary>
    public sealed class BridgeSpan
    {
        public BuildingData A = null!, B = null!;
        public BridgeData br = null!;
        /// <summary>Where the bridge's axis leaves A's outer wall face; the axis runs along <see cref="u"/>, <see cref="w"/> to its left.</summary>
        public Vec2 PA, u, w;
        /// <summary>B's outer wall face: a point on it, and its outward normal (facing back along the bridge).</summary>
        public Vec2 PB, nB;
        /// <summary>Along the axis to B's wall face, at the axis and at each side (o = −W/2 and +W/2).</summary>
        public double L, sLeft, sRight;
        public double W, ya, yb;
        /// <summary>The doors: A's storey, edge and position along it; B's.</summary>
        public int edgeA, edgeB;
        public double tA, tB;

        public double Slope => (yb - ya) / L;
        /// <summary>The deck's height a distance s along the axis.</summary>
        public double Y(double s) => ya + (yb - ya) * s / L;
        /// <summary>Where a side line (o across from the axis) meets B's wall face.</summary>
        public double SB(double o) => (L * Dot(u, nB) - o * Dot(w, nB)) / Dot(u, nB);
        public Frame F => new Frame(PA, u, w);
        static double Dot(Vec2 a, Vec2 b) => a.x * b.x + a.z * b.z;

        /// <summary>A world point is on the deck (in plan); s is how far along the axis.</summary>
        public bool OnDeck(double x, double z, out double s)
        {
            double dx = x - PA.x, dz = z - PA.z; s = dx * u.x + dz * u.z; double o = dx * w.x + dz * w.z;
            if (Math.Abs(o) > W / 2 || s < 0) return false;
            // B's end: inside its wall face
            return (x - PB.x) * nB.x + (z - PB.z) * nB.z >= 0;
        }
    }

    /// <summary>
    /// Bridges between buildings (<see cref="BridgeData"/>): a walkway from a point on one building's wall straight out
    /// (along that wall's normal) to the first wall of the other building facing back within 15°, at most 40 m away and
    /// rising at most 1 in 5. A door opens at each end. Enclosed bridges have a glazed side each way and a roof; open ones
    /// a deck with rails. The building it leaves from builds it. Storey's own.
    /// </summary>
    public static class Bridges
    {
        public const double MaxLength = 40, MaxSlope = 0.2, MinLength = 1.0, Deck = 0.3, Inside = 2.7, Roof = 0.2;
        static readonly double Parallel = Math.Cos(15 * Math.PI / 180);

        /// <summary>The nearest wall of an outline to a point (within <paramref name="reach"/>): edge, position along it, the point on it and its outward normal.</summary>
        public static (int i, double t, Vec2 p, Vec2 n)? Wall(List<Vec2> fp, Vec2 at, double reach = 1.0)
        {
            int best = -1; double bd = reach, bt = 0; Vec2 bp = default;
            for (int i = 0; i < fp.Count; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % fp.Count]; var h = Geo.SegDist(at.x, at.z, a.x, a.z, c.x, c.z);
                if (h.d <= bd) { bd = h.d; best = i; double L = Geo.Hypot(c.x - a.x, c.z - a.z); bt = L > 0 ? Geo.Hypot(h.cx - a.x, h.cz - a.z) / L : 0; bp = new Vec2(h.cx, h.cz); }
            }
            if (best < 0) return null;
            var e = Geo.EdgeInfo(fp, best, Geo.Area2(fp) > 0);
            return (best, bt, bp, e.w);
        }

        /// <summary>
        /// Where a bridge from building A's wall at <paramref name="at"/> (local, storey k) would land on building B's storey
        /// <paramref name="toK"/>, or why it can't: its span, or the reason. B's storey <paramref name="toK"/> &lt; 0 picks the
        /// storey nearest A's floor height.
        /// </summary>
        public static (BridgeSpan? span, string? why) Plan(BuildingData A, int k, Vec2 at, BuildingData B, int toK, double width = 2.4)
        {
            if (k < 1 || k >= A.floors.Count) return (null, "Bridges leave from an upper floor");
            var wa = Wall(Derived.OutlineAt(A, k), at); if (wa == null) return (null, "Not on a wall");
            var (ia, ta, pa, na) = wa.Value;
            var PA = new Vec2(pa.x + na.x * Dim.T_EXT + A.pos.x, pa.z + na.z * Dim.T_EXT + A.pos.z);
            // B's walls facing back, crossed by the ray out of A's wall, at B's chosen storey
            int kb = toK; double ya = Derived.FloorBase(A, k);
            if (kb < 0)
            {
                kb = -1; double bd = 1e9;
                for (int j = 1; j < B.floors.Count; j++) { double d = Math.Abs(Derived.FloorBase(B, j) - ya); if (d < bd) { bd = d; kb = j; } }
                if (kb < 0) return (null, B.name + " has no upper floor to meet");
            }
            if (kb < 1 || kb >= B.floors.Count) return (null, B.name + " has no such floor");
            var fb = Derived.OutlineAt(B, kb); bool bccw = Geo.Area2(fb) > 0;
            double bestS = 1e9; int ib = -1; double tb = 0; Vec2 nb = default, PB = default;
            for (int i = 0; i < fb.Count; i++)
            {
                var e = Geo.EdgeInfo(fb, i, bccw);
                if (e.w.x * na.x + e.w.z * na.z > -Parallel) continue;   // not facing back along the bridge
                // B's outer face line: the edge moved out by the wall's thickness, in world space
                var q0 = new Vec2(fb[i].x + e.w.x * Dim.T_EXT + B.pos.x, fb[i].z + e.w.z * Dim.T_EXT + B.pos.z);
                double den = na.x * e.u.z - na.z * e.u.x; if (Math.Abs(den) < 1e-9) continue;
                double s = ((q0.x - PA.x) * e.u.z - (q0.z - PA.z) * e.u.x) / den, along = ((q0.x - PA.x) * na.z - (q0.z - PA.z) * na.x) / den;
                // the axis must meet the edge, and the bridge's width fit on it
                double t = along / e.L, half = width / 2 / e.L;
                if (s <= 0 || s >= bestS || t < half || t > 1 - half) continue;
                bestS = s; ib = i; tb = t; nb = e.w; PB = new Vec2(PA.x + na.x * s, PA.z + na.z * s);
            }
            if (ib < 0) return (null, $"{B.name} has no wall facing this one straight across");
            if (bestS < MinLength) return (null, "The buildings are too close for a bridge");
            if (bestS > MaxLength) return (null, $"Too long: {bestS:0} m (at most {MaxLength:0} m)");
            double yb = Derived.FloorBase(B, kb);
            if (Math.Abs(yb - ya) > MaxSlope * bestS) return (null, $"Too steep: the floors are {Math.Abs(yb - ya):0.0} m apart over {bestS:0.0} m");
            var span = new BridgeSpan
            {
                A = A, B = B, PA = PA, u = na, w = new Vec2(-na.z, na.x), PB = PB, nB = nb, L = bestS, W = Math.Max(1.5, Math.Min(4, width)),
                ya = ya, yb = yb, edgeA = ia, tA = ta, edgeB = ib, tB = tb,
            };
            span.sLeft = span.SB(-span.W / 2); span.sRight = span.SB(span.W / 2);
            return (span, null);
        }

        /// <summary>A bridge as it stands in the layout now, or null when it no longer meets (memoised per site).</summary>
        public static BridgeSpan? Span(Site site, BuildingData A, BridgeData br)
        {
            string key = A.id + "/" + br.id;
            if (site.bridgeMemo.TryGetValue(key, out var m)) return m;
            BridgeSpan? s = null;
            var B = site.ById(br.to);
            if (B != null && !ReferenceEquals(A, B))
            {
                s = Plan(A, br.k, br.at, B, br.toK, br.width).span;
                if (s != null) s.br = br;
            }
            return site.bridgeMemo[key] = s;
        }

        /// <summary>Every bridge that stands, from or to building b.</summary>
        public static List<BridgeSpan> Touching(Site site, BuildingData b)
        {
            var o = new List<BridgeSpan>();
            foreach (var a in site.Buildings)
                foreach (var br in a.bridges)
                    if (ReferenceEquals(a, b) || br.to == b.id) { var s = Span(site, a, br); if (s != null) o.Add(s); }
            return o;
        }

        /// <summary>The doors bridges open in building b's walls: storey, edge and position along it.</summary>
        public static List<(int k, int edge, double t)> Doors(Site site, BuildingData b)
        {
            var o = new List<(int, int, double)>();
            if (b.bridges.Count == 0 && !site.Bridged(b.id)) return o;
            foreach (var s in Touching(site, b))
            {
                if (ReferenceEquals(s.A, b)) o.Add((s.br.k, s.edgeA, s.tA));
                if (ReferenceEquals(s.B, b)) o.Add((s.br.toK, s.edgeB, s.tB));
            }
            return o;
        }

        /// <summary>
        /// The bridge's geometry: deck, sides (glass and mullions, or rails), roof. <paramref name="gl"/> takes the glass
        /// (LOD0); without it the glass is opaque (LOD1). <paramref name="extra"/> takes the sides' collision.
        /// </summary>
        public static void Build(MeshBuilder op, MeshBuilder? gl, List<(Seg, double, double)>? extra, BridgeSpan s, Palette C)
        {
            var F = s.F; double hw = s.W / 2, sl = s.Slope;
            P3 At(double a, double o, double dy) => F.At(a, s.Y(a) + dy, o);
            double SE(double o) => s.SB(o);   // where a side line ends at B's wall
            var up = new P3(-sl * s.u.x, 1, -sl * s.u.z).Normalized; var down = up * -1;
            var left = new P3(s.w.x, 0, s.w.z); var right = left * -1;
            op.Ctx(null, 0);
            // the deck: its walking surface (the floor's colour inside an enclosed bridge), sides and underside
            void Slab(double y0, double y1, Swatch top, Swatch bottom, Swatch side)
            {
                op.Poly(new[] { At(0, -hw, y1), At(SE(-hw), -hw, y1), At(SE(hw), hw, y1), At(0, hw, y1) }, up, top);
                op.Poly(new[] { At(0, -hw, y0), At(0, hw, y0), At(SE(hw), hw, y0), At(SE(-hw), -hw, y0) }, down, bottom);
                op.Poly(new[] { At(0, -hw, y0), At(SE(-hw), -hw, y0), At(SE(-hw), -hw, y1), At(0, -hw, y1) }, right, side);
                op.Poly(new[] { At(0, hw, y0), At(0, hw, y1), At(SE(hw), hw, y1), At(SE(hw), hw, y0) }, left, side);
            }
            Slab(-Deck, 0, s.br.open ? C.roof : C.floor, C.ceil, C.trim);
            double sideW = 0.05;
            if (!s.br.open)
            {
                Slab(Inside, Inside + Roof, C.roof, C.ceil, C.trim);
                foreach (double o in new[] { -hw + sideW, hw - sideW })
                {
                    double e = SE(o); var n = o < 0 ? right : left;
                    // glass from a 10 cm upstand to the roof, with mullions about every 1.5 m
                    var pane = new List<P3> { At(0, o, 0.1), At(e, o, 0.1), At(e, o, Inside), At(0, o, Inside) };
                    var tris = new List<int> { 0, 1, 2, 0, 2, 3 };
                    if (gl != null) { gl.Ctx(null, 0); gl.PolyTris(pane, tris, n, C.glass); gl.PolyTris(pane, tris, n * -1, C.glass); }
                    else op.PolyTris(pane, tris, n, C.glassDark);
                    op.Poly(new[] { At(0, o, 0), At(e, o, 0), At(e, o, 0.1), At(0, o, 0.1) }, n, C.trim);
                    int mul = Math.Max(1, (int)Math.Round(e / 1.5));
                    for (int j = 1; j < mul; j++)
                    {
                        double a = e * j / mul;
                        op.OBox(F, a - 0.04, a + 0.04, s.Y(a) + 0.1, s.Y(a) + Inside, o - 0.03, o + 0.03, C.trim, null, Skip.Top | Skip.Bot);
                    }
                    if (extra != null) Side(extra, s, o, Inside);
                }
            }
            else
                foreach (double o in new[] { -hw + sideW, hw - sideW })
                {
                    double e = SE(o);
                    // a top rail and posts about every 1.5 m
                    op.Poly(new[] { At(0, o - 0.03, 1.02), At(e, o - 0.03, 1.02), At(e, o + 0.03, 1.02), At(0, o + 0.03, 1.02) }, up, C.rail);
                    op.Poly(new[] { At(0, o - 0.03, 0.95), At(0, o + 0.03, 0.95), At(e, o + 0.03, 0.95), At(e, o - 0.03, 0.95) }, down, C.rail);
                    op.Poly(new[] { At(0, o + 0.03, 0.95), At(0, o + 0.03, 1.02), At(e, o + 0.03, 1.02), At(e, o + 0.03, 0.95) }, left, C.rail);
                    op.Poly(new[] { At(0, o - 0.03, 0.95), At(e, o - 0.03, 0.95), At(e, o - 0.03, 1.02), At(0, o - 0.03, 1.02) }, right, C.rail);
                    int posts = Math.Max(1, (int)Math.Ceiling(e / 1.5));
                    for (int j = 0; j <= posts; j++)
                    {
                        double a = Math.Max(0.05, Math.Min(e - 0.05, e * j / posts));
                        op.OBox(F, a - 0.025, a + 0.025, s.Y(a), s.Y(a) + 0.94, o - 0.025, o + 0.025, C.rail, null, Skip.Bot);
                    }
                    if (extra != null) Side(extra, s, o, 1.02);
                }
        }

        static void Side(List<(Seg, double, double)> extra, BridgeSpan s, double o, double h)
        {
            var a = s.F.At2(0, o); var c = s.F.At2(s.SB(o), o);
            extra.Add((new Seg(a.x, a.z, c.x, c.z, 0.05), Math.Min(s.ya, s.yb) - 0.5, Math.Max(s.ya, s.yb) + h));
        }

        /// <summary>The bridge's walking surface at a world point, if it is on the deck.</summary>
        public static double? DeckAt(BridgeSpan s, double x, double z) => s.OnDeck(x, z, out double a) ? s.Y(a) : (double?)null;

        /// <summary>The bridge's corners in plan (world), for bounds.</summary>
        public static IEnumerable<Vec2> Corners(BridgeSpan s)
        {
            double hw = s.W / 2;
            yield return s.F.At2(0, -hw); yield return s.F.At2(0, hw); yield return s.F.At2(s.sRight, hw); yield return s.F.At2(s.sLeft, -hw);
        }
    }
}
