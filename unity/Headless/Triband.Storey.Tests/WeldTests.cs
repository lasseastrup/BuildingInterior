using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What welding finds, mesh by mesh (docs/CITY.md §7): the generator shares a full-detail building's vertices already,
    /// so the renderer packs LOD0 and its glass unwelded and welds only the shell. If the generator starts making
    /// duplicates, the first test fails and LOD0's weld should come back. Storey's own.
    /// </summary>
    public class WeldTests
    {
        readonly ITestOutputHelper output;
        public WeldTests(ITestOutputHelper o) { output = o; }

        static Site City(int n, bool walkIn)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, n, walkIn);
            return new Site(d.buildings);
        }

        [Fact]
        public void FullDetailHasAlmostNothingToWeldAndTheShellHas()
        {
            long l0 = 0, l0Gone = 0, gl = 0, glGone = 0, l1 = 0, l1Gone = 0;
            foreach (var site in new[] { new Site(PrototypeJson.Read(Fixtures.Text("demo.json")).Document.buildings), City(300, true), City(300, false) })
                foreach (var b in site.Buildings.Where((x, i) => i % 5 == 0))
                {
                    var r = Lod0.Build(site, b); var s = Lod1.Build(site, b);
                    l0 += r.Op.Verts; l0Gone += r.Op.Weld();
                    gl += r.Glass.Verts; glGone += r.Glass.Weld();
                    l1 += s.Verts; l1Gone += s.Weld();
                }
            output.WriteLine($"welding removes {100.0 * l0Gone / l0:0.00}% of LOD0's {l0:N0} vertices, {100.0 * glGone / gl:0.00}% of the glass's {gl:N0}, {100.0 * l1Gone / l1:0.00}% of LOD1's {l1:N0}");
            Assert.True(l0Gone * 100 < l0, $"LOD0's weld removes {l0Gone} of {l0} vertices: pack it welded again");
            Assert.True(glGone * 100 < gl, $"the glass's weld removes {glGone} of {gl} vertices: pack it welded again");
            Assert.True(l1Gone * 100 > l1, $"LOD1's weld removes only {l1Gone} of {l1} vertices");
        }

        [Fact]
        public void TheWeldKeepsEveryPanesPlace()
        {
            // a pane vertex carries its place on the pane, which the weld doesn't compare: it must never merge two of them
            int checkedMeshes = 0;
            foreach (var site in new[] { new Site(PrototypeJson.Read(Fixtures.Text("demo.json")).Document.buildings), City(300, true) })
                foreach (var b in site.Buildings.Where((x, i) => i % 7 == 0))
                {
                    var r = Lod0.Build(site, b);
                    foreach (var m in new[] { r.Op, r.Glass, Lod1.Build(site, b) })
                    {
                        int panes = m.PaneUV.Count; m.Weld();
                        Assert.Equal(panes, m.PaneUV.Count);
                        checkedMeshes++;
                    }
                }
            output.WriteLine($"{checkedMeshes} meshes welded without merging a pane vertex");
        }
    }
}
