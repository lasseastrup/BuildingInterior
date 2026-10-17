#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Which builds a thread takes first (docs/PERFORMANCE.md §9.2): the first one queued of the most urgent lane. A build
    /// already running isn't stopped; one waiting is overtaken.
    /// </summary>
    public enum BuildLane
    {
        /// <summary>Detail a building can't do without now: one in the camera's way, the one the player is in.</summary>
        Forced,
        /// <summary>Detail, the buildings in view first (the renderer asks in that order).</summary>
        Detail,
        /// <summary>A layout's cells, their massings, loading over frames.</summary>
        Load,
        /// <summary>Wanted soon rather than now: layout parses, detail prefetched.</summary>
        Background,
    }

    /// <summary>
    /// Storey's build threads (docs/CITY.md §2): a few long-lived background threads, named "Storey build N" and below
    /// normal priority where the platform allows it, that run detail and massing builds one after another. A build takes
    /// 8 to 22 ms on a desktop core, longer than Unity's guidance for a job, so it stays off Unity's job workers; and with
    /// its own threads rather than the .NET thread pool, Storey decides how many of them compete with the engine's (the
    /// job workers, the render thread). A <see cref="TaskScheduler"/>, so builds are ordinary tasks: waiting for one that
    /// hasn't started runs it on the waiting thread. Builds wait in lanes (<see cref="BuildLane"/>), the most urgent taken
    /// first. The Unity layer registers each thread with its Profiler (<see cref="Started"/>). Engine-free.
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

        // the lanes, most urgent first; the lock is the first lane's (as it was the one queue's)
        readonly LinkedList<Task>[] lanes = { new LinkedList<Task>(), new LinkedList<Task>(), new LinkedList<Task>(), new LinkedList<Task>() };
        readonly LinkedList<Task> queue;
        // the lane each build started is for, until it is queued (Task.Start queues it on the caller's thread, at once)
        readonly Dictionary<Task, BuildLane> laneOf = new Dictionary<Task, BuildLane>();
        int want, running, named;

        public BuildThreads(int count)
        {
            queue = lanes[0];
            Resize(count);
        }

        /// <summary>How many threads build.</summary>
        public int Count { get { lock (queue) return want; } }

        /// <summary>The builds waiting for a thread.</summary>
        internal int Queued { get { lock (queue) { int n = 0; foreach (var l in lanes) n += l.Count; return n; } } }

        /// <summary>The builds waiting for a thread in one lane.</summary>
        public int QueuedIn(BuildLane lane) { lock (queue) return lanes[(int)lane].Count; }

        /// <summary>
        /// Run <paramref name="work"/> on a build thread (on the caller's, at once, when there are none), after what waits in
        /// the more urgent lanes and in its own.
        /// </summary>
        public Task<T> Run<T>(Func<T> work, BuildLane lane = BuildLane.Detail)
        {
            var t = new Task<T>(work, TaskCreationOptions.DenyChildAttach);
            lock (queue) laneOf[t] = lane;
            t.Start(this);
            return t;
        }

        /// <summary>
        /// Move a build that is still waiting into a more urgent lane, last in it (a building wanted at once whose build was
        /// queued as wanted soon). False when it has started, or is in that lane or a more urgent one already.
        /// </summary>
        public bool Promote(Task task, BuildLane lane)
        {
            lock (queue)
            {
                for (int i = (int)lane + 1; i < lanes.Length; i++)
                    if (lanes[i].Remove(task)) { lanes[(int)lane].AddLast(task); return true; }
                return false;
            }
        }

        /// <summary>
        /// Have <paramref name="count"/> threads: more start now; spare ones end once they finish what they run. With none,
        /// the last ones run what is queued before they end, as no thread would after (a build only polled for, as the
        /// renderer's are, would never finish); what is asked for after runs on the caller.
        /// </summary>
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
                            var first = Next();
                            if (running > want && (want > 0 || first == null)) { running--; return; }
                            if (first != null) { task = first.Value; first.List!.RemoveFirst(); break; }
                            Monitor.Wait(queue);
                        }
                    }
                    TryExecuteTask(task);
                }
            }
            finally { Ended?.Invoke(); }
        }

        // the first build waiting in the most urgent lane that has one (under the lock)
        LinkedListNode<Task>? Next()
        {
            foreach (var l in lanes) if (l.First != null) return l.First;
            return null;
        }

        protected override void QueueTask(Task task)
        {
            lock (queue)
            {
                var lane = laneOf.TryGetValue(task, out var l) ? l : BuildLane.Detail;
                laneOf.Remove(task);
                if (want > 0) { lanes[(int)lane].AddLast(task); Monitor.Pulse(queue); return; }
            }
            TryExecuteTask(task);
        }

        protected override bool TryDequeue(Task task)
        {
            lock (queue) { foreach (var l in lanes) if (l.Remove(task)) return true; return false; }
        }

        // a build waited for before a thread took it runs on the waiting thread, rather than after the rest of the queue
        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
            (!taskWasPreviouslyQueued || TryDequeue(task)) && TryExecuteTask(task);

        protected override IEnumerable<Task> GetScheduledTasks() { lock (queue) { var all = new List<Task>(); foreach (var l in lanes) all.AddRange(l); return all; } }

        public override int MaximumConcurrencyLevel { get { lock (queue) return Math.Max(1, want); } }
    }
}
