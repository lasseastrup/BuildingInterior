#nullable enable
using System.Collections.Generic;
using Triband.Storey.Edit;
using Triband.Storey.Generate;

namespace Triband.Storey.Validate
{
    /// <summary>
    /// An artist's window or door tried on a building (docs/OPENINGS.md §3.5): a 20 × 12 m brick block of six storeys with a
    /// street door, built at LOD0 and LOD1 with the kind and with the generator's own, its LOD0 shell and walk-in and its LOD1
    /// (where a LOD1 in front of the wall lies on the flat wall) checked for faces that overlap (<see cref="CoplanarCheck"/>).
    /// For the inspector's Check on a building; the kind must be registered. Storey's own.
    /// </summary>
    public static class KindTrial
    {
        public sealed class Result
        {
            /// <summary>The block's triangles with the kind (a shell building), and with the generator's own windows and doors.</summary>
            public int lod0, lod1, plainLod0, plainLod1;
            /// <summary>How many of a facade detail the block has (<see cref="Run(DetailKind)"/>).</summary>
            public int placed;
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
                if (walkIn) return (l0.Op.Tris + l0.Glass.Tris, 0);
                var l1 = Lod1.Build(site, b);
                if (check) r.overlaps.AddRange(CoplanarCheck.Run(l1).Overlaps);
                return (l0.Op.Tris + l0.Glass.Tris, l1.Tris);
            }
            (r.lod0, r.lod1) = Build(true, false, true);
            Build(true, true, true);
            (r.plainLod0, r.plainLod1) = Build(false, false, false);
            return r;
        }

        /// <summary>
        /// An artist's facade detail tried on the same block (docs/EDITOR.md §6.12): by its rule at 100 % with the defaults,
        /// or, without one, put by hand on each storey of the long wall (over its door, for one over an opening). Built at
        /// LOD0 and LOD1 with it and without, and checked for faces that overlap. <see cref="Result.placed"/> says how many
        /// it put. The kind must be registered. Storey's own.
        /// </summary>
        public static Result Run(DetailKind k)
        {
            var r = new Result();
            (int lod0, int lod1) Build(bool withKind, bool check)
            {
                var d = new StoreyDocument();
                var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
                b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 12), new Vec2(0, 12) };
                Floors.SetCount(b, 6);
                b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
                b.style = Styles.Preset("brick"); b.style.ground = GroundType.Match; b.style.details = new DetailRules { ac = 0, vents = 0 };
                if (withKind)
                {
                    if (Details.RuleFits(k)) b.style.details.kinds = new SortedDictionary<string, double>(System.StringComparer.Ordinal) { [k.id] = 100 };
                    else if (k.anchor == Details.Anchor.Opening) b.details.Add(new DetailData { kind = k.id, k = 0, edge = 0, t = 0.5 });
                    else if (k.anchor == Details.Anchor.Tier) b.details.Add(new DetailData { kind = k.id, k = 1, edge = 2, t = 0.5 });
                    else for (int f = 0; f < 6; f++) b.details.Add(new DetailData { kind = k.id, k = f, edge = 2, t = 0.13, y = 1.0 });
                }
                var site = new Site(d.buildings);
                if (withKind) for (int f = 0; f < 6; f++) foreach (var it in Details.At(site, b, f)) if (it.kind == k) r.placed++;
                var l0 = Lod0.Build(site, b);
                if (check) r.overlaps.AddRange(CoplanarCheck.Run(l0.Op).Overlaps);
                var l1 = Lod1.Build(site, b);
                if (check) r.overlaps.AddRange(CoplanarCheck.Run(l1).Overlaps);
                return (l0.Op.Tris + l0.Glass.Tris, l1.Tris);
            }
            (r.lod0, r.lod1) = Build(true, true);
            (r.plainLod0, r.plainLod1) = Build(false, false);
            return r;
        }
    }
}
