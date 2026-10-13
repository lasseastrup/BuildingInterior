using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
        static string UnityRoot =>
            typeof(FacadeStylePresetTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "UnityRoot").Value!;

        /// <summary>A top-level field of a Unity YAML asset, its single-quoted value unquoted (folded lines joined as YAML joins them).</summary>
        static string Field(string[] lines, string name)
        {
            string head = "  " + name + ": ";
            int i = System.Array.FindIndex(lines, l => l.StartsWith(head));
            Assert.True(i >= 0, $"no {name}");
            string v = lines[i].Substring(head.Length);
            if (!v.StartsWith("'")) return v.Trim();
            var sb = new StringBuilder(v.Substring(1));
            // a single-quoted scalar ends at a quote that isn't doubled
            bool Closed(string s) { int q = 0; for (int j = s.Length - 1; j >= 0 && s[j] == '\''; j--) q++; return q % 2 == 1; }
            while (!Closed(sb.ToString())) { var next = lines[++i].Trim(); sb.Append(next.Length == 0 ? "\n" : " " + next); }
            return sb.ToString(0, sb.Length - 1).Replace("''", "'");
        }

        [Fact]
        public void ThePrototypesFivePresetsShipAsFacadeStyleAssets()
        {
            var pkg = Path.Combine(UnityRoot, "Packages", "com.triband.storey.authoring");
            string script = File.ReadAllLines(Path.Combine(pkg, "Editor", "FacadeStylePreset.cs.meta")).First(l => l.StartsWith("guid: ")).Substring(6).Trim();
            var keys = new List<string>();
            foreach (var path in Directory.GetFiles(Path.Combine(pkg, "Facade Styles"), "*.asset"))
            {
                var lines = File.ReadAllLines(path);
                string name = Path.GetFileNameWithoutExtension(path);
                Assert.Contains($"guid: {script}, type: 3", Field(lines, "m_Script"));   // a Facade Style, as Save as preset… makes one
                Assert.Equal(name, Field(lines, "m_Name"));
                var unknown = new List<string>();
                var style = PrototypeJson.ReadStyle(Field(lines, "style"), unknown);
                Assert.Empty(unknown);
                Assert.Equal(name, style.label);
                Assert.Null(style.preset);   // a preset's key is its asset's, given to a style applied from it
                keys.Add(Field(lines, "key"));
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
