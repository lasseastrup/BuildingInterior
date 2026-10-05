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

        /// <summary>Where a district's scene is.</summary>
        public enum Load { Unloaded, Loading, Loaded, Unloading }

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
            int loading = 0; foreach (var st in states) if (st == Load.Loading) loading++;
            var want = new List<(int i, double d)>();
            for (int i = 0; i < areas.Count && i < states.Count; i++)
            {
                double d = areas[i].Distance(x, z);
                if (states[i] == Load.Unloaded && d <= loadRadius) want.Add((i, d));
                else if (states[i] == Load.Loaded && d > Math.Max(unloadRadius, loadRadius) && d > 0) unload.Add(i);
            }
            want.Sort((a, b) => a.d.CompareTo(b.d));
            foreach (var (i, _) in want) { if (loading >= maxLoading) break; load.Add(i); loading++; }
            return (load, unload);
        }
    }
}
