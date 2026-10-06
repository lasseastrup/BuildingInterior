#nullable enable
using System.Collections.Generic;
using Triband.Storey.Generate;
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
    /// building table and the shader globals; nothing per material, no extra draws but Sink's footprints.
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
        [Tooltip("With the city split into districts (several Storey Sites loaded at once), occlude the one the player is in, moving over as they cross into the next.")]
        public bool followPlayer = true;

        readonly OcclusionSettings core = new OcclusionSettings();
        PlayWorld? world; OcclusionCore? occ; Site? builtFor;
        Vector3 feet; bool hasFeet;
        Vector3? cameraTarget;
        readonly Dictionary<(string, int, int), Mesh> footprints = new Dictionary<(string, int, int), Mesh>();
        readonly List<GameObject> shownFootprints = new List<GameObject>();
        readonly Stack<GameObject> spareFootprints = new Stack<GameObject>();
        readonly HashSet<int> occluding = new HashSet<int>();
        readonly HashSet<int> wallsSet = new HashSet<int>();

        /// <summary>The walk model for this layout (where the player is, what they stand on, what stops them), for a controller.</summary>
        public PlayWorld? World => world;
        /// <summary>The occlusion state this frame (the building the player is in, the buildings in the way).</summary>
        public OcclusionCore? Core => occ;
        /// <summary>The camera assist's extra pitch this frame (radians), for a camera that honours it.</summary>
        public float Assist => occ != null ? (float)occ.Assist : 0;

        /// <summary>Where the player's feet are this frame (world space), when <see cref="player"/> is not set.</summary>
        public void SetPlayer(Vector3 worldFeet) { feet = worldFeet; hasFeet = true; }

        /// <summary>
        /// The point a trailing camera looks at (world space). The cutaway then tests with the camera's offset from it
        /// applied to the player's exact position, so a wall seen edge-on does not drop for a frame as the player
        /// crosses its line. Null when the camera looks straight at the player.
        /// </summary>
        public void SetCameraTarget(Vector3? worldTarget) => cameraTarget = worldTarget;

        void OnEnable() { if (site != null) site.Occlusion = this; }

        readonly List<StoreySite> candidates = new List<StoreySite>();
        readonly List<Lod.Districts.Area> areas = new List<Lod.Districts.Area>();

        // before the sites draw (they run late): the district the player is in takes the occlusion
        void Update()
        {
            if (!followPlayer || StoreySite.All.Count < 2) return;
            var cam = viewCamera != null ? viewCamera : Camera.main;
            Vector3? at = player != null ? player.position : hasFeet ? feet : cam != null ? cam.transform.position : (Vector3?)null;
            if (at == null) return;
            candidates.Clear(); areas.Clear();
            foreach (var s in StoreySite.All) if (s.isActiveAndEnabled && s.PlanArea is Lod.Districts.Area a) { candidates.Add(s); areas.Add(a); }
            int pick = Lod.Districts.PlayerIn(areas, site != null ? candidates.IndexOf(site) : -1, at.Value.x, at.Value.z);
            if (pick >= 0 && candidates[pick] != site) Attach(candidates[pick]);
        }

        /// <summary>Drive this site (for a component added from code, whose OnEnable ran before the site was set).</summary>
        public void Attach(StoreySite s)
        {
            if (site != null && site != s && site.Occlusion == this) site.Occlusion = null;
            site = s; builtFor = null;
            if (isActiveAndEnabled) s.Occlusion = this;
        }

        void OnDisable()
        {
            if (site != null && site.Occlusion == this) site.Occlusion = null;
            occ?.Clear(); HideFootprints();
            foreach (var m in footprints.Values) Destroy(m);
            footprints.Clear();
            foreach (var go in spareFootprints) Destroy(go);
            spareFootprints.Clear();
        }

        /// <summary>Called by the site's renderer each frame, after the cameras have moved, in place of its own view globals.</summary>
        internal void Apply(SiteRenderer r)
        {
            if (settings != null) settings.ApplyTo(core);
            var cam = viewCamera != null ? viewCamera : Camera.main;
            var s = r.Site;
            if (s == null || cam == null || site == null) { Neutral(r); return; }
            if (s != builtFor)
            {
                // the layout changed: a new walk model on the renderer's own LOD0s, and occlusion starts afresh
                builtFor = s; world = new PlayWorld(s);
                foreach (var b in s.Buildings) { var l0 = r.Lod0Of(b.id); if (l0 != null) world.UseLod0(b, l0); }
                occ = new OcclusionCore(world, core);
                foreach (var m in footprints.Values) Destroy(m);
                footprints.Clear();
            }
            var origin = site.transform.position;   // the layout is in the site's space: unrotated, unscaled (the inspector says so)
            Vector3 pw = player != null ? player.position : hasFeet ? feet : cam.transform.position;
            var p = new PlayerState(); p.Spawn(pw.x - origin.x, pw.y - origin.y, pw.z - origin.z);
            var cw = cam.transform.position; var tw = cameraTarget ?? pw + Vector3.up * (float)FollowCamera.Chest;
            occ!.Frame(p, (cw.x - origin.x, cw.y - origin.y, cw.z - origin.z), (tw.x - origin.x, tw.y - origin.y, tw.z - origin.z), Time.deltaTime);
            Write(r, occ, origin, pw);
        }

        void Neutral(SiteRenderer r)
        {
            StoreyGlobals.SetActive(-1, 1e9f);
            StoreyGlobals.SetCut(false, 1, 0, 0, 1e9f);
            StoreyGlobals.SetOcclusion(StoreyGlobals.OcclusionMode.Off, Vector3.zero, 2.4f);
        }

        void Write(SiteRenderer r, OcclusionCore o, Vector3 origin, Vector3 playerWorld)
        {
            var table = r.Table; var v = o.View; float lift = origin.y;
            // the view: heights in world space, the cutaway's x and z in the site's (the wall data in the vertices is)
            int active = v.active != null ? r.TableIndexOf(v.active.id) : -1;
            StoreyGlobals.SetActive(active, active >= 0 ? (float)v.clipY + lift : 1e9f);
            if (active >= 0 && v.cutOn)
            {
                StoreyGlobals.SetCamera(new Vector3((float)v.cx, (float)v.cy + lift, (float)v.cz), new Vector3((float)v.fx, (float)v.fy + lift, (float)v.fz), new Vector2((float)v.dirX, (float)v.dirZ));
                StoreyGlobals.SetCut(true, (float)v.stub, (float)v.cutBase + lift, (float)v.cutTop + lift, (float)v.clipY + lift);
            }
            else StoreyGlobals.SetCut(false, 1, 0, 0, active >= 0 ? (float)v.clipY + lift : 1e9f);

            // walls: every slide into the table, the ones that came back up to zero
            foreach (int id in wallsSet) table.Wall[id] = 0;
            wallsSet.Clear();
            foreach (var kv in o.Walls)
            {
                int wb = r.WallBaseOf(kv.Key.id); if (wb < 0) continue;
                int id = wb + kv.Key.wall; table.Wall[id] = (float)kv.Value; wallsSet.Add(id);
            }
            table.MarkWallDirty();

            // buildings in the way: the slot in each one's state row, the rows with world heights
            foreach (int i in occluding) table.SetOccluder(i, -1);
            occluding.Clear();
            HideFootprints();
            foreach (var oc in o.Active)
            {
                if (oc.slot < 0) continue;
                int idx = r.TableIndexOf(oc.b.id); if (idx < 0) continue;
                table.SetOccluder(idx, oc.slot); occluding.Add(idx);
                int at = oc.slot * OcclusionCore.RowTexels, n = (int)o.Rows[at * 4];
                var hd = new Vector4(o.Rows[at * 4], o.Rows[at * 4 + 1], o.Rows[at * 4 + 2] + lift, o.Rows[at * 4 + 3] + lift);
                table.Occ[at] = hd;
                for (int i = 1; i < OcclusionCore.RowTexels; i++)
                {
                    int q = (at + i) * 4;
                    table.Occ[at + i] = new Vector4(o.Rows[q] + (i <= n ? lift : 0), o.Rows[q + 1], o.Rows[q + 2], o.Rows[q + 3]);
                }
                // the footprint stands in once the building starts to go: Sink's at once, Dissolve's fading in as it fades out
                if (oc.k < oc.b.floors.Count && (core.mode == OccluderMode.Sink ? oc.t > 0 : core.mode == OccluderMode.Dissolve && oc.a > 0))
                {
                    ShowFootprint(r, oc.b, oc.k, oc.slot);
                    table.SetLod(BuildingTable.FootprintRow(oc.slot), 0, 0, core.mode == OccluderMode.Dissolve ? (float)oc.a : 1);
                }
            }
            table.MarkOccDirty();
            var modeG = core.mode switch
            {
                OccluderMode.Off => StoreyGlobals.OcclusionMode.Off,
                OccluderMode.Cutout => StoreyGlobals.OcclusionMode.Cutout,
                OccluderMode.Fade => StoreyGlobals.OcclusionMode.Fade,
                OccluderMode.Dissolve => StoreyGlobals.OcclusionMode.Dissolve,
                _ => StoreyGlobals.OcclusionMode.SinkOrSlice,
            };
            StoreyGlobals.SetOcclusion(modeG, playerWorld + Vector3.up * 1.1f, (float)core.holeRadius);
        }

        // a footprint's mesh carries its row, so it is kept per slot: drawn through the slot's row, always at LOD0, never
        // occluded, and faded with the building in Dissolve
        void ShowFootprint(SiteRenderer r, BuildingData b, int k, int slot)
        {
            if (!footprints.TryGetValue((b.id, k, slot), out var mesh))
                footprints[(b.id, k, slot)] = mesh = r.UploadExtra(SinkFootprint.Build(r.Site!, b, k), b.name + " footprint " + k, BuildingTable.FootprintRow(slot));
            var go = spareFootprints.Count > 0 ? spareFootprints.Pop() : NewFootprint();
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.layer = site!.gameObject.layer;   // the site's layer, like its buildings
            go.SetActive(true);
            shownFootprints.Add(go);
        }

        GameObject NewFootprint()
        {
            var go = new GameObject("Storey footprint") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(site!.transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = site.opaque;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // the building it stands for still casts its whole shadow
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            return go;
        }

        void HideFootprints()
        {
            foreach (var go in shownFootprints) { if (go == null) continue; go.SetActive(false); spareFootprints.Push(go); }
            shownFootprints.Clear();
        }
    }
}
