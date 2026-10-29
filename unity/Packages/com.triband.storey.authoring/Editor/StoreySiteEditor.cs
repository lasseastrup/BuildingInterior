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
        static readonly (string label, string hint, Action<Rect, Color> icon)[] InteriorTools =
        {
            ("Select", "Pick a wall, stairs or a lift to edit or move it", StoreyInspectorUI.SelectIcon),
            ("Wall", "Draw walls: click a chain of points, or drag one wall", StoreyInspectorUI.WallIcon),
            ("Door", "Click a wall to put a doorway in it, or take one out", StoreyInspectorUI.DoorIcon),
            ("Erase", "Click a wall, doorway, stairs or lift to remove it", StoreyInspectorUI.EraseIcon),
            ("Stairs", "Place stairs: switchback or straight flights", StoreyInspectorUI.StairsIcon),
            ("Lift", "Place a lift shaft", StoreyInspectorUI.LiftIcon),
        };
        string? message;

        static readonly (string label, string hint, Action<Rect, Color> icon)[] Modes =
        {
            ("Shape", "Outline, setbacks, corners and courtyards", StoreyInspectorUI.ShapeIcon),
            ("Facade", "Style, windows, colours, roof, wall details", StoreyInspectorUI.FacadeIcon),
            ("Interior", "Floors, rooms, doors, stairs and lifts", StoreyInspectorUI.InteriorIcon),
        };

        public override void OnInspectorGUI()
        {
            using var _t = StoreyTimings.Time("inspector: draw");
            var site = (StoreySite)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(StoreySite.layout)), new GUIContent("Layout", "The .storey file this site draws and edits"));
            serializedObject.ApplyModifiedProperties();
            if (site.layout == null)
            {
                EditorGUILayout.HelpBox("Give the site a layout: a .storey file (Assets › Create › Storey › Layout).", MessageType.Info);
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

        /// <summary>The site's own settings, which an artist rarely changes: materials, the LOD shown, colliders, the openings and details kept.</summary>
        void SiteSettings()
        {
            if (!StoreyInspectorUI.Fold("site", "Site settings", false, "Materials, the LOD shown, colliders, and the artist-made windows, doors and facade details the layout uses")) return;
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
            if (GUILayout.Button(new GUIContent("Find neighbouring districts", "Link this site with every other Storey Site in the open scenes whose buildings come within 2 m of its own, both ways, so buildings back to back across the edge share one party wall")))
                FindNeighbours((StoreySite)target);
        }

        /// <summary>
        /// Every other site in the open scenes whose layout comes within 2 m of this one's becomes its neighbour, and this
        /// one becomes theirs (docs/CITY.md §5).
        /// </summary>
        static void FindNeighbours(StoreySite me)
        {
            if (me.layout == null || me.PlanArea is not Lod.Districts.Area a) return;
            int linked = 0;
            foreach (var other in StoreySite.All)
            {
                if (other == me || other.layout == null || other.layout == me.layout || other.PlanArea is not Lod.Districts.Area b) continue;
                bool near = a.x0 <= b.x1 + 2 && b.x0 <= a.x1 + 2 && a.z0 <= b.z1 + 2 && b.z0 <= a.z1 + 2;
                if (!near) continue;
                Undo.RecordObjects(new UnityEngine.Object[] { me, other }, "Link neighbouring districts");
                Link(me, other); Link(other, me); linked++;
            }
            Debug.Log(linked == 0 ? "Storey: no other site's buildings come within 2 m of this one's." : $"Storey: linked {linked} neighbouring district{(linked == 1 ? "" : "s")}.");
        }

        static void Link(StoreySite from, StoreySite to)
        {
            var d = to.transform.position - from.transform.position; var offset = new Vector2(d.x, d.z);
            var n = from.neighbours.FirstOrDefault(x => x != null && x.layout == to.layout);
            if (n == null) from.neighbours.Add(n = new StoreySite.NeighbourDistrict { layout = to.layout });
            n.offset = offset;
            EditorUtility.SetDirty(from);
        }

        static bool StoreyToolActive() => typeof(StoreyTool).IsAssignableFrom(ToolManager.activeToolType);

        // Frame Selected (F) in the Scene view asks the selection's inspectors for these (Unity messages, by name): in the
        // Storey context it frames the building being edited, not the whole site
        Bounds? FrameBounds() => target is StoreySite s ? StoreyToolContext.FrameBounds(s) : null;
        bool HasFrameBounds() => FrameBounds() != null;
        Bounds OnGetFrameBounds() => FrameBounds() ?? default;

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
                if (GUILayout.Button(new GUIContent("New ▾", "A new building from a template: the package's, or the project's own"), EditorStyles.miniButton, GUILayout.Width(52)))
                {
                    // one list, every entry a Building Template asset: the package's (the prototype's footprints), then the project's
                    var menu = new GenericMenu();
                    var templates = StoreyBuildingTemplate.All();
                    bool any = templates.Any(t => t.Own);
                    if (!any)   // the package's own are missing: the prototype's footprints, from code
                        for (int i = 0; i < ShapeKeys.Length; i++)
                        {
                            string key = ShapeKeys[i], label = ShapeLabels[i];
                            menu.AddItem(new GUIContent(label), false, () => AddBuilding(e, label, d => Buildings.Add(d, key, Near()), true));
                        }
                    bool split = false;
                    foreach (var t in templates)
                    {
                        if (!t.Own && !split) { menu.AddSeparator(""); split = true; }
                        var tb = t.Building!; string tname = t.name; bool own = t.Own;
                        menu.AddItem(new GUIContent(own ? tname : "Project/" + tname), false, () => AddBuilding(e, tname, d => Buildings.FromTemplate(d, tb, Near()), own));
                    }
                    if (!split) { menu.AddSeparator(""); menu.AddDisabledItem(new GUIContent("Project (save a building as a template first)")); }
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
            // its collider (docs/PLAY.md §5): one undo step, and the building is built again without it, or with it
            bool col = EditorGUILayout.Toggle(CollidersLabel, sb.colliders);
            if (col != sb.colliders) e.ApplyTo(col ? "Colliders on" : "Colliders off", bb => { bb.colliders = col; return true; });
        }

        static readonly GUIContent CollidersLabel = new GUIContent("Colliders",
            "The building's MeshCollider, made with its LOD0, at load or in edit mode as Site settings say. Off, it gets none: " +
            "physics and raycasts pass through it, and nothing waits for it. Storey's own play kit still walks it");

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
            bool stale = StoreyProblems.Stale(e);
            using (new EditorGUILayout.HorizontalScope())
            {
                string status =
                    StoreyProblems.Running ? $"Checking for problems … {StoreyProblems.RunningFor:0} s" :
                    !StoreyProblems.Checked(e) ? "Problems: not checked yet" :
                    ps.Count == 0 ? (stale ? "✓ No problems at the last check (edited since)" : "✓ No problems: every storey can be reached") :
                    $"⚠ {ps.Count} problem{(ps.Count == 1 ? "" : "s")}{(errors > 0 ? $", {errors} serious" : "")}{(stale ? " (edited since)" : "")}";
                const string tip = "Rooms and storeys nobody can reach, stairs that are walled in, overlapping buildings";
                if (ps.Count == 0 || StoreyProblems.Running && !StoreyProblems.Checked(e)) EditorGUILayout.LabelField(new GUIContent(status, tip), StoreyInspectorUI.Caption);
                else
                {
                    var keepC = GUI.contentColor; if (errors > 0) GUI.contentColor = Color.Lerp(Color.white, StoreyProblems.ColorOf(ps.First(p => p.severity == Validate.Severity.Error)), 0.6f);
                    bool show = EditorGUILayout.Foldout(v.showProblems, new GUIContent(status, "Click one to go to it"), true);
                    GUI.contentColor = keepC;
                    if (show != v.showProblems) { v.showProblems = show; SceneView.RepaintAll(); }
                }
                using (new EditorGUI.DisabledScope(StoreyProblems.Running || (!stale && StoreyProblems.Checked(e))))
                    if (GUILayout.Button(new GUIContent("Check", "Check for problems now, in the background: the whole layout the first time (a 3,000-building layout takes about 7 s), then only what changed since"), EditorStyles.miniButton, GUILayout.Width(52)))
                        StoreyProblems.CheckNow(e);
                bool want = GUILayout.Toggle(StoreyProblems.Auto, new GUIContent("Auto", "Check again by itself after each edit: only the buildings it changed, with their neighbours and bridges"), EditorStyles.miniButton, GUILayout.Width(40));
                if (want != StoreyProblems.Auto) StoreyProblems.Auto = want;
            }
            if (!v.showProblems || ps.Count == 0) return;
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

        /// <summary>
        /// The building's height in the site (docs/EDITOR.md §6.11): moved up and down with the move gizmo in the Scene view,
        /// or stood on the ground under it.
        /// </summary>
        void Placement(StoreyEdit e, BuildingData b)
        {
            using var c = StoreyInspectorUI.Card("placement", "Placement", StoreyInspectorUI.PlacementIcon, StoreyInspectorUI.Tint.Details,
                b.elevation == 0 ? "On the site's ground" : $"{b.elevation:0.##} m up",
                tip: "Where the building stands in the site: its ground floor's height above the site's ground");
            if (!c.Open) return;
            StoreyInspectorUI.Note("Move the building with the move gizmo in the Scene view; its up arrow raises and lowers it, level with the ground or a neighbour as it passes them (Alt: free).");
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Snap to ground", "Cast down onto the scene's colliders (a Terrain has one) under the building: it stands on the highest ground it finds, and its foundation reaches down to the lowest"), GUILayout.Width(120)))
                    SnapToGround(e, b);
            }
        }

        void SnapToGround(StoreyEdit e, BuildingData b)
        {
            var site = (StoreySite)target; var o = site.transform.position;
            var heights = new List<double>();
            foreach (var p in Ground.Samples(b))
            {
                // from high above, the nearest hit that isn't the site's own (its buildings' colliders)
                var ray = new Ray(new Vector3(o.x + (float)p.x, o.y + 10000f, o.z + (float)p.z), Vector3.down);
                float best = float.PositiveInfinity; double at = 0;
                foreach (var h in Physics.RaycastAll(ray, 20000f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider == null || h.collider.transform.IsChildOf(site.transform) || h.distance >= best) continue;
                    best = h.distance; at = h.point.y - o.y;
                }
                if (!float.IsPositiveInfinity(best)) heights.Add(at);
            }
            double? drop = null;
            if (heights.Count > 0) e.ApplyTo("Snap to ground", bb => (drop = Ground.Snap(bb, heights)) != null);
            message = drop == null ? "No ground found under the building: Snap to ground casts down onto colliders (a Terrain has one), and none were under it."
                : drop > 0.05 ? $"Standing on the highest ground under it, {b.elevation:0.00} m; the foundation reaches {drop:0.00} m further down to the lowest."
                : $"Standing on the ground, {b.elevation:0.00} m.";
        }

        void ShapeTab(StoreyEdit e, BuildingData b)
        {
            var v = e.View; int k0 = v.tier; var ts = Tiers.Of(b); var cur = ts.First(t => t.k0 == k0);
            Placement(e, b);
            using (var c = StoreyInspectorUI.Card("outline", "Outline", StoreyInspectorUI.ShapeIcon, StoreyInspectorUI.Tint.Windows,
                $"{Math.Abs(Geo.Area2(Tiers.Outline(b, k0))) / 2:0} m²" + (ts.Count > 1 ? $" · {ts.Count - 1} setback{(ts.Count > 2 ? "s" : "")}" : ""),
                tip: "The building's footprint, and the setbacks above it that each have their own outline"))
                if (c.Open) Outline(e, b, k0, ts, cur);
            CornerSection(e, b, k0);
            VoidSection(e, b);
            using (var c = StoreyInspectorUI.Card("heights", "Storey heights", StoreyInspectorUI.HeightsIcon, StoreyInspectorUI.Tint.Doors,
                $"{b.floors.Count} storeys · {Derived.RoofY(b):0.0} m tall", tip: "Every storey's height; one storey can differ (Interior tab)"))
                if (c.Open)
                {
                    Slider(e, "Ground", b.groundHeight, 3, 6, 0.1, (bb, x) => bb.groundHeight = x);
                    Slider(e, "Upper", b.floorHeight, 2.7, 4.5, 0.1, (bb, x) => bb.floorHeight = x);
                    StoreyInspectorUI.Note($"{Math.Abs(Geo.Area2(Tiers.Outline(b, k0))) / 2:0} m² per floor · {Derived.RoofY(b):0.0} m tall");
                }
        }

        /// <summary>The outlines: the base's and each setback's, picked to edit, added and removed; a setback's start and its terrace; templates to start from.</summary>
        void Outline(StoreyEdit e, BuildingData b, int k0, List<Tier> ts, Tier cur)
        {
            var v = e.View;
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
                    // what covers the floors below where this setback leaves them uncovered: their roof, set with them in the Facade tab
                    var tr = b.floors[k0].terraceRoof;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel(new GUIContent("Uncovered below", "Where this setback leaves the floors below it uncovered: a terrace, or a roof"));
                        GUILayout.Label(tr == null ? "A terrace" : $"A {(tr.type == RoofType.Gable ? "gable" : "hip")} roof, {tr.pitch:0}°");
                        if (GUILayout.Button(new GUIContent("Roof…", "The Facade tab with the floors below selected: their Terrace roof card"), EditorStyles.miniButton, GUILayout.Width(56)))
                        { v.tier = lo; SetTab(v, StoreyTab.Facade); }
                    }
                }
                EditorGUILayout.HelpBox("Drag corners in the Scene view, or drag an edge's orange bar to push that wall in. The roof it leaves uncovered below is a terrace, or a roof of its own: the floors below's Terrace roof card, in the Facade tab.", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Drag the corners in the Scene view. Drag a + to add a corner, double-click a corner to remove it, drag an orange bar to push a wall, and the move gizmo to move the building. Alt: no snapping.", MessageType.None);
                using (new EditorGUILayout.HorizontalScope())
                {
                    // the outline of a template: the package's as buttons, the project's in a menu
                    EditorGUILayout.PrefixLabel("Start from");
                    var templates = StoreyBuildingTemplate.All();
                    if (!templates.Any(t => t.Own))   // the package's own are missing: the prototype's footprints, from code
                        for (int i = 0; i < ShapeKeys.Length; i++)
                            if (GUILayout.Button(ShapeLabels[i], EditorStyles.miniButton)) StartFrom(e, b, Setbacks.Preset(ShapeKeys[i]), null);
                    foreach (var t in templates.Where(t => t.Own))
                        if (GUILayout.Button(t.name, EditorStyles.miniButton)) { var tb = t.Building!; StartFrom(e, b, tb.footprint, tb.corners); }
                    var mine = templates.Where(t => !t.Own).ToList();
                    if (mine.Count > 0 && GUILayout.Button(new GUIContent("Project ▾", "The outline of one of the project's building templates"), EditorStyles.miniButton))
                    {
                        var menu = new GenericMenu();
                        foreach (var t in mine) { var tb = t.Building!; menu.AddItem(new GUIContent(t.name), false, () => StartFrom(e, b, tb.footprint, tb.corners)); }
                        menu.ShowAsContext();
                    }
                }
            }
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
            using var card = StoreyInspectorUI.Card("corners", "Corners", StoreyInspectorUI.CornerIcon, StoreyInspectorUI.Tint.Style,
                cuts.Count == 0 ? "All sharp" : $"{cuts.Count} cut", tip: "Click a corner in the Scene view to cut it, or to change its cut");
            if (!card.Open) return;
            // a cut corner shows its own cut, and changes as it is set; otherwise these set the next cut
            var shape = cut?.shape ?? v.cornerShape; float size = cut != null ? (float)cut.size : v.cornerSize; bool door = cut?.door ?? v.cornerDoor;
            EditorGUI.BeginChangeCheck();
            shape = (CornerShape)StoreyInspectorUI.Segmented(new GUIContent("Cut", "How a corner is cut"), (int)shape, CornerShapes);
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

        static readonly GUIContent[] CornerShapes = { new GUIContent("Chamfer", "Cut the corner off straight"), new GUIContent("Round", "Round the corner off in an arc") };

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
            using var c = StoreyInspectorUI.Card("voids", "Courtyards and atria", StoreyInspectorUI.CourtyardIcon, StoreyInspectorUI.Tint.Roof,
                b.voids.Count == 0 ? "None" : b.voids.Count.ToString(), open: b.voids.Count > 0, tip: "Open space inside the building: a courtyard open to the sky, or an atrium under a skylight");
            if (!c.Open) return;
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
                int from;
                using (new EditorGUI.IndentLevelScope()) from = EditorGUILayout.Popup(vd.kind == VoidKind.Courtyard ? "Paved on" : "Floor on", Math.Min(vd.bottom, N - 1), floors);
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
                int sel = GUI.Toolbar(EditorGUILayout.GetControlRect(), ts.FindIndex(t => t.k0 == k0), ts.Select(t => $"{(t.k0 > 0 ? "Setback" : "Base")} {Range(b, t)}").ToArray());
                if (sel >= 0 && ts[sel].k0 != k0) { v.tier = ts[sel].k0; k0 = v.tier; }
            }
            bool own = Styles.HasOwn(b, k0);
            var target = new SiteStyle(this, e, b, k0);
            FacadeStyle? st = null;
            if (!own)
            {
                EditorGUILayout.HelpBox("This setback looks like the floors below it.", MessageType.None);
                if (GUILayout.Button("Give it its own style")) e.ApplyTo("The setback has its own style now", bb => { Styles.GiveOwn(bb, k0); return true; });
            }
            else
            {
                st = Styles.Edited(b, k0);
                if (k0 > 0 && GUILayout.Button(new GUIContent("Match the floors below", "Drop this setback's style and follow the floors below again"))) e.ApplyTo("The setback matches the floors below again", bb => { Styles.MatchBelow(bb, k0); return true; });
                GUILayout.Space(4);
                using (var c = StoreyInspectorUI.Card("style", "Style", StoreyInspectorUI.StyleIcon, StoreyInspectorUI.Tint.Style, FacadeStylePreset.Of(st)?.asset.name ?? "Custom",
                    tip: "A preset sets everything below at once"))
                    if (c.Open)
                    {
                        Presets(e, b, k0, st);
                        StoreyInspectorUI.Note("Start from a preset. Changing anything below makes the style this building's own.");
                    }
                StoreyStyleCards.WindowsCard(target, st);
                if (k0 == 0) StoreyStyleCards.DoorsCard(target, st);
                StoreyStyleCards.WallsCard(target, st);
                StoreyStyleCards.ColoursCard(target, st);
                if (Styles.DrivesRoof(b, k0)) StoreyStyleCards.RoofCard(target, st);
            }
            // the roof over these floors where the setback above leaves them uncovered: whatever their style
            TerraceRoofCard(e, b, k0, st);
            StoreyStyleCards.DetailsCard(target, st);
            BridgeList(e, b);
        }

        /// <summary>
        /// The style the Facade tab edits for the building's tier (<see cref="Styles.Edited"/>), as the style cards edit it
        /// (<see cref="StoreyStyleCards"/>): each change one undo step of the layout's edit, and a window, door or detail it
        /// picks kept in the site's list. Its walls take doors and details placed by hand, with the Scene view's Facade tool.
        /// </summary>
        sealed class SiteStyle : StyleTarget
        {
            readonly StoreySiteEditor owner;
            readonly StoreyEdit e;
            readonly BuildingData b;
            readonly int k0;

            public SiteStyle(StoreySiteEditor owner, StoreyEdit e, BuildingData b, int k0) { this.owner = owner; this.e = e; this.b = b; this.k0 = k0; }

            public override FacadeStyle Style => Styles.Edited(e.Selected ?? b, k0);
            public override bool Base => k0 == 0;
            public override string Key => $"{b.id}/{k0}";
            public override void Edit(string undoName, Action<FacadeStyle> op) => e.ApplyTo(undoName, bb => { op(Styles.Edited(bb, k0)); return true; });
            public override PaletteEntry? Lookup(string id) => e.Document.palette.TryGetValue(id, out var pe) ? pe : null;
            public override void Keep(StoreyOpening o) => StoreyOpenings.Keep(e.Site, o);
            public override void KeepDetail(string id) => StoreyDetails.Keep(e.Site, id);
            public override void Say(string? message) => owner.message = message;
            public override bool RoofOnTop => Tiers.Of(b).Last().k0 != k0;
            public override string DoorsPlaced => b.entrances.Count == 1 ? " · 1 placed" : $" · {b.entrances.Count} placed";
            public override int DetailsByHand => b.details.Count;
            public override void PlaceByHand() => owner.PlaceByHand(e, b, k0);

            public override void DoorsOnWalls(FacadeStyle st)
            {
                StoreyInspectorUI.SubHeader("On the walls", "Put doors in, and take them out, in the Scene view");
                owner.PlaceDoors(e, st);
            }
        }

        static readonly GUIContent PlaceDoorsLabel = new GUIContent("Place doors", "Click a wall in the Scene view to put a door in it, and a door to take it out");
        static readonly GUIContent DoorToPlaceLabel = new GUIContent("Door to place",
            "Which of the style's doors a click puts in. Any of them, by weight: one picked for its wall as the style picks, which changes when the list does");

        /// <summary>
        /// Place doors (docs/EDITOR.md §6.7): the Facade tool that puts a door in the wall clicked, or takes out the one clicked
        /// (<see cref="Facades.ToggleEntrance"/>), at the street or onto a terrace or a neighbour's roof; and, where the style
        /// has several doors, which one it puts in.
        /// </summary>
        void PlaceDoors(StoreyEdit e, FacadeStyle st)
        {
            var v = e.View; bool was = v.facadeTool == "entrance";
            bool on = GUILayout.Toggle(was, PlaceDoorsLabel, GUI.skin.button, GUILayout.Height(22));
            if (on != was) { v.facadeTool = on ? "entrance" : ""; if (on) StoreyToolContext.Show(StoreyTab.Facade); SceneView.RepaintAll(); }
            var choices = DoorChoices(st);
            if (choices.Count > 1)
            {
                int cur = Math.Max(0, choices.FindIndex(x => x.value == v.placeDoor));
                int pick = EditorGUILayout.Popup(DoorToPlaceLabel, cur, choices.Select(x => new GUIContent(x.label)).ToArray());
                if (pick != cur) { v.placeDoor = choices[pick].value; SceneView.RepaintAll(); }
            }
            if (on) StoreyInspectorUI.Note("Click a wall to put a door in it, and a door to take it out. Above the street, where a terrace or a neighbour's roof is in front of the wall.");
        }

        /// <summary>The doors Place doors can put in: the style's, by name (the built-in one as <see cref="EntranceData.Plain"/>), and with more than one, any of them by weight ("*").</summary>
        static List<(string value, string label)> DoorChoices(FacadeStyle st)
        {
            var o = new List<(string value, string label)>();
            foreach (var en in WindowSets.Shown(st, SetOf.Doors)!)
            {
                string value = en.kind ?? EntranceData.Plain;
                if (!en.none && !o.Exists(x => x.value == value)) o.Add((value, DoorName(value)));
            }
            if (o.Count > 1) o.Add(("*", "Any of them, by weight"));
            return o;
        }

        /// <summary>
        /// The door Place doors puts in, as an entrance names it (<see cref="EntranceData.door"/>): the one picked in the Doors
        /// card, or the list's first; none (the style's pick) for "Any of them", or where the style has one door.
        /// </summary>
        internal static string? DoorToPlace(StoreyEditView v, FacadeStyle st)
        {
            var choices = DoorChoices(st);
            if (choices.Count <= 1) return null;
            string value = choices.Exists(x => x.value == v.placeDoor) ? v.placeDoor : choices[0].value;
            return value == "*" ? null : value;
        }

        /// <summary>A door as an entrance names it, by the words that tell it from the project's other doors: none is the style's own pick.</summary>
        internal static string DoorName(string? door)
        {
            if (door == null) return "the style's door";
            if (door == EntranceData.Plain) return "the built-in door";
            var doors = StoreyOpenings.All().Where(o => o.door).ToList();
            if (!(doors.FirstOrDefault(o => o.Id == door) is { } asset)) return "a door not in the project";
            return StoreyInspectorUI.ShortNames(doors.Select(o => o.name).ToList()).TryGetValue(asset.name, out var sn) ? sn : asset.name;
        }

        /// <summary>What Place doors puts in, by name, for the Scene view: the door picked, the style's one door, or any of its doors.</summary>
        internal static string PlaceName(StoreyEditView v, FacadeStyle st)
        {
            if (DoorToPlace(v, st) is string door) return DoorName(door);
            var choices = DoorChoices(st);
            return choices.Count == 1 ? choices[0].label : "one of the style's doors";
        }

        /// <summary>The door an entrance has, by name, for the Scene view: the one it names, or the one its style picks for its wall.</summary>
        internal static string PlacedName(BuildingData b, EntranceData en)
        {
            if (en.door != null && (en.door == EntranceData.Plain || OpeningKinds.Get(en.door) != null)) return DoorName(en.door);
            var kind = OpeningSets.DoorAt(Derived.StyleAt(b, en.k), b.id, en.k, en.edge, out _);
            return kind != null ? DoorName(kind.id) : "the built-in door";
        }

        /// <summary>
        /// The setback starting on the floor above tier <paramref name="k0"/>'s top, where it leaves these floors' roof uncovered (a
        /// terrace, or a roof over it); -1 where nothing is uncovered (the top tier, or a setback flush with them).
        /// </summary>
        static int TerraceAbove(BuildingData b, int k0)
        {
            var t = Tiers.Of(b).Find(x => x.k0 == k0);
            return t.k1 < b.floors.Count && Geo.TerracePolys(Derived.OutlineAt(b, t.k1 - 1), Derived.OutlineAt(b, t.k1)).Count > 0 ? t.k1 : -1;
        }

        // a hip or a gable: one shed slope over the whole outline below towers over the setback, and a mansard's steep slope hides it
        static readonly RoofType[] TerraceRoofTypes = { RoofType.Hip, RoofType.Gable };
        static readonly GUIContent TerraceRoofLabel = new GUIContent("Type", "What covers these floors where the setback above leaves them uncovered");
        static readonly GUIContent[] TerraceRoofChoices =
        {
            new GUIContent("Terrace", "Flat, with a parapet round it: walk out onto it through a door (Doors ▸ Place doors)"),
            new GUIContent("Hip", "Sloping up from every eave to the walls above"),
            new GUIContent("Gable", "Sloping up from two sides, with a gable at each end"),
        };

        /// <summary>
        /// The roof over these floors where the setback above leaves them uncovered (docs/SPEC.md §4.1): a terrace to walk on, or
        /// a roof of its own shape and pitch, with this style's colour and surface. Its own card, whichever style drives the roof
        /// on top: the Facade tab with these floors selected is where their roof is.
        /// </summary>
        void TerraceRoofCard(StoreyEdit e, BuildingData b, int k0, FacadeStyle? st)
        {
            int k1 = TerraceAbove(b, k0); if (k1 < 0) return;
            var tr = b.floors[k1].terraceRoof;
            int cur = tr == null ? 0 : tr.type == RoofType.Gable ? 2 : 1;
            string summary = tr == null ? "Terrace" : $"{TerraceRoofChoices[cur].text}, {tr.pitch:0}°" + (st?.roofSurface != null ? " · " + StoreyStyleCards.SurfaceName(st) : "");
            using var c = StoreyInspectorUI.Card("terraceRoof", "Terrace roof", StoreyInspectorUI.RoofIcon, StoreyInspectorUI.Tint.Roof, summary,
                tip: $"Where the setback above ({Floor(b, k1)} and up) leaves these floors uncovered: a terrace, or a roof");
            if (!c.Open) return;
            int pick = StoreyInspectorUI.Segmented(TerraceRoofLabel, cur, TerraceRoofChoices);
            if (pick != cur) e.ApplyTo(pick == 0 ? "The roof below is a terrace again" : $"The terrace below is now a {TerraceRoofChoices[pick].text.ToLowerInvariant()} roof", bb =>
            {
                Facades.SetTerraceRoof(bb, k1, pick > 0);
                if (pick > 0) bb.floors[k1].terraceRoof!.type = TerraceRoofTypes[pick - 1];
                return true;
            });
            if (tr == null) { StoreyInspectorUI.Note("A terrace with a parapet round it. Put a door onto it from the setback with Doors ▸ Place doors."); return; }
            Slider(e, "Pitch", tr.pitch, 10, 50, 1, (bb, x) => bb.floors[k1].terraceRoof!.pitch = x);
            if (st != null) StoreyStyleCards.RoofSurfaceFields(new SiteStyle(this, e, b, k0), st);
            else StoreyInspectorUI.Note("Its colour and surface are these floors' style's, which is the floors below's: give them their own style to choose them.");
        }

        /// <summary>
        /// The details card's Place by hand (docs/EDITOR.md §6.12): Blank wall, every Facade Detail and Bridge, each a tool for
        /// the Scene view's Facade tool, as many to a row as fit; then the picked tool's options, or a line about it.
        /// </summary>
        void PlaceByHand(StoreyEdit e, BuildingData b, int k0)
        {
            var v = e.View;
            StoreyInspectorUI.SubHeader("Place by hand", "Pick one, then click a wall in the Scene view; click a placed one to remove it");
            var tools = new List<(string tool, string label, string tip)>
            {
                ("blank", "Blank wall", "Take the windows (and more) off a wall, or a whole side"),
            };
            var palette = StoreyDetails.Palette();
            var shortName = StoreyInspectorUI.ShortNames(palette.Where(x => !DetailKinds.IsBuiltIn(x.id)).Select(x => x.name).ToList());
            tools.AddRange(palette.Select(x => (x.id, shortName.TryGetValue(x.name, out var sn) ? sn : x.name, x.name)));
            tools.Add(("bridge", "Bridge", "A bridge from an upper floor to the building facing it"));
            // as many to a row as fit, each as wide as the others, so the card is never wider than the inspector
            float avail = EditorGUIUtility.currentViewWidth - 52; int cols = Math.Max(2, Math.Min(4, (int)(avail / 96)));
            for (int row = 0; row < tools.Count; row += cols)
            {
                var r = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
                float w = (r.width - (cols - 1) * 3) / cols;
                for (int i = 0; i < cols && row + i < tools.Count; i++)
                {
                    var (tool, label, tip) = tools[row + i];
                    bool was = v.facadeTool == tool;
                    bool on = GUI.Toggle(new Rect(r.x + i * (w + 3), r.y, w, 20), was, new GUIContent(StoreyInspectorUI.Fit(label, w - 8, EditorStyles.miniButton), tip), EditorStyles.miniButton);
                    if (on != was) { v.facadeTool = on ? tool : ""; if (on) StoreyToolContext.Show(StoreyTab.Facade); SceneView.RepaintAll(); }
                }
            }
            if (v.facadeTool == "blank") WallStates(e, b, k0);
            else if (v.facadeTool == "bridge") StoreyInspectorUI.Note("Click an upper floor's wall: a bridge goes straight out to the building facing it, to its floor nearest this one. Click a bridge's door to remove it.");
            else if (v.facadeTool.Length > 0) StoreyInspectorUI.Note("Click a wall to place it; click a placed one to remove it.");
        }

        /// <summary>What the Blank wall tool makes a wall, as its toolbar names them (<see cref="EdgeState"/> from Blank).</summary>
        internal static readonly string[] WallStateNames = { "Blank wall", "Quiet wall", "Bare wall" };

        static readonly GUIContent[] WallStateChoices =
        {
            new GUIContent("No windows", "Blank: no windows (doors you placed stay); bands, plinth and details stay"),
            new GUIContent("No details", "Quiet: no windows and no automatic details; bands and plinth stay"),
            new GUIContent("Bare", "Bare: a plain wall: no windows, no automatic details, no floor bands or plinth (the foundation and parapet stay)"),
        };

        /// <summary>
        /// The Blank wall tool's options (docs/EDITOR.md §6.7): what a click makes a wall (blank, quiet or bare; a click on a
        /// wall that is so already brings its windows back), and every wall of a side at once.
        /// </summary>
        void WallStates(StoreyEdit e, BuildingData b, int k0)
        {
            var v = e.View;
            int cur = Math.Max(0, (int)v.wallState - 1), pick = GUI.Toolbar(EditorGUILayout.GetControlRect(), cur, WallStateChoices, EditorStyles.miniButton);
            if (pick != cur) v.wallState = (EdgeState)(pick + 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Whole side", "Every wall of this outline facing that way (by its outward normal, in the building's axes: front +Z, right +X)"), GUILayout.Width(EditorGUIUtility.labelWidth - 4));
                foreach (var side in new[] { Side.Front, Side.Back, Side.Left, Side.Right })
                    if (GUILayout.Button(side.ToString(), EditorStyles.miniButton, GUILayout.MinWidth(30)))
                    {
                        var s = v.wallState; int n = 0;
                        e.ApplyTo($"{side} walls {WallStateNames[(int)s - 1].ToLowerInvariant()}", bb => (n = Facades.SetSide(bb, k0, side, s)) > 0);
                        if (n == 0) message = $"Every {side.ToString().ToLowerInvariant()} wall already has that, or more.";
                    }
            }
            StoreyInspectorUI.Note("Click a wall to make it so; click it again for its windows back.");
        }

        /// <summary>The bridges from and to this building: enclosed or open, their width, and removing them.</summary>
        void BridgeList(StoreyEdit e, BuildingData b)
        {
            var site = new Site(e.Document.buildings);
            var spans = Bridges.Touching(site, b);
            var dangling = b.bridges.Where(x => Bridges.Span(site, b, x) == null).ToList();
            if (spans.Count == 0 && dangling.Count == 0) return;
            int n = spans.Count + dangling.Count;
            using var c = StoreyInspectorUI.Card("bridges", "Bridges", StoreyInspectorUI.BridgeIcon, StoreyInspectorUI.Tint.Details, n == 1 ? "1 bridge" : $"{n} bridges",
                tip: "Bridges from and to this building: add one with Bridge, under Details");
            if (!c.Open) return;
            foreach (var s in spans)
            {
                bool own = ReferenceEquals(s.A, b); var other = own ? s.B : s.A; string owner = s.A.id, id = s.br.id;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{Floor(b, own ? s.br.k : s.br.toK)} ↔ {other.name}, {Floor(other, own ? s.br.toK : s.br.k)} · {s.L:0.0} m", GUILayout.MinWidth(60));
                    int kind = GUILayout.Toolbar(s.br.open ? 1 : 0, new[] { "Enclosed", "Open" }, EditorStyles.miniButton, GUILayout.Width(120));
                    if (kind != (s.br.open ? 1 : 0)) e.Apply(kind == 1 ? "Bridge opened" : "Bridge enclosed", d => { var br = d.buildings.First(x => x.id == owner).bridges.First(x => x.id == id); br.open = kind == 1; return true; });
                    if (GUILayout.Button(new GUIContent("×", "Remove the bridge"), EditorStyles.miniButton, GUILayout.Width(22))) { e.Apply("Bridge removed", d => BridgeEdits.Remove(d.buildings.First(x => x.id == owner), id)); return; }
                }
                using (new EditorGUI.IndentLevelScope())
                {
                    EditorGUI.BeginChangeCheck();
                    float w = EditorGUILayout.Slider("Width", (float)s.br.width, 1.5f, 4f);
                    if (EditorGUI.EndChangeCheck()) e.Apply("Bridge width", d => { d.buildings.First(x => x.id == owner).bridges.First(x => x.id == id).width = Math.Round(w * 10) / 10; return true; });
                }
            }
            foreach (var br in dangling)
                using (new EditorGUILayout.HorizontalScope())
                {
                    string id = br.id, bid = b.id;
                    EditorGUILayout.HelpBox($"A bridge from {Floor(b, br.k)} no longer meets {site.ById(br.to)?.name ?? "the building it went to"}.", MessageType.Warning);
                    if (GUILayout.Button("Remove", GUILayout.Width(64))) { e.Apply("Bridge removed", d => BridgeEdits.Remove(d.buildings.First(x => x.id == bid), id)); return; }
                }
        }

        static Vec2 Near()
        {
            var p = SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.pivot : Vector3.zero;
            return new Vec2(p.x, p.z);
        }

        /// <summary>
        /// A new building from a template (<paramref name="make"/> places it), selected, on the Shape tab. One of the
        /// package's own (<paramref name="starter"/>) takes the package's Brick walk-up as it is now, as New's buildings
        /// always have, should that preset have been edited.
        /// </summary>
        void AddBuilding(StoreyEdit e, string name, Func<StoreyDocument, BuildingData> make, bool starter)
        {
            string id = "";
            var start = starter ? FacadeStylePreset.Default() : null;
            e.Apply("Added a building from " + name, d =>
            {
                var nb = make(d);
                if (start is (FacadeStyle sty, string sk)) Styles.Apply(nb, 0, sty, sk);
                id = nb.id; return true;
            });
            e.View.Select(id); SetTab(e.View, StoreyTab.Shape);
        }

        /// <summary>The Shape tab's Start from: the footprint (and its cut corners) of a template's building.</summary>
        void StartFrom(StoreyEdit e, BuildingData b, IReadOnlyList<Vec2> footprint, IEnumerable<CornerData>? corners)
        {
            int had = Tiers.Of(b).Count;
            e.ApplyTo("Footprint replaced", bb => { Setbacks.ApplyFootprint(bb, footprint, corners); return true; });
            message = e.Selected != null && Tiers.Of(e.Selected).Count < had ? "Setbacks that no longer fit were removed." : null;
        }

        void Presets(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            // one list, every preset a Facade Style asset: the package's, then the project's; "Custom" when the style carries
            // no preset's key (a field of it edited by hand since, or it never came from one)
            var all = FacadeStylePreset.All();
            var cur = FacadeStylePreset.Of(st);
            var items = new List<(string name, int i)>();   // i: the preset, -1 for Custom and the separators
            if (cur == null) { items.Add(("Custom", -1)); items.Add(("", -1)); }
            for (int i = 0; i < all.Count; i++)
            {
                if (i > 0 && all[i].own != all[i - 1].own) items.Add(("", -1));
                items.Add(((all[i].own ? "" : "Project/") + all[i].asset.name, i));
            }
            int shown = cur == null ? 0 : items.FindIndex(x => x.i >= 0 && all[x.i] == cur);
            using (new EditorGUILayout.HorizontalScope())
            {
                int pick = EditorGUILayout.Popup("Preset", shown, items.Select(x => x.name).ToArray());
                if (pick != shown && pick >= 0 && items[pick].i >= 0)
                {
                    var p = all[items[pick].i]; FacadeStyle? style = null;
                    try { style = p.asset.Style; }
                    catch (Exception ex) { Debug.LogWarning($"Storey: the Facade Style \"{p.asset.name}\" holds no style it can read ({ex.Message}).", p.asset); }
                    if (style != null) e.ApplyTo(p.asset.name + " applied", bb => { Styles.Apply(bb, k0, style, p.key); return true; });
                }
                if (GUILayout.Button(new GUIContent("Save as preset…", "Keep this style as a Facade Style asset, shared with every building, in the list with the package's"), EditorStyles.miniButton))
                {
                    string path = EditorUtility.SaveFilePanelInProject("Facade style", st.label.Length > 0 ? st.label : "Facade Style", "asset", "Where the style preset goes");
                    if (!string.IsNullOrEmpty(path))
                    {
                        string name = System.IO.Path.GetFileNameWithoutExtension(path);
                        var p = CreateInstance<FacadeStylePreset>(); p.Set(st, name);
                        AssetDatabase.CreateAsset(p, path); AssetDatabase.SaveAssets();
                        // the style is the new preset's now, named as it is, as one applied from it would be
                        string key = AssetDatabase.AssetPathToGUID(path);
                        if (key.Length > 0) e.ApplyTo("Saved as " + name, bb => { var s = Styles.Edited(bb, k0); s.label = name; s.preset = key; return true; });
                    }
                }
            }
        }

        // ---- Interior ----

        void InteriorTab(StoreyEdit e, BuildingData b)
        {
            var v = e.View; int k = v.floor, N = b.floors.Count;
            using (var c = StoreyInspectorUI.Card("inside", "Inside", StoreyInspectorUI.InteriorIcon, StoreyInspectorUI.Tint.Windows, b.interior ? "Walk-in" : "Shell only",
                tip: "Walk-in buildings have floors, rooms and stairs; shells are facades only"))
                if (c.Open)
                {
                    int shell = GUI.Toolbar(EditorGUILayout.GetControlRect(), b.interior ? 0 : 1, new[] { "Walk-in interior", "Shell only" });
                    if ((shell == 1) == b.interior) e.ApplyTo(shell == 1 ? "Shell only: facade with no interior" : "Walk-in interior restored", bb => { bb.interior = shell == 0; return true; });
                    if (!b.interior) StoreyInspectorUI.Note("Facade only: windows are opaque, no floors or rooms inside, and the player can't enter. Use it for background blocks. The floor layouts are kept if you switch back.");
                    else
                    {
                        StoreyInspectorUI.SubHeader("Colours inside", "The walls and floors inside, for the whole building");
                        StoreyStyleCards.Colour(new SiteStyle(this, e, b, 0), "Walls", "interior");
                        StoreyStyleCards.Colour(new SiteStyle(this, e, b, 0), "Floors", "floor");
                    }
                }
            if (!b.interior) return;
            using (var c = StoreyInspectorUI.Card("floor", "Floor", StoreyInspectorUI.FloorPlanIcon, StoreyInspectorUI.Tint.Doors,
                Floor(b, k) + (k < N && b.floors[k].filled ? " · filled" : ""), tip: "The storey being edited; the Storey Floors overlay in the Scene view picks it too"))
            {
                if (c.Open) k = FloorSettings(e, b, k);
            }
            if (k == N || b.floors[k].filled) return;
            using (var c = StoreyInspectorUI.Card("tools", "Tools", StoreyInspectorUI.StairsIcon, StoreyInspectorUI.Tint.Style, InteriorTools[(int)v.interiorTool].label,
                tip: "Pick one, then work in the Scene view"))
                if (c.Open) Tools(e, b, k);
        }

        /// <summary>The storey being edited (picked here or in the Scene view's overlay), how its walls show, rooms or filled, its height; on the roof, which stairs and lifts reach it. Returns the storey picked.</summary>
        int FloorSettings(StoreyEdit e, BuildingData b, int k)
        {
            var v = e.View; int N = b.floors.Count;
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
            if (k == N)
            {
                if (Roofs.IsPitched(b)) { StoreyInspectorUI.Note("A pitched roof has no roof access. Switch the roof to Flat in the Facade tab to put stairs up here."); return k; }
                if (b.shafts.Count == 0) StoreyInspectorUI.Note("No stairs or lifts yet. Pick a floor in the Storey Floors overlay, then place stairs.");
                for (int i = 0; i < b.shafts.Count; i++)
                {
                    var s = b.shafts[i]; bool reach = Derived.ShaftTop(b, s) == N - 1; string id = s.id;
                    using (new EditorGUI.DisabledScope(!reach))
                    {
                        bool roof = EditorGUILayout.Toggle($"{(s.type == CoreType.Lift ? "Lift" : "Stairs")} {i + 1} reaches the roof", s.roof);
                        if (roof != s.roof) e.ApplyTo(roof ? "Now reaches the roof" : "Stops at the top floor", bb => { bb.shafts.First(x => x.id == id).roof = roof; return true; });
                    }
                }
                return k;
            }
            int fill = StoreyInspectorUI.Segmented(new GUIContent("Inside", "Rooms to walk in, or nothing"), b.floors[k].filled ? 1 : 0, new[] { new GUIContent("Rooms", "A storey to walk in: walls, doors, stairs and lift stops"), new GUIContent("Filled", "Nothing inside: opaque windows and closed doors, no rooms or stairs; lifts pass through without stopping") });
            if ((fill == 1) != b.floors[k].filled)
            {
                bool to = fill == 1; int kk = k;
                e.ApplyTo(to ? $"{Floor(b, k)} filled" : $"{Floor(b, k)} opened", bb => { bb.floors[kk].filled = to; return true; });
                v.selectedCore = ""; v.selectedWall = -1;
            }
            if (b.floors[k].filled)
                StoreyInspectorUI.Note("Filled: this storey has nothing inside. Its windows are opaque and its doors closed. Stairs stop at the storeys on either side, and lifts pass through without stopping. The rooms drawn here are kept if you switch back.");
            using (new EditorGUILayout.HorizontalScope())
            {
                Slider(e, "Height", Derived.FloorH(b, k), 2.4, 8, 0.1, (bb, x) => Floors.SetHeight(bb, k, x));
                if (b.floors[k].h.HasValue && GUILayout.Button(new GUIContent("Default", $"Use the building's {(k == 0 ? b.groundHeight : b.floorHeight):0.0} m"), EditorStyles.miniButton, GUILayout.Width(56)))
                    e.ApplyTo("Storey uses the default height", bb => { Floors.SetHeight(bb, k, null); return true; });
            }
            return k;
        }

        /// <summary>The interior tools (pick one, then work in the Scene view), their options, and what is selected with them.</summary>
        void Tools(StoreyEdit e, BuildingData b, int k)
        {
            var v = e.View;
            int tool = StoreyInspectorUI.ToolBar((int)v.interiorTool, InteriorTools);
            if (tool != (int)v.interiorTool) { v.interiorTool = (InteriorTool)tool; SceneView.RepaintAll(); }
            StoreyInspectorUI.Note(ToolHint(v.interiorTool));
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
                    EditorGUILayout.LabelField($"Selected wall · {Tiers.Hypot(w.b.x - w.a.x, w.b.z - w.a.z):0.00} m");
                    if (GUILayout.Button("Remove", EditorStyles.miniButton)) { e.ApplyTo("Removed", bb => { bb.floors[k].walls.RemoveAt(wi); return true; }); v.selectedWall = -1; }
                }
            }
        }

        void CoreInspector(StoreyEdit e, BuildingData b, CoreData s)
        {
            int N = b.floors.Count; string id = s.id;
            StoreyInspectorUI.SubHeader(s.type == CoreType.Lift ? "Selected lift" : "Selected stairs");
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

        static void Slider(StoreyEdit e, string label, double value, double min, double max, double step, Action<BuildingData, double> set)
        {
            EditorGUI.BeginChangeCheck();
            double x = Math.Round(EditorGUILayout.Slider(label, (float)value, (float)min, (float)max) / step) * step;
            if (EditorGUI.EndChangeCheck() && x != value) e.ApplyTo(label, bb => { set(bb, Math.Round(x, 3)); return true; });
        }

        static string Floor(BuildingData b, int k) => k == b.floors.Count ? "Roof" : k == 0 ? "Ground" : "Floor " + k;
        static string Short(BuildingData b, int k) => k == b.floors.Count ? "R" : k == 0 ? "G" : k.ToString();
        static string Range(BuildingData b, Tier t) => t.k1 - 1 == t.k0 ? Floor(b, t.k0) : $"{Short(b, t.k0)}–{Short(b, t.k1 - 1)}";
    }
}
