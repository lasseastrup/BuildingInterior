using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A walk-in building in the way keeps its doors (KeptDoors, docs/PLAY.md §4): a box round each door of its walk-in storeys
    /// at or under the storey at the player's height, as LOD0 cuts it, carried at the end of its occluder row, and a gap in its
    /// footprint's rim there.
    /// </summary>
    public class KeptDoorsTests
    {
        const double Dt = 1.0 / 30;

        [Fact]
        public void AWalkInBuildingKeepsItsDoors()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var site = new Site(doc.buildings);
            var boxes = new List<DoorBox>(); int kept = 0, terrace = 0;
            foreach (var b in doc.buildings)
            {
                int N = b.floors.Count;
                int Open(int k) => b.entrances.Count(e => e.k <= k && Facade.DoorOpen(site, b, e));
                KeptDoors.Of(site, b, 0, boxes);
                if (!b.interior) { Assert.Empty(boxes); KeptDoors.Of(site, b, N - 1, boxes); Assert.Empty(boxes); continue; }
                // with the player on the ground, its street doors; from the top storey, its terraces' doors too
                Assert.Equal(Open(0), boxes.Count);
                Assert.True(boxes.Count > 0, $"{b.name} keeps no door");
                KeptDoors.Of(site, b, N - 1, boxes);
                Assert.Equal(Math.Min(Open(N - 1), KeptDoors.Max), boxes.Count);
                terrace += boxes.Count(d => d.k > 0);
                foreach (var d in boxes)
                {
                    var f = Details.EdgeFrameAt(b, d.k, d.edge);
                    double mid = (d.u0 + d.u1) / 2, floor = Derived.FloorBase(b, d.k), T = Dim.T_EXT;
                    // the door in the wall, low and high, its step outside and the floor inside
                    var inWall = f.e.F.At2(mid, T / 2);
                    Assert.True(d.Holds(inWall.x, floor + 0.1, inWall.z), $"{b.name}: its door's foot isn't kept");
                    Assert.True(d.Holds(inWall.x, floor + 2.0, inWall.z), $"{b.name}: its door's head isn't kept");
                    var step = f.e.F.At2(mid, T + 0.2); var inside = f.e.F.At2(mid, -0.2);
                    Assert.True(d.Holds(step.x, floor + 0.05, step.z) && d.Holds(inside.x, floor + 0.05, inside.z), $"{b.name}: its step or threshold isn't kept");
                    // not the wall a metre past it, the room two metres in, nor the storey above
                    var past = f.e.F.At2(d.u1 + 1, T / 2); var room = f.e.F.At2(mid, -2);
                    Assert.False(d.Holds(past.x, floor + 1, past.z), $"{b.name}: the wall past its door is kept");
                    Assert.False(d.Holds(room.x, floor + 1, room.z), $"{b.name}: the room behind its door is kept");
                    Assert.False(d.Holds(inWall.x, floor + Derived.FloorH(b, d.k) + 0.5, inWall.z), $"{b.name}: the storey over its door is kept");
                    // where its entrance asks for it, or moved in from the wall's end
                    var e = b.entrances.First(x => x.k == d.k && x.edge == d.edge && Math.Abs(x.t * f.e.L - mid) < 1.5);
                    Assert.NotNull(e);
                    kept++;
                }
            }
            Assert.True(kept > 0 && terrace > 0, $"{kept} doors kept, {terrace} onto terraces");
        }

        [Fact]
        public void AShellStoreysDoorIsNotKept()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var site = new Site(doc.buildings);
            var b = doc.buildings.First(x => x.name == "Corner Shop");
            var boxes = new List<DoorBox>();
            KeptDoors.Of(site, b, 0, boxes); Assert.Single(boxes);
            b.floors[0].filled = true;   // its ground storey filled: shut, as a shell's
            KeptDoors.Of(site, b, 0, boxes); Assert.Empty(boxes);
        }

        /// <summary>
        /// Linden Court in the way of a camera over it, the player 3 m out of its first street door: under every mode its row
        /// ends with its two street doors' boxes, after Sink's segments, and with Keep Doors off with none.
        /// </summary>
        [Fact]
        public void ItsRowCarriesItsDoorsAfterSinksSegments()
        {
            var (doc, _) = PlayWorldTests.World("demo");
            var world = new PlayWorld(new Site(doc.buildings));
            var b = world.Site.Buildings.First(x => x.name == "Linden Court");
            var e = b.entrances[0]; var fp = Derived.OutlineAt(b, e.k); var a = fp[e.edge]; var c = fp[(e.edge + 1) % fp.Count];
            double dx = c.x - a.x, dz = c.z - a.z, L = Math.Sqrt(dx * dx + dz * dz), nx = dz / L, nz = -dx / L;
            double mx = a.x + dx * e.t, mz = a.z + dz * e.t; if (Geo.Pip(fp, mx + nx, mz + nz)) { nx = -nx; nz = -nz; }
            double y = Derived.FloorBase(b, e.k), px = b.pos.x + mx + nx * 3, pz = b.pos.z + mz + nz * 3;
            foreach (var mode in new[] { OccluderMode.Dissolve, OccluderMode.Sink, OccluderMode.Slice, OccluderMode.Cutout, OccluderMode.Fade })
                foreach (bool keep in new[] { true, false })
                {
                    var core = new OcclusionCore(world, new OcclusionSettings { mode = mode, keepDoors = keep });
                    var p = new PlayerState(); p.Spawn(px, y, pz);
                    for (int i = 0; i < 20; i++) core.Frame(p, (px - nx * 9, y + 9, pz - nz * 9), (px, y + FollowCamera.Chest, pz), Dt);
                    var r = core.Active.Single(q => q.b == b);
                    int o = r.slot * OcclusionCore.RowTexels * 4, nd = (int)core.Rows[o + (OcclusionCore.RowTexels - 1) * 4];
                    if (!keep) { Assert.Equal(0, nd); continue; }
                    var want = new List<DoorBox>(); KeptDoors.Of(world.Site, b, r.k, want);
                    Assert.Equal(0, r.k);
                    Assert.Equal(2, want.Count);
                    Assert.Equal(want.Count, nd);
                    Assert.True(OcclusionCore.DoorTexel(nd - 1) > (int)core.Rows[o], $"{mode}: its doors run into Sink's segments");
                    for (int i = 0; i < nd; i++)
                    {
                        int t = o + OcclusionCore.DoorTexel(i) * 4; var d = want[i];
                        Assert.Equal(new[] { (float)d.cx, (float)d.cz, (float)d.ux, (float)d.uz, (float)d.halfW, (float)d.halfD, (float)d.y0, (float)d.y1 }, core.Rows.Skip(t).Take(8).ToArray());
                    }
                }
        }

        [Fact]
        public void TheFootprintsRimLeavesAGapAtAKeptDoor()
        {
            var doc = PrototypeJson.Read(Fixtures.Text("demo.json")).Document;
            var site = new Site(doc.buildings);
            var boxes = new List<DoorBox>(); int gaps = 0;
            foreach (var b in doc.buildings.Where(x => x.interior))
            {
                KeptDoors.Of(site, b, 0, boxes);
                var with = SinkFootprint.Build(site, b, 0, 0, false, true); var without = SinkFootprint.Build(site, b, 0);
                double floor = Derived.FloorBase(b, 0);
                foreach (var d in boxes)
                {
                    var f = Details.EdgeFrameAt(b, 0, d.edge);
                    double mid = (d.u0 + d.u1) / 2;
                    (double u, double w) At(P3 q) { double lx = q.x - f.e.a.x, lz = q.z - f.e.a.z; return (lx * f.e.u.x + lz * f.e.u.z, lx * f.e.w.x + lz * f.e.w.z); }
                    // a rim triangle (over the fill, in the wall's thickness) across the door's middle
                    bool Across(MeshBuilder op)
                    {
                        for (int t = 0; t + 2 < op.I.Count; t += 3)
                        {
                            var q = new[] { op.P[op.I[t]], op.P[op.I[t + 1]], op.P[op.I[t + 2]] };
                            if (q.Max(v => v.y) < floor + 0.05) continue;
                            var uw = q.Select(At).ToArray();
                            if (uw.Any(x => x.w < -0.01 || x.w > Dim.T_EXT + 0.01)) continue;
                            if (uw.Min(x => x.u) < mid && uw.Max(x => x.u) > mid) return true;
                        }
                        return false;
                    }
                    Assert.False(Across(with), $"{b.name}: the rim runs across its kept door");
                    Assert.True(Across(without), $"{b.name}: the rim didn't run across its door before");
                    gaps++;
                }
                with.Recycle(); without.Recycle();
            }
            Assert.True(gaps > 0);
        }
    }
}
