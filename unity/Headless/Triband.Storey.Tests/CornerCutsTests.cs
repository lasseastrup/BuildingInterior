using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>A cut corner stays one corner (<see cref="CornerCuts"/>): restyled, cleared and moved in one step. Storey's own.</summary>
    public class CornerCutsTests
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

        static void SameOutline(List<Vec2> want, List<Vec2> got)
        {
            Assert.Equal(want.Count, got.Count);
            for (int i = 0; i < want.Count; i++) Assert.True(Math.Abs(want[i].x - got[i].x) < 1e-6 && Math.Abs(want[i].z - got[i].z) < 1e-6, $"corner {i}: {got[i]}, not {want[i]}");
        }

        [Fact]
        public void ACutCornerIsOneCornerOfTheSharpOutline()
        {
            var b = Box();
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 2, CornerShape.Round, 3, false));
            Assert.True(b.footprint.Count > 5, "the outline holds the arc");
            var (sharp, cuts) = CornerCuts.Sharp(b, 0);
            SameOutline(Rect, sharp);
            Assert.Single(cuts); Assert.Equal(2, cuts[0].i); Assert.Equal(CornerShape.Round, cuts[0].shape);
        }

        [Fact]
        public void RestylingACutReplacesIt()
        {
            var b = Box();
            CornerCuts.Cut(b, 0, 2, CornerShape.Round, 3, false);
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 2, CornerShape.Chamfer, 2, false));
            Assert.Equal(5, b.footprint.Count);
            var (sharp, cuts) = CornerCuts.Sharp(b, 0);
            SameOutline(Rect, sharp);
            Assert.Single(cuts); Assert.Equal(CornerShape.Chamfer, cuts[0].shape); Assert.Equal(2, cuts[0].size);
        }

        [Fact]
        public void ClearingGivesTheSharpCornerAndTheDoorsBack()
        {
            var b = Box();
            b.entrances.Add(new EntranceData { edge = 1, t = 0.3 });   // on the edge into the corner cut
            b.details.Add(new DetailData { kind = DetailKind.Ac, k = 1, edge = 2, t = 0.6, y = 1 });
            var e0 = (b.entrances[0].edge, b.entrances[0].t); var d0 = (b.details[0].edge, b.details[0].t);
            CornerCuts.Cut(b, 0, 2, CornerShape.Round, 3, false);
            CornerCuts.Cut(b, 0, 0, CornerShape.Chamfer, 2, false);
            Assert.True(CornerCuts.Clear(b, 0, 2));
            Assert.Equal(2, CornerCuts.ClearAll(b, 0) + 1);
            SameOutline(Rect, b.footprint);
            Assert.Empty(b.corners);
            Assert.Equal(e0.edge, b.entrances[0].edge); Assert.Equal(e0.t, b.entrances[0].t, 6);
            Assert.Equal(d0.edge, b.details[0].edge); Assert.Equal(d0.t, b.details[0].t, 6);
        }

        [Fact]
        public void EveryCornerAndClearAll()
        {
            var b = Box();
            Assert.Equal(4, CornerCuts.CutAll(b, 0, CornerShape.Chamfer, 1.5));
            Assert.Equal(8, b.footprint.Count);
            SameOutline(Rect, CornerCuts.Sharp(b, 0).sharp);
            Assert.Equal(4, CornerCuts.ClearAll(b, 0));
            SameOutline(Rect, b.footprint);
        }

        [Fact]
        public void ACutFollowsItsCornerAndItsEdges()
        {
            var b = Box();
            CornerCuts.Cut(b, 0, 2, CornerShape.Round, 3, false);
            // the cut corner itself moves
            var moved = Rect.ToList(); moved[2] = new Vec2(14, 11);
            Assert.True(CornerCuts.Around(b, 0, (x, _) => Outlines.Set(x, 0, moved) == OutlineIssue.None));
            var (sharp, cuts) = CornerCuts.Sharp(b, 0);
            SameOutline(moved, sharp); Assert.Single(cuts); Assert.Equal(new Vec2(14, 11).x, cuts[0].at.x);
            // a neighbouring corner moves: the arc meets the new edge
            var nb = moved.ToList(); nb[1] = new Vec2(13, -1);
            CornerCuts.Around(b, 0, (x, _) => Outlines.Set(x, 0, nb) == OutlineIssue.None);
            SameOutline(nb, CornerCuts.Sharp(b, 0).sharp);
            var want = Outlines.CornerPoints(nb, 2, CornerShape.Round, 3)!;
            SameOutline(want, b.corners[0].pts);
            // a corner added elsewhere keeps it
            CornerCuts.Around(b, 0, (x, _) => { Outlines.InsertVertex(x, 3, 0); return true; });
            Assert.Single(CornerCuts.Sharp(b, 0).cuts);
            Assert.Equal(5, CornerCuts.Sharp(b, 0).sharp.Count);
        }

        [Fact]
        public void ACutThatStopsFittingLeavesTheCornerSharp()
        {
            var b = Box();
            CornerCuts.Cut(b, 0, 2, CornerShape.Chamfer, 4, false);
            var tight = Rect.ToList(); tight[1] = new Vec2(12, 7); tight[2] = new Vec2(12.5, 10);   // a 3 m edge into it
            Assert.True(CornerCuts.Around(b, 0, (x, _) => Outlines.Set(x, 0, tight) == OutlineIssue.None, out int lost));
            Assert.Equal(1, lost);
            SameOutline(tight, b.footprint);
        }

        [Fact]
        public void ACornerEntranceStaysOneDoor()
        {
            var b = Box();
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(b, 0, 2, CornerShape.Chamfer, 3, true));
            Assert.Single(b.entrances);
            int chamfer = CornerCuts.Live(b, 0)[0].start;
            Assert.Equal(chamfer, b.entrances[0].edge);
            CornerCuts.Cut(b, 0, 2, CornerShape.Chamfer, 3.5, true);
            CornerCuts.Around(b, 0, (x, _) => Outlines.Set(x, 0, Rect) == OutlineIssue.None);   // something else changes
            Assert.Single(b.entrances);
            Assert.Equal(CornerCuts.Live(b, 0)[0].start, b.entrances[0].edge);
            CornerCuts.Clear(b, 0, 2);
            Assert.Empty(b.entrances);   // the door was the chamfer's
        }

        [Fact]
        public void ADetailOnTheChamferRidesThroughOtherEdits()
        {
            var b = Box();
            CornerCuts.Cut(b, 0, 2, CornerShape.Chamfer, 3, false);
            int chamfer = CornerCuts.Live(b, 0)[0].start;
            b.details.Add(new DetailData { kind = DetailKind.Ac, k = 1, edge = chamfer, t = 0.4, y = 1 });
            var other = Rect.ToList(); other[0] = new Vec2(-1, -1);
            CornerCuts.Around(b, 0, (x, _) => Outlines.Set(x, 0, other) == OutlineIssue.None);
            Assert.Single(b.details);
            Assert.Equal(CornerCuts.Live(b, 0)[0].start, b.details[0].edge); Assert.Equal(0.4, b.details[0].t, 6);
        }

        [Fact]
        public void CutsSurviveTheFileAndASetback()
        {
            var b = Box();
            CornerCuts.Cut(b, 0, 2, CornerShape.Round, 2, false);
            var back = PrototypeJson.ReadBuilding(PrototypeJson.Write(b));
            Assert.Single(CornerCuts.Sharp(back, 0).cuts);
            int k = Setbacks.Add(b, 0);
            Assert.Single(CornerCuts.Sharp(b, k).cuts);
            SameOutline(Rect, CornerCuts.Sharp(b, k).sharp);
        }

        [Fact]
        public void ACutBuildingBuildsCleanly()
        {
            var b = Box(); var d = new StoreyDocument(); d.buildings.Add(b);
            CornerCuts.Cut(b, 0, 2, CornerShape.Round, 3, false);
            CornerCuts.Cut(b, 0, 0, CornerShape.Chamfer, 2.5, true);
            var site = new Site(d.buildings);
            var rep = CoplanarCheck.Run(Lod0.Build(site, b).Op);
            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
        }
    }
}
