#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// How <see cref="StoreyDistricts"/> loads and unloads a district's scene, by the scene's path (docs/CITY.md §5): from
    /// the build's scene list (<see cref="BuildSceneDistricts"/>), or through Addressables (Storey's Addressables support,
    /// <see cref="StoreyDistricts.AddressablesScenes"/>). A loader keeps what it needs to unload each scene it loaded.
    /// </summary>
    public interface IDistrictScenes
    {
        /// <summary>Start loading a district's scene, additively. False when it can't be; the loader says why.</summary>
        bool Load(string scene);
        /// <summary>Start unloading a district's scene. False when it isn't loaded.</summary>
        bool Unload(string scene);
        /// <summary>Where the scene is now. <see cref="Lod.Districts.Load.Failed"/>: its load failed, and it isn't tried again.</summary>
        Lod.Districts.Load StateOf(string scene);
    }

    /// <summary>
    /// District scenes from the build's scene list (<c>SceneManager</c>), by path. A scene is known loaded from Unity's
    /// scene callbacks, where every district's scene was looked up by its path every frame (docs/PERFORMANCE.md §6.9);
    /// one loaded otherwise, opened in the editor beside the city say, counts too.
    /// </summary>
    public sealed class BuildSceneDistricts : IDistrictScenes, IDisposable
    {
        readonly Dictionary<string, AsyncOperation> loading = new Dictionary<string, AsyncOperation>(StringComparer.Ordinal);
        readonly Dictionary<string, AsyncOperation> unloading = new Dictionary<string, AsyncOperation>(StringComparer.Ordinal);
        readonly HashSet<string> loaded = new HashSet<string>(StringComparer.Ordinal), failed = new HashSet<string>(StringComparer.Ordinal);

        public BuildSceneDistricts()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s.isLoaded) loaded.Add(s.path); }
            SceneManager.sceneLoaded += Loaded;
            SceneManager.sceneUnloaded += Unloaded;
        }

        public void Dispose()
        {
            SceneManager.sceneLoaded -= Loaded;
            SceneManager.sceneUnloaded -= Unloaded;
        }

        void Loaded(Scene s, LoadSceneMode mode) => loaded.Add(s.path);
        void Unloaded(Scene s) => loaded.Remove(s.path);

        public bool Load(string scene)
        {
            if (failed.Contains(scene)) return false;
            var op = SceneManager.LoadSceneAsync(scene, LoadSceneMode.Additive);
            if (op == null)
            {
                failed.Add(scene);   // once: it would be asked for again every frame
                Debug.LogError($"Storey: can't load district \"{scene}\": is it in the build's scene list?");
                return false;
            }
            loading[scene] = op;
            return true;
        }

        public bool Unload(string scene)
        {
            if (!loaded.Contains(scene)) return false;
            var op = SceneManager.UnloadSceneAsync(scene);
            if (op == null) return false;
            unloading[scene] = op;
            return true;
        }

        public Lod.Districts.Load StateOf(string scene)
        {
            if (failed.Contains(scene)) return Lod.Districts.Load.Failed;
            // what an operation finished, as its callback says it: the two may come in either order within the frame
            if (loading.TryGetValue(scene, out var lo)) { if (!lo.isDone) return Lod.Districts.Load.Loading; loading.Remove(scene); loaded.Add(scene); }
            if (unloading.TryGetValue(scene, out var un)) { if (!un.isDone) return Lod.Districts.Load.Unloading; unloading.Remove(scene); loaded.Remove(scene); }
            return loaded.Contains(scene) ? Lod.Districts.Load.Loaded : Lod.Districts.Load.Unloaded;
        }
    }
}
