#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The site's inspector (docs/EDITOR.md slice 6.4; SPEC §9): the building bar (pick, new from a footprint, duplicate,
    /// delete, rename, isolate) and the Shape, Facade and Interior tabs, which also switch the Scene view's tool. What the
    /// prototype's panel holds, field for field; Save, Revert and Stop editing at the bottom.
    /// </summary>
    [CustomEditor(typeof(StoreySite))]
    internal sealed class StoreySiteEditor : UnityEditor.Editor
    {
        static readonly string[] TabNames = { "Shape", "Facade", "Interior" };
        static readonly string[] ShapeKeys = { "rect", "L", "U", "T", "oct" }, ShapeLabels = { "Rect", "L", "U", "T", "Octa" };
        static readonly (string tool, string label)[] FacadeTools = { ("entrance", "Entrance"), ("blank", "Blank wall"), ("ac", "AC unit"), ("vent", "Vent"), ("dish", "Dish"), ("escape", "Fire escape"), ("awning", "Awning") };
        static readonly string[] InteriorTools = { "Select", "Wall", "Door", "Erase", "Stairs", "Lift" };
        string? message;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var site = (StoreySite)target;
            if (site.layout == null) return;
            EditorGUILayout.Space();
            var e = StoreyEdit.Of(site);
            if (e == null)
            {
                if (GUILayout.Button("Edit layout")) { var ne = StoreyEdit.Begin(site); StoreyToolContext.Show(ne.View.tab); }
                return;
            }

            var tr = site.transform;
            if (tr.rotation != Quaternion.identity || tr.lossyScale != Vector3.one)
                EditorGUILayout.HelpBox("Keep the Storey Site unrotated and unscaled: the layout is in metres, and the Interior tab's floor clip and cutaway assume it. Moving it is fine.", MessageType.Warning);
            BuildingBar(e);
            var b = e.Selected;
            if (b != null)
            {
                EditorGUILayout.Space();
                var v = e.View;
                int tab = GUILayout.Toolbar((int)v.tab, TabNames);
                if (tab != (int)v.tab) SetTab(v, (StoreyTab)tab);
                if (!StoreyToolActive() && GUILayout.Button("Show the Storey handles in the Scene view")) SetTab(v, v.tab);
                if (message != null) EditorGUILayout.HelpBox(message, MessageType.Info);
                switch (v.tab)
                {
                    case StoreyTab.Shape: ShapeTab(e, b); break;
                    case StoreyTab.Facade: FacadeTab(e, b); break;
                    default: InteriorTab(e, b); break;
                }
            }
            EditorGUILayout.Space();
            SaveBar(e);
        }

        static bool StoreyToolActive() => typeof(StoreyTool).IsAssignableFrom(ToolManager.activeToolType);

        static void SetTab(StoreyEditView v, StoreyTab t)
        {
            v.tab = t;
            StoreyToolContext.Show(t);
        }

        // ---- building bar ----

        void BuildingBar(StoreyEdit e)
        {
            var doc = e.Document; var v = e.View;
            using (new EditorGUILayout.HorizontalScope())
            {
                int cur = doc.buildings.FindIndex(x => x.id == v.selectedId);
                int sel = EditorGUILayout.Popup(cur, doc.buildings.Select(x => $"{x.name}  ({x.floors.Count} fl)").ToArray());
                if (sel != cur && sel >= 0) { v.Select(doc.buildings[sel].id); SceneView.RepaintAll(); }
                if (GUILayout.Button("New", EditorStyles.miniButton, GUILayout.Width(40)))
                {
                    var menu = new GenericMenu();
                    for (int i = 0; i < ShapeKeys.Length; i++)
                    {
                        string key = ShapeKeys[i];
                        menu.AddItem(new GUIContent(ShapeLabels[i]), false, () =>
                        {
                            string id = "";
                            var near = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                            e.Apply("Added a 3-storey building", d => { id = Buildings.Add(d, key, new Vec2(near.x, near.z)).id; return true; });
                            e.View.Select(id); SetTab(e.View, StoreyTab.Shape);
                        });
                    }
                    menu.ShowAsContext();
                }
                var b = e.Selected;
                using (new EditorGUI.DisabledScope(b == null))
                {
                    if (GUILayout.Button("Duplicate", EditorStyles.miniButton, GUILayout.Width(64)) && b != null)
                    {
                        string id = ""; string src = b.id;
                        var near = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                        e.Apply("Duplicated " + b.name, d => { id = Buildings.Duplicate(d, d.buildings.First(x => x.id == src), new Vec2(near.x, near.z)).id; return true; });
                        e.View.Select(id);
                    }
                    if (GUILayout.Button("Delete", EditorStyles.miniButton, GUILayout.Width(48)) && b != null)
                    {
                        string id = b.id; e.Apply(b.name + " deleted", d => Buildings.Delete(d, id));
                        e.View.selectedId = e.Document.buildings.FirstOrDefault()?.id ?? "";
                    }
                }
            }
            var sb = e.Selected; if (sb == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                string name = EditorGUILayout.DelayedTextField("Name", sb.name);
                if (name != sb.name && name.Trim().Length > 0) e.ApplyTo("Renamed to " + name, bb => { bb.name = name.Trim(); return true; });
                bool iso = GUILayout.Toggle(v.isolate, new GUIContent("Isolate", "Hide every other building"), EditorStyles.miniButton, GUILayout.Width(56));
                if (iso != v.isolate) { v.isolate = iso; SceneView.RepaintAll(); }
            }
        }

        // ---- Shape ----

        void ShapeTab(StoreyEdit e, BuildingData b)
        {
            var v = e.View; int k0 = v.tier; var ts = Tiers.Of(b); var cur = ts.First(t => t.k0 == k0);
            EditorGUILayout.LabelField("Outline by floor", EditorStyles.boldLabel);
            foreach (var t in ts.AsEnumerable().Reverse())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool on = GUILayout.Toggle(t.k0 == k0, $"{(t.k0 > 0 ? "Setback" : "Base")}  {Range(b, t)}", EditorStyles.miniButton);
                    if (on && t.k0 != k0) { v.tier = t.k0; SceneView.RepaintAll(); }
                    if (t.k0 > 0 && GUILayout.Button(new GUIContent("×", "Remove this setback"), EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        int kk = t.k0; e.ApplyTo($"Setback removed: {Floor(b, kk)} and up follow the outline below", bb => { Setbacks.Remove(bb, kk); return true; });
                        v.tier = 0;
                    }
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(cur.k1 - cur.k0 < 2))
                    if (GUILayout.Button(new GUIContent("Add setback", "The floors from the middle of this outline up get their own outline")))
                    {
                        int k = -1; e.ApplyTo("Setback added", bb => { k = Setbacks.Add(bb, k0); return true; });
                        if (k > 0) { v.tier = k; message = $"{Floor(b, k)} and up now have their own outline: push an edge in to make a terrace."; }
                    }
                if (k0 > 0 && GUILayout.Button(new GUIContent("Inset 1.5 m", "Move every edge 1.5 m in")))
                {
                    var why = OutlineIssue.None;
                    e.ApplyTo("Inset 1.5 m", bb => (why = Outlines.Set(bb, k0, Outlines.Inset(Tiers.Outline(bb, k0), 1.5))) == OutlineIssue.None);
                    message = why == OutlineIssue.None ? null : Outlines.Why(why);
                }
            }
            if (k0 > 0)
            {
                int lo = ts.First(t => t.k1 == k0).k0;
                var opts = Enumerable.Range(lo + 1, cur.k1 - lo - 1).ToList();
                int at = EditorGUILayout.Popup("Starts at", opts.IndexOf(k0), opts.Select(j => Floor(b, j)).ToArray());
                if (at >= 0 && opts[at] != k0)
                {
                    int to = opts[at];
                    if (e.ApplyTo("Setback now starts at " + Floor(b, to), bb => Setbacks.Move(bb, k0, to))) v.tier = to;
                    else message = "Stairs or a lift don’t fit the setback there";
                }
                if (Geo.TerracePolys(Derived.OutlineAt(b, k0 - 1), Derived.OutlineAt(b, k0)).Count > 0)
                {
                    var tr = b.floors[k0].terraceRoof;
                    int top = GUILayout.Toolbar(tr != null ? 1 : 0, new[] { "Terrace", "Roof" });
                    if ((top == 1) != (tr != null)) e.ApplyTo(top == 1 ? "The terrace below is now a roof" : "The roof below is a terrace again", bb => { Facades.SetTerraceRoof(bb, k0, top == 1); return true; });
                    if (tr != null)
                    {
                        EditorGUI.BeginChangeCheck();
                        float pitch = EditorGUILayout.Slider("Pitch", (float)tr.pitch, 10, 50);
                        if (EditorGUI.EndChangeCheck()) e.ApplyTo("Roof pitch", bb => { bb.floors[k0].terraceRoof!.pitch = Math.Round(pitch); return true; });
                    }
                }
                EditorGUILayout.HelpBox("Drag corners in the Scene view, or drag an edge's orange bar to push that wall in. " + (b.floors[k0].terraceRoof != null ? "The roof left uncovered below runs up against the walls above." : "The roof left uncovered below becomes a terrace: add a door onto it with Facade ▸ Entrance."), MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Drag the corners in the Scene view. Drag a + to add a corner, double-click a corner to remove it, drag an orange bar to push a wall. Alt: no snapping.", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Start from");
                    for (int i = 0; i < ShapeKeys.Length; i++)
                        if (GUILayout.Button(ShapeLabels[i], EditorStyles.miniButton))
                        {
                            int had = Tiers.Of(b).Count; string key = ShapeKeys[i];
                            e.ApplyTo("Footprint replaced", bb => { Setbacks.ApplyPreset(bb, key); return true; });
                            message = e.Selected != null && Tiers.Of(e.Selected).Count < had ? "Setbacks that no longer fit were removed." : null;
                        }
                }
            }
            EditorGUILayout.LabelField("Storey height", EditorStyles.boldLabel);
            Slider(e, "Ground", b.groundHeight, 3, 6, 0.1, (bb, x) => bb.groundHeight = x);
            Slider(e, "Upper", b.floorHeight, 2.7, 4.5, 0.1, (bb, x) => bb.floorHeight = x);
            EditorGUILayout.LabelField($"{Math.Abs(Geo.Area2(Tiers.Outline(b, k0))) / 2:0} m² per floor · {Derived.RoofY(b):0.0} m tall", EditorStyles.miniLabel);
        }

        // ---- Facade ----

        void FacadeTab(StoreyEdit e, BuildingData b)
        {
            var v = e.View; var ts = Tiers.Of(b); int k0 = ts.Count > 1 ? v.tier : 0;
            if (ts.Count > 1)
            {
                int sel = GUILayout.Toolbar(ts.FindIndex(t => t.k0 == k0), ts.Select(t => $"{(t.k0 > 0 ? "Setback" : "Base")} {Range(b, t)}").ToArray());
                if (sel >= 0 && ts[sel].k0 != k0) { v.tier = ts[sel].k0; k0 = v.tier; }
            }
            bool own = Styles.HasOwn(b, k0);
            if (!own)
            {
                EditorGUILayout.HelpBox("This setback looks like the floors below it.", MessageType.None);
                if (GUILayout.Button("Give it its own style")) e.ApplyTo("The setback has its own style now", bb => { Styles.GiveOwn(bb, k0); return true; });
            }
            else
            {
                var st = Styles.Edited(b, k0);
                if (k0 > 0 && GUILayout.Button(new GUIContent("Match the floors below", "Drop this setback's style and follow the floors below again"))) e.ApplyTo("The setback matches the floors below again", bb => { Styles.MatchBelow(bb, k0); return true; });
                Presets(e, b, k0, st);
                EditorGUILayout.LabelField("Windows", EditorStyles.boldLabel);
                StyleEnum(e, k0, "Windows", st.windows, (s, x) => s.windows = x);
                EditorGUILayout.LabelField("Colours", EditorStyles.boldLabel);
                Colour(e, b, k0, "Walls", "wall");
                Colour(e, b, k0, "Trim", "trim");
                Colour(e, b, k0, "Roof", "roof");
                bool drives = Styles.DrivesRoof(b, k0);
                if (drives)
                {
                    EditorGUILayout.LabelField("Roof", EditorStyles.boldLabel);
                    StyleEnum(e, k0, "Type", st.roofType, (s, x) => s.roofType = x);
                    if (st.roofType != RoofType.Flat)
                    {
                        StyleSlider(e, k0, "Pitch", st.pitch ?? (st.roofType == RoofType.Shed ? 15 : 30), 5, 60, 1, (s, x) => s.pitch = x);
                        StyleSlider(e, k0, "Eaves", st.eave ?? 0.35, 0, 1.2, 0.05, (s, x) => s.eave = x);
                    }
                }
                EditorGUILayout.LabelField("Details by rule", EditorStyles.boldLabel);
                var (ac, vents) = Details.Rules(st);
                StyleSlider(e, k0, "AC units %", Math.Round(ac * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).ac = x);
                StyleSlider(e, k0, "Vents %", Math.Round(vents * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).vents = x);
                v.showMore = EditorGUILayout.Foldout(v.showMore, "More options", true);
                if (v.showMore)
                {
                    StyleSlider(e, k0, "Window width", st.winW, 0.6, 2.4, 0.1, (s, x) => s.winW = x);
                    StyleSlider(e, k0, "Bay spacing", st.bay, 1.4, 5, 0.1, (s, x) => s.bay = x);
                    if (k0 == 0) StyleEnum(e, k0, "Ground floor", st.ground, (s, x) => s.ground = x);
                    StyleToggle(e, k0, "Floor bands", st.bands, (s, x) => s.bands = x);
                    if (drives && st.roofType == RoofType.Flat) StyleToggle(e, k0, "Roof parapet", st.parapet, (s, x) => s.parapet = x);
                    Colour(e, b, k0, "Glass", "glass");
                    Colour(e, b, k0, "Stairs and lifts", "core");
                    foreach (var f in new[] { ("Doors", "door"), ("Rails", "rail"), ("Metal", "metal"), ("Ceilings", "ceiling"), ("Lift inside", "liftInterior"), ("Lift button", "liftButton"), ("Detail metal", "detailMetal"), ("Grilles", "grille"), ("Detail dark", "detailDark"), ("Dishes", "dish") })
                        Colour(e, b, k0, f.Item1, f.Item2, optional: true);
                    DefaultsNotice();
                }
            }
            EditorGUILayout.LabelField("Click walls to add", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var (tool, label) in FacadeTools)
                {
                    bool on = GUILayout.Toggle(v.facadeTool == tool, label, EditorStyles.miniButton);
                    if (on != (v.facadeTool == tool)) { v.facadeTool = on ? tool : ""; if (on) StoreyToolContext.Show(StoreyTab.Facade); SceneView.RepaintAll(); }
                }
            }
        }

        void Presets(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            // one list: the built-in presets, then the project's Facade Style assets; "Custom" once the style is edited
            var assets = AssetDatabase.FindAssets("t:" + nameof(FacadeStylePreset)).Select(g => AssetDatabase.LoadAssetAtPath<FacadeStylePreset>(AssetDatabase.GUIDToAssetPath(g))).Where(a => a != null).Select(a => a!).ToList();
            var names = Styles.Presets.Select(p => p.style.label).ToList();
            if (assets.Count > 0) { names.Add(""); names.AddRange(assets.Select(a => "Project/" + a.name)); }
            int current = Styles.Presets.ToList().FindIndex(p => p.key == st.preset);
            bool custom = current < 0;
            if (custom) { names.Insert(0, "Custom"); names.Insert(1, ""); }
            int shift = custom ? 2 : 0;
            using (new EditorGUILayout.HorizontalScope())
            {
                int pick = EditorGUILayout.Popup("Preset", custom ? 0 : current, names.ToArray());
                int i = pick - shift;
                if (pick != (custom ? 0 : current) && i >= 0)
                {
                    if (i < Styles.Presets.Count)
                    {
                        var (key, ps) = Styles.Presets[i];
                        e.ApplyTo(ps.label + " applied", bb => { Styles.ApplyPreset(bb, k0, key); return true; });
                    }
                    else if (i > Styles.Presets.Count)   // past the separator
                    {
                        var a = assets[i - Styles.Presets.Count - 1]; var style = a.Style;
                        e.ApplyTo(a.name + " applied", bb => { Styles.Apply(bb, k0, style); return true; });
                    }
                }
                if (GUILayout.Button(new GUIContent("Save as preset…", "Keep this style as a Facade Style asset, shared with every building"), EditorStyles.miniButton))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Facade style", st.label.Length > 0 ? st.label : "Facade Style", "asset", "Where the style preset goes");
                    if (!string.IsNullOrEmpty(path))
                    {
                        var p = CreateInstance<FacadeStylePreset>(); p.Set(st);
                        AssetDatabase.CreateAsset(p, path); AssetDatabase.SaveAssets();
                    }
                }
            }
        }

        // ---- Interior ----

        void InteriorTab(StoreyEdit e, BuildingData b)
        {
            var v = e.View; int k = v.floor, N = b.floors.Count;
            int shell = GUILayout.Toolbar(b.interior ? 0 : 1, new[] { "Walk-in interior", "Shell only" });
            if ((shell == 1) == b.interior) e.ApplyTo(shell == 1 ? "Shell only: facade with no interior" : "Walk-in interior restored", bb => { bb.interior = shell == 0; return true; });
            if (!b.interior)
            {
                EditorGUILayout.HelpBox("Facade only: windows are opaque, no floors or rooms inside, and the player can't enter. Use it for background blocks. The floor layouts are kept if you switch back.", MessageType.None);
                return;
            }
            int pick = EditorGUILayout.Popup("Floor", N - k, Enumerable.Range(0, N + 1).Select(j => Floor(b, N - j)).ToArray());
            if (pick != N - k) { v.floor = N - pick; k = v.floor; v.selectedCore = ""; v.selectedWall = -1; SceneView.RepaintAll(); }
            if (k == N)
            {
                if (Roofs.IsPitched(b)) { EditorGUILayout.HelpBox("A pitched roof has no roof access. Switch the roof to Flat in the Facade tab to put stairs up here.", MessageType.None); return; }
                if (b.shafts.Count == 0) EditorGUILayout.HelpBox("No stairs or lifts yet. Pick a floor in the Storey Floors overlay, then place stairs.", MessageType.None);
                for (int i = 0; i < b.shafts.Count; i++)
                {
                    var s = b.shafts[i]; bool reach = Derived.ShaftTop(b, s) == N - 1; string id = s.id;
                    using (new EditorGUI.DisabledScope(!reach))
                    {
                        bool roof = EditorGUILayout.Toggle($"{(s.type == CoreType.Lift ? "Lift" : "Stairs")} {i + 1} reaches the roof", s.roof);
                        if (roof != s.roof) e.ApplyTo(roof ? "Now reaches the roof" : "Stops at the top floor", bb => { bb.shafts.First(x => x.id == id).roof = roof; return true; });
                    }
                }
                return;
            }
            int fill = GUILayout.Toolbar(b.floors[k].filled ? 1 : 0, new[] { new GUIContent("Rooms", "A storey to walk in: walls, doors, stairs and lift stops"), new GUIContent("Filled", "Nothing inside: opaque windows and closed doors, no rooms or stairs; lifts pass through without stopping") });
            if ((fill == 1) != b.floors[k].filled)
            {
                bool to = fill == 1; int kk = k;
                e.ApplyTo(to ? $"{Floor(b, k)} filled" : $"{Floor(b, k)} opened", bb => { bb.floors[kk].filled = to; return true; });
                v.selectedCore = ""; v.selectedWall = -1;
            }
            if (b.floors[k].filled)
            {
                EditorGUILayout.HelpBox("Filled: this storey has nothing inside. Its windows are opaque and its doors closed. Stairs stop at the storeys on either side, and lifts pass through without stopping. The rooms drawn here are kept if you switch back.", MessageType.None);
                return;
            }
            PlayHere(e, b, k);
            int tool = GUILayout.Toolbar((int)v.interiorTool, InteriorTools);
            if (tool != (int)v.interiorTool) { v.interiorTool = (InteriorTool)tool; SceneView.RepaintAll(); }
            EditorGUILayout.HelpBox(ToolHint(v.interiorTool), MessageType.None);
            if (v.interiorTool == InteriorTool.Stairs)
            {
                int kind = GUILayout.Toolbar(v.stairKind == CoreType.Flight ? 1 : 0, new[] { new GUIContent("Switchback", "Two flights and a landing per storey, in a walled stairwell"), new GUIContent("Straight flights", "One straight flight per storey with no walls, and a walkway beside it back to the next flight") });
                var want = kind == 1 ? CoreType.Flight : CoreType.Stairs;
                if (want != v.stairKind) { v.stairKind = want; SceneView.RepaintAll(); }
            }
            if (v.interiorTool == InteriorTool.Stairs || v.interiorTool == InteriorTool.Lift)
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Placed at", $"{v.placeRot}° from the nearest wall");
                    if (GUILayout.Button("Turn 90°", EditorStyles.miniButton, GUILayout.Width(64))) { v.placeRot = (v.placeRot + 90) % 360; SceneView.RepaintAll(); }
                }

            var core = b.shafts.FirstOrDefault(s => s.id == v.selectedCore);
            if (core != null) CoreInspector(e, b, core);
            else if (v.selectedWall >= 0 && v.selectedWall < b.floors[k].walls.Count)
            {
                var w = b.floors[k].walls[v.selectedWall]; int wi = v.selectedWall;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"Wall · {Tiers.Hypot(w.b.x - w.a.x, w.b.z - w.a.z):0.00} m");
                    if (GUILayout.Button("Remove", EditorStyles.miniButton)) { e.ApplyTo("Removed", bb => { bb.floors[k].walls.RemoveAt(wi); return true; }); v.selectedWall = -1; }
                }
            }

            EditorGUILayout.LabelField("Storey height", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                Slider(e, "Height", Derived.FloorH(b, k), 2.4, 8, 0.1, (bb, x) => Floors.SetHeight(bb, k, x));
                if (b.floors[k].h.HasValue && GUILayout.Button(new GUIContent("Default", $"Use the building's {(k == 0 ? b.groundHeight : b.floorHeight):0.0} m"), EditorStyles.miniButton, GUILayout.Width(56)))
                    e.ApplyTo("Storey uses the default height", bb => { Floors.SetHeight(bb, k, null); return true; });
            }
            v.showMore = EditorGUILayout.Foldout(v.showMore, "Interior colours", true);
            if (v.showMore) { Colour(e, b, 0, "Walls", "interior", baseStyle: true); Colour(e, b, 0, "Floors", "floor", baseStyle: true); }
        }

        /// <summary>
        /// Play from this floor (docs/PLAY.md, slice 5.4): Play mode starts on this storey, in front of its stairs or lift.
        /// The play kit picks the request up, and makes itself if the scene has none. Play mode shows the saved layout, so
        /// unsaved edits are saved first.
        /// </summary>
        void PlayHere(StoreyEdit e, BuildingData b, int k)
        {
            if (!GUILayout.Button(new GUIContent("Play from this floor", "Enter Play mode on this storey, in front of its stairs or lift"))) return;
            var site = e.Site; if (site == null) return;
            if (e.Dirty)
            {
                if (!EditorUtility.DisplayDialog("Play from this floor", "Play mode shows the saved layout. Save the changes first?", "Save and play", "Cancel")) return;
                e.Save();
            }
            var p = Play.PlayWorld.SpawnOn(b, k);
            var feet = site.transform.position + new Vector3((float)(p.x + b.pos.x), (float)Derived.FloorBase(b, Math.Min(k, b.floors.Count)), (float)(p.z + b.pos.z));
            PlayFrom.Request(site.name, feet, 0);
            EditorApplication.EnterPlaymode();
        }

        void CoreInspector(StoreyEdit e, BuildingData b, CoreData s)
        {
            int N = b.floors.Count; string id = s.id;
            EditorGUILayout.LabelField(s.type == CoreType.Lift ? "Lift" : "Stairs", EditorStyles.boldLabel);
            if (s.type != CoreType.Lift)
            {
                int kind = GUILayout.Toolbar(s.type == CoreType.Flight ? 1 : 0, new[] { "Switchback", "Straight flights" });
                var want = kind == 1 ? CoreType.Flight : CoreType.Stairs;
                if (want != s.type && !e.ApplyTo(want == CoreType.Flight ? "Straight flights" : "Switchback stairs", bb => Shafts.SetKind(bb, bb.shafts.First(x => x.id == id), want)))
                    message = want == CoreType.Flight ? "No room for straight flights here (they are longer)" : "No room for switchback stairs here";
            }
            var floors = Enumerable.Range(0, N).Select(k => Floor(b, k)).ToArray();
            int from = EditorGUILayout.Popup("From", s.bottom, floors);
            if (from != s.bottom) e.ApplyTo("Core range", bb => { Shafts.SetBottom(bb.shafts.First(x => x.id == id), from); return true; });
            int to = EditorGUILayout.Popup("To", s.top < 0 ? 0 : s.top + 1, new[] { "Top floor" }.Concat(floors).ToArray());
            int top = to == 0 ? -1 : to - 1;
            if (top != s.top) e.ApplyTo("Core range", bb => { Shafts.SetTop(bb.shafts.First(x => x.id == id), top); return true; });
            bool roof = EditorGUILayout.Toggle("Continue to the roof", s.roof);
            if (roof != s.roof) e.ApplyTo(roof ? "Now reaches the roof" : "Stops at the top floor", bb => { bb.shafts.First(x => x.id == id).roof = roof; return true; });
            TurnAndRemove(e, s, id);
        }

        /// <summary>Turning and removing, for every core.</summary>
        void TurnAndRemove(StoreyEdit e, CoreData s, string id)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                double ang = EditorGUILayout.DelayedDoubleField("Angle", Math.Round(s.rot, 1));
                if (ang != Math.Round(s.rot, 1) && !e.ApplyTo("Turn core", bb => Shafts.SetAngle(bb, bb.shafts.First(x => x.id == id), ang))) message = $"No room to turn it to {ang}°";
                if (GUILayout.Button(new GUIContent("Align to wall", "Line it up with the nearest wall"), EditorStyles.miniButton) && !e.ApplyTo("Align to wall", bb => Shafts.Align(bb, bb.shafts.First(x => x.id == id)))) message = "No room to turn it there";
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rotate 90°", EditorStyles.miniButton) && !e.ApplyTo("Turn core", bb => { var c = bb.shafts.First(x => x.id == id); return Shafts.SetAngle(bb, c, c.rot + 90); })) message = "No room to rotate here";
                if (s.type == CoreType.Flight && GUILayout.Button(new GUIContent("Reverse", "Turn it round: the flights start at the other end, and the walkway changes side"), EditorStyles.miniButton) && !e.ApplyTo("Flight reversed", bb => { var c = bb.shafts.First(x => x.id == id); return Shafts.SetAngle(bb, c, c.rot + 180); })) message = "No room to turn it round here";
                if (GUILayout.Button("Remove", EditorStyles.miniButton)) { e.ApplyTo("Removed", bb => bb.shafts.RemoveAll(x => x.id == id) > 0); e.View.selectedCore = ""; }
            }
        }

        static string ToolHint(InteriorTool t) => t switch
        {
            InteriorTool.Select => "Drag wall points to reshape rooms · drag + to split a wall · double-click a point to join or remove · drag stairs and lifts · Alt: no snap",
            InteriorTool.Wall => "Click to chain walls, or drag one · snaps to points (ring) and walls (diamond) · Alt: no snap · click the last point again, or Finish wall, to end",
            InteriorTool.Door => "Click a wall to add or remove a doorway",
            InteriorTool.Erase => "Click anything on this floor to remove it",
            InteriorTool.Stairs => "Click to place · lines up with the nearest wall · runs to the top floor · straight flights: the arrow points up them, the walkway back is beside it",
            _ => "Click to place · lines up with the nearest wall · serves every floor above",
        };

        // ---- fields ----

        void Colour(StoreyEdit e, BuildingData b, int k0, string label, string field, bool optional = false, bool baseStyle = false)
        {
            var spec = StyleColorFields.All.First(f => f.name == field);
            var st = baseStyle ? b.style : Styles.Edited(b, k0);
            string? value = spec.get(st);
            string shown = value ?? StyleColors.Of(st, SlotOf(field), StoreyColorField.Defaults?.Invoke() ?? new StyleDefaults()) ?? "#FF00FF";
            var doc = e.Document;
            using (new EditorGUILayout.HorizontalScope())
            {
                string? picked = StoreyColorField.Picker.Draw($"{b.id}/{k0}/{field}", optional && value == null ? label + " (default)" : label, shown, field,
                    id => doc.palette.TryGetValue(id, out var pe) ? pe : null);
                if (picked != null && picked != value)
                    e.ApplyTo(label + " colour", bb => { spec.set(baseStyle ? bb.style : Styles.Edited(bb, k0), picked); return true; });
                if (optional && value != null && GUILayout.Button(new GUIContent("Default", "Use the project's default colour"), EditorStyles.miniButton, GUILayout.Width(56)))
                    e.ApplyTo(label + " colour", bb => { spec.set(baseStyle ? bb.style : Styles.Edited(bb, k0), null!); return true; });
            }
        }

        /// <summary>
        /// The project leaves some defaults unset, so they are the prototype's colours, shown as their nearest palette
        /// entries: offer to store those entries as the project's defaults.
        /// </summary>
        void DefaultsNotice()
        {
            if (StoreyColorField.FillDefaults == null) return;
            var d = StoreyColorField.Defaults?.Invoke() ?? new StyleDefaults();
            if (!new[] { d.door, d.rail, d.metal, d.ceiling, d.liftInterior, d.liftButton, d.detailMetal, d.grille, d.detailDark, d.dish }.Any(x => x != null && x.StartsWith("#"))) return;
            EditorGUILayout.HelpBox("Some default colours aren't set for this project, so they show the nearest palette colour to the prototype's. Store those palette colours as the project's defaults (in Storey Color Settings) to pick them yourself.", MessageType.None);
            if (GUILayout.Button("Store the defaults in Storey Color Settings"))
            {
                int n = StoreyColorField.FillDefaults();
                message = n > 0 ? $"{n} default colour{(n == 1 ? "" : "s")} stored in Storey Color Settings." : null;
            }
        }

        static ColorSlot SlotOf(string field) => field switch
        {
            "wall" => ColorSlot.Wall, "trim" => ColorSlot.Trim, "interior" => ColorSlot.Interior, "floor" => ColorSlot.Floor, "roof" => ColorSlot.Roof,
            "core" => ColorSlot.Core, "glass" => ColorSlot.Glass, "door" => ColorSlot.Door, "rail" => ColorSlot.Rail, "metal" => ColorSlot.Metal,
            "ceiling" => ColorSlot.Ceiling, "liftInterior" => ColorSlot.LiftInterior, "liftButton" => ColorSlot.LiftButton, "detailMetal" => ColorSlot.DetailMetal,
            "grille" => ColorSlot.Grille, "detailDark" => ColorSlot.DetailDark, _ => ColorSlot.Dish,
        };

        static void Slider(StoreyEdit e, string label, double value, double min, double max, double step, Action<BuildingData, double> set)
        {
            EditorGUI.BeginChangeCheck();
            double x = Math.Round(EditorGUILayout.Slider(label, (float)value, (float)min, (float)max) / step) * step;
            if (EditorGUI.EndChangeCheck() && x != value) e.ApplyTo(label, bb => { set(bb, Math.Round(x, 3)); return true; });
        }

        static void StyleSlider(StoreyEdit e, int k0, string label, double value, double min, double max, double step, Action<FacadeStyle, double> set) =>
            Slider(e, label, value, min, max, step, (bb, x) => set(Styles.Edited(bb, k0), x));

        static void StyleToggle(StoreyEdit e, int k0, string label, bool value, Action<FacadeStyle, bool> set)
        {
            bool x = EditorGUILayout.Toggle(label, value);
            if (x != value) e.ApplyTo(label, bb => { set(Styles.Edited(bb, k0), x); return true; });
        }

        static void StyleEnum<T>(StoreyEdit e, int k0, string label, T value, Action<FacadeStyle, T> set) where T : Enum
        {
            var x = (T)EditorGUILayout.EnumPopup(label, value);
            if (!x.Equals(value)) e.ApplyTo(label, bb => { set(Styles.Edited(bb, k0), x); return true; });
        }

        static string Floor(BuildingData b, int k) => k == b.floors.Count ? "Roof" : k == 0 ? "Ground" : "Floor " + k;
        static string Short(BuildingData b, int k) => k == b.floors.Count ? "R" : k == 0 ? "G" : k.ToString();
        static string Range(BuildingData b, Tier t) => t.k1 - 1 == t.k0 ? Floor(b, t.k0) : $"{Short(b, t.k0)}–{Short(b, t.k1 - 1)}";

        // ---- save ----

        void SaveBar(StoreyEdit e)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!e.Dirty))
                {
                    if (GUILayout.Button("Save")) e.Save();
                    if (GUILayout.Button("Revert")) e.Revert();
                }
                if (GUILayout.Button("Stop editing"))
                {
                    if (!e.Dirty) e.End();
                    else
                    {
                        int r = EditorUtility.DisplayDialogComplex("Unsaved layout", e.Path + "\n\nSave the changes?", "Save", "Cancel", "Don't save");
                        if (r == 0) { e.Save(); e.End(); }
                        else if (r == 2) e.End();
                    }
                    if (StoreyEdit.Of((StoreySite)target) == null) StoreyToolContext.Leave();
                }
            }
            if (e.Dirty) EditorGUILayout.HelpBox("Unsaved: Save, or save the scene (Ctrl+S).", MessageType.None);
        }
    }
}
