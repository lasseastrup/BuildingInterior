using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Colours by reference (docs/COLOURS.md §3.1, §3.2): style colours are CSS literals or palette ids,
    /// the generator emits swatches instead of RGB, and the census tests resolve them back
    /// (<see cref="GeneratorTests"/>). These pin what the census cannot see.
    /// </summary>
    public class ColorTests
    {
        const string PaletteId = "0123456789abcdef0123456789ABCDEF";

        static StoreyDocument Demo() => PrototypeJson.Read(Fixtures.Text("demo.json")).Document;

        [Theory]
        [InlineData("#9A4B38", true, false)]
        [InlineData("#abc", true, false)]
        [InlineData(PaletteId, false, true)]
        [InlineData("9A4B38", false, false)]
        [InlineData("#9A4B3", false, false)]
        [InlineData("red", false, false)]
        [InlineData("0123456789abcdef0123456789abcdeg", false, false)]
        public void ColourReferencesAreLiteralsOrPaletteIds(string s, bool hex, bool id)
        {
            Assert.Equal(hex, ColorRef.IsHex(s));
            Assert.Equal(id, ColorRef.IsPaletteId(s));
        }

        [Fact]
        public void OptionalStyleColoursStayAbsentWhenTheFileHasNone()
        {
            var text = Fixtures.Text("demo.json");
            var doc = PrototypeJson.Read(text).Document;
            Assert.All(doc.buildings, b => { Assert.Null(b.style.door); Assert.Null(b.style.dish); });
            var again = PrototypeJson.Write(doc);
            Assert.DoesNotContain("\"door\"", again);
            Assert.DoesNotContain("\"liftButton\"", again);
        }

        [Fact]
        public void OptionalStyleColoursAndPaletteIdsRoundTrip()
        {
            var doc = Demo();
            doc.buildings[0].style.door = "#226644";
            doc.buildings[0].style.wall = PaletteId;
            var read = PrototypeJson.Read(PrototypeJson.Write(doc));
            Assert.Empty(read.Unknown);
            Assert.Equal("#226644", read.Document.buildings[0].style.door);
            Assert.Equal(PaletteId, read.Document.buildings[0].style.wall);
            Assert.Null(read.Document.buildings[0].style.rail);
        }

        [Theory]
        [InlineData("wall", "blue")]
        [InlineData("door", "#12")]
        public void AColourThatIsNeitherFails(string key, string value)
        {
            string json = "{\"v\":2,\"buildings\":[{\"id\":\"a\",\"floors\":[],\"style\":{\"" + key + "\":\"" + value + "\"}}]}";
            var e = Assert.Throws<FormatException>(() => PrototypeJson.Read(json));
            Assert.Contains(key, e.Message);
        }

        [Fact]
        public void ASetbackTakesItsDoorFromItsOwnStyleAndItsIndoorColoursFromTheBuilding()
        {
            var b = new BuildingData { id = "b" };
            b.footprint.AddRange(new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) });
            b.floors.Add(new FloorData()); b.floors.Add(new FloorData());
            b.style.rail = "#111111"; b.style.door = "#222222";
            b.floors[1].shape.AddRange(new[] { new Vec2(1, 1), new Vec2(9, 1), new Vec2(9, 9), new Vec2(1, 9) });
            b.floors[1].style = new FacadeStyle { wall = "#333333", door = "#444444", rail = "#555555", ceiling = "#666666" };

            var top = Derived.StyleAt(b, 1);
            Assert.Equal("#333333", top.wall);
            Assert.Equal("#444444", top.door);
            Assert.Equal("#111111", top.rail);
            Assert.Null(top.ceiling);
            Assert.Equal(StyleColors.Ceiling, StyleColors.Of(top, ColorSlot.Ceiling));
            Assert.Equal("#222222", StyleColors.Of(Derived.StyleAt(b, 0), ColorSlot.Door));
        }

        [Fact]
        public void UnsetSlotsResolveToThePrototypesFixedColours()
        {
            var doc = Demo(); var site = new Site(doc.buildings); var colors = new ColorResolver(site);
            var C = new Palette(new StyleRef(0, 0));
            Assert.Equal(Colors.Col("#3B3129"), colors.Resolve(C.door));
            Assert.Equal(Colors.Shade("#C9C4BB", 0.85), new ColorResolver(new Site(new List<BuildingData> { new BuildingData { id = "x" } })).Resolve(new Palette(new StyleRef(0, 0)).slabSide));
            Assert.Equal(Colors.Shade(doc.buildings[0].style.wall, 0.72), colors.Resolve(C.plinth));
        }

        [Fact]
        public void APaletteIdResolvesThroughThePaletteAndOnlyThere()
        {
            var doc = Demo(); doc.buildings[0].style.wall = PaletteId;
            var site = new Site(doc.buildings);
            var wall = new Palette(new StyleRef(0, 0)).plinth;
            Assert.Throws<FormatException>(() => new ColorResolver(site).Resolve(wall));
            var got = new ColorResolver(site, id => id == PaletteId ? new Rgb(0.5, 0.25, 1) : throw new KeyNotFoundException(id)).Resolve(wall);
            Assert.Equal(new Rgb(0.36, 0.18, 0.72), got);
        }

        /// <summary>The point of swatches: a building restyled with other colours has the same mesh, swatch for swatch.</summary>
        [Fact]
        public void RecolouringABuildingChangesNoGeometry()
        {
            var doc = Demo(); var site = new Site(doc.buildings); var b = doc.buildings[0];
            var before = Lod0.Build(site, b).Op;
            b.style.wall = "#123456"; b.style.trim = "#654321"; b.style.door = "#0A0B0C"; b.style.interior = PaletteId;
            var after = Lod0.Build(site, b).Op;
            Assert.Equal(before.P, after.P);
            Assert.Equal(before.I, after.I);
            Assert.Equal(before.C, after.C);
        }

        [Fact]
        public void APartyWallShowsTheNeighboursInteriorAndNothingElseOfIt()
        {
            var doc = Demo(); var site = new Site(doc.buildings);
            var foreign = new List<Swatch>();
            for (int i = 0; i < doc.buildings.Count; i++)
                foreach (var gb in new[] { Lod0.Build(site, doc.buildings[i]).Op, Lod1.Build(site, doc.buildings[i]) })
                    foreign.AddRange(gb.C.Where(s => s.style.building != i));
            Assert.NotEmpty(foreign);
            Assert.All(foreign, s => { Assert.Equal(ColorSlot.Interior, s.slot); Assert.Equal(0, s.style.tier); Assert.Equal(1.0, s.tone); });
        }
    }
}
