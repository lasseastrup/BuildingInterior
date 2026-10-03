using System;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The facade details from the artist's brick building: arched heads, pane grids, frames, bands, glazed doors, plinth,
    /// foundation and brick patches (<see cref="FacadeStyle"/>). Storey's own; a style without them builds as the prototype's.
    /// </summary>
    public class FacadeDetailTests
    {
        static FacadeStyle Artist()
        {
            var st = Styles.Preset("brick").Clone();
            st.head = HeadType.Arch; st.paneCols = 2; st.paneRows = 3; st.frames = true; st.bandH = 0.65; st.bandDepth = 0.02; st.bandWall = true;
            st.doorType = DoorType.Glazed; st.plinthH = 0.8; st.foundation = 0.5; st.foundationH = 0.2; st.bricks = 0.6; st.ground = GroundType.Match;
            return st;
        }

        static (StoreyDocument d, BuildingData b, Site site) Block(FacadeStyle st, bool interior = true)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.style = st; b.interior = interior;
            return (d, b, new Site(d.buildings));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ItBuildsCleanly(bool interior)
        {
            var (_, b, site) = Block(Artist(), interior);
            var r = Lod0.Build(site, b, solids: true);
            var rep = CoplanarCheck.Run(r.Op);
            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
            var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true));
            Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(10)));
        }

        [Fact]
        public void EachDetailAddsGeometryAndColour()
        {
            var plain = Styles.Preset("brick").Clone(); plain.ground = GroundType.Match;
            var (_, b0, s0) = Block(plain); int t0 = Lod0.Build(s0, b0).Op.Tris;
            foreach (Action<FacadeStyle> f in new Action<FacadeStyle>[] { s => s.head = HeadType.Arch, s => { s.paneCols = 2; s.paneRows = 3; }, s => s.frames = true, s => s.doorType = DoorType.Glazed, s => s.foundation = 0.5, s => s.bricks = 0.5 })
            {
                var st = plain.Clone(); f(st);
                var (_, b, s) = Block(st);
                Assert.True(Lod0.Build(s, b).Op.Tris > t0, "a detail added nothing");
            }
            var (_, ba, sa) = Block(Artist());
            var slots = Lod0.Build(sa, ba).Op.C.Select(c => c.slot).ToHashSet();
            Assert.Contains(ColorSlot.Frame, slots); Assert.Contains(ColorSlot.Foundation, slots);
        }

        [Fact]
        public void WindowsKeepClearOfABandAndAPlinth()
        {
            var st = Artist(); st.windows = WindowType.Tall;
            double h = 3.0;
            var ws = Facade.WindowSpec(st.windows, h, st)!;
            Assert.True(ws.y1 <= h - 0.65 - 0.3 + 1e-9, $"head at {ws.y1}");
            var gs = Facade.GroundSpec(st, 3.6)!;
            Assert.True(gs.y0 >= 0.8 + 0.15 - 1e-9, $"sill at {gs.y0}");
            // the prototype's styles keep their windows
            var p = Styles.Preset("brick");
            Assert.Equal(Math.Min(h - 0.55, 2.25), Facade.WindowSpec(p.windows, h, p)!.y1, 9);
        }

        [Fact]
        public void TheFoundationGoesAsDeepAsAsked()
        {
            var st = Artist(); st.foundation = 1.2; st.foundationH = 0.3;
            var (_, b, s) = Block(st);
            var op = Lod0.Build(s, b).Op;
            var ys = Enumerable.Range(0, op.P.Count).Where(i => op.C[i].slot == ColorSlot.Foundation).Select(i => op.P[i].y).ToList();
            Assert.Equal(-1.2, ys.Min(), 6); Assert.Equal(0.3, ys.Max(), 6);
        }

        [Fact]
        public void BricksKeepClearOfOpeningsAndComeBackTheSame()
        {
            var st = Artist();
            var (_, b, s) = Block(st);
            var a = Lod0.Build(s, b).Op; var again = Lod0.Build(new Site(new() { b }), b).Op;
            var bricks = Enumerable.Range(0, a.P.Count).Where(i => a.C[i].slot == ColorSlot.Wall && Math.Abs(a.C[i].tone - 0.8) < 1e-9).ToList();
            Assert.True(bricks.Count > 40, $"{bricks.Count} brick corners");
            Assert.Equal(bricks.Select(i => a.P[i]), Enumerable.Range(0, again.P.Count).Where(i => again.C[i].slot == ColorSlot.Wall && Math.Abs(again.C[i].tone - 0.8) < 1e-9).Select(i => again.P[i]));
            // none on the front wall's door (x 6.2..7.8, up to 2.6 m)
            Assert.DoesNotContain(bricks, i => Math.Abs(a.P[i].z + Dim.T_EXT + 0.004) < 1e-6 && a.P[i].x > 6.1 && a.P[i].x < 7.9 && a.P[i].y < 2.7);
        }

        [Fact]
        public void TheBandReachesTheFarShader()
        {
            var st = Artist();
            var (_, b, s) = Block(st);
            var m = Lod2.Build(s, b);
            Assert.Contains(m.Rows, r => Math.Abs(r.band[0] - 0.65) < 1e-9 && r.band[1] == 1);
            var plain = Block(Styles.Preset("brick").Clone());
            Assert.All(Lod2.Build(plain.site, plain.b).Rows, r => Assert.Equal(0, r.band[0]));
        }

        [Fact]
        public void TheyRoundTrip()
        {
            var st = Artist(); st.frame = "#222222"; st.foundationColor = "#111111"; st.plinth = "#777777";
            var back = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(st));
            Assert.Equal(PrototypeJson.WriteStyle(st), PrototypeJson.WriteStyle(back));
            Assert.Equal((2, 3), (back.paneCols, back.paneRows));
            Assert.Equal(HeadType.Arch, back.head); Assert.Equal(DoorType.Glazed, back.doorType);
            // a prototype style writes none of them
            var p = PrototypeJson.WriteStyle(Styles.Preset("brick"));
            foreach (var key in new[] { "head", "panes", "frames", "bandH", "doorType", "plinthH", "foundation", "bricks", "frame", "plinth" }) Assert.DoesNotContain($"\"{key}\"", p);
        }

        [Fact]
        public void SillsAndHeadsCanGo()
        {
            var plain = Styles.Preset("brick").Clone(); plain.ground = GroundType.Match;
            var (_, b0, s0) = Block(plain); int all = Lod0.Build(s0, b0).Op.Tris;
            var bare = plain.Clone(); bare.sills = false; bare.heads = false;
            var (_, b1, s1) = Block(bare); var op = Lod0.Build(s1, b1).Op;
            Assert.True(op.Tris < all, "the sills and heads are gone");
            var rep = CoplanarCheck.Run(op);
            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
            // heads off takes the arched hood too
            var arch = plain.Clone(); arch.head = HeadType.Arch; var noHead = arch.Clone(); noHead.heads = false;
            var (_, ba, sa) = Block(arch); var (_, bn, sn) = Block(noHead);
            Assert.True(Lod0.Build(sn, bn).Op.Tris < Lod0.Build(sa, ba).Op.Tris);
            // the file keeps them, and leaves them out when on
            var back = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(bare));
            Assert.False(back.sills); Assert.False(back.heads);
            Assert.DoesNotContain("sills", PrototypeJson.WriteStyle(plain));
        }

        [Theory]
        [InlineData(0.08)]
        [InlineData(0.4)]
        public void TheFoundationStandsOutAsSet(double outBy)
        {
            var st = Artist(); st.foundationOut = outBy;
            var (_, b, site) = Block(st);
            var op = Lod0.Build(site, b).Op;
            // its furthest point is out by that much past the wall's outer face, beyond the 14 × 10 footprint
            double far = Enumerable.Range(0, op.Verts).Where(i => op.C[i].slot == ColorSlot.Foundation).Max(i => Math.Max(-op.P[i].z, op.P[i].z - 10));
            Assert.Equal(Dim.T_EXT + outBy, far, 3);
            var rep = CoplanarCheck.Run(op);
            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
            Assert.Equal(outBy, PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(st)).foundationOut);
        }
    }
}
