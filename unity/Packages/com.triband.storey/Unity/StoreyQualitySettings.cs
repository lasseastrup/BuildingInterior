#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Per-tier quality settings for the package (Plan §6.1, §6.6). One asset per device
    /// tier; a project selects one at startup from the device model. Nothing here changes
    /// content: the same buildings, LODs and occlusion modes exist on every tier, and
    /// these knobs only trade detail distance, far buildings' occlusion and build speed
    /// for memory, CPU time and fill rate.
    /// </summary>
    /// <remarks>
    /// Defaults are the primary tier (desktop and iPhone 14 or newer). The package ships both
    /// tiers as assets (Quality Tiers/): Storey Primary Tier, the defaults, and Storey Minimum
    /// Tier (iPhone 7), with half the residency, 12 MB of mesh chunks and no occlusion on far
    /// materials (docs/CITY.md §7). A project picks one, or makes its own, the same kind of
    /// asset. A change to these defaults needs a primary-tier reason.
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

        [Tooltip("How many buildings keep their LOD0 geometry, shown or not. Past this, those not showing it lose it, the least recently shown first. It doesn't limit how many show LOD0: that is the LOD0 distance (the pixels per metre above, and the screen's height).")]
        [Min(1)] public int lod0Residency = 16;

        [Tooltip("How many buildings keep their LOD1 geometry, shown or not, as for LOD0.")]
        [Min(1)] public int lod1Residency = 260;

        [Tooltip("How many buildings keep their collider. A collider comes with a building's LOD0 and stays when the LOD0 goes; past this many, those whose LOD0 went longest ago lose theirs, made again with their next LOD0. 0 keeps every one for the session.")]
        [Min(0)] public int collidersKept = 64;

        [Tooltip("Milliseconds a frame may spend building LOD0 and LOD1 meshes, every site's together (each site still builds one when any is wanted).")]
        [Range(0.5f, 33)] public float buildBudgetMs = 6f;

        [Tooltip("LOD0's see-through windows start as LOD1's painted rooms where LOD0 takes over, so the switch doesn't change the windows, and clear to glass by this share of that distance (0.6: clear at 60% of it). 1: clear at once.")]
        [Range(0.1f, 1f)] public float windowsClearAt = 0.6f;

        [Tooltip("Generate LOD0 and LOD1 on Storey's own build threads; only the upload is on the main thread. Off builds on the main thread (WebGL always does).")]
        public bool buildOnWorkerThreads = true;

        [Tooltip("How many build threads: 0 is automatic, two on a phone or tablet and one fewer than the cores (at most four) on a desktop. They run below normal priority, beside Unity's own job workers; more brings detail in sooner and competes with the engine for cores.")]
        [Min(0)] public int buildThreads = 0;

        [Header("Memory")]
        [Tooltip("Megabytes of mesh memory kept between detail builds for the next ones: the builders' lists and the packed meshes waiting for upload are made of the same fixed-size chunks. Past this, a chunk given back is garbage: the test city's five-minute walk makes 91 MB of garbage with 16 and 226 MB with 8. 12 on the minimum tier.")]
        [Min(0)] public int meshChunksKeptMB = 16;

        [Header("Occlusion")]
        [Tooltip("Cut and fade massings (LOD2 and the cells) in the way like near buildings. Off, far geometry always draws without discard (the steady variant), which tile-based GPUs treat as opaque; off on the minimum tier.")]
        public bool occlusionOnFarMaterials = true;

        // No GPU occlusion culling or frame rate here. Unity has no per-renderer switch for GPU occlusion: it is each quality
        // level's Universal Renderer setting. The frame rate is the project's (Application.targetFrameRate), and Storey's own
        // budget is the milliseconds above. docs/SPEC.md §6.7 lists the project settings Storey needs.

        /// <summary>The automatic LOD's settings from these.</summary>
        public Lod.LodSettings ToLodSettings() { var s = new Lod.LodSettings(); ApplyTo(s); return s; }

        /// <summary>Write these into the automatic LOD's settings (a site's, live).</summary>
        public void ApplyTo(Lod.LodSettings s)
        {
            if (s.PX.Length != 3) s.PX = new double[3];   // into the array it has: the site applies this every frame
            s.PX[0] = lod0PixelsPerMetre; s.PX[1] = lod1PixelsPerMetre; s.PX[2] = lod2PixelsPerMetre;
            s.Hysteresis = hysteresis; s.Fade = Mathf.Max(0.01f, fadeSeconds); s.Far = farDistance;
            s.GlassClear = windowsClearAt; s.MaxLod0 = lod0Residency; s.MaxLod1 = lod1Residency; s.MaxColliders = collidersKept; s.BudgetMs = buildBudgetMs;
            s.Threads = !buildOnWorkerThreads ? 0 : buildThreads > 0 ? buildThreads : -1;
            s.KeepChunkBytes = (long)meshChunksKeptMB << 20;
            s.FarOcclusion = occlusionOnFarMaterials;
        }
    }
}
