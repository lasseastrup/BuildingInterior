using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A corner too sharp to mitre in full (docs/EDITOR.md §6.1, <see cref="Geo.MitreLimit"/>): each strip on its two walls ends
    /// short of the corner's bisector, as the prototype's do, and closes the rest to it (<see cref="Cut.Tip"/>), so the walls,
    /// their strips and the roof's edge meet in a clipped tip rather than leave the corner open between them. Storey's own.
    /// </summary>
    public class AcuteCornerTests
    {
        // an outline with one corner of `deg` at (20, 0), counter-clockwise: along x, back up at that angle, across, down
        static List<Vec2> Wedge(double deg)
        {
            double a = deg * Math.PI / 180, L = 14, z = L * Math.Sin(a);
            return new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20 - L * Math.Cos(a), z), new Vec2(0, z) };
        }

        static (BuildingData b, Site site) Block(List<Vec2> outline, FacadeStyle st)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(3, 5);
            b.footprint = outline;
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 3, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            Floors.SetCount(b, 3);
            b.style = st; b.interior = false;
            return (b, new Site(d.buildings));
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (double deg in new[] { 15.0, 30, 45, 60 })
                foreach (string look in new[] { "plain", "parapet", "strips", "cap" })
                    yield return new object[] { deg, look };
        }

        static FacadeStyle Look(string look)
        {
            var st = Styles.Preset("brick").Clone();
            st.parapet = look == "parapet";
            if (look == "strips") { st.corniceH = 0.3; st.corniceDepth = 0.2; st.plinthH = 0.4; st.plinthDepth = 0.12; st.bandH = 0.12; st.bandDepth = 0.05; }
            if (look == "cap") { st.capH = 0.4; st.capDepth = -0.25; st.capStrip = 0.43; st.cap = "#C8B89A"; st.capRim = "#4A4A4A"; }
            return st;
        }

        /// <summary>The first triangle a ray meets: its distance, and whether it faces the ray's start (by its winding, as culling sees it).</summary>
        static (double t, bool front)? Hit(IReadOnlyList<P3> P, IReadOnlyList<int> I, P3 o, P3 d)
        {
            (double, bool)? best = null;
            for (int i = 0; i < I.Count; i += 3)
            {
                P3 a = P[I[i]], b = P[I[i + 1]], c = P[I[i + 2]];
                P3 e1 = b - a, e2 = c - a, h = P3.Cross(d, e2); double det = P3.Dot(e1, h);
                if (Math.Abs(det) < 1e-12) continue;
                double f = 1 / det; P3 s = o - a; double u = f * P3.Dot(s, h); if (u < -1e-9 || u > 1 + 1e-9) continue;
                P3 q = P3.Cross(s, e1); double v = f * P3.Dot(d, q); if (v < -1e-9 || u + v > 1 + 1e-9) continue;
                double t = f * P3.Dot(e2, q); if (t < 1e-6) continue;
                if (best == null || t < best.Value.Item1) best = (t, P3.Dot(P3.Cross(e1, e2), d) < 0);
            }
            return best;
        }

        [Theory, MemberData(nameof(Cases))]
        public void ItsTipIsClosedAtEveryLod(double deg, string look)
        {
            var (b, site) = Block(Wedge(deg), Look(look));
            // the tip and the way out of it, along its bisector
            var V = new P3(b.pos.x + 20, 0, b.pos.z); double a = deg * Math.PI / 180;
            var wA = new Vec2(0, -1); var wB = new Vec2(Math.Sin(a), Math.Cos(a));
            double bl = Geo.Hypot(wA.x + wB.x, wA.z + wB.z); var out_ = new P3((wA.x + wB.x) / bl, 0, (wA.z + wB.z) / bl);
            double top = Derived.RoofY(b);
            var heights = new List<double> { 0.6, Derived.FloorBase(b, 1) + 1.5, Derived.FloorBase(b, 2) + 1.5, top - 0.15, top + 0.1 };
            var meshes = new List<(string lod, IReadOnlyList<P3> P, IReadOnlyList<int> I)>();
            var op0 = Lod0.Build(site, b).Op; meshes.Add(("LOD0", op0.P, op0.I));
            var op1 = Lod1.Build(site, b); meshes.Add(("LOD1", op1.P, op1.I));
            var m2 = Lod2.Build(site, b); meshes.Add(("LOD2", m2.P, m2.I));
            // across the bisector: either side of it, as the two walls' halves of the clip meet on it
            var side = new P3(-out_.z, 0, out_.x);
            foreach (var (lod, P, I) in meshes)
                foreach (double y in heights)
                    foreach (double off in new[] { -0.04, 0.04 })
                    {
                        // from 6 m out, at the height, straight at the tip: the first face met faces out
                        var o = new P3(V.x + out_.x * 6 + side.x * off, y, V.z + out_.z * 6 + side.z * off);
                        var h = Hit(P, I, o, new P3(-out_.x, 0, -out_.z));
                        if (h == null && y > top) continue;   // over the roof's edge, a plain roof's
                        Assert.True(h != null && h.Value.t < 6 + 1e-6, $"{lod} {deg}° {look}: nothing at the tip {y:0.00} m up");
                        Assert.True(h!.Value.front, $"{lod} {deg}° {look}: {y:0.00} m up, the tip is open: the ray meets an inside face {6 - h.Value.t:0.000} m out of it");
                    }
            // seen from above, the walls' top is closed at the tip: down onto the bisector over the wall, a face turned up at the roof's level
            foreach (var (lod, P, I) in meshes)
                for (double s = 0.03; s < Dim.T_EXT * 1.2; s += 0.04)
                    foreach (double off in new[] { -0.01, 0.01 })
                {
                    var o = new P3(V.x + out_.x * s + side.x * off, top + 20, V.z + out_.z * s + side.z * off);
                    var h = Hit(P, I, o, new P3(0, -1, 0));
                    if (h == null) continue;   // past the clipped tip
                    Assert.True(h.Value.front && top + 20 - h.Value.t > top - 0.05, $"{lod} {deg}° {look}: the top is open {s:0.00} m out from the tip, the first face under it {top + 20 - h.Value.t:0.00} m up ({(h.Value.front ? "up" : "down")})");
                }
        }

        [Fact]
        public void OnlyACornerSharperThanTheLimitIsClipped()
        {
            foreach (double deg in new[] { 15.0, 30, 45, 60, 67, 68, 75, 90, 120 })
            {
                var fp = Wedge(deg); bool ccw = Geo.Area2(fp) > 0;
                var c = Geo.CornerAt(fp, 1, ccw);
                double t = Math.Tan((180 - deg) / 2 * Math.PI / 180);
                Assert.Equal(t > Geo.MitreLimit, c.bc > 0);
                Assert.Equal(Math.Min(t, Geo.MitreLimit), c.t, 6);
                // the two walls' strips meet on the bisector: each half of the clip ends at the same point, w out from either
                var E0 = Geo.EdgeInfo(fp, 0, ccw); var E1 = Geo.EdgeInfo(fp, 1, ccw);
                var m0 = Geo.MiterOf(Geo.Corners(fp, ccw), 0, E0.L); var m1 = Geo.MiterOf(Geo.Corners(fp, ccw), 1, E1.L);
                Assert.Equal(c.bc > 0, m0.E.Clipped); Assert.Equal(c.bc > 0, m1.S.Clipped);
                if (!m0.E.Clipped) continue;
                foreach (double w in new[] { 0.1, 0.3, 1.0 })
                {
                    var (u0, w0) = m0.E.Tip(w); var (u1, w1) = m1.S.Tip(w);
                    var p0 = E0.F.At2(u0, w0); var p1 = E1.F.At2(u1, w1);
                    Assert.True(Geo.Hypot(p0.x - p1.x, p0.z - p1.z) < 1e-6, $"{deg}°: the halves meet {Geo.Hypot(p0.x - p1.x, p0.z - p1.z):0.000} m apart at {w} m");
                    // and stand out from the corner by at most the mitre limit's reach
                    Assert.InRange(Geo.Hypot(p0.x - fp[1].x, p0.z - fp[1].z), 0, w * Math.Sqrt(1 + Geo.MitreLimit * Geo.MitreLimit) + 1e-6);
                }
            }
        }

        [Fact]
        public void ACornerMitredInFullBuildsAsBefore()
        {
            // a rectangle's corners are not clipped: no cut carries a bisector, so nothing is added
            var fp = new List<Vec2> { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            var cor = Geo.Corners(fp, Geo.Area2(fp) > 0);
            for (int i = 0; i < fp.Count; i++) { var m = Geo.MiterOf(cor, i, 10); Assert.False(m.S.Clipped || m.E.Clipped); }
        }
    }
}
