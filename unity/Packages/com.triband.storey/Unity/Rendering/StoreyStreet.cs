#nullable enable
using System.Collections.Generic;
using Triband.Storey.Generate;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The visual-parity harness (Plan §8, workstream 4): generates every LOD of every building in a
    /// layout, uploads the meshes and shows one LOD for all of them through the building table. Drop
    /// it on an empty GameObject with a <c>.storey</c> asset and the three Storey materials, press Play,
    /// and compare with the prototype. Not a runtime feature: the LOD manager (workstream 7) replaces
    /// the fixed LOD, and districts replace the whole-layout build.
    /// </summary>
    [AddComponentMenu("Storey/Storey Street (parity harness)")]
    public sealed class StoreyStreet : MonoBehaviour
    {
        [Tooltip("The layout to build, imported from the prototype.")]
        public StoreyDocumentAsset? layout;
        [Tooltip("Storey/Opaque")] public Material? opaque;
        [Tooltip("Storey/Glass")] public Material? glass;
        [Tooltip("Storey/Massing")] public Material? massing;
        [Tooltip("Which LOD every building shows: 0 full, 1 shell, 2 massing.")]
        [Range(0, 2)] public int displayedLod = 0;
        [Tooltip("Tint by LOD, as the prototype's stats card does.")]
        public bool lodTint;

        BuildingTable? table;
        readonly List<Built> built = new List<Built>();
        // one colour row per style the meshes name (docs/COLOURS.md §3.3), rewritten when the palette invalidates
        ColorRowBook? book;
        IStoreyPalette? palette;
        readonly Dictionary<int, (string[] original, string[] overwrite)> remaps = new Dictionary<int, (string[], string[])>();
        int shownLod = -1;

        sealed class Built
        {
            public int idx; public int wallBase, wallCount; public List<int> rows = new List<int>();
            public GameObject? root;
        }

        void Start() { Rebuild(); }

        void OnDestroy() { Clear(); book?.Dispose(); book = null; table?.Dispose(); table = null; }

        void LateUpdate()
        {
            if (table == null) return;
            if (shownLod != displayedLod)
            {
                foreach (var b in built) table.SetLod(b.idx, displayedLod, displayedLod, 1);
                shownLod = displayedLod;
            }
            StoreyGlobals.SetLodTint(lodTint);
            StoreyGlobals.SetActive(-1, 1e9f);
            StoreyGlobals.SetCut(false, 1, 0, 0, 1e9f);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector2.zero, 1, 100);
            StoreyGlobals.SetCap(new Color(0.23f, 0.25f, 0.24f));
            palette?.Bind();
            table.Upload();
        }

        /// <summary>Generate and upload everything again (after the layout or the materials changed).</summary>
        public void Rebuild()
        {
            Clear();
            table ??= new BuildingTable();
            if (layout == null || opaque == null || glass == null || massing == null) return;
            var doc = layout.Document;
            var site = new Site(doc.buildings);
            palette ??= StoreyPalettes.Active;
            book?.Dispose();
            book = new ColorRowBook(new ColorResolver(site), palette, table);
            foreach (var kv in remaps) book.SetRemap(kv.Key, kv.Value.original, kv.Value.overwrite);
            foreach (var b in doc.buildings)
            {
                var bt = new Built { idx = table.AllocIndex() };
                var root = new GameObject(b.name);
                root.transform.SetParent(transform, false);
                bt.root = root;

                var l0 = Lod0.Build(site, b);
                bt.wallCount = l0.Op.Walls.Count; bt.wallBase = table.AllocWalls(bt.wallCount);
                Add(root, "LOD0", MeshUpload.Upload(Tagged(l0.Op, bt.idx), b.name + " LOD0", RowOf, bt.wallBase), opaque, true);
                Add(root, "LOD0 glass", MeshUpload.Upload(Tagged(l0.Glass, bt.idx), b.name + " glass", RowOf, bt.wallBase), glass, false);
                Add(root, "LOD1", MeshUpload.Upload(Tagged(Lod1.Build(site, b), bt.idx + Lod1.LOD_TAG), b.name + " LOD1", RowOf), opaque, true);

                var l2 = Lod2.Build(site, b);
                var rowMap = new int[l2.Rows.Count];
                for (int r = 0; r < l2.Rows.Count; r++) { rowMap[r] = table.WriteRow(l2.Rows[r], RowOf(l2.Rows[r].wall.style)); bt.rows.Add(rowMap[r]); }
                l2.Tag = bt.idx + 2 * Lod1.LOD_TAG;
                Add(root, "LOD2", MeshUpload.Upload(l2, b.name + " LOD2", rowMap), massing, true);

                built.Add(bt);
            }
            shownLod = -1;
        }

        int RowOf(StyleRef s) => book!.RowOf(s);

        /// <summary>
        /// Remap building <paramref name="building"/>'s colours (its index in the layout; palette ids, pairwise), as a
        /// <c>ColorRemap</c> would a renderer's. Needs Color Pipeline. Empty arrays clear it. No mesh is rebuilt.
        /// </summary>
        public void SetRemap(int building, string[] original, string[] overwrite)
        {
            if (original.Length == 0) remaps.Remove(building); else remaps[building] = (original, overwrite);
            book?.SetRemap(building, original, overwrite);
        }

        /// <summary>The generator tags meshes with the site index; the table hands out its own, so retag before upload.</summary>
        static MeshBuilder Tagged(MeshBuilder gb, int tag) { gb.RetagForUpload(tag); return gb; }

        static void Add(GameObject root, string name, Mesh mesh, Material mat, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;   // the GPU Resident Drawer wants no probes on the renderer
        }

        void Clear()
        {
            if (table != null)
                foreach (var b in built)
                {
                    table.ReleaseIndex(b.idx);
                    if (b.wallBase >= 0) table.ReleaseWalls(b.wallBase, b.wallCount);
                    foreach (var r in b.rows) table.ReleaseRow(r);
                    if (b.root != null) Destroy(b.root);
                }
            built.Clear();
            book?.Clear();
        }
    }
}
