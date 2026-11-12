using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// An artist's window seen from inside (<see cref="KindPart.Backs"/>). One made for the street alone, its frame's members
    /// open behind as the converted windows' are, is closed from behind where a storey's rooms see it: at LOD0, on a storey
    /// with rooms, over its hole. Shell storeys, LOD1 and what lies round the hole are as the artist made them. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class OpeningBackTests
    {
        readonly ITestOutputHelper output;
        public OpeningBackTests(ITestOutputHelper o) { output = o; }

        /// <summary>
        /// A window 1 × 1.5 m made for the street alone: a frame of four members 6 cm wide, from 2 cm proud of the wall to 6 cm
        /// into it, each its front, its side to the glass and its outer side where it stands proud (the bottom one's rests on
        /// the sill), and open behind, but the top one, closed behind (a face turned to the rooms); a glazing bar the same, in
        /// front of the glass; the glass 3.5 cm in; and a metal sill under the hole, round it. 30 frame triangles, 28 of them
        /// seen from behind by the rooms: all but the top member's back, turned to them (the rest of that member they see
        /// from behind through its open ends, from inside the side members); 6 sill triangles; 2 of glass.
        /// </summary>
        internal static OpeningKind StreetOnly(string id, bool glass = true)
        {
            var P = new List<P3>(); var N = new List<P3>(); var frame = new List<int>(); var sill = new List<int>(); var pane = new List<int>();
            // a quad, its normal its winding's
            void Q(List<int> into, P3 a, P3 b, P3 c, P3 d)
            {
                var n = P3.Cross(b - a, c - a).Normalized; int s = P.Count;
                P.AddRange(new[] { a, b, c, d }); N.AddRange(new[] { n, n, n, n });
                into.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            void Front(List<int> into, double x0, double y0, double x1, double y1, double z) => Q(into, new P3(x0, y0, z), new P3(x1, y0, z), new P3(x1, y1, z), new P3(x0, y1, z));
            const double w = 1, h = 1.5, f = 0.06, zf = 0.02, zb = -0.06, xl = -w / 2 + f, xr = w / 2 - f;
            // the members' fronts, turned to the street
            Front(frame, -w / 2, 0, xl, h, zf); Front(frame, xr, 0, w / 2, h, zf); Front(frame, xl, 0, xr, f, zf); Front(frame, xl, h - f, xr, h, zf);
            // their sides to the glass, from their fronts in
            Q(frame, new P3(xl, f, zf), new P3(xl, f, zb), new P3(xl, h - f, zb), new P3(xl, h - f, zf));   // +x
            Q(frame, new P3(xr, f, zb), new P3(xr, f, zf), new P3(xr, h - f, zf), new P3(xr, h - f, zb));   // −x
            Q(frame, new P3(xl, f, zf), new P3(xr, f, zf), new P3(xr, f, zb), new P3(xl, f, zb));           // +y
            Q(frame, new P3(xl, h - f, zb), new P3(xr, h - f, zb), new P3(xr, h - f, zf), new P3(xl, h - f, zf));   // −y
            // their outer sides, where they stand proud of the wall (in it, the wall's reveal covers them)
            Q(frame, new P3(-w / 2, 0, 0), new P3(-w / 2, 0, zf), new P3(-w / 2, h, zf), new P3(-w / 2, h, 0));   // −x
            Q(frame, new P3(w / 2, 0, zf), new P3(w / 2, 0, 0), new P3(w / 2, h, 0), new P3(w / 2, h, zf));       // +x
            Q(frame, new P3(-w / 2, h, zf), new P3(w / 2, h, zf), new P3(w / 2, h, 0), new P3(-w / 2, h, 0));     // +y
            // the top member's back, turned to the rooms
            Q(frame, new P3(xr, h - f, zb), new P3(xl, h - f, zb), new P3(xl, h, zb), new P3(xr, h, zb));
            // the glazing bar: its front and its sides
            Front(frame, -0.015, f, 0.015, h - f, -0.01);
            Q(frame, new P3(-0.015, f, -0.03), new P3(-0.015, f, -0.01), new P3(-0.015, h - f, -0.01), new P3(-0.015, h - f, -0.03));   // −x
            Q(frame, new P3(0.015, f, -0.01), new P3(0.015, f, -0.03), new P3(0.015, h - f, -0.03), new P3(0.015, h - f, -0.01));       // +x
            Front(pane, xl, f, xr, h - f, -0.035);
            // the sill under the hole: its top, its front and its underside
            Q(sill, new P3(-0.55, 0, 0.08), new P3(0.55, 0, 0.08), new P3(0.55, 0, 0), new P3(-0.55, 0, 0));
            Front(sill, -0.55, -0.05, 0.55, 0, 0.08);
            Q(sill, new P3(-0.55, -0.05, 0), new P3(0.55, -0.05, 0), new P3(0.55, -0.05, 0.08), new P3(-0.55, -0.05, 0.08));
            var m = new OpeningKind.Mesh { P = P.ToArray(), N = N.ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = frame.ToArray() });
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Metal, tris = sill.ToArray() });
            if (glass) m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, tris = pane.ToArray() });
            return new OpeningKind { id = id, name = id, w = w, h = h, lod0 = m };
        }

        const int FrameTris = 30, Backed = 28, SillTris = 6;

        /// <summary>A 14 × 10 m brick block, its windows <paramref name="kind"/>; walked in where it has <paramref name="interior"/>.</summary>
        static (BuildingData b, Site site) Block(bool interior, string kind)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.interior = interior; b.style = Styles.Preset("brick").Clone(); b.style.ground = GroundType.Match; b.style.windowKind = kind;
            return (b, new Site(d.buildings));
        }

        static ColorSlot Slot(MeshBuilder m, int t) => m.C[m.I[3 * t]].slot;
        static int Tris(MeshBuilder m, ColorSlot s, Func<int, bool>? also = null) => Enumerable.Range(0, m.Tris).Count(t => Slot(m, t) == s && (also == null || also(t)));
        static bool[] Marked(MeshBuilder m, List<(int start, int end)> ranges)
        {
            var o = new bool[m.Tris];
            foreach (var (s, e) in ranges) for (int t = s; t < e; t++) o[t] = true;
            return o;
        }

        [Fact]
        public void TheRoomsGetTheBacksOfWhatTheySeeAndNothingElse()
        {
            OpeningKinds.Register(StreetOnly("t-back"));
            // a shell building: the window as made, nothing behind it
            var (bs, ss) = Block(false, "t-back"); var shell = Lod0.Build(ss, bs).Op;
            int windows = Tris(shell, ColorSlot.Glass) / 2;
            Assert.True(windows >= 8, $"{windows} windows");
            Assert.Equal(FrameTris * windows, Tris(shell, ColorSlot.Frame));
            Assert.Equal(SillTris * windows, Tris(shell, ColorSlot.Metal));
            var shellBacking = Marked(shell, shell.BackingTris);
            Assert.Equal(0, Tris(shell, ColorSlot.Frame, t => shellBacking[t]));
            // walked in: each frame closed from behind where it is open, the face already turned to the rooms as made; the
            // sill, round the hole and so behind the wall from inside, as made; the glass both faces, in the see-through glass
            var (bw, sw) = Block(true, "t-back"); var r = Lod0.Build(sw, bw); var op = r.Op;
            int walked = Tris(r.Glass, ColorSlot.Glass) / 4;
            Assert.True(walked >= 8, $"{walked} windows walked in");
            Assert.Equal((FrameTris + Backed) * walked, Tris(op, ColorSlot.Frame));
            Assert.Equal(SillTris * walked, Tris(op, ColorSlot.Metal));
            // the backs are faces only Unity draws, each half a millimetre behind the face it backs, turned the other way
            var backing = Marked(op, op.BackingTris);
            Assert.Equal(Backed * walked, Tris(op, ColorSlot.Frame, t => backing[t]));
            // LOD1 is never walked in: as made
            Assert.Equal(0, Tris(Lod1.Build(sw, bw), ColorSlot.Frame));   // no LOD1 mesh: its plain pane
        }

        [Fact]
        public void AWindowMadeWholeGetsNoBacks()
        {
            // KindBakeTests' shuttered window, baked from a prefab of closed boxes: its members closed behind, butting on one
            // another; the rooms see none of it from behind, so a walk-in storey draws it as made, nothing inside it
            var (src, hole) = KindBakeTests.Shuttered();
            var k = KindBakeTests.Kind("t-back-whole", KindBake.Bake(src, hole, false), hole, KindBakeTests.ShutteredColour);
            OpeningKinds.Register(k);
            var (b, site) = Block(true, "t-back-whole"); var op = Lod0.Build(site, b).Op;
            var backing = Marked(op, op.BackingTris);
            Assert.True(Tris(op, ColorSlot.Frame) > 0);
            Assert.Equal(0, Tris(op, ColorSlot.Frame, t => backing[t]));
            Assert.Equal(0, Tris(op, ColorSlot.Trim, t => backing[t]));
            var c = OpeningKinds.Check(OpeningKinds.Get("t-back-whole")!);
            Assert.Equal(c.tris + k.lod0.parts.Where(p => p.slot == ColorSlot.Glass).Sum(p => p.tris.Length / 3), c.walkInTris);   // its glass's other face alone
        }

        [Fact]
        public void EachBackLiesJustBehindTheFaceItBacksTurnedTheOtherWay()
        {
            OpeningKinds.Register(StreetOnly("t-back-pair"));
            var (b, site) = Block(true, "t-back-pair"); var op = Lod0.Build(site, b).Op;
            var backing = Marked(op, op.BackingTris);
            var fronts = Enumerable.Range(0, op.Tris).Where(t => Slot(op, t) == ColorSlot.Frame && !backing[t]).ToList();
            var backs = Enumerable.Range(0, op.Tris).Where(t => Slot(op, t) == ColorSlot.Frame && backing[t]).ToList();
            P3 Centre(int t) => (op.P[op.I[3 * t]] + op.P[op.I[3 * t + 1]] + op.P[op.I[3 * t + 2]]) * (1.0 / 3);
            P3 Facing(int t) => P3.Cross(op.P[op.I[3 * t + 1]] - op.P[op.I[3 * t]], op.P[op.I[3 * t + 2]] - op.P[op.I[3 * t]]).Normalized;
            // every back: a front face it backs, its centre that face's moved back along its facing by KindPart.BackOff (the
            // window is stretched to its opening across and up, so a back of a side face moves a little more or less)
            int paired = 0;
            foreach (int t in backs)
            {
                P3 c = Centre(t), n = Facing(t);
                bool found = fronts.Any(u =>
                {
                    var fn = Facing(u); if (P3.Dot(fn, n) > -0.999) return false;
                    var gap = c - Centre(u); double along = -P3.Dot(gap, fn), off = (gap + fn * along).Length;
                    return off < 1e-6 && along > KindPart.BackOff * 0.3 && along < KindPart.BackOff * 3;
                });
                if (found) paired++;
            }
            Assert.Equal(backs.Count, paired);
            // and the stored normals are the winding's: a back is lit as turned to the rooms
            Assert.All(backs, t => Assert.True(P3.Dot(op.N[op.I[3 * t]], Facing(t)) > 0.99));
        }

        [Fact]
        public void FromInsideItsFrameShowsNoHole()
        {
            OpeningKinds.Register(StreetOnly("t-back-ray"));
            FromInside("t-back-ray", 1000);
        }

        /// <summary>The converted scenes' window (kinds-converted.json), made for the street alone: half the rays from the rooms that met it saw its faces' backs.</summary>
        [Fact]
        public void FromInsideAConvertedWindowShowsNoHole()
        {
            var k = KindTopologyTests.Converted()["Visual_BuildingPiece08_Single (window)"];
            OpeningKinds.Register(k);
            FromInside(k.id, 500);   // a smaller window than the other: fewer rays meet it
        }

        /// <summary>
        /// Rays from eyes in each storey's room of a walk-in block whose windows are <paramref name="kind"/>, at points on its
        /// windows (its triangles: the frame's colour, or colours of their own), through LOD0's opaque mesh as drawn, the glass
        /// left out (see-through): at least <paramref name="seen"/> meet a window first, and none of those its back face.
        /// </summary>
        void FromInside(string kind, int seen)
        {
            var (b, site) = Block(true, kind); var op = Lod0.Build(site, b).Op;
            bool Window(int t) => Slot(op, t) == ColorSlot.Frame || (int)Slot(op, t) >= 32;
            var sc = new RayScene(); sc.Add(op.P, op.I, t => Window(t) ? 1 : 0);
            var windows = Enumerable.Range(0, op.Tris).Where(Window).ToList();
            var rng = new Random(7); int rays = 0, met = 0, holes = 0, slivers = 0; var where = new List<string>();
            double Cy(int t) => (op.P[op.I[3 * t]].y + op.P[op.I[3 * t + 1]].y + op.P[op.I[3 * t + 2]].y) / 3;
            for (int k = 0; k < b.floors.Count; k++)
            {
                double y0 = Derived.FloorBase(b, k), top = y0 + Derived.FloorH(b, k) - Dim.SLAB;
                var mine = windows.Where(t => Cy(t) > y0 && Cy(t) < top).ToList();
                for (int i = 0; i < 1500 && mine.Count > 0; i++)
                {
                    // an eye in the storey's room, clear of its walls, standing or sitting; aimed at a point on a window
                    double ex = 1.5 + rng.NextDouble() * 11, ez = 1.5 + rng.NextDouble() * 7, ey = y0 + 0.8 + rng.NextDouble() * 1.2;
                    int t = mine[rng.Next(mine.Count)]; double u = rng.NextDouble(), v = rng.NextDouble(); if (u + v > 1) { u = 1 - u; v = 1 - v; }
                    P3 a = op.P[op.I[3 * t]], q = a + (op.P[op.I[3 * t + 1]] - a) * u + (op.P[op.I[3 * t + 2]] - a) * v;
                    double dx = q.x - ex, dy = q.y - ey, dz = q.z - ez, l = Math.Sqrt(dx * dx + dy * dy + dz * dz); dx /= l; dy /= l; dz /= l;
                    int hit = sc.Hit(ex, ey, ez, dx, dy, dz, out double th); if (hit < 0) continue;
                    rays++;
                    ref var f = ref sc[hit];
                    if (f.tag != 1) continue;   // the wall, its reveal or a slab, in front of the window from here
                    met++;
                    if (!sc.Back(hit, dx, dy, dz)) continue;
                    if (Math.Abs((f.nx * dx + f.ny * dy + f.nz * dz) / Math.Sqrt(f.nx * f.nx + f.ny * f.ny + f.nz * f.nz)) < 0.1) continue;   // grazing
                    // where a member is open behind, a ray can slip in between a face and its back, half a millimetre apart, at
                    // their open edge: a sliver no wider, with a face turned to the eye within that
                    double hx = ex + dx * th, hy = ey + dy * th, hz = ez + dz * th;
                    if (sc.FrontNear(hx, hy, hz, 0.002, dx, dy, dz)) { slivers++; continue; }
                    holes++;
                    if (where.Count < 10) where.Add($"storey {k}: a window's back face at ({hx:0.000}, {hy:0.000}, {hz:0.000}) from ({ex:0.00}, {ey:0.00}, {ez:0.00})");
                }
            }
            output.WriteLine($"{rays} rays from the rooms, {met} meeting a window first, {holes} of them its back face, {slivers} a back face's sliver");
            Assert.True(met > seen, $"{met} rays met a window");
            Assert.True(slivers < met / 50, $"{slivers} slivers of {met}");
            Assert.True(holes == 0, $"{holes} rays from the rooms meet a window's back face (a hole where back faces are culled, the section cap where a cut building draws them):\n  " + string.Join("\n  ", where));
        }

        [Fact]
        public void TheBacksGoWithTheRooms()
        {
            // without glass a window shows its storey's rooms only near (the rooms kept apart): its backs go with them. With
            // glass, clear at any distance, the storey's rooms stay with the shell, and its backs with them
            OpeningKinds.Register(StreetOnly("t-back-rooms", glass: false));
            OpeningKinds.Register(StreetOnly("t-back-shell"));
            foreach (var (id, apart) in new[] { ("t-back-rooms", true), ("t-back-shell", false) })
            {
                var (b, site) = Block(true, id); var op = Lod0.Build(site, b).Op;
                var backing = Marked(op, op.BackingTris); var rooms = Marked(op, op.InsideTris);
                double y1 = Derived.FloorBase(b, 1);   // the ground storey's street door shows its rooms at any distance
                bool Above(int t) => op.P[op.I[3 * t]].y > y1;
                var backs = Enumerable.Range(0, op.Tris).Where(t => Slot(op, t) == ColorSlot.Frame && backing[t] && Above(t)).ToList();
                Assert.NotEmpty(backs);
                if (apart) Assert.All(backs, t => Assert.True(rooms[t], $"{id}: a back left with the shell"));
                else Assert.All(backs, t => Assert.False(rooms[t], $"{id}: a back with rooms the glass shows at any distance"));
                // the window's front faces stay with the shell either way
                Assert.DoesNotContain(Enumerable.Range(0, op.Tris), t => Slot(op, t) == ColorSlot.Frame && !backing[t] && rooms[t]);
            }
        }

        [Fact]
        public void ItsBacksAreFlickerFree()
        {
            OpeningKinds.Register(StreetOnly("t-back-cop"));
            var (b, site) = Block(true, "t-back-cop");
            var r = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
            Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(5)));
        }

        [Fact]
        public void TheInspectorCountsWhatAWalkInStoreyDraws()
        {
            var k = StreetOnly("t-back-check"); OpeningKinds.Register(k);
            var c = OpeningKinds.Check(OpeningKinds.Get("t-back-check")!);
            Assert.Equal(FrameTris + SillTris + 2, c.tris);
            Assert.Equal(FrameTris + Backed + SillTris + 2 * 2, c.walkInTris);   // the backs, and the glass's both faces
        }

        /// <summary>
        /// Seen from outside, through the clear glass of a walk-in city's windows, a window across the room shows no hole
        /// either, and its backs show none of their own (<see cref="BackFaceTests"/>' rays, with every building's windows the
        /// artist's).
        /// </summary>
        [Theory]
        [InlineData(BackFaceTests.View.Near)]
        [InlineData(BackFaceTests.View.Far)]
        public void FromOutsideCullingMakesNoHole(BackFaceTests.View view)
        {
            OpeningKinds.Register(StreetOnly("t-back-city"));
            var d = new StoreyDocument(); TestCity.Generate(d, 10, walkIn: true);
            foreach (var b in d.buildings) { b.style.windowKind = "t-back-city"; b.style.windowSet = null; b.style.groundSet = null; }
            var (holes, rooms, rays, where) = BackFaceTests.Look(BackFaceTests.Make(d), view);
            output.WriteLine($"{view}: {rays:N0} rays, {holes} meet a back face with nothing close behind, {rooms} a room");
            foreach (var w in where) output.WriteLine("  " + w);
            Assert.True(rays > 4000);
            Assert.True(holes + rooms == 0, $"{view}: {holes} holes from outside, {rooms} rays meeting a room:\n  " + string.Join("\n  ", where));
        }
    }
}
