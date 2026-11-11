using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Text;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The Interior tab's Copy and Paste (<see cref="Floors.CopyRooms"/>, <see cref="Floors.PasteRooms"/>): a floor's rooms, its
    /// walls and their doors, as text, put in place of another floor's, of the same building or another. Storey's own, in place
    /// of the prototype's copy up.
    /// </summary>
    public class CopyRoomsTests
    {
        static (StoreyDocument d, BuildingData b) Block(int floors = 3)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));   // 12 × 9 m
            b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
            return (d, b);
        }

        // two rooms and a corridor wall, with doors
        static List<WallData> Rooms() => new List<WallData>
        {
            new WallData { a = new Vec2(6, 0), b = new Vec2(6, 9), doors = { new DoorData { t = 0.25 } } },
            new WallData { a = new Vec2(0, 4.5), b = new Vec2(6, 4.5), doors = { new DoorData { t = 0.5 }, new DoorData { t = 0.85 } } },
        };

        static string Walls(FloorData f) => PrototypeJson.WriteRooms(f);

        [Fact]
        public void PasteGivesAnotherFloorTheRoomsCopied()
        {
            var (_, b) = Block();
            b.floors[1].walls = Rooms();
            b.floors[2].h = 3.4; b.floors[2].filled = true;
            b.floors[2].walls.Add(new WallData { a = new Vec2(1, 1), b = new Vec2(1, 8) });
            string copied = Floors.CopyRooms(b, 1);
            Assert.Equal(2, Floors.RoomsWalls(copied));
            Assert.True(Floors.PasteRooms(b, 2, copied));
            Assert.Equal(Walls(b.floors[1]), Walls(b.floors[2]));   // in place of its own wall, doors and all
            Assert.Equal(3.4, b.floors[2].h);                        // and nothing else of the floor changes
            Assert.True(b.floors[2].filled);
            // its own: changing one floor's rooms leaves the other's
            b.floors[2].walls[0].doors.Clear();
            Assert.Single(b.floors[1].walls[0].doors);
        }

        [Fact]
        public void TheCopyIsTheRoomsAsTheyWereWhenCopied()
        {
            var (_, b) = Block();
            b.floors[0].walls = Rooms();
            string copied = Floors.CopyRooms(b, 0);
            b.floors[0].walls.RemoveAt(0);
            Assert.True(Floors.PasteRooms(b, 1, copied));
            Assert.Equal(2, b.floors[1].walls.Count);
            Assert.Equal(3, b.floors[1].walls.Sum(w => w.doors.Count));
        }

        [Fact]
        public void RoomsPasteIntoAnotherBuilding()
        {
            var (_, a) = Block();
            var (_, c) = Block(floors: 5);
            a.floors[0].walls = Rooms();
            Assert.True(Floors.PasteRooms(c, 4, Floors.CopyRooms(a, 0)));
            Assert.Equal(Walls(a.floors[0]), Walls(c.floors[4]));
        }

        [Fact]
        public void AFloorWithoutRoomsPastesNone()
        {
            var (_, b) = Block();
            b.floors[1].walls = Rooms();
            string empty = Floors.CopyRooms(b, 0);
            Assert.Equal(0, Floors.RoomsWalls(empty));
            Assert.True(Floors.PasteRooms(b, 1, empty));
            Assert.Empty(b.floors[1].walls);
        }

        [Fact]
        public void TextThatIsNotRoomsPastesNothing()
        {
            var (_, b) = Block();
            b.floors[1].walls = Rooms();
            string before = Walls(b.floors[1]);
            foreach (var text in new[] { "", "hello", "{\"v\":1}", "[1,2]", "{\"walls\":3}", "{\"walls\":[{\"a\":1}]}" })
            {
                Assert.Equal(-1, Floors.RoomsWalls(text));
                Assert.False(Floors.PasteRooms(b, 1, text));
                Assert.Equal(before, Walls(b.floors[1]));
            }
            Assert.False(Floors.PasteRooms(b, 3, Floors.CopyRooms(b, 1)));   // no storey 3: the roof has no rooms
        }

        [Fact]
        public void TheTextHoldsTheRoomsAlone()
        {
            var (_, b) = Block();
            b.floors[1].walls = Rooms(); b.floors[1].h = 4; b.floors[1].filled = true;
            var o = (Dictionary<string, object?>)Json.Parse(Floors.CopyRooms(b, 1))!;
            Assert.Equal(new[] { "walls" }, o.Keys);
        }
    }
}
