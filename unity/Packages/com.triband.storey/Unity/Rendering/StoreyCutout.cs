#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The hole an artist's window or door fills, on the root of its prefab (docs/OPENINGS.md §3.2): Storey's own
    /// BuildingCutoutBox. The hole's size and middle are in the root's X and Y, so the pivot can be anywhere, and
    /// <see cref="wallFace"/> says where the wall's outer face is along Z. <see cref="leaf"/> lists the objects that are a
    /// door's leaf. Sliced into an opening of another size (docs/OPENINGS.md §4.3), what lies within <see cref="borders"/> of
    /// the hole's sides keeps its size, and <see cref="keepShape"/>'s objects keep their shape. A Storey Opening made from
    /// the prefab reads it when it bakes (docs/EDITOR.md §6.9); nothing reads it at run time, and the prefab doesn't ship.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Storey/Storey Cutout")]
    public sealed class StoreyCutout : MonoBehaviour
    {
        [Tooltip("The hole in the wall: its width and height, in metres.")]
        public Vector2 size = new Vector2(1.2f, 1.6f);
        [Tooltip("The middle of the hole, in this object's X and Y: the pivot can be anywhere.")]
        public Vector2 center = new Vector2(0, 0.8f);
        [Tooltip("Where the wall's outer face is along this object's Z. The wall is 0.3 m thick, behind it; +Z is out of the wall.")]
        public float wallFace;
        [Tooltip("A door's leaf: these objects, with everything under them, are left out where the doorway stays open (walk-in storeys, which the player walks through).")]
        public List<Transform> leaf = new List<Transform>();
        [Tooltip("Sliced into an opening of another size: the slice lines, in from the hole's left, right, bottom and top (x, y, z, w), metres. What lies between them stretches; what lies outside them (a frame's members, as thick as these) keeps its size and moves with the hole's sides.")]
        public Vector4 borders;
        [Tooltip("Objects that keep their shape however the window is fitted (a keystone, a lamp, a house number): each moves with the middle of its bounds, unscaled.")]
        public List<Transform> keepShape = new List<Transform>();

        /// <summary>Set by the editor: fits a cutout just added to the meshes under it.</summary>
        internal static System.Action<StoreyCutout>? Fit;

        void Reset() => Fit?.Invoke(this);

        void OnValidate()
        {
            size = new Vector2(Mathf.Max(0.05f, size.x), Mathf.Max(0.05f, size.y));
            borders = new Vector4(Mathf.Max(0, borders.x), Mathf.Max(0, borders.y), Mathf.Max(0, borders.z), Mathf.Max(0, borders.w));
        }
    }
}
