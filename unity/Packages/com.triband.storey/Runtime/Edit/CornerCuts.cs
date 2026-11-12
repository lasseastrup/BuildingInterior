#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// Cut corners as one corner each (docs/EDITOR.md §6.1). An outline holds a cut's points, so everything that builds
    /// or anchors to it (walls, doors, details) is unchanged; <see cref="CornerData"/> remembers the sharp corner each cut
    /// replaces. The editor shows and edits the <em>sharp</em> outline: every change to it goes through <see cref="Around"/>,
    /// which takes the cuts out, makes the change, and cuts the corners again. So a cut corner drags as one point, its
    /// cut follows its edges, and changing or clearing a cut is one step. Storey's own.
    /// </summary>
    public static class CornerCuts
    {
        /// <summary>A cut corner of the sharp outline: its index there, and how it is cut.</summary>
        public sealed class Spec
        {
            public int i;
            public Vec2 at;
            public CornerShape shape;
            public double size;
            public bool door;
        }

        static bool Same(Vec2 a, Vec2 b) => Math.Abs(a.x - b.x) < 1e-6 && Math.Abs(a.z - b.z) < 1e-6;

        /// <summary>Where a cut's points start in the outline, or −1 when they are not there, in order.</summary>
        static int Find(List<Vec2> fp, List<Vec2> pts)
        {
            int n = fp.Count, m = pts.Count;
            if (m < 2 || m >= n) return -1;
            for (int j = 0; j < n; j++)
            {
                if (!Same(fp[j], pts[0])) continue;
                bool all = true;
                for (int k = 1; k < m && all; k++) all = Same(fp[(j + k) % n], pts[k]);
                if (all) return j;
            }
            return -1;
        }

        /// <summary>Tier k0's cuts that still stand in its outline, with where each starts; the rest are dropped.</summary>
        public static List<(CornerData c, int start)> Live(BuildingData b, int k0)
        {
            var list = Tiers.Corners(b, k0); var fp = Tiers.Outline(b, k0);
            var live = new List<(CornerData, int)>(); var used = new HashSet<int>();
            foreach (var c in list)
            {
                int s = Find(fp, c.pts); if (s < 0) continue;
                bool clash = false;
                for (int k = 0; k < c.pts.Count; k++) clash |= used.Contains((s + k) % fp.Count);
                if (clash) continue;
                for (int k = 0; k < c.pts.Count; k++) used.Add((s + k) % fp.Count);
                live.Add((c, s));
            }
            if (live.Count != list.Count) { list.Clear(); list.AddRange(live.Select(x => x.Item1)); }
            return live.OrderBy(x => x.Item2).ToList();
        }

        /// <summary>
        /// Each of the outline's points' place in the sharp outline (a cut's points share their corner's), and the sharp
        /// outline itself. A cut that runs over the outline's first point takes the sharp outline's first place.
        /// </summary>
        static (int[] map, List<Vec2> sharp) Collapse(List<Vec2> fp, List<(CornerData c, int start)> live)
        {
            int n = fp.Count; var owner = new CornerData?[n];
            foreach (var (c, s) in live) for (int k = 0; k < c.pts.Count; k++) owner[(s + k) % n] = c;
            var map = new int[n]; var sharp = new List<Vec2>(); var placed = new Dictionary<CornerData, int>();
            for (int j = 0; j < n; j++)
            {
                var c = owner[j];
                if (c == null) { map[j] = sharp.Count; sharp.Add(fp[j]); continue; }
                if (!placed.TryGetValue(c, out int at)) { placed[c] = at = sharp.Count; sharp.Add(c.at); }
                map[j] = at;
            }
            return (map, sharp);
        }

        /// <summary>
        /// For the generator (a gable or shed roof is planned on the sharp outline): each of tier k0's outline points' place
        /// in the sharp outline (a cut's points share their corner's), and the sharp outline. Null when no cut stands.
        /// Reads only: unlike <see cref="Live"/> it never drops a cut that no longer stands from the layout.
        /// </summary>
        public static (int[] map, List<Vec2> sharp)? SharpMap(BuildingData b, int k0)
        {
            var list = Tiers.Corners(b, k0); if (list.Count == 0) return null;
            var fp = Tiers.Outline(b, k0);
            var live = new List<(CornerData, int)>(); var used = new HashSet<int>();
            foreach (var c in list)
            {
                int st = Find(fp, c.pts); if (st < 0) continue;
                bool clash = false;
                for (int k = 0; k < c.pts.Count; k++) clash |= used.Contains((st + k) % fp.Count);
                if (clash) continue;
                for (int k = 0; k < c.pts.Count; k++) used.Add((st + k) % fp.Count);
                live.Add((c, st));
            }
            if (live.Count == 0) return null;
            return Collapse(fp, live.OrderBy(x => x.Item2).ToList());
        }

        /// <summary>The sharp outline of tier k0, as the editor shows it, and the cut corners on it.</summary>
        public static (List<Vec2> sharp, List<Spec> cuts) Sharp(BuildingData b, int k0)
        {
            var fp = Tiers.Outline(b, k0); var live = Live(b, k0);
            var (map, sharp) = Collapse(fp, live);
            return (sharp, live.Select(x => new Spec { i = map[x.start], at = x.c.at, shape = x.c.shape, size = x.c.size, door = x.c.door }).OrderBy(s => s.i).ToList());
        }

        /// <summary>A thing on a cut's own edges, kept while the cut is out: which cut, which of its edges, how far along.</summary>
        sealed class Kept { public Vec2 at; public int j; public double t; public EntranceData? e; public DetailData? d; public WindowData? w; public EdgeState state; }

        /// <summary>
        /// Take every cut of tier k0 out: the outline becomes the sharp one. Doors, details and blank walls on the edges
        /// either side of a cut keep their places on the longer edges; those on a cut's own edges are returned, to go back
        /// on the cut when it is made again the same way.
        /// </summary>
        static (List<Spec> cuts, List<Kept> kept) Uncut(BuildingData b, int k0)
        {
            var fp = Tiers.Outline(b, k0); var live = Live(b, k0); int n = fp.Count;
            var cuts = new List<Spec>(); var kept = new List<Kept>();
            if (live.Count == 0) return (cuts, kept);
            var (map, sharp) = Collapse(fp, live); int m = sharp.Count;
            var runOf = new Dictionary<int, (CornerData c, int j)>();
            foreach (var (c, s) in live) for (int k = 0; k + 1 < c.pts.Count; k++) runOf[(s + k) % n] = (c, k);
            (int, double)? Remap(int edge, double t, Func<Kept> keep)
            {
                if (runOf.TryGetValue(edge, out var r)) { var kk = keep(); kk.at = r.c.at; kk.j = r.j; kk.t = t; kept.Add(kk); return null; }
                var p0 = fp[edge]; var p1 = fp[(edge + 1) % n];
                double px = p0.x + (p1.x - p0.x) * t, pz = p0.z + (p1.z - p0.z) * t;
                int ne = map[edge]; var s0 = sharp[ne]; var s1 = sharp[(ne + 1) % m];
                double L2 = (s1.x - s0.x) * (s1.x - s0.x) + (s1.z - s0.z) * (s1.z - s0.z);
                double nt = L2 > 1e-12 ? ((px - s0.x) * (s1.x - s0.x) + (pz - s0.z) * (s1.z - s0.z)) / L2 : 0.5;
                return (ne, Math.Max(0, Math.Min(1, nt)));
            }
            var ents = new List<EntranceData>();
            foreach (var e in b.entrances)
            {
                if (!Tiers.DoorOn(b, e, k0)) { ents.Add(e); continue; }
                var r = Remap(e.edge, e.t, () => new Kept { e = e });
                if (r != null) { e.edge = r.Value.Item1; e.t = r.Value.Item2; ents.Add(e); }
            }
            b.entrances = ents;
            var dets = new List<DetailData>();
            foreach (var d in b.details)
            {
                if (Derived.TierStart(b, d.k) != k0) { dets.Add(d); continue; }
                var r = Remap(d.edge, d.t, () => new Kept { d = d });
                if (r != null) { d.edge = r.Value.Item1; d.t = r.Value.Item2; dets.Add(d); }
            }
            b.details = dets;
            var wins = new List<WindowData>();
            foreach (var w in b.windows)
            {
                if (Derived.TierStart(b, w.k) != k0) { wins.Add(w); continue; }
                var r = Remap(w.edge, w.t, () => new Kept { w = w });
                if (r != null) { w.edge = r.Value.Item1; w.t = r.Value.Item2; wins.Add(w); }
            }
            b.windows = wins;
            foreach (int j in Tiers.Blank(b, k0))
                if (runOf.TryGetValue(j, out var r)) kept.Add(new Kept { at = r.c.at, j = r.j, state = Tiers.State(b, k0, j) });
            Tiers.MapEdges(b, k0, l =>
            {
                var nb = new List<int>();
                foreach (int j in l) if (!runOf.ContainsKey(j)) nb.Add(map[j]);
                return nb.Distinct().ToList();
            });
            foreach (var (c, s) in live) cuts.Add(new Spec { i = map[s], at = c.at, shape = c.shape, size = c.size, door = c.door });
            fp.Clear(); fp.AddRange(sharp);
            Tiers.Corners(b, k0).Clear();
            return (cuts, kept);
        }

        /// <summary>
        /// Cut the corners of tier k0's sharp outline as <paramref name="cuts"/> say (by index), last first so the indices
        /// hold. A cut that doesn't fit any more (its edges got too short, or the outline rules refuse it) is left out and
        /// the corner stays sharp. Things kept from a cut made the same way go back on it; a corner entrance is opened on
        /// a chamfer that has none. Returns how many cuts didn't fit.
        /// </summary>
        static int Recut(BuildingData b, int k0, List<Spec> cuts, List<Kept> kept)
        {
            int failed = 0; var made = new List<(CornerData c, Spec s)>();
            foreach (var s in cuts.OrderByDescending(x => x.i))
            {
                var fp = Tiers.Outline(b, k0);
                if (s.i < 0 || s.i >= fp.Count) { failed++; continue; }
                var at = fp[s.i];
                if (Outlines.Corner(b, k0, s.i, s.shape, s.size, out int first, out int count) != OutlineIssue.None) { failed++; continue; }
                var nf = Tiers.Outline(b, k0);
                var c = new CornerData { at = at, shape = s.shape, size = s.size, door = s.door && k0 == 0 && s.shape == CornerShape.Chamfer };
                for (int k = 0; k <= count; k++) c.pts.Add(nf[(first + k) % nf.Count]);
                // the cuts made already (later in the outline) moved up by this one's new points
                Tiers.Corners(b, k0).Add(c);
                made.Add((c, s));
            }
            // the things on each cut's edges, by the cut's place now
            foreach (var (c, s) in made)
            {
                var fp = Tiers.Outline(b, k0); int start = Find(fp, c.pts); if (start < 0) continue;
                int edges = c.pts.Count - 1; bool hasDoor = false;
                foreach (var kk in kept.Where(x => Same(x.at, s.at)))
                {
                    if (kk.j >= edges) continue;
                    int edge = (start + kk.j) % fp.Count;
                    if (kk.e != null) { kk.e.edge = edge; kk.e.t = kk.t; b.entrances.Add(kk.e); hasDoor |= kk.e.k == 0; }
                    else if (kk.d != null) { kk.d.edge = edge; kk.d.t = kk.t; b.details.Add(kk.d); }
                    else if (kk.w != null) { kk.w.edge = edge; kk.w.t = kk.t; b.windows.Add(kk.w); }
                    else if (kk.state != EdgeState.Open) Tiers.SetState(b, k0, edge, kk.state);
                }
                if (c.door && !hasDoor && edges == 1) b.entrances.Add(new EntranceData { edge = start, t = 0.5, k = 0 });
            }
            return failed;
        }

        /// <summary>
        /// Make a change to tier k0's <em>sharp</em> outline (<paramref name="op"/> gets the building with its cuts out, and
        /// the cuts by index, which it may change: add, restyle or remove one, or move it with its corner), then cut the
        /// corners again. When the change adds or removes corners, a cut finds its corner again by where it is. False
        /// (nothing to keep) when <paramref name="op"/> refuses; <paramref name="lost"/> counts the cuts that no longer fit.
        /// </summary>
        public static bool Around(BuildingData b, int k0, Func<BuildingData, List<Spec>, bool> op, out int lost)
        {
            lost = 0;
            var (cuts, kept) = Uncut(b, k0);
            int n0 = Tiers.Outline(b, k0).Count;
            foreach (var c in cuts) c.at = Tiers.Outline(b, k0)[c.i];
            if (!op(b, cuts)) return false;
            var fp = Tiers.Outline(b, k0);
            if (fp.Count != n0)
                foreach (var c in cuts) c.i = fp.FindIndex(p => Same(p, c.at));
            // kept things follow a cut that moved with its corner
            foreach (var c in cuts)
                if (c.i >= 0 && c.i < fp.Count && !Same(fp[c.i], c.at))
                {
                    foreach (var kk in kept.Where(x => Same(x.at, c.at))) kk.at = fp[c.i];
                    c.at = fp[c.i];
                }
            var specs = cuts.Where(c => c.i >= 0 && c.i < fp.Count).GroupBy(c => c.i).Select(g => g.Last()).ToList();
            lost = cuts.Count - specs.Count + Recut(b, k0, specs, kept);
            return true;
        }

        public static bool Around(BuildingData b, int k0, Func<BuildingData, List<Spec>, bool> op) => Around(b, k0, op, out _);

        /// <summary>Cut (or cut again another way) corner i of the sharp outline. The issue when it doesn't fit.</summary>
        public static OutlineIssue Cut(BuildingData b, int k0, int i, CornerShape shape, double size, bool door)
        {
            // try it on the sharp outline alone first, for the reason it might not fit
            var (sharp, _) = Sharp(b, k0);
            if (i < 0 || i >= sharp.Count) return OutlineIssue.Shape;
            int lost = 0; bool fits = true;
            Around(b, k0, (bb, cuts) =>
            {
                cuts.RemoveAll(c => c.i == i);
                cuts.Add(new Spec { i = i, at = Tiers.Outline(bb, k0)[i], shape = shape, size = size, door = door });
                return true;
            }, out lost);
            fits = Live(b, k0).Any(x => Same(x.c.at, sharp[i]));
            if (fits) return OutlineIssue.None;
            // why: the same cut on the outline as it stands (its other cuts made)
            var probe = PrototypeJson.ReadBuilding(PrototypeJson.Write(b));
            var (ps, _) = Sharp(probe, k0); int pi = ps.FindIndex(p => Same(p, sharp[i]));
            int at = pi < 0 ? -1 : Tiers.Outline(probe, k0).FindIndex(p => Same(p, sharp[i]));
            var why = at < 0 ? OutlineIssue.Shape : Outlines.Corner(probe, k0, at, shape, size, out _, out _);
            return why == OutlineIssue.None ? OutlineIssue.Shape : why;
        }

        /// <summary>Make corner i of the sharp outline sharp again. False when it wasn't cut.</summary>
        public static bool Clear(BuildingData b, int k0, int i)
        {
            bool had = false;
            Around(b, k0, (bb, cuts) => { had = cuts.RemoveAll(c => c.i == i) > 0; return true; });
            return had;
        }

        /// <summary>Every corner of the sharp outline that can take it cut this way. Returns how many are cut.</summary>
        public static int CutAll(BuildingData b, int k0, CornerShape shape, double size)
        {
            Around(b, k0, (bb, cuts) =>
            {
                var fp = Tiers.Outline(bb, k0); cuts.Clear();
                for (int i = 0; i < fp.Count; i++) cuts.Add(new Spec { i = i, at = fp[i], shape = shape, size = size });
                return true;
            });
            return Tiers.Corners(b, k0).Count;
        }

        /// <summary>Every cut of tier k0 cleared: the outline is sharp again. Returns how many there were.</summary>
        public static int ClearAll(BuildingData b, int k0)
        {
            int had = 0;
            Around(b, k0, (bb, cuts) => { had = cuts.Count; cuts.Clear(); return true; });
            return had;
        }

        /// <summary>
        /// Corner i of tier k0's sharp outline moved to <paramref name="to"/> (the building's own metres), as the Shape tab's
        /// list of corners types it (docs/EDITOR.md §6.11, Storey's own): its cut and its neighbours' made again to meet their
        /// edges (<see cref="Around"/>). False, nothing to keep, where it doesn't move, the outline rules refuse it
        /// (<paramref name="why"/>), or a cut would no longer fit its edges (<paramref name="lost"/>: how many), which a drag
        /// allows but a typed place doesn't: round the corner less first.
        /// </summary>
        public static bool Move(BuildingData b, int k0, int i, Vec2 to, out OutlineIssue why, out int lost)
        {
            why = OutlineIssue.None; lost = 0;
            var (sharp, _) = Sharp(b, k0);
            if (i < 0 || i >= sharp.Count || Same(sharp[i], to)) return false;
            var nf = Tiers.Copy(sharp); nf[i] = to;
            var w = OutlineIssue.None;
            bool done = Around(b, k0, (x, _) => (w = Outlines.Set(x, k0, nf)) == OutlineIssue.None, out lost);
            why = w;
            return done && lost == 0;
        }

        /// <summary>
        /// The largest cut corner i of tier k0's sharp outline takes this way, as its edges and its neighbours' cuts leave
        /// room (<see cref="Outlines.CornerPoints"/> keeps 0.3 m of each edge; and 2 cm for the cut's ends, kept to the
        /// centimetre): the Shape tab's Radius or Size goes up to it. 0 where it takes none (a straight corner). The outline
        /// rules (setbacks, cores) may still refuse one that big. Storey's own.
        /// </summary>
        public static double Largest(BuildingData b, int k0, int i, CornerShape shape)
        {
            var (sharp, cuts) = Sharp(b, k0); int n = sharp.Count;
            if (i < 0 || i >= n) return 0;
            // half the angle between corner j's edges, and how far back along them a cut of its starts
            double Half(int j)
            {
                var A = sharp[(j - 1 + n) % n]; var V = sharp[j]; var C = sharp[(j + 1) % n];
                double L1 = Tiers.Hypot(A.x - V.x, A.z - V.z), L2 = Tiers.Hypot(C.x - V.x, C.z - V.z);
                if (L1 < 1e-6 || L2 < 1e-6) return Math.PI / 2;
                return Math.Acos(Tiers.Clamp(((A.x - V.x) * (C.x - V.x) + (A.z - V.z) * (C.z - V.z)) / (L1 * L2), -1, 1)) / 2;
            }
            double Back(int j)
            {
                var c = cuts.FirstOrDefault(x => x.i == j);
                return c == null || j == i ? 0 : c.shape == CornerShape.Round ? c.size / Math.Tan(Half(j)) : c.size;
            }
            double half = Half(i);
            if (half > (Math.PI - 0.05) / 2) return 0;   // straight: nothing to cut
            int prev = (i - 1 + n) % n, next = (i + 1) % n;
            double room = Math.Min(Tiers.EdgeLen(sharp, prev) - Back(prev), Tiers.EdgeLen(sharp, i) - Back(next)) - 0.3 - 0.02;
            if (room <= 0) return 0;
            return shape == CornerShape.Round ? room * Math.Tan(half) : room;
        }

        /// <summary>
        /// How much less than a circle's radius its corners are rounded (<see cref="Circle"/>): an arc leaves 0.3 m of each
        /// edge (<see cref="Outlines.CornerPoints"/>), and a centimetre is kept for the outline's centimetres, so a circle has
        /// four short straight walls, 0.32 m long, where it touches the square round it. The arcs are within 8 cm of a true
        /// circle of the radius.
        /// </summary>
        public const double CircleGap = 0.16;

        /// <summary>
        /// Tier k0's outline made a circle of <paramref name="radius"/> metres, round the middle of the outline as it stands
        /// (docs/EDITOR.md §6.1, Storey's own): a square's four corners, each rounded as far as it goes
        /// (<see cref="CircleGap"/> less than the radius), so it is four corners to drag, type (<see cref="Move"/>) or round
        /// less one at a time, not dozens. What was anchored to the old outline's walls goes, as with a new footprint
        /// (<see cref="Setbacks.ApplyFootprint"/>: setbacks, cores and courtyards that don't fit go too). The base's street door
        /// goes on the wall nearest the front (the square's first edge) where the standard doorway fits; where none does
        /// (<paramref name="door"/> false: a circle under about 10 m, whose arcs' 15° steps are too short), it is on the
        /// front's short wall, built once that is long enough. The issue when the outline rules refuse it, the building
        /// then the caller's to put back.
        /// </summary>
        public static OutlineIssue Circle(BuildingData b, int k0, double radius, out bool door)
        {
            door = true;
            double R = Tiers.Cm(radius), r = R - CircleGap;
            if (r < 0.5) return OutlineIssue.Shape;
            var (sharp, _) = Sharp(b, k0);
            double cx = Tiers.Cm((sharp.Min(p => p.x) + sharp.Max(p => p.x)) / 2), cz = Tiers.Cm((sharp.Min(p => p.z) + sharp.Max(p => p.z)) / 2);
            // counter-clockwise, from the front's left, as the presets go
            var square = new List<Vec2> { new Vec2(Tiers.Cm(cx - R), Tiers.Cm(cz - R)), new Vec2(Tiers.Cm(cx + R), Tiers.Cm(cz - R)), new Vec2(Tiers.Cm(cx + R), Tiers.Cm(cz + R)), new Vec2(Tiers.Cm(cx - R), Tiers.Cm(cz + R)) };
            if (k0 == 0) Setbacks.ApplyFootprint(b, square);
            else
            {
                // a setback's: its terrace doors, details, windows by hand and wall states were its old walls'
                b.details.RemoveAll(d => Derived.TierStart(b, d.k) == k0);
                b.windows.RemoveAll(w => Derived.TierStart(b, w.k) == k0);
                b.entrances.RemoveAll(e => Tiers.DoorOn(b, e, k0));
                Tiers.Corners(b, k0).Clear(); Tiers.MapEdges(b, k0, _ => new List<int>());
                var set = Outlines.Set(b, k0, square);
                if (set != OutlineIssue.None) return set;
            }
            if (CutAll(b, k0, CornerShape.Round, r) < 4)
            {
                // the rules' reason, from the first corner left sharp
                var (now, cuts) = Sharp(b, k0);
                int i = Enumerable.Range(0, now.Count).FirstOrDefault(j => cuts.All(c => c.i != j));
                var why = Cut(b, k0, i, CornerShape.Round, r, false);
                return why == OutlineIssue.None ? OutlineIssue.Shape : why;
            }
            if (k0 == 0) door = FrontDoor(b, new Vec2(cx, cz - R));
            return OutlineIssue.None;
        }

        /// <summary>
        /// The base's street door on the wall nearest <paramref name="front"/> where the standard doorway fits, as the generator
        /// lays it out (<see cref="Facade.Ops"/>: 0.3 m clear of each end of the wall's span). False where no wall is long
        /// enough: the door stays where it is.
        /// </summary>
        static bool FrontDoor(BuildingData b, Vec2 front)
        {
            var fp = b.footprint; int best = -1; double bd = double.MaxValue;
            bool Fits(int j) { var f = Details.EdgeFrameAt(b, 0, j); return f.uE - f.uS > Dim.DOOR_EXT + 0.6 + 1e-6; }
            for (int j = 0; j < fp.Count; j++)
            {
                if (!Fits(j)) continue;
                var a = fp[j]; var c = fp[(j + 1) % fp.Count];
                double d = Tiers.Hypot((a.x + c.x) / 2 - front.x, (a.z + c.z) / 2 - front.z);
                if (d < bd - 1e-6) { bd = d; best = j; }
            }
            var e = b.entrances.FirstOrDefault(x => x.k == 0);
            if (best < 0) return e != null && Fits(e.edge);
            if (e == null) b.entrances.Insert(0, e = new EntranceData());
            e.edge = best; e.t = 0.5;
            return true;
        }
    }
}
