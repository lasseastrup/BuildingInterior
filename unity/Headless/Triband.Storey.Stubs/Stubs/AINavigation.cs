// The members of Unity's AI Navigation package (com.unity.ai.navigation 1.1 and 2.0) the authoring package's NavMesh bake
// support (com.triband.storey.authoring/Editor/Navigation) uses, as Unity declares them, so the optional assembly compiles
// here; every body is empty.

#pragma warning disable CS0067, CS0108, CS0169, CS0414, CS0649, CS0660, CS0661   // empty bodies: events never raised, fields never set, == without Equals

namespace Unity.AI.Navigation
{
    public class NavMeshSurface : UnityEngine.MonoBehaviour
    {
        public static System.Collections.Generic.List<NavMeshSurface> activeSurfaces => new System.Collections.Generic.List<NavMeshSurface>();
    }
}

#if UNITY_EDITOR
namespace Unity.AI.Navigation.Editor
{
    public class NavMeshAssetManager : UnityEditor.ScriptableSingleton<NavMeshAssetManager>
    {
        public bool IsSurfaceBaking(NavMeshSurface surface) => false;
    }
}
#endif
