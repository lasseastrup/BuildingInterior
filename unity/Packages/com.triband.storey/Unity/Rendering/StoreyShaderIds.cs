#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>Shader property names shared by the C# side and the HLSL include (StoreyOcclusion.hlsl).</summary>
    public static class StoreyShaderIds
    {
        public static readonly int State = Shader.PropertyToID("_StoreyState");
        public static readonly int Occ = Shader.PropertyToID("_StoreyOcc");
        public static readonly int Wall = Shader.PropertyToID("_StoreyWall");
        public static readonly int Params = Shader.PropertyToID("_StoreyParams");
        public static readonly int Cam = Shader.PropertyToID("_StoreyCam");
        public static readonly int Focus = Shader.PropertyToID("_StoreyFocus");
        public static readonly int CamDir = Shader.PropertyToID("_StoreyCamDir");
        public static readonly int Cut = Shader.PropertyToID("_StoreyCut");
        public static readonly int Active = Shader.PropertyToID("_StoreyActive");
        public static readonly int Cap = Shader.PropertyToID("_StoreyCap");
        public static readonly int Iso = Shader.PropertyToID("_StoreyIso");
        public static readonly int OccMode = Shader.PropertyToID("_StoreyOccMode");
        public static readonly int Player = Shader.PropertyToID("_StoreyPlayer");
        public static readonly int LodTint = Shader.PropertyToID("_StoreyLodTint");
    }
}
