#nullable enable
using System.Collections.Generic;
using System.Threading;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A part of an artist's mesh (an opening's or a facade detail's) made ready once (docs/PERFORMANCE.md §8.1): the points
    /// its triangles use and their normals, its triangles into them, and wound the other way for glass seen from inside.
    /// A placement transforms it straight into the builder (<see cref="Put"/>), where each placement made two arrays of the
    /// mesh's points, and each part a map, two lists and their copies (walk-in glass two more): about 21 MB a LOD0 build
    /// with a 100-vertex window. Made when the kind is registered, or when first placed if its part changed since; a part
    /// is read by every build thread, and never changed once made. Storey's own.
    /// </summary>
    internal sealed class KindPart
    {
        readonly P3[] p, n; readonly int[] t; int[]? back;
        // what it was made from: a part given other arrays since is made again
        readonly P3[] fromP, fromN; readonly int[] fromT;

        KindPart(P3[] P, P3[] N, int[] tris)
        {
            fromP = P; fromN = N; fromT = tris;
            var map = new Dictionary<int, int>(); var pl = new List<P3>(); var nl = new List<P3>(); var tl = new List<int>(tris.Length);
            int Slot(int v)
            {
                if (map.TryGetValue(v, out int j)) return j;
                map[v] = j = pl.Count; pl.Add(P[v]); nl.Add(v < N.Length ? N[v] : new P3(0, 0, 1));
                return j;
            }
            // whole triangles, in order; one naming a point the mesh hasn't is left out
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if ((uint)a >= (uint)P.Length || (uint)b >= (uint)P.Length || (uint)c >= (uint)P.Length) continue;
                tl.Add(Slot(a)); tl.Add(Slot(b)); tl.Add(Slot(c));
            }
            p = pl.ToArray(); n = nl.ToArray(); t = tl.ToArray();
        }

        /// <summary>The part ready, from its kind's cache when it is there and made from these arrays, else made (and cached).</summary>
        public static KindPart Of(P3[] P, P3[] N, int[] tris, ref KindPart? cache)
        {
            var k = Volatile.Read(ref cache);
            if (k != null && ReferenceEquals(k.fromP, P) && ReferenceEquals(k.fromN, N) && ReferenceEquals(k.fromT, tris)) return k;
            k = new KindPart(P, N, tris);
            Volatile.Write(ref cache, k);
            return k;
        }

        /// <summary>Its triangles.</summary>
        public int Tris => t.Length / 3;

        // the triangles wound the other way: glass seen from inside (made once, on first use)
        int[] Back
        {
            get
            {
                var b = Volatile.Read(ref back);
                if (b != null) return b;
                b = new int[t.Length];
                for (int i = 0; i < b.Length; i += 3) { b[i] = t[i]; b[i + 1] = t[i + 2]; b[i + 2] = t[i + 1]; }
                Volatile.Write(ref back, b);
                return b;
            }
        }

        /// <summary>
        /// Into <paramref name="m"/> where <paramref name="at"/> puts it: front faces as made, or (<paramref name="inside"/>)
        /// wound the other way with the normals turned. Returns the first new vertex.
        /// </summary>
        public int Put(MeshBuilder m, in KindPlace at, Swatch c, bool inside = false)
        {
            int bse = m.Verts;
            if (m.ShapeOnly) return bse;
            for (int i = 0; i < p.Length; i++) m.V(at.Point(p[i]), inside ? at.Normal(n[i]) * -1 : at.Normal(n[i]), c);
            var tt = inside ? Back : t;
            for (int i = 0; i < tt.Length; i++) m.I.Add(bse + tt[i]);
            return bse;
        }
    }

    /// <summary>
    /// Where an artist's mesh goes on a wall: its x along the wall from <c>u</c> (along cross(up, outward), whichever way
    /// the wall's frame runs, so its front faces stay front), its y up from <c>y0</c>, its z out from the wall's outer face,
    /// stretched by <c>sx</c> across and <c>sy</c> up. A normal under a stretch goes by the inverse scale.
    /// </summary>
    internal readonly struct KindPlace
    {
        readonly Frame F; readonly double u, y0, sx, sy, flip;

        public KindPlace(Frame f, double u, double y0, double sx, double sy)
        {
            F = f; this.u = u; this.y0 = y0; this.sx = sx; this.sy = sy;
            flip = f.u.x * f.w.z - f.u.z * f.w.x < 0 ? -1 : 1;
        }

        public P3 Point(P3 p) => F.At(u + flip * p.x * sx, y0 + p.y * sy, Dim.T_EXT + p.z);

        public P3 Normal(P3 n)
        {
            double nx = n.x / sx, ny = n.y / sy, nz = n.z;
            return new P3(F.u.x * flip * nx + F.w.x * nz, ny, F.u.z * flip * nx + F.w.z * nz).Normalized;
        }
    }
}
