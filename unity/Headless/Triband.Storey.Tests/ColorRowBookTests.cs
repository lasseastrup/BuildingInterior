using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// <see cref="ColorRowBook"/>: colour rows and building remaps (docs/COLOURS.md §3.3, §3.6, §5) against a
    /// palette that behaves like Color Pipeline 2.1.11: indices that move, remap rows deduplicated by mapping,
    /// and an invalidation that can fire inside the first registration.
    /// </summary>
    public class ColorRowBookTests
    {
        const string Teal = "00000000000000000000000000000001", Rust = "00000000000000000000000000000002";

        sealed class FakePalette : IColorPalette
        {
            readonly List<string> order = new List<string>();
            readonly Dictionary<string, int> rows = new Dictionary<string, int>(StringComparer.Ordinal);
            public int Shift, Registrations;
            public bool InvalidateInsideNextRegistration;
            public StyleDefaults Defaults { get; } = new StyleDefaults();
            public event Action? Invalidated;

            public int IndexOf(string c) { int i = order.IndexOf(c); if (i < 0) { order.Add(c); i = order.Count - 1; } return i + Shift; }

            public int RegisterRemap(IReadOnlyList<string> original, IReadOnlyList<string> overwrite)
            {
                Registrations++;
                if (InvalidateInsideNextRegistration) { InvalidateInsideNextRegistration = false; Shift += 100; rows.Clear(); Invalidated?.Invoke(); }
                if (original.Count == 0) return 0;
                string key = string.Join(",", original) + ">" + string.Join(",", overwrite);
                if (!rows.TryGetValue(key, out int r)) rows[key] = r = rows.Count + 1;
                return r;
            }

            /// <summary>The palette lost an entry: every index after it moved.</summary>
            public void Move() { Shift += 10; rows.Clear(); Invalidated?.Invoke(); }
        }

        sealed class Sink : IColorRowSink
        {
            public readonly Dictionary<int, (int[] idx, int remap)> Rows = new Dictionary<int, (int[], int)>();
            public readonly List<int> Released = new List<int>();
            int next;
            public int Writes;
            public int AllocColorRow() => next++;
            public void ReleaseColorRow(int row) { Released.Add(row); Rows.Remove(row); }
            public void WriteColorRow(int row, int[] paletteIndices, int remapRow) { Rows[row] = ((int[])paletteIndices.Clone(), remapRow); Writes++; }
        }

        static (ColorRowBook book, FakePalette palette, Sink sink, ColorResolver colors) Make()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var colors = new ColorResolver(new Site(doc.buildings));
            var palette = new FakePalette(); var sink = new Sink();
            return (new ColorRowBook(colors, palette, sink), palette, sink, colors);
        }

        static int[] Expected(ColorResolver colors, StyleRef s, FakePalette p) => ColorRows.Indices(colors.StyleOf(s), p);

        [Fact]
        public void EachStyleGetsOneRowWrittenOnce()
        {
            var (book, palette, sink, colors) = Make();
            int a = book.RowOf(new StyleRef(0, 0)), b = book.RowOf(new StyleRef(1, 0));
            Assert.Equal(a, book.RowOf(new StyleRef(0, 0)));
            Assert.NotEqual(a, b);
            Assert.Equal(2, sink.Writes);
            Assert.Equal(Expected(colors, new StyleRef(1, 0), palette), sink.Rows[b].idx);
            Assert.Equal(0, sink.Rows[a].remap);
        }

        [Fact]
        public void ARemapReachesEveryRowOfItsBuildingAndNoOther()
        {
            var (book, _, sink, _) = Make();
            int a0 = book.RowOf(new StyleRef(0, 0)), a1 = book.RowOf(new StyleRef(0, 1)), b = book.RowOf(new StyleRef(1, 0));
            book.SetRemap(0, new[] { Teal }, new[] { Rust });
            Assert.Equal((1, 1, 0), (sink.Rows[a0].remap, sink.Rows[a1].remap, sink.Rows[b].remap));
            book.SetRemap(1, new[] { Teal }, new[] { Rust });
            Assert.Equal(1, sink.Rows[b].remap);   // the same mapping shares an atlas row
            book.SetRemap(0, Array.Empty<string>(), Array.Empty<string>());
            Assert.Equal((0, 0, 1), (sink.Rows[a0].remap, sink.Rows[a1].remap, sink.Rows[b].remap));
            Assert.Throws<ArgumentException>(() => book.SetRemap(0, new[] { Teal }, Array.Empty<string>()));
        }

        [Fact]
        public void AnInvalidationRewritesEveryRowAndRegistersTheRemapsAgain()
        {
            var (book, palette, sink, colors) = Make();
            var s = new StyleRef(2, 0); int row = book.RowOf(s);
            book.SetRemap(2, new[] { Teal }, new[] { Rust });
            int before = palette.Registrations;
            palette.Move();
            Assert.Equal(before + 1, palette.Registrations);
            Assert.Equal(Expected(colors, s, palette), sink.Rows[row].idx);
            Assert.Equal(1, sink.Rows[row].remap);
        }

        [Fact]
        public void AnInvalidationFromInsideARegistrationIsSafe()
        {
            var (book, palette, sink, colors) = Make();
            var s = new StyleRef(0, 0); int row = book.RowOf(s);
            book.SetRemap(3, new[] { Rust }, new[] { Teal });
            palette.InvalidateInsideNextRegistration = true;   // as Color Pipeline does when it creates its atlas lazily
            book.SetRemap(0, new[] { Teal }, new[] { Rust });
            Assert.Equal(Expected(colors, s, palette), sink.Rows[row].idx);
            Assert.Equal(book.RemapRowOf(0), sink.Rows[row].remap);
            Assert.NotEqual(0, book.RemapRowOf(3));
        }

        [Fact]
        public void ProjectDefaultsFillWhatAStyleLeavesOut()
        {
            var (book, palette, sink, _) = Make();
            palette.Defaults.door = Teal;
            int row = book.RowOf(new StyleRef(0, 0));
            Assert.Equal(palette.IndexOf(Teal), sink.Rows[row].idx[(int)ColorSlot.Door]);
        }

        [Fact]
        public void ClearingFreesRowsAndDisposingStopsListening()
        {
            var (book, palette, sink, _) = Make();
            book.RowOf(new StyleRef(0, 0)); book.RowOf(new StyleRef(1, 0));
            book.Dispose();
            Assert.Equal(2, sink.Released.Count);
            int writes = sink.Writes; palette.Move();
            Assert.Equal(writes, sink.Writes);
        }

        [Fact]
        public void TheBuiltInPaletteHasNoRemaps()
        {
            var p = new HexPaletteIndex();
            Assert.Equal(0, p.RegisterRemap(Array.Empty<string>(), Array.Empty<string>()));
            Assert.Throws<NotSupportedException>(() => p.RegisterRemap(new[] { Teal }, new[] { Rust }));
        }

        /// <summary>An edited style reaches its row without the row moving, so meshes that are not rebuilt stay right.</summary>
        [Fact]
        public void RetargetingRewritesRowsInPlace()
        {
            var (book, palette, sink, _) = Make();
            int a = book.RowOf(new StyleRef(0, 0)), b = book.RowOf(new StyleRef(1, 0));
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            doc.buildings[0].style.wall = Teal;
            var edited = new ColorResolver(new Site(doc.buildings));
            book.Retarget(edited);
            Assert.Equal(a, book.RowOf(new StyleRef(0, 0)));
            Assert.Equal(Expected(edited, new StyleRef(0, 0), palette), sink.Rows[a].idx);
            Assert.Equal(Expected(edited, new StyleRef(1, 0), palette), sink.Rows[b].idx);
            Assert.Equal(palette.IndexOf(Teal), sink.Rows[a].idx[(int)ColorSlot.Wall]);
        }

        /// <summary>
        /// An edit retargets without rewriting: the edited building's rows are freed and written afresh as it is rebuilt,
        /// and every other row is left alone (rewriting them all cost 0.9 s an edit at 300 buildings with Color Pipeline).
        /// </summary>
        [Fact]
        public void AnEditRewritesOnlyTheRebuiltBuildingsRows()
        {
            var (book, palette, sink, _) = Make();
            int a = book.RowOf(new StyleRef(0, 0)), b = book.RowOf(new StyleRef(1, 0));
            int writes = sink.Writes;
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            doc.buildings[0].style.wall = Teal;
            var edited = new ColorResolver(new Site(doc.buildings));
            book.Retarget(edited, rewrite: false);
            Assert.Equal(writes, sink.Writes);   // nothing rewritten
            book.ReleaseBuilding(0);
            int a2 = book.RowOf(new StyleRef(0, 0));   // the rebuild asks for its row again
            Assert.Equal(writes + 1, sink.Writes);
            Assert.Equal(palette.IndexOf(Teal), sink.Rows[a2].idx[(int)ColorSlot.Wall]);
            Assert.Equal(Expected(edited, new StyleRef(1, 0), palette), sink.Rows[b].idx);   // the other building is as it was
        }

        [Fact]
        public void ReleasingABuildingFreesOnlyItsRows()
        {
            var (book, _, sink, _) = Make();
            int a0 = book.RowOf(new StyleRef(0, 0)), a1 = book.RowOf(new StyleRef(0, 1)), b = book.RowOf(new StyleRef(1, 0));
            book.ReleaseBuilding(0);
            Assert.Equal(new[] { a0, a1 }, sink.Released.OrderBy(x => x).ToArray());
            Assert.Equal(b, book.RowOf(new StyleRef(1, 0)));
            Assert.DoesNotContain(new StyleRef(0, 0), book.Rows.Keys);
        }
    }
}
