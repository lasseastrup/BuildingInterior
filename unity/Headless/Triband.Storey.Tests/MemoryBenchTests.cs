using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
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
    /// What Storey keeps in memory and what building costs, on the 3,000-building test city (docs/CITY.md §7): what a
    /// building keeps once its meshes are uploaded, what a finished build holds while it waits for the upload, what
    /// building allocates, a layout load's massings, and the walk model on a five-minute walk. Headless (.NET's heap,
    /// not Unity's), with bounds a few times the measured numbers: they catch a regression (the generator's result
    /// kept again is a hundred times a shape), not noise.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class MemoryBenchTests
    {
        readonly ITestOutputHelper log;
        public MemoryBenchTests(ITestOutputHelper log) { this.log = log; }

        static readonly Lazy<(StoreyDocument d, Site site)> City = new(() =>
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 3000);
            return (d, new Site(d.buildings));
        });

        static long Heap() { for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); } return GC.GetTotalMemory(true); }
        static double KB(double b) => b / 1024;
        static double MB(double b) => b / 1048576;
        static long Gpu(MeshBuilder m) => m.Tris == 0 ? 0 : 32L * m.Verts + (m.Verts > MeshPack.MaxVerts ? 4L : 2L) * m.I.Count;

        // each measurement in its own method: a local of the measuring one would keep a result alive through the collection
        [MethodImpl(MethodImplOptions.NoInlining)]
        static List<Lod0Shape> Shapes(Site site, IEnumerable<BuildingData> bs) => bs.Select(b => { var r = Lod0.Build(site, b); r.Op.Weld(); r.Glass.Weld(); return r.Shape; }).ToList();

        [Fact]
        public void ABuildingAtLod0KeepsOnlyItsShape()
        {
            var (_, site) = City.Value;
            (BuildingData b, long g) Sized(BuildingData b) { var r = Lod0.Build(site, b); r.Op.Weld(); r.Glass.Weld(); return (b, Gpu(r.Op) + Gpu(r.Glass)); }
            // the largest: the 16 biggest LOD0s of the 48 buildings with the most floor area; and every 12th building (enough of
            // them that what they keep is well above the heap count's noise of a few hundred kilobytes)
            var largest = site.Buildings.OrderByDescending(b => b.floors.Count * Math.Abs(Geo.Area2(b.footprint))).Take(48).Select(Sized).OrderByDescending(x => x.g).Take(16).ToList();
            var spread = site.Buildings.Where((x, i) => i % 12 == 6).Select(Sized).ToList();
            foreach (var (what, bs) in new[] { ("the 16 largest", largest), ($"a spread of {spread.Count}", spread) })
            {
                long a = Heap(); var kept = Shapes(site, bs.Select(x => x.b)); long kb = Heap() - a; GC.KeepAlive(kept);
                double meshes = bs.Sum(x => (double)x.g);
                log.WriteLine($"{what}: shapes {KB(kb) / bs.Count:0.0} KB a building, {100 * kb / meshes:0.0}% of their GPU meshes ({MB(meshes) / bs.Count:0.00} MB a building)");
                Assert.True(kb < meshes * 0.05, $"{what}: {kb} bytes kept for {meshes} of meshes");
            }
        }

        [Fact]
        public void AFinishedBuildWaitsAtTheSizeOfItsMeshes()
        {
            var (_, site) = City.Value; long packed = 0, gpu = 0, builder = 0; int n = 0;
            foreach (var b in site.Buildings.Where((x, i) => i % 30 == 0))
            {
                n++;
                var r = Lod0.Build(site, b);
                foreach (var m in new[] { r.Op, r.Glass })
                {
                    m.Weld(); gpu += Gpu(m);
                    builder += m.Verts * (24L + 24 + 24 + 8) + 4L * m.I.Count;   // P, N, C, K and WI, I: what the welded builder held
                    foreach (var p in MeshPack.Pack(m, true)) packed += 24L * p.Verts + (p.Cut != null ? 8L * p.Verts : 0) + 2L * p.Indices;
                }
            }
            log.WriteLine($"a finished LOD0 waiting for its upload: {MB(packed) / n:0.00} MB packed, {MB(builder) / n:0.00} MB as the welded builder held it; its GPU meshes {MB(gpu) / n:0.00} MB");
            Assert.True(packed <= gpu, "packed bigger than its meshes");
            Assert.True(packed * 2 < builder);
        }

        static long Make(Site site, BuildingData b)
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var r = Lod0.Build(site, b); MeshPack.Pack(r.Op, true, (0, 10)); MeshPack.Pack(r.Glass, true, (0, 10));
            MeshPack.Pack(Lod1.Build(site, b), true);
            return GC.GetAllocatedBytesForCurrentThread() - a0;
        }

        [Fact]
        public void BuildingAgainAllocatesLess()
        {
            var (d, _) = City.Value; var site = new Site(d.buildings);   // its own: no building built yet
            var bs = site.Buildings.Where((x, i) => i % 15 == 0).ToList();
            Make(site, bs[0]);
            long first = 0, again = 0;
            foreach (var b in bs.Skip(1)) first += Make(site, b);
            foreach (var b in bs.Skip(1)) again += Make(site, b);
            double f = MB(first) / (bs.Count - 1), g = MB(again) / (bs.Count - 1);
            log.WriteLine($"LOD0 and LOD1 built, welded and packed: {f:0.00} MB allocated the first time, {g:0.00} MB built again");
            Assert.True(f < 16, $"{f:0.0} MB allocated building a LOD0 and LOD1");
            Assert.True(g < f * 0.75, $"{g:0.0} MB built again, {f:0.0} the first time");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static List<Lod2Mesh> Massings(Site site, IEnumerable<BuildingData> bs) => bs.Select(b => Lod2.Build(site, b)).ToList();

        [Fact]
        public void ALoadHoldsOneCellsMassingsAtATime()
        {
            var (_, site) = City.Value;
            var cells = site.Buildings.GroupBy(b => { var bb = Site.BoundsOf(b); return Cells.KeyOf((bb.x0 + bb.x1) / 2, (bb.z0 + bb.z1) / 2); }).OrderByDescending(g => g.Count()).ToList();
            long a = Heap(); var big = Massings(site, cells[0]); long held = Heap() - a;
            long b0 = Heap(); var all = Massings(site, site.Buildings); long every = Heap() - b0; GC.KeepAlive(big); GC.KeepAlive(all);
            var sw = Stopwatch.StartNew(); Massings(site, cells[0]); double again = sw.Elapsed.TotalMilliseconds;
            log.WriteLine($"{cells.Count} cells: the biggest's {cells[0].Count()} massings {KB(held):0} KB (every building's: {MB(every):0.0} MB), made again in {again:0.0} ms");
            Assert.True(held * 50 < every);
            Assert.True(again < 50, $"{again:0} ms to make a cell's massings again");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static (PlayWorld world, OcclusionCore core, double worst, int slow) Walk(StoreyDocument d, Site site, int frames)
        {
            var world = new PlayWorld(site);
            var core = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Sink });
            var p = new PlayerState(); p.Spawn(d.spawn.x, 0, d.spawn.z); var cam = new FollowCamera { yaw = 0.6, pitch = 0.5, dist = 9 }; cam.Snap(p);
            var sw = new Stopwatch(); double worst = 0; int slow = 0;
            for (int f = 0; f < frames; f++)
            {
                cam.yaw += 0.004;
                sw.Restart();
                Walker.Step(world, p, new MoveInput(f % 300 < 150 ? 0.4 : -0.3, 1, true), cam.yaw, 1.0 / 30);
                cam.Follow(p, 1.0 / 30);
                core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), 1.0 / 30);
                double ms = sw.Elapsed.TotalMilliseconds;
                if (f >= 30) { worst = Math.Max(worst, ms); if (ms > 16.7) slow++; }   // past the first second: the code's first run is the JIT's
            }
            return (world, core, worst, slow);
        }

        [Fact]
        public void TheWalkModelStaysSmallOnAFiveMinuteWalk()
        {
            var (d, _) = City.Value; var site = new Site(d.buildings);
            long a = Heap(); var (world, core, worst, slow) = Walk(d, site, 9000); long kept = Heap() - a;
            log.WriteLine($"five minutes at 30 fps: the walk model holds {world.ShapeCount} shapes ({world.ShapesBuilt} made), {MB(kept):0.00} MB with the occlusion's state; worst frame {worst:0.0} ms, {slow} over 16.7 ms");
            Assert.True(world.ShapeCount <= world.Keep);
            Assert.True(kept < 16 * 1048576, $"{MB(kept):0.0} MB kept by the walk model");
            Assert.True(worst < 25, $"a {worst:0} ms frame");
            GC.KeepAlive(core);
        }
    }
}
