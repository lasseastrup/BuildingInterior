#nullable enable
using System;
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
    [EditorTool("Storey Facade", typeof(StoreySite))]
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
            if (tool.Length == 0) { Hint("Pick Entrance, Blank wall or a detail in the Facade tab, then click a wall."); return; }
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
            else if (Enum.TryParse<DetailKind>(tool, true, out var kind))
            {
                var r = Facades.DetailAt(site, b, h, kind);
                if (r.error != null) { Label(At(b, h), r.error); return; }
                if (r.ghost != null) Box(r.ghost.Value, r.remove >= 0 ? Ok : Accent);
            }
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
            else if (Enum.TryParse<DetailKind>(tool, true, out var kind))
                e.ApplyTo(Details.Catalogue[kind].label, bb => (err = Facades.ToggleDetail(new Site(e.Document.buildings), bb, h, kind)) == null);
            if (err != null) Notify(sv, err);
            return true;
        }

        protected override bool Key(StoreyEdit e, BuildingData b, KeyCode key)
        {
            if (key != KeyCode.Escape || e.View.facadeTool.Length == 0) return false;
            e.View.facadeTool = "";
            return true;
        }
    }
}
