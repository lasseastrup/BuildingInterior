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
            var settings = new OcclusionSettings { mode = ModeOf(o.GetProperty("outside").GetString()!), cutaway = o.GetProperty("cut").GetBoolean(), stub = o.GetProperty("cutH").GetDouble(), baseHeight = o.GetProperty("baseH").GetDouble(), holeRadius = o.GetProperty("holeR").GetDouble(), assist = o.GetProperty("assist").GetBoolean() };
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
                // the sliding walls
                var wantWalls = fr.GetProperty("walls").EnumerateArray().ToDictionary(e => (e.GetProperty("id").GetString()!, e.GetProperty("i").GetInt32()), e => e.GetProperty("a").GetDouble());
                Assert.True(wantWalls.Count == core.Walls.Count, $"{at}: {core.Walls.Count} walls sliding, the prototype {wantWalls.Count}; only here: {string.Join(" ", core.Walls.Keys.Except(wantWalls.Keys).Take(5))}; only there: {string.Join(" ", wantWalls.Keys.Except(core.Walls.Keys).Take(5))}");
                foreach (var kv in wantWalls)
                {
                    Assert.True(core.Walls.TryGetValue(kv.Key, out var a), $"{at}: wall {kv.Key} should be sliding");
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
    }
}
