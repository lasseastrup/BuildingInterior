#nullable enable
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Unity;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// The Shape tool (docs/EDITOR.md slice 6.5): the selected outline's corners, a "+" on each edge to add a corner, an
    /// orange bar outside each edge to push that wall, and a centre handle to move the building, at the outline's
    /// height with the outline below dotted. Corners snap to neighbours, the outline below and the next corners (Alt:
    /// free); a refused change says why. Double-click a corner to remove it. Every drag is one undo step.
    /// </summary>
    [EditorTool("Storey Shape", typeof(StoreySite), typeof(StoreyToolContext))]
    internal sealed class StoreyShapeTool : StoreyTool
    {
        protected override StoreyTab Tab => StoreyTab.Shape;

        public override GUIContent toolbarIcon => new GUIContent("Shape", "Storey Shape: outlines and setbacks");

        enum Drag { None, Corner, Insert, Push, Move }
        Drag drag;
        int dragIndex, dragControl;
        List<Vec2>? dragBase;
        Vector3 dragStart;
        Vec2 moveOffset;
        string? refusal;
        // control ids by kind of handle, so adding a corner mid-drag does not renumber the other handles
        static readonly int CornerHint = "StoreyCorner".GetHashCode(), InsertHint = "StoreyInsert".GetHashCode(), PushHint = "StoreyPush".GetHashCode(), MoveHint = "StoreyMove".GetHashCode();

        protected override void ToolGUI(StoreyEdit e, BuildingData b, SceneView sv)
        {
            int k0 = e.View.tier;
            var fp = Tiers.Outline(b, k0);
            double y = Derived.FloorBase(b, k0) + 0.02;
            var ev = Event.current;
            bool alt = ev.alt;

            if (k0 > 0) Dotted(b, Derived.OutlineAt(b, k0 - 1), y, Faint);
            Outline(b, fp, y, Accent);
            if (e.View.selectedCorner >= fp.Count) e.View.selectedCorner = -1;
            // the selected corner's cut, as the Shape tab would make it
            if (e.View.selectedCorner >= 0 && drag == Drag.None && ev.type == EventType.Repaint)
            {
                var cut = Outlines.CornerPoints(fp, e.View.selectedCorner, e.View.cornerShape, e.View.cornerSize);
                if (cut != null) { var pts = cut.Select(p => W(b, p, y)).ToArray(); Handles.color = Ok; Handles.DrawAAPolyLine(3f, pts); }
            }

            // a corner being added follows the pointer on the outline's plane until release
            if (drag == Drag.Insert && GUIUtility.hotControl == dragControl)
            {
                if (ev.type == EventType.MouseDrag)
                {
                    var (o, dr) = MouseRay();
                    var p = Picking.OnPlane(b, o, dr, y);
                    if (p != null) MoveCorner(e, b, k0, dragIndex, W(b, p.Value, y), alt);
                    ev.Use();
                }
                else if (ev.type == EventType.MouseUp) { GUIUtility.hotControl = 0; ev.Use(); Finish(e, k0); return; }
            }

            // corners
            for (int i = 0; i < fp.Count; i++)
            {
                int id = GUIUtility.GetControlID(CornerHint, FocusType.Passive);
                var at = W(b, fp[i], y);
                if (ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2 && HandleUtility.nearestControl == id)
                {
                    int ii = i;
                    if (!e.ApplyTo("Corner removed", bb => Outlines.RemoveVertex(bb, ii, k0))) Notify(sv, "An outline needs at least three corners");
                    e.View.selectedCorner = -1;
                    ev.Use(); return;
                }
                // a click selects the corner (for the Shape tab's chamfer and round); the handle still takes the drag
                if (ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 1 && HandleUtility.nearestControl == id && e.View.selectedCorner != i)
                {
                    e.View.selectedCorner = i;
                    StoreyJuice.Ring(at, Size(at, 0.12f), Accent);
                    Inspectors();
                }
                bool sel = e.View.selectedCorner == i;
                if (sel && ev.type == EventType.Repaint) { Handles.color = Accent; Handles.DrawWireDisc(at, Vector3.up, Size(at, 0.11f), 2f); }
                Handles.color = sel ? Accent : Color.white;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(id, at, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(at), Handles.DotHandleCap, Vector2.zero, false);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginDragOf(e, Drag.Corner, i, id, "Move corner");
                    MoveCorner(e, b, k0, i, np, alt);
                }
            }

            // a "+" on each edge long enough: dragging it adds a corner there and moves it
            for (int i = 0; i < fp.Count; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % fp.Count];
                int id = GUIUtility.GetControlID(InsertHint, FocusType.Passive);
                if (drag == Drag.Insert || Tiers.EdgeLen(fp, i) <= 1.2) continue;
                var mid = W(b, new Vec2((a.x + c.x) / 2, (a.z + c.z) / 2), y);
                Handles.color = Faint;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(id, mid, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(mid, 0.06f), Handles.RectangleHandleCap, Vector2.zero, false);
                if (EditorGUI.EndChangeCheck())
                {
                    BeginDragOf(e, Drag.Insert, i, id, "Add corner");
                    int ii = i; int ni = -1;
                    e.ApplyTo("Add corner", bb => { ni = Outlines.InsertVertex(bb, ii, k0); return true; });
                    dragIndex = ni; e.View.selectedCorner = ni;
                    GUIUtility.hotControl = id;   // the tool drives the rest of the drag (above)
                    MoveCorner(e, e.Selected!, k0, dragIndex, np, alt);
                    return;
                }
            }

            // an orange bar outside each edge pushes that wall along its normal
            double sg = Generate.Geo.Area2(fp) > 0 ? 1 : -1;
            for (int i = 0; i < fp.Count; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % fp.Count];
                double Ln = Tiers.EdgeLen(fp, i); if (Ln == 0) Ln = 1;
                var n = new Vector3((float)(sg * (c.z - a.z) / Ln), 0, (float)(-sg * (c.x - a.x) / Ln));
                var mid = W(b, new Vec2((a.x + c.x) / 2, (a.z + c.z) / 2), y) + n * 0.9f;
                int id = GUIUtility.GetControlID(PushHint, FocusType.Passive);
                Handles.color = Accent;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider(id, mid, n, Size(mid, 0.1f), Handles.CubeHandleCap, 0f);
                if (EditorGUI.EndChangeCheck())
                {
                    if (drag != Drag.Push) { BeginDragOf(e, Drag.Push, i, id, "Push wall"); dragBase = Tiers.Copy(fp); dragStart = mid; }
                    double d = Vector3.Dot(np - dragStart, n);
                    var nf = System.Math.Abs(d) < 0.005 ? dragBase! : Outlines.PushEdge(dragBase!, i, d);
                    Set(e, k0, nf);
                    Label(np, System.Math.Abs(d) < 0.005 ? "Drag in or out" : $"{System.Math.Abs(d):0.00} m {(d < 0 ? "in" : "out")}");
                }
            }

            // the building's centre moves it; an edge that comes within 0.5 m of a neighbour's snaps onto it (Alt: free)
            if (k0 == 0)
            {
                var bb0 = Tiers.Bbox(b.footprint);
                var centre = W(b, new Vec2((bb0.x0 + bb0.x1) / 2, (bb0.z0 + bb0.z1) / 2), y);
                int id = GUIUtility.GetControlID(MoveHint, FocusType.Passive);
                Handles.color = Accent;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(id, centre, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(centre, 0.15f), Handles.CircleHandleCap, Vector2.zero, false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (drag != Drag.Move) { BeginDragOf(e, Drag.Move, 0, id, "Move building"); moveOffset = new Vec2(b.pos.x - np.x, b.pos.z - np.z); moveSnapped = false; }
                    double nx = Tiers.Cm(np.x + moveOffset.x), nz = Tiers.Cm(np.z + moveOffset.z), rx = nx, rz = nz;
                    if (!alt) { var s = Outlines.SnapMove(b, nx, nz, e.Document.buildings); nx = s.x; nz = s.z; }
                    if (nx != b.pos.x || nz != b.pos.z) e.ApplyTo("Move building", bb => { bb.pos = new Vec2(nx, nz); return true; });
                    // snapped against a neighbour: the wall they now share flashes
                    bool snapped = nx != rx || nz != rz;
                    if (snapped && !moveSnapped) FlashShared(e, b.id);
                    moveSnapped = snapped;
                }
            }

            if (drag != Drag.None)
            {
                // edge lengths while dragging, and why the last change was refused
                var cur = Tiers.Outline(e.Selected!, k0);
                for (int i = 0; i < cur.Count; i++)
                {
                    var a = cur[i]; var c = cur[(i + 1) % cur.Count];
                    Label(W(e.Selected!, new Vec2((a.x + c.x) / 2, (a.z + c.z) / 2), y), $"{Tiers.EdgeLen(cur, i):0.00} m");
                }
                if (refusal != null) Label(AlongRay(10), refusal);
                // the drag ends when its handle lets go of the mouse, whichever event that came in (a used MouseUp
                // no longer reads as one)
                if (drag != Drag.Insert && GUIUtility.hotControl != dragControl) Finish(e, k0);
            }
        }

        void BeginDragOf(StoreyEdit e, Drag kind, int index, int control, string undoName)
        {
            if (drag != Drag.None) return;
            drag = kind; dragIndex = index; dragControl = control; refusal = null;
            e.BeginDrag(undoName);
        }

        /// <summary>The drag ended: corners that are straight or doubled merge, and the drag is one undo step.</summary>
        void Finish(StoreyEdit e, int k0)
        {
            if (drag == Drag.Corner || drag == Drag.Insert || drag == Drag.Push)
                e.ApplyTo("Tidy outline", bb => Outlines.Simplify(bb, k0));
            e.EndDrag();
            drag = Drag.None; dragBase = null; refusal = null;
            Inspectors();
        }

        void MoveCorner(StoreyEdit e, BuildingData b, int k0, int i, Vector3 np, bool alt)
        {
            var fp = Tiers.Outline(b, k0);
            var q = new Vec2(Tiers.Cm(np.x - b.pos.x), Tiers.Cm(np.z - b.pos.z));
            var raw = q;
            if (!alt) q = Outlines.Snap(b, k0, fp, i, q.x, q.z, e.Document.buildings);
            // snapped onto a neighbour's corner or edge: a pulse there (once per place it snaps to)
            bool snapped = q.x != raw.x || q.z != raw.z;
            if (snapped && (!cornerSnapped || Tiers.Hypot(q.x - cornerAt.x, q.z - cornerAt.z) > 0.02))
                StoreyJuice.Ring(W(b, q, Derived.FloorBase(b, k0) + 0.03), Size(W(b, q, Derived.FloorBase(b, k0)), 0.12f), Accent);
            cornerSnapped = snapped; cornerAt = q;
            if (fp[i].x == q.x && fp[i].z == q.z) return;
            var nf = Tiers.Copy(fp); nf[i] = q;
            Set(e, k0, nf);
        }

        bool moveSnapped, cornerSnapped; Vec2 cornerAt;

        /// <summary>Flash every stretch of wall the building now shares with a neighbour, at the ground and at the shared height.</summary>
        static void FlashShared(StoreyEdit e, string id)
        {
            var site = new Generate.Site(e.Document.buildings);
            var b = site.ById(id); if (b == null) return;
            var fp = Derived.OutlineAt(b, 0);
            for (int i = 0; i < fp.Count; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % fp.Count]; double L = Tiers.Hypot(c.x - a.x, c.z - a.z); if (L == 0) continue;
                double ux = (c.x - a.x) / L, uz = (c.z - a.z) / L;
                foreach (var r in Generate.Party.Ranges(site, b, 0, i))
                {
                    var p0 = new Vec2(a.x + ux * r.s, a.z + uz * r.s); var p1 = new Vec2(a.x + ux * r.e, a.z + uz * r.e);
                    StoreyJuice.Flash(W(b, p0, 0.05), W(b, p1, 0.05), Ok, 0.6);
                    if (r.Hp > 0.1) StoreyJuice.Flash(W(b, p0, r.Hp), W(b, p1, r.Hp), Ok, 0.6);
                }
            }
        }

        void Set(StoreyEdit e, int k0, List<Vec2> nf)
        {
            var why = OutlineIssue.None;
            e.ApplyTo("Change outline", bb => (why = Outlines.Set(bb, k0, nf)) == OutlineIssue.None);
            refusal = why == OutlineIssue.None ? null : Outlines.Why(why);
        }
    }
}
