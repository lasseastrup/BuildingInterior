#nullable enable
using System;
using System.Collections.Generic;
using Triband.ColorPipeline.Editor;
using Triband.ColorPipeline.Runtime;
using Triband.Core.Utils;
using Triband.Storey.ColorPipeline;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor.ColorPipeline
{
    /// <summary>
    /// Style colours as Color Pipeline palette entries (docs/COLOURS.md §3.8): each field shows the entry's swatch and
    /// name and opens Color Pipeline's own picker; the project's suggested colours per field (<see cref="StoreyColorSettings"/>)
    /// show underneath. Installed in place of the hex picker whenever this assembly compiles, which is when
    /// com.triband.colorpipeline 2.1.11 up to 3.0 is in the project.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class ColorPipelinePicker : IStoreyColorPicker
    {
        static ColorPipelinePicker()
        {
            StoreyColorField.Picker = new ColorPipelinePicker();
            StoreyColorField.Defaults = () => StoreyColorSettings.Load()?.ToDefaults();
            StoreyColorField.FillDefaults = FillDefaults;
            StoreyColorField.PaletteEntryAt = i =>
            {
                ColorPaletteDefinition palette;
                try { palette = ColorPaletteDefinition.Instance; }
                catch (Exception) { return null; }   // no palette yet
                if (palette == null || i < 0 || i >= palette.Colors.Count) return null;
                var d = palette.Colors[i];
                return (ColorPipelinePalette.IdOf(d.ID), d.Name);
            };
            StoreyColorField.RemapOf = c =>
            {
                if (!(c is ColorRemap r)) return null;
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                var from = r.OriginalColors; var to = r.OverwriteColors;
                if (from != null && to != null)
                    for (int i = 0; i < from.Count && i < to.Count; i++) map[ColorPipelinePalette.IdOf(from[i])] = ColorPipelinePalette.IdOf(to[i]);
                return map;
            };
            // Color Pipeline's lit shader takes a palette offset (ColorMappingManager's remaps); a material without it doesn't
            // draw from the palette
            StoreyColorField.PaletteMaterial = m => m != null && m.HasProperty("_ColorPaletteOffset");
            // swatches and names for the Storey Library: a palette id as its entry, a CSS colour as the entry it renders as
            StoreyColorField.Describe = value =>
            {
                ColorPaletteDefinition palette;
                try { palette = ColorPaletteDefinition.Instance; }
                catch (Exception) { return null; }   // no palette yet
                if (palette == null) return null;
                if (ColorRef.IsPaletteId(value)) return palette.TryGetColor(ColorPipelinePalette.Guid(value), out var d) ? (d.Color, d.Name) : ((Color, string)?)null;
                if (!ColorUtility.TryParseHtmlString(value, out var c)) return null;
                return Nearest(palette, value) is ColorDefinition nd ? (nd.Color, nd.Name) : (c, value.ToUpperInvariant());
            };
            StoreyColorField.Conform = d =>
            {
                ColorPaletteDefinition palette;
                try { palette = ColorPaletteDefinition.Instance; }
                catch (Exception) { return new List<ConformedColor>(); }   // no palette yet: nothing to keep to
                return palette == null ? new List<ConformedColor>() : PaletteConform.Apply(d, ColorPipelinePalette.Entries(palette));
            };
        }

        // Color Pipeline's picker answers later, from its own window: the choice waits here for the field's next draw
        readonly Dictionary<string, string> picked = new Dictionary<string, string>(StringComparer.Ordinal);

        public string? Draw(string key, string label, string value, string field, Func<string, PaletteEntry?> lookup)
        {
            string? result = null;
            if (picked.TryGetValue(key, out var p)) { picked.Remove(key); result = p; }
            ColorPaletteDefinition palette;
            try { palette = ColorPaletteDefinition.Instance; }
            catch (Exception) { EditorGUILayout.HelpBox("Color Pipeline has no palette: create ColorPalette.palette in a Resources folder.", MessageType.Warning); return result; }

            Color c; string name; ColorDefinition? def = null;
            bool isId = ColorRef.IsPaletteId(value);
            if (isId && palette.TryGetColor(ColorPipelinePalette.Guid(value), out var d)) { def = d; c = d.Color; name = d.Name; }
            else if (isId) { var e = lookup(value); ColorUtility.TryParseHtmlString(e?.hex ?? "#FF00FF", out c); name = (e?.name ?? value) + " (not in the palette)"; }
            else if (Nearest(palette, value) is ColorDefinition nd) { def = nd; c = nd.Color; name = $"{nd.Name} (nearest to {value.ToUpperInvariant()})"; }   // what it renders as
            else { ColorUtility.TryParseHtmlString(value, out c); name = value; }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);
                var rect = GUILayoutUtility.GetRect(36, 18, GUILayout.Width(36), GUILayout.Height(18));
                EditorGUI.DrawRect(rect, c);
                // cut short where the field is narrow, never widening what it is in; the whole name in its tooltip
                GUILayout.Label(new GUIContent(name, name), EditorStyles.miniLabel, GUILayout.MinWidth(10));
                if (GUILayout.Button("Pick…", EditorStyles.miniButton, GUILayout.Width(48)))
                {
                    int sel = def != null ? palette.GetIndexOfColor(def.ID) : 0;
                    PaletteColorPickerWindow.Open(GUIUtility.GUIToScreenPoint(new Vector2(rect.x, rect.yMax)), sel, null, id =>
                    {
                        picked[key] = ColorPipelinePalette.IdOf(id);
                        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                    });
                }
            }

            var suggested = Suggested(field);
            if (suggested != null && suggested.Length > 0)
            {
                var shown = new List<ColorDefinition>();
                foreach (var g in suggested) if (g.valid && palette.TryGetColor(g, out var sd)) shown.Add(sd);
                // as many to a row as fit beside the label, on as many rows as it takes, so the field is never wider than
                // what it is in (the Storey Library's panel, a narrow inspector); the width is the last repaint's
                swatchWidths.TryGetValue(key, out float width);
                int per = Math.Max(1, (int)((width > 0 ? width : 200) / 20)), rows = (shown.Count + per - 1) / per;
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth);
                    var area = GUILayoutUtility.GetRect(20, rows * 20, GUILayout.ExpandWidth(true), GUILayout.MinWidth(20));
                    if (Event.current.type == EventType.Repaint) swatchWidths[key] = area.width;
                    for (int i = 0; i < shown.Count; i++)
                    {
                        var rect = new Rect(area.x + (i % per) * 20, area.y + (i / per) * 20, 18, 18);
                        EditorGUI.DrawRect(rect, shown[i].Color);
                        string id = ColorPipelinePalette.IdOf(shown[i].ID);
                        if (id == value) EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2, rect.width, 2), Color.white);
                        if (GUI.Button(rect, new GUIContent("", shown[i].Name), GUIStyle.none)) result = id;
                    }
                }
            }
            return result;
        }

        // each field's swatch row's width at its last repaint, for how many swatches fit in a row
        readonly Dictionary<string, float> swatchWidths = new Dictionary<string, float>(StringComparer.Ordinal);

        // a hex colour's nearest entry, by the rule the renderer and conforming use; kept until the palette changes
        static readonly Dictionary<string, ColorDefinition?> nearest = new Dictionary<string, ColorDefinition?>(StringComparer.OrdinalIgnoreCase);
        static ColorPaletteDefinition? nearestOf; static int nearestCount;

        static ColorDefinition? Nearest(ColorPaletteDefinition palette, string hex)
        {
            if (nearestOf != palette || nearestCount != palette.Colors.Count) { nearest.Clear(); nearestOf = palette; nearestCount = palette.Colors.Count; }
            if (nearest.TryGetValue(hex, out var hit)) return hit;
            var m = PaletteMatch.Nearest(hex, ColorPipelinePalette.Entries(palette));
            ColorDefinition? d = null;
            if (m != null) palette.TryGetColor(ColorPipelinePalette.Guid(m.Value.entry.id), out d);
            return nearest[hex] = d;
        }

        static int FillDefaults()
        {
            ColorPaletteDefinition palette;
            try { palette = ColorPaletteDefinition.Instance; } catch (Exception) { return 0; }
            if (palette == null) return 0;
            var s = StoreyColorSettings.Load();
            if (s == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                s = ScriptableObject.CreateInstance<StoreyColorSettings>();
                AssetDatabase.CreateAsset(s, "Assets/Resources/" + StoreyColorSettings.ResourcesName + ".asset");
            }
            Undo.RecordObject(s, "Store the default colours");
            int n = s.FillUnsetDefaults(hex => Nearest(palette, hex)?.ID);
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            return n;
        }

        static SerializableGUID[]? Suggested(string field)
        {
            var s = StoreyColorSettings.Load(); if (s == null) return null;
            return field switch { "wall" => s.walls, "trim" => s.trims, "roof" => s.roofs, "interior" => s.interiors, "floor" => s.floors, _ => null };
        }
    }
}
