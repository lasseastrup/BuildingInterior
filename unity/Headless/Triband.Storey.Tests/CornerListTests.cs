using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The Placement card's list of corners (docs/EDITOR.md §6.11): a corner typed to a place (<see cref="CornerCuts.Move"/>),
    /// its cut and its neighbours' made again; and how far the Corners card's Radius or Size goes for the corner selected
    /// (<see cref="CornerCuts.Largest"/>). Storey's own.
    /// </summary>
    public class CornerListTests
    {
        static readonly List<Vec2> Rect = new() { new Vec2(0, 0), new Vec2(12, 0), new Vec2(12, 10), new Vec2(0, 10) };

        static BuildingData Box()
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.footprint = Rect.ToList(); b.pos = new Vec2(0, 0);
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear();
            foreach (var f in b.floors) f.walls.Clear();
            return b;
        }

        // the building as it is: an operation that refuses leaves it to the caller to put back (CornerCuts.Around), as the editor does
        static BuildingData Copy(BuildingData b) => PrototypeJson.ReadBuilding(PrototypeJson.Write(b));

        static void At(Vec2 want, Vec2 got) => Assert.True(Math.Abs(want.x - got.x) < 1e-6 && Math.Abs(want.z - got.z) < 1e-6, $"{got}, not {want}");

        [Fact]
        public void ACornerTypedSomewhereMovesThereWithItsCut()
        {
            var b = Box();
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 2, CornerShape.Round, 3, false));
            Assert.True(CornerCuts.Move(b, 0, 2, new Vec2(15, 13), out var why, out int lost));
            Assert.Equal(OutlineIssue.None, why); Assert.Equal(0, lost);
            var (sharp, cuts) = CornerCuts.Sharp(b, 0);
            Assert.Equal(4, sharp.Count);
            At(new Vec2(15, 13), sharp[2]); At(Rect[0], sharp[0]); At(Rect[1], sharp[1]); At(Rect[3], sharp[3]);
            // still rounded, 3 m, its arc made again round the corner where it is now
            var c = Assert.Single(cuts); Assert.Equal(2, c.i); Assert.Equal(CornerShape.Round, c.shape); Assert.Equal(3, c.size);
            var want = Outlines.CornerPoints(sharp, 2, CornerShape.Round, 3)!;
            Assert.Equal(want.Count, b.corners[0].pts.Count);
            for (int k = 0; k < want.Count; k++) At(want[k], b.corners[0].pts[k]);
            // a neighbour typed too: the cut follows its edges again
            Assert.True(CornerCuts.Move(b, 0, 3, new Vec2(-2, 13), out _, out _));
            Assert.Single(CornerCuts.Sharp(b, 0).cuts);
        }

        [Fact]
        public void AnOutlineTheRulesRefuseIsNotMade()
        {
            // corner 2 across the first edge: the outline would cross itself
            var b = Box();
            Assert.False(CornerCuts.Move(b, 0, 2, new Vec2(6, -4), out var why, out _));
            Assert.Equal(OutlineIssue.Shape, why);
            // where it is already: nothing to do
            var c = Box();
            Assert.False(CornerCuts.Move(c, 0, 1, Rect[1], out var none, out _));
            Assert.Equal(OutlineIssue.None, none);
        }

        [Fact]
        public void ACornerThatWouldLoseItsCutIsNotMovedThere()
        {
            // a 4 m round at corner 2 needs 4.3 m of the edge to corner 3: a drag lets the cut go while it doesn't fit, a
            // typed place doesn't
            var b = Box();
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 2, CornerShape.Round, 4, false));
            var c = Copy(b);
            Assert.False(CornerCuts.Move(c, 0, 3, new Vec2(8, 10), out var why, out int lost));
            Assert.Equal(OutlineIssue.None, why); Assert.Equal(1, lost);
            // with room for it, it goes
            Assert.True(CornerCuts.Move(b, 0, 3, new Vec2(7, 10), out _, out lost));
            Assert.Equal(0, lost); Assert.Single(CornerCuts.Sharp(b, 0).cuts);
        }

        [Fact]
        public void TheRadiusGoesAsFarAsTheCornerTakesOne()
        {
            // a square corner between edges of 10 and 12 m: 0.3 m of each left, and 2 cm for the centimetres
            var b = Box();
            double most = CornerCuts.Largest(b, 0, 2, CornerShape.Round);
            Assert.Equal(10 - 0.32, most, 6);
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(Copy(b), 0, 2, CornerShape.Round, most, false));
            Assert.NotEqual(OutlineIssue.None, CornerCuts.Cut(Copy(b), 0, 2, CornerShape.Round, most + 0.05, false));
            // a neighbour rounded 3 m takes that much of their edge: up to the most, both stand; past it, one of them gives
            // (a cut made bigger than that can push its neighbour's out, so the Corners card stops at the most)
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 1, CornerShape.Round, 3, false));
            most = CornerCuts.Largest(b, 0, 2, CornerShape.Round);
            Assert.Equal(10 - 3 - 0.32, most, 6);
            var at = Copy(b);
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(at, 0, 2, CornerShape.Round, most, false));
            Assert.Equal(2, CornerCuts.Sharp(at, 0).cuts.Count);
            var over = Copy(b);
            Assert.False(CornerCuts.Cut(over, 0, 2, CornerShape.Round, most + 0.05, false) == OutlineIssue.None && CornerCuts.Sharp(over, 0).cuts.Count == 2);
            // the cut corner itself: room on its edges, its own cut left out
            Assert.Equal(10 - 0.32, CornerCuts.Largest(b, 0, 1, CornerShape.Round), 6);
        }

        [Fact]
        public void OnABluntCornerARoundGoesFurtherThanAChamfer()
        {
            // the octagon's corners turn 45°: a round of radius r starts r · tan(22.5°) back along each edge
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "oct", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.entrances.Clear(); b.shafts.Clear();
            double chamfer = CornerCuts.Largest(b, 0, 0, CornerShape.Chamfer), round = CornerCuts.Largest(b, 0, 0, CornerShape.Round);
            double room = Math.Min(Tiers.EdgeLen(b.footprint, b.footprint.Count - 1), Tiers.EdgeLen(b.footprint, 0)) - 0.32;
            Assert.Equal(room, chamfer, 6);
            Assert.Equal(room * Math.Tan(Math.PI * 3 / 8), round, 6);
            foreach (var shape in new[] { CornerShape.Chamfer, CornerShape.Round })
            {
                double most = shape == CornerShape.Round ? round : chamfer;
                Assert.Equal(OutlineIssue.None, CornerCuts.Cut(Copy(b), 0, 0, shape, most, false));
                Assert.NotEqual(OutlineIssue.None, CornerCuts.Cut(Copy(b), 0, 0, shape, most + 0.08, false));
            }
            // a straight corner takes none
            var s = Box(); s.footprint.Insert(1, new Vec2(6, 0));
            Assert.Equal(0, CornerCuts.Largest(s, 0, 1, CornerShape.Round));
        }
    }
}
