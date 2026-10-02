#nullable enable
using System;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The Storey tool context (docs/EDITOR.md §2): chosen in the Scene view's tool context menu for a selected Storey Site,
    /// or by <b>Edit layout</b>. While it is active the Scene view's tools are the Shape, Facade and Interior tools, which
    /// edit the layout. The built-in Move, Rotate, Scale, Rect and Transform tools are off, so the site cannot be moved by
    /// accident while its buildings are edited. Clicking empty space does not leave the context (Unity 6000.3 keeps
    /// every context but the GameObject one on such a click). Switching back to the GameObject context keeps the edit
    /// open; <b>Stop editing</b> ends it.
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
            if (ToolManager.activeContextType != typeof(StoreyToolContext)) ToolManager.SetActiveContext<StoreyToolContext>();
            ShowTool(tab);
        }

        static void ShowTool(StoreyTab tab)
        {
            if (tab == StoreyTab.Shape) ToolManager.SetActiveTool<StoreyShapeTool>();
            else if (tab == StoreyTab.Facade) ToolManager.SetActiveTool<StoreyFacadeTool>();
            else ToolManager.SetActiveTool<StoreyInteriorTool>();
        }

        /// <summary>Back to the GameObject context, after an edit ends.</summary>
        public static void Leave()
        {
            if (ToolManager.activeContextType == typeof(StoreyToolContext)) ToolManager.SetActiveContext<GameObjectToolContext>();
        }
    }
}
