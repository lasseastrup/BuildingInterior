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

        /// <summary>
        /// A block comes from the smallest free one it fits (docs/PERFORMANCE.md §6.9), leaving the large ones for large
        /// districts; streaming districts near the table's capacity, that refuses fewer loads than first fit did.
        /// </summary>
        [Fact]
        public void ABlockComesFromTheSmallestFreeOneItFits()
        {
            var b = new IndexBlocks(100);
            int x = b.Alloc(10), y = b.Alloc(20), z = b.Alloc(10), w = b.Alloc(5);   // 0, 10, 30, 40; 45.. free
            b.Release(x, 10); b.Release(z, 10);   // free: 0–9, 30–39, 45–99
            Assert.Equal(0, b.Alloc(8));          // the first of the two 10s
            Assert.Equal(30, b.Alloc(10));        // the other 10, not the 55 at the end
            Assert.Equal(45, b.Alloc(40));
            Assert.Equal(8, b.Alloc(2));          // what is left of the first 10
            // streamed districts of 50–300 buildings, about 7,000 indices in use of the table's 8,176
            int Refused(bool firstFit)
            {
                int refused = 0;
                for (int seed = 0; seed < 5; seed++)
                {
                    var rnd = new System.Random(seed); var blocks = new IndexBlocks(8176); var first = new System.Collections.Generic.List<(int s, int n)> { (0, 8176) };
                    var held = new System.Collections.Generic.List<(int s, int n)>(); int used = 0;
                    for (int step = 0; step < 600; step++)
                    {
                        if (used < 7000 || held.Count == 0)
                        {
                            int n = 50 + rnd.Next(251), s = firstFit ? FirstFit(first, n) : blocks.Alloc(n);
                            if (s >= 0) { held.Add((s, n)); used += n; continue; }
                            refused++; if (held.Count == 0) continue;
                        }
                        int k = rnd.Next(held.Count); var h = held[k];
                        if (firstFit) Give(first, h.s, h.n); else blocks.Release(h.s, h.n);
                        used -= h.n; held.RemoveAt(k);
                    }
                }
                return refused;
            }
            int bestRefused = Refused(false), firstRefused = Refused(true);
            Assert.True(bestRefused < firstRefused * 3 / 4, $"best fit refused {bestRefused}, first fit {firstRefused}");
        }

        // first fit, as the blocks were handed out before
        static int FirstFit(System.Collections.Generic.List<(int s, int n)> free, int n)
        {
            for (int i = 0; i < free.Count; i++)
            {
                var f = free[i]; if (f.n < n) continue;
                if (f.n == n) free.RemoveAt(i); else free[i] = (f.s + n, f.n - n);
                return f.s;
            }
            return -1;
        }

        static void Give(System.Collections.Generic.List<(int s, int n)> free, int s, int n)
        {
            free.Add((s, n)); free.Sort((a, c) => a.s.CompareTo(c.s));
            for (int i = free.Count - 2; i >= 0; i--) if (free[i].s + free[i].n == free[i + 1].s) { free[i] = (free[i].s, free[i].n + free[i + 1].n); free.RemoveAt(i + 1); }
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
