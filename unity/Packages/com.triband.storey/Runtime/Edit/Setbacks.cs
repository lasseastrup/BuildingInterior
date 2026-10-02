#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>Setbacks (a new outline from some floor up) and footprint presets. Ported from the prototype.</summary>
    public static class Setbacks
    {
        /// <summary>The footprint presets of the Shape tab, in metres.</summary>
        public static readonly IReadOnlyDictionary<string, double[][]> Presets = new Dictionary<string, double[][]>
        {
            ["rect"] = new[] { new[] { 0.0, 0 }, new[] { 12.0, 0 }, new[] { 12.0, 9 }, new[] { 0.0, 9 } },
            ["L"] = new[] { new[] { 0.0, 0 }, new[] { 16.0, 0 }, new[] { 16.0, 8 }, new[] { 8.0, 8 }, new[] { 8.0, 16 }, new[] { 0.0, 16 } },
            ["U"] = new[] { new[] { 0.0, 0 }, new[] { 18.0, 0 }, new[] { 18.0, 14 }, new[] { 12.0, 14 }, new[] { 12.0, 6 }, new[] { 6.0, 6 }, new[] { 6.0, 14 }, new[] { 0.0, 14 } },
            ["T"] = new[] { new[] { 0.0, 0 }, new[] { 18.0, 0 }, new[] { 18.0, 7 }, new[] { 12.0, 7 }, new[] { 12.0, 15 }, new[] { 6.0, 15 }, new[] { 6.0, 7 }, new[] { 0.0, 7 } },
            ["oct"] = new[] { new[] { 3.5, 0 }, new[] { 8.5, 0 }, new[] { 12.0, 3.5 }, new[] { 12.0, 8.5 }, new[] { 8.5, 12 }, new[] { 3.5, 12 }, new[] { 0.0, 8.5 }, new[] { 0.0, 3.5 } },
        };

        public static List<Vec2> Preset(string key) => Presets[key].Select(p => new Vec2(p[0], p[1])).ToList();

        /// <summary>Split tier k0 halfway up: the upper half gets its own copy of the outline. Returns the floor it starts at.</summary>
        public static int Add(BuildingData b, int k0)
        {
            var t = Tiers.Of(b).First(x => x.k0 == k0);
            int k = t.k0 + (t.k1 - t.k0 + 1) / 2;
            if (k >= b.floors.Count) throw new InvalidOperationException($"floor {k0} has no floor above it to start a setback");
            var f = b.floors[k];
            f.shape = Tiers.Copy(Tiers.Outline(b, t.k0)); f.blank = new List<int>(Tiers.Blank(b, t.k0));
            f.corners = Tiers.Corners(b, t.k0).Select(c => new CornerData { at = c.at, shape = c.shape, size = c.size, pts = Tiers.Copy(c.pts) }).ToList();   // no door: that is the street's
            return k;
        }

        /// <summary>Remove the setback starting at k0: its floors follow the outline below again; its doors and details go.</summary>
        public static void Remove(BuildingData b, int k0)
        {
            b.details.RemoveAll(d => Derived.TierStart(b, d.k) == k0);
            b.entrances.RemoveAll(e => Tiers.DoorOn(b, e, k0));
            var f = b.floors[k0]; f.shape = new List<Vec2>(); f.blank = new List<int>(); f.corners = new List<CornerData>(); f.terraceRoof = null;
        }

        /// <summary>
        /// Start the setback at k0 at floor k instead. Terrace doors move with it; a door or detail whose storey changes
        /// outline goes. False when a core or the outline no longer fits there (the tools then undo it).
        /// </summary>
        public static bool Move(BuildingData b, int k0, int k)
        {
            var tb = b.details.Select(d => Derived.TierStart(b, d.k)).ToList();
            var te = b.entrances.Select(e => e.k == k0 ? -1 : Derived.TierStart(b, e.k)).ToList();
            var f = b.floors[k0]; var g = b.floors[k];
            g.shape = f.shape; g.blank = f.blank; g.corners = f.corners; f.shape = new List<Vec2>(); f.blank = new List<int>(); f.corners = new List<CornerData>();
            if (f.terraceRoof != null) { g.terraceRoof = f.terraceRoof; f.terraceRoof = null; }
            foreach (var e in b.entrances) if (e.k == k0) e.k = k;
            b.details = b.details.Where((d, j) => Derived.TierStart(b, d.k) == tb[j]).ToList();
            b.entrances = b.entrances.Where((e, j) => te[j] < 0 || Derived.TierStart(b, e.k) == te[j]).ToList();
            return b.shafts.All(s => Tiers.ShaftFits(b, s)) && Outlines.Issue(b, k, g.shape) == OutlineIssue.None;
        }

        /// <summary>Replace the footprint with a preset: one entrance on the first edge; setbacks and cores that no longer fit go.</summary>
        public static void ApplyPreset(BuildingData b, string key)
        {
            b.details.RemoveAll(d => Derived.TierStart(b, d.k) == 0);
            b.footprint = Preset(key); b.corners = new List<CornerData>();
            var kept = b.entrances.Where(e => e.k != 0 && Derived.TierStart(b, e.k) != 0).ToList();
            b.entrances = new List<EntranceData> { new EntranceData { edge = 0, t = 0.5 } };
            b.entrances.AddRange(kept);
            b.blank = new List<int>();
            foreach (var t in Tiers.Of(b))
                if (t.k0 > 0 && Outlines.Issue(b, t.k0, Derived.OutlineAt(b, t.k0)) != OutlineIssue.None) Remove(b, t.k0);
            b.shafts = b.shafts.Where(s => Tiers.ShaftFits(b, s)).ToList();
            b.voids = b.voids.Where(v => Voids.Issue(b, v) == VoidIssue.None).ToList();
        }
    }
}
