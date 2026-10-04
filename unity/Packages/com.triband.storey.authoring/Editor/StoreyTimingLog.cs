#nullable enable
using Stopwatch = System.Diagnostics.Stopwatch;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// <i>Tools ▸ Storey ▸ Log Edit Timings</i> (docs/EDITOR.md §2.1): after each edit has settled, one Console line with
    /// where its time went, stage by stage (<see cref="StoreyTimings"/>), and the slowest editor frame since the edit: when
    /// that is far above Storey's stages, the time went somewhere else (another inspector, the Scene view, the GC).
    /// </summary>
    [InitializeOnLoad]
    internal static class StoreyTimingLog
    {
        const string MenuPath = "Tools/Storey/Log Edit Timings", Pref = "Storey.timings";
        static string? pending; static double editAt, lastUpdate, slowest; static readonly Stopwatch since = new Stopwatch();

        static StoreyTimingLog()
        {
            StoreyTimings.Enabled = EditorPrefs.GetBool(Pref, false);
            EditorApplication.update += Update;
        }

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            StoreyTimings.Enabled = !StoreyTimings.Enabled;
            EditorPrefs.SetBool(Pref, StoreyTimings.Enabled);
            StoreyTimings.Take();
            Debug.Log(StoreyTimings.Enabled ? "Storey: logging each edit's timings to the Console." : "Storey: edit timings off.");
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleCheck() { Menu.SetChecked(MenuPath, StoreyTimings.Enabled); return true; }

        /// <summary>An edit began (one per Apply; a drag's steps add up into one line when it settles).</summary>
        public static void Edited(string what)
        {
            if (!StoreyTimings.Enabled) return;
            if (pending == null) { StoreyTimings.Take(); slowest = 0; since.Restart(); }
            pending = what; editAt = EditorApplication.timeSinceStartup;
        }

        static void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            if (pending != null && lastUpdate > 0) slowest = System.Math.Max(slowest, (now - lastUpdate) * 1000);
            lastUpdate = now;
            // settled: nothing edited for a second
            if (pending == null || now - editAt < 1.0) return;
            Debug.Log($"Storey edit \"{pending}\": slowest editor frame {slowest:0} ms, {since.Elapsed.TotalSeconds:0.0} s until it settled. Storey's stages (nested ones are inside \"all of the below\"):{StoreyTimings.Take()}");
            pending = null;
        }
    }
}
