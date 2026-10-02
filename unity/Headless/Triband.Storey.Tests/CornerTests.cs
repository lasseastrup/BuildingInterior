using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Chamfered and rounded corners (<see cref="Outlines.Corner"/>). Storey's own.</summary>
    public class CornerTests
    {
        static (StoreyDocument d, BuildingData b) Box()
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.footprint = new() { new Vec2(0, 0), new Vec2(12, 0), new Vec2(12, 10), new Vec2(0, 10) };
            b.pos = new Vec2(0, 0);
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear();
            foreach (var f in b.floors) f.walls.Clear();
            return (d, b);
        }

        static double Area(BuildingData b) => Math.Abs(Geo.Area2(b.footprint)) / 2;

        [Fact]
        public void AChamferCutsTheCornerOff()
        {
            var (_, b) = Box();
            Assert.Equal(OutlineIssue.None, Outlines.Corner(b, 0, 2, CornerShape.Chamfer, 3, out int first, out int count));
            Assert.Equal(5, b.footprint.Count);
            Assert.Equal(1, count); Assert.Equal(2, first);
            Assert.Equal(120 - 4.5, Area(b), 6);
            Assert.Equal(3 * Math.Sqrt(2), Tiers.EdgeLen(b.footprint, first), 2);
        }

        [Fact]
        public void ARoundedCornerIsAnArcOfThatRadius()
        {
            var (_, b) = Box();
            Assert.Equal(OutlineIssue.None, Outlines.Corner(b, 0, 2, CornerShape.Round, 4, out int first, out int count));
            Assert.True(count >= 6, $"{count} steps");
            // every new corner sits 4 m from the arc's centre (8, 6), and the area lost is close to (1 − π/4)·16
            for (int j = first; j <= first + count; j++) { var p = b.footprint[j]; Assert.Equal(4, Tiers.Hypot(p.x - 8, p.z - 6), 1); }
            Assert.InRange(120 - Area(b), (1 - Math.PI / 4) * 16, (1 - Math.PI / 4) * 16 * 1.12);
            Assert.All(Enumerable.Range(0, b.footprint.Count), i => Assert.True(Tiers.EdgeLen(b.footprint, i) >= 0.3));
        }

        [Fact]
        public void DoorsAndBlankWallsKeepTheirPlaces()
        {
            var (_, b) = Box();
            b.entrances.Add(new EntranceData { edge = 1, t = 0.2, k = 0 });   // (12, 2): stays
            b.entrances.Add(new EntranceData { edge = 2, t = 0.25, k = 0 });  // (9, 10): stays, on the edge after the cut
            b.entrances.Add(new EntranceData { edge = 1, t = 0.95, k = 0 });  // (12, 9.5): inside the cut, goes
            b.blank.Add(3);
            Outlines.Corner(b, 0, 2, CornerShape.Chamfer, 2, out _, out _);
            Assert.Equal(2, b.entrances.Count);
            Vec2 At(EntranceData e) { var a = b.footprint[e.edge]; var c = b.footprint[(e.edge + 1) % b.footprint.Count]; return new Vec2(a.x + (c.x - a.x) * e.t, a.z + (c.z - a.z) * e.t); }
            var p0 = At(b.entrances[0]); var p1 = At(b.entrances[1]);
            Assert.Equal(12, p0.x, 6); Assert.Equal(2, p0.z, 6);
            Assert.Equal(9, p1.x, 6); Assert.Equal(10, p1.z, 6);
            Assert.Equal(new[] { 4 }, b.blank);   // the old edge 3 is edge 4 now
        }

        [Fact]
        public void TooBigIsRefused()
        {
            var (_, b) = Box();
            Assert.NotEqual(OutlineIssue.None, Outlines.Corner(b, 0, 2, CornerShape.Chamfer, 9.9, out _, out _));
            Assert.Equal(4, b.footprint.Count);
        }

        [Fact]
        public void EveryCornerAtOnce()
        {
            var (_, b) = Box();
            Assert.Equal(4, Outlines.AllCorners(b, 0, CornerShape.Chamfer, 1.5));
            Assert.Equal(8, b.footprint.Count);
            Assert.True(Tiers.SimplePoly(b.footprint));
        }

        [Fact]
        public void TheFirstCornerWrapsRound()
        {
            var (_, b) = Box();
            b.entrances.Add(new EntranceData { edge = 3, t = 0.5, k = 0 });   // (0, 5) on the closing edge
            Assert.Equal(OutlineIssue.None, Outlines.Corner(b, 0, 0, CornerShape.Chamfer, 2, out int first, out _));
            Assert.Equal(0, first);
            var e = b.entrances[0]; var a = b.footprint[e.edge]; var c = b.footprint[(e.edge + 1) % b.footprint.Count];
            Assert.Equal(0, a.x + (c.x - a.x) * e.t, 6); Assert.Equal(5, a.z + (c.z - a.z) * e.t, 6);
        }

        [Fact]
        public void ACornerEntranceOpensOnAWideChamfer()
        {
            var (d, b) = Box();
            Outlines.Corner(b, 0, 2, CornerShape.Chamfer, 3, out int first, out _);
            b.entrances.Add(new EntranceData { edge = first, t = 0.5, k = 0 });
            var site = new Site(d.buildings);
            var L = Tiers.EdgeLen(b.footprint, first);
            var ops = Facade.Ops(site, b, 0, first, L, 0.3, L - 0.3);
            Assert.Contains(ops, o => o.door);
        }

        [Theory]
        [InlineData(CornerShape.Chamfer)]
        [InlineData(CornerShape.Round)]
        public void ItBuildsCleanly(CornerShape shape)
        {
            var (d, b) = Box();
            Outlines.AllCorners(b, 0, shape, 2.5);
            var site = new Site(d.buildings);
            var r = Lod0.Build(site, b, solids: true);
            var report = CoplanarCheck.Run(r.Op);
            Assert.True(report.Overlaps.Count == 0, string.Join("\n", report.Overlaps.Take(10)));
            Assert.Empty(CoplanarCheck.Run(Lod1.Build(site, b, solids: true)).Overlaps);
            // the walls are closed: a player walking from the middle into a cut corner stops at its wall
            var w = new PlayWorld(site);
            var p = new PlayerState(); p.Spawn(6, 0, 5);
            for (int f = 0; f < 120; f++) Walker.Step(w, p, new MoveInput(0, 1), -3 * Math.PI / 4, 1.0 / 30);
            Assert.True(Geo.Pip(b.footprint, p.x, p.z), $"walked out to ({p.x:0.00}, {p.z:0.00})");
            Assert.True(p.x + p.z > 18, $"stopped early at ({p.x:0.00}, {p.z:0.00})");   // it got to the corner
        }
    }
}
