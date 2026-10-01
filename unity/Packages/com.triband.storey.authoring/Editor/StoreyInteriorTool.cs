#nullable enable
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
    /// The Interior tool (docs/EDITOR.md slice 6.6) on the active storey, with the floors above clipped and the walls in
    /// the way cut away as in play. Select (V): drag stairs and lifts, wall joints and the "+" on a wall; click a wall to
    /// select it; Delete removes, R turns a core. Wall (W): click to chain walls, or drag one; snaps to points (ring) and
    /// walls (diamond), Alt for free, Esc to finish. Door (D), Erase (X), Stairs (S), Lift (L; R turns the new core).
    /// </summary>
    [EditorTool("Storey Interior", typeof(StoreySite))]
    internal sealed class StoreyInteriorTool : StoreyTool
    {
        protected override StoreyTab Tab => StoreyTab.Interior;

        public override GUIContent toolbarIcon => new GUIContent("Interior", "Storey Interior: walls, doors, stairs and lifts");

        static readonly int CoreHint = "StoreyCore".GetHashCode(), JointHint = "StoreyJoint".GetHashCode(), SplitHint = "StoreySplit".GetHashCode();

        // the Wall tool's chain
        bool chain, fresh;
        Vec2 chainStart;
        // a drag in progress with the Select tool
        enum Drag { None, Core, Joint, Split }
        Drag drag;
        string dragKey = ""; int dragWall = -1; Vec2 grabOffset; Vector3 dragAt;

        protected override void ToolGUI(StoreyEdit e, BuildingData b, SceneView sv)
        {
            var v = e.View; int k = v.floor, N = b.floors.Count;
            if (!b.interior) { Hint("Shell only: switch to Walk-in interior in the Interior tab to edit rooms."); return; }
            if (k == N) { Hint("The roof: roof access is set per stair or lift in the Interior tab. Pick a floor below to edit rooms."); return; }
            double y = Derived.FloorBase(b, k), yy = y + 0.02;
            var ev = Event.current; bool alt = ev.alt;
            var walls = b.floors[k].walls;

            // what is on this storey
            for (int wi = 0; wi < walls.Count; wi++)
            {
                Handles.color = wi == v.selectedWall ? Ok : Color.white;
                Handles.DrawAAPolyLine(wi == v.selectedWall ? 5f : 3f, W(b, walls[wi].a, yy), W(b, walls[wi].b, yy));
                foreach (var d in walls[wi].doors)
                {
                    var w = walls[wi]; var at = new Vec2(w.a.x + (w.b.x - w.a.x) * d.t, w.a.z + (w.b.z - w.a.z) * d.t);
                    Handles.color = Accent; Handles.DrawWireDisc(W(b, at, yy), Vector3.up, (float)(Dim.DOOR_INT / 2));
                }
            }
            foreach (var s in b.shafts.Where(s => Cores.Levels(b, s).Contains(k))) CoreRect(b, s, yy, s.id == v.selectedCore ? Ok : Color.white);

            var (o, dr) = MouseRay();
            var p = Picking.OnPlane(b, o, dr, y);
            var site = new Site(e.Document.buildings);

            switch (v.interiorTool)
            {
                case InteriorTool.Select: SelectHandles(e, b, k, yy, alt); break;
                case InteriorTool.Wall:
                    if (p == null) break;
                    {
                        var q = Walls.Snap(b, k, p.Value, new SnapOptions { from = chain ? chainStart : (Vec2?)null, free = alt });
                        Marker(b, q, yy);
                        if (chain)
                        {
                            Handles.color = Accent; Handles.DrawAAPolyLine(4f, W(b, chainStart, yy), W(b, q.Point, yy));
                            Label(W(b, q.Point, yy), $"{Tiers.Hypot(q.x - chainStart.x, q.z - chainStart.z):0.00} m");
                        }
                    }
                    break;
                case InteriorTool.Door:
                    if (p == null) break;
                    {
                        var d = Walls.DoorAt(site, b, k, p.Value);
                        if (d != null) Box(DoorBox(b, k, d), d.door >= 0 || d.entrance >= 0 ? Bad : Accent);
                    }
                    break;
                case InteriorTool.Erase:
                    if (p == null) break;
                    {
                        var t = Walls.HoverTarget(b, k, p.Value);
                        if (t != null) Highlight(b, k, t.Value, Bad);
                    }
                    break;
                case InteriorTool.Stairs:
                case InteriorTool.Lift:
                    if (p == null) break;
                    {
                        var (it, ok) = Shafts.Placement(b, k, p.Value, v.interiorTool == InteriorTool.Stairs ? CoreType.Stairs : CoreType.Lift, v.placeRot);
                        CoreRect(b, it, yy, ok ? Accent : Bad);
                    }
                    break;
            }
        }

        // ---- Select: cores, joints, wall splits ----

        void SelectHandles(StoreyEdit e, BuildingData b, int k, double yy, bool alt)
        {
            var ev = Event.current; var v = e.View; var walls = b.floors[k].walls;
            foreach (var s in b.shafts.Where(s => Cores.Levels(b, s).Contains(k)).ToList())
            {
                int id = GUIUtility.GetControlID(CoreHint, FocusType.Passive);
                var at = W(b, new Vec2(s.x, s.z), yy);
                Handles.color = s.id == v.selectedCore ? Ok : Color.white;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(id, at, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(at, 0.2f), Handles.RectangleHandleCap, Vector2.zero, false);
                if (EditorGUI.EndChangeCheck())
                {
                    if (drag == Drag.None)
                    {
                        drag = Drag.Core; e.BeginDrag(s.type == CoreType.Stairs ? "Move stairs" : "Move lift");
                        v.selectedCore = s.id; v.selectedWall = -1; Inspectors();
                        var g = L(b, np); grabOffset = new Vec2(s.x - g.x, s.z - g.z);
                    }
                    var to = L(b, np); string sid = s.id;
                    e.ApplyTo("Move core", bb => { var it = bb.shafts.First(x => x.id == sid); return Shafts.Drag(bb, it, new Vec2(it.x - grabOffset.x, it.z - grabOffset.z), to); });
                }
            }

            var nodes = Walls.Nodes(walls);
            foreach (var n in nodes)
            {
                int id = GUIUtility.GetControlID(JointHint, FocusType.Passive);
                var pt = n[0].atB ? walls[n[0].wall].b : walls[n[0].wall].a; var at = W(b, pt, yy);
                string key = Walls.Key(pt);
                if (ev.type == EventType.MouseDown && ev.button == 0 && ev.clickCount == 2 && HandleUtility.nearestControl == id)
                {
                    string? msg = null;
                    e.ApplyTo("Walls joined or removed", bb => (msg = Walls.RemoveJoint(bb, k, key)) != null);
                    if (msg != null && SceneView.lastActiveSceneView != null) Notify(SceneView.lastActiveSceneView, msg);
                    ev.Use(); return;
                }
                Handles.color = Color.white;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(id, drag == Drag.Joint && dragKey == key ? dragAt : at, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(at, 0.06f), Handles.DotHandleCap, Vector2.zero, false);
                if (EditorGUI.EndChangeCheck()) { drag = Drag.Joint; dragKey = key; dragAt = np; }
                if (drag == Drag.Joint && dragKey == key)
                {
                    // preview: the walls ending here, to where the joint would land
                    var q = Walls.Snap(b, k, L(b, dragAt), new SnapOptions { free = alt });
                    foreach (var (wi, atB) in n) { Handles.color = Accent; Handles.DrawDottedLine(W(b, atB ? walls[wi].a : walls[wi].b, yy), W(b, q.Point, yy), 4f); }
                    Marker(b, q, yy);
                }
            }

            for (int wi = 0; wi < walls.Count; wi++)
            {
                int id = GUIUtility.GetControlID(SplitHint, FocusType.Passive);
                var w = walls[wi]; if (Tiers.Hypot(w.b.x - w.a.x, w.b.z - w.a.z) <= 1.2) continue;
                var mid = W(b, new Vec2((w.a.x + w.b.x) / 2, (w.a.z + w.b.z) / 2), yy);
                Handles.color = Faint;
                EditorGUI.BeginChangeCheck();
                var np = Handles.Slider2D(id, drag == Drag.Split && dragWall == wi ? dragAt : mid, Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Size(mid, 0.05f), Handles.RectangleHandleCap, Vector2.zero, false);
                if (EditorGUI.EndChangeCheck()) { drag = Drag.Split; dragWall = wi; dragAt = np; }
                if (drag == Drag.Split && dragWall == wi)
                {
                    Handles.color = Accent;
                    Handles.DrawDottedLine(W(b, w.a, yy), dragAt, 4f); Handles.DrawDottedLine(dragAt, W(b, w.b, yy), 4f);
                }
            }

            if (drag != Drag.None && ev.rawType == EventType.MouseUp && GUIUtility.hotControl == 0)
            {
                var to = L(b, dragAt); string key = dragKey; int wall = dragWall;
                if (drag == Drag.Core) e.EndDrag();
                else if (drag == Drag.Joint) e.ApplyTo("Move wall point", bb => Walls.MoveJoint(bb, k, key, to, alt));
                else if (drag == Drag.Split) e.ApplyTo("Split wall", bb => { Walls.SplitAndDrag(bb, k, wall, to, alt); return true; });
                drag = Drag.None; dragKey = ""; dragWall = -1;
                Inspectors();
            }
        }

        // ---- clicks ----

        protected override bool Click(StoreyEdit e, BuildingData b, Vec3d origin, Vec3d dir, SceneView sv)
        {
            var v = e.View; int k = v.floor;
            if (!b.interior || k >= b.floors.Count) return false;
            var hit = Picking.OnPlane(b, origin, dir, Derived.FloorBase(b, k)); if (hit == null) return false;
            var p = hit.Value; var site = new Site(e.Document.buildings);
            switch (v.interiorTool)
            {
                case InteriorTool.Select:
                {
                    var w = Walls.NearestWall(b, k, p, 0.4);
                    v.selectedCore = ""; v.selectedWall = w?.i ?? -1; Inspectors();
                    return w != null || Shafts.At(b, k, p) != null || Generate.Geo.Pip(Derived.OutlineAt(b, k), p.x, p.z);
                }
                case InteriorTool.Wall:
                {
                    var q = Walls.Snap(b, k, p, new SnapOptions { from = chain ? chainStart : (Vec2?)null, free = Event.current.alt });
                    if (!chain) { chainStart = q.Point; chain = true; fresh = true; } else fresh = false;
                    return true;
                }
                case InteriorTool.Door:
                    if (Walls.DoorAt(site, b, k, p) == null) return false;
                    e.ApplyTo("Doorway", bb => Walls.ToggleDoor(new Site(e.Document.buildings), bb, k, p));
                    return true;
                case InteriorTool.Erase:
                {
                    var t = Walls.HoverTarget(b, k, p); if (t == null) return false;
                    e.ApplyTo("Removed", bb => { Walls.Erase(bb, k, t.Value); return true; });
                    v.selectedCore = ""; v.selectedWall = -1;
                    return true;
                }
                case InteriorTool.Stairs:
                case InteriorTool.Lift:
                {
                    var type = v.interiorTool == InteriorTool.Stairs ? CoreType.Stairs : CoreType.Lift;
                    bool placed = e.Apply(type == CoreType.Stairs ? "Stairs added" : "Lift added", d =>
                    {
                        var bb = d.buildings.First(x => x.id == b.id);
                        return Shafts.Place(bb, k, p, type, v.placeRot, Shafts.NewId(d)) != null;
                    });
                    if (!placed) Notify(sv, "Needs room inside the footprint, clear of other cores");
                    return true;
                }
            }
            return false;
        }

        protected override void Release(StoreyEdit e, BuildingData b, Vec3d origin, Vec3d dir, SceneView sv)
        {
            var v = e.View; int k = v.floor;
            if (v.interiorTool != InteriorTool.Wall || !chain || k >= b.floors.Count) return;
            var hit = Picking.OnPlane(b, origin, dir, Derived.FloorBase(b, k)); if (hit == null) return;
            var q = Walls.Snap(b, k, hit.Value, new SnapOptions { from = chainStart, free = Event.current.alt }).Point;
            var s = chainStart; double len = Tiers.Hypot(q.x - s.x, q.z - s.z);
            if (fresh) { if (len > 0.5) { e.ApplyTo("Wall added", bb => { Walls.Add(bb, k, s, q); return true; }); chain = false; } }   // a drag draws one wall
            else if (len < 0.3) chain = false;                                                                                      // a click on the last point ends the chain
            else { e.ApplyTo("Wall added", bb => { Walls.Add(bb, k, s, q); return true; }); chainStart = q; }
        }

        protected override bool Key(StoreyEdit e, BuildingData b, KeyCode key)
        {
            var v = e.View;
            switch (key)
            {
                case KeyCode.V: v.interiorTool = InteriorTool.Select; chain = false; return true;
                case KeyCode.W: v.interiorTool = InteriorTool.Wall; return true;
                case KeyCode.D: v.interiorTool = InteriorTool.Door; chain = false; return true;
                case KeyCode.X: v.interiorTool = InteriorTool.Erase; chain = false; return true;
                case KeyCode.S: v.interiorTool = InteriorTool.Stairs; chain = false; return true;
                case KeyCode.L: v.interiorTool = InteriorTool.Lift; chain = false; return true;
                case KeyCode.Escape: if (!chain) return false; chain = false; return true;
                case KeyCode.R:
                    if (v.interiorTool == InteriorTool.Stairs || v.interiorTool == InteriorTool.Lift) { v.placeRot = (v.placeRot + 90) % 360; return true; }
                    if (v.selectedCore.Length > 0)
                    {
                        string id = v.selectedCore;
                        if (!e.ApplyTo("Turn core", bb => { var s = bb.shafts.First(x => x.id == id); return Shafts.SetAngle(bb, s, s.rot + 90); }) && SceneView.lastActiveSceneView != null)
                            Notify(SceneView.lastActiveSceneView, "No room to rotate here");
                        return true;
                    }
                    return false;
                case KeyCode.Delete:
                case KeyCode.Backspace:
                    if (v.selectedCore.Length > 0) { string id = v.selectedCore; e.ApplyTo("Removed", bb => bb.shafts.RemoveAll(x => x.id == id) > 0); v.selectedCore = ""; return true; }
                    if (v.selectedWall >= 0) { int wi = v.selectedWall, k = v.floor; e.ApplyTo("Removed", bb => { if (wi >= bb.floors[k].walls.Count) return false; bb.floors[k].walls.RemoveAt(wi); return true; }); v.selectedWall = -1; return true; }
                    return false;
            }
            return false;
        }

        // ---- drawing ----

        /// <summary>The snap marker: a ring on a point, a diamond on a wall, and dotted alignment guides.</summary>
        static void Marker(BuildingData b, WallSnap q, double y)
        {
            var at = W(b, q.Point, y); float r = Size(at, 0.06f);
            Handles.color = Accent;
            if (q.kind == SnapKind.Node) Handles.DrawWireDisc(at, Vector3.up, r);
            else if (q.kind == SnapKind.Edge) Handles.DrawAAPolyLine(2f, at + new Vector3(r, 0, 0), at + new Vector3(0, 0, r), at - new Vector3(r, 0, 0), at - new Vector3(0, 0, r), at + new Vector3(r, 0, 0));
            foreach (var g in q.guides) Handles.DrawDottedLine(W(b, g, y), at, 3f);
        }

        static GhostBox DoorBox(BuildingData b, int k, DoorSpot d)
        {
            double y = Derived.FloorBase(b, k);
            if (!d.exterior)
            {
                var w = b.floors[k].walls[d.wall]; double dx = w.b.x - w.a.x, dz = w.b.z - w.a.z;
                return new GhostBox { cx = w.a.x + dx * d.t + b.pos.x, cy = y + 1.08, cz = w.a.z + dz * d.t + b.pos.z, sx = Dim.DOOR_INT - 0.05, sy = 2.13, sz = Dim.T_INT + 0.12, u = new Vec2(dx / d.L, dz / d.L) };
            }
            var fp = Derived.OutlineAt(b, d.k); var a = fp[d.edge]; var c = fp[(d.edge + 1) % fp.Count]; double ex = c.x - a.x, ez = c.z - a.z;
            return new GhostBox { cx = a.x + ex * d.t + b.pos.x, cy = Derived.FloorBase(b, d.k) + 1.27, cz = a.z + ez * d.t + b.pos.z, sx = Dim.DOOR_EXT - 0.05, sy = 2.5, sz = Dim.T_EXT * 2.2, u = new Vec2(ex / d.L, ez / d.L) };
        }

        static void Highlight(BuildingData b, int k, Target t, Color c)
        {
            double y = Derived.FloorBase(b, k), h = Derived.FloorH(b, k);
            switch (t.kind)
            {
                case TargetKind.Shaft: CoreRect(b, b.shafts[t.index], y + 0.05, c); break;
                case TargetKind.Wall:
                {
                    var w = b.floors[k].walls[t.index]; double L = Tiers.Hypot(w.b.x - w.a.x, w.b.z - w.a.z); if (L == 0) L = 1;
                    Box(new GhostBox { cx = (w.a.x + w.b.x) / 2 + b.pos.x, cy = y + h / 2, cz = (w.a.z + w.b.z) / 2 + b.pos.z, sx = L + Dim.T_INT, sy = h + 0.05, sz = Dim.T_INT + 0.08, u = new Vec2((w.b.x - w.a.x) / L, (w.b.z - w.a.z) / L) }, c);
                    break;
                }
                case TargetKind.Door:
                {
                    var w = b.floors[k].walls[t.index]; double L = Tiers.Hypot(w.b.x - w.a.x, w.b.z - w.a.z); if (L == 0) L = 1;
                    Box(DoorBox(b, k, new DoorSpot { wall = t.index, t = w.doors[t.door].t, L = L, k = k }), c);
                    break;
                }
                case TargetKind.Entrance:
                {
                    var en = b.entrances[t.index]; var fp = Derived.OutlineAt(b, en.k); var a = fp[en.edge]; var cc = fp[(en.edge + 1) % fp.Count];
                    Box(DoorBox(b, en.k, new DoorSpot { exterior = true, edge = en.edge, t = en.t, L = Tiers.Hypot(cc.x - a.x, cc.z - a.z), k = en.k }), c);
                    break;
                }
            }
        }
    }
}
