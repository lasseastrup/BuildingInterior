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
    /// A terrace's edge (<see cref="TerraceData"/>, docs/EDITOR.md §6.2), set as a flat roof's: its parapet or a curb, and a cap
    /// on the edge, a slab or a hanging one, in the colours of the floors below's style. Storey's own; a terrace without
    /// settings builds as it always has.
    /// </summary>
    public class TerraceEdgeTests
    {
        const double T = Dim.T_EXT;

        // a 14 × 10 m block of three storeys whose top storey sets back to its west 8 m: a terrace 6 m deep on the east, on storey 2
        static (StoreyDocument d, BuildingData b, Site site) Block(TerraceData? td, bool interior = true, bool neighbour = false, bool cornice = false)
        {
            var st = Styles.Preset("brick").Clone(); st.cap = "#C8B89A"; st.capRim = "#4A4A4A"; if (cornice) st.corniceH = 0.3;
            var (d, b, _) = RoofCapTests.Block(st, interior: interior, neighbour: neighbour);
            b.floors[2].shape = new List<Vec2> { new Vec2(0, 0), new Vec2(8, 0), new Vec2(8, 10), new Vec2(0, 10) };
            b.floors[2].terrace = td;
            return (d, b, new Site(d.buildings));
        }

        static TerraceData Edge(bool parapet, double? h = null, double? depth = null, double? strip = null) =>
            new TerraceData { parapet = parapet, capH = h, capDepth = depth, capStrip = strip };

        public static IEnumerable<object[]> Edges()
        {
            // none, a slab with an overhang and a strip, a slab flush, a hanging edge, a strip alone; each with a parapet and without
            foreach (var (h, dp, strip) in new (double?, double?, double?)[] { (null, null, null), (0.4, -0.25, 0.43), (0.11, -0.02, null), (-0.4, -0.3, 0.1), (0, 0, 0.43) })
                foreach (bool parapet in new[] { true, false })
                    yield return new object[] { parapet, h ?? double.NaN, dp ?? double.NaN, strip ?? double.NaN };
        }

        static double? N(double v) => double.IsNaN(v) ? null : v;

        [Theory, MemberData(nameof(Edges))]
        public void ItBuildsWithoutFacesThatFlicker(bool parapet, double h, double depth, double strip)
        {
            foreach (bool interior in new[] { true, false })
                foreach (bool neighbour in new[] { false, true })
                {
                    var (_, b, site) = Block(Edge(parapet, N(h), N(depth), N(strip)), interior, neighbour, cornice: true);
                    var r0 = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
                    Assert.True(r0.Overlaps.Count == 0, string.Join("\n", r0.Overlaps.Take(10)));
                    var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true));
                    Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(10)));
                    Assert.True(Lod2.Build(site, b).Tris > 0);
                }
        }

        [Fact]
        public void ATerraceWithoutSettingsIsBuiltAsItAlwaysWas()
        {
            static (List<P3> l0, List<P3> l2) Built(TerraceData? td)
            {
                var (_, b, site) = Block(td);
                return (Lod0.Build(site, b).Op.P.ToList(), Lod2.Build(site, b).P.ToList());
            }
            var none = Built(null); var dflt = Built(new TerraceData());
            Assert.Equal(none.l0, dflt.l0);
            Assert.Equal(none.l2, dflt.l2);
        }

        // the top of the terrace's edge on its east side, out past the wall's inner face, and the height its collision segments give it
        static (double top, double segH) EastEdge(TerraceData? td, bool lod2 = false)
        {
            var (_, b, site) = Block(td); double y = Derived.FloorBase(b, 2);
            var r = Lod0.Build(site, b);
            IEnumerable<P3> pts = lod2 ? Lod2.Build(site, b).P : r.Op.P;
            // a run's faces have their corners at its ends: along the whole east side, out past the wall's inner face (the setback is west)
            double top = pts.Where(p => p.x > 14 - 1e-6 && p.x < 15 && p.y > y - 1 && p.y < y + 4).Max(p => p.y) - y;
            var hs = r.Segs[2].Where(s => Math.Abs(s.ax - (14 + T / 2)) < 1e-6 && Math.Abs(s.bx - (14 + T / 2)) < 1e-6).Select(s => s.h).Distinct().ToList();
            return (top, Assert.Single(hs));
        }

        [Fact]
        public void ItsEdgeStandsAsHighAsItsSettingsSay()
        {
            Assert.Equal((1.1, 1.1), Round(EastEdge(null)));                              // the parapet, as always
            Assert.Equal((0.3, 0.3), Round(EastEdge(Edge(false))));                      // without: the curb
            Assert.Equal((1.5, 1.5), Round(EastEdge(Edge(true, 0.4, -0.25))));          // a slab lifts the parapet
            Assert.Equal((0.4, 0.4), Round(EastEdge(Edge(false, 0.4, -0.25))));         // and takes the curb's place
            Assert.Equal((1.1, 1.1), Round(EastEdge(Edge(true, -0.4, -0.3))));          // a hanging edge leaves the parapet
            Assert.Equal((0.3, 0.3), Round(EastEdge(Edge(false, -0.4, -0.3))));         // and the curb
            // LOD2 as high
            Assert.Equal(0.3, Math.Round(EastEdge(Edge(false), lod2: true).top, 6));
            Assert.Equal(1.5, Math.Round(EastEdge(Edge(true, 0.4, -0.25), lod2: true).top, 6));
            Assert.Equal(1.1, Math.Round(EastEdge(null, lod2: true).top, 6));
            static (double, double) Round((double a, double b) x) => (Math.Round(x.a, 6), Math.Round(x.b, 6));
        }

        // the cap's faces (its own colour: the style gives the cap one): the lowest and the furthest out past the east wall
        static (double low, double outX) CapFaces(TerraceData td)
        {
            var (_, b, site) = Block(td); double y = Derived.FloorBase(b, 2);
            var op = Lod0.Build(site, b).Op;
            var cap = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cap && op.P[v].x > 14).Select(v => op.P[v]).ToList();
            Assert.NotEmpty(cap);
            return (cap.Min(p => p.y) - y, cap.Max(p => p.x) - 14);
        }

        [Fact]
        public void ACapStandsOutOverTheWallBelow()
        {
            var slab = CapFaces(Edge(true, 0.4, -0.25));
            Assert.Equal(0, slab.low, 6); Assert.Equal(T + 0.25, slab.outX, 6);       // from the deck up, 25 cm out past the wall
            var hanging = CapFaces(Edge(true, -0.4, -0.3));
            Assert.Equal(-0.4, hanging.low, 6); Assert.Equal(T + 0.3, hanging.outX, 6);
        }

        [Fact]
        public void WhereItStopsAgainstTheSetbackItsOverhangIsClosed()
        {
            // the south and north runs stop against the setback's east wall, its outer face at x = 8.3; the slab's overhang
            // stands out past the wall below there (z under −0.3, over 10.3), and its end is a face
            int Ends(TerraceData? td)
            {
                var (_, b, site) = Block(td); var op = Lod0.Build(site, b).Op; int n = 0;
                for (int t = 0; t < op.I.Count; t += 3)
                {
                    var ps = new[] { op.P[op.I[t]], op.P[op.I[t + 1]], op.P[op.I[t + 2]] };
                    if (ps.All(p => Math.Abs(p.x - (8 + T)) < 1e-6) && (ps.All(p => p.z < -T + 1e-6) || ps.All(p => p.z > 10 + T - 1e-6))) n++;
                }
                return n;
            }
            Assert.Equal(0, Ends(null));
            Assert.Equal(4, Ends(Edge(true, 0.4, -0.25)));    // a quad at each of the two
            Assert.Equal(4, Ends(Edge(false, -0.4, -0.3)));
        }

        [Fact]
        public void UnderAHangingEdgeTheCorniceDrops()
        {
            var (_, b, _) = Block(Edge(true, -0.4, -0.3), cornice: true);
            Assert.Equal(0.4, Facade.CorniceDrop(b, 1), 9);
            b.floors[2].terrace = null;
            Assert.Equal(0, Facade.CorniceDrop(b, 1), 9);
        }

        [Fact]
        public void ACapSetInIsARoofsAlone()
        {
            Assert.Null(RoofCap.Of(Edge(true, 0.4, 0.5)));                    // set in: none on a terrace
            Assert.NotNull(RoofCapTests.Capped(0.4, 0.5).capH);
            Assert.True(RoofCap.Of(RoofCapTests.Capped(0.4, 0.5))!.Value.inset);
            Assert.Null(RoofCap.Of((TerraceData?)null));
            Assert.Null(RoofCap.Of(Edge(true)));
        }

        [Fact]
        public void ItIsKeptInTheLayoutOnlyWhenSet()
        {
            var (_, b, _) = Block(Edge(false, -0.4, -0.3, 0.1));
            string json = PrototypeJson.Write(b);
            Assert.Contains("\"terrace\":{\"parapet\":false,\"capH\":-0.4,\"capDepth\":-0.3,\"capStrip\":0.1}", json);
            var unknown = new List<string>();
            var back = PrototypeJson.ReadBuilding(json, unknown);
            Assert.Empty(unknown);
            Assert.Equal(json, PrototypeJson.Write(back));
            b.floors[2].terrace = new TerraceData();                          // the parapet a terrace always had: nothing written
            Assert.DoesNotContain("\"terrace\"", PrototypeJson.Write(b));
            Assert.Null(PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).floors[2].terrace);
        }

        [Fact]
        public void ItGoesWithTheSetback()
        {
            var (_, b, _) = Block(Edge(false, 0.4, -0.25));
            Floors.SetCount(b, 4);
            Assert.True(Setbacks.Move(b, 2, 3));
            Assert.Null(b.floors[2].terrace);
            Assert.False(b.floors[3].terrace!.parapet);
            Setbacks.Remove(b, 3);
            Assert.Null(b.floors[3].terrace);
            // a roof over the terrace keeps it for when the terrace is back
            var (_, c, _) = Block(Edge(false));
            Facades.SetTerraceRoof(c, 2, true);
            Assert.False(c.floors[2].terrace!.parapet);
            Facades.SetTerraceRoof(c, 2, false);
            Assert.False(c.floors[2].terrace!.parapet);
        }
    }
}
