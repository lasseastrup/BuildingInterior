using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Auto resolution (<see cref="AutoResolution"/>), the prototype's drsTick: what the phone reports showed holds 60 fps
    /// where the GPU is the limit. Frames come every 1/60 s here; a late one is one the caller blamed on the GPU.
    /// </summary>
    public class AutoResolutionTests
    {
        const double Frame = 1000.0 / 60;

        sealed class Run
        {
            public readonly AutoResolution A = new AutoResolution();
            public double T;
            /// <summary><paramref name="n"/> frames, late or not; the level after each is reported to <paramref name="each"/>.</summary>
            public Run Frames(int n, bool late = false, bool? fits = null, System.Action<int>? each = null)
            {
                for (int i = 0; i < n; i++) { T += Frame; A.Frame(T, late, fits); each?.Invoke(A.Level); }
                return this;
            }
        }

        [Fact]
        public void FramesOnTimeKeepFullResolution()
        {
            var r = new Run().Frames(600);
            Assert.Equal(0, r.A.Level);
            Assert.Equal(1f, r.A.Scale);
        }

        [Fact]
        public void ThreeLateFramesOfTheLastTwentyStepDownALevel()
        {
            var r = new Run().Frames(40).Frames(2, late: true);
            Assert.Equal(0, r.A.Level);
            r.Frames(1, late: true);
            Assert.Equal(1, r.A.Level);
            Assert.Equal(0.9f, r.A.Scale);
            // two late frames now and then aren't three in twenty
            var s = new Run();
            for (int i = 0; i < 20; i++) s.Frames(9).Frames(1, late: true);
            Assert.Equal(0, s.A.Level);
        }

        [Fact]
        public void MostFramesLateStepDownTwoLevelsAtMostEveryHalfSecond()
        {
            var r = new Run().Frames(40);
            int first = -1, steps = 0, was = 0;
            r.Frames(60, late: true, each: l => { if (l != was) { steps++; if (first < 0) first = l; was = l; } });
            Assert.Equal(1, first);   // the first step: three late of twenty
            Assert.Equal(3, r.A.Level);   // half a second later most of twenty were late: two levels
            Assert.Equal(2, steps);
        }

        [Fact]
        public void TwoSecondsWithoutALateFrameStepUpALevel()
        {
            // down to level 3, then on time: a late frame after a step down counts towards the next one
            var r = new Run().Frames(40).Frames(33, late: true);
            Assert.Equal(3, r.A.Level);
            r.Frames(110);
            Assert.Equal(3, r.A.Level);   // late frames still in the last two seconds
            r.Frames(15);
            Assert.Equal(2, r.A.Level);
            r.Frames(110);
            Assert.Equal(2, r.A.Level);
            r.Frames(15);
            Assert.Equal(1, r.A.Level);
            r.Frames(1200);
            Assert.Equal(0, r.A.Level);
            Assert.Equal(1f, r.A.Scale);
        }

        [Fact]
        public void AStepUpThatBringsLateFramesBackHoldsTheLevelBelowFor20Seconds()
        {
            var r = new Run().Frames(40).Frames(3, late: true);   // level 1
            r.Frames(125);   // two seconds clean: up to full
            Assert.Equal(0, r.A.Level);
            r.Frames(30).Frames(3, late: true);   // late again within 3 s of the step up
            Assert.Equal(1, r.A.Level);
            r.Frames(60 * 15);   // fifteen seconds clean: still held
            Assert.Equal(1, r.A.Level);
            r.Frames(60 * 6);    // past the 20 s
            Assert.Equal(0, r.A.Level);
            // a step up that holds for 3 s sets no ceiling: the next drop can climb back two seconds later
            var s = new Run().Frames(40).Frames(3, late: true).Frames(125);
            Assert.Equal(0, s.A.Level);
            s.Frames(240).Frames(3, late: true);
            Assert.Equal(1, s.A.Level);
            s.Frames(125);
            Assert.Equal(0, s.A.Level);
        }

        [Fact]
        public void NoStepUpWhenTheCallerKnowsItWontFit()
        {
            var r = new Run().Frames(40).Frames(3, late: true);
            r.Frames(600, fits: false);
            Assert.Equal(1, r.A.Level);
            r.Frames(1, fits: true);
            Assert.Equal(0, r.A.Level);
        }

        [Fact]
        public void NeverBelowTheLowestShare()
        {
            var r = new Run(); r.A.Lowest = 0.65f;
            r.Frames(40).Frames(600, late: true);
            Assert.Equal(0.65f, r.A.Scale);
            r.Frames(600, late: true);
            Assert.Equal(0.65f, r.A.Scale);
            r.A.Lowest = 0.8f;   // raised: the next frame goes up to it
            r.Frames(1, late: true);
            Assert.Equal(0.8f, r.A.Scale);
            var s = new Run().Frames(40).Frames(600, late: true);
            Assert.Equal(AutoResolution.Levels.Length - 1, s.A.Level);
            Assert.Equal(0.5f, s.A.Scale);
        }

        [Fact]
        public void ResetGoesBackToFullAndForgetsTheFrames()
        {
            var r = new Run().Frames(40).Frames(60, late: true);
            Assert.True(r.A.Level > 0);
            r.A.Reset();
            Assert.Equal(0, r.A.Level);
            r.Frames(20, late: true);   // fewer than 30 frames since: nothing decided yet
            Assert.Equal(0, r.A.Level);
        }
    }
}
