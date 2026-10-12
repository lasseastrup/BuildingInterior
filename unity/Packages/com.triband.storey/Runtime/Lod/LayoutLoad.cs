#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Triband.Storey.Generate;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// A layout's buildings in the order a renderer loads them (docs/CITY.md §2): cell by cell (<see cref="Cells"/>, by
    /// the centre of each building's bounds), the nearest first when a point is given, each cell's massings built on
    /// the worker threads a few cells ahead of the one taken. The renderer takes the cells all at once, or a few a frame
    /// as their massings are ready, so loading a district never holds a frame. Engine-free.
    /// </summary>
    public sealed class LayoutLoad
    {
        readonly Site site;
        readonly List<BuildingData[]> cells;
        readonly Task<Lod2Mesh[]>?[] made;
        readonly int ahead;
        int next, started;

        /// <param name="threads">Worker threads (0: every massing built by <see cref="TryTake"/>, on the caller's thread).</param>
        /// <param name="from">A point in the layout (the camera): the cells nearest it first; otherwise in the layout's order.</param>
        public LayoutLoad(Site site, IEnumerable<BuildingData> buildings, int threads, (double x, double z)? from = null)
        {
            this.site = site;
            var groups = buildings.GroupBy(b => { var bb = Site.BoundsOf(b); return Cells.KeyOf((bb.x0 + bb.x1) / 2, (bb.z0 + bb.z1) / 2); });
            if (from is (double fx, double fz))
                groups = groups.OrderBy(g => { double cx = (g.Key.x + 0.5) * Cells.Size - fx, cz = (g.Key.z + 0.5) * Cells.Size - fz; return cx * cx + cz * cz; });
            cells = groups.Select(g => g.ToArray()).ToList();
            made = new Task<Lod2Mesh[]>?[cells.Count];
            ahead = Math.Max(0, threads);
            Buildings = cells.Sum(c => c.Length);
            Start();
        }

        /// <summary>The cells, and how many have been taken.</summary>
        public int Count => cells.Count;
        public int Taken => next;
        public bool Done => next >= cells.Count;
        /// <summary>The buildings in all the cells.</summary>
        public int Buildings { get; }

        /// <summary>A cell's massings: each on its own, one cell on one worker (cells in flight are the parallelism).</summary>
        static Lod2Mesh[] Massings(Site site, BuildingData[] bs)
        {
            var m = new Lod2Mesh[bs.Length];
            for (int i = 0; i < bs.Length; i++) m[i] = Lod2.Build(site, bs[i]);
            return m;
        }

        void Start()
        {
            while (started < cells.Count && started < next + ahead)
            {
                var c = cells[started];
                made[started++] = Task.Run(() => Massings(site, c));
            }
        }

        /// <summary>
        /// The next cell: its buildings and their massings, in the same order. <paramref name="wait"/> false takes it only
        /// when its massings are ready (false otherwise: try again later); true waits for them. False once every cell is
        /// taken. A worker's exception comes out here, as itself.
        /// </summary>
        public bool TryTake(bool wait, out BuildingData[] buildings, out Lod2Mesh[] massings)
        {
            buildings = Array.Empty<BuildingData>(); massings = Array.Empty<Lod2Mesh>();
            if (Done) return false;
            var t = made[next];
            if (t == null) massings = Massings(site, cells[next]);
            else
            {
                if (!wait && !t.IsCompleted) return false;
                massings = t.GetAwaiter().GetResult();
                made[next] = null;
            }
            buildings = cells[next++];
            Start();
            return true;
        }
    }
}
