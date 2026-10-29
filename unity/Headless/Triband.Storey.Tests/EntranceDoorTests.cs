using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// An entrance naming its door (<see cref="EntranceData.door"/>), as the Facade tab's Doors card places them: that door,
    /// whatever the style would pick for its wall, with its entry's own recess; the built-in one by
    /// <see cref="EntranceData.Plain"/>; the style's pick where it names none, or one the project doesn't have. Storey's own.
    /// </summary>
    public class EntranceDoorTests
    {
        // a leaf w × 2 m in front of the door's pivot
        static OpeningKind Door(string id, double w)
        {
            var P = new[] { new P3(-w / 2, 0, 0.03), new P3(w / 2, 0, 0.03), new P3(w / 2, 2, 0.03), new P3(-w / 2, 2, 0.03) };
            var m = new OpeningKind.Mesh { P = P, N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Door, leaf = true, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            return new OpeningKind { id = id, name = id, door = true, w = w, h = 2, stretch = false, lod0 = m };
        }

        static (BuildingData b, Site site) Block(FacadeStyle st, string? door)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            Floors.SetCount(b, 2);
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5, door = door }); b.details.Clear(); b.shafts.Clear();
            b.interior = false; b.style = st;
            return (b, new Site(d.buildings));
        }

        static Opening DoorOf((BuildingData b, Site site) x)
        {
            var f = Details.EdgeFrameAt(x.b, 0, 0);
            return Facade.Ops(x.site, x.b, 0, 0, f.e.L, f.uS, f.uE).Single(o => o.door);
        }

        // two doors of the project's and the built-in one, the wide one so heavy that the style picks it on every wall
        static FacadeStyle Style()
        {
            var st = Styles.Preset("brick").Clone(); st.ground = GroundType.Match;
            st.doorSet = new List<OpeningEntry>
            {
                new OpeningEntry { kind = "ed-wide", weight = 1000 },
                new OpeningEntry { kind = "ed-narrow", weight = 0.001 },
                new OpeningEntry { weight = 0.001, recess = 0.1 },
            };
            return st;
        }

        [Fact]
        public void AnEntranceNamingItsDoorHasThatOneWhateverTheStylePicks()
        {
            OpeningKinds.Register(Door("ed-wide", 3.0)); OpeningKinds.Register(Door("ed-narrow", 1.2));
            try
            {
                var st = Style();
                Assert.Same(OpeningKinds.Get("ed-wide"), DoorOf(Block(st, null)).kind);                // the style's pick
                var narrow = DoorOf(Block(st, "ed-narrow"));
                Assert.Same(OpeningKinds.Get("ed-narrow"), narrow.kind);
                Assert.Equal(1.2, narrow.u1 - narrow.u0, 6);                                              // its own width, on a shell storey
                var plain = DoorOf(Block(st, EntranceData.Plain));
                Assert.Null(plain.kind);                                                                  // the built-in one
                Assert.Equal(0.1, plain.recess);                                                          // with its entry's own recess
                Assert.Same(OpeningKinds.Get("ed-wide"), DoorOf(Block(st, "not-in-the-project")).kind);   // the style's pick again
                // a door named but not in the style's set: that door all the same, at the style's depth
                var lone = Styles.Preset("brick").Clone(); lone.ground = GroundType.Match;
                Assert.Same(OpeningKinds.Get("ed-narrow"), DoorOf(Block(lone, "ed-narrow")).kind);
            }
            finally { OpeningKinds.Remove("ed-wide"); OpeningKinds.Remove("ed-narrow"); }
        }

        [Fact]
        public void PlacingADoorNamesItAndTakingItOutTakesItsDoorWithIt()
        {
            var (b, site) = Block(Style(), null); b.entrances.Clear();
            var hit = new FacadeHit { i = 0, k0 = 0, k = 0, L = 14, t = 0.3, y = 1 };
            Assert.Null(Facades.ToggleEntrance(site, b, hit, "ed-narrow"));
            Assert.Equal(("ed-narrow", 0, 0), (b.entrances.Single().door, b.entrances[0].edge, b.entrances[0].k));
            Assert.Null(Facades.ToggleEntrance(site, b, hit, EntranceData.Plain));   // a click on it takes it out
            Assert.Empty(b.entrances);
            Assert.Null(Facades.ToggleEntrance(site, b, hit));                         // the style's pick
            Assert.Null(b.entrances.Single().door);
        }

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var (b, _) = Block(Style(), "ed-narrow");
            static string Entrances(string json) => Regex.Match(json, "\"entrances\":\\[[^\\]]*\\]").Value;
            Assert.Contains("\"door\":\"ed-narrow\"", Entrances(PrototypeJson.Write(b)));
            Assert.Equal("ed-narrow", PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).entrances[0].door);
            var d = new StoreyDocument(); d.buildings.Add(b);
            Assert.Equal("ed-narrow", BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].entrances[0].door);
            // naming none, nothing is written, and it reads and bakes back as none
            b.entrances[0].door = null;
            Assert.DoesNotContain("door", Entrances(PrototypeJson.Write(b)));
            Assert.Null(PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).entrances[0].door);
            Assert.Null(BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].entrances[0].door);
        }
    }
}
