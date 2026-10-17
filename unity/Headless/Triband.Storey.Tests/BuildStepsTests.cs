using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// LOD0 built a storey at a time on the build threads (docs/PERFORMANCE.md §9.2): asked after each storey whether to
    /// stop, the build is the same when it goes on; and between storeys a more urgent build runs first.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class BuildStepsTests
    {
        readonly ITestOutputHelper log;
        public BuildStepsTests(ITestOutputHelper log) { this.log = log; }

        static bool Same(MeshBuilder a, MeshBuilder b) =>
            a.Verts == b.Verts && a.P.SequenceEqual(b.P) && a.N.SequenceEqual(b.N) && a.I.SequenceEqual(b.I) && a.C.SequenceEqual(b.C);

        [Fact]
        public void ABuildAskedAfterEachStoreyIsTheSameBuild()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; var site = new Site(d.buildings);
            foreach (var b in d.buildings)
            {
                int asked = 0;
                var stepped = Lod0.Build(site, b, () => { asked++; return false; })!;
                var whole = Lod0.Build(site, b);
                Assert.True(Same(whole.Op, stepped.Op) && Same(whole.Glass, stepped.Glass), $"{b.name}: the stepped build differs");
                Assert.Equal(b.floors.Count + 1, asked);   // each storey and the roof
            }
        }

        [Fact]
        public void AStoppedBuildIsNoBuild()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; var site = new Site(d.buildings);
            var b = d.buildings.OrderByDescending(x => x.floors.Count).First();
            int asked = 0;
            Assert.Null(Lod0.Build(site, b, () => ++asked == 2));
            Assert.Equal(2, asked);
            // and what it gave back builds the next one as before
            Assert.True(Same(Lod0.Build(site, b).Op, Lod0.Build(site, b, () => false)!.Op));
        }

        /// <summary>
        /// With the only build thread on the walk-in test city's tallest tower, a building wanted at once waits for a storey
        /// of it, where it waited for the whole tower.
        /// </summary>
        [Fact]
        public void AForcedBuildWaitsForAStoreyNotATower()
        {
            var d = new StoreyDocument(); TestCity.Generate(d, 1000, walkIn: true); var site = new Site(d.buildings);
            var tower = d.buildings.Where(b => b.interior).OrderByDescending(b => b.floors.Count * Math.Abs(Geo.Area2(b.footprint))).First();
            var small = d.buildings.Where(b => b.interior && b != tower).OrderBy(b => b.floors.Count).First();
            Lod0.Build(site, tower); Lod0.Build(site, small);   // warm up
            double Wait(bool steps)
            {
                var bt = new BuildThreads(1);
                using var started = new ManualResetEventSlim(false);
                try
                {
                    var big = bt.Run(() =>
                    {
                        started.Set();
                        return steps ? Lod0.Build(site, tower, () => { bt.RunMoreUrgent(BuildLane.Detail); return false; }) : Lod0.Build(site, tower);
                    }, BuildLane.Detail);
                    Assert.True(started.Wait(5000));
                    Thread.Sleep(5);
                    var sw = Stopwatch.StartNew();
                    var forced = bt.Run(() => Lod0.Build(site, small), BuildLane.Forced);
                    while (!forced.IsCompleted) Thread.Sleep(0);
                    double waited = sw.Elapsed.TotalMilliseconds;
                    Assert.NotNull(forced.Result);
                    while (!big.IsCompleted) Thread.Sleep(1);
                    Assert.NotNull(big.Result);
                    return waited;
                }
                finally { bt.Resize(0); }
            }
            var plain = Enumerable.Range(0, 3).Select(_ => Wait(false)).Min();
            var stepped = Enumerable.Range(0, 3).Select(_ => Wait(true)).Min();
            var ts = Stopwatch.StartNew(); Lod0.Build(site, tower); double towerMs = ts.Elapsed.TotalMilliseconds;
            log.WriteLine($"{tower.name}: {tower.floors.Count} storeys, its LOD0 {towerMs:0} ms; {small.name}'s forced LOD0 done after {plain:0.0} ms behind it whole, {stepped:0.0} ms in storey steps");
            Assert.True(stepped < plain / 2, $"{stepped:0.0} ms in steps, {plain:0.0} ms whole");
        }
    }
}
