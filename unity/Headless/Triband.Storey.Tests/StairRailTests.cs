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
    /// A straight flight's rails and solid flights (<see cref="CoreData.rails"/>, <see cref="CoreData.solid"/>; docs/EDITOR.md
    /// §5): a handrail on balusters up each side no wall stands against, from a newel at the flight's foot to one at its head,
    /// and each flight solid, filled down to its floor or, over the flight below, with a sloped underside that keeps that
    /// flight's headroom. The rails round the openings the flights come up through have balusters too, between newels on the
    /// openings' corners. The walk model and the collision mesh keep the player on a railed flight and out from under a solid
    /// one. Storey's own (not in the prototype), so these are its spec.
    /// </summary>
    public class StairRailTests
    {
        // a 12 × 9 m walk-in block of three storeys (3.6 m, 3 m, 3 m) under a flat roof, straight flights up to the roof from
        // the ground with their front at z 1.3 (rot 0: across x, up along z)
        /// <summary>
        /// The look straight flights had before their defaults were an artist's (spindles 13.5 cm apart, no posts, 13 cm newels,
        /// no feet nor bottom rail): what the tests of how each part is built count with. <paramref name="also"/> changes it.
        /// </summary>
        internal static StairLook Classic(Action<StairLook>? also = null)
        {
            var l = new StairLook { gap = 0.135, posts = 0, newel = 0.13, feet = false, bottom = false };
            also?.Invoke(l);
            return l;
        }

        internal static (StoreyDocument d, BuildingData b, CoreData s) Stack(double x = 6, bool rails = true, bool solid = true)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(12, 0), new Vec2(12, 9), new Vec2(0, 9) };
            b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList();
            b.interior = true; b.details.Clear(); b.shafts.Clear();
            var s = Shafts.Place(b, 0, new Vec2(x, 4.5), CoreType.Flight, 0, Shafts.NewId(d))!;
            s.rails = rails; s.solid = solid;
            return (d, b, s);
        }

        // the core's frame: the flight lane from −Hw to Xm across, its run from Z0 to Z1 along
        const double Hw = Dim.FLIGHT_W / 2, Xm = -Hw + Dim.FLIGHT_LANE, Z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, Z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;

        static int Steps(double fh) => Math.Max(8, (int)Math.Round(fh / 0.18));

        // the boxes built in the core's frame (Lod0.Build with solids)
        static List<Solid> Boxes(MeshBuilder op, Frame f) => op.Solids!.Where(x => !x.Mitred && x.Prism == null && x.F.O.x == f.O.x && x.F.O.z == f.O.z).ToList();
        static bool Baluster(Solid x) => Math.Abs(x.u1 - x.u0 - 0.025) < 1e-9 && Math.Abs(x.w1 - x.w0 - 0.025) < 1e-9;
        static bool Newel(Solid x) => Math.Abs(x.u1 - x.u0 - 0.13) < 1e-9 && Math.Abs(x.w1 - x.w0 - 0.13) < 1e-9;
        static bool At(double a, double b) => Math.Abs(a - b) < 1e-9;

        [Fact]
        public void ARailedFlightHasAHandrailOnBalustersUpEachOpenSideFromNewelToNewel()
        {
            var (d, b, s) = Stack(); s.look = Classic();
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var boxes = Boxes(op, f);
            for (int k = 0; k < 3; k++)
            {
                double y = Derived.FloorBase(b, k), fh = Derived.FloorH(b, k), up = Derived.FloorBase(b, k + 1); int n = Steps(fh);
                double run = (Z1 - Z0) / n, sl = fh / (Z1 - Z0);
                foreach (var (xe, o) in new[] { (-Hw, -1.0), (Xm, 1.0) })
                {
                    // 4 cm into the lane; two a tread (runs of 23 and 27 cm), each on its tread and into the handrail, whose
                    // top is 90 cm over the nosings' line
                    double xc = xe - o * 0.04;
                    var mine = boxes.Where(x => Baluster(x) && At((x.u0 + x.u1) / 2, xc) && x.y0 > y + 1e-6 && x.y0 <= up + 1e-6).ToList();
                    Assert.Equal(2 * n, mine.Count);
                    foreach (var x in mine)
                    {
                        double zc = (x.w0 + x.w1) / 2; int i = (int)Math.Floor((zc - Z0) / run);
                        Assert.Equal(y + (i + 1) * fh / n, x.y0, 9);
                        double top = y + (zc - Z0 + run) * sl + 0.9;
                        Assert.InRange(x.y1, top - 0.07 + 1e-6, top);
                    }
                    // a newel at its foot, on this storey's floor (the opening's corner's, but on the ground floor its own), and
                    // at its head, on the floor above (the roof, for the last)
                    Assert.Contains(boxes, x => Newel(x) && At((x.u0 + x.u1) / 2, xe) && At(x.w1, Z0) && At(x.y0, y));
                    Assert.Contains(boxes, x => Newel(x) && At((x.u0 + x.u1) / 2, xe) && At(x.w0, Z1) && Math.Abs(x.y0 - up) < 1e-6);
                    Assert.All(boxes.Where(Newel), x => Assert.Equal(Lod0.Shared.StairNewel, x.y1 - x.y0, 9));
                }
            }
        }

        [Fact]
        public void AWallStandsInForTheRailOnItsSide()
        {
            // the flight lane against the block's west wall: railed on the walkway's side alone, round the opening as up the flights
            var (d, b, s) = Stack(x: Dim.FLIGHT_W / 2); s.look = Classic();
            Assert.NotNull(Cores.CoreFlush(b.footprint, s, b: b)[0]);
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var boxes = Boxes(op, f);
            Assert.DoesNotContain(boxes, x => (Baluster(x) || Newel(x)) && (x.u0 + x.u1) / 2 < -Hw + 0.1);
            Assert.Contains(boxes, x => Baluster(x) && At((x.u0 + x.u1) / 2, Xm - 0.04));
            Assert.Contains(boxes, x => Baluster(x) && At((x.u0 + x.u1) / 2, Xm + 0.04));
            Assert.Contains(boxes, x => Newel(x) && At((x.u0 + x.u1) / 2, Xm));
        }

        [Fact]
        public void WithoutItsRailsTheOpeningsRailsArePlain()
        {
            // no balusters nor newels anywhere: round each opening (the first floor's, the second's, the roof's), on both sides,
            // a handrail and a post at each end, as before the flights had rails of their own
            var (d, b, s) = Stack(rails: false); s.look = Classic();
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            var boxes = Boxes(op, f);
            Assert.DoesNotContain(boxes, x => Baluster(x) || Newel(x));
            for (int k = 1; k <= 3; k++)
            {
                double y = Derived.FloorBase(b, k);
                foreach (double xc in new[] { -Hw - 0.04, Xm + 0.04 })
                {
                    Assert.Contains(boxes, x => At((x.u0 + x.u1) / 2, xc) && Math.Abs(x.y0 - (y + 0.95)) < 1e-6 && At(x.w0, Z0 + 0.04) && At(x.w1, Z1 - 0.04));
                    // on the roof, where nothing carries on up, the front rail's post at that end too
                    Assert.Equal(k == 3 ? 3 : 2, boxes.Count(x => At((x.u0 + x.u1) / 2, xc) && Math.Abs(x.y0 - y) < 1e-6 && Math.Abs(x.y1 - (y + Lod0.Shared.StairRail)) < 1e-6 && At(x.w1 - x.w0, 0.04)));
                }
            }
        }

        [Fact]
        public void ALevelRailHasBalustersUnderItsHandrailBetweenItsNewels()
        {
            // the first floor's opening, its walkway side: a baluster every 13.5 cm or so from the front newel to the back one
            var (d, b, s) = Stack(); s.look = Classic();
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; var f = Cores.FrameOf(b, s);
            double y = Derived.FloorBase(b, 1);
            var mine = Boxes(op, f).Where(x => Baluster(x) && At((x.u0 + x.u1) / 2, Xm + 0.04) && Math.Abs(x.y0 - y) < 1e-6).OrderBy(x => x.w0).ToList();
            Assert.Equal((int)Math.Round((Z1 - Z0) / 0.135), mine.Count);
            Assert.All(mine, x => { Assert.InRange(x.w0, Z0, Z1); Assert.Equal(y + Lod0.Shared.StairRail - 0.07, x.y1, 6); });
            for (int i = 1; i < mine.Count; i++) Assert.InRange(mine[i].w0 - mine[i - 1].w0, 0.13, 0.14);
            // across the roof's front, where nothing carries on up: between the two newels
            double top = Derived.FloorBase(b, 3);
            var front = Boxes(op, f).Where(x => Baluster(x) && At((x.w0 + x.w1) / 2, Z0 - 0.04) && Math.Abs(x.y0 - top) < 1e-6).ToList();
            Assert.NotEmpty(front);
            Assert.All(front, x => Assert.InRange(x.u0, -Hw + 0.065, Xm - 0.065));
        }

        // the triangles of a mesh in the core's frame: their corners (u, y, w), normal and colour
        static IEnumerable<(P3 a, P3 b, P3 c, P3 n, Swatch sw)> Tris(MeshBuilder m, Frame f)
        {
            P3 L(P3 p) { double dx = p.x - f.O.x, dz = p.z - f.O.z; return new P3(dx * f.u.x + dz * f.u.z, p.y, dx * f.w.x + dz * f.w.z); }
            for (int t = 0; t < m.Tris; t++)
            {
                int i = m.I[3 * t];
                var nn = m.N[i];
                yield return (L(m.P[i]), L(m.P[m.I[3 * t + 1]]), L(m.P[m.I[3 * t + 2]]), new P3(nn.x * f.u.x + nn.z * f.u.z, nn.y, nn.x * f.w.x + nn.z * f.w.z), m.C[i]);
            }
        }

        static double Area(P3 a, P3 b, P3 c) { var x = P3.Cross(b - a, c - a); return Math.Sqrt(x.x * x.x + x.y * x.y + x.z * x.z) / 2; }

        [Fact]
        public void TheGroundFloorsFlightIsFilledDownToItsFloor()
        {
            var (d, b, s) = Stack(rails: false);
            var op = Lod0.Build(new Site(d.buildings), b).Op; var f = Cores.FrameOf(b, s);
            double fh = Derived.FloorH(b, 0); int n = Steps(fh);
            // its side on the walkway, in the rooms' walls' colour: everything under the steps' profile
            var side = Tris(op, f).Where(t => t.sw.slot == ColorSlot.Interior && t.n.x > 0.99 && Math.Abs(t.a.x - Xm) < 1e-9 && Math.Abs(t.b.x - Xm) < 1e-9 && Math.Abs(t.c.x - Xm) < 1e-9 && t.a.y < fh + 1e-6 && t.b.y < fh + 1e-6 && t.c.y < fh + 1e-6).ToList();
            Assert.Equal((Z1 - Z0) * fh * (n + 1) / (2.0 * n), side.Sum(t => Area(t.a, t.b, t.c)), 6);
            Assert.All(side, t => Assert.True(Math.Min(t.a.y, Math.Min(t.b.y, t.c.y)) > -1e-9));
            // and its back, from the floor to the floor above
            var back = Tris(op, f).Where(t => t.sw.slot == ColorSlot.Interior && t.n.z > 0.99 && Math.Abs(t.a.z - Z1) < 1e-9 && t.a.x >= -Hw - 1e-9 && t.a.x <= Xm + 1e-9).ToList();
            Assert.Equal(Dim.FLIGHT_LANE * fh, back.Where(t => t.a.y < fh + 1e-6).Sum(t => Area(t.a, t.b, t.c)), 6);
        }

        [Fact]
        public void AFlightOverTheOneBelowKeepsItsHeadroom()
        {
            // the first floor's flight stands over the ground floor's, in its opening: its underside slopes with it, 20 cm under
            // its treads' back edges, more than 2 m over the nosings of the flight below everywhere
            var (d, b, s) = Stack(rails: false);
            var op = Lod0.Build(new Site(d.buildings), b).Op; var f = Cores.FrameOf(b, s);
            double y0 = Derived.FloorBase(b, 0), fh0 = Derived.FloorH(b, 0), y1 = Derived.FloorBase(b, 1), fh1 = Derived.FloorH(b, 1);
            double sl0 = fh0 / (Z1 - Z0), sl1 = fh1 / (Z1 - Z0), run0 = (Z1 - Z0) / Steps(fh0);
            double Soffit(double w) => y1 + (w - Z0) * sl1 - 0.2;
            // its own faces: in the rooms' walls' colour, in the lane, between its underside's foot and the floor above, and
            // not all at the floor or under it (the ground floor's flight's top)
            bool Mine((P3 a, P3 b, P3 c, P3 n, Swatch sw) t) => t.sw.slot == ColorSlot.Interior && new[] { t.a, t.b, t.c }.All(p => p.x >= -Hw - 1e-9 && p.x <= Xm + 1e-9 && p.y >= y1 - 0.2 - 1e-9 && p.y <= y1 + fh1 + 1e-6) && Math.Max(t.a.y, Math.Max(t.b.y, t.c.y)) > y1 + 0.01;
            var under = Tris(op, f).Where(t => Mine(t) && t.n.y < -0.5).ToList();
            Assert.Equal(Dim.FLIGHT_LANE * Math.Sqrt((Z1 - Z0) * (Z1 - Z0) + fh1 * fh1), under.Sum(t => Area(t.a, t.b, t.c)), 6);
            foreach (var t in under)
                foreach (var p in new[] { t.a, t.b, t.c })
                {
                    Assert.Equal(Soffit(p.z), p.y, 9);
                    Assert.True(p.y - (y0 + (p.z - Z0 + run0) * sl0) > 2.0, $"headroom {p.y - (y0 + (p.z - Z0 + run0) * sl0):0.00} m at w {p.z:0.00}");
                }
            // nothing of the flight's lower: it stands in the opening, not on the flight below
            var mine = Tris(op, f).Where(Mine).ToList();
            Assert.Contains(mine, t => t.n.x > 0.99);
            Assert.All(mine, t => Assert.All(new[] { t.a, t.b, t.c }, p => Assert.True(p.y >= Soffit(p.z) - 1e-9, $"({p.x:0.00}, {p.y:0.000}, {p.z:0.00}) under {Soffit(p.z):0.000}")));
        }

        public static IEnumerable<object[]> Layouts()
        {
            foreach (var (rails, solid) in new[] { (true, true), (true, false), (false, true), (false, false) })
                foreach (bool open in new[] { true, false })
                {
                    yield return new object[] { "middle", rails, solid, open };
                    yield return new object[] { "flight lane on a wall", rails, solid, open };
                    yield return new object[] { "one flight up, on a wall", rails, solid, open };
                }
        }

        static (StoreyDocument d, BuildingData b, CoreData s) Laid(string name, bool rails, bool solid, bool open = true)
        {
            if (name == "middle") { var m = Stack(rails: rails, solid: solid); m.s.openingRails = open; return m; }
            if (name == "flight lane on a wall") { var w = Stack(x: Dim.FLIGHT_W / 2, rails: rails, solid: solid); w.s.openingRails = open; return w; }
            // one flight up, the ground floor to the first, its lane's other side on the block's east wall
            var (d, b, s) = Stack(rails: rails, solid: solid);
            b.shafts.Clear();
            s = new CoreData { id = "f", type = CoreType.Flight, x = 12 - Xm, z = 4.5, rot = 0, bottom = 0, top = 1, roof = false, rails = rails, solid = solid, openingRails = open };
            b.shafts.Add(s);
            Assert.True(Cores.OneFlight(b, s));
            Assert.NotNull(Cores.CoreFlush(b.footprint, s, b: b)[1]);
            return (d, b, s);
        }

        [Theory, MemberData(nameof(Layouts))]
        public void TheGeometryIsCleanEitherWay(string name, bool rails, bool solid, bool open)
        {
            var (d, b, _) = Laid(name, rails, solid, open);
            var report = CoplanarCheck.Run(Lod0.Build(new Site(d.buildings), b, solids: true).Op);
            Assert.True(report.Overlaps.Count == 0, $"{name}, rails {rails}, solid {solid}, opening rails {open}: {report.Overlaps.Count} visible coplanar overlaps:\n  " + string.Join("\n  ", report.Overlaps.Take(10)));
        }

        /// <summary>
        /// Rays from eyes on each storey round the stairs meet no back face first: the flights, their rails and the openings are
        /// closed. As the player sees a storey: the cutaway clips everything over its ceiling, so a ray whose first hit is up
        /// there is let go (it sees the clipped solids' section caps, as meant).
        /// </summary>
        [Theory, MemberData(nameof(Layouts))]
        public void FromInsideNoRayMeetsTheBackOfAFace(string name, bool rails, bool solid, bool open)
        {
            var (d, b, s) = Laid(name, rails, solid, open);
            var (holes, rays, where) = Inside(d, b, s);
            Assert.True(rays > 2000, $"{rays} rays");   // a lone flight on the east wall has half the eyes round it
            Assert.True(holes == 0, $"{name}, rails {rails}, solid {solid}, opening rails {open}: {holes} of {rays} rays meet a back face first:\n  " + string.Join("\n  ", where));
        }

        /// <summary>Rays from eyes on each storey round the stairs <paramref name="s"/> of a 12 × 9 m block: how many meet a back face first, of how many.</summary>
        internal static (int holes, int rays, List<string> where) Inside(StoreyDocument d, BuildingData b, CoreData s)
        {
            var op = Lod0.Build(new Site(d.buildings), b).Op;
            var sc = new RayScene(); sc.Add(op.P, op.I, t => 0);
            var f = Cores.FrameOf(b, s); var rng = new Random(3); int holes = 0, rays = 0; var where = new List<string>();
            // clear of the rails and newels either side of the lane's edges and past its ends, as wide as the stairs' look makes them
            var rl = Railing.Of(s.look);
            double mu = Math.Max(rl.Newel / 2, rl.Off + Math.Max(rl.W, Math.Max(rl.Spindle, rl.Post)) / 2) + 0.06, mw = Math.Max(rl.Newel, rl.Off + rl.W / 2) + 0.1;
            for (int k = 0; k < 3; k++)
            {
                double yb = Derived.FloorBase(b, k), ceil = Derived.FloorBase(b, k + 1) - Dim.SLAB;
                for (int e = 0; e < 2500; e++)
                {
                    // an eye round the stairs, not over the flight lane (the flights, the opening) nor in a wall
                    double u = -Hw - 1.6 + rng.NextDouble() * (2 * Hw + 3.2), w = -4.0 + rng.NextDouble() * 8.0;
                    if (u > -Hw - mu && u < Xm + mu && w > Z0 - mw && w < Z1 + mw) continue;
                    var p = f.At2(u, w);
                    if (p.x < Dim.T_EXT + 0.1 || p.x > 12 - Dim.T_EXT - 0.1 || p.z < Dim.T_EXT + 0.1 || p.z > 9 - Dim.T_EXT - 0.1) continue;
                    if (Cores.OneFlight(b, s) && u > Xm) continue;   // a lone flight has no walkway: there is the floor round it, or the wall
                    double ey = yb + 0.3 + rng.NextDouble() * Math.Min(1.6, ceil - yb - 0.4);
                    double dx = rng.NextDouble() * 2 - 1, dy = rng.NextDouble() * 2 - 1, dz = rng.NextDouble() * 2 - 1, l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (l < 0.1) continue;
                    dx /= l; dy /= l; dz /= l;
                    int h = sc.Hit(p.x, ey, p.z, dx, dy, dz, out double th); if (h < 0) continue;
                    double hx = p.x + dx * th, hy = ey + dy * th, hz = p.z + dz * th;
                    if (hy > ceil) continue;   // clipped
                    rays++;
                    if (!sc.Back(h, dx, dy, dz)) continue;
                    ref var q = ref sc[h];
                    double len = Math.Sqrt(q.nx * q.nx + q.ny * q.ny + q.nz * q.nz);
                    if (Math.Abs((q.nx * dx + q.ny * dy + q.nz * dz) / len) < 0.1) continue;   // grazing
                    if (sc.FrontNear(hx, hy, hz, 0.05, dx, dy, dz)) continue;                    // its edge, seen aslant past the face it backs
                    holes++;
                    if (where.Count < 10) where.Add($"storey {k}: a back face at ({hx:0.000}, {hy:0.000}, {hz:0.000}), from ({p.x:0.00}, {ey:0.00}, {p.z:0.00})");
                }
            }
            return (holes, rays, where);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void TheWalkModelKeepsThePlayerOnARailedFlightAndOutFromUnderASolidOne(bool rails, bool solid)
        {
            var (d, b, s) = Stack(rails: rails, solid: solid);
            var world = new PlayWorld(new Site(d.buildings)); var f = Cores.FrameOf(b, s);
            double fh = Derived.FloorH(b, 0);
            (double u, double w) Local(double x, double z) { double dx = x - f.O.x, dz = z - f.O.z; return (dx * f.u.x + dz * f.u.z, dx * f.w.x + dz * f.w.z); }
            // halfway up the ground floor's flight, a step toward the walkway: kept on the flight where it's railed
            var on = f.At2(Xm - 0.05, 0); double yOn = world.SurfaceAt(on.x, on.z, fh / 2 + 0.1);
            Assert.Equal(fh / 2, yOn, 6);
            var (u1, _) = Local(world.Collide(on.x, on.z, yOn).x, world.Collide(on.x, on.z, yOn).z);
            var rl = Railing.Of(s.look);
            if (rails || solid) Assert.True(u1 <= Xm - rl.Off - rl.R - PlayWorld.Radius + 1e-6, $"on the flight at u {u1:0.000}");
            else Assert.Equal(Xm - 0.05, u1, 6);
            // walking on the floor in 5 cm steps, as a frame's are: from the walkway in under the flight's high end, kept out where
            // it's solid down to the floor (and where it's railed, which the walk model has as a wall all the way up); from behind
            // it, toward its back, kept out where it's solid
            (double u, double w) Walk(double u, double w, double du, double dw)
            {
                for (int i = 0; i < 40; i++) { var p = f.At2(u + du, w + dw); var q = world.Collide(p.x, p.z, 0.1); (u, w) = Local(q.x, q.z); }
                return (u, w);
            }
            var (u2, _) = Walk(Xm + 0.45, 1.8, -0.05, 0);
            if (rails || solid) Assert.Equal(Xm - rl.Off + rl.R + PlayWorld.Radius, u2, 6);
            else Assert.True(u2 < Xm - 0.5, $"under the flight at u {u2:0.000}");
            var (_, w3) = Walk(-Hw / 2, Z1 + 0.5, 0, -0.05);
            if (solid) Assert.Equal(Z1 + PlayWorld.Radius, w3, 6);
            else Assert.True(w3 < Z1 - 0.5, $"behind the flight at w {w3:0.000}");
        }

        static double? WallHit(CollisionMesh m, P3 o, P3 d)
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

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void TheColliderHasARailAsHighAsItsHandrailAndASolidFlightAsItIs(bool rails, bool solid)
        {
            var (d, b, s) = Stack(rails: rails, solid: solid);
            var site = new Site(d.buildings);
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b)); var f = Cores.FrameOf(b, s);
            double fh = Derived.FloorH(b, 0), rise = fh / Steps(fh);
            var dir = new P3(-f.u.x, 0, -f.u.z);
            P3 From(double w, double y) { var p = f.At2(Xm + 1, w); return new P3(p.x, y, p.z); }
            // from the walkway, toward the flight's middle: its handrail's top 90 cm over the nosings' line
            double top = fh / 2 + rise + 0.9;
            double face = 1 + Railing.Of(s.look).Off - Railing.Of(s.look).R;   // to the wall's face, at the rail's line less its half width
            double? low = WallHit(m, From(0, top - 0.05), dir), high = WallHit(m, From(0, top + 0.05), dir);
            if (rails) Assert.True(low is double l && Math.Abs(l - face) < 1e-6, $"under the handrail: {low}");
            Assert.True(high == null || high > 3, $"over the handrail: {high}");
            // low down under its high end: the solid's side, or the rail's wall from the floor
            double? under = WallHit(m, From(1.8, 0.3), dir);
            if (rails || solid) Assert.True(under is double u && Math.Abs(u - face) < 1e-6, $"under the flight: {under}");
            else Assert.True(under == null || under > 3, $"under the flight: {under}");
            // its back, from behind
            var bp = f.At2(-Hw / 2, Z1 + 1);
            double? back = WallHit(m, new P3(bp.x, 0.3, bp.z), new P3(-f.w.x, 0, -f.w.z));
            if (solid) Assert.True(back is double bk && Math.Abs(bk - 1) < 1e-6, $"behind the flight: {back}");
            else Assert.True(back == null || back > 3, $"behind the flight: {back}");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void WithoutTheOpeningsRailsOnlyTheFlightsHaveRails(bool slim)
        {
            // nothing round the openings but the newels the flights' handrails start and end in: no level handrail, no spindles
            // on the floor, no rail across the top's front; the flights' own as they were
            var (d, b, s) = Stack(); s.look = Classic(l => { if (slim) l.newel = 0.06; });
            var rl = Railing.Of(s.look); Assert.Equal(slim, rl.Slim);
            var f = Cores.FrameOf(b, s);
            var with = Lod0.Build(new Site(d.buildings), b, solids: true).Op;
            s.openingRails = false;
            var without = Lod0.Build(new Site(d.buildings), b, solids: true).Op;
            List<Solid> before = Boxes(with, f), boxes = Boxes(without, f);
            double Uc(Solid x) => (x.u0 + x.u1) / 2;
            double Wc(Solid x) => (x.w0 + x.w1) / 2;
            bool Tall(Solid x, double y) => At(x.y0, y) && x.y1 - x.y0 > 1 && At(x.u1 - x.u0, rl.Newel) && At(x.w1 - x.w0, rl.Newel);
            int N = b.floors.Count;   // the roof's opening is the top one, where no flight carries on
            Assert.True(Cores.StairHoleAt(b, s, N) && !Cores.HasFlight(b, s, N));
            for (int k = 1; k <= N; k++)
            {
                double y = Derived.FloorBase(b, k);
                bool Rail(Solid x) => At(x.y1, y + rl.H) && At(x.y1 - x.y0, rl.T);
                bool Spindle(Solid x) => Baluster(x) && At(x.y0, y) && (At(Uc(x), -Hw - rl.Off) || At(Uc(x), Xm + rl.Off) || At(Wc(x), Z0 - rl.Off));
                Assert.Contains(before, Rail); Assert.Contains(before, Spindle);
                Assert.DoesNotContain(boxes, Rail); Assert.DoesNotContain(boxes, Spindle);
                foreach (var (xe, o) in new[] { (-Hw, -1.0), (Xm, 1.0) })
                {
                    if (!slim)
                    {
                        // across the lane's edge: the flight coming up ends in the one at the back; the one going on up starts in
                        // the one at the front, which the top has none of
                        Assert.Contains(boxes, x => Tall(x, y) && At(Uc(x), xe) && At(x.w0, Z1));
                        Assert.Equal(k < N, boxes.Any(x => Tall(x, y) && At(Uc(x), xe) && At(x.w1, Z0)));
                    }
                    else
                    {
                        // the flight coming up's own, on its line; none on the opening's
                        Assert.Contains(boxes, x => Tall(x, y) && At(Uc(x), xe - o * rl.Off) && At(Wc(x), Z1 + rl.Off));
                        Assert.Contains(before, x => Tall(x, y) && At(Uc(x), xe + o * rl.Off));
                        Assert.DoesNotContain(boxes, x => Tall(x, y) && At(Uc(x), xe + o * rl.Off));
                    }
                }
            }
            // the flights' handrails and spindles as they were
            List<string> Sloped(MeshBuilder op) => Enumerable.Range(0, op.Tris).Where(t => op.N[op.I[3 * t]].y > 0.5 && op.N[op.I[3 * t]].y < 0.99)
                .Select(t => $"{op.P[op.I[3 * t]]} {op.P[op.I[3 * t + 1]]} {op.P[op.I[3 * t + 2]]}").OrderBy(x => x).ToList();
            Assert.NotEmpty(Sloped(without)); Assert.Equal(Sloped(with), Sloped(without));
            List<string> Flights(List<Solid> bs) => bs.Where(x => Baluster(x) && (At(Uc(x), -Hw + rl.Off) || At(Uc(x), Xm - rl.Off)))
                .Select(x => $"{x.u0:F6} {x.y0:F6} {x.y1:F6} {x.w0:F6}").OrderBy(x => x).ToList();
            Assert.NotEmpty(Flights(boxes)); Assert.Equal(Flights(before), Flights(boxes));
            // without the flights' rails too, no rail anywhere; with them off alone, the openings' plain ones
            bool RailColour(Swatch c) => KindColors.RefOf(c.slot) is Railing.HandrailColour or Railing.UprightColour;
            s.rails = false;
            var none = Lod0.Build(new Site(d.buildings), b).Op;
            Assert.DoesNotContain(Enumerable.Range(0, none.Verts), v => RailColour(none.C[v]));
            s.openingRails = true;
            var plain = Lod0.Build(new Site(d.buildings), b).Op;
            Assert.Contains(Enumerable.Range(0, plain.Verts), v => RailColour(plain.C[v]));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void WithoutTheOpeningsRailsNobodyWalksIntoAnOpening(bool rails)
        {
            // their edges are walls to the walk model as the rails were, as high, so the collider has them too: only the newels
            // left out take their own with them
            var (d, b, s) = Stack(rails: rails);
            var with = Lod0.Build(new Site(d.buildings), b).Segs;
            s.openingRails = false;
            var site = new Site(d.buildings);
            var without = Lod0.Build(site, b).Segs;
            string Key(Seg g) => $"{g.ax:F6},{g.az:F6},{g.bx:F6},{g.bz:F6},{g.r:F6},{g.h:F6}";
            for (int k = 0; k < with.Count; k++)
            {
                var kept = without[k].Select(Key).ToHashSet();
                Assert.All(without[k], g => Assert.Contains(Key(g), with[k].Select(Key)));
                Assert.All(with[k].Where(g => !kept.Contains(Key(g))), g => Assert.True(g.ax == g.bx && g.az == g.bz, $"storey {k}: {Key(g)} gone"));
            }
            // on the roof, where nothing else stops it: walking from the walkway toward the opening, and from its front
            var world = new PlayWorld(site); var f = Cores.FrameOf(b, s); var rl = Railing.Of(s.look);
            double yr = Derived.FloorBase(b, b.floors.Count) + 0.1;
            (double u, double w) Local(double x, double z) { double dx = x - f.O.x, dz = z - f.O.z; return (dx * f.u.x + dz * f.u.z, dx * f.w.x + dz * f.w.z); }
            (double u, double w) Walk(double u, double w, double du, double dw)
            {
                for (int i = 0; i < 40; i++) { var p = f.At2(u + du, w + dw); var q = world.Collide(p.x, p.z, yr); (u, w) = Local(q.x, q.z); }
                return (u, w);
            }
            Assert.Equal(Xm + rl.Off + rl.R + PlayWorld.Radius, Walk(Xm + 0.6, 0, -0.05, 0).u, 6);
            Assert.Equal(Z0 - rl.Off - rl.R - PlayWorld.Radius, Walk(-Hw / 2, Z0 - 0.6, 0, 0.05).w, 6);
            // the collider's wall there, from the walkway: under the rails' height, none over it
            var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
            var p0 = f.At2(Xm + 1, 0); var dir = new P3(-f.u.x, 0, -f.u.z);
            double? low = WallHit(m, new P3(p0.x, yr - 0.1 + rl.H - 0.05, p0.z), dir), high = WallHit(m, new P3(p0.x, yr - 0.1 + rl.H + 0.05, p0.z), dir);
            Assert.True(low is double l && Math.Abs(l - (1 - rl.Off - rl.R)) < 1e-6, $"under the rails' height: {low}");
            Assert.True(high == null || high > 3, $"over it: {high}");
        }

        [Fact]
        public void TheyAreKeptOnlyWhenOff()
        {
            // a layout's stairs have them all unless it says otherwise, and it says so only for one that's off
            var (d, b, s) = Stack();
            Assert.DoesNotContain("\"rails\"", PrototypeJson.Write(b));
            Assert.DoesNotContain("\"openingRails\"", PrototypeJson.Write(b));
            Assert.DoesNotContain("\"solid\"", PrototypeJson.Write(b));
            s.rails = false;
            var back = PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).shafts[0];
            Assert.False(back.rails); Assert.True(back.openingRails); Assert.True(back.solid);
            s.rails = true; s.solid = false;
            back = PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).shafts[0];
            Assert.True(back.rails); Assert.True(back.openingRails); Assert.False(back.solid);
            s.solid = true; s.openingRails = false;
            Assert.Contains("\"openingRails\":false", PrototypeJson.Write(b).Replace(" ", ""));
            back = PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).shafts[0];
            Assert.True(back.rails); Assert.False(back.openingRails); Assert.True(back.solid);
            // and baked
            foreach (var (r, o, so) in new[] { (false, true, true), (true, false, true), (true, true, false) })
            {
                s.rails = r; s.openingRails = o; s.solid = so;
                var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].shafts[0];
                Assert.Equal((r, o, so), (baked.rails, baked.openingRails, baked.solid));
            }
            var read = PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\",\"shafts\":[{\"id\":\"s\",\"type\":\"flight\"}]}]}");
            var rs = read.Document.buildings[0].shafts[0];
            Assert.True(rs.rails && rs.openingRails && rs.solid);
            Assert.Empty(read.Unknown);
        }
    }
}
