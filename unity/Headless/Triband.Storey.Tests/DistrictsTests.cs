using System.Collections.Generic;
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
    }
}
