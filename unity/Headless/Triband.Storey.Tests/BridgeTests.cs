using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Bridges between buildings (<see cref="BridgeData"/>, <see cref="Bridges"/>, <see cref="BridgeEdits"/>). Storey's own.</summary>
    public class BridgeTests
    {
        static BuildingData Box(StoreyDocument d, double x, double z, double w, double dd, int floors = 4, double floorH = 3.0)
        {
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.pos = new Vec2(x, z);
            b.footprint = new() { new Vec2(0, 0), new Vec2(w, 0), new Vec2(w, dd), new Vec2(0, dd) };
            b.entrances.Clear(); b.details.Clear(); b.blank.Clear(); b.shafts.Clear();
            b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
            b.floorHeight = floorH;
            return b;
        }

        // A at x 0..12, B at x 20..32: walls 8 m apart, facing each other across x = 12 and x = 20
        static (StoreyDocument d, BuildingData A, BuildingData B) Street(double bFloorH = 3.0)
        {
            var d = new StoreyDocument();
            var A = Box(d, 0, 0, 12, 10); var B = Box(d, 20, 0, 12, 10, 4, bFloorH);
            return (d, A, B);
        }

        static BridgeData Add(StoreyDocument d, BuildingData A, int k = 2, bool open = false)
        {
            var (br, why) = BridgeEdits.Add(new Site(d.buildings), A, k, new Vec2(12, 5), open);
            Assert.True(br != null, why);
            return br!;
        }

        [Fact]
        public void ItReachesTheWallAcross()
        {
            var (d, A, B) = Street();
            var br = Add(d, A);
            Assert.Equal(B.id, br.to); Assert.Equal(2, br.toK);
            var s = Bridges.Span(new Site(d.buildings), A, br)!;
            Assert.Equal(8 - 2 * Dim.T_EXT, s.L, 6);
            Assert.Equal(12 + Dim.T_EXT, s.PA.x, 6); Assert.Equal(20 - Dim.T_EXT, s.PB.x, 6);
            Assert.Equal(Derived.FloorBase(A, 2), s.ya, 6); Assert.Equal(s.ya, s.yb, 6);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ItBuildsCleanlyWithADoorAtEachEnd(bool open)
        {
            var (d, A, B) = Street();
            var site0 = new Site(d.buildings); int a0 = Lod0.Build(site0, A).Op.Tris, m0 = Lod2.Build(site0, A).Tris;
            Add(d, A, 2, open);
            var site = new Site(d.buildings);
            var ra = Lod0.Build(site, A, solids: true); var rb = Lod0.Build(site, B, solids: true);
            Assert.True(ra.Op.Tris > a0);
            foreach (var r in new[] { ra, rb }) { var rep = CoplanarCheck.Run(r.Op); Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(8))); }
            var r1 = CoplanarCheck.Run(Lod1.Build(site, A, solids: true));
            Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(8)));
            Assert.True(Lod2.Build(site, A).Tris > m0);
            // the doors: nothing stops you at the bridge's ends on storey 2, on either wall
            bool Wall(Lod0Result r, int k, double x) => r.Segs[k].Any(g => Math.Abs(g.ax - x) < 0.2 && Math.Abs(g.bx - x) < 0.2 && Math.Min(g.az, g.bz) < 5 && Math.Max(g.az, g.bz) > 5);
            Assert.False(Wall(ra, 2, 12 + Dim.T_EXT / 2)); Assert.True(Wall(ra, 1, 12 + Dim.T_EXT / 2));
            Assert.False(Wall(rb, 2, 20 - Dim.T_EXT / 2)); Assert.True(Wall(rb, 1, 20 - Dim.T_EXT / 2));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void YouWalkAcross(bool open)
        {
            var (d, A, B) = Street();
            Add(d, A, 2, open);
            var w = new PlayWorld(new Site(d.buildings));
            double y = Derived.FloorBase(A, 2);
            var p = new PlayerState(); p.Spawn(9, y, 5);
            // camera yaw −π/2: forward is +x
            for (int f = 0; f < 150; f++) Walker.Step(w, p, new MoveInput(0, 1), -Math.PI / 2, 1.0 / 30);
            Assert.Equal(y, p.y, 6);
            var loc = w.Locate(p.x, p.y, p.z);
            Assert.True(loc != null && loc.Value.b.id == B.id && loc.Value.floor == 2, $"ended at ({p.x:0.00}, {p.y:0.00}, {p.z:0.00})");
            // half way across, walking sideways: the sides hold you
            p.Spawn(16, y, 5);
            for (int f = 0; f < 60; f++) Walker.Step(w, p, new MoveInput(1, 0), -Math.PI / 2, 1.0 / 30);
            Assert.Equal(y, p.y, 6);
            Assert.True(Math.Abs(p.z - 5) < 1.2, $"walked off the side to z = {p.z:0.00}");
        }

        [Fact]
        public void ARampMeetsAFloorAtAnotherHeight()
        {
            var (d, A, B) = Street(3.4);   // B's floor 2 is 0.8 m higher
            var br = Add(d, A);
            var s = Bridges.Span(new Site(d.buildings), A, br)!;
            Assert.Equal(Derived.FloorBase(B, 2), s.yb, 6);
            var w = new PlayWorld(new Site(d.buildings));
            Assert.Equal((s.ya + s.yb) / 2, w.SurfaceAt(16, 5, s.yb), 2);
        }

        [Fact]
        public void TheReasonsItCantBe()
        {
            var d = new StoreyDocument();
            var A = Box(d, 0, 0, 12, 10);
            Box(d, 60, 0, 12, 10);   // too far
            Assert.Contains("40 m", BridgeEdits.Add(new Site(d.buildings), A, 2, new Vec2(12, 5)).why);
            var (d2, A2, B2) = Street(6);   // too steep
            Assert.Contains("steep", BridgeEdits.Add(new Site(d2.buildings), A2, 2, new Vec2(12, 5)).why);
            var (d3, A3, _) = Street();
            Box(d3, 15, 4, 4, 2, 6);   // a tower in the way (its wall facing A is too narrow to land on)
            Assert.Contains("in the way", BridgeEdits.Add(new Site(d3.buildings), A3, 2, new Vec2(12, 5)).why);
            var (d4, A4, _) = Street();
            Assert.NotNull(BridgeEdits.Add(new Site(d4.buildings), A4, 0, new Vec2(12, 5)).why);   // the ground floor
            Assert.NotNull(BridgeEdits.Add(new Site(d4.buildings), A4, 2, new Vec2(0, 5)).why);    // the back wall faces nothing
        }

        [Fact]
        public void EditsRebuildBothEnds()
        {
            var (d, A, B) = Street();
            var session = new EditSession(PrototypeJson.Write(d));
            var a = session.Document.buildings[0];
            BridgeEdits.Add(new Site(session.Document.buildings), a, 2, new Vec2(12, 5));
            var c = session.Commit();
            Assert.Contains(A.id, c.rebuild); Assert.Contains(B.id, c.rebuild);
            // and B moving rebuilds A, which builds the bridge
            session.Document.buildings[1].pos = new Vec2(21, 0);
            c = session.Commit();
            Assert.Contains(A.id, c.rebuild);
            // and removing it rebuilds B, whose door goes
            session.Document.buildings[0].bridges.Clear();
            c = session.Commit();
            Assert.Contains(B.id, c.rebuild);
        }

        [Fact]
        public void DeletingOrCopyingABuildingTakesItsBridges()
        {
            var (d, A, B) = Street();
            Add(d, A);
            var copy = Buildings.Duplicate(d, A, new Vec2(0, 40));
            Assert.Empty(copy.bridges);
            Buildings.Delete(d, B.id);
            Assert.Empty(A.bridges);
        }

        [Fact]
        public void AClickOnItsDoorFindsIt()
        {
            var (d, A, B) = Street();
            var br = Add(d, A);
            var site = new Site(d.buildings);
            Assert.Equal(br.id, BridgeEdits.At(site, A, 2, new Vec2(12, 5.5))!.Value.br.id);
            Assert.Equal(br.id, BridgeEdits.At(site, B, 2, new Vec2(0, 4.5))!.Value.br.id);
            Assert.Null(BridgeEdits.At(site, A, 1, new Vec2(12, 5)));
            Assert.Contains("already", BridgeEdits.Add(site, A, 2, new Vec2(12, 6)).why);
        }

        [Fact]
        public void TheColliderHasADeck()
        {
            var (d, A, _) = Street();
            Add(d, A);
            var site = new Site(d.buildings);
            var m = CollisionMesh.Build(site, A, Lod0.Build(site, A));
            double y = Derived.FloorBase(A, 2);
            Assert.Contains(Enumerable.Range(0, m.Tris), t => m.Kind[t] == CollisionKind.Floor && Math.Abs(m.P[m.I[3 * t]].y - y) < 1e-6 && m.P[m.I[3 * t]].x > 12.2 && m.P[m.I[3 * t]].x < 19.8);
        }

        [Fact]
        public void ItRoundTrips()
        {
            var (d, A, _) = Street();
            Add(d, A, 2, true);
            var rd = PrototypeJson.Read(PrototypeJson.Write(d)); Assert.Empty(rd.Unknown);
            var br = rd.Document.buildings[0].bridges.Single();
            Assert.True(br.open); Assert.Equal(2, br.k); Assert.Equal(2, br.toK); Assert.Equal(12, br.at.x);
        }
    }
}
