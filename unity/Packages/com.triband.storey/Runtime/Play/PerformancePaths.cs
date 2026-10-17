#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;

namespace Triband.Storey.Play
{
    /// <summary>
    /// Where a performance run goes in a layout (docs/PERFORMANCE.md §14): its longest straight street, its tallest building
    /// and a point inside it on each storey. Site space. Storey Performance Run (the play kit) takes the camera and the
    /// player along them. Engine-free; Storey's own.
    /// </summary>
    public static class PerformancePaths
    {
        /// <summary>
        /// The longest straight run along x or along z that no building's box comes within <paramref name="clear"/> of,
        /// looked for every metre across the middle seven tenths of the layout's own buildings (a street between them, not
        /// one along the layout's edge), then across all of it: from (x0, z0) to (x1, z1). Null with no buildings.
        /// </summary>
        public static (double x0, double z0, double x1, double z1)? Street(Site site, double clear = 1.5) =>
            Street(site, clear, 0.15) ?? Street(site, clear, 0);

        static (double x0, double z0, double x1, double z1)? Street(Site site, double clear, double edge)
        {
            int n = site.Own; if (n == 0) return null;
            var boxes = new (double x0, double z0, double x1, double z1)[n];
            double X0 = 1e18, Z0 = 1e18, X1 = -1e18, Z1 = -1e18;
            for (int i = 0; i < n; i++)
            {
                var b = site.Bounds(site.Buildings[i]);
                boxes[i] = (b.x0 - clear, b.z0 - clear, b.x1 + clear, b.z1 + clear);
                X0 = Math.Min(X0, b.x0); Z0 = Math.Min(Z0, b.z0); X1 = Math.Max(X1, b.x1); Z1 = Math.Max(Z1, b.z1);
            }
            (double len, double x0, double z0, double x1, double z1) best = (0, X0, Z0, X0, Z0);
            var cover = new List<(double a, double b)>();
            for (int axis = 0; axis < 2; axis++)
            {
                // a line along x at each z (axis 0), along z at each x (axis 1)
                double lo = axis == 0 ? Z0 : X0, hi = axis == 0 ? Z1 : X1, from = axis == 0 ? X0 : Z0, to = axis == 0 ? X1 : Z1;
                double span = hi - lo; lo += span * edge; hi -= span * edge;
                for (double c = Math.Floor(lo) + 0.5; c < hi; c += 1)
                {
                    cover.Clear();
                    foreach (var q in boxes)
                    {
                        double a0 = axis == 0 ? q.z0 : q.x0, a1 = axis == 0 ? q.z1 : q.x1;
                        if (c < a0 || c > a1) continue;
                        cover.Add(axis == 0 ? (q.x0, q.x1) : (q.z0, q.z1));
                    }
                    cover.Sort((p, q) => p.a.CompareTo(q.a));
                    double at = from;
                    foreach (var (a, b) in cover)
                    {
                        if (a > at && a - at > best.len) best = axis == 0 ? (a - at, at, c, a, c) : (a - at, c, at, c, a);
                        at = Math.Max(at, b);
                    }
                    if (to > at && to - at > best.len) best = axis == 0 ? (to - at, at, c, to, c) : (to - at, c, at, c, to);
                }
            }
            return best.len > 0 ? (best.x0, best.z0, best.x1, best.z1) : ((double, double, double, double)?)null;
        }

        /// <summary>The layout's own building whose roof is highest; null with none.</summary>
        public static BuildingData? Tallest(Site site)
        {
            BuildingData? top = null; double y = double.NegativeInfinity;
            for (int i = 0; i < site.Own; i++)
            {
                var b = site.Buildings[i]; double r = Derived.RoofY(b);
                if (r > y) { y = r; top = b; }
            }
            return top;
        }

        /// <summary>
        /// A point to stand on storey <paramref name="k"/> of <paramref name="b"/> (site space, on the floor): the middle of
        /// its outline if that is inside it, else the point of a grid over it farthest from its edges.
        /// </summary>
        public static (double x, double y, double z) StandOn(BuildingData b, int k)
        {
            var o = Derived.OutlineAt(b, Math.Min(k, b.floors.Count));
            double y = Derived.FloorBase(b, k);
            double cx = 0, cz = 0, a = 0;
            for (int i = 0; i < o.Count; i++)
            {
                var p = o[i]; var q = o[(i + 1) % o.Count]; double c = p.x * q.z - q.x * p.z;
                a += c; cx += (p.x + q.x) * c; cz += (p.z + q.z) * c;
            }
            if (Math.Abs(a) > 1e-9) { cx /= 3 * a; cz /= 3 * a; }
            else { foreach (var p in o) { cx += p.x; cz += p.z; } cx /= Math.Max(1, o.Count); cz /= Math.Max(1, o.Count); }
            if (!Geo.Pip(o, cx, cz))
            {
                double x0 = 1e18, z0 = 1e18, x1 = -1e18, z1 = -1e18;
                foreach (var p in o) { x0 = Math.Min(x0, p.x); z0 = Math.Min(z0, p.z); x1 = Math.Max(x1, p.x); z1 = Math.Max(z1, p.z); }
                double far = -1;
                for (int i = 1; i < 16; i++)
                    for (int j = 1; j < 16; j++)
                    {
                        double x = x0 + (x1 - x0) * i / 16, z = z0 + (z1 - z0) * j / 16;
                        if (!Geo.Pip(o, x, z)) continue;
                        double d = Geo.DistToEdges(o, new Vec2(x, z));
                        if (d > far) { far = d; cx = x; cz = z; }
                    }
            }
            return (cx + b.pos.x, y, cz + b.pos.z);
        }
    }

    /// <summary>A path's frame times, or any per-frame number, and their percentiles (nearest rank). Engine-free.</summary>
    public sealed class FrameSeries
    {
        readonly List<double> values = new List<double>();
        double[]? sorted;

        public int Count => values.Count;
        public void Add(double v) { values.Add(v); sorted = null; }
        public void Clear() { values.Clear(); sorted = null; }

        /// <summary>The value <paramref name="p"/> % of them are at or under (0 with none); 50 the median, 100 the most.</summary>
        public double Percentile(double p)
        {
            if (values.Count == 0) return 0;
            if (sorted == null) { sorted = values.ToArray(); Array.Sort(sorted); }
            int rank = (int)Math.Ceiling(p / 100 * sorted.Length);
            return sorted[Math.Min(sorted.Length - 1, Math.Max(0, rank - 1))];
        }

        public double Max => Percentile(100);
    }
}
