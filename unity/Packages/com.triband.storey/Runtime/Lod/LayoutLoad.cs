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
    /// the worker threads a few cells ahead of the one taken (<see cref="AheadPerThread"/>). The renderer takes the cells
    /// all at once, or a few a frame as their massings are ready, so loading a district never holds a frame. Engine-free.
    /// </summary>
    public sealed class LayoutLoad
    {
        /// <summary>
        /// Cells whose massings are built ahead of the one taken, for each worker thread. A cell's massings take about a
        /// millisecond, and a renderer takes the cells that are ready when its frame comes: with one cell a thread it
        /// took about that many a frame and the threads sat idle the rest, so a layout took as many frames to load whatever
        /// the threads' speed (the 3,000-building test city about 130 with two threads: 2.4 s at 60 fps, 4.5 s at 30). With
        /// four, a frame takes as many as its budget allows. A few cells' massings more are held meanwhile, 0.4 MB at most
        /// each on the test city.
        /// </summary>
        public const int AheadPerThread = 4;

        readonly Site site;
        readonly List<BuildingData[]> cells;
        readonly Task<Lod2Mesh[]>?[] made;
        readonly int ahead;
        readonly BuildThreads on;
        int next, started;

        /// <param name="threads">Worker threads (0: every massing built by <see cref="TryTake"/>, on the caller's thread).</param>
        /// <param name="from">A point in the layout (the camera): the cells nearest it first; otherwise in the layout's order.</param>
        /// <param name="on">The threads the massings are built on: <see cref="BuildThreads.Shared"/> unless given.</param>
        public LayoutLoad(Site site, IEnumerable<BuildingData> buildings, int threads, (double x, double z)? from = null, BuildThreads? on = null)
        {
            this.site = site; this.on = on ?? BuildThreads.Shared;
            var groups = buildings.GroupBy(b => { var bb = Site.BoundsOf(b); return Cells.KeyOf((bb.x0 + bb.x1) / 2, (bb.z0 + bb.z1) / 2); });
            if (from is (double fx, double fz))
                groups = groups.OrderBy(g => { double cx = (g.Key.x + 0.5) * Cells.Size - fx, cz = (g.Key.z + 0.5) * Cells.Size - fz; return cx * cx + cz * cz; });
            cells = groups.Select(g => g.ToArray()).ToList();
            made = new Task<Lod2Mesh[]>?[cells.Count];
            ahead = Math.Max(0, threads) * AheadPerThread;
            Buildings = cells.Sum(c => c.Length);
            Start();
        }

        /// <summary>The cells, and how many have been taken.</summary>
        public int Count => cells.Count;
        public int Taken => next;
        public bool Done => next >= cells.Count;
        /// <summary>The cells whose massings are being built, or are ready, past the ones taken.</summary>
        public int InFlight => started - next;
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
                made[started++] = on.Run(() => Massings(site, c));
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
