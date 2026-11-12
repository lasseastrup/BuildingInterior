using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A shell-only building keeps its rooms, stairs and lifts for when it is walk-in again, but builds none of them
    /// (docs/EDITOR.md §6.5): they never stop its outline, its setbacks or its courtyards and atria being edited, and are
    /// never taken away by those edits. Made walk-in again, the problem list shows those that no longer fit. Storey's own.
    /// </summary>
    public class ShellEditTests
    {
        /// <summary>A three-storey 14 × 10 m block with stairs against its left wall, walk-in or shell only.</summary>
        static (StoreyDocument d, BuildingData b, CoreData s) Block(bool shell)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear(); b.voids.Clear();
            b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList();
            b.entrances.Add(new EntranceData { edge = 0, t = 0.5, k = 0 });
            var s = Shafts.Place(b, 0, new Vec2(2, 5), CoreType.Stairs, 0, Shafts.NewId(d));
            Assert.NotNull(s);
            b.interior = !shell;
            return (d, b, s!);
        }

        // the left wall pushed 3 m in: through the stairs
        static readonly List<Vec2> Narrower = new List<Vec2> { new Vec2(3, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(3, 10) };

        [Fact]
        public void AnOutlineThroughAShellsStairsIsTaken()
        {
            var (_, walkIn, _) = Block(false);
            Assert.Equal(OutlineIssue.Core, Outlines.Set(walkIn, 0, Narrower));
            var (_, shell, s) = Block(true);
            Assert.Equal(OutlineIssue.None, Outlines.Set(shell, 0, Narrower));
            Assert.Equal(Narrower, shell.footprint);
            Assert.Same(s, shell.shafts.Single());   // kept as it was
            // a corner cut through them too
            var (_, walkIn2, _) = Block(false);
            var (_, shell2, _) = Block(true);
            Assert.NotEqual(OutlineIssue.None, CornerCuts.Cut(walkIn2, 0, 0, CornerShape.Chamfer, 4, false));
            Assert.Equal(OutlineIssue.None, CornerCuts.Cut(shell2, 0, 0, CornerShape.Chamfer, 4, false));
        }

        [Fact]
        public void ASetbacksOutlineAndStartIgnoreAShellsStairs()
        {
            foreach (bool shell in new[] { false, true })
            {
                var (_, b, _) = Block(shell);
                int k = Setbacks.Add(b, 0);
                Assert.Equal(2, k);
                var why = Outlines.Set(b, k, Narrower);
                Assert.Equal(shell ? OutlineIssue.None : OutlineIssue.Core, why);
            }
            // a setback on the top floor that leaves the stairs out (they stop under it), started a floor lower: over them
            foreach (bool shell in new[] { false, true })
            {
                var (_, b, s) = Block(shell);
                s.top = 1;
                b.floors[2].shape = new List<Vec2>(Narrower);
                Assert.Equal(shell, Setbacks.Move(b, 2, 1));
            }
        }

        [Fact]
        public void AnotherFootprintKeepsAShellsStairs()
        {
            var (_, walkIn, _) = Block(false);
            Setbacks.ApplyFootprint(walkIn, Narrower);
            Assert.Empty(walkIn.shafts);   // walk-in: stairs that no longer fit go
            var (_, shell, s) = Block(true);
            Setbacks.ApplyFootprint(shell, Narrower);
            Assert.Same(s, shell.shafts.Single());
        }

        [Fact]
        public void AnAtriumOverAShellsStairsFits()
        {
            var (_, walkIn, s) = Block(false);
            var over = new List<Vec2> { new Vec2(1.6, 2), new Vec2(6, 2), new Vec2(6, 8), new Vec2(1.6, 8) };
            var v = new VoidData { id = "v1", kind = VoidKind.Atrium, bottom = 0, shape = over };
            Assert.Equal(VoidIssue.Core, Voids.Issue(walkIn, v));
            var (_, shell, _) = Block(true);
            Assert.Equal(VoidIssue.None, Voids.Issue(shell, v));
            shell.voids.Add(v);
            Assert.Equal(VoidIssue.None, Voids.SetBottom(shell, "v1", 1));
        }
    }
}
