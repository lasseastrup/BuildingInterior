#nullable enable
using System;
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
    /// The Facade tool (docs/EDITOR.md slice 6.7): the facade wall under the pointer, a ghost of what a click would do
    /// with the tool picked in the Facade tab (an entrance or terrace door, a blank wall, or a facade detail), and the
    /// click that adds it or takes it off again. A refused click says why.
    /// </summary>
    [EditorTool("Storey Facade", typeof(StoreySite), typeof(StoreyToolContext))]
    internal sealed class StoreyFacadeTool : StoreyTool
    {
        protected override StoreyTab Tab => StoreyTab.Facade;

        public override GUIContent toolbarIcon => new GUIContent("Facade", "Storey Facade: styles, entrances, blank walls, details");

        static FacadeHit? Hit(BuildingData b)
        {
            var (o, d) = MouseRay();
            return Facades.Pick(b, o, d);
        }

        protected override void ToolGUI(StoreyEdit e, BuildingData b, SceneView sv)
        {
            string tool = e.View.facadeTool;
            if (tool.Length == 0) { Hint("Pick Place doors (Doors), Place windows (Windows), or Blank wall, a detail or Bridge (Details), in the Facade tab, then click a wall."); return; }
            if (Event.current.type != EventType.Repaint) return;
            var hit = Hit(b); if (hit == null) return;
            var h = hit.Value; var site = new Site(e.Document.buildings);
            if (tool == "blank")
            {
                var fp = Derived.OutlineAt(b, h.k); var a = fp[h.i]; var c = fp[(h.i + 1) % fp.Count];
                double y0 = Derived.FloorBase(b, h.k0), y1 = Derived.FloorBase(b, Tiers.End(b, h.k0));
                var want = e.View.wallState; bool on = Tiers.State(b, h.k0, h.i) == want;
                Box(new GhostBox { cx = (a.x + c.x) / 2 + b.pos.x, cy = (y0 + y1) / 2, cz = (a.z + c.z) / 2 + b.pos.z, sx = h.L, sy = y1 - y0 + 0.2, sz = Dim.T_EXT * 2.4, u = new Vec2((c.x - a.x) / h.L, (c.z - a.z) / h.L) }, on ? Ok : Accent);
                Label(new Vector3((float)((a.x + c.x) / 2 + b.pos.x), (float)y1, (float)((a.z + c.z) / 2 + b.pos.z)), on ? "Restore the windows" : StoreySiteEditor.WallStateNames[(int)want - 1]);
            }
            else if (tool == "entrance")
            {
                var (d, err) = Facades.EntranceAt(site, b, h);
                if (d == null) { Label(At(b, h), err ?? ""); return; }
                var fp = Derived.OutlineAt(b, d.k); var a = fp[d.edge]; var c = fp[(d.edge + 1) % fp.Count];
                double dx = c.x - a.x, dz = c.z - a.z;
                Box(new GhostBox { cx = a.x + dx * d.t + b.pos.x, cy = Derived.FloorBase(b, d.k) + 1.27, cz = a.z + dz * d.t + b.pos.z, sx = Dim.DOOR_EXT - 0.05, sy = 2.5, sz = Dim.T_EXT * 2.2, u = new Vec2(dx / d.L, dz / d.L) }, d.entrance >= 0 ? Bad : Accent);
                // which door a click puts in (the Doors card's Door to place), or which it takes out
                Label(new Vector3((float)(a.x + dx * d.t + b.pos.x), (float)(Derived.FloorBase(b, d.k) + 2.7), (float)(a.z + dz * d.t + b.pos.z)),
                    d.entrance >= 0 ? "Take out " + StoreySiteEditor.PlacedName(b, b.entrances[d.entrance]) : "Put in " + StoreySiteEditor.PlaceName(e.View, b.style));
            }
            else if (tool == "window")
            {
                var w = Facades.WindowAt(site, b, h);
                if (w == null) { Label(At(b, h), "No room for a window on this wall"); return; }
                // the window there, to take out; or where one would go in, its bay's middle, as big as the style makes its windows
                var f = Details.EdgeFrameAt(b, w.k, w.edge); var st = Derived.StyleAt(b, w.k); double y = Derived.FloorBase(b, w.k);
                double u0, u1, y0, y1;
                if (w.window is { } o) { u0 = o.u0; u1 = o.u1; y0 = o.y0; y1 = o.y1; }
                else
                {
                    double fh = Derived.FloorH(b, w.k); var cu = Facade.CorniceUnder(b, w.k);
                    var ws = w.k == 0 ? Facade.GroundSpec(st, fh, cu) : Facade.WindowSpec(st.windows, fh, st, cornice: cu);
                    double c = w.bS + (w.j + 0.5) * w.bay, ww = ws == null ? 1.2 : ws.full != null ? w.bay - ws.full.Value : Math.Min(ws.w, w.bay - 0.5);
                    u0 = c - ww / 2; u1 = c + ww / 2; y0 = ws?.y0 ?? 0.9; y1 = ws?.y1 ?? 2.3;
                }
                var p = f.e.F.At((u0 + u1) / 2, y + (y0 + y1) / 2, Dim.T_EXT / 2);
                Box(new GhostBox { cx = p.x, cy = p.y, cz = p.z, sx = u1 - u0 + 0.06, sy = y1 - y0 + 0.06, sz = Dim.T_EXT * 2.2, u = f.e.u }, w.window != null ? Bad : Accent);
                // which window a click takes out, or puts in (the Windows card's Window to place)
                Label(new Vector3((float)p.x, (float)(y + y1 + 0.35), (float)p.z),
                    w.window != null ? "Take out " + (w.window.kind == null ? "the built-in window" : StoreySiteEditor.WindowName(w.window.kind.id)) : "Put in " + StoreySiteEditor.PlaceWindowName(e.View, st));
            }
            else if (tool == "bridge")
            {
                var at = Local(b, h);
                if (BridgeEdits.At(site, b, h.k, at) is var (owner, br))
                {
                    if (Bridges.Span(site, owner, br) is BridgeSpan old) { BridgeGhost(old, Bad); Label(At(b, h), "Remove the bridge"); }
                    return;
                }
                var (s, why) = BridgeEdits.Plan(site, b, h.k, at);
                if (s == null) { Label(At(b, h), why ?? ""); return; }
                BridgeGhost(s, Accent);
                Label(At(b, h), $"Bridge to {s.B.name}, {s.L:0.0} m");
            }
            else if (DetailKinds.Get(tool) != null)
            {
                var r = Facades.DetailAt(site, b, h, tool);
                if (r.error != null) { Label(At(b, h), r.error); return; }
                if (r.ghost != null) Box(r.ghost.Value, r.remove >= 0 ? Ok : Accent);
            }
        }

        /// <summary>A ghost of a bridge: its whole volume, from deck to roof.</summary>
        void BridgeGhost(BridgeSpan s, Color c)
        {
            var mid = s.F.At2(s.L / 2, 0); double y = (s.ya + s.yb) / 2 - Bridges.Deck + (Bridges.Inside + Bridges.Roof + Bridges.Deck) / 2;
            Box(new GhostBox { cx = mid.x, cy = y, cz = mid.z, sx = s.L, sy = Bridges.Inside + Bridges.Roof + Bridges.Deck, sz = s.W, u = s.u }, c);
        }

        /// <summary>The facade point hit, building-local, on its storey's outline.</summary>
        static Vec2 Local(BuildingData b, FacadeHit h)
        {
            var fp = Derived.OutlineAt(b, h.k); var a = fp[h.i]; var c = fp[(h.i + 1) % fp.Count];
            return new Vec2(a.x + (c.x - a.x) * h.t, a.z + (c.z - a.z) * h.t);
        }

        static Vector3 At(BuildingData b, FacadeHit h)
        {
            var fp = Derived.OutlineAt(b, h.k); var a = fp[h.i]; var c = fp[(h.i + 1) % fp.Count];
            return new Vector3((float)(a.x + (c.x - a.x) * h.t + b.pos.x), (float)h.y, (float)(a.z + (c.z - a.z) * h.t + b.pos.z));
        }

        protected override bool Click(StoreyEdit e, BuildingData b, Vec3d origin, Vec3d dir, SceneView sv)
        {
            string tool = e.View.facadeTool;
            if (tool.Length == 0) return false;
            var hit = Facades.Pick(b, origin, dir); if (hit == null) return false;
            var h = hit.Value; string? err = null;
            if (tool == "blank")
            {
                var want = e.View.wallState; bool on = Tiers.State(b, h.k0, h.i) == want;
                e.ApplyTo(on ? "Windows restored" : "Wall set to " + StoreySiteEditor.WallStateNames[(int)want - 1].ToLowerInvariant(), bb => { Facades.ToggleState(bb, h.k, h.i, want); return true; });
            }
            else if (tool == "entrance")
            {
                // the door the Doors card says, its asset kept in the site's list so builds include it
                string? door = StoreySiteEditor.DoorToPlace(e.View, b.style);
                e.ApplyTo("Door", bb => (err = Facades.ToggleEntrance(new Site(e.Document.buildings), bb, h, door)) == null);
                if (err == null && door != null && StoreyOpenings.All().FirstOrDefault(o => o.Id == door) is { } asset) StoreyOpenings.Keep(e.Site, asset);
            }
            else if (tool == "window")
            {
                // the window the Windows card says, its asset kept in the site's list so builds include it
                string? win = StoreySiteEditor.WindowToPlace(e.View, Derived.StyleAt(b, h.k));
                e.ApplyTo("Window", bb => (err = Facades.ToggleWindow(new Site(e.Document.buildings), bb, h, win)) == null);
                if (err == null && win != null && StoreyOpenings.All().FirstOrDefault(o => o.Id == win) is { } asset) StoreyOpenings.Keep(e.Site, asset);
            }
            else if (tool == "bridge")
            {
                var at = Local(b, h); string bid = b.id; int k = h.k;
                var found = BridgeEdits.At(new Site(e.Document.buildings), b, k, at);
                if (found is var (owner, br)) { string oid = owner.id, id = br.id; e.Apply("Bridge removed", d => BridgeEdits.Remove(d.buildings.First(x => x.id == oid), id)); }
                else e.Apply("Bridge added", d => { var a = d.buildings.First(x => x.id == bid); return (err = BridgeEdits.Add(new Site(d.buildings), a, k, at).why) == null; });
            }
            else if (DetailKinds.Get(tool) is DetailKind kind)
            {
                e.ApplyTo(kind.name, bb => (err = Facades.ToggleDetail(new Site(e.Document.buildings), bb, h, tool)) == null);
                if (err == null) StoreyDetails.Keep(e.Site, tool);   // an artist's: builds include it
            }
            if (err != null) Notify(sv, err);
            return true;
        }
    }
}
