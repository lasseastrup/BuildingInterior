#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// Straight skeleton of a simple polygon (the hip roof): every edge moves inward at unit speed and
    /// sweeps one roof face; a face's height is the time its edge took to get there. Brute-force event
    /// simulation (fine for the few dozen corners a building has): find the next edge event (an edge
    /// shrinks to nothing) or split event (a reflex corner runs into another edge), apply it, repeat.
    /// A port of the prototype's <c>straightSkeleton</c>; the outline must be counter-clockwise.
    /// </summary>
    public static class Skeleton
    {
        public sealed class Face { public int Edge; public List<double[]> Pts = new List<double[]>(); }
        public sealed class Result { public List<Face> Faces = new List<Face>(); public double TMax; }

        sealed class Ed { public double[] a = null!, d = null!, n = null!; }
        sealed class Vtx { public double[] p = null!; public double t0; public int e1, e2, node; public double[]? v; }

        public static Result Compute(List<Vec2> poly)
        {
            int n = poly.Count; const double EPS = 1e-9;
            var E = new Ed[n];
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                double L = Geo.Hypot(q.x - p.x, q.z - p.z); if (L == 0) L = 1;
                var d = new[] { (q.x - p.x) / L, (q.z - p.z) / L };
                E[i] = new Ed { a = new[] { p.x, p.z }, d = d, n = new[] { -d[1], d[0] } };
            }
            var nodes = new List<double[]>(); var arcs = new List<int[]>();
            int Node(double x, double z, double t)
            {
                for (int i = 0; i < nodes.Count; i++) { var q = nodes[i]; if (Math.Abs(q[0] - x) < 1e-6 && Math.Abs(q[1] - z) < 1e-6) return i; }
                nodes.Add(new[] { x, z, t }); return nodes.Count - 1;
            }
            double[]? Vel(int e1, int e2)
            {
                var a = E[e1].n; var b = E[e2].n; double det = a[0] * b[1] - a[1] * b[0];
                if (Math.Abs(det) < 1e-9) return a[0] * b[0] + a[1] * b[1] > 0 ? new[] { a[0], a[1] } : null;
                return new[] { (b[1] - a[1]) / det, (a[0] - b[0]) / det };
            }
            Vtx Mk(double x, double z, double t, int e1, int e2, int nd) => new Vtx { p = new[] { x, z }, t0 = t, e1 = e1, e2 = e2, node = nd, v = Vel(e1, e2) };
            double now = 0;
            double[] Pos(Vtx v, double t) => v.v != null ? new[] { v.p[0] + v.v[0] * (t - v.t0), v.p[1] + v.v[1] * (t - v.t0) } : v.p;

            var lavs = new List<List<Vtx>>();
            { var l = new List<Vtx>(); for (int i = 0; i < n; i++) l.Add(Mk(poly[i].x, poly[i].z, 0, (i - 1 + n) % n, i, Node(poly[i].x, poly[i].z, 0))); lavs.Add(l); }
            int guard = 0;
            void Close(List<Vtx> l)
            {
                int m = l.Count; var P = new int[m];
                for (int k = 0; k < m; k++) { var p = Pos(l[k], now); P[k] = Node(p[0], p[1], now); }
                for (int k = 0; k < m; k++) arcs.Add(new[] { l[k].node, P[k], l[k].e1, l[k].e2 });
                for (int k = 0; k < m; k++) { int s = l[k].e2; arcs.Add(new[] { P[k], P[(k + 1) % m], s, s }); }
            }
            bool Flat(List<Vtx> l)
            {
                double A = 0; var P = new List<double[]>(); foreach (var v in l) P.Add(Pos(v, now));
                for (int k = 0; k < P.Count; k++) { var a = P[k]; var c = P[(k + 1) % P.Count]; A += a[0] * c[1] - c[0] * a[1]; }
                return Math.Abs(A) < 1e-7;
            }

            while (lavs.Count > 0 && guard++ < 4 * n + 20)
            {
                var keep = new List<List<Vtx>>();
                foreach (var l in lavs) { if (l.Count > 2 && !Flat(l)) keep.Add(l); else if (l.Count >= 2) Close(l); }
                lavs = keep; if (lavs.Count == 0) break;
                double bestT = double.PositiveInfinity; List<Vtx>? bestL = null; int bestI = -1, bestJ = -1; bool bestSplit = false;
                foreach (var l in lavs)
                {
                    int m = l.Count;
                    for (int i = 0; i < m; i++)
                    {
                        var a = l[i]; var b = l[(i + 1) % m]; var d = E[a.e2].d; var pa = Pos(a, now); var pb = Pos(b, now);
                        double L0 = (pb[0] - pa[0]) * d[0] + (pb[1] - pa[1]) * d[1];
                        var va = a.v ?? new[] { 0.0, 0.0 }; var vb = b.v ?? new[] { 0.0, 0.0 };
                        double rate = (vb[0] - va[0]) * d[0] + (vb[1] - va[1]) * d[1];
                        double t = L0 <= 1e-7 ? now : rate < -EPS ? now + L0 / (-rate) : double.PositiveInfinity;
                        if (t < double.PositiveInfinity && (bestL == null || t < bestT - 1e-9)) { bestT = t; bestL = l; bestI = i; bestSplit = false; }
                    }
                    for (int r = 0; r < m; r++)
                    {
                        var R = l[r]; if (R.v == null) continue;
                        var din = E[R.e1].d; var dout = E[R.e2].d; if (din[0] * dout[1] - din[1] * dout[0] > -1e-9) continue;
                        var pr = Pos(R, now);
                        for (int j = 0; j < m; j++)
                        {
                            var X = l[j]; var Y = l[(j + 1) % m]; int f = X.e2; if (f == R.e1 || f == R.e2 || X == R || Y == R) continue;
                            var F = E[f]; double s0 = (pr[0] - F.a[0]) * F.n[0] + (pr[1] - F.a[1]) * F.n[1] - now, rate = R.v[0] * F.n[0] + R.v[1] * F.n[1] - 1;
                            if (rate > -EPS || s0 < -1e-7) continue;
                            double t = now + Math.Max(0, s0) / (-rate); if (bestL != null && t >= bestT - 1e-9) continue;
                            var h = Pos(R, t); var px = Pos(X, t); var py = Pos(Y, t);
                            double ux = (px[0] - h[0]) * F.d[0] + (px[1] - h[1]) * F.d[1], uy = (py[0] - h[0]) * F.d[0] + (py[1] - h[1]) * F.d[1];
                            if (ux > 1e-6 || uy < -1e-6) continue;
                            bestT = t; bestL = l; bestI = r; bestJ = j; bestSplit = true;
                        }
                    }
                }
                if (bestL == null) { foreach (var l in lavs) Close(l); break; }
                now = bestT; var lav = bestL; int mm = lav.Count;
                if (!bestSplit)
                {
                    var a = lav[bestI]; var b = lav[(bestI + 1) % mm]; var pa = Pos(a, now); var pb = Pos(b, now);
                    double x = (pa[0] + pb[0]) / 2, z = (pa[1] + pb[1]) / 2; int nd = Node(x, z, now);
                    arcs.Add(new[] { a.node, nd, a.e1, a.e2 }); arcs.Add(new[] { b.node, nd, b.e1, b.e2 });
                    var c = Mk(x, z, now, a.e1, b.e2, nd);
                    if (a.e1 == b.e2) { lavs.Remove(lav); continue; }
                    lav[bestI] = c; lav.Remove(b);
                }
                else
                {
                    var R = lav[bestI]; var h = Pos(R, now); int nd = Node(h[0], h[1], now); var X = lav[bestJ]; int f = X.e2;
                    arcs.Add(new[] { R.node, nd, R.e1, R.e2 });
                    var V1 = Mk(h[0], h[1], now, R.e1, f, nd); var V2 = Mk(h[0], h[1], now, f, R.e2, nd);
                    var A = new List<Vtx> { V1 }; var B = new List<Vtx> { V2 };
                    for (int k = (bestJ + 1) % mm; k != bestI; k = (k + 1) % mm) A.Add(lav[k]);
                    for (int k = (bestI + 1) % mm; k != (bestJ + 1) % mm; k = (k + 1) % mm) B.Add(lav[k]);
                    lavs.Remove(lav); lavs.Add(A); lavs.Add(B);
                }
            }

            // faces: each edge plus the arcs of the corners that bordered it
            var result = new Result();
            for (int i = 0; i < n; i++)
            {
                var adj = new Dictionary<int, List<int>>();
                void Add(int u, int v)
                {
                    if (u == v) return;
                    if (!adj.TryGetValue(u, out var lu)) adj[u] = lu = new List<int>(); lu.Add(v);
                    if (!adj.TryGetValue(v, out var lv)) adj[v] = lv = new List<int>(); lv.Add(u);
                }
                var fa = new List<int[]>(); foreach (var a in arcs) if (a[2] == i || a[3] == i) fa.Add(new[] { a[0], a[1] });
                var fn = new List<int>(); var seenN = new HashSet<int>(); foreach (var a in fa) foreach (var v in a) if (seenN.Add(v)) fn.Add(v);
                var keys = new HashSet<string>();
                List<int[]> Split(int u, int v)
                {
                    var A = nodes[u]; var B = nodes[v]; double dx = B[0] - A[0], dz = B[1] - A[1], L2 = dx * dx + dz * dz;
                    if (L2 < 1e-12) return new List<int[]>();
                    foreach (var w in fn)
                    {
                        if (w == u || w == v) continue; var C = nodes[w]; double t = ((C[0] - A[0]) * dx + (C[1] - A[1]) * dz) / L2;
                        if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                        if (Math.Abs((C[0] - A[0]) * dz - (C[1] - A[1]) * dx) / Math.Sqrt(L2) < 1e-6) { var o = Split(u, w); o.AddRange(Split(w, v)); return o; }
                    }
                    return new List<int[]> { new[] { u, v } };
                }
                foreach (var a in fa) foreach (var xy in Split(a[0], a[1])) { string k = Math.Min(xy[0], xy[1]) + ":" + Math.Max(xy[0], xy[1]); if (keys.Add(k)) Add(xy[0], xy[1]); }
                int s = Node(poly[i].x, poly[i].z, 0), e = Node(poly[(i + 1) % n].x, poly[(i + 1) % n].z, 0);
                var path = new List<int> { s, e }; var seen = new HashSet<int> { s, e }; int cur = e; bool ok = true;
                while (cur != s)
                {
                    adj.TryGetValue(cur, out var nbs); nbs ??= new List<int>();
                    int nx = -1; foreach (var v in nbs) if (!seen.Contains(v)) { nx = v; break; }
                    if (nx < 0 && nbs.Contains(s) && path.Count > 2) nx = s;
                    if (nx < 0) { ok = false; break; }
                    if (nx == s) break;
                    path.Add(nx); seen.Add(nx); cur = nx;
                }
                if (ok) { var face = new Face { Edge = i }; foreach (var k in path) face.Pts.Add(nodes[k]); result.Faces.Add(face); }
            }
            double tMax = 0; foreach (var q in nodes) tMax = Math.Max(tMax, q[2]);
            result.TMax = tMax;
            return result;
        }
    }
}
