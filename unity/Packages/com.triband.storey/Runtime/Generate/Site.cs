#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// The buildings a generator run can see: a building's neighbours decide its party walls and
    /// where its upper doors may open. Each building gets the small integer index the GPU
    /// building table uses (SPEC §6.4); a site holds the party-wall memo cleared per building.
    /// </summary>
    public sealed class Site
    {
        public readonly List<BuildingData> Buildings;
        readonly Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly Dictionary<string, BuildingData> byId = new Dictionary<string, BuildingData>(StringComparer.Ordinal);
        readonly Dictionary<(int, int), List<BuildingData>> grid = new Dictionary<(int, int), List<BuildingData>>();
        internal readonly Dictionary<string, Dictionary<string, List<Party.Range>>> partyMemo = new Dictionary<string, Dictionary<string, List<Party.Range>>>(StringComparer.Ordinal);
        const double BGRID = 32;

        public Site(List<BuildingData> buildings)
        {
            Buildings = buildings;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                index[b.id] = i; byId[b.id] = b;
                var bb = BoundsOf(b);
                for (int gx = (int)Math.Floor(bb.x0 / BGRID); gx <= (int)Math.Floor(bb.x1 / BGRID); gx++)
                    for (int gz = (int)Math.Floor(bb.z0 / BGRID); gz <= (int)Math.Floor(bb.z1 / BGRID); gz++)
                    {
                        if (!grid.TryGetValue((gx, gz), out var l)) grid[(gx, gz)] = l = new List<BuildingData>();
                        l.Add(b);
                    }
            }
        }

        public int IndexOf(BuildingData b) => index[b.id];
        public BuildingData? ById(string id) => byId.TryGetValue(id, out var b) ? b : null;

        /// <summary>World bounding box of every outline of a building.</summary>
        public static (double x0, double z0, double x1, double z1) BoundsOf(BuildingData b)
        {
            double x0 = 1e9, z0 = 1e9, x1 = -1e9, z1 = -1e9;
            for (int k = 0; k <= b.floors.Count; k++)
                foreach (var p in Derived.OutlineAt(b, k)) { x0 = Math.Min(x0, p.x); z0 = Math.Min(z0, p.z); x1 = Math.Max(x1, p.x); z1 = Math.Max(z1, p.z); }
            return (x0 + b.pos.x, z0 + b.pos.z, x1 + b.pos.x, z1 + b.pos.z);
        }

        /// <summary>Buildings whose grid cells touch a circle (a superset of the ones within it).</summary>
        public HashSet<BuildingData> Near(double x, double z, double rad)
        {
            var out_ = new HashSet<BuildingData>();
            for (int gx = (int)Math.Floor((x - rad) / BGRID); gx <= (int)Math.Floor((x + rad) / BGRID); gx++)
                for (int gz = (int)Math.Floor((z - rad) / BGRID); gz <= (int)Math.Floor((z + rad) / BGRID); gz++)
                    if (grid.TryGetValue((gx, gz), out var l)) foreach (var b in l) out_.Add(b);
            return out_;
        }
    }

    /// <summary>
    /// Shared (party) walls: an outline edge that runs along a neighbour's edge the other way round
    /// (within 1 cm) is one wall for both. The taller building along it owns the wall (ties: the lower
    /// id) and builds it blank wherever the neighbour reaches; the other leaves its wall out.
    /// </summary>
    public static class Party
    {
        public sealed class Range
        {
            public double s, e, sRaw, eRaw, Hp;
            public bool own;
            public int pIdx;
            public Swatch pInner;
        }

        /// <summary>How high b's outlines run along world segment a–c, counting tiers up from the base.</summary>
        public static double CoverH(BuildingData b, double ax, double az, double cx, double cz)
        {
            double mx = (ax + cx) / 2 - b.pos.x, mz = (az + cz) / 2 - b.pos.z, dx = cx - ax, dz = cz - az, L = Geo.Hypot(dx, dz); if (L == 0) L = 1;
            double H = 0;
            foreach (var t in Tiers(b))
            {
                var fp = Derived.OutlineAt(b, t.k0); bool on = false;
                for (int i = 0; i < fp.Count && !on; i++)
                {
                    var p = fp[i]; var q = fp[(i + 1) % fp.Count]; double ex = q.x - p.x, ez = q.z - p.z;
                    if (Math.Abs(ex * dz - ez * dx) > 0.01 * L * Geo.Hypot(ex, ez)) continue;
                    if (Geo.SegDist(mx, mz, p.x, p.z, q.x, q.z).d < 0.01) on = true;
                }
                if (!on) break;
                H = Derived.FloorBase(b, t.k1);
            }
            return H;
        }

        public static List<(int k0, int k1)> Tiers(BuildingData b)
        {
            var t = new List<int> { 0 };
            for (int j = 1; j < b.floors.Count; j++) if (b.floors[j].HasShape) t.Add(j);
            var out_ = new List<(int, int)>();
            for (int i = 0; i < t.Count; i++) out_.Add((t[i], i + 1 < t.Count ? t[i + 1] : b.floors.Count));
            return out_;
        }

        public static int TierEnd(BuildingData b, int k0)
        {
            for (int j = k0 + 1; j < b.floors.Count; j++) if (b.floors[j].HasShape) return j;
            return b.floors.Count;
        }

        /// <summary>Party ranges along tier k0's edge i, sorted by start.</summary>
        public static List<Range> Ranges(Site site, BuildingData b, int k0, int i)
        {
            if (!site.partyMemo.TryGetValue(b.id, out var m)) site.partyMemo[b.id] = m = new Dictionary<string, List<Range>>(StringComparer.Ordinal);
            string key = k0 + ":" + i;
            if (m.TryGetValue(key, out var hit)) return hit;

            var fp = Derived.OutlineAt(b, k0); var a = fp[i]; var c = fp[(i + 1) % fp.Count];
            double ax = a.x + b.pos.x, az = a.z + b.pos.z, cx = c.x + b.pos.x, cz = c.z + b.pos.z, L = Geo.Hypot(cx - ax, cz - az); if (L == 0) L = 1;
            double ux = (cx - ax) / L, uz = (cz - az) / L;
            var found = new Dictionary<string, (BuildingData p, double s, double e, double sRaw, double eRaw)>(StringComparer.Ordinal);
            var order = new List<string>();
            foreach (var p in site.Near((ax + cx) / 2, (az + cz) / 2, L / 2 + 1))
            {
                if (ReferenceEquals(p, b)) continue;
                foreach (var t in Tiers(p))
                {
                    var q = Derived.OutlineAt(p, t.k0);
                    for (int j = 0; j < q.Count; j++)
                    {
                        var P0 = q[j]; var Q0 = q[(j + 1) % q.Count];
                        double px = P0.x + p.pos.x, pz = P0.z + p.pos.z, qx = Q0.x + p.pos.x, qz = Q0.z + p.pos.z, M = Geo.Hypot(qx - px, qz - pz); if (M == 0) M = 1;
                        if (((qx - px) * ux + (qz - pz) * uz) / M > -0.9999) continue;
                        if (Math.Abs((px - ax) * uz - (pz - az) * ux) > 0.01 || Math.Abs((qx - ax) * uz - (qz - az) * ux) > 0.01) continue;
                        double t0 = (px - ax) * ux + (pz - az) * uz, t1 = (qx - ax) * ux + (qz - az) * uz;
                        double s0 = Math.Max(0, Math.Min(t0, t1)), e0 = Math.Min(L, Math.Max(t0, t1)); if (e0 - s0 < 0.1) continue;
                        if (found.TryGetValue(p.id, out var f))
                            found[p.id] = (p, Math.Min(f.s, s0), Math.Max(f.e, e0), Math.Min(f.sRaw, Math.Min(t0, t1)), Math.Max(f.eRaw, Math.Max(t0, t1)));
                        else { found[p.id] = (p, s0, e0, Math.Min(t0, t1), Math.Max(t0, t1)); order.Add(p.id); }
                    }
                }
            }
            var out_ = new List<Range>();
            foreach (var id in order)
            {
                var (p, s0, e0, sRaw, eRaw) = found[id];
                double sx = ax + ux * s0, sz = az + uz * s0, ex = ax + ux * e0, ez = az + uz * e0;
                double Hm = CoverH(b, sx, sz, ex, ez), Hp = CoverH(p, sx, sz, ex, ez);
                out_.Add(new Range
                {
                    s = s0, e = e0, sRaw = sRaw, eRaw = eRaw, Hp = Hp,
                    own = Hm > Hp + 1e-3 || (Math.Abs(Hm - Hp) <= 1e-3 && string.CompareOrdinal(b.id, p.id) < 0),
                    pIdx = site.IndexOf(p), pInner = new Swatch(new StyleRef(site.IndexOf(p), 0), ColorSlot.Interior),
                });
            }
            out_.Sort((x, y) => x.s.CompareTo(y.s));
            m[key] = out_;
            return out_;
        }

        /// <summary>Ranges along tier k0's edge i that belong to a neighbour's wall.</summary>
        public static List<(double, double)> Skips(Site site, BuildingData b, int k0, int i)
        {
            var out_ = new List<(double, double)>();
            foreach (var r in Ranges(site, b, k0, i)) if (!r.own) out_.Add((r.s, r.e));
            return out_;
        }

        /// <summary>
        /// At a corner next to a party wall, a wall stops against the face of the wall holding the
        /// corner instead of mitring past the line. Null for the usual mitre.
        /// </summary>
        public static Cut? CornerCut(Site site, BuildingData b, int k0, int i, bool end, double y, bool strip = false)
        {
            var fp = Derived.OutlineAt(b, k0); int n = fp.Count, j = end ? (i + 1) % n : (i - 1 + n) % n;
            double Lj = Geo.EdgeLen(fp, j);
            Range? r = null;
            foreach (var x in Ranges(site, b, k0, j)) if (end ? x.s < 0.02 : x.e > Lj - 0.02) { r = x; break; }
            if (r == null) return null;
            bool past = end ? r.sRaw < -0.02 : r.eRaw > Lj + 0.02;
            double off = Dim.T_EXT + (strip && past ? 0.06 : 0);
            if (r.own && !(past && y < r.Hp + 0.01)) { if (!strip || y >= r.Hp) return null; off = -Dim.T_EXT; }
            int sg = Geo.Area2(fp) > 0 ? 1 : -1; var a = fp[i]; var c = fp[(i + 1) % n]; double L = Geo.EdgeLen(fp, i);
            double Ux = (c.x - a.x) / L, Uz = (c.z - a.z) / L, Wx = sg * Uz, Wz = -sg * Ux;
            var p = fp[j]; var q = fp[(j + 1) % n]; double M = Geo.EdgeLen(fp, j);
            double nqx = -sg * (q.z - p.z) / M, nqz = sg * (q.x - p.x) / M;
            double un = Ux * nqx + Uz * nqz, wn = Wx * nqx + Wz * nqz; if (Math.Abs(un) < 0.2) return null;
            return new Cut((end ? L : 0) + off / un, -wn / un);
        }

        public enum PieceKind { Wall, Party, Skip }

        /// <summary>A stretch of a storey's wall along an edge: facade, a party wall we own, or the neighbour's.</summary>
        public sealed class Piece
        {
            public PieceKind kind; public int i; public double s, e, lo, hi; public Cut S, E; public Range? r;
        }

        public static List<Piece> WallPieces(Site site, BuildingData b, int k, int i, double L, Miter m)
        {
            double y = Derived.FloorBase(b, k); int k0 = Derived.TierStart(b, k);
            var cuts = new List<Range>();
            foreach (var r in Ranges(site, b, k0, i)) if (!r.own || y < r.Hp - 0.2) cuts.Add(r);
            var c0 = CornerCut(site, b, k0, i, false, y); var c1 = CornerCut(site, b, k0, i, true, y);
            Piece Mk(PieceKind kind, double s0, double e0, Range? r) => new Piece
            {
                kind = kind, i = i, s = s0, e = e0, r = r,
                S = s0 < 0.02 ? (c0 ?? m.S) : new Cut(s0, 0), E = e0 > L - 0.02 ? (c1 ?? m.E) : new Cut(e0, 0),
                lo = s0 < 0.02 ? (c0 != null ? Math.Max(c0.Value.s, c0.Value.At(Dim.T_EXT)) + 0.14 : -1e9) : s0 + 0.14,
                hi = e0 > L - 0.02 ? (c1 != null ? Math.Min(c1.Value.s, c1.Value.At(Dim.T_EXT)) - 0.14 : 1e9) : e0 - 0.14,
            };
            var out_ = new List<Piece>();
            if (cuts.Count == 0) { out_.Add(Mk(PieceKind.Wall, 0, L, null)); return out_; }
            double pos = 0;
            foreach (var r in cuts)
            {
                double s0 = Math.Max(pos, r.s); if (s0 >= r.e) continue;
                if (s0 > pos + 0.02) out_.Add(Mk(PieceKind.Wall, pos, s0, null));
                out_.Add(Mk(r.own ? PieceKind.Party : PieceKind.Skip, s0, r.e, r));
                pos = r.e;
            }
            if (pos < L - 0.02) out_.Add(Mk(PieceKind.Wall, pos, L, null));
            return out_;
        }
    }
}
