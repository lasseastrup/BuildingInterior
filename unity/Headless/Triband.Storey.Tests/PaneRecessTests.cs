using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// LOD1's flat panes and the recess LOD0 cuts (StoreyWindow.hlsl's StoreyPaneRecess, docs/CITY.md §2). LOD0 sets a plain
    /// window's pane 16.5 cm into the wall with the wall's colour round it; LOD1's pane lies 3 cm proud of a flat wall, and the
    /// shader works out what LOD0 shows at each of its points: the pane, or a reveal. Here the shader's sum, written out
    /// again, against rays cast at LOD0's own triangles, from where LOD1 and LOD0 meet. Storey's own.
    /// </summary>
    public class PaneRecessTests
    {
        readonly ITestOutputHelper output;
        public PaneRecessTests(ITestOutputHelper o) { output = o; }

        /// <summary>
        /// StoreyPaneRecess as the shader has it, in doubles: (true, where on LOD0's pane) or (false, the normal of the reveal
        /// or, trim, the centre bar met).
        /// </summary>
        static (bool pane, double u, double v, P3 n, bool trim) Recess(double u, double v, double perMu, double perMv, P3 pos, P3 eye, P3 N, bool bar = false)
        {
            double tl = Math.Sqrt(N.z * N.z + N.x * N.x); var T = new P3(N.z / tl, 0, -N.x / tl);
            double vx = eye.x - pos.x, vy = eye.y - pos.y, vz = eye.z - pos.z, vl = Math.Sqrt(vx * vx + vy * vy + vz * vz); vx /= vl; vy /= vl; vz /= vl;
            double vn = Math.Max(vx * N.x + vy * N.y + vz * N.z, 1e-3);
            double gu = -(vx * T.x + vz * T.z) / vn * Recess195 * perMu, gv = -vy / vn * Recess195 * perMv;
            double hu = u + gu, hv = v + gv;
            if (hu >= 0 && hu <= 1 && hv >= 0 && hv <= 1)
            {
                double front = u + gu * (BarFront / Recess195), hw = Facade.BarHalf * perMu;
                if (bar && Math.Max(front, hu) > 0.5 - hw && Math.Min(front, hu) < 0.5 + hw)
                {
                    if (Math.Abs(front - 0.5) < hw) return (false, 0, 0, N, true);
                    double sb = -Math.Sign(gu); return (false, 0, 0, new P3(sb * T.x, 0, sb * T.z), true);
                }
                return (true, hu, hv, default, false);
            }
            double au = (gu >= 0 ? 1 - u : u) / Math.Max(Math.Abs(gu), 1e-6), av = (gv >= 0 ? 1 - v : v) / Math.Max(Math.Abs(gv), 1e-6);
            if (au < av) { double s = -Math.Sign(gu); return (false, 0, 0, new P3(s * T.x, 0, s * T.z), false); }
            return (false, 0, 0, new P3(0, -Math.Sign(gv), 0), false);
        }

        const double Recess195 = 0.195, BarFront = 0.15;

        [Fact]
        public void TheShadersRecessIsLod0sDepthFromLod1sPane()
        {
            // from LOD1's pane, PaneProud off the wall's outer face, to LOD0's, T × 0.45 from its inner face
            Assert.Equal(Recess195, Dim.T_EXT - Dim.T_EXT * 0.45 + Lod1.PaneProud, 9);
            // LOD0's centre bar: its front T × 0.6 from the wall's inside, 3 cm either side of the middle (Facade.Dress)
            Assert.Equal(BarFront, Dim.T_EXT + Lod1.PaneProud - Dim.T_EXT * 0.6, 9);
            var hlsl = File.ReadAllText(UnityAsset.Package("com.triband.storey", "Unity", "Shaders", "StoreyWindow.hlsl"));
            Assert.Matches(new Regex(@"#define STOREY_PANE_RECESS 0\.195\b"), hlsl);
            Assert.Matches(new Regex(@"#define STOREY_BAR_FRONT 0\.15\b"), hlsl);
            Assert.Matches(new Regex(@"#define STOREY_BAR_HALF " + Facade.BarHalf.ToString(System.Globalization.CultureInfo.InvariantCulture).Replace(".", "\\.") + @"\b"), hlsl);
            // the sum written out above is the shader's
            foreach (var line in new[]
            {
                "float2 go = -float2(dot(V, T), V.y) / max(dot(V, N), 1e-3) * (STOREY_PANE_RECESS * perM);",
                "if (all(hit >= 0.0) && all(hit <= 1.0))",
                "uv = hit; return float3(0.0, 0.0, 0.0);",
                "float2 at = lerp(uv, 1.0 - uv, step(0.0, go)) / max(abs(go), 1e-6);",
                "float front = uv.x + go.x * (STOREY_BAR_FRONT / STOREY_PANE_RECESS), hw = STOREY_BAR_HALF * perM.x;",
                "if (bar && max(front, hit.x) > 0.5 - hw && min(front, hit.x) < 0.5 + hw)",
                "if (abs(front - 0.5) < hw) return N;",
                "return -sign(go.x) * T;",
                "if (at.x < at.y) return -sign(go.x) * T;",
                "return float3(0.0, -sign(go.y), 0.0);",
            }) Assert.Contains(line, hlsl);
        }

        static (BuildingData b, Site site) Tower(string preset)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.footprint = new() { new Vec2(0, 0), new Vec2(24, 0), new Vec2(24, 16), new Vec2(0, 16) };
            b.floors = Enumerable.Range(0, 8).Select(_ => new FloorData()).ToList();
            b.style = Styles.Preset(preset); b.interior = false; b.details.Clear(); b.shafts.Clear();
            return (b, new Site(d.buildings));
        }

        /// <summary>
        /// Rays from where LOD1 and LOD0 meet (35 to 80 m, at every angle and height) through points on LOD1's flat panes:
        /// what LOD0 shows on the same line of sight against what the shader says. They agree but where LOD0 has trim the
        /// shader leaves out (a sill's or head's edge), the wall's share of the windows is LOD0's, and so is the share of a
        /// wide window's centre bar.
        /// </summary>
        [Theory]
        [InlineData("glass")]
        [InlineData("brick")]
        [InlineData("concrete")]
        public void TheRecessShowsWhatLod0Shows(string preset)
        {
            var (b, site) = Tower(preset);
            var l0 = Lod0.Build(site, b).Op; var l1 = Lod1.Build(site, b);
            var s0 = new RayScene(); s0.Add(l0.P, l0.I, t => (int)l0.C[l0.I[3 * t]].slot);
            bool Flat(int t) => Enumerable.Range(0, 3).All(k => l1.PaneUV.TryGetValue(l1.I[3 * t + k], out var w) && w.flat);
            var s1 = new RayScene(); s1.Add(l1.P, l1.I, t => Flat(t) ? 1 : 0);
            var flats = Enumerable.Range(0, l1.Tris).Where(Flat).ToList();
            Assert.NotEmpty(flats);
            var rng = new Random(5);
            int rays = 0, agree = 0, trim = 0, wall0 = 0, wall1 = 0, normals = 0, normalsRight = 0, bar0 = 0, bar1 = 0;
            var miss = new Dictionary<string, int>();
            while (rays < 6000)
            {
                // a point on a flat pane, and an eye in front of it
                int t = flats[rng.Next(flats.Count)];
                int ia = l1.I[3 * t], ib = l1.I[3 * t + 1], ic = l1.I[3 * t + 2];
                double r1 = rng.NextDouble(), r2 = rng.NextDouble(); if (r1 + r2 > 1) { r1 = 1 - r1; r2 = 1 - r2; }
                P3 A = l1.P[ia], B = l1.P[ib], C = l1.P[ic];
                var p = new P3(A.x + (B.x - A.x) * r1 + (C.x - A.x) * r2, A.y + (B.y - A.y) * r1 + (C.y - A.y) * r2, A.z + (B.z - A.z) * r1 + (C.z - A.z) * r2);
                var N = l1.N[ia];
                double ang = (rng.NextDouble() - 0.5) * Math.PI * 0.9, dist = 35 + rng.NextDouble() * 45, h = 1.5 + rng.NextDouble() * 60;
                double tl = Math.Sqrt(N.x * N.x + N.z * N.z); double tx = N.z / tl, tz = -N.x / tl, nx = N.x / tl, nz = N.z / tl;
                double flatD = dist * Math.Cos(Math.Asin(Math.Min(0.95, Math.Abs(h - p.y) / dist)));
                var eye = new P3(p.x + (nx * Math.Cos(ang) + tx * Math.Sin(ang)) * flatD, h, p.z + (nz * Math.Cos(ang) + tz * Math.Sin(ang)) * flatD);
                double dx = p.x - eye.x, dy = p.y - eye.y, dz = p.z - eye.z, dl = Math.Sqrt(dx * dx + dy * dy + dz * dz); dx /= dl; dy /= dl; dz /= dl;
                // LOD1 sees this pane there (nothing of its own in front)
                int h1 = s1.Hit(eye.x, eye.y, eye.z, dx, dy, dz, out double t1);
                if (h1 < 0 || s1[h1].tag != 1 || Math.Abs(t1 - dl) > 0.01) continue;
                int h0 = s0.Hit(eye.x, eye.y, eye.z, dx, dy, dz, out double t0);
                if (h0 < 0) continue;
                rays++;
                // where on the window, and its uv per metre, from the triangle (the shader: from the screen derivatives)
                var wa = l1.PaneUV[ia]; var wb = l1.PaneUV[ib]; var wc = l1.PaneUV[ic];
                double u = wa.u + (wb.u - wa.u) * r1 + (wc.u - wa.u) * r2, v = wa.v + (wb.v - wa.v) * r1 + (wc.v - wa.v) * r2;
                double perMu = PerMetre(A, B, C, wa.u, wb.u, wc.u, P => P.x * tx + P.z * tz), perMv = PerMetre(A, B, C, wa.v, wb.v, wc.v, P => P.y);
                var sh = Recess(u, v, perMu, perMv, p, eye, N, wa.bar);
                var slot = (ColorSlot)s0[h0].tag;
                bool isWall = slot == ColorSlot.Wall, isPane = slot == ColorSlot.Glass;
                // LOD0's centre bar: trim LOD0 shows within 4 cm of the window's middle along the wall (its 3 cm, and its side)
                double middle = (p.x * tx + p.z * tz) - (u - 0.5) / perMu, at0 = (eye.x + dx * t0) * tx + (eye.z + dz * t0) * tz;
                bool isBar = slot == ColorSlot.Trim && wa.bar && Math.Abs(at0 - middle) < Facade.BarHalf + 0.01;
                if (isWall) wall0++;
                if (!sh.pane && !sh.trim) wall1++;
                if (isBar) bar0++;
                if (sh.trim) bar1++;
                if ((isWall && !sh.pane && !sh.trim) || (isPane && sh.pane) || (slot == ColorSlot.Trim && sh.trim)) agree++;
                else if (slot == ColorSlot.Trim || slot == ColorSlot.Frame) trim++;
                else { string k = $"LOD0 {slot}, shader {(sh.pane ? "pane" : "reveal")}"; miss[k] = miss.TryGetValue(k, out var c) ? c + 1 : 1; }
                // the reveal faces the way LOD0's does (its wall's own face where the line of sight meets the wall before the opening)
                if (isWall && !sh.pane && !sh.trim)
                {
                    var q = s0[h0]; double ql = Math.Sqrt(q.nx * q.nx + q.ny * q.ny + q.nz * q.nz);
                    normals++; if (sh.n.x * q.nx / ql + sh.n.y * q.ny / ql + sh.n.z * q.nz / ql > 0.99 || q.nx * N.x / ql + q.nz * N.z / ql > 0.99) normalsRight++;
                }
            }
            output.WriteLine($"{preset}: {rays} rays through LOD1's flat panes; LOD0 shows its wall on {100.0 * wall0 / rays:0.0}%, the shader {100.0 * wall1 / rays:0.0}% (before it, 0); its centre bars {100.0 * bar0 / rays:0.0}%, the shader {100.0 * bar1 / rays:0.0}%; agree {100.0 * agree / rays:0.0}%, LOD0's other trim {100.0 * trim / rays:0.0}%, its reveals' normals {100.0 * normalsRight / Math.Max(1, normals):0.0}% right");
            foreach (var kv in miss.OrderByDescending(kv => kv.Value)) output.WriteLine($"  {kv.Key}: {kv.Value}");
            Assert.True(agree >= 0.95 * (rays - trim), $"{agree} of {rays - trim} agree");
            Assert.True(Math.Abs(wall1 - wall0) <= 0.03 * rays, $"the wall's share: LOD0 {wall0}, the shader {wall1} of {rays}");
            Assert.True(normalsRight >= 0.98 * normals, $"{normalsRight} of {normals} reveals face LOD0's way");
            Assert.True(Math.Abs(bar1 - bar0) <= 0.01 * rays, $"the centre bars' share: LOD0 {bar0}, the shader {bar1} of {rays}");
        }

        /// <summary>
        /// The shader finds a window's size from screen derivatives (StoreyPaneRecess's perM): of uv, which reaches the
        /// fragment stage in 16-bit floats, and of the point. A 1.66 × 3 m pane through a pinhole camera at an iPhone 11 Pro's
        /// 1,125 lines and 60°, from 15 to 120 m and at angles to 70°: the reveal's edge, 16.5 cm deep, moves by under a
        /// tenth of a pixel for the rounding from 30 m out, and under a third at 15 m (nearer than LOD1 shows but for a LOD0
        /// that is late).
        /// </summary>
        [Fact]
        public void TheWindowsSizeFromScreenDerivativesHoldsInHalves()
        {
            double ppm = 1125 / (2 * Math.Tan(Math.PI / 6)), W = 1.66, H = 3.0;
            double worst = 0, worstFar = 0;
            foreach (double dist in new[] { 15.0, 30, 55, 80, 120 })
                foreach (double deg in new[] { 0.0, 30, 50, 70 })
                    foreach (double pitch in new[] { 0.0, 25 })
                    {
                        // the pane in the plane z = 0, facing +z, u along +x, v up; the eye looking at its middle
                        double a = deg * Math.PI / 180, b = pitch * Math.PI / 180;
                        double ex = W / 2 + Math.Sin(a) * Math.Cos(b) * dist, ey = H / 2 + Math.Sin(b) * dist, ez = Math.Cos(a) * Math.Cos(b) * dist;
                        double fx = W / 2 - ex, fy = H / 2 - ey, fz = -ez, fl = Math.Sqrt(fx * fx + fy * fy + fz * fz); fx /= fl; fy /= fl; fz /= fl;
                        double rx = -fz, rz = fx, rl = Math.Sqrt(rx * rx + rz * rz); rx /= rl; rz /= rl;   // right: cross(forward, up)
                        double ux = -rz * fy, uy = rz * fx - rx * fz, uz = rx * fy;                       // up: cross(right, forward)
                        // a 2 × 2 quad of pixels at the pane's middle, as the GPU takes derivatives
                        var u = new double[2, 2]; var v = new double[2, 2]; var x = new double[2, 2]; var y = new double[2, 2];
                        for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
                        {
                            double dx = fx + (i - 0.5) / ppm * rx + (j - 0.5) / ppm * ux, dy = fy + (j - 0.5) / ppm * uy, dz = fz + (i - 0.5) / ppm * rz + (j - 0.5) / ppm * uz;
                            double t = -ez / dz, px = ex + dx * t, py = ey + dy * t;
                            x[i, j] = (float)px; y[i, j] = (float)py;
                            u[i, j] = (double)(Half)(px / W); v[i, j] = (double)(Half)(py / H);   // half4 win
                        }
                        double perU = (Math.Abs(u[1, 0] - u[0, 0]) + Math.Abs(u[0, 1] - u[0, 0])) / Math.Max(Math.Abs(x[1, 0] - x[0, 0]) + Math.Abs(x[0, 1] - x[0, 0]), 1e-6);
                        double perV = (Math.Abs(v[1, 0] - v[0, 0]) + Math.Abs(v[0, 1] - v[0, 0])) / Math.Max(Math.Abs(y[1, 0] - y[0, 0]) + Math.Abs(y[0, 1] - y[0, 0]), 1e-6);
                        // the reveal's width on the window (metres), and its error in pixels at this distance
                        double side = Math.Tan(a) * 0.165, px1 = ppm / dist;
                        double err = Math.Max(Math.Abs(perU * W - 1) * side, Math.Abs(perV * H - 1) * Math.Tan(b) * 0.165) * px1 * Math.Cos(a);
                        worst = Math.Max(worst, err); if (dist >= 30) worstFar = Math.Max(worstFar, err);
                        if (err > 0.1) output.WriteLine($"  {dist} m, {deg}°, pitch {pitch}°: {err:0.000} px (u {perU * W - 1:+0.000;-0.000}, v {perV * H - 1:+0.000;-0.000})");
                    }
            output.WriteLine($"the reveal's edge moves by at most {worst:0.000} px for 16-bit uv, {worstFar:0.000} px from 30 m out");
            Assert.True(worst < 0.3 && worstFar < 0.1, $"{worst:0.00} px, {worstFar:0.00} px from 30 m out");
        }

        /// <summary>How much a uv changes per metre along <paramref name="along"/> over a flat triangle (affine there).</summary>
        static double PerMetre(P3 A, P3 B, P3 C, double a, double b, double c, Func<P3, double> along)
        {
            double xa = along(A), xb = along(B), xc = along(C);
            // the pair whose uv differs most gives the slope
            var pairs = new[] { (xb - xa, b - a), (xc - xa, c - a), (xc - xb, c - b) };
            var (dx, du) = pairs.OrderByDescending(q => Math.Abs(q.Item2)).First();
            return Math.Abs(dx) < 1e-9 ? 0 : Math.Abs(du / dx);
        }
    }
}
