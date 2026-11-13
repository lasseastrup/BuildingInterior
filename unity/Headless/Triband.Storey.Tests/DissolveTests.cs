using System;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Dissolve: the prototype's Sink walks replayed with Sink and with Dissolve side by side. Each building in the way
    /// is taken down to the same storey, its row carries no segments, the fade and that storey's floor, and it ends fully
    /// dissolved where Sink ends fully sunk.
    /// </summary>
    public class DissolveTests
    {
        const double Dt = 1.0 / 30;

        /// <summary>
        /// Dissolve's looks: only Outline and Blueprint make the footprint with lines, and Stub has no footprint, its storey
        /// staying up to the stub, so a prop there is still drawn and one over it is hidden as under the others.
        /// </summary>
        [Fact]
        public void ItsLooksLeaveWhatTheySay()
        {
            foreach (DissolveLook look in Enum.GetValues(typeof(DissolveLook)))
            {
                var s = new OcclusionSettings { mode = OccluderMode.Dissolve, dissolveLook = look, outlineWidth = 0.08 };
                bool lines = look == DissolveLook.Outline || look == DissolveLook.Blueprint;
                Assert.Equal(lines ? 0.08 : 0, s.CageWidth);
                Assert.Equal(look != DissolveLook.Stub, s.Footprint);
                s.mode = OccluderMode.Sink;
                Assert.Equal(0, s.CageWidth); Assert.True(s.Footprint);
            }
            // the demo's Linden Court, in the way of a camera over it from its street door out
            var (doc, _) = PlayWorldTests.World("demo");
            var world = new PlayWorld(new Site(doc.buildings));
            var b = world.Site.Buildings.First(x => x.name == "Linden Court");
            var e = b.entrances[0]; var fp = Derived.OutlineAt(b, e.k); var a = fp[e.edge]; var c = fp[(e.edge + 1) % fp.Count];
            double dx = c.x - a.x, dz = c.z - a.z, L = Math.Sqrt(dx * dx + dz * dz), nx = dz / L, nz = -dx / L;
            double mx = a.x + dx * e.t, mz = a.z + dz * e.t; if (Geo.Pip(fp, mx + nx, mz + nz)) { nx = -nx; nz = -nz; }
            double y = Derived.FloorBase(b, e.k), px = b.pos.x + mx + nx * 3, pz = b.pos.z + mz + nz * 3;
            foreach (var look in new[] { DissolveLook.Plain, DissolveLook.Stub })
            {
                var core = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Dissolve, dissolveLook = look, stub = 1.0 });
                var p = new PlayerState(); p.Spawn(px, y, pz);
                for (int i = 0; i < 20; i++) core.Frame(p, (px - nx * 9, y + 9, pz - nz * 9), (px, y + FollowCamera.Chest, pz), Dt);
                var r = core.Active.Single(q => q.b == b);
                Assert.Equal(1, r.a);
                double floor = Derived.FloorBase(b, r.k);
                Assert.Equal(look != DissolveLook.Stub, core.HidesPoint(b, floor + 0.5));   // under the stub
                Assert.True(core.HidesPoint(b, floor + 1.5));                              // over it
            }
        }

        [Fact]
        public void EndsWhereSinkEnds()
        {
            var root = PlayWorldTests.Play.Value.RootElement;
            int walks = 0, sunk = 0, dissolved = 0;
            foreach (var w in root.GetProperty("walks").EnumerateArray())
            {
                if (w.GetProperty("occ").GetProperty("outside").GetString() != "sink") continue;
                walks++;
                var (doc, _) = PlayWorldTests.World("demo");
                var world = new PlayWorld(new Site(doc.buildings));
                var sink = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Sink });
                var diss = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Dissolve });
                var start = w.GetProperty("start").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var c = w.GetProperty("cam").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var p = new PlayerState(); p.Spawn(start[0], start[1], start[2]);
                var cam = new FollowCamera { yaw = c[0], pitch = c[1], dist = c[2], assist = 0 }; cam.Snap(p);
                int f = 0;
                foreach (var fr in w.GetProperty("frames").EnumerateArray())
                {
                    var input = fr.GetProperty("input");
                    bool K(string k) => input.TryGetProperty(k, out var v) && v.GetBoolean();
                    if (input.TryGetProperty("yaw", out var yv)) cam.yaw = yv.GetDouble();
                    Walker.Step(world, p, new MoveInput((K("KeyD") ? 1 : 0) - (K("KeyA") ? 1 : 0), (K("KeyW") ? 1 : 0) - (K("KeyS") ? 1 : 0), K("ShiftLeft")), cam.yaw, Dt);
                    cam.Follow(p, Dt);
                    var pos = cam.Position();
                    sink.Frame(p, pos, (cam.tx, cam.ty, cam.tz), Dt);
                    diss.Frame(p, pos, (cam.tx, cam.ty, cam.tz), Dt);
                    string at = $"walk {walks}, frame {f++}";

                    foreach (var r in diss.Active)
                    {
                        Assert.True(r.t == 0, $"{at}: {r.b.name} has a Sink time under Dissolve");
                        int o = r.slot * OcclusionCore.RowTexels * 4, N = r.b.floors.Count;
                        Assert.Equal(0f, diss.Rows[o]);                       // no segments: not Sink's squash
                        Assert.Equal((float)r.a, diss.Rows[o + 1]);           // the fade, and the darkening
                        Assert.True(diss.Rows[o + 2] > 1e8, $"{at}: {r.b.name} is sliced");
                        Assert.Equal((float)(r.k < N ? Derived.FloorBase(r.b, r.k) : Derived.RoofY(r.b)), diss.Rows[o + 3]);
                        if (r.a >= 1) dissolved++;
                    }
                    foreach (var s in sink.Active)
                    {
                        if (s.t < s.plan.total - 1e-9) continue;
                        sunk++;
                        // fully sunk: the same building is going under Dissolve, down to the same storey's floor
                        var d = diss.Active.FirstOrDefault(r => r.b.id == s.b.id);
                        Assert.True(d != null, $"{at}: {s.b.name} is sunk but not dissolving");
                        Assert.Equal(s.k, d!.k);
                        Assert.Equal((float)s.plan.segs[0].y, diss.Rows[d.slot * OcclusionCore.RowTexels * 4 + 3]);
                    }
                }
            }
            Assert.True(walks > 0 && sunk > 0 && dissolved > 0, $"walks {walks}, sunk frames {sunk}, dissolved frames {dissolved}");
        }
    }
}
