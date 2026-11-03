using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The cutaway's walls facing the camera (<see cref="OcclusionSettings.wallsFacingCamera"/>, docs/PLAY.md §4): every wall
    /// whose outside the camera looks at goes down, not only those between the camera and the player; the far walls stand,
    /// and nothing moves as the player walks about. A partition faces both ways and goes down only between the camera and
    /// the player, and a neighbour's party wall faces out of the neighbour. Storey's own.
    /// </summary>
    public class FacingWallsTests
    {
        const double Dt = 1.0 / 30;
        /// <summary>The camera west of the player, as in the screenshot the option was asked for with.</summary>
        const double West = -Math.PI / 2;

        /// <summary>
        /// Alder House, as a user's street lays it out: one walk-in storey on a nine-sided outline, with a notch in its west
        /// side (edges 1 and 2) and another in its south (edges 4 and 5), its door on edge 3.
        /// </summary>
        static (PlayWorld world, BuildingData b) AlderHouse()
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.name = "Alder House"; b.pos = new Vec2(101.57, -388.04); b.elevation = 6.35;
            b.footprint = new List<Vec2> { new Vec2(-7.43, 8.34), new Vec2(-7.43, 3.82), new Vec2(-4.28, 0.93), new Vec2(-7, -1.76), new Vec2(-2.88, -5.86),
                new Vec2(-0.24, -3.1), new Vec2(2.35, -5.65), new Vec2(9.31, 1.3), new Vec2(-2.58, 13.14) };
            Plain(b, storeys: 1); b.groundHeight = 3.1;
            b.entrances.Add(new EntranceData { edge = 3, t = 0.4934 });
            return (new PlayWorld(new Site(d.buildings)), b);
        }

        /// <summary>A walk-in building of <paramref name="storeys"/> plain storeys: no doors, details, cores, cut corners or rooms.</summary>
        static void Plain(BuildingData b, int storeys)
        {
            b.floors = Enumerable.Range(0, storeys).Select(_ => new FloorData()).ToList();
            b.interior = true; b.floorHeight = 3;
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear(); b.corners.Clear(); b.windows.Clear(); b.voids.Clear(); b.bridges.Clear();
        }

        /// <summary>The outline edge each of b's walls on storey k stands on (its cutaway data is the edge's), -1 for none.</summary>
        static int[] EdgeOf(PlayWorld world, BuildingData b, int k)
        {
            var shape = world.ShapeOf(b); var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0;
            var Wp = fp.Select(p => new Vec2(p.x + b.pos.x, p.z + b.pos.z)).ToList();
            var ctx = Enumerable.Range(0, Wp.Count).Select(i => { var e = Geo.EdgeInfo(Wp, i, ccw); return Geo.WallCtx(e.a, e.u, e.w, e.L); }).ToList();
            double y = Derived.FloorBase(b, k);
            return shape.Walls.Select((w, j) => shape.WallBase[j] != y ? -1 : ctx.FindIndex(c => c.SequenceEqual(w.W))).ToArray();
        }

        /// <summary>
        /// The edges whose walls are all the way down. Every wall on an edge slides as one, its dressing with its panel: a
        /// wall partly down, or an edge with some of its walls down and some up, fails.
        /// </summary>
        static SortedSet<int> Down(OcclusionCore core, BuildingData b, int[] edgeOf)
        {
            var slides = new Dictionary<int, HashSet<double>>();
            for (int j = 0; j < edgeOf.Length; j++)
            {
                if (edgeOf[j] < 0) continue;
                double a = core.Walls.TryGetValue((b.id, j), out var v) ? v : 0;
                if (!slides.TryGetValue(edgeOf[j], out var s)) slides[edgeOf[j]] = s = new HashSet<double>();
                s.Add(a);
            }
            foreach (var kv in slides) Assert.True(kv.Value.Count == 1 && (kv.Value.Single() == 0 || kv.Value.Single() == 1), $"edge {kv.Key}'s walls at {string.Join(", ", kv.Value)}");
            return new SortedSet<int>(slides.Where(kv => kv.Value.Single() == 1).Select(kv => kv.Key));
        }

        /// <summary>The camera's direction from the player (in plan) at <paramref name="yaw"/>, as <see cref="FollowCamera.Position"/> puts it.</summary>
        static (double x, double z) Dir(double yaw) => (Math.Sin(yaw), Math.Cos(yaw));

        /// <summary>
        /// The edges of storey k's outline whose outside faces the direction, by more than <paramref name="clear"/> (the
        /// cosine; by default the core's margin, which a wall coming down from standing waits for).
        /// </summary>
        static SortedSet<int> Facing(BuildingData b, int k, (double x, double z) dir, double clear = OcclusionCore.FacingMargin)
        {
            var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0; var r = new SortedSet<int>();
            for (int i = 0; i < fp.Count; i++) { var e = Geo.EdgeInfo(fp, i, ccw); if (e.w.x * dir.x + e.w.z * dir.z > clear) r.Add(i); }
            return r;
        }

        /// <summary>The player stood at (lx, lz) on b's storey k, the camera trailing at <paramref name="yaw"/> for a second: the core after.</summary>
        static OcclusionCore Stand(PlayWorld world, BuildingData b, double lx, double lz, double yaw, bool facing, int k = 0)
        {
            var core = new OcclusionCore(world, new OcclusionSettings { wallsFacingCamera = facing });
            var p = new PlayerState(); p.Spawn(b.pos.x + lx, Derived.FloorBase(b, k), b.pos.z + lz);
            var cam = new FollowCamera { yaw = yaw }; cam.Snap(p);
            for (int i = 0; i < 30; i++) { cam.Follow(p, Dt); core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), Dt); }
            Assert.True(core.View.active == b && core.View.cutOn, $"the player at ({lx}, {lz}) isn't in {b.name}, cut away");
            return core;
        }

        static string Show(IEnumerable<int> edges) => "{" + string.Join(", ", edges) + "}";

        /// <summary>
        /// The view the option was asked for with: the player in Alder House's main room, the camera west of them. The
        /// cutaway takes down the one wall between the camera and the player (edge 0), and leaves the walls either side of it
        /// standing (edges 8, 1 and 2), though the camera looks at their outsides and they hide the room. With the walls
        /// facing the camera, those go down too, with the rest of the west side's (3 and 5, out of shot), and the walls facing
        /// away (4, 6 and 7) stand.
        /// </summary>
        [Fact]
        public void TheWallsFacingTheCameraGoDownAndTheFarWallsStand()
        {
            var (world, b) = AlderHouse(); var edgeOf = EdgeOf(world, b, 0);
            Assert.All(Enumerable.Range(0, b.footprint.Count), i => Assert.Contains(i, edgeOf));
            var off = Down(Stand(world, b, -3, 5.5, West, false), b, edgeOf);
            Assert.True(off.SetEquals(new[] { 0 }), $"without the walls facing the camera: {Show(off)} down");
            var on = Down(Stand(world, b, -3, 5.5, West, true), b, edgeOf);
            Assert.True(on.SetEquals(new[] { 0, 1, 2, 3, 5, 8 }), $"with the walls facing the camera: {Show(on)} down");
            Assert.True(Facing(b, 0, Dir(West)).SetEquals(on));
        }

        /// <summary>
        /// From every side, and from anywhere in the building: the walls down are those between the camera and the player,
        /// as without the option, and every wall facing the camera. The camera is never edge-on to a wall here: Alder House's
        /// walls run every 45° and it looks between them.
        /// </summary>
        [Fact]
        public void FromEverySideTheWallsDownAreThoseInTheWayAndThoseFacingTheCamera()
        {
            var (world, b) = AlderHouse(); var edgeOf = EdgeOf(world, b, 0);
            int added = 0;
            foreach (var (lx, lz) in new[] { (-3.0, 5.5), (1.5, -4.0), (-4.5, -2.0), (4.0, 2.5) })
                for (int i = 0; i < 8; i++)
                {
                    double yaw = (22.5 + 45 * i) * Math.PI / 180;
                    var off = Down(Stand(world, b, lx, lz, yaw, false), b, edgeOf);
                    var on = Down(Stand(world, b, lx, lz, yaw, true), b, edgeOf);
                    var want = new SortedSet<int>(off); want.UnionWith(Facing(b, 0, Dir(yaw)));
                    Assert.True(on.SetEquals(want), $"at ({lx}, {lz}), the camera at {yaw * 180 / Math.PI:0.#}°: {Show(on)} down, {Show(want)} wanted");
                    added += on.Count - off.Count;
                }
            Assert.True(added > 0);
        }

        /// <summary>
        /// The player runs round Alder House with the camera trailing, west of them: without the option the walls slide up
        /// and down as the sightline sweeps across them; with it, the walls facing the camera are down from the first second
        /// to the last, and no wall moves.
        /// </summary>
        [Fact]
        public void NothingMovesAsThePlayerWalksAbout()
        {
            var (world, b) = AlderHouse(); var edgeOf = EdgeOf(world, b, 0);
            var path = new[] { (-5.0, 7.0), (2.5, 7.0), (3.0, 1.0), (-3.0, -1.0), (-5.0, 7.0) };
            foreach (bool facing in new[] { true, false })
            {
                var core = new OcclusionCore(world, new OcclusionSettings { wallsFacingCamera = facing });
                var p = new PlayerState(); p.Spawn(b.pos.x + path[0].Item1, b.elevation, b.pos.z + path[0].Item2);
                var cam = new FollowCamera { yaw = West }; cam.Snap(p);
                for (int i = 0; i < 30; i++) { cam.Follow(p, Dt); core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), Dt); }
                var first = new Dictionary<(string, int), double>(core.Walls);
                int moved = 0;
                for (int s = 0; s + 1 < path.Length; s++)
                {
                    var (x0, z0) = path[s]; var (x1, z1) = path[s + 1]; int n = (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / 0.1);   // 3 m/s
                    for (int i = 1; i <= n; i++)
                    {
                        double t = (double)i / n; p.Spawn(b.pos.x + x0 + (x1 - x0) * t, b.elevation, b.pos.z + z0 + (z1 - z0) * t);
                        cam.Follow(p, Dt); core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), Dt);
                        Assert.Same(b, core.View.active);
                        if (core.Walls.Count != first.Count || core.Walls.Any(kv => !first.TryGetValue(kv.Key, out var a) || a != kv.Value)) moved++;
                    }
                }
                if (facing)
                {
                    Assert.True(moved == 0, $"with the walls facing the camera, walls moved in {moved} frames");
                    Assert.True(Down(core, b, edgeOf).SetEquals(Facing(b, 0, Dir(West))));
                }
                else Assert.True(moved > 0, "without them, no wall moved: the walk doesn't show the difference");
            }
        }

        /// <summary>
        /// Edge 0 faces west, and the camera is turned to look along it, from the north. A wall starts down once the camera
        /// is 2° round onto its outside (<see cref="OcclusionCore.FacingMargin"/>), and once down, goes back up only as the
        /// camera turns back past its plane: a camera wavering about a wall seen edge-on doesn't make it drop and rise.
        /// </summary>
        [Fact]
        public void AWallSeenEdgeOnWaitsForTheCameraToTurnPastIt()
        {
            var (world, b) = AlderHouse(); var edgeOf = EdgeOf(world, b, 0);
            var core = new OcclusionCore(world, new OcclusionSettings { wallsFacingCamera = true });
            var p = new PlayerState(); p.Spawn(b.pos.x - 3, b.elevation, b.pos.z + 5.5);
            var cam = new FollowCamera(); cam.Snap(p);
            bool Edge0Down(double degrees, int frames = 30)
            {
                cam.yaw = degrees * Math.PI / 180;   // 0: the camera north of the player; less, round to the west
                for (int i = 0; i < frames; i++) { cam.Follow(p, Dt); core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), Dt); }
                return Down(core, b, edgeOf).Contains(0);
            }
            Assert.False(Edge0Down(30));
            Assert.False(Edge0Down(-1));    // onto its outside, within the margin
            for (int i = 0; i < 20; i++) Assert.False(Edge0Down(i % 2 == 0 ? 1 : -1, 3));   // wavering
            Assert.True(Edge0Down(-3));     // past the margin
            Assert.True(Edge0Down(-1));     // back within it: still down
            for (int i = 0; i < 20; i++) Assert.True(Edge0Down(-0.5 - (i % 2), 3));
            Assert.False(Edge0Down(1));     // back past its plane
        }

        [Fact]
        public void TheFacingTestReadsTheNormalAndItsSide()
        {
            // a wall along x from (0, 0), 4 m long, its normal +z (scaled by 1 + length, as LOD0 writes it)
            double[] wall = { 0, 0, 0, 5 };
            Assert.True(OcclusionCore.WallFaces(wall, 0, 1));
            Assert.False(OcclusionCore.WallFaces(wall, 0, -1));
            Assert.True(OcclusionCore.WallFaces(wall, 0, -1, outward: false));   // a neighbour's: its outside is the other side
            Assert.False(OcclusionCore.WallFaces(wall, 1, 0));                   // edge-on
            Assert.True(OcclusionCore.WallFaces(wall, Math.Cos(0.05), Math.Sin(0.05), margin: 0.035));
            Assert.False(OcclusionCore.WallFaces(wall, Math.Cos(0.02), Math.Sin(0.02), margin: 0.035));
            Assert.False(OcclusionCore.WallFaces(MeshBuilder.Zero4, 0, 1));      // no data
        }

        /// <summary>
        /// A partition faces both ways: the wall between a building's two rooms goes down only with the player past it, as
        /// without the option, whichever way it was drawn (its normal's sign is the way it was drawn), while the outside walls
        /// facing the camera are down. The generator marks the room walls, and nothing else of the storey's walls.
        /// </summary>
        [Fact]
        public void APartitionGoesDownOnlyBetweenTheCameraAndThePlayer()
        {
            foreach (bool flipped in new[] { false, true })
            {
                var d = new StoreyDocument();
                var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(40, 20);
                b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(12, 0), new Vec2(12, 8), new Vec2(0, 8) };
                Plain(b, storeys: 1);
                var wall = new WallData { a = new Vec2(6, 0), b = new Vec2(6, 8) }; wall.doors.Add(new DoorData { t = 0.5 });
                if (flipped) (wall.a, wall.b) = (wall.b, wall.a);
                b.floors[0].walls.Add(wall);
                var world = new PlayWorld(new Site(d.buildings));
                var shape = world.ShapeOf(b);
                // the room wall's walls (on the line x = 6) are the partitions, and only they are
                var onLine = Enumerable.Range(0, shape.Walls.Count).Where(j => Math.Abs(shape.Walls[j].W[0] - (b.pos.x + 6)) < 1e-9 && Math.Abs(shape.Walls[j].W[3]) < 1e-9).ToList();
                Assert.NotEmpty(onLine);
                Assert.Equal(onLine, shape.Partitions);
                var edgeOf = EdgeOf(world, b, 0);
                bool PartitionDown(OcclusionCore c) => onLine.All(j => c.Walls.TryGetValue((b.id, j), out var a) && a == 1);
                bool PartitionUp(OcclusionCore c) => onLine.All(j => !c.Walls.ContainsKey((b.id, j)));
                // the camera west of the player: in the west room the partition stands behind them, in the east room it is in the way
                var west = Stand(world, b, 3, 4, West, true);
                Assert.True(PartitionUp(west), $"drawn {(flipped ? "north to south" : "south to north")}: the partition is down behind the player");
                Assert.True(Down(west, b, edgeOf).SetEquals(Facing(b, 0, Dir(West))));
                Assert.True(PartitionDown(Stand(world, b, 9, 4, West, true)));
                // and from the east, the other way round
                Assert.True(PartitionUp(Stand(world, b, 9, 4, -West, true)));
                Assert.True(PartitionDown(Stand(world, b, 3, 4, -West, true)));
            }
        }

        /// <summary>
        /// Over whole layouts: every room wall the generator makes is a partition, every partition has an inside either side
        /// of it, and no outside wall is one.
        /// </summary>
        [Theory]
        [InlineData("demo")]
        [InlineData("variants")]
        public void ThePartitionsAreTheWallsWithAnInsideEitherSide(string corpus)
        {
            var site = new Site(PrototypeJson.Read(Fixtures.Text(corpus + ".json")).Document.buildings);
            int rooms = 0, outside = 0, partitions = 0;
            foreach (var b in site.Buildings)
            {
                var shape = Lod0.BuildShape(site, b); var parts = new HashSet<int>(shape.Partitions); partitions += parts.Count;
                Assert.True(shape.Partitions.SequenceEqual(shape.Partitions.Distinct().OrderBy(j => j)) && shape.Partitions.All(j => j >= 0 && j < shape.Walls.Count), $"{b.name}: partitions out of order or past the walls");
                var shared = new Lod0.Shared(site, b);
                for (int k = 0; k < b.floors.Count; k++)
                {
                    double y = Derived.FloorBase(b, k);
                    if (!Derived.ShellAt(b, k))
                        foreach (var r in shared.RoomWalls(k))
                        {
                            var W = Geo.WallCtx(r.a, r.u, r.w, r.L); rooms++;
                            int j = Enumerable.Range(0, shape.Walls.Count).Where(i => shape.WallBase[i] == y && shape.Walls[i].W.SequenceEqual(W)).DefaultIfEmpty(-1).First();
                            Assert.True(j >= 0, $"{b.name}: storey {k}'s room wall at {r.a} has no wall");
                            Assert.True(parts.Contains(j), $"{b.name}: storey {k}'s room wall at {r.a} isn't a partition");
                        }
                    // the storey's outside walls: on an edge of its outline
                    var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0;
                    var Wp = fp.Select(p => new Vec2(p.x + b.pos.x, p.z + b.pos.z)).ToList();
                    for (int i = 0; i < Wp.Count; i++)
                    {
                        var e = Geo.EdgeInfo(Wp, i, ccw); var W = Geo.WallCtx(e.a, e.u, e.w, e.L);
                        for (int j = 0; j < shape.Walls.Count; j++)
                            if (shape.WallBase[j] == y && shape.Walls[j].W.SequenceEqual(W)) { outside++; Assert.False(parts.Contains(j), $"{b.name}: storey {k}'s outside wall on edge {i} is a partition"); }
                    }
                }
                // a partition has an inside either side: a step off its middle either way is in the storey's outline
                foreach (int j in shape.Partitions)
                {
                    var W = shape.Walls[j].W; double nl = Math.Sqrt(W[2] * W[2] + W[3] * W[3]), nx = W[2] / nl, nz = W[3] / nl, half = (nl - 1) / 2;
                    double mx = W[0] + nz * half - b.pos.x, mz = W[1] - nx * half - b.pos.z;
                    int k = Enumerable.Range(0, b.floors.Count + 1).First(q => Derived.FloorBase(b, q) == shape.WallBase[j]);
                    var fp = Derived.OutlineAt(b, Math.Min(k, b.floors.Count - 1));
                    Assert.True(Geo.Pip(fp, mx + nx * 0.3, mz + nz * 0.3) && Geo.Pip(fp, mx - nx * 0.3, mz - nz * 0.3), $"{b.name}: partition {j} on storey {k} has an outside");
                }
            }
            // the demo has rooms; the variants' partitions are their stairs' middle walls
            Assert.True(partitions > rooms && outside > 0, $"{corpus}: {partitions} partitions, {rooms} of them room walls, {outside} outside walls: nothing to test");
        }

        /// <summary>
        /// Two walk-in buildings wall to wall, the player in the west one. The party wall is the building's too, and its
        /// outside is the neighbour's side, whichever of the two builds it: with the camera north-east, looking at it from
        /// over the neighbour, it goes down, though the sightline to the player crosses the north wall and not it; with the
        /// camera west, it is the room's far wall and stands.
        /// </summary>
        [Fact]
        public void APartyWallFacesOutOfTheBuildingThePlayerIsIn()
        {
            var d = new StoreyDocument();
            var A = Buildings.Add(d, "rect", new Vec2(0, 0)); var B = Buildings.Add(d, "rect", new Vec2(0, 0));
            foreach (var (b, x) in new[] { (A, 0.0), (B, 10.0) })
            {
                b.pos = new Vec2(x, 0); b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 8), new Vec2(0, 8) };
                Plain(b, storeys: 1);
            }
            var world = new PlayWorld(new Site(d.buildings));
            var site = world.Site; int ia = site.IndexOf(A), ib = site.IndexOf(B);
            // the party wall's walls: A's built for B, or B's built for A
            var party = new List<(string id, int j)>();
            foreach (var (b, mate) in new[] { (A, ib), (B, ia) })
            {
                var shape = world.ShapeOf(b);
                for (int j = 0; j < shape.Walls.Count; j++)
                    if (shape.Walls[j].K >= 8 && (shape.Walls[j].K >> 3) - 1 == mate && shape.WallBase[j] == Derived.FloorBase(b, 0)) party.Add((b.id, j));
            }
            Assert.NotEmpty(party);
            bool PartyDown(OcclusionCore c) => party.All(w => c.Walls.TryGetValue(w, out var a) && a == 1);
            bool PartyUp(OcclusionCore c) => party.All(w => !c.Walls.ContainsKey(w));
            double northEast = 30 * Math.PI / 180;   // 60° round from east to north: the sightline leaves by the north wall
            Assert.True(PartyUp(Stand(world, A, 3, 1, northEast, false)), "the sightline crosses the party wall: the test shows nothing");
            Assert.True(PartyDown(Stand(world, A, 3, 1, northEast, true)));
            Assert.True(PartyUp(Stand(world, A, 3, 4, West, true)));
            // the player in the east one: the other way round
            Assert.True(PartyUp(Stand(world, B, 7, 1, -northEast, false)), "the sightline crosses the party wall: the test shows nothing");
            Assert.True(PartyDown(Stand(world, B, 7, 1, -northEast, true)));
            Assert.True(PartyUp(Stand(world, B, 7, 4, -West, true)));
        }
    }
}
