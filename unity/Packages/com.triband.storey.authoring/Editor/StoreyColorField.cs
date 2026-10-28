#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// How a style's colour fields are picked (docs/COLOURS.md §3.8). The default picks CSS colours with Unity's colour
    /// field and the prototype's suggested swatches; the Color Pipeline integration installs one that picks palette
    /// entries with Color Pipeline's own picker and the project's suggested palette colours.
    /// </summary>
    public interface IStoreyColorPicker
    {
        /// <summary>
        /// Draw one colour field and return its new value, or null when unchanged. <paramref name="key"/> names the field
        /// uniquely (for pickers that answer later); <paramref name="field"/> is the style field ("wall", "trim", …);
        /// <paramref name="lookup"/> resolves palette ids through the layout's palette block when nothing better is known.
        /// </summary>
        string? Draw(string key, string label, string value, string field, Func<string, PaletteEntry?> lookup);
    }

    public static class StoreyColorField
    {
        /// <summary>The installed picker; Color Pipeline's replaces the default when its package is present.</summary>
        public static IStoreyColorPicker Picker { get; set; } = new HexPicker();

        /// <summary>
        /// Keeps a layout inside the palette: replaces every colour the palette lacks with its nearest entry and says what
        /// it changed. Run after every edit and when an edit opens (<c>StoreyEdit</c>). Null without a palette to keep to;
        /// Color Pipeline's integration installs <see cref="PaletteConform.Apply"/> over its palette.
        /// </summary>
        public static Func<StoreyDocument, List<ConformedColor>>? Conform { get; set; }

        /// <summary>The colours a style that leaves one out gets (the project's, with Color Pipeline); null for the prototype's.</summary>
        public static Func<StyleDefaults?>? Defaults { get; set; }

        /// <summary>
        /// Store a palette colour for every default the project leaves unset (with Color Pipeline: in Storey Color
        /// Settings, created if missing). Null when there is nothing to store them in. Returns how many it set.
        /// </summary>
        public static Func<int>? FillDefaults { get; set; }

        /// <summary>
        /// The palette entry at an index, its id and name: what a model coloured by Color Pipeline's Model Remapper names in
        /// its vertices (docs/OPENINGS.md §6). Null without a palette; Color Pipeline's integration installs it.
        /// </summary>
        public static Func<int, (string id, string name)?>? PaletteEntryAt { get; set; }

        /// <summary>
        /// A component's palette colour remap, original palette id to the one drawn instead (Color Pipeline's
        /// <c>ColorRemap</c> on a prefab's renderer), for a prefab's bake to keep (docs/OPENINGS.md §6); null for any other
        /// component, and without a palette. Color Pipeline's integration installs it. Storey's own.
        /// </summary>
        public static Func<UnityEngine.Component, IReadOnlyDictionary<string, string>?>? RemapOf { get; set; }

        /// <summary>
        /// Whether a material draws with the palette (Color Pipeline's lit shader), so a Model Remapper's palette indices on a
        /// mesh drawn with it are its colours; one that doesn't (a window shader of its own) is a part by its material, though
        /// its mesh carries indices. Null without a palette: every material is taken to. Color Pipeline's integration installs
        /// it. Storey's own.
        /// </summary>
        public static Func<UnityEngine.Material, bool>? PaletteMaterial { get; set; }

        sealed class HexPicker : IStoreyColorPicker
        {
            public string? Draw(string key, string label, string value, string field, Func<string, PaletteEntry?> lookup)
            {
                string hex = ColorRef.IsPaletteId(value) ? (lookup(value)?.hex ?? "#FF00FF") : value;
                ColorUtility.TryParseHtmlString(hex, out var c);
                string? result = null;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var nc = EditorGUILayout.ColorField(new GUIContent(label, ColorRef.IsPaletteId(value) ? "Palette colour " + (lookup(value)?.name ?? value) : hex), c, true, false, false);
                    if (EditorGUI.EndChangeCheck()) result = "#" + ColorUtility.ToHtmlStringRGB(nc);
                }
                if (Styles.Suggested.TryGetValue(field, out var sw)) result = Swatches(sw, hex) ?? result;
                return result;
            }
        }

        /// <summary>A row of suggested colours (CSS literals); returns the one clicked.</summary>
        public static string? Swatches(IReadOnlyList<string> suggested, string current)
        {
            string? r = null;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                foreach (var s in suggested)
                {
                    ColorUtility.TryParseHtmlString(s, out var c);
                    var rect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18), GUILayout.Height(18));
                    EditorGUI.DrawRect(rect, c);
                    if (string.Equals(s, current, StringComparison.OrdinalIgnoreCase)) EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), Color.white);
                    if (GUI.Button(rect, GUIContent.none, GUIStyle.none)) r = s;
                }
            }
            return r;
        }
    }
}
