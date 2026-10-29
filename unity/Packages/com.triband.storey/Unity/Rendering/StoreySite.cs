#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// A layout in the scene (docs/EDITOR.md §2): draws every building of a <c>.storey</c> asset, in edit mode as in
    /// play. While the layout is being edited the editor shows its unsaved state through <see cref="Preview"/>, and only
    /// the buildings an edit touched are built again. The meshes are never saved with the scene.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(1000)]   // after the cameras and characters have moved this frame: occlusion reads them
    [AddComponentMenu("Storey/Storey Site")]
    public sealed class StoreySite : MonoBehaviour
    {
        [Tooltip("The layout: a .storey file.")]
        public StoreyDocumentAsset? layout;
        [Tooltip("Storey/Opaque, or a project variant of it")] public Material? opaque;
        [Tooltip("Storey/Glass, or a project variant of it")] public Material? glass;
        [Tooltip("Storey/Massing, or a project variant of it")] public Material? massing;
        [Tooltip("Automatic: each building's detail goes by how close the camera is, and far buildings are merged into cells (docs/CITY.md). Fixed: every building shows the same LOD.")]
        public LodMode lodMode = LodMode.Automatic;
        [Tooltip("Fixed LOD only: which LOD every building shows: 0 full, 1 shell, 2 massing.")]
        [Range(0, 2)] public int displayedLod = 0;
        [Tooltip("Automatic LOD: the camera detail is picked for in Play mode; the main camera when empty. In the editor it's the Scene view's.")]
        public Camera? lodCamera;
        [Tooltip("Detail distances, residency and the build budget; the defaults (the primary tier's) when empty.")]
        public StoreyQualitySettings? quality;
        [Tooltip("Tint each building by the level of detail it shows.")]
        public bool lodTint;
        [Tooltip("Automatic LOD: show its numbers (LOD counts, resident meshes, cells, build time) in the Game view in Play mode, and in the Scene view while the site is selected.")]
        public bool showLodStats;
        [Tooltip("Give every building a MeshCollider from a collision mesh of its floors, stairs, walls and roofs, for physics characters and raycasts; a building whose Colliders are off on its card gets none. Storey's own play kit walks without them.")]
        public bool generateColliders = true;
        [Tooltip("Colliders in edit mode too, rebuilt with every edit (raycasts and physics previews in the editor). Off, edits cook nothing.")]
        public bool collidersInEditMode;
        [Tooltip("In Play mode, make every building's collider as the site loads, before physics first steps, and keep them all, instead of each with its building's LOD0 as the camera nears: nothing placed on a building falls through it at the start, a prop or not. The layout then loads at once, not over frames. About a millisecond a building on one desktop core, shared by the build threads, plus PhysX's cooking, and every collider kept (2 to 12 KB of mesh a building, besides PhysX's copy): for a level's layout, not a streamed city. Off, Storey Props wait for their building's collider.")]
        public bool collidersAtLoad;
        [Tooltip("The artist-made windows and doors the layout's styles use (the editor keeps this list), so builds include them.")]
        public List<StoreyOpening> openings = new List<StoreyOpening>();
        [Tooltip("The artist-made facade details the layout uses, placed by hand or by its styles' rules (the editor keeps this list), so builds include them.")]
        public List<StoreyDetail> facadeDetails = new List<StoreyDetail>();
        [Tooltip("Districts next to this one (their layouts, and how far their sites are from this one): buildings back to back across the edge share one party wall. The inspector's Find neighbouring districts fills it.")]
        public List<NeighbourDistrict> neighbours = new List<NeighbourDistrict>();

        /// <summary>A district next to this one: its layout, and its site's position less this site's (x, z).</summary>
        [Serializable]
        public sealed class NeighbourDistrict
        {
            public StoreyDocumentAsset? layout;
            public Vector2 offset;
        }

        // the layout and neighbours last built with, compared by reference each frame (no strings: they were garbage every frame)
        StoreyDocumentAsset? neighboursFor;
        readonly List<(StoreyDocumentAsset? layout, Vector2 offset, string? text)> neighboursSeen = new List<(StoreyDocumentAsset?, Vector2, string?)>();
        // the occlusion's per-frame call, made once (a method group made a new delegate every frame)
        Action<SiteRenderer>? occApply; StoreyOcclusion? occApplyFor;

        /// <summary>How a site picks each building's LOD.</summary>
        public enum LodMode { Automatic, Fixed }

        SiteRenderer? site;
        Material?[] builtWith = new Material?[3];
        bool builtAuto;
        readonly Lod.LodSettings defaults = new Lod.LodSettings();
        string? shownJson;
        StoreyDocument? preview;
        readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
        bool pendingAll;
        int kindsSeen = -1, detailKindsSeen = -1;

        /// <summary>Set by the editor's Storey tools each frame they are active: the storey being edited, isolate. Null draws the plain layout.</summary>
        public SiteView? View { get => view; set { view = value; if (value == null) ViewSource = null; } }
        SiteView? view;

        /// <summary>
        /// Where the editor's view comes from at the moment the site is built and drawn. The tools set it so the floor clip
        /// is worked out from the layout just built: a view kept from the frame before would clip a storey made shorter
        /// at its old ceiling for a frame, and its ceiling would flash.
        /// </summary>
        public Func<SiteView?>? ViewSource { get; set; }

        /// <summary>Something is still easing in the editor's view (the cutaway's walls): keep redrawing.</summary>
        public bool Animating => site != null && site.Animating;

        /// <summary>
        /// Buildings that always show full detail under the automatic LOD, besides the ones the editor and the occlusion
        /// system keep (being edited, walked in, in the camera's way). A project adds the ones its game needs.
        /// </summary>
        public readonly HashSet<string> keepDetail = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Ask for full detail round a point the view is about to reach (a spawn, a teleport; world space): the buildings
        /// within <paramref name="radius"/> metres of it get their LOD0 built ahead of the rest of the city, after the ones
        /// the view can't do without (docs/PERFORMANCE.md §3.1), for <paramref name="seconds"/>. A spawn or teleport behind a
        /// fade or a loading screen waits for <see cref="DetailReadyAround"/>, and a physics character for
        /// <see cref="CollidersReadyAround"/> too; the play kit's character asks at every spawn.
        /// </summary>
        public void PrefetchAround(Vector3 world, float radius = 8, float seconds = 10) => prefetchAt.Add((world, radius, Time.unscaledTime + seconds));

        /// <summary>
        /// Every building within <paramref name="radius"/> metres of a point (world space) has its LOD0 built: full detail is
        /// up there. True with the fixed LOD, which builds every building's at once.
        /// </summary>
        public bool DetailReadyAround(Vector3 world, float radius = 8)
        {
            if (site == null) return false;
            if (!site.AutoLod) return true;
            var o = transform.position; nearIds.Clear();
            site.BuildingsNear(world.x - o.x, world.z - o.z, radius, nearIds);
            foreach (var id in nearIds) if (!site.HasDetail(id)) return false;
            return true;
        }

        /// <summary>
        /// Every building within <paramref name="radius"/> metres of a point (world space) has its collider up (docs/PLAY.md
        /// §5): a physics character or rigid body put there has floors, stairs and walls to meet. A building's collider comes
        /// with its LOD0, cooked off the main thread a frame or two after it, so a spawn or teleport asks for detail there
        /// first (<see cref="PrefetchAround"/>) and waits for this behind its fade, as for <see cref="DetailReadyAround"/>.
        /// True when the site makes no colliders: there are none to wait for, as for a building with its colliders off
        /// (<see cref="BuildingData.colliders"/>). A Storey Prop with a rigid body waits for its building's by itself
        /// (docs/PROPS.md §2.6).
        /// </summary>
        public bool CollidersReadyAround(Vector3 world, float radius = 8)
        {
            if (site == null) return false;
            if (!site.Colliders) return true;
            if (Loading) return false;   // a building there may not be built yet
            var o = transform.position;
            return site.CollidersUpNear(world.x - o.x, world.z - o.z, radius);
        }
        readonly List<(Vector3 at, float radius, float until)> prefetchAt = new List<(Vector3, float, float)>();
        readonly List<string> nearIds = new List<string>();

        /// <summary>
        /// Hide one building of the layout, or show it again, at run time (docs/PLAY.md §6): for a game's own occlusion, which
        /// switches a building off while the player is behind it. Hidden, the building draws nothing in the colour pass at any
        /// LOD, in its cell's merged massing too, while its shadow is cast as before and its collider stays; shown again, it
        /// draws what it would have. Kept by id, so it holds while the layout loads, through LOD changes, edits and the site
        /// switched off and on, and a building not built yet is hidden as it is built. Nothing is done per frame, and no
        /// garbage is made. <see cref="StoreyBuildingHandle"/> calls it as its GameObject is switched on and off.
        /// </summary>
        public void SetHidden(string buildingId, bool hidden)
        {
            if (hidden ? !hiddenIds.Add(buildingId) : !hiddenIds.Remove(buildingId)) return;
            site?.SetHidden(buildingId, hidden);
        }

        /// <summary>Whether a building is hidden (<see cref="SetHidden"/>).</summary>
        public bool IsHidden(string buildingId) => hiddenIds.Count > 0 && hiddenIds.Contains(buildingId);

        // the buildings hidden, given again to every renderer the site makes; room for a few, so hiding makes no garbage
        readonly HashSet<string> hiddenIds = new HashSet<string>(16, StringComparer.Ordinal);

        /// <summary>The editor's camera (the Scene view's), set by the authoring package; the automatic LOD picks for it outside Play mode.</summary>
        public static Func<Camera?>? EditorCamera { get; set; }

        /// <summary>The automatic LOD's numbers from the last frame (null with the fixed LOD), for a stats overlay.</summary>
        public SiteRenderer.LodStats? LodStats => site != null && site.AutoLod ? site.Stats : (SiteRenderer.LodStats?)null;

        /// <summary>The occlusion system driving this site in Play mode (it registers itself), or null.</summary>
        public StoreyOcclusion? Occlusion { get; internal set; }

        /// <summary>The layout's ground plan in world space (its buildings' bounds), for picking the district a player is in.</summary>
        /// <summary>The site's renderer (null until it has built), for the props manager.</summary>
        internal SiteRenderer? Renderer => site;

        public Lod.Districts.Area? PlanArea
        {
            get
            {
                // a layout still being parsed on a build thread has no area yet: asking for it mustn't parse it here
                var d = preview ?? (layout == null ? null : Application.isPlaying && OverFrames ? layout.Parsed : layout.Document);
                if (d == null || d.buildings.Count == 0) return null;
                var o = transform.position;
                if (!ReferenceEquals(d, areaFor) || d.buildings.Count != areaCount || o != areaAt)
                {
                    double x0 = double.MaxValue, z0 = x0, x1 = double.MinValue, z1 = x1;
                    foreach (var b in d.buildings) { var bb = Generate.Site.BoundsOf(b); x0 = Math.Min(x0, bb.x0); z0 = Math.Min(z0, bb.z0); x1 = Math.Max(x1, bb.x1); z1 = Math.Max(z1, bb.z1); }
                    area = new Lod.Districts.Area(x0 + o.x, z0 + o.z, x1 + o.x, z1 + o.z); areaFor = d; areaCount = d.buildings.Count; areaAt = o;
                }
                return area;
            }
        }
        Lod.Districts.Area area; StoreyDocument? areaFor; int areaCount; Vector3 areaAt;

        /// <summary>The layout as shown: the unsaved edit while there is one, otherwise the asset's.</summary>
        public StoreyDocument? Shown => preview ?? (layout != null ? layout.Document : null);

        /// <summary>
        /// A building's LOD0 shape as shown (its collision segments and wall list), or null when it shows no LOD0 or is
        /// waiting to be built again (it would not match the layout).
        /// </summary>
        public Generate.Lod0Shape? BuiltShape(string id) => site == null || pendingAll || pending.Contains(id) ? null : site.ShapeOf(id);

        /// <summary>The building table's index of a building (for isolate and the active-floor globals); -1 when not shown.</summary>
        public int TableIndexOf(string id) => site?.TableIndexOf(id) ?? -1;

        /// <summary>
        /// Load the layout over several frames when the site starts in Play mode, nearest cells first, rather than all in
        /// the frame it starts (docs/CITY.md §2): its text and its neighbours' are parsed on the build threads first. On by
        /// default, so a district streamed in never holds a frame; turn it off to have every building at once (behind a
        /// loading screen, say), or wait for <see cref="Loading"/> to clear. A site with <see cref="collidersAtLoad"/> loads at
        /// once whatever this says.
        /// </summary>
        public bool LoadOverFrames { get; set; } = true;

        /// <summary>A load in Play mode goes over frames: <see cref="LoadOverFrames"/>, unless every collider is wanted as it loads.</summary>
        bool OverFrames => LoadOverFrames && !(collidersAtLoad && generateColliders);

        /// <summary>The layout is still loading over frames: its text is being parsed, or some of its buildings aren't built yet.</summary>
        public bool Loading => site != null && (parsing || preparing || site.Loading);
        bool parsing, preparing;

        /// <summary>
        /// Show an edited layout that is not saved yet, building again what <paramref name="change"/> names (all of it
        /// when null). Null <paramref name="doc"/> shows the asset again. <paramref name="text"/> is the layout's text:
        /// once the asset holds the same, nothing is rebuilt.
        /// </summary>
        public void Preview(StoreyDocument? doc, string? text, SessionChange? change)
        {
            Preview(doc, text, change, null);
        }

        /// <summary>
        /// <see cref="Preview(StoreyDocument?, string?, SessionChange?)"/>, for an edit read from <paramref name="readFrom"/>:
        /// when that is the text the site shows now, the layout is the same and nothing is built again (selecting the
        /// site starts an edit, and building every building again then was a long pause in Play mode).
        /// </summary>
        public void Preview(StoreyDocument? doc, string? text, SessionChange? change, string? readFrom)
        {
            if (doc != null && change == null && readFrom != null && site != null && preview == null && !pendingAll
                && layout != null && shownJson == layout.Json && readFrom == shownJson)
            {
                preview = doc; shownJson = text;
                return;
            }
            if (doc == null) { preview = null; shownJson = text; return; }
            preview = doc; shownJson = text;
            if (change == null || change.structural) pendingAll = true;
            else foreach (var id in change.rebuild) pending.Add(id);
        }

        /// <summary>
        /// A site going away (deleted, disabled, its scene closed) or coming back (an undo of the delete). The editor saves
        /// an open edit of its layout before it goes, and shows the edit again when it is back, so an undo brings back
        /// the layout as it was, not the file as last saved.
        /// </summary>
        public static event Action<StoreySite>? Disabling, Enabled;

        static readonly HashSet<StoreySite> all = new HashSet<StoreySite>();
        /// <summary>The sites enabled now (the editor redraws when the camera moves for those with the automatic LOD).</summary>
        public static IReadOnlyCollection<StoreySite> All => all;
        /// <summary><see cref="All"/> as the set itself, for a loop every frame (the interface boxes its enumerator).</summary>
        internal static HashSet<StoreySite> AllSet => all;

        void OnEnable() { all.Add(this); Enabled?.Invoke(this); Refresh(); }

        void OnDisable() { all.Remove(this); TearDown(); Hold(false); if (overlay != null) overlay.enabled = false; }

        // the layouts this site uses in Play mode, its own and its neighbours': a layout no site uses lets its parse go
        // (docs/PERFORMANCE.md §6.5)
        readonly List<StoreyDocumentAsset> held = new List<StoreyDocumentAsset>(), holding = new List<StoreyDocumentAsset>();

        /// <summary>Use the layouts the site needs now (none when it shows nothing, or outside Play mode), and release the rest.</summary>
        void Hold(bool showing)
        {
            holding.Clear();
            if (showing && Application.isPlaying && layout != null)
            {
                holding.Add(layout);
                // a neighbour's baked layout is read from its bytes for the edge: its parse isn't needed
                foreach (var n in neighbours) if (n != null && n.layout != null && n.layout.Baked == null && !holding.Contains(n.layout)) holding.Add(n.layout);
            }
            bool same = holding.Count == held.Count;
            for (int i = 0; same && i < held.Count; i++) same = ReferenceEquals(held[i], holding[i]);
            if (same) return;
            foreach (var a in holding) a.Use();   // first: a layout in both is never let go in between
            foreach (var a in held) a.Release();
            held.Clear(); held.AddRange(holding);
        }

        void TearDown() { Disabling?.Invoke(this); site?.Dispose(); site = null; shownJson = null; preview = null; neighboursFor = null; neighboursSeen.Clear(); }

        // the numbers in the Game view: a component of their own while shown, in Play mode (docs/PERFORMANCE.md §3.4)
        StoreyLodStatsOverlay? overlay;

        void LateUpdate()
        {
            bool show = showLodStats && Application.isPlaying;
            if (show && overlay == null) { overlay = gameObject.AddComponent<StoreyLodStatsOverlay>(); overlay.hideFlags = HideFlags.HideAndDontSave; overlay.site = this; }
            if (overlay != null && overlay.enabled != show) overlay.enabled = show;
            Refresh();
        }

        void OnDestroy() { if (overlay != null) Destroy(overlay); }

        /// <summary>Build what is out of date and set this frame's globals. Edit mode calls it on every scene update.</summary>
        public void Refresh()
        {
            using var _t = StoreyTimings.Time("site: refresh (all of the below)");
            Hold(layout != null && opaque != null && glass != null && massing != null);
            if (layout == null || opaque == null || glass == null || massing == null) { if (site != null) TearDown(); return; }
            bool auto = lodMode == LodMode.Automatic;
            if (site != null && (builtWith[0] != opaque || builtWith[1] != glass || builtWith[2] != massing || builtAuto != auto)) TearDown();   // materials or the LOD mode changed
            if (site == null)
            {
                site = new SiteRenderer(transform, opaque, glass, massing, Application.isPlaying ? HideFlags.None : HideFlags.DontSave | HideFlags.NotEditable,
                    auto ? (quality != null ? quality.ToLodSettings() : new Lod.LodSettings()) : null);
                builtWith = new Material?[] { opaque, glass, massing }; builtAuto = auto;
                shownJson = null; pendingAll = true;
                foreach (var id in hiddenIds) site.SetHidden(id, true);   // before any building is built: each is hidden as it is
            }
            site.Layer = gameObject.layer;   // everything the site makes is on its layer
            site.LoadOverFrames = OverFrames;
            // in Play mode, loading over frames, the layouts are parsed on the build threads, all at once, and the site waits
            // for them: a district streamed in isn't held up a frame by its text (docs/CITY.md §2)
            parsing = false;
            if (Application.isPlaying && OverFrames)
            {
                site.StartThreads();
                if (layout.DocumentOffMainThread() == null) parsing = true;
                foreach (var n in neighbours) if (n != null && n.layout != null && n.layout.Baked == null && n.layout.DocumentOffMainThread() == null) parsing = true;
            }
            site.Colliders = generateColliders && (Application.isPlaying || collidersInEditMode);
            // every collider made as the layout is built, before physics steps (docs/PLAY.md §5): a load is at once then
            site.AllColliders = collidersAtLoad && generateColliders && Application.isPlaying;
            // the neighbouring districts, for party walls along the edge: a change builds everything again
            // (the layouts' texts compared as objects: a reimport makes a new one, and nothing is read or hashed)
            bool neighboursChanged = neighboursFor != layout || neighboursSeen.Count != neighbours.Count;
            for (int i = 0; !neighboursChanged && i < neighbours.Count; i++)
            {
                var n = neighbours[i]; var was = neighboursSeen[i];
                neighboursChanged = was.layout != n?.layout || was.offset != (n?.offset ?? default) || !ReferenceEquals(was.text, n?.layout?.Json);
            }
            if (neighboursChanged && !parsing)
            {
                neighboursFor = layout; shownJson = null; pendingAll = true;
                neighboursSeen.Clear(); foreach (var n in neighbours) neighboursSeen.Add((n?.layout, n?.offset ?? default, n?.layout?.Json));
                site.District = layout.name; site.Neighbours.Clear(); site.BakedNeighbours.Clear();
                foreach (var n in neighbours)
                {
                    if (n == null || n.layout == null || n.layout == layout) continue;
                    if (n.layout.Baked is BakedLayout baked) site.BakedNeighbours.Add((baked, n.layout.name, n.offset.x, n.offset.y));   // the edge alone, from the bytes (docs/PERFORMANCE.md §6.3)
                    else site.Neighbours.Add((n.layout.Document.buildings, n.layout.name, n.offset.x, n.offset.y));
                }
            }
            // an artist's window, door or facade detail was added or changed: everything is built again with it
            foreach (var o in openings) if (o != null && Generate.OpeningKinds.Get(o.Id) == null) o.Register();
            foreach (var d in facadeDetails) if (d != null && Generate.DetailKinds.Get(d.Id) == null) d.Register();
            if (Generate.OpeningKinds.Version != kindsSeen || Generate.DetailKinds.Version != detailKindsSeen)
            {
                kindsSeen = Generate.OpeningKinds.Version; detailKindsSeen = Generate.DetailKinds.Version; shownJson = null; pendingAll = true;
            }
            // the camera before a load, so one over frames starts with the cells nearest it
            if (auto && Application.isPlaying && (pendingAll || (preview == null && shownJson != layout.Json))) EyeFor(site);
            preparing = false;
            if (preview == null)
            {
                // in Play mode, loading over frames, the site index and the load plan are made on a build thread first, as the
                // parse was (docs/PERFORMANCE.md §6.2)
                if (shownJson != layout.Json && !parsing)
                {
                    if (!(Application.isPlaying && OverFrames) || site.Prepare(layout.Document)) { site.Show(layout.Document); shownJson = layout.Json; }
                    else preparing = true;
                }
            }
            else if (pendingAll) site.Show(preview);
            else if (pending.Count > 0) site.Rebuild(preview, pending);
            pendingAll = false; pending.Clear();
            site.Lod = displayedLod;
            StoreyGlobals.SetSimpleLighting(quality != null && !quality.specularAndReflections);   // the tier's lighting, with either LOD mode
            site.CullSteadyBackFaces = quality == null || quality.cullBackFacesWhenSteady;   // on without a quality asset, as its default
            if (occApplyFor != Occlusion) { occApplyFor = Occlusion; occApply = Occlusion != null ? Occlusion.Apply : null; }
            site.Occlusion = Application.isPlaying && Occlusion != null && Occlusion.isActiveAndEnabled ? occApply : null;
            var v = ViewSource != null ? (view = ViewSource()) : view;
            if (auto) PickFor(site, v);
            using (StoreyTimings.Time("site: frame")) site.Frame(lodTint, v);
        }

        /// <summary>The automatic LOD's camera: the LOD camera or the main one in Play mode, the Scene view's in the editor.</summary>
        void EyeFor(SiteRenderer s)
        {
            var cam = Application.isPlaying ? (lodCamera != null ? lodCamera : Camera.main) : EditorCamera?.Invoke();
            if (cam != null) s.Eye = SiteRenderer.LodEye.Of(cam);
        }

        /// <summary>The automatic LOD's camera and the buildings kept at full detail, this frame.</summary>
        void PickFor(SiteRenderer s, SiteView? v)
        {
            var settings = s.LodSettings!;
            if (quality != null) quality.ApplyTo(settings);
            else if (settings.PX[0] != defaults.PX[0] || settings.MaxLod0 != defaults.MaxLod0)
            {
                // the quality asset was taken away: back to the defaults
                settings.PX = (double[])defaults.PX.Clone(); settings.Hysteresis = defaults.Hysteresis; settings.Fade = defaults.Fade; settings.FadeDetail = defaults.FadeDetail; settings.Far = defaults.Far;
                settings.MaxLod0 = defaults.MaxLod0; settings.MaxLod1 = defaults.MaxLod1; settings.MaxColliders = defaults.MaxColliders; settings.GlassClear = defaults.GlassClear; settings.WindowsPainted = defaults.WindowsPainted; settings.BudgetMs = defaults.BudgetMs; settings.Threads = defaults.Threads;
                settings.KeepChunkBytes = defaults.KeepChunkBytes; settings.FarOcclusion = defaults.FarOcclusion;
            }
            if (Application.platform == RuntimePlatform.WebGLPlayer) settings.Threads = 0;   // no threads there
            EyeFor(s);
            var f = s.Forced; f.Clear();
            foreach (var id in keepDetail) f.Add(id);
            if (v is SiteView sv)
            {
                if (sv.activeId != null) f.Add(sv.activeId);
                if (sv.focusId != null) f.Add(sv.focusId);
            }
            var core = Application.isPlaying && Occlusion != null && Occlusion.isActiveAndEnabled ? Occlusion.Core : null;
            if (core != null)
            {
                if (core.View.active != null) f.Add(core.View.active.id);
                for (int i = 0; i < core.Active.Count; i++) f.Add(core.Active[i].b.id);
            }
            // LOD0 wanted soon (docs/PERFORMANCE.md §3.1): along the camera's line to the player, where a building not in the
            // way yet comes into it as the camera swings, and round the player, who can walk into one; and round a spawn or
            // teleport asked for. Built after the forced ones and those in view
            var pf = s.Prefetch; pf.Clear();
            var o = transform.position;
            if (core != null && Occlusion!.PlayerAndCamera(out var feet, out var camAt))
            {
                double px = feet.x - o.x, pz = feet.z - o.z, dx = camAt.x - feet.x, dz = camAt.z - feet.z;
                int n = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz) / 8));
                for (int i = 0; i <= n; i++) s.BuildingsNear(px + dx * i / n, pz + dz * i / n, i == 0 ? 8 : 5, pf);
            }
            float now = Time.unscaledTime;
            for (int i = prefetchAt.Count - 1; i >= 0; i--)
            {
                var (at, r, until) = prefetchAt[i];
                if (now > until) { prefetchAt.RemoveAt(i); continue; }
                s.BuildingsNear(at.x - o.x, at.z - o.z, r, pf);
            }
        }
    }
}
