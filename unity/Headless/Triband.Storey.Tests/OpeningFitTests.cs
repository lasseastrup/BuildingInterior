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
    /// An artist's window that decides its hole, and the dressing round it kept clear (docs/OPENINGS.md §4.1, §4.2):
    /// Keep size makes the hole the window's own size in every LOD, and corners, doors, bands, plinths, facade details,
    /// brick patches and the next window keep clear of its shutters, sill and pediment. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class OpeningFitTests
    {
        /// <summary>
        /// A window <paramref name="w"/> × <paramref name="h"/>: a pane in the hole, and quads out of the wall reaching
        /// <paramref name="l"/> left and <paramref name="r"/> right of it (shutters), <paramref name="below"/> under it (a
        /// sill or a flower box) and <paramref name="above"/> over it (a pediment). Front faces out.
        /// </summary>
        static OpeningKind Dressed(string id, double w, double h, double l = 0, double r = 0, double below = 0, double above = 0, bool keep = false, bool door = false)
        {
            var P = new List<P3>(); var N = new List<P3>(); var pane = new List<int>(); var dress = new List<int>();
            void Q(double x0, double y0, double x1, double y1, double z, List<int> into)
            {
                int s = P.Count;
                P.AddRange(new[] { new P3(x0, y0, z), new P3(x1, y0, z), new P3(x1, y1, z), new P3(x0, y1, z) }); N.AddRange(Enumerable.Repeat(new P3(0, 0, 1), 4));
                into.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Q(-w / 2, 0, w / 2, h, -0.05, pane);
            if (l > 0) Q(-w / 2 - l, 0, -w / 2 - 0.02, h, 0.03, dress);
            if (r > 0) Q(w / 2 + 0.02, 0, w / 2 + r, h, 0.03, dress);
            if (below > 0) Q(-w / 2, -below, w / 2, -0.01, 0.05, dress);
            if (above > 0) Q(-w / 2, h + 0.01, w / 2, h + above, 0.05, dress);
            var m = new OpeningKind.Mesh { P = P.ToArray(), N = N.ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = door ? ColorSlot.Door : ColorSlot.Glass, tris = pane.ToArray() });
            if (dress.Count > 0) m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = dress.ToArray() });
            return new OpeningKind { id = id, name = id, door = door, w = w, h = h, stretch = !keep, lod0 = m };
        }

        static (BuildingData b, Site s) Block(Action<FacadeStyle>? f = null, int floors = 3)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            Floors.SetCount(b, floors);
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.interior = false; b.style = Styles.Preset("brick"); b.style.ground = GroundType.Match;
            f?.Invoke(b.style);
            return (b, new Site(d.buildings));
        }

        static IEnumerable<(int k, int i, Details.EdgeFrame f, List<Opening> ops)> Walls(BuildingData b, Site s)
        {
            for (int k = 0; k < b.floors.Count; k++)
                for (int i = 0; i < Derived.OutlineAt(b, k).Count; i++)
                {
                    var f = Details.EdgeFrameAt(b, k, i);
                    yield return (k, i, f, Facade.Ops(s, b, k, i, f.e.L, f.uS, f.uE));
                }
        }

        static void With(OpeningKind k, Action body)
        {
            OpeningKinds.Register(k);
            try { body(); } finally { OpeningKinds.Remove(k.id); }
        }

        [Fact]
        public void KeepSizeMakesTheHoleTheWindowsOwnInEveryLod()
        {
            With(Dressed("t-fit-keep", 0.9, 1.3, keep: true), () =>
            {
                var (b, s) = Block(st => st.windowKind = "t-fit-keep");
                int windows = 0;
                foreach (var (k, _, _, ops) in Walls(b, s))
                    foreach (var o in ops.Where(o => !o.door))
                    {
                        windows++;
                        Assert.InRange(o.u1 - o.u0, 0.9 - 1e-9, 0.9 + 1e-9);
                        Assert.InRange(o.y1 - o.y0, 1.3 - 1e-9, 1.3 + 1e-9);
                    }
                Assert.True(windows > 10);
                // LOD2 paints the same windows: a fixed width, the same sill and head
                var spec = Lod2.SpecOf(Facade.WindowSpec(b.style.windows, Derived.FloorH(b, 1), b.style));
                Assert.Equal(-1, spec[0]); Assert.Equal(0.9, spec[1], 9); Assert.Equal(1.3, spec[3] - spec[2], 9);
                // and the placed panes are the window's own height on every storey, filling its hole
                var op = Lod0.Build(s, b).Op;
                for (int k = 0; k < b.floors.Count; k++)
                {
                    double f0 = Derived.FloorBase(b, k), f1 = f0 + Derived.FloorH(b, k);
                    var ys = Enumerable.Range(0, op.Verts).Where(i => op.C[i].slot == ColorSlot.Glass && op.P[i].y > f0 && op.P[i].y < f1).Select(i => op.P[i].y).ToList();
                    Assert.NotEmpty(ys);
                    Assert.InRange(ys.Max() - ys.Min(), 1.3 - 1e-6, 1.3 + 1e-6);
                }
            });
        }

        [Fact]
        public void AWindowOfItsOwnSizeSitsAtTheStylesSillHeight()
        {
            With(Dressed("t-fit-sill", 0.9, 1.3, keep: true), () =>
            {
                // without a sill height, as high as its type's windows (Punched: 90 cm); with one, that high, in every LOD
                foreach (var (sill, want) in new[] { ((double?)null, 0.9), (1.2, 1.2), (0.3, 0.3) })
                {
                    var (b, s) = Block(st => { st.windowKind = "t-fit-sill"; st.sillH = sill; });
                    int windows = 0;
                    foreach (var (k, _, _, ops) in Walls(b, s))
                        foreach (var o in ops.Where(o => !o.door))
                        {
                            windows++;
                            Assert.InRange(o.y0, want - 1e-9, want + 1e-9);
                            Assert.InRange(o.y1 - o.y0, 1.3 - 1e-9, 1.3 + 1e-9);
                        }
                    Assert.True(windows > 10, $"{windows} windows");
                    var spec = Lod2.SpecOf(Facade.WindowSpec(b.style.windows, Derived.FloorH(b, 1), b.style));
                    Assert.Equal(want, spec[2], 9); Assert.Equal(want + 1.3, spec[3], 9);
                }
                // over a plinth, the ground floor's stay 15 cm above it
                var (bp, sp) = Block(st => { st.windowKind = "t-fit-sill"; st.sillH = 0.3; st.plinthH = 0.8; });
                foreach (var (k, _, _, ops) in Walls(bp, sp))
                    foreach (var o in ops.Where(o => !o.door)) Assert.InRange(o.y0, (k == 0 ? 0.95 : 0.3) - 1e-9, (k == 0 ? 0.95 : 0.3) + 1e-9);
            });
        }

        [Fact]
        public void OnlyAWindowOfItsOwnSizeTakesTheSillHeight()
        {
            // the generator's own windows, and an artist's fitted to the openings, take their type's sill whatever the style says
            With(Dressed("t-fit-sill-stretch", 1.0, 1.5), () =>
            {
                foreach (var kind in new[] { null, "t-fit-sill-stretch" })
                {
                    var (b, s) = Block(st => { st.windowKind = kind; st.sillH = 1.5; });
                    var ys = Walls(b, s).SelectMany(w => w.ops).Where(o => !o.door).Select(o => o.y0).ToList();
                    Assert.NotEmpty(ys);
                    Assert.All(ys, y => Assert.InRange(y, 0.9 - 1e-9, 0.9 + 1e-9));
                }
            });
        }

        [Fact]
        public void ASillTooHighForItsStoreyLeavesNoWindowsAtAnyLod()
        {
            With(Dressed("t-fit-sill-high", 0.9, 1.3, keep: true), () =>
            {
                // squeezed under the storey's top into a hole 20 cm high: none in LOD0 and LOD1, and none painted in LOD2,
                // which painted a 20 cm strip
                var (b0, _) = Block(); double h = Derived.FloorH(b0, 1);
                var (b, s) = Block(st => { st.windowKind = "t-fit-sill-high"; st.sillH = h - 0.3; });
                Assert.Null(Facade.WindowSpec(b.style.windows, h, b.style));
                Assert.DoesNotContain(Walls(b, s).Where(w => w.k > 0 && Math.Abs(Derived.FloorH(b, w.k) - h) < 1e-9).SelectMany(w => w.ops), o => !o.door);
                var spec = Lod2.SpecOf(Facade.WindowSpec(b.style.windows, h, b.style));
                Assert.True(spec[3] <= spec[2], "LOD2 paints a window");
            });
        }

        [Fact]
        public void TheSillHeightIsKeptInTheLayout()
        {
            var st = Styles.Preset("brick"); st.sillH = 1.15;
            var json = PrototypeJson.WriteStyle(st);
            Assert.Contains("\"sillH\"", json);
            Assert.Equal(1.15, PrototypeJson.ReadStyle(json).sillH);
            Assert.DoesNotContain("\"sillH\"", PrototypeJson.WriteStyle(Styles.Preset("brick")));   // none set, none written
        }

        [Fact]
        public void StretchedWindowsKeepTheStylesHoles()
        {
            // without Keep size, or without dressing, an artist's window leaves the openings as the generator's are
            With(Dressed("t-fit-plain", 1.0, 1.5), () =>
            {
                var (b0, s0) = Block();
                var plain = Walls(b0, s0).SelectMany(w => w.ops).Select(o => (o.u0, o.u1, o.y0, o.y1)).ToList();
                var (b, s) = Block(st => st.windowKind = "t-fit-plain");
                Assert.Equal(plain, Walls(b, s).SelectMany(w => w.ops).Select(o => (o.u0, o.u1, o.y0, o.y1)).ToList());
            });
        }

        [Fact]
        public void ShuttersKeepClearOfTheCorners()
        {
            With(Dressed("t-fit-shutters", 1.2, 1.4, l: 0.45, r: 0.45), () =>
            {
                var (b, s) = Block(st => st.windowKind = "t-fit-shutters");
                foreach (var (k, i, f, ops) in Walls(b, s))
                    foreach (var o in ops.Where(o => !o.door))
                    {
                        Assert.InRange(o.rl, 0.45 - 1e-9, 0.45 + 1e-9);
                        Assert.True(o.Left >= f.uS - 1e-9 && o.Right <= f.uE + 1e-9, $"storey {k}, wall {i}: a shutter past the span ({o.Left:0.00}..{o.Right:0.00} of {f.uS:0.00}..{f.uE:0.00})");
                    }
                // LOD2 lays its windows out over the same narrower span
                var (wS, wE) = Facade.WindowSpan(OpeningKinds.Get("t-fit-shutters"), 0.14, 9.86);
                Assert.Equal(0.59, wS, 9); Assert.Equal(9.41, wE, 9);
            });
        }

        [Fact]
        public void TheDressingStaysUnderTheBandAndOverThePlinth()
        {
            With(Dressed("t-fit-tall", 1.0, 1.8, below: 0.12, above: 0.35, keep: true), () =>
            {
                var (b, s) = Block(st => { st.windowKind = "t-fit-tall"; st.bandH = 0.4; st.bands = true; st.plinthH = 0.8; });
                foreach (var (k, _, _, ops) in Walls(b, s))
                {
                    double h = Derived.FloorH(b, k);
                    foreach (var o in ops.Where(o => !o.door))
                    {
                        Assert.True(o.Top <= h - 0.4 - 0.05 + 1e-9, $"storey {k}: the pediment reaches {o.Top:0.00} into a band from {h - 0.4:0.00}");
                        Assert.True(o.Bottom >= (k == 0 ? 0.8 + 0.05 : 0.05) - 1e-9, $"storey {k}: the sill reaches down to {o.Bottom:0.00}");
                    }
                }
            });
        }

        [Fact]
        public void AcUnitsAndVentsKeepClearOfTheDressing()
        {
            // shutters 30 cm each side and a 2 cm sill, on bays wide enough for a vent between two windows' shutters; an AC unit
            // goes under the sill (a punched window's at 0.9 m leaves room for one below a 3 cm sill at most)
            With(Dressed("t-fit-flowers", 1.2, 1.4, l: 0.3, r: 0.3, below: 0.02), () =>
            {
                var (b, s) = Block(st => { st.windowKind = "t-fit-flowers"; st.bay = 4.0; st.details = new DetailRules { ac = 100, vents = 100 }; }, floors: 4);
                int acs = 0, vents = 0;
                for (int k = 1; k < b.floors.Count; k++)
                {
                    var walls = Walls(b, s).Where(w => w.k == k).ToDictionary(w => w.i, w => w.ops);
                    foreach (var d in Details.At(s, b, k))
                    {
                        var ops = walls[d.i].Where(o => !o.door).ToList();
                        if (d.kind.id == DetailKinds.Ac)
                        {
                            acs++;
                            var o = ops.OrderBy(o => Math.Abs((o.u0 + o.u1) / 2 - d.u)).First();
                            Assert.True(d.y + 0.55 <= o.Bottom - 0.07 + 1e-9, $"an AC unit's top at {d.y + 0.55:0.00} reaches the flower box at {o.Bottom:0.00}");
                        }
                        else if (d.kind.id == DetailKinds.Vent)
                        {
                            vents++;
                            Assert.DoesNotContain(ops, o => d.u + 0.25 > o.Left && d.u - 0.25 < o.Right);
                        }
                    }
                }
                Assert.True(acs > 0, "AC units go under windows with a sill, lower down");
                Assert.True(vents > 0, "vents go between the windows' shutters");
            });
        }

        [Fact]
        public void BrickPatchesKeepClearOfTheShutters()
        {
            var ops = new List<Opening> { new Opening().Set(3, 4.2, 0.9, 2.3, rl: 0.45, rr: 0.45, rb: 0.1) };
            var op = new MeshBuilder();
            var F = new Frame(new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, -1));
            Facade.Bricks(op, F, 0, 8, 0, 3, 0.45, 0, ops, 1, 12345, new Swatch(new StyleRef(0, 0), ColorSlot.Wall, 0.9));
            Assert.True(op.Verts > 0);
            for (int i = 0; i < op.Verts; i++)
            {
                var p = op.P[i];
                Assert.False(p.x > ops[0].Left - 0.12 + 1e-6 && p.x < ops[0].Right + 0.12 - 1e-6 && p.y > ops[0].Bottom - 0.15 + 1e-6 && p.y < ops[0].Top + 0.45 - 1e-6, $"a brick at {p} on the shutters");
            }
        }

        [Fact]
        public void ADoorsDressingKeepsWindowsAway()
        {
            With(Dressed("t-fit-door", 1.6, 2.4, l: 0.6, r: 0.6, door: true), () =>
            {
                var (b, s) = Block(st => st.doorKind = "t-fit-door");
                var ops = Walls(b, s).First(w => w.k == 0 && w.i == 0).ops;
                var door = ops.Single(o => o.door);
                Assert.Equal(0.6, door.rl, 9);
                Assert.All(ops.Where(o => !o.door), o => Assert.True(o.u1 <= door.Left - 0.1 + 1e-9 || o.u0 >= door.Right + 0.1 - 1e-9, "a window in the door's dressing"));
            });
        }

        [Fact]
        public void BaysTooCloseForTheDressingAreAProblem()
        {
            // shutters 0.8 m each side of a 1.2 m window need bays of 2.9 m; the brick preset's are 2.6
            With(Dressed("t-fit-wide", 1.2, 1.4, l: 0.8, r: 0.8, keep: true), () =>
            {
                var (b, s) = Block(st => st.windowKind = "t-fit-wide");
                var p = Problems.Check(s).Where(x => x.code == "dressing").ToList();
                Assert.Single(p);
                Assert.Contains("t-fit-wide", p[0].message);
            });
            With(Dressed("t-fit-narrow", 1.2, 1.4, l: 0.3, r: 0.3, keep: true), () =>
            {
                var (b, s) = Block(st => st.windowKind = "t-fit-narrow");
                Assert.DoesNotContain(Problems.Check(s), x => x.code == "dressing");
            });
        }
    }
}
