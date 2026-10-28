using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The layout baked at import (docs/PERFORMANCE.md §6.3, §6.4): read back, it is the model the JSON gave, field for
    /// field, and a building or its bounds can be read alone.
    /// </summary>
    public class BakedLayoutTests
    {
        public static IEnumerable<object[]> Files => new[] { new object[] { "demo.json" }, new object[] { "variants.json" } };

        static StoreyDocument RoundTrip(StoreyDocument d) => BakedLayout.Of(BakedLayout.Bake(d))!.Document();

        [Theory, MemberData(nameof(Files))]
        public void ALayoutReadsBackAsTheJsonGaveIt(string file)
        {
            var d = PrototypeJson.Read(Fixtures.Text(file)).Document;
            var back = RoundTrip(d);
            Same(d, back, file);
            Assert.Equal(PrototypeJson.Write(d), PrototypeJson.Write(back));
        }

        [Theory]
        [InlineData(3000, false)]
        [InlineData(1000, true)]
        public void TheTestCitiesReadBackAsTheyWere(int n, bool walkIn)
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            if (walkIn) d = new StoreyDocument();
            TestCity.Generate(d, n, walkIn: walkIn);
            var json = PrototypeJson.Read(PrototypeJson.Write(d)).Document;   // as an imported layout is
            var baked = BakedLayout.Of(BakedLayout.Bake(json))!;
            Same(json, baked.Document(), $"{n} buildings");
            Assert.Equal(json.buildings.Count, baked.Count);
            for (int i = 0; i < baked.Count; i += 97)
            {
                Same(json.buildings[i], baked.Building(i), $"building {i}");
                Assert.Equal(Site.BoundsOf(json.buildings[i]), baked.BoundsOf(i));
            }
        }

        /// <summary>Every field of every class of the model, filled at random, comes back: a field the format leaves out reads back as its default.</summary>
        [Fact]
        public void EveryFieldOfTheModelComesBack()
        {
            var rnd = new Random(11);
            for (int run = 0; run < 40; run++)
            {
                var d = (StoreyDocument)Fill(typeof(StoreyDocument), rnd, 0);
                Same(d, RoundTrip(d), $"run {run}");
            }
        }

        /// <summary>
        /// The model's shape, pinned: a field or an enum value added, removed or renamed changes the format. Then update
        /// <see cref="BakedLayout.Bake"/> and the reading for it, bump <see cref="BakedLayout.Version"/> (the importer's
        /// version follows it, so Unity bakes every layout again; bytes of the old version aren't read) and pin the new hash.
        /// </summary>
        [Fact]
        public void TheModelIsTheShapeTheFormatWasMadeFor()
        {
            var sb = new StringBuilder();
            foreach (var t in ModelTypes.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                sb.Append(t.FullName).Append('{');
                if (t.IsEnum) foreach (var v in Enum.GetValues(t)) sb.Append(v).Append('=').Append(Convert.ToInt32(v)).Append(';');
                else foreach (var f in Fields(t)) sb.Append(f.Name).Append(':').Append(f.FieldType).Append(';');
                sb.Append('}');
            }
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
            Assert.True(hash == "81A8C9D451EBEB35", $"the model's shape is {hash}: see this test's summary ({sb})");
        }

        /// <summary>A number the enum doesn't name, which the JSON reader takes ("roofType": "7"), bakes and reads back as it was.</summary>
        [Fact]
        public void AnEnumNumberWithoutANameComesBack()
        {
            var d = PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\",\"style\":{\"roofType\":\"7\",\"windows\":\"2\"}}]}").Document;
            Assert.Equal((RoofType)7, d.buildings[0].style.roofType);
            var back = RoundTrip(d);
            Assert.Equal((RoofType)7, back.buildings[0].style.roofType);
            Assert.Equal(WindowType.Ribbon, back.buildings[0].style.windows);
        }

        [Fact]
        public void OtherBytesAreNotALayout()
        {
            Assert.Null(BakedLayout.Of(null));
            Assert.Null(BakedLayout.Of(Array.Empty<byte>()));
            Assert.Null(BakedLayout.Of(Encoding.UTF8.GetBytes("{\"v\":2,\"buildings\":[]} and some more text")));
            var bytes = BakedLayout.Bake(PrototypeJson.Read(Fixtures.Text("demo.json")).Document);
            var other = (byte[])bytes.Clone(); other[4] = BakedLayout.Version + 1;
            Assert.Null(BakedLayout.Of(other));
            Assert.Null(BakedLayout.Of(bytes.Take(20).ToArray()));
            Assert.NotNull(BakedLayout.Of(bytes));
        }

        /// <summary>Each distinct string is made once a layout: 21,049 colour strings with 36 distinct values were each their own (§6.7).</summary>
        [Fact]
        public void EachDistinctStringIsMadeOnce()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 300);
            var back = RoundTrip(d);
            var walls = back.buildings.Select(b => b.style.wall).ToList();
            foreach (var g in walls.GroupBy(w => w)) Assert.All(g, w => Assert.Same(g.First(), w));
            var json = PrototypeJson.Read(PrototypeJson.Write(d)).Document;
            int distinctInJson = json.buildings.Select(b => b.style.wall).Distinct(ReferenceEqualityComparer.Instance).Count();
            int distinctBaked = walls.Distinct(ReferenceEqualityComparer.Instance).Count();
            Assert.True(distinctBaked < distinctInJson, $"{distinctBaked} wall strings, {distinctInJson} from the JSON");
            // a building's style is its own: changing one changes no other
            back.buildings[0].style.wall = "#000000";
            Assert.NotEqual("#000000", back.buildings[1].style.wall);
        }

        [Fact]
        public void ReadingFromSeveralThreadsGivesTheSameLayout()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 400);
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!;
            var docs = new StoreyDocument[8];
            Parallel.For(0, docs.Length, i => docs[i] = baked.Document());
            foreach (var x in docs) Same(d, x, "a thread's read");
        }

        // ---- the model, by reflection --------------------------------------------------------

        static readonly Type[] ModelTypes =
        {
            typeof(StoreyDocument), typeof(PaletteEntry), typeof(BuildingData), typeof(BridgeData), typeof(VoidData), typeof(CornerData),
            typeof(FloorData), typeof(WallData), typeof(DoorData), typeof(CoreData), typeof(EntranceData), typeof(DetailData),
            typeof(TerraceRoofData), typeof(FacadeStyle), typeof(DetailRules), typeof(OpeningEntry), typeof(Vec2),
            typeof(VoidKind), typeof(WindowPick), typeof(CornerShape), typeof(CoreType), typeof(WindowType), typeof(HeadType), typeof(GroundType), typeof(RoofType),
        };

        static IEnumerable<FieldInfo> Fields(Type t) => t.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken);

        /// <summary>Field for field, through lists, dictionaries and nested objects.</summary>
        static void Same(object? a, object? b, string path)
        {
            if (a == null || b == null) { Assert.True(a == null && b == null, $"{path}: {a ?? "null"} against {b ?? "null"}"); return; }
            var t = a.GetType();
            Assert.Equal(t, b.GetType());
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
            {
                if (a is double x && b is double y) Assert.True(BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y), $"{path}: {x} against {y}");
                else Assert.True(Equals(a, b), $"{path}: {a} against {b}");
                return;
            }
            if (a is IDictionary da)
            {
                var db = (IDictionary)b;
                Assert.True(da.Count == db.Count, $"{path}: {da.Count} entries against {db.Count}");
                var ka = da.Keys.Cast<object>().ToList(); var kb = db.Keys.Cast<object>().ToList();
                for (int i = 0; i < ka.Count; i++) { Same(ka[i], kb[i], $"{path} key {i}"); Same(da[ka[i]], db[kb[i]], $"{path}[{ka[i]}]"); }
                return;
            }
            if (a is IList la)
            {
                var lb = (IList)b;
                Assert.True(la.Count == lb.Count, $"{path}: {la.Count} items against {lb.Count}");
                for (int i = 0; i < la.Count; i++) Same(la[i], lb[i], $"{path}[{i}]");
                return;
            }
            foreach (var f in Fields(t)) Same(f.GetValue(a), f.GetValue(b), $"{path}.{f.Name}");
        }

        static readonly NullabilityInfoContext Nullability = new NullabilityInfoContext();
        static readonly string[] Words = { "", "a", "North", "#9A4B38", "#abc", "0123456789abcdef0123456789abcdef", "ac", "escape", "Ünïcødé 名前" };

        /// <summary>An object of the model's type <paramref name="t"/> with every field set at random.</summary>
        static object Fill(Type t, Random r, int depth)
        {
            var o = Activator.CreateInstance(t)!;
            foreach (var f in Fields(t))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                bool nullable = Nullability.Create(f).WriteState == NullabilityState.Nullable;
                f.SetValue(o, Value(f.FieldType, nullable, r, depth));
            }
            return o;
        }

        static object? Value(Type t, bool nullable, Random r, int depth)
        {
            var under = Nullable.GetUnderlyingType(t);
            if (under != null) return r.Next(3) == 0 ? null : Value(under, false, r, depth);
            if (t == typeof(string)) return nullable && r.Next(3) == 0 ? null : Words[r.Next(Words.Length)] + (r.Next(2) == 0 ? "" : r.Next(1000).ToString());
            if (t == typeof(int)) return r.Next(-50, 50) * (r.Next(4) == 0 ? 100000 : 1);
            if (t == typeof(double)) return r.Next(5) == 0 ? (double)r.Next(-3, 3) : (r.NextDouble() - 0.5) * Math.Pow(10, r.Next(-3, 6));
            if (t == typeof(bool)) return r.Next(2) == 0;
            if (t.IsEnum) { var vs = Enum.GetValues(t); return vs.GetValue(r.Next(vs.Length)); }
            if (t == typeof(Vec2)) return new Vec2((r.NextDouble() - 0.5) * 200, (r.NextDouble() - 0.5) * 200);
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (nullable && r.Next(3) == 0) return null;   // a style's sets: none
                var list = (IList)Activator.CreateInstance(t)!; var item = t.GetGenericArguments()[0];
                int n = depth > 3 ? 0 : r.Next(4);
                for (int i = 0; i < n; i++) list.Add(Value(item, false, r, depth + 1));
                return list;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(SortedDictionary<,>))
            {
                if (nullable && r.Next(3) == 0) return null;
                var dict = (IDictionary)Activator.CreateInstance(t, StringComparer.Ordinal)!; var args = t.GetGenericArguments();
                int n = r.Next(4);
                for (int i = 0; i < n; i++) dict[Words[r.Next(Words.Length)] + i] = Value(args[1], false, r, depth + 1);
                return dict;
            }
            if (t.IsClass) return nullable && r.Next(3) == 0 ? null : Fill(t, r, depth + 1);
            throw new InvalidOperationException($"the model has a {t}: teach this test to fill it");
        }
    }
}
