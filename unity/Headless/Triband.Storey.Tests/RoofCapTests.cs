using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The cap on a flat roof's edge (docs/EDITOR.md §6.7, <see cref="RoofCap"/>), for parity with the old BuildingGenerator's
    /// roof insert: a slab standing out over the walls, a fascia hanging from the edge, or a ring set in on the roof, with a
    /// strip in its colour and its overhang's underside in the rim's. Storey's own; a style without one builds as before.
    /// </summary>
    public class RoofCapTests
    {
        static FacadeStyle Capped(double h, double depth, double? strip = null, bool parapet = false)
        {
            var st = Styles.Preset("brick").Clone();
            st.capH = h; st.capDepth = depth; st.capStrip = strip; st.parapet = parapet; st.cap = "#C8B89A"; st.capRim = "#4A4A4A";
            return st;
        }

        static readonly List<Vec2> Rect = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
        // an L, concave at (8, 5), with a short edge an inset of 2.25 m swallows
        static readonly List<Vec2> Ell = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 5), new Vec2(8, 5), new Vec2(8, 12), new Vec2(0, 12) };

        static (StoreyDocument d, BuildingData b, Site site) Block(FacadeStyle st, List<Vec2>? outline = null, bool interior = true, bool core = false, bool neighbour = false)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new List<Vec2>(outline ?? Rect);
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            Floors.SetCount(b, 3);
            b.style = st; b.interior = interior;
            if (core) b.shafts.Add(new CoreData { id = "c", type = CoreType.Stairs, x = 4, z = 4, roof = true });
            if (neighbour)
            {
                var n = Buildings.Add(d, "rect", new Vec2(0, 0)); n.pos = new Vec2(0, 0);
                n.footprint = new List<Vec2> { new Vec2(14, 0), new Vec2(26, 0), new Vec2(26, 10), new Vec2(14, 10) };   // back to back along x = 14
                n.entrances.Clear(); n.details.Clear(); n.shafts.Clear(); Floors.SetCount(n, 3); n.style = Styles.Preset("brick").Clone();
            }
            return (d, b, new Site(d.buildings));
        }

        public static IEnumerable<object[]> Caps()
        {
            // the old generator's themes: Residential (−0.25, 0.4, strip 0.43), Industrial Grey (−0.17, 0.91), Old Red (−0.02,
            // 0.11), Skyscraper 2 (0.74, 1.31) and Skyscraper (2.25, 3.23); a fascia; a strip alone
            foreach (var (h, dp, strip) in new (double, double, double?)[] { (0.4, -0.25, 0.43), (0.91, -0.17, null), (0.11, -0.02, null), (1.31, 0.74, null), (3.23, 2.25, null), (-0.4, -0.3, 0.1), (0, 0, 0.43) })
                foreach (bool parapet in new[] { false, true })
                    yield return new object[] { h, dp, strip ?? 0, parapet };
        }

        [Theory, MemberData(nameof(Caps))]
        public void ItBuildsWithoutFacesThatFlicker(double h, double depth, double strip, bool parapet)
        {
            foreach (var outline in new[] { Rect, Ell })
                foreach (bool interior in new[] { true, false })
                    foreach (bool neighbour in new[] { false, true })
                    {
                        var (_, b, site) = Block(Capped(h, depth, strip > 0 ? strip : null, parapet), outline, interior, core: interior, neighbour: neighbour);
                        var r0 = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
                        Assert.True(r0.Overlaps.Count == 0, string.Join("\n", r0.Overlaps.Take(10)));
                        var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true));
                        Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(10)));
                        Assert.True(Lod2.Build(site, b).Tris > 0);
                    }
        }

        [Fact]
        public void ItsNumbersWithoutAHeightBuildNothing()
        {
            var plain = Styles.Preset("brick").Clone();
            var set = plain.Clone(); set.capDepth = -0.25; set.capStrip = 0.4; set.cap = "#C8B89A";
            var (_, b0, s0) = Block(plain); var (_, b1, s1) = Block(set);
            Assert.Equal(Lod0.Build(s0, b0).Op.P.ToList(), Lod0.Build(s1, b1).Op.P.ToList());
            Assert.Equal(Lod1.Build(s0, b0).P.ToList(), Lod1.Build(s1, b1).P.ToList());
            Assert.Null(RoofCap.Of(set));
            // nor on a pitched roof
            var hip = Capped(0.4, -0.25); hip.roofType = RoofType.Hip;
            Assert.Null(RoofCap.Of(hip));
            foreach (var key in new[] { "capH", "capDepth", "capStrip", "\"cap\"", "capRim" }) Assert.DoesNotContain(key.StartsWith("\"") ? key : $"\"{key}\"", PrototypeJson.WriteStyle(plain));
        }

        [Fact]
        public void ARaisedCapStandsOutOverTheWallsAndLiftsTheParapet()
        {
            var st = Capped(0.4, -0.25, 0.43, parapet: true);
            var (_, b, site) = Block(st);
            double top = Derived.RoofY(b), T = Dim.T_EXT;
            var op = Lod0.Build(site, b).Op;
            var cap = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cap).Select(v => op.P[v]).ToList();
            Assert.NotEmpty(cap);
            Assert.All(cap, p => Assert.InRange(p.y, top - 1e-6, top + 0.4 + 1e-6));
            Assert.Equal(-T - 0.25, cap.Min(p => p.z), 6);   // the south face: out past the wall by the overhang
            Assert.Equal(14 + T + 0.25, cap.Max(p => p.x), 6);
            // the underside of the overhang in the rim's colour, facing down, at the roof's level
            var rim = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.CapRim).ToList();
            Assert.NotEmpty(rim); Assert.All(rim, v => { Assert.Equal(top, op.P[v].y, 6); Assert.True(op.N[v].y < -0.9); });
            // the strip round its top, out from the parapet's foot; without a parapet, 43 cm in from its outer edge
            var strip = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cap && op.N[v].y > 0.9).Select(v => op.P[v]).ToList();
            Assert.Contains(strip, p => Math.Abs(p.z - (-T)) < 1e-6); Assert.DoesNotContain(strip, p => p.z > -T + 1e-6 && p.x > 1 && p.x < 13);
            var (_, b2, s2) = Block(Capped(0.4, -0.25, 0.43));
            var op2 = Lod0.Build(s2, b2).Op;
            Assert.Contains(Enumerable.Range(0, op2.Verts), v => op2.C[v].slot == ColorSlot.Cap && op2.N[v].y > 0.9 && Math.Abs(op2.P[v].z - (-T - 0.25 + 0.43)) < 1e-6);
            // the parapet stands on it
            var parapet = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Wall && op.P[v].y > top + 0.01).Select(v => op.P[v].y).ToList();
            Assert.Equal(top + 0.4, parapet.Min(), 6);
            Assert.Equal(top + 0.4 + 1.1, op.P.ToList().Max(p => p.y), 6);
            // LOD2: the massing rises to the parapet's top, and paints the cap in the parapet's zone
            var m = Lod2.Build(site, b);
            Assert.Equal(top + 0.4 + 1.1, m.P.Max(p => p.y), 6);
            Assert.Contains(m.Rows, r => r.run[3] == 4 && Math.Abs(r.cap[0] - 0.4) < 1e-9 && r.cap[1] == (int)ColorSlot.Cap);
        }

        [Fact]
        public void ARaisedCapTakesTheCurbsPlace()
        {
            var (_, b, site) = Block(Capped(0.11, -0.02));
            double top = Derived.RoofY(b);
            var op = Lod0.Build(site, b).Op;
            Assert.Equal(top + 0.11, op.P.ToList().Max(p => p.y), 6);   // no 30 cm curb
            Assert.Equal(top + 0.11, Lod2.Build(site, b).P.Max(p => p.y), 6);
        }

        /// <summary>
        /// A raised cap on a roof nothing comes up through lifts the whole roof to its top, as the old BuildingGenerator's roof
        /// insert is a slab (<see cref="RoofCap.Lid"/>): no ring with the roof below it, and the roof walked on there. With stairs
        /// to the roof the roof keeps its height inside the ring, for the bulkhead's door.
        /// </summary>
        [Fact]
        public void ARaisedCapLiftsTheWholeRoofWhereNothingComesUpThroughIt()
        {
            foreach (bool core in new[] { false, true })
            {
                var (_, b, site) = Block(Capped(0.4, -0.25, 0.43), core: core);
                double top = Derived.RoofY(b), lift = core ? 0 : 0.4;
                Assert.Equal(lift, RoofCap.Lid(b, out _), 9);
                var op = Lod0.Build(site, b).Op;
                // the roof's own colour facing up, away from the bulkhead: at the cap's top, or at the walls' with stairs up to it
                var roof = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Roof && op.N[v].y > 0.9 && op.P[v].x > 8).Select(v => op.P[v].y).Distinct().ToList();
                Assert.Contains(top + lift, roof.Select(y => Math.Round(y, 6)));
                if (!core) Assert.DoesNotContain(roof, y => Math.Abs(y - top) < 1e-6);
                // the cap's sides facing in over the roof: none round a lid
                var inner = Enumerable.Range(0, op.Verts).Count(v => op.C[v].slot == ColorSlot.Cap && Math.Abs(op.N[v].y) < 0.1 && op.N[v].z > 0.9 && Math.Abs(op.P[v].z) < 1e-6);
                Assert.True(core ? inner > 0 : inner == 0, $"core {core}: {inner} inner side vertices");
                var r = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
                Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(5)));
                // walked on at its top
                var m = CollisionMesh.Build(site, b, Lod0.BuildShape(site, b));
                var floors = Enumerable.Range(0, m.Tris).Where(t => m.Kind[t] == CollisionKind.Floor).Select(t => m.P[m.I[3 * t]].y).ToList();
                Assert.Equal(top + lift, floors.Max(), 6);
            }
        }

        /// <summary>
        /// An inset cap on a roof nothing comes up through is a solid block, as the old BuildingGenerator's inset roof insert,
        /// its top the roof's colour (round it the strip's, where there is one), with no curb round the roof under it.
        /// </summary>
        [Fact]
        public void AnInsetCapIsASolidBlockWhereNothingComesUpThroughIt()
        {
            foreach (double strip in new[] { 0.0, 0.5 })
            {
                var (_, b, site) = Block(Capped(1.31, 0.74, strip > 0 ? strip : null));
                double top = Derived.RoofY(b), T = Dim.T_EXT, inset = 0.74 - T;
                var op = Lod0.Build(site, b).Op;
                double area = 0, stripArea = 0;
                for (int t = 0; t < op.Tris; t++)
                {
                    int i0 = op.I[3 * t], i1 = op.I[3 * t + 1], i2 = op.I[3 * t + 2];
                    if (Math.Abs(op.P[i0].y - (top + 1.31)) > 1e-6 || op.N[i0].y < 0.9) continue;
                    var a = op.P[i1] - op.P[i0]; var c = op.P[i2] - op.P[i0];
                    double ar = Math.Abs(a.x * c.z - a.z * c.x) / 2;
                    area += ar; if (op.C[i0].slot == ColorSlot.Cap) stripArea += ar;
                }
                // the outline (14 × 10) set in to the cap's outer side, all of it
                Assert.Equal((14 - 2 * inset) * (10 - 2 * inset), area, 3);
                Assert.Equal(strip > 0 ? (14 - 2 * inset) * (10 - 2 * inset) - (14 - 2 * inset - 2 * strip) * (10 - 2 * inset - 2 * strip) : 0, stripArea, 3);
                // no curb: nothing in the trim's colour stands above the walls' top
                Assert.DoesNotContain(Enumerable.Range(0, op.Verts), v => op.C[v].slot == ColorSlot.Trim && op.P[v].y > top + 1e-6);
                // the roof round the block in the cap's rim colour, as the old generator's roof insert has it
                Assert.Contains(Enumerable.Range(0, op.Verts), v => op.C[v].slot == ColorSlot.CapRim && op.N[v].y > 0.9 && Math.Abs(op.P[v].y - top) < 1e-6);
                Assert.DoesNotContain(Enumerable.Range(0, op.Verts), v => op.C[v].slot == ColorSlot.Roof && op.N[v].y > 0.9 && Math.Abs(op.P[v].y - top) < 1e-6);
                var r = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
                Assert.True(r.Overlaps.Count == 0, string.Join("\n", r.Overlaps.Take(5)));
            }
        }

        [Fact]
        public void AFasciaHangsUnderTheCurbAndTheCorniceUnderIt()
        {
            var st = Capped(-0.5, -0.3); st.corniceH = 0.3;
            var (_, b, site) = Block(st);
            double top = Derived.RoofY(b);
            var op = Lod0.Build(site, b).Op;
            var cap = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cap).Select(v => op.P[v]).ToList();
            Assert.All(cap, p => Assert.InRange(p.y, top - 0.5 - 1e-6, top + 1e-6));
            Assert.Equal(top + 0.3, op.P.ToList().Max(p => p.y), 6);   // the curb stays
            Assert.Equal(0.5 + 0.3, Facade.CorniceUnder(b, b.floors.Count - 1)!.Value, 9);
            // without an overhang there's nothing to hang
            Assert.Null(RoofCap.Of(Capped(-0.5, 0)));
        }

        [Fact]
        public void AnInsetCapOnAConcaveOutlineStaysClearOfTheWallsAndRoundTheBulkhead()
        {
            var (_, b, site) = Block(Capped(3.23, 2.25), Ell, core: true);
            double top = Derived.RoofY(b), T = Dim.T_EXT;
            var op = Lod0.Build(site, b).Op;
            var cap = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cap).Select(v => op.P[v]).ToList();
            Assert.NotEmpty(cap);
            // every point of it inside the outline, its outer side 2.25 m in from the wall's face, and none past its inner side
            foreach (var p in cap)
            {
                Assert.True(Geo.Pip(Ell, p.x, p.z), $"{p} is outside the outline");
                double dist = Enumerable.Range(0, Ell.Count).Min(i => Geo.SegDist(p.x, p.z, Ell[i].x, Ell[i].z, Ell[(i + 1) % Ell.Count].x, Ell[(i + 1) % Ell.Count].z).d);
                Assert.InRange(dist, 2.25 - T - 1e-3, 2.25 * Math.Sqrt(2) + 1e-3);   // a mitred corner's point is √2 times as far
            }
            // and round the bulkhead of the stairs to the roof (its centre at (4, 4)): none of it within it
            Assert.DoesNotContain(cap, p => Math.Abs(p.x - 4) < 1.5 && Math.Abs(p.z - 4) < 2.8);
            Assert.InRange(op.P.ToList().Max(p => p.y), top + 3.23 - 1e-6, top + 3.23 + 1e-6);
        }

        [Fact]
        public void TheCapStopsWhereTheParapetDoesAlongAPartyWall()
        {
            foreach (var (h, dp) in new[] { (0.4, -0.25), (1.0, 1.0) })
            {
                var (_, b, site) = Block(Capped(h, dp), neighbour: true);
                var op = Lod0.Build(site, b).Op;
                var cap = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cap).Select(v => op.P[v]).ToList();
                Assert.NotEmpty(cap);
                // nothing of it along the east wall, which the neighbour shares (but the corners' ends)
                Assert.DoesNotContain(cap, p => p.x > 14 - (dp > 0 ? dp : 0) - 0.05 && p.z > 1.5 && p.z < 8.5);
            }
        }

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var st = Capped(0.4, -0.25, 0.43);
            var back = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(st));
            Assert.Equal((0.4, -0.25, 0.43, "#C8B89A", "#4A4A4A"), (back.capH, back.capDepth, back.capStrip, back.cap, back.capRim));
            var (d, _, _) = Block(st);
            Assert.Equal(PrototypeJson.WriteStyle(st), PrototypeJson.WriteStyle(BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style));
            Assert.Equal("#4A4A4A", StyleColors.Of(st, ColorSlot.CapRim));
            var noRim = st.Clone(); noRim.capRim = null;
            Assert.Equal("#C8B89A", StyleColors.Of(noRim, ColorSlot.CapRim));
        }

        /// <summary>A cap on every flat-roofed building of the prototype's corpora (setbacks, courtyards, atria, cores, neighbours), with nothing that flickers.</summary>
        [Theory]
        [InlineData("demo.json", 0.4, -0.25, 0.43)]
        [InlineData("variants.json", 0.4, -0.25, 0.43)]
        [InlineData("demo.json", 1.31, 0.74, 0.0)]
        [InlineData("variants.json", 1.31, 0.74, 0.0)]
        public void EveryFixtureBuildingTakesItCleanly(string file, double h, double depth, double strip)
        {
            var doc = PrototypeJson.Read(Fixtures.Text(file)).Document;
            foreach (var st in StyleColorFields.Styles(doc)) { st.capH = h; st.capDepth = depth; st.capStrip = strip > 0 ? strip : null; st.cap = "#C8B89A"; }
            var site = new Site(doc.buildings);
            var bad = new List<string>();
            foreach (var b in doc.buildings)
                foreach (var mesh in new[] { Lod0.Build(site, b, solids: true).Op, Lod1.Build(site, b, solids: true) })
                {
                    var r = CoplanarCheck.Run(mesh);
                    if (r.Overlaps.Count > 0) bad.Add($"{b.name}: {string.Join("; ", r.Overlaps.Take(3))}");
                }
            Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
        }
    }
}
