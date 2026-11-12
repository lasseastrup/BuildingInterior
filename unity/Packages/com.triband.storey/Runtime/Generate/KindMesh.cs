#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A part of an artist's mesh (an opening's or a facade detail's) made ready once (docs/PERFORMANCE.md §8.1): the points
    /// its triangles use and their normals, its triangles into them, and wound the other way for glass seen from inside, and
    /// for an opening's back where a storey's rooms see it (<see cref="Backs"/>).
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
        // the opening's mesh it is a part of, which may hide its back from a storey's rooms (Backs); null for a facade detail's
        readonly OpeningKind.Mesh? mesh;

        /// <summary>
        /// A triangle whose least height (twice its area over its longest side) is under this, metres, is no surface: float
        /// noise along a seam of the artist's file (the converted scenes' were all under 1.5 µm, the next 27 µm), no wider
        /// than the gap it leaves. Left out when the part is made ready.
        /// </summary>
        internal const double Thin = 2e-6;

        // places: how many of the kind's parts have a point at each place, as placed at its own size (Join)
        KindPart(P3[] P, P3[] N, int[] tris, Hole hole, Dictionary<(double, double, double), int>? places, OpeningKind.Mesh? mesh = null)
        {
            fromP = P; fromN = N; fromT = tris; this.hole = hole; this.mesh = mesh;
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
            fromP = part.fromP; fromN = part.fromN; fromT = part.fromT; hole = part.hole; mesh = part.mesh;
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
            if (c != null && c.From(m.P, m.N, part.tris, hole) && ReferenceEquals(c.mesh, m)) return c;
            c = new KindPart(m.P, m.N, part.tris, hole, hole.sliced ? places ?? Places(m, hole) : null, m);
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

        // what of it the rooms see from behind (Backs): made once, on first use
        KindPart? backs;

        /// <summary>
        /// How far behind a face its back lies (<see cref="Backs"/>), metres: off it, as a cut building draws both sides of
        /// every face and two back to back would flicker (docs/PERFORMANCE.md §7.1), yet within the thinnest member an artist
        /// models, so the back stays inside it.
        /// </summary>
        internal const double BackOff = 5e-4;

        /// <summary>
        /// What of this part a storey's rooms see from behind (Storey's own), to be placed wound the other way
        /// (<see cref="PutBacks"/>): the back a window or door modelled for the street alone hasn't (no faces turned to the
        /// rooms, as the converted windows were made). Without it the rooms saw its frame's faces from behind, which a cut
        /// building draws in the section cap's colour and a steady one culls, so the frame showed as holes. Its triangles over
        /// the hole (any of them inside it: the rooms see nothing of what lies round it, behind the wall, but a face on one of
        /// the hole's sides in front of the wall turned away from the hole, a member's side standing proud, whose back they see
        /// through the member), but those turned to the rooms, which they see from the front, and those whose back the rest
        /// of the window hides from them (<see cref="KindSight"/>: one made whole, its members closed, gets none); each point
        /// <see cref="BackOff"/> behind the faces it is on, along their winding (a normal turned the wrong way in the artist's
        /// file doesn't bring a back out in front of its face). Empty for a facade detail's (no hole).
        /// </summary>
        internal KindPart Backs
        {
            get
            {
                var b = Volatile.Read(ref backs);
                if (b != null) return b;
                b = MakeBacks();
                Volatile.Write(ref backs, b);
                return b;
            }
        }

        KindPart MakeBacks()
        {
            const double E = 1e-4;   // KindBake's: how near a line a point must be to be on it
            var keep = new List<int>(t.Length);
            var way = new P3[p.Length];   // each point's way back: its kept triangles' facing, by their area
            if (hole.w > 0 && hole.h > 0)
            {
                double hw = hole.w / 2, h = hole.h;
                var sight = mesh != null ? KindSight.Of(mesh, hole.w, hole.h) : null;
                for (int i = 0; i < t.Length; i += 3)
                {
                    P3 a = p[t[i]], b = p[t[i + 1]], c = p[t[i + 2]];
                    var cr = P3.Cross(b - a, c - a); double l = cr.Length;
                    if (l <= 0 || cr.z < -0.5 * l) continue;   // turned to the rooms: they see its front
                    double x0 = Math.Min(a.x, Math.Min(b.x, c.x)), x1 = Math.Max(a.x, Math.Max(b.x, c.x)), y0 = Math.Min(a.y, Math.Min(b.y, c.y)), y1 = Math.Max(a.y, Math.Max(b.y, c.y));
                    bool across = x1 > -hw + E && x0 < hw - E, up = y1 > E && y0 < h - E;   // some of it within the hole's width, its height
                    // round the hole the rooms see nothing, the wall between, but for a face on one of the hole's sides in front
                    // of the wall turned away from the hole (a member's side standing proud of the wall): its back, through the hole
                    if (!(across && up) && !(Math.Min(a.z, Math.Min(b.z, c.z)) >= -E
                        && (up && (x1 <= -hw + E && x0 >= -hw - E && cr.x < -0.5 * l || x0 >= hw - E && x1 <= hw + E && cr.x > 0.5 * l)
                            || across && (y1 <= E && y0 >= -E && cr.y < -0.5 * l || y0 >= h - E && y1 <= h + E && cr.y > 0.5 * l)))) continue;
                    if (sight != null && !sight.Seen(a, b, c, cr * (1 / l))) continue;   // the rest of it hides its back: made whole
                    keep.Add(t[i]); keep.Add(t[i + 1]); keep.Add(t[i + 2]);
                    way[t[i]] += cr; way[t[i + 1]] += cr; way[t[i + 2]] += cr;
                }
            }
            var behind = new P3[p.Length];
            for (int v = 0; v < p.Length; v++) { double l = way[v].Length; behind[v] = l > 1e-12 ? p[v] - way[v] * (BackOff / l) : p[v]; }
            return Keep(keep, behind, n);
        }

        /// <summary>
        /// Its back where the rooms see it (<see cref="Backs"/>) into <paramref name="m"/>, placed as <see cref="Put"/> places the
        /// part (its cuts joined at its own size, what a recess buries left out), wound the other way with the normals turned.
        /// Returns the first new vertex.
        /// </summary>
        public int PutBacks(MeshBuilder m, in KindPlace at, Swatch c, double back = 0, double deep = 0)
        {
            if (m.ShapeOnly) return m.Verts;
            var k = at.OwnSize ? AtOwnSize : this;
            if (deep > 0) k = k.Buried(back, deep);
            return k.Backs.Put(m, at, c, true);
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
    /// What a storey's rooms see of an opening's mesh from behind (Storey's own), for <see cref="KindPart.Backs"/>. Rays go
    /// from just behind a triangle into the building, straight in and aslant; one that gets behind all of the mesh within the
    /// hole (the wall's reveal round it), past none of its triangles but glass (see-through there) and a door's leaf (left out
    /// there), reaches the rooms, which then see that triangle's back. So a window made for the street alone, open behind, is
    /// seen from behind where it is open; one made whole, its members closed, is not, and gets no backs (they would lie inside
    /// it). In the kind's own space, its hole <see cref="w"/> × <see cref="h"/>. Made once a mesh, on first use; read by every
    /// build thread, and never changed once made.
    /// </summary>
    internal sealed class KindSight
    {
        readonly double w, h;
        // the mesh's triangles that hide what is behind them, nine numbers each; and how deep a ray is behind all of them
        readonly double[] v; readonly double zEnd;
        // binned by x and y: cell (i, j)'s triangles are items[start[i * ny + j]] .. items[start[i * ny + j + 1] - 1]
        readonly double x0, y0, cs; readonly int nx, ny; readonly int[] start, items;

        // how steeply a ray goes into the building, against its way straight back from the face; and its leanings aslant
        static readonly double[] Steep = { 0.5, 2, 8 };
        static readonly P3[] Lean = { new P3(0.5, 0, 0), new P3(-0.5, 0, 0), new P3(0, 0.5, 0), new P3(0, -0.5, 0) };

        /// <summary>Mesh <paramref name="m"/>'s, for its kind's hole <paramref name="w"/> × <paramref name="h"/>: from its cache when it is there, else made (and cached).</summary>
        internal static KindSight Of(OpeningKind.Mesh m, double w, double h)
        {
            var s = Volatile.Read(ref m.sight);
            if (s != null && s.w == w && s.h == h) return s;
            s = new KindSight(m, w, h);
            Volatile.Write(ref m.sight, s);
            return s;
        }

        KindSight(OpeningKind.Mesh m, double w, double h)
        {
            this.w = w; this.h = h;
            var tl = new List<double>(); double zMin = 0, X0 = double.MaxValue, Y0 = double.MaxValue, X1 = double.MinValue, Y1 = double.MinValue;
            var P = m.P;
            foreach (var part in m.parts)
            {
                if (part.slot == ColorSlot.Glass || part.leaf) continue;   // see-through, or left out, where the rooms are
                var t = part.tris;
                for (int i = 0; i + 2 < t.Length; i += 3)
                {
                    if ((uint)t[i] >= (uint)P.Length || (uint)t[i + 1] >= (uint)P.Length || (uint)t[i + 2] >= (uint)P.Length) continue;
                    P3 a = P[t[i]], b = P[t[i + 1]], c = P[t[i + 2]];
                    if (P3.Cross(b - a, c - a).Length < 1e-12) continue;
                    tl.Add(a.x); tl.Add(a.y); tl.Add(a.z); tl.Add(b.x); tl.Add(b.y); tl.Add(b.z); tl.Add(c.x); tl.Add(c.y); tl.Add(c.z);
                    zMin = Math.Min(zMin, Math.Min(a.z, Math.Min(b.z, c.z)));
                    X0 = Math.Min(X0, Math.Min(a.x, Math.Min(b.x, c.x))); X1 = Math.Max(X1, Math.Max(a.x, Math.Max(b.x, c.x)));
                    Y0 = Math.Min(Y0, Math.Min(a.y, Math.Min(b.y, c.y))); Y1 = Math.Max(Y1, Math.Max(a.y, Math.Max(b.y, c.y)));
                }
            }
            v = tl.ToArray(); zEnd = zMin - 1e-3;
            int n = v.Length / 9;
            if (n == 0) { x0 = y0 = 0; cs = 1; nx = ny = 1; start = new int[2]; items = Array.Empty<int>(); return; }
            cs = Math.Max(0.01, Math.Max(X1 - X0, Y1 - Y0) / 24);
            x0 = X0 - 1e-6; y0 = Y0 - 1e-6;
            nx = Math.Max(1, (int)Math.Ceiling((X1 + 1e-6 - x0) / cs)); ny = Math.Max(1, (int)Math.Ceiling((Y1 + 1e-6 - y0) / cs));
            // each triangle in every cell its span over x and y meets: counted, then listed
            var at = new int[nx * ny + 1]; int[]? list = null;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int o = 0; o < v.Length; o += 9)
                {
                    int i0 = Cell(Math.Min(v[o], Math.Min(v[o + 3], v[o + 6])) - x0, nx), i1 = Cell(Math.Max(v[o], Math.Max(v[o + 3], v[o + 6])) - x0, nx);
                    int j0 = Cell(Math.Min(v[o + 1], Math.Min(v[o + 4], v[o + 7])) - y0, ny), j1 = Cell(Math.Max(v[o + 1], Math.Max(v[o + 4], v[o + 7])) - y0, ny);
                    for (int i = i0; i <= i1; i++)
                        for (int j = j0; j <= j1; j++)
                            if (list == null) at[i * ny + j + 1]++; else list[at[i * ny + j]++] = o;
                }
                if (list != null) break;
                for (int c = 0; c < nx * ny; c++) at[c + 1] += at[c];
                list = new int[at[nx * ny]]; start = (int[])at.Clone();
            }
            start ??= new int[2]; items = list ?? Array.Empty<int>();
        }

        int Cell(double d, int count) => Math.Max(0, Math.Min(count - 1, (int)Math.Floor(d / cs)));

        /// <summary>
        /// Whether a storey's rooms see the back of triangle abc, turned to <paramref name="g"/> (unit): from any of a lattice
        /// of points over it, about <see cref="Spacing"/> apart (at most 16 along an edge), each a millimetre in from its edges.
        /// One triangle can be closed behind in its middle and open at a corner, where another member's inside meets it.
        /// </summary>
        public bool Seen(P3 a, P3 b, P3 c, P3 g)
        {
            var mid = (a + b + c) * (1.0 / 3);
            if (From(mid, g)) return true;   // most often seen from there
            int n = Math.Max(1, Math.Min(16, (int)Math.Ceiling(Math.Max((b - a).Length, Math.Max((c - a).Length, (c - b).Length)) / Spacing)));
            for (int i = 0; i <= n; i++)
                for (int j = 0; i + j <= n; j++)
                {
                    var q = a + (b - a) * ((double)i / n) + (c - a) * ((double)j / n);
                    var dq = mid - q; double dl = dq.Length;
                    if (From(dl > 1e-9 ? q + dq * Math.Min(0.1, 1e-3 / dl) : q, g)) return true;
                }
            return false;
        }

        /// <summary>How far apart, at most, the points a triangle is seen from behind from are (<see cref="Seen"/>), metres.</summary>
        const double Spacing = 0.02;

        // whether the rooms see point q of a face turned to g from behind: rays from just behind it into the building, straight
        // in and aslant
        bool From(P3 q, P3 g)
        {
            var o = q - g * 1e-6;
            foreach (double k in Steep)
            {
                var straight = (g * -1 + new P3(0, 0, -k)).Normalized;
                for (int l = -1; l < Lean.Length; l++)
                {
                    var d = l < 0 ? straight : (straight + Lean[l]).Normalized;
                    if (d.z > -0.05 || P3.Dot(d, g) > -0.02) continue;   // into the building, from behind
                    if (Through(o, d)) return true;
                }
            }
            return false;
        }

        // whether a ray from o along d gets behind all of the mesh within the hole, past none of its triangles
        bool Through(P3 o, P3 d)
        {
            const double E = 1e-4;
            bool InHole(P3 q) => q.x >= -w / 2 - E && q.x <= w / 2 + E && q.y >= -E && q.y <= h + E;
            // the wall round the hole: where the ray comes to the wall's face, and where it is behind all of the mesh
            if (!InHole(o.z > 0 ? o + d * (-o.z / d.z) : o)) return false;
            double tEnd = Math.Max(0, (zEnd - o.z) / d.z); var end = o + d * tEnd;
            if (!InHole(end)) return false;
            int i0 = Cell(Math.Min(o.x, end.x) - x0, nx), i1 = Cell(Math.Max(o.x, end.x) - x0, nx);
            int j0 = Cell(Math.Min(o.y, end.y) - y0, ny), j1 = Cell(Math.Max(o.y, end.y) - y0, ny);
            for (int i = i0; i <= i1; i++)
                for (int j = j0; j <= j1; j++)
                    for (int x = start[i * ny + j]; x < start[i * ny + j + 1]; x++)
                        if (Hits(items[x], o, d, tEnd)) return false;
            return true;
        }

        // the ray from o along d meets the triangle at v[at..at + 9] before tMax (Möller–Trumbore, either side)
        bool Hits(int at, P3 o, P3 d, double tMax)
        {
            double ax = v[at], ay = v[at + 1], az = v[at + 2];
            double e1x = v[at + 3] - ax, e1y = v[at + 4] - ay, e1z = v[at + 5] - az, e2x = v[at + 6] - ax, e2y = v[at + 7] - ay, e2z = v[at + 8] - az;
            double px = d.y * e2z - d.z * e2y, py = d.z * e2x - d.x * e2z, pz = d.x * e2y - d.y * e2x;
            double det = e1x * px + e1y * py + e1z * pz; if (Math.Abs(det) < 1e-15) return false;
            double inv = 1 / det, sx = o.x - ax, sy = o.y - ay, sz = o.z - az;
            double u = (sx * px + sy * py + sz * pz) * inv; if (u < 0 || u > 1) return false;
            double qx = sy * e1z - sz * e1y, qy = sz * e1x - sx * e1z, qz = sx * e1y - sy * e1x;
            double vv = (d.x * qx + d.y * qy + d.z * qz) * inv; if (vv < 0 || u + vv > 1) return false;
            double t = (e2x * qx + e2y * qy + e2z * qz) * inv;
            return t > 1e-7 && t < tMax;
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
        // bent to a corner (Bent): each half turned about the corner's edge, across u and Dim.T_EXT out, by bend (the left half
        // one way, the right the other); a point on the bend out along the corner's bisector, bendOut times as far
        readonly double bend, bendOut;

        public KindPlace(Frame f, double u, double y0, double sx, double sy) : this(f, u, y0, sx, sy, false, 0, 0, 0, 1, false, 0, 0, 0, 1, false, 0, 0, 0, 1) { }

        KindPlace(Frame f, double u, double y0, double sx, double sy, bool cutX, double xL, double xR, double XL, double mx, bool cutY, double yB, double yT, double YB, double my, bool rigid, double ax, double ay,
            double bend, double bendOut)
        {
            F = f; this.u = u; this.y0 = y0; this.sx = sx; this.sy = sy;
            flip = f.u.x * f.w.z - f.u.z * f.w.x < 0 ? -1 : 1;
            this.cutX = cutX; this.xL = xL; this.xR = xR; this.XL = XL; this.mx = mx;
            this.cutY = cutY; this.yB = yB; this.yT = yT; this.YB = YB; this.my = my;
            this.rigid = rigid; this.ax = ax; this.ay = ay;
            this.bend = bend; this.bendOut = bendOut;
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
                cx, xL, xR, XL, cx ? (XR - XL) / (xR - xL) : 1, cy, yB, yT, YB, cy ? (YT - YB) / (yT - yB) : 1, false, 0, 0, 0, 1);
        }

        /// <summary>The same placement for a part that keeps its shape: it goes where its anchor goes, unscaled.</summary>
        public KindPlace Rigid(P3 anchor) => new KindPlace(F, u, y0, sx, sy, cutX, xL, xR, XL, mx, cutY, yB, yT, YB, my, true, anchor.x, anchor.y, bend, bendOut);

        /// <summary>
        /// The same placement bent to a corner (Details.Corners): a kind made for a 90° corner, at a corner of another angle, each
        /// half turned by <paramref name="by"/> (radians; the left half that way, the right the other) about the corner's edge,
        /// so each lies on its own wall as it lies on its wall at 90°. A point on the bend goes out along the corner's bisector,
        /// <paramref name="outScale"/> times as far, where it stands as far from both walls as it did: the two halves still meet.
        /// </summary>
        public KindPlace Bent(double by, double outScale) => new KindPlace(F, u, y0, sx, sy, cutX, xL, xR, XL, mx, cutY, yB, yT, YB, my, rigid, ax, ay, by, outScale);

        /// <summary>A point's place across and out (frame space) bent to the corner; <paramref name="side"/>: which half it is in (0 on the bend).</summary>
        (double a, double w) Bend(double a, double w, double side)
        {
            double r = w - Dim.T_EXT;
            if (side == 0) return (u, Dim.T_EXT + r * bendOut);
            double t = side < 0 ? bend : -bend, c = Math.Cos(t), s = Math.Sin(t), x = a - u;
            return (u + x * c - r * s, Dim.T_EXT + x * s + r * c);
        }

        // within this of the bend, a point is on it
        const double OnBend = 1e-4;

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

        public P3 Point(P3 p)
        {
            double a = rigid ? u + flip * (X(ax) + p.x - ax) : u + flip * X(p.x), y = rigid ? y0 + Y(ay) + p.y - ay : y0 + Y(p.y), w = Dim.T_EXT + p.z;
            if (bend != 0) (a, w) = Bend(a, w, Math.Abs(a - u) <= OnBend ? 0 : a - u);
            return F.At(a, y, w);
        }

        /// <summary>The normal <paramref name="n"/> at point <paramref name="p"/> (its own space): by the inverse of the scale where the point is.</summary>
        public P3 Normal(P3 p, P3 n)
        {
            double kx = rigid ? 1 : !cutX ? sx : p.x < xL || p.x > xR ? 1 : mx;
            double ky = rigid ? 1 : !cutY ? sy : p.y < yB || p.y > yT ? 1 : my;
            double nx = n.x / kx, ny = n.y / ky, nz = n.z, na = flip * nx;
            if (bend != 0)
            {
                // turned with its half: by where the point is, or on the bend by which way it faces
                double a = rigid ? u + flip * (X(ax) + p.x - ax) : u + flip * X(p.x), side = Math.Abs(a - u) > OnBend ? a - u : na;
                if (side != 0) { double t = side < 0 ? bend : -bend, c = Math.Cos(t), s = Math.Sin(t); (na, nz) = (na * c - nz * s, na * s + nz * c); }
            }
            return new P3(F.u.x * na + F.w.x * nz, ny, F.u.z * na + F.w.z * nz).Normalized;
        }
    }
}
