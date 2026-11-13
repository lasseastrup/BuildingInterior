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

    /// <summary>
    /// What Dissolve leaves of a building in the way once it has gone, on top of its footprint (docs/PLAY.md §4). Storey's own.
    /// </summary>
    public enum DissolveLook
    {
        /// <summary>Nothing: the footprint alone (a dark fill ringed by a low rim of the outer wall).</summary>
        Plain,
        /// <summary>A wire outline of the building: its corners, the top of each tier and, with <see cref="OcclusionSettings.outlineStoreys"/>, a line round every storey (<see cref="SinkFootprint.Cage"/>).</summary>
        Outline,
        /// <summary>A pale screen of the whole building, an x-ray, a share of its pixels (<see cref="OcclusionSettings.dissolveKeeps"/>).</summary>
        Ghost,
        /// <summary>Diagonal stripes across its faces, dark as an ink hatching (<see cref="OcclusionSettings.hatchSpacing"/>, <see cref="OcclusionSettings.dissolveKeeps"/>).</summary>
        Hatch,
        /// <summary>The storey at the player's height up to the cutaway's stub, its walls capped as the cutaway leaves them; no footprint.</summary>
        Stub,
        /// <summary>Ghost's pale screen with Outline's lines.</summary>
        Blueprint,
    }

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
        /// stands between the camera and the player and they are at its door, so a camera that trails behind isn't buried in
        /// it. Past the door it is handed over to the buildings in the way, as one the player never entered is: it goes
        /// (sinks, dissolves...) still cut away, and is let go once it has gone (<see cref="OcclusionView.leaving"/>). With
        /// Cutout and Fade, which leave it standing round the camera, it is held while the camera is in it too, and with the
        /// buildings in the way left alone (Off) while it is in the way at all. The prototype's is off.
        /// </summary>
        public bool holdAfterExit = true;
        /// <summary>
        /// The cutaway takes down every wall that faces the camera, as well as those between the camera and the player: the
        /// storey opens on the camera's side, as a doll's house does, and its far walls stand. A wall faces the camera when
        /// the camera's direction is on its outside (its normal's side; for a neighbour's party wall, the neighbour's), so a
        /// wall goes down or up only as the camera turns past it, never as the player walks about. A partition, with a room
        /// either side, faces both ways and goes down as before, when it is between the camera and the player
        /// (<see cref="Generate.MeshBuilder.Partitions"/>). Storey's own; the prototype's is off.
        /// </summary>
        public bool wallsFacingCamera = false;
        /// <summary>What Dissolve leaves of a building in the way (<see cref="DissolveLook"/>). Storey's own.</summary>
        public DissolveLook dissolveLook = DissolveLook.Plain;
        /// <summary>How much Ghost and Hatch keep: Ghost's (and Blueprint's) share of the building's pixels, Hatch's line's share of its spacing.</summary>
        public double dissolveKeeps = 0.25;
        /// <summary>Hatch: metres from one stripe to the next.</summary>
        public double hatchSpacing = 0.45;
        /// <summary>Outline and Blueprint: the lines' thickness, in metres.</summary>
        public double outlineWidth = 0.06;
        /// <summary>Outline and Blueprint: a line round every storey too, not only the corners and each tier's top.</summary>
        public bool outlineStoreys = true;

        /// <summary>The outline's thickness Dissolve's footprint is made with now (0: none; <see cref="SinkFootprint.Build"/>).</summary>
        public double CageWidth => mode == OccluderMode.Dissolve && (dissolveLook == DissolveLook.Outline || dissolveLook == DissolveLook.Blueprint) ? Math.Max(0.01, outlineWidth) : 0;
        /// <summary>Whether the building in the way has a footprint stand in for it: not under Dissolve's Stub, whose storey stays.</summary>
        public bool Footprint => mode == OccluderMode.Sink || mode == OccluderMode.Dissolve && dissolveLook != DissolveLook.Stub;
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
        /// <summary>
        /// Out on a terrace of the active building with what stands over it between the camera and the player: the terrace's
        /// floor. Everything of the active building above it is drawn as a building in the way, by its row in
        /// <see cref="OcclusionCore.Active"/>, but the terrace's own walls (<see cref="OcclusionCore.TerraceWalls"/>); the storeys
        /// under the terrace, which the player stands on, are drawn as before. 1e9 when not. Storey's own.
        /// </summary>
        public double terraceY;
        /// <summary>
        /// The active building is one the player has just left, past its door (<see cref="OcclusionSettings.holdAfterExit"/>),
        /// being handed over to the buildings in the way: still clipped and cut away, and in the way as well, by its row in
        /// <see cref="OcclusionCore.Active"/>. Once it has gone as far as the mode takes it, it is let go and drawn as in the way
        /// alone, which then looks the same. Storey's own.
        /// </summary>
        public bool leaving;
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
    /// The occluder slots and their rows, one set for every core drawing through one building table (docs/PLAY.md §7): the
    /// core of the site the player is in and those of the sites round it take their slots here, so no two buildings in
    /// the way share one, and each core writes only its own slots' rows. A core made without one has its own.
    /// </summary>
    public sealed class OccluderSlots
    {
        /// <summary>The occluder rows, <see cref="OcclusionCore.Slots"/> × <see cref="OcclusionCore.RowTexels"/> × 4 floats, as the shader reads them.</summary>
        public readonly float[] Rows = new float[OcclusionCore.Slots * OcclusionCore.RowTexels * 4];
        // the occluder in each slot, whichever core it is
        internal readonly Occluder?[] taken = new Occluder?[OcclusionCore.Slots];

        /// <summary>How many slots are taken now, by every core sharing them.</summary>
        public int Taken { get { int n = 0; for (int i = 0; i < taken.Length; i++) if (taken[i] != null) n++; return n; } }
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
        /// <summary>
        /// The occluder rows, <see cref="Slots"/> × <see cref="RowTexels"/> × 4 floats, as the shader reads them: the
        /// <see cref="Shared"/> slots', so an occluder's row is at its slot whichever core it is.
        /// </summary>
        public readonly float[] Rows;
        /// <summary>Where this core's occluders take their slots: its own, or the slots every site's core drawing through one table shares.</summary>
        public readonly OccluderSlots Shared;
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
        /// <summary>
        /// The buildings hidden at run time, by id (docs/PLAY.md §6): they draw nothing, so none is ever in the way, and one
        /// hidden while in the way comes back up as one out of it does. Null or empty for none. Storey's own.
        /// </summary>
        public ICollection<string>? Hidden;

        readonly Occluder?[] slots;   // Shared's
        readonly List<Occluder> active = new List<Occluder>();
        readonly Dictionary<string, Occluder> records = new Dictionary<string, Occluder>(StringComparer.Ordinal);
        double hiddenFor;
        // the building the view is on: the one the player is in, or the one just left while it is still in the way; and
        // whether that one is being handed over to the buildings in the way (OcclusionView.leaving)
        BuildingData? held; int heldFloor; bool leaving;
        // out on a terrace of the building the view is on (LookAtTerrace): that building and the terrace's storey, whether what
        // stands over the terrace is between the camera and the player, and whether they are at a walk-in setback's door
        BuildingData? terrace; int terraceK; bool terraceInWay, terraceAtDoor;
        // the terrace's own walls (TerraceWalls), found once for the shape and the storey they were found in
        readonly List<int> terraceWalls = new List<int>();
        Lod0Shape? terraceWallsOf; int terraceWallsK = -1;

        // reused every frame, so a frame makes no garbage (FrameAllocTests)
        readonly (double x, double y, double z)[] targets = new (double, double, double)[3];
        readonly List<Occluder> hits = new List<Occluder>(), activeCopy = new List<Occluder>();
        readonly List<BuildingData> cand = new List<BuildingData>(), near = new List<BuildingData>();
        readonly HashSet<BuildingData> candSeen = new HashSet<BuildingData>();
        // each building's slides by wall, and the frame each wall was last looked at, where they were keyed by (id, wall) in
        // maps (85 % of the cutaway's time was hashing those); its partitions, which face both ways; and the walls down now
        sealed class WallState { public double[] slide = Array.Empty<double>(); public int[] seen = Array.Empty<int>(); public bool[] partition = Array.Empty<bool>(); public bool sorted; }
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

        /// <summary>A core for a site's walk model; its occluders take their slots from <paramref name="slots"/>, or slots of its own.</summary>
        public OcclusionCore(PlayWorld world, OcclusionSettings settings, OccluderSlots? slots = null)
        {
            World = world; Settings = settings;
            Shared = slots ?? new OccluderSlots(); Rows = Shared.Rows; this.slots = Shared.taken;
        }

        /// <summary>
        /// The buildings in the way now, each with its slot. Out on a terrace with what stands over it in the way, the building
        /// the player is in is one of them, from the terrace's storey up (<see cref="OcclusionView.terraceY"/>).
        /// </summary>
        public IReadOnlyList<Occluder> Active => active;

        /// <summary>
        /// The terrace's own walls while what stands over it is in the way (<see cref="OcclusionView.terraceY"/>), by index in
        /// the active building's LOD0 wall list: its parapet, and the rails round a stair coming up onto it. They stand on the
        /// terrace's floor, as the setback's walls do, but off its outline, and they stay while the setback goes: the caller
        /// marks them -1 in the wall table, which the shaders read as a wall left standing. Empty otherwise.
        /// </summary>
        public IReadOnlyList<int> TerraceWalls => View.active != null && View.terraceY < 1e8 ? terraceWalls : (IReadOnlyList<int>)Array.Empty<int>();

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
            LookAtTerrace(p, cam);
            UpdateOccluders(p, cam, dt);
            ApplyView(p, cam, target);
        }

        /// <summary><see cref="Frame"/>'s second half, after <see cref="FrameView"/>: the cutaway's walls (a Profiler marker of their own in Unity).</summary>
        public void FrameCutaway(double dt) => UpdateCutaway(dt);

        /// <summary>
        /// One frame for a site the player isn't in (docs/PLAY.md §7): the next district over, or a prop or landmark placed as a
        /// site of its own. Only its buildings in the way, by the same three rays, door rule, fade and hold as
        /// <see cref="Frame"/>, the player and the camera given in this site's space. There is no view: none of its
        /// buildings holds the player, so none is clipped or cut away and any can be in the way. <see cref="View"/> stays
        /// neutral, and a wall still down from when the player was in the site slides back up. Its occluders take their slots
        /// from <see cref="Shared"/>, as the core of the site the player is in does.
        /// </summary>
        public void FrameOccluders(PlayerState p, (double x, double y, double z) cam, double dt)
        {
            held = null; leaving = false; terrace = null; terraceInWay = terraceAtDoor = false;
            View = new OcclusionView { clipY = 1e9, terraceY = 1e9 };
            UpdateOccluders(p, cam, dt);
            UpdateCutaway(dt);
        }

        /// <summary>Stop: every occluder and every wall back to normal, the occluders' slots given back.</summary>
        public void Clear()
        {
            foreach (var r in active) { r.a = 0; r.t = 0; if (r.slot >= 0 && ReferenceEquals(slots[r.slot], r)) slots[r.slot] = null; r.slot = -1; }
            active.Clear(); Walls.Clear(); wallStates.Clear(); down.Clear(); hiddenFor = 0; Assist = 0; held = null; leaving = false;
            terrace = null; terraceInWay = terraceAtDoor = false;
            View = new OcclusionView { clipY = 1e9, terraceY = 1e9 };
        }

        // ---- props drawn whole (docs/PROPS.md §2.5) ----

        /// <summary>
        /// Whether a point of building b at site height y is hidden this frame: above the ceiling clip of the building the
        /// player is in, or in a storey of a building in the way that has gone (sunk, sliced off, or faded past half).
        /// What a prop whose shader can't follow the occlusion is switched off by: the whole of it, by one point.
        /// </summary>
        public bool HidesPoint(BuildingData b, double y)
        {
            // the building the player is in: above its ceiling clip, but out on its terrace with what stands over it in the way,
            // from the terrace's floor up as a building in the way, by its row below (a prop on the deck goes with the setback,
            // as the shaders draw one). The row keeps its heights as floats, the deck's a hair over the deck: a point on it is
            // taken a millimetre up, in the storey over it
            if (View.active != null && View.active.id == b.id)
            {
                // just left and being handed over: clipped as it was, and in the way as well, by its row below
                if (View.leaving) { if (y > View.clipY) return true; }
                else
                {
                    if (y < View.terraceY) return y > View.clipY;
                    y = Math.Max(y, View.terraceY + 0.001);
                }
            }
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
                // Dissolve's Stub keeps the storey up to the stub
                double from = Rows[o + 3] + (Settings.mode == OccluderMode.Dissolve && Settings.dissolveLook == DissolveLook.Stub ? Settings.stub : 0);
                return y >= from && Rows[o + 1] >= 0.5;                  // Cutout, Fade, Dissolve: above the base, half gone
            }
            return false;
        }

        // ---- the view ----

        /// <summary>
        /// The building the view is on this frame (<see cref="OcclusionSettings.holdAfterExit"/>): the one the player is in, or
        /// the one they just left while it is still in the way. That one is held as it was (clipped, cut away) while they are
        /// at its door; past it, it is handed over to the buildings in the way (<see cref="OcclusionView.leaving"/>), as one
        /// they never entered is, and let go once it has gone. Out of its back door, with the camera on its far side, it was
        /// held as long as the player walked on, cut away where any other building would have gone.
        /// </summary>
        void HoldView(PlayerState p, (double x, double y, double z) cam)
        {
            leaving = false;
            var loc = World.Locate(p.x, p.y, p.z);
            if (loc != null) { held = loc.Value.b; heldFloor = loc.Value.floor; return; }
            if (held != null && Settings.holdAfterExit)
            {
                var b = World.Site.ById(held.id);
                if (b != null && heldFloor <= b.floors.Count && AnyHits(b, cam, Targets(p, cam)))
                {
                    held = b;
                    if (!Keeps(b, p, cam))
                    {
                        // gone as far as the mode takes it: let go, and drawn as in the way alone, which looks the same
                        if (HandedOver(b)) { held = null; return; }
                        leaving = true;
                    }
                    return;
                }
            }
            held = null;
        }

        /// <summary>
        /// The building just left is held as it is, not yet handed over to the buildings in the way: the player is at one of
        /// its doors (where no building is in the way, <see cref="AtDoorOf"/>), the buildings in the way are left alone (Off:
        /// nothing would take it over), or the mode leaves it standing round the camera (Cutout, Fade) and the camera is still
        /// in it. Sink, Slice and Dissolve take away everything from the player's storey up, so nothing is left to bury the camera in.
        /// </summary>
        bool Keeps(BuildingData b, PlayerState p, (double x, double y, double z) cam)
        {
            var m = Settings.mode;
            if (m == OccluderMode.Off || AtDoorOf(b, p)) return true;
            return (m == OccluderMode.Cutout || m == OccluderMode.Fade) && Contains(b, cam);
        }

        /// <summary>b, in the way, has gone as far as the mode takes it: sunk, sliced, dissolved, or its hole or fade full.</summary>
        bool HandedOver(BuildingData b)
        {
            if (!records.TryGetValue(b.id, out var r) || !InTheWay(b)) return false;
            return Settings.mode == OccluderMode.Sink ? r.t >= r.plan.total - 1e-9 && r.plan.segs.Count > 0 : r.a >= 1;
        }

        /// <summary>The point is in b's volume, or within a wall's thickness of it: one prism per setback tier, and the roof's rise.</summary>
        public bool Contains(BuildingData b, (double x, double y, double z) q)
        {
            int N = b.floors.Count; var shape = ShapeOf(b);
            double lx = q.x - b.pos.x, lz = q.z - b.pos.z; var all = shape.box;
            if (lx < all.x0 - Reach || lx > all.x1 + Reach || lz < all.z0 - Reach || lz > all.z1 + Reach) return false;
            for (int ti = 0; ti < shape.tiers.Count; ti++)
            {
                var (k0, k1) = shape.tiers[ti];
                double y0 = Derived.FloorBase(b, k0), y1 = Derived.FloorBase(b, k1) + (k1 == N ? shape.rise : 0);
                if (q.y < y0 || q.y > y1) continue;
                var fp = Derived.OutlineAt(b, k0);
                if (Geo.Pip(fp, lx, lz) || DistToEdges(fp, lx, lz) < Dim.T_EXT) return true;
            }
            return false;
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
            v.terraceY = 1e9;
            if (loc != null)
            {
                var (b, floor) = loc.Value; int N = b.floors.Count;
                v.active = b; v.floor = floor; v.leaving = leaving;
                v.clipY = floor < N ? Derived.FloorBase(b, floor) + Derived.FloorH(b, floor) - Dim.SLAB - 0.01 : 1e9;
                v.cutOn = Settings.cutaway; v.cutBase = Derived.FloorBase(b, floor); v.cutTop = Derived.FloorBase(b, floor) + (floor < N ? Derived.FloorH(b, floor) : 3.3) + 0.02;
                // out on a terrace the player is outdoors: the building is shown whole, every storey and the roof, and what
                // stands above the terrace, when it is between the camera and the player, is a building in the way like any
                // other, from the terrace's storey up (its row in Active, LookAtTerrace): nothing clipped, no wall cut away, and
                // the storeys under the terrace as they are. Only where it can't be in the way, at a walk-in setback's door or
                // with the buildings in the way left alone, is the view as indoors (its storey's ceiling clipped and walls cut
                // away). Storey's own: the prototype clips
                if (b == terrace)
                {
                    if (InTheWay(b)) { v.clipY = 1e9; v.cutOn = false; v.terraceY = Derived.FloorBase(b, floor); FindTerraceWalls(b, floor); }
                    else if (!terraceInWay) { v.clipY = 1e9; v.cutOn = false; }
                }
            }
            else { v.active = null; v.clipY = 1e9; v.cutOn = false; v.leaving = false; }
            View = v;
        }

        /// <summary>b's occluder record is in the way now (has a slot), going down, down or coming back up.</summary>
        bool InTheWay(BuildingData b) => records.TryGetValue(b.id, out var r) && r.slot >= 0 && ReferenceEquals(slots[r.slot], r);

        /// <summary>
        /// Out on a terrace of the building the view is on (<see cref="OnTerrace"/>): whether what stands above it is between
        /// the camera and the player, by the three rays of the buildings in the way from the terrace's storey up
        /// (<see cref="SegmentHits"/>' <c>from</c>: the storeys under the deck can't hide the player), and whether they are at
        /// the setback's door. A walk-in setback is never in the way at its door, as no building is (<see cref="AtDoorOf"/>):
        /// the player is going in or coming out, and the view is as indoors. A shell's door is shut, so its setback has none.
        /// </summary>
        void LookAtTerrace(PlayerState p, (double x, double y, double z) cam)
        {
            terrace = null; terraceInWay = terraceAtDoor = false;
            if (held == null || !OnTerrace(held, heldFloor, p)) return;
            terrace = held; terraceK = heldFloor;
            terraceInWay = AnyHits(held, cam, Targets(p, cam), heldFloor);
            terraceAtDoor = !Derived.ShellAt(held, heldFloor) && AtDoorOf(held, p);
        }

        /// <summary>
        /// What stands over the terrace the player is out on can be drawn as a building in the way: they are out on b's terrace,
        /// and not at a walk-in setback's door with it in the way, where the view is as indoors instead.
        /// </summary>
        bool OutOn(BuildingData b) => b == terrace && !(terraceAtDoor && terraceInWay);

        /// <summary>
        /// The terrace's own walls on storey k (<see cref="TerraceWalls"/>): the walls of b's LOD0 made on the terrace's storey
        /// whose middle is off the setback's outline, outside it. The setback's own walls, its rooms' and its cores' stand on
        /// or inside its outline, as do the details on its walls (their wall's line); the terrace's parapet runs along the
        /// outline below, where it is the terrace's edge, and a stair's rails come up out on the deck. Found once for a shape
        /// and a storey.
        /// </summary>
        void FindTerraceWalls(BuildingData b, int k)
        {
            var shape = World.ShapeOf(b);
            if (ReferenceEquals(shape, terraceWallsOf) && terraceWallsK == k) return;
            terraceWalls.Clear(); terraceWallsOf = shape; terraceWallsK = k;
            double y = Derived.FloorBase(b, k); var fp = Derived.OutlineAt(b, k);
            for (int i = 0; i < shape.Walls.Count; i++)
            {
                if (shape.WallBase[i] != y) continue;
                var W = shape.Walls[i].W; double nl = Tiers.Hypot(W[2], W[3]);
                if (nl < 1) continue;   // no data: no wall
                // its line runs from (W[0], W[1]) along its normal turned a right angle, 1 + its length being the normal's
                double L = nl - 1, nx = W[2] / nl, nz = W[3] / nl;
                double mx = W[0] + nz * L / 2 - b.pos.x, mz = W[1] - nx * L / 2 - b.pos.z;
                if (!Geo.Pip(fp, mx, mz) && DistToEdges(fp, mx, mz) > TerraceWallClear) terraceWalls.Add(i);
            }
        }

        /// <summary>How far off the setback's outline a wall's middle must be to be the terrace's (metres): a wall on it is its own.</summary>
        const double TerraceWallClear = 0.05;

        /// <summary>
        /// The player is out on a terrace of b: on the storey a setback starts at (<paramref name="floor"/>, as
        /// <see cref="PlayWorld.Locate"/> has it), but past the setback's walls, over the roof of the storeys below. In the
        /// building's plan, so located in it, as the prototype has it, but outdoors (docs/PLAY.md §4).
        /// </summary>
        public static bool OnTerrace(BuildingData b, int floor, PlayerState p)
        {
            if (!Derived.IsSetback(b, floor)) return false;
            double lx = p.x - b.pos.x, lz = p.z - b.pos.z;
            return Geo.Pip(Derived.OutlineAt(b, floor - 1), lx, lz) && !Geo.Pip(Derived.OutlineAt(b, floor), lx, lz);
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

        /// <summary>
        /// How far round past a wall's plane the camera must turn before the walls facing the camera (<see
        /// cref="OcclusionSettings.wallsFacingCamera"/>) start to drop it: the sine of the angle, about 2°. Once it is going
        /// down, turning back past the plane raises it, so a wall seen edge-on doesn't drop and rise again as the camera
        /// wavers about its plane.
        /// </summary>
        public const double FacingMargin = 0.035;

        /// <summary>
        /// The test of the walls facing the camera for one wall: the camera's direction from the player (in plan, of unit
        /// length) is on the wall's outside, by more than <paramref name="margin"/> (the sine of the angle). Its outside is
        /// its normal's side, or with <paramref name="outward"/> false the other (a neighbour's party wall, whose normal
        /// points out of the neighbour, into the building the player is in). A direction, not the camera's place: a camera
        /// trailing the player moves as the player does, and a wall pointing at it would rise and fall as they walked by.
        /// </summary>
        public static bool WallFaces(double[] W, double dirX, double dirZ, bool outward = true, double margin = 0)
        {
            double nl = Tiers.Hypot(W[2], W[3]); if (nl == 0) return false;   // no data: no wall
            double d = (W[2] * dirX + W[3] * dirZ) / nl;
            return (outward ? d : -d) > margin;
        }

        void UpdateCutaway(double dt)
        {
            double step = dt / CutSlide;
            int frame = ++cutFrame;
            var act = View.active; bool facing = Settings.wallsFacingCamera;
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
                        // a wall starts down only with the player clear of its line, and stays down until they cross back;
                        // or, with the walls facing the camera, once the camera has turned onto its outside (never a
                        // partition's: it faces both ways)
                        bool up = WallBlocks(walls[i].W, View.cx, View.cz, View.fx, View.fz, a > 0 ? 0 : Settings.wallMargin)
                            || facing && !st.partition[i] && WallFaces(walls[i].W, View.dirX, View.dirZ, b == act, a > 0 ? 0 : FacingMargin);
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
            st = wallStates[b] = new WallState { slide = new double[n], seen = new int[n], partition = new bool[n], sorted = true };
            foreach (int i in shape.Partitions) if (i < n) st.partition[i] = true;
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
        /// <param name="from">Only the tiers reaching above this storey's floor: from a terrace, what stands above it.</param>
        public bool SegmentHits(BuildingData b, (double x, double y, double z) p, (double x, double y, double z) q, int from = 0)
        {
            int N = b.floors.Count; var shape = ShapeOf(b); double extra = shape.rise;
            // the whole segment clear of every tier's box: no hit, and no tier's heights worked out
            var all = shape.box; double px = p.x - b.pos.x, pz = p.z - b.pos.z, qx = q.x - b.pos.x, qz = q.z - b.pos.z;
            if (Math.Max(px, qx) < all.x0 - Reach || Math.Min(px, qx) > all.x1 + Reach || Math.Max(pz, qz) < all.z0 - Reach || Math.Min(pz, qz) > all.z1 + Reach) return false;
            for (int ti = 0; ti < shape.tiers.Count; ti++)
            {
                var (k0, k1) = shape.tiers[ti];
                if (k1 <= from) continue;
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

        bool AnyHits(BuildingData b, (double x, double y, double z) c, (double x, double y, double z)[] tg, int from = 0)
        {
            foreach (var t in tg) if (SegmentHits(b, c, t, from)) return true;
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
                var site = World.Site; bool context = site.Own < site.Buildings.Count;
                foreach (var b in cand)
                {
                    // a neighbouring district's building, a copy here for the party walls (Site.WithContext): never drawn by this
                    // site, so its own district's core takes it (docs/PLAY.md §7)
                    if (context && site.IndexOf(b) >= site.Own) continue;
                    // the building the view is on is never in the way, but the one just left as it is handed over
                    if ((b == held && !leaving) || AtDoorOf(b, p) || (Hidden != null && Hidden.Count > 0 && Hidden.Contains(b.id))) continue;
                    if (AnyHits(b, c, tg))
                    {
                        var r = Record(b); hits.Add(r);
                        r.sliceY = SliceHeight(b, p); r.top = Derived.RoofY(b) + RoofRise(b) + 0.5;
                        // the storey that stays is held while the building is down (sunk, or partly dissolved)
                        int k = OccStorey(b, p); if (k != r.k && !(r.t > 0) && !(m == OccluderMode.Dissolve && r.a > 0)) r.k = k;
                    }
                }
                // out on a terrace, what stands over it is in the way as any building is (LookAtTerrace), from the terrace's
                // storey up: the storeys under it, the player's floor, stay
                if (terrace != null && terraceInWay && !terraceAtDoor && !(Hidden != null && Hidden.Count > 0 && Hidden.Contains(terrace.id)))
                {
                    var r = Record(terrace); hits.Add(r);
                    r.sliceY = SliceHeight(terrace, p); r.top = Derived.RoofY(terrace) + RoofRise(terrace) + 0.5; r.k = terraceK;
                }
            }
            foreach (var r in hits)
            {
                r.hold = Hold;
                if (!active.Contains(r))
                {
                    int i = Array.IndexOf(slots, null); if (i < 0) continue;
                    slots[i] = r; r.slot = i; r.t = 0; r.k = r.b == terrace ? terraceK : OccStorey(r.b, p); active.Add(r);
                    // stepping out of a setback onto its terrace, the view was as indoors (the storeys over the player's clipped):
                    // what stands over the terrace starts gone, as it ends, where it would have stood whole a moment first
                    if (r.b == terrace && View.active == terrace && View.clipY < 1e8) { r.a = 1; r.t = double.MaxValue; }
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
                // the building the view is on is never in the way, but for what stands over the terrace the player is out on,
                // and the one just left as it is handed over
                if (done || b == null || (b == held && !OutOn(b) && !leaving))
                {
                    r.a = 0; r.t = 0; active.Remove(r);
                    if (r.slot >= 0) { if (ReferenceEquals(slots[r.slot], r)) slots[r.slot] = null; r.slot = -1; }
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
