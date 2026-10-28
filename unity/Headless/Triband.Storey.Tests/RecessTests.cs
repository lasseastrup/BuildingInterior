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
    /// How deep windows and doors are set into the wall (docs/EDITOR.md §6.7, <see cref="FacadeStyle.recess"/>,
    /// <see cref="FacadeStyle.doorRecess"/>), for parity with the old BuildingGenerator's insets: the generator's pane, frame
    /// and bars go that deep, a shell storey's reveal as deep, an artist's window or door is pushed back that far, and a
    /// recessed street door's threshold is in the foundation colour. Storey's own; null builds as before.
    /// </summary>
    public class RecessTests
    {
        const double T = Dim.T_EXT;

        // the south wall's face is at z = −T: a point w out from the outline's line is at z = −w
        static (StoreyDocument d, BuildingData b, Site site) Block(Action<FacadeStyle>? f = null, bool interior = false)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            Floors.SetCount(b, 3);
            b.interior = interior; b.style = Styles.Preset("brick").Clone(); b.style.ground = GroundType.Match;
            f?.Invoke(b.style);
            return (d, b, new Site(d.buildings));
        }

        [Fact]
        public void TheDepthItAlwaysHadBuildsWhatNoneBuilds()
        {
            foreach (bool interior in new[] { false, true })
            {
                var (_, b0, s0) = Block(st => st.frames = true, interior);
                var (_, b1, s1) = Block(st => { st.frames = true; st.recess = T * 0.55; st.doorRecess = T * 0.55; }, interior);
                var p0 = Lod0.Build(s0, b0); var p1 = Lod0.Build(s1, b1);
                foreach (var (m0, m1) in new[] { (p0.Op, p1.Op), (p0.Glass, p1.Glass), (Lod1.Build(s0, b0), Lod1.Build(s1, b1)) })
                {
                    Assert.Equal(m0.Verts, m1.Verts);
                    for (int v = 0; v < m0.Verts; v++) Assert.True((m0.P[v] - m1.P[v]).Length < 1e-9, $"{m0.P[v]} moved to {m1.P[v]}");
                }
            }
            Assert.Equal(T * 0.45, Facade.PaneW(Styles.Preset("brick"), false));
            Assert.Equal(0, Facade.KindBack(Styles.Preset("brick"), true));
            foreach (var key in new[] { "recess", "doorRecess" }) Assert.DoesNotContain($"\"{key}\"", PrototypeJson.WriteStyle(Styles.Preset("brick")));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.05)]
        [InlineData(0.25)]
        public void TheGeneratorsWindowsSitAsDeepAsSet(double recess)
        {
            var (_, b, site) = Block(st => { st.recess = recess; st.frames = true; st.paneCols = 2; st.paneRows = 2; });
            var op = Lod0.Build(site, b).Op;
            // the shell storeys' opaque panes, and LOD1's cut ones, at that depth
            var depths = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Glass && op.N[v].z < -0.9 && op.P[v].y > 1).Select(v => Math.Round(-op.P[v].z, 6)).Distinct().ToList();
            double deep = Math.Max(0.01, recess);   // a centimetre at least, clear of a band's back in the wall's face
            Assert.Equal(new[] { Math.Round(T - deep, 6) }, depths);
            // the frame round it and the bars across it go with it, inside the wall
            var frame = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Frame && op.P[v].y > 1 && op.P[v].z < 0 && Math.Abs(op.N[v].z) > 0.9).Select(v => -op.P[v].z).ToList();
            Assert.NotEmpty(frame);
            Assert.Equal(T - deep - 0.045, frame.Min(), 6);
            Assert.True(frame.Min() > 0);
            var lod1 = Lod1.Build(site, b, recessed: true);
            Assert.Contains(Enumerable.Range(0, lod1.Verts), v => lod1.C[v].slot == ColorSlot.Glass && Math.Abs(-lod1.P[v].z - (T - deep)) < 1e-9);
        }

        [Fact]
        public void ARecessedStreetDoorHasItsPanelAtTheBackAndAThresholdInTheFoundationColour()
        {
            var (_, b, site) = Block(st => st.doorRecess = 0.25);
            foreach (var m in new[] { Lod0.Build(site, b).Op, Lod1.Build(site, b) })
            {
                var door = Enumerable.Range(0, m.Verts).Where(v => m.C[v].slot == ColorSlot.Door && m.N[v].z < -0.9).Select(v => Math.Round(-m.P[v].z, 6)).Distinct().ToList();
                Assert.Equal(new[] { Math.Round(T - 0.25, 6) }, door);
                // the threshold: facing up at the floor, from the panel to the wall's face
                var thr = Enumerable.Range(0, m.Verts).Where(v => m.C[v].slot == ColorSlot.Foundation && m.N[v].y > 0.9 && Math.Abs(m.P[v].y) < 1e-9).Select(v => -m.P[v].z).ToList();
                Assert.Contains(thr, w => Math.Abs(w - (T - 0.25)) < 1e-9); Assert.Contains(thr, w => Math.Abs(w - T) < 1e-9);
            }
            // a door recess leaves the windows where they were, and the windows' the doors
            var (_, b2, s2) = Block(st => st.recess = 0.05);
            var op2 = Lod0.Build(s2, b2).Op;
            Assert.Contains(Enumerable.Range(0, op2.Verts), v => op2.C[v].slot == ColorSlot.Door && Math.Abs(-op2.P[v].z - T * 0.45) < 1e-9);
        }

        [Fact]
        public void AnArtistsWindowAndDoorArePushedBack()
        {
            var win = OpeningKindTestsKind("t-recess-win", false); var door = OpeningKindTestsKind("t-recess-door", true);
            OpeningKinds.Register(win); OpeningKinds.Register(door);
            try
            {
                double Front(Action<FacadeStyle> f, ColorSlot slot)
                {
                    var (_, b, site) = Block(st => { st.windowKind = win.id; st.doorKind = door.id; f(st); });
                    var op = Lod0.Build(site, b).Op;
                    // the windows above the ground floor, the street door on it
                    return Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == slot && op.P[v].z < 0 && (slot == ColorSlot.Door || op.P[v].y > 3.7)).Min(v => op.P[v].z);
                }
                // its frame strip stands 3 cm out from its own wall face: at the wall's face +3 cm as before, and pushed back with a recess
                Assert.Equal(-T - 0.03, Front(st => { }, ColorSlot.Frame), 6);
                Assert.Equal(-T - 0.03 + 0.12, Front(st => st.recess = 0.12, ColorSlot.Frame), 6);
                Assert.Equal(-T + 0.05 + 0.2, Front(st => st.doorRecess = 0.2, ColorSlot.Door), 6);   // the leaf, 5 cm behind its face, 20 cm back
            }
            finally { OpeningKinds.Remove(win.id); OpeningKinds.Remove(door.id); }
        }

        // OpeningKindTests' window and door: a frame strip along the top 3 cm proud, the pane or leaf 5 cm back
        static OpeningKind OpeningKindTestsKind(string id, bool door)
        {
            var m = new OpeningKind.Mesh();
            var P = new List<P3>(); var f = new P3(0, 0, 1);
            int Q(P3 a, P3 b, P3 c, P3 d) { int s = P.Count; P.AddRange(new[] { a, b, c, d }); return s; }
            int s0 = Q(new P3(-0.5, 1.4, 0.03), new P3(-0.5, 1.5, 0.03), new P3(0.5, 1.5, 0.03), new P3(0.5, 1.4, 0.03));
            int s1 = Q(new P3(-0.5, 0, -0.05), new P3(-0.5, 1.4, -0.05), new P3(0.5, 1.4, -0.05), new P3(0.5, 0, -0.05));
            int[] Tri(int s) => new[] { s, s + 1, s + 2, s, s + 2, s + 3 };
            m.P = P.ToArray(); m.N = Enumerable.Repeat(f, P.Count).ToArray();
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = Tri(s0) });
            m.parts.Add(new OpeningKind.Part { slot = door ? ColorSlot.Door : ColorSlot.Glass, leaf = door, tris = Tri(s1) });
            return new OpeningKind { id = id, name = id, door = door, w = 1, h = 1.5, lod0 = m };
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ItBuildsWithoutFacesThatFlicker(bool interior)
        {
            foreach (double r in new[] { 0.0, 0.1, 0.25 })
                foreach (Action<FacadeStyle> f in new Action<FacadeStyle>[] { st => { }, st => { st.frames = true; st.paneCols = 3; st.paneRows = 2; }, st => st.windows = WindowType.Curtain, st => st.head = HeadType.Arch })
                {
                    var (_, b, site) = Block(st => { f(st); st.recess = r; st.doorRecess = r + 0.04; }, interior);
                    var lod0 = Lod0.Build(site, b, solids: true);
                    var rep = CoplanarCheck.Run(new[] { lod0.Op, lod0.Glass });
                    Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
                    foreach (bool recessed in new[] { false, true })
                    {
                        var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true, recessed: recessed));
                        Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(10)));
                    }
                }
        }

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var (d, b, _) = Block(st => { st.recess = 0.1; st.doorRecess = 0.2; });
            var back = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(b.style));
            Assert.Equal((0.1, 0.2), (back.recess, back.doorRecess));
            Assert.Equal(PrototypeJson.WriteStyle(b.style), PrototypeJson.WriteStyle(BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style));
        }

        // a street door set of one plain door with a recess of its own (null: the style's)
        static void OwnDoor(FacadeStyle st, double? recess, string? kind = null) => st.doorSet = new List<OpeningEntry> { new OpeningEntry { kind = kind, recess = recess } };

        static List<double> DoorDepths(MeshBuilder m) =>
            Enumerable.Range(0, m.Verts).Where(v => m.C[v].slot == ColorSlot.Door && m.N[v].z < -0.9).Select(v => Math.Round(-m.P[v].z, 6)).Distinct().ToList();

        [Fact]
        public void ADoorSetWithoutRecessesBuildsAsTheStylesDoor()
        {
            foreach (double? style in new double?[] { null, 0.12 })
            {
                var (_, b0, s0) = Block(st => st.doorRecess = style);
                var (_, b1, s1) = Block(st => { st.doorRecess = style; OwnDoor(st, null); });
                var p0 = Lod0.Build(s0, b0); var p1 = Lod0.Build(s1, b1);
                foreach (var (m0, m1) in new[] { (p0.Op, p1.Op), (p0.Glass, p1.Glass), (Lod1.Build(s0, b0), Lod1.Build(s1, b1)) })
                {
                    Assert.Equal(m0.Verts, m1.Verts);
                    for (int v = 0; v < m0.Verts; v++) { Assert.True((m0.P[v] - m1.P[v]).Length < 1e-9, $"{m0.P[v]} moved to {m1.P[v]}"); Assert.Equal(m0.C[v].slot, m1.C[v].slot); }
                }
            }
            // an opening taken again from a pool loses the recess it had; a copy keeps it
            var o = new Opening().Set(0, 1, 0, 2, door: true, recess: 0.2);
            Assert.Equal(0.2, new Opening().Set(o).recess);
            Assert.Null(o.Set(0, 1, 0, 2, door: true).recess);
        }

        [Fact]
        public void AnEntrysRecessPushesItsDoorsBackFurtherThanTheStyles()
        {
            // the style's doors 5 cm in, this entry's 25: its panel at the back, its threshold in the foundation colour, the windows as they were
            foreach (double? style in new double?[] { 0.05, null })
            {
                var (_, b, site) = Block(st => { st.doorRecess = style; OwnDoor(st, 0.25); });
                var (_, bw, sw) = Block(st => st.doorRecess = style);
                foreach (var (m, plain) in new[] { (Lod0.Build(site, b).Op, Lod0.Build(sw, bw).Op), (Lod1.Build(site, b), Lod1.Build(sw, bw)) })
                {
                    Assert.Equal(new[] { Math.Round(T - 0.25, 6) }, DoorDepths(m));
                    Assert.NotEqual(DoorDepths(plain), DoorDepths(m));
                    var thr = Enumerable.Range(0, m.Verts).Where(v => m.C[v].slot == ColorSlot.Foundation && m.N[v].y > 0.9 && Math.Abs(m.P[v].y) < 1e-9).Select(v => -m.P[v].z).ToList();
                    Assert.Contains(thr, w => Math.Abs(w - (T - 0.25)) < 1e-9); Assert.Contains(thr, w => Math.Abs(w - T) < 1e-9);
                    var glass = Enumerable.Range(0, m.Verts).Where(v => m.C[v].slot == ColorSlot.Glass).Select(v => m.P[v]).ToList();
                    Assert.Equal(Enumerable.Range(0, plain.Verts).Where(v => plain.C[v].slot == ColorSlot.Glass).Select(v => plain.P[v]).ToList(), glass);
                }
            }
            // an artist's door from the entry is pushed back as far
            var door = OpeningKindTestsKind("t-recess-entry-door", true);
            OpeningKinds.Register(door);
            try
            {
                var (_, b, site) = Block(st => { st.doorRecess = 0.05; OwnDoor(st, 0.2, door.id); });
                var op = Lod0.Build(site, b).Op;
                Assert.Equal(-T + 0.05 + 0.2, Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Door && op.P[v].z < 0).Min(v => op.P[v].z), 6);
            }
            finally { OpeningKinds.Remove(door.id); }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DoorsWithRecessesOfTheirOwnBuildWithoutFacesThatFlicker(bool interior)
        {
            var door = OpeningKindTestsKind("t-recess-entry-flicker", true);
            OpeningKinds.Register(door);
            try
            {
                foreach (double? style in new double?[] { null, 0.1 })
                    foreach (double r in new[] { 0.0, 0.08, 0.29 })
                        foreach (string? kind in new[] { null, door.id })
                        {
                            var (_, b, site) = Block(st => { st.recess = 0.1; st.doorRecess = style; OwnDoor(st, r, kind); }, interior);
                            var lod0 = Lod0.Build(site, b, solids: true);
                            var rep = CoplanarCheck.Run(new[] { lod0.Op, lod0.Glass });
                            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
                            foreach (bool recessed in new[] { false, true })
                            {
                                var r1 = CoplanarCheck.Run(Lod1.Build(site, b, solids: true, recessed: recessed));
                                Assert.True(r1.Overlaps.Count == 0, string.Join("\n", r1.Overlaps.Take(10)));
                            }
                        }
            }
            finally { OpeningKinds.Remove(door.id); }
        }

        [Fact]
        public void AnEntrysRecessRoundTripsAndBakes()
        {
            var (d, b, _) = Block(st => st.doorSet = new List<OpeningEntry> { new OpeningEntry { recess = 0.22 }, new OpeningEntry { weight = 2 } });
            var json = PrototypeJson.WriteStyle(b.style);
            var back = PrototypeJson.ReadStyle(json);
            Assert.Equal(new double?[] { 0.22, null }, back.doorSet!.Select(e => e.recess));
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"recess\""));   // written only where set
            Assert.Equal(json, PrototypeJson.WriteStyle(BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style));
            // a set of one door with a recess of its own stays a set; without it, it is the style's one door again
            var st = Styles.Preset("brick");
            WindowSets.Recess(st, SetOf.Doors, 0, 0.4);
            Assert.Equal(0.29, st.doorSet![0].recess);
            WindowSets.Recess(st, SetOf.Doors, 0, null);
            Assert.Null(st.doorSet);
        }

        /// <summary>Recessed windows and doors on every building of the prototype's corpora, at LOD0 and LOD1, with nothing that flickers.</summary>
        [Theory]
        [InlineData("demo.json")]
        [InlineData("variants.json")]
        public void EveryFixtureBuildingTakesItCleanly(string file)
        {
            var doc = PrototypeJson.Read(Fixtures.Text(file)).Document;
            foreach (var st in StyleColorFields.Styles(doc)) { st.recess = 0.08; st.doorRecess = 0.25; }
            var site = new Site(doc.buildings);
            var bad = new List<string>();
            foreach (var b in doc.buildings)
            {
                var l0 = Lod0.Build(site, b, solids: true);
                foreach (var r in new[] { CoplanarCheck.Run(new[] { l0.Op, l0.Glass }), CoplanarCheck.Run(Lod1.Build(site, b, solids: true)) })
                    if (r.Overlaps.Count > 0) bad.Add($"{b.name}: {string.Join("; ", r.Overlaps.Take(3))}");
            }
            Assert.True(bad.Count == 0, string.Join("\n", bad.Take(10)));
        }
    }
}
