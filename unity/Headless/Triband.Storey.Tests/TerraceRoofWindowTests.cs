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
    /// A roof over a setback's terrace leaves out what it comes up against on the walls above it (<see cref="Roofs.TerraceRoofAt"/>,
    /// docs/EDITOR.md §6.2): the windows whose foot it would cover, at LOD0 and LOD1 (<see cref="Facade.Ops"/>) and in LOD2's
    /// paint, and the details there. A window clear of it keeps its place, and a terrace keeps them all. Storey's own: the
    /// prototype keeps them (<see cref="Site.keepUnderTerraceRoofs"/>).
    /// </summary>
    public class TerraceRoofWindowTests
    {
        static readonly List<Vec2> Rect = new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 14), new Vec2(0, 14) };
        static readonly List<Vec2> Ell = new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 8), new Vec2(10, 8), new Vec2(10, 16), new Vec2(0, 16) };

        // five storeys, the setback halfway up inset all round, its terrace roofed (type) or not (null); keep: a site that keeps
        // what the roof comes up against, as the prototype's does
        static (BuildingData b, Site site, int k) Block(List<Vec2> outline, RoofType? type, double pitch = 30, bool keep = false, Action<BuildingData>? f = null)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = outline.ToList(); b.entrances.Clear(); b.details.Clear(); b.shafts.Clear(); b.interior = false;
            Floors.SetCount(b, 5); b.style = Styles.Preset("brick").Clone(); b.style.details = new DetailRules { ac = 0, vents = 0 };
            int k = Setbacks.Add(b, 0);
            Assert.Equal(OutlineIssue.None, Outlines.Set(b, k, Outlines.Inset(Tiers.Outline(b, k), 2.5)));
            if (type is RoofType t) { Facades.SetTerraceRoof(b, k, true); b.floors[k].terraceRoof!.type = t; b.floors[k].terraceRoof!.pitch = pitch; }
            f?.Invoke(b);
            return (b, new Site(d.buildings) { keepUnderTerraceRoofs = keep }, k);
        }

        static List<Opening> Windows(Site s, BuildingData b, int k, int i)
        {
            var f = Details.EdgeFrameAt(b, k, i);
            return Facade.Ops(s, b, k, i, f.e.L, f.uS, f.uE).Where(o => !o.door).ToList();
        }

        // the roof comes up to the window's foot: within the clearance of it, or over it
        static bool Under(Site s, BuildingData b, int k, int i, Opening o) =>
            Roofs.TerraceRoofAt(s, b, k, i, o.Left, o.Right) > Derived.FloorBase(b, k) + o.Bottom - Roofs.TerraceRoofClear;

        [Fact]
        public void TheRoofMeetsTheWallsAboveItWhereItsSlopesRiseTo()
        {
            foreach (var outline in new[] { Rect, Ell })
            {
                var (b, s, k) = Block(outline, RoofType.Hip);
                var up = Derived.OutlineAt(b, k); double slope = Math.Tan(30 * Math.PI / 180), top = Derived.FloorBase(b, k);
                for (int i = 0; i < up.Count; i++)
                {
                    var a = up[i]; var c = up[(i + 1) % up.Count]; double L = Geo.Hypot(c.x - a.x, c.z - a.z);
                    // inset 2.5 m all round: up the hip roof's slope from the wall below's face to this one's, 2 cm short of it
                    Assert.InRange(Roofs.TerraceRoofAt(s, b, k, i, L / 2 - 0.5, L / 2 + 0.5) - top, 2.48 * slope - 0.01, 2.48 * slope + 0.01);
                }
                // the storeys above stand on it too; the tier below has none, nor a site that keeps what the roof comes up against
                Assert.True(Roofs.TerraceRoofAt(s, b, k + 1, 0, 1, 2) > top);
                Assert.Equal(double.NegativeInfinity, Roofs.TerraceRoofAt(s, b, k - 1, 0, 1, 2));
                var (b2, kept, k2) = Block(outline, RoofType.Hip, keep: true);
                Assert.Equal(double.NegativeInfinity, Roofs.TerraceRoofAt(kept, b2, k2, 0, 1, 2));
            }
        }

        [Fact]
        public void NoWindowIsLeftWhereTheRoofComesUpToIt()
        {
            foreach (var outline in new[] { Rect, Ell })
                foreach (var (type, pitch) in new[] { (RoofType.Hip, 30.0), (RoofType.Gable, 30.0), (RoofType.Hip, 50.0) })
                {
                    var (b, s, k) = Block(outline, type, pitch);
                    var (b0, keep, _) = Block(outline, type, pitch, keep: true);
                    int left = 0;
                    for (int kk = k; kk < b.floors.Count; kk++)
                        for (int i = 0; i < Derived.OutlineAt(b, kk).Count; i++)
                        {
                            var got = Windows(s, b, kk, i); var all = Windows(keep, b0, kk, i);
                            // none under the roof; the rest where they were, the same windows
                            Assert.DoesNotContain(got, o => Under(s, b, kk, i, o));
                            Assert.All(got, o => Assert.Contains(all, w => Math.Abs(w.u0 - o.u0) < 1e-9 && Math.Abs(w.y0 - o.y0) < 1e-9 && Math.Abs(w.u1 - o.u1) < 1e-9));
                            // and each left out was under it
                            foreach (var w in all.Where(w => !got.Any(o => Math.Abs(w.u0 - o.u0) < 1e-9)))
                            {
                                Assert.True(Under(s, b, kk, i, w), $"{type} {pitch}°: storey {kk} edge {i}: a window at {w.u0:0.00} left out though clear of the roof");
                                left++;
                            }
                        }
                    Assert.True(left > 0, $"{type} {pitch}°: no window left out");
                }
        }

        [Fact]
        public void ATerraceAndTheStoreysClearOfTheRoofKeepTheirWindows()
        {
            foreach (var outline in new[] { Rect, Ell })
            {
                // a terrace: every window
                var (b, s, k) = Block(outline, null);
                var (b0, keep, _) = Block(outline, null, keep: true);
                for (int kk = k; kk < b.floors.Count; kk++)
                    for (int i = 0; i < Derived.OutlineAt(b, kk).Count; i++)
                        Assert.Equal(Windows(keep, b0, kk, i).Count, Windows(s, b, kk, i).Count);
                // a hip roof 2.5 m in at 30° rises 1.4 m up the walls: the storeys above the setback's first keep theirs
                var (bh, sh, kh) = Block(outline, RoofType.Hip);
                var (bk, kept, _) = Block(outline, RoofType.Hip, keep: true);
                for (int kk = kh + 1; kk < bh.floors.Count; kk++)
                    for (int i = 0; i < Derived.OutlineAt(bh, kk).Count; i++)
                        Assert.Equal(Windows(kept, bk, kk, i).Count, Windows(sh, bh, kk, i).Count);
            }
        }

        [Fact]
        public void LodTwoPaintsTheWindowsLodZeroHas()
        {
            foreach (var outline in new[] { Rect, Ell })
                foreach (var type in new[] { RoofType.Hip, RoofType.Gable })
                {
                    var (b, s, k) = Block(outline, type);
                    var (b0, keep, _) = Block(outline, type, keep: true);
                    var m = Lod2.Build(s, b);
                    int kept = 0, left = 0;
                    for (int kk = k; kk < b.floors.Count; kk++)
                        for (int i = 0; i < Derived.OutlineAt(b, kk).Count; i++)
                        {
                            var f = Details.EdgeFrameAt(b, kk, i); var got = Windows(s, b, kk, i);
                            foreach (var w in Windows(keep, b0, kk, i))
                            {
                                // clear of where LOD2's window, a little narrower or wider, may be judged otherwise
                                double margin = Roofs.TerraceRoofAt(s, b, kk, i, w.Left - 0.3, w.Right + 0.3) - (Derived.FloorBase(b, kk) + w.Bottom - Roofs.TerraceRoofClear);
                                double tight = Roofs.TerraceRoofAt(s, b, kk, i, w.Left, w.Right) - (Derived.FloorBase(b, kk) + w.Bottom - Roofs.TerraceRoofClear);
                                if (Math.Abs(margin) < 0.2 || Math.Abs(tight) < 0.2 || (margin > 0) != (tight > 0)) continue;
                                bool there = got.Any(o => Math.Abs(o.u0 - w.u0) < 1e-9);
                                double bay = BayAt(m, f.e.F.At((w.u0 + w.u1) / 2, Derived.FloorBase(b, kk) + (w.y0 + w.y1) / 2, Dim.T_EXT), new P3(f.e.w.x, 0, f.e.w.z));
                                Assert.True(there ? bay > 0 : bay == 0, $"{type}: storey {kk} edge {i}: the window at {w.u0:0.00} is {(there ? "drawn" : "left out")} at LOD0, {(bay > 0 ? "painted" : "not")} at LOD2");
                                if (there) kept++; else left++;
                            }
                        }
                    Assert.True(kept > 0 && left > 0, $"{type}: {kept} kept, {left} left out");
                    // the walls in pieces are whole: no vertex twice, as the upload has it (MeshTopologyTests)
                    var seen = new HashSet<string>();
                    for (int v = 0; v < m.P.Count; v++)
                        Assert.True(seen.Add($"{m.P[v].x},{m.P[v].y},{m.P[v].z}|{m.N[v].x},{m.N[v].y},{m.N[v].z}|{m.Fac[4 * v]},{m.Fac[4 * v + 1]},{m.Fac[4 * v + 2]},{m.Fac[4 * v + 3]}|{m.Fac2[2 * v]},{m.Fac2[2 * v + 1]}|{m.Slot[v]}"));
                }
        }

        // the bay of the massing's facade quad over point p of a wall facing n (-1: none)
        static double BayAt(Lod2Mesh m, P3 p, P3 n)
        {
            for (int t = 0; t < m.I.Count; t += 3)
            {
                int a = m.I[t], c = m.I[t + 1], d = m.I[t + 2];
                if (m.Fac2[2 * a + 1] != 1 || P3.Dot(m.N[a] * (1.0 / 127), n) < 0.99) continue;
                P3 A = m.P[a], B = m.P[c], C = m.P[d]; var nr = P3.Cross(B - A, C - A); double area = nr.Length;
                if (area < 1e-9 || Math.Abs(P3.Dot(nr * (1 / area), p - A)) > 1e-4) continue;
                // inside: on the same side of each edge
                double s0 = P3.Dot(P3.Cross(B - A, p - A), nr), s1 = P3.Dot(P3.Cross(C - B, p - B), nr), s2 = P3.Dot(P3.Cross(A - C, p - C), nr);
                if (s0 >= 0 && s1 >= 0 && s2 >= 0) return m.Fac2[2 * a];
            }
            return -1;
        }

        [Fact]
        public void NoDetailStandsInTheRoof()
        {
            var foot = DetailKindTests.Kind(Details.Anchor.Wall, Details.Rule.AlongFoot, w: 0.5, h: 0.4, d: 0.1);
            var corner = DetailKindTests.Kind(Details.Anchor.Wall, Details.Rule.Corners, w: 0.6, h: 0.3, d: 0.1);
            void Rules(BuildingData b)
            {
                DetailKindTests.Rule(b, foot.id, 100); DetailKindTests.Rule(b, corner.id, 100);
                (b.style.details!.rules ??= new SortedDictionary<string, KindRule>(StringComparer.Ordinal))[corner.id] = new KindRule { spacing = 0.5 };
            }
            var (b, s, k) = Block(Rect, RoofType.Hip, f: Rules);
            var (b0, keep, _) = Block(Rect, RoofType.Hip, keep: true, f: Rules);
            double Bottom(BuildingData bb, int kk, Details.Item it) => Derived.FloorBase(bb, kk) + it.y + it.kind.bottom;
            int under = 0, above = 0;
            for (int kk = k; kk < b.floors.Count; kk++)
            {
                var got = Details.At(s, b, kk).Where(it => it.kind == foot || it.kind == corner).ToList();
                foreach (var it in Details.At(keep, b0, kk).Where(it => it.kind == foot || it.kind == corner))
                {
                    // where the roof comes up the wall at it: at its middle, or a corner's on both walls
                    var up = Derived.OutlineAt(b, kk); int ip = (it.i - 1 + up.Count) % up.Count;
                    double L = Geo.Hypot(up[it.i].x - up[ip].x, up[it.i].z - up[ip].z);
                    double roof = it.corner ? Math.Max(Roofs.TerraceRoofAt(s, b, kk, it.i, 0, 0.4), Roofs.TerraceRoofAt(s, b, kk, ip, L - 0.4, L))
                        : Roofs.TerraceRoofAt(s, b, kk, it.i, it.u - 0.25, it.u + 0.25);
                    bool there = got.Any(g => g.kind == it.kind && g.i == it.i && Math.Abs(g.u - it.u) < 1e-9 && Math.Abs(g.y - it.y) < 1e-9);
                    if (roof > Bottom(b0, kk, it) - Roofs.TerraceRoofClear) { Assert.False(there, $"storey {kk}: a {it.kind.rule} detail in the roof (edge {it.i}, its foot at {Bottom(b0, kk, it):0.00} m, the roof at {roof:0.00} m)"); under++; }
                    else if (roof < Bottom(b0, kk, it) - Roofs.TerraceRoofClear - 0.1) { Assert.True(there, $"storey {kk}: a {it.kind.rule} detail clear of the roof left out"); above++; }
                }
            }
            Assert.True(under > 0 && above > 0, $"{under} under the roof, {above} clear of it");
        }

        [Fact]
        public void ADetailPutByHandIsntPutInTheRoof()
        {
            var (b, s, k) = Block(Rect, RoofType.Hip);
            var up = Derived.OutlineAt(b, k); double L = Geo.Hypot(up[1].x - up[0].x, up[1].z - up[0].z);
            // an AC unit between two windows, low on the setback's first storey: the roof comes 1.4 m up the wall there
            FacadeHit At(int kk, double y) => new FacadeHit { i = 0, k0 = k, k = kk, t = 0.5, L = L, y = Derived.FloorBase(b, kk) + y };
            Assert.Equal("That would sit in the roof below", Facades.DetailAt(s, b, At(k, 0.5), DetailKinds.Ac).error);
            // a storey up it goes on, as it does on a terrace
            Assert.NotNull(Facades.DetailAt(s, b, At(k + 1, 0.5), DetailKinds.Ac).add);
            var (bt, st, kt) = Block(Rect, null);
            Assert.NotEqual("That would sit in the roof below", Facades.DetailAt(st, bt, new FacadeHit { i = 0, k0 = kt, k = kt, t = 0.5, L = L, y = Derived.FloorBase(bt, kt) + 0.5 }, DetailKinds.Ac).error);
        }

        [Fact]
        public void ItBuildsCleanly()
        {
            foreach (var outline in new[] { Rect, Ell })
                foreach (var type in new[] { RoofType.Hip, RoofType.Gable })
                {
                    var (b, s, _) = Block(outline, type);
                    Assert.Empty(CoplanarCheck.Run(Lod0.Build(s, b, solids: true).Op).Overlaps);
                    Assert.Empty(CoplanarCheck.Run(Lod1.Build(s, b, solids: true)).Overlaps);
                }
        }
    }
}
