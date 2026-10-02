#nullable enable
using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace Triband.Storey.Generate
{
    /// <summary>Outline maths: the prototype's small helpers and the corner rule.</summary>
    public static class Geo
    {
        public static double Area2(List<Vec2> fp)
        {
            double s = 0;
            for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var b = fp[(i + 1) % fp.Count]; s += a.x * b.z - b.x * a.z; }
            return s;
        }

        public static bool Pip(List<Vec2> poly, double x, double z)
        {
            bool ins = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if (((a.z > z) != (b.z > z)) && (x < (b.x - a.x) * (z - a.z) / (b.z - a.z) + a.x)) ins = !ins;
            }
            return ins;
        }

        public struct SegHit { public double d, t, cx, cz; }

        public static SegHit SegDist(double px, double pz, double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az, L2 = dx * dx + dz * dz;
            double t = L2 != 0 ? ((px - ax) * dx + (pz - az) * dz) / L2 : 0;
            t = Math.Max(0, Math.Min(1, t));
            double cx = ax + dx * t, cz = az + dz * t;
            return new SegHit { d = Math.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz)), t = t, cx = cx, cz = cz };
        }

        public static double EdgeLen(List<Vec2> fp, int i)
        {
            var a = fp[i]; var c = fp[(i + 1) % fp.Count];
            return Math.Sqrt((c.x - a.x) * (c.x - a.x) + (c.z - a.z) * (c.z - a.z));
        }

        public static double DistToEdges(List<Vec2> fp, Vec2 p)
        {
            double d = 1e9;
            for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; d = Math.Min(d, SegDist(p.x, p.z, a.x, a.z, c.x, c.z).d); }
            return d;
        }

        public static double Hypot(double a, double b) => Math.Sqrt(a * a + b * b);

        /// <summary>Normal of a slanted end face of a mitred box (the prototype's <c>endNormal</c>).</summary>
        public static P3 EndNormal(Frame F, double a0, double a1, double w0, double w1, int sign)
        {
            double dx = F.u.x * (a1 - a0) + F.w.x * (w1 - w0), dz = F.u.z * (a1 - a0) + F.w.z * (w1 - w0);
            double nx = dz, nz = -dx, l = Hypot(nx, nz); if (l == 0) l = 1;
            nx /= l; nz /= l;
            if ((nx * F.u.x + nz * F.u.z) * sign < 0) { nx = -nx; nz = -nz; }
            return new P3(nx, 0, nz);
        }

        /// <summary>Edge i of a world outline: start, length, unit direction, outward normal.</summary>
        public struct Edge { public Vec2 a; public double L; public Vec2 u, w; public Frame F => new Frame(a, u, w); }

        public static Edge EdgeInfo(List<Vec2> Wp, int i, bool ccw)
        {
            var a = Wp[i]; var c = Wp[(i + 1) % Wp.Count];
            double dx = c.x - a.x, dz = c.z - a.z, L = Hypot(dx, dz); if (L == 0) L = 1e-6;
            var u = new Vec2(dx / L, dz / L);
            var w = ccw ? new Vec2(u.z, -u.x) : new Vec2(-u.z, u.x);
            return new Edge { a = a, L = L, u = u, w = w };
        }

        /// <summary>Corner rule: every strip along an edge ends on the corner's bisector plane.</summary>
        public struct Corner { public int sg; public double t; }

        public static Corner[] Corners(List<Vec2> fp, bool ccw)
        {
            int n = fp.Count; var out_ = new Corner[n];
            for (int i = 0; i < n; i++)
            {
                var p = fp[i]; var a = fp[(i - 1 + n) % n]; var c = fp[(i + 1) % n];
                double ax = p.x - a.x, az = p.z - a.z, cx = c.x - p.x, cz = c.z - p.z;
                double la = Hypot(ax, az), lc = Hypot(cx, cz); if (la == 0) la = 1; if (lc == 0) lc = 1;
                double cr = ax * cz - az * cx, cs = Math.Max(-1, Math.Min(1, (ax * cx + az * cz) / (la * lc)));
                bool convex = ccw ? cr > 1e-9 : cr < -1e-9;
                out_[i] = new Corner { sg = convex ? 1 : -1, t = Math.Min(1.5, Math.Sqrt(Math.Max(0, 1 - cs) / (1 + cs + 1e-9))) };
            }
            return out_;
        }

        public static Miter MiterOf(Corner[] cor, int i, double L)
        {
            var c0 = cor[i]; var c1 = cor[(i + 1) % cor.Length];
            return new Miter(new Cut(0, -c0.sg * c0.t), new Cut(L, c1.sg * c1.t));
        }

        /// <summary>
        /// Cutaway data for a wall running L metres from a along u: a start point from which the wall
        /// runs along (w.z, −w.x), and the normal scaled by 1 + L.
        /// </summary>
        public static double[] WallCtx(Vec2 a, Vec2 u, Vec2 w, double L)
        {
            var s = w.z * u.x - w.x * u.z >= 0 ? a : new Vec2(a.x + u.x * L, a.z + u.z * L);
            return new[] { s.x, s.z, w.x * (1 + L), w.z * (1 + L) };
        }

        /// <summary>Ranges minus cuts (both as [s,e] along an edge).</summary>
        public static List<(double, double)> MinusRanges(List<(double, double)> rs, List<(double, double)> cuts)
        {
            var out_ = new List<(double, double)>(rs);
            foreach (var (c0, c1) in cuts)
            {
                var next = new List<(double, double)>();
                foreach (var (a, e) in out_)
                {
                    if (c1 <= a || c0 >= e) { next.Add((a, e)); continue; }
                    if (Math.Min(e, c0) - a > 0.02) next.Add((a, Math.Min(e, c0)));
                    if (e - Math.Max(a, c1) > 0.02) next.Add((Math.Max(a, c1), e));
                }
                out_ = next;
            }
            return out_;
        }

        /// <summary>
        /// Parts of edge i of outline A that lie outside outline B (or strictly inside it with
        /// <paramref name="inside"/>), as [s,e] along the edge.
        /// </summary>
        public static List<(double, double)> ExposedRanges(List<Vec2> A, List<Vec2> B, int i, bool inside = false)
        {
            var a = A[i]; var c = A[(i + 1) % A.Count];
            double dx = c.x - a.x, dz = c.z - a.z, L = Hypot(dx, dz); if (L == 0) L = 1e-6;
            var ts = new List<double> { 0, L };
            for (int j = 0; j < B.Count; j++)
            {
                var p = B[j]; var q = B[(j + 1) % B.Count];
                var r = SegDist(p.x, p.z, a.x, a.z, c.x, c.z); if (r.d < 0.01) ts.Add(r.t * L);
                double ex = q.x - p.x, ez = q.z - p.z, den = dx * ez - dz * ex; if (Math.Abs(den) < 1e-9) continue;
                double t = ((p.x - a.x) * ez - (p.z - a.z) * ex) / den, u = ((p.x - a.x) * dz - (p.z - a.z) * dx) / den;
                if (t > 0 && t < 1 && u >= 0 && u <= 1) ts.Add(t * L);
            }
            ts.Sort();
            var out_ = new List<(double, double)>();
            for (int j = 0; j < ts.Count - 1; j++)
            {
                double s0 = ts[j], e0 = ts[j + 1]; if (e0 - s0 < 0.02) continue;
                double m = (s0 + e0) / 2 / L, x = a.x + dx * m, z = a.z + dz * m;
                bool on = DistToEdges(B, new Vec2(x, z)) < 0.01, inB = !on && Pip(B, x, z);
                if (inside ? !inB : (inB || on)) continue;
                if (out_.Count > 0 && s0 - out_[out_.Count - 1].Item2 < 0.02) out_[out_.Count - 1] = (out_[out_.Count - 1].Item1, e0);
                else out_.Add((s0, e0));
            }
            out_.RemoveAll(r => r.Item2 - r.Item1 <= 0.05);
            return out_;
        }

        /// <summary>The terrace deck: lower minus upper, as polygons with holes (outer ring first).</summary>
        public static List<List<List<Vec2>>> TerracePolys(List<Vec2> lower, List<Vec2> upper)
        {
            var subj = new PathsD { Path(lower) }; var clip = new PathsD { Path(upper) };
            var tree = new PolyTreeD();
            Clipper.BooleanOp(ClipType.Difference, subj, clip, tree, FillRule.NonZero, 6);
            var out_ = new List<List<List<Vec2>>>();
            Collect(tree, out_);
            out_.RemoveAll(poly => poly[0].Count < 3 || Math.Abs(Area2(poly[0])) <= 0.02);
            return out_;
        }

        /// <summary>
        /// A walkable level as polygons with holes (outer ring first): the union of <paramref name="outlines"/>, grown by
        /// <paramref name="grow"/> with round corners (every point within that distance of one of them), minus
        /// <paramref name="holes"/>.
        /// </summary>
        public static List<List<List<Vec2>>> GrownUnion(List<List<Vec2>> outlines, double grow, List<List<Vec2>> holes)
        {
            var subj = new PathsD(); foreach (var o in outlines) if (o.Count >= 3) subj.Add(Path(o));
            var union = Clipper.Union(subj, new PathsD(), FillRule.NonZero, 6);
            var grown = grow > 0 ? Clipper.InflatePaths(union, grow, JoinType.Round, EndType.Polygon, 2.0, 6, 0.005) : union;
            var clip = new PathsD(); foreach (var h in holes) if (h.Count >= 3) clip.Add(Path(h));
            var tree = new PolyTreeD();
            Clipper.BooleanOp(ClipType.Difference, grown, clip, tree, FillRule.NonZero, 6);
            var out_ = new List<List<List<Vec2>>>();
            Collect(tree, out_);
            out_.RemoveAll(poly => poly[0].Count < 3 || Math.Abs(Area2(poly[0])) <= 0.0001);
            return out_;
        }

        static void Collect(PolyPathD node, List<List<List<Vec2>>> out_)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var child = (PolyPathD)node[i];
                if (child.Polygon == null) continue;
                var poly = new List<List<Vec2>> { Ring(child.Polygon) };
                for (int j = 0; j < child.Count; j++)
                {
                    var hole = (PolyPathD)child[j];
                    if (hole.Polygon != null) poly.Add(Ring(hole.Polygon));
                    Collect(hole, out_);   // islands inside holes are polygons of their own
                }
                out_.Add(poly);
            }
        }

        static PathD Path(List<Vec2> fp) { var p = new PathD(fp.Count); foreach (var v in fp) p.Add(new PointD(v.x, v.z)); return p; }
        static List<Vec2> Ring(PathD p) { var r = new List<Vec2>(p.Count); foreach (var v in p) r.Add(new Vec2(v.x, v.y)); return r; }

        /// <summary>Interior walls are trimmed to the storey's outline (a setback can cut through a copied layout).</summary>
        public static List<WallData> ClipWall(List<Vec2> fp, WallData wl)
        {
            double ax = wl.a.x, az = wl.a.z, dx = wl.b.x - ax, dz = wl.b.z - az, L = Hypot(dx, dz);
            var out_ = new List<WallData>();
            if (L < 0.05) return out_;
            var ts = new List<double> { 0, 1 };
            for (int i = 0; i < fp.Count; i++)
            {
                var p = fp[i]; var q = fp[(i + 1) % fp.Count];
                double ex = q.x - p.x, ez = q.z - p.z, den = dx * ez - dz * ex; if (Math.Abs(den) < 1e-9) continue;
                double t = ((p.x - ax) * ez - (p.z - az) * ex) / den, u = ((p.x - ax) * dz - (p.z - az) * dx) / den;
                if (t > 1e-4 && t < 1 - 1e-4 && u >= -1e-6 && u <= 1 + 1e-6) ts.Add(t);
            }
            bool Keep(double t0, double t1)
            {
                double tm = (t0 + t1) / 2; var m = new Vec2(ax + dx * tm, az + dz * tm);
                return (t1 - t0) * L >= 0.3 && Pip(fp, m.x, m.z) && DistToEdges(fp, m) > 0.05;
            }
            if (ts.Count == 2) { if (Keep(0, 1)) out_.Add(wl); return out_; }
            ts.Sort();
            for (int j = 0; j < ts.Count - 1; j++)
            {
                double t0 = ts[j], t1 = ts[j + 1]; if (!Keep(t0, t1)) continue;
                var w = new WallData { a = new Vec2(ax + dx * t0, az + dz * t0), b = new Vec2(ax + dx * t1, az + dz * t1) };
                foreach (var d in wl.doors) if (d.t > t0 && d.t < t1) w.doors.Add(new DoorData { t = (d.t - t0) / (t1 - t0) });
                out_.Add(w);
            }
            return out_;
        }
    }
}
