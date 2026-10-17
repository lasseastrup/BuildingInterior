using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What a building's first meeting costs the walk (docs/PERFORMANCE.md §10.2): its roof's rise for the occlusion's ray
    /// test, its walk shape, Sink's footprint. Made ahead on build threads, or kept from the massing's build, so the frame
    /// that meets a building doesn't make them. Storey's own.
    /// </summary>
    public class FirstMeetingTests
    {
        readonly ITestOutputHelper output;
        public FirstMeetingTests(ITestOutputHelper o) { output = o; }

        [Fact]
        public void TheMassingKeepsEachRoofsRise()
        {
            var d = new StoreyDocument(); TestCity.Generate(d, 300, walkIn: true);
            d.buildings.AddRange(PrototypeJson.Read(Fixtures.Text("variants.json")).Document.buildings);
            var site = new Site(d.buildings);
            foreach (var b in d.buildings) Lod2.Build(site, b);
            int pitched = 0;
            foreach (var b in d.buildings)
            {
                if (!Roofs.IsPitched(b)) { Assert.Equal(0, Roofs.Rise(site, b)); Assert.False(site.riseMemo.ContainsKey(b.id)); continue; }
                pitched++;
                Assert.True(site.riseMemo.TryGetValue(b.id, out double kept), b.name);
                Assert.Equal(Roofs.Parts(new Site(d.buildings), b)!.Rise, kept, 9);
                Assert.Equal(kept, Roofs.Rise(site, b));
            }
            Assert.True(pitched > 20, $"{pitched} pitched roofs");
        }

        [Fact]
        public void AFootprintMakesLittleGarbage()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; var site = new Site(doc.buildings);
            void All() { foreach (var b in doc.buildings) for (int k = 0; k < b.floors.Count; k++) SinkFootprint.Build(site, b, k).Recycle(); }
            All();   // warm: the shared builders' lists grown once
            int n = doc.buildings.Sum(b => b.floors.Count);
            long a0 = GC.GetAllocatedBytesForCurrentThread(); All(); long per = (GC.GetAllocatedBytesForCurrentThread() - a0) / n;
            output.WriteLine($"{per:N0} bytes a footprint");
            Assert.True(per < 24 * 1024, $"{per:N0} bytes a footprint (83 KB with a fresh builder)");
        }

        [Fact]
        public void ShapesMadeAheadAreThereWhenABuildingIsMet()
        {
            // the five-minute walk, with the walk model asked to have the shapes round the player ready (what the Storey
            // Occlusion does each frame), the build threads given the time a frame leaves them: the walk itself makes almost none
            var d = new StoreyDocument(); TestCity.Generate(d, 1000, walkIn: true); var site = new Site(d.buildings);
            var threads = new BuildThreads(2);
            try
            {
                var world = new PlayWorld(site);
                var core = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Sink });
                var p = new PlayerState(); p.Spawn(d.spawn.x, 0, d.spawn.z); var cam = new FollowCamera { yaw = 0.6, pitch = 0.5, dist = 9 }; cam.Snap(p);
                void Ahead()
                {
                    world.Prepare(p.x, p.z, 24, threads);
                    if (core.View.active != null) world.Prepare(world.Touching(core.View.active), threads);
                    var sw = Stopwatch.StartNew();
                    while (world.Preparing > 0 && sw.ElapsedMilliseconds < 10000) Thread.Sleep(1);
                }
                Ahead();
                for (int f = 0; f < 4500; f++)
                {
                    cam.yaw += 0.004;
                    Walker.Step(world, p, new MoveInput(f % 300 < 150 ? 0.4 : -0.3, 1, true), cam.yaw, 1.0 / 30);
                    cam.Follow(p, 1.0 / 30);
                    core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), 1.0 / 30);
                    Ahead();
                }
                output.WriteLine($"{world.ShapesPrepared} shapes made ahead, {world.ShapesBuilt} when met; {world.ShapeCount} held");
                Assert.True(world.ShapesPrepared > 10);
                Assert.True(world.ShapesBuilt * 10 <= world.ShapesPrepared, $"{world.ShapesBuilt} shapes made when met, {world.ShapesPrepared} ahead");
            }
            finally { threads.Resize(0); }
        }
    }
}
