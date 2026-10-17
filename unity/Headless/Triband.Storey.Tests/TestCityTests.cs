using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The stress-test city (<see cref="TestCity"/>) is the prototype's, building for building (fixtures/city.json).</summary>
    public class TestCityTests
    {
        static StoreyDocument Demo() => PrototypeJson.Read(Fixtures.Text("demo.json")).Document;

        [Fact]
        public void TheCityIsThePrototypes()
        {
            var want = JsonDocument.Parse(Fixtures.Text("city.json")).RootElement.EnumerateArray().ToList();
            var d = Demo();
            Assert.Equal(want.Count, TestCity.Generate(d, want.Count));
            var got = d.buildings.Where(b => b.gen).ToList();
            Assert.Equal(want.Count, got.Count);
            static double N(JsonElement e) => e.GetDouble();
            for (int i = 0; i < want.Count; i++)
            {
                var w = want[i]; var b = got[i]; string at = $"building {i} ({w.GetProperty("name").GetString()})";
                Assert.True(w.GetProperty("name").GetString() == b.name, $"{at}: named {b.name}");
                var pos = w.GetProperty("pos").EnumerateArray().Select(N).ToArray();
                Assert.True(Math.Abs(pos[0] - b.pos.x) < 1e-3 && Math.Abs(pos[1] - b.pos.z) < 1e-3, $"{at}: at {b.pos}");
                var fp = w.GetProperty("fp").EnumerateArray().Select(q => q.EnumerateArray().Select(N).ToArray()).ToList();
                Assert.True(fp.Count == b.footprint.Count, $"{at}: {b.footprint.Count} corners");
                for (int j = 0; j < fp.Count; j++) Assert.True(Math.Abs(fp[j][0] - b.footprint[j].x) < 1e-3 && Math.Abs(fp[j][1] - b.footprint[j].z) < 1e-3, $"{at}: corner {j}");
                Assert.True(w.GetProperty("floors").GetInt32() == b.floors.Count, $"{at}: {b.floors.Count} floors");
                Assert.Equal(N(w.GetProperty("gh")), b.groundHeight, 3); Assert.Equal(N(w.GetProperty("fh")), b.floorHeight, 3);
                Assert.True(w.GetProperty("style").GetString() == b.style.preset, $"{at}: style {b.style.preset}");
                Assert.True(w.GetProperty("roof").GetString() == b.style.roofType.ToString().ToLowerInvariant(), $"{at}: roof {b.style.roofType}");
                if (w.GetProperty("pitch").ValueKind == JsonValueKind.Number) Assert.Equal(N(w.GetProperty("pitch")), b.style.pitch!.Value, 3);
                Assert.True(w.GetProperty("interior").GetBoolean() == b.interior, $"{at}: interior");
                Assert.True(w.GetProperty("shafts").GetInt32() == b.shafts.Count, $"{at}: cores");
                Assert.Equal(N(w.GetProperty("door")), b.entrances[0].t, 3);
                Assert.Equal(w.GetProperty("blank").EnumerateArray().Select(e => e.GetInt32()), b.blank);
            }
        }

        [Fact]
        public void MakingItAgainReplacesIt()
        {
            var d = Demo(); int hand = d.buildings.Count;
            TestCity.Generate(d, 500); TestCity.Generate(d, 200);
            Assert.Equal(hand + 200, d.buildings.Count);
            Assert.Equal(200, TestCity.Clear(d));
            Assert.Equal(hand, d.buildings.Count);
            Assert.Equal(d.buildings.Count, d.buildings.Select(b => b.id).Distinct().Count());
        }

        [Fact]
        public void AWalkInCityHasStairsInEveryBuilding()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            int n = TestCity.Generate(d, 300, walkIn: true);
            var gen = d.buildings.Where(b => b.gen).ToList();
            Assert.Equal(300, n);
            Assert.All(gen, b => Assert.True(b.interior));
            int stairs = gen.Count(b => b.shafts.Any(s => s.type == CoreType.Stairs));
            int lifts = gen.Count(b => b.shafts.Any(s => s.type == CoreType.Lift));
            Assert.True(stairs >= gen.Count * 0.95, $"{stairs} of {gen.Count} have stairs");
            Assert.All(gen, b => Assert.All(b.shafts, s => Assert.True(Cores.CoreFits(b.footprint, s))));
            Assert.True(lifts > 0, "the tall ones have lifts");
            // the same city otherwise: positions, outlines and heights as the prototype's
            var plain = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(plain, 300);
            var pg = plain.buildings.Where(b => b.gen).ToList();
            for (int i = 0; i < gen.Count; i++) { Assert.Equal(pg[i].pos.x, gen[i].pos.x); Assert.Equal(pg[i].floors.Count, gen[i].floors.Count); Assert.Equal(pg[i].footprint.Count, gen[i].footprint.Count); }
        }

        static List<BuildingData> Gen(StoreyDocument d) => d.buildings.Where(b => b.gen).ToList();

        /// <summary>
        /// A walk-in share makes exactly that share of the buildings walk-in, with stairs to the top where they fit, and the
        /// rest shells; the city is the prototype's otherwise, building for building. A share of 1 is the walk-in city.
        /// </summary>
        [Fact]
        public void AWalkInShareMakesThatShareWalkIn()
        {
            var plain = Demo(); TestCity.Generate(plain, 400); var pg = Gen(plain);
            foreach (var share in new[] { 0.0, 0.1, 0.37, 1.0 })
            {
                var d = Demo(); TestCity.Generate(d, new TestCity.Options { Count = 400, WalkIn = share }); var g = Gen(d);
                Assert.Equal(400, g.Count);
                var walk = g.Where(b => b.interior).ToList();
                Assert.Equal((int)Math.Round(share * 400), walk.Count);
                Assert.All(g.Where(b => !b.interior), b => Assert.Empty(b.shafts));
                if (walk.Count > 0) Assert.True(walk.Count(b => b.shafts.Any(c => c.type == CoreType.Stairs)) >= walk.Count * 0.95, $"{share}: stairs in too few");
                for (int i = 0; i < g.Count; i++)
                {
                    Assert.Equal((pg[i].pos.x, pg[i].pos.z), (g[i].pos.x, g[i].pos.z));
                    Assert.Equal(pg[i].floors.Count, g[i].floors.Count); Assert.Equal(pg[i].footprint.Count, g[i].footprint.Count);
                    Assert.Equal(pg[i].style.preset, g[i].style.preset); Assert.Equal(pg[i].style.roofType, g[i].style.roofType);
                }
            }
            // all of them: the walk-in city, to the id
            var one = Demo(); TestCity.Generate(one, new TestCity.Options { Count = 400, WalkIn = 1 });
            var walkIn = Demo(); TestCity.Generate(walkIn, 400, walkIn: true);
            Assert.Equal(PrototypeJson.Write(walkIn), PrototypeJson.Write(one));
            // spread through the city, not bunched at its start
            var tenth = Demo(); TestCity.Generate(tenth, new TestCity.Options { Count = 400, WalkIn = 0.1 });
            var at = Gen(tenth).Select((b, i) => (b, i)).Where(x => x.b.interior).Select(x => x.i).ToList();
            Assert.True(at.Min() < 100 && at.Max() >= 300, $"walk-in from {at.Min()} to {at.Max()}");
        }

        /// <summary>Asked for near the middle, the walk-in buildings are the first made, which the city is made from the middle out.</summary>
        [Fact]
        public void TheWalkInBuildingsCanBeTheNearestTheMiddle()
        {
            var d = Demo(); TestCity.Generate(d, new TestCity.Options { Count = 400, WalkIn = 0.1, WalkInNearMiddle = true }); var g = Gen(d);
            Assert.All(g.Take(40), b => Assert.True(b.interior));
            Assert.All(g.Skip(40), b => Assert.False(b.interior));
            static double Far(BuildingData b) => Math.Sqrt(b.pos.x * b.pos.x + b.pos.z * b.pos.z);
            Assert.True(g.Where(b => b.interior).Average(Far) < g.Average(Far) / 2, "nearer the middle than the city as a whole");
        }

        /// <summary>The heights are the prototype's scaled and capped; nothing else of the city changes.</summary>
        [Fact]
        public void TheHeightsAreScaledAndCapped()
        {
            var plain = Demo(); TestCity.Generate(plain, 300); var pg = Gen(plain);
            var tall = Demo(); TestCity.Generate(tall, new TestCity.Options { Count = 300, HeightScale = 2, MaxStoreys = 50 }); var tg = Gen(tall);
            var low = Demo(); TestCity.Generate(low, new TestCity.Options { Count = 300, MaxStoreys = 3 }); var lg = Gen(low);
            var cap = Demo(); TestCity.Generate(cap, new TestCity.Options { Count = 300, HeightScale = 3, MaxStoreys = 500 }); var cg = Gen(cap);
            for (int i = 0; i < pg.Count; i++)
            {
                Assert.Equal(Math.Min(50, (int)Math.Floor(pg[i].floors.Count * 2 + 0.5)), tg[i].floors.Count);
                Assert.Equal(Math.Min(3, pg[i].floors.Count), lg[i].floors.Count);
                Assert.True(cg[i].floors.Count <= TestCity.MostStoreys);
                Assert.Equal((pg[i].pos.x, pg[i].pos.z, pg[i].style.preset), (tg[i].pos.x, tg[i].pos.z, tg[i].style.preset));
            }
            Assert.Contains(cg, b => b.floors.Count == TestCity.MostStoreys);
        }

        /// <summary>Another seed lays out another city of as many buildings; the prototype's is the default.</summary>
        [Fact]
        public void AnotherSeedLaysOutAnotherCity()
        {
            var a = Demo(); TestCity.Generate(a, new TestCity.Options { Count = 300 });
            var b = Demo(); TestCity.Generate(b, 300);
            Assert.Equal(PrototypeJson.Write(b), PrototypeJson.Write(a));
            var c = Demo(); Assert.Equal(300, TestCity.Generate(c, new TestCity.Options { Count = 300, Seed = 7 }));
            var ag = Gen(a); var cg = Gen(c);
            Assert.True(Enumerable.Range(0, 300).Count(i => ag[i].floors.Count != cg[i].floors.Count || ag[i].footprint.Count != cg[i].footprint.Count) > 100, "another city");
        }

        /// <summary>A walk-in building gets a lift above the storeys asked for, where one fits; the summary counts it all.</summary>
        [Fact]
        public void LiftsComeAboveTheStoreysAskedForAndTheSummaryCountsThem()
        {
            int Lifts(int above)
            {
                var d = Demo(); TestCity.Generate(d, new TestCity.Options { Count = 200, WalkIn = 1, LiftAbove = above });
                Assert.All(Gen(d), b => Assert.True(b.floors.Count > above || b.shafts.All(c => c.type != CoreType.Lift)));
                return TestCity.Summary(d).lifts;
            }
            Assert.True(Lifts(2) > Lifts(5) && Lifts(5) > Lifts(12), "more buildings with lifts the lower they start");
            var m = Demo(); TestCity.Generate(m, new TestCity.Options { Count = 250, WalkIn = 0.2 });
            var g = Gen(m); var (n, walk, storeys, tallest, lifts) = TestCity.Summary(m);
            Assert.Equal((250, 50, g.Sum(b => b.floors.Count), g.Max(b => b.floors.Count), g.Sum(b => b.shafts.Count(c => c.type == CoreType.Lift))), (n, walk, storeys, tallest, lifts));
        }

        /// <summary>A mixed city, taller than the prototype's, can be walked: every generated building's storeys are reached.</summary>
        [Fact]
        public void ATallerMixedCityCanBeWalked()
        {
            var d = Demo();
            TestCity.Generate(d, new TestCity.Options { Count = 60, WalkIn = 0.5, HeightScale = 1.5, MaxStoreys = TestCity.MostStoreys, LiftAbove = 3 });
            var site = new Site(d.buildings);
            var gen = new HashSet<string>(d.buildings.Where(b => b.gen && b.shafts.Count > 0).Select(b => b.id));
            var bad = Validate.Problems.Check(site).Where(p => gen.Contains(p.buildingId) && p.severity == Validate.Severity.Error).ToList();
            Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
        }

        [Fact]
        public void EveryStoreyOfAWalkInCityCanBeReached()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            TestCity.Generate(d, 60, walkIn: true);
            var site = new Site(d.buildings);
            var gen = new HashSet<string>(d.buildings.Where(b => b.gen && b.shafts.Count > 0).Select(b => b.id));
            var bad = Validate.Problems.Check(site).Where(p => gen.Contains(p.buildingId) && p.severity == Validate.Severity.Error).ToList();
            Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
        }
    }
}
