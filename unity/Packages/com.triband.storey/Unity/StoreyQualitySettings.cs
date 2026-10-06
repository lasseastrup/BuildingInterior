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
        [Tooltip("Pixels one metre must cover, at a building's nearest point, for it to show LOD0 (full detail). Feature scale, not screen size: furniture and frames only matter up close, however big the building.")]
        [Min(0.1f)] public float lod0PixelsPerMetre = 16f;

        [Tooltip("Pixels per metre for LOD1 (the shell, with windows).")]
        [Min(0.01f)] public float lod1PixelsPerMetre = 4f;

        [Tooltip("Pixels per metre for LOD2 (the massing, in merged cells); below this a building isn't drawn.")]
        [Min(0.001f)] public float lod2PixelsPerMetre = 0.3f;

        [Tooltip("How much further a building must go before it switches back, so it doesn't flicker at the boundary (0.12: 12%).")]
        [Range(0, 0.5f)] public float hysteresis = 0.12f;

        [Tooltip("The dithered cross-fade between two LODs, in seconds.")]
        [Range(0, 2)] public float fadeSeconds = 0.35f;

        [Tooltip("Beyond this many metres a building is not drawn.")]
        [Min(10)] public float farDistance = 1500f;

        [Tooltip("How many buildings may hold LOD0 geometry at once. The least recently shown go first beyond this.")]
        [Min(1)] public int lod0Residency = 16;

        [Tooltip("How many buildings may hold LOD1 geometry at once.")]
        [Min(1)] public int lod1Residency = 260;

        [Tooltip("Milliseconds a frame may spend building LOD0 and LOD1 meshes (one is always built when any is wanted).")]
        [Range(0.5f, 33)] public float buildBudgetMs = 6f;

        [Tooltip("LOD0's see-through windows start as LOD1's painted rooms where LOD0 takes over, so the switch doesn't change the windows, and clear to glass by this share of that distance (0.6: clear at 60% of it). 1: clear at once.")]
        [Range(0.1f, 1f)] public float windowsClearAt = 0.6f;

        [Tooltip("Generate LOD0 and LOD1 on worker threads, one fewer than the cores; only the upload is on the main thread. Off builds on the main thread (WebGL always does).")]
        public bool buildOnWorkerThreads = true;

        [Header("Occlusion")]
        [Tooltip("Compile the occlusion discard into the LOD2 and cell materials. Off on the minimum tier: alpha clipping costs early-Z on tile-based GPUs.")]
        public bool occlusionOnFarMaterials = true;

        [Header("Rendering")]
        [Tooltip("Allow URP's GPU occlusion culling for building renderers. Measured per project; off on the minimum tier.")]
        public bool gpuOcclusionCulling = true;

        [Tooltip("Target frame rate the package's budgets are measured against.")]
        [Range(30, 120)] public int targetFrameRate = 60;

        /// <summary>The automatic LOD's settings from these.</summary>
        public Lod.LodSettings ToLodSettings() { var s = new Lod.LodSettings(); ApplyTo(s); return s; }

        /// <summary>Write these into the automatic LOD's settings (a site's, live).</summary>
        public void ApplyTo(Lod.LodSettings s)
        {
            if (s.PX.Length != 3) s.PX = new double[3];   // into the array it has: the site applies this every frame
            s.PX[0] = lod0PixelsPerMetre; s.PX[1] = lod1PixelsPerMetre; s.PX[2] = lod2PixelsPerMetre;
            s.Hysteresis = hysteresis; s.Fade = Mathf.Max(0.01f, fadeSeconds); s.Far = farDistance;
            s.GlassClear = windowsClearAt; s.MaxLod0 = lod0Residency; s.MaxLod1 = lod1Residency; s.BudgetMs = buildBudgetMs; s.Threads = buildOnWorkerThreads ? -1 : 0;
        }
    }
}
