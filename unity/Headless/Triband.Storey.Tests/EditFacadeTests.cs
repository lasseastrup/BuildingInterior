using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The Facade tab's edits and new buildings (docs/EDITOR.md slices 6.4, 6.7) against the prototype:
    /// unity/Fixtures/ops-facade.json, replayed like ops-interior.json.
    /// </summary>
    public class EditFacadeTests
    {
        static readonly Lazy<OpsReplay> Data = new(() => new OpsReplay("ops-facade.json"));

        public static IEnumerable<object[]> Names() => Data.Value.Names();

        [Theory, MemberData(nameof(Names))]
        public void MatchesThePrototype(string name) => Data.Value.Check(name, Step);

        static FacadeHit Hit(JsonElement e) => new FacadeHit
        {
            i = e.GetProperty("i").GetInt32(), t = e.GetProperty("t").GetDouble(), L = e.GetProperty("L").GetDouble(),
            y = e.GetProperty("y").GetDouble(), k0 = e.GetProperty("k0").GetInt32(), k = e.GetProperty("k").GetInt32(),
        };

        static DetailKind Kind(JsonElement e) => Enum.Parse<DetailKind>(e.GetString()!, true);

        static object? Detail(DetailData? d)
        {
            if (d == null) return null;
            var o = new Dictionary<string, object?> { ["kind"] = d.kind.ToString().ToLowerInvariant(), ["k"] = d.k, ["edge"] = d.edge, ["t"] = d.t };
            if (d.y.HasValue) o["y"] = d.y.Value;
            return o;
        }

        static object? Box(GhostBox? g) => g == null ? null : new Dictionary<string, object?>
        {
            ["cx"] = g.Value.cx, ["cy"] = g.Value.cy, ["cz"] = g.Value.cz, ["sx"] = g.Value.sx, ["sy"] = g.Value.sy, ["sz"] = g.Value.sz,
            ["u"] = new[] { g.Value.u.x, g.Value.u.z },
        };

        static object? Step(Site site, BuildingData b, string op, JsonElement[] a)
        {
            switch (op)
            {
                case "detailAt": { var r = Facades.DetailAt(site, b, Hit(a[0]), Kind(a[1])); return new Dictionary<string, object?> { ["add"] = Detail(r.add), ["remove"] = r.remove, ["error"] = r.error, ["box"] = Box(r.ghost) }; }
                case "toggleDetail": return Facades.ToggleDetail(site, b, Hit(a[0]), Kind(a[1]));
                case "entranceAt":
                {
                    var (d, err) = Facades.EntranceAt(site, b, Hit(a[0]));
                    return err != null ? new Dictionary<string, object?> { ["error"] = err } : new Dictionary<string, object?> { ["door"] = EditInteriorTests.Door(d) };
                }
                case "toggleEntrance": return Facades.ToggleEntrance(site, b, Hit(a[0]));
                case "toggleBlank": Facades.ToggleBlank(b, a[0].GetInt32(), a[1].GetInt32()); return null;
                case "applyPreset": Styles.ApplyPreset(b, a[0].GetInt32(), a[1].GetString()!); return null;
                case "giveOwnStyle": Styles.GiveOwn(b, a[0].GetInt32()); return null;
                case "matchBelow": Styles.MatchBelow(b, a[0].GetInt32()); return null;
                case "setTerraceRoof": Facades.SetTerraceRoof(b, a[0].GetInt32(), a[1].GetBoolean()); return null;
                case "drivesRoof": return Styles.DrivesRoof(b, a[0].GetInt32());
                default: throw new NotSupportedException("no C# step for " + op);
            }
        }

        [Fact]
        public void NewBuildingsGoWhereThePrototypePutsThem()
        {
            var doc = Data.Value.Demo;
            foreach (var s in Data.Value.Root.GetProperty("freeSpots").EnumerateArray())
            {
                var near = s.GetProperty("near"); var spot = s.GetProperty("spot");
                var got = Buildings.FreeSpot(doc, s.GetProperty("w").GetDouble(), s.GetProperty("d").GetDouble(), new Vec2(near[0].GetDouble(), near[1].GetDouble()));
                Assert.Equal((spot[0].GetDouble(), spot[1].GetDouble()), (got.x, got.z));
            }
            Assert.Equal(Data.Value.Root.GetProperty("nextName").GetString(), Buildings.NextName(doc));
        }

        [Fact]
        public void MakeBuildsWhatThePrototypeBuilds()
        {
            var doc = PrototypeJson.Read(PrototypeJson.Write(Data.Value.Demo)).Document;
            foreach (var m in Data.Value.Root.GetProperty("made").EnumerateArray())
            {
                doc.seq = m.GetProperty("seq").GetInt32();
                var b = Buildings.Make(doc, m.GetProperty("shape").GetString()!, new Vec2(3, 4), m.GetProperty("style").GetString()!);
                Assert.Equal(PrototypeJson.Write(PrototypeJson.ReadBuilding(m.GetProperty("building").GetRawText())), PrototypeJson.Write(b));
            }
        }

        [Fact]
        public void DuplicatingGivesNewIdsAndAFreeSpot()
        {
            var doc = PrototypeJson.Read(PrototypeJson.Write(Data.Value.Demo)).Document;
            var src = doc.buildings.First(x => x.shafts.Count > 0);
            var copy = Buildings.Duplicate(doc, src, new Vec2(0, 0));
            Assert.NotEqual(src.id, copy.id);
            Assert.Equal(src.name + " copy", copy.name);
            Assert.Empty(copy.shafts.Select(s => s.id).Intersect(doc.buildings.Where(x => x != copy).SelectMany(x => x.shafts).Select(s => s.id)));
            Assert.Equal(src.floors.Count, copy.floors.Count);
            Assert.True(Buildings.Delete(doc, copy.id));
            Assert.DoesNotContain(doc.buildings, x => x.id == copy.id);
        }

        [Fact]
        public void PickingFindsTheFacadeTheRayMeets()
        {
            var doc = PrototypeJson.Read(PrototypeJson.Write(Data.Value.Demo)).Document;
            var b = doc.buildings.First(x => x.name == "Westgate Tower");   // a 14 x 11 rectangle
            var fp = b.footprint;
            // aim at the middle of edge 0, 2 m above the ground, from 20 m out along its outward normal
            var a = fp[0]; var c = fp[1]; double L = Math.Sqrt((c.x - a.x) * (c.x - a.x) + (c.z - a.z) * (c.z - a.z));
            double sg = Geo.Area2(fp) > 0 ? 1 : -1, nx = sg * (c.z - a.z) / L, nz = -sg * (c.x - a.x) / L;
            double mx = (a.x + c.x) / 2 + b.pos.x, mz = (a.z + c.z) / 2 + b.pos.z;
            var hit = Facades.Pick(b, new Vec3d(mx + nx * 20, 2, mz + nz * 20), new Vec3d(-nx, 0, -nz));
            Assert.NotNull(hit);
            Assert.Equal((0, 0, 0), (hit!.Value.i, hit.Value.k0, hit.Value.k));
            Assert.Equal(0.5, hit.Value.t, 6);
            // from inside, looking out, nothing faces the ray
            Assert.Null(Facades.Pick(b, new Vec3d(mx - nx * 2, 2, mz - nz * 2), new Vec3d(nx, 0, nz)));
        }

        [Fact]
        public void AStyleRoundTripsOnItsOwn()
        {
            foreach (var b in Data.Value.Demo.buildings)
            {
                string json = PrototypeJson.WriteStyle(b.style);
                var unknown = new List<string>();
                var back = PrototypeJson.ReadStyle(json, unknown);
                Assert.Empty(unknown);
                Assert.Equal(json, PrototypeJson.WriteStyle(back));
            }
        }
    }
}
