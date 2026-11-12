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
    /// A door set in a room wall's doorway (<see cref="DoorData.door"/>, the Interior tab's Door tool with Door picked): the
    /// project's door, facing the side of the wall it was clicked from (<see cref="DoorData.flip"/>), in a doorway its own size
    /// where it keeps its size; at LOD0 its frame with its leaf left out, as a walk-in storey's street door, or, with an Open
    /// prefab (<see cref="OpeningKind.live"/>), none of it, the prefab going live there (<see cref="LiveDoor"/>), as it does
    /// for a walk-in street door with one. Storey's own.
    /// </summary>
    public class InteriorDoorTests
    {
        // a leaf in front of the door's pivot and a frame round it 10 cm behind, w × h: a door made for a room wall
        static OpeningKind Door(string id, double w = 0.9, double h = 2.05, bool stretch = false, bool live = false)
        {
            var P = new List<P3>(); var leaf = new List<int>(); var frame = new List<int>();
            void Q(double x0, double y0, double x1, double y1, double z, List<int> into)
            {
                int s = P.Count;
                P.AddRange(new[] { new P3(x0, y0, z), new P3(x1, y0, z), new P3(x1, y1, z), new P3(x0, y1, z) });
                into.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Q(-w / 2 + 0.1, 0, w / 2 - 0.1, h - 0.1, 0.03, leaf);
            Q(-w / 2, 0, w / 2, h, -0.1, frame);
            var m = new OpeningKind.Mesh { P = P.ToArray(), N = Enumerable.Repeat(new P3(0, 0, 1), P.Count).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Door, leaf = true, tris = leaf.ToArray() });
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = frame.ToArray() });
            return new OpeningKind { id = id, name = id, door = true, live = live, w = w, h = h, stretch = stretch, lod0 = m };
        }

        // 12 × 9 m, three storeys; on storey 1 a wall at x = 6 from z = 0 to 9 with a doorway or a door in its middle:
        // a west room and an east room
        static (StoreyDocument d, BuildingData b) Block(string? door = null, bool flip = false, bool doorway = true)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.floors = Enumerable.Range(0, 3).Select(_ => new FloorData()).ToList();
            b.interior = true; b.shafts.Clear(); b.details.Clear();
            var w = new WallData { a = new Vec2(6, 0), b = new Vec2(6, 9) };
            if (doorway) w.doors.Add(new DoorData { t = 0.5, door = door, flip = flip });
            b.floors[1].walls.Add(w);
            return (d, b);
        }

        // the openings LOD0 makes in the one room wall on storey 1
        static List<Opening> Ops(StoreyDocument d, BuildingData b) => new Lod0.Shared(new Site(d.buildings), b).RoomWalls(1).Single().ops;

        // what LOD0 has round the middle of the wall on storey 1
        static List<int> AtDoor(MeshBuilder op, BuildingData b)
        {
            double y = Derived.FloorBase(b, 1);
            return Enumerable.Range(0, op.Verts).Where(v => Math.Abs(op.P[v].x - 6) < 0.3 && op.P[v].y > y + 0.005 && op.P[v].y < y + 2.5 && Math.Abs(op.P[v].z - 4.5) < 0.8).ToList();
        }

        [Fact]
        public void ADoorwayKeepsItsDoorAndWhichWayItFaces()
        {
            var f = new FloorData();
            f.walls.Add(new WallData { a = new Vec2(6, 0), b = new Vec2(6, 9) });
            f.walls[0].doors.Add(new DoorData { t = 0.25 });
            // a doorway is written as the prototype's: its t alone
            string plain = PrototypeJson.WriteRooms(f);
            Assert.DoesNotContain("\"door\"", plain);
            Assert.DoesNotContain("\"flip\"", plain);
            f.walls[0].doors.Add(new DoorData { t = 0.75, door = "idoor-json", flip = true });
            var back = PrototypeJson.ReadRooms(PrototypeJson.WriteRooms(f))!;
            var doors = back.walls[0].doors;
            Assert.Equal(2, doors.Count);
            Assert.Null(doors[0].door); Assert.False(doors[0].flip);
            Assert.Equal("idoor-json", doors[1].door); Assert.True(doors[1].flip); Assert.Equal(0.75, doors[1].t, 9);
            // the whole layout, and its baked form
            var (d, b) = Block("idoor-json", flip: true);
            var read = PrototypeJson.Read(PrototypeJson.Write(d));
            Assert.Empty(read.Unknown);
            var rd = read.Document.buildings[0].floors[1].walls[0].doors.Single();
            Assert.Equal("idoor-json", rd.door); Assert.True(rd.flip);
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].floors[1].walls[0].doors.Single();
            Assert.Equal("idoor-json", baked.door); Assert.True(baked.flip);
            // copied and pasted onto another floor, and copied up, it keeps both
            Assert.True(Floors.PasteRooms(b, 2, Floors.CopyRooms(b, 1)));
            Assert.Equal("idoor-json", b.floors[2].walls[0].doors.Single().door);
            Assert.True(b.floors[2].walls[0].doors.Single().flip);
            b.floors[2].walls.Clear();
            Floors.CopyLayoutUp(b, 1);
            Assert.Equal("idoor-json", b.floors[2].walls[0].doors.Single().door);
            Assert.True(b.floors[2].walls[0].doors.Single().flip);
        }

        [Fact]
        public void TheDoorToolSetsADoorInADoorwayAndTakesItOut()
        {
            var (d, b) = Block(doorway: false); var site = new Site(d.buildings);
            Vec2 west = new Vec2(5.8, 4.5), east = new Vec2(6.2, 4.5);
            var doors = b.floors[1].walls[0].doors;
            Assert.False(Walls.RightOf(b.floors[1].walls[0], west));
            Assert.True(Walls.RightOf(b.floors[1].walls[0], east));
            // the prototype's: a doorway, and the same click takes it out
            Assert.True(Walls.ToggleDoor(site, b, 1, west));
            Assert.Null(Assert.Single(doors).door); Assert.False(doors[0].flip);
            // a door clicked on a doorway makes it that door, facing the side clicked
            Assert.False(Walls.TakesOut(b, Walls.DoorAt(site, b, 1, east)!, "idoor-a"));
            Assert.True(Walls.ToggleDoor(site, b, 1, east, "idoor-a"));
            Assert.Equal("idoor-a", Assert.Single(doors).door); Assert.True(doors[0].flip);
            Assert.Equal(0.5, doors[0].t, 9);
            // another door makes it that one, facing the side clicked
            Assert.True(Walls.ToggleDoor(site, b, 1, west, "idoor-b"));
            Assert.Equal("idoor-b", Assert.Single(doors).door); Assert.False(doors[0].flip);
            // a doorway clicked on a door makes it a doorway again
            Assert.True(Walls.ToggleDoor(site, b, 1, east));
            Assert.Null(Assert.Single(doors).door); Assert.False(doors[0].flip);
            // the same kind again takes it out
            Assert.True(Walls.TakesOut(b, Walls.DoorAt(site, b, 1, east)!));
            Assert.True(Walls.ToggleDoor(site, b, 1, east));
            Assert.Empty(doors);
            // a door on a wall with none: put in; the same door clicked again, from either side, taken out
            Assert.True(Walls.ToggleDoor(site, b, 1, new Vec2(6.2, 2), "idoor-a"));
            Assert.Equal("idoor-a", Assert.Single(doors).door); Assert.True(doors[0].flip);
            Assert.True(Walls.TakesOut(b, Walls.DoorAt(site, b, 1, new Vec2(5.8, 2))!, "idoor-a"));
            Assert.True(Walls.ToggleDoor(site, b, 1, new Vec2(5.8, 2), "idoor-a"));
            Assert.Empty(doors);
            // the outline's doors are street doors, as ever: no door of the rooms' there
            Assert.True(Walls.ToggleDoor(site, b, 0, new Vec2(3, 0.1), "idoor-a"));
            Assert.Null(b.entrances.Last().door);
        }

        [Fact]
        public void ADoorInARoomWallIsItsFrameWithItsLeafLeftOut()
        {
            OpeningKinds.Register(Door("idoor-frame"));
            try
            {
                var (d, b) = Block("idoor-frame");
                var o = Ops(d, b).Single();
                Assert.Same(OpeningKinds.Get("idoor-frame"), o.kind);
                Assert.Equal(0.9, o.u1 - o.u0, 9);   // its own size, as a street door's on a shell storey
                Assert.Equal(2.05, o.y1, 9);
                var r = Lod0.Build(new Site(d.buildings), b);
                var op = r.Op; var here = AtDoor(op, b);
                // its frame 0.9 m wide, 10 cm into the wall from the face it faces: the wall's left, the west; and its back,
                // which the east room sees, half a millimetre behind it, turned east (KindPart.Backs)
                var frame = here.Where(v => op.C[v].slot == ColorSlot.Frame && op.N[v].x < -0.5).Select(v => op.P[v]).ToList();
                var back = here.Where(v => op.C[v].slot == ColorSlot.Frame && op.N[v].x > 0.5).Select(v => op.P[v]).ToList();
                Assert.NotEmpty(frame); Assert.NotEmpty(back);
                Assert.Equal(here.Count(v => op.C[v].slot == ColorSlot.Frame), frame.Count + back.Count);
                Assert.Equal(0.9, frame.Max(p => p.z) - frame.Min(p => p.z), 6);
                Assert.Equal(2.05, frame.Max(p => p.y) - Derived.FloorBase(b, 1), 6);
                Assert.All(frame, p => Assert.Equal(6 - Dim.T_INT / 2 + 0.1, p.x, 6));
                Assert.All(back, p => Assert.Equal(6 - Dim.T_INT / 2 + 0.1 + KindPart.BackOff, p.x, 6));
                Assert.Equal(0.9, back.Max(p => p.z) - back.Min(p => p.z), 6);
                // its leaf left out, and no casing round it (a doorway's)
                Assert.DoesNotContain(here, v => op.C[v].slot == ColorSlot.Door);
                Assert.DoesNotContain(here, v => op.C[v].slot == ColorSlot.Interior && Math.Abs(op.C[v].tone - 0.72) < 1e-9);
                var (d0, b0) = Block();
                var op0 = Lod0.Build(new Site(d0.buildings), b0).Op;
                Assert.Contains(AtDoor(op0, b0), v => op0.C[v].slot == ColorSlot.Interior && Math.Abs(op0.C[v].tone - 0.72) < 1e-9);
                // no Open prefab: nothing goes live
                Assert.Empty(r.Doors);
                // facing the other way, its frame 10 cm in from the east face, its back turned west
                b.floors[1].walls[0].doors[0].flip = true;
                var opf = Lod0.Build(new Site(d.buildings), b).Op;
                var ff = AtDoor(opf, b).Where(v => opf.C[v].slot == ColorSlot.Frame && opf.N[v].x > 0.5).Select(v => opf.P[v]).ToList();
                var fb = AtDoor(opf, b).Where(v => opf.C[v].slot == ColorSlot.Frame && opf.N[v].x < -0.5).Select(v => opf.P[v]).ToList();
                Assert.NotEmpty(ff); Assert.NotEmpty(fb);
                Assert.All(ff, p => Assert.Equal(6 + Dim.T_INT / 2 - 0.1, p.x, 6));
                Assert.All(fb, p => Assert.Equal(6 + Dim.T_INT / 2 - 0.1 - KindPart.BackOff, p.x, 6));
                Assert.Equal(0.9, ff.Max(p => p.z) - ff.Min(p => p.z), 6);
            }
            finally { OpeningKinds.Remove("idoor-frame"); }
        }

        [Fact]
        public void ADoorWithAnOpenPrefabIsLeftOutAndGoesLive()
        {
            OpeningKinds.Register(Door("idoor-live", live: true));
            try
            {
                var (d, b) = Block("idoor-live"); var site = new Site(d.buildings);
                var r = Lod0.Build(site, b);
                // none of it drawn: the prefab is the door
                Assert.DoesNotContain(AtDoor(r.Op, b), v => r.Op.C[v].slot == ColorSlot.Frame || r.Op.C[v].slot == ColorSlot.Door);
                var live = Assert.Single(r.Doors);
                Assert.Same(r.Doors, r.Shape.Doors);
                Assert.Equal("idoor-live", live.kind);
                Assert.True(live.inside);
                // its own space: the doorway's bottom middle on the face it faces, +Z out of it, +X across as cross(up, out)
                Assert.Equal(6 - Dim.T_INT / 2, live.origin.x, 9);
                Assert.Equal(Derived.FloorBase(b, 1), live.origin.y, 9);
                Assert.Equal(4.5, live.origin.z, 9);
                Assert.Equal(-1, live.outward.x, 9); Assert.Equal(0, live.outward.z, 9);
                Assert.Equal(0, live.across.x, 9); Assert.Equal(1, live.across.z, 9);
                Assert.Equal(0.9, live.width, 9); Assert.Equal(2.05, live.height, 9);
                // facing the other way: out of the east face, +X the other way along the wall
                b.floors[1].walls[0].doors[0].flip = true;
                var f = Assert.Single(Lod0.Build(site, b).Doors);
                Assert.Equal(6 + Dim.T_INT / 2, f.origin.x, 9); Assert.Equal(4.5, f.origin.z, 9);
                Assert.Equal(1, f.outward.x, 9); Assert.Equal(0, f.across.x, 9); Assert.Equal(-1, f.across.z, 9);
                // the walk model: open across the doorway, as a doorway is
                Assert.DoesNotContain(Lod0.Build(site, b).Segs[1], s => Math.Abs(s.ax - s.bx) < 1e-6 && Math.Abs(s.ax - 6) < 0.1 && Math.Min(s.az, s.bz) < 4.5 && Math.Max(s.az, s.bz) > 4.5);
                // a shape alone has none
                Assert.Empty(Lod0.BuildShape(site, b).Doors);
                // into a room blocked out, the wall goes on across it: no door
                Assert.Null(Walls.ToggleBlock(b, 1, new Vec2(9, 4.5)));
                Assert.Empty(Lod0.Build(site, b).Doors);
            }
            finally { OpeningKinds.Remove("idoor-live"); }
        }

        [Fact]
        public void AWalkInStreetDoorWithAnOpenPrefabGoesLiveToo()
        {
            OpeningKinds.Register(Door("idoor-street", 1.2, 2.2, live: true));
            try
            {
                var st = Styles.Preset("brick").Clone(); st.doorKind = "idoor-street"; st.ground = GroundType.Match;
                var (d, b) = Block(); b.style = st;
                b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 });
                var site = new Site(d.buildings);
                var live = Assert.Single(Lod0.Build(site, b).Doors);
                Assert.Equal("idoor-street", live.kind);
                Assert.False(live.inside);
                Assert.Equal(Dim.DOOR_EXT, live.width, 6);   // a walk-in storey's doorway, the standard one
                // out of the building, +X as cross(up, out)
                var fp = Derived.OutlineAt(b, 0); double cx = fp.Average(p => p.x), cz = fp.Average(p => p.z);
                Assert.True((live.origin.x - cx) * live.outward.x + (live.origin.z - cz) * live.outward.z > 0);
                Assert.Equal(live.outward.z, live.across.x, 9); Assert.Equal(-live.outward.x, live.across.z, 9);   // (oz, -ox)
                // shut on a shell storey, and a filled one: drawn as ever, nothing live
                b.interior = false;
                Assert.Empty(Lod0.Build(site, b).Doors);
                b.interior = true; b.floors[0].filled = true;
                Assert.Empty(Lod0.Build(site, b).Doors);
            }
            finally { OpeningKinds.Remove("idoor-street"); }
        }

        [Fact]
        public void ADoorTheProjectDoesNotHaveIsADoorway()
        {
            var (d0, b0) = Block();
            foreach (var id in new[] { "idoor-missing", "idoor-window" })
            {
                if (id == "idoor-window") { var w = Door(id); w.door = false; OpeningKinds.Register(w); }   // a window isn't a door
                try
                {
                    var (d, b) = Block(id);
                    var o = Ops(d, b).Single();
                    Assert.Null(o.kind);
                    Assert.Equal(Dim.DOOR_INT, o.u1 - o.u0, 9); Assert.Equal(2.2, o.y1, 9);
                    var r = Lod0.Build(new Site(d.buildings), b);
                    Assert.Empty(r.Doors);
                    Assert.Equal(Lod0.Build(new Site(d0.buildings), b0).Op.Verts, r.Op.Verts);   // the doorway's LOD0, as it is without one
                }
                finally { OpeningKinds.Remove(id); }
            }
        }

        [Fact]
        public void AStretchedDoorFillsTheDoorwayAndOneKeepingItsSizeFitsTheWall()
        {
            OpeningKinds.Register(Door("idoor-stretch", 0.8, 2.0, stretch: true));
            OpeningKinds.Register(Door("idoor-wide", 1.4, 2.1));
            try
            {
                var (d, b) = Block("idoor-stretch");
                var o = Ops(d, b).Single();
                Assert.Equal(Dim.DOOR_INT, o.u1 - o.u0, 9); Assert.Equal(2.2, o.y1, 9);
                var op = Lod0.Build(new Site(d.buildings), b).Op;
                var frame = AtDoor(op, b).Where(v => op.C[v].slot == ColorSlot.Frame).Select(v => op.P[v]).ToList();
                Assert.Equal(Dim.DOOR_INT, frame.Max(p => p.z) - frame.Min(p => p.z), 6);
                // a wall 1.4 m long has room for a doorway 1.2 m wide, 10 cm in from either end
                var (dw, bw) = Block(doorway: false);
                bw.floors[1].walls[0] = new WallData { a = new Vec2(6, 3), b = new Vec2(6, 4.4) };
                bw.floors[1].walls[0].doors.Add(new DoorData { t = 0.5, door = "idoor-wide" });
                var ow = Ops(dw, bw).Single();
                Assert.Equal(1.2, ow.u1 - ow.u0, 9); Assert.Equal(0.1, ow.u0, 9);
            }
            finally { OpeningKinds.Remove("idoor-stretch"); OpeningKinds.Remove("idoor-wide"); }
        }

        [Fact]
        public void ItsDoorsAreFlickerFree()
        {
            OpeningKinds.Register(Door("idoor-cop"));
            OpeningKinds.Register(Door("idoor-cop-live", live: true));
            try
            {
                var (d, b) = Block("idoor-cop");
                var w = b.floors[1].walls[0];
                w.doors.Add(new DoorData { t = 0.15, door = "idoor-cop", flip = true });
                w.doors.Add(new DoorData { t = 0.85 });   // a doorway beside them
                w.doors.Add(new DoorData { t = 0.32, door = "idoor-cop-live" });
                var r = CoplanarCheck.Run(Lod0.Build(new Site(d.buildings), b, solids: true).Op);
                Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(5)));
            }
            finally { OpeningKinds.Remove("idoor-cop"); OpeningKinds.Remove("idoor-cop-live"); }
        }
    }
}
