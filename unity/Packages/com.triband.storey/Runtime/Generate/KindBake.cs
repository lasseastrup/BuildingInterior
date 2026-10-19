#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// An artist's window or door made ready for <see cref="OpeningKinds"/>, from a prefab of several objects or from one
    /// mesh (docs/OPENINGS.md §3): each object's mesh through its matrix into the prefab root's space, then into the
    /// opening's own (x across, centred on the hole; y up from the hole's bottom; z out of the wall, 0 on its outer face),
    /// its triangles grouped into parts by key (a material), welded, and the faces nobody can see left out: those buried in
    /// the wall around the hole, and those on its faces turned into it. Engine-free: the editor gathers the meshes, the
    /// matrices and the keys, this does the geometry. Storey's own.
    /// </summary>
    public static class KindBake
    {
        /// <summary>One object's mesh: its points and normals in its own space, the matrix to the prefab root's, and its submeshes.</summary>
        public sealed class Source
        {
            public P3[] P = Array.Empty<P3>(), N = Array.Empty<P3>();
            /// <summary>The object's space to the root's, three rows of four: x' = M[0]·x + M[1]·y + M[2]·z + M[3], and so on.</summary>
            public double[] M = { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
            public List<Sub> subs = new List<Sub>();
            /// <summary>Under one of a door's leaf objects: left out where the doorway stays open.</summary>
            public bool leaf;
        }

        /// <summary>A submesh: its triangles, and the part they go to (<see cref="keys"/>, one a triangle, when that differs by triangle).</summary>
        public sealed class Sub
        {
            public int[] tris = Array.Empty<int>();
            public string key = "";
            public string[]? keys;
        }

        /// <summary>
        /// The hole in the wall, in the root's space: its middle, its width and height, and where the wall's outer face is
        /// along Z. The opening's space starts at the hole's bottom middle, on that face.
        /// </summary>
        public struct Hole
        {
            public double cx, cy, w, h, wallZ;
            public Hole(double cx, double cy, double w, double h, double wallZ = 0) { this.cx = cx; this.cy = cy; this.w = w; this.h = h; this.wallZ = wallZ; }
        }

        /// <summary>A part made: its key, whether it is a leaf object's, and its triangles (into <see cref="Result.P"/>).</summary>
        public sealed class Part
        {
            public string key = "";
            public bool leaf;
            public readonly List<int> tris = new List<int>();
        }

        public sealed class Result
        {
            public readonly List<P3> P = new List<P3>(), N = new List<P3>();
            public readonly List<Part> parts = new List<Part>();
            /// <summary>Triangles left out: buried in the wall or on its faces turned into it, and those with no area.</summary>
            public int hidden, degenerate;
            /// <summary>Triangles lying on the wall's outer face, facing out, round the hole: they flicker against the wall.</summary>
            public int onWall;
            /// <summary>The deepest point (most negative z): anything behind the wall's back shows inside the rooms.</summary>
            public double deepest;
            /// <summary>The bounds of what was kept, in the opening's space.</summary>
            public P3 min, max;
            public int Tris { get { int n = 0; foreach (var p in parts) n += p.tris.Count / 3; return n; } }
        }

        /// <summary>How close to a plane a point must be to count as on it, metres.</summary>
        const double Eps = 1e-4;

        /// <summary>
        /// The sources into the opening's space, in parts. <paramref name="facesBack"/>: the prefab was made facing −Z, and is
        /// turned round (half a turn about +Y). Parts come in the order their keys are first met.
        /// </summary>
        public static Result Bake(IReadOnlyList<Source> sources, Hole hole, bool facesBack)
        {
            var r = new Result { min = new P3(double.MaxValue, double.MaxValue, double.MaxValue), max = new P3(double.MinValue, double.MinValue, double.MinValue) };
            var at = new Dictionary<(string key, bool leaf), Part>();
            var weld = new Dictionary<(Part part, P3 p, P3 n), int>(new WeldKey());
            double ox = hole.cx, oy = hole.cy - hole.h / 2, oz = hole.wallZ, turn = facesBack ? -1 : 1;
            var pts = new List<P3>(); var nrm = new List<P3>();
            foreach (var s in sources)
            {
                var M = s.M; var (inv, det) = NormalMatrix(M);
                if (Math.Abs(det) < 1e-12) { foreach (var sub in s.subs) r.degenerate += sub.tris.Length / 3; continue; }   // flattened to nothing
                pts.Clear(); nrm.Clear();
                bool flat = s.N.Length != s.P.Length;   // a mesh without normals: each face its own
                for (int i = 0; i < s.P.Length; i++)
                {
                    var p = s.P[i];
                    double x = M[0] * p.x + M[1] * p.y + M[2] * p.z + M[3], y = M[4] * p.x + M[5] * p.y + M[6] * p.z + M[7], z = M[8] * p.x + M[9] * p.y + M[10] * p.z + M[11];
                    pts.Add(new P3((x - ox) * turn, y - oy, (z - oz) * turn));
                    if (flat) continue;
                    var n = s.N[i];
                    var q = new P3(inv[0] * n.x + inv[1] * n.y + inv[2] * n.z, inv[3] * n.x + inv[4] * n.y + inv[5] * n.z, inv[6] * n.x + inv[7] * n.y + inv[8] * n.z).Normalized;
                    nrm.Add(new P3(q.x * turn, q.y, q.z * turn));
                }
                bool mirrored = det < 0;   // wound the other way, so its front faces stay front
                foreach (var sub in s.subs)
                {
                    var t = sub.tris;
                    for (int i = 0; i + 2 < t.Length; i += 3)
                    {
                        int a = t[i], b = mirrored ? t[i + 2] : t[i + 1], c = mirrored ? t[i + 1] : t[i + 2];
                        if ((uint)a >= (uint)pts.Count || (uint)b >= (uint)pts.Count || (uint)c >= (uint)pts.Count) { r.degenerate++; continue; }   // a point the mesh hasn't
                        P3 A = pts[a], B = pts[b], C = pts[c];
                        var cr = P3.Cross(B - A, C - A); double area = cr.Length;
                        if (area < 1e-10) { r.degenerate++; continue; }
                        var fn = cr * (1 / area);
                        switch (Where(A, B, C, fn, hole.w, hole.h))
                        {
                            case Place.Hidden: r.hidden++; continue;
                            case Place.OnWall: r.onWall++; break;
                        }
                        string key = sub.keys != null && i / 3 < sub.keys.Length ? sub.keys[i / 3] : sub.key;
                        if (!at.TryGetValue((key, s.leaf), out var part)) { part = new Part { key = key, leaf = s.leaf }; at[(key, s.leaf)] = part; r.parts.Add(part); }
                        part.tris.Add(Vertex(r, weld, part, A, flat ? fn : nrm[a])); part.tris.Add(Vertex(r, weld, part, B, flat ? fn : nrm[b])); part.tris.Add(Vertex(r, weld, part, C, flat ? fn : nrm[c]));
                    }
                }
            }
            r.deepest = r.P.Count > 0 ? r.min.z : 0;
            if (r.P.Count == 0) r.min = r.max = new P3(0, 0, 0);
            return r;
        }

        /// <summary>
        /// The bounds of the sources' points in the root's space (before any hole): what a hole fitted to the meshes takes.
        /// Null when there are none.
        /// </summary>
        public static (P3 min, P3 max)? Bounds(IReadOnlyList<Source> sources)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            foreach (var s in sources)
            {
                var M = s.M;
                // only the points a triangle uses: a mesh can carry points nothing draws
                var used = new bool[s.P.Length];
                foreach (var sub in s.subs) foreach (int i in sub.tris) if ((uint)i < (uint)used.Length) used[i] = true;
                for (int i = 0; i < s.P.Length; i++)
                {
                    if (!used[i]) continue;
                    var p = s.P[i];
                    double x = M[0] * p.x + M[1] * p.y + M[2] * p.z + M[3], y = M[4] * p.x + M[5] * p.y + M[6] * p.z + M[7], z = M[8] * p.x + M[9] * p.y + M[10] * p.z + M[11];
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
                }
            }
            return x0 > x1 ? ((P3, P3)?)null : (new P3(x0, y0, z0), new P3(x1, y1, z1));
        }

        /// <summary>The hole a source's bounds make: as wide and high as they are, the wall's outer face on the root's Z 0.</summary>
        public static Hole HoleOf((P3 min, P3 max) b) => new Hole((b.min.x + b.max.x) / 2, (b.min.y + b.max.y) / 2, Math.Max(0.05, b.max.x - b.min.x), Math.Max(0.05, b.max.y - b.min.y), 0);

        enum Place { Kept, Hidden, OnWall }

        /// <summary>
        /// Where a triangle (in the opening's space) is against the wall round a hole <paramref name="w"/> wide and
        /// <paramref name="h"/> high: buried in the wall (or on one of its faces, turned into it), lying on its outer face
        /// turned out, or anywhere else. The wall fills z from −<see cref="Dim.T_EXT"/> to 0 all round the hole.
        /// </summary>
        static Place Where(P3 a, P3 b, P3 c, P3 n, double w, double h)
        {
            double T = Dim.T_EXT, hw = w / 2;
            double xmin = Math.Min(a.x, Math.Min(b.x, c.x)), xmax = Math.Max(a.x, Math.Max(b.x, c.x));
            double ymin = Math.Min(a.y, Math.Min(b.y, c.y)), ymax = Math.Max(a.y, Math.Max(b.y, c.y));
            double zmin = Math.Min(a.z, Math.Min(b.z, c.z)), zmax = Math.Max(a.z, Math.Max(b.z, c.z));
            // round the hole: wholly to one side of it (beside, under or over), within the wall's depth
            bool round = xmax <= -hw + Eps || xmin >= hw - Eps || ymax <= Eps || ymin >= h - Eps;
            if (!round) return Place.Kept;
            bool onFace = zmax <= Eps && zmin >= -Eps;
            if (onFace && n.z > 0.5) return Place.OnWall;
            if (zmin < -T - Eps || zmax > Eps) return Place.Kept;
            // its middle strictly inside the wall: buried, whichever way it faces
            double cx = (a.x + b.x + c.x) / 3, cy = (a.y + b.y + c.y) / 3, cz = (a.z + b.z + c.z) / 3;
            bool inside = cz < -Eps && cz > -T + Eps && (cx < -hw - Eps || cx > hw + Eps || cy < -Eps || cy > h + Eps);
            if (inside) return Place.Hidden;
            // on one of the wall's faces (its middle is, so all of it is): hidden when turned into the wall
            if (onFace) return n.z < -0.5 ? Place.Hidden : Place.Kept;
            if (zmax <= -T + Eps) return n.z > 0.5 ? Place.Hidden : Place.Kept;   // the wall's back
            if (xmax <= -hw + Eps && xmin >= -hw - Eps) return n.x < -0.5 ? Place.Hidden : Place.Kept;   // the hole's sides
            if (xmin >= hw - Eps && xmax <= hw + Eps) return n.x > 0.5 ? Place.Hidden : Place.Kept;
            if (ymax <= Eps && ymin >= -Eps) return n.y < -0.5 ? Place.Hidden : Place.Kept;   // its bottom and top
            if (ymin >= h - Eps && ymax <= h + Eps) return n.y > 0.5 ? Place.Hidden : Place.Kept;
            return Place.Kept;
        }

        /// <summary>A vertex of a part: an equal one already in it (position and normal), or a new one.</summary>
        static int Vertex(Result r, Dictionary<(Part, P3, P3), int> weld, Part part, P3 p, P3 n)
        {
            if (weld.TryGetValue((part, p, n), out int i)) return i;
            i = r.P.Count; r.P.Add(p); r.N.Add(n); weld[(part, p, n)] = i;
            r.min = new P3(Math.Min(r.min.x, p.x), Math.Min(r.min.y, p.y), Math.Min(r.min.z, p.z));
            r.max = new P3(Math.Max(r.max.x, p.x), Math.Max(r.max.y, p.y), Math.Max(r.max.z, p.z));
            return i;
        }

        /// <summary>Exact equality of the doubles (0 and −0 alike, as == has them), and the part by reference.</summary>
        sealed class WeldKey : IEqualityComparer<(Part part, P3 p, P3 n)>
        {
            public bool Equals((Part part, P3 p, P3 n) a, (Part part, P3 p, P3 n) b) =>
                ReferenceEquals(a.part, b.part) && a.p.x == b.p.x && a.p.y == b.p.y && a.p.z == b.p.z && a.n.x == b.n.x && a.n.y == b.n.y && a.n.z == b.n.z;
            public int GetHashCode((Part part, P3 p, P3 n) k) =>
                HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(k.part), k.p.x + 0.0, k.p.y + 0.0, k.p.z + 0.0, k.n.x + 0.0, k.n.y + 0.0, k.n.z + 0.0);
        }

        /// <summary>
        /// A matrix's normal transform, the inverse transpose of its 3 × 3 part (row-major, nine numbers), and its
        /// determinant: negative for a mirrored object.
        /// </summary>
        static (double[] inv, double det) NormalMatrix(double[] M)
        {
            double a = M[0], b = M[1], c = M[2], d = M[4], e = M[5], f = M[6], g = M[8], h = M[9], i = M[10];
            double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
            double D = -(b * i - c * h), E = a * i - c * g, F = -(a * h - b * g);
            double G = b * f - c * e, H = -(a * f - c * d), I = a * e - b * d;
            double det = a * A + b * B + c * C;
            if (Math.Abs(det) < 1e-12) return (new double[9], det);
            double k = 1 / det;
            // the inverse is the cofactors' transpose over the determinant; its transpose, the cofactors themselves
            return (new[] { A * k, B * k, C * k, D * k, E * k, F * k, G * k, H * k, I * k }, det);
        }
    }
}
