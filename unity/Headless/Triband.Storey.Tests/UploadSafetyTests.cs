using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What the renderer hands Unity's mesh upload (and Mesh.Optimize, which crashes natively on a bad index buffer):
    /// every index inside its mesh, whole triangles, and welded meshes the same. Empty meshes exist (a shell's LOD0 glass)
    /// and are not uploaded.
    /// </summary>
    public class UploadSafetyTests
    {
        readonly ITestOutputHelper log;
        public UploadSafetyTests(ITestOutputHelper log) { this.log = log; }

        static void Check(MeshBuilder m, string what)
        {
            Assert.True(m.I.Count % 3 == 0, what + ": whole triangles");
            int nv = m.Verts;
            Assert.All(m.I, i => Assert.True(i >= 0 && i < nv, $"{what}: index {i} of {nv} vertices"));
            Assert.Equal(nv, m.N.Count); Assert.Equal(nv, m.C.Count);
            if (!m.Lean) { Assert.Equal(nv, m.K.Count); Assert.Equal(nv, m.WI.Count); }
        }

        static int Degenerate(MeshBuilder m)
        {
            int n = 0;
            for (int t = 0; t + 2 < m.I.Count; t += 3) { int a = m.I[t], b = m.I[t + 1], c = m.I[t + 2]; if (a == b || b == c || a == c) n++; }
            return n;
        }

        [Fact]
        public void EveryDetailMeshOfTheTestCityIsSound()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            TestCity.Generate(d, 300);
            var site = new Site(d.buildings); int empty = 0;
            foreach (var b in d.buildings)
            {
                var l0 = Lod0.Build(site, b); var l1 = Lod1.Build(site, b);
                foreach (var (m, w) in new[] { (l0.Op, "LOD0"), (l0.Glass, "LOD0 glass"), (l1, "LOD1") })
                {
                    Check(m, $"{b.name} {w}"); m.Weld(); Check(m, $"{b.name} {w} welded");
                    if (m.Tris == 0) empty++;
                }
            }
            log.WriteLine($"{empty} empty meshes among {d.buildings.Count * 3}");
            int before = 0, after = 0, meshes = 0;
            foreach (var b in d.buildings)
            {
                var l0 = Lod0.Build(site, b);
                foreach (var m in new[] { l0.Op, l0.Glass, Lod1.Build(site, b) })
                {
                    int x = Degenerate(m); m.Weld(); int y = Degenerate(m);
                    before += x; after += y; if (y > 0) meshes++;
                }
            }
            log.WriteLine($"triangles repeating a vertex: {before} before welding, {after} after, in {meshes} meshes");
            Assert.Equal(0, after);
        }
    }
}
