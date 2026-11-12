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
    /// A window's version for buildings with interiors (<see cref="OpeningKind.inside"/>, the Unity side's
    /// <c>StoreyOpening.interiorPrefab</c>; docs/OPENINGS.md §7.3): where a storey's rooms see the window, LOD0 places that
    /// version as it is, in place of the window, with no backs of its own made for it; what it turns to the rooms goes with
    /// them. Shell storeys, LOD1, and a window without one are as before (<see cref="OpeningBackTests"/>). Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class InteriorWindowTests
    {
        // the version for interiors: OpeningBackTests' street-only window, its frame and sill in colours of their own, so
        // what is placed tells which version it is. Its top member's back is turned to the rooms (2 triangles)
        const string FrameColour = "#A1B2C3", SillColour = "#A1B2C4";
        const int FrameTris = 30, SillTris = 6, Turned = 2;

        static OpeningKind WithInterior(string id, bool glass = true)
        {
            var k = OpeningBackTests.StreetOnly(id, glass);
            var m = OpeningBackTests.StreetOnly(id + "-in", glass).lod0;
            foreach (var p in m.parts) p.own = p.slot == ColorSlot.Frame ? FrameColour : p.slot == ColorSlot.Metal ? SillColour : null;
            k.inside = m;
            return k;
        }

        /// <summary>A 14 × 10 m brick block, its windows <paramref name="kind"/>; walked in where it has <paramref name="interior"/>.</summary>
        static (BuildingData b, Site site) Block(bool interior, string kind)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.interior = interior; b.style = Styles.Preset("brick").Clone(); b.style.ground = GroundType.Match; b.style.windowKind = kind;
            return (b, new Site(d.buildings));
        }

        static string? Colour(MeshBuilder m, int t) => KindColors.RefOf(m.C[m.I[3 * t]].slot);
        static int Tris(MeshBuilder m, Func<int, bool> which) => Enumerable.Range(0, m.Tris).Count(which);
        static bool[] Marked(MeshBuilder m, List<(int start, int end)> ranges)
        {
            var o = new bool[m.Tris];
            foreach (var (s, e) in ranges) for (int t = s; t < e; t++) o[t] = true;
            return o;
        }

        [Fact]
        public void WhereTheRoomsSeeItTheWindowIsItsVersionForInteriors()
        {
            OpeningKinds.Register(WithInterior("t-in"));
            // a shell building: the window, its version for interiors nowhere
            var (bs, ss) = Block(false, "t-in"); var shell = Lod0.Build(ss, bs).Op;
            int windows = Enumerable.Range(0, shell.Tris).Count(t => shell.C[shell.I[3 * t]].slot == ColorSlot.Glass) / 2;
            Assert.True(windows >= 8, $"{windows} windows");
            Assert.Equal(FrameTris * windows, Tris(shell, t => shell.C[shell.I[3 * t]].slot == ColorSlot.Frame));
            Assert.Equal(0, Tris(shell, t => Colour(shell, t) is FrameColour or SillColour));
            // walked in: every window its version for interiors, as made: no backs made for it, and none of the window
            var (bw, sw) = Block(true, "t-in"); var r = Lod0.Build(sw, bw); var op = r.Op;
            int walked = Enumerable.Range(0, r.Glass.Tris).Count(t => r.Glass.C[r.Glass.I[3 * t]].slot == ColorSlot.Glass) / 4;
            Assert.True(walked >= 8, $"{walked} windows walked in");
            Assert.Equal(FrameTris * walked, Tris(op, t => Colour(op, t) == FrameColour));
            Assert.Equal(SillTris * walked, Tris(op, t => Colour(op, t) == SillColour));
            Assert.Equal(0, Tris(op, t => op.C[op.I[3 * t]].slot == ColorSlot.Frame));
            var backing = Marked(op, op.BackingTris);
            Assert.Equal(0, Tris(op, t => Colour(op, t) is FrameColour or SillColour && backing[t]));
            // LOD1 is never walked in: its plain pane, as before
            var l1 = Lod1.Build(sw, bw);
            Assert.Equal(0, Enumerable.Range(0, l1.Tris).Count(t => KindColors.RefOf(l1.C[l1.I[3 * t]].slot) is FrameColour or SillColour));
        }

        [Fact]
        public void WhatItTurnsToTheRoomsGoesWithThem()
        {
            // without glass a window shows its storey's rooms only near (the rooms kept apart): its faces turned to the rooms go
            // with them, the rest with the shell. With glass, clear at any distance, all of it stays with the shell
            OpeningKinds.Register(WithInterior("t-in-rooms", glass: false));
            OpeningKinds.Register(WithInterior("t-in-shell"));
            foreach (var (id, apart) in new[] { ("t-in-rooms", true), ("t-in-shell", false) })
            {
                var (b, site) = Block(true, id); var op = Lod0.Build(site, b).Op;
                var rooms = Marked(op, op.InsideTris);
                double y1 = Derived.FloorBase(b, 1);   // the ground storey's street door shows its rooms at any distance
                bool Above(int t) => op.P[op.I[3 * t]].y > y1;
                bool ToRooms(int t)
                {
                    P3 a = op.P[op.I[3 * t]], bb = op.P[op.I[3 * t + 1]], c = op.P[op.I[3 * t + 2]];
                    var n = P3.Cross(bb - a, c - a).Normalized; var f = Facing(b, a);
                    return n.x * f.x + n.z * f.z < -0.5;   // into the building, from the wall it is on
                }
                var mine = Enumerable.Range(0, op.Tris).Where(t => Colour(op, t) == FrameColour && Above(t)).ToList();
                int windows = mine.Count / FrameTris;
                Assert.True(windows >= 4, $"{id}: {windows} windows over the ground storey");
                Assert.Equal(Turned * windows, mine.Count(ToRooms));
                if (apart) Assert.All(mine, t => Assert.Equal(ToRooms(t), rooms[t]));
                else Assert.All(mine, t => Assert.False(rooms[t], $"{id}: a face with rooms the glass shows at any distance"));
            }
        }

        // the way out of the wall a point of the block's facade is on: its outline's nearest edge's normal, away from the middle
        static P3 Facing(BuildingData b, P3 p)
        {
            var fp = b.footprint; double best = double.MaxValue; var o = new P3(0, 0, 0);
            double mx = fp.Average(v => v.x), mz = fp.Average(v => v.z), px = p.x - b.pos.x, pz = p.z - b.pos.z;
            for (int i = 0; i < fp.Count; i++)
            {
                Vec2 a = fp[i], c = fp[(i + 1) % fp.Count];
                double ex = c.x - a.x, ez = c.z - a.z, l = Math.Sqrt(ex * ex + ez * ez);
                double t = Math.Clamp(((px - a.x) * ex + (pz - a.z) * ez) / (l * l), 0, 1);
                double dx = px - (a.x + ex * t), dz = pz - (a.z + ez * t), d = dx * dx + dz * dz;
                if (d >= best) continue;
                best = d; o = new P3(ez / l, 0, -ex / l);
                if (o.x * (a.x + ex / 2 - mx) + o.z * (a.z + ez / 2 - mz) < 0) o = new P3(-o.x, 0, -o.z);
            }
            return o;
        }

        [Fact]
        public void TheInspectorCountsItsVersionForInteriors()
        {
            // on a walk-in storey: the version for interiors as made, its glass both faces, no backs
            OpeningKinds.Register(WithInterior("t-in-check"));
            var c = OpeningKinds.Check(OpeningKinds.Get("t-in-check")!);
            Assert.Equal(FrameTris + SillTris + 2, c.tris);
            Assert.Equal(FrameTris + SillTris + 2 * 2, c.walkInTris);
        }

        [Fact]
        public void TheOpeningsKeepClearOfEitherVersion()
        {
            // its version for interiors reaching further round the hole than the window (a board 10 cm past each side, 5 cm
            // over it): what keeps clear of the window's dressing keeps clear of that too
            var k = WithInterior("t-in-reach");
            var m = k.inside!; var P = m.P.ToList(); var N = m.N.ToList(); int s = P.Count;
            P.AddRange(new[] { new P3(-0.6, -0.04, -0.31), new P3(0.6, -0.04, -0.31), new P3(0.6, 1.55, -0.31), new P3(-0.6, 1.55, -0.31) });
            N.AddRange(Enumerable.Repeat(new P3(0, 0, -1), 4));
            m.P = P.ToArray(); m.N = N.ToArray();
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Interior, tris = new[] { s, s + 2, s + 1, s, s + 3, s + 2 } });
            OpeningKinds.Register(k);
            Assert.Equal((0.1, 0.1, 0.05), (Math.Round(k.reachL, 6), Math.Round(k.reachR, 6), Math.Round(k.reachT, 6)));
            Assert.Equal(0.05, Math.Round(k.reachB, 6));   // the window's own sill, 5 cm under the hole, reaches lower than the board
            // and the window alone, as before
            var plain = OpeningBackTests.StreetOnly("t-in-reach-plain"); OpeningKinds.Register(plain);
            Assert.Equal((0.05, 0.05, 0.0), (Math.Round(plain.reachL, 6), Math.Round(plain.reachR, 6), Math.Round(plain.reachT, 6)));
        }

        [Fact]
        public void ItIsFlickerFree()
        {
            OpeningKinds.Register(WithInterior("t-in-cop"));
            var (b, site) = Block(true, "t-in-cop");
            var r = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
            Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(5)));
        }
    }
}
