#nullable enable
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// LOD1: exterior shell only. Outer faces with opaque windows, doors, floor bands, plinth,
    /// canopies, parapet and stair/lift bulkheads. No interior, frames or transparency, and no
    /// per-vertex occlusion data (a lean mesh). A plain window is a pane just proud of a flat wall,
    /// its reveal shaded in the shader (StoreyFragment): cutting the wall round every window made
    /// the walls four-fifths of LOD1. Doors and artist-made windows keep their openings.
    /// </summary>
    public static class Lod1
    {
        public const int LOD_TAG = 65536;

        /// <summary>How far a flat wall's window pane stands proud of the wall (metres): clear of it at LOD1's distances.</summary>
        public const double PaneProud = 0.03;

        /// <param name="recessed">The prototype's LOD1: every window cut into the wall with its reveals (the parity tests).</param>
        public static MeshBuilder Build(Site site, BuildingData b, bool solids = false, bool recessed = false)
        {
            int idx = site.IndexOf(b), N = b.floors.Count; double T = Dim.T_EXT;
            var op = new MeshBuilder(idx + LOD_TAG, lean: true);
            if (solids) op.Solids = new List<Solid>();
            site.partyMemo.TryRemove(b.id, out _);
            var L0 = new Lod0.Shared(site, b);
            L0.Slab(op, N, L0.At(N).C, true, true);
            for (int k = 0; k < N; k++)
            {
                double h = Derived.FloorH(b, k), y = Derived.FloorBase(b, k); var g = L0.At(k); var C = g.C;
                if (Derived.IsSetback(b, k))
                {
                    var Cb = L0.At(k - 1).C; L0.Slab(op, k, Cb, true, true);
                    if (!Roofs.Draw(op, Roofs.TerraceRoof(site, b, k), Cb)) L0.Terrace(op, null, k, Cb);
                    L0.Overhang(op, k, C);
                }
                for (int i = 0; i < g.Wp.Count; i++)
                {
                    var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var F = e.F; var m = Geo.MiterOf(g.cor, i, e.L); var (uS, uE) = Facade.Span(m, e.L);
                    var all = Facade.Ops(site, b, k, i, e.L, uS, uE);
                    foreach (var pc in Party.WallPieces(site, b, k, i, e.L, m))
                    {
                        if (pc.kind == Party.PieceKind.Skip) continue;
                        bool party = pc.kind == Party.PieceKind.Party;
                        var ops = party ? new List<Opening>() : all.FindAll(o => o.u0 >= pc.lo && o.u1 <= pc.hi);
                        var stk = Derived.StyleAt(b, k);
                        var wk = OpeningKinds.Of(stk, false); var dk = OpeningKinds.Of(stk, true);
                        // the wall is cut for doors and artist-made windows only; a plain window lies on it (recessed: all)
                        var cut = recessed ? ops : ops.FindAll(o => o.door || wk?.lod1 != null);
                        Facade.WallPanel(op, F, new Miter(pc.S, pc.E), 0, T, y, h, cut, party ? pc.r!.pInner : C.wall, C.wall, new Facade.PanelOpt { inner = false, revealFrom = T * 0.45, threshold = k == 0 });
                        foreach (var o in ops)
                        {
                            // an artist's window or door with a LOD1 mesh takes the opening; without one, the plain pane
                            var kk = o.door ? (o.bare ? null : dk) : wk;
                            if (kk?.lod1 != null) { OpeningKinds.Place(op, null, F, kk, kk.lod1, o.u0, o.u1, o.door ? y : y + o.y0, y + o.y1, C, true); continue; }
                            bool glazedDoor = o.door && Derived.StyleAt(b, k).doorType == DoorType.Glazed;
                            Facade.Pane(op, F, o.u0, o.u1, y + o.y0, y + o.y1, recessed || o.door ? T * 0.45 : T + PaneProud, o.door && !glazedDoor ? C.door : C.glassDark, false);
                            if (o.door && !o.bare && !glazedDoor && kk == null) op.OBox(F, o.u0 - 0.35, o.u1 + 0.35, y + o.y1 + 0.1, y + o.y1 + 0.24, T, T + 1.1, C.trim, C.trim, Skip.In);
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
