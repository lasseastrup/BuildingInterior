#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// A city split into districts (docs/CITY.md §5): one scene each, with its Storey Site, loaded additively as the player
    /// nears it and unloaded when they are well past it. Put it in the scene that stays loaded (the player, the camera,
    /// the lighting). The districts' areas are stored here, since an unloaded scene can't be asked; the inspector reads
    /// them from the open district scenes. The scenes must be in the build's scene list.
    /// </summary>
    [AddComponentMenu("Storey/Storey Districts")]
    public sealed class StoreyDistricts : MonoBehaviour
    {
        [Serializable]
        public sealed class District
        {
            [Tooltip("The district's scene, by path (Assets/…/North.unity).")]
            public string scene = "";
            [Tooltip("Its ground plan in world space: the buildings' bounds (x0, z0, x1, z1). The inspector reads it from the open scene.")]
            public Vector4 area;
            public Lod.Districts.Area Area => new Lod.Districts.Area(area.x, area.y, area.z, area.w);
        }

        public List<District> districts = new List<District>();
        [Tooltip("Where the player is: the main camera when empty.")]
        public Transform? focus;
        [Tooltip("A district this close (metres, from the focus to its area) is loaded.")]
        [Min(0)] public float loadRadius = 300;
        [Tooltip("A loaded district stays until it is this far: larger than the load radius, so one on the edge isn't loaded and unloaded over and over.")]
        [Min(0)] public float unloadRadius = 400;
        [Tooltip("How many districts may be loading at once: a scene loading costs frames.")]
        [Range(1, 4)] public int maxLoading = 1;
        [Tooltip("Where the district scenes come from: the build's scene list, or Addressables (add the Addressables package; a district's address is its scene's path, the address Addressables gives by default). A district unloaded through Addressables releases its scene's bundles, and with them its layout and whatever no loaded district uses; from the scene list its assets stay loaded.")]
        public SceneSource source = SceneSource.BuildScenes;

        /// <summary>Where the district scenes come from.</summary>
        public enum SceneSource { BuildScenes, Addressables }

        /// <summary>
        /// Makes the Addressables loader: set by Storey's Addressables support (the <c>Triband.Storey.Addressables</c>
        /// assembly) when the package is in the project; null otherwise.
        /// </summary>
        public static Func<IDistrictScenes>? AddressablesScenes { get; set; }

        IDistrictScenes? scenes; SceneSource scenesFor;
        readonly List<Lod.Districts.Area> areas = new List<Lod.Districts.Area>();
        readonly List<Lod.Districts.Load> states = new List<Lod.Districts.Load>();
        readonly List<int> toLoad = new List<int>(), toUnload = new List<int>();   // the plan, every frame, without garbage

        /// <summary>Where each district's scene is now (for a loading screen or a debug overlay).</summary>
        public IReadOnlyList<Lod.Districts.Load> States => states;

        void OnDisable() { (scenes as IDisposable)?.Dispose(); scenes = null; }

        /// <summary>The loader for <see cref="source"/>, made when first needed; null for Addressables without the package.</summary>
        IDistrictScenes? Scenes()
        {
            if (scenes != null && scenesFor == source) return scenes;
            (scenes as IDisposable)?.Dispose(); scenes = null; scenesFor = source;
            if (source == SceneSource.BuildScenes) scenes = new BuildSceneDistricts();
            else if (AddressablesScenes != null) scenes = AddressablesScenes();
            else Debug.LogError("Storey: the districts come from Addressables, but the Addressables package isn't in the project.");
            return scenes;
        }

        void Update()
        {
            if (!Application.isPlaying || districts.Count == 0) return;
            var f = focus != null ? focus : Camera.main != null ? Camera.main.transform : null;
            if (f == null) return;
            var from = Scenes();
            if (from == null) return;
            areas.Clear(); states.Clear();
            foreach (var d in districts) { areas.Add(d.Area); states.Add(from.StateOf(d.scene)); }
            var p = f.position;
            Lod.Districts.Plan(areas, states, p.x, p.z, toLoad, toUnload, loadRadius, unloadRadius, maxLoading);
            foreach (int i in toLoad) states[i] = from.Load(districts[i].scene) ? Lod.Districts.Load.Loading : from.StateOf(districts[i].scene);
            foreach (int i in toUnload) if (from.Unload(districts[i].scene)) states[i] = Lod.Districts.Load.Unloading;
        }
    }
}
