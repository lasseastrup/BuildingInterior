#nullable enable
using System.Collections.Generic;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Indices marked since they were last taken, each once, taken a range at a time: the building table's rows whose
    /// steady answer may have changed, which each site takes for its own block of the table before the cameras render
    /// (docs/PERFORMANCE.md §3.3). Engine-free.
    /// </summary>
    public sealed class Marks
    {
        readonly bool[] marked;
        readonly List<int> list = new List<int>();

        public Marks(int length) { marked = new bool[length]; }

        /// <summary>How many indices are marked.</summary>
        public int Count => list.Count;

        /// <summary>An index changed; one out of range is ignored.</summary>
        public void Mark(int i)
        {
            if ((uint)i >= (uint)marked.Length || marked[i]) return;
            marked[i] = true; list.Add(i);
        }

        /// <summary>The marked indices in [<paramref name="start"/>, <paramref name="start"/> + <paramref name="n"/>), added to <paramref name="into"/> in the order marked and unmarked; the others stay marked.</summary>
        public void Take(int start, int n, List<int> into)
        {
            int keep = 0;
            for (int k = 0; k < list.Count; k++)
            {
                int i = list[k];
                if (i >= start && i - start < n) { marked[i] = false; into.Add(i); }
                else list[keep++] = i;
            }
            list.RemoveRange(keep, list.Count - keep);
        }
    }
}
