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
                if (windows && s.slot == ColorSlot.Glass && gb.PaneUV.TryGetValue(i, out var uv)) { var (pa, pw, pb) = ColorRows.EncodePane(uv.u, uv.v, uv.id); verts[i].a = pa; verts[i].nw = pw; verts[i].b = pb; }
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
                        Assert.True(rv.SequenceEqual(pm.V), $"{what}: the vertices differ");
                        Assert.True(rc == null ? pm.Cut == null : rc.SequenceEqual(pm.Cut!), $"{what}: the cutaway stream differs");
                        var ix = pm.I16 != null ? pm.I16.Select(i => (int)i).ToArray() : pm.I32!;
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
                    tris.AddRange(p.I16!.Select(i => p.V[i]));
                    Assert.True(p.V.All(v => v.x >= p.X0 && v.x <= p.X1 && v.z >= p.Z0 && v.z <= p.Z1));
                }
                Assert.True(tris.SequenceEqual(whole.I32 != null ? whole.I32.Select(i => whole.V[i]) : whole.I16!.Select(i => whole.V[i])), "the parts draw other triangles");
                Assert.True(extra < m.Verts / 20, $"{extra} vertices copied");
            }
        }

        [Fact]
        public void APackedMeshIsTheSizeOfItsGpuMesh()
        {
            var site = City(300, true);
            // a large building that fits 16-bit indices: one part, its vertices as welded
            var (b, l0) = site.Buildings.Select(x => (x, l0: Lod0.Build(site, x))).First(x => { x.l0.Op.Weld(); return x.l0.Op.Verts > 20000 && x.l0.Op.Verts <= MeshPack.MaxVerts; });
            int nv = l0.Op.Verts, ni = l0.Op.I.Count;
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var pm = MeshPack.Pack(l0.Op, true, null).Single();
            long made = GC.GetAllocatedBytesForCurrentThread() - a0, gpu = 32L * nv + 2L * ni;
            output.WriteLine($"{b.name}: {nv:N0} vertices; packed {made / 1024:N0} KB made for a {gpu / 1024:N0} KB mesh");
            Assert.True(made < gpu * 1.25, $"{made} bytes made for a {gpu}-byte mesh");
        }
    }
}
