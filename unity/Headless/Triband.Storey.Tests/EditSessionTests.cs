using System.Linq;
using Triband.Storey.Edit;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The edit session's bookkeeping (docs/EDITOR.md slice 6.3): what an edit or an undo makes the scene build again.</summary>
    public class EditSessionTests
    {
        static EditSession Demo() => new EditSession(Fixtures.Text("demo.json"));
        static int Index(EditSession s, string name) => s.Document.buildings.FindIndex(b => b.name == name);

        [Fact]
        public void TheTextIsTheLayoutAsStoreyWritesIt()
        {
            var s = Demo();
            Assert.Equal(PrototypeJson.Write(PrototypeJson.Read(Fixtures.Text("demo.json")).Document), s.Text);
            Assert.True(s.Commit().None);
            Assert.True(s.Load(s.Text).None);
        }

        [Fact]
        public void AnEditRebuildsTheBuildingAndTheNeighboursItTouches()
        {
            var s = Demo();
            var row = s.Document.buildings[Index(s, "Row House")];
            Floors.SetCount(row, row.floors.Count + 2);
            var c = s.Commit();
            Assert.False(c.structural);
            // the Row House stands between the Corner Shop and the Dockside Store: both share a wall with it
            var names = c.rebuild.Select(id => s.Document.buildings.First(b => b.id == id).name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "Corner Shop", "Dockside Store", "Row House" }, names);
            Assert.True(s.Commit().None);
        }

        [Fact]
        public void AnIsolatedBuildingRebuildsAlone()
        {
            var s = Demo();
            var tower = s.Document.buildings[Index(s, "Westgate Tower")];
            Floors.AddTop(tower);
            Assert.Equal(new[] { tower.id }, s.Commit().rebuild.ToArray());
        }

        [Fact]
        public void MovingAwayRebuildsTheOldNeighbours()
        {
            var s = Demo();
            var row = s.Document.buildings[Index(s, "Row House")];
            row.pos = new Vec2(row.pos.x, row.pos.z + 40);   // nowhere near anything now
            var names = s.Commit().rebuild.Select(id => s.Document.buildings.First(b => b.id == id).name).ToHashSet();
            Assert.Contains("Corner Shop", names);
            Assert.Contains("Dockside Store", names);
        }

        [Fact]
        public void UndoComesBackAsTheSameBuildingsChanging()
        {
            var s = Demo();
            string before = s.Text;
            var tower = s.Document.buildings[Index(s, "Westgate Tower")];
            Floors.SetCount(tower, 3);
            s.Commit();
            var undo = s.Load(before);
            Assert.Equal(new[] { tower.id }, undo.rebuild.ToArray());
            Assert.Equal(before, s.Text);
        }

        [Fact]
        public void AddingOrRemovingABuildingRebuildsEverything()
        {
            var s = Demo();
            s.Document.buildings.RemoveAt(0);
            Assert.True(s.Commit().structural);
        }

        /// <summary>Assets > Create > Storey > Layout writes an empty document: it must read back, and take a first building.</summary>
        [Fact]
        public void AnEmptyLayoutStartsAnEdit()
        {
            var s = new EditSession(PrototypeJson.Write(new StoreyDocument()));
            Assert.Empty(s.Document.buildings);
            var b = Buildings.Add(s.Document, "rect", new Vec2(0, 0));
            Assert.True(s.Commit().structural);
            Assert.Equal(3, PrototypeJson.Read(s.Text).Document.buildings[0].floors.Count);
            Assert.Equal("Alder House", b.name);
        }
    }
}

namespace Triband.Storey.Tests
{
    /// <summary>The edit session's shortcuts for big layouts: a drag step puts back one building, a refused edit only what it changed.</summary>
    public class EditSessionShortcutTests
    {
        static Edit.EditSession City() { var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; Edit.TestCity.Generate(d, 200); return new Edit.EditSession(PrototypeJson.Write(d)); }

        [Fact]
        public void RestoringTheDraggedBuildingIsTheDragStart()
        {
            var s = City(); string start = s.Text;
            var b = s.Document.buildings[3]; string json = PrototypeJson.Write(b);
            b.pos = new Vec2(b.pos.x + 3, b.pos.z); s.Commit();
            Assert.NotEqual(start, s.Text);
            Assert.True(s.Restore(b.id, json));
            var c = s.Commit();
            Assert.Equal(start, s.Text);
            Assert.Contains(b.id, c.rebuild);
            Assert.False(s.Restore("no-such-building", json));
        }

        [Fact]
        public void DiscardPutsBackOnlyWhatChanged()
        {
            var s = City(); string start = s.Text;
            var other = s.Document.buildings[7];
            s.Document.buildings[2].floorHeight += 1; s.Document.buildings[5].floors.RemoveAt(0);
            s.Discard();
            Assert.Same(other, s.Document.buildings[7]);   // untouched buildings are kept, not read again
            Assert.Equal(start, PrototypeJson.Write(s.Document));
            Assert.True(s.Commit().None);
        }

        [Fact]
        public void DiscardReadsEverythingWhenBuildingsCameOrWent()
        {
            var s = City(); string start = s.Text;
            s.Document.buildings.RemoveAt(4); s.Document.palette["00000000000000000000000000000009"] = new PaletteEntry { name = "x", hex = "#123456" };
            s.Discard();
            Assert.Equal(start, PrototypeJson.Write(s.Document));
        }
    }
}

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A preview (<see cref="EditSession.With"/>): a building shown as an edit would make it, a preset hovered in the Facade
    /// tab's list, with the layout left as it is, and the same buildings built again as the edit would build.
    /// </summary>
    public class EditSessionPreviewTests
    {
        static EditSession Demo() => new EditSession(Fixtures.Text("demo.json"));
        static BuildingData Copy(BuildingData b) => PrototypeJson.ReadBuilding(PrototypeJson.Write(b));
        static string[] Names(EditSession s, SessionChange c) => c.rebuild.Select(id => s.Document.buildings.First(b => b.id == id).name).OrderBy(n => n).ToArray();

        [Fact]
        public void APreviewLeavesTheLayoutAsItIs()
        {
            var s = Demo(); string before = s.Text;
            int i = s.Document.buildings.FindIndex(b => b.name == "Row House");
            var row = s.Document.buildings[i];
            var glass = Copy(row); Styles.ApplyPreset(glass, 0, "glass");
            var (doc, _) = s.With(glass)!.Value;
            Assert.Same(glass, doc.buildings[i]);
            Assert.Same(row, s.Document.buildings[i]);   // the layout keeps its own
            Assert.NotSame(s.Document.buildings, doc.buildings);
            for (int j = 0; j < doc.buildings.Count; j++) if (j != i) Assert.Same(s.Document.buildings[j], doc.buildings[j]);
            Assert.Equal(before, s.Text);
            Assert.Equal(before, PrototypeJson.Write(s.Document));
            Assert.True(s.Commit().None);   // an edit after it starts from the layout
        }

        [Fact]
        public void APreviewBuildsAgainWhatTheEditWould()
        {
            var shown = Demo(); var edited = Demo();
            var preview = Copy(shown.Document.buildings.First(b => b.name == "Row House"));
            Styles.ApplyPreset(preview, 0, "glass");
            var change = shown.With(preview)!.Value.change;
            Styles.ApplyPreset(edited.Document.buildings.First(b => b.name == "Row House"), 0, "glass");
            var edit = edited.Commit();
            Assert.False(change.structural);
            Assert.Equal(Names(edited, edit), Names(shown, change));
            // the Row House stands between the Corner Shop and the Dockside Store: their party walls are built again too
            Assert.Equal(new[] { "Corner Shop", "Dockside Store", "Row House" }, Names(shown, change));
        }

        [Fact]
        public void APreviewOfABridgedBuildingBuildsTheOtherEnd()
        {
            var (d, A, B) = BridgeTests.Street();
            BridgeTests.Add(d, A);   // A builds the bridge, and B a door for it
            var s = new EditSession(PrototypeJson.Write(d));
            var b = Copy(s.Document.buildings.First(x => x.id == B.id)); Styles.ApplyPreset(b, 0, "warehouse");
            var c = s.With(b)!.Value.change;
            Assert.Contains(A.id, c.rebuild); Assert.Contains(B.id, c.rebuild);
        }

        [Fact]
        public void APreviewLikeTheLayoutBuildsNothing()
        {
            var s = Demo();
            Assert.True(s.With(Copy(s.Document.buildings[2]))!.Value.change.None);
        }

        [Fact]
        public void ABuildingNotInTheLayoutHasNoPreview() => Assert.Null(Demo().With(new BuildingData { id = "no-such-building" }));

        [Fact]
        public void AnUncommittedLayoutBuildsEverything()
        {
            var s = Demo();
            var last = Copy(s.Document.buildings[^1]);
            s.Document.buildings.RemoveAt(0);   // changed, and not committed
            Assert.True(s.With(last)!.Value.change.structural);
        }
    }
}
