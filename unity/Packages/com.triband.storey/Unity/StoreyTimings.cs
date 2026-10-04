#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Where an edit's time goes, by stage (docs/EDITOR.md §2.1): each stage is a Profiler sample named "Storey …" (search
    /// the Profiler's hierarchy for "Storey"), and, while <see cref="Enabled"/>, its time is added up for the authoring
    /// package's timing log (Tools ▸ Storey ▸ Log Edit Timings). Main thread only.
    /// </summary>
    public static class StoreyTimings
    {
        /// <summary>Add up stage times for the log (the Profiler samples are always there).</summary>
        public static bool Enabled { get; set; }

        static readonly Dictionary<string, (double ms, int n)> acc = new Dictionary<string, (double, int)>(StringComparer.Ordinal);
        static readonly Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public readonly struct Scope : IDisposable
        {
            readonly string what; readonly long t0;
            internal Scope(string what) { this.what = what; t0 = Stopwatch.GetTimestamp(); UnityEngine.Profiling.Profiler.BeginSample("Storey " + what); }
            public void Dispose()
            {
                UnityEngine.Profiling.Profiler.EndSample();
                if (Enabled) Add(what, (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency);
            }
        }

        /// <summary>Time a stage: <c>using (StoreyTimings.Time("site: rebuild")) { … }</c>.</summary>
        public static Scope Time(string what) => new Scope(what);

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
