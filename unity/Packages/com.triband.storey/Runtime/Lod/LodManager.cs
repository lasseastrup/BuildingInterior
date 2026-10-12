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
        /// <summary>
        /// LOD0's see-through windows show LOD1's painted pane where LOD0 takes over, and are clear glass within this share
        /// of that distance (blended between), so the switch changes nothing in the windows. 1: clear at once (no blend).
        /// </summary>
        public double GlassClear = 0.6;

        /// <summary>The distance at which a building's nearest point takes LOD0 coming closer, for a camera's pixels per metre.</summary>
        public double Lod0Distance(double pixelsPerMetre) => pixelsPerMetre * Bias / (PX[0] * (1 + Hysteresis));

        /// <summary>Worker threads generating detail at once: 0 builds on the main thread, -1 one fewer than the cores (at least one).</summary>
        public int Threads = -1;
    }

    /// <summary>
    /// Picks each building's LOD every frame, as the prototype's LOD system does (docs/CITY.md §2): LOD0 full detail,
    /// LOD1 the shell, LOD2 the massing (always resident, merged into cells), 3 not drawn. A LOD the building doesn't have
    /// yet is asked for (largest on screen first) and the best one it has is shown meanwhile; a change cross-fades.
    /// LOD0 and LOD1 meshes past the resident caps are dropped, least recently shown first. Engine-free: the renderer
    /// builds and drops the meshes and writes the state into the building table.
    ///
    /// The buildings are kept in 128 m squares of the ground. A square whose buildings all show their massing (or nothing)
    /// and stay so this frame is passed over whole: every building's nearest point is between the square's box's nearest
    /// and farthest, so when neither crosses a threshold none of them can (exactly so: the floating-point steps are
    /// monotonic). Most of a city is such squares.
    /// </summary>
    public sealed class LodManager
    {
        /// <summary>
        /// One building: its bounds (site space, metres; the ground is y 0), and its LOD state. Change the state only right
        /// after <see cref="Set"/>, or through <see cref="SetBuilt"/>: a square whose buildings stay as they are is passed over.
        /// </summary>
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
            internal int idx;
            internal Square? square;
        }

        /// <summary>A square of the ground's buildings, the box round them all, and whether they are settled.</summary>
        internal sealed class Square
        {
            public readonly (int x, int z) key;
            public readonly List<Entry> members = new List<Entry>();
            public double x0, z0, x1, z1, yLo, yHi;
            public bool stale = true;
            /// <summary>Every member wants and shows this LOD (2 or 3), not fading; -1 when not.</summary>
            public int settled = -1;
            public long forcedAt = -1;
            public Square((int, int) key) { this.key = key; }
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
        // ties in building order, so the order never depends on how the buildings are kept
        sealed class MostPixels : IComparer<(int idx, int lod, double px)>
        {
            public int Compare((int idx, int lod, double px) a, (int idx, int lod, double px) b)
            { int c = b.px.CompareTo(a.px); if (c != 0) return c; c = a.idx.CompareTo(b.idx); return c != 0 ? c : a.lod.CompareTo(b.lod); }
        }
        sealed class LeastRecent : IComparer<(int idx, long used)>
        {
            public int Compare((int idx, long used) a, (int idx, long used) b) { int c = a.used.CompareTo(b.used); return c != 0 ? c : a.idx.CompareTo(b.idx); }
        }
        static readonly MostPixels mostPixels = new MostPixels();
        static readonly LeastRecent leastRecent = new LeastRecent();

        public readonly LodSettings Settings;
        readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        readonly Dictionary<(int, int), Square> byKey = new Dictionary<(int, int), Square>();
        readonly List<Square> squares = new List<Square>();
        // the buildings holding their LOD0 and LOD1 meshes: the caps look at these only
        readonly HashSet<Entry> has0 = new HashSet<Entry>(), has1 = new HashSet<Entry>();
        long frame;

        public LodManager(LodSettings? s = null) { Settings = s ?? new LodSettings(); }

        public IReadOnlyDictionary<int, Entry> Entries => entries;

        /// <summary>How many buildings have their LOD0 and their LOD1 built.</summary>
        public (int lod0, int lod1) Resident() => (has0.Count, has1.Count);
        public Entry? Get(int idx) => entries.TryGetValue(idx, out var e) ? e : null;

        /// <summary>Add a building (or move it: an edit), by its table index. Its detail LODs are marked unbuilt.</summary>
        public Entry Set(int idx, double x0, double z0, double x1, double z1, double y1)
        {
            if (!entries.TryGetValue(idx, out var e)) entries[idx] = e = new Entry { idx = idx };
            e.x0 = x0; e.z0 = z0; e.x1 = x1; e.z1 = z1; e.y1 = y1;
            Built(e, 0, false); Built(e, 1, false);
            var key = Cells.KeyOf((x0 + x1) / 2, (z0 + z1) / 2);
            if (e.square == null || e.square.key != key)
            {
                Leave(e);
                if (!byKey.TryGetValue(key, out var sq)) { byKey[key] = sq = new Square(key); squares.Add(sq); }
                sq.members.Add(e); e.square = sq;
            }
            e.square.stale = true; e.square.settled = -1;
            return e;
        }

        void Leave(Entry e)
        {
            var sq = e.square; if (sq == null) return;
            sq.members.Remove(e); sq.stale = true; sq.settled = -1; e.square = null;
        }

        public void Remove(int idx)
        {
            if (!entries.TryGetValue(idx, out var e)) return;
            Built(e, 0, false); Built(e, 1, false); Leave(e);
            entries.Remove(idx);
        }

        public void Clear() { entries.Clear(); byKey.Clear(); squares.Clear(); has0.Clear(); has1.Clear(); }

        /// <summary>A building's LOD0 or LOD1 mesh is built (or gone).</summary>
        public void SetBuilt(int idx, int lod, bool built)
        {
            if (!entries.TryGetValue(idx, out var e)) return;
            Built(e, lod, built);
            if (e.square != null) e.square.settled = -1;
        }

        void Built(Entry e, int lod, bool built)
        {
            if (lod == 0) { e.Has0 = built; if (built) has0.Add(e); else has0.Remove(e); }
            else if (lod == 1) { e.Has1 = built; if (built) has1.Add(e); else has1.Remove(e); }
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
            // a forced building's square is looked at building by building (a HashSet's own enumerator: no garbage)
            if (forced is HashSet<int> fs) { foreach (int i in fs) Force(i); }
            else if (forced != null) foreach (int i in forced) Force(i);
            // passing over a square needs the finer LODs at more pixels, as they are unless the settings say otherwise
            bool ordered = s.PX[0] >= s.PX[1] && s.PX[1] >= s.PX[2] && H > -1;
            for (int q = 0; q < squares.Count; q++)
            {
                var sq = squares[q]; var ms = sq.members;
                if (ms.Count == 0) continue;
                if (sq.stale) Bound(sq);
                if (ordered && sq.settled >= 2 && sq.forcedAt != frame && Stays(sq, cx, cy, cz, pxm, H))
                {
                    f.Count[sq.settled] += ms.Count;
                    continue;
                }
                int settled = -2;
                for (int m = 0; m < ms.Count; m++)
                {
                    var e = ms[m]; int idx = e.idx;
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
                    int st = e.T >= 1 && e.Want == e.Shown && e.Want >= 2 ? e.Want : -1;
                    settled = settled == -2 || settled == st ? st : -1;
                }
                sq.settled = settled;
            }
            f.Build.Sort(mostPixels);
            f.Build.InsertRange(0, forcedBuild);
            Evict(f, 0, s.MaxLod0); Evict(f, 1, s.MaxLod1);
            return f;
        }

        void Force(int idx) { if (entries.TryGetValue(idx, out var e) && e.square != null) e.square.forcedAt = frame; }

        /// <summary>The box round a square's buildings (y from the lowest top, or the ground, to the highest).</summary>
        static void Bound(Square sq)
        {
            double x0 = double.MaxValue, z0 = x0, yLo = 0, x1 = double.MinValue, z1 = x1, yHi = 0;
            foreach (var e in sq.members)
            {
                x0 = Math.Min(x0, e.x0); z0 = Math.Min(z0, e.z0); x1 = Math.Max(x1, e.x1); z1 = Math.Max(z1, e.z1);
                yLo = Math.Min(yLo, e.y1); yHi = Math.Max(yHi, e.y1);
            }
            sq.x0 = x0; sq.z0 = z0; sq.x1 = x1; sq.z1 = z1; sq.yLo = yLo; sq.yHi = yHi; sq.stale = false;
        }

        /// <summary>
        /// A settled square's buildings all stay so this frame: each one's nearest point is no nearer than the square's
        /// box's nearest and no farther than its farthest, and neither of those crosses a threshold of the LOD they show.
        /// </summary>
        bool Stays(Square sq, double cx, double cy, double cz, double pxm, double H)
        {
            var s = Settings;
            double nx = Math.Max(Math.Max(sq.x0 - cx, 0), cx - sq.x1), ny = Math.Max(Math.Max(-cy, 0), cy - sq.yHi), nz = Math.Max(Math.Max(sq.z0 - cz, 0), cz - sq.z1);
            double near = Math.Max(1, Math.Sqrt(nx * nx + ny * ny + nz * nz));
            double fx = Math.Max(Math.Abs(cx - sq.x0), Math.Abs(cx - sq.x1)), fy = Math.Max(Math.Abs(cy), Math.Max(Math.Abs(cy - sq.yHi), Math.Abs(cy - sq.yLo))), fz = Math.Max(Math.Abs(cz - sq.z0), Math.Abs(cz - sq.z1));
            double far = Math.Max(1, Math.Sqrt(fx * fx + fy * fy + fz * fz));
            if (sq.settled == 2) return pxm / near < s.PX[1] * (1 + H) && pxm / far >= s.PX[2] * (1 - H) && far <= s.Far;
            return pxm / near < s.PX[2] * (1 + H) || near > s.Far;   // 3: none near enough for its massing, or all past the far distance
        }

        /// <summary>Past the cap, the meshes not on screen go, least recently shown first.</summary>
        void Evict(Frame f, int lod, int max)
        {
            var held = lod == 0 ? has0 : has1;
            int count = held.Count; if (count <= max) return;
            idle.Clear();
            foreach (var e in held) if (!e.Visible(lod)) idle.Add((e.idx, lod == 0 ? e.Used0 : e.Used1));
            idle.Sort(leastRecent);
            foreach (var (idx, _) in idle)
            {
                if (count <= max) break;
                SetBuilt(idx, lod, false); f.Drop.Add((idx, lod)); count--;
            }
        }
    }
}
