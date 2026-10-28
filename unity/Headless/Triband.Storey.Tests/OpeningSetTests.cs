using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Several windows and street doors a style, picked opening by opening (<see cref="OpeningSets"/>, docs/OPENINGS.md §7.1):
    /// as often as their weights say, the same on every build, different from building to building; none leaves the wall
    /// whole; the ground floor's own set; a door per wall; and the editing that keeps a style of one window as it was. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class OpeningSetTests
    {
        /// <summary>A pane in the hole and a sill under it, <paramref name="w"/> × <paramref name="h"/>; Keep size when <paramref name="keep"/>.</summary>
        static OpeningKind Kind(string id, double w = 1, double h = 1.4, bool keep = false, bool door = false, double reach = 0, bool lod1 = false)
        {
            var P = new List<P3>(); var N = new List<P3>(); var pane = new List<int>(); var sill = new List<int>();
            void Q(double x0, double y0, double x1, double y1, double z, List<int> into)
            {
                int s = P.Count;
                P.AddRange(new[] { new P3(x0, y0, z), new P3(x1, y0, z), new P3(x1, y1, z), new P3(x0, y1, z) }); N.AddRange(Enumerable.Repeat(new P3(0, 0, 1), 4));
                into.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Q(-w / 2, 0, w / 2, h, -0.05, pane);
            Q(-w / 2 - reach, -0.08, w / 2 + reach, -0.01, 0.05, sill);
            var m = new OpeningKind.Mesh { P = P.ToArray(), N = N.ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = door ? ColorSlot.Door : ColorSlot.Glass, leaf = door, tris = pane.ToArray() });
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = sill.ToArray() });
            return new OpeningKind { id = id, name = id, door = door, w = w, h = h, stretch = !keep, lod0 = m, lod1 = lod1 ? m : null };
        }

        static (BuildingData b, Site s, StoreyDocument d) Block(Action<FacadeStyle>? f = null, int floors = 5, string id = "", double x = 0, double len = 30)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(x, 0);
            if (id.Length > 0) b.id = id;
            b.footprint = new() { new Vec2(0, 0), new Vec2(len, 0), new Vec2(len, 12), new Vec2(0, 12) };
            Floors.SetCount(b, floors);
            b.entrances.Clear(); b.details.Clear(); b.shafts.Clear();
            b.interior = false; b.style = Styles.Preset("brick"); b.style.ground = GroundType.Match;
            f?.Invoke(b.style);
            return (b, new Site(d.buildings), d);
        }

        /// <summary>Every opening of the building, storey by storey and wall by wall.</summary>
        static List<(int k, int i, Opening o)> All(BuildingData b, Site s)
        {
            var all = new List<(int, int, Opening)>();
            for (int k = 0; k < b.floors.Count; k++)
                for (int i = 0; i < Derived.OutlineAt(b, k).Count; i++)
                {
                    var f = Details.EdgeFrameAt(b, k, i);
                    foreach (var o in Facade.Ops(s, b, k, i, f.e.L, f.uS, f.uE)) all.Add((k, i, o));
                }
            return all;
        }

        static void With(IEnumerable<OpeningKind> ks, Action body)
        {
            var list = ks.ToList();
            foreach (var k in list) OpeningKinds.Register(k);
            try { body(); } finally { foreach (var k in list) OpeningKinds.Remove(k.id); }
        }

        static List<OpeningEntry> Set(params (string? kind, bool none, double weight)[] es) =>
            es.Select(e => new OpeningEntry { kind = e.kind, none = e.none, weight = e.weight }).ToList();

        [Fact]
        public void ASetOfOneWindowBuildsAsTheStylesOneWindowDoes()
        {
            With(new[] { Kind("t-set-one") }, () =>
            {
                var (b0, s0, _) = Block(st => st.windowKind = "t-set-one");
                var (b1, s1, _) = Block(st => st.windowSet = Set(("t-set-one", false, 3)));
                var m0 = Lod0.Build(s0, b0).Op; var m1 = Lod0.Build(s1, b1).Op;
                Assert.Equal(m0.Verts, m1.Verts);
                for (int i = 0; i < m0.Verts; i++) Assert.Equal(m0.P[i], m1.P[i]);
                Assert.Equal(Lod1.Build(s0, b0).Verts, Lod1.Build(s1, b1).Verts);
            });
        }

        [Fact]
        public void EachWindowIsPickedAsOftenAsItsWeightSays()
        {
            With(new[] { Kind("t-set-a"), Kind("t-set-b") }, () =>
            {
                int a = 0, n = 0, none = 0, slots = 0;
                for (int t = 0; t < 30; t++)
                {
                    var (b, s, _) = Block(st => st.windowSet = Set(("t-set-a", false, 3), ("t-set-b", false, 1)), id: "w" + t);
                    foreach (var (_, _, o) in All(b, s)) { n++; if (o.kind?.id == "t-set-a") a++; else Assert.Equal("t-set-b", o.kind?.id); }
                    // and none half the time: half the windows are left out
                    var (bn, sn, _) = Block(st => st.windowSet = Set((null, false, 1), (null, true, 1)), id: "w" + t);
                    var (bf, sf, _) = Block(id: "w" + t);
                    none += All(bn, sn).Count; slots += All(bf, sf).Count;
                }
                Assert.InRange(a / (double)n, 0.70, 0.80);
                Assert.InRange(none / (double)slots, 0.45, 0.55);
            });
        }

        [Fact]
        public void AWeightOfNoughtLeavesAWindowOutUnlessEveryWeightIsNought()
        {
            var off = Set(("a", false, 2), ("b", false, 0));
            var even = Set(("a", false, 0), ("b", false, 0));
            int picksB = 0, evenB = 0;
            for (uint h = 0; h < 4000; h++)
            {
                if (OpeningSets.Pick(off, OpeningSets.At(h * 2654435761u, 3)) == 1) picksB++;
                if (OpeningSets.Pick(even, OpeningSets.At(h * 2654435761u, 3)) == 1) evenB++;
            }
            Assert.Equal(0, picksB);
            Assert.InRange(evenB, 1800, 2200);
        }

        [Fact]
        public void APickIsTheSameOnEveryBuildAndDiffersFromBuildingToBuilding()
        {
            With(new[] { Kind("t-set-c"), Kind("t-set-d") }, () =>
            {
                string Picks(string id, double x)
                {
                    var (b, s, _) = Block(st => st.windowSet = Set(("t-set-c", false, 1), ("t-set-d", false, 1), (null, true, 1)), id: id, x: x);
                    return string.Join(",", All(b, s).Select(p => $"{p.k}/{p.i}/{p.o.u0:0.00}/{p.o.kind?.id}"));
                }
                Assert.Equal(Picks("same", 0), Picks("same", 0));
                Assert.Equal(Picks("same", 0), Picks("same", 40));    // moved, it keeps its windows
                Assert.NotEqual(Picks("same", 0), Picks("other", 0));   // the same footprint, a different building
            });
        }

        [Fact]
        public void NoneLeavesTheWallWholeAtEveryLod()
        {
            var (b, s, _) = Block(st => st.windowSet = Set((null, true, 1)));
            Assert.Empty(All(b, s));
            var (bf, sf, _) = Block();
            Assert.True(Lod1.Build(s, b).Verts < Lod1.Build(sf, bf).Verts);
            // LOD2 paints none either: the style has no window to stand for its set
            Assert.Null(Facade.WindowSpec(b.style.windows, 3, b.style));
            Assert.All(Lod2.Build(s, b).Rows, r => Assert.True(r.spec[3] <= r.spec[2] + 0.4));
        }

        [Fact]
        public void LodTwoPaintsTheHeaviestWindowOfASet()
        {
            With(new[] { Kind("t-set-big", 1.6, 1.8, keep: true), Kind("t-set-small", 0.8, 1.0, keep: true) }, () =>
            {
                var (b, s, _) = Block(st => st.windowSet = Set(("t-set-small", false, 1), ("t-set-big", false, 2), (null, true, 5)));
                Assert.True(OpeningSets.Representative(b.style, false, out var k));
                Assert.Equal("t-set-big", k!.id);
                var spec = Lod2.SpecOf(Facade.WindowSpec(b.style.windows, 3, b.style));
                Assert.Equal(1.6, spec[1], 6);
                // and every opening the geometry LODs lay out is in one of LOD2's bays
                var ops = All(b, s).Where(p => p.k > 0).ToList();
                Assert.Contains(ops, p => p.o.kind?.id == "t-set-small");
                Assert.Contains(ops, p => p.o.kind?.id == "t-set-big");
            });
        }

        [Fact]
        public void AKeepSizeWindowInASetMakesItsOwnHoleAndTheOthersKeepTheirs()
        {
            With(new[] { Kind("t-set-keep", 0.7, 0.9, keep: true) }, () =>
            {
                var (b, s, _) = Block(st => st.windowSet = Set(("t-set-keep", false, 1), (null, false, 1)));
                var ops = All(b, s).Where(p => p.k > 0).Select(p => p.o).ToList();
                var plain = Facade.WindowSpec(b.style.windows, Derived.FloorH(b, 1), b.style, (OpeningKind?)null)!;
                Assert.All(ops.Where(o => o.kind != null), o => { Assert.Equal(0.7, o.u1 - o.u0, 6); Assert.Equal(0.9, o.y1 - o.y0, 6); });
                Assert.All(ops.Where(o => o.kind == null), o => { Assert.Equal(plain.w, o.u1 - o.u0, 6); Assert.Equal(plain.y1, o.y1, 6); });
            });
        }

        [Fact]
        public void TheBaysMakeRoomForTheWidestDressingOfASet()
        {
            With(new[] { Kind("t-set-shutters", reach: 0.5) }, () =>
            {
                var (b, s, _) = Block(st => st.windowSet = Set((null, false, 1), ("t-set-shutters", false, 1)));
                var (l, r) = OpeningSets.Reach(b.style, false);
                Assert.Equal((0.5, 0.5), (l, r));
                // whichever is picked, no window's dressing comes within the span's 14 cm of a corner
                foreach (var (k, i, o) in All(b, s).Where(p => p.k > 0))
                {
                    var f = Details.EdgeFrameAt(b, k, i);
                    Assert.True(o.Left >= f.uS - 1e-6 && o.Right <= f.uE + 1e-6);
                }
            });
        }

        [Fact]
        public void TheGroundFloorTakesItsOwnSet()
        {
            With(new[] { Kind("t-set-shop"), Kind("t-set-flat") }, () =>
            {
                var (b, s, _) = Block(st => { st.windowKind = "t-set-flat"; st.groundSet = Set(("t-set-shop", false, 1)); });
                var all = All(b, s);
                Assert.All(all.Where(p => p.k == 0), p => Assert.Equal("t-set-shop", p.o.kind?.id));
                Assert.All(all.Where(p => p.k > 0), p => Assert.Equal("t-set-flat", p.o.kind?.id));
                // a solid ground floor has none, whatever its set
                b.style.ground = GroundType.Solid;
                Assert.DoesNotContain(All(b, s), p => p.k == 0);
                // and storefronts take it, fitted to theirs
                b.style.ground = GroundType.Storefront;
                Assert.All(All(b, s).Where(p => p.k == 0), p => Assert.True(p.o.full && p.o.kind?.id == "t-set-shop"));
            });
        }

        [Fact]
        public void EveryDoorOnAWallIsTheSameDoor()
        {
            With(new[] { Kind("t-set-door1", door: true), Kind("t-set-door2", door: true) }, () =>
            {
                var seen = new HashSet<string?>();
                for (int t = 0; t < 12; t++)
                {
                    var (b, s, _) = Block(st => st.doorSet = Set(("t-set-door1", false, 1), ("t-set-door2", false, 1)), id: "d" + t);
                    b.entrances.Add(new EntranceData { edge = 0, t = 0.2 }); b.entrances.Add(new EntranceData { edge = 0, t = 0.8 }); b.entrances.Add(new EntranceData { edge = 2, t = 0.5 });
                    var doors = All(b, s).Where(p => p.o.door).ToList();
                    Assert.Equal(3, doors.Count);
                    Assert.Single(doors.Where(p => p.i == 0).Select(p => p.o.kind!.id).Distinct());
                    foreach (var p in doors) seen.Add(p.o.kind?.id);
                }
                Assert.Equal(new HashSet<string?> { "t-set-door1", "t-set-door2" }, seen);
            });
        }

        [Theory]
        [InlineData(WindowPick.Column)]
        [InlineData(WindowPick.Storey)]
        [InlineData(WindowPick.Wall)]
        public void APickByColumnStoreyOrWallKeepsThemAlike(WindowPick pick)
        {
            With(new[] { Kind("t-set-e"), Kind("t-set-f") }, () =>
            {
                var (b, s, _) = Block(st => { st.windowSet = Set(("t-set-e", false, 1), ("t-set-f", false, 1)); st.windowPick = pick; }, floors: 6);
                var ops = All(b, s).Where(p => p.k > 0).ToList();
                var groups = pick switch
                {
                    WindowPick.Column => ops.GroupBy(p => $"{p.i}/{p.o.u0:0.000}"),
                    WindowPick.Storey => ops.GroupBy(p => $"{p.i}/{p.k}"),
                    _ => ops.GroupBy(p => $"{p.i}"),
                };
                Assert.All(groups, g => Assert.Single(g.Select(p => p.o.kind!.id).Distinct()));
                Assert.Equal(2, ops.Select(p => p.o.kind!.id).Distinct().Count());
            });
        }

        [Fact]
        public void LodOneCutsTheWallOnlyForTheWindowsThatNeedIt()
        {
            // an artist's LOD1 that reaches into the wall cuts it; the generator's lies on it: in a set, each its own way
            With(new[] { Kind("t-set-cut", lod1: true) }, () =>
            {
                var (b, s, _) = Block(st => st.windowSet = Set(("t-set-cut", false, 1), (null, false, 1)));
                var (ba, sa, _) = Block(st => st.windowKind = "t-set-cut");
                var (bp, sp, _) = Block();
                int mixed = Lod1.Build(s, b).Tris, cutAll = Lod1.Build(sa, ba).Tris, none = Lod1.Build(sp, bp).Tris;
                Assert.True(mixed > none && mixed < cutAll, $"mixed {mixed}, all the artist's {cutAll}, the generator's {none}");
            });
        }

        [Fact]
        public void ASetAndItsPickComeBackFromJson()
        {
            var st = Styles.Preset("brick").Clone();
            st.windowSet = Set(("abc", false, 2), (null, true, 0.5), (null, false, 1)); st.groundSet = Set((null, true, 1)); st.doorSet = Set(("def", false, 1), (null, false, 3));
            st.windowPick = WindowPick.Column;
            var json = PrototypeJson.WriteStyle(st);
            Assert.Contains("\"windowSet\":[{\"kind\":\"abc\",\"weight\":2},{\"none\":true,\"weight\":0.5},{}]", json);
            var back = PrototypeJson.ReadStyle(json);
            Assert.Equal(json, PrototypeJson.WriteStyle(back));
            Assert.Equal(WindowPick.Column, back.windowPick);
            // a style without a set writes as it did
            Assert.DoesNotContain("Set", PrototypeJson.WriteStyle(Styles.Preset("brick")));
            // and a copy's sets are its own
            var c = st.Clone(); c.windowSet![0].weight = 9;
            Assert.Equal(2, st.windowSet[0].weight);
        }

        [Fact]
        public void AStyleOfOneWindowStaysOneUntilASecondIsAdded()
        {
            var st = Styles.Preset("brick").Clone(); st.windowKind = "abc";
            WindowSets.Add(st, SetOf.Windows, "def");
            Assert.Null(st.windowKind);
            Assert.Equal(new[] { "abc", "def" }, st.windowSet!.Select(e => e.kind));
            WindowSets.Remove(st, SetOf.Windows, 0);
            Assert.Null(st.windowSet); Assert.Equal("def", st.windowKind);
            // none on its own stays a set: no windows
            WindowSets.Set(st, SetOf.Windows, 0, null, none: true);
            Assert.True(st.windowSet is { Count: 1 } && st.windowSet[0].none);
            // a door is never none, and a door set of one is the style's one door
            WindowSets.Add(st, SetOf.Doors, null, none: true);
            Assert.False(st.doorSet![1].none);
            WindowSets.Remove(st, SetOf.Doors, 1);
            Assert.Null(st.doorSet);
            // the ground floor's set starts as the floors above's, and the last entry removed follows them again
            var g = WindowSets.Own(st, SetOf.Ground);
            Assert.True(g.Count == 1 && g[0].none && !ReferenceEquals(g[0], st.windowSet[0]));
            WindowSets.Remove(st, SetOf.Ground, 0);
            Assert.Null(st.groundSet);
        }

        [Fact]
        public void APickMakesNoGarbage()
        {
            var set = Set(("a", false, 2), (null, true, 1), (null, false, 1));
            var st = new FacadeStyle { windowSet = set };
            OpeningSets.Window(st, false, OpeningSets.WindowSeed("building-17", WindowPick.Window, 3, 0, 2, 0), out _);
            long a = GC.GetAllocatedBytesForCurrentThread();
            int n = 0;
            for (int j = 0; j < 10000; j++) if (OpeningSets.Window(st, false, OpeningSets.WindowSeed("building-17", WindowPick.Window, 3, 0, 2, j), out _)) n++;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - a);
            Assert.InRange(n, 7000, 8000);
        }
    }
}
