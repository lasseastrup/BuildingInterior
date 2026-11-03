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
                l = doc.buildings.Select(b => (b, CollisionMesh.Build(world.Site, b, world.ShapeOf(b)))).ToList();
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
                var segs = world.ShapeOf(b).Segs; int N = b.floors.Count;
                for (int k = 0; k < segs.Count; k++)
                    foreach (var s in segs[k])
                    {
                        double dx = s.bx - s.ax, dz = s.bz - s.az, L = Math.Sqrt(dx * dx + dz * dz); if (L < 0.05) continue;
                        if (s.h < 0) continue;   // nothing stands on it over its floor (Seg.Flush): no wall there, by design
                        double nx = -dz / L, nz = dx / L, mx = (s.ax + s.bx) / 2, mz = (s.az + s.bz) / 2;
                        // a metre up a storey, 60 cm over the roof, or halfway up one lower than that (a roof's curb without a parapet)
                        double up = k < N ? 1.0 : 0.6; if (s.h > 0 && s.h <= up) up = s.h / 2;
                        double y = Derived.FloorBase(b, k) + up;
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

        // the nearest wall a ray meets, or null
        static double? WallHit(CollisionMesh m, P3 o, P3 d)
        {
            double? best = null;
            for (int t = 0; t < m.Tris; t++)
            {
                if (m.Kind[t] != CollisionKind.Wall) continue;
                var h = Hit(o, d, m.P[m.I[3 * t]], m.P[m.I[3 * t + 1]], m.P[m.I[3 * t + 2]]);
                if (h != null && (best == null || h < best)) best = h;
            }
            return best;
        }

        /// <summary>
        /// A terrace's parapet is a wall as high as it is, 1.1 m, not a storey high: Linden Court's terrace (the roof of its six
        /// lower storeys, east of its setback) stops a ray at its deck's level and lets one at head height over it, to the setback's
        /// wall 10 m in. The wall it stands on, the same line a storey down, ends at the parapet's top with it.
        /// </summary>
        [Fact]
        public void ATerracesParapetIsAsHighAsItIs()
        {
            var (_, world) = PlayWorldTests.World("demo");
            var b = world.Site.Buildings.First(x => x.name == "Linden Court");
            var m = CollisionMesh.Build(world.Site, b, world.ShapeOf(b));
            double deck = Derived.FloorBase(b, 6);
            // 2 m east of the terrace's outer edge (Linden Court's local x 16), over its middle, looking west
            var d = new P3(-1, 0, 0); double x = b.pos.x + 18, z = b.pos.z + 4;
            Assert.Equal(2 - Dim.T_EXT, WallHit(m, new P3(x, deck + 0.5, z), d)!.Value, 6);   // the wall stands outside the outline
            Assert.True(WallHit(m, new P3(x, deck + 1.5, z), d) is double over && over > 9.5, "a wall over the parapet");
            Assert.True(WallHit(m, new P3(x, deck - 1.0, z), d) is double below && below < 2.1);   // the wall below it, as before
            Assert.DoesNotContain(Enumerable.Range(0, m.P.Count), v => m.P[v].x > b.pos.x + 15.5 && m.P[v].y > deck + Lod0.Shared.TerraceParapet + 1e-6 && m.P[v].y < deck + 3);
        }

        /// <summary>The rails round a straight flight's opening are walls 1.02 m high, as they are, not a storey high.</summary>
        [Fact]
        public void AStairOpeningsRailsAreAsHighAsTheyAre()
        {
            var d = new StoreyDocument();
            var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(12, 0), new Vec2(12, 9), new Vec2(0, 9) };
            b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList(); b.interior = true; b.shafts.Clear();
            Assert.NotNull(Edit.Shafts.Place(b, 0, new Vec2(6, 4.5), CoreType.Flight, 0, Edit.Shafts.NewId(d)));
            var site = new Site(d.buildings);
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
            // on storey 1, across the opening from the walkway's side: the rail along the flight lane at x 6.04, then the far one
            double y = Derived.FloorBase(b, 1); var dir = new P3(-1, 0, 0);
            Assert.True(WallHit(m, new P3(8, y + 0.5, 4.5), dir) is double low && low < 2.0);
            Assert.True(WallHit(m, new P3(8, y + 1.5, 4.5), dir) is double high && high > 7, "a wall over the rails");
        }

        /// <summary>
        /// A flat roof's edge is as high in the collider as what stands on it: a parapet's wall
        /// (<see cref="CollisionMesh.RoofWallHeight"/>) only where the style has its parapet on. Without one, the 30 cm curb (which
        /// a cap hanging from the edge leaves), a raised cap's top (the roof lifted with it, a lid), or round a cap set in, where
        /// the walls' top is closed flush with the roof, nothing over the roof. A ray from 2 m west of the west wall meets the edge
        /// under its top and goes over the whole roof above it, and no wall stands higher. The walk model keeps every edge as a
        /// wall, as before: the roof's segments are the same lines whatever stands on them.
        /// </summary>
        [Theory]
        [InlineData(true, null, null, CollisionMesh.RoofWallHeight)]   // the parapet
        [InlineData(false, null, null, Lod0.Shared.RoofCurb)]          // the curb
        [InlineData(false, -0.6, -0.3, Lod0.Shared.RoofCurb)]          // a cap hanging from the edge, under the curb
        [InlineData(false, 0.6, -0.25, 0.6)]                           // a raised cap, the roof lifted to its top
        [InlineData(false, 0.5, 0.5, 0.0)]                             // a cap set in: nothing on the walls
        [InlineData(true, 0.5, 0.5, CollisionMesh.RoofWallHeight)]     // a cap set in, inside the parapet
        public void ARoofsEdgeIsAsHighAsWhatStandsOnIt(bool parapet, double? capH, double? capDepth, double high)
        {
            (Site site, BuildingData b) Block(bool withParapet)
            {
                var d = new StoreyDocument();
                var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
                b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
                b.details.Clear(); b.shafts.Clear(); b.interior = true;
                b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList();
                b.style.parapet = withParapet; b.style.capH = capH; b.style.capDepth = capDepth;
                b.style.windows = WindowType.None;   // the top storey's wall solid where the ray under the roof meets it
                return (new Site(d.buildings), b);
            }
            var (site, b) = Block(parapet);
            var shape = Lod0.BuildShape(site, b);
            var m = CollisionMesh.Build(site, b, shape);
            double roof = Derived.RoofY(b); var east = new P3(1, 0, 0);
            if (high > 0) Assert.Equal(2 - Dim.T_EXT, WallHit(m, new P3(-2, roof + high / 2, 5), east)!.Value, 6);   // its outer face
            Assert.Null(WallHit(m, new P3(-2, roof + high + 0.05, 5), east));
            Assert.True(WallHit(m, new P3(-2, roof - 1.0, 5), east) is double below && Math.Abs(below - (2 - Dim.T_EXT)) < 1e-6, "the top storey's wall");
            double top = Enumerable.Range(0, m.Tris).Where(t => m.Kind[t] == CollisionKind.Wall).SelectMany(t => new[] { 0, 1, 2 }.Select(c => m.P[m.I[3 * t + c]].y)).Max();
            Assert.Equal(roof + high, top, 9);
            // the walk model's walls: the roof's edge as with a parapet
            var (site1, b1) = Block(true);
            static IEnumerable<(double, double, double, double, double)> Lines(List<Seg> sg) => sg.Select(s => (s.ax, s.az, s.bx, s.bz, s.r));
            int N = b.floors.Count;
            Assert.Equal(Lines(Lod0.BuildShape(site1, b1).Segs[N]), Lines(shape.Segs[N]));
            Assert.NotEmpty(shape.Segs[N]);
        }

        /// <summary>A courtyard's edge on a flat roof without a parapet is its 30 cm curb in the collider too, not a parapet's wall.</summary>
        [Fact]
        public void ACourtyardsCurbIsAsHighAsItIs()
        {
            var (d, b) = CourtyardTests.Block();
            CourtyardTests.Court(b);   // (6, 5) to (14, 11); its walls stand just inside it
            b.style.parapet = false;
            var site = new Site(d.buildings);
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
            double roof = Derived.RoofY(b); var west = new P3(-1, 0, 0);
            // from the courtyard's middle, looking west at its west side (x 6, the wall from 6 to 6.3)
            Assert.True(WallHit(m, new P3(10, roof + 0.15, 8), west) is double low && Math.Abs(low - (4 - Dim.T_EXT)) < 1e-6, "the curb");
            Assert.True(WallHit(m, new P3(10, roof + 0.6, 8), west) is null, "a wall over the curb");
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
            var l0 = doc.buildings.ToDictionary(b => b, b => Lod0.Build(world.Site, b));   // LOD0 exists already when colliders are made
            var sw = Stopwatch.StartNew();
            var ms = doc.buildings.Select(b => (b, m: CollisionMesh.Build(world.Site, b, l0[b].Shape))).ToList();
            sw.Stop();
            foreach (var (b, m) in ms)
            {
                int render = l0[b].Op.Tris;
                output.WriteLine($"{b.name,-16} {m.Tris,6} collision triangles ({m.P.Count} vertices), LOD0 {render}: {100.0 * m.Tris / render:0.0}%");
                Assert.True(m.Tris < render / 4, $"{b.name}: {m.Tris} collision triangles for {render} rendered");
            }
            output.WriteLine($"all built in {sw.Elapsed.TotalMilliseconds:0.0} ms");
        }

        static string Print(CollisionMesh m)
        {
            var h = new HashCode();
            foreach (var p in m.P) { h.Add(p.x); h.Add(p.y); h.Add(p.z); }
            foreach (int i in m.I) h.Add(i);
            foreach (var k in m.Kind) h.Add(k);
            return $"{m.P.Count}/{m.Tris}/{h.ToHashCode()}";
        }

        /// <summary>
        /// A site making every collider as it loads (Colliders At Load, docs/PLAY.md §5) makes their meshes on the build
        /// threads at once (<see cref="CollisionMesh.BuildAll"/>): the same meshes as one after another, from a LOD0's shape
        /// where it gives one and from a shape made for the purpose otherwise, and a building whose build throws is reported,
        /// not thrown, the rest made all the same.
        /// </summary>
        [Fact]
        public void ManyAtOnceAreTheSameAsOneByOne()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            Edit.TestCity.Generate(d, 300, walkIn: true);
            var bs = d.buildings; var site = new Site(bs);
            var each = bs.Select(b => Print(CollisionMesh.Build(site, b, Lod0.BuildShape(site, b)))).ToList();
            var threads = new Lod.BuildThreads(4);
            try
            {
                var sw = Stopwatch.StartNew();
                var made = CollisionMesh.BuildAll(new Site(bs), bs, null, threads);
                output.WriteLine($"{bs.Count} walk-in buildings' collision meshes, shapes made for them, on four build threads: {sw.Elapsed.TotalMilliseconds:0} ms");
                Assert.Equal(each, made.Select(m => Print(m!)).ToList());
                // from LOD0's shapes where given; one building that throws, reported
                var site2 = new Site(bs);
                var shapes = bs.Select((b, i) => i % 2 == 0 ? Lod0.Build(site2, b).Shape : null).ToList();
                var broken = new BuildingData { name = "broken", floors = null! };   // throws as soon as it is read
                var failed = new List<string>();
                var some = CollisionMesh.BuildAll(site2, bs.Append(broken).ToList(), shapes.Append(null).ToList(), threads, (b, ex) => failed.Add(b.name));
                Assert.Equal(new[] { "broken" }, failed);
                Assert.Null(some[bs.Count]);
                Assert.Equal(each, some.Take(bs.Count).Select(m => Print(m!)).ToList());
            }
            finally { threads.Resize(0); }
        }
    }
}
