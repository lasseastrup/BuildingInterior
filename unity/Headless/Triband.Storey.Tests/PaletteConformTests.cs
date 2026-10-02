using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Keeping layouts inside the palette (docs/COLOURS.md §3.8). The measure is Color Pipeline's ColorFormulas and the
    /// Model Remapper's rule; checked against them directly outside the repository (their source is Triband's): no
    /// difference over 200,000 random distances, nor over 20,000 random colours remapped against a 338-entry palette.
    /// </summary>
    public class PaletteConformTests
    {
        static PaletteColor P(string id, string name, string hex) { var (r, g, b) = PaletteMatch.Parse(hex); return new PaletteColor(id.PadLeft(32, '0'), name, r, g, b); }

        static readonly List<PaletteColor> Palette = new List<PaletteColor>
        {
            P("1", "Brick", "#8D392C"), P("2", "Sand", "#EAD9B8"), P("3", "Slate", "#556160"), P("4", "Chalk", "#F3F2EE"), P("5", "Coal", "#2B2B2B"), P("6", "Sky", "#8DB3C8"),
        };

        [Fact]
        public void TheMeasureIsColorPipelines()
        {
            Assert.Equal(0, PaletteMatch.Distance(PaletteMatch.Lab(141, 57, 44), PaletteMatch.Lab(141, 57, 44)));
            Assert.Equal(100, PaletteMatch.Distance(PaletteMatch.Lab(0, 0, 0), PaletteMatch.Lab(255, 255, 255)));   // L* 0 to 100
            Assert.Equal(1.0, PaletteMatch.Similarity(0));
            Assert.Equal(0.6, PaletteMatch.Similarity(102), 9);
        }

        [Fact]
        public void TheFirstOfEqualsWins()
        {
            var twins = new List<PaletteColor> { P("a", "First", "#808080"), P("b", "Second", "#808080") };
            Assert.Equal("First", PaletteMatch.Nearest("#818181", twins)!.Value.entry.name);
            Assert.Null(PaletteMatch.Nearest("#818181", new List<PaletteColor>()));
        }

        [Fact]
        public void ALayoutEndsUpWithPaletteColoursOnly()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            Assert.NotEmpty(ColorMapping.Literals(d));
            var changed = PaletteConform.Apply(d, Palette);
            Assert.Empty(ColorMapping.Literals(d));
            var ids = Palette.Select(p => p.id).ToHashSet();
            Assert.All(StyleColorFields.Values(d), v => Assert.Contains(v, ids));
            Assert.All(changed, c => Assert.Equal(PaletteMatch.Nearest(c.from, Palette)!.Value.entry.id, c.id));
            Assert.Equal(PaletteSnapshot.UsedIds(d), d.palette.Keys.ToHashSet());
            Assert.Empty(PaletteConform.Apply(d, Palette));   // nothing left to change
        }

        [Fact]
        public void AnIdThePaletteLostGoesToTheNearestEntryByItsLastColour()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            PaletteConform.Apply(d, Palette);
            string brick = Palette[0].id;
            Assert.Contains(brick, StyleColorFields.Values(d));
            // Brick is deleted from the palette; the layout's palette block still knows it was #8D392C
            var without = Palette.Skip(1).ToList();
            var changed = PaletteConform.Apply(d, without);
            Assert.Contains(changed, c => c.from == brick);
            Assert.DoesNotContain(brick, StyleColorFields.Values(d));
            Assert.All(StyleColorFields.Values(d), v => Assert.Contains(v, without.Select(p => p.id)));
        }

        [Fact]
        public void PresetsAndNewBuildingsAreConformedToo()
        {
            var d = new StoreyDocument();
            var b = Edit.Buildings.Add(d, "rect", new Vec2(0, 0));
            Edit.Styles.ApplyPreset(b, 0, "glass");
            PaletteConform.Apply(d, Palette);
            Assert.All(StyleColorFields.Values(d), v => Assert.True(ColorRef.IsPaletteId(v)));
        }
    }
}
