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
    /// The facade's strips (docs/EDITOR.md §6.7): the plinth's depth, the cornice round the top of a tier's walls, bands over
    /// the ground floor only, and the bands', cornice's and ground storey's own colours. Storey's own, for parity with the
    /// old BuildingGenerator's bottom, top and floor strips; a style without them builds as before.
    /// </summary>
    public class FacadeStripTests
    {
        static FacadeStyle Strips()
        {
            var st = Styles.Preset("brick").Clone();
            st.ground = GroundType.Match;
            st.plinthH = 0.35; st.plinthDepth = 0.12;
            st.corniceH = 0.4; st.corniceDepth = 0.2; st.cornice = "#D8C9A6";
            st.bandGround = true; st.bandH = 0.3; st.bandDepth = 0.1; st.band = "#5A5A5A";
            st.groundWall = "#3E3A36";
            return st;
        }

        /// <summary>A 14 × 10 m block of four storeys, a door on its south face; with <paramref name="setback"/> its top storey is set back to the west half.</summary>
        static (StoreyDocument d, BuildingData b, Site site) Block(FacadeStyle st, bool interior = true, bool setback = false, int storeys = 4)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            Floors.SetCount(b, storeys);
            if (setback) b.floors[storeys - 1].shape = new List<Vec2> { new Vec2(0, 0), new Vec2(7, 0), new Vec2(7, 10), new Vec2(0, 10) };
            b.style = st; b.interior = interior;
            return (d, b, new Site(d.buildings));
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public void TheStripsBuildWithoutFacesThatFlicker(bool interior, bool setback)
        {
            foreach (bool parapet in new[] { true, false })
            {
                var st = Strips(); st.parapet = parapet;
                var (_, b, site) = Block(st, interior, setback);
                var r0 = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
                Assert.True(r0.Overlaps.Count == 0, string.Join("\n", r0.Overlaps.Take(10)));
                var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true));
                Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(10)));
            }
        }

        [Theory]
        [InlineData(RoofType.Hip)]
        [InlineData(RoofType.Gable)]
        [InlineData(RoofType.Mansard)]
        [InlineData(RoofType.Shed)]
        public void ACorniceSitsUnderAPitchedRoofCleanly(RoofType roof)
        {
            var st = Strips(); st.roofType = roof;
            var (_, b, site) = Block(st);
            var r0 = CoplanarCheck.Run(Lod0.Build(site, b, solids: true).Op);
            Assert.True(r0.Overlaps.Count == 0, string.Join("\n", r0.Overlaps.Take(10)));
            // it stops under the eave's soffit, and the top storey's windows under it
            double drop = Roofs.SoffitDrop(roof, st.pitch, st.eave), top = Derived.RoofY(b);
            Assert.True(drop > 0.15);
            var op = Lod0.Build(site, b).Op;
            Assert.All(Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cornice), v => Assert.InRange(op.P[v].y, top - drop - 0.4 - 1e-6, top - drop + 1e-6));
            Assert.Equal(drop + 0.4, Facade.CorniceUnder(b, b.floors.Count - 1)!.Value, 9);
        }

        [Fact]
        public void TheirDefaultsBuildWhatNoneBuilds()
        {
            // the depth the plinth always had, and a ground-only flag off, build the very same mesh
            var plain = Styles.Preset("brick").Clone();
            var set = plain.Clone(); set.plinthDepth = 0.04; set.bandGround = false; set.corniceDepth = 0.3;   // a depth without a cornice is nothing
            var (_, b0, s0) = Block(plain); var (_, b1, s1) = Block(set);
            var m0 = Lod0.Build(s0, b0).Op; var m1 = Lod0.Build(s1, b1).Op;
            Assert.Equal(m0.P.ToList(), m1.P.ToList());
            Assert.Equal(m0.C.ToList(), m1.C.ToList());
            var l0 = Lod2.Build(s0, b0); var l1 = Lod2.Build(s1, b1);
            Assert.Equal(l0.Rows.SelectMany(r => r.GpuTexels(0)), l1.Rows.SelectMany(r => r.GpuTexels(0)));
            // and none of them is written for a style without them
            var json = PrototypeJson.WriteStyle(plain);
            foreach (var key in new[] { "plinthDepth", "corniceH", "corniceDepth", "bandGround", "groundWall", "\"band\"", "\"cornice\"" }) Assert.DoesNotContain(key.StartsWith("\"") ? key : $"\"{key}\"", json);
        }

        [Fact]
        public void TheyRoundTripAndBake()
        {
            var st = Strips();
            var back = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(st));
            Assert.Equal(PrototypeJson.WriteStyle(st), PrototypeJson.WriteStyle(back));
            Assert.Equal((0.4, 0.2, "#D8C9A6", true, "#5A5A5A", "#3E3A36", 0.12), (back.corniceH, back.corniceDepth, back.cornice, back.bandGround, back.band, back.groundWall, back.plinthDepth));
            var (d, _, _) = Block(st);
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document();
            Assert.Equal(PrototypeJson.WriteStyle(st), PrototypeJson.WriteStyle(baked.buildings[0].style));
        }

        /// <summary>
        /// A plinth deeper than the prototype's 4 cm is closed where a door cuts it, 8 cm either side, at LOD0 and LOD1: its
        /// ends were left open, as the prototype's sliver is, and the old Skyscraper Simple 2 theme's 22 cm plinth showed into
        /// itself beside its door. The prototype's is left as it draws it.
        /// </summary>
        [Fact]
        public void APlinthStandingOutIsClosedWhereADoorCutsIt()
        {
            foreach (double depth in new[] { 0.22, 0.04 })
            {
                var st = Styles.Preset("brick").Clone(); st.ground = GroundType.Match; st.plinthH = 0.62; st.plinthDepth = depth; st.foundation = null;
                var (_, b, site) = Block(st, interior: false);
                var f = Details.EdgeFrameAt(b, 0, 0);
                var door = Facade.Ops(site, b, 0, 0, f.e.L, f.uS, f.uE).Single(o => o.door);
                foreach (var (lod, mesh) in new[] { (0, Lod0.Build(site, b).Op), (1, Lod1.Build(site, b)) })
                {
                    // its ends facing the door, across its depth (out from the wall's outer face) and height
                    double area = 0;
                    for (int t = 0; t < mesh.Tris; t++)
                    {
                        var A = mesh.P[mesh.I[3 * t]]; var B = mesh.P[mesh.I[3 * t + 1]]; var C = mesh.P[mesh.I[3 * t + 2]];
                        double nx = (B.y - A.y) * (C.z - A.z) - (B.z - A.z) * (C.y - A.y), ny = (B.z - A.z) * (C.x - A.x) - (B.x - A.x) * (C.z - A.z), nz = (B.x - A.x) * (C.y - A.y) - (B.y - A.y) * (C.x - A.x);
                        double L = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (L < 1e-12) continue;
                        double x = (A.x + B.x + C.x) / 3, y = (A.y + B.y + C.y) / 3, z = (A.z + B.z + C.z) / 3;
                        bool faces = (nx / L > 0.99 && Math.Abs(x - (door.u0 - 0.08)) < 1e-6) || (nx / L < -0.99 && Math.Abs(x - (door.u1 + 0.08)) < 1e-6);
                        if (faces && y > 0 && y < 0.62 && z < -Dim.T_EXT && z > -Dim.T_EXT - depth) area += L / 2;
                    }
                    Assert.True(Math.Abs(area - (depth > 0.04 ? 2 * depth * 0.62 : 0)) < 1e-6, $"{depth} m deep, LOD{lod}: {area:F4} m² of ends facing the door");
                }
            }
        }

        [Fact]
        public void ThePlinthStandsOutAsSet()
        {
            foreach (double depth in new[] { 0.04, 0.15, 0.4 })
            {
                var st = Styles.Preset("brick").Clone(); st.plinthDepth = depth; st.plinthH = 0.5;
                var (_, b, site) = Block(st);
                var op = Lod0.Build(site, b).Op;
                // the plinth's face, out from the wall's face (the south wall's at z = −0.3), in its colour (a shade of the wall)
                var faces = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Wall && Math.Abs(op.C[v].tone - 0.72) < 1e-9 && op.P[v].y < 0.5 + 1e-6).Select(v => op.P[v].z).ToList();
                Assert.Contains(faces, z => Math.Abs(z - (-Dim.T_EXT - depth)) < 1e-6);
                Assert.DoesNotContain(faces, z => z < -Dim.T_EXT - depth - 1e-6);
            }
        }

        [Fact]
        public void ACorniceTopsTheWallsAndKeepsTheTopWindowsUnderIt()
        {
            var st = Strips(); st.windows = WindowType.Tall; st.corniceH = 0.6;
            var (_, b, site) = Block(st);
            int N = b.floors.Count; double top = Derived.RoofY(b), ch = 0.6;
            var op = Lod0.Build(site, b).Op;
            var cn = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cornice).Select(v => op.P[v]).ToList();
            Assert.NotEmpty(cn);
            Assert.All(cn, p => Assert.InRange(p.y, top - ch - 1e-6, top + 1e-6));
            // round all four walls: out to 20 cm past each face
            Assert.Contains(cn, p => Math.Abs(p.z - (-0.5)) < 1e-6); Assert.Contains(cn, p => Math.Abs(p.z - 10.5) < 1e-6);
            Assert.Contains(cn, p => Math.Abs(p.x - (-0.5)) < 1e-6); Assert.Contains(cn, p => Math.Abs(p.x - 14.5) < 1e-6);
            // the top storey's windows stay under it, the storeys below keep their own
            Assert.Equal(ch, Facade.CorniceUnder(b, N - 1)!.Value, 9); Assert.Null(Facade.CorniceUnder(b, N - 2));
            var spec = Facade.WindowSpec(st.windows, Derived.FloorH(b, N - 1), st, cornice: ch)!;
            Assert.True(spec.y1 <= Derived.FloorH(b, N - 1) - ch - 0.1 + 1e-9);
            var site2 = site; var ops = Facade.Ops(site2, b, N - 1, 0, 14, 0.44, 13.56);
            Assert.All(ops, o => Assert.True(o.y1 <= Derived.FloorH(b, N - 1) - ch - 0.1 + 1e-9, $"a window to {o.y1}"));
            Assert.Contains(Facade.Ops(site2, b, N - 2, 0, 14, 0.44, 13.56), o => o.y1 > Derived.FloorH(b, N - 2) - ch - 0.1);
            // LOD2 paints it, on a run of its own
            var m = Lod2.Build(site, b);
            Assert.Contains(m.Rows, r => Math.Abs(r.corniceH - ch) < 1e-9 && r.corniceSlot == (int)ColorSlot.Cornice);
        }

        [Fact]
        public void ACorniceRunsAlongATerraceAndTheBandWhereTheWallGoesOnUp()
        {
            var st = Strips(); st.bandGround = false;
            var (_, b, site) = Block(st, setback: true);
            int N = b.floors.Count; double y = Derived.FloorBase(b, N - 1);
            var op = Lod0.Build(site, b).Op;
            // on the storey under the setback, the cornice tops the eastern half (the terrace's edge)...
            var cn = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Cornice && Math.Abs(op.P[v].y - y) < 1e-6).Select(v => op.P[v]).ToList();
            Assert.Contains(cn, p => Math.Abs(p.x - 14.5) < 1e-6);
            // ...and the band the western half, whose wall goes on up into the setback
            var band = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Band && Math.Abs(op.P[v].y - y) < 1e-6).Select(v => op.P[v]).ToList();
            Assert.Contains(band, p => Math.Abs(p.x - (-0.4)) < 1e-6);
            Assert.DoesNotContain(band, p => p.x > 7.5);
        }

        [Fact]
        public void BandsCanBeOverTheGroundFloorOnly()
        {
            var st = Styles.Preset("brick").Clone(); st.bandGround = true; st.band = "#5A5A5A";
            var (_, b, site) = Block(st);
            var op = Lod0.Build(site, b).Op;
            var ys = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Band).Select(v => op.P[v].y).Distinct().ToList();
            Assert.NotEmpty(ys);
            Assert.All(ys, yy => Assert.InRange(yy, Derived.FloorBase(b, 1) - 0.22 - 1e-6, Derived.FloorBase(b, 1) + 1e-6));
            Assert.True(Facade.BandAt(b, 0)); Assert.False(Facade.BandAt(b, 1));
            // LOD2 bands its ground run alone, in the band's own colour
            var m = Lod2.Build(site, b);
            Assert.All(m.Rows.Where(r => r.run[1] > 0), r => Assert.Equal(1, r.run[1]));
            Assert.Contains(m.Rows, r => r.run[1] == 1 && r.band[3] == (int)ColorSlot.Band);
        }

        [Fact]
        public void TheGroundStoreyTakesItsOwnWallColour()
        {
            var st = Styles.Preset("brick").Clone(); st.groundWall = "#3E3A36"; st.ground = GroundType.Match;
            var (_, b, site) = Block(st);
            double y1 = Derived.FloorBase(b, 1);
            foreach (var op in new[] { Lod0.Build(site, b).Op, Lod1.Build(site, b) })
            {
                var ground = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.GroundWall).Select(v => op.P[v].y).ToList();
                Assert.NotEmpty(ground);
                Assert.All(ground, yy => Assert.True(yy <= y1 + 1e-6));
                // the plinth is a shade of it
                Assert.Contains(Enumerable.Range(0, op.Verts), v => op.C[v].slot == ColorSlot.GroundWall && Math.Abs(op.C[v].tone - 0.72) < 1e-9);
            }
            var m = Lod2.Build(site, b);
            Assert.Contains(m.Rows, r => r.wallSlot == (int)ColorSlot.GroundWall);
            Assert.Contains(m.Rows, r => r.wallSlot == 0 && r.spec[3] > 0);
            // the colour row resolves it: the walls' colour when the style has none
            Assert.Equal("#3E3A36", StyleColors.Of(st, ColorSlot.GroundWall));
            Assert.Equal(st.wall, StyleColors.Of(Styles.Preset("brick"), ColorSlot.GroundWall));
            Assert.Equal(Styles.Preset("brick").trim, StyleColors.Of(Styles.Preset("brick"), ColorSlot.Cornice));
        }

        /// <summary>The strips on every building of the prototype's corpora, at LOD0 and LOD1, with nothing that flickers.</summary>
        [Theory]
        [InlineData("demo.json")]
        [InlineData("variants.json")]
        public void EveryFixtureBuildingTakesThemCleanly(string file)
        {
            var doc = PrototypeJson.Read(Fixtures.Text(file)).Document;
            foreach (var st in StyleColorFields.Styles(doc))
            {
                st.plinthDepth = 0.12; st.corniceH = 0.35; st.corniceDepth = 0.15; st.cornice = "#D8C9A6"; st.bandGround = true; st.band = "#5A5A5A"; st.groundWall = "#3E3A36";
            }
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
