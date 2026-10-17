using System;
using System.Linq;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The LOD manager (<see cref="LodManager"/>, docs/CITY.md §2): the prototype's selection, fades, build queue and caches.</summary>
    public class LodManagerTests
    {
        const double Pxm = 1000;   // pixels per metre at 1 m

        /// <summary>A 10 m cube with its near face at x = <paramref name="at"/>, the camera at the origin, 1 m up.</summary>
        static LodManager.Entry Box(LodManager m, int idx, double at, bool built = true)
        {
            var e = m.Set(idx, at, -5, at + 10, 5, 10);
            if (built) { m.SetBuilt(idx, 0, true); m.SetBuilt(idx, 1, true); }
            return e;
        }

        static LodManager.Frame Step(LodManager m, double dt = 1, params int[] forced) => m.Update(0, 1, 0, Pxm, dt, forced);

        [Theory]
        [InlineData(20, 0)]      // 50 px/m: LOD0 (16 × 1.12)
        [InlineData(100, 1)]     // 10 px/m: LOD1
        [InlineData(1000, 2)]    // 1 px/m: LOD2
        [InlineData(5000, 3)]    // 0.2 px/m: not drawn
        public void ItPicksByPixelsPerMetre(double at, int lod)
        {
            var m = new LodManager(); var e = Box(m, 0, at);
            Step(m);
            Assert.Equal(lod, e.Shown);
        }

        [Fact]
        public void HysteresisHoldsALodNearItsThreshold()
        {
            var m = new LodManager(); var e = Box(m, 0, 50);   // 20 px/m: LOD0 from LOD3 needs 17.92
            Step(m); Assert.Equal(0, e.Shown);
            m.Set(0, 64, -5, 74, 5, 10); m.SetBuilt(0, 0, true); m.SetBuilt(0, 1, true);   // 15.6 px/m: above 16 × 0.88 = 14.08
            Step(m); Assert.Equal(0, e.Shown);
            m.Set(0, 75, -5, 85, 5, 10); m.SetBuilt(0, 0, true); m.SetBuilt(0, 1, true);   // 13.3 px/m: below
            Step(m); Assert.Equal(1, e.Shown);
            m.Set(0, 64, -5, 74, 5, 10); m.SetBuilt(0, 0, true); m.SetBuilt(0, 1, true);   // back to 15.6: not enough to come back (17.92)
            Step(m); Assert.Equal(1, e.Shown);
        }

        [Fact]
        public void AMissingLodIsAskedForAndTheBestOneShown()
        {
            var m = new LodManager();
            var near = Box(m, 0, 10, built: false); var nearer = Box(m, 1, 5, built: false); m.SetBuilt(1, 1, true);
            var f = Step(m);
            Assert.Equal(2, near.Shown);     // nothing finer built yet
            Assert.Equal(1, nearer.Shown);   // its LOD1 meanwhile
            Assert.Equal(new[] { (1, 0), (0, 0) }, f.Build.Select(b => (b.idx, b.lod)).ToArray());   // most pixels first
            m.SetBuilt(0, 0, true);
            Step(m);
            Assert.Equal(0, near.Shown);
        }

        [Fact]
        public void AViewSeesWhatIsInFrontOfIt()
        {
            var v = new LodView(); v.Set(0, 1, 0, 1, 0, 0, 0, 1, 0, 60, 1, 0.1, 1500);   // looking along +x
            Assert.True(v.Sees(50, 1, 0, 5));
            Assert.False(v.Sees(-50, 1, 0, 5));     // behind
            Assert.False(v.Sees(50, 1, 60, 5));     // off to the side: 50 m ahead the view is ±29 m wide
            Assert.True(v.Sees(50, 1, 33, 5));      // just outside, within the margin (5 x 1.2 + 6)
            Assert.False(v.Sees(2000, 1, 0, 5));    // past the far distance
        }

        [Fact]
        public void DetailInViewIsBuiltFirstAndNoLod0OutOfIt()
        {
            // docs/PERFORMANCE.md §9.2: two buildings near enough for LOD0, one ahead and one behind the camera
            var m = new LodManager();
            var ahead = Box(m, 0, 30, built: false); var behind = m.Set(1, -40, -5, -30, 5, 10);   // 30 m off either way (a building nearer behind is within the view's margin)
            var v = new LodView(); v.Set(0, 1, 0, 1, 0, 0, 0, 1, 0, 60, 1, 0.1, 1500);
            var f = m.Update(0, 1, 0, Pxm, 1, null, v);
            Assert.Equal(new[] { (0, 0), (1, 1) }, f.Build.Select(b => (b.idx, b.lod)).ToArray());   // ahead's LOD0 first, behind gets its shell
            Assert.Equal(0, ahead.Want); Assert.Equal(0, behind.Want);
            m.SetBuilt(1, 1, true);
            m.Update(0, 1, 0, Pxm, 1, null, v);
            Assert.Equal(1, behind.Shown);   // its shell, until it comes into view
            v.Set(0, 1, 0, -1, 0, 0, 0, 1, 0, 60, 1, 0.1, 1500);   // turned round
            f = m.Update(0, 1, 0, Pxm, 1, null, v);
            Assert.Contains((1, 0), f.Build.Select(b => (b.idx, b.lod)));
            Assert.DoesNotContain((0, 0), f.Build.Select(b => (b.idx, b.lod)).Take(1));   // the one now behind isn't first
            Assert.Equal(new[] { (0, 0), (1, 0) }, Step(m).Build.Select(b => (b.idx, b.lod)).OrderBy(b => b).ToArray());   // no view: both, by pixels
        }

        [Fact]
        public void AForcedBuildingWantsLod0FirstWherever()
        {
            var m = new LodManager();
            Box(m, 0, 10, built: false); var far = Box(m, 1, 3000, built: false);
            var f = Step(m, 1, 1);
            Assert.Equal(0, far.Want);
            Assert.Equal((1, 0), (f.Build[0].idx, f.Build[0].lod));
        }

        [Fact]
        public void AForcedBuildingShowsWhatItHasMeanwhile()
        {
            // the renderer no longer waits for a forced LOD0 in Play mode (docs/PERFORMANCE.md §3.1): it shows its shell, or
            // its massing, until the LOD0 is up, and is asked for first, counted apart
            var m = new LodManager();
            var a = Box(m, 0, 3000, built: false); var b = Box(m, 1, 3000, built: false); m.SetBuilt(1, 1, true);
            var f = m.Update(0, 1, 0, Pxm, 1, new[] { 0, 1 });
            Assert.Equal((2, 1), (a.Shown, b.Shown));
            Assert.Equal(2, f.Forced);
            Assert.All(f.Build.Take(2), x => Assert.Equal((0, double.MaxValue), (x.lod, x.px)));
            m.SetBuilt(0, 0, true);
            m.Update(0, 1, 0, Pxm, 1, new[] { 0, 1 });
            Assert.Equal(0, a.Shown);
        }

        [Fact]
        public void APrefetchedBuildingGetsItsLod0AfterTheRestAndKeepsIt()
        {
            // along the camera's line to the player, round the player, round a spawn: LOD0 built ahead, after the forced and
            // those in view, whatever the building shows; kept past the cap while prefetched (docs/PERFORMANCE.md §3.1)
            var m = new LodManager(new LodSettings { MaxLod0 = 1 });
            var near = Box(m, 0, 20, built: false); var far = Box(m, 1, 100, built: false); var forced = Box(m, 2, 3000, built: false);
            var f = m.Update(0, 1, 0, Pxm, 1, new[] { 2 }, null, new[] { 1 });
            Assert.Equal((1, 1, 1), (f.Forced, f.Seen - 1, f.Prefetched));   // seen: near's LOD0 and far's LOD1
            Assert.Equal(new[] { (2, 0), (0, 0), (1, 1), (1, 0) }, f.Build.Select(b => (b.idx, b.lod)).ToArray());
            Assert.Equal(2, far.Shown);   // what it shows doesn't change: LOD1 wanted, nothing built yet
            // built: far keeps its LOD0 past the cap while prefetched though not shown; then not
            foreach (var (i, lod, _) in f.Build) m.SetBuilt(i, lod, true);
            m.Update(0, 1, 0, Pxm, 1, new[] { 2 }, null, new[] { 1 });
            Assert.True(far.Has0); Assert.Equal(1, far.Shown);
            m.Update(0, 1, 0, Pxm, 1, new[] { 2 });
            Assert.False(far.Has0);
            // one that wants LOD0 in view anyway is asked for once
            Box(m, 3, 20, built: false);
            var g = m.Update(0, 1, 0, Pxm, 1, null, null, new[] { 3 });
            Assert.Single(g.Build, x => x.idx == 3 && x.lod == 0);
            Assert.Equal(0, g.Prefetched);
        }

        [Fact]
        public void AChangeCrossFades()
        {
            var m = new LodManager(); var e = Box(m, 0, 1000);
            Step(m, 10);
            m.Set(0, 20, -5, 30, 5, 10); m.SetBuilt(0, 0, true); m.SetBuilt(0, 1, true);
            var f = Step(m, 0.1);
            Assert.Equal(0, e.Shown); Assert.Equal(2, e.From); Assert.InRange(e.T, 0.28, 0.29);
            Assert.True(e.Visible(0) && e.Visible(2) && !e.Visible(1));
            Assert.Contains(0, f.Changed);
            Step(m, 1);
            Assert.Equal(1, e.T); Assert.False(e.Visible(2));
            Assert.Empty(Step(m, 1).Changed);   // settled: nothing to write
        }

        [Fact]
        public void WithoutFadeDetailFullDetailAndShellSwitchAtOnce()
        {
            // the prototype's touch screens: LOD0 and LOD1 switch in one frame, no dithered (discarding) fade of the LOD0
            // mesh; a change to or from the massing still cross-fades
            var m = new LodManager(new LodSettings { FadeDetail = false }); var e = Box(m, 0, 100);   // 10 px/m: LOD1
            Step(m, 10);
            Assert.Equal((1, 1.0), (e.Shown, e.T));
            m.Set(0, 20, -5, 30, 5, 10); m.SetBuilt(0, 0, true); m.SetBuilt(0, 1, true);   // 50 px/m: LOD0
            var f = Step(m, 0.01);
            Assert.Equal((0, 1, 1.0), (e.Shown, e.From, e.T));
            Assert.True(e.Visible(0) && !e.Visible(1));
            Assert.Contains(0, f.Changed);   // its row is written
            m.Set(0, 1000, -5, 1010, 5, 10); m.SetBuilt(0, 0, true); m.SetBuilt(0, 1, true);   // 1 px/m: the massing
            Step(m, 0.01);
            Assert.Equal(2, e.Shown); Assert.True(e.T < 1);   // fading
        }

        [Fact]
        public void PastTheCapTheLeastRecentlyShownGo()
        {
            var m = new LodManager(new LodSettings { MaxLod1 = 2 });
            for (int i = 0; i < 4; i++) Box(m, i, 100);   // all at LOD1
            Step(m);
            // the camera turns away from 0 and 1: four resident, two on screen, so the two off screen go
            foreach (var i in new[] { 0, 1 }) m.Set(i, 5000, -5, 5010, 5, 10);
            foreach (var i in new[] { 0, 1 }) m.SetBuilt(i, 1, true);
            var f = Step(m);
            Assert.Equal(new[] { (0, 1), (1, 1) }, f.Drop.OrderBy(x => x.idx).ToArray());
            // then from 2 and 3: two resident, within the cap, so they stay
            foreach (var i in new[] { 2, 3 }) m.Set(i, 5000, -5, 5010, 5, 10);
            foreach (var i in new[] { 2, 3 }) m.SetBuilt(i, 1, true);
            Assert.Empty(Step(m).Drop);
            Assert.False(m.Get(0)!.Has1); Assert.True(m.Get(3)!.Has1);
        }

        [Fact]
        public void TheCapsDropOnlyWhatIsNotShown()
        {
            // 64 small buildings packed round the camera all show LOD0 with a cap of 8: the caps bound the meshes kept for
            // later, not those shown (docs/CITY.md §2, Residency)
            var l = new LodManager(new LodSettings { MaxLod0 = 8, MaxLod1 = 130 });
            for (int i = 0; i < 64; i++) l.Set(i, i % 8 * 8 - 32, i / 8 * 8 - 32, i % 8 * 8 - 26, i / 8 * 8 - 26, 25);
            double px = LodManager.PixelsPerMetre(1080, 60);
            void Frames(double x)
            {
                for (int f = 0; f < 200; f++) { var fr = l.Update(x, 8, 0, px, 1.0 / 60); foreach (var (i, lod, _) in fr.Build) l.SetBuilt(i, lod, true); }
            }
            Frames(0);
            Assert.Equal(64, l.Resident().lod0);
            Assert.All(l.Entries.Values, e => Assert.Equal(0, e.Shown));
            // gone past the far distance: none shown, and those kept go down to the cap
            Frames(2000);
            Assert.All(l.Entries.Values, e => Assert.Equal(3, e.Shown));
            Assert.Equal(8, l.Resident().lod0);
        }

        [Fact]
        public void PastTheColliderCapThoseWhoseLod0WentLongestAgoGo()
        {
            // buildings 0..5 come close one after another (LOD0) and go far again; 6 stays close, 7 never came close
            var m = new LodManager();
            for (int i = 0; i < 8; i++) Box(m, i, 5000);
            for (int i = 0; i < 6; i++)
            {
                Box(m, i, 20); Step(m); Step(m);   // LOD0 now (50 px/m)
                Box(m, i, 5000); Step(m); Step(m);   // gone far
            }
            Box(m, 6, 20); Step(m); Step(m);
            Assert.Equal(0, m.Get(6)!.Shown);
            var drop = new System.Collections.Generic.List<int>();
            var held = Enumerable.Range(0, 8).ToList();
            m.OldestDetail(held, 8, drop); Assert.Empty(drop);   // none past the cap
            m.OldestDetail(held, 4, drop);
            Assert.Equal(new[] { 7, 0, 1, 2 }, drop);   // never close first, then the longest since LOD0
            m.OldestDetail(held, 0, drop);
            Assert.DoesNotContain(6, drop);   // showing its LOD0: kept whatever the cap
            Assert.Equal(7, drop.Count);
            m.OldestDetail(new[] { 99, 5 }, 1, drop);
            Assert.Equal(new[] { 99 }, drop);   // one no longer in the layout goes first
        }

        [Fact]
        public void SelectionStaysCheapForTheCity()
        {
            var m = new LodManager(); var r = new Random(1);
            for (int i = 0; i < 3000; i++) { double x = r.NextDouble() * 4000 - 2000, z = r.NextDouble() * 4000 - 2000; m.Set(i, x, z, x + 20, z + 20, 30); m.SetBuilt(i, 1, true); }
            Step(m);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int k = 0; k < 50; k++) m.Update(0, 30, 0, Pxm, 1.0 / 60);
            double ms = sw.Elapsed.TotalMilliseconds / 50;
            Console.WriteLine($"LOD selection, 3,000 buildings: {ms:0.000} ms a frame");
            Assert.True(ms < 5, $"{ms:0.00} ms a frame");
        }

        [Fact]
        public void Lod0DistanceIsWhereABuildingTakesLod0ComingCloser()
        {
            // the shader fades LOD0's windows from LOD1's look at this distance: it must be where the switch happens
            var m = new LodManager(); double at = m.Settings.Lod0Distance(Pxm);
            var far = Box(m, 0, at + 0.5); var near = Box(m, 1, at - 0.5);
            far.Want = far.Shown = near.Want = near.Shown = 1;
            Step(m);
            Assert.Equal(1, far.Want);
            Assert.Equal(0, near.Want);
        }
    }
}
