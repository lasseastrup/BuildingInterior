#nullable enable
using System.IO;
using UnityEngine;
#if UNITY_6000_5_OR_NEWER
using GraphicsStateCollection = UnityEngine.Rendering.GraphicsStateCollection;
#else
using GraphicsStateCollection = UnityEngine.Experimental.Rendering.GraphicsStateCollection;
#endif

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The pipeline states the city draws with, made before it first draws them (docs/PERFORMANCE.md §7.9). A shader
    /// variant met for the first time mid-game has its pipeline state compiled in that frame, on Metal a hitch of
    /// milliseconds to tens of them, and the steady variant doubles the states a city can need: a building that starts to
    /// fade, or comes into the way, draws with the discarding one. <c>ShaderVariantCollection.WarmUp</c> can make the wrong
    /// states on Metal, Vulkan and DirectX 12. Unity's <c>GraphicsStateCollection</c> records the states a run used and
    /// makes exactly those.
    /// <para>Tracing: in a development build with <see cref="trace"/> on, the states this run uses are recorded, and saved
    /// when the app goes to the background or quits (<see cref="TracePath"/>), and sent to the editor when one is connected.
    /// Walk the city with each tier's quality settings, then add the file to the project (a .graphicsstate file imports as
    /// a collection) and to <see cref="collections"/>. Warming up: the collection that matches this device's platform and
    /// graphics API (and its quality level, when several do) is made a few states a frame from the start, or all at once
    /// behind a loading screen.</para>
    /// </summary>
    [AddComponentMenu("Storey/Storey Shader Warm-Up")]
    [DisallowMultipleComponent]
    public sealed class StoreyShaderWarmUp : MonoBehaviour
    {
        [Tooltip("Traced collections of pipeline states (.graphicsstate files), one for each platform and graphics API, and for each quality level where they differ. The one that matches this device is made; with none, nothing is.")]
        public GraphicsStateCollection[] collections = new GraphicsStateCollection[0];
        [Tooltip("Pipeline states made a frame: more finishes sooner and holds each frame longer. 0 makes them all in the first frame, behind a loading screen.")]
        [Min(0)] public int perFrame = 16;
        [Tooltip("Development builds: record the pipeline states this run uses, and save them when the app goes to the background or quits, to add to Collections. Release builds can't trace.")]
        public bool trace;

        /// <summary>The collection being made, null with none for this device.</summary>
        public GraphicsStateCollection? Warming { get; private set; }
        /// <summary>Every state of <see cref="Warming"/> made (or nothing to make).</summary>
        public bool WarmedUp => Warming == null || Warming.isWarmedUp;

        /// <summary>Where a trace is saved: the app's persistent data folder, named for the platform and graphics API.</summary>
        public static string TracePath => Path.Combine(Application.persistentDataPath, $"Storey {Application.platform} {SystemInfo.graphicsDeviceType}.graphicsstate");

        GraphicsStateCollection? tracer;
        global::Unity.Jobs.JobHandle step;
        float started; bool noted;

        void Start()
        {
            Warming = Match();
            started = Time.realtimeSinceStartup;
            if (Warming != null)
            {
                Note($"shader warm-up: {Warming.name}, {Warming.totalGraphicsStateCount:N0} pipeline states, {(perFrame > 0 ? perFrame + " a frame" : "all at once")}");
                if (perFrame == 0) { Warming.WarmUp().Complete(); Done(); }
            }
            else if (collections.Length > 0) Note($"shader warm-up: none of the {collections.Length} collections is for {Application.platform} with {SystemInfo.graphicsDeviceType}");
            if (trace && Debug.isDebugBuild)
            {
                tracer = new GraphicsStateCollection();
                Note(tracer.BeginTrace() ? $"shader trace: recording, saved to {TracePath} when the app goes to the background or quits" : "shader trace: Unity didn't start it");
            }
        }

        /// <summary>The collection for this platform and graphics API; of several, the one for this quality level.</summary>
        GraphicsStateCollection? Match()
        {
            int q = QualitySettings.GetQualityLevel(); var names = QualitySettings.names;
            string quality = q >= 0 && q < names.Length ? names[q] : "";
            GraphicsStateCollection? any = null;
            foreach (var c in collections)
            {
                if (c == null || c.runtimePlatform != Application.platform || c.graphicsDeviceType != SystemInfo.graphicsDeviceType) continue;
                if (c.qualityLevelName == quality) return c;
                any ??= c;
            }
            return any;
        }

        void Update()
        {
            if (Warming == null || noted) return;
            if (Warming.isWarmedUp) { Done(); return; }
            if (step.IsCompleted) step = Warming.WarmUpProgressively(perFrame);   // the next few once the last are made
        }

        void Done()
        {
            if (noted) return;
            noted = true;
            Note($"shader warm-up done: {Warming!.completedWarmupCount:N0} pipeline states in {Time.realtimeSinceStartup - started:0.0} s");
        }

        void OnApplicationPause(bool pause) { if (pause) Save(again: true); }
        void OnApplicationQuit() => Save(again: false);
        void OnDestroy() => Save(again: false);

        /// <summary>The trace so far to its file and the editor; tracing goes on after a pause.</summary>
        void Save(bool again)
        {
            if (tracer == null || !tracer.isTracing) return;
            tracer.EndTrace();
            bool saved = tracer.SaveToFile(TracePath), sent = tracer.SendToEditor(Path.GetFileName(TracePath));
            Note($"shader trace: {tracer.variantCount:N0} variants, {(saved ? "saved to " + TracePath : "not saved")}{(sent ? ", sent to the editor" : "")}");
            if (again) tracer.BeginTrace();
        }

        static void Note(string what)
        {
            if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note(what);
            else Debug.Log("Storey: " + what);
        }
    }
}
