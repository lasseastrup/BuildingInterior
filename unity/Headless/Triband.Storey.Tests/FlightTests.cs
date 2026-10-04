using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Straight flights (<see cref="CoreType.Flight"/>): flights stacked over the same floor range as switchback stairs,
    /// with a walkway back beside them and no walls. Storey's own (not in the prototype), so these are its spec.
    /// </summary>
    public class FlightTests
    {
        static (StoreyDocument d, BuildingData b) Block(int floors = 3, RoofType roof = RoofType.Flat)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));   // 12 × 9 m
            b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
            b.interior = true;
            b.style.roofType = roof;
            return (d, b);
        }

        // rot 0 on this block: across x, running along z (in at z−, out at z+)
        static CoreData Place(StoreyDocument d, BuildingData b, int k, double x = 6, double z = 4.5, double rot = 0) =>
            Shafts.Place(b, k, new Vec2(x, z), CoreType.Flight, rot, Shafts.NewId(d))!;

        [Fact]
        public void AFlightUpOntoATerraceOpensTheDeck()
        {
            // three storeys; the top one set back to the front 2 m, so a flight up to it comes out on the terrace
            var (d, b) = Block();
            var s = Place(d, b, 0);
            Shafts.SetTop(s, 2);
            b.floors[2].shape = new List<Vec2> { new Vec2(0, 0), new Vec2(12, 0), new Vec2(12, 2), new Vec2(0, 2) };
            Assert.True(Derived.IsSetback(b, 2));
            Assert.True(Cores.StairHoleAt(b, s, 2));
            var site = new Site(d.buildings); var l0 = Lod0.Build(site, b);
            // the middle of the flight lane, at the terrace's level: no upward face of the deck covers it
            var f = Cores.FrameOf(b, s); double hw = Dim.FLIGHT_W / 2;
            var mid = f.At2(-hw + Dim.FLIGHT_LANE / 2, 0); double y = Derived.FloorBase(b, 2);
            var m = l0.Op; int covering = 0;
            for (int t = 0; t < m.I.Count; t += 3)
            {
                P3 A = m.P[m.I[t]], B = m.P[m.I[t + 1]], C = m.P[m.I[t + 2]];
                if (System.Math.Abs(A.y - y) > 1e-6 || System.Math.Abs(B.y - y) > 1e-6 || System.Math.Abs(C.y - y) > 1e-6) continue;
                if ((B.x - A.x) * (C.z - A.z) - (B.z - A.z) * (C.x - A.x) == 0) continue;
                if (Geo.Pip(new List<Vec2> { new Vec2(A.x, A.z), new Vec2(B.x, B.z), new Vec2(C.x, C.z) }, mid.x, mid.z)) covering++;
            }
            Assert.Equal(0, covering);
        }

        [Fact]
        public void FlightsServeTheSameFloorsAsSwitchbackStairs()
        {
            var (d, b) = Block();
            var s = Place(d, b, 0);
            Assert.NotNull(s);
            Assert.Equal(new[] { 0, 1, 2, 3 }, Cores.Levels(b, s));   // up to the roof, as switchback stairs are placed
            Assert.Equal(new[] { 0, 1, 2 }, Enumerable.Range(0, 4).Where(k => Cores.HasFlight(b, s, k)));
            Assert.Equal(new[] { 1, 2, 3 }, Enumerable.Range(0, 4).Where(k => Cores.StairHoleAt(b, s, k)));
            Shafts.SetTop(s, 1);
            Assert.Equal(new[] { 0, 1 }, Cores.Levels(b, s));
            Assert.Equal(new[] { 0 }, Enumerable.Range(0, 4).Where(k => Cores.HasFlight(b, s, k)));
        }

        [Fact]
        public void UnderAPitchedRoofTheyStopAtTheTopFloor()
        {
            var (d, b) = Block(roof: RoofType.Gable);
            var s = Place(d, b, 0);
            Assert.Equal(new[] { 0, 1, 2 }, Cores.Levels(b, s));
            Assert.False(Cores.ShaftRoof(b, s));
        }

        [Fact]
        public void ItIsNeverPulledOntoAWallByItsBack()
        {
            // 20 cm short of the wall at z = 9: switchback stairs are pulled onto it by their back, a flight (its way out) is not
            var (_, b) = Block();
            double Pulled(CoreType t)
            {
                var it = new CoreData { type = t };
                double z = 9 - Cores.Size(it).D / 2 - 0.2;
                return Shafts.Snap(b.footprint, it, 6, z, 0).z - z;
            }
            Assert.Equal(0.2, Pulled(CoreType.Stairs), 6);
            Assert.Equal(0, Pulled(CoreType.Flight), 6);
            Assert.Null(Cores.CoreFlush(b.footprint, new CoreData { type = CoreType.Flight, x = 6, z = 9 - Dim.FLIGHT_D / 2, rot = 0 })[3]);
        }

        [Fact]
        public void StairsSwitchKindWhereTheOtherFits()
        {
            var (d, b) = Block();
            var s = Shafts.Place(b, 0, new Vec2(6, 4.5), CoreType.Stairs, 0, Shafts.NewId(d))!;
            Assert.True(Shafts.SetKind(b, s, CoreType.Flight));
            Assert.Equal(CoreType.Flight, s.type); Assert.Equal(new[] { 0, 1, 2, 3 }, Cores.Levels(b, s));   // the same floors
            Assert.True(Shafts.SetKind(b, s, CoreType.Stairs));
            Assert.Equal(CoreType.Stairs, s.type);
            var lift = Shafts.Place(b, 0, new Vec2(2, 2), CoreType.Lift, 0, Shafts.NewId(d))!;
            Assert.False(Shafts.SetKind(b, lift, CoreType.Flight));
        }

        [Fact]
        public void FlightsAreSavedAsFlight()
        {
            var (d, b) = Block();
            Place(d, b, 1);
            string json = PrototypeJson.Write(d);
            Assert.Contains("\"flight\"", json);
            var back = PrototypeJson.Read(json).Document.buildings[0].shafts.Single();
            Assert.Equal(CoreType.Flight, back.type);
            Assert.Equal(1, back.bottom);
        }

        public static IEnumerable<object[]> Layouts()
        {
            // bottom to roof in the middle, with the flight lane or the walkway against a side wall, from the top floor only,
            // and two side by side
            yield return new object[] { "middle", new[] { (0, 6.0, 4.5, 0.0) } };
            yield return new object[] { "side wall, flight lane", new[] { (0, Dim.FLIGHT_W / 2, 4.5, 0.0) } };
            yield return new object[] { "side wall, walkway", new[] { (0, 12 - Dim.FLIGHT_W / 2, 4.5, 0.0) } };
            yield return new object[] { "top floor to the roof", new[] { (2, 6.0, 4.5, 0.0) } };
            yield return new object[] { "two", new[] { (0, 3.0, 4.5, 0.0), (1, 9.0, 4.5, 180.0) } };
        }

        [Theory, MemberData(nameof(Layouts))]
        public void TheGeometryIsClean(string name, (int k, double x, double z, double rot)[] flights) => Clean(name, flights, RoofType.Flat, null);

        [Fact]
        public void TheGeometryIsCleanWhereItStopsShort()
        {
            Clean("stops at the first floor", new[] { (0, 6.0, 4.5, 0.0) }, RoofType.Flat, 1);
            Clean("under a gable roof", new[] { (0, 6.0, 4.5, 0.0) }, RoofType.Gable, null);
        }

        static void Clean(string name, (int k, double x, double z, double rot)[] flights, RoofType roof, int? top)
        {
            var (d, b) = Block(roof: roof);
            foreach (var f in flights) Assert.True(Place(d, b, f.k, f.x, f.z, f.rot) != null, $"{name}: no room at {f}");
            if (top != null) Shafts.SetTop(b.shafts[0], top.Value);
            if (name.StartsWith("side wall"))   // the building's wall stands in for the rail there
                Assert.Contains(Cores.CoreFlush(b.footprint, b.shafts[0]).Take(2), x => x != null);
            var site = new Site(d.buildings);
            foreach (var mesh in new[] { Lod0.Build(site, b, solids: true).Op, Lod1.Build(site, b, solids: true) })
            {
                var report = CoplanarCheck.Run(mesh);
                Assert.True(report.Overlaps.Count == 0, $"{name}: {report.Overlaps.Count} visible coplanar overlaps:\n  " + string.Join("\n  ", report.Overlaps.Take(10)));
            }
        }

        [Fact]
        public void EachFlightEndsOnTheFloorAbove()
        {
            var (d, b) = Block();
            var s = Place(d, b, 0);
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op;
            var f = Cores.FrameOf(b, s);
            double z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
            var steps = op.Solids!.Where(x => !x.Mitred && x.F.O.x == f.O.x && x.F.O.z == f.O.z && x.w0 >= z0 - 1e-9 && x.w1 <= z1 + 1e-9 && System.Math.Abs(x.y1 - x.y0 - 0.2) < 1e-9).ToList();
            for (int k = 0; k < 3; k++)
            {
                double lo = Derived.FloorBase(b, k), hi = Derived.FloorBase(b, k + 1);
                var mine = steps.Where(x => x.y1 > lo + 1e-9 && x.y1 <= hi + 1e-9).ToList();
                Assert.Equal((int)System.Math.Round(Derived.FloorH(b, k) / 0.18), mine.Count);
                Assert.Equal(hi, mine.Max(x => x.y1), 9);
                Assert.All(mine, x => Assert.True(x.u1 <= -Dim.FLIGHT_W / 2 + Dim.FLIGHT_LANE + 1e-9));   // in the flight lane, the walkway clear
            }
        }

        [Fact]
        public void ThereAreNoWallsNorABulkhead()
        {
            // nothing of the flights' taller than a rail, on any storey or on the roof, at LOD0 or LOD1
            var (d, b) = Block();
            var s = Place(d, b, 0);
            Assert.True(Cores.ShaftRoof(b, s));
            var site = new Site(d.buildings);
            var f = Cores.FrameOf(b, s);
            foreach (var op in new[] { Lod0.Build(site, b, solids: true).Op, Lod1.Build(site, b, solids: true) })
                Assert.Empty(op.Solids!.Where(x => !x.Mitred && x.F.O.x == f.O.x && x.F.O.z == f.O.z && x.y1 - x.y0 > 1.1));
        }

        [Fact]
        public void TheParapetRunsOnPastAFlightAgainstTheWall()
        {
            // a switchback's bulkhead stands in for the parapet where it shares the wall; a flight has none, so above the
            // roof everything but the flight's own rails is what the roof has without roof access
            List<string> AboveRoof(bool roof)
            {
                var (d, b) = Block();
                var s = Place(d, b, 0, Dim.FLIGHT_W / 2, 4.5, 0); s.roof = roof;
                Assert.NotNull(Cores.CoreFlush(b.footprint, s)[0]);
                var f = Cores.FrameOf(b, s); double y = Derived.RoofY(b);
                return Lod0.Build(new Site(d.buildings), b, solids: true).Op.Solids!
                    .Where(x => x.y0 >= y - 1e-6 && !(x.F.O.x == f.O.x && x.F.O.z == f.O.z))
                    .Select(x => $"{x.F.O.x:F3},{x.F.O.z:F3},{x.u0:F3},{x.u1:F3},{x.w0:F3},{x.w1:F3},{x.y0:F3},{x.y1:F3},{x.Mitred}").OrderBy(t => t).ToList();
            }
            var with = AboveRoof(true);
            Assert.NotEmpty(with);   // the parapet
            Assert.Equal(AboveRoof(false), with);
        }
    }
}
