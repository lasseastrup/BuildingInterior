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
        /// reuses its list. The coordinates go to Earcut through arrays lent for the call (<see cref="TriScratch"/>).
        /// </summary>
        public static List<int> Shape(List<Vec2> contour, List<List<Vec2>>? holes, List<int> tris)
        {
            var s = lent.Take();
            try { return s.Shape(contour, holes, tris); }
            finally { lent.Give(s); }
        }

        static readonly Lender<TriScratch> lent = new Lender<TriScratch>(8);

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

    /// <summary>
    /// What a triangulation works in: the coordinates as Earcut takes them, and an <see cref="Earcut"/> whose nodes are
    /// taken again. One thread at a time: <see cref="Triangulate"/> lends these out, and a wall panel's builder keeps its own.
    /// </summary>
    public sealed class TriScratch
    {
        double[] data = new double[256];
        int[] holeIdx = new int[16];
        readonly Earcut earcut = new Earcut();

        /// <summary><see cref="Triangulate.Shape(List{Vec2}, List{List{Vec2}})"/> into <paramref name="tris"/>, emptied first.</summary>
        public List<int> Shape(List<Vec2> contour, List<List<Vec2>>? holes, List<int> tris)
        {
            tris.Clear();
            int n = 0, nh = 0;
            Put(contour, ref n);
            if (holes != null)
                foreach (var h in holes)
                {
                    if (nh == holeIdx.Length) Array.Resize(ref holeIdx, holeIdx.Length * 2);
                    holeIdx[nh++] = n / 2;
                    Put(h, ref n);
                }
            return earcut.Tessellate(data, n, nh > 0 ? holeIdx : null, nh, tris);
        }

        // a ring's points as Earcut takes them, but for a closing duplicate of the first (Triangulate.Dedupe)
        void Put(List<Vec2> ring, ref int n)
        {
            int l = ring.Count;
            if (l > 2 && ring[l - 1].x == ring[0].x && ring[l - 1].z == ring[0].z) l--;
            if (data.Length < n + 2 * l) Array.Resize(ref data, Math.Max(n + 2 * l, data.Length * 2));
            for (int i = 0; i < l; i++) { data[n++] = ring[i].x; data[n++] = ring[i].z; }
        }
    }
}
