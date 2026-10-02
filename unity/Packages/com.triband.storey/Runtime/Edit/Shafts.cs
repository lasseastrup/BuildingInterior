#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// Stairs and lifts (SPEC §2.2, §7): building-level cores with a floor range. Placed lined up with the nearest wall
    /// and pulled flush onto it, dragged, turned and aligned only where they fit every storey they serve and stay
    /// clear of the other cores. Ported from the prototype.
    /// </summary>
    public static class Shafts
    {
        /// <summary>A new core's id, from the document's counter (the prototype's <c>nid('s')</c>).</summary>
        public static string NewId(StoreyDocument d) => "s" + d.seq++;

        /// <summary>The core serving storey k under a point (15 cm grace), if any.</summary>
        public static CoreData? At(BuildingData b, int k, Vec2 p) =>
            b.shafts.FirstOrDefault(s => Cores.Levels(b, s).Contains(k) && Cores.Contains(s, p.x, p.z, 0.15));

        /// <summary>A core fits the outline of every storey from k0 to k1.</summary>
        public static bool FitsLevels(BuildingData b, CoreData it, int k0, int k1, double? x = null, double? z = null, double? rot = null)
        {
            for (int k = k0; k <= k1; k++) if (!Cores.CoreFits(Derived.OutlineAt(b, k), it, 0.16, x, z, rot)) return false;
            return true;
        }

        /// <summary>Two cores closer than the gap (separating-axis test on the oriented rectangles).</summary>
        public static bool Overlap(CoreData a, CoreData b, double gap = 0.3, double? bx = null, double? bz = null, double? brot = null)
        {
            var A = Cores.RectOf(a, gap / 2); var B = Cores.RectOf(b, gap / 2, bx, bz, brot);
            static double Ext(Cores.Rect Q, Vec2 ax) => Q.hx * Math.Abs(Q.u.x * ax.x + Q.u.z * ax.z) + Q.hz * Math.Abs(Q.w.x * ax.x + Q.w.z * ax.z);
            foreach (var ax in new[] { A.u, A.w, B.u, B.w })
            {
                double d = Math.Abs((B.c.x - A.c.x) * ax.x + (B.c.z - A.c.z) * ax.z);
                if (d >= Ext(A, ax) + Ext(B, ax)) return false;
            }
            return true;
        }

        /// <summary>The angle (degrees, 0 to 90) of the outline edge nearest a point, so a new core lines up with the walls.</summary>
        public static double WallAngle(List<Vec2> fp, double x, double z)
        {
            double? best = null; double bd = 1e9;
            for (int i = 0; i < fp.Count; i++)
            {
                var p = fp[i]; var q = fp[(i + 1) % fp.Count]; var r = Tiers.SegDist(x, z, p.x, p.z, q.x, q.z);
                if (r.d < bd) { bd = r.d; best = Math.Atan2(q.z - p.z, q.x - p.x) * 180 / Math.PI; }
            }
            return best == null ? 0 : Tiers.JsRound(((best.Value % 90) + 90) % 90 * 10) / 10;
        }

        /// <summary>Pull a core the last few centimetres onto an outline edge that runs along one of its sides, so it shares that wall.</summary>
        public static Vec2 Snap(List<Vec2> fp, CoreData it, double x, double z, double rot)
        {
            var R = Cores.RectOf(it, 0, x, z, rot);
            var n = new Vec2?[] { new Vec2(-R.u.x, -R.u.z), R.u, null, R.w }; var hh = new[] { R.hx, R.hx, 0, R.hz };
            double sx = x, sz = z;
            foreach (var sides in it.type == CoreType.Flight ? new[] { new[] { 0, 1 } } : new[] { new[] { 0, 1 }, new[] { 3 } })   // a flight's back is a way out
            {
                (double d, Vec2 nn)? best = null;
                foreach (int side in sides)
                {
                    var nn = n[side]!.Value; var mid = new Vec2(R.c.x + nn.x * hh[side], R.c.z + nn.z * hh[side]);
                    for (int i = 0; i < fp.Count; i++)
                    {
                        var a = fp[i]; var c = fp[(i + 1) % fp.Count];
                        double L = Tiers.Hypot(c.x - a.x, c.z - a.z); if (L == 0) L = 1e-6;
                        double dx = (c.x - a.x) / L, dz = (c.z - a.z) / L;
                        if (Math.Abs(dx * nn.x + dz * nn.z) > 0.02) continue;                  // the edge must run along the face
                        double t = (mid.x - a.x) * dx + (mid.z - a.z) * dz; if (t < -0.05 || t > L + 0.05) continue;
                        double d = (a.x - mid.x) * nn.x + (a.z - mid.z) * nn.z;                  // gap from the face out to the wall line
                        if (d > -0.05 && d < 0.45 && (best == null || Math.Abs(d) < Math.Abs(best.Value.d))) best = (d, nn);
                    }
                }
                if (best != null) { sx += best.Value.nn.x * best.Value.d; sz += best.Value.nn.z * best.Value.d; }
            }
            return new Vec2(Tiers.Fixed(sx, 3), Tiers.Fixed(sz, 3));
        }

        /// <summary>
        /// Where the Stairs or Lift tool would put a new core at p on storey k: lined up with the nearest wall (plus the
        /// tool's 90° turns), flush onto it when close, and whether it fits from k to the top floor clear of the other cores.
        /// </summary>
        public static (CoreData it, bool ok) Placement(BuildingData b, int k, Vec2 p, CoreType type, double placeRot)
        {
            double x = Tiers.Cm(p.x), z = Tiers.Cm(p.z);
            var it = new CoreData { type = type, x = x, z = z, rot = (WallAngle(Derived.OutlineAt(b, k), x, z) + placeRot) % 360 };
            int N = b.floors.Count;
            if (k < N)
            {
                var sn = Snap(Derived.OutlineAt(b, k), it, x, z, it.rot);
                if (FitsLevels(b, it, k, N - 1, sn.x, sn.z)) { it.x = sn.x; it.z = sn.z; }
            }
            bool ok = k < N && FitsLevels(b, it, k, N - 1);
            if (ok) ok = !b.shafts.Any(s => Overlap(s, it));
            return (it, ok);
        }

        /// <summary>
        /// The Stairs or Lift tool's click: the new core, from storey k to the top floor (stairs of either kind reach the
        /// roof), or null where it does not fit.
        /// </summary>
        public static CoreData? Place(BuildingData b, int k, Vec2 p, CoreType type, double placeRot, string id)
        {
            var (it, ok) = Placement(b, k, p, type, placeRot);
            if (!ok) return null;
            var s = new CoreData { id = id, type = it.type, x = it.x, z = it.z, rot = it.rot, bottom = k, top = -1, roof = Cores.IsStairs(it) };
            b.shafts.Add(s);
            return s;
        }

        /// <summary>
        /// One move of the Select tool's core drag: the core grabbed at <paramref name="grab"/> follows the pointer to
        /// <paramref name="p"/>, pulled flush onto a wall when close, and only where it fits. True when it moved.
        /// </summary>
        public static bool Drag(BuildingData b, CoreData it, Vec2 grab, Vec2 p)
        {
            double offX = it.x - grab.x, offZ = it.z - grab.z;
            double nx = Tiers.Cm(p.x + offX), nz = Tiers.Cm(p.z + offZ);
            int top = Derived.ShaftTop(b, it);
            bool Free(double x, double z) => FitsLevels(b, it, it.bottom, top, x, z) && !b.shafts.Any(s => !ReferenceEquals(s, it) && Overlap(s, it, 0.3, x, z));
            var sn = Snap(Derived.OutlineAt(b, it.bottom), it, nx, nz, it.rot);
            if (Free(sn.x, sn.z)) { nx = sn.x; nz = sn.z; }
            if (Free(nx, nz) && (nx != it.x || nz != it.z)) { it.x = nx; it.z = nz; return true; }
            return false;
        }

        /// <summary>Turn a core to an angle (degrees, kept to 0.1° in 0–360); false, unchanged, when there is no room.</summary>
        public static bool SetAngle(BuildingData b, CoreData sh, double deg)
        {
            double r = Tiers.JsRound((((deg % 360) + 360) % 360) * 10) / 10;
            var turned = new CoreData { id = sh.id, type = sh.type, x = sh.x, z = sh.z, rot = r, bottom = sh.bottom, top = sh.top, roof = sh.roof };
            if (!Tiers.ShaftFits(b, turned) || b.shafts.Any(x => !ReferenceEquals(x, sh) && Overlap(x, turned))) return false;
            sh.rot = r;
            return true;
        }

        /// <summary>Align to wall: the nearest edge's angle, keeping the core's quarter turn.</summary>
        public static bool Align(BuildingData b, CoreData sh)
        {
            double wa = WallAngle(Derived.OutlineAt(b, sh.bottom), sh.x, sh.z);
            return SetAngle(b, sh, wa + Tiers.JsRound((sh.rot - wa) / 90) * 90);
        }

        /// <summary>
        /// Switch stairs between switchback and straight flights, in place and over the same floors: false, unchanged,
        /// when the other kind has no room there.
        /// </summary>
        public static bool SetKind(BuildingData b, CoreData sh, CoreType type)
        {
            if (sh.type == type) return true;
            if (sh.type == CoreType.Lift || type == CoreType.Lift) return false;
            var other = new CoreData { id = sh.id, type = type, x = sh.x, z = sh.z, rot = sh.rot, bottom = sh.bottom, top = sh.top, roof = sh.roof };
            if (!Tiers.ShaftFits(b, other) || b.shafts.Any(x => !ReferenceEquals(x, sh) && Overlap(x, other))) return false;
            sh.type = type;
            return true;
        }

        /// <summary>The lowest floor a core serves; the top follows it up if needed.</summary>
        public static void SetBottom(CoreData sh, int v) { sh.bottom = v; if (sh.top >= 0 && sh.top < v) sh.top = v; }

        /// <summary>The highest floor a core serves (-1 = follow the top floor); the bottom follows it down if needed.</summary>
        public static void SetTop(CoreData sh, int v) { sh.top = v; if (v >= 0 && v < sh.bottom) sh.bottom = v; }
    }
}
