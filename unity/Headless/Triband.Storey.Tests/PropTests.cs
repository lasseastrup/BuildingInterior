using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Triband.Storey.Props;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Props inside buildings (docs/PROPS.md): which building a prop is in, and the CPU fallback's rule.</summary>
    public class PropTests
    {
        static (Site site, BuildingData a, BuildingData b) TwoHouses(double elevB = 0)
        {
            var d = new StoreyDocument();
            BuildingData One(double x0, double elev)
            {
                var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0));
                b.pos = new Vec2(x0, 0); b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 8), new Vec2(0, 8) };
                b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList(); b.elevation = elev;
                return b;
            }
            One(0, 0); One(30, elevB);
            var site = new Site(d.buildings);
            return (site, site.Buildings[0], site.Buildings[1]);
        }

        [Fact]
        public void APropIsInTheBuildingWhoseOutlineHoldsIt()
        {
            var (site, a, b) = TwoHouses();
            Assert.Same(a, PropLocator.BuildingAt(site, 5, 0.5, 4));            // ground floor
            Assert.Same(a, PropLocator.BuildingAt(site, 5, 7.0, 4));            // second floor
            Assert.Same(b, PropLocator.BuildingAt(site, 35, 4.0, 4));
            Assert.Null(PropLocator.BuildingAt(site, 20, 0.5, 4));              // the street between
            Assert.Null(PropLocator.BuildingAt(site, 5, 0.5, 9));               // out of the window, on the ground
            Assert.Null(PropLocator.BuildingAt(site, 5, 40, 4));                // well above the roof
        }

        [Fact]
        public void APropUnderARaisedBuildingIsntInIt()
        {
            var (site, _, b) = TwoHouses(elevB: 6);
            Assert.Null(PropLocator.BuildingAt(site, 35, 0.3, 4));
            Assert.Same(b, PropLocator.BuildingAt(site, 35, 6.3, 4));
        }

        [Fact]
        public void ANeighbouringDistrictsBuildingIsNeverAPropsHome()
        {
            var (site, a, _) = TwoHouses();
            var other = new List<BuildingData> { PrototypeJson.ReadBuilding(PrototypeJson.Write(a)) };
            var withEdge = Site.WithContext(new List<BuildingData> { site.Buildings[1] }, "Here", new[] { (other, "There", 21.0, 0.0) });
            Assert.Null(PropLocator.BuildingAt(withEdge, 26, 0.5, 4));         // the neighbour's copy, 21 m east: there for its walls only
            Assert.NotNull(PropLocator.BuildingAt(withEdge, 35, 0.5, 4));
        }

        [Fact]
        public void TheUserValueIsTheTableIndexPlusOne()
        {
            Assert.Equal(0u, PropLocator.Encode(-1));
            Assert.Equal(1u, PropLocator.Encode(0));
            Assert.Equal(8176u, PropLocator.Encode(8175));
            Assert.Equal(-1, PropLocator.Decode(0)); Assert.Equal(41, PropLocator.Decode(42));
        }

        [Fact]
        public void LocatingMakesNoGarbage()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; Edit.TestCity.Generate(d, 300);
            var site = new Site(d.buildings); var rnd = new Random(3); int found = 0;
            for (int i = 0; i < 200; i++) PropLocator.BuildingAt(site, rnd.NextDouble() * 200 - 100, 1, rnd.NextDouble() * 200 - 100);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 2000; i++) if (PropLocator.BuildingAt(site, rnd.NextDouble() * 400 - 200, rnd.NextDouble() * 10, rnd.NextDouble() * 400 - 200) != null) found++;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - a0);
            Assert.True(found > 100, $"only {found} of 2,000 points landed in a building");
        }

        [Fact]
        public void TheFallbackHidesWhatTheShaderWould()
        {
            // the player on a's ground floor: a's upper floors are hidden, its ground floor isn't, b's floors aren't
            var (site, a, b) = TwoHouses();
            var core = new OcclusionCore(new PlayWorld(site), new OcclusionSettings { mode = OccluderMode.Sink });
            var p = new PlayerState(); p.Spawn(5, 0, 4);
            core.Frame(p, (5, 6, -6), (5, 1.4, 4), 1.0 / 30);
            Assert.Same(a, core.View.active);
            Assert.False(core.HidesPoint(a, 1.0));
            Assert.True(core.HidesPoint(a, 5.0));
            Assert.False(core.HidesPoint(b, 5.0));
        }

        [Fact]
        public void TheFallbackHidesTheStoreysOfASunkBuilding()
        {
            // the player in the street behind b, the camera beyond it: b sinks to the player's storey, a prop upstairs goes
            var (site, _, b) = TwoHouses();
            var core = new OcclusionCore(new PlayWorld(site), new OcclusionSettings { mode = OccluderMode.Sink });
            var p = new PlayerState(); p.Spawn(35, 0, 14);
            for (int f = 0; f < 30; f++) core.Frame(p, (35, 8, -8), (35, 1.4, 14), 1.0 / 30);
            Assert.Contains(core.Active, r => r.b == b);
            Assert.True(core.HidesPoint(b, 7.0));
            Assert.True(core.HidesPoint(b, 1.0));    // the player's own storey squashes to a low base too
            Assert.False(core.HidesPoint(b, -0.5));  // below it: nothing
        }
    }
}
