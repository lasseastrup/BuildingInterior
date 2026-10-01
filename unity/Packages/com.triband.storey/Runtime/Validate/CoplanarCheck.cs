#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;

namespace Triband.Storey.Validate
{
    /// <summary>
    /// Finds pairs of same-facing, coplanar triangles that overlap with positive area: z-fighting
    /// candidates (SPEC §4.5). A port of the prototype's checker. An overlap is ignored when a point
    /// just in front of it lies inside one of the mesh's solids (a buried face cannot be seen), or
    /// when it is a downward face at ground level.
    /// </summary>
    public static class CoplanarCheck
    {
        public sealed class Overlap
        {
            /// <summary>SameColour: both faces use the same swatch, so they show the same colour whatever the palette.</summary>
            public P3 At; public P3 N; public bool SameColour; public double Area;
            public override string ToString() => $"{(SameColour ? "same colour" : "different colours")} at {At}, normal {N}, area {Area:0.####}";
        }

        public sealed class Report
        {
            public int Triangles, Buried;
            public List<Overlap> Overlaps = new List<Overlap>();
        }

        sealed class Tri { public P3[] P = null!; public P3 n; public Swatch c; public double d; }

        public static Report Run(MeshBuilder gb) => Run(new[] { gb });

        /// <summary>Check several meshes together (LOD levels that are visible at once).</summary>
        public static Report Run(IEnumerable<MeshBuilder> meshes)
        {
            var tris = new List<Tri>(); var solids = new List<Solid>();
            foreach (var L in meshes)
            {
                if (L.Solids != null) solids.AddRange(L.Solids);
                for (int t = 0; t < L.I.Count; t += 3)
                {
                    int a = L.I[t], b = L.I[t + 1], c = L.I[t + 2];
                    var tri = new Tri { P = new[] { L.P[a], L.P[b], L.P[c] }, n = L.N[a], c = L.C[a] };
                    tri.d = P3.Dot(tri.n, tri.P[0]);
                    tris.Add(tri);
                }
            }
            var report = new Report { Triangles = tris.Count };
            var buckets = new Dictionary<string, List<Tri>>(StringComparer.Ordinal);
            foreach (var t in tris)
            {
                string key = $"{Math.Round(t.n.x * 1000)},{Math.Round(t.n.y * 1000)},{Math.Round(t.n.z * 1000)}|{Math.Round(t.d * 500)}";
                if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<Tri>();
                l.Add(t);
            }
            bool Inside(P3 p) { foreach (var s in solids) if (s.Contains(p)) return true; return false; }

            foreach (var arr in buckets.Values)
            {
                if (arr.Count < 2) continue;
                var pr = new List<Vec2[]>(arr.Count);
                foreach (var t in arr) pr.Add(Project(t));
                for (int a = 0; a < arr.Count; a++)
                    for (int c = a + 1; c < arr.Count; c++)
                    {
                        if (Math.Abs(arr[a].d - arr[c].d) > 0.0015) continue;
                        var o = Clip(pr[a], pr[c]);
                        if (o.Count < 3 || Math.Abs(Area(o)) <= 2e-4) continue;
                        var (U, V) = Basis(arr[a].n);
                        double cx = 0, cy = 0; foreach (var p in o) { cx += p.x; cy += p.z; } cx /= o.Count; cy /= o.Count;
                        var n = arr[a].n; double d = arr[a].d;
                        var p3 = U * cx + V * cy + n * (d + 0.01);
                        if (Inside(p3)) { report.Buried++; continue; }
                        if (n.y < -0.9 && Math.Abs(d) < 0.01) { report.Buried++; continue; }
                        var ca = arr[a].c; var cb = arr[c].c;
                        bool same = ca.Equals(cb);
                        report.Overlaps.Add(new Overlap { At = arr[a].P[0], N = n, SameColour = same, Area = Math.Abs(Area(o)) });
                    }
            }
            return report;
        }

        static (P3 U, P3 V) Basis(P3 n)
        {
            var u = Math.Abs(n.y) < 0.9 ? new P3(-n.z, 0, n.x) : new P3(1, 0, 0);
            var U = u.Normalized;
            var V = new P3(n.y * U.z - n.z * U.y, n.z * U.x - n.x * U.z, n.x * U.y - n.y * U.x);
            return (U, V);
        }

        static Vec2[] Project(Tri t)
        {
            var (U, V) = Basis(t.n);
            var o = new Vec2[3];
            for (int i = 0; i < 3; i++) o[i] = new Vec2(P3.Dot(t.P[i], U), P3.Dot(t.P[i], V));
            return o;
        }

        static double Area(List<Vec2> poly)
        {
            double a = 0;
            for (int k = 0; k < poly.Count; k++) { var p = poly[k]; var q = poly[(k + 1) % poly.Count]; a += p.x * q.z - q.x * p.z; }
            return a / 2;
        }

        /// <summary>Sutherland–Hodgman clip of one triangle by another (both convex).</summary>
        static List<Vec2> Clip(Vec2[] subj, Vec2[] clipP)
        {
            var out_ = new List<Vec2>(subj);
            bool ccw = Area(new List<Vec2>(clipP)) > 0;
            for (int k = 0; k < clipP.Length; k++)
            {
                var A = clipP[k]; var B = clipP[(k + 1) % clipP.Length];
                bool In(Vec2 p) { double c = (B.x - A.x) * (p.z - A.z) - (B.z - A.z) * (p.x - A.x); return ccw ? c >= -1e-9 : c <= 1e-9; }
                var inp = out_; out_ = new List<Vec2>();
                for (int m = 0; m < inp.Count; m++)
                {
                    var P = inp[m]; var Q = inp[(m + 1) % inp.Count]; bool ip = In(P), iq = In(Q);
                    if (ip) out_.Add(P);
                    if (ip != iq)
                    {
                        double x1 = P.x, y1 = P.z, x2 = Q.x, y2 = Q.z, x3 = A.x, y3 = A.z, x4 = B.x, y4 = B.z;
                        double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
                        if (Math.Abs(den) > 1e-12) { double tt = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / den; out_.Add(new Vec2(x1 + tt * (x2 - x1), y1 + tt * (y2 - y1))); }
                    }
                }
                if (out_.Count == 0) break;
            }
            return out_;
        }
    }
}
