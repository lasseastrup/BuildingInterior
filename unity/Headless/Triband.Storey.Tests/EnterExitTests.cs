using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Walking in and out of a building: the cutaway's margin for walls seen edge-on, the building held while it is
    /// still in the camera's way after the player steps out, and no LOD0 built for buildings that can't share a wall.
    /// </summary>
    public class EnterExitTests
    {
        const double Dt = 1.0 / 30;

        // a wall along x from (0, 0), 4 m long, its normal +z (scaled by 1 + length, as LOD0 writes it)
        static readonly double[] Wall = { 0, 0, 0, 5 };

        [Fact]
        public void AWallDropsOnlyWithThePlayerClearOfIt()
        {
            // the camera 6 m on one side; the player just past the line, then well past it
            Assert.True(OcclusionCore.WallBlocks(Wall, 2, 6, 2, -0.1));
            Assert.False(OcclusionCore.WallBlocks(Wall, 2, 6, 2, -0.1, 0.3));
            Assert.True(OcclusionCore.WallBlocks(Wall, 2, 6, 2, -0.5, 0.3));
            Assert.False(OcclusionCore.WallBlocks(Wall, 2, 6, 2, 0.5, 0.3));   // same side as the camera
        }

        /// <summary>The demo's Linden Court, its street door, and the outward normal there (world space).</summary>
        static (PlayWorld world, BuildingData b, double x, double z, double nx, double nz, double y) Door()
        {
            var (doc, _) = PlayWorldTests.World("demo");
            var world = new PlayWorld(new Site(doc.buildings));
            var b = world.Site.Buildings.First(x => x.name == "Linden Court");
            var e = b.entrances[0];
            var fp = Derived.OutlineAt(b, e.k); var a = fp[e.edge]; var c = fp[(e.edge + 1) % fp.Count];
            double dx = c.x - a.x, dz = c.z - a.z, L = Math.Sqrt(dx * dx + dz * dz), nx = dz / L, nz = -dx / L;
            double mx = a.x + (c.x - a.x) * e.t, mz = a.z + (c.z - a.z) * e.t;
            if (Geo.Pip(fp, mx + nx, mz + nz)) { nx = -nx; nz = -nz; }
            return (world, b, b.pos.x + mx, b.pos.z + mz, nx, nz, Derived.FloorBase(b, e.k));
        }

        /// <summary>
        /// Out on Linden Court's terrace (the roof of its lower six storeys, east of the setback that starts on storey 6), the
        /// player is outdoors: the building is shown whole, and when the setback stands between the camera and the player it is
        /// a building in the way from storey 6 up, sunk as any other, with nothing clipped or cut away and the six storeys under
        /// the terrace as they are (TerraceOcclusionTests). In the setback's own rooms it is as indoors either way.
        /// </summary>
        [Fact]
        public void OnATerraceTheBuildingIsWholeAndTheSetbackIsInTheWay()
        {
            var (doc, _) = PlayWorldTests.World("demo");
            var world = new PlayWorld(new Site(doc.buildings));
            var b = world.Site.Buildings.First(x => x.name == "Linden Court");
            const int K = 6; double y = Derived.FloorBase(b, K), ceiling = y + Derived.FloorH(b, K) - Dim.SLAB - 0.01;
            Assert.True(Derived.HasTerrace(b, K));
            (OcclusionView view, Occluder? way, bool cut, bool hidesAbove, bool hidesBelow) Seen(double lx, double lz, double camDx)
            {
                var core = new OcclusionCore(world, new OcclusionSettings());
                var p = new PlayerState(); double px = b.pos.x + lx, pz = b.pos.z + lz;
                p.Spawn(px, y, pz);
                var cam = (px + camDx, y + 6.0, pz);
                for (int i = 0; i < 30; i++) core.Frame(p, cam, (px, y + FollowCamera.Chest, pz), Dt);
                return (core.View, core.Active.FirstOrDefault(r => r.b == b), core.Walls.Any(kv => kv.Key.id == b.id && kv.Value > 0), core.HidesPoint(b, y + 6), core.HidesPoint(b, y - 2));
            }
            // on the terrace (local x 8..16, z 0..8), the camera east of the player, over the street: nothing in the way
            Assert.True(OcclusionCore.OnTerrace(b, K, Spot(b, 12, 4, y)));
            var open = Seen(12, 4, 10);
            Assert.Same(b, open.view.active); Assert.Equal(K, open.view.floor);   // still on its storey 6, for the HUD and the lifts
            Assert.Equal(1e9, open.view.clipY); Assert.False(open.view.cutOn); Assert.Equal(1e9, open.view.terraceY);
            Assert.Null(open.way); Assert.False(open.hidesAbove);
            // the camera west of the player, looking down through the setback: it is in the way from storey 6 up and sinks;
            // nothing is clipped or cut away, and the storeys under the terrace stand
            var behind = Seen(12, 4, -14);
            Assert.Same(b, behind.view.active); Assert.Equal(1e9, behind.view.clipY); Assert.False(behind.view.cutOn); Assert.False(behind.cut);
            Assert.Equal(y, behind.view.terraceY);
            Assert.True(behind.way != null && behind.way.k == K, "the setback isn't in the way from the terrace's storey");
            Assert.True(behind.hidesAbove); Assert.False(behind.hidesBelow);
            // in the setback's rooms behind the terrace (local x 0..8), with the same clear camera: indoors
            Assert.False(OcclusionCore.OnTerrace(b, K, Spot(b, 4, 4, y)));
            var inside = Seen(4, 4, 10);
            Assert.Equal(ceiling, inside.view.clipY, 9); Assert.True(inside.hidesAbove); Assert.Null(inside.way);
        }

        static PlayerState Spot(BuildingData b, double lx, double lz, double y) { var p = new PlayerState(); p.Spawn(b.pos.x + lx, y, b.pos.z + lz); return p; }

        /// <summary>
        /// Walking out of Linden Court's door with the camera trailing over the building: held as it was (clipped, cut away)
        /// while the player is at the door, then handed over to the buildings in the way, still held as it sinks, and let go
        /// once it has sunk, from then on in the way as any building is. Without the hold it is let go at once.
        /// </summary>
        [Fact]
        public void TheBuildingIsHeldAtItsDoorThenHandedOver()
        {
            var (world, b, x, z, nx, nz, y) = Door();
            foreach (bool hold in new[] { true, false })
            {
                var core = new OcclusionCore(world, new OcclusionSettings { holdAfterExit = hold });
                var p = new PlayerState();
                bool heldOutside = false, handing = false, released = false;
                // walking out of the door, the camera trailing 7 m behind (over the building) and 5 m up
                for (int i = 0; i <= 200; i++)
                {
                    double d = -2 + i * 0.07;
                    double px = x + nx * d, pz = z + nz * d;
                    p.Spawn(px, y, pz);
                    var cam = (px - nx * 7, y + 5.0, pz - nz * 7);
                    core.Frame(p, cam, (px, y + FollowCamera.Chest, pz), Dt);
                    if (d > 0.5 && d < 1.1)
                    {
                        // at the door: held as it was, not in the way
                        if (hold) Assert.True(core.View.active == b && !core.View.leaving, $"{d:F2} m out: Linden Court let go at its door");
                        else Assert.Null(core.View.active);
                    }
                    if (d > 0.2 && core.View.active == b) heldOutside = true;
                    var way = core.Active.FirstOrDefault(r => r.b == b);
                    if (core.View.leaving)
                    {
                        // handed over: still held, and in the way as well
                        Assert.True(hold && d > 1.1, $"{d:F2} m out: handed over at the door");
                        Assert.Same(b, core.View.active); Assert.NotNull(way);
                        handing = true;
                    }
                    else Assert.DoesNotContain(core.Active, r => r.b == core.View.active);   // held and in the way only while handed over
                    if (handing && !released && core.View.active == null)
                    {
                        // let go once it has sunk, and drawn as in the way alone from there
                        released = true;
                        Assert.True(way != null && way.t >= way.plan.total - 1e-9, $"{d:F2} m out: let go before it had sunk");
                    }
                }
                Assert.Equal(hold, heldOutside);
                Assert.Equal(hold, handing);
                if (hold) Assert.True(released, "Linden Court was never let go");
            }
        }

        /// <summary>
        /// Out of a building and away from it with the camera on its far side, it goes as a building the player never entered
        /// goes: under Dissolve, dissolved down to the storey at the player's height and let go, its row the same frame for
        /// frame once it has gone. It was held, cut away, for as long as the player walked on.
        /// </summary>
        [Fact]
        public void LeftByADoorItGoesAsABuildingNeverEnteredDoes()
        {
            var (world, b, x, z, nx, nz, y) = Door();
            foreach (var mode in new[] { OccluderMode.Dissolve, OccluderMode.Sink, OccluderMode.Slice })
            {
                var left = new OcclusionCore(world, new OcclusionSettings { mode = mode });
                var never = new OcclusionCore(world, new OcclusionSettings { mode = mode });
                var p = new PlayerState(); int same = 0;
                for (int i = 0; i <= 120; i++)
                {
                    double d = -2 + i * 0.07;
                    double px = x + nx * d, pz = z + nz * d;
                    p.Spawn(px, y, pz);
                    // the camera 9 m back over the building and 9 m up, as the play kit's
                    var cam = (px - nx * 9, y + 9.0, pz - nz * 9);
                    var target = (px, y + FollowCamera.Chest, pz);
                    left.Frame(p, cam, target, Dt);
                    if (d < 1.5) continue;
                    never.Frame(p, cam, target, Dt);   // first seen out here, never having been in
                    var a = never.Active.FirstOrDefault(r => r.b == b);
                    Assert.True(a != null, $"{mode}, {d:F2} m out: Linden Court isn't in the way of the camera over it");
                    var r = left.Active.FirstOrDefault(q => q.b == b);
                    Assert.True(r != null, $"{mode}, {d:F2} m out: Linden Court left is not in the way");
                    if (left.View.active != null) continue;   // still being handed over
                    Assert.Equal(a!.k, r!.k);
                    bool gone = mode == OccluderMode.Sink ? a.t >= a.plan.total - 1e-9 : a.a >= 1;
                    if (!gone) continue;
                    // both gone: the same row
                    int o = r.slot * OcclusionCore.RowTexels * 4, q = a.slot * OcclusionCore.RowTexels * 4;
                    for (int t = 0; t < OcclusionCore.RowTexels * 4; t++) Assert.Equal(never.Rows[q + t], left.Rows[o + t]);
                    same++;
                }
                Assert.True(same > 20, $"{mode}: {same} frames alike");
            }
        }

        [Fact]
        public void EnteringBuildsNoLod0ForBuildingsFarAway()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 300);
            var world = new PlayWorld(new Site(d.buildings));
            var core = new OcclusionCore(world, new OcclusionSettings());
            // a building whose outline's middle is inside it, with the player stood there on the ground floor
            var b = world.Site.Buildings.Skip(150).First(x => { var f = Derived.OutlineAt(x, 0); return PlayWorld.Inside(x, f.Average(q => q.x), f.Average(q => q.z)); });
            var fp = Derived.OutlineAt(b, 0);
            double cx = fp.Average(q => q.x) + b.pos.x, cz = fp.Average(q => q.z) + b.pos.z;
            var p = new PlayerState(); p.Spawn(cx, Derived.FloorBase(b, 0), cz);
            core.Frame(p, (cx, 6, cz - 8), (cx, 1.4, cz), Dt);
            Assert.True(core.View.active == b, $"the player is in {core.View.active?.name ?? "nothing"}, not {b.name}");
            Assert.True(world.ShapeCount <= world.Touching(b).Count && world.ShapeCount < 30, $"{world.ShapeCount} shapes made of {world.Site.Buildings.Count}");
        }
    }
}
