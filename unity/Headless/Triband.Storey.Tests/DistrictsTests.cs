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
    }
}
