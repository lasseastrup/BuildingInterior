using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Detail meshes packed on the worker that built them (<see cref="MeshPack"/>, docs/CITY.md §7): byte for byte what
    /// the upload made on the main thread before, and a mesh past 65,535 vertices in parts with 16-bit indices that
    /// draw the same triangles. Storey's own.
    /// </summary>
    public class MeshPackTests
    {
        readonly ITestOutputHelper output;
        public MeshPackTests(ITestOutputHelper o) { output = o; }

        static int RowOf(StyleRef s) => 1 + s.building * 5 + s.tier;   // any colour rows: each style its own

        /// <summary>What <c>MeshUpload.Upload</c> wrote on the main thread before packing moved to the workers, vertex by vertex.</summary>
        static (PackedMesh.Vertex[] v, PackedMesh.Cutaway[]? c) Reference(MeshBuilder gb, bool windows, (int, int)? mates, int wallBase)
        {
            static sbyte S8(double v) => (sbyte)Math.Round(Math.Max(-1, Math.Min(1, v)) * 127);
            int nv = gb.Verts; var verts = new PackedMesh.Vertex[nv];
            for (int i = 0; i < nv; i++)
            {
                var p = gb.P[i]; var n = gb.N[i]; var s = gb.C[i];
                var (cr, cg, cb, ca) = ColorRows.EncodeVertex(RowOf(s.style), s.slot, s.tone);
                verts[i] = new PackedMesh.Vertex { x = (float)p.x, y = (float)p.y, z = (float)p.z, nx = S8(n.x), ny = S8(n.y), nz = S8(n.z), r = cr, g = cg, b = cb, a = ca, tag = gb.Tag };
                if (windows && s.slot == ColorSlot.Glass && gb.PaneUV.TryGetValue(i, out var uv)) { var (pa, pw, pb, py) = ColorRows.EncodePane(uv.u, uv.v, uv.id, uv.flat, uv.bar); verts[i].a = pa; verts[i].nw = pw; verts[i].b = pb; if (uv.flat) verts[i].ny = py; }
            }
            if (gb.Lean) return (verts, null);
            var cut = new PackedMesh.Cutaway[nv];
            for (int i = 0; i < nv; i++)
            {
                int wi = gb.WI[i];
                int kind = mates is (int ms, int mc) ? MateIndex.Shift(gb.K[i], ms, mc) : gb.K[i];
                cut[i] = new PackedMesh.Cutaway { kind = kind, wid = wi > 0 && wallBase >= 0 ? wallBase + wi - 1 : 0 };
            }
            return (verts, cut);
        }

        static IEnumerable<(string what, MeshBuilder m, bool windows)> Meshes(Site site, BuildingData b)
        {
            var l0 = Lod0.Build(site, b); var l1 = Lod1.Build(site, b);
            yield return ($"{b.name} LOD0", l0.Op, true);
            yield return ($"{b.name} glass", l0.Glass, true);
            yield return ($"{b.name} LOD1", l1, true);
        }

        static Site City(int n, bool walkIn)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, n, walkIn);
            return new Site(d.buildings);
        }

        [Fact]
        public void APackedMeshIsWhatTheUploadMadeBefore()
        {
            var mates = (100, 400); const int wallBase = 3000; int meshes = 0; long verts = 0;
            foreach (var site in new[] { new Site(PrototypeJson.Read(Fixtures.Text("demo.json")).Document.buildings), City(300, true) })
                foreach (var b in site.Buildings.Where((x, i) => i % 6 == 0))
                    foreach (var (what, m, windows) in Meshes(site, b))
                    {
                        m.Weld();
                        var parts = MeshPack.Pack(m, windows, mates, int.MaxValue);
                        if (m.Tris == 0) { Assert.Empty(parts); continue; }
                        var pm = Assert.Single(parts);
                        pm.Finish(pm.Styles.Select(RowOf).ToArray(), wallBase);
                        var (rv, rc) = Reference(m, windows, mates, wallBase);
                        Assert.True(rv.SequenceEqual(pm.V.Take(pm.Verts)), $"{what}: the vertices differ");
                        Assert.True(rc == null ? pm.Cut == null : rc.SequenceEqual(pm.Cut!.Take(pm.Verts)), $"{what}: the cutaway stream differs");
                        var ix = pm.I16 != null ? pm.I16.Take(pm.Indices).Select(i => (int)i).ToArray() : pm.I32!.Take(pm.Indices).ToArray();
                        Assert.True(ix.SequenceEqual(m.I), $"{what}: the indices differ");
                        Assert.Equal(m.Verts > MeshPack.MaxVerts, pm.I32 != null);
                        Assert.Equal((m.P.Min(p => (float)p.x), m.P.Max(p => (float)p.y)), (pm.X0, pm.Y1));
                        meshes++; verts += m.Verts;
                    }
            output.WriteLine($"{meshes} meshes, {verts:N0} vertices: the same bytes");
        }

        [Fact]
        public void AMeshPastSixteenBitsComesInPartsDrawingTheSameTriangles()
        {
            var site = City(1000, true);
            // the walk-in city's largest LOD0s are past 65,535 vertices; a small part size splits an ordinary one too
            var big = site.Buildings.Select(b => (b, l0: Lod0.Build(site, b).Op)).OrderByDescending(x => x.l0.Verts).First();
            big.l0.Weld();
            Assert.True(big.l0.Verts > MeshPack.MaxVerts, $"the largest LOD0 has {big.l0.Verts} vertices");
            var small = Lod0.Build(site, site.Buildings[40]).Op; small.Weld();
            foreach (var (m, max) in new[] { (big.l0, MeshPack.MaxVerts), (small, 997) })
            {
                var whole = MeshPack.Pack(m, true, null, int.MaxValue).Single();
                whole.Finish(whole.Styles.Select(RowOf).ToArray(), 0);
                var parts = MeshPack.Pack(m, true, null, max);
                int extra = parts.Sum(p => p.Verts) - m.Verts;
                output.WriteLine($"{m.Verts:N0} vertices, {m.Tris:N0} triangles: {parts.Count} parts of at most {max:N0}, {extra:N0} vertices copied into a second part");
                Assert.True(parts.Count > 1);
                var tris = new List<PackedMesh.Vertex>();
                foreach (var p in parts)
                {
                    Assert.True(p.Verts <= max && p.I16 != null);
                    p.Finish(p.Styles.Select(RowOf).ToArray(), 0);
                    tris.AddRange(p.I16!.Take(p.Indices).Select(i => p.V[i]));
                    Assert.True(p.V.Take(p.Verts).All(v => v.x >= p.X0 && v.x <= p.X1 && v.z >= p.Z0 && v.z <= p.Z1));
                }
                Assert.True(tris.SequenceEqual(whole.I32 != null ? whole.I32.Take(whole.Indices).Select(i => whole.V[i]) : whole.I16!.Take(whole.Indices).Select(i => whole.V[i])), "the parts draw other triangles");
                Assert.True(extra < m.Verts / 20, $"{extra} vertices copied");
            }
        }

        [Fact]
        public void AMeshFinishedOnABuildThreadIsTheSameBytes()
        {
            // the renderer stages a worker's build (docs/PERFORMANCE.md §3.2, §4.1): the colour rows and walls found on the
            // main thread, the vertices written on a build thread, and the upload finishing nothing again
            var site = City(200, true); var threads = new BuildThreads(2); int parts = 0;
            try
            {
                foreach (var b in site.Buildings.Where((x, i) => i % 25 == 0))
                    foreach (var (what, m, windows) in Meshes(site, b))
                    {
                        m.Weld();
                        var here = MeshPack.Pack(m, windows, (100, 400), 20000); var there = MeshPack.Pack(m, windows, (100, 400), 20000);
                        var rows = there.Select(p => p.Styles.Select(RowOf).ToArray()).ToArray();
                        Assert.All(there, p => Assert.False(p.Finished));
#pragma warning disable xUnit1031   // finished on a build thread, as the renderer does, and waited for here
                        threads.Run(() => { for (int k = 0; k < there.Count; k++) there[k].Finish(rows[k], 3000); return there.Count; }, BuildLane.Detail).Wait();
#pragma warning restore xUnit1031
                        for (int k = 0; k < here.Count; k++)
                        {
                            here[k].Finish(here[k].Styles.Select(RowOf).ToArray(), 3000);
                            Assert.True(there[k].Finished);
                            Assert.True(here[k].V.Take(here[k].Verts).SequenceEqual(there[k].V.Take(there[k].Verts)), $"{what} ({k + 1}): the vertices differ");
                            Assert.True(here[k].Cut == null ? there[k].Cut == null : here[k].Cut!.Take(here[k].Verts).SequenceEqual(there[k].Cut!.Take(there[k].Verts)), $"{what} ({k + 1}): the cutaway stream differs");
                            Assert.Throws<InvalidOperationException>(() => there[k].Finish(rows[k], 3000));
                            parts++;
                        }
                        foreach (var p in here.Concat(there)) p.Release();
                    }
            }
            finally { threads.Resize(0); }
            output.WriteLine($"{parts} parts finished on a build thread: the same bytes");
        }

        [Fact]
        public void HalfIsTheGpusHalf()
        {
            // bit for bit .NET's own conversion (round to nearest, ties to even): edges, every exponent, ties, and a sweep
            var values = new List<float> { 0f, -0f, 1f, -1f, 0.5f, 65504f, 65519.99f, 65520f, 1e6f, -1e6f, float.PositiveInfinity, float.NegativeInfinity, 5.96e-8f, 2.98e-8f, 2.99e-8f, 6.1e-5f, 6.09e-5f, 1e-9f };
            for (int e = -26; e <= 17; e++) foreach (double m in new[] { 1.0, 1.0004882812, 1.0009765625, 1.00048828125, 1.5, 1.99951171875, 1.999755859375 }) { values.Add((float)(m * Math.Pow(2, e))); values.Add((float)(-m * Math.Pow(2, e))); }
            var rng = new Random(20261012);
            for (int i = 0; i < 200000; i++) values.Add((float)((rng.NextDouble() - 0.5) * Math.Pow(2, rng.Next(-30, 20))));
            foreach (var f in values)
                Assert.True(MeshPack.Half(f) == BitConverter.HalfToUInt16Bits((Half)f), $"{f:R}: {MeshPack.Half(f):X4}, .NET {BitConverter.HalfToUInt16Bits((Half)f):X4}");
            Assert.True(double.IsNaN(MeshPack.FromHalf(MeshPack.Half(float.NaN))));
            foreach (var f in values.Where(v => !float.IsInfinity(v) && Math.Abs(v) < 65504)) Assert.Equal((double)(float)(Half)f, MeshPack.FromHalf(MeshPack.Half(f)));
        }

        [Fact]
        public void TheMassingsFacadeNumbersFitHalves()
        {
            // a massing's facade numbers go to the GPU as 16-bit floats: metres along a wall and up a run of storeys, a
            // window span, a bay's width, a kind. Off by a few centimetres at most on the test city: a tenth of a pixel where
            // a massing shows (under 4 px a metre)
            double worst = 0, largest = 0; long n = 0; string at = "";
            foreach (var site in new[] { City(3000, false), City(1000, true) })
                foreach (var b in site.Buildings)
                {
                    var m = Lod2.Build(site, b);
                    foreach (var list in new[] { m.Fac, m.Fac2 })
                        foreach (var v in list)
                        {
                            double err = Math.Abs(MeshPack.FromHalf(MeshPack.Half(v)) - v); n++;
                            if (err > worst) { worst = err; at = $"{b.name}: {v:0.###}"; }
                            largest = Math.Max(largest, Math.Abs(v));
                        }
                }
            output.WriteLine($"{n:N0} numbers, the largest {largest:0.0}: off by {worst * 100:0.00} cm at most ({at})");
            Assert.True(worst < 0.07, $"{worst:0.000} m off ({at})");
        }
    }

    /// <summary>
    /// What packing allocates, given back and taken again (<see cref="Chunks"/>): counted on this thread, so on its own,
    /// as no other test may take the chunks given back meanwhile.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class MeshPackReuseTests
    {
        readonly ITestOutputHelper output;
        public MeshPackReuseTests(ITestOutputHelper o) { output = o; }

        static Site City(int n, bool walkIn)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, n, walkIn);
            return new Site(d.buildings);
        }

        [Fact]
        public void APackedMeshIsTheSizeOfItsGpuMesh()
        {
            var site = City(300, true);
            // a large building that fits 16-bit indices: one part, its vertices as welded
            var (b, l0) = site.Buildings.Select(x => (x, l0: Lod0.Build(site, x))).First(x => { x.l0.Op.Weld(); return x.l0.Op.Verts > 20000 && x.l0.Op.Verts <= MeshPack.MaxVerts; });
            int nv = l0.Op.Verts, ni = l0.Op.I.Count;
            Chunks.Clear();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var pm = MeshPack.Pack(l0.Op, true, null).Single();
            long made = GC.GetAllocatedBytesForCurrentThread() - a0, gpu = 32L * nv + 2L * ni;
            // uploaded, its chunks go back (Chunks), and the next packing takes them
            pm.Release();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            var again = MeshPack.Pack(l0.Op, true, null).Single();
            long remade = GC.GetAllocatedBytesForCurrentThread() - a1;
            output.WriteLine($"{b.name}: {nv:N0} vertices; packed {made / 1024:N0} KB made for a {gpu / 1024:N0} KB mesh, {remade / 1024:N0} KB packed again after an upload");
            Assert.True(made < gpu * 1.5, $"{made} bytes made for a {gpu}-byte mesh");
            Assert.True(remade < gpu / 10, $"{remade} bytes made packing again");
            Assert.True(again.V.Capacity >= nv && again.V.Capacity < nv + Chunks.Size);
            again.Release(); Chunks.Clear();
        }
    }
}
