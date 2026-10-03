#nullable enable
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The automatic LOD in the editor (docs/CITY.md §2): it picks for the Scene view's camera, and edit mode only
    /// updates when asked, so a camera move, a cross-fade or detail still to build asks for another update. Draws the
    /// LOD stats in the Scene view for a selected site that shows them.
    /// </summary>
    [InitializeOnLoad]
    internal static class StoreyLodEditor
    {
        static Vector3 lastPos; static Quaternion lastRot; static float lastSize;

        static StoreyLodEditor()
        {
            StoreySite.EditorCamera = () => SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            SceneView.duringSceneGui += OnScene;
        }

        static void OnScene(SceneView sv)
        {
            if (Application.isPlaying || sv != SceneView.lastActiveSceneView) return;
            bool any = false, busy = false;
            foreach (var s in StoreySite.All)
            {
                if (s.lodMode != StoreySite.LodMode.Automatic) continue;
                any = true; busy |= s.Animating;
            }
            if (!any) return;
            var c = sv.camera; float size = c.pixelHeight * 1000f + (c.orthographic ? c.orthographicSize : c.fieldOfView);
            bool moved = c.transform.position != lastPos || c.transform.rotation != lastRot || size != lastSize;
            if (moved) { lastPos = c.transform.position; lastRot = c.transform.rotation; lastSize = size; }
            if (moved || busy) EditorApplication.QueuePlayerLoopUpdate();
            if (busy && Event.current.type == EventType.Repaint) sv.Repaint();

            var sel = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<StoreySite>() : null;
            if (sel == null || !sel.showLodStats || sel.LodStats is not SiteRenderer.LodStats st) return;
            Handles.BeginGUI();
            var r = new Rect(10, sv.position.height - 120, 400, 76);
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 10, r.height - 4), st.ToString(), EditorStyles.miniLabel);
            Handles.EndGUI();
        }
    }
}
