#nullable enable
namespace Triband.Storey.Occlusion
{
    /// <summary>
    /// Whether a building's renderer may draw with the shaders' steady variant this frame (<c>STOREY_STEADY</c>,
    /// docs/CITY.md §2): one with no discard, which tile-based GPUs such as Apple's treat as opaque and hide behind
    /// other geometry before shading. Nothing may hide any of the building's pixels: it isn't cross-fading between LODs,
    /// no other building is isolated, it isn't in the way (an occluder slot; for a far renderer only while far
    /// occlusion is on), and it isn't the building the player is in, or one carrying a party wall with it (the cutaway
    /// and the ceiling clip). These are StoreyOcclude's conditions (StoreyOcclusion.hlsl), and change with them.
    /// Engine-free.
    /// </summary>
    public static class Steady
    {
        /// <param name="index">The building's row in the building table.</param>
        /// <param name="fade">Its state's cross-fade, 0 to 1 (1: not fading).</param>
        /// <param name="occluder">Its state's occluder slot + 1 (0: not in the way).</param>
        /// <param name="active">The building the player is in, or the one being edited (-1: none).</param>
        /// <param name="isolate">How far the other buildings are faded for an isolated one (0: none isolated).</param>
        /// <param name="isolated">The isolated building.</param>
        /// <param name="mates">The buildings whose party walls the renderer's mesh carries (LOD0's; null for none).</param>
        /// <param name="far">A massing renderer (LOD2 or a cell): in the way only while <paramref name="farOcclusion"/>.</param>
        public static bool Of(int index, float fade, float occluder, int active, float isolate, int isolated, int[]? mates = null, bool far = false, bool farOcclusion = true)
        {
            if (fade < 1f) return false;
            if (Isolating(isolate) && index != isolated) return false;
            if (occluder > 0.5f && (!far || farOcclusion)) return false;
            if (active >= 0)
            {
                if (index == active) return false;
                if (mates != null) foreach (int m in mates) if (m == active) return false;
            }
            return true;
        }

        /// <summary>Whether a building is isolated at this amount: the others then never draw steady.</summary>
        public static bool Isolating(float isolate) => isolate > 0.001f;

        /// <summary>
        /// Whether a state row going from one fade and occluder slot to another leaves <see cref="Of"/>'s answer for it as
        /// it was, whatever else it is asked with: it reads only whether the fade is under 1 and whether there is a slot. The
        /// building table marks the rows for which it doesn't, and only those buildings are looked at again
        /// (docs/PERFORMANCE.md §3.3).
        /// </summary>
        public static bool Same(float fade0, float occluder0, float fade1, float occluder1) =>
            (fade0 < 1f) == (fade1 < 1f) && (occluder0 > 0.5f) == (occluder1 > 0.5f);
    }
}
