#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// What the style cards edit (docs/EDITOR.md §6.10): one style, read and changed through here, each change one named
    /// undo step. A building's in a layout being edited (the site inspector's Facade tab), or a Facade Style asset (the
    /// Storey Library and the asset's own inspector, §6.13). The site's own tools, placing doors and details by hand on its
    /// walls, come in through the virtual members, and draw nothing where there are no walls.
    /// </summary>
    internal abstract class StyleTarget
    {
        /// <summary>The style as it stands: the cards draw it, and an edit changes it in place.</summary>
        public abstract FacadeStyle Style { get; }

        /// <summary>The base's style: its ground floor, doors, plinth and foundation are set on it. A Facade Style asset is one.</summary>
        public virtual bool Base => true;

        /// <summary>Names this style's colour fields, for a picker that answers later (Color Pipeline's).</summary>
        public abstract string Key { get; }

        /// <summary>
        /// What the cards' folds are remembered under, before each card's own name: the site inspector's are as they always
        /// were; the Library's are its own, so folding a card there leaves the inspector's open.
        /// </summary>
        public virtual string Cards => "";

        /// <summary>One change to the style, one undo step named <paramref name="undoName"/>.</summary>
        public abstract void Edit(string undoName, Action<FacadeStyle> op);

        /// <summary>A palette id's entry where the palette doesn't know it (a layout's palette block); none by default.</summary>
        public virtual PaletteEntry? Lookup(string id) => null;

        /// <summary>An artist's window or door the style now picks, kept where a build will find it (the site's list).</summary>
        public virtual void Keep(StoreyOpening o) { }

        /// <summary>An artist's facade detail the style now gives a chance, kept where a build will find it (the site's list).</summary>
        public virtual void KeepDetail(string id) { }

        /// <summary>A line to show after an edit: what it did, or why it didn't.</summary>
        public virtual void Say(string? message) { }

        /// <summary>The roof card is the roof on top of the building, over floors below a setback that shares this style.</summary>
        public virtual bool RoofOnTop => false;

        /// <summary>A line of its own at the top of the roof card, or null.</summary>
        public virtual string? RoofNote => null;

        /// <summary>After the doors card's summary, how many doors the building has (" · 2 placed"); nothing without a building.</summary>
        public virtual string DoorsPlaced => "";

        /// <summary>The doors card's On the walls: the site's Place doors tool. Nothing without walls to place them on.</summary>
        public virtual void DoorsOnWalls(FacadeStyle st) { }

        /// <summary>How many details the building has placed by hand, for the details card's summary.</summary>
        public virtual int DetailsByHand => 0;

        /// <summary>The details card's Place by hand: the site's tools. Nothing without walls to place them on.</summary>
        public virtual void PlaceByHand() { }
    }

    /// <summary>
    /// The Facade tab's style cards (docs/EDITOR.md §6.10): Windows, Doors, Walls, Colours, Roof and Details, each with its
    /// icon, its colour and a line of what is set, drawn for any <see cref="StyleTarget"/>: the site inspector's building,
    /// and the Storey Library's Facade Style assets (§6.13), so a style is edited the same way wherever it is.
    /// </summary>
    internal static class StoreyStyleCards
    {
        /// <summary>Every card a whole style has, in the Facade tab's order: what a Facade Style asset shows.</summary>
        public static void All(StyleTarget t, FacadeStyle st)
        {
            WindowsCard(t, st);
            if (t.Base) DoorsCard(t, st);
            WallsCard(t, st);
            ColoursCard(t, st);
            RoofCard(t, st);
            DetailsCard(t, st);
        }

        // ---- Windows ----

        public static void WindowsCard(StyleTarget t, FacadeStyle st)
        {
            OpeningSets.Uses(st, out bool generated, out _, out bool anyOwnSize);
            bool ownSize = !generated && anyOwnSize;
            string summary = st.windows == WindowType.None ? "None"
                : $"{(ownSize ? "Own size" : OpeningName(st.windows))} · bays of {st.bay:0.##} m";
            using var c = StoreyInspectorUI.Card(t.Cards + "windows", "Windows", StoreyInspectorUI.WindowFrontIcon, StoreyInspectorUI.Tint.Windows, summary,
                tip: "What fills the walls' openings, how they're spaced, and how they sit in the wall");
            if (!c.Open) return;
            SetRows(t, st, SetOf.Windows, "Window");
            Variety(t, st);

            StoreyInspectorUI.SubHeader("Size and spacing");
            Openings(t, st, ownSize, anyOwnSize);
            // Window width sizes Punched and Tall holes alone: Ribbon and Curtain span the bay, and a window of its own size is its own width
            if (!ownSize && st.windows is WindowType.Punched or WindowType.Tall)
                StyleSlider(t, "Window width", st.winW, 0.6, 2.4, 0.1, (s, x) => s.winW = x);
            if (st.windows != WindowType.None)
            {
                BayFits(t, st);
                StyleSlider(t, "Bay width", st.bay, 1.4, 5, 0.1, (s, x) => s.bay = x);
            }

            if (t.Base)
            {
                StoreyInspectorUI.SubHeader("Ground floor");
                int g = EditorGUILayout.Popup(GroundLabel, (int)st.ground, GroundChoices);
                if (g != (int)st.ground) t.Edit("Ground floor", s => s.ground = (GroundType)g);
                GroundRows(t, st);
            }

            StoreyInspectorUI.SubHeader("Depth in the wall");
            Recess(t, st, "Windows", "Windows sit back from the wall's face, this deep: the built-in window's glass and frame, or yours pushed back that far. Off: the built-in window sits 16.5 cm in, and yours at the wall's face",
                st.recess, 0.25, (s, x) => s.recess = x);

            if (generated)
            {
                StoreyInspectorUI.SubHeader("Built-in window");
                WindowDetails(t, st);
            }
            else StoreyInspectorUI.Note("Your window brings its own frame, sill and glazing bars.");
        }

        static readonly GUIContent GroundLabel = new GUIContent("Layout", "What the ground floor's walls have");
        static readonly GUIContent[] GroundChoices =
        {
            new GUIContent("Shopfront", "Wide shop windows along the street"),
            new GUIContent("Like the floors above", "The same openings as the floors above"),
            new GUIContent("No windows", "A solid ground floor; doors you place stay"),
        };

        static readonly GUIContent VarietyLabel = new GUIContent("Variety",
            "How the windows are picked from the lists, by their weights. Per window: each on its own. Per column: a column the same all the way up. " +
            "Per storey: a storey of a wall the same along it. Per wall: each wall one window. Same pattern everywhere: every building with this style " +
            "gets the same windows in the same places, picked by wall, storey and bay, and the same door on each wall (otherwise each wall's doors are picked by the wall)");
        static readonly GUIContent[] VarietyChoices =
        {
            new GUIContent("Per window"), new GUIContent("Per column"), new GUIContent("Per storey"), new GUIContent("Per wall"), new GUIContent("Same pattern everywhere"),
        };

        /// <summary>How the windows are picked from the style's lists (<see cref="FacadeStyle.windowPick"/>), where any list has a choice to make.</summary>
        static void Variety(StyleTarget t, FacadeStyle st)
        {
            if (!(st.windowSet is { Count: > 1 } || st.groundSet is { Count: > 1 } || st.doorSet is { Count: > 1 })) return;
            int x = EditorGUILayout.Popup(VarietyLabel, (int)st.windowPick, VarietyChoices);
            if (x != (int)st.windowPick) t.Edit("Variety: " + VarietyChoices[x].text, s => s.windowPick = (WindowPick)x);
        }

        /// <summary>The built-in window's details (docs/EDITOR.md §6.7): sills, heads, glazing bars and frames. A window of your own brings its own.</summary>
        static void WindowDetails(StyleTarget t, FacadeStyle st)
        {
            StyleToggle(t, "Sills", st.sills, (s, x) => s.sills = x);
            StyleToggle(t, "Heads", st.heads, (s, x) => s.heads = x);
            if (st.heads) using (new EditorGUI.IndentLevelScope()) StyleEnum(t, "Head shape", st.head, (s, x) => s.head = x);
            bool panes = st.paneCols.HasValue || st.paneRows.HasValue;
            bool p = EditorGUILayout.Toggle(new GUIContent("Glazing bars", "Divide every window into panes"), panes);
            if (p != panes) t.Edit(p ? "Glazing bars" : "No glazing bars", s => { s.paneCols = p ? 2 : (int?)null; s.paneRows = p ? 3 : (int?)null; });
            if (panes)
                using (new EditorGUI.IndentLevelScope())
                {
                    StyleSlider(t, "Panes across", st.paneCols ?? 1, 1, 6, 1, (s, x) => s.paneCols = (int)x);
                    StyleSlider(t, "Panes up", st.paneRows ?? 1, 1, 6, 1, (s, x) => s.paneRows = (int)x);
                }
            StyleToggle(t, "Frames", st.frames, (s, x) => s.frames = x);
        }

        static readonly GUIContent SolidBackLabel = new GUIContent("Solid back",
            "Windows and doors of your own that are set into the wall get a back in the wall's colour at their depth: whatever of them is behind it " +
            "is hidden, and round a hole bigger than what fills it the wall shows. One setting for windows and doors, on buildings without interiors");

        /// <summary>
        /// Windows or doors set into the wall (docs/EDITOR.md §6.7, <see cref="FacadeStyle.recess"/>, <see cref="FacadeStyle.doorRecess"/>):
        /// off, the built-in ones sit at their usual depth and yours at the wall's face; on, how deep, and whether the hole has a
        /// solid back (<see cref="FacadeStyle.recessBack"/>).
        /// </summary>
        static void Recess(StyleTarget t, FacadeStyle st, string what, string tip, double? value, double max, Action<FacadeStyle, double?> set)
        {
            bool on = StoreyInspectorUI.Feature("Set into the wall", tip, value.HasValue);
            if (on != value.HasValue) t.Edit(on ? what + " set into the wall" : what + " at their usual depth", s => set(s, on ? OpeningKinds.ShellReveal : (double?)null));
            if (!(value is double v)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                StyleSlider(t, new GUIContent("Depth", "How far in from the wall's face, in metres"), v, 0, max, 0.01, (s, x) => set(s, x));
                StyleToggle(t, SolidBackLabel, st.recessBack, (s, x) => s.recessBack = x);
            }
        }

        static readonly GUIContent BaysLabel = new GUIContent("Bays", "How the windows are spaced along each wall: each bay has one window");
        static readonly GUIContent[] BayFitChoices =
        {
            new GUIContent("Fill the wall", "As many bays as Bay width rounds to, stretched to fill each wall from corner to corner"),
            new GUIContent("Fixed width", "As many whole bays of exactly Bay width as fit on each wall, centred on it; a window that keeps its own size keeps it however narrow the bay"),
        };

        /// <summary>How the style's bays are laid out (<see cref="FacadeStyle.bayFit"/>, docs/EDITOR.md §6.7): spread to fill each wall, or fixed at Bay width and centred.</summary>
        static void BayFits(StyleTarget t, FacadeStyle st)
        {
            int x = StoreyInspectorUI.Segmented(BaysLabel, (int)st.bayFit, BayFitChoices);
            if (x != (int)st.bayFit) t.Edit(x == (int)BayFit.Fixed ? "Bays of a fixed width" : "Bays filling the wall", s => s.bayFit = (BayFit)x);
        }

        static readonly GUIContent OpeningsLabel = new GUIContent("Openings",
            "The holes in each storey's walls, whatever window fills them (the ground floor's follow Ground floor). " +
            "Standard: up to 1.35 m high over a 90 cm sill, Window width wide. Tall: from 40 cm up to half a metre under the storey's top, Window width wide. " +
            "Ribbon: a strip across each bay. Glass wall: glass across each bay, floor to ceiling. None: blank walls.");
        /// <summary>The window types (<see cref="WindowType"/>) as the inspector names them.</summary>
        static readonly GUIContent[] OpeningChoices = { new GUIContent("Standard"), new GUIContent("Tall"), new GUIContent("Ribbon"), new GUIContent("Glass wall"), new GUIContent("None") };
        /// <summary>A window type as the inspector names it.</summary>
        public static string OpeningName(WindowType t) => OpeningChoices[(int)t].text;
        static readonly GUIContent OwnSizeLabel = new GUIContent("Openings", "Your window keeps its own size, so each hole is as big as it is. None: blank walls.");
        static readonly GUIContent[] OwnSizeChoices = { new GUIContent("As big as the window"), new GUIContent("None") };

        /// <summary>
        /// The style's openings (its window type, docs/EDITOR.md §6.9): where each storey's windows are and how big their
        /// holes are. A window of your own that keeps its own size leaves two things to you: whether there are any, and how
        /// high they sit (<see cref="Sill"/>).
        /// </summary>
        /// <param name="sill">Some of the windows (not all: <paramref name="ownSize"/>) keep their own size: they take a height too.</param>
        static void Openings(StyleTarget t, FacadeStyle st, bool ownSize, bool sill)
        {
            if (!ownSize)
            {
                int x = EditorGUILayout.Popup(OpeningsLabel, (int)st.windows, OpeningChoices);
                if (x != (int)st.windows) t.Edit("Openings: " + OpeningChoices[x].text, s => s.windows = (WindowType)x);
                if (sill && st.windows != WindowType.None) using (new EditorGUI.IndentLevelScope()) Sill(t, st, OwnHeightLabel);
                return;
            }
            int was = st.windows == WindowType.None ? 1 : 0, pick = EditorGUILayout.Popup(OwnSizeLabel, was, OwnSizeChoices);
            if (pick != was) t.Edit(pick == 1 ? "No windows" : "Windows", s => s.windows = pick == 1 ? WindowType.None : WindowType.Punched);
            if (was == 0) Sill(t, st, HeightLabel);
        }

        static readonly GUIContent HeightLabel = new GUIContent("Height", "Where a window that keeps its own size sits in each storey: halfway up (the ground storey's in its own height), or at a sill height over the floor");
        static readonly GUIContent OwnHeightLabel = new GUIContent("Your windows", "Where your windows that keep their own size sit in each storey: halfway up (the ground storey's in its own height), or at a sill height over the floor");
        static readonly GUIContent[] HeightChoices = { new GUIContent("Centred", "Halfway up each storey"), new GUIContent("Sill height", "This high over each floor") };

        /// <summary>
        /// Where a window that keeps its own size sits (docs/EDITOR.md §6.9): centred in its storey
        /// (<see cref="FacadeStyle.winCentre"/>), or at <see cref="FacadeStyle.sillH"/>, whose slider shows while it isn't.
        /// </summary>
        static void Sill(StyleTarget t, FacadeStyle st, GUIContent label)
        {
            int cur = st.winCentre ? 0 : 1, x = StoreyInspectorUI.Segmented(label, cur, HeightChoices);
            if (x != cur) t.Edit(x == 0 ? "Windows centred in storey" : "Windows at sill height", s => s.winCentre = x == 0);
            if (!st.winCentre)
                using (new EditorGUI.IndentLevelScope())
                    StyleSlider(t, "Sill height", st.sillH ?? Facade.SillOf(st.windows), 0, 2, 0.05, (s, y) => s.sillH = y);
        }

        /// <summary>
        /// A style's windows (or street doors, or the ground floor's own windows): the built-in one, ones you made (Storey
        /// Window or Door assets: docs/EDITOR.md §6.9), or none (no window there), each picked as often as its weight
        /// (docs/OPENINGS.md §7.1). One entry is the style's one window; with two or more, each has a weight, its share, and
        /// a ×. Picking one of yours keeps it where a build will find it (<see cref="StyleTarget.Keep"/>).
        /// </summary>
        static void SetRows(StyleTarget t, FacadeStyle st, SetOf which, string label)
        {
            // none: the ground floor follows the floors above's (its own may have gone earlier in this pass of the cards)
            if (!(WindowSets.Shown(st, which) is { } set)) return;
            bool door = which == SetOf.Doors, many = set.Count > 1;
            var kinds = StoreyOpenings.All().Where(o => o.door == door).ToList();
            var names = new List<GUIContent> { new GUIContent("Built-in") };
            if (!door) names.Add(new GUIContent("None", "No window: the wall is left whole there"));
            int first = names.Count;
            // by the words that tell them apart (Visual_BuildingPiece08_Door: 08 Door): the rows' popups are narrow
            var shortName = StoreyInspectorUI.ShortNames(kinds.Select(o => o.name).ToList());
            names.AddRange(kinds.Select(o => new GUIContent(shortName.TryGetValue(o.name, out var sn) ? sn : o.name, o.name)));
            if (kinds.Count == 0) names.Add(new GUIContent("(make one: Create ▸ Storey ▸ Window or Door)"));
            double total = 0; for (int i = 0; i < set.Count; i++) total += OpeningSets.Weight(set, i);
            var tip = new GUIContent(label, door
                ? "The doors: the built-in one (a flat panel set into the wall, in the door colour), or doors you made (Create ▸ Storey ▸ Window or Door). With several, Door to place says which a click puts in; a door placed as Any of them is one picked for its wall by its weight"
                : "What fills each opening: the built-in window, windows you made (Create ▸ Storey ▸ Window or Door), or none. With several, each window is one of them, picked by its weight, the same on every build and different from building to building");
            for (int i = 0; i < set.Count; i++)
            {
                var en = set[i]; int at = i;
                using (new EditorGUILayout.HorizontalScope())
                {
                    var shown = new List<GUIContent>(names);
                    int cur = en.none && !door ? 1 : en.kind == null ? 0 : kinds.FindIndex(o => o.Id == en.kind) + first;
                    if (en.kind != null && !en.none && cur == first - 1) { shown.Add(new GUIContent("Missing: " + en.kind.Substring(0, Math.Min(8, en.kind.Length)))); cur = shown.Count - 1; }
                    int pick = EditorGUILayout.Popup(i == 0 ? tip : new GUIContent(" "), cur, shown.ToArray());
                    if (pick != cur && pick < first + kinds.Count)
                    {
                        bool none = !door && pick == 1; var k = pick >= first ? kinds[pick - first] : null; string? id = k?.Id;
                        t.Edit(label + ": " + shown[pick].text, s => WindowSets.Set(s, which, at, id, none));
                        if (k != null) t.Keep(k);
                    }
                    if (many)
                    {
                        EditorGUI.BeginChangeCheck();
                        double w = Math.Max(0, EditorGUILayout.DelayedFloatField((float)en.weight, GUILayout.Width(36)));
                        if (EditorGUI.EndChangeCheck() && w != en.weight) t.Edit(label + " weight", s => WindowSets.Weigh(s, which, at, w));
                        EditorGUILayout.LabelField(new GUIContent(total > 0 ? $"{OpeningSets.Weight(set, i) / total * 100:0}%" : "", "Its share: its weight over all of theirs"), StoreyInspectorUI.Caption, GUILayout.Width(30));
                    }
                    if (door && (many || en.recess.HasValue)) DoorRecess(t, en, at, label);
                    if (many && GUILayout.Button(new GUIContent("×", "Remove it from the list"), EditorStyles.miniButton, GUILayout.Width(22)))
                        t.Edit(label + " removed", s => WindowSets.Remove(s, which, at));
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                if (GUILayout.Button(new GUIContent(door ? "+ Another door" : "+ Another window", door ? "Add a door: each wall's doors are picked from the list, by weight" : "Add a window (or none): each opening is picked from the list, by weight"), EditorStyles.miniButton))
                    t.Edit(label + " added", s => WindowSets.Add(s, which, null));
            }
        }

        static readonly GUIContent DoorRecessLabel = new GUIContent("depth",
            "How deep this door sits in the wall, in metres from its face (0 to 0.29), in place of the style's; empty: the style's");

        /// <summary>
        /// A street door entry's own depth in the wall (docs/EDITOR.md §6.7, <see cref="OpeningEntry.recess"/>), in metres:
        /// empty, the style's <see cref="FacadeStyle.doorRecess"/>. One undo step, as every style edit.
        /// </summary>
        static void DoorRecess(StyleTarget t, OpeningEntry en, int at, string label)
        {
            EditorGUILayout.LabelField(DoorRecessLabel, StoreyInspectorUI.Caption, GUILayout.Width(36));
            string was = en.recess is double r ? r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
            string x = EditorGUILayout.DelayedTextField(was, GUILayout.Width(40)).Trim();
            if (x == was) return;
            double? to = x.Length == 0 ? (double?)null
                : double.TryParse(x.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : en.recess;
            if (to != en.recess) t.Edit(label + " depth", s => WindowSets.Recess(s, SetOf.Doors, at, to));
        }

        /// <summary>The ground floor's windows: the floors above's, or a list of its own (docs/OPENINGS.md §7.1). A ground floor without windows has none.</summary>
        static void GroundRows(StyleTarget t, FacadeStyle st)
        {
            if (st.ground == GroundType.Solid) return;
            bool own = st.groundSet is { Count: > 0 };
            bool x = EditorGUILayout.Toggle(new GUIContent("Own windows", "The ground floor's openings take windows of their own, not the floors above's"), own);
            if (x != own) t.Edit(x ? "Ground floor windows of its own" : "Ground floor windows as above", s => { if (x) WindowSets.Own(s, SetOf.Ground); else WindowSets.FollowAbove(s); });
            // as the style is now: the edit above changes it in place, so a list just let go of isn't there to draw
            if (st.groundSet is { Count: > 0 }) SetRows(t, st, SetOf.Ground, "Ground window");
        }

        // ---- Doors ----

        public static void DoorsCard(StyleTarget t, FacadeStyle st)
        {
            string summary = (st.doorSet is { Count: > 1 } ? $"{st.doorSet.Count} kinds" : st.doorKind != null ? "Your door" : "Built-in")
                + t.DoorsPlaced + (st.doorRecess is double dr ? $" · set in {dr * 100:0} cm" : "");
            using var c = StoreyInspectorUI.Card(t.Cards + "doors", "Doors", StoreyInspectorUI.DoorFrontIcon, StoreyInspectorUI.Tint.Doors, summary,
                tip: "The building's doors: which door its style has (or several), and where they are on its walls");
            if (!c.Open) return;
            SetRows(t, st, SetOf.Doors, "Door");
            Colour(t, "Colour", "door", optional: true);
            t.DoorsOnWalls(st);
            StoreyInspectorUI.SubHeader("Depth in the wall");
            Recess(t, st, "Doors", "Doors sit back from the wall's face, this deep: the built-in door's panel, or yours pushed back that far; a set-in door's threshold takes the foundation's colour. Off: the built-in door sits at its usual depth, and yours at the wall's face",
                st.doorRecess, 0.29, (s, x) => s.doorRecess = x);
            if (!st.doorRecess.HasValue || !OwnDoors(st)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                StyleSlider(t, OpeningMarginLabel, st.doorRecessOffset ?? 0, -0.5, 1, 0.01, (s, x) => s.doorRecessOffset = Math.Abs(x) < 1e-4 ? (double?)null : x);
                StoreyInspectorUI.Note((st.doorRecessOffset ?? 0) < 0 ? "The opening is smaller than the door: its frame stays hidden in the wall."
                    : (st.doorRecessOffset ?? 0) > 0 ? "The opening is bigger than the door: the wall shows round it." : "The opening is the door's own size.");
            }
        }

        /// <summary>Whether any street door the style can pick is one you made: only those keep their own size, so only their holes take a margin.</summary>
        static bool OwnDoors(FacadeStyle st) => st.doorKind != null || (st.doorSet?.Any(x => !x.none && x.kind != null) ?? false);

        static readonly GUIContent OpeningMarginLabel = new GUIContent("Opening margin",
            "How much bigger the hole is than your door, each side and above, in metres; below 0 it is smaller and the door's frame stays hidden in the wall. For doors that keep their own size, on buildings without interiors");

        // ---- Walls ----

        public static void WallsCard(StyleTarget t, FacadeStyle st)
        {
            var on = new List<string>();
            if (st.corniceH.HasValue) on.Add("cornice");
            if (st.bands) on.Add("bands");
            if (t.Base && (st.plinthH ?? 0.45) > 0) on.Add("plinth");
            if (t.Base && st.foundation.HasValue) on.Add("foundation");
            if (st.bricks > 0) on.Add("bricks");
            string summary = on.Count == 0 ? "Plain" : char.ToUpperInvariant(on[0][0]) + string.Join(" · ", on).Substring(1);
            using var c = StoreyInspectorUI.Card(t.Cards + "walls", "Walls", StoreyInspectorUI.BricksIcon, StoreyInspectorUI.Tint.Walls, summary,
                tip: "The strips along the walls, top to bottom, and brick patches");
            if (!c.Open) return;
            WallDetails(t, st);
        }

        static readonly GUIContent StandsOutLabel = new GUIContent("Stands out", "How far it stands out from the wall's face, in metres");

        /// <summary>
        /// The style's wall strips (docs/EDITOR.md §6.7), top to bottom as they stand on the wall: the cornice, bands, and on
        /// the base the plinth and foundation; brick patches on any. Each one's settings and colour are under it while it's on.
        /// </summary>
        static void WallDetails(StyleTarget t, FacadeStyle st)
        {
            bool cn = StoreyInspectorUI.Feature("Cornice", "A strip round the top of the walls, under the roof (and under a terrace's parapet where a setback starts above); the top storey's windows stay under it", st.corniceH.HasValue);
            if (cn != st.corniceH.HasValue) t.Edit(cn ? "Cornice" : "No cornice", s => s.corniceH = cn ? 0.3 : (double?)null);
            if (st.corniceH is double ch)
                using (new EditorGUI.IndentLevelScope())
                {
                    StyleSlider(t, "Height", ch, 0.05, 2, 0.01, (s, x) => s.corniceH = x);
                    StyleSlider(t, StandsOutLabel, st.corniceDepth ?? 0.1, 0.01, 1, 0.01, (s, x) => s.corniceDepth = x);
                    Colour(t, "Colour", "cornice", optional: true);
                }
            bool bd = StoreyInspectorUI.Feature("Floor bands", "A strip along the walls under each storey's ceiling", st.bands);
            if (bd != st.bands) t.Edit(bd ? "Floor bands" : "No floor bands", s => s.bands = bd);
            if (st.bands)
                using (new EditorGUI.IndentLevelScope())
                {
                    if (t.Base) StyleToggle(t, new GUIContent("Ground floor only", "One band, over the ground floor; the storeys above have none"), st.bandGround, (s, x) => s.bandGround = x);
                    StyleSlider(t, "Height", st.bandH ?? 0.22, 0.05, 1.5, 0.01, (s, x) => s.bandH = x);
                    StyleSlider(t, StandsOutLabel, st.bandDepth ?? 0.06, 0.01, 0.2, 0.01, (s, x) => s.bandDepth = x);
                    Colour(t, "Colour", "band", optional: true);
                    if (st.band == null) StyleToggle(t, new GUIContent("Shade of the wall", "With no colour of its own, the band is a shade of the wall's colour, not the trim's"), st.bandWall, (s, x) => s.bandWall = x);
                }
            if (t.Base)
            {
                double ph = st.plinthH ?? 0.45;
                bool pl = StoreyInspectorUI.Feature("Plinth", "A strip along the foot of the ground floor's walls", ph > 0);
                if (pl != ph > 0) t.Edit(pl ? "Plinth" : "No plinth", s => s.plinthH = pl ? (double?)null : 0);
                if (ph > 0)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        StyleSlider(t, "Height", ph, 0.05, 2, 0.05, (s, x) => s.plinthH = x);
                        StyleSlider(t, StandsOutLabel, st.plinthDepth ?? 0.04, 0.01, 1, 0.01, (s, x) => s.plinthDepth = x);
                        Colour(t, "Colour", "plinth", optional: true);
                    }
                bool fd = StoreyInspectorUI.Feature("Foundation", "A foundation along the foot of the ground floor, reaching down into the ground", st.foundation.HasValue);
                if (fd != st.foundation.HasValue) t.Edit(fd ? "Foundation" : "No foundation", s => s.foundation = fd ? 0.5 : (double?)null);
                if (st.foundation is double depth)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        StyleSlider(t, "Below ground", depth, 0, 5, 0.05, (s, x) => s.foundation = x);
                        StyleSlider(t, "Above ground", st.foundationH ?? 0.2, 0.02, 1.5, 0.01, (s, x) => s.foundationH = x);
                        StyleSlider(t, StandsOutLabel, st.foundationOut ?? 0.08, 0.01, 1, 0.01, (s, x) => s.foundationOut = x);
                        Colour(t, "Colour", "foundationColor", optional: true);
                    }
            }
            bool br = StoreyInspectorUI.Feature("Brick patches", "Patches of brick scattered over the walls", (st.bricks ?? 0) > 0);
            if (br != (st.bricks ?? 0) > 0) t.Edit(br ? "Brick patches" : "No brick patches", s => s.bricks = br ? 0.3 : (double?)null);
            if (st.bricks is double bk && bk > 0)
                using (new EditorGUI.IndentLevelScope()) StyleSlider(t, "Amount", bk, 0.05, 1, 0.05, (s, x) => s.bricks = x);
        }

        // ---- Colours ----

        public static void ColoursCard(StyleTarget t, FacadeStyle st)
        {
            using var c = StoreyInspectorUI.Card(t.Cards + "colours", "Colours", StoreyInspectorUI.PaletteIcon, StoreyInspectorUI.Tint.Colours, "From the project's palette");
            if (!c.Open) return;
            Colour(t, "Walls", "wall");
            if (t.Base) Colour(t, "Ground floor walls", "groundWall", optional: true);
            Colour(t, "Trim", "trim");
            Colour(t, "Roof", "roof");
            Colour(t, "Glass", "glass");
            bool more;
            using (new EditorGUI.IndentLevelScope()) more = StoreyInspectorUI.Fold(t.Cards + "colours", "More colours", false, "Frames, metal, the inside, details: each follows the project's default until you set it");
            if (more)
                using (new EditorGUI.IndentLevelScope())
                {
                    Colour(t, "Stairs and lifts", "core");
                    // the base's doors, plinth and foundation have theirs with them (Doors, Walls); a set-in door's threshold
                    // takes the foundation's colour, so it stays here while there's no foundation
                    if (!t.Base) Colour(t, "Doors", "door", optional: true);
                    if (t.Base && !st.foundation.HasValue) Colour(t, "Foundation", "foundationColor", optional: true);
                    foreach (var f in MoreColours) Colour(t, f.label, f.field, optional: true);
                    DefaultsNotice(t);
                }
        }

        static readonly (string label, string field)[] MoreColours =
        {
            ("Frames", "frame"), ("Rails", "rail"), ("Metal", "metal"), ("Ceilings", "ceiling"), ("Lift inside", "liftInterior"), ("Lift button", "liftButton"),
            ("Detail metal", "detailMetal"), ("Grilles", "grille"), ("Detail dark", "detailDark"), ("Dishes", "dish"),
        };

        /// <summary>
        /// One of the style's colours (<see cref="StyleColorFields"/>), with the project's colour picker; an optional one shows
        /// the colour it follows while it has none of its own, and Default takes its own away.
        /// </summary>
        public static void Colour(StyleTarget t, string label, string field, bool optional = false)
        {
            var spec = StyleColorFields.All.First(f => f.name == field);
            var st = t.Style;
            string? value = spec.get(st);
            string shown = value ?? StyleColors.Of(st, SlotOf(field), StoreyColorField.Defaults?.Invoke() ?? new StyleDefaults()) ?? "#FF00FF";
            using (new EditorGUILayout.HorizontalScope())
            {
                string? picked = StoreyColorField.Picker.Draw($"{t.Key}/{field}", optional && value == null ? label + " (default)" : label, shown, field, t.Lookup);
                if (picked != null && picked != value) t.Edit(label + " colour", s => spec.set(s, picked));
                if (optional && value != null && GUILayout.Button(new GUIContent("Default", "Use the project's default colour, or the colour it follows"), EditorStyles.miniButton, GUILayout.Width(56)))
                    t.Edit(label + " colour", s => spec.set(s, null!));
            }
        }

        /// <summary>
        /// The project leaves some defaults unset, so they are Storey's own colours, shown as their nearest palette
        /// entries: offer to store those entries as the project's defaults.
        /// </summary>
        static void DefaultsNotice(StyleTarget t)
        {
            if (StoreyColorField.FillDefaults == null) return;
            var d = StoreyColorField.Defaults?.Invoke() ?? new StyleDefaults();
            if (!new[] { d.door, d.rail, d.metal, d.ceiling, d.liftInterior, d.liftButton, d.detailMetal, d.grille, d.detailDark, d.dish, d.frame, d.foundation, d.plinth }.Any(x => x != null && x.StartsWith("#"))) return;
            EditorGUILayout.HelpBox("Some default colours aren't set for this project, so they show the palette colour nearest to Storey's own. Store those palette colours as the project's defaults (in Storey Color Settings) to pick them yourself.", MessageType.None);
            if (GUILayout.Button("Store the defaults in Storey Color Settings"))
            {
                int n = StoreyColorField.FillDefaults();
                t.Say(n > 0 ? $"{n} default colour{(n == 1 ? "" : "s")} stored in Storey Color Settings." : null);
            }
        }

        /// <summary>The colour slot a style's colour field names.</summary>
        public static ColorSlot SlotOf(string field) => field switch
        {
            "wall" => ColorSlot.Wall, "trim" => ColorSlot.Trim, "interior" => ColorSlot.Interior, "floor" => ColorSlot.Floor, "roof" => ColorSlot.Roof,
            "core" => ColorSlot.Core, "glass" => ColorSlot.Glass, "door" => ColorSlot.Door, "rail" => ColorSlot.Rail, "metal" => ColorSlot.Metal,
            "ceiling" => ColorSlot.Ceiling, "liftInterior" => ColorSlot.LiftInterior, "liftButton" => ColorSlot.LiftButton, "detailMetal" => ColorSlot.DetailMetal,
            "grille" => ColorSlot.Grille, "detailDark" => ColorSlot.DetailDark, "frame" => ColorSlot.Frame, "foundationColor" => ColorSlot.Foundation, "plinth" => ColorSlot.Plinth,
            "groundWall" => ColorSlot.GroundWall, "band" => ColorSlot.Band, "cornice" => ColorSlot.Cornice, "cap" => ColorSlot.Cap, "capRim" => ColorSlot.CapRim, _ => ColorSlot.Dish,
        };

        // ---- Roof ----

        /// <summary>What the roof card says of a style's roof, folded or not.</summary>
        public static string RoofSummary(FacadeStyle st) =>
            st.roofType != RoofType.Flat ? $"{st.roofType}, {st.pitch ?? (st.roofType == RoofType.Shed ? 15 : st.roofType == RoofType.Mansard ? 20 : 30):0}°" + (st.roofSurface != null ? " · " + SurfaceName(st) : "")
            : "Flat" + (st.parapet ? " · parapet" : "") + (Generate.RoofCap.Of(st) is Generate.RoofCap rc ? rc.inset ? " · edge set in" : rc.fascia ? " · hanging edge" : " · slab edge" : "");

        public static void RoofCard(StyleTarget t, FacadeStyle st)
        {
            // from floors under a setback that shares their style (which has its own Terrace roof card), the roof on top
            bool below = t.RoofOnTop;
            using var c = StoreyInspectorUI.Card(t.Cards + "roof", below ? "Roof on top" : "Roof", StoreyInspectorUI.RoofIcon, StoreyInspectorUI.Tint.Roof, RoofSummary(st),
                tip: below ? "The roof on top of the building: the floors above these share their style" : null);
            if (!c.Open) return;
            if (t.RoofNote is string note) StoreyInspectorUI.Note(note);
            StyleEnum(t, "Type", st.roofType, (s, x) => s.roofType = x);
            if (st.roofType != RoofType.Flat)
            {
                bool mans = st.roofType == RoofType.Mansard;
                StyleSlider(t, mans ? "Top pitch" : "Pitch", st.pitch ?? (st.roofType == RoofType.Shed ? 15 : mans ? 20 : 30), 5, 60, 1, (s, x) => s.pitch = x);
                StyleSlider(t, "Eaves", st.eave ?? 0.35, 0, 1.2, 0.05, (s, x) => s.eave = x);
                if (mans) StyleSlider(t, "Steep part", st.mansard ?? 2.4, 0.5, 6, 0.1, (s, x) => s.mansard = x);
                bool dm = StoreyInspectorUI.Feature("Dormers", "Dormer windows along every eave, wherever they fit on the slope", st.dormers.HasValue);
                if (dm != st.dormers.HasValue) t.Edit(dm ? "Dormers added" : "Dormers removed", s => s.dormers = dm ? 3.5 : (double?)null);
                if (st.dormers is double every) using (new EditorGUI.IndentLevelScope()) StyleSlider(t, "Every", every, 2.2, 10, 0.1, (s, x) => s.dormers = x);
                if (mans) StoreyInspectorUI.Note("A mansard: a steep 70° slope from the eaves to the break, then a shallow hip at the top pitch.");
                RoofSurfaceFields(t, st);
                return;
            }
            bool pp = StoreyInspectorUI.Feature("Parapet", "A low wall round the roof's edge", st.parapet);
            if (pp != st.parapet) t.Edit(pp ? "Parapet" : "No parapet", s => s.parapet = pp);
            RoofEdge(t, st);
        }

        /// <summary>A roof surface's pattern as the roof card names it ("Plain" without one).</summary>
        public static string SurfaceName(FacadeStyle st) => st.roofSurface is RoofSurface rs ? RoofSurfaces[(int)rs.pattern].text : "Plain";

        static readonly GUIContent RoofSurfaceLabel = new GUIContent("Pattern", "Drawn on the slopes by the shader, in the roof's colour, so it adds no triangles: clay tiles, slates, shingles or a metal roof's seams");
        static readonly GUIContent[] RoofSurfaces =
        {
            new GUIContent("Plain", "One colour"),
            new GUIContent("Tiles", "Clay tiles: rounded, in columns up the slope"),
            new GUIContent("Slate", "Flat slates, each row half a slate along"),
            new GUIContent("Shingles", "Shingles with rounded feet, each row half a shingle along"),
            new GUIContent("Seams", "A metal roof: a standing seam up the slope at each panel's edge"),
        };

        /// <summary>
        /// A pitched roof's surface (docs/EDITOR.md §6.2): its pattern, a tile's size, its relief and each tile's own shade.
        /// A slider left alone keeps the pattern's own number, so the next pattern picked brings its own.
        /// </summary>
        public static void RoofSurfaceFields(StyleTarget t, FacadeStyle st, string title = "Surface")
        {
            StoreyInspectorUI.SubHeader(title, RoofSurfaceLabel.tooltip);
            var rs = st.roofSurface;
            int cur = rs != null ? (int)rs.pattern : 0, pick = StoreyInspectorUI.Segmented(RoofSurfaceLabel, cur, RoofSurfaces);
            if (pick != cur) t.Edit("Roof surface", s =>
            {
                if (pick == 0) s.roofSurface = null;
                else { s.roofSurface ??= new RoofSurface(); s.roofSurface.pattern = (RoofPattern)pick; s.roofSurface.w = s.roofSurface.h = null; }
            });
            if (rs == null) return;
            var (_, w, h, relief, variation) = ColorRows.RoofSurface(st);
            bool seams = rs.pattern == RoofPattern.Seams;
            StyleSlider(t, new GUIContent(seams ? "Panel width" : "Tile width", "Along the eave, in metres"), w, 0.05, 2, 0.01, (s, v) => s.roofSurface!.w = v);
            if (!seams) StyleSlider(t, new GUIContent("Tile height", "Up the slope: how much of each tile shows, in metres"), h, 0.05, 2, 0.01, (s, v) => s.roofSurface!.h = v);
            StyleSlider(t, new GUIContent("Relief", "How strongly the light picks out each tile"), relief, 0, 1, 0.05, (s, v) => s.roofSurface!.relief = v);
            StyleSlider(t, new GUIContent("Variation", "How much each tile's shade differs from the next"), variation, 0, 1, 0.05, (s, v) => s.roofSurface!.variation = v);
        }

        static readonly GUIContent EdgeKindLabel = new GUIContent("Kind", "What the roof's edge is");
        static readonly GUIContent[] EdgeKinds =
        {
            new GUIContent("Slab", "A slab on top of the walls, standing out over them: it takes the curb's place, and a parapet stands on it"),
            new GUIContent("Hanging", "A fascia hanging down from the roof's edge, out past the walls, under the curb or parapet"),
            new GUIContent("Set in", "A raised ring on the roof, set in from its edge, cut round the bulkheads and openings"),
        };

        static readonly GUIContent OverhangLabel = new GUIContent("Overhang", "How far it stands out past the walls' face");

        /// <summary>
        /// A flat roof's edge (docs/EDITOR.md §6.7, <see cref="FacadeStyle.capH"/>, <see cref="Generate.RoofCap"/>): a slab, a
        /// hanging fascia or a ring set in, each set by a few numbers that are all positive, which the style's signed ones
        /// follow (a fascia's height is below 0, an overhang's depth below 0); the strip round its top, and its colours.
        /// </summary>
        static void RoofEdge(StyleTarget t, FacadeStyle st)
        {
            bool on = StoreyInspectorUI.Feature("Roof edge", "A cap round the roof's edge: a slab standing out over the walls, a fascia hanging from it, or a ring set in on the roof", st.capH.HasValue);
            if (on != st.capH.HasValue) t.Edit(on ? "Roof edge" : "No roof edge", s => { s.capH = on ? 0.4 : (double?)null; if (on && !s.capDepth.HasValue) s.capDepth = -0.25; });
            if (!(st.capH is double h)) return;
            double d = st.capDepth ?? 0;
            int kind = d > 1e-3 ? 2 : h >= 0 ? 0 : 1;
            using var _ = new EditorGUI.IndentLevelScope();
            int pick = StoreyInspectorUI.Segmented(EdgeKindLabel, kind, EdgeKinds);
            if (pick != kind) t.Edit("Roof edge: " + EdgeKinds[pick].text, s =>
            {
                double size = Math.Abs(s.capH ?? 0.4) < 0.02 ? 0.4 : Math.Abs(s.capH ?? 0.4), dd = s.capDepth ?? 0;
                s.capH = pick == 1 ? -size : size;
                s.capDepth = pick == 2 ? (dd > 1e-3 ? dd : 0.5) : pick == 1 ? (dd < -0.01 ? dd : -0.25) : (dd > 1e-3 ? -0.25 : dd);
            });
            switch (kind)
            {
                case 0:
                    StyleSlider(t, "Height", h, 0, 4, 0.01, (s, x) => s.capH = x);
                    StyleSlider(t, OverhangLabel, -d, 0, 1, 0.01, (s, x) => s.capDepth = x > 0 ? -x : 0);
                    break;
                case 1:
                    StyleSlider(t, new GUIContent("Hangs down", "How far it hangs below the roof's edge"), -h, 0.02, 1.5, 0.01, (s, x) => s.capH = -x);
                    StyleSlider(t, OverhangLabel, -d, 0.02, 1, 0.01, (s, x) => s.capDepth = -x);
                    break;
                default:
                    StyleSlider(t, "Height", h, 0, 4, 0.01, (s, x) => s.capH = x);
                    StyleSlider(t, new GUIContent("In from the edge", "How far in from the walls' face it stands"), d, 0.01, 5, 0.01, (s, x) => s.capDepth = x);
                    break;
            }
            StyleSlider(t, kind == 2 ? new GUIContent("Thickness", "How thick the ring is; 0: as thick as the wall")
                : new GUIContent("Top strip", "A strip of the edge's colour round its top, this wide from its outer side; 0: its top is the roof's colour"),
                st.capStrip ?? 0, 0, 2, 0.01, (s, x) => s.capStrip = x > 0 ? x : (double?)null);
            Colour(t, "Colour", "cap", optional: true);
            if (d < 0) Colour(t, "Underside", "capRim", optional: true);
            if (!(Generate.RoofCap.Of(st) is Generate.RoofCap)) StoreyInspectorUI.Note("A hanging edge needs an overhang.");
        }

        // ---- Details ----

        /// <summary>How many details a style places by itself: AC units and vents with a chance, and every artist's detail it gives one.</summary>
        public static int PlacedForYou(FacadeStyle st)
        {
            var (ac, vents) = Details.Rules(st);
            return (ac > 0 ? 1 : 0) + (vents > 0 ? 1 : 0) + (st.details?.kinds?.Count(kv => Details.Density(st, kv.Key) > 0) ?? 0);
        }

        /// <summary>
        /// The details card: Placed for you (the style's chances) where there is a style of its own (<paramref name="st"/>),
        /// then the target's Place by hand, where it has walls to place them on.
        /// </summary>
        public static void DetailsCard(StyleTarget t, FacadeStyle? st)
        {
            int auto = st != null ? PlacedForYou(st) : 0, byHand = t.DetailsByHand;
            string summary = auto == 0 && byHand == 0 ? "None"
                : string.Join(" · ", new[] { auto > 0 ? $"{auto} placed for you" : null, byHand > 0 ? $"{byHand} by hand" : null }.Where(x => x != null));
            using var c = StoreyInspectorUI.Card(t.Cards + "details", "Details", StoreyInspectorUI.DetailsIcon, StoreyInspectorUI.Tint.Details, summary,
                tip: "Things on the walls: some placed for you by chance, others placed by hand");
            if (!c.Open) return;
            if (st != null)
            {
                StoreyInspectorUI.SubHeader("Placed for you", "Scattered over the walls by chance, the same on every rebuild; 0% leaves one out");
                RuleDetails(t, st);
            }
            t.PlaceByHand();
        }

        /// <summary>
        /// The style's automatic details (docs/EDITOR.md §6.12): the AC units' and vents' chances, then each of the project's
        /// Facade Details with a rule, and any the style names that the project no longer has. Giving one a chance keeps it
        /// where a build will find it (<see cref="StyleTarget.KeepDetail"/>).
        /// </summary>
        static void RuleDetails(StyleTarget t, FacadeStyle st)
        {
            var (ac, vents) = Details.Rules(st);
            StyleSlider(t, "AC units %", Math.Round(ac * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).ac = x);
            StyleSlider(t, "Vents %", Math.Round(vents * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).vents = x);
            var shown = new HashSet<string>(StringComparer.Ordinal);
            var all = StoreyDetails.All();
            var shortName = StoreyInspectorUI.ShortNames(all.Where(d => !d.BuiltIn).Select(d => d.name).ToList());
            foreach (var d in all)
            {
                if (d.BuiltIn || !Details.RuleFits(d.anchor, d.rule)) continue;
                shown.Add(d.Id);
                RuleSlider(t, st, new GUIContent(shortName.TryGetValue(d.name, out var sn) ? sn : d.name, d.name), d.Id);
                if (Details.Density(st, d.Id) > 0)
                    using (new EditorGUI.IndentLevelScope()) RuleSettings(t, st, d.Id, d.rule);
            }
            if (st.details?.kinds != null)
                foreach (var id in st.details.kinds.Keys.Where(id => !shown.Contains(id)).ToList()) RuleSlider(t, st, new GUIContent(StoreyDetails.Name(id), "Not in the project any more"), id);
            if (shown.Count == 0) StoreyInspectorUI.Note("A Facade Detail of your own with a placement rule gets a slider here (Create ▸ Storey ▸ Facade Detail).");
        }

        /// <summary>An artist's detail's chance in the style; giving it one keeps the detail where a build will find it.</summary>
        static void RuleSlider(StyleTarget t, FacadeStyle st, GUIContent name, string id)
        {
            double was = Math.Round(Details.Density(st, id) * 100);
            EditorGUI.BeginChangeCheck();
            double x = Math.Round(EditorGUILayout.Slider(new GUIContent(name.text + " %", name.tooltip), (float)was, 0, 100) / 5) * 5;
            if (!EditorGUI.EndChangeCheck() || x == was) return;
            t.Edit(name.text + " %", s => StoreyDetails.SetDensity(s, id, x));
            if (x > 0) t.KeepDetail(id);
        }

        static readonly GUIContent[] FloorNames = { new GUIContent("All"), new GUIContent("Upper", "Not on the ground floor"), new GUIContent("Ground", "On the ground floor alone") };
        static readonly GUIContent[] CornerNames = { new GUIContent("Outward", "The corners that stand out"), new GUIContent("Inward", "The corners set in"), new GUIContent("All") };

        /// <summary>
        /// A detail's settings in the style beside its chance (docs/EDITOR.md §6.12, <see cref="KindRule"/>), as its rule takes
        /// them: how many to the square metre and the gap round each, scattered; how many to a bay, in a row; how far apart and
        /// which corners, up the corners; which storeys; and the colour its main colour takes in this style.
        /// </summary>
        static void RuleSettings(StyleTarget t, FacadeStyle st, string id, Details.Rule rule)
        {
            var r = Details.RuleOf(st, id);
            void Set(string label, Action<KindRule> f) => t.Edit(label, s => StoreyDetails.EditRule(s, id, f));
            void Number(string label, double value, double min, double max, double step, Action<KindRule, double> f)
            {
                EditorGUI.BeginChangeCheck();
                double x = Math.Round(EditorGUILayout.Slider(label, (float)value, (float)min, (float)max) / step) * step;
                if (EditorGUI.EndChangeCheck() && Math.Abs(x - value) > 1e-9) Set(label, kr => f(kr, Math.Round(x, 3)));
            }
            switch (rule)
            {
                case Details.Rule.Scatter:
                {
                    var (density, gap) = Details.ScatterOf(r);
                    Number("Per m²", density, 0, 4, 0.05, (kr, x) => kr.density = x);
                    Number("Gap", gap, 0, 1, 0.01, (kr, x) => kr.spacing = x);
                    break;
                }
                case Details.Rule.AlongTop:
                case Details.Rule.AlongFoot:
                    Number("Per bay", Details.PerBay(r), 1, 8, 1, (kr, x) => kr.per = (int)x);
                    break;
                case Details.Rule.Corners:
                {
                    Number("Every", Details.CornerStep(r), 0.1, 3, 0.05, (kr, x) => kr.spacing = x);
                    int c = EditorGUILayout.Popup(new GUIContent("Corners", "Which corners it goes up"), (int)(r?.corners ?? DetailCorners.Outward), CornerNames);
                    if (c != (int)(r?.corners ?? DetailCorners.Outward)) Set("Corners", kr => kr.corners = (DetailCorners)c);
                    break;
                }
            }
            if (rule == Details.Rule.Scatter || rule == Details.Rule.Corners)
            {
                int f = EditorGUILayout.Popup(new GUIContent("Storeys", "Which storeys it goes on"), (int)(r?.floors ?? DetailFloors.All), FloorNames);
                if (f != (int)(r?.floors ?? DetailFloors.All)) Set("Storeys", kr => kr.floors = (DetailFloors)f);
            }
            bool own = r?.color != null;
            bool on = EditorGUILayout.Toggle(new GUIContent("Recolour", "Give its main colour (the one most of its triangles have) another in this style; off, it keeps its own"), own);
            if (on != own) Set(on ? "Detail colour" : "Detail's own colour", kr => kr.color = on ? MainColour(st, id) : null);
            if (r?.color is string colour)
                using (new EditorGUI.IndentLevelScope())
                {
                    string? picked = StoreyColorField.Picker.Draw($"{t.Key}/detail/{id}", "Main colour", colour, "detail", t.Lookup);
                    if (picked != null && picked != colour) Set("Detail colour", kr => kr.color = picked);
                }
        }

        /// <summary>What a detail's main colour is now, in this style: where a colour of its own starts.</summary>
        static string MainColour(FacadeStyle st, string id)
        {
            var main = DetailKinds.Get(id)?.main;
            if (main == null) return "#808080";
            return KindColors.RefOf(main.Value) ?? StyleColors.Of(st, main.Value, StoreyColorField.Defaults?.Invoke() ?? new StyleDefaults()) ?? "#808080";
        }

        // ---- fields ----

        public static void StyleSlider(StyleTarget t, string label, double value, double min, double max, double step, Action<FacadeStyle, double> set) =>
            StyleSlider(t, new GUIContent(label), value, min, max, step, set);

        public static void StyleSlider(StyleTarget t, GUIContent label, double value, double min, double max, double step, Action<FacadeStyle, double> set)
        {
            EditorGUI.BeginChangeCheck();
            double x = Math.Round(EditorGUILayout.Slider(label, (float)value, (float)min, (float)max) / step) * step;
            if (EditorGUI.EndChangeCheck() && x != value) t.Edit(label.text, s => set(s, Math.Round(x, 3)));
        }

        public static void StyleToggle(StyleTarget t, string label, bool value, Action<FacadeStyle, bool> set) => StyleToggle(t, new GUIContent(label), value, set);

        public static void StyleToggle(StyleTarget t, GUIContent label, bool value, Action<FacadeStyle, bool> set)
        {
            bool x = EditorGUILayout.Toggle(label, value);
            if (x != value) t.Edit(label.text, s => set(s, x));
        }

        public static void StyleEnum<T>(StyleTarget t, string label, T value, Action<FacadeStyle, T> set) where T : Enum
        {
            var x = (T)EditorGUILayout.EnumPopup(label, value);
            if (!x.Equals(value)) t.Edit(label, s => set(s, x));
        }
    }
}
