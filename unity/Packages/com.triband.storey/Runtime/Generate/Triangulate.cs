#nullable enable
using System;
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
        public static List<int> Shape(List<Vec2> contour, List<List<Vec2>>? holes) => Shape(contour, holes, new List<int>());

        /// <summary>
        /// <see cref="Shape(List{Vec2}, List{List{Vec2}})"/> into <paramref name="tris"/>, emptied first: for a caller that
        /// reuses its list. The coordinates go to Earcut through this thread's own arrays.
        /// </summary>
        public static List<int> Shape(List<Vec2> contour, List<List<Vec2>>? holes, List<int> tris)
        {
            tris.Clear();
            var d = data ??= new double[256]; var hi = holeIdx ??= new int[16];
            int n = 0, nh = 0;
            Put(contour, ref d, ref n);
            if (holes != null)
                foreach (var h in holes)
                {
                    if (nh == hi.Length) Array.Resize(ref hi, hi.Length * 2);
                    hi[nh++] = n / 2;
                    Put(h, ref d, ref n);
                }
            data = d; holeIdx = hi;
            return Earcut.Tessellate(d, n, nh > 0 ? hi : null, nh, tris);
        }

        [ThreadStatic] static double[]? data;
        [ThreadStatic] static int[]? holeIdx;

        // a ring's points as Earcut takes them, but for a closing duplicate of the first (Dedupe)
        static void Put(List<Vec2> ring, ref double[] d, ref int n)
        {
            int l = ring.Count;
            if (l > 2 && ring[l - 1].x == ring[0].x && ring[l - 1].z == ring[0].z) l--;
            if (d.Length < n + 2 * l) Array.Resize(ref d, Math.Max(n + 2 * l, d.Length * 2));
            for (int i = 0; i < l; i++) { d[n++] = ring[i].x; d[n++] = ring[i].z; }
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
