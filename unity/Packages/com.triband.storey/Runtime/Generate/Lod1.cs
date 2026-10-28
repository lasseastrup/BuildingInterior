#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// LOD1: exterior shell only. Outer faces with opaque windows, doors, floor bands, plinth,
    /// parapet and stair/lift bulkheads. No interior, frames or transparency, and no
    /// per-vertex occlusion data (a lean mesh). A plain window is a pane just proud of a flat wall,
    /// and the shader draws the recess LOD0 cuts behind it, its reveals as the view shows them
    /// (StoreyWindow.hlsl): cutting the wall round every window made the walls four-fifths of LOD1.
    /// Doors keep their openings, and so do artist-made windows whose LOD1 reaches into the wall; one whose LOD1 stays in
    /// front of it (shutters, a sill) lies on the flat wall with the plain pane (docs/OPENINGS.md §5).
    /// </summary>
    public static class Lod1
    {
        public const int LOD_TAG = 65536;

        /// <summary>
        /// How far a flat wall's window pane stands proud of the wall (metres): clear of it at LOD1's distances. The shader's
        /// recess (STOREY_PANE_RECESS) is from here to LOD0's pane, <see cref="Dim.T_EXT"/> × 0.55 into the wall.
        /// </summary>
        public const double PaneProud = 0.03;

        /// <summary>
        /// The openings of <paramref name="ops"/> LOD1 cuts the wall for, in order, into <paramref name="into"/> (emptied first):
        /// doors, and an artist's windows whose LOD1 reaches into the wall (<see cref="Cut"/>).
        /// </summary>
        static List<Opening> Cuts(List<Opening> ops, List<Opening> into)
        {
            into.Clear();
            foreach (var o in ops) if (o.door || Cut(o)) into.Add(o);
            return into;
        }

        /// <summary>A window LOD1 cuts the wall for: an artist's whose LOD1 reaches into it, or comes nearer it than the flat pane (docs/OPENINGS.md §5).</summary>
        static bool Cut(Opening o) => !o.door && o.kind?.lod1 != null && !o.kind.lod1OnWall;

        /// <param name="recessed">The prototype's LOD1: every window cut into the wall with its reveals (the parity tests).</param>
        public static MeshBuilder Build(Site site, BuildingData b, bool solids = false, bool recessed = false)
        {
            int idx = site.IndexOf(b), N = b.floors.Count; double T = Dim.T_EXT;
            var op = MeshBuilder.Make(idx + LOD_TAG, lean: true);
            if (solids) op.Solids = new List<Solid>();
            var L0 = new Lod0.Shared(site, b);
            L0.Slab(op, N, L0.At(N).C, true, true);
            for (int k = 0; k < N; k++)
            {
                double h = Derived.FloorH(b, k), y = Derived.FloorBase(b, k); var g = L0.At(k); var C = g.C;
                if (Derived.IsSetback(b, k))
                {
                    var Cb = L0.At(k - 1).C; L0.Slab(op, k, Cb, true, true);
                    if (Roofs.Draw(op, Roofs.TerraceRoof(site, b, k), Cb)) L0.TerraceTops(op, k, Cb); else L0.Terrace(op, null, k, Cb);
                    L0.Overhang(op, k, C);
                }
                for (int i = 0; i < g.Wp.Count; i++)
                {
                    var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L); var (uS, uE) = Facade.Span(m, e.L);
                    L0.WallPool.Reset();
                    var all = Facade.Ops(site, b, k, i, e.L, uS, uE, L0.WallOps, L0.WallPool);
                    foreach (var pc in Party.WallPieces(site, b, k, i, e.L, m))
                    {
                        if (pc.kind == Party.PieceKind.Skip) continue;
                        bool party = pc.kind == Party.PieceKind.Party;
                        var ops = L0.Piece(all, party, pc.lo, pc.hi);
                        var stk = Derived.StyleAt(b, k);
                        // the wall is cut for doors, and round an artist's windows whose LOD1 reaches into it; a plain window lies on
                        // it, as does an artist's whose LOD1 stays in front of it (recessed: all are cut). Each opening has its own
                        // window, the one its style's set picked (docs/OPENINGS.md §7.1); the cut ones' reveals go as deep as the style
                        // recesses an artist's window, the doors' as deep as it recesses theirs
                        var cut = recessed ? ops : Cuts(ops, L0.CutOps);
                        OpeningKind? cw = null, cd = null;
                        foreach (var o in cut) { if (o.door) cd ??= o.kind; else cw ??= o.kind; }
                        Facade.WallPanel(op, F, new Miter(pc.S, pc.E), 0, T, y, h, cut, party ? pc.r!.pInner : Lod0.Shared.Wall(stk, C, k == 0), C.wall, L0.Panel.Set(inner: false, revealFrom: Facade.ShellRevealFrom(stk, false, cw), threshold: k == 0,
                            doorRevealFrom: Facade.ShellRevealFrom(stk, true, cd), thresholdC: k == 0 ? C.foundation : (Swatch?)null, doorsRecessed: stk.doorRecess.HasValue));
                        var wallC = Lod0.Shared.Wall(stk, C, k == 0);
                        foreach (var o in ops)
                        {
                            // an artist's window or door with a LOD1 mesh, and the plain pane where it leaves the hole open; where the
                            // style closes its recesses, their backs in the wall's colour instead (a door's own panel in front of it)
                            var kk = o.kind;
                            if (kk?.lod1 != null)
                            {
                                // pushed back by the style's recess, as LOD0's, where it sits in a hole; one on the flat wall stays on it
                                OpeningKinds.Place(op, null, F, kk, kk.lod1, o.u0 + o.hl, o.u1 - o.hr, o.door ? y : y + o.y0, y + o.y1 - o.ht, C, true, o.door || recessed || Cut(o) ? Facade.KindBack(stk, o) : 0);
                                if ((o.door || recessed || Cut(o)) && Facade.RecessBack(op, F, o, y, stk, wallC)) continue;
                                if (kk.lod1Covers) continue;
                            }
                            else if (o.door && kk != null && Facade.RecessBack(op, F, o, y, stk, wallC))
                            {
                                Facade.Pane(op, F, o.u0 + Math.Max(0, o.hl), o.u1 - Math.Max(0, o.hr), y, y + o.y1 - Math.Max(0, o.ht), Facade.PaneW(stk, o) + 0.01, C.door, false);
                                continue;
                            }
                            bool flat = !recessed && !o.door && !Cut(o);   // a window on the flat wall: the shader draws its recess, and LOD0's centre bar
                            Facade.Pane(op, F, o.u0, o.u1, y + o.y0, y + o.y1, flat ? T + PaneProud : Facade.PaneW(stk, o), o.door ? C.door : C.glassDark, false, flat, flat && kk == null && Facade.CentreBar(stk, o));
                        }
                        if (!party) L0.PieceStrips(op, k, F, m, pc, ops, y, h, C);
                    }
                }
                Details.Build(op, site, b, k, C, 1);
                foreach (var br in b.bridges) if (br.k == k && Bridges.Span(site, b, br) is BridgeSpan bs) Bridges.Build(op, null, null, bs, C);
                foreach (var v in b.voids)
                {
                    if (Courtyards.WallsAt(b, v, k)) Courtyards.Walls(op, null, null, b, v, k, C);
                    if (v.kind == VoidKind.Courtyard && k == v.bottom) Courtyards.Pave(op, b, v, C);
                }
            }
            { var g = L0.At(N); L0.Roof(op, null, g, g.C); }
            if (!Roofs.IsPitched(b)) foreach (var v in b.voids) if (v.kind == VoidKind.Atrium && Courtyards.HoleAt(b, v, N)) Courtyards.Skylight(op, null, null, b, v, L0.At(N).C);
            if (b.interior)
            {
                var C = L0.At(N).C; double y = Derived.RoofY(b);
                foreach (var s in b.shafts)
                {
                    if (!Cores.ShaftRoof(b, s) || s.type == CoreType.Flight) continue;   // straight flights come up through a railed opening
                    var f = Cores.FrameOf(b, s); bool st = s.type == CoreType.Stairs;
                    double hx = st ? 1.45 : 1.35, hz = st ? 2.75 : 1.35, hh = st ? 2.7 : 2.9, cx = st ? 1.55 : 1.45, cz = st ? 2.85 : 1.45;
                    op.OBox(f, -hx, hx, y, y + hh, -hz, hz, C.core, C.core, Skip.Top | Skip.Bot);
                    op.OBox(f, -cx, cx, y + hh, y + hh + 0.2, -cz, cz, C.roof);
                }
            }
            return op;
        }
    }
}
