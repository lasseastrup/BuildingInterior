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
