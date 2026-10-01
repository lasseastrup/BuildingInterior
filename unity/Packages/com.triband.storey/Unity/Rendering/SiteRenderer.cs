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

        /// <summary>Per frame: the neutral view globals (no player, no cutaway), the palette, the LODs, the table upload.</summary>
        public void Frame(bool lodTint)
        {
            if (shownLod != lod)
            {
                foreach (var b in built.Values) table.SetLod(b.idx, lod, lod, 1);
                shownLod = lod;
            }
            StoreyGlobals.SetLodTint(lodTint);
            StoreyGlobals.SetActive(-1, 1e9f);
            StoreyGlobals.SetCut(false, 1, 0, 0, 1e9f);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector2.zero, 1, 100);
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
            var bt = new Built { idx = table.AllocIndex() };
            var root = new GameObject(b.name) { hideFlags = flags };
            root.transform.SetParent(parent, false);
            bt.root = root;

            var l0 = Lod0.Build(site!, b);
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

        /// <summary>The generator tags meshes with the site index; the table hands out its own, so retag before upload.</summary>
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
            table.ReleaseIndex(b.idx);
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
