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

        [Fact]
        public void TheBuildingIsHeldWhileTheCameraIsStillBehindIt()
        {
            var (world, b, x, z, nx, nz, y) = Door();
            foreach (bool hold in new[] { true, false })
            {
                var core = new OcclusionCore(world, new OcclusionSettings { holdAfterExit = hold });
                var p = new PlayerState();
                bool heldOutside = false, released = false;
                // walking out of the door, the camera trailing 7 m behind (over the building) and 5 m up
                for (int i = 0; i <= 200; i++)
                {
                    double d = -2 + i * 0.07;
                    double px = x + nx * d, pz = z + nz * d;
                    p.Spawn(px, y, pz);
                    var cam = (px - nx * 7, y + 5.0, pz - nz * 7);
                    core.Frame(p, cam, (px, y + FollowCamera.Chest, pz), Dt);
                    if (d > 0.5 && d < 1.5)
                    {
                        if (hold) Assert.True(core.View.active == b, $"{d:F2} m out: Linden Court let go with the camera over it");
                        else Assert.Null(core.View.active);
                    }
                    if (d > 0.2 && core.View.active == b) heldOutside = true;
                    if (heldOutside && core.View.active == null) released = true;
                    Assert.DoesNotContain(core.Active, r => r.b == core.View.active);   // never both held and in the way
                }
                Assert.Equal(hold, heldOutside);
                if (hold) Assert.True(released, "Linden Court was never let go once the camera had passed it");
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
            Assert.True(world.Lod0Count <= world.Touching(b).Count && world.Lod0Count < 30, $"{world.Lod0Count} LOD0s built of {world.Site.Buildings.Count}");
        }
    }
}
