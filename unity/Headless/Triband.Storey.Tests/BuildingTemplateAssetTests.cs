using System.Collections.Generic;
using System.IO;
using System.Linq;
using Triband.Storey.Edit;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The building templates the package ships (Building Templates/, docs/EDITOR.md): the prototype's five footprints as
    /// the 3-storey buildings New made of them, kept as Save as template keeps a building; and Start from, which takes a
    /// template's outline with its cut corners.
    /// </summary>
    public class BuildingTemplateAssetTests
    {
        [Fact]
        public void ThePrototypesFiveFootprintsShipAsBuildingTemplates()
        {
            var shapes = new Dictionary<string, string> { ["Rect"] = "rect", ["L"] = "L", ["U"] = "U", ["T"] = "T", ["Octa"] = "oct" };
            var names = new List<string>();
            foreach (var path in Directory.GetFiles(UnityAsset.Package("com.triband.storey.authoring", "Building Templates"), "*.asset"))
            {
                var lines = File.ReadAllLines(path);
                string name = Path.GetFileNameWithoutExtension(path); names.Add(name);
                UnityAsset.IsA(lines, UnityAsset.Package("com.triband.storey.authoring", "Editor", "StoreyBuildingTemplate.cs"));   // as Save as template makes one
                Assert.Equal(name, UnityAsset.Field(lines, "m_Name"));
                var unknown = new List<string>();
                var b = PrototypeJson.ReadBuilding(UnityAsset.Field(lines, "building"), unknown);
                Assert.Empty(unknown);
                Assert.Equal(Setbacks.Preset(shapes[name]), b.footprint);
                Assert.Equal(3, b.floors.Count);
                Assert.Equal("", b.id); Assert.Equal("", b.name);   // a placed one takes the layout's next name
                Assert.Equal("brick", b.style.preset);
            }
            Assert.Equal(shapes.Keys.OrderBy(k => k), names.OrderBy(k => k));
        }

        [Fact]
        public void StartFromTakesATemplatesOutlineWithItsCutCorners()
        {
            var d = new StoreyDocument();
            var t = Buildings.Add(d, "L", new Vec2(0, 0));
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(t, 0, 0, CornerShape.Chamfer, 2, false));
            var template = Buildings.AsTemplate(t);
            var b = Buildings.Add(d, "rect", new Vec2(40, 0));
            Setbacks.ApplyFootprint(b, template.footprint, template.corners);
            Assert.Equal(template.footprint, b.footprint);
            Assert.NotEmpty(template.corners);
            Assert.Equal(template.corners.Select(c => (c.at, c.shape, c.size)), b.corners.Select(c => (c.at, c.shape, c.size)));
            Assert.NotSame(template.corners[0], b.corners[0]);   // the template's own stay its own
            Assert.Single(b.entrances);
            // a footprint preset is the same replacement
            var p = Buildings.Add(d, "T", new Vec2(80, 0)); var q = Buildings.AsTemplate(p); q.id = p.id;
            Setbacks.ApplyPreset(p, "oct"); Setbacks.ApplyFootprint(q, Setbacks.Preset("oct"));
            q.pos = p.pos; q.gen = p.gen;
            Assert.Equal(PrototypeJson.Write(p), PrototypeJson.Write(q));
        }
    }
}
