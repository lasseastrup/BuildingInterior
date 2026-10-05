#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        readonly Dictionary<string, AsyncOperation> loading = new Dictionary<string, AsyncOperation>(StringComparer.Ordinal);
        readonly Dictionary<string, AsyncOperation> unloading = new Dictionary<string, AsyncOperation>(StringComparer.Ordinal);
        readonly List<Lod.Districts.Area> areas = new List<Lod.Districts.Area>();
        readonly List<Lod.Districts.Load> states = new List<Lod.Districts.Load>();

        /// <summary>Where each district's scene is now (for a loading screen or a debug overlay).</summary>
        public IReadOnlyList<Lod.Districts.Load> States => states;

        void Update()
        {
            if (!Application.isPlaying || districts.Count == 0) return;
            var f = focus != null ? focus : Camera.main != null ? Camera.main.transform : null;
            if (f == null) return;
            areas.Clear(); states.Clear();
            foreach (var d in districts)
            {
                areas.Add(d.Area);
                if (loading.TryGetValue(d.scene, out var lo)) { if (!lo.isDone) { states.Add(Lod.Districts.Load.Loading); continue; } loading.Remove(d.scene); }
                if (unloading.TryGetValue(d.scene, out var un)) { if (!un.isDone) { states.Add(Lod.Districts.Load.Unloading); continue; } unloading.Remove(d.scene); }
                states.Add(SceneManager.GetSceneByPath(d.scene).isLoaded ? Lod.Districts.Load.Loaded : Lod.Districts.Load.Unloaded);
            }
            var p = f.position;
            var (load, unload) = Lod.Districts.Plan(areas, states, p.x, p.z, loadRadius, unloadRadius, maxLoading);
            foreach (int i in load)
            {
                var op = SceneManager.LoadSceneAsync(districts[i].scene, LoadSceneMode.Additive);
                if (op == null) { Debug.LogError($"Storey: can't load district \"{districts[i].scene}\": is it in the build's scene list?"); continue; }
                loading[districts[i].scene] = op; states[i] = Lod.Districts.Load.Loading;
            }
            foreach (int i in unload)
            {
                var op = SceneManager.UnloadSceneAsync(districts[i].scene);
                if (op != null) { unloading[districts[i].scene] = op; states[i] = Lod.Districts.Load.Unloading; }
            }
        }
    }
}
