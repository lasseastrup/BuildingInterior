#nullable enable
using System;
using System.Collections.Generic;
using Clipper2Lib;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>One outline of a building: the base (k0 = 0) or a setback, from floor k0 up to k1 (exclusive).</summary>
    public readonly struct Tier
    {
        public readonly int k0, k1;
        public Tier(int k0, int k1) { this.k0 = k0; this.k1 = k1; }
    }

    /// <summary>The prototype's outline bookkeeping and the small geometry the edit operations share.</summary>
    public static class Tiers
    {
        /// <summary>The base and every setback, bottom up.</summary>
        public static List<Tier> Of(BuildingData b)
        {
            var starts = new List<int> { 0 };
            for (int j = 1; j < b.floors.Count; j++) if (b.floors[j].HasShape) starts.Add(j);
            var r = new List<Tier>(starts.Count);
            for (int i = 0; i < starts.Count; i++) r.Add(new Tier(starts[i], i + 1 < starts.Count ? starts[i + 1] : b.floors.Count));
            return r;
        }

        /// <summary>The floor after tier k0's last: the next setback, or the floor count.</summary>
        public static int End(BuildingData b, int k0)
        {
            for (int j = k0 + 1; j < b.floors.Count; j++) if (b.floors[j].HasShape) return j;
            return b.floors.Count;
        }

        /// <summary>Tier k0's outline, edited in place.</summary>
        public static List<Vec2> Outline(BuildingData b, int k0) => k0 > 0 ? b.floors[k0].shape : b.footprint;

        /// <summary>Tier k0's blank walls (edge indices).</summary>
        public static List<int> Blank(BuildingData b, int k0) => k0 > 0 ? b.floors[k0].blank : b.blank;

        public static void SetBlank(BuildingData b, int k0, List<int> v) { if (k0 > 0) b.floors[k0].blank = v; else b.blank = v; }

        /// <summary>A door is anchored to the edges of the tier its storey uses.</summary>
        public static bool DoorOn(BuildingData b, EntranceData e, int k0) => Derived.TierStart(b, e.k) == k0;

        /// <summary>A stair or lift fits every storey it serves.</summary>
        public static bool ShaftFits(BuildingData b, CoreData s)
        {
            for (int k = s.bottom; k <= Derived.ShaftTop(b, s); k++) if (!Cores.CoreFits(Derived.OutlineAt(b, k), s)) return false;
            return true;
        }

        /// <summary>The prototype's <c>cm</c>: editing is free-form, coordinates are kept to the centimetre (JavaScript's rounding, halves up).</summary>
        public static double Cm(double v) => Math.Floor(v * 100 + 0.5) / 100;

        /// <summary>
        /// JavaScript's <c>Math.hypot</c> as V8 computes it (scaled, Kahan-summed), so lengths, and the door positions
        /// and snaps computed from them, agree with the prototype to the last bit.
        /// </summary>
        public static double Hypot(double a, double b)
        {
            a = Math.Abs(a); b = Math.Abs(b);
            double max = Math.Max(a, b);
            if (double.IsInfinity(max)) return double.PositiveInfinity;
            if (max == 0) return 0;
            double sum = 0, compensation = 0;
            foreach (var v in new[] { a, b })
            {
                double n = v / max, summand = n * n - compensation, preliminary = sum + summand;
                compensation = (preliminary - sum) - summand; sum = preliminary;
            }
            return Math.Sqrt(sum) * max;
        }

        public static double EdgeLen(List<Vec2> fp, int i) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; return Hypot(c.x - a.x, c.z - a.z); }

        public static bool SegCross(Vec2 a, Vec2 b, Vec2 c, Vec2 d)
        {
            static double O(Vec2 p, Vec2 q, Vec2 r) => (q.x - p.x) * (r.z - p.z) - (q.z - p.z) * (r.x - p.x);
            const double e = 1e-6;
            double d1 = O(c, d, a), d2 = O(c, d, b), d3 = O(a, b, c), d4 = O(a, b, d);
            return ((d1 > e && d2 < -e) || (d1 < -e && d2 > e)) && ((d3 > e && d4 < -e) || (d3 < -e && d4 > e));
        }

        /// <summary>At least 3 corners, 1 m² of floor, no edge shorter than 0.3 m and no two edges crossing.</summary>
        public static bool SimplePoly(List<Vec2> fp)
        {
            int n = fp.Count;
            if (n < 3 || Math.Abs(Geo.Area2(fp)) < 2) return false;
            for (int i = 0; i < n; i++)
            {
                if (EdgeLen(fp, i) < 0.3) return false;
                for (int j = i + 2; j < n; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    if (SegCross(fp[i], fp[(i + 1) % n], fp[j], fp[(j + 1) % n])) return false;
                }
            }
            return true;
        }

        public const double TerraceMin = 0.8;

        /// <summary>Every corner and edge midpoint of a setback either sits on the outline below or leaves a usable terrace in front of it.</summary>
        public static bool TerraceClear(List<Vec2> inner, List<Vec2> outer)
        {
            for (int i = 0; i < inner.Count; i++)
            {
                var a = inner[i]; var c = inner[(i + 1) % inner.Count];
                foreach (var q in new[] { a, new Vec2((a.x + c.x) / 2, (a.z + c.z) / 2) })
                {
                    if (!Geo.Pip(outer, q.x, q.z)) continue;
                    double d = Geo.DistToEdges(outer, q);
                    if (d > 0.01 && d < TerraceMin) return false;
                }
            }
            return true;
        }

        /// <summary>The area two outlines share.</summary>
        public static double SharedArea(List<Vec2> a, List<Vec2> b)
        {
            var r = Clipper.Intersect(new PathsD { Path(a) }, new PathsD { Path(b) }, FillRule.NonZero, 6);
            return Math.Abs(Clipper.Area(r));
        }

        static PathD Path(List<Vec2> fp) { var p = new PathD(fp.Count); foreach (var v in fp) p.Add(new PointD(v.x, v.z)); return p; }

        public static (double x0, double z0, double x1, double z1) Bbox(List<Vec2> fp)
        {
            double x0 = double.PositiveInfinity, z0 = double.PositiveInfinity, x1 = double.NegativeInfinity, z1 = double.NegativeInfinity;
            foreach (var p in fp) { x0 = Math.Min(x0, p.x); z0 = Math.Min(z0, p.z); x1 = Math.Max(x1, p.x); z1 = Math.Max(z1, p.z); }
            return (x0, z0, x1, z1);
        }

        internal static List<Vec2> Copy(List<Vec2> fp) => new List<Vec2>(fp);

        internal static WallData Copy(WallData w)
        {
            var c = new WallData { a = w.a, b = w.b };
            foreach (var d in w.doors) c.doors.Add(new DoorData { t = d.t });
            return c;
        }

        internal static List<WallData> Copy(List<WallData> ws) { var r = new List<WallData>(ws.Count); foreach (var w in ws) r.Add(Copy(w)); return r; }
    }
}
