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
            Shader.SetGlobalVector(StoreyShaderIds.Active, new Vector4(activeIndex, clipY, 0, 0));
        }

        static float activeIndex = -1;

        /// <summary>The building the player is in (or the one being edited); -1 = none.</summary>
        public static void SetActive(int buildingIndex, float clipY)
        {
            activeIndex = buildingIndex;
            Shader.SetGlobalVector(StoreyShaderIds.Active, new Vector4(buildingIndex, clipY, 0, 0));
        }

        /// <summary>Colour of the section cap drawn where a cut exposes a wall's inside.</summary>
        public static void SetCap(Color cap) => Shader.SetGlobalVector(StoreyShaderIds.Cap, new Vector4(cap.r, cap.g, cap.b, 1));

        /// <summary>Isolate a building: every other one fades to a screen-door ghost (0 = off, 1 = fully hidden).</summary>
        public static void SetIsolate(float amount, int buildingIndex) => Shader.SetGlobalVector(StoreyShaderIds.Iso, new Vector4(amount, buildingIndex, 0, 0));

        /// <summary>
        /// Buildings-in-the-way mode, the player's chest in world space (Cutout and Fade only touch what lies in front
        /// of it; the shader projects it with the camera it draws with) and Cutout's hole radius in metres.
        /// </summary>
        public static void SetOcclusion(OcclusionMode mode, Vector3 playerChest, float holeRadius)
        {
            Shader.SetGlobalFloat(StoreyShaderIds.OccMode, (float)mode);
            Shader.SetGlobalVector(StoreyShaderIds.Player, new Vector4(playerChest.x, playerChest.y, playerChest.z, holeRadius));
        }

        /// <summary>Debug tint by LOD (0 off, 1 on).</summary>
        public static void SetLodTint(bool on) => Shader.SetGlobalFloat(StoreyShaderIds.LodTint, on ? 1 : 0);
    }
}
