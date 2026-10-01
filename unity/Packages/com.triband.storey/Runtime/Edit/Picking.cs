#nullable enable
using System;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>What the editor's pointer is over: a building, or a point on a storey's floor. World-space rays.</summary>
    public static class Picking
    {
        /// <summary>
        /// The building a ray meets first, and how far along: each tier's walls (outer faces turned to the ray) and the
        /// flat top of each tier. -1 when it meets none.
        /// </summary>
        public static (int index, double distance) Building(StoreyDocument d, Vec3d origin, Vec3d dir)
        {
            int best = -1; double bd = double.PositiveInfinity;
            for (int i = 0; i < d.buildings.Count; i++)
            {
                var b = d.buildings[i];
                var h = Facades.Pick(b, origin, dir, bd);
                if (h != null && h.Value.distance < bd) { bd = h.Value.distance; best = i; }
                if (Math.Abs(dir.y) < 1e-9) continue;
                foreach (var t in Tiers.Of(b))
                {
                    double y = Derived.FloorBase(b, t.k1), tt = (y - origin.y) / dir.y;
                    if (tt < 0 || tt >= bd) continue;
                    double x = origin.x + dir.x * tt - b.pos.x, z = origin.z + dir.z * tt - b.pos.z;
                    if (Geo.Pip(Derived.OutlineAt(b, t.k0), x, z)) { bd = tt; best = i; }
                }
            }
            return (best, best < 0 ? double.PositiveInfinity : bd);
        }

        /// <summary>Where a ray meets the horizontal plane at height y, in building b's local coordinates; null when it never does.</summary>
        public static Vec2? OnPlane(BuildingData b, Vec3d origin, Vec3d dir, double y)
        {
            if (Math.Abs(dir.y) < 1e-9) return null;
            double tt = (y - origin.y) / dir.y;
            if (tt < 0) return null;
            return new Vec2(origin.x + dir.x * tt - b.pos.x, origin.z + dir.z * tt - b.pos.z);
        }

        /// <summary>
        /// The Interior tab's view of storey k (SPEC principle 4, the prototype's applyView): the ceiling clip just under
        /// the slab above, and the cutaway's base and top for the storey's walls.
        /// </summary>
        public static (double clipY, double cutBase, double cutTop) StoreyView(BuildingData b, int k)
        {
            int N = b.floors.Count;
            double fb = Derived.FloorBase(b, k);
            double clip = k < N ? fb + Derived.FloorH(b, k) - Dim.SLAB - 0.01 : 1e9;
            return (clip, fb, fb + (k < N ? Derived.FloorH(b, k) : 3.3) + 0.02);
        }
    }
}
