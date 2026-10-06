#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// An object inside a building: furniture, clutter, a rigidbody that may fall down the stairs or out of a window
    /// (docs/PROPS.md). Every renderer under it is drawn as part of the building it's in: its shader (a Shader Graph with
    /// the Storey Prop nodes) reads the building from the renderer's user value, which this keeps up to date, and the
    /// building's occlusion and LOD fade apply to it. A renderer under another Storey Prop belongs to that one.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Storey/Storey Prop")]
    public sealed class StoreyProp : MonoBehaviour
    {
        public enum Motion
        {
            [Tooltip("Moving when it has a Rigidbody (on it or above it), still otherwise")] Auto,
            [Tooltip("Placed once: found its building when enabled (and when moved in the editor)")] Still,
            [Tooltip("Found its building again whenever it has moved 5 cm (a Rigidbody's only while awake)")] Moving,
        }
        public enum Point
        {
            [Tooltip("The middle of its renderers' bounds")] BoundsCentre,
            [Tooltip("Its transform's position")] Pivot,
        }

        [Tooltip("Whether it moves in play: a moving prop is found again as it goes, so it shows and hides with whichever building and storey it's in.")]
        public Motion motion = Motion.Auto;
        [Tooltip("The point that decides which building it's in.")]
        public Point locateBy = Point.BoundsCentre;
        [Tooltip("For a renderer whose shader can't take the Storey Prop nodes: switch the whole prop off when the point is in a storey the occlusion hides (no dither, no squash).")]
        public bool hideWhole;

        /// <summary>The building it's in (null: in the street), and the site; for the inspector.</summary>
        public string? BuildingId => entry?.buildingId;
        public StoreySite? Site => entry?.site;

        internal StoreyProps.Entry? entry;
        readonly List<Renderer> found = new List<Renderer>();

        /// <summary>Its renderers: every MeshRenderer and SkinnedMeshRenderer under it, but those under another Storey Prop.</summary>
        internal Renderer[] CollectRenderers()
        {
            found.Clear();
            Collect(transform, true);
            return found.ToArray();
        }

        void Collect(Transform t, bool self)
        {
            if (!self && t.GetComponent<StoreyProp>() != null) return;
            foreach (var r in t.GetComponents<Renderer>()) if (r is MeshRenderer || r is SkinnedMeshRenderer) found.Add(r);
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), false);
        }

        void OnEnable() => StoreyProps.Register(this);
        void OnDisable() => StoreyProps.Unregister(this);
        void OnTransformChildrenChanged() => StoreyProps.Refresh(this);

        /// <summary>Find its renderers and its building again now (after adding renderers from code, or teleporting it).</summary>
        public void Refresh() => StoreyProps.Refresh(this);
    }
}
