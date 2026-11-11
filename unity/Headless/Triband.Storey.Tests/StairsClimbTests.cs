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
    /// Stairs with nothing to climb to (<see cref="Shafts.Climb"/>). Placed on the top floor under a pitched roof, which has no
    /// roof access, switchback stairs were their walls alone: no steps in the stairwell and no slab opened (straight flights,
    /// nothing at all). The Stairs tool refuses them and says why, and the problem list flags stairs another edit leaves so
    /// (a roof made pitched, roof access off, From and To on one floor, filled storeys). Storey's own: the prototype places them.
    /// </summary>
    public class StairsClimbTests
    {
        // a 12 × 9 m walk-in block
        static (StoreyDocument d, BuildingData b) Block(int floors = 3, RoofType roof = RoofType.Flat)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList();
            b.interior = true;
            b.style.roofType = roof;
            return (d, b);
        }

        static readonly Vec2 Mid = new Vec2(6, 4.5);

        static CoreData? Place(StoreyDocument d, BuildingData b, int k, CoreType type = CoreType.Stairs) => Shafts.Place(b, k, Mid, type, 0, Shafts.NewId(d));

        // the step faces' corners built on storey k (the core's colour at the steps' shade): over its floor, which the steps
        // coming up to it reach, and under the next flight's first step, which starts a few centimetres under the floor above
        static int StepCorners(MeshBuilder op, BuildingData b, int k)
        {
            double y0 = Derived.FloorBase(b, k) + 0.05, y1 = Derived.FloorBase(b, k + 1) - 0.06;
            return Enumerable.Range(0, op.Verts).Count(v => op.C[v].slot == ColorSlot.Core && Math.Abs(op.C[v].tone - 0.82) < 1e-6 && op.P[v].y > y0 && op.P[v].y < y1);
        }

        // the faces lying at height y over a point (world space): a slab opened there has none
        static int Covering(MeshBuilder m, Vec2 at, double y)
        {
            int n = 0;
            for (int t = 0; t < m.I.Count; t += 3)
            {
                P3 A = m.P[m.I[t]], B = m.P[m.I[t + 1]], C = m.P[m.I[t + 2]];
                if (Math.Abs(A.y - y) > 1e-6 || Math.Abs(B.y - y) > 1e-6 || Math.Abs(C.y - y) > 1e-6) continue;
                if ((B.x - A.x) * (C.z - A.z) - (B.z - A.z) * (C.x - A.x) == 0) continue;
                if (Geo.Pip(new List<Vec2> { new Vec2(A.x, A.z), new Vec2(B.x, B.z), new Vec2(C.x, C.z) }, at.x, at.z)) n++;
            }
            return n;
        }

        // a point the slab over storey k's flight is open at: over the first flight of switchback stairs (whose last step
        // tops out at the floor above on the other side), the middle of a straight flight's lane
        static Vec2 OverTheFlight(BuildingData b, CoreData s) =>
            s.type == CoreType.Flight ? Cores.FrameOf(b, s).At2(-Dim.FLIGHT_W / 2 + Dim.FLIGHT_LANE / 2, 0) : Cores.FrameOf(b, s).At2(-0.65, 0.5);

        static string? NoFlight(StoreyDocument d, BuildingData b) =>
            Problems.Check(new Site(d.buildings)).FirstOrDefault(p => p.code == "stairs-no-flight" && p.buildingId == b.id)?.message;

        [Fact]
        public void ThePrototypesStairsOnATopFloorUnderAPitchedRoofAreWallsAlone()
        {
            // what was placed before: switchback stairs on the top floor under a gable roof built their walls and nothing more
            var (d, b) = Block(roof: RoofType.Gable);
            var s = Shafts.Place(b, 2, Mid, CoreType.Stairs, 0, Shafts.NewId(d), climb: false)!;
            Assert.NotNull(s);
            Assert.Equal(new[] { 2 }, Cores.Levels(b, s));
            Assert.Empty(Enumerable.Range(0, 4).Where(k => Cores.HasFlight(b, s, k) || Cores.StairHoleAt(b, s, k)));
            var op = Lod0.Build(new Site(d.buildings), b).Op;
            Assert.Equal(0, StepCorners(op, b, 2));
            Assert.Contains(Problems.Check(new Site(d.buildings)), p => p.code == "stairs-no-flight" && p.k == 2);
        }

        [Theory]
        [InlineData(RoofType.Hip)]
        [InlineData(RoofType.Gable)]
        [InlineData(RoofType.Shed)]
        [InlineData(RoofType.Mansard)]
        public void OnTheTopFloorUnderAPitchedRoofTheToolPlacesNone(RoofType roof)
        {
            var (d, b) = Block(roof: roof);
            foreach (var type in new[] { CoreType.Stairs, CoreType.Flight })
            {
                Assert.Equal(ClimbIssue.TopFloor, Shafts.Climb(b, 2, type));
                Assert.False(Shafts.Placement(b, 2, Mid, type, 0).ok);
                Assert.True(Shafts.Placement(b, 2, Mid, type, 0, climb: false).ok);   // they fit: it's the climb that's missing
                Assert.Null(Place(d, b, 2, type));
            }
            Assert.Empty(b.shafts);
            Assert.Contains("pitched roof", Shafts.Why(ClimbIssue.TopFloor));
            // a lift goes there all the same, with its one stop (the problem list says so)
            Assert.Equal(ClimbIssue.None, Shafts.Climb(b, 2, CoreType.Lift));
            Assert.NotNull(Place(d, b, 2, CoreType.Lift));
        }

        [Fact]
        public void UnderAFlatRoofTheTopFloorsStairsGoUpToTheRoof()
        {
            var (d, b) = Block();
            Assert.Equal(ClimbIssue.None, Shafts.Climb(b, 2, CoreType.Stairs));
            var s = Place(d, b, 2)!;
            Assert.NotNull(s);
            Assert.True(Cores.HasFlight(b, s, 2));
            Assert.True(Cores.StairHoleAt(b, s, 3));
            Assert.Null(NoFlight(d, b));
        }

        [Fact]
        public void AOneStoreyBuildingTakesStairsOnlyUnderAFlatRoof()
        {
            var (d, b) = Block(floors: 1, roof: RoofType.Hip);
            Assert.Equal(ClimbIssue.TopFloor, Shafts.Climb(b, 0, CoreType.Stairs));
            Assert.Null(Place(d, b, 0));
            b.style.roofType = RoofType.Flat;
            var s = Place(d, b, 0);
            Assert.NotNull(s);
            Assert.Equal(new[] { 0, 1 }, Cores.Levels(b, s!));
        }

        [Fact]
        public void FilledStoreysThatStopEveryFlightLeaveNothingToClimbTo()
        {
            // the storeys over the ground floor filled: under a flat roof the top floor's flight would climb from a filled storey
            var (d, b) = Block();
            b.floors[1].filled = true; b.floors[2].filled = true;
            Assert.Equal(ClimbIssue.Filled, Shafts.Climb(b, 0, CoreType.Stairs));
            Assert.Null(Place(d, b, 0));
            Assert.Contains("filled", Shafts.Why(ClimbIssue.Filled));
            // the top floor open again: the stairs climb from it to the roof, and keep their railed dead end on the ground floor
            // under the filled storey, as stairs through filled storeys always have (FilledStoreyTests)
            b.floors[2].filled = false;
            Assert.Equal(ClimbIssue.None, Shafts.Climb(b, 0, CoreType.Stairs));
            var s = Place(d, b, 0)!;
            Assert.NotNull(s);
            Assert.False(Cores.HasFlight(b, s, 0));
            Assert.True(Cores.HasFlight(b, s, 2));
        }

        [Fact]
        public void WhatTheToolPlacesHasStepsAndTheSlabOverThemOpen()
        {
            foreach (var roof in new[] { RoofType.Flat, RoofType.Hip, RoofType.Gable })
                for (int floors = 1; floors <= 3; floors++)
                    for (int k = 0; k < floors; k++)
                        foreach (var type in new[] { CoreType.Stairs, CoreType.Flight })
                        {
                            var (d, b) = Block(floors, roof);
                            string what = $"{type} on storey {k} of {floors} under a {roof} roof";
                            var s = Place(d, b, k, type);
                            bool climbs = k < floors - 1 || roof == RoofType.Flat;
                            Assert.True(climbs == (s != null), what + (climbs ? ": refused" : ": placed"));
                            Assert.Equal(climbs, Shafts.Climb(b, k, type) == ClimbIssue.None);
                            if (s == null) continue;
                            var op = Lod0.Build(new Site(d.buildings), b).Op;
                            Assert.True(StepCorners(op, b, k) > 0, what + ": no steps");
                            Assert.True(Covering(op, OverTheFlight(b, s), Derived.FloorBase(b, k + 1)) == 0, what + ": the slab over the flight is closed");
                            Assert.Null(NoFlight(d, b));
                        }
        }

        [Fact]
        public void StairsAnotherEditLeavesWithNothingToClimbToAreAProblem()
        {
            // on the top floor under a flat roof they climb to the roof
            var (d, b) = Block();
            var s = Place(d, b, 2)!;
            Assert.Null(NoFlight(d, b));
            // the roof made pitched: no roof access
            b.style.roofType = RoofType.Gable;
            Assert.Contains("pitched roof", NoFlight(d, b));
            // flat again, but they stop at the top floor
            b.style.roofType = RoofType.Flat; s.roof = false;
            Assert.Contains("continue to the roof", NoFlight(d, b));
            // From and To both floor 1
            s.roof = true; Shafts.SetBottom(s, 1); Shafts.SetTop(s, 1);
            Assert.Contains("From and To", NoFlight(d, b));
            // from the ground floor to the roof: flights all the way
            Shafts.SetBottom(s, 0); Shafts.SetTop(s, -1);
            Assert.Null(NoFlight(d, b));
            // every storey over the ground floor filled, the top floor's flight to the roof with them
            b.floors[1].filled = true; b.floors[2].filled = true;
            Assert.Contains("filled", NoFlight(d, b));
            // a shell-only building builds no stairs: nothing of its inside is a problem
            b.interior = false;
            Assert.Null(NoFlight(d, b));
        }
    }
}
