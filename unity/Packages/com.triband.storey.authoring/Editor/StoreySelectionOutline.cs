#nullable enable
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The Scene view's selection outline while a building is edited (docs/EDITOR.md §2): on that building alone. The site is
    /// what is selected, and Unity outlines a selected object's children too, so every building of the site had it. Unity
    /// draws the outline with each material's selection pass, and Storey's keeps one building's (StoreyLit.hlsl,
    /// <see cref="StoreyShaderIds.Outline"/>): in the Storey context, the building picked in the selected site, or none while
    /// none is; anywhere else, every building, as Unity gives it. The shaders do it, not the renderers' selected state
    /// (EditorUtility.SetSelectedRenderState): the GPU Resident Drawer outlines whatever is under the selection, past it.
    /// </summary>
    [InitializeOnLoad]
    static class StoreySelectionOutline
    {
        /// <summary>The value that outlines no building: one past the table's rows.</summary>
        const float None = BuildingTable.MaxBuildings + 1;

        // the value the shaders have: none until the first update sets it, as a reload leaves the global as it was
        static float shown = float.NaN;

        static StoreySelectionOutline() => EditorApplication.update += Update;

        static void Update()
        {
            float want = Wanted();
            if (want == shown) return;
            shown = want;
            Shader.SetGlobalFloat(StoreyShaderIds.Outline, want);
            SceneView.RepaintAll();
        }

        /// <summary>The outlined building's index plus one: 0 (every building) but in the Storey context with a site selected.</summary>
        static float Wanted()
        {
            if (ToolManager.activeContextType != typeof(StoreyToolContext) || !(StoreyToolContext.SelectedSite() is StoreySite s)) return 0;
            string? id = StoreyEdit.Of(s)?.View.selectedId;
            int row = string.IsNullOrEmpty(id) ? -1 : s.TableIndexOf(id!);
            return row >= 0 ? row + 1 : None;
        }
    }
}
