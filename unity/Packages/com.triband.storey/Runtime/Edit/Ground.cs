#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// Standing a building on uneven ground (its <see cref="BuildingData.elevation"/>): where to sample the ground under
    /// it, and what to make of the heights found. The editor casts down at <see cref="Samples"/> onto the scene's
    /// colliders (a terrain has one) and hands the heights to <see cref="Snap"/>. The building stands on the highest, so
    /// it never sinks into the slope, and its foundation reaches down to the lowest, so it never floats.
    /// </summary>
    public static class Ground
    {
        /// <summary>How far in from the outline the corners are sampled: a ray down the wall's line may miss a steep bank.</summary>
        public const double Inset = 0.3;

        /// <summary>Points under the base outline (site space, x and z): each corner a little in, each edge's middle, the centre.</summary>
        public static List<Vec2> Samples(BuildingData b)
        {
            var fp = b.footprint; var out_ = new List<Vec2>();
            if (fp.Count == 0) return out_;
            double cx = fp.Average(p => p.x), cz = fp.Average(p => p.z);
            Vec2 In(double x, double z)
            {
                double dx = cx - x, dz = cz - z, l = Math.Sqrt(dx * dx + dz * dz), t = l > Inset ? Inset / l : 0;
                return new Vec2(b.pos.x + x + dx * t, b.pos.z + z + dz * t);
            }
            for (int i = 0; i < fp.Count; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % fp.Count];
                out_.Add(In(a.x, a.z)); out_.Add(In((a.x + c.x) / 2, (a.z + c.z) / 2));
            }
            out_.Add(new Vec2(b.pos.x + cx, b.pos.z + cz));
            return out_;
        }

        /// <summary>
        /// Stand the building on the ground found at its <see cref="Samples"/> (site heights): its ground floor on the
        /// highest, and, where the ground falls away by more than 5 cm, its foundation (a style's, Facade ▸ Foundation)
        /// down past the lowest. Returns the drop from the highest to the lowest; null when nothing was found.
        /// </summary>
        public static double? Snap(BuildingData b, IReadOnlyList<double> heights)
        {
            if (heights.Count == 0) return null;
            double hi = heights.Max(), lo = heights.Min(), drop = hi - lo;
            b.elevation = Tiers.Cm(hi);
            if (drop > 0.05)
            {
                double need = Tiers.Cm(drop + 0.1);   // 10 cm into the ground at the lowest point
                if (!(b.style.foundation >= need)) { b.style = b.style.Clone(); b.style.foundation = need; }
            }
            return drop;
        }

        /// <summary>How near a height dragged to comes to the site's ground or a neighbour's ground floor before it stands level with it, in metres.</summary>
        public const double LevelSnap = 0.15;

        /// <summary>
        /// Where a building dragged up or down to <paramref name="y"/> stands (the Shape tool's move gizmo, docs/EDITOR.md
        /// §6.11), its outline's origin at <paramref name="x"/>, <paramref name="z"/>: level with the site's ground (0), or with
        /// the ground floor of a building within a metre of it, where it comes within <see cref="LevelSnap"/> of one (the
        /// nearest), so floors line up and a wall between them is shared all the way up; otherwise to the centimetre.
        /// <paramref name="with"/> is the neighbour it stands level with; null for the ground, or none.
        /// </summary>
        public static double Level(BuildingData b, double x, double z, double y, IEnumerable<BuildingData> neighbours, out BuildingData? with)
        {
            with = null;
            double to = Tiers.Cm(y), best = LevelSnap;
            if (Math.Abs(y) < best) { best = Math.Abs(y); to = 0; }
            if (b.footprint.Count == 0) return to;
            var (x0, z0, x1, z1) = Tiers.Bbox(b.footprint);
            x0 += x - 1; x1 += x + 1; z0 += z - 1; z1 += z + 1;
            foreach (var o in neighbours)
            {
                if (ReferenceEquals(o, b) || o.id == b.id || o.footprint.Count == 0) continue;
                var (ox0, oz0, ox1, oz1) = Tiers.Bbox(o.footprint);
                if (ox1 + o.pos.x < x0 || ox0 + o.pos.x > x1 || oz1 + o.pos.z < z0 || oz0 + o.pos.z > z1) continue;
                double d = Math.Abs(y - o.elevation);
                if (d < best) { best = d; to = o.elevation; with = o; }
            }
            return to;
        }
    }
}
