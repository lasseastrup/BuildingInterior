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
    }

    /// <summary>
    /// LOD0: everything. Interior, frames, see-through glass; one opaque mesh and one glass mesh per
    /// building. A port of the prototype's <c>lod0Steps</c>, storey by storey, so a caller can spread a
    /// building over several frames.
    /// </summary>
    public static class Lod0
    {
        /// <summary>Build a building's LOD0. <paramref name="solids"/> records volumes for the coplanar checker.</summary>
        public static Lod0Result Build(Site site, BuildingData b, bool solids = false)
        {
            var self = new Shared(site, b);
            int idx = site.IndexOf(b);
            var r = new Lod0Result { Op = new MeshBuilder(idx), Glass = new MeshBuilder(idx) };
            r.Glass.Walls = r.Op.Walls;   // glass shares the wall ids of the walls it sits in
            if (solids) r.Op.Solids = new List<Solid>();
            site.partyMemo.Remove(b.id);
            foreach (var _ in self.Steps(r)) { }
            return r;
        }

        /// <summary>Outline geometry and colours of one setback tier.</summary>
        public sealed class TierCtx { public List<Vec2> fp = null!; public bool ccw; public List<Vec2> Wp = null!; public Geo.Corner[] cor = null!; public Palette C = null!; }

        /// <summary>The storey builders, shared by the three LODs of one building.</summary>
        public sealed class Shared
        {
        readonly Site site; readonly BuildingData b; readonly int N; readonly bool shell;
        readonly Dictionary<int, TierCtx> tiers = new Dictionary<int, TierCtx>();

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
            for (int k = 0; k <= N; k++)
            {
                var sg = new List<Seg>(); var g = At(k); bool sb = Derived.IsSetback(b, k); var Cb = At(Math.Max(0, k - 1)).C;
                // a filled storey is built as a shell-only building's storeys are; a slab between two of them is not needed
                bool sh = Derived.ShellAt(b, k), shBelow = Derived.ShellAt(b, k - 1);
                if (!(sh && shBelow) || k == N || sb) Slab(op, k, Cb, sh, false);
                if (sb) { if (!Roofs.Draw(op, Roofs.TerraceRoof(site, b, k), Cb)) Terrace(op, sg, k, Cb); Overhang(op, k, g.C); }
                if (k < N) { FacadeStorey(op, gl, sg, k, g); Details.Build(op, site, b, k, g.C, 0); if (!sh) Interior(op, sg, k, g.C); }
                else Roof(op, sg, g, Cb);
                if (!shell && !Derived.Filled(b, k)) Shafts(op, sg, k, g.C);
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
            var holes = new List<List<Vec2>>();
            if (!shell && !topOnly)
                foreach (var s in b.shafts)
                    if (Cores.StairHoleAt(b, s, k))
                    {
                        var f = Cores.FrameOf(b, s);
                        if (s.type == CoreType.Flight)
                        {
                            // over the flight lane's run only: the landings and the walkway are this floor
                            double hw = Dim.FLIGHT_W / 2, xm = -hw + Dim.FLIGHT_LANE, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
                            holes.Add(new List<Vec2> { f.At2(-hw, z0), f.At2(xm, z0), f.At2(xm, z1), f.At2(-hw, z1) });
                        }
                        else holes.Add(new List<Vec2> { f.At2(-1.3, -1.6), f.At2(1.3, -1.6), f.At2(1.3, 2.6), f.At2(-1.3, 2.6) });
                    }
            List<Vec2> V2(List<Vec2> pts) { var o = new List<Vec2>(pts.Count); foreach (var p in pts) o.Add(new Vec2(p.x + ox, p.z + oz)); return o; }
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
            if (!step) Surf(outer, holes, y, up, k == N ? C.roof : C.floor);
            else
            {
                if (!shell) Surf(V2(above!), holes, y, up, C.floor);
                foreach (var poly in Geo.TerracePolys(below!, above!)) Surf(V2(poly[0]), poly.GetRange(1, poly.Count - 1).ConvertAll(V2), y, up, C.roof);
            }
            if (k > 0 && !topOnly) Surf(outer, holes, yb, down, C.ceil);
            if (step) foreach (var poly in Geo.TerracePolys(above!, below!)) Surf(V2(poly[0]), poly.GetRange(1, poly.Count - 1).ConvertAll(V2), yb, down, C.ceil);   // soffit under an overhang
            foreach (var h in holes)
            {
                double cx = 0, cz = 0; foreach (var p in h) { cx += p.x; cz += p.z; } cx /= h.Count; cz /= h.Count;
                for (int i = 0; i < h.Count; i++)
                {
                    var a = h[i]; var c = h[(i + 1) % h.Count];
                    double dx = c.x - a.x, dz = c.z - a.z, L = Geo.Hypot(dx, dz); if (L == 0) L = 1;
                    double nx = dz / L, nz = -dx / L, mx = (a.x + c.x) / 2 - cx, mz = (a.z + c.z) / 2 - cz;
                    if ((nx * mx + nz * mz > 0) == true) { nx = -nx; nz = -nz; }   // inward
                    op.Poly(new[] { new P3(a.x, yb, a.z), new P3(c.x, yb, c.z), new P3(c.x, y, c.z), new P3(a.x, y, a.z) }, new P3(nx, 0, nz), side);
                }
            }
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
            for (int i = 0; i < n; i++) ex.Add(kA == null ? Geo.ExposedRanges(lo, up, i) : Geo.MinusRanges(Geo.ExposedRanges(lo, up, i), Party.Skips(site, b, kA.Value, i)));
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

        // ---- facade -------------------------------------------------------------------------

        /// <summary>LOD0 facade of one storey: panels with window and door holes, recessed panes, frames and trims.</summary>
        public void FacadeStorey(MeshBuilder op, MeshBuilder gl, List<Seg> sg, int k, TierCtx g)
        {
            var Wp = g.Wp; var C = g.C; int n = Wp.Count; bool shell = Derived.ShellAt(b, k); double h = Derived.FloorH(b, k), y = Derived.FloorBase(b, k), T = Dim.T_EXT;   // shell: this storey (a filled one is closed as a shell building is)
            for (int i = 0; i < n; i++)
            {
                var e = Geo.EdgeInfo(Wp, i, g.ccw); var F = e.F;
                var m = Geo.MiterOf(g.cor, i, e.L); var (uS, uE) = Facade.Span(m, e.L);
                var all = Facade.Ops(site, b, k, i, e.L, uS, uE);
                foreach (var pc in Party.WallPieces(site, b, k, i, e.L, m))
                {
                    if (pc.kind == Party.PieceKind.Skip) continue;
                    var pm = new Miter(pc.S, pc.E); bool party = pc.kind == Party.PieceKind.Party;
                    var ops = party ? new List<Opening>() : all.FindAll(o => o.u0 >= pc.lo && o.u1 <= pc.hi);
                    var wc = Geo.WallCtx(e.a, e.u, e.w, e.L);
                    op.Ctx(wc, party ? 1 + 8 * (pc.r!.pIdx + 1) : 1); gl.Ctx(wc, 1);
                    Facade.WallPanel(op, F, pm, 0, T, y, h, ops, party ? pc.r!.pInner : C.wall, C.inner, new Facade.PanelOpt { inner = !shell, threshold = k == 0, revealFrom = shell ? T * 0.45 : 0 });
                    foreach (var o in ops)
                    {
                        if (o.door)
                        {
                            op.OBox(F, o.u0 - 0.08, o.u0, y, y + o.y1 + 0.08, T, T + 0.06, C.trim, C.trim, Skip.In | Skip.Bot);
                            op.OBox(F, o.u1, o.u1 + 0.08, y, y + o.y1 + 0.08, T, T + 0.06, C.trim, C.trim, Skip.In | Skip.Bot);
                            op.OBox(F, o.u0 - 0.35, o.u1 + 0.35, y + o.y1 + 0.1, y + o.y1 + 0.24, T, T + 1.1, C.trim, C.trim, Skip.In);
                            if (shell) Facade.Pane(op, F, o.u0, o.u1, y, y + o.y1, T * 0.45, C.door, false);
                        }
                        else
                        {
                            if (shell) Facade.Pane(op, F, o.u0, o.u1, y + o.y0, y + o.y1, T * 0.45, C.glassDark, false);
                            else Facade.Pane(gl, F, o.u0, o.u1, y + o.y0, y + o.y1, T * 0.45, C.glass, true);
                            double ex = o.full ? 0 : 0.06, eh = o.full ? 0 : 0.04;
                            op.OBox(F, o.u0 - ex, o.u1 + ex, y + o.y0 - 0.07, y + o.y0, T, T + 0.07, C.trim, C.trim, Skip.In);
                            op.OBox(F, o.u0 - eh, o.u1 + eh, y + o.y1, y + o.y1 + 0.06, T, T + 0.04, C.trim, C.trim, Skip.In);
                            if (o.u1 - o.u0 > 1.7) { double mm = (o.u0 + o.u1) / 2; op.OBox(F, mm - 0.03, mm + 0.03, y + o.y0, y + o.y1, T * 0.3, T * 0.6, C.trim, C.trim, Skip.Top | Skip.Bot); }
                        }
                    }
                    if (!party) { op.Ctx(wc, 1); PieceStrips(op, k, F, m, pc, ops, y, h, C); }
                    var (cs, ce) = pm.Span(T / 2, T / 2);
                    var ranges = shell || party ? new List<(double, double)> { (cs, ce) } : Facade.SolidRanges(cs, ce, ops);
                    foreach (var (s1, e1) in ranges) Facade.PushSeg(sg, F, s1, e1, T / 2, T / 2);
                }
            }
        }

        /// <summary>Floor bands and the ground-floor plinth along one facade piece (ends against a party wall are left open).</summary>
        public void PieceStrips(MeshBuilder op, int k, Frame F, Miter m, Party.Piece pc, List<Opening> ops, double y, double h, Palette C)
        {
            double T = Dim.T_EXT, L = m.E.s; int k0 = Derived.TierStart(b, k), i = pc.i;
            var c0 = pc.s < 0.02 ? Party.CornerCut(site, b, k0, i, false, y, true) : null;
            var c1 = pc.e > L - 0.02 ? Party.CornerCut(site, b, k0, i, true, y, true) : null;
            (double, double) Clip((double, double) r) => (
                pc.s > 0.02 ? Math.Max(r.Item1, pc.s) : c0 != null ? c0.Value.At(T) + 1e-4 : r.Item1,
                pc.e < L - 0.02 ? Math.Min(r.Item2, pc.e) : c1 != null ? c1.Value.At(T) - 1e-4 : r.Item2);
            if (Derived.StyleAt(b, k).bands && k < N - 1)
            {
                var under = new List<(double, double)>();
                if (Derived.IsSetback(b, k + 1))
                    foreach (var (s0, e0) in Geo.ExposedRanges(Derived.OutlineAt(b, k), Derived.OutlineAt(b, k + 1), i, true))
                        under.Add((s0 < 0.06 ? -1e9 : s0, e0 > L - 0.06 ? 1e9 : e0));
                var rs = Geo.MinusRanges(new List<(double, double)> { Clip(m.Span(T, T + 0.06)) }, under);
                rs.RemoveAll(r => r.Item2 - r.Item1 <= 0.02);
                if (rs.Count > 0) Facade.StripPieces(op, F, m, rs, y + h - 0.22, y + h, T, T + 0.06, C.trim, C.trim, Skip.In);
            }
            if (k == 0)
            {
                var r = Clip(m.Span(T, T + 0.04));
                var widened = ops.ConvertAll(o => { if (!o.door) return o; var d = o.Clone(); d.u0 -= 0.08; d.u1 += 0.08; return d; });
                Facade.StripPieces(op, F, m, Facade.SolidRanges(r.Item1, r.Item2, widened), 0, 0.45, T, T + 0.04, C.plinth, C.plinth, Skip.In | Skip.Bot | Skip.UEnd | Skip.UStart);
            }
        }

        // ---- interior -----------------------------------------------------------------------

        public void Interior(MeshBuilder op, List<Seg> sg, int k, Palette C)
        {
            double y = Derived.FloorBase(b, k), hh = Derived.FloorH(b, k) - Dim.SLAB, ox = b.pos.x, oz = b.pos.z, E = Dim.T_INT / 2 - 0.02, t = Dim.T_INT / 2 + 0.025;
            var fp = Derived.OutlineAt(b, k);
            foreach (var w0 in b.floors[k].walls)
                foreach (var wl in Geo.ClipWall(fp, w0))
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
            if (Roofs.Draw(op, Roofs.Parts(site, b), C)) return;   // hip, gable or shed (no roof access, no parapet)
            double y = Derived.RoofY(b), T = Dim.T_EXT; int n = g.Wp.Count, k0 = Derived.TierStart(b, N);
            bool parapet = Derived.StyleAt(b, N).parapet;
            for (int i = 0; i < n; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L);
                var cut = new List<(double, double)>(Party.Skips(site, b, k0, i)); cut.AddRange(CoreRoofSkips(i));
                var c0 = Party.CornerCut(site, b, k0, i, false, y); var c1 = Party.CornerCut(site, b, k0, i, true, y);
                List<(double, double)> R((double, double) r) => Geo.MinusRanges(new List<(double, double)> {
                    (c0 != null ? Math.Max(r.Item1, c0.Value.At(T)) : r.Item1, c1 != null ? Math.Min(r.Item2, c1.Value.At(T)) : r.Item2) }, cut);
                op.Ctx(Geo.WallCtx(e.a, e.u, e.w, e.L), 1);
                if (parapet)
                {
                    Facade.StripPieces(op, F, m, R(m.Span(0, T)), y, y + 1.0, 0, T, C.wall, C.inner, Skip.Top | Skip.Bot);
                    Facade.StripPieces(op, F, m, R(m.Span(-0.04, T + 0.05)), y + 1.0, y + 1.1, -0.04, T + 0.05, C.trim, null);
                }
                else Facade.StripPieces(op, F, m, R(m.Span(0, T)), y, y + 0.3, 0, T, C.trim, C.trim, Skip.Bot);
                if (sg != null) foreach (var (cs, ce) in R(m.Span(T / 2, T / 2))) Facade.PushSeg(sg, F, cs, ce, T / 2, T / 2);
            }
        }

        // ---- cores ---------------------------------------------------------------------------

        public void Shafts(MeshBuilder op, List<Seg> sg, int k, Palette C)
        {
            foreach (var s in b.shafts)
            {
                if (!Cores.Levels(b, s).Contains(k)) continue;
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
                if (s.type == CoreType.Stairs)
                {
                    double h = atRoof ? 2.7 : Derived.FloorH(b, k) - Dim.SLAB, xL = fL ? -1.3 : -1.45, xR = fR ? 1.3 : 1.45, zB = fB ? 2.6 : 2.75;
                    Skip ends = TB | Skip.Out | (atRoof ? Skip.In : Skip.None);
                    op.Ctx(Wl(-1.3, -2.6, -1.3, 2.6, -1, 0), 1); if (!fL) Bx(-1.45, -1.3, y, y + h, -2.6, 2.6, C.core, C.coreIn, ends); else if (atRoof) Bx(-1.3 - TE, -1.3, y, y + h, -2.75, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(1.3, -2.6, 1.3, 2.6, 1, 0), 1); if (!fR) Bx(1.3, 1.45, y, y + h, -2.6, 2.6, C.core, C.coreIn, ends); else if (atRoof) Bx(1.3, 1.3 + TE, y, y + h, -2.75, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(-1.45, 2.6, 1.45, 2.6, 0, 1), 1); if (!fB) Bx(xL, xR, y, y + h, 2.6, 2.75, C.core, C.coreIn, TB | (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None)); else if (atRoof) Bx(fL ? -1.3 - TE : -1.45, fR ? 1.3 + TE : 1.45, y, y + h, 2.6, 2.6 + TE, C.wall, C.coreIn, TB);
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
                        Bx(-1.3, 1.3, y + half - 0.22, y + half, 1.6, 2.6, C.step, null, Skip.UStart | Skip.UEnd | Skip.Out);
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
                        for (int i = 0; i < n; i++) { double za = z0 + i * run, t = y + (i + 1) * fh / n; Bx(-hw, xm, t - 0.2, t, za, za + run, C.step); }
                    }
                }
                else
                {
                    double h = atRoof ? 2.9 : Derived.FloorH(b, k) - Dim.SLAB, xL = fL ? -1.2 : -1.35, xR = fR ? 1.2 : 1.35, zB = fB ? 1.2 : 1.35;
                    op.Ctx(Wl(-1.2, -1.2, -1.2, 1.2, -1, 0), 1); if (!fL) Bx(-1.35, -1.2, y, y + h, -1.2, 1.2, C.core, C.coreIn, TB | Skip.In | Skip.Out); else if (atRoof) Bx(-1.2 - TE, -1.2, y, y + h, -1.35, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(1.2, -1.2, 1.2, 1.2, 1, 0), 1); if (!fR) Bx(1.2, 1.35, y, y + h, -1.2, 1.2, C.core, C.coreIn, TB | Skip.In | Skip.Out); else if (atRoof) Bx(1.2, 1.2 + TE, y, y + h, -1.35, zB, C.wall, C.wall, TB | (fB ? Skip.Out : Skip.None));
                    op.Ctx(Wl(-1.35, 1.2, 1.35, 1.2, 0, 1), 1); if (!fB) Bx(xL, xR, y, y + h, 1.2, 1.35, C.core, C.liftIn, TB | (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None)); else if (atRoof) Bx(fL ? -1.2 - TE : -1.35, fR ? 1.2 + TE : 1.35, y, y + h, 1.2, 1.2 + TE, C.wall, C.liftIn, TB);
                    op.Ctx(Wl(-1.35, -1.2, 1.35, -1.2, 0, -1), 1);
                    var ops = new List<Opening> { new Opening { u0 = -0.6, u1 = 0.6, y0 = 0, y1 = 2.3, door = true } };
                    Facade.WallOps(op, f, xL, xR, y, h, -1.35, -1.2, ops, C.core, C.coreIn, TB | (fL ? Skip.UStart : Skip.None) | (fR ? Skip.UEnd : Skip.None));
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
        }
    }
    }
}
