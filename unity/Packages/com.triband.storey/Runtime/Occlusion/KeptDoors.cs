#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;

namespace Triband.Storey.Occlusion
{
    /// <summary>
    /// One of a walk-in building's doors kept while the building is in the way (<see cref="KeptDoors"/>): a box round the
    /// door as LOD0 cuts it, in site space. Whatever of the building lies in it is drawn as it is.
    /// </summary>
    public struct DoorBox
    {
        /// <summary>The storey the door is on, and the edge of that storey's outline it is in.</summary>
        public int k, edge;
        /// <summary>The box's span along the edge, from the edge's start (metres): the door's, and <see cref="KeptDoors.Side"/> each side.</summary>
        public double u0, u1;
        /// <summary>The box's middle in plan, and the unit along the wall.</summary>
        public double cx, cz, ux, uz;
        /// <summary>Half the box's width along the wall and across it, and its foot and top (heights).</summary>
        public double halfW, halfD, y0, y1;

        /// <summary>The point (site space) is in the box, as the shaders test it (StoreyDoorAt in StoreyOcclusion.hlsl).</summary>
        public bool Holds(double x, double y, double z)
        {
            double dx = x - cx, dz = z - cz;
            return Math.Abs(dx * ux + dz * uz) < halfW && Math.Abs(-dx * uz + dz * ux) < halfD && y > y0 && y < y1;
        }
    }

    /// <summary>
    /// A walk-in building in the way keeps its doors (<see cref="OcclusionSettings.keepDoors"/>, docs/PLAY.md §4), so the
    /// player sees where they can go in: its entrances on walk-in storeys at or under the storey at the player's height, each
    /// the door LOD0 cuts there (<see cref="Facade.Ops(Site, BuildingData, int, int, double, double, double)"/>) with a little
    /// of the wall round it. The shaders draw what lies in their boxes as it is, neither gone nor darkened (Sink doesn't squash
    /// it), and the footprint's rim leaves a gap for them. Shell storeys' doors are shut, and a shell building has none to keep.
    /// Storey's own.
    /// </summary>
    public static class KeptDoors
    {
        /// <summary>The most doors a building keeps: the shaders read no more (STOREY_DOORS in StoreyOcclusion.hlsl).</summary>
        public const int Max = 8;
        /// <summary>
        /// How much of the wall the box takes round the door (metres): each side, over it and under its foot, into the room from
        /// the wall's inner face and out from its outer face (a door's frame, posts and step stand out of the wall).
        /// </summary>
        public const double Side = 0.15, Over = 0.2, Under = 0.2, Inside = 0.3, Outside = 0.4;

        /// <summary>b's doors kept with the storey at the player's height k (<see cref="KeptDoors"/>), into <paramref name="into"/> (emptied first).</summary>
        public static void Of(Site site, BuildingData b, int k, List<DoorBox> into)
        {
            into.Clear();
            if (!b.interior) return;
            int N = b.floors.Count;
            foreach (var e in b.entrances)
            {
                if (e.k > k || e.k >= N || Derived.ShellAt(b, e.k)) continue;
                var outline = Derived.OutlineAt(b, e.k);
                if (e.edge < 0 || e.edge >= outline.Count) continue;
                // the door LOD0 cuts on the entrance's wall: the one nearest where the entrance asks for it (a door near the
                // wall's end is moved in), not a bridge's
                var f = Details.EdgeFrameAt(b, e.k, e.edge);
                Opening? door = null; double best = double.MaxValue, at = e.t * f.e.L;
                foreach (var o in Facade.Ops(site, b, e.k, e.edge, f.e.L, f.uS, f.uE))
                {
                    if (!o.door || o.bare) continue;
                    double d = Math.Abs((o.u0 + o.u1) / 2 - at);
                    if (d < best) { best = d; door = o; }
                }
                if (door == null) continue;
                // the frame's w points out of the building, from the wall's inner line (the outline) to its outer face
                double floor = Derived.FloorBase(b, e.k), T = Dim.T_EXT;
                double ceiling = e.k + 1 < N ? Derived.FloorBase(b, e.k + 1) - Dim.SLAB - 0.01 : double.MaxValue;
                double u0 = door.u0 - Side, u1 = door.u1 + Side, w0 = -Inside, w1 = T + Outside;
                var mid = f.e.F.At2((u0 + u1) / 2, (w0 + w1) / 2);
                into.Add(new DoorBox
                {
                    k = e.k, edge = e.edge, u0 = u0, u1 = u1,
                    cx = mid.x, cz = mid.z, ux = f.e.u.x, uz = f.e.u.z,
                    halfW = (u1 - u0) / 2, halfD = (w1 - w0) / 2,
                    y0 = floor + door.y0 - Under, y1 = Math.Min(floor + door.y1 + Over, ceiling),
                });
                if (into.Count >= Max) return;
            }
        }
    }
}
