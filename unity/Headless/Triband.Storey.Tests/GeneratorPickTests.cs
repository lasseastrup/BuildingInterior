using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Windows and street doors picked as the old BuildingGenerator picks them (<see cref="WindowPick.Generator"/>): by the
    /// edge, the storey and the bay alone, seeded as it seeds its tiles, so a building it made gets the same window in every
    /// bay and the same door. Storey's own.
    /// </summary>
    public class GeneratorPickTests
    {
        // the old generator's BuildingTheme.PickFromWeightedList as it is: System.Random's first number times the total, the
        // weights taken off in turn, in float
        static int Reference(IReadOnlyList<OpeningEntry> list, int seed)
        {
            float total = 0;
            foreach (var e in list) total += Math.Max(0f, (float)e.weight);
            bool uniform = total <= 0f;
            if (uniform) total = list.Count;
            float r = (float)new Random(seed).NextDouble() * total;
            for (int i = 0; i < list.Count; i++)
            {
                r -= uniform ? 1f : Math.Max(0f, (float)list[i].weight);
                if (r <= 0f) return i;
            }
            return list.Count - 1;
        }

        static OpeningKind Kind(string id, double w, double h, bool door = false)
        {
            var P = new[] { new P3(-w / 2, 0, 0.02), new P3(w / 2, 0, 0.02), new P3(w / 2, h, 0.02), new P3(-w / 2, h, 0.02) };
            var m = new OpeningKind.Mesh { P = P, N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = door ? ColorSlot.Door : ColorSlot.Glass, leaf = door, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            return new OpeningKind { id = id, name = id, door = door, w = w, h = h, stretch = false, lod0 = m };
        }

        static List<OpeningEntry> Set(params (string? kind, bool none, double weight)[] es) =>
            es.Select(e => new OpeningEntry { kind = e.kind, none = e.none, weight = e.weight }).ToList();

        [Fact]
        public void ItsNumberIsSystemRandomsFirst()
        {
            var seeds = new List<int> { 0, 1, -1, 1009, 100003, 123456789, -987654321, int.MaxValue, int.MinValue, int.MinValue + 1 };
            for (int s = -20000; s < 1200000; s += 97) seeds.Add(s);
            foreach (int s in seeds) Assert.Equal(new Random(s).NextDouble(), OpeningSets.SystemRandomFirst(s));
        }

        [Fact]
        public void ItPicksWhatTheOldGeneratorPicks()
        {
            var sets = new[]
            {
                Set(("a", false, 0.5), (null, true, 0.6)),                    // the old SimpleRed ground floor: a window or none
                Set(("a", false, 0.8), ("b", false, 0.1), ("c", false, 0.6)), // StoreRed's floors above
                Set(("a", false, 0), ("b", false, 0)),                        // no weights: each as often
                Set(("a", false, 1)),
                Set(("a", false, 0.3), (null, true, 0), ("c", false, 0.7)),
            };
            foreach (var set in sets)
                for (int e = 0; e < 8; e++)
                    for (int f = 0; f < 12; f++)
                        for (int s = 0; s < 24; s++)
                        {
                            int seed = e * 100003 + f * 1009 + s;
                            Assert.Equal(Reference(set, seed), OpeningSets.PickAsGenerator(set, seed));
                        }
        }

        static (BuildingData b, Site site) Block(FacadeStyle st, bool interior = false)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0); b.id = "pick";
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            Floors.SetCount(b, 3);
            b.entrances.Clear(); b.details.Clear(); b.shafts.Clear();
            b.interior = interior; b.style = st;
            return (b, new Site(d.buildings));
        }

        [Fact]
        public void EachBayGetsTheWindowTheOldGeneratorGaveIt()
        {
            OpeningKinds.Register(Kind("pick-w", 1.0, 0.83));
            var st = Styles.Preset("brick").Clone();
            st.bayFit = BayFit.Fixed; st.bay = 1.53; st.winCentre = true; st.windowPick = WindowPick.Generator; st.ground = GroundType.Match;
            st.windowSet = Set(("pick-w", false, 1)); st.groundSet = Set(("pick-w", false, 0.5), (null, true, 0.6));
            var (b, site) = Block(st);
            int checkedBays = 0;
            for (int i = 0; i < 4; i++)
            {
                var f = Details.EdgeFrameAt(b, 0, i);
                var ops = Facade.Ops(site, b, 0, i, f.e.L, f.uS, f.uE);
                var (oS, oE) = Facade.OuterFace(b, 0, i);
                var (n, bay, bS) = Facade.Bays(st, oS, oE, oS, oE);
                Assert.True(n >= 6);
                for (int j = 0; j < n; j++)
                {
                    double c = bS + (j + 0.5) * bay;
                    bool has = ops.Any(o => !o.door && Math.Abs((o.u0 + o.u1) / 2 - c) < 0.2);
                    bool want = Reference(st.groundSet, i * 100003 + 0 * 1009 + j) == 0;
                    Assert.True(want == has, $"edge {i} bay {j}: the old generator {(want ? "puts a window" : "leaves it plain")}");
                    checkedBays++;
                }
            }
            Assert.True(checkedBays >= 30);
            OpeningKinds.Remove("pick-w");
        }

        [Fact]
        public void AStreetDoorIsPickedByItsEdgeAlone()
        {
            OpeningKinds.Register(Kind("pick-d1", 1.2, 2.1, door: true)); OpeningKinds.Register(Kind("pick-d2", 1.4, 2.1, door: true));
            var st = Styles.Preset("brick").Clone();
            st.windowPick = WindowPick.Generator;
            st.doorSet = Set(("pick-d1", false, 1), ("pick-d2", false, 1));
            for (int e = 0; e < 12; e++)
            {
                var want = st.doorSet[Reference(st.doorSet, e * 100003)].kind;
                // whatever the building and the storey
                Assert.Equal(want, OpeningSets.DoorAt(st, "one", 0, e, out _)?.id);
                Assert.Equal(want, OpeningSets.DoorAt(st, "another", 2, e, out _)?.id);
            }
            OpeningKinds.Remove("pick-d1"); OpeningKinds.Remove("pick-d2");
        }

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var st = Styles.Preset("brick").Clone(); st.windowPick = WindowPick.Generator;
            Assert.Contains("\"windowPick\":\"generator\"", PrototypeJson.WriteStyle(st));
            Assert.Equal(WindowPick.Generator, PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(st)).windowPick);
            var (b, _) = Block(st);
            var d = new StoreyDocument(); d.buildings.Add(b);
            Assert.Equal(WindowPick.Generator, BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style.windowPick);
        }
    }
}
