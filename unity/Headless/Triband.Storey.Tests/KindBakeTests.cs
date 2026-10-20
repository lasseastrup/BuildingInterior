using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// An artist's window or door from a prefab of several objects, made ready by <see cref="KindBake"/> (docs/OPENINGS.md
    /// §3): each object through its matrix into the opening's space, parts by key, mirrored objects turned the right way out,
    /// welded, and what the wall buries left out. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registry is shared
    public class KindBakeTests
    {
        /// <summary>
        /// A box from (x0, y0, z0) to (x1, y1, z1) in its object's own space, its six faces out (front faces wound as the
        /// generator's: cross(b − a, c − a) along the face's normal), four points a face, all in one submesh.
        /// </summary>
        static KindBake.Source Box(double x0, double y0, double z0, double x1, double y1, double z1, string key = "frame", double[]? m = null)
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
            if (m != null) s.M = m;
            s.subs.Add(new KindBake.Sub { tris = t.ToArray(), key = key });
            return s;
        }

        /// <summary>A pane: one quad facing +Z at <paramref name="z"/>.</summary>
        static KindBake.Source Pane(double x0, double y0, double x1, double y1, double z, string key = "glass", double[]? m = null)
        {
            var s = new KindBake.Source
            {
                P = new[] { new P3(x0, y0, z), new P3(x1, y0, z), new P3(x1, y1, z), new P3(x0, y1, z) },
                N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray(),
            };
            if (m != null) s.M = m;
            s.subs.Add(new KindBake.Sub { tris = new[] { 0, 1, 2, 0, 2, 3 }, key = key });
            return s;
        }

        /// <summary>A matrix: a scale, then a turn about +Y by <paramref name="degrees"/>, then a move.</summary>
        static double[] Trs(double tx, double ty, double tz, double degrees = 0, double sx = 1, double sy = 1, double sz = 1)
        {
            double r = degrees * Math.PI / 180, c = Math.Cos(r), s = Math.Sin(r);
            return new[] { c * sx, 0, s * sz, tx, 0, sy, 0, ty, -s * sx, 0, c * sz, tz };
        }

        static IEnumerable<(P3 a, P3 b, P3 c)> Tris(KindBake.Result r, KindBake.Part p)
        {
            for (int i = 0; i < p.tris.Count; i += 3) yield return (r.P[p.tris[i]], r.P[p.tris[i + 1]], r.P[p.tris[i + 2]]);
        }

        static void Near(double want, double got) => Assert.InRange(got, want - 1e-9, want + 1e-9);

        [Fact]
        public void ObjectsGoThroughTheirMatricesIntoTheHolesSpace()
        {
            // the prefab's pivot at the hole's middle (a cutout centred on it), 1.2 × 1.6: the glass object moved
            // back 5 cm, a sill object under the hole moved down to its bottom
            var hole = new KindBake.Hole(0, 0, 1.2, 1.6);
            var r = KindBake.Bake(new[]
            {
                Pane(-0.6, -0.8, 0.6, 0.8, 0, m: Trs(0, 0, -0.05)),
                Box(-0.7, -0.05, 0, 0.7, 0, 0.12, "sill", Trs(0, -0.8, 0)),
            }, hole, false);
            Assert.Equal(new[] { "glass", "sill" }, r.parts.Select(p => p.key));
            var glass = r.parts[0];
            Assert.All(glass.tris, i => Near(-0.05, r.P[i].z));
            Near(0, r.parts[0].tris.Min(i => r.P[i].y)); Near(1.6, r.parts[0].tris.Max(i => r.P[i].y));   // the hole's bottom is y 0
            Near(-0.6, glass.tris.Min(i => r.P[i].x)); Near(0.6, glass.tris.Max(i => r.P[i].x));
            Near(-0.05, r.min.y); Near(0.12, r.max.z);   // the sill under the hole, standing out of the wall
        }

        [Fact]
        public void APivotAnywhereGivesTheSameWindow()
        {
            // the same window with its pivot at the floor, 0.9 m under the hole, and the cutout's middle where the hole is
            var a = KindBake.Bake(new[] { Pane(-0.5, 0, 0.5, 1.4, -0.05) }, new KindBake.Hole(0, 0.7, 1, 1.4), false);
            var b = KindBake.Bake(new[] { Pane(-0.5, 0.9, 0.5, 2.3, -0.05) }, new KindBake.Hole(0, 1.6, 1, 1.4), false);
            Assert.Equal(a.P.Count, b.P.Count);
            for (int i = 0; i < a.P.Count; i++) { Near(a.P[i].x, b.P[i].x); Near(a.P[i].y, b.P[i].y); Near(a.P[i].z, b.P[i].z); }
        }

        [Fact]
        public void APartTakesEveryObjectsTrianglesOfItsKey()
        {
            // two shutters, the right one turned half round about +Y: one part, both objects in it
            var r = KindBake.Bake(new[]
            {
                Box(-0.2, 0, 0.02, 0.2, 1.4, 0.05, "shutter", Trs(-0.8, 0, 0)),
                Box(-0.2, 0, -0.05, 0.2, 1.4, -0.02, "shutter", Trs(0.8, 0, 0, 180)),
                Pane(-0.5, 0, 0.5, 1.4, -0.05),
            }, new KindBake.Hole(0, 0.7, 1, 1.4), false);
            Assert.Equal(new[] { "shutter", "glass" }, r.parts.Select(p => p.key));
            Assert.Equal(24, r.parts[0].tris.Count / 3);
            Assert.Equal(2, r.parts[0].tris.Select(i => Math.Sign(r.P[i].x)).Distinct().Count());   // both sides
        }

        [Fact]
        public void AMirroredObjectStillFacesOut()
        {
            // a shutter mirrored across X (scale −1): its triangles wound the other way, so their fronts face out of its box
            var r = KindBake.Bake(new[] { Box(0.6, 0, 0.02, 1.0, 1.4, 0.06, "shutter", Trs(0, 0, 0, 0, -1, 1, 1)) }, new KindBake.Hole(0, 0.7, 1, 1.4), false);
            var part = r.parts.Single();
            var mid = new P3(-0.8, 0.7, 0.04);
            foreach (var (a, b, c) in Tris(r, part))
            {
                var n = P3.Cross(b - a, c - a); var centre = (a + b + c) * (1.0 / 3);
                Assert.True(P3.Dot(n, centre - mid) > 0, $"a face at {centre} faces into its box");
            }
            // and its normals go with the faces
            foreach (int i in part.tris) Assert.True(P3.Dot(r.N[i], r.P[i] - mid) > 0);
        }

        [Fact]
        public void FacesBackTurnsItRound()
        {
            var front = KindBake.Bake(new[] { Box(0.1, 0.2, 0.02, 0.3, 0.4, 0.05) }, new KindBake.Hole(0, 0.7, 1, 1.4), false);
            var back = KindBake.Bake(new[] { Box(-0.3, 0.2, -0.05, -0.1, 0.4, -0.02) }, new KindBake.Hole(0, 0.7, 1, 1.4), true);
            Near(front.min.x, back.min.x); Near(front.max.z, back.max.z); Near(front.min.z, back.min.z);
            // still the right way out: the front face's winding points +Z
            var p = back.parts.Single();
            Assert.Contains(Tris(back, p), t => P3.Cross(t.b - t.a, t.c - t.a).z > 0.9 * P3.Cross(t.b - t.a, t.c - t.a).Length && Math.Abs(t.a.z - 0.05) < 1e-9);
        }

        [Fact]
        public void WhatTheWallBuriesIsLeftOut()
        {
            // a block wholly inside the wall goes; a frame member in the hole, flush with its side and with the wall's face,
            // loses its faces against the hole's side, bottom and top (turned into the wall) and keeps the rest
            double w = 1.0, h = 1.4;
            var r = KindBake.Bake(new[]
            {
                Box(-w / 2, 0, -0.1, -w / 2 + 0.08, h, 0, "frame"),
                Box(-w / 2 - 0.30, 0, -0.2, -w / 2 - 0.10, h, -0.05, "anchor"),
            }, new KindBake.Hole(0, h / 2, w, h), false);
            Assert.DoesNotContain(r.parts, p => p.key == "anchor");
            Assert.Equal(6 + 12, r.hidden);
            var f = r.parts.Single(p => p.key == "frame");
            Assert.Equal(6, f.tris.Count / 3);   // its front, back and inner side
            Assert.All(f.tris, i => Assert.True(r.P[i].x > -w / 2 + 1e-9 || Math.Abs(r.N[i].x) < 0.5, "a face on the hole's side is left"));
            // a member reaching out of the wall keeps its faces on the hole's side: they show beside the wall
            var o = KindBake.Bake(new[] { Box(-w / 2, 0, -0.1, -w / 2 + 0.08, h, 0.03, "frame") }, new KindBake.Hole(0, h / 2, w, h), false);
            Assert.Equal(12, o.parts.Single().tris.Count / 3);
        }

        [Fact]
        public void FacesOnTheWallsFaceTurnedOutAreCounted()
        {
            // a flat plate on the wall's face round the hole: it would flicker against the wall
            var plate = Pane(-0.8, -0.2, -0.55, 1.6, 0);
            var r = KindBake.Bake(new[] { plate }, new KindBake.Hole(0, 0.7, 1, 1.4), false);
            Assert.Equal(2, r.onWall);
            Assert.Single(r.parts);   // still drawn: the inspector warns
            // turned into the wall it is hidden instead
            var back = Pane(-0.8, -0.2, -0.55, 1.6, 0); back.N = Enumerable.Repeat(new P3(0, 0, -1), 4).ToArray(); back.subs[0].tris = new[] { 0, 2, 1, 0, 3, 2 };
            var rb = KindBake.Bake(new[] { back }, new KindBake.Hole(0, 0.7, 1, 1.4), false);
            Assert.Equal(2, rb.hidden); Assert.Empty(rb.parts);
        }

        [Fact]
        public void SeamsAreWelded()
        {
            // a quad as two triangles with their own points each (a UV seam): four points once welded
            var s = new KindBake.Source
            {
                P = new[] { new P3(-0.5, 0, 0), new P3(0.5, 0, 0), new P3(0.5, 1, 0), new P3(-0.5, 0, 0), new P3(0.5, 1, 0), new P3(-0.5, 1, 0) },
                N = Enumerable.Repeat(new P3(0, 0, 1), 6).ToArray(),
            };
            s.subs.Add(new KindBake.Sub { tris = new[] { 0, 1, 2, 3, 4, 5 }, key = "glass" });
            var r = KindBake.Bake(new[] { s }, new KindBake.Hole(0, 0.5, 1, 1), false);
            Assert.Equal(4, r.P.Count);
            Assert.Equal(6, r.parts.Single().tris.Count);
        }

        [Fact]
        public void KeysCanGoByTriangle()
        {
            // the Model Remapper's colours are the vertices': one submesh, a key a triangle
            var s = Pane(-0.5, 0, 0.5, 1, -0.05);
            s.subs[0].keys = new[] { "white", "blue" };
            var r = KindBake.Bake(new[] { s }, new KindBake.Hole(0, 0.5, 1, 1), false);
            Assert.Equal(new[] { "white", "blue" }, r.parts.Select(p => p.key));
        }

        [Fact]
        public void LeafObjectsAreTheirOwnParts()
        {
            var door = Box(-0.4, 0, -0.08, 0.4, 2.1, -0.04, "wood");
            var frame = Box(-0.5, 0, -0.08, -0.4, 2.2, 0.02, "wood");
            door.leaf = true;
            var r = KindBake.Bake(new[] { frame, door }, new KindBake.Hole(0, 1.1, 1, 2.2), false);
            Assert.Equal(2, r.parts.Count);
            Assert.Equal(new[] { false, true }, r.parts.Select(p => p.leaf));
            Assert.All(r.parts, p => Assert.Equal("wood", p.key));
        }

        [Fact]
        public void BadTrianglesAndFlatObjectsAreLeftOut()
        {
            var s = Pane(-0.5, 0, 0.5, 1, -0.05);
            s.subs[0].tris = new[] { 0, 1, 2, 0, 2, 3, 0, 1, 99, 1, 1, 2 };   // a point the mesh hasn't, a triangle with no area
            var flat = Pane(-0.5, 0, 0.5, 1, -0.05, m: Trs(0, 0, 0, 0, 0, 1, 1));   // scaled to nothing across
            var r = KindBake.Bake(new[] { s, flat }, new KindBake.Hole(0, 0.5, 1, 1), false);
            Assert.Equal(6, r.parts.Single().tris.Count);
            Assert.Equal(4, r.degenerate);
        }

        /// <summary>A bake as an opening kind, each part in the colour its key names (as a Storey Opening's rows give them).</summary>
        internal static OpeningKind Kind(string id, KindBake.Result r, KindBake.Hole hole, Func<string, ColorSlot> colour, bool door = false)
        {
            var m = new OpeningKind.Mesh { P = r.P.ToArray(), N = r.N.ToArray() };
            foreach (var p in r.parts) m.parts.Add(new OpeningKind.Part { slot = colour(p.key), leaf = door && p.leaf, tris = p.tris.ToArray() });
            return new OpeningKind { id = id, name = id, door = door, w = hole.w, h = hole.h, lod0 = m };
        }

        /// <summary>
        /// A window as an artist builds one in a prefab, its pivot at the storey's floor (the hole 0.9 m up): a frame of four
        /// members in the hole, flush with the wall's face; glass 5 cm in; a sill under the hole standing out; a shutter
        /// either side of it, 2 cm off the wall.
        /// </summary>
        internal static (List<KindBake.Source> src, KindBake.Hole hole) Shuttered()
        {
            double w = 1.2, h = 1.6, y0 = 0.9, f = 0.06;
            var src = new List<KindBake.Source>
            {
                Box(-w / 2, y0, -0.1, -w / 2 + f, y0 + h, 0, "frame"),
                Box(w / 2 - f, y0, -0.1, w / 2, y0 + h, 0, "frame"),
                Box(-w / 2 + f, y0 + h - f, -0.1, w / 2 - f, y0 + h, 0, "frame"),
                Box(-w / 2 + f, y0, -0.1, w / 2 - f, y0 + f, 0, "frame"),
                Pane(-w / 2 + f, y0 + f, w / 2 - f, y0 + h - f, -0.05),
                Box(-w / 2 - 0.05, y0 - 0.06, 0.0, w / 2 + 0.05, y0, 0.12, "sill"),
                Box(-w / 2 - 0.42, y0, 0.02, -w / 2 - 0.02, y0 + h, 0.05, "shutter"),
                Box(w / 2 + 0.02, y0, 0.02, w / 2 + 0.42, y0 + h, 0.05, "shutter"),
            };
            return (src, new KindBake.Hole(0, y0 + h / 2, w, h));
        }

        internal static ColorSlot ShutteredColour(string key) => key switch { "glass" => ColorSlot.Glass, "sill" => ColorSlot.Trim, _ => ColorSlot.Frame };

        [Fact]
        public void APrefabsWindowGoesOnABuildingWithNothingFlickering()
        {
            var (src, hole) = Shuttered();
            var r = KindBake.Bake(src, hole, false);
            Assert.Equal(new[] { "frame", "glass", "sill", "shutter" }, r.parts.Select(p => p.key));
            Assert.Equal(0, r.onWall);
            var k = Kind("t-bake-shuttered", r, hole, ShutteredColour);
            OpeningKinds.Register(k);
            try
            {
                var c = OpeningKinds.Check(k);
                Assert.True(c.glass); Assert.False(c.behind); Assert.False(c.deepGlass);
                Assert.True(c.uncovered < 0.001, $"{c.uncovered:P1} of the hole open");
                var t = Validate.KindTrial.Run(k);
                Assert.True(t.overlaps.Count == 0, string.Join("\n", t.overlaps.Take(5)));
                Assert.True(t.lod0 > 0 && t.plainLod0 > 0 && t.lod1 > 0);
            }
            finally { OpeningKinds.Remove(k.id); }
        }

        [Fact]
        public void TheCheckSaysWhatShowsThroughOrBehind()
        {
            // a pane over the left half of the hole only, set 20 cm in (past a shell storey's reveal), and a block behind the wall
            var r = KindBake.Bake(new[] { Pane(-0.5, 0, 0, 1.4, -0.2), Box(0.1, 0.2, -0.4, 0.2, 0.3, -0.35, "bolt") }, new KindBake.Hole(0, 0.7, 1, 1.4), false);
            var k = Kind("t-bake-check", r, new KindBake.Hole(0, 0.7, 1, 1.4), key => key == "glass" ? ColorSlot.Glass : ColorSlot.Metal);
            var c = OpeningKinds.Check(k);
            Assert.InRange(c.uncovered, 0.45, 0.55);
            Assert.True(c.deepGlass); Assert.True(c.behind); Assert.True(c.glass); Assert.False(c.leaf);
        }

        [Fact]
        public void BoundsMakeAHole()
        {
            var src = new[] { Pane(-0.5, 0.9, 0.5, 2.3, -0.05), Box(-0.6, 0.85, 0, 0.6, 0.9, 0.1, "sill") };
            var b = KindBake.Bounds(src)!.Value;
            var hole = KindBake.HoleOf(b);
            Near(1.2, hole.w); Near(1.45, hole.h); Near(1.575, hole.cy); Near(0, hole.wallZ);
            Assert.Null(KindBake.Bounds(Array.Empty<KindBake.Source>()));
        }
    }
}
