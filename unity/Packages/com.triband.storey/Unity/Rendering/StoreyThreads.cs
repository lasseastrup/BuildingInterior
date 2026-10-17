#nullable enable
using System.Threading;
using Triband.Storey.Lod;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Storey's build threads as the engine sees them (docs/PERFORMANCE.md §9.1): one count for every site, and on iOS the
    /// utility quality-of-service class (Plugins/iOS/StoreyThreads.c). Storey Diagnostics' measurement settings set both
    /// for a run that compares.
    /// </summary>
    public static class StoreyThreads
    {
        /// <summary>
        /// Every site's build threads, in place of what their settings ask for (-1: theirs, the most any site asks for; see
        /// <see cref="BuildThreads.SharedCount"/>). 0 builds on the main thread; a site whose settings ask for none (WebGL's)
        /// builds there whatever this says. For a measurement, one count against another.
        /// </summary>
        public static int Override = -1;

        /// <summary>
        /// iOS: the build threads at the utility quality-of-service class, below Unity's main and render threads, so the
        /// scheduler runs them when those leave a core free (and may put them on the efficiency cores). Off, each keeps the
        /// class it started with. On by default. Elsewhere it changes nothing: the threads ask .NET for below-normal
        /// priority, which iOS ignores.
        /// </summary>
        public static bool Utility
        {
            get => utility;
            set { if (utility == value) return; utility = value; BuildThreads.Reconfigure(); }
        }
        static volatile bool utility = true;

        /// <summary>The class the last build thread to set its own has now (iOS: 0x11 utility, 0x15 default, …); -1 before one has, and elsewhere.</summary>
        public static int Class => Volatile.Read(ref lastClass);
        static int lastClass = -1;

        // read on the main thread (Application isn't the build threads'): the plug-in is in the iOS player only, not in the
        // editor with iOS as its build target
        static bool onDevice;
        internal static void Init() => onDevice = Application.platform == RuntimePlatform.IPhonePlayer;

#if UNITY_IOS
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int StoreyThreads_SetUtility(int utility);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int StoreyThreads_Class();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int StoreyThreads_Original();
#endif

        /// <summary>On a build thread, before its first build and when <see cref="Utility"/> changes (<see cref="BuildThreads.Configure"/>).</summary>
        internal static void Configure()
        {
#if UNITY_IOS
            if (!onDevice) return;
            int err = StoreyThreads_SetUtility(utility ? 1 : 0), now = StoreyThreads_Class();
            Volatile.Write(ref lastClass, now);
            if (StoreyDiagnostics.Recording)
                StoreyDiagnostics.Note($"{Thread.CurrentThread.Name}: quality of service {Name(StoreyThreads_Original())} → {Name(now)}{(err != 0 ? $" (refused: errno {err})" : "")}");
#endif
        }

        /// <summary>A quality-of-service class's name.</summary>
        public static string Name(int qos) => qos switch
        {
            0x21 => "user-interactive", 0x19 => "user-initiated", 0x15 => "default", 0x11 => "utility", 0x09 => "background", 0 => "unspecified",
            _ => "0x" + qos.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
