using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// unity/Parity: the demo street mapped to Color Pipeline palette entries, for checking a project with Color
    /// Pipeline against the prototype (unity/Parity/README.md). The entries and the layout must agree, and the
    /// mapped layout must show exactly the demo's colours.
    /// </summary>
    public class ParityFilesTests
    {
        static string Dir => Path.Combine(typeof(ParityFilesTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "UnityRoot").Value!, "Parity");

        static StoreyDocument Mapped() => PrototypeJson.Read(File.ReadAllText(Path.Combine(Dir, "demo-palette.storey"))).Document;

        [Fact]
        public void TheMappedDemoHoldsOnlyPaletteIdsTheBlockKnows()
        {
            var r = PrototypeJson.Read(File.ReadAllText(Path.Combine(Dir, "demo-palette.storey")));
            Assert.Empty(r.Unknown);
            Assert.Empty(ColorMapping.Literals(r.Document));
            Assert.Equal(PaletteSnapshot.UsedIds(r.Document), new SortedSet<string>(r.Document.palette.Keys, StringComparer.Ordinal));
        }

        /// <summary>
        /// demo-project.storey: the demo mapped to the project's own palette by unity/tools/map_to_palette.py. Same
        /// geometry as the demo, nothing but palette ids, each one in the block; its colours are the palette's.
        /// </summary>
        [Fact]
        public void TheProjectMappingHoldsOnlyKnownIdsAndTheDemosGeometry()
        {
            var r = PrototypeJson.Read(File.ReadAllText(Path.Combine(Dir, "demo-project.storey")));
            Assert.Empty(r.Unknown);
            var d = r.Document;
            Assert.Empty(ColorMapping.Literals(d));
            // every colour the prototype has (Storey's own optional colours, frames and foundation, can be left to the project defaults)
            Assert.All(StyleColorFields.Styles(d), st => Assert.All(StyleColorFields.All.Where(f => f.name != "frame" && f.name != "foundationColor" && f.name != "plinth"), f => Assert.True(ColorRef.IsPaletteId(f.get(st)), f.name)));
            Assert.Equal(PaletteSnapshot.UsedIds(d), new SortedSet<string>(d.palette.Keys, StringComparer.Ordinal));

            var demo = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var s0 = new Site(demo.buildings); var s1 = new Site(d.buildings); var colors = new ColorResolver(s1, PaletteSnapshot.Resolver(d));
            for (int i = 0; i < demo.buildings.Count; i++)
            {
                var g0 = Lod0.Build(s0, demo.buildings[i]).Op; var g1 = Lod0.Build(s1, d.buildings[i]).Op;
                Assert.Equal(g0.P, g1.P);
                foreach (var sw in g1.C) Assert.NotEqual(Colors.Col("#FF00FF"), colors.Resolve(sw));
            }
        }

        /// <summary>The tool and the bridge agree on a palette id: a real id from the project's palette.</summary>
        [Fact]
        public void PaletteIdsAreTheTwoHalvesInHex()
        {
            Assert.Equal("629f6fbed82c07cde3e70682c2094cac", ColorRef.PaletteId(7106521602475165645, 16422101724900707500));
            Assert.Equal((7106521602475165645UL, 16422101724900707500UL), ColorRef.Parts("629f6fbed82c07cde3e70682c2094cac"));
            Assert.Equal((7106521602475165645UL, 16422101724900707500UL), ColorRef.Parts("629F6FBED82C07CDE3E70682C2094CAC"));
            Assert.Throws<FormatException>(() => ColorRef.Parts("#FFFFFF"));
        }

        [Fact]
        public void TheEntriesMatchTheBlock()
        {
            var block = Mapped().palette;
            var entries = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "colorpipeline-entries.json"))).RootElement.GetProperty("m_Colors").EnumerateArray().ToList();
            Assert.Equal(block.Count, entries.Count);
            foreach (var e in entries)
            {
                ulong a = e.GetProperty("id").GetProperty("m_Value0").GetUInt64(), b = e.GetProperty("id").GetProperty("m_Value1").GetUInt64();
                // one repeated byte, so the 32-digit string is the same whichever byte order Hash128.ToString uses
                Assert.Equal(a, b);
                var bytes = BitConverter.GetBytes(a);
                Assert.All(bytes, x => Assert.Equal(bytes[0], x));
                string id = string.Concat(Enumerable.Repeat(bytes[0].ToString("x2"), 16));
                Assert.True(block.ContainsKey(id), id);
                var c = e.GetProperty("color");
                string hex = "#" + string.Concat(new[] { "r", "g", "b" }.Select(k => ((int)Math.Round(c.GetProperty(k).GetDouble() * 255)).ToString("X2")));
                Assert.Equal(block[id].hex, hex);
                Assert.Equal(block[id].name, e.GetProperty("name").GetString());
                Assert.Equal("Storey_" + hex.Substring(1), block[id].name);
            }
        }

        /// <summary>Every vertex of every LOD shows the demo's colour, through the palette ids.</summary>
        [Fact]
        public void TheMappedDemoLooksLikeTheDemo()
        {
            var demo = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; var mapped = Mapped();
            var s0 = new Site(demo.buildings); var s1 = new Site(mapped.buildings);
            var c0 = new ColorResolver(s0); var c1 = new ColorResolver(s1, PaletteSnapshot.Resolver(mapped));
            for (int i = 0; i < demo.buildings.Count; i++)
            {
                var g0 = new[] { Lod0.Build(s0, demo.buildings[i]).Op, Lod0.Build(s0, demo.buildings[i]).Glass, Lod1.Build(s0, demo.buildings[i]) };
                var g1 = new[] { Lod0.Build(s1, mapped.buildings[i]).Op, Lod0.Build(s1, mapped.buildings[i]).Glass, Lod1.Build(s1, mapped.buildings[i]) };
                for (int m = 0; m < 3; m++)
                {
                    Assert.Equal(g0[m].P, g1[m].P);
                    for (int v = 0; v < g0[m].C.Count; v++) Assert.Equal(c0.Resolve(g0[m].C[v]), c1.Resolve(g1[m].C[v]));
                }
                var r0 = Lod2.Build(s0, demo.buildings[i]).Rows; var r1 = Lod2.Build(s1, mapped.buildings[i]).Rows;
                for (int r = 0; r < r0.Count; r++) Assert.Equal(r0[r].Texels(c0), r1[r].Texels(c1));
            }
        }
    }
}
