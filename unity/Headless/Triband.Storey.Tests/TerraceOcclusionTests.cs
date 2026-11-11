using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Out on a terrace, what stands over it is a building in the way like any other (docs/PLAY.md §3): from the terrace's
    /// storey up it sinks, slices, opens a hole, fades or dissolves by the occlusion's mode, nothing of the building is clipped
    /// or cut away as indoors, and the storeys under the terrace, which the player stands on, and the terrace's own parapet
    /// stand as they are. Storey's own.
    /// </summary>
    public class TerraceOcclusionTests
    {
        const double Dt = 1.0 / 30;
        const int K = 3;

        static List<Vec2> Square(double x0, double z0, double x1, double z1) => new List<Vec2> { new Vec2(x0, z0), new Vec2(x1, z0), new Vec2(x1, z1), new Vec2(x0, z1) };

        /// <summary>
        /// A tower on a terrace, as the building it was asked with: a 20 × 20 m base of three storeys and, from storey 3 up, an
        /// 8 × 8 m setback in its middle, five storeys more, with the terrace all round it. A shell, or with
        /// <paramref name="walkIn"/> walk-in, with a door from the tower out onto the terrace's south side (local (10, 6)).
        /// </summary>
        static (PlayWorld world, BuildingData b) Tower(bool walkIn)
        {
            var d = new StoreyDocument();
            var b = Buildings.Make(d, "rect", new Vec2(40, 60), name: "Tower");
            b.footprint = Square(0, 0, 20, 20);
            b.floors = Enumerable.Range(0, 8).Select(_ => new FloorData()).ToList();
            b.floors[K].shape = Square(6, 6, 14, 14);
            b.interior = walkIn;
            b.entrances.Clear(); b.entrances.Add(new EntranceData { edge = 0, t = 0.5 });
            if (walkIn) b.entrances.Add(new EntranceData { edge = 0, t = 0.5, k = K });
            d.buildings.Add(b);
            Assert.True(Derived.HasTerrace(b, K));
            return (new PlayWorld(new Site(d.buildings)), b);
        }

        /// <summary>The player stood at (lx, lz) on the terrace, the camera 6 m up and (dx, dz) off, for <paramref name="frames"/> frames: the core after.</summary>
        static OcclusionCore Stand(PlayWorld world, BuildingData b, double lx, double lz, double dx, double dz, OcclusionSettings? settings = null, int frames = 45)
        {
            var core = new OcclusionCore(world, settings ?? new OcclusionSettings());
            double y = Derived.FloorBase(b, K), px = b.pos.x + lx, pz = b.pos.z + lz;
            var p = new PlayerState(); p.Spawn(px, y, pz);
            for (int i = 0; i < frames; i++) core.Frame(p, (px + dx, y + 6, pz + dz), (px, y + FollowCamera.Chest, pz), Dt);
            return core;
        }

        static Occluder? Way(OcclusionCore core, BuildingData b) => core.Active.FirstOrDefault(r => r.b == b);

        /// <summary>
        /// Every storey of a Sink row down: gone, or squashed flat (the last to go, the player's storey, ends a rounding short of
        /// gone: its scale a ten-millionth).
        /// </summary>
        static bool Sunk(OcclusionCore core, Occluder way)
        {
            int o = way.slot * OcclusionCore.RowTexels * 4, n = (int)core.Rows[o];
            if (n == 0) return false;
            for (int i = 0; i < n; i++) { int q = o + (1 + i) * 4; if (!(core.Rows[q + 2] == 1 || core.Rows[q + 1] < 1e-3)) return false; }
            return true;
        }

        /// <summary>The middle of a wall's line in b's own plan (its cutaway data: a start, and a normal 1 + its length long).</summary>
        static Vec2 Middle(BuildingData b, double[] W)
        {
            double nl = Math.Sqrt(W[2] * W[2] + W[3] * W[3]), L = nl - 1, nx = W[2] / nl, nz = W[3] / nl;
            return new Vec2(W[0] + nz * L / 2 - b.pos.x, W[1] - nx * L / 2 - b.pos.z);
        }

        static double ToEdges(List<Vec2> fp, Vec2 q)
        {
            double d = 1e9;
            for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; d = Math.Min(d, Geo.SegDist(q.x, q.z, a.x, a.z, c.x, c.z).d); }
            return d;
        }

        /// <summary>
        /// The view it was asked with: a shell with a tower in the middle of its terrace, the player behind the tower. The
        /// tower goes as a building in the way does, from the terrace's storey up; nothing of it is clipped, no wall of it is
        /// cut away (it has no inside to show), and the base under the terrace stands. With the camera clear of the tower the
        /// building is whole.
        /// </summary>
        [Fact]
        public void BehindTheTowerOnlyTheTowerIsInTheWay()
        {
            var (world, b) = Tower(walkIn: false); double y = Derived.FloorBase(b, K);
            // on the terrace south of the tower, the camera north of it, looking down through it at the player
            var core = Stand(world, b, 10, 3, 0, 19);
            var v = core.View;
            Assert.Same(b, v.active); Assert.Equal(K, v.floor);   // still on its storey 3, for the HUD
            Assert.Equal(1e9, v.clipY); Assert.False(v.cutOn);
            Assert.Equal(y, v.terraceY);
            Assert.DoesNotContain(core.Walls, kv => kv.Key.id == b.id && kv.Value > 0);
            var way = Way(core, b);
            Assert.NotNull(way); Assert.True(way!.slot >= 0); Assert.Equal(K, way.k);
            // Sink, from the terrace's floor up to the roof, every storey gone
            int o = way.slot * OcclusionCore.RowTexels * 4, n = (int)core.Rows[o];
            Assert.Equal(b.floors.Count - K + 1, n);
            Assert.Equal(y, core.Rows[o + 4], 4);
            Assert.True(Sunk(core, way), "the tower isn't down");
            // a prop drawn whole in the tower's storeys is hidden, and one on the deck with them (the shaders draw a prop there
            // as what stands over the terrace); one in the base's storeys is not
            Assert.True(core.HidesPoint(b, y + 5)); Assert.True(core.HidesPoint(b, y));
            Assert.False(core.HidesPoint(b, y - 2)); Assert.False(core.HidesPoint(b, y - 0.01));
            // the camera south of the player, over the street: nothing in the way, the building whole
            var clear = Stand(world, b, 10, 3, 0, -13);
            Assert.Same(b, clear.View.active); Assert.Equal(1e9, clear.View.clipY); Assert.False(clear.View.cutOn);
            Assert.Equal(1e9, clear.View.terraceY); Assert.Null(Way(clear, b)); Assert.Empty(clear.TerraceWalls);
            Assert.False(clear.HidesPoint(b, y + 5));
        }

        /// <summary>
        /// The terrace's own walls stand while the tower goes: its parapet runs round the base's outline, off the tower's, and
        /// they are all of them; every wall the tower stands on its outline with is the tower's.
        /// </summary>
        [Fact]
        public void TheTerracesParapetStands()
        {
            foreach (bool walkIn in new[] { false, true })
            {
                var (world, b) = Tower(walkIn); double y = Derived.FloorBase(b, K);
                var core = Stand(world, b, 10, 3, 0, 19);
                var shape = world.ShapeOf(b); var kept = core.TerraceWalls.ToHashSet();
                var tower = Derived.OutlineAt(b, K); var below = Derived.OutlineAt(b, K - 1);
                int runs = new Lod0.Shared(world.Site, b).TerraceRuns(K).Count;
                Assert.Equal(4, runs);   // the tower in the middle: the terrace's edge all round
                Assert.Equal(runs, kept.Count);
                foreach (int i in kept)
                {
                    Assert.Equal(y, shape.WallBase[i]);
                    var m = Middle(b, shape.Walls[i].W);
                    Assert.True(ToEdges(below, m) < 0.05, $"wall {i}'s middle ({m.x:F2}, {m.z:F2}) isn't on the terrace's edge");
                }
                // the tower's own walls on the terrace's storey, on its outline or in it, all go
                int own = 0;
                for (int i = 0; i < shape.Walls.Count; i++)
                {
                    if (shape.WallBase[i] != y) continue;
                    var m = Middle(b, shape.Walls[i].W);
                    if (Geo.Pip(tower, m.x, m.z) || ToEdges(tower, m) < 0.05) { own++; Assert.DoesNotContain(i, kept); }
                }
                Assert.True(own >= 4, $"{(walkIn ? "walk-in" : "shell")}: only {own} of the tower's walls on its first storey");
            }
        }

        /// <summary>Every mode takes the tower from the terrace's floor up, and leaves the storeys under it alone.</summary>
        [Fact]
        public void EveryModeTakesTheTowerFromTheTerraceUp()
        {
            var (world, b) = Tower(walkIn: false); double y = Derived.FloorBase(b, K);
            foreach (var mode in new[] { OccluderMode.Sink, OccluderMode.Slice, OccluderMode.Cutout, OccluderMode.Fade, OccluderMode.Dissolve })
            {
                var s = new OcclusionSettings { mode = mode };
                var core = Stand(world, b, 10, 3, 0, 19, s);
                var way = Way(core, b);
                Assert.True(way != null && way.k == K, $"{mode}: the tower isn't in the way from storey {K}");
                Assert.Equal(y, core.View.terraceY); Assert.Equal(1e9, core.View.clipY);
                int o = way!.slot * OcclusionCore.RowTexels * 4;
                switch (mode)
                {
                    case OccluderMode.Sink: Assert.Equal(y, core.Rows[o + 4], 4); break;
                    case OccluderMode.Slice: Assert.Equal(Math.Max(y + Derived.FloorH(b, K) - Dim.SLAB - 0.01, y + 2), core.Rows[o + 2], 4); break;
                    case OccluderMode.Dissolve: Assert.Equal(1f, core.Rows[o + 1]); Assert.Equal(y, core.Rows[o + 3], 4); break;
                    default: Assert.Equal(1f, core.Rows[o + 1]); Assert.Equal(y + s.baseHeight, core.Rows[o + 3], 4); break;
                }
                Assert.False(core.HidesPoint(b, y - 2), $"{mode}: the storey under the terrace is hidden");
            }
        }

        /// <summary>With the buildings in the way left alone (mode Off), nothing goes, and the view is as indoors, as it was.</summary>
        [Fact]
        public void WithTheBuildingsInTheWayLeftAloneTheViewIsAsIndoors()
        {
            var (world, b) = Tower(walkIn: false); double y = Derived.FloorBase(b, K);
            var core = Stand(world, b, 10, 3, 0, 19, new OcclusionSettings { mode = OccluderMode.Off });
            Assert.Null(Way(core, b)); Assert.Equal(1e9, core.View.terraceY); Assert.Empty(core.TerraceWalls);
            Assert.Equal(y + Derived.FloorH(b, K) - Dim.SLAB - 0.01, core.View.clipY, 9); Assert.True(core.View.cutOn);
        }

        /// <summary>
        /// A walk-in tower's door onto the terrace. Walking out of it with the camera trailing over the tower, the view is as
        /// indoors in the tower and at its door (never in the way there, as no building is at its door); past the door the
        /// tower is in the way, gone from the first frame, as the view had the storeys over the player clipped. Backing up to
        /// the door with the tower still in the way, the view is as indoors again at once, the tower out of the way.
        /// </summary>
        [Fact]
        public void OutOfTheTowersDoorItIsGoneAtOnce()
        {
            var (world, b) = Tower(walkIn: true); double y = Derived.FloorBase(b, K);
            var core = new OcclusionCore(world, new OcclusionSettings());
            var p = new PlayerState();
            int inside = 0, atDoor = 0, outside = 0; bool first = true;
            void At(double lz)
            {
                double px = b.pos.x + 10, pz = b.pos.z + lz;
                p.Spawn(px, y, pz);
                core.Frame(p, (px, y + 5, b.pos.z + 15.5), (px, y + FollowCamera.Chest, pz), Dt);   // over the tower's north side
                var way = Way(core, b); var v = core.View;
                Assert.Same(b, v.active);
                // gone from the first frame it is in the way: no storey of it standing whole a moment
                if (way != null && first) { Assert.True(Sunk(core, way), $"z {lz:F2}: the tower stands, its first frame in the way"); first = false; }
                if (lz > 6.4 || (lz < 5.9 && lz > 4.9))
                {
                    // in the tower, or at its door: as indoors, the tower not in the way
                    Assert.True(v.clipY < 1e8 && v.cutOn && v.terraceY > 1e8, $"z {lz:F2}: not as indoors");
                    Assert.Null(way);
                    if (lz > 6.4) inside++; else atDoor++;
                }
                else if (lz < 4.7)
                {
                    Assert.True(v.clipY > 1e8 && !v.cutOn && v.terraceY == y, $"z {lz:F2}: the view isn't the terrace's");
                    Assert.NotNull(way);
                    outside++;
                }
            }
            for (double lz = 9; lz >= 2; lz -= 0.05) At(lz);     // out, south through the door, to the terrace's edge
            for (double lz = 2; lz <= 5.5; lz += 0.05) At(lz);   // and back to the door, facing out
            Assert.True(inside > 10 && atDoor > 20 && outside > 60, $"{inside} frames inside, {atDoor} at the door, {outside} out");
        }

        /// <summary>The camera going round the player on the terrace, the tower coming into the way and out of it: no garbage a frame.</summary>
        [Fact]
        public void GoingRoundOnTheTerraceMakesNoGarbage()
        {
            var (world, b) = Tower(walkIn: false); double y = Derived.FloorBase(b, K), px = b.pos.x + 10, pz = b.pos.z + 3;
            var core = new OcclusionCore(world, new OcclusionSettings());
            var p = new PlayerState(); p.Spawn(px, y, pz);
            long bytes = 0; int frames = 0, inTheWay = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int f = 0; f < 720; f++)
                {
                    double yaw = f * Math.PI * 2 / 360;   // twice round
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    core.Frame(p, (px + Math.Sin(yaw) * 9, y + 6, pz + Math.Cos(yaw) * 9), (px, y + FollowCamera.Chest, pz), 1.0 / 60);
                    long a1 = GC.GetAllocatedBytesForCurrentThread();
                    if (pass == 0) continue;   // the first time round meets the shapes and the terrace's walls
                    bytes += a1 - a0; frames++;
                    if (core.View.terraceY < 1e8) inTheWay++;
                }
            Assert.True(inTheWay > 60 && inTheWay < frames - 60, $"the tower in the way {inTheWay} of {frames} frames: the camera doesn't go round it");
            Assert.True(bytes / frames < 64, $"{bytes / frames} bytes a frame on the terrace");
        }

        static string Shaders => Path.Combine(typeof(TerraceOcclusionTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == "UnityRoot").Value!, "Packages", "com.triband.storey", "Unity", "Shaders");

        /// <summary>
        /// The shaders' every exception of the active building from the buildings in the way (StoreySink, StoreyVertex,
        /// StoreyOcclude, StoreyPropGone) knows the terrace: a function that reads the active building's index also asks
        /// whether the point stands over the terrace the player is out on, or is one of the terrace's own walls.
        /// </summary>
        [Fact]
        public void EveryShaderExceptionOfThePlayersBuildingKnowsTheTerrace()
        {
            int seen = 0;
            foreach (var name in new[] { "StoreyOcclusion.hlsl", "StoreyProp.hlsl" })
            {
                var src = Regex.Replace(File.ReadAllText(Path.Combine(Shaders, name)), @"//[^\n]*", m => new string(' ', m.Length));
                foreach (var (signature, body) in Functions(src))
                {
                    if (!body.Contains("_StoreyActive.x")) continue;
                    seen++;
                    Assert.True(Regex.IsMatch(body, @"\bStorey(OverTerrace|TerraceWall)\s*\("), $"{name}: {signature.Trim()} tells the active building apart but not what stands over its terrace");
                }
            }
            Assert.True(seen >= 4, $"only {seen} functions read the active building: has the shaders' code moved?");
        }

        /// <summary>A shader's top-level bodies, each with what comes before it (its signature, or a struct's name).</summary>
        static IEnumerable<(string signature, string body)> Functions(string src)
        {
            int depth = 0, from = 0, open = -1;
            for (int i = 0; i < src.Length; i++)
            {
                if (src[i] == '{') { if (depth++ == 0) open = i; }
                else if (src[i] == '}' && --depth == 0)
                {
                    yield return (src.Substring(from, open - from), src.Substring(open + 1, i - open - 1));
                    from = i + 1;
                }
                else if (src[i] == ';' && depth == 0) from = i + 1;
            }
        }
    }
}
