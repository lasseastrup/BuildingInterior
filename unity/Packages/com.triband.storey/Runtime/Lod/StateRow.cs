#nullable enable
namespace Triband.Storey.Lod
{
    /// <summary>
    /// The displayed LOD in a building's state row, as the building table writes it and the shaders read it (docs/CITY.md
    /// §2, §3): 0 to 3, or that plus <see cref="Hidden"/> for a building hidden at run time (docs/PLAY.md §6). The colour
    /// and depth passes draw none of a hidden building, in its own renderers or in its cell's merged massing, while the
    /// shadow caster takes the LOD less the offset, so its shadow is cast as before (StoreyOcclusion.hlsl's StoreyVertex).
    /// The rest of the row is left as it is, so the LOD manager, the cross-fades and the occluder slots carry on under it,
    /// and a building shown again has the row it would have had. Engine-free. Storey's own.
    /// </summary>
    public static class StateRow
    {
        /// <summary>Added to a hidden building's displayed LOD: StoreyOcclusion.hlsl's <c>STOREY_HIDDEN</c>.</summary>
        public const float Hidden = 4;

        /// <summary>Whether a row's displayed LOD is a hidden building's.</summary>
        public static bool IsHidden(float displayed) => displayed > Hidden - 0.5f;

        /// <summary>The LOD a row shows, hidden or not: what the shadow caster draws.</summary>
        public static float Shown(float displayed) => IsHidden(displayed) ? displayed - Hidden : displayed;

        /// <summary>A LOD as a row holds it, for a building hidden or not.</summary>
        public static float Displayed(float lod, bool hidden) => hidden ? lod + Hidden : lod;

        /// <summary>A row's displayed LOD with its building hidden or shown, the LOD it shows kept.</summary>
        public static float Hide(float displayed, bool hidden) => Displayed(Shown(displayed), hidden);
    }
}
