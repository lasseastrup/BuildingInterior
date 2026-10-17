using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Where Storey Performance Run goes (<see cref="PerformancePaths"/>, docs/PERFORMANCE.md §14): a street clear of every
    /// building, a point on each storey of the tallest that the walk model places in that storey, and the percentiles its
    /// report gives. Storey's own.
    /// </summary>
    public class PerformancePathsTests
    {
        readonly ITestOutputHelper output;
        public PerformancePathsTests(ITestOutputHelper o) { output = o; }

        static Site City(int n, bool walkIn)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, n, walkIn);
            return new Site(d.buildings);
        }

        [Theory]
        [InlineData(300, false)]
        [InlineData(120, true)]
        public void TheStreetIsClearOfEveryBuilding(int n, bool walkIn)
        {
            var site = City(n, walkIn);
            var s = PerformancePaths.Street(site)!.Value;
            double len = Math.Sqrt((s.x1 - s.x0) * (s.x1 - s.x0) + (s.z1 - s.z0) * (s.z1 - s.z0));
            output.WriteLine($"{n} buildings{(walkIn ? ", walk-in" : "")}: a street of {len:0} m from ({s.x0:0}, {s.z0:0}) to ({s.x1:0}, {s.z1:0})");
            Assert.True(s.x0 == s.x1 || s.z0 == s.z1, "straight, along x or along z");
            Assert.True(len > 100, $"{len:0} m");
            for (double t = 0; t <= 1; t += 0.25 / len)
            {
                double x = s.x0 + (s.x1 - s.x0) * t, z = s.z0 + (s.z1 - s.z0) * t;
                foreach (var b in site.Buildings.Take(site.Own))
                {
                    var q = site.Bounds(b);
                    Assert.False(x > q.x0 - 1.5 && x < q.x1 + 1.5 && z > q.z0 - 1.5 && z < q.z1 + 1.5, $"({x:0.0}, {z:0.0}) is within 1.5 m of {b.name}'s box");
                }
            }
            Assert.Null(PerformancePaths.Street(new Site(new System.Collections.Generic.List<BuildingData>())));
        }

        [Theory]
        [InlineData(300, false)]
        [InlineData(120, true)]
        public void EachStoreysPointIsInThatStorey(int n, bool walkIn)
        {
            var site = City(n, walkIn); var world = new PlayWorld(site);
            var top = PerformancePaths.Tallest(site)!;
            Assert.Equal(site.Buildings.Take(site.Own).Max(Derived.RoofY), Derived.RoofY(top));
            foreach (var b in new[] { top }.Concat(site.Buildings.Take(site.Own).Where((x, i) => i % 23 == 0)))
                for (int k = 0; k < b.floors.Count; k++)
                {
                    var (x, y, z) = PerformancePaths.StandOn(b, k);
                    Assert.Equal(Derived.FloorBase(b, k), y);
                    Assert.True(Geo.Pip(Derived.OutlineAt(b, k), x - b.pos.x, z - b.pos.z), $"{b.name} storey {k}: ({x:0.0}, {z:0.0}) outside its outline");
                    var at = world.Locate(x, y + 0.1, z);
                    Assert.True(at != null && at.Value.b.id == b.id && at.Value.floor == k, $"{b.name} storey {k}: the walk model places ({x:0.0}, {y + 0.1:0.0}, {z:0.0}) in {(at == null ? "nothing" : $"{at.Value.b.name} storey {at.Value.floor}")}");
                }
        }

        [Fact]
        public void PercentilesAreByNearestRank()
        {
            var f = new FrameSeries();
            Assert.Equal(0, f.Percentile(99));
            for (int i = 100; i >= 1; i--) f.Add(i);   // 1..100, added out of order
            Assert.Equal(50, f.Percentile(50)); Assert.Equal(95, f.Percentile(95)); Assert.Equal(99, f.Percentile(99));
            Assert.Equal(100, f.Max); Assert.Equal(1, f.Percentile(0));
            f.Add(1000); Assert.Equal(1000, f.Max); Assert.Equal(100, f.Percentile(99));   // 101 values: the 99th is the 100th
            f.Clear(); Assert.Equal(0, f.Count);
        }
    }
}
