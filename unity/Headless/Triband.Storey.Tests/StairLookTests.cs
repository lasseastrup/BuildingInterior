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
        public void WithoutALookItHasTheDefaults()
        {
            // an artist's: spindles 19 cm apart, a post between every 4, 14 cm newels on feet 17 × 8 cm, a bottom rail 6.5 cm up,
            // the handrail #6D6D6D and the spindles, posts and newels #444445; the rest as first set
            var rl = Railing.Of(null);
            Assert.Equal((Lod0.Shared.StairRail, 0.04, 0.07, 0.19, 0.025, 4, 0.05, 0.14, 0.045, 0.04), (rl.H, rl.W, rl.T, rl.Gap, rl.Spindle, rl.Posts, rl.Post, Math.Round(rl.Newel, 9), Math.Round(rl.Off, 9), rl.R));
            Assert.Equal(0.9, rl.FlightH, 9); Assert.Equal(Lod0.Shared.StairNewel, rl.NewelH, 9);
            Assert.Equal((Railing.Above, 0.17, 0.08, true, 0.065, 0.04, 0.04, false), (rl.NewelAbove, rl.FootW, rl.FootH, rl.Bottom, rl.BottomGap, rl.BottomW, rl.BottomT, rl.Slim));
            var (d, b, s) = Stack(null);
            var op = Lod0.Build(new Site(d.buildings), b).Op;
            var refs = Enumerable.Range(0, op.Tris).Select(t => KindColors.RefOf(op.C[op.I[3 * t]].slot)).ToHashSet();
            Assert.Contains(Railing.HandrailColour, refs); Assert.Contains(Railing.UprightColour, refs);
            Assert.DoesNotContain(Enumerable.Range(0, op.Tris), t => op.C[op.I[3 * t]].slot == ColorSlot.Rail);
            // a look with nothing set builds the same as none, and is written as none
            string Built(StairLook? look)
            {
                var (d1, b1, _) = Stack(look);
                var r = Lod0.Build(new Site(d1.buildings), b1);
                return string.Join(";", Enumerable.Range(0, r.Op.I.Count).Select(i => $"{r.Op.P[r.Op.I[i]]}{r.Op.C[r.Op.I[i]].slot}")) + string.Join(";", r.Segs.SelectMany(g => g).Select(g => $"{g.ax},{g.az},{g.bx},{g.bz},{g.r},{g.h}"));
            }
            Assert.Equal(Built(null), Built(new StairLook()));
            var (_, b2, _) = Stack(new StairLook());
            Assert.DoesNotContain("\"look\"", PrototypeJson.Write(b2));
            // and a part's off is kept: no posts, no feet, no bottom rail
            var off = new StairLook { posts = 0, feet = false, bottom = false };
            var ro = Railing.Of(off);
            Assert.Equal((0, 0.0, false), (ro.Posts, ro.FootW, ro.Bottom));
            Assert.Contains("\"look\":{\"posts\":0,\"feet\":false,\"bottom\":false}", PrototypeJson.Write(Stack(off).b).Replace(" ", "").Replace("\n", ""));
        }

        [Fact]
        public void SpindlesAreAsFarApartAndAsThickAsSet()
        {
            var (d, b, s) = Stack(StairRailTests.Classic(l => { l.gap = 0.1; l.spindle = 0.02; }));
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
            var look = StairRailTests.Classic(l => { l.posts = 4; l.post = 0.07; l.newel = null; });
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
            var look = StairRailTests.Classic(l => { l.height = 1.2; l.railW = 0.08; l.railT = 0.05; l.newel = null; });
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
            var big = Railing.Of(new StairLook { height = 10, railW = 1, railT = 5, gap = 0.001, spindle = 0.5, posts = 500, post = 9, newel = 0.01, feet = false });
            Assert.Equal((2.0, 0.2, 0.3, 0.1, 0.11, 100, 0.2), (big.H, big.W, big.T, big.Spindle, Math.Round(big.Gap, 9), big.Posts, big.Post));
            // a newel asked narrower than both rails is slim, a centimetre wider than its own rail, its rail's line off the edge for it
            Assert.True(big.Slim); Assert.Equal(0.21, big.Newel, 9); Assert.Equal(0.125, big.Off, 9);
            // left alone, it grows to take both rails, past its own most
            var wide = Railing.Of(new StairLook { railW = 0.2, spindle = 0.1, posts = 2, post = 0.2 });
            Assert.False(wide.Slim); Assert.Equal(0.12, wide.Off, 9); Assert.Equal(2 * (wide.Off + wide.W / 2) + 0.01, wide.Newel, 9);
            var small = Railing.Of(new StairLook { height = 0.1, railW = 0, railT = 0, gap = 0, spindle = 0, posts = -3, post = 0, newel = 0, feet = false });
            Assert.Equal((0.6, 0.02, 0.02, 0.01, 0.02, 0, 0.01), (small.H, small.W, small.T, small.Spindle, Math.Round(small.Gap, 9), small.Posts, small.Post));
            Assert.True(small.Slim); Assert.Equal(0.03, small.Newel, 9);   // a slim newel at least a centimetre wider than its handrail
            Assert.Equal(0.04, small.Off, 9);
            // a bottom rail at least 10 cm under the flights' handrail; feet wider than the newels; newels at most 60 cm over
            var odd = Railing.Of(new StairLook { bottomGap = 5, footW = 0.01, newelAbove = 2 });
            Assert.Equal(1.02 - Railing.Below - 0.07 - 0.04 - 0.1, odd.BottomGap, 9);
            Assert.Equal(odd.Newel + 0.01, odd.FootW, 9);
            Assert.Equal(0.6, odd.NewelAbove, 9);
        }

        /// <summary>A look like the artist's (their image of a straight flight railed on one side): slim newels flush with the handrail, on feet, a bottom rail, a spindle a step with a post after every five, the steps' ends a grey over the white wall under them.</summary>
        internal static StairLook Artist() => new StairLook
        {
            height = 1.0, railW = 0.06, railT = 0.05, gap = 0.28, spindle = 0.025, posts = 5, post = 0.045, newel = 0.07, newelAbove = 0, footW = 0.11, footH = 0.07,
            bottomGap = 0.06, bottomW = 0.04, bottomT = 0.04, railColor = "#5A5E61", spindleColor = "#5A5E61", postColor = "#5A5E61", bottomColor = "#5A5E61",
            treadColor = "#CFCFCC", riserColor = "#C4C4C1", endColor = "#BDBDBA", sideColor = "#E9E9E6",
        };

        public static IEnumerable<object[]> Looks()
        {
            var looks = new (string name, StairLook? look)[]
            {
                ("the defaults", null),
                ("the classic", StairRailTests.Classic()),
                ("the artist's", Artist()),
                ("bottom rail and feet", new StairLook { bottomGap = 0.1, bottomW = 0.05, bottomT = 0.05, footW = 0.25, footH = 0.12 }),
                ("slim and flush", new StairLook { newel = 0.05, newelAbove = 0, footW = 0.08, footH = 0.05, bottomGap = 0.05, posts = 3 }),
                ("step ends", new StairLook { endColor = "#BDBDBA" }),
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
        public void WhateverTheLookItIsCleanAndClosed(string name, bool rails, bool solid, StairLook? look)
        {
            var (d, b, s) = Stack(look?.Clone(), rails, solid);
            var report = CoplanarCheck.Run(Lod0.Build(new Site(d.buildings), b, solids: true).Op);
            Assert.True(report.Overlaps.Count == 0, $"{name}, rails {rails}, solid {solid}: {report.Overlaps.Count} coplanar overlaps:\n  " + string.Join("\n  ", report.Overlaps.Take(10)));
            var (holes, rays, where) = StairRailTests.Inside(d, b, s);
            Assert.True(rays > 2000);
            Assert.True(holes == 0, $"{name}, rails {rails}, solid {solid}: {holes} of {rays} rays meet a back face first:\n  " + string.Join("\n  ", where));
        }

        [Fact]
        public void ABottomRailCarriesTheSpindles()
        {
            var (d, b, s) = Stack(StairRailTests.Classic(l => { l.bottom = null; l.bottomGap = 0.08; }));
            var rl = Railing.Of(s.look);
            Assert.Equal((true, 0.08, 0.04, 0.04), (rl.Bottom, rl.BottomGap, rl.BottomW, rl.BottomT));   // as wide as the handrail
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var boxes = Boxes(op, f);
            // round the first floor's opening: 8 cm over the floor, the spindles on it, up to the handrail
            double y1 = Derived.FloorBase(b, 1), xc = Xm + 0.04;
            Assert.Contains(boxes, x => At(x.u0, xc - 0.02) && At(x.u1, xc + 0.02) && At(x.y0, y1 + 0.08) && At(x.y1, y1 + 0.12) && At(x.w0, Z0) && At(x.w1, Z1));
            var level = boxes.Where(x => Square(x, 0.025) && At((x.u0 + x.u1) / 2, xc) && x.y0 > y1 - 1e-6 && x.y0 < y1 + 0.5).ToList();
            Assert.NotEmpty(level);
            Assert.All(level, x => { Assert.Equal(y1 + 0.12, x.y0, 6); Assert.Equal(y1 + 0.95, x.y1, 6); });
            // up the ground floor's flight: the spindles' feet a centimetre into its top, 8 cm over the nosings' line
            double fh = Derived.FloorH(b, 0), run = (Z1 - Z0) / Steps(fh), sl = fh / (Z1 - Z0);
            var up = boxes.Where(x => Square(x, 0.025) && At((x.u0 + x.u1) / 2, Xm - 0.04) && x.y0 > -1 && x.y1 < y1 + 1).ToList();
            Assert.NotEmpty(up);
            Assert.All(up, x => { double zc = (x.w0 + x.w1) / 2; Assert.Equal((zc - Z0 + run) * sl + 0.12 - 0.0125 * sl - 0.01, x.y0, 6); });
            // none without one
            var (d2, b2, s2) = Stack(StairRailTests.Classic());
            Assert.DoesNotContain(Boxes(Lod0.Build(new Site(d2.buildings), b2, solids: true).Op, f), x => At(x.y1 - x.y0, 0.04) && At(x.w0, Z0) && At(x.w1, Z1));
        }

        [Fact]
        public void NewelsStandAsHighAsSetOverTheHandrails()
        {
            // flush: as high as the highest handrail ending in them, a flight's 90 cm over its nosings' line, the openings' 1.02 m
            var (d, b, s) = Stack(StairRailTests.Classic(l => l.newelAbove = 0));
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var newels = Boxes(op, f).Where(x => Square(x, 0.13)).ToList();
            double Rise(int k) => Derived.FloorH(b, k) / Steps(Derived.FloorH(b, k));
            double Top(double y0, double w) => newels.Single(x => At(x.y0, y0) && At((x.u0 + x.u1) / 2, Xm) && x.w0 < w && x.w1 > w).y1 - y0;
            double y1 = Derived.FloorBase(b, 1), y3 = Derived.FloorBase(b, 3);
            Assert.Equal(Rise(0) + 0.9, Top(0, Z0 - 0.06), 6);    // the ground floor's flight's foot
            Assert.Equal(Rise(0) + 0.9, Top(y1, Z1 + 0.06), 6);   // its head, on the first floor
            Assert.Equal(Rise(1) + 0.9, Top(y1, Z0 - 0.06), 6);   // the first floor's flight's foot
            Assert.Equal(1.02, Top(y3, Z0 - 0.06), 6);            // the roof's front corner: no flight goes on up
            // the default, 16 cm over the rails' height
            var (d2, b2, s2) = Stack(null);
            var tall = Boxes(Lod0.Build(new Site(d2.buildings), b2, solids: true).Op, f).Where(x => Square(x, 0.14)).ToList();
            Assert.NotEmpty(tall);
            Assert.All(tall, x => Assert.Equal(1.18, x.y1 - x.y0, 6));
        }

        [Fact]
        public void NewelsCanStandOnFeet()
        {
            var (d, b, s) = Stack(StairRailTests.Classic(l => { l.feet = null; l.footW = 0.2; l.footH = 0.1; }));
            var r = Lod0.Build(new Site(d.buildings), b, solids: true); var f = Cores.FrameOf(b, s);
            var boxes = Boxes(r.Op, f);
            var newels = boxes.Where(x => Square(x, 0.13)).ToList();
            Assert.NotEmpty(newels);
            // 20 cm across, as far along on its far side, cut at its face toward the flight lane's run (not into the first step,
            // nor out over an opening)
            Assert.All(newels, nw => Assert.Contains(boxes, x => At(x.u1 - x.u0, 0.2) && At(x.y0, nw.y0) && At(x.y1, nw.y0 + 0.1) && At(x.u0 + x.u1, nw.u0 + nw.u1)
                && (nw.w1 <= Z0 + 1e-9 ? At(x.w1, nw.w1) && At(x.w0, (nw.w0 + nw.w1) / 2 - 0.1) : At(x.w0, nw.w0) && At(x.w1, (nw.w0 + nw.w1) / 2 + 0.1))));
            // the walk model goes round the foot
            var c = f.At2(Xm, Z0 - 0.065);
            Assert.Contains(r.Segs[0], g => At(g.ax, c.x) && At(g.az, c.z) && At(g.bx, c.x) && At(g.r, 0.115));
        }

        [Fact]
        public void SlimNewelsGiveEachRailItsOwn()
        {
            // narrower than the two rails side by side: each rail's own, on its line, where the line across the front or the back
            // crosses it, its handrail running on to them
            var (d, b, s) = Stack(StairRailTests.Classic(l => l.newel = 0.06));
            var rl = Railing.Of(s.look);
            Assert.True(rl.Slim); Assert.Equal(0.06, rl.Newel, 9); Assert.Equal(0.05, rl.Off, 9);
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var boxes = Boxes(op, f);
            Assert.DoesNotContain(boxes, x => Square(x, 0.13));
            bool Post(double u, double w, double y0) => boxes.Any(x => Square(x, 0.06) && At((x.u0 + x.u1) / 2, u) && At((x.w0 + x.w1) / 2, w) && At(x.y0, y0));
            double y1 = Derived.FloorBase(b, 1);
            Assert.True(Post(Xm - 0.05, Z0 - 0.05, 0), "the ground floor's flight's foot");
            Assert.True(Post(Xm + 0.05, Z0 - 0.05, y1) && Post(Xm + 0.05, Z1 + 0.05, y1), "the first floor's opening's own");
            Assert.True(Post(Xm - 0.05, Z1 + 0.05, y1), "the ground floor's flight's head");
            Assert.True(Post(Xm - 0.05, Z0 - 0.05, y1), "the first floor's flight's foot");
            // the opening's handrail between its own
            Assert.Contains(boxes, x => At((x.u0 + x.u1) / 2, Xm + 0.05) && At(x.y1, y1 + 1.02) && At(x.w0, Z0 - 0.02) && At(x.w1, Z1 + 0.02));
            // a flight's handrail from its foot's to its head's
            var tops = Enumerable.Range(0, op.Tris).Where(t => op.N[op.I[3 * t]].y > 0.5 && op.N[op.I[3 * t]].y < 0.99 && KindColors.RefOf(op.C[op.I[3 * t]].slot) == Railing.HandrailColour)
                .SelectMany(t => new[] { op.P[op.I[3 * t]], op.P[op.I[3 * t + 1]], op.P[op.I[3 * t + 2]] })
                .Where(p => p.y < y1 + 1.2 && Math.Abs((p.x - f.O.x) * f.u.x + (p.z - f.O.z) * f.u.z - (Xm - 0.05)) < 0.021)
                .Select(p => (p.x - f.O.x) * f.w.x + (p.z - f.O.z) * f.w.z).ToList();
            Assert.Equal(Z0 - 0.02, tops.Min(), 6); Assert.Equal(Z1 + 0.02, tops.Max(), 6);
        }

        [Fact]
        public void TheStepsEndsCanHaveAColourOfTheirOwn()
        {
            // 20 cm under each tread (the first's down to the floor), as steps in the air have them, over the side's own colour
            var look = new StairLook { endColor = "#BDBDBA", sideColor = "#E9E9E6" };
            var (d, b, s) = Stack(look, rails: false);
            var op = Lod0.Build(new Site(d.buildings), b).Op; var f = Cores.FrameOf(b, s);
            // the walkway's side's faces in a colour, of storey k's flight: between its nosings' line and its floor or underside
            double Area(int k, string colour)
            {
                double y = Derived.FloorBase(b, k), h = Derived.FloorH(b, k), sl = h / (Z1 - Z0), rn = (Z1 - Z0) / Steps(h), a = 0;
                for (int t = 0; t < op.Tris; t++)
                {
                    if (KindColors.RefOf(op.C[op.I[3 * t]].slot) != colour) continue;
                    var nn = op.N[op.I[3 * t]]; if (nn.x * f.u.x + nn.z * f.u.z < 0.99) continue;
                    P3 A = op.P[op.I[3 * t]], B = op.P[op.I[3 * t + 1]], C = op.P[op.I[3 * t + 2]];
                    var m = (A + B + C) * (1.0 / 3); double w = (m.x - f.O.x) * f.w.x + (m.z - f.O.z) * f.w.z;
                    if (m.y > y + (w - Z0 + rn) * sl + 1e-6 || m.y < (k == 0 ? y : y + (w - Z0) * sl - 0.2) - 1e-6) continue;
                    var x = P3.Cross(B - A, C - A); a += Math.Sqrt(x.x * x.x + x.y * x.y + x.z * x.z) / 2;
                }
                return a;
            }
            // the ground floor's, on its floor
            double fh = Derived.FloorH(b, 0); int n = Steps(fh); double run = (Z1 - Z0) / n, rise = fh / n;
            Assert.Equal(run * (rise + 0.2 * (n - 1)), Area(0, look.endColor), 6);
            Assert.Equal((Z1 - Z0) * fh * (n + 1) / (2.0 * n) - run * (rise + 0.2 * (n - 1)), Area(0, look.sideColor), 6);
            // the first floor's, over the flight below: under each step a wedge down to its underside
            double y1 = Derived.FloorBase(b, 1), fh1 = Derived.FloorH(b, 1); int n1 = Steps(fh1); double run1 = (Z1 - Z0) / n1, rise1 = fh1 / n1;
            Assert.Equal(n1 * run1 * 0.2, Area(1, look.endColor), 6);
            Assert.Equal(n1 * run1 * rise1 / 2, Area(1, look.sideColor), 6);
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
                newelAbove = 0.05, feet = true, footW = 0.2, footH = 0.07, bottom = true, bottomGap = 0.07, bottomW = 0.03, bottomT = 0.035,
                railColor = "#111111", spindleColor = "#222222", postColor = "#333333", treadColor = "#444444", riserColor = "#555555", sideColor = "#666666",
                bottomColor = "#777777", endColor = "#888888" };
            Assert.All(typeof(StairLook).GetFields(), x => Assert.NotNull(x.GetValue(s.look)));   // every one set
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
