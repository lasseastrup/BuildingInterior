#nullable enable
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A building kept as an asset to place again (docs/EDITOR.md §6.6): its outlines, floors, rooms, cores, courtyards,
    /// doors, details and style. Saved from a site's building bar (<b>Save as template</b>) and placed with <b>New ▸ From
    /// template</b>. Stored as Storey's JSON for one building, as the prototype copies one, because the model's optional
    /// fields do not survive Unity's serializer.
    /// </summary>
    [CreateAssetMenu(menuName = "Storey/Building Template", fileName = "Building Template")]
    public sealed class StoreyBuildingTemplate : ScriptableObject
    {
        [SerializeField, TextArea(4, 16)] internal string building = "";

        /// <summary>The building, or null when the asset holds none (made from the Create menu and not saved into yet).</summary>
        public BuildingData? Building
        {
            get
            {
                if (string.IsNullOrWhiteSpace(building)) return null;
                try { return PrototypeJson.ReadBuilding(building); } catch (System.FormatException) { return null; }
            }
        }

        internal void Set(BuildingData b) => building = PrototypeJson.Write(Buildings.AsTemplate(b));

        /// <summary>Every template in the project, by name.</summary>
        internal static StoreyBuildingTemplate[] All() =>
            AssetDatabase.FindAssets("t:" + nameof(StoreyBuildingTemplate)).Select(g => AssetDatabase.LoadAssetAtPath<StoreyBuildingTemplate>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null && a.Building != null).OrderBy(a => a.name).ToArray();

        /// <summary>A line about what it holds, for menus and its inspector.</summary>
        internal string Summary()
        {
            var b = Building; if (b == null) return "Empty: save a building into it from a Storey Site.";
            var bb = Tiers.Bbox(b.footprint);
            return $"{b.floors.Count} storeys, {bb.x1 - bb.x0:0.#} × {bb.z1 - bb.z0:0.#} m, {Derived.RoofY(b):0.#} m tall" + (b.shafts.Count > 0 ? $", {b.shafts.Count} stairs and lifts" : "") + (b.voids.Count > 0 ? $", {b.voids.Count} courtyards" : "");
        }
    }

    [CustomEditor(typeof(StoreyBuildingTemplate))]
    internal sealed class StoreyBuildingTemplateEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var t = (StoreyBuildingTemplate)target;
            EditorGUILayout.HelpBox(t.Summary(), MessageType.None);
            EditorGUILayout.HelpBox("Place it from a Storey Site: Edit layout, then New ▸ From template. To change it, edit a building and save it over this one.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
}
