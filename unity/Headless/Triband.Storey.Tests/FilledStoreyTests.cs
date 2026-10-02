using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Filled storeys (<see cref="FloorData.filled"/>): nothing inside, lifts pass through, stairs stop at them. The
    /// building asked for: rooms on the ground floor and the top floor only, with a lift between them. Storey's own.
    /// </summary>
    public class FilledStoreyTests
    {
        // ground + 10 floors; floors 1–9 filled; a lift and switchback stairs from the ground
        static (StoreyDocument d, BuildingData b, CoreData lift, CoreData stairs) Tower(bool filled = true)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            b.interior = true; b.style.roofType = RoofType.Flat;
            b.floors = Enumerable.Range(0, 11).Select(k => new FloorData { filled = filled && k >= 1 && k <= 9 }).ToList();
            var lift = Shafts.Place(b, 0, new Vec2(2.5, 4.5), CoreType.Lift, 0, Shafts.NewId(d))!;
            var stairs = Shafts.Place(b, 0, new Vec2(8, 4.5), CoreType.Stairs, 0, Shafts.NewId(d))!;
            Assert.NotNull(lift); Assert.NotNull(stairs);
            return (d, b, lift, stairs);
        }

        [Fact]
        public void TheLiftStopsOnlyAtTheOpenStoreys()
        {
            var (_, b, lift, _) = Tower();
            Assert.Equal(new[] { 0, 10 }, Cores.Stops(b, lift));
            Assert.Equal(Enumerable.Range(0, 11), Cores.Levels(b, lift));   // the shaft still runs through
        }

        [Fact]
        public void StairsStopAtAFilledStorey()
        {
            var (_, b, _, stairs) = Tower();
            Assert.Empty(Enumerable.Range(0, 12).Where(k => Cores.HasFlight(b, stairs, k) && k < 10));
            Assert.True(Cores.HasFlight(b, stairs, 10));                       // the top floor still reaches the roof
            Assert.Empty(Enumerable.Range(1, 10).Where(k => Cores.StairHoleAt(b, stairs, k)));
            Assert.True(Cores.StairHoleAt(b, stairs, 11));
        }

        [Fact]
        public void ARideGoesFromTheGroundToTheTop()
        {
            var (d, b, lift, _) = Tower();
            var w = new PlayWorld(new Site(d.buildings));
            var p = new PlayerState(); p.Spawn(b.pos.x + lift.x, 0, b.pos.z + lift.z);
            Assert.False(Walker.CallLift(w, p, 5));   // a filled storey is not a stop
            Assert.True(Walker.CallLift(w, p, 10));
            for (int f = 0; f < 600 && p.ride != null; f++) Walker.Step(w, p, new MoveInput(0, 0), 0, 1.0 / 30);
            Assert.Equal(Derived.FloorBase(b, 10), p.y, 9);
            Assert.Equal(10, w.Locate(p.x, p.y, p.z)!.Value.floor);
        }

        [Fact]
        public void AFilledStoreyHasNoInsideAndOpaqueWindows()
        {
            var (d, b, _, _) = Tower();
            var site = new Site(d.buildings);
            var r = Lod0.Build(site, b);
            double Mid(int k) => Derived.FloorBase(b, k) + Derived.FloorH(b, k) / 2;
            // see-through glass only on the open storeys
            Assert.All(r.Glass.P, q => Assert.True(q.y < Derived.FloorBase(b, 1) || q.y > Derived.FloorBase(b, 10) - 0.01, $"glass at {q.y}"));
            // opaque panes on the filled ones
            Assert.Contains(Enumerable.Range(0, r.Op.P.Count), i => r.Op.C[i].slot == ColorSlot.Glass && Math.Abs(r.Op.P[i].y - Mid(5)) < Derived.FloorH(b, 5) / 2);
            var open = Lod0.Build(new Site(Tower(filled: false).d.buildings), Tower(filled: false).b);
            Assert.True(r.Op.Tris < open.Op.Tris * 0.8, $"{r.Op.Tris} triangles filled, {open.Op.Tris} open");
        }

        [Fact]
        public void ItsGeometryIsClean()
        {
            var (d, b, _, _) = Tower();
            b.floors[10].walls.Add(new WallData { a = new Vec2(0, 6), b = new Vec2(5, 6) });
            var site = new Site(d.buildings);
            foreach (var mesh in new[] { Lod0.Build(site, b, solids: true).Op, Lod1.Build(site, b, solids: true) })
            {
                var report = CoplanarCheck.Run(mesh);
                Assert.True(report.Overlaps.Count == 0, $"{report.Overlaps.Count} overlaps:\n  " + string.Join("\n  ", report.Overlaps.Take(8)));
            }
        }

        [Fact]
        public void FilledIsSavedOnlyWhereSet()
        {
            var (d, _, _, _) = Tower();
            var json = PrototypeJson.Write(d);
            Assert.Equal(9, json.Split("\"filled\"").Length - 1);
            var back = PrototypeJson.Read(json).Document.buildings[0];
            Assert.Equal(Enumerable.Range(0, 11).Select(k => k >= 1 && k <= 9), back.floors.Select(f => f.filled));
        }
    }
}
