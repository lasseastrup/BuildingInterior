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
