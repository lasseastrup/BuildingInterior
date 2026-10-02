using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The editor's core drag (<see cref="Shafts.Slide"/>): where the prototype's drag stops dead because the pointer has
    /// gone past a wall, the core still follows it along the wall. Storey's own behaviour.
    /// </summary>
    public class CoreSlideTests
    {
        static (StoreyDocument d, BuildingData b) Block()
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));   // 12 × 9 m, 3 floors
            b.interior = true;
            return (d, b);
        }

        static CoreData Core(StoreyDocument d, BuildingData b, CoreType t, double x, double z) =>
            Shafts.Place(b, 0, new Vec2(x, z), t, 0, Shafts.NewId(d))!;

        [Theory]
        [InlineData(CoreType.Stairs)]
        [InlineData(CoreType.Lift)]
        [InlineData(CoreType.Flight)]
        public void PastTheBackWallItStillFollowsAlongIt(CoreType t)
        {
            var (d0, b0) = Block(); var s0 = Core(d0, b0, t, 6, 4.5);
            Assert.False(Shafts.Drag(b0, s0, new Vec2(s0.x, s0.z), new Vec2(4, 30)));   // the prototype's drag refuses outright
            var (d, b) = Block();
            var s = Core(d, b, t, 6, 4.5);
            var grab = new Vec2(s.x, s.z);
            Assert.True(Shafts.Slide(b, s, grab, new Vec2(4, 30)));
            Assert.Equal(4, s.x, 6);                                             // along the wall: all the way
            Assert.True(s.z > 4.5);                                              // towards it: as far as it fits
            Assert.True(Tiers.ShaftFits(b, s));
            Assert.False(Shafts.Slide(b, s, new Vec2(s.x, s.z), new Vec2(s.x, 31)));   // pressed against it, nothing more to give
        }

        [Fact]
        public void PastASideWallItStillFollowsUpAndDown()
        {
            var (d, b) = Block();
            var s = Core(d, b, CoreType.Lift, 6, 4.5);
            var grab = new Vec2(s.x, s.z);
            Assert.True(Shafts.Slide(b, s, grab, new Vec2(40, 3)));
            Assert.Equal(3, s.z, 6);
            Assert.True(s.x > 9);
            Assert.True(Tiers.ShaftFits(b, s));
        }

        [Fact]
        public void StairsArePulledFlushOntoTheWallTheyArePressedAgainst()
        {
            // switchback stairs may share the back wall: pressed past it they end up on it
            var (d, b) = Block();
            var s = Core(d, b, CoreType.Stairs, 6, 4.5);
            Shafts.Slide(b, s, new Vec2(s.x, s.z), new Vec2(6, 30));
            Assert.NotNull(Cores.CoreFlush(b.footprint, s)[3]);
        }

        [Fact]
        public void ItSlidesAlongAnotherCoreToo()
        {
            var (d, b) = Block();
            var lift = Core(d, b, CoreType.Lift, 2, 4.5);
            var other = Core(d, b, CoreType.Lift, 6, 4.5);
            var grab = new Vec2(other.x, other.z);
            Assert.True(Shafts.Slide(b, other, grab, new Vec2(1, 6)));   // into the first lift: stops beside it, still rises
            Assert.False(Shafts.Overlap(lift, other));
            Assert.Equal(6, other.z, 6);
        }

        [Fact]
        public void AFreeDragIsTheDrag()
        {
            var (d0, b0) = Block(); var c = Core(d0, b0, CoreType.Stairs, 6, 4.5);
            var (d, b) = Block(); var a = Core(d, b, CoreType.Stairs, 6, 4.5);
            var grab = new Vec2(a.x, a.z);
            Assert.True(Shafts.Drag(b0, c, grab, new Vec2(5, 4)));
            Assert.True(Shafts.Slide(b, a, grab, new Vec2(5, 4)));
            Assert.Equal((c.x, c.z), (a.x, a.z));
        }
    }
}
