#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The site inspector's look (docs/EDITOR.md §6.10), after Unity's own editor guidelines: groups set apart by space
    /// and a header rather than boxes, boxes only round controls that act together (the building card), less-used
    /// settings behind foldouts that remember whether they're open, and the three modes as large buttons with an icon
    /// and a line saying what each is for.
    /// </summary>
    internal static class StoreyInspectorUI
    {
        static bool Dark => EditorGUIUtility.isProSkin;
        public static Color Accent => Dark ? new Color(0.36f, 0.62f, 0.95f) : new Color(0.16f, 0.43f, 0.82f);
        static Color Ink => Dark ? new Color(0.86f, 0.86f, 0.86f) : new Color(0.16f, 0.16f, 0.16f);
        static Color Rule => Dark ? new Color(1, 1, 1, 0.1f) : new Color(0, 0, 0, 0.12f);

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
                if (r.width > 150) ModeHint.Draw(new Rect(lr.x, lr.yMax - 1, lr.width, 26), modes[i].hint, false, false, false, false);
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
    }
}
