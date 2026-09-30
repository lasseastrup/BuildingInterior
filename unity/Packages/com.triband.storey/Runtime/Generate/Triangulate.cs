#nullable enable
using System.Collections.Generic;
using Triband.Storey.ThirdParty;

namespace Triband.Storey.Generate
{
    /// <summary>Polygon triangulation as the prototype uses it (THREE.ShapeUtils.triangulateShape over earcut).</summary>
    public static class Triangulate
    {
        /// <summary>
        /// Triangles (index triples into contour followed by the holes' points, in order) of a
        /// contour with holes. Duplicate closing points are dropped first, as three.js does.
        /// </summary>
        public static List<int> Shape(List<Vec2> contour, List<List<Vec2>>? holes)
        {
            var rings = new List<List<Vec2>> { Dedupe(contour) };
            if (holes != null) foreach (var h in holes) rings.Add(Dedupe(h));
            var data = new List<double>();
            var holeIdx = new List<int>();
            for (int r = 0; r < rings.Count; r++)
            {
                if (r > 0) holeIdx.Add(data.Count / 2);
                foreach (var p in rings[r]) { data.Add(p.x); data.Add(p.z); }
            }
            return Earcut.Tessellate(data.ToArray(), holeIdx.Count > 0 ? holeIdx.ToArray() : null);
        }

        /// <summary>The ring with its closing duplicate removed (three.js <c>removeDupEndPts</c>).</summary>
        public static List<Vec2> Dedupe(List<Vec2> ring)
        {
            var l = ring.Count;
            if (l > 2 && ring[l - 1].x == ring[0].x && ring[l - 1].z == ring[0].z) return ring.GetRange(0, l - 1);
            return ring;
        }

        /// <summary>Triangulate a planar 3D polygon (with holes) by projecting it onto its dominant plane.</summary>
        public static List<int> Planar(List<P3> pts, List<List<P3>>? holes, P3 n)
        {
            double ax = System.Math.Abs(n.x), ay = System.Math.Abs(n.y), az = System.Math.Abs(n.z);
            int axis = ax >= ay && ax >= az ? 0 : ay >= az ? 1 : 2;
            List<Vec2> V(List<P3> r)
            {
                var o = new List<Vec2>(r.Count);
                foreach (var p in r) o.Add(axis == 0 ? new Vec2(p.y, p.z) : axis == 1 ? new Vec2(p.x, p.z) : new Vec2(p.x, p.y));
                return o;
            }
            var hs = new List<List<Vec2>>();
            if (holes != null) foreach (var h in holes) hs.Add(V(h));
            return Shape(V(pts), hs);
        }
    }
}
