using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Xunit;
using Xunit.Abstractions;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// The LOD manager passes over squares of settled buildings (<see cref="LodManager"/>): it must pick exactly what
    /// looking at every building picks. Checked frame by frame against the manager as it was before, building by building,
    /// over a flight through the test city with zooming, forced buildings, builds, moves and removals. Storey's own.
    /// </summary>
    public class LodSquaresTests
    {
        readonly ITestOutputHelper output;
        public LodSquaresTests(ITestOutputHelper o) { output = o; }

        /// <summary>The manager before the squares: every building every frame (the order of ties fixed as now).</summary>
        sealed class Reference
        {
            public sealed class E { public double x0, z0, x1, z1, y1; public int Want = 3, Shown = 3, From = 3; public double T = 1; public bool Has0, Has1; public long Used0, Used1; public bool Visible(int l) => Shown == l || (T < 1 && From == l); }
            public readonly SortedDictionary<int, E> entries = new SortedDictionary<int, E>();
            public readonly LodSettings S;
            long frame;
            public Reference(LodSettings s) { S = s; }
            public void Set(int idx, double x0, double z0, double x1, double z1, double y1)
            {
                if (!entries.TryGetValue(idx, out var e)) entries[idx] = e = new E();
                e.x0 = x0; e.z0 = z0; e.x1 = x1; e.z1 = z1; e.y1 = y1; e.Has0 = e.Has1 = false;
            }
            public void SetBuilt(int idx, int lod, bool b) { if (!entries.TryGetValue(idx, out var e)) return; if (lod == 0) e.Has0 = b; else e.Has1 = b; }
            public (List<(int, int, double)> build, List<(int, int)> drop, List<int> changed, int[] count) Update(double cx, double cy, double cz, double ppm, double dt, ICollection<int> forced)
            {
                frame++; var build = new List<(int idx, int lod, double px)>(); var fb = new List<(int, int, double)>(); var drop = new List<(int, int)>(); var changed = new List<int>(); var count = new int[4];
                double pxm = ppm * S.Bias, H = S.Hysteresis;
                foreach (var kv in entries)
                {
                    var e = kv.Value; int idx = kv.Key;
                    double dx = Math.Max(Math.Max(e.x0 - cx, 0), cx - e.x1), dy = Math.Max(Math.Max(-cy, 0), cy - e.y1), dz = Math.Max(Math.Max(e.z0 - cz, 0), cz - e.z1);
                    double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy + dz * dz)), rel = pxm / d;
                    int want = 3;
                    for (int l = 0; l < 3; l++) if (rel >= S.PX[l] * (l < e.Want ? 1 + H : 1 - H)) { want = l; break; }
                    if (d > S.Far) want = 3;
                    bool force = forced.Contains(idx); if (force) want = 0;
                    e.Want = want; int show = want;
                    if (show == 0 && !e.Has0) { (force ? fb : build).Add((idx, 0, force ? double.MaxValue : rel)); show = e.Has1 ? 1 : 2; }
                    if (show == 1 && !e.Has1) { build.Add((idx, 1, rel)); show = 2; }
                    bool ch = false;
                    if (show != e.Shown) { e.From = e.Shown; e.Shown = show; e.T = 0; ch = true; }
                    if (e.T < 1) { e.T = Math.Min(1, e.T + dt / S.Fade); ch = true; }
                    if (ch) changed.Add(idx);
                    if (e.Visible(0)) e.Used0 = frame;
                    if (e.Visible(1)) e.Used1 = frame;
                    count[e.Shown]++;
                }
                build.Sort((a, b) => { int c = b.px.CompareTo(a.px); if (c != 0) return c; c = a.idx.CompareTo(b.idx); return c != 0 ? c : a.lod.CompareTo(b.lod); });
                build.InsertRange(0, fb);
                foreach (int lod in new[] { 0, 1 })
                {
                    int max = lod == 0 ? S.MaxLod0 : S.MaxLod1, n = 0; var idle = new List<(int idx, long used)>();
                    foreach (var kv in entries) { var e = kv.Value; if (!(lod == 0 ? e.Has0 : e.Has1)) continue; n++; if (!e.Visible(lod)) idle.Add((kv.Key, lod == 0 ? e.Used0 : e.Used1)); }
                    if (n <= max) continue;
                    idle.Sort((a, b) => { int c = a.used.CompareTo(b.used); return c != 0 ? c : a.idx.CompareTo(b.idx); });
                    foreach (var (idx, _) in idle) { if (n <= max) break; SetBuilt(idx, lod, false); drop.Add((idx, lod)); n--; }
                }
                return (build, drop, changed, count);
            }
        }

        [Fact]
        public void PassingOverSettledSquaresPicksWhatLookingAtEveryBuildingPicks()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 3000);
            var boxes = d.buildings.Select(b => { var bb = Site.BoundsOf(b); return (bb.x0, bb.z0, bb.x1, bb.z1, y1: Derived.RoofY(b) + 1); }).ToList();
            var settings = new LodSettings { MaxLod0 = 12, MaxLod1 = 150 };
            var m = new LodManager(settings); var r = new Reference(settings);
            for (int i = 0; i < boxes.Count; i++) { var x = boxes[i]; m.Set(i, x.x0, x.z0, x.x1, x.z1, x.y1); r.Set(i, x.x0, x.z0, x.x1, x.z1, x.y1); }
            var rng = new Random(20261012); var forced = new HashSet<int>();
            int frames = 6000;
            for (int f = 0; f < frames; f++)
            {
                double t = f / (double)frames;
                // a flight round the city at changing heights, then a walk at street level, zooming in and out
                double ang = t * Math.PI * 4, rad = 900 * (1 - t) + 20;
                double cx = Math.Cos(ang) * rad, cz = Math.Sin(ang) * rad, cy = f < frames * 0.7 ? 40 + 300 * Math.Abs(Math.Sin(ang * 1.7)) : 1.7;
                double ppm = 1200 * (1 + 0.6 * Math.Sin(f * 0.01));
                if (f % 50 == 0) { forced.Clear(); if (rng.NextDouble() < 0.5) forced.Add(rng.Next(boxes.Count)); }
                if (f % 97 == 0)
                {
                    // an edit: a building moved (re-set, its detail unbuilt) or taken away
                    int i = rng.Next(boxes.Count); var x = boxes[i]; double sh = rng.NextDouble() * 300 - 150;
                    if (rng.NextDouble() < 0.8) { m.Set(i, x.x0 + sh, x.z0, x.x1 + sh, x.z1, x.y1); r.Set(i, x.x0 + sh, x.z0, x.x1 + sh, x.z1, x.y1); }
                    else if (r.entries.ContainsKey(i)) { m.Remove(i); r.entries.Remove(i); }
                }
                var a = m.Update(cx, cy, cz, ppm, 1.0 / 30, forced);
                var b = r.Update(cx, cy, cz, ppm, 1.0 / 30, forced);
                Assert.True(a.Build.SequenceEqual(b.build), $"frame {f}: the build lists differ");
                Assert.True(a.Drop.SequenceEqual(b.drop), $"frame {f}: the drop lists differ");
                Assert.True(a.Changed.OrderBy(i => i).SequenceEqual(b.changed.OrderBy(i => i)), $"frame {f}: the changed buildings differ");
                Assert.True(a.Count.SequenceEqual(b.count), $"frame {f}: the counts differ");
                // the renderer builds a few of what is asked each frame
                int built = 0; foreach (var (bi, l, _) in a.Build) { if (built++ >= 3) break; m.SetBuilt(bi, l, true); r.SetBuilt(bi, l, true); }
                if (f % 500 == 499)
                    foreach (var kv in r.entries)
                    {
                        var e = m.Get(kv.Key)!; var x = kv.Value;
                        Assert.True((e.Want, e.Shown, e.From, e.T, e.Has0, e.Has1, e.Used0, e.Used1) == (x.Want, x.Shown, x.From, x.T, x.Has0, x.Has1, x.Used0, x.Used1), $"frame {f}: building {kv.Key} differs");
                    }
            }
            output.WriteLine($"{frames} frames over {boxes.Count} buildings: the same picks");
        }

        [Fact]
        public void MostOfTheCityIsPassedOver()
        {
            var d = PrototypeJson.Read(Fixtures.Text("demo.json")).Document; TestCity.Generate(d, 3000);
            var m = new LodManager(); int i = 0;
            foreach (var b in d.buildings) { var bb = Site.BoundsOf(b); m.Set(i++, bb.x0, bb.z0, bb.x1, bb.z1, Derived.RoofY(b) + 1); }
            for (int f = 0; f < 120; f++) m.Update(d.spawn.x, 1.7, d.spawn.z, 1200, 1.0 / 30);   // settled
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int f = 0; f < 600; f++) m.Update(d.spawn.x + f * 0.05, 1.7, d.spawn.z, 1200, 1.0 / 30);
            double us = sw.Elapsed.TotalMilliseconds / 600 * 1000;
            output.WriteLine($"3,007 buildings, a camera walking the street: {us:0} µs a frame");
        }
    }
}
