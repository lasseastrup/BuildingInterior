using System;
using System.Threading;
using Triband.Storey.Edit;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A layout's text parsed on a build thread (<see cref="LayoutParse"/>, docs/CITY.md §2): not there until it is done,
    /// then the same layout as parsed at once; at once with no build threads; waited for, parsed on the waiting thread.
    /// </summary>
    public class LayoutParseTests
    {
        static readonly Lazy<string> Text = new(() =>
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 300);
            return PrototypeJson.Write(d);
        });

        [Fact]
        public void ALayoutIsThereOnceItsBuildThreadHasParsedIt()
        {
            var bt = new BuildThreads(1);
            using var hold = new ManualResetEventSlim(false);
            try
            {
                var busy = bt.Run(() => { hold.Wait(); return 0; });   // the only thread, held: the parse waits behind it
                var parse = new LayoutParse(Text.Value, bt);
                Assert.Same(Text.Value, parse.Text);
                Assert.Null(parse.Document);
                hold.Set(); busy.Wait();
                for (int i = 0; i < 2000 && parse.Document == null; i++) Thread.Sleep(5);
                Assert.NotNull(parse.Document);
                Assert.Equal(PrototypeJson.Write(PrototypeJson.Read(Text.Value).Document), PrototypeJson.Write(parse.Document!));
                Assert.Same(parse.Document, parse.Wait());
            }
            finally { hold.Set(); bt.Resize(0); }
        }

        [Fact]
        public void WithNoBuildThreadsItIsParsedAtOnce()
        {
            var parse = new LayoutParse(Text.Value, new BuildThreads(0));
            Assert.NotNull(parse.Document);
        }

        [Fact]
        public void WaitingParsesItHereWhenNoThreadHasStartedIt()
        {
            var bt = new BuildThreads(1);
            using var hold = new ManualResetEventSlim(false);
            try
            {
                var busy = bt.Run(() => { hold.Wait(); return 0; });
                var parse = new LayoutParse(Text.Value, bt);
                // should it not run here, the held thread is let go after a while (no hang) and the parse still comes
                using var letGo = new Timer(_ => hold.Set(), null, 5000, Timeout.Infinite);
                Assert.NotNull(parse.Wait());
                Assert.False(hold.IsSet);   // parsed on this thread, the build thread still held
                hold.Set(); busy.Wait();
            }
            finally { hold.Set(); bt.Resize(0); }
        }

        [Fact]
        public void ABrokenLayoutThrowsWhenItIsAskedFor()
        {
            var parse = new LayoutParse("{ not a layout", new BuildThreads(0));
            Assert.ThrowsAny<Exception>(() => parse.Document);
            Assert.ThrowsAny<Exception>(() => parse.Wait());
        }
    }
}
