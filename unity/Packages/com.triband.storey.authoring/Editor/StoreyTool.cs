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
            using (new Handles.DrawingScope(site.transform.localToWorldMatrix))
            {
                if (b != null) ToolGUI(e, b, sv);
                else Hint("Click a building to select it.");
                DefaultClick(e, e.Selected, sv);
            }
            site.View = ViewFor(e, e.Selected, sv, site.transform.position);
            // keep drawing while the floor clip eases or walls slide: edit mode only updates when asked
            if (easing || site.Animating) { EditorApplication.QueuePlayerLoopUpdate(); sv.Repaint(); }
            else if (Event.current.type == EventType.MouseMove) sv.Repaint();
        }

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

        // the floor clip as shown: it eases to the active storey's ceiling, so a change of floor grows the storeys up or
        // sinks them down instead of cutting at once
        static string clipFor = ""; static double shownClip, lastTime; static bool easing;

        static double EasedClip(BuildingData b, double target)
        {
            double now = EditorApplication.timeSinceStartup, dt = Math.Min(0.05, Math.Max(0, now - lastTime)); lastTime = now;
            double finite = target > 1e8 ? Derived.RoofY(b) + 8 : target;   // the roof: everything shows, so ease to above it
            if (clipFor != b.id) { clipFor = b.id; shownClip = finite; }      // another building: no animation
            shownClip += (finite - shownClip) * (1 - Math.Exp(-dt * 12));
            easing = Math.Abs(finite - shownClip) > 0.004;
            if (!easing) shownClip = finite;
            return easing ? shownClip : target;
        }

        SiteView? ViewFor(StoreyEdit e, BuildingData? b, SceneView sv, Vector3 origin)
        {
            var v = SiteView.Neutral;
            if (b == null) { clipFor = ""; easing = false; return v; }
            if (e.View.isolate) v.isolateId = b.id;
            if (Tab == StoreyTab.Interior && b.interior)
            {
                var (clip, lo, hi) = Picking.StoreyView(b, e.View.floor);
                clip = EasedClip(b, clip);
                // the shader compares world heights: a site raised or lowered moves its storeys (rotation and scale are not supported here)
                float lift = origin.y;
                v.activeId = b.id; v.clipY = (float)clip + lift; v.cut = true; v.stubHeight = 1.0f; v.cutBase = (float)lo + lift; v.cutTop = (float)hi + lift;
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

        /// <summary>A core's rectangle on a storey floor.</summary>
        protected static void CoreRect(BuildingData b, CoreData it, double y, Color c)
        {
            var R = Generate.Cores.RectOf(it);
            var pts = new Vector3[5];
            int j = 0;
            foreach (var (a, w) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1) })
                pts[j++] = W(b, new Vec2(R.c.x + R.u.x * a * R.hx + R.w.x * w * R.hz, R.c.z + R.u.z * a * R.hx + R.w.z * w * R.hz), y);
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
