using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Districts: which loaded one the player is in (<see cref="Districts"/>, docs/CITY.md §5).</summary>
    public class DistrictsTests
    {
        static readonly List<Districts.Area> TwoSideBySide = new List<Districts.Area>
        {
            new Districts.Area(0, 0, 100, 100), new Districts.Area(100, 0, 200, 100),
        };

        [Fact]
        public void ThePlayerIsInTheDistrictUnderThem()
        {
            Assert.Equal(0, Districts.PlayerIn(TwoSideBySide, -1, 50, 50));
            Assert.Equal(1, Districts.PlayerIn(TwoSideBySide, -1, 150, 50));
            Assert.Equal(1, Districts.PlayerIn(TwoSideBySide, -1, 260, 50));   // outside both: the nearer
            Assert.Equal(-1, Districts.PlayerIn(new List<Districts.Area>(), -1, 0, 0));
        }

        [Fact]
        public void OnTheBoundaryTheyStayInTheOneTheyCameFrom()
        {
            // a few metres into the next district still counts as the one they are in, so the occlusion doesn't flick
            Assert.Equal(0, Districts.PlayerIn(TwoSideBySide, 0, 103, 50));
            Assert.Equal(1, Districts.PlayerIn(TwoSideBySide, 0, 106, 50));
            Assert.Equal(1, Districts.PlayerIn(TwoSideBySide, 1, 97, 50));
        }

        // two districts: one building each, back to back along x = 10 (district B's site 10 m east, its building at 0)
        static (StoreyDocument a, StoreyDocument b) BackToBack(int floorsA, int floorsB)
        {
            StoreyDocument One(int floors)
            {
                var d = new StoreyDocument(); var b = Buildings.Add(d, "rect", new Vec2(0, 0));
                b.pos = new Vec2(0, 0); b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 8), new Vec2(0, 8) };
                b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
                return d;
            }
            return (One(floorsA), One(floorsB));
        }

        static bool Owns(Site site, BuildingData b, int edge) => Party.Ranges(site, b, 0, edge).Any(r => r.own);
        static bool Shares(Site site, BuildingData b, int edge) => Party.Ranges(site, b, 0, edge).Count > 0;

        [Theory]
        [InlineData(3, 3)]
        [InlineData(3, 5)]
        [InlineData(5, 3)]
        public void AWallOnADistrictEdgeIsBuiltByExactlyOneSide(int floorsA, int floorsB)
        {
            var (A, B) = BackToBack(floorsA, floorsB);
            var a = A.buildings[0]; var b = B.buildings[0];
            // district A sees B's building 10 m east; district B sees A's 10 m west
            var siteA = Site.WithContext(A.buildings, "North", new[] { (B.buildings, "South", 10.0, 0.0) });
            var siteB = Site.WithContext(B.buildings, "South", new[] { (A.buildings, "North", -10.0, 0.0) });
            Assert.Equal(1, siteA.Own); Assert.Equal(2, siteA.Buildings.Count);
            // a's east edge (1) and b's west edge (3) are the same wall, seen from each district
            Assert.True(Shares(siteA, a, 1)); Assert.True(Shares(siteB, b, 3));
            Assert.True(Owns(siteA, a, 1) != Owns(siteB, b, 3), "one district builds the wall, the other leaves it out");
            if (floorsA != floorsB) Assert.Equal(floorsA > floorsB, Owns(siteA, a, 1));   // the taller owns it, as within a layout
        }

        [Fact]
        public void FarNeighboursAreLeftOut()
        {
            var (A, B) = BackToBack(3, 3);
            var site = Site.WithContext(A.buildings, "North", new[] { (B.buildings, "South", 60.0, 0.0) });
            Assert.Single(site.Buildings);
        }

        [Theory]
        [InlineData(2.0)]
        [InlineData(15.0)]
        public void TheNeighboursKeptAreThoseWithinReachOfAnOwnBuilding(double reach)
        {
            // found through a grid of the own buildings' boxes: the same ones, in the same order, as held against every box
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 1000);
            var box = d.buildings.ToDictionary(b => b, Site.BoundsOf);
            double mid = d.buildings.Average(b => box[b].x0 + box[b].x1) / 2;
            var own = d.buildings.Where(b => box[b].x0 + box[b].x1 < 2 * mid).ToList(); var other = d.buildings.Except(own).ToList();
            bool Within((double x0, double z0, double x1, double z1) a, (double x0, double z0, double x1, double z1) o) =>
                a.x0 <= o.x1 + reach && o.x0 <= a.x1 + reach && a.z0 <= o.z1 + reach && o.z0 <= a.z1 + reach;
            var want = other.Where(nb => own.Any(o => Within(box[nb], box[o]))).Select(nb => "@East/" + nb.id).ToList();
            var site = Site.WithContext(own, "West", new[] { (other, "East", 0.0, 0.0) }, reach);
            Assert.NotEmpty(want);
            Assert.Equal(want, site.Buildings.Skip(own.Count).Select(b => b.id));
        }

        /// <summary>
        /// A neighbour's baked layout gives the site its parsed layout gives (docs/PERFORMANCE.md §6.3): the same buildings
        /// along the edge, read alone from the bytes, with the same ids, places and owners, and the same massings.
        /// </summary>
        [Theory]
        [InlineData(2.0, 0.0, 0.0)]
        [InlineData(15.0, 3.5, -2.0)]
        public void ABakedNeighbourGivesTheSameEdge(double reach, double dx, double dz)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 1000);
            var box = d.buildings.ToDictionary(b => b, Site.BoundsOf);
            double mid = d.buildings.Average(b => box[b].x0 + box[b].x1) / 2;
            var own = d.buildings.Where(b => box[b].x0 + box[b].x1 < 2 * mid).ToList(); var other = d.buildings.Except(own).ToList();
            var otherDoc = PrototypeJson.Read(PrototypeJson.Write(new StoreyDocument { buildings = other })).Document;   // as its asset reads it
            var baked = BakedLayout.Of(BakedLayout.Bake(otherDoc))!;
            var parsed = Site.WithContext(own, "West", new[] { (otherDoc.buildings, "East", dx, dz) }, reach);
            var read = Site.WithContext(own, "West", System.Array.Empty<(List<BuildingData>, string, double, double)>(), new[] { (baked, "East", dx, dz) }, reach);
            Assert.True(parsed.Buildings.Count > own.Count, "the edge has buildings");
            Assert.Equal(parsed.Buildings.Count, read.Buildings.Count);
            for (int i = 0; i < parsed.Buildings.Count; i++)
            {
                Assert.Equal(PrototypeJson.Write(parsed.Buildings[i]), PrototypeJson.Write(read.Buildings[i]));
                Assert.Equal(parsed.OwnerKey(parsed.Buildings[i]), read.OwnerKey(read.Buildings[i]));
            }
            // the own buildings near the edge build the same massings, party walls and all
            int near = 0;
            foreach (var b in own.Where(b => box[b].x1 > mid - 30))
            {
                var m0 = Lod2.Build(parsed, b); var m1 = Lod2.Build(read, b); near++;
                Assert.True(m0.P.SequenceEqual(m1.P) && m0.I.SequenceEqual(m1.I), $"{b.name}: its massing differs");
            }
            Assert.True(near > 5, $"{near} buildings near the edge");
        }

        static readonly List<Districts.Area> Row = new List<Districts.Area>
        {
            new Districts.Area(0, 0, 100, 100), new Districts.Area(400, 0, 500, 100), new Districts.Area(800, 0, 900, 100),
        };

        [Fact]
        public void PlanningEveryFrameMakesNoGarbage()
        {
            // the streamer plans every frame: it made three lists and boxed an enumerator each time (136 bytes)
            var st = new List<Districts.Load> { Districts.Load.Loaded, Districts.Load.Unloaded, Districts.Load.Unloaded };
            var load = new List<int>(); var unload = new List<int>();
            Districts.Plan(Row, st, 50, 50, load, unload, loadRadius: 350);
            long a = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) Districts.Plan(Row, st, 50 + i % 7, 50, load, unload, loadRadius: 350);
            Assert.Equal(0, System.GC.GetAllocatedBytesForCurrentThread() - a);
            Assert.Equal(new[] { 1 }, load); Assert.Empty(unload);
        }

        [Fact]
        public void StreamingLoadsTheNearestFirstOneAtATime()
        {
            var st = new List<Districts.Load> { Districts.Load.Unloaded, Districts.Load.Unloaded, Districts.Load.Unloaded };
            var (load, unload) = Districts.Plan(Row, st, 50, 50, loadRadius: 350);
            Assert.Equal(new[] { 0 }, load);          // two are in range, the nearer goes first
            Assert.Empty(unload);
            st[0] = Districts.Load.Loading;
            Assert.Empty(Districts.Plan(Row, st, 50, 50, loadRadius: 350).load);   // one at a time
            st[0] = Districts.Load.Loaded;
            Assert.Equal(new[] { 1 }, Districts.Plan(Row, st, 50, 50, loadRadius: 350).load);
        }

        [Fact]
        public void StreamingUnloadsOnlyPastTheLargerRadius()
        {
            var st = new List<Districts.Load> { Districts.Load.Loaded, Districts.Load.Loaded, Districts.Load.Unloaded };
            // walking east to x = 450: district 0 is 350 m away, between the load and unload radii: it stays
            var (load, unload) = Districts.Plan(Row, st, 450, 50, loadRadius: 300, unloadRadius: 400);
            Assert.Empty(unload);
            Assert.Empty(load);                        // district 2 is 350 m away too: not yet
            (load, unload) = Districts.Plan(Row, st, 520, 50, loadRadius: 300, unloadRadius: 400);
            Assert.Equal(new[] { 0 }, unload);         // 420 m: gone
            Assert.Equal(new[] { 2 }, load);           // 280 m: wanted
        }

        [Fact]
        public void TheDistrictUnderThePlayerIsNeverUnloaded()
        {
            var st = new List<Districts.Load> { Districts.Load.Loaded };
            Assert.Empty(Districts.Plan(new List<Districts.Area> { new Districts.Area(0, 0, 100, 100) }, st, 50, 50, loadRadius: 0, unloadRadius: 0).unload);
        }

        /// <summary>A district whose load failed (not in the build, no such address) is neither tried again nor unloaded, and holds no loading slot.</summary>
        [Fact]
        public void AFailedDistrictIsLeftAlone()
        {
            var st = new List<Districts.Load> { Districts.Load.Failed, Districts.Load.Unloaded, Districts.Load.Unloaded };
            var (load, unload) = Districts.Plan(Row, st, 50, 50, loadRadius: 1000, unloadRadius: 1200, maxLoading: 1);
            Assert.Equal(new[] { 1 }, load);
            Assert.Empty(unload);
            (load, unload) = Districts.Plan(Row, st, 5000, 50, loadRadius: 100, unloadRadius: 200);
            Assert.Empty(load); Assert.Empty(unload);
        }
    }
}
