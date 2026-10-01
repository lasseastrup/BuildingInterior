using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The GPU colour path (docs/COLOURS.md §3.3–3.5) emulated on the CPU: colour rows packed as the building
    /// table uploads them, vertex bytes as MeshUpload writes them, and the lookups StoreyPalette.hlsl and
    /// StoreyFacade.hlsl make, against the RGB the census tests already judge. The shaders themselves are
    /// only compiled in an editor; this pins the data they read.
    /// </summary>
    public class GpuColorTests
    {
        /// <summary>Within the shade's one-byte encoding (±0.45 % for every factor in use).</summary>
        const double Rel = 0.0046, Abs = 1e-6;

        public static IEnumerable<object[]> Files => new[] { new object[] { "demo.json" }, new object[] { "variants.json" } };

        /// <summary>A site's colour rows as the harness writes them: a <see cref="ColorRowBook"/> over the hex palette, packed into the table's words.</summary>
        sealed class Table : IColorRowSink
        {
            public readonly HexPaletteIndex Palette = new HexPaletteIndex();
            public uint[] Words = new uint[0];
            public readonly ColorRowBook Book;
            int next;
            public Table(ColorResolver colors) { Book = new ColorRowBook(colors, Palette, this); }
            public int RowOf(StyleRef s) => Book.RowOf(s);

            public int AllocColorRow() { Array.Resize(ref Words, (next + 1) * ColorRows.Words); return next++; }
            public void ReleaseColorRow(int row) { }
            public void WriteColorRow(int row, int[] paletteIndices, int remapRow) => ColorRows.Pack(paletteIndices, remapRow, Words, row * ColorRows.Words);

            /// <summary>StoreyPaletteColor, remap row 0.</summary>
            public Rgb Shade(int row, ColorSlot slot, double tone)
            {
                int index = ColorRows.Entry(Words, row * ColorRows.Words, (int)slot);
                Assert.Equal(0, ColorRows.Entry(Words, row * ColorRows.Words, ColorRows.RemapEntry));
                return Palette.Colors[index].Shade(tone);
            }
        }

        static void Near(Rgb want, Rgb got, string what)
        {
            foreach (var (w, g) in new[] { (want.r, got.r), (want.g, got.g), (want.b, got.b) })
                Assert.True(Math.Abs(w - g) <= Abs + Rel * w, $"{what}: want {want}, got {got}");
        }

        [Theory, MemberData(nameof(Files))]
        public void EveryVertexDecodesToTheColourTheCensusJudges(string file)
        {
            var doc = PrototypeJson.Read(Fixtures.Text(file)).Document; var site = new Site(doc.buildings);
            var colors = new ColorResolver(site); var t = new Table(colors);
            int n = 0;
            foreach (var b in doc.buildings)
            {
                var l0 = Lod0.Build(site, b);
                foreach (var gb in new[] { l0.Op, l0.Glass, Lod1.Build(site, b) })
                    for (int i = 0; i < gb.Verts; i++, n++)
                    {
                        var s = gb.C[i];
                        var (r, g, bl, a) = ColorRows.EncodeVertex(t.RowOf(s.style), s.slot, s.tone);
                        // UNorm8 to float in the input assembler, then StoreyVertexColor's round(c * 255)
                        byte R(byte x) => (byte)Math.Round((float)(x / 255.0) * 255.0);
                        var (row, slot, tone) = ColorRows.DecodeVertex(R(r), R(g), R(bl), R(a));
                        Near(colors.Resolve(s), t.Shade(row, slot, tone), $"{b.name} vertex {i} {s}");
                    }
            }
            Assert.True(n > 10000);
            Assert.True(t.Palette.Colors.Count <= HexPaletteIndex.Capacity);
        }

        [Theory, MemberData(nameof(Files))]
        public void MassingRowsDecodeToTheirCensusColours(string file)
        {
            var doc = PrototypeJson.Read(Fixtures.Text(file)).Document; var site = new Site(doc.buildings);
            var colors = new ColorResolver(site); var t = new Table(colors);
            foreach (var b in doc.buildings)
                foreach (var row in Lod2.Build(site, b).Rows)
                {
                    var want = row.Texels(colors); var gpu = row.GpuTexels(t.RowOf(row.wall.style));
                    int cr = (int)gpu[0];
                    // StoreyFacadeColor: wall, trim, glass from texel 0's shades, roof from texel 1's
                    var got = new[] { t.Shade(cr, ColorSlot.Wall, gpu[1]), t.Shade(cr, ColorSlot.Trim, gpu[2]), t.Shade(cr, ColorSlot.Glass, gpu[3]), t.Shade(cr, ColorSlot.Roof, gpu[4]) };
                    for (int k = 0; k < 4; k++) Near(new Rgb(want[k * 4], want[k * 4 + 1], want[k * 4 + 2]), got[k], $"{b.name} LOD2 colour {k}");
                    Assert.Equal(want.Skip(16), gpu.Skip(16));
                }
        }

        [Fact]
        public void AParameterRowMixingStylesIsRefused()
        {
            var a = new Palette(new StyleRef(0, 0)); var b = new Palette(new StyleRef(1, 0));
            var row = new ParamRow { wall = a.wall, trim = b.trim, glass = a.glassDark, roof = a.roof };
            Assert.Throws<InvalidOperationException>(() => row.GpuTexels(0));
        }

        /// <summary>StoreyPalette.hlsl and StoreyFacade.hlsl hard-code these numbers.</summary>
        [Fact]
        public void TheShadersSlotNumbersHold()
        {
            Assert.Equal((0, 1, 4, 6), ((int)ColorSlot.Wall, (int)ColorSlot.Trim, (int)ColorSlot.Roof, (int)ColorSlot.Glass));
            Assert.Equal((24, 23, 12, 3), (ColorRows.Entries, ColorRows.RemapEntry, ColorRows.Words, ColorRows.Uint4s));
            Assert.True(ColorRows.Slots < ColorRows.RemapEntry);
        }

        [Fact]
        public void RowsPackSixteenBitEntriesAndTheRemapRow()
        {
            var w = new uint[ColorRows.Words * 2];
            var idx = Enumerable.Range(0, ColorRows.Slots).Select(i => 511 - i * 7).ToArray();
            ColorRows.Pack(idx, 1023, w, ColorRows.Words);
            for (int e = 0; e < idx.Length; e++) Assert.Equal(idx[e], ColorRows.Entry(w, ColorRows.Words, e));
            Assert.Equal(1023, ColorRows.Entry(w, ColorRows.Words, ColorRows.RemapEntry));
            Assert.All(w.Take(ColorRows.Words), x => Assert.Equal(0u, x));
            Assert.Throws<ArgumentOutOfRangeException>(() => ColorRows.Pack(new[] { 70000 }, 0, w, 0));
        }

        [Theory]
        [InlineData(1.0)] [InlineData(1.08)] [InlineData(0.85)] [InlineData(0.82)] [InlineData(0.8)] [InlineData(0.72)] [InlineData(0.42)]
        public void ShadesSurviveTheirByte(double tone)
        {
            var (r, g, b, a) = ColorRows.EncodeVertex(40000, ColorSlot.Dish, tone);
            var (row, slot, t) = ColorRows.DecodeVertex(r, g, b, a);
            Assert.Equal((40000, ColorSlot.Dish), (row, slot));
            Assert.InRange(t, tone * (1 - Rel), tone * (1 + Rel));
            if (tone == 1.0) Assert.Equal(1.0, t);
        }

        [Fact]
        public void EncodingRefusesWhatItCannotHold()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => ColorRows.EncodeVertex(ColorRows.MaxRow + 1, ColorSlot.Wall, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => ColorRows.EncodeVertex(0, ColorSlot.Wall, 2.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ColorRows.EncodeVertex(0, ColorSlot.Wall, -0.1));
        }

        [Fact]
        public void TheHexPaletteKeepsOneEntryPerColourAndNoPaletteIds()
        {
            var p = new HexPaletteIndex();
            Assert.Equal(0, p.IndexOf("#AABBCC"));
            Assert.Equal(0, p.IndexOf("#abc"));
            Assert.Equal(1, p.IndexOf("#000000"));
            Assert.Equal(2, p.Version);
            Assert.Equal(Colors.Col("#AABBCC"), p.Colors[0]);
            Assert.Throws<FormatException>(() => p.IndexOf("0123456789abcdef0123456789abcdef"));
            for (int i = 2; i < HexPaletteIndex.Capacity; i++) p.IndexOf($"#{i:X6}");
            Assert.Throws<InvalidOperationException>(() => p.IndexOf("#FEDCBA"));
        }
    }
}
