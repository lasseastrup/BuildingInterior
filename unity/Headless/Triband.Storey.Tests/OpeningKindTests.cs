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
        public void TheStyleKeepsItsKinds()
        {
            var st = Styles.Preset("brick").Clone(); st.windowKind = "abc"; st.doorKind = "def";
            var back = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(st));
            Assert.Equal("abc", back.windowKind); Assert.Equal("def", back.doorKind);
            var none = PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(Styles.Preset("brick")));
            Assert.Null(none.windowKind);
        }
    }
}
