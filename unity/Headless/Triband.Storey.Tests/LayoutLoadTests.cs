using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A layout loaded cell by cell (<see cref="LayoutLoad"/>, docs/CITY.md §2): every building once, cells nearest first,
    /// massings built on the workers ahead of the cell taken, the same as built one by one. Storey's own.
    /// </summary>
    public class LayoutLoadTests
    {
        static (StoreyDocument d, Site site) City()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 1000);
            return (d, new Site(d.buildings));
        }

        static bool Same(Lod2Mesh a, Lod2Mesh b) =>
            a.P.SequenceEqual(b.P) && a.N.SequenceEqual(b.N) && a.I.SequenceEqual(b.I) && a.Fac.SequenceEqual(b.Fac) && a.Fac2.SequenceEqual(b.Fac2) && a.Slot.SequenceEqual(b.Slot) && a.Rows.Count == b.Rows.Count;

        [Theory]
        [InlineData(0, true)]
        [InlineData(3, true)]
        [InlineData(3, false)]
        public void EveryBuildingComesOnceWithItsMassing(int threads, bool wait)
        {
            var (d, site) = City(); var alone = new Site(d.buildings);
            var load = new LayoutLoad(site, d.buildings, threads, (40, -20));
            var seen = new HashSet<string>(); var cells = new List<(int x, int z)>(); int spins = 0;
            while (!load.Done)
            {
                if (!load.TryTake(wait, out var bs, out var made)) { Assert.False(wait); Thread.Sleep(1); spins++; continue; }
                Assert.Equal(bs.Length, made.Length);
                for (int i = 0; i < bs.Length; i++)
                {
                    Assert.True(seen.Add(bs[i].id), $"{bs[i].name} came twice");
                    Assert.True(Same(made[i], Lod2.Build(alone, bs[i])), $"{bs[i].name}: its massing differs");
                }
                var bb = Site.BoundsOf(bs[0]); cells.Add(Cells.KeyOf((bb.x0 + bb.x1) / 2, (bb.z0 + bb.z1) / 2));
            }
            Assert.Equal(d.buildings.Count, seen.Count);
            Assert.Equal(load.Count, load.Taken);
            Assert.False(load.TryTake(true, out _, out _));
            // nearest first: each cell's centre no nearer (40, -20) than the one before
            double Dist((int x, int z) c) { double cx = (c.x + 0.5) * Cells.Size - 40, cz = (c.z + 0.5) * Cells.Size + 20; return cx * cx + cz * cz; }
            for (int i = 1; i < cells.Count; i++) Assert.True(Dist(cells[i]) >= Dist(cells[i - 1]));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void CellsAreBuiltAFewAheadOnEachThread(int threads)
        {
            // one cell a thread left the threads idle most of a load (the renderer takes the cells ready when its frame comes)
            var (d, site) = City();
            var load = new LayoutLoad(site, d.buildings, threads, (40, -20));
            int ahead = Math.Min(threads * LayoutLoad.AheadPerThread, load.Count);
            Assert.Equal(ahead, load.InFlight);
            Assert.True(load.TryTake(true, out _, out _));
            Assert.Equal(Math.Min(ahead, load.Count - 1), load.InFlight);
            while (load.TryTake(true, out _, out _)) { }
            Assert.Equal(0, load.InFlight);
        }

        [Fact]
        public void WithoutAPointTheCellsComeInTheLayoutsOrder()
        {
            var (d, site) = City();
            var load = new LayoutLoad(site, d.buildings, 0);
            var order = new List<BuildingData>();
            while (load.TryTake(true, out var bs, out _)) order.AddRange(bs);
            // each cell's buildings in the layout's order, and the cells in the order their first building comes
            var firsts = order.GroupBy(b => { var bb = Site.BoundsOf(b); return Cells.KeyOf((bb.x0 + bb.x1) / 2, (bb.z0 + bb.z1) / 2); }).Select(g => d.buildings.IndexOf(g.First())).ToList();
            Assert.Equal(firsts.OrderBy(i => i), firsts);
        }
    }
}
