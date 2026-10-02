#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
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
            int iso = v.isolateId != null ? TableIndexOf(v.isolateId) : -1;
            StoreyGlobals.SetIsolate(iso >= 0 ? 1 : 0, iso);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector3.zero, 2.4f);
            StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
            palette.Bind();
            table.Upload();
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
            Add(bt, "LOD0", MeshUpload.Upload(Tagged(l0.Op, bt.idx), b.name + " LOD0", RowOf, bt.wallBase), opaque, true);
            Add(bt, "LOD0 glass", MeshUpload.Upload(Tagged(l0.Glass, bt.idx), b.name + " glass", RowOf, bt.wallBase), glass, false);
            Add(bt, "LOD1", MeshUpload.Upload(Tagged(Lod1.Build(site!, b), bt.idx + Lod1.LOD_TAG), b.name + " LOD1", RowOf), opaque, true);

            var l2 = Lod2.Build(site!, b);
            var rowMap = new int[l2.Rows.Count];
            for (int r = 0; r < l2.Rows.Count; r++) { rowMap[r] = table.WriteRow(l2.Rows[r], RowOf(l2.Rows[r].wall.style)); bt.rows.Add(rowMap[r]); }
            l2.Tag = bt.idx + 2 * Lod1.LOD_TAG;
            Add(bt, "LOD2", MeshUpload.Upload(l2, b.name + " LOD2", rowMap), massing, true);

            table.SetLod(bt.idx, lod, lod, 1);
            built[b.id] = bt;
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
            foreach (var m in b.meshes) Kill(m);   // edit mode rebuilds often: meshes are not left behind
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
    public struct SiteView
    {
        /// <summary>The building whose storey is shown: everything above clipY is clipped. Null for none.</summary>
        public string? activeId;
        public float clipY;
        /// <summary>The cutaway: walls between the camera and the focus drop to a stub on the active storey.</summary>
        public bool cut;
        public float stubHeight, cutBase, cutTop;
        public Vector3 camera, focus;
        public Vector2 cameraDir;
        /// <summary>Every other building hidden. Null for none.</summary>
        public string? isolateId;

        public static SiteView Neutral => new SiteView { clipY = 1e9f, stubHeight = 1 };
    }
}
