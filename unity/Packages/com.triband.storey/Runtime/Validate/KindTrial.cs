#nullable enable
using System.Collections.Generic;
using Triband.Storey.Edit;
using Triband.Storey.Generate;

namespace Triband.Storey.Validate
{
    /// <summary>
    /// An artist's window or door tried on a building (docs/OPENINGS.md §3.5): a 20 × 12 m brick block of six storeys with a
    /// street door, built at LOD0 and LOD1 with the kind and with the generator's own, its LOD0 shell and walk-in checked
    /// for faces that overlap (<see cref="CoplanarCheck"/>). For the inspector's Check on a building; the kind must be
    /// registered. Storey's own.
    /// </summary>
    public static class KindTrial
    {
        public sealed class Result
        {
            /// <summary>The block's triangles with the kind (a shell building), and with the generator's own windows and doors.</summary>
            public int lod0, lod1, plainLod0, plainLod1;
            public readonly List<CoplanarCheck.Overlap> overlaps = new List<CoplanarCheck.Overlap>();
        }

        public static Result Run(OpeningKind k)
        {
            var r = new Result();
            (int lod0, int lod1) Build(bool withKind, bool walkIn, bool check)
            {
                var d = new StoreyDocument();
                var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
                b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 12), new Vec2(0, 12) };
                Floors.SetCount(b, 6);
                b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
                b.interior = walkIn; b.style = Styles.Preset("brick"); b.style.ground = GroundType.Match;
                if (withKind) { if (k.door) b.style.doorKind = k.id; else b.style.windowKind = k.id; }
                var site = new Site(d.buildings);
                var l0 = Lod0.Build(site, b);
                if (check) r.overlaps.AddRange(CoplanarCheck.Run(l0.Op).Overlaps);
                return (l0.Op.Tris + l0.Glass.Tris, walkIn ? 0 : Lod1.Build(site, b).Tris);
            }
            (r.lod0, r.lod1) = Build(true, false, true);
            Build(true, true, true);
            (r.plainLod0, r.plainLod1) = Build(false, false, false);
            return r;
        }
    }
}
