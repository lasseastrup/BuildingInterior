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
    /// Fixed bays (<see cref="FacadeStyle.bayFit"/>, docs/EDITOR.md §6.7): as many whole bays of the style's width as fit on
    /// each wall's outer face, centred on it, a window centred in each, as the old BuildingGenerator lays out its tiles; the
    /// same in every LOD and wherever openings are laid out. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class BayFitTests
    {
        const double T = Dim.T_EXT;

        /// <summary>A plain keep-size or stretched window <paramref name="w"/> × <paramref name="h"/>: a pane in its hole.</summary>
        static OpeningKind Kind(string id, double w, double h, bool keep)
        {
            var P = new[] { new P3(-w / 2, 0, -0.05), new P3(w / 2, 0, -0.05), new P3(w / 2, h, -0.05), new P3(-w / 2, h, -0.05) };
            var m = new OpeningKind.Mesh { P = P, N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            return new OpeningKind { id = id, name = id, w = w, h = h, stretch = !keep, lod0 = m };
        }

        static void With(OpeningKind k, Action body)
        {
            OpeningKinds.Register(k);
            try { body(); } finally { OpeningKinds.Remove(k.id); }
        }

        /// <summary>The 14 × 10 m rectangle.</summary>
        static readonly List<Vec2> Rect = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };

        /// <summary>An L with a concave corner (at 8, 6) and two corners that aren't right angles (either end of the slanted edge).</summary>
        static readonly List<Vec2> Ell = new() { new Vec2(0, 0), new Vec2(16, 0), new Vec2(16, 6), new Vec2(8, 6), new Vec2(5, 12), new Vec2(0, 12) };

        /// <summary>A brick shell of four storeys on <paramref name="fp"/>, its door on edge 0, bays of 2 m and windows 1.2 m wide.</summary>
        static (BuildingData b, Site s, StoreyDocument d) Block(List<Vec2> fp, BayFit fit, Action<FacadeStyle>? f = null)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new List<Vec2>(fp);
            Floors.SetCount(b, 4);
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.interior = false; b.style = Styles.Preset("brick"); b.style.ground = GroundType.Match;
            b.style.bay = 2.0; b.style.winW = 1.2; b.style.bayFit = fit; b.style.details = new DetailRules { ac = 0, vents = 0 };
            f?.Invoke(b.style);
            return (b, new Site(d.buildings), d);
        }

        static List<Opening> Ops(Site s, BuildingData b, int k, int i) { var f = Details.EdgeFrameAt(b, k, i); return Facade.Ops(s, b, k, i, f.e.L, f.uS, f.uE); }

        /// <summary>
        /// Edge i's outer face, worked out here on its own: each end on its corner's bisector, the wall's thickness out,
        /// from the outward normals (found by stepping off the edge's middle) of the edge and its neighbours.
        /// </summary>
        static (double s, double e) Outer(List<Vec2> fp, int i)
        {
            int n = fp.Count;
            (double ux, double uz, double nx, double nz) Edge(int j)
            {
                var a = fp[(j + n) % n]; var c = fp[(j + 1) % n]; double L = Math.Sqrt((c.x - a.x) * (c.x - a.x) + (c.z - a.z) * (c.z - a.z));
                double ux = (c.x - a.x) / L, uz = (c.z - a.z) / L, mx = (a.x + c.x) / 2, mz = (a.z + c.z) / 2;
                return Geo.Pip(fp, mx + uz * 0.01, mz - ux * 0.01) ? (ux, uz, -uz, ux) : (ux, uz, uz, -ux);
            }
            var p = Edge(i - 1); var e = Edge(i); var q = Edge(i + 1);
            double len = Math.Sqrt((fp[(i + 1) % n].x - fp[i].x) * (fp[(i + 1) % n].x - fp[i].x) + (fp[(i + 1) % n].z - fp[i].z) * (fp[(i + 1) % n].z - fp[i].z));
            double s = T * (p.nx * e.ux + p.nz * e.uz) / (1 + p.nx * e.nx + p.nz * e.nz);
            double en = len + T * (q.nx * e.ux + q.nz * e.uz) / (1 + q.nx * e.nx + q.nz * e.nz);
            return (s, en);
        }

        [Fact]
        public void SpreadIsTheDefaultAndIsNotWritten()
        {
            Assert.Equal(BayFit.Spread, new FacadeStyle().bayFit);
            Assert.Equal(BayFit.Spread, Styles.Preset("brick").bayFit);
            Assert.DoesNotContain("bayFit", PrototypeJson.WriteStyle(Styles.Preset("brick")));
            // spread bays stretch to fill the span: on the 14 m wall, seven of 1.96 m, not seven of 2 m
            var (b, s, _) = Block(Rect, BayFit.Spread);
            var w = Ops(s, b, 1, 0).Where(o => !o.door).Select(o => (o.u0 + o.u1) / 2).ToList();
            Assert.Equal(7, w.Count);
            Assert.Equal(1.96, w[1] - w[0], 9);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void FixedBaysAreAsManyAsFitOnTheOuterFaceCentredOnIt(int shape)
        {
            var fp = shape == 0 ? Rect : Ell;
            var (b, s, _) = Block(fp, BayFit.Fixed);
            for (int k = 1; k < b.floors.Count; k++)
                for (int i = 0; i < fp.Count; i++)
                {
                    var (oS, oE) = Outer(fp, i);
                    var mine = Facade.OuterFace(b, k, i);
                    Assert.Equal(oS, mine.s, 9); Assert.Equal(oE, mine.e, 9);
                    int n = (int)Math.Floor((oE - oS) / 2.0 + 1e-6);
                    var f = Details.EdgeFrameAt(b, k, i);
                    var w = Ops(s, b, k, i).Where(o => !o.door).ToList();
                    Assert.Equal(n, w.Count);
                    for (int j = 0; j < n; j++)
                    {
                        // centred in its bay, or moved in to keep the span's 14 cm from the inner corner
                        double c = (oS + oE) / 2 + (j - (n - 1) / 2.0) * 2.0, want = Math.Max(f.uS + 0.6, Math.Min(f.uE - 0.6, c));
                        Assert.Equal(1.2, w[j].u1 - w[j].u0, 9);
                        Assert.Equal(want, (w[j].u0 + w[j].u1) / 2, 9);
                        Assert.InRange(w[j].u0, oS, oE); Assert.InRange(w[j].u1, oS, oE);
                    }
                }
            // the rectangle's walls have them all where the old generator would: none near enough a corner to move
            if (shape == 0)
            {
                var w = Ops(s, b, 1, 0).Select(o => (o.u0 + o.u1) / 2).ToList();
                Assert.Equal(new[] { 1.0, 3.0, 5.0, 7.0, 9.0, 11.0, 13.0 }, w.Select(x => Math.Round(x, 9)));
            }
        }

        [Fact]
        public void AKeepSizeWindowWiderThanTheBayLessHalfAMetreKeepsItsWidth()
        {
            With(Kind("t-bay-wide", 1.7, 1.3, keep: true), () =>
            {
                var (b, s, _) = Block(Rect, BayFit.Fixed, st => st.windowKind = "t-bay-wide");
                var w = Enumerable.Range(1, 3).SelectMany(k => Enumerable.Range(0, 4).SelectMany(i => Ops(s, b, k, i))).Where(o => !o.door).ToList();
                Assert.Equal(3 * (7 + 5 + 7 + 5), w.Count);
                Assert.All(w, o => Assert.Equal(1.7, o.u1 - o.u0, 9));
                // spread, it is fitted to the bay less half a metre as ever
                var (bs, ss, _) = Block(Rect, BayFit.Spread, st => st.windowKind = "t-bay-wide");
                Assert.All(Ops(ss, bs, 1, 0).Where(o => !o.door), o => Assert.Equal(1.96 - 0.5, o.u1 - o.u0, 9));
                // LOD2 paints it its own width too: the bay less a 30 cm gap
                var spec = Lod2.SpecOf(Facade.WindowSpec(b.style.windows, Derived.FloorH(b, 1), b.style), b.style, false);
                Assert.Equal(2.0 - 1.7, spec[0], 9);
                Assert.Equal(-1, Lod2.SpecOf(Facade.WindowSpec(bs.style.windows, Derived.FloorH(bs, 1), bs.style), bs.style, false)[0]);
                Assert.Empty(CoplanarCheck.Run(Lod0.Build(s, b, solids: true).Op).Overlaps);
            });
        }

        [Fact]
        public void WindowsABayWideStandInARowAsRibbonsDo()
        {
            // the old generator's skyscraper: windows 1.40 m wide in bays of 1.43 m, 3 cm of wall between them
            With(Kind("t-ribbon", 1.40, 0.84, keep: true), () =>
            {
                var (b, s, _) = Block(Rect, BayFit.Fixed, st => { st.bay = 1.43; st.windowKind = "t-ribbon"; });
                var (os, oe) = Outer(Rect, 1);
                int n = (int)Math.Floor((oe - os) / 1.43 + 1e-6);
                var ops = Ops(s, b, 2, 1).Where(o => !o.door).OrderBy(o => o.u0).ToList();
                Assert.Equal(n, ops.Count);   // every bay has its window: none dropped for standing close to the one before
                for (int j = 1; j < ops.Count; j++) Assert.InRange(ops[j].u0 - ops[j - 1].u1, 0.01, 0.05);
                // the 14 m wall's outer face holds ten bays with 15 cm to spare at either end: on a shell storey, whose wall is
                // drawn by its outer face, the end windows stay where their bays put them, into the corner past the inner face
                var (os2, oe2) = Outer(Rect, 2);
                int n2 = (int)Math.Floor((oe2 - os2) / 1.43 + 1e-6);
                var ops2 = Ops(s, b, 2, 2).Where(o => !o.door).OrderBy(o => o.u0).ToList();
                Assert.Equal(n2, ops2.Count);
                Assert.True(ops2[0].u0 < 0 && ops2[0].u0 >= os2 + Facade.FixedCornerClear - 1e-9, $"the first window starts at {ops2[0].u0:0.###}");
                for (int j = 1; j < ops2.Count; j++) Assert.True(ops2[j].u0 - ops2[j - 1].u1 >= 0.01 - 1e-9);
                Assert.Empty(CoplanarCheck.Run(Lod0.Build(s, b).Op).Overlaps);
                Assert.Empty(CoplanarCheck.Run(Lod1.Build(s, b)).Overlaps);
            });
        }

        [Fact]
        public void TheDetailsAndLodZeroHaveTheWindowsInTheSamePlace()
        {
            var (b, s, _) = Block(Ell, BayFit.Fixed, st => st.details = new DetailRules { ac = 100, vents = 100 });
            var op = Lod0.Build(s, b).Op;
            var glass = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Glass).Select(v => op.P[v]).ToList();
            int acs = 0, vents = 0;
            for (int k = 1; k < b.floors.Count; k++)
                foreach (var it in Details.At(s, b, k))
                {
                    var ops = Ops(s, b, k, it.i).Where(o => !o.door).ToList();
                    if (it.kind.id == DetailKinds.Vent) { vents++; Assert.DoesNotContain(ops, o => o.u1 > it.u - 0.25 && o.u0 < it.u + 0.25); continue; }
                    if (it.kind.id != DetailKinds.Ac) continue;
                    acs++;
                    // under a window LOD0 draws: its pane's corners where the window's are, on the wall's outside
                    var o = ops.Single(o => Math.Abs((o.u0 + o.u1) / 2 - it.u) < 1e-9);
                    var e = Geo.EdgeInfo(b.footprint, it.i, Geo.Area2(b.footprint) > 0);
                    double y0 = Derived.FloorBase(b, k) + o.y0;
                    foreach (var u in new[] { o.u0, o.u1 })
                        Assert.Contains(glass, p => Math.Abs((p.x - e.a.x) * e.u.x + (p.z - e.a.z) * e.u.z - u) < 1e-6 && Math.Abs(p.y - y0) < 1e-6);
                }
            Assert.True(acs > 10);
        }

        [Fact]
        public void LodTwoPaintsTheFixedBays()
        {
            var (b, s, _) = Block(Ell, BayFit.Fixed);
            var m = Lod2.Build(s, b);
            var spans = new HashSet<(double, double, double)>();
            for (int v = 0; v < m.P.Count; v++)
                if (m.Fac2[2 * v + 1] == 1 && m.Fac2[2 * v] > 0) spans.Add((Math.Round(m.Fac2[2 * v], 6), Math.Round(m.Fac[4 * v + 2], 6), Math.Round(m.Fac[4 * v + 3], 6)));
            var want = new HashSet<(double, double, double)>();
            for (int i = 0; i < Ell.Count; i++)
            {
                var (oS, oE) = Outer(Ell, i); int n = (int)Math.Floor((oE - oS) / 2.0 + 1e-6); double c = (oS + oE) / 2;
                want.Add((2.0, Math.Round(c - n, 6), Math.Round(c + n, 6)));
            }
            Assert.Equal(want, spans);
            // every window the geometry LODs lay out on an upper storey is in one of the painted bays, centred
            for (int i = 0; i < Ell.Count; i++)
            {
                var (oS, oE) = Outer(Ell, i); int n = (int)Math.Floor((oE - oS) / 2.0 + 1e-6); double bS = (oS + oE) / 2 - n;
                foreach (var o in Ops(s, b, 2, i).Where(o => !o.door))
                {
                    double c = (o.u0 + o.u1) / 2, j = Math.Floor((c - bS) / 2.0);
                    Assert.InRange(j, 0, n - 1);
                    Assert.InRange(c - (bS + (j + 0.5) * 2.0), -0.15, 0.15);   // moved in by no more than a corner asks
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void FixedBaysBuildCleanly(int shape)
        {
            var (b, s, _) = Block(shape == 0 ? Rect : Ell, BayFit.Fixed, st => st.bay = 1.43);
            Assert.Empty(CoplanarCheck.Run(Lod0.Build(s, b, solids: true).Op).Overlaps);
            Assert.Empty(CoplanarCheck.Run(Lod1.Build(s, b, solids: true)).Overlaps);
        }

        [Fact]
        public void ACourtyardsWallsHaveFixedBaysOnTheirFaceIntoTheCourt()
        {
            var (b, s, _) = Block(new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 16), new Vec2(0, 16) }, BayFit.Fixed);
            var v = new VoidData { id = Voids.NewId(b), kind = VoidKind.Courtyard, shape = new() { new Vec2(6, 5), new Vec2(14, 5), new Vec2(14, 11), new Vec2(6, 11) } };
            b.voids.Add(v);
            bool flag = !(Geo.Area2(v.shape) > 0); var cor = Geo.Corners(v.shape, flag);
            for (int i = 0; i < 4; i++)
            {
                // the court's corners are all set in from its side: its walls' faces are the wall's thickness short each end
                var e = Geo.EdgeInfo(v.shape, i, flag); var (uS, uE) = Facade.Span(Geo.MiterOf(cor, i, e.L), e.L);
                int n = (int)Math.Floor((e.L - 2 * T) / 2.0 + 1e-6);
                var w = Courtyards.Ops(b, v, 2, i, e.L, uS, uE).Where(o => !o.door).Select(o => (o.u0 + o.u1) / 2).ToList();
                Assert.Equal(Enumerable.Range(0, n).Select(j => Math.Round(e.L / 2 + (j - (n - 1) / 2.0) * 2.0, 9)), w.Select(x => Math.Round(x, 9)));
            }
            Assert.Empty(CoplanarCheck.Run(Lod0.Build(s, b, solids: true).Op).Overlaps);
        }

        [Fact]
        public void FixedBaysComeBackFromJsonAndTheBakedLayout()
        {
            var (b, _, d) = Block(Ell, BayFit.Fixed);
            var json = PrototypeJson.WriteStyle(b.style);
            Assert.Contains("\"bayFit\":\"fixed\"", json);
            Assert.Equal(BayFit.Fixed, PrototypeJson.ReadStyle(json).bayFit);
            Assert.Equal(BayFit.Fixed, PrototypeJson.Read(PrototypeJson.Write(d)).Document.buildings[0].style.bayFit);
            Assert.Equal(BayFit.Fixed, BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style.bayFit);
        }
    }
}
