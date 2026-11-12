using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// How a straight flight's rails and steps look (<see cref="StairLook"/>, docs/EDITOR.md §5): the rails' height, the
    /// handrail's size, the spindles' spacing and thickness, posts between them, the newels, and a colour for each part. Each
    /// setting left out is how they looked before they could be set; one out of range is held to its range
    /// (<see cref="Railing"/>). The rails round the openings look as the flights' own do. Storey's own.
    /// </summary>
    public class StairLookTests
    {
        const double Hw = Dim.FLIGHT_W / 2, Xm = -Hw + Dim.FLIGHT_LANE, Z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, Z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;

        static (StoreyDocument d, BuildingData b, CoreData s) Stack(StairLook? look, bool rails = true, bool solid = true)
        {
            var (d, b, s) = StairRailTests.Stack(rails: rails, solid: solid);
            s.look = look;
            return (d, b, s);
        }

        static List<Solid> Boxes(MeshBuilder op, Frame f) => op.Solids!.Where(x => !x.Mitred && x.Prism == null && x.F.O.x == f.O.x && x.F.O.z == f.O.z).ToList();
        static bool Square(Solid x, double w) => Math.Abs(x.u1 - x.u0 - w) < 1e-6 && Math.Abs(x.w1 - x.w0 - w) < 1e-6;
        static bool At(double a, double b) => Math.Abs(a - b) < 1e-6;
        static int Steps(double fh) => Math.Max(8, (int)Math.Round(fh / 0.18));

        [Fact]
        public void WithoutALookTheyAreAsTheyWere()
        {
            // the defaults, which are the numbers the rails and steps were built with before
            var rl = Railing.Of(null);
            Assert.Equal((Lod0.Shared.StairRail, 0.04, 0.07, 0.135, 0.025, 0, 0.05, 0.13, 0.04, 0.04), (rl.H, rl.W, rl.T, rl.Gap, rl.Spindle, rl.Posts, rl.Post, Math.Round(rl.Newel, 9), rl.Off, rl.R));
            Assert.Equal(0.9, rl.FlightH, 9); Assert.Equal(Lod0.Shared.StairNewel, rl.NewelH, 9);
            // a look with nothing set builds the same as none, and is written as none
            string Built(StairLook? look)
            {
                var (d, b, _) = Stack(look);
                var r = Lod0.Build(new Site(d.buildings), b);
                return string.Join(";", Enumerable.Range(0, r.Op.I.Count).Select(i => $"{r.Op.P[r.Op.I[i]]}{r.Op.C[r.Op.I[i]].slot}")) + string.Join(";", r.Segs.SelectMany(g => g).Select(g => $"{g.ax},{g.az},{g.bx},{g.bz},{g.r},{g.h}"));
            }
            Assert.Equal(Built(null), Built(new StairLook()));
            var (_, b2, s2) = Stack(new StairLook());
            Assert.DoesNotContain("\"look\"", PrototypeJson.Write(b2));
        }

        [Fact]
        public void SpindlesAreAsFarApartAndAsThickAsSet()
        {
            var (d, b, s) = Stack(new StairLook { gap = 0.1, spindle = 0.02 });
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var spindles = Boxes(op, f).Where(x => Square(x, 0.02)).ToList();
            // round the first floor's opening, its walkway side: 10 cm apart from newel to newel
            double y1 = Derived.FloorBase(b, 1);
            var level = spindles.Where(x => At((x.u0 + x.u1) / 2, Xm + 0.04) && At(x.y0, y1)).OrderBy(x => x.w0).ToList();
            Assert.Equal(46, level.Count);
            for (int i = 1; i < level.Count; i++) Assert.Equal(0.1, level[i].w0 - level[i - 1].w0, 6);
            // up the flights, a whole number to a tread as near 10 cm apart as that makes: 2 on the ground floor's 23 cm
            // treads, 3 on the first floor's 27 cm ones
            foreach (var (k, each) in new[] { (0, 2), (1, 3) })
            {
                double y = Derived.FloorBase(b, k), up = Derived.FloorBase(b, k + 1);
                Assert.Equal(each * Steps(Derived.FloorH(b, k)), spindles.Count(x => At((x.u0 + x.u1) / 2, Xm - 0.04) && x.y0 > y + 1e-6 && x.y0 < up + 1e-6));
            }
        }

        [Fact]
        public void APostComesAfterEverySoManySpindles()
        {
            var look = new StairLook { posts = 4, post = 0.07 };
            var (d, b, s) = Stack(look);
            var rl = Railing.Of(look);
            Assert.Equal(0.055, rl.Off, 9);              // the rails stand further off the lane's edge for the wider posts
            Assert.Equal(0.16, rl.Newel, 9);             // and the newels grow to take both rails' ends
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var boxes = Boxes(op, f);
            // round the first floor's opening: 7 equal stretches of 4 spindles between the two newels, 6 posts between them
            double y1 = Derived.FloorBase(b, 1), xc = Xm + rl.Off;
            var posts = boxes.Where(x => Square(x, 0.07) && At((x.u0 + x.u1) / 2, xc) && At(x.y0, y1)).OrderBy(x => x.w0).ToList();
            var spindles = boxes.Where(x => Square(x, 0.025) && At((x.u0 + x.u1) / 2, xc) && At(x.y0, y1)).ToList();
            Assert.Equal(6, posts.Count); Assert.Equal(28, spindles.Count);
            for (int i = 0; i < posts.Count; i++) Assert.Equal(Z0 + (i + 1) * (Z1 - Z0) / 7, (posts[i].w0 + posts[i].w1) / 2, 6);
            // up the ground floor's flight (2 to a tread, 40 a side): every fifth upright a post
            double y0 = 0; int n = Steps(Derived.FloorH(b, 0));
            var up = boxes.Where(x => At((x.u0 + x.u1) / 2, Xm - rl.Off) && x.y0 > y0 + 1e-6 && x.y0 < y1 + 1e-6 && (Square(x, 0.07) || Square(x, 0.025))).OrderBy(x => x.w0).ToList();
            Assert.Equal(2 * n, up.Count);
            for (int q = 0; q < up.Count; q++) Assert.Equal(q % 5 == 4, Square(up[q], 0.07));
            // newels as wide as they need to be, at the opening's corners and the flight's foot
            Assert.Contains(boxes, x => Square(x, 0.16) && At((x.u0 + x.u1) / 2, Xm) && At(x.w1, Z0) && At(x.y0, y0));
        }

        [Fact]
        public void TheRailsAreAsHighAndTheirHandrailAsBigAsSet()
        {
            var look = new StairLook { height = 1.2, railW = 0.08, railT = 0.05 };
            var (d, b, s) = Stack(look);
            var rl = Railing.Of(look);
            Assert.Equal((1.2, 0.08, 0.05, 0.06, 0.21, 1.08, 1.36), (rl.H, rl.W, rl.T, rl.Off, Math.Round(rl.Newel, 9), Math.Round(rl.FlightH, 9), Math.Round(rl.NewelH, 9)));
            var site = new Site(d.buildings);
            var r = Lod0.Build(site, b, solids: true); var f = Cores.FrameOf(b, s);
            var boxes = Boxes(r.Op, f);
            double y1 = Derived.FloorBase(b, 1), xc = Xm + 0.06;
            // round the first floor's opening: the handrail 8 × 5 cm, its top 1.2 m up; the spindles up to it; the newels 16 cm higher
            Assert.Contains(boxes, x => At(x.u0, xc - 0.04) && At(x.u1, xc + 0.04) && At(x.y0, y1 + 1.15) && At(x.y1, y1 + 1.2) && At(x.w0, Z0) && At(x.w1, Z1));
            Assert.All(boxes.Where(x => Square(x, 0.025) && At((x.u0 + x.u1) / 2, xc) && At(x.y0, y1)), x => Assert.Equal(y1 + 1.15, x.y1, 6));
            Assert.Contains(boxes, x => Square(x, 0.21) && At(x.y0, y1) && At(x.y1, y1 + 1.36));
            // up the ground floor's flight: the spindles' tops go into a handrail 1.08 m over the nosings' line
            double fh = Derived.FloorH(b, 0), run = (Z1 - Z0) / Steps(fh), sl = fh / (Z1 - Z0);
            var up = boxes.Where(x => Square(x, 0.025) && At((x.u0 + x.u1) / 2, Xm - 0.06) && x.y0 > 1e-6 && x.y0 < y1 + 1e-6).ToList();
            Assert.NotEmpty(up);
            Assert.All(up, x => { double zc = (x.w0 + x.w1) / 2, top = (zc - Z0 + run) * sl + 1.08; Assert.InRange(x.y1, top - 0.05, top); });
            // the walk model's rail round the opening is as high, on the rail's line; the collider's wall up the flight follows
            // its handrail's top
            Assert.Contains(r.Segs[1], g => At(g.h, 1.2) && At(g.ax, f.O.x + xc) && At(g.bx, f.O.x + xc));
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
            double rise = fh / Steps(fh), wall = fh / 2 + rise + 1.08;
            var from = f.At2(Xm + 1, 0); var dir = new P3(-f.u.x, 0, -f.u.z);
            Assert.True(Hit(m, new P3(from.x, wall - 0.05, from.z), dir) is double h0 && Math.Abs(h0 - 1.02) < 1e-6, "under the handrail");
            Assert.True(Hit(m, new P3(from.x, wall + 0.05, from.z), dir) is not double h1 || h1 > 3, "over the handrail");
        }

        static double? Hit(CollisionMesh m, P3 o, P3 d)
        {
            double? best = null;
            for (int t = 0; t < m.Tris; t++)
            {
                if (m.Kind[t] != CollisionKind.Wall) continue;
                P3 a = m.P[m.I[3 * t]], b = m.P[m.I[3 * t + 1]], c = m.P[m.I[3 * t + 2]];
                P3 e1 = b - a, e2 = c - a, pv = P3.Cross(d, e2); double det = P3.Dot(e1, pv);
                if (Math.Abs(det) < 1e-12) continue;
                P3 tv = o - a; double uu = P3.Dot(tv, pv) / det; if (uu < 0 || uu > 1) continue;
                P3 qv = P3.Cross(tv, e1); double vv = P3.Dot(d, qv) / det; if (vv < 0 || uu + vv > 1) continue;
                double tt = P3.Dot(e2, qv) / det;
                if (tt > 1e-9 && (best == null || tt < best)) best = tt;
            }
            return best;
        }

        [Fact]
        public void EachPartIsInItsOwnColour()
        {
            var look = new StairLook
            {
                posts = 3, railColor = "#8B5A2B", spindleColor = "#F2F2EE", postColor = "#2E2E30", treadColor = "#B8864B", riserColor = "#FAFAF7", sideColor = "#9AA0A6",
            };
            var (d, b, s) = Stack(look);
            var op = Lod0.Build(new Site(d.buildings), b).Op; var f = Cores.FrameOf(b, s);
            string? Ref(int t) => KindColors.RefOf(op.C[op.I[3 * t]].slot);
            (P3 c, P3 n) Local(int t)
            {
                P3 L(P3 p) { double dx = p.x - f.O.x, dz = p.z - f.O.z; return new P3(dx * f.u.x + dz * f.u.z, p.y, dx * f.w.x + dz * f.w.z); }
                var c = (L(op.P[op.I[3 * t]]) + L(op.P[op.I[3 * t + 1]]) + L(op.P[op.I[3 * t + 2]])) * (1.0 / 3); var nn = op.N[op.I[3 * t]];
                return (c, new P3(nn.x * f.u.x + nn.z * f.u.z, nn.y, nn.x * f.w.x + nn.z * f.w.z));
            }
            var refs = Enumerable.Range(0, op.Tris).Select(Ref).Where(r => r != null).ToHashSet();
            foreach (var c in new[] { look.railColor, look.spindleColor, look.postColor, look.treadColor, look.riserColor, look.sideColor }) Assert.Contains(c, refs);
            // nothing is in the style's rail colour any more, nor in its steps' shade of its stairs' colour
            Assert.DoesNotContain(Enumerable.Range(0, op.Tris), t => op.C[op.I[3 * t]].slot == ColorSlot.Rail);
            Assert.DoesNotContain(Enumerable.Range(0, op.Tris), t => op.C[op.I[3 * t]].slot == ColorSlot.Core && Math.Abs(op.C[op.I[3 * t]].tone - 0.82) < 1e-6);
            // in the flight lane: the treads facing up, the risers facing its foot, and its sides on its edges, each in its own
            for (int t = 0; t < op.Tris; t++)
            {
                var (c, n) = Local(t);
                if (c.x < -Hw - 1e-6 || c.x > Xm + 1e-6 || c.z < Z0 + 1e-6 || c.z > Z1 - 1e-6) continue;
                if (n.y > 0.99 && Ref(t) != null && Ref(t) != look.postColor && Ref(t) != look.railColor) Assert.Equal(look.treadColor, Ref(t));
                if (n.z < -0.99 && (Ref(t) == look.treadColor || Ref(t) == look.riserColor)) Assert.Equal(look.riserColor, Ref(t));
                // its sides face out of the lane (the openings' slab edges face into it)
                if ((At(c.x, -Hw) && n.x < -0.99) || (At(c.x, Xm) && n.x > 0.99)) Assert.Equal(look.sideColor, Ref(t));
            }
        }

        [Fact]
        public void OutOfRangeASettingIsHeldToItsRange()
        {
            var big = Railing.Of(new StairLook { height = 10, railW = 1, railT = 5, gap = 0.001, spindle = 0.5, posts = 500, post = 9, newel = 0.01 });
            Assert.Equal((2.0, 0.2, 0.3, 0.1, 0.11, 100, 0.2), (big.H, big.W, big.T, big.Spindle, Math.Round(big.Gap, 9), big.Posts, big.Post));
            Assert.Equal(0.12, big.Off, 9);
            Assert.Equal(2 * (big.Off + big.W / 2) + 0.01, big.Newel, 9);   // as wide as both rails, past its own most
            var small = Railing.Of(new StairLook { height = 0.1, railW = 0, railT = 0, gap = 0, spindle = 0, posts = -3, post = 0, newel = 0 });
            Assert.Equal((0.6, 0.02, 0.02, 0.01, 0.02, 0, 0.01), (small.H, small.W, small.T, small.Spindle, Math.Round(small.Gap, 9), small.Posts, small.Post));
            Assert.Equal(0.04, small.Off, 9);
        }

        public static IEnumerable<object[]> Looks()
        {
            var looks = new (string name, StairLook look)[]
            {
                ("chunky", new StairLook { railW = 0.15, railT = 0.12, gap = 0.2, spindle = 0.06, posts = 3, post = 0.12, newel = 0.2 }),
                ("posts as wide as the handrail", new StairLook { railW = 0.05, posts = 2, post = 0.05, spindle = 0.05, gap = 0.12 }),
                ("tall and thin", new StairLook { height = 1.6, railW = 0.02, railT = 0.02, gap = 0.06, spindle = 0.01 }),
                ("low", new StairLook { height = 0.6, railT = 0.3 }),
            };
            foreach (var (name, look) in looks)
                foreach (var (rails, solid) in new[] { (true, true), (true, false), (false, true) })
                    yield return new object[] { name, rails, solid, look };
        }

        [Theory, MemberData(nameof(Looks))]
        public void WhateverTheLookItIsCleanAndClosed(string name, bool rails, bool solid, StairLook look)
        {
            var (d, b, s) = Stack(look.Clone(), rails, solid);
            var report = CoplanarCheck.Run(Lod0.Build(new Site(d.buildings), b, solids: true).Op);
            Assert.True(report.Overlaps.Count == 0, $"{name}, rails {rails}, solid {solid}: {report.Overlaps.Count} coplanar overlaps:\n  " + string.Join("\n  ", report.Overlaps.Take(10)));
            var (holes, rays, where) = StairRailTests.Inside(d, b, s);
            Assert.True(rays > 2000);
            Assert.True(holes == 0, $"{name}, rails {rails}, solid {solid}: {holes} of {rays} rays meet a back face first:\n  " + string.Join("\n  ", where));
        }

        [Fact]
        public void ItIsKeptAsSetAndBaked()
        {
            var (d, b, s) = Stack(new StairLook { gap = 0.1, posts = 4, railColor = "#8B5A2B" });
            var json = PrototypeJson.Write(b);
            Assert.Contains("\"look\":{\"gap\":0.1,\"posts\":4,\"railColor\":\"#8B5A2B\"}", json.Replace(" ", "").Replace("\n", ""));
            var back = PrototypeJson.ReadBuilding(json).shafts[0].look!;
            Assert.Equal((0.1, 4, "#8B5A2B"), (back.gap!.Value, back.posts!.Value, back.railColor));
            Assert.Null(back.height); Assert.Null(back.sideColor);
            s.look = new StairLook { height = 1.1, railW = 0.05, railT = 0.06, gap = 0.12, spindle = 0.03, posts = 2, post = 0.06, newel = 0.15,
                railColor = "#111111", spindleColor = "#222222", postColor = "#333333", treadColor = "#444444", riserColor = "#555555", sideColor = "#666666" };
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].shafts[0].look!;
            foreach (var f in typeof(StairLook).GetFields()) Assert.Equal(f.GetValue(s.look), f.GetValue(baked));
            // a key it doesn't know is reported, as any other's; posts are whole
            var read = PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\",\"shafts\":[{\"id\":\"s\",\"type\":\"flight\",\"look\":{\"gap\":0.1,\"rot\":3}}]}]}");
            Assert.Contains(read.Unknown, u => u.EndsWith("look.rot"));
            Assert.Throws<FormatException>(() => PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\",\"shafts\":[{\"id\":\"s\",\"type\":\"flight\",\"look\":{\"posts\":2.5}}]}]}"));
        }

        static PaletteColor P(string id, string name, string hex) { var (r, g, bl) = PaletteMatch.Parse(hex); return new PaletteColor(id.PadLeft(32, '0'), name, r, g, bl); }

        [Fact]
        public void ThePaletteKeepsItsColoursAsAStylesOwn()
        {
            var palette = new List<PaletteColor> { P("1", "Oak", "#B8864B"), P("2", "Chalk", "#F3F2EE"), P("3", "Coal", "#2B2B2B") };
            var (d, b, s) = Stack(new StairLook { treadColor = "#B5884A", railColor = palette[2].id });
            d.palette[palette[2].id] = new PaletteEntry("Coal", "#2B2B2B");
            // a CSS colour on a stair is a literal to map, and a palette id it alone uses stays in the layout's palette block
            Assert.Contains("#B5884A", ColorMapping.Literals(d).Keys);
            Assert.Contains(palette[2].id, PaletteSnapshot.UsedIds(d));
            PaletteSnapshot.Refresh(d, id => null);
            Assert.True(d.palette.ContainsKey(palette[2].id));
            // kept inside the palette: the tread's CSS colour becomes the nearest entry
            PaletteConform.Apply(d, palette);
            Assert.Equal(palette[0].id, s.look!.treadColor);
            Assert.Equal(palette[2].id, s.look.railColor);
            Assert.Empty(ColorMapping.Literals(d).Keys.Where(k => k == "#B5884A"));
        }
    }
}
