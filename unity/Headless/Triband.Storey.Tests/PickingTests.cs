using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Selecting buildings with the pointer and the Interior tab's storey view (docs/EDITOR.md slices 6.5, 6.6).</summary>
    public class PickingTests
    {
        static StoreyDocument Demo() => PrototypeJson.Read(Fixtures.Text("demo.json")).Document;

        [Fact]
        public void LookingDownFindsTheBuildingUnderThePoint()
        {
            var d = Demo();
            foreach (var (b, i) in d.buildings.Select((b, i) => (b, i)))
            {
                var c = Tiers.Bbox(b.footprint);
                // straight down onto a point inside the footprint (a corner can be notched, so use an inside point)
                var p = Enumerable.Range(1, 9).SelectMany(u => Enumerable.Range(1, 9).Select(v => new Vec2(c.x0 + (c.x1 - c.x0) * u / 10, c.z0 + (c.z1 - c.z0) * v / 10)))
                    .First(q => Geo.Pip(Derived.OutlineAt(b, b.floors.Count), q.x, q.z));
                var hit = Picking.Building(d, new Vec3d(p.x + b.pos.x, 200, p.z + b.pos.z), new Vec3d(0, -1, 0));
                Assert.Equal(i, hit.index);
                Assert.Equal(200 - Derived.RoofY(b), hit.distance, 6);
            }
        }

        [Fact]
        public void TheNearestBuildingWinsAndEmptyGroundIsNone()
        {
            var d = Demo();
            Assert.Equal(-1, Picking.Building(d, new Vec3d(500, 10, 500), new Vec3d(0, -1, 0)).index);
            // along the street through the Row House, the Corner Shop stands first
            int shop = d.buildings.FindIndex(b => b.name == "Corner Shop");
            Assert.Equal(shop, Picking.Building(d, new Vec3d(0, 1.5, -2), new Vec3d(1, 0, 0)).index);
        }

        [Fact]
        public void TheStoreyViewClipsUnderTheSlabAbove()
        {
            var b = Demo().buildings.First(x => x.name == "Linden Court");
            var (clip, lo, hi) = Picking.StoreyView(b, 1);
            Assert.Equal(Derived.FloorBase(b, 2) - Dim.SLAB - 0.01, clip, 9);
            Assert.Equal(Derived.FloorBase(b, 1), lo, 9);
            Assert.Equal(Derived.FloorBase(b, 2) + 0.02, hi, 9);
            Assert.Equal(1e9, Picking.StoreyView(b, b.floors.Count).clipY);
        }
    }
}
