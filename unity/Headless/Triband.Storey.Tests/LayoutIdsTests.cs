using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Every building's id its own, and the counter new ids come from past every id in use (docs/EDITOR.md §2,
    /// <see cref="LayoutIds"/>). In a layout whose counter was behind its ids, a duplicate was given another building's id.
    /// The renderer knows a building by its id and kept one of the two: the other's LOD0 stayed drawn where nothing could
    /// reach it, one more every time the layout was built again, so dragging the building left a trail of its LOD0. Every
    /// edit built the whole layout again too. Storey's own.
    /// </summary>
    public class LayoutIdsTests
    {
        // the demo: buildings b1, b4, b6, b9, b12, b14 and b15, cores s2 to s13, the counter at 16
        static string Demo() => Fixtures.Text("demo.json");

        // the demo with its counter set back, as a layout written by hand or put together from two may have it
        static string Behind(int seq)
        {
            var d = PrototypeJson.Read(Demo()).Document; d.seq = seq;
            return PrototypeJson.Write(d);
        }

        /// <summary>Every building's id and every core's its own, and a row of the building table each: what the renderer goes by.</summary>
        static void EachItsOwn(StoreyDocument d)
        {
            var ids = d.buildings.Select(b => b.id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            var cores = d.buildings.SelectMany(b => b.shafts).Select(s => s.id).ToList();
            Assert.Equal(cores.Count, cores.Distinct(StringComparer.Ordinal).Count());
            var site = new Site(d.buildings);
            Assert.Equal(ids.Count, d.buildings.Select(site.IndexOf).Distinct().Count());
        }

        [Fact]
        public void ADuplicateInALayoutWhoseCounterIsBehindGetsAnIdNoBuildingHas()
        {
            // the counter at 9, the Overlook's number: a duplicate was given the Overlook's id, and the two were one
            // building to the renderer
            Assert.Contains("\"seq\":9", Behind(9).Replace(" ", ""));
            var read = PrototypeJson.Read(Behind(9));
            var d = read.Document;
            Assert.Equal(16, d.seq);   // past b15 and s13
            Assert.Empty(read.Renamed);
            var copy = Buildings.Duplicate(d, d.buildings.First(b => b.name == "Corner Shop"), new Vec2(0, 0));
            Assert.Equal("b16", copy.id);
            Assert.Equal(new[] { "s17" }, copy.shafts.Select(s => s.id));
            EachItsOwn(d);

            // as the site inspector's Duplicate does it, through the edit session
            var s = new EditSession(Behind(9));
            var c = Buildings.Duplicate(s.Document, s.Document.buildings.First(b => b.name == "Corner Shop"), new Vec2(0, 0));
            Assert.True(s.Commit().structural);
            Assert.Equal("b16", c.id);
            EachItsOwn(s.Document);
            EachItsOwn(PrototypeJson.Read(s.Text).Document);
            // New and Add, and a core placed by hand, also take the next id
            Assert.Equal("b18", Buildings.Add(s.Document, "rect", new Vec2(0, 0)).id);
            Assert.Equal("s19", Shafts.NewId(s.Document));
        }

        /// <summary>A layout made in code, its counter set behind its ids: a new id passes over those in use.</summary>
        [Fact]
        public void ANewIdIsNeverOneInUseWhateverTheCounterSays()
        {
            var d = PrototypeJson.Read(Demo()).Document;
            d.seq = 9;
            Assert.Equal("b10", Buildings.NewId(d));   // b9 is the Overlook's
            Assert.Equal(11, d.seq);
            d.seq = 10;
            Assert.Equal("s12", Shafts.NewId(d));   // s10 and s11 are the Overlook's cores
            d.seq = 4;
            var t = Buildings.FromTemplate(d, Buildings.AsTemplate(d.buildings[0]), new Vec2(0, 0));
            Assert.Equal("b5", t.id);   // b4 is the Corner Shop's
            Assert.Equal(new[] { "s6", "s9" }, t.shafts.Select(s => s.id));   // s7 and s8 are the Harbor Office's
            d.seq = 12;
            var copy = Buildings.Duplicate(d, d.buildings.First(b => b.name == "Overlook"), new Vec2(0, 0));
            Assert.Equal("b13", copy.id);   // b12 is the Row House's
            EachItsOwn(d);
        }

        [Fact]
        public void TwoBuildingsWithOneIdAreToldApartAsTheLayoutIsRead()
        {
            // the Westgate Tower with the Corner Shop's id, as a layout put together from two may have it
            var twins = PrototypeJson.Read(Demo()).Document;
            twins.buildings[5].id = "b4";
            string text = PrototypeJson.Write(twins);
            Assert.Equal(2, Regex.Matches(text, "\"id\":\\s*\"b4\"").Count);

            var read = PrototypeJson.Read(text);
            var d = read.Document;
            Assert.Equal(new[] { (5, "b4", "b16") }, read.Renamed);
            Assert.Equal("Westgate Tower", d.buildings[5].name);
            Assert.Equal("b4", d.buildings[1].id);   // the first keeps it: what names it finds it still
            Assert.Equal(17, d.seq);
            EachItsOwn(d);
            // nothing else of the layout changes
            for (int i = 0; i < d.buildings.Count; i++)
            {
                var was = PrototypeJson.ReadBuilding(PrototypeJson.Write(twins.buildings[i]));
                was.id = d.buildings[i].id;
                Assert.Equal(PrototypeJson.Write(was), PrototypeJson.Write(d.buildings[i]));
            }
            // the same ids every time it is read, and nothing to tell apart once written with them
            Assert.Equal(PrototypeJson.Write(d), PrototypeJson.Write(PrototypeJson.Read(text).Document));
            var again = PrototypeJson.Read(PrototypeJson.Write(d));
            Assert.Empty(again.Renamed);
            Assert.Equal(PrototypeJson.Write(d), PrototypeJson.Write(again.Document));

            // the edit session reads it so, and knows the file is behind (the editor saves it then); its undo text too
            var s = new EditSession(text);
            Assert.Equal(read.Renamed, s.Renamed);
            Assert.Equal(PrototypeJson.Write(d), s.Text);
            Assert.True(s.Load(text).None);
            Assert.Empty(new EditSession(s.Text).Renamed);
            Assert.Empty(new EditSession(Demo()).Renamed);
            // the bake the importer makes of it, which the runtime reads
            EachItsOwn(BakedLayout.Of(BakedLayout.Bake(read.Document))!.Document());
        }

        [Fact]
        public void BuildingsWithNoIdAreToldApartToo()
        {
            var read = PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"name\":\"A\"},{\"name\":\"B\"},{\"name\":\"C\",\"id\":\"b3\"},{\"name\":\"D\",\"id\":\"b3\"}]}");
            Assert.Equal(new[] { "", "b4", "b3", "b5" }, read.Document.buildings.Select(b => b.id));
            Assert.Equal(new[] { (1, "", "b4"), (3, "b3", "b5") }, read.Renamed);
            Assert.Equal(6, read.Document.seq);
        }

        /// <summary>
        /// Only the ids a new one could be again move the counter: a building's of the form b12, a core's of the form s7.
        /// Ids of another form (the variant set's buildings are t0, s30, o42…), and numbers past the counter's range, are
        /// left as they are and passed over.
        /// </summary>
        [Fact]
        public void OnlyIdsTheToolsCouldMakeMoveTheCounter()
        {
            var d = PrototypeJson.Read("{\"v\":2,\"seq\":3,\"buildings\":[{\"id\":\"t40\"},{\"id\":\"b007\"},{\"id\":\"house-12\"},{\"id\":\"b99999999999\"}," +
                "{\"id\":\"s41\",\"shafts\":[{\"id\":\"s9\",\"type\":\"stairs\"},{\"id\":\"b30\",\"type\":\"lift\"}]}]}").Document;
            Assert.Equal(10, d.seq);   // past b007 and the core s9; not the building s41 nor the core b30
            Assert.Equal(new[] { "t40", "b007", "house-12", "b99999999999", "s41" }, d.buildings.Select(b => b.id));
            Assert.Equal(new[] { "s9", "b30" }, d.buildings[4].shafts.Select(s => s.id));
        }

        /// <summary>The prototype's layouts have their counter past their ids and no id twice: read as they are written.</summary>
        [Theory]
        [InlineData("demo.json")]
        [InlineData("variants.json")]
        public void TheFixturesAreReadAsTheyAreWritten(string name)
        {
            var text = Fixtures.Text(name);
            var read = PrototypeJson.Read(text);
            Assert.Empty(read.Renamed);
            Assert.Equal(JsonDocument.Parse(text).RootElement.GetProperty("seq").GetInt32(), read.Document.seq);
        }

        /// <summary>
        /// The renderer knows a building by its id (its record, the forced and hidden sets, the editor's pick). Building one
        /// with the id of one it has built lets that one go first, so nothing it made is left drawing out of its reach: a
        /// layout made in code can still hand it two, which one read from a file no longer can.
        /// </summary>
        [Fact]
        public void TheRendererLetsGoOfTheBuildingWithItsIdBeforeKeepingIt()
        {
            var r = File.ReadAllText(UnityAsset.Package("com.triband.storey", "Unity", "Rendering", "SiteRenderer.cs"));
            int a = r.IndexOf("void Build(BuildingData b, Lod2Mesh? made = null)", StringComparison.Ordinal), z = r.IndexOf("void Twin(string id)", a, StringComparison.Ordinal);
            Assert.True(a > 0 && z > a);
            var build = r.Substring(a, z - a);
            int let = build.IndexOf("if (built.TryGetValue(b.id, out var twin)) { Release(twin); Twin(b.id); }", StringComparison.Ordinal);
            int keep = build.IndexOf("built[b.id] = bt; byIdx[bt.idx] = bt;", StringComparison.Ordinal);
            Assert.True(let > 0 && keep > let, "Build lets the one before go, then keeps this one");
            Assert.Single(Regex.Matches(r, @"\bbuilt\[[^\]]+\]\s*=(?!=)"));   // the one place a building is kept
        }
    }
}
