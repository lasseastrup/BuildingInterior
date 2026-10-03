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
}
