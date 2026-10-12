using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What is kept of LOD0 once its meshes are uploaded (docs/CITY.md §7): a shape made without meshes is the full
    /// build's to the last bit and a fraction of its cost, and the walk model keeps a bounded number of them, taking
    /// the renderer's when it has them. Storey's own.
    /// </summary>
    public class Lod0ShapeTests
    {
        readonly ITestOutputHelper output;
        public Lod0ShapeTests(ITestOutputHelper o) { output = o; }

        /// <summary>Layouts with every kind of wall a shape holds: facades, party walls, interiors, cores, details, bridges, courtyards, atria.</summary>
        static IEnumerable<(string name, Site site, int every)> Layouts()
        {
            foreach (var c in new[] { "demo", "variants" }) yield return (c, new Site(PrototypeJson.Read(Fixtures.Text(c + ".json")).Document.buildings), 1);
            var city = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(city, 300, true);
            yield return ("walk-in city", new Site(city.buildings), 5);
            var (bd, A, _) = BridgeTests.Street(); BridgeTests.Add(bd, A); BridgeTests.Add(bd, A, 3, open: true);
            yield return ("bridges", new Site(bd.buildings), 1);
            foreach (var kind in new[] { VoidKind.Courtyard, VoidKind.Atrium })
            {
                var (cd, b) = CourtyardTests.Block(5); CourtyardTests.Court(b, kind, kind == VoidKind.Atrium ? 1 : 0);
                yield return (kind.ToString(), new Site(cd.buildings), 1);
            }
        }

        static void Same(Lod0Shape want, Lod0Shape got, string what)
        {
            Assert.True(want.Segs.Count == got.Segs.Count, $"{what}: {got.Segs.Count} storeys of segments, the full build {want.Segs.Count}");
            for (int k = 0; k < want.Segs.Count; k++) Assert.True(want.Segs[k].SequenceEqual(got.Segs[k]), $"{what}: storey {k}'s segments differ");
            Assert.True(want.Extra.SequenceEqual(got.Extra), $"{what}: the bridges' segments differ");
            Assert.True(want.Walls.Count == got.Walls.Count, $"{what}: {got.Walls.Count} walls, the full build {want.Walls.Count}");
            for (int i = 0; i < want.Walls.Count; i++)
                Assert.True(want.Walls[i].K == got.Walls[i].K && want.Walls[i].W.SequenceEqual(got.Walls[i].W) && want.WallBase[i] == got.WallBase[i], $"{what}: wall {i} differs");
        }

        [Fact]
        public void AShapeIsTheFullBuildsToTheLastBit()
        {
            int n = 0, walls = 0, extra = 0;
            foreach (var (name, site, every) in Layouts())
                for (int i = 0; i < site.Buildings.Count; i += every)
                {
                    var b = site.Buildings[i]; var shape = Lod0.BuildShape(site, b);
                    Same(Lod0.Build(site, b).Shape, shape, $"{name}: {b.name}");
                    n++; walls += shape.Walls.Count; extra += shape.Extra.Count;
                }
            output.WriteLine($"{n} buildings, {walls} walls, {extra} bridge segments: the same");
            Assert.True(extra > 0, "no bridge segments: the layouts don't test them");
        }

        [Fact]
        public void AShapeCostsAFractionOfLod0()
        {
            var city = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(city, 300, true);
            var site = new Site(city.buildings); var some = site.Buildings.Where((b, i) => i % 10 == 0).ToList();
            foreach (var b in some.Take(3)) { Lod0.Build(site, b); Lod0.BuildShape(site, b); }   // warm
            long full = 0, shape = 0; var sw = new System.Diagnostics.Stopwatch(); double tFull = 0, tShape = 0;
            foreach (var b in some)
            {
                long a0 = GC.GetAllocatedBytesForCurrentThread(); sw.Restart();
                Lod0.Build(site, b);
                tFull += sw.Elapsed.TotalMilliseconds; long a1 = GC.GetAllocatedBytesForCurrentThread(); sw.Restart();
                Lod0.BuildShape(site, b);
                tShape += sw.Elapsed.TotalMilliseconds; long a2 = GC.GetAllocatedBytesForCurrentThread();
                full += a1 - a0; shape += a2 - a1;
            }
            output.WriteLine($"a walk-in building, {some.Count} of them: LOD0 {tFull / some.Count:0.00} ms and {full / some.Count / 1024.0:N0} KB made; its shape alone {tShape / some.Count:0.00} ms and {shape / some.Count / 1024.0:N0} KB");
            Assert.True(shape * 8 < full, $"a shape made {shape / some.Count} bytes, LOD0 {full / some.Count}");
        }

        /// <summary>The prototype's walk route round the city, as the allocation test walks it: every frame's position.</summary>
        static List<(double x, double y, double z)> Walk(PlayWorld world, StoreyDocument d, int frames, Action? each = null)
        {
            var at = new List<(double, double, double)>(frames);
            var p = new PlayerState(); p.Spawn(d.spawn.x, 0, d.spawn.z);
            var cam = new FollowCamera { yaw = 0.6, pitch = 0.5, dist = 9 }; cam.Snap(p);
            var core = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Sink });
            for (int f = 0; f < frames; f++)
            {
                cam.yaw += 0.004;
                Walker.Step(world, p, new MoveInput(f % 300 < 150 ? 0.4 : -0.3, 1, true), cam.yaw, 1.0 / 30);
                cam.Follow(p, 1.0 / 30);
                core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), 1.0 / 30);
                at.Add((p.x, p.y, p.z));
                each?.Invoke();
            }
            return at;
        }

        [Fact]
        public void TheWalkModelKeepsAFewShapesAndWalksTheSame()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 1000);
            var site = new Site(d.buildings);
            // the walk needs a dozen or so buildings' shapes over five minutes: keeping two, most are let go and made again
            var all = new PlayWorld(site) { Keep = int.MaxValue }; var few = new PlayWorld(site) { Keep = 2 };
            int most = 0;
            var a = Walk(all, d, 9000); var b = Walk(few, d, 9000, () => most = Math.Max(most, few.ShapeCount));
            output.WriteLine($"all kept: {all.ShapeCount} shapes, {all.ShapesBuilt} made; two kept: at most {most}, {few.ShapesBuilt} made");
            Assert.True(most <= 2, $"{most} shapes kept");
            Assert.True(few.ShapesBuilt > all.ShapesBuilt, "nothing was let go and made again: the walk doesn't test it");
            for (int f = 0; f < a.Count; f++) Assert.True(a[f] == b[f], $"frame {f}: {b[f]} with two kept, {a[f]} with all");
        }

        [Fact]
        public void TheWalkModelTakesTheRenderersShapes()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 300);
            var site = new Site(d.buildings);
            // the renderer's: LOD0 built ahead of the player, its shape kept (here, every building's)
            var made = new Dictionary<BuildingData, Lod0Shape>(); int asked = 0;
            var world = new PlayWorld(site) { Source = b => { asked++; return made.TryGetValue(b, out var s) ? s : (made[b] = Lod0.Build(site, b).Shape); } };
            Walk(world, d, 1500);
            output.WriteLine($"{asked} shapes asked of the renderer, {world.ShapesBuilt} made by the walk model");
            Assert.True(asked > 0 && world.ShapesBuilt == 0);
            foreach (var kv in made) Assert.Same(kv.Value, world.ShapeOf(kv.Key));
        }

        [Fact]
        public void AMassingMadeAgainIsTheSame()
        {
            // a cell merged again makes its members' massings again (SiteRenderer): from the same layout, and from the
            // layout read again after an edit elsewhere, they are the ones built first, rows and all
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 300);
            var site = new Site(d.buildings); var again = new Site(d.buildings);
            foreach (var b in d.buildings)
            {
                var m = Lod2.Build(site, b);
                foreach (var o in new[] { Lod2.Build(site, b), Lod2.Build(again, b) })
                {
                    Assert.True(m.P.SequenceEqual(o.P) && m.N.SequenceEqual(o.N) && m.I.SequenceEqual(o.I) && m.Fac.SequenceEqual(o.Fac) && m.Fac2.SequenceEqual(o.Fac2) && m.Slot.SequenceEqual(o.Slot), $"{b.name}: the massing differs");
                    Assert.Equal(m.Rows.Count, o.Rows.Count);
                    for (int r = 0; r < m.Rows.Count; r++) Assert.True(m.Rows[r].GpuTexels(7).SequenceEqual(o.Rows[r].GpuTexels(7)), $"{b.name}: row {r} differs");
                }
            }
        }
    }
}
