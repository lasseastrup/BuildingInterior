#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;

namespace Triband.Storey.Play
{
    /// <summary>
    /// A layout as the player meets it (docs/PLAY.md, slice 5.1): which building and floor a point is in, the surface
    /// under it, and the walls that stop it. Collision is the prototype's model rather than physics: walkable heights,
    /// and LOD0's 2D wall segments with a radius per storey. Engine-free; a port of the prototype's <c>playerLoc</c>,
    /// <c>surfaceAt</c> and <c>collide</c>, checked against it (<c>play.json</c>).
    /// </summary>
    public sealed class PlayWorld
    {
        /// <summary>The player's radius (the prototype's <c>PR</c>).</summary>
        public const double Radius = 0.32;
        /// <summary>The highest step the player walks up (and the reach of a surface below a point's height).</summary>
        public const double StepUp = 0.6;

        public readonly Site Site;
        readonly Dictionary<(int, int), List<BuildingData>> grid = new Dictionary<(int, int), List<BuildingData>>();
        readonly Dictionary<string, (double x0, double z0, double x1, double z1)> bboxAll = new Dictionary<string, (double, double, double, double)>(StringComparer.Ordinal);
        readonly Dictionary<string, Lod0Result> lod0 = new Dictionary<string, Lod0Result>(StringComparer.Ordinal);
        const double BGRID = 32;

        public PlayWorld(Site site)
        {
            Site = site;
            // the prototype's grid: every building in the cells its outlines' box touches, grown by a metre, in layout order
            // (and its bridges' decks: the building that builds a bridge holds its collision)
            foreach (var b in site.Buildings)
            {
                var bb = BboxAll(b);
                foreach (var br in b.bridges)
                    if (Bridges.Span(site, b, br) is BridgeSpan s)
                        foreach (var c in Bridges.Corners(s)) bb = (Math.Min(bb.x0, c.x - b.pos.x), Math.Min(bb.z0, c.z - b.pos.z), Math.Max(bb.x1, c.x - b.pos.x), Math.Max(bb.z1, c.z - b.pos.z));
                bboxAll[b.id] = bb;
                for (int gx = (int)Math.Floor((bb.x0 + b.pos.x - 1) / BGRID); gx <= (int)Math.Floor((bb.x1 + b.pos.x + 1) / BGRID); gx++)
                    for (int gz = (int)Math.Floor((bb.z0 + b.pos.z - 1) / BGRID); gz <= (int)Math.Floor((bb.z1 + b.pos.z + 1) / BGRID); gz++)
                    {
                        if (!grid.TryGetValue((gx, gz), out var l)) grid[(gx, gz)] = l = new List<BuildingData>();
                        l.Add(b);
                    }
            }
        }

        /// <summary>The building's LOD0 (its collision segments and wall list), built on first use.</summary>
        public Lod0Result Lod0Of(BuildingData b)
        {
            if (!lod0.TryGetValue(b.id, out var r)) lod0[b.id] = r = Lod0.Build(Site, b);
            return r;
        }

        readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<BuildingData>> touching = new System.Collections.Concurrent.ConcurrentDictionary<string, List<BuildingData>>(StringComparer.Ordinal);

        /// <summary>
        /// The building and every one whose box comes within a metre of its own, in layout order: the only ones that can
        /// share a party wall with it. Kept per building (the layout doesn't change under a walk model).
        /// </summary>
        public List<BuildingData> Touching(BuildingData b)
        {
            if (touching.TryGetValue(b.id, out var l)) return l;
            var a = bboxAll[b.id]; l = new List<BuildingData>();
            foreach (var o in Site.Buildings)
            {
                var q = bboxAll[o.id];
                if (q.x0 + o.pos.x <= a.x1 + b.pos.x + 1 && a.x0 + b.pos.x <= q.x1 + o.pos.x + 1 && q.z0 + o.pos.z <= a.z1 + b.pos.z + 1 && a.z0 + b.pos.z <= q.z1 + o.pos.z + 1) l.Add(o);
            }
            return touching.GetOrAdd(b.id, l);
        }

        /// <summary>How many buildings' LOD0s the walk model holds (built or handed in).</summary>
        public int Lod0Count => lod0.Count;

        /// <summary>Hand in a LOD0 built elsewhere (the renderer's), so it is not built twice.</summary>
        public void UseLod0(BuildingData b, Lod0Result r) => lod0[b.id] = r;

        /// <summary>The local box of the footprint and every setback outline (the prototype's <c>bboxAll</c>).</summary>
        static (double x0, double z0, double x1, double z1) BboxAll(BuildingData b)
        {
            double x0 = 1e9, z0 = 1e9, x1 = -1e9, z1 = -1e9;
            void Add(List<Vec2> fp) { foreach (var p in fp) { x0 = Math.Min(x0, p.x); z0 = Math.Min(z0, p.z); x1 = Math.Max(x1, p.x); z1 = Math.Max(z1, p.z); } }
            Add(b.footprint);
            foreach (var t in Party.Tiers(b)) if (t.k0 > 0) Add(Derived.OutlineAt(b, t.k0));
            return (x0, z0, x1, z1);
        }

        /// <summary>The buildings in the grid cells a circle touches, in the order the prototype visits them.</summary>
        public List<BuildingData> Near(double x, double z, double rad)
        {
            var out_ = new List<BuildingData>();
            NearInto(x, z, rad, out_);
            return out_;
        }

        // scratch, per thread: a walk model may be asked from more than one (no garbage, and no thread treads on another's)
        [ThreadStatic] static HashSet<BuildingData>? nearSeenT;
        [ThreadStatic] static List<BuildingData>? nearSharedT;
        [ThreadStatic] static List<Seg>? collideSegsT;

        /// <summary><see cref="Near"/> into a list the caller keeps (cleared first): nothing allocated.</summary>
        public void NearInto(double x, double z, double rad, List<BuildingData> into)
        {
            var nearSeen = nearSeenT ??= new HashSet<BuildingData>();
            into.Clear(); nearSeen.Clear();
            for (int gx = (int)Math.Floor((x - rad) / BGRID); gx <= (int)Math.Floor((x + rad) / BGRID); gx++)
                for (int gz = (int)Math.Floor((z - rad) / BGRID); gz <= (int)Math.Floor((z + rad) / BGRID); gz++)
                    if (grid.TryGetValue((gx, gz), out var l)) foreach (var b in l) if (nearSeen.Add(b)) into.Add(b);
        }

        // the walk model's own queries, each done with its list before the next (every frame: no garbage)
        List<BuildingData> NearShared(double x, double z, double rad) { var l = nearSharedT ??= new List<BuildingData>(); NearInto(x, z, rad, l); return l; }

        // ---- where ----

        /// <summary>The storey a height is on: a floor line counts from 0.9 m below it, so the switch comes halfway up a flight.</summary>
        public static int FloorAtY(BuildingData b, double y)
        {
            int N = b.floors.Count;
            for (int k = N; k > 0; k--) if (y >= Derived.FloorBase(b, k) - 0.9) return k;
            return 0;
        }

        /// <summary>Inside one of the building's tier outlines (building-local).</summary>
        public static bool Inside(BuildingData b, double lx, double lz)
        {
            // each tier's outline: the footprint and every setback (Party.Tiers' starts), without making the list
            if (Geo.Pip(b.footprint, lx, lz)) return true;
            for (int j = 1; j < b.floors.Count; j++) if (b.floors[j].HasShape && Geo.Pip(b.floors[j].shape, lx, lz)) return true;
            return false;
        }

        /// <summary>On one of the building's tier outlines, or within a wall's thickness of its edge (building-local).</summary>
        static bool OnAnyTier(BuildingData b, double lx, double lz)
        {
            if (OnOutline(b.footprint, lx, lz)) return true;
            for (int j = 1; j < b.floors.Count; j++) if (b.floors[j].HasShape && OnOutline(b.floors[j].shape, lx, lz)) return true;
            return false;
        }

        static bool OnOutline(List<Vec2> fp, double lx, double lz) => Geo.Pip(fp, lx, lz) || DistToEdges(fp, lx, lz) <= Dim.T_EXT + 0.02;

        /// <summary>The building and storey the player is in (up to 1.5 m above its roof), or null outside.</summary>
        public (BuildingData b, int floor)? Locate(double x, double y, double z)
        {
            foreach (var b in NearShared(x, z, 0))
                if (Inside(b, x - b.pos.x, z - b.pos.z) && y <= Derived.RoofY(b) + 1.5) return (b, FloorAtY(b, y));
            return null;
        }

        // ---- what is underfoot ----

        static double DistToEdges(List<Vec2> fp, double x, double z)
        {
            double d = 1e9;
            for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; d = Math.Min(d, Tiers.SegDist(x, z, a.x, a.z, c.x, c.z).d); }
            return d;
        }

        /// <summary>A point in a core's frame (u across, w along).</summary>
        public static (double x, double z) ToCore(CoreData s, double lx, double lz)
        {
            double a = s.rot * Math.PI / 180, dx = lx - s.x, dz = lz - s.z;
            return (dx * Math.Cos(a) + dz * Math.Sin(a), -dx * Math.Sin(a) + dz * Math.Cos(a));
        }

        /// <summary>Where a stair's slab hole is, in its frame.</summary>
        static bool InHole(CoreData s, double qx, double qz)
        {
            if (s.type == CoreType.Flight)
            {
                double hw = Dim.FLIGHT_W / 2, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
                return qx > -hw && qx < -hw + Dim.FLIGHT_LANE && qz > z0 && qz < z1;
            }
            return Math.Abs(qx) < 1.3 && qz > -1.6 && qz < 2.6;
        }

        /// <summary>
        /// The highest walkable surface at (x, z) no more than <see cref="StepUp"/> above y: the ground, a floor, a
        /// terrace or a roof (slabs run out under the walls), or a stair's flights. Below the ground, -1e9.
        /// </summary>
        public double SurfaceAt(double x, double z, double y)
        {
            double best = y + StepUp >= 0 ? 0 : -1e9;
            foreach (var b in NearShared(x, z, Dim.T_EXT + 0.05))
            {
                foreach (var br in b.bridges)
                    if (Bridges.Span(Site, b, br) is BridgeSpan bs && Bridges.DeckAt(bs, x, z) is double dy && dy <= y + StepUp && dy > best) best = dy;
                double lx = x - b.pos.x, lz = z - b.pos.z;
                if (!OnAnyTier(b, lx, lz)) continue;
                int N = b.floors.Count;
                for (int k = 0; k <= N; k++)
                {
                    double yk = Derived.FloorBase(b, k);
                    if (yk > y + StepUp || yk <= best) continue;
                    if (!(k > 0 && OnOutline(Derived.OutlineAt(b, k - 1), lx, lz)) && !(k < N && OnOutline(Derived.OutlineAt(b, k), lx, lz))) continue;   // the storey below's roof or terrace, or this storey's floor
                    if (HoleAt(b, k, lx, lz)) continue;
                    best = yk;
                }
                foreach (var s in b.shafts)
                {
                    if (!Cores.IsStairs(s)) continue;
                    var q = ToCore(s, lx, lz);
                    if (s.type == CoreType.Flight)
                    {
                        // one ramp per storey up the flight lane, from its run's start to its end
                        double hw = Dim.FLIGHT_W / 2, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
                        if (q.x <= -hw || q.x >= -hw + Dim.FLIGHT_LANE || q.z <= z0 || q.z >= z1) continue;
                        for (int k = s.bottom; k <= N; k++)
                        {
                            if (!Cores.HasFlight(b, s, k)) continue;
                            double hy = Derived.FloorBase(b, k) + (q.z - z0) / (z1 - z0) * Derived.FloorH(b, k);
                            if (hy <= y + StepUp && hy > best) best = hy;
                        }
                        continue;
                    }
                    if (Math.Abs(q.x) >= 1.3 || q.z <= -1.6 || q.z >= 2.6) continue;
                    for (int k = s.bottom; k <= N; k++)
                    {
                        if (!Cores.HasFlight(b, s, k)) continue;
                        double yb = Derived.FloorBase(b, k), half = Derived.FloorH(b, k) / 2, hy;
                        if (q.z >= 1.6) hy = yb + half;                                   // the mid landing
                        else if (q.x < 0) hy = yb + (q.z + 1.6) / 3.2 * half;             // the first flight, up along +w
                        else hy = yb + half + (1.6 - q.z) / 3.2 * half;                   // the second, back down along -w and up
                        if (hy <= y + StepUp && hy > best) best = hy;
                    }
                }
            }
            return best;
        }

        /// <summary>A stair's slab hole, a courtyard or an atrium is open at storey k here (loops: this runs every frame).</summary>
        static bool HoleAt(BuildingData b, int k, double lx, double lz)
        {
            foreach (var s in b.shafts)
                if (Cores.StairHoleAt(b, s, k)) { var q = ToCore(s, lx, lz); if (InHole(s, q.x, q.z)) return true; }
            foreach (var v in b.voids)
                if (Courtyards.HoleAt(b, v, k) && Geo.Pip(v.shape, lx, lz)) return true;
            return false;
        }

        // ---- what stops it ----

        /// <summary>
        /// A point moved out of every wall near it (the walls of the storey at height y, within 3 m): each segment
        /// pushes it to its radius plus the player's, four passes over all of them.
        /// </summary>
        public (double x, double z) Collide(double px, double pz, double y)
        {
            var segs = collideSegsT ??= new List<Seg>(); segs.Clear();
            foreach (var b in NearShared(px, pz, 3))
            {
                var bb = bboxAll[b.id];
                if (px < bb.x0 + b.pos.x - 3 || px > bb.x1 + b.pos.x + 3 || pz < bb.z0 + b.pos.z - 3 || pz > bb.z1 + b.pos.z + 3) continue;
                if (y > Derived.RoofY(b) + 2) continue;
                var l0 = Lod0Of(b); var st = l0.Segs; int k = FloorAtY(b, y);
                if (k < st.Count) segs.AddRange(st[k]);
                foreach (var (s, y0, y1) in l0.Extra) if (y >= y0 && y <= y1) segs.Add(s);   // a bridge's sides
            }
            for (int it = 0; it < 4; it++)
                foreach (var s in segs)
                {
                    var q = Tiers.SegDist(px, pz, s.ax, s.az, s.bx, s.bz); double rr = Radius + s.r;
                    if (q.d < rr)
                    {
                        if (q.d < 1e-5) { px += rr; continue; }
                        px = q.cx + (px - q.cx) / q.d * rr; pz = q.cz + (pz - q.cz) / q.d * rr;
                    }
                }
            return (px, pz);
        }

        /// <summary>
        /// Where to start on storey k of building b, as the prototype's "Play here" does: in front of the first stairs or
        /// lift serving it, or the storey's middle. Building-local.
        /// </summary>
        public static Vec2 SpawnOn(BuildingData b, int k)
        {
            k = Math.Min(k, b.floors.Count);
            var fp = Derived.OutlineAt(b, k);
            var s = b.shafts.FirstOrDefault(x => Cores.Stops(b, x).Contains(k));
            if (s != null)
            {
                double a = s.rot * Math.PI / 180, d = s.type == CoreType.Lift ? -2.2 : s.type == CoreType.Flight ? -(Dim.FLIGHT_D / 2 + 0.8) : -3.4;
                var p = new Vec2(s.x - Math.Sin(a) * d, s.z + Math.Cos(a) * d);
                if (Geo.Pip(fp, p.x, p.z)) return p;
            }
            double cx = 0, cz = 0;
            foreach (var q in fp) { cx += q.x / fp.Count; cz += q.z / fp.Count; }
            return new Vec2(cx, cz);
        }

        /// <summary>The lift whose car a point is in, on a storey it serves, if any.</summary>
        public (BuildingData b, CoreData s, int floor)? LiftAt(double x, double y, double z)
        {
            var loc = Locate(x, y, z); if (loc == null) return null;
            var (b, floor) = loc.Value;
            foreach (var s in b.shafts)
            {
                if (s.type != CoreType.Lift) continue;
                var q = ToCore(s, x - b.pos.x, z - b.pos.z);
                if (Math.Abs(q.x) < 1.2 && Math.Abs(q.z) < 1.2 && Cores.Stops(b, s).Contains(floor)) return (b, s, floor);
            }
            return null;
        }
    }
}
