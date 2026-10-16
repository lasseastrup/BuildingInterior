#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Where an edit's time goes, by stage (docs/EDITOR.md §2.1): each stage is a Profiler marker named "Storey …" (search
    /// the Profiler's hierarchy for "Storey"), and, while <see cref="Enabled"/>, its time is added up for the authoring
    /// package's timing log (Tools ▸ Storey ▸ Log Edit Timings). The stage open now is <see cref="Stage"/>, which
    /// <see cref="StoreyDiagnostics"/> reads to say where a hang is. Main thread only.
    /// </summary>
    public static class StoreyTimings
    {
        /// <summary>Add up stage times for the log (the Profiler markers are always there).</summary>
        public static bool Enabled { get; set; }

        /// <summary>
        /// The innermost stage open on the main thread (null outside them all), and the building it is for when the stage
        /// named one. Read from any thread: <see cref="StoreyDiagnostics"/>'s watchdog reads it while the main thread hangs.
        /// </summary>
        public static string? Stage => stage;
        public static string? StageOf => stageOf;
        static volatile string? stage, stageOf;

        /// <summary>A stage of the frame outside Storey's own (physics, rendering), for <see cref="Stage"/>: until the next one.</summary>
        internal static void Enter(string? what) { stage = what; stageOf = null; }

        static readonly Dictionary<string, (double ms, int n)> acc = new Dictionary<string, (double, int)>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public readonly struct Scope : IDisposable
        {
            readonly string what; readonly string? outer, outerOf; readonly long t0; readonly global::Unity.Profiling.ProfilerMarker marker;
            internal Scope(string what, string? of)
            {
                this.what = what; outer = stage; outerOf = stageOf; stage = what; stageOf = of;
                t0 = Stopwatch.GetTimestamp(); marker = Marker(what); marker.Begin();
            }
            public void Dispose()
            {
                marker.End();
                stage = outer; stageOf = outerOf;
                if (Enabled) Add(what, (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            }
        }

        // a ProfilerMarker per stage, made once each: it sends the Profiler an id, not the name, on every sample
        static readonly Dictionary<string, global::Unity.Profiling.ProfilerMarker> markers = new Dictionary<string, global::Unity.Profiling.ProfilerMarker>(StringComparer.Ordinal);
        static global::Unity.Profiling.ProfilerMarker Marker(string what)
        {
            if (!markers.TryGetValue(what, out var m)) markers[what] = m = new global::Unity.Profiling.ProfilerMarker("Storey " + what);
            return m;
        }

        /// <summary>Time a stage: <c>using (StoreyTimings.Time("site: rebuild")) { … }</c>; <paramref name="of"/>, the building it is for.</summary>
        public static Scope Time(string what, string? of = null) => new Scope(what, of);

        public static void Add(string what, double ms)
        {
            acc.TryGetValue(what, out var a); acc[what] = (a.ms + ms, a.n + 1);
        }

        /// <summary>Count something (buildings rebuilt, cells merged) for the log.</summary>
        public static void Count(string what, int n = 1)
        {
            if (!Enabled) return;
            counts.TryGetValue(what, out int c); counts[what] = c + n;
        }

        /// <summary>The stages since the last call, slowest first, and forget them.</summary>
        public static string Take()
        {
            var sb = new StringBuilder();
            foreach (var kv in acc.OrderByDescending(kv => kv.Value.ms))
                sb.Append($"\n  {kv.Value.ms,8:0.0} ms  {kv.Key}{(kv.Value.n > 1 ? $" (×{kv.Value.n})" : "")}");
            if (counts.Count > 0) sb.Append("\n  " + string.Join(", ", counts.Select(kv => $"{kv.Key} {kv.Value}")));
            acc.Clear(); counts.Clear();
            return sb.ToString();
        }

        /// <summary>The total of the stages since the last <see cref="Take"/> (they may nest: the log says which do).</summary>
        public static double Total(string prefix) => acc.Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal)).Sum(kv => kv.Value.ms);
    }
}
