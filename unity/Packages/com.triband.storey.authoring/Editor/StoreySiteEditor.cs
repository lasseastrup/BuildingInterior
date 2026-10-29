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
                WindowsCard(e, b, k0, st);
                if (k0 == 0) DoorsCard(e, b, k0, st);
                WallsCard(e, b, k0, st);
                ColoursCard(e, b, k0, st);
                if (Styles.DrivesRoof(b, k0)) RoofCard(e, b, k0, st);
            }
            DetailsCard(e, b, k0, st);
            BridgeList(e, b);
        }

        // ---- the Facade tab's cards ----

        void WindowsCard(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            OpeningSets.Uses(st, out bool generated, out _, out bool anyOwnSize);
            bool ownSize = !generated && anyOwnSize;
            string summary = st.windows == WindowType.None ? "None"
                : $"{(ownSize ? "Own size" : OpeningName(st.windows))} · bays of {st.bay:0.##} m";
            using var c = StoreyInspectorUI.Card("windows", "Windows", StoreyInspectorUI.WindowFrontIcon, StoreyInspectorUI.Tint.Windows, summary,
                tip: "What fills the walls' openings, how they're spaced, and how they sit in the wall");
            if (!c.Open) return;
            SetRows(e, k0, st, SetOf.Windows, "Window");
            Variety(e, k0, st);

            StoreyInspectorUI.SubHeader("Size and spacing");
            Openings(e, k0, st, ownSize, anyOwnSize);
            // Window width sizes Punched and Tall holes alone: Ribbon and Curtain span the bay, and a window of its own size is its own width
            if (!ownSize && st.windows is WindowType.Punched or WindowType.Tall)
                StyleSlider(e, k0, "Window width", st.winW, 0.6, 2.4, 0.1, (s, x) => s.winW = x);
            if (st.windows != WindowType.None)
            {
                BayFits(e, k0, st);
                StyleSlider(e, k0, "Bay width", st.bay, 1.4, 5, 0.1, (s, x) => s.bay = x);
            }

            if (k0 == 0)
            {
                StoreyInspectorUI.SubHeader("Ground floor");
                int g = EditorGUILayout.Popup(GroundLabel, (int)st.ground, GroundChoices);
                if (g != (int)st.ground) e.ApplyTo("Ground floor", bb => { Styles.Edited(bb, 0).ground = (GroundType)g; return true; });
                GroundRows(e, st);
            }

            StoreyInspectorUI.SubHeader("Depth in the wall");
            Recess(e, k0, st, "Windows", "Windows sit back from the wall's face, this deep: the built-in window's glass and frame, or yours pushed back that far. Off: the built-in window sits 16.5 cm in, and yours at the wall's face",
                st.recess, 0.25, (s, x) => s.recess = x);

            if (generated)
            {
                StoreyInspectorUI.SubHeader("Built-in window");
                WindowDetails(e, b, k0, st);
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
        static void Variety(StoreyEdit e, int k0, FacadeStyle st)
        {
            if (!(st.windowSet is { Count: > 1 } || st.groundSet is { Count: > 1 } || st.doorSet is { Count: > 1 })) return;
            int x = EditorGUILayout.Popup(VarietyLabel, (int)st.windowPick, VarietyChoices);
            if (x != (int)st.windowPick) e.ApplyTo("Variety: " + VarietyChoices[x].text, bb => { Styles.Edited(bb, k0).windowPick = (WindowPick)x; return true; });
        }

        void DoorsCard(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            int placed = b.entrances.Count;
            string summary = (st.doorSet is { Count: > 1 } ? $"{st.doorSet.Count} kinds" : st.doorKind != null ? "Your door" : "Built-in")
                + (placed == 1 ? " · 1 placed" : $" · {placed} placed") + (st.doorRecess is double dr ? $" · set in {dr * 100:0} cm" : "");
            using var c = StoreyInspectorUI.Card("doors", "Doors", StoreyInspectorUI.DoorFrontIcon, StoreyInspectorUI.Tint.Doors, summary,
                tip: "The building's doors: which door its style has (or several), and where they are on its walls");
            if (!c.Open) return;
            SetRows(e, k0, st, SetOf.Doors, "Door");
            Colour(e, b, k0, "Colour", "door", optional: true);
            StoreyInspectorUI.SubHeader("On the walls", "Put doors in, and take them out, in the Scene view");
            PlaceDoors(e, st);
            StoreyInspectorUI.SubHeader("Depth in the wall");
            Recess(e, k0, st, "Doors", "Doors sit back from the wall's face, this deep: the built-in door's panel, or yours pushed back that far; a set-in door's threshold takes the foundation's colour. Off: the built-in door sits at its usual depth, and yours at the wall's face",
                st.doorRecess, 0.29, (s, x) => s.doorRecess = x);
            if (!st.doorRecess.HasValue || !OwnDoors(st)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                StyleSlider(e, k0, OpeningMarginLabel, st.doorRecessOffset ?? 0, -0.5, 1, 0.01, (s, x) => s.doorRecessOffset = Math.Abs(x) < 1e-4 ? (double?)null : x);
                StoreyInspectorUI.Note((st.doorRecessOffset ?? 0) < 0 ? "The opening is smaller than the door: its frame stays hidden in the wall."
                    : (st.doorRecessOffset ?? 0) > 0 ? "The opening is bigger than the door: the wall shows round it." : "The opening is the door's own size.");
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

        /// <summary>Whether any street door the style can pick is one you made: only those keep their own size, so only their holes take a margin.</summary>
        static bool OwnDoors(FacadeStyle st) => st.doorKind != null || (st.doorSet?.Any(x => !x.none && x.kind != null) ?? false);

        static readonly GUIContent OpeningMarginLabel = new GUIContent("Opening margin",
            "How much bigger the hole is than your door, each side and above, in metres; below 0 it is smaller and the door's frame stays hidden in the wall. For doors that keep their own size, on buildings without interiors");

        void WallsCard(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            var on = new List<string>();
            if (st.corniceH.HasValue) on.Add("cornice");
            if (st.bands) on.Add("bands");
            if (k0 == 0 && (st.plinthH ?? 0.45) > 0) on.Add("plinth");
            if (k0 == 0 && st.foundation.HasValue) on.Add("foundation");
            if (st.bricks > 0) on.Add("bricks");
            string summary = on.Count == 0 ? "Plain" : char.ToUpperInvariant(on[0][0]) + string.Join(" · ", on).Substring(1);
            using var c = StoreyInspectorUI.Card("walls", "Walls", StoreyInspectorUI.BricksIcon, StoreyInspectorUI.Tint.Walls, summary,
                tip: "The strips along the walls, top to bottom, and brick patches");
            if (!c.Open) return;
            WallDetails(e, b, k0, st);
        }

        void ColoursCard(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            using var c = StoreyInspectorUI.Card("colours", "Colours", StoreyInspectorUI.PaletteIcon, StoreyInspectorUI.Tint.Colours, "From the project's palette");
            if (!c.Open) return;
            Colour(e, b, k0, "Walls", "wall");
            if (k0 == 0) Colour(e, b, k0, "Ground floor walls", "groundWall", optional: true);
            Colour(e, b, k0, "Trim", "trim");
            Colour(e, b, k0, "Roof", "roof");
            Colour(e, b, k0, "Glass", "glass");
            bool more;
            using (new EditorGUI.IndentLevelScope()) more = StoreyInspectorUI.Fold("colours", "More colours", false, "Frames, metal, the inside, details: each follows the project's default until you set it");
            if (more)
                using (new EditorGUI.IndentLevelScope())
                {
                    Colour(e, b, k0, "Stairs and lifts", "core");
                    // the base's doors, plinth and foundation have theirs with them (Doors, Walls); a set-in door's threshold
                    // takes the foundation's colour, so it stays here while there's no foundation
                    if (k0 > 0) Colour(e, b, k0, "Doors", "door", optional: true);
                    if (k0 == 0 && !st.foundation.HasValue) Colour(e, b, k0, "Foundation", "foundationColor", optional: true);
                    foreach (var f in MoreColours) Colour(e, b, k0, f.label, f.field, optional: true);
                    DefaultsNotice();
                }
        }

        static readonly (string label, string field)[] MoreColours =
        {
            ("Frames", "frame"), ("Rails", "rail"), ("Metal", "metal"), ("Ceilings", "ceiling"), ("Lift inside", "liftInterior"), ("Lift button", "liftButton"),
            ("Detail metal", "detailMetal"), ("Grilles", "grille"), ("Detail dark", "detailDark"), ("Dishes", "dish"),
        };

        void RoofCard(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            string summary = st.roofType != RoofType.Flat ? $"{st.roofType}, {st.pitch ?? (st.roofType == RoofType.Shed ? 15 : st.roofType == RoofType.Mansard ? 20 : 30):0}°"
                : "Flat" + (st.parapet ? " · parapet" : "") + (Generate.RoofCap.Of(st) is Generate.RoofCap rc ? rc.inset ? " · edge set in" : rc.fascia ? " · hanging edge" : " · slab edge" : "");
            using var c = StoreyInspectorUI.Card("roof", "Roof", StoreyInspectorUI.RoofIcon, StoreyInspectorUI.Tint.Roof, summary);
            if (!c.Open) return;
            StyleEnum(e, k0, "Type", st.roofType, (s, x) => s.roofType = x);
            if (st.roofType != RoofType.Flat)
            {
                bool mans = st.roofType == RoofType.Mansard;
                StyleSlider(e, k0, mans ? "Top pitch" : "Pitch", st.pitch ?? (st.roofType == RoofType.Shed ? 15 : mans ? 20 : 30), 5, 60, 1, (s, x) => s.pitch = x);
                StyleSlider(e, k0, "Eaves", st.eave ?? 0.35, 0, 1.2, 0.05, (s, x) => s.eave = x);
                if (mans) StyleSlider(e, k0, "Steep part", st.mansard ?? 2.4, 0.5, 6, 0.1, (s, x) => s.mansard = x);
                bool dm = StoreyInspectorUI.Feature("Dormers", "Dormer windows along every eave, wherever they fit on the slope", st.dormers.HasValue);
                if (dm != st.dormers.HasValue) e.ApplyTo(dm ? "Dormers added" : "Dormers removed", bb => { Styles.Edited(bb, k0).dormers = dm ? 3.5 : (double?)null; return true; });
                if (st.dormers is double every) using (new EditorGUI.IndentLevelScope()) StyleSlider(e, k0, "Every", every, 2.2, 10, 0.1, (s, x) => s.dormers = x);
                if (mans) StoreyInspectorUI.Note("A mansard: a steep 70° slope from the eaves to the break, then a shallow hip at the top pitch.");
                return;
            }
            bool pp = StoreyInspectorUI.Feature("Parapet", "A low wall round the roof's edge", st.parapet);
            if (pp != st.parapet) e.ApplyTo(pp ? "Parapet" : "No parapet", bb => { Styles.Edited(bb, k0).parapet = pp; return true; });
            RoofEdge(e, b, k0, st);
        }

        void DetailsCard(StoreyEdit e, BuildingData b, int k0, FacadeStyle? st)
        {
            var v = e.View;
            int auto = 0;
            if (st != null)
            {
                var (ac, vents) = Details.Rules(st);
                auto = (ac > 0 ? 1 : 0) + (vents > 0 ? 1 : 0) + (st.details?.kinds?.Count(kv => Details.Density(st, kv.Key) > 0) ?? 0);
            }
            string summary = auto == 0 && b.details.Count == 0 ? "None"
                : string.Join(" · ", new[] { auto > 0 ? $"{auto} placed for you" : null, b.details.Count > 0 ? $"{b.details.Count} by hand" : null }.Where(x => x != null));
            using var c = StoreyInspectorUI.Card("details", "Details", StoreyInspectorUI.DetailsIcon, StoreyInspectorUI.Tint.Details, summary,
                tip: "Things on the walls: some placed for you by chance, others placed by hand");
            if (!c.Open) return;
            if (st != null)
            {
                StoreyInspectorUI.SubHeader("Placed for you", "Scattered over the walls by chance, the same on every rebuild; 0% leaves one out");
                RuleDetails(e, b, k0, st);
            }
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

        static readonly GUIContent EdgeKindLabel = new GUIContent("Kind", "What the roof's edge is");
        static readonly GUIContent[] EdgeKinds =
        {
            new GUIContent("Slab", "A slab on top of the walls, standing out over them: it takes the curb's place, and a parapet stands on it"),
            new GUIContent("Hanging", "A fascia hanging down from the roof's edge, out past the walls, under the curb or parapet"),
            new GUIContent("Set in", "A raised ring on the roof, set in from its edge, cut round the bulkheads and openings"),
        };

        /// <summary>
        /// A flat roof's edge (docs/EDITOR.md §6.7, <see cref="FacadeStyle.capH"/>, <see cref="Generate.RoofCap"/>): a slab, a
        /// hanging fascia or a ring set in, each set by a few numbers that are all positive, which the style's signed ones
        /// follow (a fascia's height is below 0, an overhang's depth below 0); the strip round its top, and its colours.
        /// </summary>
        void RoofEdge(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            bool on = StoreyInspectorUI.Feature("Roof edge", "A cap round the roof's edge: a slab standing out over the walls, a fascia hanging from it, or a ring set in on the roof", st.capH.HasValue);
            if (on != st.capH.HasValue) e.ApplyTo(on ? "Roof edge" : "No roof edge", bb => { var s = Styles.Edited(bb, k0); s.capH = on ? 0.4 : (double?)null; if (on && !s.capDepth.HasValue) s.capDepth = -0.25; return true; });
            if (!(st.capH is double h)) return;
            double d = st.capDepth ?? 0;
            int kind = d > 1e-3 ? 2 : h >= 0 ? 0 : 1;
            using var _ = new EditorGUI.IndentLevelScope();
            int pick = StoreyInspectorUI.Segmented(EdgeKindLabel, kind, EdgeKinds);
            if (pick != kind) e.ApplyTo("Roof edge: " + EdgeKinds[pick].text, bb =>
            {
                var s = Styles.Edited(bb, k0); double size = Math.Abs(s.capH ?? 0.4) < 0.02 ? 0.4 : Math.Abs(s.capH ?? 0.4), dd = s.capDepth ?? 0;
                s.capH = pick == 1 ? -size : size;
                s.capDepth = pick == 2 ? (dd > 1e-3 ? dd : 0.5) : pick == 1 ? (dd < -0.01 ? dd : -0.25) : (dd > 1e-3 ? -0.25 : dd);
                return true;
            });
            switch (kind)
            {
                case 0:
                    StyleSlider(e, k0, "Height", h, 0, 4, 0.01, (s, x) => s.capH = x);
                    StyleSlider(e, k0, OverhangLabel, -d, 0, 1, 0.01, (s, x) => s.capDepth = x > 0 ? -x : 0);
                    break;
                case 1:
                    StyleSlider(e, k0, new GUIContent("Hangs down", "How far it hangs below the roof's edge"), -h, 0.02, 1.5, 0.01, (s, x) => s.capH = -x);
                    StyleSlider(e, k0, OverhangLabel, -d, 0.02, 1, 0.01, (s, x) => s.capDepth = -x);
                    break;
                default:
                    StyleSlider(e, k0, "Height", h, 0, 4, 0.01, (s, x) => s.capH = x);
                    StyleSlider(e, k0, new GUIContent("In from the edge", "How far in from the walls' face it stands"), d, 0.01, 5, 0.01, (s, x) => s.capDepth = x);
                    break;
            }
            StyleSlider(e, k0, kind == 2 ? new GUIContent("Thickness", "How thick the ring is; 0: as thick as the wall")
                : new GUIContent("Top strip", "A strip of the edge's colour round its top, this wide from its outer side; 0: its top is the roof's colour"),
                st.capStrip ?? 0, 0, 2, 0.01, (s, x) => s.capStrip = x > 0 ? x : (double?)null);
            Colour(e, b, k0, "Colour", "cap", optional: true);
            if (d < 0) Colour(e, b, k0, "Underside", "capRim", optional: true);
            if (!(Generate.RoofCap.Of(st) is Generate.RoofCap)) StoreyInspectorUI.Note("A hanging edge needs an overhang.");
        }

        static readonly GUIContent OverhangLabel = new GUIContent("Overhang", "How far it stands out past the walls' face");

        /// <summary>
        /// The style's automatic details (docs/EDITOR.md §6.12): the AC units' and vents' chances, then each of the project's
        /// Facade Details with a rule, and any the style names that the project no longer has. Giving one a chance adds it
        /// to the site's list, so builds include it.
        /// </summary>
        void RuleDetails(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            var (ac, vents) = Details.Rules(st);
            StyleSlider(e, k0, "AC units %", Math.Round(ac * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).ac = x);
            StyleSlider(e, k0, "Vents %", Math.Round(vents * 100), 0, 100, 5, (s, x) => (s.details ??= new DetailRules()).vents = x);
            var shown = new HashSet<string>(StringComparer.Ordinal);
            var all = StoreyDetails.All();
            var shortName = StoreyInspectorUI.ShortNames(all.Where(d => !d.BuiltIn).Select(d => d.name).ToList());
            foreach (var d in all)
            {
                if (d.BuiltIn || !Details.RuleFits(d.anchor, d.rule)) continue;
                shown.Add(d.Id);
                RuleSlider(e, k0, st, new GUIContent(shortName.TryGetValue(d.name, out var sn) ? sn : d.name, d.name), d.Id);
                if (Details.Density(st, d.Id) > 0)
                    using (new EditorGUI.IndentLevelScope()) RuleSettings(e, b, k0, st, d.Id, d.rule);
            }
            if (st.details?.kinds != null)
                foreach (var id in st.details.kinds.Keys.Where(id => !shown.Contains(id)).ToList()) RuleSlider(e, k0, st, new GUIContent(StoreyDetails.Name(id), "Not in the project any more"), id);
            if (shown.Count == 0) StoreyInspectorUI.Note("A Facade Detail of your own with a placement rule gets a slider here (Create ▸ Storey ▸ Facade Detail).");
        }

        /// <summary>An artist's detail's chance in the style; giving it one keeps the detail in the site's list.</summary>
        static void RuleSlider(StoreyEdit e, int k0, FacadeStyle st, GUIContent name, string id)
        {
            double was = Math.Round(Details.Density(st, id) * 100);
            EditorGUI.BeginChangeCheck();
            double x = Math.Round(EditorGUILayout.Slider(new GUIContent(name.text + " %", name.tooltip), (float)was, 0, 100) / 5) * 5;
            if (!EditorGUI.EndChangeCheck() || x == was) return;
            e.ApplyTo(name.text + " %", bb => { StoreyDetails.SetDensity(Styles.Edited(bb, k0), id, x); return true; });
            if (x > 0) StoreyDetails.Keep(e.Site, id);
        }

        static readonly GUIContent[] FloorNames = { new GUIContent("All"), new GUIContent("Upper", "Not on the ground floor"), new GUIContent("Ground", "On the ground floor alone") };
        static readonly GUIContent[] CornerNames = { new GUIContent("Outward", "The corners that stand out"), new GUIContent("Inward", "The corners set in"), new GUIContent("All") };

        /// <summary>
        /// A detail's settings in the style beside its chance (docs/EDITOR.md §6.12, <see cref="KindRule"/>), as its rule takes
        /// them: how many to the square metre and the gap round each, scattered; how many to a bay, in a row; how far apart and
        /// which corners, up the corners; which storeys; and the colour its main colour takes in this style.
        /// </summary>
        void RuleSettings(StoreyEdit e, BuildingData b, int k0, FacadeStyle st, string id, Details.Rule rule)
        {
            var r = Details.RuleOf(st, id);
            void Set(string label, Action<KindRule> f) => e.ApplyTo(label, bb => { StoreyDetails.EditRule(Styles.Edited(bb, k0), id, f); return true; });
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
                    var doc = e.Document;
                    string? picked = StoreyColorField.Picker.Draw($"{b.id}/{k0}/detail/{id}", "Main colour", colour, "detail", pid => doc.palette.TryGetValue(pid, out var pe) ? pe : null);
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

        /// <summary>The built-in window's details (docs/EDITOR.md §6.7): sills, heads, glazing bars and frames. A window of your own brings its own.</summary>
        void WindowDetails(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            StyleToggle(e, k0, "Sills", st.sills, (s, x) => s.sills = x);
            StyleToggle(e, k0, "Heads", st.heads, (s, x) => s.heads = x);
            if (st.heads) using (new EditorGUI.IndentLevelScope()) StyleEnum(e, k0, "Head shape", st.head, (s, x) => s.head = x);
            bool panes = st.paneCols.HasValue || st.paneRows.HasValue;
            bool p = EditorGUILayout.Toggle(new GUIContent("Glazing bars", "Divide every window into panes"), panes);
            if (p != panes) e.ApplyTo(p ? "Glazing bars" : "No glazing bars", bb => { var s = Styles.Edited(bb, k0); s.paneCols = p ? 2 : (int?)null; s.paneRows = p ? 3 : (int?)null; return true; });
            if (panes)
                using (new EditorGUI.IndentLevelScope())
                {
                    StyleSlider(e, k0, "Panes across", st.paneCols ?? 1, 1, 6, 1, (s, x) => s.paneCols = (int)x);
                    StyleSlider(e, k0, "Panes up", st.paneRows ?? 1, 1, 6, 1, (s, x) => s.paneRows = (int)x);
                }
            StyleToggle(e, k0, "Frames", st.frames, (s, x) => s.frames = x);
        }

        static readonly GUIContent SolidBackLabel = new GUIContent("Solid back",
            "Windows and doors of your own that are set into the wall get a back in the wall's colour at their depth: whatever of them is behind it " +
            "is hidden, and round a hole bigger than what fills it the wall shows. One setting for windows and doors, on buildings without interiors");

        /// <summary>
        /// Windows or doors set into the wall (docs/EDITOR.md §6.7, <see cref="FacadeStyle.recess"/>, <see cref="FacadeStyle.doorRecess"/>):
        /// off, the built-in ones sit at their usual depth and yours at the wall's face; on, how deep, and whether the hole has a
        /// solid back (<see cref="FacadeStyle.recessBack"/>).
        /// </summary>
        static void Recess(StoreyEdit e, int k0, FacadeStyle st, string what, string tip, double? value, double max, Action<FacadeStyle, double?> set)
        {
            bool on = StoreyInspectorUI.Feature("Set into the wall", tip, value.HasValue);
            if (on != value.HasValue) e.ApplyTo(on ? what + " set into the wall" : what + " at their usual depth", bb => { set(Styles.Edited(bb, k0), on ? Generate.OpeningKinds.ShellReveal : (double?)null); return true; });
            if (!(value is double v)) return;
            using (new EditorGUI.IndentLevelScope())
            {
                StyleSlider(e, k0, new GUIContent("Depth", "How far in from the wall's face, in metres"), v, 0, max, 0.01, (s, x) => set(s, x));
                StyleToggle(e, k0, SolidBackLabel, st.recessBack, (s, x) => s.recessBack = x);
            }
        }

        /// <summary>
        /// The style's wall strips (docs/EDITOR.md §6.7), top to bottom as they stand on the wall: the cornice, bands, and on
        /// the base the plinth and foundation; brick patches on any. Each one's settings and colour are under it while it's on.
        /// </summary>
        void WallDetails(StoreyEdit e, BuildingData b, int k0, FacadeStyle st)
        {
            bool cn = StoreyInspectorUI.Feature("Cornice", "A strip round the top of the walls, under the roof (and under a terrace's parapet where a setback starts above); the top storey's windows stay under it", st.corniceH.HasValue);
            if (cn != st.corniceH.HasValue) e.ApplyTo(cn ? "Cornice" : "No cornice", bb => { Styles.Edited(bb, k0).corniceH = cn ? 0.3 : (double?)null; return true; });
            if (st.corniceH is double ch)
                using (new EditorGUI.IndentLevelScope())
                {
                    StyleSlider(e, k0, "Height", ch, 0.05, 2, 0.01, (s, x) => s.corniceH = x);
                    StyleSlider(e, k0, StandsOutLabel, st.corniceDepth ?? 0.1, 0.01, 1, 0.01, (s, x) => s.corniceDepth = x);
                    Colour(e, b, k0, "Colour", "cornice", optional: true);
                }
            bool bd = StoreyInspectorUI.Feature("Floor bands", "A strip along the walls under each storey's ceiling", st.bands);
            if (bd != st.bands) e.ApplyTo(bd ? "Floor bands" : "No floor bands", bb => { Styles.Edited(bb, k0).bands = bd; return true; });
            if (st.bands)
                using (new EditorGUI.IndentLevelScope())
                {
                    if (k0 == 0) StyleToggle(e, k0, new GUIContent("Ground floor only", "One band, over the ground floor; the storeys above have none"), st.bandGround, (s, x) => s.bandGround = x);
                    StyleSlider(e, k0, "Height", st.bandH ?? 0.22, 0.05, 1.5, 0.01, (s, x) => s.bandH = x);
                    StyleSlider(e, k0, StandsOutLabel, st.bandDepth ?? 0.06, 0.01, 0.2, 0.01, (s, x) => s.bandDepth = x);
                    Colour(e, b, k0, "Colour", "band", optional: true);
                    if (st.band == null) StyleToggle(e, k0, new GUIContent("Shade of the wall", "With no colour of its own, the band is a shade of the wall's colour, not the trim's"), st.bandWall, (s, x) => s.bandWall = x);
                }
            if (k0 == 0)
            {
                double ph = st.plinthH ?? 0.45;
                bool pl = StoreyInspectorUI.Feature("Plinth", "A strip along the foot of the ground floor's walls", ph > 0);
                if (pl != ph > 0) e.ApplyTo(pl ? "Plinth" : "No plinth", bb => { Styles.Edited(bb, 0).plinthH = pl ? (double?)null : 0; return true; });
                if (ph > 0)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        StyleSlider(e, k0, "Height", ph, 0.05, 2, 0.05, (s, x) => s.plinthH = x);
                        StyleSlider(e, k0, StandsOutLabel, st.plinthDepth ?? 0.04, 0.01, 1, 0.01, (s, x) => s.plinthDepth = x);
                        Colour(e, b, k0, "Colour", "plinth", optional: true);
                    }
                bool fd = StoreyInspectorUI.Feature("Foundation", "A foundation along the foot of the ground floor, reaching down into the ground", st.foundation.HasValue);
                if (fd != st.foundation.HasValue) e.ApplyTo(fd ? "Foundation" : "No foundation", bb => { Styles.Edited(bb, k0).foundation = fd ? 0.5 : (double?)null; return true; });
                if (st.foundation is double depth)
                    using (new EditorGUI.IndentLevelScope())
                    {
                        StyleSlider(e, k0, "Below ground", depth, 0, 5, 0.05, (s, x) => s.foundation = x);
                        StyleSlider(e, k0, "Above ground", st.foundationH ?? 0.2, 0.02, 1.5, 0.01, (s, x) => s.foundationH = x);
                        StyleSlider(e, k0, StandsOutLabel, st.foundationOut ?? 0.08, 0.01, 1, 0.01, (s, x) => s.foundationOut = x);
                        Colour(e, b, k0, "Colour", "foundationColor", optional: true);
                    }
            }
            bool br = StoreyInspectorUI.Feature("Brick patches", "Patches of brick scattered over the walls", (st.bricks ?? 0) > 0);
            if (br != (st.bricks ?? 0) > 0) e.ApplyTo(br ? "Brick patches" : "No brick patches", bb => { Styles.Edited(bb, k0).bricks = br ? 0.3 : (double?)null; return true; });
            if (st.bricks is double bk && bk > 0)
                using (new EditorGUI.IndentLevelScope()) StyleSlider(e, k0, "Amount", bk, 0.05, 1, 0.05, (s, x) => s.bricks = x);
        }

        static readonly GUIContent StandsOutLabel = new GUIContent("Stands out", "How far it stands out from the wall's face, in metres");

        static readonly GUIContent BaysLabel = new GUIContent("Bays", "How the windows are spaced along each wall: each bay has one window");
        static readonly GUIContent[] BayFitChoices =
        {
            new GUIContent("Fill the wall", "As many bays as Bay width rounds to, stretched to fill each wall from corner to corner"),
            new GUIContent("Fixed width", "As many whole bays of exactly Bay width as fit on each wall, centred on it; a window that keeps its own size keeps it however narrow the bay"),
        };

        /// <summary>How the style's bays are laid out (<see cref="FacadeStyle.bayFit"/>, docs/EDITOR.md §6.7): spread to fill each wall, or fixed at Bay width and centred.</summary>
        static void BayFits(StoreyEdit e, int k0, FacadeStyle st)
        {
            int x = StoreyInspectorUI.Segmented(BaysLabel, (int)st.bayFit, BayFitChoices);
            if (x != (int)st.bayFit) e.ApplyTo(x == (int)BayFit.Fixed ? "Bays of a fixed width" : "Bays filling the wall", bb => { Styles.Edited(bb, k0).bayFit = (BayFit)x; return true; });
        }

        static readonly GUIContent OpeningsLabel = new GUIContent("Openings",
            "The holes in each storey's walls, whatever window fills them (the ground floor's follow Ground floor). " +
            "Standard: up to 1.35 m high over a 90 cm sill, Window width wide. Tall: from 40 cm up to half a metre under the storey's top, Window width wide. " +
            "Ribbon: a strip across each bay. Glass wall: glass across each bay, floor to ceiling. None: blank walls.");
        /// <summary>The window types (<see cref="WindowType"/>) as the inspector names them.</summary>
        static readonly GUIContent[] OpeningChoices = { new GUIContent("Standard"), new GUIContent("Tall"), new GUIContent("Ribbon"), new GUIContent("Glass wall"), new GUIContent("None") };
        static string OpeningName(WindowType t) => OpeningChoices[(int)t].text;
        static readonly GUIContent OwnSizeLabel = new GUIContent("Openings", "Your window keeps its own size, so each hole is as big as it is. None: blank walls.");
        static readonly GUIContent[] OwnSizeChoices = { new GUIContent("As big as the window"), new GUIContent("None") };

        /// <summary>
        /// The style's openings (its window type, docs/EDITOR.md §6.9): where each storey's windows are and how big their
        /// holes are. A window of your own that keeps its own size leaves two things to you: whether there are any, and how
        /// high they sit (<see cref="Sill"/>).
        /// </summary>
        /// <param name="sill">Some of the windows (not all: <paramref name="ownSize"/>) keep their own size: they take a height too.</param>
        static void Openings(StoreyEdit e, int k0, FacadeStyle st, bool ownSize, bool sill)
        {
            if (!ownSize)
            {
                int x = EditorGUILayout.Popup(OpeningsLabel, (int)st.windows, OpeningChoices);
                if (x != (int)st.windows) e.ApplyTo("Openings: " + OpeningChoices[x].text, bb => { Styles.Edited(bb, k0).windows = (WindowType)x; return true; });
                if (sill && st.windows != WindowType.None) using (new EditorGUI.IndentLevelScope()) Sill(e, k0, st, OwnHeightLabel);
                return;
            }
            int was = st.windows == WindowType.None ? 1 : 0, pick = EditorGUILayout.Popup(OwnSizeLabel, was, OwnSizeChoices);
            if (pick != was) e.ApplyTo(pick == 1 ? "No windows" : "Windows", bb => { Styles.Edited(bb, k0).windows = pick == 1 ? WindowType.None : WindowType.Punched; return true; });
            if (was == 0) Sill(e, k0, st, HeightLabel);
        }

        static readonly GUIContent HeightLabel = new GUIContent("Height", "Where a window that keeps its own size sits in each storey: halfway up (the ground storey's in its own height), or at a sill height over the floor");
        static readonly GUIContent OwnHeightLabel = new GUIContent("Your windows", "Where your windows that keep their own size sit in each storey: halfway up (the ground storey's in its own height), or at a sill height over the floor");
        static readonly GUIContent[] HeightChoices = { new GUIContent("Centred", "Halfway up each storey"), new GUIContent("Sill height", "This high over each floor") };

        /// <summary>
        /// Where a window that keeps its own size sits (docs/EDITOR.md §6.9): centred in its storey
        /// (<see cref="FacadeStyle.winCentre"/>), or at <see cref="FacadeStyle.sillH"/>, whose slider shows while it isn't.
        /// </summary>
        static void Sill(StoreyEdit e, int k0, FacadeStyle st, GUIContent label)
        {
            int cur = st.winCentre ? 0 : 1, x = StoreyInspectorUI.Segmented(label, cur, HeightChoices);
            if (x != cur) e.ApplyTo(x == 0 ? "Windows centred in storey" : "Windows at sill height", bb => { Styles.Edited(bb, k0).winCentre = x == 0; return true; });
            if (!st.winCentre)
                using (new EditorGUI.IndentLevelScope())
                    StyleSlider(e, k0, "Sill height", st.sillH ?? Generate.Facade.SillOf(st.windows), 0, 2, 0.05, (s, y) => s.sillH = y);
        }

        /// <summary>
        /// A style's windows (or street doors, or the ground floor's own windows): the built-in one, ones you made (Storey
        /// Window or Door assets: docs/EDITOR.md §6.9), or none (no window there), each picked as often as its weight
        /// (docs/OPENINGS.md §7.1). One entry is the style's one window; with two or more, each has a weight, its share, and
        /// a ×. Picking one of yours adds it to the site's list, so builds include it.
        /// </summary>
        void SetRows(StoreyEdit e, int k0, FacadeStyle st, SetOf which, string label)
        {
            // none: the ground floor follows the floors above's (its own may have gone earlier in this pass of the inspector)
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
                        e.ApplyTo(label + ": " + shown[pick].text, bb => { WindowSets.Set(Styles.Edited(bb, k0), which, at, id, none); return true; });
                        if (k != null) StoreyOpenings.Keep(e.Site, k);
                    }
                    if (many)
                    {
                        EditorGUI.BeginChangeCheck();
                        double w = Math.Max(0, EditorGUILayout.DelayedFloatField((float)en.weight, GUILayout.Width(36)));
                        if (EditorGUI.EndChangeCheck() && w != en.weight) e.ApplyTo(label + " weight", bb => { WindowSets.Weigh(Styles.Edited(bb, k0), which, at, w); return true; });
                        EditorGUILayout.LabelField(new GUIContent(total > 0 ? $"{OpeningSets.Weight(set, i) / total * 100:0}%" : "", "Its share: its weight over all of theirs"), StoreyInspectorUI.Caption, GUILayout.Width(30));
                    }
                    if (door && (many || en.recess.HasValue)) DoorRecess(e, k0, en, at, label);
                    if (many && GUILayout.Button(new GUIContent("×", "Remove it from the list"), EditorStyles.miniButton, GUILayout.Width(22)))
                        e.ApplyTo(label + " removed", bb => { WindowSets.Remove(Styles.Edited(bb, k0), which, at); return true; });
                }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                if (GUILayout.Button(new GUIContent(door ? "+ Another door" : "+ Another window", door ? "Add a door: each wall's doors are picked from the list, by weight" : "Add a window (or none): each opening is picked from the list, by weight"), EditorStyles.miniButton))
                    e.ApplyTo(label + " added", bb => { WindowSets.Add(Styles.Edited(bb, k0), which, null); return true; });
            }
        }

        static readonly GUIContent DoorRecessLabel = new GUIContent("depth",
            "How deep this door sits in the wall, in metres from its face (0 to 0.29), in place of the style's; empty: the style's");

        /// <summary>
        /// A street door entry's own depth in the wall (docs/EDITOR.md §6.7, <see cref="OpeningEntry.recess"/>), in metres:
        /// empty, the style's <see cref="FacadeStyle.doorRecess"/>. One undo step, as every style edit.
        /// </summary>
        static void DoorRecess(StoreyEdit e, int k0, OpeningEntry en, int at, string label)
        {
            EditorGUILayout.LabelField(DoorRecessLabel, StoreyInspectorUI.Caption, GUILayout.Width(36));
            string was = en.recess is double r ? r.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) : "";
            string x = EditorGUILayout.DelayedTextField(was, GUILayout.Width(40)).Trim();
            if (x == was) return;
            double? to = x.Length == 0 ? (double?)null
                : double.TryParse(x.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : en.recess;
            if (to != en.recess) e.ApplyTo(label + " depth", bb => { WindowSets.Recess(Styles.Edited(bb, k0), SetOf.Doors, at, to); return true; });
        }

        /// <summary>The ground floor's windows: the floors above's, or a list of its own (docs/OPENINGS.md §7.1). A ground floor without windows has none.</summary>
        void GroundRows(StoreyEdit e, FacadeStyle st)
        {
            if (st.ground == GroundType.Solid) return;
            bool own = st.groundSet is { Count: > 0 };
            bool x = EditorGUILayout.Toggle(new GUIContent("Own windows", "The ground floor's openings take windows of their own, not the floors above's"), own);
            if (x != own) e.ApplyTo(x ? "Ground floor windows of its own" : "Ground floor windows as above", bb => { var s = Styles.Edited(bb, 0); if (x) WindowSets.Own(s, SetOf.Ground); else WindowSets.FollowAbove(s); return true; });
            // as the style is now: the edit above changes it in place, so a list just let go of isn't there to draw
            if (st.groundSet is { Count: > 0 }) SetRows(e, 0, st, SetOf.Ground, "Ground window");
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
                        Colour(e, b, 0, "Walls", "interior", baseStyle: true);
                        Colour(e, b, 0, "Floors", "floor", baseStyle: true);
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
                if (optional && value != null && GUILayout.Button(new GUIContent("Default", "Use the project's default colour, or the colour it follows"), EditorStyles.miniButton, GUILayout.Width(56)))
                    e.ApplyTo(label + " colour", bb => { spec.set(baseStyle ? bb.style : Styles.Edited(bb, k0), null!); return true; });
            }
        }

        /// <summary>
        /// The project leaves some defaults unset, so they are Storey's own colours, shown as their nearest palette
        /// entries: offer to store those entries as the project's defaults.
        /// </summary>
        void DefaultsNotice()
        {
            if (StoreyColorField.FillDefaults == null) return;
            var d = StoreyColorField.Defaults?.Invoke() ?? new StyleDefaults();
            if (!new[] { d.door, d.rail, d.metal, d.ceiling, d.liftInterior, d.liftButton, d.detailMetal, d.grille, d.detailDark, d.dish, d.frame, d.foundation, d.plinth }.Any(x => x != null && x.StartsWith("#"))) return;
            EditorGUILayout.HelpBox("Some default colours aren't set for this project, so they show the palette colour nearest to Storey's own. Store those palette colours as the project's defaults (in Storey Color Settings) to pick them yourself.", MessageType.None);
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
            "grille" => ColorSlot.Grille, "detailDark" => ColorSlot.DetailDark, "frame" => ColorSlot.Frame, "foundationColor" => ColorSlot.Foundation, "plinth" => ColorSlot.Plinth,
            "groundWall" => ColorSlot.GroundWall, "band" => ColorSlot.Band, "cornice" => ColorSlot.Cornice, "cap" => ColorSlot.Cap, "capRim" => ColorSlot.CapRim, _ => ColorSlot.Dish,
        };

        static void Slider(StoreyEdit e, string label, double value, double min, double max, double step, Action<BuildingData, double> set)
        {
            EditorGUI.BeginChangeCheck();
            double x = Math.Round(EditorGUILayout.Slider(label, (float)value, (float)min, (float)max) / step) * step;
            if (EditorGUI.EndChangeCheck() && x != value) e.ApplyTo(label, bb => { set(bb, Math.Round(x, 3)); return true; });
        }

        static void StyleSlider(StoreyEdit e, int k0, string label, double value, double min, double max, double step, Action<FacadeStyle, double> set) =>
            Slider(e, label, value, min, max, step, (bb, x) => set(Styles.Edited(bb, k0), x));

        static void StyleSlider(StoreyEdit e, int k0, GUIContent label, double value, double min, double max, double step, Action<FacadeStyle, double> set)
        {
            EditorGUI.BeginChangeCheck();
            double x = Math.Round(EditorGUILayout.Slider(label, (float)value, (float)min, (float)max) / step) * step;
            if (EditorGUI.EndChangeCheck() && x != value) e.ApplyTo(label.text, bb => { set(Styles.Edited(bb, k0), Math.Round(x, 3)); return true; });
        }

        static void StyleToggle(StoreyEdit e, int k0, string label, bool value, Action<FacadeStyle, bool> set)
        {
            bool x = EditorGUILayout.Toggle(label, value);
            if (x != value) e.ApplyTo(label, bb => { set(Styles.Edited(bb, k0), x); return true; });
        }

        static void StyleToggle(StoreyEdit e, int k0, GUIContent label, bool value, Action<FacadeStyle, bool> set)
        {
            bool x = EditorGUILayout.Toggle(label, value);
            if (x != value) e.ApplyTo(label.text, bb => { set(Styles.Edited(bb, k0), x); return true; });
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
