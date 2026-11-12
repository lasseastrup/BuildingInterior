#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Districts (docs/CITY.md §5): a city split into Storey Sites, one a scene, loaded by distance. Engine-free choices
    /// the Unity side acts on: which loaded district the player is in, and which to load and unload.
    /// </summary>
    public static class Districts
    {
        /// <summary>A district's ground plan in world space, metres.</summary>
        public readonly struct Area
        {
            public readonly double x0, z0, x1, z1;
            public Area(double x0, double z0, double x1, double z1) { this.x0 = x0; this.z0 = z0; this.x1 = x1; this.z1 = z1; }
            /// <summary>How far a point is from the area (0 inside).</summary>
            public double Distance(double x, double z)
            {
                double dx = Math.Max(Math.Max(x0 - x, 0), x - x1), dz = Math.Max(Math.Max(z0 - z, 0), z - z1);
                return Math.Sqrt(dx * dx + dz * dz);
            }

            /// <summary>
            /// How near the segment from (ax, az) to (bx, bz) comes to the area (0 when any of it is inside): whether a site's
            /// buildings can stand between a camera and the player (docs/PLAY.md §7).
            /// </summary>
            public double Distance(double ax, double az, double bx, double bz)
            {
                // the segment clipped to the box (Liang–Barsky): any of it left is inside
                double t0 = 0, t1 = 1, dx = bx - ax, dz = bz - az;
                if (Clip(-dx, ax - x0, ref t0, ref t1) && Clip(dx, x1 - ax, ref t0, ref t1) && Clip(-dz, az - z0, ref t0, ref t1) && Clip(dz, z1 - az, ref t0, ref t1)) return 0;
                // apart: the nearest is an end of the segment to the box, or a corner of the box to the segment
                double d = Math.Min(Distance(ax, az), Distance(bx, bz));
                d = Math.Min(d, Math.Min(ToSegment(x0, z0, ax, az, dx, dz), ToSegment(x1, z0, ax, az, dx, dz)));
                return Math.Min(d, Math.Min(ToSegment(x0, z1, ax, az, dx, dz), ToSegment(x1, z1, ax, az, dx, dz)));
            }

            // one side of the box: the part of the segment within it kept (p t <= q), false when none is
            static bool Clip(double p, double q, ref double t0, ref double t1)
            {
                if (p == 0) return q >= 0;
                double r = q / p;
                if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
                return true;
            }

            static double ToSegment(double x, double z, double ax, double az, double dx, double dz)
            {
                double l = dx * dx + dz * dz, t = l > 0 ? Math.Max(0, Math.Min(1, ((x - ax) * dx + (z - az) * dz) / l)) : 0;
                double ex = ax + dx * t - x, ez = az + dz * t - z;
                return Math.Sqrt(ex * ex + ez * ez);
            }
        }

        /// <summary>
        /// The district the player is in: the current one while they are within <paramref name="margin"/> of it (so a
        /// player on the boundary doesn't flick between two), else the nearest, else -1 when there are none.
        /// </summary>
        public static int PlayerIn(IReadOnlyList<Area> areas, int current, double x, double z, double margin = 4)
        {
            if (current >= 0 && current < areas.Count && areas[current].Distance(x, z) <= margin) return current;
            int best = -1; double bd = double.MaxValue;
            for (int i = 0; i < areas.Count; i++) { double d = areas[i].Distance(x, z); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// <summary>Where a district's scene is. Failed: its load failed (not in the build, no such address), and it is neither loaded nor unloaded again.</summary>
        public enum Load { Unloaded, Loading, Loaded, Unloading, Failed }

        /// <summary>
        /// What to load and unload this frame. A district within <paramref name="loadRadius"/> of the point is wanted;
        /// one loaded stays until it is past <paramref name="unloadRadius"/> (larger: no loading and unloading over and
        /// over at the edge). Loads start nearest first, at most <paramref name="maxLoading"/> at once (a scene loading
        /// costs frames); the district the point is in is never unloaded.
        /// </summary>
        public static (List<int> load, List<int> unload) Plan(IReadOnlyList<Area> areas, IReadOnlyList<Load> states, double x, double z,
            double loadRadius = 300, double unloadRadius = 400, int maxLoading = 1)
        {
            var load = new List<int>(); var unload = new List<int>();
            Plan(areas, states, x, z, load, unload, loadRadius, unloadRadius, maxLoading);
            return (load, unload);
        }

        /// <summary>
        /// <see cref="Plan(IReadOnlyList{Area}, IReadOnlyList{Load}, double, double, double, double, int)"/> into
        /// <paramref name="load"/> and <paramref name="unload"/> (emptied first), making no garbage: for a caller planning
        /// every frame. Of districts as near, the first listed loads first.
        /// </summary>
        public static void Plan(IReadOnlyList<Area> areas, IReadOnlyList<Load> states, double x, double z, List<int> load, List<int> unload,
            double loadRadius = 300, double unloadRadius = 400, int maxLoading = 1)
        {
            load.Clear(); unload.Clear();
            int n = Math.Min(areas.Count, states.Count), loading = 0;
            for (int i = 0; i < states.Count; i++) if (states[i] == Load.Loading) loading++;
            // the nearest wanted, one at a time, as many as may load at once
            while (loading < maxLoading)
            {
                int best = -1; double bd = double.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (states[i] != Load.Unloaded || load.Contains(i)) continue;
                    double d = areas[i].Distance(x, z);
                    if (d <= loadRadius && d < bd) { bd = d; best = i; }
                }
                if (best < 0) break;
                load.Add(best); loading++;
            }
            for (int i = 0; i < n; i++)
            {
                double d = areas[i].Distance(x, z);
                if (states[i] == Load.Loaded && d > Math.Max(unloadRadius, loadRadius) && d > 0) unload.Add(i);
            }
        }
    }
}
