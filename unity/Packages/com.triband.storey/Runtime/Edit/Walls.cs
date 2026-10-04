#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    public enum SnapKind { Free, Node, Edge }

    /// <summary>Where a wall point lands, and what it snapped to: a point (ring marker), a wall or outline edge (diamond), or nothing; with the alignment guides to draw.</summary>
    public struct WallSnap
    {
        public double x, z;
        public SnapKind kind;
        /// <summary>For an edge snap, the wall it landed on, or -1 for an outline edge.</summary>
        public int? wall;
        public List<Vec2> guides;
        public Vec2 Point => new Vec2(x, z);
    }

    /// <summary>What a snap may use: the previous point and other anchors for axis alignment; wall ends and walls to ignore (the ones being dragged); Alt for free.</summary>
    public sealed class SnapOptions
    {
        public Vec2? from;
        public List<Vec2> anchors = new List<Vec2>();
        public HashSet<(int wall, bool atB)> ignoreEnds = new HashSet<(int, bool)>();
        public HashSet<int> ignoreWalls = new HashSet<int>();
        public bool free;
    }

    public enum TargetKind { Shaft, Wall, Door, Entrance }

    /// <summary>What a pointer is over on a storey: a core, a wall, a doorway in a wall, or an exterior door.</summary>
    public struct Target
    {
        public TargetKind kind;
        public int index;   // core, wall or entrance index
        public int door;    // the doorway's index in its wall
    }

    /// <summary>Where the Door tool would put (or find) a door: an interior doorway in a wall, or an exterior door on an outline edge.</summary>
    public sealed class DoorSpot
    {
        public bool exterior;
        public int wall = -1, door = -1;      // interior: the wall, and the existing doorway there (-1 none)
        public int edge = -1, entrance = -1;  // exterior: the outline edge, and the existing door there (-1 none)
        public int k;
        public double t, L;
    }

    /// <summary>
    /// The interior walls of a storey as a graph (SPEC §9): walls meeting at a point form a joint that moves as one; a
    /// point landing on the middle of a wall splits it into a T-junction. One snapping rule for drawing and dragging.
    /// Doors and erasing. Ported from the prototype.
    /// </summary>
    public static class Walls
    {
        /// <summary>A joint's key: the point to the centimetre.</summary>
        public static string Key(Vec2 p) => ((long)Tiers.JsRound(p.x * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                                            ((long)Tiers.JsRound(p.z * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>The joints of a storey: per point, the wall ends that meet there (wall index, b end?), in first-seen order.</summary>
        public static List<List<(int wall, bool atB)>> Nodes(List<WallData> walls)
        {
            var byKey = new Dictionary<string, List<(int, bool)>>(); var order = new List<List<(int, bool)>>();
            for (int wi = 0; wi < walls.Count; wi++)
                foreach (bool atB in new[] { false, true })
                {
                    string k = Key(atB ? walls[wi].b : walls[wi].a);
                    if (!byKey.TryGetValue(k, out var n)) { byKey[k] = n = new List<(int, bool)>(); order.Add(n); }
                    n.Add((wi, atB));
                }
            return order;
        }

        static Vec2 End(WallData w, bool atB) => atB ? w.b : w.a;
        static void SetEnd(WallData w, bool atB, Vec2 p) { if (atB) w.b = p; else w.a = p; }
        static double Len(WallData w) => Tiers.Hypot(w.b.x - w.a.x, w.b.z - w.a.z);

        /// <summary>
        /// The snapping rule: other wall points and outline corners first; then the centimetre grid, in line with the
        /// previous point and the anchors; then onto a wall or outline edge.
        /// </summary>
        public static WallSnap Snap(BuildingData b, int k, Vec2 p, SnapOptions? o = null)
        {
            o ??= new SnapOptions();
            if (o.free) return new WallSnap { x = p.x, z = p.z, kind = SnapKind.Free, guides = new List<Vec2>() };
            var walls = b.floors[k].walls;
            WallSnap? best = null; double bd = 0.45;
            for (int wi = 0; wi < walls.Count; wi++)
                foreach (bool atB in new[] { false, true })
                {
                    if (o.ignoreEnds.Contains((wi, atB))) continue;
                    var c = End(walls[wi], atB); double d = Tiers.Hypot(c.x - p.x, c.z - p.z);
                    if (d < bd) { bd = d; best = new WallSnap { x = c.x, z = c.z, kind = SnapKind.Node, guides = new List<Vec2>() }; }
                }
            var fp = Derived.OutlineAt(b, k);
            foreach (var v in fp)
            {
                double d = Tiers.Hypot(v.x - p.x, v.z - p.z);
                if (d < bd) { bd = d; best = new WallSnap { x = v.x, z = v.z, kind = SnapKind.Node, guides = new List<Vec2>() }; }
            }
            if (best != null) return best.Value;
            var q = new WallSnap { x = Tiers.Cm(p.x), z = Tiers.Cm(p.z), kind = SnapKind.Free, guides = new List<Vec2>() };
            if (o.from is Vec2 f)
            {
                double dx = q.x - f.x, dz = q.z - f.z;
                if (Math.Abs(dx) < Math.Abs(dz) * 0.3) { q.x = f.x; q.guides.Add(f); }
                else if (Math.Abs(dz) < Math.Abs(dx) * 0.3) { q.z = f.z; q.guides.Add(f); }
            }
            foreach (var a in o.anchors)
            {
                if (Math.Abs(q.x - a.x) < 0.3) { q.x = a.x; q.guides.Add(a); }
                if (Math.Abs(q.z - a.z) < 0.3) { q.z = a.z; q.guides.Add(a); }
            }
            WallSnap? eb = null; double ed = 0.3;
            for (int wi = 0; wi < walls.Count; wi++)
            {
                if (o.ignoreWalls.Contains(wi)) continue;
                var w = walls[wi]; var r = Tiers.SegDist(q.x, q.z, w.a.x, w.a.z, w.b.x, w.b.z);
                if (r.d < ed) { ed = r.d; eb = new WallSnap { x = r.cx, z = r.cz, kind = SnapKind.Edge, wall = wi }; }
            }
            for (int i = 0; i < fp.Count; i++)
            {
                var v = fp[i]; var c = fp[(i + 1) % fp.Count]; var r = Tiers.SegDist(q.x, q.z, v.x, v.z, c.x, c.z);
                if (r.d < ed) { ed = r.d; eb = new WallSnap { x = r.cx, z = r.cz, kind = SnapKind.Edge, wall = -1 }; }
            }
            if (eb != null)
            {
                var e = eb.Value;
                foreach (var g in q.guides)
                {
                    if (g.x == q.x && Math.Abs(e.x - q.x) < 0.05) e.x = q.x;
                    if (g.z == q.z && Math.Abs(e.z - q.z) < 0.05) e.z = q.z;
                }
                e.guides = q.guides.Where(g => g.x == e.x || g.z == e.z).ToList();
                return e;
            }
            return q;
        }

        /// <summary>Split wall wi at p (kept off its ends); its doorways go to the half they are on. Returns the wall's length.</summary>
        public static double Split(FloorData f, int wi, Vec2 p)
        {
            var w = f.walls[wi]; double L = Len(w), t = Tiers.Clamp(Tiers.SegDist(p.x, p.z, w.a.x, w.a.z, w.b.x, w.b.z).t, 0.01, 0.99);
            var w1 = new WallData { a = w.a, b = p }; var w2 = new WallData { a = p, b = w.b };
            foreach (var d in w.doors) { if (d.t < t) w1.doors.Add(new DoorData { t = d.t / t }); else w2.doors.Add(new DoorData { t = (d.t - t) / (1 - t) }); }
            f.walls.RemoveAt(wi); f.walls.Insert(wi, w2); f.walls.Insert(wi, w1);
            return L;
        }

        /// <summary>A point landing on the middle of a wall (not one of <paramref name="skip"/>) splits it, so the two become a real joint.</summary>
        public static bool ConnectAt(FloorData f, Vec2 p, ICollection<WallData>? skip = null)
        {
            for (int i = 0; i < f.walls.Count; i++)
            {
                if (skip != null && skip.Contains(f.walls[i])) continue;
                var w = f.walls[i]; double L = Len(w); var r = Tiers.SegDist(p.x, p.z, w.a.x, w.a.z, w.b.x, w.b.z);
                if (r.d < 0.02 && r.t * L > 0.05 && (1 - r.t) * L > 0.05) { Split(f, i, p); return true; }
            }
            return false;
        }

        /// <summary>Drop walls shorter than 5 cm and duplicates.</summary>
        public static void Clean(FloorData f)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            f.walls = f.walls.Where(w =>
            {
                if (Len(w) < 0.05) return false;
                string ka = Key(w.a), kb = Key(w.b), key = string.CompareOrdinal(ka, kb) <= 0 ? ka + "|" + kb : kb + "|" + ka;
                return seen.Add(key);
            }).ToList();
        }

        /// <summary>A wall from s to q on storey k, joined to the walls its ends land on.</summary>
        /// <summary>
        /// Some of a wall from s to q stands inside storey k, so something of it is built: the generator keeps a wall's
        /// part inside the storey. Wholly outside it (over a setback's terrace, which is the storey below's roof, not this
        /// storey's floor) nothing would be.
        /// </summary>
        public static bool Inside(BuildingData b, int k, Vec2 s, Vec2 q)
        {
            var fp = Derived.OutlineAt(b, k);
            for (int i = 0; i <= 16; i++)
            {
                double t = i / 16.0;
                if (Generate.Geo.Pip(fp, s.x + (q.x - s.x) * t, s.z + (q.z - s.z) * t)) return true;
            }
            return false;
        }

        /// <summary>What the editor says when a wall would stand wholly outside the storey (<see cref="Inside"/>).</summary>
        public const string OutsideWhy = "Walls go inside this storey. That's outside it (a terrace is the storey below's roof).";

        public static void Add(BuildingData b, int k, Vec2 s, Vec2 q)
        {
            var f = b.floors[k];
            ConnectAt(f, s); ConnectAt(f, q);
            f.walls.Add(new WallData { a = s, b = q });
            Clean(f);
        }

        /// <summary>
        /// Double-clicking a joint: two walls in a straight line merge into one (their doorways kept in place); otherwise
        /// the walls ending there go. Returns what happened, or null when there is no joint at the key.
        /// </summary>
        public static string? RemoveJoint(BuildingData b, int k, string key)
        {
            var f = b.floors[k];
            var n = Nodes(f.walls).FirstOrDefault(x => Key(End(f.walls[x[0].wall], x[0].atB)) == key);
            if (n == null) return null;
            if (n.Count == 2)
            {
                var (i1, e1) = n[0]; var (i2, e2) = n[1]; var w1 = f.walls[i1]; var w2 = f.walls[i2];
                Vec2 A = End(w1, !e1), B = End(w2, !e2), J = End(w1, e1);
                double cr = Math.Abs((J.x - A.x) * (B.z - A.z) - (J.z - A.z) * (B.x - A.x)) / Math.Max(1e-6, Tiers.Hypot(B.x - A.x, B.z - A.z));
                if (cr < 0.05)
                {
                    var nw = new WallData { a = A, b = B };
                    foreach (var w in new[] { w1, w2 })
                        foreach (var d in w.doors)
                        {
                            double px = w.a.x + (w.b.x - w.a.x) * d.t, pz = w.a.z + (w.b.z - w.a.z) * d.t;
                            nw.doors.Add(new DoorData { t = Tiers.SegDist(px, pz, A.x, A.z, B.x, B.z).t });
                        }
                    f.walls = f.walls.Where((w, i) => i != i1 && i != i2).ToList(); f.walls.Add(nw);
                    return "Walls joined";
                }
            }
            var drop = new HashSet<int>(n.Select(r => r.wall));
            f.walls = f.walls.Where((w, i) => !drop.Contains(i)).ToList();
            return drop.Count == 1 ? "Wall removed" : drop.Count + " walls removed";
        }

        static void DragEnds(BuildingData b, int k, List<(WallData w, bool atB)> refs, List<Vec2> anchors, Vec2 p, bool free)
        {
            var f = b.floors[k]; var o = new SnapOptions { anchors = anchors, free = free };
            foreach (var (w, atB) in refs) { int wi = f.walls.IndexOf(w); o.ignoreEnds.Add((wi, atB)); o.ignoreWalls.Add(wi); }
            var q = Snap(b, k, p, o);
            foreach (var (w, atB) in refs) SetEnd(w, atB, q.Point);
            ConnectAt(f, End(refs[0].w, refs[0].atB), refs.Select(r => r.w).ToList());
            Clean(f);
        }

        /// <summary>Drag the joint at key to p (snapped, in line with the walls' other ends unless free), then join and clean. False when there is no joint there.</summary>
        public static bool MoveJoint(BuildingData b, int k, string key, Vec2 p, bool free = false)
        {
            var f = b.floors[k];
            var n = Nodes(f.walls).FirstOrDefault(x => Key(End(f.walls[x[0].wall], x[0].atB)) == key);
            if (n == null) return false;
            var refs = n.Select(r => (f.walls[r.wall], r.atB)).ToList();
            DragEnds(b, k, refs, refs.Select(r => End(r.Item1, !r.atB)).ToList(), p, free);
            return true;
        }

        /// <summary>The "+" handle: split wall wi at its middle and drag the new joint to p.</summary>
        public static void SplitAndDrag(BuildingData b, int k, int wi, Vec2 p, bool free = false)
        {
            var f = b.floors[k]; var x = f.walls[wi];
            Split(f, wi, new Vec2((x.a.x + x.b.x) / 2, (x.a.z + x.b.z) / 2));
            var w1 = f.walls[wi]; var w2 = f.walls[wi + 1];
            DragEnds(b, k, new List<(WallData, bool)> { (w1, true), (w2, false) }, new List<Vec2> { w1.a, w2.b }, p, free);
        }

        /// <summary>The wall of storey k nearest p within maxD.</summary>
        public static (int i, double d, double t)? NearestWall(BuildingData b, int k, Vec2 p, double maxD)
        {
            (int, double, double)? best = null;
            if (k >= b.floors.Count) return null;
            var walls = b.floors[k].walls;
            for (int i = 0; i < walls.Count; i++)
            {
                var w = walls[i]; var r = Tiers.SegDist(p.x, p.z, w.a.x, w.a.z, w.b.x, w.b.z);
                if (r.d < maxD && (best == null || r.d < best.Value.Item2)) best = (i, r.d, r.t);
            }
            return best;
        }

        /// <summary>The outline edge of storey k nearest p within maxD, with its length.</summary>
        public static (int i, double d, double t, double L)? NearestEdge(BuildingData b, Vec2 p, double maxD, int k)
        {
            var fp = Derived.OutlineAt(b, k); (int, double, double, double)? best = null;
            for (int i = 0; i < fp.Count; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % fp.Count]; var r = Tiers.SegDist(p.x, p.z, a.x, a.z, c.x, c.z);
                if (r.d < maxD && (best == null || r.d < best.Value.Item2)) best = (i, r.d, r.t, Tiers.Hypot(c.x - a.x, c.z - a.z));
            }
            return best;
        }

        /// <summary>What the pointer is over on storey k: a core, a doorway, a wall or an exterior door (the Erase tool's target).</summary>
        public static Target? HoverTarget(BuildingData b, int k, Vec2 p)
        {
            var s = Shafts.At(b, k, p);
            if (s != null) return new Target { kind = TargetKind.Shaft, index = b.shafts.IndexOf(s) };
            var w = NearestWall(b, k, p, 0.45);
            if (w != null)
            {
                var wall = b.floors[k].walls[w.Value.i]; double L = Len(wall);
                int di = wall.doors.FindIndex(d => Math.Abs(d.t * L - w.Value.t * L) < Dim.DOOR_INT * 0.6);
                return di >= 0 ? new Target { kind = TargetKind.Door, index = w.Value.i, door = di } : new Target { kind = TargetKind.Wall, index = w.Value.i };
            }
            var e = NearestEdge(b, p, 0.6, k);
            if (e != null)
            {
                var (ei_, _, et, eL) = e.Value; int ei = b.entrances.FindIndex(x => x.k == k && x.edge == ei_ && Math.Abs(x.t * eL - et * eL) < Dim.DOOR_EXT * 0.7);
                if (ei >= 0) return new Target { kind = TargetKind.Entrance, index = ei };
            }
            return null;
        }

        /// <summary>Remove a target (the Erase tool's click).</summary>
        public static void Erase(BuildingData b, int k, Target t)
        {
            switch (t.kind)
            {
                case TargetKind.Shaft: { var s = b.shafts[t.index]; b.shafts = b.shafts.Where(x => !ReferenceEquals(x, s) && x.id != s.id).ToList(); break; }
                case TargetKind.Door: b.floors[k].walls[t.index].doors.RemoveAt(t.door); break;
                case TargetKind.Wall: b.floors[k].walls.RemoveAt(t.index); break;
                case TargetKind.Entrance: b.entrances.RemoveAt(t.index); break;
            }
        }

        /// <summary>
        /// Where the Door tool would put a door at p on storey k: a doorway in the nearest wall, or an exterior door on
        /// the nearest outline edge (on an upper floor only onto a terrace or a neighbour's roof or terrace). Null where none fits.
        /// </summary>
        public static DoorSpot? DoorAt(Site site, BuildingData b, int k, Vec2 p, bool exteriorOnly = false)
        {
            var w = exteriorOnly ? null : NearestWall(b, k, p, 0.6);
            var e = NearestEdge(b, p, 0.7, k);
            if (w != null && (e == null || w.Value.d <= e.Value.d))
            {
                var wall = b.floors[k].walls[w.Value.i]; double L = Len(wall);
                if (L < Dim.DOOR_INT + 0.3) return null;
                double t = Tiers.Clamp(w.Value.t * L, Dim.DOOR_INT / 2 + 0.1, L - Dim.DOOR_INT / 2 - 0.1) / L;
                return new DoorSpot { wall = w.Value.i, t = t, door = wall.doors.FindIndex(d => Math.Abs(d.t - t) * L < Dim.DOOR_INT * 0.8), L = L, k = k };
            }
            if (e != null)
            {
                var (i, _, et, eL) = e.Value;
                if (eL < Dim.DOOR_EXT + 0.6) return null;
                double t = Tiers.Clamp(et * eL, Dim.DOOR_EXT / 2 + 0.3, eL - Dim.DOOR_EXT / 2 - 0.3) / eL;
                if (k > 0 && !Facade.TerraceAt(b, k, i, t) && Facade.LandingAt(site, b, k, i, t) == null) return null;
                return new DoorSpot { exterior = true, edge = i, t = t, entrance = b.entrances.FindIndex(x => x.k == k && x.edge == i && Math.Abs(x.t - t) * eL < Dim.DOOR_EXT * 0.8), L = eL, k = k };
            }
            return null;
        }

        /// <summary>The Door tool's click: add the door at p, or remove the one there. False where no door fits.</summary>
        public static bool ToggleDoor(Site site, BuildingData b, int k, Vec2 p)
        {
            var d = DoorAt(site, b, k, p);
            if (d == null) return false;
            if (!d.exterior)
            {
                var w = b.floors[k].walls[d.wall];
                if (d.door >= 0) w.doors.RemoveAt(d.door); else w.doors.Add(new DoorData { t = d.t });
            }
            else if (d.entrance >= 0) b.entrances.RemoveAt(d.entrance);
            else b.entrances.Add(new EntranceData { edge = d.edge, t = d.t, k = d.k });
            return true;
        }
    }
}
