using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Slice: one window for every size of opening (docs/OPENINGS.md §4.3). What lies between the slice lines stretches, what
    /// lies outside them keeps its size and moves with the hole's sides, objects that keep their shape move with their anchor,
    /// and triangles are cut where they cross a line. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class SliceTests
    {
        // a wall along +x facing +z: across the opening is +x, up is +y, out is +z (KindPlace's own space lands on the world's)
        static readonly Frame Wall = new Frame(new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1));

        static P3 Own(P3 world, double u, double y0) => new P3(world.x - u, world.y - y0, world.z - Dim.T_EXT);

        static void Near(double want, double got, double tol = 1e-9) => Assert.InRange(got, want - tol, want + tol);

        [Fact]
        public void TheWorldFrameIsTheOpeningsOwn()
        {
            var at = new KindPlace(Wall, 5, 3, 1, 1);
            var p = Own(at.Point(new P3(0.2, 0.7, 0.05)), 5, 3);
            Near(0.2, p.x); Near(0.7, p.y); Near(0.05, p.z);
        }

        [Fact]
        public void WhatIsOutsideTheLinesKeepsItsSize()
        {
            // a 1.2 × 1.6 hole sliced into a 2.0 × 1.35 opening, its lines at its sides
            var at = KindPlace.Sliced(Wall, 0, 0, 1.2, 1.6, 2.0, 1.35, 0, 0, 0, 0);
            // a shutter from 0.62 to 1.02 right of the middle: it keeps its 40 cm and moves out with the hole's side
            Near(1.02, at.Point(new P3(0.62, 0.5, 0.03)).x); Near(1.42, at.Point(new P3(1.02, 0.5, 0.03)).x);
            // the hole's sides go to the opening's
            Near(1.0, at.Point(new P3(0.6, 0.5, 0)).x); Near(-1.0, at.Point(new P3(-0.6, 0.5, 0)).x);
            // a sill under the hole keeps its height; what is over it moves up with the hole's top
            Near(-0.06, at.Point(new P3(0, -0.06, 0.05)).y); Near(1.35 + 0.3, at.Point(new P3(0, 1.6 + 0.3, 0.05)).y);
            // inside, everything stretches
            Near(1.35 / 2, at.Point(new P3(0, 0.8, 0)).y); Near(0.5, at.Point(new P3(0.3, 0.8, 0)).x);
        }

        [Fact]
        public void BordersKeepAFramesMembersThick()
        {
            // 6 cm borders: a 1 m hole's left member, 6 cm wide, stays 6 cm wide in a 1.6 m opening; the glass between stretches
            var at = KindPlace.Sliced(Wall, 0, 0, 1.0, 1.4, 1.6, 1.4, 0.06, 0.06, 0.06, 0.06);
            Near(-0.8, at.Point(new P3(-0.5, 0.7, 0)).x); Near(-0.74, at.Point(new P3(-0.44, 0.7, 0)).x);
            Near(0.74, at.Point(new P3(0.44, 0.7, 0)).x);
            Near(0, at.Point(new P3(0, 0.7, 0)).x);
            // a slanted face between the lines turns with the stretch; outside them it doesn't
            var slant = new P3(1, 0, 1).Normalized;
            var inside = at.Normal(new P3(0.2, 0.7, 0), slant); var outside = at.Normal(new P3(-0.48, 0.7, 0), slant);
            Assert.True(Math.Abs(inside.x) < Math.Abs(outside.x) - 0.05, $"{inside} {outside}");
            Near(slant.x, outside.x, 1e-9);
        }

        [Fact]
        public void AnOpeningWithNoRoomBetweenTheLinesIsStretched()
        {
            // borders as wide as the opening: no room between the lines, so it is stretched instead
            var at = KindPlace.Sliced(Wall, 0, 0, 1.2, 1.6, 0.5, 1.6, 0.3, 0.3, 0, 0);
            Near(0.25, at.Point(new P3(0.6, 0.5, 0)).x); Near(-0.25, at.Point(new P3(-0.6, 0.5, 0)).x);
        }

        [Fact]
        public void APartThatKeepsItsShapeMovesWithItsAnchor()
        {
            // a keystone over a 1 × 1.4 hole, anchored at its middle 5 cm over the hole: in a 2 × 1.6 opening it moves up 20 cm
            // and keeps its 16 cm, where the lintel under it stretches
            var at = KindPlace.Sliced(Wall, 0, 0, 1.0, 1.4, 2.0, 1.6, 0, 0, 0, 0).Rigid(new P3(0, 1.45, 0.05));
            Near(-0.08, at.Point(new P3(-0.08, 1.45, 0.05)).x); Near(0.08, at.Point(new P3(0.08, 1.45, 0.05)).x);
            Near(1.65, at.Point(new P3(0, 1.45, 0.05)).y);
            var slant = new P3(1, 0, 1).Normalized;
            Near(slant.x, at.Normal(new P3(0, 1.45, 0.05), slant).x);
        }

        [Fact]
        public void TrianglesAreCutAtTheLinesAndTheirPointsGoToTheirSide()
        {
            // one quad right across a 1 × 1 hole (a sill, 1.4 m long): cut at both lines into three, each piece on one side
            var s = new KindBake.Source
            {
                P = new[] { new P3(-0.7, -0.1, 0.05), new P3(0.7, -0.1, 0.05), new P3(0.7, 0, 0.05), new P3(-0.7, 0, 0.05) },
                N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray(),
            };
            s.subs.Add(new KindBake.Sub { tris = new[] { 0, 1, 2, 0, 2, 3 }, key = "sill" });
            var hole = new KindBake.Hole(0, 0.5, 1, 1);
            var plain = KindBake.Bake(new[] { s }, hole, false);
            var cut = KindBake.Bake(new[] { s }, hole, false, new KindBake.Cuts(1, 1, 0, 0, 0, 0));
            Assert.Equal(2, plain.Tris);
            Assert.True(cut.Tris >= 6, $"{cut.Tris} triangles");
            // no point lies on a line: each went to its own side, so it takes that side's stretch and normal
            Assert.DoesNotContain(cut.P, p => Math.Abs(Math.Abs(p.x) - 0.5) < 1e-6);
            // and the pieces still cover the quad, front faces out
            double area = 0;
            var t = cut.parts.Single().tris;
            for (int i = 0; i < t.Count; i += 3) { var c = P3.Cross(cut.P[t[i + 1]] - cut.P[t[i]], cut.P[t[i + 2]] - cut.P[t[i]]); Assert.True(c.z > 0); area += c.Length / 2; }
            Near(1.4 * 0.1, area, 1e-4);   // less the hundredths of a millimetre its points on the lines were set aside
            // sliced into a 1.6 m opening, the pieces meet again on the lines (no crack), each with its own side's normal
            var at = KindPlace.Sliced(Wall, 0, 0, 1, 1, 1.6, 1, 0, 0, 0, 0);
            var placed = cut.P.Select(p => at.Point(p).x).ToList();
            foreach (var line in new[] { -0.8, 0.8 })
            {
                var near = placed.Where(x => Math.Abs(x - line) < 1e-3).ToList();
                Assert.NotEmpty(near);
                Assert.All(near, x => Near(line, x, 1e-12));
            }
            var slant = new P3(1, 0, 1).Normalized;
            var sides = cut.P.Where(p => Math.Abs(p.x + 0.5) < 1e-3).Select(p => at.Normal(p, slant).x).Distinct().Count();
            Assert.Equal(2, sides);
        }

        [Fact]
        public void ObjectsThatKeepTheirShapeAreAnchoredNotCut()
        {
            var keystone = new KindBake.Source
            {
                P = new[] { new P3(-0.08, 1.4, 0.05), new P3(0.08, 1.4, 0.05), new P3(0.08, 1.55, 0.05), new P3(-0.08, 1.55, 0.05) },
                N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray(), keep = 0,
            };
            keystone.subs.Add(new KindBake.Sub { tris = new[] { 0, 1, 2, 0, 2, 3 }, key = "stone" });
            var r = KindBake.Bake(new[] { keystone }, new KindBake.Hole(0, 0.7, 1, 1.4), false, new KindBake.Cuts(1, 1.4, 0, 0, 0.1, 0.1));
            var part = r.parts.Single();
            Assert.Equal(2, part.tris.Count / 3);   // across the top line, but not cut
            Assert.NotNull(part.anchor);
            Near(0, part.anchor!.Value.x); Near(1.475, part.anchor.Value.y);
        }

        [Fact]
        public void ASlicedWindowFitsEveryOpeningWithNothingFlickering()
        {
            var (src, hole) = KindBakeTests.Shuttered();
            var r = KindBake.Bake(src, hole, false, new KindBake.Cuts(hole.w, hole.h, 0, 0, 0, 0));
            var k = KindBakeTests.Kind("t-slice-shuttered", r, hole, KindBakeTests.ShutteredColour);
            k.slice = true;
            OpeningKinds.Register(k);
            try
            {
                Assert.Equal(0.42, k.reachL, 3); Assert.Equal(0.06, k.reachB, 3);
                foreach (var preset in new[] { "brick", "stucco", "warehouse" })
                {
                    var d = new StoreyDocument();
                    var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
                    b.footprint = new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 12), new Vec2(0, 12) };
                    b.entrances.Clear(); b.details.Clear(); b.shafts.Clear(); b.interior = false;
                    b.style = Styles.Preset(preset); b.style.ground = GroundType.Match; b.style.windowKind = k.id; b.style.bay = 3.4;
                    var site = new Site(d.buildings);
                    var op = Lod0.Build(site, b).Op;
                    // the shutters keep their 40 cm whatever the opening: each shutter's front, a quad standing 5 cm out
                    var shutterFronts = Enumerable.Range(0, op.Verts).Where(i => op.C[i].slot == ColorSlot.Frame && Math.Abs(op.N[i].y) < 0.1).Select(i => op.P[i]).ToList();
                    Assert.NotEmpty(shutterFronts);
                    var rep = Validate.CoplanarCheck.Run(op);
                    Assert.True(rep.Overlaps.Count == 0, preset + ": " + string.Join("\n", rep.Overlaps.Take(5)));
                }
                var t = Validate.KindTrial.Run(k);
                Assert.True(t.overlaps.Count == 0, string.Join("\n", t.overlaps.Take(5)));
            }
            finally { OpeningKinds.Remove(k.id); }
        }

        [Fact]
        public void TheShuttersOfASlicedWindowStayTheirWidth()
        {
            // one storey's windows on the brick preset's wall along +x: a shutter's width, measured along the wall, is 40 cm
            var (src, hole) = KindBakeTests.Shuttered();
            var r = KindBake.Bake(src, hole, false, new KindBake.Cuts(hole.w, hole.h, 0, 0, 0, 0));
            var k = KindBakeTests.Kind("t-slice-width", r, hole, key => key == "shutter" ? ColorSlot.Rail : KindBakeTests.ShutteredColour(key));
            k.slice = true;
            OpeningKinds.Register(k);
            try
            {
                var d = new StoreyDocument();
                var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
                b.footprint = new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 12), new Vec2(0, 12) };
                b.entrances.Clear(); b.details.Clear(); b.shafts.Clear(); b.interior = false;
                b.style = Styles.Preset("stucco"); b.style.ground = GroundType.Match; b.style.windowKind = k.id; b.style.bay = 3.4; b.style.winW = 1.6;
                var op = Lod0.Build(new Site(d.buildings), b).Op;
                // the shutters on the wall at z = 0 (facing −z), each a box: group their points by storey and side
                var pts = Enumerable.Range(0, op.Verts).Where(i => op.C[i].slot == ColorSlot.Rail && op.P[i].z < -0.2).Select(i => op.P[i]).ToList();
                Assert.NotEmpty(pts);
                int shutters = 0;
                foreach (var storey in pts.GroupBy(p => Math.Floor(p.y / Derived.FloorH(b, 0))))
                {
                    // a shutter's points along the wall are at its two sides, 40 cm apart; the next shutter's are past a window
                    // (1.6 m) or between two windows (about 0.9 m)
                    var xs = storey.Select(p => p.x).OrderBy(x => x).ToList();
                    double from = xs[0];
                    for (int i = 1; i <= xs.Count; i++)
                    {
                        if (i < xs.Count && xs[i] - xs[i - 1] < 0.6) continue;
                        Near(0.4, xs[i - 1] - from, 1e-3); shutters++;
                        if (i < xs.Count) from = xs[i];
                    }
                }
                Assert.True(shutters >= 8, $"{shutters} shutters");
            }
            finally { OpeningKinds.Remove(k.id); }
        }
    }
}
