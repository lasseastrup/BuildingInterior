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

        [Fact]
        public void AWorkersBuildGoesUpAMeshAtATimeOnceAThreadHasFinishedIt()
        {
            // docs/PERFORMANCE.md §3.2, §4.1: SiteRenderer stages a worker's build as it comes in, has a build thread write
            // its vertices' rows and wall ids (MeshPackTests: the same bytes), then uploads its meshes one at a time within
            // the budget, the forced buildings' first and whatever the budget, those in view next, the prefetched last; the
            // LOD is built once its last mesh is up. The upload doesn't finish a mesh twice
            string Text(params string[] path) => System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey", path));
            var renderer = Text("Unity", "Rendering", "SiteRenderer.cs");
            var take = renderer.Substring(renderer.IndexOf("int TakeJobs(System.Diagnostics.Stopwatch sw, int made)", System.StringComparison.Ordinal));
            take = take.Substring(0, take.IndexOf("bool TakeJobNow(", System.StringComparison.Ordinal));
            Assert.Contains("Stage(j);", take);
            Assert.DoesNotContain("FinishDetail(", take);
            int forced = take.IndexOf("TakeStaged(sw, made, BuildLane.Forced)", System.StringComparison.Ordinal), detail = take.IndexOf("TakeStaged(sw, made, BuildLane.Detail)", System.StringComparison.Ordinal), pre = take.IndexOf("TakeStaged(sw, made, BuildLane.Background)", System.StringComparison.Ordinal);
            Assert.True(forced > 0 && detail > forced && pre > detail, "the lanes' uploads in order");
            Assert.Contains("BuildThreads.Shared.Run(() => { for (int k = 0; k < n; k++) PartAt(which, d, k).Finish(rows[k], wallBase); return n; }, j.lane)", renderer);
            Assert.Contains("do { UploadPart(st.bt, st.name, st.which, st.d, st.next++); made++; }", renderer);
            Assert.Contains("while (st.next < st.parts && (forced || sw.Elapsed.TotalMilliseconds <= budget));", renderer);
            int rest = renderer.IndexOf("if (st.next < st.parts) continue;", System.StringComparison.Ordinal), built = renderer.IndexOf("l.SetBuilt(st.bt.idx, st.which, true);", System.StringComparison.Ordinal);
            Assert.True(rest > 0 && built > rest, "built once the last mesh is up");
            Assert.Contains("if (!pm.Finished)", Text("Unity", "Rendering", "MeshUpload.cs"));
        }

        [Fact]
        public void ABuildingsObjectsAreMadeForItsDetailAndColliderAloneAndKept()
        {
            // docs/PERFORMANCE.md §3.4: a building's own object comes with its first collider, not at load; a detail mesh's
            // object is one kept from a drop when there is one, under the site's detail object, and a drop keeps it with
            // no mesh (taken from it before the mesh goes); the stats overlay is a component of its own
            string Text(params string[] path) => System.IO.File.ReadAllText(UnityAsset.Package("com.triband.storey", path));
            var renderer = Text("Unity", "Rendering", "SiteRenderer.cs");
            string Method(string start, string next)
            {
                int a = renderer.IndexOf(start, System.StringComparison.Ordinal); Assert.True(a > 0, start);
                int b = renderer.IndexOf(next, a, System.StringComparison.Ordinal); Assert.True(b > a, next);
                return renderer.Substring(a, b - a);
            }
            Assert.DoesNotContain("new GameObject", Method("void Build(BuildingData b, Lod2Mesh? made = null)", "int[] WriteRows("));
            Assert.DoesNotContain("new GameObject", Method("void Add(Built bt, int which,", "PartObject TakeObject("));
            Assert.Contains("TakeObject(name)", Method("void Add(Built bt, int which,", "PartObject TakeObject("));
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(renderer, @"Root\(bt\)"));
            Assert.Contains("Root(bt).AddComponent<MeshCollider>()", renderer);
            var drop = Method("void DropDetail(Built bt, int which)", "/// <summary>");
            Assert.True(drop.IndexOf("FreeObject(o)", System.StringComparison.Ordinal) < drop.IndexOf("Kill(m)", System.StringComparison.Ordinal), "the objects let go of their meshes first");
            Assert.Contains("o.renderer.enabled = false; o.filter.sharedMesh = null;", renderer);
            Assert.DoesNotContain("OnGUI", Text("Unity", "Rendering", "StoreySite.cs"));
        }
    }
}
