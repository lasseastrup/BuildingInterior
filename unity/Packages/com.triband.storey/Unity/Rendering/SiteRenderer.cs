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
            public Lod0Result? l0;
            public readonly List<int> rows = new List<int>();
            // per LOD: the meshes and the objects drawing them
            public readonly List<Mesh>[] meshes = { new List<Mesh>(), new List<Mesh>(), new List<Mesh>() };
            public readonly List<GameObject>[] objs = { new List<GameObject>(), new List<GameObject>(), new List<GameObject>() };
            public GameObject? root;
            public Mesh? collision;
            // automatic LOD: the massing (drawn in its cell) with its rows' places in the table, and the cell
            public Lod2Mesh? l2;
            public int[]? rowMap;
            public (int x, int z) cell;
        }

        /// <param name="flags">Edit mode passes <c>DontSave</c>: the meshes are a preview of the layout, never part of the scene file.</param>
        /// <param name="auto">The automatic LOD's settings; null for the fixed LOD (every LOD built, <see cref="Lod"/> shown).</param>
        public SiteRenderer(Transform parent, Material opaque, Material glass, Material massing, HideFlags flags = HideFlags.None, LodSettings? auto = null)
        {
            this.parent = parent; layer = parent.gameObject.layer; this.opaque = opaque; this.glass = glass; this.massing = massing; this.flags = flags;
            if (auto != null) lods = new LodManager(auto);
        }

        // ---- the automatic LOD (docs/CITY.md §2, §3) ----
        readonly LodManager? lods;
        readonly Dictionary<int, Built> byIdx = new Dictionary<int, Built>();
        readonly Dictionary<(int x, int z), Cell> cells = new Dictionary<(int, int), Cell>();
        readonly HashSet<(int x, int z)> dirtyCells = new HashSet<(int, int)>();
        readonly HashSet<int> forcedIdx = new HashSet<int>();
        readonly Dictionary<string, int> carry = new Dictionary<string, int>(StringComparer.Ordinal);
        // detail being generated on worker threads: one at a time per building, uploaded when done
        readonly List<Job> jobs = new List<Job>();
        readonly HashSet<int> busy = new HashSet<int>();

        sealed class Job
        {
            public Built bt = null!; public Site site = null!; public string name = ""; public int which;
            public System.Threading.Tasks.Task<object> task = null!;
        }
        GameObject? cellRoot;
        float lastLod = -1;
        bool lodBusy;
        LodStats stats;

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
        }

        /// <summary>The camera the automatic LOD picks for: where it is (world space) and how many pixels a metre covers a metre away.</summary>
        public struct LodEye
        {
            public Vector3 position;
            public float pixelsPerMetre;

            /// <summary>A camera's eye. An orthographic one is treated as a perspective one far behind it, so detail goes by its zoom.</summary>
            public static LodEye Of(Camera c)
            {
                if (!c.orthographic)
                    return new LodEye { position = c.transform.position, pixelsPerMetre = (float)LodManager.PixelsPerMetre(c.pixelHeight, c.fieldOfView) };
                const float back = 200;
                return new LodEye { position = c.transform.position - c.transform.forward * back, pixelsPerMetre = c.pixelHeight / (2 * Mathf.Max(0.01f, c.orthographicSize)) * back };
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
            /// <summary>Detail LODs wanted and not started yet, being generated on worker threads, and uploaded this frame.</summary>
            public int queued, generating, builtThisFrame;
            /// <summary>Milliseconds the LOD work took this frame (picking, building, cells).</summary>
            public double ms;

            public override string ToString() =>
                $"{buildings:N0} buildings: LOD0 {lod0}, LOD1 {lod1}, LOD2 {lod2}, not drawn {culled}\n" +
                $"Resident: LOD0 {resident0}, LOD1 {resident1}\n" +
                $"Cells: {cellsShown} of {cells} drawn ({cellMeshes} meshes)\n" +
                $"Built this frame {builtThisFrame}, generating {generating}, waiting {queued}; LOD {ms:0.00} ms";
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

        Site NewSite(StoreyDocument doc) => Neighbours.Count == 0 ? new Site(doc.buildings) : Site.WithContext(doc.buildings, District, Neighbours);

        /// <summary>The automatic LOD's camera this frame. Null keeps every building at the LOD it shows.</summary>
        public LodEye? Eye { get; set; }

        /// <summary>
        /// Buildings that always have LOD0 under the automatic LOD (being edited, walked in, in the way of the camera):
        /// built at once when they need it, never shown as their massing. The caller fills it each frame.
        /// </summary>
        public readonly HashSet<string> Forced = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The automatic LOD's numbers from the last frame.</summary>
        public LodStats Stats => stats;

        /// <summary>Which LOD every building shows: 0 full, 1 shell, 2 massing.</summary>
        public int Lod { get => lod; set => lod = Math.Max(0, Math.Min(2, value)); }

        /// <summary>The building table's index of a building, for the shader globals (isolate, active building); -1 when not shown.</summary>
        public int TableIndexOf(string id) => built.TryGetValue(id, out var b) ? b.idx : -1;

        /// <summary>The layout as built (null before <see cref="Show"/>). A new one after every change.</summary>
        public Site? Site => site;

        /// <summary>A building's LOD0 as built: its collision segments and its wall list (for the cutaway).</summary>
        public Lod0Result? Lod0Of(string id) => built.TryGetValue(id, out var b) ? b.l0 : null;

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
                    foreach (var list in bt.objs) foreach (var go in list) if (go != null) go.layer = value;
                }
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
                if (value) foreach (var id in built.Keys) colliderPending.Add(id);
                else { colliderPending.Clear(); foreach (var b in built.Values) RemoveCollider(b); }
            }
        }

        /// <summary>
        /// Baked once with these and given to the collider with the same, so assigning it cooks nothing: no cleaning or
        /// welding (the mesh is welded and has no degenerate triangles already), the fast midphase where the platform has it.
        /// </summary>
        public const MeshColliderCookingOptions Cooking = MeshColliderCookingOptions.UseFastMidphase;

        /// <summary>Build everything again.</summary>
        public void Show(StoreyDocument doc)
        {
            using var _t = StoreyTimings.Time("site: full rebuild (every building)");
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
            site = NewSite(doc);
            book?.Dispose();
            book = new ColorRowBook(new ColorResolver(site), palette, table);
            foreach (var kv in remaps) book.SetRemap(kv.Key, kv.Value.original, kv.Value.overwrite);
            foreach (var b in doc.buildings) Build(b);
            carry.Clear();
        }

        /// <summary>
        /// Build these buildings again (an edit or an undo changed them or their party walls); the others keep their
        /// meshes, and their colour rows are rewritten in place. The layout's buildings must be the ones shown, in order.
        /// </summary>
        public void Rebuild(StoreyDocument doc, IEnumerable<string> ids)
        {
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
        }

        /// <summary>Per frame: the view globals (neutral unless the editor set a view), the palette, the LODs, the table upload.</summary>
        public void Frame(bool lodTint, SiteView? view = null)
        {
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
                float at = (float)lods.Settings.Lod0Distance(ge.pixelsPerMetre), clear = (float)Math.Min(1, Math.Max(0, lods.Settings.GlassClear));
                StoreyGlobals.SetGlassFill(at * clear, clear >= 1 ? 0 : at);
            }
            else StoreyGlobals.SetGlassFill(0, 0);
            StoreyGlobals.SetLodTint(lodTint);
            // the view globals (active building, cutaway, isolate, occlusion) are one set for every site: written by the
            // site that has a view (the editor's, or the occlusion system's), and neutral only when none has
            var v0 = view ?? SiteView.Neutral;
            claims = Occlusion != null || v0.activeId != null || v0.isolateId != null;
            live.Add(this);
            bool writeView = claims || !OtherClaims();
            if (Occlusion != null)
            {
                Occlusion(this);
                StoreyGlobals.SetIsolate(0, -1);
                StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
                palette.Bind();
                table.Upload();
                return;
            }
            var v = v0;
            EditCutaway(v.cut && v.activeId != null && TableIndexOf(v.activeId) >= 0 ? v.activeId : null, v);
            if (!writeView) { palette.Bind(); table.Upload(); return; }
            int active = v.activeId != null ? TableIndexOf(v.activeId) : -1;
            StoreyGlobals.SetActive(active, active >= 0 ? v.clipY : 1e9f);
            if (active >= 0 && v.cut) { StoreyGlobals.SetCamera(v.camera, v.focus, v.cameraDir); StoreyGlobals.SetCut(true, v.stubHeight, v.cutBase, v.cutTop, v.clipY); }
            else StoreyGlobals.SetCut(false, 1, 0, 0, active >= 0 ? v.clipY : 1e9f);
            int iso = v.isolateId != null ? TableIndexOf(v.isolateId) : -1;
            StoreyGlobals.SetIsolate(iso >= 0 ? v.isolateAmount : 0, iso);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector3.zero, 2.4f);
            StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
            palette.Bind();
            table.Upload();
        }

        // every renderer drawing now, and whether it owns the view globals (claims)
        static readonly HashSet<SiteRenderer> live = new HashSet<SiteRenderer>();
        bool claims;
        bool OtherClaims() { foreach (var r in live) if (r != this && r.claims) return true; return false; }

        // the editor's wall slides, by wall id: eased over the prototype's 0.22 s, down when a wall is in the way, back up after
        readonly Dictionary<int, float> editSlide = new Dictionary<int, float>();
        float lastEdit = -1;

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
            var down = new HashSet<int>();
            var act = activeId != null && site != null ? site.ById(activeId) : null;
            if (act != null)
            {
                int ai = site!.IndexOf(act);
                foreach (var bt in built)
                {
                    if (bt.Value.l0 == null || bt.Value.wallBase < 0) continue;
                    var walls = bt.Value.l0.Op.Walls; var bases = bt.Value.l0.Op.WallBase; bool own = bt.Key == act.id;
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
            bool changed = false, moving = false;
            foreach (int id in down) if (!editSlide.ContainsKey(id)) editSlide[id] = 0;
            foreach (var id in new List<int>(editSlide.Keys))
            {
                float a = editSlide[id], to = down.Contains(id) ? 1 : 0;
                float n = to > a ? Mathf.Min(to, a + step) : Mathf.Max(to, a - step);
                if (n != a) changed = true;
                if (n != to) moving = true;
                table.Wall[id] = n;
                if (n <= 0 && to == 0) editSlide.Remove(id); else editSlide[id] = n;
            }
            wallsMoving = moving;
            if (changed) table.MarkWallDirty();
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

        void Build(BuildingData b)
        {
            // the table row is the site's block plus the building's index in the layout: party walls carry their
            // neighbour's layout index, moved into the block at upload (MeshUpload's mates), and the shader looks it up
            var bt = new Built { id = b.id, idx = blockStart + site!.IndexOf(b) };
            var root = new GameObject(b.name) { hideFlags = flags, layer = layer };
            root.transform.SetParent(parent, false);
            bt.root = root;
            built[b.id] = bt; byIdx[bt.idx] = bt;

            StoreyTimings.Count("buildings built");
            var t2 = StoreyTimings.Time("build: massing and its rows");
            var l2 = Lod2.Build(site!, b);
            var rowMap = new int[l2.Rows.Count];
            for (int r = 0; r < l2.Rows.Count; r++) { rowMap[r] = table.WriteRow(l2.Rows[r], RowOf(l2.Rows[r].wall.style)); bt.rows.Add(rowMap[r]); }
            l2.Tag = bt.idx + 2 * Lod1.LOD_TAG;
            t2.Dispose();

            if (lods == null)
            {
                BuildDetail(bt, b, 0); BuildDetail(bt, b, 1);
                Add(bt, 2, "LOD2", MeshUpload.Upload(l2, b.name + " LOD2", rowMap), massing, true);
                table.SetLod(bt.idx, lod, lod, 1);
                if (colliders) colliderPending.Add(b.id);
                return;
            }

            // automatic: the massing goes into its cell, and the detail the building showed (an edit) is built again at
            // once, so it doesn't fade through its massing
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
            c.members[bt.idx] = bt; dirtyCells.Add(bt.cell);
            if (e.Visible(1)) { BuildDetail(bt, b, 1); lods.SetBuilt(bt.idx, 1, true); }
            if (e.Visible(0) || forced) { BuildDetail(bt, b, 0); lods.SetBuilt(bt.idx, 0, true); }
            table.SetLod(bt.idx, e.Shown, e.From, (float)e.T);
        }

        /// <summary>A building's LOD0 (with its glass and its walls in the table) or LOD1, generated and uploaded now.</summary>
        void BuildDetail(Built bt, BuildingData b, int which)
        {
            object made;
            using (StoreyTimings.Time(which == 0 ? "build: LOD0 on the main thread" : "build: LOD1 on the main thread")) made = Generate(site!, b, bt.idx, which);
            FinishDetail(bt, b.name, which, made);
        }

        /// <summary>
        /// The engine-free half of a detail build: generated, tagged and welded. Safe on a worker thread (the generator's
        /// memos are per site and thread-safe; ParallelBuildTests).
        /// </summary>
        static object Generate(Site site, BuildingData b, int idx, int which)
        {
            if (which == 1) { var m = Lod1.Build(site, b); m.RetagForUpload(idx + Lod1.LOD_TAG); m.Weld(); return m; }
            var l0 = Lod0.Build(site, b);
            l0.Op.RetagForUpload(idx); l0.Op.Weld(); l0.Glass.RetagForUpload(idx); l0.Glass.Weld();
            return l0;
        }

        /// <summary>The main thread's half: the walls into the table, the meshes uploaded and drawn.</summary>
        void FinishDetail(Built bt, string name, int which, object made)
        {
            using var _t = StoreyTimings.Time(which == 0 ? "build: LOD0 upload" : "build: LOD1 upload");
            bool opt = Application.isPlaying;   // edit mode rebuilds on every drag: skip the cache reorder there
            if (which == 1)
            {
                var m1 = (MeshBuilder)made;
                if (m1.Tris > 0) Add(bt, 1, "LOD1", MeshUpload.Upload(m1, name + " LOD1", RowOf, -1, opt, windows: true), opaque, true);
                return;
            }
            var l0 = (Lod0Result)made; bt.l0 = l0;
            bt.wallCount = l0.Op.Walls.Count; bt.wallBase = table.AllocWalls(bt.wallCount);
            if (bt.wallBase >= 0)
            {
                for (int i = 0; i < bt.wallCount; i++) table.WallData[bt.wallBase + i] = MeshUpload.WallData(l0.Op.Walls[i].W);
                table.MarkWallDataDirty();
            }
            // a shell building's LOD0 has no see-through glass: no mesh for it
            var mates = (blockStart, blockSize);
            if (l0.Op.Tris > 0) Add(bt, 0, "LOD0", MeshUpload.Upload(l0.Op, name + " LOD0", RowOf, bt.wallBase, opt, windows: true, mates: mates), opaque, true);
            if (l0.Glass.Tris > 0) Add(bt, 0, "LOD0 glass", MeshUpload.Upload(l0.Glass, name + " glass", RowOf, bt.wallBase, opt, windows: true, mates: mates), glass, false);   // its panes carry their place, to start as LOD1's
            // automatic: a building's collider comes with its first LOD0 and stays when the LOD0 is dropped
            if (lods != null && colliders && bt.collision == null) colliderPending.Add(bt.id);
        }

        int Threads => lods == null ? 0 : lods.Settings.Threads >= 0 ? lods.Settings.Threads : Math.Max(1, Environment.ProcessorCount - 1);

        /// <summary>
        /// Detail finished on the worker threads, uploaded within the budget (the rest wait for the next frame). A result
        /// for a layout or a building since rebuilt, or one no longer wanted, is thrown away.
        /// </summary>
        int TakeJobs(System.Diagnostics.Stopwatch sw, int made)
        {
            var l = lods!;
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i]; if (!j.task.IsCompleted) continue;
                bool ok = j.task.Status == System.Threading.Tasks.TaskStatus.RanToCompletion && ReferenceEquals(j.site, site)
                    && built.TryGetValue(j.bt.id, out var cur) && ReferenceEquals(cur, j.bt);
                var e = ok ? l.Get(j.bt.idx) : null;
                if (e == null || (j.which == 0 ? e.Has0 : e.Has1) || e.Want > j.which) ok = false;
                if (ok && made > 0 && sw.Elapsed.TotalMilliseconds > l.Settings.BudgetMs) continue;   // next frame
                jobs.RemoveAt(i--); busy.Remove(j.bt.idx);
                if (j.task.IsFaulted) _ = j.task.Exception;   // observed: a build of a layout edited under it may throw, and is dropped
                if (!ok) continue;
                FinishDetail(j.bt, j.name, j.which, j.task.Result); l.SetBuilt(j.bt.idx, j.which, true); made++;
            }
            return made;
        }

        /// <summary>Drop a building's LOD0 (and its walls) or LOD1; its collider stays.</summary>
        void DropDetail(Built bt, int which)
        {
            foreach (var go in bt.objs[which]) Kill(go);
            foreach (var m in bt.meshes[which]) Kill(m);
            bt.objs[which].Clear(); bt.meshes[which].Clear();
            if (which != 0) return;
            if (bt.wallBase >= 0) table.ReleaseWalls(bt.wallBase, bt.wallCount);
            bt.wallBase = -1; bt.wallCount = 0; bt.l0 = null;
        }

        /// <summary>
        /// The automatic LOD's frame: forced buildings' LOD0 built at once, then each building's LOD picked from the
        /// camera, meshes dropped past the caps, detail built most-pixels-first within the budget, the changed buildings'
        /// state written, and the cells built again where a building changed and shown where any shows its massing.
        /// </summary>
        readonly System.Diagnostics.Stopwatch lodClock = new System.Diagnostics.Stopwatch();

        // its own method: a lambda capturing the build loop's locals made its closure on every pass of the loop, job or not
        // (hundreds of buildings a frame wanting detail: about 14 KB of garbage a frame)
        void StartJob(Built bt, BuildingData b, int which)
        {
            var s0 = site!; int ix = bt.idx;
            jobs.Add(new Job { bt = bt, site = s0, name = b.name, which = which, task = System.Threading.Tasks.Task.Run(() => Generate(s0, b, ix, which)) });
        }

        void UpdateLods()
        {
            using var _t = StoreyTimings.Time("site: automatic LOD");
            var sw = lodClock; sw.Restart();
            float now = Time.realtimeSinceStartup, dt = lastLod < 0 ? 0 : Mathf.Min(0.1f, now - lastLod); lastLod = now;
            var l = lods!; int made = 0, queued = 0; bool fading = false;
            forcedIdx.Clear();
            foreach (var id in Forced)
            {
                if (!built.TryGetValue(id, out var bt)) continue;
                forcedIdx.Add(bt.idx);
                var e = l.Get(bt.idx); var b = site?.ById(id);
                if (e != null && !e.Has0 && b != null) { BuildDetail(bt, b, 0); l.SetBuilt(bt.idx, 0, true); made++; }
            }
            int threads = Threads;
            if (jobs.Count > 0) made = TakeJobs(sw, made);
            if (Eye is LodEye eye)
            {
                var o = parent.position;   // the layout is in the site's space: unrotated, unscaled
                var f = l.Update(eye.position.x - o.x, eye.position.y - o.y, eye.position.z - o.z, eye.pixelsPerMetre, dt, forcedIdx);
                foreach (var (idx, which) in f.Drop) if (byIdx.TryGetValue(idx, out var bt)) DropDetail(bt, which);
                for (int bi = 0; bi < f.Build.Count; bi++)
                {
                    var (idx, which, _) = f.Build[bi];
                    if (busy.Contains(idx)) continue;   // being generated
                    if (threads > 0 && jobs.Count >= threads) { queued += f.Build.Count - bi; break; }   // the rest wait for a thread
                    if (!byIdx.TryGetValue(idx, out var bt)) continue;
                    var b = site?.ById(bt.id); if (b == null) continue;
                    if (threads > 0)
                    {
                        // most pixels first, as many at once as there are threads; the upload comes in a later frame
                        StartJob(bt, b, which); busy.Add(idx);
                        continue;
                    }
                    if (made > 0 && sw.Elapsed.TotalMilliseconds > l.Settings.BudgetMs) { queued++; continue; }
                    BuildDetail(bt, b, which); l.SetBuilt(idx, which, true); made++;
                }
                foreach (int idx in f.Changed)
                {
                    var e = l.Get(idx)!; table.SetLod(idx, e.Shown, e.From, (float)e.T);
                    if (e.T < 1) fading = true;
                }
                stats.lod0 = f.Count[0]; stats.lod1 = f.Count[1]; stats.lod2 = f.Count[2]; stats.culled = f.Count[3];
            }
            foreach (var key in dirtyCells) BuildCell(key);
            dirtyCells.Clear();
            int shown = 0, pieces = 0;
            foreach (var c in cells.Values)
            {
                bool on = false;
                foreach (int idx in c.order) { var e = l.Get(idx); if (e == null || e.Visible(2)) { on = true; break; } }
                if (on != c.shown) { c.shown = on; foreach (var r in c.renderers) r.enabled = on; }
                if (on) shown++;
                pieces += c.renderers.Count;
            }
            var (r0, r1) = l.Resident();
            lodBusy = fading || queued > 0 || jobs.Count > 0;
            stats.buildings = built.Count; stats.resident0 = r0; stats.resident1 = r1;
            stats.cells = cells.Count; stats.cellMeshes = pieces; stats.cellsShown = shown;
            stats.queued = queued; stats.generating = jobs.Count; stats.builtThisFrame = made; stats.ms = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>A cell's massings merged again (a member was built, rebuilt or removed).</summary>
        void BuildCell((int x, int z) key)
        {
            using var _t = StoreyTimings.Time("site: cells merged");
            StoreyTimings.Count("cells merged");
            if (!cells.TryGetValue(key, out var c)) return;
            foreach (var r in c.renderers) if (r != null) Kill(r.gameObject);
            foreach (var m in c.meshes) Kill(m);
            c.renderers.Clear(); c.meshes.Clear();
            if (c.members.Count == 0) { cells.Remove(key); return; }
            c.order = c.members.Keys.ToArray();
            if (cellRoot == null) { cellRoot = new GameObject("Cells") { hideFlags = flags, layer = layer }; cellRoot.transform.SetParent(parent, false); }
            var pieces = Cells.Merge(c.members.Values.Select(bt => new Cells.Member(bt.l2!, bt.idx + 2 * Lod1.LOD_TAG, bt.rowMap!)));
            for (int i = 0; i < pieces.Count; i++)
            {
                string name = $"Cell {key.x},{key.z}" + (pieces.Count > 1 ? $" ({i + 1})" : "");
                var mesh = MeshUpload.Upload(pieces[i], name, null); mesh.hideFlags = flags;
                c.meshes.Add(mesh);
                var go = new GameObject(name) { hideFlags = flags, layer = layer };
                go.transform.SetParent(cellRoot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = massing;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.enabled = c.shown;
                c.renderers.Add(mr);
            }
        }

        int RowOf(StyleRef s) => book!.RowOf(s);

        void Add(Built bt, int which, string name, Mesh mesh, Material mat, bool shadows)
        {
            mesh.hideFlags = flags;
            bt.meshes[which].Add(mesh);
            var go = new GameObject(name) { hideFlags = flags, layer = layer };
            bt.objs[which].Add(go);
            go.transform.SetParent(bt.root!.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;   // the GPU Resident Drawer wants no probes on the renderer
        }

        void Release(Built b)
        {
            table.State[b.idx] = new Vector4(3, 3, 1, 0); table.MarkStateDirty();
            if (b.wallBase >= 0) table.ReleaseWalls(b.wallBase, b.wallCount);
            foreach (var r in b.rows) table.ReleaseRow(r);
            if (b.root != null) Kill(b.root);
            if (b.collision != null) Kill(b.collision);
            foreach (var list in b.meshes) foreach (var m in list) Kill(m);   // edit mode rebuilds often: meshes are not left behind
            byIdx.Remove(b.idx);
            if (b.l2 != null && cells.TryGetValue(b.cell, out var c)) { c.members.Remove(b.idx); dirtyCells.Add(b.cell); }
        }

        /// <summary>
        /// The colliders of every building built since the last frame, at once: their meshes made on the main thread
        /// (a millisecond or so a building), then cooked together across the worker threads, then assigned already cooked.
        /// </summary>
        void BuildColliders()
        {
            using var _t = StoreyTimings.Time("site: colliders");
            var todo = new List<(Built bt, Mesh mesh)>();
            foreach (var id in colliderPending)
            {
                if (!built.TryGetValue(id, out var bt) || bt.l0 == null || bt.root == null || site == null) continue;
                var b = site.ById(id); if (b == null) continue;
                RemoveCollider(bt);
                var cm = CollisionMesh.Build(site, b, bt.l0);
                if (cm.Tris == 0) continue;
                todo.Add((bt, ToMesh(cm, b.name + " collision")));
            }
            colliderPending.Clear();
            if (todo.Count == 0) return;
            var ids = new int[todo.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = todo[i].mesh.GetInstanceID();
#pragma warning disable CS0618   // BakeMesh(int, …): Unity 6.3 prefers an EntityId; the int form still works there and before
            if (ids.Length == 1) Physics.BakeMesh(ids[0], false, Cooking);
#pragma warning restore CS0618
            else
            {
                var na = new NativeArray<int>(ids, Allocator.TempJob);
                new BakeJob { meshes = na }.Schedule(ids.Length, 1).Complete();
                na.Dispose();
            }
            foreach (var (bt, mesh) in todo)
            {
                bt.collision = mesh;
                var mc = bt.root.AddComponent<MeshCollider>();
                mc.cookingOptions = Cooking;   // before the mesh: the same options as the bake, so nothing is cooked again
                mc.sharedMesh = mesh;
            }
        }

        struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> meshes;
#pragma warning disable CS0618   // as above
            public void Execute(int i) => Physics.BakeMesh(meshes[i], false, Cooking);
#pragma warning restore CS0618
        }

        void RemoveCollider(Built b)
        {
            if (b.root != null) { var mc = b.root.GetComponent<MeshCollider>(); if (mc != null) Kill(mc); }
            if (b.collision != null) { Kill(b.collision); b.collision = null; }
        }

        /// <summary>Positions and indices only, 16-bit when they fit; kept readable, as physics needs.</summary>
        Mesh ToMesh(CollisionMesh cm, string name)
        {
            int n = cm.P.Count;
            var v = new Vector3[n];
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
                var ix = new ushort[ni]; for (int i = 0; i < ni; i++) ix[i] = (ushort)cm.I[i];
                m.SetIndexBufferParams(ni, UnityEngine.Rendering.IndexFormat.UInt16);
                m.SetIndexBufferData(ix, 0, 0, ni, quiet);
            }
            else
            {
                m.SetIndexBufferParams(ni, UnityEngine.Rendering.IndexFormat.UInt32);
                m.SetIndexBufferData(cm.I.ToArray(), 0, 0, ni, quiet);
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
            foreach (var b in built.Values) Release(b);
            built.Clear(); byIdx.Clear();
            foreach (var key in new List<(int, int)>(cells.Keys)) { cells[key].members.Clear(); BuildCell(key); }
            cells.Clear(); dirtyCells.Clear();
            lods?.Clear();
            jobs.Clear(); busy.Clear();   // what they make is for the old layout: let them finish unread
            book?.Clear();
            shownLod = -1;
        }

        public void Dispose()
        {
            Clear();
            if (cellRoot != null) Kill(cellRoot);
            book?.Dispose(); book = null;
            (palette as IDisposable)?.Dispose();   // Color Pipeline's palette listens for invalidations
            if (blockStart >= 0) table.ReleaseBlock(blockStart, blockSize);
            blockStart = -1; blockSize = 0;
            live.Remove(this);
            BuildingTable.ReleaseShared(table);
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
