using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The ids of the assets a layout names by id (<see cref="AssetIds"/>, docs/EDITOR.md §6.9, §6.12): a copy is a kind of
    /// its own, and the ids layouts already name are kept.
    /// </summary>
    public class AssetIdsTests
    {
        static AssetIds.Entry E(string path, string guid, string id, bool fresh = false, bool package = false) =>
            new AssetIds.Entry { path = path, guid = guid, id = id, fresh = fresh, package = package };

        static Dictionary<int, AssetIds.Change> Settle(params AssetIds.Entry[] e) => AssetIds.Settle(e, DetailKinds.IsBuiltIn).ToDictionary(c => c.asset);

        const string G1 = "11111111111111111111111111111111", G2 = "22222222222222222222222222222222", G3 = "33333333333333333333333333333333";
        const string Old = "0123456789abcdef0123456789abcdef";   // a window made before ids were GUIDs

        [Fact]
        public void ANewOneTakesItsGuidAndTheIdsLayoutsNameStay()
        {
            var c = Settle(E("Assets/New.asset", G1, ""), E("Assets/Old.asset", G2, Old), E("Assets/Mine.asset", G3, G3));
            Assert.Equal((G1, -1), (c[0].id, c[0].keeper));
            Assert.Single(c);   // the old window's id and the GUID one stay
            // one not saved yet has no GUID to take: it waits
            Assert.Empty(Settle(E("", "", "")));
        }

        [Fact]
        public void ACopyTakesItsOwnAndTheOriginalKeepsItsId()
        {
            // a copy of one whose id is its GUID: the original is known, wherever the copy sorts, and whether or not just imported
            foreach (var fresh in new[] { false, true })
            {
                var c = Settle(E("Assets/A copy.asset", G2, G1, fresh), E("Assets/Window.asset", G1, G1));
                var ch = Assert.Single(c.Values);
                Assert.Equal((0, G2, 1, true), (ch.asset, ch.id, ch.keeper, ch.sure));
            }
            // a copy of an old window: the one just imported is the copy
            var o = Settle(E("Assets/Window 1.asset", G2, Old, fresh: true), E("Assets/Window.asset", G1, Old));
            var oc = Assert.Single(o.Values);
            Assert.Equal((0, G2, 1, true), (oc.asset, oc.id, oc.keeper, oc.sure));
            // three of them: the original keeps it, each copy takes its own
            var t = Settle(E("Assets/C.asset", G3, G1, true), E("Assets/B.asset", G1, G1), E("Assets/A.asset", G2, G1, true));
            Assert.Equal(new[] { 0, 2 }, t.Keys.OrderBy(k => k));
            Assert.Equal((G3, G2), (t[0].id, t[2].id));
            Assert.All(t.Values, x => Assert.Equal(1, x.keeper));
        }

        [Fact]
        public void TwoOldOnesThatShareAnIdSplitByPathAndSaySo()
        {
            // copies made before: nothing shows which is the original, so the first by path keeps the id, and it isn't sure
            var c = Settle(E("Assets/Window.asset", G1, Old), E("Assets/Arch.asset", G2, Old));
            var ch = Assert.Single(c.Values);
            Assert.Equal((0, G1, 1, false), (ch.asset, ch.id, ch.keeper, ch.sure));
            // both just imported (a new checkout): the same
            var f = Settle(E("Assets/Window.asset", G1, Old, true), E("Assets/Arch.asset", G2, Old, true));
            Assert.False(Assert.Single(f.Values).sure);
        }

        [Fact]
        public void OnlyThePackagesFiveHoldTheGeneratorsIds()
        {
            // a copy of the package's AC unit: its own id, the package's keeps "ac"
            var c = Settle(E("Packages/com.triband.storey/Facade Details/AC unit.asset", G1, DetailKinds.Ac, package: true), E("Assets/AC unit 1.asset", G2, DetailKinds.Ac, fresh: true));
            var ch = Assert.Single(c.Values);
            Assert.Equal((1, G2), (ch.asset, ch.id));
            // and one holding "ac" with the package's missing too
            Assert.Equal(G2, Assert.Single(Settle(E("Assets/AC.asset", G2, "ac")).Values).id);
            // the package's own are never changed
            Assert.Empty(Settle(E("Packages/com.triband.storey/Facade Details/Vent.asset", G3, DetailKinds.Vent, package: true)));
        }
    }
}
