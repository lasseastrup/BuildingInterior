#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>A side of a building, by its walls' outward normals in the building's own axes: +z the front, −z the back, +x the right, −x the left.</summary>
    public enum Side { Front, Back, Left, Right }

    /// <summary>A point on a facade: outline edge i of tier k0, t along it, at height y (world), on storey k.</summary>
    public struct FacadeHit
    {
        public int i, k0, k;
        public double t, L, y;
        /// <summary>Distance along the ray, for picking the nearest facade.</summary>
        public double distance;
    }

    /// <summary>A box to draw as a ghost: centre, size, and the wall's direction.</summary>
    public struct GhostBox
    {
        public double cx, cy, cz, sx, sy, sz;
        public Vec2 u;
    }

    /// <summary>What the Facade tool's detail palette would do at a facade point: add a detail, remove one, or why not.</summary>
    public sealed class DetailSpot
    {
        public DetailData? add;
        public int remove = -1;
        public string? error;
        public GhostBox? ghost;
    }

    /// <summary>
    /// The Facade tab's edits (SPEC §9): picking a wall, entrances and terrace doors, blank walls, facade details by
    /// hand, and whether a setback's uncovered roof below is a terrace or a roof. Ported from the prototype.
    /// </summary>
    public static class Facades
    {
        /// <summary>
        /// The facade a ray (world space) meets first: each tier's outline edges, outer faces turned towards the ray,
        /// between the tier's floor and its top.
        /// </summary>
        public static FacadeHit? Pick(BuildingData b, Vec3d origin, Vec3d dir, double maxDistance = double.PositiveInfinity)
        {
            FacadeHit? best = null;
            foreach (var t in Tiers.Of(b))
            {
                var fp = Derived.OutlineAt(b, t.k0); double y0 = Derived.FloorBase(b, t.k0), y1 = Derived.FloorBase(b, t.k1), sg = Geo.Area2(fp) > 0 ? 1 : -1;
                for (int i = 0; i < fp.Count; i++)
                {
                    var a = fp[i]; var c = fp[(i + 1) % fp.Count];
                    double L = Tiers.Hypot(c.x - a.x, c.z - a.z); if (L == 0) L = 1;
                    double nx = sg * (c.z - a.z) / L, nz = -sg * (c.x - a.x) / L;
                    double den = dir.x * nx + dir.z * nz; if (den > -1e-6) continue;   // faces turned towards the viewer only
                    double tt = ((a.x + b.pos.x + nx * Dim.T_EXT - origin.x) * nx + (a.z + b.pos.z + nz * Dim.T_EXT - origin.z) * nz) / den;
                    if (tt < 0 || tt > maxDistance || (best != null && tt >= best.Value.distance)) continue;
                    double hy = origin.y + dir.y * tt; if (hy < y0 - 0.01 || hy > y1) continue;
                    double u = ((origin.x + dir.x * tt - b.pos.x - a.x) * (c.x - a.x) + (origin.z + dir.z * tt - b.pos.z - a.z) * (c.z - a.z)) / (L * L);
                    if (u < 0 || u > 1) continue;
                    best = new FacadeHit { distance = tt, i = i, t = u, L = L, y = hy, k0 = t.k0 };
                }
            }
            if (best == null) return null;
            var h = best.Value; int k = 0;
            for (int j = 1; j < b.floors.Count; j++) if (h.y >= Derived.FloorBase(b, j) - 0.01) k = j;
            h.k = k;
            return h;
        }

        /// <summary>The blank walls of storey k's outline: switch edge i's windows off, or back on (a quiet or bare wall then has everything again).</summary>
        public static void ToggleBlank(BuildingData b, int k, int i)
        {
            int k0 = Derived.TierStart(b, k);
            Tiers.SetState(b, k0, i, Tiers.State(b, k0, i) == EdgeState.Open ? EdgeState.Blank : EdgeState.Open);
        }

        /// <summary>
        /// What wall i of storey k's outline has (<see cref="EdgeState"/>, docs/EDITOR.md §6.7): the Facade tool sets
        /// <paramref name="s"/>, or, on a wall that has it already, its windows and all back (Open).
        /// </summary>
        public static void ToggleState(BuildingData b, int k, int i, EdgeState s)
        {
            int k0 = Derived.TierStart(b, k);
            Tiers.SetState(b, k0, i, Tiers.State(b, k0, i) == s ? EdgeState.Open : s);
        }

        /// <summary>
        /// Every wall of tier k0 facing <paramref name="side"/> (by the larger part of its outward normal, as the old
        /// BuildingGenerator's sides: +z the front, −z the back, +x the right, −x the left) set to <paramref name="s"/>: the old
        /// generator's per-building side mask, as per-wall states (a converter's, or the Facade tab's). Walls already in a
        /// stronger state keep it. Returns how many changed.
        /// </summary>
        public static int SetSide(BuildingData b, int k0, Side side, EdgeState s)
        {
            var fp = Tiers.Outline(b, k0); int n = fp.Count, changed = 0; bool ccw = Geo.Area2(fp) > 0;
            for (int i = 0; i < n; i++)
            {
                var a = fp[i]; var c = fp[(i + 1) % n]; double dx = c.x - a.x, dz = c.z - a.z;
                if (dx * dx + dz * dz < 1e-10) continue;
                // the outward normal: to the right of the edge on a counter-clockwise outline (x right, z up in plan)
                double nx = ccw ? dz : -dz, nz = ccw ? -dx : dx;
                var at = Math.Abs(nx) >= Math.Abs(nz) ? (nx >= 0 ? Side.Right : Side.Left) : (nz >= 0 ? Side.Front : Side.Back);
                if (at != side || Tiers.State(b, k0, i) >= s) continue;
                Tiers.SetState(b, k0, i, s); changed++;
            }
            return changed;
        }

        /// <summary>
        /// Where the Entrance tool would put a door at a facade point: the ground floor, a setback's first floor onto its
        /// terrace, or any floor with a neighbour’s roof or terrace in front at that level. The error says why not.
        /// </summary>
        public static (DoorSpot? door, string? error) EntranceAt(Site site, BuildingData b, FacadeHit ed)
        {
            bool Ter(int k) => Derived.HasTerrace(b, k) && Facade.TerraceAt(b, k, ed.i, ed.t);
            int kc = ed.k, k0 = ed.k0;
            int k = kc == 0 ? 0 : Ter(kc) ? kc : Facade.LandingAt(site, b, kc, ed.i, ed.t) != null ? kc : Ter(k0) ? k0 : -1;
            if (k < 0) return (null, "No terrace, and no neighbour’s roof or terrace at this level, in front of this wall");
            var fp = Derived.OutlineAt(b, k); var a = fp[ed.i]; var c = fp[(ed.i + 1) % fp.Count];
            var d = Walls.DoorAt(site, b, k, new Vec2(a.x + (c.x - a.x) * ed.t, a.z + (c.z - a.z) * ed.t), true);
            return d != null ? (d, null) : (null, "That wall is too short for an entrance");
        }

        /// <summary>
        /// The Doors card's Place doors click: add an entrance with <paramref name="door"/> (<see cref="EntranceData.door"/>;
        /// null, the style's pick), or remove the one there. The error says why nothing happened.
        /// </summary>
        public static string? ToggleEntrance(Site site, BuildingData b, FacadeHit ed, string? door = null)
        {
            var (d, err) = EntranceAt(site, b, ed);
            if (d == null) return err;
            if (d.entrance >= 0) b.entrances.RemoveAt(d.entrance);
            else b.entrances.Add(new EntranceData { edge = d.edge, t = d.t, k = d.k, door = door });
            return null;
        }

        /// <summary>
        /// The detail palette at a facade point, for the kind with id <paramref name="kind"/> (<see cref="DetailKinds"/>):
        /// the detail to add, or the hand-placed one to remove, and its ghost.
        /// </summary>
        public static DetailSpot DetailAt(Site site, BuildingData b, FacadeHit ed, string kind)
        {
            var spec = DetailKinds.Get(kind);
            if (spec == null) return new DetailSpot { error = "That detail isn’t in the project" };
            bool artist = spec.lod0 != null;
            int k = ed.k, k0 = ed.k0; double h = Derived.FloorH(b, k), L = ed.L;
            var fp = Derived.OutlineAt(b, k); var a = fp[ed.i]; var c = fp[(ed.i + 1) % fp.Count]; double sg = Geo.Area2(fp) > 0 ? 1 : -1;
            var u0 = new Vec2((c.x - a.x) / L, (c.z - a.z) / L); var nrm = new Vec2(sg * u0.z, -sg * u0.x);   // outward normal
            GhostBox Box(double uc, double y0, double y1, double w, double dp) => new GhostBox
            {
                cx = a.x + u0.x * uc + nrm.x * (Dim.T_EXT + dp / 2) + b.pos.x, cy = (y0 + y1) / 2, cz = a.z + u0.z * uc + nrm.z * (Dim.T_EXT + dp / 2) + b.pos.z,
                sx = w, sy = y1 - y0, sz = dp, u = u0,
            };
            double u = ed.t * L, y = ed.y - Derived.FloorBase(b, k), fb = Derived.FloorBase(b, k);

            // an existing hand-placed item here comes off again
            var items = Details.At(site, b, k).Where(d => d.man != null && d.i == ed.i
                && Math.Abs(d.u - u) < Math.Max(d.kind.w != 0 && !d.Spans ? d.kind.w : 1.0, 0.7) / 2 + 0.15
                && (d.kind.anchor != Details.Anchor.Wall || Math.Abs(d.y + d.kind.h / 2 - y) < d.kind.h / 2 + 0.4)).ToList();
            if (items.Count > 0)
            {
                var d = items[0]; int ei = b.details.IndexOf(d.man!);
                if (d.kind.lod0 != null)   // an artist's: its own size, where its pivot is
                {
                    bool up = d.kind.anchor == Details.Anchor.Tier;
                    double ya = up ? fb : fb + d.y + d.kind.bottom, ha = up && d.kind.stretch ? h : d.kind.h;
                    return new DetailSpot { remove = ei, ghost = Box(d.Spans ? (d.u0 + d.u1) / 2 : d.u, ya, ya + ha, d.Width, d.kind.d) };
                }
                double yb = d.kind.anchor == Details.Anchor.Wall ? fb + d.y : d.kind.anchor == Details.Anchor.Opening ? fb + d.yt - 0.15 : fb;
                double w = d.kind.w != 0 ? d.kind.w : (d.u1 - d.u0) != 0 ? d.u1 - d.u0 : 1;
                return new DetailSpot { remove = ei, ghost = Box(d.u, yb, yb + (d.kind.h != 0 ? d.kind.h : h), w, d.kind.d) };
            }
            if (Party.Ranges(site, b, k0, ed.i).Any(r => u > r.s - 0.3 && u < r.e + 0.3)) return new DetailSpot { error = "That wall is shared with the building next door" };
            var f = Details.EdgeFrameAt(b, k, ed.i); var ops = Facade.Ops(site, b, k, ed.i, L, f.uS, f.uE);
            if (spec.anchor == Details.Anchor.Opening)
            {
                if (k > 0) return new DetailSpot { error = artist ? spec.name + " goes over a ground-floor window or door" : "Awnings go over a ground-floor window or door" };
                Opening? best = null; double bd = 0;
                foreach (var o in ops)
                {
                    double cc = (o.u0 + o.u1) / 2, dd = Math.Abs(cc - u);
                    if (dd < Math.Max(0.6, (o.u1 - o.u0) / 2 + 0.3) && (best == null || dd < bd)) { best = o; bd = dd; }
                }
                if (best == null) return new DetailSpot { error = artist ? "Click a window or door to put " + spec.name + " over it" : "Click a window or door to put an awning over it" };
                var add = new DetailData { kind = kind, k = k, edge = ed.i, t = (best.u0 + best.u1) / 2 / L };
                var (sa, sb) = Details.OverSpan(best);
                double ya = fb + best.Top + Details.OverLift + spec.bottom;
                if (artist) return new DetailSpot { add = add, ghost = Box((sa + sb) / 2, ya, ya + spec.h, spec.stretch ? sb - sa : spec.w, spec.d) };
                double yt = fb + best.Top + 0.06;
                return new DetailSpot { add = add, ghost = Box((sa + sb) / 2, yt - 0.15, yt + 0.45, sb - sa, 1.0) };
            }
            double half = spec.w / 2 + 0.15;
            if (L < 2 * half + 0.2) return new DetailSpot { error = "That wall is too short" };
            u = Tiers.Clamp(u, half, L - half);
            if (spec.anchor == Details.Anchor.Tier)
            {
                // not across a door (a fire escape's stair 35 cm clear of it, as the prototype's) on any storey it climbs past, nor
                // a fire escape's drop ladder coming down over one
                bool escape = kind == DetailKinds.Escape;
                var run = Details.EscapeRun(b, k, ed.i, u, Details.RunHalf(spec));
                var steps = run.Select(r => (r.k, r.i, r.u, reach: spec.w / 2 + (escape ? 0.35 : 0.05))).ToList();
                if (escape && k > k0) steps.Insert(0, (k - 1, ed.i, u, 0.7));
                foreach (var st in steps)
                {
                    var fs = Details.EdgeFrameAt(b, st.k, st.i); var o2 = Facade.Ops(site, b, st.k, st.i, fs.e.L, fs.uS, fs.uE);
                    if (o2.Any(o => o.door && st.u + st.reach > o.Left && st.u - st.reach < o.Right))
                        return new DetailSpot { error = !escape ? spec.name + " can’t run across a door" : st.k < k ? "The ladder would come down over a door" : "A fire escape can’t run across a door" };
                }
                var last = run[run.Count - 1];
                double yt = Derived.FloorBase(b, last.k) + Derived.FloorH(b, last.k);
                return new DetailSpot { add = new DetailData { kind = kind, k = k, edge = ed.i, t = u / L }, ghost = Box(u, fb, yt, spec.w, spec.d) };
            }
            if (artist && h - spec.h - 0.1 < 0.15) return new DetailSpot { error = spec.name + " is too tall for this storey" };
            y = Tiers.Clamp(y, 0.15, h - spec.h - 0.1);
            if (ops.Any(o => u + spec.w / 2 > o.Left - 0.05 && u - spec.w / 2 < o.Right + 0.05 && y + spec.h > o.Bottom - 0.05 && y < o.Top + 0.05))
                return new DetailSpot { error = "That would sit across a window or door" };
            // where the roof over the terrace below comes up the wall, it would stand in it: Details.At leaves it out
            if (Roofs.TerraceRoofAt(site, b, k, ed.i, u - spec.w / 2, u + spec.w / 2) > fb + y + spec.bottom - Roofs.TerraceRoofClear)
                return new DetailSpot { error = "That would sit in the roof below" };
            return new DetailSpot { add = new DetailData { kind = kind, k = k, edge = ed.i, t = u / L, y = Tiers.Fixed(y, 2) }, ghost = Box(u, fb + y, fb + y + spec.h, spec.w, spec.d) };
        }

        /// <summary>The detail palette's click: add the detail or remove the one there. The error says why nothing happened.</summary>
        public static string? ToggleDetail(Site site, BuildingData b, FacadeHit ed, string kind)
        {
            var r = DetailAt(site, b, ed, kind);
            if (r.error != null) return r.error;
            if (r.remove >= 0) b.details.RemoveAt(r.remove); else b.details.Add(r.add!);
            return null;
        }

        /// <summary>The setback at k0's uncovered roof below: a walkable terrace, or a pitched roof (30° to start).</summary>
        public static void SetTerraceRoof(BuildingData b, int k0, bool roof)
        {
            var f = b.floors[k0];
            if ((f.terraceRoof != null) == roof) return;
            f.terraceRoof = roof ? new TerraceRoofData { pitch = 30 } : null;
        }
    }

    /// <summary>A world-space point or direction, engine-free.</summary>
    public struct Vec3d
    {
        public double x, y, z;
        public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    }
}
