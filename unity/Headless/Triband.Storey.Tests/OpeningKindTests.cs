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
            // a frame strip along the top, standing out 3 cm, and the pane 5 cm back in the reveal; front faces as the generator's
            int s0 = Q(new P3(-0.5, 1.4, 0.03), new P3(-0.5, 1.5, 0.03), new P3(0.5, 1.5, 0.03), new P3(0.5, 1.4, 0.03), f);
            int s1 = Q(new P3(-0.5, 0, -0.05), new P3(-0.5, 1.4, -0.05), new P3(0.5, 1.4, -0.05), new P3(0.5, 0, -0.05), f);
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
        public void AnArtistsWindowMakesNoGarbageWherePlaced()
        {
            // docs/PERFORMANCE.md §8.1: a kind's parts are made ready once, and each placement goes straight into the builder.
            // Placing one made two arrays of its points, and for each part a map, two lists and their copies (walk-in
            // glass two more): 7.2 MB a plain LOD0 build with a 100-vertex window
            OpeningKinds.Register(Kind100("t-win100"));
            foreach (bool walkIn in new[] { false, true })
            {
                var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 120, walkIn);
                var site = new Site(d.buildings); var some = d.buildings.Where((x, i) => i % 4 == 0).ToList();
                (double ms, long bytes) Run(bool artist, int lod)
                {
                    foreach (var b in some) b.style.windowKind = artist ? "t-win100" : null;
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
                foreach (var b in some) b.style.windowKind = null;
            }
        }
    }
}
