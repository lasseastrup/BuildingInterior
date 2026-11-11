#nullable enable
using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// A storey's rooms (Storey's own): its outline less its room walls, each as thick as it is built and run on past its
    /// ends into what it meets, and less the courtyards and atria that take room on it. Doorways don't join rooms. What
    /// the Interior tab's Block tool blocks out (<see cref="FloorData.blocked"/>): the room holding each mark, built solid to
    /// the ceiling, its doorways closed and its windows opaque. Building-local.
    /// </summary>
    public static class Rooms
    {
        /// <summary>A room: its outer ring, and its holes (a wall standing free in it).</summary>
        public sealed class Room
        {
            public List<Vec2> ring = new List<Vec2>();
            public List<List<Vec2>> holes = new List<List<Vec2>>();
            /// <summary>A building-local point is in it (not in one of its holes).</summary>
            public bool Contains(double x, double z)
            {
                if (!Geo.Pip(ring, x, z)) return false;
                foreach (var h in holes) if (Geo.Pip(h, x, z)) return false;
                return true;
            }
        }

        /// <summary>Storey k's rooms (none on a storey outside the building).</summary>
        public static List<Room> All(BuildingData b, int k)
        {
            var o = new List<Room>();
            if (k < 0 || k >= b.floors.Count) return o;
            var fp = Derived.OutlineAt(b, k); double hw = Dim.T_INT / 2;
            var cut = new PathsD();
            foreach (var w0 in b.floors[k].walls)
                foreach (var wl in Courtyards.ClipOut(b, k, Geo.ClipWall(fp, w0)))
                {
                    double dx = wl.b.x - wl.a.x, dz = wl.b.z - wl.a.z, L = Geo.Hypot(dx, dz); if (L < 0.05) continue;
                    double ux = dx / L, uz = dz / L, wx = -uz * hw, wz = ux * hw;
                    // run on past its ends by its half thickness, so walls meeting at a corner or a T close the room there
                    double ax = wl.a.x - ux * hw, az = wl.a.z - uz * hw, cx = wl.b.x + ux * hw, cz = wl.b.z + uz * hw;
                    cut.Add(new PathD { new PointD(ax + wx, az + wz), new PointD(cx + wx, cz + wz), new PointD(cx - wx, cz - wz), new PointD(ax - wx, az - wz) });
                }
            foreach (var v in b.voids)
                if (Courtyards.Taken(b, v, k)) { var p = new PathD(v.shape.Count); foreach (var q in v.shape) p.Add(new PointD(q.x, q.z)); cut.Add(p); }
            var subject = new PathsD(); var sp = new PathD(fp.Count); foreach (var q in fp) sp.Add(new PointD(q.x, q.z)); subject.Add(sp);
            // each the same way round: under the non-zero rule a ring the other way would cancel one it overlaps
            foreach (var p in cut) if (!Clipper.IsPositive(p)) p.Reverse();
            var tree = new PolyTreeD();
            Clipper.BooleanOp(ClipType.Difference, subject, Clipper.Union(cut, new PathsD(), FillRule.NonZero, 6), tree, FillRule.NonZero, 6);
            Collect(tree, o);
            return o;
        }

        static void Collect(PolyPathD node, List<Room> o)
        {
            static List<Vec2> V(PathD p) { var r = new List<Vec2>(p.Count); foreach (var q in p) r.Add(new Vec2(q.x, q.y)); return r; }
            for (int i = 0; i < node.Count; i++)
            {
                var outer = (PolyPathD)node[i]; if (outer.Polygon == null || outer.Polygon.Count < 3) continue;
                var room = new Room { ring = V(outer.Polygon) };
                for (int j = 0; j < outer.Count; j++)
                {
                    var h = (PolyPathD)outer[j];
                    if (h.Polygon != null && h.Polygon.Count >= 3) room.holes.Add(V(h.Polygon));
                    Collect(h, o);   // an island in a hole: a room of its own
                }
                // a sliver between walls drawn side by side is no room
                if (Math.Abs(Geo.Area2(room.ring)) / 2 >= 0.05) o.Add(room);
            }
        }

        /// <summary>The room of storey k holding a building-local point, or null where the point is in a wall, a void or outside.</summary>
        public static Room? At(BuildingData b, int k, Vec2 p)
        {
            foreach (var r in All(b, k)) if (r.Contains(p.x, p.z)) return r;
            return null;
        }

        /// <summary>
        /// Storey k's blocked rooms (<see cref="FloorData.blocked"/>): the room holding each of its marks, each once (two marks in
        /// one room block it once). None on a storey with no inside (a shell-only building's, a filled one): nothing is built there.
        /// </summary>
        public static List<Room> Blocked(BuildingData b, int k)
        {
            var o = new List<Room>();
            if (Derived.ShellAt(b, k) || b.floors[k].blocked.Count == 0) return o;
            var all = All(b, k);
            foreach (var r in all)
                foreach (var m in b.floors[k].blocked)
                    if (r.Contains(m.x, m.z)) { o.Add(r); break; }
            return o;
        }

        /// <summary>Whether a building-local point on storey k is in one of its blocked rooms (<paramref name="blocked"/>: <see cref="Blocked"/>'s).</summary>
        public static bool InBlocked(List<Room> blocked, double x, double z)
        {
            foreach (var r in blocked) if (r.Contains(x, z)) return true;
            return false;
        }
    }
}
