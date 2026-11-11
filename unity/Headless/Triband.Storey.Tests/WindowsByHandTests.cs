using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Windows put in or taken out by hand (<see cref="WindowData"/>), as the Facade tab's Windows card places them (Place
    /// windows): bay by bay, a click on a window takes it out and a click on a bay without one puts one in, the style's or one
    /// named; a door still wins its bay. Kept with their storey and the outline's edges, as facade details are, in the layout,
    /// baked, and at every LOD. Storey's own.
    /// </summary>
    public class WindowsByHandTests
    {
        // a 12 × 9 m block of three storeys, its street door in the south wall's middle (edge 0); edge 1 is the east wall, 9 m
        static (StoreyDocument d, BuildingData b, Site site) Block()
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(12, 0), new Vec2(12, 9), new Vec2(0, 9) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            Floors.SetCount(b, 3);
            return (d, b, new Site(d.buildings));
        }

        // a click on storey k's wall i at t along it, half way up the storey
        static FacadeHit Hit(BuildingData b, int k, int i, double t) =>
            new FacadeHit { i = i, k0 = Derived.TierStart(b, k), k = k, t = t, L = Geo.EdgeLen(Derived.OutlineAt(b, k), i), y = Derived.FloorBase(b, k) + 1.5 };

        static List<Opening> WindowsOn(Site site, BuildingData b, int k, int i)
        {
            var f = Details.EdgeFrameAt(b, k, i);
            return Facade.Ops(site, b, k, i, f.e.L, f.uS, f.uE).Where(o => !o.door).ToList();
        }

        static double T(BuildingData b, int k, int i, Opening o) => (o.u0 + o.u1) / 2 / Geo.EdgeLen(Derived.OutlineAt(b, k), i);

        // an artist's window w × 1.4 m, its glass in its hole
        static OpeningKind Window(string id, double w)
        {
            var P = new[] { new P3(-w / 2, 0, 0.03), new P3(w / 2, 0, 0.03), new P3(w / 2, 1.4, 0.03), new P3(-w / 2, 1.4, 0.03) };
            var m = new OpeningKind.Mesh { P = P, N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            return new OpeningKind { id = id, name = id, door = false, w = w, h = 1.4, stretch = false, lod0 = m };
        }

        [Fact]
        public void AClickTakesAWindowOutAndAnotherPutsItBack()
        {
            var (_, b, site) = Block();
            var before = WindowsOn(site, b, 1, 1);
            Assert.True(before.Count >= 3);
            var w = before[1]; double t = T(b, 1, 1, w);
            Assert.Same(w.GetType(), Facades.WindowAt(site, b, Hit(b, 1, 1, t))!.window!.GetType());
            Assert.Null(Facades.ToggleWindow(site, b, Hit(b, 1, 1, t)));
            var after = WindowsOn(site, b, 1, 1);
            Assert.Equal(before.Count - 1, after.Count);
            Assert.DoesNotContain(after, o => Math.Abs(o.u0 - w.u0) < 1e-9);
            Assert.True(b.windows.Single().none);
            Assert.Equal(before.Count, WindowsOn(site, b, 2, 1).Count);   // the storey above keeps its own
            // the same bay clicked again: the style's window back, and nothing kept by hand
            Assert.Null(Facades.ToggleWindow(site, b, Hit(b, 1, 1, t)));
            Assert.Empty(b.windows);
            Assert.Equal(before.Count, WindowsOn(site, b, 1, 1).Count);
        }

        [Fact]
        public void OneGoesInOnABlankWallInTheMiddleOfItsBay()
        {
            var (_, b, site) = Block();
            Facades.ToggleState(b, 1, 1, EdgeState.Blank);
            Assert.Empty(WindowsOn(site, b, 1, 1));
            var spot = Facades.WindowAt(site, b, Hit(b, 1, 1, 0.4))!;
            Assert.Null(spot.window);
            Assert.Null(Facades.ToggleWindow(site, b, Hit(b, 1, 1, 0.4)));
            var put = WindowsOn(site, b, 1, 1).Single();
            Assert.Equal(spot.bS + (spot.j + 0.5) * spot.bay, (put.u0 + put.u1) / 2, 6);
            var wd = b.windows.Single(); Assert.False(wd.none); Assert.Null(wd.window);
            Assert.Empty(WindowsOn(site, b, 2, 1));   // the blank wall's other storeys have none
            // taken out again: nothing kept by hand, the wall blank as it was
            Assert.Null(Facades.ToggleWindow(site, b, Hit(b, 1, 1, 0.4)));
            Assert.Empty(b.windows); Assert.Empty(WindowsOn(site, b, 1, 1));
        }

        [Fact]
        public void APutInWindowCanBeOneNamed()
        {
            OpeningKinds.Register(Window("wb-narrow", 0.8));
            try
            {
                var (_, b, site) = Block();
                var w = WindowsOn(site, b, 1, 1)[1]; double t = T(b, 1, 1, w), mid = (w.u0 + w.u1) / 2;
                Assert.Null(w.kind);   // the built-in
                Facades.ToggleWindow(site, b, Hit(b, 1, 1, t));
                Assert.Null(Facades.ToggleWindow(site, b, Hit(b, 1, 1, t), "wb-narrow"));
                Opening There() => WindowsOn(site, b, 1, 1).Single(o => Math.Abs((o.u0 + o.u1) / 2 - mid) < 1e-6);
                Assert.Same(OpeningKinds.Get("wb-narrow"), There().kind);
                Assert.Equal("wb-narrow", b.windows.Single().window);
                // one the project doesn't have is the style's pick
                b.windows[0].window = "not-in-the-project";
                Assert.Null(There().kind);
                Assert.True(OpeningSets.PlacedWindow(WindowData.Plain, out var plain) && plain == null);
            }
            finally { OpeningKinds.Remove("wb-narrow"); }
        }

        // a click as the Windows card's Place windows makes it: the window its Window to place says, nothing picked (the first)
        // or <paramref name="picked"/>, from the style of the hit wall's tier
        static string? Click(Site site, BuildingData b, FacadeHit h, string picked = "") =>
            Facades.ToggleWindow(site, b, h, Facades.WindowToPlace(Derived.StyleAt(b, h.k0), h.k0 == 0, picked));

        // a click in the middle of each of the bays of storey k's wall i
        static List<FacadeHit> Bays(Site site, BuildingData b, int k, int i)
        {
            var s = Facades.WindowAt(site, b, Hit(b, k, i, 0.5))!;
            return Enumerable.Range(0, s.n).Select(j => Hit(b, k, i, (s.bS + (j + 0.5) * s.bay) / s.L)).ToList();
        }

        static OpeningKind? KindAt(Site site, BuildingData b, FacadeHit h) => Facades.WindowAt(site, b, h)!.window?.kind;

        [Fact]
        public void TheStylesOneWindowIsTheOnePutInOnEveryStorey()
        {
            OpeningKinds.Register(Window("wb-one", 0.8)); OpeningKinds.Register(Window("wb-shop", 1.0)); OpeningKinds.Register(Window("wb-shop2", 0.9));
            try
            {
                // one window, and the ground floor's own two, picked bay by bay; the east wall blank, so a click puts one in
                var (_, b, site) = Block();
                b.style.windowKind = "wb-one";
                b.style.groundSet = new() { new OpeningEntry { kind = "wb-shop" }, new OpeningEntry { kind = "wb-shop2" } };
                Facades.ToggleState(b, 0, 1, EdgeState.Blank);
                // on the base, the ground floor's own are windows to place too; on a setback, the style's alone; a ground floor
                // without windows lists none
                Assert.Equal(new[] { "wb-one", "wb-shop", "wb-shop2", Facades.AnyWindow }, Facades.WindowChoices(b.style, true));
                Assert.Equal(new[] { "wb-one" }, Facades.WindowChoices(b.style, false));
                b.style.ground = GroundType.Solid;
                Assert.Equal(new[] { "wb-one" }, Facades.WindowChoices(b.style, true));
                b.style.ground = GroundType.Storefront;
                // nothing picked: the style's one window, by name, in every bay of every storey, the ground floor's too, as the
                // Scene view names it. The style's pick for a ground-floor bay is one of the ground floor's own, which a click
                // put in while the style had one window.
                var one = OpeningKinds.Get("wb-one"); int put = 0;
                for (int k = 0; k < 3; k++)
                    foreach (var h in Bays(site, b, k, 1))
                    {
                        Assert.Null(Facades.WindowAt(site, b, h)!.window);
                        Assert.Null(Click(site, b, h));
                        Assert.Same(one, KindAt(site, b, h)); put++;
                    }
                Assert.Equal(put, b.windows.Count(w => w.window == "wb-one"));
                // taken out, then put in again: any of them, by weight, the ground floor's own; one of them picked by name, above too
                var ground = Bays(site, b, 0, 1);
                foreach (var h in ground) Assert.Null(Click(site, b, h));
                Assert.DoesNotContain(b.windows, w => w.k == 0);
                foreach (var h in ground)
                {
                    Assert.Null(Click(site, b, h, Facades.AnyWindow));
                    Assert.Contains(KindAt(site, b, h)?.id, new[] { "wb-shop", "wb-shop2" });
                    Assert.Null(b.windows.Last().window);
                }
                var up = Bays(site, b, 2, 1)[0];
                Assert.Null(Click(site, b, up));
                Assert.Null(Click(site, b, up, "wb-shop"));
                Assert.Same(OpeningKinds.Get("wb-shop"), KindAt(site, b, up));
            }
            finally { OpeningKinds.Remove("wb-one"); OpeningKinds.Remove("wb-shop"); OpeningKinds.Remove("wb-shop2"); }
        }

        [Fact]
        public void TheStylesOneWindowGoesInThoughItIsNeverPicked()
        {
            OpeningKinds.Register(Window("wb-rare", 0.8));
            try
            {
                // its one window weighs 0 beside none: the style lays no window, and its pick for a bay put in by hand is the
                // built-in window, none of its own weighing anything
                var (_, b, site) = Block();
                b.style.windowSet = new() { new OpeningEntry { kind = "wb-rare", weight = 0 }, new OpeningEntry { none = true, weight = 1 } };
                Assert.Empty(WindowsOn(site, b, 1, 1));
                Assert.Equal(new[] { "wb-rare" }, Facades.WindowChoices(b.style, true));
                var h = Hit(b, 1, 1, 0.4);
                Assert.Null(Click(site, b, h));
                Assert.Same(OpeningKinds.Get("wb-rare"), KindAt(site, b, h));
                Assert.Equal("wb-rare", b.windows.Single().window);
                // a style listing no window puts in the built-in one, by name
                b.style.windowSet = new() { new OpeningEntry { none = true } };
                Assert.Equal(new[] { WindowData.Plain }, Facades.WindowChoices(b.style, true));
                Assert.Equal(WindowData.Plain, Facades.WindowToPlace(b.style, true, "wb-rare"));
            }
            finally { OpeningKinds.Remove("wb-rare"); }
        }

        [Fact]
        public void TheStylesOwnWindowByNamePutsTheBayBack()
        {
            OpeningKinds.Register(Window("wb-own", 0.8)); OpeningKinds.Register(Window("wb-other", 0.9));
            try
            {
                var (_, b, site) = Block();
                b.style.windowKind = "wb-own";
                var own = OpeningKinds.Get("wb-own"); var w = WindowsOn(site, b, 1, 1)[1]; var h = Hit(b, 1, 1, T(b, 1, 1, w));
                Assert.Same(own, w.kind);
                Assert.Null(Click(site, b, h));
                Assert.True(b.windows.Single().none);
                // the second click names the style's one window, which the style has there: the bay as the style has it, nothing by hand
                Assert.Equal("wb-own", Facades.WindowToPlace(b.style, true, ""));
                Assert.Null(Click(site, b, h));
                Assert.Empty(b.windows);
                Assert.Same(own, KindAt(site, b, h));
                // another, named, is kept by hand; taken out and the style's own named, the bay is back as the style has it
                Assert.Null(Click(site, b, h));
                Assert.Null(Facades.ToggleWindow(site, b, h, "wb-other"));
                Assert.Equal("wb-other", b.windows.Single().window);
                Assert.Null(Click(site, b, h));
                Assert.Null(Facades.ToggleWindow(site, b, h, "wb-own"));
                Assert.Empty(b.windows);
                // one the project doesn't have would be the style's pick: the style's own back
                Assert.Null(Click(site, b, h));
                Assert.Null(Facades.ToggleWindow(site, b, h, "not-in-the-project"));
                Assert.Empty(b.windows);
            }
            finally { OpeningKinds.Remove("wb-own"); OpeningKinds.Remove("wb-other"); }
        }

        [Fact]
        public void ADoorKeepsItsBay()
        {
            var (_, b, site) = Block();
            var spot = Facades.WindowAt(site, b, Hit(b, 0, 0, 0.5))!;
            Assert.Null(spot.window);
            Assert.NotNull(Facades.ToggleWindow(site, b, Hit(b, 0, 0, 0.5)));
            Assert.Empty(b.windows);
        }

        [Fact]
        public void TheyGoWithTheirStoreyAndWall()
        {
            var (_, b, site) = Block();
            var w = WindowsOn(site, b, 1, 1)[1]; double t = T(b, 1, 1, w), mid = (w.u0 + w.u1) / 2;
            Facades.ToggleWindow(site, b, Hit(b, 1, 1, t));
            Floors.InsertAbove(b, 0);
            Assert.Equal(2, b.windows.Single().k);
            Assert.DoesNotContain(WindowsOn(site, b, 2, 1), o => Math.Abs((o.u0 + o.u1) / 2 - mid) < 1e-6);
            Floors.Delete(b, 1);
            Assert.Equal(1, b.windows.Single().k);
            // a corner put in the wall: the window by hand keeps its place on it
            Outlines.InsertVertex(b, 1);
            var wd = b.windows.Single(); var fp = Derived.OutlineAt(b, 1);
            var at = new Vec2(fp[wd.edge].x + (fp[(wd.edge + 1) % fp.Count].x - fp[wd.edge].x) * wd.t, fp[wd.edge].z + (fp[(wd.edge + 1) % fp.Count].z - fp[wd.edge].z) * wd.t);
            Assert.Equal(12, at.x, 6); Assert.Equal(mid, at.z, 6);
            Floors.Delete(b, 1);
            Assert.Empty(b.windows);
        }

        [Fact]
        public void TheLayoutKeepsThem()
        {
            var (d, b, site) = Block();
            Assert.DoesNotMatch("\"windows\"\\s*:\\s*\\[", PrototypeJson.Write(b));   // none, no key (the style's "windows" is its window type)
            var w = WindowsOn(site, b, 1, 1)[1];
            Facades.ToggleWindow(site, b, Hit(b, 1, 1, T(b, 1, 1, w)));
            Facades.ToggleState(b, 2, 1, EdgeState.Blank);
            Facades.ToggleWindow(site, b, Hit(b, 2, 1, 0.4), WindowData.Plain);
            string Key(WindowData x) => $"{x.k}/{x.edge}/{x.t:R}/{x.none}/{x.window}";
            var want = b.windows.Select(Key).ToList();
            Assert.Equal(2, want.Count);
            Assert.Equal(want, PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).windows.Select(Key));
            Assert.Equal(want, BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].windows.Select(Key));
        }

        [Fact]
        public void EveryLodShowsThemAsLaidOut()
        {
            // the east wall's middle window on storey 1 taken out; the west wall blank (the whole tier's), one put in on storey 2
            var (_, b, site) = Block();
            var w = WindowsOn(site, b, 1, 1)[1]; var keep = WindowsOn(site, b, 1, 1)[0];
            Facades.ToggleWindow(site, b, Hit(b, 1, 1, T(b, 1, 1, w)));
            Facades.ToggleState(b, 2, 3, EdgeState.Blank);
            Facades.ToggleWindow(site, b, Hit(b, 2, 3, 0.4));
            var put = WindowsOn(site, b, 2, 3).Single();
            // LOD0 and LOD1 lay out the openings as Facade.Ops does; LOD2 paints the bays
            var r0 = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
            Assert.True(r0.Overlaps.Count == 0, string.Join("\n", r0.Overlaps.Take(10)));
            var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true));
            Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(10)));
            var m = Lod2.Build(site, b);
            double Painted(int k, int i, Opening o)
            {
                var f = Details.EdgeFrameAt(b, k, i);
                return TerraceRoofWindowTests.BayAt(m, f.e.F.At((o.u0 + o.u1) / 2, Derived.FloorBase(b, k) + (o.y0 + o.y1) / 2, Dim.T_EXT), new P3(f.e.w.x, 0, f.e.w.z));
            }
            Assert.Equal(0, Painted(1, 1, w));                                // taken out
            Assert.True(Painted(1, 1, keep) > 0);                             // its neighbour, as the style has it
            Assert.True(Painted(2, 3, put) > 0);                              // put in on the blank wall
            Assert.Equal(0, Painted(1, 3, put));                              // the blank wall's storey below, without one
            Assert.True(Painted(0, 1, WindowsOn(site, b, 0, 1)[0]) > 0);
        }
    }
}
