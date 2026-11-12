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
            var s = Edit.Shafts.Place(b, 0, new Vec2(6, 4.5), CoreType.Flight, 0, Edit.Shafts.NewId(d));
            Assert.NotNull(s);
            s!.rails = false;   // the opening's rails alone: the flight going on up from here has none of its own
            var site = new Site(d.buildings);
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
            // on storey 1, across the opening from the walkway's side: the rail along the flight lane at x 6.04, then the far one
            double y = Derived.FloorBase(b, 1); var dir = new P3(-1, 0, 0);
            Assert.True(WallHit(m, new P3(8, y + 0.5, 4.5), dir) is double low && low < 2.0);
            Assert.True(WallHit(m, new P3(8, y + 1.5, 4.5), dir) is double high && high > 7, "a wall over the rails");
        }

        // a 14 × 10 m block of three storeys under a flat roof, with the parapet and roof cap given and no windows (the top
        // storey's wall is solid where a ray under the roof meets it); with stairs, up to the roof at (10, 5) under a bulkhead
        static (Site site, BuildingData b) Roofed(bool parapet, double? capH, double? capDepth, bool stairs = false)
        {
            var d = new StoreyDocument();
            var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.details.Clear(); b.shafts.Clear(); b.interior = true;
            b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList();
            b.style.parapet = parapet; b.style.capH = capH; b.style.capDepth = capDepth; b.style.windows = WindowType.None;
            if (stairs) b.shafts.Add(new CoreData { id = "c", type = CoreType.Stairs, x = 10, z = 5, roof = true });
            return (new Site(d.buildings), b);
        }

        /// <summary>
        /// A flat roof's edge is as high in the collider as what stands on it. A parapet is a parapet's height
        /// (<see cref="Lod0.Shared.RoofParapet"/>) over what it stands on: the walls, or a raised cap, which lifts it. Without
        /// one, the edge is the 30 cm curb (which a cap hanging from the edge leaves), a raised cap's top however high (the roof
        /// lifted with it, a lid), or round a cap set in, where the walls' top is closed flush with the roof, nothing over the
        /// roof. A ray from 2 m west of the west wall meets the edge under its top (a raised cap's overhang first, where it
        /// stands out) and goes over it above, and no wall on the edge stands higher. The walk model keeps every edge as a
        /// wall, as before: the roof's segments are the same lines whatever stands on them.
        /// </summary>
        /// <param name="face">How far out from the outline the ray at half the edge's height meets it.</param>
        [Theory]
        [InlineData(true, null, null, Lod0.Shared.RoofParapet, Dim.T_EXT)]              // the parapet
        [InlineData(false, null, null, Lod0.Shared.RoofCurb, Dim.T_EXT)]                // the curb
        [InlineData(false, -0.6, -0.3, Lod0.Shared.RoofCurb, Dim.T_EXT)]                // a cap hanging from the edge, under the curb
        [InlineData(false, 0.6, -0.25, 0.6, Dim.T_EXT + 0.25)]                          // a raised cap, the roof lifted to its top
        [InlineData(false, 2.0, -0.25, 2.0, Dim.T_EXT + 0.25)]                          // one higher than the roof storey's other walls
        [InlineData(true, 0.6, -0.25, 0.6 + Lod0.Shared.RoofParapet, Dim.T_EXT)]        // the parapet on a raised cap, met over it
        [InlineData(false, 0.5, 0.5, 0.0, 0.0)]                                         // a cap set in: nothing on the walls
        [InlineData(true, 0.5, 0.5, Lod0.Shared.RoofParapet, Dim.T_EXT)]                // a cap set in, inside the parapet
        public void ARoofsEdgeIsAsHighAsWhatStandsOnIt(bool parapet, double? capH, double? capDepth, double high, double face)
        {
            var (site, b) = Roofed(parapet, capH, capDepth);
            var shape = Lod0.BuildShape(site, b);
            var m = CollisionMesh.Build(site, b, shape);
            double roof = Derived.RoofY(b); var east = new P3(1, 0, 0);
            if (high > 0) Assert.Equal(2 - face, WallHit(m, new P3(-2, roof + high / 2, 5), east)!.Value, 6);
            Assert.True(WallHit(m, new P3(-2, roof + high + 0.05, 5), east) is not double over || over > 2 + 1e-6, "a wall over the edge");
            Assert.True(WallHit(m, new P3(-2, roof - 1.0, 5), east) is double below && Math.Abs(below - (2 - Dim.T_EXT)) < 1e-6, "the top storey's wall");
            // the highest wall on the west edge, which a cap set in stands 20 cm in from
            double top = Enumerable.Range(0, m.Tris).Where(t => m.Kind[t] == CollisionKind.Wall)
                .SelectMany(t => new[] { 0, 1, 2 }.Select(c => m.P[m.I[3 * t + c]])).Where(p => p.x < 0.1).Max(p => p.y);
            Assert.Equal(roof + high, top, 9);
            // the walk model's walls: the roof's edge as with a parapet
            var (site1, b1) = Roofed(true, capH, capDepth);
            static IEnumerable<(double, double, double, double, double)> Lines(List<Seg> sg) => sg.Select(s => (s.ax, s.az, s.bx, s.bz, s.r));
            int N = b.floors.Count;
            Assert.Equal(Lines(Lod0.BuildShape(site1, b1).Segs[N]), Lines(shape.Segs[N]));
            Assert.NotEmpty(shape.Segs[N]);
        }

        /// <summary>
        /// A cap set in on a flat roof is in the collider as high as it is: here 0.5 m high, its outer side 0.5 m in from the
        /// walls' face (20 cm in from the outline). On a roof nothing comes up through, it is a block: a ray from outside meets
        /// its side, one dropped on it lands on its top (the roof's kind, <see cref="CollisionKind.Roof"/>), and the walk model's
        /// roof is still a floor under it and between it and the walls. With stairs up to the roof, it is a ring the wall's
        /// thickness wide, met from outside and from the roof inside it, its top a wall's, the roof inside it clear. Nothing
        /// stands over its top.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ACapSetInIsAsHighAsItIs(bool stairs)
        {
            var (site, b) = Roofed(false, 0.5, 0.5, stairs);
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
            double roof = Derived.RoofY(b), h = 0.5; var ms = new[] { m }; var east = new P3(1, 0, 0);
            Assert.Equal(2.2, WallHit(m, new P3(-2, roof + h / 2, 5), east)!.Value, 6);   // its outer side
            Assert.True(WallHit(m, new P3(-2, roof + h + 0.05, 5), east) is not double over || over > 2.5 + 1e-6, "a wall over the cap");
            // between it and the walls: the roof, nothing over it
            Assert.Equal(roof, Down(ms, 0.1, roof + 5, 5, k => k == CollisionKind.Floor)!.Value, 9);
            Assert.Null(Down(ms, 0.1, roof + 5, 5, k => k != CollisionKind.Floor));
            Assert.Equal(roof, Down(ms, 3, roof + 5, 5, k => k == CollisionKind.Floor)!.Value, 9);   // the walk model's roof
            if (!stairs)
            {
                Assert.Equal(roof + h, Down(ms, 3, roof + 5, 5, k => k == CollisionKind.Roof)!.Value, 9);   // the block's top
                return;
            }
            Assert.Equal(roof + h, Down(ms, 0.35, roof + 5, 5, k => k == CollisionKind.Wall)!.Value, 9);   // the ring's top
            Assert.Equal(2.5, WallHit(m, new P3(3, roof + h / 2, 5), new P3(-1, 0, 0))!.Value, 6);       // its inner side, from the roof
            Assert.Null(Down(ms, 3, roof + 5, 5, k => k != CollisionKind.Floor));                            // inside it, the roof clear
        }

        /// <summary>
        /// A roof cap standing out past the walls, a slab on them or a fascia hanging from the roof's edge, is in the collider as
        /// far out as its Overhang sets it, and as high (or low) as it is: a ray from outside meets its outer side, one dropped
        /// at its outer edge, and round a corner, where its pieces meet on the mitre, lands on its top. Its pieces are LOD0's
        /// (<see cref="Lod0Shape.Blocks"/>, <see cref="EveryRoofCapMakesACleanCollider"/>); the walk model's walls are as with
        /// no cap.
        /// </summary>
        [Theory]
        [InlineData(0.4, 0.25)]    // a slab, the editor's
        [InlineData(0.4, 1.0)]
        [InlineData(-0.4, 0.3)]    // a fascia
        [InlineData(-0.4, 1.0)]
        public void ARoofCapStandsOutAsFarAsItDoes(double h, double overhang)
        {
            // a shell-only block against a neighbour
            var (_, b, site) = RoofCapTests.Block(RoofCapTests.Capped(h, -overhang), RoofCapTests.Rect, interior: false, neighbour: true);
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b)); var ms = new[] { m };
            double roof = Derived.RoofY(b), top = h > 0 ? roof + h : roof, mid = top - Math.Abs(h) / 2, edge = Dim.T_EXT + overhang;
            Assert.Equal(2 - edge, WallHit(m, new P3(-2, mid, 5), new P3(1, 0, 0))!.Value, 6);   // the west side's
            static bool Wall(CollisionKind k) => k == CollisionKind.Wall;
            Assert.Equal(top, Down(ms, 0.01 - edge, roof + 5, 5, Wall)!.Value, 9);
            Assert.Equal(top, Down(ms, 0.01 - edge, roof + 5, 0.05 - edge, Wall)!.Value, 9);   // past the south-west corner
            // as the walls the walk model has: the same with no cap
            var (_, plain, plainSite) = RoofCapTests.Block(RoofCapTests.Capped(h, -overhang), RoofCapTests.Rect, interior: false, neighbour: true);
            plain.style.capH = null;
            static IEnumerable<(double, double, double, double, double)> Lines(Lod0Shape s) => s.Segs.SelectMany(sg => sg).Select(g => (g.ax, g.az, g.bx, g.bz, g.r));
            Assert.Equal(Lines(Lod0.BuildShape(plainSite, plain)), Lines(Lod0.BuildShape(site, b)));
        }

        // a 14 × 10 m block of four storeys, walk-in or shell-only, with its stairs and lift or without: the stairs up to the roof,
        // flush against the west wall, where a walk-in building's bulkhead takes the parapet's place
        static (Site site, BuildingData b) Cored(bool interior, bool cores)
        {
            var d = new StoreyDocument();
            var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.details.Clear(); b.shafts.Clear();
            b.floors = Enumerable.Range(0, 4).Select(_ => new FloorData()).ToList();
            if (cores)
            {
                b.shafts.Add(new CoreData { id = "s", type = CoreType.Stairs, x = 1.3, z = 5, roof = true });
                b.shafts.Add(new CoreData { id = "l", type = CoreType.Lift, x = 10, z = 5 });
            }
            b.interior = interior;
            return (new Site(d.buildings), b);
        }

        /// <summary>
        /// A shell-only building keeps its stairs and lifts for when it is walk-in again, but builds none of them, and its
        /// collider has none of them either: no ramps, no hole in its roof (its floors as with none, and a ray dropped over the
        /// stairs lands on the roof), and its roof's edge a parapet's height along the core flush with its wall, as its parapet
        /// is drawn there, where a walk-in building's bulkhead stands. The walk model keeps them, as the prototype's does.
        /// Walk-in, the same building has its stairs in its collider, its roof open over them.
        /// </summary>
        [Fact]
        public void AShellOnlyBuildingsStairsAreNotInItsCollider()
        {
            var (site, b) = Cored(false, true);
            int N = b.floors.Count; double roof = Derived.RoofY(b); var east = new P3(1, 0, 0);
            Assert.Contains(Cores.CoreFlush(Derived.OutlineAt(b, N), b.shafts[0]), f => f != null);   // flush with the west wall, z 2.25 to 7.75
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
            var (site0, b0) = Cored(false, false);
            var m0 = CollisionMesh.Build(site0, b0, Lod0.BuildShape(site0, b0));
            Assert.DoesNotContain(m.Kind, k => k == CollisionKind.Stair);
            Assert.Equal(m0.Kind.Count(k => k == CollisionKind.Floor), m.Kind.Count(k => k == CollisionKind.Floor));
            Assert.Equal(roof, Down(new[] { m }, 1.0, roof + 0.5, 5.0, k => k == CollisionKind.Floor)!.Value, 9);
            // the roof's edge along the core as along the rest of the wall
            foreach (double z in new[] { 1.0, 5.0 })
            {
                Assert.Equal(2 - Dim.T_EXT, WallHit(m, new P3(-2, roof + 1.1, z), east)!.Value, 6);
                Assert.True(WallHit(m, new P3(-2, roof + Lod0.Shared.RoofParapet + 0.05, z), east) is not double over || over > 2 + 1e-6, "a wall over the parapet");
            }
            // walk-in: the stairs, and the roof open over them
            var (site1, b1) = Cored(true, true);
            var m1 = CollisionMesh.Build(site1, b1, Lod0.BuildShape(site1, b1));
            Assert.Contains(m1.Kind, k => k == CollisionKind.Stair);
            Assert.True(Down(new[] { m1 }, 1.0, roof + 0.5, 5.0, k => k == CollisionKind.Floor) is not double floor || floor < roof - 1, "a roof over the walk-in building's stairs");
            Assert.True(new PlayWorld(site1).SurfaceAt(1.0, 5.0, roof + 0.5) < roof - 1e-6);
        }

        /// <summary>
        /// Every roof cap of the old generator's themes (<see cref="RoofCapTests.Caps"/>), with and without the parapet, on a
        /// rectangle and an L (concave, with an edge a deep inset swallows), walk-in with stairs up to the roof or shell-only,
        /// alone and against a neighbour: no triangle of the collider is degenerate (it is cooked without cleaning,
        /// docs/PLAY.md §5), its floors and roofs face up, and the highest thing over the roof is its edge, its cap set in or a
        /// bulkhead (<see cref="CollisionMesh.RoofWallHeight"/>), as high as each says.
        /// </summary>
        [Theory, MemberData(nameof(RoofCapTests.Caps), MemberType = typeof(RoofCapTests))]
        public void EveryRoofCapMakesACleanCollider(double h, double depth, double strip, bool parapet)
        {
            foreach (var outline in new[] { RoofCapTests.Rect, RoofCapTests.Ell })
                foreach (bool interior in new[] { true, false })
                    foreach (bool neighbour in new[] { false, true })
                    {
                        var (_, b, site) = RoofCapTests.Block(RoofCapTests.Capped(h, depth, strip > 0 ? strip : null, parapet), outline, interior, core: interior, neighbour: neighbour);
                        var r = Lod0.Build(site, b, solids: true);
                        var m = CollisionMesh.Build(site, b, r.Shape);
                        string what = $"{(outline == RoofCapTests.Rect ? "a rectangle" : "an L")}{(interior ? " with stairs" : "")}{(neighbour ? " against a neighbour" : "")}";
                        for (int t = 0; t < m.Tris; t++)
                        {
                            var cr = P3.Cross(m.P[m.I[3 * t + 1]] - m.P[m.I[3 * t]], m.P[m.I[3 * t + 2]] - m.P[m.I[3 * t]]);
                            Assert.True(cr.Length > 1e-9, $"{what}: a degenerate {m.Kind[t]} triangle");
                            if (m.Kind[t] != CollisionKind.Wall) Assert.True(cr.y > 0, $"{what}: a {m.Kind[t]} triangle faces down");
                        }
                        var cap = RoofCap.Of(b);
                        // a cap standing out: its pieces are the boxes LOD0 draws it with, every one of them (along party walls and
                        // corners cut for a neighbour too)
                        var drawn = r.Op.Solids!.Where(s => s.Mitred).ToList();
                        foreach (var k in r.Shape.Blocks)
                            Assert.True(drawn.Any(s => s.F.Equals(k.F) && s.sA == k.S.s && s.kA == k.S.k && s.eB == k.E.s && s.kB == k.E.k && s.w0 == k.w0 && s.w1 == k.w1 && s.y0 == k.y0 && s.y1 == k.y1), $"{what}: a cap's piece LOD0 doesn't draw");
                        if (cap is RoofCap cs && !cs.inset)
                        {
                            double ya = cs.raised ? Derived.RoofY(b) : Derived.RoofY(b) - cs.h, yb = cs.raised ? Derived.RoofY(b) + cs.h : Derived.RoofY(b);
                            Assert.Equal(drawn.Count(s => s.w0 == cs.wIn && s.w1 == cs.wOut && s.y0 == ya && s.y1 == yb), r.Shape.Blocks.Count);
                        }
                        else Assert.Empty(r.Shape.Blocks);
                        double want = Math.Max(Math.Max(Lod0.Shared.RoofEdgeH(parapet, cap), 0), cap is RoofCap c && c.inset ? c.h : 0);
                        if (interior) want = Math.Max(want, CollisionMesh.RoofWallHeight);   // the stairs' bulkhead
                        Assert.True(Math.Abs(m.P.Max(p => p.y) - (Derived.RoofY(b) + want)) < 1e-9, $"{what}: the highest point {m.P.Max(p => p.y) - Derived.RoofY(b):0.###} m over the roof, not {want:0.###}");
                    }
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
