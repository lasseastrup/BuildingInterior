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
        public void AForcedBuildingWantsLod0FirstWherever()
        {
            var m = new LodManager();
            Box(m, 0, 10, built: false); var far = Box(m, 1, 3000, built: false);
            var f = Step(m, 1, 1);
            Assert.Equal(0, far.Want);
            Assert.Equal((1, 0), (f.Build[0].idx, f.Build[0].lod));
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
    }
}
