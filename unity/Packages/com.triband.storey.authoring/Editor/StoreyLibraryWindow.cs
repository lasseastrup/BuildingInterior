#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The Storey Library (docs/EDITOR.md §6.13), <i>Tools ▸ Storey ▸ Library…</i>: every facade style, artist's window and
    /// door, facade detail and building template the project and the package have, in one window, so nobody goes looking for
    /// them in the Project window.
    ///
    /// The side bar picks the kind, each in the colour and with the icon the site inspector's cards give it, and says which
    /// building in the scene the Library acts on. The middle lists the kind's items as cards with a picture of each on a
    /// building, by family, found by name, by family or by what they are (a style by its colours' names). The right shows
    /// the item picked on a building in a live preview, orbited with the mouse, at any LOD, on any of the five footprints and
    /// any number of storeys; a window, door or detail on whichever style; then its name, what it is used by, and its
    /// settings: a style in the Facade tab's own cards, a window, door or detail in its own inspector. New makes styles,
    /// windows, doors, details and templates; prefabs dropped on the window become windows, doors or details. What Storey
    /// ships is shown, not changed: duplicate it to make your own. No key does anything here (docs/EDITOR.md §2).
    /// </summary>
    internal sealed class StoreyLibraryWindow : EditorWindow, IHasCustomMenu
    {
        [MenuItem(StoreyEditorInfo.MenuRoot + "Library...", priority = 1)]
        static void OpenFromMenu() => Open();

        /// <summary>The Library, with <paramref name="item"/> picked (a Facade Style, Storey Opening, Facade Detail or Building Template asset).</summary>
        public static StoreyLibraryWindow Open(UnityEngine.Object? item = null)
        {
            var w = GetWindow<StoreyLibraryWindow>(false, "Storey Library", true);
            w.minSize = new Vector2(840, 500);
            if (!w.placed && !w.docked)
            {
                // the first time, roomy: three panes side by side, in the middle of the editor
                var main = EditorGUIUtility.GetMainWindowPosition();
                float ww = Mathf.Min(1380, main.width * 0.85f), wh = Mathf.Min(880, main.height * 0.85f);
                w.position = new Rect(main.x + (main.width - ww) / 2, main.y + (main.height - wh) / 2, ww, wh);
            }
            w.placed = true;
            if (item != null) w.Pick(item);
            w.Show();
            return w;
        }

        /// <summary>A double click on one of the Library's assets in the Project window opens it here.</summary>
        [OnOpenAsset(0)]
        static bool OpenAsset(EntityId id, int line)
        {
            var o = EditorUtility.EntityIdToObject(id);
            if (!(o is FacadeStylePreset || o is StoreyOpening || o is StoreyDetail || o is StoreyBuildingTemplate)) return false;
            Open(o);
            return true;
        }

        /// <summary>An item's own inspector is being drawn inside the Library: it leaves out its Open in Storey Library button.</summary>
        internal static bool Embedded;

        // what is shown, kept through domain reloads; how it is laid out, kept per user
        [SerializeField] LibraryKind kind;
        [SerializeField] List<string> picked = new List<string> { "", "", "", "", "" };
        [SerializeField] string search = "";
        [SerializeField] int source;
        [SerializeField] Vector2 browseScroll, panelScroll;
        [SerializeField] string renaming = "";
        [SerializeField] string renameText = "";
        [SerializeField] bool placed;

        bool AsList { get => EditorPrefs.GetBool(Pref + "List", false); set => EditorPrefs.SetBool(Pref + "List", value); }
        float CardSize { get => EditorPrefs.GetFloat(Pref + "Card", 124); set => EditorPrefs.SetFloat(Pref + "Card", value); }
        float PanelWidth { get => EditorPrefs.GetFloat(Pref + "Panel", 430); set => EditorPrefs.SetFloat(Pref + "Panel", value); }
        float PreviewHeight { get => EditorPrefs.GetFloat(Pref + "PreviewHeight", 300); set => EditorPrefs.SetFloat(Pref + "PreviewHeight", value); }
        string Shape { get => EditorPrefs.GetString(Pref + "Shape", "L"); set => EditorPrefs.SetString(Pref + "Shape", value); }
        int Storeys { get => EditorPrefs.GetInt(Pref + "Storeys", 4); set => EditorPrefs.SetInt(Pref + "Storeys", value); }
        int Lod { get => EditorPrefs.GetInt(Pref + "Lod", 0); set => EditorPrefs.SetInt(Pref + "Lod", value); }
        bool TemplateOwnStyle { get => EditorPrefs.GetBool(Pref + "TemplateOwnStyle", true); set => EditorPrefs.SetBool(Pref + "TemplateOwnStyle", value); }
        /// <summary>The style windows, doors and details are shown on, in the preview and on their cards: a preset's key.</summary>
        internal static string ShowOnKey { get => showOnKey ??= EditorPrefs.GetString(Pref + "ShowOn", ""); set { showOnKey = value; EditorPrefs.SetString(Pref + "ShowOn", value); } }
        static string? showOnKey;   // read once: every window's, door's and detail's card asks at every repaint
        const string Pref = "Storey.Library.";

        StoreyPreview? preview;
        StoreyThumbnails? thumbs;
        UnityEditor.Editor? inspector;
        PresetStyle? cards;
        SearchField? searchField;
        string failedKey = "";
        float column = 300;   // the item panel's width inside its margins, this repaint
        double retryAt;
        string? message; double messageUntil;
        string? scrollTo;
        LibraryItem? pressed;
        bool dropping; int dropCount;
        double lastSlow;

        static readonly string[] Sources = { "Everything", "The project's", "Storey's own" };
        static readonly GUIContent[] LodLabels =
        {
            new GUIContent("LOD0", "Full detail: what the player sees close by"),
            new GUIContent("LOD1", "The shell, a little further off: plain panes and lean walls"),
            new GUIContent("LOD2", "The massing, far off: a box with painted windows"),
        };

        void OnEnable()
        {
            titleContent = new GUIContent("Storey Library", "Every facade style, window, door, facade detail and building template");
            wantsMouseMove = true;
            Undo.undoRedoPerformed += Refresh;
            EditorApplication.projectChanged += Refresh;
            EditorApplication.update += Tick;
            Selection.selectionChanged += Repaint;
            while (picked.Count < StoreyLibrary.Kinds.Length) picked.Add("");
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= Refresh;
            EditorApplication.projectChanged -= Refresh;
            EditorApplication.update -= Tick;
            Selection.selectionChanged -= Repaint;
            preview?.Dispose(); preview = null;
            thumbs?.Dispose(); thumbs = null;
            if (inspector != null) DestroyImmediate(inspector);
            inspector = null; cards = null;
        }

        void OnHierarchyChange() => Repaint();

        void Refresh() { preview?.Invalidate(); Repaint(); }

        void Tick()
        {
            // the cards' pictures, a few at a time; the turntable; and now and then what the scene's layouts use
            if (thumbs != null && thumbs.Waiting > 0 && thumbs.Work(18)) Repaint();
            if (preview != null && preview.Turntable) Repaint();
            if (EditorApplication.timeSinceStartup - lastSlow > 2) { lastSlow = EditorApplication.timeSinceStartup; Repaint(); }
            if (message != null && EditorApplication.timeSinceStartup > messageUntil) { message = null; Repaint(); }
        }

        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Draw every picture again"), false, () => { thumbs?.Forget(); preview?.Invalidate(); Repaint(); });
            menu.AddItem(new GUIContent("Look for new assets"), false, () => { StoreyLibrary.Changed(); Refresh(); });
        }

        // ---- what is picked ----

        LibraryItem? Picked()
        {
            var i = StoreyLibrary.Find(picked[(int)kind]);
            if (i != null && i.kind == kind) return i;
            // nothing picked yet: the kind's first of the project's, or its first
            var first = StoreyLibrary.Of(kind).FirstOrDefault(x => !x.package && Shows(x)) ?? StoreyLibrary.Of(kind).FirstOrDefault(Shows);
            if (first != null) picked[(int)kind] = first.guid;
            return first;
        }

        void Pick(LibraryItem i)
        {
            if (picked[(int)i.kind] != i.guid) { panelScroll = Vector2.zero; renaming = ""; }
            kind = i.kind; picked[(int)i.kind] = i.guid;
            GUI.FocusControl(null);
            Repaint();
        }

        void Pick(UnityEngine.Object asset)
        {
            var i = StoreyLibrary.Find(asset);
            if (i == null) { StoreyLibrary.Changed(); i = StoreyLibrary.Find(asset); }
            if (i == null) return;
            if (!Shows(i)) source = 0;
            if (!StoreyLibrary.Matches(i, search)) search = "";
            Pick(i);
            scrollTo = i.guid;
        }

        bool Shows(LibraryItem i) => source == 0 || (source == 1) == !i.package;

        void Say(string text) { message = text; messageUntil = EditorApplication.timeSinceStartup + 8; Repaint(); }

        // ---- the window ----

        const float RailWidth = 172, BarHeight = 21, StatusHeight = 20;

        void OnGUI()
        {
            thumbs ??= new StoreyThumbnails();
            if (Event.current.type == EventType.MouseMove) Repaint();
            float w = position.width, h = position.height;
            Toolbar(new Rect(0, 0, w, BarHeight));
            float panel = Mathf.Clamp(PanelWidth, 340, Math.Max(340, w - RailWidth - 300));
            var body = new Rect(0, BarHeight, w, h - BarHeight - StatusHeight);
            var rail = new Rect(0, body.y, RailWidth, body.height);
            var browse = new Rect(RailWidth, body.y, Math.Max(120, w - RailWidth - panel), body.height);
            var right = new Rect(browse.xMax, body.y, w - browse.xMax, body.height);
            SplitPanel(new Rect(right.x - 3, right.y, 6, right.height));   // first: its clicks before the list's scroll bar and the preview take them
            Rail(rail);
            Browser(browse);
            Panel(right);
            StatusBar(new Rect(0, h - StatusHeight, w, StatusHeight));
        }

        // ---- toolbar ----

        void Toolbar(Rect r)
        {
            GUILayout.BeginArea(r, EditorStyles.toolbar);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("New ▾", "Make a style, a window, a door, a facade detail or a template"), EditorStyles.toolbarDropDown, GUILayout.Width(60))) NewMenu();
                GUILayout.Space(8);
                searchField ??= new SearchField();
                string s = searchField.OnToolbarGUI(search, GUILayout.MinWidth(130), GUILayout.MaxWidth(340));
                if (s != search) { search = s; browseScroll = Vector2.zero; }
                GUILayout.FlexibleSpace();
                int src = GUILayout.Toolbar(source, Sources, EditorStyles.toolbarButton, GUILayout.Width(240));
                if (src != source) { source = src; browseScroll = Vector2.zero; }
                GUILayout.Space(8);
                bool grid = IconToggle(!AsList, StoreyInspectorUI.GridIcon, "Cards with pictures");
                bool list = IconToggle(AsList, StoreyInspectorUI.ListIcon, "A list, one line each");
                if (grid && AsList) AsList = false; else if (list && !AsList) AsList = true;
                using (new EditorGUI.DisabledScope(AsList))
                {
                    float size = GUILayout.HorizontalSlider(CardSize, 92, 200, GUILayout.Width(70));
                    if (Math.Abs(size - CardSize) > 0.5f) CardSize = size;
                }
                GUILayout.Space(6);
            }
            GUILayout.EndArea();
        }

        bool IconToggle(bool on, Action<Rect, Color> icon, string tip)
        {
            bool now = GUILayout.Toggle(on, new GUIContent("", tip), EditorStyles.toolbarButton, GUILayout.Width(26));
            if (Event.current.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                icon(new Rect(r.center.x - 7, r.center.y - 7, 14, 14), on ? StoreyInspectorUI.Accent : StoreyInspectorUI.Muted);
            }
            return now;
        }

        // ---- side bar ----

        void Rail(Rect r)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint) { EditorGUI.DrawRect(r, Look.Rail); EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), Look.Line); }
            float y = r.y + 12;
            GUI.Label(new Rect(r.x + 14, y, r.width - 20, 14), "STOREY LIBRARY", Look.Heading);
            y += 22;
            foreach (var k in StoreyLibrary.Kinds)
            {
                RailRow(new Rect(r.x + 6, y, r.width - 12, 34), k);
                y += 36;
            }
            y += 8;
            if (ev.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(r.x + 12, y, r.width - 24, 1), Look.Line);
            y += 12;
            SceneBox(new Rect(r.x + 10, y, r.width - 20, r.yMax - y - 8));
        }

        void RailRow(Rect row, LibraryKind k)
        {
            var ev = Event.current;
            bool on = k == kind;
            var tint = StoreyLibrary.Tint(k);
            if (ev.type == EventType.Repaint)
            {
                if (on)
                {
                    EditorGUI.DrawRect(row, new Color(tint.r, tint.g, tint.b, 0.17f));
                    EditorGUI.DrawRect(new Rect(row.x, row.y + 5, 3, row.height - 10), tint);
                }
                else if (row.Contains(ev.mousePosition)) EditorGUI.DrawRect(row, Look.Hover);
                StoreyLibrary.Icon(k)(new Rect(row.x + 12, row.y + 8, 18, 18), tint);
                var label = on ? Look.RailOn : Look.RailOff;
                label.Draw(new Rect(row.x + 38, row.y, row.width - 80, row.height), StoreyLibrary.Name(k), false, false, false, false);
                int n = StoreyLibrary.Of(k).Count(Shows);
                var pill = new Rect(row.xMax - 34, row.y + 9, 28, 16);
                EditorGUI.DrawRect(pill, on ? new Color(tint.r, tint.g, tint.b, 0.3f) : Look.Pill);
                Look.Count.Draw(pill, n.ToString(), false, false, false, false);
            }
            if (GUI.Button(row, new GUIContent("", StoreyLibrary.About(k)), GUIStyle.none) && !on)
            {
                kind = k; browseScroll = Vector2.zero; panelScroll = Vector2.zero; renaming = ""; GUI.FocusControl(null);
            }
            EditorGUIUtility.AddCursorRect(row, MouseCursor.Link);
        }

        /// <summary>The building the Library acts on: the one being edited in the selected Storey Site, and its tier's style.</summary>
        void SceneBox(Rect r)
        {
            GUILayout.BeginArea(r);
            GUILayout.Label("IN THE SCENE", Look.Heading);
            GUILayout.Space(4);
            var t = Target();
            if (t == null)
            {
                var s = Site();
                GUILayout.Label(s == null
                    ? "Select a Storey Site and a building, and what you pick here can be used on it."
                    : "Pick a building in the Scene view, and what you pick here can be used on it.", Look.Wrap);
            }
            else
            {
                var (site, e, b, k0) = t.Value;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var ic = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16), GUILayout.Height(16));
                    if (Event.current.type == EventType.Repaint) StoreyInspectorUI.FacadeIcon(ic, StoreyInspectorUI.Ink);
                    GUILayout.Label(new GUIContent(StoreyInspectorUI.Fit(b.name, r.width - 24, EditorStyles.boldLabel), b.name + ": the building being edited in " + site.name), EditorStyles.boldLabel);
                }
                var ts = Tiers.Of(b);
                string tier = ts.Count > 1 ? (k0 > 0 ? "Setback from floor " + k0 : "Base") + " · " : "";
                var st = Styles.Edited(b, Styles.HasOwn(b, k0) ? k0 : 0);
                string style = FacadeStylePreset.Of(st)?.asset.name ?? "a style of its own";
                GUILayout.Label($"{tier}{b.floors.Count} storeys", Look.Wrap);
                GUILayout.Label("Wears " + style, Look.Wrap);
                GUILayout.Label("In " + site.name, Look.Wrap);
            }
            GUILayout.EndArea();
        }

        static StoreySite? Site()
        {
            var go = Selection.activeGameObject;
            var site = go != null ? go.GetComponent<StoreySite>() : null;
            return site != null && site.layout != null && StoreyEdit.Of(site) != null ? site : null;
        }

        /// <summary>The building being edited in the selected site, the edit, and the tier the Facade tab shows; null with none.</summary>
        static (StoreySite site, StoreyEdit e, BuildingData b, int k0)? Target()
        {
            var site = Site();
            var e = site != null ? StoreyEdit.Of(site) : null;
            var b = e?.Selected;
            if (site == null || e == null || b == null) return null;
            int k0 = Tiers.Of(b).Count > 1 ? e.View.tier : 0;
            if (k0 >= b.floors.Count) k0 = 0;
            return (site, e, b, k0);
        }

        // ---- the list ----

        void Browser(Rect r)
        {
            var ev = Event.current;
            var shown = StoreyLibrary.Of(kind).Where(i => Shows(i) && StoreyLibrary.Matches(i, search)).ToList();
            // the panel shows one of the list: a search or a filter that hides the item picked picks the first it leaves
            // (the search keeps its focus, as it's typed in)
            var cur = Picked();
            if (shown.Count > 0 && (cur == null || !shown.Contains(cur))) { picked[(int)kind] = shown[0].guid; panelScroll = Vector2.zero; renaming = ""; }
            var head = new Rect(r.x + 16, r.y + 10, r.width - 32, 52);
            var tint = StoreyLibrary.Tint(kind);
            if (ev.type == EventType.Repaint)
            {
                StoreyLibrary.Icon(kind)(new Rect(head.x, head.y + 1, 20, 20), tint);
                Look.Title.Draw(new Rect(head.x + 28, head.y - 1, head.width - 28, 22), StoreyLibrary.Name(kind), false, false, false, false);
                int all = StoreyLibrary.Of(kind).Count();
                string counted = shown.Count == all ? $"{all}" : $"{shown.Count} of {all}";
                float tw = Look.Title.CalcSize(new GUIContent(StoreyLibrary.Name(kind))).x;
                Look.Muted.Draw(new Rect(head.x + 34 + tw, head.y + 3, 120, 18), counted, false, false, false, false);
            }
            // what the kind is, under its name, in as many lines as it takes (three at most)
            var about = new GUIContent(StoreyLibrary.About(kind));
            float ah = Math.Min(Look.Wrap.CalcHeight(about, head.width), Look.Wrap.lineHeight * 3 + 2);
            GUI.Label(new Rect(head.x, head.y + 24, head.width, ah), about, Look.Wrap);
            head.height = 26 + ah;
            var view = new Rect(r.x, head.yMax + 6, r.width, r.yMax - head.yMax - 6);
            Drop(view);
            if (shown.Count == 0) { Empty(view); if (dropping) DropOverlay(view); return; }

            var groups = Groups(shown);
            float inner = view.width - 14, pad = 12, gap = 8;
            bool list = AsList;
            int cols = list ? 1 : Math.Max(1, (int)((inner - pad * 2 + gap) / (CardSize + gap)));
            // the cards share the row's width, but never grow much past the size picked
            float cw = list ? inner - pad * 2 : Math.Min((inner - pad * 2 - gap * (cols - 1)) / cols, CardSize * 1.3f);
            float ch = list ? 30 : cw + 42;
            float total = pad;
            foreach (var (family, items) in groups)
                total += (family.Length > 0 ? 26 : 0) + (float)Math.Ceiling(items.Count / (double)cols) * (ch + (list ? 2 : gap)) + 6;
            browseScroll = GUI.BeginScrollView(view, browseScroll, new Rect(0, 0, inner, total));
            float y = pad, top = browseScroll.y - ch, bottom = browseScroll.y + view.height + ch;
            foreach (var (family, items) in groups)
            {
                if (family.Length > 0)
                {
                    if (y + 24 > top && y < bottom) Family(new Rect(pad, y, inner - pad * 2, 22), family, items.Count);
                    y += 26;
                }
                for (int n = 0; n < items.Count; n++)
                {
                    var cell = new Rect(pad + (n % cols) * (cw + gap), y + (n / cols) * (ch + (list ? 2 : gap)), cw, ch);
                    if (scrollTo == items[n].guid && ev.type == EventType.Repaint)
                    {
                        if (cell.y < browseScroll.y) browseScroll.y = cell.y - pad;
                        else if (cell.yMax > browseScroll.y + view.height) browseScroll.y = cell.yMax - view.height + pad;
                        scrollTo = null; Repaint();
                    }
                    if (cell.yMax < top || cell.y > bottom) continue;
                    if (list) Row(cell, items[n]); else Card(cell, items[n], family.Length > 0);
                }
                y += (float)Math.Ceiling(items.Count / (double)cols) * (ch + (list ? 2 : gap)) + 6;
            }
            GUI.EndScrollView();
            if (dropping) DropOverlay(view);
        }

        /// <summary>The items in families where it helps (eight or more, in more than one family), the project's first; otherwise one list.</summary>
        static List<(string family, List<LibraryItem> items)> Groups(List<LibraryItem> shown)
        {
            var families = shown.GroupBy(i => i.family).Select(g => (g.Key, g.ToList())).ToList();
            if (shown.Count < 8 || families.Count < 2) return new List<(string, List<LibraryItem>)> { ("", shown) };
            return families;
        }

        void Family(Rect r, string family, int count)
        {
            if (Event.current.type != EventType.Repaint) return;
            var c = new GUIContent(family);
            float tw = Look.FamilyName.CalcSize(c).x;
            Look.FamilyName.Draw(new Rect(r.x, r.y + 2, tw + 4, 18), c, false, false, false, false);
            Look.Muted.Draw(new Rect(r.x + tw + 8, r.y + 3, 40, 18), count.ToString(), false, false, false, false);
            float cx = tw + 8 + Look.Muted.CalcSize(new GUIContent(count.ToString())).x + 8;
            EditorGUI.DrawRect(new Rect(r.x + cx, r.center.y + 1, Math.Max(0, r.width - cx), 1), Look.Line);
        }

        /// <summary>An item as a card: its picture, badges and colours, its name (without its family's words under a family's header, <paramref name="grouped"/>) and a line about it.</summary>
        void Card(Rect r, LibraryItem i, bool grouped)
        {
            var ev = Event.current;
            bool on = Picked() == i, hover = r.Contains(ev.mousePosition);
            var tint = StoreyLibrary.Tint(i.kind);
            var img = new Rect(r.x + 5, r.y + 5, r.width - 10, r.width - 10);
            var facts = StoreyLibrary.FactsOf(i);
            if (ev.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, on ? Mix(Look.Card, tint, 0.22f) : hover ? Look.CardHover : Look.Card);
                var tex = thumbs!.Get(i);
                if (tex != null) GUI.DrawTexture(img, tex, ScaleMode.ScaleAndCrop);
                else
                {
                    EditorGUI.DrawRect(img, StoreyPreview.Background);
                    float s = img.width * 0.34f;
                    StoreyLibrary.Icon(i.kind)(new Rect(img.center.x - s / 2, img.center.y - s / 2, s, s), new Color(tint.r, tint.g, tint.b, 0.45f));
                }
                if (i.package) Chip(new Rect(img.x + 4, img.y + 4, 0, 16), "Storey", StoreyInspectorUI.LockIcon, Look.ChipBack);
                if (facts.Warnings > 0)
                {
                    var wr = new Rect(img.xMax - 22, img.y + 4, 18, 18);
                    EditorGUI.DrawRect(wr, new Color(0, 0, 0, 0.45f));
                    StoreyInspectorUI.WarningIcon(new Rect(wr.x + 2, wr.y + 2, 14, 14), Look.Amber);
                }
                if (facts.swatches.Length > 0)
                {
                    float sw = 9, sx = img.x + 4, sy = img.yMax - sw - 4;
                    EditorGUI.DrawRect(new Rect(sx - 1, sy - 1, facts.swatches.Length * (sw + 1) + 1, sw + 2), new Color(0, 0, 0, 0.5f));
                    for (int n = 0; n < facts.swatches.Length; n++) EditorGUI.DrawRect(new Rect(sx + n * (sw + 1), sy, sw, sw), facts.swatches[n]);
                }
                int used = StoreyUsage.Buildings(i);
                if (used > 0) Chip(new Rect(img.xMax - 4, img.yMax - 20, 0, 16), used.ToString(), StoreyInspectorUI.LayoutIcon, Look.ChipBack, right: true);
                if (on) Frame(r, tint, 2); else if (hover) Frame(r, Look.Line, 1);
                Look.CardTitle.Draw(new Rect(r.x + 6, img.yMax + 5, r.width - 12, 16), StoreyInspectorUI.Fit(grouped ? i.shortTitle : i.title, r.width - 14, Look.CardTitle), false, false, false, false);
                Look.CardLine.Draw(new Rect(r.x + 6, img.yMax + 21, r.width - 12, 14), StoreyInspectorUI.Fit(facts.line, r.width - 14, Look.CardLine), false, false, false, false);
            }
            Interact(r, i, Tip(i, facts));
        }

        void Row(Rect r, LibraryItem i)
        {
            var ev = Event.current;
            bool on = Picked() == i, hover = r.Contains(ev.mousePosition);
            var tint = StoreyLibrary.Tint(i.kind);
            var facts = StoreyLibrary.FactsOf(i);
            if (ev.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, on ? Mix(Look.Card, tint, 0.22f) : hover ? Look.CardHover : Look.Card);
                if (on) EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), tint);
                var img = new Rect(r.x + 6, r.y + 3, r.height - 6, r.height - 6);
                var tex = thumbs!.Get(i);
                if (tex != null) GUI.DrawTexture(img, tex, ScaleMode.ScaleAndCrop);
                else StoreyLibrary.Icon(i.kind)(new Rect(img.x + 3, img.y + 3, img.width - 6, img.height - 6), new Color(tint.r, tint.g, tint.b, 0.6f));
                float x = img.xMax + 8, w = r.width * 0.42f;
                Look.CardTitle.Draw(new Rect(x, r.y + 7, w, 16), StoreyInspectorUI.Fit(i.title, w, Look.CardTitle), false, false, false, false);
                x += w + 6;
                float lw = r.xMax - x - 120;
                if (lw > 40) Look.CardLine.Draw(new Rect(x, r.y + 8, lw, 16), StoreyInspectorUI.Fit(facts.line, lw, Look.CardLine), false, false, false, false);
                float rx = r.xMax - 8;
                int used = StoreyUsage.Buildings(i);
                if (used > 0) rx = Chip(new Rect(rx, r.y + 7, 0, 16), used.ToString(), StoreyInspectorUI.LayoutIcon, Look.ChipBack, right: true) - 4;
                if (facts.Warnings > 0) { StoreyInspectorUI.WarningIcon(new Rect(rx - 16, r.y + 8, 14, 14), Look.Amber); rx -= 22; }
                if (i.package) Chip(new Rect(rx, r.y + 7, 0, 16), "Storey", StoreyInspectorUI.LockIcon, Look.ChipBack, right: true);
            }
            Interact(r, i, Tip(i, facts));
        }

        static string Tip(LibraryItem i, StoreyLibrary.Facts f)
        {
            string tip = i.asset.name;
            int used = StoreyUsage.Buildings(i);
            if (used > 0) tip += $"\nUsed by {used} building{(used == 1 ? "" : "s")}";
            foreach (var (text, type) in f.issues.Where(x => x.type == MessageType.Warning || x.type == MessageType.Error)) tip += "\n⚠ " + text;
            return tip;
        }

        /// <summary>A card's or row's clicks: pick it; a double click shows it in the Project window; a right click its menu; a drag takes the asset (to a field, the Project window).</summary>
        void Interact(Rect r, LibraryItem i, string tip)
        {
            var ev = Event.current;
            GUI.Label(r, new GUIContent("", tip));
            switch (ev.type)
            {
                case EventType.MouseDown when r.Contains(ev.mousePosition):
                    if (ev.button == 0)
                    {
                        Pick(i);
                        if (ev.clickCount == 2) EditorGUIUtility.PingObject(i.asset);
                        pressed = i;
                    }
                    else if (ev.button == 1) { Pick(i); ItemMenu(i); }
                    ev.Use();
                    break;
                case EventType.MouseDrag when pressed == i:
                    pressed = null;
                    DragAndDrop.PrepareStartDrag();
                    DragAndDrop.objectReferences = new[] { i.asset };
                    DragAndDrop.paths = new[] { i.path };
                    DragAndDrop.StartDrag(i.title);
                    ev.Use();
                    break;
                case EventType.MouseUp:
                    if (pressed == i) pressed = null;
                    break;
            }
        }

        void Empty(Rect r)
        {
            var tint = StoreyLibrary.Tint(kind);
            float s = 44;
            var ir = new Rect(r.center.x - s / 2, r.y + r.height * 0.28f, s, s);
            if (Event.current.type == EventType.Repaint) StoreyLibrary.Icon(kind)(ir, new Color(tint.r, tint.g, tint.b, 0.5f));
            string text = search.Length > 0 ? $"Nothing here matches “{search}”."
                : source == 2 ? $"Storey ships no {StoreyLibrary.Name(kind).ToLowerInvariant()} of its own."
                : kind == LibraryKind.Templates ? "No templates of the project's yet. Select a building in a Storey Site and use New ▸ Template from it, or Save as template in its inspector."
                : kind == LibraryKind.Styles ? "No styles of the project's yet. New ▸ Style makes one, or Duplicate one of Storey's own."
                : $"No {StoreyLibrary.Name(kind).ToLowerInvariant()} of the project's yet. Drop prefabs here to make them, or use New.";
            GUI.Label(new Rect(r.x + 30, ir.yMax + 12, r.width - 60, 60), text, Look.Centred);
            if (search.Length > 0 && GUI.Button(new Rect(r.center.x - 60, ir.yMax + 64, 120, 22), "Clear the search")) { search = ""; GUI.FocusControl(null); }
        }

        // ---- prefabs dropped on the list ----

        void Drop(Rect r)
        {
            var ev = Event.current;
            if (ev.type == EventType.DragExited || (ev.type == EventType.DragUpdated && !r.Contains(ev.mousePosition))) { if (dropping) { dropping = false; Repaint(); } return; }
            if (!(ev.type == EventType.DragUpdated || ev.type == EventType.DragPerform) || !r.Contains(ev.mousePosition)) return;
            var prefabs = DragAndDrop.objectReferences.OfType<GameObject>().Where(AssetDatabase.Contains).ToList();
            if (prefabs.Count == 0 || !(kind is LibraryKind.Windows or LibraryKind.Doors or LibraryKind.Details)) { dropping = false; return; }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            dropping = true; dropCount = prefabs.Count;
            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                dropping = false;
                var made = StoreyLibrary.FromPrefabs(kind, prefabs);
                if (made.Count > 0) { Pick(made[0]); Say($"Made {made.Count} {(made.Count == 1 ? StoreyLibrary.One(kind) : StoreyLibrary.Name(kind).ToLowerInvariant())}, each beside its prefab."); }
            }
            ev.Use();
            Repaint();
        }

        void DropOverlay(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            var tint = StoreyLibrary.Tint(kind);
            EditorGUI.DrawRect(r, new Color(tint.r, tint.g, tint.b, 0.14f));
            Frame(new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12), tint, 2);
            string what = dropCount == 1 ? StoreyLibrary.One(kind) : $"{dropCount} {StoreyLibrary.Name(kind).ToLowerInvariant()}";
            Look.DropText.Draw(new Rect(r.x, r.center.y - 20, r.width, 40), $"Drop to make {(dropCount == 1 ? "a " : "")}{what} from {(dropCount == 1 ? "this prefab" : "these prefabs")}", false, false, false, false);
        }

        // ---- the item picked ----

        void Panel(Rect r)
        {
            var ev = Event.current;
            if (ev.type == EventType.Repaint) { EditorGUI.DrawRect(r, Look.Panel); EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), Look.Line); }
            var i = Picked();
            if (i == null)
            {
                GUI.Label(new Rect(r.x + 20, r.center.y - 30, r.width - 40, 60), "Pick something on the left to see it on a building.", Look.Centred);
                return;
            }
            float ph = Mathf.Clamp(PreviewHeight, 170, Math.Max(170, r.height - 140));
            var pv = new Rect(r.x, r.y, r.width, ph);
            SplitPreview(new Rect(r.x, pv.yMax - 2, r.width, 6));   // first: its clicks before the preview takes them
            Preview(pv, i);
            var rest = new Rect(r.x, pv.yMax + 2, r.width, r.yMax - pv.yMax - 2);
            GUILayout.BeginArea(rest);
            // never wider than the panel: no sideways scrolling, and long lines wrap
            panelScroll = EditorGUILayout.BeginScrollView(panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            float labelWidth = EditorGUIUtility.labelWidth; bool wide = EditorGUIUtility.wideMode;
            float inner = rest.width - 12 - 10 - 22;   // the scroll bar, and a margin either side
            column = inner;
            EditorGUIUtility.labelWidth = Mathf.Clamp(inner * 0.4f, 110, 175);
            EditorGUIUtility.wideMode = inner > 330;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(inner)))
                {
                    GUILayout.Space(10);
                    Header(i);
                    Actions(i);
                    if (message != null) { GUILayout.Space(4); EditorGUILayout.HelpBox(message, MessageType.Info); }
                    Used(i);
                    GUILayout.Space(6);
                    Settings(i);
                    GUILayout.Space(16);
                }
                GUILayout.Space(10);
            }
            EditorGUIUtility.labelWidth = labelWidth; EditorGUIUtility.wideMode = wide;
            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---- the preview ----

        void Preview(Rect pv, LibraryItem i)
        {
            preview ??= new StoreyPreview();
            preview.Lod = Lod;
            bool onStyle = i.kind is LibraryKind.Windows or LibraryKind.Doors or LibraryKind.Details || (i.kind == LibraryKind.Templates && !TemplateOwnStyle);
            FacadeStyle? on = null; string onKey = "";
            if (onStyle) { var s = StoreyLibrary.ShowOn(ShowOnKey); on = s.style; onKey = s.stamp; }
            string key = i.guid + "|" + StoreyLibrary.Stamp(i) + "|" + Shape + "|" + Storeys + "|" + onKey;
            if (key != preview.ShownKey && EditorApplication.timeSinceStartup >= retryAt)
            {
                try
                {
                    var (doc, framing) = StoreyLibrary.Placeholder(i, Shape, Storeys, on);
                    preview.Show(doc, key, framing);
                }
                catch (Exception ex)
                {
                    // tried again shortly (the editor may still be settling after a script reload), said once
                    if (failedKey != key) { failedKey = key; Debug.LogWarning($"Storey Library: couldn't show {i.asset.name} on a building yet ({ex.GetType().Name}: {ex.Message}); trying again."); }
                    retryAt = EditorApplication.timeSinceStartup + 1;
                }
            }

            // the controls over the preview, laid out first: the preview leaves their clicks to them
            bool template = i.kind == LibraryKind.Templates;
            var lodR = new Rect(pv.x + 8, pv.y + 8, 138, 20);
            var onR = new Rect(pv.xMax - 8 - Math.Min(210, pv.width - 170), pv.y + 8, Math.Min(210, pv.width - 170), 20);
            var shapesR = new Rect(pv.x + 8, pv.yMax - 30, Shapes.Length * 26, 22);
            var storeysR = new Rect(shapesR.xMax + 8, pv.yMax - 30, 104, 22);
            var toolsR = new Rect(pv.xMax - 64, pv.yMax - 30, 56, 22);
            var holes = new List<Rect> { lodR, toolsR };
            if (onStyle || template) holes.Add(onR);
            if (!template) { holes.Add(shapesR); holes.Add(storeysR); }
            if (preview.Draw(pv, holes)) Repaint();
            if (preview.Error != null)
                GUI.Label(new Rect(pv.x + 20, pv.center.y - 30, pv.width - 40, 60), "This one doesn't build:\n" + preview.Error, Look.Centred);

            int lod = GUI.Toolbar(lodR, Lod, LodLabels, EditorStyles.miniButton);
            if (lod != Lod) { Lod = lod; preview.Lod = lod; }
            if (onStyle || template) ShowOnField(onR, i);
            if (!template)
            {
                for (int n = 0; n < Shapes.Length; n++)
                {
                    var (sk, sn) = Shapes[n];
                    var b = new Rect(shapesR.x + n * 26, shapesR.y, 24, 22);
                    bool was = Shape == sk;
                    if (GUI.Toggle(b, was, new GUIContent("", sn + ": the building's footprint"), EditorStyles.miniButton) && !was) Shape = sk;
                    if (Event.current.type == EventType.Repaint) StoreyInspectorUI.Outline(new Rect(b.x + 4, b.y + 3, 16, 16), was ? StoreyInspectorUI.Accent : StoreyInspectorUI.Ink, Setbacks.Preset(sk));
                }
                if (GUI.Button(new Rect(storeysR.x, storeysR.y, 22, 22), new GUIContent("−", "One storey fewer"), EditorStyles.miniButtonLeft) && Storeys > 1) Storeys = Storeys - 1;
                GUI.Box(new Rect(storeysR.x + 22, storeysR.y, 60, 22), "", EditorStyles.miniButtonMid);
                GUI.Label(new Rect(storeysR.x + 22, storeysR.y + 1, 60, 20), Storeys == 1 ? "1 storey" : $"{Storeys} storeys", Look.CentredMini);
                if (GUI.Button(new Rect(storeysR.x + 82, storeysR.y, 22, 22), new GUIContent("+", "One storey more"), EditorStyles.miniButtonRight) && Storeys < 30) Storeys = Storeys + 1;
            }
            bool turn = GUI.Toggle(new Rect(toolsR.x, toolsR.y, 26, 22), preview.Turntable, new GUIContent("", "Turn slowly round the building"), EditorStyles.miniButtonLeft);
            if (turn != preview.Turntable) preview.Turntable = turn;
            if (GUI.Button(new Rect(toolsR.x + 26, toolsR.y, 26, 22), new GUIContent("", "Back to the first view (or double-click the preview)"), EditorStyles.miniButtonRight)) preview.Reframe();
            if (Event.current.type == EventType.Repaint)
            {
                StoreyInspectorUI.TurnIcon(new Rect(toolsR.x + 6, toolsR.y + 4, 14, 14), preview.Turntable ? StoreyInspectorUI.Accent : StoreyInspectorUI.Ink);
                StoreyInspectorUI.FrameIcon(new Rect(toolsR.x + 32, toolsR.y + 4, 14, 14), StoreyInspectorUI.Ink);
            }
            EditorGUIUtility.AddCursorRect(shapesR, MouseCursor.Arrow);
        }

        static readonly (string key, string name)[] Shapes = StoreyLibrary.Shapes;

        /// <summary>Which style a window, door or detail is shown on (a template: its own, or one picked).</summary>
        void ShowOnField(Rect r, LibraryItem i)
        {
            var all = FacadeStylePreset.All();
            var names = new List<string>();
            int offset = 0;
            if (i.kind == LibraryKind.Templates) { names.Add("Its own style"); offset = 1; }
            names.AddRange(all.Select(e => (e.own ? "Storey/" : "") + e.asset.name));
            var cur = StoreyLibrary.ShowOn(ShowOnKey).key;
            int at = i.kind == LibraryKind.Templates && TemplateOwnStyle ? 0 : offset + Math.Max(0, all.ToList().FindIndex(e => e.key == cur));
            var lr = new Rect(r.x - 26, r.y + 2, 24, 16);
            if (Event.current.type == EventType.Repaint) Look.Overlay.Draw(lr, "On", false, false, false, false);
            int pick = EditorGUI.Popup(r, at, names.ToArray(), EditorStyles.miniPullDown);
            if (pick == at) return;
            if (i.kind == LibraryKind.Templates && pick == 0) { TemplateOwnStyle = true; return; }
            TemplateOwnStyle = false;
            ShowOnKey = all[pick - offset].key;
        }

        // ---- the item's header and what can be done with it ----

        void Header(LibraryItem i)
        {
            var tint = StoreyLibrary.Tint(i.kind);
            using (new EditorGUILayout.HorizontalScope())
            {
                var ic = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                if (Event.current.type == EventType.Repaint) StoreyLibrary.Icon(i.kind)(new Rect(ic.x, ic.y + 2, 20, 20), tint);
                GUILayout.Space(4);
                if (renaming == i.guid)
                {
                    GUI.SetNextControlName("StoreyLibraryRename");
                    string to = EditorGUILayout.DelayedTextField(renameText, Look.RenameField, GUILayout.Height(22));
                    if (to != renameText)
                    {
                        renaming = "";
                        string? why = StoreyLibrary.Rename(i, to);
                        if (why != null) Say("Couldn't rename it: " + why);
                        else { var again = StoreyLibrary.Find(i.asset); if (again != null) Pick(again); }
                    }
                    if (GUILayout.Button("Cancel", EditorStyles.miniButton, GUILayout.Width(52))) renaming = "";
                }
                else
                {
                    GUILayout.Label(new GUIContent(i.title, i.asset.name), Look.Name);
                    using (new EditorGUI.DisabledScope(i.package))
                        if (IconButton(StoreyInspectorUI.PencilIcon, i.package ? "Storey's own keep their names" : "Rename it"))
                        {
                            renaming = i.guid; renameText = i.asset.name;
                            EditorApplication.delayCall += () => { EditorGUI.FocusTextInControl("StoreyLibraryRename"); Repaint(); };
                        }
                    GUILayout.FlexibleSpace();
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(28);
                Chip(StoreyLibrary.One(i.kind), tint, null);
                if (i.package) Chip("Storey's own", Look.Muted.normal.textColor, StoreyInspectorUI.LockIcon);
                else Chip("The project's", Look.Muted.normal.textColor, null);
                if (i.family != StoreyLibrary.OwnFamily && i.family != StoreyLibrary.Others) Chip(i.family, Look.Muted.normal.textColor, null);
                if (EditorUtility.IsDirty(i.asset))
                {
                    Chip("Not saved", Look.Amber, null);
                    if (GUILayout.Button(new GUIContent("Save", "Write it to its file now (File ▸ Save Project does too)"), EditorStyles.miniButton, GUILayout.Width(42))) AssetDatabase.SaveAssetIfDirty(i.asset);
                }
                GUILayout.FlexibleSpace();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(28);
                // cut to the column: a label as long as its text would widen the whole column
                if (GUILayout.Button(new GUIContent(StoreyInspectorUI.Fit(i.path, column - 34, Look.Path), i.path + "\nShow it in the Project window"), Look.Path, GUILayout.MaxWidth(column - 30))) EditorGUIUtility.PingObject(i.asset);
                EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
            }
            if (preview != null && preview.Ready)
            {
                var (l0, rooms, l1, l2) = preview.Triangles();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(28);
                    GUILayout.Label(new GUIContent($"Building shown: LOD0 {l0:N0} triangles{(rooms > 0 ? $" (+{rooms:N0} rooms)" : "")} · LOD1 {l1:N0} · LOD2 {l2:N0}",
                        "Triangles of the whole building in the preview, at each LOD: compare one style, window or detail with another"), Look.Wrap);
                }
            }
        }

        void Actions(LibraryItem i)
        {
            GUILayout.Space(8);
            UseButton(i);
            GUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Duplicate", i.package ? "A copy of your own, to change as you like" : "A copy of it, beside it"), GUILayout.Height(24)))
                {
                    var copy = StoreyLibrary.Duplicate(i);
                    if (copy != null) { Pick(copy); Say($"“{copy.name}” is the project's own: change it as you like."); }
                }
                if (GUILayout.Button(new GUIContent("Show in Project", "Show its asset in the Project window"), GUILayout.Height(24))) EditorGUIUtility.PingObject(i.asset);
                var more = GUILayoutUtility.GetRect(26, 24, GUILayout.Width(26), GUILayout.Height(24));
                if (GUI.Button(more, new GUIContent("", "More: rename, delete, open its prefab…"))) ItemMenu(i);
                if (Event.current.type == EventType.Repaint) StoreyInspectorUI.MoreIcon(new Rect(more.x + 6, more.y + 5, 14, 14), StoreyInspectorUI.Ink);
            }
            if (i.package)
            {
                GUILayout.Space(6);
                EditorGUILayout.HelpBox($"One of Storey's own {StoreyLibrary.Name(i.kind).ToLowerInvariant()}: it ships with the package, so it's shown here, not changed. Duplicate it to make one of your own.", MessageType.None);
            }
        }

        /// <summary>
        /// Using the item on the building being edited in the scene: a style applied to its tier; a window or door made its
        /// only one, or added to its list; a detail given a chance in its style, or picked to place by hand; a template placed
        /// in the layout as a new building.
        /// </summary>
        void UseButton(LibraryItem i)
        {
            var t = Target(); var site = Site();
            bool can = i.kind == LibraryKind.Templates ? site != null && i.Template!.Building != null : t != null;
            string label = i.kind switch
            {
                LibraryKind.Styles => t != null ? $"Apply to {t.Value.b.name}" : "Apply to a building",
                LibraryKind.Templates => site != null ? $"Add to {site.layout!.name}" : "Add to a layout",
                _ => t != null ? $"Use on {t.Value.b.name} ▾" : "Use on a building ▾",
            };
            string tip = can ? i.kind switch
            {
                LibraryKind.Styles => "Apply this style to the building being edited (its tier, with a setback), keeping its roof: as picking it under Preset in the Facade tab",
                LibraryKind.Templates => "A new building from this template, near the Scene view's middle, in the layout being edited",
                LibraryKind.Windows => "Make it the building's window, or one of its windows",
                LibraryKind.Doors => "Make it the building's street door, or one of its doors",
                _ => "Give it a chance in the building's style, or place it by hand on its walls",
            } : i.kind == LibraryKind.Templates ? "Select a Storey Site first: the template is placed in its layout" : "Select a Storey Site and pick a building in it first";
            var keep = GUI.backgroundColor;
            if (can) GUI.backgroundColor = Color.Lerp(Color.white, StoreyLibrary.Tint(i.kind), 0.55f);
            var r = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            using (new EditorGUI.DisabledScope(!can))
                if (GUI.Button(r, new GUIContent(StoreyInspectorUI.Fit(label, r.width - 16, Look.UseButton), tip), Look.UseButton))
                    Use(i);
            GUI.backgroundColor = keep;
        }

        void Use(LibraryItem i)
        {
            var site = Site();
            if (i.kind == LibraryKind.Templates) { if (site != null) AddFromTemplate(i, site); return; }
            if (!(Target() is { } t)) return;
            var (s, e, b, k0) = t;
            int own = Styles.HasOwn(b, k0) ? k0 : 0;   // a setback that follows the floors below: theirs
            string where = Tiers.Of(b).Count > 1 ? (own > 0 ? " (its setback)" : " (its base)") : "";
            switch (i.kind)
            {
                case LibraryKind.Styles:
                {
                    FacadeStyle style;
                    try { style = i.Style!.Style; }
                    catch (Exception ex) { Say("It holds no style Storey can read: " + ex.Message); return; }
                    string key = i.Style!.Listed?.key ?? i.key;
                    e.ApplyTo(i.asset.name + " applied", bb => { Styles.Apply(bb, own, style, key); return true; });
                    Done($"{i.title} applied to {b.name}{where}.");
                    break;
                }
                case LibraryKind.Windows:
                case LibraryKind.Doors:
                {
                    bool door = i.kind == LibraryKind.Doors; var o = i.Opening!;
                    int k = door ? 0 : own;   // doors are the base's
                    var m = new GenericMenu();
                    m.AddItem(new GUIContent(door ? "Its only door" : "Its only window"), false, () =>
                    {
                        e.ApplyTo((door ? "Door: " : "Window: ") + i.title, bb =>
                        {
                            var st = Styles.Edited(bb, k);
                            if (door) { st.doorSet = null; st.doorKind = o.Id; }
                            else { st.windowSet = null; st.windowKind = o.Id; if (st.windows == WindowType.None) st.windows = WindowType.Punched; }
                            return true;
                        });
                        StoreyOpenings.Keep(s, o);
                        Done($"{b.name}'s {(door ? "doors are" : "windows are")} {i.title} now{(door ? "" : where)}.");
                    });
                    m.AddItem(new GUIContent(door ? "Another of its doors, by weight" : "Another of its windows, by weight"), false, () =>
                    {
                        e.ApplyTo((door ? "Door added: " : "Window added: ") + i.title, bb => { WindowSets.Add(Styles.Edited(bb, k), door ? SetOf.Doors : SetOf.Windows, o.Id); return true; });
                        StoreyOpenings.Keep(s, o);
                        Done($"{i.title} added to {b.name}'s {(door ? "doors" : "windows")}: each is picked by its weight (Facade tab).");
                    });
                    m.ShowAsContext();
                    break;
                }
                case LibraryKind.Details:
                {
                    var k = DetailKinds.Get(i.key);
                    var m = new GenericMenu();
                    bool rule = k != null && (Details.RuleFits(k) || i.key == DetailKinds.Ac || i.key == DetailKinds.Vent);
                    foreach (var p in new[] { 25, 50, 100 })
                    {
                        if (!rule) { m.AddDisabledItem(new GUIContent("Placed for you: it has no rule (set one in its settings)")); break; }
                        int percent = p;
                        m.AddItem(new GUIContent($"Placed for you, {percent}% in its style"), false, () =>
                        {
                            e.ApplyTo($"{i.title} {percent}%", bb =>
                            {
                                var st = Styles.Edited(bb, own);
                                if (i.key == DetailKinds.Ac) (st.details ??= new DetailRules()).ac = percent;
                                else if (i.key == DetailKinds.Vent) (st.details ??= new DetailRules()).vents = percent;
                                else StoreyDetails.SetDensity(st, i.key, percent);
                                return true;
                            });
                            StoreyDetails.Keep(s, i.key);
                            Done($"{i.title} placed for you at {percent}% on {b.name}{where}.");
                        });
                    }
                    m.AddSeparator("");
                    m.AddItem(new GUIContent("Place it by hand in the Scene view"), false, () =>
                    {
                        var v = e.View; v.facadeTool = i.key; v.tab = StoreyTab.Facade;
                        StoreyToolContext.Show(StoreyTab.Facade);
                        SceneView.RepaintAll();
                        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Focus();
                        Say($"Click a wall of {b.name} in the Scene view to put {i.title} there; click a placed one to take it away.");
                    });
                    m.ShowAsContext();
                    break;
                }
            }
        }

        void Done(string text)
        {
            Say(text);
            ShowNotification(new GUIContent(text), 2.5);
            SceneView.RepaintAll();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>A new building from a template, near the Scene view's middle, picked and on the Shape tab, as the building bar's New does.</summary>
        void AddFromTemplate(LibraryItem i, StoreySite site)
        {
            var tb = i.Template!.Building; if (tb == null) return;
            var e = StoreyEdit.Begin(site);
            var near = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot - site.transform.position : Vector3.zero;
            var start = i.package ? FacadeStylePreset.Default() : null;   // the package's own take the brick walk-up as it is now
            string id = "";
            e.Apply("Added a building from " + i.title, d =>
            {
                var nb = Buildings.FromTemplate(d, tb, new Vec2(near.x, near.z));
                if (start is (FacadeStyle sty, string sk)) Styles.Apply(nb, 0, sty, sk);
                id = nb.id; return true;
            });
            e.View.Select(id); e.View.tab = StoreyTab.Shape;
            StoreyToolContext.Show(StoreyTab.Shape);
            Done($"A building from {i.title} is in {site.layout!.name}, near the Scene view's middle.");
        }

        void ItemMenu(LibraryItem i)
        {
            var m = new GenericMenu();
            if (!i.package) m.AddItem(new GUIContent("Rename"), false, () => { renaming = i.guid; renameText = i.asset.name; Repaint(); });
            else m.AddDisabledItem(new GUIContent("Rename (Storey's own keep their names)"));
            m.AddItem(new GUIContent("Duplicate"), false, () => { var c = StoreyLibrary.Duplicate(i); if (c != null) Pick(c); });
            var prefab = i.Opening?.prefab ?? i.Detail?.prefab;
            if (prefab != null) m.AddItem(new GUIContent("Open its prefab"), false, () => AssetDatabase.OpenAsset(prefab));
            m.AddSeparator("");
            m.AddItem(new GUIContent("Show in the Project window"), false, () => EditorGUIUtility.PingObject(i.asset));
            m.AddItem(new GUIContent("Select it (the Inspector shows it; editing a site stops)"), false, () => Selection.activeObject = i.asset);
            m.AddItem(new GUIContent("Draw its picture again"), false, () => { thumbs?.Forget(); Refresh(); });
            m.AddSeparator("");
            if (!i.package) m.AddItem(new GUIContent("Delete…"), false, () => Delete(i));
            else m.AddDisabledItem(new GUIContent("Delete (Storey's own stay)"));
            m.ShowAsContext();
        }

        void Delete(LibraryItem i)
        {
            int used = StoreyUsage.Buildings(i);
            string after = i.kind switch
            {
                LibraryKind.Styles => used > 0 ? $"{used} building{(used == 1 ? " was" : "s were")} styled with it: they keep their copy of the style." : "No building in the project's layouts was styled with it.",
                LibraryKind.Windows or LibraryKind.Doors => used > 0 ? $"{used} building{(used == 1 ? " uses" : "s use")} it: they show the built-in one in its place until another is picked." : "No building in the project's layouts uses it.",
                LibraryKind.Details => used > 0 ? $"{used} building{(used == 1 ? " has" : "s have")} it: it's left off them, and their styles show it as a missing detail." : "No building in the project's layouts has it.",
                _ => "Buildings placed from it stay as they are.",
            };
            if (!EditorUtility.DisplayDialog($"Delete {i.title}?", $"{i.path}\n\n{after}\n\nIt goes to the system's trash.", "Delete", "Cancel")) return;
            if (StoreyLibrary.Delete(i)) { picked[(int)i.kind] = ""; Say($"{i.title} is in the trash."); }
        }

        // ---- where it's used ----

        void Used(LibraryItem i)
        {
            if (i.kind == LibraryKind.Templates) return;
            var uses = StoreyUsage.Of(i);
            int n = uses.Sum(u => u.buildings.Count);
            string summary = n == 0 ? "No building uses it" : $"{n} building{(n == 1 ? "" : "s")} in {uses.Count} layout{(uses.Count == 1 ? "" : "s")}";
            GUILayout.Space(6);
            using var c = StoreyInspectorUI.Card("library.used", "Used by", StoreyInspectorUI.LayoutIcon, StoreyLibrary.Tint(i.kind), summary, open: false,
                tip: i.kind == LibraryKind.Styles ? "The buildings styled with it, and not changed by hand since, or styled exactly as it is, in every layout in the project" : "The buildings that use it, in every layout in the project");
            if (!c.Open) return;
            if (n == 0) { StoreyInspectorUI.Note(i.kind == LibraryKind.Styles ? "No building in the project's layouts is styled with it, or exactly as it is." : "No building in the project's layouts uses it yet."); return; }
            foreach (var u in uses)
            {
                StoreyInspectorUI.SubHeader(u.name, u.path);
                if (u.site == null) StoreyInspectorUI.Note("Not in the open scenes: open a scene with its Storey Site to go to these.");
                foreach (var row in Rows(u.buildings, 2))
                    using (new EditorGUILayout.HorizontalScope())
                        foreach (var (id, name) in row)
                            using (new EditorGUI.DisabledScope(u.site == null))
                                if (GUILayout.Button(new GUIContent(StoreyInspectorUI.Fit(name, column / 2 - 14, EditorStyles.miniButton), name + (u.site != null ? ": select it in the Scene view" : ": its layout isn't open in a scene")), EditorStyles.miniButton, GUILayout.MaxWidth(column / 2 - 2)) && u.site != null)
                                    GoTo(u.site, id);
            }
            if (i.kind == LibraryKind.Styles && !i.package) Outdated(i, uses);
        }

        static IEnumerable<List<(string id, string name)>> Rows(List<(string id, string name)> all, int per)
        {
            for (int n = 0; n < all.Count; n += per) yield return all.Skip(n).Take(per).ToList();
        }

        /// <summary>Select a building of a site in the open scenes: the site selected, the building edited, the Scene view on it.</summary>
        static void GoTo(StoreySite site, string id)
        {
            Selection.activeGameObject = site.gameObject;
            var e = StoreyEdit.Begin(site);
            e.View.Select(id); e.View.tab = StoreyTab.Facade;
            StoreyToolContext.Show(StoreyTab.Facade);
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && StoreyToolContext.FrameBounds(site) is Bounds bounds) sv.Frame(bounds, false);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// The buildings in the open scenes styled with this preset that look as it was before it was changed: applied again,
        /// one undo step a layout, they look as it is now (each keeps its roof, as applying a preset does).
        /// </summary>
        void Outdated(LibraryItem i, IReadOnlyList<StoreyUsage.Layout> uses)
        {
            FacadeStyle now;
            try { now = i.Style!.Style; } catch (Exception) { return; }
            string key = i.Style!.Listed?.key ?? i.key, look = Styles.Look(now);
            var stale = new List<(StoreySite site, int count)>();
            foreach (var u in uses)
            {
                if (u.site == null || !(StoreyEdit.Of(u.site) is StoreyEdit e)) continue;
                int count = e.Document.buildings.Sum(b => Styles.Looks(b).Count(x => x.preset == key && x.look != look));
                if (count > 0) stale.Add((u.site, count));
            }
            int n = stale.Sum(x => x.count);
            if (n == 0) return;
            GUILayout.Space(4);
            EditorGUILayout.HelpBox($"{n} of them {(n == 1 ? "looks" : "look")} as this style was before you changed it.", MessageType.Info);
            if (!GUILayout.Button(new GUIContent($"Update {(n == 1 ? "it" : $"the {n}")} to the style as it is now", "Apply the style again to each of them, keeping their roofs: one undo step for each layout"))) return;
            foreach (var (site, _) in stale)
                StoreyEdit.Begin(site).Apply("Updated to " + i.asset.name, d =>
                {
                    bool any = false;
                    foreach (var b in d.buildings)
                        foreach (var (k, _, preset, l) in Styles.Looks(b))
                            if (preset == key && l != look) { Styles.Apply(b, k, now, key); any = true; }
                    return any;
                });
            Done($"{n} building{(n == 1 ? "" : "s")} updated to {i.title}.");
        }

        // ---- its settings ----

        void Settings(LibraryItem i)
        {
            switch (i.kind)
            {
                case LibraryKind.Styles: StyleSettings(i); break;
                case LibraryKind.Templates: TemplateSettings(i); break;
                default: AssetSettings(i); break;
            }
        }

        void StyleSettings(LibraryItem i)
        {
            var p = i.Style!;
            if (cards == null || cards.Asset != p)
            {
                cards = new PresetStyle(p);
                cards.Changed += Repaint;   // its text changed: the preview builds it again
            }
            FacadeStyle st;
            try { st = cards.Style; }
            catch (Exception ex) { EditorGUILayout.HelpBox($"It holds no style Storey can read ({ex.Message}).", MessageType.Error); return; }
            using (new EditorGUI.DisabledScope(i.package)) StoreyStyleCards.All(cards, st);
        }

        void AssetSettings(LibraryItem i)
        {
            StoreyInspectorUI.SubHeader("Settings", "Its own inspector, as the Inspector shows it");
            UnityEditor.Editor.CreateCachedEditor(i.asset, null, ref inspector);
            if (inspector == null) return;
            bool hierarchy = EditorGUIUtility.hierarchyMode;
            Embedded = true; EditorGUIUtility.hierarchyMode = true;
            try { inspector.OnInspectorGUI(); }
            finally { Embedded = false; EditorGUIUtility.hierarchyMode = hierarchy; }
        }

        void TemplateSettings(LibraryItem i)
        {
            var t = i.Template!;
            var b = t.Building;
            using (var c = StoreyInspectorUI.Card("library.template", "Building", StoreyInspectorUI.ShapeIcon, StoreyLibrary.Tint(i.kind), StoreyLibrary.FactsOf(i).line))
                if (c.Open)
                {
                    if (b == null) { StoreyInspectorUI.Note("Empty: save a building into it from a Storey Site (Save as template)."); return; }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var plan = GUILayoutUtility.GetRect(84, 84, GUILayout.Width(84), GUILayout.Height(84));
                        if (Event.current.type == EventType.Repaint)
                        {
                            EditorGUI.DrawRect(plan, StoreyPreview.Background);
                            StoreyInspectorUI.Outline(new Rect(plan.x + 6, plan.y + 6, plan.width - 12, plan.height - 12), StoreyLibrary.Tint(i.kind), b.footprint, 2f);
                        }
                        GUILayout.Space(8);
                        using (new EditorGUILayout.VerticalScope())
                        {
                            GUILayout.Label(t.Summary(), Look.Wrap);
                            GUILayout.Label("Style: " + (FacadeStylePreset.Of(b.style)?.asset.name ?? (b.style.label.Length > 0 ? b.style.label : "its own")), Look.Wrap);
                            GUILayout.Label(b.interior ? "Walk-in: floors, rooms and stairs inside" : "A shell: no interior", Look.Wrap);
                        }
                    }
                    StoreyInspectorUI.Note(i.package
                        ? "One of Storey's own: a Storey Site's New lists it first, and the Shape tab's Start from takes its outline. A building made from it takes the brick walk-up style."
                        : "Place it from a Storey Site with New ▸ Project, or with Add to the layout above. To change it, edit a building made from it and save it over this one (Save as template).");
                }
        }

        // ---- new things ----

        void NewMenu()
        {
            var m = new GenericMenu();
            var t = Target(); var cur = Picked();
            var prefabs = Selection.objects.OfType<GameObject>().Where(AssetDatabase.Contains).ToList();
            void Styles_()
            {
                m.AddItem(new GUIContent("Style"), false, () =>
                {
                    FacadeStyle from;
                    try { from = cur?.Style?.Style ?? FacadeStylePreset.Default()?.style ?? Styles.Preset(FacadeStylePreset.DefaultKey); }
                    catch (Exception) { from = Styles.Preset(FacadeStylePreset.DefaultKey); }
                    var p = StoreyLibrary.NewStyle(from, cur?.Style != null ? cur.asset.name + " variant" : "Facade Style");
                    if (p != null) { Pick(p); Say($"“{p.name}” starts as {(cur?.Style != null ? cur.title : "the brick walk-up")}: change it below."); }
                });
                if (t is { } tt)
                    m.AddItem(new GUIContent($"Style from {tt.b.name}'s"), false, () =>
                    {
                        var p = StoreyLibrary.NewStyle(Styles.Edited(tt.b, Styles.HasOwn(tt.b, tt.k0) ? tt.k0 : 0), tt.b.name);
                        if (p != null) { Pick(p); Say($"“{p.name}” holds {tt.b.name}'s style."); }
                    });
                else m.AddDisabledItem(new GUIContent("Style from a building's (pick one in a Storey Site first)"));
            }
            void Openings_(LibraryKind k)
            {
                string many = StoreyLibrary.Name(k);
                if (prefabs.Count > 0) m.AddItem(new GUIContent($"{many} from the {prefabs.Count} prefab{(prefabs.Count == 1 ? "" : "s")} selected"), false, () => Made(k, StoreyLibrary.FromPrefabs(k, prefabs)));
                else m.AddDisabledItem(new GUIContent($"{many} from prefabs (select them in the Project window, or drop them here)"));
                m.AddItem(new GUIContent($"{char.ToUpperInvariant(StoreyLibrary.One(k)[0]) + StoreyLibrary.One(k).Substring(1)} to give a prefab or a mesh"), false, () => { var o = StoreyLibrary.NewEmpty(k); if (o != null) Pick(o); });
            }
            void Templates_()
            {
                if (t is { } tt) m.AddItem(new GUIContent($"Template from {tt.b.name}"), false, () => { var tm = StoreyLibrary.TemplateFrom(tt.b); if (tm != null) Pick(tm); });
                else m.AddDisabledItem(new GUIContent("Template from a building (pick one in a Storey Site first)"));
            }
            var order = new List<LibraryKind> { kind };
            order.AddRange(StoreyLibrary.Kinds.Where(k => k != kind));
            for (int n = 0; n < order.Count; n++)
            {
                if (n > 0) m.AddSeparator("");
                switch (order[n])
                {
                    case LibraryKind.Styles: Styles_(); break;
                    case LibraryKind.Templates: Templates_(); break;
                    default: Openings_(order[n]); break;
                }
            }
            m.ShowAsContext();
        }

        void Made(LibraryKind k, List<UnityEngine.Object> made)
        {
            if (made.Count == 0) return;
            Pick(made[0]);
            Say($"Made {made.Count} {(made.Count == 1 ? StoreyLibrary.One(k) : StoreyLibrary.Name(k).ToLowerInvariant())}, each beside its prefab.");
        }

        // ---- dropped on a building in the Scene view ----

        /// <summary>
        /// A style, window, door or detail dragged from the Library (or the Project window) onto a building in the Scene view:
        /// while it hovers, the building under the pointer is outlined and named with what a drop does; dropped, it is used on
        /// that building as the Library's buttons use it (a style applied to its base, keeping its roof; its only window or
        /// door; a detail placed for you at 50 % in its style), one undo step, and that building is picked for editing.
        /// </summary>
        [InitializeOnLoadMethod]
        static void SceneDrops() => SceneView.duringSceneGui += SceneDrop;

        // what hovers over which building while a drag is on, for the outline and the label
        static (LibraryItem item, StoreySite site, string id, Vector2 at)? hovering;

        static void SceneDrop(SceneView view)
        {
            var ev = Event.current;
            if (ev.type == EventType.DragExited) { if (hovering != null) { hovering = null; view.Repaint(); } return; }
            if (ev.type == EventType.Repaint) { if (hovering is { } h) DrawHover(h.item, h.site, h.id, h.at); return; }
            if (ev.type != EventType.DragUpdated && ev.type != EventType.DragPerform) return;
            var asset = DragAndDrop.objectReferences.FirstOrDefault(o => o is FacadeStylePreset || o is StoreyOpening || o is StoreyDetail);
            var item = asset != null ? StoreyLibrary.Find(asset) : null;
            if (item == null) return;
            var hit = StoreyToolContext.SiteAt(HandleUtility.GUIPointToWorldRay(ev.mousePosition), null, double.MaxValue);
            if (hit == null || hit.Value.site.layout == null)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                hovering = null; view.Repaint(); ev.Use();
                return;
            }
            var (site, id) = hit.Value;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            hovering = (item, site, id, ev.mousePosition);
            if (ev.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                hovering = null;
                string? done = DropOn(item, site, id);
                if (done != null) view.ShowNotification(new GUIContent(done), 2.5);
            }
            view.Repaint();
            ev.Use();
        }

        /// <summary>What a drop on a building does, in words.</summary>
        static string DropWhat(LibraryItem i, string building) => i.kind switch
        {
            LibraryKind.Styles => $"Apply {i.title} to {building}",
            LibraryKind.Windows => $"{i.title}: {building}'s windows",
            LibraryKind.Doors => $"{i.title}: {building}'s doors",
            _ => DetailHasRule(i) ? $"{i.title}: placed for you on {building}, 50 %" : $"{i.title} has no rule: place it by hand from the Library",
        };

        static bool DetailHasRule(LibraryItem i) =>
            DetailKinds.Get(i.key) is DetailKind k && (Details.RuleFits(k) || i.key == DetailKinds.Ac || i.key == DetailKinds.Vent);

        static void DrawHover(LibraryItem i, StoreySite site, string id, Vector2 at)
        {
            var b = site.Shown?.buildings.FirstOrDefault(x => x.id == id);
            if (b == null) return;
            // its footprint, at its ground floor, in the item's colour
            var tint = StoreyLibrary.Tint(i.kind);
            var m = site.transform.localToWorldMatrix;
            var pts = b.footprint.Concat(new[] { b.footprint[0] }).Select(p => m.MultiplyPoint(new Vector3((float)(p.x + b.pos.x), (float)b.elevation + 0.05f, (float)(p.z + b.pos.z)))).ToArray();
            Handles.color = tint;
            Handles.DrawAAPolyLine(4f, pts);
            Handles.BeginGUI();
            var text = new GUIContent(DropWhat(i, b.name));
            var size = Look.DropLabel.CalcSize(text);
            var r = new Rect(at.x + 18, at.y + 12, size.x + 16, size.y + 8);
            EditorGUI.DrawRect(r, new Color(0.1f, 0.1f, 0.11f, 0.88f));
            EditorGUI.DrawRect(new Rect(r.x, r.y, 3, r.height), tint);
            Look.DropLabel.Draw(new Rect(r.x + 9, r.y + 4, size.x, size.y), text, false, false, false, false);
            Handles.EndGUI();
        }

        /// <summary>Use an item on a building of a site, as a drop does; returns what was done, or null.</summary>
        static string? DropOn(LibraryItem i, StoreySite site, string id)
        {
            var e = StoreyEdit.Begin(site);
            var b = e.Document.buildings.FirstOrDefault(x => x.id == id);
            if (b == null) return null;
            string name = b.name;
            switch (i.kind)
            {
                case LibraryKind.Styles:
                {
                    FacadeStyle style;
                    try { style = i.Style!.Style; } catch (Exception) { return null; }
                    string key = i.Style!.Listed?.key ?? i.key;
                    e.ApplyTo(i.asset.name + " applied", bb => { Styles.Apply(bb, 0, style, key); return true; }, id);
                    break;
                }
                case LibraryKind.Windows:
                case LibraryKind.Doors:
                {
                    var o = i.Opening!; bool door = i.kind == LibraryKind.Doors;
                    e.ApplyTo((door ? "Door: " : "Window: ") + i.title, bb =>
                    {
                        var st = bb.style;
                        if (door) { st.doorSet = null; st.doorKind = o.Id; }
                        else { st.windowSet = null; st.windowKind = o.Id; if (st.windows == WindowType.None) st.windows = WindowType.Punched; }
                        return true;
                    }, id);
                    StoreyOpenings.Keep(site, o);
                    break;
                }
                default:
                    if (!DetailHasRule(i)) return $"{i.title} has no rule: pick it in the Library and use Place it by hand.";
                    e.ApplyTo($"{i.title} 50%", bb =>
                    {
                        if (i.key == DetailKinds.Ac) (bb.style.details ??= new DetailRules()).ac = 50;
                        else if (i.key == DetailKinds.Vent) (bb.style.details ??= new DetailRules()).vents = 50;
                        else StoreyDetails.SetDensity(bb.style, i.key, 50);
                        return true;
                    }, id);
                    StoreyDetails.Keep(site, i.key);
                    break;
            }
            StoreyToolContext.Edit(site, id);   // the building picked, so its inspector shows what changed
            return DropWhat(i, name).Replace("Apply ", "Applied ");
        }

        // ---- the status bar ----

        void StatusBar(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, Look.Rail);
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), Look.Line);
            var all = StoreyLibrary.Of(kind).ToList();
            int mine = all.Count(i => !i.package);
            string left = $"{all.Count} {StoreyLibrary.Name(kind).ToLowerInvariant()}: {mine} the project's, {all.Count - mine} Storey's own";
            Look.Status.Draw(new Rect(r.x + 10, r.y + 2, r.width * 0.5f, r.height - 2), left, false, false, false, false);
            string right = thumbs != null && thumbs.Waiting > 0 ? $"Drawing pictures… {thumbs.Waiting} to go"
                : kind is LibraryKind.Windows or LibraryKind.Doors or LibraryKind.Details ? "Drop prefabs on the list to make more"
                : "Double-click an asset in the Project window to open it here";
            var size = Look.Status.CalcSize(new GUIContent(right));
            Look.Status.Draw(new Rect(r.xMax - size.x - 10, r.y + 2, size.x, r.height - 2), right, false, false, false, false);
        }

        // ---- splitters ----

        static readonly int SplitHint = "StoreyLibrarySplit".GetHashCode();

        void SplitPanel(Rect r)
        {
            int id = GUIUtility.GetControlID(SplitHint, FocusType.Passive);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal, id);
            var ev = Event.current;
            switch (ev.GetTypeForControl(id))
            {
                case EventType.MouseDown when r.Contains(ev.mousePosition) && ev.button == 0: GUIUtility.hotControl = id; ev.Use(); break;
                case EventType.MouseDrag when GUIUtility.hotControl == id: PanelWidth = Mathf.Clamp(PanelWidth - ev.delta.x, 340, Math.Max(340, position.width - RailWidth - 300)); ev.Use(); Repaint(); break;
                case EventType.MouseUp when GUIUtility.hotControl == id: GUIUtility.hotControl = 0; ev.Use(); break;

            }
        }

        void SplitPreview(Rect r)
        {
            int id = GUIUtility.GetControlID(SplitHint + 1, FocusType.Passive);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical, id);
            var ev = Event.current;
            switch (ev.GetTypeForControl(id))
            {
                case EventType.MouseDown when r.Contains(ev.mousePosition) && ev.button == 0: GUIUtility.hotControl = id; ev.Use(); break;
                case EventType.MouseDrag when GUIUtility.hotControl == id: PreviewHeight = Mathf.Clamp(PreviewHeight + ev.delta.y, 170, Math.Max(170, position.height - 200)); ev.Use(); Repaint(); break;
                case EventType.MouseUp when GUIUtility.hotControl == id: GUIUtility.hotControl = 0; ev.Use(); break;
                case EventType.Repaint: EditorGUI.DrawRect(new Rect(r.x, r.center.y, r.width, 1), Look.Line); break;
            }
        }

        // ---- drawing helpers ----

        static Color Mix(Color a, Color b, float t) => new Color(Mathf.Lerp(a.r, b.r, t), Mathf.Lerp(a.g, b.g, t), Mathf.Lerp(a.b, b.b, t), 1);

        static void Frame(Rect r, Color c, float w)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, w), c); EditorGUI.DrawRect(new Rect(r.x, r.yMax - w, r.width, w), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, w, r.height), c); EditorGUI.DrawRect(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        /// <summary>A small tag over a picture, from <paramref name="r"/>'s left (or its right edge, <paramref name="right"/>); returns its far edge.</summary>
        static float Chip(Rect r, string text, Action<Rect, Color>? icon, Color back, bool right = false)
        {
            float tw = Look.ChipText.CalcSize(new GUIContent(text)).x, w = tw + (icon != null ? 18 : 8);
            var c = new Rect(right ? r.x - w : r.x, r.y, w, r.height);
            EditorGUI.DrawRect(c, back);
            icon?.Invoke(new Rect(c.x + 4, c.y + 3, 10, 10), new Color(1, 1, 1, 0.85f));
            Look.ChipText.Draw(new Rect(c.x + (icon != null ? 16 : 4), c.y, tw + 4, c.height), text, false, false, false, false);
            return right ? c.x : c.xMax;
        }

        /// <summary>A tag in a row of the header, in its colour.</summary>
        static void Chip(string text, Color colour, Action<Rect, Color>? icon)
        {
            var content = new GUIContent(text);
            float tw = Look.HeaderChip.CalcSize(content).x;
            var r = GUILayoutUtility.GetRect(tw + (icon != null ? 20 : 10), 18, GUILayout.Width(tw + (icon != null ? 20 : 10)), GUILayout.Height(18));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(r, new Color(colour.r, colour.g, colour.b, 0.18f));
                icon?.Invoke(new Rect(r.x + 5, r.y + 4, 10, 10), colour);
                var keep = Look.HeaderChip.normal.textColor; Look.HeaderChip.normal.textColor = colour;
                Look.HeaderChip.Draw(new Rect(r.x + (icon != null ? 16 : 5), r.y, tw + 4, r.height), content, false, false, false, false);
                Look.HeaderChip.normal.textColor = keep;
            }
            GUILayout.Space(4);
        }

        static bool IconButton(Action<Rect, Color> icon, string tip)
        {
            var r = GUILayoutUtility.GetRect(22, 22, GUILayout.Width(22), GUILayout.Height(22));
            bool clicked = GUI.Button(r, new GUIContent("", tip), GUIStyle.none);
            if (Event.current.type == EventType.Repaint)
            {
                if (r.Contains(Event.current.mousePosition) && GUI.enabled) EditorGUI.DrawRect(r, Look.Hover);
                icon(new Rect(r.x + 4, r.y + 4, 14, 14), GUI.enabled ? StoreyInspectorUI.Muted : new Color(0.5f, 0.5f, 0.5f, 0.4f));
            }
            return clicked;
        }

        /// <summary>The window's colours and text styles, for either editor skin.</summary>
        static class Look
        {
            static bool Dark => EditorGUIUtility.isProSkin;
            public static Color Rail => Dark ? new Color(0.17f, 0.17f, 0.17f) : new Color(0.79f, 0.79f, 0.79f);
            public static Color Panel => Dark ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.8f, 0.8f, 0.8f);
            public static Color Card => Dark ? new Color(0.25f, 0.25f, 0.25f) : new Color(0.87f, 0.87f, 0.87f);
            public static Color CardHover => Dark ? new Color(0.29f, 0.29f, 0.29f) : new Color(0.92f, 0.92f, 0.92f);
            public static Color Hover => Dark ? new Color(1, 1, 1, 0.06f) : new Color(0, 0, 0, 0.06f);
            public static Color Line => StoreyInspectorUI.Rule;
            public static Color Pill => Dark ? new Color(1, 1, 1, 0.08f) : new Color(0, 0, 0, 0.08f);
            public static Color ChipBack => new Color(0.08f, 0.08f, 0.09f, 0.72f);
            public static Color Amber => Dark ? new Color(0.98f, 0.74f, 0.3f) : new Color(0.8f, 0.5f, 0.05f);

            static GUIStyle? heading, railOn, railOff, count, title, muted, wrap, family, cardTitle, cardLine, chipText, headerChip, name, path, centred, centredMini, dropText, overlay, status, renameField;
            public static GUIStyle Heading => heading ??= new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = StoreyInspectorUI.Muted } };
            public static GUIStyle RailOn => railOn ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            public static GUIStyle RailOff => railOff ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
            public static GUIStyle Count => count ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = StoreyInspectorUI.Ink } };
            public static GUIStyle Title => title ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 };
            public static GUIStyle Muted => muted ??= new GUIStyle(EditorStyles.label) { normal = { textColor = StoreyInspectorUI.Muted } };
            public static GUIStyle Wrap => wrap ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = StoreyInspectorUI.Muted } };
            public static GUIStyle FamilyName => family ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, normal = { textColor = StoreyInspectorUI.Ink } };
            public static GUIStyle CardTitle => cardTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 11, clipping = TextClipping.Clip };
            public static GUIStyle CardLine => cardLine ??= new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip, normal = { textColor = StoreyInspectorUI.Muted } };
            public static GUIStyle ChipText => chipText ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1, 1, 1, 0.92f) } };
            public static GUIStyle HeaderChip => headerChip ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
            public static GUIStyle Name => name ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, wordWrap = true };
            public static GUIStyle Path => path ??= new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip, normal = { textColor = StoreyInspectorUI.Muted } };
            public static GUIStyle Centred => centred ??= new GUIStyle(EditorStyles.label) { alignment = TextAnchor.UpperCenter, wordWrap = true, normal = { textColor = StoreyInspectorUI.Muted } };
            public static GUIStyle CentredMini => centredMini ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            public static GUIStyle DropText => dropText ??= new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = true };
            public static GUIStyle Overlay => overlay ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1, 1, 1, 0.85f) } };
            public static GUIStyle Status => status ??= new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = StoreyInspectorUI.Muted } };
            public static GUIStyle RenameField => renameField ??= new GUIStyle(EditorStyles.textField) { fontSize = 13 };
            static GUIStyle? useButton, dropLabel;
            public static GUIStyle UseButton => useButton ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 };
            public static GUIStyle DropLabel => dropLabel ??= new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.95f, 0.95f, 0.95f) } };
        }
    }
}
