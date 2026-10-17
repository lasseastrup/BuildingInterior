using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Text;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The data model against the prototype's own output. Two corpora: the demo street and
    /// the variant set the coplanar checker runs over.
    /// </summary>
    public class PrototypeJsonTests
    {
        public static IEnumerable<object[]> Corpora() => new[] { new object[] { "demo.json" }, new object[] { "variants.json" } };

        [Theory, MemberData(nameof(Corpora))]
        public void EveryKeyThePrototypeWritesHasAField(string file)
        {
            var r = PrototypeJson.Read(Fixtures.Text(file));
            Assert.True(r.Unknown.Count == 0,
                $"{file}: the model has no field for {string.Join(", ", r.Unknown.Distinct().Take(10))}. " +
                "The prototype grew a key; add it to BuildingData and PrototypeJson.");
        }

        /// <summary>
        /// Read, write, and compare as JSON trees (key order ignored, numbers by value): no
        /// field dropped, none invented, every optional key omitted where the prototype omits
        /// it. An absent key and an empty list count as equal, which is how the prototype
        /// reads them.
        /// </summary>
        [Theory, MemberData(nameof(Corpora))]
        public void ReadThenWriteIsTheSameDocument(string file)
        {
            string text = Fixtures.Text(file);
            var original = Json.Parse(text);
            var written = Json.Parse(PrototypeJson.Write(PrototypeJson.Read(text).Document));
            var diffs = new List<string>();
            Diff("", original, written, diffs);
            Assert.True(diffs.Count == 0, $"{file}: {diffs.Count} differences, first: {string.Join("; ", diffs.Take(8))}");
        }

        [Theory, MemberData(nameof(Corpora))]
        public void ReadingWhatWasWrittenGivesTheSameModel(string file)
        {
            var first = PrototypeJson.Read(Fixtures.Text(file)).Document;
            var second = PrototypeJson.Read(PrototypeJson.Write(first));
            Assert.Empty(second.Unknown);
            Assert.Equal(PrototypeJson.Write(first), PrototypeJson.Write(second.Document));
        }

        [Fact]
        public void TheDemoStreetIsWhatTheSpecDescribes()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            Assert.Equal(2, d.v);
            Assert.Equal(7, d.buildings.Count);
            var linden = d.buildings.Single(b => b.name == "Linden Court");
            Assert.Equal(10, linden.floors.Count);
            Assert.True(linden.interior);
            Assert.Equal(6, linden.footprint.Count);
            Assert.Contains(linden.shafts, s => s.type == CoreType.Lift);
            Assert.Contains(linden.details, x => x.kind == Generate.DetailKinds.Escape);
            Assert.Contains(d.buildings, b => !b.interior);                       // a shell
            Assert.Contains(d.buildings, b => b.floors.Any(f => f.HasShape));       // a setback
            Assert.Contains(d.buildings, b => b.entrances.Any(e => e.k > 0));      // a door above ground
            Assert.Contains(d.buildings, b => b.style.roofType != RoofType.Flat);  // a pitched roof
        }

        [Fact]
        public void TheVariantSetCoversWhatTheCoplanarCheckerCovers()
        {
            var d = PrototypeJson.Read(Fixtures.Text("variants.json")).Document;
            Assert.Equal(78, d.buildings.Count);
            foreach (var w in new[] { WindowType.Punched, WindowType.Tall, WindowType.Ribbon, WindowType.Curtain, WindowType.None })
                Assert.Contains(d.buildings, b => b.style.windows == w);
            foreach (var rt in new[] { RoofType.Hip, RoofType.Gable, RoofType.Shed })
                Assert.Contains(d.buildings, b => b.style.roofType == rt);
            Assert.Contains(d.buildings, b => b.floors.Any(f => f.terraceRoof != null));
            Assert.Contains(d.buildings, b => b.floors.Any(f => f.style != null));
            Assert.Contains(d.buildings, b => b.floors.Any(f => f.HasHeight));
            Assert.Contains(d.buildings, b => b.floors.Count(f => f.HasShape) == 2);
            Assert.Equal(Generate.DetailKinds.BuiltIn.OrderBy(k => k, System.StringComparer.Ordinal),
                d.buildings.SelectMany(b => b.details).Select(x => x.kind).Distinct().OrderBy(k => k, System.StringComparer.Ordinal));
        }

        [Fact]
        public void AnUnknownKeyIsReportedWithItsPath()
        {
            var r = PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\",\"floors\":[{\"walls\":[],\"colour\":1}],\"mood\":\"x\"}]}");
            Assert.Equal(new[] { "buildings[0].floors[0].colour", "buildings[0].mood" }, r.Unknown.OrderBy(x => x));
        }

        [Fact]
        public void DeepPathsAreAsBeforeThoughMadeOnlyWhenReported()
        {
            // docs/PERFORMANCE.md §6.1: a path is made into text only for an unknown key or an error, the same text as when
            // every element made its own
            var r = PrototypeJson.Read("{\"v\":2,\"palette\":{\"0123456789abcdef0123456789abcdef\":{\"name\":\"a\",\"hex\":\"#112233\",\"x\":1}},\"buildings\":[{\"id\":\"a\"},{\"id\":\"b\",\"footprint\":[{\"x\":0,\"z\":0,\"y\":1}]," +
                "\"floors\":[{\"walls\":[]},{\"walls\":[{\"a\":{\"x\":0,\"z\":0},\"b\":{\"x\":1,\"z\":0,\"w\":2},\"doors\":[{\"t\":0.5,\"q\":1}]}],\"style\":{\"wall\":\"#112233\",\"zz\":1,\"details\":{\"ac\":2,\"nope\":1}}}]}]}");
            Assert.Equal(new[] { "buildings[1].floors[1].style.details.nope", "buildings[1].floors[1].style.zz", "buildings[1].floors[1].walls[0].b.w", "buildings[1].floors[1].walls[0].doors[0].q", "buildings[1].footprint[0].y", "palette.0123456789abcdef0123456789abcdef.x" },
                r.Unknown.OrderBy(x => x, System.StringComparer.Ordinal));
            var e = Assert.Throws<System.FormatException>(() => PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\"},{\"floors\":[{\"walls\":[{\"doors\":[{\"t\":\"x\"}]}]}]}]}"));
            Assert.Equal("buildings[1].floors[0].walls[0].doors[0].t: expected a number", e.Message);
            e = Assert.Throws<System.FormatException>(() => PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"footprint\":[{\"x\":0,\"z\":0},{\"x\":true}]}]}"));
            Assert.Equal("buildings[0].footprint[1].x: expected a number", e.Message);
            e = Assert.Throws<System.FormatException>(() => PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"style\":{\"roofType\":\"dome\"}}]}"));
            Assert.Equal("buildings[0].style.roofType: 'dome' is not a RoofType", e.Message);
            // an enum's names in any case, and a number as Enum.TryParse took one
            Assert.Equal(RoofType.Gable, PrototypeJson.ReadBuilding("{\"style\":{\"roofType\":\"GABLE\"}}").style.roofType);
            Assert.Equal((RoofType)1, PrototypeJson.ReadBuilding("{\"style\":{\"roofType\":\"1\"}}").style.roofType);
        }

        [Fact]
        public void ReadingMakesLittleBesidesTheJsonTreeAndTheModel()
        {
            // docs/PERFORMANCE.md §6.1: no path text per element, no key list per object, no list or style made only to be
            // replaced, no Enum.TryParse. The 300-building test city: 4.6 MB in all, 3.2 MB of it the JSON tree (Json.Parse),
            // where the reading made 4.6 MB besides the tree
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; Edit.TestCity.Generate(d, 300);
            var text = PrototypeJson.Write(d);
            for (int i = 0; i < 2; i++) PrototypeJson.Read(text);   // warm
            long a0 = System.GC.GetAllocatedBytesForCurrentThread(); Json.Parse(text); long tree = System.GC.GetAllocatedBytesForCurrentThread() - a0;
            a0 = System.GC.GetAllocatedBytesForCurrentThread(); PrototypeJson.Read(text); long all = System.GC.GetAllocatedBytesForCurrentThread() - a0;
            Assert.True(all - tree < 2 << 20, $"{(all - tree) / 1048576.0:0.0} MB besides the {tree / 1048576.0:0.0} MB tree");
        }

        [Fact]
        public void MissingKeysTakeThePrototypesDefaults()
        {
            var b = PrototypeJson.ReadBuilding("{\"id\":\"a\"}");
            Assert.Equal(3.6, b.groundHeight);
            Assert.Equal(3.0, b.floorHeight);
            Assert.True(b.interior);
            Assert.Equal(WindowType.Punched, b.style.windows);
            Assert.Equal(RoofType.Flat, b.style.roofType);
            Assert.Empty(b.floors);
        }

        [Fact]
        public void AnotherDocumentVersionIsRefused()
        {
            Assert.Throws<System.FormatException>(() => PrototypeJson.Read("{\"v\":3,\"buildings\":[]}"));
        }

        [Fact]
        public void TheSummaryCountsWhatTheInspectorShows()
        {
            var s = DocumentSummary.Of(PrototypeJson.Read(Fixtures.Text("demo.json")));
            Assert.Equal(7, s.buildingCount);
            Assert.Equal(7, s.buildingNames.Count);
            Assert.Contains("Linden Court", s.buildingNames);
            Assert.Equal(5, s.walkInCount);   // Westgate Tower and Dockside Store are shells
            Assert.Equal(8, s.coreCount);
            Assert.Empty(s.unknownKeys);
        }

        // ---- structural diff -------------------------------------------------------------

        static bool IsEmptyList(object? v) => v is List<object?> l && l.Count == 0;

        static void Diff(string path, object? a, object? b, List<string> diffs)
        {
            if (diffs.Count > 50) return;
            switch (a)
            {
                case Dictionary<string, object?> oa when b is Dictionary<string, object?> ob:
                    foreach (var key in oa.Keys.Union(ob.Keys))
                    {
                        // An absent key and an empty list mean the same to the prototype (`b.details || []`).
                        if (!oa.ContainsKey(key)) { if (!IsEmptyList(ob[key])) diffs.Add($"{path}.{key} was invented"); continue; }
                        if (!ob.ContainsKey(key)) { if (!IsEmptyList(oa[key])) diffs.Add($"{path}.{key} was dropped"); continue; }
                        Diff($"{path}.{key}", oa[key], ob[key], diffs);
                    }
                    break;
                case List<object?> la when b is List<object?> lb:
                    if (la.Count != lb.Count) { diffs.Add($"{path} has {la.Count} items, written {lb.Count}"); break; }
                    for (int i = 0; i < la.Count; i++) Diff($"{path}[{i}]", la[i], lb[i], diffs);
                    break;
                default:
                    if (!Equals(a, b)) diffs.Add($"{path}: {a ?? "null"} became {b ?? "null"}");
                    break;
            }
        }
    }
}
