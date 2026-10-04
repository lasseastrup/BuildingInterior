#nullable enable
using System;
using System.Collections.Generic;
using Clipper2Lib;

namespace Triband.Storey.Generate
{
    /// <summary>A planar roof piece: points (with holes), normal, and what it is coloured as.</summary>
    public sealed class RoofPart
    {
        public List<P3> Pts = new List<P3>();
        public List<List<P3>> Holes = new List<List<P3>>();
        public P3 N;
        public RoofKind Kind;
        /// <summary>Part of a dormer: LOD2's massing leaves these out.</summary>
        public bool Dormer;
        /// <summary>
        /// A dormer's window as a wall opening (its frame: the dormer front at w = T_EXT), so it is dressed as the
        /// facade's windows are (<see cref="Facade.Dress"/>: sills, heads, frames, bars, the artist's window, rooms behind
        /// the glass). Null for plain glass.
        /// </summary>
        public Frame? WinF;
        public Opening? WinO;
    }

    public enum RoofKind { Roof, Trim, Soffit, Wall, Window }

    public sealed class RoofParts { public List<RoofPart> Parts = new List<RoofPart>(); public double Rise; }

    /// <summary>
    /// Pitched roofs. The top tier's style picks the roof: flat (parapet or curb), hip, gable or shed, with
    /// a pitch and an eave. Roof faces come from the straight skeleton of the outline pushed out to the
    /// eave line (every face at the same pitch), then clipped to the real roof outline: at the eave line
    /// on eave edges, at the wall face on rake and gable edges, and behind the owner's wall on a shared
    /// wall the neighbour owns. A gable turns a triangular end face into a wall by running the ridge out
    /// to the wall along the ridge line; walls on rake and gable edges follow the roof profile up from the
    /// wall top. Eave edges get a fascia and a soffit, closed by end caps where they meet a rake.
    /// </summary>
    public static class Roofs
    {
        public static RoofType TypeOf(BuildingData b) => Derived.StyleAt(b, b.floors.Count).roofType;
        public static bool IsPitched(BuildingData b) => TypeOf(b) != RoofType.Flat;

        enum EdgeKind { Eave, Rake, Own, Skip, Gable }

        /// <summary>The building's own pitched roof, or null for a flat one.</summary>
        public static RoofParts? Parts(Site site, BuildingData b)
        {
            var st = Derived.StyleAt(b, b.floors.Count); if (st.roofType == RoofType.Flat) return null;
            int N = b.floors.Count, k0 = Derived.TierStart(b, N); var raw = Derived.OutlineAt(b, N);
            var R = Make(b, st.roofType, st.pitch, st.eave, raw, PartyKinds(site, b, k0, raw), Derived.RoofY(b), null, st.mansard, st.dormers, Edit.CornerCuts.SharpMap(b, k0));
            if (b.voids.Count > 0) R.Parts = Courtyards.CutRoof(b, R.Parts, Derived.RoofY(b));   // courtyards open through the roof
            return R;
        }

        static string[] PartyKinds(Site site, BuildingData b, int k0, List<Vec2> raw)
        {
            var party = new string[raw.Count];
            for (int i = 0; i < raw.Count; i++)
            {
                var pr = Party.Ranges(site, b, k0, i); double L = Geo.EdgeLen(raw, i), cov = 0; bool skip = false;
                foreach (var r in pr) { cov += r.e - r.s; if (!r.own) skip = true; }
                party[i] = cov > 0.5 * L ? (skip ? "skip" : "own") : "";
            }
            return party;
        }

        static double Clamp(double v, double a, double c) => Math.Max(a, Math.Min(c, v));

        /// <summary>
        /// Roof parts of <paramref name="raw"/> (building-local) sitting on wall tops at <paramref name="top"/>;
        /// <paramref name="cut"/> is a world-space multipolygon the roof is cut away from (rings as x,z pairs).
        /// </summary>
        /// <param name="sharp">
        /// For an outline with cut corners: each of <paramref name="raw"/>'s points' place in the sharp outline, and the
        /// sharp outline (<see cref="Edit.CornerCuts.SharpMap"/>). The roof's planes (eaves, ridge, gable ends, a shed's
        /// slope) are then planned on the sharp outline and fitted to the cut one: a rounded corner's short edges would
        /// otherwise each become an eave or a gable of their own.
        /// </param>
        public static RoofParts Make(BuildingData b, RoofType type, double? pitch, double? eave, List<Vec2> raw, string[] party, double top, PathsD? cut, double? mansard = null, double? dormers = null, (int[] map, List<Vec2> sharp)? sharp = null)
        {
            int n = raw.Count; double T = Dim.T_EXT; bool mans = type == RoofType.Mansard;
            // a mansard's pitch is its shallow upper roof's; its eaves have the steep lower slope, with a short eave
            double slope = Math.Tan(Clamp(pitch ?? (type == RoofType.Shed ? 15 : mans ? 20 : 30), 5, 60) * Math.PI / 180), e = Clamp(eave ?? 0.35, 0, 1.2), ox = b.pos.x, oz = b.pos.z;
            double eaveSlope = mans ? Math.Tan(MansardPitch * Math.PI / 180) : slope;
            if (mans) e = Math.Min(e, 0.2);
            double hb = top - e * eaveSlope;
            bool ccw = Geo.Area2(raw) > 0;
            var fp = new List<Vec2>(n); var em = new int[n];
            for (int j = 0; j < n; j++) { var p = ccw ? raw[j] : raw[n - 1 - j]; fp.Add(new Vec2(p.x + ox, p.z + oz)); em[j] = ccw ? j : (2 * n - 2 - j) % n; }
            var E = new (Vec2 p, double[] d, double[] nin, double L)[n];
            for (int j = 0; j < n; j++)
            {
                var p = fp[j]; var q = fp[(j + 1) % n]; double L = Geo.Hypot(q.x - p.x, q.z - p.z); if (L == 0) L = 1;
                var d = new[] { (q.x - p.x) / L, (q.z - p.z) / L };
                E[j] = (p, d, new[] { -d[1], d[0] }, L);
            }
            static List<Vec2> OffsetOf(int count, (Vec2 p, double[] d, double[] nin, double L)[] Ed, double[] off)
            {
                var o = new List<Vec2>(count);
                for (int j = 0; j < count; j++)
                {
                    int i = (j - 1 + count) % count; var A = Ed[i]; var B = Ed[j];
                    double pax = A.p.x - A.nin[0] * off[i], paz = A.p.z - A.nin[1] * off[i], pbx = B.p.x - B.nin[0] * off[j], pbz = B.p.z - B.nin[1] * off[j];
                    double den = A.d[0] * B.d[1] - A.d[1] * B.d[0];
                    if (Math.Abs(den) < 1e-9) { o.Add(new Vec2(pbx, pbz)); continue; }
                    double t = ((pbx - pax) * B.d[1] - (pbz - paz) * B.d[0]) / den;
                    double mx = pax + A.d[0] * t, mz = paz + A.d[1] * t;
                    // two nearly parallel edges set back by different amounts meet far off: a spike. Halfway between
                    // their ends instead (only ever at a cut corner's short edges)
                    double ex = A.p.x + A.d[0] * A.L - A.nin[0] * off[i], ez = A.p.z + A.d[1] * A.L - A.nin[1] * off[i];
                    if (Math.Abs(off[i] - off[j]) > 1e-9 && Geo.Hypot(mx - B.p.x, mz - B.p.z) > Math.Max(2.0, 4 * Math.Max(Math.Abs(off[i]), Math.Abs(off[j]))))
                    { mx = (ex + pbx) / 2; mz = (ez + pbz) / 2; }
                    o.Add(new Vec2(mx, mz));
                }
                return o;
            }
            List<Vec2> OffsetPoly(double[] off) => OffsetOf(n, E, off);
            var kinds = new EdgeKind[n];
            for (int j = 0; j < n; j++) kinds[j] = party[em[j]] == "skip" ? EdgeKind.Skip : party[em[j]] == "own" ? EdgeKind.Own : EdgeKind.Eave;

            // the polygon the roof's planes are planned on: the outline itself, or for a gable or shed roof with cut
            // corners the sharp one (src: each edge's sharp edge, or -1 for a cut's own edge, with its corner in cornerOf)
            int m = n; var EP = E; var kindsP = kinds;
            int[]? src = null, cornerOf = null;
            if (sharp is (int[] smap, List<Vec2> sraw) && sraw.Count >= 3 && sraw.Count < n && (type == RoofType.Gable || type == RoofType.Shed))
            {
                m = sraw.Count;
                int SI(int r) => ccw ? r : m - 1 - r;   // a sharp point's place in the roof's (counter-clockwise) order
                var fpS = new List<Vec2>(m); for (int j = 0; j < m; j++) { var q = sraw[ccw ? j : m - 1 - j]; fpS.Add(new Vec2(q.x + ox, q.z + oz)); }
                EP = new (Vec2 p, double[] d, double[] nin, double L)[m];
                for (int j = 0; j < m; j++)
                {
                    var p = fpS[j]; var q = fpS[(j + 1) % m]; double L = Geo.Hypot(q.x - p.x, q.z - p.z); if (L == 0) L = 1;
                    var d = new[] { (q.x - p.x) / L, (q.z - p.z) / L };
                    EP[j] = (p, d, new[] { -d[1], d[0] }, L);
                }
                src = new int[n]; cornerOf = new int[n]; kindsP = new EdgeKind[m];
                for (int j = 0; j < m; j++) kindsP[j] = EdgeKind.Eave;
                for (int j = 0; j < n; j++)
                {
                    int a = SI(smap[ccw ? j : n - 1 - j]), c = SI(smap[ccw ? (j + 1) % n : (2 * n - 2 - j) % n]);
                    if (a == c) { src[j] = -1; cornerOf[j] = a; continue; }
                    src[j] = a; cornerOf[j] = -1;
                    if (kinds[j] != EdgeKind.Eave) kindsP[a] = kinds[j];   // a party wall along the sharp edge
                }
            }
            int low = -1;
            if (type == RoofType.Shed)
            {
                double bl = -1; for (int j = 0; j < m; j++) if (kindsP[j] == EdgeKind.Eave && EP[j].L > bl) { bl = EP[j].L; low = j; }
                if (low < 0) low = 0;
                for (int j = 0; j < m; j++) if (kindsP[j] == EdgeKind.Eave && Math.Abs(EP[j].d[0] * EP[low].d[0] + EP[j].d[1] * EP[low].d[1]) < 0.9) kindsP[j] = EdgeKind.Rake;
            }
            var offFull = new double[m]; for (int j = 0; j < m; j++) offFull[j] = T + e;
            var full = OffsetOf(m, EP, offFull);
            double HOf(int j, double x, double z) => hb + eaveSlope * ((x - full[j].x) * EP[j].nin[0] + (z - full[j].z) * EP[j].nin[1]);

            var faces = new List<(int j, List<double[]> pts)>();
            if (type == RoofType.Shed) { var pts = new List<double[]>(); foreach (var p in full) pts.Add(new[] { p.x, p.z }); faces.Add((low, pts)); }
            else
            {
                var sk = Skeleton.Compute(full);
                foreach (var f in sk.Faces) { var pts = new List<double[]>(); foreach (var q in f.Pts) pts.Add(new[] { q[0], q[1] }); faces.Add((f.Edge, pts)); }
                if (type == RoofType.Gable)
                {
                    string K(double[] q) => q[0].ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + "," + q[1].ToString("F5", System.Globalization.CultureInfo.InvariantCulture);
                    var cnt = new Dictionary<string, int>(); foreach (var f in faces) foreach (var q in f.pts) { cnt.TryGetValue(K(q), out var c0); cnt[K(q)] = c0 + 1; }
                    var gone = new HashSet<int>();
                    for (int fi = 0; fi < faces.Count; fi++)
                    {
                        var f = faces[fi]; if (f.pts.Count != 3 || kindsP[f.j] != EdgeKind.Eave) continue;
                        var tip = f.pts[2]; if (!cnt.TryGetValue(K(tip), out var c) || c != 3) continue;
                        var a = EP[(f.j - 1 + m) % m].nin; var cc = EP[(f.j + 1) % m].nin; var g = new[] { a[0] - cc[0], a[1] - cc[1] }; var dir = new[] { -g[1], g[0] }; var Gn = EP[f.j].nin;
                        double gpx = EP[f.j].p.x - Gn[0] * T, gpz = EP[f.j].p.z - Gn[1] * T, den = dir[0] * Gn[0] + dir[1] * Gn[1]; if (Math.Abs(den) < 1e-6) continue;
                        double t = ((gpx - tip[0]) * Gn[0] + (gpz - tip[1]) * Gn[1]) / den; var nt = new[] { tip[0] + dir[0] * t, tip[1] + dir[1] * t };
                        string kt = K(tip);
                        for (int hi = 0; hi < faces.Count; hi++) { if (hi == fi) continue; var h = faces[hi]; for (int q = 0; q < h.pts.Count; q++) if (K(h.pts[q]) == kt) h.pts[q] = nt; }
                        kindsP[f.j] = EdgeKind.Gable; gone.Add(fi);
                    }
                    var kept = new List<(int, List<double[]>)>(); for (int fi = 0; fi < faces.Count; fi++) if (!gone.Contains(fi)) kept.Add(faces[fi]);
                    faces = kept;
                }
            }
            if (src != null)
            {
                // the outline's edges take their sharp edge's part; a cut's own edges that of the side they turn nearer to
                for (int j = 0; j < n; j++)
                {
                    if (src[j] >= 0) { kinds[j] = kindsP[src[j]]; continue; }
                    int c = cornerOf![j], before = (c - 1 + m) % m;
                    double near0 = Math.Abs(E[j].d[0] * EP[before].d[0] + E[j].d[1] * EP[before].d[1]), near1 = Math.Abs(E[j].d[0] * EP[c].d[0] + E[j].d[1] * EP[c].d[1]);
                    var k = near0 >= near1 ? kindsP[before] : kindsP[c];
                    kinds[j] = k == EdgeKind.Gable ? EdgeKind.Rake : k;
                }
            }
            var off = new double[n]; for (int j = 0; j < n; j++) off[j] = kinds[j] == EdgeKind.Eave ? T + e : kinds[j] == EdgeKind.Skip ? -T : T;
            var Q = OffsetPoly(off); var offT = new double[n]; for (int j = 0; j < n; j++) offT[j] = T; var W = OffsetPoly(offT);
            var parts = new List<RoofPart>(); double rise = 0;
            var clipped = new List<(int j, List<P3> pts)>();
            var Qpath = new PathD(); foreach (var p in Q) Qpath.Add(new PointD(p.x, p.z));
            var region = new PathsD { Qpath };
            PathsD? brk = null; double hBreak = 0;
            if (mans)
            {
                // the steep slope rises to the break; above it, a shallow hip over the outline the slope has reached there
                hBreak = top + Clamp(mansard ?? 2.4, 0.5, 6);
                var fullPath = new PathD(); foreach (var p in full) fullPath.Add(new PointD(p.x, p.z));
                brk = Clipper.InflatePaths(new PathsD { fullPath }, -(e + (hBreak - top) / eaveSlope), JoinType.Miter, EndType.Polygon, 1000, 6);
                if (brk.Count > 0) region = Clipper.BooleanOp(ClipType.Difference, region, brk, FillRule.NonZero, 6);
            }
            var bands = new Dictionary<int, List<List<Vec2>>>();   // each eave's sloped face in plan, for the dormers
            foreach (var f in faces)
            {
                var subj = new PathD(); foreach (var q in f.pts) subj.Add(new PointD(q[0], q[1]));
                var tree = new PolyTreeD();
                Clipper.BooleanOp(ClipType.Intersection, new PathsD { subj }, region, tree, FillRule.NonZero, 6);
                var polys = new List<List<List<Vec2>>>(); Collect(tree, polys);
                var g = EP[f.j].nin; var v = new P3(-eaveSlope * g[0], 1, -eaveSlope * g[1]); var nrm = v.Normalized;
                if (!bands.TryGetValue(f.j, out var bl)) bands[f.j] = bl = new List<List<Vec2>>();
                foreach (var pl in polys) if (pl[0].Count >= 3) bl.Add(pl[0]);
                foreach (var pl in polys)
                {
                    if (pl[0].Count < 3) continue;
                    var part = new RoofPart { N = nrm, Kind = RoofKind.Roof };
                    foreach (var p in pl[0]) { var q = new P3(p.x, HOf(f.j, p.x, p.z), p.z); rise = Math.Max(rise, q.y - top); part.Pts.Add(q); }
                    for (int r = 1; r < pl.Count; r++) { var hole = new List<P3>(); foreach (var p in pl[r]) hole.Add(new P3(p.x, HOf(f.j, p.x, p.z), p.z)); part.Holes.Add(hole); }
                    parts.Add(part); clipped.Add((f.j, part.Pts));
                }
            }
            if (brk != null)
                foreach (var ring in brk)
                {
                    var R = new List<Vec2>(ring.Count); foreach (var q in ring) R.Add(new Vec2(q.x, q.y));
                    if (R.Count < 3) continue;
                    if (Geo.Area2(R) < 0) R.Reverse();
                    int rm = R.Count;
                    var ud = new double[rm][]; for (int j = 0; j < rm; j++) { var p = R[j]; var q = R[(j + 1) % rm]; double L = Geo.Hypot(q.x - p.x, q.z - p.z); if (L == 0) L = 1; ud[j] = new[] { -(q.z - p.z) / L, (q.x - p.x) / L }; }
                    double HU(int j, double x, double z) => hBreak + slope * ((x - R[j].x) * ud[j][0] + (z - R[j].z) * ud[j][1]);
                    foreach (var f in Skeleton.Compute(R).Faces)
                    {
                        if (f.Pts.Count < 3) continue;
                        var part = new RoofPart { N = new P3(-slope * ud[f.Edge][0], 1, -slope * ud[f.Edge][1]).Normalized, Kind = RoofKind.Roof };
                        foreach (var q in f.Pts) { var pt = new P3(q[0], HU(f.Edge, q[0], q[1]), q[1]); rise = Math.Max(rise, pt.y - top); part.Pts.Add(pt); }
                        parts.Add(part);
                    }
                }
            if (dormers is double every && every > 0)
                for (int j = 0; j < m; j++)
                    if (kindsP[j] == EdgeKind.Eave && bands.TryGetValue(j, out var band) && band.Count > 0)
                        Dormers(parts, full[j], EP[j].d, EP[j].nin, EP[j].L, eaveSlope, hb, e, mans ? hBreak : double.PositiveInfinity, Math.Max(every, 2.2), band);
            double EaveH(int j) => type == RoofType.Shed ? HOf(low, Q[j].x, Q[j].z) : hb;
            // the roof's height over a point: the plane of the face it lies in (in plan)
            double RoofAt(double x, double z)
            {
                if (type == RoofType.Shed) return HOf(low, x, z);
                int best = -1; double bd = double.MaxValue;
                foreach (var f in faces)
                {
                    var ring = new List<Vec2>(f.pts.Count); foreach (var q in f.pts) ring.Add(new Vec2(q[0], q[1]));
                    if (Geo.Pip(ring, x, z)) return HOf(f.j, x, z);
                    foreach (var q in f.pts) { double dd = Geo.Hypot(q[0] - x, q[1] - z); if (dd < bd) { bd = dd; best = f.j; } }
                }
                return best >= 0 ? HOf(best, x, z) : hb;
            }
            int CapAt(int j, int end)
            {
                int nb = end == 1 ? (j + 1) % n : (j - 1 + n) % n; if (kinds[nb] != EdgeKind.Eave || e < 0.05) return -1;
                var d0 = E[end == 1 ? j : nb].d; var d1 = E[end == 1 ? nb : j].d; return d0[0] * d1[1] - d0[1] * d1[0] > 1e-9 ? nb : -1;
            }
            List<double[]> ClipAbove(List<double[]> poly, double y0)
            {
                var cl = new List<double[]>();
                for (int i = 0; i < poly.Count; i++)
                {
                    var u = poly[i]; var vv = poly[(i + 1) % poly.Count]; bool iu = u[1] >= y0 - 1e-6, iv = vv[1] >= y0 - 1e-6;
                    if (iu) cl.Add(u);
                    if (iu != iv) { double s = (y0 - u[1]) / (vv[1] - u[1]); cl.Add(new[] { u[0] + (vv[0] - u[0]) * s, y0 }); }
                }
                var o = new List<double[]>(); for (int i = 0; i < cl.Count; i++) if (i == 0 || Geo.Hypot(cl[i][0] - cl[i - 1][0], cl[i][1] - cl[i - 1][1]) > 1e-5) o.Add(cl[i]);
                return o;
            }
            for (int j = 0; j < n; j++)
            {
                var A = Q[j]; var B = Q[(j + 1) % n]; double L = Geo.Hypot(B.x - A.x, B.z - A.z); if (L < 1e-4) continue;
                var d = new[] { (B.x - A.x) / L, (B.z - A.z) / L }; var outN = new P3(-E[j].nin[0], 0, -E[j].nin[1]);
                P3 Lift(double t, double y) => new P3(A.x + d[0] * t, y, A.z + d[1] * t);
                if (kinds[j] == EdgeKind.Eave && src != null && src[j] < 0)
                {
                    // a cut corner's eave runs round inside the sharp roof's eave line: as high as the roof is at each end
                    if (e < 0.05) continue;
                    double hA = RoofAt(A.x, A.z), hB = RoofAt(B.x, B.z), ya = hA - 0.15, yb2 = hB - 0.15; var Wa = W[j]; var Wb = W[(j + 1) % n];
                    parts.Add(new RoofPart { Pts = { new P3(A.x, ya, A.z), new P3(B.x, yb2, B.z), new P3(B.x, hB, B.z), new P3(A.x, hA, A.z) }, N = outN, Kind = RoofKind.Trim });
                    parts.Add(new RoofPart { Pts = { new P3(A.x, ya, A.z), new P3(B.x, yb2, B.z), new P3(Wb.x, yb2, Wb.z), new P3(Wa.x, ya, Wa.z) }, N = new P3(0, -1, 0), Kind = RoofKind.Soffit });
                    if (Math.Min(ya, yb2) > top + 0.01) parts.Add(new RoofPart { Pts = { new P3(Wa.x, top, Wa.z), new P3(Wb.x, top, Wb.z), new P3(Wb.x, yb2, Wb.z), new P3(Wa.x, ya, Wa.z) }, N = outN, Kind = RoofKind.Wall });
                    continue;
                }
                if (kinds[j] == EdgeKind.Eave)
                {
                    if (e < 0.05) continue;
                    double hE = EaveH(j), yb = hE - 0.15; var Wa = W[j]; var Wb = W[(j + 1) % n];
                    parts.Add(new RoofPart { Pts = { new P3(A.x, yb, A.z), new P3(B.x, yb, B.z), new P3(B.x, hE, B.z), new P3(A.x, hE, A.z) }, N = outN, Kind = RoofKind.Trim });
                    parts.Add(new RoofPart { Pts = { new P3(A.x, yb, A.z), new P3(B.x, yb, B.z), new P3(Wb.x, yb, Wb.z), new P3(Wa.x, yb, Wa.z) }, N = new P3(0, -1, 0), Kind = RoofKind.Soffit });
                    if (yb > top + 0.01) parts.Add(new RoofPart { Pts = { new P3(Wa.x, top, Wa.z), new P3(Wb.x, top, Wb.z), new P3(Wb.x, yb, Wb.z), new P3(Wa.x, yb, Wa.z) }, N = outN, Kind = RoofKind.Wall });
                    continue;
                }
                if (kinds[j] == EdgeKind.Skip) continue;
                var prof = new List<double[]>();
                foreach (var c in clipped) foreach (var q in c.pts)
                {
                    double t = (q.x - A.x) * d[0] + (q.z - A.z) * d[1], dd = Math.Abs((q.x - A.x) * d[1] - (q.z - A.z) * d[0]);
                    if (dd < 1e-4 && t > -1e-4 && t < L + 1e-4) prof.Add(new[] { t, q.y });
                }
                prof.Sort((u, v) => u[0].CompareTo(v[0]));
                var pr = new List<double[]>(); for (int i = 0; i < prof.Count; i++) if (i == 0 || prof[i][0] - prof[i - 1][0] > 1e-4 || Math.Abs(prof[i][1] - prof[i - 1][1]) > 1e-4) pr.Add(prof[i]);
                if (pr.Count < 2) continue;
                double At(double t)
                {
                    if (t <= pr[0][0]) return pr[0][1];
                    for (int i = 1; i < pr.Count; i++) if (t <= pr[i][0]) { double t0 = pr[i - 1][0], y0 = pr[i - 1][1], t1 = pr[i][0], y1 = pr[i][1]; return y0 + (y1 - y0) * (t - t0) / Math.Max(t1 - t0, 1e-9); }
                    return pr[pr.Count - 1][1];
                }
                List<double[]> Span(double t0, double t1) { var o = new List<double[]> { new[] { t0, At(t0) } }; foreach (var q in pr) if (q[0] > t0 + 1e-4 && q[0] < t1 - 1e-4) o.Add(q); o.Add(new[] { t1, At(t1) }); return o; }
                int c0 = CapAt(j, 0), c1 = CapAt(j, 1);
                double TW(Vec2 Wc) => (Wc.x - A.x) * d[0] + (Wc.z - A.z) * d[1];
                double tt0 = c0 >= 0 ? TW(W[j]) : pr[0][0], tt1 = c1 >= 0 ? TW(W[(j + 1) % n]) : pr[pr.Count - 1][0];
                if (tt1 > tt0 + 1e-4)
                {
                    var poly = new List<double[]> { new[] { tt0, top } }; poly.AddRange(Span(tt0, tt1)); poly.Add(new[] { tt1, top });
                    var wp = new List<P3>(); foreach (var q in ClipAbove(poly, top)) wp.Add(Lift(q[0], q[1]));
                    if (wp.Count >= 3 && Math.Abs(PolyArea3(wp)) > 1e-4) parts.Add(new RoofPart { Pts = wp, N = outN, Kind = RoofKind.Wall });
                }
                foreach (var (nb, tc, tw) in new[] { (c0, 0.0, tt0), (c1, L, tt1) })
                {
                    if (nb < 0) continue; double yb = EaveH(nb) - 0.15, a0 = Math.Min(tc, tw), a1 = Math.Max(tc, tw); if (a1 - a0 < 1e-4) continue;
                    var sp = Span(a0, a1); sp.Reverse();
                    var cp = new List<P3> { Lift(a0, yb), Lift(a1, yb) }; foreach (var q in sp) cp.Add(Lift(q[0], q[1]));
                    if (Math.Abs(PolyArea3(cp)) > 1e-4) parts.Add(new RoofPart { Pts = cp, N = outN, Kind = RoofKind.Wall });
                }
            }
            return new RoofParts { Parts = cut != null ? CutParts(parts, cut) : parts, Rise = Math.Max(rise, 0) };
        }

        /// <summary>A mansard's lower slope, in degrees.</summary>
        public const double MansardPitch = 70;

        /// <summary>A dormer's outer width, its front wall's height at most, and its own roof's pitch (degrees).</summary>
        public const double DormerW = 1.5, DormerH = 1.5, DormerPitch = 45;

        /// <summary>
        /// Dormers along one eave, <paramref name="every"/> metres apart: a front wall with a window just behind the wall
        /// below, two cheeks back to the roof, and a gabled roof running back into it. Only those that fit inside the eave's
        /// sloped face (in plan, with a margin) are built, so they keep clear of hips, valleys, ridges and a mansard's break.
        /// The eave line starts at <paramref name="o"/>, runs along <paramref name="d"/> for <paramref name="L"/>, and the roof
        /// rises inward along <paramref name="nin"/> at <paramref name="slope"/> from <paramref name="hb"/>.
        /// </summary>
        static void Dormers(List<RoofPart> parts, Vec2 o, double[] d, double[] nin, double L, double slope, double hb, double e, double maxY, double every, List<List<Vec2>> band)
        {
            double hw = DormerW / 2, rr = hw * Math.Tan(DormerPitch * Math.PI / 180);
            double n0 = e + Math.Max(0.05, 0.25 / slope);   // just behind the wall below: about 25 cm up the roof from its face
            double ys = hb + slope * n0, hf = Math.Min(DormerH, maxY - 0.2 - rr - ys);
            if (hf < 0.9) return;
            double yt = ys + hf, yr = yt + rr, nt = (yt - hb) / slope, nr = (yr - hb) / slope;
            P3 At(double a, double nn, double y) => new P3(o.x + d[0] * a + nin[0] * nn, y, o.z + d[1] * a + nin[1] * nn);
            Vec2 Pl(double a, double nn) => new Vec2(o.x + d[0] * a + nin[0] * nn, o.z + d[1] * a + nin[1] * nn);
            bool Fits(double c)
            {
                // 15 cm clear of the face's edges; the ridge's end only has to be on the face (it can run up to a mansard's break)
                foreach (var (q, m) in new[] { (Pl(c - hw, n0), 0.15), (Pl(c + hw, n0), 0.15), (Pl(c - hw, nt), 0.15), (Pl(c + hw, nt), 0.15), (Pl(c, nr), 0.05) })
                {
                    bool inside = false;
                    foreach (var poly in band) if (Geo.Pip(poly, q.x, q.z) && Geo.DistToEdges(poly, q) > m) { inside = true; break; }
                    if (!inside) return false;
                }
                return true;
            }
            int cnt = Math.Max(1, (int)Math.Floor(L / every));
            var outN = new P3(-nin[0], 0, -nin[1]); var du = new P3(d[0], 0, d[1]);
            double rs = rr / hw;
            for (int i = 0; i < cnt; i++)
            {
                double c = L / 2 + (i - (cnt - 1) / 2.0) * every;
                if (!Fits(c)) continue;
                var front = new RoofPart { Kind = RoofKind.Wall, N = outN, Dormer = true, Pts = { At(c - hw, n0, ys), At(c + hw, n0, ys), At(c + hw, n0, yt), At(c, n0, yr), At(c - hw, n0, yt) } };
                // the reveal as deep as a facade window's, so its frame and pane sit as theirs do (Facade.Dress)
                double wx = hw - 0.22, w0 = ys + 0.3, w1 = yt - 0.12, rv = Dim.T_EXT * 0.7;
                var hole = new List<P3> { At(c - wx, n0, w0), At(c - wx, n0, w1), At(c + wx, n0, w1), At(c + wx, n0, w0) };
                front.Holes.Add(hole);
                parts.Add(front);
                // the window: a frame whose wall face (w = T_EXT) is the dormer's front, as a facade wall's
                var wf = new Frame(new Vec2(o.x + nin[0] * (n0 + Dim.T_EXT), o.z + nin[1] * (n0 + Dim.T_EXT)), new Vec2(d[0], d[1]), new Vec2(-nin[0], -nin[1]));
                parts.Add(new RoofPart { Kind = RoofKind.Window, N = outN, Dormer = true, WinF = wf, WinO = new Opening { u0 = c - wx, u1 = c + wx, y0 = w0, y1 = w1 },
                    Pts = { At(c - wx, n0 + rv, w0), At(c + wx, n0 + rv, w0), At(c + wx, n0 + rv, w1), At(c - wx, n0 + rv, w1) } });
                // the window's reveals, from the front wall back to the pane
                parts.Add(new RoofPart { Kind = RoofKind.Trim, N = new P3(0, 1, 0), Dormer = true, Pts = { At(c - wx, n0, w0), At(c + wx, n0, w0), At(c + wx, n0 + rv, w0), At(c - wx, n0 + rv, w0) } });
                parts.Add(new RoofPart { Kind = RoofKind.Trim, N = new P3(0, -1, 0), Dormer = true, Pts = { At(c - wx, n0, w1), At(c + wx, n0, w1), At(c + wx, n0 + rv, w1), At(c - wx, n0 + rv, w1) } });
                parts.Add(new RoofPart { Kind = RoofKind.Trim, N = du, Dormer = true, Pts = { At(c - wx, n0, w0), At(c - wx, n0 + rv, w0), At(c - wx, n0 + rv, w1), At(c - wx, n0, w1) } });
                parts.Add(new RoofPart { Kind = RoofKind.Trim, N = du * -1, Dormer = true, Pts = { At(c + wx, n0, w0), At(c + wx, n0 + rv, w0), At(c + wx, n0 + rv, w1), At(c + wx, n0, w1) } });
                // the cheeks: from the front back to where the front's top meets the roof
                parts.Add(new RoofPart { Kind = RoofKind.Wall, N = du * -1, Dormer = true, Pts = { At(c - hw, n0, ys), At(c - hw, n0, yt), At(c - hw, nt, yt) } });
                parts.Add(new RoofPart { Kind = RoofKind.Wall, N = du, Dormer = true, Pts = { At(c + hw, n0, ys), At(c + hw, nt, yt), At(c + hw, n0, yt) } });
                // the dormer's roof: two planes from the cheek tops up to the ridge, back into the main roof
                parts.Add(new RoofPart { Kind = RoofKind.Roof, N = new P3(-rs * d[0], 1, -rs * d[1]).Normalized, Dormer = true, Pts = { At(c - hw, n0, yt), At(c - hw, nt, yt), At(c, nr, yr), At(c, n0, yr) } });
                parts.Add(new RoofPart { Kind = RoofKind.Roof, N = new P3(rs * d[0], 1, rs * d[1]).Normalized, Dormer = true, Pts = { At(c + hw, n0, yt), At(c, n0, yr), At(c, nr, yr), At(c + hw, nt, yt) } });
            }
        }

        static double PolyArea3(List<P3> pts)
        {
            double a = 0;
            for (int i = 0; i < pts.Count; i++) { var p = pts[i]; var q = pts[(i + 1) % pts.Count]; a += (p.x - q.x) * (p.y + q.y) + (p.z - q.z) * (p.y + q.y); }
            return a / 2;
        }

        static void Collect(PolyPathD node, List<List<List<Vec2>>> out_)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var child = (PolyPathD)node[i]; if (child.Polygon == null) continue;
                var poly = new List<List<Vec2>> { Ring(child.Polygon) };
                for (int j = 0; j < child.Count; j++) { var hole = (PolyPathD)child[j]; if (hole.Polygon != null) poly.Add(Ring(hole.Polygon)); Collect(hole, out_); }
                out_.Add(poly);
            }
        }
        static List<Vec2> Ring(PathD p) { var r = new List<Vec2>(p.Count); foreach (var v in p) r.Add(new Vec2(v.x, v.y)); return r; }

        static bool InsideCut(PathsD cut, double x, double z)
        {
            // even-odd over the multipolygon's rings: inside an outer and not inside one of its holes
            var pt = new PointD(x, z); int depth = 0;
            foreach (var r in cut) if (Clipper.PointInPolygon(pt, r) != PointInPolygonResult.IsOutside) depth++;
            return depth % 2 == 1;
        }

        /// <summary>
        /// Remove the parts of planar roof pieces that fall inside a multipolygon (in plan): sloped and flat
        /// pieces by polygon difference in plan, lifted back onto their plane; vertical pieces by the stretches
        /// of their base line outside it.
        /// </summary>
        public static List<RoofPart> CutParts(List<RoofPart> parts, PathsD cut)
        {
            var out_ = new List<RoofPart>();
            foreach (var p in parts)
            {
                var nx = p.N.x; var ny = p.N.y; var nz = p.N.z; var o = p.Pts[0];
                if (Math.Abs(ny) > 1e-6)
                {
                    double Y(double x, double z) => o.y - (nx * (x - o.x) + nz * (z - o.z)) / ny;
                    var subj = new PathsD(); PathD R(List<P3> r) { var pd = new PathD(); foreach (var q in r) pd.Add(new PointD(q.x, q.z)); return pd; }
                    subj.Add(R(p.Pts)); foreach (var h in p.Holes) subj.Add(R(h));
                    var tree = new PolyTreeD();
                    Clipper.BooleanOp(ClipType.Difference, subj, cut, tree, FillRule.EvenOdd, 6);
                    var polys = new List<List<List<Vec2>>>(); Collect(tree, polys);
                    foreach (var pl in polys)
                    {
                        if (pl[0].Count < 3) continue;
                        var np = new RoofPart { N = p.N, Kind = p.Kind };
                        foreach (var q in pl[0]) np.Pts.Add(new P3(q.x, Y(q.x, q.z), q.z));
                        for (int r = 1; r < pl.Count; r++) { var hole = new List<P3>(); foreach (var q in pl[r]) hole.Add(new P3(q.x, Y(q.x, q.z), q.z)); np.Holes.Add(hole); }
                        out_.Add(np);
                    }
                    continue;
                }
                var a = p.Pts[0]; P3? cOpt = null; foreach (var q in p.Pts) if (Geo.Hypot(q.x - a.x, q.z - a.z) > 1e-4) { cOpt = q; break; }
                if (cOpt == null) { out_.Add(p); continue; }
                var c = cOpt.Value; double L = Geo.Hypot(c.x - a.x, c.z - a.z); var d = new[] { (c.x - a.x) / L, (c.z - a.z) / L };
                double Tp(P3 q) => (q.x - a.x) * d[0] + (q.z - a.z) * d[1];
                double t0 = double.PositiveInfinity, t1 = double.NegativeInfinity; foreach (var q in p.Pts) { double t = Tp(q); t0 = Math.Min(t0, t); t1 = Math.Max(t1, t); }
                var cuts = new List<double> { t0, t1 };
                foreach (var r in cut)
                    for (int i = 0; i < r.Count; i++)
                    {
                        var u = r[i]; var v = r[(i + 1) % r.Count]; double ex = v.x - u.x, ez = v.y - u.y, den = d[0] * ez - d[1] * ex; if (Math.Abs(den) < 1e-9) continue;
                        double wx = u.x - a.x, wz = u.y - a.z, t = (wx * ez - wz * ex) / den, s2 = (wx * d[1] - wz * d[0]) / den;
                        if (s2 >= -1e-9 && s2 <= 1 + 1e-9 && t > t0 && t < t1) cuts.Add(t);
                    }
                cuts.Sort();
                for (int i = 0; i < cuts.Count - 1; i++)
                {
                    double s0 = cuts[i], s1 = cuts[i + 1]; if (s1 - s0 < 1e-3) continue; double m = (s0 + s1) / 2;
                    if (InsideCut(cut, a.x + d[0] * m, a.z + d[1] * m)) continue;
                    var poly = new List<double[]>(); foreach (var q in p.Pts) poly.Add(new[] { Tp(q), q.y });
                    foreach (var (lim, sg) in new[] { (s0, 1.0), (s1, -1.0) })
                    {
                        var nxt = new List<double[]>();
                        for (int j = 0; j < poly.Count; j++)
                        {
                            var u = poly[j]; var v = poly[(j + 1) % poly.Count]; bool iu = sg * (u[0] - lim) >= -1e-9, iv = sg * (v[0] - lim) >= -1e-9;
                            if (iu) nxt.Add(u);
                            if (iu != iv) { double f = (lim - u[0]) / (v[0] - u[0]); nxt.Add(new[] { lim, u[1] + (v[1] - u[1]) * f }); }
                        }
                        poly = nxt;
                    }
                    if (poly.Count >= 3)
                    {
                        var pts = new List<P3>(); foreach (var q in poly) pts.Add(new P3(a.x + d[0] * q[0], q[1], a.z + d[1] * q[0]));
                        if (Math.Abs(PolyArea3(pts)) > 1e-5) out_.Add(new RoofPart { Pts = pts, N = p.N, Kind = p.Kind });
                    }
                }
            }
            return out_;
        }

        /// <summary>
        /// A roofed setback: the storey below a setback gets a hip roof instead of a terrace. It is the hip
        /// roof of the lower outline, sitting on its wall tops, with the storeys above cut out: at their outer
        /// wall face, and along an edge where the upper wall runs flush with the lower one, out past the eave.
        /// </summary>
        public static RoofParts? TerraceRoof(Site site, BuildingData b, int k)
        {
            if (k <= 0 || k >= b.floors.Count) return null;
            var tr = b.floors[k].terraceRoof; if (tr == null || !Derived.IsSetback(b, k)) return null;
            int k0 = Derived.TierStart(b, k - 1); var raw = Derived.OutlineAt(b, k - 1); var up = Derived.OutlineAt(b, k);
            double T = Dim.T_EXT, e = 0.3, ox = b.pos.x, oz = b.pos.z;
            var party = PartyKinds(site, b, k0, raw);
            bool uccw = Geo.Area2(up) > 0; int n = up.Count;
            var U = new List<double[]>(n); foreach (var p in up) U.Add(new[] { p.x + ox, p.z + oz });
            double[] OutN(int i)
            {
                var a = U[i]; var c = U[(i + 1) % n]; double L = Geo.Hypot(c[0] - a[0], c[1] - a[1]); if (L == 0) L = 1;
                var u = new[] { (c[0] - a[0]) / L, (c[1] - a[1]) / L }; return uccw ? new[] { u[1], -u[0] } : new[] { -u[1], u[0] };
            }
            var rings = new PathsD();
            var offRing = new PathD();
            for (int j = 0; j < n; j++)
            {
                int i = (j - 1 + n) % n; var A = OutN(i); var B = OutN(j); double mx = A[0] + B[0], mz = A[1] + B[1], dd = 1 + A[0] * B[0] + A[1] * B[1];
                offRing.Add(dd < 1e-6 ? new PointD(U[j][0], U[j][1]) : new PointD(U[j][0] + mx * T / dd, U[j][1] + mz * T / dd));
            }
            rings.Add(offRing);
            var lower = new List<Vec2>(raw.Count); foreach (var p in raw) lower.Add(new Vec2(p.x + ox, p.z + oz));
            double D = T + e + 0.3;
            var ed = new List<(double[] a, double L, double[] u, double[] w, List<double[]> iv)>();
            for (int i = 0; i < n; i++)
            {
                var a = U[i]; var c = U[(i + 1) % n]; double L = Geo.Hypot(c[0] - a[0], c[1] - a[1]); if (L == 0) L = 1e-6;
                ed.Add((a, L, new[] { (c[0] - a[0]) / L, (c[1] - a[1]) / L }, OutN(i), new List<double[]>()));
            }
            foreach (var g in ed)
            {
                if (g.L < 1e-3) continue;
                int N2 = Math.Max(2, (int)Math.Ceiling(g.L / 0.25)); double s0 = -1;
                for (int j = 0; j <= N2; j++)
                {
                    double t = (double)j / N2 * g.L, x = g.a[0] + g.u[0] * t + g.w[0] * (T + 0.1), z = g.a[1] + g.u[1] * t + g.w[1] * (T + 0.1);
                    bool fl = !Geo.Pip(lower, x, z);
                    if (fl && s0 < 0) s0 = t;
                    if ((!fl || j == N2) && s0 >= 0) { double s1 = fl ? t : t - g.L / N2; if (s1 - s0 > 0.2) g.iv.Add(new[] { s0, s1 }); s0 = -1; }
                }
            }
            for (int i = 0; i < n; i++)
            {
                var A = ed[(i - 1 + n) % n]; var B = ed[i];
                double[]? ea = null, sb = null; foreach (var v in A.iv) if (v[1] > A.L - 0.02) { ea = v; break; } foreach (var v in B.iv) if (v[0] < 0.02) { sb = v; break; }
                if (ea != null && sb != null) { ea[1] = A.L + D; sb[0] = -D; }
            }
            foreach (var g in ed) foreach (var v in g.iv)
            {
                PointD X(double tt, double dw) => new PointD(g.a[0] + g.u[0] * tt + g.w[0] * dw, g.a[1] + g.u[1] * tt + g.w[1] * dw);
                rings.Add(new PathD { X(v[0], 0), X(v[1], 0), X(v[1], D), X(v[0], D) });
            }
            // polygon-clipping normalises ring orientation before a union; Clipper does not, and under the
            // non-zero rule a clockwise ring cancels a counter-clockwise one where they overlap
            for (int i = 0; i < rings.Count; i++) if (!Clipper.IsPositive(rings[i])) rings[i].Reverse();
            var cut = Clipper.Union(rings, new PathsD(), FillRule.NonZero, 6);   // the default precision is centimetres
            return Make(b, RoofType.Hip, tr.pitch, e, raw, party, Derived.FloorBase(b, k), cut);
        }

        /// <summary>
        /// Emit roof parts into a mesh. Returns false when there are none. A dormer's window is dressed by the style
        /// <paramref name="st"/> as the facade's are (LOD0), or a plain pane with the window shader's coordinates (LOD1).
        /// </summary>
        public static bool Draw(MeshBuilder op, RoofParts? R, Palette C, FacadeStyle? st = null)
        {
            if (R == null) return false;
            op.Ctx(null, 0);
            foreach (var p in R.Parts)
            {
                if (p.Kind == RoofKind.Window && p.WinF is Frame wf && p.WinO is Opening wo)
                {
                    if (op.Lean) Facade.Pane(op, wf, wo.u0, wo.u1, wo.y0, wo.y1, Dim.T_EXT * 0.45, C.glassDark, false);
                    else Facade.Dress(op, null!, wf, new List<Opening> { wo }, 0, C, true, st);
                    op.Ctx(null, 0);
                    continue;
                }
                var col = p.Kind == RoofKind.Roof ? C.roof : p.Kind == RoofKind.Trim ? C.trim : p.Kind == RoofKind.Soffit ? C.trimShade : p.Kind == RoofKind.Window ? C.glassDark : C.wall;
                var pts = new List<P3>(p.Pts); foreach (var h in p.Holes) pts.AddRange(h);
                op.PolyTris(pts, Triangulate.Planar(p.Pts, p.Holes, p.N), p.N, col);
            }
            return true;
        }
    }
}
