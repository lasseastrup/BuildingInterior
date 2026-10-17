#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Triband.Storey.Lod;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// A flight recorder for a device with no debugger attached (docs/PERFORMANCE.md §14). Add it to any object in the
    /// scene. While the player runs it keeps a line for each of the last frames: its time and, with Frame Timing Stats on
    /// (Player Settings), the CPU's and the GPU's; how far behind the GPU is; each site's LOD numbers and wall table; the
    /// building the player is in, its floor and the buildings in the way; the player's feet and the camera. Beside them it
    /// notes what happened: Unity's warnings, errors and exceptions, LOD0 built at once for the occlusion, a table grown,
    /// every camera that renders and where to. On iOS it notes Metal's own word on each GPU command buffer that fails
    /// (Plugins/iOS/StoreyMetalErrors.mm): why, a GPU fault or too much geometry for a pass's memoryless attachments, and
    /// which of its passes it was.
    ///
    /// A thread of its own watches the frames. When none has come for a second it notes the stage the main thread is in
    /// (<see cref="StoreyTimings.Stage"/>: Storey's own, or physics, scripts or rendering) and when the GPU last finished
    /// a frame, and again each time the hang has gone on twice as long; when frames go on but the GPU finishes none, it
    /// notes that. It saves the record to <see cref="FilePath"/> every two seconds, so what led up to a hang is there after
    /// the app is force-quit, and the next run shows it with a button that copies it.
    /// </summary>
    [AddComponentMenu("Storey/Storey Diagnostics")]
    [DefaultExecutionOrder(2000)]   // after the sites (1000): a frame's line has their numbers for the frame
    [DisallowMultipleComponent]
    public sealed class StoreyDiagnostics : MonoBehaviour
    {
        [Tooltip("At start, show the record the last run left, with a button that copies it.")]
        public bool showLastRun = true;
        [Tooltip("How many frames the record keeps a line for.")]
        [Min(30)] public int frames = 240;
        [Tooltip("iOS: note Metal's own word on each GPU command buffer that fails: why (a GPU fault, too much geometry for a pass's memoryless attachments, …) and which of its passes. Metal records every pass's state for it, which costs the GPU a little.")]
        public bool metalErrors = true;

        [Header("Measurement")]
        [Tooltip("For a run that measures the build threads (docs/PERFORMANCE.md §9.1): every site's count, in place of what the quality assets ask for (-1: theirs; 0 builds on the main thread). Make a run with 1 and one with 2, and compare the records' frame times and their 'detail' notes: how long detail takes to come, and how many buildings wait for a thread.")]
        [Min(-1)] public int buildThreads = -1;
        [Tooltip("iOS: the build threads at the utility quality-of-service class, below Unity's main and render threads (the default). Off for a run that compares: each keeps the class it started with.")]
        public bool utilityThreads = true;

        /// <summary>A recorder is running: notes are kept, so a caller makes one only then.</summary>
        public static bool Recording => current != null;
        static StoreyDiagnostics? current;

        /// <summary>The record this run writes (in the app's persistent data folder); the last run's is moved beside it at start.</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, "StoreyDiagnostics.txt");
        static string LastRunPath => Path.Combine(Application.persistentDataPath, "StoreyDiagnostics (last run).txt");

        /// <summary>Note something that happened, from any thread (kept while <see cref="Recording"/>).</summary>
        public static void Note(string what) => current?.Add(what);

        /// <summary>The milliseconds since a <see cref="Stopwatch.GetTimestamp"/>.</summary>
        public static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

        // the frames' lines (a ring) and the notes, with the header: written on the main thread and from the log and the
        // watchdog's thread, saved by the watchdog
        readonly object gate = new object();
        string[] ring = Array.Empty<string>();
        int ringNext, ringCount;
        readonly List<string> notes = new List<string>();
        string? lastRaw, lastNote; int repeats;
        string header = "", path = "";
        bool dirty;

        // what the watchdog reads of the main thread: the last frame, when it came, and the last frame the GPU finished
        static long started;
        long frameAt; int frameNo;
        long gpuDoneAt; int gpuDone = -1, gpuErrors;
        Thread? watchdog; volatile bool running, paused;

        // the GPU's heartbeat: a 4-byte buffer read back each frame, asked for before the frame renders. Each callback says
        // the GPU has finished every frame before the one it was asked in (they come back in order)
        GraphicsBuffer? probe;
        readonly Queue<int> asked = new Queue<int>();
        Action<UnityEngine.Rendering.AsyncGPUReadbackRequest>? onRead;
        // the probe holds the number of the frame that wrote it: a readback that reads an earlier one wasn't run by the GPU
        readonly int[] probeValue = new int[1];
        int readbackStale;
        // the GPU's own time (Frame Timing Stats): 0 frame after frame, once it has been more, says its work is thrown away
        int gpuZero, gpuLastTimed = -1;
        // the frames before the first sign of trouble, kept apart from the ring, which goes on and would lose them
        string[]? incident; string incidentWhat = "";
        readonly Dictionary<StoreySite, (string? building, int floor)> where = new Dictionary<StoreySite, (string?, int)>();

        readonly FrameTiming[] timing = new FrameTiming[1];
        float lastScale = -1;
        // memory, as the Memory Profiler counts it (release players too): the process's, Unity's, the graphics driver's
        global::Unity.Profiling.ProfilerRecorder systemMemory, totalMemory, gfxMemory;
        long memoryNoted;
        readonly StringBuilder line = new StringBuilder(512);
        readonly Dictionary<StoreySite, (StoreyOcclusion? o, Camera? cam, RenderTexture? rt)> described = new Dictionary<StoreySite, (StoreyOcclusion?, Camera?, RenderTexture?)>();
        // the cameras that render, noted when they or their targets change
        Camera[] cameras = new Camera[8];
        int? camerasSeen;
        // Metal's own word on the command buffers that fail (iOS: Plugins/iOS/StoreyMetalErrors.mm), taken each frame
        bool metalWatched; long metalFailed;
#if UNITY_IOS
        byte[]? metalText; int metalTaken, metalCheckAt;
#endif
        // the build threads (docs/PERFORMANCE.md §9.1): the measurement's settings as they were before this run set them,
        // the count last noted, and the window the detail notes sum up
        int threadsBefore = -1; bool utilityBefore = true; int threadsSeen = -1;
        double detailFrom; long waitSum; int waitMost, waitFrames;
        readonly List<float>[] detailMs = { new List<float>(), new List<float>() };
        static bool movedLastRun;
        string? lastRun, lastRunSummary; bool showing; string copied = "";
        GUIStyle? textStyle, buttonStyle;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F1(double x) => x.ToString("0.0", Inv);
        static string F2(double x) => x.ToString("0.00", Inv);
        static double Seconds(long ticks) => ticks / (double)Stopwatch.Frequency;

        void OnEnable()
        {
            if (current != null && current != this) { enabled = false; return; }   // one recorder at a time
            current = this; useGUILayout = false;
            if (started == 0) started = Stopwatch.GetTimestamp();
            path = FilePath;   // Application's paths are the main thread's only: the watchdog saves to this
            ring = new string[Math.Max(30, frames)]; ringNext = ringCount = 0;
            lock (gate) { notes.Clear(); lastRaw = lastNote = null; repeats = 0; incident = null; incidentWhat = ""; }
            described.Clear(); where.Clear(); camerasSeen = null;
            readbackStale = 0; gpuZero = 0; gpuLastTimed = -1;
            // the record the last run left: set aside once a run, shown until closed; this run writes a new one
            if (!movedLastRun)
            {
                movedLastRun = true;
                try
                {
                    if (File.Exists(path)) { if (File.Exists(LastRunPath)) File.Delete(LastRunPath); File.Move(path, LastRunPath); }
                    if (showLastRun && File.Exists(LastRunPath)) { lastRun = File.ReadAllText(LastRunPath); lastRunSummary = Summary(lastRun); showing = true; }
                }
                catch (Exception e) { UnityEngine.Debug.LogWarning("Storey Diagnostics: the last run's record couldn't be read: " + e.Message); }
            }
            threadsBefore = StoreyThreads.Override; utilityBefore = StoreyThreads.Utility;
            if (buildThreads >= 0) StoreyThreads.Override = buildThreads;
            StoreyThreads.Utility = utilityThreads;
            threadsSeen = -1; detailFrom = Time.realtimeSinceStartupAsDouble; waitSum = 0; waitMost = waitFrames = 0;
            SiteRenderer.DetailTimes.Clear();
            header = Header() + WatchMetal() + "\n" + Threads() + "\n";
            Application.logMessageReceivedThreaded += OnLog;
            Application.lowMemory += OnLowMemory;
            systemMemory = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Memory, "System Used Memory");
            totalMemory = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Memory, "Total Used Memory");
            gfxMemory = global::Unity.Profiling.ProfilerRecorder.StartNew(global::Unity.Profiling.ProfilerCategory.Memory, "Gfx Used Memory");
            memoryNoted = 0;
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += OnBeginRendering;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering += OnEndRendering;
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                probe = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 4);
                probe.SetData(new int[1]);
                onRead = OnRead;
            }
            gpuDone = -1; gpuErrors = 0; asked.Clear();
            Volatile.Write(ref frameAt, Stopwatch.GetTimestamp());
            Add("recording: " + path);
            running = true;
            watchdog = new Thread(Watch) { IsBackground = true, Name = "Storey Diagnostics" };
            watchdog.Start();
        }

        void OnDisable()
        {
            if (current != this) return;
            running = false;
            watchdog?.Join(1000); watchdog = null;
            Application.logMessageReceivedThreaded -= OnLog;
            Application.lowMemory -= OnLowMemory;
            systemMemory.Dispose(); totalMemory.Dispose(); gfxMemory.Dispose();
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering -= OnBeginRendering;
            UnityEngine.Rendering.RenderPipelineManager.endContextRendering -= OnEndRendering;
            TakeMetal(Time.frameCount);
            NoteDetail(true);
            StoreyThreads.Override = threadsBefore; StoreyThreads.Utility = utilityBefore;
            Add("recording stopped");
            Save();
            probe?.Dispose(); probe = null; asked.Clear();
            current = null;
        }

        // in the background no frames come, and that's no stall
        void OnApplicationPause(bool pause)
        {
            if (pause) TakeMetal(Time.frameCount);
            paused = pause; Add(pause ? "paused (in the background)" : "resumed");
            if (!pause) { long now = Stopwatch.GetTimestamp(); Volatile.Write(ref frameAt, now); Volatile.Write(ref gpuDoneAt, now); }
        }

        // the stages of the frame outside Storey's code, for a stall's note: each holds until the next is entered
        void FixedUpdate() => StoreyTimings.Enter("physics: after the scripts' FixedUpdate (the simulation step, contacts, collision callbacks)");
        void Update() => StoreyTimings.Enter("scripts: Update and LateUpdate");
        void OnBeginRendering(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras) => StoreyTimings.Enter("rendering: the cameras");
        void OnEndRendering(UnityEngine.Rendering.ScriptableRenderContext context, List<Camera> cameras) =>
            StoreyTimings.Enter("after rendering: presenting the frame, then the next one's input, physics and Update");

        void LateUpdate()
        {
            int f = Time.frameCount;
            Volatile.Write(ref frameNo, f); Volatile.Write(ref frameAt, Stopwatch.GetTimestamp());
            if (probe != null && asked.Count < 60)
            {
                probeValue[0] = f; probe.SetData(probeValue);
                asked.Enqueue(f); UnityEngine.Rendering.AsyncGPUReadback.Request(probe, 4, 0, onRead);
            }
            TakeMetal(f);
            Describe();
            DescribeCameras();
            WatchThreads();
            var text = Line(f);
            lock (gate)
            {
                ring[ringNext] = text; ringNext = (ringNext + 1) % ring.Length;
                if (ringCount < ring.Length) ringCount++;
                dirty = true;
            }
            StoreyTimings.Enter("rendering: after LateUpdate, before the cameras (culling)");
        }

        void OnLowMemory() => Add($"LOW MEMORY: the OS asked the app to free memory ({Mb(systemMemory)} MB in use)");

        static string Mb(global::Unity.Profiling.ProfilerRecorder r) => r.Valid ? (r.LastValue >> 20).ToString(Inv) : "?";

        void OnRead(UnityEngine.Rendering.AsyncGPUReadbackRequest r)
        {
            int f = asked.Count > 0 ? asked.Dequeue() : -1;
            if (r.hasError) { Interlocked.Increment(ref gpuErrors); return; }
            // a later frame's number is a write that came first; an earlier one, a copy the GPU never ran
            var data = r.GetData<int>();
            int got = data.Length > 0 ? data[0] : int.MinValue;
            if (f >= 0 && got < f && readbackStale++ == 0)
            {
                Add($"GPU: the readback asked for in frame {f} read {got}: the GPU didn't run that frame's work");
                Incident($"the first frame whose work the GPU didn't run ({f})");
            }
            Volatile.Write(ref gpuDone, f - 1); Volatile.Write(ref gpuDoneAt, Stopwatch.GetTimestamp());
        }

        /// <summary>This frame's line.</summary>
        string Line(int f)
        {
            var b = line; b.Clear();
            b.Append(f).Append(' ').Append(F2(Time.realtimeSinceStartupAsDouble)).Append("s ").Append(F1(Time.unscaledDeltaTime * 1000.0)).Append("ms");
            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                {
                    double g = timing[0].gpuFrameTime;
                    b.Append(" cpu ").Append(F1(timing[0].cpuMainThreadFrameTime)).Append(" gpu ").Append(F1(g)).Append(" wait ").Append(F1(timing[0].cpuMainThreadPresentWaitTime));
                    // the timings are a few frames late: the frame named is about when it changed
                    if (g > 0) { if (gpuZero >= 30) Add($"GPU: frame times again, after {gpuZero} frames at 0 ms"); gpuZero = 0; gpuLastTimed = f; }
                    else if (gpuLastTimed >= 0 && ++gpuZero == 30)
                    {
                        Add($"GPU: 0 ms a frame for 30 frames, since about frame {gpuLastTimed}: its work is being thrown away (after a GPU error?)");
                        Incident($"the GPU's time went to 0 (about frame {gpuLastTimed})");
                    }
                }
            }
            int done = Volatile.Read(ref gpuDone);
            if (probe != null) b.Append(" gpu-").Append(done >= 0 ? (f - done).ToString(Inv) : "?");
            int errors = Volatile.Read(ref gpuErrors);
            if (errors > 0) b.Append(" readback errors ").Append(errors);
            if (metalFailed > 0) b.Append(" mtl-fail ").Append(metalFailed);
            // dynamic resolution (Storey Auto Resolution, or the project's own): the share of each axis drawn
            float scale = ScalableBufferManager.widthScaleFactor;
            if (scale != 1) b.Append(" res ").Append(F2(scale));
            if (scale != lastScale) { if (lastScale > 0) Add($"dynamic resolution: {F2(lastScale)} → {F2(scale)} of each axis"); lastScale = scale; }
            foreach (var s in StoreySite.AllSet)
            {
                if (!s.isActiveAndEnabled) continue;
                b.Append(" | ").Append(s.name).Append(':');
                if (s.LodStats is SiteRenderer.LodStats st)
                {
                    b.Append(" L0 ").Append(st.lod0).Append(" L1 ").Append(st.lod1).Append(" L2 ").Append(st.lod2).Append(" res0 ").Append(st.resident0)
                        .Append(" gen ").Append(st.generating).Append(" upl ").Append(st.uploading).Append(" wait ").Append(st.queued).Append(" built ").Append(st.builtThisFrame).Append(" lod ").Append(F1(st.ms)).Append("ms");
                    if (st.loading > 0) b.Append(" loading ").Append(st.loading);
                    b.Append(" v0 ").Append(st.lod0VerticesInView / 1000).Append("k/").Append(st.lod0Vertices / 1000).Append('k').Append(" rooms ").Append(st.roomsShown).Append('/').Append(st.lod0);
                }
                var r = s.Renderer;
                if (r != null) b.Append(" walls ").Append(r.Table.WallsInUse).Append('/').Append(r.Table.Wall.Length);
                var o = s.Occlusion; var core = o != null && o.isActiveAndEnabled ? o.Core : null;
                if (o == null || core == null) continue;
                var v = core.View;
                var at = (v.active?.name, v.active != null ? v.floor : -1);
                if (!where.TryGetValue(s, out var was) || was != at) { where[s] = at; Add(v.active != null ? $"player in {v.active.name}, floor {v.floor}" : "player in no building"); }
                b.Append(" | in ").Append(v.active != null ? v.active.name : "-");
                if (v.active != null && r != null)
                {
                    b.Append(" floor ").Append(v.floor).Append(" clip ").Append(F1(v.clipY)).Append(v.cutOn ? " cut" : "");
                    int idx = r.TableIndexOf(v.active.id);
                    if (idx >= 0) { var row = r.Table.State[idx]; b.Append(" state ").Append(F1(row.x)).Append(',').Append(F1(row.y)).Append(',').Append(F2(row.z)).Append(',').Append(F1(row.w)); }
                }
                if (core.Active.Count > 0)
                {
                    b.Append(" | in the way ").Append(core.Active.Count).Append(':');
                    for (int i = 0; i < core.Active.Count && i < 6; i++) { var oc = core.Active[i]; b.Append(' ').Append(oc.b.name).Append(" k").Append(oc.k).Append(" s").Append(oc.slot); }
                }
                if (o.player != null) { var p = o.player.position; b.Append(" | feet ").Append(F1(p.x)).Append(',').Append(F1(p.y)).Append(',').Append(F1(p.z)); }
                var cam = o.viewCamera != null ? o.viewCamera : Camera.main;
                if (cam != null) { var c = cam.transform.position; b.Append(" cam ").Append(F1(c.x)).Append(',').Append(F1(c.y)).Append(',').Append(F1(c.z)); }
            }
            // renderers given their steady copy or their own material again (each re-registered by the GPU Resident Drawer), made,
            // and LOD0 rooms switched on or off
            if (SiteRenderer.SteadySwaps > 0 || SiteRenderer.RenderersMade > 0 || SiteRenderer.RoomsSwitched > 0)
            {
                b.Append(" | swaps ").Append(SiteRenderer.SteadySwaps).Append(" new ").Append(SiteRenderer.RenderersMade).Append(" rooms switched ").Append(SiteRenderer.RoomsSwitched);
                SiteRenderer.SteadySwaps = SiteRenderer.RenderersMade = SiteRenderer.RoomsSwitched = 0;
            }
            b.Append(" | heap ").Append(GC.GetTotalMemory(false) >> 20).Append("MB");
            if (systemMemory.Valid || totalMemory.Valid || gfxMemory.Valid)
                b.Append(" mem ").Append(Mb(systemMemory)).Append('/').Append(Mb(totalMemory)).Append('/').Append(Mb(gfxMemory)).Append("MB");
            // the process's memory noted each time it passes another 256 MB: a climb towards the OS's limit shows in what happened
            if (systemMemory.Valid && systemMemory.LastValue >> 28 > memoryNoted) { memoryNoted = systemMemory.LastValue >> 28; Add($"memory in use passed {memoryNoted * 256:N0} MB (Unity's {Mb(totalMemory)} MB, graphics {Mb(gfxMemory)} MB)"); }
            return b.ToString();
        }

        /// <summary>Each site's settings, noted when it is first seen and when its occlusion changes.</summary>
        void Describe()
        {
            foreach (var s in StoreySite.AllSet)
            {
                var o = s.Occlusion;
                var cam = o != null && o.viewCamera != null ? o.viewCamera : Camera.main;
                var rt = cam != null ? cam.targetTexture : null;
                if (described.TryGetValue(s, out var seen) && seen.o == o && seen.cam == cam && seen.rt == rt) continue;
                described[s] = (o, cam, rt);
                var os = o != null ? o.settings : null;
                Add($"site {s.name}: layout {(s.layout != null ? s.layout.name : "none")}, {s.lodMode} LOD, quality {(s.quality != null ? s.quality.name : "defaults")}, " +
                    $"colliders {(s.generateColliders ? "on" : "off")}, occlusion {(o == null ? "none" : os == null ? "default settings" : $"{os.name} (in the way: {os.buildingsInTheWay}, cutaway {(os.cutaway ? "on" : "off")})")}" +
                    (o != null ? $", player {(o.player != null ? o.player.name : "not set")}, camera {(o.viewCamera != null ? o.viewCamera.name : "the main one")}" : "") +
                    // a memoryless attachment can't be stored for a partial render: with too much geometry in a pass the GPU
                    // throws the frame away instead (Metal's "too much geometry to support memoryless render pass attachments")
                    (cam == null ? "" : rt == null ? $"; {cam.name} draws to the screen" :
                        $"; {cam.name} draws into {rt.name} ({rt.width}×{rt.height}, depth {rt.depth} bits, MSAA {rt.antiAliasing}, memoryless {rt.memorylessMode})"));
            }
        }

        /// <summary>Every camera that renders, where to and with what: noted at first and when they or their targets change.</summary>
        void DescribeCameras()
        {
            if (cameras.Length < Camera.allCamerasCount) cameras = new Camera[Camera.allCamerasCount * 2];
            int n = Camera.GetAllCameras(cameras), h = n;
            for (int i = 0; i < n; i++)
            {
                var c = cameras[i]; var rt = c.targetTexture;
                h = (h * 31 + c.GetInstanceID()) * 31 + (rt != null ? rt.GetInstanceID() : 0);
                h = h * 31 + c.cullingMask;
            }
            if (camerasSeen == h) return;
            camerasSeen = h;
            var b = new StringBuilder(n == 1 ? "1 camera renders:" : $"{n} cameras render:");
            for (int i = 0; i < n; i++) b.Append("\n      ").Append(CameraLine(cameras[i]));
            Add(b.ToString());
        }

        static string CameraLine(Camera c)
        {
            var rt = c.targetTexture;
            var b = new StringBuilder();
            b.Append(c.name).Append(" (depth ").Append(F1(c.depth)).Append("): ")
                .Append(rt == null ? "draws to the screen" : $"draws into {rt.name} ({rt.width}×{rt.height}, depth {rt.depth} bits, MSAA {rt.antiAliasing}, memoryless {rt.memorylessMode})")
                .Append(", culling mask ").Append(c.cullingMask == -1 ? "everything" : "0x" + c.cullingMask.ToString("X8", Inv));
            // URP's per-camera settings, read by name as the pipeline asset's are: which decide whether its passes store
            // their depth or keep it memoryless (a depth texture copies it; without one and with post-processing it is a
            // transient attachment, memoryless where supported)
            var d = c.GetComponent("UniversalAdditionalCameraData");
            if (d != null)
                b.Append("; URP ").Append(Prop(d, "renderType")).Append(", renderer ").Append(Field(d, "m_RendererIndex") is int ri && ri >= 0 ? ri.ToString(Inv) : "default")
                    .Append(", post-processing ").Append(Prop(d, "renderPostProcessing")).Append(", depth texture ").Append(Prop(d, "requiresDepthTexture"))
                    .Append(", opaque texture ").Append(Prop(d, "requiresColorTexture")).Append(", anti-aliasing ").Append(Prop(d, "antialiasing"))
                    .Append(", shadows ").Append(Prop(d, "renderShadows"));
            return b.ToString();
        }

        // ---- the build threads ------------------------------------------------------------------------------------------

        /// <summary>The header's line on the build threads: how many this device gets, who decides, and their class on iOS.</summary>
        string Threads()
        {
            bool handheld = SystemInfo.deviceType == DeviceType.Handheld;
            var b = new StringBuilder("build threads: ");
            b.Append(buildThreads >= 0 ? $"{buildThreads} for this run (Storey Diagnostics' Build Threads)" : "the quality assets' count")
                .Append("; automatic on this device: ").Append(BuildThreads.Automatic(SystemInfo.processorCount, handheld))
                .Append(" (").Append(SystemInfo.processorCount).Append(handheld ? " cores, handheld)" : " cores)");
            b.Append(Application.platform == RuntimePlatform.IPhonePlayer
                ? (utilityThreads ? "; utility quality of service" : "; the quality of service they start with (Utility Threads off)")
                : "; below-normal priority (no quality of service off iOS)");
            return b.ToString();
        }

        /// <summary>
        /// The build threads' count noted when it changes, and every ten seconds while detail comes: how long it took from
        /// a build's start to its last part up, and how many buildings waited for a thread.
        /// </summary>
        void WatchThreads()
        {
            int n = BuildThreads.Shared.Count;
            if (n != threadsSeen)
            {
                if (threadsSeen >= 0 || n > 0) Add($"build threads: {n}{(StoreyThreads.Override >= 0 ? " (Storey Diagnostics' Build Threads)" : "")}");
                threadsSeen = n;
            }
            int waiting = 0;
            foreach (var s in StoreySite.AllSet) if (s.isActiveAndEnabled && s.LodStats is SiteRenderer.LodStats st) waiting += st.queued;
            waitSum += waiting; waitMost = Math.Max(waitMost, waiting); waitFrames++;
            foreach (var (which, ms) in SiteRenderer.DetailTimes) detailMs[which].Add(ms);
            SiteRenderer.DetailTimes.Clear();
            NoteDetail(false);
        }

        void NoteDetail(bool now)
        {
            double t = Time.realtimeSinceStartupAsDouble;
            if (!now && t - detailFrom < 10) return;
            if (detailMs[0].Count + detailMs[1].Count > 0)
            {
                var b = new StringBuilder($"detail in the last {F1(t - detailFrom)} s with {BuildThreads.Shared.Count} build threads:");
                for (int w = 0; w < 2; w++)
                {
                    var l = detailMs[w]; if (l.Count == 0) continue;
                    l.Sort();
                    b.Append(" LOD").Append(w).Append(' ').Append(l.Count).Append(" up, ").Append(F1(l[l.Count / 2])).Append(" ms from a build's start to its last mesh up (90th percentile ")
                        .Append(F1(l[(int)(l.Count * 0.9)])).Append(", most ").Append(F1(l[l.Count - 1])).Append(");");
                    l.Clear();
                }
                b.Append(" buildings waiting for a thread ").Append(F1(waitFrames > 0 ? (double)waitSum / waitFrames : 0)).Append(" on average, ").Append(waitMost).Append(" at most");
                Add(b.ToString());
            }
            detailFrom = t; waitSum = 0; waitMost = waitFrames = 0;
        }

        // ---- Metal's errors (iOS) --------------------------------------------------------------------------------------

#if UNITY_IOS
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int StoreyMetalErrors_Install();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern void StoreyMetalErrors_Frame(int frame);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern int StoreyMetalErrors_Take(byte[] buffer, int size);
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern long StoreyMetalErrors_Completed();
        [System.Runtime.InteropServices.DllImport("__Internal")] static extern long StoreyMetalErrors_Failed();
#endif

        /// <summary>The Metal error hook started (iOS players); the header's line on it.</summary>
        string WatchMetal()
        {
            metalWatched = false; metalFailed = 0;
#if UNITY_IOS
            metalTaken = 0; metalCheckAt = 0;
            // the editor with iOS as its build target compiles this too: the plug-in is in the iOS player only
            if (Application.platform != RuntimePlatform.IPhonePlayer) return "Metal errors: not watched (iOS players only)";
            if (!metalErrors) return "Metal errors: not watched (Metal Errors is off)";
            try { metalWatched = StoreyMetalErrors_Install() != 0; }
            catch (Exception e) { return "Metal errors: not watched (" + e.Message + ")"; }
            if (!metalWatched) return "Metal errors: not watched (no Metal device)";
            metalCheckAt = Time.frameCount + 300;
            return "Metal errors: watched (each failed command buffer, with its passes' states)";
#else
            return "Metal errors: not watched (iOS players only)";
#endif
        }

        /// <summary>The failed command buffers Metal has reported since the last frame, noted; main thread.</summary>
        void TakeMetal(int f)
        {
#if UNITY_IOS
            if (!metalWatched) return;
            StoreyMetalErrors_Frame(f);
            metalFailed = StoreyMetalErrors_Failed();
            metalText ??= new byte[16384];
            for (int i = 0; i < 4; i++)
            {
                int n = StoreyMetalErrors_Take(metalText, metalText.Length);
                if (n <= 0) break;
                Add(Encoding.UTF8.GetString(metalText, 0, n));
                if (metalTaken++ == 0) Incident("the first failed Metal command buffer");
            }
            // once, a few seconds in: that the hook sees the command buffers Unity makes (none: it watches nothing)
            if (metalCheckAt > 0 && f >= metalCheckAt)
            {
                metalCheckAt = 0;
                long done = StoreyMetalErrors_Completed();
                Add(done > 0 ? $"Metal: watching; {done:N0} command buffers completed so far, {metalFailed:N0} failed"
                    : "Metal: the hook has seen no command buffer complete: Unity's are made elsewhere, and a failure won't show here");
            }
#endif
        }

        static string Header()
        {
            var b = new StringBuilder();
            b.Append("Storey Diagnostics, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv)).Append('\n');
            b.Append(SystemInfo.deviceModel).Append(", ").Append(SystemInfo.operatingSystem).Append(", ").Append(SystemInfo.processorCount).Append(" cores, ")
                .Append(SystemInfo.systemMemorySize).Append(" MB\n");
            b.Append(SystemInfo.graphicsDeviceName).Append(", ").Append(SystemInfo.graphicsDeviceVersion).Append("; screen ").Append(Screen.width).Append('×').Append(Screen.height)
                .Append(", target frame rate ").Append(Application.targetFrameRate).Append('\n');
            int q = QualitySettings.GetQualityLevel(); var names = QualitySettings.names;
            var rp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            b.Append("Unity ").Append(Application.unityVersion).Append(", app ").Append(Application.version).Append("; quality ").Append(q >= 0 && q < names.Length ? names[q] : q.ToString(Inv))
                .Append(", render pipeline ").Append(rp != null ? rp.name : "none").Append("; Frame Timing Stats ").Append(FrameTimingManager.IsFeatureEnabled() ? "on" : "off (no CPU and GPU times)")
                .Append("; memoryless textures ").Append(SystemInfo.supportsMemorylessTextures ? "supported" : "not supported").Append('\n');
            // the pipeline asset's settings that weigh on the GPU, read by name: this assembly needs no URP types ("?": not
            // there, or stripped from the build)
            if (rp != null)
                b.Append("pipeline asset: render scale ").Append(Prop(rp, "renderScale")).Append(", MSAA ").Append(Prop(rp, "msaaSampleCount")).Append(", HDR ").Append(Prop(rp, "supportsHDR"))
                    .Append(", depth texture ").Append(Prop(rp, "supportsCameraDepthTexture")).Append(", opaque texture ").Append(Prop(rp, "supportsCameraOpaqueTexture"))
                    .Append(", GPU Resident Drawer ").Append(Prop(rp, "gpuResidentDrawerMode")).Append(", its occlusion culling ").Append(Prop(rp, "gpuResidentDrawerEnableOcclusionCullingInCameras")).Append('\n')
                    .Append("shadows: main light ").Append(Prop(rp, "supportsMainLightShadows")).Append(", ").Append(Prop(rp, "mainLightShadowmapResolution")).Append(" px, ").Append(Prop(rp, "shadowCascadeCount"))
                    .Append(" cascades to ").Append(Prop(rp, "shadowDistance")).Append(" m, soft ").Append(Prop(rp, "supportsSoftShadows")).Append('\n');
            if (rp != null) Renderers(b, rp);
            return b.ToString();
        }

        /// <summary>
        /// The pipeline asset's renderers: the settings that decide which attachments a camera's passes have, and so
        /// whether one is memoryless (Render Graph makes an attachment no later pass reads memoryless where supported).
        /// </summary>
        static void Renderers(StringBuilder b, object rp)
        {
            if (!(Field(rp, "m_RendererDataList") is Array list)) return;
            int def = Field(rp, "m_DefaultRendererIndex") is int d ? d : 0;
            for (int i = 0; i < list.Length; i++)
            {
                var r = list.GetValue(i); if (r == null) continue;
                b.Append("renderer ").Append(i).Append(i == def ? " (default) " : " ").Append(r is UnityEngine.Object ro ? ro.name : "?")
                    .Append(": ").Append(Prop(r, "renderingMode")).Append(", depth priming ").Append(Prop(r, "depthPrimingMode"))
                    .Append(", depth texture copied ").Append(Prop(r, "copyDepthMode")).Append(", intermediate texture ").Append(Prop(r, "intermediateTextureMode"))
                    .Append(", features:");
                int features = 0;
                if (Value(r, "rendererFeatures") is System.Collections.IList fs)
                    foreach (var f in fs)
                        if (f is UnityEngine.Object fo) { b.Append(features++ == 0 ? " " : ", ").Append(fo.name).Append(Prop(f, "isActive") == "True" ? "" : " (off)"); }
                b.Append(features == 0 ? " none\n" : "\n");
            }
        }

        static string Prop(object o, string name) => Convert.ToString(Value(o, name), Inv) is string v && v.Length > 0 ? v : "?";

        static object? Value(object o, string name)
        {
            try { return o.GetType().GetProperty(name)?.GetValue(o); }
            catch (Exception) { return null; }
        }

        /// <summary>A field by name, the private ones of the type and its bases too; null when there is none.</summary>
        static object? Field(object o, string name)
        {
            try
            {
                for (var t = o.GetType(); t != null; t = t.BaseType)
                    if (t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is FieldInfo f) return f.GetValue(o);
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>A note, stamped with the time and the main thread's frame; the same one again counts up instead.</summary>
        void Add(string what)
        {
            lock (gate)
            {
                if (what == lastRaw && notes.Count > 0) { repeats++; notes[notes.Count - 1] = lastNote + " (×" + repeats + ")"; dirty = true; return; }
                if (notes.Count >= 600) { notes.RemoveRange(40, 200); notes.Insert(40, "… (older notes dropped)"); }
                lastRaw = what; repeats = 1;
                lastNote = "[" + F2(Seconds(Stopwatch.GetTimestamp() - started)) + "s f" + Volatile.Read(ref frameNo) + "] " + what;
                notes.Add(lastNote);
                dirty = true;
            }
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;
            bool error = type == LogType.Exception || type == LogType.Error || type == LogType.Assert;
            Add(type + ": " + condition + (error && !string.IsNullOrEmpty(stackTrace) ? "\n      " + FirstLines(stackTrace, 4).Replace("\n", "\n      ") : ""));
        }

        static string FirstLines(string s, int n)
        {
            int at = 0;
            for (int i = 0; i < n && at >= 0; i++) { at = s.IndexOf('\n', at); if (at >= 0) at++; }
            return (at > 0 ? s.Substring(0, at) : s).TrimEnd();
        }

        // ---- the watchdog --------------------------------------------------------------------------------------------

        void Watch()
        {
            long lastSave = 0; double nextStall = 1, stalled = 0; bool gpuStuck = false;
            while (running)
            {
                Thread.Sleep(100);
                long now = Stopwatch.GetTimestamp();
                double since = paused ? 0 : Seconds(now - Volatile.Read(ref frameAt));
                int done = Volatile.Read(ref gpuDone);
                double gpuAgo = done >= 0 ? Seconds(now - Volatile.Read(ref gpuDoneAt)) : -1;
                if (since >= 1) stalled = Math.Max(stalled, since);
                if (since >= nextStall)
                {
                    // a stall: noted at a second, then each time it has gone on twice as long, and saved at once
                    nextStall *= 2;
                    string of = StoreyTimings.StageOf is string bn ? " (" + bn + ")" : "";
                    Add($"STALL: no frame for {F1(since)} s after frame {Volatile.Read(ref frameNo)}. The main thread is in {StoreyTimings.Stage ?? "none of the stages"}{of}. " +
                        (probe == null ? "No GPU heartbeat (no async readback here)." : done < 0 ? "The GPU has finished no frame yet." : $"The GPU last finished frame {done}, {F1(gpuAgo)} s ago."));
                    Save(); lastSave = now;
                }
                else if (since < 0.5 && stalled > 0)
                {
                    Add($"frames again, after a stall of {F1(stalled)} s");
                    stalled = 0; nextStall = 1;
                }
                // frames going on while the GPU finishes none: the GPU is stuck, or its work is being thrown away
                if (probe != null && !paused && since < 0.5 && done >= 0)
                {
                    if (!gpuStuck && gpuAgo > 2) { gpuStuck = true; Add($"GPU: no frame finished for {F1(gpuAgo)} s while the main thread went on to frame {Volatile.Read(ref frameNo)} (the GPU's last: {done})"); Save(); lastSave = now; }
                    else if (gpuStuck && gpuAgo < 0.5) { gpuStuck = false; Add("GPU: finishing frames again"); }
                }
                if (Seconds(now - lastSave) >= 2) { Save(); lastSave = now; }
            }
        }

        /// <summary>The record written to <see cref="FilePath"/> when anything changed (a temporary file first, so a kill mid-write keeps the one before).</summary>
        void Save()
        {
            string text;
            lock (gate)
            {
                if (!dirty) return;
                dirty = false;
                text = Text();
            }
            try
            {
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, text);
                try { if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path); }
                catch (Exception) { File.WriteAllText(path, text); }   // no replace here: written in place
            }
            catch (Exception) { lock (gate) dirty = true; }   // nothing to tell it to; the next save tries again
        }

        /// <summary>The frames in the ring now, kept as the frames before the first sign of trouble (the first only).</summary>
        void Incident(string what)
        {
            lock (gate)
            {
                if (incident != null) return;
                incident = new string[ringCount];
                for (int i = 0; i < ringCount; i++) incident[i] = ring[(ringNext - ringCount + i + ring.Length) % ring.Length];
                incidentWhat = what; dirty = true;
            }
        }

        string Text()
        {
            var b = new StringBuilder(header.Length + notes.Count * 120 + (ringCount + (incident?.Length ?? 0)) * 260);
            b.Append(header);
            b.Append("\nWhat happened:\n");
            foreach (var n in notes) b.Append(n).Append('\n');
            if (incident != null)
            {
                b.Append("\nThe ").Append(incident.Length).Append(" frames before ").Append(incidentWhat).Append(" (columns as below):\n");
                foreach (var l in incident) b.Append(l).Append('\n');
            }
            b.Append("\nThe last ").Append(ringCount).Append(" frames: frame, time, its length; with Frame Timing Stats the main thread's, the GPU's and the present wait, ms; gpu-N: frames since the last the GPU finished; mtl-fail N: Metal command buffers failed so far (iOS)")
                .Append(" | per site: LOD0/1/2 shown, LOD0 resident, generating, built and waiting for their upload, waiting for a thread, meshes uploaded this frame, LOD ms, v0: LOD0 vertices in view/shown, wall ids used/table")
                .Append(" | in: the player's building, floor, clip height, cut, its state row (LOD, from, fade, slot+1) | in the way: building, kN its storey at the player, sN its slot")
                .Append(" | feet, camera | swaps N new M: renderers given another material, and renderers made | managed heap; memory: the process's, Unity's, the graphics driver's\n");
            for (int i = 0; i < ringCount; i++) b.Append(ring[(ringNext - ringCount + i + ring.Length) % ring.Length]).Append('\n');
            return b.ToString();
        }

        // ---- the last run's record ------------------------------------------------------------------------------------

        /// <summary>The header and what happened (the start and the end of it, when long): what the screen shows of a record.</summary>
        static string Summary(string record)
        {
            int cut = record.IndexOf("\nThe ", StringComparison.Ordinal);   // the frames' sections; the notes are before them
            var lines = (cut >= 0 ? record.Substring(0, cut) : record).Split('\n');
            if (lines.Length <= 40) return string.Join("\n", lines);
            return string.Join("\n", lines, 0, 12) + "\n…\n" + string.Join("\n", lines, lines.Length - 26, 26);
        }

        void OnGUI()
        {
            if (!showing || lastRun == null) return;
            float u = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 400f);   // larger on a phone's dense screen
            textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = (int)(9 * u + 0.5f), wordWrap = true };
            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = (int)(12 * u + 0.5f) };
            var area = new Rect(10 * u, 10 * u, Screen.width - 20 * u, Screen.height - 20 * u);
            GUI.Box(area, "");
            GUI.Box(area, "");   // twice: the default box is see-through
            float bh = 34 * u, pad = 6 * u;
            GUI.Label(new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, area.height - bh - 3 * pad),
                "The last run's Storey Diagnostics record (Copy takes all of it, frames too):\n\n" + lastRunSummary, textStyle);
            float bw = (area.width - 3 * pad) / 2, by = area.yMax - bh - pad;
            if (GUI.Button(new Rect(area.x + pad, by, bw, bh), new GUIContent(copied.Length > 0 ? copied : "Copy the record"), buttonStyle))
            {
                GUIUtility.systemCopyBuffer = lastRun;
                copied = "Copied: " + lastRun.Length.ToString("N0", Inv) + " characters";
            }
            if (GUI.Button(new Rect(area.x + 2 * pad + bw, by, bw, bh), new GUIContent("Close"), buttonStyle)) showing = false;
        }
    }
}
