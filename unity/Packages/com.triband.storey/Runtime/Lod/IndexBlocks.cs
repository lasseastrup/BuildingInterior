#nullable enable
using System.Collections.Generic;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Contiguous blocks of indices out of a fixed range, best fit, merged again when given back (docs/CITY.md §5): the
    /// shared building table hands each Storey Site one block for its layout's buildings.
    /// </summary>
    public sealed class IndexBlocks
    {
        readonly List<(int start, int n)> free = new List<(int, int)>();

        /// <summary>Indices 0 to <paramref name="count"/> − 1.</summary>
        public IndexBlocks(int count) { if (count > 0) free.Add((0, count)); }

        /// <summary>How many indices are free in all (not necessarily in one block).</summary>
        public int Free { get { int t = 0; foreach (var f in free) t += f.n; return t; } }

        /// <summary>
        /// A block of <paramref name="n"/> indices (the first returned), or -1 when no free block is that long: from the
        /// smallest free block it fits (the first of those), which leaves the large ones for large districts. First fit
        /// broke them up: streaming 50–300-building districts with about 7,000 indices in use, it refused three loads in ten
        /// where best fit refuses one in seven (docs/PERFORMANCE.md §6.9).
        /// </summary>
        public int Alloc(int n)
        {
            if (n <= 0) return 0;
            int pick = -1;
            for (int i = 0; i < free.Count; i++)
                if (free[i].n >= n && (pick < 0 || free[i].n < free[pick].n)) pick = i;
            if (pick < 0) return -1;
            var f = free[pick];
            if (f.n == n) free.RemoveAt(pick); else free[pick] = (f.start + n, f.n - n);
            return f.start;
        }

        /// <summary>Give a block back.</summary>
        public void Release(int start, int n)
        {
            if (n <= 0 || start < 0) return;
            free.Add((start, n));
            free.Sort((a, b) => a.start.CompareTo(b.start));
            for (int i = free.Count - 2; i >= 0; i--)
                if (free[i].start + free[i].n == free[i + 1].start) { free[i] = (free[i].start, free[i].n + free[i + 1].n); free.RemoveAt(i + 1); }
        }
    }

    /// <summary>A LOD0 vertex's cutaway kind with its party-wall neighbour moved into a site's block of the shared table.</summary>
    public static class MateIndex
    {
        /// <summary>
        /// <paramref name="kind"/> is 0–7, plus 8 × (the neighbour's layout index + 1) on a party wall. The neighbour is
        /// moved to <paramref name="start"/> + its index; one past <paramref name="count"/> (a neighbouring district's
        /// building, there for the walls only, with no row) is dropped, so the wall is this building's alone.
        /// </summary>
        public static int Shift(int kind, int start, int count)
        {
            if (kind < 8) return kind;
            int mate = (kind >> 3) - 1;
            return mate < count ? (kind & 7) + ((start + mate + 1) << 3) : kind & 7;
        }
    }
}
