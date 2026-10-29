#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>
    /// Facade details: AC units, vents, dishes, fire escapes and awnings on the outside of a wall
    /// (SPEC §4.4), and the kinds artists make (<see cref="DetailKinds"/>). Each is anchored to a storey
    /// and an outline edge, placed by hand (<c>BuildingData.details</c>) or by the style's rules (seeded
    /// per building, storey and edge, so nothing reshuffles when something else is edited). Geometry is
    /// built per storey with the host wall's cutaway data. LOD0 is the full item, LOD1 a simpler one.
    /// Never on a party wall.
    /// </summary>
    public static class Details
    {
        /// <summary>Where a kind goes: on a wall at a height, up the wall from a storey (a fire escape), or over a ground-floor opening (an awning).</summary>
        public enum Anchor { Wall, Tier, Opening }

        /// <summary>
        /// Where a style's rules put a kind: under upper-floor windows (as AC units), in the stretches of wall between
        /// windows (as vents), or over ground-floor windows (as awnings over shop windows; a door's is put by hand). The
        /// first two are for kinds on a wall, the third for kinds over an opening. Then Storey's own, for kinds on a wall
        /// (docs/EDITOR.md §6.12): scattered over the walls (as brick patches), in a row along the top of each tier's wall or
        /// along the foot of the ground floor's (so many to a bay), and stacked up the building's corners.
        /// </summary>
        public enum Rule { None, UnderWindows, BareWall, OverOpenings, Scatter, AlongTop, AlongFoot, Corners }

        /// <summary>
        /// How far above an opening's top an artist's detail over it has its pivot: clear of a window's head (6 cm, or an
        /// arched hood's 17 cm over a usual window).
        /// </summary>
        public const double OverLift = 0.25;

        /// <summary>Whether a kind has a rule, and it fits where the kind goes.</summary>
        public static bool RuleFits(DetailKind k) => RuleFits(k.anchor, k.rule);

        public static bool RuleFits(Anchor a, Rule r) => r == Rule.OverOpenings ? a == Anchor.Opening : r != Rule.None && a == Anchor.Wall;

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

        /// <summary><see cref="Hash01(object[])"/> of two strings and three integers, without making their text.</summary>
        public static double Hash01(string a, string b, int k, int i, int j)
        {
            uint h = Feed(Feed(Feed(Feed(Feed(2166136261, a), b), k), i), j);
            return (h % 10000) / 10000.0;
        }

        static uint Feed(uint h, char c) => unchecked((h ^ c) * 16777619);
        static uint Feed(uint h, string s) { foreach (char c in s) h = Feed(h, c); return h; }
        // the decimal digits of v as its invariant text has them
        static uint Feed(uint h, int v)
        {
            long x = v;
            if (x < 0) { h = Feed(h, '-'); x = -x; }
            long p = 1; while (p * 10 <= x) p *= 10;
            for (; p > 0; p /= 10) h = Feed(h, (char)('0' + x / p % 10));
            return h;
        }

        public static (double ac, double vents) Rules(FacadeStyle st) => ((st.details?.ac ?? 30) / 100, (st.details?.vents ?? 12) / 100);

        /// <summary>The chance, 0 to 1, a style's rules give an artist's kind; 0 when the style doesn't name it.</summary>
        public static double Density(FacadeStyle st, string id) => st.details?.kinds != null && st.details.kinds.TryGetValue(id, out var p) ? p / 100 : 0;

        /// <summary>A style's settings for an artist's kind beyond its chance; null when it has none (the rule's own then). Storey's own.</summary>
        public static KindRule? RuleOf(FacadeStyle st, string id) => st.details?.rules != null && st.details.rules.TryGetValue(id, out var r) ? r : null;

        /// <summary>Scattered: how many to the square metre at 100 %, and the gap kept round each (<see cref="KindRule"/>'s defaults).</summary>
        public static (double density, double gap) ScatterOf(KindRule? r) => (Math.Max(0, r?.density ?? 0.5), Math.Max(0, r?.spacing ?? 0.1));

        /// <summary>Up the corners: how far apart, at least 10 cm (1 m by default).</summary>
        public static double CornerStep(KindRule? r) => Math.Max(0.1, r?.spacing ?? 1.0);

        /// <summary>Along the top or the foot of the wall: how many to a bay, 1 to 16.</summary>
        public static int PerBay(KindRule? r) => Math.Min(16, Math.Max(1, r?.per ?? 1));

        /// <summary>Whether a kind by rule goes on storey k, by its <see cref="KindRule.floors"/>.</summary>
        public static bool OnFloor(KindRule? r, int k) => (r?.floors ?? DetailFloors.All) switch { DetailFloors.Upper => k > 0, DetailFloors.Ground => k == 0, _ => true };

        /// <summary>A detail resolved to metres along its edge on one storey.</summary>
        public sealed class Item
        {
            public DetailKind kind = null!;
            /// <summary>Its edge, its middle along it, and how high its pivot is above the floor (over an opening: <see cref="OverLift"/> above the opening's top).</summary>
            public int i; public double u, y;
            /// <summary>Over an opening: the opening's extent, 25 cm past it each side, and the awning's top.</summary>
            public double u0, u1, yt; public bool hasSpan;
            /// <summary>Up the wall: the anchor storey, the storey after the last one climbed, and whether this is a fire escape's drop ladder below its first platform.</summary>
            public int ka, k1; public bool ladder;
            /// <summary>The hand-placed record this came from; null for a rule's item.</summary>
            public DetailData? man;
            /// <summary>Up a corner (<see cref="Rule.Corners"/>): at the corner where edge <see cref="i"/> starts, on the outside of its walls, facing out along the corner's bisector.</summary>
            public bool corner;
            /// <summary>
            /// Up a corner of another angle than 90°, bent to fit it (<see cref="DetailKind.cornerBend"/>): each half turned by this
            /// (radians, the left half this way) about the corner's edge, and a point on the bend this far out along the
            /// bisector per metre it was (<see cref="bendOut"/>). 0 and 1: as made.
            /// </summary>
            public double bend, bendOut = 1;
            /// <summary>As wide as its opening's span (an awning, or an artist's kind fitted across).</summary>
            public bool Spans => hasSpan && (kind.stretch || kind.w <= 0);
            public double Width => Spans ? u1 - u0 : kind.w > 0 ? kind.w : 1;
        }

        public struct EdgeFrame { public Geo.Edge e; public Miter m; public double uS, uE; }

        public static EdgeFrame EdgeFrameAt(BuildingData b, int k, int i)
        {
            var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0;
            return EdgeFrameAt(SiteOutline(b, fp), ccw, Geo.Corners(fp, ccw), i);
        }

        /// <summary>Edge i's frame from its storey's outline in site space and corners, worked out once for all its edges.</summary>
        static EdgeFrame EdgeFrameAt(List<Vec2> Wp, bool ccw, Geo.Corner[] cor, int i)
        {
            var e = Geo.EdgeInfo(Wp, i, ccw); var m = Geo.MiterOf(cor, i, e.L); var (uS, uE) = Facade.Span(m, e.L);
            return new EdgeFrame { e = e, m = m, uS = uS, uE = uE };
        }

        static List<Vec2> SiteOutline(BuildingData b, List<Vec2> fp)
        {
            var Wp = new List<Vec2>(fp.Count); foreach (var p in fp) Wp.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z));
            return Wp;
        }

        public struct Step { public int k, i; public double u; public double? s; }

        /// <summary>
        /// The storeys a fire escape anchored at (kA, edge i, u metres) climbs: up its own tier, then on
        /// through any setback whose outline has an edge on the same line covering the escape's width
        /// (<paramref name="half"/> each side of it: 1.4 m for the fire escape).
        /// </summary>
        public static List<Step> EscapeRun(BuildingData b, int kA, int i, double u, double half = 1.4)
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
                        if (t0 <= s - half && t1 >= s + half) { found = new Step { i = j, u = s - t0, s = s }; break; }
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

        /// <summary>How much flush wall a kind going up the wall needs each side of it, in setbacks it climbs into.</summary>
        public static double RunHalf(DetailKind k) => k.id == DetailKinds.Escape ? 1.4 : Math.Max(0.1, k.w / 2 + 0.1);

        /// <summary>Every detail on storey k, resolved to metres along its edge: the hand-placed ones, then the style's.</summary>
        public static List<Item> At(Site site, BuildingData b, int k)
        {
            int N = b.floors.Count; var out_ = new List<Item>(); if (k >= N) return out_;
            int k0 = Derived.TierStart(b, k); var fp = Derived.OutlineAt(b, k); int n = fp.Count; double h = Derived.FloorH(b, k);
            var st = Derived.StyleAt(b, k); var R = Rules(st);
            bool OnParty(int i, double u) { foreach (var r in Party.Ranges(site, b, k0, i)) if (u > r.s - 0.3 && u < r.e + 0.3) return true; return false; }
            void Push(Item d)
            {
                if (OnParty(d.i, d.u)) return;
                if (d.man == null) foreach (var m in out_) if (m.man != null && m.i == d.i && Math.Abs(m.u - d.u) < (m.Width + d.Width) / 2 + 0.15) return;   // rules keep clear of hand-placed items
                out_.Add(d);
            }
            // one edge's openings at a time, each let go before the next edge's are made; the outline's frames from its
            // points and corners worked out once
            var opsList = new List<Opening>(); var opsPool = new OpeningPool();
            var ac = DetailKinds.Get(DetailKinds.Ac)!; var vent = DetailKinds.Get(DetailKinds.Vent)!;
            bool ccw = Geo.Area2(fp) > 0; var Wp = SiteOutline(b, fp); var cor = Geo.Corners(fp, ccw);
            (EdgeFrame f, List<Opening> ops) OpsOf(int i) { var f = EdgeFrameAt(Wp, ccw, cor, i); opsPool.Reset(); return (f, Facade.Ops(site, b, k, i, f.e.L, f.uS, f.uE, opsList, opsPool)); }
            Item Mk(DetailKind kind, int i, double u, DetailData? man) => new Item { kind = kind, i = i, u = u, man = man };
            Item Over(DetailKind kind, int i, Opening o, DetailData? man)
            {
                var it = Mk(kind, i, (o.u0 + o.u1) / 2, man); it.hasSpan = true; (it.u0, it.u1) = OverSpan(o); it.yt = o.Top + 0.06; it.y = o.Top + OverLift;
                return it;
            }
            // neighbouring spans over openings meet in the middle
            void Trim()
            {
                for (int i = 0; i < n; i++)
                {
                    var aw = new List<Item>(); foreach (var d in out_) if (d.Spans && d.i == i) aw.Add(d);
                    aw.Sort((p, q) => p.u.CompareTo(q.u));
                    for (int j = 1; j < aw.Count; j++) { var p = aw[j - 1]; var q = aw[j]; if (p.u1 > q.u0 - 0.04) { double m = (p.u1 + q.u0) / 2; p.u1 = m - 0.02; q.u0 = m + 0.02; } }
                }
            }

            foreach (var d in b.details)
            {
                var kind = DetailKinds.Get(d.kind); if (kind == null || d.edge >= n) continue;
                double L = Geo.EdgeLen(fp, d.edge), u = d.t * L;
                if (kind.anchor == Anchor.Tier)
                {
                    if (d.k >= N) continue;
                    var run = EscapeRun(b, d.k, d.edge, d.t * Geo.EdgeLen(Derived.OutlineAt(b, d.k), d.edge), RunHalf(kind)); Step? at = null;
                    foreach (var r in run) if (r.k == k) { at = r; break; }
                    int k1 = run[run.Count - 1].k + 1;
                    if (at != null) { var it = Mk(kind, at.Value.i, at.Value.u, d); it.y = 0; it.ka = d.k; it.k1 = k1; Push(it); }
                    else if (kind.id == DetailKinds.Escape && k == d.k - 1 && Derived.TierStart(b, d.k) == k0) { var it = Mk(kind, d.edge, u, d); it.ladder = true; it.ka = d.k; it.k1 = k1; Push(it); }
                }
                else if (d.k != k) continue;
                else if (kind.anchor == Anchor.Opening)
                {
                    var (_, ops) = OpsOf(d.edge); Opening? best = null; double bestD = 0;
                    foreach (var o in ops) { double c = (o.u0 + o.u1) / 2, dd = Math.Abs(c - u); if (dd < Math.Max(0.6, (o.u1 - o.u0) / 2 + 0.3) && (best == null || dd < bestD)) { best = o; bestD = dd; } }
                    if (best != null) Push(Over(kind, d.edge, best, d));
                }
                else { var it = Mk(kind, d.edge, u, d); it.y = d.y ?? 0; Push(it); }
            }
            Trim();
            // the artist's kinds the style places by rule, in id order. Those in a row along the top or foot of the wall, or up
            // the corners, share their places with the others of their rule and spacing: each takes its chance's share of one
            // draw a place (from), so their chances together are weights, as several prefabs on one role (Storey's own)
            List<(DetailKind kind, double p, KindRule? r, double from)>? more = null;
            if (st.details?.kinds != null)
            {
                List<(Rule rule, double step, double taken)>? shared = null;
                foreach (var kv in st.details.kinds)
                {
                    if (kv.Value <= 0 || !(DetailKinds.Get(kv.Key) is DetailKind dk) || !RuleFits(dk)) continue;
                    var kr = RuleOf(st, kv.Key); double p = kv.Value / 100, from = 0;
                    if (dk.rule == Rule.AlongTop || dk.rule == Rule.AlongFoot || dk.rule == Rule.Corners)
                    {
                        double step = dk.rule == Rule.Corners ? CornerStep(kr) : PerBay(kr);
                        shared ??= new List<(Rule, double, double)>(); int g = shared.FindIndex(x => x.rule == dk.rule && x.step == step);
                        if (g < 0) { g = shared.Count; shared.Add((dk.rule, step, 0)); }
                        from = shared[g].taken; shared[g] = (dk.rule, step, from + p);
                    }
                    (more ??= new List<(DetailKind, double, KindRule?, double)>()).Add((dk, p, kr, from));
                }
            }
            bool spans = false;
            // an artist's kind keeps clear of everything already on the wall (spans over two openings meet in the middle instead)
            void PushClear(Item d)
            {
                var (lo, hi) = Rise(d, h);
                foreach (var m in out_)
                {
                    if (m.i != d.i) continue;
                    if (m.Spans && d.Spans) { if (Math.Abs(m.u - d.u) < 0.05) return; continue; }
                    if (Math.Abs(m.u - d.u) >= (m.Width + d.Width) / 2 + 0.15) continue;
                    var (mlo, mhi) = Rise(m, h);
                    if (lo < mhi + 0.1 && mlo < hi + 0.1) return;
                }
                int was = out_.Count; Push(d);
                spans |= out_.Count > was && d.Spans;
            }
            // what is placed under the ceiling (vents, an artist's on bare wall) has its top 45 cm under it, or 5 cm under a
            // cornice's foot (Facade.CorniceUnder)
            double clear = Facade.CorniceUnder(b, k) is double cu ? Math.Max(0.45, cu + 0.05) : 0.45;
            for (int i = 0; i < n; i++)
            {
                if (Derived.EdgeStateAt(b, k, i) >= EdgeState.Quiet) continue;   // a quiet or bare wall has no details by rule (docs/EDITOR.md §6.7)
                var (f, ops) = OpsOf(i); double uS = f.uS, uE = f.uE, L = f.e.L; if (L < 1.5) continue;
                if (R.ac > 0 && k > 0)
                    for (int j = 0; j < ops.Count; j++)
                    {
                        var o = ops[j]; if (o.door || o.full || Hash01(b.id, "ac", k, i, j) >= R.ac) continue;
                        double y = o.Bottom - 0.62; if (y >= 0.25) { var it = Mk(ac, i, (o.u0 + o.u1) / 2, null); it.y = y; Push(it); }
                    }
                if (R.vents > 0)
                {
                    var gaps = new List<(double, double)>(); double prev = uS;
                    foreach (var o in ops) { if (o.Left - prev >= 1.0) gaps.Add((prev, o.Left)); prev = Math.Max(prev, o.Right); }
                    if (uE - prev >= 1.0) gaps.Add((prev, uE));
                    for (int j = 0; j < gaps.Count; j++)
                        if (Hash01(b.id, "vent", k, i, j) < R.vents && h - clear - 0.5 >= 0.25) { var it = Mk(vent, i, (gaps[j].Item1 + gaps[j].Item2) / 2, null); it.y = h - clear - 0.5; Push(it); }
                }
                if (more == null) continue;
                foreach (var (dk, p, kr, from) in more)
                {
                    if (dk.rule == Rule.UnderWindows && k > 0)
                        for (int j = 0; j < ops.Count; j++)
                        {
                            // as an AC unit: its top 7 cm under the window
                            var o = ops[j]; if (o.door || o.full || Hash01(b.id, dk.id, k, i, j) >= p) continue;
                            double y = o.Bottom - 0.07 - dk.h; if (y >= 0.25) { var it = Mk(dk, i, (o.u0 + o.u1) / 2, null); it.y = y; PushClear(it); }
                        }
                    else if (dk.rule == Rule.BareWall)
                    {
                        // as a vent: in a stretch of wall it fits with 25 cm to spare each side, its top 45 cm under the ceiling
                        double need = Math.Max(1.0, dk.w + 0.5), y = h - clear - dk.h, prev = uS; int j = 0;
                        if (y < 0.15) continue;
                        for (int g = 0; g <= ops.Count; g++)   // the gaps before each opening, then after the last
                        {
                            double c = g < ops.Count ? ops[g].Left : uE;
                            if (c - prev >= need && Hash01(b.id, dk.id, k, i, j++) < p) { var it = Mk(dk, i, (prev + c) / 2, null); it.y = y; PushClear(it); }
                            if (g < ops.Count) prev = Math.Max(prev, ops[g].Right);
                        }
                    }
                    else if (dk.rule == Rule.OverOpenings && k == 0)
                    {
                        for (int j = 0; j < ops.Count; j++)
                            if (!ops[j].door && !ops[j].full && Hash01(b.id, dk.id, k, i, j) < p) PushClear(Over(dk, i, ops[j], null));
                    }
                    else if (dk.rule == Rule.Scatter && OnFloor(kr, k)) Scatter(dk, p, kr, i, f, ops);
                    else if (dk.rule == Rule.AlongTop || (dk.rule == Rule.AlongFoot && k == 0)) Along(dk, p, from, kr, i, f, ops);
                }
            }
            if (more != null)
                foreach (var (dk, p, kr, from) in more)
                    if (dk.rule == Rule.Corners && OnFloor(kr, k)) Corners(dk, p, from, kr);
            if (spans) Trim();
            return out_;

            // scattered over a wall, as brick patches (Storey's own): an even spread, a grid of about as many cells as it
            // wants with one tried at random in each of them, in a shuffled order, rather than at random anywhere (which
            // clumps and leaves holes, and wants many more tries). Each keeps its gap from the openings (and the heads over
            // them), the band, the plinth, the wall's ends, party walls and everything already on the wall.
            void Scatter(DetailKind dk, double p, KindRule? kr, int i, EdgeFrame f, List<Opening> ops)
            {
                var (density, gap) = ScatterOf(kr);
                double lo = k == 0 ? Math.Max(st.plinthH ?? 0.45, st.foundation.HasValue ? Lod0.Shared.FoundationH(st) : 0) : 0;
                double band = st.bands && k < N - 1 ? Math.Max(0.05, st.bandH ?? 0.22) : 0;   // as Lod0.Band
                double y0 = Math.Max(0.2, lo + 0.15), y1 = h - (band > 0 ? band + 0.12 : 0.25), u0 = f.uS + 0.25, u1 = f.uE - 0.25;
                // the room its middle has
                double w = dk.w, hh = dk.h, cu0 = u0 + w / 2, cu1 = u1 - w / 2, cv0 = y0 + hh / 2, cv1 = y1 - hh / 2;
                if (cu1 < cu0 || cv1 < cv0) return;
                uint s = Facade.Seed(b.id, k, i, 0); foreach (char c in dk.id) s = (s ^ c) * 16777619; if (s == 0) s = 1;
                double R() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / (double)0x1000000; }
                double want = (u1 - u0) * (y1 - y0) * density * p;
                int n = (int)Math.Floor(want) + (R() < want - Math.Floor(want) ? 1 : 0);
                if (n <= 0) return;
                double W = cu1 - cu0, H = cv1 - cv0;
                int cols = Math.Max(1, Math.Min(n, (int)Math.Round(Math.Sqrt(n * (W + 0.01) / (H + 0.01))))), rows = (n + cols - 1) / cols;
                var order = new int[cols * rows];
                for (int c = 0; c < order.Length; c++) order[c] = c;
                for (int c = order.Length - 1; c > 0; c--) { int x = (int)(R() * (c + 1)); (order[c], order[x]) = (order[x], order[c]); }
                var party = Party.Ranges(site, b, k0, i);
                for (int c = 0; c < n; c++)
                    for (int tries = 0; tries < 4; tries++)
                    {
                        int cell = order[c];
                        double uc = cu0 + (cell % cols + R()) * W / cols, vc = cv0 + (cell / cols + R()) * H / rows;
                        double a0 = uc - w / 2, a1 = uc + w / 2, b0 = vc - hh / 2, b1 = vc + hh / 2;
                        if (!Clear(a0, a1, b0, b1, gap, i, ops)) continue;
                        bool onParty = false; foreach (var r in party) if (a0 < r.e + 0.3 && a1 > r.s - 0.3) { onParty = true; break; }
                        if (onParty) break;
                        var it = Mk(dk, i, uc, null); it.y = vc - hh / 2 - dk.bottom; out_.Add(it);
                        break;
                    }
            }

            // whether a box (u across, v up from the floor) is clear of the openings with their heads, and of what is on the wall
            // the gap is kept from the other details, as the old generator's spacing; openings by a few centimetres, past what
            // dresses them (an artist's window's bounds take in its frame and sill; the generator's own heads, sills and hoods
            // lie outside the hole, so more is kept round those)
            bool Clear(double a0, double a1, double b0, double b1, double gap, int i, List<Opening> ops)
            {
                foreach (var o in ops)
                {
                    bool own = o.kind == null;
                    double side = own ? 0.12 : 0.05, over = own ? 0.45 : 0.05, under = own ? 0.15 : 0.05;
                    if (a0 < o.Right + side && a1 > o.Left - side && b0 < o.Top + over && b1 > o.Bottom - under) return false;
                }
                foreach (var m in out_)
                {
                    if (m.i != i || m.corner) continue;
                    double half = m.Width / 2; var (mlo, mhi) = Rise(m, h);
                    if (a0 - gap < m.u + half && a1 + gap > m.u - half && b0 - gap < mhi && b1 + gap > mlo) return false;
                }
                return true;
            }

            // in a row along the top of the tier's wall (its pivot at the top of the storey's wall: it hangs below it), or
            // along the foot of the ground floor's (its pivot at the floor), so many to a bay: the bays as the style lays
            // them out along the whole edge, centred. Never across a doorway, past the wall's end or where the tier above goes on.
            void Along(DetailKind dk, double p, double from, KindRule? kr, int i, EdgeFrame f, List<Opening> ops)
            {
                bool top = dk.rule == Rule.AlongTop;
                List<(double, double)>? open = null;
                if (top && k < N - 1)
                {
                    if (Derived.TierStart(b, k + 1) == k0) return;   // the wall goes on up
                    open = Geo.ExposedRanges(fp, Derived.OutlineAt(b, k + 1), i);
                    if (open.Count == 0) return;
                }
                int per = PerBay(kr); double bay = Math.Max(0.5, st.bay), L = f.e.L;
                int slots = (int)Math.Floor(L / bay); if (slots == 0) return;
                double off = (L - slots * bay) / 2, sp = bay / per, half = dk.w / 2;
                var (fs, fe) = f.m.Span(Dim.T_EXT, Dim.T_EXT);
                for (int j = 0; j < slots * per; j++)
                {
                    double u = off + j * sp + sp / 2, x = Hash01(b.id, top ? "top" : "foot", k, i, j * 32 + per);
                    if (x < from || x >= from + p || u - half < fs - 1e-6 || u + half > fe + 1e-6) continue;
                    bool ok = open == null;
                    if (open != null) foreach (var (s0, e0) in open) if (u > s0 && u < e0) { ok = true; break; }
                    if (!top) foreach (var o in ops) if (o.door && u + half > o.Left && u - half < o.Right) { ok = false; break; }
                    if (!ok) continue;
                    var it = Mk(dk, i, u, null); it.y = top ? h : 0; Push(it);
                }
            }

            // stacked up the corners in steps of the kind's spacing, each one middled in its step, over the whole run of storeys
            // the corner goes up (its tier's, those the rule puts it on), the leftover split between the run's foot and top, as
            // the old generator stacks its vertical edge details up the whole building: each belongs to the storey its middle
            // is on, and may reach into the next. A storey at a time, each storey's leftover was a gap at every floor. Facing
            // out along the corner's bisector, on the outside of its walls. Not where a party wall meets the corner.
            void Corners(DetailKind dk, double p, double from, KindRule? kr)
            {
                double step = CornerStep(kr); var mode = kr?.corners ?? DetailCorners.Outward;
                var floors = kr?.floors ?? DetailFloors.All;
                int ka = floors == DetailFloors.Upper ? Math.Max(k0, 1) : k0, kb = floors == DetailFloors.Ground ? Math.Min(Party.TierEnd(b, k0), 1) : Party.TierEnd(b, k0);
                if (k < ka || k >= kb) return;
                double y0 = Derived.FloorBase(b, ka), run = Derived.FloorBase(b, kb) - y0, yk = Derived.FloorBase(b, k);
                int steps = (int)Math.Floor(run / step + 1e-9); if (steps == 0) return;
                double off = (run - steps * step) / 2;
                // this storey's steps: those whose middle is on it, from its floor up to the next one's, worked out alike for both
                // storeys at a floor, so each step is on exactly one
                int j0 = Math.Max(0, (int)Math.Ceiling((yk - y0 - off) / step - 0.5)), j1 = Math.Min(steps, (int)Math.Ceiling((Derived.FloorBase(b, k + 1) - y0 - off) / step - 0.5));
                for (int i = 0; i < n; i++)
                {
                    int ip = (i - 1 + n) % n;
                    var a = fp[ip]; var c = fp[i]; var e = fp[(i + 1) % n];
                    double ax = c.x - a.x, az = c.z - a.z, cx = e.x - c.x, cz = e.z - c.z, la = Geo.Hypot(ax, az), lc = Geo.Hypot(cx, cz);
                    if (la < 1e-6 || lc < 1e-6) continue;
                    double turn = (ax * cz - az * cx) / (la * lc) * (ccw ? 1 : -1);
                    if (Math.Abs(turn) < 1e-4) continue;   // no corner: the walls run on
                    bool outward = turn > 0;
                    if ((mode == DetailCorners.Outward && !outward) || (mode == DetailCorners.Inward && outward)) continue;
                    // the corner's angle where the detail stands, between its walls (90° a rectangle's; going in, the notch's):
                    // outside the kind's range it would float off a sharp corner or sink into a blunt one
                    double half = (180 - Math.Acos(Math.Max(-1, Math.Min(1, (ax * cx + az * cz) / (la * lc)))) * 180 / Math.PI) / 2;
                    if (2 * half < dk.cornerMin - 1e-6 || 2 * half > dk.cornerMax + 1e-6) continue;
                    if (OnParty(i, 0) || OnParty(ip, la)) continue;
                    // none beside a bare wall, nor between two quiet ones (as the old generator kept them off its blank edges and
                    // between two sides it left plain); a wall that is only blank keeps its corners
                    EdgeState sa = Derived.EdgeStateAt(b, k, ip), sc = Derived.EdgeStateAt(b, k, i);
                    if (sa == EdgeState.Bare || sc == EdgeState.Bare || (sa >= EdgeState.Quiet && sc >= EdgeState.Quiet)) continue;
                    for (int j = j0; j < j1; j++)
                    {
                        double x = Hash01(b.id, "corner", ka, i, j * 32 + (int)Math.Min(31, Math.Round(step * 10)));
                        if (x < from || x >= from + p) continue;
                        var it = Mk(dk, i, 0, null); it.corner = true; it.y = y0 + off + (j + 0.5) * step - yk - dk.bottom - dk.h / 2;
                        if (dk.cornerBend && Math.Abs(half - 45) > 1e-6)
                        {
                            // made for 90°: each half turned onto its own wall, which leaves the corner at half its angle from the
                            // bisector (behind the detail, round a corner going out; ahead of it, in a notch)
                            it.bend = (outward ? 45 - half : half - 45) * Math.PI / 180;
                            it.bendOut = Math.Sin(Math.PI / 4) / Math.Sin(half * Math.PI / 180);
                        }
                        out_.Add(it);
                    }
                }
            }
        }

        /// <summary>
        /// Where a detail over an opening spans: 25 cm past it each side, or 5 cm past an artist's window's dressing where that
        /// reaches further (shutters).
        /// </summary>
        public static (double u0, double u1) OverSpan(Opening o) => (o.u0 - Math.Max(0.25, o.rl + 0.05), o.u1 + Math.Max(0.25, o.rr + 0.05));

        /// <summary>How high an item reaches on its storey (of height h), from its bottom to its top, for keeping clear of it.</summary>
        static (double lo, double hi) Rise(Item it, double h) => it.kind.anchor switch
        {
            Anchor.Wall => (it.y + it.kind.bottom, it.y + it.kind.bottom + it.kind.h),
            Anchor.Opening => it.kind.lod0 == null ? (it.yt - 0.15, it.yt + 0.45) : (it.y + it.kind.bottom, it.y + it.kind.bottom + it.kind.h),
            _ => (0, h),
        };

        /// <summary>Geometry of storey k's details; lod 0 = full, 1 = simplified (an artist's kind: its LOD1 mesh, if it has one).</summary>
        public static void Build(MeshBuilder op, Site site, BuildingData b, int k, Palette C, int lod)
        {
            var items = At(site, b, k); if (items.Count == 0) return;
            var fp = Derived.OutlineAt(b, k); bool ccw = Geo.Area2(fp) > 0;
            var Wp = new List<Vec2>(fp.Count); foreach (var p in fp) Wp.Add(new Vec2(p.x + b.pos.x, p.z + b.pos.z));
            double y = Derived.FloorBase(b, k), h = Derived.FloorH(b, k), T = Dim.T_EXT; var st = Derived.StyleAt(b, k);
            Swatch metal = C.detailMetal, grille = C.grille, dark = C.detailDark, rail = C.rail, canvas = C.trim, canvasIn = C.trimShade, disc = C.dish;
            foreach (var d in items)
            {
                var e = Geo.EdgeInfo(Wp, d.i, ccw); var F = e.F; var u = e.u; var w = e.w; op.Ctx(Geo.WallCtx(e.a, u, w, e.L), 1);
                if (op.ShapeOnly) continue;   // the wall is numbered; the rest is faces
                if (lod != 0 && d.kind.noLod1) continue;   // too small to see at LOD1's distance (Storey's own)
                P3 X(double uu, double yy, double ww) => F.At(uu, yy, ww);
                void Bx(double u0, double u1, double y0, double y1, double w0, double w1, Swatch c, Swatch? cIn = null, Skip sk = Skip.None) => op.OBox(F, u0, u1, y0, y1, w0, w1, c, cIn, sk);
                double uc = d.u;
                if (d.kind.lod0 is DetailKind.Mesh full)
                {
                    // an artist's mesh: on the wall where it was put, at each storey's floor up the wall, or over its opening
                    double sx = 1, sy = 1, um = uc, y0 = y + d.y;
                    if (d.kind.anchor == Anchor.Opening && d.Spans) { um = (d.u0 + d.u1) / 2; sx = (d.u1 - d.u0) / Math.Max(0.05, d.kind.w); }
                    else if (d.kind.anchor == Anchor.Tier) { y0 = y; if (d.kind.stretch && d.kind.h > 0.05) sy = h / d.kind.h; }
                    if (d.corner)
                    {
                        // up a corner: a frame out along the bisector of its two walls' outward normals, from where their outer
                        // faces meet
                        var pe = Geo.EdgeInfo(Wp, (d.i - 1 + Wp.Count) % Wp.Count, ccw);
                        double dot = pe.w.x * w.x + pe.w.z * w.z, bx = pe.w.x + w.x, bz = pe.w.z + w.z, bl = Geo.Hypot(bx, bz);
                        if (1 + dot < 1e-6 || bl < 1e-9) continue;   // a wall doubling back on itself
                        double ox = e.a.x + bx * T / (1 + dot), oz = e.a.z + bz * T / (1 + dot); bx /= bl; bz /= bl;
                        F = new Frame(new Vec2(ox - bx * T, oz - bz * T), new Vec2(bz, -bx), new Vec2(bx, bz)); um = 0;
                    }
                    // the style's colour for its main colour, where the style gives one (Storey's own)
                    ColorSlot? to = RuleOf(st, d.kind.id)?.color is string cr ? KindColors.SlotOf(cr) : null;
                    DetailKinds.Place(op, F, lod == 0 || d.kind.lod1 == null ? full : d.kind.lod1, um, y0, sx, sy, C, to.HasValue ? d.kind.main : null, to, d.bend, d.bendOut);
                    continue;
                }
                switch (d.kind.id)
                {
                    case DetailKinds.Ac:
                    {
                        double yb = y + d.y; Bx(uc - 0.375, uc + 0.375, yb, yb + 0.55, T, T + 0.45, metal, null, Skip.In);
                        if (lod == 0) { Bx(uc - 0.3, uc + 0.3, yb + 0.08, yb + 0.47, T + 0.45, T + 0.47, grille, null, Skip.In); foreach (var q in new[] { -1, 1 }) Bx(uc + q * 0.31 - 0.02, uc + q * 0.31 + 0.02, yb - 0.06, yb, T, T + 0.42, dark, null, Skip.In | Skip.Top); }
                        break;
                    }
                    case DetailKinds.Vent:
                    {
                        double yb = y + d.y; Bx(uc - 0.25, uc + 0.25, yb, yb + 0.5, T, T + 0.12, dark, null, Skip.In);
                        if (lod == 0) for (int j = 0; j < 4; j++) Bx(uc - 0.22, uc + 0.22, yb + 0.08 + j * 0.1, yb + 0.11 + j * 0.1, T + 0.12, T + 0.16, metal, null, Skip.In);
                        break;
                    }
                    case DetailKinds.Dish:
                    {
                        double yb = y + d.y; var c0 = X(uc, yb + 0.3, T + 0.38); var nn = new P3(w.x * 0.8, 0.6, w.z * 0.8); var e1 = new P3(u.x, 0, u.z);
                        var e2 = P3.Cross(nn, e1);
                        P3[] Ring(double off) { var r = new P3[8]; for (int j = 0; j < 8; j++) { double an = j / 8.0 * Math.PI * 2, cx = Math.Cos(an) * 0.3, cz = Math.Sin(an) * 0.3; r[j] = c0 + e1 * cx + e2 * cz + nn * off; } return r; }
                        op.Poly(Ring(0.03), nn, disc); op.Poly(Ring(0), nn * -1, dark);
                        Bx(uc - 0.03, uc + 0.03, yb - 0.04, yb + 0.03, T, T + 0.33, dark, null, Skip.In); if (lod == 0) Bx(uc - 0.02, uc + 0.02, yb + 0.03, yb + 0.3, T + 0.3, T + 0.36, dark, null, Skip.Bot);
                        break;
                    }
                    case DetailKinds.Awning:
                    {
                        double yt = y + d.yt, u0 = d.u0, u1 = d.u1; var nn = new P3(w.x * 0.371, 0.928, w.z * 0.371);
                        op.Quad(X(u0, yt + 0.45, T), X(u1, yt + 0.45, T), X(u1, yt + 0.05, T + 1.0), X(u0, yt + 0.05, T + 1.0), nn, canvas);
                        op.Quad(X(u0, yt + 0.41, T), X(u1, yt + 0.41, T), X(u1, yt + 0.01, T + 1.0), X(u0, yt + 0.01, T + 1.0), nn * -1, canvasIn);
                        Bx(u0, u1, yt - 0.15, yt + 0.05, T + 0.98, T + 1.02, canvas, null, Skip.UStart | Skip.UEnd);
                        if (lod == 0) foreach (var (uu, sg) in new[] { (u0, -1.0), (u1, 1.0) }) op.Poly(new[] { X(uu, yt + 0.45, T), X(uu, yt + 0.05, T + 1.0), X(uu, yt - 0.15, T + 1.0) }, new P3(u.x * sg, 0, u.z * sg), canvasIn);
                        break;
                    }
                    case DetailKinds.Escape:
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
                        // its side against the wall, which the door onto it shows from inside: only Unity draws it, a centimetre out
                        if (lod == 0) { op.Backing = true; Bx(u0, u1, pb, pt, T + 0.01, T + 1.1, rail, null, Facade.AllBut(Skip.In)); op.Backing = false; }
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
                                foreach (var (ww, sg) in new[] { (T + 0.96, 1.0), (T + 0.14, -1.0) }) op.Quad(X(us, pt - 0.05, ww), X(ue, pt + h - 0.45, ww), X(ue, pt + h - 0.17, ww), X(us, pt + 0.23, ww), new P3(w.x * sg, 0, w.z * sg), dark);
                                // the stringers' backs, a centimetre in (only Unity draws them: the prototype's are two-sided)
                                op.Backing = true;
                                foreach (var (ww, sg) in new[] { (T + 0.95, -1.0), (T + 0.15, 1.0) }) op.Quad(X(us, pt - 0.05, ww), X(ue, pt + h - 0.45, ww), X(ue, pt + h - 0.17, ww), X(us, pt + 0.23, ww), new P3(w.x * sg, 0, w.z * sg), dark);
                                op.Backing = false;
                            }
                            else
                            {
                                double du = ue - us, dy = h - 0.14, ln = Geo.Hypot(du, dy), sgn = Math.Sign(du);
                                var nn = new P3(-u.x * dy / ln * sgn, Math.Abs(du) / ln, -u.z * dy / ln * sgn);
                                var q = new[] { X(us, pt + 0.02, T + 0.2), X(us, pt + 0.02, T + 0.9), X(ue, pt + h - 0.12, T + 0.9), X(ue, pt + h - 0.12, T + 0.2) };
                                var qb = new P3[4]; for (int j = 0; j < 4; j++) qb[j] = new P3(q[j].x, q[j].y - 0.06, q[j].z);
                                op.Poly(q, nn, rail); op.Poly(qb, nn * -1, dark);
                                // its edges (only Unity draws them: the prototype's faces are two-sided)
                                op.Backing = true;
                                P3 mid = (q[0] + q[1] + q[2] + q[3]) * 0.25;
                                for (int j = 0; j < 4; j++)
                                {
                                    int jn = (j + 1) % 4; P3 o = (q[j] + q[jn]) * 0.5 - mid; o = o - nn * P3.Dot(o, nn);
                                    op.Quad(q[j], q[jn], qb[jn], qb[j], o * (1 / Math.Sqrt(P3.Dot(o, o))), dark);
                                }
                                op.Backing = false;
                            }
                        }
                        break;
                    }
                }
            }
        }
    }
}
