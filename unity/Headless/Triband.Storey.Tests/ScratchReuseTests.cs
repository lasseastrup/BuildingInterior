using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Triband.Storey.Generate;
using Triband.Storey.ThirdParty;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The pieces detail builds reuse their memory with (docs/CITY.md §7): <see cref="ChunkList{T}"/> and the
    /// <see cref="Chunks"/> it takes from, and the per-thread lists of Earcut, the triangulation and the wall panels,
    /// which must give the same answers reused as new.
    /// </summary>
    public class ScratchReuseTests
    {
        [Fact]
        public void AChunkListIsAList()
        {
            var l = new ChunkList<int>(); var r = new List<int>();
            for (int i = 0; i < 3 * Chunks.Size + 17; i++) { l.Add(i * 7); r.Add(i * 7); }
            Assert.Equal(r, l);
            Assert.Equal(r.Count, l.Count);
            Assert.Equal(4, l.ChunkCount);
            l[5] = -1; r[5] = -1; l[Chunks.Size] += 3; r[Chunks.Size] += 3;
            Assert.Equal(r, l.ToArray());
            var part = new int[100]; l.CopyTo(Chunks.Size - 50, part, 0, 100);
            Assert.Equal(r.GetRange(Chunks.Size - 50, 100), part);
            Assert.Throws<ArgumentOutOfRangeException>(() => l[l.Count]);
            // cut short, its chunks past the cut go back, and adding carries on from the cut
            l.Truncate(Chunks.Size + 1); r.RemoveRange(Chunks.Size + 1, r.Count - Chunks.Size - 1);
            Assert.Equal(2, l.ChunkCount);
            for (int i = 0; i < Chunks.Size; i++) { l.Add(i); r.Add(i); }
            Assert.Equal(r, l);
            l.Truncate(Chunks.Size); r.RemoveRange(Chunks.Size, r.Count - Chunks.Size);
            l.Add(42); r.Add(42);
            Assert.Equal(r, l);
            // resized up, the new elements are written before they are read; down, as Truncate
            l.Resize(2 * Chunks.Size + 3); for (int i = r.Count; i < l.Count; i++) { l[i] = i; r.Add(i); }
            Assert.Equal(r, l);
            l.Resize(10); r.RemoveRange(10, r.Count - 10);
            Assert.Equal(r, l);
            l.Clear();
            Assert.Empty(l); Assert.Equal(0, l.ChunkCount);
            l.Add(1);
            Assert.Equal(new[] { 1 }, l);
        }

        [Fact]
        public void SetCopiesEveryFieldOfAnOpening()
        {
            // a field added to Opening must be copied by Set too: the wall panels merge their openings into copies made with it
            var o = new Opening(); var rnd = new Random(5);
            foreach (var f in typeof(Opening).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                f.SetValue(o, f.FieldType == typeof(double) ? rnd.NextDouble() + 1 : f.FieldType == typeof(bool) ? true : throw new InvalidOperationException($"Opening.{f.Name}: Set copies doubles and bools; add {f.FieldType.Name} to it and to this test"));
            var c = new Opening().Set(o);
            foreach (var f in typeof(Opening).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Assert.Equal(f.GetValue(o), f.GetValue(c));
        }

        [Fact]
        public void SettingAnOpeningOrPanelOptionsSetsEveryField()
        {
            // the openings and panel options made again wall after wall are set whole: a field added must be a parameter too
            int fields(Type t) => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length;
            var openingSet = typeof(Opening).GetMethods().Single(m => m.Name == nameof(Opening.Set) && m.GetParameters().Length > 1);
            Assert.Equal(fields(typeof(Opening)), openingSet.GetParameters().Length);
            Assert.Equal(fields(typeof(Facade.PanelOpt)), typeof(Facade.PanelOpt).GetMethod(nameof(Facade.PanelOpt.Set))!.GetParameters().Length);
            var used = new Opening().Set(new Opening { u0 = 1, u1 = 2, y0 = 3, y1 = 4, door = true, full = true, thr = true, bare = true });
            var again = used.Set(5, 6, 0.5, 2, full: true);
            Assert.True(again.u0 == 5 && again.u1 == 6 && again.y0 == 0.5 && again.y1 == 2 && again.full && !again.door && !again.thr && !again.bare);
            var p = new Facade.PanelOpt { outer = false, ends = true, revealC = new Swatch(new StyleRef(1, 0), ColorSlot.Wall) }.Set(inner: false);
            Assert.True(p.outer && !p.inner && !p.ends && !p.threshold && p.revealFrom == null && p.revealC == null);
        }

        [Fact]
        public void TheTypedHashIsTheTextOne()
        {
            var rnd = new Random(11);
            foreach (var id in new[] { "b", "tower-17", "" })
                foreach (var tag in new[] { "ac", "vent" })
                    for (int n = 0; n < 200; n++)
                    {
                        int k = rnd.Next(-5, 300), i = n % 7 == 0 ? 0 : rnd.Next(0, 100000), j = n % 11 == 0 ? int.MinValue + n : rnd.Next(-1000, 1000);
                        Assert.Equal(Details.Hash01(id, tag, (object)k, i, j), Details.Hash01(id, tag, k, i, j));
                    }
        }

        [Fact]
        public void EarcutGivesTheSameTrianglesReusingItsNodes()
        {
            // a square with two square holes, and a long notched strip, each triangulated new and with a list reused,
            // on several threads at once (each has its own nodes)
            var shapes = new List<(double[] data, int[]? holes)>
            {
                (new double[] { 0, 0, 10, 0, 10, 10, 0, 10, 2, 2, 4, 2, 4, 4, 2, 4, 6, 6, 8, 6, 8, 8, 6, 8 }, new[] { 4, 8 }),
                (Enumerable.Range(0, 60).SelectMany(i => new double[] { i, i % 2 }).Concat(Enumerable.Range(0, 60).Reverse().SelectMany(i => new double[] { i, 5 + i % 3 })).ToArray(), null),
            };
            var expect = shapes.Select(s => Earcut.Tessellate(s.data, s.holes)).ToList();
            Parallel.For(0, 8, t =>
            {
                var into = new List<int>();
                for (int round = 0; round < 50; round++)
                    for (int k = 0; k < shapes.Count; k++)
                    {
                        // the data in a longer array, as the triangulation's own scratch holds it
                        var (d, h) = shapes[(k + t + round) % shapes.Count]; int e = (k + t + round) % shapes.Count;
                        var longer = d.Concat(new double[] { 99, 99, 98, 97 }).ToArray(); var hl = h?.Concat(new[] { 1000 }).ToArray();
                        into.Clear();
                        Earcut.Tessellate(longer, d.Length, hl, h?.Length ?? 0, into);
                        Assert.Equal(expect[e], into);
                    }
            });
        }

        [Fact]
        public void ATriangulationReusingItsListsIsTheNewOnes()
        {
            var outer = new List<Vec2> { new(0, 0), new(5, 0), new(5, 3), new(0, 3), new(0, 0) };
            var holes = new List<List<Vec2>> { new() { new(1, 1), new(2, 1), new(2, 2), new(1, 2) }, new() { new(3, 1), new(4, 1), new(4, 2), new(3, 2) } };
            var fresh = Triangulate.Shape(outer, holes);
            var into = new List<int> { 7, 7, 7 };
            for (int i = 0; i < 3; i++) Assert.Equal(fresh, Triangulate.Shape(outer, holes, into));
            Assert.Equal(Triangulate.Shape(outer, null), Triangulate.Shape(outer, new List<List<Vec2>>(), into));
        }
    }

    /// <summary>The limit on the chunks kept, which every thread's lists give back to: set here, so on its own.</summary>
    [Collection(TimingCollection.Name)]
    public class ChunkLimitTests
    {
        [Fact]
        public void NoMoreChunksAreKeptThanTheLimit()
        {
            long keep = Chunks.KeepBytes;
            try
            {
                // (other tests may give chunks back meanwhile: the limit holds for theirs too)
                Chunks.KeepBytes = 3L * Chunks<long>.Bytes; Chunks.Clear();
                var l = new ChunkList<long>(); l.Resize(5 * Chunks.Size);
                l.Clear();
                Assert.True(Chunks.KeptBytes <= Chunks.KeepBytes, $"{Chunks.KeptBytes} bytes kept past the limit");
            }
            finally { Chunks.KeepBytes = keep; }
        }
    }
}
