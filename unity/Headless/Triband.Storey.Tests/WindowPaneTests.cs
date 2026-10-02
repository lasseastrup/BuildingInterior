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
            {
                double u = i / 20.0, v = 1 - i / 20.0;
                var (a, nw, id) = ColorRows.EncodePane(u, v, (byte)(i * 12));
                Assert.True(nw >= 1, "a pane vertex must be told from the rest");
                var (du, dv, did) = ColorRows.DecodePane(a, nw, id);
                Assert.True(Math.Abs(du - u) <= 0.5 / 255 + 1e-9 && Math.Abs(dv - v) <= 0.5 / 126 + 1e-9);
                Assert.Equal(i * 12, did);
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

        static List<(P3 p, P3 n, (double u, double v, byte id) w)> Panes(MeshBuilder m) =>
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
