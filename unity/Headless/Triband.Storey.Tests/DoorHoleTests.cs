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
    /// An artist's street door that keeps its size, as the old BuildingGenerator set its door prefabs: as wide as it is on a
    /// shell storey (a shop's double door isn't squeezed into a house door's hole), its hole bigger or smaller than it by the
    /// style's offset (<see cref="FacadeStyle.doorRecessOffset"/>; smaller, its frame is behind the wall), the recess closed
    /// at its depth in the wall's colour (<see cref="FacadeStyle.recessBack"/>), and in fixed bays the windows of the bays
    /// beside it kept. Storey's own.
    /// </summary>
    public class DoorHoleTests
    {
        // a leaf in front of the door's pivot and a frame round it behind, w × h
        static OpeningKind Door(string id, double w, double h)
        {
            var P = new List<P3>(); var tris = new List<int>(); var frame = new List<int>();
            void Q(double x0, double y0, double x1, double y1, double z, List<int> into)
            {
                int s = P.Count;
                P.AddRange(new[] { new P3(x0, y0, z), new P3(x1, y0, z), new P3(x1, y1, z), new P3(x0, y1, z) });
                into.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Q(-w / 2 + 0.2, 0, w / 2 - 0.2, h - 0.2, 0.03, tris);
            Q(-w / 2, 0, w / 2, h, -0.1, frame);
            var m = new OpeningKind.Mesh { P = P.ToArray(), N = Enumerable.Repeat(new P3(0, 0, 1), P.Count).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Door, leaf = true, tris = tris.ToArray() });
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = frame.ToArray() });
            return new OpeningKind { id = id, name = id, door = true, w = w, h = h, stretch = false, lod0 = m };
        }

        static OpeningKind Window(string id, double w, double h)
        {
            var P = new[] { new P3(-w / 2, 0, 0.02), new P3(w / 2, 0, 0.02), new P3(w / 2, h, 0.02), new P3(-w / 2, h, 0.02) };
            var m = new OpeningKind.Mesh { P = P, N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            return new OpeningKind { id = id, name = id, w = w, h = h, stretch = false, lod0 = m };
        }

        static (BuildingData b, Site site) Block(FacadeStyle st, bool interior = false, double t = 0.5)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            Floors.SetCount(b, 2);
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = t }); b.details.Clear(); b.shafts.Clear();
            b.interior = interior; b.style = st;
            return (b, new Site(d.buildings));
        }

        static Opening DoorOf(BuildingData b, Site site)
        {
            var f = Details.EdgeFrameAt(b, 0, 0);
            return Facade.Ops(site, b, 0, 0, f.e.L, f.uS, f.uE).Single(o => o.door);
        }

        static FacadeStyle Style(string door)
        {
            var st = Styles.Preset("brick").Clone();
            st.doorKind = door; st.ground = GroundType.Match;
            return st;
        }

        [Fact]
        public void AShopDoorKeepsItsWidthOnAShellStorey()
        {
            OpeningKinds.Register(Door("hole-shop", 4.3, 1.81));
            var st = Style("hole-shop");
            var shell = DoorOf(Block(st).b, Block(st).site);
            Assert.Equal(4.3, shell.u1 - shell.u0, 6);
            Assert.Equal(1.81, shell.y1, 6);
            // a walk-in storey's doorway, which the player walks through, stays the standard one
            var (bw, sw) = Block(st, interior: true);
            var walk = DoorOf(bw, sw);
            Assert.Equal(Dim.DOOR_EXT, walk.u1 - walk.u0, 6);
            var r = CoplanarCheck.Run(Lod0.Build(Block(st).site, Block(st).b, solids: true).Op);
            Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(5)));
            OpeningKinds.Remove("hole-shop");
        }

        [Fact]
        public void AHoleSmallerThanTheDoorHidesItsFrameBehindTheWall()
        {
            OpeningKinds.Register(Door("hole-house", 1.49, 1.92));
            var st = Style("hole-house"); st.doorRecess = 0.2; st.doorRecessOffset = -0.21;
            var (b, site) = Block(st);
            var o = DoorOf(b, site);
            Assert.Equal(1.49 - 0.42, o.u1 - o.u0, 6);
            Assert.Equal(1.92 - 0.21, o.y1, 6);
            Assert.Equal(-0.21, o.hl, 9); Assert.Equal(-0.21, o.hr, 9); Assert.Equal(-0.21, o.ht, 9);
            // the door itself keeps its size: its frame 1.49 m wide, pushed 20 cm in, behind the wall round the hole
            var op = Lod0.Build(site, b).Op;
            var frame = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Frame && op.P[v].y < 3).Select(v => op.P[v]).ToList();
            Assert.Equal(1.49, frame.Max(p => p.x) - frame.Min(p => p.x), 6);
            Assert.All(frame, p => Assert.Equal(-(Dim.T_EXT - 0.2 - 0.1), p.z, 6));   // its pivot 20 cm in from the face, the frame 10 cm behind it
            // without a recess the offset does nothing
            var flat = Style("hole-house"); flat.doorRecessOffset = -0.21;
            var (bf, sf) = Block(flat);
            Assert.Equal(1.49, DoorOf(bf, sf).u1 - DoorOf(bf, sf).u0, 6);
            OpeningKinds.Remove("hole-house");
        }

        [Fact]
        public void AHoleBiggerThanTheDoorIsHeldToItsBayAndClosedInTheWallsColour()
        {
            OpeningKinds.Register(Door("hole-big", 1.2, 2.0));
            var st = Style("hole-big"); st.doorRecess = 0.12; st.doorRecessOffset = 0.41; st.recessBack = true; st.bayFit = BayFit.Fixed; st.bay = 1.5;
            var (b, site) = Block(st);
            var o = DoorOf(b, site);
            Assert.Equal(1.5, o.u1 - o.u0, 6);                                      // 1.2 + 2 × 0.41, held to the bay
            Assert.Equal(0.15, o.hl, 6);
            Assert.InRange(o.y1, 2.0 + 0.01, 2.0 + 0.41 + 1e-9);
            // the back: a face in the wall's colour across the whole hole, 12 cm in from the wall's face, facing out
            var op = Lod0.Build(site, b).Op;
            double z = -(Dim.T_EXT - 0.12);
            var back = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Wall && Math.Abs(op.P[v].z - z) < 1e-6 && op.N[v].z < -0.9 && op.P[v].y < 3).Select(v => op.P[v]).ToList();
            Assert.NotEmpty(back);
            Assert.Equal(o.u0, back.Min(p => p.x), 6); Assert.Equal(o.u1, back.Max(p => p.x), 6); Assert.Equal(o.y1, back.Max(p => p.y), 6);
            var r = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
            Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(5)));
            var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true));
            Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(5)));
            OpeningKinds.Remove("hole-big");
        }

        [Fact]
        public void InFixedBaysTheWindowsBesideADoorStay()
        {
            OpeningKinds.Register(Door("hole-tight", 1.49, 1.92)); OpeningKinds.Register(Window("hole-win", 1.39, 0.83));
            foreach (var fit in new[] { BayFit.Fixed, BayFit.Spread })
            {
                var st = Style("hole-tight"); st.windowKind = "hole-win"; st.bay = 1.53; st.bayFit = fit; st.winCentre = true;
                st.doorRecess = 0.2;
                var (oS, oE) = Facade.OuterFace(new List<Vec2> { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) }, true, 0);
                var (n, bay, bS) = Facade.Bays(st, oS, oE, oS, oE);
                // the door in the middle bay, as the converter puts the old generator's
                double mid = bS + (n / 2 + 0.5) * bay;
                var (b, site) = Block(st, t: mid / 14);
                var f = Details.EdgeFrameAt(b, 0, 0);
                var ops = Facade.Ops(site, b, 0, 0, f.e.L, f.uS, f.uE);
                var door = ops.Single(o => o.door);
                bool beside = ops.Any(o => !o.door && o.u1 < door.u0 && door.u0 - o.u1 < 0.2) && ops.Any(o => !o.door && o.u0 > door.u1 && o.u0 - door.u1 < 0.2);
                Assert.True(beside == (fit == BayFit.Fixed), $"{fit}: windows beside the door {beside}");
                Assert.DoesNotContain(ops, o => !o.door && o.u1 > door.u0 && o.u0 < door.u1);
            }
            OpeningKinds.Remove("hole-tight"); OpeningKinds.Remove("hole-win");
        }

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var st = Styles.Preset("brick").Clone(); st.doorRecessOffset = -0.21; st.recessBack = true;
            var json = PrototypeJson.WriteStyle(st);
            Assert.Contains("\"doorRecessOffset\":-0.21", json); Assert.Contains("\"recessBack\":true", json);
            var back = PrototypeJson.ReadStyle(json);
            Assert.Equal((-0.21, true), (back.doorRecessOffset, back.recessBack));
            var (b, _) = Block(st);
            var d = new StoreyDocument(); d.buildings.Add(b);
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style;
            Assert.Equal((-0.21, true), (baked.doorRecessOffset, baked.recessBack));
            // unset, neither is written
            var plain = PrototypeJson.WriteStyle(Styles.Preset("brick"));
            Assert.DoesNotContain("doorRecessOffset", plain); Assert.DoesNotContain("recessBack", plain);
        }
    }
}
