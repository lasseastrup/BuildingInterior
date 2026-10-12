using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Merged far-distance cells (<see cref="Cells"/>, docs/CITY.md §3).</summary>
    public class CellsTests
    {
        static (Site site, List<BuildingData> bs) City(int n)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            TestCity.Generate(d, n);
            return (new Site(d.buildings), d.buildings);
        }

        [Fact]
        public void AMergedCellHoldsEveryMemberWhole()
        {
            var (site, bs) = City(60);
            var members = new List<Cells.Member>(); int verts = 0, tris = 0, row = 100;
            foreach (var b in bs)
            {
                var m = Lod2.Build(site, b); var rows = Enumerable.Range(row, m.Rows.Count).ToArray(); row += m.Rows.Count;
                members.Add(new Cells.Member(m, site.IndexOf(b) + 2 * Lod1.LOD_TAG, rows)); verts += m.Verts; tris += m.Tris;
            }
            var merged = Cells.Merge(members, 4000);
            Assert.True(merged.Count > 1, "split under the vertex limit");
            Assert.All(merged, c => Assert.True(c.Verts <= 4000));
            Assert.Equal(verts, merged.Sum(c => c.Verts)); Assert.Equal(tris, merged.Sum(c => c.Tris));
            // every vertex keeps its building's tag and its rows' places in the table; indices stay inside their piece
            int at = 0, mi = 0;
            foreach (var c in merged)
            {
                Assert.All(c.I, i => Assert.InRange(i, 0, c.Verts - 1));
                for (int v = 0; v < c.Verts; )
                {
                    var m = members[mi]; while (m.Mesh.Verts == 0) m = members[++mi];
                    for (int k = 0; k < m.Mesh.Verts; k++, v++)
                    {
                        Assert.Equal(m.Tag, c.Tags![v]);
                        Assert.Equal(m.Rows[m.Mesh.Slot[k]], c.Slot[v]);
                    }
                    mi++; at++;
                }
            }
        }

        static List<Cells.Member> Members(Site site, List<BuildingData> bs)
        {
            var members = new List<Cells.Member>(); int row = 100;
            foreach (var b in bs)
            {
                var m = Lod2.Build(site, b); var rows = Enumerable.Range(row, m.Rows.Count).ToArray(); row += m.Rows.Count;
                members.Add(new Cells.Member(m, site.IndexOf(b) + 2 * Lod1.LOD_TAG, rows));
            }
            return members;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void APackedCellIsTheMergedCellAsItWasUploaded(bool half)
        {
            var (site, bs) = City(60);
            var members = Members(site, bs);
            var merged = Cells.Merge(members, 4000); var packed = Cells.Pack(members, half, 4000);
            Assert.True(merged.Count > 1);
            Assert.Equal(merged.Count, packed.Count);
            for (int p = 0; p < merged.Count; p++)
            {
                var m = merged[p]; var c = packed[p];
                Assert.Equal(m.Verts, c.Verts); Assert.Equal(m.I.Count, c.Indices);
                Assert.NotNull(c.I16); Assert.Null(c.I32);
                for (int i = 0; i < m.I.Count; i++) Assert.Equal(m.I[i], c.I16![i]);
                float x0 = float.MaxValue, y0 = x0, z0 = x0, x1 = float.MinValue, y1 = x1, z1 = x1;
                for (int v = 0; v < m.Verts; v++)
                {
                    // each vertex as MeshUpload made it from the merged mesh
                    var q = m.P[v]; var n = m.N[v]; int f = v * 4, g = v * 2;
                    float x = (float)q.x, y = (float)q.y, z = (float)q.z;
                    x0 = Math.Min(x0, x); y0 = Math.Min(y0, y); z0 = Math.Min(z0, z); x1 = Math.Max(x1, x); y1 = Math.Max(y1, y); z1 = Math.Max(z1, z);
                    if (half)
                    {
                        ref var h = ref c.Half![v];
                        Assert.Equal((x, y, z), (h.x, h.y, h.z));
                        Assert.Equal(((sbyte)n.x, (sbyte)n.y, (sbyte)n.z, (sbyte)0), (h.nx, h.ny, h.nz, h.nw));
                        Assert.Equal((float)m.Tags![v], h.tag); Assert.Equal((float)m.Slot[v], h.slot);
                        Assert.Equal((MeshPack.Half(m.Fac[f]), MeshPack.Half(m.Fac[f + 1]), MeshPack.Half(m.Fac[f + 2]), MeshPack.Half(m.Fac[f + 3])), (h.facX, h.facY, h.facZ, h.facW));
                        Assert.Equal((MeshPack.Half(m.Fac2[g]), MeshPack.Half(m.Fac2[g + 1])), (h.fac2X, h.fac2Y));
                    }
                    else
                    {
                        ref var w = ref c.Full![v];
                        Assert.Equal((x, y, z), (w.x, w.y, w.z));
                        Assert.Equal(((sbyte)n.x, (sbyte)n.y, (sbyte)n.z, (sbyte)0), (w.nx, w.ny, w.nz, w.nw));
                        Assert.Equal((float)m.Tags![v], w.tag); Assert.Equal((float)m.Slot[v], w.slot);
                        Assert.Equal(((float)m.Fac[f], (float)m.Fac[f + 1], (float)m.Fac[f + 2], (float)m.Fac[f + 3]), (w.facX, w.facY, w.facZ, w.facW));
                        Assert.Equal(((float)m.Fac2[g], (float)m.Fac2[g + 1]), (w.fac2X, w.fac2Y));
                    }
                }
                Assert.Equal((x0, y0, z0, x1, y1, z1), (c.X0, c.Y0, c.Z0, c.X1, c.Y1, c.Z1));
                c.Release();
                Assert.Equal(0, (half ? c.Half!.Count : c.Full!.Count) + c.I16!.Count);
            }
        }

        [Fact]
        public void AMassingPast16BitIndicesIsAPieceOfItsOwnWith32BitOnes()
        {
            // no layout's massing comes near 65,535 vertices: a made-up one, between two real ones
            var (site, bs) = City(2); var small = Lod2.Build(site, bs[0]); var rows = Enumerable.Range(0, small.Rows.Count).ToArray();
            var big = new Lod2Mesh(); const int n = 70002;
            for (int i = 0; i < n; i++) { big.P.Add(new P3(i, 0, 0)); big.N.Add(new P3(0, 127, 0)); for (int k = 0; k < 4; k++) big.Fac.Add(0); big.Fac2.Add(0); big.Fac2.Add(0); big.Slot.Add(0); }
            for (int i = 0; i < n; i++) big.I.Add(n - 1 - i);
            var packed = Cells.Pack(new[] { new Cells.Member(small, 5, rows), new Cells.Member(big, 7, new[] { 3 }), new Cells.Member(small, 9, rows) }, true);
            Assert.Equal(3, packed.Count);
            Assert.Equal(n, packed[1].Verts); Assert.Null(packed[1].I16); Assert.NotNull(packed[1].I32);
            for (int i = 0; i < n; i++) Assert.Equal(n - 1 - i, packed[1].I32![i]);
            Assert.NotNull(packed[0].I16); Assert.NotNull(packed[2].I16);
            foreach (var c in packed) c.Release();
        }

        [Fact]
        public void TheCityFallsIntoCompactCells()
        {
            var (site, bs) = City(3000);
            var cells = new Dictionary<(int, int), int>();
            foreach (var b in bs) { var bb = Site.BoundsOf(b); var k = Cells.KeyOf((bb.x0 + bb.x1) / 2, (bb.z0 + bb.z1) / 2); cells[k] = cells.TryGetValue(k, out var n) ? n + 1 : 1; }
            Console.WriteLine($"3,000-building city: {cells.Count} cells, up to {cells.Values.Max()} buildings in one");
            Assert.InRange(cells.Count, 200, 450);   // the prototype has 316
        }
    }

    /// <summary>
    /// A packed cell's chunks are given back after its upload and taken again by the next (<see cref="Chunks"/>):
    /// counted on this thread, with the shared pool of chunks, so on its own.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class CellPackReuseTests
    {
        [Fact]
        public void PackingCellsOneAfterAnotherMakesLittleGarbage()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 200);
            var site = new Site(d.buildings); var members = new List<Cells.Member>(); int row = 0;
            foreach (var b in d.buildings)
            {
                var m = Lod2.Build(site, b); members.Add(new Cells.Member(m, site.IndexOf(b) + 2 * Lod1.LOD_TAG, Enumerable.Range(row, m.Rows.Count).ToArray())); row += m.Rows.Count;
            }
            long KeepWas = Chunks.KeepBytes; Chunks.KeepBytes = 16L << 20; Chunks.Clear();
            try
            {
                foreach (var c in Cells.Pack(members, true)) c.Release();   // the first fills the pool
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int r = 0; r < 5; r++) foreach (var c in Cells.Pack(members, true)) c.Release();
                long pack = (GC.GetAllocatedBytesForCurrentThread() - a0) / 5;
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                Cells.Merge(members);
                long merge = GC.GetAllocatedBytesForCurrentThread() - b0;
                Console.WriteLine($"{members.Count} massings: packing them again allocates {pack / 1024} KB, merging them {merge / 1024} KB");
                Assert.True(pack * 20 < merge, $"packing allocated {pack} bytes, merging {merge}");
            }
            finally { Chunks.KeepBytes = KeepWas; Chunks.Clear(); }
        }
    }
}
