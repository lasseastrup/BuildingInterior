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
    }
}
