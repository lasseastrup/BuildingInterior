using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// What an artist's window, door or facade detail costs where it is placed (<see cref="KindPart"/>): no vertex twice as
    /// uploaded, no triangle without area, the bake's cuts joined at the kind's own size, and what a recess buries in a
    /// whole wall left out; and the surface it shows the same, plane by plane and colour by colour. Against two of the
    /// converted scenes' kinds (kinds-converted.json) and one baked here. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class KindTopologyTests
    {
        const double T = Dim.T_EXT;
        readonly ITestOutputHelper output;
        public KindTopologyTests(ITestOutputHelper o) { output = o; }

        // ---- the kinds ----

        /// <summary>The converted scenes' kinds, by name: each number the float it was.</summary>
        static Dictionary<string, OpeningKind> Converted()
        {
            static double F(JsonElement e) => (double)float.Parse(e.GetRawText(), CultureInfo.InvariantCulture);
            static P3[] Ps(JsonElement e) => e.EnumerateArray().Select(p => new P3(F(p[0]), F(p[1]), F(p[2]))).ToArray();
            var o = new Dictionary<string, OpeningKind>();
            foreach (var k in JsonDocument.Parse(Fixtures.Text("kinds-converted.json")).RootElement.GetProperty("openings").EnumerateArray())
            {
                var l0 = k.GetProperty("lod0"); var m = new OpeningKind.Mesh { P = Ps(l0.GetProperty("P")), N = Ps(l0.GetProperty("N")) };
                foreach (var p in l0.GetProperty("parts").EnumerateArray())
                    m.parts.Add(new OpeningKind.Part
                    {
                        slot = Enum.Parse<ColorSlot>(p.GetProperty("slot").GetString()!), own = p.TryGetProperty("own", out var own) ? own.GetString() : null,
                        tris = p.GetProperty("tris").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
                    });
                var kind = new OpeningKind
                {
                    id = "converted-" + k.GetProperty("id").GetString(), name = k.GetProperty("name").GetString()!, door = k.GetProperty("door").GetBoolean(),
                    w = F(k.GetProperty("w")), h = F(k.GetProperty("h")), stretch = k.GetProperty("stretch").GetBoolean(), slice = k.GetProperty("slice").GetBoolean(),
                    borderL = F(k.GetProperty("borderL")), borderR = F(k.GetProperty("borderR")), borderB = F(k.GetProperty("borderB")), borderT = F(k.GetProperty("borderT")),
                    lod0 = m,
                };
                o[kind.name] = kind;
            }
            return o;
        }

        static OpeningKind Window => Converted()["Visual_BuildingPiece08_Single (window)"];
        static OpeningKind Door => Converted()["Visual_BuildingPiece09_DoubleDoor (door)"];

        /// <summary>
        /// A window 1 × 1.4 m baked here as the conversion bakes one (<see cref="KindBake"/>, its hole's edges the slice
        /// lines): a frame of two materials Storey draws in one colour (its head split between them), a flange on the wall's
        /// face round the hole, a sill standing out, and a pane with a sliver of float noise along its side.
        /// </summary>
        static OpeningKind Baked(string id)
        {
            var src = new List<KindBake.Source>
            {
                Box(-0.58, 1.4, -0.06, 0, 1.5, 0.04, "frameA"), Box(0, 1.4, -0.06, 0.58, 1.5, 0.04, "frameB"),
                Box(-0.58, 0, -0.06, -0.5, 1.4, 0.04, "frameA"), Box(0.5, 0, -0.06, 0.58, 1.4, 0.04, "frameB"),
                Box(-0.62, -0.08, -0.06, 0.62, 0, 0.07, "frameB"),
                Box(-0.66, -0.12, 0, -0.58, 1.56, 0.005, "frameA"), Box(0.58, -0.12, 0, 0.66, 1.56, 0.005, "frameA"), Box(-0.58, 1.5, 0, 0.58, 1.56, 0.005, "frameA"),
            };
            var pane = new KindBake.Source
            {
                P = new[] { new P3(-0.5, 0, -0.02), new P3(0.5, 0, -0.02), new P3(0.5, 1.4, -0.02), new P3(-0.5 + 1e-6, 0.7, -0.02), new P3(-0.5, 1.4, -0.02) },
                N = Enumerable.Repeat(new P3(0, 0, 1), 5).ToArray(),
            };
            pane.subs.Add(new KindBake.Sub { tris = new[] { 0, 1, 2, 0, 2, 3, 3, 2, 4, 0, 3, 4 }, key = "glass" });
            src.Add(pane);
            var r = KindBake.Bake(src, new KindBake.Hole(0, 0.7, 1, 1.4), false, new KindBake.Cuts(1, 1.4, 0, 0, 0, 0));
            // the two frame materials one part, as the conversion gives a part each colour
            var m = new OpeningKind.Mesh { P = r.P.ToArray(), N = r.N.ToArray() };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, tris = r.parts.Where(p => p.key != "glass").SelectMany(p => p.tris).ToArray() });
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, tris = r.parts.Where(p => p.key == "glass").SelectMany(p => p.tris).ToArray() });
            return new OpeningKind { id = id, name = id, w = 1, h = 1.4, stretch = false, lod0 = m };
        }

        /// <summary>A box, its six faces out, four points a face (as KindBakeTests' own).</summary>
        static KindBake.Source Box(double x0, double y0, double z0, double x1, double y1, double z1, string key)
        {
            var P = new List<P3>(); var N = new List<P3>(); var t = new List<int>();
            void Q(P3 a, P3 b, P3 c, P3 d, P3 n)
            {
                if (P3.Dot(P3.Cross(b - a, c - a), n) < 0) (b, d) = (d, b);
                int s = P.Count; P.AddRange(new[] { a, b, c, d }); N.AddRange(new[] { n, n, n, n });
                t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            Q(new P3(x0, y0, z1), new P3(x1, y0, z1), new P3(x1, y1, z1), new P3(x0, y1, z1), new P3(0, 0, 1));
            Q(new P3(x0, y0, z0), new P3(x1, y0, z0), new P3(x1, y1, z0), new P3(x0, y1, z0), new P3(0, 0, -1));
            Q(new P3(x0, y0, z0), new P3(x0, y0, z1), new P3(x0, y1, z1), new P3(x0, y1, z0), new P3(-1, 0, 0));
            Q(new P3(x1, y0, z0), new P3(x1, y0, z1), new P3(x1, y1, z1), new P3(x1, y1, z0), new P3(1, 0, 0));
            Q(new P3(x0, y1, z0), new P3(x1, y1, z0), new P3(x1, y1, z1), new P3(x0, y1, z1), new P3(0, 1, 0));
            Q(new P3(x0, y0, z0), new P3(x1, y0, z0), new P3(x1, y0, z1), new P3(x0, y0, z1), new P3(0, -1, 0));
            var s = new KindBake.Source { P = P.ToArray(), N = N.ToArray() };
            s.subs.Add(new KindBake.Sub { tris = t.ToArray(), key = key });
            return s;
        }

        static void With(OpeningKind k, Action body)
        {
            OpeningKinds.Register(k);
            try { body(); } finally { OpeningKinds.Remove(k.id); }
        }

        // ---- placing one, as made and as the generator does ----

        static readonly Frame Wall = new Frame(new Vec2(0, 0), new Vec2(1, 0), new Vec2(0, 1));
        static readonly Palette C = new Palette(new StyleRef(0, 0));

        /// <summary>Where an opening of the kind starts over the floor: a window's sill, a door's foot.</summary>
        static double Sill(OpeningKind k) => k.door ? 0 : 0.9;

        /// <summary>
        /// The kind in a shell storey's opening W × H (from its <see cref="Sill"/> up) on a wall along +X facing +Z (its outer
        /// face at z = T), pushed <paramref name="back"/> into a wall whole <paramref name="deep"/> in.
        /// </summary>
        static MeshBuilder Placed(OpeningKind k, double W, double H, double back = 0, double deep = 0)
        {
            var m = MeshBuilder.Make();
            OpeningKinds.Place(m, null, Wall, k, k.lod0, -W / 2, W / 2, Sill(k), Sill(k) + H, C, true, back, deep);
            return m;
        }

        /// <summary>The same as made: every point of the mesh a vertex, every triangle as it is, as a placement was before.</summary>
        static MeshBuilder AsMade(OpeningKind k, double W, double H, double back = 0)
        {
            var m = MeshBuilder.Make();
            var at = OpeningKinds.At(new Frame(new Vec2(0, -back), Wall.u, Wall.w), k, -W / 2, W / 2, Sill(k), Sill(k) + H);
            foreach (var part in k.lod0.parts)
            {
                var c = part.slot == ColorSlot.Glass ? C.glassDark : new Swatch(C.style, part.ownSlot ?? part.slot);
                int bse = m.Verts;
                for (int i = 0; i < k.lod0.P.Length; i++) m.V(at.Point(k.lod0.P[i]), at.Normal(k.lod0.P[i], k.lod0.N[i]), c);
                foreach (int i in part.tris) m.I.Add(bse + i);
            }
            return m;
        }

        // ---- what a mesh shows ----

        /// <summary>
        /// The surface a mesh shows: by colour and packed normal, each plane's area (by its triangles' middles; planes a tenth of
        /// a millimetre apart are one), the triangles <paramref name="leave"/> picks left out.
        /// </summary>
        static Dictionary<string, List<(double d, double area)>> Surface(MeshBuilder m, Func<P3, P3, P3, P3, bool>? leave = null)
        {
            var o = new Dictionary<string, List<(double d, double area)>>();
            for (int t = 0; t < m.I.Count; t += 3)
            {
                P3 a = m.P[m.I[t]], b = m.P[m.I[t + 1]], c = m.P[m.I[t + 2]];
                var cr = P3.Cross(b - a, c - a); double l = cr.Length; if (l < 1e-12) continue;
                var n = cr * (1 / l);
                if (leave != null && leave(a, b, c, n)) continue;
                string key = $"{m.C[m.I[t]]}|{MeshPack.S8(n.x)},{MeshPack.S8(n.y)},{MeshPack.S8(n.z)}";
                if (!o.TryGetValue(key, out var planes)) o[key] = planes = new List<(double, double)>();
                planes.Add((P3.Dot(n, (a + b + c) * (1.0 / 3)), l / 2));
            }
            return o;
        }

        /// <summary>
        /// Each plane (planes a tenth of a millimetre apart in either one) where <paramref name="a"/>'s area isn't between
        /// <paramref name="low"/>'s and <paramref name="high"/>'s, to a millionth.
        /// </summary>
        static List<string> Outside(Dictionary<string, List<(double d, double area)>> a, Dictionary<string, List<(double d, double area)>> low, Dictionary<string, List<(double d, double area)>> high)
        {
            var o = new List<string>();
            foreach (var key in a.Keys.Union(low.Keys).Union(high.Keys))
            {
                var all = new List<(double d, double area, int which)>();
                foreach (var (m, which) in new[] { (a, 0), (low, 1), (high, 2) }) if (m.TryGetValue(key, out var l)) foreach (var q in l) all.Add((q.d, q.area, which));
                all.Sort((p, q) => p.d.CompareTo(q.d));
                var sum = new double[3]; double at = double.NaN, last = double.NegativeInfinity;
                void Flush()
                {
                    double tol = 1e-9 + 1e-6 * sum[2];
                    if (sum[0] < sum[1] - tol || sum[0] > sum[2] + tol) o.Add($"{key} at {at:0.####}: {sum[0]:E4} m², not {sum[1]:E4} to {sum[2]:E4}");
                    Array.Clear(sum, 0, 3);
                }
                foreach (var (d, area, which) in all)
                {
                    if (d - last >= 1e-4) { if (!double.IsNaN(at)) Flush(); at = d; }
                    sum[which] += area; last = d;
                }
                if (!double.IsNaN(at)) Flush();
            }
            return o;
        }

        /// <summary>Vertices the upload can't tell apart from one before them (as <see cref="MeshPack"/> packs them, every attribute).</summary>
        static int Twice(IEnumerable<PackedMesh> ms)
        {
            int n = 0;
            foreach (var m in ms)
            {
                var seen = new HashSet<string>();
                for (int i = 0; i < m.Verts; i++)
                {
                    var v = m.V[i]; var c = m.Cut != null ? m.Cut[i] : default;
                    if (!seen.Add($"{v.x:R},{v.y:R},{v.z:R}|{v.nx},{v.ny},{v.nz},{v.nw}|{v.r},{v.g},{v.b},{v.a}|{v.tag:R}|{c.kind:R},{c.wid:R}")) n++;
                }
            }
            return n;
        }

        /// <summary>Triangles with no area as uploaded (their corners' floats in one line).</summary>
        static int NoArea(IEnumerable<PackedMesh> ms)
        {
            int n = 0;
            foreach (var m in ms)
                for (int t = 0; t < m.Indices; t += 3)
                {
                    PackedMesh.Vertex A = m.V[m.I16![t]], B = m.V[m.I16[t + 1]], D = m.V[m.I16[t + 2]];
                    var cr = P3.Cross(new P3(B.x - A.x, B.y - A.y, B.z - A.z), new P3(D.x - A.x, D.y - A.y, D.z - A.z));
                    if (cr.Length == 0) n++;
                }
            return n;
        }

        // ---- the tests ----

        [Fact]
        public void APlacedKindHasNoVertexTwice()
        {
            foreach (var k in new[] { Window, Door, Baked("t-topo-twice") })
                With(k, () =>
                {
                    foreach (var (W, H) in new[] { (k.w, k.h), (k.w * 0.8, k.h * 0.9) })
                    {
                        var made = AsMade(k, W, H); var m = Placed(k, W, H);
                        int twice = Twice(MeshPack.Pack(m, weld: false)), was = Twice(MeshPack.Pack(made, weld: false));
                        output.WriteLine($"{k.name} in {W:0.###} × {H:0.###}: {made.Verts} vertices ({was} twice) placed as {m.Verts} ({twice} twice)");
                        Assert.Equal(0, twice);
                        Assert.True(was > 0 && m.Verts < made.Verts - was, $"{k.name}: {m.Verts} vertices of {made.Verts}");
                    }
                });
        }

        [Fact]
        public void APlacedKindHasNoTriangleWithoutArea()
        {
            foreach (var k in new[] { Window, Door, Baked("t-topo-area") })
                With(k, () =>
                {
                    foreach (var (W, H) in new[] { (k.w, k.h), (k.w * 0.8, k.h * 0.9) })
                    {
                        var m = Placed(k, W, H);
                        Assert.Equal(0, NoArea(MeshPack.Pack(m, weld: false)));
                        for (int t = 0; t < m.I.Count; t += 3) Assert.False(KindPart.IsThin(m.P[m.I[t]], m.P[m.I[t + 1]], m.P[m.I[t + 2]]), $"{k.name}: a sliver");
                    }
                    // the converted door's pane and the one baked here have slivers that slicing folds flat: as made, they had none
                    var made = AsMade(k, k.w, k.h);
                    if (k.door || k.id.StartsWith("t-")) Assert.True(NoArea(MeshPack.Pack(made, weld: false)) > 0, $"{k.name}: no folded sliver to leave out");
                });
        }

        /// <summary>
        /// At its own size, the cuts the bake made along the hole's edges are joined: far fewer triangles, the same surface
        /// (every plane's area in every colour), shaded the same. Squeezed into a smaller opening, the slices stand.
        /// </summary>
        [Fact]
        public void AKindShowsWhatItShowedJoinedOrSliced()
        {
            foreach (var k in new[] { Window, Door, Baked("t-topo-join") })
                With(k, () =>
                {
                    foreach (var (W, H) in new[] { (k.w, k.h), (k.w * 0.8, k.h * 0.9) })
                    {
                        var made = AsMade(k, W, H); var m = Placed(k, W, H);
                        var was = Surface(made, (a, b, c, _) => KindPart.IsThin(a, b, c)); var now = Surface(m);
                        var differ = Outside(now, was, was);
                        Assert.True(differ.Count == 0, $"{k.name} in {W:0.###} × {H:0.###}: " + string.Join("; ", differ.Take(5)));
                        output.WriteLine($"{k.name} in {W:0.###} × {H:0.###}: {made.Tris} triangles placed as {m.Tris}");
                        if (W == k.w) Assert.True(m.Tris < made.Tris * 0.9, $"{k.name}: {m.Tris} of {made.Tris} triangles at its own size");
                    }
                });
        }

        /// <summary>
        /// Pushed back into a whole wall by a recess, a kind leaves out what the wall then buries: a frame made to lie on the
        /// wall's face, round its hole. Every triangle left out lies in the wall (a point just in front of it is inside the
        /// wall round the hole, as deep as the reveal goes): nothing seen is lost. Where the wall isn't known to be whole
        /// (<see cref="Facade.Whole"/>: the converted door reaches under its floor), nothing is left out.
        /// </summary>
        [Fact]
        public void WhatARecessBuriesIsLeftOutAndNothingElse()
        {
            foreach (var k in new[] { Window, Door, Baked("t-topo-buried") })
                With(k, () =>
                {
                    double W = k.w, H = k.h, y0 = Sill(k);
                    foreach (double back in new[] { 0.09, 0.15, 0.3 })
                        foreach (double reveal in new[] { T, Math.Min(T, back + OpeningKinds.ShellReveal), back })
                        {
                            // as Facade.Dress asks, on a wall 10 m long in a storey 3.2 m high, its reveal so deep
                            var o = new Opening { u0 = -W / 2, u1 = W / 2, y0 = y0, y1 = y0 + H, door = k.door }; Facade.Dressing(o, k);
                            double deep = Facade.Whole(o, new List<Opening> { o }, new Miter(new Cut(-5, 0), new Cut(5, 0)), 3.2, new Facade.PanelOpt().Set(revealFrom: T - reveal, doorRevealFrom: T - reveal));
                            Assert.Equal(k.door ? 0 : reveal, deep, 9);
                            var made = AsMade(k, W, H, back); var m = Placed(k, W, H, back, deep); var whole = Placed(k, W, H, back);
                            // in the wall: behind its face (z = T) as deep as it is whole, beside, under or over the hole
                            bool InWall(P3 a, P3 b, P3 c, P3 n)
                            {
                                var p = (a + b + c) * (1.0 / 3) + n * 1e-5;
                                return (p.x < -W / 2 || p.x > W / 2 || p.y < y0 || p.y > y0 + H) && p.z < T && p.z > T - deep;
                            }
                            var all = Surface(made, (a, b, c, _) => KindPart.IsThin(a, b, c)); var seen = Surface(made, (a, b, c, n) => KindPart.IsThin(a, b, c) || InWall(a, b, c, n));
                            var differ = Outside(Surface(m), seen, all);
                            Assert.True(differ.Count == 0, $"{k.name} {back} m back, whole {deep:0.###} m in: " + string.Join("; ", differ.Take(5)));
                            Assert.Empty(Outside(Surface(whole), all, all));
                            if (reveal == T) output.WriteLine($"{k.name} {back} m back: {whole.Tris} triangles, {m.Tris} with what the wall buries left out");
                            if (!k.door && back < 0.2 && reveal == T) Assert.True(m.Tris < whole.Tris, $"{k.name}, {back} m back: nothing buried left out");
                            if (k.door) Assert.Equal(whole.Tris, m.Tris);
                        }
                });
        }

        /// <summary>
        /// Where the wall round an opening is whole (<see cref="Facade.Whole"/>): as deep as its reveal goes, but not where
        /// what dresses it reaches another opening, the wall's end or its storey's floor or top.
        /// </summary>
        [Fact]
        public void TheWallIsWholeOnlyWhereNothingElseOpensIt()
        {
            var opt = new Facade.PanelOpt().Set(inner: false, revealFrom: T * 0.45, doorRevealFrom: 0.05);
            var wall = new Miter(new Cut(0, 0), new Cut(10, 0));
            Opening Win(double u0, double u1, double reach = 0.1) => new Opening { u0 = u0, u1 = u1, y0 = 0.9, y1 = 2.2, rl = reach, rr = reach, rb = reach, rt = reach };
            var a = Win(2, 3); var b = Win(3.05, 4.05); var c = Win(6, 7);
            var ops = new List<Opening> { a, b, c };
            Assert.Equal(T * 0.55, Facade.Whole(c, ops, wall, 3, opt), 9);                                        // clear of the rest
            Assert.Equal(0, Facade.Whole(a, ops, wall, 3, opt));                                                   // its dressing reaches b's hole
            Assert.Equal(0, Facade.Whole(b, ops, wall, 3, opt));                                                   // a's hole is in b's dressing
            Assert.Equal(0, Facade.Whole(Win(0.05, 1), new List<Opening>(), wall, 3, opt));                        // past the wall's end
            Assert.Equal(0, Facade.Whole(Win(9, 9.95), new List<Opening>(), wall, 3, opt));
            Assert.Equal(0, Facade.Whole(c, ops, wall, 2.25, opt));                                                // over its storey's top
            var concave = new Miter(new Cut(0, 1), new Cut(10, -1));                                             // the outer face starts T in at a concave corner
            Assert.Equal(0, Facade.Whole(Win(0.25, 1.1, 0.01), new List<Opening>(), concave, 3, opt));
            Assert.Equal(T * 0.55, Facade.Whole(Win(0.4, 1.2, 0.01), new List<Opening>(), concave, 3, opt), 9);
            var convex = new Miter(new Cut(0, -1), new Cut(10, 1));                                              // and runs on past a convex one
            Assert.Equal(T * 0.55, Facade.Whole(Win(0.02, 0.9, 0.01), new List<Opening>(), convex, 3, opt), 9);
            // a door: down to the floor, and nothing of it under it
            var door = new Opening { u0 = 4, u1 = 5.2, y0 = 0, y1 = 2.4, door = true, rl = 0.1, rr = 0.1, rt = 0.1 };
            Assert.Equal(T - 0.05, Facade.Whole(door, new List<Opening> { door }, wall, 3, opt), 9);
            door.rb = 0.02;
            Assert.Equal(0, Facade.Whole(door, new List<Opening> { door }, wall, 3, opt));
            // its own recess's reveal (OpeningEntry.recess): as deep as that
            var own = new Opening { u0 = 4, u1 = 5.2, y0 = 0, y1 = 2.4, door = true, kind = Door, recess = 0.2 };
            Assert.Equal(Math.Min(T, 0.2 + OpeningKinds.ShellReveal), Facade.Whole(own, new List<Opening> { own }, wall, 3, opt), 9);
        }

        /// <summary>
        /// A shell building with the converted window and door, recessed as the converted styles recess them, in fixed bays:
        /// no vertex twice and no triangle without area at any LOD as uploaded, and its kinds placed with fewer triangles
        /// than they were made with.
        /// </summary>
        [Fact]
        public void AConvertedBuildingHasNothingTwiceAndNoTriangleWithoutArea()
        {
            var w = Window; var d = Door;
            OpeningKinds.Register(w); OpeningKinds.Register(d);
            try
            {
                foreach (bool walkIn in new[] { false, true })
                {
                    var doc = new StoreyDocument();
                    var b = Buildings.Add(doc, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
                    b.footprint = new List<Vec2> { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
                    Floors.SetCount(b, 4);
                    b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
                    b.interior = walkIn; b.style = Styles.Preset("brick").Clone(); b.style.ground = GroundType.Match;
                    b.style.windowKind = w.id; b.style.doorKind = d.id; b.style.recess = 0.15; b.style.doorRecess = 0.2;
                    b.style.bayFit = BayFit.Fixed; b.style.bay = 1.6; b.style.details = new DetailRules { ac = 0, vents = 0 };
                    var site = new Site(doc.buildings);
                    var l0 = Lod0.Build(site, b); var rooms = new List<PackedMesh>();
                    var op = MeshPack.Pack(l0.Op, true, null, weld: false, rooms: rooms); var gl = MeshPack.Pack(l0.Glass, true, null, weld: false);
                    var l1 = MeshPack.Pack(Lod1.Build(site, b), windows: true);
                    foreach (var (ms, what) in new[] { (op, "LOD0"), (gl, "its glass"), (rooms, "its rooms"), (l1, "LOD1") })
                    {
                        Assert.Equal(0, Twice(ms));
                        Assert.Equal(0, NoArea(ms));
                        output.WriteLine($"{(walkIn ? "walk-in" : "shell")} {what}: {ms.Sum(x => x.Verts)} vertices, {ms.Sum(x => x.Indices) / 3} triangles");
                    }
                    // the kinds placed: as made, every placement had all of a kind's triangles
                    int windows = 0, doors = 0;
                    for (int k = 0; k < b.floors.Count; k++)
                        for (int i = 0; i < b.footprint.Count; i++)
                        {
                            var f = Details.EdgeFrameAt(b, k, i);
                            foreach (var o in Facade.Ops(site, b, k, i, f.e.L, f.uS, f.uE)) if (o.kind == w) windows++; else if (o.kind == d) doors++;
                        }
                    int made = windows * w.lod0.parts.Sum(p => p.tris.Length / 3) + doors * d.lod0.parts.Sum(p => p.tris.Length / 3);
                    output.WriteLine($"{windows} windows and {doors} door: {made} triangles as made; the whole LOD0 {l0.Op.Tris + l0.Glass.Tris}");
                    Assert.True(windows > 10 && doors == 1);
                    Assert.True(l0.Op.Tris + l0.Glass.Tris < made, "the kinds placed with fewer triangles than they were made with, the walls and all");
                    l0.Op.Recycle(); l0.Glass.Recycle();
                }
            }
            finally { OpeningKinds.Remove(w.id); OpeningKinds.Remove(d.id); }
        }

        /// <summary>The converted kinds' cost, pinned: their own size joined, and what the converted styles' recess buries left out.</summary>
        [Theory]
        [InlineData("Visual_BuildingPiece08_Single (window)", 363, 280)]
        [InlineData("Visual_BuildingPiece09_DoubleDoor (door)", 306, 203)]
        public void TheConvertedKindsCostLess(string name, int points, int tris)
        {
            var k = Converted()[name];
            Assert.Equal(points, k.lod0.P.Length); Assert.Equal(tris, k.lod0.parts.Sum(p => p.tris.Length / 3));
            With(k, () =>
            {
                var own = Placed(k, k.w, k.h); var squeezed = Placed(k, k.w * 0.8, k.h * 0.9);
                double back = k.door ? 0.2 : 0.15; var recessed = Placed(k, k.w, k.h, back, Math.Min(T, back + OpeningKinds.ShellReveal));
                output.WriteLine($"{name}: {points} points, {tris} triangles as made; at its own size {own.Verts} vertices, {own.Tris} triangles; squeezed {squeezed.Verts}, {squeezed.Tris}; recessed {back} m {recessed.Verts}, {recessed.Tris}");
                Assert.True(own.Tris <= tris * 0.85 && own.Verts <= points * 0.75, $"{own.Verts} vertices, {own.Tris} triangles");
                Assert.True(squeezed.Verts < points * 0.95 && squeezed.Tris <= tris);
                Assert.True(recessed.Tris < own.Tris);
            });
        }
    }
}
