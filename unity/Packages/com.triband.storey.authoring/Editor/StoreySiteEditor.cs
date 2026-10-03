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
    /// prototype's panel holds, field for field. Selecting the site starts editing it; the layout saves itself
    /// (<see cref="StoreyEdit"/>).
    /// </summary>
    [CustomEditor(typeof(StoreySite))]
    internal sealed class StoreySiteEditor : UnityEditor.Editor
    {
        static readonly string[] TabNames = { "Shape", "Facade", "Interior" };
        static readonly string[] ShapeKeys = { "rect", "L", "U", "T", "oct" }, ShapeLabels = { "Rect", "L", "U", "T", "Octa" };
        static readonly (string tool, string label)[] FacadeTools = { ("entrance", "Entrance"), ("blank", "Blank wall"), ("ac", "AC unit"), ("vent", "Vent"), ("dish", "Dish"), ("escape", "Fire escape"), ("awning", "Awning"), ("bridge", "Bridge") };
        static readonly string[] InteriorTools = { "Select", "Wall", "Door", "Erase", "Stairs", "Lift" };
        string? message;

        static readonly (string label, string hint, Action<Rect, Color> icon)[] Modes =
        {
            ("Shape", "Outline, setbacks, corners and courtyards", StoreyInspectorUI.ShapeIcon),
            ("Facade", "Style, windows, colours, roof, wall details", StoreyInspectorUI.FacadeIcon),
            ("Interior", "Floors, rooms, doors, stairs and lifts", StoreyInspectorUI.InteriorIcon),
        };

        public override void OnInspectorGUI()
        {
            var site = (StoreySite)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(StoreySite.layout)), new GUIContent("Layout", "The .storey file this site draws and edits"));
            serializedObject.ApplyModifiedProperties();
            if (site.layout == null)
            {
                EditorGUILayout.HelpBox("Give the site a layout: a .storey file (Assets › Create › Storey › Layout, or import one from the prototype).", MessageType.Info);
                SiteSettings();
                return;
            }
            var e = StoreyEdit.Of(site);
            // a different layout dropped into the field: the old one is saved, the new one opened
            if (e != null && e.Path != AssetDatabase.GetAssetPath(site.layout)) { e.Save(); e.End(); e = null; }
            e ??= StoreyEdit.Begin(site);

            var tr = site.transform;
            if (tr.rotation != Quaternion.identity || tr.lossyScale != Vector3.one)
                EditorGUILayout.HelpBox("Keep the Storey Site unrotated and unscaled: the layout is in metres, and the Interior tab's floor clip and cutaway assume it. Moving it is fine.", MessageType.Warning);
            BuildingBar(e);
            ProblemsPanel(e);
            var b = e.Selected;
            if (b != null)
            {
                var v = e.View;
                int tab = StoreyInspectorUI.ModeBar((int)v.tab, Modes);
                if (tab != (int)v.tab) SetTab(v, (StoreyTab)tab);
                if (EditorGUIUtility.currentViewWidth < 470) StoreyInspectorUI.ModeHintLine(Modes[(int)v.tab].hint);
                if (!StoreyToolActive() && GUILayout.Button(new GUIContent("Show the Storey handles in the Scene view", "The Scene view's Storey tools edit what this tab shows"))) SetTab(v, v.tab);
                if (message != null) EditorGUILayout.HelpBox(message, MessageType.Info);
                switch (v.tab)
                {
                    case StoreyTab.Shape: ShapeTab(e, b); break;
                    case StoreyTab.Facade: FacadeTab(e, b); break;
                    default: InteriorTab(e, b); break;
                }
            }
            GUILayout.Space(12);
            SiteSettings();
        }

        /// <summary>The site's own settings, which an artist rarely changes: materials, the LOD shown, colliders, the openings kept.</summary>
        void SiteSettings()
        {
            if (!StoreyInspectorUI.Fold("site", "Site settings", false, "Materials, the LOD shown, colliders, and the artist-made windows and doors the layout uses")) return;
            serializedObject.Update();
            var it = serializedObject.GetIterator(); bool enter = true;
            using (new EditorGUI.IndentLevelScope())
                while (it.NextVisible(enter))
                {
                    enter = false;
                    if (it.name == "m_Script" || it.name == nameof(StoreySite.layout)) continue;
                    EditorGUILayout.PropertyField(it, true);
                }
            serializedObject.ApplyModifiedProperties();
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
            GUILayout.Space(6);
            // one card: everything in it acts on the building picked at its top
            using var card = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
            EditorGUILayout.LabelField(new GUIContent("Building", "Pick a building here or click it in the Scene view"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                int cur = doc.buildings.FindIndex(x => x.id == v.selectedId);
                int sel = EditorGUILayout.Popup(cur, doc.buildings.Select(x => $"{x.name}  ({x.floors.Count} fl)").ToArray());
                if (sel != cur && sel >= 0) { v.Select(doc.buildings[sel].id); SceneView.RepaintAll(); }
                if (GUILayout.Button(new GUIContent("New ▾", "A new building from a shape or a template"), EditorStyles.miniButton, GUILayout.Width(52)))
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
                    // the project's building templates
                    var templates = StoreyBuildingTemplate.All();
                    if (templates.Length > 0) menu.AddSeparator("");
                    foreach (var t in templates)
                    {
                        var tb = t.Building!; string tname = t.name;
                        menu.AddItem(new GUIContent("From template/" + tname), false, () =>
                        {
                            string id = "";
                            var near = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                            e.Apply("Placed " + tname, d => { id = Buildings.FromTemplate(d, tb, new Vec2(near.x, near.z)).id; return true; });
                            e.View.Select(id); SetTab(e.View, StoreyTab.Shape);
                        });
                    }
                    if (templates.Length == 0) menu.AddDisabledItem(new GUIContent("From template (save a building as one first)"));
                    menu.ShowAsContext();
                }
                var b = e.Selected;
                using (new EditorGUI.DisabledScope(b == null))
                {
                    if (GUILayout.Button(new GUIContent("Duplicate", "A copy of it, next to it"), EditorStyles.miniButton, GUILayout.Width(64)) && b != null)
                    {
                        string id = ""; string src = b.id;
                        var near = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
                        e.Apply("Duplicated " + b.name, d => { id = Buildings.Duplicate(d, d.buildings.First(x => x.id == src), new Vec2(near.x, near.z)).id; return true; });
                        e.View.Select(id);
                    }
                    if (GUILayout.Button(new GUIContent("Delete", "Remove it (undo brings it back)"), EditorStyles.miniButton, GUILayout.Width(48)) && b != null)
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
                if (GUILayout.Button(new GUIContent("Save as template…", "Keep this building as an asset to place again, in this layout or another"), EditorStyles.miniButton, GUILayout.Width(110))) SaveTemplate(sb);
            }
        }

        /// <summary>Save a building as a template asset: a new one, or over one picked in the save panel.</summary>
        static void SaveTemplate(BuildingData b)
        {
            string path = EditorUtility.SaveFilePanelInProject("Save building template", b.name, "asset", "Where to keep the template");
            if (string.IsNullOrEmpty(path)) return;
            var t = AssetDatabase.LoadAssetAtPath<StoreyBuildingTemplate>(path);
            if (t == null)
            {
                t = CreateInstance<StoreyBuildingTemplate>(); t.Set(b);
                AssetDatabase.CreateAsset(t, path);
            }
            else { Undo.RecordObject(t, "Save building template"); t.Set(b); EditorUtility.SetDirty(t); }
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(t);
        }

        // ---- problems ----

        /// <summary>
        /// The layout's problems (Validate.Problems): a foldout with a count, one line each, and a click that selects the
        /// building, opens the storey and frames the Scene view on it.
        /// </summary>
        void ProblemsPanel(StoreyEdit e)
        {
            var v = e.View; var ps = StoreyProblems.For(e);
            int errors = ps.Count(p => p.severity == Validate.Severity.Error);
            if (ps.Count == 0)
            {
                EditorGUILayout.LabelField(new GUIContent(StoreyProblems.Stale(e) ? "✓ No problems found yet …" : "✓ No problems: every storey can be reached", "Storey checks the layout as you edit: rooms and storeys nobody can reach, stairs that are walled in"), StoreyInspectorUI.Caption);
                return;
            }
            string title = $"⚠ {ps.Count} problem{(ps.Count == 1 ? "" : "s")}{(errors > 0 ? $", {errors} serious" : "")}";
            if (StoreyProblems.Stale(e)) title += " …";
            var keepC = GUI.contentColor; if (errors > 0) GUI.contentColor = Color.Lerp(Color.white, StoreyProblems.ColorOf(ps.First(p => p.severity == Validate.Severity.Error)), 0.6f);
            bool show = EditorGUILayout.Foldout(v.showProblems, new GUIContent(title, "Click one to go to it"), true);
            GUI.contentColor = keepC;
            if (show != v.showProblems) { v.showProblems = show; SceneView.RepaintAll(); }
            if (!show || ps.Count == 0) return;
            const int Most = 25;
            foreach (var p in ps.Take(Most))
            {
                var keep = GUI.color; GUI.color = Color.Lerp(Color.white, StoreyProblems.ColorOf(p), 0.55f);
                string where = p.k >= 0 ? $"{p.building} · {Floor(e.Document.buildings.FirstOrDefault(x => x.id == p.buildingId) ?? new BuildingData(), p.k)}" : p.building;
                bool go = GUILayout.Button(new GUIContent($"{(p.severity == Validate.Severity.Error ? "●" : "○")} {where}: {p.message}", "Show it"), EditorStyles.miniButtonLeft);
                GUI.color = keep;
                if (go) GoTo(e, p);
            }
            if (ps.Count > Most) EditorGUILayout.LabelField($"and {ps.Count - Most} more", EditorStyles.miniLabel);
        }

        static void GoTo(StoreyEdit e, Validate.Problem p)
        {
            var v = e.View; v.Select(p.buildingId);
            var b = e.Selected;
            if (b != null && p.k >= 0 && p.k < b.floors.Count && p.code.StartsWith("unreached")) { v.floor = p.k; SetTab(v, StoreyTab.Interior); }
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && e.Site != null)
            {
                var at = e.Site.transform.TransformPoint(new Vector3((float)p.x, (float)p.y, (float)p.z));
                sv.LookAt(at, sv.rotation, 14f);
            }
            SceneView.RepaintAll();
        }

        // ---- Shape ----

        void ShapeTab(StoreyEdit e, BuildingData b)
        {
            var v = e.View; int k0 = v.tier; var ts = Tiers.Of(b); var cur = ts.First(t => t.k0 == k0);
            StoreyInspectorUI.Section("Outline", "The building's footprint, and the setbacks above it that each have their own outline");
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
            CornerSection(e, b, k0);
            VoidSection(e, b);
            StoreyInspectorUI.Section("Storey heights", "Every storey's height; one storey can differ (Interior tab)");
            Slider(e, "Ground", b.groundHeight, 3, 6, 0.1, (bb, x) => bb.groundHeight = x);
            Slider(e, "Upper", b.floorHeight, 2.7, 4.5, 0.1, (bb, x) => bb.floorHeight = x);
            EditorGUILayout.LabelField($"{Math.Abs(Geo.Area2(Tiers.Outline(b, k0))) / 2:0} m² per floor · {Derived.RoofY(b):0.0} m tall", EditorStyles.miniLabel);
        }

        /// <summary>
        /// Chamfer or round the selected corner of this outline, or every corner (CornerCuts). A cut corner stays one
        /// corner: select it and its cut changes as you set it, or goes with Make sharp. On the base outline a chamfer
        /// wide enough can take a corner entrance.
        /// </summary>
        void CornerSection(StoreyEdit e, BuildingData b, int k0)
        {
            var v = e.View; var (fp, cuts) = CornerCuts.Sharp(b, k0);
            int ci = v.selectedCorner < fp.Count ? v.selectedCorner : -1;
            var cut = cuts.FirstOrDefault(c => c.i == ci);
            StoreyInspectorUI.Section("Corners", "Click a corner in the Scene view to cut it, or to change its cut");
            // a cut corner shows its own cut, and changes as it is set; otherwise these set the next cut
            var shape = cut?.shape ?? v.cornerShape; float size = cut != null ? (float)cut.size : v.cornerSize; bool door = cut?.door ?? v.cornerDoor;
            EditorGUI.BeginChangeCheck();
            shape = (CornerShape)GUILayout.Toolbar((int)shape, new[] { new GUIContent("Chamfer", "Cut the corner off straight"), new GUIContent("Round", "Round the corner off in an arc") });
            size = EditorGUILayout.Slider(shape == CornerShape.Round ? "Radius" : "Size", size, 0.5f, 8f);
            if (k0 == 0 && shape == CornerShape.Chamfer)
                door = EditorGUILayout.Toggle(new GUIContent("Corner entrance", "A street door in the middle of the chamfer (it needs about 2.7 m)"), door);
            bool changed = EditorGUI.EndChangeCheck();
            if (changed)
            {
                v.cornerShape = shape; v.cornerSize = size; v.cornerDoor = door;
                if (cut != null) CutCorner(e, k0, ci, shape, size, door);
                SceneView.RepaintAll();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (cut != null)
                {
                    if (GUILayout.Button(new GUIContent("Make sharp", "Take this corner's cut away")))
                        e.ApplyTo("Corner made sharp", bb => CornerCuts.Clear(bb, k0, ci));
                }
                else using (new EditorGUI.DisabledScope(ci < 0))
                    if (GUILayout.Button(new GUIContent(ci < 0 ? "Click a corner first" : "Cut this corner", "The selected corner, shown green in the Scene view")))
                        CutCorner(e, k0, ci, shape, size, door);
                if (GUILayout.Button(new GUIContent("Every corner", "Cut every corner that can take it this way")))
                {
                    int done = 0;
                    e.ApplyTo(shape == CornerShape.Round ? "Corners rounded" : "Corners chamfered", bb => (done = CornerCuts.CutAll(bb, k0, shape, size)) > 0);
                    message = done == 0 ? "No corner can take a cut that size." : null;
                }
                using (new EditorGUI.DisabledScope(cuts.Count == 0))
                    if (GUILayout.Button(new GUIContent("Clear all", "Make every corner of this outline sharp again")))
                        e.ApplyTo("Corners made sharp", bb => CornerCuts.ClearAll(bb, k0) > 0);
            }
        }

        void CutCorner(StoreyEdit e, int k0, int ci, CornerShape shape, double size, bool door)
        {
            var why = OutlineIssue.None;
            bool ok = e.ApplyTo(shape == CornerShape.Round ? "Corner rounded" : "Corner chamfered", bb => (why = CornerCuts.Cut(bb, k0, ci, shape, size, door)) == OutlineIssue.None);
            var b = e.Selected;
            var made = b != null ? CornerCuts.Live(b, k0).FirstOrDefault(x => x.c.door && door) : default;
            message = !ok ? (why == OutlineIssue.Shape ? "That corner is straight, or its edges are too short for a cut that size." : Outlines.Why(why))
                : made.c != null && Tiers.EdgeLen(Tiers.Outline(b!, 0), made.start) < 2.7 ? "The chamfer is too narrow to open a door in: make it bigger." : null;
        }

        /// <summary>
        /// The building's courtyards and atria (Voids): add one, pick which the Scene view edits, switch its kind, set the
        /// storey it starts on, remove it.
        /// </summary>
        void VoidSection(StoreyEdit e, BuildingData b)
        {
            var v = e.View; int N = b.floors.Count;
            if (!StoreyInspectorUI.Fold("voids", b.voids.Count > 0 ? $"Courtyards and atria ({b.voids.Count})" : "Courtyards and atria", b.voids.Count > 0, "Open space inside the building: a courtyard open to the sky, or an atrium under a skylight")) return;
            var floors = Enumerable.Range(0, N).Select(k => Floor(b, k)).ToArray();
            for (int i = 0; i < b.voids.Count; i++)
            {
                var vd = b.voids[i]; string id = vd.id;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool on = GUILayout.Toggle(v.selectedVoid == id, $"{(vd.kind == VoidKind.Courtyard ? "Courtyard" : "Atrium")} {i + 1}", EditorStyles.miniButton, GUILayout.Width(90));
                    if (on && v.selectedVoid != id) { v.selectedVoid = id; SceneView.RepaintAll(); }
                    int kind = GUILayout.Toolbar((int)vd.kind, new[] { new GUIContent("Courtyard", "Open to the sky, with facades and a door onto it"), new GUIContent("Atrium", "Inside: the floors above its bottom are open round it, under a skylight") }, EditorStyles.miniButton);
                    if (kind != (int)vd.kind)
                    {
                        var why = VoidIssue.None;
                        if (!e.ApplyTo(kind == 1 ? "Now an atrium" : "Now a courtyard", bb => (why = Voids.SetKind(bb, id, (VoidKind)kind)) == VoidIssue.None)) message = Voids.Why(why);
                    }
                    if (GUILayout.Button(new GUIContent("×", "Remove it"), EditorStyles.miniButton, GUILayout.Width(22)))
                    {
                        e.ApplyTo((vd.kind == VoidKind.Courtyard ? "Courtyard" : "Atrium") + " removed", bb => Voids.Remove(bb, id));
                        if (v.selectedVoid == id) v.selectedVoid = "";
                        return;
                    }
                }
                int from = EditorGUILayout.Popup(vd.kind == VoidKind.Courtyard ? "   Paved on" : "   Floor on", Math.Min(vd.bottom, N - 1), floors);
                if (from != vd.bottom)
                {
                    var why = VoidIssue.None;
                    if (!e.ApplyTo("Starts on " + Floor(b, from), bb => (why = Voids.SetBottom(bb, id, from)) == VoidIssue.None)) message = Voids.Why(why);
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var (kind, label) in new[] { (VoidKind.Courtyard, "Add courtyard"), (VoidKind.Atrium, "Add atrium") })
                    if (GUILayout.Button(label))
                    {
                        string nid = "";
                        bool ok = e.ApplyTo(label.Substring(4, 1).ToUpper() + label.Substring(5) + " added", bb => { var nv = Voids.Add(bb, kind, 0); nid = nv?.id ?? ""; return nv != null; });
                        if (ok) { v.selectedVoid = nid; message = "Drag its green corners in the Scene view, or its centre to move it."; SceneView.RepaintAll(); }
                        else message = "There's no room for one: it needs 2.5 × 2.5 m, 1.5 m inside the walls of every floor and clear of stairs and lifts.";
                    }
            }
        }

        // ---- Facade ----

        void FacadeTab(StoreyEdit e, BuildingData b)
        {
            var v = e.View; var ts = Tiers.Of(b); int k0 = ts.Count > 1 ? v.tier : 0;
            if (ts.Count > 1)
            {
                StoreyInspectorUI.Section("Style for", "The base and each setback can have a style of their own");
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
                StoreyInspectorUI.Section("Style", "Start from a preset; every change below makes the style this building's own");
                Presets(e, b, k0, st);
                StoreyInspectorUI.Section("Windows and doors");
                StyleEnum(e, k0, "Windows", st.windows, (s, x) => s.windows = x);
                KindPopup(e, k0, "Window", st.windowKind, false);
                StyleSlider(e, k0, "Window width", st.winW, 0.6, 2.4, 0.1, (s, x) => s.winW = x);
                StyleSlider(e, k0, "Bay spacing", st.bay, 1.4, 5, 0.1, (s, x) => s.bay = x);
                WindowDetails(e, b, k0, st);
                StoreyInspectorUI.Section("Walls");
                if (k0 == 0) StyleEnum(e, k0, "Ground floor", st.ground, (s, x) => s.ground = x);
                WallDetails(e, b, k0, st);
                StoreyInspectorUI.Section("Colours", "From the project's palette");
                Colour(e, b, k0, "Walls", "wall");
                Colour(e, b, k0, "Trim", "trim");
                Colour(e, b, k0, "Roof", "roof");
                Colour(e, b, k0, "Glass", "glass");
                if (StoreyInspectorUI.Fold("colours", "More colours", false, "Doors, frames, metal, the inside, details: each follows the project's default until set"))
                {
                    Colour(e, b, k0, "Stairs and lifts", "core");
                    foreach (var f in new[] { ("Doors", "door"), ("Frames", "frame"), ("Plinth", "plinth"), ("Foundation", "foundationColor"), ("Rails", "rail"), ("Metal", "metal"), ("Ceilings", "ceiling"), ("Lift inside", "liftInterior"), ("Lift button", "liftButton"), ("Detail metal", "detailMetal"), ("Grilles", "grille"), ("Detail dark", "detailDark"), ("Dishes", "dish") })
                        Colour(e, b, k0, f.Item1, f.Item2, optional: true);
                    DefaultsNotice();
                }
                bool drives = Styles.DrivesRoof(b, k0);
                if (drives)
                {
                    StoreyInspectorUI.Section("Roof");
                    StyleEnum(e, k0, "Type", st.roofType, (s, x) => s.roofType = x);
                    if (st.roofType != RoofType.Flat)
                    {
                        bool mans = st.roofType == RoofType.Mansard;
                        StyleSlider(e, k0, mans ? "Top pitch" : "Pitch", st.pitch ?? (st.roofType == RoofType.Shed ? 15 : mans ? 20 : 30), 5, 60, 1, (s, x) => s.pitch = x);
                        StyleSlider(e, k0, "Eaves", st.eave ?? 0.35, 0, 1.2, 0.05, (s, x) => s.eave = x);
                        if (mans) StyleSlider(e, k0, "Steep part", st.mansard ?? 2.4, 0.5, 6, 0.1, (s, x) => s.mansard = x);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            bool dm = EditorGUILayout.Toggle(new GUIContent("Dormers", "Dormer windows along every eave, wherever they fit on the slope"), st.dormers.HasValue);
                            if (dm != st.dormers.HasValue) e.ApplyTo(dm ? "Dormers added" : "Dormers removed", bb => { Styles.Edited(bb, k0).dormers = dm ? 3.5 : (double?)null; return true; });
                        }
                        if (st.dormers is double every) StyleSlider(e, k0, "Dormers every", every, 2.2, 10, 0.1, (s, x) => s.dormers = x);
                        if (mans) EditorGUILayout.HelpBox("A mansard: a steep 70° slope rises from the eaves to the break, then a shallow hip at the top pitch.", MessageType.None);
                    }
                    else StyleToggle(e, k0, "Parapet", st.parapet, (s, x) => s.parapet = x);
                }
                if (StoreyInspectorUI.Fold("rules", "Details by rule", false, "AC units and vents scattered on the walls by the style; place your own below"))
                {
                    var (ac, vents) = Details.Rules(st);
                    StyleSlider(e, k0, "AC units %", Math.Round(ac * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).ac = x);
                    StyleSlider(e, k0, "Vents %", Math.Round(vents * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).vents = x);
                }
            }
            StoreyInspectorUI.Section("Place on walls", "Pick one, then click a wall in the Scene view; click a placed one to remove it");
            // two rows of four: the labels stay whole at inspector widths
            for (int row = 0; row < FacadeTools.Length; row += 4)
                using (new EditorGUILayout.HorizontalScope())
                    foreach (var (tool, label) in FacadeTools.Skip(row).Take(4))
                    {
                        bool on = GUILayout.Toggle(v.facadeTool == tool, label, EditorStyles.miniButton, GUILayout.Height(20));
                        if (on != (v.facadeTool == tool)) { v.facadeTool = on ? tool : ""; if (on) StoreyToolContext.Show(StoreyTab.Facade); SceneView.RepaintAll(); }
                    }
            if (v.facadeTool == "bridge") EditorGUILayout.HelpBox("Click an upper floor's wall: a bridge goes straight out to the building facing it, to its floor nearest this one. Click a bridge's door to remove it.", MessageType.None);
            BridgeList(e, b);
        }

        /// <summary>The bridges from and to this building: enclosed or open, their width, and removing them.</summary>
        void BridgeList(StoreyEdit e, BuildingData b)
        {
            var site = new Site(e.Document.buildings);
            var spans = Bridges.Touching(site, b);
            var dangling = b.bridges.Where(x => Bridges.Span(site, b, x) == null).ToList();
            if (spans.Count == 0 && dangling.Count == 0) return;
            StoreyInspectorUI.Section("Bridges", "Bridges from and to this building");
            foreach (var s in spans)
            {
                bool own = ReferenceEquals(s.A, b); var other = own ? s.B : s.A; string owner = s.A.id, id = s.br.id;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{Floor(b, own ? s.br.k : s.br.toK)} ↔ {other.name}, {Floor(other, own ? s.br.toK : s.br.k)} · {s.L:0.0} m", GUILayout.MinWidth(120));
                    int kind = GUILayout.Toolbar(s.br.open ? 1 : 0, new[] { "Enclosed", "Open" }, GUILayout.Width(130));
                    if (kind != (s.br.open ? 1 : 0)) e.Apply(kind == 1 ? "Bridge opened" : "Bridge enclosed", d => { var br = d.buildings.First(x => x.id == owner).bridges.First(x => x.id == id); br.open = kind == 1; return true; });
                    if (GUILayout.Button(new GUIContent("×", "Remove the bridge"), EditorStyles.miniButton, GUILayout.Width(22))) { e.Apply("Bridge removed", d => BridgeEdits.Remove(d.buildings.First(x => x.id == owner), id)); return; }
                }
                EditorGUI.BeginChangeCheck();
                float w = EditorGUILayout.Slider("   Width", (float)s.br.width, 1.5f, 4f);
                if (EditorGUI.EndChangeCheck()) e.Apply("Bridge width", d => { d.buildings.First(x => x.id == owner).bridges.First(x => x.id == id).width = Math.Round(w * 10) / 10; return true; });
            }
            foreach (var br in dangling)
                using (new EditorGUILayout.HorizontalScope())
                {
                    string id = br.id, bid = b.id;
                    EditorGUILayout.HelpBox($"A bridge from {Floor(b, br.k)} no longer meets {site.ById(br.to)?.name ?? "the building it went to"}.", MessageType.Warning);
                    if (GUILayout.Button("Remove", GUILayout.Width(64))) { e.Apply("Bridge removed", d => BridgeEdits.Remove(d.buildings.First(x => x.id == bid), id)); return; }
                }
        }

        /// <summary>The style's window and door details (docs/EDITOR.md §6.7): heads, glazing bars, frames, and on the base the street doors.</summary>
        void WindowDetails(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            bool artist = Generate.OpeningKinds.Of(st, false) != null;
            using (new EditorGUI.DisabledScope(artist))
            {
                StyleToggle(e, k0, "Sills", st.sills, (s, x) => s.sills = x);
                StyleToggle(e, k0, "Heads", st.heads, (s, x) => s.heads = x);
                using (new EditorGUI.DisabledScope(!st.heads))
                    StyleEnum(e, k0, "   Head shape", st.head, (s, x) => s.head = x);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool panes = EditorGUILayout.Toggle(new GUIContent("Glazing bars", "Divide every window into panes"), st.paneCols.HasValue || st.paneRows.HasValue);
                    if (panes != (st.paneCols.HasValue || st.paneRows.HasValue)) e.ApplyTo(panes ? "Glazing bars" : "No glazing bars", bb => { var s = Styles.Edited(bb, k0); s.paneCols = panes ? 2 : (int?)null; s.paneRows = panes ? 3 : (int?)null; return true; });
                }
                if (st.paneCols.HasValue || st.paneRows.HasValue)
                {
                    StyleSlider(e, k0, "   Panes across", st.paneCols ?? 1, 1, 6, 1, (s, x) => s.paneCols = (int)x);
                    StyleSlider(e, k0, "   Panes up", st.paneRows ?? 1, 1, 6, 1, (s, x) => s.paneRows = (int)x);
                }
                StyleToggle(e, k0, "Frames", st.frames, (s, x) => s.frames = x);
            }
            if (artist) EditorGUILayout.LabelField("The artist's window brings its own head, frame and bars.", StoreyInspectorUI.Caption);
            if (k0 == 0)
            {
                KindPopup(e, k0, "Street door", st.doorKind, true);
                if (Generate.OpeningKinds.Get(st.doorKind) == null) StyleEnum(e, k0, "Street doors", st.doorType, (s, x) => s.doorType = x);
            }
        }

        /// <summary>The style's wall details (docs/EDITOR.md §6.7): bands, and on the base the plinth and foundation; brick patches on any.</summary>
        void WallDetails(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            StyleToggle(e, k0, "Floor bands", st.bands, (s, x) => s.bands = x);
            if (st.bands)
            {
                StyleSlider(e, k0, "   Band height", st.bandH ?? 0.22, 0.05, 1.5, 0.01, (s, x) => s.bandH = x);
                StyleSlider(e, k0, "   Band stands out", st.bandDepth ?? 0.06, 0.01, 0.2, 0.01, (s, x) => s.bandDepth = x);
                StyleToggle(e, k0, "   Band in a wall shade", st.bandWall, (s, x) => s.bandWall = x);
            }
            if (k0 == 0)
            {
                StyleSlider(e, k0, "Plinth height", st.plinthH ?? 0.45, 0, 2, 0.05, (s, x) => s.plinthH = x);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool fd = EditorGUILayout.Toggle(new GUIContent("Foundation", "A foundation along the foot of the ground floor"), st.foundation.HasValue);
                    if (fd != st.foundation.HasValue) e.ApplyTo(fd ? "Foundation" : "No foundation", bb => { Styles.Edited(bb, k0).foundation = fd ? 0.5 : (double?)null; return true; });
                }
                if (st.foundation is double depth)
                {
                    StyleSlider(e, k0, "   Below ground", depth, 0, 5, 0.05, (s, x) => s.foundation = x);
                    StyleSlider(e, k0, "   Above ground", st.foundationH ?? 0.2, 0.02, 1.5, 0.01, (s, x) => s.foundationH = x);
                }
            }
            StyleSlider(e, k0, "Brick patches", st.bricks ?? 0, 0, 1, 0.05, (s, x) => s.bricks = x > 0 ? x : (double?)null);
        }

        /// <summary>
        /// The generator's window (or street door), or one an artist made (a Storey Opening asset: docs/EDITOR.md §6.9).
        /// Picking one adds it to the site's list, so builds include it.
        /// </summary>
        void KindPopup(StoreyEdit e, int k0, string label, string? current, bool door)
        {
            var kinds = StoreyOpenings.All().Where(o => o.door == door).ToList();
            var names = new List<string> { door ? "The style's own" : "Generated" };
            names.AddRange(kinds.Select(o => o.name));
            if (kinds.Count == 0) names.Add(door ? "(make one: Create ▸ Storey ▸ Window or Door)" : "(make one: Create ▸ Storey ▸ Window or Door)");
            int at = current == null ? 0 : kinds.FindIndex(o => o.Id == current) + 1;
            if (current != null && at == 0) { names.Add("Missing: " + current.Substring(0, Math.Min(8, current.Length))); at = names.Count - 1; }
            int pick = EditorGUILayout.Popup(new GUIContent(label, door ? "Street doors: the style's own (Canopy or Glazed), or one an artist made" : "Windows: the generator's own, or one an artist made"), at, names.Select(n => new GUIContent(n)).ToArray());
            if (pick == at) return;
            if (pick == 0) { e.ApplyTo(label + ": generated", bb => { var s = Styles.Edited(bb, k0); if (door) s.doorKind = null; else s.windowKind = null; return true; }); return; }
            if (pick - 1 >= kinds.Count) return;
            var k = kinds[pick - 1]; string id = k.Id;
            e.ApplyTo(label + ": " + k.name, bb => { var s = Styles.Edited(bb, k0); if (door) s.doorKind = id; else s.windowKind = id; return true; });
            StoreyOpenings.Keep(e.Site, k);
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
            StoreyInspectorUI.Section("Inside", "Walk-in buildings have floors, rooms and stairs; shells are facades only");
            int shell = GUILayout.Toolbar(b.interior ? 0 : 1, new[] { "Walk-in interior", "Shell only" });
            if ((shell == 1) == b.interior) e.ApplyTo(shell == 1 ? "Shell only: facade with no interior" : "Walk-in interior restored", bb => { bb.interior = shell == 0; return true; });
            if (!b.interior)
            {
                EditorGUILayout.HelpBox("Facade only: windows are opaque, no floors or rooms inside, and the player can't enter. Use it for background blocks. The floor layouts are kept if you switch back.", MessageType.None);
                return;
            }
            StoreyInspectorUI.Section("Floor", "The storey being edited; the Storey Floors overlay in the Scene view picks it too");
            using (new EditorGUILayout.HorizontalScope())
            {
                int pickF = EditorGUILayout.Popup("Floor", N - k, Enumerable.Range(0, N + 1).Select(j => Floor(b, N - j)).ToArray());
                if (pickF != N - k) { v.floor = N - pickF; k = v.floor; v.selectedCore = ""; v.selectedWall = -1; SceneView.RepaintAll(); }
                v.followFloor = GUILayout.Toggle(v.followFloor, new GUIContent("Camera follows", "The Scene view's camera moves up and down with the active floor"), EditorStyles.miniButton, GUILayout.Width(100));
            }
            int wm = EditorGUILayout.Popup(new GUIContent("Walls", "This storey's walls in the Scene view"), (int)v.walls, new[]
            {
                new GUIContent("Down", "Every wall low, so the rooms show from any side; nothing moves as the camera goes round"),
                new GUIContent("Cutaway", "Only the walls between the camera and the building drop, and they follow the camera"),
                new GUIContent("Up", "Every wall at full height"),
            });
            if (wm != (int)v.walls) { v.walls = (CutWalls)wm; SceneView.RepaintAll(); }
            int pick = N - k;
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
            StoreyInspectorUI.Section("Tools", "Pick one, then work in the Scene view");
            int tool = GUILayout.Toolbar((int)v.interiorTool, InteriorTools, GUILayout.Height(24));
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

            StoreyInspectorUI.Section("This storey's height");
            using (new EditorGUILayout.HorizontalScope())
            {
                Slider(e, "Height", Derived.FloorH(b, k), 2.4, 8, 0.1, (bb, x) => Floors.SetHeight(bb, k, x));
                if (b.floors[k].h.HasValue && GUILayout.Button(new GUIContent("Default", $"Use the building's {(k == 0 ? b.groundHeight : b.floorHeight):0.0} m"), EditorStyles.miniButton, GUILayout.Width(56)))
                    e.ApplyTo("Storey uses the default height", bb => { Floors.SetHeight(bb, k, null); return true; });
            }
            if (StoreyInspectorUI.Fold("inside", "Interior colours", false, "The walls and floors inside, for the whole building")) { Colour(e, b, 0, "Walls", "interior", baseStyle: true); Colour(e, b, 0, "Floors", "floor", baseStyle: true); }
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
            if (!new[] { d.door, d.rail, d.metal, d.ceiling, d.liftInterior, d.liftButton, d.detailMetal, d.grille, d.detailDark, d.dish, d.frame, d.foundation, d.plinth }.Any(x => x != null && x.StartsWith("#"))) return;
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
            "grille" => ColorSlot.Grille, "detailDark" => ColorSlot.DetailDark, "frame" => ColorSlot.Frame, "foundationColor" => ColorSlot.Foundation, "plinth" => ColorSlot.Plinth, _ => ColorSlot.Dish,
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
    }
}
