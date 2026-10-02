#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Draws a layout: every LOD of every building generated, uploaded and shown through one building table, with one
    /// colour row per style. Rebuilds single buildings after an edit (docs/EDITOR.md §2), keeping the other buildings'
    /// meshes and colour rows. Shared by the parity harness and <see cref="StoreySite"/>; works in edit mode.
    /// </summary>
    public sealed class SiteRenderer : IDisposable
    {
        readonly Transform parent;
        readonly Material opaque, glass, massing;
        readonly HideFlags flags;
        readonly BuildingTable table = new BuildingTable();
        readonly IStoreyPalette palette = StoreyPalettes.Active;
        readonly Dictionary<string, Built> built = new Dictionary<string, Built>(StringComparer.Ordinal);
        readonly Dictionary<int, (string[] original, string[] overwrite)> remaps = new Dictionary<int, (string[], string[])>();
        ColorRowBook? book;
        Site? site;
        int lod, shownLod = -1;

        sealed class Built
        {
            public int idx, wallBase = -1, wallCount;
            public Lod0Result? l0;
            public readonly List<int> rows = new List<int>();
            public readonly List<Mesh> meshes = new List<Mesh>();
            public GameObject? root;
            public Mesh? collision;
        }

        /// <param name="flags">Edit mode passes <c>DontSave</c>: the meshes are a preview of the layout, never part of the scene file.</param>
        public SiteRenderer(Transform parent, Material opaque, Material glass, Material massing, HideFlags flags = HideFlags.None)
        {
            this.parent = parent; this.opaque = opaque; this.glass = glass; this.massing = massing; this.flags = flags;
        }

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
        readonly HashSet<string> colliderPending = new HashSet<string>(StringComparer.Ordinal);

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
            Clear();
            site = new Site(doc.buildings);
            book?.Dispose();
            book = new ColorRowBook(new ColorResolver(site), palette, table);
            foreach (var kv in remaps) book.SetRemap(kv.Key, kv.Value.original, kv.Value.overwrite);
            foreach (var b in doc.buildings) Build(b);
        }

        /// <summary>
        /// Build these buildings again (an edit or an undo changed them or their party walls); the others keep their
        /// meshes, and their colour rows are rewritten in place. The layout's buildings must be the ones shown, in order.
        /// </summary>
        public void Rebuild(StoreyDocument doc, IEnumerable<string> ids)
        {
            if (book == null || site == null || doc.buildings.Count != built.Count || doc.buildings.Any(b => !built.ContainsKey(b.id))) { Show(doc); return; }
            site = new Site(doc.buildings);
            book.Retarget(new ColorResolver(site));
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
            if (shownLod != lod)
            {
                foreach (var b in built.Values) table.SetLod(b.idx, lod, lod, 1);
                shownLod = lod;
            }
            StoreyGlobals.SetLodTint(lodTint);
            if (Occlusion != null)
            {
                Occlusion(this);
                StoreyGlobals.SetIsolate(0, -1);
                StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
                palette.Bind();
                table.Upload();
                return;
            }
            var v = view ?? SiteView.Neutral;
            int active = v.activeId != null ? TableIndexOf(v.activeId) : -1;
            StoreyGlobals.SetActive(active, active >= 0 ? v.clipY : 1e9f);
            if (active >= 0 && v.cut) { StoreyGlobals.SetCamera(v.camera, v.focus, v.cameraDir); StoreyGlobals.SetCut(true, v.stubHeight, v.cutBase, v.cutTop, v.clipY); }
            else StoreyGlobals.SetCut(false, 1, 0, 0, active >= 0 ? v.clipY : 1e9f);
            EditCutaway(active >= 0 && v.cut ? v.activeId : null, v);
            int iso = v.isolateId != null ? TableIndexOf(v.isolateId) : -1;
            StoreyGlobals.SetIsolate(iso >= 0 ? v.isolateAmount : 0, iso);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector3.zero, 2.4f);
            StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
            palette.Bind();
            table.Upload();
        }

        // the editor's wall slides, by wall id: eased over the prototype's 0.22 s, down when a wall is in the way, back up after
        readonly Dictionary<int, float> editSlide = new Dictionary<int, float>();
        float lastEdit = -1;

        /// <summary>Walls are sliding in the editor's cutaway: the editor keeps redrawing until they settle.</summary>
        public bool Animating { get; private set; }

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
            Animating = moving;
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
            // the table row is the building's index in the layout: party walls carry their neighbour's layout index, and
            // the shader looks that up in the same table
            var bt = new Built { idx = site!.IndexOf(b) };
            var root = new GameObject(b.name) { hideFlags = flags };
            root.transform.SetParent(parent, false);
            bt.root = root;

            var l0 = Lod0.Build(site!, b); bt.l0 = l0;
            bt.wallCount = l0.Op.Walls.Count; bt.wallBase = table.AllocWalls(bt.wallCount);
            if (bt.wallBase >= 0)
            {
                for (int i = 0; i < bt.wallCount; i++) table.WallData[bt.wallBase + i] = MeshUpload.WallData(l0.Op.Walls[i].W);
                table.MarkWallDataDirty();
            }
            bool opt = Application.isPlaying;   // edit mode rebuilds on every drag: skip the cache reorder there
            Add(bt, "LOD0", MeshUpload.Upload(Tagged(l0.Op, bt.idx), b.name + " LOD0", RowOf, bt.wallBase, opt, windows: true), opaque, true);
            Add(bt, "LOD0 glass", MeshUpload.Upload(Tagged(l0.Glass, bt.idx), b.name + " glass", RowOf, bt.wallBase, opt), glass, false);
            Add(bt, "LOD1", MeshUpload.Upload(Tagged(Lod1.Build(site!, b), bt.idx + Lod1.LOD_TAG), b.name + " LOD1", RowOf, -1, opt, windows: true), opaque, true);

            var l2 = Lod2.Build(site!, b);
            var rowMap = new int[l2.Rows.Count];
            for (int r = 0; r < l2.Rows.Count; r++) { rowMap[r] = table.WriteRow(l2.Rows[r], RowOf(l2.Rows[r].wall.style)); bt.rows.Add(rowMap[r]); }
            l2.Tag = bt.idx + 2 * Lod1.LOD_TAG;
            Add(bt, "LOD2", MeshUpload.Upload(l2, b.name + " LOD2", rowMap), massing, true);

            table.SetLod(bt.idx, lod, lod, 1);
            built[b.id] = bt;
            if (colliders) colliderPending.Add(b.id);
        }

        int RowOf(StyleRef s) => book!.RowOf(s);

        /// <summary>The building's tag for upload: its table row (its layout index) plus the LOD.</summary>
        static MeshBuilder Tagged(MeshBuilder gb, int tag) { gb.RetagForUpload(tag); return gb; }

        void Add(Built bt, string name, Mesh mesh, Material mat, bool shadows)
        {
            mesh.hideFlags = flags;
            bt.meshes.Add(mesh);
            var go = new GameObject(name) { hideFlags = flags };
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
            foreach (var m in b.meshes) Kill(m);   // edit mode rebuilds often: meshes are not left behind
        }

        /// <summary>
        /// The colliders of every building built since the last frame, at once: their meshes made on the main thread
        /// (a millisecond or so a building), then cooked together across the worker threads, then assigned already cooked.
        /// </summary>
        void BuildColliders()
        {
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
            if (ids.Length == 1) Physics.BakeMesh(ids[0], false, Cooking);
            else
            {
                var na = new NativeArray<int>(ids, Allocator.TempJob);
                new BakeJob { meshes = na }.Schedule(ids.Length, 1).Complete();
                na.Dispose();
            }
            foreach (var (bt, mesh) in todo)
            {
                bt.collision = mesh;
                bt.root!.layer = parent.gameObject.layer;
                var mc = bt.root.AddComponent<MeshCollider>();
                mc.cookingOptions = Cooking;   // before the mesh: the same options as the bake, so nothing is cooked again
                mc.sharedMesh = mesh;
            }
        }

        struct BakeJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> meshes;
            public void Execute(int i) => Physics.BakeMesh(meshes[i], false, Cooking);
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
            built.Clear();
            book?.Clear();
            shownLod = -1;
        }

        public void Dispose()
        {
            Clear();
            book?.Dispose(); book = null;
            (palette as IDisposable)?.Dispose();   // Color Pipeline's palette listens for invalidations
            table.Dispose();
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
        /// <summary>Every other building hidden. Null for none.</summary>
        public string? isolateId;
        /// <summary>How far the others have faded (0..1), for an eased isolate.</summary>
        public float isolateAmount;

        public static SiteView Neutral => new SiteView { clipY = 1e9f, stubHeight = 1 };
    }
}
