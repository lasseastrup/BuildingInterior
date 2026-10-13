using System.Collections.Generic;
using System.IO;
using System.Linq;
using Triband.Storey.Edit;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Facade style presets (docs/EDITOR.md, the Facade tab): the prototype's five ship as Facade Style assets, as a
    /// preset saved in the editor is kept, and a style carries its preset's key until a field of it is edited by hand.
    /// </summary>
    public class FacadeStylePresetTests
    {
        [Fact]
        public void ThePrototypesFivePresetsShipAsFacadeStyleAssets()
        {
            var keys = new List<string>();
            foreach (var path in Directory.GetFiles(UnityAsset.Package("com.triband.storey.authoring", "Facade Styles"), "*.asset"))
            {
                var lines = File.ReadAllLines(path);
                string name = Path.GetFileNameWithoutExtension(path);
                UnityAsset.IsA(lines, UnityAsset.Package("com.triband.storey.authoring", "Editor", "FacadeStylePreset.cs"));   // a Facade Style, as Save as preset… makes one
                Assert.Equal(name, UnityAsset.Field(lines, "m_Name"));
                var unknown = new List<string>();
                var style = PrototypeJson.ReadStyle(UnityAsset.Field(lines, "style"), unknown);
                Assert.Empty(unknown);
                Assert.Equal(name, style.label);
                Assert.Null(style.preset);   // a preset's key is its asset's, given to a style applied from it
                keys.Add(UnityAsset.Field(lines, "key"));
            }
            Assert.Equal(Styles.Presets.Select(p => p.key).OrderBy(k => k), keys.OrderBy(k => k));
        }

        [Fact]
        public void AStyleKeepsItsPresetsKeyTillItIsEditedByHand()
        {
            var d = new StoreyDocument();
            var b = Buildings.Make(d, "rect", new Vec2(0, 0));
            b.floors = Enumerable.Range(0, 6).Select(_ => new FloorData()).ToList();
            Assert.Equal("brick", b.style.preset);
            void Edit(System.Action<BuildingData> op) { var looks = Styles.Looks(b); op(b); Styles.ForgetEdited(b, looks); }

            // a project's preset: the style carries its asset's key
            var saved = Styles.Preset("glass"); saved.preset = null; saved.winW = 1.7;
            Edit(bb => Styles.Apply(bb, 0, saved, "0f3c"));
            Assert.Equal("0f3c", b.style.preset);
            Assert.Equal(1.7, b.style.winW);
            // its roof is no part of what a preset sets: editing it keeps the key
            Edit(bb => { bb.style.roofType = RoofType.Gable; bb.style.pitch = 35; });
            Assert.Equal("0f3c", b.style.preset);
            // a field of it edited by hand: Custom
            Edit(bb => bb.style.bay = 2.0);
            Assert.Null(b.style.preset);
            // a preset applied again: its key, and the roof kept
            Edit(bb => Styles.Apply(bb, 0, Styles.Preset("brick"), "brick"));
            Assert.Equal("brick", b.style.preset); Assert.Equal(RoofType.Gable, b.style.roofType);
            // and applied once more after its asset was changed: a new look under the same key, still the preset's
            var changed = Styles.Preset("brick"); changed.wall = "#7A3B28";
            Edit(bb => Styles.Apply(bb, 0, changed, "brick"));
            Assert.Equal("brick", b.style.preset); Assert.Equal("#7A3B28", b.style.wall);
            // a setback's own style starts as the one below it, key and all, and is edited on its own
            Styles.GiveOwn(b, 3);
            Assert.Equal("brick", b.floors[3].style!.preset);
            Edit(bb => Styles.Edited(bb, 3).wall = "#123456");
            Assert.Null(b.floors[3].style!.preset); Assert.Equal("brick", b.style.preset);
        }
    }
}
