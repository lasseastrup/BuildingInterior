#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Triband.ColorPipeline.Editor;
using Triband.ColorPipeline.Runtime;
using Triband.Storey.ColorPipeline;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor.ColorPipeline
{
    /// <summary>
    /// <i>Map Colours to Palette…</i> on a <c>.storey</c> asset (docs/COLOURS.md §3.8). It lists every CSS literal
    /// the layout's styles hold, with the nearest Color Pipeline palette entry by the Model Remapper's measure.
    /// Each literal is mapped to that entry, to one picked in Color Pipeline's picker, or to a new palette entry,
    /// or is left as it is. Applying rewrites the file with palette ids and its palette block.
    /// </summary>
    public sealed class MapColorsWindow : EditorWindow
    {
        const string MenuPath = "Assets/Storey/Map Colours to Palette...";

        enum Choice { Palette, NewEntry, Keep }

        sealed class Row
        {
            public string hex = "";
            public int uses;
            public string? id;
            public string name = "";
            public Color paletteColor;
            public double similarity;
            public bool exact;
            public Choice choice;
            public string newName = "";
        }

        string? path;
        StoreyDocument? doc;
        List<Row> rows = new List<Row>();
        string? error;
        Vector2 scroll;

        [MenuItem(MenuPath, true)]
        static bool CanOpen() => SelectedPath() != null;

        [MenuItem(MenuPath, false, 2000)]
        static void Open()
        {
            var p = SelectedPath();
            if (p == null) return;
            var w = GetWindow<MapColorsWindow>(true, "Map Colours to Palette");
            w.minSize = new Vector2(560, 300);
            w.Load(p);
        }

        static string? SelectedPath()
        {
            var o = Selection.activeObject;
            if (o == null) return null;
            string p = AssetDatabase.GetAssetPath(o);
            return p.EndsWith(".storey", StringComparison.OrdinalIgnoreCase) ? p : null;
        }

        static Color Parse(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        void Load(string p)
        {
            path = p; error = null; rows = new List<Row>();
            try { doc = PrototypeJson.Read(File.ReadAllText(p)).Document; }
            catch (FormatException e) { doc = null; error = e.Message; return; }

            List<ColorDefinition> colors;
            try { colors = ColorPaletteDefinition.Instance.Colors; }
            catch (Exception e) { error = "Color Pipeline has no palette: " + e.Message; return; }

            var palette = colors.Select(c => (ColorPipelinePalette.IdOf(c.ID), c.Name, "#" + ColorUtility.ToHtmlStringRGB(c.Color))).ToList();
            foreach (var pr in ColorMapping.Propose(ColorMapping.Literals(doc), palette, (a, b) => Parse(a).GetColorSimilarity(Parse(b))))
                rows.Add(new Row
                {
                    hex = pr.hex, uses = pr.uses, id = pr.id, name = pr.name ?? "", paletteColor = pr.paletteHex != null ? Parse(pr.paletteHex) : Color.clear,
                    similarity = pr.similarity, exact = pr.exact, choice = pr.id != null ? Choice.Palette : Choice.NewEntry, newName = "Storey_" + pr.hex.Substring(1),
                });
        }

        void OnGUI()
        {
            if (path == null) { EditorGUILayout.HelpBox("Select a .storey asset and choose Assets > Storey > Map Colours to Palette.", MessageType.Info); return; }
            EditorGUILayout.LabelField(path, EditorStyles.boldLabel);
            if (error != null) { EditorGUILayout.HelpBox(error, MessageType.Error); return; }
            if (rows.Count == 0) { EditorGUILayout.HelpBox("Every colour of this layout is already a palette colour.", MessageType.Info); return; }

            EditorGUILayout.HelpBox("Exact matches are mapped already. Check the rest: map to the nearest entry, pick another, add the colour to the palette, or keep it as it is. Applying rewrites the .storey file.", MessageType.None);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var r in rows) DrawRow(r);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Reload", GUILayout.Width(80))) Load(path);
                if (GUILayout.Button("Apply", GUILayout.Width(80))) Apply();
            }
        }

        void DrawRow(Row r)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ColorField(GUIContent.none, Parse(r.hex), false, false, false, GUILayout.Width(40));
                EditorGUILayout.LabelField($"{r.hex}  ×{r.uses}", GUILayout.Width(110));
                r.choice = (Choice)EditorGUILayout.EnumPopup(r.choice, GUILayout.Width(80));
                switch (r.choice)
                {
                    case Choice.Palette:
                        if (r.id == null) { EditorGUILayout.LabelField("no entry picked"); break; }
                        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ColorField(GUIContent.none, r.paletteColor, false, false, false, GUILayout.Width(40));
                        EditorGUILayout.LabelField(r.exact ? $"{r.name}  (exact)" : $"{r.name}  {r.similarity * 100:0.#} %");
                        break;
                    case Choice.NewEntry:
                        r.newName = EditorGUILayout.TextField(r.newName);
                        break;
                    default:
                        EditorGUILayout.LabelField("stays a CSS colour (shown as the nearest palette colour)");
                        break;
                }
                if (GUILayout.Button("Pick...", GUILayout.Width(60))) Pick(r);
            }
        }

        void Pick(Row r)
        {
            var palette = ColorPaletteDefinition.Instance;
            int selected = r.id != null && palette.TryGetColor(ColorPipelinePalette.Guid(r.id), out _) ? palette.GetIndexOfColor(ColorPipelinePalette.Guid(r.id)) : 0;
            PaletteColorPickerWindow.Open(GUIUtility.GUIToScreenPoint(Event.current.mousePosition), selected, null, g =>
            {
                if (!ColorPaletteDefinition.Instance.TryGetColor(g, out var def)) return;
                r.id = ColorPipelinePalette.IdOf(g); r.name = def.Name; r.paletteColor = def.Color;
                r.similarity = Parse(r.hex).GetColorSimilarity(def.Color);
                r.exact = ColorUtility.ToHtmlStringRGB(def.Color) == r.hex.Substring(1);
                r.choice = Choice.Palette;
                Repaint();
            });
        }

        void Apply()
        {
            if (doc == null || path == null) return;
            var added = rows.Where(r => r.choice == Choice.NewEntry).ToList();
            if (added.Count > 0 && !EditorUtility.DisplayDialog("Add to the palette",
                    $"Add {added.Count} colour{(added.Count == 1 ? "" : "s")} to the end of ColorPalette.palette? Existing entries keep their indices.", "Add and apply", "Cancel"))
                return;

            if (added.Count > 0)
            {
                var palette = ColorPaletteDefinition.Instance;
                int display = palette.Colors.Count == 0 ? 0 : palette.Colors.Max(c => c.DisplayIndex) + 1;
                foreach (var r in added)
                {
                    var def = new ColorDefinition(string.IsNullOrWhiteSpace(r.newName) ? r.hex : r.newName, Parse(r.hex), display++);
                    palette.Colors.Add(def);   // appended: every existing index stays where it is
                    r.id = ColorPipelinePalette.IdOf(def.ID);
                }
                ColorPaletteDefinition.Save(palette);
                ColorMappingManager.Reset();
            }

            var map = rows.Where(r => r.choice != Choice.Keep && r.id != null).ToDictionary(r => r.hex, r => r.id!);
            using var bridge = new ColorPipelinePalette(null);
            int fields = ColorMapping.Apply(doc, map, bridge.Entry);
            File.WriteAllText(path, PrototypeJson.Write(doc));
            AssetDatabase.ImportAsset(path);
            Debug.Log($"Storey: {path}: {fields} colour field{(fields == 1 ? "" : "s")} now use palette colours ({map.Count} colour{(map.Count == 1 ? "" : "s")}, {added.Count} added to the palette).");
            Load(path);
        }
    }
}
