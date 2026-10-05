using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The shared building table's blocks of indices, one per site (<see cref="IndexBlocks"/>, docs/CITY.md §5).</summary>
    public class IndexBlocksTests
    {
        [Fact]
        public void SitesGetBlocksThatNeverOverlapAndComeBackWhole()
        {
            var b = new IndexBlocks(100);
            int a = b.Alloc(30), c = b.Alloc(50), d = b.Alloc(30);
            Assert.Equal(0, a); Assert.Equal(30, c);
            Assert.Equal(-1, d);                 // 20 left: no room for 30
            Assert.Equal(80, b.Alloc(5));        // a smaller one fits in what is left
            b.Release(a, 30);
            Assert.Equal(0, b.Alloc(30));        // the freed block is reused
            b.Release(0, 30); b.Release(30, 50); b.Release(80, 5);
            Assert.Equal(100, b.Free);
            Assert.Equal(0, b.Alloc(100));       // given back whole: merged into one block again
        }

        [Fact]
        public void APartyWallsNeighbourMovesIntoTheSitesBlock()
        {
            Assert.Equal(1, MateIndex.Shift(1, 500, 10));                     // not a party wall
            Assert.Equal(1 + 8 * (500 + 3 + 1), MateIndex.Shift(1 + 8 * (3 + 1), 500, 10));   // neighbour 3 → 503
            Assert.Equal(2, MateIndex.Shift(2 + 8 * (12 + 1), 500, 10));      // neighbour 12: another district's, dropped
            Assert.Equal(1 + 8 * 4, MateIndex.Shift(1 + 8 * 4, 0, 10));       // a site at the table's start: unchanged
        }
    }
}
