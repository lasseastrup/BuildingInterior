#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>Buildings of a layout: new from a footprint preset, duplicated, deleted, named. Ported from the prototype.</summary>
    public static class Buildings
    {
        /// <summary>The grid new buildings are placed on.</summary>
        public const double Grid = 0.25;

        static double SnapG(double v) => Tiers.JsRound(v / Grid) * Grid;

        static readonly string[] Names = { "Alder", "Birch", "Cedar", "Dogwood", "Elm", "Fir", "Ginkgo", "Hazel", "Ironwood", "Juniper", "Kauri", "Larch", "Maple" };

        /// <summary>The first unused tree name, then "Building n".</summary>
        public static string NextName(StoreyDocument d)
        {
            foreach (var n in Names) { string nm = n + " House"; if (!d.buildings.Any(b => b.name == nm)) return nm; }
            return "Building " + d.seq;
        }

        public static string NewId(StoreyDocument d) => "b" + d.seq++;

        /// <summary>A building from a footprint preset and a style preset: one floor, one entrance on the first edge, walk-in.</summary>
        public static BuildingData Make(StoreyDocument d, string shape, Vec2 pos, string styleKey = "brick", string? name = null) => new BuildingData
        {
            id = NewId(d), name = name ?? NextName(d), pos = pos, footprint = Setbacks.Preset(shape),
            floors = new List<FloorData> { new FloorData() }, groundHeight = 3.6, floorHeight = 3.0,
            entrances = new List<EntranceData> { new EntranceData { edge = 0, t = 0.5 } }, style = Styles.Preset(styleKey), interior = true,
        };

        /// <summary>
        /// A free spot for a w × d footprint near a point: rings around it, on the grid, 3 m clear of every other
        /// building's footprint.
        /// </summary>
        public static Vec2 FreeSpot(StoreyDocument d, double w, double dd, Vec2 near)
        {
            bool Occupied(double x, double z) => d.buildings.Any(b =>
            {
                var bb = Tiers.Bbox(b.footprint);
                return x < bb.x1 + b.pos.x + 3 && x + w > bb.x0 + b.pos.x - 3 && z < bb.z1 + b.pos.z + 3 && z + dd > bb.z0 + b.pos.z - 3;
            });
            for (int r = 0; r < 16; r++)
                for (int a = 0; a < Math.Max(1, r * 8); a++)
                {
                    double ang = (double)a / Math.Max(1, r * 8) * Math.PI * 2;
                    double x = SnapG(near.x + Math.Cos(ang) * r * 8 - w / 2), z = SnapG(near.z + Math.Sin(ang) * r * 8 - dd / 2);
                    if (!Occupied(x, z)) return new Vec2(x, z);
                }
            return new Vec2(SnapG(near.x), SnapG(near.z + 30));
        }

        /// <summary>New: a 3-storey brick building from a footprint preset, in a free spot near a point. Returns it (added last).</summary>
        public static BuildingData Add(StoreyDocument d, string shape, Vec2 near)
        {
            var fp = Setbacks.Preset(shape);
            var pos = FreeSpot(d, fp.Max(p => p.x), fp.Max(p => p.z), near);
            var b = Make(d, shape, pos);
            b.floors = new List<FloorData> { new FloorData(), new FloorData(), new FloorData() };
            d.buildings.Add(b);
            return b;
        }

        /// <summary>A copy of a building in a free spot near a point, with new ids for it and its cores. Returns it (added last).</summary>
        public static BuildingData Duplicate(StoreyDocument d, BuildingData b, Vec2 near)
        {
            var nb = PrototypeJson.ReadBuilding(PrototypeJson.Write(b));
            nb.id = NewId(d); nb.name = b.name + " copy";
            foreach (var s in nb.shafts) s.id = Shafts.NewId(d);
            nb.bridges.Clear();   // a copy stands somewhere else: its bridges would reach nothing
            var bb = Tiers.Bbox(b.footprint);
            var sp = FreeSpot(d, bb.x1 - bb.x0, bb.z1 - bb.z0, near);
            nb.pos = new Vec2(sp.x - bb.x0, sp.z - bb.z0);
            d.buildings.Add(nb);
            return nb;
        }

        public static bool Delete(StoreyDocument d, string id)
        {
            if (d.buildings.RemoveAll(b => b.id == id) == 0) return false;
            BridgeEdits.Forget(d, id);   // and the bridges to it
            return true;
        }
    }
}
