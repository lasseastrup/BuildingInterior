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
        [Tooltip("Tint by LOD, as the prototype's stats card does.")]
        public bool lodTint;
        [Tooltip("Automatic LOD: show its numbers (LOD counts, resident meshes, cells, build time) in the Game view, and in the Scene view while the site is selected.")]
        public bool showLodStats;
        [Tooltip("Give every building a MeshCollider from a collision mesh of its floors, stairs, walls and roofs, for physics characters and raycasts. Storey's own play kit walks without them.")]
        public bool generateColliders = true;
        [Tooltip("Colliders in edit mode too, rebuilt with every edit (raycasts and physics previews in the editor). Off, edits cook nothing.")]
        public bool collidersInEditMode;
        [Tooltip("The artist-made windows and doors the layout's styles use (the editor keeps this list), so builds include them.")]
        public List<StoreyOpening> openings = new List<StoreyOpening>();
        [Tooltip("Districts next to this one (their layouts, and how far their sites are from this one): buildings back to back across the edge share one party wall. The inspector's Find neighbouring districts fills it.")]
        public List<NeighbourDistrict> neighbours = new List<NeighbourDistrict>();

        /// <summary>A district next to this one: its layout, and its site's position less this site's (x, z).</summary>
        [Serializable]
        public sealed class NeighbourDistrict
        {
            public StoreyDocumentAsset? layout;
            public Vector2 offset;
        }

        string neighboursSeen = "";
        readonly List<string?> neighbourTexts = new List<string?>();

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
        int kindsSeen = -1;

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

        /// <summary>The editor's camera (the Scene view's), set by the authoring package; the automatic LOD picks for it outside Play mode.</summary>
        public static Func<Camera?>? EditorCamera { get; set; }

        /// <summary>The automatic LOD's numbers from the last frame (null with the fixed LOD), for a stats overlay.</summary>
        public SiteRenderer.LodStats? LodStats => site != null && site.AutoLod ? site.Stats : (SiteRenderer.LodStats?)null;

        /// <summary>The occlusion system driving this site in Play mode (it registers itself), or null.</summary>
        public StoreyOcclusion? Occlusion { get; internal set; }

        /// <summary>The layout's ground plan in world space (its buildings' bounds), for picking the district a player is in.</summary>
        public Lod.Districts.Area? PlanArea
        {
            get
            {
                var d = Shown; if (d == null || d.buildings.Count == 0) return null;
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

        /// <summary>A building's LOD0 as shown, or null when it is waiting to be built again (it would not match the layout).</summary>
        public Generate.Lod0Result? BuiltLod0(string id) => site == null || pendingAll || pending.Contains(id) ? null : site.Lod0Of(id);

        /// <summary>The building table's index of a building (for isolate and the active-floor globals); -1 when not shown.</summary>
        public int TableIndexOf(string id) => site?.TableIndexOf(id) ?? -1;

        /// <summary>
        /// Show an edited layout that is not saved yet, building again what <paramref name="change"/> names (all of it
        /// when null). Null <paramref name="doc"/> shows the asset again. <paramref name="text"/> is the layout's text:
        /// once the asset holds the same, nothing is rebuilt.
        /// </summary>
        public void Preview(StoreyDocument? doc, string? text, SessionChange? change)
        {
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

        void OnEnable() { all.Add(this); Enabled?.Invoke(this); Refresh(); }

        void OnDisable() { all.Remove(this); TearDown(); }

        void TearDown() { Disabling?.Invoke(this); site?.Dispose(); site = null; shownJson = null; preview = null; neighboursSeen = ""; neighbourTexts.Clear(); }

        void OnGUI()
        {
            if (!showLodStats || LodStats is not SiteRenderer.LodStats st) return;
            GUI.Box(new Rect(10, 10, 400, 76), "");
            GUI.Label(new Rect(18, 14, 390, 72), st.ToString());
        }

        void LateUpdate() => Refresh();

        /// <summary>Build what is out of date and set this frame's globals. Edit mode calls it on every scene update.</summary>
        public void Refresh()
        {
            using var _t = StoreyTimings.Time("site: refresh (all of the below)");
            if (layout == null || opaque == null || glass == null || massing == null) { if (site != null) TearDown(); return; }
            bool auto = lodMode == LodMode.Automatic;
            if (site != null && (builtWith[0] != opaque || builtWith[1] != glass || builtWith[2] != massing || builtAuto != auto)) TearDown();   // materials or the LOD mode changed
            if (site == null)
            {
                site = new SiteRenderer(transform, opaque, glass, massing, Application.isPlaying ? HideFlags.None : HideFlags.DontSave | HideFlags.NotEditable,
                    auto ? (quality != null ? quality.ToLodSettings() : new Lod.LodSettings()) : null);
                builtWith = new Material?[] { opaque, glass, massing }; builtAuto = auto;
                shownJson = null; pendingAll = true;
            }
            site.Layer = gameObject.layer;   // everything the site makes is on its layer
            site.Colliders = generateColliders && (Application.isPlaying || collidersInEditMode);
            // the neighbouring districts, for party walls along the edge: a change builds everything again
            // (the layouts' texts compared as objects: a reimport makes a new one, and nothing is read or hashed)
            string seen = layout.name + "|" + string.Join(";", neighbours.Where(n => n != null && n.layout != null).Select(n => $"{n.layout!.name}@{n.offset.x},{n.offset.y}"));
            bool textsChanged = neighbourTexts.Count != neighbours.Count;
            for (int i = 0; !textsChanged && i < neighbours.Count; i++) textsChanged = !ReferenceEquals(neighbourTexts[i], neighbours[i]?.layout?.Json);
            if (seen != neighboursSeen || textsChanged)
            {
                neighboursSeen = seen; shownJson = null; pendingAll = true;
                neighbourTexts.Clear(); foreach (var n in neighbours) neighbourTexts.Add(n?.layout?.Json);
                site.District = layout.name; site.Neighbours.Clear();
                foreach (var n in neighbours) if (n != null && n.layout != null && n.layout != layout) site.Neighbours.Add((n.layout.Document.buildings, n.layout.name, n.offset.x, n.offset.y));
            }
            // an artist's window or door was added or changed: everything is built again with it
            foreach (var o in openings) if (o != null && Generate.OpeningKinds.Get(o.Id) == null) o.Register();
            if (Generate.OpeningKinds.Version != kindsSeen) { kindsSeen = Generate.OpeningKinds.Version; shownJson = null; pendingAll = true; }
            if (preview == null)
            {
                if (shownJson != layout.Json) { site.Show(layout.Document); shownJson = layout.Json; }
            }
            else if (pendingAll) site.Show(preview);
            else if (pending.Count > 0) site.Rebuild(preview, pending);
            pendingAll = false; pending.Clear();
            site.Lod = displayedLod;
            site.Occlusion = Application.isPlaying && Occlusion != null && Occlusion.isActiveAndEnabled ? Occlusion.Apply : null;
            var v = ViewSource != null ? (view = ViewSource()) : view;
            if (auto) PickFor(site, v);
            using (StoreyTimings.Time("site: frame")) site.Frame(lodTint, v);
        }

        /// <summary>The automatic LOD's camera and the buildings kept at full detail, this frame.</summary>
        void PickFor(SiteRenderer s, SiteView? v)
        {
            var settings = s.LodSettings!;
            if (quality != null) quality.ApplyTo(settings);
            else if (settings.PX[0] != defaults.PX[0] || settings.MaxLod0 != defaults.MaxLod0)
            {
                // the quality asset was taken away: back to the defaults
                settings.PX = (double[])defaults.PX.Clone(); settings.Hysteresis = defaults.Hysteresis; settings.Fade = defaults.Fade; settings.Far = defaults.Far;
                settings.MaxLod0 = defaults.MaxLod0; settings.MaxLod1 = defaults.MaxLod1; settings.BudgetMs = defaults.BudgetMs; settings.Threads = defaults.Threads;
            }
            if (Application.platform == RuntimePlatform.WebGLPlayer) settings.Threads = 0;   // no threads there
            var cam = Application.isPlaying ? (lodCamera != null ? lodCamera : Camera.main) : EditorCamera?.Invoke();
            if (cam != null) s.Eye = SiteRenderer.LodEye.Of(cam);
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
                foreach (var oc in core.Active) f.Add(oc.b.id);
            }
        }
    }
}
