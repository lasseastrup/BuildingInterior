using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The palette block and the engine-free half of <i>Map colours to palette…</i> (docs/COLOURS.md §3.1, §3.8).
    /// The prototype side (rendering ids through the block, keeping it on save) is checked by driving the
    /// prototype in Chromium; these pin the C# reader, writer and the mapping.
    /// </summary>
    public class PaletteBlockTests
    {
        const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        static StoreyDocument Demo() => PrototypeJson.Read(Fixtures.Text("demo.json")).Document;

        /// <summary>A made-up palette: one id per literal of the demo, keyed by colour.</summary>
        static Dictionary<string, (string id, PaletteEntry entry)> PaletteFor(StoreyDocument d)
        {
            var r = new Dictionary<string, (string, PaletteEntry)>(StringComparer.Ordinal); int i = 0;
            foreach (var hex in ColorMapping.Literals(d).Keys) r[hex] = ((i++).ToString("x32"), new PaletteEntry("C" + i, hex));
            return r;
        }

        [Fact]
        public void TheBlockRoundTripsAndIsLeftOutWhenEmpty()
        {
            var d = Demo();
            Assert.DoesNotContain("\"palette\"", PrototypeJson.Write(d));
            d.palette[A] = new PaletteEntry("BrickRed", "#9A4B38");
            var r = PrototypeJson.Read(PrototypeJson.Write(d));
            Assert.Empty(r.Unknown);
            Assert.Equal(("BrickRed", "#9A4B38"), (r.Document.palette[A].name, r.Document.palette[A].hex));
        }

        [Theory]
        [InlineData("{\"v\":2,\"buildings\":[],\"palette\":{\"red\":{\"name\":\"x\",\"hex\":\"#112233\"}}}", "palette ids")]
        [InlineData("{\"v\":2,\"buildings\":[],\"palette\":{\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\":{\"name\":\"x\",\"hex\":\"blue\"}}}", "hex")]
        [InlineData("{\"v\":2,\"buildings\":[],\"palette\":{\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\":{\"name\":\"x\",\"hex\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\"}}}", "hex")]
        public void AMalformedBlockFails(string json, string mentions)
        {
            var e = Assert.Throws<FormatException>(() => PrototypeJson.Read(json));
            Assert.Contains(mentions, e.Message);
        }

        [Fact]
        public void RefreshKeepsWhatIsUsedAndWhatThePaletteLost()
        {
            var d = Demo();
            d.buildings[0].style.wall = A; d.buildings[1].style.door = B;
            d.palette[B] = new PaletteEntry("Old", "#010203");
            d.palette["cccccccccccccccccccccccccccccccc"] = new PaletteEntry("Unused", "#FFFFFF");
            PaletteSnapshot.Refresh(d, id => id == A ? new PaletteEntry("BrickRed", "#9a4b38") : null);
            Assert.Equal(new[] { A, B }, d.palette.Keys);
            Assert.Equal("#9A4B38", d.palette[A].hex);
            Assert.Equal("Old", d.palette[B].name);
        }

        [Fact]
        public void LiteralsCountEveryFieldAndMatchExactly()
        {
            var d = Demo(); d.buildings[0].style.door = "#abc";
            var lit = ColorMapping.Literals(d);
            Assert.True(lit["#AABBCC"] >= 1);
            Assert.Equal(StyleColorFields.Values(d).Count(ColorRef.IsHex), lit.Values.Sum());
            var m = ColorMapping.ExactMatches(lit.Keys, new[] { (A, "#AABBCC"), (B, "#aabbcc") });
            Assert.Equal(A, m["#AABBCC"]);
            Assert.Throws<ArgumentException>(() => ColorMapping.Apply(d, new Dictionary<string, string> { ["#AABBCC"] = "#112233" }, _ => null));
        }

        /// <summary>Mapping every literal to a palette entry of the same colour changes no vertex colour.</summary>
        [Fact]
        public void AFullyMappedLayoutLooksTheSame()
        {
            var before = Demo(); var after = Demo();
            var pal = PaletteFor(after);
            int n = ColorMapping.Apply(after, pal.ToDictionary(kv => kv.Key, kv => kv.Value.id),
                id => pal.Values.Where(p => p.id == id).Select(p => p.entry).FirstOrDefault());
            Assert.Equal(StyleColorFields.Values(before).Count(), n);
            Assert.Empty(ColorMapping.Literals(after));
            Assert.Equal(pal.Count, after.palette.Count);

            var reread = PrototypeJson.Read(PrototypeJson.Write(after)).Document;
            var s0 = new Site(before.buildings); var s1 = new Site(reread.buildings);
            var c0 = new ColorResolver(s0); var c1 = new ColorResolver(s1, PaletteSnapshot.Resolver(reread));
            Assert.Throws<FormatException>(() => new ColorResolver(s1).Resolve(new Palette(new StyleRef(0, 0)).wall));
            for (int i = 0; i < before.buildings.Count; i++)
            {
                var g0 = Lod0.Build(s0, before.buildings[i]).Op; var g1 = Lod0.Build(s1, reread.buildings[i]).Op;
                Assert.Equal(g0.C.Count, g1.C.Count);
                for (int v = 0; v < g0.C.Count; v++) Assert.Equal(c0.Resolve(g0.C[v]), c1.Resolve(g1.C[v]));
            }
        }

        [Fact]
        public void AnIdMissingFromTheBlockIsMagenta()
        {
            var d = Demo();
            Assert.Equal(Colors.Col("#FF00FF"), PaletteSnapshot.Resolver(d)(A));
        }
    }
}
