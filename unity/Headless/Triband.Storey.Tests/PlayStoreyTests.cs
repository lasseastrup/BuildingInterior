using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>The walk model where Storey goes past the prototype (docs/PLAY.md §3): straight flights, and the lift ride.</summary>
    public class PlayStoreyTests
    {
        static (StoreyDocument d, BuildingData b, PlayWorld w) Block(CoreType t)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));   // 12 × 9 m, 3 floors, at its free spot
            b.interior = true;
            Assert.NotNull(Shafts.Place(b, 0, new Vec2(6, 4.5), t, 0, Shafts.NewId(d)));
            return (d, b, new PlayWorld(new Site(d.buildings)));
        }

        static (double x, double z) At(BuildingData b, CoreData s, double u, double w)
        {
            double a = s.rot * Math.PI / 180;
            return (b.pos.x + s.x + Math.Cos(a) * u - Math.Sin(a) * w, b.pos.z + s.z + Math.Sin(a) * u + Math.Cos(a) * w);
        }

        [Fact]
        public void AStraightFlightIsARampUpItsLane()
        {
            var (_, b, w) = Block(CoreType.Flight);
            var s = b.shafts[0];
            double lane = -Dim.FLIGHT_W / 2 + Dim.FLIGHT_LANE / 2, z0 = -Dim.FLIGHT_D / 2 + Dim.FLIGHT_LANDING, z1 = Dim.FLIGHT_D / 2 - Dim.FLIGHT_LANDING, h = Derived.FloorH(b, 0);
            var (mx, mz) = At(b, s, lane, (z0 + z1) / 2);
            Assert.Equal(h / 2, w.SurfaceAt(mx, mz, h / 2), 9);                         // halfway up
            var (wx, wz) = At(b, s, Dim.FLIGHT_W / 2 - 0.3, (z0 + z1) / 2);
            Assert.Equal(Derived.FloorBase(b, 1), w.SurfaceAt(wx, wz, Derived.FloorBase(b, 1)), 9);   // the walkway on floor 1 is floor
            Assert.Equal(0, w.SurfaceAt(wx, wz, 0.1), 9);                                // and the ground floor under it
            Assert.Equal(h / 2, w.SurfaceAt(mx, mz, h / 2 + 0.01), 9);                   // floor 1's slab is open over the run
        }

        [Fact]
        public void AWalkUpAStraightFlightReachesTheFloorAbove()
        {
            var (_, b, w) = Block(CoreType.Flight);
            var s = b.shafts[0];
            double lane = -Dim.FLIGHT_W / 2 + Dim.FLIGHT_LANE / 2;
            var (sx, sz) = At(b, s, lane, -Dim.FLIGHT_D / 2 + 0.4);
            var p = new PlayerState(); p.Spawn(sx, 0, sz);
            double a = s.rot * Math.PI / 180, yaw = Math.Atan2(Math.Sin(a), -Math.Cos(a));   // the camera looks along +w: forward is up the flight
            for (int f = 0; f < 60; f++) Walker.Step(w, p, new MoveInput(0, 1), yaw, 1.0 / 30);
            Assert.Equal(Derived.FloorBase(b, 1), p.y, 6);
            Assert.Equal(1, w.Locate(p.x, p.y, p.z)!.Value.floor);
        }

        [Fact]
        public void ALiftCarriesThePlayerToTheFloorCalled()
        {
            var (_, b, w) = Block(CoreType.Lift);
            var s = b.shafts[0];
            var p = new PlayerState(); p.Spawn(b.pos.x + s.x, 0, b.pos.z + s.z);
            Assert.False(Walker.CallLift(w, p, 0));   // already here
            Assert.True(Walker.CallLift(w, p, 2));
            for (int f = 0; f < 120 && p.ride != null; f++)
            {
                Walker.Step(w, p, new MoveInput(1, 0), 0, 1.0 / 30);
                if (p.ride != null) Assert.Equal(b.pos.x + s.x, p.x, 9);   // no walking while it rides (the frame it arrives, the player may step off)
            }
            Assert.Null(p.ride);
            Assert.Equal(Derived.FloorBase(b, 2), p.y, 9);
        }
    }
}
