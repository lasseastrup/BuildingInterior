#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Which chunks of a table have changed since it was last uploaded, so only those go to the GPU (the building table's
    /// buffers, docs/CITY.md §7): a frame's few wall slides and fading buildings are a few kilobytes, not the whole
    /// buffer. Engine-free.
    /// </summary>
    public sealed class DirtyChunks
    {
        public readonly int ChunkSize;
        bool[] dirty;
        int lo = int.MaxValue, hi = -1;   // the first and last dirty chunk
        int length;

        public DirtyChunks(int length, int chunkSize)
        {
            if (chunkSize <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
            ChunkSize = chunkSize; this.length = length;
            dirty = new bool[(length + chunkSize - 1) / chunkSize];
        }

        /// <summary>Elements in the table.</summary>
        public int Length => length;
        /// <summary>Whether anything is to upload.</summary>
        public bool Any => hi >= 0;

        /// <summary>One element changed.</summary>
        public void Mark(int index)
        {
            if ((uint)index >= (uint)length) return;
            int c = index / ChunkSize; dirty[c] = true;
            if (c < lo) lo = c; if (c > hi) hi = c;
        }

        /// <summary>A run of elements changed.</summary>
        public void Mark(int start, int count)
        {
            if (count <= 0) return;
            int a = Math.Max(0, start), b = Math.Min(length, start + count) - 1; if (b < a) return;
            int c0 = a / ChunkSize, c1 = b / ChunkSize;
            for (int c = c0; c <= c1; c++) dirty[c] = true;
            if (c0 < lo) lo = c0; if (c1 > hi) hi = c1;
        }

        /// <summary>Everything changed (the buffer is new).</summary>
        public void MarkAll() => Mark(0, length);

        /// <summary>The table grew to <paramref name="newLength"/>: everything is to upload (into a new buffer).</summary>
        public void Grow(int newLength)
        {
            if (newLength < length) throw new ArgumentOutOfRangeException(nameof(newLength));
            length = newLength;
            Array.Resize(ref dirty, (length + ChunkSize - 1) / ChunkSize);
            MarkAll();
        }

        /// <summary>The changed runs (element start, count), adjacent chunks joined, into <paramref name="runs"/> (cleared first); then nothing is dirty.</summary>
        public void Take(List<(int start, int count)> runs)
        {
            runs.Clear();
            if (hi < 0) return;
            int c = lo;
            while (c <= hi)
            {
                if (!dirty[c]) { c++; continue; }
                int c0 = c;
                while (c <= hi && dirty[c]) { dirty[c] = false; c++; }
                int start = c0 * ChunkSize, end = Math.Min(length, c * ChunkSize);
                runs.Add((start, end - start));
            }
            lo = int.MaxValue; hi = -1;
        }
    }
}
