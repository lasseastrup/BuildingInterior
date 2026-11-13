#nullable enable
using System;
using System.Collections.Generic;
using Triband.Storey.Generate;
using Triband.Storey.Lod;
using Triband.Storey.Occlusion;
using Triband.Storey.Play;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Occlusion in Play mode (docs/PLAY.md, slice 5.3; SPEC §5): each frame, for the player and the camera given, the
    /// floors above the player are clipped, the walls between the camera and the player slide down to a stub, and
    /// buildings in the way sink, slice, open a hole or fade. Camera- and character-agnostic: a project's own controller
    /// sets <see cref="player"/> (or calls <see cref="SetPlayer"/>) and, if its camera trails the player, the point the
    /// camera looks at. The play kit is the reference. Runs the engine-free <see cref="OcclusionCore"/> and writes the
    /// building table and the shader globals; nothing per material, no extra draws but Sink's footprints. The player is in
    /// one site, but a building of any other loaded site can be in the way too: the next district over, or a prop placed as
    /// a site of its own (docs/PLAY.md §7, <see cref="Guests"/>).
    /// </summary>
    [AddComponentMenu("Storey/Storey Occlusion")]
    public sealed class StoreyOcclusion : MonoBehaviour
    {
        [Tooltip("The layout whose buildings are occluded.")]
        public StoreySite? site;
        [Tooltip("The project's occlusion settings; the defaults without one.")]
        public StoreyOcclusionSettings? settings;
        [Tooltip("The camera the player is seen through; the main camera when empty.")]
        public Camera? viewCamera;
        [Tooltip("The player's feet. Or leave empty and call SetPlayer each frame.")]
        public Transform? player;
        [Tooltip("With the city split into districts (several Storey Sites loaded at once), occlude the one the player is in, moving over as they cross into the next. Only sites with District on are followed.")]
        public bool followPlayer = true;

        /// <summary>How far round the player the walk model has its buildings' shapes made ahead, on build threads (metres).</summary>
        const double PrepareRadius = 24;
        /// <summary>How many of Sink's footprint meshes a site keeps; past it, the least recently shown go.</summary>
        const int FootprintsKept = 32;
        /// <summary>
        /// How near the camera's line to the player another site's ground plan must come for its buildings to be looked at
        /// (metres): the segment test reaches a wall's thickness past an outline (docs/PLAY.md §7).
        /// </summary>
        const double GuestReach = 1;

        /// <summary>
        /// A site's walk model and occlusion state, kept while the site shows the same layout: crossing back into a district
        /// finds them there, where they were made again at every crossing (3.9 ms and 1.7 MB for 3,007 buildings;
        /// docs/PERFORMANCE.md §10.2). The walk model is made on a build thread.
        /// </summary>
        sealed class Walked
        {
            /// <summary>The layout the walk model is for.</summary>
            public Site? site;
            public System.Threading.Tasks.Task<PlayWorld>? making;
            public PlayWorld? world; public OcclusionCore? occ;
            // Sink's footprints: a mesh per building, storey, slot (it carries the slot's row) and Dissolve's outline (its thickness in
            // millimetres, negative with a line round every storey; 0 for none), and the frame each was last shown
            public readonly Dictionary<(string, int, int, int), (Mesh mesh, int used)> footprints = new Dictionary<(string, int, int, int), (Mesh, int)>();
            // one object per slot, kept: its mesh, layer and state are set only when they change (docs/PERFORMANCE.md §10.3)
            public readonly GameObject?[] slots = new GameObject?[OcclusionCore.Slots];
            public readonly MeshFilter?[] filters = new MeshFilter?[OcclusionCore.Slots];
            public readonly bool[] shown = new bool[OcclusionCore.Slots];
            // what was written into the site's table: the occluders' rows and the walls down
            public HashSet<int> occluding = new HashSet<int>(), occludingNow = new HashSet<int>();
            public HashSet<int> wallsSet = new HashSet<int>(), wallsNow = new HashSet<int>();
        }

        readonly OcclusionSettings core = new OcclusionSettings();
        readonly Dictionary<StoreySite, Walked> walked = new Dictionary<StoreySite, Walked>();
        Walked? cur;
        Vector3 feet; bool hasFeet;
        Vector3? cameraTarget;
        readonly PlayerState playerState = new PlayerState(), guestPlayer = new PlayerState();
        readonly List<StoreySite> gone = new List<StoreySite>();
        // the other sites whose buildings were looked at this frame, and the frame before's (FrameGuests)
        List<StoreySite> guests = new List<StoreySite>(), guestsWas = new List<StoreySite>();

        /// <summary>The walk model for this layout (where the player is, what they stand on, what stops them), for a controller. Null while it is being made.</summary>
        public PlayWorld? World => cur?.world;
        /// <summary>The occlusion state this frame (the building the player is in, the buildings in the way).</summary>
        public OcclusionCore? Core => cur?.occ;

        /// <summary>
        /// The other sites whose buildings were looked at as buildings in the way this frame (docs/PLAY.md §7): those whose
        /// ground plan comes near the camera's line to the player, and those with a building still going or coming back.
        /// Their states are <see cref="CoreOf"/>'s.
        /// </summary>
        public IReadOnlyList<StoreySite> Guests => guests;

        /// <summary>
        /// A site's occlusion state this frame: <see cref="Core"/> for the site the player is in; for one of
        /// <see cref="Guests"/>, its buildings in the way alone (no view, <see cref="OcclusionCore.FrameOccluders"/>), in
        /// that site's space. Every core's occluders take their slots from the same table. Null for any other site.
        /// </summary>
        public OcclusionCore? CoreOf(StoreySite s)
        {
            if (s == null) return null;
            if (s == site) return Core;
            return guests.Contains(s) && walked.TryGetValue(s, out var w) ? w.occ : null;
        }
        /// <summary>The camera assist's extra pitch this frame (radians), for a camera that honours it.</summary>
        public float Assist => cur?.occ != null ? (float)cur.occ.Assist : 0;

        /// <summary>Where the player's feet are this frame (world space), when <see cref="player"/> is not set.</summary>
        public void SetPlayer(Vector3 worldFeet) { feet = worldFeet; hasFeet = true; }

        /// <summary>
        /// The point a trailing camera looks at (world space). The cutaway then tests with the camera's offset from it
        /// applied to the player's exact position, so a wall seen edge-on does not drop for a frame as the player
        /// crosses its line. Null when the camera looks straight at the player.
        /// </summary>
        public void SetCameraTarget(Vector3? worldTarget) => cameraTarget = worldTarget;

        /// <summary>The player's feet and the camera this frame (world space), as the occlusion takes them; false without a camera.</summary>
        internal bool PlayerAndCamera(out Vector3 playerFeet, out Vector3 camera)
        {
            var cam = viewCamera != null ? viewCamera : Camera.main;
            playerFeet = camera = default;
            if (cam == null) return false;
            camera = cam.transform.position;
            playerFeet = player != null ? player.position : hasFeet ? feet : camera;
            return true;
        }

        void OnEnable() { if (site != null) site.Occlusion = this; }

        readonly List<StoreySite> candidates = new List<StoreySite>();
        readonly List<Lod.Districts.Area> areas = new List<Lod.Districts.Area>();

        // before the sites draw (they run late): the district the player is in takes the occlusion, and the other districts'
        // walk models are made ahead on build threads, so crossing into one finds it ready
        void Update()
        {
            Forget();
            if (!followPlayer || StoreySite.All.Count < 2) return;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            Vector3? at = player != null ? player.position : hasFeet ? feet : cam != null ? cam.transform.position : (Vector3?)null;
            if (at == null) return;
            candidates.Clear(); areas.Clear();
            // districts only: a site placed as a prop is a few metres across, and the player near one past every district's
            // edge took the occlusion into it, the districts' buildings no longer in the way (docs/PLAY.md §7)
            foreach (var s in StoreySite.AllSet) if (s.isActiveAndEnabled && s.district && s.PlanArea is Lod.Districts.Area a) { candidates.Add(s); areas.Add(a); }
            int pick = Lod.Districts.PlayerIn(areas, site != null ? candidates.IndexOf(site) : -1, at.Value.x, at.Value.z);
            if (pick >= 0 && candidates[pick] != site) Attach(candidates[pick]);
            foreach (var s in candidates) if (s != site && s.Renderer is SiteRenderer r && r.Site is Site laid) Ready(StateOf(s), r, laid, BuildLane.Background);
        }

        /// <summary>Drive this site (for a component added from code, whose OnEnable ran before the site was set).</summary>
        public void Attach(StoreySite s)
        {
            if (site != null && site != s)
            {
                if (site.Occlusion == this) site.Occlusion = null;
                if (walked.TryGetValue(site, out var left)) Leave(site, left);
            }
            site = s; cur = null;
            if (isActiveAndEnabled) s.Occlusion = this;
        }

        void OnDisable()
        {
            if (site != null && site.Occlusion == this) site.Occlusion = null;
            foreach (var s in guests) if (s != null && s.GuestOf == this) s.GuestOf = null;
            guests.Clear(); guestsWas.Clear();
            foreach (var kv in walked) { if (kv.Key != null) Leave(kv.Key, kv.Value); Drop(kv.Value); }
            walked.Clear(); cur = null;
        }

        Walked StateOf(StoreySite s)
        {
            if (!walked.TryGetValue(s, out var w)) walked[s] = w = new Walked();
            return w;
        }

        /// <summary>The states of sites destroyed since: their meshes go too (their objects went with the site).</summary>
        void Forget()
        {
            gone.Clear();
            foreach (var kv in walked) if (kv.Key == null) gone.Add(kv.Key!);
            foreach (var s in gone) { Drop(walked[s]); walked.Remove(s); if (cur != null && !walked.ContainsValue(cur)) cur = null; }
        }

        /// <summary>
        /// The site's walk model is ready for the layout it shows: one for a new layout (shown, edited) is made on a build
        /// thread, in <paramref name="lane"/>, and occlusion starts afresh; false until it is made.
        /// </summary>
        bool Ready(Walked w, SiteRenderer r, Site s, BuildLane lane)
        {
            if (!ReferenceEquals(w.site, s))
            {
                // the old layout's buildings in the way let go: their slots are every site's, and a row left naming one would
                // draw this site's building there with another's occluder
                foreach (int i in w.occluding) r.Table.SetOccluder(i, -1);
                w.occ?.Clear();
                w.site = s; w.world = null; w.occ = null; w.occluding.Clear(); w.wallsSet.Clear();
                ClearFootprints(w);
                w.making = MakeWorld(s, lane);
            }
            if (w.world != null) return true;
            if (w.making == null) return false;
            if (!w.making.IsCompleted)
            {
                // wanted now (it is the site the player is in): moved ahead of what waits
                if (lane == BuildLane.Forced) BuildThreads.Shared.Promote(w.making, lane);
                return false;
            }
            // a build thread's failure: made here instead, so a fault shows as it did when it was made here
            w.world = w.making.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? w.making.Result : new PlayWorld(s);
            w.making = null;
            // each building's shape from the renderer's LOD0 when it has one (built on a worker as the camera nears, long
            // before the player is close enough to touch), or made ahead round the player (Prepare)
            w.world.Source = ShapesFrom(r, s);
            // a hidden building is never in the way (docs/PLAY.md §6); the slots are the table's, shared with every other site's core
            w.occ = new OcclusionCore(w.world, core, r.Table.OccluderSlots) { Hidden = r.Hidden };
            return true;
        }

        // in methods of their own: lambdas capturing Ready's parameters had their closure made as Ready began, on every
        // call, the walk model made or not (40 bytes a frame for every district loaded: Update asks after the others', Apply the player's)
        static System.Threading.Tasks.Task<PlayWorld> MakeWorld(Site s, BuildLane lane) => BuildThreads.Shared.Run(() => new PlayWorld(s), lane);
        static Func<BuildingData, Lod0Shape?> ShapesFrom(SiteRenderer r, Site shown) => b => ReferenceEquals(r.Site, shown) ? r.ShapeOf(b.id) : null;

        /// <summary>Called by the site's renderer each frame, after the cameras have moved, in place of its own view globals.</summary>
        internal void Apply(SiteRenderer r)
        {
            if (settings != null) settings.ApplyTo(core);
            var cam = viewCamera != null ? viewCamera : Camera.main;
            var s = r.Site;
            if (s == null || cam == null || site == null) { Neutral(r); return; }
            var w = StateOf(site);
            if (!Ready(w, r, s, BuildLane.Forced)) { cur = null; Neutral(r); return; }   // the walk model being made: a frame or two after a load
            cur = w; var occ = w.occ!;
            var origin = site.transform.position;   // the layout is in the site's space: unrotated, unscaled (the inspector says so)
            Vector3 pw = player != null ? player.position : hasFeet ? feet : cam.transform.position;
            var p = playerState; p.Spawn(pw.x - origin.x, pw.y - origin.y, pw.z - origin.z);
            var cw = cam.transform.position; var tw = cameraTarget ?? pw + Vector3.up * (float)FollowCamera.Chest;
            using (StoreyTimings.Time("occlusion: frame"))
            {
                using (StoreyTimings.Time("occlusion: view and buildings in the way")) occ.FrameView(p, (cw.x - origin.x, cw.y - origin.y, cw.z - origin.z), (tw.x - origin.x, tw.y - origin.y, tw.z - origin.z), Time.deltaTime);
                using (StoreyTimings.Time("occlusion: cutaway")) occ.FrameCutaway(Time.deltaTime);
            }
            using (StoreyTimings.Time("occlusion: table and globals")) Write(r, w, occ, origin, pw);
            using (StoreyTimings.Time("occlusion: other sites' buildings in the way")) FrameGuests(pw, cw, Time.deltaTime);
            // the shapes the walk and the cutaway will want next: round the player, and the neighbours of the building they are in
            using (StoreyTimings.Time("occlusion: shapes asked ahead"))
            {
                var world = w.world!;
                world.Prepare(p.x, p.z, PrepareRadius, BuildThreads.Shared);
                if (occ.View.active != null) world.Prepare(world.Touching(occ.View.active), BuildThreads.Shared);
            }
        }

        void Neutral(SiteRenderer r)
        {
            StoreyGlobals.SetActive(-1, 1e9f);
            StoreyGlobals.SetCut(false, 1, 0, 0, 1e9f);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector3.zero, 2.4f);
        }

        /// <summary>
        /// The occlusion leaves a site (to another district, off, or a site no longer round the player): what it wrote into the
        /// site's table is undone, its occluders' slots given back, its footprints hidden.
        /// </summary>
        void Leave(StoreySite s, Walked w)
        {
            var r = s.Renderer;
            if (r != null && ReferenceEquals(r.Site, w.site))
            {
                var t = r.Table;
                foreach (int i in w.occluding) t.SetOccluder(i, -1);
                foreach (int id in w.wallsSet) t.SetWall(id, 0);
            }
            w.occluding.Clear(); w.wallsSet.Clear();
            w.occ?.Clear();
            for (int i = 0; i < w.slots.Length; i++) { w.shown[i] = false; var go = w.slots[i]; if (go != null && go.activeSelf) go.SetActive(false); }
        }

        /// <summary>A state let go: its occluders' slots given back, its footprints' meshes and objects destroyed.</summary>
        void Drop(Walked w)
        {
            w.occ?.Clear();
            ClearFootprints(w);
            for (int i = 0; i < w.slots.Length; i++) { if (w.slots[i] != null) Destroy(w.slots[i]); w.slots[i] = null; w.filters[i] = null; }
        }

        void ClearFootprints(Walked w)
        {
            foreach (var f in w.footprints.Values) Destroy(f.mesh);
            w.footprints.Clear();
            for (int i = 0; i < w.filters.Length; i++) { var mf = w.filters[i]; if (mf != null) mf.sharedMesh = null; }
        }

        void Write(SiteRenderer r, Walked w, OcclusionCore o, Vector3 origin, Vector3 playerWorld)
        {
            var table = r.Table; var v = o.View; float lift = origin.y;
            // the view: heights in world space, the cutaway's x and z in the site's (the wall data in the vertices is)
            int active = v.active != null ? r.TableIndexOf(v.active.id) : -1;
            // out on a terrace with what stands over it in the way, the active building is drawn as in the way above its floor;
            // one just left, as it is handed over to the buildings in the way, is drawn as in the way as well as cut away
            StoreyGlobals.SetActive(active, active >= 0 ? (float)v.clipY + lift : 1e9f, v.terraceY < 1e8 ? (float)v.terraceY + lift : (float?)null, v.leaving);
            if (active >= 0 && v.cutOn)
            {
                StoreyGlobals.SetCamera(new Vector3((float)v.cx, (float)v.cy + lift, (float)v.cz), new Vector3((float)v.fx, (float)v.fy + lift, (float)v.fz), new Vector2((float)v.dirX, (float)v.dirZ));
                StoreyGlobals.SetCut(true, (float)v.stub, (float)v.cutBase + lift, (float)v.cutTop + lift, (float)v.clipY + lift);
            }
            else StoreyGlobals.SetCut(false, 1, 0, 0, active >= 0 ? (float)v.clipY + lift : 1e9f);

            // walls: every slide into the table, the ones that came back up to zero (only the chunks where a slide changed
            // are uploaded: a wall held down isn't written again)
            var wallsNow = w.wallsNow; wallsNow.Clear();
            foreach (var kv in o.Walls)
            {
                int wb = r.WallBaseOf(kv.Key.id); if (wb < 0) continue;
                int id = wb + kv.Key.wall; table.SetWall(id, (float)kv.Value); wallsNow.Add(id);
            }
            // a terrace's own walls (its parapet) stand while what stands over it is in the way: -1, which the shaders leave be
            var tw = o.TerraceWalls;
            if (tw.Count > 0 && v.active != null)
            {
                int wb = r.WallBaseOf(v.active.id);
                if (wb >= 0) for (int i = 0; i < tw.Count; i++) { int id = wb + tw[i]; table.SetWall(id, -1); wallsNow.Add(id); }
            }
            foreach (int id in w.wallsSet) if (!wallsNow.Contains(id)) table.SetWall(id, 0);
            (w.wallsSet, w.wallsNow) = (wallsNow, w.wallsSet);

            WriteOccluders(r, site!, w, o, lift);
            var modeG = core.mode switch
            {
                OccluderMode.Off => StoreyGlobals.OcclusionMode.Off,
                OccluderMode.Cutout => StoreyGlobals.OcclusionMode.Cutout,
                OccluderMode.Fade => StoreyGlobals.OcclusionMode.Fade,
                OccluderMode.Dissolve => StoreyGlobals.OcclusionMode.Dissolve,
                _ => StoreyGlobals.OcclusionMode.SinkOrSlice,
            };
            StoreyGlobals.SetOcclusion(modeG, playerWorld + Vector3.up * 1.1f, (float)core.holeRadius);
            StoreyGlobals.SetDissolve(core.dissolveLook, (float)core.dissolveKeeps, (float)core.hatchSpacing, (float)core.stub);
            if (settings != null) StoreyGlobals.SetCap(settings.cutCapColour);
        }

        /// <summary>
        /// A core's buildings in the way into the table, through the renderer of the site they belong to: the slot in each
        /// one's state row, the rows with world heights (<paramref name="lift"/>, the site's height), and the footprints.
        /// </summary>
        void WriteOccluders(SiteRenderer r, StoreySite s, Walked w, OcclusionCore o, float lift)
        {
            var table = r.Table;
            // written where they changed (docs/PERFORMANCE.md §10.4): every occluder's slot was cleared and set again, and its
            // 64 texels marked for upload, every frame, about 1 KB an occluder a frame and its building looked at again by the
            // steady pass
            var occNow = w.occludingNow; occNow.Clear();
            Array.Clear(w.shown, 0, w.shown.Length);
            for (int ai = 0; ai < o.Active.Count; ai++)
            {
                var oc = o.Active[ai];
                if (oc.slot < 0) continue;
                int idx = r.TableIndexOf(oc.b.id); if (idx < 0) continue;
                table.SetOccluder(idx, oc.slot); occNow.Add(idx);
                int at = oc.slot * OcclusionCore.RowTexels, n = (int)o.Rows[at * 4];
                bool changed = Put(table.Occ, at, new Vector4(o.Rows[at * 4], o.Rows[at * 4 + 1], o.Rows[at * 4 + 2] + lift, o.Rows[at * 4 + 3] + lift));
                for (int i = 1; i < OcclusionCore.RowTexels; i++)
                {
                    int q = (at + i) * 4;
                    changed |= Put(table.Occ, at + i, new Vector4(o.Rows[q] + (i <= n ? lift : 0), o.Rows[q + 1], o.Rows[q + 2], o.Rows[q + 3]));
                }
                if (changed) table.MarkOccDirty(oc.slot);
                // the footprint stands in once the building starts to go: Sink's at once, Dissolve's fading in as it fades out.
                // None for a building hidden meanwhile (docs/PLAY.md §6): nothing of it is drawn while it comes back up
                // Dissolve's Stub has none: its storey stays
                if (oc.k < oc.b.floors.Count && core.Footprint && (core.mode == OccluderMode.Sink ? oc.t > 0 : oc.a > 0) && !r.IsHidden(oc.b.id))
                {
                    ShowFootprint(r, s, w, oc.b, oc.k, oc.slot);
                    table.SetLod(BuildingTable.FootprintRow(oc.slot), 0, 0, core.mode == OccluderMode.Dissolve ? (float)oc.a : 1);
                }
            }
            foreach (int i in w.occluding) if (!occNow.Contains(i)) table.SetOccluder(i, -1);   // out of the way now
            (w.occluding, w.occludingNow) = (occNow, w.occluding);
            for (int i = 0; i < w.slots.Length; i++) { var go = w.slots[i]; if (!w.shown[i] && go != null && go.activeSelf) go.SetActive(false); }
        }

        /// <summary>
        /// The other loaded sites' buildings in the way (docs/PLAY.md §7). Each site whose ground plan comes near the camera's
        /// line to the player, or which has a building still going or coming back, runs its own core with the player and the
        /// camera in its space (<see cref="OcclusionCore.FrameOccluders"/>), and its occluders are written through its own
        /// renderer. A site no longer round the player has what it wrote undone and its slots given back.
        /// </summary>
        void FrameGuests(Vector3 feetWorld, Vector3 camWorld, float dt)
        {
            (guests, guestsWas) = (guestsWas, guests); guests.Clear();
            foreach (var s in StoreySite.AllSet)
            {
                if (s == site || !s.isActiveAndEnabled || s.Renderer is not SiteRenderer gr || gr.Site is not Site laid) continue;
                walked.TryGetValue(s, out var gw);
                bool busy = gw?.occ != null && gw.occ.Active.Count > 0;
                if (!busy && !(s.PlanArea is Lod.Districts.Area a && a.Distance(camWorld.x, camWorld.z, feetWorld.x, feetWorld.z) <= GuestReach)) continue;
                gw ??= StateOf(s);
                if (!Ready(gw, gr, laid, BuildLane.Forced)) continue;   // its walk model being made: a frame or two
                guests.Add(s); s.GuestOf = this;
                var o = s.transform.position; var oc = gw.occ!;
                var p = guestPlayer; p.Spawn(feetWorld.x - o.x, feetWorld.y - o.y, feetWorld.z - o.z);
                oc.FrameOccluders(p, (camWorld.x - o.x, camWorld.y - o.y, camWorld.z - o.z), dt);
                WriteOccluders(gr, s, gw, oc, o.y);
            }
            foreach (var s in guestsWas)
            {
                if (s == null || guests.Contains(s)) continue;
                if (s != site && walked.TryGetValue(s, out var gw)) Leave(s, gw);
                if (s.GuestOf == this) s.GuestOf = null;
            }
        }

        /// <summary>A texel of the occluder rows written: true when it differs (exactly: an approximate test could let small steps drift unsent).</summary>
        static bool Put(Vector4[] occ, int i, Vector4 v)
        {
            var o = occ[i];
            if (o.x == v.x && o.y == v.y && o.z == v.z && o.w == v.w) return false;
            occ[i] = v; return true;
        }

        // a footprint's mesh carries its row, so it is kept per slot: drawn through the slot's row, always at LOD0, never
        // occluded, and faded with the building in Dissolve. Each slot keeps its own object, given a mesh only when it changes,
        // under the site the building is in (s)
        void ShowFootprint(SiteRenderer r, StoreySite s, Walked w, BuildingData b, int k, int slot)
        {
            double cage = core.CageWidth; int mm = (int)Math.Round(cage * 1000);
            var key = (b.id, k, slot, core.outlineStoreys ? -mm : mm);
            if (!w.footprints.TryGetValue(key, out var f))
            {
                using (StoreyTimings.Time("occlusion: footprint", b.name))
                {
                    var gb = SinkFootprint.Build(r.Site!, b, k, cage, core.outlineStoreys);
                    f = (r.UploadExtra(gb, b.name + " footprint " + k, BuildingTable.FootprintRow(slot)), 0);
                    gb.Recycle();   // uploaded: its lists go back for the next build
                }
                Trim(w);
            }
            w.footprints[key] = (f.mesh, Time.frameCount);
            var go = w.slots[slot];
            if (go == null) { go = NewFootprint(s); w.slots[slot] = go; w.filters[slot] = go.GetComponent<MeshFilter>(); }
            var mf = w.filters[slot]!;
            if (mf.sharedMesh != f.mesh) mf.sharedMesh = f.mesh;
            int layer = s.gameObject.layer;   // the site's layer, like its buildings
            if (go.layer != layer) go.layer = layer;
            if (!go.activeSelf) go.SetActive(true);
            w.shown[slot] = true;
        }

        /// <summary>Past <see cref="FootprintsKept"/>, the footprint meshes shown longest ago go (none shown this frame).</summary>
        void Trim(Walked w)
        {
            while (w.footprints.Count >= FootprintsKept)
            {
                (string, int, int, int)? oldest = null; int at = int.MaxValue;
                foreach (var kv in w.footprints) if (kv.Value.used < at && kv.Value.used != Time.frameCount) { at = kv.Value.used; oldest = kv.Key; }
                if (oldest == null) return;
                var m = w.footprints[oldest.Value].mesh;
                for (int i = 0; i < w.filters.Length; i++) { var mf = w.filters[i]; if (mf != null && mf.sharedMesh == m) mf.sharedMesh = null; }
                Destroy(m); w.footprints.Remove(oldest.Value);
            }
        }

        GameObject NewFootprint(StoreySite s)
        {
            var go = new GameObject("Storey footprint") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(s.transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s.opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // the building it stands for still casts its whole shadow
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            go.SetActive(false);
            return go;
        }
    }
}
