// The members of Unity's Addressables (com.unity.addressables 1.21 and 2.x) Storey's Addressables support uses, as
// Unity declares them, so the optional assembly compiles here; every body is empty.

#pragma warning disable CS0067, CS0108, CS0169, CS0414, CS0649, CS0660, CS0661   // empty bodies: events never raised, fields never set, == without Equals

namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }

    public struct AsyncOperationHandle<TObject>
    {
        public bool IsDone => true;
        public bool IsValid() => false;
        public AsyncOperationStatus Status => AsyncOperationStatus.None;
        public TObject Result => default;
        public System.Exception OperationException => null;
    }

    public struct AsyncOperationHandle
    {
        public bool IsDone => true;
        public bool IsValid() => false;
    }
}

namespace UnityEngine.ResourceManagement.ResourceProviders
{
    public struct SceneInstance
    {
        public UnityEngine.SceneManagement.Scene Scene => default;
    }
}

namespace UnityEngine.AddressableAssets
{
    using UnityEngine.ResourceManagement.AsyncOperations;
    using UnityEngine.ResourceManagement.ResourceProviders;

    public static class Addressables
    {
        public static AsyncOperationHandle<SceneInstance> LoadSceneAsync(object key, UnityEngine.SceneManagement.LoadSceneMode loadMode = UnityEngine.SceneManagement.LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100) => default;
        public static AsyncOperationHandle<SceneInstance> UnloadSceneAsync(AsyncOperationHandle<SceneInstance> handle, bool autoReleaseHandle = true) => default;
        public static AsyncOperationHandle<SceneInstance> UnloadSceneAsync(AsyncOperationHandle handle, bool autoReleaseHandle = true) => default;
        public static AsyncOperationHandle<SceneInstance> UnloadSceneAsync(SceneInstance scene, bool autoReleaseHandle = true) => default;
        public static void Release<TObject>(TObject obj) { }
        public static void Release<TObject>(AsyncOperationHandle<TObject> handle) { }
        public static void Release(AsyncOperationHandle handle) { }
    }
}
