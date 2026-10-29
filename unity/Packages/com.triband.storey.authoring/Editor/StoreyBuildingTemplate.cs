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
    /// doors, details and style. Every building the building bar's <b>New</b> makes comes from one: the package ships the
    /// prototype's five footprints as 3-storey buildings (Building Templates/: Rect, L, U, T, Octa), and <b>Save as
    /// template</b> adds the project's own, the same kind of asset. The Shape tab's <b>Start from</b> takes a template's
    /// outline. Stored as Storey's JSON for one building, as the prototype copies one, because the model's optional fields
    /// do not survive Unity's serializer.
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

        /// <summary>The folder of the templates the package ships, and theirs in the order New lists them (the prototype's).</summary>
        internal const string Folder = "Packages/com.triband.storey.authoring/Building Templates/";
        static readonly string[] Starters = { "Rect", "L", "U", "T", "Octa" };

        /// <summary>One of the package's own, which New lists first and Start from shows as buttons.</summary>
        internal bool Own => AssetDatabase.GetAssetPath(this).StartsWith(Folder, System.StringComparison.Ordinal);

        // every template, searched once and kept until the project changes (the Shape tab asks on every redraw, and a search
        // of a big project's AssetDatabase is slow)
        static StoreyBuildingTemplate[]? all;
        static bool hooked;

        /// <summary>Every template that holds a building: the package's first, in the prototype's order, then the project's by name.</summary>
        internal static StoreyBuildingTemplate[] All()
        {
            if (!hooked) { hooked = true; EditorApplication.projectChanged += () => all = null; }
            if (all != null && all.All(a => a != null)) return all;
            int Order(StoreyBuildingTemplate a) { int i = System.Array.IndexOf(Starters, a.name); return i < 0 ? int.MaxValue : i; }
            return all = AssetDatabase.FindAssets("t:" + nameof(StoreyBuildingTemplate)).Select(g => AssetDatabase.LoadAssetAtPath<StoreyBuildingTemplate>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(a => a != null && a.Building != null).Select(a => a!)
                .OrderBy(a => a.Own ? 0 : 1).ThenBy(a => a.Own ? Order(a) : 0).ThenBy(a => a.name, System.StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>Search again next time: a template was just made, copied, renamed or deleted (the project's change comes later).</summary>
        internal static void Changed() => all = null;

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
            EditorGUILayout.HelpBox(t.Own
                ? "One of the package's own: a Storey Site's New lists it first, and the Shape tab's Start from takes its outline. To make one of your own, edit a building placed from it and Save as template."
                : "Place it from a Storey Site: Edit layout, then New ▸ Project ▸ " + t.name + ". The Shape tab's Start from ▸ Project takes its outline. To change it, edit a building and save it over this one.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
        }
    }
}
