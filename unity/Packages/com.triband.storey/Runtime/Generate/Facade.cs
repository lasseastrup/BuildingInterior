#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>Window and door layout along a facade edge, shared by every LOD.</summary>
    public static class Facade
    {
        public sealed class WinSpec { public double sp, w, y0, y1; public double? full; }

        /// <summary>
        /// The window spec of a style's upper floors, for the window that stands for its set (<see cref="OpeningSets.Representative"/>):
        /// the one window of a style without a set. None where every window it can pick is none.
        /// </summary>
        public static WinSpec? WindowSpec(WindowType kind, double h, FacadeStyle st, bool storefront = false) =>
            OpeningSets.Representative(st, false, out var k) ? Fit(RawSpec(kind, h, st, storefront), h, st, null, k) : null;

        /// <summary>The window spec of a style's upper floors for window <paramref name="k"/> (null: the generator's), one of its set's.</summary>
        public static WinSpec? WindowSpec(WindowType kind, double h, FacadeStyle st, OpeningKind? k) => Fit(RawSpec(kind, h, st, false), h, st, null, k);

        static WinSpec? RawSpec(WindowType kind, double h, FacadeStyle st, bool storefront)
        {
            if (storefront) return new WinSpec { sp = st.bay, full = 0.4, y0 = 0.4, y1 = h - 0.75 };
            double y0 = SillOf(kind);
            switch (kind)
            {
                case WindowType.Punched: return new WinSpec { sp = st.bay, w = st.winW, y0 = y0, y1 = Math.Min(h - 0.55, 2.25) };
                case WindowType.Tall: return new WinSpec { sp = st.bay, w = st.winW, y0 = y0, y1 = h - 0.5 };
                case WindowType.Ribbon: return new WinSpec { sp = st.bay, full = 0.2, y0 = y0, y1 = h - 0.6 };
                case WindowType.Curtain: return new WinSpec { sp = st.bay, full = 0.07, y0 = y0, y1 = h - 0.1 };
                default: return null;
            }
        }

        /// <summary>How high a window type's windows start over the floor (metres): what an artist's window of its own size takes without <see cref="FacadeStyle.sillH"/>.</summary>
        public static double SillOf(WindowType kind) => kind switch { WindowType.Tall => 0.4, WindowType.Ribbon => 0.95, WindowType.Curtain => 0.1, _ => 0.9 };

        /// <summary>The ground floor's window spec: the style's own, none, or a storefront; for the window that stands for its set, as <see cref="WindowSpec(WindowType, double, FacadeStyle, bool)"/>.</summary>
        public static WinSpec? GroundSpec(FacadeStyle st, double h) => OpeningSets.Representative(st, true, out var k) ? GroundSpec(st, h, k) : null;

        /// <summary>The ground floor's window spec for window <paramref name="k"/> (null: the generator's), one of its set's.</summary>
        public static WinSpec? GroundSpec(FacadeStyle st, double h, OpeningKind? k)
        {
            var ws = st.ground == GroundType.Match ? RawSpec(st.windows, h, st, false) : st.ground == GroundType.Solid ? null : RawSpec(st.windows, h, st, true);
            // a plinth (or foundation) of the style's own height: the ground floor's windows start above it
            double? plinth = st.plinthH.HasValue || st.foundation.HasValue ? Math.Max(st.plinthH ?? 0.45, st.foundation.HasValue ? Math.Max(0.02, st.foundationH ?? 0.2) : 0) : (double?)null;
            return Fit(ws, h, st, plinth, k);
        }

        /// <summary>
        /// A window spec finished for its style (docs/OPENINGS.md §4.1, §4.2). An artist's window that keeps its size
        /// (<see cref="OpeningKind.stretch"/> off) makes the hole its own size, the style still deciding where it goes and its
        /// sill's height (<see cref="FacadeStyle.sillH"/>, or its type's); and its dressing stays clear of the floor, of a
        /// plinth (<paramref name="plinth"/>: its top, on the ground floor), of the storey's top and of a band. The generator's
        /// own windows, and their arched heads, stay under a band, and over a plinth. A hole left 40 cm high or less is none:
        /// <see cref="Ops"/> lays out no windows there, and LOD2 paints none. <paramref name="k"/>: the window in it (null: the generator's).
        /// </summary>
        static WinSpec? Fit(WinSpec? ws, double h, FacadeStyle st, double? plinth, OpeningKind? k)
        {
            if (ws == null) return null;
            if (k != null && !k.stretch && st.sillH is double sill) ws.y0 = sill;
            double sy = k != null && OpeningKinds.Stretched(k) ? (ws.y1 - ws.y0) / Math.Max(1e-3, k.h) : 1, rb = (k?.reachB ?? 0) * sy, rt = (k?.reachT ?? 0) * sy;
            if (plinth is double p) ws.y0 = Math.Max(ws.y0, p + Math.Max(0.15, rb + 0.05));
            if (k != null)
            {
                ws.y0 = Math.Max(ws.y0, rb + 0.05);
                if (!k.stretch) { ws.full = null; ws.w = k.w; ws.y1 = ws.y0 + k.h; }
                ws.y1 = Math.Min(ws.y1, h - Math.Max(0.1, rt + 0.05));
            }
            // a band of the style's own height: windows (and their heads) stay under it
            if (st.bandH is double bh && st.bands) ws.y1 = Math.Min(ws.y1, h - bh - (k != null ? Math.Max(0.1, rt + 0.05) : st.head == HeadType.Arch ? 0.3 : 0.1));
            return ws.y1 - ws.y0 > 0.4 ? ws : null;
        }

        /// <summary>
        /// Where windows are laid out along a wall: the span (<see cref="Span"/>) less how far an artist's window reaches past
        /// its hole each side, so its dressing keeps the span's 14 cm from a neighbouring wall too. LOD2 paints its windows from
        /// the same span.
        /// </summary>
        public static (double, double) WindowSpan(OpeningKind? wk, double uS, double uE) => (uS + (wk?.reachL ?? 0), uE - (wk?.reachR ?? 0));

        /// <summary>The span a style's ground-floor (<paramref name="ground"/>) or upper windows are laid out in: less the most any of its set reaches (<see cref="OpeningSets.Reach"/>).</summary>
        public static (double, double) WindowSpan(FacadeStyle st, bool ground, double uS, double uE)
        {
            var (l, r) = OpeningSets.Reach(st, ground);
            return (uS + l, uE - r);
        }

        /// <summary>A street door's top: 2.6 m, or an artist's door's own height when it keeps its size; with its dressing, under the storey's top and a band (<paramref name="band"/>: its height).</summary>
        static double DoorTop(OpeningKind? dk, double h, double? band)
        {
            double top = dk != null && !dk.stretch ? Math.Min(dk.h, h - 0.4) : Math.Min(2.6, h - 0.4);
            if (dk == null) return band is double b0 ? Math.Min(top, h - b0 - 0.08) : top;
            double over = Math.Max(0.08, dk.reachT + 0.05);   // its posts stand 8 cm over a generated door (Dress)
            top = Math.Min(top, h - Math.Max(0.1, dk.reachT + 0.05));
            return band is double b ? Math.Min(top, h - b - over) : top;
        }

        /// <summary>The artist's window or door in an opening (null: the generator's), and its dressing: how far it reaches past it, as it is fitted there.</summary>
        public static void Dressing(Opening o, OpeningKind? k)
        {
            o.kind = k;
            if (k == null) { o.rl = o.rr = o.rb = o.rt = 0; return; }
            bool whole = OpeningKinds.Stretched(k);   // sliced, what is round the hole keeps its size
            double sx = whole ? (o.u1 - o.u0) / Math.Max(1e-3, k.w) : 1, sy = whole ? (o.y1 - o.y0) / Math.Max(1e-3, k.h) : 1;
            o.rl = k.reachL * sx; o.rr = k.reachR * sx; o.rb = k.reachB * sy; o.rt = k.reachT * sy;
        }

        /// <summary>
        /// Whether a window from <paramref name="u0"/> to <paramref name="u1"/>, its dressing reaching <paramref name="l"/> and
        /// <paramref name="r"/> past it, comes too near a door: 30 cm between them, and 10 cm between their dressings.
        /// </summary>
        public static bool NearDoor(Opening door, double u0, double u1, double l, double r) =>
            u1 > door.u0 - Math.Max(0.3, r + door.rl + 0.1) && u0 < door.u1 + Math.Max(0.3, l + door.rr + 0.1);

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
        public static List<Opening> Ops(Site site, BuildingData b, int k, int i, double L, double uS, double uE) => Ops(site, b, k, i, L, uS, uE, new List<Opening>(), null);

        /// <summary>
        /// <see cref="Ops(Site, BuildingData, int, int, double, double, double)"/> into <paramref name="ops"/> (emptied first), the
        /// openings taken from <paramref name="pool"/> (new ones when it is null): for a caller doing one wall after another.
        /// </summary>
        public static List<Opening> Ops(Site site, BuildingData b, int k, int i, double L, double uS, double uE, List<Opening> ops, OpeningPool? pool)
        {
            var st = Derived.StyleAt(b, k); double h = Derived.FloorH(b, k), spn = uE - uS;
            var doorSet = OpeningSets.Doors(st); var dk = doorSet == null ? OpeningKinds.Of(st, true) : null;
            // a band of the style's own height: doors stay under it as windows do (WindowSpec), their posts too (Dress: 8 cm
            // above the door, standing out as far as a band does by default, so they would lie in its face)
            double? band = st.bandH is double bh && st.bands ? bh : (double?)null;
            double under = band is double b0 ? h - b0 - 0.08 : double.MaxValue;
            ops.Clear();
            Opening New() => pool != null ? pool.Take() : new Opening();
            foreach (var e in b.entrances)
            {
                if (e.k != k || e.edge != i) continue;
                // the style's street door, or the one its set picks for this wall (docs/OPENINGS.md §7.1)
                var ek = doorSet == null ? dk : OpeningSets.Door(st, OpeningSets.DoorSeed(b.id, k, i));
                // a door's dressing keeps the span's 14 cm from a neighbouring wall, as a window's does
                double sl = Math.Max(0.3, ek?.reachL ?? 0), sr = Math.Max(0.3, ek?.reachR ?? 0);
                if (spn <= Dim.DOOR_EXT + sl + sr || !DoorOpen(site, b, e)) continue;
                double c = Math.Max(uS + Dim.DOOR_EXT / 2 + sl, Math.Min(uE - Dim.DOOR_EXT / 2 - sr, e.t * L));
                var land = k > 0 && !TerraceAt(b, k, i, e.t) ? LandingAt(site, b, k, i, e.t) : null;
                var o = New().Set(c - Dim.DOOR_EXT / 2, c + Dim.DOOR_EXT / 2, 0, DoorTop(ek, h, band), door: true, thr: land != null && land.y < Derived.FloorBase(b, k) - 0.02);
                Dressing(o, ek);
                ops.Add(o);
            }
            foreach (var (bk, bi, bt) in Bridges.Doors(site, b))
            {
                if (bk != k || bi != i || spn <= Dim.DOOR_EXT + 0.6) continue;
                double c = Math.Max(uS + Dim.DOOR_EXT / 2 + 0.3, Math.Min(uE - Dim.DOOR_EXT / 2 - 0.3, bt * L));
                ops.Add(New().Set(c - Dim.DOOR_EXT / 2, c + Dim.DOOR_EXT / 2, 0, Math.Min(Math.Min(2.5, h - 0.4), under), door: true, bare: true));
            }
            List<Cores.Flush>? shaftHere = null;   // made only where a lift is flush with this edge
            foreach (var s in b.shafts)
            {
                if (s.type != CoreType.Lift || !Cores.Serves(b, s, k)) continue;
                foreach (var f in Cores.CoreFlush(Derived.OutlineAt(b, k), s)) if (f != null && f.i == i) (shaftHere ??= new List<Cores.Flush>()).Add(f);
            }
            var blank = Derived.TierStart(b, k) > 0 ? b.floors[Derived.TierStart(b, k)].blank : b.blank;
            if (!blank.Contains(i)) Windows(ops, pool, st, h, k == 0, b.id, k, Derived.TierStart(b, k), i, uS, uE, shaftHere);
            ops.Sort((p, q) => p.u0.CompareTo(q.u0));
            return ops;
        }

        /// <summary>
        /// A wall's windows into <paramref name="ops"/> (its doors already there), from <paramref name="pool"/> (new ones when
        /// null): the style's bays along the span, each with the window its set picks (docs/OPENINGS.md §7.1), or none, each
        /// opening the size that window makes it. The bays are laid out once for every window the set can pick, so a pick
        /// never moves another window. <paramref name="ground"/>: the ground floor (its spec, and its own set); the pick is
        /// seeded by <paramref name="id"/>, the storey <paramref name="k"/> of the tier from <paramref name="k0"/>, and edge
        /// <paramref name="edge"/>. Windows keep clear of doors and of <paramref name="shafts"/> (lifts flush with the wall).
        /// </summary>
        internal static void Windows(List<Opening> ops, OpeningPool? pool, FacadeStyle st, double h, bool ground, string id, int k, int k0, int edge, double uS, double uE,
            List<Cores.Flush>? shafts)
        {
            var set = OpeningSets.Windows(st, ground);
            var (wS, wE) = WindowSpan(st, ground, uS, uE);
            var (n, bay) = BayLayout(st, wS, wE);
            if (n == 0) return;
            // one window: its spec once; a set: each window's it picks, once each, in specs lent for the wall
            OpeningKind? one = set == null ? OpeningKinds.Of(st, false) : null;
            WinSpec? oneSpec = set == null ? ground ? GroundSpec(st, h, one) : WindowSpec(st.windows, h, st, one) : null;
            var specs = set != null ? SpecCache.Lent.Take() : null;
            uint wall = set != null ? OpeningSets.WallSeed(id, k, edge) : 0;
            for (int j = 0; j < n; j++)
            {
                OpeningKind? wk = one;
                if (set != null && !OpeningSets.Window(st, ground, st.windowPick == WindowPick.Window ? OpeningSets.At(wall, j) : OpeningSets.WindowSeed(id, st.windowPick, k, k0, edge, j), out wk)) continue;
                var ws = specs != null ? specs.Of(wk, st, h, ground) : oneSpec;
                if (ws == null || ws.y1 - ws.y0 <= 0.4) continue;
                double c = wS + (j + 0.5) * bay, ww = ws.full != null ? bay - ws.full.Value : Math.Min(ws.w, bay - 0.5);
                if (ww < 0.45) continue;
                double u0 = c - ww / 2, u1 = c + ww / 2, sx = wk != null && OpeningKinds.Stretched(wk) ? ww / Math.Max(1e-3, wk.w) : 1;
                bool blocked = false;
                foreach (var o in ops) if (o.door && NearDoor(o, u0, u1, (wk?.reachL ?? 0) * sx, (wk?.reachR ?? 0) * sx)) blocked = true;
                if (shafts != null) foreach (var f in shafts) if (u1 > f.s0 - 0.1 && u0 < f.s1 + 0.1) blocked = true;
                if (blocked) continue;
                var w = (pool != null ? pool.Take() : new Opening()).Set(u0, u1, ws.y0, ws.y1, full: ws.full != null);
                Dressing(w, wk);
                ops.Add(w);
            }
            if (specs != null) { specs.Clear(); SpecCache.Lent.Give(specs); }
        }

        /// <summary>
        /// The window specs of one wall's windows, one for each window its set picks there, made once each (a set's few
        /// windows on a wall of many): lent for the wall, and given back emptied.
        /// </summary>
        sealed class SpecCache
        {
            public static readonly Lender<SpecCache> Lent = new Lender<SpecCache>(8);

            readonly List<OpeningKind?> kinds = new List<OpeningKind?>(4);
            readonly List<WinSpec?> specs = new List<WinSpec?>(4);

            public WinSpec? Of(OpeningKind? k, FacadeStyle st, double h, bool ground)
            {
                for (int i = 0; i < kinds.Count; i++) if (ReferenceEquals(kinds[i], k)) return specs[i];
                var ws = ground ? GroundSpec(st, h, k) : WindowSpec(st.windows, h, st, k);
                kinds.Add(k); specs.Add(ws);
                return ws;
            }

            public void Clear() { kinds.Clear(); specs.Clear(); }
        }

        /// <summary>
        /// What dresses a facade's openings in LOD0: window sills, heads and mullions, and the panes (see-through glass in
        /// <paramref name="gl"/>; opaque panes on a <paramref name="shell"/> storey); and doors, a plain panel at the back of
        /// the recess on a shell storey and the doorway open on a walk-in one (docs/EDITOR.md §6.7). The style's own details
        /// (<see cref="FacadeStyle.head"/>, panes, frames) change what is drawn on windows; without them, windows are the
        /// prototype's. The prototype's doors have a surround and a canopy, which Storey's leave out.
        /// </summary>
        public static void Dress(MeshBuilder op, MeshBuilder gl, Frame F, List<Opening> ops, double y, Palette C, bool shell, FacadeStyle? st = null)
        {
            if (op.ShapeOnly) return;   // faces only: no walls numbered, no segments
            double T = Dim.T_EXT;
            bool frames = st != null && st.frames, arch = st != null && st.head == HeadType.Arch;
            bool bars = st != null && (st.paneCols > 1 || st.paneRows > 1);
            bool sills = st == null || st.sills, heads = st == null || st.heads;
            var trim = frames ? C.frame : C.trim;
            // an artist's window or door (OpeningKinds), the one picked for the opening, stands in for all of the generator's dressing
            foreach (var o in ops)
            {
                var kk = o.kind;
                if (o.door && kk != null) { OpeningKinds.Place(op, gl, F, kk, kk.lod0, o.u0, o.u1, y, y + o.y1, C, shell); if (!shell) op.SeeThrough++; continue; }   // its leaf is left out
                if (!o.door && kk != null) { OpeningKinds.Place(op, gl, F, kk, kk.lod0, o.u0, o.u1, y + o.y0, y + o.y1, C, shell); continue; }
                if (o.door)
                {
                    // the generator's door: the wall's recess, with a flat panel at its back where the doorway is closed
                    if (shell) Facade.Pane(op, F, o.u0, o.u1, y, y + o.y1, T * 0.45, C.door, false);
                    else op.SeeThrough++;
                }
                else
                {
                    if (shell) Facade.Pane(op, F, o.u0, o.u1, y + o.y0, y + o.y1, T * 0.45, C.glassDark, false);
                    else Facade.Pane(gl, F, o.u0, o.u1, y + o.y0, y + o.y1, T * 0.45, C.glass, true);
                    double ex = o.full ? 0 : 0.06, eh = o.full ? 0 : 0.04;
                    if (sills) op.OBox(F, o.u0 - ex, o.u1 + ex, y + o.y0 - 0.07, y + o.y0, T, T + 0.07, trim, trim, Skip.In);
                    if (heads && arch && !o.full) Hood(op, F, o.u0 - HoodOver, o.u1 + HoodOver, y + o.y1, trim);
                    else if (heads) op.OBox(F, o.u0 - eh, o.u1 + eh, y + o.y1, y + o.y1 + 0.06, T, T + 0.04, trim, trim, Skip.In);
                    double fw = frames ? FrameW : 0;
                    if (frames) WindowFrame(op, F, o.u0, o.u1, y + o.y0, y + o.y1, fw, C.frame);
                    if (bars) Bars(op, F, o.u0 + fw, o.u1 - fw, y + o.y0 + fw, y + o.y1 - fw, st!.paneCols ?? 1, st.paneRows ?? 1, C.frame);
                    else if (CentreBar(st, o)) { double mm = (o.u0 + o.u1) / 2; op.OBox(F, mm - BarHalf, mm + BarHalf, y + o.y0, y + o.y1, T * 0.3, T * 0.6, C.trim, C.trim, Skip.Top | Skip.Bot); }
                }
            }
        }

        /// <summary>
        /// Whether a plain window gets LOD0's centre bar: a wide one, in a style without glazing bars. The bar is
        /// <see cref="BarHalf"/> either side of the middle, in the trim's colour, from <see cref="Dim.T_EXT"/> × 0.3 to × 0.6
        /// into the wall (it stands proud of the pane). LOD1's flat pane is marked with it, and the shader draws it there
        /// (StoreyPaneRecess).
        /// </summary>
        public static bool CentreBar(FacadeStyle? st, Opening o) => !(st != null && (st.paneCols > 1 || st.paneRows > 1)) && o.u1 - o.u0 > 1.7;

        /// <summary>Half LOD0's centre bar's width, metres.</summary>
        public const double BarHalf = 0.03;

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
                        if (pu < o.Right + 0.12 && pu + pw > o.Left - 0.12 && pv < y + o.Top + 0.45 && pv + ph > y + o.Bottom - 0.15) { clear = false; break; }
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
                            op.Quad(F.At(bu, bv, wv), F.At(bu + BrickW, bv, wv), F.At(bu + BrickW, bv + BrickH, wv), F.At(bu, bv + BrickH, wv), nr, c);
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
                op.Quad(At(x0, Top(x0), w0), At(x1, Top(x1), w0), At(x1, Top(x1), w1), At(x0, Top(x0), w1), Up(-dTop(xm), 1), c);
                op.Quad(At(x0, Bot(x0), w0), At(x0, Bot(x0), w1), At(x1, Bot(x1), w1), At(x1, Bot(x1), w0), Up(dBot(xm), -1), c);
            }
            var side = new P3(F.u.x, 0, F.u.z);
            op.Quad(At(-half, Bot(-half), w0), At(-half, Top(-half), w0), At(-half, Top(-half), w1), At(-half, Bot(-half), w1), side * -1, c);
            op.Quad(At(half, Bot(half), w0), At(half, Bot(half), w1), At(half, Top(half), w1), At(half, Top(half), w0), side, c);
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

        /// <summary>Stretches between doors (walls to collide with).</summary>
        public static List<(double, double)> SolidRanges(double uA, double uB, List<Opening> ops)
        {
            var out_ = new List<(double, double)>(); double cur = uA;
            foreach (var o in ops) { if (!o.door) continue; if (o.u0 > cur) out_.Add((cur, o.u0)); cur = Math.Max(cur, o.u1); }
            if (uB > cur) out_.Add((cur, uB));
            return out_;
        }

        public static List<Opening> MergeOps(List<Opening> ops) => MergeOps(ops, new List<Opening>(), new List<Opening>(ops.Count), null);

        /// <summary>
        /// <see cref="MergeOps(List{Opening})"/> into <paramref name="into"/>, sorting in <paramref name="sorted"/> (both
        /// emptied first), the copies taken from <paramref name="copies"/> (made as needed) or made new when it is null.
        /// </summary>
        static List<Opening> MergeOps(List<Opening> ops, List<Opening> into, List<Opening> sorted, List<Opening>? copies)
        {
            sorted.Clear(); sorted.AddRange(ops);
            sorted.Sort((p, q) => p.u0.CompareTo(q.u0));
            into.Clear();
            int made = 0;
            foreach (var o in sorted)
            {
                var l = into.Count > 0 ? into[into.Count - 1] : null;
                if (l != null && o.u0 < l.u1 + 1e-3) { l.u1 = Math.Max(l.u1, o.u1); l.y0 = Math.Min(l.y0, o.y0); l.y1 = Math.Max(l.y1, o.y1); l.door = l.door || o.door; }
                else if (copies == null) into.Add(o.Clone());
                else { if (made == copies.Count) copies.Add(new Opening()); into.Add(copies[made++].Set(o)); }
            }
            return into;
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
                if (o.u0 > cur)
                {
                    gb.OBox(F, cur, o.u0, y0, y0 + h, w0, w1, c, cIn, skip);
                    // a skipped end is the wall's (against what it meets at uA or uB); one at an opening is its jamb, which only
                    // Unity draws back (the prototype's faces are two-sided)
                    var jambs = skip & ((cur > uA ? Skip.UStart : Skip.None) | Skip.UEnd);
                    if (jambs != Skip.None) { bool b0 = gb.Backing; gb.Backing = true; gb.OBox(F, cur, o.u0, y0, y0 + h, w0, w1, c, cIn, AllBut(jambs)); gb.Backing = b0; }
                }
                double a = Math.Max(o.u0, cur), e = o.u1;
                if (e > a)
                {
                    if (o.y0 > 0.001) gb.OBox(F, a, e, y0, y0 + o.y0, w0, w1, c, cIn, skip | Skip.Top);
                    if (o.y1 < h - 0.001) gb.OBox(F, a, e, y0 + o.y1, y0 + h, w0, w1, c, cIn, skip | Skip.Bot);
                    // the opening's sill and head, which the prototype leaves to its two-sided faces: only Unity draws them
                    bool was = gb.Backing; gb.Backing = true;
                    if (o.y0 > 0.001) gb.OBox(F, a, e, y0, y0 + o.y0, w0, w1, c, cIn, AllBut(Skip.Top));
                    if (o.y1 < h - 0.001) gb.OBox(F, a, e, y0 + o.y1, y0 + h, w0, w1, c, cIn, AllBut(Skip.Bot));
                    gb.Backing = was;
                }
                cur = Math.Max(cur, o.u1);
            }
            if (uB > cur)
            {
                gb.OBox(F, cur, uB, y0, y0 + h, w0, w1, c, cIn, skip);
                var jamb = cur > uA ? skip & Skip.UStart : Skip.None;
                if (jamb != Skip.None) { bool b0 = gb.Backing; gb.Backing = true; gb.OBox(F, cur, uB, y0, y0 + h, w0, w1, c, cIn, AllBut(jamb)); gb.Backing = b0; }
            }
        }

        /// <summary>Every face of a box skipped but <paramref name="keep"/>.</summary>
        public static Skip AllBut(Skip keep) => (Skip.Out | Skip.In | Skip.UEnd | Skip.UStart | Skip.Top | Skip.Bot) & ~keep;

        public sealed class PanelOpt
        {
            public bool outer = true, inner = true, ends, threshold;
            /// <summary>The inner face is the rooms' (<see cref="MeshBuilder.Inside"/>): an outside wall's, seen only through its windows.</summary>
            public bool rooms;
            public double? revealFrom; public Swatch? revealC;

            /// <summary>Every option set, as a new one with these would be (for options used again panel after panel).</summary>
            public PanelOpt Set(bool outer = true, bool inner = true, bool ends = false, bool threshold = false, double? revealFrom = null, Swatch? revealC = null, bool rooms = false)
            {
                this.outer = outer; this.inner = inner; this.ends = ends; this.threshold = threshold; this.revealFrom = revealFrom; this.revealC = revealC; this.rooms = rooms;
                return this;
            }
        }

        static readonly PanelOpt DefaultPanel = new PanelOpt();   // read only: WallPanel never changes its options

        /// <summary>What <see cref="WallPanel"/> works in, kept by a builder for its next panel (<see cref="MeshBuilder.Panel"/>).</summary>
        internal sealed class PanelScratch
        {
            public readonly TriScratch Tri = new TriScratch();
            public readonly List<Opening> Merged = new List<Opening>(), Sorted = new List<Opening>(), Copies = new List<Opening>();
            public readonly List<Vec2> Cont = new List<Vec2>();
            public readonly List<List<Vec2>> Holes = new List<List<Vec2>>();
            readonly List<List<Vec2>> spare = new List<List<Vec2>>();
            public readonly List<int> Tris = new List<int>();
            public readonly List<P3> Pts = new List<P3>();

            /// <summary>An empty ring added to <see cref="Holes"/>, one used before when there is one.</summary>
            public List<Vec2> Hole()
            {
                int k = Holes.Count;
                if (k == spare.Count) spare.Add(new List<Vec2>(4));
                var h = spare[k]; h.Clear(); Holes.Add(h);
                return h;
            }
        }

        /// <summary>
        /// Wall panel: each face one polygon with the openings cut out, triangulated once. Only visible
        /// surfaces: outer face, inner face (optional), the reveals of each opening, end faces for
        /// free-standing walls.
        /// </summary>
        public static void WallPanel(MeshBuilder gb, Frame F, Miter m, double w0, double w1, double y0, double h, List<Opening> ops, Swatch cOut, Swatch cIn, PanelOpt? opt = null)
        {
            if (gb.ShapeOnly) return;   // faces only: no walls numbered, no segments
            opt ??= DefaultPanel;
            // the outline, holes, triangles and points of each face, and the merged openings, in the builder's own lists
            // (a builder is used by one thread at a time)
            var sc = gb.Panel ??= new PanelScratch();
            var os = MergeOps(ops, sc.Merged, sc.Sorted, sc.Copies);
            P3 X(double uu, double yy, double wv) => F.At(uu, y0 + yy, wv);
            void Face(double wv, int sign, Swatch c)
            {
                double s = m.S.At(wv), e = m.E.At(wv);
                var cont = sc.Cont; cont.Clear(); cont.Add(new Vec2(s, 0));
                foreach (var o in os) if (o.y0 <= 1e-3) { cont.Add(new Vec2(o.u0, 0)); cont.Add(new Vec2(o.u0, o.y1)); cont.Add(new Vec2(o.u1, o.y1)); cont.Add(new Vec2(o.u1, 0)); }
                cont.Add(new Vec2(e, 0)); cont.Add(new Vec2(e, h));
                for (int j = os.Count - 1; j >= 0; j--) { var o = os[j]; if (o.y1 >= h - 1e-3 && o.y0 > 1e-3) { cont.Add(new Vec2(o.u1, h)); cont.Add(new Vec2(o.u1, o.y0)); cont.Add(new Vec2(o.u0, o.y0)); cont.Add(new Vec2(o.u0, h)); } }
                cont.Add(new Vec2(s, h));
                var holes = sc.Holes; holes.Clear();
                foreach (var o in os) if (o.y0 > 1e-3 && o.y1 < h - 1e-3) { var hl = sc.Hole(); hl.Add(new Vec2(o.u0, o.y0)); hl.Add(new Vec2(o.u1, o.y0)); hl.Add(new Vec2(o.u1, o.y1)); hl.Add(new Vec2(o.u0, o.y1)); }
                var tris = sc.Tri.Shape(cont, holes, sc.Tris);
                var pts = sc.Pts; pts.Clear();
                foreach (var q in cont) pts.Add(X(q.x, q.z, wv));
                foreach (var hh in holes) foreach (var q in hh) pts.Add(X(q.x, q.z, wv));
                gb.PolyTris(pts, tris, new P3(F.w.x * sign, 0, F.w.z * sign), c);
            }
            if (opt.outer) Face(w1, 1, cOut);
            if (opt.inner) { bool was = gb.Inside; gb.Inside = was || opt.rooms; Face(w0, -1, cIn); gb.Inside = was; }
            double rw = opt.revealFrom ?? w0; Swatch rc = opt.revealC ?? cOut;
            var nu = new P3(F.u.x, 0, F.u.z);
            foreach (var o in os)
            {
                gb.Quad(X(o.u0, o.y0, rw), X(o.u0, o.y0, w1), X(o.u0, o.y1, w1), X(o.u0, o.y1, rw), nu, rc);
                gb.Quad(X(o.u1, o.y0, rw), X(o.u1, o.y0, w1), X(o.u1, o.y1, w1), X(o.u1, o.y1, rw), new P3(-nu.x, 0, -nu.z), rc);
                if (o.y0 > 1e-3 || opt.threshold || o.thr) gb.Quad(X(o.u0, o.y0, rw), X(o.u1, o.y0, rw), X(o.u1, o.y0, w1), X(o.u0, o.y0, w1), new P3(0, 1, 0), rc);
                if (o.y1 < h - 1e-3) gb.Quad(X(o.u0, o.y1, rw), X(o.u1, o.y1, rw), X(o.u1, o.y1, w1), X(o.u0, o.y1, w1), new P3(0, -1, 0), rc);
            }
            if (opt.ends)
            {
                double s0 = m.S.At(w0), s1 = m.S.At(w1), e0 = m.E.At(w0), e1 = m.E.At(w1);
                gb.Quad(X(s0, 0, w0), X(s1, 0, w1), X(s1, h, w1), X(s0, h, w0), Geo.EndNormal(F, s0, s1, w0, w1, -1), cOut);
                gb.Quad(X(e0, 0, w0), X(e1, 0, w1), X(e1, h, w1), X(e0, h, w0), Geo.EndNormal(F, e0, e1, w0, w1, 1), cOut);
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

        /// <summary>
        /// A glass or door pane filling an opening: one quad (two for glass seen from both sides). <paramref name="flat"/>:
        /// LOD1's pane proud of a flat wall, for which the shader draws the recess LOD0 cuts (<see cref="Lod1.PaneProud"/>), and
        /// <paramref name="bar"/> LOD0's centre bar in it (<see cref="CentreBar"/>).
        /// </summary>
        public static void Pane(MeshBuilder gb, Frame F, double u0, double u1, double y0, double y1, double wv, Swatch c, bool two, bool flat = false, bool bar = false)
        {
            P3 a = F.At(u0, y0, wv), b = F.At(u1, y0, wv), d = F.At(u1, y1, wv), e = F.At(u0, y1, wv);
            int first = gb.Verts;
            gb.Quad(a, b, d, e, new P3(F.w.x, 0, F.w.z), c);
            if (two) gb.Quad(a, b, d, e, new P3(-F.w.x, 0, -F.w.z), c);
            PaneUV(gb, first, F, u0, u1, y0, y1, flat, bar);
        }

        /// <summary>
        /// Where on its window (u0..u1, y0..y1 on the wall) each vertex from <paramref name="first"/> on is, for the window
        /// shader: across along the wall's own "across" (cross(up, outward normal), whichever way the outline runs), so
        /// the shader needs no frame; and the window's number, from its centre on the wall's face, as LOD2's shader works
        /// it out.
        /// </summary>
        public static void PaneUV(MeshBuilder gb, int first, Frame F, double u0, double u1, double y0, double y1, bool flat = false, bool bar = false)
        {
            bool flip = F.u.x * F.w.z - F.u.z * F.w.x < 0;   // F.u runs against cross(up, w) = (w.z, 0, −w.x)
            byte id = WindowId(F.At((u0 + u1) / 2, (y0 + y1) / 2, Dim.T_EXT));
            for (int i = first; i < gb.Verts; i++)
            {
                var p = gb.P[i]; double a = (p.x - F.O.x) * F.u.x + (p.z - F.O.z) * F.u.z;
                double u = Math.Max(0, Math.Min(1, (a - u0) / Math.Max(1e-6, u1 - u0)));
                gb.PaneUV[i] = (flip ? 1 - u : u, Math.Max(0, Math.Min(1, (p.y - y0) / Math.Max(1e-6, y1 - y0))), id, flat, flat && bar);
            }
        }

        /// <summary>
        /// A window's number, 0 to 255, from its centre on the wall's face (site space): which room it shows, and when it
        /// lights. StoreyWindow.hlsl's <c>StoreyWindowId</c> is the same integer hash, so LOD2's painted windows match.
        /// </summary>
        public static byte WindowId(P3 c)
        {
            unchecked
            {
                int ix = (int)Math.Floor(c.x * 4 + 0.5), iy = (int)Math.Floor(c.y * 4 + 0.5), iz = (int)Math.Floor(c.z * 4 + 0.5);
                uint h = ((uint)ix * 73856093u) ^ ((uint)iy * 19349663u) ^ ((uint)iz * 83492791u);
                h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
                return (byte)(h & 255);
            }
        }

        /// <summary>
        /// The openings <paramref name="ops"/> merged, in the builder's own
        /// lists (as <see cref="WallPanel"/> merges them): for a backing face over each, never two over one place.
        /// </summary>
        static List<Opening> Merged(MeshBuilder gb, List<Opening> ops)
        {
            var sc = gb.Panel ??= new PanelScratch();
            return MergeOps(ops, sc.Merged, sc.Sorted, sc.Copies);
        }

        /// <summary>
        /// Where an opening reaches above its storey's ceiling (a curtain wall's, <paramref name="h"/> − the slab), the slab
        /// above shows through its top: its edge, a centimetre into the reveal (clear of a stair hole's side, which lies along
        /// the wall's inner face facing in), facing out. Only Unity draws it (<see cref="MeshBuilder.Backing"/>): the prototype
        /// draws the floors and ceilings two-sided, and shows their backs there. Only clear glass shows it: with the rooms,
        /// when the storey has them (<paramref name="rooms"/>, <see cref="MeshBuilder.Inside"/>).
        /// </summary>
        public static void SlabEdges(MeshBuilder gb, Frame F, List<Opening> ops, double y, double h, Swatch c, bool rooms = false)
        {
            double ceil = h - Dim.SLAB; bool any = false;
            foreach (var o in ops) if (o.y1 > ceil + 1e-3) any = true;
            if (!any || gb.ShapeOnly) return;
            var nw = new P3(F.w.x, 0, F.w.z); bool was = gb.Backing, wasIn = gb.Inside; gb.Backing = true; gb.Inside = wasIn || rooms;
            foreach (var o in Merged(gb, ops))
            {
                if (o.y1 <= ceil + 1e-3) continue;
                double y0 = y + Math.Max(o.y0, ceil), y1 = y + Math.Min(o.y1, h);
                gb.Quad(F.At(o.u0, y0, 0.01), F.At(o.u1, y0, 0.01), F.At(o.u1, y1, 0.01), F.At(o.u0, y1, 0.01), nw, c);
                // and the centimetre between it and the ceiling's edge, level with the ceiling
                if (o.y0 < ceil) gb.Quad(F.At(o.u0, y0, 0), F.At(o.u1, y0, 0), F.At(o.u1, y0, 0.01), F.At(o.u0, y0, 0.01), new P3(0, -1, 0), c);
            }
            gb.Inside = wasIn; gb.Backing = was;
        }

        /// <summary>
        /// A strip on the wall's face (a plinth, a foundation: <paramref name="ranges"/> along it, <paramref name="y0"/> to
        /// <paramref name="y1"/> high, its back at <paramref name="w"/>) has no face against the wall (Skip.In); where it
        /// runs across an opening (the storey's floor at <paramref name="y"/>), the opening shows that side: drawn there,
        /// facing in, by Unity only (<see cref="MeshBuilder.Backing"/>). Only from inside, so from outside only through clear
        /// glass and a room: with the rooms, when the storey has them (<paramref name="rooms"/>, <see cref="MeshBuilder.Inside"/>),
        /// and not at LOD1 (a lean builder), whose windows are opaque.
        /// </summary>
        public static void StripBacks(MeshBuilder gb, Frame F, List<(double, double)> ranges, List<Opening> ops, double y, double y0, double y1, double w, Swatch c, bool rooms = false)
        {
            bool any = false;
            foreach (var o in ops) if (y + o.y0 < y1 - 1e-4 && y + o.y1 > y0 + 1e-4) any = true;
            if (!any || gb.ShapeOnly || gb.Lean) return;
            var n = new P3(-F.w.x, 0, -F.w.z); bool was = gb.Backing, wasIn = gb.Inside; gb.Backing = true; gb.Inside = wasIn || rooms;
            foreach (var o in Merged(gb, ops))
            {
                double b0 = Math.Max(y0, y + o.y0), b1 = Math.Min(y1, y + o.y1); if (b1 - b0 < 1e-4) continue;
                foreach (var (a, e) in ranges)
                {
                    double u0 = Math.Max(a, o.u0), u1 = Math.Min(e, o.u1); if (u1 - u0 < 1e-4) continue;
                    gb.Quad(F.At(u0, b0, w), F.At(u1, b0, w), F.At(u1, b1, w), F.At(u0, b1, w), n, c);
                }
            }
            gb.Inside = wasIn; gb.Backing = was;
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
