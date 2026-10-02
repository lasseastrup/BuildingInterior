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

        enum Drag { None, Corner, Insert, Push, Move, VoidCorner, VoidMove }
        Drag drag;
        int dragIndex, dragControl;
        List<Vec2>? dragBase;
        Vector3 dragStart;
        Vec2 moveOffset;
        string? refusal;
        // control ids by kind of handle, so adding a corner mid-drag does not renumber the other handles
        static readonly int CornerHint = "StoreyCorner".GetHashCode(), InsertHint = "StoreyInsert".GetHashCode(), PushHint = "StoreyPush".GetHashCode(), MoveHint = "StoreyMove".GetHashCode();
        static readonly int VoidCornerHint = "StoreyVoidCorner".GetHashCode(), VoidInsertHint = "StoreyVoidInsert".GetHashCode(), VoidMoveHint = "StoreyVoidMove".GetHashCode();
        Vec2 voidGrab;

        protected override void ToolGUI(StoreyEdit e, BuildingData b, SceneView sv)
        {
            int k0 = e.View.tier;
            // the handles are the sharp outline's: a cut corner is one point (CornerCuts), and every change goes through
            // CornerCuts.Around, which cuts the corners again after it
            var (fp, cuts) = CornerCuts.Sharp(b, k0);
            double y = Derived.FloorBase(b, k0) + 0.02;
            var ev = Event.current;
            bool alt = ev.alt;

            if (k0 > 0) Dotted(b, Derived.OutlineAt(b, k0 - 1), y, Faint);
            Outline(b, Tiers.Outline(b, k0), y, Accent);
            // each cut corner's sharp point, faint, where its two edges would meet
            if (ev.type == EventType.Repaint)
                foreach (var c in cuts)
                {
                    var A = fp[(c.i - 1 + fp.Count) % fp.Count]; var C = fp[(c.i + 1) % fp.Count];
                    Handles.color = Faint;
                    Handles.DrawDottedLine(W(b, Lerp(c.at, A, 0.25), y), W(b, c.at, y), 3f);
                    Handles.DrawDottedLine(W(b, c.at, y), W(b, Lerp(c.at, C, 0.25), y), 3f);
                }
            if (e.View.selectedCorner >= fp.Count) e.View.selectedCorner = -1;
            // the selected corner's cut, as the Shape tab would make it (a corner cut already shows its own)
            if (e.View.selectedCorner >= 0 && drag == Drag.None && ev.type == EventType.Repaint && !cuts.Any(c => c.i == e.View.selectedCorner))
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
                    if (!e.ApplyTo("Corner removed", bb => CornerCuts.Around(bb, k0, (x, _) => Outlines.RemoveVertex(x, ii, k0)))) Notify(sv, "An outline needs at least three corners");
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
                    e.ApplyTo("Add corner", bb => CornerCuts.Around(bb, k0, (x, _) => { ni = Outlines.InsertVertex(x, ii, k0); return true; }));
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

            VoidHandles(e, b, sv, alt);

            if (drag != Drag.None)
            {
                // edge lengths while dragging, and why the last change was refused
                var dv = drag == Drag.VoidCorner || drag == Drag.VoidMove ? Voids.Of(e.Selected!, e.View.selectedVoid) : null;
                var cur = dv != null ? dv.shape : Tiers.Outline(e.Selected!, k0);
                double ly = dv != null ? Derived.FloorBase(e.Selected!, System.Math.Min(dv.bottom, e.Selected!.floors.Count)) + 0.03 : y;
                for (int i = 0; i < cur.Count; i++)
                {
                    var a = cur[i]; var c = cur[(i + 1) % cur.Count];
                    Label(W(e.Selected!, new Vec2((a.x + c.x) / 2, (a.z + c.z) / 2), ly), $"{Tiers.EdgeLen(cur, i):0.00} m");
                }
                if (refusal != null) Label(AlongRay(10), refusal);
                // the drag ends when its handle lets go of the mouse, whichever event that came in (a used MouseUp
                // no longer reads as one)
                if (drag != Drag.Insert && GUIUtility.hotControl != dragControl) Finish(e, k0);
            }
        }

        /// <summary>
        /// The building's courtyards and atria, drawn green at their bottom floor: click one's outline or centre to select
        /// it; the selected one's corners drag, a "+" on an edge adds a corner, a double-click removes one, and the centre
        /// handle moves it. A change that does not fit is refused, and says why.
        /// </summary>
        void VoidHandles(StoreyEdit e, BuildingData b, SceneView sv, bool alt)
        {
            var ev = Event.current;
            foreach (var v in b.voids.ToList())
            {
                if (v.shape.Count < 3) continue;
                double y = Derived.FloorBase(b, System.Math.Min(v.bottom, b.floors.Count)) + 0.03;
                bool sel = e.View.selectedVoid == v.id; string id = v.id;
                if (ev.type == EventType.Repaint)
                {
                    var ring = v.shape.Select(p => W(b, p, y)).ToList(); ring.Add(ring[0]);
                    Handles.color = sel ? Ok : new Color(Ok.r, Ok.g, Ok.b, 0.6f);
                    if (sel) Handles.DrawAAPolyLine(4f, ring.ToArray()); else Handles.DrawDottedLines(ring.SelectMany((p, i) => i + 1 < ring.Count ? new[] { p, ring[i + 1] } : new Vector3[0]).ToArray(), 4f);
                }
                double cx = v.shape.Average(p => p.x), cz = v.shape.Average(p => p.z);
                var centre = W(b, new Vec2(cx, cz), y);
                int mid = GUIUtility.GetControlID(VoidMoveHint, FocusType.Passive);
                Handles.color = Ok;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(mid, centre, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(centre, sel ? 0.13f : 0.09f), Handles.CircleHandleCap, Vector2.zero, false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (!sel) { e.View.selectedVoid = id; Inspectors(); }
                    if (drag != Drag.VoidMove) { BeginDragOf(e, Drag.VoidMove, 0, mid, "Move courtyard"); voidGrab = new Vec2(np.x, np.z); }
                    double dx = Tiers.Cm(np.x - voidGrab.x), dz = Tiers.Cm(np.z - voidGrab.z);
                    if (dx != 0 || dz != 0)
                    {
                        var why = VoidIssue.None;
                        if (e.ApplyTo("Move courtyard", bb => (why = Voids.Move(bb, id, dx, dz)) == VoidIssue.None)) { voidGrab = new Vec2(voidGrab.x + dx, voidGrab.z + dz); refusal = null; }
                        else refusal = Voids.Why(why);
                    }
                }
                if (!sel) continue;
                var shape = v.shape;
                for (int i = 0; i < shape.Count; i++)
                {
                    int cid = GUIUtility.GetControlID(VoidCornerHint, FocusType.Passive);
                    var at = W(b, shape[i], y);
                    if (ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2 && HandleUtility.nearestControl == cid)
                    {
                        int ii = i;
                        if (!e.ApplyTo("Courtyard corner removed", bb => Voids.RemoveVertex(bb, id, ii))) Notify(sv, "A courtyard needs at least three corners, and room to fit");
                        ev.Use(); return;
                    }
                    Handles.color = Ok;
                    EditorGUI.BeginChangeCheck();
                    var cp = Handles.Slider2D(cid, at, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(at), Handles.DotHandleCap, Vector2.zero, false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        BeginDragOf(e, Drag.VoidCorner, i, cid, "Move courtyard corner");
                        var q = new Vec2(Tiers.Cm(cp.x - b.pos.x), Tiers.Cm(cp.z - b.pos.z));
                        if (!alt)
                        {
                            // in line with the neighbouring corners
                            int n = shape.Count;
                            foreach (int j in new[] { (i - 1 + n) % n, (i + 1) % n }) { var o = shape[j]; if (System.Math.Abs(o.x - q.x) < 0.4) q.x = o.x; if (System.Math.Abs(o.z - q.z) < 0.4) q.z = o.z; }
                        }
                        if (shape[i].x != q.x || shape[i].z != q.z)
                        {
                            var nf = Tiers.Copy(shape); nf[i] = q; int ii = i; var why = VoidIssue.None;
                            refusal = e.ApplyTo("Move courtyard corner", bb => (why = Voids.SetShape(bb, id, nf)) == VoidIssue.None) ? null : Voids.Why(why);
                        }
                    }
                }
                for (int i = 0; i < shape.Count; i++)
                {
                    var a = shape[i]; var c = shape[(i + 1) % shape.Count];
                    int pid = GUIUtility.GetControlID(VoidInsertHint, FocusType.Passive);
                    if (Tiers.EdgeLen(shape, i) <= 1.2) continue;
                    var m = W(b, new Vec2((a.x + c.x) / 2, (a.z + c.z) / 2), y);
                    Handles.color = Faint;
                    if (Handles.Button(m, Quaternion.LookRotation(Vector3.up), Size(m, 0.05f), Size(m, 0.07f), Handles.RectangleHandleCap))
                    {
                        int ii = i;
                        e.ApplyTo("Courtyard corner added", bb => { Voids.InsertVertex(bb, id, ii); return true; });
                    }
                }
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
                e.ApplyTo("Tidy outline", bb => { bool ch = false; CornerCuts.Around(bb, k0, (x, _) => ch = Outlines.Simplify(x, k0)); return ch; });
            e.EndDrag();
            drag = Drag.None; dragBase = null; refusal = null;
            Inspectors();
        }

        static Vec2 Lerp(Vec2 a, Vec2 c, double t) => new Vec2(a.x + (c.x - a.x) * t, a.z + (c.z - a.z) * t);

        void MoveCorner(StoreyEdit e, BuildingData b, int k0, int i, Vector3 np, bool alt)
        {
            var fp = CornerCuts.Sharp(b, k0).sharp;
            if (i < 0 || i >= fp.Count) return;
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
            int lost = 0;
            bool Op(BuildingData bb) => CornerCuts.Around(bb, k0, (x, _) => (why = Outlines.Set(x, k0, nf)) == OutlineIssue.None, out lost);
            // a corner or wall drag is redone from where it began, so a cut that stops fitting on the way comes back
            if (drag == Drag.Corner || drag == Drag.Push) e.ApplyFromDragStart("Change outline", Op);
            else e.ApplyTo("Change outline", Op);
            refusal = why != OutlineIssue.None ? Outlines.Why(why) : lost > 0 ? "A cut corner's edges are too short for its cut: it's sharp until they're longer" : null;
        }
    }
}
