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
    /// Straight flights climbing one storey (<see cref="Cores.OneFlight"/>): no flight carries on from the top, so the walkway
    /// back beside the flight leads nowhere, and they take the flight lane alone, the steps where a longer stack's are. Storey's
    /// own, as straight flights are.
    /// </summary>
    public class OneFlightTests
    {
        static (StoreyDocument d, BuildingData b) Block(int floors = 3, RoofType roof = RoofType.Flat, double width = 12, double depth = 9)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(width, 0), new Vec2(width, depth), new Vec2(0, depth) };
            b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
            b.entrances.Clear(); b.details.Clear(); b.shafts.Clear();
            b.interior = true;
            b.style.roofType = roof;
            return (d, b);
        }

        // rot 0: across x (the flight lane the −x half), running along z (in at z−, out at z+)
        static CoreData Place(StoreyDocument d, BuildingData b, int k, double x = 6, double z = 4.5, CoreType type = CoreType.Flight) =>
            Shafts.Place(b, k, new Vec2(x, z), type, 0, Shafts.NewId(d))!;

        [Fact]
        public void OneFlightIsAFlightThatClimbsOneStorey()
        {
            var (d, b) = Block();
            var s = Place(d, b, 0);
            Assert.False(Cores.OneFlight(b, s));    // three flights: to storeys 1 and 2, and the roof
            Shafts.SetTop(s, 1);
            Assert.True(Cores.OneFlight(b, s));     // storey 0 to 1 (the roof is the top floor's)
            Shafts.SetTop(s, 2);
            Assert.False(Cores.OneFlight(b, s));    // on to the roof from the top floor
            s.roof = false;
            Assert.False(Cores.OneFlight(b, s));    // 0 to 2: two flights
            Shafts.SetBottom(s, 1);
            Assert.True(Cores.OneFlight(b, s));
            // the top floor up to a flat roof is one flight; under a pitched roof the floor below the top is
            var top = Place(d, b, 2, x: 2.5, z: 4.5);
            Assert.Equal(new[] { 2, 3 }, Cores.Levels(b, top)); Assert.True(Cores.OneFlight(b, top));
            var (d2, p) = Block(roof: RoofType.Hip);
            Assert.True(Cores.OneFlight(p, Place(d2, p, 1)));
            Assert.False(Cores.OneFlight(p, Place(d2, p, 0, x: 2.5)));
            // switchback stairs and lifts keep their size, whatever they serve
            var (d3, c) = Block();
            var sw = Place(d3, c, 0, type: CoreType.Stairs); Shafts.SetTop(sw, 1);
            Assert.False(Cores.OneFlight(c, sw));
        }

        [Fact]
        public void ItTakesTheFlightLaneAloneWithTheStepsWhereTheyWere()
        {
            var (d, b) = Block();
            var s = Place(d, b, 0); Shafts.SetTop(s, 1);
            var R = Cores.RectOf(s, b: b);
            Assert.Equal(Dim.FLIGHT_LANE / 2, R.hx, 9); Assert.Equal(Dim.FLIGHT_D / 2, R.hz, 9);
            Assert.Equal(s.x - Dim.FLIGHT_W / 2 + Dim.FLIGHT_LANE / 2, R.c.x, 9);   // its −u side where a longer stack's is
            var whole = Cores.RectOf(s);                                             // without the building: the kind's size
            Assert.Equal(Dim.FLIGHT_W / 2, whole.hx, 9);
            // the walkway's half is floor: nothing there picks the stairs
            Assert.Same(s, Shafts.At(b, 0, new Vec2(s.x - 0.6, s.z)));
            Assert.Null(Shafts.At(b, 0, new Vec2(s.x + 0.6, s.z)));
            Assert.Null(Shafts.At(b, 1, new Vec2(s.x + 0.6, s.z)));
            // the steps and the slab's opening are the same as a longer stack's on its first storey
            var steps = StepsOn(b, d, 0);
            Shafts.SetTop(s, -1);
            Assert.Equal(StepsOn(b, d, 0), steps);
        }

        // the step faces' corners on storey k (the core's colour at the steps' shade), rounded to the millimetre: under the
        // floor above by 5 cm, clear of the next flight's first step, which starts 2.4 cm under it
        static List<(double, double, double)> StepsOn(BuildingData b, StoreyDocument d, int k)
        {
            var op = Lod0.Build(new Site(d.buildings), b).Op; double y1 = Derived.FloorBase(b, k + 1) - 0.05;
            return Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Core && Math.Abs(op.C[v].tone - 0.82) < 1e-6 && op.P[v].y < y1)
                .Select(v => (Math.Round(op.P[v].x, 3), Math.Round(op.P[v].y, 3), Math.Round(op.P[v].z, 3))).Distinct().OrderBy(q => q).ToList();
        }

        [Fact]
        public void AnotherCoreCanStandWhereItsWalkwayWouldBe()
        {
            var (d, b) = Block();
            var s = Place(d, b, 0); Shafts.SetTop(s, 1);
            // a lift 30 cm past the flight lane's side, over where the walkway would be
            var lift = new CoreData { id = "s99", type = CoreType.Lift, x = s.x + 0.3 + 1.2 + 0.05, z = s.z, bottom = 0, top = 1 };
            Assert.False(Shafts.Overlap(s, lift, of: b));
            Assert.True(Shafts.Overlap(s, lift));   // as the kind's whole size, it would be in the way
            b.shafts.Add(lift);
            // a longer stack needs its walkway: the same lift is in its way
            Shafts.SetTop(s, -1);
            Assert.True(Shafts.Overlap(s, lift, of: b));
        }

        [Fact]
        public void ItFitsWhereALongerStackDoesNot()
        {
            // a block 2.4 m across: room for the flight lane (1.3 m and 14 cm each side), not for a walkway beside it
            var (_, b) = Block(floors: 2, roof: RoofType.Hip, width: 2.4, depth: 10);
            var (it, ok) = Shafts.Placement(b, 0, new Vec2(1.75, 5), CoreType.Flight, 0);
            Assert.True(ok);
            Assert.True(Cores.OneFlight(b, it));
            var (_, c) = Block(floors: 3, roof: RoofType.Hip, width: 2.4, depth: 10);
            Assert.False(Shafts.Placement(c, 0, new Vec2(1.75, 5), CoreType.Flight, 0).ok);   // two flights: a walkway too
        }

        [Fact]
        public void AgainstAWallOnItsOtherSideItHasNoRailThere()
        {
            // pulled flush onto the block's side, its flight lane's +x side against the wall: the wall stands in for the rail
            var (d, b) = Block(floors: 2, roof: RoofType.Hip, width: 2.4, depth: 10);
            var s = Shafts.Place(b, 0, new Vec2(2.1, 5), CoreType.Flight, 0, Shafts.NewId(d))!;
            Assert.NotNull(s);
            var fl = Cores.CoreFlush(Derived.OutlineAt(b, 0), s, b: b);
            Assert.NotNull(fl[1]); Assert.Null(fl[0]);
            var op = Lod0.Build(new Site(d.buildings), b, solids: true).Op; double y = Derived.FloorBase(b, 1);
            var rails = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Rail && op.P[v].y > y).Select(v => op.P[v]).ToList();
            Assert.NotEmpty(rails);                                                   // the open side's, and across the front
            Assert.DoesNotContain(rails, p => p.x > s.x + 0.01 && p.x < 2.4);          // none along the wall
            var r = CoplanarCheck.Run(op);
            Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(10)));
        }
    }
}
