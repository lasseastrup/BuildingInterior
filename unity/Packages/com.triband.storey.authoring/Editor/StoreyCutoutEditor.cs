#nullable enable
using System;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The Storey Cutout's inspector, and its drawing in the Scene view while it is selected (docs/OPENINGS.md §3.2): the
    /// hole, the reveal to the wall's back 0.3 m behind, and the wall's face round it. Fits the hole to every mesh under
    /// the prefab, or to one object's (the frame, say, so shutters and a sill stay outside it).
    /// </summary>
    [CustomEditor(typeof(StoreyCutout))]
    internal sealed class StoreyCutoutEditor : UnityEditor.Editor
    {
        Transform? fitTo;

        public override void OnInspectorGUI()
        {
            var c = (StoreyCutout)target;
            DrawDefaultInspector();
            if (GUILayout.Button(new GUIContent("Fit to the meshes", "The hole as wide and high as every mesh under this object")))
            {
                Undo.RecordObject(c, "Fit the cutout"); FitToMeshes(c); EditorUtility.SetDirty(c);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                fitTo = (Transform?)EditorGUILayout.ObjectField(new GUIContent("Fit to", "One object's meshes, with what is under it: the frame, so shutters and a sill stay outside the hole"), fitTo, typeof(Transform), true);
                using (new EditorGUI.DisabledScope(fitTo == null))
                    if (GUILayout.Button("Fit", GUILayout.Width(48)) && fitTo != null) { Undo.RecordObject(c, "Fit the cutout"); Fit(c, fitTo); EditorUtility.SetDirty(c); }
            }
            EditorGUILayout.HelpBox("The hole the window or door fills: Size and Center in this object's X and Y, the wall's outer face at Wall face along Z, and +Z out of the wall. A Storey Opening made from this prefab reads it (Assets ▸ Create ▸ Storey ▸ Window or Door from Prefab).", MessageType.None);
        }

        /// <summary>The hole fitted to every mesh under the cutout's object (a cutout just added is).</summary>
        internal static void FitToMeshes(StoreyCutout c) => Fit(c, c.transform);

        /// <summary>The hole fitted to the meshes under <paramref name="under"/>, in the cutout's space.</summary>
        static void Fit(StoreyCutout c, Transform under)
        {
            if (KindBake.Bounds(StoreyOpeningBake.Meshes(c.transform, under)) is not { } b) return;
            c.size = new Vector2((float)Math.Max(0.05, b.max.x - b.min.x), (float)Math.Max(0.05, b.max.y - b.min.y));
            c.center = new Vector2((float)((b.min.x + b.max.x) / 2), (float)((b.min.y + b.max.y) / 2));
        }

        void OnSceneGUI()
        {
            var c = (StoreyCutout)target;
            var was = Handles.matrix;
            Handles.matrix = c.transform.localToWorldMatrix;
            float x0 = c.center.x - c.size.x / 2, x1 = c.center.x + c.size.x / 2, y0 = c.center.y - c.size.y / 2, y1 = c.center.y + c.size.y / 2, z = c.wallFace;
            float back = z - (float)Dim.T_EXT / Mathf.Max(1e-4f, Mathf.Abs(c.transform.lossyScale.z)), m = 0.5f;
            Vector3 P(float x, float y, float d) => new Vector3(x, y, d);
            Handles.color = new Color(0f, 0.85f, 1f, 0.95f);
            Handles.DrawAAPolyLine(3, P(x0, y0, z), P(x1, y0, z), P(x1, y1, z), P(x0, y1, z), P(x0, y0, z));
            Handles.color = new Color(0f, 0.85f, 1f, 0.5f);
            Handles.DrawAAPolyLine(1.5f, P(x0, y0, back), P(x1, y0, back), P(x1, y1, back), P(x0, y1, back), P(x0, y0, back));
            foreach (var (x, y) in new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) }) Handles.DrawDottedLine(P(x, y, z), P(x, y, back), 4);
            Handles.color = new Color(1f, 1f, 1f, 0.3f);
            Handles.DrawAAPolyLine(1, P(x0 - m, y0 - m, z), P(x1 + m, y0 - m, z), P(x1 + m, y1 + m, z), P(x0 - m, y1 + m, z), P(x0 - m, y0 - m, z));
            Handles.matrix = was;
        }
    }
}
