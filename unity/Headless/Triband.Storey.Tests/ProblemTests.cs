using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>The problem list (<see cref="Problems"/>). Storey's own.</summary>
    public class ProblemTests
    {
        readonly ITestOutputHelper output; public ProblemTests(ITestOutputHelper o) { output = o; }

        static BuildingData Box(StoreyDocument d, double x, double z, int floors = 3, double w = 14, double dd = 10)
        {
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.pos = new Vec2(x, z);
            b.footprint = new() { new Vec2(0, 0), new Vec2(w, 0), new Vec2(w, dd), new Vec2(0, dd) };
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear();
            b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
            b.entrances.Add(new EntranceData { edge = 0, t = 0.5, k = 0 });
            return b;
        }

        static System.Collections.Generic.List<Problem> Check(StoreyDocument d) => Problems.Check(new Site(d.buildings));

        [Fact]
        public void AGoodBuildingHasNone()
        {
            var d = new StoreyDocument(); var b = Box(d, 0, 0);
            Assert.NotNull(Shafts.Place(b, 0, new Vec2(3, 6), CoreType.Stairs, 0, Shafts.NewId(d)));
            var ps = Check(d);
            Assert.True(ps.Count == 0, string.Join("\n", ps));
        }

        [Fact]
        public void FloorsWithoutStairsCantBeReached()
        {
            var d = new StoreyDocument(); Box(d, 0, 0);
            var ps = Check(d);
            Assert.Equal(new[] { 1, 2 }, ps.Where(p => p.code == "unreached").Select(p => p.k).OrderBy(k => k));
            Assert.Contains("no stairs", ps.First(p => p.code == "unreached").message);
        }

        [Fact]
        public void ARoomWithNoDoor()
        {
            var d = new StoreyDocument(); var b = Box(d, 0, 0, 1);
            b.floors[0].walls.Add(new WallData { a = new Vec2(9, 0), b = new Vec2(9, 10) });   // a 5 × 10 m room with no door
            var p = Check(d).Single();
            Assert.Equal("unreached-part", p.code);
            Assert.InRange(p.x, 9, 14); Assert.InRange(p.z, 0, 10);
            Assert.Contains("about", p.message);
            b.floors[0].walls[0].doors.Add(new DoorData { t = 0.5 });
            Assert.Empty(Check(d));
        }

        [Fact]
        public void AWallInFrontOfTheStairs()
        {
            // the demo's Row House: a wall 10 cm in front of the stairs on the upper floors shuts the stairs in
            var d = new StoreyDocument(); var b = Box(d, 0, 0, 3, 6, 8);
            b.shafts.Add(new CoreData { id = "s1", type = CoreType.Stairs, x = 3, z = 4.9 });
            foreach (var f in b.floors.Skip(1)) f.walls.Add(new WallData { a = new Vec2(0, 2.2), b = new Vec2(6, 2.2), doors = { new DoorData { t = 0.25 } } });
            var ps = Check(d).Where(p => p.code == "unreached").ToList();
            Assert.Equal(2, ps.Count);
            Assert.All(ps, p => Assert.Contains("closes them in", p.message));
        }

        [Fact]
        public void NoWayIn()
        {
            var d = new StoreyDocument(); var b = Box(d, 0, 0, 1); b.entrances.Clear();
            var p = Check(d).Single();
            Assert.Equal("no-door", p.code);
        }

        [Fact]
        public void ABridgeIsAWayIn()
        {
            var d = new StoreyDocument();
            var a = Box(d, 0, 0, 3, 12, 10); a.shafts.Add(new CoreData { id = "s1", type = CoreType.Stairs, x = 3, z = 5 });
            var b = Box(d, 20, 0, 3, 12, 10); b.entrances.Clear(); b.shafts.Add(new CoreData { id = "s2", type = CoreType.Stairs, x = 9, z = 5 });
            Assert.Contains(Check(d), p => p.code == "no-door" && p.buildingId == b.id);
            Assert.Null(BridgeEdits.Add(new Site(d.buildings), a, 2, new Vec2(12, 5)).why);
            var ps = Check(d);
            Assert.True(ps.Count == 0, string.Join("\n", ps));
        }

        [Fact]
        public void ALiftPassingFilledFloors()
        {
            var d = new StoreyDocument(); var b = Box(d, 0, 0, 6);
            for (int k = 1; k <= 4; k++) b.floors[k].filled = true;
            Assert.NotNull(Shafts.Place(b, 0, new Vec2(2, 7), CoreType.Lift, 0, Shafts.NewId(d)));
            var ps = Check(d);
            Assert.True(ps.Count == 0, string.Join("\n", ps));
        }

        [Fact]
        public void ACourtyardIsReachedThroughItsDoor()
        {
            var d = new StoreyDocument(); var b = Box(d, 0, 0, 1, 20, 16);
            b.voids.Add(new VoidData { id = "v1", shape = new() { new Vec2(6, 5), new Vec2(14, 5), new Vec2(14, 11), new Vec2(6, 11) } });
            var ps = Check(d);
            Assert.True(ps.Count == 0, string.Join("\n", ps));
        }

        [Fact]
        public void OverlapsDoorsToNowhereAndBrokenBridges()
        {
            var d = new StoreyDocument();
            var a = Box(d, 0, 0, 1); var b = Box(d, 10, 0, 1);   // 4 m into each other
            a.entrances.Add(new EntranceData { edge = 2, t = 0.5, k = 0 });
            var c = Box(d, 40, 0, 3); c.shafts.Add(new CoreData { id = "s1", type = CoreType.Stairs, x = 3, z = 5 });
            c.entrances.Add(new EntranceData { edge = 1, t = 0.5, k = 2 });   // no terrace there
            c.bridges.Add(new BridgeData { id = "br1", to = a.id, k = 2, at = new Vec2(0, 5), toK = 1 });
            var ps = Check(d);
            Assert.Contains(ps, p => p.code == "overlap" && p.message.Contains("40 m²"));
            Assert.Contains(ps, p => p.code == "door-nowhere" && p.buildingId == c.id && p.k == 2);
            Assert.Contains(ps, p => p.code == "bridge" && p.buildingId == c.id);
        }

        [Theory]
        [InlineData("demo")]
        [InlineData("variants")]
        public void TheCorporaAreCheckedQuickly(string corpus)
        {
            var (doc, world) = PlayWorldTests.World(corpus);
            foreach (var b in doc.buildings) world.Lod0Of(b);   // built already in the editor
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ps = Problems.Check(world.Site, world);
            sw.Stop();
            output.WriteLine($"{corpus}: {doc.buildings.Count} buildings, {ps.Count} problems, {sw.Elapsed.TotalMilliseconds:0} ms");
            foreach (var p in ps.Take(15)) output.WriteLine("  " + p);
            if (corpus == "demo")
            {
                // what the demo street really has: the Row House's stairs walled in above the ground floor, and Linden Court's
                // room behind its lift, reached only past a 40 cm gap
                Assert.Equal(new[] { 1, 2, 3 }, ps.Where(p => p.building == "Row House" && p.code == "unreached").Select(p => p.k));
                Assert.Equal(10, ps.Count(p => p.building == "Linden Court" && p.code == "unreached-part"));
                Assert.Equal(13, ps.Count);
            }
            Assert.True(sw.Elapsed.TotalMilliseconds < 3000);
        }
    }
}
