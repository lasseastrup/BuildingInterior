using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Building templates (<see cref="Buildings.AsTemplate"/>, <see cref="Buildings.FromTemplate"/>). Storey's own.</summary>
    public class TemplateTests
    {
        static (StoreyDocument d, BuildingData b) Source()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var b = d.buildings.First(x => x.name == "Linden Court");
            b.voids.Add(new VoidData { id = "v1", kind = VoidKind.Atrium, shape = new() { new Vec2(2, 2), new Vec2(6, 2), new Vec2(6, 6), new Vec2(2, 6) } });
            b.bridges.Add(new BridgeData { id = "br1", to = d.buildings[1].id, k = 2, at = new Vec2(16, 4) });
            return (d, b);
        }

        [Fact]
        public void ATemplateKeepsTheBuildingButNotItsPlace()
        {
            var (_, b) = Source();
            var t = Buildings.AsTemplate(b);
            Assert.Equal("", t.id); Assert.Equal(0, t.pos.x); Assert.Equal(0, t.pos.z);
            Assert.Empty(t.bridges);
            Assert.All(t.shafts, s => Assert.Equal("", s.id));
            Assert.Equal(b.floors.Count, t.floors.Count); Assert.Single(t.voids);
            Assert.Equal(PrototypeJson.WriteStyle(b.style), PrototypeJson.WriteStyle(t.style));
            Assert.NotEmpty(b.bridges);   // the source keeps its own
        }

        [Fact]
        public void PlacingOneMakesANewBuilding()
        {
            var (d, b) = Source();
            var t = PrototypeJson.ReadBuilding(PrototypeJson.Write(Buildings.AsTemplate(b)));   // as the asset stores it
            int had = d.buildings.Count;
            var nb = Buildings.FromTemplate(d, t, new Vec2(200, 200));
            var again = Buildings.FromTemplate(d, t, new Vec2(200, 200));
            Assert.Equal(had + 2, d.buildings.Count);
            Assert.Equal("Linden Court 2", nb.name); Assert.Equal("Linden Court 3", again.name);
            Assert.Equal(d.buildings.Count, d.buildings.Select(x => x.id).Distinct().Count());
            var cores = d.buildings.SelectMany(x => x.shafts).Select(s => s.id).ToList();
            Assert.Equal(cores.Count, cores.Distinct().Count());
            // at a free spot near where it was asked for, clear of the copy placed first
            Assert.True(Tiers.SharedArea(Courtyards.World(nb, nb.footprint), Courtyards.World(again, again.footprint)) < 1e-6);
            Assert.InRange(nb.pos.x, 150, 250);
            // and it builds like the original
            var site = new Site(d.buildings);
            Assert.True(Lod0.Build(site, nb).Op.Tris > 1000);
        }

        [Fact]
        public void ANamelessTemplateGetsATreeName()
        {
            var d = new StoreyDocument(); var t = Buildings.AsTemplate(Buildings.Make(d, "rect", new Vec2(0, 0))); t.name = "";
            Assert.EndsWith("House", Buildings.FromTemplate(d, t, new Vec2(0, 0)).name);
        }
    }
}
