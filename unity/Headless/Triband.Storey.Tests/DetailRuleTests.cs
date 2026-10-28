using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Validate;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Artist-made details placed by Storey's own rules (docs/EDITOR.md §6.12): scattered over the walls, in a row along the
    /// top or foot of the wall, and up the corners; a style's settings for each kind (<see cref="KindRule"/>), its colour
    /// for a kind's main colour, and kinds left out of LOD1. Storey's own.
    /// </summary>
    [Collection("DetailKinds")]   // the registry is shared
    public class DetailRuleTests
    {
        readonly ITestOutputHelper output;
        public DetailRuleTests(ITestOutputHelper o) { output = o; }

        static DetailKind Kind(Details.Rule rule, double w = 0.6, double h = 0.3, double d = 0.05, double y0 = 0, bool noLod1 = false, string? id = null)
        {
            var k = DetailKindTests.Kind(Details.Anchor.Wall, rule, w, h, d, y0: y0, id: id);
            if (noLod1) { k.noLod1 = true; DetailKinds.Register(k); }
            return k;
        }

        static void Set(BuildingData b, string id, double percent, KindRule? r = null)
        {
            DetailKindTests.Rule(b, id, percent);
            if (r != null) (b.style.details!.rules ??= new SortedDictionary<string, KindRule>(StringComparer.Ordinal))[id] = r;
        }

        static List<Details.Item> Items(Site s, BuildingData b, DetailKind k) =>
            Enumerable.Range(0, b.floors.Count).SelectMany(f => Details.At(s, b, f).Select(it => (f, it))).Where(x => x.it.kind == k).Select(x => x.it).ToList();

        static List<(int k, Details.Item it)> ItemsByFloor(Site s, BuildingData b, DetailKind k) =>
            Enumerable.Range(0, b.floors.Count).SelectMany(f => Details.At(s, b, f).Where(it => it.kind == k).Select(it => (f, it))).ToList();

        static List<Opening> Ops(Site s, BuildingData b, int k, int i) { var f = Details.EdgeFrameAt(b, k, i); return Facade.Ops(s, b, k, i, f.e.L, f.uS, f.uE); }

        [Fact]
        public void AKindsSettingsGoInPlaceOfItsChanceAndTheChanceAloneIsWrittenAsBefore()
        {
            string a = Guid.NewGuid().ToString("N"), c = Guid.NewGuid().ToString("N"), e = Guid.NewGuid().ToString("N");
            var st = Styles.Preset("brick").Clone();
            st.details = new DetailRules { ac = 20, kinds = new SortedDictionary<string, double>(StringComparer.Ordinal) { [a] = 35, [c] = 60 } };
            st.details.rules = new SortedDictionary<string, KindRule>(StringComparer.Ordinal)
            {
                [c] = new KindRule { density = 1.25, spacing = 0.12, floors = DetailFloors.Upper, color = "#AABBCC" },
                [e] = new KindRule { per = 2, corners = DetailCorners.Inward },
            };
            string json = PrototypeJson.WriteStyle(st);
            Assert.Contains($"\"{a}\":35", json.Replace(" ", ""));
            Assert.Contains("\"floors\":\"upper\"", json.Replace(" ", ""));
            var back = PrototypeJson.ReadStyle(json, new List<string>());
            Assert.Equal(35, back.details!.kinds![a]);
            Assert.Equal(60, back.details.kinds[c]);
            Assert.False(back.details.kinds.ContainsKey(e));   // settings without a chance: placed by hand only, its settings kept
            var rc = back.details.rules![c]; var re = back.details.rules[e];
            Assert.Equal((1.25, 0.12, DetailFloors.Upper, "#AABBCC"), (rc.density!.Value, rc.spacing!.Value, rc.floors, rc.color));
            Assert.Equal((2, DetailCorners.Inward), (re.per!.Value, re.corners));
            Assert.False(back.details.rules.ContainsKey(a));
            // a copy is a copy
            var copy = back.Clone(); copy.details!.rules![c].density = 9;
            Assert.Equal(1.25, back.details.rules[c].density);
            // an unknown setting is reported, as any unknown key is
            var unknown = new List<string>();
            PrototypeJson.ReadStyle(json.Replace("\"density\"", "\"denisty\""), unknown);
            Assert.Contains(unknown, u => u.EndsWith("denisty"));
        }

        [Fact]
        public void AStyleWithoutSettingsWritesNoNewKeys()
        {
            string id = Guid.NewGuid().ToString("N");
            var st = Styles.Preset("brick").Clone(); st.details = new DetailRules { kinds = new SortedDictionary<string, double>(StringComparer.Ordinal) { [id] = 50 } };
            string json = PrototypeJson.WriteStyle(st).Replace(" ", "");
            Assert.Contains($"\"details\":{{\"{id}\":50}}", json);
            st.details.rules = new SortedDictionary<string, KindRule>(StringComparer.Ordinal) { [id] = new KindRule() };   // all defaults: nothing to say
            Assert.Equal(json, PrototypeJson.WriteStyle(st).Replace(" ", ""));
        }

        [Fact]
        public void ScatteredDetailsSpreadOverTheWallsClearOfOpeningsAndOneAnother()
        {
            var k = Kind(Details.Rule.Scatter, id: "5ca77e2ed0000000000000000000a001");   // its seed, the same every run
            var (b, s) = DetailKindTests.Block(bb => { Floors.SetCount(bb, 4); Set(bb, k.id, 100, new KindRule { density = 0.6, spacing = 0.2 }); });
            var all = ItemsByFloor(s, b, k);
            Assert.True(all.Count > 40, $"{all.Count} scattered");
            foreach (var g in all.GroupBy(x => (x.k, x.it.i)))
            {
                var ops = Ops(s, b, g.Key.k, g.Key.i);
                var list = g.Select(x => x.it).ToList();
                foreach (var it in list)
                {
                    double u0 = it.u - k.w / 2, u1 = it.u + k.w / 2, v0 = it.y + k.bottom, v1 = v0 + k.h;
                    Assert.All(ops, o => Assert.False(u0 < o.Right && u1 > o.Left && v0 < o.Top && v1 > o.Bottom, $"over an opening on storey {g.Key.k}, edge {g.Key.i}"));
                    Assert.InRange(v0, 0.1, Derived.FloorH(b, g.Key.k));
                    foreach (var other in list)
                        if (other != it) Assert.False(Math.Abs(other.u - it.u) < k.w + 0.2 - 1e-9 && Math.Abs(other.y - it.y) < k.h + 0.2 - 1e-9, "two too close");
                }
            }
            // the same on every build, and different from storey to storey
            Assert.Equal(all.Select(x => (x.k, x.it.i, x.it.u, x.it.y)), ItemsByFloor(s, b, k).Select(x => (x.k, x.it.i, x.it.u, x.it.y)));
            var f1 = all.Where(x => x.k == 1 && x.it.i == 2).Select(x => Math.Round(x.it.u, 3)).ToList();
            var f2 = all.Where(x => x.k == 2 && x.it.i == 2).Select(x => Math.Round(x.it.u, 3)).ToList();
            Assert.NotEqual(f1, f2);
            // about as many as the density asks: a 14 × 10 m block's walls, less the openings
            int half = ItemsByFloor(s, b, k).Count;
            b.style.details!.rules![k.id].density = 0.3;
            Assert.InRange(ItemsByFloor(s, b, k).Count, half * 0.35, half * 0.65);
        }

        [Fact]
        public void AScatteredKindGoesOnTheStoreysItsStyleSays()
        {
            var k = Kind(Details.Rule.Scatter);
            var (b, s) = DetailKindTests.Block(bb => { bb.style.ground = GroundType.Match; Set(bb, k.id, 100, new KindRule { floors = DetailFloors.Upper }); });
            var up = ItemsByFloor(s, b, k);
            Assert.NotEmpty(up); Assert.DoesNotContain(up, x => x.k == 0);
            b.style.details!.rules![k.id].floors = DetailFloors.Ground;
            var ground = ItemsByFloor(s, b, k);
            Assert.NotEmpty(ground); Assert.All(ground, x => Assert.Equal(0, x.k));
        }

        [Fact]
        public void ScatteredDetailsKeepClearOfHandPlacedOnesAndOfOneAnother()
        {
            var k = Kind(Details.Rule.Scatter); var m = Kind(Details.Rule.Scatter, 0.4, 0.4);
            var (b, s) = DetailKindTests.Block(bb =>
            {
                Set(bb, k.id, 100, new KindRule { density = 2 }); Set(bb, m.id, 100, new KindRule { density = 2 });
                bb.details.Add(new DetailData { kind = m.id, k = 1, edge = 2, t = 0.5, y = 1.0 });
            });
            var items = Details.At(s, b, 1).Where(it => it.i == 2).ToList();
            Assert.Contains(items, it => it.man != null);
            for (int x = 0; x < items.Count; x++)
                for (int y = x + 1; y < items.Count; y++)
                {
                    var p = items[x]; var q = items[y];
                    bool across = Math.Abs(p.u - q.u) < (p.Width + q.Width) / 2, up = p.y + p.kind.bottom < q.y + q.kind.bottom + q.kind.h && q.y + q.kind.bottom < p.y + p.kind.bottom + p.kind.h;
                    Assert.False(across && up, $"{p.kind.name} at {p.u:0.00} and {q.kind.name} at {q.u:0.00} overlap");
                }
        }

        [Fact]
        public void ARowAlongTheTopHasSoManyToABayOnTheTopStoreyOnly()
        {
            var k = Kind(Details.Rule.AlongTop, 0.5, 0.3, y0: -0.3);
            var (b, s) = DetailKindTests.Block(bb => Set(bb, k.id, 100));
            var items = ItemsByFloor(s, b, k);
            int slots = 2 * (int)Math.Floor(14 / b.style.bay) + 2 * (int)Math.Floor(10 / b.style.bay);
            Assert.Equal(slots, items.Count);
            Assert.All(items, x => { Assert.Equal(b.floors.Count - 1, x.k); Assert.Equal(Derived.FloorH(b, x.k), x.it.y, 9); });
            b.style.details!.rules = new SortedDictionary<string, KindRule>(StringComparer.Ordinal) { [k.id] = new KindRule { per = 2 } };
            Assert.Equal(2 * slots, ItemsByFloor(s, b, k).Count);
        }

        [Fact]
        public void KindsInARowShareItsPlacesByTheirChances()
        {
            var a = Kind(Details.Rule.AlongTop, 0.5, 0.3, y0: -0.3); var c = Kind(Details.Rule.AlongTop, 0.5, 0.3, y0: -0.3);
            var (b, s) = DetailKindTests.Block(bb => { Set(bb, a.id, 60); Set(bb, c.id, 40); });
            int top = b.floors.Count - 1;
            var items = Details.At(s, b, top);
            var ia = items.Where(it => it.kind == a).ToList(); var ic = items.Where(it => it.kind == c).ToList();
            Assert.NotEmpty(ia); Assert.NotEmpty(ic);
            int slots = 2 * (int)Math.Floor(14 / b.style.bay) + 2 * (int)Math.Floor(10 / b.style.bay);
            Assert.Equal(slots, ia.Count + ic.Count);   // every place, each taken once
            Assert.Empty(ia.Where(p => ic.Any(q => q.i == p.i && Math.Abs(q.u - p.u) < 1e-9)));
        }

        [Fact]
        public void ARowAlongTheFootNeverCrossesADoor()
        {
            var k = Kind(Details.Rule.AlongFoot, 0.5, 0.3);
            var (b, s) = DetailKindTests.Block(bb => Set(bb, k.id, 100, new KindRule { per = 3 }));
            var items = ItemsByFloor(s, b, k);
            Assert.NotEmpty(items);
            Assert.All(items, x => { Assert.Equal(0, x.k); Assert.Equal(0, x.it.y); });
            var doors = Ops(s, b, 0, 0).Where(o => o.door).ToList();
            Assert.NotEmpty(doors);
            Assert.All(items.Where(x => x.it.i == 0), x => Assert.All(doors, o => Assert.False(x.it.u + 0.25 > o.Left && x.it.u - 0.25 < o.Right)));
            int slots = 3 * (2 * (int)Math.Floor(14 / b.style.bay) + 2 * (int)Math.Floor(10 / b.style.bay));
            Assert.True(items.Count < slots);
        }

        [Fact]
        public void CornerDetailsStackUpTheCornersItsStyleNamesFacingOut()
        {
            var k = Kind(Details.Rule.Corners, 0.3, 0.4, 0.1, y0: -0.2);
            var (b, s) = DetailKindTests.Block(bb => Set(bb, k.id, 100, new KindRule { spacing = 0.75 }));
            var items = ItemsByFloor(s, b, k);
            int perStorey = Enumerable.Range(0, b.floors.Count).Sum(f => (int)Math.Floor(Derived.FloorH(b, f) / 0.75));
            Assert.Equal(4 * perStorey, items.Count);   // a box has four corners, all standing out
            Assert.All(items, x => { Assert.True(x.it.corner); Assert.InRange(x.it.y + k.bottom, 0, Derived.FloorH(b, x.k) - k.h + 1e-9); });
            // each faces out along its corner's bisector, from outside its walls
            var (b0, s0) = DetailKindTests.Block(bb => Floors.SetCount(bb, b.floors.Count)); int plain = DetailKindTests.Slot(Lod0.Build(s0, b0).Op, ColorSlot.Grille).Count;
            var op = Lod0.Build(s, b).Op; var front = DetailKindTests.Slot(op, ColorSlot.Grille).Skip(plain).ToList();
            Assert.Equal(4 * items.Count, front.Count);
            Assert.All(front, i =>
            {
                var n = op.N[i]; var p = op.P[i];
                Assert.Equal(Math.Sqrt(0.5), Math.Abs(n.x), 6); Assert.Equal(Math.Sqrt(0.5), Math.Abs(n.z), 6);
                // out beyond the corner it is at, the way it faces
                double cx = p.x < 7 ? 0 : 14, cz = p.z < 5 ? 0 : 10;
                Assert.True((p.x - cx) * n.x > 0 && (p.z - cz) * n.z > 0, $"{p} faces {n}");
            });
            // a box has no corner set in
            b.style.details!.rules![k.id].corners = DetailCorners.Inward;
            Assert.Empty(ItemsByFloor(s, b, k));
        }

        [Fact]
        public void ABareWallOrTwoQuietOnesTakeTheCornerDetailsBetweenThem()
        {
            var k = Kind(Details.Rule.Corners, 0.3, 0.4, 0.1, y0: -0.2);
            var (b, s) = DetailKindTests.Block(bb => Set(bb, k.id, 100, new KindRule { spacing = 0.75 }));
            int all = ItemsByFloor(s, b, k).Count, per = all / 4;
            Tiers.SetState(b, 0, 0, EdgeState.Blank);   // blank: no windows, the corners stay
            Assert.Equal(all, ItemsByFloor(s, b, k).Count);
            Tiers.SetState(b, 0, 0, EdgeState.Quiet);   // one quiet wall: its corners stay
            Assert.Equal(all, ItemsByFloor(s, b, k).Count);
            Tiers.SetState(b, 0, 1, EdgeState.Quiet);   // two quiet walls: the corner between them goes
            Assert.Equal(all - per, ItemsByFloor(s, b, k).Count);
            Tiers.SetState(b, 0, 1, EdgeState.Open); Tiers.SetState(b, 0, 0, EdgeState.Bare);   // a bare wall: both its corners go
            Assert.Equal(all - 2 * per, ItemsByFloor(s, b, k).Count);
        }

        [Fact]
        public void AnLShapeHasOneCornerSetIn()
        {
            var k = Kind(Details.Rule.Corners, 0.3, 0.4, 0.1, y0: -0.2);
            var (b, s) = DetailKindTests.Block(bb =>
            {
                bb.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 6), new Vec2(8, 6), new Vec2(8, 12), new Vec2(0, 12) };
                Set(bb, k.id, 100, new KindRule { corners = DetailCorners.Inward });
            });
            var items = ItemsByFloor(s, b, k);
            Assert.NotEmpty(items);
            Assert.All(items, x => Assert.Equal(3, x.it.i));   // the corner at (8, 6), where edge 3 starts
            b.style.details!.rules![k.id].corners = DetailCorners.All;
            Assert.Equal(6, ItemsByFloor(s, b, k).Select(x => x.it.i).Distinct().Count());
            Assert.Empty(CoplanarCheck.Run(Lod0.Build(s, b).Op).Overlaps);
        }

        [Fact]
        public void AStylesColourReplacesAKindsMainColour()
        {
            var k = Kind(Details.Rule.Scatter);   // more Detail metal triangles (its sides and caps) than Grille (its front)
            Assert.Equal(ColorSlot.DetailMetal, k.main);
            var (b, s) = DetailKindTests.Block(bb => Set(bb, k.id, 100));
            var plain = Lod0.Build(s, b).Op;
            var slot = KindColors.SlotOf("#123456")!.Value;
            Assert.Empty(DetailKindTests.Slot(plain, slot));
            b.style.details!.rules = new SortedDictionary<string, KindRule>(StringComparer.Ordinal) { [k.id] = new KindRule { color = "#123456" } };
            var op = Lod0.Build(s, b).Op;
            Assert.Equal(DetailKindTests.Slot(plain, ColorSlot.DetailMetal).Count - DetailKindTests.Slot(op, ColorSlot.DetailMetal).Count, DetailKindTests.Slot(op, slot).Count);
            Assert.NotEmpty(DetailKindTests.Slot(op, slot));
            Assert.Equal(DetailKindTests.Slot(plain, ColorSlot.Grille).Count, DetailKindTests.Slot(op, ColorSlot.Grille).Count);   // the rest keep theirs
        }

        [Fact]
        public void AKindLeftOutOfLodOneCostsLodOneNothing()
        {
            var k = Kind(Details.Rule.Scatter, noLod1: true);
            var (b0, s0) = DetailKindTests.Block();
            var (b, s) = DetailKindTests.Block(bb => Set(bb, k.id, 100));
            Assert.True(Lod0.Build(s, b).Op.Tris > Lod0.Build(s0, b0).Op.Tris);
            Assert.Equal(Lod1.Build(s0, b0).Tris, Lod1.Build(s, b).Tris);
        }

        [Fact]
        public void NewRulesFitOnlyAKindOnAWall()
        {
            foreach (var r in new[] { Details.Rule.Scatter, Details.Rule.AlongTop, Details.Rule.AlongFoot, Details.Rule.Corners })
            {
                Assert.True(Details.RuleFits(Details.Anchor.Wall, r));
                Assert.False(Details.RuleFits(Details.Anchor.Tier, r));
                Assert.False(Details.RuleFits(Details.Anchor.Opening, r));
            }
        }

        [Fact]
        public void APrefabsDetailBakesAgainstAWholeWallItsPivotTheRootsOrigin()
        {
            // a 40 × 20 cm block from 10 cm into the wall to 5 cm out of it, its pivot 1 m left and 2 m up of the root's origin
            var P = new List<P3>(); var N = new List<P3>(); var T = new List<int>();
            void Q(P3 a, P3 b, P3 c, P3 e, P3 n)
            {
                var x = P3.Cross(b - a, c - a); if (x.x * n.x + x.y * n.y + x.z * n.z > 0) (b, e) = (e, b);
                int at = P.Count; P.AddRange(new[] { a, b, c, e }); N.AddRange(new[] { n, n, n, n }); T.AddRange(new[] { at, at + 1, at + 2, at, at + 2, at + 3 });
            }
            double x0 = -0.2, x1 = 0.2, y0 = 0, y1 = 0.2, z0 = -0.1, z1 = 0.05;
            Q(new P3(x0, y0, z1), new P3(x0, y1, z1), new P3(x1, y1, z1), new P3(x1, y0, z1), new P3(0, 0, 1));    // front
            Q(new P3(x0, y0, z0), new P3(x0, y1, z0), new P3(x1, y1, z0), new P3(x1, y0, z0), new P3(0, 0, -1));   // back, in the wall
            Q(new P3(x0, y1, z0), new P3(x0, y1, z1), new P3(x1, y1, z1), new P3(x1, y1, z0), new P3(0, 1, 0));    // top, half in the wall
            var src = new KindBake.Source { P = P.ToArray(), N = N.ToArray(), M = new double[] { 1, 0, 0, 1, 0, 1, 0, 2, 0, 0, 1, 0 } };
            src.subs.Add(new KindBake.Sub { tris = T.ToArray(), key = "m" });
            var r = KindBake.Bake(new[] { src }, KindBake.Hole.None, false);
            Assert.Equal(2, r.hidden);   // the back
            Assert.Equal(4, r.Tris);     // the front and the top, which shows where it stands out
            Assert.Equal(1.0, (r.min.x + r.max.x) / 2, 9); Assert.Equal(2.0, r.min.y, 9); Assert.Equal(0.05, r.max.z, 9);
        }

        [Fact]
        public void CheckOnABuildingTriesADetailByItsRule()
        {
            var k = Kind(Details.Rule.Scatter, noLod1: true, id: "5ca77e2ed0000000000000000000a002");
            var t = KindTrial.Run(k);
            Assert.True(t.placed > 20, $"{t.placed} placed");
            Assert.True(t.lod0 > t.plainLod0);
            Assert.Equal(t.plainLod1, t.lod1);
            Assert.Empty(t.overlaps);
            var byHand = KindTrial.Run(DetailKindTests.Kind());
            Assert.Equal(6, byHand.placed);
        }

        /// <summary>
        /// What brick patches cost, as the old generator's industrial themes used them (Bricks01: two bricks, five faces each,
        /// 20 triangles): on a 20 × 12 m block of six storeys at its densities, against the block's own triangles.
        /// </summary>
        [Fact]
        public void BrickPatchesCostLodZeroLittleAndLodOneNothing()
        {
            var brick = new DetailKind.Mesh(); var P = new List<P3>(); var N = new List<P3>(); var tris = new List<int>();
            void Brick(double x0, double x1, double y0, double y1)
            {
                var one = DetailKindTests.Box(x1 - x0, y1 - y0, 0.04, ColorSlot.Wall, y0);
                int at = P.Count;
                foreach (var p in one.P) P.Add(new P3(p.x + (x0 + x1) / 2, p.y, p.z));
                N.AddRange(one.N);
                foreach (var part in one.parts) foreach (int t in part.tris) tris.Add(at + t);
            }
            Brick(-0.3, 0.1, 0, 0.12); Brick(-0.1, 0.3, 0.16, 0.28);
            brick.P = P.ToArray(); brick.N = N.ToArray(); brick.parts.Add(new DetailKind.Part { slot = ColorSlot.Trim, tris = tris.ToArray() });
            var k = new DetailKind { id = "b51c4500000000000000000000000001", name = "Bricks", anchor = Details.Anchor.Wall, rule = Details.Rule.Scatter, w = 0.6, h = 0.28, d = 0.04, lod0 = brick, noLod1 = true };
            DetailKinds.Register(k);
            int bt = tris.Count / 3;
            (BuildingData b, Site s) Block(double? density)
            {
                var d = new StoreyDocument();
                var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
                b.footprint = new() { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 12), new Vec2(0, 12) };
                Floors.SetCount(b, 6);
                b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 }); b.details.Clear(); b.shafts.Clear();
                b.style = Styles.Preset("brick").Clone(); b.style.details = new DetailRules { ac = 0, vents = 0 };
                if (density is double x) Set(b, k.id, 100, new KindRule { density = x, spacing = 0.05 });
                return (b, new Site(d.buildings));
            }
            var (p0, ps) = Block(null); var plain = Lod0.Build(ps, p0).Op; int plain1 = Lod1.Build(ps, p0).Tris;
            foreach (var density in new[] { 0.15, 0.5, 2.0 })   // the old themes' lowest, most usual and highest
            {
                var (b, s) = Block(density);
                int n = ItemsByFloor(s, b, k).Count;
                var op = Lod0.Build(s, b).Op;
                output.WriteLine($"density {density}/m²: {n} patches, LOD0 +{op.Verts - plain.Verts} vertices, +{op.Tris - plain.Tris} triangles ({100.0 * (op.Tris - plain.Tris) / plain.Tris:0}% of the block's {plain.Tris}); LOD1 +{Lod1.Build(s, b).Tris - plain1}");
                Assert.Equal(n * bt, op.Tris - plain.Tris);
                Assert.Equal(plain1, Lod1.Build(s, b).Tris);
                Assert.Empty(CoplanarCheck.Run(op).Overlaps);
            }
        }
    }
}
