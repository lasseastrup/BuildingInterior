#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The site inspector's look (docs/EDITOR.md §6.10): the three modes as large buttons with an icon and a line saying what
    /// each is for; the Facade tab's settings in cards, one for each part of the building (style, windows, doors, walls,
    /// colours, roof, details), each with its icon and colour, a line of what is set when it is folded, and its own
    /// remembered fold; inside a card, sub-headers, features switched on with their settings indented under them, and only
    /// the settings that apply shown. The Shape and Interior tabs keep groups set apart by space and a header.
    /// </summary>
    internal static class StoreyInspectorUI
    {
        static bool Dark => EditorGUIUtility.isProSkin;
        public static Color Accent => Dark ? new Color(0.36f, 0.62f, 0.95f) : new Color(0.16f, 0.43f, 0.82f);
        internal static Color Ink => Dark ? new Color(0.86f, 0.86f, 0.86f) : new Color(0.16f, 0.16f, 0.16f);
        internal static Color Rule => Dark ? new Color(1, 1, 1, 0.1f) : new Color(0, 0, 0, 0.12f);
        internal static Color Muted => Dark ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f);

        /// <summary>Each card's colour: its icon and the stripe down its side, so a part of the building is found by its colour.</summary>
        public static class Tint
        {
            static Color Pick(Color dark, Color light) => Dark ? dark : light;
            public static Color Style => Pick(new Color(0.69f, 0.53f, 0.95f), new Color(0.47f, 0.30f, 0.78f));
            public static Color Windows => Pick(new Color(0.36f, 0.62f, 0.95f), new Color(0.16f, 0.43f, 0.82f));
            public static Color Doors => Pick(new Color(0.95f, 0.68f, 0.30f), new Color(0.75f, 0.45f, 0.05f));
            public static Color Walls => Pick(new Color(0.91f, 0.50f, 0.40f), new Color(0.72f, 0.30f, 0.20f));
            public static Color Colours => Pick(new Color(0.92f, 0.47f, 0.72f), new Color(0.72f, 0.22f, 0.50f));
            public static Color Roof => Pick(new Color(0.33f, 0.79f, 0.72f), new Color(0.10f, 0.55f, 0.48f));
            public static Color Details => Pick(new Color(0.58f, 0.82f, 0.42f), new Color(0.33f, 0.58f, 0.18f));
        }

        static GUIStyle? card, cardTitle, cardSummary, subHeader, feature;
        static GUIStyle CardBox => card ??= new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 8, 4, 8), margin = new RectOffset(2, 2, 4, 4) };
        static GUIStyle CardTitle => cardTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
        static GUIStyle CardSummary => cardSummary ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip };
        static GUIStyle SubHeaderStyle => subHeader ??= new GUIStyle(EditorStyles.miniBoldLabel);
        static GUIStyle FeatureStyle => feature ??= new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };

        /// <summary>
        /// A card round one part of the building's settings: a header with its icon, title and, folded or not, a short line
        /// of what is set (<paramref name="summary"/>), a click on the header folding it (remembered per editor); a stripe of
        /// its colour down its side. Draw its settings while <see cref="CardScope.Open"/>; dispose it after.
        /// </summary>
        public static CardScope Card(string key, string title, Action<Rect, Color> icon, Color tint, string? summary = null, bool open = true, string? tip = null) =>
            new CardScope(key, title, icon, tint, summary, open, tip);

        public readonly struct CardScope : IDisposable
        {
            public readonly bool Open;
            readonly Color tint;

            internal CardScope(string key, string title, Action<Rect, Color> icon, Color tint, string? summary, bool open, string? tip)
            {
                this.tint = tint;
                GUILayout.Space(2);
                EditorGUILayout.BeginVertical(CardBox);
                string k = "Storey.card." + key;
                bool was = SessionState.GetBool(k, open);
                var r = GUILayoutUtility.GetRect(0, 24, GUILayout.ExpandWidth(true));
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition)) { SessionState.SetBool(k, !was); was = !was; ev.Use(); }
                if (ev.type == EventType.Repaint)
                {
                    EditorStyles.foldout.Draw(new Rect(r.x - 2, r.y + 4, 14, 16), GUIContent.none, false, false, was, false);
                    icon(new Rect(r.x + 14, r.y + 3, 18, 18), tint);
                    float tw = CardTitle.CalcSize(new GUIContent(title)).x;
                    CardTitle.Draw(new Rect(r.x + 38, r.y + 2, tw + 4, 20), new GUIContent(title, tip), false, false, false, false);
                    if (summary != null)
                    {
                        var keep = CardSummary.normal.textColor; CardSummary.normal.textColor = Muted;
                        CardSummary.Draw(new Rect(r.x + 44 + tw, r.y + 3, Math.Max(0, r.width - tw - 48), 18), summary, false, false, false, false);
                        CardSummary.normal.textColor = keep;
                    }
                }
                if (tip != null) GUI.Label(r, new GUIContent("", tip));
                EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
                Open = was;
                if (Open) GUILayout.Space(2);
            }

            public void Dispose()
            {
                EditorGUILayout.EndVertical();
                if (Event.current.type == EventType.Repaint)
                {
                    var r = GUILayoutUtility.GetLastRect();
                    EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 2, 3, r.height - 4), tint);
                }
            }
        }

        /// <summary>A small header inside a card: a group of its settings, with a hairline after it.</summary>
        public static void SubHeader(string title, string? tip = null)
        {
            GUILayout.Space(6);
            var r = GUILayoutUtility.GetRect(new GUIContent(title, tip), SubHeaderStyle, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var keep = SubHeaderStyle.normal.textColor; SubHeaderStyle.normal.textColor = Muted;
                float tw = SubHeaderStyle.CalcSize(new GUIContent(title)).x;
                SubHeaderStyle.Draw(r, new GUIContent(title), false, false, false, false);
                SubHeaderStyle.normal.textColor = keep;
                EditorGUI.DrawRect(new Rect(r.x + tw + 6, r.center.y, Math.Max(0, r.width - tw - 6), 1), Rule);
            }
            if (tip != null) GUI.Label(r, new GUIContent("", tip));
        }

        /// <summary>A part of the building switched on or off (a cornice, bands, a recess): its name in bold by a check box. Its settings go indented under it while it is on.</summary>
        public static bool Feature(string label, string tip, bool on) => EditorGUILayout.ToggleLeft(new GUIContent(label, tip), on, FeatureStyle);

        /// <summary>A choice of a few, side by side, beside its label as a field's is.</summary>
        public static int Segmented(GUIContent label, int current, GUIContent[] options)
        {
            var r = EditorGUILayout.GetControlRect();
            r = EditorGUI.PrefixLabel(r, label);
            return GUI.Toolbar(r, current, options, EditorStyles.miniButton);
        }

        /// <summary>A quiet line saying what the settings above do, or why some aren't there.</summary>
        public static void Note(string text) => EditorGUILayout.LabelField(text, Caption);

        static readonly Dictionary<string, Dictionary<string, string>> shortNames = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>
        /// A project's asset names as a person would write them: words apart, not run together with underscores or capitals
        /// (Visual_BuildingDetail_EdgeBricks: Visual Building Detail Edge Bricks), and the words a family of them all start
        /// with left out (two or more named alike: Edge Bricks), so a button shows what tells them apart. Their own names go
        /// in their tooltips. Kept for each list of names (the details', the windows', the doors'), while it stays the same.
        /// </summary>
        public static IReadOnlyDictionary<string, string> ShortNames(IReadOnlyList<string> names)
        {
            string key = string.Join("\n", names);
            if (shortNames.TryGetValue(key, out var kept)) return kept;
            if (shortNames.Count > 16) shortNames.Clear();
            var words = names.Distinct().ToDictionary(n => n, Words);
            var o = new Dictionary<string, string>();
            foreach (var family in words.GroupBy(kv => kv.Value.Length > 0 ? kv.Value[0] : ""))
            {
                var list = family.ToList();
                int common = 0;
                if (list.Count > 1)
                    while (list.All(kv => kv.Value.Length > common + 1 && kv.Value[common] == list[0].Value[common])) common++;
                foreach (var kv in list) o[kv.Key] = kv.Value.Length == 0 ? kv.Key : string.Join(" ", kv.Value.Skip(common));
            }
            shortNames[key] = o;
            return o;
        }

        static readonly Regex Asides = new Regex(@"\s*\([^)]*\)");
        static readonly Regex WordsIn = new Regex(@"\([^)]*\)|\p{Lu}+(?=\p{Lu}\p{Ll})|\p{Lu}?\p{Ll}+|\p{Lu}+|\p{Nd}+|[^\p{L}\p{Nd}\s_.()-]+");
        /// <summary>A name's words: run-together capitals and digits apart, underscores and dots dropped, an aside in brackets one word.</summary>
        internal static string[] Words(string name) => WordsIn.Matches(name).Cast<Match>().Select(m => m.Value).ToArray();

        /// <summary>
        /// <paramref name="text"/> where it is wider than <paramref name="width"/> in <paramref name="style"/>: without its
        /// asides in brackets first, then cut short with an ellipsis.
        /// </summary>
        public static string Fit(string text, float width, GUIStyle style)
        {
            if (style.CalcSize(new GUIContent(text)).x <= width) return text;
            string bare = Asides.Replace(text, "").Trim();
            if (bare.Length > 0 && bare != text) return Fit(bare, width, style);
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (style.CalcSize(new GUIContent(text.Substring(0, mid).TrimEnd() + "…")).x <= width) lo = mid; else hi = mid - 1;
            }
            return text.Substring(0, lo).TrimEnd() + "…";
        }

        static GUIStyle? header, modeLabel, modeHint, caption, fold;
        static GUIStyle FoldStyle => fold ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
        static GUIStyle Header => header ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        static GUIStyle ModeLabel => modeLabel ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
        static GUIStyle ModeHint => modeHint ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperLeft, wordWrap = true };
        /// <summary>A quiet line under a header or a group: what it does.</summary>
        public static GUIStyle Caption => caption ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };

        /// <summary>A group's header: space above, the title, a hairline under it.</summary>
        public static void Section(string title, string? tip = null)
        {
            GUILayout.Space(10);
            EditorGUILayout.LabelField(new GUIContent(title, tip), Header);
            var r = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(r, Rule);
            GUILayout.Space(3);
        }

        /// <summary>A foldout group for less-used settings; it remembers whether it's open (per editor, not per layout).</summary>
        public static bool Fold(string key, string title, bool open = false, string? tip = null)
        {
            GUILayout.Space(6);
            string k = "Storey.fold." + key;
            bool was = SessionState.GetBool(k, open);
            bool now = EditorGUILayout.Foldout(was, new GUIContent(title, tip), true, FoldStyle);
            if (now != was) SessionState.SetBool(k, now);
            return now;
        }

        /// <summary>The three modes, large, each with its icon and a line about it. Returns the one picked.</summary>
        public static int ModeBar(int current, (string label, string hint, Action<Rect, Color> icon)[] modes)
        {
            GUILayout.Space(8);
            var row = GUILayoutUtility.GetRect(0, 54, GUILayout.ExpandWidth(true));
            int picked = current; float w = row.width / modes.Length;
            // each mode's line about it, where every one fits under its label (the picked one's is under the bar anyway)
            bool hints = true;
            foreach (var m in modes) hints &= w > 150 && ModeHint.CalcHeight(new GUIContent(m.hint), w - 54) <= 27;
            for (int i = 0; i < modes.Length; i++)
            {
                var r = new Rect(row.x + i * w + (i > 0 ? 2 : 0), row.y, w - (i > 0 ? 2 : 0), row.height);
                bool on = i == current;
                if (GUI.Button(r, new GUIContent("", modes[i].hint), GUI.skin.button) && !on) picked = i;
                if (Event.current.type != EventType.Repaint) continue;
                if (on)
                {
                    EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(Accent.r, Accent.g, Accent.b, 0.22f));
                    EditorGUI.DrawRect(new Rect(r.x + 1, r.yMax - 4, r.width - 2, 3), Accent);
                }
                var col = on ? Accent : Ink;
                var ic = new Rect(r.x + 8, r.y + (r.height - 30) / 2 - 1, 30, 30);
                modes[i].icon(ic, col);
                var lr = new Rect(ic.xMax + 6, r.y + 7, r.width - ic.width - 18, 18);
                var keep = ModeLabel.normal.textColor; ModeLabel.normal.textColor = col;
                ModeLabel.Draw(lr, modes[i].label, false, false, false, false);
                ModeLabel.normal.textColor = keep;
                if (hints) ModeHint.Draw(new Rect(lr.x, lr.yMax - 1, lr.width, 26), modes[i].hint, false, false, false, false);
            }
            return picked;
        }

        static GUIStyle? toolLabel;
        static GUIStyle ToolLabel => toolLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };

        /// <summary>A row of tools, each an icon over its name (the hint is the tooltip). Returns the one picked.</summary>
        public static int ToolBar(int current, (string label, string hint, Action<Rect, Color> icon)[] tools)
        {
            var row = GUILayoutUtility.GetRect(0, 44, GUILayout.ExpandWidth(true));
            int picked = current; float w = row.width / tools.Length;
            for (int i = 0; i < tools.Length; i++)
            {
                var r = new Rect(row.x + i * w, row.y, w - (i < tools.Length - 1 ? 2 : 0), row.height);
                bool on = i == current;
                if (GUI.Button(r, new GUIContent("", tools[i].hint), GUI.skin.button) && !on) picked = i;
                if (Event.current.type != EventType.Repaint) continue;
                if (on)
                {
                    EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(Accent.r, Accent.g, Accent.b, 0.22f));
                    EditorGUI.DrawRect(new Rect(r.x + 1, r.yMax - 3, r.width - 2, 2), Accent);
                }
                var col = on ? Accent : Ink;
                tools[i].icon(new Rect(r.x + r.width / 2 - 10, r.y + 4, 20, 20), col);
                var keep = ToolLabel.normal.textColor; ToolLabel.normal.textColor = col;
                ToolLabel.Draw(new Rect(r.x, r.y + 25, r.width, 16), tools[i].label, false, false, false, false);
                ToolLabel.normal.textColor = keep;
            }
            return picked;
        }

        /// <summary>The selected mode's line, under the bar, when the buttons are too narrow to hold it.</summary>
        public static void ModeHintLine(string hint) => EditorGUILayout.LabelField(hint, Caption);

        // ---- icons, drawn so they follow the skin and need no image files ----

        static Vector3 P(Rect r, float x, float y) => new Vector3(r.x + x * r.width, r.y + y * r.height, 0);

        static void Lines(Rect r, Color c, float width, params (float x, float y)[] pts)
        {
            var v = new Vector3[pts.Length];
            for (int i = 0; i < pts.Length; i++) v[i] = P(r, pts[i].x, pts[i].y);
            Handles.color = c; Handles.DrawAAPolyLine(width, v);
        }

        static void Dot(Rect r, Color c, float x, float y, float s = 0.12f) =>
            EditorGUI.DrawRect(new Rect(r.x + (x - s / 2) * r.width, r.y + (y - s / 2) * r.height, s * r.width, s * r.height), c);

        /// <summary>Shape: an L-shaped outline with its corners.</summary>
        public static void ShapeIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.12f, 0.15f), (0.6f, 0.15f), (0.6f, 0.5f), (0.88f, 0.5f), (0.88f, 0.85f), (0.12f, 0.85f), (0.12f, 0.15f));
            foreach (var (x, y) in new[] { (0.12f, 0.15f), (0.6f, 0.15f), (0.6f, 0.5f), (0.88f, 0.5f), (0.88f, 0.85f), (0.12f, 0.85f) }) Dot(r, c, x, y);
        }

        /// <summary>Facade: a building's front, its windows and its door.</summary>
        public static void FacadeIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.18f, 0.92f), (0.18f, 0.1f), (0.82f, 0.1f), (0.82f, 0.92f));
            Lines(r, c, 2f, (0.08f, 0.92f), (0.92f, 0.92f));
            foreach (var y in new[] { 0.22f, 0.45f })
                foreach (var x in new[] { 0.3f, 0.56f }) EditorGUI.DrawRect(new Rect(r.x + x * r.width, r.y + y * r.height, 0.14f * r.width, 0.14f * r.height), c);
            EditorGUI.DrawRect(new Rect(r.x + 0.42f * r.width, r.y + 0.68f * r.height, 0.16f * r.width, 0.24f * r.height), c);
        }

        // ---- the Facade tab's cards ----

        static void Box(Rect r, Color c, float x0, float y0, float x1, float y1) =>
            EditorGUI.DrawRect(new Rect(r.x + x0 * r.width, r.y + y0 * r.height, (x1 - x0) * r.width, (y1 - y0) * r.height), c);

        /// <summary>Style: swatches fanned out.</summary>
        public static void StyleIcon(Rect r, Color c)
        {
            Box(r, new Color(c.r, c.g, c.b, 0.45f), 0.08f, 0.3f, 0.42f, 0.9f);
            Box(r, new Color(c.r, c.g, c.b, 0.7f), 0.33f, 0.2f, 0.67f, 0.8f);
            Box(r, c, 0.58f, 0.1f, 0.92f, 0.7f);
        }

        /// <summary>Windows: a window with its glazing bars.</summary>
        public static void WindowFrontIcon(Rect r, Color c)
        {
            Lines(r, c, 2f, (0.15f, 0.08f), (0.85f, 0.08f), (0.85f, 0.92f), (0.15f, 0.92f), (0.15f, 0.08f));
            Lines(r, c, 1.5f, (0.5f, 0.08f), (0.5f, 0.92f));
            Lines(r, c, 1.5f, (0.15f, 0.5f), (0.85f, 0.5f));
        }

        /// <summary>Doors: a door with its panel and knob.</summary>
        public static void DoorFrontIcon(Rect r, Color c)
        {
            Lines(r, c, 2f, (0.22f, 0.95f), (0.22f, 0.05f), (0.78f, 0.05f), (0.78f, 0.95f));
            Lines(r, c, 1.2f, (0.34f, 0.18f), (0.66f, 0.18f), (0.66f, 0.5f), (0.34f, 0.5f), (0.34f, 0.18f));
            Dot(r, c, 0.66f, 0.62f, 0.14f);
        }

        /// <summary>Walls: courses of brick.</summary>
        public static void BricksIcon(Rect r, Color c)
        {
            for (int row = 0; row < 3; row++)
            {
                float y0 = 0.1f + row * 0.29f, y1 = y0 + 0.22f, off = row % 2 == 0 ? 0 : 0.2f;
                for (float x = -0.2f + off; x < 0.95f; x += 0.42f)
                    Box(r, c, Math.Max(0.05f, x), y0, Math.Min(0.95f, x + 0.36f), y1);
            }
        }

        /// <summary>Colours: four swatches.</summary>
        public static void PaletteIcon(Rect r, Color c)
        {
            Box(r, c, 0.1f, 0.1f, 0.45f, 0.45f);
            Box(r, new Color(c.r, c.g, c.b, 0.7f), 0.55f, 0.1f, 0.9f, 0.45f);
            Box(r, new Color(c.r, c.g, c.b, 0.5f), 0.1f, 0.55f, 0.45f, 0.9f);
            Box(r, new Color(c.r, c.g, c.b, 0.3f), 0.55f, 0.55f, 0.9f, 0.9f);
        }

        /// <summary>Roof: a roof over a wall.</summary>
        public static void RoofIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.05f, 0.55f), (0.5f, 0.12f), (0.95f, 0.55f));
            Lines(r, c, 2f, (0.2f, 0.45f), (0.2f, 0.92f), (0.8f, 0.92f), (0.8f, 0.45f));
        }

        /// <summary>Details: a sparkle.</summary>
        public static void DetailsIcon(Rect r, Color c)
        {
            Lines(r, c, 2f, (0.5f, 0.05f), (0.5f, 0.95f)); Lines(r, c, 2f, (0.05f, 0.5f), (0.95f, 0.5f));
            Lines(r, c, 1.5f, (0.22f, 0.22f), (0.78f, 0.78f)); Lines(r, c, 1.5f, (0.78f, 0.22f), (0.22f, 0.78f));
            Dot(r, c, 0.5f, 0.5f, 0.26f);
        }

        /// <summary>Placement: an arrow up from the ground.</summary>
        public static void PlacementIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.05f, 0.9f), (0.95f, 0.9f));
            Lines(r, c, 2f, (0.5f, 0.75f), (0.5f, 0.12f));
            Lines(r, c, 2f, (0.28f, 0.36f), (0.5f, 0.12f), (0.72f, 0.36f));
        }

        /// <summary>Corners: an outline with a corner cut off.</summary>
        public static void CornerIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.12f, 0.9f), (0.12f, 0.12f), (0.55f, 0.12f), (0.88f, 0.45f), (0.88f, 0.9f));
            Dot(r, c, 0.55f, 0.12f); Dot(r, c, 0.88f, 0.45f);
        }

        /// <summary>Courtyards and atria: an outline with an open square in it.</summary>
        public static void CourtyardIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.1f, 0.1f), (0.9f, 0.1f), (0.9f, 0.9f), (0.1f, 0.9f), (0.1f, 0.1f));
            Box(r, new Color(c.r, c.g, c.b, 0.6f), 0.36f, 0.36f, 0.64f, 0.64f);
        }

        /// <summary>Storey heights: storeys stacked, and an arrow up their height.</summary>
        public static void HeightsIcon(Rect r, Color c)
        {
            foreach (var y in new[] { 0.12f, 0.4f, 0.66f, 0.92f }) Lines(r, c, 2f, (0.06f, y), (0.6f, y));
            Lines(r, c, 1.5f, (0.82f, 0.14f), (0.82f, 0.9f));
            Lines(r, c, 1.5f, (0.7f, 0.28f), (0.82f, 0.12f), (0.94f, 0.28f));
            Lines(r, c, 1.5f, (0.7f, 0.76f), (0.82f, 0.92f), (0.94f, 0.76f));
        }

        /// <summary>Floor: a storey's plan with its rooms.</summary>
        public static void FloorPlanIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.1f, 0.1f), (0.9f, 0.1f), (0.9f, 0.9f), (0.1f, 0.9f), (0.1f, 0.1f));
            Lines(r, c, 1.5f, (0.5f, 0.1f), (0.5f, 0.55f));
            Lines(r, c, 1.5f, (0.1f, 0.55f), (0.34f, 0.55f));
            Lines(r, c, 1.5f, (0.62f, 0.55f), (0.9f, 0.55f));
        }

        /// <summary>Bridges: two buildings and the span between them.</summary>
        public static void BridgeIcon(Rect r, Color c)
        {
            Box(r, c, 0.05f, 0.25f, 0.3f, 0.95f);
            Box(r, c, 0.7f, 0.1f, 0.95f, 0.95f);
            Box(r, new Color(c.r, c.g, c.b, 0.7f), 0.3f, 0.42f, 0.7f, 0.58f);
        }

        // ---- the Interior tab's tools ----

        /// <summary>Select: a pointer.</summary>
        public static void SelectIcon(Rect r, Color c)
        {
            Lines(r, c, 2f, (0.3f, 0.1f), (0.3f, 0.85f), (0.48f, 0.66f), (0.62f, 0.95f), (0.72f, 0.9f), (0.58f, 0.62f), (0.82f, 0.6f), (0.3f, 0.1f));
        }

        /// <summary>Wall: a thick wall between two points.</summary>
        public static void WallIcon(Rect r, Color c)
        {
            Lines(r, c, 4f, (0.15f, 0.75f), (0.85f, 0.25f));
            Dot(r, c, 0.15f, 0.75f, 0.2f); Dot(r, c, 0.85f, 0.25f, 0.2f);
        }

        /// <summary>Door: a wall with a gap and the door swinging open in it.</summary>
        public static void DoorIcon(Rect r, Color c)
        {
            Lines(r, c, 3f, (0.05f, 0.8f), (0.3f, 0.8f)); Lines(r, c, 3f, (0.75f, 0.8f), (0.95f, 0.8f));
            Lines(r, c, 2f, (0.3f, 0.8f), (0.3f, 0.35f));
            var arc = new (float, float)[9];
            for (int i = 0; i < 9; i++) { double a = Math.PI / 2 * i / 8; arc[i] = (0.3f + 0.45f * (float)Math.Sin(a), 0.8f - 0.45f * (float)Math.Cos(a)); }
            Lines(r, c, 1.2f, arc);
        }

        /// <summary>Erase: a cross.</summary>
        public static void EraseIcon(Rect r, Color c)
        {
            Lines(r, c, 3f, (0.2f, 0.2f), (0.8f, 0.8f)); Lines(r, c, 3f, (0.8f, 0.2f), (0.2f, 0.8f));
        }

        /// <summary>Stairs: steps going up.</summary>
        public static void StairsIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.08f, 0.9f), (0.08f, 0.72f), (0.3f, 0.72f), (0.3f, 0.52f), (0.52f, 0.52f), (0.52f, 0.32f), (0.74f, 0.32f), (0.74f, 0.12f), (0.95f, 0.12f));
        }

        /// <summary>Lift: a shaft with up and down arrows.</summary>
        public static void LiftIcon(Rect r, Color c)
        {
            Lines(r, c, 2f, (0.18f, 0.08f), (0.82f, 0.08f), (0.82f, 0.92f), (0.18f, 0.92f), (0.18f, 0.08f));
            Lines(r, c, 2f, (0.35f, 0.4f), (0.5f, 0.22f), (0.65f, 0.4f));
            Lines(r, c, 2f, (0.35f, 0.6f), (0.5f, 0.78f), (0.65f, 0.6f));
        }

        /// <summary>Interior: a floor plan, rooms and a doorway.</summary>
        public static void InteriorIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.1f, 0.12f), (0.9f, 0.12f), (0.9f, 0.88f), (0.1f, 0.88f), (0.1f, 0.12f));
            Lines(r, c, 2f, (0.5f, 0.12f), (0.5f, 0.42f));
            Lines(r, c, 2f, (0.5f, 0.6f), (0.5f, 0.88f));
            Lines(r, c, 2f, (0.5f, 0.5f), (0.9f, 0.5f));
            // stairs in the corner
            for (int i = 0; i < 3; i++) Lines(r, c, 1.5f, (0.16f, 0.62f + i * 0.08f), (0.38f, 0.62f + i * 0.08f));
        }

        // ---- the Storey Library's ----

        /// <summary>Grid: four tiles.</summary>
        public static void GridIcon(Rect r, Color c)
        {
            Box(r, c, 0.1f, 0.1f, 0.44f, 0.44f); Box(r, c, 0.56f, 0.1f, 0.9f, 0.44f);
            Box(r, c, 0.1f, 0.56f, 0.44f, 0.9f); Box(r, c, 0.56f, 0.56f, 0.9f, 0.9f);
        }

        /// <summary>List: rows, each a picture and a line.</summary>
        public static void ListIcon(Rect r, Color c)
        {
            for (int i = 0; i < 3; i++)
            {
                float y = 0.1f + i * 0.3f;
                Box(r, c, 0.08f, y, 0.3f, y + 0.2f);
                Box(r, new Color(c.r, c.g, c.b, c.a * 0.8f), 0.4f, y + 0.07f, 0.92f, y + 0.13f);
            }
        }

        /// <summary>Turntable: an arrow going round.</summary>
        public static void TurnIcon(Rect r, Color c)
        {
            var arc = new (float, float)[13];
            for (int i = 0; i < 13; i++) { double a = Math.PI * (0.15 + 1.55 * i / 12); arc[i] = (0.5f + 0.36f * (float)Math.Cos(a), 0.5f - 0.36f * (float)Math.Sin(a)); }
            Lines(r, c, 2f, arc);
            var (ex, ey) = arc[12];
            Lines(r, c, 2f, (ex - 0.2f, ey - 0.02f), (ex, ey), (ex + 0.02f, ey - 0.2f));
        }

        /// <summary>Frame: a frame's four corners round a dot.</summary>
        public static void FrameIcon(Rect r, Color c)
        {
            foreach (var (x, y, dx, dy) in new[] { (0.1f, 0.1f, 1f, 1f), (0.9f, 0.1f, -1f, 1f), (0.1f, 0.9f, 1f, -1f), (0.9f, 0.9f, -1f, -1f) })
                Lines(r, c, 2f, (x, y + dy * 0.26f), (x, y), (x + dx * 0.26f, y));
            Dot(r, c, 0.5f, 0.5f, 0.2f);
        }

        /// <summary>Lock: shown, not changed (one of Storey's own).</summary>
        public static void LockIcon(Rect r, Color c)
        {
            Box(r, c, 0.18f, 0.46f, 0.82f, 0.94f);
            var arc = new (float, float)[9];
            for (int i = 0; i < 9; i++) { double a = Math.PI * i / 8; arc[i] = (0.5f + 0.22f * (float)Math.Cos(a), 0.46f - 0.3f * (float)Math.Sin(a)); }
            Lines(r, c, 2f, arc);
        }

        /// <summary>Warning: a triangle with an exclamation mark.</summary>
        public static void WarningIcon(Rect r, Color c)
        {
            Lines(r, c, 1.8f, (0.5f, 0.08f), (0.94f, 0.9f), (0.06f, 0.9f), (0.5f, 0.08f));
            Lines(r, c, 2.2f, (0.5f, 0.36f), (0.5f, 0.62f));
            Dot(r, c, 0.5f, 0.76f, 0.11f);
        }

        /// <summary>More: three dots.</summary>
        public static void MoreIcon(Rect r, Color c) { Dot(r, c, 0.2f, 0.5f, 0.16f); Dot(r, c, 0.5f, 0.5f, 0.16f); Dot(r, c, 0.8f, 0.5f, 0.16f); }

        /// <summary>Plus: something new.</summary>
        public static void PlusIcon(Rect r, Color c) { Lines(r, c, 2.2f, (0.5f, 0.14f), (0.5f, 0.86f)); Lines(r, c, 2.2f, (0.14f, 0.5f), (0.86f, 0.5f)); }

        /// <summary>Rename: a pencil.</summary>
        public static void PencilIcon(Rect r, Color c)
        {
            Lines(r, c, 2.5f, (0.22f, 0.78f), (0.74f, 0.26f));
            Lines(r, c, 1.5f, (0.14f, 0.86f), (0.22f, 0.78f));
            Lines(r, c, 1.5f, (0.66f, 0.18f), (0.82f, 0.34f));
        }

        /// <summary>A layout: a plan of buildings on a site.</summary>
        public static void LayoutIcon(Rect r, Color c)
        {
            Lines(r, c, 1.5f, (0.06f, 0.06f), (0.94f, 0.06f), (0.94f, 0.94f), (0.06f, 0.94f), (0.06f, 0.06f));
            Box(r, c, 0.18f, 0.18f, 0.46f, 0.5f); Box(r, c, 0.56f, 0.18f, 0.82f, 0.38f); Box(r, c, 0.56f, 0.5f, 0.82f, 0.82f);
        }

        /// <summary>
        /// A footprint's outline (a building template's, a placeholder's shape), fitted into <paramref name="r"/> and drawn
        /// from above, +Z up the rect.
        /// </summary>
        public static void Outline(Rect r, Color c, IReadOnlyList<Vec2> footprint, float width = 1.5f)
        {
            if (footprint.Count < 3) return;
            double x0 = footprint.Min(p => p.x), x1 = footprint.Max(p => p.x), z0 = footprint.Min(p => p.z), z1 = footprint.Max(p => p.z);
            double s = Math.Max(1e-6, Math.Max(x1 - x0, z1 - z0));
            float ox = (float)(1 - (x1 - x0) / s) / 2, oz = (float)(1 - (z1 - z0) / s) / 2;
            var pts = new (float, float)[footprint.Count + 1];
            for (int i = 0; i <= footprint.Count; i++)
            {
                var p = footprint[i % footprint.Count];
                pts[i] = (0.08f + 0.84f * (ox + (float)((p.x - x0) / s)), 0.92f - 0.84f * (oz + (float)((p.z - z0) / s)));
            }
            Lines(r, c, width, pts);
        }
    }
}
