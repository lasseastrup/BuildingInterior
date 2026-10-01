#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Edit;
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
