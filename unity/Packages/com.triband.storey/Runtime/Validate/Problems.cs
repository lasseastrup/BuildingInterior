#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;

namespace Triband.Storey.Validate
{
    public enum Severity { Warning, Error }

    /// <summary>Something wrong with a layout: where (building, storey, a world point to look at), how bad, and what.</summary>
    public sealed class Problem
    {
        public Severity severity;
        public string buildingId = "", building = "";
        /// <summary>The storey it is on; the floor count means the roof, −1 the whole building.</summary>
        public int k = -1;
        public double x, y, z;
        public string message = "";
        /// <summary>A short code for tests and filters: "unreached", "no-door", "door-nowhere", …</summary>
        public string code = "";
        /// <summary>The other building, for a problem between two ("overlap"); "" otherwise.</summary>
        public string otherId = "";
        public override string ToString() => $"{severity} {building} {(k >= 0 ? "k" + k : "")}: {message}";
    }

    /// <summary>
    /// The problem list (docs/EDITOR.md §6.5): what in a layout is broken or can't be used. Each storey of a walk-in
    /// building is walked as the player would, on a 25 cm grid with the walk model's own walls (LOD0's collision
    /// segments), from the ways in: street doors, bridges, and the stairs and lifts between storeys. Parts nobody can
    /// reach are listed, with doors that open onto nothing, cores and openings that no longer fit, bridges that no longer
    /// meet, and buildings that overlap. Engine-free; fast enough to run after an edit settles (tens of milliseconds
    /// for a street).
    /// </summary>
    public static class Problems
    {
        /// <summary>The grid's cell, the clearance kept from a wall (about the player's radius), and the smallest area worth reporting.</summary>
        public const double Cell = 0.25, Clear = 0.25, MinArea = 2.0;

        public static List<Problem> Check(Site site, PlayWorld? world = null) => Check(site, world, null);

        /// <summary>
        /// The problems of the buildings in <paramref name="only"/> (all when null), and the overlaps they are part of.
        /// Give it a <see cref="Scope"/>: an edit's buildings with the neighbours and bridges that can change with them.
        /// <see cref="Merge"/> puts the result into the last full list.
        /// </summary>
        public static List<Problem> Check(Site site, PlayWorld? world, ICollection<string>? only)
        {
            world ??= new PlayWorld(site);
            bool In(BuildingData b) => only == null || only.Contains(b.id);
            var o = new List<Problem>();
            foreach (var b in site.Buildings) if (In(b)) Static(site, b, o);
            Overlaps(site, o, In);
            Reach(site, world, o, In);
            return o;
        }

        /// <summary>
        /// What a check after an edit must cover: the buildings that changed (by id; added, edited or removed), every
        /// building within a couple of metres of where they are or were (party walls, doors onto a neighbour's roof),
        /// and every building joined to those by bridges, however many in turn (one can only be reached through another).
        /// </summary>
        public static HashSet<string> Scope(Site site, Site? before, IEnumerable<string> changed)
        {
            var scope = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in changed)
            {
                scope.Add(id);
                foreach (var s in new[] { site, before })
                {
                    var b = s?.ById(id); if (b == null) continue;
                    var bb = Site.BoundsOf(b);
                    double cx = (bb.x0 + bb.x1) / 2, cz = (bb.z0 + bb.z1) / 2, r = Math.Sqrt((bb.x1 - bb.x0) * (bb.x1 - bb.x0) + (bb.z1 - bb.z0) * (bb.z1 - bb.z0)) / 2 + 2;
                    foreach (var n in site.Near(cx, cz, r))
                    {
                        var nb = Site.BoundsOf(n);
                        if (nb.x0 <= bb.x1 + 2 && nb.x1 >= bb.x0 - 2 && nb.z0 <= bb.z1 + 2 && nb.z1 >= bb.z0 - 2) scope.Add(n.id);
                    }
                }
            }
            // the bridges, until nothing more joins
            for (bool grew = true; grew; )
            {
                grew = false;
                foreach (var b in site.Buildings)
                    foreach (var br in b.bridges)
                        if (scope.Contains(b.id) != scope.Contains(br.to) && site.ById(br.to) != null) { scope.Add(b.id); scope.Add(br.to); grew = true; }
            }
            return scope;
        }

        /// <summary>Each building's text, by id: what <see cref="Changed"/> compares.</summary>
        public static Dictionary<string, string> Prints(StoreyDocument d)
        {
            var o = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var b in d.buildings) o[b.id] = PrototypeJson.Write(b);
            return o;
        }

        /// <summary>The buildings added, removed or changed between two layouts' <see cref="Prints"/>.</summary>
        public static List<string> Changed(Dictionary<string, string> before, Dictionary<string, string> after)
        {
            var o = new List<string>();
            foreach (var kv in after) if (!before.TryGetValue(kv.Key, out var t) || t != kv.Value) o.Add(kv.Key);
            foreach (var id in before.Keys) if (!after.ContainsKey(id)) o.Add(id);
            return o;
        }

        /// <summary>
        /// The last full list with a scoped check's result in place of what it covered: problems of buildings in the
        /// scope, overlaps with one, and anything of a building that is gone are replaced. In layout order.
        /// </summary>
        public static List<Problem> Merge(Site site, List<Problem> last, List<Problem> fresh, ICollection<string> scope)
        {
            var o = new List<Problem>();
            foreach (var p in last)
            {
                if (scope.Contains(p.buildingId) || (p.otherId.Length > 0 && scope.Contains(p.otherId)) || site.ById(p.buildingId) == null) continue;
                o.Add(p);
            }
            o.AddRange(fresh);
            int Ix(Problem p) { var b = site.ById(p.buildingId); return b != null ? site.IndexOf(b) : int.MaxValue; }
            return o.Select((p, i) => (p, i)).OrderBy(t => Ix(t.p)).ThenBy(t => t.i).Select(t => t.p).ToList();
        }

        static string Floor(BuildingData b, int k) => k >= b.floors.Count ? "the roof" : k == 0 ? "the ground floor" : "floor " + k;
        static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        static Problem P(BuildingData b, int k, Severity sv, string code, string msg, double lx, double lz, double? y = null) => new Problem
        {
            severity = sv, buildingId = b.id, building = b.name, k = k, code = code, message = msg,
            x = lx + b.pos.x, z = lz + b.pos.z, y = y ?? Derived.FloorBase(b, Math.Max(0, Math.Min(k, b.floors.Count))) + 1,
        };

        static Vec2 Centre(List<Vec2> fp) => new Vec2(fp.Average(p => p.x), fp.Average(p => p.z));

        // ---- what can be told from the data alone ----

        static void Static(Site site, BuildingData b, List<Problem> o)
        {
            int N = b.floors.Count;
            foreach (var e in b.entrances)
            {
                var fp = Derived.OutlineAt(b, e.k);
                if (e.edge >= fp.Count) continue;
                var a = fp[e.edge]; var c = fp[(e.edge + 1) % fp.Count]; double ex = a.x + (c.x - a.x) * e.t, ez = a.z + (c.z - a.z) * e.t;
                if (e.k > 0 && !Facade.DoorOpen(site, b, e))
                    o.Add(P(b, e.k, Severity.Warning, "door-nowhere", $"A door on {Floor(b, e.k)} has no terrace or roof to open onto, so it isn't built", ex, ez));
                if (b.interior && Derived.Filled(b, e.k))
                    o.Add(P(b, e.k, Severity.Warning, "door-filled", $"A door opens into {Floor(b, e.k)}, which is filled", ex, ez));
            }
            foreach (var s in b.shafts)
            {
                if (!Tiers.ShaftFits(b, s)) o.Add(P(b, s.bottom, Severity.Error, "core-fit", $"{Kind(s)} from {Floor(b, s.bottom)} no longer fit every floor they serve", s.x, s.z));
                if (s.type == CoreType.Lift && Cores.Stops(b, s).Count < 2) o.Add(P(b, s.bottom, Severity.Warning, "lift-one-stop", "This lift has only one stop", s.x, s.z));
                foreach (var t in b.shafts) if (string.CompareOrdinal(s.id, t.id) < 0 && Cores.Levels(b, s).Intersect(Cores.Levels(b, t)).Any() && Shafts.Overlap(s, t, 0)) o.Add(P(b, s.bottom, Severity.Error, "core-overlap", $"{Kind(s)} and {Kind(t).ToLowerInvariant()} overlap", (s.x + t.x) / 2, (s.z + t.z) / 2));
            }
            foreach (var v in b.voids)
            {
                var why = Voids.Issue(b, v); if (why == VoidIssue.None) continue;
                var c = v.shape.Count > 0 ? Centre(v.shape) : Centre(b.footprint);
                o.Add(P(b, Math.Min(v.bottom, N - 1), Severity.Error, "void", $"{(v.kind == VoidKind.Courtyard ? "A courtyard" : "An atrium")}: {Voids.Why(why).TrimEnd('.')}", c.x, c.z));
            }
            foreach (var br in b.bridges)
            {
                var s = Bridges.Span(site, b, br);
                if (s == null)
                {
                    var to = site.ById(br.to);
                    o.Add(P(b, br.k, Severity.Error, "bridge", $"A bridge from {Floor(b, br.k)} no longer meets {(to != null ? to.name : "the building it went to")}", br.at.x, br.at.z));
                    continue;
                }
                if (!s.B.interior || Derived.Filled(s.B, br.toK)) o.Add(P(b, br.k, Severity.Warning, "bridge-shell", $"A bridge leads into {s.B.name}'s {Floor(s.B, br.toK)}, which has nothing inside", br.at.x, br.at.z));
                if (!b.interior || Derived.Filled(b, br.k)) o.Add(P(b, br.k, Severity.Warning, "bridge-shell", $"A bridge leaves from {Floor(b, br.k)}, which has nothing inside", br.at.x, br.at.z));
            }
        }

        static string Kind(CoreData s) => s.type == CoreType.Lift ? "The lift" : "The stairs";

        static void Overlaps(Site site, List<Problem> o, Func<BuildingData, bool> In)
        {
            var bs = site.Buildings;
            var box = bs.Select(Site.BoundsOf).ToArray();
            for (int i = 0; i < bs.Count; i++)
                for (int j = i + 1; j < bs.Count; j++)
                {
                    var a = bs[i]; var c = bs[j];
                    if (!In(a) && !In(c)) continue;
                    // only footprints whose boxes overlap can share area: the clip is for those
                    if (box[i].x0 >= box[j].x1 || box[j].x0 >= box[i].x1 || box[i].z0 >= box[j].z1 || box[j].z0 >= box[i].z1) continue;
                    var A = Courtyards.World(a, a.footprint); var C = Courtyards.World(c, c.footprint);
                    double area = Tiers.SharedArea(A, C);
                    if (area > 0.5)
                    {
                        var m = Centre(A);
                        o.Add(new Problem { severity = Severity.Error, buildingId = a.id, building = a.name, k = 0, code = "overlap", otherId = c.id, message = $"Overlaps {c.name} by {area:0} m²", x = m.x, y = 1, z = m.z });
                    }
                }
        }

        // ---- who can get where ----

        sealed class Grid
        {
            public BuildingData b = null!; public int k; public double x0, z0; public int nx, nz;
            public bool[] free = null!; public int[] region = null!; public List<int> sizes = new List<int>(); public List<(double x, double z)> sums = new List<(double, double)>();
            public int At(double lx, double lz)
            {
                int i = (int)Math.Floor((lx - x0) / Cell), j = (int)Math.Floor((lz - z0) / Cell);
                if (i < 0 || j < 0 || i >= nx || j >= nz) return -1;
                // the nearest free cell within half a metre: a way in lands on the floor beside it, not in a wall's clearance
                int best = -1; double bd = 1e9;
                for (int dj = -2; dj <= 2; dj++)
                    for (int di = -2; di <= 2; di++)
                    {
                        int ii = i + di, jj = j + dj; if (ii < 0 || jj < 0 || ii >= nx || jj >= nz) continue;
                        int c = jj * nx + ii; if (!free[c]) continue;
                        double d = di * di + dj * dj; if (d < bd) { bd = d; best = c; }
                    }
                return best < 0 ? -1 : region[best];
            }
        }

        static Grid Raster(BuildingData b, int k, Lod0Result l0)
        {
            var fp = Derived.OutlineAt(b, k); var bb = Tiers.Bbox(fp);
            var g = new Grid { b = b, k = k, x0 = bb.x0, z0 = bb.z0, nx = Math.Max(1, (int)Math.Ceiling((bb.x1 - bb.x0) / Cell)), nz = Math.Max(1, (int)Math.Ceiling((bb.z1 - bb.z0) / Cell)) };
            int n = g.nx * g.nz; g.free = new bool[n]; g.region = new int[n];
            for (int j = 0; j < g.nz; j++)
                for (int i = 0; i < g.nx; i++)
                {
                    double x = g.x0 + (i + 0.5) * Cell, z = g.z0 + (j + 0.5) * Cell;
                    bool f = Geo.Pip(fp, x, z);
                    if (f) foreach (var s in b.shafts) if (Cores.StairHoleAt(b, s, k) && InHole(s, x, z)) { f = false; break; }
                    if (f) foreach (var v in b.voids) if (Courtyards.HoleAt(b, v, k) && Geo.Pip(v.shape, x, z)) { f = false; break; }
                    g.free[j * g.nx + i] = f;
                }
            // the walls, as the walk model has them, each with a player's clearance
            if (k < l0.Segs.Count)
                foreach (var s in l0.Segs[k])
                {
                    double ax = s.ax - b.pos.x, az = s.az - b.pos.z, bx = s.bx - b.pos.x, bz = s.bz - b.pos.z, r = s.r + Clear;
                    int i0 = Math.Max(0, (int)Math.Floor((Math.Min(ax, bx) - r - g.x0) / Cell)), i1 = Math.Min(g.nx - 1, (int)Math.Floor((Math.Max(ax, bx) + r - g.x0) / Cell));
                    int j0 = Math.Max(0, (int)Math.Floor((Math.Min(az, bz) - r - g.z0) / Cell)), j1 = Math.Min(g.nz - 1, (int)Math.Floor((Math.Max(az, bz) + r - g.z0) / Cell));
                    for (int j = j0; j <= j1; j++)
                        for (int i = i0; i <= i1; i++)
                        {
                            int c = j * g.nx + i; if (!g.free[c]) continue;
                            if (Geo.SegDist(g.x0 + (i + 0.5) * Cell, g.z0 + (j + 0.5) * Cell, ax, az, bx, bz).d < r) g.free[c] = false;
                        }
                }
            // regions: 4-connected runs of free cells
            for (int c = 0; c < n; c++) g.region[c] = -1;
            var q = new Queue<int>();
            for (int c0 = 0; c0 < n; c0++)
            {
                if (!g.free[c0] || g.region[c0] >= 0) continue;
                int id = g.sizes.Count; int size = 0; double sx = 0, sz = 0;
                g.region[c0] = id; q.Enqueue(c0);
                while (q.Count > 0)
                {
                    int c = q.Dequeue(); size++; int i = c % g.nx, j = c / g.nx; sx += g.x0 + (i + 0.5) * Cell; sz += g.z0 + (j + 0.5) * Cell;
                    void Go(int ii, int jj) { if (ii < 0 || jj < 0 || ii >= g.nx || jj >= g.nz) return; int d = jj * g.nx + ii; if (g.free[d] && g.region[d] < 0) { g.region[d] = id; q.Enqueue(d); } }
                    Go(i + 1, j); Go(i - 1, j); Go(i, j + 1); Go(i, j - 1);
                }
                g.sizes.Add(size); g.sums.Add((sx, sz));
            }
            return g;
        }

        static bool InHole(CoreData s, double lx, double lz)
        {
            var (qx, qz) = PlayWorld.ToCore(s, lx, lz);
            if (s.type == CoreType.Flight)
            {
                double hw = Dim.FLIGHT_W / 2, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
                return qx > -hw && qx < -hw + Dim.FLIGHT_LANE && qz > z0 && qz < z1;
            }
            return s.type == CoreType.Stairs && Math.Abs(qx) < 1.3 && qz > -1.6 && qz < 2.6;
        }

        static Vec2 CorePoint(CoreData s, double qx, double qz)
        {
            double a = s.rot * Math.PI / 180;
            return new Vec2(s.x + qx * Math.Cos(a) - qz * Math.Sin(a), s.z + qx * Math.Sin(a) + qz * Math.Cos(a));
        }

        static void Reach(Site site, PlayWorld world, List<Problem> o, Func<BuildingData, bool> In)
        {
            // nodes: (building, storey, region); the outside is node 0
            var grids = new Dictionary<(string, int), Grid>();
            var ids = new Dictionary<(string, int, int), int>();
            var links = new List<List<int>> { new List<int>() };
            int Node(BuildingData b, int k, int r)
            {
                var key = (b.id, k, r);
                if (!ids.TryGetValue(key, out var n)) { ids[key] = n = links.Count; links.Add(new List<int>()); }
                return n;
            }
            void Link(int a, int c) { if (a < 0 || c < 0) return; links[a].Add(c); links[c].Add(a); }
            Grid? G(BuildingData b, int k)
            {
                if (!b.interior || k < 0 || k >= b.floors.Count || Derived.Filled(b, k)) return null;
                if (!grids.TryGetValue((b.id, k), out var g)) grids[(b.id, k)] = g = Raster(b, k, world.Lod0Of(b));
                return g;
            }
            int At(BuildingData b, int k, Vec2 local) { var g = G(b, k); if (g == null) return -1; int r = g.At(local.x, local.z); return r < 0 ? -1 : Node(b, k, r); }

            var ways = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var b in site.Buildings)
            {
                if (!b.interior || !In(b)) continue;
                bool way = false;
                foreach (var e in b.entrances)
                {
                    if (e.k != 0) continue;
                    var fp = Derived.OutlineAt(b, 0); if (e.edge >= fp.Count) continue;
                    var ed = Geo.EdgeInfo(fp, e.edge, Geo.Area2(fp) > 0);
                    var a = fp[e.edge]; var c = fp[(e.edge + 1) % fp.Count];
                    var p = new Vec2(a.x + (c.x - a.x) * e.t - ed.w.x * 0.6, a.z + (c.z - a.z) * e.t - ed.w.z * 0.6);
                    Link(0, At(b, 0, p)); way = true;
                }
                foreach (var s in b.shafts)
                {
                    var lv = Cores.Levels(b, s);
                    if (s.type == CoreType.Lift)
                    {
                        var stops = Cores.Stops(b, s).Where(k => k < b.floors.Count).Select(k => At(b, k, CorePoint(s, 0, 0))).Where(n => n >= 0).ToList();
                        for (int i = 1; i < stops.Count; i++) Link(stops[0], stops[i]);
                    }
                    else if (s.type == CoreType.Stairs)
                    {
                        foreach (int k in lv) if (Cores.HasFlight(b, s, k) && k + 1 < b.floors.Count) Link(At(b, k, CorePoint(s, 0, -2.1)), At(b, k + 1, CorePoint(s, 0, -2.1)));
                    }
                    else
                    {
                        double hw = Dim.FLIGHT_W / 2, hd = Dim.FLIGHT_D / 2, lane = -hw + Dim.FLIGHT_LANE / 2, z0 = -hd + Dim.FLIGHT_LANDING, z1 = hd - Dim.FLIGHT_LANDING;
                        foreach (int k in lv) if (Cores.HasFlight(b, s, k) && k + 1 < b.floors.Count) Link(At(b, k, CorePoint(s, lane, (-hd + z0) / 2)), At(b, k + 1, CorePoint(s, lane, (z1 + hd) / 2)));
                    }
                }
                ways[b.id] = way;
            }
            foreach (var a in site.Buildings)
                foreach (var br in a.bridges)
                {
                    if (!In(a)) continue;   // a scope holds both ends of every bridge (Scope)
                    var s = Bridges.Span(site, a, br); if (s == null) continue;
                    var pa = new Vec2(s.PA.x - s.u.x * (Dim.T_EXT + 0.6) - a.pos.x, s.PA.z - s.u.z * (Dim.T_EXT + 0.6) - a.pos.z);
                    var pb = new Vec2(s.PB.x - s.nB.x * (Dim.T_EXT + 0.6) - s.B.pos.x, s.PB.z - s.nB.z * (Dim.T_EXT + 0.6) - s.B.pos.z);
                    Link(At(a, br.k, pa), At(s.B, br.toK, pb));
                    ways[a.id] = true; ways[s.B.id] = true;
                }
            // everyone starts outside
            var seen = new bool[links.Count]; var q = new Queue<int>(); seen[0] = true; q.Enqueue(0);
            while (q.Count > 0) foreach (var n in links[q.Dequeue()]) if (!seen[n]) { seen[n] = true; q.Enqueue(n); }

            foreach (var b in site.Buildings)
            {
                if (!b.interior || !In(b)) continue;
                if (!ways[b.id]) { var c = Centre(b.footprint); o.Add(P(b, 0, Severity.Error, "no-door", "No way in: add a street door (Facade ▸ Entrance) or a bridge", c.x, c.z)); continue; }
                for (int k = 0; k < b.floors.Count; k++)
                {
                    var g = G(b, k); if (g == null) continue;
                    double cellA = Cell * Cell; bool any = false, arrive = false; var lost = new List<int>();
                    for (int r = 0; r < g.sizes.Count; r++)
                    {
                        bool got = ids.TryGetValue((b.id, k, r), out var n) && seen[n];
                        arrive |= got;
                        if (g.sizes[r] * cellA < MinArea) continue;
                        if (got) any = true; else lost.Add(r);
                    }
                    if (!any && lost.Count > 0)
                    {
                        int r = lost.OrderByDescending(x => g.sizes[x]).First();
                        // stairs or a lift arrive, but only into a pocket: something stands in the way out
                        string why = arrive ? "the stairs or lift arrive, but a wall closes them in" : "no stairs, lift or door get there";
                        o.Add(P(b, k, Severity.Error, "unreached", $"{Cap(Floor(b, k))} can't be reached: {why}", g.sums[r].x / g.sizes[r], g.sums[r].z / g.sizes[r]));
                        continue;
                    }
                    foreach (var r in lost)
                        o.Add(P(b, k, Severity.Warning, "unreached-part", $"Part of {Floor(b, k)} can't be reached (about {g.sizes[r] * cellA:0} m²): a room with no door?", g.sums[r].x / g.sizes[r], g.sums[r].z / g.sizes[r]));
                }
            }
        }
    }
}
