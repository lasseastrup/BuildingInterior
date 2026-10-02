#nullable enable
using UnityEngine;

namespace Triband.Storey.Editor
{
    public enum StoreyTab { Shape, Facade, Interior }

    public enum InteriorTool { Select, Wall, Door, Erase, Stairs, Lift }

    /// <summary>
    /// What the editor is looking at while a layout is edited: the building, tab, outline tier, active floor and tools.
    /// A separate object from <see cref="StoreyEdit"/> so undo never changes it (undo steps are edits, not clicks).
    /// </summary>
    public sealed class StoreyEditView : ScriptableObject
    {
        [SerializeField] internal string selectedId = "";
        [SerializeField] internal StoreyTab tab;
        /// <summary>The outline the Shape and Facade tabs edit: 0 the base, otherwise the floor a setback starts at.</summary>
        [SerializeField] internal int tier;
        /// <summary>The storey the Interior tab edits; the floor count means the roof.</summary>
        [SerializeField] internal int floor;
        [SerializeField] internal InteriorTool interiorTool;
        /// <summary>The Facade tab's click tool: "", "entrance", "blank", or a detail kind ("ac", "vent", "dish", "escape", "awning").</summary>
        [SerializeField] internal string facadeTool = "";
        [SerializeField] internal int placeRot;
        /// <summary>What the Stairs tool places: switchback stairs or straight flights (both over the same floors).</summary>
        [SerializeField] internal CoreType stairKind = CoreType.Stairs;
        [SerializeField] internal bool isolate;
        /// <summary>The Scene view camera follows the active floor up and down (the Interior tab).</summary>
        [SerializeField] internal bool followFloor = true;
        [SerializeField] internal string selectedCore = "";
        [SerializeField] internal int selectedWall = -1;
        [SerializeField] internal bool showMore;
        /// <summary>The Shape tab's selected corner of the tier's outline (a click on it), or -1.</summary>
        [SerializeField] internal int selectedCorner = -1;
        [SerializeField] internal Edit.CornerShape cornerShape = Edit.CornerShape.Chamfer;
        [SerializeField] internal float cornerSize = 2f;
        [SerializeField] internal bool cornerDoor;
        /// <summary>The courtyard or atrium the Shape tool's handles edit, by id; "" for none.</summary>
        [SerializeField] internal string selectedVoid = "";
        [SerializeField] internal bool showProblems = true;

        internal void Select(string id)
        {
            if (selectedId == id) return;
            selectedId = id; tier = 0; floor = 0; selectedCore = ""; selectedWall = -1; selectedCorner = -1; selectedVoid = "";
        }
    }
}
