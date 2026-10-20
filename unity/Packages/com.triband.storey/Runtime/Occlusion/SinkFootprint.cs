#nullable enable
using System.Collections.Generic;
using Triband.Storey.Generate;

namespace Triband.Storey.Occlusion
{
    /// <summary>
    /// Sink's footprint (SPEC §5.1): what stands in for a building in the way once it has collapsed. The outline of
    /// the storey at the player's height as a flat near-black fill 2 cm above its floor, ringed by a low rim of its
    /// outer wall in the wall's colour, with no doors or interior. Never along a party wall: that stands while
    /// the neighbour does, or both sides sank and their outlines merge. A port of the prototype's <c>buildLid</c>.
    /// Built once per building version and storey, in a builder of the shared ones (<see cref="MeshBuilder.Make"/>): give
    /// it back once uploaded (<see cref="MeshBuilder.Recycle"/>). A fresh builder made 83 KB of garbage for 60 vertices.
    /// </summary>
    public static class SinkFootprint
    {
        public const double RimHeight = 0.3;
        /// <summary>How dark the fill is: the wall colour scaled down to near-black.</summary>
        public const double FillTone = 0.05;

        public static MeshBuilder Build(Site site, BuildingData b, int k)
        {
            var g = new Lod0.Shared(site, b).At(k);
            double y = Derived.FloorBase(b, k), T = Dim.T_EXT; int k0 = Derived.TierStart(b, k);
            var op = MeshBuilder.Make(0, lean: true);
            var tris = Triangulate.Shape(g.Wp, null);
            var pts = new List<P3>(g.Wp.Count); foreach (var q in g.Wp) pts.Add(new P3(q.x, y + 0.02, q.z));
            op.PolyTris(pts, tris, new P3(0, 1, 0), g.C.wall.Shade(FillTone));
            var party = new List<(double, double)>(); var span = new List<(double, double)>(1);
            for (int i = 0; i < g.Wp.Count; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var m = Geo.MiterOf(g.cor, i, e.L);
                party.Clear(); foreach (var r in Party.Ranges(site, b, k0, i)) party.Add((r.s, r.e));
                span.Clear(); span.Add(m.Span(0, T));
                Facade.StripPieces(op, e.F, m, Geo.MinusRanges(span, party), y, y + RimHeight, 0, T, g.C.wall, g.C.wall, Skip.Bot);
            }
            return op;
        }
    }
}
