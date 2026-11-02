#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// A drop-down list that shows each choice before it is picked (docs/EDITOR.md §6.10). The choice under the pointer is
    /// handed to <c>show</c>, and the caller shows it: the Facade tab's Preset list puts the preset on the building in the
    /// Scene view. A click picks it. A click outside the list closes it with nothing picked, and <c>show</c> is handed null:
    /// things as they are. The current choice is ticked and is things as they are too, so the list opens on it, and
    /// pointing at it shows the building as it was. Unity's own drop-down (<c>EditorGUI.Popup</c>) says nothing until a
    /// choice is picked. Like the rest of the editor, it reads no keys (§2).
    /// </summary>
    internal sealed class StoreyPreviewMenu : PopupWindowContent
    {
        /// <summary>A row of the list: a choice, the name of the group of choices under it, or a line between groups.</summary>
        internal readonly struct Row
        {
            public readonly string label;
            /// <summary>What <c>show</c> and <c>pick</c> are handed for a choice.</summary>
            public readonly int value;
            public readonly bool choice;

            Row(string label, int value, bool choice) { this.label = label; this.value = value; this.choice = choice; }

            public static Row Choice(string label, int value) => new Row(label, value, true);
            /// <summary>The name of the group of choices under it (not a choice), with a hairline after it.</summary>
            public static Row Group(string label) => new Row(label, 0, false);
            public static Row Line => new Row("", 0, false);
        }

        const float ChoiceH = 20, GroupH = 20, LineH = 7, Pad = 4, Tick = 18, MaxH = 440, Bar = 14;

        readonly IReadOnlyList<Row> rows;
        readonly int current;
        readonly Action<int?> show;
        readonly Action<int> pick;
        readonly float minWidth, content;
        int lit = -1, pressed = -1;   // the row lit (the current one, then the last the pointer was on), and the row a click began on
        float width;
        Vector2 scroll;

        /// <param name="rows">The list, top to bottom.</param>
        /// <param name="current">The value of the choice that is things as they are (ticked), or one no row has.</param>
        /// <param name="show">Show a choice, or things as they are (null): told as the row lit changes, and null as the list closes.</param>
        /// <param name="pick">Take a choice other than the current one, clicked.</param>
        /// <param name="minWidth">The drop-down's width: the list is no narrower.</param>
        public StoreyPreviewMenu(IReadOnlyList<Row> rows, int current, Action<int?> show, Action<int> pick, float minWidth)
        {
            this.rows = rows; this.current = current; this.show = show; this.pick = pick; this.minWidth = minWidth;
            content = 2 * Pad;
            for (int i = 0; i < rows.Count; i++)
            {
                content += Height(rows[i]);
                if (rows[i].choice && rows[i].value == current) lit = i;
            }
        }

        static float Height(Row r) => r.choice ? ChoiceH : r.label.Length > 0 ? GroupH : LineH;

        public override Vector2 GetWindowSize()
        {
            if (width <= 0)
            {
                float w = 0;
                foreach (var r in rows) if (r.label.Length > 0) w = Math.Max(w, (r.choice ? Item : GroupStyle).CalcSize(new GUIContent(r.label)).x);
                width = Math.Max(minWidth, Tick + w + 16 + (content > MaxH ? Bar : 0));
            }
            return new Vector2(width, Math.Min(content, MaxH));
        }

        public override void OnOpen()
        {
            editorWindow.wantsMouseMove = true;   // the pointer lights a row as it moves over it, with no click
            Reveal(lit, Math.Min(content, MaxH));
        }

        // picked or not, things are shown as they are now: a choice picked is, by then
        public override void OnClose() => show(null);

        public override void OnGUI(Rect rect)
        {
            var ev = Event.current;
            var view = new Rect(0, 0, rect.width - (content > rect.height ? Bar : 0), content);
            scroll = GUI.BeginScrollView(rect, scroll, view);
            int at = RowAt(ev.mousePosition, view.width), take = -1;
            switch (ev.type)
            {
                case EventType.MouseMove:
                    if (at >= 0 && at != lit) Light(at);   // off the choices (a group's name, a line, outside) it stays lit
                    break;
                case EventType.MouseDown when ev.button == 0:
                    pressed = at;
                    if (at >= 0 && at != lit) Light(at);
                    ev.Use();
                    break;
                case EventType.MouseUp when ev.button == 0:
                    if (at >= 0 && at == pressed) take = at;
                    pressed = -1;
                    ev.Use();
                    break;
                case EventType.Repaint:
                    Draw(view.width);
                    break;
            }
            GUI.EndScrollView();
            if (take >= 0) Pick(take);
        }

        void Light(int i)
        {
            lit = i;
            int v = rows[i].value;
            show(v == current ? (int?)null : v);
            editorWindow.Repaint();
        }

        void Pick(int i)
        {
            int v = rows[i].value;
            if (v != current) pick(v);
            editorWindow.Close();
        }

        /// <summary>The choice's row at <paramref name="p"/> (in the list's own space), or -1.</summary>
        int RowAt(Vector2 p, float w)
        {
            if (p.x < 0 || p.x > w) return -1;
            float y = Pad;
            for (int i = 0; i < rows.Count; i++)
            {
                float h = Height(rows[i]);
                if (p.y >= y && p.y < y + h) return rows[i].choice ? i : -1;
                y += h;
            }
            return -1;
        }

        /// <summary>Scroll row <paramref name="i"/> into a view <paramref name="viewH"/> high.</summary>
        void Reveal(int i, float viewH)
        {
            if (i < 0) return;
            float y = Pad;
            for (int j = 0; j < i; j++) y += Height(rows[j]);
            float h = Height(rows[i]);
            if (y - Pad < scroll.y) scroll.y = Math.Max(0, y - Pad);
            else if (y + h + Pad > scroll.y + viewH) scroll.y = y + h + Pad - viewH;
        }

        void Draw(float w)
        {
            float y = Pad;
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i]; float h = Height(row);
                var r = new Rect(0, y, w, h); y += h;
                if (!row.choice)
                {
                    float tw = 0;
                    if (row.label.Length > 0)
                    {
                        var keep = GroupStyle.normal.textColor; GroupStyle.normal.textColor = StoreyInspectorUI.Muted;
                        tw = GroupStyle.CalcSize(new GUIContent(row.label)).x + 4;
                        GroupStyle.Draw(new Rect(r.x + Tick, r.y, tw, h), row.label, false, false, false, false);
                        GroupStyle.normal.textColor = keep;
                    }
                    float x0 = r.x + (tw > 0 ? Tick + tw : 4);
                    EditorGUI.DrawRect(new Rect(x0, r.y + (int)(h / 2), Math.Max(0, r.xMax - 4 - x0), 1), StoreyInspectorUI.Rule);
                    continue;
                }
                bool on = i == lit;
                if (on) EditorGUI.DrawRect(r, LitColour);
                var style = on ? ItemLit : Item;
                if (row.value == current) style.Draw(new Rect(r.x + 4, r.y, Tick - 4, h), "✓", false, false, false, false);
                style.Draw(new Rect(r.x + Tick, r.y, Math.Max(0, r.width - Tick - 4), h), row.label, false, false, false, false);
            }
        }

        // the editor's own selection blue, as its lists and menus light a row
        static Color LitColour => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f);

        static GUIStyle? item, itemLit, group;
        static GUIStyle Item => item ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
        static GUIStyle GroupStyle => group ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
        static GUIStyle ItemLit
        {
            get
            {
                if (itemLit == null) { itemLit = new GUIStyle(Item); itemLit.normal.textColor = Color.white; }
                return itemLit;
            }
        }
    }
}
