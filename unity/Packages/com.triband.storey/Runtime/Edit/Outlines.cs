#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>Why an outline is refused (<see cref="Outlines.Issue"/>), with the prototype's wording.</summary>
    public enum OutlineIssue { None, Shape, Out, Up, Thin, Core }

    /// <summary>
    /// Outline editing: corners inserted, removed and merged with the doors, details and blank walls on the tier's
    /// edges kept in place; edges pushed; outlines checked; corners and buildings snapped. k0 is the tier being
    /// edited: 0 is the footprint, otherwise the floor where a setback starts. Ported from the prototype.
    /// </summary>
    public static class Outlines
    {
        public static string Why(OutlineIssue i) => i switch
        {
            OutlineIssue.Shape => "Edges can't cross or get shorter than 0.3 m",
            OutlineIssue.Out => "At least a quarter of a setback has to rest on the floor below",
            OutlineIssue.Up => "The setback above would lose its footing",
            OutlineIssue.Thin => "Line the edge up with the floor below, or leave at least 0.8 m of terrace",
            OutlineIssue.Core => "Stairs or a lift are in the way",
            _ => "",
        };

        /// <summary>
        /// Apply an edge remap to everything anchored to tier k0's edges, doors and details: <paramref name="fn"/>
        /// maps (edge, t) to a new place, or null to drop the item.
        /// </summary>
        static void RemapEdgeItems(BuildingData b, int k0, Func<int, double, (int edge, double t)?> fn)
        {
            var entrances = new List<EntranceData>();
            foreach (var e in b.entrances)
            {
                if (!Tiers.DoorOn(b, e, k0)) { entrances.Add(e); continue; }
                var r = fn(e.edge, e.t);
                if (r != null) { e.edge = r.Value.edge; e.t = r.Value.t; entrances.Add(e); }
            }
            b.entrances = entrances;
            var details = new List<DetailData>();
            foreach (var d in b.details)
            {
                if (Derived.TierStart(b, d.k) != k0) { details.Add(d); continue; }
                var r = fn(d.edge, d.t);
                if (r != null) { d.edge = r.Value.edge; d.t = r.Value.t; details.Add(d); }
            }
            b.details = details;
        }

        /// <summary>A corner in the middle of edge i; returns its index. Doors and details stay where they are on the wall.</summary>
        public static int InsertVertex(BuildingData b, int i, int k0 = 0)
        {
            var fp = Tiers.Outline(b, k0); var a = fp[i]; var c = fp[(i + 1) % fp.Count];
            fp.Insert(i + 1, new Vec2(Tiers.Cm((a.x + c.x) / 2), Tiers.Cm((a.z + c.z) / 2)));
            RemapEdgeItems(b, k0, (edge, t) => edge > i ? (edge + 1, t) : edge == i ? (t < 0.5 ? (i, t * 2) : (i + 1, (t - 0.5) * 2)) : (edge, t));
            Tiers.SetBlank(b, k0, Tiers.Blank(b, k0).SelectMany(j => j > i ? new[] { j + 1 } : j == i ? new[] { i, i + 1 } : new[] { j }).ToList());
            return i + 1;
        }

        /// <summary>Remove corner i (an outline keeps at least 3). Doors and details on its two edges go.</summary>
        public static bool RemoveVertex(BuildingData b, int i, int k0 = 0)
        {
            var fp = Tiers.Outline(b, k0); if (fp.Count <= 3) return false;
            int n = fp.Count, prev = (i - 1 + n) % n;
            fp.RemoveAt(i);
            int Remap(int j) => j > i ? j - 1 : j;
            RemapEdgeItems(b, k0, (edge, t) => edge == i || edge == prev ? null : ((int, double)?)(Remap(edge), t));
            Tiers.SetBlank(b, k0, Tiers.Blank(b, k0).Where(j => j != i).Select(Remap).Distinct().Where(j => j < fp.Count).ToList());
            return true;
        }

        /// <summary>Drop corner i where its two edges run on in a straight line (or it doubles a neighbour): doors keep their place.</summary>
        public static void MergeVertex(BuildingData b, int i, int k0)
        {
            var fp = Tiers.Outline(b, k0); int n = fp.Count, prev = (i - 1 + n) % n;
            double L1 = Tiers.EdgeLen(fp, prev), L2 = Tiers.EdgeLen(fp, i), LL = L1 + L2; if (LL == 0) LL = 1;
            int Ni(int j) => j > i ? j - 1 : j;
            int merged = Ni(prev);
            RemapEdgeItems(b, k0, (edge, t) => edge == prev ? (merged, t * L1 / LL) : edge == i ? (merged, (L1 + t * L2) / LL) : (Ni(edge), t));
            var bl = Tiers.Blank(b, k0);
            var nb = bl.Where(j => j != i && j != prev).Select(Ni).ToList();
            if (bl.Contains(i) || bl.Contains(prev)) nb.Add(merged);
            Tiers.SetBlank(b, k0, nb.Distinct().ToList());
            fp.RemoveAt(i);
        }

        /// <summary>Merge every corner that is straight or doubled, after a drag. True when anything changed.</summary>
        public static bool Simplify(BuildingData b, int k0)
        {
            var fp = Tiers.Outline(b, k0); bool changed = false, again = true;
            while (again && fp.Count > 3)
            {
                again = false;
                for (int i = 0; i < fp.Count; i++)
                {
                    int n = fp.Count; var a = fp[(i - 1 + n) % n]; var v = fp[i]; var c = fp[(i + 1) % n];
                    double cr = (v.x - a.x) * (c.z - v.z) - (v.z - a.z) * (c.x - v.x), La = Tiers.Hypot(v.x - a.x, v.z - a.z), Lc = Tiers.Hypot(c.x - v.x, c.z - v.z);
                    if (La < 0.01 || (Math.Abs(cr) < 0.01 * Math.Max(La * Lc, 1e-6) && (v.x - a.x) * (c.x - v.x) + (v.z - a.z) * (c.z - v.z) > 0))
                    {
                        MergeVertex(b, i, k0); changed = again = true; break;
                    }
                }
            }
            return changed;
        }

        /// <summary>
        /// Can tier k0 take this outline? It must stay a simple polygon, stand on the tier below, hold the tier above,
        /// leave a usable terrace wherever it steps in, and keep every core it serves inside it.
        /// </summary>
        public static OutlineIssue Issue(BuildingData b, int k0, List<Vec2> fp)
        {
            if (!Tiers.SimplePoly(fp)) return OutlineIssue.Shape;
            static bool Stands(List<Vec2> up, List<Vec2> lo) => Tiers.SharedArea(up, lo) >= Math.Max(4, 0.25 * Math.Abs(Geo.Area2(up)) / 2);   // a quarter of it, and 4 m², rests on the storey below
            if (k0 > 0)
            {
                var lo = Derived.OutlineAt(b, k0 - 1);
                if (!Stands(fp, lo)) return OutlineIssue.Out;
                if (!Tiers.TerraceClear(fp, lo)) return OutlineIssue.Thin;
            }
            int k1 = Tiers.End(b, k0), N = b.floors.Count;
            if (k1 < N)
            {
                var up = b.floors[k1].shape;
                if (!Stands(up, fp)) return OutlineIssue.Up;
                if (!Tiers.TerraceClear(up, fp)) return OutlineIssue.Thin;
            }
            foreach (var s in b.shafts)
            {
                if (!Cores.Levels(b, s).Any(k => k >= k0 && (k < k1 || (k == N && k1 == N)))) continue;
                if (!Cores.CoreFits(fp, s)) return OutlineIssue.Core;
            }
            return OutlineIssue.None;
        }

        /// <summary>Replace tier k0's outline if it passes <see cref="Issue"/>; returns the issue (None when replaced).</summary>
        public static OutlineIssue Set(BuildingData b, int k0, List<Vec2> fp)
        {
            var why = Issue(b, k0, fp);
            if (why != OutlineIssue.None) return why;
            var cur = Tiers.Outline(b, k0); cur.Clear(); cur.AddRange(fp);
            return OutlineIssue.None;
        }

        /// <summary>Edge i moved along its outward normal by d, the neighbouring edges keeping their directions.</summary>
        public static List<Vec2> PushEdge(List<Vec2> fp, int i, double d)
        {
            int n = fp.Count; var a = fp[i]; var c = fp[(i + 1) % n];
            double L = Tiers.Hypot(c.x - a.x, c.z - a.z); if (L == 0) L = 1;
            double sg = Geo.Area2(fp) > 0 ? 1 : -1, nx = sg * (c.z - a.z) / L, nz = -sg * (c.x - a.x) / L;
            double ox = nx * d, oz = nz * d, dx = c.x - a.x, dz = c.z - a.z;
            Vec2? Meet(Vec2 p0, Vec2 q)
            {
                double ex = q.x - p0.x, ez = q.z - p0.z, el = Tiers.Hypot(ex, ez); if (el == 0) el = 1;
                double den = dx * ez - dz * ex;
                if (Math.Abs(den) / (L * el) < 0.3) return null;
                double t = ((p0.x - (a.x + ox)) * ez - (p0.z - (a.z + oz)) * ex) / den;
                return new Vec2(Tiers.Cm(a.x + ox + dx * t), Tiers.Cm(a.z + oz + dz * t));
            }
            var o = Tiers.Copy(fp);
            o[i] = Meet(fp[(i - 1 + n) % n], a) ?? new Vec2(Tiers.Cm(a.x + ox), Tiers.Cm(a.z + oz));
            o[(i + 1) % n] = Meet(fp[(i + 2) % n], c) ?? new Vec2(Tiers.Cm(c.x + ox), Tiers.Cm(c.z + oz));
            return o;
        }

        /// <summary>Every edge moved inward by d, for quick wedding-cake towers.</summary>
        public static List<Vec2> Inset(List<Vec2> fp, double d)
        {
            var o = Tiers.Copy(fp);
            for (int i = 0; i < fp.Count; i++)
            {
                var moved = PushEdge(Tiers.Copy(o), i, -d);
                o[i] = moved[i]; o[(i + 1) % fp.Count] = moved[(i + 1) % fp.Count];
            }
            return o;
        }

        static IEnumerable<List<Vec2>> WorldFootprints(BuildingData b, IEnumerable<BuildingData> neighbours)
        {
            foreach (var p in neighbours)
            {
                if (ReferenceEquals(p, b)) continue;
                var w = new List<Vec2>(p.footprint.Count);
                foreach (var q in p.footprint) w.Add(new Vec2(q.x + p.pos.x, q.z + p.pos.z));
                yield return w;
            }
        }

        /// <summary>
        /// Where a dragged corner i lands: on a neighbouring building's corner or edge (the base outline), on the
        /// outline below (a setback), then in line with the neighbouring corners. Alt in the tools skips this.
        /// </summary>
        public static Vec2 Snap(BuildingData b, int k0, List<Vec2> fp, int i, double x, double z, IEnumerable<BuildingData> neighbours)
        {
            Vec2? best = null;
            if (k0 == 0)
            {
                double bd = 0.45, wx = x + b.pos.x, wz = z + b.pos.z;
                var ns = WorldFootprints(b, neighbours).ToList();
                foreach (var q in ns) foreach (var v in q)
                {
                    double d = Tiers.Hypot(v.x - wx, v.z - wz);
                    if (d < bd) { bd = d; best = new Vec2(Tiers.Cm(v.x - b.pos.x), Tiers.Cm(v.z - b.pos.z)); }
                }
                if (best != null) return best.Value;
                double ed = 0.35;
                foreach (var q in ns) for (int j = 0; j < q.Count; j++)
                {
                    var v = q[j]; var c = q[(j + 1) % q.Count]; var r = Tiers.SegDist(wx, wz, v.x, v.z, c.x, c.z);
                    if (r.d < ed) { ed = r.d; best = new Vec2(Tiers.Cm(r.cx - b.pos.x), Tiers.Cm(r.cz - b.pos.z)); }
                }
                if (best != null) return best.Value;
            }
            else
            {
                var lo = Derived.OutlineAt(b, k0 - 1); double bd = 0.45;
                foreach (var v in lo) { double d = Tiers.Hypot(v.x - x, v.z - z); if (d < bd) { bd = d; best = v; } }
                if (best != null) return best.Value;
                double ed = 0.35;
                for (int j = 0; j < lo.Count; j++)
                {
                    var v = lo[j]; var c = lo[(j + 1) % lo.Count]; var r = Tiers.SegDist(x, z, v.x, v.z, c.x, c.z);
                    if (r.d < ed) { ed = r.d; best = new Vec2(Tiers.Cm(r.cx), Tiers.Cm(r.cz)); }
                }
                if (best != null) return best.Value;
            }
            int n = fp.Count;
            foreach (int j in new[] { (i - 1 + n) % n, (i + 1) % n })
            {
                var q = fp[j];
                if (Math.Abs(q.x - x) < 0.4) x = q.x;
                if (Math.Abs(q.z - z) < 0.4) z = q.z;
            }
            return new Vec2(x, z);
        }

        /// <summary>
        /// Where a moved building lands: an edge that comes within 0.5 m of a neighbour's edge running the other way
        /// snaps onto it, making a party wall.
        /// </summary>
        public static Vec2 SnapMove(BuildingData b, double x, double z, IEnumerable<BuildingData> neighbours)
        {
            var fp = b.footprint; var W = fp.Select(q => new Vec2(q.x + x, q.z + z)).ToList();
            (double, double)? best = null; double bd = 0.5;
            foreach (var q in WorldFootprints(b, neighbours))
                for (int i = 0; i < W.Count; i++)
                {
                    var a = W[i]; var c = W[(i + 1) % W.Count];
                    double L = Tiers.Hypot(c.x - a.x, c.z - a.z); if (L == 0) L = 1;
                    double ux = (c.x - a.x) / L, uz = (c.z - a.z) / L;
                    for (int j = 0; j < q.Count; j++)
                    {
                        var p0 = q[j]; var p1 = q[(j + 1) % q.Count];
                        double M = Tiers.Hypot(p1.x - p0.x, p1.z - p0.z); if (M == 0) M = 1;
                        if (((p1.x - p0.x) * ux + (p1.z - p0.z) * uz) / M > -0.999) continue;
                        double d = (p0.x - a.x) * (-uz) + (p0.z - a.z) * ux, t0 = (p0.x - a.x) * ux + (p0.z - a.z) * uz, t1 = (p1.x - a.x) * ux + (p1.z - a.z) * uz;
                        if (Math.Min(L, Math.Max(t0, t1)) - Math.Max(0, Math.Min(t0, t1)) < 0.5 || Math.Abs(d) >= bd) continue;
                        bd = Math.Abs(d); best = (-uz * d, ux * d);
                    }
                }
            return best != null ? new Vec2(Tiers.Cm(x + best.Value.Item1), Tiers.Cm(z + best.Value.Item2)) : new Vec2(x, z);
        }
    }
}
