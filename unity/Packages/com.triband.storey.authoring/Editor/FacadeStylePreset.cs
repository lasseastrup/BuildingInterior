#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A facade style kept as an asset (Plan §4.1), shared and versioned. Every preset in the Facade tab is one: the package
    /// ships the prototype's five (in Facade Styles/), and Save as preset… adds the project's own, the same kind of asset.
    /// Applying one keeps the building's roof, and the style carries the preset's key (<see cref="FacadeStyle.preset"/>)
    /// until a field of it is edited by hand. The style is stored as Storey's JSON for a style, because the model's
    /// optional fields do not survive Unity's serializer.
    /// </summary>
    [CreateAssetMenu(menuName = "Storey/Facade Style", fileName = "Facade Style")]
    public sealed class FacadeStylePreset : ScriptableObject
    {
        [SerializeField, TextArea(4, 16)] internal string style = Initial();

        [Tooltip("The key a style applied from it carries: a name for the package's own five (brick, glass…), which layouts name them by. Empty for a project's: its asset's GUID is its key.")]
        [SerializeField] internal string key = "";

        /// <summary>The folder of the presets the package ships.</summary>
        internal const string Folder = "Packages/com.triband.storey.authoring/Facade Styles/";

        /// <summary>The preset a new building starts as: the prototype's brick walk-up.</summary>
        internal const string DefaultKey = "brick";

        public FacadeStyle Style => PrototypeJson.ReadStyle(style);

        // a new one (Create ▸ Storey ▸ Facade Style) starts as the brick walk-up, kept as Set keeps a style: with no key of its own
        static string Initial() { var s = Styles.Preset("brick"); s.preset = null; return PrototypeJson.WriteStyle(s); }

        /// <summary>
        /// Keep <paramref name="s"/>, named <paramref name="label"/> (the asset's name, as the package's own are): the asset's
        /// key is its own, not the preset's the style came from.
        /// </summary>
        internal void Set(FacadeStyle s, string? label = null)
        {
            var c = s.Clone(); c.preset = null;
            if (label != null) c.label = label;
            style = PrototypeJson.WriteStyle(c);
        }

        /// <summary>A preset in the list: its asset, the key a style applied from it carries, and whether the package ships it.</summary>
        internal sealed class Entry
        {
            public FacadeStylePreset asset = null!;
            public string guid = "", key = "";
            public bool own;
        }

        // every preset, searched once and kept until the project changes (the inspector redraws often, and a search of a
        // big project's AssetDatabase is slow)
        static List<Entry>? all;
        static bool hooked;

        /// <summary>Every Facade Style asset: the package's own first, in the prototype's order, then the project's by name.</summary>
        internal static IReadOnlyList<Entry> All()
        {
            if (!hooked) { hooked = true; EditorApplication.projectChanged += () => all = null; }
            if (all != null && all.All(e => e.asset != null)) return all;
            var found = new List<Entry>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(FacadeStylePreset)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<FacadeStylePreset>(path);
                if (a == null) continue;
                // a copy of one of the package's put in the project is the project's, known by its own GUID
                bool own = a.key.Length > 0 && path.StartsWith(Folder, StringComparison.Ordinal);
                found.Add(new Entry { asset = a, guid = guid, key = own ? a.key : guid, own = own });
            }
            var order = Styles.Presets.Select((p, i) => (p.key, i)).ToDictionary(x => x.key, x => x.i);
            List<Entry> Sorted() => found.OrderBy(e => e.own ? 0 : 1).ThenBy(e => order.TryGetValue(e.key, out int i) ? i : int.MaxValue)
                .ThenBy(e => e.asset.name, StringComparer.OrdinalIgnoreCase).ToList();
            // a copy made in the package's own folder (Duplicate) carries the key too: the first keeps it, a copy is the
            // project's
            var seen = new HashSet<string>();
            foreach (var e in Sorted()) if (e.own && !seen.Add(e.key)) { e.own = false; e.key = e.guid; }
            return all = Sorted();
        }

        /// <summary>The preset a style carries the key of (applied from it, and not edited by hand since), or null: Custom.</summary>
        internal static Entry? Of(FacadeStyle s) => s.preset == null ? null : All().FirstOrDefault(e => e.key == s.preset);

        /// <summary>The style of the preset a new building starts as, with its key; null when it is missing or broken.</summary>
        internal static (FacadeStyle style, string key)? Default()
        {
            var e = All().FirstOrDefault(x => x.own && x.key == DefaultKey);
            if (e == null) return null;
            try { return (e.asset.Style, e.key); }
            catch (Exception) { return null; }   // edited into something that isn't a style: the prototype's brick, as before
        }
    }
}
