#nullable enable
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>The Storey Prop's inspector: its settings, and where it is (docs/PROPS.md §2.4).</summary>
    [CustomEditor(typeof(StoreyProp)), CanEditMultipleObjects]
    internal sealed class StoreyPropEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (targets.Length != 1) return;
            var p = (StoreyProp)target;
            var at = StoreyProps.Where(p);
            EditorGUILayout.HelpBox(at is var (b, k) ? $"In {b}, {(k == 0 ? "ground floor" : "floor " + k)}." : "In the street: not in any building, so never hidden.", MessageType.None);
            EditorGUILayout.HelpBox("Its renderers' materials need the Storey Prop nodes in their Shader Graph (docs/PROPS.md §2.2). Tools ▸ Storey ▸ Props ▸ Show Prop Buildings tints every prop by its building: one that doesn't change colour doesn't read it.", MessageType.Info);
        }
    }

    internal static class StoreyPropsMenu
    {
        const string Show = "Tools/Storey/Props/Show Prop Buildings";

        [MenuItem(Show, priority = 70)]
        static void Toggle() { StoreyProps.ShowBuildings = !StoreyProps.ShowBuildings; SceneView.RepaintAll(); }

        [MenuItem(Show, true)]
        static bool Check() { Menu.SetChecked(Show, StoreyProps.ShowBuildings); return true; }
    }
}
