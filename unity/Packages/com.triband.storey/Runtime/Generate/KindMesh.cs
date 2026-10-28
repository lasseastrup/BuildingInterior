#nullable enable
using System;
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
    /// <remarks>
    /// Made ready, it keeps nothing a placement would only waste (Storey's own): a point the mesh has twice (the same place
    /// and normal: split along a UV seam, or between materials Storey draws in one colour) is one point; a triangle too
    /// thin to show is left out (<see cref="Thin"/>); points that land in one place with one normal are one vertex
    /// (<see cref="Twins"/>); at its own size, the bake's cuts along the slice lines are joined again (<see cref="Join"/>);
    /// and pushed back into a whole wall by a recess, what the wall then buries is left out (<see cref="Buried"/>). The
    /// converted scenes' LOD0s had 13 % of their vertices twice, all of them their artists' windows'.
    /// </remarks>
    internal sealed class KindPart
    {
        /// <summary>
        /// What an opening's part is placed in: its kind's hole, <see cref="w"/> × <see cref="h"/>, going down to the floor
        /// where it is <see cref="open"/> (a door's); and for a kind placed <see cref="sliced"/> (not stretched whole,
        /// <see cref="OpeningKinds.At"/>), the slice lines, as <see cref="KindPlace.Sliced"/> has them. None (0 × 0): a facade
        /// detail's, on a whole wall.
        /// </summary>
        internal readonly struct Hole : IEquatable<Hole>
        {
            public readonly double w, h, xL, xR, yB, yT;
            public readonly bool open, sliced;

            public Hole(OpeningKind k)
            {
                w = k.w; h = k.h; open = k.door; sliced = !OpeningKinds.Stretched(k);
                xL = -w / 2 + k.borderL; xR = w / 2 - k.borderR; yB = k.borderB; yT = h - k.borderT;
            }

            /// <summary>
            /// Where a placement at the kind's own size puts a point, in the kind's space: one the bake set aside from a slice
            /// line goes back onto it (<see cref="KindPlace"/>), the rest stay.
            /// </summary>
            public P3 OwnSize(P3 q)
            {
                double x = Math.Abs(q.x - xL) <= KindPlace.OnLine ? xL : Math.Abs(q.x - xR) <= KindPlace.OnLine ? xL + (xR - xL) : q.x;
                double y = Math.Abs(q.y - yB) <= KindPlace.OnLine ? yB : Math.Abs(q.y - yT) <= KindPlace.OnLine ? yB + (yT - yB) : q.y;
                return new P3(x, y, q.z);
            }

            /// <summary>Whether a point (as <see cref="OwnSize"/> puts it) is on a slice line.</summary>
            public bool OnLine(P3 q) => q.x == xL || q.x == xL + (xR - xL) || q.y == yB || q.y == yB + (yT - yB);

            public bool Equals(Hole o) => w == o.w && h == o.h && xL == o.xL && xR == o.xR && yB == o.yB && yT == o.yT && open == o.open && sliced == o.sliced;
            public override bool Equals(object? o) => o is Hole k && Equals(k);
            public override int GetHashCode() => HashCode.Combine(w, h, xL, xR, yB, yT, open, sliced);
        }

        readonly P3[] p, n; readonly int[] t; int[]? back;
        // what it was made from: a part given other arrays (or another hole) since is made again
        readonly P3[] fromP, fromN; readonly int[] fromT;
        readonly Hole hole;
        // the points that may be one vertex where placed (Twins): point j's are twins[twinAt[j]] up to twins[twinAt[j + 1]],
        // each before it; null where none may
        readonly int[]? twinAt, twins;
        // the part as placed at its own size, the bake's cuts joined (Join); null where that is no less
        readonly KindPart? own;
        // the part pushed back into the wall, what that buries left out (Buried): one for each depth it is placed at
        (double back, double deep, KindPart part)[] buried = Array.Empty<(double, double, KindPart)>();

        /// <summary>
        /// A triangle whose least height (twice its area over its longest side) is under this, metres, is no surface: float
        /// noise along a seam of the artist's file (the converted scenes' were all under 1.5 µm, the next 27 µm), no wider
        /// than the gap it leaves. Left out when the part is made ready.
        /// </summary>
        internal const double Thin = 2e-6;

        // places: how many of the kind's parts have a point at each place, as placed at its own size (Join)
        KindPart(P3[] P, P3[] N, int[] tris, Hole hole, Dictionary<(double, double, double), int>? places)
        {
            fromP = P; fromN = N; fromT = tris; this.hole = hole;
            // a point by its index, then by its place and normal (exactly: the builder can't tell two such apart)
            var map = new Dictionary<int, int>(); var same = new Dictionary<(P3 p, P3 n), int>(Exact.Instance);
            var pl = new List<P3>(); var nl = new List<P3>(); var tl = new List<int>(tris.Length);
            int Slot(int v)
            {
                if (map.TryGetValue(v, out int j)) return j;
                var q = P[v]; var nr = v < N.Length ? N[v] : new P3(0, 0, 1);
                if (!same.TryGetValue((q, nr), out j)) { same[(q, nr)] = j = pl.Count; pl.Add(q); nl.Add(nr); }
                return map[v] = j;
            }
            // whole triangles, in order; one naming a point the mesh hasn't is left out, and one too thin to show
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                if ((uint)a >= (uint)P.Length || (uint)b >= (uint)P.Length || (uint)c >= (uint)P.Length || IsThin(P[a], P[b], P[c])) continue;
                tl.Add(Slot(a)); tl.Add(Slot(b)); tl.Add(Slot(c));
            }
            p = pl.ToArray(); n = nl.ToArray(); t = tl.ToArray();
            (twinAt, twins) = Twins(p, n);
            own = hole.sliced && hole.w > 0 && places != null ? Join(places) : null;
        }

        // one of part's: these points and triangles
        KindPart(KindPart part, P3[] p, P3[] n, int[] t)
        {
            fromP = part.fromP; fromN = part.fromN; fromT = part.fromT; hole = part.hole;
            this.p = p; this.n = n; this.t = t;
            (twinAt, twins) = Twins(p, n);
        }

        /// <summary>One of this part's: the triangles kept (<paramref name="keep"/>, into points <paramref name="pts"/> and <paramref name="nrm"/>), with only the points they use, in the order they first use them.</summary>
        KindPart Keep(List<int> keep, IReadOnlyList<P3> pts, IReadOnlyList<P3> nrm)
        {
            var map = new int[pts.Count]; var pl = new List<P3>(); var nl = new List<P3>(); var tl = new int[keep.Count];
            for (int i = 0; i < keep.Count; i++)
            {
                int v = keep[i];
                if (map[v] == 0) { pl.Add(pts[v]); nl.Add(nrm[v]); map[v] = pl.Count; }
                tl[i] = map[v] - 1;
            }
            return new KindPart(this, pl.ToArray(), nl.ToArray(), tl);
        }

        /// <summary>
        /// The points that may be one vertex where placed: as near as slicing moves a point (the bake sets a point on a slice
        /// line a hundredth of a millimetre to its side, <see cref="KindBake.Aside"/>, and a placement puts it back:
        /// <see cref="KindPlace"/>), their normals as near as the packed bytes tell apart. A pair is one vertex where it is
        /// placed in the same place with the same packed normal (<see cref="Put"/>): the converted scenes' windows, baked with
        /// their holes' edges as slice lines, had 7 % of their vertices so.
        /// </summary>
        static (int[]? at, int[]? list) Twins(P3[] p, P3[] n)
        {
            const double Near = KindPlace.OnLine * 2, Turn = 2.0 / 127;
            var order = new int[p.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) => p[a].x.CompareTo(p[b].x));
            List<(int i, int j)>? pairs = null;
            for (int a = 0; a < order.Length; a++)
                for (int b = a + 1; b < order.Length && p[order[b]].x - p[order[a]].x <= Near; b++)
                {
                    int i = Math.Min(order[a], order[b]), j = Math.Max(order[a], order[b]);
                    if (Math.Abs(p[i].y - p[j].y) > Near || Math.Abs(p[i].z - p[j].z) > Near) continue;
                    var d = n[i] - n[j]; if (P3.Dot(d, d) >= Turn * Turn) continue;
                    (pairs ??= new List<(int, int)>()).Add((i, j));
                }
            if (pairs == null) return (null, null);
            pairs.Sort((x, y) => x.j != y.j ? x.j.CompareTo(y.j) : x.i.CompareTo(y.i));
            var at = new int[p.Length + 1]; var list = new int[pairs.Count];
            for (int k = 0; k < pairs.Count; k++) { list[k] = pairs[k].i; at[pairs[k].j + 1]++; }
            for (int j = 0; j < p.Length; j++) at[j + 1] += at[j];
            return (at, list);
        }

        /// <summary>
        /// This part as placed at its own size (<see cref="KindPlace.OwnSize"/>), the cuts the bake made along the slice lines
        /// (<see cref="KindBake.Cuts"/>: for where an opening has to give) joined again; null where that is no less. Each point
        /// where that placement puts it, one vertex for the points it puts in one place with one normal (to a millionth);
        /// then each vertex on a slice line whose triangles all lie in one plane, with its normal at every corner, goes to a
        /// neighbour (a half-edge collapse) where their triangles then cover the same area, none turned over or thinner than
        /// the thinnest they replace. One at the part's edge goes only where none of the kind's other parts has a point there
        /// (<paramref name="places"/>), so no edge of theirs is left meeting its side. The same surface, shaded the same: the
        /// converted scenes' windows had a third more triangles cut. Storey's own.
        /// </summary>
        KindPart? Join(Dictionary<(double, double, double), int> places)
        {
            // each point where an own-size placement puts it, one vertex for those in one place with one normal
            var at = new Dictionary<(double, double, double), List<int>>(); var pts = new List<P3>(); var nrm = new List<P3>(); var id = new int[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                var q = hole.OwnSize(p[i]); var key = (q.x + 0.0, q.y + 0.0, q.z + 0.0); int found = -1;
                if (!at.TryGetValue(key, out var here)) at[key] = here = new List<int>(1);
                foreach (int j in here) if (Near(nrm[j], n[i])) { found = j; break; }
                if (found < 0) { found = pts.Count; pts.Add(q); nrm.Add(n[i]); here.Add(found); }
                id[i] = found;
            }
            var tri = new List<int>(t.Length);
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = id[t[i]], b = id[t[i + 1]], c = id[t[i + 2]];
                if (a == b || b == c || a == c || P3.Cross(pts[b] - pts[a], pts[c] - pts[a]).Length <= 0) continue;
                tri.Add(a); tri.Add(b); tri.Add(c);
            }
            // the collapses, a vertex at a time, until none is left to make (each takes a vertex away: at most so many)
            var fan = new List<int>[pts.Count]; for (int v = 0; v < fan.Length; v++) fan[v] = new List<int>();
            for (int i = 0; i < tri.Count; i += 3) { fan[tri[i]].Add(i); fan[tri[i + 1]].Add(i); fan[tri[i + 2]].Add(i); }
            for (bool more = true; more;)
            {
                more = false;
                for (int v = 0; v < pts.Count; v++)
                    if (fan[v].Count > 0 && hole.OnLine(pts[v]) && Collapse(v, tri, fan, pts, nrm, places)) more = true;
            }
            var keep = new List<int>(tri.Count); foreach (int x in tri) if (x >= 0) keep.Add(x);
            if (keep.Count == t.Length && pts.Count == p.Length) return null;
            return Keep(keep, pts, nrm);
        }

        // normals the same to a millionth: no shade tells them apart
        static bool Near(P3 a, P3 b) => Math.Abs(a.x - b.x) <= 1e-6 && Math.Abs(a.y - b.y) <= 1e-6 && Math.Abs(a.z - b.z) <= 1e-6;

        /// <summary>
        /// Vertex <paramref name="v"/>'s triangles (each vertex's in <paramref name="fans"/>, into <paramref name="tri"/>: three
        /// a triangle, −1 where one went) given to a neighbour, where that changes nothing seen (<see cref="Join"/>). Whether it
        /// was.
        /// </summary>
        static bool Collapse(int v, List<int> tri, List<int>[] fans, List<P3> pts, List<P3> nrm, Dictionary<(double, double, double), int> places)
        {
            var fan = fans[v];
            // one plane, the vertex's normal at every corner; and the thinnest triangle, which none made may be thinner than
            P3 fn = default; double area = 0, thinnest = double.MaxValue;
            foreach (int i in fan)
            {
                var cr = P3.Cross(pts[tri[i + 1]] - pts[tri[i]], pts[tri[i + 2]] - pts[tri[i]]); double l = cr.Length;
                if (l <= 0) return false;
                var f = cr * (1 / l);
                if (area == 0) fn = f; else if (P3.Dot(f, fn) < 1 - 1e-9) return false;
                area += l; thinnest = Math.Min(thinnest, Height(pts[tri[i]], pts[tri[i + 1]], pts[tri[i + 2]]));
                for (int c = 0; c < 3; c++) if (!Near(nrm[tri[i + c]], nrm[v])) return false;
            }
            // its neighbours round it, each triangle's two in its winding: one ring, or one run where it is at the part's edge
            int starts = 0, ring = 0;
            foreach (int i in fan)
            {
                int k = tri[i] == v ? 0 : tri[i + 1] == v ? 1 : 2, a = tri[i + (k + 1) % 3], b = tri[i + (k + 2) % 3];
                foreach (int j in fan)
                {
                    if (j == i) continue;
                    int kj = tri[j] == v ? 0 : tri[j + 1] == v ? 1 : 2, aj = tri[j + (kj + 1) % 3], bj = tri[j + (kj + 2) % 3];
                    if (aj == a || bj == b) return false;   // two triangles turning the same way from one neighbour: not one surface
                    if (aj == b) starts++;
                }
                ring++;
            }
            bool edge = starts < ring;   // a run, not a ring: the part's edge
            if (edge && (ring - starts > 1 || places.TryGetValue((pts[v].x + 0.0, pts[v].y + 0.0, pts[v].z + 0.0), out int parts) && parts > 1)) return false;
            // to the first neighbour its triangles go to and still cover the same area, none turned over or thinner
            for (int pick = 0; pick < fan.Count * 2; pick++)
            {
                int i0 = fan[pick / 2], k0 = tri[i0] == v ? 0 : tri[i0 + 1] == v ? 1 : 2, u = tri[i0 + (k0 + 1 + pick % 2) % 3];
                double after = 0; bool ok = true;
                foreach (int i in fan)
                {
                    int a = tri[i], b = tri[i + 1], c = tri[i + 2];
                    if (a == u || b == u || c == u) continue;
                    if (a == v) a = u; else if (b == v) b = u; else c = u;
                    var cr = P3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
                    if (P3.Dot(cr, fn) <= 0 || Height(pts[a], pts[b], pts[c]) < thinnest) { ok = false; break; }   // turned over, or thinner
                    after += cr.Length;
                }
                if (!ok || Math.Abs(after - area) > 1e-12 + 1e-7 * area) continue;
                foreach (int i in fan)
                {
                    if (tri[i] == u || tri[i + 1] == u || tri[i + 2] == u)
                    {
                        for (int c = 0; c < 3; c++) if (tri[i + c] != v) fans[tri[i + c]].Remove(i);
                        tri[i] = tri[i + 1] = tri[i + 2] = -1;
                        continue;
                    }
                    for (int c = 0; c < 3; c++) if (tri[i + c] == v) tri[i + c] = u;
                    fans[u].Add(i);
                }
                fan.Clear();
                return true;
            }
            return false;
        }

        /// <summary>Whether triangle abc is too thin to show (<see cref="Thin"/>).</summary>
        internal static bool IsThin(P3 a, P3 b, P3 c) => Height(a, b, c) <= Thin;

        /// <summary>Triangle abc's least height: twice its area over its longest side (0 for three points in one).</summary>
        static double Height(P3 a, P3 b, P3 c)
        {
            P3 e0 = b - a, e1 = c - a, e2 = c - b;
            double longest = Math.Sqrt(Math.Max(P3.Dot(e0, e0), Math.Max(P3.Dot(e1, e1), P3.Dot(e2, e2))));
            return longest > 0 ? P3.Cross(e0, e1).Length / longest : 0;
        }

        /// <summary>
        /// An opening's part ready, from its cache when it is there and made from these, else made (and cached).
        /// <paramref name="places"/>: its mesh's <see cref="Places"/>, where the caller has them.
        /// </summary>
        public static KindPart Of(OpeningKind k, OpeningKind.Mesh m, OpeningKind.Part part, Dictionary<(double, double, double), int>? places = null)
        {
            var hole = new Hole(k);
            var c = Volatile.Read(ref part.ready);
            if (c != null && c.From(m.P, m.N, part.tris, hole)) return c;
            c = new KindPart(m.P, m.N, part.tris, hole, hole.sliced ? places ?? Places(m, hole) : null);
            Volatile.Write(ref part.ready, c);
            return c;
        }

        /// <summary>
        /// How many of a mesh's parts have a point at each place, as its kind's own size puts it (<see cref="Hole.OwnSize"/>):
        /// <see cref="Join"/> leaves a part's edge alone where another has a point.
        /// </summary>
        internal static Dictionary<(double, double, double), int> Places(OpeningKind.Mesh m, Hole hole)
        {
            var o = new Dictionary<(double, double, double), int>(); var one = new HashSet<(double, double, double)>();
            foreach (var q in m.parts)
            {
                one.Clear();
                foreach (int i in q.tris)
                {
                    if ((uint)i >= (uint)m.P.Length) continue;
                    var w = hole.OwnSize(m.P[i]); var key = (w.x + 0.0, w.y + 0.0, w.z + 0.0);
                    if (one.Add(key)) o[key] = o.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            return o;
        }

        /// <summary>A facade detail's part ready, as an opening's (<see cref="Of(OpeningKind, OpeningKind.Mesh, OpeningKind.Part)"/>).</summary>
        public static KindPart Of(DetailKind.Mesh m, DetailKind.Part part)
        {
            var c = Volatile.Read(ref part.ready);
            if (c != null && c.From(m.P, m.N, part.tris, default)) return c;
            c = new KindPart(m.P, m.N, part.tris, default, null);
            Volatile.Write(ref part.ready, c);
            return c;
        }

        bool From(P3[] P, P3[] N, int[] tris, Hole h) => ReferenceEquals(fromP, P) && ReferenceEquals(fromN, N) && ReferenceEquals(fromT, tris) && hole.Equals(h);

        /// <summary>Its triangles.</summary>
        public int Tris => t.Length / 3;

        /// <summary>Its points: each a vertex where it is placed.</summary>
        public int Points => p.Length;

        /// <summary>This part as placed at its own size (<see cref="Join"/>): itself where that is no less.</summary>
        public KindPart AtOwnSize => own ?? this;

        /// <summary>How many depths a part keeps what a recess buries for (<see cref="Buried"/>); past them it is placed whole.</summary>
        const int Depths = 8;

        /// <summary>
        /// This part pushed <paramref name="back"/> metres into the wall by a recess, with what is then buried in it left out:
        /// the triangles round the hole within <paramref name="deep"/> metres of the wall's face, where the wall is known to be
        /// whole (<see cref="Facade.Whole"/>), as the bake leaves out what the wall buries before any recess
        /// (<see cref="KindBake.Where"/>). Itself where nothing is (and for a facade detail's). Made once for each depth (a
        /// style's recess, a storey's reveal: a few), on the thread that first places it so.
        /// </summary>
        public KindPart Buried(double back, double deep)
        {
            if (hole.w <= 0 || back <= 0 || deep <= 0) return this;
            var made = Volatile.Read(ref buried);
            foreach (var x in made) if (x.back == back && x.deep == deep) return x.part;
            lock (this)
            {
                foreach (var x in buried) if (x.back == back && x.deep == deep) return x.part;
                if (buried.Length >= Depths) return this;
                var keep = new List<int>(t.Length); var z = new P3(0, 0, back);
                for (int i = 0; i < t.Length; i += 3)
                {
                    P3 a = p[t[i]], b = p[t[i + 1]], c = p[t[i + 2]];
                    var cr = P3.Cross(b - a, c - a); double l = cr.Length;
                    if (l > 0 && KindBake.Where(a - z, b - z, c - z, cr * (1 / l), hole.w, hole.h, false, deep, hole.open) == KindBake.Place.Hidden) continue;
                    keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
                }
                var part = keep.Count == t.Length ? this : Keep(keep, p, n);
                var more = new (double, double, KindPart)[buried.Length + 1];
                Array.Copy(buried, more, buried.Length); more[buried.Length] = (back, deep, part);
                Volatile.Write(ref buried, more);
                return part;
            }
        }

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
        /// wound the other way with the normals turned. At its own size its cuts are joined (<see cref="Join"/>); pushed
        /// <paramref name="back"/> metres into a wall known to be whole <paramref name="deep"/> metres in round the hole, what
        /// that buries is left out (<see cref="Buried"/>). Returns the first new vertex.
        /// </summary>
        public int Put(MeshBuilder m, in KindPlace at, Swatch c, bool inside = false, double back = 0, double deep = 0)
        {
            int bse = m.Verts;
            if (m.ShapeOnly) return bse;
            var k = at.OwnSize ? AtOwnSize : this;
            if (deep > 0) k = k.Buried(back, deep);
            var p = k.p; var n = k.n; var t = k.t; var tt = inside ? k.Back : t;
            if (k.twinAt == null)
            {
                for (int i = 0; i < p.Length; i++) m.V(at.Point(p[i]), inside ? at.Normal(p[i], n[i]) * -1 : at.Normal(p[i], n[i]), c);
                for (int i = 0; i < tt.Length; i++) m.I.Add(bse + tt[i]);
                return bse;
            }
            // a point placed where a twin of it went, its normal packing as that one's, is that vertex (Twins); a triangle two
            // of whose corners so became one is left out. Each point's vertex in the builder's own scratch
            var at2 = k.twinAt; var tw = k.twins!; var v = m.KindScratch;
            if (v == null || v.Length < p.Length) m.KindScratch = v = new int[Math.Max(p.Length, 2 * (v?.Length ?? 0))];
            int made = 0;
            for (int i = 0; i < p.Length; i++)
            {
                P3 q = at.Point(p[i]), nr = inside ? at.Normal(p[i], n[i]) * -1 : at.Normal(p[i], n[i]);
                int one = -1;
                for (int x = at2[i]; x < at2[i + 1] && one < 0; x++)
                {
                    int was = v[tw[x]]; P3 wq = m.P[bse + was], wn = m.N[bse + was];
                    if ((float)wq.x == (float)q.x && (float)wq.y == (float)q.y && (float)wq.z == (float)q.z && MeshPack.S8(wn.x) == MeshPack.S8(nr.x) && MeshPack.S8(wn.y) == MeshPack.S8(nr.y) && MeshPack.S8(wn.z) == MeshPack.S8(nr.z)) one = was;
                }
                if (one >= 0) { v[i] = one; continue; }
                v[i] = made++; m.V(q, nr, c);
            }
            for (int i = 0; i < tt.Length; i += 3)
            {
                int a = v[tt[i]], b = v[tt[i + 1]], d = v[tt[i + 2]];
                if (a == b || b == d || a == d) continue;
                m.I.Add(bse + a); m.I.Add(bse + b); m.I.Add(bse + d);
            }
            return bse;
        }

        /// <summary>Exact equality of a place and a normal (0 and −0 alike, as == has them).</summary>
        sealed class Exact : IEqualityComparer<(P3 p, P3 n)>
        {
            public static readonly Exact Instance = new Exact();
            public bool Equals((P3 p, P3 n) a, (P3 p, P3 n) b) =>
                a.p.x == b.p.x && a.p.y == b.p.y && a.p.z == b.p.z && a.n.x == b.n.x && a.n.y == b.n.y && a.n.z == b.n.z;
            public int GetHashCode((P3 p, P3 n) k) => HashCode.Combine(k.p.x + 0.0, k.p.y + 0.0, k.p.z + 0.0, k.n.x + 0.0, k.n.y + 0.0, k.n.z + 0.0);
        }
    }

    /// <summary>
    /// Where an artist's mesh goes on a wall: its x along the wall from <c>u</c> (along cross(up, outward), whichever way
    /// the wall's frame runs, so its front faces stay front), its y up from <c>y0</c>, its z out from the wall's outer face.
    /// Stretched by <c>sx</c> across and <c>sy</c> up; or sliced (<see cref="Sliced"/>, docs/OPENINGS.md §4.3), as 9-slice
    /// scaling does: what lies between the slice lines is stretched to where they go in the opening, and what lies outside
    /// them moves with the nearest, unscaled. A normal goes by the inverse scale where its point is stretched. A part that
    /// keeps its shape (<see cref="Rigid"/>) moves with its anchor, unscaled.
    /// </summary>
    internal readonly struct KindPlace
    {
        readonly Frame F; readonly double u, y0, sx, sy, flip;
        // sliced: across between xL and xR (to XL..XR, scale mx), up between yB and yT (to YB..YT, scale my); else stretched
        readonly bool cutX, cutY;
        readonly double xL, xR, XL, mx, yB, yT, YB, my;
        // a part that keeps its shape: its anchor in its own space
        readonly bool rigid; readonly double ax, ay;

        public KindPlace(Frame f, double u, double y0, double sx, double sy) : this(f, u, y0, sx, sy, false, 0, 0, 0, 1, false, 0, 0, 0, 1, false, 0, 0) { }

        KindPlace(Frame f, double u, double y0, double sx, double sy, bool cutX, double xL, double xR, double XL, double mx, bool cutY, double yB, double yT, double YB, double my, bool rigid, double ax, double ay)
        {
            F = f; this.u = u; this.y0 = y0; this.sx = sx; this.sy = sy;
            flip = f.u.x * f.w.z - f.u.z * f.w.x < 0 ? -1 : 1;
            this.cutX = cutX; this.xL = xL; this.xR = xR; this.XL = XL; this.mx = mx;
            this.cutY = cutY; this.yB = yB; this.yT = yT; this.YB = YB; this.my = my;
            this.rigid = rigid; this.ax = ax; this.ay = ay;
        }

        /// <summary>
        /// A hole <paramref name="w"/> × <paramref name="h"/> sliced into an opening <paramref name="W"/> × <paramref name="H"/>
        /// at <paramref name="u"/> along the wall (its middle) and <paramref name="y0"/> up (its bottom): the slice lines
        /// <paramref name="bl"/>, <paramref name="br"/>, <paramref name="bb"/> and <paramref name="bt"/> in from the hole's
        /// sides, bottom and top. Where an opening (or the hole) has no room between them, that way is stretched instead.
        /// </summary>
        public static KindPlace Sliced(Frame f, double u, double y0, double w, double h, double W, double H, double bl, double br, double bb, double bt)
        {
            double xL = -w / 2 + bl, xR = w / 2 - br, XL = -W / 2 + bl, XR = W / 2 - br, yB = bb, yT = h - bt, YB = bb, YT = H - bt;
            bool cx = xR - xL > 1e-3 && XR - XL > 1e-3, cy = yT - yB > 1e-3 && YT - YB > 1e-3;
            return new KindPlace(f, u, y0, W / Math.Max(1e-3, w), H / Math.Max(1e-3, h),
                cx, xL, xR, XL, cx ? (XR - XL) / (xR - xL) : 1, cy, yB, yT, YB, cy ? (YT - YB) / (yT - yB) : 1, false, 0, 0);
        }

        /// <summary>The same placement for a part that keeps its shape: it goes where its anchor goes, unscaled.</summary>
        public KindPlace Rigid(P3 anchor) => new KindPlace(F, u, y0, sx, sy, cutX, xL, xR, XL, mx, cutY, yB, yT, YB, my, true, anchor.x, anchor.y);

        /// <summary>
        /// How near a slice line a point the bake set aside from it (<see cref="KindBake.Aside"/>) is: it goes back onto the
        /// line, so the pieces either side meet; its side still decides its normal.
        /// </summary>
        internal const double OnLine = KindBake.Aside * 2;

        /// <summary>
        /// Placed sliced at the kind's own size: every point moved alike (a point the bake set aside put back on its line), the
        /// slices as they were cut (<see cref="KindPart.Join"/>). Storey's own.
        /// </summary>
        public bool OwnSize => !rigid && cutX && cutY && Math.Abs(mx - 1) <= 1e-9 && Math.Abs(my - 1) <= 1e-9;

        // a point's place across and up, in the opening's own space (one set aside from a line goes back onto it: OnLine)
        double X(double x) => !cutX ? x * sx
            : Math.Abs(x - xL) <= OnLine ? XL : Math.Abs(x - xR) <= OnLine ? XL + (xR - xL) * mx
            : x < xL ? x + (XL - xL) : x > xR ? x + (XL + (xR - xL) * mx - xR) : XL + (x - xL) * mx;
        double Y(double y) => !cutY ? y * sy
            : Math.Abs(y - yB) <= OnLine ? YB : Math.Abs(y - yT) <= OnLine ? YB + (yT - yB) * my
            : y < yB ? y : y > yT ? y + (YB + (yT - yB) * my - yT) : YB + (y - yB) * my;

        public P3 Point(P3 p) => rigid
            ? F.At(u + flip * (X(ax) + p.x - ax), y0 + Y(ay) + p.y - ay, Dim.T_EXT + p.z)
            : F.At(u + flip * X(p.x), y0 + Y(p.y), Dim.T_EXT + p.z);

        /// <summary>The normal <paramref name="n"/> at point <paramref name="p"/> (its own space): by the inverse of the scale where the point is.</summary>
        public P3 Normal(P3 p, P3 n)
        {
            double kx = rigid ? 1 : !cutX ? sx : p.x < xL || p.x > xR ? 1 : mx;
            double ky = rigid ? 1 : !cutY ? sy : p.y < yB || p.y > yT ? 1 : my;
            double nx = n.x / kx, ny = n.y / ky, nz = n.z;
            return new P3(F.u.x * flip * nx + F.w.x * nz, ny, F.u.z * flip * nx + F.w.z * nz).Normalized;
        }
    }
}
