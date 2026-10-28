using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The upload's mesh hygiene: welding the vertices the upload can't tell apart changes no triangle as uploaded, and every
    /// LOD0 wall vertex has a wall id, through which the shader now reads the wall's cutaway data (the vertex carries only
    /// kind and id).
    /// </summary>
    public class MeshTopologyTests
    {
        readonly ITestOutputHelper output; public MeshTopologyTests(ITestOutputHelper o) { output = o; }

        public static IEnumerable<object[]> Core() => GeneratorTests.Core();

        static (StoreyDocument doc, Site site) Load(string file) { var d = PrototypeJson.Read(Fixtures.Text(file)).Document; return (d, new Site(d.buildings)); }

        // each triangle's corners as uploaded: the position's floats, the normal's packed bytes (MeshPack)
        static List<string> Corners(MeshBuilder m) =>
            Enumerable.Range(0, m.I.Count).Select(t => { int i = m.I[t]; return $"{(float)m.P[i].x},{(float)m.P[i].y},{(float)m.P[i].z}|{MeshPack.S8(m.N[i].x)},{MeshPack.S8(m.N[i].y)},{MeshPack.S8(m.N[i].z)}|{m.C[i]}" + (m.Lean ? "" : $"|{m.K[i]}|{m.WI[i]}"); }).ToList();

        [Theory, MemberData(nameof(Core))]
        public void WeldingChangesNoTriangle(string file, int i)
        {
            var (doc, site) = Load(file);
            var b = doc.buildings[i];
            foreach (var m in new[] { Lod0.Build(site, b).Op, Lod0.Build(site, b).Glass, Lod1.Build(site, b) })
            {
                var before = Corners(m); int nv = m.Verts;
                int gone = m.Weld();
                Assert.Equal(nv - gone, m.Verts);
                Assert.Equal(before, Corners(m));   // the same corners, in the same order: the same triangles
                Assert.Equal(0, m.Weld());           // nothing left to merge
            }
        }

        /// <summary>
        /// LOD1 as packed (welded) and the massing have no vertex the upload can't tell from another: a strip's corner, worked
        /// out from each of the two walls it mitres between, and a parapet's top where it turns a corner, are one vertex.
        /// </summary>
        [Theory, MemberData(nameof(Core))]
        public void NoVertexOfAShellOrAMassingIsThereTwice(string file, int i)
        {
            var (doc, site) = Load(file);
            var b = doc.buildings[i];
            foreach (var pm in MeshPack.Pack(Lod1.Build(site, b), windows: true))
            {
                var seen = new HashSet<string>();
                for (int v = 0; v < pm.Verts; v++) { var q = pm.V[v]; Assert.True(seen.Add($"{q.x:R},{q.y:R},{q.z:R}|{q.nx},{q.ny},{q.nz},{q.nw}|{q.r},{q.g},{q.b},{q.a}|{q.tag:R}"), $"{b.name}: LOD1 vertex {v} twice"); }
            }
            var m = Lod2.Build(site, b); var at = new HashSet<string>();
            for (int v = 0; v < m.Verts; v++)
            {
                string F(double x) => ((float)x + 0f).ToString("R");
                Assert.True(at.Add($"{F(m.P[v].x)},{F(m.P[v].y)},{F(m.P[v].z)}|{m.N[v]}|{F(m.Fac[4 * v])},{F(m.Fac[4 * v + 1])},{F(m.Fac[4 * v + 2])},{F(m.Fac[4 * v + 3])}|{F(m.Fac2[2 * v])},{F(m.Fac2[2 * v + 1])}|{m.Slot[v]}"), $"{b.name}: massing vertex {v} twice");
            }
            Assert.Equal(0, m.Weld());
        }

        [Theory, MemberData(nameof(Core))]
        public void EveryWallVertexHasAWallId(string file, int i)
        {
            var (doc, site) = Load(file);
            var r = Lod0.Build(site, doc.buildings[i]);
            foreach (var m in new[] { r.Op, r.Glass })
                for (int v = 0; v < m.Verts; v++)
                    if (m.K[v] % 8 == 1) Assert.True(m.WI[v] > 0 && m.WI[v] <= m.Walls.Count, $"{doc.buildings[i].name}: a wall vertex with no wall id");
        }

        [Theory, MemberData(nameof(Core))]
        public void EveryWallKnowsItsStorey(string file, int i)
        {
            var (doc, site) = Load(file);
            var b = doc.buildings[i];
            var r = Lod0.Build(site, b);
            Assert.Equal(r.Op.Walls.Count, r.Op.WallBase.Count);
            var bases = Enumerable.Range(0, b.floors.Count + 1).Select(k => Derived.FloorBase(b, k)).ToHashSet();
            Assert.All(r.Op.WallBase, y => Assert.Contains(y, bases));
        }

        [Fact]
        public void TheDemoStreetIsLeaner()
        {
            // 44 bytes a LOD0 vertex before (with the cutaway's wall data on every vertex), 28 after, and the welding on top
            var (doc, site) = Load("demo.json");
            long before = 0, after = 0; int vb = 0, va = 0;
            foreach (var b in doc.buildings)
            {
                var r = Lod0.Build(site, b);
                foreach (var m in new[] { r.Op, r.Glass })
                {
                    int ib = m.Verts > 65535 ? 4 : 2;
                    before += m.Verts * 44L + m.I.Count * ib; vb += m.Verts;
                    m.Weld();
                    after += m.Verts * 28L + m.I.Count * ib + 0; va += m.Verts;
                }
                after += r.Op.Walls.Count * 16L;   // the walls' entries in the table
            }
            output.WriteLine($"LOD0 vertices {vb} -> {va} ({100.0 * (vb - va) / vb:0.0}% welded); LOD0 bytes {before / 1024} KB -> {after / 1024} KB ({100.0 * (before - after) / before:0}% less)");
            Assert.True(after < before * 0.7);
        }
    }
}
