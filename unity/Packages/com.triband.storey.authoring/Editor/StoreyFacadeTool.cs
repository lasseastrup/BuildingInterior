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
            if (tool.Length == 0) { Hint("Pick Entrance, Blank wall, a detail or Bridge in the Facade tab, then click a wall."); return; }
            if (Event.current.type != EventType.Repaint) return;
            var hit = Hit(b); if (hit == null) return;
            var h = hit.Value; var site = new Site(e.Document.buildings);
            if (tool == "blank")
            {
                var fp = Derived.OutlineAt(b, h.k); var a = fp[h.i]; var c = fp[(h.i + 1) % fp.Count];
                double y0 = Derived.FloorBase(b, h.k0), y1 = Derived.FloorBase(b, Tiers.End(b, h.k0));
                bool on = Tiers.Blank(b, h.k0).Contains(h.i);
                Box(new GhostBox { cx = (a.x + c.x) / 2 + b.pos.x, cy = (y0 + y1) / 2, cz = (a.z + c.z) / 2 + b.pos.z, sx = h.L, sy = y1 - y0 + 0.2, sz = Dim.T_EXT * 2.4, u = new Vec2((c.x - a.x) / h.L, (c.z - a.z) / h.L) }, on ? Ok : Accent);
                Label(new Vector3((float)((a.x + c.x) / 2 + b.pos.x), (float)y1, (float)((a.z + c.z) / 2 + b.pos.z)), on ? "Restore the windows" : "Blank wall");
            }
            else if (tool == "entrance")
            {
                var (d, err) = Facades.EntranceAt(site, b, h);
                if (d == null) { Label(At(b, h), err ?? ""); return; }
                var fp = Derived.OutlineAt(b, d.k); var a = fp[d.edge]; var c = fp[(d.edge + 1) % fp.Count];
                double dx = c.x - a.x, dz = c.z - a.z;
                Box(new GhostBox { cx = a.x + dx * d.t + b.pos.x, cy = Derived.FloorBase(b, d.k) + 1.27, cz = a.z + dz * d.t + b.pos.z, sx = Dim.DOOR_EXT - 0.05, sy = 2.5, sz = Dim.T_EXT * 2.2, u = new Vec2(dx / d.L, dz / d.L) }, d.entrance >= 0 ? Bad : Accent);
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
            else if (Enum.TryParse<DetailKind>(tool, true, out var kind))
            {
                var r = Facades.DetailAt(site, b, h, kind);
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
                bool on = Tiers.Blank(b, h.k0).Contains(h.i);
                e.ApplyTo(on ? "Windows restored" : "Wall set to blank", bb => { Facades.ToggleBlank(bb, h.k, h.i); return true; });
            }
            else if (tool == "entrance")
                e.ApplyTo("Entrance", bb => (err = Facades.ToggleEntrance(new Site(e.Document.buildings), bb, h)) == null);
            else if (tool == "bridge")
            {
                var at = Local(b, h); string bid = b.id; int k = h.k;
                var found = BridgeEdits.At(new Site(e.Document.buildings), b, k, at);
                if (found is var (owner, br)) { string oid = owner.id, id = br.id; e.Apply("Bridge removed", d => BridgeEdits.Remove(d.buildings.First(x => x.id == oid), id)); }
                else e.Apply("Bridge added", d => { var a = d.buildings.First(x => x.id == bid); return (err = BridgeEdits.Add(new Site(d.buildings), a, k, at).why) == null; });
            }
            else if (Enum.TryParse<DetailKind>(tool, true, out var kind))
                e.ApplyTo(Details.Catalogue[kind].label, bb => (err = Facades.ToggleDetail(new Site(e.Document.buildings), bb, h, kind)) == null);
            if (err != null) Notify(sv, err);
            return true;
        }
    }
}
