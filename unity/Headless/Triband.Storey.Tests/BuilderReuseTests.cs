using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A worker thread reuses its builders' lists from one detail build to the next (<see cref="MeshBuilder.Recycle"/>,
    /// docs/CITY.md §7): the same meshes, a fraction of the garbage, and no more kept than
    /// <see cref="MeshBuilder.KeepBytes"/>. Storey's own.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class BuilderReuseTests
    {
        readonly ITestOutputHelper log;
        public BuilderReuseTests(ITestOutputHelper log) { this.log = log; }

        static Site City(int n, bool walkIn)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, n, walkIn);
            return new Site(d.buildings);
        }

        /// <summary>A building's LOD0 and LOD1 as the renderer's worker makes them: built, packed, and the builders let go or not.</summary>
        static List<PackedMesh> Build(Site site, BuildingData b, bool recycle)
        {
            var o = new List<PackedMesh>();
            var r = Lod0.Build(site, b);
            o.AddRange(MeshPack.Pack(r.Op, true, (0, 10), weld: false)); o.AddRange(MeshPack.Pack(r.Glass, true, (0, 10), weld: false));
            if (recycle) { r.Op.Recycle(); r.Glass.Recycle(); }
            var s = Lod1.Build(site, b);
            o.AddRange(MeshPack.Pack(s, true));
            if (recycle) s.Recycle();
            return o;
        }

        static bool Same(PackedMesh a, PackedMesh b) =>
            a.Verts == b.Verts && a.Indices == b.Indices && a.V.Take(a.Verts).SequenceEqual(b.V.Take(b.Verts))
            && (a.Cut == null ? b.Cut == null : b.Cut != null && a.Cut.Take(a.Verts).SequenceEqual(b.Cut.Take(b.Verts)))
            && (a.I16 == null ? b.I16 == null : b.I16 != null && a.I16.Take(a.Indices).SequenceEqual(b.I16.Take(b.Indices)))
            && (a.I32 == null ? b.I32 == null : b.I32 != null && a.I32.Take(a.Indices).SequenceEqual(b.I32.Take(b.Indices)))
            && a.Styles.SequenceEqual(b.Styles);

        [Fact]
        public void ReusedBuildersMakeTheSameMeshesForLessGarbage()
        {
            MeshBuilder.ForgetSpares();
            try
            {
                foreach (var walkIn in new[] { false, true })
                {
                    // each way on its own site (what each building's last build came to is kept per site), every 25th
                    // building once: new builders for each, then reusing the last one's
                    var fresh = City(1000, walkIn); var reused = City(1000, walkIn);
                    var ix = Enumerable.Range(0, fresh.Buildings.Count).Where(i => i % 25 == 0).ToList();
                    Build(fresh, fresh.Buildings[1], false); MeshBuilder.ForgetSpares();   // warm up
                    long a = 0, c = 0; int meshes = 0; var made = new List<List<PackedMesh>>();
                    foreach (int i in ix)
                    {
                        MeshBuilder.ForgetSpares();
                        long a0 = GC.GetAllocatedBytesForCurrentThread(); made.Add(Build(fresh, fresh.Buildings[i], false)); a += GC.GetAllocatedBytesForCurrentThread() - a0;
                    }
                    MeshBuilder.ForgetSpares();
                    for (int k = 0; k < ix.Count; k++)
                    {
                        long a0 = GC.GetAllocatedBytesForCurrentThread(); var y = Build(reused, reused.Buildings[ix[k]], true); c += GC.GetAllocatedBytesForCurrentThread() - a0;
                        var x = made[k];
                        Assert.Equal(x.Count, y.Count);
                        for (int m = 0; m < x.Count; m++) Assert.True(Same(x[m], y[m]), $"{fresh.Buildings[ix[k]].name}: mesh {m} differs built with reused lists");
                        meshes += x.Count;
                    }
                    log.WriteLine($"{(walkIn ? "walk-in" : "plain")} city: a building's LOD0 and LOD1 allocate {a / 1048576.0 / ix.Count:0.00} MB with new builders, {c / 1048576.0 / ix.Count:0.00} MB reusing them ({100 - 100.0 * c / a:0}% less); {meshes} meshes the same");
                    Assert.True(c < a * 0.8, $"reusing the builders allocates {c} bytes against {a}");
                }
            }
            finally { MeshBuilder.ForgetSpares(); }
        }

        [Fact]
        public void ARecycledBuildersShapeStaysAndWhatItKeepsIsBounded()
        {
            MeshBuilder.ForgetSpares();
            try
            {
                var site = City(1000, true);
                var r = Lod0.Build(site, site.Buildings[30]);
                var walls = r.Shape.Walls.Select(w => (w.W.ToArray(), w.K)).ToList(); var bases = r.Shape.WallBase.ToList();
                r.Op.Recycle(); r.Glass.Recycle();
                // the shape holds the wall lists the builders had: replaced in the builders, not emptied
                Assert.Equal(walls.Count, r.Shape.Walls.Count);
                Assert.True(walls.Select(w => w.Item1).Zip(r.Shape.Walls.Select(w => w.W), (p, q) => p.SequenceEqual(q)).All(t => t));
                Assert.Equal(bases, r.Shape.WallBase);
                // a builder made after takes the recycled lists, empty and with nothing of the last build
                var m = MeshBuilder.Make(5, vertices: 10, indices: 30);
                Assert.True(m == r.Op || m == r.Glass);
                Assert.Equal(0, m.Verts); Assert.Empty(m.I); Assert.Empty(m.PaneUV); Assert.Empty(m.Walls); Assert.False(m.Welded);
                // the largest building of the walk-in city is let go, not kept, past the limit
                long keep = MeshBuilder.KeepBytes;
                try
                {
                    MeshBuilder.KeepBytes = 1 << 20;
                    var big = site.Buildings.OrderByDescending(b => b.floors.Count * Math.Abs(Geo.Area2(b.footprint))).First();
                    var rb = Lod0.Build(site, big); rb.Op.Recycle();
                    var again = MeshBuilder.Make(1, vertices: 1);
                    Assert.NotSame(rb.Op, again);
                }
                finally { MeshBuilder.KeepBytes = keep; }
            }
            finally { MeshBuilder.ForgetSpares(); }
        }
    }
}
