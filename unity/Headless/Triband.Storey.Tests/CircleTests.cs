using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A circle from four corners (docs/EDITOR.md §6.1, the Outline card's Circle, <see cref="CornerCuts.Circle"/>): a square's
    /// corners, each rounded as far as it goes, so it is four corners to drag, type or round less, not dozens. Storey's own.
    /// </summary>
    public class CircleTests
    {
        /// <summary>A rect preset building (12 × 9 m, its middle at (6, 4.5)) of four floors, walked in or not, with no stairs.</summary>
        static (StoreyDocument d, BuildingData b) Block(bool walkIn = false)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.details.Clear(); b.shafts.Clear(); b.interior = walkIn;
            Floors.SetCount(b, 4);
            return (d, b);
        }

        static readonly Vec2 Middle = new Vec2(6, 4.5);
        static double From(Vec2 o, Vec2 p) => Math.Sqrt((p.x - o.x) * (p.x - o.x) + (p.z - o.z) * (p.z - o.z));

        [Theory]
        [InlineData(4.0)]
        [InlineData(6.5)]
        [InlineData(10.0)]
        [InlineData(18.25)]
        public void ItIsFourCornersEachRoundedAsFarAsItGoes(double radius)
        {
            var (_, b) = Block();
            Assert.Equal(OutlineIssue.None, CornerCuts.Circle(b, 0, radius, out _));
            var (sharp, cuts) = CornerCuts.Sharp(b, 0);
            Assert.Equal(4, sharp.Count);
            Assert.Equal(4, cuts.Count);
            Assert.All(cuts, c => { Assert.Equal(CornerShape.Round, c.shape); Assert.Equal(radius - CornerCuts.CircleGap, c.size, 6); });
            // a square round the old outline's middle, as wide as the circle
            Assert.Equal(2 * radius, sharp.Max(p => p.x) - sharp.Min(p => p.x), 6);
            Assert.Equal(2 * radius, sharp.Max(p => p.z) - sharp.Min(p => p.z), 6);
            // every point of the outline on the circle, or within 7.5 cm outside it (6.6 cm at 45°, and the centimetres)
            Assert.All(b.footprint, p => Assert.InRange(From(Middle, p), radius - 1e-3, radius + 0.075));
            // its quarters alike: 15° steps, 6 of them each (one had 7 before, as a hair over 90° rounded up)
            Assert.All(b.corners, c => Assert.Equal(7, c.pts.Count));
            Assert.Equal(4 * 7, b.footprint.Count);
            // the most the Corners card rounds each: as far as it is
            for (int i = 0; i < 4; i++) Assert.Equal(radius - CornerCuts.CircleGap, CornerCuts.Largest(b, 0, i, CornerShape.Round), 2);
        }

        [Fact]
        public void ARoundedSquareCornerIsSixStepsWhereverItIs()
        {
            // the cut's ends kept to the centimetre: a square corner's sweep a hair over 90° rounded up to 7 steps, at some
            // places and radii and not at others
            foreach (double r in new[] { 1.7, 2.35, 3, 4.84, 5.84, 7.13, 9.84 })
                foreach (var at in new[] { new Vec2(0, 0), new Vec2(3.37, -1.11), new Vec2(-7.03, 2.59) })
                {
                    var fp = new List<Vec2> { new Vec2(at.x, at.z), new Vec2(at.x + 20, at.z), new Vec2(at.x + 20, at.z + 20), new Vec2(at.x, at.z + 20) };
                    for (int i = 0; i < 4; i++) Assert.Equal(7, Outlines.CornerPoints(fp, i, CornerShape.Round, r)!.Count);
                }
        }

        [Fact]
        public void ItBuildsAtEveryLodWithWindowsAllRoundAndNothingFlickering()
        {
            foreach (bool walkIn in new[] { false, true })
            {
                var (d, b) = Block(walkIn);
                Assert.Equal(OutlineIssue.None, CornerCuts.Circle(b, 0, 8, out _));
                var site = new Site(d.buildings);
                var l0 = Lod0.Build(site, b, solids: true);
                Assert.True(Lod1.Build(site, b).Tris > 0 && Lod2.Build(site, b).Tris > 0);
                var rep = CoplanarCheck.Run(l0.Op);
                Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(5)));
                // a window on every 15° step of every quarter, on every storey: its middle's bearing from the circle's
                var m = walkIn ? l0.Glass : l0.Op;
                var panes = Enumerable.Range(0, m.Tris).Where(t => m.C[m.I[3 * t]].slot == ColorSlot.Glass)
                    .Select(t => (m.P[m.I[3 * t]] + m.P[m.I[3 * t + 1]] + m.P[m.I[3 * t + 2]]) * (1.0 / 3)).ToList();
                for (int k = 0; k < 4; k++)
                    for (int q = 0; q < 4; q++)
                    {
                        double y0 = Derived.FloorBase(b, k), y1 = y0 + Derived.FloorH(b, k);
                        Assert.True(panes.Any(p => p.y > y0 && p.y < y1 && (int)Math.Floor((Math.Atan2(p.z - Middle.z, p.x - Middle.x) + Math.PI) / (Math.PI / 2)) % 4 == q),
                            $"{(walkIn ? "walk-in" : "shell")}: no window in quarter {q} of storey {k}");
                    }
            }
        }

        /// <summary>The street doors the ground floor's walls are built with (as LOD0 lays them out).</summary>
        static int Doors(BuildingData b, Site site) =>
            Enumerable.Range(0, b.footprint.Count).Sum(i => { var f = Details.EdgeFrameAt(b, 0, i); return Facade.Ops(site, b, 0, i, f.e.L, f.uS, f.uE).Count(o => o.door); });

        [Fact]
        public void ItsDoorIsOnTheWallNearestTheFrontThatTakesOne()
        {
            var (d, b) = Block(walkIn: true);
            Assert.Equal(OutlineIssue.None, CornerCuts.Circle(b, 0, 12, out bool door));
            Assert.True(door);
            var e = Assert.Single(b.entrances);
            // one of the two 15° steps either side of the front's short wall, the middle of the square's first edge
            var fp = b.footprint; var a = fp[e.edge]; var c = fp[(e.edge + 1) % fp.Count];
            var mid = new Vec2((a.x + c.x) / 2, (a.z + c.z) / 2);
            Assert.True(mid.z < Middle.z - 11 && Math.Abs(mid.x - Middle.x) < 3, $"the door's wall's middle {mid}");
            // built: the way in to the walk-in ground floor
            Assert.Equal(1, Doors(b, new Site(d.buildings)));
        }

        [Fact]
        public void UnderTenMetresItsFrontCornersRoundedLessMakeRoomForItsDoor()
        {
            var (d, b) = Block(walkIn: true);
            Assert.Equal(OutlineIssue.None, CornerCuts.Circle(b, 0, 8, out bool door));
            Assert.False(door);   // its steps are 2.04 m: too short for the doorway and 0.3 m either side
            var site = new Site(d.buildings);
            Assert.Equal(0, Doors(b, site));
            // the front corners (the list's 1 and 2) rounded less: the straight wall between them grows, and takes the door
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 0, CornerShape.Round, 6.5, false));
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 1, CornerShape.Round, 6.5, false));
            Assert.Equal(1, Doors(b, new Site(d.buildings)));
            Assert.Equal(4, CornerCuts.Sharp(b, 0).cuts.Count);
        }

        [Fact]
        public void ItsCornersMoveAsAnyOthersDo()
        {
            // pulled out to the right: a stadium's end, the cuts kept
            var (_, b) = Block();
            Assert.Equal(OutlineIssue.None, CornerCuts.Circle(b, 0, 10, out _));
            var (sharp, _) = CornerCuts.Sharp(b, 0);
            Assert.True(CornerCuts.Move(b, 0, 1, new Vec2(sharp[1].x + 6, sharp[1].z), out _, out int lost));
            Assert.True(CornerCuts.Move(b, 0, 2, new Vec2(sharp[2].x + 6, sharp[2].z), out _, out lost));
            Assert.Equal(0, lost);
            var (now, cuts) = CornerCuts.Sharp(b, 0);
            Assert.Equal(4, now.Count); Assert.Equal(4, cuts.Count);
            Assert.Equal(26, now.Max(p => p.x) - now.Min(p => p.x), 6);
            // pushed in, its edge too short for the cuts either side: not moved
            var c = PrototypeJson.ReadBuilding(PrototypeJson.Write(b));
            Assert.False(CornerCuts.Move(c, 0, 3, new Vec2(now[3].x, now[3].z - 4), out _, out lost));
            Assert.True(lost > 0);
        }

        [Fact]
        public void ASetbackCanBeACircleThatRestsOnTheFloorsBelow()
        {
            var (_, b) = Block();
            b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(24, 0), new Vec2(24, 20), new Vec2(0, 20) };
            int k = Setbacks.Add(b, 0);
            Assert.Equal(OutlineIssue.None, CornerCuts.Circle(b, k, 7, out bool door));
            Assert.True(door);   // a setback's doors are its terraces', put in by hand
            var (sharp, cuts) = CornerCuts.Sharp(b, k);
            Assert.Equal(4, sharp.Count); Assert.Equal(4, cuts.Count);
            Assert.All(Tiers.Outline(b, k), p => Assert.InRange(From(new Vec2(12, 10), p), 7 - 1e-3, 7.075));
            Assert.Equal(4, b.footprint.Count);   // the base as it was
            // one far wider than the floors below: most of it would stand on nothing
            var c = PrototypeJson.ReadBuilding(PrototypeJson.Write(b));
            Assert.Equal(OutlineIssue.Out, CornerCuts.Circle(c, k, 30, out _));
        }
    }
}
