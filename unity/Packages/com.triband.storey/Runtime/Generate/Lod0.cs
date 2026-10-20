#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>What LOD0 generation produces for one building.</summary>
    public sealed class Lod0Result
    {
        public MeshBuilder Op = null!;
        public MeshBuilder Glass = null!;
        /// <summary>Collision segments per storey (index = floor; the last entry is the roof).</summary>
        public List<List<Seg>> Segs = new List<List<Seg>>();
        /// <summary>Collision segments outside the storeys (a bridge's sides), each with the heights it stands between.</summary>
        public List<(Seg s, double y0, double y1)> Extra = new List<(Seg, double, double)>();
        /// <summary>What is kept of it once the meshes are uploaded (made with the result: its lists, not copies).</summary>
        public Lod0Shape Shape = null!;
    }

    /// <summary>
    /// What is read of a building's LOD0 after its meshes are uploaded (docs/CITY.md §7): the collision segments (the walk
    /// model, colliders, the problem list) and the walls given ids (the cutaway). Tens of kilobytes, about 1% of the LOD0's
    /// GPU meshes, where the generator's whole result is several megabytes. <see cref="Lod0.BuildShape"/> makes it
    /// without the meshes.
    /// </summary>
    public sealed class Lod0Shape
    {
        /// <summary>Collision segments per storey (index = floor; the last entry is the roof).</summary>
        public readonly List<List<Seg>> Segs;
        /// <summary>Collision segments outside the storeys (a bridge's sides), each with the heights it stands between.</summary>
        public readonly List<(Seg s, double y0, double y1)> Extra;
        /// <summary>The walls given ids, in id order (wall i carries id i + 1 in the mesh): the cutaway's data and kind.</summary>
        public readonly List<(double[] W, int K)> Walls;
        /// <summary>The floor height of the storey each wall was made on.</summary>
        public readonly List<double> WallBase;

        /// <summary>The lists themselves, trimmed to their length: nothing is added to a built building's.</summary>
        public Lod0Shape(List<List<Seg>> segs, List<(Seg s, double y0, double y1)> extra, List<(double[] W, int K)> walls, List<double> wallBase)
        {
            foreach (var l in segs) l.TrimExcess();
            segs.TrimExcess(); extra.TrimExcess(); walls.TrimExcess(); wallBase.TrimExcess();
            Segs = segs; Extra = extra; Walls = walls; WallBase = wallBase;
        }
    }

    /// <summary>
    /// LOD0: everything. Interior, frames, see-through glass; one opaque mesh and one glass mesh per
    /// building. A port of the prototype's <c>lod0Steps</c>, storey by storey, so a caller can spread a
    /// building over several frames.
    /// </summary>
    public static class Lod0
    {
        /// <summary>Build a building's LOD0. <paramref name="solids"/> records volumes for the coplanar checker.</summary>
        public static Lod0Result Build(Site site, BuildingData b, bool solids = false) => Make(site, b, solids, false, null)!;

        /// <summary>
        /// <see cref="Build(Site, BuildingData, bool)"/>, asking <paramref name="stop"/> after each storey (docs/PERFORMANCE.md
        /// §9.2): true stops it, giving back what it made, and the build is null. A caller on a build thread runs more urgent
        /// builds there too (<see cref="Lod.BuildThreads.RunMoreUrgent"/>). The same meshes when it isn't stopped.
        /// </summary>
        public static Lod0Result? Build(Site site, BuildingData b, Func<bool> stop) => Make(site, b, false, false, stop);

        /// <summary>
        /// A building's LOD0 shape alone: the segments and walls <see cref="Build"/> makes, the same to the last bit,
        /// without its meshes. For the walk model when no renderer has built the building (docs/PLAY.md §1).
        /// </summary>
        public static Lod0Shape BuildShape(Site site, BuildingData b) => Make(site, b, false, true, null)!.Shape;

        static Lod0Result? Make(Site site, BuildingData b, bool solids, bool shapeOnly, Func<bool>? stop)
        {
            var self = new Shared(site, b);
            int idx = site.IndexOf(b);
            var r = new Lod0Result { Op = MeshBuilder.Make(idx, shapeOnly: shapeOnly), Glass = MeshBuilder.Make(idx, shapeOnly: shapeOnly) };
            r.Glass.Walls = r.Op.Walls;   // glass shares the wall ids of the walls it sits in
            r.Glass.WallBase = r.Op.WallBase;
            if (solids) r.Op.Solids = new List<Solid>();
            foreach (var _ in self.Steps(r))
                if (stop != null && stop()) { r.Op.Recycle(); r.Glass.Recycle(); return null; }
            r.Shape = new Lod0Shape(r.Segs, r.Extra, r.Op.Walls, r.Op.WallBase);
            return r;
        }

        /// <summary>Outline geometry and colours of one setback tier.</summary>
        public sealed class TierCtx { public List<Vec2> fp = null!; public bool ccw; public List<Vec2> Wp = null!; public Geo.Corner[] cor = null!; public Palette C = null!; }

        /// <summary>The storey builders, shared by the three LODs of one building.</summary>
        public sealed class Shared
        {
        readonly Site site; readonly BuildingData b; readonly int N; readonly bool shell;
        readonly Dictionary<int, TierCtx> tiers = new Dictionary<int, TierCtx>();
        // what one wall's facade works in, used again wall after wall (a wall's openings are let go before the next's):
        // its openings, those of the piece being built, and the panel's options
        internal readonly OpeningPool WallPool = new OpeningPool();
        internal readonly List<Opening> WallOps = new List<Opening>(), PieceOps = new List<Opening>(), CutOps = new List<Opening>();
        internal readonly Facade.PanelOpt Panel = new Facade.PanelOpt();
        List<BridgeSpan>? bridges;   // the bridges that meet this building, looked up when a band first asks
        static readonly List<BridgeSpan> NoBridges = new List<BridgeSpan>();

        /// <summary>The openings of a piece of wall: those of <paramref name="all"/> between lo and hi, in order (none on a party wall).</summary>
        internal List<Opening> Piece(List<Opening> all, bool party, double lo, double hi)
        {
            PieceOps.Clear();
            if (!party) foreach (var o in all) if (o.u0 >= lo && o.u1 <= hi) PieceOps.Add(o);
            return PieceOps;
        }

        public Shared(Site site, BuildingData b) { this.site = site; this.b = b; N = b.floors.Count; shell = !b.interior; }

        public TierCtx At(int k)
        {
            int j = Derived.TierStart(b, k);
            if (!tiers.TryGetValue(j, out var g))
            {
                var fp = Derived.OutlineAt(b, k);
                var Wp = new List<Vec2>(fp.Count); foreach (var p in fp) Wp.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z));
                bool ccw = Geo.Area2(fp) > 0;
                tiers[j] = g = new TierCtx { fp = fp, ccw = ccw, Wp = Wp, cor = Geo.Corners(fp, ccw), C = new Palette(new StyleRef(site.IndexOf(b), j)) };
            }
            return g;
        }

        public IEnumerable<int> Steps(Lod0Result r)
        {
            var op = r.Op; var gl = r.Glass;
            // the rooms (MeshBuilder.Inside): what a storey shows only through its windows, which LOD0's far windows paint over:
            // the walls' inner faces, the partitions and the cores. The floors and ceilings stay with the shell, which casts
            // shadows (the rooms only for the building the player is in): without them the sun would light every storey
            // through the windows of those above. What the roof shows at any distance stays with the shell too: every storey
            // under an atrium's skylight or a straight flight's railed opening (stacked over the flights), and the top storey
            // under a bulkhead's door
            bool open = false, bulkhead = false;
            if (!Roofs.IsPitched(b)) foreach (var v in b.voids) if (v.kind == VoidKind.Atrium && Courtyards.HoleAt(b, v, N)) open = true;
            foreach (var s in b.shafts) if (Cores.StairHoleAt(b, s, N)) { if (s.type == CoreType.Flight) open = true; else bulkhead = true; }
            // and what a storey's openings show takes in what its stairs open onto: a ray in through a walk-in storey's
            // doorway goes on up the stairwell. A storey that shows its rooms shows the next storey's up or down a switchback
            // stair (its flights and landings close the well beyond), and every storey up a straight flight's stack
            bool shownBelow = false; (int ranges, int end)? markBelow = null;
            bool Up(int k, bool straight) { foreach (var s in b.shafts) if (Cores.HasFlight(b, s, k) && (!straight || s.type == CoreType.Flight)) return true; return false; }
            for (int k = 0; k <= N; k++)
            {
                var sg = new List<Seg>(); var g = At(k); bool sb = Derived.IsSetback(b, k); var Cb = At(Math.Max(0, k - 1)).C;
                op.StoreyBase = gl.StoreyBase = Derived.FloorBase(b, k);
                // a filled storey is built as a shell-only building's storeys are; a slab between two of them is not needed
                bool sh = Derived.ShellAt(b, k), shBelow = Derived.ShellAt(b, k - 1);
                if ((!(sh && shBelow) || k == N || sb) && !op.ShapeOnly) Slab(op, k, Cb, sh, false);   // no walls or segments: none for a shape
                if (sb) { if (Roofs.Draw(op, Roofs.TerraceRoof(site, b, k), Cb)) TerraceTops(op, k, Cb); else Terrace(op, sg, k, Cb); Overhang(op, k, g.C); }
                foreach (var v in b.voids) if (v.kind == VoidKind.Courtyard && k == v.bottom && Courtyards.Live(b, v)) Courtyards.Pave(op, b, v, g.C);
                // the storey's rooms are kept apart unless one of its openings shows them at any distance (a doorway, an
                // artist's glass), which is known once its walls are made: until then they are only marked
                bool apart = k < N && !open && !(bulkhead && k == N - 1) && !shownBelow; var mark = op.InsideMark; int seen = op.SeeThrough;
                if (k < N)
                {
                    FacadeStorey(op, gl, sg, k, g, apart); Details.Build(op, site, b, k, g.C, 0);
                    foreach (var br in b.bridges) if (br.k == k && Bridges.Span(site, b, br) is BridgeSpan bs) Bridges.Build(op, gl, r.Extra, bs, g.C);
                    foreach (var v in b.voids) if (Courtyards.WallsAt(b, v, k)) Courtyards.Walls(op, gl, sg, b, v, k, g.C, apart);
                    if (!sh) { op.Inside = apart; Interior(op, sg, k, g.C); foreach (var v in b.voids) if (Courtyards.RailsAt(b, v, k)) Courtyards.Rails(op, sg, b, v, k, g.C); op.Inside = false; }
                }
                else
                {
                    Roof(op, sg, g, Cb);
                    if (!Roofs.IsPitched(b)) foreach (var v in b.voids) if (v.kind == VoidKind.Atrium && Courtyards.HoleAt(b, v, N)) Courtyards.Skylight(op, gl, sg, b, v, Cb);
                }
                if (!shell && !Derived.Filled(b, k)) Shafts(op, sg, k, g.C, apart);
                bool shows = op.SeeThrough != seen;
                if (shows && markBelow is { } below && Up(k - 1, false)) op.TakeBackInside(below);   // the storey below too, down its stairs
                else if (shows && apart) op.TakeBackInside(mark);
                shownBelow = shows && Up(k, false) || shownBelow && Up(k, true);
                markBelow = apart && !shows ? mark : ((int, int)?)null;
                r.Segs.Add(sg);
                yield return k;
            }
        }

        // ---- slabs ------------------------------------------------------------------------

        /// <summary>
        /// Slab at level k. Where a setback starts, the floor covers the new outline and the rest of the
        /// storey below becomes terrace deck; the underside always covers the storey below.
        /// </summary>
        public void Slab(MeshBuilder op, int k, Palette C, bool shell, bool topOnly)
        {
            double y = Derived.FloorBase(b, k), ox = b.pos.x, oz = b.pos.z;
            var below = k > 0 ? Derived.OutlineAt(b, k - 1) : null; var above = k < N ? Derived.OutlineAt(b, k) : null;
            bool step = below != null && above != null && !ReferenceEquals(below, above);
            var holes = new List<List<Vec2>>(); var flightHoles = new List<List<Vec2>>();
            if (!shell && !topOnly)
                foreach (var s in b.shafts)
                    if (Cores.StairHoleAt(b, s, k))
                    {
                        var f = Cores.FrameOf(b, s);
                        if (s.type == CoreType.Flight)
                        {
                            // over the flight lane's run only: the landings and the walkway are this floor
                            double hw = Dim.FLIGHT_W / 2, xm = -hw + Dim.FLIGHT_LANE, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
                            var fh = new List<Vec2> { f.At2(-hw, z0), f.At2(xm, z0), f.At2(xm, z1), f.At2(-hw, z1) };
                            holes.Add(fh); flightHoles.Add(fh);
                        }
                        else holes.Add(new List<Vec2> { f.At2(-1.3, -1.6), f.At2(1.3, -1.6), f.At2(1.3, 2.6), f.At2(-1.3, 2.6) });
                    }
            List<Vec2> V2(List<Vec2> pts) { var o = new List<Vec2>(pts.Count); foreach (var p in pts) o.Add(new Vec2(p.x + ox, p.z + oz)); return o; }
            // courtyards and atria cut every slab above their bottom; a courtyard's paving takes its bottom floor's place
            var edged = new List<List<Vec2>>(holes);   // openings whose slab edge shows: stairs, and atria
            var paved = new List<List<Vec2>>();
            foreach (var v in b.voids)
            {
                if (Courtyards.HoleAt(b, v, k)) { var w = V2(v.shape); holes.Add(w); if (v.kind == VoidKind.Atrium) edged.Add(w); }
                else if (v.kind == VoidKind.Courtyard && k == v.bottom && Courtyards.Live(b, v)) paved.Add(V2(v.shape));
            }
            var floorHoles = paved.Count == 0 ? holes : new List<List<Vec2>>(holes); floorHoles.AddRange(paved);
            void Surf(List<Vec2> contour, List<List<Vec2>> hs, double yy, P3 nr, Swatch c)
            {
                var tris = Triangulate.Shape(contour, hs);
                var pts = new List<P3>();
                foreach (var p in contour) pts.Add(new P3(p.x, yy, p.z));
                foreach (var h in hs) foreach (var p in h) pts.Add(new P3(p.x, yy, p.z));
                op.PolyTris(pts, tris, nr, c);
            }
            var side = C.slabSide; double yb = k == 0 ? y - 0.05 : y - Dim.SLAB;
            var outer = V2(below ?? above!);
            op.Ctx(null, 0);
            if (op.Solids != null)
            {
                op.Solids.Add(new Solid { Prism = outer, y0 = yb, y1 = y });
                if (step) op.Solids.Add(new Solid { Prism = V2(above!), y0 = yb, y1 = y });
            }
            var up = new P3(0, 1, 0); var down = new P3(0, -1, 0);
            if (!step) Surf(outer, floorHoles, y, up, k == N ? C.roof : C.floor);
            else
            {
                // a stair opening out on the terrace (straight flights coming up onto it): the deck is cut round it, and the
                // floor above the setback leaves it out. Only then: other slabs triangulate exactly as before
                var top = V2(above!); var onDeck = new List<List<Vec2>>();
                foreach (var h in holes) { double cx = 0, cz = 0; foreach (var p in h) { cx += p.x; cz += p.z; } if (!Geo.Pip(top, cx / h.Count, cz / h.Count)) onDeck.Add(h); }
                if (!shell) Surf(top, onDeck.Count == 0 ? floorHoles : floorHoles.FindAll(h => !onDeck.Contains(h)), y, up, C.floor);
                if (onDeck.Count == 0)
                    foreach (var poly in Geo.TerracePolys(below!, above!)) Surf(V2(poly[0]), poly.GetRange(1, poly.Count - 1).ConvertAll(V2), y, up, C.roof);
                else
                {
                    var deck = new Clipper2Lib.PathsD();
                    foreach (var poly in Geo.TerracePolys(below!, above!)) foreach (var ring in poly) deck.Add(Path(V2(ring)));
                    var cutOut = new Clipper2Lib.PathsD(); foreach (var h in onDeck) cutOut.Add(Path(h));
                    var tree = new Clipper2Lib.PolyTreeD();
                    Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Difference, deck, cutOut, tree, Clipper2Lib.FillRule.EvenOdd, 6);
                    foreach (var (ring, inner) in Rings(tree)) Surf(ring, inner, y, up, C.roof);
                }
            }
            if (k > 0 && !topOnly) Surf(outer, holes, yb, down, C.ceil);
            if (step) foreach (var poly in Geo.TerracePolys(above!, below!)) Surf(V2(poly[0]), poly.GetRange(1, poly.Count - 1).ConvertAll(V2), yb, down, C.ceil);   // soffit under an overhang
            // a straight flight's opening along the building's outline: that side is the wall's inner face already (the
            // walls run down through the slab), and drawn again it would flicker with it. (Only Storey's own straight
            // flights: the prototype draws the side for switchback stairs and atria, which match it face for face)
            var rims = new List<List<Vec2>> { outer }; if (step) rims.Add(V2(above!));
            bool OnRim(Vec2 a, Vec2 c)
            {
                foreach (var r in rims)
                    for (int j = 0; j < r.Count; j++)
                    {
                        var p = r[j]; var q = r[(j + 1) % r.Count];
                        if (Geo.SegDist(a.x, a.z, p.x, p.z, q.x, q.z).d < 1e-4 && Geo.SegDist(c.x, c.z, p.x, p.z, q.x, q.z).d < 1e-4) return true;
                    }
                return false;
            }
            foreach (var h in edged)
            {
                double cx = 0, cz = 0; foreach (var p in h) { cx += p.x; cz += p.z; } cx /= h.Count; cz /= h.Count;
                for (int i = 0; i < h.Count; i++)
                {
                    var a = h[i]; var c = h[(i + 1) % h.Count];
                    if (flightHoles.Contains(h) && OnRim(a, c)) continue;
                    double dx = c.x - a.x, dz = c.z - a.z, L = Geo.Hypot(dx, dz); if (L == 0) L = 1;
                    double nx = dz / L, nz = -dx / L, mx = (a.x + c.x) / 2 - cx, mz = (a.z + c.z) / 2 - cz;
                    if ((nx * mx + nz * mz > 0) == true) { nx = -nx; nz = -nz; }   // inward
                    op.Quad(new P3(a.x, yb, a.z), new P3(c.x, yb, c.z), new P3(c.x, y, c.z), new P3(a.x, y, a.z), new P3(nx, 0, nz), side);
                }
            }
        }

        static Clipper2Lib.PathD Path(List<Vec2> pts) { var o = new Clipper2Lib.PathD(pts.Count); foreach (var p in pts) o.Add(new Clipper2Lib.PointD(p.x, p.z)); return o; }

        /// <summary>A clipped region's polygons, each its outer ring and its holes.</summary>
        static List<(List<Vec2> ring, List<List<Vec2>> holes)> Rings(Clipper2Lib.PolyPathD node)
        {
            var o = new List<(List<Vec2>, List<List<Vec2>>)>();
            static List<Vec2> V(Clipper2Lib.PathD p) { var r = new List<Vec2>(p.Count); foreach (var q in p) r.Add(new Vec2(q.x, q.y)); return r; }
            for (int i = 0; i < node.Count; i++)
            {
                var outer = (Clipper2Lib.PolyPathD)node[i]; if (outer.Polygon == null || outer.Polygon.Count < 3) continue;
                var hs = new List<List<Vec2>>();
                for (int j = 0; j < outer.Count; j++) { var h = (Clipper2Lib.PolyPathD)outer[j]; if (h.Polygon != null && h.Polygon.Count >= 3) hs.Add(V(h.Polygon)); o.AddRange(Rings(h)); }
                o.Add((V(outer.Polygon), hs));
            }
            return o;
        }

        // ---- terraces and overhangs ------------------------------------------------------

        public sealed class Run { public Vec2 a, u, w; public double L; public Cut S, E; }

        /// <summary>Where a parapet run ends against the setback's wall: the outer face of that wall.</summary>
        static Cut? ParapetEnd(List<Vec2> up, bool uccw, Vec2 V, Vec2 U, Vec2 Wn, double uV, int dir)
        {
            for (int j = 0; j < up.Count; j++)
            {
                var p = up[j]; var q = up[(j + 1) % up.Count];
                if (Geo.SegDist(V.x, V.z, p.x, p.z, q.x, q.z).d > 0.02) continue;
                double L = Geo.Hypot(q.x - p.x, q.z - p.z); if (L == 0) L = 1;
                double dux = (q.x - p.x) / L, duz = (q.z - p.z) / L;
                if (Math.Abs(dux * U.z - duz * U.x) < 0.2) continue;
                double nx = uccw ? duz : -duz, nz = uccw ? -dux : dux;
                double nu = nx * U.x + nz * U.z, nw = nx * Wn.x + nz * Wn.z;
                if (nu * dir < 0.2) continue;
                return new Cut(uV + Dim.T_EXT / nu, -nw / nu);
            }
            return null;
        }

        /// <summary>Strips along the parts of A's edges outside B; they end on A's mitre at an open corner, or against B's wall.</summary>
        public List<Run> StripRuns(List<Vec2> A, List<Vec2> B, int? kA)
        {
            var lo = A; var up = B; bool lccw = Geo.Area2(lo) > 0, uccw = Geo.Area2(up) > 0; int n = lo.Count;
            var Wp = new List<Vec2>(n); foreach (var p in lo) Wp.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z));
            var cor = Geo.Corners(lo, lccw);
            var ex = new List<List<(double, double)>>(n);
            for (int i = 0; i < n; i++) ex.Add(kA == null ? Geo.ExposedRanges(lo, up, i) : Geo.MinusRanges(Geo.ExposedRanges(lo, up, i), Party.Skips(site, b, kA.Value, i, Derived.FloorBase(b, kA.Value))));
            var out_ = new List<Run>();
            for (int i = 0; i < n; i++)
            {
                var g = Geo.EdgeInfo(Wp, i, lccw); var m = Geo.MiterOf(cor, i, g.L); int ip = (i - 1 + n) % n;
                foreach (var (s0, e0) in ex[i])
                {
                    bool open0 = s0 < 0.02 && ex[ip].Exists(r => r.Item2 > Geo.EdgeLen(lo, ip) - 0.02), open1 = e0 > g.L - 0.02 && ex[(i + 1) % n].Exists(r => r.Item1 < 0.02);
                    Vec2 V(double t) => new Vec2(lo[i].x + g.u.x * t, lo[i].z + g.u.z * t);
                    var S = open0 ? m.S : (ParapetEnd(up, uccw, V(s0), g.u, g.w, s0, 1) ?? new Cut(s0, 0));
                    var E = open1 ? m.E : (ParapetEnd(up, uccw, V(e0), g.u, g.w, e0, -1) ?? new Cut(e0, 0));
                    out_.Add(new Run { a = g.a, u = g.u, w = g.w, L = g.L, S = S, E = E });
                }
            }
            return out_;
        }

        public List<Run> TerraceRuns(int k) => StripRuns(Derived.OutlineAt(b, k - 1), Derived.OutlineAt(b, k), Derived.TierStart(b, k - 1));
        public List<Run> OverhangRuns(int k) => StripRuns(Derived.OutlineAt(b, k), Derived.OutlineAt(b, k - 1), null);

        public void Overhang(MeshBuilder op, int k, Palette C)
        {
            double y = Derived.FloorBase(b, k), T = Dim.T_EXT;
            foreach (var r in OverhangRuns(k))
            {
                op.Ctx(Geo.WallCtx(new Vec2(r.a.x + r.u.x * r.S.s, r.a.z + r.u.z * r.S.s), r.u, r.w, Math.Max(r.E.s - r.S.s, 0.05)), 1);
                op.MBox(new Frame(r.a, r.u, r.w), r.S.s, r.S.k, r.E.s, r.E.k, y - Dim.SLAB, y, 0, T, C.wall, C.wall, Skip.Top | Skip.In | Skip.UStart | Skip.UEnd);
            }
        }

        public void Terrace(MeshBuilder op, List<Seg>? sg, int k, Palette C)
        {
            double y = Derived.FloorBase(b, k), T = Dim.T_EXT;
            foreach (var r in TerraceRuns(k))
            {
                var F = new Frame(r.a, r.u, r.w);
                op.Ctx(Geo.WallCtx(new Vec2(r.a.x + r.u.x * r.S.s, r.a.z + r.u.z * r.S.s), r.u, r.w, Math.Max(r.E.s - r.S.s, 0.05)), 1);
                op.MBox(F, r.S.s, r.S.k, r.E.s, r.E.k, y, y + 1.0, 0, T, C.wall, C.inner, Skip.Top | Skip.Bot | Skip.UStart | Skip.UEnd);
                op.MBox(F, r.S.s, r.S.k, r.E.s, r.E.k, y + 1.0, y + 1.1, -0.04, T, C.trim, C.trim, Skip.UStart | Skip.UEnd);
                if (sg != null) Facade.PushSeg(sg, F, r.S.s + r.S.k * T / 2, r.E.s + r.E.k * T / 2, T / 2, T / 2);
            }
        }

        /// <summary>
        /// Under a terrace's pitched roof, the tops of the walls it sits on, by Unity only (<see cref="MeshBuilder.Backing"/>):
        /// where a flat terrace has its parapet. As <see cref="WallTops"/>.
        /// </summary>
        public void TerraceTops(MeshBuilder op, int k, Palette C)
        {
            if (op.ShapeOnly) return;
            double y = Derived.FloorBase(b, k), T = Dim.T_EXT;
            op.Ctx(null, 0); op.Backing = true;
            foreach (var r in TerraceRuns(k))
            {
                var F = new Frame(r.a, r.u, r.w); double s1 = r.S.At(T), e1 = r.E.At(T);
                if (r.E.s - r.S.s < 1e-4 || e1 - s1 < 1e-4) continue;
                op.Quad(F.At(r.S.s, y, 0), F.At(r.E.s, y, 0), F.At(e1, y, T), F.At(s1, y, T), new P3(0, 1, 0), C.roof);
            }
            op.Backing = false;
        }

        // ---- facade -------------------------------------------------------------------------

        /// <summary>LOD0 facade of one storey: panels with window and door holes, recessed panes, frames and trims.</summary>
        /// <param name="rooms">The walls' inner faces are the rooms' (<see cref="MeshBuilder.Inside"/>), and what only clear glass
        /// shows from outside (the slab's edges, the strips' backs).</param>
        public void FacadeStorey(MeshBuilder op, MeshBuilder gl, List<Seg> sg, int k, TierCtx g, bool rooms = false)
        {
            var Wp = g.Wp; var C = g.C; int n = Wp.Count; bool shell = Derived.ShellAt(b, k); double h = Derived.FloorH(b, k), y = Derived.FloorBase(b, k), T = Dim.T_EXT;   // shell: this storey (a filled one is closed as a shell building is)
            for (int i = 0; i < n; i++)
            {
                var e = Geo.EdgeInfo(Wp, i, g.ccw); var F = e.F;
                var m = Geo.MiterOf(g.cor, i, e.L); var (uS, uE) = Facade.Span(m, e.L);
                WallPool.Reset();
                var all = Facade.Ops(site, b, k, i, e.L, uS, uE, WallOps, WallPool);
                foreach (var pc in Party.WallPieces(site, b, k, i, e.L, m))
                {
                    if (pc.kind == Party.PieceKind.Skip) continue;
                    var pm = new Miter(pc.S, pc.E); bool party = pc.kind == Party.PieceKind.Party;
                    var ops = Piece(all, party, pc.lo, pc.hi);
                    var wc = Geo.WallCtx(e.a, e.u, e.w, e.L);
                    op.Ctx(wc, party ? 1 + 8 * (pc.r!.pIdx + 1) : 1); gl.Ctx(wc, 1);
                    Facade.WallPanel(op, F, pm, 0, T, y, h, ops, party ? pc.r!.pInner : C.wall, C.inner, Panel.Set(inner: !shell, threshold: k == 0, revealFrom: shell ? T * 0.45 : 0, rooms: rooms));
                    var stk = Derived.StyleAt(b, k);
                    Facade.Dress(op, gl, F, ops, y, C, shell, stk);
                    if (!shell) Facade.SlabEdges(op, F, ops, y, h, C.slabSide, rooms);
                    if (!party && stk.bricks is double density && density > 0)
                    {
                        var (bs0, be0) = pm.Span(T, T);
                        Facade.Bricks(op, F, Math.Max(bs0, pc.lo), Math.Min(be0, pc.hi), y, h, k == 0 ? Math.Max(stk.plinthH ?? 0.45, stk.foundation.HasValue ? FoundationH(stk) : 0) : 0,
                            stk.bands && k < N - 1 ? Band(stk, C).h : 0, ops, density, Facade.Seed(b.id, k, i, pc.lo), C.brick);
                    }
                    if (!party) { op.Ctx(wc, 1); PieceStrips(op, k, F, m, pc, ops, y, h, C, rooms && !shell); }
                    var (cs, ce) = pm.Span(T / 2, T / 2);
                    var ranges = shell || party ? new List<(double, double)> { (cs, ce) } : Facade.SolidRanges(cs, ce, ops);
                    foreach (var (s1, e1) in ranges) Facade.PushSeg(sg, F, s1, e1, T / 2, T / 2);
                }
            }
        }

        /// <summary>Floor bands and the ground-floor plinth along one facade piece (ends against a party wall are left open).</summary>
        /// <param name="rooms">The strips' backs over openings are the rooms' (<see cref="Facade.StripBacks"/>).</param>
        public void PieceStrips(MeshBuilder op, int k, Frame F, Miter m, Party.Piece pc, List<Opening> ops, double y, double h, Palette C, bool rooms = false)
        {
            double T = Dim.T_EXT, L = m.E.s; int k0 = Derived.TierStart(b, k), i = pc.i;
            var c0 = pc.s < 0.02 ? Party.CornerCut(site, b, k0, i, false, y, true) : null;
            var c1 = pc.e > L - 0.02 ? Party.CornerCut(site, b, k0, i, true, y, true) : null;
            (double, double) Clip((double, double) r) => (
                pc.s > 0.02 ? Math.Max(r.Item1, pc.s) : c0 != null ? c0.Value.At(T) + 1e-4 : r.Item1,
                pc.e < L - 0.02 ? Math.Min(r.Item2, pc.e) : c1 != null ? c1.Value.At(T) - 1e-4 : r.Item2);
            var st = Derived.StyleAt(b, k);
            if (st.bands && k < N - 1)
            {
                var (bh, bd, bc) = Band(st, C);
                var under = new List<(double, double)>();
                if (Derived.IsSetback(b, k + 1))
                    foreach (var (s0, e0) in Geo.ExposedRanges(Derived.OutlineAt(b, k), Derived.OutlineAt(b, k + 1), i, true))
                        under.Add((s0 < 0.06 ? -1e9 : s0, e0 > L - 0.06 ? 1e9 : e0));
                var band = Geo.MinusRanges(new List<(double, double)> { Clip(m.Span(T, T + bd)) }, under);
                // a bridge's deck at the floor above lies where the band's top would: the band stops at the deck's sides,
                // and one deeper than the deck goes on beneath it
                var decks = Decks(k + 1, F, T, T + bd);
                var rs = decks == null ? band : Geo.MinusRanges(band, decks);
                rs.RemoveAll(r => r.Item2 - r.Item1 <= 0.02);
                if (rs.Count > 0) { Facade.StripPieces(op, F, m, rs, y + h - bh, y + h, T, T + bd, bc, bc, Skip.In); Facade.StripBacks(op, F, rs, ops, y, y + h - bh, y + h, T, bc, rooms); }
                if (decks != null && bh > Bridges.Deck + 0.01)
                {
                    var below = new List<(double, double)>();
                    foreach (var (a0, a1) in band) foreach (var (d0, d1) in decks) if (Math.Min(a1, d1) - Math.Max(a0, d0) > 0.02) below.Add((Math.Max(a0, d0), Math.Min(a1, d1)));
                    if (below.Count > 0) { Facade.StripPieces(op, F, m, below, y + h - bh, y + h - Bridges.Deck, T, T + bd, bc, bc, Skip.In); Facade.StripBacks(op, F, below, ops, y, y + h - bh, y + h - Bridges.Deck, T, bc, rooms); }
                }
            }
            if (k == 0)
            {
                // the foundation: from below the ground up to its height, standing out past the plinth, under the doors too
                double fTop = st.foundation.HasValue ? FoundationH(st) : 0, pTop = st.plinthH ?? 0.45;
                if (st.foundation is double depth)
                {
                    double fo = FoundationOut(st);
                    var rf = new List<(double, double)> { Clip(m.Span(T, T + fo)) };
                    Facade.StripPieces(op, F, m, rf, y - Math.Max(0, depth), y + fTop, T, T + fo, C.foundation, C.foundation, Skip.In | Skip.Bot);   // y: the ground floor's slab, the building's ground
                    Facade.StripBacks(op, F, rf, ops, y, y - Math.Max(0, depth), y + fTop, T, C.foundation, rooms);
                }
                if (pTop > fTop + 0.01)
                {
                    var r = Clip(m.Span(T, T + 0.04));
                    var widened = ops.ConvertAll(o => { if (!o.door) return o; var d = o.Clone(); d.u0 -= 0.08; d.u1 += 0.08; return d; });
                    var pc0 = st.plinth != null ? new Swatch(C.style, ColorSlot.Plinth) : C.plinth;
                    var rp = Facade.SolidRanges(r.Item1, r.Item2, widened);
                    Facade.StripPieces(op, F, m, rp, y + fTop, y + pTop, T, T + 0.04, pc0, pc0, Skip.In | (fTop > 0 ? Skip.None : Skip.Bot) | Skip.UEnd | Skip.UStart);
                    Facade.StripBacks(op, F, rp, ops, y, y + fTop, y + pTop, T, pc0, rooms);
                }
            }
        }

        /// <summary>
        /// Along a wall (frame F, its line at w = 0), the stretch each deck of a bridge at storey k's floor covers between w0
        /// and w1 out from the line, or null when none does: where the band under the storey below, its top at that floor,
        /// would lie flush with a deck's walking surface.
        /// </summary>
        List<(double, double)>? Decks(int k, Frame F, double w0, double w1)
        {
            bridges ??= b.bridges.Count == 0 && !site.Bridged(b.id) ? NoBridges : Bridges.Touching(site, b);
            List<(double, double)>? o = null;
            foreach (var s in bridges)
            {
                if (!(ReferenceEquals(s.A, b) && s.br.k == k) && !(ReferenceEquals(s.B, b) && s.br.toK == k)) continue;
                // the deck's outline in plan (Bridges.Corners), cut to the strip between w0 and w1: how far along the wall it goes
                var D = s.F; double hw = s.W / 2;
                Vec2 p0 = D.At2(0, -hw), p1 = D.At2(0, hw), p2 = D.At2(s.sRight, hw), p3 = D.At2(s.sLeft, -hw);
                double lo = double.MaxValue, hi = double.MinValue, wl = double.MaxValue, wh = double.MinValue;
                Within(F, w0, w1, p0, p1, ref lo, ref hi, ref wl, ref wh); Within(F, w0, w1, p1, p2, ref lo, ref hi, ref wl, ref wh);
                Within(F, w0, w1, p2, p3, ref lo, ref hi, ref wl, ref wh); Within(F, w0, w1, p3, p0, ref lo, ref hi, ref wl, ref wh);
                if (hi - lo > 1e-3 && wh - wl > 1e-3) (o ??= new List<(double, double)>()).Add((lo, hi));
            }
            return o;
        }

        /// <summary>The part of segment pq between w0 and w1 out from frame F's line, widening the range it spans along and out.</summary>
        static void Within(Frame F, double w0, double w1, Vec2 p, Vec2 q, ref double lo, ref double hi, ref double wl, ref double wh)
        {
            double pu = (p.x - F.O.x) * F.u.x + (p.z - F.O.z) * F.u.z, pw = (p.x - F.O.x) * F.w.x + (p.z - F.O.z) * F.w.z;
            double qu = (q.x - F.O.x) * F.u.x + (q.z - F.O.z) * F.u.z, qw = (q.x - F.O.x) * F.w.x + (q.z - F.O.z) * F.w.z;
            double t0 = 0, t1 = 1;
            if (Math.Abs(qw - pw) < 1e-12) { if (pw < w0 || pw > w1) return; }
            else
            {
                double ta = (w0 - pw) / (qw - pw), tb = (w1 - pw) / (qw - pw);
                t0 = Math.Max(0, Math.Min(ta, tb)); t1 = Math.Min(1, Math.Max(ta, tb));
                if (t0 > t1) return;
            }
            for (int j = 0; j < 2; j++)
            {
                double t = j == 0 ? t0 : t1, u = pu + (qu - pu) * t, w = pw + (qw - pw) * t;
                lo = Math.Min(lo, u); hi = Math.Max(hi, u); wl = Math.Min(wl, w); wh = Math.Max(wh, w);
            }
        }

        /// <summary>The band under a storey's ceiling: height, how far it stands out (at least a centimetre, so it never lies on the wall), colour.</summary>
        public static (double h, double depth, Swatch c) Band(FacadeStyle st, Palette C) =>
            (Math.Max(0.05, st.bandH ?? 0.22), Math.Max(0.01, st.bandDepth ?? 0.06), st.bandWall ? C.wallBand : C.trim);

        /// <summary>How high the foundation shows above the ground.</summary>
        public static double FoundationH(FacadeStyle st) => Math.Max(0.02, st.foundationH ?? 0.2);

        /// <summary>How far the foundation stands out from the wall: at least a centimetre, so it never lies on the wall.</summary>
        public static double FoundationOut(FacadeStyle st) => Math.Max(0.01, Math.Min(1.5, st.foundationOut ?? 0.08));

        // ---- interior -----------------------------------------------------------------------

        public void Interior(MeshBuilder op, List<Seg> sg, int k, Palette C)
        {
            double y = Derived.FloorBase(b, k), hh = Derived.FloorH(b, k) - Dim.SLAB, ox = b.pos.x, oz = b.pos.z, E = Dim.T_INT / 2 - 0.02, t = Dim.T_INT / 2 + 0.025;
            var fp = Derived.OutlineAt(b, k);
            foreach (var w0 in b.floors[k].walls)
                foreach (var wl in Courtyards.ClipOut(b, k, Geo.ClipWall(fp, w0)))
                {
                    var a = new Vec2(wl.a.x + ox, wl.a.z + oz); var c = new Vec2(wl.b.x + ox, wl.b.z + oz);
                    double dx = c.x - a.x, dz = c.z - a.z, L = Geo.Hypot(dx, dz); if (L < 0.05) continue;
                    var u = new Vec2(dx / L, dz / L); var w = new Vec2(-u.z, u.x); var F = new Frame(a, u, w);
                    var ops = new List<Opening>();
                    foreach (var d in wl.doors)
                    {
                        double mm = Math.Max(Dim.DOOR_INT / 2 + 0.1, Math.Min(L - Dim.DOOR_INT / 2 - 0.1, d.t * L));
                        ops.Add(new Opening { u0 = mm - Dim.DOOR_INT / 2, u1 = mm + Dim.DOOR_INT / 2, y0 = 0, y1 = 2.2, door = true });
                    }
                    ops.Sort((p, q) => p.u0.CompareTo(q.u0));
                    op.Ctx(Geo.WallCtx(a, u, w, L), 1);
                    Facade.WallPanel(op, F, new Miter(new Cut(-E, 0), new Cut(L + E, 0)), -Dim.T_INT / 2, Dim.T_INT / 2, y, hh, ops, C.inner, C.inner, new Facade.PanelOpt { ends = true });
                    foreach (var o in Facade.MergeOps(ops))
                    {
                        op.OBox(F, o.u0 - 0.07, o.u0 + 0.01, y, y + 2.19, -t, t, C.doorTrim, C.doorTrim, Skip.Bot);
                        op.OBox(F, o.u1 - 0.01, o.u1 + 0.07, y, y + 2.19, -t, t, C.doorTrim, C.doorTrim, Skip.Bot);
                        op.OBox(F, o.u0 - 0.07, o.u1 + 0.07, y + 2.19, y + 2.27, -t, t, C.doorTrim);
                    }
                    foreach (var (s0, e0) in Facade.SolidRanges(-E, L + E, ops)) Facade.PushSeg(sg, F, s0, e0, 0, Dim.T_INT / 2);
                }
        }

        // ---- roof ----------------------------------------------------------------------------

        /// <summary>Stretches of the top outline's edge i where a roof-access core shares the wall: its bulkhead stands there instead of the parapet.</summary>
        List<(double, double)> CoreRoofSkips(int i)
        {
            var out_ = new List<(double, double)>();
            foreach (var s in b.shafts)
            {
                if (!Cores.ShaftRoof(b, s) || s.type == CoreType.Flight) continue;   // straight flights have no bulkhead: the parapet runs on
                foreach (var f in Cores.CoreFlush(Derived.OutlineAt(b, N), s)) if (f != null && f.i == i) out_.Add((f.s0, f.s1));
            }
            return out_;
        }

        public void Roof(MeshBuilder op, List<Seg>? sg, TierCtx g, Palette C)
        {
            if (Roofs.Draw(op, Roofs.Parts(site, b), C, Derived.StyleAt(b, N))) { WallTops(op, g, C); return; }   // hip, gable or shed (no roof access, no parapet)
            double y = Derived.RoofY(b), T = Dim.T_EXT; int n = g.Wp.Count, k0 = Derived.TierStart(b, N);
            bool parapet = Derived.StyleAt(b, N).parapet;
            for (int i = 0; i < n; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L);
                var party = Party.Skips(site, b, k0, i, y); var cores = CoreRoofSkips(i);
                var cut = new List<(double, double)>(party); cut.AddRange(cores);
                var c0 = Party.CornerCut(site, b, k0, i, false, y); var c1 = Party.CornerCut(site, b, k0, i, true, y);
                List<(double, double)> Clip((double, double) r) => new List<(double, double)> {
                    (c0 != null ? Math.Max(r.Item1, c0.Value.At(T)) : r.Item1, c1 != null ? Math.Min(r.Item2, c1.Value.At(T)) : r.Item2) };
                List<(double, double)> R((double, double) r) => Geo.MinusRanges(Clip(r), cut);
                op.Ctx(Geo.WallCtx(e.a, e.u, e.w, e.L), 1);
                void Edge(Func<(double, double), List<(double, double)>> ranges)
                {
                    if (parapet)
                    {
                        Facade.StripPieces(op, F, m, ranges(m.Span(0, T)), y, y + 1.0, 0, T, C.wall, C.inner, Skip.Top | Skip.Bot);
                        Facade.StripPieces(op, F, m, ranges(m.Span(-0.04, T + 0.05)), y + 1.0, y + 1.1, -0.04, T + 0.05, C.trim, null);
                    }
                    else Facade.StripPieces(op, F, m, ranges(m.Span(0, T)), y, y + 0.3, 0, T, C.trim, C.trim, Skip.Bot);
                }
                Edge(R);
                // the parapet stops where a core stands flush against the wall, whose bulkhead (LOD0's) takes the wall up. A
                // building without insides builds none (LOD0 its cores, LOD1 its bulkheads, only for a walk-in one), and LOD1's
                // stand clear of the wall: there the wall's top is open, so the parapet the prototype leaves out is filled in, by
                // Unity only
                if ((shell || op.Lean) && cores.Count > 0)
                {
                    op.Backing = true;
                    Edge(r => { var all = Geo.MinusRanges(Clip(r), party); return Geo.MinusRanges(all, Geo.MinusRanges(all, cores)); });
                    op.Backing = false;
                }
                if (sg != null) foreach (var (cs, ce) in R(m.Span(T / 2, T / 2))) Facade.PushSeg(sg, F, cs, ce, T / 2, T / 2);
            }
            foreach (var v in b.voids) if (v.kind == VoidKind.Courtyard && Courtyards.HoleAt(b, v, N)) Courtyards.RoofEdge(op, sg, b, v, C);
        }

        /// <summary>
        /// Under a pitched roof, the outer walls' tops, by Unity only (<see cref="MeshBuilder.Backing"/>). The roof sits on them,
        /// but where its edge rises above them (a cut corner's short edge, between a gable's rake and an eave) the wall is open
        /// at the top, into the slab; the prototype's two-sided faces show its inside there, a back face culled shows through.
        /// Not along a neighbour as high: its slab is there.
        /// </summary>
        void WallTops(MeshBuilder op, TierCtx g, Palette C)
        {
            if (op.ShapeOnly) return;
            double y = Derived.RoofY(b), T = Dim.T_EXT; int k0 = Derived.TierStart(b, N);
            op.Backing = true;
            for (int i = 0; i < g.Wp.Count; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var m = Geo.MiterOf(g.cor, i, e.L);
                var party = new List<(double, double)>(); foreach (var r in Party.Ranges(site, b, k0, i)) if (Party.Shares(r, y)) party.Add((r.s, r.e));
                var c0 = Party.CornerCut(site, b, k0, i, false, y); var c1 = Party.CornerCut(site, b, k0, i, true, y);
                var (s, en) = m.Span(0, T);
                var all = new List<(double, double)> { (c0 != null ? Math.Max(s, c0.Value.At(T)) : s, c1 != null ? Math.Min(en, c1.Value.At(T)) : en) };
                foreach (var (a, z) in Geo.MinusRanges(all, party))
                {
                    // mitred where it reaches the corner (as Facade.StripPieces)
                    bool ms = a <= s + 1e-6, me = z >= en - 1e-6;
                    double sA = ms ? m.S.s : a, kA = ms ? m.S.k : 0, eB = me ? m.E.s : z, kB = me ? m.E.k : 0;
                    if (eB - sA < 1e-4 || eB + kB * T - (sA + kA * T) < 1e-4) continue;
                    op.Quad(e.F.At(sA, y, 0), e.F.At(eB, y, 0), e.F.At(eB + kB * T, y, T), e.F.At(sA + kA * T, y, T), new P3(0, 1, 0), C.roof);
                }
            }
            op.Backing = false;
        }

        // ---- cores ---------------------------------------------------------------------------

        /// <summary>The cores at level k. <paramref name="rooms"/>: below the roof, they are the rooms' (<see cref="MeshBuilder.Inside"/>).</summary>
        public void Shafts(MeshBuilder op, List<Seg> sg, int k, Palette C, bool rooms = false)
        {
            foreach (var s in b.shafts)
            {
                if (!Cores.Serves(b, s, k)) continue;
                op.Inside = rooms && k < N;
                var f = Cores.FrameOf(b, s); double y = Derived.FloorBase(b, k); bool atRoof = k == N;
                double[] Wl(double x0, double z0, double x1, double z1, double nx, double nz)
                {
                    var a = f.At2(x0, z0); var c = f.At2(x1, z1); double L = Geo.Hypot(c.x - a.x, c.z - a.z);
                    return Geo.WallCtx(a, new Vec2((c.x - a.x) / L, (c.z - a.z) / L), new Vec2(f.u.x * nx + f.w.x * nz, f.u.z * nx + f.w.z * nz), L);
                }
                void Bx(double x0, double x1, double y0, double y1, double z0, double z1, Swatch c, Swatch? ci = null, Skip sk = Skip.None) => op.OBox(f, x0, x1, y0, y1, z0, z1, c, ci, sk);
                void S(double px, double pz, double qx, double qz, double r) { var a = f.At2(px, pz); var c = f.At2(qx, qz); sg.Add(new Seg(a.x, a.z, c.x, c.z, r)); }
                const Skip TB = Skip.Top | Skip.Bot;
                var fl = Cores.CoreFlush(Derived.OutlineAt(b, k), s); bool fL = fl[0] != null, fR = fl[1] != null, fB = fl[3] != null; double TE = Dim.T_EXT;
                // a core wall's end against the building's own wall, whose windows show it (the prototype's faces are two-sided):
                // only Unity draws it, a centimetre in
                void End(double x0, double x1, double y0, double y1, double z0, double z1, Skip face)
                {
                    if (face == Skip.Out) z1 -= 0.01; else if (face == Skip.In) z0 += 0.01; else if (face == Skip.UStart) x0 += 0.01; else x1 -= 0.01;
                    op.Backing = true; Bx(x0, x1, y0, y1, z0, z1, C.core, null, Facade.AllBut(face)); op.Backing = false;
                }
                if (s.type == CoreType.Stairs)
                {
                    double h = atRoof ? 2.7 : Derived.FloorH(b, k) - Dim.SLAB, xL = fL ? -1.3 : -1.45, xR = fR ? 1.3 : 1.45, zB = fB ? 2.6 : 2.75;
                    Skip ends = TB | Skip.Out | (atRoof ? Skip.In : Skip.None);
                    bool ow = !atRoof;   // below the roof a wall flush with the building's ends behind its windows (the bulkhead's own wall closes it above)
                    op.Ctx(Wl(-1.3, -2.6, -1.3, 2.6, -1, 0), 1);
                    if (!fL) { Bx(-1.45, -1.3, y, y + h, -2.6, 2.6, C.core, C.coreIn, ends); if (ow && fB) End(-1.45, -1.3, y, y + h, -2.6, 2.6, Skip.Out); } else if (atRoof) Bx(-1.3 - TE, -1.3, y, y + h, -2.75, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(1.3, -2.6, 1.3, 2.6, 1, 0), 1);
                    if (!fR) { Bx(1.3, 1.45, y, y + h, -2.6, 2.6, C.core, C.coreIn, ends); if (ow && fB) End(1.3, 1.45, y, y + h, -2.6, 2.6, Skip.Out); } else if (atRoof) Bx(1.3, 1.3 + TE, y, y + h, -2.75, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(-1.45, 2.6, 1.45, 2.6, 0, 1), 1);
                    if (!fB)
                    {
                        Bx(xL, xR, y, y + h, 2.6, 2.75, C.core, C.coreIn, TB | (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None));
                        if (ow && fL) End(xL, xR, y, y + h, 2.6, 2.75, Skip.UStart); if (ow && fR) End(xL, xR, y, y + h, 2.6, 2.75, Skip.UEnd);
                    }
                    else if (atRoof) Bx(fL ? -1.3 - TE : -1.45, fR ? 1.3 + TE : 1.45, y, y + h, 2.6, 2.6 + TE, C.wall, C.coreIn, TB);
                    op.Ctx(Wl(0, -1.6, 0, 1.6, 1, 0), 1); Bx(-0.05, 0.05, y, y + (atRoof ? 1.1 : h), -1.6, 1.6, C.coreIn);
                    if (!fL) S(-1.375, -2.6, -1.375, 2.6, 0.075); else if (atRoof) S(-1.3 - TE / 2, -2.75, -1.3 - TE / 2, 2.75, TE / 2);
                    if (!fR) S(1.375, -2.6, 1.375, 2.6, 0.075); else if (atRoof) S(1.3 + TE / 2, -2.75, 1.3 + TE / 2, 2.75, TE / 2);
                    if (!fB) S(-1.45, 2.675, 1.45, 2.675, 0.075); else if (atRoof) S(-1.6, 2.6 + TE / 2, 1.6, 2.6 + TE / 2, TE / 2);
                    S(0, -1.6, 0, 1.6, 0.05);
                    if (atRoof)
                    {
                        op.Ctx(Wl(-1.45, -2.6, 1.45, -2.6, 0, -1), 1);
                        var ops = new List<Opening> { new Opening { u0 = -1.15, u1 = 1.15, y0 = 0, y1 = 2.2, door = true } };
                        Facade.WallOps(op, f, xL, xR, y, h, -2.75, -2.6, ops, C.core, C.coreIn, TB | (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None));
                        foreach (var (a, e) in Facade.SolidRanges(xL, xR, ops)) S(a, -2.675, e, -2.675, 0.075);
                        op.Ctx(null, 0); Bx(fL ? -1.3 - TE - 0.1 : -1.55, fR ? 1.3 + TE + 0.1 : 1.55, y + h, y + h + 0.2, -2.85, fB ? 2.6 + TE + 0.1 : 2.85, C.roof);
                    }
                    op.Ctx(null, 0);
                    if (Cores.HasFlight(b, s, k))
                    {
                        double fh = Derived.FloorH(b, k), half = fh / 2; int n = 10; double run = 3.2 / n;
                        for (int i = 0; i < n; i++) { double z0 = -1.6 + i * run, t = y + (i + 1) * half / n; Bx(-1.25, -0.05, t - 0.2, t, z0, z0 + run, C.step, null, Skip.UEnd); }
                        // the steps' ends against the centre wall, which the prototype leaves out as the wall covers them: where a
                        // step reaches past the wall (the second flight's last steps over its top, a slab under the next floor; the
                        // first step's foot under the floor) it was open, its riser and tread seen from behind across the stairwell.
                        // Only Unity draws those parts
                        double top = y + (atRoof ? 1.1 : h);
                        void Ends(double x0, double x1, double t, double z0, double z1, Skip face)
                        {
                            if (t > top) Bx(x0, x1, Math.Max(t - 0.2, top), t, z0, z1, C.step, null, Facade.AllBut(face));
                            if (t - 0.2 < y) Bx(x0, x1, t - 0.2, Math.Min(t, y), z0, z1, C.step, null, Facade.AllBut(face));
                        }
                        op.Backing = true;
                        for (int i = 0; i < n; i++) { double z0 = -1.6 + i * run, t = y + (i + 1) * half / n; Ends(-1.25, -0.05, t, z0, z0 + run, Skip.UEnd); }
                        for (int i = 0; i < n; i++) { double z1 = 1.6 - i * run, t = y + half + (i + 1) * half / n; Ends(0.05, 1.25, t, z1 - run, z1, Skip.UStart); }
                        op.Backing = false;
                        Bx(-1.3, 1.3, y + half - 0.22, y + half, 1.6, 2.6, C.step, null, Skip.UStart | Skip.UEnd | Skip.Out);
                        // its ends against the building's own walls, whose windows show them: only Unity draws them
                        var against = (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None) | (fB ? Skip.Out : Skip.None);
                        if (against != Skip.None) { op.Backing = true; Bx(-1.3, 1.3, y + half - 0.22, y + half, 1.6, 2.6, C.step, null, Facade.AllBut(against)); op.Backing = false; }
                        for (int i = 0; i < n; i++) { double z1 = 1.6 - i * run, t = y + half + (i + 1) * half / n; Bx(0.05, 1.25, t - 0.2, t, z1 - run, z1, C.step, null, Skip.UStart); }
                    }
                    void Rail(double x0, double x1)
                    {
                        Bx(x0 + 0.04, x1 - 0.04, y + 0.95, y + 1.02, -1.64, -1.56, C.rail);
                        Bx(x0, x0 + 0.04, y, y + 1.02, -1.62, -1.58, C.rail, null, Skip.Bot);
                        Bx(x1 - 0.04, x1, y, y + 1.02, -1.62, -1.58, C.rail, null, Skip.Bot);
                        S(x0, -1.6, x1, -1.6, 0.04);
                    }
                    int T = Derived.ShaftTop(b, s);
                    if (!Cores.HasFlight(b, s, k)) Rail(-1.3, -0.05);                        // nothing goes on up from here (the top, or a filled storey above)
                    if (k == s.bottom || !Cores.HasFlight(b, s, k - 1)) Rail(0.05, 1.3);     // nothing comes up to here (the bottom, or a filled storey below)
                }
                else if (s.type == CoreType.Flight)
                {
                    // straight flights stacked one above the other, with no walls: up the flight lane (the −x half) from the
                    // front (−z), off at the back, then back along the walkway (the +x half) to the next flight. Rails where
                    // the floor is open: both sides of the flight lane on every storey a flight comes up through (the
                    // building's wall stands in on a side against it), and across the front where no flight carries on,
                    // the roof included: no bulkhead, the opening is railed like any other storey's.
                    double hw = Dim.FLIGHT_W / 2, hd = Dim.FLIGHT_D / 2, xm = -hw + Dim.FLIGHT_LANE;
                    double z0 = -hd + Dim.FLIGHT_LANDING, z1 = hd - Dim.FLIGHT_LANDING;
                    op.Ctx(null, 0);
                    if (Cores.StairHoleAt(b, s, k))
                    {
                        // 2 cm clear of the flight lane, so no rail face lies on a step's
                        void RailZ(double x)
                        {
                            Bx(x - 0.02, x + 0.02, y + 0.95, y + 1.02, z0 + 0.04, z1 - 0.04, C.rail);
                            Bx(x - 0.02, x + 0.02, y, y + 1.02, z0, z0 + 0.04, C.rail, null, Skip.Bot);
                            Bx(x - 0.02, x + 0.02, y, y + 1.02, z1 - 0.04, z1, C.rail, null, Skip.Bot);
                            S(x, z0, x, z1, 0.04);
                        }
                        RailZ(xm + 0.04);
                        if (!fL) RailZ(-hw - 0.04);
                        if (!Cores.HasFlight(b, s, k))
                        {
                            // the top: nothing carries on up, so the front of the opening is railed too
                            double zr = z0 - 0.04, x0 = fL ? -hw + 0.02 : -hw - 0.06, x1 = xm + 0.06;
                            Bx(x0 + 0.04, x1 - 0.04, y + 0.95, y + 1.02, zr - 0.02, zr + 0.02, C.rail);
                            Bx(x0, x0 + 0.04, y, y + 1.02, zr - 0.02, zr + 0.02, C.rail, null, Skip.Bot);
                            Bx(x1 - 0.04, x1, y, y + 1.02, zr - 0.02, zr + 0.02, C.rail, null, Skip.Bot);
                            S(x0, zr, x1, zr, 0.04);
                        }
                    }
                    if (Cores.HasFlight(b, s, k))
                    {
                        // risers of about 18 cm, whatever the storey's height, over a fixed run
                        double fh = Derived.FloorH(b, k); int n = Math.Max(8, (int)Math.Round(fh / 0.18)); double run = (z1 - z0) / n;
                        // against a wall, a step's side would lie in the wall's inner face and flicker with it: left out
                        for (int i = 0; i < n; i++) { double za = z0 + i * run, t = y + (i + 1) * fh / n; Bx(-hw, xm, t - 0.2, t, za, za + run, C.step, null, fL ? Skip.UStart : Skip.None); }
                    }
                }
                else
                {
                    double h = atRoof ? 2.9 : Derived.FloorH(b, k) - Dim.SLAB, xL = fL ? -1.2 : -1.35, xR = fR ? 1.2 : 1.35, zB = fB ? 1.2 : 1.35;
                    bool ow = !atRoof;   // as the stairs'
                    op.Ctx(Wl(-1.2, -1.2, -1.2, 1.2, -1, 0), 1);
                    if (!fL) { Bx(-1.35, -1.2, y, y + h, -1.2, 1.2, C.core, C.coreIn, TB | Skip.In | Skip.Out); if (ow && fB) End(-1.35, -1.2, y, y + h, -1.2, 1.2, Skip.Out); } else if (atRoof) Bx(-1.2 - TE, -1.2, y, y + h, -1.35, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(1.2, -1.2, 1.2, 1.2, 1, 0), 1);
                    if (!fR) { Bx(1.2, 1.35, y, y + h, -1.2, 1.2, C.core, C.coreIn, TB | Skip.In | Skip.Out); if (ow && fB) End(1.2, 1.35, y, y + h, -1.2, 1.2, Skip.Out); } else if (atRoof) Bx(1.2, 1.2 + TE, y, y + h, -1.35, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(-1.35, 1.2, 1.35, 1.2, 0, 1), 1);
                    if (!fB)
                    {
                        Bx(xL, xR, y, y + h, 1.2, 1.35, C.core, C.liftIn, TB | (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None));
                        if (ow && fL) End(xL, xR, y, y + h, 1.2, 1.35, Skip.UStart); if (ow && fR) End(xL, xR, y, y + h, 1.2, 1.35, Skip.UEnd);
                    }
                    else if (atRoof) Bx(fL ? -1.2 - TE : -1.35, fR ? 1.2 + TE : 1.35, y, y + h, 1.2, 1.2 + TE, C.wall, C.liftIn, TB);
                    op.Ctx(Wl(-1.35, -1.2, 1.35, -1.2, 0, -1), 1);
                    var ops = new List<Opening> { new Opening { u0 = -0.6, u1 = 0.6, y0 = 0, y1 = 2.3, door = true } };
                    Facade.WallOps(op, f, xL, xR, y, h, -1.35, -1.2, ops, C.core, C.coreIn, TB | (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None));
                    if (ow && fL) End(xL, xR, y, y + h, -1.35, -1.2, Skip.UStart); if (ow && fR) End(xL, xR, y, y + h, -1.35, -1.2, Skip.UEnd);
                    Bx(-0.68, -0.59, y, y + 2.29, -1.4, -1.18, C.metal, null, Skip.Bot); Bx(0.59, 0.68, y, y + 2.29, -1.4, -1.18, C.metal, null, Skip.Bot); Bx(-0.68, 0.68, y + 2.29, y + 2.38, -1.4, -1.18, C.metal);
                    Bx(0.8, 0.92, y + 1.0, y + 1.3, -1.42, -1.35, C.liftButton);
                    if (!fL) S(-1.275, -1.35, -1.275, 1.35, 0.075); else if (atRoof) S(-1.2 - TE / 2, -1.35, -1.2 - TE / 2, 1.35, TE / 2);
                    if (!fR) S(1.275, -1.35, 1.275, 1.35, 0.075); else if (atRoof) S(1.2 + TE / 2, -1.35, 1.2 + TE / 2, 1.35, TE / 2);
                    if (!fB) S(-1.35, 1.275, 1.35, 1.275, 0.075); else if (atRoof) S(-1.5, 1.2 + TE / 2, 1.5, 1.2 + TE / 2, TE / 2);
                    foreach (var (a, e) in Facade.SolidRanges(xL, xR, ops)) S(a, -1.275, e, -1.275, 0.075);
                    op.Ctx(null, 0); Bx(-1.2, 1.2, y, y + 0.03, -1.2, -1.18, C.metal, null, Skip.Bot); Bx(-1.2, 1.2, y, y + 0.03, -1.18, 1.2, C.metal, null, Skip.Bot);
                    if (atRoof) Bx(fL ? -1.2 - TE - 0.1 : -1.45, fR ? 1.2 + TE + 0.1 : 1.45, y + h, y + h + 0.2, -1.45, fB ? 1.2 + TE + 0.1 : 1.45, C.roof);
                }
            }
            op.Inside = false;
        }
    }
    }
}
