#nullable enable
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Occlusion
{
    /// <summary>
    /// Sink's footprint (SPEC §5.1): what stands in for a building in the way once it has collapsed. The outline of
    /// the storey at the player's height as a flat near-black fill 2 cm above its floor, ringed by a low rim of its
    /// outer wall in the wall's colour, with no doors, canopies or interior. Never along a party wall: that stands while
    /// the neighbour does, or both sides sank and their outlines merge. A port of the prototype's <c>buildLid</c>.
    /// Built once per building version and storey.
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
            var op = new MeshBuilder(0, lean: true);
            var tris = Triangulate.Shape(g.Wp, null);
            op.PolyTris(g.Wp.Select(q => new P3(q.x, y + 0.02, q.z)).ToList(), tris, new P3(0, 1, 0), g.C.wall.Shade(FillTone));
            for (int i = 0; i < g.Wp.Count; i++)
            {
                var e = Geo.EdgeInfo(g.Wp, i, g.ccw); var m = Geo.MiterOf(g.cor, i, e.L);
                var party = Party.Ranges(site, b, k0, i).Select(r => (r.s, r.e)).ToList();
                var R = Geo.MinusRanges(new List<(double, double)> { m.Span(0, T) }, party);
                Facade.StripPieces(op, e.F, m, R, y, y + RimHeight, 0, T, g.C.wall, g.C.wall, Skip.Bot);
            }
            return op;
        }
    }
}
