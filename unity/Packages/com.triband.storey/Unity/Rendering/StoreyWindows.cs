#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Windows with rooms behind them and lights at night (docs/EDITOR.md §6.8), after the project's Window shader graph.
    /// Add it to any object in a scene. It sets the globals StoreyWindow.hlsl reads, so the panes that aren't see-through
    /// (shell and filled storeys, LOD1's and LOD2's windows) show a room from the atlas, lit by the main light by day,
    /// and light up as <see cref="nightBlend"/> rises. Turning it off (or removing it) gives the plain dark glass back.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Storey/Storey Windows")]
    public sealed class StoreyWindows : MonoBehaviour
    {
        [Tooltip("The room atlas: each cell a room in one-point perspective, laid out in thirds (ceiling, floor and side walls round the back wall), as the Window shader graph's Texture2D.")]
        public Texture2D? rooms;
        [Tooltip("The atlas's columns and rows (the Window shader graph's AtlasGrid).")]
        public Vector2Int atlasGrid = new Vector2Int(1, 1);
        [Tooltip("How deep the rooms look: the room's depth over the window's width, 1 a cube (the Window shader graph's Depth).")]
        [Range(0.05f, 4f)] public float depth = 1f;
        [Tooltip("By day the room is lit by the main light's colour times this (the Window shader graph's Color).")]
        public Color tint = Color.white;
        [Tooltip("At night a lit window's room is multiplied by a colour between these two (ColorLight, ColorLight2).")]
        public new Color light = new Color(1f, 0.85f, 0.55f);   // new: hides Component.light, long obsolete
        public Color light2 = new Color(1f, 0.65f, 0.35f);
        [Tooltip("The glare over the glass (the Window shader graph's second texture), and how strong it is (0.03 there).")]
        public Texture2D? glare;
        [Range(0, 0.5f)] public float glareStrength = 0.03f;
        [Tooltip("0 day, 1 night: windows light up as it rises. Set it from your day and night cycle, or with NightBlend. A project's shaders can take their own instead (the Color Palette Lit sample takes _GlobalNightBlend).")]
        [Range(0, 1)] public float nightBlend;
        [Tooltip("The share of windows lit at full night.")]
        [Range(0, 1)] public float lightChance = 1f;

        /// <summary>The night blend for every StoreyWindows in the scene, for a day and night system to drive.</summary>
        public static float? NightBlend { get; set; }

        static readonly int On = Shader.PropertyToID("_StoreyWindowsOn"), Rooms = Shader.PropertyToID("_StoreyWindowRooms"),
            Glare = Shader.PropertyToID("_StoreyWindowGlare"), Atlas = Shader.PropertyToID("_StoreyWindowAtlas"),
            Tint = Shader.PropertyToID("_StoreyWindowTint"), Light = Shader.PropertyToID("_StoreyWindowLight"),
            Light2 = Shader.PropertyToID("_StoreyWindowLight2"), Night = Shader.PropertyToID("_StoreyWindowNight");

        void OnEnable() => Apply();
        void OnValidate() => Apply();
        void Update() => Apply();
        void OnDisable() => Shader.SetGlobalFloat(On, 0);

        static Vector4 Linear(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        void Apply()
        {
            if (!isActiveAndEnabled) return;
            Shader.SetGlobalFloat(On, rooms != null ? 1 : 0);
            Shader.SetGlobalTexture(Rooms, rooms != null ? rooms : Texture2D.blackTexture);
            Shader.SetGlobalTexture(Glare, glare != null ? glare : Texture2D.blackTexture);
            Shader.SetGlobalVector(Atlas, new Vector4(Mathf.Max(1, atlasGrid.x), Mathf.Max(1, atlasGrid.y), depth, glare != null ? glareStrength : 0));
            // colours as a material's colour properties are, in linear space when the project is: a global keeps what it is given,
            // and the gamma values made the rooms several times too bright
            Shader.SetGlobalVector(Tint, Linear(tint));
            Shader.SetGlobalVector(Light, Linear(light));
            Shader.SetGlobalVector(Light2, Linear(light2));
            Shader.SetGlobalVector(Night, new Vector4(NightBlend ?? nightBlend, lightChance, 0, 0));
        }
    }
}
