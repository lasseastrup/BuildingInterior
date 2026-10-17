using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Text;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The derived values against the prototype's own functions, over both corpora
    /// (<c>derived.json</c>). Exact for identities, counts and tier starts; to the recorded
    /// tolerance for heights and coordinates.
    /// </summary>
    public class DerivedTests
    {
        [Fact]
        public void ABuildingsBoundsAreEveryStoreysOutlines()
        {
            // Site.BoundsOf looks at the footprint and each setback's shape once, where it asked every storey for its outline
            // (docs/PERFORMANCE.md §6.2): the same box
            var docs = new[] { PrototypeJson.Read(Fixtures.Text("demo.json")).Document, PrototypeJson.Read(Fixtures.Text("variants.json")).Document };
            var city = new StoreyDocument(); Edit.TestCity.Generate(city, 300, walkIn: true);
            int n = 0;
            foreach (var b in docs.SelectMany(d => d.buildings).Concat(city.buildings))
            {
                double x0 = 1e9, z0 = 1e9, x1 = -1e9, z1 = -1e9;
                for (int k = 0; k <= b.floors.Count; k++)
                    foreach (var p in Derived.OutlineAt(b, k)) { x0 = System.Math.Min(x0, p.x); z0 = System.Math.Min(z0, p.z); x1 = System.Math.Max(x1, p.x); z1 = System.Math.Max(z1, p.z); }
                Assert.Equal((x0 + b.pos.x, z0 + b.pos.z, x1 + b.pos.x, z1 + b.pos.z), Generate.Site.BoundsOf(b));
                n++;
            }
            Assert.True(n > 300);
        }

        /// <summary>
        /// The walk model and the occlusion core sum a building's floor bases going up (docs/PERFORMANCE.md §10.5): the next
        /// storey's from the last's is <see cref="Derived.FloorBase"/>'s value to the bit, from below the ground to the roof,
        /// and the storey at a height is the one the old search from the top found.
        /// </summary>
        [Fact]
        public void FloorBasesSummedGoingUpAreFloorBases()
        {
            var docs = new[] { PrototypeJson.Read(Fixtures.Text("demo.json")).Document, PrototypeJson.Read(Fixtures.Text("variants.json")).Document };
            var city = new StoreyDocument(); Edit.TestCity.Generate(city, 300, walkIn: true);
            int n = 0;
            foreach (var b in docs.SelectMany(d => d.buildings).Concat(city.buildings))
            {
                int N = b.floors.Count;
                for (int k0 = -2; k0 <= 1; k0++)
                {
                    double y = Derived.FloorBase(b, k0);
                    for (int k = k0; k <= N + 1; y = Derived.NextFloorBase(b, k, y), k++)
                        Assert.Equal(System.BitConverter.DoubleToInt64Bits(Derived.FloorBase(b, k) + 0.0), System.BitConverter.DoubleToInt64Bits(y + 0.0));
                }
                int Old(double y) { for (int k = N; k > 0; k--) if (y >= Derived.FloorBase(b, k) - 0.9) return k; return 0; }
                for (double y = b.elevation - 3; y <= Derived.RoofY(b) + 3; y += 0.37) Assert.Equal(Old(y), Play.PlayWorld.FloorAtY(b, y));
                n++;
            }
            Assert.True(n > 300);
        }

        /// <summary>A core stops at a storey just when its list of stops has it (the walk model asks without making the list).</summary>
        [Fact]
        public void ACoreStopsWhereItsStopsSay()
        {
            var docs = new[] { PrototypeJson.Read(Fixtures.Text("demo.json")).Document, PrototypeJson.Read(Fixtures.Text("variants.json")).Document };
            var city = new StoreyDocument(); Edit.TestCity.Generate(city, 300, walkIn: true);
            int cores = 0;
            foreach (var b in docs.SelectMany(d => d.buildings).Concat(city.buildings))
                foreach (var s in b.shafts)
                {
                    var stops = Generate.Cores.Stops(b, s);
                    for (int k = -2; k <= b.floors.Count + 2; k++) Assert.Equal(stops.Contains(k), Generate.Cores.StopsAt(b, s, k));
                    cores++;
                }
            Assert.True(cores > 100, $"{cores} cores");
        }

        static readonly Dictionary<string, object?> Corpus = (Dictionary<string, object?>)Json.Parse(Fixtures.Text("derived.json"))!;
        static double Tol => (double)((Dictionary<string, object?>)Corpus["tolerance"]!)["absolute"]!;

        public static IEnumerable<object[]> Cases()
        {
            foreach (var (file, key) in new[] { ("demo.json", "demo"), ("variants.json", "variants") })
            {
                var doc = PrototypeJson.Read(Fixtures.Text(file)).Document;
                var expected = (List<object?>)Corpus[key]!;
                Assert.Equal(doc.buildings.Count, expected.Count);
                for (int i = 0; i < doc.buildings.Count; i++) yield return new object[] { file, i };
            }
        }

        static (BuildingData b, Dictionary<string, object?> e) Case(string file, int i)
        {
            var doc = PrototypeJson.Read(Fixtures.Text(file)).Document;
            var e = (Dictionary<string, object?>)((List<object?>)Corpus[file == "demo.json" ? "demo" : "variants"]!)[i]!;
            Assert.Equal(doc.buildings[i].id, e["id"]);
            return (doc.buildings[i], e);
        }

        static List<double> Nums(object? v) => ((List<object?>)v!).Select(x => (double)x!).ToList();

        [Theory, MemberData(nameof(Cases))]
        public void FloorBasesAndRoofHeight(string file, int i)
        {
            var (b, e) = Case(file, i);
            var bases = Nums(e["floorBases"]);
            Assert.Equal(b.floors.Count, bases.Count);
            for (int k = 0; k < bases.Count; k++) Assert.InRange(Derived.FloorBase(b, k), bases[k] - Tol, bases[k] + Tol);
            Assert.InRange(Derived.RoofY(b), (double)e["roofY"]! - Tol, (double)e["roofY"]! + Tol);
        }

        [Theory, MemberData(nameof(Cases))]
        public void TierStartsAndOutlines(string file, int i)
        {
            var (b, e) = Case(file, i);
            var starts = Nums(e["tierStarts"]);
            for (int k = 0; k < starts.Count; k++) Assert.Equal((int)starts[k], Derived.TierStart(b, k));

            var outlines = (List<object?>)e["outlines"]!;
            Assert.Equal(b.floors.Count + 1, outlines.Count);
            for (int k = 0; k < outlines.Count; k++)
            {
                var want = ((List<object?>)outlines[k]!).Select(p => Nums(p)).ToList();
                var got = Derived.OutlineAt(b, k);
                Assert.Equal(want.Count, got.Count);
                for (int v = 0; v < want.Count; v++)
                {
                    Assert.InRange(got[v].x, want[v][0] - Tol, want[v][0] + Tol);
                    Assert.InRange(got[v].z, want[v][1] - Tol, want[v][1] + Tol);
                }
            }
        }

        [Theory, MemberData(nameof(Cases))]
        public void StyleResolutionPerFloor(string file, int i)
        {
            var (b, e) = Case(file, i);
            var wall = ((List<object?>)e["styleWall"]!).Cast<string>().ToList();
            var interior = ((List<object?>)e["styleInterior"]!).Cast<string>().ToList();
            for (int k = 0; k < b.floors.Count; k++)
            {
                var st = Derived.StyleAt(b, k);
                Assert.Equal(wall[k], st.wall);
                Assert.Equal(interior[k], st.interior);
            }
        }

        [Theory, MemberData(nameof(Cases))]
        public void ShaftTops(string file, int i)
        {
            var (b, e) = Case(file, i);
            var tops = Nums(e["shaftTops"]);
            Assert.Equal(b.shafts.Count, tops.Count);
            for (int s = 0; s < tops.Count; s++) Assert.Equal((int)tops[s], Derived.ShaftTop(b, b.shafts[s]));
        }
    }
}
