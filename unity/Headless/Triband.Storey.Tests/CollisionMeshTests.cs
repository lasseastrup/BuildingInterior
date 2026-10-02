using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The collision mesh (<see cref="CollisionMesh"/>) against the walk model it stands for: a ray dropped onto the floors
    /// and stairs lands where <see cref="PlayWorld.SurfaceAt"/> says (at the prototype's sample points, <c>play.json</c>),
    /// every collision segment is a wall a ray cannot pass, the floors face up, and it stays small.
    /// </summary>
    public class CollisionMeshTests
    {
        readonly ITestOutputHelper output; public CollisionMeshTests(ITestOutputHelper o) { output = o; }

        public static IEnumerable<object[]> Corpora() { yield return new object[] { "demo" }; yield return new object[] { "variants" }; }

        static readonly Dictionary<string, List<(BuildingData b, CollisionMesh m)>> Built = new();
        static List<(BuildingData b, CollisionMesh m)> Meshes(string corpus)
        {
            lock (Built)
            {
                if (Built.TryGetValue(corpus, out var l)) return l;
                var (doc, world) = PlayWorldTests.World(corpus);
                l = doc.buildings.Select(b => (b, CollisionMesh.Build(world.Site, b, world.Lod0Of(b)))).ToList();
                return Built[corpus] = l;
            }
        }

        /// <summary>Möller–Trumbore, both sides: the ray's distance to the triangle, or null.</summary>
        static double? Hit(P3 o, P3 d, P3 a, P3 b, P3 c)
        {
            var e1 = b - a; var e2 = c - a; var p = P3.Cross(d, e2); double det = e1.x * p.x + e1.y * p.y + e1.z * p.z;
            if (Math.Abs(det) < 1e-12) return null;
            double inv = 1 / det; var s = o - a; double u = (s.x * p.x + s.y * p.y + s.z * p.z) * inv; if (u < -1e-9 || u > 1 + 1e-9) return null;
            var q = P3.Cross(s, e1); double v = (d.x * q.x + d.y * q.y + d.z * q.z) * inv; if (v < -1e-9 || u + v > 1 + 1e-9) return null;
            double t = (e2.x * q.x + e2.y * q.y + e2.z * q.z) * inv;
            return t >= -1e-9 ? t : null;
        }

        static double? Down(IEnumerable<CollisionMesh> ms, double x, double y, double z, Func<CollisionKind, bool> kinds)
        {
            var o = new P3(x, y, z); var d = new P3(0, -1, 0); double? best = null;
            foreach (var m in ms)
                for (int t = 0; t < m.Tris; t++)
                {
                    if (!kinds(m.Kind[t])) continue;
                    var h = Hit(o, d, m.P[m.I[3 * t]], m.P[m.I[3 * t + 1]], m.P[m.I[3 * t + 2]]);
                    if (h != null && (best == null || h < best)) best = h;
                }
            return best == null ? null : y - best;
        }

        static double DistToEdges(List<Vec2> fp, double x, double z)
        {
            double d = 1e9;
            for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; d = Math.Min(d, Edit.Tiers.SegDist(x, z, a.x, a.z, c.x, c.z).d); }
            return d;
        }

        [Theory, MemberData(nameof(Corpora))]
        public void ARayDroppedLandsWhereTheWalkModelStands(string corpus)
        {
            var (doc, world) = PlayWorldTests.World(corpus);
            var meshes = Meshes(corpus).Select(x => x.m).ToList();
            var bad = new List<string>(); int n = 0, skipped = 0;
            foreach (var c in PlayWorldTests.Play.Value.RootElement.GetProperty("corpora").GetProperty(corpus).GetProperty("surface").EnumerateArray())
            {
                var a = c.EnumerateArray().Select(e => e.GetDouble()).ToArray();
                double x = a[0], z = a[1], y = a[2];
                double want = world.SurfaceAt(x, z, y);
                // not compared: inside a shell (no one gets in, so no floors), and on the rim the walk model measures exactly
                // (a distance from each outline) where the collider has a polygon arc
                bool skip = false;
                foreach (var b in doc.buildings)
                {
                    double lx = x - b.pos.x, lz = z - b.pos.z;
                    for (int k = 0; k <= b.floors.Count && !skip; k++)
                    {
                        var fp = Derived.OutlineAt(b, k); double dd = DistToEdges(fp, lx, lz);
                        if (Math.Abs(dd - (Dim.T_EXT + 0.02)) < 0.03) skip = true;
                        if (!b.interior && (Geo.Pip(fp, lx, lz) || dd < Dim.T_EXT + 0.05)) skip = true;
                    }
                }
                if (skip) { skipped++; continue; }
                n++;
                double? hit = Down(meshes, x, y + PlayWorld.StepUp + 1e-7, z, k => k == CollisionKind.Floor || k == CollisionKind.Stair);
                double got = hit ?? (y + PlayWorld.StepUp >= 0 ? 0 : -1e9);
                if (want > 0 && Math.Abs(got - want) > 1e-6) bad.Add($"({x}, {z}) from {y}: collider {got}, walk model {want}");
                else if (want <= 0 && got > 1e-6) bad.Add($"({x}, {z}) from {y}: collider {got}, walk model the ground");
            }
            output.WriteLine($"{n} compared, {skipped} skipped");
            Assert.True(n > 500);
            Assert.True(bad.Count == 0, $"{bad.Count} of {n}:\n" + string.Join("\n", bad.Take(12)));
        }

        [Theory, MemberData(nameof(Corpora))]
        public void EveryWallSegmentStopsARay(string corpus)
        {
            var (doc, world) = PlayWorldTests.World(corpus);
            var meshes = Meshes(corpus);
            var bad = new List<string>(); int n = 0;
            foreach (var (b, m) in meshes)
            {
                var segs = world.Lod0Of(b).Segs; int N = b.floors.Count;
                for (int k = 0; k < segs.Count; k++)
                    foreach (var s in segs[k])
                    {
                        double dx = s.bx - s.ax, dz = s.bz - s.az, L = Math.Sqrt(dx * dx + dz * dz); if (L < 0.05) continue;
                        double nx = -dz / L, nz = dx / L, mx = (s.ax + s.bx) / 2, mz = (s.az + s.bz) / 2;
                        double y = Derived.FloorBase(b, k) + (k < N ? 1.0 : 0.6);
                        var o = new P3(mx + nx * 2, y, mz + nz * 2); var d = new P3(-nx, 0, -nz);
                        bool hit = false;
                        for (int t = 0; t < m.Tris && !hit; t++)
                        {
                            if (m.Kind[t] != CollisionKind.Wall) continue;
                            var h = Hit(o, d, m.P[m.I[3 * t]], m.P[m.I[3 * t + 1]], m.P[m.I[3 * t + 2]]);
                            hit = h != null && h <= 2 + 1e-6;
                        }
                        n++;
                        if (!hit) bad.Add($"{b.name} storey {k}: segment ({s.ax:0.##}, {s.az:0.##})–({s.bx:0.##}, {s.bz:0.##}) lets a ray through");
                    }
            }
            Assert.True(n > 100);
            Assert.True(bad.Count == 0, $"{bad.Count} of {n}:\n" + string.Join("\n", bad.Take(12)));
        }

        [Theory, MemberData(nameof(Corpora))]
        public void FloorsAndStairsFaceUp(string corpus)
        {
            foreach (var (b, m) in Meshes(corpus))
                for (int t = 0; t < m.Tris; t++)
                {
                    if (m.Kind[t] == CollisionKind.Wall) continue;
                    var cr = P3.Cross(m.P[m.I[3 * t + 1]] - m.P[m.I[3 * t]], m.P[m.I[3 * t + 2]] - m.P[m.I[3 * t]]);
                    Assert.True(cr.y > 0, $"{b.name}: a {m.Kind[t]} triangle faces down");
                }
        }

        [Fact]
        public void ItIsSmallAndQuickToBuild()
        {
            var (doc, world) = PlayWorldTests.World("demo");
            foreach (var b in doc.buildings) world.Lod0Of(b);   // LOD0 exists already when colliders are made
            var sw = Stopwatch.StartNew();
            var ms = doc.buildings.Select(b => (b, m: CollisionMesh.Build(world.Site, b, world.Lod0Of(b)))).ToList();
            sw.Stop();
            foreach (var (b, m) in ms)
            {
                int render = world.Lod0Of(b).Op.Tris;
                output.WriteLine($"{b.name,-16} {m.Tris,6} collision triangles ({m.P.Count} vertices), LOD0 {render}: {100.0 * m.Tris / render:0.0}%");
                Assert.True(m.Tris < render / 4, $"{b.name}: {m.Tris} collision triangles for {render} rendered");
            }
            output.WriteLine($"all built in {sw.Elapsed.TotalMilliseconds:0.0} ms");
        }
    }
}
