#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// Facade details: AC units, vents, dishes, fire escapes and awnings on the outside of a wall
    /// (SPEC §4.4). Each is anchored to a storey and an outline edge, placed by hand
    /// (<c>BuildingData.details</c>) or by the style's rules (seeded per building, storey and edge, so
    /// nothing reshuffles when something else is edited). Geometry is built per storey with the host
    /// wall's cutaway data. LOD0 is the full item, LOD1 a simpler one. Never on a party wall.
    /// </summary>
    public static class Details
    {
        public enum Anchor { Wall, Tier, Opening }

        public sealed class Spec { public string label = ""; public double w, h, d; public Anchor anchor; }

        public static readonly Dictionary<DetailKind, Spec> Catalogue = new Dictionary<DetailKind, Spec>
        {
            [DetailKind.Ac] = new Spec { label = "AC unit", w = 0.75, h = 0.55, d = 0.45, anchor = Anchor.Wall },
            [DetailKind.Vent] = new Spec { label = "Vent", w = 0.5, h = 0.5, d = 0.16, anchor = Anchor.Wall },
            [DetailKind.Dish] = new Spec { label = "Dish", w = 0.6, h = 0.6, d = 0.4, anchor = Anchor.Wall },
            [DetailKind.Escape] = new Spec { label = "Fire escape", w = 2.6, h = 0, d = 1.1, anchor = Anchor.Tier },
            [DetailKind.Awning] = new Spec { label = "Awning", w = 0, h = 0.5, d = 1.0, anchor = Anchor.Opening },
        };

        /// <summary>FNV-1a over the decimal/ordinal text of the arguments, to 0..1, as the prototype's <c>hash01</c>.</summary>
        public static double Hash01(params object[] a)
        {
            uint h = 2166136261;
            foreach (var v in a)
            {
                string q = v is IFormattable f ? f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : v.ToString() ?? "";
                foreach (char c in q) { h ^= c; h = unchecked(h * 16777619); }
            }
            return (h % 10000) / 10000.0;
        }

        public static (double ac, double vents) Rules(FacadeStyle st) => ((st.details?.ac ?? 30) / 100, (st.details?.vents ?? 12) / 100);

        /// <summary>A detail resolved to metres along its edge on one storey.</summary>
        public sealed class Item
        {
            public DetailKind kind; public Spec spec = null!;
            public int i; public double u, y;
            /// <summary>Awnings: the opening's extent and top.</summary>
            public double u0, u1, yt; public bool hasSpan;
            /// <summary>Fire escapes: the anchor storey, the storey after the last one climbed, and whether this is the drop ladder below the first platform.</summary>
            public int ka, k1; public bool ladder;
            /// <summary>The hand-placed record this came from; null for a rule's item.</summary>
            public DetailData? man;
            public double Width => spec.w > 0 ? spec.w : hasSpan ? u1 - u0 : 1;
        }

        public struct EdgeFrame { public Geo.Edge e; public Miter m; public double uS, uE; }

        public static EdgeFrame EdgeFrameAt(BuildingData b, int k, int i)
        {
            var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0;
            var Wp = new List<Vec2>(fp.Count); foreach (var p in fp) Wp.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z));
            var e = Geo.EdgeInfo(Wp, i, ccw); var m = Geo.MiterOf(Geo.Corners(fp, ccw), i, e.L); var (uS, uE) = Facade.Span(m, e.L);
            return new EdgeFrame { e = e, m = m, uS = uS, uE = uE };
        }

        public struct Step { public int k, i; public double u; public double? s; }

        /// <summary>
        /// The storeys a fire escape anchored at (kA, edge i, u metres) climbs: up its own tier, then on
        /// through any setback whose outline has an edge on the same line covering the escape's width.
        /// </summary>
        public static List<Step> EscapeRun(BuildingData b, int kA, int i, double u)
        {
            int N = b.floors.Count; var fp0 = Derived.OutlineAt(b, kA); var a = fp0[i]; var c = fp0[(i + 1) % fp0.Count];
            double L0 = Geo.Hypot(c.x - a.x, c.z - a.z); if (L0 == 0) L0 = 1e-6; var d = new[] { (c.x - a.x) / L0, (c.z - a.z) / L0 };
            var run = new List<Step>(); int k = kA; var cur = new Step { i = i, u = u };
            while (k < N)
            {
                if (Derived.TierStart(b, k) != Derived.TierStart(b, kA) && k == Derived.TierStart(b, k))
                {
                    var fp = Derived.OutlineAt(b, k); double s = cur.s ?? u; Step? found = null;
                    for (int j = 0; j < fp.Count; j++)
                    {
                        var p = fp[j]; var q = fp[(j + 1) % fp.Count];
                        double Off(Vec2 v) => Math.Abs((v.x - a.x) * d[1] - (v.z - a.z) * d[0]);
                        double Along(Vec2 v) => (v.x - a.x) * d[0] + (v.z - a.z) * d[1];
                        if (Off(p) > 0.03 || Off(q) > 0.03) continue;
                        double t0 = Along(p), t1 = Along(q); if (t1 <= t0) continue;
                        if (t0 <= s - 1.4 && t1 >= s + 1.4) { found = new Step { i = j, u = s - t0, s = s }; break; }
                    }
                    if (found == null) break;
                    cur = found.Value;
                }
                run.Add(new Step { k = k, i = cur.i, u = cur.u }); k++;
                if (k < N && Derived.TierStart(b, k) == Derived.TierStart(b, k - 1)) continue;
            }
            if (run.Count == 0) run.Add(new Step { k = kA, i = i, u = u });
            return run;
        }

        /// <summary>Every detail on storey k, resolved to metres along its edge: the hand-placed ones, then the style's.</summary>
        public static List<Item> At(Site site, BuildingData b, int k)
        {
            int N = b.floors.Count; var out_ = new List<Item>(); if (k >= N) return out_;
            int k0 = Derived.TierStart(b, k); var fp = Derived.OutlineAt(b, k); int n = fp.Count; double h = Derived.FloorH(b, k);
            var R = Rules(Derived.StyleAt(b, k));
            bool OnParty(int i, double u) { foreach (var r in Party.Ranges(site, b, k0, i)) if (u > r.s - 0.3 && u < r.e + 0.3) return true; return false; }
            void Push(Item d)
            {
                if (OnParty(d.i, d.u)) return;
                if (d.man == null) foreach (var m in out_) if (m.man != null && m.i == d.i && Math.Abs(m.u - d.u) < (m.Width + d.Width) / 2 + 0.15) return;   // rules keep clear of hand-placed items
                out_.Add(d);
            }
            (EdgeFrame f, List<Opening> ops) OpsOf(int i) { var f = EdgeFrameAt(b, k, i); return (f, Facade.Ops(site, b, k, i, f.e.L, f.uS, f.uE)); }
            Item Mk(DetailKind kind, int i, double u, DetailData? man) => new Item { kind = kind, spec = Catalogue[kind], i = i, u = u, man = man };

            foreach (var d in b.details)
            {
                if (!Catalogue.TryGetValue(d.kind, out var spec) || d.edge >= n) continue;
                double L = Geo.EdgeLen(fp, d.edge), u = d.t * L;
                if (spec.anchor == Anchor.Tier)
                {
                    if (d.k >= N) continue;
                    var run = EscapeRun(b, d.k, d.edge, d.t * Geo.EdgeLen(Derived.OutlineAt(b, d.k), d.edge)); Step? at = null;
                    foreach (var r in run) if (r.k == k) { at = r; break; }
                    int k1 = run[run.Count - 1].k + 1;
                    if (at != null) { var it = Mk(d.kind, at.Value.i, at.Value.u, d); it.y = 0; it.ka = d.k; it.k1 = k1; Push(it); }
                    else if (d.kind == DetailKind.Escape && k == d.k - 1 && Derived.TierStart(b, d.k) == k0) { var it = Mk(DetailKind.Escape, d.edge, u, d); it.ladder = true; it.ka = d.k; it.k1 = k1; Push(it); }
                }
                else if (d.k != k) continue;
                else if (spec.anchor == Anchor.Opening)
                {
                    var (_, ops) = OpsOf(d.edge); Opening? best = null; double bestD = 0;
                    foreach (var o in ops) { double c = (o.u0 + o.u1) / 2, dd = Math.Abs(c - u); if (dd < Math.Max(0.6, (o.u1 - o.u0) / 2 + 0.3) && (best == null || dd < bestD)) { best = o; bestD = dd; } }
                    if (best != null) { var it = Mk(d.kind, d.edge, (best.u0 + best.u1) / 2, d); it.hasSpan = true; it.u0 = best.u0 - 0.25; it.u1 = best.u1 + 0.25; it.yt = best.y1 + 0.06; Push(it); }
                }
                else { var it = Mk(d.kind, d.edge, u, d); it.y = d.y ?? 0; Push(it); }
            }
            for (int i = 0; i < n; i++)
            {
                var aw = new List<Item>(); foreach (var d in out_) if (d.kind == DetailKind.Awning && d.i == i) aw.Add(d);
                aw.Sort((p, q) => p.u.CompareTo(q.u));
                for (int j = 1; j < aw.Count; j++) { var p = aw[j - 1]; var q = aw[j]; if (p.u1 > q.u0 - 0.04) { double m = (p.u1 + q.u0) / 2; p.u1 = m - 0.02; q.u0 = m + 0.02; } }
            }
            for (int i = 0; i < n; i++)
            {
                var (f, ops) = OpsOf(i); double uS = f.uS, uE = f.uE, L = f.e.L; if (L < 1.5) continue;
                if (R.ac > 0 && k > 0)
                    for (int j = 0; j < ops.Count; j++)
                    {
                        var o = ops[j]; if (o.door || o.full || Hash01(b.id, "ac", k, i, j) >= R.ac) continue;
                        double y = o.y0 - 0.62; if (y >= 0.25) { var it = Mk(DetailKind.Ac, i, (o.u0 + o.u1) / 2, null); it.y = y; Push(it); }
                    }
                if (R.vents > 0)
                {
                    var gaps = new List<(double, double)>(); double prev = uS;
                    foreach (var o in ops) { if (o.u0 - prev >= 1.0) gaps.Add((prev, o.u0)); prev = Math.Max(prev, o.u1); }
                    if (uE - prev >= 1.0) gaps.Add((prev, uE));
                    for (int j = 0; j < gaps.Count; j++)
                        if (Hash01(b.id, "vent", k, i, j) < R.vents) { var it = Mk(DetailKind.Vent, i, (gaps[j].Item1 + gaps[j].Item2) / 2, null); it.y = h - 0.95; Push(it); }
                }
            }
            return out_;
        }

        /// <summary>Geometry of storey k's details; lod 0 = full, 1 = simplified.</summary>
        public static void Build(MeshBuilder op, Site site, BuildingData b, int k, Palette C, int lod)
        {
            var items = At(site, b, k); if (items.Count == 0) return;
            var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0;
            var Wp = new List<Vec2>(fp.Count); foreach (var p in fp) Wp.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z));
            double y = Derived.FloorBase(b, k), h = Derived.FloorH(b, k), T = Dim.T_EXT;
            Rgb metal = Colors.Col("#C9CDCB"), grille = Colors.Col("#6E7476"), dark = Colors.Col("#5B5F5E"), rail = C.rail, canvas = C.trim, canvasIn = C.trim.Shade(0.8), disc = Colors.Col("#DDE0DE");
            foreach (var d in items)
            {
                var e = Geo.EdgeInfo(Wp, d.i, ccw); var F = e.F; var u = e.u; var w = e.w; op.Ctx(Geo.WallCtx(e.a, u, w, e.L), 1);
                P3 X(double uu, double yy, double ww) => F.At(uu, yy, ww);
                void Bx(double u0, double u1, double y0, double y1, double w0, double w1, Rgb c, Rgb? cIn = null, Skip sk = Skip.None) => op.OBox(F, u0, u1, y0, y1, w0, w1, c, cIn, sk);
                double uc = d.u;
                switch (d.kind)
                {
                    case DetailKind.Ac:
                    {
                        double yb = y + d.y; Bx(uc - 0.375, uc + 0.375, yb, yb + 0.55, T, T + 0.45, metal, null, Skip.In);
                        if (lod == 0) { Bx(uc - 0.3, uc + 0.3, yb + 0.08, yb + 0.47, T + 0.45, T + 0.47, grille, null, Skip.In); foreach (var q in new[] { -1, 1 }) Bx(uc + q * 0.31 - 0.02, uc + q * 0.31 + 0.02, yb - 0.06, yb, T, T + 0.42, dark, null, Skip.In | Skip.Top); }
                        break;
                    }
                    case DetailKind.Vent:
                    {
                        double yb = y + d.y; Bx(uc - 0.25, uc + 0.25, yb, yb + 0.5, T, T + 0.12, dark, null, Skip.In);
                        if (lod == 0) for (int j = 0; j < 4; j++) Bx(uc - 0.22, uc + 0.22, yb + 0.08 + j * 0.1, yb + 0.11 + j * 0.1, T + 0.12, T + 0.16, metal, null, Skip.In);
                        break;
                    }
                    case DetailKind.Dish:
                    {
                        double yb = y + d.y; var c0 = X(uc, yb + 0.3, T + 0.38); var nn = new P3(w.x * 0.8, 0.6, w.z * 0.8); var e1 = new P3(u.x, 0, u.z);
                        var e2 = P3.Cross(nn, e1);
                        P3[] Ring(double off) { var r = new P3[8]; for (int j = 0; j < 8; j++) { double an = j / 8.0 * Math.PI * 2, cx = Math.Cos(an) * 0.3, cz = Math.Sin(an) * 0.3; r[j] = c0 + e1 * cx + e2 * cz + nn * off; } return r; }
                        op.Poly(Ring(0.03), nn, disc); op.Poly(Ring(0), nn * -1, dark);
                        Bx(uc - 0.03, uc + 0.03, yb - 0.04, yb + 0.03, T, T + 0.33, dark, null, Skip.In); if (lod == 0) Bx(uc - 0.02, uc + 0.02, yb + 0.03, yb + 0.3, T + 0.3, T + 0.36, dark, null, Skip.Bot);
                        break;
                    }
                    case DetailKind.Awning:
                    {
                        double yt = y + d.yt, u0 = d.u0, u1 = d.u1; var nn = new P3(w.x * 0.371, 0.928, w.z * 0.371);
                        op.Poly(new[] { X(u0, yt + 0.45, T), X(u1, yt + 0.45, T), X(u1, yt + 0.05, T + 1.0), X(u0, yt + 0.05, T + 1.0) }, nn, canvas);
                        op.Poly(new[] { X(u0, yt + 0.41, T), X(u1, yt + 0.41, T), X(u1, yt + 0.01, T + 1.0), X(u0, yt + 0.01, T + 1.0) }, nn * -1, canvasIn);
                        Bx(u0, u1, yt - 0.15, yt + 0.05, T + 0.98, T + 1.02, canvas, null, Skip.UStart | Skip.UEnd);
                        if (lod == 0) foreach (var (uu, sg) in new[] { (u0, -1.0), (u1, 1.0) }) op.Poly(new[] { X(uu, yt + 0.45, T), X(uu, yt + 0.05, T + 1.0), X(uu, yt - 0.15, T + 1.0) }, new P3(u.x * sg, 0, u.z * sg), canvasIn);
                        break;
                    }
                    case DetailKind.Escape:
                    {
                        double u0 = uc - 1.3, u1 = uc + 1.3;
                        if (d.ladder)
                        {
                            double yt = y + h + 0.14; foreach (var q in new[] { -0.28, 0.22 }) Bx(uc + q, uc + q + 0.05, yt - 2.4, yt, T + 0.5, T + 0.56, dark);
                            if (lod == 0) for (int j = 0; j < 7; j++) Bx(uc - 0.23, uc + 0.22, yt - 2.2 + j * 0.3, yt - 2.16 + j * 0.3, T + 0.51, T + 0.55, dark, null, Skip.UStart | Skip.UEnd);
                            break;
                        }
                        double pt = y + 0.24, pb = pt - 0.08;
                        Bx(u0, u1, pb, pt, T, T + 1.1, rail, null, Skip.In);
                        Bx(u0, u1, pt + 0.9, pt + 0.95, T + 1.05, T + 1.1, rail);
                        if (lod == 0)
                        {
                            Bx(u0, u1, pt + 0.45, pt + 0.5, T + 1.05, T + 1.1, rail);
                            foreach (var uu in new[] { u0 + 0.03, uc, u1 - 0.03 }) Bx(uu - 0.025, uu + 0.025, pt, pt + 0.9, T + 1.055, T + 1.095, rail, null, Skip.Top | Skip.Bot);
                            foreach (var (ua, ub) in new[] { (u0, u0 + 0.05), (u1 - 0.05, u1) }) { Bx(ua, ub, pt + 0.9, pt + 0.95, T + 0.1, T + 1.05, rail, null, Skip.Out); Bx(ua + 0.005, ub - 0.005, pt, pt + 0.9, T + 0.12, T + 0.17, rail, null, Skip.Top | Skip.Bot); }
                        }
                        if (k + 1 < d.k1)
                        {
                            int dir = (k - d.ka) % 2 == 0 ? 1 : -1; double us = dir > 0 ? u0 + 0.2 : u1 - 0.2, ue = dir > 0 ? u1 - 0.4 : u0 + 0.4; int n = 9; double run = (ue - us) / n, rise = (h - 0.1) / n;
                            if (lod == 0)
                            {
                                for (int j = 0; j < n; j++) { double uA = us + run * j, uB = us + run * (j + 1), yt = pt + rise * (j + 1); Bx(Math.Min(uA, uB), Math.Max(uA, uB), yt - 0.05, yt, T + 0.15, T + 0.95, rail); }
                                foreach (var (ww, sg) in new[] { (T + 0.96, 1.0), (T + 0.14, -1.0) }) op.Poly(new[] { X(us, pt - 0.05, ww), X(ue, pt + h - 0.45, ww), X(ue, pt + h - 0.17, ww), X(us, pt + 0.23, ww) }, new P3(w.x * sg, 0, w.z * sg), dark);
                            }
                            else
                            {
                                double du = ue - us, dy = h - 0.14, ln = Geo.Hypot(du, dy), sgn = Math.Sign(du);
                                var nn = new P3(-u.x * dy / ln * sgn, Math.Abs(du) / ln, -u.z * dy / ln * sgn);
                                var q = new[] { X(us, pt + 0.02, T + 0.2), X(us, pt + 0.02, T + 0.9), X(ue, pt + h - 0.12, T + 0.9), X(ue, pt + h - 0.12, T + 0.2) };
                                var qb = new P3[4]; for (int j = 0; j < 4; j++) qb[j] = new P3(q[j].x, q[j].y - 0.06, q[j].z);
                                op.Poly(q, nn, rail); op.Poly(qb, nn * -1, dark);
                            }
                        }
                        break;
                    }
                }
            }
        }
    }
}
