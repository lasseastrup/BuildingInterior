#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Unity;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
using AA = UnityEngine.AddressableAssets.Addressables;

namespace Triband.Storey.Addressables
{
    /// <summary>
    /// District scenes through Addressables (docs/CITY.md §5, docs/PERFORMANCE.md §6.5), for Storey Districts with
    /// <b>Source</b> set to Addressables. A district's scene is loaded by its address, its path (the address Addressables
    /// gives by default), and unloaded through its handle, which releases the scene's bundles by reference count: the
    /// district's layout, and every asset no loaded district uses, go with it. From the build's scene list a scene's
    /// assets stay loaded for the session.
    /// </summary>
    public sealed class AddressableDistrictScenes : IDistrictScenes
    {
        readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> loads = new Dictionary<string, AsyncOperationHandle<SceneInstance>>(StringComparer.Ordinal);
        readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> unloads = new Dictionary<string, AsyncOperationHandle<SceneInstance>>(StringComparer.Ordinal);
        readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);

        // Storey Districts makes one when its districts come from Addressables
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration), Preserve]
        static void Register() => StoreyDistricts.AddressablesScenes = () => new AddressableDistrictScenes();

        public bool Load(string scene)
        {
            if (failed.Contains(scene) || loads.ContainsKey(scene) || unloads.ContainsKey(scene)) return false;
            loads[scene] = AA.LoadSceneAsync(scene, LoadSceneMode.Additive);
            return true;
        }

        public bool Unload(string scene)
        {
            if (!loads.TryGetValue(scene, out var h) || !h.IsDone || h.Status != AsyncOperationStatus.Succeeded) return false;
            loads.Remove(scene);
            unloads[scene] = AA.UnloadSceneAsync(h);   // the load's handle goes with it
            return true;
        }

        public Lod.Districts.Load StateOf(string scene)
        {
            if (failed.Contains(scene)) return Lod.Districts.Load.Failed;
            // the unload's handle releases itself when done: an invalid one is done
            if (unloads.TryGetValue(scene, out var u)) { if (!u.IsDone) return Lod.Districts.Load.Unloading; unloads.Remove(scene); }
            if (!loads.TryGetValue(scene, out var h)) return Lod.Districts.Load.Unloaded;
            if (!h.IsDone) return Lod.Districts.Load.Loading;
            if (h.Status == AsyncOperationStatus.Succeeded) return Lod.Districts.Load.Loaded;
            failed.Add(scene); loads.Remove(scene);   // once: it would be asked for again every frame
            Debug.LogError($"Storey: can't load district \"{scene}\" through Addressables: is its scene addressable, with its path as its address? ({h.OperationException?.Message})");
            AA.Release(h);
            return Lod.Districts.Load.Failed;
        }
    }
}
