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
    /// The Storey tool context (docs/EDITOR.md §2), always listed in the Scene view's Active Context menu: the mesh a click
    /// on a building selects (as for any object) has no Storey Site for a context to target. Choosing Storey with that mesh
    /// selected edits its building: the site is selected in the mesh's place and the building picked in it. With the site
    /// itself selected, it edits the building picked before; a tab in the site's inspector enters it too. While it is
    /// active the Scene view's tools are the Shape, Facade and Interior tools, which edit the layout, and a click on a
    /// building (any site's) edits it. The built-in Move, Rotate, Scale, Rect and Transform tools are off, so the site
    /// cannot be moved by accident while its buildings are edited. Clicking empty space does not leave the context;
    /// selecting another object goes back to the GameObject context, as choosing it does, which keeps the edit open. Frame
    /// Selected (F) frames the building being edited.
    /// </summary>
    [EditorToolContext("Storey")]
    internal sealed class StoreyToolContext : EditorToolContext
    {
        protected override Type? GetEditorToolType(Tool tool) => null;

        // after Unity has finished switching contexts: the selection may change
        public override void OnActivated() => EditorApplication.delayCall += EditSelected;

        /// <summary>Edit what is selected: a site (the building picked before), or a mesh a site draws a building with (that building).</summary>
        static void EditSelected()
        {
            var go = Selection.activeGameObject;
            if (go == null || ToolManager.activeContextType != typeof(StoreyToolContext)) return;
            if (go.GetComponent<StoreySite>() is StoreySite site) Edit(site, null);
            else if (MadeBy(go) is StoreySite owner) Edit(owner, picked.mesh == go ? picked.id : owner.Renderer!.BuildingOf(go));
        }

        /// <summary>
        /// With no site selected the Storey tools are not there to take a click: the context takes it. A building clicked is
        /// edited; anything else is selected as Unity would, which leaves the context (<see cref="SelectionChanged"/>).
        /// </summary>
        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView || SelectedSite() != null) return;
            StoreyTool.Hint("Click a building to edit it. Choose the GameObject context to leave.");
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var ev = Event.current;
            if (ev.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            else if (ev.type == EventType.MouseDown && ev.button == 0 && !ev.alt && HandleUtility.nearestControl == id)
            {
                var go = HandleUtility.PickGameObject(ev.mousePosition, false);
                if (go != null && MadeBy(go) is StoreySite site) Edit(site, site.Renderer!.BuildingOf(go) ?? BuildingAt(site, HandleUtility.GUIPointToWorldRay(ev.mousePosition)));
                else Selection.activeGameObject = go != null ? HandleUtility.PickGameObject(ev.mousePosition, true) : null;
                ev.Use();
            }
        }

        /// <summary>The site selected in the Hierarchy, if any.</summary>
        public static StoreySite? SelectedSite() =>
            Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<StoreySite>() : null;

        /// <summary>Enter the Storey context with the tool for <paramref name="tab"/>.</summary>
        public static void Show(StoreyTab tab)
        {
            if (TryShow(tab)) return;
            // Unity makes the selection's tools when the inspector is rebuilt, which has not happened yet after a selection
            // change or a script reload: rebuild it (not inside this inspector's own GUI, which it would tear down) and try again
            EditorApplication.delayCall += () =>
            {
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                if (!TryShow(tab)) Debug.LogWarning("Storey: select the Storey Site in the Hierarchy to edit it (the Storey tools need it selected).");
            };
        }

        static bool TryShow(StoreyTab tab)
        {
            try
            {
                if (ToolManager.activeContextType != typeof(StoreyToolContext)) ToolManager.SetActiveContext<StoreyToolContext>();
                if (tab == StoreyTab.Shape) ToolManager.SetActiveTool<StoreyShapeTool>();
                else if (tab == StoreyTab.Facade) ToolManager.SetActiveTool<StoreyFacadeTool>();
                else ToolManager.SetActiveTool<StoreyInteriorTool>();
                return true;
            }
            catch (InvalidOperationException) { return false; }   // Unity has no Storey tools for the selection (yet)
        }

        /// <summary>The tab a Storey tool edits; null for any other tool.</summary>
        static StoreyTab? TabOf(Type tool) =>
            tool == typeof(StoreyShapeTool) ? StoreyTab.Shape : tool == typeof(StoreyFacadeTool) ? StoreyTab.Facade : tool == typeof(StoreyInteriorTool) ? StoreyTab.Interior : (StoreyTab?)null;

        /// <summary>
        /// Edit a building of a site: the site selected in place of what was, the building with <paramref name="id"/> picked
        /// (with null, or one the layout lacks, the one picked before), and the Storey tool in use kept, or the site's last
        /// tab's shown.
        /// </summary>
        internal static void Edit(StoreySite site, string? id)
        {
            click = null;   // the selection made here is no click's
            if (site.layout == null) return;
            if (Selection.activeGameObject != site.gameObject) Selection.activeGameObject = site.gameObject;
            var e = StoreyEdit.Begin(site);
            if (id != null && e.Document.buildings.Exists(x => x.id == id)) e.View.Select(id);
            if (TabOf(ToolManager.activeToolType) is StoreyTab inUse) e.View.tab = inUse;
            SceneView.RepaintAll();
            // Unity makes the site's tools once it has the selection: show the tab's then (Show waits longer if it must)
            var tab = e.View.tab;
            EditorApplication.delayCall += () =>
            {
                if (SelectedSite() == site && ToolManager.activeContextType == typeof(StoreyToolContext) && TabOf(ToolManager.activeToolType) != tab) Show(tab);
            };
        }

        // ---- what a click selected ----

        // the last left click in a Scene view, as a ray in world space, until the selection it makes is looked at
        static Ray? click;
        // the mesh of a site's last selected, and the building it draws (the one under the click, for a merged cell): what
        // choosing the Storey context edits
        static (GameObject? mesh, string? id) picked;

        [InitializeOnLoadMethod]
        static void Hooks()
        {
            SceneView.duringSceneGui += _ =>
            {
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && !ev.alt) click = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
            };
            Selection.selectionChanged += SelectionChanged;
        }

        /// <summary>
        /// A selection is left as it was made. A mesh of a site's has the building it draws noted, for when the Storey context
        /// is chosen (a merged cell draws many: the one under the click that selected it). In the Storey context a site
        /// selected shows its tools, and another object in the scene goes back to the GameObject context, where its
        /// transform tools are; a mesh of a site's, nothing or an asset stays in it.
        /// </summary>
        static void SelectionChanged()
        {
            var ray = click; click = null;
            var go = Selection.activeGameObject;
            var site = go != null ? MadeBy(go) : null;
            bool clicked = ray != null && GUIUtility.hotControl == 0 && EditorWindow.focusedWindow is SceneView;
            picked = site != null ? (go, site.Renderer!.BuildingOf(go!) ?? (clicked ? BuildingAt(site, ray!.Value) : null)) : (null, null);
            if (go == null || site != null || ToolManager.activeContextType != typeof(StoreyToolContext)) return;
            // once Unity has finished with the selection
            EditorApplication.delayCall += () =>
            {
                if (Selection.activeGameObject != go || ToolManager.activeContextType != typeof(StoreyToolContext)) return;
                if (go.GetComponent<StoreySite>() is StoreySite s) Edit(s, null);
                else if (!EditorUtility.IsPersistent(go)) ToolManager.SetActiveContext<GameObjectToolContext>();
            };
        }

        /// <summary>The site that made an object to draw its buildings with (a building's mesh, a merged cell), or null.</summary>
        static StoreySite? MadeBy(GameObject go)
        {
            var site = go.GetComponentInParent<StoreySite>();
            return site != null && site.Renderer != null && site.Renderer.Made(go.transform) ? site : null;
        }

        /// <summary>The building of a site a world-space ray meets first, by id, or null.</summary>
        static string? BuildingAt(StoreySite site, Ray ray)
        {
            if (site.Shown is not StoreyDocument doc) return null;
            var (o, d) = InSite(site, ray);
            var hit = Picking.Building(doc, o, d);
            return hit.index >= 0 ? doc.buildings[hit.index].id : null;
        }

        /// <summary>
        /// The site other than <paramref name="except"/> with a building a world-space ray meets nearer than
        /// <paramref name="within"/>, the nearest, and that building's id; null when none has. Sites are unrotated and
        /// unscaled, so the distances along the ray compare.
        /// </summary>
        internal static (StoreySite site, string id)? SiteAt(Ray ray, StoreySite except, double within)
        {
            (StoreySite, string)? best = null;
            foreach (var s in StoreySite.All)
            {
                if (s == except || s.Renderer == null || s.Loading || s.Shown is not StoreyDocument doc) continue;
                var (o, d) = InSite(s, ray);
                var hit = Picking.Building(doc, o, d);
                if (hit.index >= 0 && hit.distance < within) { within = hit.distance; best = (s, doc.buildings[hit.index].id); }
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

        /// <summary>
        /// A building is being edited: a Storey tool is active on a site with a building picked. The Storey Floors overlay
        /// shows only then.
        /// </summary>
        internal static bool Editing =>
            typeof(StoreyTool).IsAssignableFrom(ToolManager.activeToolType) && SelectedSite() is StoreySite s && StoreyEdit.Of(s)?.Selected != null;
    }
}
