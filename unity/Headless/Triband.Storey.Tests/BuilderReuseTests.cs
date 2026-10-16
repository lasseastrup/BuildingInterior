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
    /// Detail builds reuse their memory (docs/CITY.md §7): a builder's lists and a packed mesh are made of
    /// <see cref="Chunks"/>, given back when the builder is recycled and the mesh uploaded, and the next build takes them.
    /// The same meshes, a fraction of the garbage, and no more kept than <see cref="Chunks.KeepBytes"/>. Storey's own.
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

        /// <summary>A building's LOD0 and LOD1 as the renderer's worker makes them: built, packed, and the builders recycled or not.</summary>
        static List<PackedMesh> Build(Site site, BuildingData b, bool recycle)
        {
            var o = new List<PackedMesh>();
            var r = Lod0.Build(site, b);
            var rooms = new List<PackedMesh>();
            o.AddRange(MeshPack.Pack(r.Op, true, (0, 10), weld: false, rooms: rooms)); o.AddRange(rooms); o.AddRange(MeshPack.Pack(r.Glass, true, (0, 10), weld: false));
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
            MeshBuilder.ForgetSpares(); Chunks.Clear();
            try
            {
                foreach (var walkIn in new[] { false, true })
                {
                    // every 25th building once: with nothing kept from the build before, then as the renderer's worker
                    // builds one after another (the builders recycled, the meshes given back once compared, as an upload does)
                    var fresh = City(1000, walkIn); var reused = City(1000, walkIn);
                    var ix = Enumerable.Range(0, fresh.Buildings.Count).Where(i => i % 25 == 0).ToList();
                    Build(fresh, fresh.Buildings[1], false); MeshBuilder.ForgetSpares(); Chunks.Clear();   // warm up
                    long a = 0, c = 0; int meshes = 0; var made = new List<List<PackedMesh>>();
                    foreach (int i in ix)
                    {
                        MeshBuilder.ForgetSpares(); Chunks.Clear();
                        long a0 = GC.GetAllocatedBytesForCurrentThread(); made.Add(Build(fresh, fresh.Buildings[i], false)); a += GC.GetAllocatedBytesForCurrentThread() - a0;
                    }
                    MeshBuilder.ForgetSpares(); Chunks.Clear();
                    for (int k = 0; k < ix.Count; k++)
                    {
                        long a0 = GC.GetAllocatedBytesForCurrentThread(); var y = Build(reused, reused.Buildings[ix[k]], true); c += GC.GetAllocatedBytesForCurrentThread() - a0;
                        var x = made[k];
                        Assert.Equal(x.Count, y.Count);
                        for (int m = 0; m < x.Count; m++) Assert.True(Same(x[m], y[m]), $"{fresh.Buildings[ix[k]].name}: mesh {m} differs built with reused lists");
                        foreach (var p in y) p.Release();
                        meshes += x.Count;
                    }
                    log.WriteLine($"{(walkIn ? "walk-in" : "plain")} city: a building's LOD0 and LOD1 allocate {a / 1048576.0 / ix.Count:0.00} MB with nothing kept, {c / 1048576.0 / ix.Count:0.00} MB built one after another ({100 - 100.0 * c / a:0}% less); {meshes} meshes the same");
                    Assert.True(c < a * 0.6, $"reusing the chunks allocates {c} bytes against {a}");
                }
            }
            finally { MeshBuilder.ForgetSpares(); Chunks.Clear(); }
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
                // a builder made after is a recycled one, empty and with nothing of the last build
                var m = MeshBuilder.Make(5);
                Assert.True(m == r.Op || m == r.Glass);
                Assert.Equal(0, m.Verts); Assert.Empty(m.I); Assert.Empty(m.PaneUV); Assert.Empty(m.Walls); Assert.False(m.Welded);
                Assert.Equal(0, m.P.ChunkCount + m.N.ChunkCount + m.C.ChunkCount + m.K.ChunkCount + m.WI.ChunkCount + m.I.ChunkCount);
                // the largest building of the walk-in city gives its chunks back, and no more are kept than the limit
                long keep = Chunks.KeepBytes;
                try
                {
                    var big = site.Buildings.OrderByDescending(b => b.floors.Count * Math.Abs(Geo.Area2(b.footprint))).First();
                    Chunks.Clear(); Chunks.KeepBytes = 1 << 20;
                    var rb = Lod0.Build(site, big); rb.Op.Recycle();
                    Assert.InRange(Chunks.KeptBytes, 1, 1 << 20);
                    Chunks.Clear(); Chunks.KeepBytes = 256L << 20;
                    rb = Lod0.Build(site, big); int held = rb.Op.P.ChunkCount; rb.Op.Recycle();
                    Assert.True(Chunks.KeptBytes >= (long)held * Chunks<P3>.Bytes, $"{Chunks.KeptBytes} bytes kept of {held} position chunks");
                }
                finally { Chunks.KeepBytes = keep; Chunks.Clear(); }
            }
            finally { MeshBuilder.ForgetSpares(); }
        }
    

        static bool SameMassing(Lod2Mesh a, Lod2Mesh b) =>
            a.P.SequenceEqual(b.P) && a.N.SequenceEqual(b.N) && a.I.SequenceEqual(b.I) && a.Fac.SequenceEqual(b.Fac) && a.Fac2.SequenceEqual(b.Fac2)
            && a.Slot.SequenceEqual(b.Slot) && a.Tag == b.Tag && a.Tags == null && b.Tags == null
            && a.Rows.Count == b.Rows.Count && a.Rows.Zip(b.Rows).All(r => r.First.GpuTexels(1).SequenceEqual(r.Second.GpuTexels(1)));

        [Fact]
        public void ReusedMassingsAreTheSameForLessGarbage()
        {
            // a layout load's massings: built on the workers, let go once their cell is packed, and taken again by the next
            var site = City(400, false); var bs = site.Buildings.Where((x, i) => i % 2 == 0).ToList();
            Lod2Mesh.ForgetSpares();
            try
            {
                var fresh = bs.Select(b => Lod2.Build(site, b)).ToList();   // nothing kept: each from nothing, the references
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                var given = bs.Select(b => Lod2.Build(site, b)).ToList();
                long none = GC.GetAllocatedBytesForCurrentThread() - a0;
                // given back, and each built again into one let go (as a load does, a cell's after another's)
                foreach (var m in given) m.Recycle();
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                foreach (var b in bs) Lod2.Build(site, b).Recycle();
                long reused = GC.GetAllocatedBytesForCurrentThread() - a1;
                log.WriteLine($"{bs.Count} massings: {none / 1024} KB built from nothing, {reused / 1024} KB with the massings reused");
                Assert.True(reused < none * 0.6, $"{reused} bytes reused, {none} from nothing");
                // the same massings, each built into one that held another building's (the opposite order)
                for (int i = bs.Count - 1; i >= 0; i--)
                {
                    var again = Lod2.Build(site, bs[i]);
                    Assert.True(SameMassing(fresh[i], again), $"{bs[i].name}: its massing differs built into a reused one");
                    again.Recycle();
                }
            }
            finally { Lod2Mesh.ForgetSpares(); }
        }

        [Fact]
        public void AMassingIsKeptOnceAndNoMoreThanTheSpares()
        {
            Lod2Mesh.ForgetSpares();
            try
            {
                var m = Lod2Mesh.Make(); m.P.Add(new P3(1, 2, 3)); m.Tags = new List<int> { 4 }; m.Tag = 9;
                m.Recycle(); m.Recycle();   // twice: kept once
                var a = Lod2Mesh.Make(); var b = Lod2Mesh.Make();
                Assert.Same(m, a); Assert.NotSame(m, b);
                Assert.Equal(0, a.Verts); Assert.Null(a.Tags); Assert.Equal(0, a.Tag);
                var many = Enumerable.Range(0, 100).Select(_ => new Lod2Mesh()).ToList();
                foreach (var x in many) x.Recycle();
                int kept = 0; while (!many.Contains(Lod2Mesh.Make()) ? false : ++kept < 1000) { }
                Assert.InRange(kept, 1, 99);   // a cap, below the hundred let go
            }
            finally { Lod2Mesh.ForgetSpares(); }
        }
    }
}
