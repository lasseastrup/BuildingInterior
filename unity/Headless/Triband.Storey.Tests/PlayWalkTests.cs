using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The prototype's walks (docs/PLAY.md, slices 5.1 and 5.2) replayed frame by frame: the same held keys and camera
    /// yaw, the player step, the follow camera, buildings in the way, the view and the sliding walls, compared with what
    /// the prototype recorded each frame in <c>play.json</c>. Also its camera-to-player segment tests.
    /// </summary>
    public class PlayWalkTests
    {
        static JsonElement Root => PlayWorldTests.Play.Value.RootElement;
        const double Dt = 1.0 / 30;

        public static IEnumerable<object[]> Walks()
        {
            int n = Root.GetProperty("walks").GetArrayLength();
            for (int i = 0; i < n; i++) yield return new object[] { i };
        }

        static OccluderMode ModeOf(string s) => s switch { "sink" => OccluderMode.Sink, "slice" => OccluderMode.Slice, "cutout" => OccluderMode.Cutout, "fade" => OccluderMode.Fade, _ => OccluderMode.Off };

        [Theory, MemberData(nameof(Walks))]
        public void WalksMatchFrameByFrame(int index)
        {
            var w = Root.GetProperty("walks")[index];
            var (doc, _) = PlayWorldTests.World("demo");
            var world = new PlayWorld(new Site(doc.buildings));   // its own: the occlusion state must start clean
            var o = w.GetProperty("occ");
            var settings = new OcclusionSettings { mode = ModeOf(o.GetProperty("outside").GetString()!), cutaway = o.GetProperty("cut").GetBoolean(), stub = o.GetProperty("cutH").GetDouble(), baseHeight = o.GetProperty("baseH").GetDouble(), holeRadius = o.GetProperty("holeR").GetDouble(), assist = o.GetProperty("assist").GetBoolean(), wallMargin = 0, holdAfterExit = false };   // the prototype's
            var core = new OcclusionCore(world, settings);
            var start = w.GetProperty("start").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var c = w.GetProperty("cam").EnumerateArray().Select(e => e.GetDouble()).ToArray();
            var p = new PlayerState(); p.Spawn(start[0], start[1], start[2]);
            var cam = new FollowCamera { yaw = c[0], pitch = c[1], dist = c[2], assist = 0 }; cam.Snap(p);
            string name = $"{doc.buildings[w.GetProperty("building").GetInt32()].name} ({o.GetProperty("outside").GetString()}{(w.TryGetProperty("shaft", out _) ? ", stairs" : "")})";

            int f = 0;
            foreach (var fr in w.GetProperty("frames").EnumerateArray())
            {
                var input = fr.GetProperty("input");
                bool K(string k) => input.TryGetProperty(k, out var v) && v.GetBoolean();
                if (input.TryGetProperty("yaw", out var yv)) cam.yaw = yv.GetDouble();
                var move = new MoveInput((K("KeyD") ? 1 : 0) - (K("KeyA") ? 1 : 0), (K("KeyW") ? 1 : 0) - (K("KeyS") ? 1 : 0), K("ShiftLeft"));
                Walker.Step(world, p, move, cam.yaw, Dt);
                cam.Follow(p, Dt);
                cam.assist = core.Assist;
                var pos = cam.Position();
                core.Frame(p, pos, (cam.tx, cam.ty, cam.tz), Dt);

                string at = $"{name}, frame {f}";
                var pl = fr.GetProperty("player");
                Near(pl.GetProperty("x").GetDouble(), p.x, $"{at}: player x"); Near(pl.GetProperty("y").GetDouble(), p.y, $"{at}: player y"); Near(pl.GetProperty("z").GetDouble(), p.z, $"{at}: player z");
                var cp = fr.GetProperty("cam").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                Near(cp[0], pos.x, $"{at}: camera x"); Near(cp[1], pos.y, $"{at}: camera y"); Near(cp[2], pos.z, $"{at}: camera z");
                Near(fr.GetProperty("assist").GetDouble(), core.Assist, $"{at}: assist");
                string? act = fr.GetProperty("active").ValueKind == JsonValueKind.Null ? null : fr.GetProperty("active").GetString();
                Assert.True(act == core.View.active?.id, $"{at}: in {core.View.active?.id ?? "nothing"}, the prototype {act ?? "nothing"}");
                if (act != null)
                {
                    double clip = fr.GetProperty("clipY").GetDouble();
                    Near(clip, core.View.clipY, $"{at}: clip");
                    var cut = fr.GetProperty("cut").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                    Assert.True((cut[0] > 0.5) == core.View.cutOn, $"{at}: cutaway on");
                    Near(cut[2], core.View.cutBase, $"{at}: cut base"); Near(cut[3], core.View.cutTop, $"{at}: cut top");
                    var uc = fr.GetProperty("ucam").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                    Near(uc[0], core.View.cx, $"{at}: cutaway camera x"); Near(uc[2], core.View.cz, $"{at}: cutaway camera z");
                }
                // buildings in the way
                var want = fr.GetProperty("occ").EnumerateArray().ToList();
                var got = core.Active.Where(r => r.slot >= 0).ToList();
                Assert.True(want.Count == got.Count, $"{at}: {got.Count} buildings in the way ({string.Join(", ", got.Select(r => r.b.name))}), the prototype {want.Count} ({string.Join(", ", want.Select(e => e.GetProperty("id").GetString()))})");
                foreach (var e in want)
                {
                    var r = got.FirstOrDefault(x => x.b.id == e.GetProperty("id").GetString());
                    Assert.True(r != null, $"{at}: {e.GetProperty("id").GetString()} should be in the way");
                    Assert.True(r!.slot == e.GetProperty("slot").GetInt32(), $"{at}: {r.b.name} in slot {r.slot}, the prototype {e.GetProperty("slot").GetInt32()}");
                    Near(e.GetProperty("a").GetDouble(), r.a, $"{at}: {r.b.name} fade");
                    Near(e.GetProperty("t").GetDouble(), r.t, $"{at}: {r.b.name} sink time");
                    var row = e.GetProperty("row").EnumerateArray().Select(x => x.GetDouble()).ToArray();
                    for (int j = 0; j < row.Length; j++)
                    {
                        double g = core.Rows[r.slot * OcclusionCore.RowTexels * 4 + j];
                        Assert.True(Math.Abs(g - row[j]) <= 1e-4 * Math.Max(1, Math.Abs(row[j])), $"{at}: {r.b.name} row [{j}] = {g}, the prototype {row[j]}");
                    }
                }
                // the sliding walls: those near the player's storey, which the cutaway works on (the prototype's on every
                // storey; the shaders read the player's alone, docs/PERFORMANCE.md §10.1), the same as the prototype's
                bool Near0((string id, int i) w)
                {
                    var bw = world.Site.ById(w.id); if (bw == null || core.View.active == null) return false;
                    double yb = world.ShapeOf(bw).WallBase[w.i];
                    return yb >= core.View.cutBase - OcclusionCore.CutBand / 2 && yb < core.View.cutTop + OcclusionCore.CutBand / 2;
                }
                var wantWalls = fr.GetProperty("walls").EnumerateArray().ToDictionary(e => (e.GetProperty("id").GetString()!, e.GetProperty("i").GetInt32()), e => e.GetProperty("a").GetDouble())
                    .Where(kv => Near0(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
                var gotWalls = core.Walls.Where(kv => Near0(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
                Assert.True(wantWalls.Count == gotWalls.Count, $"{at}: {gotWalls.Count} walls sliding, the prototype {wantWalls.Count}; only here: {string.Join(" ", gotWalls.Keys.Except(wantWalls.Keys).Take(5))}; only there: {string.Join(" ", wantWalls.Keys.Except(gotWalls.Keys).Take(5))}");
                foreach (var kv in wantWalls)
                {
                    Assert.True(gotWalls.TryGetValue(kv.Key, out var a), $"{at}: wall {kv.Key} should be sliding");
                    Near(kv.Value, a, $"{at}: wall {kv.Key}");
                }
                f++;
            }
        }

        static void Near(double want, double got, string what) =>
            Assert.True(Math.Abs(want - got) <= 1e-6 * Math.Max(1, Math.Abs(want)), $"{what} = {got}, the prototype {want}");

        public static IEnumerable<object[]> Corpora() { yield return new object[] { "demo" }; yield return new object[] { "variants" }; }

        [Theory, MemberData(nameof(Corpora))]
        public void SegmentHitsMatch(string corpus)
        {
            var (doc, world) = PlayWorldTests.World(corpus);
            var core = new OcclusionCore(world, new OcclusionSettings());
            var bad = new List<string>(); int n = 0;
            foreach (var h in Root.GetProperty("corpora").GetProperty(corpus).GetProperty("hits").EnumerateArray())
            {
                var a = h.EnumerateArray().ToArray(); n++;
                var b = doc.buildings[a[0].GetInt32()];
                bool got = core.SegmentHits(b, (a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()), (a[4].GetDouble(), a[5].GetDouble(), a[6].GetDouble()));
                if (got != a[7].GetBoolean()) bad.Add($"{b.name}: {got}, the prototype {a[7].GetBoolean()}");
            }
            Assert.True(n > 0);
            Assert.True(bad.Count == 0, $"{bad.Count} of {n}:\n" + string.Join("\n", bad.Take(10)));
        }

        /// <summary>
        /// The segment test's box rejection (docs/PERFORMANCE.md §10.5) rejects no hit: over random segments round the walk-in
        /// test city's buildings, near and far, through every tier's height, it answers as the test without it does.
        /// </summary>
        [Fact]
        public void TheSegmentTestsBoxRejectionRejectsNoHit()
        {
            var d = new StoreyDocument(); Edit.TestCity.Generate(d, 200, walkIn: true);
            var site = new Generate.Site(d.buildings); var world = new PlayWorld(site);
            var core = new OcclusionCore(world, new OcclusionSettings());
            var rnd = new Random(11); int hits = 0, n = 0;
            foreach (var b in d.buildings)
            {
                var (x0, z0, x1, z1) = Generate.Site.BoundsOf(b); double top = Derived.RoofY(b) + 3;
                for (int i = 0; i < 60; i++)
                {
                    double R() => rnd.NextDouble();
                    // ends in a box twice the building's size round it, some of them just past its outline
                    var p = (x0 - (x1 - x0) / 2 + R() * 2 * (x1 - x0), b.elevation - 1 + R() * (top - b.elevation + 1), z0 - (z1 - z0) / 2 + R() * 2 * (z1 - z0));
                    var q = (x0 - (x1 - x0) / 2 + R() * 2 * (x1 - x0), b.elevation - 1 + R() * (top - b.elevation + 1), z0 - (z1 - z0) / 2 + R() * 2 * (z1 - z0));
                    if (i % 3 == 0) q = (p.Item1 + (R() - 0.5) * 0.8, p.Item2, p.Item3 + (R() - 0.5) * 0.8);   // short ones, at the walls
                    bool want = Reference(world, b, p, q), got = core.SegmentHits(b, p, q);
                    Assert.True(want == got, $"{b.name}: {got}, without the box test {want}");
                    if (want) hits++; n++;
                }
            }
            Assert.True(hits > n / 10 && hits < n * 9 / 10, $"{hits} of {n} hit");
        }

        /// <summary>
        /// The occlusion core keeps so many buildings' shapes and occluder records, where they only grew (docs/PERFORMANCE.md
        /// §10.5): a walk round the 3,000-building test city with the caps at a handful answers frame by frame as the same
        /// walk without them, and keeps no more than the caps.
        /// </summary>
        [Fact]
        public void TheCoresCapsChangeNothingButMemory()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; Edit.TestCity.Generate(d, 3000);
            var world = new PlayWorld(new Generate.Site(d.buildings));
            var free = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Sink }) { shapesKept = int.MaxValue, recordsKept = int.MaxValue };
            var capped = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Sink }) { shapesKept = 24, recordsKept = 6 };
            // a point somewhere in the city every 40 frames, a low camera swinging round it: many buildings in the way
            double cx0 = d.buildings.Min(b => b.pos.x), cx1 = d.buildings.Max(b => b.pos.x), cz0 = d.buildings.Min(b => b.pos.z), cz1 = d.buildings.Max(b => b.pos.z);
            var rnd = new Random(5); var p = new PlayerState(); var cam = new FollowCamera();
            const double dt = 1.0 / 60; int inTheWay = 0, mostShapes = 0, mostRecords = 0;
            for (int f = 0; f < 2400; f++)
            {
                if (f % 40 == 0)
                {
                    p = new PlayerState(); p.Spawn(cx0 + rnd.NextDouble() * (cx1 - cx0), 0, cz0 + rnd.NextDouble() * (cz1 - cz0));
                    cam = new FollowCamera { yaw = rnd.NextDouble() * 6.28, pitch = 0.3, dist = 16 }; cam.Snap(p);
                }
                cam.yaw += 0.02;
                Walker.Step(world, p, new MoveInput(f % 300 < 150 ? 0.4 : -0.3, 1, true), cam.yaw, dt);
                cam.Follow(p, dt);
                var pos = cam.Position();
                free.Frame(p, pos, (cam.tx, cam.ty, cam.tz), dt); capped.Frame(p, pos, (cam.tx, cam.ty, cam.tz), dt);
                Assert.Equal(free.Active.Select(r => (r.b.id, r.slot, r.a, r.t, r.k)), capped.Active.Select(r => (r.b.id, r.slot, r.a, r.t, r.k)));
                Assert.Equal(free.Rows, capped.Rows);
                Assert.Equal((free.View.active?.id, free.View.floor, free.View.clipY), (capped.View.active?.id, capped.View.floor, capped.View.clipY));
                if (free.Active.Count > 0) inTheWay++;
                mostShapes = Math.Max(mostShapes, capped.Kept.shapes); mostRecords = Math.Max(mostRecords, capped.Kept.records);
            }
            Assert.True(inTheWay > 100, $"{inTheWay} frames with buildings in the way");
            Assert.True(free.Kept.shapes > 24 && free.Kept.records > 6, $"uncapped: {free.Kept}");
            Assert.True(mostShapes <= 24 && mostRecords <= 6 + OcclusionCore.Slots, $"capped kept at most {mostShapes} shapes and {mostRecords} records");
        }

        // the segment test as it was before its box rejection
        static bool Reference(PlayWorld world, BuildingData b, (double x, double y, double z) p, (double x, double y, double z) q)
        {
            int N = b.floors.Count; double extra = Generate.Roofs.IsPitched(b) ? Generate.Roofs.Rise(world.Site, b) : 1.1;
            foreach (var (k0, k1) in Generate.Party.Tiers(b))
            {
                double y0 = Derived.FloorBase(b, k0), y1 = Derived.FloorBase(b, k1) + (k1 == N ? extra : 0), dy = q.y - p.y, t0 = 0, t1 = 1;
                if (Math.Abs(dy) < 1e-9) { if (p.y < y0 || p.y > y1) continue; }
                else
                {
                    double a = (y0 - p.y) / dy, c = (y1 - p.y) / dy; if (a > c) { var x = a; a = c; c = x; }
                    t0 = Math.Max(0, a); t1 = Math.Min(1, c); if (t0 >= t1) continue;
                }
                var fp = Derived.OutlineAt(b, k0);
                var A = new Vec2(p.x + (q.x - p.x) * t0 - b.pos.x, p.z + (q.z - p.z) * t0 - b.pos.z);
                var B = new Vec2(p.x + (q.x - p.x) * t1 - b.pos.x, p.z + (q.z - p.z) * t1 - b.pos.z);
                bool Near(Vec2 v, Vec2 w) => Edit.Tiers.SegCross(A, B, v, w)
                    || Math.Min(Math.Min(Edit.Tiers.SegDist(v.x, v.z, A.x, A.z, B.x, B.z).d, Edit.Tiers.SegDist(w.x, w.z, A.x, A.z, B.x, B.z).d),
                                Math.Min(Edit.Tiers.SegDist(A.x, A.z, v.x, v.z, w.x, w.z).d, Edit.Tiers.SegDist(B.x, B.z, v.x, v.z, w.x, w.z).d)) < 0.05;
                double Dist(double x, double z) { double dd = 1e9; for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; dd = Math.Min(dd, Edit.Tiers.SegDist(x, z, a.x, a.z, c.x, c.z).d); } return dd; }
                bool edge = false;
                for (int i = 0; i < fp.Count && !edge; i++) edge = Near(fp[i], fp[(i + 1) % fp.Count]);
                if (Generate.Geo.Pip(fp, A.x, A.z) || Generate.Geo.Pip(fp, B.x, B.z) || edge || Dist(A.x, A.z) < Generate.Dim.T_EXT || Dist(B.x, B.z) < Generate.Dim.T_EXT) return true;
            }
            return false;
        }
    }
}
