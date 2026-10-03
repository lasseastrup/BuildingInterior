#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// What the three Storey tools share (docs/EDITOR.md slices 6.5–6.7): only one set of handles in the Scene view at a
    /// time (SPEC §9), a click that hits nothing of the tool's selects the building under the pointer, and the site is
    /// drawn with the view the tab wants (the active storey clipped and cut away as in play in the Interior tab). They
    /// belong to <see cref="StoreyToolContext"/>. No keyboard shortcuts: everything is a button in the inspector or the
    /// Storey Floors overlay.
    /// </summary>
    internal abstract class StoreyTool : EditorTool
    {
        protected abstract StoreyTab Tab { get; }
        protected StoreySite? Site => target as StoreySite;

        /// <summary>Draw the tool's handles and ghosts for the selected building.</summary>
        protected abstract void ToolGUI(StoreyEdit e, BuildingData b, SceneView sv);

        /// <summary>A click the tool's handles did not take. True when the tool used it (it then gets the release too).</summary>
        protected virtual bool Click(StoreyEdit e, BuildingData b, Vec3d origin, Vec3d dir, SceneView sv) => false;

        protected virtual void Release(StoreyEdit e, BuildingData b, Vec3d origin, Vec3d dir, SceneView sv) { }

        /// <summary>
        /// Delete (or Backspace) in the Scene view: remove what the tool has selected (a wall, stairs, a corner…). True when
        /// something went; false shows a hint. Never the site's GameObject (the Hierarchy still deletes that).
        /// </summary>
        protected virtual bool DeleteSelected(StoreyEdit e, BuildingData b, SceneView sv) => false;

        /// <summary>What the hint says Delete works on in this tool.</summary>
        protected virtual string DeleteHint => "Nothing selected to delete";

        /// <summary>Take the Scene view's Delete before Unity does: it would delete the whole site.</summary>
        void DeleteKey(StoreyEdit e, BuildingData? b, SceneView sv)
        {
            var ev = Event.current;
            if (ev.type != EventType.ValidateCommand && ev.type != EventType.ExecuteCommand) return;
            if (ev.commandName != "Delete" && ev.commandName != "SoftDelete") return;
            if (ev.type == EventType.ExecuteCommand && (b == null || !DeleteSelected(e, b, sv)))
                Notify(sv, DeleteHint + " (Delete here never deletes the site: use the Hierarchy for that)");
            ev.Use();
            Inspectors();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView sv) return;
            var site = Site; if (site == null) return;
            var e = StoreyEdit.Of(site);
            if (e == null) { site.View = null; Hint("Edit layout in the Storey Site inspector to use this tool."); return; }
            if (e.View.tab != Tab) { e.View.tab = Tab; Inspectors(); }
            var b = e.Selected;
            if (b != null) ClampView(e, b);
            // the layout lives in the site's local space: the meshes are the site's children, so the handles and rays are too
            toLocal = site.transform.worldToLocalMatrix;
            DeleteKey(e, b, sv);
            using (new Handles.DrawingScope(site.transform.localToWorldMatrix))
            {
                Hover(e, sv);
                if (b != null) ToolGUI(e, b, sv);
                else Hint("Click a building to select it.");
                DefaultClick(e, e.Selected, sv);
                SelectionGlow(e);
                StoreyProblems.Draw(e);
                StoreyJuice.Draw();
            }
            var shown = site.View;
            site.View = ViewFor(e, e.Selected, sv, site.transform.position);
            // and again when the site builds and draws, from the layout it has just built (StoreySite.ViewSource)
            var origin = site.transform.position;
            site.ViewSource = () => ViewFor(e, e.Selected, sv, origin);
            if (!Same(shown, site.View)) EditorApplication.QueuePlayerLoopUpdate();
            FollowFloor(e, e.Selected, sv);
            // keep drawing while anything eases (floor clip, walls, a building growing in, the camera, isolate, the effects):
            // edit mode only updates when asked
            if (easing || site.Animating || revealing || pivotTo != null || isoEasing || StoreyJuice.Active) { EditorApplication.QueuePlayerLoopUpdate(); sv.Repaint(); }
            else if (Event.current.type == EventType.MouseMove) sv.Repaint();
        }

        static bool Same(SiteView? a, SiteView? b) =>
            a.HasValue == b.HasValue && (!a.HasValue || (a.Value.activeId == b!.Value.activeId && a.Value.clipY == b.Value.clipY && a.Value.cut == b.Value.cut
                && a.Value.walls == b.Value.walls && a.Value.cutBase == b.Value.cutBase && a.Value.cutTop == b.Value.cutTop && a.Value.isolateId == b.Value.isolateId && a.Value.isolateAmount == b.Value.isolateAmount));

        public override void OnWillBeDeactivated()
        {
            if (Site != null) Site.View = null;
        }

        void DefaultClick(StoreyEdit e, BuildingData? b, SceneView sv)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var ev = Event.current;
            if (ev.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
            if (ev.type == EventType.MouseDown && ev.button == 0 && !ev.alt && HandleUtility.nearestControl == id)
            {
                var (o, d) = MouseRay();
                if (b != null && Click(e, b, o, d, sv)) { GUIUtility.hotControl = id; ev.Use(); return; }
                var hit = Picking.Building(e.Document, o, d);
                if (hit.index >= 0) { e.View.Select(e.Document.buildings[hit.index].id); ev.Use(); Inspectors(); }
            }
            else if (ev.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                if (b != null) { var (o, d) = MouseRay(); Release(e, b, o, d, sv); }
                ev.Use();
            }
            else if (ev.type == EventType.MouseDrag && GUIUtility.hotControl == id) { ev.Use(); sv.Repaint(); }
        }

        static void ClampView(StoreyEdit e, BuildingData b)
        {
            var v = e.View;
            v.floor = System.Math.Max(0, System.Math.Min(b.floors.Count, v.floor));
            if (v.tier != 0 && !Derived.IsSetback(b, v.tier)) v.tier = 0;
        }

        // the floor clip as shown: going up, it eases to the active storey's ceiling, so the storeys grow up instead of
        // cutting at once. Going down (a lower floor, a storey made shorter) it drops at once: easing down would leave the
        // clip above the new ceiling for a moment, and that ceiling would cover the storey. The new storey's walls still
        // slide down to the stub, so the change is not abrupt.
        static string clipFor = ""; static double shownClip, lastTime; static bool easing;

        static double EasedClip(BuildingData b, double target)
        {
            double now = EditorApplication.timeSinceStartup, dt = Math.Min(0.05, Math.Max(0, now - lastTime)); lastTime = now;
            double finite = target > 1e8 ? Derived.RoofY(b) + 8 : target;   // the roof: everything shows, so ease to above it
            if (clipFor != b.id) { clipFor = b.id; shownClip = finite; }      // another building: no animation
            if (finite < shownClip) shownClip = finite;                         // down: at once (above)
            shownClip += (finite - shownClip) * (1 - Math.Exp(-dt * 12));
            easing = Math.Abs(finite - shownClip) > 0.004;
            if (!easing) shownClip = finite;
            return easing ? shownClip : target;
        }

        // ---- the juice (docs/EDITOR.md §5) ----

        // the building under the pointer, outlined faintly before it is clicked
        static string hoverId = "";

        void Hover(StoreyEdit e, SceneView sv)
        {
            var ev = Event.current;
            if (ev.type == EventType.MouseMove)
            {
                var (o, d) = MouseRay(); var hit = Picking.Building(e.Document, o, d);
                string id = hit.index >= 0 ? e.Document.buildings[hit.index].id : "";
                if (id != hoverId) { hoverId = id; sv.Repaint(); }
            }
            else if (ev.type == EventType.MouseLeaveWindow) hoverId = "";
            if (ev.type != EventType.Repaint || hoverId.Length == 0 || hoverId == e.View.selectedId || GUIUtility.hotControl != 0) return;
            var hb = e.Document.buildings.Find(x => x.id == hoverId); if (hb == null) return;
            var c = Color.white; c.a = 0.45f; Handles.color = c;
            foreach (var l in BuildingOutline(hb)) Handles.DrawAAPolyLine(2f, l);
        }

        /// <summary>A building's footprint at the ground and its top outline at the roof.</summary>
        protected static Vector3[][] BuildingOutline(BuildingData b)
        {
            var bot = Derived.OutlineAt(b, 0); var top = Derived.OutlineAt(b, b.floors.Count); double yt = Derived.RoofY(b);
            Vector3[] Ring(List<Vec2> fp, double y) { var pts = new Vector3[fp.Count + 1]; for (int i = 0; i <= fp.Count; i++) pts[i] = W(b, fp[i % fp.Count], y); return pts; }
            return new[] { Ring(bot, 0.03), Ring(top, yt + 0.03) };
        }

        // what was just selected glows a moment, so it is clear what the inspector now shows
        static string glowBuilding = "", glowCore = ""; static int glowWall = -1;

        void SelectionGlow(StoreyEdit e)
        {
            var v = e.View; var b = e.Selected;
            if (v.selectedId != glowBuilding)
            {
                glowBuilding = v.selectedId;
                if (b != null) StoreyJuice.Glow(BuildingOutline(b), Accent, 0.55);
            }
            if (b == null) { glowCore = ""; glowWall = -1; return; }
            double y = Derived.FloorBase(b, System.Math.Min(v.floor, b.floors.Count)) + 0.04;
            if (v.selectedCore != glowCore)
            {
                glowCore = v.selectedCore;
                var s = b.shafts.Find(x => x.id == v.selectedCore);
                if (s != null) StoreyJuice.Glow(new[] { CoreOutline(b, s, y) }, Ok, 0.45);
            }
            if (v.selectedWall != glowWall)
            {
                glowWall = v.selectedWall;
                if (v.floor < b.floors.Count && v.selectedWall >= 0 && v.selectedWall < b.floors[v.floor].walls.Count)
                {
                    var w = b.floors[v.floor].walls[v.selectedWall];
                    StoreyJuice.Glow(new[] { new[] { W(b, w.a, y), W(b, w.b, y) } }, Ok, 0.45);
                }
            }
        }

        // a building that is new, or has grown floors, grows into place: the floor clip rises from where it was to above the roof
        static readonly HashSet<string> knownIds = new HashSet<string>();
        static StoreyEdit? watchFor;
        static string revealId = "", roofFor = ""; static double revealFrom, revealTo, revealT0, lastRoof; static bool revealing;
        const double RevealTime = 0.5;

        void WatchGrowth(StoreyEdit e, BuildingData b)
        {
            double now = EditorApplication.timeSinceStartup, roof = Derived.RoofY(b);
            if (watchFor != e) { watchFor = e; knownIds.Clear(); roofFor = ""; }   // another layout: nothing in it is new
            bool isNew = knownIds.Count > 0 && !knownIds.Contains(b.id);
            if (isNew) { revealId = b.id; revealFrom = 0; revealTo = roof + 8; revealT0 = now; }
            else if (roofFor == b.id && roof > lastRoof + 0.01) { revealId = b.id; revealFrom = lastRoof; revealTo = roof + 8; revealT0 = now; }
            roofFor = b.id; lastRoof = roof;
            knownIds.Clear(); foreach (var x in e.Document.buildings) knownIds.Add(x.id);
        }

        static double? RevealClip()
        {
            if (revealId.Length == 0) { revealing = false; return null; }
            double t = (EditorApplication.timeSinceStartup - revealT0) / RevealTime;
            if (t >= 1) { revealId = ""; revealing = false; return null; }
            revealing = true;
            double ease = 1 - (1 - t) * (1 - t) * (1 - t);   // ease out: fast, then settling
            return revealFrom + (revealTo - revealFrom) * ease;
        }

        // Isolate fades the other buildings in and out instead of switching at once
        static string isoId = ""; static double isoAmount, isoTime; static bool isoEasing;

        void EaseIsolate(StoreyEdit e, BuildingData b, ref SiteView v)
        {
            double now = EditorApplication.timeSinceStartup, dt = System.Math.Min(0.05, System.Math.Max(0, now - isoTime)); isoTime = now;
            double want = e.View.isolate ? 1 : 0;
            if (e.View.isolate) isoId = b.id;
            isoAmount += (want - isoAmount) * System.Math.Min(1, dt * 10);
            if (System.Math.Abs(want - isoAmount) < 0.01) isoAmount = want;
            isoEasing = isoAmount != want;
            if (isoAmount > 0 && isoId.Length > 0) { v.isolateId = isoId; v.isolateAmount = (float)isoAmount; }
        }

        // the camera follows the active floor: the Scene view's pivot eases up or down by the storey change (a toggle in the Interior tab)
        static string floorFor = ""; static int lastFloor = -1; static float? pivotTo; static double pivotTime;

        void FollowFloor(StoreyEdit e, BuildingData? b, SceneView sv)
        {
            double now = EditorApplication.timeSinceStartup, dt = System.Math.Min(0.05, System.Math.Max(0, now - pivotTime)); pivotTime = now;
            if (b == null || Tab != StoreyTab.Interior) { floorFor = ""; lastFloor = -1; pivotTo = null; return; }
            int k = e.View.floor;
            if (floorFor == b.id && lastFloor >= 0 && k != lastFloor && e.View.followFloor)
            {
                float dy = (float)(Derived.FloorBase(b, k) - Derived.FloorBase(b, lastFloor));
                pivotTo = (pivotTo ?? sv.pivot.y) + dy;
            }
            floorFor = b.id; lastFloor = k;
            if (pivotTo == null || Event.current.type != EventType.Repaint) return;
            var p = sv.pivot; float to = pivotTo.Value;
            float y = p.y + (to - p.y) * (float)(1 - System.Math.Exp(-dt * 10));
            if (System.Math.Abs(to - y) < 0.01f) { y = to; pivotTo = null; }
            sv.pivot = new Vector3(p.x, y, p.z);
        }

        SiteView? ViewFor(StoreyEdit e, BuildingData? b, SceneView sv, Vector3 origin)
        {
            var v = SiteView.Neutral;
            if (b == null) { clipFor = ""; easing = false; return v; }
            WatchGrowth(e, b);
            EaseIsolate(e, b, ref v);
            var grow = RevealClip();
            if (grow != null && !(Tab == StoreyTab.Interior && b.interior))
            {
                // growing in: only the clip, no cutaway
                var rb = e.Document.buildings.Find(x => x.id == revealId);
                if (rb != null) { v.activeId = rb.id; v.clipY = (float)grow.Value + origin.y; }
            }
            if (Tab == StoreyTab.Interior && b.interior)
            {
                var (clip, lo, hi) = Picking.StoreyView(b, e.View.floor);
                clip = EasedClip(b, clip);
                // the shader compares world heights: a site raised or lowered moves its storeys (rotation and scale are not supported here)
                float lift = origin.y;
                v.activeId = b.id; v.clipY = (float)clip + lift; v.cut = true; v.walls = e.View.walls; v.stubHeight = 1.0f; v.cutBase = (float)lo + lift; v.cutTop = (float)hi + lift;
                // the cutaway compares with the walls' data, which is in the site's x and z: a moved site moves them
                var cam = sv.camera.transform.position - new Vector3(origin.x, 0, origin.z); var focus = sv.pivot - new Vector3(origin.x, 0, origin.z);
                v.camera = cam; v.focus = focus;
                var dxz = new Vector2(cam.x - focus.x, cam.z - focus.z);
                v.cameraDir = dxz.sqrMagnitude > 1e-8f ? dxz.normalized : new Vector2(0, 1);
            }
            else { clipFor = ""; easing = false; }   // back in the Interior tab, the clip starts where the floor is
            return v;
        }

        // ---- shared helpers ----

        protected static Vector3 W(BuildingData b, Vec2 p, double y) => new Vector3((float)(p.x + b.pos.x), (float)y, (float)(p.z + b.pos.z));
        protected static Vec2 L(BuildingData b, Vector3 w) => new Vec2(w.x - b.pos.x, w.z - b.pos.z);
        protected static Vec3d D(Vector3 v) => new Vec3d(v.x, v.y, v.z);

        static Matrix4x4 toLocal = Matrix4x4.identity;

        /// <summary>The ray under the pointer, in the site's (the layout's) coordinates.</summary>
        protected static (Vec3d origin, Vec3d dir) MouseRay()
        {
            var r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            return (D(toLocal.MultiplyPoint(r.origin)), D(toLocal.MultiplyVector(r.direction)));
        }

        /// <summary>A point along the pointer's ray, in the site's coordinates (for labels next to the pointer).</summary>
        protected static Vector3 AlongRay(float distance)
        {
            var r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            return toLocal.MultiplyPoint(r.GetPoint(distance));
        }

        protected static float Size(Vector3 at, float k = 0.08f) => HandleUtility.GetHandleSize(at) * k;

        protected static void Outline(BuildingData b, List<Vec2> fp, double y, Color c, float width = 3f)
        {
            var pts = new Vector3[fp.Count + 1];
            for (int i = 0; i <= fp.Count; i++) pts[i] = W(b, fp[i % fp.Count], y);
            Handles.color = c;
            Handles.DrawAAPolyLine(width, pts);
        }

        protected static void Dotted(BuildingData b, List<Vec2> fp, double y, Color c)
        {
            var seg = new Vector3[fp.Count * 2];
            for (int i = 0; i < fp.Count; i++) { seg[2 * i] = W(b, fp[i], y); seg[2 * i + 1] = W(b, fp[(i + 1) % fp.Count], y); }
            Handles.color = c;
            Handles.DrawDottedLines(seg, 4f);
        }

        /// <summary>A wire box in a wall's frame: centre, size along the wall (sx), up (sy) and out (sz).</summary>
        protected static void Box(GhostBox g, Color c)
        {
            var u = new Vector3((float)g.u.x, 0, (float)g.u.z);
            var m = Handles.matrix;
            Handles.matrix = m * Matrix4x4.TRS(new Vector3((float)g.cx, (float)g.cy, (float)g.cz), Quaternion.LookRotation(u, Vector3.up), Vector3.one);
            Handles.color = c;
            Handles.DrawWireCube(Vector3.zero, new Vector3((float)g.sz, (float)g.sy, (float)g.sx));
            Handles.matrix = m;
        }

        /// <summary>A core's rectangle on a storey floor, as a closed polyline.</summary>
        protected static Vector3[] CoreOutline(BuildingData b, CoreData it, double y)
        {
            var R = Generate.Cores.RectOf(it);
            var pts = new Vector3[5]; int j = 0;
            foreach (var (a, w) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1) })
                pts[j++] = W(b, new Vec2(R.c.x + R.u.x * a * R.hx + R.w.x * w * R.hz, R.c.z + R.u.z * a * R.hx + R.w.z * w * R.hz), y);
            return pts;
        }

        /// <summary>A core's rectangle on a storey floor.</summary>
        protected static void CoreRect(BuildingData b, CoreData it, double y, Color c)
        {
            var R = Generate.Cores.RectOf(it);
            var pts = CoreOutline(b, it, y);
            Handles.color = c;
            Handles.DrawAAPolyLine(3f, pts);
            if (it.type == CoreType.Flight)
            {
                // which way is up: an arrow up the flight lane (the −u half), and the walkway back beside it dotted
                Vector3 P(double a, double w) => W(b, new Vec2(R.c.x + R.u.x * a + R.w.x * w, R.c.z + R.u.z * a + R.w.z * w), y);
                double l = R.hz * 0.7, lane = Generate.Dim.FLIGHT_LANE, ax = -R.hx + lane / 2, hh = lane * 0.35, xm = -R.hx + lane;
                Handles.DrawAAPolyLine(3f, P(ax, -l), P(ax, l));
                Handles.DrawAAPolyLine(3f, P(ax - hh, l - hh), P(ax, l), P(ax + hh, l - hh));
                Handles.DrawDottedLine(P(xm, -R.hz), P(xm, R.hz), 3f);
            }
        }

        protected static void Label(Vector3 at, string text) => Handles.Label(at, text, EditorStyles.whiteBoldLabel);

        protected static void Hint(string text)
        {
            if (Event.current.type != EventType.Repaint) return;
            Handles.BeginGUI();
            GUI.Label(new Rect(10, 10, 420, 22), text, EditorStyles.helpBox);
            Handles.EndGUI();
        }

        protected static void Notify(SceneView sv, string text) => sv.ShowNotification(new GUIContent(text), 1.5);

        protected static void Inspectors() => UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

        protected static readonly Color Accent = new Color(0.87f, 0.35f, 0.07f), Ok = new Color(0.18f, 0.49f, 0.31f), Bad = new Color(0.75f, 0.22f, 0.17f), Faint = new Color(1, 1, 1, 0.55f);
    }
}
