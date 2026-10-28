using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What a wall has (<see cref="EdgeState"/>, docs/EDITOR.md §6.7): a blank wall has no windows; a quiet one no details by
    /// rule either; a bare one no floor bands or plinth either, its neighbours' ending square at its corners. The old
    /// BuildingGenerator's suppressed edges, sides without details, and blank edges. Storey's own.
    /// </summary>
    public class EdgeStateTests
    {
        static (BuildingData b, Site s) Block(EdgeState s = EdgeState.Open, int edge = 1, bool ccw = true)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(16, 0), new Vec2(16, 10), new Vec2(0, 10) };
            if (!ccw) b.footprint.Reverse();
            Floors.SetCount(b, 4);
            b.entrances.Clear(); b.details.Clear(); b.shafts.Clear(); b.interior = false;
            b.style = Styles.Preset("brick"); b.style.ground = GroundType.Match; b.style.bands = true; b.style.plinthH = 0.6;
            b.style.details = new DetailRules { ac = 100, vents = 100 };
            if (s != EdgeState.Open) Tiers.SetState(b, 0, edge, s);
            return (b, new Site(d.buildings));
        }

        static int RuleDetails(BuildingData b, Site s, int edge) => Enumerable.Range(0, b.floors.Count).Sum(k => Details.At(s, b, k).Count(d => d.man == null && d.i == edge));

        [Fact]
        public void EachStateTakesAwayMoreThanTheOneBefore()
        {
            var (bo, so) = Block(); var (bb, sb) = Block(EdgeState.Blank); var (bq, sq) = Block(EdgeState.Quiet); var (bx, sx) = Block(EdgeState.Bare);
            // blank: no windows, but its details by rule (vents on the bare wall) and strips stay
            Assert.Empty(Facade.Ops(sb, bb, 2, 1, 10, 0.3, 9.7));
            Assert.NotEmpty(Facade.Ops(so, bo, 2, 1, 10, 0.3, 9.7));
            Assert.True(RuleDetails(bb, sb, 1) > 0);
            // quiet: no details by rule either, its strips as blank's
            Assert.Equal(0, RuleDetails(bq, sq, 1));
            Assert.True(RuleDetails(bq, sq, 0) > 0);   // the other walls keep theirs
            // bare: no bands or plinth either; the strips of the walls each side end square at its corners
            int quiet = Lod0.Build(sq, bq).Op.Verts, bare = Lod0.Build(sx, bx).Op.Verts;
            Assert.True(bare < quiet, $"bare {bare}, quiet {quiet}");
            Assert.True(Lod1.Build(sx, bx).Verts < Lod1.Build(sq, bq).Verts);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ABareWallsNeighboursStripsEndClosedWithNothingOverlapping(bool ccw)
        {
            var (b, s) = Block(EdgeState.Bare, 1, ccw);
            foreach (var m in new[] { Lod0.Build(s, b).Op, Lod1.Build(s, b) })
            {
                var rep = CoplanarCheck.Run(m);
                Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
            }
        }

        [Fact]
        public void LodTwoPaintsABareWallWithoutItsBand()
        {
            var (bo, so) = Block(); var (b, s) = Block(EdgeState.Bare);
            var plain = Lod2.Build(so, bo); var bare = Lod2.Build(s, b);
            Assert.True(bare.Rows.Count > plain.Rows.Count);
            Assert.Contains(bare.Rows, r => r.run[1] == 0 && r.run[0] == 3.0);
            // a building with no bare wall has the rows it had
            var (bb, sb) = Block(EdgeState.Blank);
            Assert.Equal(plain.Rows.Count, Lod2.Build(sb, bb).Rows.Count);
        }

        [Fact]
        public void AQuietOrBareWallIsBlankTooSoThePrototypeShowsItWindowless()
        {
            var (b, _) = Block(EdgeState.Bare, 2);
            Assert.Contains(2, b.blank); Assert.Contains(2, b.bare!); Assert.Null(b.quiet);
            Tiers.SetState(b, 0, 2, EdgeState.Quiet);
            Assert.Contains(2, b.blank); Assert.Contains(2, b.quiet!); Assert.Null(b.bare);   // none is null, not an empty list
            Facades.ToggleBlank(b, 0, 2);   // the old toggle: everything back
            Assert.Equal(EdgeState.Open, Tiers.State(b, 0, 2));
            Facades.ToggleState(b, 0, 2, EdgeState.Bare); Assert.Equal(EdgeState.Bare, Derived.EdgeStateAt(b, 3, 2));
            Facades.ToggleState(b, 0, 2, EdgeState.Bare); Assert.Equal(EdgeState.Open, Derived.EdgeStateAt(b, 3, 2));
        }

        [Fact]
        public void AWallKeepsItsStateThroughOutlineEdits()
        {
            var (b, _) = Block(EdgeState.Bare, 1);
            Tiers.SetState(b, 0, 2, EdgeState.Quiet);
            int c = Outlines.InsertVertex(b, 1);   // edge 1 split in two, edge 2 now 3
            Assert.Equal(EdgeState.Bare, Tiers.State(b, 0, 1)); Assert.Equal(EdgeState.Bare, Tiers.State(b, 0, c));
            Assert.Equal(EdgeState.Quiet, Tiers.State(b, 0, 3));
            Outlines.MergeVertex(b, c, 0);
            Assert.Equal(EdgeState.Bare, Tiers.State(b, 0, 1)); Assert.Equal(EdgeState.Quiet, Tiers.State(b, 0, 2));
            int k = Setbacks.Add(b, 0);
            Assert.Equal(EdgeState.Bare, Tiers.State(b, k, 1));
            Assert.True(Setbacks.Move(b, k, k + 1));
            Assert.Equal(EdgeState.Bare, Tiers.State(b, k + 1, 1)); Assert.Null(b.floors[k].bare);
            Assert.True(Outlines.RemoveVertex(b, 2));   // edges 1 and 2 go into one
            Assert.DoesNotContain(EdgeState.Quiet, Enumerable.Range(0, b.footprint.Count).Select(i => Tiers.State(b, 0, i)));
        }

        [Fact]
        public void CuttingACornerKeepsTheStateOfItsWalls()
        {
            var (b, _) = Block(EdgeState.Bare, 1);
            Tiers.SetState(b, 0, 2, EdgeState.Bare);
            Assert.Equal(OutlineIssue.None, Outlines.Corner(b, 0, 2, CornerShape.Chamfer, 2, out int first, out _));
            Assert.Equal(EdgeState.Bare, Tiers.State(b, 0, first));   // a bare corner stays bare
            Assert.All(b.bare!, i => Assert.Contains(i, b.blank));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AWholeSideTakesAStateAsTheOldGeneratorsSideMaskDid(bool ccw)
        {
            var (b, _) = Block(ccw: ccw);
            Tiers.SetState(b, 0, 0, EdgeState.Bare);   // a stronger state is kept
            Assert.Equal(1, Facades.SetSide(b, 0, Side.Back, EdgeState.Quiet) + Facades.SetSide(b, 0, Side.Front, EdgeState.Quiet));
            // the edge along z = 10 faces +z (front), the one along z = 0 faces −z (back), whichever way the outline runs
            int front = Enumerable.Range(0, 4).Single(i => b.footprint[i].z == 10 && b.footprint[(i + 1) % 4].z == 10);
            int back = Enumerable.Range(0, 4).Single(i => b.footprint[i].z == 0 && b.footprint[(i + 1) % 4].z == 0);
            Assert.Equal(front == 0 ? EdgeState.Bare : EdgeState.Quiet, Tiers.State(b, 0, front));
            Assert.Equal(back == 0 ? EdgeState.Bare : EdgeState.Quiet, Tiers.State(b, 0, back));
            Assert.Equal(1, Facades.SetSide(b, 0, Side.Right, EdgeState.Blank));
            int right = Enumerable.Range(0, 4).Single(i => b.footprint[i].x == 16 && b.footprint[(i + 1) % 4].x == 16);
            Assert.Equal(EdgeState.Blank, Tiers.State(b, 0, right));
        }

        [Fact]
        public void QuietAndBareWallsComeBackFromJsonAndAreWrittenOnlyWhenThere()
        {
            var (b, _) = Block(EdgeState.Bare, 1);
            Tiers.SetState(b, 0, 3, EdgeState.Quiet);
            int k = Setbacks.Add(b, 0); Tiers.SetState(b, k, 0, EdgeState.Bare);
            var json = PrototypeJson.Write(b);
            var back = PrototypeJson.ReadBuilding(json);
            Assert.Equal(new[] { 1 }, back.bare); Assert.Equal(new[] { 3 }, back.quiet); Assert.Equal(new[] { 1, 3 }, back.blank.OrderBy(x => x));
            Assert.Equal(new[] { 0, 1 }, back.floors[k].bare!.OrderBy(x => x));   // the base's, copied, and its own
            Assert.Equal(json, PrototypeJson.Write(back));
            var (plain, _) = Block();
            var pj = PrototypeJson.Write(plain);
            Assert.DoesNotContain("\"quiet\"", pj); Assert.DoesNotContain("\"bare\"", pj);
        }
    }
}
