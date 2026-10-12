#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Storey's build threads (docs/CITY.md §2): a few long-lived background threads, named "Storey build N" and below
    /// normal priority where the platform allows it, that run detail and massing builds one after another. A build takes
    /// 8 to 22 ms on a desktop core, longer than Unity's guidance for a job, so it stays off Unity's job workers; and with
    /// its own threads rather than the .NET thread pool, Storey decides how many of them compete with the engine's (the
    /// job workers, the render thread). A <see cref="TaskScheduler"/>, so builds are ordinary tasks: waiting for one that
    /// hasn't started runs it on the waiting thread. The Unity layer registers each thread with its Profiler
    /// (<see cref="Started"/>). Engine-free.
    /// </summary>
    public sealed class BuildThreads : TaskScheduler
    {
        /// <summary>Called on each build thread as it starts, with its name, and as it ends (the Profiler's registration).</summary>
        public static Action<string>? Started;
        public static Action? Ended;

        /// <summary>The build threads every site shares; none until <see cref="Resize"/>.</summary>
        public static BuildThreads Shared { get; } = new BuildThreads(0);

        /// <summary>
        /// How many build threads to have when the settings leave it automatic: two on a phone or tablet, one fewer than
        /// the cores on a desktop, at most four (and at least one).
        /// </summary>
        public static int Automatic(int cores, bool handheld) => Math.Max(1, Math.Min(handheld ? 2 : 4, cores - 1));

        readonly LinkedList<Task> queue = new LinkedList<Task>();
        readonly TaskFactory factory;
        int want, running, named;

        public BuildThreads(int count)
        {
            factory = new TaskFactory(CancellationToken.None, TaskCreationOptions.DenyChildAttach, TaskContinuationOptions.None, this);
            Resize(count);
        }

        /// <summary>How many threads build.</summary>
        public int Count { get { lock (queue) return want; } }

        /// <summary>Run <paramref name="work"/> on a build thread (on the caller's, at once, when there are none).</summary>
        public Task<T> Run<T>(Func<T> work) => factory.StartNew(work);

        /// <summary>Have <paramref name="count"/> threads: more start now; spare ones end once they finish what they run.</summary>
        public void Resize(int count)
        {
            lock (queue)
            {
                want = Math.Max(0, count);
                while (running < want)
                {
                    running++;
                    var t = new Thread(Loop) { IsBackground = true, Name = $"Storey build {++named}" };
                    try { t.Priority = ThreadPriority.BelowNormal; } catch (Exception) { }   // not every platform lets a thread lower its priority
                    t.Start();
                }
                Monitor.PulseAll(queue);
            }
        }

        void Loop()
        {
            Started?.Invoke(Thread.CurrentThread.Name ?? "Storey build");
            try
            {
                while (true)
                {
                    Task task;
                    lock (queue)
                    {
                        while (true)
                        {
                            if (running > want) { running--; return; }
                            var first = queue.First;
                            if (first != null) { task = first.Value; queue.RemoveFirst(); break; }
                            Monitor.Wait(queue);
                        }
                    }
                    TryExecuteTask(task);
                }
            }
            finally { Ended?.Invoke(); }
        }

        protected override void QueueTask(Task task)
        {
            lock (queue)
                if (want > 0) { queue.AddLast(task); Monitor.Pulse(queue); return; }
            TryExecuteTask(task);
        }

        protected override bool TryDequeue(Task task) { lock (queue) return queue.Remove(task); }

        // a build waited for before a thread took it runs on the waiting thread, rather than after the rest of the queue
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
            (!taskWasPreviouslyQueued || TryDequeue(task)) && TryExecuteTask(task);

        protected override IEnumerable<Task> GetScheduledTasks() { lock (queue) return new List<Task>(queue); }

        public override int MaximumConcurrencyLevel { get { lock (queue) return Math.Max(1, want); } }
    }
}
