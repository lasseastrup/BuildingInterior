using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Single flights (<see cref="CoreType.Flight"/>): a straight stair from its floor to the next, or up to a flat roof.
    /// Storey's own (not in the prototype), so these are its spec.
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
        public void AFlightServesItsFloorAndTheNext()
        {
            var (d, b) = Block();
            var s = Place(d, b, 0);
            Assert.NotNull(s);
            Assert.Equal(new[] { 0, 1 }, Cores.Levels(b, s));
            Assert.True(Cores.HasFlight(b, s, 0)); Assert.False(Cores.HasFlight(b, s, 1));
            Assert.Equal(new[] { 1 }, Enumerable.Range(0, 4).Where(k => Cores.StairHoleAt(b, s, k)));
            Assert.False(Cores.ShaftRoof(b, s));
        }

        [Fact]
        public void FromTheTopFloorItNeedsAFlatRoof()
        {
            var (d, b) = Block();
            var s = Place(d, b, 2);
            Assert.Equal(new[] { 2, 3 }, Cores.Levels(b, s));
            Assert.True(Cores.ShaftRoof(b, s));
            Assert.True(Cores.StairHoleAt(b, s, 3));
            var (d2, gable) = Block(roof: RoofType.Gable);
            Assert.False(Shafts.Placement(gable, 2, new Vec2(6, 4.5), CoreType.Flight, 0).ok);
            Assert.NotNull(Place(d2, gable, 1));   // below the top floor a pitched roof is no matter
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
            Assert.Equal(CoreType.Flight, s.type); Assert.Equal(new[] { 0, 1 }, Cores.Levels(b, s));
            Assert.True(Shafts.SetKind(b, s, CoreType.Stairs));
            Assert.Equal(new[] { 0, 1, 2, 3 }, Cores.Levels(b, s));   // a switchback reaches the roof again
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
            // in the middle, against a side wall, from the top floor to the roof, and two chained one above the other's landing
            yield return new object[] { "middle", new[] { (0, 6.0, 4.5, 0.0) } };
            yield return new object[] { "side wall", new[] { (0, 12 - Dim.FLIGHT_W / 2, 4.5, 0.0) } };
            yield return new object[] { "to the roof", new[] { (2, 6.0, 4.5, 0.0) } };
            yield return new object[] { "side wall to the roof", new[] { (2, Dim.FLIGHT_W / 2, 4.5, 0.0) } };
            yield return new object[] { "two storeys", new[] { (0, 3.0, 4.5, 0.0), (1, 9.0, 4.5, 180.0) } };
        }

        [Theory, MemberData(nameof(Layouts))]
        public void TheGeometryIsClean(string name, (int k, double x, double z, double rot)[] flights)
        {
            var (d, b) = Block();
            foreach (var f in flights) Assert.True(Place(d, b, f.k, f.x, f.z, f.rot) != null, $"{name}: no room at {f}");
            if (name.StartsWith("side wall"))   // the building's wall stands in for the flight's own there
                Assert.Contains(Cores.CoreFlush(b.footprint, b.shafts[0]).Take(2), x => x != null);
            var site = new Site(d.buildings);
            foreach (var mesh in new[] { Lod0.Build(site, b, solids: true).Op, Lod1.Build(site, b, solids: true) })
            {
                var report = CoplanarCheck.Run(mesh);
                Assert.True(report.Overlaps.Count == 0, $"{name}: {report.Overlaps.Count} visible coplanar overlaps:\n  " + string.Join("\n  ", report.Overlaps.Take(10)));
            }
        }

        [Fact]
        public void TheTopStepIsTheNextFloor()
        {
            var (d, b) = Block();
            var s = Place(d, b, 0);
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op;
            var f = Cores.FrameOf(b, s);
            // the steps: solids in the flight's frame within its run
            double z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING;
            var steps = op.Solids!.Where(x => !x.Mitred && x.F.O.x == f.O.x && x.F.O.z == f.O.z && x.w0 >= z0 - 1e-9 && x.w1 <= z1 + 1e-9 && System.Math.Abs(x.y1 - x.y0 - 0.2) < 1e-9).ToList();
            Assert.Equal((int)System.Math.Round(Derived.FloorH(b, 0) / 0.18), steps.Count);
            Assert.Equal(Derived.FloorBase(b, 1), steps.Max(x => x.y1), 9);
        }
    }
}
