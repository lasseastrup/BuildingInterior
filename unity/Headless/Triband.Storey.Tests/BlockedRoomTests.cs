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
    /// Rooms blocked out (<see cref="FloorData.blocked"/>, the Interior tab's Block tool): the room holding a mark is solid to
    /// the ceiling, the doorways into it shut, the windows and doors over it opaque and closed as a filled storey's, and
    /// nobody walks into it. Storey's own.
    /// </summary>
    public class BlockedRoomTests
    {
        // 12 × 9 m, three storeys; on storey 1 a wall at x = 6 with a doorway in its middle: a west room and an east room
        static (StoreyDocument d, BuildingData b) Block(bool door = true)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList();
            b.interior = true; b.shafts.Clear();
            var w = new WallData { a = new Vec2(6, 0), b = new Vec2(6, 9) };
            if (door) w.doors.Add(new DoorData { t = 0.5 });
            b.floors[1].walls.Add(w);
            return (d, b);
        }

        static readonly Vec2 East = new Vec2(9, 4.5), West = new Vec2(3, 4.5);

        [Fact]
        public void ARoomIsTheStoreyLessItsWalls()
        {
            var (_, b) = Block();
            var all = Rooms.All(b, 1);
            Assert.Equal(2, all.Count);
            double hw = Dim.T_INT / 2;
            foreach (var r in all) Assert.Equal((6 - hw) * 9, Math.Abs(Geo.Area2(r.ring)) / 2, 6);   // the doorway joins no rooms
            Assert.True(Rooms.At(b, 1, East)!.Contains(11, 8));
            Assert.False(Rooms.At(b, 1, East)!.Contains(1, 1));
            Assert.Null(Rooms.At(b, 1, new Vec2(6, 2)));                                            // in the wall
            Assert.Single(Rooms.All(b, 0));                                                          // no walls: one room
        }

        [Fact]
        public void TheBlockToolBlocksARoomAndOpensItAgain()
        {
            var (_, b) = Block();
            Assert.Null(Walls.ToggleBlock(b, 1, East));
            Assert.Single(b.floors[1].blocked);
            Assert.Single(Rooms.Blocked(b, 1));
            Assert.Null(Walls.ToggleBlock(b, 1, new Vec2(11, 1)));                                  // anywhere in it opens it
            Assert.Empty(b.floors[1].blocked);
            Assert.Contains("wall", Walls.ToggleBlock(b, 1, new Vec2(6, 2)));
            Assert.Contains("Filled", Walls.ToggleBlock(b, 0, East));                              // the whole floor
            b.shafts.Add(new CoreData { id = "s", type = CoreType.Lift, x = 9, z = 4.5, bottom = 0 });
            Assert.Contains("lift", Walls.ToggleBlock(b, 1, East));
            Assert.Empty(b.floors[1].blocked);
        }

        static (Lod0Result r, Site site) Built(BuildingData b, StoreyDocument d) { var site = new Site(d.buildings); return (Lod0.Build(site, b, solids: true), site); }

        [Fact]
        public void ItIsSolidToTheCeilingAndItsDoorwayIsShut()
        {
            var (d, b) = Block();
            var L0 = new Lod0.Shared(new Site(d.buildings), b);
            Assert.Single(L0.RoomWalls(1)[0].ops);                                                   // the doorway, before
            Walls.ToggleBlock(b, 1, East);
            L0 = new Lod0.Shared(new Site(d.buildings), b);
            Assert.Empty(L0.RoomWalls(1)[0].ops);                                                    // the wall goes on across it
            // its top inside the slab over it, its foot a centimetre over the floor, a centimetre in from the walls round it
            var op = Built(b, d).r.Op;
            double y = Derived.FloorBase(b, 1), top = y + Derived.FloorH(b, 1) - Dim.SLAB + 0.005;
            var inner = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Interior && op.P[v].x > 6 + Dim.T_INT / 2 + 0.005).Select(v => op.P[v]).ToList();
            Assert.Contains(inner, p => Math.Abs(p.y - top) < 1e-9);
            Assert.Contains(inner, p => Math.Abs(p.y - (y + 0.01)) < 1e-9);
            Assert.Equal(6 + Dim.T_INT / 2 + 0.01, inner.Where(p => Math.Abs(p.y - top) < 1e-9).Min(p => p.x), 6);
            Assert.Equal(12 - 0.01, inner.Where(p => Math.Abs(p.y - top) < 1e-9).Max(p => p.x), 6);
        }

        [Fact]
        public void ItBuildsWithoutFacesThatFlicker()
        {
            foreach (bool door in new[] { true, false })
            {
                var (d, b) = Block(door);
                Walls.ToggleBlock(b, 1, East);
                var r = CoplanarCheck.Run(Built(b, d).r.Op);
                Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(10)));
            }
        }

        [Fact]
        public void TheWindowsOverItAreShut()
        {
            // glass on storey 1's east wall (x = 12) and on the west room's walls
            static (int east, int west) Glass(BuildingData b, StoreyDocument d)
            {
                var gl = Built(b, d).r.Glass; double y0 = Derived.FloorBase(b, 1), y1 = Derived.FloorBase(b, 2);
                var on = Enumerable.Range(0, gl.Verts).Select(v => gl.P[v]).Where(p => p.y > y0 && p.y < y1).ToList();
                return (on.Count(p => p.x > 12 - 1e-6), on.Count(p => p.x < 1e-6));
            }
            var (d, b) = Block();
            var open = Glass(b, d);
            Assert.True(open.east > 0 && open.west > 0);
            Walls.ToggleBlock(b, 1, East);
            var shut = Glass(b, d);
            Assert.Equal(0, shut.east);
            Assert.Equal(open.west, shut.west);
        }

        [Fact]
        public void NobodyWalksIntoIt()
        {
            // its outline is a wall to the walk model, across the doorway too
            var (d2, b2) = Block();
            Walls.ToggleBlock(b2, 1, East);
            var segs = Built(b2, d2).r.Segs[1];
            double x = 6 + Dim.T_INT / 2;
            Assert.Contains(segs, s => Math.Abs(s.ax - x) < 1e-6 && Math.Abs(s.bx - x) < 1e-6 && Math.Min(s.az, s.bz) < 4 && Math.Max(s.az, s.bz) > 5);
        }

        [Fact]
        public void AMarkAWallLandsOnBlocksNothingUntilItMovesOff()
        {
            var (_, b) = Block();
            Walls.ToggleBlock(b, 1, East);
            Assert.Single(Rooms.Blocked(b, 1));
            b.floors[1].walls.Add(new WallData { a = new Vec2(9, 0), b = new Vec2(9, 9) });        // a wall on its mark
            Assert.Empty(Rooms.Blocked(b, 1));                                                       // it blocks nothing
            Assert.Single(b.floors[1].blocked);                                                      // and is kept
            b.floors[1].walls.RemoveAt(1);
            Assert.Single(Rooms.Blocked(b, 1));                                                      // the wall gone, the room is blocked again
        }

        [Fact]
        public void OnAFilledOrShellStoreyItBlocksNothing()
        {
            var (_, b) = Block();
            Walls.ToggleBlock(b, 1, East);
            b.floors[1].filled = true;
            Assert.Empty(Rooms.Blocked(b, 1));
            b.floors[1].filled = false; b.interior = false;
            Assert.Empty(Rooms.Blocked(b, 1));
        }

        [Fact]
        public void TheMarksAreKeptWithTheRooms()
        {
            var (_, b) = Block();
            Walls.ToggleBlock(b, 1, East);
            string json = PrototypeJson.Write(b);
            Assert.Contains("\"blocked\":[{\"x\":9,\"z\":4.5}]", json);
            var back = PrototypeJson.ReadBuilding(json);
            Assert.Equal(new[] { East }, back.floors[1].blocked);
            Assert.Equal(json, PrototypeJson.Write(back));
            // copied and pasted with the walls, and onto new floors with the top floor's rooms
            Assert.True(Floors.PasteRooms(b, 2, Floors.CopyRooms(b, 1)));
            Assert.Equal(new[] { East }, b.floors[2].blocked);
            Floors.AddTop(b);
            Assert.Equal(new[] { East }, b.floors[3].blocked);
            b.floors[3].blocked.Clear();
            Assert.Single(b.floors[2].blocked);                                                      // a copy each
        }
    }
}
