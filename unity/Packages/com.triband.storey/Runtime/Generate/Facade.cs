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
            var ws = RawSpec(kind, h, st, storefront);
            // a band of the style's own height: windows (and their arched heads) stay under it
            if (ws != null && st.bandH is double bh && st.bands) ws.y1 = Math.Min(ws.y1, h - bh - (st.head == HeadType.Arch ? 0.3 : 0.1));
            return ws;
        }

        static WinSpec? RawSpec(WindowType kind, double h, FacadeStyle st, bool storefront)
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
        public static WinSpec? GroundSpec(FacadeStyle st, double h)
        {
            var ws = st.ground == GroundType.Match ? WindowSpec(st.windows, h, st) : st.ground == GroundType.Solid ? null : WindowSpec(st.windows, h, st, true);
            // a plinth (or foundation) of the style's own height: the ground floor's windows start above it
            if (ws != null && (st.plinthH.HasValue || st.foundation.HasValue))
                ws.y0 = Math.Max(ws.y0, Math.Max(st.plinthH ?? 0.45, st.foundation.HasValue ? Math.Max(0.02, st.foundationH ?? 0.2) : 0) + 0.15);
            return ws;
        }

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
            foreach (var (bk, bi, bt) in Bridges.Doors(site, b))
            {
                if (bk != k || bi != i || spn <= Dim.DOOR_EXT + 0.6) continue;
                double c = Math.Max(uS + Dim.DOOR_EXT / 2 + 0.3, Math.Min(uE - Dim.DOOR_EXT / 2 - 0.3, bt * L));
                ops.Add(new Opening { u0 = c - Dim.DOOR_EXT / 2, u1 = c + Dim.DOOR_EXT / 2, y0 = 0, y1 = Math.Min(2.5, h - 0.4), door = true, bare = true });
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

        /// <summary>
        /// What dresses a facade's openings in LOD0: door frames and canopies, window sills, heads and mullions, and the
        /// panes (see-through glass in <paramref name="gl"/>; opaque panes and closed doors on a <paramref name="shell"/> storey).
        /// The style's own details (<see cref="FacadeStyle.head"/>, panes, frames, glazed doors) change what is drawn; without
        /// them it is the prototype's.
        /// </summary>
        public static void Dress(MeshBuilder op, MeshBuilder gl, Frame F, List<Opening> ops, double y, Palette C, bool shell, FacadeStyle? st = null)
        {
            double T = Dim.T_EXT;
            bool frames = st != null && st.frames, arch = st != null && st.head == HeadType.Arch, glazed = st != null && st.doorType == DoorType.Glazed;
            bool bars = st != null && (st.paneCols > 1 || st.paneRows > 1);
            var trim = frames ? C.frame : C.trim;
            foreach (var o in ops)
            {
                if (o.door)
                {
                    op.OBox(F, o.u0 - 0.08, o.u0, y, y + o.y1 + 0.08, T, T + 0.06, trim, trim, Skip.In | Skip.Bot);
                    op.OBox(F, o.u1, o.u1 + 0.08, y, y + o.y1 + 0.08, T, T + 0.06, trim, trim, Skip.In | Skip.Bot);
                    if (!o.bare && !glazed) op.OBox(F, o.u0 - 0.35, o.u1 + 0.35, y + o.y1 + 0.1, y + o.y1 + 0.24, T, T + 1.1, C.trim, C.trim, Skip.In);
                    else if (arch && !o.bare) Hood(op, F, o.u0 - 0.08 - HoodOver, o.u1 + 0.08 + HoodOver, y + o.y1 + 0.08, trim);
                    if (glazed && !o.bare) GlazedDoor(op, gl, F, o, y, C, shell);
                    else if (shell) Facade.Pane(op, F, o.u0, o.u1, y, y + o.y1, T * 0.45, C.door, false);
                }
                else
                {
                    if (shell) Facade.Pane(op, F, o.u0, o.u1, y + o.y0, y + o.y1, T * 0.45, C.glassDark, false);
                    else Facade.Pane(gl, F, o.u0, o.u1, y + o.y0, y + o.y1, T * 0.45, C.glass, true);
                    double ex = o.full ? 0 : 0.06, eh = o.full ? 0 : 0.04;
                    op.OBox(F, o.u0 - ex, o.u1 + ex, y + o.y0 - 0.07, y + o.y0, T, T + 0.07, trim, trim, Skip.In);
                    if (arch && !o.full) Hood(op, F, o.u0 - HoodOver, o.u1 + HoodOver, y + o.y1, trim);
                    else op.OBox(F, o.u0 - eh, o.u1 + eh, y + o.y1, y + o.y1 + 0.06, T, T + 0.04, trim, trim, Skip.In);
                    double fw = frames ? FrameW : 0;
                    if (frames) WindowFrame(op, F, o.u0, o.u1, y + o.y0, y + o.y1, fw, C.frame);
                    if (bars) Bars(op, F, o.u0 + fw, o.u1 - fw, y + o.y0 + fw, y + o.y1 - fw, st!.paneCols ?? 1, st.paneRows ?? 1, C.frame);
                    else if (o.u1 - o.u0 > 1.7) { double mm = (o.u0 + o.u1) / 2; op.OBox(F, mm - 0.03, mm + 0.03, y + o.y0, y + o.y1, T * 0.3, T * 0.6, C.trim, C.trim, Skip.Top | Skip.Bot); }
                }
            }
        }

        /// <summary>A seed for a wall piece's brick patches, the same on every build.</summary>
        public static uint Seed(string id, int k, int edge, double lo)
        {
            uint h = 2166136261;
            foreach (char ch in id) h = (h ^ ch) * 16777619;
            h = (h ^ (uint)k) * 16777619; h = (h ^ (uint)edge) * 16777619; h = (h ^ (uint)(int)Math.Round(lo * 100)) * 16777619;
            return h == 0 ? 1 : h;
        }

        /// <summary>A brick's size, and the mortar between bricks.</summary>
        public const double BrickW = 0.24, BrickH = 0.075, Mortar = 0.03;

        /// <summary>
        /// Brick patches on a wall piece (u from <paramref name="ua"/> to <paramref name="ub"/>, storey from y, h high): small
        /// groups of one to three courses of bricks, laid on the wall's face, about one group per 2.5 m² at density 1. They
        /// keep clear of the openings (and the heads above them), the band and the plinth. Placed by a seeded random, so a
        /// rebuild puts them back where they were.
        /// </summary>
        public static void Bricks(MeshBuilder op, Frame F, double ua, double ub, double y, double h, double bottom, double band, List<Opening> ops, double density, uint seed, Swatch c)
        {
            double y0 = y + Math.Max(0.2, bottom + 0.15), y1 = y + h - (band > 0 ? band + 0.12 : 0.25), u0 = ua + 0.25, u1 = ub - 0.25;
            if (u1 - u0 < 0.6 || y1 - y0 < 0.4) return;
            uint s = seed;
            double R() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (double)0x1000000; }
            double want = Math.Min(1, density) * (u1 - u0) * (y1 - y0) / 2.5;
            int n = (int)Math.Floor(want) + (R() < want - Math.Floor(want) ? 1 : 0);
            double wv = Dim.T_EXT + 0.004; var nr = new P3(F.w.x, 0, F.w.z);
            var placed = new List<(double u0, double v0, double u1, double v1)>();
            for (int p = 0; p < n; p++)
                for (int tries = 0; tries < 4; tries++)
                {
                    int rows = 1 + (int)(R() * 3), cols = 1 + (int)(R() * 3);
                    double pw = cols * BrickW + (cols - 1) * Mortar + BrickW / 2, ph = rows * BrickH + (rows - 1) * Mortar;
                    double pu = u0 + R() * Math.Max(0, u1 - u0 - pw), pv = y0 + R() * Math.Max(0, y1 - y0 - ph);
                    bool clear = true;
                    foreach (var o in ops)
                        if (pu < o.u1 + 0.12 && pu + pw > o.u0 - 0.12 && pv < y + o.y1 + 0.45 && pv + ph > y + o.y0 - 0.15) { clear = false; break; }
                    foreach (var q0 in placed) if (pu < q0.u1 + 0.1 && pu + pw > q0.u0 - 0.1 && pv < q0.v1 + 0.1 && pv + ph > q0.v0 - 0.1) { clear = false; break; }
                    if (!clear) continue;
                    placed.Add((pu, pv, pu + pw, pv + ph));
                    for (int r = 0; r < rows; r++)
                    {
                        double off = (r & 1) == 1 ? BrickW / 2 : 0, bv = pv + r * (BrickH + Mortar);
                        int nb = cols - ((r & 1) == 1 && cols > 1 && R() < 0.5 ? 1 : 0);
                        for (int q = 0; q < nb; q++)
                        {
                            double bu = pu + off + q * (BrickW + Mortar);
                            op.Poly(new[] { F.At(bu, bv, wv), F.At(bu + BrickW, bv, wv), F.At(bu + BrickW, bv + BrickH, wv), F.At(bu, bv + BrickH, wv) }, nr, c);
                        }
                    }
                    break;
                }
        }

        /// <summary>A frame member's width, and a glazing bar's.</summary>
        public const double FrameW = 0.06, BarW = 0.035;

        /// <summary>
        /// An arched hood over an opening from <paramref name="ua"/> to <paramref name="ub"/>, sitting at
        /// <paramref name="ys"/>, as the artist draws it: a solid cap standing 14 cm out from the wall, its top arched
        /// (7 cm thick at the ends, rising by about a twelfth of its span in the middle), its underside dipping 4 cm at the
        /// ends so the cap seems to hang over the window.
        /// </summary>
        public static void Hood(MeshBuilder op, Frame F, double ua, double ub, double ys, Swatch c)
        {
            double T = Dim.T_EXT, w0 = T, w1 = T + HoodOut, half = (ub - ua) / 2, um = (ua + ub) / 2;
            double rise = Math.Max(0.05, Math.Min(0.2, (ub - ua) / 12)), endH = 0.07, droop = 0.04;
            const int n = 10;
            double Bot(double x) { double q = x / half; return ys - droop * q * q; }
            double Top(double x) { double q = x / half; return ys + endH + rise * (1 - q * q); }
            double dBot(double x) => -2 * droop * x / (half * half);
            double dTop(double x) => -2 * rise * x / (half * half);
            P3 At(double x, double y, double w) => F.At(um + x, y, w);
            P3 Up(double ux, double uy) { double l = Math.Sqrt(ux * ux + uy * uy); return new P3(F.u.x * ux / l, uy / l, F.u.z * ux / l); }
            var outN = new P3(F.w.x, 0, F.w.z);
            var front = new List<P3>();
            for (int i = 0; i <= n; i++) { double x = -half + 2 * half * i / n; front.Add(At(x, Bot(x), w1)); }
            for (int i = n; i >= 0; i--) { double x = -half + 2 * half * i / n; front.Add(At(x, Top(x), w1)); }
            op.PolyTris(front, Triangulate.Planar(front, null, outN), outN, c);
            for (int i = 0; i < n; i++)
            {
                double x0 = -half + 2 * half * i / n, x1 = -half + 2 * half * (i + 1) / n, xm = (x0 + x1) / 2;
                op.Poly(new[] { At(x0, Top(x0), w0), At(x1, Top(x1), w0), At(x1, Top(x1), w1), At(x0, Top(x0), w1) }, Up(-dTop(xm), 1), c);
                op.Poly(new[] { At(x0, Bot(x0), w0), At(x0, Bot(x0), w1), At(x1, Bot(x1), w1), At(x1, Bot(x1), w0) }, Up(dBot(xm), -1), c);
            }
            var side = new P3(F.u.x, 0, F.u.z);
            op.Poly(new[] { At(-half, Bot(-half), w0), At(-half, Top(-half), w0), At(-half, Top(-half), w1), At(-half, Bot(-half), w1) }, side * -1, c);
            op.Poly(new[] { At(half, Bot(half), w0), At(half, Bot(half), w1), At(half, Top(half), w1), At(half, Top(half), w0) }, side, c);
        }

        /// <summary>How far a hood stands out from the wall, and how far past the opening it reaches each side.</summary>
        public const double HoodOut = 0.14, HoodOver = 0.1;

        /// <summary>A frame all round an opening, in its reveal, around the pane.</summary>
        public static void WindowFrame(MeshBuilder op, Frame F, double u0, double u1, double ya, double yb, double fw, Swatch c)
        {
            double T = Dim.T_EXT, wa = T * 0.3, wb = T * 0.6;
            op.OBox(F, u0, u0 + fw, ya, yb, wa, wb, c, null, Skip.UStart | Skip.Top | Skip.Bot);
            op.OBox(F, u1 - fw, u1, ya, yb, wa, wb, c, null, Skip.UEnd | Skip.Top | Skip.Bot);
            op.OBox(F, u0 + fw, u1 - fw, yb - fw, yb, wa, wb, c, null, Skip.Top | Skip.UStart | Skip.UEnd);
            op.OBox(F, u0 + fw, u1 - fw, ya, ya + fw, wa, wb, c, null, Skip.Bot | Skip.UStart | Skip.UEnd);
        }

        /// <summary>Glazing bars dividing the pane inside (ua..ub, ya..yb) into columns × rows.</summary>
        public static void Bars(MeshBuilder op, Frame F, double ua, double ub, double ya, double yb, int cols, int rows, Swatch c)
        {
            double T = Dim.T_EXT, wa = T * 0.36, wb = T * 0.54, h = BarW / 2;
            cols = Math.Max(1, Math.Min(8, cols)); rows = Math.Max(1, Math.Min(8, rows));
            var xs = new double[cols + 1];
            for (int i = 0; i <= cols; i++) xs[i] = ua + (ub - ua) * i / cols;
            for (int i = 1; i < cols; i++) op.OBox(F, xs[i] - h, xs[i] + h, ya, yb, wa, wb, c, null, Skip.Top | Skip.Bot);
            for (int j = 1; j < rows; j++)
            {
                double yj = ya + (yb - ya) * j / rows;
                for (int i = 0; i < cols; i++)
                    op.OBox(F, i == 0 ? ua : xs[i] + h, i == cols - 1 ? ub : xs[i + 1] - h, yj - h, yj + h, wa, wb, c, null, Skip.UStart | Skip.UEnd);
            }
        }

        /// <summary>
        /// Glazed double doors in a door opening: a frame, a transom bar half a metre under the head with a fanlight above.
        /// Closed (a <paramref name="shell"/> storey): two glazed leaves with a centre stile, bottom rails and pull handles.
        /// Open (a walk-in storey, which the player walks through): the frame, transom and fanlight only.
        /// </summary>
        public static void GlazedDoor(MeshBuilder op, MeshBuilder? gl, Frame F, Opening o, double y, Palette C, bool shell)
        {
            double T = Dim.T_EXT, wa = T * 0.3, wb = T * 0.6, fw = 0.08, top = y + o.y1, yt = top - 0.5, um = (o.u0 + o.u1) / 2;
            op.OBox(F, o.u0, o.u0 + fw, y, top, wa, wb, C.frame, null, Skip.UStart | Skip.Top | Skip.Bot);
            op.OBox(F, o.u1 - fw, o.u1, y, top, wa, wb, C.frame, null, Skip.UEnd | Skip.Top | Skip.Bot);
            op.OBox(F, o.u0 + fw, o.u1 - fw, top - fw, top, wa, wb, C.frame, null, Skip.Top | Skip.UStart | Skip.UEnd);
            op.OBox(F, o.u0 + fw, o.u1 - fw, yt - 0.04, yt + 0.04, wa, wb, C.frame, null, Skip.UStart | Skip.UEnd);
            if (shell || gl == null) Facade.Pane(op, F, o.u0 + fw, o.u1 - fw, yt + 0.04, top - fw, T * 0.45, C.glassDark, false);
            else Facade.Pane(gl, F, o.u0 + fw, o.u1 - fw, yt + 0.04, top - fw, T * 0.45, C.glass, true);
            if (!shell) return;
            op.OBox(F, um - 0.04, um + 0.04, y, yt - 0.04, wa, wb, C.frame, null, Skip.Top | Skip.Bot);
            op.OBox(F, o.u0 + fw, um - 0.04, y, y + 0.15, wa, wb, C.frame, null, Skip.Bot | Skip.UStart | Skip.UEnd);
            op.OBox(F, um + 0.04, o.u1 - fw, y, y + 0.15, wa, wb, C.frame, null, Skip.Bot | Skip.UStart | Skip.UEnd);
            Facade.Pane(op, F, o.u0 + fw, o.u1 - fw, y + 0.15, yt - 0.04, T * 0.45, C.glassDark, false);
            foreach (double hu in new[] { um - 0.12, um + 0.1 }) op.OBox(F, hu, hu + 0.02, y + 0.9, y + 1.35, wb, wb + 0.05, C.metal, null, Skip.In);
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
        public static void WallOps(MeshBuilder gb, Frame F, double uA, double uB, double y0, double h, double w0, double w1, List<Opening> ops, Swatch c, Swatch? cIn, Skip skip = Skip.None)
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
            public double? revealFrom; public Swatch? revealC;
        }

        /// <summary>
        /// Wall panel: each face one polygon with the openings cut out, triangulated once. Only visible
        /// surfaces: outer face, inner face (optional), the reveals of each opening, end faces for
        /// free-standing walls.
        /// </summary>
        public static void WallPanel(MeshBuilder gb, Frame F, Miter m, double w0, double w1, double y0, double h, List<Opening> ops, Swatch cOut, Swatch cIn, PanelOpt? opt = null)
        {
            opt ??= new PanelOpt();
            var os = MergeOps(ops);
            P3 X(double uu, double yy, double wv) => F.At(uu, y0 + yy, wv);
            void Face(double wv, int sign, Swatch c)
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
            double rw = opt.revealFrom ?? w0; Swatch rc = opt.revealC ?? cOut;
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
        public static void Pane(MeshBuilder gb, Frame F, double u0, double u1, double y0, double y1, double wv, Swatch c, bool two)
        {
            var q = new[] { F.At(u0, y0, wv), F.At(u1, y0, wv), F.At(u1, y1, wv), F.At(u0, y1, wv) };
            gb.Poly(q, new P3(F.w.x, 0, F.w.z), c);
            if (two) gb.Poly((P3[])q.Clone(), new P3(-F.w.x, 0, -F.w.z), c);
        }

        /// <summary>A strip split into pieces at ranges; the first and last take the mitred ends.</summary>
        public static void StripPieces(MeshBuilder gb, Frame F, Miter m, List<(double, double)> ranges, double y0, double y1, double wa, double wb, Swatch c, Swatch? cIn, Skip skip = Skip.None)
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
