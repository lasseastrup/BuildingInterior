#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The per-frame uniforms every Storey material reads: the camera and focus (the player) for the
    /// cutaway, the cut heights, the active building, and the occlusion mode. Written by the
    /// occlusion system (workstream 5); until then a project sets them from its own camera script.
    /// </summary>
    public static class StoreyGlobals
    {
        public enum OcclusionMode { Off = 0, SinkOrSlice = 1, Cutout = 2, Fade = 3, Dissolve = 4 }   // Sink and Slice share a mode; the occluder rows decide (segments > 0 = Sink)

        /// <summary>Camera position used by the cutaway: the focus plus the camera's offset from the play camera's target.</summary>
        public static void SetCamera(Vector3 cam, Vector3 focus, Vector2 camDir)
        {
            Shader.SetGlobalVector(StoreyShaderIds.Cam, cam);
            Shader.SetGlobalVector(StoreyShaderIds.Focus, focus);
            Shader.SetGlobalVector(StoreyShaderIds.CamDir, camDir);
        }

        /// <summary>Cutaway parameters: on/off, stub height, base and top of the active storey's walls, and the ceiling clip.</summary>
        public static void SetCut(bool on, float stubHeight, float cutBase, float cutTop, float clipY)
        {
            Shader.SetGlobalVector(StoreyShaderIds.Cut, new Vector4(on ? 1 : 0, stubHeight, cutBase, cutTop));
            Shader.SetGlobalVector(StoreyShaderIds.Active, ActiveRow(clipY));
        }

        static float activeIndex = -1;
        static float? terraceY;
        static bool leaving;

        /// <summary>
        /// _StoreyActive: x the active building's row (-1: none), y its ceiling clip, z the floor of the terrace over which it is in
        /// the way and w 1 while it is, or w 2 while it is being left (<see cref="SetActive"/>; 0 and 0 otherwise).
        /// </summary>
        static Vector4 ActiveRow(float clipY) => new Vector4(activeIndex, clipY, terraceY ?? 0, leaving ? 2 : terraceY != null ? 1 : 0);

        /// <summary>The active building's row (-1: none), as last set: what the steady variant can't be given (SiteRenderer).</summary>
        public static int ActiveIndex => (int)activeIndex;
        /// <summary>How far the buildings other than <see cref="IsolatedIndex"/> are faded, as last set (0: none isolated).</summary>
        public static float IsolateAmount { get; private set; }
        public static int IsolatedIndex { get; private set; } = -1;

        /// <summary>
        /// The building the player is in (or the one being edited); -1 = none. <paramref name="inWayAbove"/>: out on a terrace of it
        /// with what stands over the terrace in the way, the terrace's floor (world height): everything of it above that is
        /// drawn as a building in the way, by the occluder slot in its row, but the walls the wall table marks -1 (the
        /// terrace's own); null for none (Occlusion.OcclusionView.terraceY). <paramref name="leftBehind"/>: the player has just
        /// left it, past its door, and it is being handed over to the buildings in the way: clipped and cut away as it was,
        /// and in the way as well, by the occluder slot in its row (Occlusion.OcclusionView.leaving).
        /// </summary>
        public static void SetActive(int buildingIndex, float clipY, float? inWayAbove = null, bool leftBehind = false)
        {
            activeIndex = buildingIndex; terraceY = buildingIndex >= 0 ? inWayAbove : null; leaving = buildingIndex >= 0 && leftBehind;
            Shader.SetGlobalVector(StoreyShaderIds.Active, ActiveRow(clipY));
        }

        /// <summary>
        /// The section cap drawn where a cut exposes a wall's inside: the top face of a wall the cutaway, the ceiling clip, Slice
        /// or Dissolve's Stub cut, lit as a face facing up (StoreyFragment.hlsl). It is the wall's own colour (the colour of its
        /// outside, a room wall's of its faces; Lod0Shape.OutsideSlots), tinted towards <paramref name="cap"/> by
        /// <paramref name="tint"/>: 0 the wall's colour, 1 the cap colour.
        /// </summary>
        public static void SetCap(Color cap, float tint) => Shader.SetGlobalVector(StoreyShaderIds.Cap, new Vector4(cap.r, cap.g, cap.b, tint < 0 ? 0 : tint > 1 ? 1 : tint));

        /// <summary>The colour a cut wall's top can be tinted to, without occlusion settings to say otherwise (the editor's cutaway): a light warm grey, as a plastered wall's top.</summary>
        public static readonly Color DefaultCap = new Color(0.72f, 0.71f, 0.68f);

        /// <summary>
        /// Dissolve's look (<see cref="Occlusion.DissolveLook"/>): how much Ghost and Hatch keep (Ghost's share of the pixels,
        /// Hatch's line's share of its spacing), Hatch's spacing and Stub's height, in metres.
        /// </summary>
        public static void SetDissolve(Occlusion.DissolveLook look, float keeps, float hatchSpacing, float stubHeight) =>
            Shader.SetGlobalVector(StoreyShaderIds.Dissolve, new Vector4((float)look, keeps, hatchSpacing, stubHeight));

        /// <summary>Isolate a building: every other one fades to a screen-door ghost (0 = off, 1 = fully hidden).</summary>
        public static void SetIsolate(float amount, int buildingIndex)
        {
            IsolateAmount = amount; IsolatedIndex = buildingIndex;
            Shader.SetGlobalVector(StoreyShaderIds.Iso, new Vector4(amount, buildingIndex, 0, 0));
        }

        /// <summary>
        /// Buildings-in-the-way mode, the player's chest in world space (Cutout and Fade only touch what lies in front
        /// of it; the shader projects it with the camera it draws with) and Cutout's hole radius in metres.
        /// </summary>
        public static void SetOcclusion(OcclusionMode mode, Vector3 playerChest, float holeRadius)
        {
            Shader.SetGlobalFloat(StoreyShaderIds.OccMode, (float)mode);
            Shader.SetGlobalVector(StoreyShaderIds.Player, new Vector4(playerChest.x, playerChest.y, playerChest.z, holeRadius));
        }

        /// <summary>
        /// LOD0's see-through windows: clear within <paramref name="clearWithin"/> metres of the camera, LOD1's painted pane
        /// from <paramref name="paintedFrom"/> on, blended between. 0 for <paramref name="paintedFrom"/>: always clear.
        /// </summary>
        public static void SetGlassFill(float clearWithin, float paintedFrom) => Shader.SetGlobalVector(StoreyShaderIds.GlassFill, new Vector4(clearWithin, paintedFrom, 0, 0));

        /// <summary>Debug tint by LOD (0 off, 1 on).</summary>
        public static void SetLodTint(bool on) => Shader.SetGlobalFloat(StoreyShaderIds.LodTint, on ? 1 : 0);

        /// <summary>The global keyword of <see cref="SetSimpleLighting"/>, as the opaque and massing shaders declare it.</summary>
        public const string SimpleLightingKeyword = "STOREY_SIMPLE_LIGHTING";

        /// <summary>
        /// Light Storey/Opaque and Storey/Massing without URP's specular term and reflection probe (the minimum tier's
        /// <see cref="StoreyQualitySettings.specularAndReflections"/> off; docs/PERFORMANCE.md §7.3). A global keyword:
        /// every site's materials follow the last call. The engine keeps it across a domain reload, so it is compared
        /// with the engine's state, not a copy here.
        /// </summary>
        public static void SetSimpleLighting(bool on)
        {
            if (Shader.IsKeywordEnabled(SimpleLightingKeyword) == on) return;
            if (on) Shader.EnableKeyword(SimpleLightingKeyword); else Shader.DisableKeyword(SimpleLightingKeyword);
        }
    }
}
