#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// Bridges between buildings (<see cref="BridgeData"/>, <see cref="Bridges"/>): added from a point on a wall to the
    /// building facing it across, removed, opened or enclosed, widened. Storey's own.
    /// </summary>
    public static class BridgeEdits
    {
        public static string NewId(BuildingData a)
        {
            int n = 1; while (a.bridges.Any(x => x.id == "br" + n)) n++;
            return "br" + n;
        }

        /// <summary>
        /// The bridge a click at a wall point would make: to the nearest building whose wall faces this one straight across,
        /// clear of every other building, to its floor nearest this one's. The reason when there is none.
        /// </summary>
        public static (BridgeSpan? span, string? why) Plan(Site site, BuildingData a, int k, Vec2 at, double width = 2.4)
        {
            BridgeSpan? best = null; string? why = null;
            foreach (var other in site.Buildings)
            {
                if (ReferenceEquals(other, a)) continue;
                var (s, w) = Bridges.Plan(a, k, at, other, -1, width);
                if (s != null) { if (best == null || s.L < best.L) best = s; }
                else if (why == null || (w != null && !w.Contains("no wall facing"))) why = w;
            }
            if (best == null) return (null, why != null && !why.Contains("no wall facing") ? why : "No building faces this wall straight across, within 40 m");
            // nothing else stands in the way, up to the bridge's roof
            foreach (var o in site.Buildings)
            {
                if (ReferenceEquals(o, a) || ReferenceEquals(o, best.B)) continue;
                for (int j = 1; j < 20; j++)
                {
                    double t = best.L * j / 20;
                    foreach (double side in new[] { -best.W / 2, 0, best.W / 2 })
                    {
                        var p = best.F.At2(t, side);
                        if (Derived.RoofY(o) > Math.Min(best.ya, best.yb) - Bridges.Deck && Over(o, p)) return (null, o.name + " is in the way");
                    }
                }
            }
            return (best, null);
        }

        static bool Over(BuildingData o, Vec2 world)
        {
            double lx = world.x - o.pos.x, lz = world.z - o.pos.z;
            for (int k = 0; k <= o.floors.Count; k++) if (Geo.Pip(Derived.OutlineAt(o, k), lx, lz)) return true;
            return false;
        }

        /// <summary>Add a bridge from building a's wall at <paramref name="at"/> (local) on storey k. Returns it, or why not.</summary>
        public static (BridgeData? br, string? why) Add(Site site, BuildingData a, int k, Vec2 at, bool open = false, double width = 2.4)
        {
            var (s, why) = Plan(site, a, k, at, width);
            if (s == null) return (null, why);
            if (Bridges.Touching(site, a).Any(o => (o.br.k == k && ReferenceEquals(o.A, a) && Geo.Hypot(o.br.at.x - at.x, o.br.at.z - at.z) < o.W + width)
                                                || (ReferenceEquals(o.B, a) && o.br.toK == k && o.edgeB == s.edgeA && Math.Abs(o.tB - s.tA) * Geo.EdgeLen(Derived.OutlineAt(a, k), s.edgeA) < (o.W + width) / 2)))
                return (null, "Another bridge is already here");
            var br = new BridgeData { id = NewId(a), to = s.B.id, k = k, at = new Vec2(Tiers.Cm(at.x), Tiers.Cm(at.z)), toK = NearestStorey(s.B, s.ya), width = width, open = open };
            a.bridges.Add(br);
            return (br, null);
        }

        static int NearestStorey(BuildingData b, double y)
        {
            int best = 1; double bd = 1e9;
            for (int j = 1; j < b.floors.Count; j++) { double d = Math.Abs(Derived.FloorBase(b, j) - y); if (d < bd) { bd = d; best = j; } }
            return best;
        }

        /// <summary>The bridge (from or to building b) whose door is at this wall point on storey k, within 1.5 m.</summary>
        public static (BuildingData owner, BridgeData br)? At(Site site, BuildingData b, int k, Vec2 at)
        {
            foreach (var s in Bridges.Touching(site, b))
            {
                if (ReferenceEquals(s.A, b) && s.br.k == k && Geo.Hypot(s.PA.x - b.pos.x - at.x, s.PA.z - b.pos.z - at.z) < Dim.T_EXT + 1.5) return (s.A, s.br);
                if (ReferenceEquals(s.B, b) && s.br.toK == k && Geo.Hypot(s.PB.x - b.pos.x - at.x, s.PB.z - b.pos.z - at.z) < Dim.T_EXT + 1.5) return (s.A, s.br);
            }
            return null;
        }

        public static bool Remove(BuildingData owner, string id) => owner.bridges.RemoveAll(x => x.id == id) > 0;

        /// <summary>Bridges to a building that is going: removed from every other building.</summary>
        public static void Forget(StoreyDocument d, string id) { foreach (var b in d.buildings) b.bridges.RemoveAll(x => x.to == id); }
    }
}
