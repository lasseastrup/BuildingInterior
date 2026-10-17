#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Triband.Storey.Play;

namespace Triband.Storey.Occlusion
{
    /// <summary>
    /// What happens to a building between the camera and the player (SPEC §5.1). Dissolve ends as Sink does (gone from
    /// the player's storey up, the rest dark, the footprint in its place) but fades there instead of collapsing.
    /// </summary>
    public enum OccluderMode { Off, Sink, Slice, Cutout, Fade, Dissolve }

    /// <summary>The designer's occlusion settings (SPEC §10 decision 8: per project, not per player).</summary>
    public sealed class OcclusionSettings
    {
        public OccluderMode mode = OccluderMode.Sink;
        /// <summary>Cut away the walls between the camera and the player in the building they are in.</summary>
        public bool cutaway = true;
        /// <summary>How high a cut wall stays (metres above the floor).</summary>
        public double stub = 1.0;
        /// <summary>Cutout and Fade: the solid dark base above the player's storey floor; Sink: the squashed storey.</summary>
        public double baseHeight = 1.0;
        /// <summary>Cutout: the hole's radius around the player, in metres.</summary>
        public double holeRadius = 2.4;
        /// <summary>Tilt the camera steeper when the player has been hidden for half a second.</summary>
        public bool assist = false;
        /// <summary>Draw the character through walls.</summary>
        public bool silhouette = true;
        /// <summary>
        /// How far past a wall's line the player must be before the cutaway starts to drop it (metres); once it is going
        /// down, crossing back is enough to raise it. A wall seen edge-on (pointing at the camera) then doesn't drop and
        /// rise again as the player passes its line. The prototype's is 0.
        /// </summary>
        public double wallMargin = 0.3;
        /// <summary>
        /// Once the player steps out of a building, it stays cut away (floors above clipped, walls down) while it still
        /// stands between the camera and the player, so a camera that trails behind isn't buried in it. The prototype's
        /// is off.
        /// </summary>
        public bool holdAfterExit = true;
    }

    /// <summary>The view's occlusion state for the shaders (SPEC §5): the building the player is in, cut and clipped.</summary>
    public struct OcclusionView
    {
        /// <summary>The building the player is in, or null.</summary>
        public BuildingData? active;
        public int floor;
        /// <summary>Everything of the active building above this height is clipped (just under the slab above).</summary>
        public double clipY;
        public bool cutOn;
        public double stub, cutBase, cutTop;
        /// <summary>The player's chest, and the camera for the cutaway test: its offset from its target applied to the player.</summary>
        public double fx, fy, fz, cx, cy, cz;
        /// <summary>The camera's direction from the focus, in the XZ plane.</summary>
        public double dirX, dirZ;
    }

    /// <summary>A building in the way: how far it has faded or sunk, and the slot its row is in.</summary>
    public sealed class Occluder
    {
        public BuildingData b = null!;
        public int slot = -1;
        /// <summary>The fade 0..1 (Cutout, Fade, Slice's ease).</summary>
        public double a;
        /// <summary>Sink's time into its plan, seconds.</summary>
        public double t;
        /// <summary>The storey at the player's height (the base that stays).</summary>
        public int k;
        public double hold, sliceY, top;
        public SinkPlan plan = new SinkPlan();
        internal bool started;
    }

    /// <summary>Sink's plan: the storeys from the player's up, collapsing from the roof down, staggered.</summary>
    public sealed class SinkPlan
    {
        public readonly List<(double y, double h)> segs = new List<(double, double)>();
        public double stag, total;
    }

    /// <summary>
    /// Occlusion, engine-free (docs/PLAY.md, slice 5.2): per frame, the view (the building the player is in, its
    /// ceiling clip and cutaway range), each wall's slide for the cutaway, and the buildings in the way with their
    /// occluder rows. A port of the prototype's <c>applyView</c>, <c>updateCutaway</c> and <c>updateOccluders</c>,
    /// checked frame by frame against its walks (<c>play.json</c>). The caller copies <see cref="Rows"/>, the slot of
    /// each occluder and the wall slides into the building table.
    /// </summary>
    public sealed class OcclusionCore
    {
        public const int Slots = 16, RowTexels = 64;
        public const double CutSlide = 0.22, Fade = 0.2, Hold = 0.35, SinkDur = 0.12;

        public readonly PlayWorld World;
        public readonly OcclusionSettings Settings;
        public OcclusionView View;
        /// <summary>The occluder rows, <see cref="Slots"/> × <see cref="RowTexels"/> × 4 floats, as the shader reads them.</summary>
        public readonly float[] Rows = new float[Slots * RowTexels * 4];
        /// <summary>
        /// How far each wall has slid down (0..1), by building id and its index in LOD0's wall list; walls fully up are
        /// absent. Only walls near the player's storey slide (<see cref="CutBand"/>, docs/PERFORMANCE.md §10.1).
        /// </summary>
        public readonly Dictionary<(string id, int wall), double> Walls = new Dictionary<(string, int), double>();

        /// <summary>
        /// How far above and below the player's storey a wall slides (metres, from the storey's floor and its ceiling's
        /// top): the storeys next to it, so a flight of stairs finds the walls there already down, and a neighbour's taller
        /// storey reaching into it. The shaders read a slide only on the player's storey; the cutaway worked on every storey
        /// of the building, 97 % of its slides for nothing.
        /// </summary>
        public const double CutBand = 8;
        /// <summary>The camera's extra pitch while the player is hidden (camera assist).</summary>
        public double Assist;

        readonly Occluder?[] slots = new Occluder?[Slots];
        readonly List<Occluder> active = new List<Occluder>();
        readonly Dictionary<string, Occluder> records = new Dictionary<string, Occluder>(StringComparer.Ordinal);
        double hiddenFor;
        // the building the view is on: the one the player is in, or the one just left while it is still in the way
        BuildingData? held; int heldFloor;

        // reused every frame, so a frame makes no garbage (FrameAllocTests)
        readonly (double x, double y, double z)[] targets = new (double, double, double)[3];
        readonly List<Occluder> hits = new List<Occluder>(), activeCopy = new List<Occluder>();
        readonly List<BuildingData> cand = new List<BuildingData>(), near = new List<BuildingData>();
        readonly HashSet<BuildingData> candSeen = new HashSet<BuildingData>();
        // each building's slides by wall, and the frame each wall was last looked at, where they were keyed by (id, wall) in
        // maps (85 % of the cutaway's time was hashing those); and the walls down now
        sealed class WallState { public double[] slide = Array.Empty<double>(); public int[] seen = Array.Empty<int>(); public bool sorted; }
        readonly Dictionary<BuildingData, WallState> wallStates = new Dictionary<BuildingData, WallState>();
        readonly List<(BuildingData b, int wall)> down = new List<(BuildingData, int)>();
        int cutFrame;
        bool[] gone = new bool[8];

        /// <summary>
        /// What the segment test needs of a building, worked out once: its tiers' starts and ends, each tier's outline's box
        /// (building-local), and its roof's rise.
        /// </summary>
        sealed class Shape { public List<(int k0, int k1)> tiers = null!; public (double x0, double z0, double x1, double z1)[] boxes = null!; public (double x0, double z0, double x1, double z1) box; public double rise; }
        readonly Dictionary<BuildingData, Shape> shapes = new Dictionary<BuildingData, Shape>();
        // the most buildings' shapes and occluder records kept (docs/PERFORMANCE.md §10.5): they only grew, 648 KB after 400
        // occluders. Past the cap the shapes are made again as they are met, and the records of buildings not in the way go
        internal const int ShapesKept = 2048, RecordsKept = 256;
        internal int shapesKept = ShapesKept, recordsKept = RecordsKept;   // a test's own
        Shape ShapeOf(BuildingData b)
        {
            if (shapes.TryGetValue(b, out var s)) return s;
            if (shapes.Count >= shapesKept) shapes.Clear();
            var tiers = Party.Tiers(b); var boxes = new (double, double, double, double)[tiers.Count];
            double X0 = double.MaxValue, Z0 = X0, X1 = double.MinValue, Z1 = X1;
            for (int i = 0; i < tiers.Count; i++)
            {
                double x0 = double.MaxValue, z0 = x0, x1 = double.MinValue, z1 = x1;
                foreach (var v in Derived.OutlineAt(b, tiers[i].k0)) { x0 = Math.Min(x0, v.x); z0 = Math.Min(z0, v.z); x1 = Math.Max(x1, v.x); z1 = Math.Max(z1, v.z); }
                boxes[i] = (x0, z0, x1, z1);
                X0 = Math.Min(X0, x0); Z0 = Math.Min(Z0, z0); X1 = Math.Max(X1, x1); Z1 = Math.Max(Z1, z1);
            }
            return shapes[b] = new Shape { tiers = tiers, boxes = boxes, box = (X0, Z0, X1, Z1), rise = Roofs.IsPitched(b) ? Roofs.Rise(World.Site, b) : 1.1 };
        }

        // how far from a tier's outline the segment test can hit: a wall's thickness (DistToEdges), or 5 cm (an edge
        // grazed), and a rounding's worth over
        static readonly double Reach = Math.Max(Dim.T_EXT, 0.05) + 1e-6;

        public OcclusionCore(PlayWorld world, OcclusionSettings settings) { World = world; Settings = settings; }

        /// <summary>The buildings in the way now, each with its slot.</summary>
        public IReadOnlyList<Occluder> Active => active;

        /// <summary>
        /// One frame, in the prototype's order: buildings in the way (from the camera this frame), then the view, then
        /// the walls' slide. <paramref name="cam"/> is the camera, <paramref name="target"/> the point it looks at.
        /// </summary>
        public void Frame(PlayerState p, (double x, double y, double z) cam, (double x, double y, double z) target, double dt)
        {
            FrameView(p, cam, target, dt);
            FrameCutaway(dt);
        }

        /// <summary><see cref="Frame"/>'s first half: the building the view is on, the buildings in the way, the view.</summary>
        public void FrameView(PlayerState p, (double x, double y, double z) cam, (double x, double y, double z) target, double dt)
        {
            HoldView(p, cam);
            UpdateOccluders(p, cam, dt);
            ApplyView(p, cam, target);
        }

        /// <summary><see cref="Frame"/>'s second half, after <see cref="FrameView"/>: the cutaway's walls (a Profiler marker of their own in Unity).</summary>
        public void FrameCutaway(double dt) => UpdateCutaway(dt);

        /// <summary>Stop: every occluder and every wall back to normal.</summary>
        public void Clear()
        {
            foreach (var r in active) { r.a = 0; r.t = 0; if (r.slot >= 0) slots[r.slot] = null; r.slot = -1; }
            active.Clear(); Walls.Clear(); wallStates.Clear(); down.Clear(); hiddenFor = 0; Assist = 0; held = null;
            View = new OcclusionView { clipY = 1e9 };
        }

        // ---- props drawn whole (docs/PROPS.md §2.5) ----

        /// <summary>
        /// Whether a point of building b at site height y is hidden this frame: above the ceiling clip of the building the
        /// player is in, or in a storey of a building in the way that has gone (sunk, sliced off, or faded past half).
        /// What a prop whose shader can't follow the occlusion is switched off by: the whole of it, by one point.
        /// </summary>
        public bool HidesPoint(BuildingData b, double y)
        {
            if (View.active != null && View.active.id == b.id) return y > View.clipY;
            for (int i = 0; i < active.Count; i++)
            {
                var r = active[i];
                if (r.slot < 0 || r.b.id != b.id) continue;
                int o = r.slot * RowTexels * 4, n = (int)Rows[o];
                if (n > 0)
                {
                    // Sink: the storey holding y, gone or squashed past half
                    for (int s = 0; s < n; s++)
                    {
                        int q = o + (1 + s) * 4; double top = s + 1 < n ? Rows[q + 4] : double.PositiveInfinity;
                        if (y >= Rows[q] && y < top) return Rows[q + 2] > 0.5 || Rows[q + 1] < 0.5;
                    }
                    return false;
                }
                if (y > Rows[o + 2]) return true;                         // Slice
                return y >= Rows[o + 3] && Rows[o + 1] >= 0.5;           // Cutout, Fade, Dissolve: above the base, half gone
            }
            return false;
        }

        // ---- the view ----

        /// <summary>The building the view is on this frame (<see cref="OcclusionSettings.holdAfterExit"/>).</summary>
        void HoldView(PlayerState p, (double x, double y, double z) cam)
        {
            var loc = World.Locate(p.x, p.y, p.z);
            if (loc != null) { held = loc.Value.b; heldFloor = loc.Value.floor; return; }
            if (held != null && Settings.holdAfterExit)
            {
                var b = World.Site.ById(held.id);
                if (b != null && heldFloor <= b.floors.Count && AnyHits(b, cam, Targets(p, cam))) { held = b; return; }
            }
            held = null;
        }

        void ApplyView(PlayerState p, (double x, double y, double z) cam, (double x, double y, double z) target)
        {
            var v = View;
            var loc = held != null ? (held, heldFloor) : ((BuildingData, int)?)null;
            v.fx = p.x; v.fy = p.y + FollowCamera.Chest; v.fz = p.z;
            // the camera trails the player: test the cutaway with its offset applied to the player's exact position, or
            // a wall seen edge-on drops for a frame as the player crosses its line
            v.cx = v.fx + cam.x - target.x; v.cy = v.fy + cam.y - target.y; v.cz = v.fz + cam.z - target.z;
            v.stub = Settings.stub;
            double dx = v.cx - v.fx, dz = v.cz - v.fz, len = Math.Sqrt(dx * dx + dz * dz); if (len == 0) len = 1;
            v.dirX = dx / len; v.dirZ = dz / len;
            if (loc != null)
            {
                var (b, floor) = loc.Value; int N = b.floors.Count;
                v.active = b; v.floor = floor;
                v.clipY = floor < N ? Derived.FloorBase(b, floor) + Derived.FloorH(b, floor) - Dim.SLAB - 0.01 : 1e9;
                v.cutOn = Settings.cutaway; v.cutBase = Derived.FloorBase(b, floor); v.cutTop = Derived.FloorBase(b, floor) + (floor < N ? Derived.FloorH(b, floor) : 3.3) + 0.02;
            }
            else { v.active = null; v.clipY = 1e9; v.cutOn = false; }
            View = v;
        }

        // ---- the sliding cutaway ----

        /// <summary>
        /// The cutaway test for one wall: it separates camera and player, and the sightline crosses it (±0.5 m for the
        /// body). With a <paramref name="margin"/>, the player must be that far past the wall's line.
        /// </summary>
        public static bool WallBlocks(double[] W, double cx, double cz, double fx, double fz, double margin = 0)
        {
            double nl = Tiers.Hypot(W[2], W[3]), nx = W[2] / nl, nz = W[3] / nl;
            double sc = (cx - W[0]) * nx + (cz - W[1]) * nz, sp = (fx - W[0]) * nx + (fz - W[1]) * nz;
            if (sc * sp >= 0 || Math.Abs(sp) < margin) return false;
            if (nl < 1.01) return true;
            double t = sc / (sc - sp), x = cx + (fx - cx) * t, z = cz + (fz - cz) * t, st = (x - W[0]) * nz - (z - W[1]) * nx;
            return st > -0.5 && st < nl - 1 + 0.5;
        }

        void UpdateCutaway(double dt)
        {
            double step = dt / CutSlide;
            int frame = ++cutFrame;
            var act = View.active;
            if (View.cutOn && act != null)
            {
                int ai = World.Site.IndexOf(act);
                double y0 = View.cutBase - CutBand, y1 = View.cutTop + CutBand;
                // only the buildings next to it can share a wall with it: the rest's shapes are never made for this
                foreach (var b in World.Touching(act))
                {
                    var shape = World.ShapeOf(b); var walls = shape.Walls; var bases = shape.WallBase;
                    var st = StateOf(b, shape);
                    // the walls whose storey is in the band: a storey's walls follow the one below's (found by halves), or
                    // looked at one by one where they don't
                    int i = st.sorted ? FirstAt(bases, y0) : 0;
                    for (; i < walls.Count; i++)
                    {
                        double yb = bases[i];
                        if (yb >= y1) { if (st.sorted) break; continue; }
                        if (yb < y0) continue;
                        // the active building's walls, and the neighbours' party walls shared with it
                        if (b != act && (walls[i].K < 8 || (walls[i].K >> 3) - 1 != ai)) continue;
                        st.seen[i] = frame;
                        double a = st.slide[i];
                        // a wall starts down only with the player clear of its line, and stays down until they cross back
                        bool up = WallBlocks(walls[i].W, View.cx, View.cz, View.fx, View.fz, a > 0 ? 0 : Settings.wallMargin);
                        if (!up && a == 0) continue;
                        SetWall(b, st, i, Math.Max(0, Math.Min(1, a + (up ? step : -step))));
                    }
                }
            }
            // walls no longer cut slide back up
            for (int j = down.Count - 1; j >= 0; j--)
            {
                var (b, i) = down[j];
                if (!wallStates.TryGetValue(b, out var st) || i >= st.slide.Length) { down.RemoveAt(j); continue; }
                if (st.seen[i] == frame) continue;
                SetWall(b, st, i, Math.Max(0, st.slide[i] - step));
            }
        }

        // a building's slides, sized to its walls (afresh when its shape has another count)
        WallState StateOf(BuildingData b, Lod0Shape shape)
        {
            int n = shape.Walls.Count;
            if (wallStates.TryGetValue(b, out var st) && st.slide.Length == n) return st;
            if (st != null) for (int i = 0; i < st.slide.Length; i++) if (st.slide[i] > 0) SetWall(b, st, i, 0);
            st = wallStates[b] = new WallState { slide = new double[n], seen = new int[n], sorted = true };
            var bases = shape.WallBase;
            for (int i = 1; i < bases.Count; i++) if (bases[i] < bases[i - 1]) { st.sorted = false; break; }
            return st;
        }

        // the first wall whose storey starts at or above y, in walls ordered by storey
        static int FirstAt(List<double> bases, double y)
        {
            int lo = 0, hi = bases.Count;
            while (lo < hi) { int m = (lo + hi) >> 1; if (bases[m] < y) lo = m + 1; else hi = m; }
            return lo;
        }

        void SetWall(BuildingData b, WallState st, int i, double v)
        {
            double was = st.slide[i];
            if (v > 0)
            {
                if (was == 0) down.Add((b, i));
                st.slide[i] = v; Walls[(b.id, i)] = v;
                return;
            }
            if (was == 0) return;
            st.slide[i] = 0; Walls.Remove((b.id, i));
            for (int j = down.Count - 1; j >= 0; j--) if (ReferenceEquals(down[j].b, b) && down[j].wall == i) { down.RemoveAt(j); break; }
        }

        // ---- buildings in the way ----

        static double DistToEdges(List<Vec2> fp, double x, double z)
        {
            double d = 1e9;
            for (int i = 0; i < fp.Count; i++) { var a = fp[i]; var c = fp[(i + 1) % fp.Count]; d = Math.Min(d, Tiers.SegDist(x, z, a.x, a.z, c.x, c.z).d); }
            return d;
        }

        double RoofRise(BuildingData b) => ShapeOf(b).rise;

        /// <summary>
        /// The segment p–q passes through the building's volume: one prism per setback tier, the roof's rise or the
        /// parapet on the top one. Grazing or running along an edge counts (a ray down the seam between two joined
        /// buildings touches neither interior, but both walls are there).
        /// </summary>
        public bool SegmentHits(BuildingData b, (double x, double y, double z) p, (double x, double y, double z) q)
        {
            int N = b.floors.Count; var shape = ShapeOf(b); double extra = shape.rise;
            // the whole segment clear of every tier's box: no hit, and no tier's heights worked out
            var all = shape.box; double px = p.x - b.pos.x, pz = p.z - b.pos.z, qx = q.x - b.pos.x, qz = q.z - b.pos.z;
            if (Math.Max(px, qx) < all.x0 - Reach || Math.Min(px, qx) > all.x1 + Reach || Math.Max(pz, qz) < all.z0 - Reach || Math.Min(pz, qz) > all.z1 + Reach) return false;
            for (int ti = 0; ti < shape.tiers.Count; ti++)
            {
                var (k0, k1) = shape.tiers[ti];
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
                // the segment's box clear of the tier's outline's, widened by the test's reach: no hit (89 % of the
                // candidates miss, and this rejects most of them before the edges are looked at, docs/PERFORMANCE.md §10.5)
                var bx = shape.boxes[ti];
                if (Math.Max(A.x, B.x) < bx.x0 - Reach || Math.Min(A.x, B.x) > bx.x1 + Reach || Math.Max(A.z, B.z) < bx.z0 - Reach || Math.Min(A.z, B.z) > bx.z1 + Reach) continue;
                bool Near(Vec2 v, Vec2 w) => Tiers.SegCross(A, B, v, w)
                    || Math.Min(Math.Min(Tiers.SegDist(v.x, v.z, A.x, A.z, B.x, B.z).d, Tiers.SegDist(w.x, w.z, A.x, A.z, B.x, B.z).d),
                                Math.Min(Tiers.SegDist(A.x, A.z, v.x, v.z, w.x, w.z).d, Tiers.SegDist(B.x, B.z, v.x, v.z, w.x, w.z).d)) < 0.05;
                bool edge = false;
                for (int i = 0; i < fp.Count && !edge; i++) edge = Near(fp[i], fp[(i + 1) % fp.Count]);
                if (Geo.Pip(fp, A.x, A.z) || Geo.Pip(fp, B.x, B.z) || edge || DistToEdges(fp, A.x, A.z) < Dim.T_EXT || DistToEdges(fp, B.x, B.z) < Dim.T_EXT) return true;
            }
            return false;
        }

        /// <summary>The player is at one of b's doors (within 1.2 m, on its floor): never in the way on the way in or out.</summary>
        public static bool AtDoorOf(BuildingData b, PlayerState p)
        {
            double lx = p.x - b.pos.x, lz = p.z - b.pos.z;
            foreach (var e in b.entrances)
            {
                int k = e.k;
                if (Math.Abs(p.y - Derived.FloorBase(b, k)) > 1) continue;
                var fp = Derived.OutlineAt(b, k);
                if (e.edge < 0 || e.edge >= fp.Count) continue;
                var a = fp[e.edge]; var c = fp[(e.edge + 1) % fp.Count];
                if (Tiers.Hypot(a.x + (c.x - a.x) * e.t - lx, a.z + (c.z - a.z) * e.t - lz) < 1.2) return true;
            }
            return false;
        }

        /// <summary>Slice: just under the ceiling of the occluder's storey at the player's height (at least head height).</summary>
        static double SliceHeight(BuildingData b, PlayerState p)
        {
            int N = b.floors.Count, k = 0; double yj = b.elevation, yk = yj;   // the bases summed going up
            for (int j = 1; j < N; j++) { yj = Derived.NextFloorBase(b, j - 1, yj); if (p.y + 0.5 >= yj) { k = j; yk = yj; } }
            return Math.Max(yk + Derived.FloorH(b, k) - Dim.SLAB - 0.01, p.y + 2.0);
        }

        /// <summary>The occluder's storey at the player's height, the floor count when the player is above its roof.</summary>
        static int OccStorey(BuildingData b, PlayerState p)
        {
            int N = b.floors.Count, k = 0; double yj = b.elevation;   // the bases summed going up
            for (int j = 1; j <= N; j++) { yj = Derived.NextFloorBase(b, j - 1, yj); if (p.y + 0.5 >= yj) k = j; }
            return k;
        }

        static void Plan(SinkPlan plan, BuildingData b, int k)
        {
            int N = b.floors.Count; plan.segs.Clear();
            double yj = Derived.FloorBase(b, k);
            for (int j = k; j <= N; yj = Derived.NextFloorBase(b, j, yj), j++) plan.segs.Add((yj, j < N ? Derived.FloorH(b, j) : 0));
            int nc = plan.segs.Count;
            plan.stag = Math.Min(0.03, 0.1 / Math.Max(nc, 1));
            plan.total = (nc - 1) * plan.stag + SinkDur;
        }

        Occluder Record(BuildingData b)
        {
            if (!records.TryGetValue(b.id, out var r))
            {
                if (records.Count >= recordsKept) ForgetIdle();
                records[b.id] = r = new Occluder { b = b };
            }
            r.b = b;
            return r;
        }

        readonly List<string> idle = new List<string>();

        /// <summary>
        /// The records of the buildings not in the way let go: at rest, nothing of theirs is carried over. One hit this frame
        /// and not yet active stays (it is about to take a slot).
        /// </summary>
        void ForgetIdle()
        {
            idle.Clear();
            foreach (var kv in records) if (!active.Contains(kv.Value) && !hits.Contains(kv.Value)) idle.Add(kv.Key);
            foreach (var id in idle) records.Remove(id);
        }

        /// <summary>How many buildings' shapes and occluder records are kept now (for tests).</summary>
        internal (int shapes, int records) Kept => (shapes.Count, records.Count);

        /// <summary>The three rays' ends, the feet, chest and head, 0.5 m short so the character's own body touching a wall doesn't count.</summary>
        (double x, double y, double z)[] Targets(PlayerState p, (double x, double y, double z) c)
        {
            double dl = Tiers.Hypot(c.x - p.x, c.z - p.z), sh = dl > 0.6 ? 0.5 / dl : 0;
            targets[0] = (p.x + (c.x - p.x) * sh, p.y + 0.25, p.z + (c.z - p.z) * sh);
            targets[1] = (p.x + (c.x - p.x) * sh, p.y + 1.1, p.z + (c.z - p.z) * sh);
            targets[2] = (p.x + (c.x - p.x) * sh, p.y + 1.75, p.z + (c.z - p.z) * sh);
            return targets;
        }

        bool AnyHits(BuildingData b, (double x, double y, double z) c, (double x, double y, double z)[] tg)
        {
            foreach (var t in tg) if (SegmentHits(b, c, t)) return true;
            return false;
        }

        void UpdateOccluders(PlayerState p, (double x, double y, double z) c, double dt)
        {
            hits.Clear(); var m = Settings.mode;
            if (m != OccluderMode.Off)
            {
                var tg = Targets(p, c);
                double L = Tiers.Hypot(c.x - p.x, c.z - p.z); int steps = (int)Math.Ceiling(L / 16);
                cand.Clear(); candSeen.Clear();
                for (int i = 0; i <= steps; i++)
                {
                    double f = (double)i / Math.Max(steps, 1);
                    World.NearInto(c.x + (p.x - c.x) * f, c.z + (p.z - c.z) * f, 17, near);
                    foreach (var b in near) if (candSeen.Add(b)) cand.Add(b);
                }
                foreach (var b in cand)
                {
                    if (b == held || AtDoorOf(b, p)) continue;
                    if (AnyHits(b, c, tg))
                    {
                        var r = Record(b); hits.Add(r);
                        r.sliceY = SliceHeight(b, p); r.top = Derived.RoofY(b) + RoofRise(b) + 0.5;
                        // the storey that stays is held while the building is down (sunk, or partly dissolved)
                        int k = OccStorey(b, p); if (k != r.k && !(r.t > 0) && !(m == OccluderMode.Dissolve && r.a > 0)) r.k = k;
                    }
                }
            }
            foreach (var r in hits)
            {
                r.hold = Hold;
                if (!active.Contains(r))
                {
                    int i = Array.IndexOf(slots, null); if (i < 0) continue;
                    slots[i] = r; r.slot = i; r.t = 0; r.k = OccStorey(r.b, p); active.Add(r);
                }
            }
            activeCopy.Clear(); activeCopy.AddRange(active);
            foreach (var r in activeCopy)
            {
                var b = World.Site.ById(r.b.id);
                bool want = b != null && (hits.Contains(r) || (r.hold -= dt) > 0);
                r.a = Math.Max(0, Math.Min(1, r.a + (want ? dt : -dt) / Fade));
                if (b != null)
                {
                    Plan(r.plan, b, Math.Min(r.k, b.floors.Count));
                    r.t = m == OccluderMode.Sink ? Math.Max(0, Math.Min(r.plan.total, r.t + (want ? dt : -dt * 1.25))) : 0;
                }
                bool done = !want && r.a <= 0 && !(r.t > 0);
                if (done || b == null || b == held)
                {
                    r.a = 0; r.t = 0; active.Remove(r);
                    if (r.slot >= 0) { slots[r.slot] = null; r.slot = -1; }
                    continue;
                }
                WriteRow(r, b);
            }
            // camera assist: after half a second hidden, tilt the camera a little steeper (never overriding the player's orbit)
            hiddenFor = hits.Count > 0 ? hiddenFor + dt : 0;
            double wantTilt = Settings.assist && hiddenFor > 0.5 ? 0.3 : 0;
            Assist += (wantTilt - Assist) * Math.Min(1, dt * 2.5);
        }

        void WriteRow(Occluder r, BuildingData b)
        {
            var m = Settings.mode; int o = r.slot * RowTexels * 4;
            Array.Clear(Rows, o, RowTexels * 4); Rows[o + 2] = 1e9f; Rows[o + 3] = -1e9f;
            if (m == OccluderMode.Slice) { double e = r.a * r.a * (3 - 2 * r.a); Rows[o + 2] = (float)(r.top + (r.sliceY - r.top) * e); return; }
            int k = r.k, N = b.floors.Count;
            double bas = (k < N ? Derived.FloorBase(b, k) : Derived.RoofY(b)) + Settings.baseHeight;
            if (m == OccluderMode.Cutout || m == OccluderMode.Fade) { Rows[o + 1] = (float)r.a; Rows[o + 3] = (float)bas; return; }
            if (m == OccluderMode.Dissolve)
            {
                // no segments: (0, fade, -, the floor of the storey that stays). The shader dithers away everything from
                // that floor up and darkens the rest; the footprint dithers in with the complementary pattern
                Rows[o + 1] = (float)r.a; Rows[o + 3] = (float)(k < N ? Derived.FloorBase(b, k) : Derived.RoofY(b)); return;
            }
            var S = r.plan.segs; int n = S.Count; double T = r.t;
            Rows[o] = n; Rows[o + 1] = (float)Math.Min(1, T / 0.08);
            if (gone.Length < n) gone = new bool[Math.Max(n, gone.Length * 2)];
            for (int i = n - 1; i >= 0; i--)
            {
                int q = o + (1 + i) * 4;
                double pr = Math.Max(0, Math.Min(1, (T - (n - 1 - i) * r.plan.stag) / SinkDur));
                double sc = pr >= 1 ? 0 : 1 - pr * pr; gone[i] = pr >= 1;
                Rows[q] = (float)S[i].y; Rows[q + 1] = (float)sc; Rows[q + 2] = gone[i] ? 1 : 0; Rows[q + 3] = i + 1 < n && gone[i + 1] ? 1 : 0;
            }
        }
    }
}
