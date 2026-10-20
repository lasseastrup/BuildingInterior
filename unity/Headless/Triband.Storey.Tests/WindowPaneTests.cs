using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What the window shader reads from the opaque panes (docs/EDITOR.md §6.8): each pane vertex's place on its pane and
    /// its window's number, the same window giving the same number in LOD0 and LOD1. Storey's own.
    /// </summary>
    public class WindowPaneTests
    {
        static (BuildingData b, Site site) Block(bool interior)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(3, -2);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.interior = interior;
            return (b, new Site(d.buildings));
        }

        [Fact]
        public void APanesPlaceAndNumberRoundTrip()
        {
            for (int i = 0; i <= 20; i++)
                foreach (bool flat in new[] { false, true })
                    foreach (bool bar in new[] { false, true })
                    {
                        double u = i / 20.0, v = 1 - i / 20.0;
                        var (a, nw, id, ny) = ColorRows.EncodePane(u, v, (byte)(i * 12), flat, bar);
                        Assert.True(Math.Abs((int)nw) >= 1, "a pane vertex must be told from the rest");
                        Assert.Equal(flat, nw < 0);   // LOD1's flat pane: negative (the shader draws its recess)
                        Assert.Equal(flat && bar ? 1 : 0, (int)ny);   // its upright normal's up byte: the centre bar
                        var (du, dv, did, dflat, dbar) = ColorRows.DecodePane(a, nw, id, ny);
                        Assert.True(Math.Abs(du - u) <= 0.5 / 255 + 1e-9 && Math.Abs(dv - v) <= 0.5 / 126 + 1e-9);
                        Assert.Equal((i * 12, flat, flat && bar), (did, dflat, dbar));
                    }
        }

        [Fact]
        public void TheNumberStaysPutWithinItsCell()
        {
            // a window centre, nudged a few centimetres within its 25 cm cell, keeps its number
            var c = new P3(4.25, 5.0, -2.75);
            byte n = Facade.WindowId(c);
            foreach (var d in new[] { -0.04, 0.04 })
                Assert.Equal(n, Facade.WindowId(new P3(c.x + d, c.y + d, c.z - d)));
            // and the numbers spread over the range
            var seen = new HashSet<byte>();
            for (int x = 0; x < 40; x++) for (int y = 0; y < 10; y++) seen.Add(Facade.WindowId(new P3(x * 1.3, y * 3.1, 0)));
            Assert.True(seen.Count > 150, $"only {seen.Count} numbers");
        }

        static List<(P3 p, P3 n, (double u, double v, byte id, bool flat, bool bar) w)> Panes(MeshBuilder m) =>
            m.PaneUV.Where(kv => m.C[kv.Key].slot == ColorSlot.Glass).Select(kv => (m.P[kv.Key], m.N[kv.Key], kv.Value)).ToList();

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EveryOpaquePaneRunsAcrossAndUp(bool interior)
        {
            var (b, site) = Block(interior);
            var r = Lod0.Build(site, b);
            var lod1 = Lod1.Build(site, b);
            foreach (var m in new[] { r.Op, lod1 })
            {
                var ps = Panes(m);
                // a walk-in storey's glass is see-through (the glass mesh); LOD1's is all opaque
                if (!interior || m == lod1) Assert.NotEmpty(ps);
                // every opaque glass vertex is a pane's
                for (int i = 0; i < m.Verts; i++) if (m.C[i].slot == ColorSlot.Glass) Assert.True(m.PaneUV.ContainsKey(i), "opaque glass without its place on the pane");
                foreach (var g in ps.GroupBy(x => x.w.id))
                    foreach (var a in g) foreach (var c in g)
                    {
                        if (Geo.Hypot(a.p.x - c.p.x, a.p.z - c.p.z) > 3 || Math.Abs(a.p.y - c.p.y) > 3 || a.n.x * c.n.x + a.n.z * c.n.z < 0.99) continue;
                        // u grows along cross(up, n) = (n.z, 0, −n.x), v with height
                        double along = (c.p.x - a.p.x) * a.n.z - (c.p.z - a.p.z) * a.n.x;
                        if (Math.Abs(along) > 0.05) Assert.True(Math.Sign(along) == Math.Sign(c.w.u - a.w.u), $"u runs backwards at {a.p}");
                        if (Math.Abs(c.p.y - a.p.y) > 0.05) Assert.True(Math.Sign(c.p.y - a.p.y) == Math.Sign(c.w.v - a.w.v));
                    }
            }
        }

        /// <summary>
        /// LOD1's plain windows are panes proud of a flat wall, marked so the shader draws the recess LOD0 cuts behind them;
        /// its doors are recessed in their openings as LOD0's are, and LOD0's panes and the prototype's recessed LOD1 have no
        /// flat pane.
        /// </summary>
        [Fact]
        public void OnlyLod1sPlainWindowsAreFlatPanes()
        {
            var (b, site) = Block(false);
            var m1 = Lod1.Build(site, b); var lod1 = Panes(m1);
            Assert.Contains(lod1, x => x.w.flat);
            Assert.All(lod1.Where(x => x.p.y > Derived.FloorBase(b, 1) - 0.01), x => Assert.True(x.w.flat, $"an upper storey's window at {x.p} isn't flat"));
            var door = m1.PaneUV.Where(kv => m1.C[kv.Key].slot == ColorSlot.Door).ToList();
            Assert.NotEmpty(door); Assert.All(door, kv => Assert.False(kv.Value.flat));   // the door's panel, in its recess
            Assert.DoesNotContain(Panes(Lod1.Build(site, b, recessed: true)), x => x.w.flat);
            Assert.DoesNotContain(Panes(Lod0.Build(site, b).Op), x => x.w.flat);
        }

        /// <summary>
        /// A flat pane is marked with LOD0's centre bar where LOD0's window has one (Facade.CentreBar: wider than 1.7 m, no
        /// glazing bars), and only then: the glass preset's curtain wall has them, its narrower windows and a style with
        /// glazing bars don't.
        /// </summary>
        [Fact]
        public void AFlatPaneHasTheCentreBarLod0sWindowHas()
        {
            var (b, site) = Block(false);
            b.style = Styles.Preset("glass");
            var l0 = Lod0.Build(site, b).Op; var l1 = Lod1.Build(site, b);
            // LOD0's bars: trim boxes 6 cm wide inside the openings, by their middles along each wall
            var bars = Enumerable.Range(0, l0.Tris).Where(t => l0.C[l0.I[3 * t]].slot == ColorSlot.Trim)
                .Select(t => l0.P[l0.I[3 * t]]).ToList();
            int with = 0, without = 0;
            for (int t = 0; t < l1.Tris; t++)
            {
                var vs = Enumerable.Range(0, 3).Select(k => l1.I[3 * t + k]).ToList();
                if (!vs.All(i => l1.PaneUV.TryGetValue(i, out var w) && w.flat)) continue;
                var ws = vs.Select(i => l1.PaneUV[i]).ToList();
                Assert.True(ws.All(w => w.bar == ws[0].bar), "a pane's corners agree");
                // the window's width: along the wall over the change in u, from the corners that differ most in u
                var n = l1.N[vs[0]]; double tx = n.z, tz = -n.x;
                var (a, c) = (from i in vs from j in vs select (i, j)).OrderByDescending(q => Math.Abs(l1.PaneUV[q.i].u - l1.PaneUV[q.j].u)).First();
                double width = Math.Abs((l1.P[a].x - l1.P[c].x) * tx + (l1.P[a].z - l1.P[c].z) * tz) / Math.Abs(l1.PaneUV[a].u - l1.PaneUV[c].u);
                Assert.Equal(width > 1.7, ws[0].bar);
                if (ws[0].bar) with++; else without++;
            }
            Assert.True(with > 0 && without > 0, $"{with} triangles of panes with a bar, {without} without");
            b.style.paneCols = 2;   // glazing bars instead: no centre bar
            Assert.DoesNotContain(Panes(Lod1.Build(site, b)), x => x.w.bar);
        }

        [Fact]
        public void AWindowKeepsItsNumberFromLod0ToLod1()
        {
            var (b, site) = Block(false);
            var w0 = Panes(Lod0.Build(site, b).Op).Select(x => x.w.id).ToHashSet();
            var w1 = Panes(Lod1.Build(site, b)).Select(x => x.w.id).ToHashSet();
            Assert.True(w0.SetEquals(w1), $"LOD0 {w0.Count} numbers, LOD1 {w1.Count}, {w0.Intersect(w1).Count()} shared");
        }
    }
}
