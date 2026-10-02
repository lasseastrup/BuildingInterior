#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>Why a courtyard or atrium is refused (<see cref="Voids.Issue"/>).</summary>
    public enum VoidIssue { None, Shape, Small, Outside, Core, Overlap }

    /// <summary>
    /// Courtyards and atria (<see cref="VoidData"/>): added, reshaped, moved and removed, only where they keep clear of
    /// every outline they pass through, the stairs and lifts, and each other. Storey's own.
    /// </summary>
    public static class Voids
    {
        /// <summary>The gap a void keeps from the outside walls (theirs, its own and a room's worth), and from cores and other voids.</summary>
        public const double WallGap = 1.5, CoreGap = 0.3;

        public static string Why(VoidIssue i) => i switch
        {
            VoidIssue.Shape => "Edges can’t cross or get shorter than 0.3 m",
            VoidIssue.Small => "A courtyard or atrium needs at least 2.5 × 2.5 m",
            VoidIssue.Outside => "Keep it 1.5 m inside the outline of every floor it goes through",
            VoidIssue.Core => "Stairs or a lift are in the way",
            VoidIssue.Overlap => "It runs into another courtyard or atrium",
            _ => "",
        };

        public static string NewId(BuildingData b)
        {
            int n = 1; while (b.voids.Any(v => v.id == "v" + n)) n++;
            return "v" + n;
        }

        public static VoidData? Of(BuildingData b, string id) => b.voids.FirstOrDefault(v => v.id == id);

        static PathD Path(List<Vec2> fp) { var p = new PathD(fp.Count); foreach (var v in fp) p.Add(new PointD(v.x, v.z)); return p; }
        static double Area(PathsD ps) { double a = 0; foreach (var p in ps) a += Clipper.Area(p); return Math.Abs(a); }

        /// <summary>A polygon lies inside an outline with <paramref name="gap"/> to spare.</summary>
        public static bool Inside(List<Vec2> shape, List<Vec2> outline, double gap)
        {
            var grown = Clipper.InflatePaths(new PathsD { Path(shape) }, gap, JoinType.Miter, EndType.Polygon, 4, 6);
            return Area(Clipper.Difference(grown, new PathsD { Path(outline) }, FillRule.NonZero, 6)) < 1e-4;
        }

        static bool Meets(List<Vec2> a, List<Vec2> c, double gap)
        {
            var grown = Clipper.InflatePaths(new PathsD { Path(a) }, gap, JoinType.Miter, EndType.Polygon, 4, 6);
            return Area(Clipper.Intersect(grown, new PathsD { Path(c) }, FillRule.NonZero, 6)) > 1e-4;
        }

        static List<Vec2> CoreRect(CoreData s)
        {
            var r = Cores.RectOf(s);
            return new List<Vec2>
            {
                new Vec2(r.c.x - r.u.x * r.hx - r.w.x * r.hz, r.c.z - r.u.z * r.hx - r.w.z * r.hz), new Vec2(r.c.x + r.u.x * r.hx - r.w.x * r.hz, r.c.z + r.u.z * r.hx - r.w.z * r.hz),
                new Vec2(r.c.x + r.u.x * r.hx + r.w.x * r.hz, r.c.z + r.u.z * r.hx + r.w.z * r.hz), new Vec2(r.c.x - r.u.x * r.hx + r.w.x * r.hz, r.c.z - r.u.z * r.hx + r.w.z * r.hz),
            };
        }

        /// <summary>The storeys a void takes room on: a courtyard from its bottom, an atrium above it, up to the top floor.</summary>
        public static IEnumerable<int> Storeys(BuildingData b, VoidKind kind, int bottom)
        {
            for (int k = kind == VoidKind.Courtyard ? bottom : bottom + 1; k < b.floors.Count; k++) yield return k;
        }

        /// <summary>A core in the way of a void taking room on these storeys.</summary>
        public static bool CoreInWay(BuildingData b, CoreData s, List<Vec2> shape, IEnumerable<int> storeys) =>
            Cores.Levels(b, s).Intersect(storeys).Any() && Meets(shape, CoreRect(s), CoreGap);

        /// <summary>
        /// Can building b take this void? A simple polygon of at least 2.5 × 2.5 m, 1.5 m inside the outline of every storey
        /// from its bottom up (and the roof's), clear of the cores on the storeys it opens, and of the other voids.
        /// </summary>
        public static VoidIssue Issue(BuildingData b, VoidData v, List<Vec2>? shape = null, int? bottom = null, VoidKind? kind = null)
        {
            var sh = shape ?? v.shape; int bt = bottom ?? v.bottom; var kd = kind ?? v.kind; int N = b.floors.Count;
            if (sh.Count < 3 || !Tiers.SimplePoly(sh)) return VoidIssue.Shape;
            var bb = Tiers.Bbox(sh);
            if (Math.Abs(Geo.Area2(sh)) / 2 < 6.25 || bb.x1 - bb.x0 < 2.5 || bb.z1 - bb.z0 < 2.5) return VoidIssue.Small;
            if (bt < 0 || bt >= N) return VoidIssue.Outside;
            for (int k = bt; k <= N; k++) if (!Inside(sh, Derived.OutlineAt(b, k), WallGap)) return VoidIssue.Outside;
            var storeys = Storeys(b, kd, bt).ToList();
            if (b.shafts.Any(s => CoreInWay(b, s, sh, storeys))) return VoidIssue.Core;
            if (b.voids.Any(o => !ReferenceEquals(o, v) && Meets(sh, o.shape, 1.0))) return VoidIssue.Overlap;
            return VoidIssue.None;
        }

        /// <summary>Every void of the building still fits an outline that tier k0 might take (for <see cref="Outlines.Issue"/>).</summary>
        public static bool FitOutline(BuildingData b, int k0, List<Vec2> fp)
        {
            int k1 = Tiers.End(b, k0), N = b.floors.Count;
            foreach (var v in b.voids)
            {
                if (!Courtyards.Live(b, v)) continue;
                // the tier's storeys the void passes through (every void reaches the roof, whose outline is the top tier's)
                bool passes = k1 == N || Math.Max(k0, v.bottom) < k1;
                if (passes && !Inside(v.shape, fp, WallGap)) return false;
            }
            return true;
        }

        /// <summary>A core standing on storeys k0 to k1 (where it would, if moved) keeps clear of every void open there.</summary>
        public static bool CoreClear(BuildingData b, CoreData s, int k0, int k1, double? x = null, double? z = null, double? rot = null)
        {
            if (b.voids.Count == 0) return true;
            var it = new CoreData { type = s.type, x = x ?? s.x, z = z ?? s.z, rot = rot ?? s.rot };
            var rect = CoreRect(it); var levels = Enumerable.Range(k0, Math.Max(0, k1 - k0 + 1)).ToList();
            foreach (var v in b.voids)
                if (Courtyards.Live(b, v) && levels.Intersect(Storeys(b, v.kind, v.bottom)).Any() && Meets(v.shape, rect, CoreGap)) return false;
            return true;
        }

        /// <summary>
        /// A new void on storey <paramref name="bottom"/> and up: a rectangle in the roomiest part of the outlines it passes
        /// through, as big as fits (up to 8 × 8 m). Null when nothing fits.
        /// </summary>
        public static VoidData? Add(BuildingData b, VoidKind kind, int bottom)
        {
            if (bottom < 0 || bottom >= b.floors.Count) return null;
            var fp = Derived.OutlineAt(b, b.floors.Count);   // the smallest: every void reaches the roof
            var bb = Tiers.Bbox(fp);
            // the point deepest inside every outline it passes through, on a 0.5 m grid
            Vec2? best = null; double bd = 0;
            for (double x = bb.x0; x <= bb.x1; x += 0.5)
                for (double z = bb.z0; z <= bb.z1; z += 0.5)
                {
                    double d = 1e9;
                    for (int k = bottom; k <= b.floors.Count; k++)
                    {
                        var o = Derived.OutlineAt(b, k);
                        d = Geo.Pip(o, x, z) ? Math.Min(d, Geo.DistToEdges(o, new Vec2(x, z))) : -1;
                        if (d < 0) break;
                    }
                    // the deepest point, and among equals the one nearest the middle
                    double mid = Geo.Hypot(x - (bb.x0 + bb.x1) / 2, z - (bb.z0 + bb.z1) / 2);
                    if (d > bd + 1e-9 || (best != null && Math.Abs(d - bd) <= 1e-9 && mid < Geo.Hypot(best.Value.x - (bb.x0 + bb.x1) / 2, best.Value.z - (bb.z0 + bb.z1) / 2))) { bd = d; best = new Vec2(x, z); }
                }
            if (best == null) return null;
            var v = new VoidData { id = NewId(b), kind = kind, bottom = bottom };
            for (double half = Math.Min(4, bd - WallGap); half >= 1.25; half -= 0.25)
            {
                var c = best.Value;
                var sh = new List<Vec2> { new Vec2(Tiers.Cm(c.x - half), Tiers.Cm(c.z - half)), new Vec2(Tiers.Cm(c.x + half), Tiers.Cm(c.z - half)), new Vec2(Tiers.Cm(c.x + half), Tiers.Cm(c.z + half)), new Vec2(Tiers.Cm(c.x - half), Tiers.Cm(c.z + half)) };
                if (Issue(b, v, sh) == VoidIssue.None) { v.shape = sh; b.voids.Add(v); return v; }
            }
            return null;
        }

        public static bool Remove(BuildingData b, string id) => b.voids.RemoveAll(v => v.id == id) > 0;

        /// <summary>Replace a void's outline if it fits; returns why not otherwise.</summary>
        public static VoidIssue SetShape(BuildingData b, string id, List<Vec2> shape)
        {
            var v = Of(b, id); if (v == null) return VoidIssue.Shape;
            var why = Issue(b, v, shape);
            if (why == VoidIssue.None) { v.shape = shape.Select(p => new Vec2(Tiers.Cm(p.x), Tiers.Cm(p.z))).ToList(); }
            return why;
        }

        /// <summary>Move a void's bottom to storey k if it fits there.</summary>
        public static VoidIssue SetBottom(BuildingData b, string id, int k)
        {
            var v = Of(b, id); if (v == null) return VoidIssue.Shape;
            var why = Issue(b, v, bottom: k);
            if (why == VoidIssue.None) v.bottom = k;
            return why;
        }

        /// <summary>Turn a courtyard into an atrium or back, if it fits as that.</summary>
        public static VoidIssue SetKind(BuildingData b, string id, VoidKind kind)
        {
            var v = Of(b, id); if (v == null) return VoidIssue.Shape;
            var why = Issue(b, v, kind: kind);
            if (why == VoidIssue.None) v.kind = kind;
            return why;
        }

        /// <summary>Move the whole void by (dx, dz) if it fits there.</summary>
        public static VoidIssue Move(BuildingData b, string id, double dx, double dz)
        {
            var v = Of(b, id); if (v == null) return VoidIssue.Shape;
            return SetShape(b, id, v.shape.Select(p => new Vec2(p.x + dx, p.z + dz)).ToList());
        }

        /// <summary>A corner in the middle of edge i; returns its index (it always fits: the outline is unchanged).</summary>
        public static int InsertVertex(BuildingData b, string id, int i)
        {
            var v = Of(b, id)!; var a = v.shape[i]; var c = v.shape[(i + 1) % v.shape.Count];
            v.shape.Insert(i + 1, new Vec2(Tiers.Cm((a.x + c.x) / 2), Tiers.Cm((a.z + c.z) / 2)));
            return i + 1;
        }

        /// <summary>Remove corner i (a void keeps at least 3) if what is left fits.</summary>
        public static bool RemoveVertex(BuildingData b, string id, int i)
        {
            var v = Of(b, id); if (v == null || v.shape.Count <= 3) return false;
            var sh = new List<Vec2>(v.shape); sh.RemoveAt(i);
            return SetShape(b, id, sh) == VoidIssue.None;
        }
    }
}
