using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A detail up the corners at a corner of another angle than the 90° it was made for (docs/EDITOR.md §6.12,
    /// <see cref="DetailKind.cornerMin"/>, <see cref="DetailKind.cornerBend"/>): bent to fit, each half lies on its own wall as
    /// it lies on its wall at 90°; and outside the kind's range of angles it isn't placed, where it would float off a sharp
    /// corner or sink into a blunt one. Storey's own.
    /// </summary>
    public class CornerDetailAngleTests
    {
        const double Gap = 0.02, Arm = 0.5;
        static readonly double R = Math.Sqrt(0.5);

        // a corner piece made for 90°: a plate on each wall, 2 cm out from its outer face and half a metre along it. In the kind's
        // own space x runs across the corner, z out along its bisector from where the walls' faces meet, and a 90° corner's
        // faces run back from there at 45° either side (forward, in a notch: notch = true)
        static DetailKind Quoin(bool notch, double min = 45, double max = 135, bool bend = true)
        {
            var P = new List<P3>(); var N = new List<P3>(); var tris = new List<int>();
            foreach (int side in new[] { -1, 1 })
            {
                // s along the face from the corner, Gap out from it: back from the corner going out, forward into a notch
                P3 Along(double s, double y) => notch ? new P3(side * R * (s - Gap), y, R * (s + Gap)) : new P3(side * R * (s + Gap), y, R * (Gap - s));
                var n = notch ? new P3(-side * R, 0, R) : new P3(side * R, 0, R);
                int i0 = P.Count;
                foreach (var (s, y) in new[] { (0.05, 0.0), (Arm, 0.0), (Arm, 0.4), (0.05, 0.4) }) { P.Add(Along(s, y)); N.Add(n); }
                tris.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            var m = new DetailKind.Mesh { P = P.ToArray(), N = N.ToArray() };
            m.parts.Add(new DetailKind.Part { slot = ColorSlot.Dish, tris = tris.ToArray() });
            var k = new DetailKind
            {
                id = Guid.NewGuid().ToString("N"), name = "Quoin", anchor = Details.Anchor.Wall, rule = Details.Rule.Corners, w = 0.8, h = 0.4, d = 0.1,
                lod0 = m, cornerMin = min, cornerMax = max, cornerBend = bend,
            };
            DetailKinds.Register(k);
            return k;
        }

        static (BuildingData b, Site site) Block(List<Vec2> outline, DetailKind k, DetailCorners corners)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = outline; b.entrances.Clear(); b.details.Clear(); b.shafts.Clear(); b.interior = false;
            Floors.SetCount(b, 2);
            b.style = Styles.Preset("brick").Clone(); b.style.details = new DetailRules { ac = 0, vents = 0 };
            DetailKindTests.Rule(b, k.id, 100);
            (b.style.details.rules ??= new SortedDictionary<string, KindRule>(StringComparer.Ordinal))[k.id] = new KindRule { spacing = 1.0, corners = corners };
            return (b, new Site(d.buildings));
        }

        // one corner of `deg` at (20, 0), counter-clockwise, its other corners 180 - deg and two of 90°
        static List<Vec2> Wedge(double deg)
        {
            double a = deg * Math.PI / 180, L = 14;
            return new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20 - L * Math.Cos(a), L * Math.Sin(a)), new Vec2(0, L * Math.Sin(a)) };
        }

        // a notch of `deg` in the top side, its point at (10, 12 - depth)
        static List<Vec2> Notched(double deg)
        {
            double h = 4 / Math.Tan(deg / 2 * Math.PI / 180);
            return new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 12), new Vec2(14, 12), new Vec2(10, 12 - h), new Vec2(6, 12), new Vec2(0, 12) };
        }

        // the outer faces of the two walls at corner i: a point on each, its outward normal and the way back along it
        static (Vec2 o, Vec2 n, Vec2 u)[] Faces(List<Vec2> fp, int i)
        {
            bool ccw = Geo.Area2(fp) > 0; int n = fp.Count;
            var r = new List<(Vec2, Vec2, Vec2)>();
            foreach (int e in new[] { (i - 1 + n) % n, i })
            {
                var a = fp[e]; var c = fp[(e + 1) % n]; double L = Geo.Hypot(c.x - a.x, c.z - a.z);
                var u = new Vec2((c.x - a.x) / L, (c.z - a.z) / L); var w = ccw ? new Vec2(u.z, -u.x) : new Vec2(-u.z, u.x);
                r.Add((new Vec2(a.x + w.x * Dim.T_EXT, a.z + w.z * Dim.T_EXT), w, u));
            }
            return r.ToArray();
        }

        /// <summary>Every vertex of the pieces round corner <paramref name="i"/>: off which face, how far and how far along it, and its normal.</summary>
        static void OnTheWalls(List<Vec2> fp, DetailKind k, DetailCorners corners, int i, bool fits, string what)
        {
            var (b, site) = Block(fp, k, corners);
            var op = Lod0.Build(site, b).Op;
            var faces = Faces(fp, i); var V = fp[i];
            var vs = Enumerable.Range(0, op.Verts).Where(v => op.C[v].slot == ColorSlot.Dish && Geo.Hypot(op.P[v].x - V.x, op.P[v].z - V.z) < 2).ToList();
            Assert.NotEmpty(vs);
            bool all = true; string first = "";
            foreach (int v in vs)
            {
                var p = op.P[v]; var nv = op.N[v];
                bool ok = faces.Any(f =>
                {
                    double off = (p.x - f.o.x) * f.n.x + (p.z - f.o.z) * f.n.z;
                    return Math.Abs(off - Gap) < 1e-6 && Math.Abs(nv.x * f.n.x + nv.z * f.n.z - 1) < 1e-6;
                });
                if (!ok && all) { all = false; first = $"{p} ({nv})"; }
            }
            Assert.True(all == fits, $"{what}: {(fits ? "off its wall at " + first : "on its walls though not bent")}");
        }

        [Fact]
        public void BentEachHalfLiesOnItsOwnWall()
        {
            var k = Quoin(notch: false);
            OnTheWalls(Wedge(60), k, DetailCorners.Outward, 1, true, "60° going out");
            OnTheWalls(Wedge(60), k, DetailCorners.Outward, 2, true, "120° going out");
            OnTheWalls(Wedge(90), k, DetailCorners.Outward, 1, true, "90°, as made");
            var n = Quoin(notch: true);
            OnTheWalls(Notched(60), n, DetailCorners.Inward, 4, true, "a 60° notch");
            // not bent, it is what it was: off the walls of a corner not 90°
            OnTheWalls(Wedge(60), Quoin(notch: false, bend: false), DetailCorners.Outward, 1, false, "60°, not bent");
        }

        [Fact]
        public void OutsideItsAnglesItIsntPlaced()
        {
            var k = Quoin(notch: false, min: 45, max: 135);
            foreach (double deg in new[] { 30.0, 45, 60, 90 })
            {
                var (b, site) = Block(Wedge(deg), k, DetailCorners.Outward);
                var at = Enumerable.Range(0, b.floors.Count).SelectMany(f => Details.At(site, b, f)).Where(it => it.kind == k).Select(it => it.i).Distinct().OrderBy(x => x).ToList();
                // corners 0 and 3 are square; 1 is `deg`, and 2 is 180 - deg: both in the range from 45° to 135°, or neither
                var want = new List<int> { 0, 3 }; if (deg >= 45) want.AddRange(new[] { 1, 2 });
                Assert.Equal(want.OrderBy(x => x), at);
            }
            // a range of its own: the square corners alone
            var square = Quoin(notch: false, min: 80, max: 100);
            var (b2, s2) = Block(Wedge(60), square, DetailCorners.Outward);
            Assert.Equal(new[] { 0, 3 }, Enumerable.Range(0, b2.floors.Count).SelectMany(f => Details.At(s2, b2, f)).Where(it => it.kind == square).Select(it => it.i).Distinct().OrderBy(x => x));
        }
    }
}
