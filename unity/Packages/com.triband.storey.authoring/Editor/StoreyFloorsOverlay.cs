#nullable enable
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.Overlays;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The floor card (SPEC §9, docs/EDITOR.md slice 6.6) as a Scene view overlay: the floor count with + and − (new floors
    /// copy the top floor), and with the Interior tool the floor list: pick the active floor, see its height, copy its
    /// layout to every floor above, duplicate or delete it.
    /// </summary>
    [Overlay(typeof(SceneView), "Storey Floors", true)]
    internal sealed class StoreyFloorsOverlay : IMGUIOverlay
    {
        public override void OnGUI()
        {
            var site = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<StoreySite>() : null;
            var e = site != null ? StoreyEdit.Of(site) : null;
            var b = e?.Selected;
            if (e == null || b == null) { GUILayout.Label("Select a Storey Site and a building to edit floors.", EditorStyles.wordWrappedMiniLabel); return; }

            GUILayout.Label(b.name, EditorStyles.boldLabel);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Floors", GUILayout.Width(44));
                int n = EditorGUILayout.DelayedIntField(b.floors.Count, GUILayout.Width(40));
                if (n != b.floors.Count) e.ApplyTo($"{System.Math.Max(1, System.Math.Min(Floors.MaxFloors, n))} floors", bb => { Floors.SetCount(bb, n); return true; });
                if (GUILayout.Button("+", GUILayout.Width(22))) e.ApplyTo($"Floor {b.floors.Count} added", bb => { Floors.AddTop(bb); return true; });
                using (new EditorGUI.DisabledScope(b.floors.Count <= 1))
                    if (GUILayout.Button("−", GUILayout.Width(22))) e.ApplyTo("Top floor removed", bb => Floors.Delete(bb, bb.floors.Count - 1));
            }
            if (ToolManager.activeToolType != typeof(StoreyInteriorTool) || !b.interior) return;

            var v = e.View; int N = b.floors.Count;
            for (int k = N; k >= 0; k--)
            {
                using (new GUILayout.HorizontalScope())
                {
                    string label = k == N ? "Roof" : k == 0 ? "Ground" : "Floor " + k;
                    bool on = GUILayout.Toggle(v.floor == k, label, EditorStyles.miniButton, GUILayout.Width(70));
                    if (on && v.floor != k) { v.floor = k; v.selectedCore = ""; v.selectedWall = -1; SceneView.RepaintAll(); }
                    if (k < N)
                    {
                        GUILayout.Label($"{Derived.FloorH(b, k):0.0} m{(b.floors[k].h.HasValue ? "*" : "")}", EditorStyles.miniLabel, GUILayout.Width(44));
                        if (v.floor == k)
                        {
                            int kk = k;
                            using (new EditorGUI.DisabledScope(k == N - 1))
                                if (GUILayout.Button(new GUIContent("Copy up", "Copy this floor's rooms to every floor above"), EditorStyles.miniButton))
                                    e.ApplyTo($"{label} copied to {N - 1 - k} floor{(N - 1 - k == 1 ? "" : "s")} above", bb => { Floors.CopyLayoutUp(bb, kk); return true; });
                            if (GUILayout.Button(new GUIContent("Dup", "Duplicate this floor"), EditorStyles.miniButton)) { e.ApplyTo($"{label} duplicated", bb => { Floors.InsertAbove(bb, kk); return true; }); v.floor = k + 1; }
                            using (new EditorGUI.DisabledScope(N <= 1))
                                if (GUILayout.Button(new GUIContent("Del", "Delete this floor; the floors above move down"), EditorStyles.miniButton)) { e.ApplyTo($"{label} deleted", bb => Floors.Delete(bb, kk)); v.floor = System.Math.Min(k, N - 2); }
                        }
                    }
                }
            }
        }
    }
}
