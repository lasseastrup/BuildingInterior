using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>Artist-made facade details (<see cref="DetailKinds"/>, docs/EDITOR.md §6.12). Storey's own.</summary>
    [Collection("DetailKinds")]   // the registry is shared
    public class DetailKindTests
    {
        /// <summary>
        /// A box in the detail's own space, 2 cm off the wall (no face on the wall's plane), from <paramref name="y0"/> up:
        /// its body in Detail metal, its front in <paramref name="front"/>; front faces wound as the generator's. Without
        /// <paramref name="caps"/> it has no top or bottom, as a pipe going up the wall.
        /// </summary>
        internal static DetailKind.Mesh Box(double w, double h, double d, ColorSlot front = ColorSlot.Grille, double y0 = 0, bool caps = true)
        {
            var m = new DetailKind.Mesh(); var P = new List<P3>(); var N = new List<P3>();
            var body = new List<int>(); var face = new List<int>();
            void Q(P3 a, P3 b, P3 c, P3 e, P3 n, List<int> into)
            {
                // the generator's front faces: (b − a) × (c − a) points into the face, away from its normal
                var x = P3.Cross(b - a, c - a); if (x.x * n.x + x.y * n.y + x.z * n.z > 0) (b, e) = (e, b);
                int s = P.Count; P.AddRange(new[] { a, b, c, e }); N.AddRange(new[] { n, n, n, n });
                into.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
            }
            double x0 = -w / 2, x1 = w / 2, z0 = 0.02, z1 = d, y1 = y0 + h;
            Q(new P3(x0, y0, z1), new P3(x0, y1, z1), new P3(x1, y1, z1), new P3(x1, y0, z1), new P3(0, 0, 1), face);
            if (caps)
            {
                Q(new P3(x0, y1, z0), new P3(x0, y1, z1), new P3(x1, y1, z1), new P3(x1, y1, z0), new P3(0, 1, 0), body);
                Q(new P3(x0, y0, z0), new P3(x0, y0, z1), new P3(x1, y0, z1), new P3(x1, y0, z0), new P3(0, -1, 0), body);
            }
            Q(new P3(x0, y0, z0), new P3(x0, y1, z0), new P3(x0, y1, z1), new P3(x0, y0, z1), new P3(-1, 0, 0), body);
            Q(new P3(x1, y0, z0), new P3(x1, y1, z0), new P3(x1, y1, z1), new P3(x1, y0, z1), new P3(1, 0, 0), body);
            m.P = P.ToArray(); m.N = N.ToArray();
            m.parts.Add(new DetailKind.Part { slot = ColorSlot.DetailMetal, tris = body.ToArray() });
            m.parts.Add(new DetailKind.Part { slot = front, tris = face.ToArray() });
            return m;
        }

        internal static DetailKind Kind(Details.Anchor anchor = Details.Anchor.Wall, Details.Rule rule = Details.Rule.None,
            double w = 0.8, double h = 0.6, double d = 0.3, bool stretch = false, bool lod1 = false, double y0 = 0, string? id = null)
        {
            id ??= Guid.NewGuid().ToString("N");
            bool caps = !(anchor == Details.Anchor.Tier && stretch);   // fitted to each storey, a cap would lie on the band above
            var k = new DetailKind
            {
                id = id, name = "Test " + id.Substring(0, 4), anchor = anchor, rule = rule, w = w, h = h, d = d, stretch = stretch, bottom = y0,
                lod0 = Box(w, h, d, ColorSlot.Grille, y0, caps), lod1 = lod1 ? Box(w, h, d, ColorSlot.Dish, y0, caps) : null,
            };
            DetailKinds.Register(k);
            return k;
        }

        /// <summary>A 14 × 10 m brick block of three storeys, its door on edge 0, and none of the generator's own details by rule.</summary>
        internal static (BuildingData b, Site site) Block(Action<BuildingData>? f = null)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
            b.style = Styles.Preset("brick").Clone(); b.style.details = new DetailRules { ac = 0, vents = 0 };
            f?.Invoke(b);
            return (b, new Site(d.buildings));
        }

        internal static void Rule(BuildingData b, string id, double percent) =>
            (b.style.details!.kinds ??= new SortedDictionary<string, double>(StringComparer.Ordinal))[id] = percent;

        internal static List<int> Slot(MeshBuilder m, ColorSlot s) => Enumerable.Range(0, m.Verts).Where(i => m.C[i].slot == s).ToList();

        static List<Opening> Ops(Site s, BuildingData b, int k, int i) { var f = Details.EdgeFrameAt(b, k, i); return Facade.Ops(s, b, k, i, f.e.L, f.uS, f.uE); }

        [Fact]
        public void AnArtistsDetailGoesWhereItIsPutAndFacesOut()
        {
            var k = Kind();
            var (b0, s0) = Block(); int plain = Slot(Lod0.Build(s0, b0).Op, ColorSlot.Grille).Count;
            var (b, s) = Block(bb => bb.details.Add(new DetailData { kind = k.id, k = 1, edge = 0, t = 0.5, y = 1.1 }));
            var op = Lod0.Build(s, b).Op;
            var front = Slot(op, ColorSlot.Grille);
            Assert.Equal(plain + 4, front.Count);   // its front, once
            var fp = front.Skip(plain).Select(i => op.P[i]).ToList();
            double y0 = Derived.FloorBase(b, 1) + 1.1;
            Assert.All(fp, p => Assert.InRange(p.y, y0 - 1e-9, y0 + 0.6 + 1e-9));
            Assert.Equal(7.0, fp.Average(p => p.x), 6);   // halfway along edge 0
            Assert.All(fp, p => Assert.Equal(Dim.T_EXT + 0.3, Geo.DistToEdges(b.footprint, new Vec2(p.x, p.z)), 6));   // its front, 30 cm out of the wall
            Assert.All(fp, p => Assert.False(Geo.Pip(b.footprint, p.x, p.z)));
            // its front faces away from the building, wound as the generator's
            Assert.All(front.Skip(plain), i => Assert.True(op.N[i].z < -0.999, $"normal {op.N[i]}"));
            var I = Enumerable.Range(0, op.Tris).Select(t => (op.I[3 * t], op.I[3 * t + 1], op.I[3 * t + 2])).Where(t => op.C[t.Item1].slot == ColorSlot.Grille && op.P[t.Item1].y >= y0 - 1e-9).ToList();
            Assert.Equal(2, I.Count);
            foreach (var (a, c, e) in I)
            {
                var x = P3.Cross(op.P[c] - op.P[a], op.P[e] - op.P[a]); var n = op.N[a];
                Assert.True(x.x * n.x + x.y * n.y + x.z * n.z < 0, "front face wound as the generator's");
            }
        }

        [Fact]
        public void LodOneTakesItsOwnMeshOrTheFullOne()
        {
            var own = Kind(lod1: true); var full = Kind();
            var (b0, s0) = Block(); var plain = Lod1.Build(s0, b0);
            var (b, s) = Block(bb =>
            {
                bb.details.Add(new DetailData { kind = own.id, k = 1, edge = 2, t = 0.3, y = 1.0 });
                bb.details.Add(new DetailData { kind = full.id, k = 2, edge = 2, t = 0.7, y = 1.0 });
            });
            var lod1 = Lod1.Build(s, b);
            Assert.Equal(Slot(plain, ColorSlot.Dish).Count + 4, Slot(lod1, ColorSlot.Dish).Count);     // the LOD1 mesh's front
            Assert.Equal(Slot(plain, ColorSlot.Grille).Count + 4, Slot(lod1, ColorSlot.Grille).Count); // the full mesh's front
        }

        [Fact]
        public void AKindThatIsntRegisteredIsKeptAndNotBuilt()
        {
            string id = Guid.NewGuid().ToString("N");   // never registered
            var (b0, s0) = Block(); int plain = Lod0.Build(s0, b0).Op.Tris;
            var (b, s) = Block(bb => { bb.details.Add(new DetailData { kind = id, k = 1, edge = 0, t = 0.5, y = 1.1 }); Rule(bb, id, 50); });
            Assert.Equal(plain, Lod0.Build(s, b).Op.Tris);
            var unknown = new List<string>();
            var back = PrototypeJson.ReadBuilding(PrototypeJson.Write(b), unknown);
            Assert.Empty(unknown);
            Assert.Equal(id, Assert.Single(back.details).kind);
            Assert.Equal(50, back.style.details!.kinds![id]);
            Assert.Equal("That detail isn’t in the project", Facades.DetailAt(s, b, new FacadeHit { i = 0, k = 1, L = 14, t = 0.5, y = 5 }, id).error);
        }

        [Fact]
        public void TheFacadeToolPutsItOnAWallAndTakesItOff()
        {
            var k = Kind();
            var (b, s) = Block();
            var ops = Ops(s, b, 1, 0); double L = Details.EdgeFrameAt(b, 1, 0).e.L;
            FacadeHit Hit(double u, double above) => new FacadeHit { i = 0, k0 = 0, k = 1, L = L, t = u / L, y = Derived.FloorBase(b, 1) + above };
            var win = ops.First(o => !o.door);
            Assert.Equal("That would sit across a window or door", Facades.DetailAt(s, b, Hit((win.u0 + win.u1) / 2, (win.y0 + win.y1) / 2), k.id).error);
            // a stretch of bare wall between two windows
            var gap = ops.Zip(ops.Skip(1), (p, q) => (a: p.u1, c: q.u0)).First(g => g.c - g.a > 1.3);
            double u = (gap.a + gap.c) / 2;
            var spot = Facades.DetailAt(s, b, Hit(u, 1.2), k.id);
            Assert.Null(spot.error);
            Assert.Equal(k.id, spot.add!.kind);
            var g = spot.ghost!.Value;
            Assert.Equal(0.8, g.sx, 9); Assert.Equal(0.6, g.sy, 9); Assert.Equal(0.3, g.sz, 9);
            Assert.Null(Facades.ToggleDetail(s, b, Hit(u, 1.2), k.id));
            Assert.Equal(k.id, Assert.Single(b.details).kind);
            Assert.Equal(0, Facades.DetailAt(s, b, Hit(u, 1.2), k.id).remove);
            Assert.Null(Facades.ToggleDetail(s, b, Hit(u, 1.2), k.id));
            Assert.Empty(b.details);
            // too tall for a storey
            var tall = Kind(h: 9);
            Assert.Equal(tall.name + " is too tall for this storey", Facades.DetailAt(s, b, Hit(u, 1.2), tall.id).error);
        }

        [Fact]
        public void UnderWindowsItTakesTheWindowsTheACUnitsLeave()
        {
            var k = Kind(rule: Details.Rule.UnderWindows, h: 0.45);
            var (b, s) = Block(bb => Rule(bb, k.id, 100));
            // the upper floors' windows it fits under (its top 7 cm below the window, its bottom 25 cm above the floor), at a chance
            int Fits(double p) => Enumerable.Range(1, b.floors.Count - 1).Sum(kk => Enumerable.Range(0, b.footprint.Count).Sum(i =>
            {
                var ops = Ops(s, b, kk, i);
                return Enumerable.Range(0, ops.Count).Count(j => !ops[j].door && !ops[j].full && ops[j].y0 - 0.52 >= 0.25 && Details.Hash01(b.id, k.id, kk, i, j) < p);
            }));
            // alone: one under every window it fits under
            int got = 0;
            for (int kk = 1; kk < b.floors.Count; kk++)
                foreach (var it in Details.At(s, b, kk))
                {
                    Assert.Equal(k.id, it.kind.id);
                    Assert.Contains(Ops(s, b, kk, it.i), o => Math.Abs((o.u0 + o.u1) / 2 - it.u) < 1e-9 && Math.Abs(o.y0 - 0.07 - 0.45 - it.y) < 1e-9);
                    got++;
                }
            Assert.True(got > 0);
            Assert.Equal(Fits(1), got);
            Assert.Empty(Details.At(s, b, 0));   // never on the ground floor
            // at half: the windows its own seed picks, on every build
            Rule(b, k.id, 50);
            Assert.Equal(Fits(0.5), Enumerable.Range(1, b.floors.Count - 1).Sum(kk => Details.At(s, b, kk).Count));
            // with AC units under every window, it keeps clear of them
            Rule(b, k.id, 100); b.style.details!.ac = 100;
            for (int kk = 1; kk < b.floors.Count; kk++) Assert.All(Details.At(s, b, kk), it => Assert.Equal(DetailKinds.Ac, it.kind.id));
        }

        [Fact]
        public void BetweenWindowsItGoesInTheBareWallUnderTheCeiling()
        {
            var k = Kind(rule: Details.Rule.BareWall, w: 0.5, h: 0.4, d: 0.15);
            var (b, s) = Block(bb => Rule(bb, k.id, 100));
            int got = 0;
            for (int kk = 0; kk < b.floors.Count; kk++)
                foreach (var it in Details.At(s, b, kk))
                {
                    Assert.Equal(Derived.FloorH(b, kk) - 0.45 - 0.4, it.y, 9);
                    Assert.DoesNotContain(Ops(s, b, kk, it.i), o => o.u1 > it.u - 0.5 && o.u0 < it.u + 0.5);   // 25 cm to spare each side
                    got++;
                }
            Assert.True(got > 0);
        }

        [Fact]
        public void OverGroundFloorWindowsItIsFittedAcrossThem()
        {
            var k = Kind(Details.Anchor.Opening, Details.Rule.OverOpenings, w: 1.0, h: 0.5, d: 0.8, stretch: true, y0: -0.2);
            var (b, s) = Block(bb => { Rule(bb, k.id, 100); bb.style.ground = GroundType.Match; });   // punched windows on the ground floor
            var items = Details.At(s, b, 0);
            int windows = Enumerable.Range(0, 4).Sum(i => Ops(s, b, 0, i).Count(o => !o.door && !o.full));
            Assert.True(windows > 0);
            Assert.Equal(windows, items.Count);   // every ground-floor window, not the door
            Assert.All(items, it => { Assert.True(it.Spans); Assert.Contains(Ops(s, b, 0, it.i), o => !o.door && Math.Abs(o.y1 + Details.OverLift - it.y) < 1e-9); });
            for (int kk = 1; kk < b.floors.Count; kk++) Assert.Empty(Details.At(s, b, kk));
            // its front spans the window and 25 cm past each side
            var op = Lod0.Build(s, b).Op; var (b0, s0) = Block(bb => bb.style.ground = GroundType.Match); int plain = Slot(Lod0.Build(s0, b0).Op, ColorSlot.Grille).Count;
            var first = items[0]; var e = Details.EdgeFrameAt(b, 0, first.i).e;
            double Along(P3 p) => (p.x - e.a.x) * e.u.x + (p.z - e.a.z) * e.u.z;
            double Out(P3 p) => (p.x - e.a.x) * e.w.x + (p.z - e.a.z) * e.w.z;
            var fp = Slot(op, ColorSlot.Grille).Skip(plain).Select(i => op.P[i])
                .Where(p => Math.Abs(Out(p) - Dim.T_EXT - 0.8) < 1e-6 && Along(p) > first.u0 - 0.01 && Along(p) < first.u1 + 0.01).ToList();
            Assert.Equal(4, fp.Count);
            Assert.Equal(first.u1 - first.u0, fp.Max(Along) - fp.Min(Along), 6);
            Assert.Equal(Derived.FloorBase(b, 0) + first.y - 0.2, fp.Min(p => p.y), 6);   // it hangs 20 cm below its pivot
        }

        [Fact]
        public void UpTheWallItIsOnEveryStoreyItClimbs()
        {
            var k = Kind(Details.Anchor.Tier, w: 0.4, h: 1.0, d: 0.2, stretch: true);
            var (b, s) = Block(bb => bb.details.Add(new DetailData { kind = k.id, k = 0, edge = 2, t = 0.5 }));
            var op = Lod0.Build(s, b).Op; var (b0, s0) = Block(); int plain = Slot(Lod0.Build(s0, b0).Op, ColorSlot.Grille).Count;
            // one front a storey, fitted to the storey's height
            var fronts = Slot(op, ColorSlot.Grille).Skip(plain).Select(i => op.P[i]).Chunk(4).ToList();
            Assert.Equal(b.floors.Count, fronts.Count);
            for (int kk = 0; kk < b.floors.Count; kk++)
            {
                var it = Assert.Single(Details.At(s, b, kk));
                Assert.Equal((k.id, 0.0, 0, b.floors.Count), (it.kind.id, it.y, it.ka, it.k1));
                double y0 = Derived.FloorBase(b, kk);
                Assert.Equal(y0, fronts[kk].Min(p => p.y), 9); Assert.Equal(y0 + Derived.FloorH(b, kk), fronts[kk].Max(p => p.y), 9);
            }
        }

        [Fact]
        public void NothingOfItsSitsInTheWallsPlane()
        {
            var wall = Kind(rule: Details.Rule.UnderWindows); var bare = Kind(rule: Details.Rule.BareWall, w: 0.5, h: 0.4, d: 0.15);
            var over = Kind(Details.Anchor.Opening, Details.Rule.OverOpenings, w: 1.0, h: 0.5, d: 0.8, stretch: true, y0: -0.2);
            var up = Kind(Details.Anchor.Tier, w: 0.4, h: 1.0, d: 0.2, stretch: true);
            var (b, s) = Block(bb =>
            {
                bb.style.details = new DetailRules { ac = 30, vents = 12 }; bb.style.ground = GroundType.Match;
                Rule(bb, wall.id, 60); Rule(bb, bare.id, 60); Rule(bb, over.id, 100);
                bb.details.Add(new DetailData { kind = up.id, k = 0, edge = 2, t = 0.5 });
                bb.details.Add(new DetailData { kind = DetailKinds.Awning, k = 0, edge = 1, t = 0.5 });
            });
            foreach (var m in new[] { Lod0.Build(s, b).Op, Lod1.Build(s, b) })
            {
                var rep = CoplanarCheck.Run(m);
                Assert.True(rep.Overlaps.Count == 0, string.Join("\n", rep.Overlaps.Take(10)));
            }
        }

        [Fact]
        public void TheStyleKeepsItsRulesForArtistsKinds()
        {
            string id = Guid.NewGuid().ToString("N");
            var st = Styles.Preset("brick").Clone(); st.details = new DetailRules { ac = 20, kinds = new SortedDictionary<string, double>(StringComparer.Ordinal) { [id] = 35 } };
            var json = PrototypeJson.WriteStyle(st);
            var unknown = new List<string>();
            var back = PrototypeJson.ReadStyle(json, unknown);
            Assert.Empty(unknown);
            Assert.Equal((20.0, 35.0), (back.details!.ac!.Value, back.details.kinds![id]));
            // a key that is neither ac, vents nor a kind's id is still unknown
            var unknown2 = new List<string>();
            PrototypeJson.ReadStyle(json.Replace("\"ac\"", "\"acs\""), unknown2);
            Assert.Contains(unknown2, u => u.EndsWith("acs"));
            // a copy of the style has rules of its own
            var c = st.Clone(); c.details!.kinds![id] = 5;
            Assert.Equal(35, st.details.kinds[id]);
            // and a style with no rules for the artist's kinds reads back with none
            Assert.Null(PrototypeJson.ReadStyle(PrototypeJson.WriteStyle(Styles.Preset("brick"))).details?.kinds);
        }

        [Fact]
        public void TheGeneratorsOwnFiveStayTheGenerators()
        {
            DetailKinds.Register(new DetailKind { id = DetailKinds.Ac, name = "Not mine", w = 3, lod0 = Box(3, 1, 1) });
            DetailKinds.Remove(DetailKinds.Vent);
            var ac = DetailKinds.Get(DetailKinds.Ac)!;
            Assert.Equal(("AC unit", 0.75, (DetailKind.Mesh?)null), (ac.name, ac.w, ac.lod0));
            Assert.NotNull(DetailKinds.Get(DetailKinds.Vent));
            Assert.Equal(new[] { "ac", "vent", "dish", "escape", "awning" }, DetailKinds.BuiltIn);
            Assert.Equal("escape", DetailKinds.Id("Escape"));
            string id = Guid.NewGuid().ToString("N"); Assert.Equal(id, DetailKinds.Id(id));
            Assert.True(DetailKinds.IsAssetId(id)); Assert.False(DetailKinds.IsAssetId("ac"));
        }
    }
}
