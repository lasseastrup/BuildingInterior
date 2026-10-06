using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics.Tracing;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Garbage per frame in Play mode, for the engine-free parts that run every frame: walking (the walk model), the
    /// occlusion core and the automatic LOD's pick. A walk down the 3,000-building test city's streets, measured after a
    /// warm-up; what is built once (a building's LOD0, its outlines) is not per-frame garbage and is warmed first.
    /// </summary>
    public class FrameAllocTests
    {
        readonly ITestOutputHelper output;
        public FrameAllocTests(ITestOutputHelper o) { output = o; }
        const double Dt = 1.0 / 60;

        sealed class Ticks : EventListener
        {
            public readonly Dictionary<string, long> types = new Dictionary<string, long>();
            public bool on;
            protected override void OnEventSourceCreated(EventSource s) { if (s.Name == "Microsoft-Windows-DotNETRuntime") EnableEvents(s, EventLevel.Verbose, (EventKeywords)1); }
            protected override void OnEventWritten(EventWrittenEventArgs e)
            {
                if (!on || e.EventName == null || !e.EventName.StartsWith("GCAllocationTick")) return;
                int i = e.PayloadNames!.IndexOf("TypeName"); string t = i >= 0 ? (string)e.Payload![i]! : "?";
                lock (types) types[t] = types.TryGetValue(t, out var n) ? n + 1 : 1;
            }
        }
        static Ticks? ticks;

        [Fact]
        public void AFrameMakesNoGarbage()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 3000);
            var world = new PlayWorld(new Site(d.buildings));
            var core = new OcclusionCore(world, new OcclusionSettings { mode = OccluderMode.Sink });
            var lods = new LodManager();
            int idx = 0; var forced = new HashSet<int>();
            foreach (var b in world.Site.Buildings)
            {
                var fp = b.footprint; double x0 = fp.Min(q => q.x) + b.pos.x, x1 = fp.Max(q => q.x) + b.pos.x, z0 = fp.Min(q => q.z) + b.pos.z, z1 = fp.Max(q => q.z) + b.pos.z;
                lods.Set(idx++, x0, z0, x1, z1, Derived.RoofY(b) + 1);
            }
            // the same walk twice: the first meets every building near the route for the first time (its LOD0, its
            // shape for the segment test: built once, not per frame), the second is measured
            var p = new PlayerState(); var cam = new FollowCamera();
            long walk = 0, occ = 0, lod = 0; int frames = 0, inTheWay = 0, inside = 0;
            for (int f = 0; f < 2400; f++)
            {
                if (f % 1200 == 0) { p = new PlayerState(); p.Spawn(d.spawn.x, 0, d.spawn.z); cam = new FollowCamera { yaw = 0.6, pitch = 0.5, dist = 9 }; cam.Snap(p); }
                // turning as it goes: the walk follows the streets round, the camera sweeps the buildings behind
                cam.yaw += 0.004;
                double yaw0 = cam.yaw;
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                Walker.Step(world, p, new MoveInput(f % 300 < 150 ? 0.4 : -0.3, 1, true), yaw0, Dt);
                cam.Follow(p, Dt);
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                var pos = cam.Position();
                core.Frame(p, pos, (cam.tx, cam.ty, cam.tz), Dt);
                long a2 = GC.GetAllocatedBytesForCurrentThread();
                forced.Clear(); foreach (var r in core.Active) forced.Add(world.Site.IndexOf(r.b));
                var fr = lods.Update(pos.x, pos.y, pos.z, 1200, Dt, forced);
                // the renderer builds a few of what is asked each frame
                int built = 0; foreach (var (bi, l, _) in fr.Build) { if (built++ >= 4) break; lods.SetBuilt(bi, l, true); }
                long a3 = GC.GetAllocatedBytesForCurrentThread();
                if (f == 1200 && Environment.GetEnvironmentVariable("STOREY_TICKS") != null) { ticks = new Ticks(); ticks.on = true; }
                if (f < 1200) continue;   // warm-up: everything near the route has met its first use
                walk += a1 - a0; occ += a2 - a1; lod += a3 - a2; frames++;
                if (core.Active.Count > 0) inTheWay++;
                if (core.View.active != null) inside++;
            }
            if (ticks != null) { ticks.on = false; foreach (var kv in ticks.types.OrderByDescending(k => k.Value).Take(15)) output.WriteLine($"tick {kv.Value} {kv.Key}"); }
            output.WriteLine($"bytes a frame: walking {walk / frames:N0}, occlusion {occ / frames:N0}, LOD {lod / frames:N0}; {inTheWay} of {frames} frames with buildings in the way, {inside} inside one");
            Assert.True(inTheWay > 50, $"only {inTheWay} frames had a building in the way: the walk doesn't test the occluders");
            // nothing per frame: a few bytes of slack for a list growing to a new high once
            Assert.True(walk / frames < 64 && occ / frames < 64 && lod / frames < 64, $"garbage a frame: walking {walk / frames}, occlusion {occ / frames}, LOD {lod / frames} bytes");
        }

        [Fact]
        public void WalkingInAndUpTheStairsMakesNoGarbage()
        {
            // the prototype's walks (in at the street door, round under each mode, up the stairs), each twice on one core:
            // the second time is measured
            var root = PlayWorldTests.Play.Value.RootElement; long bytes = 0; int frames = 0, inside = 0;
            var (doc, _) = PlayWorldTests.World("demo");
            var world = new PlayWorld(new Site(doc.buildings));
            foreach (var w in root.GetProperty("walks").EnumerateArray())
            {
                var mode = w.GetProperty("occ").GetProperty("outside").GetString() switch { "sink" => OccluderMode.Sink, "slice" => OccluderMode.Slice, "cutout" => OccluderMode.Cutout, "fade" => OccluderMode.Fade, _ => OccluderMode.Off };
                var core = new OcclusionCore(world, new OcclusionSettings { mode = mode });
                var start = w.GetProperty("start").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var c = w.GetProperty("cam").EnumerateArray().Select(e => e.GetDouble()).ToArray();
                var inputs = w.GetProperty("frames").EnumerateArray().Select(fr =>
                {
                    var input = fr.GetProperty("input");
                    bool K(string k) => input.TryGetProperty(k, out var v) && v.GetBoolean();
                    double? yaw = input.TryGetProperty("yaw", out var yv) ? yv.GetDouble() : (double?)null;
                    return (move: new MoveInput((K("KeyD") ? 1 : 0) - (K("KeyA") ? 1 : 0), (K("KeyW") ? 1 : 0) - (K("KeyS") ? 1 : 0), K("ShiftLeft")), yaw);
                }).ToArray();
                for (int pass = 0; pass < 2; pass++)
                {
                    core.Clear();
                    var p = new PlayerState(); p.Spawn(start[0], start[1], start[2]);
                    var cam = new FollowCamera { yaw = c[0], pitch = c[1], dist = c[2] }; cam.Snap(p);
                    foreach (var (move, yaw) in inputs)
                    {
                        if (yaw is double y) cam.yaw = y;
                        long a0 = GC.GetAllocatedBytesForCurrentThread();
                        Walker.Step(world, p, move, cam.yaw, 1.0 / 30);
                        cam.Follow(p, 1.0 / 30);
                        core.Frame(p, cam.Position(), (cam.tx, cam.ty, cam.tz), 1.0 / 30);
                        long a1 = GC.GetAllocatedBytesForCurrentThread();
                        if (pass == 1) { bytes += a1 - a0; frames++; if (core.View.active != null) inside++; }
                    }
                }
            }
            output.WriteLine($"walks: {bytes / Math.Max(frames, 1):N0} bytes a frame over {frames} frames, {inside} inside a building");
            Assert.True(inside > frames / 3, $"only {inside} of {frames} frames inside a building: the walks don't test the cutaway");
            Assert.True(bytes / frames < 64, $"{bytes / frames} bytes a frame walking in and up the stairs");
        }
    }
}
