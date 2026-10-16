using System;
using System.Collections.Generic;
using Triband.Storey.Generate;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What an eye sees first: rays against triangles (Möller–Trumbore, either side), each mesh added in a uniform grid of
    /// its own, every mesh whose box a ray crosses searched nearest first. A triangle carries a tag (what it is, for the
    /// test) and the normal its winding gives (front faces wind as the generator's: cross(b − a, c − a)). For the headless
    /// ray tests (BackFaceTests, RoomsTests).
    /// </summary>
    internal sealed class RayScene
    {
        public struct Tri
        {
            public double ax, ay, az, e1x, e1y, e1z, e2x, e2y, e2z;
            /// <summary>The winding's normal, not normalised.</summary>
            public double nx, ny, nz;
            public int tag, mesh;
        }

        sealed class Part
        {
            public double x0, y0, z0, x1, y1, z1, cs; public int nx, ny, nz, first, count;
            public int[] start = Array.Empty<int>(), items = Array.Empty<int>();
        }

        readonly List<Tri> tris = new List<Tri>();
        readonly List<Part> parts = new List<Part>();
        readonly List<(double x0, double y0, double z0, double x1, double y1, double z1)> boxes = new List<(double, double, double, double, double, double)>();
        int[] stamp = Array.Empty<int>();
        ICollection<int>? excluded;
        int ray;

        public int Count => tris.Count;
        public ref Tri this[int i] => ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tris)[i];

        /// <summary>The meshes added, by number: the box round each one's triangles.</summary>
        public (double x0, double y0, double z0, double x1, double y1, double z1) Box(int mesh) => boxes[mesh];
        public int Meshes => parts.Count;

        /// <summary>
        /// A mesh: the triangles of P and I (those in <paramref name="only"/>, or all), each tagged <paramref name="tag"/>(its
        /// index), in a grid of <paramref name="cell"/> metres. Degenerate ones are left out. Returns the mesh's number.
        /// </summary>
        public int Add(IReadOnlyList<P3> P, IReadOnlyList<int> I, Func<int, int> tag, double cell = 1.0, IReadOnlyList<int>? only = null)
        {
            int mesh = parts.Count, first = tris.Count;
            double X0 = double.MaxValue, Y0 = X0, Z0 = X0, X1 = double.MinValue, Y1 = X1, Z1 = X1;
            void One(int t)
            {
                var a = P[I[3 * t]]; var b = P[I[3 * t + 1]]; var c = P[I[3 * t + 2]];
                double e1x = b.x - a.x, e1y = b.y - a.y, e1z = b.z - a.z, e2x = c.x - a.x, e2y = c.y - a.y, e2z = c.z - a.z;
                double nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                if (nx * nx + ny * ny + nz * nz < 1e-14) return;
                tris.Add(new Tri { ax = a.x, ay = a.y, az = a.z, e1x = e1x, e1y = e1y, e1z = e1z, e2x = e2x, e2y = e2y, e2z = e2z, nx = nx, ny = ny, nz = nz, tag = tag(t), mesh = mesh });
                X0 = Math.Min(X0, Math.Min(a.x, Math.Min(b.x, c.x))); X1 = Math.Max(X1, Math.Max(a.x, Math.Max(b.x, c.x)));
                Y0 = Math.Min(Y0, Math.Min(a.y, Math.Min(b.y, c.y))); Y1 = Math.Max(Y1, Math.Max(a.y, Math.Max(b.y, c.y)));
                Z0 = Math.Min(Z0, Math.Min(a.z, Math.Min(b.z, c.z))); Z1 = Math.Max(Z1, Math.Max(a.z, Math.Max(b.z, c.z)));
            }
            if (only != null) foreach (int t in only) One(t);
            else for (int t = 0; t < I.Count / 3; t++) One(t);
            var p = new Part { first = first, count = tris.Count - first, cs = cell };
            parts.Add(p);
            if (p.count == 0) { boxes.Add((0, 0, 0, -1, -1, -1)); return mesh; }
            boxes.Add((X0, Y0, Z0, X1, Y1, Z1));
            p.x0 = X0 - 0.01; p.y0 = Y0 - 0.01; p.z0 = Z0 - 0.01;
            p.nx = Math.Max(1, (int)Math.Ceiling((X1 - p.x0 + 0.01) / cell)); p.ny = Math.Max(1, (int)Math.Ceiling((Y1 - p.y0 + 0.01) / cell)); p.nz = Math.Max(1, (int)Math.Ceiling((Z1 - p.z0 + 0.01) / cell));
            p.x1 = p.x0 + p.nx * cell; p.y1 = p.y0 + p.ny * cell; p.z1 = p.z0 + p.nz * cell;
            // the cells' lists, flat: start[c] .. start[c + 1] in items
            int cells = p.nx * p.ny * p.nz; var counts = new int[cells + 1];
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = first; i < tris.Count; i++)
                {
                    var q = tris[i];
                    double bx = q.ax + q.e1x, by = q.ay + q.e1y, bz = q.az + q.e1z, cx = q.ax + q.e2x, cy = q.ay + q.e2y, cz = q.az + q.e2z;
                    int ix0 = C(Math.Min(q.ax, Math.Min(bx, cx)) - p.x0, p.nx, cell), ix1 = C(Math.Max(q.ax, Math.Max(bx, cx)) - p.x0, p.nx, cell);
                    int iy0 = C(Math.Min(q.ay, Math.Min(by, cy)) - p.y0, p.ny, cell), iy1 = C(Math.Max(q.ay, Math.Max(by, cy)) - p.y0, p.ny, cell);
                    int iz0 = C(Math.Min(q.az, Math.Min(bz, cz)) - p.z0, p.nz, cell), iz1 = C(Math.Max(q.az, Math.Max(bz, cz)) - p.z0, p.nz, cell);
                    for (int a = ix0; a <= ix1; a++) for (int b = iy0; b <= iy1; b++) for (int c = iz0; c <= iz1; c++)
                    {
                        int id = (a * p.ny + b) * p.nz + c;
                        if (pass == 0) counts[id + 1]++; else p.items[counts[id]++] = i;
                    }
                }
                if (pass == 0)
                {
                    for (int c = 0; c < cells; c++) counts[c + 1] += counts[c];
                    p.start = (int[])counts.Clone(); p.items = new int[counts[cells]];
                }
            }
            return mesh;
        }

        static int C(double v, int n, double cs) => Math.Max(0, Math.Min(n - 1, (int)Math.Floor(v / cs)));

        /// <summary>
        /// The first triangle along o + t·d, t &gt; 0 (its index, and t), or -1. <paramref name="skip"/>: tags passed through;
        /// <paramref name="passed"/>: triangles (by index) passed through.
        /// </summary>
        public int Hit(double ox, double oy, double oz, double dx, double dy, double dz, out double tHit, Func<int, bool>? skip = null, ICollection<int>? passed = null)
        {
            excluded = passed;
            if (stamp.Length < tris.Count) stamp = new int[tris.Count];
            ray++;
            tHit = double.MaxValue; int best = -1;
            // the meshes the ray crosses, nearest entry first
            Span<(double t, int m)> order = parts.Count <= 256 ? stackalloc (double, int)[parts.Count] : new (double, int)[parts.Count];
            int n = 0;
            for (int m = 0; m < parts.Count; m++)
            {
                var p = parts[m]; if (p.count == 0) continue;
                double t0 = 0, t1 = double.MaxValue;
                if (Slab(ox, dx, p.x0, p.x1, ref t0, ref t1) && Slab(oy, dy, p.y0, p.y1, ref t0, ref t1) && Slab(oz, dz, p.z0, p.z1, ref t0, ref t1)) order[n++] = (t0, m);
            }
            var o = order.Slice(0, n); o.Sort((a, b) => a.t.CompareTo(b.t));
            foreach (var (tEnter, m) in o)
            {
                if (tEnter > tHit) break;
                Walk(parts[m], ox, oy, oz, dx, dy, dz, ref tHit, ref best, skip);
            }
            return best;
        }

        void Walk(Part p, double ox, double oy, double oz, double dx, double dy, double dz, ref double tbest, ref int best, Func<int, bool>? skip)
        {
            double tmin = 0, tmax = double.MaxValue, cs = p.cs;
            if (!Slab(ox, dx, p.x0, p.x1, ref tmin, ref tmax) || !Slab(oy, dy, p.y0, p.y1, ref tmin, ref tmax) || !Slab(oz, dz, p.z0, p.z1, ref tmin, ref tmax)) return;
            double t = tmin + 1e-9;
            int ix = C(ox + dx * t - p.x0, p.nx, cs), iy = C(oy + dy * t - p.y0, p.ny, cs), iz = C(oz + dz * t - p.z0, p.nz, cs);
            int sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
            double tdx = dx != 0 ? cs / Math.Abs(dx) : double.MaxValue, tdy = dy != 0 ? cs / Math.Abs(dy) : double.MaxValue, tdz = dz != 0 ? cs / Math.Abs(dz) : double.MaxValue;
            double tx = dx != 0 ? (p.x0 + (ix + (sx > 0 ? 1 : 0)) * cs - ox) / dx : double.MaxValue;
            double ty = dy != 0 ? (p.y0 + (iy + (sy > 0 ? 1 : 0)) * cs - oy) / dy : double.MaxValue;
            double tz = dz != 0 ? (p.z0 + (iz + (sz > 0 ? 1 : 0)) * cs - oz) / dz : double.MaxValue;
            var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tris);
            while (ix >= 0 && iy >= 0 && iz >= 0 && ix < p.nx && iy < p.ny && iz < p.nz)
            {
                int c = (ix * p.ny + iy) * p.nz + iz;
                for (int k = p.start[c]; k < p.start[c + 1]; k++)
                {
                    int i = p.items[k];
                    if (stamp[i] == ray) continue;
                    stamp[i] = ray;
                    ref var q = ref all[i];
                    if (skip != null && skip(q.tag)) continue;
                    if (excluded != null && excluded.Contains(i)) continue;
                    double th = Ray(ref q, ox, oy, oz, dx, dy, dz);
                    if (th > 1e-6 && th < tbest) { tbest = th; best = i; }
                }
                double next = Math.Min(tx, Math.Min(ty, tz));
                if (best >= 0 && tbest <= next) return;
                if (tx <= ty && tx <= tz) { ix += sx; tx += tdx; } else if (ty <= tz) { iy += sy; ty += tdy; } else { iz += sz; tz += tdz; }
            }
        }

        static bool Slab(double o, double d, double lo, double hi, ref double tmin, ref double tmax)
        {
            if (Math.Abs(d) < 1e-12) return o >= lo && o <= hi;
            double a = (lo - o) / d, b = (hi - o) / d; if (a > b) (a, b) = (b, a);
            tmin = Math.Max(tmin, a); tmax = Math.Min(tmax, b); return tmin <= tmax;
        }

        static double Ray(ref Tri q, double ox, double oy, double oz, double dx, double dy, double dz)
        {
            double px = dy * q.e2z - dz * q.e2y, py = dz * q.e2x - dx * q.e2z, pz = dx * q.e2y - dy * q.e2x;
            double det = q.e1x * px + q.e1y * py + q.e1z * pz; if (Math.Abs(det) < 1e-12) return -1;
            double inv = 1 / det, sx = ox - q.ax, sy = oy - q.ay, sz = oz - q.az;
            double u = (sx * px + sy * py + sz * pz) * inv; if (u < -1e-9 || u > 1 + 1e-9) return -1;
            double qx = sy * q.e1z - sz * q.e1y, qy = sz * q.e1x - sx * q.e1z, qz = sx * q.e1y - sy * q.e1x;
            double v = (dx * qx + dy * qy + dz * qz) * inv; if (v < -1e-9 || u + v > 1 + 1e-9) return -1;
            return (q.e2x * qx + q.e2y * qy + q.e2z * qz) * inv;
        }

        /// <summary>Triangle i faces away from a ray going along d: a back face, which culling drops.</summary>
        public bool Back(int i, double dx, double dy, double dz) { ref var q = ref this[i]; return q.nx * dx + q.ny * dy + q.nz * dz > 0; }

        /// <summary>
        /// Some triangle turned towards a ray going along d (not a back face) lies within r of the point: where a back face
        /// is met at its edge, seen aslant past the face it backs (a centimetre off), culling shows a sliver no wider.
        /// </summary>
        public bool FrontNear(double px, double py, double pz, double r, double dx, double dy, double dz)
        {
            if (stamp.Length < tris.Count) stamp = new int[tris.Count];
            ray++;
            var all = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(tris);
            foreach (var p in parts)
            {
                if (p.count == 0 || px < p.x0 - r || px > p.x1 + r || py < p.y0 - r || py > p.y1 + r || pz < p.z0 - r || pz > p.z1 + r) continue;
                int ix0 = C(px - r - p.x0, p.nx, p.cs), ix1 = C(px + r - p.x0, p.nx, p.cs), iy0 = C(py - r - p.y0, p.ny, p.cs), iy1 = C(py + r - p.y0, p.ny, p.cs), iz0 = C(pz - r - p.z0, p.nz, p.cs), iz1 = C(pz + r - p.z0, p.nz, p.cs);
                for (int a = ix0; a <= ix1; a++) for (int b = iy0; b <= iy1; b++) for (int c = iz0; c <= iz1; c++)
                {
                    int id = (a * p.ny + b) * p.nz + c;
                    for (int k = p.start[id]; k < p.start[id + 1]; k++)
                    {
                        int i = p.items[k];
                        if (stamp[i] == ray) continue;
                        stamp[i] = ray;
                        ref var q = ref all[i];
                        if (q.nx * dx + q.ny * dy + q.nz * dz >= 0) continue;
                        if (Dist2(ref q, px, py, pz) <= r * r) return true;
                    }
                }
            }
            return false;
        }

        /// <summary>The squared distance from a point to triangle q (Ericson, Real-Time Collision Detection, 5.1.5).</summary>
        static double Dist2(ref Tri q, double px, double py, double pz)
        {
            double abx = q.e1x, aby = q.e1y, abz = q.e1z, acx = q.e2x, acy = q.e2y, acz = q.e2z;
            double apx = px - q.ax, apy = py - q.ay, apz = pz - q.az;
            double d1 = abx * apx + aby * apy + abz * apz, d2 = acx * apx + acy * apy + acz * apz;
            double cx, cy, cz;
            if (d1 <= 0 && d2 <= 0) { cx = q.ax; cy = q.ay; cz = q.az; }
            else
            {
                double bpx = apx - abx, bpy = apy - aby, bpz = apz - abz;
                double d3 = abx * bpx + aby * bpy + abz * bpz, d4 = acx * bpx + acy * bpy + acz * bpz;
                double cpx = apx - acx, cpy = apy - acy, cpz = apz - acz;
                double d5 = abx * cpx + aby * cpy + abz * cpz, d6 = acx * cpx + acy * cpy + acz * cpz;
                double vc = d1 * d4 - d3 * d2, vb = d5 * d2 - d1 * d6, va = d3 * d6 - d5 * d4;
                if (d3 >= 0 && d4 <= d3) { cx = q.ax + abx; cy = q.ay + aby; cz = q.az + abz; }
                else if (d6 >= 0 && d5 <= d6) { cx = q.ax + acx; cy = q.ay + acy; cz = q.az + acz; }
                else if (vc <= 0 && d1 >= 0 && d3 <= 0) { double v = d1 / (d1 - d3); cx = q.ax + abx * v; cy = q.ay + aby * v; cz = q.az + abz * v; }
                else if (vb <= 0 && d2 >= 0 && d6 <= 0) { double w = d2 / (d2 - d6); cx = q.ax + acx * w; cy = q.ay + acy * w; cz = q.az + acz * w; }
                else if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                {
                    double w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                    cx = q.ax + abx + (acx - abx) * w; cy = q.ay + aby + (acy - aby) * w; cz = q.az + abz + (acz - abz) * w;
                }
                else
                {
                    double den = 1 / (va + vb + vc), v = vb * den, w = vc * den;
                    cx = q.ax + abx * v + acx * w; cy = q.ay + aby * v + acy * w; cz = q.az + abz * v + acz * w;
                }
            }
            double ddx = px - cx, ddy = py - cy, ddz = pz - cz;
            return ddx * ddx + ddy * ddy + ddz * ddz;
        }

        /// <summary>
        /// Eyes round a box, outside every one of <paramref name="solid"/> (2 m clear): <paramref name="near"/> to
        /// <paramref name="far"/> metres out from the box's corner circle, half at street height and half up to
        /// <paramref name="above"/> metres over its top, each aimed at a random point in the box. Seeded, so a test sees
        /// the same rays every run.
        /// </summary>
        public static IEnumerable<(double ex, double ey, double ez, double dx, double dy, double dz)> Eyes(
            (double x0, double y0, double z0, double x1, double y1, double z1) box, IReadOnlyList<(double x0, double y0, double z0, double x1, double y1, double z1)> solid,
            Random rng, int count, double near, double far, double above)
        {
            var (X0, Y0, Z0, X1, Y1, Z1) = box;
            double cx = (X0 + X1) / 2, cz = (Z0 + Z1) / 2, r = Math.Sqrt((X1 - X0) * (X1 - X0) + (Z1 - Z0) * (Z1 - Z0)) / 2;
            for (int k = 0, tries = 0; k < count && tries < count * 20; tries++)
            {
                double ang = rng.NextDouble() * Math.PI * 2, dist = r + near + rng.NextDouble() * (far - near);
                double ex = cx + Math.Cos(ang) * dist, ez = cz + Math.Sin(ang) * dist, ey = rng.NextDouble() < 0.5 ? 1.0 + rng.NextDouble() * 1.5 : rng.NextDouble() * (Y1 + above);
                double tx = X0 + rng.NextDouble() * (X1 - X0), ty = Y0 + rng.NextDouble() * (Y1 - Y0), tz = Z0 + rng.NextDouble() * (Z1 - Z0);
                bool within = false;
                foreach (var q in solid) if (ex > q.x0 - 2 && ex < q.x1 + 2 && ez > q.z0 - 2 && ez < q.z1 + 2 && ey < q.y1 + 2) { within = true; break; }
                if (within) continue;
                double dx = tx - ex, dy = ty - ey, dz = tz - ez, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                k++;
                yield return (ex, ey, ez, dx / l, dy / l, dz / l);
            }
        }
    }
}
