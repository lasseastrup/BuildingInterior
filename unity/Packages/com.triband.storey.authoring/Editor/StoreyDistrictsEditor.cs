#nullable enable
using System.Linq;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The district list's inspector (docs/CITY.md §5): adds the open scenes that hold a Storey Site, reads each district's
    /// area from its open scene, and says which district scenes the build's scene list lacks.
    /// </summary>
    [CustomEditor(typeof(StoreyDistricts))]
    internal sealed class StoreyDistrictsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var t = (StoreyDistricts)target;
            GUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Add open district scenes", "Every open scene with a Storey Site, other than this one's, becomes a district")))
                {
                    Undo.RecordObject(t, "Add district scenes");
                    foreach (var s in StoreySite.All)
                    {
                        string path = s.gameObject.scene.path;
                        if (path.Length == 0 || path == t.gameObject.scene.path || t.districts.Any(d => d.scene == path)) continue;
                        t.districts.Add(new StoreyDistricts.District { scene = path });
                    }
                    ReadAreas(t);
                }
                if (GUILayout.Button(new GUIContent("Read areas", "Each district's ground plan from its Storey Site, for the districts whose scenes are open")))
                {
                    Undo.RecordObject(t, "Read district areas"); ReadAreas(t);
                }
            }
            var inBuild = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToHashSet();
            var missing = t.districts.Where(d => d.scene.Length > 0 && !inBuild.Contains(d.scene)).Select(d => d.scene).ToList();
            if (missing.Count > 0) EditorGUILayout.HelpBox("Not in the build's scene list, so they can't be loaded: " + string.Join(", ", missing), MessageType.Warning);
            var flat = t.districts.Where(d => d.area == Vector4.zero).Select(d => d.scene).ToList();
            if (flat.Count > 0) EditorGUILayout.HelpBox("No area yet (open the scene and press Read areas): " + string.Join(", ", flat), MessageType.Info);
        }

        static void ReadAreas(StoreyDistricts t)
        {
            foreach (var d in t.districts)
            {
                var site = StoreySite.All.FirstOrDefault(s => s.gameObject.scene.path == d.scene);
                if (site != null && site.PlanArea is Lod.Districts.Area a) d.area = new Vector4((float)a.x0, (float)a.z0, (float)a.x1, (float)a.z1);
            }
            EditorUtility.SetDirty(t);
        }
    }
}
