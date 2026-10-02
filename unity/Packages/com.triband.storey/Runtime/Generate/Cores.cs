#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>Stair and lift cores: placement queries shared by the generator and the editor.</summary>
    public static class Cores
    {
        public static (double W, double D) Size(CoreData it) => it.type switch
        {
            CoreType.Stairs => (Dim.STAIR_W, Dim.STAIR_D),
            CoreType.Flight => (Dim.FLIGHT_W, Dim.FLIGHT_D),
            _ => (Dim.LIFT_W, Dim.LIFT_D),
        };

        /// <summary>Either kind of stairs.</summary>
        public static bool IsStairs(CoreData s) => s.type != CoreType.Lift;

        static double Rad(CoreData it) => it.rot * Math.PI / 180;

        /// <summary>The core's frame in world space (origin at its centre, u along its width).</summary>
        public static Frame FrameOf(BuildingData b, CoreData it)
        {
            double a = Rad(it);
            return new Frame(new Vec2(b.pos.x + it.x, b.pos.z + it.z), new Vec2(Math.Cos(a), Math.Sin(a)), new Vec2(-Math.Sin(a), Math.Cos(a)));
        }

        /// <summary>The core's oriented rectangle in building-local coordinates, grown by a margin.</summary>
        public struct Rect { public Vec2 c, u, w; public double hx, hz; }

        public static Rect RectOf(CoreData it, double m = 0, double? x = null, double? z = null, double? rot = null)
        {
            var s = Size(it); double a = (rot ?? it.rot) * Math.PI / 180;
            return new Rect { c = new Vec2(x ?? it.x, z ?? it.z), u = new Vec2(Math.Cos(a), Math.Sin(a)), w = new Vec2(-Math.Sin(a), Math.Cos(a)), hx = s.W / 2 + m, hz = s.D / 2 + m };
        }

        public static bool Contains(CoreData it, double lx, double lz, double m = 0)
        {
            double a = Rad(it), dx = lx - it.x, dz = lz - it.z, qx = dx * Math.Cos(a) + dz * Math.Sin(a), qz = -dx * Math.Sin(a) + dz * Math.Cos(a);
            var s = Size(it);
            return Math.Abs(qx) <= s.W / 2 + m && Math.Abs(qz) <= s.D / 2 + m;
        }

        public static bool ShaftRoof(BuildingData b, CoreData s) => s.roof && Derived.ShaftTop(b, s) == b.floors.Count - 1 && !Roofs.IsPitched(b);

        public static List<int> Levels(BuildingData b, CoreData s)
        {
            var out_ = new List<int>();
            for (int k = s.bottom; k <= Derived.ShaftTop(b, s); k++) out_.Add(k);
            if (ShaftRoof(b, s)) out_.Add(b.floors.Count);
            return out_;
        }

        /// <summary>The slab at level k is open over the stairs: a flight comes up to it from the storey below.</summary>
        public static bool StairHoleAt(BuildingData b, CoreData s, int k) =>
            IsStairs(s) && k > s.bottom && (k <= Derived.ShaftTop(b, s) || (k == b.floors.Count && ShaftRoof(b, s))) && !Derived.Filled(b, k) && !Derived.Filled(b, k - 1);

        /// <summary>
        /// A flight goes up from storey k to the next. Never into or out of a filled storey: stairs stop at one (and the
        /// storeys on either side rail the dead end).
        /// </summary>
        public static bool HasFlight(BuildingData b, CoreData s, int k)
        {
            int T = Derived.ShaftTop(b, s);
            return IsStairs(s) && k >= s.bottom && (k < T || (k == T && ShaftRoof(b, s))) && !Derived.Filled(b, k) && !Derived.Filled(b, k + 1);
        }

        /// <summary>The storeys a core can be used on: its levels but the filled ones (a lift passes those without stopping).</summary>
        public static List<int> Stops(BuildingData b, CoreData s)
        {
            var l = Levels(b, s);
            l.RemoveAll(k => Derived.Filled(b, k));
            return l;
        }

        /// <summary>A side of the core that sits on an outline edge: the edge and the stretch along it.</summary>
        public sealed class Flush { public int i; public double s0, s1; }

        /// <summary>
        /// Which of the core's sides ([−x, +x, front (never), back (never for a flight)]) sit on an outline edge with
        /// their inner face on the line, so the building's wall serves as the core's.
        /// </summary>
        public static Flush?[] CoreFlush(List<Vec2> fp, CoreData it, double? x = null, double? z = null, double? rot = null)
        {
            var R = RectOf(it, 0, x, z, rot);
            double hx = R.hx, hz = R.hz, ex = hx + Dim.CORE_T, ez = hz + Dim.CORE_T;
            Vec2 P2(double a, double c) => new Vec2(R.c.x + R.u.x * a + R.w.x * c, R.c.z + R.u.z * a + R.w.z * c);
            Flush? OnEdge(Vec2 p, Vec2 q)
            {
                for (int i = 0; i < fp.Count; i++)
                {
                    var a = fp[i]; var c = fp[(i + 1) % fp.Count];
                    double L = Geo.Hypot(c.x - a.x, c.z - a.z); if (L == 0) L = 1e-6;
                    double dx = (c.x - a.x) / L, dz = (c.z - a.z) / L;
                    double Off(Vec2 v) => Math.Abs((v.x - a.x) * dz - (v.z - a.z) * dx);
                    double Along(Vec2 v) => (v.x - a.x) * dx + (v.z - a.z) * dz;
                    double s0 = Math.Min(Along(p), Along(q)), s1 = Math.Max(Along(p), Along(q));
                    if (Off(p) < 0.03 && Off(q) < 0.03 && s0 > -Dim.CORE_T - 0.05 && s1 < L + Dim.CORE_T + 0.05)
                        return new Flush { i = i, s0 = Math.Max(0, s0), s1 = Math.Min(L, s1) };
                }
                return null;
            }
            // a flight's landings are at both ends (on at the front, off at the back), so neither may stand on a wall
            return new[] { OnEdge(P2(-hx, -ez), P2(-hx, ez)), OnEdge(P2(hx, -ez), P2(hx, ez)), null, it.type == CoreType.Flight ? null : OnEdge(P2(-ex, hz), P2(ex, hz)) };
        }

        /// <summary>A core fits an outline when its (grown) corners and edge midpoints are inside and no outline corner pokes into it.</summary>
        public static bool CoreFits(List<Vec2> fp, CoreData it, double m = 0.16, double? x = null, double? z = null, double? rot = null)
        {
            var fl = CoreFlush(fp, it, x, z, rot); var R = RectOf(it, 0, x, z, rot);
            double Mu(int a) => (a < 0 ? fl[0] : fl[1]) != null ? -0.01 : m - 0.02;
            double Mw(int c) => (c < 0 ? fl[2] : fl[3]) != null ? -0.01 : m - 0.02;
            foreach (var (a, c) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1), (0, -1), (1, 0), (0, 1), (-1, 0) })
            {
                double qx = R.c.x + R.u.x * a * (R.hx + Mu(a)) + R.w.x * c * (R.hz + Mw(c)), qz = R.c.z + R.u.z * a * (R.hx + Mu(a)) + R.w.z * c * (R.hz + Mw(c));
                if (!Geo.Pip(fp, qx, qz)) return false;
            }
            foreach (var p in fp)
            {
                double dx = p.x - R.c.x, dz = p.z - R.c.z;
                if (Math.Abs(dx * R.u.x + dz * R.u.z) < R.hx && Math.Abs(dx * R.w.x + dz * R.w.z) < R.hz) return false;
            }
            return true;
        }
    }

}
