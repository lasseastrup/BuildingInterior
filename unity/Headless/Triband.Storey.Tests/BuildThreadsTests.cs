using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Storey's own build threads (<see cref="BuildThreads"/>, docs/CITY.md §2): named, at most as many at once as asked
    /// for, a build waited for before it starts run by the waiting thread, a failure kept in its task, what is queued run before
    /// the threads stop. Storey's own.
    /// </summary>
    public class BuildThreadsTests
    {
        static void Done(Task[] tasks) { var sw = System.Diagnostics.Stopwatch.StartNew(); while (!tasks.All(t => t.IsCompleted) && sw.ElapsedMilliseconds < 10000) Thread.Sleep(1); Assert.True(tasks.All(t => t.IsCompleted)); }

        [Fact]
        public void AutomaticIsTwoOnAPhoneAndUpToFourOnADesktop()
        {
            Assert.Equal(2, BuildThreads.Automatic(6, true));
            Assert.Equal(1, BuildThreads.Automatic(2, true));
            Assert.Equal(4, BuildThreads.Automatic(16, false));
            Assert.Equal(3, BuildThreads.Automatic(4, false));
            Assert.Equal(1, BuildThreads.Automatic(1, false));
        }

        [Fact]
        public void BuildsRunOnNamedThreadsAtMostAsManyAtOnceAsAskedFor()
        {
            var bt = new BuildThreads(2);
            try
            {
                int now = 0, most = 0; var names = new ConcurrentBag<string>();
                var tasks = Enumerable.Range(0, 12).Select(i => bt.Run(() =>
                {
                    int n = Interlocked.Increment(ref now); int m; while ((m = most) < n && Interlocked.CompareExchange(ref most, n, m) != m) { }
                    names.Add(Thread.CurrentThread.Name ?? ""); Thread.Sleep(5);
                    Interlocked.Decrement(ref now); return i * i;
                })).ToArray();
                Done(tasks);   // not Task.WaitAll: a waiting thread runs queued builds itself
                Assert.Equal(Enumerable.Range(0, 12).Select(i => i * i), tasks.Select(t => t.Result));
                Assert.InRange(most, 1, 2);
                Assert.All(names, n => Assert.StartsWith("Storey build ", n));
                // fewer threads: the spare one ends, and builds after run one at a time
                bt.Resize(1); now = 0; most = 0;
                Done(Enumerable.Range(0, 6).Select(i => bt.Run(() =>
                {
                    int n = Interlocked.Increment(ref now); int m; while ((m = most) < n && Interlocked.CompareExchange(ref most, n, m) != m) { }
                    Thread.Sleep(3); Interlocked.Decrement(ref now); return 0;
                })).ToArray());
                Assert.Equal(1, most);
            }
            finally { bt.Resize(0); }
        }

        [Fact]
        public void ABuildWaitedForBeforeItStartsRunsOnTheWaitingThread()
        {
            var bt = new BuildThreads(1);
            using var hold = new ManualResetEventSlim(false);
            try
            {
                var busy = bt.Run(() => { hold.Wait(); return 0; });   // the only thread, held
                var queued = bt.Run(() => Environment.CurrentManagedThreadId);
                // a wait without a timeout runs it here; should it not, the held thread is let go after a while (no hang)
                using var letGo = new Timer(_ => hold.Set(), null, 5000, Timeout.Infinite);
                queued.Wait();
                Assert.Equal(Environment.CurrentManagedThreadId, queued.Result);
                hold.Set(); busy.Wait();
            }
            finally { hold.Set(); bt.Resize(0); }
        }

        [Fact]
        public void TheMostUrgentLaneGoesFirst()
        {
            // docs/PERFORMANCE.md §9.2: forced detail before detail, a load's cells, then parses and prefetch; first come first
            // in a lane; a waiting build moved to a more urgent lane overtakes
            var bt = new BuildThreads(1);
            using var started = new ManualResetEventSlim(false);
            using var hold = new ManualResetEventSlim(false);
            try
            {
                var busy = bt.Run(() => { started.Set(); hold.Wait(); return 0; });   // the only thread, held
                Assert.True(started.Wait(5000));
                var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
                Task<int> Q(string name, BuildLane lane) => bt.Run(() => { order.Enqueue(name); return 0; }, lane);
                var tasks = new[]
                {
                    Q("parse", BuildLane.Background), Q("cell 1", BuildLane.Load), Q("detail 1", BuildLane.Detail), Q("cell 2", BuildLane.Load),
                    Q("forced", BuildLane.Forced), Q("detail 2", BuildLane.Detail), Q("prefetch", BuildLane.Background),
                };
                Assert.Equal((7, 2, 2), (bt.Queued, bt.QueuedIn(BuildLane.Load), bt.QueuedIn(BuildLane.Background)));
                Assert.True(bt.Promote(tasks[6], BuildLane.Forced));       // the prefetched building is wanted now
                Assert.False(bt.Promote(tasks[2], BuildLane.Load));        // not to a less urgent lane
                hold.Set();
                Done(tasks.Cast<Task>().Append(busy).ToArray());
                Assert.Equal(new[] { "forced", "prefetch", "detail 1", "detail 2", "cell 1", "cell 2", "parse" }, order.ToArray());
                Assert.False(bt.Promote(tasks[0], BuildLane.Forced));      // done: nothing to move
            }
            finally { hold.Set(); bt.Resize(0); }
        }

        [Fact]
        public void AFailureStaysInItsTaskAndNoThreadsRunOnTheCaller()
        {
            var bt = new BuildThreads(1);
            try
            {
                var bad = bt.Run<int>(() => throw new InvalidOperationException("a broken layout"));
                Assert.Throws<AggregateException>(() => bad.Wait());
                Assert.True(bad.IsFaulted);
                Assert.IsType<InvalidOperationException>(bad.Exception!.InnerException);
                Assert.Equal(7, bt.Run(() => 7).Result);   // the thread carries on
            }
            finally { bt.Resize(0); }
            // none: a build runs at once, on the caller's thread
            var none = new BuildThreads(0);
            var t = none.Run(() => Environment.CurrentManagedThreadId);
            Assert.True(t.IsCompleted);
            Assert.Equal(Environment.CurrentManagedThreadId, t.Result);
        }

        [Fact]
        public void StoppingTheThreadsRunsWhatIsQueuedFirst()
        {
            // a build queued when the threads stopped (worker builds switched off mid-load) never ran, and the renderer, which
            // polls its builds rather than wait for them, waited for ever: on a detail job, a load's cell, a layout's parse
            var bt = new BuildThreads(1);
            using var started = new ManualResetEventSlim(false);
            using var hold = new ManualResetEventSlim(false);
            try
            {
                var busy = bt.Run(() => { started.Set(); hold.Wait(); return 1; });
                Assert.True(started.Wait(5000));
                var queued = bt.Run(() => 2);
                bt.Resize(0); hold.Set();
                Done(new Task[] { busy, queued });   // polled: a wait would run it here
                Assert.Equal(2, queued.Result);
                Assert.Equal(0, bt.Queued);
                var after = bt.Run(() => Environment.CurrentManagedThreadId);   // with none, on the caller
                Assert.True(after.IsCompleted);
                Assert.Equal(Environment.CurrentManagedThreadId, after.Result);
            }
            finally { hold.Set(); bt.Resize(0); }
        }
    }
}
