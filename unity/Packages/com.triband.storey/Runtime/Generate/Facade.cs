#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>Window and door layout along a facade edge, shared by every LOD.</summary>
    public static class Facade
    {
        public sealed class WinSpec { public double sp, w, y0, y1; public double? full; }

        public static WinSpec? WindowSpec(WindowType kind, double h, FacadeStyle st, bool storefront = false)
        {
            if (storefront) return new WinSpec { sp = st.bay, full = 0.4, y0 = 0.4, y1 = h - 0.75 };
            switch (kind)
            {
                case WindowType.Punched: return new WinSpec { sp = st.bay, w = st.winW, y0 = 0.9, y1 = Math.Min(h - 0.55, 2.25) };
                case WindowType.Tall: return new WinSpec { sp = st.bay, w = st.winW, y0 = 0.4, y1 = h - 0.5 };
                case WindowType.Ribbon: return new WinSpec { sp = st.bay, full = 0.2, y0 = 0.95, y1 = h - 0.6 };
                case WindowType.Curtain: return new WinSpec { sp = st.bay, full = 0.07, y0 = 0.1, y1 = h - 0.1 };
                default: return null;
            }
        }

        /// <summary>The ground floor's window spec: the style's own, none, or a storefront.</summary>
        public static WinSpec? GroundSpec(FacadeStyle st, double h) =>
            st.ground == GroundType.Match ? WindowSpec(st.windows, h, st) : st.ground == GroundType.Solid ? null : WindowSpec(st.windows, h, st, true);

        public static (int n, double bay) BayLayout(FacadeStyle st, double uS, double uE)
        {
            double spn = uE - uS; if (spn <= 1.2) return (0, 0);
            int n = Math.Max(1, (int)Math.Round(spn / st.bay, MidpointRounding.AwayFromZero));
            return (n, spn / n);
        }

        /// <summary>Windows keep clear of the neighbouring wall.</summary>
        public static (double, double) Span(Miter m, double L)
        {
            var (ws, we) = m.Span(0, Dim.T_EXT);
            return (Math.Max(0, ws) + 0.14, Math.Min(L, we) - 0.14);
        }

        /// <summary>A door on an upper floor needs terrace in front of it.</summary>
        public static bool TerraceAt(BuildingData b, int k, int i, double t)
        {
            if (!Derived.IsSetback(b, k) || b.floors[k].terraceRoof != null) return false;
            var fp = Derived.OutlineAt(b, k); if (i >= fp.Count) return false;
            var a = fp[i]; var c = fp[(i + 1) % fp.Count];
            double L = Geo.Hypot(c.x - a.x, c.z - a.z); if (L == 0) L = 1;
            int sg = Geo.Area2(fp) > 0 ? 1 : -1; double nx = sg * (c.z - a.z) / L, nz = -sg * (c.x - a.x) / L, d = Dim.T_EXT + 0.5;
            double x = a.x + (c.x - a.x) * t + nx * d, z = a.z + (c.z - a.z) * t + nz * d;
            return Geo.Pip(Derived.OutlineAt(b, k - 1), x, z) && !Geo.Pip(fp, x, z);
        }

        public sealed class Landing { public BuildingData b = null!; public int k; public double y; }

        /// <summary>A neighbour's flat roof or terrace at this floor's level, right in front of edge i at t.</summary>
        public static Landing? LandingAt(Site site, BuildingData b, int k, int i, double t)
        {
            if (k == 0) return null;
            var fp = Derived.OutlineAt(b, k); if (i >= fp.Count) return null;
            var a = fp[i]; var c = fp[(i + 1) % fp.Count];
            double L = Geo.Hypot(c.x - a.x, c.z - a.z); if (L == 0) L = 1;
            int sg = Geo.Area2(fp) > 0 ? 1 : -1; double nx = sg * (c.z - a.z) / L, nz = -sg * (c.x - a.x) / L, d = Dim.T_EXT + 0.5;
            double wx = a.x + (c.x - a.x) * t + nx * d + b.pos.x, wz = a.z + (c.z - a.z) * t + nz * d + b.pos.z, y = Derived.FloorBase(b, k);
            foreach (var p in site.Near(wx, wz, 1))
            {
                if (ReferenceEquals(p, b)) continue;
                double lx = wx - p.pos.x, lz = wz - p.pos.z; int N = p.floors.Count;
                if (!Roofs.IsPitched(p) && Math.Abs(Derived.RoofY(p) - y) <= 0.35 && Geo.Pip(Derived.OutlineAt(p, N), lx, lz)) return new Landing { b = p, k = N, y = Derived.RoofY(p) };
                foreach (var tr in Party.Tiers(p))
                    if (tr.k0 > 0 && Derived.HasTerrace(p, tr.k0) && Math.Abs(Derived.FloorBase(p, tr.k0) - y) <= 0.35 && Geo.Pip(Derived.OutlineAt(p, tr.k0 - 1), lx, lz) && !Geo.Pip(Derived.OutlineAt(p, tr.k0), lx, lz))
                        return new Landing { b = p, k = tr.k0, y = Derived.FloorBase(p, tr.k0) };
            }
            return null;
        }

        public static bool DoorOpen(Site site, BuildingData b, EntranceData e) =>
            e.k == 0 || TerraceAt(b, e.k, e.edge, e.t) || LandingAt(site, b, e.k, e.edge, e.t) != null;

        /// <summary>The openings along edge i of storey k between uS and uE, sorted by start.</summary>
        public static List<Opening> Ops(Site site, BuildingData b, int k, int i, double L, double uS, double uE)
        {
            var st = Derived.StyleAt(b, k); double h = Derived.FloorH(b, k), spn = uE - uS;
            var ops = new List<Opening>();
            foreach (var e in b.entrances)
            {
                if (e.k != k || e.edge != i || spn <= Dim.DOOR_EXT + 0.6 || !DoorOpen(site, b, e)) continue;
                double c = Math.Max(uS + Dim.DOOR_EXT / 2 + 0.3, Math.Min(uE - Dim.DOOR_EXT / 2 - 0.3, e.t * L));
                var land = k > 0 && !TerraceAt(b, k, i, e.t) ? LandingAt(site, b, k, i, e.t) : null;
                ops.Add(new Opening { u0 = c - Dim.DOOR_EXT / 2, u1 = c + Dim.DOOR_EXT / 2, y0 = 0, y1 = Math.Min(2.6, h - 0.4), door = true, thr = land != null && land.y < Derived.FloorBase(b, k) - 0.02 });
            }
            var shaftHere = new List<Cores.Flush>();
            foreach (var s in b.shafts)
            {
                if (s.type != CoreType.Lift || !Cores.Levels(b, s).Contains(k)) continue;
                foreach (var f in Cores.CoreFlush(Derived.OutlineAt(b, k), s)) if (f != null && f.i == i) shaftHere.Add(f);
            }
            var blank = Derived.TierStart(b, k) > 0 ? b.floors[Derived.TierStart(b, k)].blank : b.blank;
            if (!blank.Contains(i))
            {
                var ws = k == 0 ? GroundSpec(st, h) : WindowSpec(st.windows, h, st);
                var (n, bay) = BayLayout(st, uS, uE);
                if (ws != null && ws.y1 - ws.y0 > 0.4 && n > 0)
                {
                    for (int j = 0; j < n; j++)
                    {
                        double c = uS + (j + 0.5) * bay, ww = ws.full != null ? bay - ws.full.Value : Math.Min(ws.w, bay - 0.5);
                        if (ww < 0.45) continue;
                        double u0 = c - ww / 2, u1 = c + ww / 2;
                        bool blocked = false;
                        foreach (var o in ops) if (o.door && u1 > o.u0 - 0.3 && u0 < o.u1 + 0.3) blocked = true;
                        foreach (var f in shaftHere) if (u1 > f.s0 - 0.1 && u0 < f.s1 + 0.1) blocked = true;
                        if (blocked) continue;
                        ops.Add(new Opening { u0 = u0, u1 = u1, y0 = ws.y0, y1 = ws.y1, full = ws.full != null });
                    }
                }
            }
            ops.Sort((p, q) => p.u0.CompareTo(q.u0));
            return ops;
        }

        /// <summary>Stretches between doors (walls to collide with).</summary>
        public static List<(double, double)> SolidRanges(double uA, double uB, List<Opening> ops)
        {
            var out_ = new List<(double, double)>(); double cur = uA;
            foreach (var o in ops) { if (!o.door) continue; if (o.u0 > cur) out_.Add((cur, o.u0)); cur = Math.Max(cur, o.u1); }
            if (uB > cur) out_.Add((cur, uB));
            return out_;
        }

        public static List<Opening> MergeOps(List<Opening> ops)
        {
            var sorted = new List<Opening>(ops); sorted.Sort((p, q) => p.u0.CompareTo(q.u0));
            var out_ = new List<Opening>();
            foreach (var o in sorted)
            {
                var l = out_.Count > 0 ? out_[out_.Count - 1] : null;
                if (l != null && o.u0 < l.u1 + 1e-3) { l.u1 = Math.Max(l.u1, o.u1); l.y0 = Math.Min(l.y0, o.y0); l.y1 = Math.Max(l.y1, o.y1); l.door = l.door || o.door; }
                else out_.Add(o.Clone());
            }
            return out_;
        }

        public static void PushSeg(List<Seg> sg, Frame F, double s, double e, double wo, double r)
        {
            var a = F.At2(s, wo); var b = F.At2(e, wo);
            sg.Add(new Seg(a.x, a.z, b.x, b.z, r));
        }

        /// <summary>A wall as stacked boxes around its openings (used for core fronts).</summary>
        public static void WallOps(MeshBuilder gb, Frame F, double uA, double uB, double y0, double h, double w0, double w1, List<Opening> ops, Rgb c, Rgb? cIn, Skip skip = Skip.None)
        {
            double cur = uA;
            foreach (var o in ops)
            {
                if (o.u0 > cur) gb.OBox(F, cur, o.u0, y0, y0 + h, w0, w1, c, cIn, skip);
                double a = Math.Max(o.u0, cur), e = o.u1;
                if (e > a)
                {
                    if (o.y0 > 0.001) gb.OBox(F, a, e, y0, y0 + o.y0, w0, w1, c, cIn, skip | Skip.Top);
                    if (o.y1 < h - 0.001) gb.OBox(F, a, e, y0 + o.y1, y0 + h, w0, w1, c, cIn, skip | Skip.Bot);
                }
                cur = Math.Max(cur, o.u1);
            }
            if (uB > cur) gb.OBox(F, cur, uB, y0, y0 + h, w0, w1, c, cIn, skip);
        }

        public sealed class PanelOpt
        {
            public bool outer = true, inner = true, ends, threshold;
            public double? revealFrom; public Rgb? revealC;
        }

        /// <summary>
        /// Wall panel: each face one polygon with the openings cut out, triangulated once. Only visible
        /// surfaces: outer face, inner face (optional), the reveals of each opening, end faces for
        /// free-standing walls.
        /// </summary>
        public static void WallPanel(MeshBuilder gb, Frame F, Miter m, double w0, double w1, double y0, double h, List<Opening> ops, Rgb cOut, Rgb cIn, PanelOpt? opt = null)
        {
            opt ??= new PanelOpt();
            var os = MergeOps(ops);
            P3 X(double uu, double yy, double wv) => F.At(uu, y0 + yy, wv);
            void Face(double wv, int sign, Rgb c)
            {
                double s = m.S.At(wv), e = m.E.At(wv);
                var cont = new List<Vec2> { new Vec2(s, 0) };
                foreach (var o in os) if (o.y0 <= 1e-3) { cont.Add(new Vec2(o.u0, 0)); cont.Add(new Vec2(o.u0, o.y1)); cont.Add(new Vec2(o.u1, o.y1)); cont.Add(new Vec2(o.u1, 0)); }
                cont.Add(new Vec2(e, 0)); cont.Add(new Vec2(e, h));
                for (int j = os.Count - 1; j >= 0; j--) { var o = os[j]; if (o.y1 >= h - 1e-3 && o.y0 > 1e-3) { cont.Add(new Vec2(o.u1, h)); cont.Add(new Vec2(o.u1, o.y0)); cont.Add(new Vec2(o.u0, o.y0)); cont.Add(new Vec2(o.u0, h)); } }
                cont.Add(new Vec2(s, h));
                var holes = new List<List<Vec2>>();
                foreach (var o in os) if (o.y0 > 1e-3 && o.y1 < h - 1e-3) holes.Add(new List<Vec2> { new Vec2(o.u0, o.y0), new Vec2(o.u1, o.y0), new Vec2(o.u1, o.y1), new Vec2(o.u0, o.y1) });
                var tris = Triangulate.Shape(cont, holes);
                var pts = new List<P3>();
                foreach (var q in cont) pts.Add(X(q.x, q.z, wv));
                foreach (var hh in holes) foreach (var q in hh) pts.Add(X(q.x, q.z, wv));
                gb.PolyTris(pts, tris, new P3(F.w.x * sign, 0, F.w.z * sign), c);
            }
            if (opt.outer) Face(w1, 1, cOut);
            if (opt.inner) Face(w0, -1, cIn);
            double rw = opt.revealFrom ?? w0; Rgb rc = opt.revealC ?? cOut;
            var nu = new P3(F.u.x, 0, F.u.z);
            foreach (var o in os)
            {
                gb.Poly(new[] { X(o.u0, o.y0, rw), X(o.u0, o.y0, w1), X(o.u0, o.y1, w1), X(o.u0, o.y1, rw) }, nu, rc);
                gb.Poly(new[] { X(o.u1, o.y0, rw), X(o.u1, o.y0, w1), X(o.u1, o.y1, w1), X(o.u1, o.y1, rw) }, new P3(-nu.x, 0, -nu.z), rc);
                if (o.y0 > 1e-3 || opt.threshold || o.thr) gb.Poly(new[] { X(o.u0, o.y0, rw), X(o.u1, o.y0, rw), X(o.u1, o.y0, w1), X(o.u0, o.y0, w1) }, new P3(0, 1, 0), rc);
                if (o.y1 < h - 1e-3) gb.Poly(new[] { X(o.u0, o.y1, rw), X(o.u1, o.y1, rw), X(o.u1, o.y1, w1), X(o.u0, o.y1, w1) }, new P3(0, -1, 0), rc);
            }
            if (opt.ends)
            {
                double s0 = m.S.At(w0), s1 = m.S.At(w1), e0 = m.E.At(w0), e1 = m.E.At(w1);
                gb.Poly(new[] { X(s0, 0, w0), X(s1, 0, w1), X(s1, h, w1), X(s0, h, w0) }, Geo.EndNormal(F, s0, s1, w0, w1, -1), cOut);
                gb.Poly(new[] { X(e0, 0, w0), X(e1, 0, w1), X(e1, h, w1), X(e0, h, w0) }, Geo.EndNormal(F, e0, e1, w0, w1, 1), cOut);
            }
            if (gb.Solids != null)
            {
                double? cur = null;
                void Put(double? a, double? bb, double ya, double yb) => gb.Solids.Add(new Solid
                {
                    F = F, Mitred = true, sA = a ?? m.S.s, kA = a == null ? m.S.k : 0, eB = bb ?? m.E.s, kB = bb == null ? m.E.k : 0, y0 = y0 + ya, y1 = y0 + yb, w0 = w0, w1 = w1,
                });
                foreach (var o in os) { Put(cur, o.u0, 0, h); if (o.y0 > 1e-3) Put(o.u0, o.u1, 0, o.y0); if (o.y1 < h - 1e-3) Put(o.u0, o.u1, o.y1, h); cur = o.u1; }
                Put(cur, null, 0, h);
            }
        }

        /// <summary>A glass or door pane filling an opening: one quad (two for glass seen from both sides).</summary>
        public static void Pane(MeshBuilder gb, Frame F, double u0, double u1, double y0, double y1, double wv, Rgb c, bool two)
        {
            var q = new[] { F.At(u0, y0, wv), F.At(u1, y0, wv), F.At(u1, y1, wv), F.At(u0, y1, wv) };
            gb.Poly(q, new P3(F.w.x, 0, F.w.z), c);
            if (two) gb.Poly((P3[])q.Clone(), new P3(-F.w.x, 0, -F.w.z), c);
        }

        /// <summary>A strip split into pieces at ranges; the first and last take the mitred ends.</summary>
        public static void StripPieces(MeshBuilder gb, Frame F, Miter m, List<(double, double)> ranges, double y0, double y1, double wa, double wb, Rgb c, Rgb? cIn, Skip skip = Skip.None)
        {
            var (s0, e0) = m.Span(wa, wb);
            foreach (var (a, bb) in ranges)
            {
                bool ms = a <= s0 + 1e-6, me = bb >= e0 - 1e-6;
                gb.MBox(F, ms ? m.S.s : a, ms ? m.S.k : 0, me ? m.E.s : bb, me ? m.E.k : 0, y0, y1, wa, wb, c, cIn, skip | (ms ? Skip.UStart : Skip.None) | (me ? Skip.UEnd : Skip.None));
            }
        }
    }
}
