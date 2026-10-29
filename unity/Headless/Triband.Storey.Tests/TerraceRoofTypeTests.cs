using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The roof over a setback's terrace has a shape of its own (<see cref="TerraceRoofData.type"/>, docs/SPEC.md §4.1): a hip, as
    /// the prototype's always is, or a gable, of the outline below and cut away where the setback stands; any other type is
    /// built and read as a hip. Set in the Facade tab with the floors below selected. Storey's own.
    /// </summary>
    public class TerraceRoofTypeTests
    {
        static (BuildingData b, Site site, int k) Block(bool ell)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = ell ? new List<Vec2> { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 8), new Vec2(10, 8), new Vec2(10, 16), new Vec2(0, 16) }
                : new List<Vec2> { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 14), new Vec2(0, 14) };
            b.entrances.Clear(); b.details.Clear(); b.shafts.Clear(); b.interior = false;
            Floors.SetCount(b, 5); b.style = Styles.Preset("brick").Clone();
            int k = Setbacks.Add(b, 0);
            Assert.Equal(OutlineIssue.None, Outlines.Set(b, k, Outlines.Inset(Tiers.Outline(b, k), 2.5)));
            Facades.SetTerraceRoof(b, k, true);
            return (b, new Site(d.buildings), k);
        }

        [Fact]
        public void ATerracesRoofHasTheShapeItIsGiven()
        {
            foreach (bool ell in new[] { false, true })
            {
                var parts = new Dictionary<RoofType, List<RoofPart>>();
                foreach (var rt in new[] { RoofType.Hip, RoofType.Gable, RoofType.Shed, RoofType.Mansard })
                {
                    var (b, site, k) = Block(ell);
                    b.floors[k].terraceRoof!.type = rt;
                    parts[rt] = Roofs.TerraceRoof(site, b, k)!.Parts;
                    // every LOD builds it, nothing flickering
                    Assert.Empty(CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op).Overlaps);
                    Assert.Empty(CoplanarCheck.Run(Lod1.Build(site, b, solids: true)).Overlaps);
                    Assert.True(Lod2.Build(site, b).Tris > 0);
                    // under the setback: none of it over the floors above, where the cut takes it away
                    double y = Derived.FloorBase(b, k);
                    Assert.All(parts[rt].SelectMany(p => p.Pts), q => Assert.InRange(q.y, y - 1.0, y + 12));
                }
                // a hip has no gables, and a gable has a wall at each end
                Assert.DoesNotContain(parts[RoofType.Hip], p => p.Kind == RoofKind.Wall);
                Assert.Contains(parts[RoofType.Gable], p => p.Kind == RoofKind.Wall);
                // a shed or a mansard would tower over the setback or hide it: either is built as the hip
                foreach (var rt in new[] { RoofType.Shed, RoofType.Mansard })
                    Assert.Equal(Shape(parts[RoofType.Hip]), Shape(parts[rt]));
            }
        }

        static string Shape(List<RoofPart> parts) => string.Join(";", parts.Select(p => p.Kind + ":" + string.Join(",", p.Pts.Select(q => $"{q.x:0.###} {q.y:0.###} {q.z:0.###}"))));

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var (b, _, k) = Block(false);
            Assert.DoesNotContain("\"type\"", PrototypeJson.Write(b).Substring(PrototypeJson.Write(b).IndexOf("terraceRoof")));   // a hip, as the prototype's
            b.floors[k].terraceRoof!.type = RoofType.Gable; b.floors[k].terraceRoof!.pitch = 24;
            var json = PrototypeJson.Write(b);
            Assert.Contains("\"terraceRoof\":{\"pitch\":24,\"type\":\"gable\"}", json.Replace(" ", ""));
            var back = PrototypeJson.ReadBuilding(json).floors[k].terraceRoof!;
            Assert.Equal((RoofType.Gable, 24.0), (back.type, back.pitch));
            var d = new StoreyDocument(); d.buildings.Add(b);
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].floors[k].terraceRoof!;
            Assert.Equal((RoofType.Gable, 24.0), (baked.type, baked.pitch));
            // any other type is read as the prototype's hip: flat (that is the terrace), shed or mansard
            foreach (var other in new[] { "flat", "shed", "mansard" })
                Assert.Equal(RoofType.Hip, PrototypeJson.ReadBuilding(json.Replace("\"gable\"", $"\"{other}\"")).floors[k].terraceRoof!.type);
        }
    }
}
