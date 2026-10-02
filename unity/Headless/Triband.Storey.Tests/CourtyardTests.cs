using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Courtyards and atria (<see cref="VoidData"/>, <see cref="Courtyards"/>, <see cref="Voids"/>). Storey's own.</summary>
    public class CourtyardTests
    {
        static (StoreyDocument d, BuildingData b) Block(int floors = 4, RoofType roof = RoofType.Flat)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 16), new Vec2(0, 16) };
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear();
            b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
            b.entrances.Add(new EntranceData { edge = 0, t = 0.5, k = 0 });
            b.style.roofType = roof;
            return (d, b);
        }

        static VoidData Court(BuildingData b, VoidKind kind = VoidKind.Courtyard, int bottom = 0)
        {
            var v = new VoidData { id = Voids.NewId(b), kind = kind, bottom = bottom, shape = new() { new Vec2(6, 5), new Vec2(14, 5), new Vec2(14, 11), new Vec2(6, 11) } };
            Assert.Equal(VoidIssue.None, Voids.Issue(b, v));
            b.voids.Add(v);
            return v;
        }

        [Fact]
        public void AddFindsRoomAndFits()
        {
            var (_, b) = Block();
            var v = Voids.Add(b, VoidKind.Courtyard, 0);
            Assert.NotNull(v);
            Assert.Equal(VoidIssue.None, Voids.Issue(b, v!));
            var bb = Tiers.Bbox(v!.shape);
            Assert.InRange((bb.x0 + bb.x1) / 2, 9, 11); Assert.InRange((bb.z0 + bb.z1) / 2, 7, 9);
        }

        [Theory]
        [InlineData(VoidKind.Courtyard, RoofType.Flat)]
        [InlineData(VoidKind.Atrium, RoofType.Flat)]
        [InlineData(VoidKind.Courtyard, RoofType.Hip)]
        [InlineData(VoidKind.Atrium, RoofType.Hip)]
        [InlineData(VoidKind.Courtyard, RoofType.Mansard)]
        public void ItBuildsCleanly(VoidKind kind, RoofType roof)
        {
            var (d, b) = Block(4, roof);
            Court(b, kind, kind == VoidKind.Atrium ? 0 : 1);
            var site = new Site(d.buildings);
            var r = Lod0.Build(site, b, solids: true);
            var rep = CoplanarCheck.Run(r.Op);
            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
            var rep1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true));
            Assert.True(rep1.Overlaps.Count == 0, string.Join("\n", rep1.Overlaps.Take(10)));
            Assert.True(Lod2.Build(site, b).Tris > 0);
        }

        [Fact]
        public void TheCourtyardHasWindowsAndADoor()
        {
            var (d, b) = Block();
            var site0 = new Site(d.buildings); var plain = Lod0.Build(site0, b);
            var v = Court(b);
            var site = new Site(d.buildings); var r = Lod0.Build(site, b);
            Assert.True(r.Glass.Tris > plain.Glass.Tris, "no windows face the courtyard");
            Assert.True(r.Op.Walls.Count > plain.Op.Walls.Count, "the courtyard's walls have no cutaway ids");
            // the door: on the longest edge (6,5)–(14,5) at x = 10 (its wall stands just inside the courtyard), no segment blocks it on the ground floor; on the floor above, one does
            bool Blocked(int k) => r.Segs[k].Any(s => Math.Abs(s.az - s.bz) < 1e-6 && Math.Abs(s.az - (5 + Dim.T_EXT / 2)) < 0.05 && Math.Min(s.ax, s.bx) < 10 && Math.Max(s.ax, s.bx) > 10);
            Assert.False(Blocked(0)); Assert.True(Blocked(1));
        }

        [Fact]
        public void YouWalkIntoTheCourtyardThroughItsDoor()
        {
            var (d, b) = Block();
            Court(b);
            var w = new PlayWorld(new Site(d.buildings));
            var p = new PlayerState(); p.Spawn(10, 0, 2);   // inside, between the street wall and the courtyard's door
            for (int f = 0; f < 90; f++) Walker.Step(w, p, new MoveInput(0, 1), Math.PI, 1.0 / 30);   // camera yaw π: forward is +z
            Assert.True(p.z > 6 && p.z < 11 && p.x > 6 && p.x < 14, $"ended at ({p.x:0.00}, {p.z:0.00})");
            Assert.Equal(0, p.y, 6);
            // and a wall without a door stops you: walk from the courtyard into its far side
            for (int f = 0; f < 90; f++) Walker.Step(w, p, new MoveInput(0, 1), Math.PI, 1.0 / 30);
            Assert.True(p.z < 11 - Dim.T_EXT, $"walked through the courtyard's far wall to z = {p.z:0.00}");
        }

        [Fact]
        public void TheCourtyardIsOpenAboveItsPaving()
        {
            var (d, b) = Block();
            Court(b, VoidKind.Courtyard, 1);
            var w = new PlayWorld(new Site(d.buildings));
            double y1 = Derived.FloorBase(b, 1);
            Assert.Equal(y1, w.SurfaceAt(10, 8, Derived.FloorBase(b, 3)), 6);              // falls to the paving, on floor 1
            Assert.Equal(Derived.FloorBase(b, 3), w.SurfaceAt(3, 3, Derived.FloorBase(b, 3)), 6);   // the floor beside it is there
            Assert.Equal(0, w.SurfaceAt(10, 8, 0.1), 6);                                     // the ground floor under it is a room
        }

        [Fact]
        public void AnAtriumIsRailedAndOpen()
        {
            var (d, b) = Block();
            Court(b, VoidKind.Atrium, 0);
            var w = new PlayWorld(new Site(d.buildings));
            Assert.Equal(0, w.SurfaceAt(10, 8, Derived.FloorBase(b, 3)), 6);
            var p = new PlayerState(); p.Spawn(10, Derived.FloorBase(b, 2), 2.5);
            for (int f = 0; f < 90; f++) Walker.Step(w, p, new MoveInput(0, 1), Math.PI, 1.0 / 30);
            Assert.Equal(Derived.FloorBase(b, 2), p.y, 6);
            Assert.True(p.z < 5, $"walked through the rail into the atrium, at z = {p.z:0.00}");
        }

        [Fact]
        public void APitchedRoofOpensOverTheCourtyard()
        {
            var (d, b) = Block(4, RoofType.Hip);
            Court(b);
            var R = Roofs.Parts(new Site(d.buildings), b)!;
            foreach (var part in R.Parts.Where(q => q.Kind == RoofKind.Roof))
                Assert.False(Geo.Pip(part.Pts.Select(q => new Vec2(q.x, q.z)).ToList(), 10, 8), "the roof covers the courtyard");
            Assert.Contains(R.Parts, q => q.Kind == RoofKind.Wall && Math.Abs(q.N.z - 1) < 1e-9 && q.Pts.Max(z => z.y) > Derived.RoofY(b) + 0.5);   // up from the courtyard wall at z ≈ 5.3 to the roof
        }

        [Fact]
        public void OutlinesCoresAndOtherVoidsKeepClear()
        {
            var (d, b) = Block();
            var v = Court(b);
            // an outline pushed in over it
            Assert.Equal(OutlineIssue.Void, Outlines.Issue(b, 0, Outlines.PushEdge(b.footprint, 0, -4.5)));
            // stairs into it
            Assert.Null(Shafts.Place(b, 0, new Vec2(10, 8), CoreType.Stairs, 0, Shafts.NewId(d)));
            Assert.NotNull(Shafts.Place(b, 0, new Vec2(17.5, 8), CoreType.Lift, 0, Shafts.NewId(d)));
            // a courtyard over that lift, and one too near the outline, and one over the first
            Assert.Equal(VoidIssue.Core, Voids.SetShape(b, v.id, new() { new Vec2(6, 5), new Vec2(17.5, 5), new Vec2(17.5, 11), new Vec2(6, 11) }));
            Assert.Equal(VoidIssue.Outside, Voids.SetShape(b, v.id, new() { new Vec2(1, 5), new Vec2(14, 5), new Vec2(14, 11), new Vec2(1, 11) }));
            var w = new VoidData { id = "v9", kind = VoidKind.Atrium, shape = new() { new Vec2(7, 6), new Vec2(10, 6), new Vec2(10, 9), new Vec2(7, 9) } };
            Assert.Equal(VoidIssue.Overlap, Voids.Issue(b, w));
            Assert.Equal(VoidIssue.Small, Voids.Issue(b, new VoidData { shape = new() { new Vec2(3, 3), new Vec2(4, 3), new Vec2(4, 4), new Vec2(3, 4) } }));
        }

        [Fact]
        public void RoomsStopAtTheCourtyard()
        {
            var (d, b) = Block();
            Court(b);
            var wall = new WallData { a = new Vec2(0.5, 8), b = new Vec2(19.5, 8), doors = { new DoorData { t = 0.1 }, new DoorData { t = 0.9 } } };
            var pieces = Courtyards.ClipOut(b, 1, new List<WallData> { wall });
            Assert.Equal(2, pieces.Count);
            Assert.Equal(6, pieces[0].b.x, 6); Assert.Equal(14, pieces[1].a.x, 6);
            Assert.Single(pieces[0].doors); Assert.Single(pieces[1].doors);
            Assert.Equal(0.5 + 19 * 0.1, pieces[0].a.x + (pieces[0].b.x - pieces[0].a.x) * pieces[0].doors[0].t, 6);
        }

        [Fact]
        public void TheColliderIsOpenAboveTheCourtyard()
        {
            var (d, b) = Block();
            Court(b, VoidKind.Courtyard, 1);
            var site = new Site(d.buildings);
            var m = CollisionMesh.Build(site, b, Lod0.Build(site, b));
            double y1 = Derived.FloorBase(b, 1);
            for (int t = 0; t < m.Tris; t++)
            {
                if (m.Kind[t] != CollisionKind.Floor) continue;
                var a = m.P[m.I[3 * t]]; var c = m.P[m.I[3 * t + 1]]; var e = m.P[m.I[3 * t + 2]];
                double cx = (a.x + c.x + e.x) / 3, cz = (a.z + c.z + e.z) / 3;
                if (cx > 6.5 && cx < 13.5 && cz > 5.5 && cz < 10.5) Assert.True(Math.Abs(a.y - y1) < 1e-6 || a.y < y1, $"a floor at {a.y:0.00} over the courtyard");
            }
        }

        [Fact]
        public void TheyRoundTrip()
        {
            var (d, b) = Block();
            Court(b, VoidKind.Atrium, 1);
            var rd = PrototypeJson.Read(PrototypeJson.Write(d)); Assert.Empty(rd.Unknown);
            var v = rd.Document.buildings[0].voids.Single();
            Assert.Equal(VoidKind.Atrium, v.kind); Assert.Equal(1, v.bottom); Assert.Equal(4, v.shape.Count);
            // a building without any writes no key, as the prototype's do
            var (d2, _) = Block();
            Assert.DoesNotContain("voids", PrototypeJson.Write(d2));
        }
    }
}
