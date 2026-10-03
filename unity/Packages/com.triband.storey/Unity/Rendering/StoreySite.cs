#nullable enable
using System;
using System.Collections.Generic;
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
        [Tooltip("Which LOD every building shows: 0 full, 1 shell, 2 massing. (The LOD manager replaces this in workstream 7.)")]
        [Range(0, 2)] public int displayedLod = 0;
        [Tooltip("Tint by LOD, as the prototype's stats card does.")]
        public bool lodTint;
        [Tooltip("Give every building a MeshCollider from a collision mesh of its floors, stairs, walls and roofs, for physics characters and raycasts. Storey's own play kit walks without them.")]
        public bool generateColliders = true;
        [Tooltip("Colliders in edit mode too, rebuilt with every edit (raycasts and physics previews in the editor). Off, edits cook nothing.")]
        public bool collidersInEditMode;
        [Tooltip("The artist-made windows and doors the layout's styles use (the editor keeps this list), so builds include them.")]
        public List<StoreyOpening> openings = new List<StoreyOpening>();

        SiteRenderer? site;
        Material?[] builtWith = new Material?[3];
        string? shownJson;
        StoreyDocument? preview;
        readonly HashSet<string> pending = new HashSet<string>(StringComparer.Ordinal);
        bool pendingAll;
        int kindsSeen = -1;

        /// <summary>Set by the editor's Storey tools each frame they are active: the storey being edited, isolate. Null draws the plain layout.</summary>
        public SiteView? View { get; set; }

        /// <summary>Something is still easing in the editor's view (the cutaway's walls): keep redrawing.</summary>
        public bool Animating => site != null && site.Animating;

        /// <summary>The occlusion system driving this site in Play mode (it registers itself), or null.</summary>
        public StoreyOcclusion? Occlusion { get; internal set; }

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

        void OnEnable() { Enabled?.Invoke(this); Refresh(); }

        void OnDisable() { Disabling?.Invoke(this); site?.Dispose(); site = null; shownJson = null; preview = null; }

        void LateUpdate() => Refresh();

        /// <summary>Build what is out of date and set this frame's globals. Edit mode calls it on every scene update.</summary>
        public void Refresh()
        {
            if (layout == null || opaque == null || glass == null || massing == null) { if (site != null) OnDisable(); return; }
            if (site != null && (builtWith[0] != opaque || builtWith[1] != glass || builtWith[2] != massing)) OnDisable();   // materials changed
            if (site == null)
            {
                site = new SiteRenderer(transform, opaque, glass, massing, Application.isPlaying ? HideFlags.None : HideFlags.DontSave | HideFlags.NotEditable);
                builtWith = new Material?[] { opaque, glass, massing };
                shownJson = null; pendingAll = true;
            }
            site.Colliders = generateColliders && (Application.isPlaying || collidersInEditMode);
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
            site.Frame(lodTint, View);
        }
    }
}
