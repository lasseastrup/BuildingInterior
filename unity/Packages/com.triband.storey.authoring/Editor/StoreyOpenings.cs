#nullable enable
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The project's artist-made windows and doors (<see cref="StoreyOpening"/>, docs/EDITOR.md §6.9): found and
    /// registered when the editor loads, baked again when their models are reimported, and kept in each site's list of
    /// the ones its layout uses, so builds include them.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoreyOpenings
    {
        static StoreyOpenings() => EditorApplication.delayCall += () => { foreach (var o in All()) o.Register(); };

        /// <summary>Every Storey Opening asset in the project, by name.</summary>
        public static List<StoreyOpening> All() =>
            AssetDatabase.FindAssets("t:" + nameof(StoreyOpening)).Select(g => AssetDatabase.LoadAssetAtPath<StoreyOpening>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(o => o != null).Select(o => o!).OrderBy(o => o.name).ToList();

        /// <summary>Add an opening to a site's list, if it isn't there.</summary>
        public static void Keep(StoreySite? site, StoreyOpening o)
        {
            if (site == null || site.openings.Contains(o)) return;
            Undo.RecordObject(site, "Keep " + o.name);
            site.openings.Add(o);
            EditorUtility.SetDirty(site);
        }

        /// <summary>A site's list made to match what its layout's styles use (on save).</summary>
        public static void Sync(StoreySite site, StoreyDocument doc)
        {
            var used = new HashSet<string>();
            foreach (var b in doc.buildings)
            {
                foreach (var s in new[] { b.style }.Concat(b.floors.Select(f => f.style)))
                {
                    if (s == null) continue;
                    if (s.windowKind != null) used.Add(s.windowKind);
                    if (s.doorKind != null) used.Add(s.doorKind);
                }
            }
            var want = All().Where(o => used.Contains(o.Id)).ToList();
            if (want.Count == site.openings.Count && want.All(site.openings.Contains)) return;
            site.openings = want;
            EditorUtility.SetDirty(site);
        }
    }

    /// <summary>A reimported model: the openings made from its meshes are baked again.</summary>
    internal sealed class StoreyOpeningReimport : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported.Length == 0) return;
            var set = new HashSet<string>(imported);
            foreach (var o in StoreyOpenings.All())
            {
                bool hit = (o.mesh != null && set.Contains(AssetDatabase.GetAssetPath(o.mesh))) || (o.lod1Mesh != null && set.Contains(AssetDatabase.GetAssetPath(o.lod1Mesh)));
                if (!hit) continue;
                o.Bake(); EditorUtility.SetDirty(o);
            }
        }
    }

    /// <summary>
    /// A Storey Opening's inspector: setting a mesh names its parts from the model's materials and takes the hole's size
    /// from its bounds; any change bakes it again. Says what it found, and what looks wrong with the pivot.
    /// </summary>
    [CustomEditor(typeof(StoreyOpening))]
    internal sealed class StoreyOpeningEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var o = (StoreyOpening)target;
            var before = o.mesh;
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            if (o.mesh != before && o.mesh != null) Fill(o);
            if (changed || (o.mesh != null && OpeningKinds.Get(o.Id) == null)) { o.Bake(); EditorUtility.SetDirty(o); }
            if (o.mesh == null) { EditorGUILayout.HelpBox("Give it a mesh: pivot at the bottom middle of the hole in the wall, +Y up, +Z out of the wall, in metres. Each submesh is one part.", MessageType.Info); return; }
            var b = o.mesh.bounds;
            int tris = 0; for (int s = 0; s < o.mesh.subMeshCount; s++) tris += o.mesh.GetTriangles(s).Length / 3;
            EditorGUILayout.HelpBox($"{tris} triangles in {o.mesh.subMeshCount} part{(o.mesh.subMeshCount == 1 ? "" : "s")}; {b.size.x:0.00} × {b.size.y:0.00} m, {b.size.z:0.00} m deep. Every {(o.door ? "street door" : "window")} of a style that picks it gets one.", MessageType.None);
            if (Mathf.Abs(b.min.y) > 0.05f || Mathf.Abs(b.center.x) > 0.05f)
                EditorGUILayout.HelpBox("The pivot looks off: it should be at the bottom middle of the hole (the mesh's bounds start at y 0 and are centred on x 0).", MessageType.Warning);
            if (o.mesh.subMeshCount > o.parts.Count)
                EditorGUILayout.HelpBox("Some parts have no colour set: they take Frames.", MessageType.Warning);
            if (GUILayout.Button(new GUIContent("Name the parts from the model again", "Guess each part's colour from its material's name, and the size from the mesh"))) { Undo.RecordObject(o, "Name the parts"); Fill(o); o.Bake(); EditorUtility.SetDirty(o); }
        }

        /// <summary>The parts from the model's materials' names, the size from the mesh's bounds.</summary>
        static void Fill(StoreyOpening o)
        {
            var m = o.mesh!; var names = new string[m.subMeshCount];
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(m));
            if (model != null)
                foreach (var f in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (f.sharedMesh != m) continue;
                    var r = f.GetComponent<MeshRenderer>(); if (r == null) continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < names.Length && i < mats.Length; i++) names[i] = mats[i] != null ? mats[i].name : "";
                }
            o.parts = names.Select(n => StoreyOpening.Guess(n ?? "")).ToList();
            o.size = new Vector2(Mathf.Max(0.1f, m.bounds.size.x), Mathf.Max(0.1f, m.bounds.max.y));
            o.door = o.door || names.Any(n => n != null && n.ToLowerInvariant().Contains("door"));
        }
    }
}
