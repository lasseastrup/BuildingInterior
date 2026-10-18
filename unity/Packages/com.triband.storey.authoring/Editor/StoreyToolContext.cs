#nullable enable
using System;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The Storey tool context (docs/EDITOR.md §2): entered by clicking a building in the Scene view, by a tab in the site's
    /// inspector, or by choosing it in the Scene view's tool context menu for a selected Storey Site. While it is active the
    /// Scene view's tools are the Shape, Facade and Interior tools, which edit the layout. The built-in Move, Rotate, Scale,
    /// Rect and Transform tools are off, so the site cannot be moved by accident while its buildings are edited. Clicking
    /// empty space does not leave the context (Unity 6000.3 keeps every context but the GameObject one on such a click).
    /// Switching back to the GameObject context keeps the edit open. Frame Selected (F) frames the building being edited.
    /// </summary>
    [EditorToolContext("Storey", typeof(StoreySite))]
    internal sealed class StoreyToolContext : EditorToolContext
    {
        protected override Type? GetEditorToolType(Tool tool) => null;

        public override void OnActivated()
        {
            var site = SelectedSite();
            if (site == null || site.layout == null) return;
            var e = StoreyEdit.Begin(site);
            var tab = e.View.tab;
            // a context change settles the tool after this call: pick the tab's tool once it has
            EditorApplication.delayCall += () => { if (ToolManager.activeContextType == typeof(StoreyToolContext)) ShowTool(tab); };
        }

        /// <summary>The site selected in the Hierarchy, if any.</summary>
        public static StoreySite? SelectedSite() =>
            Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<StoreySite>() : null;

        /// <summary>Enter the Storey context with the tool for <paramref name="tab"/>.</summary>
        public static void Show(StoreyTab tab)
        {
            if (ToolManager.activeContextType == typeof(StoreyToolContext)) { ShowTool(tab); return; }
            if (TryEnter()) { ShowTool(tab); return; }
            // Unity makes the selection's contexts when the inspector is rebuilt, which has not happened yet after a script
            // reload: rebuild it (not inside this inspector's own GUI, which it would tear down) and try again
            EditorApplication.delayCall += () =>
            {
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                if (TryEnter()) ShowTool(tab);
                else Debug.LogWarning("Storey: select the Storey Site in the Hierarchy to edit it (the Storey tool context needs it selected).");
            };
        }

        static bool TryEnter()
        {
            try { ToolManager.SetActiveContext<StoreyToolContext>(); return true; }
            catch (InvalidOperationException) { return false; }   // Unity has no Storey context for the selection (yet)
        }

        static void ShowTool(StoreyTab tab)
        {
            if (tab == StoreyTab.Shape) ToolManager.SetActiveTool<StoreyShapeTool>();
            else if (tab == StoreyTab.Facade) ToolManager.SetActiveTool<StoreyFacadeTool>();
            else ToolManager.SetActiveTool<StoreyInteriorTool>();
        }

        // ---- a click on a building edits it ----

        // the last left click in a Scene view, as a ray in world space, until the selection it makes is looked at
        static Ray? click;

        [InitializeOnLoadMethod]
        static void Hooks()
        {
            SceneView.duringSceneGui += _ =>
            {
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && !ev.alt) click = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
            };
            Selection.selectionChanged += Clicked;
        }

        /// <summary>
        /// A building clicked in the Scene view outside the Storey context: Unity selects the mesh under the pointer, which the
        /// site made and nobody edits. The site is selected in its place and the building edited (<see cref="Edit"/>). A click
        /// that adds to a selection, a box drawn round it (the selection changes while the mouse is held), and a mesh picked
        /// in the Hierarchy (to look at it) are left as they are.
        /// </summary>
        static void Clicked()
        {
            var ray = click; click = null;
            var go = Selection.activeGameObject;
            if (ray == null || go == null || Selection.objects.Length != 1 || GUIUtility.hotControl != 0 || EditorWindow.focusedWindow is not SceneView) return;
            var site = go.GetComponentInParent<StoreySite>();
            if (site == null || site.Renderer == null || !site.Renderer.Made(go.transform)) return;
            // once Unity has finished with the selection it made; one undo step with it, so undo never selects the mesh
            int group = Undo.GetCurrentGroup();
            EditorApplication.delayCall += () =>
            {
                if (Selection.activeGameObject != go) return;
                Edit(site, ray.Value);
                Undo.CollapseUndoOperations(group);
            };
        }

        /// <summary>
        /// Edit the building a world-space ray meets in a site: the site selected, the building under the ray picked (the one
        /// picked before stays when the ray meets none), and the Storey context entered with the site's last tab's tool. A
        /// Storey tool already in use stays.
        /// </summary>
        internal static void Edit(StoreySite site, Ray ray)
        {
            click = null;   // the selection made here is no click's
            if (site.layout == null) return;
            if (Selection.activeGameObject != site.gameObject) Selection.activeGameObject = site.gameObject;
            var e = StoreyEdit.Begin(site);
            var (o, d) = InSite(site, ray);
            var hit = Picking.Building(e.Document, o, d);
            if (hit.index >= 0) e.View.Select(e.Document.buildings[hit.index].id);
            if (TabOf(ToolManager.activeToolType) is StoreyTab inUse) e.View.tab = inUse;
            SceneView.RepaintAll();
            // Unity settles the selection's contexts after this: enter the Storey context once it has (Show waits longer if it must)
            var tab = e.View.tab;
            EditorApplication.delayCall += () => { if (SelectedSite() == site && TabOf(ToolManager.activeToolType) != tab) Show(tab); };
        }

        /// <summary>The tab a Storey tool edits; null for any other tool.</summary>
        static StoreyTab? TabOf(Type tool) =>
            tool == typeof(StoreyShapeTool) ? StoreyTab.Shape : tool == typeof(StoreyFacadeTool) ? StoreyTab.Facade : tool == typeof(StoreyInteriorTool) ? StoreyTab.Interior : (StoreyTab?)null;

        /// <summary>
        /// The site other than <paramref name="except"/> with a building a world-space ray meets nearer than
        /// <paramref name="within"/>, the nearest; null when none has. Sites are unrotated and unscaled, so the distances
        /// along the ray compare.
        /// </summary>
        internal static StoreySite? SiteAt(Ray ray, StoreySite except, double within)
        {
            StoreySite? best = null;
            foreach (var s in StoreySite.All)
            {
                if (s == except || s.Renderer == null || s.Loading || s.Shown is not StoreyDocument doc) continue;
                var (o, d) = InSite(s, ray);
                var hit = Picking.Building(doc, o, d);
                if (hit.index >= 0 && hit.distance < within) { within = hit.distance; best = s; }
            }
            return best;
        }

        /// <summary>A world-space ray in a site's space, which is the layout's.</summary>
        static (Vec3d origin, Vec3d dir) InSite(StoreySite site, Ray ray)
        {
            var m = site.transform.worldToLocalMatrix;
            Vector3 o = m.MultiplyPoint(ray.origin), d = m.MultiplyVector(ray.direction);
            return (new Vec3d(o.x, o.y, o.z), new Vec3d(d.x, d.y, d.z));
        }

        /// <summary>
        /// What Frame Selected (F) frames in the Storey context, in world space: the building being edited, or in the
        /// Interior tab its active storey (the floors above are hidden there). Null outside the context, or with no building
        /// selected: Unity frames the site then.
        /// </summary>
        internal static Bounds? FrameBounds(StoreySite site)
        {
            if (ToolManager.activeContextType != typeof(StoreyToolContext) || StoreyEdit.Of(site) is not StoreyEdit e || e.Selected is not BuildingData b) return null;
            double x0, z0, x1, z1, y0 = b.elevation, y1 = Derived.RoofY(b);
            if (e.View.tab == StoreyTab.Interior && b.interior)
            {
                int k = Math.Min(e.View.floor, b.floors.Count);
                (_, y0, y1) = Picking.StoreyView(b, k);
                x0 = z0 = double.MaxValue; x1 = z1 = double.MinValue;
                foreach (var p in Derived.OutlineAt(b, k)) { x0 = Math.Min(x0, p.x); z0 = Math.Min(z0, p.z); x1 = Math.Max(x1, p.x); z1 = Math.Max(z1, p.z); }
                x0 += b.pos.x; x1 += b.pos.x; z0 += b.pos.z; z1 += b.pos.z;
            }
            else (x0, z0, x1, z1) = Generate.Site.BoundsOf(b);   // every tier's outline
            var centre = new Vector3((float)((x0 + x1) / 2), (float)((y0 + y1) / 2), (float)((z0 + z1) / 2));
            return new Bounds(site.transform.TransformPoint(centre), new Vector3((float)(x1 - x0), (float)(y1 - y0), (float)(z1 - z0)));
        }
    }
}
