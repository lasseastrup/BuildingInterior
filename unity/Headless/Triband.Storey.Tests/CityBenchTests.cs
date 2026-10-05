using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What the automatic LOD costs on the CPU for the 3,000-building test city (docs/CITY.md §4): the load (every
    /// massing and every cell), a detail build (what the per-frame budget pays for one building), and the LOD pick.
    /// Headless numbers, without the upload; generous bounds, so a slow machine passes and a regression doesn't.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class CityBenchTests
    {
        readonly ITestOutputHelper log;
        public CityBenchTests(ITestOutputHelper log) { this.log = log; }

        static (Site site, List<BuildingData> bs) City(int n)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            TestCity.Generate(d, n);
            return (new Site(d.buildings), d.buildings);
        }

        [Fact]
        public void LoadingTheCityBuildsOnlyMassingsAndCells()
        {
            var (site, bs) = City(3000);
            Lod2.Build(site, bs[0]);   // warm up
            var sw = Stopwatch.StartNew();
            var cells = new Dictionary<(int, int), List<Cells.Member>>(); int row = 0, verts = 0;
            foreach (var b in bs)
            {
                var m = Lod2.Build(site, b); var bb = Site.BoundsOf(b);
                var k = Cells.KeyOf((bb.x0 + bb.x1) / 2, (bb.z0 + bb.z1) / 2);
                if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<Cells.Member>();
                list.Add(new Cells.Member(m, site.IndexOf(b) + 2 * Lod1.LOD_TAG, Enumerable.Range(row, m.Rows.Count).ToArray()));
                row += m.Rows.Count; verts += m.Verts;
            }
            double massings = sw.Elapsed.TotalMilliseconds; sw.Restart();
            int pieces = 0; foreach (var c in cells.Values) pieces += Cells.Merge(c).Count;
            double merge = sw.Elapsed.TotalMilliseconds;
            log.WriteLine($"{row} massing parameter rows for {bs.Count} buildings (the table held 512 before it could grow)");
            Assert.True(row > 512, "the test city needs more rows than the old fixed table");
            log.WriteLine($"{bs.Count} massings {massings:0} ms ({massings / bs.Count:0.00} ms each), {verts:N0} vertices; {cells.Count} cells in {pieces} meshes merged in {merge:0} ms");
            Assert.True(massings / bs.Count < 5, "a massing in under 5 ms");
            Assert.True(merge < 3000, "the cells in under 3 s");
        }

        [Fact]
        public void ADetailBuildFitsTheFrameBudget()
        {
            var (site, bs) = City(3000);
            var sample = bs.Where((b, i) => i % 60 == 0).ToList();
            Lod0.Build(site, sample[0]); Lod1.Build(site, sample[0]);   // warm up
            double t0 = 0, t1 = 0, worst = 0;
            foreach (var b in sample)
            {
                var sw = Stopwatch.StartNew(); Lod0.Build(site, b); double a = sw.Elapsed.TotalMilliseconds; t0 += a; worst = Math.Max(worst, a);
                sw.Restart(); Lod1.Build(site, b); t1 += sw.Elapsed.TotalMilliseconds;
            }
            log.WriteLine($"{sample.Count} buildings: LOD0 {t0 / sample.Count:0.00} ms each (worst {worst:0.0}), LOD1 {t1 / sample.Count:0.00} ms each");
            // on worker threads, as the renderer does: the wall-clock time for the lot
            int threads = Math.Max(1, Environment.ProcessorCount - 1);
            var fresh = new Site(bs); var all = Stopwatch.StartNew();
            System.Threading.Tasks.Parallel.ForEach(sample, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = threads }, b => { Lod0.Build(fresh, b); Lod1.Build(fresh, b); });
            log.WriteLine($"the same on {threads} threads: {all.Elapsed.TotalMilliseconds / sample.Count:0.00} ms a building (LOD0 and LOD1), off the main thread");
            // today LOD0 is several frames' budget and LOD1 about one: the budget lets one through a frame regardless, so
            // a LOD0 build is a hitch (docs/CITY.md §4: building on worker threads is the next step). These bounds catch a regression.
            Assert.True(t1 / sample.Count < 20, "a LOD1 in under 20 ms");
            Assert.True(t0 / sample.Count < 80, "a LOD0 in under 80 ms");
        }

        [Fact]
        public void CheckingTheCityForProblems()
        {
            var (site, bs) = City(3000);
            var sw = Stopwatch.StartNew();
            var prints = Validate.Problems.Prints(new StoreyDocument { buildings = bs });
            double printMs = sw.Elapsed.TotalMilliseconds; sw.Restart();
            var all = Validate.Problems.Check(site, new Play.PlayWorld(site));
            double full = sw.Elapsed.TotalMilliseconds;
            // an edit: the first walk-in building's stairs taken out
            var b = bs.First(x => x.interior && x.shafts.Count > 0); b.shafts.Clear();
            sw.Restart();
            var after = Validate.Problems.Prints(new StoreyDocument { buildings = bs });
            var site2 = new Site(bs);
            var scope = Validate.Problems.Scope(site2, site, Validate.Problems.Changed(prints, after));
            var merged = Validate.Problems.Merge(site2, all, Validate.Problems.Check(site2, new Play.PlayWorld(site2), scope), scope);
            double scoped = sw.Elapsed.TotalMilliseconds;
            log.WriteLine($"problems, {bs.Count} buildings: whole layout {full:0} ms; after an edit {scoped:0} ms ({scope.Count} buildings checked, {printMs:0} ms of it comparing)");
            Assert.True(scoped < full / 4, "an edit's check is a fraction of the whole");
        }

        [Fact]
        public void PickingTheLodsForTheCityIsCheap()
        {
            var (site, bs) = City(3000);
            var m = new LodManager();
            foreach (var b in bs) { var bb = Site.BoundsOf(b); m.Set(site.IndexOf(b), bb.x0, bb.z0, bb.x1, bb.z1, Derived.RoofY(b) + 1); }
            double pxm = LodManager.PixelsPerMetre(1080, 60);
            for (int i = 0; i < 10; i++) m.Update(0, 30, -50 + i, pxm, 1 / 60.0);   // warm up
            var sw = Stopwatch.StartNew(); const int frames = 100;
            for (int i = 0; i < frames; i++)
            {
                var f = m.Update(i * 2, 30, -50, pxm, 1 / 60.0);
                foreach (var (idx, lod, _) in f.Build) m.SetBuilt(idx, lod, true);
            }
            double ms = sw.Elapsed.TotalMilliseconds / frames;
            log.WriteLine($"LOD pick for {bs.Count} buildings: {ms:0.000} ms a frame");
            Assert.True(ms < 4, "the pick in well under a frame");
        }
    }
}
