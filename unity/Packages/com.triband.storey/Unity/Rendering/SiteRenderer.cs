#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Triband.Storey.Play;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Draws a layout through one building table, with one colour row per style. Rebuilds single buildings after an edit
    /// (docs/EDITOR.md §2), keeping the other buildings' meshes and colour rows. Shared by the parity harness and
    /// <see cref="StoreySite"/>; works in edit mode.
    ///
    /// Two ways to pick the LOD. Fixed (the parity harness): every LOD of every building is built and all show
    /// <see cref="Lod"/>. Automatic (docs/CITY.md §2, §3): every building's massing is built and merged into 128 m cells,
    /// and LOD0 and LOD1 are built as the camera nears (<see cref="Eye"/>), a few milliseconds a frame, and dropped again
    /// past the resident caps; <see cref="Forced"/> buildings always have LOD0.
    /// </summary>
    public sealed class SiteRenderer : IDisposable
    {
        readonly Transform parent;
        readonly Material opaque, glass, massing;
        // the opaque and massing materials' steady copies (STOREY_STEADY on: no discard), null when the shader has no such variant
        readonly Material? opaqueSteady, massingSteady;
        readonly HideFlags flags;
        readonly BuildingTable table = BuildingTable.Acquire();   // shared by every site (docs/CITY.md §5)
        int blockStart = -1, blockSize;   // this site's building indices there: the layout's buildings in order
        readonly IStoreyPalette palette = StoreyPalettes.Active;
        readonly Dictionary<string, Built> built = new Dictionary<string, Built>(StringComparer.Ordinal);
        readonly Dictionary<int, (string[] original, string[] overwrite)> remaps = new Dictionary<int, (string[], string[])>();
        ColorRowBook? book;
        Site? site;
        int lod, shownLod = -1;

        sealed class Built
        {
            public string id = "";
            public int idx, wallBase = -1, wallCount;
            // its LOD0's vertices, opaque and glass (0 without one): how much geometry it puts in a pass; and its rooms'
            public long lod0Verts, roomsVerts;
            // while LOD0 is built: what is read of it after upload (collision segments, the wall list), not the generator's result
            public Lod0Shape? shape;
            public readonly List<int> rows = new List<int>();
            // what draws its LODs, made with the first and let go with the last (docs/PERFORMANCE.md §3.4): most buildings
            // never have one, and its lists were most of the 900 bytes each building's record took
            public Parts? parts;
            // the buildings whose party walls its LOD0's meshes carry
            public int[] mates = Array.Empty<int>();
            // its own object, made with its first collider (docs/PERFORMANCE.md §3.4)
            public GameObject? root;
            public Mesh? collision;
            // its collision mesh, made on the worker with its first LOD0, waiting for the colliders' turn in the frame
            public CollisionMesh? collisionMade;
            // automatic LOD: the massing until its cell is merged (made again if the cell is merged again), its rows'
            // places in the table, and the cell it is drawn in
            public Lod2Mesh? l2;
            public int[]? rowMap;
            public bool inCell;
            public (int x, int z) cell;
        }

        /// <summary>A building's meshes and the objects drawing them, per LOD (index 2: the fixed LOD's massing).</summary>
        sealed class Parts
        {
            // per LOD: the meshes and the objects drawing them
            public readonly List<Mesh>[] meshes = { new List<Mesh>(), new List<Mesh>(), new List<Mesh>() };
            public readonly List<PartObject>[] objs = { new List<PartObject>(), new List<PartObject>(), new List<PartObject>() };
            // per LOD: the renderers with the opaque or massing material, and whether they have its steady copy now
            public readonly List<MeshRenderer>[] solid = { new List<MeshRenderer>(), new List<MeshRenderer>(), new List<MeshRenderer>() };
            public readonly bool[] steady = new bool[3];
            // per LOD: every renderer, and whether they draw (automatic LOD: a LOD0 or LOD1 kept for later is switched off,
            // see Draws)
            public readonly List<MeshRenderer>[] rends = { new List<MeshRenderer>(), new List<MeshRenderer>(), new List<MeshRenderer>() };
            public readonly bool[] drawn = { true, true, true };
            // LOD0's rooms (docs/PERFORMANCE.md §7.7): their renderers, drawn with the LOD0 while a window can show them
            // (RoomsDraw), casting shadows for the building the player is in; whether they draw and cast now, and whether
            // its nearest point is in the windows' clear distance
            public readonly List<MeshRenderer> rooms = new List<MeshRenderer>();
            public bool roomsOn = true, roomsCast, near = true;
            public bool Empty => objs[0].Count == 0 && objs[1].Count == 0 && objs[2].Count == 0;
        }

        /// <summary>
        /// An object drawing one detail mesh, with its filter and renderer. Kept when its mesh goes, switched off with none,
        /// and given the next build's (docs/PERFORMANCE.md §3.4): each build made a GameObject and two components per mesh
        /// under its building's object, and each drop destroyed them. They sit under one object of the site's, never moved.
        /// </summary>
        sealed class PartObject { public GameObject go = null!; public MeshFilter filter = null!; public MeshRenderer renderer = null!; }
        readonly Stack<PartObject> spareObjects = new Stack<PartObject>();
        GameObject? detailRoot;
        /// <summary>The most objects kept for the next builds; more are destroyed.</summary>
        const int SpareObjects = 256;
        /// <summary>Detail objects given a new mesh rather than made, since <see cref="StoreyDiagnostics"/> last read it (main thread).</summary>
        internal static int RenderersReused;

        // names for the objects and meshes made: with the building's in the editor and development builds, where the
        // hierarchy and the Profiler show them; a fixed word in a release player, where each was a string built per mesh
        // (docs/PERFORMANCE.md §3.4)
        static readonly bool named;
        static string Named(string building, string what, int k = 0, int n = 1) => named ? Part(building + " " + what, k, n) : what;

        // Storey's build threads show in the Profiler's timeline under their own names, with the markers below, and end
        // when the player quits
        static SiteRenderer()
        {
            named = Application.isEditor || Debug.isDebugBuild;
            BuildThreads.Started = name => UnityEngine.Profiling.Profiler.BeginThreadProfiling("Storey", name);
            BuildThreads.Ended = UnityEngine.Profiling.Profiler.EndThreadProfiling;
            // iOS: each build thread asks for the utility quality-of-service class (docs/PERFORMANCE.md §9.1)
            StoreyThreads.Init();
            BuildThreads.Configure = StoreyThreads.Configure;
            Application.quitting += () => BuildThreads.Shared.Resize(0);
            // the steady variant is picked just before the cameras render: every site's frame has written the table and the view
            UnityEngine.Rendering.RenderPipelineManager.beginContextRendering += (context, cameras) => { foreach (var s in live) s.UpdateSteady(); };
        }

        static readonly global::Unity.Profiling.ProfilerMarker markLod0 = new global::Unity.Profiling.ProfilerMarker("Storey.Generate LOD0"),
            markLod1 = new global::Unity.Profiling.ProfilerMarker("Storey.Generate LOD1"), markPack = new global::Unity.Profiling.ProfilerMarker("Storey.Pack"),
            markCollision = new global::Unity.Profiling.ProfilerMarker("Storey.Collision mesh");

        /// <param name="flags">Edit mode passes <c>DontSave</c>: the meshes are a preview of the layout, never part of the scene file.</param>
        /// <param name="auto">The automatic LOD's settings; null for the fixed LOD (every LOD built, <see cref="Lod"/> shown).</param>
        public SiteRenderer(Transform parent, Material opaque, Material glass, Material massing, HideFlags flags = HideFlags.None, LodSettings? auto = null)
        {
            this.parent = parent; layer = parent.gameObject.layer; this.opaque = opaque; this.glass = glass; this.massing = massing; this.flags = flags;
            if (auto != null) lods = new LodManager(auto);
            opaqueSteady = SteadyCopy(opaque); massingSteady = SteadyCopy(massing);
        }

        /// <summary>A copy of <paramref name="m"/> drawing with the steady variant; null when its shader has none (a project's own).</summary>
        static Material? SteadyCopy(Material m)
        {
            if (m.shader == null || !m.shader.keywordSpace.FindKeyword("STOREY_STEADY").isValid) return null;
            var c = new Material(m) { name = m.name + " (steady)", hideFlags = HideFlags.HideAndDontSave };
            Follow(c, m);
            return c;
        }

        /// <summary>
        /// Steady copy <paramref name="c"/> made like its material <paramref name="m"/> again: its shader, properties and
        /// keywords, then the steady variant, in the opaque queue. The keyword is looked up again each time, in the shader
        /// the copy has now: a shader reimported in the editor, or another picked for the material, has keywords of its own,
        /// and Unity refuses one from before ("comes from a different shader").
        /// </summary>
        static void Follow(Material c, Material m)
        {
            if (m.shader != null && c.shader != m.shader) c.shader = m.shader;
            c.CopyMatchingPropertiesFromMaterial(m);
            var kw = c.shader != null ? c.shader.keywordSpace.FindKeyword("STOREY_STEADY") : default;
            if (kw.isValid) c.SetKeyword(kw, true);
            // the shaders sit in the AlphaTest queue for their discarding variant; the steady copy draws with the opaque
            // geometry before it (docs/PERFORMANCE.md §7.6)
            c.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        }

        int steadySynced = -1;

        /// <summary>
        /// The steady copies cull back faces (docs/PERFORMANCE.md §7.1; the quality asset's Cull Back Faces When Steady, on by
        /// default): about half of what most of the city draws never reaches the tiler. Every one-sided surface the prototype
        /// draws two-sided has its back (MeshBuilder.Backing), so none is open from outside, which the headless ray test
        /// (BackFaceTests) checks. Off, every face is drawn, as on the materials themselves, whose section cap shows a cut
        /// wall's inside.
        /// </summary>
        public bool CullSteadyBackFaces { get; set; }
        bool? steadyCulled;   // what the copies have, noted by Storey Diagnostics when it changes

        /// <summary>Renderers given their steady copy or their own material again, and renderers made, since <see cref="StoreyDiagnostics"/> last read them (main thread).</summary>
        internal static int SteadySwaps, RenderersMade;

        /// <summary>
        /// Each building's and cell's opaque and massing renderers on the steady copy of their material while nothing can
        /// hide their pixels (<see cref="global::Triband.Storey.Occlusion.Steady"/>), on the material itself otherwise: no
        /// discard for most of the city, so tile-based GPUs treat it as opaque. Run before the cameras render.
        /// </summary>
        void UpdateSteady()
        {
            bool copies = opaqueSteady != null || massingSteady != null;
            if (!copies && roomsKept == 0) return;
            using var _t = StoreyTimings.Time("site: steady variants (before the cameras)");
            // the copies follow their materials' properties: always in edit mode, twice a second in Play mode
            bool synced = false;
            if (copies && (!Application.isPlaying || Time.frameCount - steadySynced >= 30))
            {
                steadySynced = Time.frameCount; synced = true;
                if (opaqueSteady != null) Follow(opaqueSteady, opaque);
                if (massingSteady != null) Follow(massingSteady, massing);
            }
            // their cull mode, set again after the sync, which copies the material's (every face)
            if (copies && (synced || steadyCulled != CullSteadyBackFaces))
            {
                float cull = (float)(CullSteadyBackFaces ? UnityEngine.Rendering.CullMode.Back : UnityEngine.Rendering.CullMode.Off);
                if (opaqueSteady != null) opaqueSteady.SetFloat(StoreyShaderIds.Cull, cull);
                if (massingSteady != null) massingSteady.SetFloat(StoreyShaderIds.Cull, cull);
                if (steadyCulled != CullSteadyBackFaces && StoreyDiagnostics.Recording)
                {
                    // a shader without the property (a Color Palette Lit sample imported before it had one) draws every face whatever this says
                    var without = opaqueSteady != null && !opaqueSteady.HasProperty(StoreyShaderIds.Cull) ? opaque.shader.name : massingSteady != null && !massingSteady.HasProperty(StoreyShaderIds.Cull) ? massing.shader.name : null;
                    StoreyDiagnostics.Note($"back faces of steady buildings: {(CullSteadyBackFaces ? "culled" : "drawn")}" +
                        (CullSteadyBackFaces && without != null ? $", but not by {without}: it has no _StoreyCull (re-import the Color Palette Lit sample)" : ""));
                }
                steadyCulled = CullSteadyBackFaces;
            }
            int active = StoreyGlobals.ActiveIndex, isolated = StoreyGlobals.IsolatedIndex; float iso = StoreyGlobals.IsolateAmount;
            bool farOcc = lods?.Settings.FarOcclusion ?? true, isolating = global::Triband.Storey.Occlusion.Steady.Isolating(iso);
            // what every building's answer goes by: when it changed, every building and cell is looked at again
            var with = (active, isolating, isolating ? isolated : 0, farOcc, roomsHide);
            if (with != steadyWith) { steadyWith = with; steadyAll = true; }
            // the buildings whose rows changed since, as the table marked them
            if (blockStart >= 0)
            {
                table.SteadyChanged.Take(blockStart, blockSize, rowsTaken);
                foreach (int idx in rowsTaken) steadyDirty.Add(idx);
                rowsTaken.Clear();
            }
            // while Storey Diagnostics records, every building and cell once a second: one that changes though nothing marked
            // it is a change the marks missed, noted
            bool check = !steadyAll && StoreyDiagnostics.Recording && Time.frameCount - steadyChecked >= 60, all = steadyAll || check;
            if (check) steadyChecked = Time.frameCount;
            var state = table.State; int missed = 0;
            foreach (int idx in steadyDirty)
                if (byIdx.TryGetValue(idx, out var bt))
                {
                    if (!all) SteadyOf(bt, state, active, iso, isolated, farOcc);
                    if (bt.inCell) steadyCells.Add(bt.cell);
                }
            if (all) foreach (var bt in built.Values) if (SteadyOf(bt, state, active, iso, isolated, farOcc) && check && !steadyDirty.Contains(bt.idx)) missed++;
            if (massingSteady != null)
            {
                if (all) { foreach (var kv in cells) if (SteadyOf(kv.Value, state, active, iso, isolated, farOcc) && check && !steadyCells.Contains(kv.Key)) missed++; }
                else foreach (var key in steadyCells) if (cells.TryGetValue(key, out var c)) SteadyOf(c, state, active, iso, isolated, farOcc);
            }
            if (missed > 0) StoreyDiagnostics.Note($"steady variants: {missed} buildings or cells were out of date, a change nothing marked (docs/PERFORMANCE.md §3.3)");
            steadyDirty.Clear(); steadyCells.Clear(); steadyAll = false;
        }

        // what the steady variant and the cells' visibility go by that changed since they were last looked at
        // (docs/PERFORMANCE.md §3.3), where every building and cell was looked at before every render context, and every
        // cell's members walked every frame: buildings by table index (their rows as the table marks them, their LOD0's
        // mates, renderers made, their rooms' distance or forcing), cells by key (their members, their meshes). Everything
        // again when a view global or setting they all go by changes. A second render context in a frame finds nothing marked
        readonly HashSet<int> steadyDirty = new HashSet<int>();
        readonly HashSet<(int x, int z)> steadyCells = new HashSet<(int, int)>(), shownCells = new HashSet<(int, int)>();
        readonly List<int> rowsTaken = new List<int>();
        bool steadyAll = true, shownAll = true;
        (int active, bool isolating, int isolated, bool farOcc, bool roomsHide) steadyWith;
        int steadyChecked, shownChecked;

        /// <summary>A building's opaque and massing renderers on the steady copy or their own material, and its LOD0's rooms drawn or not, as things are now: true when any changed.</summary>
        bool SteadyOf(Built bt, Vector4[] state, int active, float iso, int isolated, bool farOcc)
        {
            var p = bt.parts; if (p == null) return false;
            bool changed = false;
            for (int w = 0; w < 3; w++)
            {
                var list = p.solid[w]; if (list.Count == 0) continue;
                var copy = w == 2 ? massingSteady : opaqueSteady;
                if (copy == null && (w != 0 || p.rooms.Count == 0)) continue;
                var st = state[bt.idx];
                bool on = global::Triband.Storey.Occlusion.Steady.Of(bt.idx, st.z, st.w, active, iso, isolated, w == 0 ? bt.mates : null, w == 2, farOcc);
                if (w == 0 && p.rooms.Count > 0 && RoomsDraw(bt, p, on, active)) changed = true;
                if (copy == null || on == p.steady[w]) continue;
                p.steady[w] = on; changed = true;
                foreach (var r in list) if (r != null) { r.sharedMaterial = on ? copy : w == 2 ? massing : opaque; SteadySwaps++; }
            }
            return changed;
        }

        /// <summary>A cell's renderers on the massing's steady copy while every member may draw steady, on the massing otherwise: true when they changed.</summary>
        bool SteadyOf(Cell c, Vector4[] state, int active, float iso, int isolated, bool farOcc)
        {
            if (c.renderers.Count == 0) return false;
            bool on = true;
            foreach (int idx in c.order) { var st = state[idx]; if (!global::Triband.Storey.Occlusion.Steady.Of(idx, st.z, st.w, active, iso, isolated, null, true, farOcc)) { on = false; break; } }
            if (on == c.steady) return false;
            c.steady = on;
            foreach (var r in c.renderers) if (r != null) r.sharedMaterial = on ? massingSteady : massing;
            return true;
        }

        // LOD0's rooms (docs/PERFORMANCE.md §7.7): whether they may be left out this frame (the automatic LOD, a perspective
        // eye, LOD0's far windows painted over), and how many buildings have them (none: nothing to decide before the cameras)
        bool roomsHide;
        int roomsKept;

        /// <summary>LOD0's rooms switched on or off since <see cref="StoreyDiagnostics"/> last read it (main thread).</summary>
        internal static int RoomsSwitched;

        /// <summary>
        /// A building's LOD0 rooms drawn while a window can show them (docs/PERFORMANCE.md §7.7): its nearest point within
        /// the distance from which LOD0's windows are painted over (<see cref="Parts.near"/>; with the fixed LOD they never
        /// are), anything able to show its inside (a cut or a fade: not <paramref name="steady"/>), or kept at full detail
        /// (<see cref="Forced"/>). They cast shadows only for the building the player is in, whose own light needs them
        /// (SPEC §5): elsewhere the shell's shadow keeps the sun out of them, through the windows only. True when either changed.
        /// </summary>
        bool RoomsDraw(Built bt, Parts p, bool steady, int active)
        {
            bool on = !roomsHide || p.near || !steady || bt.idx == active || forcedIdx.Contains(bt.idx), cast = bt.idx == active;
            bool changed = on != p.roomsOn || cast != p.roomsCast;
            if (on != p.roomsOn)
            {
                p.roomsOn = on; RoomsSwitched++;
                foreach (var r in p.rooms) if (r != null) r.enabled = on && p.drawn[0];
            }
            if (cast != p.roomsCast)
            {
                p.roomsCast = cast;
                var mode = cast ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (var r in p.rooms) if (r != null) r.shadowCastingMode = mode;
            }
            return changed;
        }

        // ---- the automatic LOD (docs/CITY.md §2, §3) ----
        readonly LodManager? lods;
        readonly Dictionary<int, Built> byIdx = new Dictionary<int, Built>();
        readonly Dictionary<(int x, int z), Cell> cells = new Dictionary<(int, int), Cell>();
        readonly HashSet<(int x, int z)> dirtyCells = new HashSet<(int, int)>();
        readonly HashSet<int> forcedIdx = new HashSet<int>(), prefetchIdx = new HashSet<int>();
        // the buildings with LOD0 up, whose rooms' distance and vertices are looked at every frame (not every building)
        readonly HashSet<Built> withLod0 = new HashSet<Built>();
        readonly LodView lodView = new LodView();
        readonly Dictionary<string, int> carry = new Dictionary<string, int>(StringComparer.Ordinal);
        // detail being generated on worker threads: one at a time per building, uploaded when done
        readonly List<Job> jobs = new List<Job>();
        readonly HashSet<int> busy = new HashSet<int>();

        sealed class Job
        {
            public Built bt = null!; public Site site = null!; public string name = ""; public int which;
            public volatile BuildLane lane;
            public System.Threading.Tasks.Task<object?> task = null!;
            public long started;   // Stopwatch ticks
            // a build no longer wanted stops at its next storey (docs/PERFORMANCE.md §9.2), once it has been unwanted a while:
            // a building on the edge of a LOD's distance would otherwise lose its build each time the camera wavers
            public volatile bool cancelled;
            public float unwantedSince = -1;

            /// <summary>Between a LOD0's storeys, on its build thread: the more urgent builds waiting run first; true stops it.</summary>
            public bool Between() { BuildThreads.Shared.RunMoreUrgent(lane); return cancelled; }
        }
        /// <summary>How long a build stays unwanted before it is stopped (seconds).</summary>
        const float CancelAfter = 0.25f;
        GameObject? cellRoot;
        float lastLod = -1;
        bool lodBusy;
        LodStats stats;

        // cells packed on a build thread, in Play mode (docs/PERFORMANCE.md §3.2): a cell shows the meshes it had until its
        // new ones are uploaded
        sealed class CellJob { public (int x, int z) key; public Cell cell = null!; public System.Threading.Tasks.Task<List<Cells.Packed>> task = null!; }
        readonly List<CellJob> cellJobs = new List<CellJob>();
        readonly List<(int x, int z)> dirtyLater = new List<(int, int)>();

        sealed class Cell
        {
            public readonly SortedDictionary<int, Built> members = new SortedDictionary<int, Built>();
            // the members' indices as a plain array, for the visibility check every frame: enumerating a
            // SortedDictionary makes garbage each time. Made again with the cell's meshes (every change to the members
            // marks the cell dirty, and dirty cells are built before the check)
            public int[] order = Array.Empty<int>();
            public readonly List<Mesh> meshes = new List<Mesh>();
            public readonly List<MeshRenderer> renderers = new List<MeshRenderer>();
            public bool shown = true;
            // a member shows LOD1 and none its massing: the cell draws into the shadow maps only, casting for the LOD1s
            // (docs/PERFORMANCE.md §7.8)
            public bool shadowsOnly;
            public bool steady;   // its renderers have the massing's steady copy
        }

        /// <summary>The camera the automatic LOD picks for: where it is (world space) and how many pixels a metre covers a metre away.</summary>
        public struct LodEye
        {
            public Vector3 position;
            public float pixelsPerMetre;
            /// <summary>Where it looks, its up, vertical field of view (degrees) and width over height: the view whose buildings
            /// are built first (docs/PERFORMANCE.md §9.2). A zero forward: no view, every building by its pixels alone.</summary>
            public Vector3 forward, up;
            public float fieldOfView, aspect;
            /// <summary>An orthographic camera's, which stands in front of <see cref="position"/>: LOD0's windows aren't painted over by its distances, so the rooms always show.</summary>
            public bool orthographic;

            /// <summary>A camera's eye. An orthographic one is treated as a perspective one far behind it, so detail goes by its zoom.</summary>
            public static LodEye Of(Camera c)
            {
                var t = c.transform;
                if (!c.orthographic)
                    return new LodEye { position = t.position, pixelsPerMetre = (float)LodManager.PixelsPerMetre(c.pixelHeight, c.fieldOfView), forward = t.forward, up = t.up, fieldOfView = c.fieldOfView, aspect = c.aspect };
                const float back = 200;
                float size = Mathf.Max(0.01f, c.orthographicSize);
                return new LodEye { position = t.position - t.forward * back, pixelsPerMetre = c.pixelHeight / (2 * size) * back,
                    forward = t.forward, up = t.up, fieldOfView = 2 * Mathf.Atan(size / back) * Mathf.Rad2Deg, aspect = c.aspect, orthographic = true };
            }
        }

        /// <summary>The automatic LOD's numbers this frame, for a stats overlay.</summary>
        public struct LodStats
        {
            /// <summary>Buildings, and how many show LOD0, LOD1, LOD2, and none (beyond the far distance).</summary>
            public int buildings, lod0, lod1, lod2, culled;
            /// <summary>Buildings holding LOD0 and LOD1 meshes (shown or not).</summary>
            public int resident0, resident1;
            /// <summary>Cells, the meshes they're split into, and the cells drawn.</summary>
            public int cells, cellMeshes, cellsShown;
            /// <summary>
            /// Detail LODs wanted and not started yet, being generated on worker threads, built and waiting for their meshes'
            /// upload, and the meshes uploaded this frame.
            /// </summary>
            public int queued, generating, uploading, builtThisFrame;
            /// <summary>Milliseconds the LOD work took this frame (picking, building, cells).</summary>
            public double ms;
            /// <summary>Cells of a layout loading over frames still to load (<see cref="LoadOverFrames"/>).</summary>
            public int loading;
            /// <summary>
            /// The vertices of the LOD0 meshes shown, and of those in the camera's view: the geometry a tile-based GPU holds
            /// for a pass. Past what it can hold with memoryless attachments, an iPhone throws the frame away.
            /// </summary>
            public long lod0Vertices, lod0VerticesInView;
            /// <summary>LOD0s shown with their rooms drawn (docs/PERFORMANCE.md §7.7): the rest of <see cref="lod0"/> leave them out, or have none.</summary>
            public int roomsShown;

            public override string ToString() =>
                $"{buildings:N0} buildings: LOD0 {lod0}, LOD1 {lod1}, LOD2 {lod2}, not drawn {culled}\n" +
                $"Resident: LOD0 {resident0}, LOD1 {resident1}\n" +
                $"Cells: {cellsShown} of {cells} drawn ({cellMeshes} meshes)\n" +
                $"Meshes uploaded this frame {builtThisFrame}, generating {generating}, uploading {uploading}, waiting {queued}; LOD {ms:0.00} ms\n" +
                $"LOD0 vertices: {lod0Vertices:N0} shown, {lod0VerticesInView:N0} in view; rooms drawn in {roomsShown} of {lod0}" +
                (loading > 0 ? $"\nLoading: {loading} cells to go" : "");
        }

        /// <summary>Whether the LOD is automatic (made with settings).</summary>
        public bool AutoLod => lods != null;

        /// <summary>The automatic LOD's settings (null when fixed); changes apply from the next frame.</summary>
        public LodSettings? LodSettings => lods?.Settings;

        /// <summary>
        /// This layout's district name and its neighbouring districts' buildings (with each district's offset from this
        /// site, metres), for party walls along the district edges (docs/CITY.md §5). Set before <see cref="Show"/>.
        /// </summary>
        public string District { get; set; } = "";
        public List<(List<BuildingData> buildings, string district, double dx, double dz)> Neighbours { get; } = new List<(List<BuildingData>, string, double, double)>();
        /// <summary>
        /// Neighbouring districts' baked layouts, as <see cref="Neighbours"/>: only their buildings along the edge are read
        /// (docs/PERFORMANCE.md §6.3), where a neighbour's layout was parsed whole for them.
        /// </summary>
        public List<(BakedLayout layout, string district, double dx, double dz)> BakedNeighbours { get; } = new List<(BakedLayout, string, double, double)>();

        Site NewSite(StoreyDocument doc) => MakeSite(doc, District, Neighbours, BakedNeighbours);

        static Site MakeSite(StoreyDocument doc, string district, List<(List<BuildingData> buildings, string district, double dx, double dz)> neighbours,
            List<(BakedLayout layout, string district, double dx, double dz)> baked) =>
            neighbours.Count == 0 && baked.Count == 0 ? new Site(doc.buildings) : Site.WithContext(doc.buildings, district, neighbours, baked);

        // a layout's site index and load plan, made on a build thread ahead of Show (docs/PERFORMANCE.md §6.2)
        sealed class Prepared
        {
            public StoreyDocument doc = null!; public string district = "";
            public List<(List<BuildingData> buildings, string district, double dx, double dz)> neighbours = null!;
            public List<(BakedLayout layout, string district, double dx, double dz)> baked = null!;
            public System.Threading.Tasks.Task<(Site site, List<BuildingData[]> plan)> task = null!;
        }
        Prepared? prepared;

        /// <summary>
        /// Make a layout's site index (every building's bounds, its grid, the neighbouring districts' buildings along the
        /// edge) and its load plan on a build thread, ahead of <see cref="Show"/>: false until they are made, then
        /// <see cref="Show"/> takes them, where it made them on the main thread (18 ms for 3,000 buildings headless; docs/
        /// PERFORMANCE.md §6.2). Set <see cref="District"/> and <see cref="Neighbours"/> first. True at once with the fixed
        /// LOD, which plans no load.
        /// </summary>
        public bool Prepare(StoreyDocument doc)
        {
            if (lods == null) return true;
            if (prepared != null && ReferenceEquals(prepared.doc, doc) && SameNeighbours(prepared)) return prepared.task.IsCompleted;
            var neighbours = new List<(List<BuildingData>, string, double, double)>(Neighbours); string district = District;   // copies: a build thread reads them
            var baked = new List<(BakedLayout, string, double, double)>(BakedNeighbours);
            var o = parent.position;
            (double, double)? from = Eye is LodEye eye ? (eye.position.x - o.x, eye.position.z - o.z) : ((double, double)?)null;
            prepared = new Prepared
            {
                doc = doc, district = district, neighbours = neighbours, baked = baked,
                task = BuildThreads.Shared.Run(() => { var made = MakeSite(doc, district, neighbours, baked); return (made, LayoutLoad.Plan(made, doc.buildings, from)); }, BuildLane.Load),
            };
            return prepared.task.IsCompleted;
        }

        bool SameNeighbours(Prepared p)
        {
            if (p.district != District || p.neighbours.Count != Neighbours.Count || p.baked.Count != BakedNeighbours.Count) return false;
            for (int i = 0; i < Neighbours.Count; i++) if (!p.neighbours[i].Equals(Neighbours[i])) return false;
            for (int i = 0; i < BakedNeighbours.Count; i++) if (!p.baked[i].Equals(BakedNeighbours[i])) return false;
            return true;
        }

        /// <summary>The automatic LOD's camera this frame. Null keeps every building at the LOD it shows.</summary>
        public LodEye? Eye { get; set; }

        /// <summary>
        /// Buildings that always want LOD0 under the automatic LOD (being edited, walked in, in the way of the camera): built
        /// before any other, never shown as their massing. In Play mode one shows what it has until its LOD0 is up, which a
        /// build thread makes ahead of everything else (docs/PERFORMANCE.md §3.1); in the editor, and with no build threads,
        /// it is built at once. The caller fills it each frame.
        /// </summary>
        public readonly HashSet<string> Forced = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Buildings whose LOD0 is wanted soon (near the camera's line to the player, round the player, round a spawn or
        /// teleport): built after the forced ones and those in view, whatever they show, and kept past the residency cap
        /// while asked for. The caller fills it each frame.
        /// </summary>
        public readonly HashSet<string> Prefetch = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The building's LOD0 is built (shown or not): what a spawn or teleport waits for behind its transition.</summary>
        public bool HasDetail(string id) => built.TryGetValue(id, out var b) && (lods == null ? b.parts != null && b.parts.objs[0].Count > 0 : lods.Get(b.idx)?.Has0 == true);

        /// <summary>The buildings whose box comes within <paramref name="radius"/> metres of a point (site space, x and z), into <paramref name="into"/>.</summary>
        public void BuildingsNear(double x, double z, double radius, ICollection<string> into)
        {
            if (lods == null) return;
            foreach (var bt in built.Values)
            {
                if (!(lods.Get(bt.idx) is LodManager.Entry e)) continue;
                double dx = Math.Max(Math.Max(e.x0 - x, 0), x - e.x1), dz = Math.Max(Math.Max(e.z0 - z, 0), z - e.z1);
                if (dx * dx + dz * dz <= radius * radius) into.Add(bt.id);
            }
        }

        /// <summary>
        /// The building has its collider, cooked and assigned (<see cref="Colliders"/>): a rigid body can rest on it. Not yet
        /// while the building waits for its first LOD0, which brings it, or while that is cooked (a frame or two after), and no
        /// longer once let go past <see cref="LodSettings.MaxColliders"/>.
        /// </summary>
        public bool HasCollider(string id) => built.TryGetValue(id, out var b) && b.collision != null;

        /// <summary><see cref="HasCollider"/> by table index (<see cref="TableIndexOf"/>), for the props manager.</summary>
        internal bool ColliderUp(int idx) => byIdx.TryGetValue(idx, out var b) && b.collision != null;

        /// <summary>
        /// Every building whose box comes within <paramref name="radius"/> metres of a point (site space, x and z) has its
        /// collider (<see cref="HasCollider"/>). The boxes are the layout's (its outlines'), so the fixed LOD answers too.
        /// </summary>
        public bool CollidersUpNear(double x, double z, double radius)
        {
            if (site == null) return false;
            foreach (var bt in built.Values)
            {
                if (bt.collision != null || !(site.ById(bt.id) is BuildingData b)) continue;
                var (x0, z0, x1, z1) = site.Bounds(b);
                double dx = Math.Max(Math.Max(x0 - x, 0), x - x1), dz = Math.Max(Math.Max(z0 - z, 0), z - z1);
                if (dx * dx + dz * dz <= radius * radius) return false;
            }
            return true;
        }

        /// <summary>The automatic LOD's numbers from the last frame.</summary>
        public LodStats Stats => stats;

        /// <summary>Which LOD every building shows: 0 full, 1 shell, 2 massing.</summary>
        public int Lod { get => lod; set => lod = Math.Max(0, Math.Min(2, value)); }

        /// <summary>The building table's index of a building, for the shader globals (isolate, active building); -1 when not shown.</summary>
        public int TableIndexOf(string id) => built.TryGetValue(id, out var b) ? b.idx : -1;

        /// <summary>
        /// An object this site made to draw its buildings (a building's detail mesh or a merged cell), not one the project put
        /// under the site: what the Scene view picks when a building is clicked.
        /// </summary>
        public bool Made(Transform t) => (detailRoot != null && t.IsChildOf(detailRoot.transform)) || (cellRoot != null && t.IsChildOf(cellRoot.transform));

        /// <summary>The building an object of the site's draws, by id; null for a merged cell, which draws many, and for any other object.</summary>
        public string? BuildingOf(GameObject go)
        {
            foreach (var bt in built.Values)
                if (bt.parts != null) foreach (var list in bt.parts.objs) foreach (var o in list) if (o.go == go) return bt.id;
            return null;
        }

        /// <summary>The layout as built (null before <see cref="Show"/>). A new one after every change.</summary>
        public Site? Site => site;

        /// <summary>
        /// Counts the times the layout was built again (all of it, or some buildings): a building's table index may have
        /// moved, or the building gone. <see cref="AnyVersion"/> counts it for every site (the props re-file then).
        /// </summary>
        public int Version { get; private set; }
        public static int AnyVersion { get; private set; }

        /// <summary>
        /// The building at table index idx is showing its LOD0 (or fading from it): its props draw. False once it has settled
        /// at LOD1 or LOD2, or with the fixed LOD set to another.
        /// </summary>
        public bool DetailShown(int idx)
        {
            if (lods == null) return lod == 0;
            var e = lods.Get(idx);
            return e == null || e.Visible(0);
        }

        /// <summary>
        /// A building's LOD0 shape while its LOD0 is built: its collision segments and its wall list (the walk model's, the
        /// cutaway's). Null when the building shows no LOD0.
        /// </summary>
        public Lod0Shape? ShapeOf(string id) => built.TryGetValue(id, out var b) ? b.shape : null;

        /// <summary>The first global wall id of a building's LOD0 walls (wall i is this + i), or -1.</summary>
        public int WallBaseOf(string id) => built.TryGetValue(id, out var b) ? b.wallBase : -1;

        /// <summary>The building table, for the occlusion system.</summary>
        internal BuildingTable Table => table;

        /// <summary>
        /// Set while something else drives the view (the occlusion system in Play mode): <see cref="Frame"/> then calls
        /// it instead of writing the view globals itself.
        /// </summary>
        internal Action<SiteRenderer>? Occlusion { get; set; }

        /// <summary>
        /// Upload a mesh the generator did not make as part of a building (Sink's footprints), coloured through the same
        /// rows, tagged with <paramref name="tag"/>. The caller owns the mesh.
        /// </summary>
        internal Mesh UploadExtra(MeshBuilder gb, string name, int tag) { gb.RetagForUpload(tag); var m = MeshUpload.Upload(gb, name, RowOf); m.hideFlags = flags; return m; }

        bool colliders;
        int layer;
        readonly HashSet<string> colliderPending = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// The layer of every object the site makes (the buildings, their LODs and colliders, the cells): the Storey
        /// Site's own, so cameras' culling masks, lights and physics see the buildings as the project sorts them.
        /// Changing it moves the objects already made.
        /// </summary>
        public int Layer
        {
            get => layer;
            set
            {
                if (layer == value) return;
                layer = value;
                foreach (var bt in built.Values)
                {
                    if (bt.root != null) bt.root.layer = value;
                    if (bt.parts != null) foreach (var list in bt.parts.objs) foreach (var o in list) if (o.go != null) o.go.layer = value;
                }
                foreach (var o in spareObjects) if (o.go != null) o.go.layer = value;
                if (detailRoot != null) detailRoot.layer = value;
                if (cellRoot != null) cellRoot.layer = value;
                foreach (var c in cells.Values) foreach (var r in c.renderers) if (r != null) r.gameObject.layer = value;
            }
        }

        /// <summary>
        /// Give every building a <see cref="MeshCollider"/> on its root, from <see cref="CollisionMesh"/> (floors, stairs,
        /// walls and pitched roofs; a few hundred triangles a building, not the drawn mesh). Turning it off removes them.
        /// </summary>
        public bool Colliders
        {
            get => colliders;
            set
            {
                if (colliders == value) return;
                colliders = value;
                if (value) { foreach (var id in built.Keys) colliderPending.Add(id); if (allColliders && load == null) MakeColliders(); }
                else { FinishBake(true, discard: true); colliderPending.Clear(); foreach (var b in built.Values) { RemoveCollider(b); b.collisionMade = null; } }
            }
        }

        bool allColliders;

        /// <summary>
        /// Every building has its collider from the frame it's built (a site's Colliders At Load, docs/PLAY.md §5): when a
        /// load ends and after a rebuild, the colliders still missing are made at once (<see cref="MakeColliders"/>), and none is
        /// let go past <see cref="LodSettings.MaxColliders"/>. Off, a building's collider comes with its LOD0, as the camera nears.
        /// </summary>
        public bool AllColliders
        {
            get => allColliders;
            set
            {
                if (allColliders == value) return;
                allColliders = value;
                if (value) { if (load == null) MakeColliders(); }
                else if (lods != null) TrimColliders();
            }
        }

        /// <summary>
        /// Baked once with these and given to the collider with the same, so assigning it cooks nothing: no cleaning or
        /// welding (the mesh is welded and has no degenerate triangles already), the fast midphase where the platform has it.
        /// </summary>
        public const MeshColliderCookingOptions Cooking = MeshColliderCookingOptions.UseFastMidphase;

        /// <summary>
        /// A site's first load in Play mode (a district streamed in, a scene starting) goes over several frames, nearest
        /// cells first, within the build budget, so it never holds a frame (docs/CITY.md §2); the buildings appear as their
        /// cells are taken. Off, or after an edit or in the editor, a load is done at once. On by default.
        /// </summary>
        public bool LoadOverFrames { get; set; } = true;

        /// <summary>The layout is still loading over frames: some of its buildings are not built yet.</summary>
        public bool Loading => load != null || cellJobs.Count > 0;
        LayoutLoad? load;

        /// <summary>Build everything again.</summary>
        public void Show(StoreyDocument doc)
        {
            Version++; AnyVersion++;
            using var _t = StoreyTimings.Time("site: full rebuild (every building)");
            bool first = built.Count == 0 && load == null;   // nothing shown yet: loading over frames makes nothing vanish
            // automatic LOD: the detail each building showed is built again at once, so a structural edit (a building
            // added or removed) doesn't fade the buildings in view through their massings
            carry.Clear();
            if (lods != null) foreach (var bt in built.Values) { var e = lods.Get(bt.idx); if (e != null && e.Shown < 2) carry[bt.id] = e.Shown; }
            Clear();
            // this site's block of building indices in the shared table, as many as the layout has buildings
            if (doc.buildings.Count != blockSize || blockStart < 0)
            {
                if (blockStart >= 0) table.ReleaseBlock(blockStart, blockSize);
                blockSize = doc.buildings.Count; blockStart = table.AllocBlock(blockSize);
                if (blockStart < 0)
                {
                    Debug.LogError($"Storey: the loaded sites have more than {BuildingTable.Capacity:N0} buildings between them; this one ({blockSize:N0}) isn't drawn.");
                    blockSize = 0; site = null; return;
                }
            }
            Reserve(doc.buildings.Count);
            // the site index and the load plan, made on a build thread ahead when they were asked for this layout (Prepare)
            (Site site, List<BuildingData[]> plan)? ready = null;
            if (prepared != null && ReferenceEquals(prepared.doc, doc) && SameNeighbours(prepared) && prepared.task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
                ready = prepared.task.Result;
            prepared = null;
            using (StoreyTimings.Time("site: index the layout")) site = ready?.site ?? NewSite(doc);
            book?.Dispose();
            book = new ColorRowBook(new ColorResolver(site), palette, table);
            // the table's rows for the layout, made once where they doubled as the load went: a building has three massing
            // rows and one colour row in the test cities (more grow the tables as before; docs/PERFORMANCE.md §6.8)
            table.Reserve(doc.buildings.Count * 3 + 64, doc.buildings.Count + 64);
            foreach (var kv in remaps) book.SetRemap(kv.Key, kv.Value.original, kv.Value.overwrite);
            if (lods == null) { foreach (var b in doc.buildings) Build(b); carry.Clear(); if (allColliders) MakeColliders(); return; }
            // cell by cell, each merged as soon as its buildings are built: a few cells' massings are held at a time, not
            // every building's (docs/CITY.md §7), built on the workers ahead of the cell being taken. A massing reaching
            // into another cell only has that one merged again
            var o = parent.position;
            load = ready != null ? new LayoutLoad(site, ready.Value.plan, Threads)
                : new LayoutLoad(site, doc.buildings, Threads, Eye is LodEye eye ? (eye.position.x - o.x, eye.position.z - o.z) : ((double, double)?)null);
            if (!(first && LoadOverFrames && Application.isPlaying)) TakeLoad(null);
        }

        /// <summary>
        /// Whether a building's props may draw their materials' steady copies now (docs/PERFORMANCE.md §11.1): nothing can
        /// hide its pixels, as for its own renderers (<see cref="global::Triband.Storey.Occlusion.Steady"/>); a prop carries
        /// no party wall.
        /// </summary>
        internal bool PropSteady(int idx)
        {
            if ((uint)idx >= (uint)table.State.Length) return false;
            var st = table.State[idx];
            return global::Triband.Storey.Occlusion.Steady.Of(idx, st.z, st.w, StoreyGlobals.ActiveIndex, StoreyGlobals.IsolateAmount, StoreyGlobals.IsolatedIndex);
        }

        /// <summary>
        /// Room for the objects a layout of <paramref name="buildings"/> makes, in the transform hierarchy the site is in, made
        /// at once (docs/PERFORMANCE.md §3.4): it grew a reallocation at a time as the load made them. The automatic LOD's
        /// cells (about one for every ten buildings in the test cities), its detail objects (kept up to the residency caps)
        /// and its colliders' objects; every building's LOD objects with the fixed LOD.
        /// </summary>
        void Reserve(int buildings)
        {
            var top = parent.root;
            int more = lods == null ? buildings * 4 : buildings / 8 + 16 + Math.Min(lods.Settings.MaxLod0 * 4 + lods.Settings.MaxLod1 * 2, 4096) + (allColliders ? buildings : Math.Max(0, lods.Settings.MaxColliders));
            int want = top.hierarchyCount + more;
            if (top.hierarchyCapacity < want) top.hierarchyCapacity = want;
        }

        /// <summary>
        /// The loading layout's cells, built and merged: every one now (<paramref name="sw"/> null), or as many as are ready
        /// within the frame's build budget. When the last is taken, the buildings' order is final (<see cref="Version"/>).
        /// </summary>
        void TakeLoad(System.Diagnostics.Stopwatch? sw)
        {
            var l = load!;
            using var _t = StoreyTimings.Time("site: layout loaded cell by cell");
            while (l.TryTake(sw == null, out var bs, out var made))
            {
                for (int i = 0; i < bs.Length; i++) Build(bs[i], made[i]);
                MergeCells(sw == null);
                if (sw != null && sw.Elapsed.TotalMilliseconds > budget) break;
            }
            if (!l.Done) return;
            load = null; carry.Clear();
            if (sw != null) { Version++; AnyVersion++; }   // over frames: the props and the occlusion find their buildings again
            if (allColliders) MakeColliders();   // every building built: their colliders before physics steps again
        }

        /// <summary>
        /// Build these buildings again (an edit or an undo changed them or their party walls); the others keep their
        /// meshes, and their colour rows are rewritten in place. The layout's buildings must be the ones shown, in order.
        /// </summary>
        public void Rebuild(StoreyDocument doc, IEnumerable<string> ids)
        {
            Version++; AnyVersion++;
            if (book == null || site == null || doc.buildings.Count != built.Count || doc.buildings.Any(b => !built.ContainsKey(b.id))) { StoreyTimings.Count("full rebuilds: the layout's buildings changed"); Show(doc); return; }
            using var _t = StoreyTimings.Time("site: rebuild of the edited buildings");
            using (StoreyTimings.Time("site: index the layout")) site = NewSite(doc);
            // only the rebuilt buildings' colour rows change, and they are written again as those are built
            book.Retarget(new ColorResolver(site), rewrite: false);
            foreach (var id in ids)
            {
                var b = site.ById(id); if (b == null) continue;
                Release(built[id]); built.Remove(id);
                book.ReleaseBuilding(site.IndexOf(b));
                Build(b);
            }
            if (allColliders) MakeColliders();   // the rebuilt buildings' colliders, gone with their old selves
        }

        /// <summary>Per frame: the view globals (neutral unless the editor set a view), the palette, the LODs, the table upload.</summary>
        public void Frame(bool lodTint, SiteView? view = null)
        {
            FinishBake(false);
            if (unwritten.Count > 0) DisposeUnwritten(false);
            if (colliderPending.Count > 0) BuildColliders();
            if (lods != null) UpdateLods();
            else if (shownLod != lod)
            {
                foreach (var b in built.Values) table.SetLod(b.idx, lod, lod, 1);
                shownLod = lod;
            }
            // LOD0's see-through windows: LOD1's painted pane where LOD0 takes over, clear glass closer in (the automatic
            // LOD only: with a fixed LOD there is no switch to hide)
            if (lods != null && Eye is LodEye ge)
            {
                float at = (float)lods.Settings.PaintDistance(ge.pixelsPerMetre), clear = (float)Math.Min(1, Math.Max(0, lods.Settings.GlassClear));
                StoreyGlobals.SetGlassFill(at * clear, clear >= 1 ? 0 : at);
                // the rooms are left out past that distance (RoomsDraw), where no window shows them; an orthographic camera
                // stands in front of its eye, nearer than the windows' distances say
                roomsHide = clear < 1 && !ge.orthographic;
            }
            else { StoreyGlobals.SetGlassFill(0, 0); roomsHide = false; }
            StoreyGlobals.SetLodTint(lodTint);
            // the view globals (active building, cutaway, isolate, occlusion) are one set for every site: written by the
            // site that has a view (the editor's, or the occlusion system's), and neutral only when none has
            var v0 = view ?? SiteView.Neutral;
            claims = Occlusion != null || v0.activeId != null || v0.isolateId != null;
            live.Add(this);
            bool writeView = claims || !OtherClaims();
            if (Occlusion != null)
            {
                using (StoreyTimings.Time("site: occlusion")) Occlusion(this);
                StoreyGlobals.SetIsolate(0, -1);
                StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
                BindAndUpload();
                return;
            }
            var v = v0;
            EditCutaway(v.cut && v.activeId != null && TableIndexOf(v.activeId) >= 0 ? v.activeId : null, v);
            if (!writeView) { BindAndUpload(); return; }
            int active = v.activeId != null ? TableIndexOf(v.activeId) : -1;
            StoreyGlobals.SetActive(active, active >= 0 ? v.clipY : 1e9f);
            if (active >= 0 && v.cut) { StoreyGlobals.SetCamera(v.camera, v.focus, v.cameraDir); StoreyGlobals.SetCut(true, v.stubHeight, v.cutBase, v.cutTop, v.clipY); }
            else StoreyGlobals.SetCut(false, 1, 0, 0, active >= 0 ? v.clipY : 1e9f);
            int iso = v.isolateId != null ? TableIndexOf(v.isolateId) : -1;
            StoreyGlobals.SetIsolate(iso >= 0 ? v.isolateAmount : 0, iso);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector3.zero, 2.4f);
            StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
            BindAndUpload();
        }

        /// <summary>The palette bound, the colours artists' parts keep written (docs/OPENINGS.md §6), and the table's changes uploaded.</summary>
        void BindAndUpload()
        {
            palette.Bind();
            table.SyncKindColors(palette);
            table.Upload();
        }

        // every renderer drawing now, and whether it owns the view globals (claims)
        static readonly HashSet<SiteRenderer> live = new HashSet<SiteRenderer>();
        bool claims;
        bool OtherClaims() { foreach (var r in live) if (r != this && r.claims) return true; return false; }

        // the editor's wall slides, by wall id: eased over the prototype's 0.22 s, down when a wall is in the way, back up after
        readonly Dictionary<int, float> editSlide = new Dictionary<int, float>();
        float lastEdit = -1;
        // the walls to slide down this frame, and the slides' ids to step: kept, so a frame makes no garbage
        readonly HashSet<int> cutDown = new HashSet<int>();
        readonly List<int> slideIds = new List<int>();

        /// <summary>
        /// Walls are sliding in the editor's cutaway, or the automatic LOD is fading or has detail still to build: the
        /// editor keeps redrawing until they settle.
        /// </summary>
        public bool Animating => wallsMoving || lodBusy;
        bool wallsMoving;

        /// <summary>
        /// The editor's cutaway (the Interior tab): every wall has an id, so the shader drops a wall by its slide value,
        /// which only the occlusion system animates in Play mode. Here each wall of the edited building, and each party wall
        /// a neighbour shares with it, is down or up at once by the same test: every one on the active storey (Down, the
        /// default: nothing moves as the camera orbits), those between the camera and the focus (Cutaway), or none (Up).
        /// </summary>
        void EditCutaway(string? activeId, SiteView v)
        {
            float now = Time.realtimeSinceStartup, dt = lastEdit < 0 ? 0 : Mathf.Min(0.05f, now - lastEdit); lastEdit = now;
            float step = dt / (float)global::Triband.Storey.Occlusion.OcclusionCore.CutSlide;
            var act = activeId != null && site != null ? site.ById(activeId) : null;
            // nothing cut and nothing sliding back: the case of every site the editor isn't cutting (every frame, in Play
            // mode too, for one with no occlusion system)
            if (act == null && editSlide.Count == 0) { wallsMoving = false; return; }
            var down = cutDown; down.Clear();
            if (act != null)
            {
                int ai = site!.IndexOf(act);
                foreach (var bt in built)
                {
                    if (bt.Value.shape == null || bt.Value.wallBase < 0) continue;
                    var walls = bt.Value.shape.Walls; var bases = bt.Value.shape.WallBase; bool own = bt.Key == act.id;
                    for (int i = 0; i < walls.Count; i++)
                    {
                        if (!own && (walls[i].K < 8 || (walls[i].K >> 3) - 1 != ai)) continue;
                        // only the active storey's own walls: the next floor's start up and slide down when it becomes active (a
                        // neighbour's party walls may sit at other heights, and the shader's cut range keeps them right)
                        if (own && i < bases.Count && Math.Abs(bases[i] + parent.position.y - v.cutBase) > 0.05) continue;
                        if (v.walls == CutWalls.Up) continue;
                        if (v.walls == CutWalls.Down || global::Triband.Storey.Occlusion.OcclusionCore.WallBlocks(walls[i].W, v.camera.x, v.camera.z, v.focus.x, v.focus.z)) down.Add(bt.Value.wallBase + i);
                    }
                }
            }
            bool moving = false;
            foreach (int id in down) if (!editSlide.ContainsKey(id)) editSlide[id] = 0;
            slideIds.Clear(); foreach (int id in editSlide.Keys) slideIds.Add(id);
            foreach (int id in slideIds)
            {
                float a = editSlide[id], to = down.Contains(id) ? 1 : 0;
                float n = to > a ? Mathf.Min(to, a + step) : Mathf.Max(to, a - step);
                if (n != to) moving = true;
                table.SetWall(id, n);
                if (n <= 0 && to == 0) editSlide.Remove(id); else editSlide[id] = n;
            }
            wallsMoving = moving;
        }

        /// <summary>
        /// Remap building <paramref name="building"/>'s colours (its index in the layout; palette ids, pairwise), as a
        /// <c>ColorRemap</c> would a renderer's. Needs Color Pipeline. Empty arrays clear it. No mesh is rebuilt.
        /// </summary>
        public void SetRemap(int building, string[] original, string[] overwrite)
        {
            if (original.Length == 0) remaps.Remove(building); else remaps[building] = (original, overwrite);
            book?.SetRemap(building, original, overwrite);
        }

        /// <summary>A building's table row, root object and massing (<paramref name="made"/> when made already), and its detail if it showed one.</summary>
        void Build(BuildingData b, Lod2Mesh? made = null)
        {
            // the table row is the site's block plus the building's index in the layout: party walls carry their
            // neighbour's layout index, moved into the block at upload (MeshUpload's mates), and the shader looks it up
            var bt = new Built { id = b.id, idx = blockStart + site!.IndexOf(b) };
            built[b.id] = bt; byIdx[bt.idx] = bt;

            StoreyTimings.Count("buildings built");
            var t2 = StoreyTimings.Time("build: massing and its rows");
            var l2 = made ?? Lod2.Build(site!, b);
            var rowMap = WriteRows(bt, l2);
            l2.Tag = bt.idx + 2 * Lod1.LOD_TAG;
            t2.Dispose();

            if (lods == null)
            {
                BuildDetail(bt, b, 0); BuildDetail(bt, b, 1);
                string n2 = Named(b.name, "LOD2");
                Add(bt, 2, n2, MeshUpload.Upload(l2, n2, rowMap), massing, true);
                l2.Recycle();   // uploaded: its lists go to the next massing
                table.SetLod(bt.idx, lod, lod, 1);
                if (colliders) colliderPending.Add(b.id);
                return;
            }

            // automatic: the massing goes into its cell (and is let go once the cell is merged), and the detail the building
            // showed (an edit) is built again at once, so it doesn't fade through its massing
            bt.l2 = l2; bt.rowMap = rowMap;
            double x0 = double.MaxValue, z0 = x0, x1 = double.MinValue, z1 = x1, y1 = 0;
            foreach (var p in l2.P) { x0 = Math.Min(x0, p.x); z0 = Math.Min(z0, p.z); x1 = Math.Max(x1, p.x); z1 = Math.Max(z1, p.z); y1 = Math.Max(y1, p.y); }
            if (l2.Verts == 0) { x0 = x1 = b.pos.x; z0 = z1 = b.pos.z; }
            bool fresh = lods.Get(bt.idx) == null, forced = Forced.Contains(b.id);
            var e = lods.Set(bt.idx, x0, z0, x1, z1, y1 + 1);
            if (fresh)
            {
                // shown at once, without a fade: the massing when the layout loads, or the detail it had before a Show
                int was = forced ? 0 : carry.TryGetValue(b.id, out var w) ? w : 2;
                e.Want = e.Shown = e.From = was; e.T = 1;
            }
            bt.cell = Cells.KeyOf((x0 + x1) / 2, (z0 + z1) / 2);
            if (!cells.TryGetValue(bt.cell, out var c)) cells[bt.cell] = c = new Cell();
            c.members[bt.idx] = bt; bt.inCell = true; dirtyCells.Add(bt.cell);
            if (e.Visible(1)) { BuildDetail(bt, b, 1); lods.SetBuilt(bt.idx, 1, true); }
            if (e.Visible(0) || forced) { BuildDetail(bt, b, 0); lods.SetBuilt(bt.idx, 0, true); }
            table.SetLod(bt.idx, e.Shown, e.From, e.RowFade);
        }

        /// <summary>A massing's parameter rows written into the table: the table row of each of the massing's rows.</summary>
        int[] WriteRows(Built bt, Lod2Mesh l2)
        {
            var rowMap = new int[l2.Rows.Count];
            for (int r = 0; r < l2.Rows.Count; r++) { rowMap[r] = table.WriteRow(l2.Rows[r], RowOf(l2.Rows[r].wall.style)); bt.rows.Add(rowMap[r]); }
            return rowMap;
        }

        /// <summary>A building's LOD0 (with its glass and its walls in the table) or LOD1, generated and uploaded now.</summary>
        void BuildDetail(Built bt, BuildingData b, int which)
        {
            object made;
            using (StoreyTimings.Time(which == 0 ? "build: LOD0 on the main thread" : "build: LOD1 on the main thread", b.name)) made = Generate(site!, b, bt.idx, which, (blockStart, blockSize), colliders && bt.collision == null)!;
            FinishDetail(bt, b.name, which, made);
        }

        /// <summary>
        /// A detail build as a worker hands it over: its meshes packed in the upload's layout, in parts past 65,535
        /// vertices, and for LOD0 the shape that is kept. The generator's own result is gone by then (docs/CITY.md §7).
        /// </summary>
        sealed class Detail
        {
            public List<PackedMesh> opaque = null!;
            public List<PackedMesh>? glass, rooms;
            public Lod0Shape? shape;
            public CollisionMesh? collision;
        }

        /// <summary>
        /// The engine-free half of a detail build: generated, tagged and packed (LOD1 welded first). Safe on a worker thread (the
        /// generator's memos are per site and thread-safe; ParallelBuildTests). <paramref name="mates"/>: the site's block
        /// of the table, for party walls; <paramref name="collider"/>: the building's collision mesh is wanted too.
        /// </summary>
        static object? Generate(Site site, BuildingData b, int idx, int which, (int start, int count) mates, bool collider, Func<bool>? between = null)
        {
            // once packed, the builders' lists give their chunks back for the next build (MeshBuilder.Recycle)
            if (which == 1)
            {
                MeshBuilder m;
                using (markLod1.Auto()) m = Lod1.Build(site, b);
                m.RetagForUpload(idx + Lod1.LOD_TAG);
                Detail shell;
                using (markPack.Auto()) shell = new Detail { opaque = MeshPack.Pack(m, windows: true) };
                m.Recycle();
                return shell;
            }
            Lod0Result? l0;
            using (markLod0.Auto()) l0 = between != null ? Lod0.Build(site, b, between) : Lod0.Build(site, b);
            if (l0 == null) return null;   // stopped: no longer wanted (docs/PERFORMANCE.md §9.2)
            l0.Op.RetagForUpload(idx); l0.Glass.RetagForUpload(idx);
            // the glass's panes carry their place too, to start as LOD1's; neither is welded (it would find almost nothing)
            // the rooms apart from the rest (docs/PERFORMANCE.md §7.7): drawn only while a window can show them
            var full = new Detail { shape = l0.Shape, rooms = new List<PackedMesh>() };
            using (markPack.Auto()) { full.opaque = MeshPack.Pack(l0.Op, true, mates, weld: false, rooms: full.rooms); full.glass = MeshPack.Pack(l0.Glass, true, mates, weld: false); }
            if (collider) using (markCollision.Auto()) full.collision = CollisionMesh.Build(site, b, l0.Shape);
            l0.Op.Recycle(); l0.Glass.Recycle();
            return full;
        }

        /// <summary>
        /// The main thread's half, all at once (the editor's forced buildings, a site without build threads): the walls into
        /// the table, the meshes' colour rows and wall ids filled in, uploaded and drawn. A worker's build goes up in stages
        /// instead (<see cref="Staged"/>). A shell building's LOD0 has no see-through glass: no mesh for it.
        /// </summary>
        void FinishDetail(Built bt, string name, int which, object made)
        {
            using var _t = StoreyTimings.Time(which == 0 ? "build: LOD0 upload" : "build: LOD1 upload", name);
            var d = (Detail)made;
            BeginDetail(bt, which, d);
            for (int k = 0, n = PartsOf(which, d); k < n; k++) UploadPart(bt, name, which, d, k);
            EndDetail(bt, name, which, d);
        }

        /// <summary>LOD0's walls into the table (their ids go into its vertices) and its shape kept; nothing for LOD1.</summary>
        void BeginDetail(Built bt, int which, Detail d)
        {
            if (which == 1) return;
            var shape = d.shape!; bt.shape = shape;
            bt.mates = d.opaque.Count > 0 ? d.opaque[0].Mates : Array.Empty<int>(); steadyDirty.Add(bt.idx);
            bt.wallCount = shape.Walls.Count; bt.wallBase = table.AllocWalls(bt.wallCount);
            if (bt.wallBase >= 0)
            {
                for (int i = 0; i < bt.wallCount; i++) table.SetWallData(bt.wallBase + i, MeshUpload.WallData(shape.Walls[i].W));
            }
        }

        /// <summary>A build's meshes, in the order they go up: LOD0's opaque parts, its glass, its rooms; LOD1's parts.</summary>
        static int PartsOf(int which, Detail d) => d.opaque.Count + (which == 0 ? d.glass!.Count + d.rooms!.Count : 0);

        static PackedMesh PartAt(int which, Detail d, int k)
        {
            if (k < d.opaque.Count || which == 1) return d.opaque[k];
            k -= d.opaque.Count;
            return k < d.glass!.Count ? d.glass[k] : d.rooms![k - d.glass.Count];
        }

        /// <summary>
        /// The <paramref name="k"/>th of a build's meshes uploaded, with its renderer (off until the LOD is shown); a staged
        /// build's (<paramref name="st"/>) from the native mesh data a build thread wrote it into, where it did.
        /// </summary>
        void UploadPart(Built bt, string name, int which, Detail d, int k, Staged? st = null)
        {
            int no = d.opaque.Count;
            if (which == 1)
            {
                // no shadow of its own: the building casts through its massing while at LOD1 (docs/PERFORMANCE.md §7.8)
                string n1 = Named(name, "LOD1", k, no);
                Add(bt, 1, n1, MeshOf(d.opaque[k], n1, -1, st, k), opaque, false);
                return;
            }
            int ng = d.glass!.Count, nr = d.rooms!.Count;
            if (k < no) { string n0 = Named(name, "LOD0", k, no); Add(bt, 0, n0, MeshOf(d.opaque[k], n0, bt.wallBase, st, k), opaque, true); return; }
            if (k < no + ng) { int g = k - no; string ns = Named(name, "LOD0 glass", g, ng); Add(bt, 0, ns, MeshOf(d.glass[g], ns, bt.wallBase, st, k), glass, false); return; }
            // the rooms: their shadow only for the building the player is in (RoomsDraw)
            int r = k - no - ng;
            if ((bt.parts?.rooms.Count ?? 0) == 0) roomsKept++;
            string nm = Named(name, "LOD0 rooms", r, nr);
            Add(bt, 0, nm, MeshOf(d.rooms[r], nm, bt.wallBase, st, k), opaque, false, rooms: true);
        }

        /// <summary>A build up: LOD0's numbers and its collider asked for.</summary>
        void EndDetail(Built bt, string name, int which, Detail d)
        {
            if (which == 1) return;
            long nv = 0, nr = 0; foreach (var p in d.opaque) nv += p.Verts; foreach (var p in d.glass!) nv += p.Verts; foreach (var p in d.rooms!) nr += p.Verts;
            bt.lod0Verts = nv; bt.roomsVerts = nr; withLod0.Add(bt);
            if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note($"LOD0 up: {name} ({bt.wallCount} walls at {bt.wallBase}, {d.opaque.Count} + {d.glass.Count} + {d.rooms.Count} meshes, {nv + nr:N0} vertices, {nr:N0} of them the rooms')");
            // automatic: a building's collider comes with its first LOD0 and stays when the LOD0 is dropped
            if (d.collision != null && bt.collision == null) bt.collisionMade = d.collision;
            if (lods != null && colliders && bt.collision == null) colliderPending.Add(bt.id);
        }

        /// <summary>
        /// A worker's build on its way up (docs/PERFORMANCE.md §3.2, §4.1): its walls into the table and its parts' colour
        /// rows looked up here, their vertices finished on a build thread, then the parts uploaded within the frame's budget,
        /// counted per part (a forced building's at once). The LOD is built, and shown, once the last part is up.
        /// </summary>
        sealed class Staged
        {
            public Built bt = null!; public Site site = null!; public string name = ""; public int which; public BuildLane lane;
            public Detail d = null!; public System.Threading.Tasks.Task<int> finishing = null!; public int next, parts;
            public long started;   // the job's
            // each part's native mesh data and whether the build thread wrote it (docs/PERFORMANCE.md §4.1); null when
            // writing it there is off
            public Mesh.MeshDataArray[]? data; public bool[]? written;
        }
        readonly List<Staged> staged = new List<Staged>();

        /// <summary>
        /// While Storey Diagnostics records, every site's worker builds up since it last took them: the LOD, and the
        /// milliseconds from the build's start to its last part up, the time detail takes to come (docs/PERFORMANCE.md §9.1).
        /// </summary>
        public static readonly List<(int which, float ms)> DetailTimes = new List<(int, float)>();

        void Stage(Job j)
        {
            var d = (Detail)j.task.Result!;
            BeginDetail(j.bt, j.which, d);
            int n = PartsOf(j.which, d), wallBase = j.which == 0 ? j.bt.wallBase : -1;
            var rows = new int[n][];
            for (int k = 0; k < n; k++)
            {
                var pm = PartAt(j.which, d, k); var r = rows[k] = new int[pm.Styles.Length];
                for (int i = 0; i < r.Length; i++) r[i] = RowOf(pm.Styles[i]);
            }
            int which = j.which;
            // the native mesh data each part is written into on the build thread, allocated here (docs/PERFORMANCE.md §4.1)
            Mesh.MeshDataArray[]? data = null;
            if (MeshUpload.WriteOffMainThread)
            {
                data = new Mesh.MeshDataArray[n];
                for (int k = 0; k < n; k++) data[k] = MeshUpload.Allocate();
            }
            var written = data != null ? new bool[n] : null;
            staged.Add(new Staged
            {
                bt = j.bt, site = j.site, name = j.name, which = which, lane = j.lane, d = d, parts = n, started = j.started, data = data, written = written,
                finishing = BuildThreads.Shared.Run(() => FinishParts(which, d, rows, wallBase, data, written), j.lane),
            });
        }

        /// <summary>
        /// A staged build's parts finished on a build thread (their colour rows and wall ids), and each written into the
        /// native mesh data allocated for it, where there is some: the main thread then only makes the mesh from it.
        /// </summary>
        static int FinishParts(int which, Detail d, int[][] rows, int wallBase, Mesh.MeshDataArray[]? data, bool[]? written)
        {
            int n = rows.Length;
            for (int k = 0; k < n; k++)
            {
                var pm = PartAt(which, d, k);
                pm.Finish(rows[k], wallBase);
                if (data != null && data[k].Length > 0) written![k] = MeshUpload.Write(pm, data[k]);
            }
            return n;
        }

        /// <summary>
        /// A part's mesh (main thread): made from the native mesh data a build thread wrote it into (docs/PERFORMANCE.md
        /// §4.1), or copied in here (a build finished on the main thread, writing it off, or Unity refusing it).
        /// </summary>
        Mesh MeshOf(PackedMesh pm, string name, int wallBase, Staged? st, int k)
        {
            var data = st?.data;
            if (data != null && data[k].Length > 0)
            {
                var md = data[k]; data[k] = default;
                if (st!.written![k] && MeshUpload.Apply(pm, md, name) is Mesh made) return made;
                if (!st.written[k]) md.Dispose();
                if (MeshUpload.WasRefused) MeshUpload.Refused();
            }
            return MeshUpload.Upload(pm, name, RowOf, wallBase);
        }

        // native mesh data of builds let go while a build thread may still be writing it: disposed on the main thread once
        // the thread is done with it (docs/PERFORMANCE.md §4.1)
        static readonly List<(System.Threading.Tasks.Task task, Mesh.MeshDataArray[] data, int from)> unwritten = new List<(System.Threading.Tasks.Task, Mesh.MeshDataArray[], int)>();

        static void DisposeData(Mesh.MeshDataArray[] data, int from)
        {
            for (int k = from; k < data.Length; k++) if (data[k].Length > 0) { data[k].Dispose(); data[k] = default; }
        }

        /// <summary>The native mesh data of builds let go, disposed once their threads are done with it (<paramref name="wait"/>: waited for).</summary>
        static void DisposeUnwritten(bool wait)
        {
            for (int i = unwritten.Count - 1; i >= 0; i--)
            {
                var (task, data, from) = unwritten[i];
                if (!task.IsCompleted) { if (!wait) continue; try { task.Wait(); } catch (AggregateException) { } }
                DisposeData(data, from); unwritten.RemoveAt(i);
            }
        }

        /// <summary>
        /// One lane's staged builds' parts uploaded: a forced building's all at once, the rest within the budget, one part a
        /// frame at least. One whose building was built again or let go meanwhile is let go.
        /// </summary>
        int TakeStaged(System.Diagnostics.Stopwatch sw, int made, BuildLane lane)
        {
            var l = lods!; bool forced = lane == BuildLane.Forced;
            for (int i = 0; i < staged.Count; i++)
            {
                var st = staged[i]; if (st.lane != lane) continue;
                // its building still shown (an edit's Rebuild keeps the others, and indexes the layout again: a build of
                // the old index is let go, as a worker's is), and, before its first part is up, still wanted
                bool mine = built.TryGetValue(st.bt.id, out var cur) && ReferenceEquals(cur, st.bt);
                var e = mine && ReferenceEquals(st.site, site) ? l.Get(st.bt.idx) : null;
                bool wanted = e != null && (st.next > 0 || st.lane == BuildLane.Background || e.Want <= st.which);
                if (!wanted || st.finishing.IsFaulted)
                {
                    if (st.finishing.IsFaulted && e != null) { var ex = st.finishing.Exception!; Debug.LogException(ex.InnerException ?? ex); }
                    staged.RemoveAt(i--); busy.Remove(st.bt.idx); Unstage(st, mine);
                    continue;
                }
                if (!st.finishing.IsCompleted || !(forced || made == 0 || sw.Elapsed.TotalMilliseconds <= budget)) continue;
                using (StoreyTimings.Time(st.which == 0 ? "build: LOD0 upload" : "build: LOD1 upload", st.name))
                    do { UploadPart(st.bt, st.name, st.which, st.d, st.next++, st); made++; }
                    while (st.next < st.parts && (forced || sw.Elapsed.TotalMilliseconds <= budget));
                if (st.next < st.parts) continue;   // the rest next frame
                staged.RemoveAt(i--); busy.Remove(st.bt.idx);
                EndDetail(st.bt, st.name, st.which, st.d);
                l.SetBuilt(st.bt.idx, st.which, true);
                if (StoreyDiagnostics.Recording && DetailTimes.Count < 4096) DetailTimes.Add((st.which, (float)StoreyDiagnostics.Ms(st.started)));
            }
            return made;
        }

        /// <summary>
        /// A staged build let go. A building still shown loses what the build had put up (its walls in the table, the parts
        /// uploaded so far); one released has lost it with the rest of its meshes.
        /// </summary>
        void Unstage(Staged st, bool mine)
        {
            if (mine)
            {
                if (st.next > 0) DropDetail(st.bt, st.which);
                else if (st.which == 0)
                {
                    var bt = st.bt;
                    if (bt.wallBase >= 0) table.ReleaseWalls(bt.wallBase, bt.wallCount);
                    bt.wallBase = -1; bt.wallCount = 0; bt.shape = null; bt.mates = Array.Empty<int>();
                }
            }
            Abandon(st);
        }

        /// <summary>A staged build let go: the parts not uploaded give their chunks back, once their vertices are finished.</summary>
        static void Abandon(Staged st)
        {
            int from = st.next;
            void Release() { for (int k = from; k < st.parts; k++) PartAt(st.which, st.d, k).Release(); }
            if (st.finishing.IsCompleted) { Release(); if (st.data != null) DisposeData(st.data, from); return; }
            st.finishing.ContinueWith(_ => Release(), System.Threading.CancellationToken.None, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously, System.Threading.Tasks.TaskScheduler.Default);
            if (st.data != null) unwritten.Add((st.finishing, st.data, from));   // its native data on the main thread, once the build thread is done
        }

        /// <summary>A building's staged build, finished and uploaded now (the editor wants it at once): false when it has none.</summary>
        bool TakeStagedNow(Built bt, int which)
        {
            for (int i = 0; i < staged.Count; i++)
            {
                var st = staged[i]; if (st.bt != bt || st.which != which) continue;
                using (StoreyTimings.Time(which == 0 ? "build: LOD0 waited for (a worker's)" : "build: LOD1 waited for (a worker's)", st.name))
                    try { st.finishing.Wait(); } catch (AggregateException) { }
                staged.RemoveAt(i); busy.Remove(bt.idx);
                if (st.finishing.IsFaulted || !ReferenceEquals(st.site, site)) { Unstage(st, true); return false; }
                while (st.next < st.parts) UploadPart(bt, st.name, which, st.d, st.next++, st);
                EndDetail(bt, st.name, which, st.d);
                return true;
            }
            return false;
        }

        /// <summary>A finished build that won't be uploaded after all: its packed arrays go back for the next build's.</summary>
        static void Discard(object? made)
        {
            if (!(made is Detail d)) return;
            foreach (var p in d.opaque) p.Release();
            if (d.glass != null) foreach (var p in d.glass) p.Release();
            if (d.rooms != null) foreach (var p in d.rooms) p.Release();
        }

        /// <summary>A mesh's name, numbered when it comes in parts.</summary>
        static string Part(string name, int i, int n) => n > 1 ? $"{name} ({i + 1})" : name;

        /// <summary>The build threads sized for this site, as its first build would size them, so work can start on them before it.</summary>
        internal void StartThreads() => _ = Threads;

        /// <summary>
        /// Build threads (<see cref="BuildThreads.Shared"/>, sized here): one count for every site, the most any asks for
        /// (automatic: two on a phone or tablet and one fewer than the cores, at most four, on a desktop), or
        /// <see cref="StoreyThreads.Override"/>. Each site sized them by its own settings, so two sites with different
        /// quality assets resized them in turn every frame (docs/PERFORMANCE.md §9.1). A site whose settings ask for none
        /// builds on the main thread.
        /// </summary>
        int Threads
        {
            get
            {
                if (lods == null) return 0;
                askedFor.Clear(); askedFor.Add(lods.Settings.Threads);
                foreach (var s in live) if (s.lods != null) askedFor.Add(s.lods.Settings.Threads);
                int n = BuildThreads.SharedCount(StoreyThreads.Override, askedFor, SystemInfo.processorCount, SystemInfo.deviceType == DeviceType.Handheld);
                if (BuildThreads.Shared.Count != n) BuildThreads.Shared.Resize(n);
                return lods.Settings.Threads == 0 ? 0 : n;
            }
        }
        static readonly List<int> askedFor = new List<int>();

        /// <summary>
        /// Detail built on the worker threads, staged as it comes in (<see cref="Stage"/>) and uploaded part by part within the
        /// budget (<see cref="TakeStaged"/>). A result for a layout or a building since rebuilt, or one no longer wanted, is
        /// thrown away.
        /// </summary>
        int TakeJobs(System.Diagnostics.Stopwatch sw, int made)
        {
            var l = lods!;
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i]; if (!j.task.IsCompleted) continue;
                bool current = ReferenceEquals(j.site, site) && built.TryGetValue(j.bt.id, out var cur) && ReferenceEquals(cur, j.bt);
                bool ok = j.task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && j.task.Result != null && current;   // null: stopped
                var e = ok ? l.Get(j.bt.idx) : null;
                // a prefetched LOD0 is taken though the building doesn't want it by distance (yet)
                if (e == null || (j.which == 0 ? e.Has0 : e.Has1) || (e.Want > j.which && j.lane != BuildLane.Background)) ok = false;
                jobs.RemoveAt(i--);
                // a build of a layout edited under it may throw, and is dropped; one of the layout shown is a fault to report
                if (j.task.IsFaulted) { var ex = j.task.Exception!; if (current) Debug.LogException(ex.InnerException ?? ex); }
                if (!ok) { busy.Remove(j.bt.idx); if (j.task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) Discard(j.task.Result); continue; }
                Stage(j);   // the building stays busy until its last part is up
            }
            // the forced ones' parts whatever the budget, as they are wanted now; then the buildings in view's, then the
            // prefetched
            made = TakeStaged(sw, made, BuildLane.Forced);
            made = TakeStaged(sw, made, BuildLane.Detail);
            return TakeStaged(sw, made, BuildLane.Background);
        }

        /// <summary>A worker's build of a building's LOD0 or LOD1, waited for and uploaded now: false when there is none, or it failed.</summary>
        bool TakeJobNow(Built bt, int which)
        {
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i]; if (j.bt != bt || j.which != which) continue;
                using (StoreyTimings.Time(which == 0 ? "build: LOD0 waited for (a worker's)" : "build: LOD1 waited for (a worker's)", j.name))
                    try { j.task.Wait(); } catch (AggregateException) { }
                jobs.RemoveAt(i); busy.Remove(bt.idx);
                if (j.task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || j.task.Result == null) return false;   // failed, or stopped
                if (!ReferenceEquals(j.site, site)) { Discard(j.task.Result); return false; }
                FinishDetail(bt, j.name, which, j.task.Result);
                return true;
            }
            return false;
        }

        /// <summary>Drop a building's LOD0 (and its walls) or LOD1; its collider stays.</summary>
        void DropDetail(Built bt, int which)
        {
            var p = bt.parts;
            if (p != null)
            {
                foreach (var o in p.objs[which]) FreeObject(o);   // before the meshes: no renderer is left with a destroyed one
                foreach (var m in p.meshes[which]) Kill(m);
                p.objs[which].Clear(); p.meshes[which].Clear(); p.solid[which].Clear(); p.steady[which] = false;
                p.rends[which].Clear(); p.drawn[which] = true;
                if (which == 0) { if (p.rooms.Count > 0) roomsKept--; p.rooms.Clear(); }
                if (p.Empty) bt.parts = null;   // the next one starts afresh: looked at again as its renderers are made
            }
            if (which != 0) return;
            bt.roomsVerts = 0;
            if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note($"LOD0 dropped: {site?.ById(bt.id)?.name ?? bt.id}");
            bt.mates = Array.Empty<int>();
            if (bt.wallBase >= 0) table.ReleaseWalls(bt.wallBase, bt.wallCount);
            bt.wallBase = -1; bt.wallCount = 0; bt.shape = null; bt.lod0Verts = 0; withLod0.Remove(bt);
        }

        /// <summary>
        /// The automatic LOD's frame: forced buildings' LOD0 built at once, then each building's LOD picked from the
        /// camera, meshes dropped past the caps, detail built most-pixels-first within the budget, the changed buildings'
        /// state written, and the cells built again where a building changed and shown where any shows its massing.
        /// </summary>
        readonly System.Diagnostics.Stopwatch lodClock = new System.Diagnostics.Stopwatch();

        // the build budget (Build Budget Ms) is a frame's, every site's together: each site was given all of it, so several
        // loading at once took it several times over. What this frame's sites have spent of it, and this site's share now
        static int budgetFrame = -1;
        static double budgetSpent;
        double budget;

        // its own method: a lambda capturing the build loop's locals made its closure on every pass of the loop, job or not
        // (hundreds of buildings a frame wanting detail: about 14 KB of garbage a frame)
        void StartJob(Built bt, BuildingData b, int which, BuildLane lane)
        {
            var s0 = site!; int ix = bt.idx; var mates = (blockStart, blockSize); bool collider = which == 0 && colliders && bt.collision == null;
            var job = new Job { bt = bt, site = s0, name = b.name, which = which, lane = lane, started = System.Diagnostics.Stopwatch.GetTimestamp() };
            Func<bool> between = job.Between;
            job.task = BuildThreads.Shared.Run(() => Generate(s0, b, ix, which, mates, collider, between), lane);
            jobs.Add(job);
        }

        /// <summary>
        /// Builds still running that a finished one would be thrown away for (another layout, the building's LOD there
        /// already, or a coarser one wanted, unless prefetched) are stopped at their next storey once they have been
        /// unwanted for <see cref="CancelAfter"/> (docs/PERFORMANCE.md §9.2): a tower's LOD0 held its thread 75–135 ms on a
        /// desktop whether or not anything would use it.
        /// </summary>
        void CancelUnwanted(float now)
        {
            var l = lods!;
            foreach (var j in jobs)
            {
                if (j.cancelled || j.task.IsCompleted) continue;
                bool current = ReferenceEquals(j.site, site) && built.TryGetValue(j.bt.id, out var cur) && ReferenceEquals(cur, j.bt);
                var e = current ? l.Get(j.bt.idx) : null;
                bool unwanted = e == null || (j.which == 0 ? e.Has0 : e.Has1) || (e.Want > j.which && j.lane != BuildLane.Background);
                if (!unwanted) { j.unwantedSince = -1; continue; }
                if (j.unwantedSince < 0) j.unwantedSince = now;
                else if (now - j.unwantedSince >= CancelAfter) { j.cancelled = true; cancelledBuilds++; }
            }
        }
        int cancelledBuilds;

        /// <summary>A building's builds still waiting for a thread move to the forced lane: it is wanted at once now.</summary>
        void Hurry(int idx)
        {
            foreach (var j in jobs)
                if (j.bt.idx == idx && j.lane != BuildLane.Forced && BuildThreads.Shared.Promote(j.task, BuildLane.Forced)) j.lane = BuildLane.Forced;
            foreach (var st in staged)
                if (st.bt.idx == idx && st.lane != BuildLane.Forced) { BuildThreads.Shared.Promote(st.finishing, BuildLane.Forced); st.lane = BuildLane.Forced; }
        }

        void UpdateLods()
        {
            using var _t = StoreyTimings.Time("site: automatic LOD");
            var sw = lodClock; sw.Restart();
            float now = Time.realtimeSinceStartup, dt = lastLod < 0 ? 0 : Mathf.Min(0.1f, now - lastLod); lastLod = now;
            var l = lods!; int made = 0, queued = 0; bool fading = false;
            if (!Application.isPlaying || budgetFrame != Time.frameCount) { budgetFrame = Time.frameCount; budgetSpent = 0; }
            budget = Math.Max(0, l.Settings.BudgetMs - budgetSpent);
            // the collider cap lowered: past it now, not only when the next batch is cooked
            if (l.Settings.MaxColliders != colliderCap) { colliderCap = l.Settings.MaxColliders; TrimColliders(); }
            // the mesh chunks kept for reuse: one limit for every site (the quality asset's)
            Chunks.KeepBytes = l.Settings.KeepChunkBytes;
            int threads = Threads;
            // in Play mode a forced building shows what it has until a build thread, taking it before anything else, has
            // its LOD0 (docs/PERFORMANCE.md §3.1): waiting for it here held the frame for the whole build, 75–135 ms for
            // the largest towers on a desktop. In the editor (an edit wants its building's walls at once), and with no
            // build threads, it is built now
            bool atOnce = !Application.isPlaying || threads == 0;
            // a forced building's rooms always draw (RoomsDraw): those leaving the set and those joining it are looked at again
            foreach (int i in forcedIdx) steadyDirty.Add(i);
            forcedIdx.Clear();
            foreach (var id in Forced)
            {
                if (!built.TryGetValue(id, out var bt)) continue;
                forcedIdx.Add(bt.idx); steadyDirty.Add(bt.idx);
                var e = l.Get(bt.idx); var b = site?.ById(id);
                if (e == null || e.Has0 || b == null) continue;
                if (!atOnce) { Hurry(bt.idx); continue; }
                // one already being generated is waited for, not built a second time
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                bool waited = TakeStagedNow(bt, 0) || TakeJobNow(bt, 0);
                if (!waited) BuildDetail(bt, b, 0);
                l.SetBuilt(bt.idx, 0, true); made++;
                if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note($"LOD0 of {b.name} wanted at once: {StoreyDiagnostics.Ms(t0):0.0} ms, {(waited ? "a worker's, waited for" : "built on the main thread")}");
            }
            prefetchIdx.Clear();
            foreach (var id in Prefetch) if (built.TryGetValue(id, out var pt) && !forcedIdx.Contains(pt.idx)) prefetchIdx.Add(pt.idx);
            if (load != null) TakeLoad(sw);   // a layout loading over frames: the cells ready, within the budget
            if (jobs.Count > 0 || staged.Count > 0) made = TakeJobs(sw, made);
            if (Eye is LodEye eye)
            {
                var o = parent.position;   // the layout is in the site's space: unrotated, unscaled
                LodView? seen = null;
                if (eye.forward.sqrMagnitude > 0)   // the view, in site space (the site is moved, never turned)
                {
                    lodView.Set(eye.position.x - o.x, eye.position.y - o.y, eye.position.z - o.z, eye.forward.x, eye.forward.y, eye.forward.z,
                        eye.up.x, eye.up.y, eye.up.z, eye.fieldOfView, eye.aspect, 0.1, l.Settings.Far);
                    seen = lodView;
                }
                using var _p = StoreyTimings.Time("site: LOD picked and applied");
                var f = l.Update(eye.position.x - o.x, eye.position.y - o.y, eye.position.z - o.z, eye.pixelsPerMetre, dt, forcedIdx, seen, prefetchIdx);
                foreach (var (idx, which) in f.Drop) if (byIdx.TryGetValue(idx, out var bt)) DropDetail(bt, which);
                for (int bi = 0; bi < f.Build.Count; bi++)
                {
                    var (idx, which, _) = f.Build[bi];
                    bool force = bi < f.Forced, pre = !force && bi >= f.Forced + f.Seen && bi < f.Forced + f.Seen + f.Prefetched;
                    if (busy.Contains(idx)) { if (force) Hurry(idx); continue; }   // being generated
                    // the rest wait for a thread, with at most as many again built and waiting for their upload (which a
                    // frame's budget may not keep up with); a forced one goes ahead of them
                    if (threads > 0 && !force && (jobs.Count >= threads || jobs.Count + staged.Count >= 2 * threads)) { queued += f.Build.Count - bi; break; }
                    if (!byIdx.TryGetValue(idx, out var bt)) continue;
                    var b = site?.ById(bt.id); if (b == null) continue;
                    if (threads > 0)
                    {
                        // most pixels first, as many at once as there are threads; the upload comes in a later frame
                        StartJob(bt, b, which, force ? BuildLane.Forced : pre ? BuildLane.Background : BuildLane.Detail); busy.Add(idx);
                        continue;
                    }
                    if (made > 0 && sw.Elapsed.TotalMilliseconds > budget) { queued++; continue; }
                    BuildDetail(bt, b, which); l.SetBuilt(idx, which, true); made++;
                }
                if (jobs.Count > 0) CancelUnwanted(now);
                foreach (int idx in f.Changed)
                {
                    var e = l.Get(idx)!; table.SetLod(idx, e.Shown, e.From, e.RowFade);
                    if (e.T < 1 || e.Hold > 0) fading = true;
                    if (!byIdx.TryGetValue(idx, out var cb)) continue;
                    ShowDetail(cb); if (cb.inCell) shownCells.Add(cb.cell);
                    // a LOD0 shown again after it was kept may have lost its collider past the cap meanwhile (one prefetched
                    // and never shown is the first to): made again from its shape, so one showing its LOD0 has its collider
                    if (colliders && cb.collision == null && cb.shape != null && e.Visible(0) && !Baking(cb)) colliderPending.Add(cb.id);
                }
                // a late LOD0's windows clearing (LodManager.Entry.Hold): its row alone, nothing shown or hidden
                foreach (int idx in f.Held) { var e = l.Get(idx)!; table.SetLod(idx, e.Shown, e.From, e.RowFade); fading = true; }
                stats.lod0 = f.Count[0]; stats.lod1 = f.Count[1]; stats.lod2 = f.Count[2]; stats.culled = f.Count[3];
                // the LOD0 geometry shown, and in the view (by each building's box, as the LOD manager sees it); and each LOD0
                // with rooms, whether its nearest point is in the distance from which its windows are painted over (RoomsDraw),
                // on from 5 % beyond it and off from 10 %, so a building on the line doesn't flick (and a camera moving after
                // this frame's eye stays inside it)
                double paint = l.Settings.PaintDistance(eye.pixelsPerMetre), ex = eye.position.x - o.x, ey = eye.position.y - o.y, ez = eye.position.z - o.z;
                long shownV = 0, viewV = 0; int roomsShown = 0;
                foreach (var bt in withLod0)
                {
                    if (!(l.Get(bt.idx) is LodManager.Entry e)) continue;
                    var p = bt.parts; bool rooms = p != null && p.rooms.Count > 0;
                    if (rooms)
                    {
                        double dx = Math.Max(Math.Max(e.x0 - ex, 0), ex - e.x1), dy = Math.Max(Math.Max(-ey, 0), ey - e.y1), dz = Math.Max(Math.Max(e.z0 - ez, 0), ez - e.z1);
                        bool near = Math.Sqrt(dx * dx + dy * dy + dz * dz) < paint * (p!.near ? 1.1 : 1.05);
                        if (near != p.near) { p.near = near; steadyDirty.Add(bt.idx); }
                    }
                    if (!e.Visible(0)) continue;
                    long v = bt.lod0Verts + (rooms && p!.roomsOn ? bt.roomsVerts : 0);
                    if (rooms && p!.roomsOn) roomsShown++;
                    shownV += v;
                    if (seen == null || seen.Sees((e.x0 + e.x1) / 2, e.y1 / 2, (e.z0 + e.z1) / 2, 0.5 * Math.Sqrt((e.x1 - e.x0) * (e.x1 - e.x0) + e.y1 * e.y1 + (e.z1 - e.z0) * (e.z1 - e.z0))))
                        viewV += v;
                }
                stats.lod0Vertices = shownV; stats.lod0VerticesInView = viewV; stats.roomsShown = roomsShown;
            }
            if (cellJobs.Count > 0) made = TakeCells(sw, made);
            MergeCells(false);
            // the cells whose members' LODs changed this frame, or whose members or meshes did (docs/PERFORMANCE.md §3.3):
            // every cell's members were walked every frame. While Storey Diagnostics records, every cell once a second, one
            // that changes though nothing marked it noted
            bool check = !shownAll && StoreyDiagnostics.Recording && Time.frameCount - shownChecked >= 60;
            if (check) shownChecked = Time.frameCount;
            if (shownAll || check)
            {
                int missed = 0;
                foreach (var kv in cells) if (CellShown(kv.Value, l) && check && !shownCells.Contains(kv.Key)) missed++;
                if (missed > 0) StoreyDiagnostics.Note($"cells: {missed} shown or hidden late, a change nothing marked (docs/PERFORMANCE.md §3.3)");
            }
            else foreach (var key in shownCells) if (cells.TryGetValue(key, out var c)) CellShown(c, l);
            shownCells.Clear(); shownAll = false;
            int shown = 0, pieces = 0;
            foreach (var c in cells.Values) { if (c.shown && !c.shadowsOnly) shown++; pieces += c.renderers.Count; }
            var (r0, r1) = l.Resident();
            lodBusy = fading || queued > 0 || jobs.Count > 0 || staged.Count > 0 || load != null || cellJobs.Count > 0;
            stats.loading = load != null ? load.Count - load.Taken : 0;
            stats.buildings = built.Count; stats.resident0 = r0; stats.resident1 = r1;
            stats.cells = cells.Count; stats.cellMeshes = pieces; stats.cellsShown = shown;
            stats.queued = queued; stats.generating = jobs.Count; stats.uploading = staged.Count; stats.builtThisFrame = made; stats.ms = sw.Elapsed.TotalMilliseconds;
            budgetSpent += sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// A cell drawn while a member shows its massing; in the shadow maps alone while a member shows LOD1 instead, whose
        /// own renderers cast no shadow: the massing has its outline and heights in a twentieth of the triangles. True when
        /// that changed.
        /// </summary>
        static bool CellShown(Cell c, LodManager l)
        {
            bool on = false, cast = false;
            foreach (int idx in c.order) { var e = l.Get(idx); if (e == null || e.Visible(2)) { on = true; break; } if (e.Visible(1)) cast = true; }
            bool shadowsOnly = !on && cast;
            if ((on || shadowsOnly) == c.shown && shadowsOnly == c.shadowsOnly) return false;
            c.shown = on || shadowsOnly; c.shadowsOnly = shadowsOnly;
            foreach (var r in c.renderers) { r.enabled = c.shown; r.shadowCastingMode = CellCasting(c); }
            return true;
        }

        /// <summary>
        /// The cells a building was built into, rebuilt in or removed from, merged again. In Play mode with build threads
        /// they are packed on one (<paramref name="now"/> false; docs/PERFORMANCE.md §3.2) and uploaded in a later frame
        /// within the budget (<see cref="TakeCells"/>); a cell whose pack is still under way is merged again once it is in.
        /// </summary>
        void MergeCells(bool now)
        {
            if (dirtyCells.Count == 0) return;
            bool later = !now && Application.isPlaying && lods != null && BuildThreads.Shared.Count > 0;
            foreach (var key in dirtyCells)
            {
                if (later && Packing(key)) { dirtyLater.Add(key); continue; }
                BuildCell(key, later);
            }
            dirtyCells.Clear();
            foreach (var key in dirtyLater) dirtyCells.Add(key);
            dirtyLater.Clear();
        }

        bool Packing((int x, int z) key)
        {
            foreach (var j in cellJobs) if (j.key == key && cells.TryGetValue(key, out var c) && ReferenceEquals(c, j.cell)) return true;
            return false;
        }

        /// <summary>
        /// A cell's massings merged again (a member was built, rebuilt or removed). Once merged, the members' massings are
        /// let go: the cell's meshes hold them. A member's is made again when its cell is next merged (docs/CITY.md §7).
        /// <paramref name="later"/>: packed on a build thread, uploaded when it is done.
        /// </summary>
        void BuildCell((int x, int z) key, bool later = false)
        {
            using var _t = StoreyTimings.Time("site: cells merged");
            StoreyTimings.Count("cells merged");
            if (!cells.TryGetValue(key, out var c)) return;
            if (c.members.Count == 0) { KillCell(c); cells.Remove(key); return; }
            c.order = c.members.Keys.ToArray(); steadyCells.Add(key); shownCells.Add(key);
            var members = new List<Cells.Member>(c.members.Count);
            foreach (var bt in c.members.Values) members.Add(new Cells.Member(MassingOf(bt), bt.idx + 2 * Lod1.LOD_TAG, bt.rowMap!));
            // the massings go into the packed cell, and their lists to the next ones built (a member's is made again from the
            // layout when its cell is merged again)
            foreach (var bt in c.members.Values) bt.l2 = null;
            bool half = MeshUpload.HalfMassing;
            if (later) { cellJobs.Add(new CellJob { key = key, cell = c, task = BuildThreads.Shared.Run(() => PackCell(members, half), BuildLane.Load) }); return; }
            UploadCell(key, c, PackCell(members, half));
        }

        /// <summary>A cell's members packed straight into the upload's layout, in chunks that go back once uploaded (no merged lists in between); their massings given back. Engine-free: any thread.</summary>
        static List<Cells.Packed> PackCell(List<Cells.Member> members, bool half)
        {
            var pieces = Cells.Pack(members, half);
            foreach (var m in members) m.Mesh.Recycle();
            return pieces;
        }

        void KillCell(Cell c)
        {
            foreach (var r in c.renderers) if (r != null) Kill(r.gameObject);
            foreach (var m in c.meshes) Kill(m);
            c.renderers.Clear(); c.meshes.Clear();
        }

        /// <summary>
        /// The cells packed on the build threads, uploaded within the budget (one a frame at least). One whose members
        /// changed meanwhile is let go (it is merged again), as is one of a layout no longer shown.
        /// </summary>
        int TakeCells(System.Diagnostics.Stopwatch sw, int made)
        {
            for (int i = 0; i < cellJobs.Count; i++)
            {
                var j = cellJobs[i]; if (!j.task.IsCompleted) continue;
                bool current = cells.TryGetValue(j.key, out var c) && ReferenceEquals(c, j.cell) && !dirtyCells.Contains(j.key);
                bool ok = current && j.task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion;
                if (ok && made > 0 && sw.Elapsed.TotalMilliseconds > budget) continue;   // next frame
                cellJobs.RemoveAt(i--);
                if (j.task.IsFaulted) { var ex = j.task.Exception!; Debug.LogException(ex.InnerException ?? ex); continue; }
                if (!ok) { if (j.task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) foreach (var p in j.task.Result) p.Release(); continue; }
                using (StoreyTimings.Time("site: cells uploaded")) UploadCell(j.key, c!, j.task.Result);
                made++;
            }
            return made;
        }

        void UploadCell((int x, int z) key, Cell c, List<Cells.Packed> pieces)
        {
            KillCell(c); steadyCells.Add(key); shownCells.Add(key);
            if (cellRoot == null) { cellRoot = new GameObject("Cells") { hideFlags = flags, layer = layer }; cellRoot.transform.SetParent(parent, false); }
            for (int i = 0; i < pieces.Count; i++)
            {
                string name = named ? $"Cell {key.x},{key.z}" + (pieces.Count > 1 ? $" ({i + 1})" : "") : "Cell";
                var mesh = MeshUpload.Upload(pieces[i], name); mesh.hideFlags = flags;
                c.meshes.Add(mesh);
                var go = new GameObject(name) { hideFlags = flags, layer = layer };
                go.transform.SetParent(cellRoot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = c.steady && massingSteady != null ? massingSteady : massing;
                mr.shadowCastingMode = CellCasting(c);
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.enabled = c.shown;
                c.renderers.Add(mr);
            }
        }

        static UnityEngine.Rendering.ShadowCastingMode CellCasting(Cell c) =>
            c.shadowsOnly ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;

        /// <summary>
        /// A cell member's massing: the one it was built with, or made again from the layout. The layout's buildings are the
        /// ones shown (a building that changed is built again, with its own), so it is the same massing, with the same
        /// rows; if its rows ever differ in number, they are written again.
        /// </summary>
        Lod2Mesh MassingOf(Built bt)
        {
            if (bt.l2 != null) return bt.l2;
            StoreyTimings.Count("massings made again for a cell");
            var l2 = Lod2.Build(site!, site!.ById(bt.id)!);
            l2.Tag = bt.idx + 2 * Lod1.LOD_TAG;
            if (l2.Rows.Count != bt.rowMap!.Length)
            {
                foreach (var r in bt.rows) table.ReleaseRow(r);
                bt.rows.Clear(); bt.rowMap = WriteRows(bt, l2);
            }
            return l2;
        }

        int RowOf(StyleRef s) => book!.RowOf(s);

        void Add(Built bt, int which, string name, Mesh mesh, Material mat, bool shadows, bool rooms = false)
        {
            mesh.hideFlags = flags;
            var p = bt.parts ??= new Parts();
            p.meshes[which].Add(mesh);
            var o = TakeObject(name);
            p.objs[which].Add(o);
            o.filter.sharedMesh = mesh;
            var mr = o.renderer;
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            bool on = p.drawn[which] = Draws(bt, which);
            if (rooms)
            {
                // LOD0's rooms: switched with the LOD0, and by RoomsDraw
                p.rooms.Add(mr); mr.enabled = on && p.roomsOn;
                if (p.roomsCast) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            else { p.rends[which].Add(mr); mr.enabled = on; }
            if (mat == opaque || mat == massing)
            {
                p.solid[which].Add(mr);
                var copy = mat == opaque ? opaqueSteady : massingSteady;
                if (p.steady[which] && copy != null) mr.sharedMaterial = copy;   // as the LOD's others are
            }
            steadyDirty.Add(bt.idx);   // the first of a LOD's has its own material until looked at
        }

        /// <summary>An object for a detail mesh: one kept from a drop, or made under the site's detail object.</summary>
        PartObject TakeObject(string name)
        {
            while (spareObjects.Count > 0)
            {
                var o = spareObjects.Pop();
                if (o.go == null) continue;   // destroyed meanwhile (the editor)
                if (named) o.go.name = name;
                RenderersReused++;
                return o;
            }
            if (detailRoot == null) { detailRoot = new GameObject("Detail") { hideFlags = flags, layer = layer }; detailRoot.transform.SetParent(parent, false); }
            var go = new GameObject(name) { hideFlags = flags, layer = layer };
            go.transform.SetParent(detailRoot.transform, false);
            var made = new PartObject { go = go, filter = go.AddComponent<MeshFilter>(), renderer = go.AddComponent<MeshRenderer>() };
            made.renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;   // the GPU Resident Drawer wants no probes on the renderer
            RenderersMade++;
            return made;
        }

        /// <summary>A detail object whose mesh is going: switched off with none and kept for the next build, or destroyed past <see cref="SpareObjects"/>.</summary>
        void FreeObject(PartObject o)
        {
            if (o.go == null) return;
            if (spareObjects.Count >= SpareObjects) { Kill(o.go); return; }
            o.renderer.enabled = false; o.filter.sharedMesh = null;
            spareObjects.Push(o);
        }

        /// <summary>
        /// A building's own object, for its collider: made with the first (docs/PERFORMANCE.md §3.4). Every building had
        /// one from the load, about 3,000 GameObjects on the test city, though the cells draw the massings and the detail
        /// objects sit under the site's detail object.
        /// </summary>
        GameObject Root(Built bt)
        {
            if (bt.root != null) return bt.root;
            var root = new GameObject(named ? site?.ById(bt.id)?.name ?? bt.id : "Building") { hideFlags = flags, layer = layer };
            root.transform.SetParent(parent, false);
            return bt.root = root;
        }

        /// <summary>
        /// Whether a building's LOD draws. With the automatic LOD, LOD0 and LOD1 only while shown or fading out: one kept for
        /// later (under the residency caps) drew too, every vertex fetched and clipped in the vertex shader, shadows too.
        /// </summary>
        bool Draws(Built bt, int which) => lods == null || which == 2 || !(lods.Get(bt.idx) is LodManager.Entry e) || e.Visible(which);

        /// <summary>A building's LOD0 and LOD1 renderers switched on or off as its LOD state changed (<see cref="Draws"/>).</summary>
        void ShowDetail(Built bt)
        {
            var p = bt.parts; if (p == null) return;
            for (int w = 0; w < 2; w++)
            {
                bool on = Draws(bt, w);
                if (on == p.drawn[w]) continue;
                p.drawn[w] = on;
                foreach (var r in p.rends[w]) if (r != null) r.enabled = on;
                if (w == 0) foreach (var r in p.rooms) if (r != null) r.enabled = on && p.roomsOn;
            }
        }

        void Release(Built b)
        {
            FinishBake(true);   // its collider may be cooking: no mesh is destroyed while a bake reads it
            table.SetState(b.idx, new Vector4(3, 3, 1, 0));
            if (b.wallBase >= 0) table.ReleaseWalls(b.wallBase, b.wallCount);
            foreach (var r in b.rows) table.ReleaseRow(r);
            if (b.root != null) Kill(b.root);
            if (b.collision != null) Kill(b.collision);
            if (b.parts is Parts p)
            {
                if (p.rooms.Count > 0) { roomsKept--; p.rooms.Clear(); }
                foreach (var list in p.objs) foreach (var o in list) FreeObject(o);
                foreach (var list in p.meshes) foreach (var m in list) Kill(m);   // edit mode rebuilds often: meshes are not left behind
                b.parts = null;
            }
            byIdx.Remove(b.idx); withLod0.Remove(b);
            // its row, reset, is in its cell's until the cell is merged again
            if (b.inCell && cells.TryGetValue(b.cell, out var c)) { c.members.Remove(b.idx); dirtyCells.Add(b.cell); steadyCells.Add(b.cell); }
        }

        // the colliders being cooked on Unity's job workers, one batch at a time, and their buildings
        readonly List<(Built bt, Mesh mesh)> baking = new List<(Built, Mesh)>();
        JobHandle bakeJob; NativeArray<int> bakeIds;

        /// <summary>
        /// Every building's collider made now, and assigned before this returns, so the next physics step has them all
        /// (<see cref="AllColliders"/>): a batch being cooked is finished first, then the buildings with none have their
        /// collision meshes made on the build threads at once, from their LOD0's shape or a shape made for it alone
        /// (<see cref="CollisionMesh.BuildAll"/>, the main thread helping), and cooked together across Unity's job workers,
        /// waited for. Headless, about a millisecond a building on one desktop core before the cooking (docs/PLAY.md §5).
        /// </summary>
        public void MakeColliders()
        {
            if (!colliders || site == null) return;
            using var _t = StoreyTimings.Time("site: every collider made at once");
            FinishBake(true);
            making.Clear(); makingFor.Clear(); makingFrom.Clear();
            foreach (var bt in built.Values)
            {
                if (bt.collision != null) continue;
                colliderPending.Add(bt.id);
                if (bt.collisionMade == null && site.ById(bt.id) is BuildingData b) { making.Add(bt); makingFor.Add(b); makingFrom.Add(bt.shape); }
            }
            if (making.Count > 0)
            {
                StartThreads();
                var made = CollisionMesh.BuildAll(site, makingFor, makingFrom, BuildThreads.Shared, (b, ex) => Debug.LogException(ex));
                for (int i = 0; i < made.Length; i++)
                {
                    making[i].collisionMade = made[i];
                    if (made[i] == null) colliderPending.Remove(making[i].id);   // it threw (logged): not again on the main thread
                }
            }
            making.Clear(); makingFor.Clear(); makingFrom.Clear();
            BuildColliders();
            FinishBake(true);
        }
        // the buildings whose collision meshes MakeColliders has made at once, and what from
        readonly List<Built> making = new List<Built>();
        readonly List<BuildingData> makingFor = new List<BuildingData>();
        readonly List<Lod0Shape?> makingFrom = new List<Lod0Shape?>();

        /// <summary>
        /// The colliders of every building built since the last batch, together: their meshes made with their LOD0 on the
        /// build threads (on the main thread only when colliders are switched on, or for a kept LOD0 shown again after its
        /// collider went past the cap: 0.4 to 2 ms a building), then cooked
        /// across Unity's job workers while the frames go on, and assigned, already cooked, once the batch has finished
        /// (<see cref="FinishBake"/>). In edit mode, which only updates when asked, at once.
        /// </summary>
        void BuildColliders()
        {
            if (baking.Count > 0) return;   // the batch before is still cooking
            using var _t = StoreyTimings.Time("site: colliders");
            foreach (var id in colliderPending)
            {
                if (!built.TryGetValue(id, out var bt) || (bt.shape == null && bt.collisionMade == null) || site == null) continue;
                var b = site.ById(id); if (b == null) continue;
                RemoveCollider(bt);
                var cm = bt.collisionMade ?? CollisionMesh.Build(site, b, bt.shape!);   // made on the worker, as a rule
                bt.collisionMade = null;
                if (cm.Tris == 0) continue;
                baking.Add((bt, ToMesh(cm, named ? b.name + " collision" : "Collision")));
            }
            colliderPending.Clear();
            if (baking.Count == 0) return;
            // it may outlive the four frames a TempJob array may; filled in place, without an array of its own
            bakeIds = new NativeArray<int>(baking.Count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < baking.Count; i++) bakeIds[i] = baking[i].mesh.GetInstanceID();
            bakeJob = new BakeJob { meshes = bakeIds }.Schedule(baking.Count, 1);
            JobHandle.ScheduleBatchedJobs();
            if (!Application.isPlaying) FinishBake(true);
        }

        /// <summary>
        /// The batch of colliders being cooked, assigned once it has finished (<paramref name="now"/>: waited for). A
        /// building released or rebuilt meanwhile, or every one when <paramref name="discard"/>, has its mesh destroyed
        /// instead.
        /// </summary>
        void FinishBake(bool now, bool discard = false)
        {
            if (baking.Count == 0 || (!now && !bakeJob.IsCompleted)) return;
            using var _t = StoreyTimings.Time("site: colliders");
            bakeJob.Complete(); bakeIds.Dispose();
            foreach (var (bt, mesh) in baking)
            {
                if (discard || !colliders || bt.collision != null || !built.TryGetValue(bt.id, out var cur) || !ReferenceEquals(cur, bt)) { Kill(mesh); continue; }
                bt.collision = mesh;
                var mc = Root(bt).AddComponent<MeshCollider>();
                mc.cookingOptions = Cooking;   // before the mesh: the same options as the bake, so nothing is cooked again
                mc.sharedMesh = mesh;
            }
            baking.Clear();
            if (lods != null) TrimColliders();
        }

        /// <summary>The building's collider is in the batch being cooked.</summary>
        bool Baking(Built bt)
        {
            foreach (var (b, _) in baking) if (b == bt) return true;
            return false;
        }

        readonly List<int> collided = new List<int>(), colliderDrop = new List<int>();
        int colliderCap = int.MinValue;   // the cap last trimmed to

        /// <summary>
        /// Past the settings' cap (<see cref="LodSettings.MaxColliders"/>), the colliders of the buildings whose LOD0 went
        /// longest ago go: a collider stays when its building's LOD0 goes, and they piled up for the session (docs/CITY.md §7).
        /// One is made again with the building's next LOD0, or when its LOD0, kept meanwhile, is shown again.
        /// </summary>
        void TrimColliders()
        {
            int max = lods!.Settings.MaxColliders; if (max <= 0 || allColliders) return;
            collided.Clear(); foreach (var bt in built.Values) if (bt.collision != null) collided.Add(bt.idx);
            if (collided.Count <= max) return;
            lods.OldestDetail(collided, max, colliderDrop);
            foreach (int idx in colliderDrop) if (byIdx.TryGetValue(idx, out var bt)) RemoveCollider(bt);
        }

        struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> meshes;
#pragma warning disable CS0618   // BakeMesh(int, …): Unity 6.3 prefers an EntityId; the int form still works there and before
            public void Execute(int i) => Physics.BakeMesh(meshes[i], false, Cooking);
#pragma warning restore CS0618
        }

        void RemoveCollider(Built b)
        {
            if (b.root != null) { var mc = b.root.GetComponent<MeshCollider>(); if (mc != null) Kill(mc); }
            if (b.collision != null) { Kill(b.collision); b.collision = null; }
        }

        // ToMesh's arrays, kept from one collider to the next and grown as needed (docs/PERFORMANCE.md §3.5): two arrays a
        // collider were garbage. The mesh copies what it is given
        Vector3[] colliderV = Array.Empty<Vector3>();
        ushort[] colliderI16 = Array.Empty<ushort>();
        int[] colliderI32 = Array.Empty<int>();

        /// <summary>Positions and indices only, 16-bit when they fit; kept readable, as physics needs.</summary>
        Mesh ToMesh(CollisionMesh cm, string name)
        {
            int n = cm.P.Count;
            if (colliderV.Length < n) colliderV = new Vector3[Math.Max(n, colliderV.Length * 2)];
            var v = colliderV;
            double x0 = double.MaxValue, y0 = x0, z0 = x0, x1 = double.MinValue, y1 = x1, z1 = x1;
            for (int i = 0; i < n; i++)
            {
                var p = cm.P[i]; v[i] = new Vector3((float)p.x, (float)p.y, (float)p.z);
                x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); z0 = Math.Min(z0, p.z);
                x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); z1 = Math.Max(z1, p.z);
            }
            var m = new Mesh { name = name, hideFlags = flags };
            const UnityEngine.Rendering.MeshUpdateFlags quiet = UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices | UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds;
            m.SetVertexBufferParams(n, new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position, UnityEngine.Rendering.VertexAttributeFormat.Float32, 3));
            m.SetVertexBufferData(v, 0, 0, n, 0, quiet);
            int ni = cm.I.Count;
            if (n <= 65535)
            {
                if (colliderI16.Length < ni) colliderI16 = new ushort[Math.Max(ni, colliderI16.Length * 2)];
                var ix = colliderI16; for (int i = 0; i < ni; i++) ix[i] = (ushort)cm.I[i];
                m.SetIndexBufferParams(ni, UnityEngine.Rendering.IndexFormat.UInt16);
                m.SetIndexBufferData(ix, 0, 0, ni, quiet);
            }
            else
            {
                if (colliderI32.Length < ni) colliderI32 = new int[Math.Max(ni, colliderI32.Length * 2)];
                cm.I.CopyTo(colliderI32);
                m.SetIndexBufferParams(ni, UnityEngine.Rendering.IndexFormat.UInt32);
                m.SetIndexBufferData(colliderI32, 0, 0, ni, quiet);
            }
            m.subMeshCount = 1;
            m.SetSubMesh(0, new UnityEngine.Rendering.SubMeshDescriptor(0, ni), quiet);
            m.bounds = new Bounds(new Vector3((float)(x0 + x1) / 2, (float)(y0 + y1) / 2, (float)(z0 + z1) / 2), new Vector3((float)(x1 - x0), (float)(y1 - y0), (float)(z1 - z0)));
            return m;
        }

        static void Kill(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        void Clear()
        {
            FinishBake(true, discard: true);
            foreach (var b in built.Values) Release(b);
            built.Clear(); byIdx.Clear(); withLod0.Clear();
            steadyDirty.Clear(); steadyCells.Clear(); shownCells.Clear(); steadyAll = shownAll = true;
            foreach (var c in cells.Values) KillCell(c);
            cells.Clear(); dirtyCells.Clear();
            foreach (var j in cellJobs)
                j.task.ContinueWith(t => { if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) foreach (var p in t.Result) p.Release(); },
                    System.Threading.CancellationToken.None, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously, System.Threading.Tasks.TaskScheduler.Default);
            cellJobs.Clear();
            lods?.Clear();
            // what they make is for the old layout: they stop at their next storey, or finish unread, and give their packed
            // meshes' chunks back
            foreach (var j in jobs) j.cancelled = true;
            foreach (var j in jobs)
                j.task.ContinueWith(t => { if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) Discard(t.Result); },
                    System.Threading.CancellationToken.None, System.Threading.Tasks.TaskContinuationOptions.ExecuteSynchronously, System.Threading.Tasks.TaskScheduler.Default);
            jobs.Clear(); busy.Clear();
            foreach (var st in staged) Abandon(st);
            staged.Clear();
            load = null;
            book?.Clear();
            shownLod = -1;
        }

        public void Dispose()
        {
            Clear();
            DisposeUnwritten(true);   // every site's: their build threads finish a few meshes at most
            if (cellRoot != null) Kill(cellRoot);
            if (detailRoot != null) Kill(detailRoot);   // the objects kept for later with it
            spareObjects.Clear();
            book?.Dispose(); book = null;
            (palette as IDisposable)?.Dispose();   // Color Pipeline's palette listens for invalidations
            if (blockStart >= 0) table.ReleaseBlock(blockStart, blockSize);
            blockStart = -1; blockSize = 0;
            live.Remove(this);
            BuildingTable.ReleaseShared(table);
            if (opaqueSteady != null) Kill(opaqueSteady);
            if (massingSteady != null) Kill(massingSteady);
        }
    }
}

namespace Triband.Storey.Unity
{
    /// <summary>
    /// How the editor wants a site drawn this frame (docs/EDITOR.md slices 6.4, 6.6): the building being edited with its
    /// storey clipped and cut away as in play (SPEC principle 4), and an isolated building.
    /// </summary>
    /// <summary>The editor's walls on the active storey, after The Sims' wall modes: all down, cut away towards the camera, or up.</summary>
    public enum CutWalls { Down, Cutaway, Up }

    public struct SiteView
    {
        /// <summary>The building whose storey is shown: everything above clipY is clipped. Null for none.</summary>
        public string? activeId;
        public float clipY;
        /// <summary>The cutaway: the active storey's walls drop to a stub, as <see cref="walls"/> says.</summary>
        public bool cut;
        /// <summary>Which walls drop: all of the active storey's (steady as the camera moves), or only those between the camera and the focus.</summary>
        public CutWalls walls;
        public float stubHeight, cutBase, cutTop;
        public Vector3 camera, focus;
        public Vector2 cameraDir;
        /// <summary>The building selected in the editor: it keeps LOD0 under the automatic LOD. Null for none.</summary>
        public string? focusId;
        /// <summary>Every other building hidden. Null for none.</summary>
        public string? isolateId;
        /// <summary>How far the others have faded (0..1), for an eased isolate.</summary>
        public float isolateAmount;

        public static SiteView Neutral => new SiteView { clipY = 1e9f, stubHeight = 1 };
    }
}
