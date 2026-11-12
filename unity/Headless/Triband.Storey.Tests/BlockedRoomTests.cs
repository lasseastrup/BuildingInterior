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

        // every LOD0 triangle of storey k turned into room r (building-local): a point just in front of it lies in the room, in the
        // storey's height; with its colour
        static List<(P3 at, ColorSlot slot)> FacingInto(MeshBuilder op, BuildingData b, int k, Rooms.Room r)
        {
            double y0 = Derived.FloorBase(b, k), y1 = y0 + Derived.FloorH(b, k) - Dim.SLAB;
            var o = new List<(P3, ColorSlot)>();
            for (int t = 0; t + 2 < op.I.Count; t += 3)
            {
                int i0 = op.I[t]; P3 A = op.P[i0], B = op.P[op.I[t + 1]], C = op.P[op.I[t + 2]], n = op.N[i0];
                var c = new P3((A.x + B.x + C.x) / 3 + n.x * 0.02, (A.y + B.y + C.y) / 3 + n.y * 0.02, (A.z + B.z + C.z) / 3 + n.z * 0.02);
                if (c.y > y0 && c.y < y1 && r.Contains(c.x - b.pos.x, c.z - b.pos.z)) o.Add((c, op.C[i0].slot));
            }
            return o;
        }

        // the area of the triangles all of whose corners pass
        static double Area(MeshBuilder op, Func<P3, bool> corner)
        {
            double a = 0;
            for (int t = 0; t + 2 < op.I.Count; t += 3)
            {
                P3 A = op.P[op.I[t]], B = op.P[op.I[t + 1]], C = op.P[op.I[t + 2]];
                if (!corner(A) || !corner(B) || !corner(C)) continue;
                var x = P3.Cross(B - A, C - A); a += Math.Sqrt(x.x * x.x + x.y * x.y + x.z * x.z) / 2;
            }
            return a;
        }

        [Fact]
        public void ItIsOneSolidWithTheWallsRoundItAndItsDoorwayIsShut()
        {
            var (d, b) = Block();
            var L0 = new Lod0.Shared(new Site(d.buildings), b);
            Assert.Single(L0.RoomWalls(1)[0].ops);                                                   // the doorway, before
            var room = Rooms.At(b, 1, East)!;
            var before = FacingInto(Built(b, d).r.Op, b, 1, room);
            Assert.Contains(before, f => f.slot == ColorSlot.Interior);                              // the walls' faces, before
            Assert.Contains(before, f => f.slot == ColorSlot.Ceiling);
            double y = Derived.FloorBase(b, 1), hh = Derived.FloorH(b, 1) - Dim.SLAB;
            // a window's reveal runs right through the east wall (x = 12, its inner face, to 12.3): a pocket into the room
            bool Pocket(MeshBuilder m) => Enumerable.Range(0, m.Verts).Any(v => Math.Abs(m.P[v].x - 12) < 1e-6 && m.P[v].y > y + 0.05 && m.P[v].y < y + hh && Math.Abs(m.N[v].x) < 0.5);
            Assert.True(Pocket(Built(b, d).r.Op));
            Walls.ToggleBlock(b, 1, East);
            L0 = new Lod0.Shared(new Site(d.buildings), b);
            Assert.Empty(L0.RoomWalls(1)[0].ops);                                                    // the wall goes on across it
            Assert.Single(L0.MergedRooms(1));
            var op = Built(b, d).r.Op;
            // nothing faces into it but its floor, kept to keep the sun off the storey below: not the walls round it, their
            // ends, its ceiling, or the pockets of the windows over it
            var facing = FacingInto(op, b, 1, room);
            Assert.All(facing, f => Assert.Equal(ColorSlot.Floor, f.slot));
            Assert.NotEmpty(facing);
            Assert.False(Pocket(op));
            // the walls keep their other faces: the room wall's west face is whole, the doorway gone
            double xw = 6 - Dim.T_INT / 2;
            Assert.InRange(Area(op, p => Math.Abs(p.x - xw) < 1e-6 && p.y >= y - 1e-6 && p.y <= y + hh + 1e-6), 9 * hh - 1e-6, 9.2 * hh);
            // closed from below level with the walls' feet, over the whole room
            Assert.Equal((12 - 6 - Dim.T_INT / 2) * 9, Area(op, p => Math.Abs(p.y - (y + Dim.BACK)) < 1e-9 && p.x > 6 + Dim.T_INT / 2 - 1e-6 && p.x < 12 + 1e-6 && p.z > -1e-6 && p.z < 9 + 1e-6), 6);
        }

        [Fact]
        public void OpenToAnAtriumItIsASolidOfItsOwn()
        {
            // an atrium in the east room from the ground floor: on storey 1 the room is open to it, behind its rails
            var (d, b) = Block();
            b.voids.Add(new VoidData { id = "a1", kind = VoidKind.Atrium, bottom = 0, shape = new() { new Vec2(9, 6), new Vec2(11, 6), new Vec2(11, 8), new Vec2(9, 8) } });
            Assert.Null(Walls.ToggleBlock(b, 1, East));
            var L0 = new Lod0.Shared(new Site(d.buildings), b);
            Assert.Single(L0.BlockedRooms(1));
            Assert.Empty(L0.MergedRooms(1));
            // the walls round it keep their faces, and it is a solid a centimetre inside them, its top in the slab over it
            var r = Built(b, d).r;
            Assert.Contains(FacingInto(r.Op, b, 1, L0.BlockedRooms(1)[0]), f => f.slot == ColorSlot.Interior);
            double top = Derived.FloorBase(b, 1) + Derived.FloorH(b, 1) - Dim.SLAB + 0.005;
            Assert.Contains(Enumerable.Range(0, r.Op.Verts), v => Math.Abs(r.Op.P[v].y - top) < 1e-9 && Math.Abs(r.Op.P[v].x - (6 + Dim.T_INT / 2 + 0.01)) < 1e-9);
            var c = CoplanarCheck.Run(r.Op);
            Assert.True(c.Overlaps.Count == 0, string.Join("\n", c.Overlaps.Take(10)));
        }

        [Fact]
        public void OnACourtyardItsWindowsThereAreShutAndItsWallsMergeWithIt()
        {
            // a court (6..14 × 5..11) from the ground; on storey 1 a room wall each side of it: a west room and an east one
            var (d, b) = CourtyardTests.Block(floors: 3);
            b.interior = true;
            CourtyardTests.Court(b);
            b.floors[1].walls.Add(new WallData { a = new Vec2(10, 0), b = new Vec2(10, 5) });
            b.floors[1].walls.Add(new WallData { a = new Vec2(10, 11), b = new Vec2(10, 16) });
            double y0 = Derived.FloorBase(b, 1), y1 = Derived.FloorBase(b, 2);
            int Glass(double x0, double x1)
            {
                var gl = Built(b, d).r.Glass;
                return Enumerable.Range(0, gl.Verts).Select(v => gl.P[v]).Count(p => p.y > y0 && p.y < y1 && p.x > x0 && p.x < x1 && p.z > 5 && p.z < 11);
            }
            int west = Glass(5.5, 6.5);
            Assert.True(Glass(13.5, 14.5) > 0 && west > 0);                                       // the court's east and west walls
            Assert.Null(Walls.ToggleBlock(b, 1, new Vec2(17, 8)));
            var L0 = new Lod0.Shared(new Site(d.buildings), b);
            Assert.Single(L0.MergedRooms(1));
            Assert.Equal(0, Glass(13.5, 14.5));                                                    // shut into the room
            Assert.Equal(west, Glass(5.5, 6.5));
            var r = Built(b, d).r;
            Assert.All(FacingInto(r.Op, b, 1, L0.MergedRooms(1)[0]), f => Assert.Equal(ColorSlot.Floor, f.slot));
            var c = CoplanarCheck.Run(r.Op);
            Assert.True(c.Overlaps.Count == 0, string.Join("\n", c.Overlaps.Take(10)));
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
