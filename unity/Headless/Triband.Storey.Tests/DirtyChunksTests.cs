using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The building table's changed chunks (<see cref="DirtyChunks"/>): only they are uploaded. Storey's own.</summary>
    public class DirtyChunksTests
    {
        static List<(int, int)> Runs(DirtyChunks d) { var r = new List<(int, int)>(); d.Take(r); return r; }

        [Fact]
        public void OnlyTheChangedChunksAreTaken()
        {
            var d = new DirtyChunks(1000, 64);
            Assert.False(d.Any); Assert.Empty(Runs(d));
            d.Mark(5); d.Mark(70); d.Mark(300);
            Assert.Equal(new[] { (0, 128), (256, 64) }, Runs(d));   // chunks 0 and 1 joined, chunk 4
            Assert.False(d.Any); Assert.Empty(Runs(d));             // taken: nothing left
            d.Mark(990);
            Assert.Equal(new[] { (960, 40) }, Runs(d));             // the last chunk, short
            d.Mark(100, 200);                                       // elements 100..299: chunks 1..4
            Assert.Equal(new[] { (64, 256) }, Runs(d));
            d.Mark(-5); d.Mark(1000); d.Mark(990, 50); d.Mark(-10, 5);
            Assert.Equal(new[] { (960, 40) }, Runs(d));             // out of range ignored, clipped
        }

        [Fact]
        public void GrowingMarksEverything()
        {
            var d = new DirtyChunks(100, 16);
            Runs(d); d.Grow(250);
            Assert.Equal(250, d.Length);
            Assert.Equal(new[] { (0, 250) }, Runs(d));
            d.MarkAll(); Assert.Equal(new[] { (0, 250) }, Runs(d));
            Assert.Throws<ArgumentOutOfRangeException>(() => d.Grow(10));
        }

        [Fact]
        public void EveryMarkIsCoveredAndTheRunsAreSmall()
        {
            var rng = new Random(7); var d = new DirtyChunks(65536, 256);
            for (int round = 0; round < 200; round++)
            {
                var marked = new HashSet<int>();
                for (int i = 0; i < 20; i++) { int x = rng.Next(65536); d.Mark(x); marked.Add(x); }
                var runs = Runs(d);
                Assert.All(marked, x => Assert.Contains(runs, r => x >= r.Item1 && x < r.Item1 + r.Item2));
                Assert.True(runs.Sum(r => r.Item2) <= marked.Count * 256);
                for (int i = 1; i < runs.Count; i++) Assert.True(runs[i].Item1 > runs[i - 1].Item1 + runs[i - 1].Item2, "touching runs not joined");
            }
        }
    }
}
