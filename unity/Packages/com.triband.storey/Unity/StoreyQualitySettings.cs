#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Per-tier quality settings for the package (Plan §6.1, §6.6). One asset per device
    /// tier; a project selects one at startup from the device model. Nothing here changes
    /// content: the same buildings, LODs and occlusion modes exist on every tier, and
    /// these knobs only trade detail distance and two shader features for memory and
    /// fill rate.
    /// </summary>
    /// <remarks>
    /// Defaults are the primary tier (desktop and iPhone 14 or newer). The minimum tier
    /// (iPhone 7) is a second asset with the values from Plan §6.6's right-hand column,
    /// and a change to these defaults needs a primary-tier reason.
    /// </remarks>
    [CreateAssetMenu(menuName = "Storey/Quality Settings", fileName = "StoreyQualitySettings")]
    public sealed class StoreyQualitySettings : ScriptableObject
    {
        [Header("Level of detail")]
        [Tooltip("On-screen feature size, in pixels per metre, below which a building drops from LOD0 to LOD1.")]
        [Min(1f)] public float lod1PixelsPerMetre = 40f;

        [Tooltip("On-screen feature size, in pixels per metre, below which a building drops from LOD1 to LOD2.")]
        [Min(1f)] public float lod2PixelsPerMetre = 12f;

        [Tooltip("How many buildings may hold LOD0 geometry at once. The LOD manager evicts least recently used ones beyond this.")]
        [Min(1)] public int lod0Residency = 24;

        [Tooltip("How many buildings may hold LOD1 geometry at once.")]
        [Min(1)] public int lod1Residency = 160;

        [Header("Occlusion")]
        [Tooltip("Compile the occlusion discard into the LOD2 and cell materials. Off on the minimum tier: alpha clipping costs early-Z on tile-based GPUs.")]
        public bool occlusionOnFarMaterials = true;

        [Header("Rendering")]
        [Tooltip("Allow URP's GPU occlusion culling for building renderers. Measured per project; off on the minimum tier.")]
        public bool gpuOcclusionCulling = true;

        [Tooltip("Target frame rate the package's budgets are measured against.")]
        [Range(30, 120)] public int targetFrameRate = 60;
    }
}
