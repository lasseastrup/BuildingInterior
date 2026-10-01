#nullable enable
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The site's inspector. For now (docs/EDITOR.md slice 6.3) it starts and ends an edit and changes floor counts, enough
    /// to check that edits draw at once, undo and save; slice 6.4 replaces it with the Shape, Facade and Interior tabs.
    /// </summary>
    [CustomEditor(typeof(StoreySite))]
    internal sealed class StoreySiteEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var site = (StoreySite)target;
            if (site.layout == null) return;
            EditorGUILayout.Space();
            var e = StoreyEdit.Of(site);
            if (e == null)
            {
                if (GUILayout.Button("Edit layout")) StoreyEdit.Begin(site);
                return;
            }

            var doc = e.Document;
            if (doc.buildings.Count == 0) { EditorGUILayout.HelpBox("The layout has no buildings.", MessageType.Info); }
            else
            {
                e.selected = System.Math.Max(0, System.Math.Min(e.selected, doc.buildings.Count - 1));
                e.selected = EditorGUILayout.Popup("Building", e.selected, doc.buildings.Select(b => b.name).ToArray());
                var b = doc.buildings[e.selected]; int i = e.selected;
                int n = EditorGUILayout.DelayedIntField("Floors", b.floors.Count);
                if (n != b.floors.Count) e.Apply($"{b.name}: {n} floors", d => { Floors.SetCount(d.buildings[i], n); return true; });
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Add floor")) e.Apply($"{b.name}: floor added", d => { Floors.AddTop(d.buildings[i]); return true; });
                    using (new EditorGUI.DisabledScope(b.floors.Count <= 1))
                        if (GUILayout.Button("Remove top floor")) e.Apply($"{b.name}: top floor removed", d => Floors.Delete(d.buildings[i], d.buildings[i].floors.Count - 1));
                }
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!e.Dirty))
                {
                    if (GUILayout.Button("Save")) e.Save();
                    if (GUILayout.Button("Revert")) e.Revert();
                }
                if (GUILayout.Button("Stop editing"))
                {
                    if (!e.Dirty) e.End();
                    else
                    {
                        int r = EditorUtility.DisplayDialogComplex("Unsaved layout", e.Path + "\n\nSave the changes?", "Save", "Cancel", "Don't save");
                        if (r == 0) { e.Save(); e.End(); }
                        else if (r == 2) e.End();
                    }
                }
            }
            if (e.Dirty) EditorGUILayout.HelpBox("Unsaved: Save, or save the scene (Ctrl+S).", MessageType.None);
        }
    }
}
