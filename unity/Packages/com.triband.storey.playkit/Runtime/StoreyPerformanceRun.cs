#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Unity;
using Unity.Profiling;
using UnityEngine;

namespace Triband.Storey.PlayKit
{
    /// <summary>
    /// A performance run for a device (docs/PERFORMANCE.md §14). Add it to any object in the scene. Once every site has
    /// loaded it takes the camera, and the player of the scene's occlusion system, along fixed paths and measures each:
    /// <list type="bullet">
    /// <item>an overview from high above and an orbit round the layout (no player: the occlusion's view left out);</item>
    /// <item>a walk along the layout's longest street with the camera trailing, as the play camera does;</item>
    /// <item>up the tallest building, a storey every two seconds;</item>
    /// <item>a teleport to the street's far end, and the seconds after it;</item>
    /// <item>a crossing into the nearest other site, when there is one.</item>
    /// </list>
    /// For each: the frame time's median, 95th and 99th percentiles and worst; with Frame Timing Stats on (Player Settings)
    /// the main thread's, the render thread's and the GPU's; draw calls, SetPass calls, batches and vertices; memory at its
    /// end; and in a development player the garbage a frame and the time in Storey's own stages. A path passes when its
    /// 99th percentile is under the quality asset's <see cref="StoreyQualitySettings.p99FrameMs"/>. The report is shown
    /// with a button that copies it, saved to <see cref="FilePath"/>, and noted in Storey Diagnostics' record. The play
    /// kit's character and camera are paused meanwhile, and put back after.
    /// </summary>
    [AddComponentMenu("Storey/Play Kit/Storey Performance Run")]
    [DisallowMultipleComponent]
    public sealed class StoreyPerformanceRun : MonoBehaviour
    {
        [Tooltip("Start once every site has loaded. Off: start it from the component's menu (Run) in Play mode, or from code (Run).")]
        public bool runOnStart = true;
        [Tooltip("The camera the run drives; the main one if empty.")]
        public Camera? viewCamera;
        [Tooltip("Seconds each path is measured for. The tallest building takes two a storey (30 storeys at most), the teleport eight.")]
        [Min(5)] public float secondsPerPath = 20;
        [Tooltip("Metres a second along the street, and twice that across districts.")]
        [Min(0.5f)] public float speed = 5;

        /// <summary>A run is going.</summary>
        public bool Running { get; private set; }
        /// <summary>The last run's report; null until one has finished.</summary>
        public string? Report { get; private set; }

        /// <summary>Where the report is saved (the app's persistent data folder, beside Storey Diagnostics' record).</summary>
        public static string FilePath => Path.Combine(Application.persistentDataPath, "StoreyPerformance.txt");

        // the play camera's trailing pose (StoreyPlayCamera's defaults): 13 m back at 0.78 rad up, looking at the chest
        const float Trail = 13f, Pitch = 0.78f, Chest = 1.5f;
        // Storey's own stages measured in a development player (StoreyTimings' markers)
        static readonly string[] Stages = { "site: refresh (all of the below)", "site: automatic LOD", "occlusion: frame", "occlusion: cutaway", "props: frame" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        sealed class PathStats
        {
            public string name = "", note = "";
            public readonly FrameSeries frame = new FrameSeries(), cpu = new FrameSeries(), render = new FrameSeries(), gpu = new FrameSeries();
            public readonly FrameSeries draws = new FrameSeries(), setPass = new FrameSeries(), batches = new FrameSeries(), verts = new FrameSeries(), gc = new FrameSeries();
            public readonly FrameSeries[] stages = new FrameSeries[Stages.Length];
            public long system, total, gfx;
            public PathStats() { for (int i = 0; i < stages.Length; i++) stages[i] = new FrameSeries(); }
        }

        readonly List<PathStats> paths = new List<PathStats>();
        PathStats? measuring;
        ProfilerRecorder draws, setPass, batches, verts, gcAlloc, system, total, gfx;
        readonly ProfilerRecorder[] stages = new ProfilerRecorder[Stages.Length];
        readonly FrameTiming[] timing = new FrameTiming[1];
        bool showing; string copied = ""; GUIStyle? textStyle, buttonStyle;

        void Start() { if (runOnStart) Run(); }

        /// <summary>Start a run (Play mode). It waits for the sites to load first.</summary>
        [ContextMenu("Run")]
        public void Run() { if (!Running && Application.isPlaying) StartCoroutine(Go()); }

        IEnumerator Go()
        {
            Running = true; showing = false; paths.Clear();
            var cam = viewCamera != null ? viewCamera : Camera.main;
            if (cam == null) { Debug.LogWarning("Storey Performance Run: no camera to drive."); Running = false; yield break; }
            // every site loaded, the walk model made, then two seconds for the first view's detail
            float waited = Time.unscaledTime;
            while (Time.unscaledTime - waited < 120 && !Loaded()) yield return null;
            for (float t0 = Time.unscaledTime; Time.unscaledTime - t0 < 2;) yield return null;

            var occ = OcclusionOf();
            var home = occ != null && occ.site != null ? occ.site : First();
            var shown = home != null ? home.Renderer?.Site : null;
            if (home == null || shown == null) { Debug.LogWarning("Storey Performance Run: no site to run over."); Running = false; yield break; }

            // paused: the play kit's character and camera, which would move them back; put back after (Restore)
            paused.Clear();
            foreach (var c in FindObjectsByType<StoreyCharacter>(FindObjectsSortMode.None)) if (c.enabled) { c.enabled = false; paused.Add(c); }
            foreach (var c in FindObjectsByType<StoreyPlayCamera>(FindObjectsSortMode.None)) if (c.enabled) { c.enabled = false; paused.Add(c); }
            driven = cam; camAt = cam.transform.position; camTurn = cam.transform.rotation;
            occAt = occ; feetAt = occ != null && occ.player != null ? occ.player.position : (Vector3?)null;
            Record(true);
            try
            {
                var o = home.transform.position;
                var box = Box(); var mid = new Vector3((box.x0 + box.x1) / 2, o.y, (box.z0 + box.z1) / 2); float extent = Mathf.Max(box.x1 - box.x0, box.z1 - box.z0);
                var street = PerformancePaths.Street(shown);
                var tall = PerformancePaths.Tallest(shown);

                // from high above, and round, with no player: the occlusion unhooked from the sites (its walk models kept), as
                // no play camera goes there
                Unhook(occ);
                yield return Measure("overview", secondsPerPath, t =>
                {
                    float a = t / secondsPerPath * 2 * Mathf.PI;
                    return (mid + new Vector3(Mathf.Sin(a) * 0.4f * extent, 0.6f * extent + 60, Mathf.Cos(a) * 0.4f * extent), mid, null);
                });
                yield return Measure("orbit", secondsPerPath, t =>
                {
                    float a = t / secondsPerPath * 2 * Mathf.PI, r = 0.25f * extent + 60;
                    return (mid + new Vector3(Mathf.Sin(a) * r, 35, Mathf.Cos(a) * r), mid, null);
                });
                Rehook();

                // along the street, the camera trailing; back the other way at its end
                Vector3 s0 = mid, s1 = mid + Vector3.right;
                if (street.HasValue) { var (x0, z0, x1, z1) = street.Value; s0 = o + new Vector3((float)x0, 0, (float)z0); s1 = o + new Vector3((float)x1, 0, (float)z1); }
                float len = Mathf.Max(1, Vector3.Distance(s0, s1)); Vector3 feet = s0, dir = (s1 - s0) / len;
                yield return Measure("street walk", secondsPerPath, t =>
                {
                    float d = Mathf.PingPong(t * speed, len); var f = s0 + (s1 - s0) * (d / len);
                    var way = Mathf.Repeat(t * speed, 2 * len) < len ? dir : -dir;
                    feet = f;
                    return Trailing(f, way);
                }, street == null ? "no street found: from the middle" : $"{len:0} m of street at {speed:0.#} m/s");

                // up the tallest building, a storey every two seconds, the camera outside towards the street
                if (tall != null)
                {
                    int n = tall.floors.Count, steps = Math.Min(n, 30);
                    var tb = shown.Bounds(tall); var tc = o + new Vector3((float)(tb.x0 + tb.x1) / 2, 0, (float)(tb.z0 + tb.z1) / 2);
                    var outward = (s0 + s1) / 2 - tc; outward.y = 0; outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.back;
                    yield return Measure("up the tallest building", 2f * steps, t =>
                    {
                        int i = Mathf.Min(steps - 1, (int)(t / 2)), k = steps > 1 ? Mathf.RoundToInt(i * (n - 1) / (float)(steps - 1)) : 0;
                        var (x, y, z) = PerformancePaths.StandOn(tall, k);
                        feet = o + new Vector3((float)x, (float)y, (float)z);
                        return Trailing(feet, -outward);
                    }, $"{tall.name}: {n} storeys, {steps} of them visited");
                }

                // a teleport to the street's far end: the frames after the jump, nothing asked for ahead
                var far = Vector3.Distance(feet, s0) > Vector3.Distance(feet, s1) ? s0 : s1;
                yield return Measure("teleport", 8, t => Trailing(far, (s1 - s0).normalized), "to the street's far end, at once");
                feet = far;

                // into the nearest other site, when there is one
                StoreySite? other = null; float best = float.MaxValue;
                foreach (var s in StoreySite.AllSet)
                {
                    if (s == home || !s.isActiveAndEnabled || s.Renderer?.Site == null) continue;
                    float d = Vector3.Distance(Middle(s), feet); if (d < best) { best = d; other = s; }
                }
                if (other != null)
                {
                    var from = feet; var to = Middle(other); to.y = from.y; float far2 = Mathf.Max(1, Vector3.Distance(from, to)); var way = (to - from) / far2;
                    yield return Measure("district crossing", Mathf.Min(secondsPerPath, far2 / (2 * speed) + 2), t => Trailing(from + way * Mathf.Min(far2, t * 2 * speed), way), $"to {other.name}, {far2:0} m at {2 * speed:0.#} m/s");
                }
                Finish(home, other == null ? "one site: no district crossing" : "");
            }
            finally { Restore(); }
        }

        // what a run changed, to put back when it ends or the component is switched off mid-run
        readonly List<Behaviour> paused = new List<Behaviour>();
        readonly List<StoreySite> unhooked = new List<StoreySite>();
        Camera? driven; Vector3 camAt; Quaternion camTurn;
        StoreyOcclusion? occAt; Vector3? feetAt;
        bool recording;

        // the sites the occlusion draws for, drawn with the neutral view for a while
        void Unhook(StoreyOcclusion? occ)
        {
            if (occ == null) return;
            foreach (var s in StoreySite.AllSet) if (s.Occlusion == occ) { s.Occlusion = null; unhooked.Add(s); }
        }

        void Rehook()
        {
            foreach (var s in unhooked) if (s != null && occAt != null && occAt.isActiveAndEnabled && occAt.site == s) s.Occlusion = occAt;
            unhooked.Clear();
        }

        void Restore()
        {
            if (!Running) return;
            Record(false); measuring = null;
            Rehook();
            if (occAt != null) { occAt.SetCameraTarget(null); if (feetAt is Vector3 f && occAt.player != null) occAt.player.position = f; }
            if (driven != null) driven.transform.SetPositionAndRotation(camAt, camTurn);
            foreach (var b in paused) if (b != null) b.enabled = true;
            paused.Clear(); driven = null; occAt = null;
            Running = false;
        }

        void OnDisable() => Restore();

        /// <summary>Every site loaded and none loading, and the occlusion's walk model made (if there is one).</summary>
        bool Loaded()
        {
            int n = 0;
            foreach (var s in StoreySite.AllSet)
            {
                if (!s.isActiveAndEnabled) continue;
                n++;
                if (s.Loading || s.Renderer?.Site == null) return false;
            }
            var occ = OcclusionOf();
            return n > 0 && (occ == null || !occ.enabled || occ.World != null);
        }

        static StoreyOcclusion? OcclusionOf()
        {
            foreach (var o in FindObjectsByType<StoreyOcclusion>(FindObjectsSortMode.None)) if (o.enabled && o.player != null) return o;
            foreach (var o in FindObjectsByType<StoreyOcclusion>(FindObjectsSortMode.None)) if (o.enabled) return o;
            return null;
        }

        static StoreySite? First() { foreach (var s in StoreySite.AllSet) if (s.isActiveAndEnabled) return s; return null; }

        /// <summary>Every site's own buildings' box, in world space.</summary>
        static (float x0, float z0, float x1, float z1) Box()
        {
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var s in StoreySite.AllSet)
            {
                var site = s.isActiveAndEnabled ? s.Renderer?.Site : null; if (site == null) continue;
                var o = s.transform.position;
                for (int i = 0; i < site.Own; i++)
                {
                    var b = site.Bounds(site.Buildings[i]);
                    x0 = Mathf.Min(x0, (float)b.x0 + o.x); z0 = Mathf.Min(z0, (float)b.z0 + o.z); x1 = Mathf.Max(x1, (float)b.x1 + o.x); z1 = Mathf.Max(z1, (float)b.z1 + o.z);
                }
            }
            return x0 <= x1 ? (x0, z0, x1, z1) : (0, 0, 0, 0);
        }

        static Vector3 Middle(StoreySite s)
        {
            var site = s.Renderer!.Site!; var o = s.transform.position;
            double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < site.Own; i++) { var b = site.Bounds(site.Buildings[i]); x0 = Math.Min(x0, b.x0); z0 = Math.Min(z0, b.z0); x1 = Math.Max(x1, b.x1); z1 = Math.Max(z1, b.z1); }
            return site.Own == 0 ? o : o + new Vector3((float)(x0 + x1) / 2, 0, (float)(z0 + z1) / 2);
        }

        /// <summary>The play camera's pose behind a player at <paramref name="feet"/> walking along <paramref name="way"/>.</summary>
        static (Vector3 cam, Vector3 look, Vector3? feet) Trailing(Vector3 feet, Vector3 way)
        {
            way.y = 0; way = way.sqrMagnitude > 1e-6f ? way.normalized : Vector3.forward;
            var look = feet + Vector3.up * Chest;
            return (look - way * (Trail * Mathf.Cos(Pitch)) + Vector3.up * (Trail * Mathf.Sin(Pitch)), look, feet);
        }

        /// <summary>
        /// One path: each frame the camera and the player where <paramref name="at"/> says, for <paramref name="seconds"/>,
        /// and each frame's numbers kept (a frame's are known the frame after: the first frame on a path isn't counted).
        /// </summary>
        IEnumerator Measure(string name, float seconds, Func<float, (Vector3 cam, Vector3 look, Vector3? feet)> at, string note = "")
        {
            var stats = new PathStats { name = name, note = note }; paths.Add(stats);
            var cam = viewCamera != null ? viewCamera : Camera.main; var occ = OcclusionOf();
            if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note($"performance run: {name}{(note.Length > 0 ? " (" + note + ")" : "")}");
            measuring = null;
            for (float t0 = Time.unscaledTime; Time.unscaledTime - t0 < seconds;)
            {
                var (c, look, feet) = at(Time.unscaledTime - t0);
                if (cam != null) { cam.transform.position = c; cam.transform.LookAt(look); }
                if (occ != null && occ.enabled && feet is Vector3 f)
                {
                    if (occ.player != null) occ.player.position = f; else occ.SetPlayer(f);
                    occ.SetCameraTarget(look);
                }
                yield return null;
                measuring = stats;   // from the next frame: this one's numbers come then
            }
            measuring = null;
            stats.system = Last(system); stats.total = Last(total); stats.gfx = Last(gfx);
        }

        // each frame while a path is measured: the frame before's numbers
        void Update()
        {
            var p = measuring; if (p == null) return;
            p.frame.Add(Time.unscaledDeltaTime * 1000.0);
            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                {
                    if (timing[0].cpuMainThreadFrameTime > 0) p.cpu.Add(timing[0].cpuMainThreadFrameTime);
                    if (timing[0].cpuRenderThreadFrameTime > 0) p.render.Add(timing[0].cpuRenderThreadFrameTime);
                    if (timing[0].gpuFrameTime > 0) p.gpu.Add(timing[0].gpuFrameTime);
                }
            }
            if (draws.Valid) p.draws.Add(draws.LastValue);
            if (setPass.Valid) p.setPass.Add(setPass.LastValue);
            if (batches.Valid) p.batches.Add(batches.LastValue);
            if (verts.Valid) p.verts.Add(verts.LastValue);
            if (gcAlloc.Valid) p.gc.Add(gcAlloc.LastValue);
            for (int i = 0; i < stages.Length; i++) if (stages[i].Valid) p.stages[i].Add(stages[i].LastValue / 1e6);
        }

        static long Last(ProfilerRecorder r) => r.Valid ? r.LastValue : -1;

        void Record(bool on)
        {
            if (on == recording) return;
            recording = on;
            if (on)
            {
                draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
                setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
                gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                system = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
                total = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
                gfx = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
                for (int i = 0; i < stages.Length; i++) stages[i] = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Storey " + Stages[i]);
                return;
            }
            draws.Dispose(); setPass.Dispose(); batches.Dispose(); verts.Dispose(); gcAlloc.Dispose(); system.Dispose(); total.Dispose(); gfx.Dispose();
            for (int i = 0; i < stages.Length; i++) stages[i].Dispose();
        }

        void Finish(StoreySite home, string note)
        {
            var q = home.quality;
            float limit = q != null ? q.p99FrameMs : 20f;
            int passed = 0;
            foreach (var p in paths) if (p.frame.Count > 0 && p.frame.Percentile(99) <= limit) passed++;
            var b = new StringBuilder();
            b.Append("Storey Performance Run, ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv)).Append('\n')
                .Append(SystemInfo.deviceModel).Append(", ").Append(SystemInfo.operatingSystem).Append(", ").Append(SystemInfo.graphicsDeviceName)
                .Append("; screen ").Append(Screen.width).Append('×').Append(Screen.height).Append(", target frame rate ").Append(Application.targetFrameRate).Append('\n')
                .Append("quality ").Append(q != null ? q.name : "none (the defaults)").Append(": a path passes with 99 frames in a hundred under ").Append(F1(limit)).Append(" ms; ")
                .Append(passed).Append(" of ").Append(paths.Count).Append(" passed").Append(Debug.isDebugBuild ? " (a development player: Storey's stages and garbage measured)" : "").Append('\n');
            if (note.Length > 0) b.Append(note).Append('\n');
            foreach (var p in paths)
            {
                var f = p.frame;
                b.Append('\n').Append(p.name).Append(p.note.Length > 0 ? " (" + p.note + ")" : "").Append(", ").Append(f.Count).Append(" frames: ")
                    .Append(f.Count > 0 && f.Percentile(99) <= limit ? "PASS" : "FAIL").Append('\n')
                    .Append("  frame ").Append(Ms(f)).Append(" ms (median / 95th / 99th / worst)\n");
                if (p.cpu.Count > 0) b.Append("  main thread ").Append(Ms(p.cpu)).Append(", render thread ").Append(Ms(p.render)).Append(", GPU ").Append(Ms(p.gpu)).Append('\n');
                if (p.draws.Count > 0)
                    b.Append("  draw calls ").Append(N(p.draws)).Append(", SetPass ").Append(N(p.setPass)).Append(", batches ").Append(N(p.batches))
                        .Append(", vertices ").Append(N(p.verts, 1000)).Append("k (median / most)\n");
                if (p.gc.Count > 0) b.Append("  garbage a frame ").Append(N(p.gc, 1024)).Append(" KB (median / most)\n");
                var st = new StringBuilder();
                for (int i = 0; i < Stages.Length; i++)
                    if (p.stages[i].Count > 0) st.Append(st.Length > 0 ? ", " : "").Append(Stages[i]).Append(' ').Append(F2(p.stages[i].Percentile(99))).Append(" ms");
                if (st.Length > 0) b.Append("  Storey's stages, 99th percentile: ").Append(st).Append('\n');
                if (p.system >= 0 || p.total >= 0)
                    b.Append("  memory at its end: the process's ").Append(Mb(p.system)).Append(" MB, Unity's ").Append(Mb(p.total)).Append(" MB, graphics ").Append(Mb(p.gfx)).Append(" MB\n");
            }
            Report = b.ToString();
            try { File.WriteAllText(FilePath, Report); } catch (Exception e) { Debug.LogWarning("Storey Performance Run: the report couldn't be saved: " + e.Message); }
            Debug.Log(Report);
            if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note(Report.TrimEnd('\n'));
            showing = true; copied = "";
        }

        static string F1(double x) => x.ToString("0.0", Inv);
        static string F2(double x) => x.ToString("0.00", Inv);
        static string Ms(FrameSeries s) => s.Count == 0 ? "?" : $"{F1(s.Percentile(50))} / {F1(s.Percentile(95))} / {F1(s.Percentile(99))} / {F1(s.Max)}";
        static string N(FrameSeries s, double unit = 1) => s.Count == 0 ? "?" : $"{(s.Percentile(50) / unit).ToString("N0", Inv)} / {(s.Max / unit).ToString("N0", Inv)}";
        static string Mb(long bytes) => bytes < 0 ? "?" : (bytes >> 20).ToString(Inv);

        void OnGUI()
        {
            if (!showing || Report == null) return;
            float u = Mathf.Max(1f, Mathf.Min(Screen.width, Screen.height) / 400f);   // larger on a phone's dense screen
            textStyle ??= new GUIStyle(GUI.skin.label) { fontSize = (int)(8 * u + 0.5f), wordWrap = true };
            buttonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = (int)(12 * u + 0.5f) };
            var area = new Rect(10 * u, 10 * u, Screen.width - 20 * u, Screen.height - 20 * u);
            GUI.Box(area, ""); GUI.Box(area, "");   // twice: the default box is see-through
            float bh = 34 * u, pad = 6 * u;
            GUI.Label(new Rect(area.x + pad, area.y + pad, area.width - 2 * pad, area.height - bh - 3 * pad), Report, textStyle);
            float bw = (area.width - 3 * pad) / 2, by = area.yMax - bh - pad;
            if (GUI.Button(new Rect(area.x + pad, by, bw, bh), new GUIContent(copied.Length > 0 ? copied : "Copy the report"), buttonStyle))
            {
                GUIUtility.systemCopyBuffer = Report;
                copied = "Copied: " + Report.Length.ToString("N0", Inv) + " characters";
            }
            if (GUI.Button(new Rect(area.x + 2 * pad + bw, by, bw, bh), new GUIContent("Close"), buttonStyle)) showing = false;
        }
    }
}
