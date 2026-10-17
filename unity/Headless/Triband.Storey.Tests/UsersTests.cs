using System.Collections.Generic;
using Triband.Storey.Lod;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Who uses a layout, and when its parse is let go (docs/PERFORMANCE.md §6.5): <see cref="Users{T}"/>, which
    /// <c>StoreyDocumentAsset</c> keeps for the sites in Play mode.
    /// </summary>
    public class UsersTests
    {
        sealed class Layout { public readonly string name; public Layout(string n) { name = n; } public override string ToString() => name; }

        [Fact]
        public void ALayoutGoesOnlyOnceNoOneUsesItAndItFallsOffTheKeptOnes()
        {
            var u = new Users<Layout>(2); var gone = new List<Layout>();
            Layout a = new Layout("a"), b = new Layout("b"), c = new Layout("c");
            u.Use(a); u.Use(a);                    // a site and its neighbour's
            u.Release(a, gone);
            Assert.Equal(1, u.Count(a)); Assert.False(u.Kept(a)); Assert.Empty(gone);
            u.Release(a, gone);
            Assert.Equal(0, u.Count(a)); Assert.True(u.Kept(a)); Assert.Empty(gone);
            u.Use(b); u.Release(b, gone);
            u.Use(c); u.Release(c, gone);          // a third let go: the oldest goes
            Assert.Equal(new[] { a }, gone);
            Assert.False(u.Kept(a)); Assert.True(u.Kept(b)); Assert.True(u.Kept(c));
        }

        [Fact]
        public void ALayoutUsedAgainIsTakenBackFromTheKeptOnes()
        {
            var u = new Users<Layout>(1); var gone = new List<Layout>();
            Layout a = new Layout("a"), b = new Layout("b");
            u.Use(a); u.Release(a, gone);
            u.Use(a);                              // the player turned back
            Assert.False(u.Kept(a)); Assert.Equal(1, u.Count(a));
            u.Use(b); u.Release(b, gone);
            Assert.Empty(gone);                    // a is in use: only b is kept
            u.Release(a, gone);
            Assert.Equal(new[] { b }, gone);
        }

        [Fact]
        public void ReleasingWhatNoOneUsesDoesNothing()
        {
            var u = new Users<Layout>(0); var gone = new List<Layout>();
            var a = new Layout("a");
            u.Release(a, gone);
            Assert.Empty(gone); Assert.False(u.Kept(a));
            u.Use(a); u.Release(a, gone); u.Release(a, gone);
            Assert.Equal(new[] { a }, gone);       // keeping none: let go at once, and once
        }

        [Fact]
        public void KeepingFewerLetsTheOldestGo()
        {
            var u = new Users<Layout>(3); var gone = new List<Layout>();
            var all = new[] { new Layout("a"), new Layout("b"), new Layout("c") };
            foreach (var l in all) { u.Use(l); u.Release(l, gone); }
            Assert.Empty(gone);
            u.Keep = 1; u.Trim(gone);
            Assert.Equal(new[] { all[0], all[1] }, gone);
            u.Keep = -4;
            Assert.Equal(0, u.Keep);
        }

        /// <summary>Layouts are told apart by reference: Unity's objects compare by their native object, and two equal texts are two layouts.</summary>
        [Fact]
        public void LayoutsAreToldApartByReference()
        {
            var u = new Users<string>(0); var gone = new List<string>();
            string a = new string('x', 3), b = new string('x', 3);
            u.Use(a); u.Use(b); u.Release(a, gone);
            Assert.Single(gone); Assert.Same(a, gone[0]);
            Assert.Equal(1, u.Count(b));
            u.Clear();
            Assert.Equal(0, u.Count(b));
        }
    }
}
