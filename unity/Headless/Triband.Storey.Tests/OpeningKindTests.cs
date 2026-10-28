using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Artist-made windows and doors (<see cref="OpeningKinds"/>, docs/EDITOR.md §6.9). Storey's own.</summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class OpeningKindTests
    {
        /// <summary>A box in the opening's own space, as two parts: a frame-coloured slab and a pane (or a leaf).</summary>
        static OpeningKind Kind(string id, bool door, bool lod1 = false)
        {
            var m = new OpeningKind.Mesh();
            var P = new List<P3>(); var N = new List<P3>();
            int Q(P3 a, P3 b, P3 c, P3 d, P3 n) { int s = P.Count; P.AddRange(new[] { a, b, c, d }); N.AddRange(new[] { n, n, n, n }); return s; }
            var f = new P3(0, 0, 1);
            // a frame strip along the top, standing out 3 cm, and the pane 5 cm back in the reveal; front faces as a baked
            // kind's (KindBake), turned to the street, so they cover the hole (OpeningKind.lod0Covers)
            int s0 = Q(new P3(-0.5, 1.4, 0.03), new P3(0.5, 1.4, 0.03), new P3(0.5, 1.5, 0.03), new P3(-0.5, 1.5, 0.03), f);
            int s1 = Q(new P3(-0.5, 0, -0.05), new P3(0.5, 0, -0.05), new P3(0.5, 1.4, -0.05), new P3(-0.5, 1.4, -0.05), f);
            int[] Tri(int s) { var t = new[] { s, s + 1, s + 2, s, s + 2, s + 3 }; return t; }
            m.P = P.ToArray(); m.N = N.ToArray();
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = Tri(s0) });
            m.parts.Add(new OpeningKind.Part { slot = door ? ColorSlot.Door : ColorSlot.Glass, leaf = door, tris = Tri(s1) });
            return new OpeningKind { id = id, name = id, door = door, w = 1, h = 1.5, lod0 = m, lod1 = lod1 ? m : null };
        }

        static (BuildingData b, Site site) Block(bool interior, Action<FacadeStyle>? f = null)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.interior = interior; b.style = Styles.Preset("brick").Clone(); b.style.ground = GroundType.Match;
            f?.Invoke(b.style);
            return (b, new Site(d.buildings));
        }

        static int Count(MeshBuilder m, ColorSlot s) => Enumerable.Range(0, m.Verts).Count(i => m.C[i].slot == s);

        [Fact]
        public void AShellStoreyClosesTheHoleAnArtistsWindowLeavesOpen()
        {
            // a pane over the lower half of its hole only, as an arched window leaves its top corners open
            var m = new OpeningKind.Mesh();
            var f = new P3(0, 0, 1);
            m.P = new[] { new P3(-0.5, 0, -0.05), new P3(0.5, 0, -0.05), new P3(0.5, 0.75, -0.05), new P3(-0.5, 0.75, -0.05) };
            m.N = new[] { f, f, f, f };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            OpeningKinds.Register(new OpeningKind { id = "t-half", name = "t-half", w = 1, h = 1.5, lod0 = m });
            OpeningKinds.Register(Kind("t-whole", false));
            Assert.False(OpeningKinds.Get("t-half")!.lod0Covers); Assert.True(OpeningKinds.Get("t-whole")!.lod0Covers);
            // a shell storey: each half-covered window has the plain pane behind it, where its reveal ends; a whole one none
            var (bh, sh) = Block(false, st => st.windowKind = "t-half"); var half = Lod0.Build(sh, bh).Op;
            var (bw, sw) = Block(false, st => st.windowKind = "t-whole"); var whole = Lod0.Build(sw, bw).Op;
            int windows = Count(half, ColorSlot.Frame) / 4;
            Assert.True(windows > 0);
            Assert.Equal(4 * windows, Count(half, ColorSlot.Glass));
            Assert.Equal(Count(whole, ColorSlot.Frame), Count(whole, ColorSlot.Glass));   // its own glass, no plain pane
            Assert.Empty(CoplanarCheck.Run(half).Overlaps);
        }

        [Fact]
        public void AnArtistsWindowFillsEveryWindow()
        {
            OpeningKinds.Register(Kind("t-win", false));
            var (b0, s0) = Block(false); var plain = Lod0.Build(s0, b0).Op;
            var (b, s) = Block(false, st => st.windowKind = "t-win");
            var r = Lod0.Build(s, b);
            int frames = Count(r.Op, ColorSlot.Frame);
            Assert.True(frames > 0 && frames % 4 == 0, "the frame part, four corners a window");
            // every pane of the artist's window carries the window shader's pane coordinates (a shell building)
            var glass = Enumerable.Range(0, r.Op.Verts).Where(i => r.Op.C[i].slot == ColorSlot.Glass).ToList();
            Assert.NotEmpty(glass); Assert.All(glass, i => Assert.True(r.Op.PaneUV.ContainsKey(i)));
            Assert.Equal(frames, glass.Count);   // one pane per frame
            // and no generated glazing: the plain building's panes are gone (as many windows, each now the artist's)
            Assert.Equal(Count(plain, ColorSlot.Glass), glass.Count);
            var rep = CoplanarCheck.Run(r.Op);
            Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
        }

        [Fact]
        public void ItStretchesToTheOpeningAndFacesOut()
        {
            OpeningKinds.Register(Kind("t-win2", false));
            var (b, s) = Block(false, st => st.windowKind = "t-win2");
            var op = Lod0.Build(s, b).Op;
            // the frame strip stands 3 cm proud of the wall: outside the footprint; the pane is inside it
            var fr = Enumerable.Range(0, op.Verts).Where(i => op.C[i].slot == ColorSlot.Frame).Select(i => op.P[i]).ToList();
            Assert.All(fr, p => Assert.False(Geo.Pip(b.footprint, p.x, p.z) && Geo.DistToEdges(b.footprint, new Vec2(p.x, p.z)) > 0.04, $"the frame at {p} is not out of the wall"));
            // a window is as wide as the style's windows
            var grp = fr.GroupBy(p => (Math.Round(p.y, 1), Math.Round(p.z / 3), Math.Round(p.x / 3))).First().ToList();
            double span = Math.Max(grp.Max(p => p.x) - grp.Min(p => p.x), grp.Max(p => p.z) - grp.Min(p => p.z));
            Assert.InRange(span, 0.5, 2.5);
        }

        [Fact]
        public void ADoorsLeafOnlyWhereTheDoorwayIsClosed()
        {
            OpeningKinds.Register(Kind("t-door", true));
            var (bw, sw) = Block(true, st => st.doorKind = "t-door");
            Assert.Equal(0, Count(Lod0.Build(sw, bw).Op, ColorSlot.Door));     // walk-in: the doorway stays open
            var (bs, ss) = Block(false, st => st.doorKind = "t-door");
            Assert.Equal(4, Count(Lod0.Build(ss, bs).Op, ColorSlot.Door));     // shell: the leaf shows
        }

        [Fact]
        public void WalkInGlassIsSeeThroughBothWays()
        {
            OpeningKinds.Register(Kind("t-win3", false));
            var (b, s) = Block(true, st => st.windowKind = "t-win3");
            var r = Lod0.Build(s, b);
            Assert.True(Count(r.Glass, ColorSlot.Glass) > 0);
            Assert.Equal(0, Count(r.Glass, ColorSlot.Glass) % 8);   // both faces
        }

        [Fact]
        public void AnUnknownKindBuildsTheStandardWindow()
        {
            var (b0, s0) = Block(false); int plain = Lod0.Build(s0, b0).Op.Tris;
            var (b, s) = Block(false, st => { st.windowKind = "not-there"; st.doorKind = "nor-this"; });
            Assert.Equal(plain, Lod0.Build(s, b).Op.Tris);
        }

        [Fact]
        public void LodOneTakesItsOwnMeshOrThePlainPane()
        {
            OpeningKinds.Register(Kind("t-lod1", false, lod1: true));
            OpeningKinds.Register(Kind("t-nolod1", false));
            var (b, s) = Block(false, st => st.windowKind = "t-lod1");
            Assert.True(Count(Lod1.Build(s, b), ColorSlot.Frame) > 0);
            var (b2, s2) = Block(false, st => st.windowKind = "t-nolod1");
            Assert.Equal(0, Count(Lod1.Build(s2, b2), ColorSlot.Frame));
        }

        const string ShutterColour = "#3a5f2e";

        /// <summary>
        /// A LOD1 of shutters 40 cm wide either side of a 1 × 1.5 hole, <paramref name="out"/> from the wall, in a colour of their
        /// own; and a quad over the hole at <paramref name="z"/> (glass, or a frame strip along its top) if asked.
        /// </summary>
        static OpeningKind.Mesh Shutters(double? z = null, bool glass = true, double @out = 0.02)
        {
            var m = new OpeningKind.Mesh();
            var P = new List<P3>(); var f = new P3(0, 0, 1);
            int Q(double x0, double x1, double y0, double y1, double d) { int at = P.Count; P.AddRange(new[] { new P3(x0, y0, d), new P3(x1, y0, d), new P3(x1, y1, d), new P3(x0, y1, d) }); return at; }
            int[] Tri(params int[] at) => at.SelectMany(q => new[] { q, q + 1, q + 2, q, q + 2, q + 3 }).ToArray();
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, own = ShutterColour, tris = Tri(Q(-0.95, -0.55, 0, 1.5, @out), Q(0.55, 0.95, 0, 1.5, @out)) });
            if (z is double d) m.parts.Add(new OpeningKind.Part { slot = glass ? ColorSlot.Glass : ColorSlot.Frame, tris = Tri(glass ? Q(-0.5, 0.5, 0, 1.5, d) : Q(-0.5, 0.5, 1.4, 1.5, d)) });
            m.P = P.ToArray(); m.N = Enumerable.Repeat(f, P.Count).ToArray();
            return m;
        }

        /// <summary>LOD1 of a block whose windows are kind <paramref name="lod1"/>'s, and how many windows that is (the shutters it placed).</summary>
        static (MeshBuilder lod1, int windows) Lod1Of(string id, OpeningKind.Mesh? lod1)
        {
            var k = Kind(id, false); k.lod1 = lod1;
            OpeningKinds.Register(k);
            var (b, s) = Block(false, st => st.windowKind = id);
            var m = Lod1.Build(s, b);
            var own = KindColors.SlotOf(ShutterColour)!.Value;
            return (m, Count(m, own) / 8);
        }

        // the glass's vertices in flat panes on the wall (the shader draws their recess), and the rest
        static (int flat, int other) Glass(MeshBuilder m)
        {
            int f = 0, o = 0;
            for (int i = 0; i < m.Verts; i++)
                if (m.C[i].slot == ColorSlot.Glass) { if (m.PaneUV.TryGetValue(i, out var p) && p.flat) f++; else o++; }
            return (f, o);
        }

        [Fact]
        public void ALod1InFrontOfTheWallLiesOnTheFlatWall()
        {
            var (none, _) = Lod1Of("t-lod1-none", null);
            var plain = Glass(none);
            // shutters only: on the flat wall, with the plain pane, and no cut (docs/OPENINGS.md §5)
            var (front, n) = Lod1Of("t-lod1-front", Shutters());
            var k = OpeningKinds.Get("t-lod1-front")!;
            Assert.True(k.lod1OnWall); Assert.False(k.lod1Covers);
            Assert.True(n > 20, $"{n} windows");
            Assert.Equal(4 * n, plain.flat);
            Assert.Equal(none.Tris + 4 * n, front.Tris);   // its four triangles a window, and nothing for a cut
            Assert.Equal(plain, Glass(front));
            // glass of its own standing 5 cm out, in front of where the plain pane goes: no cut, and no plain pane either
            var (proud, n2) = Lod1Of("t-lod1-proud", Shutters(z: 0.05));
            k = OpeningKinds.Get("t-lod1-proud")!;
            Assert.True(k.lod1OnWall); Assert.True(k.lod1Covers);
            Assert.Equal(n, n2);
            Assert.Equal(none.Tris - 2 * n + 6 * n, proud.Tris);
            Assert.Equal((0, plain.other + 4 * n), Glass(proud));
        }

        [Fact]
        public void ALod1ReachingIntoTheWallCutsIt()
        {
            var (none, _) = Lod1Of("t-lod1-none2", null);
            var plain = Glass(none);
            // glass 5 cm into the wall: the wall is cut round every window, as before, and its glass fills the hole
            var (deep, n) = Lod1Of("t-lod1-deep", Shutters(z: -0.05));
            var k = OpeningKinds.Get("t-lod1-deep")!;
            Assert.False(k.lod1OnWall); Assert.True(k.lod1Covers);
            Assert.True(deep.Tris - (none.Tris + 4 * n) >= 8 * n, $"{deep.Tris} against {none.Tris} for {n} windows: the cuts");
            Assert.Equal((0, plain.other + 4 * n), Glass(deep));   // its own glass, not the plain pane
            // flush glass, 1 cm out over the hole: it would flicker against a flat wall behind it, so that is cut too
            var (flush, nf) = Lod1Of("t-lod1-flush", Shutters(z: 0.01));
            k = OpeningKinds.Get("t-lod1-flush")!;
            Assert.False(k.lod1OnWall); Assert.True(k.lod1Covers);
            Assert.Equal(deep.Tris, flush.Tris); Assert.Equal(n, nf);
            // a frame strip 5 cm in and no glass: cut, and the plain pane fills the hole it leaves open, in the reveal
            var (open, n2) = Lod1Of("t-lod1-open", Shutters(z: -0.05, glass: false));
            k = OpeningKinds.Get("t-lod1-open")!;
            Assert.False(k.lod1OnWall); Assert.False(k.lod1Covers);
            Assert.Equal(n, n2);
            Assert.Equal((0, plain.other + 4 * n), Glass(open));
        }

        [Fact]
        public void CheckOnABuildingLooksAtLod1Too()
        {
            var k = Kind("t-lod1-trial", false); k.lod1 = Shutters(); OpeningKinds.Register(k);
            var t = KindTrial.Run(k);
            Assert.True(t.overlaps.Count == 0, string.Join("\n", t.overlaps.Take(5)));
            // shutters lying on the wall's face: at LOD1, on the flat wall, they flicker against it
            var flat = Kind("t-lod1-trial-flat", false); flat.lod1 = Shutters(@out: 0); OpeningKinds.Register(flat);
            Assert.NotEmpty(KindTrial.Run(flat).overlaps);
        }

        [Fact]
        public void ATriangleNamingAPointTheMeshHasntIsLeftOut()
        {
            // an artist's triangle with an index past the mesh's points, or a part ending in a partial triangle, is left out
            // whole; the rest go in. Such a triangle was kept, pointing at the next part's first vertex or past the mesh
            var k = Kind("t-bad", false);
            k.lod0.parts[0].tris = k.lod0.parts[0].tris.Concat(new[] { 0, 1, 99, 2, 3 }).ToArray();
            OpeningKinds.Register(k);
            var (b, s) = Block(true, st => st.windowKind = "t-bad");
            var r = Lod0.Build(s, b);
            foreach (var m in new[] { r.Op, r.Glass })
            {
                Assert.Equal(0, m.I.Count % 3);
                Assert.All(m.I, i => Assert.InRange(i, 0, m.Verts - 1));
            }
            Assert.True(Count(r.Op, ColorSlot.Frame) > 0 && Count(r.Op, ColorSlot.Frame) % 4 == 0, "each frame strip's four corners, and nothing of the bad triangles");
        }

        [Fact]
        public void TheStyleKeepsItsKinds()
        {
            var st = Styles.Preset("brick").Clone(); st.windowKind = "abc"; st.doorKind = "def";
            var back = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(st));
            Assert.Equal("abc", back.windowKind); Assert.Equal("def", back.doorKind);
            var none = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(Styles.Preset("brick")));
            Assert.Null(none.windowKind);
        }
    }

    /// <summary>
    /// What an artist's window costs a build (docs/PERFORMANCE.md §8.1), counted on this thread: on its own, as no other test
    /// may take the chunks the builds give back meanwhile. Its kind's id is its own, so the registry needs no other guard.
    /// </summary>
    [Collection(TimingCollection.Name)]
    public class OpeningKindGarbageTests
    {
        readonly Xunit.Abstractions.ITestOutputHelper output;
        public OpeningKindGarbageTests(Xunit.Abstractions.ITestOutputHelper o) { output = o; }

        /// <summary>A 100-vertex window in two parts: a frame of 19 small quads round a pane of 6.</summary>
        static OpeningKind Kind100(string id)
        {
            var m = new OpeningKind.Mesh(); var P = new List<P3>(); var N = new List<P3>(); var f = new P3(0, 0, 1);
            int[] Quads(int n, double x0, double y0, double w, double h, double z)
            {
                var t = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    double x = x0 + w * i / n, x1 = x0 + w * (i + 1) / n; int s = P.Count;
                    P.AddRange(new[] { new P3(x, y0, z), new P3(x, y0 + h, z), new P3(x1, y0 + h, z), new P3(x1, y0, z) }); N.AddRange(new[] { f, f, f, f });
                    t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
                }
                return t.ToArray();
            }
            var frame = Quads(19, -0.5, 1.4, 1, 0.1, 0.03); var pane = Quads(6, -0.5, 0, 1, 1.4, -0.05);
            m.P = P.ToArray(); m.N = N.ToArray();
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = frame });
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, tris = pane });
            return new OpeningKind { id = id, name = id, w = 1, h = 1.5, lod0 = m, lod1 = m };
        }

        [Fact]
        public void AnArtistsWindowMakesNoGarbageWherePlaced() => NoGarbage(Kind100("t-win100"));

        [Fact]
        public void ASlicedWindowMakesNoGarbageWherePlaced()
        {
            // sliced (docs/OPENINGS.md §4.3), with borders and its frame keeping its shape: the same, nothing a placement
            var k = Kind100("t-win100-sliced"); k.slice = true; k.borderL = k.borderR = k.borderB = k.borderT = 0.06;
            k.lod0.parts[0].anchor = new P3(0, 1.45, 0.03);
            NoGarbage(k);
        }

        /// <summary>A set of the artist's window, the generator's and none, picked window by window (docs/OPENINGS.md §7.1): no garbage a window either.</summary>
        [Fact]
        public void AWindowSetMakesNoGarbageWherePicked() => NoGarbage(Kind100("t-win100-set"), set: true);

        void NoGarbage(OpeningKind kind, bool set = false)
        {
            // docs/PERFORMANCE.md §8.1: a kind's parts are made ready once, and each placement goes straight into the builder.
            // Placing one made two arrays of its points, and for each part a map, two lists and their copies (walk-in
            // glass two more): 7.2 MB a plain LOD0 build with a 100-vertex window
            OpeningKinds.Register(kind);
            foreach (bool walkIn in new[] { false, true })
            {
                var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 120, walkIn);
                var site = new Site(d.buildings); var some = d.buildings.Where((x, i) => i % 4 == 0).ToList();
                (double ms, long bytes) Run(bool artist, int lod)
                {
                    foreach (var b in some)
                        if (set) b.style.windowSet = artist ? new List<OpeningEntry> { new OpeningEntry { kind = kind.id, weight = 2 }, new OpeningEntry(), new OpeningEntry { none = true } } : null;
                        else b.style.windowKind = artist ? kind.id : null;
                    long bytes = 0; var sw = new System.Diagnostics.Stopwatch();
                    for (int pass = 0; pass < 2; pass++)   // the first warms the pools
                        foreach (var b in some)
                        {
                            long a = GC.GetAllocatedBytesForCurrentThread(); if (pass == 1) sw.Start();
                            if (lod == 0) { var r = Lod0.Build(site, b); if (pass == 1) sw.Stop(); r.Op.Recycle(); r.Glass.Recycle(); }
                            else { var m = Lod1.Build(site, b); if (pass == 1) sw.Stop(); m.Recycle(); }
                            if (pass == 1) bytes += GC.GetAllocatedBytesForCurrentThread() - a;
                        }
                    return (sw.Elapsed.TotalMilliseconds / some.Count, bytes / some.Count);
                }
                foreach (int lod in new[] { 0, 1 })
                {
                    var plain = Run(false, lod); var artist = Run(true, lod);
                    output.WriteLine($"{(walkIn ? "walk-in" : "plain")} LOD{lod}, a build: the generator's windows {plain.ms:0.0} ms, {plain.bytes / 1024.0:N0} KB; an artist's 100-vertex window {artist.ms:0.0} ms, {artist.bytes / 1024.0:N0} KB");
                    Assert.True(artist.bytes < plain.bytes * 1.5 + 64 * 1024, $"{(walkIn ? "walk-in" : "plain")} LOD{lod}: {artist.bytes:N0} bytes a build with the artist's window, {plain.bytes:N0} with the generator's");
                }
                foreach (var b in some) { b.style.windowKind = null; b.style.windowSet = null; }
            }
        }
    }
}
