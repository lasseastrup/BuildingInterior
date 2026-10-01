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

        [Header("Remap test (needs Color Pipeline; docs/COLOURS.md §3.6)")]
        [Tooltip("The building to remap: its index in the layout.")]
        public int remapBuilding;
        [Tooltip("Palette ids to replace, comma-separated (32 hex digits each).")]
        public string remapFrom = "";
        [Tooltip("Palette ids to show instead, pairwise with the ones above.")]
        public string remapTo = "";

        SiteRenderer? site;

        void Start() { Rebuild(); }

        void OnDestroy() { site?.Dispose(); site = null; }

        void LateUpdate()
        {
            if (site == null) return;
            site.Lod = displayedLod;
            site.Frame(lodTint);
        }

        /// <summary>Generate and upload everything again (after the layout or the materials changed).</summary>
        public void Rebuild()
        {
            site?.Dispose(); site = null;
            if (layout == null || opaque == null || glass == null || massing == null) return;
            site = new SiteRenderer(transform, opaque, glass, massing);
            foreach (var kv in remaps) site.SetRemap(kv.Key, kv.Value.original, kv.Value.overwrite);
            site.Show(layout.Document);
        }

        readonly Dictionary<int, (string[] original, string[] overwrite)> remaps = new Dictionary<int, (string[], string[])>();

        static string[] Ids(string s) => s.Split(new[] { ',', ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

        /// <summary>Play mode: apply the inspector's remap test. Empty fields clear it. No mesh is rebuilt.</summary>
        [ContextMenu("Apply remap")]
        void ApplyRemapFromInspector() => SetRemap(remapBuilding, Ids(remapFrom), Ids(remapTo));

        /// <summary>
        /// Remap building <paramref name="building"/>'s colours (its index in the layout; palette ids, pairwise), as a
        /// <c>ColorRemap</c> would a renderer's. Needs Color Pipeline. Empty arrays clear it. No mesh is rebuilt.
        /// </summary>
        public void SetRemap(int building, string[] original, string[] overwrite)
        {
            if (original.Length == 0) remaps.Remove(building); else remaps[building] = (original, overwrite);
            site?.SetRemap(building, original, overwrite);
        }
    }
}
