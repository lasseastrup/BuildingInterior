#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// The LOD manager's numbers (docs/CITY.md §2; SPEC §6.2, the prototype's LODCFG). Feature scale, not screen size:
    /// LOD0's frames and furniture only matter up close, however big the building.
    /// </summary>
    public sealed class LodSettings
    {
        /// <summary>Pixels one metre must cover, at a building's nearest point, for LOD0, LOD1 and LOD2.</summary>
        public double[] PX = { 16, 4, 0.3 };
        /// <summary>Hysteresis: a building moving to a finer LOD needs this much more, to a coarser this much less.</summary>
        public double Hysteresis = 0.12;
        /// <summary>The dithered cross-fade between two LODs, in seconds.</summary>
        public double Fade = 0.35;
        /// <summary>Multiplies the pixels per metre: above 1 brings detail closer in.</summary>
        public double Bias = 1;
        /// <summary>Beyond this many metres a building is not drawn.</summary>
        public double Far = 1500;
        /// <summary>How many LOD0 and LOD1 meshes stay resident in all (shown or not); the least recently shown go first.</summary>
        public int MaxLod0 = 16, MaxLod1 = 260;
        /// <summary>
        /// Milliseconds a frame may spend building LOD0 and LOD1 meshes (at least one is built when any is wanted). With
        /// worker threads, only the upload counts: the meshes are generated off the main thread.
        /// </summary>
        public double BudgetMs = 6;
        /// <summary>Worker threads generating detail at once: 0 builds on the main thread, -1 one fewer than the cores (at least one).</summary>
        public int Threads = -1;
    }

    /// <summary>
    /// Picks each building's LOD every frame, as the prototype's LOD system does (docs/CITY.md §2): LOD0 full detail,
    /// LOD1 the shell, LOD2 the massing (always resident, merged into cells), 3 not drawn. A LOD the building doesn't have
    /// yet is asked for (largest on screen first) and the best one it has is shown meanwhile; a change cross-fades.
    /// LOD0 and LOD1 meshes past the resident caps are dropped, least recently shown first. Engine-free: the renderer
    /// builds and drops the meshes and writes the state into the building table.
    /// </summary>
    public sealed class LodManager
    {
        /// <summary>One building: its bounds (site space, metres; the ground is y 0), and its LOD state.</summary>
        public sealed class Entry
        {
            public double x0, z0, x1, z1, y1;
            /// <summary>The LOD wanted by distance, the one shown, the one fading out, and how far the fade is (1 done).</summary>
            public int Want = 3, Shown = 3, From = 3;
            public double T = 1;
            /// <summary>Whether its LOD0 and LOD1 meshes are built (and still match its data).</summary>
            public bool Has0, Has1;
            /// <summary>The frame each was last drawn, for eviction.</summary>
            public long Used0, Used1;
            public bool Visible(int lod) => Shown == lod || (T < 1 && From == lod);
        }

        /// <summary>What a frame asks of the renderer. The manager's own, reused: valid until the next <see cref="Update"/>.</summary>
        public sealed class Frame
        {
            /// <summary>LODs to build, most pixels first: (building, LOD, pixels per metre).</summary>
            public readonly List<(int idx, int lod, double px)> Build = new List<(int, int, double)>();
            /// <summary>LOD meshes to drop (the building keeps the others).</summary>
            public readonly List<(int idx, int lod)> Drop = new List<(int, int)>();
            /// <summary>Buildings whose shown LOD, fading LOD or fade changed (their table rows need writing).</summary>
            public readonly List<int> Changed = new List<int>();
            /// <summary>How many buildings show each LOD (index 3: not drawn).</summary>
            public readonly int[] Count = new int[4];

            internal void Clear() { Build.Clear(); Drop.Clear(); Changed.Clear(); Array.Clear(Count, 0, Count.Length); }
        }

        // reused every frame, so a frame makes no garbage (FrameAllocTests)
        readonly Frame frameOut = new Frame();
        readonly List<(int, int, double)> forcedBuild = new List<(int, int, double)>();
        readonly List<(int idx, long used)> idle = new List<(int, long)>();
        sealed class MostPixels : IComparer<(int idx, int lod, double px)> { public int Compare((int idx, int lod, double px) a, (int idx, int lod, double px) b) => b.px.CompareTo(a.px); }
        sealed class LeastRecent : IComparer<(int idx, long used)> { public int Compare((int idx, long used) a, (int idx, long used) b) => a.used.CompareTo(b.used); }
        static readonly MostPixels mostPixels = new MostPixels();
        static readonly LeastRecent leastRecent = new LeastRecent();

        public readonly LodSettings Settings;
        readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        long frame;

        public LodManager(LodSettings? s = null) { Settings = s ?? new LodSettings(); }

        public IReadOnlyDictionary<int, Entry> Entries => entries;

        /// <summary>How many buildings have their LOD0 and their LOD1 built (no garbage: <see cref="Entries"/> boxes its enumerator).</summary>
        public (int lod0, int lod1) Resident()
        {
            int r0 = 0, r1 = 0;
            foreach (var e in entries.Values) { if (e.Has0) r0++; if (e.Has1) r1++; }
            return (r0, r1);
        }
        public Entry? Get(int idx) => entries.TryGetValue(idx, out var e) ? e : null;

        /// <summary>Add a building (or move it: an edit), by its table index. Its detail LODs are marked unbuilt.</summary>
        public Entry Set(int idx, double x0, double z0, double x1, double z1, double y1)
        {
            if (!entries.TryGetValue(idx, out var e)) entries[idx] = e = new Entry();
            e.x0 = x0; e.z0 = z0; e.x1 = x1; e.z1 = z1; e.y1 = y1; e.Has0 = e.Has1 = false;
            return e;
        }

        public void Remove(int idx) => entries.Remove(idx);
        public void Clear() => entries.Clear();

        /// <summary>A building's LOD0 or LOD1 mesh is built (or gone).</summary>
        public void SetBuilt(int idx, int lod, bool built)
        {
            if (!entries.TryGetValue(idx, out var e)) return;
            if (lod == 0) e.Has0 = built; else if (lod == 1) e.Has1 = built;
        }

        /// <summary>Pixels one metre covers at one metre away, for a camera's vertical field of view and screen height.</summary>
        public static double PixelsPerMetre(double screenHeight, double fovDegrees) => screenHeight / (2 * Math.Tan(fovDegrees * Math.PI / 360));

        /// <summary>
        /// One frame: each building's LOD from the camera (site space), the cross-fades moved on by <paramref name="dt"/>,
        /// the LODs to build and the meshes to drop. <paramref name="forced"/> buildings (being edited, walked in, sliced)
        /// always want LOD0; a forced one without it is in <see cref="Frame.Build"/> first.
        /// </summary>
        public Frame Update(double cx, double cy, double cz, double pixelsPerMetre, double dt, ICollection<int>? forced = null)
        {
            frame++;
            var f = frameOut; f.Clear(); var s = Settings;
            double pxm = pixelsPerMetre * s.Bias, H = s.Hysteresis;
            forcedBuild.Clear();
            foreach (var kv in entries)
            {
                var e = kv.Value; int idx = kv.Key;
                // the nearest point of the building's box
                double dx = Math.Max(Math.Max(e.x0 - cx, 0), cx - e.x1), dy = Math.Max(Math.Max(-cy, 0), cy - e.y1), dz = Math.Max(Math.Max(e.z0 - cz, 0), cz - e.z1);
                double d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy + dz * dz)), rel = pxm / d;
                int want = 3;
                for (int l = 0; l < 3; l++) if (rel >= s.PX[l] * (l < e.Want ? 1 + H : 1 - H)) { want = l; break; }
                if (d > s.Far) want = 3;
                bool force = forced != null && forced.Contains(idx);
                if (force) want = 0;
                e.Want = want;
                int show = want;
                if (show == 0 && !e.Has0) { (force ? forcedBuild : f.Build).Add((idx, 0, force ? double.MaxValue : rel)); show = e.Has1 ? 1 : 2; }
                if (show == 1 && !e.Has1) { f.Build.Add((idx, 1, rel)); show = 2; }
                bool changed = false;
                if (show != e.Shown) { e.From = e.Shown; e.Shown = show; e.T = 0; changed = true; }
                if (e.T < 1) { e.T = Math.Min(1, e.T + dt / s.Fade); changed = true; }
                if (changed) f.Changed.Add(idx);
                if (e.Visible(0)) e.Used0 = frame;
                if (e.Visible(1)) e.Used1 = frame;
                f.Count[e.Shown]++;
            }
            f.Build.Sort(mostPixels);
            f.Build.InsertRange(0, forcedBuild);
            Evict(f, 0, s.MaxLod0); Evict(f, 1, s.MaxLod1);
            return f;
        }

        /// <summary>Past the cap, the meshes not on screen go, least recently shown first.</summary>
        void Evict(Frame f, int lod, int max)
        {
            int count = 0; idle.Clear();
            foreach (var kv in entries)
            {
                var e = kv.Value; bool has = lod == 0 ? e.Has0 : e.Has1; if (!has) continue;
                count++;
                if (!e.Visible(lod)) idle.Add((kv.Key, lod == 0 ? e.Used0 : e.Used1));
            }
            if (count <= max) return;
            idle.Sort(leastRecent);
            foreach (var (idx, _) in idle)
            {
                if (count <= max) break;
                SetBuilt(idx, lod, false); f.Drop.Add((idx, lod)); count--;
            }
        }
    }
}
